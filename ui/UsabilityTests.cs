using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        // The welcome, the simple mode, keyboard shortcuts, help for failures, the light interface and screen-reader names.
        private async Task UsabilitySmoke()
        {
            Assert(preferences.Mode == "full" && !preferences.Welcomed, "Smoke did not start as a fresh full-mode run");
            ShowPage(HomeIndex);
            // The update switch only changes the update setting; the welcome belongs to the start of the window.
            var autoCheck = Get<CheckBox>("AutoCheck");
            bool autoCheckBefore = preferences.AutoCheck;
            autoCheck.IsChecked = false;
            autoCheck.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert(!SheetOpen && !preferences.AutoCheck, "The update switch opened a dialog");
            preferences.AutoCheck = autoCheckBefore;
            autoCheck.IsChecked = autoCheckBefore;
            SavePreferences();
            ShowUpdateMode();
            ShowWelcome();
            Assert(SheetOpen && !Get<Grid>("Body").IsEnabled, "Welcome did not open as a dialog");
            var missing = new List<string>();
            for (int step = 0; step < 3; step++)
            {
                Window.UpdateLayout();
                await Task.Delay(80);
                NameControls(sheetOverlay);
                missing.AddRange(UnnamedControls(sheetOverlay).Select(m => "welcome " + step + ": " + m));
                Capture("portable-ui-welcome-" + (step + 1) + ".png");
                if (step < 2)
                    SheetPrimary().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            welcomeModes[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SheetPrimary().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!SheetOpen && Get<Grid>("Body").IsEnabled && preferences.Welcomed && preferences.Mode == "simple" && preferences.SeenVersion == Program.Version, "Welcome did not finish with the chosen mode");
            Assert(Preferences.Load().Mode == "simple", "Chosen mode not saved");

            // The simple mode: expert pages leave navigation, home tiles and search; high-risk actions leave the catalogue.
            Window.UpdateLayout();
            Assert(Get<UIElement>("Verify").Visibility == Visibility.Collapsed && Get<UIElement>("AdvancedToggle").Visibility == Visibility.Collapsed && Get<UIElement>("Risky").Visibility == Visibility.Collapsed && !homeShortcuts[5].IsVisible && homeShortcuts[9].IsVisible && hostsCard.Visibility == Visibility.Collapsed, "Simple mode still shows expert tools");
            Assert(!SearchEverywhere("Службы").Any(h => h.Detail.StartsWith("Раздел") && h.Title.StartsWith("Службы")) && SearchEverywhere("Приложения").Any(h => h.Detail.StartsWith("Раздел")), "Search shows hidden pages in simple mode");
            Assert(!SearchEverywhere("SysMain").Any(h => h.Detail.StartsWith("Настройка")), "Search offers a high-risk action in simple mode");
            Capture("portable-ui-simple-home.png");
            ShowPage(0);
            Get<Button>("BackToGroups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Get<Button>("ShowAll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rows = Get<ListBox>("Items").Items.Cast<ActionRow>().ToArray();
            Assert(rows.Length > 50 && rows.All(r => r.Item.Risk != "high"), "Simple catalogue shows high-risk actions");
            Get<ListBox>("Items").SelectedIndex = 0;
            Assert(Get<WrapPanel>("Facts").Children.Count >= 2, "Action facts missing");
            // The catalogue stays a list: later checks of the compact window expect the action card on screen.
            Get<ComboBox>("Mode").SelectedIndex = 1;
            Window.UpdateLayout();
            Assert(preferences.Mode == "full" && Get<UIElement>("Verify").Visibility == Visibility.Visible && Get<UIElement>("Risky").Visibility == Visibility.Visible && hostsCard.Visibility == Visibility.Visible, "Full mode did not return the tools");

            // Keyboard: Ctrl+number opens pages, F1 shows the help, Esc closes it.
            Assert(HandleShortcut(Key.D2, ModifierKeys.Control) && page == 6 && HandleShortcut(Key.NumPad0, ModifierKeys.Control) && page == 3 && HandleShortcut(Key.D1, ModifierKeys.Control) && page == HomeIndex, "Ctrl+number did not open pages");
            Assert(!HandleShortcut(Key.D2, ModifierKeys.Control | ModifierKeys.Shift) && page == HomeIndex, "Ctrl+Shift+number treated as a shortcut");
            Assert(HandleShortcut(Key.F1, ModifierKeys.None) && SheetOpen && sheetTitle.Text == "Горячие клавиши", "F1 did not open the keyboard help");
            Window.UpdateLayout();
            await Task.Delay(80);
            Capture("portable-ui-shortcuts.png");
            Window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert(!SheetOpen && Get<Grid>("Body").IsEnabled, "Esc did not close the help");

            // What's new after an update, and the next steps after a failure.
            ShowWhatsNew();
            Window.UpdateLayout();
            await Task.Delay(80);
            Assert(SheetOpen && preferences.SeenVersion == Program.Version, "What's new not shown");
            Capture("portable-ui-whats-new.png");
            CloseSheet();
            Text("Status", "Не удалось выполнить операцию");
            Assert(Get<UIElement>("ReportProblem").Visibility == Visibility.Visible, "Failure offers no next steps");
            Get<Button>("ReportProblem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Window.UpdateLayout();
            await Task.Delay(80);
            Assert(SheetOpen, "Problem help not shown");
            NameControls(sheetOverlay);
            missing.AddRange(UnnamedControls(sheetOverlay).Select(m => "problem: " + m));
            Capture("portable-ui-problem.png");
            CloseSheet();
            Text("Status", "Готово к работе");
            Assert(Get<UIElement>("ReportProblem").Visibility == Visibility.Collapsed, "Next steps shown without a failure");

            // An unfinished operation is announced on the start page once; hiding it remembers the entry.
            ShowPage(HomeIndex);
            var unfinished = new HistoryRow { Run = "smoke-attention", Title = "Тестовая операция", Status = "Требует внимания", TimeUtc = DateTime.UtcNow };
            ShowAttention(new[] { unfinished });
            Window.UpdateLayout();
            Assert(attentionBanner.IsVisible && attentionText.Text.Contains("Тестовая операция"), "Unfinished operation not announced");
            NameControls(attentionBanner);
            missing.AddRange(UnnamedControls(attentionBanner).Select(m => "attention: " + m));
            Capture("portable-ui-attention.png");
            AcknowledgeAttention();
            ShowAttention(new[] { unfinished });
            Assert(attentionBanner.Visibility == Visibility.Collapsed && Preferences.Load().Acknowledged.Contains("smoke-attention|Тестовая операция"), "Hidden notice came back");

            // The light interface drops shadows and the pulse; turning it off brings them back.
            Get<CheckBox>("Lite").IsChecked = true;
            Get<CheckBox>("Lite").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert(preferences.Lite && Window.Resources["ShadowLarge"] == null && resourceTimer.Interval.TotalSeconds == 4, "Light interface kept heavy effects");
            Get<CheckBox>("Lite").IsChecked = false;
            Get<CheckBox>("Lite").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert(!preferences.Lite && Window.Resources["ShadowLarge"] != null && resourceTimer.Interval.TotalSeconds == 2, "Light interface not turned off");

            // Every control on every page has a name a screen reader can say.
            for (int i = 0; i < pages.Length; i++)
            {
                ShowPage(i);
                Window.UpdateLayout();
                await Task.Delay(60);
                NameControls(Get<DependencyObject>(pages[i]));
                missing.AddRange(UnnamedControls(Get<DependencyObject>(pages[i])).Select(m => pages[i] + ": " + m));
            }

            missing.AddRange(UnnamedControls(Get<DependencyObject>("Sidebar")).Select(m => "Sidebar: " + m));
            Assert(missing.Count == 0, missing.Count + " controls without a readable name: " + string.Join(" | ", missing.Distinct().Take(40)));
            ShowPage(0);
        }

        private Button SheetPrimary()
        {
            var end = (StackPanel)sheetFooter.Children[1];
            return (Button)end.Children[end.Children.Count - 1];
        }

        // Started with --ui-smoke-english after the main smoke: the pages a newcomer sees first, in English, with no Russian left.
        private async Task EnglishSmoke()
        {
            var cyrillic = new System.Text.RegularExpressions.Regex("[\\u0400-\\u04FF]");
            // The language switch names the other language on purpose, so it can be found by someone who cannot read this one.
            var allowed = new[] { "Русский", "Язык", "Interface language · Язык" };
            var left = new List<string>();
            // History is left out: it lists records the Russian smoke created, such as its test Store package.
            foreach (int index in new[] { HomeIndex, 2, 3, 0, 8 })
            {
                ShowPage(index);
                Window.UpdateLayout();
                await Task.Delay(150);
                left.AddRange(VisibleTexts(Get<DependencyObject>(pages[index])).Where(t => cyrillic.IsMatch(t) && !allowed.Contains(t)).Select(t => pages[index] + ": " + t));
                Capture("portable-ui-en-" + index + ".png");
            }

            ShowWelcome();
            for (int step = 0; step < 3; step++)
            {
                Window.UpdateLayout();
                await Task.Delay(80);
                left.AddRange(VisibleTexts(sheetOverlay).Where(t => cyrillic.IsMatch(t) && !allowed.Contains(t)).Select(t => "welcome: " + t));
                if (step == 0)
                    Capture("portable-ui-en-welcome.png");
                if (step < 2)
                    SheetPrimary().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            CloseSheet();
            ShowShortcuts();
            Window.UpdateLayout();
            left.AddRange(VisibleTexts(sheetOverlay).Where(t => cyrillic.IsMatch(t)).Select(t => "shortcuts: " + t));
            CloseSheet();
            Assert(left.Count == 0, "Russian left in the English interface: " + string.Join(" | ", left.Distinct().Take(40)));
        }

        private static IEnumerable<string> VisibleTexts(DependencyObject root)
        {
            foreach (var block in Descendants(root).OfType<TextBlock>())
                if (block.IsVisible && !string.IsNullOrWhiteSpace(block.Text))
                    yield return block.Text;
        }
    }
}
