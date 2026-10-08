using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private double systemTextScale = SystemTextScale(), uiZoom = 1;

        // Windows "Text size" (Settings › Accessibility) as a factor from 1 to 2.25; classic desktop programs do not receive it on their own.
        internal static double SystemTextScale()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Accessibility", false))
                {
                    var value = key == null ? null : key.GetValue("TextScaleFactor");
                    if (value is int)
                        return Math.Max(1, Math.Min(2.25, (int)value / 100.0));
                }
            }
            catch (Exception)
            {
            }

            return 1;
        }

        // The whole interface grows with the text so spacing stays in proportion; the Windows factor is capped at 150 %.
        internal static double RequestedTextScale(string setting, double system)
        {
            switch (setting)
            {
                case "100":
                    return 1;
                case "125":
                    return 1.25;
                case "150":
                    return 1.5;
                default:
                    return Math.Min(1.5, Math.Max(1, system));
            }
        }

        // Never zoom past the point where the smallest supported layout (800 × 560) still fits the window; 5 % steps avoid jitter while resizing.
        internal static double EffectiveTextScale(double requested, double width, double height)
        {
            double fit = Math.Min(width / WindowPlacement.MinimumWidth, height / WindowPlacement.MinimumHeight);
            return Math.Max(1, Math.Floor(Math.Min(requested, fit) * 20 + 1e-9) / 20);
        }

        // Applies the zoom for the current window size and returns it; the layout then works in the zoomed coordinates.
        private double ApplyTextScale(double width, double height)
        {
            double zoom = EffectiveTextScale(RequestedTextScale(preferences.TextSize, systemTextScale), width, height);
            if (Math.Abs(zoom - uiZoom) < 0.001)
                return zoom;
            uiZoom = zoom;
            var transform = new ScaleTransform(zoom, zoom);
            transform.Freeze();
            ((FrameworkElement)Window.Content).LayoutTransform = zoom == 1 ? Transform.Identity : transform;
            // Drop-downs, tooltips and menus live in their own windows and take the same zoom from this resource.
            Window.Resources["UiZoom"] = transform;
            // Display text snaps glyphs to pixels and blurs once scaled, so zoomed text uses ideal layout.
            TextOptions.SetTextFormattingMode(Window, zoom == 1 ? TextFormattingMode.Display : TextFormattingMode.Ideal);
            return zoom;
        }

        private void InitializeTextScale()
        {
            var choice = Get<ComboBox>("TextSize");
            choice.SelectedIndex = Math.Max(0, Array.IndexOf(Preferences.TextSizes, preferences.TextSize));
            choice.SelectionChanged += (s, e) =>
            {
                preferences.TextSize = Preferences.TextSizes[Math.Max(0, choice.SelectedIndex)];
                if (ready)
                    SavePreferences();
                AdaptLayout();
            };
        }

        private void RefreshSystemTextScale()
        {
            double scale = SystemTextScale();
            if (Math.Abs(scale - systemTextScale) < 0.001)
                return;
            systemTextScale = scale;
            AdaptLayout();
        }

        private async Task TextScaleSmoke()
        {
            var choice = Get<ComboBox>("TextSize");
            double width = Window.Width, height = Window.Height;
            Window.Width = 1366;
            Window.Height = 768;
            ShowPage(HomeIndex);
            choice.SelectedIndex = 3;
            Window.UpdateLayout();
            await Task.Delay(100);
            var transform = ((FrameworkElement)Window.Content).LayoutTransform as ScaleTransform;
            // The runner screen may cap the window, so the expected zoom comes from the size the window actually got.
            Assert(preferences.TextSize == "150" && transform != null && transform.ScaleX > 1.2 && transform.ScaleX == EffectiveTextScale(1.5, Window.ActualWidth, Window.ActualHeight) && Window.Resources["UiZoom"] == transform, "Large text did not zoom the window: " + uiZoom + " at " + Window.ActualWidth + "x" + Window.ActualHeight);
            Assert(IsVisibleInWindow("NavHome") && homeScenarios[0].IsVisible, "Large text hides navigation");
            Capture("portable-ui-large-text.png");
            ShowPage(3);
            choice.BringIntoView();
            Window.UpdateLayout();
            await Task.Delay(100);
            Assert(IsVisibleInWindow("TextSize"), "Text size choice clipped at large text");
            choice.SelectedIndex = 0;
            Window.UpdateLayout();
            Assert(preferences.TextSize == "system" && uiZoom == EffectiveTextScale(RequestedTextScale("system", systemTextScale), Window.ActualWidth, Window.ActualHeight), "Text size did not return to the Windows setting");
            Window.Width = width;
            Window.Height = height;
            ShowPage(0);
        }

        private void RememberWindow()
        {
            if (smoke)
                return;
            bool maximized;
            var bounds = WindowPlacement.Remember(Window, out maximized);
            if (bounds == null)
                return;
            preferences.WindowBounds = bounds;
            preferences.WindowMaximized = maximized;
            SavePreferences();
        }
    }
}
