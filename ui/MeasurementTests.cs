using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task MeasurementSmoke(){
            ShowPage(6);resourceTimer.Stop();measurementExpander.IsExpanded=true;measurementName.Text="Тест: до изменений";await StartMeasurement();measurementTimer.Stop();Assert(measurement!=null,"Measurement recording did not start");
            var first=measurement;measurementRead=(network,gpu)=>Task.FromResult(new MeasurementPoint{Cpu=20,Memory=50,Gpu=null,Receive=1048576,Send=null,Error="Нет GPU"});ShowPage(0);resourcesPaused=true;await SampleMeasurement();Assert(first.Points.Count==2&&first.Points.Last().Cpu==20,"Recording stopped on navigation or ordinary monitor pause");StopMeasurement();resourcesPaused=false;
            var path=Path.Combine(Program.Data,"measurements",first.Id+".json");var saved=Measurements.Read(path);Assert(saved.Status=="complete"&&saved.Points.Count==2,"Measurement persistence lost samples or completion");
            saved.Points=saved.Points.Skip(1).ToList();Assert(MeasurementStatistic(saved.Points.Select(p=>p.Gpu).ToArray(),"%")=="Нет замеров"&&Measurements.Csv(saved).Contains(",20,50,,1048576,"),"Missing metrics converted to zero in summary/CSV");
            var second=Measurements.Read(path);second.Id=Guid.NewGuid().ToString("N");second.Name="Тест: после изменений";second.Points=saved.Points.Select(p=>new MeasurementPoint{Seconds=p.Seconds,Cpu=30,Memory=45,Gpu=80,Receive=2097152,Error=""}).ToList();Measurements.Save(second);
            Assert(Measurements.Difference("CPU",saved.Points.Select(p=>p.Cpu),second.Points.Select(p=>p.Cpu),"п.п.").Contains("+10")&&Measurements.Difference("GPU",saved.Points.Select(p=>p.Gpu),second.Points.Select(p=>p.Gpu),"п.п.").Contains("недостаточно данных"),"Comparison average or missing sample handling incorrect");

            double? cpu=second.Points[0].Cpu;second.Points[0].Cpu=double.NaN;bool invalid=false;try{Measurements.Save(second);}catch(IOException){invalid=true;}Assert(invalid&&Measurements.Read(Path.Combine(Program.Data,"measurements",second.Id+".json")).Points[0].Cpu==cpu,"Invalid sample replaced a valid file");second.Points[0].Cpu=cpu;
            var broken=Path.Combine(Program.Data,"measurements",Guid.NewGuid().ToString("N")+".json");File.WriteAllText(broken,"{invalid");int errors;var history=Measurements.History(out errors);Assert(errors==1&&history.Length==2,"Corrupt measurement hides intact records");File.Delete(broken);
            ShowPage(6);measurementName.Text="Проверка остановки";await StartMeasurement();measurementTimer.Stop();var pending=new TaskCompletionSource<MeasurementPoint>();int reads=0;measurementRead=(network,gpu)=>{reads++;return pending.Task;};var stopped=measurement;int count=stopped.Points.Count;var running=SampleMeasurement();await SampleMeasurement();Assert(reads==1,"Measurement reads overlap");StopMeasurement();pending.SetResult(new MeasurementPoint{Cpu=99,Error=""});await running;Assert(Measurements.Read(Path.Combine(Program.Data,"measurements",stopped.Id+".json")).Points.Count==count,"Late sample changed a stopped recording");
            ReadMeasurements();measurementFirst.SelectedItem=measurementFirst.Items.Cast<MeasurementSession>().First(r=>r.Id==first.Id);measurementSecond.SelectedItem=measurementSecond.Items.Cast<MeasurementSession>().First(r=>r.Id==second.Id);Assert(measurementReport.Text.Contains("Разница средних"),"Measurement comparison not shown");
            Assert(measurementTable.Children.Count==24,"Measurement comparison table incomplete");var selectedSecond=(MeasurementSession)measurementSecond.SelectedItem;selectedSecond.GpuId="different";RenderMeasurements();Assert(((TextBlock)((Border)measurementTable.Children[15]).Child).Text=="Другой адаптер","GPU comparison accepted different adapters");selectedSecond.GpuId=first.GpuId;RenderMeasurements();
            Window.Width=1600;Window.Height=1000;Window.UpdateLayout();measurementExpander.BringIntoView();Capture("portable-ui-measurements.png");Window.Width=800;Window.Height=600;Window.UpdateLayout();measurementExpander.BringIntoView();Assert(measurementExpander.ActualWidth<=Get<ScrollViewer>("HealthPage").ActualWidth,"Measurement controls overflow compact page");Capture("portable-ui-measurements-compact.png");
            await StartMeasurement();measurementTimer.Stop();var limited=measurement;limited.Points=Enumerable.Range(0,Measurements.Limit).Select(i=>new MeasurementPoint{Seconds=i*2,Error=""}).ToList();await SampleMeasurement();Assert(measurement==null&&Measurements.Read(Path.Combine(Program.Data,"measurements",limited.Id+".json")).Points.Count==Measurements.Limit,"Recording did not stop at sample limit");
            await StartMeasurement();measurementTimer.Stop();var failed=measurement;measurementRead=(network,gpu)=>{throw new IOException("fixture read failure");};await SampleMeasurement();Assert(measurement==null&&Measurements.Read(Path.Combine(Program.Data,"measurements",failed.Id+".json")).Status=="interrupted","Failed recording presented as complete");measurementExpander.IsExpanded=false;ShowPage(0);
        }
    }
}
