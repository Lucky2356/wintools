using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class StoreRecord {public string Schema,Id,Name,Package,Action,TimeUtc,Status,Summary;}
    internal static class StoreActions {
        private static string DirectoryPath {get{return Path.Combine(Program.Data,"store-history");}}
        private static string RecordPath(string id){if(!Regex.IsMatch(id??"",@"\A[a-f0-9]{32}\z"))throw new IOException("Некорректный номер операции приложения.");return Program.Under(DirectoryPath,id+".json");}
        private static void Save(StoreRecord record){
            Program.SafeDirectory(DirectoryPath);Directory.CreateDirectory(DirectoryPath);string path=RecordPath(record.Id),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("История приложения является ссылкой.");
            try{byte[] bytes=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(record));if(bytes.Length>65536)throw new IOException("Запись слишком велика.");using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal static string Title(string action){if(action=="reset")return "Сброс данных приложения";if(action=="register")return "Исправление регистрации приложения";if(action=="remove")return "Удаление приложения пользователя";throw new IOException("Неизвестная операция приложения.");}
        internal static StoreRecord Read(string id){
            try {
            Program.SafeDirectory(DirectoryPath);string path=RecordPath(id);if(new FileInfo(path).Length>65536||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Некорректная история приложения.");
            var record=new JavaScriptSerializer().Deserialize<StoreRecord>(File.ReadAllText(path));DateTime time;
            if(record==null||record.Schema!="wintools/store-operation/1"||record.Id!=id||!StorePackages.Identity(record.Package)||!new[]{"PENDING","OK","FAILED"}.Contains(record.Status)||!DateTime.TryParseExact(record.TimeUtc,"o",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out time))throw new IOException("Повреждена история приложения.");Title(record.Action);return record;
            }catch(ArgumentException ex){throw new IOException("Повреждена история приложения.",ex);}catch(InvalidOperationException ex){throw new IOException("Повреждена история приложения.",ex);}
        }
        internal static StoreRecord[] History(){if(!Directory.Exists(DirectoryPath))return new StoreRecord[0];Program.SafeDirectory(DirectoryPath);return Directory.GetFiles(DirectoryPath,"*.json").Select(p=>Read(Path.GetFileNameWithoutExtension(p))).ToArray();}
        internal static Task<EngineResult> Run(StorePackage package,string action){return Run(package,action,StorePackages.Script);}
        internal static async Task<EngineResult> Run(StorePackage package,string action,Func<object,bool,Task<EngineResult>> execute){
            StorePackages.Validate(package,action);string lockPath=Path.Combine(Program.Data,"state","run.lock");Program.SafeDirectory(Path.GetDirectoryName(lockPath));FileStream gate=null;StoreRecord record=null;
            try{gate=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);record=new StoreRecord{Schema="wintools/store-operation/1",Id=Guid.NewGuid().ToString("N"),Name=package.Name,Package=package.FullName,Action=action,TimeUtc=DateTime.UtcNow.ToString("o"),Status="PENDING",Summary="Операция начата. Итог ещё не получен; не повторяйте её без проверки состояния приложения."};Save(record);
                var result=await execute(new{Action=action,FullName=package.FullName,FamilyName=package.FamilyName},false);record.Status=result.Code==0?"OK":"FAILED";record.Summary=result.Output.Length>12000?result.Output.Substring(0,12000):result.Output;Save(record);return result;
            }catch(Exception ex){string message="Не удалось подтвердить завершение операции: "+ex.Message+" Проверьте состояние приложения перед повтором.";if(record!=null){record.Status="FAILED";record.Summary=message;try{Save(record);}catch(Exception save){message+=" Не удалось записать итог: "+save.Message;}}return new EngineResult{Code=4,Output=message};}
            finally{if(gate!=null){gate.Dispose();File.Delete(lockPath);}}
        }
    }
}
