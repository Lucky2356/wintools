using System;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    // Lays cards out in as many equal columns as fit, so a page fills a 4K monitor as naturally as a laptop screen.
    // Masonry puts every card under the shortest column; rows mode lines cards up and gives each row one height.
    // Children marked Wide span all columns and start below everything placed before them.
    internal sealed class CardFlow : Panel
    {
        internal static readonly DependencyProperty WideProperty = DependencyProperty.RegisterAttached("Wide", typeof(bool), typeof(CardFlow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
        private double minColumn = 420, gap = 16;
        private int maxColumns = 4;
        private bool rows;

        internal CardFlow(double minColumn, int maxColumns, bool rows)
        {
            this.minColumn = minColumn;
            this.maxColumns = maxColumns;
            this.rows = rows;
        }

        internal int Columns { get; private set; }

        internal double Gap
        {
            get
            {
                return gap;
            }

            set
            {
                gap = value;
                InvalidateMeasure();
            }
        }

        internal double MinColumn
        {
            get
            {
                return minColumn;
            }

            set
            {
                minColumn = value;
                InvalidateMeasure();
            }
        }

        internal static void SetWide(UIElement element, bool value)
        {
            element.SetValue(WideProperty, value);
        }

        internal static bool GetWide(UIElement element)
        {
            return (bool)element.GetValue(WideProperty);
        }

        internal static int Fit(double width, double minColumn, int maxColumns, double gap)
        {
            return Math.Max(1, Math.Min(maxColumns, (int)Math.Floor((width + gap) / (minColumn + gap))));
        }

        // The flow owns the spacing between cards, so margins meant for older grids do not double the gaps.
        protected override void OnVisualChildrenChanged(DependencyObject added, DependencyObject removed)
        {
            base.OnVisualChildrenChanged(added, removed);
            var element = added as FrameworkElement;
            if (element != null && !GetWide(element))
                element.Margin = new Thickness(0);
        }

        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? minColumn : available.Width;
            return new Size(width, Place(width, false));
        }

        protected override Size ArrangeOverride(Size final)
        {
            Place(final.Width, true);
            return final;
        }

        // One pass serves both measuring and arranging; it returns the total height.
        private double Place(double width, bool arrange)
        {
            int columns = Fit(width, minColumn, maxColumns, gap);
            // Rows of equal cards never leave one orphan: four cards in three columns become two rows of two.
            if (rows)
            {
                int count = 0;
                foreach (UIElement child in InternalChildren)
                    if (child.Visibility != Visibility.Collapsed && !GetWide(child))
                        count++;
                if (count > 0 && count < columns * 4)
                    columns = Math.Max(1, (int)Math.Ceiling(count / Math.Ceiling(count / (double)columns)));
            }

            Columns = columns;
            double column = Math.Max(0, (width - gap * (columns - 1)) / columns), top = 0;
            var heights = new double[columns];
            int next = 0;
            double rowTop = 0, rowHeight = 0;
            bool trailingGap = false;
            var row = new UIElement[columns];
            Action closeRow = () =>
            {
                if (next == 0)
                    return;
                if (arrange)
                    for (int i = 0; i < next; i++)
                        row[i].Arrange(new Rect(i * (column + gap), rowTop, column, rowHeight));
                top = rowTop + rowHeight + gap;
                trailingGap = true;
                rowTop = top;
                rowHeight = 0;
                next = 0;
                Array.Clear(row, 0, row.Length);
            };
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    if (!arrange)
                        child.Measure(new Size(0, 0));
                    continue;
                }

                if (GetWide(child))
                {
                    if (rows)
                        closeRow();
                    else
                        top = Math.Max(top, Max(heights));
                    if (!arrange)
                        child.Measure(new Size(width, double.PositiveInfinity));
                    else
                        child.Arrange(new Rect(0, top, width, child.DesiredSize.Height));
                    top += child.DesiredSize.Height;
                    trailingGap = false;
                    rowTop = top;
                    for (int i = 0; i < columns; i++)
                        heights[i] = top;
                    continue;
                }

                if (!arrange)
                    child.Measure(new Size(column, double.PositiveInfinity));
                if (rows)
                {
                    row[next++] = child;
                    rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                    if (next == columns)
                        closeRow();
                    continue;
                }

                int shortest = 0;
                for (int i = 1; i < columns; i++)
                    if (heights[i] < heights[shortest] - 0.5)
                        shortest = i;
                double y = Math.Max(heights[shortest], top);
                if (arrange)
                    child.Arrange(new Rect(shortest * (column + gap), y, column, child.DesiredSize.Height));
                heights[shortest] = y + child.DesiredSize.Height + gap;
            }

            if (rows)
            {
                closeRow();
                return trailingGap ? Math.Max(0, top - gap) : top;
            }

            double bottom = Math.Max(top, Max(heights));
            return bottom > top ? bottom - gap : bottom;
        }

        private static double Max(double[] values)
        {
            double result = 0;
            foreach (double value in values)
                result = Math.Max(result, value);
            return result;
        }
    }
}
