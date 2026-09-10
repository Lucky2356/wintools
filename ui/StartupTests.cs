using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Wintools {
    internal static class StartupIntegrationTests {
        private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        private static string Change(StartupEntry entry,string action,string restore,int expected){string id=Guid.NewGuid().ToString("N");int code=StartupActions.Worker(new[]{"--startup-worker",action,entry.Source,Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Name)),entry.Fingerprint,id,WindowsIdentity.GetCurrent().User.Value,restore??"-"});Assert(code==expected,"Startup worker: "+File.ReadAllText(Path.Combine(Program.Data,"runtime",id+".startup.log")));return id;}
        internal static void Run(){
            if(!Program.Hosted)throw new InvalidOperationException("Startup integration runs only on disposable hosted CI.");
            foreach(string source in StartupEntries.Sources){string name="WintoolsFixture_"+Guid.NewGuid().ToString("N")+(source.EndsWith("folder")?".txt":"");string folder=null;
                try{
                    if(source.EndsWith("folder")){folder=StartupEntries.Folder(source);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,name),"Non-executable startup fixture");}
                    else using(var root=StartupEntries.Root(source,false))using(var key=root.CreateSubKey(StartupEntries.RunPath))key.SetValue(name,"WintoolsFixture-does-not-exist.exe --fixture",RegistryValueKind.ExpandString);
                    var before=StartupEntries.Inspect(source,name);Assert(before.Enabled==true&&before.Approval==null,"Missing startup approval misclassified");string record=Change(before,"disable",null,0);var disabled=StartupEntries.Inspect(source,name);Assert(disabled.Enabled==false&&disabled.Identity==before.Identity,"Startup command changed or disable failed");
                    Change(before,"disable",null,4);Assert(StartupEntries.Inspect(source,name).Approval==disabled.Approval,"Stale startup request overwrote state");
                    Change(disabled,"restore",record,0);Assert(StartupEntries.Inspect(source,name).Approval==null&&StartupActions.Read(record).Status=="REVERTED","Startup restoration did not preserve absent value");
                    string lockPath=Path.Combine(Program.Data,"state","run.lock");using(var gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))Change(StartupEntries.Inspect(source,name),"disable",null,4);File.Delete(lockPath);
                    string second=Change(StartupEntries.Inspect(source,name),"disable",null,0);StartupEntries.WriteApproval(source,name,StartupEntries.Encode(null,true));Change(StartupEntries.Inspect(source,name),"restore",second,4);Assert(StartupEntries.Inspect(source,name).Enabled==true,"Restore overwrote external change");
                    using(var root=StartupEntries.Root(source,true))using(var key=root.CreateSubKey(StartupEntries.ApprovalKey(source)))key.SetValue(name,new byte[]{99,0,0,0},RegistryValueKind.Binary);
                    var unknown=StartupEntries.Inspect(source,name);Assert(!unknown.Enabled.HasValue,"Unknown startup format reported enabled");Change(unknown,"enable",null,4);
                }finally{
                    using(var root=StartupEntries.Root(source,true))using(var key=root.OpenSubKey(StartupEntries.ApprovalKey(source),true))if(key!=null)key.DeleteValue(name,false);
                    if(folder!=null){string path=Path.Combine(folder,name);if(File.Exists(path))File.Delete(path);}else using(var root=StartupEntries.Root(source,false))using(var key=root.OpenSubKey(StartupEntries.RunPath,true))if(key!=null)key.DeleteValue(name,false);
                }
            }
        }
    }
    internal sealed partial class MainWindow {
        private async Task StartupSmoke(){
            foreach(byte code in new byte[]{2,3,6,7}){var bytes=new byte[12];bytes[0]=code;Assert(StartupEntries.Decode(Convert.ToBase64String(bytes))==(code%2==0),"Startup approval decoding");}
            Assert(!StartupEntries.Decode("bad").HasValue&&!StartupEntries.Decode(Convert.ToBase64String(new byte[12])).HasValue,"Unknown startup approval accepted");
            var inventory=await Task.Run(()=>StartupEntries.Read());Assert(inventory.Entries!=null&&inventory.Errors!=null,"Startup inventory failed");var read=startupRead;var run=startupRun;int calls=0;var rows=Enumerable.Range(0,150).Select(i=>new StartupEntry{Source="user-run",Name="Программа "+i.ToString("D3"),Command="C:\\Apps\\Example.exe --startup",Identity=StartupEntries.Hash("fixture"+i),Approval=i%2==0?null:StartupEntries.Encode(null,false)}).ToArray();rows[2].Error="Не удалось прочитать состояние";
            string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(Program.Data,"startup-history"),path=Path.Combine(directory,id+".json");
            try{startupRead=()=>new StartupSnapshot{Entries=rows,Errors=new string[0]};startupRun=(entry,action,restore)=>{calls++;entry.Approval=StartupEntries.Encode(entry.Approval,action=="enable");return Task.FromResult(new EngineResult{Code=0,Output="Тест: Windows не менялась."});};ShowPage(12);await ReadStartup();startupList.SelectedItem=rows[2];Assert(!startupEnable.IsEnabled&&!startupDisable.IsEnabled,"Unknown startup state is mutable");startupList.SelectedItem=rows[0];Assert(startupDisable.IsEnabled&&!startupEnable.IsEnabled,"Startup controls disagree with state");
                var cancel=ChangeStartup(false);Assert(confirmation!=null,"Startup change skipped confirmation");FinishConfirmation(false);await cancel;Assert(calls==0,"Cancelled startup action ran");var apply=ChangeStartup(false);FinishConfirmation(true);await apply;Assert(calls==1&&rows[0].Enabled==false&&startupEnable.IsEnabled,"Startup action or refresh failed");
                startupFilter.SelectedIndex=3;Assert(startupList.Items.Count==1,"Unknown startup filter failed");startupFilter.SelectedIndex=0;startupSearch.Text="__missing__";Assert(startupList.Items.Count==0&&!startupEnable.IsEnabled&&!startupDisable.IsEnabled,"Empty startup filter kept stale action");startupSearch.Clear();SetBusy(true);Assert(!startupRefresh.IsEnabled&&!startupEnable.IsEnabled,"Startup ignored busy state");SetBusy(false);
                Directory.CreateDirectory(directory);File.WriteAllText(path,new JavaScriptSerializer().Serialize(new StartupChange{Schema="wintools/startup-change/1",Id=id,Source="user-run",Name=rows[0].Name,Identity=rows[0].Identity,Before=null,After=rows[0].Approval,Action="disable",Status="OK",TimeUtc=DateTime.UtcNow.ToString("o")}));ReadHistory();Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.Run==id&&r.StartupChange&&r.CanRevert),"Startup history missing");
                foreach(var size in new[]{new Size(1600,1000),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();await Task.Delay(80);Assert(startupList.ActualHeight>120&&startupList.ActualWidth>400&&IsVisibleInWindow("NavStartup"),"Startup layout clipped");Capture(size.Width==800?"portable-ui-startup-compact.png":"portable-ui-startup.png");}
                var waiting=new System.Threading.ManualResetEventSlim(false);var entered=new System.Threading.ManualResetEventSlim(false);int reads=0;startupRead=()=>{int count=System.Threading.Interlocked.Increment(ref reads);if(count==1){entered.Set();waiting.Wait();}return new StartupSnapshot{Entries=count==1?new StartupEntry[0]:rows,Errors=new string[0]};};var delayed=ReadStartup();for(int n=0;n<100&&!entered.IsSet;n++)await Task.Delay(10);Assert(entered.IsSet,"Startup delayed read did not start");SetBusy(true);SetBusy(false);waiting.Set();await delayed;Assert(reads==2&&startupList.Items.Count==rows.Length,"Stale startup read replaced current state");waiting.Dispose();entered.Dispose();
                File.WriteAllText(path,"{broken");ReadHistory();Assert(Get<ListBox>("History").Items.Count==0,"Corrupt startup history accepted");startupRead=()=>{throw new IOException("Тест ошибки");};await ReadStartup();Assert(startupList.Items.Count==0&&!startupEnable.IsEnabled&&startupStatus.Text.Contains("Не удалось"),"Startup failure kept stale rows");
            }finally{if(File.Exists(path))File.Delete(path);startupRead=read;startupRun=run;ReadHistory();}ShowPage(0);
        }
    }
}
