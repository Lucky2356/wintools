using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private readonly string[] integrityActions =
        {
            "dism-scan",
            "sfc-verify",
            "dism-status",
            "windows-repair",
            "dism-repair",
            "sfc-repair"
        };
        private ComboBox integrityChoice;
        private TextBlock integrityDescription, integrityStatus;
        private TextBox integrityReport;
        private Button integrityStart, integrityStop;
        private ProgressBar integrityProgress;
        private bool integrityRunning, integrityCancelRequested;
        private Action integrityCancel;
        private Func<string, Action<string>, Action<Action>, Task<EngineResult>> integrityRun = IntegrityActions.Run;
        private void InitializeIntegrity()
        {
            var root = new Grid
            {
                Visibility = Visibility.Collapsed
            };
            Window.RegisterName("IntegrityPage", root);
            ((Grid)Get<FrameworkElement>("SettingsPage").Parent).Children.Add(root);
            foreach (var height in new[]
            {
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto,
                new GridLength(1, GridUnitType.Star)
            }

            )
                root.RowDefinitions.Add(new RowDefinition { Height = height });
            integrityChoice = new ComboBox
            {
                ItemsSource = new[]
                {
                    Lang.T("Компоненты Windows · полная проверка"),
                    Lang.T("Системные файлы · проверка без исправления"),
                    Lang.T("Компоненты · статус прошлой проверки"),
                    Lang.T("Восстановить Windows · DISM → SFC"),
                    Lang.T("Восстановить только компоненты · DISM"),
                    Lang.T("Восстановить только системные файлы · SFC"),
                    Lang.T("Очистка · расчёт объёма и удаление"),
                    Lang.T("Обновления Windows · пауза и часы активности")
                },
                SelectedIndex = 0,
                Margin = new Thickness(0, 0, 0, 12)
            };
            System.Windows.Automation.AutomationProperties.SetName(integrityChoice, Lang.T("Вид обслуживания Windows"));
            root.Children.Add(integrityChoice);
            integrityDescription = Paragraph("");
            Grid.SetRow(integrityDescription, 1);
            root.Children.Add(integrityDescription);
            integrityChoice.SelectionChanged += (s, e) => DescribeIntegrityChoice();
            DescribeIntegrityChoice();
            var buttons = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            integrityStart = new Button
            {
                Content = Lang.T("Начать проверку"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            integrityStart.Style = (Style)Window.FindResource("Primary");
            integrityStart.Click += async (s, e) => await StartIntegrity();
            buttons.Children.Add(integrityStart);
            integrityStop = new Button
            {
                Content = Lang.T("Запросить отмену"),
                IsEnabled = false,
                Margin = new Thickness(0, 0, 0, 8)
            };
            integrityStop.Click += (s, e) => CancelIntegrity();
            buttons.Children.Add(integrityStop);
            var status = new StackPanel();
            Grid.SetRow(status, 3);
            root.Children.Add(status);
            integrityProgress = new ProgressBar
            {
                Height = 5,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 10),
                Minimum = 0,
                Maximum = 100
            };
            integrityProgress.SetResourceReference(Control.ForegroundProperty, "Accent");
            status.Children.Add(integrityProgress);
            integrityStatus = Paragraph(Lang.T("Проверка ещё не выполнялась. Результаты сохраняются в истории приложения."));
            status.Children.Add(integrityStatus);
            integrityReport = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = Lang.T("Здесь появится итог проверки и сообщения Windows."),
                Padding = new Thickness(14)
            };
            System.Windows.Automation.AutomationProperties.SetName(integrityReport, Lang.T("Отчёт проверки Windows"));
            Grid.SetRow(integrityReport, 4);
            root.Children.Add(integrityReport);
            InitializeCleanup(root);
            Click("HistoryReport", () =>
            {
                var row = Get<ListBox>("History").SelectedItem as HistoryRow;
                if (!busy && row != null)
                {
                    if (row.IntegrityCheck)
                        ShowIntegrityReport(row.Run);
                    else if (row.StoreChange)
                        ShowStoreReport(row.Run);
                }
            });
        }

        private void DescribeIntegrityChoice()
        {
            if (integrityDescription == null)
                return;
            MaintenanceView();
            int choice = integrityChoice.SelectedIndex;
            if (choice >= integrityActions.Length)
                return;
            bool repair = choice >= 3;
            if (integrityStart != null)
                integrityStart.Content = repair ? Lang.T("Начать восстановление") : Lang.T("Начать проверку");
            integrityDescription.Text = choice == 0 ? Lang.T("DISM проверит хранилище компонентов, из которого Windows восстанавливает системные файлы. Это может занять несколько минут. Файлы не исправляются. Windows запросит права администратора.") : choice == 1 ? Lang.T("SFC проверит защищённые системные файлы без исправления. Проверка может занять несколько минут; остановить SFC из приложения нельзя. Не закрывайте Windows до завершения. Потребуются права администратора.") : choice == 2 ? Lang.T("Быстро читает отметку о повреждении компонентов, сохранённую Windows ранее. Новое сканирование не выполняется. Для проверки текущего состояния выберите полную проверку. Потребуются права администратора.") : choice == 3 ? Lang.T("Сначала DISM проверит и при необходимости восстановит компоненты Windows, затем SFC исправит системные файлы. Windows может скачать нужные файлы. Процесс может занять длительное время; прервать эту последовательность из приложения нельзя. Результаты каждого этапа сохраняются в отчёте.") : choice == 4 ? Lang.T("DISM найдёт повреждения компонентов, восстановит исправимые и повторит проверку. Может потребоваться интернет для загрузки файлов Windows. Штатную отмену можно запросить, но Windows принимает её не на каждом этапе; после отмены потребуется новая проверка.") : Lang.T("SFC проверит и попробует исправить защищённые системные файлы. При повреждённом хранилище сначала выполните восстановление компонентов DISM. SFC нельзя остановить из приложения. Результат сохраняется в отчёте.");
        }

        private void RefreshIntegrityEnabled()
        {
            if (integrityStart == null)
                return;
            integrityStart.IsEnabled = integrityChoice.IsEnabled = !busy;
            integrityStop.IsEnabled = integrityRunning && integrityCancel != null && !integrityCancelRequested;
            RefreshCleanupEnabled();
        }

        private void CancelIntegrity()
        {
            if (!integrityRunning || integrityCancel == null || integrityCancelRequested)
                return;
            integrityCancelRequested = true;
            integrityCancel();
            integrityStatus.Text = Lang.T("Отмена запрошена. Ждём, пока Windows завершит допустимый этап; процесс принудительно не прерывается.");
            RefreshIntegrityEnabled();
        }

        private async Task StartIntegrity()
        {
            if (busy || integrityChoice.SelectedIndex < 0 || integrityChoice.SelectedIndex >= integrityActions.Length)
                return;
            string action = integrityActions[integrityChoice.SelectedIndex];
            bool repair = IntegrityActions.IsRepair(action);
            if (repair && !await Confirm(Lang.T("Начать: ") + IntegrityActions.Title(action) + "?\n\n" + integrityDescription.Text + Lang.T("\n\nWindows будет изменять системные файлы. Отдельного отката этих исправлений в истории Wintools нет. ") + (preferences.RestorePoint ? Lang.T("Запросим точку восстановления; если Windows её не создаст, причина появится в отчёте и обслуживание продолжится.") : Lang.T("Запрос точки восстановления выключен в настройках приложения.")) + Lang.T("\n\nСохраните работу и подключите ноутбук к питанию. Перезагрузка автоматически не выполняется.")))
                return;
            integrityRunning = true;
            integrityCancelRequested = false;
            integrityCancel = null;
            SetBusy(true);
            integrityReport.Text = "";
            Get<TextBox>("Output").Text = Lang.T("Ход обслуживания показан в разделе «Обслуживание».");
            integrityProgress.Visibility = Visibility.Visible;
            integrityProgress.IsIndeterminate = true;
            integrityStatus.Text = Lang.T("Запускаем обслуживание. Подтвердите запрос Windows на права администратора.");
            var started = DateTime.UtcNow;
            try
            {
                var result = await integrityRun(action, value =>
                {
                    integrityReport.Text = IntegrityActions.CleanLog(value);
                    integrityReport.ScrollToEnd();
                    var percent = IntegrityPercent(value);
                    if (percent.HasValue)
                    {
                        integrityProgress.IsIndeterminate = false;
                        integrityProgress.Value = percent.Value;
                    }

                    if (!integrityCancelRequested)
                        integrityStatus.Text = Lang.T("Выполняется: ") + IntegrityActions.Title(action) + Lang.T(" · Прошло ") + (DateTime.UtcNow - started).ToString(@"hh\:mm\:ss") + ". Отдельный этап может долго оставаться на одном значении.";
                }, cancel =>
                {
                    integrityCancel = cancel;
                    RefreshIntegrityEnabled();
                });
                integrityReport.Text = result.Output;
                Get<TextBox>("Output").Text = result.Output;
                integrityStatus.Text = result.Code == 0 ? (repair ? Lang.T("Обслуживание завершено. Что удалось исправить — в отчёте ниже.") : Lang.T("Проверка завершена. Итог — в отчёте ниже; результат не означает оценку быстродействия ПК.")) : result.Code == 2 ? Lang.T("Операция отменена. Полного результата нет; после исправления компонентов повторите проверку.") : Lang.T("Операция не завершена успешно. Причина — в отчёте ниже.");
            }
            catch (Exception ex)
            {
                integrityStatus.Text = Lang.T("Не удалось выполнить обслуживание: ") + ex.Message;
                integrityReport.Text = ex.Message;
            }
            finally
            {
                integrityCancel = null;
                integrityRunning = false;
                integrityProgress.Visibility = Visibility.Collapsed;
                SetBusy(false);
                ReadHistory();
                Text("Status", integrityStatus.Text);
            }
        }

        private static int? IntegrityPercent(string text)
        {
            var matches = Regex.Matches(text ?? "", @"@progress\|(\d{1,3})\b|(?<![\d.])(\d{1,3})\s*%");
            if (matches.Count == 0)
                return null;
            var match = matches[matches.Count - 1];
            int value = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            return value <= 100 ? (int? )value : null;
        }

        private void ShowIntegrityReport(string id)
        {
            try
            {
                integrityChoice.SelectedIndex = 0;
                integrityReport.Text = IntegrityActions.Report(id);
                Get<TextBox>("Output").Text = integrityReport.Text;
                integrityStatus.Text = Lang.T("Сохранённый отчёт. Новая проверка не запускалась.");
                ShowPage(11);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Не удалось открыть отчёт: ") + ex.Message);
            }
        }

        private static string IntegrityStateLabel(string state)
        {
            return state == "repaired" ? Lang.T("Восстановлено") : state == "healthy" ? Lang.T("Повреждения не найдены") : state == "not-marked" ? Lang.T("Прошлый статус") : state == "cancelled" ? Lang.T("Отменено") : state == "pending" ? Lang.T("Не завершено") : state == "review" ? Lang.T("Прочитайте отчёт") : state == "failed" ? Lang.T("Ошибка обслуживания") : Lang.T("Нуждается во внимании");
        }

        private static HistoryRow[] IntegrityHistoryRows()
        {
            return IntegrityActions.History().Select(r => new HistoryRow { Run = r.Id, IntegrityCheck = true, Title = IntegrityActions.Title(r.Action), TimeUtc = DateTime.Parse(r.TimeUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind), Status = IntegrityStateLabel(r.State), CanRevert = false }).ToArray();
        }
    }
}
