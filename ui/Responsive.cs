using System;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private Grid settingsCards;
        private void InitializeResponsive()
        {
            var settings = Get<ScrollViewer>("SettingsPage");
            var original = (StackPanel)settings.Content;
            settingsCards = new Grid();
            settingsCards.ColumnDefinitions.Add(new ColumnDefinition());
            settingsCards.ColumnDefinitions.Add(new ColumnDefinition());
            while (original.Children.Count > 0)
            {
                var child = original.Children[0];
                original.Children.RemoveAt(0);
                settingsCards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                settingsCards.Children.Add(child);
            }

            settings.Content = settingsCards;
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

        // Narrow windows collapse navigation to an icon rail; short windows drop secondary text before shrinking content.
        private void AdaptLayout()
        {
            double width = Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width, height = Window.ActualHeight > 0 ? Window.ActualHeight : Window.Height;
            bool rail = width < 1000, dense = height < 820, tight = rail || dense;
            LayoutResource("NavLabelVisibility", rail ? Visibility.Collapsed : Visibility.Visible);
            LayoutResource("NavHeaderVisibility", tight ? Visibility.Collapsed : Visibility.Visible);
            LayoutResource("NavSeparatorVisibility", tight ? Visibility.Visible : Visibility.Collapsed);
            LayoutResource("NavItemHeight", dense ? 30.0 : 38.0);
            Get<Grid>("Body").ColumnDefinitions[0].Width = new GridLength(rail ? 64 : 236);
            Get<Border>("Sidebar").Padding = rail ? new Thickness(8, 12, 8, 8) : new Thickness(12, dense ? 12 : 18, 12, 12);
            var brand = Get<FrameworkElement>("Brand");
            brand.Visibility = height >= 600 ? Visibility.Visible : Visibility.Collapsed;
            brand.HorizontalAlignment = rail ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            brand.Margin = rail ? new Thickness(0, 0, 0, 8) : new Thickness(6, 0, 0, dense ? 6 : 10);
            Visible("SidebarFooter", !rail && height >= 960);
            var main = Get<Grid>("Workspace");
            main.Margin = tight ? new Thickness(16, 12, 16, 10) : new Thickness(32, 22, 32, 16);
            Get<FrameworkElement>("PageHeader").Margin = new Thickness(0, 0, 0, dense ? 10 : 20);
            Get<TextBlock>("PageTitle").FontSize = dense ? 22 : 28;
            Visible("PageEyebrow", !dense);
            Visible("PageHint", !dense);
            double available = Math.Max(0, width - (rail ? 64 : 236) - main.Margin.Left - main.Margin.Right);
            LayoutResource("BrowseColumns", available >= 1300 ? 3 : available >= 640 ? 2 : 1);
            if (collectionCards != null)
                collectionCards.Columns = main.ActualWidth >= 1500 ? 3 : main.ActualWidth >= 1000 ? 2 : 1;
            if (settingsCards != null)
            {
                bool wide = main.ActualWidth >= 1000;
                settingsCards.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                for (int i = 0; i < settingsCards.Children.Count; i++)
                {
                    var card = (Border)settingsCards.Children[i];
                    Grid.SetColumn(card, wide ? i % 2 : 0);
                    Grid.SetRow(card, wide ? i / 2 : i);
                    card.Padding = new Thickness(tight ? 16 : 22);
                    card.Margin = new Thickness(0, 0, wide && i % 2 == 0 ? 12 : 0, 12);
                }
            }

            Get<Grid>("CatalogueResults").ColumnDefinitions[1].Width = new GridLength(tight ? 10 : 16);
            Get<Border>("ActionCard").Padding = tight ? new Thickness(14, 12, 14, 12) : new Thickness(20, 18, 20, 18);
            Get<TextBlock>("ActionTitle").FontSize = tight ? 16 : 19;
            Visible("ActionLabel", !tight);
            Get<TextBlock>("Metadata").Margin = tight ? new Thickness(0, 4, 0, 8) : new Thickness(0, 6, 0, 14);
            Get<Border>("Console").Margin = new Thickness(0, dense ? 8 : 14, 0, 0);
            Get<TextBox>("Output").Height = dense ? 48 : 110;
        }
    }
}
