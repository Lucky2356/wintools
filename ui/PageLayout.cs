using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        // On a wide screen the details and actions of the chosen row move from under the list into a side pane:
        // the list keeps its full height and the details keep a readable width. Narrow windows keep the stacked layout.
        private Border SidePane(Grid root, int listRow, params FrameworkElement[] parts)
        {
            var pane = new StackPanel();
            var card = new Border
            {
                Child = pane,
                Padding = new Thickness(18, 16, 18, 8),
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed
            };
            Card(card);
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            foreach (UIElement child in root.Children)
                if (Grid.GetRow(child) < listRow)
                    Grid.SetColumnSpan(child, 2);
            Grid.SetColumn(card, 1);
            Grid.SetRow(card, listRow);
            Grid.SetRowSpan(card, Math.Max(1, root.RowDefinitions.Count - listRow));
            root.Children.Add(card);
            var rows = parts.Select(Grid.GetRow).ToArray();
            var margins = parts.Select(p => p.Margin).ToArray();
            bool wide = false;
            root.SizeChanged += (s, e) =>
            {
                bool next = root.ActualWidth >= 1200;
                root.ColumnDefinitions[1].Width = new GridLength(next ? Math.Min(480, Math.Max(360, root.ActualWidth * 0.3)) : 0);
                if (next == wide)
                    return;
                wide = next;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (wide)
                    {
                        root.Children.Remove(parts[i]);
                        parts[i].Margin = new Thickness(0, 0, 0, 8);
                        pane.Children.Add(parts[i]);
                    }
                    else
                    {
                        pane.Children.Remove(parts[i]);
                        parts[i].Margin = margins[i];
                        Grid.SetRow(parts[i], rows[i]);
                        root.Children.Add(parts[i]);
                    }
                }

                card.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            };
            return card;
        }
    }
}
