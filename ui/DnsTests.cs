using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal static class DnsIntegrationTests {
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        // Changes DNS of the runner's primary adapter and restores it; the runner is discarded after the job.
        internal static void Run(){
            if(!Program.Hosted)throw new InvalidOperationException("DNS integration runs only on disposable hosted CI.");
            var adapter=DnsSettings.Read().FirstOrDefault(a=>a.Gateway);Assert(adapter!=null,"No connected adapter for DNS test");
            string[] before4=adapter.Static4,before6=adapter.Static6;
            try{
                string first=Change(adapter.Id,"quad9",adapter.Fingerprint,null,0);var changed=DnsSettings.Find(adapter.Id);
                Assert(changed.Static4.SequenceEqual(new[]{"9.9.9.9","149.112.112.112"})&&(changed.Index6==0||changed.Static6.SequenceEqual(new[]{"2620:fe::fe","2620:fe::9"})),"DNS servers not applied: "+changed.Summary);
                var record=DnsActions.Read(first);Assert(record.Status=="OK"&&record.Before4.SequenceEqual(before4)&&record.After4.SequenceEqual(changed.Static4),"DNS change not recorded");
                Change(adapter.Id,"cloudflare",adapter.Fingerprint,null,4);Assert(DnsSettings.Find(adapter.Id).Static4.SequenceEqual(changed.Static4),"Stale DNS request changed Windows");
                string lockPath=Path.Combine(Program.Data,"state","run.lock");using(var gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){Change(adapter.Id,"cloudflare",changed.Fingerprint,null,4);}File.Delete(lockPath);Assert(DnsSettings.Find(adapter.Id).Static4.SequenceEqual(changed.Static4),"DNS change ignored engine lock");
                Change(adapter.Id,"restore",changed.Fingerprint,first,0);var restored=DnsSettings.Find(adapter.Id);
                Assert(restored.Static4.SequenceEqual(before4)&&(restored.Index6==0||restored.Static6.SequenceEqual(before6))&&DnsActions.Read(first).Status=="REVERTED","DNS restore failed: "+restored.Summary);
                Change(adapter.Id,"restore",restored.Fingerprint,first,4);
            }finally{var current=DnsSettings.Find(adapter.Id);if(!current.Static4.SequenceEqual(before4)||(current.Index6>0&&!current.Static6.SequenceEqual(before6)))DnsSettings.Apply(current,before4,before6,TextWriter.Null);}
        }
        private static string Change(string adapter,string target,string expected,string restore,int code){string token=Guid.NewGuid().ToString("N");int actual=DnsActions.Worker(new[]{"--dns-worker",adapter,target,expected,token,WindowsIdentity.GetCurrent().User.Value,restore??"-"});Assert(actual==code,"DNS worker returned "+actual+": "+File.ReadAllText(Path.Combine(Program.Data,"runtime",token+".dns.log")));return token;}
    }
    internal sealed partial class MainWindow {
        private async Task DnsSmoke(){
            Assert(DnsSettings.Normalize(new[]{"1.1.1.1","1.1.1.1"},AddressFamily.InterNetwork).SequenceEqual(new[]{"1.1.1.1"})&&DnsSettings.Normalize(new[]{"2a02:6b8::feed:0ff"},AddressFamily.InterNetworkV6).SequenceEqual(new[]{"2a02:6b8::feed:ff"}),"DNS addresses not normalized");
            foreach(var unsafeValue in new[]{new[]{"1.1.1.1 & whoami","4"},new[]{"8.8.8.8;","4"},new[]{"::1","4"},new[]{"fe80::1%12","6"},new[]{"1.1.1.1","6"}}){bool rejected=false;try{DnsSettings.Normalize(new[]{unsafeValue[0]},unsafeValue[1]=="4"?AddressFamily.InterNetwork:AddressFamily.InterNetworkV6);}catch(ArgumentException){rejected=true;}Assert(rejected,"Unsafe DNS address accepted: "+unsafeValue[0]);}
            Assert(DnsSettings.ParseRegistry("1.1.1.1,1.0.0.1 bogus",AddressFamily.InterNetwork).SequenceEqual(new[]{"1.1.1.1","1.0.0.1"})&&DnsSettings.ParseRegistry("",AddressFamily.InterNetwork).Length==0,"Registry DNS list parsed incorrectly");
            Assert(DnsSettings.Providers.All(p=>DnsSettings.Normalize(p.V4,AddressFamily.InterNetwork).Length==p.V4.Length&&DnsSettings.Normalize(p.V6,AddressFamily.InterNetworkV6).Length==p.V6.Length),"Provider list contains invalid addresses");
            Assert(DnsSettings.Fingerprint(new string[0],new string[0])!=DnsSettings.Fingerprint(new[]{"1.1.1.1"},new string[0]),"DNS fingerprint ignores servers");
            string adapterId=Guid.NewGuid().ToString("B");
            foreach(var call in new Action[]{()=>DnsActions.Validate("eth0","cloudflare",new string('a',16),null),()=>DnsActions.Validate(adapterId,"evil",new string('a',16),null),()=>DnsActions.Validate(adapterId,"restore",new string('a',16),"-"),()=>DnsActions.Validate(adapterId,"cloudflare","zz",null)}){bool rejected=false;try{call();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}Assert(rejected,"Unsafe DNS request accepted");}
            var live=await Task.Run(()=>DnsSettings.Read());Assert(live.All(a=>DnsSettings.ValidAdapter(a.Id)&&a.Index4>0),"Read-only DNS inventory failed");
            var read=dnsRead;var run=dnsRun;int calls=0;var adapter=new DnsAdapter{Id=adapterId,Name="Ethernet",Description="Тестовый адаптер",Index4=7,Index6=7,Gateway=true,Effective=new[]{"192.168.1.1"}};
            string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(Program.Data,"dns-history"),path=Path.Combine(directory,id+".json");
            try{
                dnsRead=()=>new[]{adapter};
                dnsRun=(target,provider,expected,restoreId)=>{calls++;Assert(target==adapter.Id&&expected==adapter.Fingerprint,"DNS UI sent stale state");if(provider=="restore"){adapter.Static4=new string[0];adapter.Static6=new string[0];}else{var chosen=DnsSettings.Provider(provider);adapter.Static4=chosen.V4;adapter.Static6=DnsSettings.Normalize(chosen.V6,AddressFamily.InterNetworkV6);}return Task.FromResult(new EngineResult{Code=0,Output="Тест: настройки Windows не менялись."});};
                ShowPage(10);await RefreshDns();Assert(dnsCurrent.Text.Contains("автоматически")&&!dnsApply.IsEnabled,"Current automatic DNS was unclear or re-applicable");
                dnsProvider.SelectedItem=DnsSettings.Provider("cloudflare");Assert(dnsApply.IsEnabled&&dnsDescription.Text.Contains("1.1.1.1"),"DNS provider cannot be selected");
                SetBusy(true);Assert(!dnsApply.IsEnabled&&!dnsRefresh.IsEnabled,"DNS controls ignored operation lock");SetBusy(false);
                var cancel=SelectDns();Assert(confirmation!=null,"DNS change skipped confirmation");FinishConfirmation(false);await cancel;Assert(calls==0,"Cancelled DNS change executed");
                var apply=SelectDns();FinishConfirmation(true);await apply;Assert(calls==1&&dnsCurrent.Text.Contains("1.1.1.1")&&!dnsApply.IsEnabled&&networkHost.Text=="1.1.1.1","DNS choice not applied or refreshed");
                Directory.CreateDirectory(directory);File.WriteAllText(path,new JavaScriptSerializer().Serialize(new DnsChange{Schema="wintools/dns-change/1",Id=id,Adapter=adapter.Id,AdapterName=adapter.Name,Target="cloudflare",TargetName="Cloudflare · 1.1.1.1",Before4=new string[0],Before6=new string[0],After4=adapter.Static4,After6=adapter.Static6,Action="select",Status="OK",TimeUtc=DateTime.UtcNow.ToString("o")}));
                ReadHistory();Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.Run==id&&r.DnsChange&&r.CanRevert&&r.Detail.Contains("DNS")),"DNS record missing from shared history");
                await RefreshDns();Assert(dnsRestore.IsEnabled,"DNS restore not offered for the adapter");
                var restore=RestoreDnsHistory(id);for(int i=0;i<100&&confirmation==null&&!restore.IsCompleted;i++)await Task.Delay(20);Assert(confirmation!=null,"DNS restoration skipped confirmation");FinishConfirmation(true);await restore;Assert(calls==2&&adapter.Static4.Length==0&&dnsCurrent.Text.Contains("автоматически"),"DNS history restore did not return automatic DNS");
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;ShowPage(10);Window.UpdateLayout();dnsApply.BringIntoView();await Task.Delay(80);Window.UpdateLayout();Assert(dnsAdapter.ActualWidth>250&&dnsApply.IsVisible,"DNS manager layout unusable");Capture(size.Width==800?"portable-ui-dns-compact.png":"portable-ui-dns.png");}
                File.WriteAllText(path,"{broken");ReadHistory();Assert(!Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.DnsChange),"Corrupt DNS history was accepted");
                dnsRead=()=>{throw new IOException("Тест ошибки чтения");};await RefreshDns();Assert(!dnsApply.IsEnabled&&dnsAdapter.Items.Count==0,"DNS read failure kept stale actions");
            }finally{if(File.Exists(path))File.Delete(path);dnsRead=read;dnsRun=run;ReadHistory();}
            Get<ScrollViewer>("NetworkPage").ScrollToTop();ShowPage(0);
        }
    }
}
