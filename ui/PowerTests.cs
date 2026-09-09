using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal static class PowerIntegrationTests {
        [DllImport("powrprof.dll")] private static extern uint PowerDuplicateScheme(IntPtr root,ref Guid source,out IntPtr destination);
        [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root,ref Guid scheme);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        internal static void Run(){
            if(!Program.Hosted)throw new InvalidOperationException("Power integration runs only on disposable hosted CI.");string before=PowerPlans.Active();var source=new Guid(before);IntPtr pointer;Assert(PowerDuplicateScheme(IntPtr.Zero,ref source,out pointer)==0&&pointer!=IntPtr.Zero,"Cannot create power fixture");Guid fixture;try{fixture=(Guid)Marshal.PtrToStructure(pointer,typeof(Guid));}finally{LocalFree(pointer);}
            try{string first=Change(fixture.ToString("D"),before,null,0);Assert(PowerPlans.Active()==fixture.ToString("D")&&PowerActions.Read(first).Before==before,"Power selection was not recorded");
                Change(before,fixture.ToString("D"),first,0);Assert(PowerPlans.Active()==before&&PowerActions.Read(first).Status=="REVERTED","Power restore failed");
                Change(fixture.ToString("D"),Guid.NewGuid().ToString("D"),null,4);Assert(PowerPlans.Active()==before,"Stale power request changed Windows");
                string lockPath=Path.Combine(Program.Data,"state","run.lock");using(var gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Change(fixture.ToString("D"),before,null,4);Assert(PowerPlans.Active()==before,"Power switch ignored engine lock");}File.Delete(lockPath);
            }finally{PowerPlans.Select(before);Assert(PowerDeleteScheme(IntPtr.Zero,ref fixture)==0,"Could not remove power fixture");}
        }
        private static string Change(string target,string expected,string restore,int code){string token=Guid.NewGuid().ToString("N");int actual=PowerActions.Worker(new[]{"--power-worker",target,expected,token,WindowsIdentity.GetCurrent().User.Value,restore??"-"});Assert(actual==code,"Power worker returned "+actual+": "+File.ReadAllText(Path.Combine(Program.Data,"runtime",token+".power.log")));return token;}
    }
    internal sealed partial class MainWindow {
        private async Task PowerSmoke(){
            Assert(PowerPlans.ValidId("381b4222-f694-41f0-9685-ff5bb260df2e")&&!PowerPlans.ValidId("../scheme")&&!PowerPlans.ValidId(Guid.Empty.ToString("D")),"Power ID validation failed");
            var actual=await Task.Run(()=>PowerPlans.Read());Assert(actual.Plans.Length>0&&actual.Plans.Any(p=>p.Id==actual.Active),"Read-only power inventory failed");
            var read=powerRead;var run=powerRun;int calls=0;string first=Guid.NewGuid().ToString("D"),second=Guid.NewGuid().ToString("D"),active=first;string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(Program.Data,"power-history"),path=Path.Combine(directory,id+".json");
            try{
                powerRead=()=>new PowerSnapshot{Active=active,Plans=new[]{new PowerPlan{Id=first,Name="Сбалансированная",Description="Тестовая схема: баланс производительности и расхода энергии.",Active=active==first},new PowerPlan{Id=second,Name="Для работы от сети",Description="Тестовая схема с повышенным расходом энергии.",Active=active==second}}};
                powerRun=(target,expected,restoreId)=>{calls++;Assert(expected==active,"Power UI sent stale state");active=target;return Task.FromResult(new EngineResult{Code=0,Output="Тест: настройки Windows не менялись."});};
                ShowPage(8);await RefreshPowerPlans();Assert(!powerApply.IsEnabled&&powerCurrent.Text.Contains("Сбалансированная"),"Active power plan was unclear");powerChoice.SelectedIndex=1;Assert(powerApply.IsEnabled,"Alternate power plan cannot be selected");SetBusy(true);Assert(!powerApply.IsEnabled&&!powerRefresh.IsEnabled,"Power controls ignored operation lock");SetBusy(false);
                var cancel=SelectPowerPlan();Assert(confirmation!=null,"Power switch skipped confirmation");FinishConfirmation(false);await cancel;Assert(calls==0,"Cancelled power change executed");
                var apply=SelectPowerPlan();FinishConfirmation(true);await apply;Assert(calls==1&&active==second&&powerCurrent.Text.Contains("Для работы от сети"),"Power choice not applied or refreshed");
                Directory.CreateDirectory(directory);File.WriteAllText(path,new JavaScriptSerializer().Serialize(new PowerChange{Schema="wintools/power-change/1",Id=id,Before=first,Target=second,BeforeName="Сбалансированная",TargetName="Для работы от сети",After=second,Action="select",Status="OK",TimeUtc=DateTime.UtcNow.ToString("o")}));ReadHistory();Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.Run==id&&r.PowerChange&&r.CanRevert),"Power record missing from shared history");
                var restore=RestorePowerHistory(id);for(int i=0;i<100&&confirmation==null&&!restore.IsCompleted;i++)await Task.Delay(20);Assert(confirmation!=null,"Power restoration skipped confirmation");FinishConfirmation(true);await restore;Assert(calls==2&&active==first,"Power history restore did not select previous plan");
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;ShowPage(8);Get<ScrollViewer>("OptimizationPage").ScrollToTop();Window.UpdateLayout();await Task.Delay(80);Assert(powerChoice.ActualWidth>250&&powerApply.IsVisible,"Power manager layout unusable");Capture(size.Width==800?"portable-ui-power-compact.png":"portable-ui-power.png");}
                File.WriteAllText(path,"{broken");ReadHistory();Assert(Get<ListBox>("History").Items.Count==0,"Corrupt power history was accepted");
                powerRead=()=>{throw new IOException("Тест ошибки чтения");};await RefreshPowerPlans();Assert(!powerApply.IsEnabled&&powerChoice.Items.Count==0&&powerCurrent.Text.Contains("неизвестна"),"Power read failure kept stale actions");
            }finally{if(File.Exists(path))File.Delete(path);powerRead=read;powerRun=run;ReadHistory();}
            await RefreshPowerPlans();ShowPage(0);
        }
    }
}
