using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class IntegrityRecord {public string Schema,Id,Action,TimeUtc,State,Summary;}
    internal static class IntegrityActions {
        internal static bool IsRepair(string action){return action=="dism-repair"||action=="sfc-repair"||action=="windows-repair";}
        internal static bool CanCancel(string action){return action.StartsWith("dism-");}
        internal static string Title(string action){if(action=="windows-repair")return "Восстановление Windows: DISM и SFC";if(action=="dism-repair")return "Восстановление компонентов Windows";if(action=="sfc-repair")return "Восстановление системных файлов";if(action=="dism-scan")return "Проверка компонентов Windows";if(action=="dism-status")return "Статус прошлой проверки компонентов";if(action=="sfc-verify")return "Проверка системных файлов";throw new ArgumentException("Неизвестная проверка Windows.");}
        private static string DirectoryPath {get{return Path.Combine(Program.Data,"integrity-history");}}
        private static void ValidateId(string id){if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new IOException("Некорректный номер проверки.");}
        private static string RecordPath(string id){ValidateId(id);return Program.Under(DirectoryPath,id+".json");}
        private static string LogPath(string id){ValidateId(id);return Program.Under(Path.Combine(Program.Data,"runtime"),id+".integrity.log");}
        private static string EventName(string id){ValidateId(id);return "Local\\WintoolsIntegrity-"+id;}
        private static void Save(IntegrityRecord record){
            Program.SafeDirectory(DirectoryPath);Directory.CreateDirectory(DirectoryPath);string path=RecordPath(record.Id),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Отчёт является ссылкой.");
            try{var bytes=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(record));if(bytes.Length>65536)throw new IOException("Отчёт слишком велик.");using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static IntegrityRecord Read(string id){
            try{Program.SafeDirectory(DirectoryPath);string path=RecordPath(id);if(new FileInfo(path).Length>65536||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Некорректный файл проверки.");var record=new JavaScriptSerializer().Deserialize<IntegrityRecord>(File.ReadAllText(path));DateTime time;
                if(record==null||record.Schema!="wintools/integrity/1"||record.Id!=id||!new[]{"pending","healthy","repaired","not-marked","repairable","unrepairable","issues","review","failed","cancelled"}.Contains(record.State)||!DateTime.TryParseExact(record.TimeUtc,"o",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out time))throw new IOException("Повреждена запись проверки.");Title(record.Action);return record;
            }catch(ArgumentException ex){throw new IOException("Повреждён отчёт проверки.",ex);}catch(InvalidOperationException ex){throw new IOException("Повреждён отчёт проверки.",ex);}
        }
        internal static IntegrityRecord[] History(){if(!Directory.Exists(DirectoryPath))return new IntegrityRecord[0];Program.SafeDirectory(DirectoryPath);return Directory.GetFiles(DirectoryPath,"*.json").Select(p=>Read(Path.GetFileNameWithoutExtension(p))).OrderByDescending(r=>r.TimeUtc,StringComparer.Ordinal).ToArray();}
        internal static string ReadLog(string id){string path=LogPath(id);Program.SafeDirectory(Path.GetDirectoryName(path));if(!File.Exists(path))return "";if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Журнал проверки является ссылкой.");using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){if(file.Length>600000)file.Seek(-600000,SeekOrigin.End);using(var reader=new StreamReader(file,Encoding.UTF8)){string value=reader.ReadToEnd();return value.Length>150000?value.Substring(value.Length-150000):value;}}}
        internal static string Report(string id){var record=Read(id);return Title(record.Action)+"\n"+DateTime.Parse(record.TimeUtc,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g")+"\n\n"+record.Summary+"\n\n"+CleanLog(ReadLog(id));}
        internal static string CleanLog(string value){return string.Join(Environment.NewLine,(value??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Where(line=>!line.StartsWith("@progress|")));}
        internal static async Task<EngineResult> Run(string action,Action<string> progress,Action<Action> cancelReady){
            Title(action);string id=Guid.NewGuid().ToString("N"),sid=WindowsIdentity.GetCurrent().User.Value;bool created;
            using(var cancel=new EventWaitHandle(false,EventResetMode.ManualReset,EventName(id),out created)){
                if(!created)throw new IOException("Не удалось создать независимую проверку.");cancelReady(CanCancel(action)?(Action)(()=>cancel.Set()):null);
                try{var info=new ProcessStartInfo(Program.Exe,"--integrity-worker "+action+" "+id+" "+sid){UseShellExecute=true,Verb="runas",WorkingDirectory=Program.Home,WindowStyle=ProcessWindowStyle.Hidden};using(var process=Process.Start(info)){while(!process.HasExited){await Task.Delay(400);try{progress(ReadLog(id));}catch(IOException){}catch(UnauthorizedAccessException){}}string report;try{report=Report(id);}catch(IOException){report="Проверка завершилась без полного отчёта.\n"+CleanLog(ReadLog(id));}return new EngineResult{Code=process.ExitCode,Output=report};}}
                finally{cancelReady(null);}
            }
        }
        internal static int Worker(string[] args){
            if(args.Length!=4||args[3]!=WindowsIdentity.GetCurrent().User.Value)throw new ArgumentException("Запрос проверки некорректен или права повышены под другим пользователем.");string action=args[1],id=args[2];Title(action);ValidateId(id);if(!Directory.Exists(Program.Data))throw new IOException("Сначала откройте интерфейс Wintools.");Program.SafeDirectory(Program.Data);string runtime=Path.Combine(Program.Data,"runtime");Program.SafeDirectory(runtime);Directory.CreateDirectory(runtime);
            using(var cancel=EventWaitHandle.OpenExisting(EventName(id)))using(var log=new StreamWriter(new FileStream(LogPath(id),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false))){
                string lockPath=Path.Combine(Program.Data,"state","run.lock");Program.SafeDirectory(Path.GetDirectoryName(lockPath));FileStream gate=null;IntegrityRecord record=null;bool restorePoint=false;
                try{gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);record=new IntegrityRecord{Schema="wintools/integrity/1",Id=id,Action=action,TimeUtc=DateTime.UtcNow.ToString("o"),State="pending",Summary="Обслуживание начато. Итог ещё не получен."};Save(record);log.WriteLine(Title(action));log.Flush();var sync=new object();Action<string> append=value=>{lock(sync){log.WriteLine(value);log.Flush();}};
                    if(cancel.WaitOne(0))throw new OperationCanceledException("Обслуживание отменено до начала работы.");
                    if(IsRepair(action)&&Preferences.Load().RestorePoint){append("Запрашиваем точку восстановления Windows…");restorePoint=WindowsIntegrity.RestorePointEvent(100,append);}
                    if(cancel.WaitOne(0))throw new OperationCanceledException("Обслуживание отменено до исправления файлов.");
                    IntegrityResult result;
                    if(action=="windows-repair")result=WindowsIntegrity.RepairWindows(()=>WindowsIntegrity.RepairComponents(cancel,append),()=>WindowsIntegrity.RepairFiles(append,false),append);
                    else if(action=="dism-repair")result=WindowsIntegrity.RepairComponents(cancel,append);
                    else if(action=="sfc-repair")result=WindowsIntegrity.RepairFiles(append,false);
                    else result=action=="sfc-verify"?WindowsIntegrity.CheckFiles(append,false):WindowsIntegrity.CheckComponents(action=="dism-scan",cancel,append);
                    record.State=result.State;record.Summary=result.Summary;Save(record);append(result.Summary);return record.State=="failed"?4:0;
                }catch(Exception ex){if(record!=null){record.State=ex is OperationCanceledException?"cancelled":"failed";record.Summary=ex.Message;try{Save(record);}catch(Exception saveError){log.WriteLine("Не удалось сохранить итог: "+saveError.Message);}}log.WriteLine(ex.Message);return ex is OperationCanceledException?2:4;}
                finally{try{if(restorePoint)WindowsIntegrity.RestorePointEvent(101,value=>log.WriteLine(value));}finally{if(gate!=null){gate.Dispose();File.Delete(lockPath);}}}
            }
        }
        internal static string CreateEventNameForTest(string id){if(!Program.Hosted)throw new InvalidOperationException("Hosted CI only.");return EventName(id);}
    }
}
