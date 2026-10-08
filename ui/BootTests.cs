using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private const string BootXml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>100</EventID></System><EventData><Data Name='BootTsVersion'>2</Data><Data Name='BootTime'>{0}</Data><Data Name='MainPathBootTime'>{1}</Data><Data Name='BootPostBootTime'>{2}</Data></EventData></Event>";
        private const string DelayXml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>101</EventID></System><EventData><Data Name='Name'>C:\\Program Files\\Updater\\updater.exe</Data><Data Name='FriendlyName'>{0}</Data><Data Name='TotalTime'>{1}</Data><Data Name='DegradationTime'>{2}</Data></EventData></Event>";
        private async Task BootSmoke()
        {
            var now = DateTime.UtcNow;
            var boot = BootPerformance.ParseBoot(string.Format(BootXml, 42000, 30000, 12000), now);
            var delay = BootPerformance.ParseDelay(101, string.Format(DelayXml, "Updater Service", 9000, 4000), now);
            var report = new BootReport
            {
                Boots = new[]
                {
                    boot,
                    new BootRecord
                    {
                        TimeUtc = now.AddDays(-1).ToString("o"),
                        TotalMs = 60000,
                        MainPathMs = 40000,
                        PostBootMs = 20000
                    }
                },
                Delays = new[]
                {
                    delay,
                    BootPerformance.ParseDelay(101, string.Format(DelayXml, "Updater Service", 8000, 2000), now.AddDays(-1)),
                    BootPerformance.ParseDelay(103, string.Format(DelayXml, "Print Spooler", 500, 300), now)
                }
            };
            var direct = bootReadDirect;
            var elevated = bootReadElevated;
            int elevations = 0;
            try
            {
                bootReadDirect = () =>
                {
                    throw new UnauthorizedAccessException();
                };
                bootReadElevated = () =>
                {
                    elevations++;
                    return Task.FromResult(report);
                };
                ShowPage(6);
                await ReadBoot();
                Assert(elevations == 1 && bootSummary.Text.Contains("42,0 с") && bootCulprits.Items.Count == 2, "Elevated boot read not used or shown");
                SetBusy(true);
                Assert(!bootRead.IsEnabled, "Boot analysis ignored operation lock");
                SetBusy(false);
                bootReadDirect = () => new BootReport();
                await ReadBoot();
                Assert(elevations == 1 && bootSummary.Text.Contains("ещё не записала") && bootCulprits.Items.Count == 0, "Empty boot log not explained");
                bootReadDirect = () =>
                {
                    throw new UnauthorizedAccessException();
                };
                bootReadElevated = () =>
                {
                    throw new IOException("Тест отказа UAC");
                };
                await ReadBoot();
                Assert(bootSummary.Text.Contains("Тест отказа UAC") && bootRead.IsEnabled, "Elevation failure not reported");
                bootReadDirect = () => report;
                await ReadBoot();
                bootRead.BringIntoView();
                Window.UpdateLayout();
                await Task.Delay(80);
                Capture("portable-ui-boot.png");
            }
            finally
            {
                bootReadDirect = direct;
                bootReadElevated = elevated;
            }

            ShowPage(0);
        }
    }
}
