using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Wintools {
    internal sealed class EngineResult { public int Code; public string Output; }
    internal static class Engine {
        internal static string Arguments(string verb,string id,string run,bool restorePoint) {
            if (!new[]{"apply","revert","status","verify","diagnose","cleanup","system-change"}.Contains(verb)) throw new ArgumentException("Unknown operation.");
            var selected= id=="-" ? null : Catalogue.Load().FirstOrDefault(t=>t.Id==id);
            if(id!="-" && selected==null) throw new ArgumentException("Unknown catalogue ID.");
            if(new[]{"apply","cleanup","system-change"}.Contains(verb) && (selected==null || selected.Verb!=verb)) throw new ArgumentException("Operation and selection do not match.");
            if(run!="-" && (!Regex.IsMatch(run,"^[A-Za-z0-9_.-]{1,100}$") || verb!="revert")) throw new ArgumentException("Invalid run.");
            string flags=" /yes";
            if(id!="-") flags+=" /id:"+id;
            if(run!="-") flags+=" /run:"+run;
            if(selected!=null && selected.Risk=="high") flags+=" /include-risky";
            if(id=="CLN-RECYCLE") flags+=" /include-recycle";
            flags+=restorePoint?" /restore-point":" /no-restore-point";
            return verb+flags;
        }
        internal static async Task<EngineResult> Run(string verb,string id,string run,bool dry,bool restorePoint,Action<string> progress) {
            // The worker validates independently after UAC; no shell commands come from UI text.
            Arguments(verb,id,run,restorePoint);
            Program.SafeDirectory(Program.Data);
            var token=Guid.NewGuid().ToString("N");
            var directory=Path.Combine(Program.Data,"runtime");
            Program.SafeDirectory(directory);
            Directory.CreateDirectory(directory);
            var log=Path.Combine(directory,token+".log");
            var arguments="--worker "+verb+" "+id+" "+run+" "+token+" "+(dry?"dry":restorePoint?"restore":"normal");
            bool needsAdmin=verb!="diagnose" && verb!="status";
            bool admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            var info=new ProcessStartInfo(Program.Exe,arguments){WorkingDirectory=Program.Home};
            if(needsAdmin && !admin){info.UseShellExecute=true;info.Verb="runas";info.WindowStyle=ProcessWindowStyle.Hidden;}
            else {info.UseShellExecute=false;info.CreateNoWindow=true;}
            using(var process=Process.Start(info)) {
                while(!process.HasExited) {
                    await Task.Delay(400);
                    if(File.Exists(log)) progress(ReadLog(log));
                }
                return new EngineResult{Code=process.ExitCode,Output=File.Exists(log)?ReadLog(log):"Операция завершилась без вывода."};
            }
        }
        private static string ReadLog(string path) {
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var reader=new StreamReader(file,Encoding.UTF8)) {
                var text=reader.ReadToEnd();
                return text.Length>150000?text.Substring(text.Length-150000):text;
            }
        }
        internal static int Worker(string[] args) {
            if(args.Length!=6 || !Regex.IsMatch(args[4],"^[a-f0-9]{32}$") || !new[]{"dry","restore","normal"}.Contains(args[5])) throw new ArgumentException("Invalid worker request.");
            Program.SafeDirectory(Program.Data);
            var command=Arguments(args[1],args[2],args[3],args[5]=="restore");
            if(args[5]=="dry") command+=" /dry";
            var output=Path.Combine(Program.Data,"runtime",args[4]+".log");
            Program.SafeDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var engine=Path.Combine(Program.Data,"wintweaks.cmd");
            var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),"/d /s /c \"\""+engine+"\" "+command+"\"") {
                WorkingDirectory=Program.Data,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardOutputEncoding=Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                StandardErrorEncoding=Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            };
            using(var log=new StreamWriter(new FileStream(output,FileMode.Create,FileAccess.Write,FileShare.ReadWrite),new UTF8Encoding(false)))
            using(var process=new Process{StartInfo=info}) {
                var sync=new object();
                DataReceivedEventHandler append=(sender,e)=>{if(e.Data!=null) lock(sync){log.WriteLine(e.Data);log.Flush();}};
                process.OutputDataReceived+=append;process.ErrorDataReceived+=append;
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();
                return process.ExitCode;
            }
        }
    }
}
