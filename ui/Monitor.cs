using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Wintools {
    internal sealed class ResourceSample {
        internal double? Cpu, Memory, Receive, Send;
        internal ulong TotalMemory, AvailableMemory, Uptime;
        internal string[] Processes = new string[0];
        internal string Error = "";
    }
    internal sealed class ResourceReader {
        [StructLayout(LayoutKind.Sequential)] private sealed class MemoryStatus {
            internal uint Length = 64, Load;
            internal ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
        }
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus memory);
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
        private ulong priorIdle, priorTotal, cpuStamp;
        private bool hasCpu;
        private string priorAdapter;
        private long priorReceived, priorSent, priorStamp;
        internal static double? CpuUsage(ulong idle, ulong total, ulong oldIdle, ulong oldTotal) {
            if(total<=oldTotal || idle<oldIdle || idle-oldIdle>total-oldTotal)return null;
            return 100.0*(1.0-(double)(idle-oldIdle)/(total-oldTotal));
        }
        internal static double? Rate(long value,long previous,double seconds) { return seconds>0 && value>=previous ? (double?)(value-previous)/seconds : null; }
        internal ResourceSample Read(string adapter,bool includeProcesses=true) {
            var sample=new ResourceSample();var errors=new List<string>();ulong idle,kernel,user;ulong now=GetTickCount64();
            if(GetActiveProcessorCount(0xffff)<=64 && GetSystemTimes(out idle,out kernel,out user)) {
                ulong total=kernel+user;if(hasCpu&&now-cpuStamp<=5000)sample.Cpu=CpuUsage(idle,total,priorIdle,priorTotal);priorIdle=idle;priorTotal=total;cpuStamp=now;hasCpu=true;
            } else {hasCpu=false;errors.Add("Общая загрузка CPU недоступна для этой конфигурации.");}
            var memory=new MemoryStatus();if(GlobalMemoryStatusEx(memory) && memory.TotalPhysical>0 && memory.AvailablePhysical<=memory.TotalPhysical) {sample.TotalMemory=memory.TotalPhysical;sample.AvailableMemory=memory.AvailablePhysical;sample.Memory=100.0*(1.0-(double)memory.AvailablePhysical/memory.TotalPhysical);}else errors.Add("Не удалось прочитать память.");
            sample.Uptime=GetTickCount64();
            try {
                var nic=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n=>n.Id==adapter && n.OperationalStatus==OperationalStatus.Up);
                if(nic!=null) {var stats=nic.GetIPv4Statistics();long stamp=Stopwatch.GetTimestamp();if(priorAdapter==adapter){double seconds=(double)(stamp-priorStamp)/Stopwatch.Frequency;if(seconds<=5){sample.Receive=Rate(stats.BytesReceived,priorReceived,seconds);sample.Send=Rate(stats.BytesSent,priorSent,seconds);}}priorAdapter=adapter;priorReceived=stats.BytesReceived;priorSent=stats.BytesSent;priorStamp=stamp;}
                else {priorAdapter=null;errors.Add("Выберите подключённый сетевой адаптер.");}
            } catch(NetworkInformationException) {priorAdapter=null;errors.Add("Сетевой адаптер недоступен. Обновите список.");}
            if(!includeProcesses){sample.Error=string.Join(" ",errors);return sample;}var rows=new List<Tuple<string,long>>();int inaccessible=0;
            foreach(var process in Process.GetProcesses()) {using(process){try{rows.Add(Tuple.Create(process.ProcessName+" · PID "+process.Id,process.WorkingSet64));}catch(InvalidOperationException){inaccessible++;}catch(System.ComponentModel.Win32Exception){inaccessible++;}}}
            sample.Processes=rows.OrderByDescending(r=>r.Item2).Take(8).Select(r=>r.Item1+"   —   "+(r.Item2/1048576.0).ToString("N0")+" МБ").ToArray();
            if(inaccessible>0)errors.Add("Недоступных или завершившихся процессов: "+inaccessible+".");sample.Error=string.Join(" ",errors);return sample;
        }
    }
    internal sealed class ResourceGraph : FrameworkElement {
        private readonly List<double?> values=new List<double?>();
        internal void Push(double? value){values.Add(value);if(values.Count>30)values.RemoveAt(0);InvalidateVisual();}
        internal void Clear(){values.Clear();InvalidateVisual();}
        internal bool Percent;
        protected override void OnRender(DrawingContext dc){
            base.OnRender(dc);double width=ActualWidth,height=ActualHeight;if(width<=0||height<=0)return;var grid=(Brush)FindResource("Border");var accent=(Brush)FindResource("Accent");
            var line=new Pen(grid,1){DashStyle=new DashStyle(new double[]{2,3},0)};for(int i=0;i<=2;i++){double y=Math.Round(i*(height-1)/2)+0.5;dc.DrawLine(line,new Point(0,y),new Point(width,y));}
            double max=Percent?100:Math.Max(1,values.Where(v=>v.HasValue).Select(v=>v.Value).DefaultIfEmpty(1).Max());var stroke=new Pen(accent,2){LineJoin=PenLineJoin.Round,StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};var color=accent is SolidColorBrush?((SolidColorBrush)accent).Color:Colors.SteelBlue;
            var fill=new LinearGradientBrush(Color.FromArgb(70,color.R,color.G,color.B),Color.FromArgb(0,color.R,color.G,color.B),90);
            // Gaps stay gaps: every run of consecutive samples becomes its own filled segment.
            for(int start=0;start<values.Count;){if(!values[start].HasValue){start++;continue;}int end=start;while(end+1<values.Count&&values[end+1].HasValue)end++;
                if(end>start){var area=new StreamGeometry();var path=new StreamGeometry();using(var a=area.Open())using(var p=path.Open()){Point first=Plot(start,max,width,height);a.BeginFigure(new Point(first.X,height),true,true);a.LineTo(first,false,false);p.BeginFigure(first,false,false);for(int k=start+1;k<=end;k++){var point=Plot(k,max,width,height);a.LineTo(point,false,true);p.LineTo(point,true,true);}a.LineTo(new Point(Plot(end,max,width,height).X,height),false,false);}area.Freeze();path.Freeze();dc.DrawGeometry(fill,null,area);dc.DrawGeometry(null,stroke,path);}
                else dc.DrawEllipse(accent,null,Plot(start,max,width,height),2,2);
                start=end+1;}
        }
        private Point Plot(int index,double max,double width,double height){return new Point(index*width/29,height*(1-Math.Max(0,Math.Min(max,values[index].Value))/max));}
    }
    internal sealed partial class MainWindow {
        private readonly ResourceReader resourceReader=new ResourceReader();
        private readonly DispatcherTimer resourceTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        private bool readingResources,resourcesPaused;
        private UniformGrid resourceCards;
        private TextBlock cpuValue,memoryValue,networkValue,memoryDetail,resourceStatus,uptimeValue;
        private ResourceGraph cpuGraph,memoryGraph,networkGraph;
        private ComboBox networkAdapter;
        private ItemsControl resourceProcesses;
        private void InitializeMonitor(StackPanel panel) {
            var controls=new WrapPanel{Margin=new Thickness(0,0,0,12)};panel.Children.Add(controls);
            var pause=new Button{Content="Приостановить показатели",Margin=new Thickness(0,0,10,8)};controls.Children.Add(pause);pause.Click+=(s,e)=>{resourcesPaused=!resourcesPaused;pause.Content=resourcesPaused?"Продолжить показатели":"Приостановить показатели";ResourceVisibility();};
            uptimeValue=Paragraph("Читаем показатели…");uptimeValue.VerticalAlignment=VerticalAlignment.Center;controls.Children.Add(uptimeValue);
            resourceCards=new UniformGrid{Columns=3};panel.Children.Add(resourceCards);
            var cpu=ResourceCard("Процессор",out cpuValue,out cpuGraph);cpuGraph.Percent=true;cpu.Children.Add(Paragraph("Загрузка за интервал · шкала 0–100%"));
            var ram=ResourceCard("Оперативная память",out memoryValue,out memoryGraph);memoryGraph.Percent=true;memoryDetail=Paragraph("Доступная память и общий объём");ram.Children.Add(memoryDetail);
            var network=ResourceCard("Сеть · приём и отправка",out networkValue,out networkGraph);network.Children.Add(Paragraph("График приёма · масштаб по максимуму"));
            InitializeGpu(panel);
            var adapterRow=new WrapPanel{Margin=new Thickness(0,0,0,8)};panel.Children.Add(adapterRow);networkAdapter=new ComboBox{Width=300,DisplayMemberPath="Value",SelectedValuePath="Key",Margin=new Thickness(0,0,10,8)};System.Windows.Automation.AutomationProperties.SetName(networkAdapter,"Сетевой адаптер для мониторинга");adapterRow.Children.Add(networkAdapter);var refresh=new Button{Content="Обновить адаптеры",Margin=new Thickness(0,0,0,8)};adapterRow.Children.Add(refresh);refresh.Click+=(s,e)=>ReadAdapters();networkAdapter.SelectionChanged+=(s,e)=>{networkGraph.Clear();networkValue.Text="Первый замер…";};
            resourceStatus=Paragraph("Показатели обновляются каждые 2 секунды, пока открыт этот раздел. Графики хранят последние 30 замеров.");resourceStatus.FontSize=12;panel.Children.Add(resourceStatus);
            InitializeTemperatures(panel);InitializeMeasurements(panel);var processPanel=new StackPanel();processPanel.Children.Add(Paragraph("Восемь процессов с наибольшей рабочей памятью. Общая память Windows включает также ядро, драйверы и кэш; сумма строк не равна занятой ОЗУ."));resourceProcesses=new ItemsControl();processPanel.Children.Add(resourceProcesses);var expander=new Expander{Header="Что сейчас занимает память",Content=processPanel,IsExpanded=true,Margin=new Thickness(0,0,0,18)};expander.SetResourceReference(Control.ForegroundProperty,"Text");panel.Children.Add(expander);
            ReadAdapters();resourceTimer.Tick+=async(s,e)=>await SampleResources();Window.StateChanged+=(s,e)=>ResourceVisibility();Window.Closed+=(s,e)=>resourceTimer.Stop();panel.SizeChanged+=(s,e)=>resourceCards.Columns=panel.ActualWidth>=1200?4:panel.ActualWidth>=580?2:1;
        }
        private StackPanel ResourceCard(string title,out TextBlock value,out ResourceGraph graph){var panel=new StackPanel();var label=Paragraph(title);label.FontSize=12;label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,0,0,6);label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(label);value=new TextBlock{Text="Первый замер…",MinHeight=64,FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)};panel.Children.Add(value);graph=new ResourceGraph{Height=58,Margin=new Thickness(0,0,0,12)};panel.Children.Add(graph);var border=new Border{Child=panel,CornerRadius=new CornerRadius(12),Padding=new Thickness(16),Margin=new Thickness(0,0,10,12)};Card(border);resourceCards.Children.Add(border);return panel;}
        private void ReadAdapters(){try{var prior=networkAdapter.SelectedValue as string;var adapters=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&n.NetworkInterfaceType!=NetworkInterfaceType.Tunnel).Select(n=>new KeyValuePair<string,string>(n.Id,n.Name)).ToArray();networkAdapter.ItemsSource=adapters;networkAdapter.SelectedValue=prior;if(networkAdapter.SelectedIndex<0&&adapters.Length>0)networkAdapter.SelectedIndex=0;}catch(NetworkInformationException ex){resourceStatus.Text="Не удалось прочитать адаптеры: "+ex.Message;}}
        private void ResourceVisibility(){if(resourceCards==null)return;if(!smoke&&page==6&&!closed&&!resourcesPaused&&Window.WindowState!=WindowState.Minimized){resourceTimer.Start();}else{resourceTimer.Stop();if(resourceStatus!=null)resourceStatus.Text="Мониторинг приостановлен. На экране последний замер.";}}
        private static string ResourceRate(double? value){return !value.HasValue?"—":value.Value>=1048576?(value.Value/1048576).ToString("N1")+" МБ/с":(value.Value/1024).ToString("N1")+" КБ/с";}
        private async Task SampleResources(){
            if(readingResources||closed||resourcesPaused||page!=6||Window.WindowState==WindowState.Minimized)return;readingResources=true;string adapter=networkAdapter.SelectedValue as string;
            try{var sample=await Task.Run(()=>resourceReader.Read(adapter));if(closed||page!=6||resourcesPaused)return;cpuValue.Text=sample.Cpu.HasValue?sample.Cpu.Value.ToString("N0")+" %":"Нет замера";memoryValue.Text=sample.Memory.HasValue?sample.Memory.Value.ToString("N0")+" %":"Недоступно";memoryDetail.Text=sample.Memory.HasValue?"Свободно "+(sample.AvailableMemory/1073741824.0).ToString("N1")+" из "+(sample.TotalMemory/1073741824.0).ToString("N1")+" ГБ":"Не удалось прочитать память";cpuGraph.Push(sample.Cpu);memoryGraph.Push(sample.Memory);if(adapter==networkAdapter.SelectedValue as string){networkValue.Text="↓ "+ResourceRate(sample.Receive)+"\n↑ "+ResourceRate(sample.Send);networkGraph.Push(sample.Receive);}var uptime=TimeSpan.FromMilliseconds(sample.Uptime);uptimeValue.Text="Windows работает: "+(int)uptime.TotalDays+" д. "+uptime.Hours+" ч. "+uptime.Minutes+" мин.";resourceProcesses.ItemsSource=sample.Processes;resourceStatus.Text="Замер в "+DateTime.Now.ToString("HH:mm:ss")+" · Обновление каждые 2 с · Последние 30 замеров. "+sample.Error;}
            catch(Exception ex){if(!closed){cpuValue.Text=memoryValue.Text=networkValue.Text="Недоступно";cpuGraph.Push(null);memoryGraph.Push(null);networkGraph.Push(null);resourceStatus.Text="Не удалось обновить показатели: "+ex.Message;}}
            finally{readingResources=false;}
        }
    }
}
