using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Wintools
{
    // Gives a list row the words a screen reader should say: the row's title and its first detail,
    // instead of the class name WPF reads for data objects without a text of their own.
    internal sealed class AccessibleName : IValueConverter
    {
        private static readonly string[] Titles = { "DisplayTitle", "Title", "Name", "Label", "Host", "Text", "Value" };
        private static readonly string[] Details = { "Summary", "Detail", "Status", "StateLabel", "RunningLabel", "Publisher", "Subtitle", "Description" };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Describe(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        internal static string Describe(object value)
        {
            if (value == null)
                return "";
            if (value is string)
                return (string)value;
            var title = Read(value, Titles);
            if (title == null)
            {
                var text = value.ToString();
                return text == value.GetType().ToString() ? "" : text;
            }

            var detail = Read(value, Details);
            return detail == null || detail == title ? title : title + ". " + detail;
        }

        private static string Read(object value, string[] names)
        {
            foreach (var name in names)
            {
                var property = value.GetType().GetProperty(name);
                if (property == null || property.PropertyType != typeof(string) || property.GetIndexParameters().Length > 0)
                    continue;
                var text = property.GetValue(value, null) as string;
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return null;
        }
    }

    internal sealed partial class MainWindow
    {
        private void InitializeAccessibility()
        {
            // List and drop-down rows read their data object; explicit names in a page's own styles still win.
            foreach (var type in new[] { typeof(ListBoxItem), typeof(ComboBoxItem) })
            {
                var style = new Style(type, Window.TryFindResource(type) as Style);
                style.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding { Converter = new AccessibleName() }));
                Window.Resources[type] = style;
            }

            // Pages build controls in code and refill lists later, so names are filled in after each page is shown.
            Window.Loaded += (s, e) => NameControls(Window);
        }

        // Fills in names that WPF cannot derive: icon-only buttons take their tooltip, tiles their texts,
        // and fields the label written just before them.
        private static void NameControls(DependencyObject root)
        {
            foreach (var element in Descendants(root).OfType<Control>())
            {
                if (!string.IsNullOrEmpty(AutomationProperties.GetName(element)) || element is ListBoxItem || element is ComboBoxItem || element is ScrollBar || element is ScrollViewer)
                    continue;
                string name = null;
                var content = element as ContentControl;
                var tip = element.ToolTip as string;
                if (content != null && !(element is Label))
                {
                    var text = content.Content as string;
                    if (text != null && Readable(text))
                        continue;
                    if (text == null && content.Content is TextBlock && Readable(((TextBlock)content.Content).Text))
                        continue;
                    name = tip ?? Words(content.Content as DependencyObject);
                }
                else if (element is TextBox || element is ComboBox || element is PasswordBox || element is ListBox)
                    name = tip ?? LabelBefore(element);
                if (!string.IsNullOrEmpty(name))
                    AutomationProperties.SetName(element, name);
            }
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            int count = root is Visual || root is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var nested in Descendants(child))
                    yield return nested;
            }
        }

        // Icon glyphs live in the private use area and say nothing when read aloud.
        internal static bool Readable(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && text.Any(c => char.IsLetterOrDigit(c) && (c < '' || c > ''));
        }

        private static string Words(DependencyObject content)
        {
            if (content == null)
                return null;
            var texts = new List<string>();
            CollectWords(content, texts);
            return texts.Count == 0 ? null : string.Join(". ", texts.Take(2));
        }

        private static void CollectWords(DependencyObject node, List<string> texts)
        {
            var block = node as TextBlock;
            if (block != null && Readable(block.Text) && block.Visibility == Visibility.Visible)
                texts.Add(block.Text.Trim());
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                CollectWords(child, texts);
        }

        // The text block that stands right before a field in its panel is the field's visible label.
        private static string LabelBefore(FrameworkElement element)
        {
            var panel = element.Parent as Panel;
            if (panel == null)
                return null;
            int index = panel.Children.IndexOf(element);
            for (int i = index - 1; i >= 0 && i >= index - 2; i--)
            {
                var label = panel.Children[i] as TextBlock;
                if (label != null && Readable(label.Text))
                    return label.Text.Trim();
            }

            return null;
        }

        // Narrator and NVDA hear each new status line once, without the focus moving to it.
        private void AnnounceStatus()
        {
            var status = Get<TextBlock>("Status");
            var peer = UIElementAutomationPeer.FromElement(status) ?? UIElementAutomationPeer.CreatePeerForElement(status);
            if (peer != null)
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        // Every visible control on every page must have a name that can be read aloud.
        private List<string> UnnamedControls(DependencyObject root)
        {
            var missing = new List<string>();
            foreach (var element in Descendants(root).OfType<Control>())
            {
                if (!element.IsVisible || element is ScrollBar || element is ScrollViewer || element is Label || element is RepeatButton || element is Thumb || element.TemplatedParent is ComboBox || element.TemplatedParent is ScrollBar)
                    continue;
                if (!(element is ButtonBase || element is ComboBox || element is TextBox || element is ListBox || element is ListBoxItem || element is PasswordBox || element is Slider))
                    continue;
                var peer = UIElementAutomationPeer.CreatePeerForElement(element);
                var name = peer == null ? null : peer.GetName();
                if (string.IsNullOrEmpty(name) || !Readable(name) || name.StartsWith("Wintools.") || name.StartsWith("System."))
                    missing.Add(element.GetType().Name + (string.IsNullOrEmpty(element.Name) ? "" : "#" + element.Name) + " «" + (name ?? "") + "» in " + Owner(element));
            }

            return missing;
        }

        private static string Owner(FrameworkElement element)
        {
            for (var node = VisualTreeHelper.GetParent(element) as FrameworkElement; node != null; node = VisualTreeHelper.GetParent(node) as FrameworkElement)
                if (!string.IsNullOrEmpty(node.Name))
                    return node.Name;
            return "?";
        }
    }
}
