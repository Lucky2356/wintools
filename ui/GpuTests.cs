using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task GpuSmoke(){
            const string id="0x00000000_0x00001234";
            var values=new Dictionary<string,double>{{"pid_1_luid_"+id+"_phys_0_eng_0_engtype_3D",35},{"pid_2_luid_"+id+"_phys_0_eng_0_engtype_3D",40},{"pid_1_luid_"+id+"_phys_0_eng_1_engtype_Copy",60},{"invalid",100},{"pid_3_luid_"+id+"_phys_0_eng_2_engtype_3D",double.NaN}};
            Assert(GpuReader.Aggregate(values)[id]==75,"GPU engines summed instead of busiest engine, or process usage lost");
            var originalInventory=gpuInventory;var originalRead=gpuRead;
            try{
                ShowPage(6);resourceTimer.Stop();while(readingGpu||readingGpuAdapters)await Task.Delay(50);
                gpuInventory=()=>new[]{new GpuAdapter{Name="Тестовая видеокарта · 24 ГиБ",Id=id,Dedicated=25769803776UL,SharedLimit=34359738368UL}};
                await ReadGpuAdapters();gpuRead=()=>new GpuSample{Usage=GpuReader.Aggregate(values)};await SampleGpu();
                Assert(gpuValue.Text.Contains("75")&&gpuMemory.Text.Contains("24"),"GPU load or 64-bit memory not rendered");
                Window.Width=1600;Window.Height=1000;Window.UpdateLayout();Get<ScrollViewer>("HealthPage").ScrollToTop();Capture("portable-ui-gpu.png");
                Window.Width=800;Window.Height=600;Window.UpdateLayout();Assert(resourceCards.Columns==1,"GPU compact cards not stacked");Capture("portable-ui-gpu-compact.png");
                gpuRead=()=>new GpuSample{Error="fixture unavailable"};await SampleGpu();Assert(gpuValue.Text=="Нет замера"&&gpuStatus.Text.Contains("fixture unavailable"),"Missing GPU data presented as zero");
                resourcesPaused=true;gpuRead=()=>{throw new Exception("Paused GPU queried");};await SampleGpu();Assert(gpuValue.Text=="Нет замера","Paused GPU changed");resourcesPaused=false;
            }finally{gpuInventory=originalInventory;gpuRead=originalRead;resourcesPaused=false;}
            await ReadGpuAdapters();await SampleGpu();await Task.Delay(1100);await SampleGpu();Window.Width=1600;Window.Height=1000;Window.UpdateLayout();Capture("portable-ui-gpu-native.png");ShowPage(0);
        }
    }
}
