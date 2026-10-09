using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Wintools
{
    // Maps a status or result text to a palette key, so errors, warnings and successes never share one colour.
    internal static class StatusTone
    {
        private static readonly string[] Fine = { "без ошибок", "ошибок нет", "ошибок не", "с ошибкой: 0", "no errors", "errors: 0" };
        private static readonly string[] Problems = { "не удалось", "ошибк", "не сохран", "невозможно", "не создан", "не завершил", "failed", "failure", "could not", "error", "cannot", "not saved", "not created" };
        private static readonly string[] Cautions = { "требует внимания", "нуждается во внимании", "не подтвержден", "прервано", "не завершено", "отменен", "отменён", "отложен", "прочитайте отчёт", "needs attention", "not confirmed", "interrupted", "not finished", "cancelled", "canceled", "postponed", "read the report" };
        private static readonly string[] Quiet = { "откат выполнен", "удалено из истории", "ручной откат", "прошлый статус", "rolled back", "removed from history", "manual rollback", "previous status" };

        internal static string Of(string text)
        {
            var value = (text ?? "").ToLowerInvariant();
            if (!Fine.Any(value.Contains) && Problems.Any(value.Contains))
                return "Danger";
            if (Cautions.Any(value.Contains))
                return "Warning";
            if (Quiet.Any(value.Contains))
                return "Muted";
            return "Success";
        }
    }

    internal sealed partial class MainWindow
    {
        private int statusPage = -1, statusStamp;

        // Every status message belongs to the page it was written for; the dot shows work, success or a problem.
        private void SetStatus(string value)
        {
            Get<TextBlock>("Status").Text = value;
            statusPage = page;
            statusStamp = Environment.TickCount;
            UpdateStatusDot();
            AnnounceStatus();
        }

        private void UpdateStatusDot()
        {
            var dot = Get<Ellipse>("StatusDot");
            if (busy)
            {
                dot.SetResourceReference(Shape.FillProperty, "Accent");
                // The pulse is decoration: it stays off in the light interface and when Windows animations are off.
                if (!Motion)
                {
                    dot.Tag = null;
                    dot.BeginAnimation(UIElement.OpacityProperty, null);
                }
                else if (dot.Tag == null)
                {
                    dot.Tag = "pulse";
                    dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                }

                return;
            }

            dot.Tag = null;
            dot.BeginAnimation(UIElement.OpacityProperty, null);
            string tone = StatusTone.Of(Get<TextBlock>("Status").Text);
            dot.SetResourceReference(Shape.FillProperty, tone == "Muted" ? "Success" : tone);
            // A failure offers its next steps right where it is reported.
            Visible("ReportProblem", tone == "Danger");
        }

        // A message written just before switching pages travels with the switch (it describes the result there);
        // an older message from another page is replaced so it cannot be mistaken for this page's state.
        private void StatusForPage(int index)
        {
            if (busy || statusPage == index)
                return;
            if (unchecked(Environment.TickCount - statusStamp) < 500)
            {
                statusPage = index;
                return;
            }

            SetStatus(Lang.T("Готово к работе"));
        }
    }
}
