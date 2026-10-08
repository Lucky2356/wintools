using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Wintools
{
    // The opening size follows the monitor the window appears on, measured in that monitor's DPI,
    // so an HD laptop, a 150 % Full HD screen and a 4K display all get a window that fits.
    internal static class WindowPlacement
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class MonitorInfo
        {
            internal int Size = Marshal.SizeOf(typeof(MonitorInfo));
            internal NativeRect Monitor, Work;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class NativePlacement
        {
            internal int Length = Marshal.SizeOf(typeof(NativePlacement));
            internal int Flags, ShowCommand;
            internal NativePoint Minimized, Maximized;
            internal NativeRect Normal;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowPlacement(IntPtr window, [In, Out] NativePlacement placement);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPlacement(IntPtr window, [In] NativePlacement placement);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect([In] ref NativeRect rect, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, [In, Out] MonitorInfo info);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")]
        private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data);
        private const uint NearestMonitor = 2, NoZOrder = 0x4, NoActivate = 0x10;
        internal const double MinimumWidth = 800, MinimumHeight = 560;

        // Work area and result are in device-independent pixels. Screens too small for a comfortable window open maximized.
        internal static Size Initial(double workWidth, double workHeight, out bool maximize)
        {
            maximize = workWidth < 1200 || workHeight < 740;
            double share = maximize ? 0.92 : 0.86;
            double width = Math.Floor(Math.Min(1560, workWidth * share)), height = Math.Floor(Math.Min(980, workHeight * (maximize ? 0.92 : 0.9)));
            return new Size(Math.Min(workWidth, Math.Max(Math.Min(MinimumWidth, workWidth), width)), Math.Min(workHeight, Math.Max(Math.Min(MinimumHeight, workHeight), height)));
        }

        // True when Windows renders this process per monitor, so the window stays sharp when moved between screens with different scaling.
        internal static bool PerMonitorAware()
        {
            try
            {
                return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) == 2;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        // Every monitor as pixels and Windows scaling, for the diagnostics bundle: "3840x2160 150%; 1920x1080 100% · per-monitor DPI".
        internal static string Describe()
        {
            var screens = new List<string>();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref NativeRect bounds, IntPtr data) =>
                {
                    uint x, y;
                    screens.Add((bounds.Right - bounds.Left) + "x" + (bounds.Bottom - bounds.Top) + (GetDpiForMonitor(monitor, 0, out x, out y) == 0 ? " " + (x * 100 / 96) + "%" : ""));
                    return true;
                }, IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            return (screens.Count > 0 ? string.Join("; ", screens) : "unknown") + (PerMonitorAware() ? " · per-monitor DPI" : " · system DPI");
        }

        // Window bounds in device pixels for the monitor the window is on; false when Windows cannot report the monitor.
        internal static bool Plan(Window window, out Int32Rect bounds, out Int32Rect work, out bool maximize)
        {
            bounds = work = Int32Rect.Empty;
            maximize = false;
            var handle = new WindowInteropHelper(window).Handle;
            var source = PresentationSource.FromVisual(window);
            var info = new MonitorInfo();
            if (handle == IntPtr.Zero || source == null || source.CompositionTarget == null || !GetMonitorInfo(MonitorFromWindow(handle, NearestMonitor), info))
                return false;
            work = new Int32Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            if (work.Width <= 0 || work.Height <= 0)
                return false;
            var device = source.CompositionTarget.TransformToDevice;
            double scaleX = device.M11 > 0 ? device.M11 : 1, scaleY = device.M22 > 0 ? device.M22 : 1;
            var size = Initial(work.Width / scaleX, work.Height / scaleY, out maximize);
            int width = Math.Min(work.Width, (int)Math.Round(size.Width * scaleX)), height = Math.Min(work.Height, (int)Math.Round(size.Height * scaleY));
            bounds = new Int32Rect(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
            window.MinWidth = Math.Min(MinimumWidth, work.Width / scaleX);
            window.MinHeight = Math.Min(MinimumHeight, work.Height / scaleY);
            return true;
        }

        // Normal bounds as left, top, right, bottom in Windows workspace pixels; maximized also covers a window minimized from maximized.
        internal static int[] Remember(Window window, out bool maximized)
        {
            maximized = false;
            var placement = new NativePlacement();
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !GetWindowPlacement(handle, placement))
                return null;
            maximized = placement.ShowCommand == 3 || placement.ShowCommand == 2 && (placement.Flags & 2) != 0;
            return new[] { placement.Normal.Left, placement.Normal.Top, placement.Normal.Right, placement.Normal.Bottom };
        }

        // Saved bounds are used only while they still land on a connected monitor: an unplugged screen must not hide the window.
        internal static bool Plausible(int[] bounds)
        {
            if (bounds == null || bounds.Length != 4)
                return false;
            var rect = new NativeRect { Left = bounds[0], Top = bounds[1], Right = bounds[2], Bottom = bounds[3] };
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            return width >= 320 && height >= 240 && width <= 32768 && height <= 32768 && MonitorFromRect(ref rect, 0) != IntPtr.Zero;
        }

        internal static bool Restore(Window window, int[] bounds, bool maximized)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !Plausible(bounds))
                return false;
            // SW_HIDE keeps the window invisible until WPF shows it. The second call runs once the window is on its monitor,
            // so a DPI change on the way there cannot rescale the remembered size.
            var placement = new NativePlacement { ShowCommand = 0, Normal = new NativeRect { Left = bounds[0], Top = bounds[1], Right = bounds[2], Bottom = bounds[3] } };
            if (!SetWindowPlacement(handle, placement) || !SetWindowPlacement(handle, placement))
                return false;
            if (maximized)
                window.WindowState = WindowState.Maximized;
            return true;
        }

        // Runs once the window handle exists and before it is shown, so the first frame already has its final size.
        internal static void Apply(Window window)
        {
            Int32Rect bounds, work;
            bool maximize;
            if (!Plan(window, out bounds, out work, out maximize))
                return;
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, NoZOrder | NoActivate);
            if (maximize)
                window.WindowState = WindowState.Maximized;
        }
    }
}
