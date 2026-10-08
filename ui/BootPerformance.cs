using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;

namespace Wintools
{
    internal sealed class BootRecord
    {
        public string TimeUtc;
        public long TotalMs, MainPathMs, PostBootMs;
    }

    internal sealed class BootDelay
    {
        public string Kind, Name, TimeUtc;
        public long DegradationMs, TotalMs;
    }

    internal sealed class BootReport
    {
        public BootRecord[] Boots = new BootRecord[0];
        public BootDelay[] Delays = new BootDelay[0];
    }

    internal sealed class BootCulprit
    {
        public string Title { get; set; }
        public string Detail { get; set; }
    }

    // Windows measures every boot itself and logs it to Diagnostics-Performance: event 100 is the boot, 101-109 name what slowed it down.
    internal static class BootPerformance
    {
        internal const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";
        private static readonly Dictionary<int, string> Kinds = new Dictionary<int, string>
        {
            {
                101,
                Lang.T("Программа")
            },
            {
                102,
                Lang.T("Драйвер")
            },
            {
                103,
                Lang.T("Служба")
            },
            {
                106,
                Lang.T("Фоновая оптимизация")
            },
            {
                109,
                Lang.T("Устройство")
            }
        };
        internal static BootReport Read()
        {
            var boots = new List<BootRecord>();
            var delays = new List<BootDelay>();
            var query = new EventLogQuery(Log, PathType.LogName, "*[System[(EventID=100 or EventID=101 or EventID=102 or EventID=103 or EventID=106 or EventID=109)]]")
            {
                ReverseDirection = true
            };
            using (var reader = new EventLogReader(query))
            {
                for (int i = 0; i < 2000; i++)
                {
                    using (var record = reader.ReadEvent())
                    {
                        if (record == null)
                            break;
                        var time = record.TimeCreated.HasValue ? record.TimeCreated.Value.ToUniversalTime() : DateTime.MinValue;
                        if (record.Id == 100)
                        {
                            if (boots.Count < 30)
                            {
                                var boot = ParseBoot(record.ToXml(), time);
                                if (boot != null)
                                    boots.Add(boot);
                            }
                        }
                        else if (delays.Count < 500)
                        {
                            var delay = ParseDelay(record.Id, record.ToXml(), time);
                            if (delay != null)
                                delays.Add(delay);
                        }
                    }
                }
            }

            return new BootReport
            {
                Boots = boots.ToArray(),
                Delays = delays.ToArray()
            };
        }

        private static Dictionary<string, string> Data(string xml)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var document = new XmlDocument
            {
                XmlResolver = null
            };
            using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                document.Load(reader);
            foreach (XmlNode node in document.GetElementsByTagName("Data"))
            {
                var name = node.Attributes == null ? null : node.Attributes["Name"];
                if (name != null && !result.ContainsKey(name.Value))
                    result[name.Value] = node.InnerText;
            }

            return result;
        }

