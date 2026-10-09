using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace Wintools
{
    // Store and packaged desktop apps declare a StartupTask in their manifest; Windows keeps the user's choice
    // in the per-user AppModel state: 0 disabled, 1 disabled by the user, 2 enabled, 3 and 4 set by policy.
    internal static class StoreStartup
    {
        internal const string Source = "store-task";
        private const string Packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        private const string NamePattern = "^[A-Za-z0-9.-]{3,50}_[a-z0-9]{13}![A-Za-z0-9._-]{1,128}$";
        private const string States = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\";

        // An entry is named "<package family>!<task id>", for example Microsoft.Teams_8wekyb3d8bbwe!TeamsStartupTask.
        internal static void Validate(string name)
        {
            if (!Regex.IsMatch(name ?? "", NamePattern))
                throw new ArgumentException(Lang.T("Некорректная запись автозапуска приложения."));
        }

        // Name_Version_Architecture_ResourceId_PublisherId → Name_PublisherId.
        internal static string Family(string fullName)
        {
            var parts = (fullName ?? "").Split('_');
            return parts.Length == 5 && parts[0].Length > 0 && parts[4].Length == 13 ? parts[0] + "_" + parts[4] : null;
        }

        internal static StartupEntry[] Parse(string fullName, string root, string xml, Func<string, string, int?> state)
        {
            var family = Family(fullName);
            if (family == null || xml == null || xml.Length > 4194304)
                return new StartupEntry[0];
            var doc = new XmlDocument
            {
                XmlResolver = null
            };
            using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4194304 }))
                doc.Load(reader);
            var rows = new List<StartupEntry>();
            foreach (XmlElement task in doc.GetElementsByTagName("*").OfType<XmlElement>().Where(e => e.LocalName == "StartupTask"))
            {
                var id = task.GetAttribute("TaskId");
                var name = family + "!" + id;
                if (!Regex.IsMatch(name, NamePattern) || rows.Any(r => r.Name == name))
                    continue;
                var extension = task.ParentNode as XmlElement;
                var application = extension == null ? null : extension.ParentNode == null ? null : extension.ParentNode.ParentNode as XmlElement;
                var executable = extension != null && extension.HasAttribute("Executable") ? extension.GetAttribute("Executable") : application != null ? application.GetAttribute("Executable") : "";
                var current = state(family, id);
                bool enabled = current.HasValue ? current == 2 || current == 4 : string.Equals(task.GetAttribute("Enabled"), "true", StringComparison.OrdinalIgnoreCase);
                var display = Resource(fullName, task.GetAttribute("DisplayName"));
                rows.Add(new StartupEntry
                {
                    Source = Source,
                    Name = name,
                    Display = display ?? fullName.Split('_')[0],
                    Command = executable.Length == 0 ? fullName.Split('_')[0] : Path.Combine(root ?? "", executable),
                    Approval = StartupTasks.Encode(enabled),
                    Identity = StartupEntries.Hash(Source + "|" + name + "|" + executable),
                    Restriction = current == 3 || current == 4 ? Lang.T("Состояние задаёт политика организации: только просмотр.") : null,
                    Details = Lang.T("Приложение: ") + fullName.Split('_')[0] + Lang.T(" · задача ") + id + (current.HasValue ? "" : Lang.T(" · состояние по умолчанию из описания приложения"))
                });
            }

            return rows.ToArray();
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string source, StringBuilder output, int size, IntPtr reserved);

        // Display names are usually resource references that only the package can resolve.
        private static string Resource(string fullName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (!value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                return value.Trim();
            var key = value.Substring(12);
            var uri = key.StartsWith("//") ? "ms-resource:" + key : "ms-resource://" + fullName.Split('_')[0] + "/" + (key.Contains("/") ? key.TrimStart('/') : "Resources/" + key);
            try
            {
                var output = new StringBuilder(512);
                return SHLoadIndirectString("@{" + fullName + "?" + uri + "}", output, output.Capacity, IntPtr.Zero) == 0 && output.Length > 0 ? output.ToString() : null;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        private static int? ReadState(string family, string id)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(States + family + "\\" + id, false))
            {
                var value = key == null ? null : key.GetValue("State");
                return value is int ? (int?)(int)value : null;
            }
        }

        internal static StartupSnapshot Read(string family = null)
        {
            var rows = new List<StartupEntry>();
            var errors = new List<string>();
            try
            {
                using (var packages = Registry.CurrentUser.OpenSubKey(Packages, false))
                {
                    foreach (var fullName in (packages == null ? new string[0] : packages.GetSubKeyNames()).Where(n => family == null || Family(n) == family).Take(3000))
                        try
                        {
                            string root;
                            using (var package = packages.OpenSubKey(fullName, false))
                                root = package == null ? null : package.GetValue("PackageRootFolder") as string;
                            if (string.IsNullOrEmpty(root))
                                continue;
                            var manifest = Path.Combine(root, "AppxManifest.xml");
                            if (!File.Exists(manifest) || new FileInfo(manifest).Length > 4194304)
                                continue;
                            var xml = File.ReadAllText(manifest);
                            if (xml.IndexOf("StartupTask", StringComparison.Ordinal) < 0)
                                continue;
                            foreach (var row in Parse(fullName, root, xml, ReadState))
                                if (!rows.Any(r => r.Name == row.Name))
                                    rows.Add(row);
                        }
                        catch (Exception ex)
                        {
                            if (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException || ex is System.Security.SecurityException)
                                errors.Add(Lang.T("Приложение ") + fullName.Split('_')[0] + ": " + ex.Message);
                            else
                                throw;
                        }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException))
                    throw;
                errors.Add(Lang.T("Список приложений Store недоступен: ") + ex.Message);
            }

            return new StartupSnapshot
            {
                Entries = rows.ToArray(),
                Errors = errors.Distinct().Take(10).ToArray()
            };
        }

        internal static StartupEntry Inspect(string name)
        {
            Validate(name);
            var entry = Read(name.Substring(0, name.IndexOf('!'))).Entries.FirstOrDefault(e => e.Name == name);
            if (entry == null)
                throw new IOException(Lang.T("Приложение удалено или больше не запускается при входе."));
            return entry;
        }

        internal static void Write(string name, string value, string expected)
        {
            Validate(name);
            if (value == null || !StartupEntries.Decode(value).HasValue)
                throw new IOException(Lang.T("Некорректное состояние автозапуска приложения."));
            var entry = Inspect(name);
            if (entry.Restriction != null)
                throw new IOException(entry.Restriction);
            if (expected == null || entry.Fingerprint != expected)
                throw new IOException(Lang.T("Автозапуск приложения изменился после чтения."));
            int split = name.IndexOf('!');
            using (var key = Registry.CurrentUser.CreateSubKey(States + name.Substring(0, split) + "\\" + name.Substring(split + 1)))
            {
                // "Disabled by the user" is what Settings and Task Manager write; the app itself cannot turn it back on.
                key.SetValue("State", StartupEntries.Decode(value).Value ? 2 : 1, RegistryValueKind.DWord);
                key.Flush();
            }
        }
    }
}
