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
            "Scrim"
        };
        private static readonly string[] Dark =
        {
            "#111318",
            "#0C0E12",
            "#181B21",
            "#20242C",
            "#2A2F38",
            "#ECEFF4",
            "#9AA3B2",
            "#6EA8FE",
            "#0B1A33",
            "#1E2A3D",
            "#FF8A80",
            "#1C2028",
            "#5FD39A",
            "#F2C46D",
            "#B3000000"
        };
        private static readonly string[] Light =
        {
            "#F4F6FA",
            "#EBEEF4",
            "#FFFFFF",
            "#F0F2F7",
            "#DCE1EA",
            "#161B26",
            "#5D6678",
            "#2F6FEB",
            "#FFFFFF",
            "#E3ECFD",
            "#C4362C",
            "#E6EAF1",
            "#1E8E5A",
            "#A86A00",
            "#80141A24"
        };
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
                    "#B3000000"
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
