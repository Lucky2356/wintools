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
        private UpdateSettings updateSettings;
        private UpdateChange updateRestoreRecord;
        private ComboBox updatePauseDays, updateHoursStart, updateHoursEnd;
        private TextBlock updateCurrent, updateStatus;
        private ItemsControl updateHistory;
        private Button updatePause, updateResume, updateHours, updateRefresh, updateRestore;
        private bool readingUpdates;
        private Func<UpdateSettings> updateRead = WindowsUpdates.Read;
        private Func<int, UpdateHistoryRow[]> updateInstalled = WindowsUpdates.InstalledHistory;
        private Func<string, string, string, string, Task<EngineResult>> updateRun = WindowsUpdates.Run;
        private void InitializeWindowsUpdate(StackPanel parent)
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
            var heading = Paragraph(Lang.T("Обновления Windows"));
            heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
            panel.Children.Add(heading);
            panel.Children.Add(Intro(Lang.T("Приостановите обновления на время или задайте часы, когда Windows не перезагружает ПК."), Lang.T("Пауза откладывает установку обновлений, как кнопка «Приостановить» в параметрах Windows: не дольше 35 дней, после чего Windows обновится. Часы активности — время, когда Windows не перезагружает ПК для установки обновлений. Отключать обновления совсем Wintools не предлагает: они закрывают уязвимости.")));
            updateCurrent = Paragraph(Lang.T("Нажмите «Обновить», чтобы прочитать настройки."));
            panel.Children.Add(updateCurrent);
            var pause = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(pause);
            updatePauseDays = new ComboBox
            {
                Width = 150,
                Margin = new Thickness(0, 0, 10, 8),
                ItemsSource = new[]
                {
                    Lang.T("на 7 дней"),
                    Lang.T("на 14 дней"),
                    Lang.T("на 21 день"),
                    Lang.T("на 35 дней")
                },
                SelectedIndex = 0
            };
            System.Windows.Automation.AutomationProperties.SetName(updatePauseDays, Lang.T("Срок паузы обновлений"));
            pause.Children.Add(updatePauseDays);
            updatePause = new Button
            {
                Content = Lang.T("Приостановить обновления"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            updatePause.Click += async (s, e) => await ChangeUpdates("pause");
            pause.Children.Add(updatePause);
            updateResume = new Button
            {
                Content = Lang.T("Возобновить"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            updateResume.Click += async (s, e) => await ChangeUpdates("resume");
            pause.Children.Add(updateResume);
            var hours = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(hours);
            var range = Enumerable.Range(0, 24).Select(h => h.ToString("00", CultureInfo.InvariantCulture) + ":00").ToArray();
            hours.Children.Add(new TextBlock { Text = Lang.T("Часы активности с"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
            updateHoursStart = new ComboBox
            {
                Width = 90,
                ItemsSource = range,
                SelectedIndex = 8,
                Margin = new Thickness(0, 0, 8, 8)
            };
            hours.Children.Add(updateHoursStart);
            hours.Children.Add(new TextBlock { Text = Lang.T("до"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) });
            updateHoursEnd = new ComboBox
            {
                Width = 90,
                ItemsSource = range,
                SelectedIndex = 23,
                Margin = new Thickness(0, 0, 10, 8)
            };
            hours.Children.Add(updateHoursEnd);
            System.Windows.Automation.AutomationProperties.SetName(updateHoursStart, Lang.T("Начало часов активности"));
            System.Windows.Automation.AutomationProperties.SetName(updateHoursEnd, Lang.T("Конец часов активности"));
            updateHours = new Button
            {
                Content = Lang.T("Задать часы"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            updateHours.Click += async (s, e) => await ChangeUpdates("hours");
            hours.Children.Add(updateHours);
            updateHoursStart.SelectionChanged += (s, e) => RefreshUpdateEnabled();
            updateHoursEnd.SelectionChanged += (s, e) => RefreshUpdateEnabled();
            var tools = new WrapPanel();
            panel.Children.Add(tools);
            updateRefresh = new Button
            {
                Content = Lang.T("Обновить"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            updateRefresh.Click += async (s, e) => await RefreshUpdates();
            tools.Children.Add(updateRefresh);
            updateRestore = new Button
            {
                Content = Lang.T("Вернуть прежние настройки"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            updateRestore.Click += async (s, e) =>
            {
                if (updateRestoreRecord != null)
                    await RestoreUpdateHistory(updateRestoreRecord.Id);
            };
            tools.Children.Add(updateRestore);
            var open = new Button
            {
                Content = Lang.T("Центр обновления ↗"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            open.Click += (s, e) => OpenTool("ms-settings:windowsupdate");
            tools.Children.Add(open);
            updateStatus = Paragraph("");
            updateStatus.FontSize = 12;
            panel.Children.Add(updateStatus);
            var historyHeading = Paragraph(Lang.T("Последние установленные обновления"));
            historyHeading.FontWeight = FontWeights.SemiBold;
            historyHeading.Margin = new Thickness(0, 8, 0, 6);
            panel.Children.Add(historyHeading);
            updateHistory = new ItemsControl();
            updateHistory.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Margin='0,0,0,8'><TextBlock Text='{Binding Title}' TextWrapping='Wrap'/><TextBlock Text='{Binding Detail}' Foreground='{DynamicResource Muted}' FontSize='12'/></StackPanel></DataTemplate>");
            panel.Children.Add(updateHistory);
            // Read the current settings whenever the Windows Update view of Maintenance opens.
            parent.IsVisibleChanged += async (s, e) =>
            {
                if (!smoke && parent.IsVisible)
                    await RefreshUpdates();
            };
            RefreshUpdateEnabled();
        }

        private string UpdateHoursArgument()
        {
            return updateHoursStart.SelectedIndex + "-" + updateHoursEnd.SelectedIndex;
        }

        private bool ValidHours()
        {
            try
            {
                WindowsUpdates.Validate("hours", UpdateHoursArgument(), new string ('0', 16), null);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void RefreshUpdateEnabled()
        {
            if (updatePause == null)
                return;
            bool idle = !busy && !readingUpdates && updateSettings != null;
            updatePause.IsEnabled = idle;
            updateResume.IsEnabled = idle && updateSettings.PausedUntil.HasValue;
            updateHours.IsEnabled = idle && ValidHours() && !(updateSettings.ActiveStart == updateHoursStart.SelectedIndex && updateSettings.ActiveEnd == updateHoursEnd.SelectedIndex);
            updateRefresh.IsEnabled = !busy && !readingUpdates;
            updateRestore.IsEnabled = idle && updateRestoreRecord != null;
        }

        private async Task RefreshUpdates()
        {
            if (busy || readingUpdates || updatePause == null)
                return;
            readingUpdates = true;
            RefreshUpdateEnabled();
            try
            {
                var read = updateRead;
                var settings = await Task.Run(() => read());
                if (closed)
                    return;
                updateSettings = settings;
                updateCurrent.Text = (settings.PausedUntil.HasValue ? Lang.T("Обновления приостановлены до ") + settings.PausedUntil.Value.ToLocalTime().ToString("g", Lang.Culture) + "." : Lang.T("Обновления не приостановлены.")) + " " + (settings.ActiveStart.HasValue ? Lang.T("Часы активности: ") + settings.ActiveStart.Value.ToString("00") + ":00–" + settings.ActiveEnd.Value.ToString("00") + ":00." : Lang.T("Часы активности Windows определяет сама."));
                if (settings.ActiveStart.HasValue)
                {
                    updateHoursStart.SelectedIndex = settings.ActiveStart.Value;
                    updateHoursEnd.SelectedIndex = settings.ActiveEnd.Value;
                }

                updateRestoreRecord = null;
                try
                {
                    updateRestoreRecord = WindowsUpdates.History().FirstOrDefault(r => r.Action != "restore" && r.Status != "REVERTED" && r.AfterHash == settings.Fingerprint);
                }
                catch (Exception ex)
                {
                    updateStatus.Text = Lang.T("История изменений недоступна: ") + ex.Message;
                }

                try
                {
                    var installed = updateInstalled;
                    updateHistory.ItemsSource = await Task.Run(() => installed(10));
                    if (updateHistory.Items.Count == 0)
                        updateStatus.Text = Lang.T("Журнал установленных обновлений пуст.");
                }
                catch (Exception ex)
                {
                    updateHistory.ItemsSource = null;
                    updateStatus.Text = Lang.T("Журнал обновлений недоступен: ") + ex.Message;
                }
            }
            catch (Exception ex)
            {
                updateSettings = null;
                updateRestoreRecord = null;
                updateCurrent.Text = Lang.T("Не удалось прочитать настройки обновлений: ") + ex.Message;
            }
            finally
            {
                readingUpdates = false;
                if (!closed)
                    RefreshUpdateEnabled();
            }
        }

        private async Task ChangeUpdates(string action)
        {
            var settings = updateSettings;
            if (busy || readingUpdates || settings == null)
                return;
            string argument = action == "pause" ? new[]
            {
                "7",
                "14",
                "21",
                "35"
            }[Math.Max(0, updatePauseDays.SelectedIndex)] : action == "hours" ? UpdateHoursArgument() : "-";
            string message = action == "pause" ? Lang.T("Приостановить обновления Windows на ") + argument + Lang.T(" дн.?\n\nОбновления безопасности тоже не будут устанавливаться до конца паузы. Прежние настройки сохраним для возврата.") : action == "resume" ? Lang.T("Возобновить обновления Windows?\n\nWindows сможет сразу начать загрузку и установку.") : Lang.T("Задать часы активности ") + argument.Replace("-", ":00–") + Lang.T(":00?\n\nВ это время Windows не будет перезагружать ПК для установки обновлений.");
            if (!await Confirm(message))
                return;
            await RunUpdateChange(action, argument, settings.Fingerprint, null);
        }

        private async Task RunUpdateChange(string action, string argument, string expected, string restore)
        {
            if (busy)
                return;
            SetBusy(true);
            updateStatus.Text = Lang.T("Меняем настройки обновлений…");
            try
            {
                var result = await updateRun(action, argument, expected, restore);
                Get<TextBox>("Output").Text = result.Output;
                Text("Status", result.Code == 0 ? Lang.T("Настройки обновлений изменены.") : Lang.T("Изменение настроек обновлений требует внимания. Подробности — в выводе."));
                if (result.Code != 0)
                    ExpandOutput(true);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Настройки обновлений не изменены: ") + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }

            ReadHistory();
            await RefreshUpdates();
        }

        private async Task RestoreUpdateHistory(string id)
        {
            if (busy)
                return;
            try
            {
                var record = WindowsUpdates.ReadRecord(id);
                if (record.Action == "restore" || record.Status == "REVERTED")
                    throw new InvalidOperationException(Lang.T("Это изменение уже отменено."));
                var read = updateRead;
                var settings = await Task.Run(() => read());
                if (record.AfterHash != settings.Fingerprint)
                    throw new InvalidOperationException(Lang.T("Настройки обновлений изменены после этой записи. Возврат отменён."));
                if (!await Confirm(Lang.T("Вернуть настройки обновлений, действовавшие до «") + record.Detail + "»?"))
                    return;
                await RunUpdateChange("restore", "-", settings.Fingerprint, id);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Возврат настроек обновлений невозможен: ") + ex.Message);
            }
        }

        private static HistoryRow[] UpdateHistoryRows()
        {
            return WindowsUpdates.History().Select(r => new HistoryRow { Run = r.Id, UpdateChange = true, TimeUtc = DateTime.Parse(r.TimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Title = r.Detail, Status = r.Status == "OK" ? Lang.T("Применено") : r.Status == "REVERTED" ? Lang.T("Откат выполнен") : Lang.T("Требует внимания"), CanRevert = r.Action != "restore" && r.Status != "REVERTED" && r.AfterHash != null }).ToArray();
        }
    }
}
