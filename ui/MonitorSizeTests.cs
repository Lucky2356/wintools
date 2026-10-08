using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        // Draws the current page as it is laid out on a monitor of the given size in device-independent pixels.
        // The runner screen is small, so the window keeps the monitor's proportions and the layout is zoomed out to fit;
        // the picture is then rendered at full resolution.
        private async Task CaptureAt(string name, double width, double height)
        {
            var root = (FrameworkElement)Window.Content;
            Window.UpdateLayout();
            double frameWidth = Math.Max(0, Window.ActualWidth - root.ActualWidth * uiZoom), frameHeight = Math.Max(0, Window.ActualHeight - root.ActualHeight * uiZoom);
            var area = SystemParameters.WorkArea;
            double zoom = Math.Min(1, Math.Min((area.Width - frameWidth) / width, (area.Height - frameHeight) / height));
            Window.Width = Math.Round(width * zoom) + frameWidth;
            Window.Height = Math.Round(height * zoom) + frameHeight;
            Window.UpdateLayout();
            // The client area the window really got decides the zoom, so the layout sees exactly the requested size.
            layoutZoom = Math.Min((Window.ActualWidth - frameWidth) / width, (Window.ActualHeight - frameHeight) / height);
            AdaptLayout();
            Window.UpdateLayout();
            await Task.Delay(150);
            Window.UpdateLayout();
            double dpi = 96 / layoutZoom;
            var bitmap = new RenderTargetBitmap((int)Math.Round(root.ActualWidth), (int)Math.Round(root.ActualHeight), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Program.Home, name)))
                encoder.Save(file);
            Assert(Math.Abs(root.ActualWidth - width) < 24 && Math.Abs(root.ActualHeight - height) < 24, "Layout for " + width + "x" + height + " got " + root.ActualWidth + "x" + root.ActualHeight);
        }

        private void EndCaptureAt(double width, double height)
        {
            layoutZoom = 0;
            Window.Width = width;
            Window.Height = height;
            AdaptLayout();
            Window.UpdateLayout();
        }

        // Every main page on a 2K / 4K-at-150 % monitor, a Full HD monitor and an HD laptop.
        private async Task MonitorSizesSmoke()
        {
            double width = Window.Width, height = Window.Height;
            var sizes = new[]
            {
                new Size(2560, 1440),
                new Size(1920, 1080),
                new Size(1366, 768)
            };
            foreach (var size in sizes)
            {
                string tag = size.Width == 2560 ? "2k" : size.Width == 1920 ? "fhd" : "hd";
                foreach (int index in size.Width == 2560 ? new[] { HomeIndex, 6, 8, 0, 2, 4, 9, 14, 11, 10, 12, 3, 7, 1 } : new[] { HomeIndex, 6, 0, 14 })
                {
                    ShowPage(index);
                    if (index == 6)
                        ShowHealthTab(0);
                    if (index == 11)
                        integrityChoice.SelectedIndex = integrityActions.Length;
                    if (index != 0)
                    {
                        await CaptureAt("portable-ui-at-" + tag + "-" + index + ".png", size.Width, size.Height);
                    }
                    else
                    {
                        // The catalogue is drawn as sections and as the list, then left in the view it was in.
                        bool listed = Get<FrameworkElement>("CatalogueResults").IsVisible;
                        if (listed)
                            Get<Button>("BackToGroups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await CaptureAt("portable-ui-at-" + tag + "-0.png", size.Width, size.Height);
                        Get<Button>("ShowAll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Get<ListBox>("Items").SelectedIndex = 0;
                        await CaptureAt("portable-ui-at-" + tag + "-0-list.png", size.Width, size.Height);
                        if (size.Width == 2560)
                            Assert(Get<ListBox>("Items").ActualWidth > 1000 && Get<FrameworkElement>("ActionCard").ActualWidth <= 530, "Catalogue list does not take the wide screen: list " + Get<ListBox>("Items").ActualWidth + ", details " + Get<FrameworkElement>("ActionCard").ActualWidth);
                        if (!listed)
                            Get<Button>("BackToGroups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }

                    if (index == 11)
                        integrityChoice.SelectedIndex = 0;
                }

                if (size.Width == 2560)
                {
                    ShowPage(HomeIndex);
                    await CaptureAt("portable-ui-at-2k-home.png", size.Width, size.Height);
                    Assert(homeScenarios[3].TranslatePoint(new Point(0, 0), Get<Grid>("Workspace")).X > 1000, "Home scenarios do not fill a 2K screen");
                }
            }

            EndCaptureAt(width, height);
            ShowPage(0);
        }
    }
}
