using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Wintools {
    // Pure logic checks that need no UI, network or system changes; they run first so failures show up in seconds.
    internal static class UnitTests {
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        private const string BootXml="<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>100</EventID></System><EventData><Data Name='BootTsVersion'>2</Data><Data Name='BootTime'>{0}</Data><Data Name='MainPathBootTime'>{1}</Data><Data Name='BootPostBootTime'>{2}</Data></EventData></Event>";
        private const string DelayXml="<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>101</EventID></System><EventData><Data Name='Name'>C:\\Program Files\\Updater\\updater.exe</Data><Data Name='FriendlyName'>{0}</Data><Data Name='TotalTime'>{1}</Data><Data Name='DegradationTime'>{2}</Data></EventData></Event>";
        private static void CatalogueState(){
            var dword=new Tweak{Id="SMOKE-DWORD",Kind="REG",ValueType="REG_DWORD",Value="0"};
            Assert(TweakStates.Compare(dword,RegistryValueKind.DWord,0).Applied==true,"Matching DWORD not recognised as applied");
            var other=TweakStates.Compare(dword,RegistryValueKind.DWord,1);Assert(other.Applied==false&&other.Text.Contains("сейчас 1"),"Different DWORD not reported");
            Assert(TweakStates.Compare(dword,RegistryValueKind.Unknown,null).Applied==false,"Missing value reported as applied");
            Assert(TweakStates.Compare(dword,RegistryValueKind.String,"0").Applied==false,"Value of another type reported as applied");
            uint parsed;Assert(TweakStates.TryDword("0xffffffff",out parsed)&&parsed==uint.MaxValue&&TweakStates.TryDword("4294967295",out parsed)&&parsed==uint.MaxValue&&!TweakStates.TryDword("abc",out parsed),"DWORD parsing differs from the engine");
            Assert(TweakStates.Compare(dword,RegistryValueKind.DWord,-1).Text.Contains("4294967295"),"Negative DWORD shown incorrectly");
            var empty=new Tweak{Id="SMOKE-SZ",Kind="REG",ValueType="REG_SZ",Value="@EMPTY@"};
            Assert(TweakStates.Compare(empty,RegistryValueKind.String,"").Applied==true&&TweakStates.Compare(empty,RegistryValueKind.String,"x").Applied==false,"Empty string placeholder misread");
        }
        private static void Dns(){
            Assert(DnsSettings.Normalize(new[]{"1.1.1.1","1.1.1.1"},AddressFamily.InterNetwork).SequenceEqual(new[]{"1.1.1.1"})&&DnsSettings.Normalize(new[]{"2a02:6b8::feed:0ff"},AddressFamily.InterNetworkV6).SequenceEqual(new[]{"2a02:6b8::feed:ff"}),"DNS addresses not normalized");
            foreach(var unsafeValue in new[]{new[]{"1.1.1.1 & whoami","4"},new[]{"8.8.8.8;","4"},new[]{"::1","4"},new[]{"fe80::1%12","6"},new[]{"1.1.1.1","6"}}){bool rejected=false;try{DnsSettings.Normalize(new[]{unsafeValue[0]},unsafeValue[1]=="4"?AddressFamily.InterNetwork:AddressFamily.InterNetworkV6);}catch(ArgumentException){rejected=true;}Assert(rejected,"Unsafe DNS address accepted: "+unsafeValue[0]);}
            Assert(DnsSettings.ParseRegistry("1.1.1.1,1.0.0.1 bogus",AddressFamily.InterNetwork).SequenceEqual(new[]{"1.1.1.1","1.0.0.1"})&&DnsSettings.ParseRegistry("",AddressFamily.InterNetwork).Length==0,"Registry DNS list parsed incorrectly");
            Assert(DnsSettings.Providers.All(p=>DnsSettings.Normalize(p.V4,AddressFamily.InterNetwork).Length==p.V4.Length&&DnsSettings.Normalize(p.V6,AddressFamily.InterNetworkV6).Length==p.V6.Length),"Provider list contains invalid addresses");
            Assert(DnsSettings.Fingerprint(new string[0],new string[0])!=DnsSettings.Fingerprint(new[]{"1.1.1.1"},new string[0]),"DNS fingerprint ignores servers");
            string adapterId=Guid.NewGuid().ToString("B");
            foreach(var call in new Action[]{()=>DnsActions.Validate("eth0","cloudflare",new string('a',16),null),()=>DnsActions.Validate(adapterId,"evil",new string('a',16),null),()=>DnsActions.Validate(adapterId,"restore",new string('a',16),"-"),()=>DnsActions.Validate(adapterId,"cloudflare","zz",null)}){bool rejected=false;try{call();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}Assert(rejected,"Unsafe DNS request accepted");}
        }
        private static void Hosts(){
            Assert(HostsFile.NormalizeDomain(" Ads.Example.COM. ")=="ads.example.com"&&HostsFile.NormalizeDomain("пример.рф")=="xn--e1afmkfd.xn--p1ai","Domain normalization failed");
            foreach(var invalid in new[]{"http://ads.example.com","ads.example.com/path","*.example.com","example","localhost","a b.com","ads.example.com:80","1.2.3.4"}){bool rejected=false;try{HostsFile.NormalizeDomain(invalid);}catch(ArgumentException){rejected=true;}Assert(rejected,"Invalid domain accepted: "+invalid);}
            var sample=Encoding.ASCII.GetBytes("# comment\r\n127.0.0.1 localhost\r\n10.0.0.1 nas.local media.local # home");
            var parsed=HostsFile.Parse(sample);Assert(parsed.Length==3&&parsed.All(e=>!e.Managed),"Hosts parsing incorrect");
            var added=HostsFile.Add(sample,"ads.example.com");Assert(Encoding.ASCII.GetString(added).EndsWith("# home\r\n0.0.0.0 ads.example.com # wintools\r\n")&&HostsFile.Parse(added).Single(e=>e.Host=="ads.example.com").Managed,"Hosts block line incorrect");
            Assert(HostsFile.Remove(added,"ads.example.com").SequenceEqual(Encoding.UTF8.GetBytes("# comment\r\n127.0.0.1 localhost\r\n10.0.0.1 nas.local media.local # home\r\n")),"Hosts unblock changed other lines");
            bool duplicate=false;try{HostsFile.Add(added,"ADS.example.com");}catch(InvalidOperationException){duplicate=true;}Assert(duplicate,"Duplicate hosts entry accepted");
            bool foreign=false;try{HostsFile.Remove(sample,"nas.local");}catch(ArgumentException){foreign=true;}catch(InvalidOperationException){foreign=true;}Assert(foreign,"Foreign hosts entry removed");
            foreach(var call in new Action[]{()=>HostsFile.Validate("block","ads.example.com & del",new string('a',16),null),()=>HostsFile.Validate("delete","ads.example.com",new string('a',16),null),()=>HostsFile.Validate("restore","ads.example.com",new string('a',16),Guid.NewGuid().ToString("N")),()=>HostsFile.Validate("block","ads.example.com","zz",null)}){bool rejected=false;try{call();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}Assert(rejected,"Unsafe hosts request accepted");}
        }
        private static void Package(){
            Assert(Packages.Catalog.All(p=>Packages.ValidId(p.Id))&&Packages.Catalog.Select(p=>p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()==Packages.Catalog.Length,"Package catalogue IDs invalid or duplicated");
            foreach(var unsafeId in new[]{"7zip.7zip --override x","a&b","\"x\"","../x"})Assert(!Packages.ValidId(unsafeId),"Unsafe package ID accepted: "+unsafeId);
            bool rejected=false;try{Packages.Arguments("install","Unknown.Package");}catch(ArgumentException){rejected=true;}Assert(rejected,"Package outside the catalogue accepted");
            Assert(Packages.Arguments("install","7zip.7zip")=="install --id 7zip.7zip --exact --source winget --silent "+Packages.Agreements,"Install arguments changed");
            Assert(Packages.Succeeded(0)&&Packages.Succeeded(unchecked((int)0x8A150061))&&Packages.Succeeded(unchecked((int)0x8A15002B))&&!Packages.Succeeded(1)&&Packages.Describe(unchecked((int)0x8A150061),"install")=="Уже установлено","winget result codes misread");
            Assert(Packages.ParseExport("{\"Sources\":[{\"Packages\":[{\"PackageIdentifier\":\"7zip.7zip\"},{\"PackageIdentifier\":\"bad id\"}],\"SourceDetails\":{\"Name\":\"winget\"}}]}").SetEquals(new[]{"7zip.7zip"}),"winget export parsed incorrectly");
            Assert(!Packages.Meaningful("  \\ ")&&!Packages.Meaningful("██████▒▒▒  1.2 MB / 3 MB")&&Packages.Meaningful("Successfully installed"),"winget progress filter incorrect");
        }
        private static void WindowsUpdate(){
            foreach(var call in new Action[]{()=>WindowsUpdates.Validate("pause","36",new string('a',16),null),()=>WindowsUpdates.Validate("pause","7 & x",new string('a',16),null),()=>WindowsUpdates.Validate("hours","8-8",new string('a',16),null),()=>WindowsUpdates.Validate("hours","0-23",new string('a',16),null),()=>WindowsUpdates.Validate("disable","-",new string('a',16),null),()=>WindowsUpdates.Validate("restore","-",new string('a',16),"-"),()=>WindowsUpdates.Validate("resume","-","zz",null)}){bool rejected=false;try{call();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}Assert(rejected,"Unsafe update request accepted");}
            WindowsUpdates.Validate("hours","20-8",new string('a',16),null);
            var now=new DateTime(2026,1,1,12,0,0,DateTimeKind.Utc);var plan=WindowsUpdates.Plan("pause","14",now);Assert(plan["PauseUpdatesExpiryTime"].Data=="2026-01-15T12:00:00Z"&&plan["PauseQualityUpdatesStartTime"].Data=="2026-01-01T12:00:00Z"&&plan.Count==6,"Pause plan incorrect");
            Assert(WindowsUpdates.Plan("resume","-",now).Values.All(v=>v.Data==null)&&WindowsUpdates.Plan("hours","9-21",now)["SmartActiveHoursState"].Data=="0","Resume or hours plan incorrect");
            var described=WindowsUpdates.Describe(new[]{new UpdateValue{Name="PauseUpdatesExpiryTime",Kind="string",Data=WindowsUpdates.Iso(DateTime.UtcNow.AddDays(3))},new UpdateValue{Name="ActiveHoursStart",Kind="dword",Data="9"},new UpdateValue{Name="ActiveHoursEnd",Kind="dword",Data="21"}});
            Assert(described.PausedUntil.HasValue&&described.ActiveStart==9&&described.ActiveEnd==21&&!WindowsUpdates.Describe(new[]{new UpdateValue{Name="PauseUpdatesExpiryTime",Kind="string",Data="2001-01-01T00:00:00Z"}}).PausedUntil.HasValue,"Update settings description incorrect");
        }
        private static void Boot(){
            var now=DateTime.UtcNow;var boot=BootPerformance.ParseBoot(string.Format(BootXml,42000,30000,12000),now);
            Assert(boot!=null&&boot.TotalMs==42000&&boot.MainPathMs==30000&&boot.PostBootMs==12000,"Boot event parsed incorrectly");
            Assert(BootPerformance.ParseBoot(string.Format(BootXml,0,0,0),now)==null&&BootPerformance.ParseBoot(string.Format(BootXml,"bad",1,1),now)==null,"Invalid boot event accepted");
            var delay=BootPerformance.ParseDelay(101,string.Format(DelayXml,"Updater Service",9000,4000),now);Assert(delay!=null&&delay.Kind=="Программа"&&delay.Name=="Updater Service"&&delay.DegradationMs==4000,"Delay event parsed incorrectly");
            Assert(BootPerformance.ParseDelay(101,string.Format(DelayXml,"",9000,4000),now).Name=="updater.exe"&&BootPerformance.ParseDelay(150,string.Format(DelayXml,"x",1,1),now)==null,"Delay name fallback or unknown event wrong");
            bool rejected=false;try{BootPerformance.Validate(new BootReport{Boots=new[]{new BootRecord{TimeUtc="bad",TotalMs=1}},Delays=new BootDelay[0]});}catch(IOException){rejected=true;}Assert(rejected,"Corrupt boot report accepted");
            var report=new BootReport{Boots=new[]{boot,new BootRecord{TimeUtc=now.AddDays(-1).ToString("o"),TotalMs=60000,MainPathMs=40000,PostBootMs=20000}},Delays=new[]{delay,BootPerformance.ParseDelay(101,string.Format(DelayXml,"Updater Service",8000,2000),now.AddDays(-1)),BootPerformance.ParseDelay(103,string.Format(DelayXml,"Print Spooler",500,300),now)}};
            var culprits=BootPerformance.Culprits(report);Assert(culprits.Length==2&&culprits[0].Title.StartsWith("Updater Service")&&culprits[0].Detail.Contains("2 раз")&&culprits[0].Detail.Contains("3,0 с"),"Boot culprits aggregated incorrectly: "+string.Join(" | ",culprits.Select(c=>c.Detail)));
            Assert(BootPerformance.Summary(report).Contains("42,0 с")&&BootPerformance.Summary(report).Contains("Среднее по 2")&&BootPerformance.Summary(new BootReport()).Contains("ещё не записала"),"Boot summary incorrect");
        }
        private static void Backup(){
            const string xml="<?xml version='1.0' encoding='utf-8'?><BatteryReport xmlns='http://schemas.microsoft.com/battery/2012'><Batteries><Battery><Id>DELL 1234</Id><Manufacturer>SMP</Manufacturer><DesignCapacity>60000</DesignCapacity><FullChargeCapacity>45000</FullChargeCapacity><CycleCount>310</CycleCount></Battery></Batteries></BatteryReport>";
            var batteries=Backups.ParseBatteries(xml);Assert(batteries.Length==1&&batteries[0].DesignMWh==60000&&Math.Abs(batteries[0].Health.Value-75)<0.01&&batteries[0].Cycles==310,"Battery report parsed incorrectly");
            Assert(Backups.Describe(batteries).Contains("75 %")&&Backups.Describe(batteries).Contains("310")&&Backups.Describe(new BatteryInfo[0]).Contains("не найдена"),"Battery description incorrect");
            bool rejected=false;try{Backups.Validate(new BackupResult{Folder=Path.Combine(Program.Home,"elsewhere")});}catch(IOException){rejected=true;}Assert(rejected,"Driver folder outside WintoolsData accepted");
            rejected=false;try{Backups.Run("format").GetAwaiter().GetResult();}catch(ArgumentException){rejected=true;}Assert(rejected,"Unknown backup action accepted");
        }
        private sealed class Sample {public string Id,TimeUtc;}
        private static void Records(){
            var store=new RecordStore("unit-test-records","тест",256);string a=Guid.NewGuid().ToString("N"),b=Guid.NewGuid().ToString("N");
            try{store.Save(Path.Combine(store.Folder,a+".json"),new Sample{Id=a,TimeUtc="2026-01-01T00:00:00.0000000Z"});store.Save(Path.Combine(store.Folder,b+".json"),new Sample{Id=b,TimeUtc="2026-02-01T00:00:00.0000000Z"});
                var all=store.All(id=>store.Load<Sample>(Path.Combine(store.Folder,id+".json")),r=>r.TimeUtc);Assert(all.Length==2&&all[0].Id==b&&all[1].Id==a,"Record store order or round trip failed");
                bool rejected=false;try{store.Save(Path.Combine(store.Folder,a+".json"),new Sample{Id=new string('x',300)});}catch(IOException){rejected=true;}Assert(rejected&&store.Load<Sample>(Path.Combine(store.Folder,a+".json")).Id==a,"Oversized record replaced the stored one");
                Assert(Directory.GetFiles(store.Folder,"*.tmp").Length==0,"Temporary record file left behind");
            }finally{if(Directory.Exists(store.Folder))Directory.Delete(store.Folder,true);}
        }
        private static void Usage(){
            string root=Path.Combine(Path.GetTempPath(),"wintools-usage-"+Guid.NewGuid().ToString("N"));
            try{Directory.CreateDirectory(Path.Combine(root,"big","nested"));Directory.CreateDirectory(Path.Combine(root,"small"));File.WriteAllBytes(Path.Combine(root,"big","a.bin"),new byte[3000]);File.WriteAllBytes(Path.Combine(root,"big","nested","b.bin"),new byte[2000]);File.WriteAllBytes(Path.Combine(root,"small","c.bin"),new byte[100]);File.WriteAllBytes(Path.Combine(root,"loose.bin"),new byte[50]);
                var report=DiskUsage.Measure(root,CancellationToken.None,1000);Assert(report.Folders.Length==3&&report.Folders[0].Name=="big"&&report.Folders[0].Bytes==5000&&report.Folders[0].Files==2&&report.Bytes==5150&&!report.Partial,"Folder sizes incorrect");
                Assert(DiskUsage.Measure(root,CancellationToken.None,1).Partial,"Limited scan reported complete");
            }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        private static void Masking(){
            string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);var masked=DiagnosticBundle.Mask("Path "+profile+"\\Desktop by "+Environment.UserName+" on "+Environment.MachineName+" S-1-5-21-1-2-3-1001");
            Assert(!masked.Contains(profile)&&masked.Contains("%USERPROFILE%\\Desktop")&&masked.Contains("<sid>")&&!masked.Contains("S-1-5-21-1-2-3"),"Diagnostics masking incomplete: "+masked);
        }
        internal static int Run(){
            CatalogueState();Dns();Hosts();Package();WindowsUpdate();Boot();Backup();
            Records();Usage();Masking();File.WriteAllText(Path.Combine(Program.Home,"portable-unit-tests.txt"),"Unit tests passed.");return 0;
        }
    }
}
