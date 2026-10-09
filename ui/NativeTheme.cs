using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Wintools
{
    internal static class NativeTheme
    {
        internal static readonly string[] Keys =
        {
            "Background",
            "Sidebar",
            "Surface",
            "Raised",
            "Border",
            "Text",
            "Muted",
            "Accent",
            "AccentText",
            "Selection",
            "Danger",
            "Hover",
            "Success",
            "Warning",
            "Scrim",
            "Accent2",
            "Layer",
            "NavSelected",
            "Divider"
        };
        // Neutral greys of Windows 11: the window and navigation, a lighter page layer, cards above it; Fluent accent and status colours.
        private static readonly string[] Dark =
        {
            "#1C1C1C",
            "#1C1C1C",
            "#2B2B2B",
            "#343434",
            "#3A3A3A",
            "#F3F3F3",
            "#ABABAB",
            "#60CDFF",
            "#04131C",
            "#2C3D46",
            "#FF99A4",
            "#323232",
            "#6CCB5F",
            "#F5C84C",
            "#99000000",
            "#B9A3FF",
            "#232323",
            "#2D2D2D",
            "#2F2F2F"
        };
        private static readonly string[] Light =
        {
            "#F0F0F0",
            "#F0F0F0",
            "#FFFFFF",
            "#FAFAFA",
            "#E0E0E0",
            "#1A1A1A",
            "#5C5C5C",
            "#005FB8",
            "#FFFFFF",
            "#E1ECF7",
            "#B42318",
            "#F3F3F3",
            "#0E6E0E",
            "#8A5300",
            "#66000000",
            "#6B4FBB",
            "#F9F9F9",
            "#E4E4E4",
            "#E5E5E5"
        };
        // Text colours that must stay readable (WCAG AA, 4.5:1) on every surface they are drawn on.
        internal static readonly string[] Foregrounds = { "Text", "Muted", "Accent", "Danger", "Success", "Warning" };
        internal static readonly string[] Surfaces = { "Background", "Layer", "Surface", "Raised", "Selection", "NavSelected", "Hover" };

        internal static double Contrast(string first, string second)
        {
            Func<string, double> luminance = value =>
            {
                var color = (Color)ColorConverter.ConvertFromString(value);
                Func<byte, double> channel = c =>
                {
                    double v = c / 255.0;
                    return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                };
                return 0.2126 * channel(color.R) + 0.7152 * channel(color.G) + 0.0722 * channel(color.B);
            };
            double a = luminance(first), b = luminance(second);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        internal static bool IsDark(string mode)
        {
            if (mode == "dark")
                return true;
            if (mode == "light")
                return false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                    return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static Dictionary<string, string> Colors(bool dark)
        {
            string[] values = dark ? Dark : Light;
            if (SystemParameters.HighContrast)
            {
                string window = SystemColors.WindowColor.ToString(), text = SystemColors.WindowTextColor.ToString(), control = SystemColors.ControlColor.ToString(), highlight = SystemColors.HighlightColor.ToString(), highlightText = SystemColors.HighlightTextColor.ToString();
                values = new[]
                {
                    window,
                    window,
                    window,
                    control,
                    text,
                    text,
                    text,
                    highlight,
                    highlightText,
                    control,
                    text,
                    control,
                    text,
                    text,
                    "#B3000000",
                    highlight,
                    window,
                    control,
                    text
                };
            }

            var result = new Dictionary<string, string>();
            for (int i = 0; i < Keys.Length; i++)
                result.Add(Keys[i], values[i]);
            return result;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        // 20 = immersive dark mode (Windows 10 20H1+), 35 = caption colour (Windows 11). Older systems reject the call harmlessly.
        internal static void TitleBar(IntPtr handle, bool dark, Color caption)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
                int color = caption.R | caption.G << 8 | caption.B << 16;
                DwmSetWindowAttribute(handle, 35, ref color, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }
    }
}
