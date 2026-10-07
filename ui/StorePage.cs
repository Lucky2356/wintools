using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private ComboBox applicationKind, applicationLaunchEntry;
        private Button applicationLaunch, applicationStoreMore;
        private MenuItem applicationReset, applicationRegister, applicationStoreRemove, applicationStoreFolder;
        private string storeInventoryError = "";
        private Func<Task<StoreInventory>> storeRead = StorePackages.Read;
        private Func<StorePackage, string, Task<EngineResult>> storeRun = StoreActions.Run;
        private Action<string> storeLaunch = id => Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"shell:AppsFolder\\" + id + "\"") { UseShellExecute = false });
        private void InitializeStoreActions(StackPanel footer)
        {
            var launchRow = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            footer.Children.Insert(1, launchRow);
            applicationLaunchEntry = new ComboBox
            {
                Width = 240,
                DisplayMemberPath = "Name",
                Margin = new Thickness(0, 0, 10, 0)
            };
            System.Windows.Automation.AutomationProperties.SetName(applicationLaunchEntry, Lang.T("Ярлык или приложение для запуска"));
            launchRow.Children.Add(applicationLaunchEntry);
            applicationLaunchEntry.SelectionChanged += (s, e) => DesktopLaunchTooltip();
            applicationLaunch = new Button
            {
                Content = Lang.T("Запустить"),
                IsEnabled = false
            };
            launchRow.Children.Add(applicationLaunch);
            applicationLaunch.Click += async (s, e) =>
            {
                var selected = applicationList.SelectedItem as InstalledApplication;
                if (selected != null && selected.Package == null)
                    await LaunchDesktopApplication();
                else
                    await LaunchStoreApplication();
            };
            var menu = new ContextMenu
            {
                Style = (Style)Window.FindResource("FlatMenu")
            };
            menu.Resources.MergedDictionaries.Add(Window.Resources);
            applicationStoreMore = new Button
            {
                Content = Lang.T("Действия приложения"),
                ContextMenu = menu,
                Margin = new Thickness(10, 0, 0, 0)
            };
            launchRow.Children.Add(applicationStoreMore);
            applicationStoreMore.Click += (s, e) =>
            {
                menu.PlacementTarget = applicationStoreMore;
                menu.IsOpen = true;
            };
            applicationStoreFolder = new MenuItem
            {
                Header = Lang.T("Открыть папку программы")
            };
            menu.Items.Add(applicationStoreFolder);
            applicationStoreFolder.Click += (s, e) => applicationFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            applicationStoreRemove = new MenuItem
            {
                Header = Lang.T("Удалить для текущего пользователя…")
            };
            menu.Items.Add(applicationStoreRemove);
            applicationStoreRemove.Click += async (s, e) => await RemoveApplication();
            applicationRegister = new MenuItem
            {
                Header = Lang.T("Исправить регистрацию…")
            };
            menu.Items.Add(applicationRegister);
            applicationRegister.Click += async (s, e) => await ChangeStoreApplication("register");
            applicationReset = new MenuItem
            {
                Header = Lang.T("Сбросить данные…")
            };
            menu.Items.Add(applicationReset);
            applicationReset.Click += async (s, e) => await ChangeStoreApplication("reset");
        }

        private void StoreSelection(InstalledApplication row)
        {
            if (applicationLaunch == null)
                return;
            var package = row == null ? null : row.Package;
            if (package != null)
            {
                var priorEntry = applicationLaunchEntry.SelectedItem as AppEntry;
                applicationLaunchEntry.ItemsSource = package == null ? new AppEntry[0] : package.Entries;
                applicationLaunchEntry.SelectedItem = priorEntry == null ? null : applicationLaunchEntry.Items.Cast<AppEntry>().FirstOrDefault(e => e.Id == priorEntry.Id);
                if (applicationLaunchEntry.SelectedIndex < 0)
                    applicationLaunchEntry.SelectedIndex = applicationLaunchEntry.Items.Count > 0 ? 0 : -1;
            }

            applicationLaunchEntry.Visibility = applicationLaunch.Visibility = package == null ? Visibility.Collapsed : Visibility.Visible;
            applicationLaunch.IsEnabled = !busy && !readingApplications && package != null && package.Entries.Length > 0;
            applicationRegister.IsEnabled = !busy && !readingApplications && package != null && !package.Protected && !string.IsNullOrEmpty(package.Location);
            applicationReset.IsEnabled = !busy && !readingApplications && package != null && !package.Protected && package.ResetSupported;
            applicationStoreMore.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
            applicationStoreMore.IsEnabled = !busy && !readingApplications;
            applicationFolder.Visibility = applicationRemove.Visibility = Visibility.Collapsed;
            applicationStoreRemove.IsEnabled = package == null ? applicationRemove.IsEnabled : !busy && !readingApplications && !package.Protected;
            applicationStoreRemove.Header = package == null ? Lang.T("Удалить программу…") : Lang.T("Удалить для текущего пользователя…");
            applicationRegister.Visibility = applicationReset.Visibility = package == null ? Visibility.Collapsed : Visibility.Visible;
            applicationStoreFolder.IsEnabled = applicationFolder.IsEnabled;
            if (package == null)
                DesktopSelection(row);
            else
                applicationLaunchEntry.IsEnabled = !busy && !readingApplications;
            if (package != null)
            {
                applicationRemove.IsEnabled = !busy && !readingApplications && !package.Protected;
                applicationDetail.Text = package.Name + Lang.T(" · Store / MSIX · Текущий пользователь\n") + (package.Protected ? Lang.T("Системный или защищённый пакет. Изменение недоступно.") : Lang.T("Откат удаления, сброса и исправления регистрации недоступен.")) + (!package.ResetSupported ? Lang.T(" Сброс этим способом не поддерживается в этой Windows.") : "");
            }
        }

        private async Task LaunchStoreApplication()
        {
            var row = applicationList.SelectedItem as InstalledApplication;
            var entry = applicationLaunchEntry.SelectedItem as AppEntry;
            if (busy || row == null || row.Package == null || entry == null)
                return;
            SetBusy(true);
            try
            {
                var current = (await storeRead()).Rows.FirstOrDefault(p => p.FullName == row.Package.FullName);
                if (current == null || !current.Entries.Any(e => e.Id == entry.Id) || !StorePackages.EntryValid(current, entry))
                    throw new IOException(Lang.T("Команда запуска изменилась. Обновите список."));
                storeLaunch(entry.Id);
                applicationStatus.Text = Lang.T("Запрос запуска «") + entry.Name + Lang.T("» передан Windows.");
            }
            catch (Exception ex)
            {
                applicationStatus.Text = Lang.T("Не удалось передать запрос запуска: ") + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task ChangeStoreApplication(string action)
        {
            var row = applicationList.SelectedItem as InstalledApplication;
            if (busy || row == null || row.Package == null)
                return;
            var package = row.Package;
            try
            {
                StorePackages.Validate(package, action);
                string details = action == "reset" ? Lang.T("Будут удалены локальные настройки и данные этого приложения. Может потребоваться повторный вход. Сохраните нужные данные до сброса.") : action == "register" ? Lang.T("Повторно зарегистрируем пакет из установленных файлов. Это может помочь при проблемах запуска, но не скачивает недостающие файлы и не проверяет их целостность. Сначала закройте приложение.") : Lang.T("Приложение будет удалено для текущего пользователя. Его локальные данные могут быть потеряны.");
                if (!await Confirm(StoreActions.Title(action) + ": «" + package.Name + "»?\n\n" + details + Lang.T("\n\nАвтоматический откат Wintools для этой операции недоступен.")))
                    return;
                SetBusy(true);
                applicationStatus.Text = Lang.T("Windows выполняет операцию… Дождитесь результата.");
                var result = await storeRun(package, action);
                applicationStatus.Text = result.Output;
                Get<TextBox>("Output").Text = result.Output;
                Text("Status", result.Code == 0 ? Lang.T("Операция приложения завершена. Обновите список и проверьте приложение.") : Lang.T("Операция приложения не подтверждена. Подробности в выводе."));
                ReadHistory();
            }
            catch (Exception ex)
            {
                applicationStatus.Text = Lang.T("Операция не завершена: ") + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private static HistoryRow[] StoreHistoryRows()
        {
            return StoreActions.History().Select(r => new HistoryRow { Run = r.Id, StoreChange = true, Title = StoreActions.Title(r.Action) + " · " + r.Name, TimeUtc = DateTime.Parse(r.TimeUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind), Status = r.Status == "OK" ? Lang.T("Команда выполнена · без отката") : r.Status == "PENDING" ? Lang.T("Итог неизвестен · проверьте приложение") : Lang.T("Не подтверждено · ") + r.Summary, CanRevert = false }).ToArray();
        }

        private void ShowStoreReport(string id)
        {
            try
            {
                var record = StoreActions.Read(id);
                Get<TextBox>("Output").Text = StoreActions.Title(record.Action) + " · " + record.Name + "\n" + record.Package + "\n\n" + record.Summary + Lang.T("\n\nАвтоматический откат недоступен.");
                ExpandOutput(true);
            }
            catch (IOException ex)
            {
                Text("HistoryStatus", ex.Message);
            }
        }
    }
}
