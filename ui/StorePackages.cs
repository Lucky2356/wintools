using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class AppEntry {public string Name {get;set;} public string Id {get;set;}}
    internal sealed class StorePackage {
        public string FullName {get;set;} public string FamilyName {get;set;} public string Name {get;set;} public string Version {get;set;} public string Publisher {get;set;} public string Location {get;set;}
        public bool Protected {get;set;} public bool ResetSupported {get;set;}
        public AppEntry[] Entries {get;set;}
    }
    internal sealed class StoreInventory {public StorePackage[] Rows {get;set;} public string[] Errors {get;set;}}
    internal static class StorePackages {
        internal static bool Identity(string value){return value!=null&&Regex.IsMatch(value,@"\A[A-Za-z0-9._-]{1,255}\z");}
        internal static bool EntryValid(StorePackage package,AppEntry entry){return entry!=null&&entry.Id!=null&&entry.Id.StartsWith(package.FamilyName+"!",StringComparison.Ordinal)&&Regex.IsMatch(entry.Id,@"\A[A-Za-z0-9._-]+![A-Za-z0-9._-]{1,128}\z");}
        internal static void Validate(StorePackage package,string action){
            if(package==null||!Identity(package.FullName)||!Identity(package.FamilyName))throw new IOException("Некорректный идентификатор пакета.");
            if(!new[]{"remove","reset","register"}.Contains(action))throw new IOException("Неизвестное действие пакета.");
            if(package.Protected)throw new IOException("Системный или защищённый пакет нельзя изменить этим действием.");
            if(action=="reset"&&!package.ResetSupported)throw new IOException("Эта версия Windows не поддерживает сброс через этот менеджер.");
        }
        internal static async Task<EngineResult> Script(object request,bool readOnly){
            string script;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Wintools.StoreApps.ps1"))using(var reader=new StreamReader(stream))script=reader.ReadToEnd();
            string data=Convert.ToBase64String(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(request)));
            string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes("$requestData='"+data+"'\n"+script));
            var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoLogo -NoProfile -NonInteractive -EncodedCommand "+encoded){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            using(var process=Process.Start(info)){
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
                while(!process.HasExited){if(readOnly&&timer.Elapsed.TotalSeconds>30){try{process.Kill();}catch(InvalidOperationException){}throw new IOException("Чтение Store-приложений заняло слишком много времени. Повторите обновление списка.");}await Task.Delay(150);}
                string text=await output,detail=await error;if(text.Length>4194304||detail.Length>150000)throw new IOException("Ответ Windows слишком велик.");
                return new EngineResult{Code=process.ExitCode,Output=process.ExitCode==0?text:detail};
            }
        }
        internal static async Task<StoreInventory> Read(){
            var result=await Script(new{Action="inventory"},true);if(result.Code!=0)throw new IOException(result.Output);
            var snapshot=new JavaScriptSerializer{MaxJsonLength=4194304}.Deserialize<StoreInventory>(result.Output);
            if(snapshot==null||snapshot.Rows==null||snapshot.Errors==null||snapshot.Rows.Any(p=>p==null||!Identity(p.FullName)||!Identity(p.FamilyName)||p.Entries==null||p.Entries.Any(e=>!EntryValid(p,e))))throw new IOException("Некорректный список пакетов Windows.");
            return snapshot;
        }
        internal static InstalledApplication Row(StorePackage package){return new InstalledApplication{Name=package.Name,Publisher=package.Publisher,Version=package.Version,Location=package.Location,Key=package.FullName,CanRemove=!package.Protected,Package=package};}
    }
}
