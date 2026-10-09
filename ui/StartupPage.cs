using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private StartupSnapshot startupSnapshot;
        private ListBox startupList;
        private TextBox startupSearch;
        private ComboBox startupFilter, startupSource;
        private TextBlock startupStatus, startupDetail;
        private Button startupRefresh, startupEnable, startupDisable, startupImpact;
        private bool readingStartup, startupReadPending;
        private int startupEpoch;
        private Func<StartupSnapshot> startupRead = StartupEntries.Read;
        private Func<StartupEntry, string, string, Task<EngineResult>> startupRun = StartupActions.Run;
        private void InitializeStartup()
        {
            var root = new Grid
            {
                Visibility = Visibility.Collapsed
            };
            Window.RegisterName("StartupPage", root);
            ((Grid)Get<FrameworkElement>("SettingsPage").Parent).Children.Add(root);
            foreach (var height in new[]
            {
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto,
                new GridLength(1, GridUnitType.Star),
                GridLength.Auto,
                GridLength.Auto
            }

            )
                root.RowDefinitions.Add(new RowDefinition { Height = height });
            var intro = Intro(Lang.T("Программы, которые запускаются вместе с Windows. Отключите лишние, чтобы вход был быстрее."), Lang.T("Реестр Run, папки автозагрузки, задачи входа в Windows и автозапуск приложений Store. «Включено» означает разрешение запуска, а не работающий процесс. У задач могут быть дополнительные условия."));
            root.Children.Add(intro);
            var filters = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            filters.ColumnDefinitions.Add(new ColumnDefinition());
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            Grid.SetRow(filters, 1);
            root.Children.Add(filters);
            startupSearch = new TextBox
            {
                Margin = new Thickness(0, 0, 10, 0),
                ToolTip = Lang.T("Поиск по имени, команде и источнику")
            };
            System.Windows.Automation.AutomationProperties.SetName(startupSearch, Lang.T("Поиск в автозагрузке"));
            filters.Children.Add(startupSearch);
            var searchHint = new TextBlock
            {
                Text = Lang.T("Найти программу или команду…"),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 10, 0)
            };
            searchHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            filters.Children.Add(searchHint);
            startupSearch.TextChanged += (s, e) =>
            {
                searchHint.Visibility = startupSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                FilterStartup();
            };
            startupFilter = new ComboBox
            {
                Margin = new Thickness(0, 0, 8, 0),
                ItemsSource = new[]
                {
                    Lang.T("Все записи"),
                    Lang.T("Включённые"),
                    Lang.T("Отключённые"),
                    Lang.T("Неизвестно")
                },
                SelectedIndex = 0
            };
            System.Windows.Automation.AutomationProperties.SetName(startupFilter, Lang.T("Состояние автозагрузки"));
            Grid.SetColumn(startupFilter, 1);
            filters.Children.Add(startupFilter);
            startupFilter.SelectionChanged += (s, e) => FilterStartup();
            startupSource = new ComboBox
            {
                ItemsSource = new[]
                {
                    Lang.T("Все источники"),
                    Lang.T("Реестр Run"),
                    Lang.T("Папки"),
                    Lang.T("Задачи входа"),
                    Lang.T("Приложения Store")
                },
                SelectedIndex = 0
            };
            System.Windows.Automation.AutomationProperties.SetName(startupSource, Lang.T("Источник автозагрузки"));
            Grid.SetColumn(startupSource, 2);
            filters.Children.Add(startupSource);
            startupSource.SelectionChanged += (s, e) => FilterStartup();
            startupStatus = Paragraph(Lang.T("Нажмите «Обновить список», чтобы прочитать автозагрузку."));
            Grid.SetRow(startupStatus, 2);
            root.Children.Add(startupStatus);
            startupList = new ListBox
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(startupList, 3);
            root.Children.Add(startupList);
            VirtualizingPanel.SetIsVirtualizing(startupList, true);
            VirtualizingPanel.SetVirtualizationMode(startupList, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(startupList, true);
            var rowStyle = new Style(typeof(ListBoxItem), (Style)Window.FindResource(typeof(ListBoxItem)));
            rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            rowStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4)));
            startupList.ItemContainerStyle = rowStyle;
            startupList.ItemTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='105'/></Grid.ColumnDefinitions><StackPanel Margin='0,0,12,0'><TextBlock Text='{Binding Title}' ToolTip='{Binding Title}' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Location}' Foreground='{DynamicResource Muted}' FontSize='12' Margin='0,4,0,0'/><TextBlock Text='{Binding Command}' ToolTip='{Binding Command}' Foreground='{DynamicResource Muted}' FontSize='12' TextTrimming='CharacterEllipsis' Margin='0,4,0,0'/><TextBlock x:Name='Impact' Text='{Binding Impact}' Foreground='{DynamicResource Warning}' FontSize='12' FontWeight='SemiBold' Margin='0,4,0,0'/></StackPanel><Border Grid.Column='1' x:Name='Pill' BorderBrush='{DynamicResource Warning}' BorderThickness='1' CornerRadius='9' Padding='8,1' HorizontalAlignment='Center' VerticalAlignment='Center'><TextBlock x:Name='PillText' Text='{Binding State}' FontSize='12' FontWeight='SemiBold' Foreground='{DynamicResource Warning}' TextWrapping='NoWrap'/></Border></Grid><DataTemplate.Triggers><DataTrigger Binding='{Binding Impact}' Value='{x:Null}'><Setter TargetName='Impact' Property='Visibility' Value='Collapsed'/></DataTrigger><DataTrigger Binding='{Binding Enabled}' Value='True'><Setter TargetName='Pill' Property='BorderBrush' Value='{DynamicResource Success}'/><Setter TargetName='PillText' Property='Foreground' Value='{DynamicResource Success}'/></DataTrigger><DataTrigger Binding='{Binding Enabled}' Value='False'><Setter TargetName='Pill' Property='BorderBrush' Value='{DynamicResource Border}'/><Setter TargetName='PillText' Property='Foreground' Value='{DynamicResource Muted}'/></DataTrigger></DataTemplate.Triggers></DataTemplate>");
            startupList.SelectionChanged += (s, e) =>
            {
                var selected = startupList.SelectedItem as StartupEntry;
                startupDetail.Text = selected == null ? Lang.T("Выберите программу. Исходное состояние сохраняется в истории.") : selected.Error ?? (selected.Source == StartupTasks.Source ? (selected.Restriction ?? Lang.T("Можно изменить разрешение запуска всей задачи. Работающий экземпляр не останавливается.")) + "\n" + selected.Details : selected.Source == StoreStartup.Source ? (selected.Restriction ?? Lang.T("Приложение само просит запускаться при входе. Отключение здесь — то же, что в «Параметрах» Windows.")) + "\n" + selected.Details : ("«" + selected.Name + "» · " + selected.Location + Lang.T(". Отключайте только программы, которые не нужны сразу после входа; например, мессенджер перестанет автоматически показывать сообщения.")));
                if (selected != null && selected.Impact != null)
                    startupDetail.Text += "\n" + selected.Impact + Lang.T(" — оценка Windows по журналу загрузки.");
                startupDetail.ToolTip = startupDetail.Text;
                RefreshStartupEnabled();
            };
            startupDetail = Paragraph(Lang.T("Выберите программу. Исходное состояние сохраняется в истории."));
            startupDetail.FontSize = 12;
            startupDetail.Margin = new Thickness(0, 8, 0, 8);
            var detailScroll = new ScrollViewer
            {
                Content = startupDetail,
                MaxHeight = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(detailScroll, 4);
            root.Children.Add(detailScroll);
            var buttons = new WrapPanel();
            Grid.SetRow(buttons, 5);
            root.Children.Add(buttons);
            SidePane(root, 3, detailScroll, buttons);
            startupDisable = new Button
            {
                Content = Lang.T("Отключить при входе"),
                Margin = new Thickness(0, 0, 8, 4)
            };
            startupDisable.Click += async (s, e) => await ChangeStartup(false);
            buttons.Children.Add(startupDisable);
            startupEnable = new Button
            {
                Content = Lang.T("Включить при входе"),
                Margin = new Thickness(0, 0, 8, 4)
            };
            startupEnable.Click += async (s, e) => await ChangeStartup(true);
            buttons.Children.Add(startupEnable);
            startupImpact = new Button
            {
                Content = Lang.T("Оценить влияние на вход"),
                ToolTip = Lang.T("Прочитать журнал загрузки Windows и показать, какие программы замедляют вход. Может понадобиться подтверждение администратора."),
                Margin = new Thickness(0, 0, 8, 4)
            };
            startupImpact.Click += async (s, e) =>
            {
                await ReadBoot();
                FilterStartup();
            };
            buttons.Children.Add(startupImpact);
            startupRefresh = new Button
            {
                Content = Lang.T("Обновить список"),
                Margin = new Thickness(0, 0, 0, 4)
            };
            startupRefresh.Click += async (s, e) => await ReadStartup();
            buttons.Children.Add(startupRefresh);
            root.IsVisibleChanged += async (s, e) =>
            {
                if (root.IsVisible && !smoke)
                    await ReadStartup();
            };
            RefreshStartupEnabled();
        }

        private void RefreshStartupEnabled()
        {
            if (startupList == null)
                return;
            var selected = startupList.SelectedItem as StartupEntry;
            bool available = !busy && !readingStartup;
            bool task = selected != null && selected.Source == StartupTasks.Source;
            startupEnable.Content = task ? Lang.T("Включить задачу") : Lang.T("Включить при входе");
            startupDisable.Content = task ? Lang.T("Отключить задачу") : Lang.T("Отключить при входе");
            startupRefresh.IsEnabled = available;
            startupImpact.IsEnabled = available && !readingBoot;
            startupEnable.IsEnabled = available && selected != null && selected.CanChange && selected.Enabled == false;
            startupDisable.IsEnabled = available && selected != null && selected.CanChange && selected.Enabled == true;
        }

        private void FilterStartup()
        {
            if (startupList == null)
                return;
            string key = (startupList.SelectedItem as StartupEntry) == null ? null : ((StartupEntry)startupList.SelectedItem).Key;
            string query = startupSearch.Text.Trim();
            foreach (var entry in startupSnapshot == null ? new StartupEntry[0] : startupSnapshot.Entries)
                entry.ImpactMs = BootPerformance.Impact(bootReport, entry.Command);
            var rows = (startupSnapshot == null ? new StartupEntry[0] : startupSnapshot.Entries).Where(r => (startupSource == null || startupSource.SelectedIndex == 0 || startupSource.SelectedIndex == 1 && r.Source.EndsWith("run") || startupSource.SelectedIndex == 1 && r.Source == "machine-run32" || startupSource.SelectedIndex == 2 && r.Source.EndsWith("folder") || startupSource.SelectedIndex == 3 && r.Source == StartupTasks.Source || startupSource.SelectedIndex == 4 && r.Source == StoreStartup.Source) && (r.Title + " " + r.Name + " " + r.Command + " " + r.Location).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 && (startupFilter.SelectedIndex == 0 || startupFilter.SelectedIndex == 1 && r.Enabled == true || startupFilter.SelectedIndex == 2 && r.Enabled == false || startupFilter.SelectedIndex == 3 && !r.Enabled.HasValue)).ToArray();
            // With the boot log read, the programs that slow the sign-in the most come first.
            if (bootReport != null)
                rows = rows.OrderByDescending(r => r.ImpactMs ?? -1).ToArray();
            startupList.ItemsSource = rows;
            startupList.SelectedItem = rows.FirstOrDefault(r => r.Key == key);
            if (startupSnapshot != null)
                startupStatus.Text = Lang.T("Показано: ") + rows.Length + Lang.T(" из ") + startupSnapshot.Entries.Length + Lang.T(" · Всего включено: ") + startupSnapshot.Entries.Count(r => r.Enabled == true) + Lang.T(" · Отключено: ") + startupSnapshot.Entries.Count(r => r.Enabled == false) + Lang.T(" · Неизвестно: ") + startupSnapshot.Entries.Count(r => !r.Enabled.HasValue) + (startupSnapshot.Errors.Length == 0 ? "" : Lang.T(". Не все источники прочитаны: ") + string.Join("; ", startupSnapshot.Errors));
            RefreshStartupEnabled();
        }

        private async Task ReadStartup()
        {
            if (busy)
                return;
            if (readingStartup)
            {
                startupReadPending = true;
                return;
            }

            do
            {
                startupReadPending = false;
                int epoch = startupEpoch;
                readingStartup = true;
                RefreshStartupEnabled();
                startupStatus.Text = Lang.T("Читаем записи и состояние Windows…");
                try
                {
                    var read = startupRead;
                    var result = await Task.Run(() => read());
                    if (closed)
                        return;
                    if (epoch != startupEpoch)
                        startupReadPending = true;
                    else
                    {
                        startupSnapshot = result;
                        FilterStartup();
                    }
                }
                catch (Exception ex)
                {
                    if (epoch != startupEpoch)
                        startupReadPending = true;
                    else
                    {
                        startupSnapshot = null;
                        startupList.ItemsSource = null;
                        startupStatus.Text = Lang.T("Не удалось прочитать автозагрузку: ") + ex.Message;
                    }
                }
                finally
                {
                    readingStartup = false;
                    if (!closed)
                        RefreshStartupEnabled();
                }
            }
            while (startupReadPending && !busy && !closed);
        }

        private async Task ChangeStartup(bool enabled)
        {
            var entry = startupList.SelectedItem as StartupEntry;
            if (busy || readingStartup || entry == null || !entry.CanChange || entry.Enabled.Value == enabled)
                return;
            if (!await Confirm((enabled ? Lang.T("Включить") : Lang.T("Отключить")) + Lang.T(" запуск «") + entry.Title + Lang.T("» при входе в Windows?\n\n") + entry.Location + "\n" + entry.Command + "\n" + (entry.Details ?? "") + "\n\n" + (enabled ? Lang.T("При следующем входе Windows сможет запустить эту команду.") : Lang.T("Программа не запустится через эту запись. Её уведомления, синхронизация и другие фоновые функции могут стать недоступны до ручного запуска.")) + Lang.T(" Уже работающие процессы не изменятся. Прежнее состояние можно вернуть через историю.")))
                return;
            await RunStartupChange(entry, enabled ? "enable" : "disable", null);
        }

        private async Task RunStartupChange(StartupEntry entry, string action, string restore)
        {
            if (busy)
                return;
            SetBusy(true);
            try
            {
                var result = await startupRun(entry, action, restore);
                Get<TextBox>("Output").Text = result.Output;
                Text("Status", result.Code == 0 ? Lang.T("Состояние автозагрузки сохранено.") : Lang.T("Изменение требует внимания. Причина — в выводе."));
                if (result.Code != 0)
                    ExpandOutput(true);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Автозагрузка не изменена: ") + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }

            ReadHistory();
            await ReadStartup();
        }

        private async Task RestoreStartupHistory(string id)
        {
            if (busy)
                return;
            try
            {
                var record = StartupActions.Read(id);
                if (record.Action == "restore" || record.Status == "REVERTED")
                    throw new InvalidOperationException(Lang.T("Это изменение уже возвращено."));
                var entry = await Task.Run(() => StartupEntries.Inspect(record.Source, record.Name));
                if (entry.Identity != record.Identity || entry.Approval != record.After)
                    throw new InvalidOperationException(Lang.T("Запись изменилась. Сначала отмените более поздние изменения; прежнее состояние не будет записано поверх чужого изменения."));
                if (!await Confirm(Lang.T("Вернуть прежнюю автозагрузку «") + record.Name + Lang.T("»?\n\nБудет восстановлено состояние до выбранного изменения: ") + (StartupEntries.Decode(record.Before) == true ? Lang.T("включено") : Lang.T("отключено")) + Lang.T(". Изменение относится к следующему входу в Windows.")))
                    return;
                await RunStartupChange(entry, "restore", id);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Не удалось вернуть автозагрузку: ") + ex.Message);
            }
        }

        private static HistoryRow[] StartupHistoryRows()
        {
            return StartupActions.History().Select(r => new HistoryRow { Run = r.Id, StartupChange = true, TimeUtc = DateTime.Parse(r.TimeUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind), Title = (r.Action == "restore" ? Lang.T("Возврат автозагрузки: ") : r.Action == "enable" ? Lang.T("Автозагрузка включена: ") : Lang.T("Автозагрузка отключена: ")) + (r.Source == StoreStartup.Source ? r.Name.Split('_')[0] : r.Name), Status = r.Status == "OK" ? Lang.T("Применено") : r.Status == "REVERTED" ? Lang.T("Откат выполнен") : Lang.T("Требует внимания"), CanRevert = r.Action != "restore" && r.Status != "REVERTED" }).ToArray();
        }
    }
}
