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
        private static Dictionary<string, Tweak> index;

        // The engine checks every request against the catalogue; the files are read once per process.
        internal static Tweak Find(string id)
        {
            if (index == null)
                index = Load().ToDictionary(t => t.Id);
            Tweak tweak;
            return index.TryGetValue(id, out tweak) ? tweak : null;
        }

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
        // "system" follows the Windows text size; "100", "125" and "150" are fixed percentages.
        public string TextSize = "system";
        // Normal (not maximized) window bounds as Windows reports them, in pixels; null until the first close.
        public int[] WindowBounds;
        public bool WindowMaximized;
        public bool AutoCheck = true;
        public bool AutoInstall = true;
        public bool IncludePreview = Program.Version.Contains("-");
        public bool RestorePoint = true;
        public bool VerifyAfterUpdates = true;
        // "simple" hides expert tools and high-risk actions; "full" shows everything. New users start simple.
        public string Mode = "simple";
        // Fewer effects and slower live graphs for older or busy computers.
        public bool Lite;
        public bool Welcomed;
        // The version whose "What's new" was last shown; empty until the first start.
        public string SeenVersion = "";
        public string WindowsBuild;
        // History entries whose "needs attention" notice on the start page was already seen.
        public List<string> Acknowledged = new List<string>();
        public List<string> Favorites = new List<string>();
        public List<string> Plan = new List<string>();
        internal static readonly string[] TextSizes =
        {
            "system",
            "100",
            "125",
            "150",
            "175",
            "200"
        };

        internal static Preferences Load()
        {
            try
            {
                return Parse(File.ReadAllText(Path.Combine(Program.Data, "preferences.json")));
            }
            catch
            {
                return new Preferences();
            }
        }

        internal static Preferences Parse(string text)
        {
            var result = new JavaScriptSerializer().Deserialize<Preferences>(text) ?? new Preferences();
            // Preferences saved before the welcome existed belong to people who already know the program:
            // they keep every tool and skip the introduction, and their first "What's new" is this version's.
            if (!text.Contains("\"Welcomed\""))
            {
                result.Welcomed = true;
                result.Mode = "full";
                result.SeenVersion = "";
            }

            if (result.Mode != "simple" && result.Mode != "full")
                result.Mode = "full";
            if (result.SeenVersion == null)
                result.SeenVersion = "";
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
            if (!TextSizes.Contains(result.TextSize))
                result.TextSize = "system";
            if (result.Favorites == null)
                result.Favorites = new List<string>();
            if (result.Plan == null)
                result.Plan = new List<string>();
            return result;
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
