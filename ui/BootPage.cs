using System;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private TextBlock bootSummary, bootStatus;
        private ItemsControl bootCulprits;
        private Button bootRead;
        private bool readingBoot;
        private BootReport bootReport;
        private Func<BootReport> bootReadDirect = BootPerformance.Read;
        private Func<Task<BootReport>> bootReadElevated = BootPerformance.ReadElevated;
        private void InitializeBoot(StackPanel parent)
        {
            var panel = new StackPanel();
            var card = new Border
            {
                Child = panel,
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Card(card);
            parent.Children.Add(card);
            var heading = Paragraph(Lang.T("Время загрузки Windows"));
            heading.FontSize = 21;
            heading.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(heading);
            panel.Children.Add(Paragraph(Lang.T("Windows сама измеряет каждое включение и записывает, какие программы, службы, драйверы и устройства его замедлили. Читаем этот журнал без изменений. Журнал обычно доступен только администратору, поэтому Windows может запросить подтверждение.")));
            bootRead = ToolButton(panel, Lang.T("Проанализировать загрузку"), async () => await ReadBoot());
            bootSummary = Paragraph("");
            panel.Children.Add(bootSummary);
            bootCulprits = new ItemsControl
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            bootCulprits.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Margin='0,0,0,10'><TextBlock Text='{Binding Title}' FontWeight='SemiBold' TextWrapping='Wrap'/><TextBlock Text='{Binding Detail}' Foreground='{DynamicResource Muted}' FontSize='12' TextWrapping='Wrap'/></StackPanel></DataTemplate>");
            panel.Children.Add(bootCulprits);
            bootStatus = Paragraph(Lang.T("Время включает запуск ядра, драйверов, служб, вход и фоновый запуск программ после входа. Сравнивайте загрузки с одинаковыми условиями: обновления Windows и первая загрузка после установки драйверов всегда дольше."));
            bootStatus.FontSize = 12;
            panel.Children.Add(bootStatus);
        }

        private void RefreshBootEnabled()
        {
            if (bootRead != null)
                bootRead.IsEnabled = !busy && !readingBoot;
        }

        private async Task ReadBoot()
        {
            if (busy || readingBoot)
                return;
            readingBoot = true;
            RefreshBootEnabled();
            bootSummary.Text = Lang.T("Читаем журнал загрузки…");
            try
            {
                BootReport report = null;
                bool elevate = false;
                try
                {
                    var direct = bootReadDirect;
                    report = await Task.Run(() => direct());
                }
                catch (UnauthorizedAccessException)
                {
                    elevate = true;
                }
                catch (EventLogNotFoundException)
                {
                    throw new System.IO.IOException(Lang.T("В этой версии Windows журнал загрузки отсутствует."));
                }
                catch (EventLogException)
                {
                    elevate = true;
                }

                if (elevate)
                {
                    bootSummary.Text = Lang.T("Журнал доступен только администратору. Подтвердите запрос Windows…");
                    report = await bootReadElevated();
                }

                if (closed)
                    return;
                bootReport = report;
                bootSummary.Text = BootPerformance.Summary(report);
                var culprits = BootPerformance.Culprits(report);
                bootCulprits.ItemsSource = culprits;
                if (report.Boots.Length > 0)
                    bootSummary.Text += culprits.Length == 0 ? Lang.T("\nWindows не отмечала программ и устройств, замедляющих загрузку.") : Lang.T("\nЧто замедляло загрузку (по оценке Windows, сначала наибольший суммарный эффект):");
            }
            catch (Exception ex)
            {
                bootReport = null;
                bootCulprits.ItemsSource = null;
                bootSummary.Text = Lang.T("Не удалось прочитать журнал загрузки: ") + ex.Message;
            }
            finally
            {
                readingBoot = false;
                if (!closed)
                    RefreshBootEnabled();
            }
        }
    }
}
