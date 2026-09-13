using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private readonly GpuReader gpuReader=new GpuReader();
        private TextBlock gpuValue,gpuMemory,gpuStatus;
        private ResourceGraph gpuGraph;
        private ComboBox gpuAdapter;
        private bool readingGpu,readingGpuAdapters;
        private Func<GpuAdapter[]> gpuInventory=GpuInventory.Read;
        private Func<GpuSample> gpuRead;
        private void InitializeGpu(StackPanel panel){
            gpuRead=gpuReader.Read;var card=ResourceCard("Видеокарта",out gpuValue,out gpuGraph);gpuGraph.Percent=true;gpuMemory=Paragraph("Выберите GPU ниже");card.Children.Add(gpuMemory);
            var controls=new WrapPanel{Margin=new Thickness(0,0,0,8)};panel.Children.Add(controls);gpuAdapter=new ComboBox{Width=300,DisplayMemberPath="Name",Margin=new Thickness(0,0,10,8)};System.Windows.Automation.AutomationProperties.SetName(gpuAdapter,"Видеокарта для мониторинга");controls.Children.Add(gpuAdapter);var refresh=new Button{Content="Обновить видеокарты",Margin=new Thickness(0,0,0,8)};controls.Children.Add(refresh);refresh.Click+=async(s,e)=>await ReadGpuAdapters();
            gpuStatus=Paragraph("Нагрузка самого занятого блока выбранного GPU. Объём видеопамяти сообщает DXGI; общая память — доступный предел ОЗУ, а не дополнительно установленная видеопамять.");panel.Children.Add(gpuStatus);
            gpuAdapter.SelectionChanged+=(s,e)=>{gpuGraph.Clear();gpuValue.Text="Первый замер…";var row=gpuAdapter.SelectedItem as GpuAdapter;gpuMemory.Text=row==null?"Видеоадаптер не выбран":"Выделенная видеопамять: "+(row.Dedicated/1073741824.0).ToString("N1")+" ГиБ\nОбщая ОЗУ — предел: "+(row.SharedLimit/1073741824.0).ToString("N1")+" ГиБ";};
            resourceTimer.Tick+=async(s,e)=>await SampleGpu();panel.IsVisibleChanged+=async(s,e)=>{if(panel.IsVisible&&gpuAdapter.Items.Count==0)await ReadGpuAdapters();};
        }
        private async Task ReadGpuAdapters(){
            if(readingGpuAdapters||closed)return;readingGpuAdapters=true;var prior=gpuAdapter.SelectedItem as GpuAdapter;
            try{var rows=await Task.Run(()=>gpuInventory());if(closed)return;gpuAdapter.ItemsSource=rows;gpuAdapter.SelectedItem=prior==null?null:rows.FirstOrDefault(r=>r.Id==prior.Id);if(gpuAdapter.SelectedIndex<0&&rows.Length>0)gpuAdapter.SelectedIndex=0;gpuStatus.Text=rows.Length==0?"Аппаратные видеоадаптеры DXGI не найдены. В удалённой сессии или виртуальной машине данные могут быть недоступны.":"Видеокарт: "+rows.Length+". Показывается нагрузка самого занятого блока GPU; нагрузки разных блоков не складываются.";}
            catch(Exception ex){gpuAdapter.ItemsSource=new GpuAdapter[0];gpuValue.Text="Недоступно";gpuStatus.Text="Не удалось прочитать видеокарты: "+ex.Message;}finally{readingGpuAdapters=false;}
        }
        private async Task SampleGpu(){
            if(readingGpu||closed||resourcesPaused||page!=6||Window.WindowState==WindowState.Minimized)return;var selected=gpuAdapter.SelectedItem as GpuAdapter;if(selected==null)return;readingGpu=true;
            try{var sample=await Task.Run(()=>gpuRead());if(closed||page!=6||resourcesPaused||Window.WindowState==WindowState.Minimized||gpuAdapter.SelectedItem!=selected)return;double value;bool known=sample.Usage.TryGetValue(selected.Id,out value);gpuValue.Text=known?value.ToString("N0")+" %":"Нет замера";gpuGraph.Push(known?(double?)value:null);gpuStatus.Text="GPU · "+DateTime.Now.ToString("HH:mm:ss")+" · Самый занятый блок. "+(known?sample.Error:string.IsNullOrEmpty(sample.Error)?"Счётчики выбранной видеокарты недоступны.":sample.Error);}
            catch(Exception ex){if(!closed&&page==6&&!resourcesPaused&&gpuAdapter.SelectedItem==selected){gpuValue.Text="Недоступно";gpuGraph.Push(null);gpuStatus.Text=ex.Message;}}finally{readingGpu=false;}
        }
    }
}
