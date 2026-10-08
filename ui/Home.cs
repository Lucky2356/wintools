using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private const int HomeIndex = 15;
        private Button[] homeScenarios;

        // The start page: the live state of the PC and four everyday goals, each one click from the right tool.
        private void InitializeHome()
        {
            var panel = ToolPage("HomePage");
            InitializeDashboard(panel);
            var heading = new TextBlock { Text = Lang.T("Что хотите сделать?"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 12) };
            heading.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            panel.Children.Add(heading);
            // Four goals side by side on a wide monitor, two by two on a laptop, one per row in a narrow window.
            var grid = new CardFlow(250, 4, true) { Gap = 12 };
            panel.Children.Add(grid);
            homeScenarios = new[]
            {
                AddScenario(grid, "\uE945", "Accent", Lang.T("Ускорить компьютер"), Lang.T("Автозагрузка, схема питания и подсказки, что ещё можно улучшить."), Lang.T("Ускорить"), () => ShowPage(8)),
                AddScenario(grid, "\uE72E", "Accent2", Lang.T("Убрать рекламу и подсказки"), Lang.T("Готовые подборки: меньше рекламы, телеметрии и навязчивых советов Windows."), Lang.T("Выбрать подборку"), () => ShowPage(2)),
                AddScenario(grid, "\uE74D", "Success", Lang.T("Освободить место"), Lang.T("Временные файлы, кэш браузеров и что занимает больше всего места на диске."), Lang.T("Посмотреть"), () => OpenMaintenance(6)),
                AddScenario(grid, "\uE90F", "Warning", Lang.T("Проверить Windows"), Lang.T("Проверка системных файлов и компонентов без изменений; восстановление — по вашему решению."), Lang.T("Проверить"), () => OpenMaintenance(0))
            };
            var links = new WrapPanel { Margin = new Thickness(-8, 12, 0, 8) };
            var catalogue = new Button { Content = Lang.T("Тонкая настройка: каталог действий →"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 4, 8, 4) };
            catalogue.SetResourceReference(FrameworkElement.StyleProperty, "Link");
            catalogue.Click += (s, e) => ShowPage(0);
            links.Children.Add(catalogue);
            var history = new Button { Content = Lang.T("История и откат →"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 4, 8, 4) };
            history.SetResourceReference(FrameworkElement.StyleProperty, "Link");
            history.Click += (s, e) => ShowPage(1);
            links.Children.Add(history);
            panel.Children.Add(links);
        }

        private void OpenMaintenance(int mode)
        {
            ShowPage(11);
            if (integrityChoice != null && integrityChoice.IsEnabled)
                integrityChoice.SelectedIndex = mode;
        }

        // A whole card is the button: the tinted icon, the goal, what it covers and where it leads.
        private Button AddScenario(Panel grid, string icon, string color, string title, string detail, string action, Action open)
        {
            var content = new DockPanel();
            var button = new Button { Content = content, Padding = new Thickness(20, 18, 20, 16), ToolTip = action };
            button.SetResourceReference(FrameworkElement.StyleProperty, "Tile");
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.Click += (s, e) => open();
            var badge = new Grid { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
            var tint = new Border { CornerRadius = new CornerRadius(8), Opacity = 0.16 };
            tint.SetResourceReference(Border.BackgroundProperty, color);
            badge.Children.Add(tint);
            var glyph = new TextBlock { Text = icon, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, color);
            badge.Children.Add(glyph);
            DockPanel.SetDock(badge, Dock.Top);
            content.Children.Add(badge);
            var go = new TextBlock { Text = action + " →", Margin = new Thickness(0, 14, 0, 0), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
            go.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            DockPanel.SetDock(go, Dock.Bottom);
            content.Children.Add(go);
            var words = new StackPanel();
            var name = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            words.Children.Add(name);
            var about = new TextBlock { Text = detail, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
            about.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            words.Children.Add(about);
            content.Children.Add(words);
            grid.Children.Add(button);
            return button;
        }

        private async Task HomeSmoke()
        {
            ShowPage(HomeIndex);
            Window.UpdateLayout();
            await Task.Delay(100);
            Assert(homeScenarios.Length == 4 && dashboardHeadline.Text.Length > 0 && IsVisibleInWindow("NavHome"), "Home page incomplete");
            Assert(placementPlanned && placementBounds.Width > 0 && placementBounds.X >= placementWork.X && placementBounds.Y >= placementWork.Y && placementBounds.X + placementBounds.Width <= placementWork.X + placementWork.Width && placementBounds.Y + placementBounds.Height <= placementWork.Y + placementWork.Height, "Opening bounds " + placementBounds + " do not fit the monitor work area " + placementWork);
            Capture("portable-ui-home.png");
            homeScenarios[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(page == 11 && integrityChoice.SelectedIndex == 6, "Free space scenario did not open cleanup");
            integrityChoice.SelectedIndex = 0;
            homeScenarios[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(page == 8, "Speed-up scenario did not open its page");
            ShowPage(0);
        }
    }
}
