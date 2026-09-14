using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task TemperatureSmoke(){
            Assert(Marshal.SizeOf(typeof(GpuTemperatures.Reading))==12&&GpuTemperatures.ValidTemperature(-5)==-5&&GpuTemperatures.ValidTemperature(201)==null,"Temperature structure or validation incorrect");
            var original=temperatureRead;var late=new TaskCompletionSource<TemperatureSnapshot>();
            try{
                ShowPage(6);resourceTimer.Stop();while(readingTemperature)await Task.Delay(20);temperatureExpander.IsExpanded=true;while(readingTemperature)await Task.Delay(20);await SampleTemperatures();Window.Width=1280;Window.Height=800;Window.UpdateLayout();temperatureExpander.BringIntoView();Capture("portable-ui-temperature-native.png");
                temperatureRead=()=>new TemperatureSnapshot{Rows=new[]{new TemperatureRow{Name="GPU 1 · Тестовая видеокарта",Celsius=55},new TemperatureRow{Name="GPU 2 · Другая видеокарта",Error="Датчик недоступен"}}};await SampleTemperatures();Assert(temperatureRows.Children.Count==2&&((TextBlock)temperatureRows.Children[0]).Text.Contains("55 °C")&&!((TextBlock)temperatureRows.Children[1]).Text.Contains("0 °C"),"Temperature rows mixed or unknown shown as zero");
                Window.Width=800;Window.Height=600;Window.UpdateLayout();temperatureExpander.BringIntoView();Capture("portable-ui-temperature-compact.png");
                resourcesPaused=true;temperatureRead=()=>{throw new Exception("Paused temperature queried");};await SampleTemperatures();Assert(temperatureRows.Children.Count==2,"Paused temperature changed");resourcesPaused=false;
                temperatureRead=()=>late.Task.GetAwaiter().GetResult();var pending=SampleTemperatures();ShowPage(0);late.SetResult(new TemperatureSnapshot{Error="stale"});await pending;Assert(temperatureRows.Children.Count==2&&!temperatureStatus.Text.Contains("stale"),"Hidden temperature accepted stale result");
                ShowPage(6);resourceTimer.Stop();temperatureRead=()=>{throw new InvalidOperationException("fixture unavailable");};await SampleTemperatures();Assert(temperatureRows.Children.Count==0&&temperatureStatus.Text.Contains("fixture unavailable"),"Temperature failure kept old readings");
            }finally{late.TrySetResult(new TemperatureSnapshot());temperatureRead=original;resourcesPaused=false;temperatureExpander.IsExpanded=false;ShowPage(0);}
        }
    }
}
