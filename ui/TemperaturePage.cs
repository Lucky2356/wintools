using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private Expander temperatureExpander;
        private StackPanel temperatureRows;
        private TextBlock temperatureStatus;
        private bool readingTemperature;
        private Func<TemperatureSnapshot> temperatureRead=GpuTemperatures.Read;
        private void InitializeTemperatures(StackPanel parent){
            var panel=new StackPanel();temperatureStatus=Paragraph("Температура кристалла GPU из установленного драйвера NVIDIA. CPU, AMD и Intel здесь пока не поддерживаются. Обновление каждые 2 с, пока этот блок открыт.");panel.Children.Add(temperatureStatus);temperatureRows=new StackPanel();panel.Children.Add(temperatureRows);
            temperatureExpander=new Expander{Header="Температуры видеокарт · NVIDIA",Content=panel,Margin=new Thickness(0,0,0,16)};temperatureExpander.SetResourceReference(Control.ForegroundProperty,"Text");parent.Children.Add(temperatureExpander);temperatureExpander.Expanded+=async(s,e)=>await SampleTemperatures();resourceTimer.Tick+=async(s,e)=>await SampleTemperatures();
        }
        private async Task SampleTemperatures(){
            if(readingTemperature||closed||resourcesPaused||page!=6||!temperatureExpander.IsExpanded||Window.WindowState==WindowState.Minimized)return;readingTemperature=true;
            try{var read=temperatureRead;var sample=await Task.Run(()=>read());if(closed||resourcesPaused||page!=6||!temperatureExpander.IsExpanded||Window.WindowState==WindowState.Minimized)return;
                temperatureRows.Children.Clear();foreach(var row in sample.Rows){var text=Paragraph(row.Name+" · "+(row.Celsius.HasValue?row.Celsius.Value+" °C":row.Error));text.FontSize=16;text.Margin=new Thickness(0,6,0,8);temperatureRows.Children.Add(text);}
                temperatureStatus.Text="Замер "+DateTime.Now.ToString("HH:mm:ss")+" · Кристалл GPU, источник — драйвер NVIDIA. "+sample.Error+" CPU, AMD и Intel пока не поддерживаются. Пауза показателей останавливает обновление.";
            }catch(Exception ex){if(!closed&&page==6&&temperatureExpander.IsExpanded&&!resourcesPaused){temperatureRows.Children.Clear();temperatureStatus.Text="Температуры недоступны: "+ex.Message;}}
            finally{readingTemperature=false;}
        }
    }
}
