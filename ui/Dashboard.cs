using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Wintools
{
    // Plain-language verdict for the dashboard; the most pressing problem wins.
    internal sealed class DashboardAdvice
    {
        internal string Headline, Detail;
        internal bool Problem;

        internal static DashboardAdvice For(double? cpu, double? memory, double? diskFree, double uptimeDays)
        {
            if (diskFree.HasValue && diskFree.Value < 10)
                return new DashboardAdvice { Problem = true, Headline = Lang.T("Мало места на системном диске"), Detail = Lang.T("Освободите место: «Обслуживание» → «Очистка». Windows работает медленнее, когда диск почти заполнен.") };
            if (memory.HasValue && memory.Value >= 90)
                return new DashboardAdvice { Problem = true, Headline = Lang.T("Оперативная память почти заполнена"), Detail = Lang.T("Закройте лишние программы или посмотрите, что занимает память, в разделе «Процессы».") };
            if (cpu.HasValue && cpu.Value >= 90)
                return new DashboardAdvice { Problem = true, Headline = Lang.T("Процессор сильно загружен"), Detail = Lang.T("Если это не ваша задача, найдите нагружающую программу в разделе «Процессы».") };
            if (uptimeDays >= 7)
                return new DashboardAdvice { Problem = true, Headline = Lang.T("Компьютер давно не перезагружался"), Detail = Lang.T("Перезагрузка завершит установку обновлений и освободит память.") };
            return new DashboardAdvice { Headline = Lang.T("Всё в порядке"), Detail = Lang.T("Показатели обновляются каждые 2 секунды, пока открыта эта страница.") };
        }

        // Green below 60 %, amber below 85 %, red above.
        internal static string Level(double percent)
        {
            return percent < 60 ? "Success" : percent < 85 ? "Warning" : "Danger";
        }
    }

    internal sealed partial class MainWindow
    {
        private sealed class Gauge
        {
            internal System.Windows.Shapes.Path Arc;
            internal TextBlock Value, Detail;
        }

        private const double RingSize = 76, RingStroke = 8;
        private readonly ResourceReader dashboardReader = new ResourceReader();
        private Gauge cpuGauge, memoryGauge, diskGauge, uptimeGauge;
        private TextBlock dashboardHeadline, dashboardDetail, dashboardMark;
        private Border dashboardBadge;

        // A ring from 12 o'clock clockwise; full and empty rings are special-cased because an arc cannot end where it starts.
        internal static Geometry RingGeometry(double fraction, double size, double stroke)
        {
            double radius = (size - stroke) / 2, center = size / 2;
            fraction = Math.Max(0, Math.Min(1, fraction));
            if (fraction <= 0.001)
                return Geometry.Empty;
            if (fraction >= 0.999)
                return new EllipseGeometry(new Point(center, center), radius, radius);
            double angle = fraction * 2 * Math.PI;
            var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
            figure.Segments.Add(new ArcSegment(new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)), new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private Gauge AddGauge(Panel row, string title, string icon)
        {
            var tile = new Border
            {
                Width = 272,
                Padding = new Thickness(16, 14, 16, 14),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 12, 12)
            };
            tile.SetResourceReference(Border.BackgroundProperty, "Raised");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            tile.Child = grid;
            var ring = new Grid { Width = RingSize, Height = RingSize, VerticalAlignment = VerticalAlignment.Center };
            var track = new Ellipse { StrokeThickness = RingStroke };
            track.SetResourceReference(Shape.StrokeProperty, "Border");
            ring.Children.Add(track);
            var arc = new System.Windows.Shapes.Path { StrokeThickness = RingStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Data = Geometry.Empty };
            arc.SetResourceReference(Shape.StrokeProperty, "Accent");
            ring.Children.Add(arc);
            var value = new TextBlock { Text = "—", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            ring.Children.Add(value);
            grid.Children.Add(ring);
            var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph = new TextBlock { Text = icon, FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            heading.Children.Add(glyph);
            var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(name, 1);
            heading.Children.Add(name);
            text.Children.Add(heading);
            var detail = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            text.Children.Add(detail);
            grid.Children.Add(text);
            row.Children.Add(tile);
            return new Gauge { Arc = arc, Value = value, Detail = detail };
        }

        private void InitializeDashboard(Panel panel)
        {
            var card = new Border
            {
                Padding = new Thickness(22, 20, 10, 8),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Card(card);
            var content = new StackPanel();
            card.Child = content;
            var head = new Grid { Margin = new Thickness(0, 0, 12, 16) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            dashboardBadge = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), VerticalAlignment = VerticalAlignment.Center };
            dashboardBadge.SetResourceReference(Border.BackgroundProperty, "Selection");
            dashboardMark = new TextBlock { Text = "", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            dashboardMark.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            dashboardMark.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            dashboardBadge.Child = dashboardMark;
            head.Children.Add(dashboardBadge);
            var words = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(words, 1);
            dashboardHeadline = new TextBlock { Text = Lang.T("Читаем показатели…"), FontSize = 20, FontWeight = FontWeights.SemiBold };
            dashboardHeadline.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            words.Children.Add(dashboardHeadline);
            dashboardDetail = new TextBlock { Margin = new Thickness(0, 4, 0, 0), LineHeight = 21, TextWrapping = TextWrapping.Wrap };
            dashboardDetail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            words.Children.Add(dashboardDetail);
            head.Children.Add(words);
            content.Children.Add(head);
            var row = new WrapPanel();
            content.Children.Add(row);
            cpuGauge = AddGauge(row, Lang.T("Процессор"), "");
            memoryGauge = AddGauge(row, Lang.T("Память"), "");
            diskGauge = AddGauge(row, Lang.T("Системный диск"), "");
            uptimeGauge = AddGauge(row, Lang.T("Без перезагрузки"), "");
            panel.Children.Add(card);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) =>
            {
                if (closed)
                    timer.Stop();
                else if (page == 6 && Window.WindowState != WindowState.Minimized)
                    UpdateDashboard();
            };
            timer.Start();
            UpdateDashboard();
        }

        private void ShowGauge(Gauge gauge, double? percent, string value, string detail, string level)
        {
            gauge.Value.Text = value;
            gauge.Detail.Text = detail;
            gauge.Arc.Data = percent.HasValue ? RingGeometry(percent.Value / 100, RingSize, RingStroke) : Geometry.Empty;
            gauge.Arc.SetResourceReference(Shape.StrokeProperty, level);
        }

        private void UpdateDashboard()
        {
            ResourceSample sample;
            try
            {
                sample = dashboardReader.Read(null, false);
            }
            catch (Exception)
            {
                sample = new ResourceSample();
            }

            ShowGauge(cpuGauge, sample.Cpu, sample.Cpu.HasValue ? sample.Cpu.Value.ToString("0") + "%" : "…", sample.Cpu.HasValue ? Lang.T("загрузка сейчас") : Lang.T("измеряем…"), sample.Cpu.HasValue ? DashboardAdvice.Level(sample.Cpu.Value) : "Accent");
            ShowGauge(memoryGauge, sample.Memory, sample.Memory.HasValue ? sample.Memory.Value.ToString("0") + "%" : "—", sample.TotalMemory > 0 ? ((sample.TotalMemory - sample.AvailableMemory) / 1073741824.0).ToString("0.0") + Lang.T(" из ") + (sample.TotalMemory / 1073741824.0).ToString("0.0") + Lang.T(" ГБ") : Lang.T("нет данных"), sample.Memory.HasValue ? DashboardAdvice.Level(sample.Memory.Value) : "Accent");
            double? diskUsed = null, diskFree = null;
            string diskDetail = Lang.T("нет данных");
            try
            {
                var drive = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory));
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    diskFree = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
                    diskUsed = 100 - diskFree;
                    diskDetail = Lang.T("свободно ") + (drive.AvailableFreeSpace / 1073741824.0).ToString("0") + Lang.T(" из ") + (drive.TotalSize / 1073741824.0).ToString("0") + Lang.T(" ГБ");
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            ShowGauge(diskGauge, diskUsed, diskUsed.HasValue ? diskUsed.Value.ToString("0") + "%" : "—", diskDetail, diskUsed.HasValue ? DashboardAdvice.Level(diskUsed.Value) : "Accent");
            var uptime = TimeSpan.FromMilliseconds(sample.Uptime);
            ShowGauge(uptimeGauge, Math.Min(100, uptime.TotalDays / 7 * 100), uptime.TotalDays >= 1 ? ((int)uptime.TotalDays) + Lang.T(" д") : uptime.TotalHours >= 1 ? ((int)uptime.TotalHours) + Lang.T(" ч") : ((int)uptime.TotalMinutes) + Lang.T(" мин"), Lang.T("включён ") + DateTime.Now.Subtract(uptime).ToString("dd.MM HH:mm"), uptime.TotalDays >= 7 ? "Warning" : "Accent");
            var advice = DashboardAdvice.For(sample.Cpu, sample.Memory, diskFree, uptime.TotalDays);
            dashboardHeadline.Text = advice.Headline;
            dashboardDetail.Text = advice.Detail;
            dashboardMark.Text = advice.Problem ? "" : "";
            dashboardMark.SetResourceReference(TextBlock.ForegroundProperty, advice.Problem ? "Warning" : "Success");
        }
    }
}
