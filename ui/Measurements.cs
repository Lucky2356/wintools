using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class MeasurementPoint {
        public double Seconds;
        public double? Cpu,Memory,Gpu,Receive,Send;
        public string Error;
    }
    internal sealed class MeasurementSession {
        public string Schema="wintools/measurements/1",Id,StartedUtc,Name,Status,NetworkId,NetworkName,GpuId,GpuName;
        public List<MeasurementPoint> Points=new List<MeasurementPoint>();
        [ScriptIgnore] public string Label {get{return Name+" · "+DateTime.Parse(StartedUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g")+" · "+Points.Count+" замеров";}}
    }
    internal static class Measurements {
        internal const int Limit=300;
        private static string DirectoryPath {get{return Path.Combine(Program.Data,"measurements");}}
        private static bool Metric(double? value,double maximum){return !value.HasValue||(!double.IsNaN(value.Value)&&!double.IsInfinity(value.Value)&&value>=0&&value<=maximum);}
        internal static void Validate(MeasurementSession session){
            DateTime date;
            if(session==null||session.Schema!="wintools/measurements/1"||!Regex.IsMatch(session.Id??"",@"\A[a-f0-9]{32}\z")||!DateTime.TryParseExact(session.StartedUtc,"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out date)||date.Kind!=DateTimeKind.Utc||!new[]{"recording","complete","interrupted"}.Contains(session.Status)||string.IsNullOrWhiteSpace(session.Name)||session.Name.Length>80||session.Points==null||session.Points.Count>Limit)throw new IOException("Некорректная запись измерений.");
            foreach(var value in new[]{session.NetworkId,session.NetworkName,session.GpuId,session.GpuName})if(value==null||value.Length>512)throw new IOException("Некорректные сведения об адаптере.");
            double previous=-1;
            foreach(var point in session.Points){if(point==null||!Metric(point.Seconds,660)||point.Seconds<previous||!Metric(point.Cpu,100)||!Metric(point.Memory,100)||!Metric(point.Gpu,100)||!Metric(point.Receive,1e15)||!Metric(point.Send,1e15)||point.Error==null||point.Error.Length>4096)throw new IOException("Некорректный замер.");previous=point.Seconds;}
        }
        internal static void Save(MeasurementSession session){
            Validate(session);Program.SafeDirectory(DirectoryPath);Directory.CreateDirectory(DirectoryPath);var path=Program.Under(DirectoryPath,session.Id+".json");
            if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Запись измерений является ссылкой.");
            var bytes=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(session));if(bytes.Length>1048576)throw new IOException("Запись измерений слишком велика.");var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal static MeasurementSession Read(string path){
            Program.SafeDirectory(DirectoryPath);if(!string.Equals(Path.GetFullPath(path),Program.Under(DirectoryPath,Path.GetFileName(path)),StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||new FileInfo(path).Length>1048576)throw new IOException("Недоступный файл измерений.");
            try{var session=new JavaScriptSerializer{MaxJsonLength=1048576}.Deserialize<MeasurementSession>(File.ReadAllText(path));Validate(session);if(Path.GetFileNameWithoutExtension(path)!=session.Id)throw new IOException("Номер записи измерений не совпадает.");return session;}catch(ArgumentException ex){throw new IOException("Не удалось прочитать измерения.",ex);}catch(InvalidOperationException ex){throw new IOException("Не удалось прочитать измерения.",ex);}
        }
        internal static MeasurementSession[] History(out int errors){
            errors=0;if(!Directory.Exists(DirectoryPath))return new MeasurementSession[0];Program.SafeDirectory(DirectoryPath);var rows=new List<MeasurementSession>();
            foreach(var path in Directory.GetFiles(DirectoryPath,"*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(50)){try{rows.Add(Read(path));}catch(IOException){errors++;}catch(UnauthorizedAccessException){errors++;}}
            return rows.OrderByDescending(r=>r.StartedUtc,StringComparer.Ordinal).ToArray();
        }
        internal static string Difference(string title,IEnumerable<double?> first,IEnumerable<double?> second,string unit){var a=first.Where(v=>v.HasValue).Select(v=>v.Value).ToArray();var b=second.Where(v=>v.HasValue).Select(v=>v.Value).ToArray();return (title.Length==0?"":title+": ")+(a.Length==0||b.Length==0?"недостаточно данных":(b.Average()-a.Average()).ToString("+0.0;-0.0;0.0")+" "+unit+" ("+a.Length+" / "+b.Length+" замеров)");}
        internal static string Csv(MeasurementSession session){Validate(session);var text=new StringBuilder("elapsed_seconds,cpu_percent,memory_percent,gpu_percent,receive_bytes_per_second,send_bytes_per_second\r\n");foreach(var point in session.Points)text.AppendLine(string.Join(",",new double?[]{point.Seconds,point.Cpu,point.Memory,point.Gpu,point.Receive,point.Send}.Select(v=>v.HasValue?v.Value.ToString("R",CultureInfo.InvariantCulture):"")));return text.ToString();}
    }
}
