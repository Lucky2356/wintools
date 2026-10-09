using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        // Ctrl+1 … Ctrl+9 and Ctrl+0 open the main pages in the order of the navigation.
        private static readonly int[] ShortcutPages = { HomeIndex, 6, 8, 0, 2, 4, 1, 9, 11, 3 };

        private void InitializeShortcuts()
        {
            for (int i = 0; i < ShortcutPages.Length; i++)
            {
                var button = Get<Button>(nav[ShortcutPages[i]]);
                var key = "Ctrl+" + (i + 1) % 10;
                button.ToolTip = (string)button.ToolTip + " · " + key;
                System.Windows.Automation.AutomationProperties.SetAcceleratorKey(button, key);
            }

            Window.PreviewKeyDown += (s, e) =>
            {
                if (HandleShortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers))
                    e.Handled = true;
            };
        }

        private bool HandleShortcut(Key key, ModifierKeys modifiers)
        {
            if (confirmation != null || SheetOpen || searchOverlay.Visibility == Visibility.Visible)
                return false;
            if (key == Key.F1 && modifiers == ModifierKeys.None)
            {
                ShowShortcuts();
                return true;
            }

            if (key == Key.F5 && modifiers == ModifierKeys.None)
            {
                RefreshCurrentPage();
                return true;
            }

            if (modifiers != ModifierKeys.Control)
                return false;
            int digit = key >= Key.D0 && key <= Key.D9 ? key - Key.D0 : key >= Key.NumPad0 && key <= Key.NumPad9 ? key - Key.NumPad0 : -1;
            if (digit < 0)
                return false;
            ShowPage(ShortcutPages[(digit + 9) % 10]);
            Get<Button>(nav[page]).Focus();
            return true;
        }

        // F5 repeats the reading of the current list; pages without a list are left alone.
        private void RefreshCurrentPage()
        {
            if (page == HomeIndex)
            {
                RefreshDashboard();
                return;
            }

            if (busy)
                return;
            if (page == 5 && serviceRefresh != null)
            {
                if (serviceRefresh.IsEnabled)
                    serviceRefresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }

            if (page != 9 && page != 12 && page != 13)
                return;
            var labels = new[] { Lang.T("Обновить список"), Lang.T("Обновить") };
            var button = Buttons(Get<DependencyObject>(pages[page])).FirstOrDefault(b => b.IsVisible && b.IsEnabled && labels.Contains(b.Content as string));
            if (button != null)
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void ShowShortcuts()
        {
            var body = new StackPanel();
            var rows = new[]
            {
                new[] { "Ctrl+K", Lang.T("Поиск по разделам, настройкам, службам и программам") },
                new[] { "Ctrl+1 … Ctrl+9, Ctrl+0", Lang.T("Главная, Состояние ПК, Ускорение, Каталог, Подборки, План, История, Приложения, Обслуживание, Настройки") },
                new[] { "F5", Lang.T("Обновить список на текущей странице или показатели Главной") },
                new[] { "F1", Lang.T("Эта справка") },
                new[] { "Esc", Lang.T("Закрыть окно поиска, подтверждения или справки") },
                new[] { "Tab, Shift+Tab", Lang.T("Перейти к следующему или предыдущему элементу") },
                new[] { Lang.T("Пробел, Enter"), Lang.T("Нажать кнопку или переключить отметку") },
                new[] { Lang.T("Стрелки"), Lang.T("Выбор в списке, вкладках и раскрывающемся списке") }
            };
            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            table.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < rows.Length; i++)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var key = new Border { Padding = new Thickness(8, 2, 8, 3), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 16, 10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                key.SetResourceReference(Border.BackgroundProperty, "Raised");
                key.SetResourceReference(Border.BorderBrushProperty, "Border");
                key.Child = new TextBlock { Text = rows[i][0], FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
                Grid.SetRow(key, i);
                table.Children.Add(key);
                var text = new TextBlock { Text = rows[i][1], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 10) };
                Grid.SetRow(text, i);
                Grid.SetColumn(text, 1);
                table.Children.Add(text);
            }

            body.Children.Add(table);
            var note = SheetText(Lang.T("Экранный диктор Windows (Ctrl+Win+Enter) читает названия кнопок, списков и строку состояния. Размер текста меняется в «Настройках → Оформление»."));
            note.FontSize = 13;
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            body.Children.Add(note);
            ShowSheet(null, Lang.T("Горячие клавиши"), body, CloseSheet, new Button[0], new[] { SheetButton(Lang.T("Закрыть"), true, CloseSheet) });
        }
    }
}
