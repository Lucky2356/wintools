using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class ProcessChange {
        public string Schema,Id,Name,Action,Before,After,TimeUtc,Status,Error;
        public int Pid;
        public long Started;
        public bool Restore;
    }
    internal static class ProcessActions {
        private static string DirectoryPath {get{return Path.Combine(Program.Data,"process-history");}}
        private static string RecordPath(string id){if(!Regex.IsMatch(id??"","^[a-f0-9]{32}$"))throw new IOException("Некорректный номер изменения процесса.");return Program.Under(DirectoryPath,id+".json");}
        private static void Save(ProcessChange record){
            Program.SafeDirectory(DirectoryPath);Directory.CreateDirectory(DirectoryPath);string path=RecordPath(record.Id),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("История процессов является ссылкой.");
            try{var bytes=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(record));if(bytes.Length>65536)throw new IOException("Запись слишком велика.");using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static ProcessChange Read(string id){
            try{Program.SafeDirectory(DirectoryPath);string path=RecordPath(id);if(new FileInfo(path).Length>65536||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Некорректная история процесса.");var r=new JavaScriptSerializer().Deserialize<ProcessChange>(File.ReadAllText(path));DateTime time;ulong before,after;
                if(r==null||r.Schema!="wintools/process-change/1"||r.Id!=id||r.Pid<=4||r.Started<=0||!new[]{"priority","affinity"}.Contains(r.Action)||!ulong.TryParse(r.Before,out before)||!ulong.TryParse(r.After,out after)||before==0||after==0||!new[]{"PENDING","OK","FAILED","REVERTED"}.Contains(r.Status)||!DateTime.TryParseExact(r.TimeUtc,"o",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out time))throw new IOException("Некорректная запись процесса.");if(r.Action=="priority"&&(before>uint.MaxValue||after>uint.MaxValue||!ProcessControl.Priorities.Contains((uint)before)||!ProcessControl.Priorities.Contains((uint)after)))throw new IOException("Некорректный приоритет в истории.");return r;
            }catch(ArgumentException ex){throw new IOException("Повреждена история процессов.",ex);}catch(InvalidOperationException ex){throw new IOException("Повреждена история процессов.",ex);}
        }
        internal static ProcessChange[] History(){if(!Directory.Exists(DirectoryPath))return new ProcessChange[0];Program.SafeDirectory(DirectoryPath);return Directory.GetFiles(DirectoryPath,"*.json").Select(p=>Read(Path.GetFileNameWithoutExtension(p))).OrderByDescending(r=>r.TimeUtc,StringComparer.Ordinal).ToArray();}
        internal static Task<EngineResult> Run(ProcessRow row,ProcessSettings expected,string action,ulong target,string restore){return Task.Run(()=>Change(row,expected,action,target,restore));}
        internal static EngineResult Change(ProcessRow row,ProcessSettings expected,string action,ulong target,string restore){
            string lockPath=Path.Combine(Program.Data,"state","run.lock");Program.SafeDirectory(Path.GetDirectoryName(lockPath));FileStream gate=null;ProcessChange record=null;
            try{if(row.Id!=expected.Id||row.Started!=expected.Started)throw new ArgumentException("Выбран другой запуск процесса.");ProcessControl.ValidateTarget(action,target,expected);gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                using(var handle=ProcessControl.Open(row.Id,row.Started)){var before=ProcessControl.Inspect(handle,row.Id,row.Started);if(ProcessControl.Value(before,action)!=ProcessControl.Value(expected,action))throw new IOException("Настройка процесса уже изменилась. Обновите состояние перед повтором.");ProcessControl.ValidateTarget(action,target,before);
                    ProcessChange original=null;if(restore!=null){original=Read(restore);if(original.Restore||original.Status=="REVERTED"||original.Pid!=row.Id||original.Started!=row.Started||original.Action!=action||ulong.Parse(original.Before)!=target||ulong.Parse(original.After)!=ProcessControl.Value(before,action))throw new IOException("Изменение не подходит для возврата. Сначала отмените более позднюю настройку.");}
                    if(ProcessControl.Value(before,action)==target&&original==null)return new EngineResult{Code=0,Output="Этот параметр уже установлен."};record=new ProcessChange{Schema="wintools/process-change/1",Id=Guid.NewGuid().ToString("N"),Pid=row.Id,Started=row.Started,Name=row.Name,Action=action,Before=ProcessControl.Value(before,action).ToString(),After=target.ToString(),TimeUtc=DateTime.UtcNow.ToString("o"),Status="PENDING",Restore=original!=null};Save(record);
                    if(ProcessControl.Value(ProcessControl.Inspect(handle,row.Id,row.Started),action)!=ProcessControl.Value(before,action))throw new IOException("Настройка процесса изменилась во время подготовки.");ProcessControl.Set(handle,action,target,before);if(ProcessControl.Value(ProcessControl.Inspect(handle,row.Id,row.Started),action)!=target)throw new IOException("Процесс не подтвердил новое значение.");record.Status="OK";Save(record);if(original!=null){original.Status="REVERTED";Save(original);}return new EngineResult{Code=0,Output=row.Name+" · PID "+row.Id+": "+(action=="priority"?"приоритет «"+ProcessControl.PriorityName((uint)target)+"»":"изменён набор логических процессоров")+". Настройка действует для этого запуска программы. Вернуть прежнее значение можно через историю, пока процесс работает. Дочерние процессы могут наследовать настройку; их параметры отдельно не возвращаются."};
                }
            }catch(Exception ex){string message=ex.Message;if(record!=null){record.Status="FAILED";record.Error=message;try{Save(record);}catch(Exception save){message+=" Не удалось сохранить итог: "+save.Message;}}return new EngineResult{Code=4,Output="Не удалось завершить изменение процесса: "+message+" Обновите состояние."};}
            finally{if(gate!=null){gate.Dispose();File.Delete(lockPath);}}
        }
    }
}
