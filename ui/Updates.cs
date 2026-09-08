using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    public sealed class ReleaseAsset { public string name; public string browser_download_url; public string digest; public long size; }
    public sealed class Release { public string tag_name; public string html_url; public bool draft; public bool prerelease; public ReleaseAsset[] assets; }
    internal sealed class Update { public Release Release; public ReleaseAsset Asset; }
    internal static class Updates {
        private const string Repo="https://github.com/Lucky2356/wintools/";
        internal static int Compare(string left,string right) {
            var pattern="^v?(\\d+)\\.(\\d+)\\.(\\d+)(?:-rc\\.(\\d+))?$";
            var a=Regex.Match(left,pattern);var b=Regex.Match(right,pattern);
            if(!a.Success || !b.Success) throw new FormatException("Unsupported release version.");
            for(int i=1;i<=3;i++){int n=int.Parse(a.Groups[i].Value).CompareTo(int.Parse(b.Groups[i].Value));if(n!=0)return n;}
            if(a.Groups[4].Success!=b.Groups[4].Success)return a.Groups[4].Success?-1:1;
            return a.Groups[4].Success?int.Parse(a.Groups[4].Value).CompareTo(int.Parse(b.Groups[4].Value)):0;
        }
        internal static Update Select(IEnumerable<Release> releases,string current,bool preview) {
            Update chosen=null;
            foreach(var release in releases) {
                if(release.draft || (release.prerelease && !preview) || !Regex.IsMatch(release.tag_name??"","^v\\d+\\.\\d+\\.\\d+(-rc\\.\\d+)?$"))continue;
                if(!preview && release.tag_name.Contains("-"))continue;
                if(Compare(release.tag_name,current)<=0)continue;
                var asset=(release.assets??new ReleaseAsset[0]).FirstOrDefault(a=>a.name=="WintoolsPortable.exe");
                if(asset==null || !Regex.IsMatch(asset.digest??"","^sha256:[a-fA-F0-9]{64}$") || asset.size<=0 || asset.size>50*1024*1024)continue;
                if(asset.browser_download_url!=Repo+"releases/download/"+release.tag_name+"/WintoolsPortable.exe")continue;
                if(release.html_url!=Repo+"releases/tag/"+release.tag_name)continue;
                if(chosen==null || Compare(release.tag_name,chosen.Release.tag_name)>0)chosen=new Update{Release=release,Asset=asset};
            }
            return chosen;
        }
        private static HttpClient Client() {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var client=new HttpClient{Timeout=TimeSpan.FromSeconds(90)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WintoolsPortable/"+Program.Version);
            client.DefaultRequestHeaders.Add("Accept","application/vnd.github+json");
            return client;
        }
        internal static async Task<Update> Check(bool preview) {
            using(var client=Client()) {
                var json=await client.GetStringAsync("https://api.github.com/repos/Lucky2356/wintools/releases?per_page=30");
                var releases=new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Deserialize<Release[]>(json);
                return Select(releases,Program.Version,preview);
            }
        }
        internal static bool HashMatches(string file,string expected) {
            if(!Regex.IsMatch(expected??"","^[a-fA-F0-9]{64}$"))return false;
            using(var hash=SHA256.Create())using(var stream=File.OpenRead(file)) {
                var actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
                return string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static async Task<string> Download(Update update) {
            if(Select(new[]{update.Release},Program.Version,true)==null)throw new IOException("Update metadata rejected.");
            var directory=Path.Combine(Program.Data,"updates",Guid.NewGuid().ToString("N"));
            Program.SafeDirectory(directory);Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,"next.exe");
            try {
                using(var client=Client())
                using(var response=await client.GetAsync(update.Asset.browser_download_url,HttpCompletionOption.ResponseHeadersRead)) {
                    response.EnsureSuccessStatusCode();
                    using(var input=await response.Content.ReadAsStreamAsync())using(var output=File.Create(path)) {
                        var buffer=new byte[81920];long total=0;int count;
                        using(var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3))) {
                            while((count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0) {
                                total+=count;if(total>update.Asset.size)throw new IOException("Update exceeds declared size.");
                                await output.WriteAsync(buffer,0,count,timeout.Token);
                            }
                        }
                        if(total!=update.Asset.size)throw new IOException("Incomplete update.");
                    }
                }
                if(!HashMatches(path,update.Asset.digest.Substring(7)))throw new IOException("SHA-256 обновления не совпадает. Текущая версия сохранена.");
                return directory;
            } catch {if(File.Exists(path))File.Delete(path);throw;}
        }
        internal static void LaunchReplacement(string directory,string digest) {
            if(!ValidExecutableName(Path.GetFileName(Program.Exe)))throw new IOException("Недопустимое имя EXE для обновления.");
            if(File.Exists(Path.Combine(Program.Data,"state","run.lock")))throw new IOException("Дождитесь завершения операции движка перед обновлением.");
            var updater=Path.Combine(directory,"updater.exe");
            File.Copy(Program.Exe,updater,false);
            var args="--replace "+Program.Quote(Path.GetFileName(Program.Exe))+" "+Process.GetCurrentProcess().Id+" "+digest;
            Process.Start(new ProcessStartInfo(updater,args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory});
        }
        internal static int Replace(string[] args) {
            bool noLaunch=args.Length==5 && args[4]=="--ci-no-launch" && Program.Hosted;
            if((args.Length!=4&&!noLaunch) || !ValidExecutableName(args[1]) || !Regex.IsMatch(args[3],"^[a-fA-F0-9]{64}$"))throw new ArgumentException("Invalid updater arguments.");
            var folder=new DirectoryInfo(Program.Home);
            if(!Regex.IsMatch(folder.Name,"^[a-f0-9]{32}$") || folder.Parent.Name!="updates" || folder.Parent.Parent.Name!="WintoolsData")throw new IOException("Invalid staging directory.");
            string home=folder.Parent.Parent.Parent.FullName;
            Program.SafeDirectory(home);
            var destination=Program.Under(home,args[1]);
            var next=Path.Combine(folder.FullName,"next.exe");
            try {
                int pid;
                if(!int.TryParse(args[2],out pid))throw new ArgumentException("Invalid parent PID.");
                try {
                    using(var parent=Process.GetProcessById(pid)) {
                        if(!string.Equals(parent.MainModule.FileName,destination,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected parent executable.");
                        if(!parent.WaitForExit(60000))throw new IOException("Приложение ещё работает; обновление не установлено.");
                    }
                } catch(ArgumentException) { }
                using(var gate=new Mutex(false,Program.MutexName(home))) {
                    bool acquired;try{acquired=gate.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                    if(!acquired)throw new IOException("Приложение снова запущено; обновление отложено.");
                    try {
                        if(File.Exists(Path.Combine(home,"WintoolsData","state","run.lock")))throw new IOException("Engine is busy.");
                        if(!HashMatches(next,args[3]))throw new IOException("Update hash mismatch.");
                        if((File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked executable.");
                        File.Replace(next,destination,destination+".previous");
                    }finally{gate.ReleaseMutex();}
                }
                if(!noLaunch)Process.Start(new ProcessStartInfo(destination){UseShellExecute=true,WorkingDirectory=home});
                return 0;
            } catch(Exception ex) {
                File.WriteAllText(Path.Combine(home,"WintoolsData","update-error.txt"),ex.ToString());
                if(!Program.Hosted)System.Windows.Forms.MessageBox.Show("Не удалось обновить программу. Подробности: WintoolsData\\update-error.txt\n"+ex.Message,"Wintools");
                return 4;
            }
        }
        internal static bool ValidExecutableName(string name) {
            return !string.IsNullOrWhiteSpace(name) && name==Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars())<0 && !name.Contains("\\") && !name.Contains("/") && name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase);
        }
    }
}
