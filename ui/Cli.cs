using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Wintools
{
    // Applies a saved profile without the window, for administrators who prepare several computers:
    //   WintoolsPortable.exe --apply profile.json [--report result.txt] [--dry-run]
    // Exit codes: 0 every action succeeded, 1 an action failed (later ones were not started), 2 the request was invalid.
    internal static class Cli
    {
        internal static bool Requested(string[] args)
        {
            return args.Length >= 1 && args[0] == "--apply";
        }

        internal sealed class Request
        {
            internal string Profile, Report;
            internal bool Dry;
        }

        internal static Request Parse(string[] args)
        {
            if (!Requested(args) || args.Length < 2 || args[1].StartsWith("--"))
                throw new ArgumentException("Usage: --apply <profile.json> [--report <file>] [--dry-run]");
            var request = new Request { Profile = args[1] };
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--dry-run")
                    request.Dry = true;
                else if (args[i] == "--report" && i + 1 < args.Length)
                    request.Report = args[++i];
                else
                    throw new ArgumentException("Unknown argument: " + args[i]);
            }

            return request;
        }

        // The same checks as importing a profile in the window: a known schema, at most 200 known plannable actions.
        internal static Tweak[] ReadProfile(string json, List<Tweak> catalogue)
        {
            var profile = new JavaScriptSerializer().Deserialize<ActionProfile>(json);
            if (profile == null || profile.Schema != "wintools/profile/1" || profile.Actions == null || profile.Actions.Length > 200)
                throw new IOException(Lang.T("Неподдерживаемый формат профиля."));
            var items = profile.Actions.Distinct().Select(id => catalogue.FirstOrDefault(t => t.Id == id)).ToArray();
            if (items.Any(t => !MainWindow.CanPlan(t)))
                throw new IOException(Lang.T("В профиле есть неизвестное или недопустимое действие. Обновите приложение и проверьте файл."));
            return items;
        }

        internal static int Apply(string[] args)
        {
            Request request = null;
            var report = new StringBuilder();
            int code = 2;
            try
            {
                request = Parse(args);
                // An audit report that cannot be written must stop the run before Windows changes.
                if (request.Report != null && !Save(request.Report, Lang.T("Применение профиля начато, отчёт будет дописан по завершении.") + Environment.NewLine))
                    return Refuse(Lang.T("Не удалось записать отчёт: ") + request.Report);
                report.AppendLine("Wintools " + Program.Version + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + (request.Dry ? Lang.T(" · предпросмотр без изменений") : ""));
                report.AppendLine(Lang.T("Профиль: ") + Path.GetFullPath(request.Profile));
                if (new FileInfo(request.Profile).Length > 65536)
                    throw new IOException(Lang.T("Файл профиля слишком большой."));
                var items = ReadProfile(File.ReadAllText(request.Profile), Catalogue.Load());
                var preferences = Preferences.Load();
                report.AppendLine(Lang.T("Действий: ") + items.Length);
                report.AppendLine();
                code = 0;
                foreach (var item in items)
                {
                    var result = Engine.Run(item.Verb, item.Id, "-", request.Dry, preferences.RestorePoint, value =>
                    {
                    }).GetAwaiter().GetResult();
                    report.AppendLine((result.Code == 0 ? "OK     " : "FAILED ") + item.Id + " · " + item.Title + (result.Code == 0 ? "" : " · " + Lang.T("код ") + result.Code));
                    if (result.Code != 0)
                    {
                        report.AppendLine(result.Output);
                        report.AppendLine(Lang.T("Остановлено после ошибки; следующие действия не запускались. Выполненные изменения есть в истории Wintools."));
                        code = 1;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                report.AppendLine(Lang.T("Ошибка: ") + ex.Message);
                code = 2;
            }

            report.AppendLine();
            report.AppendLine(Lang.T("Код завершения: ") + code);
            if (!Save(request == null ? null : request.Report, report.ToString()))
                return Refuse(Lang.T("Не удалось записать отчёт. Код действий: ") + code);
            return code;
        }

        // A request that could not even start (another copy open, engine locked) still leaves its report.
        internal static int Fail(string[] args, string message)
        {
            string path = null;
            try
            {
                path = Parse(args).Report;
            }
            catch (ArgumentException)
            {
            }

            if (!Save(path, Lang.T("Ошибка: ") + message + Environment.NewLine + Lang.T("Код завершения: ") + 2 + Environment.NewLine))
                Console.Error.WriteLine(message);
            return 2;
        }

        // Without a report the error still reaches a script that redirects the standard error stream.
        private static int Refuse(string message)
        {
            Console.Error.WriteLine(message);
            return 2;
        }

        private static bool Save(string path, string text)
        {
            try
            {
                if (path == null)
                {
                    var reports = Path.Combine(Program.Data, "reports");
                    Directory.CreateDirectory(reports);
                    path = Path.Combine(reports, "apply-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                }

                File.WriteAllText(path, text, new UTF8Encoding(true));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }
    }
}
