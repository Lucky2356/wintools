using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private PowerSnapshot powerSnapshot;
        private PowerChange powerRestoreRecord;
        private ComboBox powerChoice;
        private TextBlock powerCurrent, powerDescription, powerStatus;
        private Button powerApply, powerRefresh, powerRestore;
        private bool readingPower;
        private Func<PowerSnapshot> powerRead = PowerPlans.Read;
        private Func<string, string, string, Task<EngineResult>> powerRun = PowerActions.Run;
        private void InitializePowerManagement(StackPanel parent)
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
            var heading = Paragraph(Lang.T("Схема питания"));
            heading.FontSize = 21;
            heading.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(heading);
            panel.Children.Add(Intro(Lang.T("Схема питания задаёт баланс между скоростью, нагревом и расходом батареи."), Lang.T("Схема определяет используемые настройки питания, сна и производительности. Высокая производительность может увеличить нагрев и расход батареи. Показываем только схемы, доступные Windows на этом ПК; режим питания в параметрах Windows может настраиваться отдельно.")));
            // The list already marks the active plan; this line speaks only while reading or when the plan is unknown.
            powerCurrent = Paragraph(Lang.T("Читаем схемы питания…"));
            panel.Children.Add(powerCurrent);
            powerChoice = new ComboBox
            {
                DisplayMemberPath = "Label",
                Margin = new Thickness(0, 0, 0, 12)
            };
            System.Windows.Automation.AutomationProperties.SetName(powerChoice, Lang.T("Схема питания Windows"));
            panel.Children.Add(powerChoice);
            powerChoice.SelectionChanged += (s, e) =>
            {
                var selected = powerChoice.SelectedItem as PowerPlan;
                powerDescription.Text = selected == null ? "" : selected.Description;
                RefreshPowerEnabled();
            };
            powerDescription = Paragraph("");
            panel.Children.Add(powerDescription);
            var buttons = new WrapPanel();
            panel.Children.Add(buttons);
            powerApply = new Button
            {
                Content = Lang.T("Использовать схему"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            powerApply.Style = (Style)Window.FindResource("Primary");
            powerApply.Click += async (s, e) => await SelectPowerPlan();
            buttons.Children.Add(powerApply);
            powerRefresh = new Button
            {
                Content = Lang.T("↻ Обновить"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            powerRefresh.SetResourceReference(FrameworkElement.StyleProperty, "Ghost");
            powerRefresh.Click += async (s, e) => await RefreshPowerPlans();
            buttons.Children.Add(powerRefresh);
            powerRestore = new Button
            {
                Content = Lang.T("Вернуть прежнюю схему"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            powerRestore.Click += async (s, e) =>
            {
                if (powerRestoreRecord != null)
                    await RestorePowerHistory(powerRestoreRecord.Id);
            };
            buttons.Children.Add(powerRestore);
            powerStatus = Paragraph(Lang.T("Переключение сохраняется в истории. Возврат выбирает прежнюю схему, но не отменяет последующие изменения её параметров."));
            powerStatus.FontSize = 12;
            panel.Children.Add(powerStatus);
            Get<ScrollViewer>("OptimizationPage").IsVisibleChanged += async (s, e) =>
            {
                if (!smoke && Get<ScrollViewer>("OptimizationPage").IsVisible)
                    await RefreshPowerPlans();
            };
            RefreshPowerEnabled();
        }

        private void RefreshPowerEnabled()
        {
            if (powerChoice == null)
                return;
            var choice = powerChoice.SelectedItem as PowerPlan;
            powerChoice.IsEnabled = powerRefresh.IsEnabled = !busy && !readingPower;
            powerApply.IsEnabled = !busy && !readingPower && powerSnapshot != null && choice != null && choice.Id != powerSnapshot.Active;
            powerRestore.IsEnabled = !busy && !readingPower && powerRestoreRecord != null;
            powerRestore.Visibility = powerRestoreRecord != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task RefreshPowerPlans()
        {
            if (busy || readingPower)
                return;
            readingPower = true;
            RefreshPowerEnabled();
            powerStatus.Text = Lang.T("Читаем схемы питания…");
            try
            {
                var read = powerRead;
                var snapshot = await Task.Run(() => read());
                if (closed)
                    return;
                string selected = (powerChoice.SelectedItem as PowerPlan) == null ? null : ((PowerPlan)powerChoice.SelectedItem).Id;
                powerSnapshot = snapshot;
                powerChoice.ItemsSource = snapshot.Plans;
                powerChoice.SelectedItem = snapshot.Plans.FirstOrDefault(p => p.Id == selected) ?? snapshot.Plans.FirstOrDefault(p => p.Id == snapshot.Active) ?? snapshot.Plans.FirstOrDefault();
                var active = snapshot.Plans.FirstOrDefault(p => p.Id == snapshot.Active);
                powerCurrent.Text = Lang.T("Используется сейчас: ") + (active == null ? snapshot.Active : active.Name);
                powerCurrent.Visibility = active == null ? Visibility.Visible : Visibility.Collapsed;
                powerStatus.Text = snapshot.Plans.Length == 1 ? Lang.T("Windows предоставляет одну схему. Дополнительные режимы могут быть доступны в параметрах питания Windows.") : Lang.T("Доступно схем: ") + snapshot.Plans.Length + Lang.T(". Выберите подходящую и подтвердите переключение.");
                try
                {
                    powerRestoreRecord = PowerActions.History().FirstOrDefault(r => r.Action == "select" && r.Status != "REVERTED");
                }
                catch (Exception ex)
                {
                    powerRestoreRecord = null;
                    powerStatus.Text = Lang.T("Схемы прочитаны, история недоступна: ") + ex.Message;
                }
            }
            catch (Exception ex)
            {
                powerSnapshot = null;
                powerRestoreRecord = null;
                powerChoice.ItemsSource = null;
                powerCurrent.Text = Lang.T("Текущая схема неизвестна.");
                powerCurrent.Visibility = Visibility.Visible;
                powerStatus.Text = Lang.T("Не удалось прочитать схемы: ") + ex.Message;
            }
            finally
            {
                readingPower = false;
                if (!closed)
                    RefreshPowerEnabled();
            }
        }

        private async Task SelectPowerPlan()
        {
            var selected = powerChoice.SelectedItem as PowerPlan;
            var snapshot = powerSnapshot;
            if (busy || readingPower || selected == null || snapshot == null || selected.Id == snapshot.Active)
                return;
            if (!await Confirm(Lang.T("Использовать схему «") + selected.Name + "»?\n\n" + selected.Description + Lang.T("\n\nИзменятся настройки питания, сна и производительности согласно этой схеме. Это может повлиять на нагрев и расход батареи. Сохраним прежнюю схему для возврата через историю.")))
                return;
            await RunPowerChange(selected.Id, snapshot.Active, null);
        }

        private async Task RunPowerChange(string target, string expected, string restore)
        {
            if (busy)
                return;
            SetBusy(true);
            powerStatus.Text = Lang.T("Переключаем схему питания…");
            try
            {
                var result = await powerRun(target, expected, restore);
                Get<TextBox>("Output").Text = result.Output;
                Text("Status", result.Code == 0 ? Lang.T("Схема питания переключена или уже используется.") : Lang.T("Переключение требует внимания. Подробности — в выводе."));
                if (result.Code != 0)
                    ExpandOutput(true);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Схема питания не переключена: ") + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }

            ReadHistory();
            await RefreshPowerPlans();
        }

        private async Task RestorePowerHistory(string id)
        {
            if (busy)
                return;
            try
            {
                var record = PowerActions.Read(id);
                if (record.Action != "select" || record.Status == "REVERTED")
                    throw new InvalidOperationException(Lang.T("Это изменение уже восстановлено или не поддерживает повторный откат."));
                var read = powerRead;
                var snapshot = await Task.Run(() => read());
                if (!snapshot.Plans.Any(p => p.Id == record.Before))
                    throw new InvalidOperationException(Lang.T("Прежняя схема удалена или недоступна. Создавать её заново по одному названию нельзя."));
                if (!await Confirm(Lang.T("Вернуть схему «") + record.BeforeName + Lang.T("»?\n\nБудет выбрана прежняя схема питания. Более поздний выбор другой программы будет заменён. Изменения параметров внутри схемы этим действием не отменяются.")))
                    return;
                await RunPowerChange(record.Before, snapshot.Active, id);
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Не удалось вернуть схему: ") + ex.Message);
            }
        }

        private static HistoryRow[] PowerHistoryRows()
        {
            return PowerActions.History().Select(r => new HistoryRow { Run = r.Id, PowerChange = true, TimeUtc = DateTime.Parse(r.TimeUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind), Title = (r.Action == "restore" ? Lang.T("Возврат схемы: ") : Lang.T("Схема питания: ")) + r.TargetName, Status = r.Status == "OK" ? Lang.T("Применено") : r.Status == "REVERTED" ? Lang.T("Откат выполнен") : Lang.T("Требует внимания"), CanRevert = r.Action == "select" && r.Status != "REVERTED" }).ToArray();
        }
    }
}
