using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace Wintools
{
    // Russian is the source language. In English mode every Russian literal passed to T is looked up in the embedded table;
    // anything without a translation stays Russian rather than disappearing.
    internal static class Lang
    {
        private static Dictionary<string, string> table = new Dictionary<string, string>();
        internal static bool English { get; private set; }

        internal static bool RussianCulture(CultureInfo culture)
        {
            var language = culture.TwoLetterISOLanguageName;
            return language == "ru" || language == "uk" || language == "be" || language == "kk";
        }

        internal static void Initialize(string preference, bool forceRussian)
        {
            English = !forceRussian && (preference == "en" || (preference != "ru" && !RussianCulture(CultureInfo.CurrentUICulture)));
            table = English ? Load() : new Dictionary<string, string>();
        }

        internal static Dictionary<string, string> Load()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Wintools.Strings.en.json"))
            {
                if (stream == null)
                    return new Dictionary<string, string>();
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return new JavaScriptSerializer { MaxJsonLength = 8388608 }.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? new Dictionary<string, string>();
            }
        }

        internal static string T(string text)
        {
            string value;
            return text != null && table.TryGetValue(text, out value) ? value : text;
        }

        // Translates texts written directly in Shell.xaml: text blocks, contents, headers, tooltips and accessible names.
        internal static void Translate(DependencyObject root)
        {
            if (!English || root == null)
                return;
            var text = root as TextBlock;
            if (text != null && text.Inlines.Count <= 1 && !BindingOperations.IsDataBound(text, TextBlock.TextProperty))
                text.Text = T(text.Text);
            var content = root as ContentControl;
            if (content != null && content.Content is string)
                content.Content = T((string)content.Content);
            var header = root as HeaderedContentControl;
            if (header != null && header.Header is string)
                header.Header = T((string)header.Header);
            var element = root as FrameworkElement;
            if (element != null && element.ToolTip is string)
                element.ToolTip = T((string)element.ToolTip);
            var name = AutomationProperties.GetName(root);
            if (!string.IsNullOrEmpty(name))
                AutomationProperties.SetName(root, T(name));
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var dependency = child as DependencyObject;
                if (dependency != null)
                    Translate(dependency);
            }
        }
    }
}
