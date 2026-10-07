using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal static class HostsIntegrationTests {
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        // Edits the real hosts file of the disposable runner with a reserved .invalid name and restores the original bytes.
        internal static void Run(){
            if(!Program.Hosted)throw new InvalidOperationException("Hosts integration runs only on disposable hosted CI.");
            var original=HostsFile.ReadBytes();const string domain="wintools-ci-test.invalid";
            try{
                string block=Change("block",domain,HostsFile.Hash(original),null,0);var blocked=HostsFile.ReadBytes();
                Assert(HostsFile.Parse(blocked).Any(e=>e.Host==domain&&e.Managed&&e.Address=="0.0.0.0")&&blocked.Take(original.Length).SequenceEqual(original),"Hosts block did not append a managed line");
                Change("block","other-ci-test.invalid",HostsFile.Hash(original),null,4);Assert(HostsFile.ReadBytes().SequenceEqual(blocked),"Stale hosts request changed the file");
                string unblock=Change("unblock",domain,HostsFile.Hash(blocked),null,0);Assert(!HostsFile.Parse(HostsFile.ReadBytes()).Any(e=>e.Host==domain),"Hosts unblock left the entry");
                Change("restore","-",HostsFile.Hash(HostsFile.ReadBytes()),block,4);
                Change("restore","-",HostsFile.Hash(HostsFile.ReadBytes()),unblock,0);Assert(HostsFile.ReadBytes().SequenceEqual(blocked)&&HostsFile.Read(unblock).Status=="REVERTED","Hosts restore did not bring back the exact previous file");
                Change("restore","-",HostsFile.Hash(blocked),block,0);Assert(HostsFile.ReadBytes().SequenceEqual(original),"Hosts restore did not return the original file");
            }finally{if(!HostsFile.ReadBytes().SequenceEqual(original))File.WriteAllBytes(HostsFile.Path,original);}
        }
        private static string Change(string action,string domain,string expected,string restore,int code){string token=Guid.NewGuid().ToString("N");int actual=HostsFile.Worker(new[]{"--hosts-worker",action,domain,expected,token,WindowsIdentity.GetCurrent().User.Value,restore??"-"});Assert(actual==code,"Hosts worker returned "+actual+": "+File.ReadAllText(Path.Combine(Program.Data,"runtime",token+".hosts.log")));return token;}
    }
    internal sealed partial class MainWindow {
        private async Task HostsSmoke(){
            Assert(HostsFile.NormalizeDomain(" Ads.Example.COM. ")=="ads.example.com"&&HostsFile.NormalizeDomain("пример.рф")=="xn--e1afmkfd.xn--p1ai","Domain normalization failed");
            foreach(var invalid in new[]{"http://ads.example.com","ads.example.com/path","*.example.com","example","localhost","a b.com","ads.example.com:80","1.2.3.4"}){bool rejected=false;try{HostsFile.NormalizeDomain(invalid);}catch(ArgumentException){rejected=true;}Assert(rejected,"Invalid domain accepted: "+invalid);}
            var sample=Encoding.ASCII.GetBytes("# comment\r\n127.0.0.1 localhost\r\n10.0.0.1 nas.local media.local # home");
            var parsed=HostsFile.Parse(sample);Assert(parsed.Length==3&&parsed.All(e=>!e.Managed),"Hosts parsing incorrect");
            var added=HostsFile.Add(sample,"ads.example.com");Assert(Encoding.ASCII.GetString(added).EndsWith("# home\r\n0.0.0.0 ads.example.com # wintools\r\n")&&HostsFile.Parse(added).Single(e=>e.Host=="ads.example.com").Managed,"Hosts block line incorrect");
            Assert(HostsFile.Remove(added,"ads.example.com").SequenceEqual(Encoding.UTF8.GetBytes("# comment\r\n127.0.0.1 localhost\r\n10.0.0.1 nas.local media.local # home\r\n")),"Hosts unblock changed other lines");
            bool duplicate=false;try{HostsFile.Add(added,"ADS.example.com");}catch(InvalidOperationException){duplicate=true;}Assert(duplicate,"Duplicate hosts entry accepted");
            bool foreign=false;try{HostsFile.Remove(sample,"nas.local");}catch(ArgumentException){foreign=true;}catch(InvalidOperationException){foreign=true;}Assert(foreign,"Foreign hosts entry removed");
            foreach(var call in new Action[]{()=>HostsFile.Validate("block","ads.example.com & del",new string('a',16),null),()=>HostsFile.Validate("delete","ads.example.com",new string('a',16),null),()=>HostsFile.Validate("restore","ads.example.com",new string('a',16),Guid.NewGuid().ToString("N")),()=>HostsFile.Validate("block","ads.example.com","zz",null)}){bool rejected=false;try{call();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}Assert(rejected,"Unsafe hosts request accepted");}
            var live=await Task.Run(()=>HostsFile.ReadBytes());HostsFile.Parse(live);
            var read=hostsRead;var run=hostsRun;var content=sample;int calls=0;string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(Program.Data,"hosts-history");
            try{
                hostsRead=()=>content;
                hostsRun=(action,domain,expected,restoreId)=>{calls++;Assert(expected==HostsFile.Hash(content),"Hosts UI sent stale state");content=action=="block"?HostsFile.Add(content,domain):action=="unblock"?HostsFile.Remove(content,domain):sample;return Task.FromResult(new EngineResult{Code=0,Output="Тест: файл hosts не менялся."});};
                ShowPage(10);await RefreshHosts();Assert(hostsList.Items.Count==3&&!hostsBlock.IsEnabled&&!hostsUnblock.IsEnabled,"Hosts entries not shown or actions enabled without input");
                hostsDomain.Text="https://bad/";Assert(!hostsBlock.IsEnabled,"Invalid domain enabled blocking");hostsDomain.Text="Ads.Example.com";Assert(hostsBlock.IsEnabled,"Valid domain cannot be blocked");
                SetBusy(true);Assert(!hostsBlock.IsEnabled&&!hostsRefresh.IsEnabled,"Hosts controls ignored operation lock");SetBusy(false);
                var cancel=ChangeHosts("block");Assert(confirmation!=null,"Hosts change skipped confirmation");FinishConfirmation(false);await cancel;Assert(calls==0,"Cancelled hosts change executed");
                var block=ChangeHosts("block");FinishConfirmation(true);await block;Assert(calls==1&&hostsList.Items.Cast<HostsEntry>().First().Managed&&hostsDomain.Text.Length==0,"Blocked domain not shown first");
                hostsList.SelectedIndex=hostsList.Items.Cast<HostsEntry>().ToList().FindIndex(e=>!e.Managed);Assert(!hostsUnblock.IsEnabled,"Foreign hosts entry can be removed");hostsList.SelectedIndex=0;Assert(hostsUnblock.IsEnabled,"Managed entry cannot be unblocked");
                Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,id+".json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new HostsChange{Schema="wintools/hosts-change/1",Id=id,Action="block",Domain="ads.example.com",BeforeHash=HostsFile.Hash(sample),AfterHash=HostsFile.Hash(content),Status="OK",TimeUtc=DateTime.UtcNow.ToString("o")}));
                ReadHistory();Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.Run==id&&r.HostsChange&&r.CanRevert),"Hosts record missing from shared history");await RefreshHosts();Assert(hostsRestore.IsEnabled,"Hosts restore not offered");
                var restore=RestoreHostsHistory(id);for(int i=0;i<100&&confirmation==null&&!restore.IsCompleted;i++)await Task.Delay(20);Assert(confirmation!=null,"Hosts restore skipped confirmation");FinishConfirmation(true);await restore;Assert(calls==2&&content.SequenceEqual(sample),"Hosts restore not requested");
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;ShowPage(10);Window.UpdateLayout();hostsBlock.BringIntoView();await Task.Delay(80);Window.UpdateLayout();Assert(hostsDomain.ActualWidth>200&&hostsBlock.IsVisible,"Hosts editor layout unusable");Capture(size.Width==800?"portable-ui-hosts-compact.png":"portable-ui-hosts.png");}
                File.WriteAllText(Path.Combine(directory,id+".json"),"{broken");ReadHistory();Assert(!Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.HostsChange),"Corrupt hosts history was accepted");
                hostsRead=()=>{throw new IOException("Тест ошибки чтения");};await RefreshHosts();Assert(!hostsBlock.IsEnabled&&hostsList.Items.Count==0,"Hosts read failure kept stale actions");
            }finally{hostsRead=read;hostsRun=run;hostsDomain.Clear();if(Directory.Exists(directory))Directory.Delete(directory,true);ReadHistory();}
            Get<ScrollViewer>("NetworkPage").ScrollToTop();ShowPage(0);
        }
    }
}
