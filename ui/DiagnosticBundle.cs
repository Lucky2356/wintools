using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Wintools
{
    // Collects what is needed for a bug report: versions, Wintools errors and recent operation logs. User name and profile path are masked.
    internal static class DiagnosticBundle
    {
        internal static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), user = Environment.UserName, machine = Environment.MachineName;
            if (profile.Length > 3)
                text = text.Replace(profile, "%USERPROFILE%");
            if (user.Length > 1)
                text = Regex.Replace(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(user) + @"(?![\p{L}\p{N}])", "<user>", RegexOptions.IgnoreCase);
            if (machine.Length > 1)
                text = Regex.Replace(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(machine) + @"(?![\p{L}\p{N}])", "<computer>", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"S-1-5-21-[0-9-]+", "<sid>");
        }

        internal static string Summary()
        {
            var text = new StringBuilder();
            text.AppendLine("Wintools " + Program.Version);
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", false))
                    if (key != null)
                        text.AppendLine("Windows: " + key.GetValue("ProductName") + " " + key.GetValue("DisplayVersion") + " " + key.GetValue("CurrentBuild") + "." + key.GetValue("UBR"));
            }
            catch (Exception)
            {
            }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", false))
                    if (key != null)
                        text.AppendLine(".NET Framework release: " + key.GetValue("Release"));
            }
            catch (Exception)
            {
            }

            text.AppendLine("64-bit OS: " + Environment.Is64BitOperatingSystem + "; culture: " + CultureInfo.CurrentUICulture.Name + "; created: " + DateTime.UtcNow.ToString("o"));
            return text.ToString();
        }

        internal static string Create()
        {
            string reports = Path.Combine(Program.Data, "reports");
            Program.SafeDirectory(reports);
            Directory.CreateDirectory(reports);
            string path = Path.Combine(reports, "diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip");
            var files = new List<KeyValuePair<string, string>>();
            foreach (var name in new[]
            {
                "ui-error.txt",
                "update-error.txt"
            }

            )
            {
                var file = Path.Combine(Program.Data, name);
                if (File.Exists(file))
                    files.Add(new KeyValuePair<string, string>(name, file));
            }

            foreach (var folder in new[]
            {
                "logs",
                "runtime"
            }

            )
            {
                var directory = Path.Combine(Program.Data, folder);
                if (!Directory.Exists(directory))
                    continue;
                Program.SafeDirectory(directory);
                foreach (var file in new DirectoryInfo(directory).GetFiles("*.log").Where(f => (f.Attributes & FileAttributes.ReparsePoint) == 0).OrderByDescending(f => f.LastWriteTimeUtc).Take(15))
                    files.Add(new KeyValuePair<string, string>(folder + "/" + file.Name, file.FullName));
            }

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(archive, "summary.txt", Summary());
                foreach (var file in files)
                {
                    string content;
                    try
                    {
                        var info = new FileInfo(file.Value);
                        if (info.Length > 2097152)
                            continue;
                        content = File.ReadAllText(file.Value);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    Add(archive, file.Key, Mask(content));
                }
            }

            return path;
        }

        private static void Add(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }
    }
}

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private Func<string> diagnosticCreate = DiagnosticBundle.Create;
        private async Task CreateDiagnosticBundle()
        {
            if (busy)
                return;
            Enabled("DiagnosticBundle", false);
            try
            {
                var create = diagnosticCreate;
                var path = await Task.Run(() => create());
                Text("Status", Lang.T("Пакет диагностики сохранён: ") + Path.GetFileName(path) + Lang.T(". Перед отправкой можно открыть архив и проверить содержимое."));
                if (!smoke)
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select," + Program.Quote(path)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Пакет диагностики не создан: ") + ex.Message);
            }
            finally
            {
                Enabled("DiagnosticBundle", true);
            }
        }
    }
}
