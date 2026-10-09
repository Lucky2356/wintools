using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Wintools
{
    internal sealed class CollectionSection
    {
        internal string Title, Detail;
        internal string[] Ids;
        internal bool Default;
    }

    internal sealed class CollectionChoice
    {
        internal string Title;
        internal CollectionSection[] Sections;
        internal List<CheckBox> Checks = new List<CheckBox>();
        internal TextBlock Count;
        internal Button Add;
    }

    internal sealed partial class MainWindow
    {
        private CardFlow collectionCards;
        private CollectionChoice[] collectionChoices;
        private readonly List<Button> collectionPlanButtons = new List<Button>();
        private static CollectionSection Section(string title, string detail, bool selected, params string[] ids)
        {
            return new CollectionSection
            {
                Title = title,
                Detail = detail,
                Default = selected,
                Ids = ids
            };
        }

        private void InitializeCollections()
        {
            collectionChoices = new[]
            {
                new CollectionChoice
                {
                    Title = Lang.T("Меньше рекламы и советов"),
                    Sections = new[]
                    {
                        Section(Lang.T("Реклама и рекомендации"), Lang.T("Убирает предложения Windows, рекламный идентификатор и баннеры Проводника."), true, "PRIV-CDM-SYSPANE", "PRIV-CDM-SILENTAPPS", "PRIV-CDM-PREINSTALL", "PRIV-CDM-OEMPREINSTALL", "PRIV-CDM-338388", "PRIV-CDM-338389", "PRIV-CDM-353694", "PRIV-CDM-353696", "PRIV-CONSUMER", "PRIV-SOFTLANDING", "PRIV-TAILORED", "PRIV-ADVID-USER", "PRIV-ADVID-POLICY", "UI-SYNC-NOTIFY", "UI-SPOTLIGHT-OVERLAY", "UI-SPOTLIGHT-338387", "PRIV-CDM-338393", "PRIV-CDM-310093", "PRIV-CDM-SCOOBE", "UI-START-RECOMMEND", "UI-START-ACCOUNT"),
                        Section(Lang.T("Обратная связь и персонализация"), Lang.T("Сокращает запросы отзывов и обучение вводу. Персонализация текста и рукописного ввода станет ограниченнее."), true, "PRIV-FEEDBACK-NOTIF", "PRIV-SIUF", "PRIV-INK-TEXT", "PRIV-INK-INK"),
                        Section(Lang.T("История и облачный поиск"), Lang.T("Отключает хранение истории поиска и поиск по облачным аккаунтам; облачные результаты и обмен историей между устройствами будут недоступны."), false, "PRIV-ACTIVITY-FEED", "PRIV-ACTIVITY-PUB", "PRIV-ACTIVITY-UPL", "PRIV-CLOUD-SEARCH-MSA", "PRIV-CLOUD-SEARCH-AAD", "PRIV-SEARCH-HISTORY", "UI-BING-POLICY", "UI-BING-USER"),
                        Section(Lang.T("Диагностические службы и задачи"), Lang.T("Ограничивает отправку диагностики и сбор сведений о совместимости. Не выбирайте без согласования на управляемом рабочем ПК."), false, "PRIV-TELEMETRY", "PRIV-SVC-DIAGTRACK", "PRIV-SVC-DMWAPP", "PRIV-TASK-APPRAISER", "PRIV-TASK-APPRAISER-EXP", "PRIV-TASK-PROGDATA", "PRIV-TASK-CEIP-CONS", "PRIV-TASK-CEIP-USB", "PRIV-TASK-DMCLIENT", "PRIV-TASK-DMCLIENTSCEN"),
                        Section(Lang.T("Не использую Recall и Copilot"), Lang.T("Добавляет доступные политики отключения. Поддержка зависит от версии и редакции Windows."), false, "PRIV-RECALL-OFF", "PRIV-COPILOT-OFF")
                    }
                },
                new CollectionChoice
                {
                    Title = Lang.T("Удобный Проводник"),
                    Sections = new[]
                    {
                        Section(Lang.T("Файлы и навигация"), Lang.T("Показывает расширения файлов, открывает список дисков и делает строки компактнее."), true, "UI-FILEEXT", "UI-LAUNCHTO", "UI-COMPACT-VIEW"),
                        Section(Lang.T("Чистая история и меньше предложений"), Lang.T("Убирает недавние файлы, частые папки, историю документов и уведомления с предложениями. Недавние документы исчезнут также из списков переходов."), true, "UI-NO-RECENT", "UI-NO-FREQUENT", "UI-NO-TRACKDOCS", "UI-SYNC-NOTIFY"),
                        Section(Lang.T("Скрытые файлы и классическое меню"), Lang.T("Показывает скрытые файлы и возвращает классическое контекстное меню, где это поддерживается."), false, "UI-HIDDEN", "UI-CLASSIC-CONTEXT"),
                        Section(Lang.T("Оформление и быстрый доступ"), Lang.T("Тёмное оформление Windows, более быстрое меню, компактный поиск и завершение зависшей программы с панели задач."), false, "UI-DARK-APPS", "UI-DARK-SYSTEM", "UI-MENUDELAY", "UI-SEARCHBOX", "UI-END-TASK", "UI-TASKBAR-LEFT", "UI-TASKVIEW-BUTTON", "UI-COPILOT-BUTTON", "UI-HIDE-GALLERY"),
                        Section(Lang.T("Не использую сетевые медиатеки и поиск устройств"), Lang.T("Отключает 5 служб обнаружения и обмена медиатекой. Сетевые устройства, общий доступ к библиотеке и обнаружение компьютеров могут перестать работать."), false, "SVC-WMPNETWORKSVC", "SVC-SSDPSRV", "SVC-UPNPHOST", "SVC-FDPHOST", "SVC-FDRESPUB"),
                        Section(Lang.T("Не использую печать и сканирование"), Lang.T("Отключает 6 служб. Перестанут работать печать, включая PDF, и часть сканеров. Оставьте выключенным, если эти функции нужны."), false, "SVC-SPOOLER", "SVC-PRINTNOTIFY", "SVC-PRINTSCANBROKERSERVICE", "SVC-PRINTDEVICECONFIGURATIONSERVICE", "SVC-STISVC", "SVC-WIARPC")
                    }
                },
                new CollectionChoice
                {
                    Title = Lang.T("Игры без фоновой записи"),
                    Sections = new[]
                    {
                        Section(Lang.T("Запись игр и фоновый Edge"), Lang.T("Отключает встроенную запись игр, предварительный запуск и работу Edge после закрытия."), true, "PERF-GAMEDVR-USER", "PERF-GAMEDVR-POLICY", "EDGE-STARTUP-BOOST", "EDGE-BACKGROUND-OFF"),
                        Section(Lang.T("Меньше отвлекающего интерфейса"), Lang.T("Убирает виджеты, ленту новостей, анимацию панели задач и предложения Windows. Доступность зависит от версии ОС."), true, "UI-WIDGETS", "UI-FEEDS", "UI-CHATICON", "UI-TASKBARANIM", "PRIV-CDM-SILENTAPPS", "PRIV-CDM-SYSPANE", "PRIV-SOFTLANDING", "PRIV-TAILORED"),
                        Section(Lang.T("Не использую дополнительные функции Windows"), Lang.T("Отключает 8 служб: демонстрацию магазина, офлайн-карты, факс, медиатеку, кошелёк/NFC, Insider и удалённую установку приложений."), false, "SVC-RETAILDEMO", "SVC-MAPSBROKER", "SVC-FAX", "SVC-WMPNETWORKSVC", "SVC-WALLETSERVICE", "SVC-SEMGRSVC", "SVC-WISVC", "SVC-PUSHTOINSTALL"),
                        Section(Lang.T("Не использую Xbox и Game Pass"), Lang.T("Отключает 6 служб. Может нарушить запуск игр Game Pass, вход Xbox, облачные сохранения, сетевые игры и работу аксессуаров Xbox."), false, "SVC-XBL-AUTH", "SVC-XBL-SAVE", "SVC-XBL-NETAPI", "SVC-XBOX-GIP", "SVC-GAMINGSERVICES", "SVC-GAMINGSERVICESNET"),
                        Section(Lang.T("Не использую Bluetooth"), Lang.T("Отключает 3 службы. Беспроводные наушники, мышь и контроллеры Bluetooth могут перестать работать."), false, "SVC-BTHSERV", "SVC-BTHAVCTPSVC", "SVC-BTAGSERVICE"),
                        Section(Lang.T("Не принимаю удалённые подключения"), Lang.T("Отключает 3 службы удалённого рабочего стола. Удалённо подключиться к этому ПК через RDP будет нельзя."), false, "SVC-SESSIONENV", "SVC-TERMSERVICE", "SVC-UMRDPSERVICE"),
                        Section(Lang.T("Не использую печать и сканирование"), Lang.T("Отключает 6 служб печати и сканирования, включая печать в PDF."), false, "SVC-SPOOLER", "SVC-PRINTNOTIFY", "SVC-PRINTSCANBROKERSERVICE", "SVC-PRINTDEVICECONFIGURATIONSERVICE", "SVC-STISVC", "SVC-WIARPC")
                    }
                },
                new CollectionChoice
                {
                    Title = Lang.T("Слабый компьютер"),
                    Sections = new[]
                    {
                        Section(Lang.T("Меньше фоновой работы"), Lang.T("Отключает виджеты, ленту новостей, предварительный запуск и фоновую работу Edge, а также тихую установку предложенных приложений."), true, "UI-WIDGETS", "UI-FEEDS", "EDGE-STARTUP-BOOST", "EDGE-BACKGROUND-OFF", "PRIV-CDM-SILENTAPPS", "PRIV-CDM-PREINSTALL"),
                        Section(Lang.T("Отзывчивее интерфейс"), Lang.T("Меню открываются без задержки, панель задач работает без анимации, программы автозагрузки стартуют без дополнительной паузы."), true, "UI-MENUDELAY", "UI-TASKBARANIM", "UI-STARTUPDELAY"),
                        Section(Lang.T("Обновления только из локальной сети"), Lang.T("Компьютер не раздаёт обновления Windows другим компьютерам в интернете: меньше нагрузки на сеть и диск."), true, "UPD-DO-LAN"),
                        Section(Lang.T("Не ищу по содержимому файлов"), Lang.T("Отключает индексирование Windows Search. Поиск в Пуске и Проводнике станет медленнее и не найдёт текст внутри документов."), false, "PERF-WSEARCH"),
                        Section(Lang.T("Не использую дополнительные функции Windows"), Lang.T("Отключает 8 служб: демонстрацию магазина, офлайн-карты, факс, медиатеку, кошелёк/NFC, Insider и удалённую установку приложений."), false, "SVC-RETAILDEMO", "SVC-MAPSBROKER", "SVC-FAX", "SVC-WMPNETWORKSVC", "SVC-WALLETSERVICE", "SVC-SEMGRSVC", "SVC-WISVC", "SVC-PUSHTOINSTALL")
                    }
                },
                new CollectionChoice
                {
                    Title = Lang.T("Ноутбук: меньше фоновой работы"),
                    Sections = new[]
                    {
                        Section(Lang.T("Edge не работает в фоне"), Lang.T("Edge не запускается заранее после входа и не остаётся работать после закрытия окна."), true, "EDGE-STARTUP-BOOST", "EDGE-BACKGROUND-OFF"),
                        Section(Lang.T("Меньше уведомлений и предложений"), Lang.T("Убирает виджеты, ленту новостей, всплывающие советы и предложения Windows."), true, "UI-WIDGETS", "UI-FEEDS", "PRIV-SOFTLANDING", "PRIV-CDM-SYSPANE", "PRIV-CDM-SILENTAPPS"),
                        Section(Lang.T("Без перезагрузки во время работы"), Lang.T("Windows не перезагружает ноутбук после обновлений, пока вы вошли в систему, и не раздаёт обновления через интернет."), true, "UPD-NOREBOOT", "UPD-DO-LAN"),
                        Section(Lang.T("Не использую Bluetooth"), Lang.T("Отключает 3 службы. Беспроводные наушники, мышь и контроллеры Bluetooth могут перестать работать."), false, "SVC-BTHSERV", "SVC-BTHAVCTPSVC", "SVC-BTAGSERVICE"),
                        Section(Lang.T("Не принимаю удалённые подключения"), Lang.T("Отключает 3 службы удалённого рабочего стола. Удалённо подключиться к этому ПК через RDP будет нельзя."), false, "SVC-SESSIONENV", "SVC-TERMSERVICE", "SVC-UMRDPSERVICE")
                    }
                }
            };
            var root = new StackPanel();
            root.Children.Add(Intro(Lang.T("Выберите готовую подборку и снимите отметки с того, чем пользуетесь."), Lang.T("Настройте одну из пяти подборок под себя. Отмечайте дополнительные группы только для функций, которыми не пользуетесь. Выбор ничего не меняет в Windows: сначала посмотрите действия или добавьте их в план.")));
            InitializeCollectionAssistant(root);
            // Collections side by side on a wide screen; every card keeps its buttons on the bottom line.
            collectionCards = new CardFlow(380, 3, true);
            root.Children.Add(collectionCards);
            Get<ScrollViewer>("CollectionsPage").Content = root;
            for (int i = 0; i < collectionChoices.Length; i++)
            {
                var choice = collectionChoices[i];
                var frame = new DockPanel();
                var content = new StackPanel();
                var title = Paragraph(choice.Title);
                title.SetResourceReference(FrameworkElement.StyleProperty, "CardTitle");
                title.FontSize = 20;
                title.Margin = new Thickness(0, 0, 0, 4);
                content.Children.Add(title);
                choice.Count = Paragraph("");
                choice.Count.FontSize = 13;
                choice.Count.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                content.Children.Add(choice.Count);
                foreach (var section in choice.Sections)
                {
                    if (section.Ids.Any(id => !catalogue.Any(t => t.Id == id)))
                        throw new InvalidOperationException("Unknown collection action");
                    int available = CollectionAvailableIds(section.Ids, Environment.OSVersion.Version.Build).Length;
                    var check = new CheckBox
                    {
                        Content = new TextBlock
                        {
                            Text = section.Title + " · " + available + (available < section.Ids.Length ? Lang.T(" из ") + section.Ids.Length : ""),
                            TextWrapping = TextWrapping.Wrap
                        },
                        IsChecked = section.Default && available > 0,
                        IsEnabled = available > 0,
                        ToolTip = available < section.Ids.Length ? Lang.T("Действия для другой версии Windows исключены из выбора.") : null,
                        Margin = new Thickness(0, 8, 0, 4)
                    };
                    check.SetResourceReference(FrameworkElement.StyleProperty, "Tick");
                    check.Click += (s, e) => RefreshCollectionCounts();
                    choice.Checks.Add(check);
                    content.Children.Add(check);
                    var detail = Paragraph(section.Detail);
                    detail.FontSize = 13;
                    detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                    content.Children.Add(detail);
                }

                // One action row: the main step first and showing how many actions it adds.
                var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                DockPanel.SetDock(actions, Dock.Bottom);
                frame.Children.Add(actions);
                frame.Children.Add(content);
                var add = ToolButton(actions, Lang.T("Добавить выбранное в план"), () => AddCollectionToPlan(choice));
                add.Style = (Style)Window.FindResource("Primary");
                collectionPlanButtons.Add(add);
                choice.Add = add;
                var browse = ToolButton(actions, Lang.T("Посмотреть действия →"), () => ChooseCollection(CollectionIds(choice)));
                Window.RegisterName(new[] { "CollectionPrivacy", "CollectionExplorer", "CollectionGaming", "CollectionLight", "CollectionLaptop" }[i], browse);
                var guided = ToolButton(actions, Lang.T("Подобрать по ответам…"), () => OpenCollectionAssistant(choice));
                guided.SetResourceReference(FrameworkElement.StyleProperty, "Ghost");
                collectionPlanButtons.Add(guided);
                foreach (Button button in actions.Children)
                    button.Margin = new Thickness(0, 0, 8, 8);
                var card = new Border
                {
                    Child = frame,
                    Padding = new Thickness(20, 18, 20, 10)
                };
                Card(card);
                collectionCards.Children.Add(card);
            }

            RefreshCollectionCounts();
        }

        private string[] CollectionAvailableIds(System.Collections.Generic.IEnumerable<string> ids, int build)
        {
            var family = build >= 22000 ? "win11" : "win10";
            return ids.Distinct().Where(id => catalogue.Any(t => t.Id == id && (t.Os == "any" || t.Os == family))).ToArray();
        }

        // Sections made only of high-risk actions wait for the full mode, as those actions do in the catalogue.
        private void RefreshCollectionModes()
        {
            if (collectionChoices == null)
                return;
            int build = Environment.OSVersion.Version.Build;
            foreach (var choice in collectionChoices)
            {
                for (int i = 0; i < choice.Sections.Length; i++)
                {
                    var section = choice.Sections[i];
                    var ids = CollectionAvailableIds(section.Ids, build);
                    bool expertOnly = Simple && ids.Length > 0 && ids.All(id => catalogue.First(t => t.Id == id).Risk == "high");
                    var check = choice.Checks[i];
                    check.IsEnabled = ids.Length > 0 && !expertOnly;
                    if (!check.IsEnabled)
                        check.IsChecked = false;
                    check.ToolTip = expertOnly ? Lang.T("Доступно в полном режиме: здесь только действия высокого риска.") : ids.Length < section.Ids.Length ? Lang.T("Действия для другой версии Windows исключены из выбора.") : null;
                }
            }

            RefreshCollectionCounts();
        }

        private string[] CollectionIds(CollectionChoice choice)
        {
            var ids = CollectionAvailableIds(choice.Sections.Where((s, i) => choice.Checks[i].IsChecked == true).SelectMany(s => s.Ids), Environment.OSVersion.Version.Build);
            // The simple mode never plans what it hides in the catalogue, whichever way the section was ticked.
            return Simple ? ids.Where(id => catalogue.First(t => t.Id == id).Risk != "high").ToArray() : ids;
        }

        private void RefreshCollectionCounts()
        {
            foreach (var choice in collectionChoices)
            {
                var all = choice.Sections.SelectMany(s => s.Ids).Distinct().ToArray();
                int available = CollectionAvailableIds(all, Environment.OSVersion.Version.Build).Length;
                choice.Add.Content = Lang.T("Добавить в план · ") + CollectionIds(choice).Length;
                choice.Count.Text = Lang.T("Выбрано ") + CollectionIds(choice).Length + Lang.T(" из ") + available + Lang.T(" действий для вашей версии Windows.") + (available < all.Length ? Lang.T(" Для другой версии ОС: ") + (all.Length - available) + Lang.T(" — исключены из выбора.") : "") + Lang.T(" Наличие компонентов и служб проверяется при выполнении.");
            }
        }

        private void AddCollectionToPlan(CollectionChoice choice)
        {
            if (busy)
                return;
            var ids = CollectionIds(choice);
            var previous = preferences.Plan;
            var next = previous.Concat(ids).Distinct().ToList();
            if (next.Count > 200)
            {
                Text("Status", Lang.T("В плане больше 200 действий. Сначала выполните часть плана."));
                return;
            }

            preferences.Plan = next;
            if (!SavePreferences())
            {
                preferences.Plan = previous;
                return;
            }

            RefreshPlan();
            Text("Status", Lang.T("Добавлено в план: ") + (next.Count - previous.Count) + Lang.T(". Проверьте список и последствия перед применением."));
            ShowPage(4);
        }
    }
}