        private static long Number(Dictionary<string, string> data, string name)
        {
            string text;
            long value;
            return data.TryGetValue(name, out text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0 && value < 86400000 ? value : -1;
        }

        internal static BootRecord ParseBoot(string xml, DateTime timeUtc)
        {
            var data = Data(xml);
            long total = Number(data, "BootTime");
            if (total <= 0)
                return null;
            return new BootRecord
            {
                TimeUtc = timeUtc.ToString("o"),
                TotalMs = total,
                MainPathMs = Math.Max(0, Number(data, "MainPathBootTime")),
                PostBootMs = Math.Max(0, Number(data, "BootPostBootTime"))
            };
        }

        internal static BootDelay ParseDelay(int id, string xml, DateTime timeUtc)
        {
            string kind;
            if (!Kinds.TryGetValue(id, out kind))
                return null;
            var data = Data(xml);
            string name, friendly;
            data.TryGetValue("FriendlyName", out friendly);
            data.TryGetValue("Name", out name);
            var label = !string.IsNullOrWhiteSpace(friendly) ? friendly.Trim() : !string.IsNullOrWhiteSpace(name) ? Path.GetFileName(name.Trim()) : null;
            if (label == null)
                return null;
            if (label.Length > 120)
                label = label.Substring(0, 120);
            long degradation = Number(data, "DegradationTime");
            if (degradation < 0)
                return null;
            return new BootDelay
            {
                Kind = kind,
                Name = label,
                TimeUtc = timeUtc.ToString("o"),
                DegradationMs = degradation,
                TotalMs = Math.Max(0, Number(data, "TotalTime"))
            };
        }

        internal static string Seconds(long milliseconds)
        {
            return (milliseconds / 1000.0).ToString("0.0", Lang.Culture) + Lang.T(" с");
        }

        internal static BootCulprit[] Culprits(BootReport report)
        {
            return report.Delays.GroupBy(d => d.Kind + "|" + d.Name, StringComparer.OrdinalIgnoreCase).Select(g => new { Kind = g.First().Kind, Name = g.First().Name, Count = g.Count(), Max = g.Max(d => d.DegradationMs), Average = (long)g.Average(d => d.DegradationMs), Last = g.Max(d => d.TimeUtc) }).OrderByDescending(g => g.Average * g.Count).Take(15).Select(g => new BootCulprit { Title = g.Name + " · " + g.Kind.ToLowerInvariant(), Detail = Lang.T("Замедлял загрузку ") + g.Count + Lang.T(" раз(а): в среднем на ") + Seconds(g.Average) + Lang.T(", максимум ") + Seconds(g.Max) + Lang.T(". Последний раз ") + DateTime.Parse(g.Last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime().ToString("d", Lang.Culture) }).ToArray();
        }

        internal static string Summary(BootReport report)
        {
            if (report.Boots.Length == 0)
                return Lang.T("Windows ещё не записала ни одной измеренной загрузки. Записи появляются после обычного включения ПК (не после перезапуска из спящего режима и быстрого запуска).");
            var last = report.Boots[0];
            var average = (long)report.Boots.Average(b => b.TotalMs);
            return Lang.T("Последняя загрузка ") + DateTime.Parse(last.TimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime().ToString("g", Lang.Culture) + ": " + Seconds(last.TotalMs) + Lang.T(" (до рабочего стола ") + Seconds(last.MainPathMs) + Lang.T(", фоновый запуск после входа ") + Seconds(last.PostBootMs) + Lang.T(").\nСреднее по ") + report.Boots.Length + Lang.T(" загрузкам: ") + Seconds(average) + Lang.T(". Быстрее всего: ") + Seconds(report.Boots.Min(b => b.TotalMs)) + Lang.T(", медленнее всего: ") + Seconds(report.Boots.Max(b => b.TotalMs)) + ".";
        }

        // The operational log usually requires elevation; the worker writes the parsed report for the non-elevated UI.
        internal static async Task<BootReport> ReadElevated()
        {
            string token = Guid.NewGuid().ToString("N"), sid = WindowsIdentity.GetCurrent().User.Value;
            var info = new ProcessStartInfo(Program.Exe, "--boot-worker " + token + " " + sid)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Program.Home,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(info))
            {
                while (!process.HasExited)
                    await Task.Delay(300);
                string path = Path.Combine(Program.Data, "runtime", token + ".boot.json");
                try
                {
                    if (process.ExitCode != 0 || !File.Exists(path))
                        throw new IOException(File.Exists(path) ? File.ReadAllText(path) : Lang.T("Журнал загрузки не прочитан."));
                    if (new FileInfo(path).Length > 4194304)
                        throw new IOException(Lang.T("Отчёт о загрузке слишком велик."));
                    return Validate(new JavaScriptSerializer { MaxJsonLength = 4194304 }.Deserialize<BootReport>(File.ReadAllText(path)));
                }
                finally
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        internal static BootReport Validate(BootReport report)
        {
            if (report == null || report.Boots == null || report.Delays == null || report.Boots.Length > 30 || report.Delays.Length > 500)
                throw new IOException(Lang.T("Некорректный отчёт о загрузке."));
            DateTime time;
            foreach (var boot in report.Boots)
                if (boot == null || boot.TotalMs <= 0 || boot.TotalMs >= 86400000 || boot.MainPathMs < 0 || boot.PostBootMs < 0 || !DateTime.TryParse(boot.TimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time))
                    throw new IOException(Lang.T("Некорректная запись загрузки."));
            foreach (var delay in report.Delays)
                if (delay == null || string.IsNullOrWhiteSpace(delay.Name) || delay.Name.Length > 120 || !Kinds.ContainsValue(delay.Kind) || delay.DegradationMs < 0 || delay.DegradationMs >= 86400000 || !DateTime.TryParse(delay.TimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time))
                    throw new IOException(Lang.T("Некорректная запись замедления."));
            return report;
        }

        internal static int Worker(string[] args)
        {
            if (args.Length != 3 || !Regex.IsMatch(args[1], "^[a-f0-9]{32}$") || args[2] != WindowsIdentity.GetCurrent().User.Value)
                throw new ArgumentException(Lang.T("Запрос журнала загрузки некорректен или права повышены под другим пользователем."));
            if (!Directory.Exists(Program.Data))
                throw new IOException(Lang.T("Сначала запустите интерфейс Wintools."));
            Program.SafeDirectory(Program.Data);
            string runtime = Path.Combine(Program.Data, "runtime");
            Program.SafeDirectory(runtime);
            Directory.CreateDirectory(runtime);
            string text;
            int code;
            try
            {
                text = new JavaScriptSerializer
                {
                    MaxJsonLength = 4194304
                }.Serialize(Read());
                code = 0;
            }
            catch (Exception ex)
            {
                text = Lang.T("Журнал загрузки не прочитан: ") + ex.Message;
                code = 4;
            }

            using (var file = new FileStream(Path.Combine(runtime, args[1] + ".boot.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                file.Write(bytes, 0, bytes.Length);
            }

            return code;
        }
    }
}
