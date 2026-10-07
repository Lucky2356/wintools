using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private Func<string> windowsBuild = CurrentWindowsBuild;
        private Func<Task> updateVerification;
        // Build and update revision, for example 26100.4061: cumulative updates change the revision, feature updates the build.
        internal static string CurrentWindowsBuild()
        {
            using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", false))
            {
                if (key == null)
                    throw new IOException(Lang.T("Сведения о версии Windows недоступны."));
                string build = Convert.ToString(key.GetValue("CurrentBuild", ""), CultureInfo.InvariantCulture);
                object revision = key.GetValue("UBR");
                if (!Regex.IsMatch(build, "^[0-9]{4,6}$"))
                    throw new IOException(Lang.T("Некорректный номер сборки Windows."));
                return build + "." + (revision is int ? ((int)revision).ToString(CultureInfo.InvariantCulture) : "0");
            }
        }

        private static bool ValidBuild(string value)
        {
            return value != null && Regex.IsMatch(value, "^[0-9]{4,6}\\.[0-9]{1,9}$");
        }

        // After Windows changes its build, compares applied catalogue settings with the system once; the build is remembered only after the check ran.
        private async Task CheckAfterWindowsUpdate()
        {
            string current;
            try
            {
                current = windowsBuild();
            }
            catch (Exception)
            {
                return;
            }

            string previous = ValidBuild(preferences.WindowsBuild) ? preferences.WindowsBuild : null;
            if (previous == current)
                return;
            bool journal = File.Exists(Path.Combine(Program.Data, "state", "applied.dat"));
            if (previous != null && preferences.VerifyAfterUpdates && journal)
            {
                if (!smoke)
                    await Task.Delay(1500);
                if (busy || closed)
                    return;
                await (updateVerification ?? ReadVerification)();
                if (closed)
                    return;
                if (verificationDrift.Length > 0)
                {
                    Text("Status", Lang.T("Windows обновилась (") + previous + " → " + current + Lang.T("). Изменились настройки: ") + verificationDrift.Length + Lang.T(". Их можно снова добавить в план."));
                    ShowPage(7);
                }
                else
                    Text("Status", Lang.T("Windows обновилась (") + previous + " → " + current + Lang.T("). Применённые настройки на месте."));
            }

            preferences.WindowsBuild = current;
            SavePreferences();
        }

        private void AddDriftToPlan()
        {
            if (busy || verificationDrift.Length == 0)
                return;
            var ids = verificationDrift.Where(id => catalogue.Any(t => t.Id == id && t.Category != "CLEAN")).ToArray();
            var previous = preferences.Plan;
            var next = previous.Concat(ids).Distinct().ToList();
            if (next.Count > 200)
            {
                Text("Status", Lang.T("В плане больше 200 действий. Сначала выполните часть плана."));
                return;
            }

            preferences.Plan = next;
            if (!SavePreferences())
            {
                preferences.Plan = previous;
                return;
            }

            RefreshPlan();
            Text("Status", Lang.T("Добавлено в план: ") + (next.Count - previous.Count) + Lang.T(". Выполните предпросмотр, чтобы увидеть, что вернётся."));
            ShowPage(4);
        }
    }
}
