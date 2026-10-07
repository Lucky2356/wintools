using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace Wintools {
    internal sealed class RestorePointInfo {
        public string Description {get;set;}
        public string TimeUtc;
        public long Sequence;
        public string Detail {get{DateTime time;return (DateTime.TryParse(TimeUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out time)?time.ToLocalTime().ToString("g"):"дата неизвестна")+" · №"+Sequence;}}
    }
    internal sealed class BackupResult {
        public RestorePointInfo[] Points=new RestorePointInfo[0];
        public long UsedBytes,MaxBytes;
        public string Message,Folder;
        public int Drivers;
    }
    internal sealed class BatteryInfo {
        public string Name;
        public long DesignMWh,FullMWh;
        public int Cycles;
        internal double? Health {get{return DesignMWh>0&&FullMWh>0?(double?)Math.Min(100.0,100.0*FullMWh/DesignMWh):null;}}
    }
    // Restore points, shadow storage and driver export need elevation; one worker performs the requested action and returns JSON.
    internal static class Backups {
        internal static RestorePointInfo[] Points(){
            var result=new List<RestorePointInfo>();
            using(var searcher=new ManagementObjectSearcher(@"root\default","SELECT Description,CreationTime,SequenceNumber FROM SystemRestore"))foreach(ManagementObject item in searcher.Get())using(item){
                string created=Convert.ToString(item["CreationTime"]);DateTime time;string iso=null;try{time=ManagementDateTimeConverter.ToDateTime(created);iso=time.ToUniversalTime().ToString("o");}catch(ArgumentException){}catch(FormatException){}
                result.Add(new RestorePointInfo{Description=Clip(Convert.ToString(item["Description"])),TimeUtc=iso,Sequence=Convert.ToInt64(item["SequenceNumber"])});
            }
            return result.OrderByDescending(p=>p.Sequence).Take(50).ToArray();
        }
        private static string Clip(string text){text=(text??"").Trim();return text.Length>120?text.Substring(0,120):text.Length==0?"Без описания":text;}
        internal static void Storage(BackupResult result){
            using(var searcher=new ManagementObjectSearcher("SELECT UsedSpace,MaxSpace FROM Win32_ShadowStorage"))foreach(ManagementObject item in searcher.Get())using(item){result.UsedBytes+=Convert.ToInt64(item["UsedSpace"]);result.MaxBytes+=Convert.ToInt64(item["MaxSpace"]);}
        }
        internal static int CountDrivers(string folder){return Directory.Exists(folder)?Directory.GetFiles(folder,"*.inf",SearchOption.AllDirectories).Length:0;}
        internal static async Task<BackupResult> Run(string action){
            if(!new[]{"list","create","drivers"}.Contains(action))throw new ArgumentException("Неизвестное действие резервного копирования.");
            string token=Guid.NewGuid().ToString("N"),sid=WindowsIdentity.GetCurrent().User.Value;
            var info=new ProcessStartInfo(Program.Exe,"--backup-worker "+action+" "+token+" "+sid){UseShellExecute=true,Verb="runas",WorkingDirectory=Program.Home,WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(info)){while(!process.HasExited)await Task.Delay(400);string path=Path.Combine(Program.Data,"runtime",token+".backup.json");
                try{if(!File.Exists(path))throw new IOException("Действие завершилось без отчёта.");if(new FileInfo(path).Length>1048576)throw new IOException("Отчёт слишком велик.");var text=File.ReadAllText(path);if(process.ExitCode!=0)throw new IOException(text);return Validate(new JavaScriptSerializer().Deserialize<BackupResult>(text));}
                finally{try{if(File.Exists(path))File.Delete(path);}catch(IOException){}}}
        }
        internal static BackupResult Validate(BackupResult result){
            if(result==null||result.Points==null||result.Points.Length>50||result.UsedBytes<0||result.MaxBytes<0||result.Drivers<0||(result.Message!=null&&result.Message.Length>500))throw new IOException("Некорректный отчёт резервного копирования.");
            foreach(var point in result.Points)if(point==null||string.IsNullOrEmpty(point.Description)||point.Description.Length>120)throw new IOException("Некорректная точка восстановления.");
            if(result.Folder!=null){var root=Path.GetFullPath(Path.Combine(Program.Data,"drivers"))+Path.DirectorySeparatorChar;if(!Path.GetFullPath(result.Folder).StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Некорректная папка драйверов.");}
            return result;
        }
        internal static int Worker(string[] args){
            if(args.Length!=4||!new[]{"list","create","drivers"}.Contains(args[1])||!Regex.IsMatch(args[2],"^[a-f0-9]{32}$")||args[3]!=WindowsIdentity.GetCurrent().User.Value)throw new ArgumentException("Запрос резервного копирования некорректен или права повышены под другим пользователем.");
            if(!Directory.Exists(Program.Data))throw new IOException("Сначала запустите интерфейс Wintools.");Program.SafeDirectory(Program.Data);string runtime=Path.Combine(Program.Data,"runtime");Program.SafeDirectory(runtime);Directory.CreateDirectory(runtime);
            var result=new BackupResult();int code=0;string text;
            try{
                if(args[1]=="create"){long before=SafePoints().Select(p=>p.Sequence).DefaultIfEmpty(0).Max();var messages=new List<string>();if(!WindowsIntegrity.RestorePointEvent(100,messages.Add)||!WindowsIntegrity.RestorePointEvent(101,messages.Add))throw new IOException(string.Join(" ",messages));long after=SafePoints().Select(p=>p.Sequence).DefaultIfEmpty(0).Max();result.Message=after>before?"Точка восстановления создана.":"Windows приняла запрос, но новую точку не создала: обычно Windows создаёт не больше одной точки за 24 часа или защита системы выключена.";}
                if(args[1]=="drivers"){string folder=Path.Combine(Program.Data,"drivers",DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture));Program.SafeDirectory(Path.Combine(Program.Data,"drivers"));Directory.CreateDirectory(folder);
                    var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"pnputil.exe"),"/export-driver * \""+folder+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                    using(var process=Process.Start(info)){var error=process.StandardError.ReadToEndAsync();process.StandardOutput.ReadToEnd();if(!process.WaitForExit(600000)){try{process.Kill();}catch(InvalidOperationException){}throw new IOException("Экспорт драйверов не завершился за 10 минут.");}error.Wait();if(process.ExitCode!=0&&CountDrivers(folder)==0)throw new IOException("pnputil завершился с кодом "+process.ExitCode+".");}
                    result.Folder=folder;result.Drivers=CountDrivers(folder);result.Message="Сохранено драйверов: "+result.Drivers+".";}
                result.Points=SafePoints();try{Storage(result);}catch(ManagementException){}
                text=new JavaScriptSerializer().Serialize(result);
            }catch(Exception ex){text=ex.Message;code=4;}
            using(var file=new FileStream(Path.Combine(runtime,args[2]+".backup.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=new UTF8Encoding(false).GetBytes(text);file.Write(bytes,0,bytes.Length);}
            return code;
        }
        private static RestorePointInfo[] SafePoints(){try{return Points();}catch(ManagementException){return new RestorePointInfo[0];}}
        // powercfg writes the battery report without elevation; the XML variant gives design and full charge capacity directly.
        internal static BatteryInfo[] ParseBatteries(string xml){
            var document=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))document.Load(reader);
            var result=new List<BatteryInfo>();
            foreach(XmlElement battery in document.GetElementsByTagName("Battery")){Func<string,string> value=tag=>{var node=battery.GetElementsByTagName(tag);return node.Count>0?node[0].InnerText.Trim():"";};long design,full;int cycles;
                long.TryParse(value("DesignCapacity"),NumberStyles.Integer,CultureInfo.InvariantCulture,out design);long.TryParse(value("FullChargeCapacity"),NumberStyles.Integer,CultureInfo.InvariantCulture,out full);int.TryParse(value("CycleCount"),NumberStyles.Integer,CultureInfo.InvariantCulture,out cycles);
                var name=(value("Manufacturer")+" "+value("Id")).Trim();result.Add(new BatteryInfo{Name=name.Length==0?"Батарея":name.Length>80?name.Substring(0,80):name,DesignMWh=Math.Max(0,design),FullMWh=Math.Max(0,full),Cycles=Math.Max(0,cycles)});}
            return result.ToArray();
        }
        internal static string Describe(BatteryInfo[] batteries){
            if(batteries.Length==0)return "Батарея не найдена: похоже, это настольный ПК или Windows не видит батарею.";
            return string.Join("\n",batteries.Select(b=>b.Name+": "+(b.Health.HasValue?"сохранилось "+b.Health.Value.ToString("0",CultureInfo.InvariantCulture)+" % ёмкости ("+(b.FullMWh/1000.0).ToString("0.0",CultureInfo.GetCultureInfo("ru-RU"))+" из "+(b.DesignMWh/1000.0).ToString("0.0",CultureInfo.GetCultureInfo("ru-RU"))+" Вт·ч)":"ёмкость неизвестна")+(b.Cycles>0?", циклов заряда: "+b.Cycles:"")+"."))+"\nЁмкость ниже 80 % обычно заметна по времени работы; это естественный износ, а не неисправность Windows.";
        }
        internal static async Task<BatteryInfo[]> ReadBatteries(string htmlPath){
            string runtime=Path.Combine(Program.Data,"runtime");Program.SafeDirectory(runtime);Directory.CreateDirectory(runtime);string xml=Path.Combine(runtime,Guid.NewGuid().ToString("N")+".battery.xml");
            try{await Powercfg("/batteryreport /xml /output \""+xml+"\"");if(!File.Exists(xml))return new BatteryInfo[0];var batteries=ParseBatteries(File.ReadAllText(xml));if(htmlPath!=null&&batteries.Length>0)await Powercfg("/batteryreport /output \""+htmlPath+"\"");return batteries;}
            finally{try{if(File.Exists(xml))File.Delete(xml);}catch(IOException){}}
        }
        private static Task Powercfg(string arguments){return Task.Run(()=>{var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"powercfg.exe"),arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};using(var process=Process.Start(info)){var error=process.StandardError.ReadToEndAsync();process.StandardOutput.ReadToEnd();if(!process.WaitForExit(60000)){try{process.Kill();}catch(InvalidOperationException){}throw new IOException("powercfg не ответил за минуту.");}error.Wait();}});}
    }
}
