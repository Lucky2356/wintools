using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private TextBlock batteryStatus, backupStatus;
        private Button batteryRead, batteryOpen, backupList, backupCreate, backupDrivers, backupFolder;
        private ItemsControl backupPoints;
        private string batteryHtml, driverFolder;
        private bool readingBattery, runningBackup;
        private Func<string, Task<BatteryInfo[]>> batteryReader = Backups.ReadBatteries;
        private Func<string, Task<BackupResult>> backupRun = Backups.Run;
        private void InitializeBackups(Panel parent)
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
            var heading = Paragraph(Lang.T("Батарея ноутбука"));
            heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
            panel.Children.Add(heading);
            var buttons = new WrapPanel();
            panel.Children.Add(buttons);
            batteryRead = new Button
            {
                Content = Lang.T("Проверить износ батареи"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            batteryRead.Click += async (s, e) => await ReadBattery();
            buttons.Children.Add(batteryRead);
            batteryOpen = new Button
            {
                Content = Lang.T("Подробный отчёт Windows ↗"),
                Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = false
            };
            batteryOpen.Click += (s, e) =>
            {
                if (batteryHtml != null && File.Exists(batteryHtml))
                    OpenTool(batteryHtml);
            };
            buttons.Children.Add(batteryOpen);
            batteryStatus = Paragraph(Lang.T("Сравним текущую полную ёмкость с заводской по отчёту powercfg. Права администратора не нужны."));
            panel.Children.Add(batteryStatus);
            var backups = new StackPanel();
            var backupCard = new Border
            {
                Child = backups,
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Card(backupCard);
            parent.Children.Add(backupCard);
            var title = Paragraph(Lang.T("Точки восстановления и драйверы"));
            title.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
            backups.Children.Add(title);
            backups.Children.Add(Intro(Lang.T("Точка восстановления вернёт систему к прежнему состоянию, а копия драйверов пригодится после переустановки."), Lang.T("Точка восстановления позволяет вернуть системные файлы, драйверы и реестр к прежнему состоянию через «Восстановление системы»; личные файлы она не затрагивает. Копия драйверов пригодится после переустановки Windows: папку можно указать в диспетчере устройств. Действия требуют подтверждения Windows.")));
            var actions = new WrapPanel();
            backups.Children.Add(actions);
            backupList = new Button
            {
                Content = Lang.T("Показать точки"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            backupList.Click += async (s, e) => await RunBackup("list");
            actions.Children.Add(backupList);
            backupCreate = new Button
            {
                Content = Lang.T("Создать точку восстановления"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            backupCreate.Click += async (s, e) => await RunBackup("create");
            actions.Children.Add(backupCreate);
            backupDrivers = new Button
            {
                Content = Lang.T("Сохранить драйверы"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            backupDrivers.Click += async (s, e) => await RunBackup("drivers");
            actions.Children.Add(backupDrivers);
            backupFolder = new Button
            {
                Content = Lang.T("Открыть папку драйверов"),
                Margin = new Thickness(0, 0, 10, 8),
                IsEnabled = false
            };
            backupFolder.Click += (s, e) =>
            {
                if (driverFolder != null && Directory.Exists(driverFolder))
                    OpenTool(driverFolder);
            };
            actions.Children.Add(backupFolder);
            var restore = new Button
            {
                Content = Lang.T("Восстановление системы ↗"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            restore.Click += (s, e) => OpenTool(Path.Combine(Environment.SystemDirectory, "rstrui.exe"));
            actions.Children.Add(restore);
            backupStatus = Paragraph("");
            backups.Children.Add(backupStatus);
            backupPoints = new ItemsControl();
            backupPoints.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Margin='0,0,0,8'><TextBlock Text='{Binding Description}' TextWrapping='Wrap'/><TextBlock Text='{Binding Detail}' Foreground='{DynamicResource Muted}' FontSize='12'/></StackPanel></DataTemplate>");
            backups.Children.Add(backupPoints);
        }

        private void RefreshBackupsEnabled()
        {
            if (batteryRead == null)
                return;
            batteryRead.IsEnabled = !busy && !readingBattery;
            backupList.IsEnabled = backupCreate.IsEnabled = backupDrivers.IsEnabled = !busy && !runningBackup;
            backupFolder.IsEnabled = driverFolder != null;
        }

        private async Task ReadBattery()
        {
            if (busy || readingBattery)
                return;
            readingBattery = true;
            RefreshBackupsEnabled();
            batteryStatus.Text = Lang.T("Создаём отчёт о батарее…");
            try
            {
                string reports = Path.Combine(Program.Data, "reports");
                Program.SafeDirectory(reports);
                Directory.CreateDirectory(reports);
                string html = Path.Combine(reports, "battery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html");
                var batteries = await batteryReader(html);
                batteryStatus.Text = Backups.Describe(batteries);
                batteryHtml = batteries.Length > 0 && File.Exists(html) ? html : null;
                batteryOpen.IsEnabled = batteryHtml != null;
            }
            catch (Exception ex)
            {
                batteryStatus.Text = Lang.T("Не удалось получить отчёт о батарее: ") + ex.Message;
            }
            finally
            {
                readingBattery = false;
                if (!closed)
                    RefreshBackupsEnabled();
            }
        }

        private async Task RunBackup(string action)
        {
            if (busy || runningBackup)
                return;
            if (action == "create" && !await Confirm(Lang.T("Создать точку восстановления Windows?\n\nПонадобится подтверждение администратора. Windows может отказаться создавать новую точку, если последняя создана меньше суток назад.")))
                return;
            if (action == "drivers" && !await Confirm(Lang.T("Сохранить копию установленных драйверов в папку WintoolsData\\drivers?\n\nКопия может занять сотни мегабайт. Понадобится подтверждение администратора.")))
                return;
            runningBackup = true;
            RefreshBackupsEnabled();
            backupStatus.Text = action == "drivers" ? Lang.T("Сохраняем драйверы… Это может занять несколько минут.") : action == "create" ? Lang.T("Создаём точку восстановления…") : Lang.T("Читаем точки восстановления…");
            try
            {
                var result = await backupRun(action);
                backupPoints.ItemsSource = result.Points;
                if (result.Folder != null)
                    driverFolder = result.Folder;
                string storage = result.MaxBytes > 0 ? Lang.T(" Занято под точки: ") + (result.UsedBytes / 1073741824.0).ToString("0.0", Lang.Culture) + Lang.T(" из ") + (result.MaxBytes / 1073741824.0).ToString("0.0", Lang.Culture) + Lang.T(" ГБ.") : "";
                backupStatus.Text = (result.Message == null ? "" : result.Message + " ") + (result.Points.Length == 0 ? Lang.T("Точек восстановления нет: возможно, защита системы выключена.") : Lang.T("Точек восстановления: ") + result.Points.Length + ".") + storage;
                Text("Status", backupStatus.Text);
            }
            catch (Exception ex)
            {
                backupStatus.Text = Lang.T("Не выполнено: ") + ex.Message;
            }
            finally
            {
                runningBackup = false;
                if (!closed)
                    RefreshBackupsEnabled();
            }
        }
    }
}
