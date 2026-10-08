using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private byte[] hostsContent;
        private HostsChange hostsRestoreRecord;
        private TextBox hostsDomain;
        private ListBox hostsList;
        private TextBlock hostsStatus;
        private Button hostsBlock, hostsUnblock, hostsRefresh, hostsRestore;
        private bool readingHosts;
        private Func<byte[]> hostsRead = HostsFile.ReadBytes;
        private Func<string, string, string, string, Task<EngineResult>> hostsRun = HostsFile.Run;
        private void InitializeHosts(StackPanel parent)
        {
            var panel = new StackPanel();
            var card = new Border
            {
                Child = panel,
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Card(card);
            parent.Children.Add(card);
            var heading = Paragraph(Lang.T("Блокировка сайтов через hosts"));
            heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
            panel.Children.Add(heading);
            panel.Children.Add(Intro(Lang.T("Блокирует сайт на этом ПК через файл hosts."), Lang.T("Запись в файле hosts направляет сайт на несуществующий адрес 0.0.0.0, и программы на этом ПК не смогут к нему подключиться. Подходит для отдельных рекламных или отвлекающих доменов; поддомены блокируются отдельно. Microsoft Defender может пометить блокировку популярных сайтов как подозрительное изменение.")));
            var controls = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(controls);
            hostsDomain = new TextBox
            {
                Width = 280,
                Margin = new Thickness(0, 0, 10, 8)
            };
            System.Windows.Automation.AutomationProperties.SetName(hostsDomain, Lang.T("Домен для блокировки"));
            hostsDomain.TextChanged += (s, e) => RefreshHostsEnabled();
            controls.Children.Add(hostsDomain);
            hostsBlock = new Button
            {
                Content = Lang.T("Заблокировать"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            hostsBlock.Style = (Style)Window.FindResource("Primary");
            hostsBlock.Click += async (s, e) => await ChangeHosts("block");
            controls.Children.Add(hostsBlock);
            hostsUnblock = new Button
            {
                Content = Lang.T("Снять выбранную блокировку"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            hostsUnblock.Click += async (s, e) => await ChangeHosts("unblock");
            controls.Children.Add(hostsUnblock);
            hostsRefresh = new Button
            {
                Content = Lang.T("Обновить"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            hostsRefresh.Click += async (s, e) => await RefreshHosts();
            controls.Children.Add(hostsRefresh);
            hostsRestore = new Button
            {
                Content = Lang.T("Вернуть прежний hosts"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            hostsRestore.Click += async (s, e) =>
            {
                if (hostsRestoreRecord != null)
                    await RestoreHostsHistory(hostsRestoreRecord.Id);
            };
            controls.Children.Add(hostsRestore);
            hostsList = new ListBox
            {
                DisplayMemberPath = "Label",
                MaxHeight = 220,
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Automation.AutomationProperties.SetName(hostsList, Lang.T("Записи файла hosts"));
            hostsList.SelectionChanged += (s, e) => RefreshHostsEnabled();
            panel.Children.Add(hostsList);
            hostsStatus = Paragraph(Lang.T("Нажмите «Обновить», чтобы прочитать файл hosts."));
            hostsStatus.FontSize = 12;
            panel.Children.Add(hostsStatus);
            Get<ScrollViewer>("NetworkPage").IsVisibleChanged += async (s, e) =>
            {
                if (!smoke && Get<ScrollViewer>("NetworkPage").IsVisible)
                    await RefreshHosts();
            };
            RefreshHostsEnabled();
        }

        private string HostsDomainInput()
        {
            try
            {
                return HostsFile.NormalizeDomain(hostsDomain.Text);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void RefreshHostsEnabled()
        {
            if (hostsBlock == null)
                return;
            bool idle = !busy && !readingHosts && hostsContent != null;
            var entry = hostsList.SelectedItem as HostsEntry;
            hostsBlock.IsEnabled = idle && HostsDomainInput() != null;
            hostsUnblock.IsEnabled = idle && entry != null && entry.Managed;
            hostsRefresh.IsEnabled = !busy && !readingHosts;
            hostsRestore.IsEnabled = idle && hostsRestoreRecord != null;
            hostsDomain.IsEnabled = !busy;
        }

        private async Task RefreshHosts()
        {
            if (busy || readingHosts || hostsList == null)
                return;
            readingHosts = true;
            RefreshHostsEnabled();
            try
            {
                var read = hostsRead;
                var content = await Task.Run(() => read());
                if (closed)
                    return;
                hostsContent = content;
                var entries = HostsFile.Parse(content);
                hostsList.ItemsSource = entries.OrderByDescending(e => e.Managed).ThenBy(e => e.Host, StringComparer.OrdinalIgnoreCase).ToArray();
                hostsStatus.Text = Lang.T("Записей в hosts: ") + entries.Length + Lang.T(", из них добавлено Wintools: ") + entries.Count(e => e.Managed) + Lang.T(". Снять можно только блокировки Wintools; прочие записи показаны для сведения.");
                hostsRestoreRecord = null;
                try
                {
                    var current = HostsFile.Hash(content);
                    hostsRestoreRecord = HostsFile.History().FirstOrDefault(r => r.Action != "restore" && r.Status != "REVERTED" && r.AfterHash == current);
                }
                catch (Exception ex)
                {
                    hostsStatus.Text += Lang.T(" История hosts недоступна: ") + ex.Message;
                }
            }
            catch (Exception ex)
            {
                hostsContent = null;
                hostsRestoreRecord = null;
                hostsList.ItemsSource = null;
                hostsStatus.Text = Lang.T("Не удалось прочитать hosts: ") + ex.Message;
            }
            finally
            {
                readingHosts = false;
                if (!closed)
                    RefreshHostsEnabled();
            }
        }

        private async Task ChangeHosts(string action)
        {
            var content = hostsContent;
            if (busy || readingHosts || content == null)
                return;
            string domain;
            if (action == "block")
            {
                domain = HostsDomainInput();
                if (domain == null)
                    return;
                if (HostsFile.Parse(content).Any(e => e.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)))
                {
                    hostsStatus.Text = Lang.T("Для ") + domain + Lang.T(" в hosts уже есть запись.");
                    return;
                }
            }
            else
            {
                var entry = hostsList.SelectedItem as HostsEntry;
                if (entry == null || !entry.Managed)
                    return;
                domain = entry.Host;
            }

            if (!await Confirm(action == "block" ? Lang.T("Заблокировать ") + domain + Lang.T(" на этом ПК?\n\nПрограммы и браузеры не смогут подключиться к этому домену. Поддомены не блокируются. Прежний файл hosts сохраним для возврата.") : Lang.T("Снять блокировку ") + domain + Lang.T("?\n\nУдалим только строку, добавленную Wintools. Прежний файл hosts сохраним для возврата.")))
                return;
            await RunHostsChange(action, domain, HostsFile.Hash(content), null);
            if (action == "block")
                hostsDomain.Clear();
        }

        private async Task RunHostsChange(string action, string domain, string expected, string restore)
        {
            if (busy)
                return;
            SetBusy(true);
            hostsStatus.Text = Lang.T("Изменяем hosts…");
            try
            {
                var result = await hostsRun(action, domain, expected, restore);
                Get<TextBox>("Output").Text = result.Output;
                Text("Status", result.Code == 0 ? Lang.T("Файл hosts изменён.") : Lang.T("Изменение hosts требует внимания. Подробности — в выводе."));
                if (result.Code != 0)
                    ExpandOutput(true);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("hosts не изменён: ") + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }

            ReadHistory();
            await RefreshHosts();
        }

        private async Task RestoreHostsHistory(string id)
        {
            if (busy)
                return;
            try
            {
                var record = HostsFile.Read(id);
                if (record.Action == "restore" || record.Status == "REVERTED")
                    throw new InvalidOperationException(Lang.T("Это изменение hosts уже отменено или не поддерживает повторный откат."));
                var read = hostsRead;
                var content = await Task.Run(() => read());
                if (record.AfterHash != HostsFile.Hash(content))
                    throw new InvalidOperationException(Lang.T("Файл hosts изменён после этой записи. Возврат отменён, чтобы не потерять более поздние изменения."));
                if (!await Confirm(Lang.T("Вернуть файл hosts к состоянию до изменения «") + record.Domain + Lang.T("»?\n\nФайл будет полностью заменён сохранённой копией.")))
                    return;
                await RunHostsChange("restore", "-", HostsFile.Hash(content), id);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Возврат hosts невозможен: ") + ex.Message);
            }
        }

        private static HistoryRow[] HostsHistoryRows()
        {
            return HostsFile.History().Select(r => new HistoryRow { Run = r.Id, HostsChange = true, TimeUtc = DateTime.Parse(r.TimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Title = (r.Action == "block" ? Lang.T("Блокировка в hosts: ") : r.Action == "unblock" ? Lang.T("Снятие блокировки hosts: ") : Lang.T("Возврат hosts: ")) + r.Domain, Status = r.Status == "OK" ? Lang.T("Применено") : r.Status == "REVERTED" ? Lang.T("Откат выполнен") : Lang.T("Требует внимания"), CanRevert = r.Action != "restore" && r.Status != "REVERTED" && r.AfterHash != null }).ToArray();
        }
    }
}
