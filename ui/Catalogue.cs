using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Wintools
{
    internal sealed class Tweak
    {
        public string Id, Category, Title, Description, Caveat, Compatibility, Kind, Risk, Os, Target, ValueName, ValueType, Value;
        public string Verb
        {
            get
            {
                return Category == "SYS" ? "system-change" : Category == "CLEAN" ? "cleanup" : "apply";
            }
        }

        public string Rollback
        {
            get
            {
                return Category == "CLEAN" ? Lang.T("Удалённые файлы вернуть через Wintools нельзя.") : Kind == "EDGE" ? Lang.T("Потребуется заново установить браузер.") : Kind == "APPX" ? Lang.T("Попробуем вернуть приложение из оставшихся файлов. Если их уже нет, потребуется переустановка.") : Lang.T("Wintools сохранит прежнее значение. Вернуть его можно здесь или в истории изменений.");
            }
        }
    }

    internal static class Catalogue
    {
        internal static readonly Dictionary<string, string> Categories = new Dictionary<string, string>
        {
            {
                "ALL",
                Lang.T("Все действия")
            },
            {
                "PRIV",
                Lang.T("Приватность")
            },
            {
                "UI",
                Lang.T("Интерфейс Windows")
            },
            {
                "PERF",
                Lang.T("Производительность")
            },
            {
                "SVC",
                Lang.T("Службы")
            },
            {
                "TASK",
                Lang.T("Планировщик")
            },
            {
                "UPD",
                Lang.T("Обновления Windows")
            },
            {
                "EDGE",
                "Microsoft Edge"
            },
            {
                "APPS",
                Lang.T("Приложения")
            },
            {
                "SYS",
                Lang.T("Питание, сеть, диск")
            },
            {
                "CLEAN",
                Lang.T("Очистка")
            }
        };
        internal static List<Tweak> Load()
        {
            var definitions = File.ReadAllLines(Path.Combine(Program.Data, "data", "tweaks.def")).Where(l => l.Length > 0 && !l.StartsWith("#")).Select(l => l.Split('|')).ToDictionary(p => p[0]);
            var result = new List<Tweak>();
            // English descriptions live in a UTF-8 copy of the catalogue; the CMD menu keeps using the CP866 Russian file.
            var english = Path.Combine(Program.Data, "data", "descr.en");
            var lines = Lang.English && File.Exists(english) ? File.ReadAllLines(english, Encoding.UTF8) : File.ReadAllLines(Path.Combine(Program.Data, "data", "descr.ru"), Encoding.GetEncoding(866));
            foreach (var line in lines)
            {
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var p = line.Split('|');
                if (p.Length != 6 || !Regex.IsMatch(p[0], "^[A-Z][A-Z0-9-]{1,63}$"))
                    throw new IOException("Invalid catalogue description.");
                string[] def;
                bool known = definitions.TryGetValue(p[0], out def);
                if (!known && p[1] != "SYS" && p[1] != "CLEAN")
                    continue;
                result.Add(new Tweak { Id = p[0], Category = p[1], Title = p[2], Description = p[3], Caveat = p[4] == "-" ? Lang.T("Дополнительных условий нет.") : p[4], Compatibility = p[5] == "-" ? "" : p[5], Kind = known ? def[4] : p[1], Risk = known ? def[2] : "med", Os = known ? def[3] : "any", Target = known ? def[5] : null, ValueName = known ? def[6] : null, ValueType = known ? def[7] : null, Value = known ? def[8] : null });
            }

            return result;
        }
    }

    internal sealed class Preferences
    {
        public string Theme = "system";
        public string Language = "system";
        public bool AutoCheck = true;
        public bool AutoInstall = true;
        public bool IncludePreview = Program.Version.Contains("-");
        public bool RestorePoint = true;
        public bool VerifyAfterUpdates = true;
        public string WindowsBuild;
        public List<string> Favorites = new List<string>();
        public List<string> Plan = new List<string>();
        internal static Preferences Load()
        {
            try
            {
                var result = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Program.Data, "preferences.json"))) ?? new Preferences();
                if (!new[]
                {
                    "system",
                    "light",
                    "dark"
                }.Contains(result.Theme))
                    result.Theme = "system";
                if (!new[]
                {
                    "system",
                    "ru",
                    "en"
                }.Contains(result.Language))
                    result.Language = "system";
                if (result.Favorites == null)
                    result.Favorites = new List<string>();
                if (result.Plan == null)
                    result.Plan = new List<string>();
                return result;
            }
            catch
            {
                return new Preferences();
            }
        }

        internal void Save()
        {
            var path = Path.Combine(Program.Data, "preferences.json");
            var temp = path + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
    }
}
