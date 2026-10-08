using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private Action<int> showHealthTab;

        // Splits a long code-built page into views with a row of chips on top, like the segments in Windows Settings;
        // only the chosen view is visible, so a page shows one topic at a time.
        private StackPanel[] Tabs(StackPanel page, out Action<int> select, params string[] names)
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            page.Children.Add(bar);
            var views = names.Select(n => new StackPanel()).ToArray();
            var chips = new CheckBox[names.Length];
            Action<int> show = index =>
            {
                for (int i = 0; i < views.Length; i++)
                {
                    chips[i].IsChecked = i == index;
                    views[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                chips[i] = new CheckBox { Content = names[i] };
                chips[i].SetResourceReference(FrameworkElement.StyleProperty, "Chip");
                chips[i].Click += (s, e) => show(index);
                bar.Children.Add(chips[i]);
                page.Children.Add(views[i]);
            }

            show(0);
            select = show;
            return views;
        }

        private void ShowHealthTab(int index)
        {
            showHealthTab(index);
        }
    }
}
