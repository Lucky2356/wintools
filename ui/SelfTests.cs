using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Wintools {
    internal static class SelfTests {
        private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        private static void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,message);}
        internal static int Run() {
            Assert(Program.Hosted,"Hosted runner required.");
            var catalogue=Catalogue.Load();Assert(catalogue.Count>100,"Embedded catalogue incomplete.");
            Assert(catalogue.First(t=>t.Id=="UI-FILEEXT").Title.Contains("расшир"),"CP866 catalogue not decoded.");
            Assert(catalogue.Any(t=>t.Category=="CLEAN"),"Cleanup catalogue missing.");
            Assert(catalogue.All(t=>!string.IsNullOrWhiteSpace(t.Description)&&!string.IsNullOrWhiteSpace(t.Caveat)),"Result or caveat missing from catalogue.");
            Reject(()=>Program.Under(Program.Data,"..\\escape"),"Path traversal accepted.");
            Reject(()=>Engine.Arguments("apply","UI-FILEEXT & whoami","-",false),"Injected ID accepted.");
            Reject(()=>Engine.Arguments("cleanup","UI-FILEEXT","-",false),"Mismatched operation accepted.");
            Reject(()=>Engine.Arguments("apply","-","-",false),"Bulk apply accepted.");
            Reject(()=>Engine.Worker(new[]{"--worker","apply","UI-FILEEXT","-",new string('a',32),"normal","S-1-0-0"}),"Worker accepted another user identity.");
            Assert(Engine.Arguments("apply","UI-FILEEXT","-",false).Contains("/id:UI-FILEEXT"),"Selection lost.");
            Assert(Updates.Compare("v0.4.0","0.4.0-rc.2")>0,"Stable ordering wrong.");
            Assert(Updates.Compare("0.4.0-rc.10","0.4.0-rc.2")>0,"RC ordering wrong.");
            Assert(Updates.ValidExecutableName("Инструменты ПК.exe"),"Unicode executable name rejected.");
            Assert(!Updates.ValidExecutableName("..\\other.exe")&&!Updates.ValidExecutableName("other.exe:stream.exe"),"Unsafe executable name accepted.");
            var asset=new ReleaseAsset{name="WintoolsPortable.exe",digest="sha256:"+new string('a',64),size=100,browser_download_url="https://github.com/Lucky2356/wintools/releases/download/v9.0.0-rc.1/WintoolsPortable.exe"};
            var release=new Release{tag_name="v9.0.0-rc.1",html_url="https://github.com/Lucky2356/wintools/releases/tag/v9.0.0-rc.1",prerelease=true,assets=new[]{asset}};
            Assert(Updates.Select(new[]{release},Program.Version,true)!=null,"Valid release rejected.");
            Assert(Updates.Select(new[]{release},Program.Version,false)==null,"RC leaked into stable channel.");
            Assert(Updates.Select(new[]{release},"10.0.0",true)==null,"Downgrade accepted.");
            asset.browser_download_url="https://example.com/WintoolsPortable.exe";
            Assert(Updates.Select(new[]{release},Program.Version,true)==null,"Untrusted update host accepted.");
            asset.browser_download_url="https://github.com/Lucky2356/wintools/releases/download/v9.0.0-rc.1/WintoolsPortable.exe";asset.digest=null;
            Assert(Updates.Select(new[]{release},Program.Version,true)==null,"Missing checksum accepted.");
            Assert(Updates.Select(new Release[]{null,new Release{tag_name="v9999999999999999999999.0.0"}},Program.Version,true)==null,"Malformed release handling failed.");
            UpdateTests().GetAwaiter().GetResult();
            var marker=Path.Combine(Program.Data,"state","portable-test-marker.txt");Directory.CreateDirectory(Path.GetDirectoryName(marker));File.WriteAllText(marker,"preserve");
            Program.ExtractEngine();Assert(File.ReadAllText(marker)=="preserve","Extraction overwrote persistent state.");
            var preferences=new Preferences();preferences.AutoCheck=false;preferences.AutoInstall=false;preferences.Favorites.Add("UI-FILEEXT");preferences.Save();
            Assert(!Preferences.Load().AutoCheck&&!Preferences.Load().AutoInstall&&Preferences.Load().Favorites.Contains("UI-FILEEXT"),"Preferences were not retained.");
            var preferencePath=Path.Combine(Program.Data,"preferences.json");
            File.WriteAllText(preferencePath,"{\"Theme\":\"invalid\",\"Favorites\":null,\"AutoCheck\":false}");
            var repaired=Preferences.Load();Assert(repaired.Theme=="system"&&repaired.Favorites!=null&&!repaired.AutoCheck&&repaired.AutoInstall,"Malformed preferences or old-version defaults not normalized.");
            preferences.Save();
            var history=Path.Combine(Program.Data,"state","history-fixture.dat");File.WriteAllText(history,"run1|UI-FILEEXT|REG|a|b|c|d|e|f|OK|date\nrun1|UI-FILEEXT|REG|a|b|c|d|e|f|REVERTED|date");Assert(MainWindow.HistoryRows(history,catalogue).Length==1&&MainWindow.HistoryRows(history,catalogue)[0].CanRevert,"History grouping failed.");File.WriteAllText(history,"run1|UI-FILEEXT|REG|a|b|c|d|e|f|REVERTED|date");Assert(!MainWindow.HistoryRows(history,catalogue)[0].CanRevert,"Already reverted run enabled.");File.Delete(history);
            var result=Engine.Run("diagnose","-","-",false,false,text=>{}).GetAwaiter().GetResult();
            Assert(result.Code==0,"Portable diagnostic worker failed: "+result.Output);
            Assert(Directory.GetFiles(Path.Combine(Program.Data,"reports"),"*.json").Length>0,"Diagnostic report missing.");
            File.WriteAllText(Path.Combine(Program.Home,"portable-tests.txt"),"Portable catalogue, worker, paths, persistent state, preferences and update policy passed.");
            return 0;
        }
        private sealed class ResponseHandler:HttpMessageHandler {
            internal HttpStatusCode Status;
            internal string Json;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Json??"[]")});}
        }
        private static async Task UpdateTests() {
            foreach(var status in new[]{HttpStatusCode.NotFound,HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden})using(var client=new HttpClient(new ResponseHandler{Status=status})){bool failed=false;try{await Updates.Check(client,true,"0.0.0");}catch(IOException ex){failed=ex.Message.Contains(((int)status).ToString());}Assert(failed,"HTTP access error was hidden.");}
            using(var client=new HttpClient(new ResponseHandler{Status=HttpStatusCode.OK,Json="[{\"tag_name\":\"v9.0.0\",\"assets\":[]}]"})){bool failed=false;try{await Updates.Check(client,true,"0.0.0");}catch(IOException){failed=true;}Assert(failed,"Missing release binary reported as up to date.");}
            using(var client=Updates.Client(null)){Assert(client.DefaultRequestHeaders.Authorization==null,"Public client unexpectedly authenticated");var update=await Updates.Check(client,true,"0.0.0");Assert(update!=null&&update.Asset.id>0,"Real public release lookup failed.");var directory=await Updates.Download(client,update,"0.0.0",null);Assert(Updates.HashMatches(Path.Combine(directory,"next.exe"),update.Asset.digest.Substring(7)),"Real anonymous download failed.");}
        }
    }
}
