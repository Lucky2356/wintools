using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private Grid sheetOverlay;
        private TextBlock sheetTitle, sheetStep;
        private ContentControl sheetBody;
        private Grid sheetFooter;
        private Action sheetCancel;

        // A content dialog in the manner of Windows 11 for the welcome, "What's new" and the keyboard help:
        // the text on the card, the answers on a footer band; Esc does what the cancel answer does.
        private void InitializeSheets()
        {
            var root = (Grid)Get<Grid>("ConfirmOverlay").Parent;
            sheetOverlay = new Grid { Visibility = Visibility.Collapsed };
            sheetOverlay.SetResourceReference(Panel.BackgroundProperty, "Scrim");
            root.Children.Insert(root.Children.IndexOf(Get<Grid>("ConfirmOverlay")), sheetOverlay);
            var frame = new Grid { MaxWidth = 680, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sheetOverlay.Children.Add(frame);
            var shadow = new Border { CornerRadius = new CornerRadius(8) };
            shadow.SetResourceReference(Border.BackgroundProperty, "Surface");
            shadow.SetResourceReference(UIElement.EffectProperty, "ShadowLarge");
            frame.Children.Add(shadow);
            var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
            card.SetResourceReference(Border.BackgroundProperty, "Surface");
            card.SetResourceReference(Border.BorderBrushProperty, "Border");
            frame.Children.Add(card);
            var dock = new DockPanel();
            card.Child = dock;
            var band = new Border { BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 8, 8), Padding = new Thickness(24, 18, 24, 18) };
            band.SetResourceReference(Border.BackgroundProperty, "Layer");
            band.SetResourceReference(Border.BorderBrushProperty, "Divider");
            DockPanel.SetDock(band, Dock.Bottom);
            dock.Children.Add(band);
            sheetFooter = new Grid();
            band.Child = sheetFooter;
            var content = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
            dock.Children.Add(content);
            sheetStep = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
            sheetStep.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(sheetStep);
            sheetTitle = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            sheetTitle.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            content.Children.Add(sheetTitle);
            sheetBody = new ContentControl { Focusable = false };
            content.Children.Add(new ScrollViewer { Content = sheetBody, MaxHeight = 460, Margin = new Thickness(0, 14, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
            Window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && SheetOpen && confirmation == null)
                {
                    var cancel = sheetCancel;
                    if (cancel != null)
                        cancel();
                    e.Handled = true;
                }
            };
        }

        private bool SheetOpen
        {
            get
            {
                return sheetOverlay != null && sheetOverlay.Visibility == Visibility.Visible;
            }
        }

        // Shows or replaces the dialog. The secondary answers sit on the left, the main one on the right.
        private void ShowSheet(string step, string title, UIElement body, Action cancel, Button[] left, Button[] right)
        {
            sheetStep.Text = step ?? "";
            sheetStep.Visibility = string.IsNullOrEmpty(step) ? Visibility.Collapsed : Visibility.Visible;
            sheetTitle.Text = title;
            System.Windows.Automation.AutomationProperties.SetName(sheetOverlay, title);
            sheetBody.Content = body;
            sheetCancel = cancel;
            sheetFooter.Children.Clear();
            sheetFooter.ColumnDefinitions.Clear();
            sheetFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sheetFooter.ColumnDefinitions.Add(new ColumnDefinition());
            sheetFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var start = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var button in left)
            {
                button.Margin = new Thickness(0, 0, 8, 0);
                start.Children.Add(button);
            }

            sheetFooter.Children.Add(start);
            var end = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(end, 2);
            foreach (var button in right)
            {
                button.Margin = new Thickness(8, 0, 0, 0);
                end.Children.Add(button);
            }

            sheetFooter.Children.Add(end);
            Get<Grid>("Body").IsEnabled = false;
            sheetOverlay.Visibility = Visibility.Visible;
            CloseGlobalSearch();
            // The main answer takes the focus, so Enter continues and Tab reaches every control in reading order.
            Window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => NameControls(sheetOverlay)));
            var main = right.Length > 0 ? right[right.Length - 1] : null;
            if (main != null)
                Window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => main.Focus()));
        }

        private void CloseSheet()
        {
            if (sheetOverlay == null)
                return;
            sheetOverlay.Visibility = Visibility.Collapsed;
            sheetBody.Content = null;
            sheetCancel = null;
            if (confirmation == null)
                Get<Grid>("Body").IsEnabled = true;
        }

        private Button SheetButton(string label, bool primary, Action click)
        {
            var button = new Button { Content = label, MinWidth = 120, Padding = new Thickness(16, 6, 16, 6) };
            if (primary)
                button.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
            button.Click += (s, e) => click();
            return button;
        }

        // A short paragraph for dialog bodies.
        private TextBlock SheetText(string text)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 21, Margin = new Thickness(0, 0, 0, 12) };
            return block;
        }

        // An icon with a bold lead and a muted explanation, as in the Windows "Get started" pages.
        private UIElement SheetPoint(string glyph, string lead, string detail)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var badge = new Grid { Width = 36, Height = 36, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
            var tint = new Border { CornerRadius = new CornerRadius(8), Opacity = 0.16 };
            tint.SetResourceReference(Border.BackgroundProperty, "Accent");
            badge.Children.Add(tint);
            var icon = new TextBlock { Text = glyph, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            badge.Children.Add(icon);
            row.Children.Add(badge);
            var words = new StackPanel();
            Grid.SetColumn(words, 1);
            words.Children.Add(new TextBlock { Text = lead, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var about = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 2, 0, 0) };
            about.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            words.Children.Add(about);
            row.Children.Add(words);
            return row;
        }
    }
}
