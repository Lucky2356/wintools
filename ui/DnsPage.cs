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
        private DnsAdapter[] dnsAdapters;
        private DnsChange dnsRestoreRecord;
        private ComboBox dnsAdapter, dnsProvider;
        private TextBlock dnsCurrent, dnsDescription, dnsStatus;
        private Button dnsApply, dnsRefresh, dnsRestore;
        private bool readingDns;
        private Func<DnsAdapter[]> dnsRead = DnsSettings.Read;
        private Func<string, string, string, string, Task<EngineResult>> dnsRun = DnsActions.Run;
        private void InitializeDns(StackPanel parent)
        {
            var panel = new StackPanel();
            var card = new Border
            {
                Child = panel,
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Card(card);
            parent.Children.Add(card);
            var heading = Paragraph(Lang.T("DNS-серверы"));
            heading.FontSize = 21;
            heading.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(heading);
            panel.Children.Add(Intro(Lang.T("DNS влияет на открытие сайтов и может блокировать опасные домены."), Lang.T("DNS превращает имена сайтов в адреса. Другой DNS может ускорить открытие сайтов, блокировать вредоносные или рекламные домены, но не увеличивает скорость скачивания. Меняются адреса IPv4 и IPv6 только выбранного адаптера.")));
            dnsAdapter = new ComboBox
            {
                DisplayMemberPath = "Label",
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Automation.AutomationProperties.SetName(dnsAdapter, Lang.T("Сетевой адаптер для DNS"));
            panel.Children.Add(dnsAdapter);
            dnsCurrent = Paragraph(Lang.T("Нажмите «Обновить DNS», чтобы прочитать текущие настройки."));
            panel.Children.Add(dnsCurrent);
            dnsProvider = new ComboBox
            {
                DisplayMemberPath = "Name",
                ItemsSource = DnsSettings.Providers,
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Automation.AutomationProperties.SetName(dnsProvider, Lang.T("DNS-серверы"));
            panel.Children.Add(dnsProvider);
            dnsDescription = Paragraph("");
            panel.Children.Add(dnsDescription);
            dnsAdapter.SelectionChanged += (s, e) =>
            {
                RefreshDnsDetails();
                RefreshDnsEnabled();
            };
            dnsProvider.SelectionChanged += (s, e) =>
            {
                RefreshDnsDetails();
                RefreshDnsEnabled();
            };
            var buttons = new WrapPanel();
            panel.Children.Add(buttons);
            dnsApply = new Button
            {
                Content = Lang.T("Использовать DNS"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            dnsApply.Style = (Style)Window.FindResource("Primary");
            dnsApply.Click += async (s, e) => await SelectDns();
            buttons.Children.Add(dnsApply);
            dnsRefresh = new Button
            {
                Content = Lang.T("Обновить DNS"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            dnsRefresh.Click += async (s, e) => await RefreshDns();
            buttons.Children.Add(dnsRefresh);
            dnsRestore = new Button
            {
                Content = Lang.T("Вернуть прежний DNS"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            dnsRestore.Click += async (s, e) =>
            {
                if (dnsRestoreRecord != null)
                    await RestoreDnsHistory(dnsRestoreRecord.Id);
            };
            buttons.Children.Add(dnsRestore);
            dnsStatus = Paragraph(Lang.T("Прежние адреса сохраняются в истории. Возврат доступен, пока DNS адаптера не изменили позже вручную или другой программой."));
            dnsStatus.FontSize = 12;
            panel.Children.Add(dnsStatus);
            Get<ScrollViewer>("NetworkPage").IsVisibleChanged += async (s, e) =>
            {
                if (!smoke && Get<ScrollViewer>("NetworkPage").IsVisible)
                    await RefreshDns();
            };
            RefreshDnsDetails();
            RefreshDnsEnabled();
        }

        private DnsAdapter SelectedDnsAdapter()
        {
            return dnsAdapter == null ? null : dnsAdapter.SelectedItem as DnsAdapter;
        }

        private static bool DnsMatches(DnsAdapter adapter, DnsProvider provider)
        {
            return adapter != null && provider != null && adapter.Static4.SequenceEqual(provider.V4) && (adapter.Index6 == 0 || adapter.Static6.SequenceEqual(provider.V6.Select(a => System.Net.IPAddress.Parse(a).ToString())));
        }

        private void RefreshDnsDetails()
        {
            if (dnsProvider == null)
                return;
            var provider = dnsProvider.SelectedItem as DnsProvider;
            var adapter = SelectedDnsAdapter();
            dnsDescription.Text = provider == null ? "" : provider.Description + (provider.V4.Length > 0 ? Lang.T(" Адреса: ") + string.Join(", ", provider.V4.Concat(provider.V6)) + "." : "");
            dnsCurrent.Text = adapter == null ? (dnsAdapters == null ? Lang.T("Нажмите «Обновить DNS», чтобы прочитать текущие настройки.") : Lang.T("Нет подключённых сетевых адаптеров.")) : Lang.T("Сейчас: ") + adapter.Summary + ".";
            dnsRestoreRecord = null;
            if (adapter != null)
            {
                try
                {
                    dnsRestoreRecord = DnsActions.History().FirstOrDefault(r => r.Action == "select" && r.Status != "REVERTED" && r.Status != "PENDING" && string.Equals(r.Adapter, adapter.Id, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    dnsStatus.Text = Lang.T("История DNS недоступна: ") + ex.Message;
                }
            }
        }

        private void RefreshDnsEnabled()
        {
            if (dnsAdapter == null)
                return;
            var adapter = SelectedDnsAdapter();
            var provider = dnsProvider.SelectedItem as DnsProvider;
            dnsAdapter.IsEnabled = dnsProvider.IsEnabled = dnsRefresh.IsEnabled = !busy && !readingDns;
            dnsApply.IsEnabled = !busy && !readingDns && adapter != null && provider != null && !DnsMatches(adapter, provider);
            dnsRestore.IsEnabled = !busy && !readingDns && dnsRestoreRecord != null;
        }

        private async Task RefreshDns()
        {
            if (busy || readingDns || dnsAdapter == null)
                return;
            readingDns = true;
            RefreshDnsEnabled();
            dnsStatus.Text = Lang.T("Читаем настройки DNS…");
            try
            {
                var read = dnsRead;
                var adapters = await Task.Run(() => read());
                if (closed)
                    return;
                var previous = SelectedDnsAdapter();
                dnsAdapters = adapters;
                dnsAdapter.ItemsSource = adapters;
                dnsAdapter.SelectedItem = adapters.FirstOrDefault(a => previous != null && a.Id == previous.Id) ?? adapters.FirstOrDefault();
                dnsStatus.Text = adapters.Length == 0 ? Lang.T("Подключённых сетевых адаптеров нет.") : Lang.T("Адаптеров: ") + adapters.Length + Lang.T(". Выберите DNS и нажмите «Использовать DNS». Изменение потребует подтверждения Windows.");
            }
            catch (Exception ex)
            {
                dnsAdapters = null;
                dnsAdapter.ItemsSource = null;
                dnsStatus.Text = Lang.T("Не удалось прочитать DNS: ") + ex.Message;
            }
            finally
            {
                readingDns = false;
                if (!closed)
                {
                    RefreshDnsDetails();
                    RefreshDnsEnabled();
                }
            }
        }

        private async Task SelectDns()
        {
            var adapter = SelectedDnsAdapter();
            var provider = dnsProvider.SelectedItem as DnsProvider;
            if (busy || readingDns || adapter == null || provider == null || DnsMatches(adapter, provider))
                return;
            if (!await Confirm(Lang.T("Использовать «") + provider.Name + Lang.T("» для адаптера «") + adapter.Name + "»?\n\n" + provider.Description + Lang.T("\n\nСейчас: ") + adapter.Summary + Lang.T(".\n\nПрежние адреса сохраним в истории для возврата. Открытые соединения могут ненадолго прерваться.")))
                return;
            await RunDnsChange(adapter.Id, provider.Key, adapter.Fingerprint, null);
            if (provider.V4.Length > 0 && networkHost != null && !probingNetwork)
                networkHost.Text = provider.V4[0];
        }

        private async Task RunDnsChange(string adapter, string target, string expected, string restore)
        {
            if (busy)
                return;
            SetBusy(true);
            dnsStatus.Text = Lang.T("Меняем DNS…");
            try
            {
                var result = await dnsRun(adapter, target, expected, restore);
                Get<TextBox>("Output").Text = result.Output;
                Text("Status", result.Code == 0 ? Lang.T("DNS изменён или уже используется.") : Lang.T("Изменение DNS требует внимания. Подробности — в выводе."));
                if (result.Code != 0)
                    ExpandOutput(true);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("DNS не изменён: ") + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }

            ReadHistory();
            await RefreshDns();
        }

        private async Task RestoreDnsHistory(string id)
        {
            if (busy)
                return;
            try
            {
                var record = DnsActions.Read(id);
                if (record.Action != "select" || record.Status == "REVERTED")
                    throw new InvalidOperationException(Lang.T("Это изменение DNS уже отменено или не поддерживает повторный откат."));
                var read = dnsRead;
                var adapters = await Task.Run(() => read());
                var adapter = adapters.FirstOrDefault(a => string.Equals(a.Id, record.Adapter, StringComparison.OrdinalIgnoreCase));
                if (adapter == null)
                    throw new InvalidOperationException(Lang.T("Адаптер «") + record.AdapterName + Lang.T("» отключён или удалён. Подключите его и повторите возврат."));
                string before = record.Before4.Length + record.Before6.Length == 0 ? Lang.T("автоматически (адреса выдаёт сеть)") : string.Join(", ", record.Before4.Concat(record.Before6));
                if (!await Confirm(Lang.T("Вернуть прежний DNS адаптера «") + record.AdapterName + Lang.T("»?\n\nБудет: ") + before + Lang.T(".\nСейчас: ") + adapter.Summary + "."))
                    return;
                await RunDnsChange(adapter.Id, "restore", adapter.Fingerprint, id);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Возврат DNS невозможен: ") + ex.Message);
            }
        }

        private static HistoryRow[] DnsHistoryRows()
        {
            return DnsActions.History().Select(r => new HistoryRow { Run = r.Id, DnsChange = true, TimeUtc = DateTime.Parse(r.TimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Title = (r.Action == "restore" ? Lang.T("Возврат DNS: ") : "DNS: ") + r.TargetName + " · " + r.AdapterName, Status = r.Status == "OK" ? Lang.T("Применено") : r.Status == "REVERTED" ? Lang.T("Откат выполнен") : Lang.T("Требует внимания"), CanRevert = r.Action == "select" && r.Status != "REVERTED" && r.Status != "PENDING" }).ToArray();
        }
    }
}
