using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private int welcomeStep;
        private string welcomeMode;
        private Button[] welcomeModes;

        // Which dialog the start shows: the welcome until it was finished once, then "What's new" once per version.
        internal static string StartupSheet(Preferences preferences, string version)
        {
            if (!preferences.Welcomed)
                return "welcome";
            return preferences.SeenVersion != version ? "whats-new" : null;
        }

        // The first start explains the program in three short steps; nothing in Windows changes here.
        private void ShowWelcome()
        {
            welcomeStep = 0;
            welcomeMode = preferences.Mode;
            RenderWelcome();
        }

        private void RenderWelcome()
        {
            var body = new StackPanel();
            string title;
            if (welcomeStep == 0)
            {
                title = Lang.T("Добро пожаловать в Wintools");
                body.Children.Add(SheetText(Lang.T("Wintools помогает настроить Windows под ваши задачи: ускорить компьютер, убрать рекламу, освободить место и проверить систему. Сначала выберите, как приложению выглядеть.")));
                body.Children.Add(WelcomeChoice(Lang.T("Язык интерфейса · Language"), Get<ComboBox>("Language"), Lang.T("Язык применится после перезапуска Wintools.")));
                body.Children.Add(WelcomeChoice(Lang.T("Цветовая тема"), Get<ComboBox>("Theme"), null));
                body.Children.Add(WelcomeChoice(Lang.T("Размер текста"), Get<ComboBox>("TextSize"), Lang.T("Если текст мелкий, выберите «Крупный» или больше — интерфейс увеличится целиком.")));
            }
            else if (welcomeStep == 1)
            {
                title = Lang.T("Как Wintools бережёт ваш компьютер");
                body.Children.Add(SheetPoint("", Lang.T("Сначала объяснение"), Lang.T("У каждого действия написано, что изменится, на что обратить внимание и можно ли вернуть прежнее состояние.")));
                body.Children.Add(SheetPoint("", Lang.T("Предпросмотр без изменений"), Lang.T("Кнопка «Предпросмотр» показывает, что будет сделано, ничего не меняя в Windows.")));
                body.Children.Add(SheetPoint("", Lang.T("История и откат"), Lang.T("Перед изменением Wintools сохраняет прежнее значение. Вернуть его можно в разделе «История и откат».")));
                body.Children.Add(SheetPoint("", Lang.T("Ничего без вашего согласия"), Lang.T("Windows меняется только после подтверждения. Права администратора запрашиваются отдельно, когда они действительно нужны.")));
            }
            else
            {
                title = Lang.T("Выберите режим");
                body.Children.Add(SheetText(Lang.T("Режим можно сменить в любой момент в «Настройках».")));
                var modes = new CardFlow(240, 2, true) { Gap = 12, Margin = new Thickness(0, 0, 0, 14) };
                welcomeModes = new[]
                {
                    WelcomeMode(modes, "simple", "", Lang.T("Простой"), Lang.T("Основные разделы и проверенные действия. Действия высокого риска и инструменты для опытных скрыты. Рекомендуется, если вы не уверены.")),
                    WelcomeMode(modes, "full", "", Lang.T("Полный"), Lang.T("Все инструменты: службы, процессы, автозагрузка, сверка, блокировка сайтов и действия высокого риска."))
                };
                body.Children.Add(modes);
                var restore = new CheckBox { Content = Lang.T("Запрашивать точку восстановления Windows перед изменениями"), IsChecked = preferences.RestorePoint, Margin = new Thickness(0, 0, 0, 6) };
                restore.Click += (s, e) =>
                {
                    Get<CheckBox>("RestorePoint").IsChecked = restore.IsChecked == true;
                    preferences.RestorePoint = restore.IsChecked == true;
                    SavePreferences();
                };
                body.Children.Add(restore);
                var note = SheetText(Lang.T("Точка восстановления позволяет вернуть Windows целиком, даже если что-то пошло не так. Создание занимает до минуты."));
                note.FontSize = 13;
                note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                body.Children.Add(note);
                ShowWelcomeMode();
            }

            var left = welcomeStep > 0 ? new[] { SheetButton(Lang.T("Назад"), false, () => { welcomeStep--; RenderWelcome(); }) } : new Button[0];
            var skip = SheetButton(Lang.T("Пропустить"), false, FinishWelcome);
            skip.SetResourceReference(FrameworkElement.StyleProperty, "Ghost");
            var next = welcomeStep < 2 ? SheetButton(Lang.T("Далее"), true, () => { welcomeStep++; RenderWelcome(); }) : SheetButton(Lang.T("Начать работу"), true, FinishWelcome);
            ShowSheet(Lang.T("Шаг ") + (welcomeStep + 1) + Lang.T(" из 3"), title, body, FinishWelcome, left, welcomeStep < 2 ? new[] { skip, next } : new[] { next });
        }

        // A copy of a settings choice: picking here changes the setting itself, which applies and saves it.
        private UIElement WelcomeChoice(string label, ComboBox source, string note)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var choice = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(choice, label);
            foreach (ComboBoxItem item in source.Items)
                choice.Items.Add(new ComboBoxItem { Content = item.Content });
            choice.SelectedIndex = source.SelectedIndex;
            choice.SelectionChanged += (s, e) => source.SelectedIndex = choice.SelectedIndex;
            panel.Children.Add(choice);
            if (note != null)
            {
                var hint = new TextBlock { Text = note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                panel.Children.Add(hint);
            }

            return panel;
        }

        private Button WelcomeMode(Panel parent, string mode, string glyph, string title, string detail)
        {
            var words = new StackPanel();
            var icon = new TextBlock { Text = glyph, FontSize = 20, Margin = new Thickness(0, 0, 0, 10) };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            words.Children.Add(icon);
            words.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
            var about = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 4, 0, 0) };
            about.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            words.Children.Add(about);
            var button = new Button { Content = words, Tag = mode, Padding = new Thickness(16, 14, 16, 14), VerticalAlignment = VerticalAlignment.Stretch };
            button.SetResourceReference(FrameworkElement.StyleProperty, "Tile");
            System.Windows.Automation.AutomationProperties.SetName(button, title + ". " + detail);
            button.Click += (s, e) =>
            {
                welcomeMode = mode;
                ShowWelcomeMode();
            };
            parent.Children.Add(button);
            return button;
        }

        // The chosen mode wears the accent border; a screen reader hears "selected" with its name.
        private void ShowWelcomeMode()
        {
            foreach (var button in welcomeModes)
            {
                bool chosen = (string)button.Tag == welcomeMode;
                button.BorderThickness = new Thickness(chosen ? 2 : 1);
                if (chosen)
                    button.SetResourceReference(Control.BorderBrushProperty, "Accent");
                else
                    button.SetResourceReference(Control.BorderBrushProperty, "Border");
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, chosen ? Lang.T("Выбрано") : "");
            }
        }

        private void FinishWelcome()
        {
            preferences.Welcomed = true;
            preferences.SeenVersion = Program.Version;
            preferences.Mode = welcomeMode ?? preferences.Mode;
            SavePreferences();
            CloseSheet();
            ApplyMode();
            ShowPage(HomeIndex);
        }

        // After an update the first start lists what changed, taken from the bundled CHANGELOG section of this version.
        private void ShowWhatsNew()
        {
            preferences.SeenVersion = Program.Version;
            SavePreferences();
            var points = WhatsNew(ReadChangelog(), Program.Version);
            var body = new StackPanel();
            if (points.Length == 0 || Lang.English)
                body.Children.Add(SheetText(Lang.T("Список изменений этой версии доступен на странице выпуска на GitHub.")));
            foreach (var point in points.Take(8))
            {
                if (Lang.English)
                    break;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var dot = new TextBlock { Text = "•", FontWeight = FontWeights.SemiBold };
                dot.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                row.Children.Add(dot);
                var text = new TextBlock { Text = point, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                body.Children.Add(row);
            }

            var release = SheetButton(Lang.T("Все изменения на GitHub ↗"), false, () => OpenUrl("https://github.com/Lucky2356/wintools/releases/tag/v" + Program.Version));
            release.SetResourceReference(FrameworkElement.StyleProperty, "Link");
            ShowSheet(null, Lang.T("Что нового в версии ") + Program.Version, body, CloseSheet, new[] { release }, new[] { SheetButton(Lang.T("Понятно"), true, CloseSheet) });
        }

        private static string ReadChangelog()
        {
            try
            {
                var path = Path.Combine(Program.Data, "CHANGELOG.md");
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            }
            catch (IOException)
            {
                return "";
            }
            catch (UnauthorizedAccessException)
            {
                return "";
            }
        }

        // The bullet points of the "## <version>" section without Markdown marks; nested lines join their bullet.
        internal static string[] WhatsNew(string changelog, string version)
        {
            var lines = (changelog ?? "").Replace("\r\n", "\n").Split('\n');
            int start = Array.FindIndex(lines, l => Regex.IsMatch(l, "^##\\s+" + Regex.Escape(version) + "(\\s|$)"));
            if (start < 0)
                return new string[0];
            var points = new System.Collections.Generic.List<string>();
            for (int i = start + 1; i < lines.Length && !lines[i].StartsWith("## "); i++)
            {
                var line = lines[i].TrimEnd();
                if (Regex.IsMatch(line, "^[-*]\\s+"))
                    points.Add(Regex.Replace(line, "^[-*]\\s+", ""));
                else if (points.Count > 0 && line.StartsWith("  ") && line.Trim().Length > 0)
                    points[points.Count - 1] += " " + line.Trim();
            }

            return points.Select(p => Regex.Replace(p, "\\*\\*|`", "")).ToArray();
        }
    }
}
