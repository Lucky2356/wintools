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
        private bool readingDns, measuringDns;
        private Button dnsCompare;
        private StackPanel dnsTimings;
        private TextBlock dnsCompareStatus;
        private Func<System.Net.IPEndPoint, Task<DnsTiming>> dnsMeasure = server => DnsBenchmark.Measure(server, DnsBenchmark.Names, 1500);
        private Func<DnsAdapter[]> dnsRead = DnsSettings.Read;
        private Func<string, string, string, string, Task<EngineResult>> dnsRun = DnsActions.Run;
        private void InitializeDns(Panel parent)
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
            var heading = Paragraph(Lang.T("DNS-серверы"));
            heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
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
            // Which server answers fastest from this network; choosing one only fills the list above.
            var compareTitle = Paragraph(Lang.T("Какой DNS быстрее у вас"));
            compareTitle.FontWeight = FontWeights.SemiBold;
            compareTitle.Margin = new Thickness(0, 16, 0, 4);
            panel.Children.Add(compareTitle);
            dnsCompareStatus = Paragraph(Lang.T("Wintools спросит у каждого сервера адреса пяти популярных сайтов и сравнит время ответа. Настройки Windows не меняются."));
            dnsCompareStatus.FontSize = 12;
            panel.Children.Add(dnsCompareStatus);
            dnsTimings = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(dnsTimings);
            dnsCompare = new Button
            {
                Content = Lang.T("Сравнить скорость DNS"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            dnsCompare.Click += async (s, e) => await CompareDns();
            panel.Children.Add(dnsCompare);
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
            dnsCompare.IsEnabled = !measuringDns;
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

        private async Task CompareDns()
        {
            if (measuringDns)
                return;
            measuringDns = true;
            RefreshDnsEnabled();
            dnsTimings.Children.Clear();
            dnsCompareStatus.Text = Lang.T("Сравниваем серверы… Это займёт несколько секунд.");
            try
            {
                var candidates = DnsSettings.Providers.Where(p => p.V4.Length > 0).Select(p => new DnsTiming { Title = p.Name, Provider = p }).ToList();
                var adapter = SelectedDnsAdapter();
                var current = adapter == null ? null : adapter.Effective.FirstOrDefault(a => a.Contains(".") && !candidates.Any(c => c.Provider.V4.Contains(a)));
                if (current != null)
                    candidates.Insert(0, new DnsTiming { Title = Lang.T("Текущий DNS · ") + current });
                var measure = dnsMeasure;
                var results = await Task.WhenAll(candidates.Select(c => Task.Run(() => measure(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(c.Provider == null ? current : c.Provider.V4[0]), 53)))));
                if (closed)
                    return;
                for (int i = 0; i < candidates.Count; i++)
                {
                    candidates[i].MedianMs = results[i].MedianMs;
                    candidates[i].Lost = results[i].Lost;
                    candidates[i].Sent = results[i].Sent;
                }

                DnsBenchmark.Rank(candidates);
                foreach (var timing in candidates.OrderBy(t => t.MedianMs.HasValue && t.Lost < t.Sent ? 0 : 1).ThenBy(t => t.Lost).ThenBy(t => t.MedianMs ?? long.MaxValue))
                    dnsTimings.Children.Add(DnsTimingRow(timing));
                dnsCompareStatus.Text = candidates.All(t => !t.MedianMs.HasValue) ? Lang.T("Ни один сервер не ответил. Возможно, сеть блокирует DNS-запросы к внешним серверам.") : Lang.T("Меньше — лучше. Разница меньше 10 мс на глаз не заметна. Замер отражает текущую сеть и время суток.");
            }
            catch (Exception ex)
            {
                dnsCompareStatus.Text = Lang.T("Не удалось сравнить DNS: ") + ex.Message;
            }
            finally
            {
                measuringDns = false;
                if (!closed)
                    RefreshDnsEnabled();
            }
        }

        private UIElement DnsTimingRow(DnsTiming timing)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = timing.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontWeight = timing.Fastest ? FontWeights.SemiBold : FontWeights.Normal };
            row.Children.Add(title);
            var result = new TextBlock { Text = timing.Result, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            result.SetResourceReference(TextBlock.ForegroundProperty, timing.Fastest ? "Success" : timing.MedianMs.HasValue ? "Muted" : "Danger");
            Grid.SetColumn(result, 1);
            row.Children.Add(result);
            if (timing.Provider != null)
            {
                var choose = new Button { Content = Lang.T("Выбрать"), Padding = new Thickness(10, 3, 10, 3) };
                System.Windows.Automation.AutomationProperties.SetName(choose, Lang.T("Выбрать ") + timing.Title);
                choose.Click += (s, e) =>
                {
                    dnsProvider.SelectedItem = timing.Provider;
                    dnsCompareStatus.Text = Lang.T("Выбран ") + timing.Provider.Name + Lang.T(". Нажмите «Использовать DNS», чтобы применить; прежние адреса сохранятся для возврата.");
                    dnsApply.Focus();
                };
                Grid.SetColumn(choose, 2);
                row.Children.Add(choose);
            }

            return row;
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
