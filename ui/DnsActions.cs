using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class DnsChange {
        public string Schema,Id,Adapter,AdapterName,Target,TargetName,TimeUtc,Status,Error,Action;
        public string[] Before4,Before6,After4,After6;
    }
    internal static class DnsActions {
        private static readonly RecordStore Store=new RecordStore("dns-history","DNS",65536);
        private static string DirectoryPath {get{return Store.Folder;}}
        private static string RecordPath(string id){if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new IOException("Некорректный номер изменения DNS.");return Program.Under(DirectoryPath,id+".json");}
        private static void Save(DnsChange record){Store.Save(RecordPath(record.Id),record);}
        private static bool ValidList(string[] values,AddressFamily family,bool optional){if(values==null)return optional;try{return DnsSettings.Normalize(values,family).SequenceEqual(values);}catch(ArgumentException){return false;}}
        internal static DnsChange Read(string id){
            try{var record=Store.Load<DnsChange>(RecordPath(id));DateTime time;
                if(record==null||record.Schema!="wintools/dns-change/1"||record.Id!=id||!DnsSettings.ValidAdapter(record.Adapter)||string.IsNullOrEmpty(record.AdapterName)||string.IsNullOrEmpty(record.TargetName)||!ValidList(record.Before4,AddressFamily.InterNetwork,false)||!ValidList(record.Before6,AddressFamily.InterNetworkV6,false)||!ValidList(record.After4,AddressFamily.InterNetwork,true)||!ValidList(record.After6,AddressFamily.InterNetworkV6,true)||!new[]{"select","restore"}.Contains(record.Action)||!new[]{"PENDING","OK","FAILED","REVERTED"}.Contains(record.Status)||!DateTime.TryParseExact(record.TimeUtc,"o",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out time))throw new IOException("Некорректная запись DNS.");return record;
            }catch(ArgumentException ex){throw new IOException("Повреждена история DNS.",ex);}catch(InvalidOperationException ex){throw new IOException("Повреждена история DNS.",ex);}
        }
        internal static DnsChange[] History(){return Store.All(Read,r=>r.TimeUtc);}
        internal static void Validate(string adapter,string target,string expected,string restore){
            if(!DnsSettings.ValidAdapter(adapter)||!Regex.IsMatch(expected??"","^[a-f0-9]{16}$"))throw new ArgumentException("Некорректный запрос DNS.");
            if(target=="restore"){if(restore==null||restore=="-")throw new ArgumentException("Не указана запись для возврата DNS.");RecordPath(restore);}
            else if(DnsSettings.Provider(target)==null||(restore!=null&&restore!="-"))throw new ArgumentException("Неизвестный набор DNS-серверов.");
        }
        internal static async Task<EngineResult> Run(string adapter,string target,string expected,string restore){
            Validate(adapter,target,expected,restore);string token=Guid.NewGuid().ToString("N"),sid=WindowsIdentity.GetCurrent().User.Value;
            var info=new ProcessStartInfo(Program.Exe,"--dns-worker "+adapter+" "+target+" "+expected+" "+token+" "+sid+" "+(restore??"-")){UseShellExecute=true,Verb="runas",WorkingDirectory=Program.Home,WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(info)){while(!process.HasExited)await Task.Delay(400);string path=Path.Combine(Program.Data,"runtime",token+".dns.log");return new EngineResult{Code=process.ExitCode,Output=File.Exists(path)?File.ReadAllText(path):"Изменение DNS завершилось без отчёта."};}
        }
        internal static int Worker(string[] args){
            if(args.Length!=7||!Regex.IsMatch(args[4],"^[a-f0-9]{32}$")||args[5]!=WindowsIdentity.GetCurrent().User.Value)throw new ArgumentException("Запрос DNS некорректен или права повышены под другим пользователем.");
            Validate(args[1],args[2],args[3],args[6]);string adapterId=args[1],target=args[2],expected=args[3],id=args[4];
            if(!Directory.Exists(Program.Data))throw new IOException("Сначала запустите интерфейс Wintools.");Program.SafeDirectory(Program.Data);string runtime=Path.Combine(Program.Data,"runtime");Program.SafeDirectory(runtime);Directory.CreateDirectory(runtime);
            using(var log=new StreamWriter(new FileStream(Path.Combine(runtime,id+".dns.log"),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false))){
                string lockPath=Path.Combine(Program.Data,"state","run.lock");Program.SafeDirectory(Path.GetDirectoryName(lockPath));FileStream gate=null;DnsChange record=null;
                try{gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);var adapter=DnsSettings.Find(adapterId);
                    if(adapter.Fingerprint!=expected)throw new IOException("DNS этого адаптера изменился после чтения. Обновите состояние и повторите выбор.");
                    DnsChange original=null;string[] v4,v6;string name;
                    if(target=="restore"){original=Read(args[6]);if(original.Action!="select"||original.Status=="REVERTED"||!string.Equals(original.Adapter,adapter.Id,StringComparison.OrdinalIgnoreCase))throw new IOException("Запись не подходит для возврата DNS этого адаптера.");
                        if(original.After4==null||original.After6==null||DnsSettings.Fingerprint(original.After4,original.After6)!=adapter.Fingerprint)throw new IOException("DNS изменён после этой записи вручную или другой программой. Возврат отменён, чтобы не перезаписать более позднюю настройку.");
                        v4=original.Before4;v6=original.Before6;name="прежние адреса";}
                    else{var provider=DnsSettings.Provider(target);v4=provider.V4;v6=provider.V6;name=provider.Name;}
                    if(original==null&&adapter.Static4.SequenceEqual(DnsSettings.Normalize(v4,AddressFamily.InterNetwork))&&(adapter.Index6==0||adapter.Static6.SequenceEqual(DnsSettings.Normalize(v6,AddressFamily.InterNetworkV6)))){log.WriteLine("Этот DNS уже используется. Изменений нет.");return 0;}
                    record=new DnsChange{Schema="wintools/dns-change/1",Id=id,Adapter=adapter.Id,AdapterName=adapter.Name,Before4=adapter.Static4,Before6=adapter.Static6,Target=target,TargetName=original==null?name:original.TargetName,TimeUtc=DateTime.UtcNow.ToString("o"),Status="PENDING",Action=original==null?"select":"restore"};Save(record);
                    DnsSettings.Apply(adapter,v4,v6,log);var after=DnsSettings.Find(adapter.Id);record.After4=after.Static4;record.After6=after.Static6;record.Status="OK";Save(record);if(original!=null){original.Status="REVERTED";Save(original);}
                    log.WriteLine("Адаптер "+adapter.Name+": DNS "+after.Summary+". Запись истории: "+id);return 0;
                }catch(Exception ex){if(record!=null){record.Status="FAILED";record.Error=ex.Message;try{var after=DnsSettings.Find(adapterId);record.After4=after.Static4;record.After6=after.Static6;}catch(Exception){}try{Save(record);}catch(Exception saveError){log.WriteLine("История требует проверки: "+saveError.Message);}}log.WriteLine("Не удалось изменить DNS: "+ex.Message+" Обновите текущее состояние.");return 4;}
                finally{if(gate!=null){gate.Dispose();File.Delete(lockPath);}}
            }
        }
    }
}
