using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal static class IntegrityIntegrationTests {
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        internal static void Run(){
            if(!Program.Hosted)throw new InvalidOperationException("Integrity integration is limited to hosted CI.");
            string id=Guid.NewGuid().ToString("N");using(var cancel=new EventWaitHandle(false,EventResetMode.ManualReset,IntegrityActions.CreateEventNameForTest(id))){int result=IntegrityActions.Worker(new[]{"--integrity-worker","dism-status",id,WindowsIdentity.GetCurrent().User.Value});Assert(result==0,"Native DISM check failed: "+IntegrityActions.ReadLog(id));Assert(new[]{"not-marked","repairable","unrepairable"}.Contains(IntegrityActions.Read(id).State),"DISM status was not represented accurately");}
            id=Guid.NewGuid().ToString("N");using(var cancel=new EventWaitHandle(true,EventResetMode.ManualReset,IntegrityActions.CreateEventNameForTest(id))){Assert(IntegrityActions.Worker(new[]{"--integrity-worker","dism-scan",id,WindowsIdentity.GetCurrent().User.Value})==2&&IntegrityActions.Read(id).State=="cancelled","Cancelled check still ran");}
            var output=new System.Text.StringBuilder();var files=WindowsIntegrity.CheckFiles(line=>output.AppendLine(line),true);Assert(files.State!="failed"&&output.ToString().Contains("Windows Resource Protection"),"Native SFC output was unreadable: "+output);
        }
    }
    internal sealed partial class MainWindow {
        private async Task IntegritySmoke(){
            Assert(WindowsIntegrity.DescribeDism(0,false).State=="not-marked"&&WindowsIntegrity.DescribeDism(0,true).State=="healthy","Past DISM flag presented as fresh scan");Assert(WindowsIntegrity.DescribeDism(1,true).State=="repairable"&&WindowsIntegrity.DescribeDism(2,true).State=="unrepairable","DISM damage states lost");
            Assert(WindowsIntegrity.DescribeSfc("Windows Resource Protection did not find any integrity violations.",0).State=="healthy","English SFC result not recognized");Assert(WindowsIntegrity.DescribeSfc("Защита ресурсов Windows не обнаружила нарушений целостности.",0).State=="healthy","Russian SFC result not recognized");Assert(WindowsIntegrity.DescribeSfc("Windows Resource Protection found integrity violations.",0).State=="issues","SFC violations treated as healthy");Assert(WindowsIntegrity.DescribeSfc("Unknown localized response",0).State=="review","Unknown SFC result treated as healthy");Assert(WindowsIntegrity.DescribeSfc("did not find any integrity violations",4).State=="failed","SFC failure code ignored");
            Assert(IntegrityPercent("Windows 10.0.26100") ==null&&IntegrityPercent("Verification 35%") ==35&&IntegrityPercent("@progress|72") ==72&&IntegrityPercent("@progress|101")==null,"Unrelated numbers or invalid progress displayed");
            var run=integrityRun;string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(Program.Data,"integrity-history"),path=Path.Combine(directory,id+".json");
            try{
                bool cancelled=false;var completion=new TaskCompletionSource<EngineResult>();integrityRun=(action,progress,ready)=>{ready(()=>cancelled=true);progress("Проверяем компоненты Windows\n@progress|35");return completion.Task;};ShowPage(11);var pending=StartIntegrity();Assert(busy&&!integrityStart.IsEnabled&&integrityStop.IsEnabled&&integrityProgress.Value==35,"Integrity progress or operation lock failed");CancelIntegrity();Assert(cancelled&&!integrityStop.IsEnabled,"Integrity cancellation not sent exactly once");completion.SetResult(new EngineResult{Code=2,Output="Тест: проверка отменена, Windows не проверялась."});await pending;Assert(!busy&&integrityStatus.Text.Contains("отменена"),"Cancelled check reported completion");
                integrityRun=(action,progress,ready)=>{ready(null);progress("Verification 100% complete.");return Task.FromResult(new EngineResult{Code=0,Output="Тестовый отчёт: нарушений целостности не найдено. Настройки Windows не менялись."});};integrityChoice.SelectedIndex=1;await StartIntegrity();Assert(!integrityStop.IsEnabled&&integrityReport.Text.Contains("Тестовый отчёт"),"SFC check did not show result");
                Directory.CreateDirectory(directory);File.WriteAllText(path,new JavaScriptSerializer().Serialize(new IntegrityRecord{Schema="wintools/integrity/1",Id=id,Action="sfc-verify",TimeUtc=DateTime.UtcNow.ToString("o"),State="healthy",Summary="Тестовый сохранённый отчёт."}));ReadHistory();var row=Get<ListBox>("History").Items.Cast<HistoryRow>().Single(r=>r.Run==id);Assert(row.IntegrityCheck&&!row.CanRevert,"Read-only check exposed rollback");Get<ListBox>("History").SelectedItem=row;Assert(Get<Button>("HistoryReport").IsEnabled,"History report cannot be opened");Get<Button>("HistoryReport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(page==11&&integrityReport.Text.Contains("сохранённый"),"Saved integrity report was not shown");
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();await Task.Delay(80);Assert(integrityReport.ActualHeight>90&&IsVisibleInWindow("NavIntegrity"),"Integrity page or navigation clipped");Capture(size.Width==800?"portable-ui-integrity-compact.png":"portable-ui-integrity.png");}
                File.WriteAllText(path,"{broken");ReadHistory();Assert(Get<ListBox>("History").Items.Count==0,"Corrupt integrity history accepted");
            }finally{if(File.Exists(path))File.Delete(path);integrityRun=run;ReadHistory();}ShowPage(0);
        }
    }
}
