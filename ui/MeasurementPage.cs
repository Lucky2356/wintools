using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Wintools {
    internal sealed partial class MainWindow {
        private MeasurementSession measurement;
        private readonly Stopwatch measurementClock=new Stopwatch();
        private readonly DispatcherTimer measurementTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        private bool readingMeasurement;
        private TextBox measurementName;
        private TextBlock measurementStatus,measurementReport;
        private Grid measurementTable;
        private ComboBox measurementFirst,measurementSecond;
        private Button measurementStart,measurementStop;
        private Expander measurementExpander;
        private string measurementWindowTitle;
        private Func<string,string,Task<MeasurementPoint>> measurementRead;
        private void InitializeMeasurements(StackPanel parent){
            var panel=new StackPanel();panel.Children.Add(Paragraph("Запишите до 10 минут работы, затем повторите ту же задачу после изменений. Запись продолжается в других разделах и при свёрнутом окне. Пауза обычного монитора её не останавливает. Выбранные сейчас GPU и сетевой адаптер закрепляются на всю запись. Настройки Windows не меняются."));
            var controls=new WrapPanel();panel.Children.Add(controls);measurementName=new TextBox{Text="До изменений",Width=220,MaxLength=80,Margin=new Thickness(0,0,10,8)};System.Windows.Automation.AutomationProperties.SetName(measurementName,"Название записи измерений");controls.Children.Add(measurementName);
            measurementStart=new Button{Content="Начать запись",Margin=new Thickness(0,0,10,8)};controls.Children.Add(measurementStart);measurementStart.Click+=async(s,e)=>await StartMeasurement();measurementStop=new Button{Content="Остановить и сохранить",IsEnabled=false,Margin=new Thickness(0,0,0,8)};controls.Children.Add(measurementStop);measurementStop.Click+=(s,e)=>StopMeasurement();
            measurementStatus=Paragraph("Записи хранятся в WintoolsData/measurements. Каждый полученный замер сохраняется на диск; после сбоя доступны уже сохранённые данные.");panel.Children.Add(measurementStatus);
            var selectors=new WrapPanel();panel.Children.Add(selectors);measurementFirst=new ComboBox{Width=300,DisplayMemberPath="Label",Margin=new Thickness(0,0,10,8)};measurementSecond=new ComboBox{Width=300,DisplayMemberPath="Label",Margin=new Thickness(0,0,0,8)};System.Windows.Automation.AutomationProperties.SetName(measurementFirst,"Первая запись");System.Windows.Automation.AutomationProperties.SetName(measurementSecond,"Вторая запись для сравнения");selectors.Children.Add(measurementFirst);selectors.Children.Add(measurementSecond);measurementFirst.SelectionChanged+=(s,e)=>RenderMeasurements();measurementSecond.SelectionChanged+=(s,e)=>RenderMeasurements();
            var actions=new WrapPanel();panel.Children.Add(actions);var refresh=new Button{Content="Обновить записи",Margin=new Thickness(0,0,10,8)};actions.Children.Add(refresh);refresh.Click+=(s,e)=>ReadMeasurements();var export=new Button{Content="Сохранить первую запись в CSV…",Margin=new Thickness(0,0,0,8)};actions.Children.Add(export);export.Click+=(s,e)=>ExportMeasurements();
            measurementTable=new Grid{Margin=new Thickness(0,4,0,12)};for(int i=0;i<4;i++)measurementTable.ColumnDefinitions.Add(new ColumnDefinition());panel.Children.Add(measurementTable);
            measurementReport=Paragraph("Записей пока нет. Начните измерение выше.");panel.Children.Add(measurementReport);measurementExpander=new Expander{Header="Запись нагрузки и сравнение замеров",Content=panel,Margin=new Thickness(0,0,0,18)};measurementExpander.SetResourceReference(Control.ForegroundProperty,"Text");parent.Children.Add(measurementExpander);measurementExpander.Expanded+=(s,e)=>ReadMeasurements();
            measurementTimer.Tick+=async(s,e)=>await SampleMeasurement();Window.Closed+=(s,e)=>StopMeasurement();
        }
        private async Task StartMeasurement(){
            if(measurement!=null||closed)return;var name=measurementName.Text.Trim();if(name.Length==0){measurementStatus.Text="Введите название, например «До изменений» или название игры.";return;}
            var gpu=gpuAdapter.SelectedItem as GpuAdapter;var network=networkAdapter.SelectedItem;string networkId=networkAdapter.SelectedValue as string;
            var session=new MeasurementSession{Id=Guid.NewGuid().ToString("N"),StartedUtc=DateTime.UtcNow.ToString("o"),Name=name,Status="recording",GpuId=gpu==null?"":gpu.Id,GpuName=gpu==null?"не выбран":gpu.Name,NetworkId=networkId??"",NetworkName=network==null?"не выбран":((System.Collections.Generic.KeyValuePair<string,string>)network).Value};
            try{Measurements.Save(session);}catch(Exception ex){measurementStatus.Text="Запись не началась: "+ex.Message;return;}
            var resourceReader=new ResourceReader();var gpuReader=new GpuReader();
            measurementRead=async(adapter,gpuId)=>await Task.Run(()=>{
                var point=new MeasurementPoint{Error=""};try{var resources=resourceReader.Read(adapter,false);point.Cpu=resources.Cpu;point.Memory=resources.Memory;point.Receive=resources.Receive;point.Send=resources.Send;point.Error=resources.Error;}catch(Exception ex){point.Error="CPU/ОЗУ/сеть: "+ex.Message;}
                if(gpuId.Length>0){var sample=gpuReader.Read();double load;if(sample.Usage.TryGetValue(gpuId,out load))point.Gpu=load;else point.Error+=" GPU: "+sample.Error;}return point;
            });
            measurement=session;measurementWindowTitle=Window.Title;Window.Title=measurementWindowTitle+" · Идёт запись нагрузки";measurementClock.Restart();measurementStart.IsEnabled=false;measurementName.IsEnabled=false;measurementStop.IsEnabled=true;measurementTimer.Start();await SampleMeasurement();
        }
        private async Task SampleMeasurement(){
            if(measurement==null||readingMeasurement)return;var session=measurement;if(measurementClock.Elapsed.TotalSeconds>=600||session.Points.Count>=Measurements.Limit){StopMeasurement();return;}readingMeasurement=true;
            try{var point=await measurementRead(session.NetworkId,session.GpuId);if(measurement!=session)return;if(measurementClock.Elapsed.TotalSeconds>=600){StopMeasurement();return;}point.Seconds=measurementClock.Elapsed.TotalSeconds;point.Error=point.Error??"";if(point.Error.Length>4096)point.Error=point.Error.Substring(0,4096);session.Points.Add(point);Measurements.Save(session);measurementStatus.Text="Идёт запись «"+session.Name+"» · "+session.Points.Count+" замеров · "+measurementClock.Elapsed.ToString(@"mm\:ss")+" из 10:00. GPU: "+session.GpuName+" · Сеть: "+session.NetworkName+". Можно свернуть приложение.";}
            catch(Exception ex){StopMeasurement(false);measurementStatus.Text="Запись остановлена из-за ошибки: "+ex.Message+". В списке доступны сохранённые замеры.";}finally{readingMeasurement=false;}
        }
        private void StopMeasurement(bool complete=true){
            measurementTimer.Stop();if(measurement==null)return;var session=measurement;measurement=null;measurementClock.Stop();Window.Title=measurementWindowTitle;measurementStart.IsEnabled=true;measurementName.IsEnabled=true;measurementStop.IsEnabled=false;session.Status=complete?"complete":"interrupted";
            try{Measurements.Save(session);measurementStatus.Text="Запись «"+session.Name+"» сохранена: "+session.Points.Count+" замеров. Выберите две записи ниже для сравнения.";}catch(Exception ex){measurementStatus.Text="Не удалось сохранить завершение записи: "+ex.Message+". Предыдущие сохранённые замеры остаются на диске.";}
            if(!closed)ReadMeasurements();
        }
        private void ReadMeasurements(){
            try{int errors;var rows=Measurements.History(out errors);var first=measurementFirst.SelectedItem as MeasurementSession;var second=measurementSecond.SelectedItem as MeasurementSession;measurementFirst.ItemsSource=rows;measurementSecond.ItemsSource=rows;measurementFirst.SelectedItem=rows.FirstOrDefault(r=>first!=null&&r.Id==first.Id)??rows.FirstOrDefault();measurementSecond.SelectedItem=rows.FirstOrDefault(r=>second!=null&&r.Id==second.Id);RenderMeasurements();if(errors>0)measurementStatus.Text="Не удалось прочитать записей: "+errors+". Остальные доступны. Показаны последние 50 файлов.";}
            catch(Exception ex){measurementStatus.Text="Не удалось прочитать записи: "+ex.Message;}
        }
        private void RenderMeasurements(){
            if(measurementReport==null)return;measurementTable.Children.Clear();measurementTable.RowDefinitions.Clear();var first=measurementFirst.SelectedItem as MeasurementSession;var second=measurementSecond.SelectedItem as MeasurementSession;if(first==null){measurementReport.Text="Записей пока нет. Начните измерение выше.";return;}
            bool same=second!=null&&first.Id==second.Id;if(same)second=null;
            string[] titles={"Показатель","Первая запись","Вторая запись","Разница средних"};for(int r=0;r<6;r++)measurementTable.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});for(int c=0;c<4;c++)MeasurementCell(0,c,titles[c],true);
            Func<MeasurementPoint,double?>[] metrics={p=>p.Cpu,p=>p.Memory,p=>p.Gpu,p=>p.Receive/1048576,p=>p.Send/1048576};string[] names={"CPU","Занятая ОЗУ","GPU","Приём сети","Отправка сети"};
            for(int i=0;i<5;i++){var a=first.Points.Select(metrics[i]).ToArray();var b=second==null?new double?[0]:second.Points.Select(metrics[i]).ToArray();bool compatible=second!=null&&(i<2||(i==2?first.GpuId==second.GpuId&&first.GpuName==second.GpuName:first.NetworkId==second.NetworkId));MeasurementCell(i+1,0,names[i],true);MeasurementCell(i+1,1,MeasurementStatistic(a,i<3?"%":"МиБ/с"),false);MeasurementCell(i+1,2,second==null?"—":MeasurementStatistic(b,i<3?"%":"МиБ/с"),false);MeasurementCell(i+1,3,second==null?"—":compatible?Measurements.Difference("",a,b,i<3?"п.п.":"МиБ/с"):"Другой адаптер",false);}
            var info=new StringBuilder();info.AppendLine("Первая: "+MeasurementCaption(first));if(second!=null)info.AppendLine("Вторая: "+MeasurementCaption(second));info.AppendLine(second==null?(same?"Выберите другую вторую запись.":"Выберите вторую запись для сравнения."):"Разница средних: вторая запись минус первая. Для процентов разница показана в процентных пунктах (п.п.).");info.AppendLine("Средние и максимумы рассчитаны по доступным замерам. Повторяйте одинаковую задачу: фоновые программы, длительность и пропуски влияют на результат. Это не измерение FPS.");foreach(var error in first.Points.Concat(second==null?new MeasurementPoint[0]:second.Points.ToArray()).Select(p=>p.Error).Where(e=>e.Length>0).Distinct().Take(2))info.AppendLine("Неполные данные: "+error);measurementReport.Text=info.ToString();
        }
        private static string MeasurementStatistic(double?[] all,string unit){var values=all.Where(v=>v.HasValue).Select(v=>v.Value).ToArray();return values.Length==0?"Нет замеров":"Среднее "+values.Average().ToString("N1")+" "+unit+"\nМакс. "+values.Max().ToString("N1")+" · "+values.Length+" из "+all.Length;}
        private static string MeasurementCaption(MeasurementSession session){return session.Label+" · "+(session.Points.Count==0?0:session.Points.Last().Seconds).ToString("N0")+" с · "+(session.Status=="complete"?"завершена":"незавершённая запись")+"\nGPU: "+session.GpuName+" · Сеть: "+session.NetworkName;}
        private void MeasurementCell(int row,int column,string value,bool bold){var text=Paragraph(value);text.Margin=new Thickness(8);text.FontSize=12;if(bold)text.FontWeight=FontWeights.SemiBold;var cell=new Border{Child=text,Margin=new Thickness(0,0,2,2)};cell.SetResourceReference(Border.BackgroundProperty,row==0?"Selection":"Surface");Grid.SetRow(cell,row);Grid.SetColumn(cell,column);measurementTable.Children.Add(cell);}
        private void ExportMeasurements(){
            var session=measurementFirst.SelectedItem as MeasurementSession;if(session==null){measurementStatus.Text="Выберите первую запись для экспорта.";return;}
            var dialog=new Microsoft.Win32.SaveFileDialog{Title="Сохранить замеры",Filter="CSV (*.csv)|*.csv",DefaultExt=".csv",FileName="Wintools-measurements-"+session.Id+".csv"};if(dialog.ShowDialog(Window)!=true)return;
            try{File.WriteAllText(dialog.FileName,Measurements.Csv(session),new UTF8Encoding(true));measurementStatus.Text="CSV сохранён. Пустые ячейки означают отсутствие замера, а не нулевую нагрузку.";}catch(Exception ex){measurementStatus.Text="Не удалось сохранить CSV: "+ex.Message;}
        }
    }
}
