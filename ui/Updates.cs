using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    public sealed class ReleaseAsset { public string name; public string browser_download_url; public string digest; public long size; public long id; }
    public sealed class Release { public string tag_name; public string html_url; public bool draft; public bool prerelease; public ReleaseAsset[] assets; }
    internal sealed class Update { public Release Release; public ReleaseAsset Asset; }
    internal static class Updates {
        private const string Repo="https://github.com/Lucky2356/wintools/";
        private static int[] VersionParts(string value) {
            var match=Regex.Match(value??"","^v?(\\d+)\\.(\\d+)\\.(\\d+)(?:-rc\\.(\\d+))?$");if(!match.Success)return null;
            var parts=new int[5];for(int i=0;i<3;i++)if(!int.TryParse(match.Groups[i+1].Value,out parts[i]))return null;
            parts[3]=match.Groups[4].Success?0:1;if(match.Groups[4].Success&&!int.TryParse(match.Groups[4].Value,out parts[4]))return null;return parts;
        }
        internal static int Compare(string left,string right) {
            var a=VersionParts(left);var b=VersionParts(right);if(a==null||b==null)throw new FormatException("Unsupported release version.");
            for(int i=0;i<a.Length;i++){int order=a[i].CompareTo(b[i]);if(order!=0)return order;}return 0;
        }
        internal static Update Select(IEnumerable<Release> releases,string current,bool preview) {
            Update chosen=null;
            foreach(var release in releases) {
                if(release==null || release.draft || (release.prerelease && !preview) || !(release.tag_name??"").StartsWith("v") || VersionParts(release.tag_name)==null)continue;
                if(!preview && release.tag_name.Contains("-"))continue;
                if(Compare(release.tag_name,current)<=0)continue;
                var asset=(release.assets??new ReleaseAsset[0]).FirstOrDefault(a=>a!=null&&a.name=="WintoolsPortable.exe");
                if(asset==null || !Regex.IsMatch(asset.digest??"","^sha256:[a-fA-F0-9]{64}$") || asset.size<=0 || asset.size>50*1024*1024)continue;
                if(asset.browser_download_url!=Repo+"releases/download/"+release.tag_name+"/WintoolsPortable.exe")continue;
                if(release.html_url!=Repo+"releases/tag/"+release.tag_name)continue;
                if(chosen==null || Compare(release.tag_name,chosen.Release.tag_name)>0)chosen=new Update{Release=release,Asset=asset};
            }
            return chosen;
        }
        internal static HttpClient Client(string token) {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var client=new HttpClient{Timeout=TimeSpan.FromSeconds(90)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WintoolsPortable/"+Program.Version);
            client.DefaultRequestHeaders.Add("Accept","application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
            if(!string.IsNullOrWhiteSpace(token))client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.Trim());
            return client;
        }
        internal static async Task<Update> Check(bool preview) {
            // Public releases must not depend on a previously saved or expired token.
            using(var client=Client(null))return await Check(client,preview,Program.Version);
        }
        internal static async Task<Update> Check(HttpClient client,bool preview,string current) {
            using(var response=await client.GetAsync("https://api.github.com/repos/Lucky2356/wintools/releases?per_page=100")) {
                EnsureResponse(response);
                var json=await response.Content.ReadAsStringAsync();
                var releases=new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Deserialize<Release[]>(json);
                var list=releases??new Release[0];var result=Select(list,current,preview);
                if(result==null&&list.Any(r=>r!=null&&!r.draft&&(preview||!r.prerelease&&!((r.tag_name??"").Contains("-")))&&VersionParts(r.tag_name)!=null&&Compare(r.tag_name,current)>0))throw new IOException("Новый выпуск найден, но его EXE или контрольная сумма отсутствуют либо не прошли проверку. Откройте GitHub Releases или повторите позже.");
                return result;
            }
        }
        internal static void EnsureResponse(HttpResponseMessage response) {
            if(response.IsSuccessStatusCode)return;
            switch((int)response.StatusCode){
                case 404:throw new IOException("GitHub не нашёл публичный репозиторий или файл (404). Повторите проверку позже или откройте страницу выпусков.");
                case 401:throw new IOException("GitHub отклонил запрос (401). Повторите позже или откройте страницу выпусков. Для публичного репозитория токен не требуется.");
                case 403:case 429:throw new IOException("GitHub ограничил запрос ("+(int)response.StatusCode+"). Повторите позже: возможно, достигнут лимит запросов с вашего адреса.");
                default:throw new IOException("GitHub временно недоступен: HTTP "+(int)response.StatusCode+". Повторите проверку позже.");
            }
        }
        internal static bool HashMatches(string file,string expected) {
            if(!Regex.IsMatch(expected??"","^[a-fA-F0-9]{64}$"))return false;
            using(var hash=SHA256.Create())using(var stream=File.OpenRead(file)) {
                var actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
                return string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static async Task<string> Download(Update update,Action<int> progress) {
            using(var client=Client(null))return await Download(client,update,Program.Version,progress);
        }
        internal static async Task<string> Download(HttpClient client,Update update,string current,Action<int> progress) {
            var validated=Select(new[]{update.Release},current,true);
            if(validated==null || !object.ReferenceEquals(validated.Asset,update.Asset))throw new IOException("Метаданные обновления не прошли проверку.");
            var directory=Path.Combine(Program.Data,"updates",Guid.NewGuid().ToString("N"));
            Program.SafeDirectory(directory);Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,"next.exe");
            try {
                var url=update.Asset.id>0?"https://api.github.com/repos/Lucky2356/wintools/releases/assets/"+update.Asset.id:update.Asset.browser_download_url;
                if(update.Asset.id<=0 && client.DefaultRequestHeaders.Authorization!=null)throw new IOException("В релизе отсутствует идентификатор файла для авторизованного скачивания.");
                using(var request=new HttpRequestMessage(HttpMethod.Get,url)) {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead)) {
                    EnsureResponse(response);
                    using(var input=await response.Content.ReadAsStreamAsync())using(var output=File.Create(path)) {
                        var buffer=new byte[81920];long total=0;int count;
                        using(var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3))) {
                            while((count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0) {
                                total+=count;if(total>update.Asset.size)throw new IOException("Update exceeds declared size.");
                                await output.WriteAsync(buffer,0,count,timeout.Token);
                                if(progress!=null)progress((int)(total*100/update.Asset.size));
                            }
                        }
                        if(total!=update.Asset.size)throw new IOException("Incomplete update.");
                    }
                }
                }
                if(!HashMatches(path,update.Asset.digest.Substring(7)))throw new IOException("SHA-256 обновления не совпадает. Текущая версия сохранена.");
                return directory;
            } catch {if(File.Exists(path))File.Delete(path);throw;}
        }
        internal static void LaunchReplacement(string directory,string digest,bool relaunch=true) {
            if(!ValidExecutableName(Path.GetFileName(Program.Exe)))throw new IOException("Недопустимое имя EXE для обновления.");
            if(File.Exists(Path.Combine(Program.Data,"state","run.lock")))throw new IOException("Дождитесь завершения операции движка перед обновлением.");
            var updater=Path.Combine(directory,"updater.exe");
            File.Copy(Program.Exe,updater,false);
            var args="--replace "+Program.Quote(Path.GetFileName(Program.Exe))+" "+Process.GetCurrentProcess().Id+" "+digest+(relaunch?"":" --no-relaunch");
            Process.Start(new ProcessStartInfo(updater,args){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory});
        }
        internal static int Replace(string[] args) {
            bool noLaunch=args.Length==5 && (args[4]=="--no-relaunch" || args[4]=="--ci-no-launch" && Program.Hosted);
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
                        if(!parent.HasExited&&!string.Equals(parent.MainModule.FileName,destination,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected parent executable.");
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
                        for(int attempt=0;;attempt++)try{File.Replace(next,destination,destination+".previous");break;}catch(IOException ex){int code=ex.HResult&0xFFFF;if(attempt>=24||(code!=32&&code!=33))throw;Thread.Sleep(200);}
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
