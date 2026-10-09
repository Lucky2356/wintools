using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        // Services, verification, startup and processes: tools that assume the person knows what they switch off.
        private static readonly int[] ExpertPages = { 5, 7, 12, 13 };
        private readonly Dictionary<int, Button> homeShortcuts = new Dictionary<int, Button>();
        private Border hostsCard;
        private Effect shadowLarge, shadowSmall;

        private bool Simple
        {
            get
            {
                return preferences.Mode == "simple";
            }
        }

        // Lighter drawing for older computers, and no decorative motion when Windows animations are turned off.
        private bool Motion
        {
            get
            {
                return !preferences.Lite && SystemParameters.ClientAreaAnimation;
            }
        }

        private void InitializeModes()
        {
            shadowLarge = (Effect)Window.Resources["ShadowLarge"];
            shadowSmall = (Effect)Window.Resources["ShadowSmall"];
            var mode = Get<ComboBox>("Mode");
            mode.SelectedIndex = Simple ? 0 : 1;
            mode.SelectionChanged += (s, e) =>
            {
                var next = mode.SelectedIndex == 0 ? "simple" : "full";
                if (next == preferences.Mode)
                    return;
                preferences.Mode = next;
                SavePreferences();
                ApplyMode();
                Text("Status", Simple ? Lang.T("Простой режим: инструменты для опытных и действия высокого риска скрыты.") : Lang.T("Полный режим: доступны все инструменты."));
            };
            var lite = Get<CheckBox>("Lite");
            lite.IsChecked = preferences.Lite;
            lite.Click += (s, e) =>
            {
                preferences.Lite = lite.IsChecked == true;
                SavePreferences();
                ApplyLite();
            };
            Click("ShowWelcome", ShowWelcome);
            Click("ShowShortcuts", ShowShortcuts);
            ApplyLite();
            ApplyMode();
        }

        // The simple mode keeps the everyday pages: expert tools leave the navigation, the home tiles and search,
        // and high-risk catalogue actions are filtered out. Pages stay reachable from links that lead to them.
        private void ApplyMode()
        {
            bool simple = Simple;
            Visible("Verify", !simple);
            Visible("AdvancedToggle", !simple);
            if (simple)
                Visible("AdvancedNav", false);
            var risky = Get<CheckBox>("Risky");
            risky.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
            if (simple)
                risky.IsChecked = false;
            if (hostsCard != null)
                hostsCard.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
            foreach (var pair in homeShortcuts)
                pair.Value.Visibility = simple && ExpertPages.Contains(pair.Key) ? Visibility.Collapsed : Visibility.Visible;
            var mode = Get<ComboBox>("Mode");
            if (mode.SelectedIndex != (simple ? 0 : 1))
                mode.SelectedIndex = simple ? 0 : 1;
            Filter();
        }

        // Shadows cost the most on weak graphics; live graphs refresh half as often.
        private void ApplyLite()
        {
            Window.Resources["ShadowLarge"] = preferences.Lite ? null : shadowLarge;
            Window.Resources["ShadowSmall"] = preferences.Lite ? null : shadowSmall;
            Window.Resources["PopupMotion"] = Motion ? PopupAnimation.Fade : PopupAnimation.None;
            var interval = TimeSpan.FromSeconds(preferences.Lite ? 4 : 2);
            resourceTimer.Interval = interval;
            liveInterval = interval;
            UpdateStatusDot();
        }
    }
}
