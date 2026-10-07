using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private async Task DiskUsageSmoke()
        {
            string root = Path.Combine(Program.Data, "usage-fixture-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "big", "nested"));
                Directory.CreateDirectory(Path.Combine(root, "small"));
                File.WriteAllBytes(Path.Combine(root, "big", "a.bin"), new byte[3000]);
                File.WriteAllBytes(Path.Combine(root, "big", "nested", "b.bin"), new byte[2000]);
                File.WriteAllBytes(Path.Combine(root, "small", "c.bin"), new byte[100]);
                File.WriteAllBytes(Path.Combine(root, "loose.bin"), new byte[50]);
                string local = Path.Combine(root, "local");
                string chrome = Path.Combine(local, @"Google\Chrome\User Data\Default\Cache\Cache_Data");
                string profile = Path.Combine(local, @"Google\Chrome\User Data\Default");
                Directory.CreateDirectory(chrome);
                File.WriteAllBytes(Path.Combine(chrome, "data_1"), new byte[4096]);
                File.WriteAllText(Path.Combine(profile, "History"), "keep");
                string firefox = Path.Combine(local, @"Mozilla\Firefox\Profiles\abc.default\cache2\entries");
                Directory.CreateDirectory(firefox);
                File.WriteAllBytes(Path.Combine(firefox, "E1"), new byte[1024]);
                File.WriteAllText(Path.Combine(local, @"Mozilla\Firefox\Profiles\abc.default\places.sqlite"), "keep");
                var sources = BrowserCache.Sources(local);
                Assert(sources.Length == 2 && sources.Any(s => s.Browser == "Google Chrome") && sources.Any(s => s.Browser == "Firefox"), "Browser caches not found");
                bool running = sources.Any(BrowserCache.Running);
                var estimate = BrowserCache.Estimate(CancellationToken.None, local);
                if (!running)
                    Assert(estimate.Files == 2 && estimate.Bytes == 5120 && estimate.Errors == 0, "Browser cache estimate incorrect: " + estimate.Files + "/" + estimate.Bytes);
                var cleaned = BrowserCache.Clean(local);
                if (!running)
                    Assert(!File.Exists(Path.Combine(chrome, "data_1")) && !File.Exists(Path.Combine(firefox, "E1")) && Directory.Exists(chrome), "Browser cache not deleted");
                Assert(File.ReadAllText(Path.Combine(profile, "History")) == "keep" && File.Exists(Path.Combine(local, @"Mozilla\Firefox\Profiles\abc.default\places.sqlite")), "Browser profile data deleted");
                var measure = usageMeasure;
                try
                {
                    usageMeasure = (path, token) => DiskUsage.Measure(root, token, 1000);
                    ShowPage(11);
                    integrityChoice.SelectedIndex = integrityActions.Length;
                    usageScan.BringIntoView();
                    await MeasureUsage();
                    Assert(usageList.Items.Count >= 3 && (usageStatus.Text.Contains("МБ") || usageStatus.Text.Contains("ГБ")), "Disk usage not shown: " + usageStatus.Text);
                    usageList.SelectedIndex = 0;
                    Assert(usageOpen.IsEnabled, "Folder cannot be opened");
                    Window.UpdateLayout();
                    await Task.Delay(80);
                    Capture("portable-ui-disk-usage.png");
                }
                finally
                {
                    usageMeasure = measure;
                    integrityChoice.SelectedIndex = 0;
                }
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }

            ShowPage(0);
        }
    }
}
