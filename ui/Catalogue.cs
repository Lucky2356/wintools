using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Wintools {
    internal sealed class Tweak {
        public string Id, Category, Title, Description, Kind, Risk, Os;
        public string Verb { get { return Category == "SYS" ? "system-change" : Category == "CLEAN" ? "cleanup" : "apply"; } }
        public string Rollback { get { return Category == "CLEAN" ? "Нет: файлы удаляются" : Kind == "EDGE" ? "Ручная переустановка" : Kind == "APPX" ? "Регистрация из сохранённого payload; зависит от его наличия" : "Исходное состояние в журнале"; } }
    }
    internal static class Catalogue {
        internal static readonly Dictionary<string,string> Categories = new Dictionary<string,string> {
            {"ALL","Все действия"},{"PRIV","Приватность"},{"UI","Интерфейс Windows"},{"PERF","Производительность"},
            {"SVC","Службы"},{"TASK","Планировщик"},{"UPD","Обновления Windows"},{"EDGE","Microsoft Edge"},
            {"APPS","Приложения"},{"SYS","Питание, сеть, диск"},{"CLEAN","Очистка"}
        };
        internal static List<Tweak> Load() {
            var definitions = File.ReadAllLines(Path.Combine(Program.Data,"data","tweaks.def"))
                .Where(l => l.Length > 0 && !l.StartsWith("#")).Select(l => l.Split('|')).ToDictionary(p => p[0]);
            var result = new List<Tweak>();
            foreach (var line in File.ReadAllLines(Path.Combine(Program.Data,"data","descr.ru"), Encoding.GetEncoding(866))) {
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split('|');
                if (p.Length != 6 || !Regex.IsMatch(p[0], "^[A-Z][A-Z0-9-]{1,63}$")) throw new IOException("Invalid catalogue description.");
                string[] def;
                bool known = definitions.TryGetValue(p[0], out def);
                if (!known && p[1] != "SYS" && p[1] != "CLEAN") continue;
                result.Add(new Tweak {Id=p[0],Category=p[1],Title=p[2],Description=string.Join(" ",p.Skip(3).Where(s=>s!="-")),Kind=known?def[4]:p[1],Risk=known?def[2]:"med",Os=known?def[3]:"any"});
            }
            return result;
        }
    }
    internal sealed class Preferences {
        public bool AutoCheck = true;
        public bool IncludePreview = Program.Version.Contains("-");
        public bool RestorePoint = true;
        public List<string> Favorites = new List<string>();
        internal static Preferences Load() {
            try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Program.Data,"preferences.json"))) ?? new Preferences(); }
            catch { return new Preferences(); }
        }
        internal void Save() {
            var path=Path.Combine(Program.Data,"preferences.json");
            var temp=path+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
            if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
    }
}
