using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools
{
    internal sealed class HostsEntry
    {
        public string Address { get; set; }
        public string Host { get; set; }
        public bool Managed { get; set; }

        public string Label
        {
            get
            {
                return Host + " → " + Address + (Managed ? Lang.T(" · добавлено Wintools") : "");
            }
        }
    }

    internal sealed class HostsChange
    {
        public string Schema, Id, Action, Domain, BeforeHash, AfterHash, TimeUtc, Status, Error;
    }

    // Blocks domains through the hosts file. Only lines marked by Wintools are removed; any edit keeps a full backup for exact restoration.
    internal static class HostsFile
    {
        internal const string Marker = "# wintools";
        internal static string Path
        {
            get
            {
                return System.IO.Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
            }
        }

        private static readonly RecordStore Store = new RecordStore("hosts-history", "hosts", 65536);
        private static string DirectoryPath
        {
            get
            {
                return Store.Folder;
            }
        }

        internal static string Hash(byte[] content)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(content ?? new byte[0])).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        internal static byte[] ReadBytes()
        {
            var info = new FileInfo(Path);
            if (!info.Exists)
                return new byte[0];
            if (info.Length > 4194304)
                throw new IOException(Lang.T("Файл hosts больше 4 МБ: изменять его автоматически небезопасно."));
            return File.ReadAllBytes(Path);
        }

        // Lower-case ASCII host name; international names are converted to punycode. Wildcards, ports and paths are rejected.
        internal static string NormalizeDomain(string value)
        {
            var text = (value ?? "").Trim().TrimEnd('.');
            if (text.Length == 0 || text.Length > 253)
                throw new ArgumentException(Lang.T("Введите имя сайта, например ads.example.com."));
            try
            {
                text = new IdnMapping().GetAscii(text).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                throw new ArgumentException(Lang.T("Имя сайта содержит недопустимые символы."));
            }

            if (!Regex.IsMatch(text, @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}$") || text == "localhost")
                throw new ArgumentException(Lang.T("Введите имя сайта без http://, порта и пути, например ads.example.com."));
            return text;
        }

        internal static HostsEntry[] Parse(byte[] content)
        {
            var result = new List<HostsEntry>();
            foreach (var raw in Decode(content).Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                bool managed = line.TrimEnd().EndsWith(Marker, StringComparison.Ordinal);
                int comment = line.IndexOf('#');
                var data = (comment >= 0 ? line.Substring(0, comment) : line).Trim();
                if (data.Length == 0)
                    continue;
                var parts = data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;
                foreach (var host in parts.Skip(1))
                    result.Add(new HostsEntry { Address = parts[0], Host = host, Managed = managed && parts.Length == 2 });
            }

            return result.ToArray();
        }

        private static string Decode(byte[] content)
        {
            if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
                return Encoding.UTF8.GetString(content, 3, content.Length - 3);
            return Encoding.UTF8.GetString(content);
        }

        internal static string Line(string domain)
        {
            return "0.0.0.0 " + domain + " " + Marker;
        }

        internal static byte[] Add(byte[] content, string domain)
        {
            domain = NormalizeDomain(domain);
            if (Parse(content).Any(e => e.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(Lang.T("Для ") + domain + Lang.T(" в hosts уже есть запись."));
            var prefix = content.Length > 0 && content[content.Length - 1] != (byte)'\n' ? "\r\n" : "";
            var addition = Encoding.ASCII.GetBytes(prefix + Line(domain) + "\r\n");
            return content.Concat(addition).ToArray();
        }

        internal static byte[] Remove(byte[] content, string domain)
        {
            domain = NormalizeDomain(domain);
            var target = Line(domain);
            var text = Decode(content);
            bool bom = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF;
            var lines = text.Split('\n').ToList();
            int removed = lines.RemoveAll(l => l.TrimEnd('\r').Trim() == target);
            if (removed == 0)
                throw new InvalidOperationException(Lang.T("Запись Wintools для ") + domain + Lang.T(" не найдена."));
            var bytes = new UTF8Encoding(false).GetBytes(string.Join("\n", lines));
            return bom ? new byte[]
            {
                0xEF,
                0xBB,
                0xBF
            }.Concat(bytes).ToArray() : bytes;
        }

        private static string RecordPath(string id, string extension)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$"))
                throw new IOException(Lang.T("Некорректный номер изменения hosts."));
            return Program.Under(DirectoryPath, id + extension);
        }

        private static void SaveFile(string path, byte[] bytes)
        {
            Store.Write(path, bytes);
        }

        private static void Save(HostsChange record)
        {
            Store.Save(RecordPath(record.Id, ".json"), record);
        }

        internal static HostsChange Read(string id)
        {
            try
            {
                var record = Store.Load<HostsChange>(RecordPath(id, ".json"));
                DateTime time;
                if (record == null || record.Schema != "wintools/hosts-change/1" || record.Id != id || !new[]
                {
                    "block",
                    "unblock",
                    "restore"
                }.Contains(record.Action) || !Regex.IsMatch(record.BeforeHash ?? "", "^[a-f0-9]{16}$") || (record.AfterHash != null && !Regex.IsMatch(record.AfterHash, "^[a-f0-9]{16}$")) || (record.Action != "restore" && NormalizeDomain(record.Domain) != record.Domain) || !new[]
                {
                    "PENDING",
                    "OK",
                    "FAILED",
                    "REVERTED"
                }.Contains(record.Status) || !DateTime.TryParseExact(record.TimeUtc, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time))
                    throw new IOException(Lang.T("Некорректная запись hosts."));
                return record;
            }
            catch (ArgumentException ex)
            {
                throw new IOException(Lang.T("Повреждена история hosts."), ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new IOException(Lang.T("Повреждена история hosts."), ex);
            }
        }

        internal static byte[] Backup(string id)
        {
            string path = RecordPath(id, ".hosts");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4194304 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Lang.T("Резервная копия hosts не найдена."));
            return File.ReadAllBytes(path);
        }

        internal static HostsChange[] History()
        {
            return Store.All(Read, r => r.TimeUtc);
        }

        internal static void Validate(string action, string domain, string expected, string restore)
        {
            if (!Regex.IsMatch(expected ?? "", "^[a-f0-9]{16}$"))
                throw new ArgumentException(Lang.T("Некорректный запрос hosts."));
            if (action == "restore")
            {
                if (domain != "-" || restore == null || restore == "-")
                    throw new ArgumentException(Lang.T("Не указана запись для возврата hosts."));
                RecordPath(restore, ".json");
            }
            else if (action == "block" || action == "unblock")
            {
                if (NormalizeDomain(domain) != domain || (restore != null && restore != "-"))
                    throw new ArgumentException(Lang.T("Некорректный запрос hosts."));
            }
            else
                throw new ArgumentException(Lang.T("Неизвестное действие hosts."));
        }

        internal static async Task<EngineResult> Run(string action, string domain, string expected, string restore)
        {
            Validate(action, domain, expected, restore);
            string token = Guid.NewGuid().ToString("N"), sid = WindowsIdentity.GetCurrent().User.Value;
            var info = new ProcessStartInfo(Program.Exe, "--hosts-worker " + action + " " + domain + " " + expected + " " + token + " " + sid + " " + (restore ?? "-"))
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
                string path = System.IO.Path.Combine(Program.Data, "runtime", token + ".hosts.log");
                return new EngineResult
                {
                    Code = process.ExitCode,
                    Output = File.Exists(path) ? File.ReadAllText(path) : Lang.T("Изменение hosts завершилось без отчёта.")
                };
            }
        }

        [DllImport("dnsapi.dll")]
        private static extern bool DnsFlushResolverCache();
        // Truncating in place keeps the file's permissions; a full copy of the previous content is saved first.
        private static void Write(byte[] content)
        {
            using (var file = new FileStream(Path, FileMode.Truncate, FileAccess.Write, FileShare.None))
            {
                file.Write(content, 0, content.Length);
                file.Flush(true);
            }

            try
            {
                DnsFlushResolverCache();
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }
        }

        internal static int Worker(string[] args)
        {
            if (args.Length != 7 || !Regex.IsMatch(args[4], "^[a-f0-9]{32}$") || args[5] != WindowsIdentity.GetCurrent().User.Value)
                throw new ArgumentException(Lang.T("Запрос hosts некорректен или права повышены под другим пользователем."));
            Validate(args[1], args[2], args[3], args[6]);
            string action = args[1], domain = args[2], expected = args[3], id = args[4];
            if (!Directory.Exists(Program.Data))
                throw new IOException(Lang.T("Сначала запустите интерфейс Wintools."));
            Program.SafeDirectory(Program.Data);
            string runtime = System.IO.Path.Combine(Program.Data, "runtime");
            Program.SafeDirectory(runtime);
            Directory.CreateDirectory(runtime);
            using (var log = new StreamWriter(new FileStream(System.IO.Path.Combine(runtime, id + ".hosts.log"), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
            {
                string lockPath = System.IO.Path.Combine(Program.Data, "state", "run.lock");
                Program.SafeDirectory(System.IO.Path.GetDirectoryName(lockPath));
                FileStream gate = null;
                HostsChange record = null;
                try
                {
                    gate = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    if (!File.Exists(Path) || (File.GetAttributes(Path) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException(Lang.T("Файл hosts отсутствует или является ссылкой."));
                    var before = ReadBytes();
                    if (Hash(before) != expected)
                        throw new IOException(Lang.T("Файл hosts изменился после чтения. Обновите список и повторите."));
                    HostsChange original = null;
                    byte[] next;
                    if (action == "restore")
                    {
                        original = Read(args[6]);
                        if (original.Action == "restore" || original.Status == "REVERTED" || original.AfterHash == null)
                            throw new IOException(Lang.T("Запись не подходит для возврата hosts."));
                        if (original.AfterHash != Hash(before))
                            throw new IOException(Lang.T("Файл hosts изменён после этой записи. Возврат отменён, чтобы не потерять более поздние изменения."));
                        next = Backup(original.Id);
                    }
                    else
                        next = action == "block" ? Add(before, domain) : Remove(before, domain);
                    record = new HostsChange
                    {
                        Schema = "wintools/hosts-change/1",
                        Id = id,
                        Action = action,
                        Domain = action == "restore" ? original.Domain : domain,
                        BeforeHash = Hash(before),
                        TimeUtc = DateTime.UtcNow.ToString("o"),
                        Status = "PENDING"
                    };
                    SaveFile(RecordPath(id, ".hosts"), before);
                    Save(record);
                    Write(next);
                    var after = ReadBytes();
                    if (!after.SequenceEqual(next))
                        throw new IOException(Lang.T("Windows не подтвердила запись hosts. Возможно, файл защищён антивирусом."));
                    record.AfterHash = Hash(after);
                    record.Status = "OK";
                    Save(record);
                    if (original != null)
                    {
                        original.Status = "REVERTED";
                        Save(original);
                    }

                    log.WriteLine((action == "block" ? Lang.T("Заблокирован ") : action == "unblock" ? Lang.T("Разблокирован ") : Lang.T("Восстановлен прежний hosts для ")) + record.Domain + Lang.T(". Запись истории: ") + id);
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
                            record.AfterHash = Hash(ReadBytes());
                            Save(record);
                        }
                        catch (Exception saveError)
                        {
                            log.WriteLine(Lang.T("История требует проверки: ") + saveError.Message);
                        }
                    }

                    log.WriteLine(Lang.T("Не удалось изменить hosts: ") + ex.Message);
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
