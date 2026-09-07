using System;
using System.IO;
using System.Linq;

namespace Wintools {
    internal static class SelfTests {
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        private static void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,message);}
        internal static int Run() {
            Assert(Program.Hosted,"Hosted runner required.");
            var catalogue=Catalogue.Load();Assert(catalogue.Count>100,"Embedded catalogue incomplete.");
            Assert(catalogue.First(t=>t.Id=="UI-FILEEXT").Title.Contains("расшир"),"CP866 catalogue not decoded.");
            Assert(catalogue.Any(t=>t.Category=="CLEAN"),"Cleanup catalogue missing.");
            Reject(()=>Program.Under(Program.Data,"..\\escape"),"Path traversal accepted.");
            Reject(()=>Engine.Arguments("apply","UI-FILEEXT & whoami","-",false),"Injected ID accepted.");
            Reject(()=>Engine.Arguments("cleanup","UI-FILEEXT","-",false),"Mismatched operation accepted.");
            Reject(()=>Engine.Arguments("apply","-","-",false),"Bulk apply accepted.");
            Assert(Engine.Arguments("apply","UI-FILEEXT","-",false).Contains("/id:UI-FILEEXT"),"Selection lost.");
            Assert(Updates.Compare("v0.4.0","0.4.0-rc.2")>0,"Stable ordering wrong.");
            Assert(Updates.Compare("0.4.0-rc.10","0.4.0-rc.2")>0,"RC ordering wrong.");
            var asset=new ReleaseAsset{name="WintoolsPortable.exe",digest="sha256:"+new string('a',64),size=100,browser_download_url="https://github.com/Lucky2356/wintools/releases/download/v9.0.0-rc.1/WintoolsPortable.exe"};
            var release=new Release{tag_name="v9.0.0-rc.1",html_url="https://github.com/Lucky2356/wintools/releases/tag/v9.0.0-rc.1",prerelease=true,assets=new[]{asset}};
            Assert(Updates.Select(new[]{release},Program.Version,true)!=null,"Valid release rejected.");
            Assert(Updates.Select(new[]{release},Program.Version,false)==null,"RC leaked into stable channel.");
            Assert(Updates.Select(new[]{release},"10.0.0",true)==null,"Downgrade accepted.");
            asset.browser_download_url="https://example.com/WintoolsPortable.exe";
            Assert(Updates.Select(new[]{release},Program.Version,true)==null,"Untrusted update host accepted.");
            asset.browser_download_url="https://github.com/Lucky2356/wintools/releases/download/v9.0.0-rc.1/WintoolsPortable.exe";asset.digest=null;
            Assert(Updates.Select(new[]{release},Program.Version,true)==null,"Missing checksum accepted.");
            var marker=Path.Combine(Program.Data,"state","portable-test-marker.txt");Directory.CreateDirectory(Path.GetDirectoryName(marker));File.WriteAllText(marker,"preserve");
            Program.ExtractEngine();Assert(File.ReadAllText(marker)=="preserve","Extraction overwrote persistent state.");
            var preferences=new Preferences();preferences.AutoCheck=false;preferences.Favorites.Add("UI-FILEEXT");preferences.Save();
            Assert(!Preferences.Load().AutoCheck&&Preferences.Load().Favorites.Contains("UI-FILEEXT"),"Preferences were not retained.");
            var result=Engine.Run("diagnose","-","-",false,false,text=>{}).GetAwaiter().GetResult();
            Assert(result.Code==0,"Portable diagnostic worker failed: "+result.Output);
            Assert(Directory.GetFiles(Path.Combine(Program.Data,"reports"),"*.json").Length>0,"Diagnostic report missing.");
            File.WriteAllText(Path.Combine(Program.Home,"portable-tests.txt"),"Portable catalogue, worker, paths, persistent state, preferences and update policy passed.");
            return 0;
        }
    }
}
