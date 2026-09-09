using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class ServiceSnapshot {
        public string Name, Label, Description, Mode, State, Kind;
        public bool Delayed, CanStop;
    }
    internal sealed class ServiceChange {
        public string Schema, Id, Name, Action, TimeUtc, Status, Error;
        public ServiceSnapshot Before, After;
    }
    internal static class ServiceActions {
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr OpenSCManager(string machine,string database,uint access);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr OpenService(IntPtr manager,string name,uint access);
        [DllImport("advapi32.dll",SetLastError=true)] private static extern bool CloseServiceHandle(IntPtr handle);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryServiceConfig2(IntPtr service,uint level,IntPtr data,uint size,out uint needed);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool ChangeServiceConfig2(IntPtr service,uint level,ref int data);
        internal static bool Manageable(ServiceSnapshot service){return service!=null&&service.Kind!=null&&service.Kind.IndexOf("Process",StringComparison.OrdinalIgnoreCase)>=0&&new[]{"Running","Stopped"}.Contains(service.State)&&new[]{"Auto","Manual","Disabled"}.Contains(service.Mode);}
        internal static bool ValidName(string name){return !string.IsNullOrEmpty(name)&&name.Length<=256&&!name.Any(c=>char.IsControl(c)||c=='/'||c=='\\');}
        private static ManagementObject Open(string name){
            if(!ValidName(name))throw new ArgumentException("Некорректное имя службы.");
            var service=new ManagementObject(null,new ManagementPath("Win32_Service.Name=\""+name.Replace("\"","\\\"")+"\""),new ObjectGetOptions{Timeout=TimeSpan.FromSeconds(15)});
            try{service.Get();return service;}catch{service.Dispose();throw;}
        }
        private static bool DelaySetting(string name,bool? setting){
            IntPtr manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new Win32Exception();
            try{IntPtr service=OpenService(manager,name,setting.HasValue?2u:1u);if(service==IntPtr.Zero)throw new Win32Exception();
                try{if(setting.HasValue){int flag=setting.Value?1:0;if(!ChangeServiceConfig2(service,3,ref flag))throw new Win32Exception();return setting.Value;}
                    IntPtr buffer=Marshal.AllocHGlobal(4);try{uint needed;if(!QueryServiceConfig2(service,3,buffer,4,out needed))throw new Win32Exception();return Marshal.ReadInt32(buffer)!=0;}finally{Marshal.FreeHGlobal(buffer);}
                }finally{CloseServiceHandle(service);}
            }finally{CloseServiceHandle(manager);}
        }
        internal static ServiceSnapshot Inspect(string name){
            using(var service=Open(name)){return new ServiceSnapshot{Name=Convert.ToString(service["Name"]),Label=Convert.ToString(service["DisplayName"]),Description=Convert.ToString(service["Description"]),Mode=Convert.ToString(service["StartMode"]),State=Convert.ToString(service["State"]),CanStop=Convert.ToBoolean(service["AcceptStop"]),Kind=Convert.ToString(service["ServiceType"]),Delayed=DelaySetting(name,null)};}
        }
        private static void Invoke(string name,string method,string mode){
            using(var service=Open(name))using(var input=service.GetMethodParameters(method)){
                if(mode!=null)input["StartMode"]=mode;
                using(var result=service.InvokeMethod(method,input,new InvokeMethodOptions{Timeout=TimeSpan.FromSeconds(30)})){
                    uint code=Convert.ToUInt32(result["ReturnValue"]);if(code!=0)throw new InvalidOperationException(code==3?"Есть работающие зависимые службы. Они не были остановлены автоматически.":code==2?"Windows не разрешила изменение этой службы.":"Windows отклонила действие службы: код "+code+".");
                }
            }
        }
        private static void WaitState(string name,string expected){
            var watch=Stopwatch.StartNew();while(watch.Elapsed<TimeSpan.FromSeconds(30)){using(var service=Open(name)){if(Convert.ToString(service["State"])==expected)return;}Thread.Sleep(250);}throw new TimeoutException("Служба не перешла в ожидаемое состояние за 30 секунд. Обновите её состояние.");
        }
        private static void Start(string name){if(Inspect(name).State=="Running")return;Invoke(name,"StartService",null);WaitState(name,"Running");}
        private static void Stop(string name){if(Inspect(name).State=="Stopped")return;Invoke(name,"StopService",null);WaitState(name,"Stopped");}
        private static void Mode(string name,string mode,bool delayed){
            if(!new[]{"Auto","Manual","Disabled"}.Contains(mode))throw new ArgumentException("Неподдерживаемый режим запуска.");
            Invoke(name,"ChangeStartMode",mode=="Auto"?"Automatic":mode);if(mode=="Auto")DelaySetting(name,delayed);
            var actual=Inspect(name);if(actual.Mode!=mode||(mode=="Auto"&&actual.Delayed!=delayed))throw new IOException("Windows не сохранила выбранный режим запуска.");
        }
        private static void Restore(ServiceSnapshot before){
            if(before==null||!ValidName(before.Name)||!new[]{"Auto","Manual","Disabled"}.Contains(before.Mode)||!new[]{"Running","Stopped"}.Contains(before.State))throw new IOException("Некорректное исходное состояние службы.");
            if(before.State=="Stopped")Stop(before.Name);
            Mode(before.Name,before.Mode=="Disabled"&&before.State=="Running"?"Manual":before.Mode,before.Delayed);
            if(before.State=="Running")Start(before.Name);
            if(before.Mode=="Disabled"&&before.State=="Running")Mode(before.Name,"Disabled",false);
        }
        private static string DirectoryPath {get{return Path.Combine(Program.Data,"service-history");}}
        private static string RecordPath(string id){if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new IOException("Некорректный номер изменения службы.");return Program.Under(DirectoryPath,id+".json");}
        private static void Save(ServiceChange record){
            Program.SafeDirectory(DirectoryPath);Directory.CreateDirectory(DirectoryPath);var path=RecordPath(record.Id);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файл истории является ссылкой.");
            try{var data=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(record));if(data.Length>65536)throw new IOException("Описание службы слишком велико для истории.");using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data,0,data.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static ServiceChange Read(string id){
            try{
            Program.SafeDirectory(DirectoryPath);var path=RecordPath(id);if(new FileInfo(path).Length>65536||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Некорректный файл истории службы.");
            var record=new JavaScriptSerializer().Deserialize<ServiceChange>(File.ReadAllText(path));DateTime stamp;if(record==null||record.Schema!="wintools/service-change/1"||record.Id!=id||record.Before==null||record.Before.Name!=record.Name||!ValidName(record.Name)||!new[]{"Auto","Manual","Disabled"}.Contains(record.Before.Mode)||!new[]{"Running","Stopped"}.Contains(record.Before.State)||!new[]{"PENDING","OK","FAILED","REVERTED"}.Contains(record.Status)||!DateTime.TryParseExact(record.TimeUtc,"o",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out stamp))throw new IOException("Некорректная запись истории службы.");Validate(record.Name,record.Action,record.Action=="restore"?record.Id:null);return record;
            }catch(ArgumentException ex){throw new IOException("Некорректная запись истории службы.",ex);}catch(InvalidOperationException ex){throw new IOException("Некорректная запись истории службы.",ex);}
        }
        internal static ServiceChange[] History(){if(!Directory.Exists(DirectoryPath))return new ServiceChange[0];Program.SafeDirectory(DirectoryPath);return Directory.GetFiles(DirectoryPath,"*.json").Select(path=>Read(Path.GetFileNameWithoutExtension(path))).OrderByDescending(r=>r.TimeUtc,StringComparer.Ordinal).ToArray();}
        internal static ServiceChange Latest(string name){
            return History().FirstOrDefault(r=>r.Name==name&&r.Action!="restore"&&r.Status!="REVERTED");
        }
        internal static async Task<EngineResult> Run(string name,string action,string restoreId){
            Validate(name,action,restoreId);var token=Guid.NewGuid().ToString("N");string sid=WindowsIdentity.GetCurrent().User.Value;
            var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(name));var args="--service-worker "+action+" "+encoded+" "+token+" "+sid+" "+(restoreId??"-");
            var info=new ProcessStartInfo(Program.Exe,args){UseShellExecute=true,Verb="runas",WorkingDirectory=Program.Home,WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(info)){while(!process.HasExited)await Task.Delay(400);var log=Path.Combine(Program.Data,"runtime",token+".service.log");return new EngineResult{Code=process.ExitCode,Output=File.Exists(log)?File.ReadAllText(log):"Действие службы завершилось без отчёта."};}
        }
        private static void Validate(string name,string action,string restoreId){
            if(!ValidName(name)||!new[]{"start","stop","restart","auto","delayed","manual","disabled","restore"}.Contains(action))throw new ArgumentException("Недопустимое действие службы.");
            if(action=="restore")RecordPath(restoreId);else if(restoreId!=null&&restoreId!="-")throw new ArgumentException("Неожиданный номер восстановления.");
        }
        internal static int Worker(string[] args){
            if(args.Length!=6||!Regex.IsMatch(args[3],"^[a-f0-9]{32}$")||args[4]!=WindowsIdentity.GetCurrent().User.Value)throw new ArgumentException("Запрос службы некорректен или повышение прав выполнено под другим пользователем.");
            string name=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(args[2]));string action=args[1],id=args[3];Validate(name,action,args[5]);
            if(!Directory.Exists(Program.Data))throw new IOException("Сначала запустите интерфейс Wintools.");Program.SafeDirectory(Program.Data);var runtime=Path.Combine(Program.Data,"runtime");Program.SafeDirectory(runtime);Directory.CreateDirectory(runtime);
            using(var log=new StreamWriter(new FileStream(Path.Combine(runtime,id+".service.log"),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false))){
                var lockPath=Path.Combine(Program.Data,"state","run.lock");Program.SafeDirectory(Path.GetDirectoryName(lockPath));FileStream gate=null;ServiceChange record=null;
                try{gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);var before=Inspect(name);if(!Manageable(before))throw new IOException("Дождитесь стабильного состояния службы. Драйверы и приостановленные службы этим инструментом не изменяются.");
                    if((action=="start"||action=="restart")&&before.Mode=="Disabled")throw new IOException("Сначала разрешите запуск службы. Отключённая служба не была остановлена для перезапуска.");
                    ServiceChange original=null;if(action=="restore"){original=Read(args[5]);if(original.Name!=name||original.Status=="REVERTED")throw new IOException("Запись восстановления не соответствует выбранной службе.");}
                    record=new ServiceChange{Schema="wintools/service-change/1",Id=id,Name=name,Action=action,Before=before,TimeUtc=DateTime.UtcNow.ToString("o"),Status="PENDING"};Save(record);
                    if(action=="start")Start(name);else if(action=="stop")Stop(name);else if(action=="restart"){Stop(name);Start(name);}else if(action=="restore")Restore(original.Before);else Mode(name,action=="manual"?"Manual":action=="disabled"?"Disabled":"Auto",action=="delayed");
                    record.After=Inspect(name);record.Status="OK";Save(record);if(original!=null){original.Status="REVERTED";Save(original);}log.WriteLine("Готово: "+record.After.Label+". Состояние: "+record.After.State+". Запуск: "+record.After.Mode+(record.After.Delayed?" (отложенный)":"")+". История: "+id);return 0;
                }catch(Exception ex){if(record!=null){record.Status="FAILED";record.Error=ex.Message;try{record.After=Inspect(name);Save(record);}catch(Exception saveError){log.WriteLine("Не удалось обновить историю: "+saveError.Message);}}log.WriteLine("Действие не завершено: "+ex.Message+" Проверьте текущее состояние службы; часть шагов могла выполниться.");return 4;}
                finally{if(gate!=null){gate.Dispose();File.Delete(lockPath);}}
            }
        }
    }
}
