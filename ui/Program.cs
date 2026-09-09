using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Wintools {
    internal static class Program {
        internal static string Exe { get { return Application.ExecutablePath; } }
        internal static string Home { get { return Path.GetDirectoryName(Exe); } }
        internal static string Data { get { return Path.Combine(Home, "WintoolsData"); } }
        internal static string Version { get { return ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute))).InformationalVersion; } }
        internal static bool Hosted { get { return Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted"; } }

        [STAThread]
        private static int Main(string[] args) {
            try {
                if (args.Length > 0 && args[0] == "--replace") return Updates.Replace(args);
                if (args.Length > 0 && args[0] == "--power-worker") return PowerActions.Worker(args);
                if (args.Length > 0 && args[0] == "--service-worker") return ServiceActions.Worker(args);
                if (args.Length > 0 && args[0] == "--worker") return Engine.Worker(args);
                bool test = args.Length == 1 && args[0] == "--self-test";
                bool smoke = args.Length == 1 && args[0] == "--ui-smoke";
                if (test && !Hosted) throw new InvalidOperationException("System integration tests run only on GitHub-hosted runners.");
                if (smoke && !Hosted && Directory.Exists(Data)) throw new InvalidOperationException("UI smoke requires a fresh portable directory so existing preferences and history cannot be changed.");
                if (args.Length > 0 && !test && !smoke) throw new ArgumentException("Unknown argument.");
                using (var gate = new Mutex(false, MutexName(Home))) {
                    bool acquired;
                    try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new InvalidOperationException("Этот portable-каталог уже открыт в другом экземпляре.");
                    try {
                        ExtractEngine();
                        if (test) return SelfTests.Run();
                        var application=new System.Windows.Application{ShutdownMode=System.Windows.ShutdownMode.OnMainWindowClose};
                        var window=new MainWindow(smoke);
                        application.Run(window.Window);
                    } finally { gate.ReleaseMutex(); }
                }
                return Environment.ExitCode;
            } catch (Exception ex) {
                if (Hosted) { File.WriteAllText(Path.Combine(Home, "portable-error.txt"), ex.ToString()); }
                else MessageBox.Show(ex.Message, "Wintools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 4;
            }
        }

        internal static string MutexName(string root) {
            using (var hash = SHA256.Create()) return "Local\\Wintools_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).Replace("-", "");
        }
        internal static void SafeDirectory(string directory) {
            var path = Path.GetFullPath(directory);
            if (path.IndexOfAny("!%&\"^<>|\r\n".ToCharArray()) >= 0) throw new IOException("Переместите программу в каталог без символов ! % & \" ^ < > |.");
            for (var item = new DirectoryInfo(path); item != null; item = item.Parent) {
                if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Каталог portable-программы не должен проходить через junction или symlink.");
            }
        }
        internal static string Under(string root, string relative) {
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Path outside portable directory.");
            return full;
        }
        internal static void ExtractEngine() {
            SafeDirectory(Data);
            Directory.CreateDirectory(Data);
            var state=Path.Combine(Data,"state");SafeDirectory(state);Directory.CreateDirectory(state);
            var lockPath=Path.Combine(state,"run.lock");
            FileStream engineLock;
            try { engineLock=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None); }
            catch(IOException) { throw new IOException("Движок занят или остался state\\run.lock. Проверьте, завершён ли предыдущий запуск, прежде чем удалять блокировку."); }
            try {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Wintools.Engine.zip"))
            using (var zip = new ZipArchive(resource, ZipArchiveMode.Read)) {
                foreach (var entry in zip.Entries) {
                    if (entry.FullName.EndsWith("/")) continue;
                    var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    bool allowed = name.StartsWith("lib\\", StringComparison.Ordinal) || name.StartsWith("data\\", StringComparison.Ordinal) || name == "docs\\product-review.md" || new[] {"wintweaks.cmd","menu.cmd","VERSION","README.md","CHANGELOG.md","SECURITY.md"}.Contains(name);
                    if (!allowed) throw new IOException("Unexpected embedded file: " + name);
                    var destination = Under(Data, name);
                    SafeDirectory(Path.GetDirectoryName(destination));
                    if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked engine file.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    // Always use the engine bundled with this executable, preserve state/backups.
                    var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try {
                        using (var source = entry.Open()) using (var target = File.Create(temporary)) source.CopyTo(target);
                        if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
                    } finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
            } finally {engineLock.Dispose();File.Delete(lockPath);}
        }
        internal static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    }
}
