using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Wintools
{
    internal sealed class UpdateValue
    {
        public string Name, Kind, Data;
    }

    internal sealed class UpdateSettings
    {
        internal UpdateValue[] Values = new UpdateValue[0];
        internal DateTime? PausedUntil;
        internal int? ActiveStart, ActiveEnd;
        internal string Fingerprint
        {
            get
            {
                return WindowsUpdates.Fingerprint(Values);
            }
        }
    }

    internal sealed class UpdateChange
    {
        public string Schema, Id, Action, Detail, BeforeHash, AfterHash, TimeUtc, Status, Error;
        public UpdateValue[] Before;
    }

    internal sealed class UpdateHistoryRow
    {
        public string Title { get; set; }
        public string Detail { get; set; }
    }

    // Pause and active hours use the same per-machine values the Settings app writes; every change keeps the previous values for exact restoration.
    internal static class WindowsUpdates
    {
        internal const string Key = "SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings";
        internal static readonly string[] Names =
        {
            "PauseUpdatesExpiryTime",
            "PauseUpdatesStartTime",
            "PauseFeatureUpdatesStartTime",
            "PauseFeatureUpdatesEndTime",
            "PauseQualityUpdatesStartTime",
            "PauseQualityUpdatesEndTime",
            "ActiveHoursStart",
            "ActiveHoursEnd",
            "SmartActiveHoursState"
        };
        private static readonly string[] Times =
        {
            "PauseUpdatesExpiryTime",
            "PauseUpdatesStartTime",
            "PauseFeatureUpdatesStartTime",
            "PauseFeatureUpdatesEndTime",
            "PauseQualityUpdatesStartTime",
            "PauseQualityUpdatesEndTime"
        };
        internal static string Fingerprint(UpdateValue[] values)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", values.OrderBy(v => v.Name, StringComparer.Ordinal).Select(v => v.Name + "=" + v.Kind + ":" + v.Data))))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        internal static UpdateSettings Read()
        {
            var values = new List<UpdateValue>();
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = root.OpenSubKey(Key, false))
            {
                if (key != null)
                    foreach (var name in Names)
                    {
                        if (!key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                            continue;
                        var kind = key.GetValueKind(name);
                        var data = key.GetValue(name);
                        if (kind == RegistryValueKind.DWord)
                            values.Add(new UpdateValue { Name = name, Kind = "dword", Data = unchecked((uint)(int)data).ToString(CultureInfo.InvariantCulture) });
                        else if (kind == RegistryValueKind.String)
                            values.Add(new UpdateValue { Name = name, Kind = "string", Data = Convert.ToString(data, CultureInfo.InvariantCulture) });
                    }
            }

            return Describe(values.ToArray());
        }

        internal static UpdateSettings Describe(UpdateValue[] values)
        {
            var result = new UpdateSettings
            {
                Values = values
            };
            var expiry = values.FirstOrDefault(v => v.Name == "PauseUpdatesExpiryTime");
            DateTime until;
            if (expiry != null && DateTime.TryParse(expiry.Data, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out until) && until > DateTime.UtcNow)
                result.PausedUntil = until;
            var start = values.FirstOrDefault(v => v.Name == "ActiveHoursStart");
            var end = values.FirstOrDefault(v => v.Name == "ActiveHoursEnd");
            int a, b;
            if (start != null && end != null && int.TryParse(start.Data, out a) && int.TryParse(end.Data, out b) && a >= 0 && a < 24 && b >= 0 && b < 24)
            {
                result.ActiveStart = a;
                result.ActiveEnd = b;
            }

            return result;
        }

        internal static string Iso(DateTime utc)
        {
            return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        // Target values for an action; a null data removes the value.
        internal static Dictionary<string, UpdateValue> Plan(string action, string argument, DateTime nowUtc)
        {
            var result = new Dictionary<string, UpdateValue>();
            if (action == "pause")
            {
                int days = int.Parse(argument, CultureInfo.InvariantCulture);
                string start = Iso(nowUtc), end = Iso(nowUtc.AddDays(days));
                foreach (var name in new[]
                {
                    "PauseUpdatesStartTime",
                    "PauseFeatureUpdatesStartTime",
                    "PauseQualityUpdatesStartTime"
                }

                )
                    result[name] = new UpdateValue
                    {
                        Name = name,
                        Kind = "string",
                        Data = start
                    };
                foreach (var name in new[]
                {
                    "PauseUpdatesExpiryTime",
                    "PauseFeatureUpdatesEndTime",
                    "PauseQualityUpdatesEndTime"
                }

                )
                    result[name] = new UpdateValue
                    {
                        Name = name,
                        Kind = "string",
                        Data = end
                    };
            }
            else if (action == "resume")
            {
                foreach (var name in Times)
                    result[name] = new UpdateValue
                    {
                        Name = name
                    };
            }
            else if (action == "hours")
            {
                var parts = argument.Split('-');
                result["ActiveHoursStart"] = new UpdateValue
                {
                    Name = "ActiveHoursStart",
                    Kind = "dword",
                    Data = int.Parse(parts[0], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                };
                result["ActiveHoursEnd"] = new UpdateValue
                {
                    Name = "ActiveHoursEnd",
                    Kind = "dword",
                    Data = int.Parse(parts[1], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                };
                result["SmartActiveHoursState"] = new UpdateValue
                {
                    Name = "SmartActiveHoursState",
                    Kind = "dword",
                    Data = "0"
                };
            }
            else
                throw new ArgumentException("Неизвестное действие Windows Update.");
            return result;
        }

        internal static void Validate(string action, string argument, string expected, string restore)
        {
            if (!Regex.IsMatch(expected ?? "", "^[a-f0-9]{16}$"))
                throw new ArgumentException("Некорректный запрос Windows Update.");
            if (action == "pause")
            {
                int days;
                if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out days) || days < 1 || days > 35)
                    throw new ArgumentException("Пауза возможна на 1–35 дней.");
            }
            else if (action == "hours")
            {
                var match = Regex.Match(argument ?? "", "^([0-9]{1,2})-([0-9]{1,2})$");
                int a, b;
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out a) || !int.TryParse(match.Groups[2].Value, out b) || a > 23 || b > 23 || a == b || (b - a + 24) % 24 > 18)
                    throw new ArgumentException("Часы активности: начало и конец от 0 до 23, не более 18 часов.");
            }
            else if (action == "resume")
            {
                if (argument != "-")
                    throw new ArgumentException("Некорректный запрос Windows Update.");
            }
            else if (action == "restore")
            {
                if (argument != "-" || restore == null || restore == "-")
                    throw new ArgumentException("Не указана запись для возврата.");
                RecordPath(restore);
            }
            else
                throw new ArgumentException("Неизвестное действие Windows Update.");
            if (action != "restore" && restore != null && restore != "-")
                throw new ArgumentException("Некорректный запрос Windows Update.");
        }

        private static void Write(IEnumerable<UpdateValue> values)
        {
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = root.CreateSubKey(Key, true))
            {
                foreach (var value in values)
                {
                    if (!Names.Contains(value.Name))
                        throw new IOException("Недопустимое значение Windows Update.");
                    if (value.Data == null)
                    {
                        if (key.GetValueNames().Contains(value.Name, StringComparer.OrdinalIgnoreCase))
                            key.DeleteValue(value.Name, false);
                    }
                    else if (value.Kind == "dword")
                        key.SetValue(value.Name, unchecked((int)uint.Parse(value.Data, CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
                    else
                        key.SetValue(value.Name, value.Data, RegistryValueKind.String);
                }
            }
        }

        private static readonly RecordStore Store = new RecordStore("update-history", "Windows Update", 65536);
        private static string DirectoryPath
        {
            get
            {
                return Store.Folder;
            }
        }

        private static string RecordPath(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$"))
                throw new IOException("Некорректный номер изменения Windows Update.");
            return Program.Under(DirectoryPath, id + ".json");
        }

        private static void Save(UpdateChange record)
        {
            Store.Save(RecordPath(record.Id), record);
        }

        private static bool ValidValues(UpdateValue[] values)
        {
            return values != null && values.Length <= Names.Length && values.All(v => v != null && Names.Contains(v.Name) && (v.Kind == "dword" ? Regex.IsMatch(v.Data ?? "", "^[0-9]{1,10}$") : v.Kind == "string" && v.Data != null && v.Data.Length <= 64)) && values.Select(v => v.Name).Distinct().Count() == values.Length;
        }

        internal static UpdateChange ReadRecord(string id)
        {
            try
            {
                var record = Store.Load<UpdateChange>(RecordPath(id));
                DateTime time;
                if (record == null || record.Schema != "wintools/update-change/1" || record.Id != id || !new[]
                {
                    "pause",
                    "resume",
                    "hours",
                    "restore"
                }.Contains(record.Action) || string.IsNullOrEmpty(record.Detail) || record.Detail.Length > 200 || !ValidValues(record.Before) || !Regex.IsMatch(record.BeforeHash ?? "", "^[a-f0-9]{16}$") || (record.AfterHash != null && !Regex.IsMatch(record.AfterHash, "^[a-f0-9]{16}$")) || !new[]
                {
                    "PENDING",
                    "OK",
                    "FAILED",
                    "REVERTED"
                }.Contains(record.Status) || !DateTime.TryParseExact(record.TimeUtc, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time))
                    throw new IOException("Некорректная запись Windows Update.");
                return record;
            }
            catch (ArgumentException ex)
            {
                throw new IOException("Повреждена история Windows Update.", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new IOException("Повреждена история Windows Update.", ex);
            }
        }

        internal static UpdateChange[] History()
        {
            return Store.All(ReadRecord, r => r.TimeUtc);
        }

        internal static string Detail(string action, string argument)
        {
            return action == "pause" ? "Пауза обновлений на " + argument + " дн." : action == "resume" ? "Возобновление обновлений" : action == "hours" ? "Часы активности " + argument.Replace("-", ":00–") + ":00" : "Возврат настроек обновлений";
        }

        internal static async Task<EngineResult> Run(string action, string argument, string expected, string restore)
        {
            Validate(action, argument, expected, restore);
            string token = Guid.NewGuid().ToString("N"), sid = WindowsIdentity.GetCurrent().User.Value;
            var info = new ProcessStartInfo(Program.Exe, "--update-worker " + action + " " + argument + " " + expected + " " + token + " " + sid + " " + (restore ?? "-"))
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Program.Home,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(info))
            {
                while (!process.HasExited)
                    await Task.Delay(400);
                string path = Path.Combine(Program.Data, "runtime", token + ".update.log");
                return new EngineResult
                {
                    Code = process.ExitCode,
                    Output = File.Exists(path) ? File.ReadAllText(path) : "Изменение Windows Update завершилось без отчёта."
                };
            }
        }

        internal static int Worker(string[] args)
        {
            if (args.Length != 7 || !Regex.IsMatch(args[4], "^[a-f0-9]{32}$") || args[5] != WindowsIdentity.GetCurrent().User.Value)
                throw new ArgumentException("Запрос Windows Update некорректен или права повышены под другим пользователем.");
            Validate(args[1], args[2], args[3], args[6]);
            string action = args[1], argument = args[2], expected = args[3], id = args[4];
            if (!Directory.Exists(Program.Data))
                throw new IOException("Сначала запустите интерфейс Wintools.");
            Program.SafeDirectory(Program.Data);
            string runtime = Path.Combine(Program.Data, "runtime");
            Program.SafeDirectory(runtime);
            Directory.CreateDirectory(runtime);
            using (var log = new StreamWriter(new FileStream(Path.Combine(runtime, id + ".update.log"), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
            {
                string lockPath = Path.Combine(Program.Data, "state", "run.lock");
                Program.SafeDirectory(Path.GetDirectoryName(lockPath));
                FileStream gate = null;
                UpdateChange record = null;
                try
                {
                    gate = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var before = Read();
                    if (before.Fingerprint != expected)
                        throw new IOException("Настройки обновлений изменились после чтения. Обновите состояние и повторите.");
                    UpdateChange original = null;
                    IEnumerable<UpdateValue> target;
                    if (action == "restore")
                    {
                        original = ReadRecord(args[6]);
                        if (original.Action == "restore" || original.Status == "REVERTED" || original.AfterHash == null)
                            throw new IOException("Запись не подходит для возврата.");
                        if (original.AfterHash != before.Fingerprint)
                            throw new IOException("Настройки обновлений изменены после этой записи. Возврат отменён.");
                        target = Names.Select(n => original.Before.FirstOrDefault(v => v.Name == n) ?? new UpdateValue { Name = n });
                    }
                    else
                        target = Plan(action, argument, DateTime.UtcNow).Values;
                    record = new UpdateChange
                    {
                        Schema = "wintools/update-change/1",
                        Id = id,
                        Action = action,
                        Detail = original == null ? Detail(action, argument) : "Возврат: " + original.Detail,
                        Before = before.Values,
                        BeforeHash = before.Fingerprint,
                        TimeUtc = DateTime.UtcNow.ToString("o"),
                        Status = "PENDING"
                    };
                    Save(record);
                    Write(target);
                    var after = Read();
                    record.AfterHash = after.Fingerprint;
                    record.Status = "OK";
                    Save(record);
                    if (original != null)
                    {
                        original.Status = "REVERTED";
                        Save(original);
                    }

                    log.WriteLine(record.Detail + ": выполнено." + (after.PausedUntil.HasValue ? " Обновления приостановлены до " + after.PausedUntil.Value.ToLocalTime().ToString("g") + "." : "") + " Запись истории: " + id);
                    return 0;
                }
                catch (Exception ex)
                {
                    if (record != null)
                    {
                        record.Status = "FAILED";
                        record.Error = ex.Message;
                        try
                        {
                            record.AfterHash = Read().Fingerprint;
                            Save(record);
                        }
                        catch (Exception saveError)
                        {
                            log.WriteLine("История требует проверки: " + saveError.Message);
                        }
                    }

                    log.WriteLine("Не удалось изменить настройки обновлений: " + ex.Message);
                    return 4;
                }
                finally
                {
                    if (gate != null)
                    {
                        gate.Dispose();
                        File.Delete(lockPath);
                    }
                }
            }
        }

        // Windows Update Agent history: what was installed or failed, newest first. Reading does not start a scan.
        internal static UpdateHistoryRow[] InstalledHistory(int count)
        {
            object session = null, searcher = null, entries = null;
            var result = new List<UpdateHistoryRow>();
            try
            {
                session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true));
                searcher = StartupTasks.Call(session, "CreateUpdateSearcher");
                int total = (int)StartupTasks.Call(searcher, "GetTotalHistoryCount");
                if (total == 0)
                    return new UpdateHistoryRow[0];
                entries = StartupTasks.Call(searcher, "QueryHistory", 0, Math.Min(total, count));
                int size = (int)StartupTasks.Get(entries, "Count");
                for (int i = 0; i < size; i++)
                {
                    object entry = null;
                    try
                    {
                        entry = StartupTasks.Get(entries, "Item", i);
                        string title = Convert.ToString(StartupTasks.Get(entry, "Title"));
                        if (string.IsNullOrWhiteSpace(title))
                            continue;
                        var date = (DateTime)StartupTasks.Get(entry, "Date");
                        int code = Convert.ToInt32(StartupTasks.Get(entry, "ResultCode"));
                        int operation = Convert.ToInt32(StartupTasks.Get(entry, "Operation"));
                        result.Add(new UpdateHistoryRow { Title = title.Length > 160 ? title.Substring(0, 160) + "…" : title, Detail = date.ToLocalTime().ToString("g") + " · " + (operation == 2 ? "удаление" : "установка") + " · " + (code == 2 ? "успешно" : code == 3 ? "с ошибками" : code == 4 ? "ошибка" : code == 5 ? "отменено" : code == 1 ? "выполняется" : "не начато") });
                    }
                    finally
                    {
                        StartupTasks.Release(entry);
                    }
                }
            }
            finally
            {
                StartupTasks.Release(entries);
                StartupTasks.Release(searcher);
                StartupTasks.Release(session);
            }

            return result.ToArray();
        }
    }
}
