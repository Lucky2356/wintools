using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class PowerChange {
        public string Schema,Id,Before,Target,BeforeName,TargetName,After,TimeUtc,Status,Error,Action;
    }
    internal static class PowerActions {
        private static string DirectoryPath {get{return Path.Combine(Program.Data,"power-history");}}
        private static string RecordPath(string id){if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new IOException("Некорректный номер изменения питания.");return Program.Under(DirectoryPath,id+".json");}
        private static void Save(PowerChange record){
            Program.SafeDirectory(DirectoryPath);Directory.CreateDirectory(DirectoryPath);string path=RecordPath(record.Id),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("История питания является ссылкой.");
            try{var bytes=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(record));if(bytes.Length>65536)throw new IOException("Запись питания слишком велика.");using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static PowerChange Read(string id){
            try{Program.SafeDirectory(DirectoryPath);string path=RecordPath(id);if(new FileInfo(path).Length>65536||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Некорректный файл истории питания.");var record=new JavaScriptSerializer().Deserialize<PowerChange>(File.ReadAllText(path));DateTime time;
                if(record==null||record.Schema!="wintools/power-change/1"||record.Id!=id||!PowerPlans.ValidId(record.Before)||!PowerPlans.ValidId(record.Target)||(record.After!=null&&!PowerPlans.ValidId(record.After))||!new[]{"select","restore"}.Contains(record.Action)||!new[]{"PENDING","OK","FAILED","REVERTED"}.Contains(record.Status)||!DateTime.TryParseExact(record.TimeUtc,"o",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out time))throw new IOException("Некорректная запись питания.");return record;
            }catch(ArgumentException ex){throw new IOException("Повреждена история питания.",ex);}catch(InvalidOperationException ex){throw new IOException("Повреждена история питания.",ex);}
        }
        internal static PowerChange[] History(){if(!Directory.Exists(DirectoryPath))return new PowerChange[0];Program.SafeDirectory(DirectoryPath);return Directory.GetFiles(DirectoryPath,"*.json").Select(p=>Read(Path.GetFileNameWithoutExtension(p))).OrderByDescending(r=>r.TimeUtc,StringComparer.Ordinal).ToArray();}
        internal static void Validate(string target,string expected,string restore){if(!PowerPlans.ValidId(target)||!PowerPlans.ValidId(expected))throw new ArgumentException("Некорректная схема питания.");if(restore!=null&&restore!="-")RecordPath(restore);}
        internal static async Task<EngineResult> Run(string target,string expected,string restore){
            Validate(target,expected,restore);string token=Guid.NewGuid().ToString("N"),sid=WindowsIdentity.GetCurrent().User.Value;
            var info=new ProcessStartInfo(Program.Exe,"--power-worker "+target+" "+expected+" "+token+" "+sid+" "+(restore??"-")){UseShellExecute=true,Verb="runas",WorkingDirectory=Program.Home,WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(info)){while(!process.HasExited)await Task.Delay(400);string path=Path.Combine(Program.Data,"runtime",token+".power.log");return new EngineResult{Code=process.ExitCode,Output=File.Exists(path)?File.ReadAllText(path):"Действие питания завершилось без отчёта."};}
        }
        internal static int Worker(string[] args){
            if(args.Length!=6||!Regex.IsMatch(args[3],"^[a-f0-9]{32}$")||args[4]!=WindowsIdentity.GetCurrent().User.Value)throw new ArgumentException("Запрос питания некорректен или права повышены под другим пользователем.");
            Validate(args[1],args[2],args[5]);string target=args[1].ToLowerInvariant(),expected=args[2].ToLowerInvariant(),id=args[3];
            if(!Directory.Exists(Program.Data))throw new IOException("Сначала запустите интерфейс Wintools.");Program.SafeDirectory(Program.Data);string runtime=Path.Combine(Program.Data,"runtime");Program.SafeDirectory(runtime);Directory.CreateDirectory(runtime);
            using(var log=new StreamWriter(new FileStream(Path.Combine(runtime,id+".power.log"),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false))){
                string lockPath=Path.Combine(Program.Data,"state","run.lock");Program.SafeDirectory(Path.GetDirectoryName(lockPath));FileStream gate=null;PowerChange record=null;
                try{gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);var snapshot=PowerPlans.Read();if(snapshot.Active!=expected)throw new IOException("Другая программа уже изменила схему питания. Обновите список и повторите выбор.");var next=snapshot.Plans.FirstOrDefault(p=>p.Id==target);if(next==null)throw new IOException("Выбранная схема больше недоступна на этом ПК.");
                    PowerChange original=null;if(args[5]!="-"){original=Read(args[5]);if(original.Before!=target||original.Action!="select"||original.Status=="REVERTED")throw new IOException("Запись не подходит для восстановления схемы.");}
                    if(target==snapshot.Active&&original==null){log.WriteLine("Эта схема уже используется. Изменений нет.");return 0;}
                    var before=snapshot.Plans.FirstOrDefault(p=>p.Id==snapshot.Active);record=new PowerChange{Schema="wintools/power-change/1",Id=id,Before=snapshot.Active,BeforeName=before==null?snapshot.Active:before.Name,Target=target,TargetName=next.Name,TimeUtc=DateTime.UtcNow.ToString("o"),Status="PENDING",Action=original==null?"select":"restore"};Save(record);
                    PowerPlans.Select(target);record.After=PowerPlans.Active();record.Status="OK";Save(record);if(original!=null){original.Status="REVERTED";Save(original);}log.WriteLine("Используется схема: "+next.Name+". Прежняя схема: "+record.BeforeName+". Запись истории: "+id);return 0;
                }catch(Exception ex){if(record!=null){record.Status="FAILED";record.Error=ex.Message;try{record.After=PowerPlans.Active();Save(record);}catch(Exception saveError){log.WriteLine("История требует проверки: "+saveError.Message);}}log.WriteLine("Не удалось завершить переключение: "+ex.Message+" Обновите текущее состояние.");return 4;}
                finally{if(gate!=null){gate.Dispose();File.Delete(lockPath);}}
            }
        }
    }
}
