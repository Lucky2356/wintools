using System;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private CardFlow settingsCards;
        // Set only while the smoke run draws a page at a monitor size larger than the runner screen.
        private double layoutZoom;
        private void InitializeResponsive()
        {
            var settings = Get<ScrollViewer>("SettingsPage");
            var original = (StackPanel)settings.Content;
            settingsCards = new CardFlow(440, 3, false);
            while (original.Children.Count > 0)
            {
                var child = original.Children[0];
                original.Children.RemoveAt(0);
                settingsCards.Children.Add(child);
            }

            settings.Content = settingsCards;
            // Catalogue cards fill the list in as many columns as fit; the item width follows the list, not the window.
            var items = Get<ListBox>("Items");
            items.SizeChanged += (s, e) =>
            {
                double room = Math.Max(0, items.ActualWidth - 14);
                LayoutResource("CatalogueItemWidth", Math.Floor(room / CardFlow.Fit(room, 400, 3, 0)));
            };
            var area = SystemParameters.WorkArea;
            Window.MinWidth = Math.Min(800, area.Width);
            Window.MinHeight = Math.Min(560, area.Height);
            Window.Width = Math.Min(Window.Width, area.Width);
            Window.Height = Math.Min(Window.Height, area.Height);
            Window.SizeChanged += (s, e) => AdaptLayout();
            Window.Loaded += (s, e) => AdaptLayout();
            AdaptLayout();
        }

        // Changing a resource walks the whole visual tree, so resizing only writes values that differ.
        private void LayoutResource(string key, object value)
        {
            if (!object.Equals(Window.Resources[key], value))
                Window.Resources[key] = value;
        }

        // Narrow windows collapse navigation to an icon rail; short windows drop secondary text before shrinking content;
        // wide windows get roomier margins and more columns instead of empty space.
        private void AdaptLayout()
        {
            double width = Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width, height = Window.ActualHeight > 0 ? Window.ActualHeight : Window.Height;
            // With larger text the layout sees a proportionally smaller window and switches to its compact forms on its own.
            double zoom = ApplyTextScale(width, height);
            width /= zoom;
            height /= zoom;
            bool rail = width < 1000, dense = height < 820, tight = rail || dense, roomy = width >= 1700 && !dense;
            LayoutResource("NavLabelVisibility", rail ? Visibility.Collapsed : Visibility.Visible);
            LayoutResource("NavHeaderVisibility", tight ? Visibility.Collapsed : Visibility.Visible);
            LayoutResource("NavSeparatorVisibility", tight ? Visibility.Visible : Visibility.Collapsed);
            LayoutResource("NavItemHeight", dense ? 32.0 : 36.0);
            double navWidth = rail ? 64 : roomy ? 300 : 272;
            Get<Grid>("Body").ColumnDefinitions[0].Width = new GridLength(navWidth);
            // The icon rail is too narrow for a scrollbar; the wheel still scrolls it.
            Get<ScrollViewer>("NavScroll").VerticalScrollBarVisibility = rail ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
            Get<Border>("Sidebar").Padding = rail ? new Thickness(8, 12, 8, 8) : new Thickness(10, dense ? 10 : 14, 10, 10);
            var brand = Get<FrameworkElement>("Brand");
            brand.Visibility = height >= 600 ? Visibility.Visible : Visibility.Collapsed;
            brand.HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            brand.Margin = rail ? new Thickness(0, 0, 0, 10) : new Thickness(8, 0, 0, dense ? 8 : 14);
            Get<Button>("NavSearch").Margin = rail ? new Thickness(0, 0, 0, 8) : new Thickness(2, 0, 2, dense ? 6 : 10);
            Visible("SidebarFooter", !rail && height >= 960);
            var main = Get<Grid>("Workspace");
            double side = tight ? 20 : roomy ? 48 : 36;
            main.Margin = tight ? new Thickness(side, 14, side, 10) : new Thickness(side, roomy ? 32 : 26, side, 14);
            Get<Border>("Console").Padding = new Thickness(side, 0, side - 8, 0);
            Get<FrameworkElement>("PageHeader").Margin = new Thickness(0, 0, 0, dense ? 12 : 22);
            Get<TextBlock>("PageTitle").FontSize = dense ? 22 : 28;
            Visible("PageHint", !dense);
            double available = Math.Max(0, width - navWidth - side * 2);
            LayoutResource("BrowseColumns", available >= 1700 ? 4 : available >= 1100 ? 3 : available >= 640 ? 2 : 1);
            // Wide screens keep the action details at a readable width and give the rest to the list, which then shows several columns.
            bool split = available >= 1300;
            double detail = split ? Math.Min(520, Math.Max(400, available * 0.3)) : 0;
            var results = Get<Grid>("CatalogueResults");
            results.ColumnDefinitions[1].Width = new GridLength(tight ? 10 : 16);
            results.ColumnDefinitions[0].Width = new GridLength(split ? 1 : 1.1, GridUnitType.Star);
            results.ColumnDefinitions[2].Width = split ? new GridLength(detail) : new GridLength(1, GridUnitType.Star);
            Get<Border>("ActionCard").Padding = tight ? new Thickness(14, 12, 14, 12) : new Thickness(20, 18, 20, 18);
            Get<TextBlock>("ActionTitle").FontSize = tight ? 16 : 20;
            Visible("ActionLabel", !tight);
            Get<TextBlock>("Metadata").Margin = tight ? new Thickness(0, 4, 0, 6) : new Thickness(0, 6, 0, 8);
            Get<FrameworkElement>("Facts").Margin = tight ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 0, 12);
            Get<TextBox>("Output").Height = dense ? 64 : 140;
        }
    }
}
