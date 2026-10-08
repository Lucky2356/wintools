using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private void ReadHistory()
        {
            var rows = new List<HistoryRow>();
            var failed = new List<string>();
            var errors = new List<string>();
            ReadHistorySource(Lang.T("Каталог"), () => HistoryRows(Path.Combine(Program.Data, "state", "applied.dat"), catalogue), rows, failed, errors);
            ReadHistorySource(Lang.T("Службы"), ServiceHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Питание"), PowerHistoryRows, rows, failed, errors);
            ReadHistorySource("DNS", DnsHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Файл hosts"), HostsHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Обновления Windows"), UpdateHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Установка программ"), PackageHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Обслуживание"), IntegrityHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Автозагрузка"), StartupHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Процессы"), ProcessHistoryRows, rows, failed, errors);
            ReadHistorySource(Lang.T("Приложения Store"), StoreHistoryRows, rows, failed, errors);
            Get<ListBox>("History").ItemsSource = rows.OrderByDescending(r => r.TimeUtc).ToArray();
            Visible("EmptyHistory", rows.Count == 0 && failed.Count == 0);
            Visible("History", !(rows.Count == 0 && failed.Count == 0));
            Text("HistoryStatus", failed.Count > 0 ? Lang.T("Журнал частично недоступен: ") + string.Join(", ", failed) + Lang.T(". Доступно записей: ") + rows.Count + Lang.T(". Записи недоступных разделов скрыты. Повторите чтение позже; подробности — в подсказке.") : rows.Count == 0 ? Lang.T("Изменений пока нет. После выполнения действия здесь появится запись.") : Lang.T("Запусков: ") + rows.Count + Lang.T(". Сначала откатывайте самые новые изменения."));
            Get<TextBlock>("HistoryStatus").ToolTip = errors.Count == 0 ? null : string.Join("\n\n", errors);
            RefreshEnabled();
        }

        private static void ReadHistorySource(string name, Func<HistoryRow[]> read, List<HistoryRow> rows, List<string> failed, List<string> errors)
        {
            try
            {
                var loaded = read();
                rows.AddRange(loaded);
            }
            catch (IOException ex)
            {
                failed.Add(name);
                errors.Add(name + ": " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                failed.Add(name);
                errors.Add(name + Lang.T(": нет доступа. ") + ex.Message);
            }
        }
    }
}
