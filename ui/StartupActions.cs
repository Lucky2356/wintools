using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools
{
    internal sealed class StartupChange
    {
        public string Schema, Id, Source, Name, Identity, Before, After, TimeUtc, Status, Action, Error;
    }

    internal static class StartupActions
    {
        private static readonly RecordStore Store = new RecordStore("startup-history", Lang.T("автозагрузка"), 131072);
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
                throw new IOException(Lang.T("Некорректный номер изменения автозагрузки."));
            return Program.Under(DirectoryPath, id + ".json");
        }

        private static void Save(StartupChange record)
        {
            Store.Save(RecordPath(record.Id), record);
        }

        internal static StartupChange Read(string id)
        {
            try
            {
                var record = Store.Load<StartupChange>(RecordPath(id));
                DateTime time;
                if (record == null || record.Schema != "wintools/startup-change/1" || record.Id != id || !Regex.IsMatch(record.Identity ?? "", "^[a-f0-9]{64}$") || !StartupEntries.Decode(record.Before).HasValue || !StartupEntries.Decode(record.After).HasValue || !new[]
                {
                    "enable",
                    "disable",
                    "restore"
                }.Contains(record.Action) || !new[]
                {
                    "PENDING",
                    "OK",
                    "FAILED",
                    "REVERTED"
                }.Contains(record.Status) || !DateTime.TryParseExact(record.TimeUtc, "o", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out time))
                    throw new IOException(Lang.T("Некорректная запись автозагрузки."));
                StartupEntries.Validate(record.Source, record.Name);
                return record;
            }
            catch (ArgumentException ex)
            {
                throw new IOException(Lang.T("Повреждена история автозагрузки."), ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new IOException(Lang.T("Повреждена история автозагрузки."), ex);
            }
        }

        internal static StartupChange[] History()
        {
            return Store.All(Read, r => r.TimeUtc);
        }

        internal static async Task<EngineResult> Run(StartupEntry entry, string action, string restore)
        {
            StartupEntries.Validate(entry.Source, entry.Name);
            string id = Guid.NewGuid().ToString("N"), sid = WindowsIdentity.GetCurrent().User.Value;
            var info = new ProcessStartInfo(Program.Exe, "--startup-worker " + action + " " + entry.Source + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Name)) + " " + entry.Fingerprint + " " + id + " " + sid + " " + (restore ?? "-"))
            {
                UseShellExecute = true,
                WorkingDirectory = Program.Home,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (entry.Source.StartsWith("machine-") || entry.Source == StartupTasks.Source)
                info.Verb = "runas";
            using (var process = Process.Start(info))
            {
                while (!process.HasExited)
                    await Task.Delay(400);
                string path = Path.Combine(Program.Data, "runtime", id + ".startup.log");
                return new EngineResult
                {
                    Code = process.ExitCode,
                    Output = File.Exists(path) ? File.ReadAllText(path) : Lang.T("Действие автозагрузки завершилось без отчёта.")
                };
            }
        }

        internal static int Worker(string[] args)
        {
            if (args.Length != 8 || !new[]
            {
                "enable",
                "disable",
                "restore"
            }.Contains(args[1]) || !Regex.IsMatch(args[4], "^[a-f0-9]{64}$") || !Regex.IsMatch(args[5], "^[a-f0-9]{32}$") || args[6] != WindowsIdentity.GetCurrent().User.Value)
                throw new ArgumentException(Lang.T("Запрос автозагрузки некорректен или права повышены под другим пользователем."));
            string action = args[1], source = args[2], name = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(args[3])), id = args[5];
            StartupEntries.Validate(source, name);
            if (action == "restore")
                RecordPath(args[7]);
            else if (args[7] != "-")
                throw new ArgumentException(Lang.T("Некорректный запрос возврата."));
            if (!Directory.Exists(Program.Data))
                throw new IOException(Lang.T("Сначала запустите интерфейс Wintools."));
            Program.SafeDirectory(Program.Data);
            string runtime = Path.Combine(Program.Data, "runtime");
            Program.SafeDirectory(runtime);
            Directory.CreateDirectory(runtime);
            using (var log = new StreamWriter(new FileStream(Path.Combine(runtime, id + ".startup.log"), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
            {
                string lockPath = Path.Combine(Program.Data, "state", "run.lock");
                Program.SafeDirectory(Path.GetDirectoryName(lockPath));
                FileStream gate = null;
                StartupChange record = null;
                try
                {
                    gate = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var before = StartupEntries.Inspect(source, name);
                    if (!before.CanChange)
                        throw new IOException(before.Error ?? before.Restriction ?? Lang.T("Состояние неизвестно."));
                    if (before.Fingerprint != args[4])
                        throw new IOException(Lang.T("Запись изменилась после чтения. Обновите список и повторите выбор."));
                    StartupChange original = null;
                    string target;
                    if (action == "restore")
                    {
                        original = Read(args[7]);
                        if (original.Source != source || original.Name != name || original.Identity != before.Identity || original.Action == "restore" || original.Status == "REVERTED")
                            throw new IOException(Lang.T("Запись не подходит для возврата: программа изменилась или откат уже выполнен."));
                        if (before.Approval != original.After)
                            throw new IOException(Lang.T("Состояние изменено после этого действия. Сначала отмените более поздние изменения."));
                        target = original.Before;
                    }
                    else
                    {
                        if (before.Enabled.Value == (action == "enable"))
                        {
                            log.WriteLine(Lang.T("Это состояние уже установлено. Изменений нет."));
                            return 0;
                        }

                        target = source == StartupTasks.Source ? StartupTasks.Encode(action == "enable") : StartupEntries.Encode(before.Approval, action == "enable");
                    }

                    record = new StartupChange
                    {
                        Schema = "wintools/startup-change/1",
                        Id = id,
                        Source = source,
                        Name = name,
                        Identity = before.Identity,
                        Before = before.Approval,
                        After = target,
                        Action = action,
                        TimeUtc = DateTime.UtcNow.ToString("o"),
                        Status = "PENDING"
                    };
                    Save(record);
                    var latest = StartupEntries.Inspect(source, name);
                    if (latest.Fingerprint != before.Fingerprint)
                        throw new IOException(Lang.T("Запись изменилась во время подготовки. Повторите чтение."));
                    StartupEntries.WriteApproval(source, name, target, before.Fingerprint);
                    var after = StartupEntries.Inspect(source, name);
                    if (after.Identity != before.Identity || after.Approval != target)
                        throw new IOException(Lang.T("Windows не подтвердила ожидаемое состояние. Обновите список."));
                    record.Status = "OK";
                    Save(record);
                    if (original != null)
                    {
                        original.Status = "REVERTED";
                        Save(original);
                    }

                    log.WriteLine(name + ": " + after.State + Lang.T(". Изменение относится к следующему входу в Windows; уже работающая программа не остановлена. Запись истории: ") + id);
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
                            Save(record);
                        }
                        catch (Exception saveError)
                        {
                            log.WriteLine(Lang.T("История требует проверки: ") + saveError.Message);
                        }
                    }

                    log.WriteLine(Lang.T("Не удалось завершить изменение: ") + ex.Message);
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
    }
}
