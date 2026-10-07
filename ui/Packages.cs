using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Wintools
{
    internal sealed class PackageInfo
    {
        public string Id, Name, Group, Description;
    }

    internal sealed class PackageChange
    {
        public string Schema, Id, Package, Name, Action, Status, TimeUtc;
        public long Code;
    }

    // Installs and updates curated packages from the official winget source by exact identifier.
    internal static class Packages
    {
        internal static readonly PackageInfo[] Catalog =
        {
            P("Mozilla.Firefox", "Firefox", "Браузеры", "Браузер Mozilla с открытым исходным кодом."),
            P("Google.Chrome", "Google Chrome", "Браузеры", "Браузер Google."),
            P("Brave.Brave", "Brave", "Браузеры", "Браузер на основе Chromium со встроенной блокировкой рекламы."),
            P("Vivaldi.Vivaldi", "Vivaldi", "Браузеры", "Браузер на основе Chromium с гибкой настройкой интерфейса."),
            P("7zip.7zip", "7-Zip", "Файлы и система", "Архиватор: 7z, zip, rar и другие форматы."),
            P("voidtools.Everything", "Everything", "Файлы и система", "Мгновенный поиск файлов по имени на дисках NTFS."),
            P("Notepad++.Notepad++", "Notepad++", "Файлы и система", "Текстовый редактор с подсветкой синтаксиса."),
            P("Microsoft.PowerToys", "PowerToys", "Файлы и система", "Утилиты Microsoft: раскладка окон, переименование файлов, палитра цветов и другие."),
            P("WinDirStat.WinDirStat", "WinDirStat", "Файлы и система", "Показывает, какие папки и файлы занимают место на диске."),
            P("Klocman.BulkCrapUninstaller", "Bulk Crap Uninstaller", "Файлы и система", "Удаление нескольких программ и поиск их остатков."),
            P("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "Файлы и система", "Состояние SMART и температура дисков."),
            P("REALiX.HWiNFO", "HWiNFO", "Файлы и система", "Подробные сведения об оборудовании и датчиках."),
            P("CPUID.CPU-Z", "CPU-Z", "Файлы и система", "Сведения о процессоре, памяти и материнской плате."),
            P("ShareX.ShareX", "ShareX", "Файлы и система", "Снимки и запись экрана."),
            P("VideoLAN.VLC", "VLC", "Медиа", "Видеоплеер, открывающий большинство форматов без кодеков."),
            P("OBSProject.OBSStudio", "OBS Studio", "Медиа", "Запись экрана и трансляции."),
            P("Audacity.Audacity", "Audacity", "Медиа", "Запись и редактирование звука."),
            P("HandBrake.HandBrake", "HandBrake", "Медиа", "Перекодирование видео."),
            P("IrfanSkiljan.IrfanView", "IrfanView", "Медиа", "Быстрый просмотр изображений."),
            P("Telegram.TelegramDesktop", "Telegram", "Общение", "Мессенджер Telegram для компьютера."),
            P("Discord.Discord", "Discord", "Общение", "Голосовой и текстовый чат."),
            P("Zoom.Zoom", "Zoom", "Общение", "Видеоконференции."),
            P("TheDocumentFoundation.LibreOffice", "LibreOffice", "Документы и безопасность", "Офисный пакет: тексты, таблицы, презентации."),
            P("SumatraPDF.SumatraPDF", "SumatraPDF", "Документы и безопасность", "Лёгкий просмотрщик PDF, EPUB и DjVu."),
            P("KeePassXCTeam.KeePassXC", "KeePassXC", "Документы и безопасность", "Менеджер паролей с локальной базой."),
            P("Bitwarden.Bitwarden", "Bitwarden", "Документы и безопасность", "Менеджер паролей с синхронизацией."),
            P("Valve.Steam", "Steam", "Игры", "Магазин и библиотека игр Valve."),
            P("EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Игры", "Магазин и библиотека игр Epic Games."),
            P("Microsoft.VisualStudioCode", "Visual Studio Code", "Разработка", "Редактор кода Microsoft."),
            P("Git.Git", "Git", "Разработка", "Система контроля версий.")
        };
        private static PackageInfo P(string id, string name, string group, string description)
        {
            return new PackageInfo
            {
                Id = id,
                Name = name,
                Group = group,
                Description = description
            };
        }

        internal static bool ValidId(string id)
        {
            return id != null && Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9.+_-]{1,99}$");
        }

        internal static PackageInfo Find(string id)
        {
            return Catalog.FirstOrDefault(p => p.Id == id);
        }

        internal const string Agreements = "--accept-package-agreements --accept-source-agreements --disable-interactivity";
        internal static string Arguments(string action, string id)
        {
            if (action == "upgrade-all")
                return "upgrade --all --silent " + Agreements;
            if (!ValidId(id) || Find(id) == null)
                throw new ArgumentException("Неизвестная программа.");
            if (action == "install")
                return "install --id " + id + " --exact --source winget --silent " + Agreements;
            if (action == "upgrade")
                return "upgrade --id " + id + " --exact --source winget --silent " + Agreements;
            if (action == "uninstall")
                return "uninstall --id " + id + " --exact --source winget --silent --accept-source-agreements --disable-interactivity";
            throw new ArgumentException("Неизвестное действие winget.");
        }

        // winget result codes that mean the requested state is already reached.
        internal static string Describe(long code, string action)
        {
            switch (unchecked((uint)code))
            {
                case 0:
                    return action == "install" ? "Установлено" : action == "upgrade" ? "Обновлено" : action == "uninstall" ? "Удалено" : "Готово";
                case 0x8A150061:
                    return "Уже установлено";
                case 0x8A15002B:
                    return "Обновление не требуется";
                case 0x8A150109:
                case 0x8A15010A:
                    return "Готово, нужна перезагрузка";
                case 0x8A150014:
                    return action == "uninstall" ? "Уже удалено" : "Не установлено: обновлять нечего";
                default:
                    return "Ошибка winget 0x" + unchecked((uint)code).ToString("X8");
            }
        }

        internal static bool Succeeded(long code)
        {
            var value = unchecked((uint)code);
            return value == 0 || value == 0x8A150061 || value == 0x8A15002B || value == 0x8A150109 || value == 0x8A15010A;
        }

        internal static bool Succeeded(long code, string action)
        {
            return Succeeded(code) || (action == "uninstall" && unchecked((uint)code) == 0x8A150014);
        }

        internal static string Locate()
        {
            var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(alias))
                return alias;
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    if (folder.Trim().Length == 0 || !Path.IsPathRooted(folder.Trim()))
                        continue;
                    var candidate = Path.Combine(folder.Trim(), "winget.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                }
            }

            return null;
        }

        // Progress bars and spinners are redrawn with carriage returns; only meaningful text lines are reported.
        internal static bool Meaningful(string line)
        {
            var text = (line ?? "").Trim();
            return text.Length > 0 && !Regex.IsMatch(text, @"^[-\\|/]$") && text.IndexOf('█') < 0 && text.IndexOf('▒') < 0;
        }

        internal static async Task<long> Run(string arguments, Action<string> output)
        {
            var path = Locate();
            if (path == null)
                throw new FileNotFoundException("winget не найден. Установите «Установщик приложений» из Microsoft Store.");
            var info = new ProcessStartInfo(path, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = new Process
            {
                StartInfo = info,
                EnableRaisingEvents = true
            }

            )
            {
                var done = new TaskCompletionSource<bool>();
                process.Exited += (s, e) => done.TrySetResult(true);
                DataReceivedEventHandler forward = (s, e) =>
                {
                    if (e.Data == null)
                        return;
                    foreach (var part in e.Data.Split('\r'))
                        if (Meaningful(part))
                            output(part.Trim());
                };
                process.OutputDataReceived += forward;
                process.ErrorDataReceived += forward;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await done.Task;
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        internal static string Version()
        {
            var path = Locate();
            if (path == null)
                return null;
            var info = new ProcessStartInfo(path, "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(info))
            {
                string text = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return null;
                }

                return process.ExitCode == 0 ? text.Trim() : null;
            }
        }

        // "winget export" lists installed packages that match the winget source as JSON, which is stable unlike the text tables.
        internal static async Task<HashSet<string>> Installed()
        {
            string runtime = Path.Combine(Program.Data, "runtime");
            Program.SafeDirectory(runtime);
            Directory.CreateDirectory(runtime);
            string file = Path.Combine(runtime, Guid.NewGuid().ToString("N") + ".packages.json");
            try
            {
                long code = await Run("export --output \"" + file + "\" --source winget --accept-source-agreements --disable-interactivity", line =>
                {
                });
                if (!File.Exists(file))
                    throw new IOException("winget не создал список установленных программ (код 0x" + unchecked((uint)code).ToString("X8") + ").");
                if (new FileInfo(file).Length > 8388608)
                    throw new IOException("Список установленных программ слишком велик.");
                return ParseExport(File.ReadAllText(file));
            }
            finally
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch (IOException)
                {
                }
            }
        }

        internal static HashSet<string> ParseExport(string json)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = new JavaScriptSerializer
            {
                MaxJsonLength = 8388608
            }.Deserialize<Dictionary<string, object>>(json);
            object sources;
            if (root == null || !root.TryGetValue("Sources", out sources) || !(sources is System.Collections.ArrayList))
                throw new IOException("Некорректный список winget.");
            foreach (var source in ((System.Collections.ArrayList)sources).OfType<Dictionary<string, object>>())
            {
                object packages;
                if (!source.TryGetValue("Packages", out packages) || !(packages is System.Collections.ArrayList))
                    continue;
                foreach (var package in ((System.Collections.ArrayList)packages).OfType<Dictionary<string, object>>())
                {
                    object id;
                    if (package.TryGetValue("PackageIdentifier", out id) && id is string && ValidId((string)id))
                        result.Add((string)id);
                }
            }

            return result;
        }

        private static readonly RecordStore Store = new RecordStore("package-history", "winget", 65536);
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
                throw new IOException("Некорректный номер записи winget.");
            return Program.Under(DirectoryPath, id + ".json");
        }

        internal static void Save(PackageChange record)
        {
            Store.Save(RecordPath(record.Id), record);
        }

        internal static PackageChange Read(string id)
        {
            try
            {
                var record = Store.Load<PackageChange>(RecordPath(id));
                DateTime time;
                if (record == null || record.Schema != "wintools/package-change/1" || record.Id != id || !new[]
                {
                    "install",
                    "upgrade",
                    "upgrade-all",
                    "uninstall"
                }.Contains(record.Action) || (record.Action != "upgrade-all" && (!ValidId(record.Package) || Find(record.Package) == null)) || string.IsNullOrEmpty(record.Name) || !new[]
                {
                    "PENDING",
                    "OK",
                    "FAILED",
                    "REVERTED"
                }.Contains(record.Status) || !DateTime.TryParseExact(record.TimeUtc, "o", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out time))
                    throw new IOException("Некорректная запись winget.");
                return record;
            }
            catch (ArgumentException ex)
            {
                throw new IOException("Повреждена история winget.", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new IOException("Повреждена история winget.", ex);
            }
        }

        internal static PackageChange[] History()
        {
            return Store.All(Read, r => r.TimeUtc);
        }
    }
}
