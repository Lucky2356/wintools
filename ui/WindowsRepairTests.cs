using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal static class WindowsRepairTests {
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        internal static void Pure(){
            Assert(WindowsIntegrity.DescribeSfcRepair("Windows Resource Protection found corrupt files and successfully repaired them.",0).State=="repaired","SFC repair success lost");
            Assert(WindowsIntegrity.DescribeSfcRepair("Защита ресурсов Windows обнаружила поврежденные файлы и успешно их восстановила.",0).State=="repaired","Russian SFC repair success lost");
            Assert(WindowsIntegrity.DescribeSfcRepair("found corrupt files but was unable to fix some of them",0).State=="issues","Partial SFC repair reported complete");
            Assert(WindowsIntegrity.DescribeSfcRepair("system repair pending which requires reboot to complete",0).State=="failed","Pending reboot reported repair success");
            Assert(WindowsIntegrity.DescribeSfcRepair("localized message",0).State=="review"&&WindowsIntegrity.DescribeSfcRepair("successfully repaired",4).State=="failed","Uncertain SFC result reported success");
            int calls=0;var blocked=WindowsIntegrity.RepairWindows(()=>new IntegrityResult{State="unrepairable",Summary="Повреждения компонентов"},()=>{calls++;return new IntegrityResult{State="healthy"};},value=>{});Assert(calls==0&&blocked.State=="unrepairable","SFC ran after failed DISM");
            var partial=WindowsIntegrity.RepairWindows(()=>new IntegrityResult{State="repaired",Summary="Компоненты исправлены"},()=>{calls++;return new IntegrityResult{State="issues",Summary="Часть файлов не восстановлена"};},value=>{});Assert(calls==1&&partial.State=="issues"&&partial.Summary.Contains("Компоненты исправлены"),"Partial combined repair lost completed stage");
            var completed=WindowsIntegrity.RepairWindows(()=>new IntegrityResult{State="repaired",Summary="Компоненты"},()=>new IntegrityResult{State="healthy",Summary="Файлы"},value=>{});Assert(completed.State=="repaired","Combined repair lost changes");
            Assert(!IntegrityActions.CanCancel("windows-repair")&&!IntegrityActions.CanCancel("sfc-repair")&&IntegrityActions.CanCancel("dism-repair"),"Unsafe cancellation offered for SFC");
        }
        internal static void Native(){
            if(!Program.Hosted)throw new InvalidOperationException("Repair integration runs only on disposable hosted CI.");
            foreach(string action in new[]{"windows-repair","dism-repair","sfc-repair"}){string id=Guid.NewGuid().ToString("N");using(var cancelled=new EventWaitHandle(true,EventResetMode.ManualReset,IntegrityActions.CreateEventNameForTest(id))){int code=IntegrityActions.Worker(new[]{"--integrity-worker",action,id,System.Security.Principal.WindowsIdentity.GetCurrent().User.Value});Assert(code==2&&IntegrityActions.Read(id).State=="cancelled","Repair worker ignored pre-cancel");}}
            var text=new System.Text.StringBuilder();using(var cancel=new EventWaitHandle(false,EventResetMode.ManualReset)){var components=WindowsIntegrity.RepairComponents(cancel,line=>{text.AppendLine(line);SelfTests.Trace(line);});Assert(components.State=="healthy"||components.State=="repaired","DISM maintenance failed: "+text);}
            text.Clear();var files=WindowsIntegrity.RepairFiles(line=>{text.AppendLine(line);SelfTests.Trace(line);},true);Assert(files.State=="healthy"||files.State=="repaired","Native SFC single-file maintenance failed: "+text);
        }
    }
    internal sealed partial class MainWindow {
        private async Task WindowsRepairSmoke(){
            WindowsRepairTests.Pure();var run=integrityRun;int calls=0;string id=Guid.NewGuid().ToString("N"),path=Path.Combine(Program.Data,"integrity-history",id+".json");
            try{integrityRun=(action,progress,ready)=>{calls++;Assert(action=="windows-repair","Wrong repair command");ready(null);progress("Компоненты: восстановлены\n@progress|0\nSFC: проверка 100%");return Task.FromResult(new EngineResult{Code=0,Output="Компоненты восстановлены. Системные файлы проверены. Тест: Windows не менялась."});};ShowPage(11);integrityChoice.SelectedIndex=3;Window.Width=800;Window.Height=600;Window.UpdateLayout();var cancelled=StartIntegrity();Assert(confirmation!=null&&!busy,"Repair skipped confirmation");Window.UpdateLayout();Assert(IsVisibleInWindow("ConfirmYes")&&IsVisibleInWindow("ConfirmNo"),"Repair confirmation clipped");FinishConfirmation(false);await cancelled;Assert(calls==0,"Cancelled repair executed");var pending=StartIntegrity();FinishConfirmation(true);await pending;Assert(calls==1&&!integrityStop.IsEnabled&&integrityReport.Text.Contains("Компоненты восстановлены"),"Combined repair report or cancellation incorrect");
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,new JavaScriptSerializer().Serialize(new IntegrityRecord{Schema="wintools/integrity/1",Id=id,Action="windows-repair",TimeUtc=DateTime.UtcNow.ToString("o"),State="repaired",Summary="Тестовый отчёт восстановления."}));ReadHistory();var row=Get<ListBox>("History").Items.Cast<HistoryRow>().Single(r=>r.Run==id);Assert(!row.CanRevert&&row.Status=="Восстановлено","Repair exposed fictitious rollback or wrong status");
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();await Task.Delay(80);Assert(integrityReport.ActualHeight>70&&integrityStart.ActualWidth>100,"Repair layout clipped");Capture(size.Width==800?"portable-ui-repair-compact.png":"portable-ui-repair.png");}
            }finally{if(File.Exists(path))File.Delete(path);integrityRun=run;ReadHistory();integrityChoice.SelectedIndex=0;}ShowPage(0);
        }
    }
}
