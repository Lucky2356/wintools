using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task HardwareSmoke() {
            Assert(HardwareReader.Size(68719476736UL).Contains("64"),"Hardware capacity truncated to 32 bits");
            Assert(HardwareReader.Size(null)=="нет данных"&&HardwareReader.Positive(0," MHz")=="нет данных","Missing hardware value reported as zero");
            Assert(HardwareReader.Value(" To be filled by O.E.M. ")=="нет данных","Firmware placeholder displayed as model");
            var partial=HardwareReader.Section("Память","fixture",r=>HardwareReader.Value(r["Name"]),q=>HardwarePartialFixture());
            Assert(partial.Unavailable&&partial.Text.Contains("Test module")&&partial.Text.Contains("fixture unavailable"),"Partial hardware enumeration lost good rows or failure");
            var empty=HardwareReader.Section("Диски","fixture",r=>"",q=>new Dictionary<string,object>[0]);Assert(empty.Unavailable,"Empty inventory presented as complete");
            var unknown=HardwareReader.Section("BIOS","fixture",r=>HardwareReader.Value(r["Version"]),q=>new[]{new Dictionary<string,object>{{"Version",null}}});Assert(unknown.Unavailable,"Missing field not counted as incomplete hardware section");
            var original=hardwareRead;
            try{
                var pending=new TaskCompletionSource<HardwareSection[]>();int reads=0;hardwareRead=()=>{reads++;return pending.Task;};
                ShowPage(6);hardwareExpander.IsExpanded=true;await ReadHardware();Assert(reads==1&&!hardwareRefresh.IsEnabled&&!hardwareExport.IsEnabled,"Hardware queries overlap or export stale snapshot during refresh");
                var fixture=Enumerable.Range(0,6).Select(i=>new HardwareSection{Title="Оборудование "+i,Text=i==0?partial.Text:"Модель устройства · Драйвер 32.0\nЁмкость: 64 ГиБ",Unavailable=i==0}).ToArray();
                pending.SetResult(fixture);await Task.Delay(100);Assert(hardwareSnapshot==fixture&&hardwareCards.Children.Cast<StackPanel>().Sum(c=>c.Children.Count)==6&&hardwareExport.IsEnabled,"Hardware snapshot not rendered");
                Assert(HardwareReader.Report(fixture,DateTime.Now).Contains("Test module"),"Hardware export omits partial data");
                foreach(var size in new[]{new Size(1600,1000),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;((ScrollViewer)Get<FrameworkElement>("HealthPage")).ScrollToTop();Window.UpdateLayout();await Task.Delay(50);Assert(hardwareCards.ActualWidth>250&&hardwareCards.ActualWidth<=Get<FrameworkElement>("HealthPage").ActualWidth,"Hardware cards overflow viewport");if(size.Width==800)Assert(hardwareColumns==1,"Hardware compact layout not stacked");Capture(size.Width==800?"portable-ui-hardware-compact.png":"portable-ui-hardware.png");}
                hardwareRead=()=>{throw new InvalidOperationException("fixture read failure");};await ReadHardware();Assert(hardwareSnapshot==fixture&&hardwareStatus.Text.Contains("предыдущий снимок")&&hardwareExport.IsEnabled,"Failed hardware refresh loses snapshot or hides staleness");
            }finally{hardwareRead=original;}
            await ReadHardware();Assert(hardwareSnapshot.Length==6,"Native hardware inventory incomplete");Window.Width=1600;Window.Height=1000;Window.UpdateLayout();Capture("portable-ui-hardware-native.png");hardwareExpander.IsExpanded=false;ShowPage(0);
        }
        private static IEnumerable<IDictionary<string,object>> HardwarePartialFixture(){yield return new Dictionary<string,object>{{"Name","Test module"}};throw new InvalidOperationException("fixture unavailable");}
    }
}
