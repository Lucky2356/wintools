using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Win32;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        // A self-contained page an administrator can keep or send: no scripts, no external files, no user or computer names.
        internal static string VerificationHtml(string[][] rows, string windows, DateTime time)
        {
            Func<string, string> e = WebUtility.HtmlEncode;
            var html = new StringBuilder();
            html.Append("<!doctype html><html lang=\"").Append(Lang.English ? "en" : "ru").Append("\"><head><meta charset=\"utf-8\"><title>").Append(e(Lang.T("Сверка настроек Wintools"))).Append("</title>");
            html.Append("<style>body{font:15px/1.5 'Segoe UI',sans-serif;margin:32px;color:#1a1a1a;background:#fff}h1{font-size:24px;margin:0 0 4px}p{color:#5c5c5c;margin:0 0 20px}table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:8px 12px;border-bottom:1px solid #e0e0e0;vertical-align:top}th{font-weight:600;background:#f3f3f3}.MATCH{color:#0e6e0e}.DRIFT{color:#b42318;font-weight:600}.other{color:#8a5300}code{font-size:13px}</style></head><body>");
            html.Append("<h1>").Append(e(Lang.T("Сверка настроек Wintools"))).Append("</h1><p>");
            html.Append(e(Lang.T("Версия ") + Program.Version + " · " + windows + " · " + time.ToString("yyyy-MM-dd HH:mm")));
            int matched = rows.Count(r => r[0] == "MATCH"), changed = rows.Count(r => r[0] == "DRIFT");
            html.Append("<br>").Append(e(Lang.T("Совпадает: ") + matched + Lang.T(" · Изменилось: ") + changed + Lang.T(" · Не проверено: ") + (rows.Length - matched - changed))).Append("</p>");
            html.Append("<table><thead><tr><th>").Append(e(Lang.T("Результат"))).Append("</th><th>").Append(e(Lang.T("Действие"))).Append("</th><th>").Append(e(Lang.T("Подробности"))).Append("</th></tr></thead><tbody>");
            foreach (var row in rows.OrderBy(r => r[0] == "DRIFT" ? 0 : r[0] == "MATCH" ? 2 : 1))
            {
                html.Append("<tr><td class=\"").Append(row[0] == "MATCH" || row[0] == "DRIFT" ? row[0] : "other").Append("\">").Append(e(row[1])).Append("</td><td>").Append(e(row[2])).Append("<br><code>").Append(e(row[3])).Append("</code></td><td>").Append(e(row.Length > 4 ? row[4] : "")).Append("</td></tr>");
            }

            html.Append("</tbody></table></body></html>");
            return html.ToString();
        }

        // "Windows 11 24H2 (build 26100.4061)": the registry ProductName still says Windows 10 on Windows 11.
        internal static string WindowsDescription()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"))
                {
                    int build;
                    int.TryParse(key == null ? null : key.GetValue("CurrentBuild") as string, out build);
                    var display = key == null ? null : key.GetValue("DisplayVersion") as string;
                    var revision = key == null ? null : key.GetValue("UBR");
                    return "Windows " + (build >= 22000 ? "11" : "10") + (string.IsNullOrEmpty(display) ? "" : " " + display) + " (" + Lang.T("сборка ") + build + (revision == null ? "" : "." + revision) + ")";
                }
            }
            catch (Exception)
            {
                return "Windows";
            }
        }

        private void SaveVerificationReport()
        {
            if (verificationRows.Length == 0)
                return;
            var dialog = new SaveFileDialog
            {
                Title = Lang.T("Сохранить отчёт сверки"),
                Filter = Lang.T("Веб-страница (*.html)|*.html"),
                FileName = "wintools-verification-" + DateTime.Now.ToString("yyyyMMdd") + ".html",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(Window) != true)
                return;
            try
            {
                File.WriteAllText(dialog.FileName, VerificationHtml(verificationRows, WindowsDescription(), DateTime.Now), new UTF8Encoding(false));
                Text("Status", Lang.T("Отчёт сверки сохранён: ") + Path.GetFileName(dialog.FileName));
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Не удалось сохранить отчёт: ") + ex.Message);
            }
        }
    }
}
