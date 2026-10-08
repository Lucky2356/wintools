using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Wintools
{
    internal sealed class ServiceState : System.ComponentModel.INotifyPropertyChanged
    {
        private string name, label, state, mode;
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void Change(ref string field, string value)
        {
            if (field == value)
                return;
            field = value;
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new System.ComponentModel.PropertyChangedEventArgs(null));
        }

        public string Name
        {
            get
            {
                return name;
            }

            set
            {
                Change(ref name, value);
            }
        }

        public string Label
        {
            get
            {
                return label;
            }

            set
            {
                Change(ref label, value);
            }
        }

        public string State
        {
            get
            {
                return state;
            }

            set
            {
                Change(ref state, value);
            }
        }

        public string Mode
        {
            get
            {
                return mode;
            }

            set
            {
                Change(ref mode, value);
            }
        }

        public string RunningLabel
        {
            get
            {
                return State == "Running" ? Lang.T("Работает") : State == "Stopped" ? Lang.T("Остановлена") : State == "Paused" ? Lang.T("Приостановлена") : Lang.T("Переход: ") + State;
            }
        }

        public string StartLabel
        {
            get
            {
                return Mode == "Auto" ? Lang.T("Автоматический") : Mode == "Manual" ? Lang.T("Вручную / по запросу") : Mode == "Disabled" ? Lang.T("Отключён") : Mode;
            }
        }

        public string Title
        {
            get
            {
                return Label + " (" + Name + ")";
            }
        }

        public string Detail
        {
            get
            {
                return Lang.T("Сейчас: ") + (State == "Running" ? Lang.T("работает") : State == "Stopped" ? Lang.T("остановлена") : State == "Paused" ? Lang.T("приостановлена") : Lang.T("переходное состояние: ") + State) + Lang.T(" · Запуск: ") + (Mode == "Auto" ? Lang.T("автоматический") : Mode == "Manual" ? Lang.T("вручную / по запросу Windows") : Mode == "Disabled" ? Lang.T("отключён") : Mode);
            }
        }

        public override string ToString()
        {
            return Title + Environment.NewLine + Detail;
        }
    }

    internal sealed partial class MainWindow
    {
        private ServiceState[] services;
        private bool readingServices;
        private bool servicesPending;
        private int serviceEpoch;
        private TextBlock serviceStatus, healthStatus, healthResult, verificationStatus, verificationResult;
        private TextBox serviceSearch;
        private CheckBox runningOnly;
        private ListBox serviceList;
        private Button serviceRefresh, healthStart, verificationStart, verificationPlan;
        private string[] verificationDrift = new string[0];
        private ProgressBar healthProgress;
        private string priorMemory;
        private static void Card(Border card)
        {
            card.SetResourceReference(Border.BackgroundProperty, "Surface");
            card.SetResourceReference(Border.BorderBrushProperty, "Border");
            card.BorderThickness = new Thickness(1);
        }

        private static TextBlock Paragraph(string text)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        // A titled card for loose text and buttons on code-built pages; returns the panel to fill.
        private static StackPanel Section(Panel parent, string title)
        {
            var card = new Border
            {
                Padding = new Thickness(22, 20, 22, 6),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Card(card);
            var content = new StackPanel();
            var heading = Paragraph(title);
            heading.FontSize = 18;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 0, 0, 10);
            heading.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            content.Children.Add(heading);
            card.Child = content;
            parent.Children.Add(card);
            return content;
        }

        private Button ToolButton(Panel panel, string caption, Action action)
        {
            var button = new Button
            {
                Content = caption,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 14)
            };
            button.Click += (s, e) => action();
            panel.Children.Add(button);
            return button;
        }

        private StackPanel ToolPage(string name)
        {
            // Long lines are hard to read on wide screens, so tool pages keep a comfortable column width.
            var panel = new StackPanel
            {
                Margin = new Thickness(4),
                MaxWidth = 1080,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var scroll = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };
            Window.RegisterName(name, scroll);
            ((Grid)Get<FrameworkElement>("SettingsPage").Parent).Children.Add(scroll);
            return panel;
        }

        private void InitializeHealth()
        {
            InitializeServiceBrowser();
            var panel = ToolPage("HealthPage");
            InitializeDashboard(panel);
            InitializeHardware(panel);
            InitializeMonitor(panel);
            InitializeBoot(panel);
            InitializeBackups(panel);
            var outer = panel;
            panel = Section(outer, Lang.T("Снимок состояния ПК"));
            panel.Children.Add(Paragraph(Lang.T("Проверка читает загрузку процессора, доступную оперативную память, свободное место на дисках, список автозагрузки и самые крупные процессы в памяти. Настройки не меняются, файлы не удаляются. Это снимок текущего состояния, а не тест скорости или оценка FPS.")));
            healthStart = ToolButton(panel, Lang.T("Проверить состояние ПК"), async () => await ReadHealth());
            healthProgress = new ProgressBar
            {
                Height = 4,
                IsIndeterminate = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 14)
            };
            panel.Children.Add(healthProgress);
            healthStatus = Paragraph(Lang.T("Проверка ещё не выполнялась."));
            panel.Children.Add(healthStatus);
            healthResult = Paragraph("");
            panel.Children.Add(healthResult);
            ToolButton(panel, Lang.T("Что можно улучшить →"), () => ShowPage(8));
            panel = Section(ToolPage("VerificationPage"), Lang.T("Сверка с историей"));
            panel.Children.Add(Paragraph(Lang.T("Сверяем записи истории Wintools с текущими настройками Windows: реестром, типом запуска служб и другими поддерживаемыми параметрами. Так можно заметить, что обновление Windows или другая программа изменила настройку. Проверка ничего не исправляет и не оценивает скорость ПК. Повторные записи одного действия проверяются отдельно.")));
            verificationStart = ToolButton(panel, Lang.T("Сверить настройки с историей"), async () => await ReadVerification());
            verificationStatus = Paragraph(Lang.T("Совпадает — настройка соответствует каталогу. Изменилась — текущее значение отличается. Не проверено — для записи нет доступной проверки. Отменённые действия пропускаются."));
            panel.Children.Add(verificationStatus);
            verificationResult = Paragraph("");
            panel.Children.Add(verificationResult);
            verificationPlan = ToolButton(panel, Lang.T("Добавить изменившиеся в план"), AddDriftToPlan);
            verificationPlan.Visibility = Visibility.Collapsed;
            ToolButton(panel, Lang.T("Открыть историю и откат →"), () => ShowPage(1));
            panel = ToolPage("OptimizationPage");
            InitializePowerManagement(panel);
            InitializeWindowsUpdate(panel);
            var start = Section(panel, Lang.T("С чего начать"));
            start.Children.Add(Paragraph(Lang.T("Начните со снимка состояния ПК, изменяйте по одному пункту и повторяйте проверку при той же нагрузке. Эти инструменты открывают штатные настройки Windows; решение об изменении остаётся за вами.")));
            ToolButton(start, Lang.T("Снять показатели ПК →"), () => ShowPage(6));
            AddAdvice(panel, Lang.T("Ускорить вход в Windows"), Lang.T("В автозагрузке отключите приложения, которые не нужны сразу после входа. Сохраните защиту, драйверы и нужную синхронизацию. В Диспетчере задач можно посмотреть влияние приложения на запуск."), "ms-settings:startupapps");
            AddAdvice(panel, Lang.T("Освободить место на диске"), Lang.T("Просмотрите категории хранилища и настройте Контроль памяти. Перед очисткой проверьте корзину и загрузки: удаление файлов может быть необратимым."), "ms-settings:storagesense");
            AddAdvice(panel, Lang.T("Настроить визуальные эффекты"), Lang.T("На слабом ПК отключение анимации может сделать интерфейс отзывчивее. В окне параметров быстродействия можно сохранить сглаживание экранных шрифтов."), Path.Combine(Environment.SystemDirectory, "SystemPropertiesPerformance.exe"));
            AddAdvice(panel, Lang.T("Проверить питание"), Lang.T("Повышенная производительность расходует больше энергии и усиливает нагрев. На ноутбуке сравнивайте результат при подключённом питании."), "ms-settings:powersleep");
            AddAdvice(panel, Lang.T("Проверить обслуживание SSD и HDD"), Lang.T("Откройте «Оптимизация дисков» и проверьте расписание. Windows выбирает обслуживание по типу накопителя. Отключать эту службу ради ускорения не требуется."), Path.Combine(Environment.SystemDirectory, "dfrgui.exe"));
            AddAdvice(panel, Lang.T("Найти программу, создающую нагрузку"), Lang.T("Сортируйте процессы по ЦП, памяти или диску. Закрывайте только знакомые приложения с сохранёнными документами."), Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"));
            ToolButton(panel, Lang.T("Рекомендации Microsoft ↗"), () => OpenTool("https://support.microsoft.com/en-us/windows/experience/performance-optimization/tips-to-improve-pc-performance-in-windows"));
            ClickAsync("NavServices", async () =>
            {
                if (services == null)
                    await RefreshServices();
            });
            ClickAsync("RefreshCatalogueServices", async () =>
            {
                var settings = RefreshTweakStates();
                await RefreshServices();
                await settings;
            });
        }

        private void AddAdvice(StackPanel panel, string title, string description, string target)
        {
            var card = new Border
            {
                Padding = new Thickness(22, 20, 22, 6),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Card(card);
            var content = new StackPanel();
            card.Child = content;
            var heading = Paragraph(title);
            heading.FontSize = 17;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 0, 0, 8);
            content.Children.Add(heading);
            content.Children.Add(Paragraph(description));
            ToolButton(content, Lang.T("Открыть настройки ↗"), () => OpenTool(target));
            panel.Children.Add(card);
        }

        private void OpenTool(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Text("Status", Lang.T("Не удалось открыть инструмент Windows: ") + ex.Message);
            }
        }

        private async Task ReadHealth()
        {
            if (busy)
                return;
            SetBusy(true);
            healthStart.IsEnabled = false;
            healthProgress.Visibility = Visibility.Visible;
            healthResult.Text = "";
            healthStatus.Text = Lang.T("Проверяем процессор, память, диски и автозагрузку… Обычно это занимает несколько секунд.");
            try
            {
                var result = await Engine.Run("diagnose", "-", "-", false, false, value =>
                {
                });
                Get<TextBox>("Output").Text = result.Output;
                var match = Regex.Match(result.Output, @"(?m)^Report:\s*(.+?)\r?$");
                if (!match.Success)
                    throw new IOException(Lang.T("Отчёт не создан. Подробности доступны в выводе операции."));
                var path = Path.GetFullPath(match.Groups[1].Value.Trim());
                var root = Path.GetFullPath(Path.Combine(Program.Data, "reports")) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length > 2097152)
                    throw new IOException(Lang.T("Некорректный файл отчёта."));
                var report = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                healthResult.Text = DescribeHealth(report);
                healthStatus.Text = Lang.T("Снимок получен в ") + DateTime.Now.ToString("HH:mm:ss") + (result.Code == 0 ? Lang.T(". Проверка завершена; настройки не менялись.") : Lang.T(". Часть данных недоступна; это не означает неисправность ПК."));
            }
            catch (Exception ex)
            {
                healthStatus.Text = Lang.T("Проверка не завершена: ") + ex.Message;
            }
            finally
            {
                healthProgress.Visibility = Visibility.Collapsed;
                healthStart.IsEnabled = true;
                SetBusy(false);
                Text("Status", healthStatus.Text);
            }

            await PrepareAutomaticUpdate();
        }

        private string DescribeHealth(Dictionary<string, object> report)
        {
            var text = new StringBuilder();
            object value;
            var os = report.TryGetValue("os", out value) ? value as Dictionary<string, object> : null;
            if (os != null)
            {
                double total = Convert.ToDouble(os["totalMemoryMB"]), free = Convert.ToDouble(os["freeMemoryMB"]);
                string memory = Lang.T("Свободно памяти: ") + (free / 1024).ToString("F1") + Lang.T(" из ") + (total / 1024).ToString("F1") + Lang.T(" ГБ");
                text.AppendLine(memory);
                if (priorMemory != null)
                    text.AppendLine(Lang.T("Предыдущий снимок: ") + priorMemory);
                priorMemory = memory + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
                if (total > 0 && free / total < 0.15)
                    text.AppendLine(Lang.T("Мало свободной памяти. Посмотрите крупные процессы ниже и закройте ненужные приложения."));
            }
            else
                text.AppendLine(Lang.T("Память: данные недоступны."));
            foreach (var row in ReportRows(report, "cpu"))
                text.AppendLine(Lang.T("Процессор: ") + row["Name"] + Lang.T(" · Загрузка: ") + (row["LoadPercentage"] == null ? Lang.T("нет данных") : row["LoadPercentage"] + "%"));
            text.AppendLine(Lang.T("\nМЕСТО НА ДИСКАХ"));
            foreach (var row in ReportRows(report, "disks"))
            {
                double total = Convert.ToDouble(row["Size"]), free = Convert.ToDouble(row["FreeSpace"]);
                text.AppendLine(row["DeviceID"] + Lang.T(" · Свободно ") + (free / 1073741824).ToString("F1") + Lang.T(" из ") + (total / 1073741824).ToString("F1") + Lang.T(" ГБ") + (total > 0 && free / total < 0.1 ? Lang.T(" — мало места, проверьте хранилище") : ""));
            }

            text.AppendLine(Lang.T("\nБОЛЬШЕ ВСЕГО ПАМЯТИ СЕЙЧАС"));
            foreach (var row in ReportRows(report, "topMemoryProcesses"))
                text.AppendLine(row["ProcessName"] + " · " + row["workingSetMB"] + Lang.T(" МБ"));
            text.AppendLine(Lang.T("\nЗаписей автозагрузки найдено: ") + ReportRows(report, "startupEntries").Count() + Lang.T(". Это найденные записи, а не количество включённых приложений. Их состояние смотрите в настройках автозагрузки."));
            if (report.TryGetValue("errors", out value) && value is System.Collections.IEnumerable)
                foreach (var error in (System.Collections.IEnumerable)value)
                    text.AppendLine(Lang.T("Недоступные данные: ") + error);
            return text.ToString();
        }

        private static IEnumerable<Dictionary<string, object>> ReportRows(Dictionary<string, object> report, string key)
        {
            object value;
            if (!report.TryGetValue(key, out value) || !(value is System.Collections.IEnumerable))
                yield break;
            foreach (var row in (System.Collections.IEnumerable)value)
            {
                var dict = row as Dictionary<string, object>;
                if (dict != null)
                    yield return dict;
            }
        }

        private async Task HealthSmoke()
        {
            var serviceTweak = catalogue.First(t => t.Kind == "SVC");
            services = new[]
            {
                new ServiceState
                {
                    Name = serviceTweak.Target,
                    Label = Lang.T("Тест"),
                    State = "Running",
                    Mode = "Manual"
                }
            };
            ChooseCollection(new[] { serviceTweak.Id });
            var card = (ActionRow)Get<ListBox>("Items").Items[0];
            Assert(card.ServiceStatus.Contains("работает") && card.ServiceStatus.Contains("вручную"), "Catalogue service card hides current state");
            Window.UpdateLayout();
            Capture("portable-ui-service-cards.png");
            services[0].State = "Stopped";
            ShowPage(5);
            ShowPage(0);
            Assert(((ActionRow)Get<ListBox>("Items").Items[0]).ServiceStatus.Contains("остановлена"), "Returning to catalogue kept stale service state");
            services = new ServiceState[0];
            Filter();
            Assert(((ActionRow)Get<ListBox>("Items").Items[0]).ServiceStatus.Contains("Не установлена"), "Absent service confused with disabled service");
            services = null;
            Filter();
            Assert(((ActionRow)Get<ListBox>("Items").Items[0]).ServiceStatus.Contains("неизвестно"), "Unknown service state confused with disabled");
            Get<Button>("ClearCollection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            services = new[]
            {
                new ServiceState
                {
                    Name = "manual-running",
                    Label = Lang.T("Служба по запросу"),
                    State = "Running",
                    Mode = "Manual"
                },
                new ServiceState
                {
                    Name = "auto-stopped",
                    Label = Lang.T("Автоматическая служба"),
                    State = "Stopped",
                    Mode = "Auto"
                }
            };
            FilterServices();
            Assert(serviceList.Items.Count == 2, "Service snapshot missing rows");
            runningOnly.IsChecked = true;
            FilterServices();
            Assert(serviceList.Items.Count == 1 && ((ServiceState)serviceList.Items[0]).Mode == "Manual", "Running and automatic start conflated");
            runningOnly.IsChecked = false;
            serviceSearch.Text = "auto-stopped";
            Assert(serviceList.Items.Count == 1, "Service name search failed");
            serviceSearch.Clear();
            var fixture = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"os\":{\"totalMemoryMB\":8192,\"freeMemoryMB\":512},\"cpu\":[{\"Name\":\"Test CPU\",\"LoadPercentage\":24}],\"disks\":[{\"DeviceID\":\"C:\",\"Size\":107374182400,\"FreeSpace\":1073741824}],\"topMemoryProcesses\":[{\"ProcessName\":\"Example\",\"workingSetMB\":200}],\"startupEntries\":[],\"errors\":[]}");
            healthResult.Text = DescribeHealth(fixture);
            Assert(healthResult.Text.Contains("Мало свободной памяти") && healthResult.Text.Contains("мало места") && healthResult.Text.Contains("24%"), "Diagnostic advice does not reflect metrics");
            Assert(DescribeHealth(fixture).Contains("Предыдущий снимок"), "Diagnostic comparison missing");
            healthStatus.Text = Lang.T("Тестовый снимок для проверки интерфейса");
            serviceStatus.Text = Lang.T("Тестовые службы: работа и способ запуска показаны отдельно");
            foreach (var size in new[]
            {
                new Size(800, 600),
                new Size(1024, 768),
                new Size(1366, 768)
            }

            )
            {
                Window.Width = size.Width;
                Window.Height = size.Height;
                foreach (int index in new[]
                {
                    5,
                    6,
                    7,
                    8
                }

                )
                {
                    ShowPage(index);
                    Window.UpdateLayout();
                    await Task.Delay(50);
                    Assert(Get<FrameworkElement>(pages[index]).ActualHeight > 150, "Health page has no usable viewport");
                    if (size.Width == 800)
                        Capture("portable-ui-health-" + index + ".png");
                }
            }

            services = null;
            FilterServices();
            priorMemory = null;
            await ReadHealth();
            Assert(healthResult.Text.Length > 0, "Actual diagnostic report was not rendered: " + healthStatus.Text);
            await RefreshServices();
            ShowPage(0);
        }

        private async Task ReadVerification()
        {
            if (busy)
                return;
            SetBusy(true);
            verificationStart.IsEnabled = false;
            verificationResult.Text = "";
            verificationStatus.Text = Lang.T("Сверяем записи истории с Windows… Ничего не изменяем.");
            try
            {
                var result = await Engine.Run("verify", "-", "-", false, false, value =>
                {
                });
                Get<TextBox>("Output").Text = result.Output;
                var rows = Regex.Matches(result.Output, @"(?m)^VERIFY ([A-Z0-9-]+) (MATCH|DRIFT|UNSUPPORTED|ERROR): (.*)$");
                var text = new StringBuilder();
                var drift = new List<string>();
                int matched = 0, changed = 0, unknown = 0;
                foreach (Match row in rows)
                {
                    var item = catalogue.FirstOrDefault(t => t.Id == row.Groups[1].Value);
                    var status = row.Groups[2].Value;
                    string label;
                    if (status == "MATCH")
                    {
                        matched++;
                        label = Lang.T("Совпадает");
                    }
                    else if (status == "DRIFT")
                    {
                        changed++;
                        label = Lang.T("Изменилась");
                        if (item != null && !drift.Contains(item.Id))
                            drift.Add(item.Id);
                    }
                    else
                    {
                        unknown++;
                        label = Lang.T("Не проверено");
                    }

                    text.AppendLine(label + " · " + (item == null ? row.Groups[1].Value : item.Title));
                }

                verificationDrift = drift.ToArray();
                verificationPlan.Visibility = drift.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                verificationResult.Text = text.ToString();
                verificationStatus.Text = rows.Count == 0 ? (result.Code == 0 ? Lang.T("Нет активных записей для проверки. Сначала примените действие из каталога.") : Lang.T("Проверка не завершена. Откройте вывод операции для подробностей.")) : Lang.T("Совпадает: ") + matched + Lang.T(" · Изменилось: ") + changed + Lang.T(" · Не проверено: ") + unknown + Lang.T(". Ничего не исправлялось автоматически.");
            }
            catch (Exception ex)
            {
                verificationStatus.Text = Lang.T("Не удалось выполнить проверку: ") + ex.Message;
            }
            finally
            {
                verificationStart.IsEnabled = true;
                SetBusy(false);
                Text("Status", verificationStatus.Text);
            }

            await PrepareAutomaticUpdate();
        }
    }
}
