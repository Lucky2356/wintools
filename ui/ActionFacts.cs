using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private static readonly string[] SignInWords = { "перезапустить проводник", "выйти и войти", "со следующего входа", "restart file explorer", "sign out", "next sign-in", "next sign in" };

        // "full": the previous value is restored exactly; "partial": an app may need reinstalling; "none": deleted files stay deleted.
        internal static string RollbackLevel(Tweak item)
        {
            return item.Category == "CLEAN" ? "none" : item.Kind == "EDGE" || item.Kind == "APPX" ? "partial" : "full";
        }

        internal static bool NeedsSignIn(Tweak item)
        {
            var text = ((item.Description ?? "") + " " + (item.Caveat ?? "")).ToLowerInvariant();
            return SignInWords.Any(text.Contains);
        }

        private static string KindLabel(Tweak item)
        {
            switch (item.Category == "SYS" || item.Category == "CLEAN" ? item.Category : item.Kind)
            {
                case "REG":
                    return Lang.T("Параметр Windows");
                case "SVC":
                    return Lang.T("Служба Windows");
                case "TASK":
                    return Lang.T("Задача планировщика");
                case "APPX":
                    return Lang.T("Встроенное приложение");
                case "EDGE":
                    return "Microsoft Edge";
                case "SYS":
                    return Lang.T("Системная команда");
                case "CLEAN":
                    return Lang.T("Удаление файлов");
                default:
                    return null;
            }
        }

        // Short facts under the action title: risk, what is changed, how far it can be undone and when it takes effect.
        // Each fact has an icon and words, so none of them depends on colour alone.
        private void ShowFacts(Tweak item)
        {
            var facts = Get<WrapPanel>("Facts");
            facts.Children.Clear();
            facts.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            if (item == null)
                return;
            string risk = item.Risk == "high" ? "Danger" : item.Risk == "med" ? "Warning" : "Success";
            facts.Children.Add(Fact(item.Risk == "high" ? "" : item.Risk == "med" ? "" : "", Risk(item), risk));
            var kind = KindLabel(item);
            if (kind != null)
                facts.Children.Add(Fact("", kind, "Muted"));
            string level = RollbackLevel(item);
            facts.Children.Add(Fact(level == "full" ? "" : level == "partial" ? "" : "", level == "full" ? Lang.T("Полный откат") : level == "partial" ? Lang.T("Частичный откат") : Lang.T("Без отката"), level == "full" ? "Success" : level == "partial" ? "Warning" : "Danger"));
            if (NeedsSignIn(item))
                facts.Children.Add(Fact("", Lang.T("Подействует после перезапуска Проводника или нового входа"), "Warning"));
        }

        private UIElement Fact(string glyph, string text, string tone)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, tone);
            row.Children.Add(icon);
            var words = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            words.SetResourceReference(TextBlock.ForegroundProperty, tone == "Muted" ? "Text" : tone);
            row.Children.Add(words);
            var chip = new Border { Child = row, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 9, 3), Margin = new Thickness(0, 0, 6, 6) };
            chip.SetResourceReference(Border.BackgroundProperty, "Raised");
            chip.SetResourceReference(Border.BorderBrushProperty, "Border");
            System.Windows.Automation.AutomationProperties.SetName(chip, text);
            return chip;
        }
    }
}
