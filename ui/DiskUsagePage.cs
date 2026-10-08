using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private ComboBox usageRoot;
        private ListBox usageList;
        private TextBlock usageStatus;
        private Button usageScan, usageOpen, usageStop;
        private CancellationTokenSource usageCancel;
        private Func<string, CancellationToken, UsageReport> usageMeasure = (root, token) => DiskUsage.Measure(root, token, 400000);
        private static string[] UsageRoots()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                profile,
                Path.Combine(profile, "Downloads"),
                Path.GetPathRoot(Environment.SystemDirectory)
            };
        }

        private void InitializeDiskUsage(StackPanel panel)
        {
            var card = new StackPanel();
            var border = new Border
            {
                Child = card,
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 10),
                CornerRadius = new CornerRadius(8)
            };
            Card(border);
            panel.Children.Add(border);
            var heading = Paragraph(Lang.T("Что занимает место"));
            heading.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
            card.Children.Add(heading);
            card.Children.Add(Intro(Lang.T("Что занимает больше всего места в выбранной папке."), Lang.T("Показывает самые крупные папки внутри выбранной. Только чтение: удалять найденное нужно вручную и осознанно. Ссылки и junction не учитываются, системные папки без прав доступа считаются частично.")));
            var controls = new WrapPanel();
            card.Children.Add(controls);
            usageRoot = new ComboBox
            {
                Width = 220,
                Margin = new Thickness(0, 0, 10, 8),
                ItemsSource = new[]
                {
                    Lang.T("Папка пользователя"),
                    Lang.T("Загрузки"),
                    Lang.T("Системный диск")
                },
                SelectedIndex = 0
            };
            System.Windows.Automation.AutomationProperties.SetName(usageRoot, Lang.T("Где искать крупные папки"));
            controls.Children.Add(usageRoot);
            usageScan = new Button
            {
                Content = Lang.T("Показать крупные папки"),
                Margin = new Thickness(0, 0, 10, 8)
            };
            usageScan.Click += async (s, e) => await MeasureUsage();
            controls.Children.Add(usageScan);
            usageStop = new Button
            {
                Content = Lang.T("Остановить"),
                Margin = new Thickness(0, 0, 10, 8),
                Visibility = Visibility.Collapsed
            };
            usageStop.Click += (s, e) =>
            {
                if (usageCancel != null)
                    usageCancel.Cancel();
            };
            controls.Children.Add(usageStop);
            usageOpen = new Button
            {
                Content = Lang.T("Открыть выбранную папку"),
                Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = false
            };
            usageOpen.Click += (s, e) =>
            {
                var row = usageList.SelectedItem as FolderUsage;
                if (row != null && Directory.Exists(row.Path))
                    OpenTool(row.Path);
            };
            controls.Children.Add(usageOpen);
            usageStatus = Paragraph("");
            card.Children.Add(usageStatus);
            usageList = new ListBox
            {
                MaxHeight = 360
            };
            System.Windows.Automation.AutomationProperties.SetName(usageList, Lang.T("Крупные папки"));
            usageList.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel><TextBlock Text='{Binding Name}' FontWeight='SemiBold'/><TextBlock Text='{Binding Detail}' Foreground='{DynamicResource Muted}' FontSize='12'/></StackPanel></DataTemplate>");
            usageList.SelectionChanged += (s, e) => usageOpen.IsEnabled = usageList.SelectedItem != null;
            card.Children.Add(usageList);
        }

        private async Task MeasureUsage()
        {
            if (usageCancel != null)
                return;
            string root = UsageRoots()[Math.Max(0, usageRoot.SelectedIndex)];
            usageCancel = new CancellationTokenSource();
            usageScan.IsEnabled = false;
            usageStop.Visibility = Visibility.Visible;
            usageList.ItemsSource = null;
            usageStatus.Text = Lang.T("Считаем размеры в ") + root + Lang.T("… Это может занять минуту.");
            try
            {
                var measure = usageMeasure;
                var token = usageCancel.Token;
                var report = await Task.Run(() => measure(root, token));
                usageList.ItemsSource = report.Folders;
                string free = "";
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(root));
                    free = Lang.T(" Свободно на диске ") + drive.Name.TrimEnd('\\') + ": " + DiskUsage.Size(drive.AvailableFreeSpace) + Lang.T(" из ") + DiskUsage.Size(drive.TotalSize) + ".";
                }
                catch (Exception)
                {
                }

                usageStatus.Text = root + ": " + DiskUsage.Size(report.Bytes) + Lang.T(" в показанных папках.") + free + (report.Partial ? Lang.T(" Часть объектов недоступна или их слишком много — итог неполный.") : "");
            }
            catch (OperationCanceledException)
            {
                usageStatus.Text = Lang.T("Расчёт остановлен.");
            }
            catch (Exception ex)
            {
                usageStatus.Text = Lang.T("Не удалось посчитать: ") + ex.Message;
            }
            finally
            {
                usageCancel.Dispose();
                usageCancel = null;
                usageScan.IsEnabled = true;
                usageStop.Visibility = Visibility.Collapsed;
            }
        }
    }
}
