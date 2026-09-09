using System;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task MonitorSmoke(){
            Assert(Math.Abs(ResourceReader.CpuUsage(150,400,100,200).Value-75)<0.01,"CPU idle accounting is incorrect");
            Assert(ResourceReader.CpuUsage(200,100,0,0)==null,"Impossible CPU counters accepted");
            Assert(ResourceReader.Rate(3000,1000,2)==1000,"Network rate is incorrect");
            Assert(ResourceReader.Rate(10,100,2)==null && ResourceReader.Rate(100,0,0)==null,"Counter reset or zero interval accepted");
            ShowPage(6);await SampleResources();await Task.Delay(100);await SampleResources();
            Assert(memoryValue.Text!="Недоступно" && resourceProcesses.Items.Count>0,"Resource monitor did not read Windows");
            resourcesPaused=true;await SampleResources();ResourceVisibility();Assert(!resourceTimer.IsEnabled,"Paused monitoring kept its timer");resourcesPaused=false;
            Window.Width=1280;Window.Height=800;Window.UpdateLayout();Get<ScrollViewer>("HealthPage").ScrollToTop();await Task.Delay(100);await SampleResources();Capture("portable-ui-monitor.png");ShowPage(0);
        }
    }
}
