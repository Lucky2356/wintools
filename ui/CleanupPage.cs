using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private ScrollViewer cleanupPanel;
        private FrameworkElement[] integrityContent;
        private readonly string[] cleanupTitles =
        {
            Lang.T("Временные файлы пользователя"),
            Lang.T("Временные файлы Windows"),
            Lang.T("Отчёты о сбоях приложений"),
            Lang.T("Кэш браузеров")
        };
        private readonly CheckBox[] cleanupChecks = new CheckBox[4];
        private readonly TextBlock[] cleanupValues = new TextBlock[4];
        private readonly CleanupEstimate[] cleanupEstimates = new CleanupEstimate[4];
        private Button cleanupScan, cleanupApply, cleanupStop;
        private TextBlock cleanupStatus;
        private CancellationTokenSource cleanupCancel;
        // Browser caches are measured by BrowserCache; the folder preview stays free of it so the maintenance test can compile it alone.
        private Func<string, CancellationToken, CleanupEstimate> cleanupRead = (id, cancel) => id == CleanupPreview.Ids[3] ? BrowserCache.Estimate(cancel) : CleanupPreview.Read(id, cancel);
        private Func<string, Action<string>, Task<EngineResult>> cleanupRun = (id, progress) => id == "CLN-BROWSER" ? Task.Run(() => BrowserCache.Clean()) : Engine.Run("cleanup", id, "-", false, false, progress);
        private void InitializeCleanup(Grid root)
        {
            integrityContent = root.Children.Cast<FrameworkElement>().Where(c => c != integrityChoice).ToArray();
            var panel = new StackPanel();
            cleanupPanel = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(cleanupPanel, 1);
            Grid.SetRowSpan(cleanupPanel, 4);
            root.Children.Add(cleanupPanel);
            panel.Children.Add(Intro(Lang.T("Рассчитайте объём, отметьте ненужное и удалите."), Lang.T("Сначала рассчитайте объём, затем отметьте нужные категории. Удаление необратимо. Возраст определяется по последнему изменению файла; занятые файлы могут остаться.")));
            var controls = new WrapPanel();
            panel.Children.Add(controls);
            cleanupScan = ToolButton(controls, Lang.T("Рассчитать объём"), async () => await ScanCleanup());
            cleanupApply = ToolButton(controls, Lang.T("Удалить выбранное…"), async () => await ApplyCleanup());
            cleanupStop = ToolButton(controls, Lang.T("Остановить расчёт"), () =>
            {
                if (cleanupCancel != null)
                    cleanupCancel.Cancel();
                RefreshCleanupEnabled();
            });
            foreach (Button button in controls.Children)
                button.Margin = new Thickness(0, 0, 8, 8);
            cleanupStatus = Paragraph(Lang.T("Расчёт ещё не выполнен. Загрузки, корзина, документы и данные браузеров, кроме дискового кэша, в эти категории не входят."));
            panel.Children.Add(cleanupStatus);
            for (int i = 0; i < cleanupTitles.Length; i++)
            {
                var card = new StackPanel();
                cleanupChecks[i] = new CheckBox
                {
                    Content = cleanupTitles[i],
                    IsEnabled = false,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                cleanupChecks[i].SetResourceReference(FrameworkElement.StyleProperty, "Tick");
                cleanupChecks[i].Click += (s, e) => RefreshCleanupEnabled();
                card.Children.Add(cleanupChecks[i]);
                var about = Paragraph(i == 3 ?Lang.T("Дисковый кэш Chrome, Edge, Brave, Яндекс Браузера, Vivaldi и Firefox. История, пароли, вкладки и вход на сайты сохраняются. Открытые браузеры пропускаются; первые страницы после очистки загрузятся чуть дольше.") : i == 2 ? Lang.T("Файлы CrashDumps старше 7 дней. Они могут понадобиться для выяснения причин сбоев.") : Lang.T("Файлы Temp старше 3 дней. Папки и ссылки пропускаются."));
                about.Margin = new Thickness(0, 0, 0, 8);
                about.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                card.Children.Add(about);
                cleanupValues[i] = Paragraph(Lang.T("Объём неизвестен"));
                cleanupValues[i].Margin = new Thickness(0);
                cleanupValues[i].FontWeight = FontWeights.SemiBold;
                card.Children.Add(cleanupValues[i]);
                var border = new Border
                {
                    Child = card,
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 10),
                    CornerRadius = new CornerRadius(14)
                };
                Card(border);
                panel.Children.Add(border);
            }

            InitializeDiskUsage(panel);
            RefreshCleanupEnabled();
        }

        private void MaintenanceView()
        {
            if (cleanupPanel == null)
                return;
            bool cleanup = integrityChoice.SelectedIndex == integrityActions.Length;
            cleanupPanel.Visibility = cleanup ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in integrityContent)
                item.Visibility = cleanup ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshCleanupEnabled()
        {
            if (cleanupScan == null)
                return;
            cleanupScan.IsEnabled = !busy;
            cleanupApply.IsEnabled = !busy && Enumerable.Range(0, cleanupTitles.Length).Any(i => cleanupChecks[i] != null && cleanupChecks[i].IsChecked == true && cleanupEstimates[i] != null && cleanupEstimates[i].Errors == 0 && cleanupEstimates[i].Files > 0);
            cleanupStop.Visibility = cleanupCancel == null ? Visibility.Collapsed : Visibility.Visible;
            cleanupStop.IsEnabled = cleanupCancel != null && !cleanupCancel.IsCancellationRequested;
            for (int i = 0; i < cleanupTitles.Length; i++)
                if (cleanupChecks[i] != null)
                    cleanupChecks[i].IsEnabled = !busy && cleanupEstimates[i] != null && cleanupEstimates[i].Errors == 0 && cleanupEstimates[i].Files > 0;
        }

        private async Task ScanCleanup()
        {
            if (busy)
                return;
            cleanupCancel = new CancellationTokenSource();
            SetBusy(true);
            for (int i = 0; i < cleanupTitles.Length; i++)
            {
                cleanupEstimates[i] = null;
                cleanupChecks[i].IsChecked = false;
                cleanupValues[i].Text = Lang.T("Ожидает расчёта");
            }

            RefreshCleanupEnabled();
            try
            {
                for (int i = 0; i < cleanupTitles.Length; i++)
                {
                    cleanupStatus.Text = Lang.T("Считаем: ") + cleanupTitles[i] + Lang.T("… Ничего не удаляем.");
                    var id = CleanupPreview.Ids[i];
                    var read = cleanupRead;
                    var token = cleanupCancel.Token;
                    var result = await Task.Run(() => read(id, token));
                    token.ThrowIfCancellationRequested();
                    cleanupEstimates[i] = result;
                    // Folder paths only matter when something looks wrong, so they move to the tooltip; browser names stay visible.
                    cleanupValues[i].ToolTip = result.Source;
                    cleanupValues[i].Text = (i == 3 ? result.Source + "\n" : "") + CleanupPreview.Size(result.Bytes) + Lang.T(" · Файлов: ") + result.Files + (result.SkippedLinks > 0 ? Lang.T(" · Пропущено ссылок: ") + result.SkippedLinks : "") + (result.Errors > 0 ? Lang.T("\nРасчёт неполный. ") + result.Error : "");
                }

                cleanupStatus.Text = Lang.T("Расчёт завершён. Объём приблизительный: это размеры файлов, а не гарантированно освобождаемое место. Перед удалением состав будет проверен заново.");
            }
            catch (OperationCanceledException)
            {
                cleanupStatus.Text = Lang.T("Расчёт остановлен. Ничего не удалено; доступны только завершённые категории.");
            }
            catch (Exception ex)
            {
                cleanupStatus.Text = Lang.T("Расчёт не завершён: ") + ex.Message;
            }
            finally
            {
                cleanupCancel.Dispose();
                cleanupCancel = null;
                SetBusy(false);
                RefreshCleanupEnabled();
                Text("Status", cleanupStatus.Text);
            }
        }

        private async Task ApplyCleanup()
        {
            if (busy)
                return;
            var selected = Enumerable.Range(0, cleanupTitles.Length).Where(i => cleanupChecks[i].IsChecked == true && cleanupEstimates[i] != null && cleanupEstimates[i].Errors == 0 && cleanupEstimates[i].Files > 0).ToArray();
            if (selected.Length == 0)
                return;
            if (!await Confirm(Lang.T("Удалить файлы из выбранных категорий?\n\n") + string.Join("\n", selected.Select(i => cleanupTitles[i] + Lang.T(": примерно ") + CleanupPreview.Size(cleanupEstimates[i].Bytes) + "\n" + cleanupEstimates[i].Source)) + Lang.T("\n\nФайлы старше 3 дней (отчёты о сбоях — 7 дней, кэш браузеров — целиком) будут удалены без корзины и отката. Состав мог измениться после расчёта. Windows может запросить права администратора.")))
                return;
            SetBusy(true);
            var output = new StringBuilder();
            bool success = true;
            try
            {
                foreach (int i in selected)
                {
                    cleanupStatus.Text = Lang.T("Очищаем: ") + cleanupTitles[i];
                    var result = await cleanupRun(CleanupPreview.Ids[i], text =>
                    {
                        Get<TextBox>("Output").Text = output.ToString() + text;
                    });
                    output.AppendLine(cleanupTitles[i]).AppendLine(result.Output);
                    cleanupEstimates[i] = null;
                    cleanupChecks[i].IsChecked = false;
                    cleanupValues[i].Text = Lang.T("Повторите расчёт после очистки");
                    if (result.Code != 0)
                    {
                        success = false;
                        break;
                    }
                }

                cleanupStatus.Text = success ? Lang.T("Команды очистки завершены. Повторите расчёт, чтобы проверить оставшиеся файлы. Подробности — в выводе.") : Lang.T("Очистка выполнена не полностью. Часть файлов могла быть удалена; последующие категории не запускались. Подробности — в выводе.");
            }
            catch (Exception ex)
            {
                success = false;
                output.AppendLine(ex.Message);
                cleanupStatus.Text = Lang.T("Очистка не завершена: ") + ex.Message + Lang.T(". Часть файлов могла быть удалена; повторите расчёт.");
                for (int i = 0; i < cleanupTitles.Length; i++)
                {
                    cleanupEstimates[i] = null;
                    cleanupChecks[i].IsChecked = false;
                    cleanupValues[i].Text = Lang.T("Объём неизвестен — повторите расчёт");
                }
            }
            finally
            {
                SetBusy(false);
                RefreshCleanupEnabled();
                Get<TextBox>("Output").Text = output.ToString();
                if (!success)
                    ExpandOutput(true);
                Text("Status", cleanupStatus.Text);
            }
        }
    }
}
