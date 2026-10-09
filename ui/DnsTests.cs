using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal static class DnsIntegrationTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        // Changes DNS of the runner's primary adapter and restores it; the runner is discarded after the job.
        internal static void Run()
        {
            if (!Program.Hosted)
                throw new InvalidOperationException("DNS integration runs only on disposable hosted CI.");
            var adapter = DnsSettings.Read().FirstOrDefault(a => a.Gateway);
            Assert(adapter != null, "No connected adapter for DNS test");
            string[] before4 = adapter.Static4, before6 = adapter.Static6;
            try
            {
                string first = Change(adapter.Id, "quad9", adapter.Fingerprint, null, 0);
                var changed = DnsSettings.Find(adapter.Id);
                Assert(changed.Static4.SequenceEqual(new[] { "9.9.9.9", "149.112.112.112" }) && (changed.Index6 == 0 || changed.Static6.SequenceEqual(new[] { "2620:fe::fe", "2620:fe::9" })), "DNS servers not applied: " + changed.Summary);
                var record = DnsActions.Read(first);
                Assert(record.Status == "OK" && record.Before4.SequenceEqual(before4) && record.After4.SequenceEqual(changed.Static4), "DNS change not recorded");
                Change(adapter.Id, "cloudflare", adapter.Fingerprint, null, 4);
                Assert(DnsSettings.Find(adapter.Id).Static4.SequenceEqual(changed.Static4), "Stale DNS request changed Windows");
                string lockPath = Path.Combine(Program.Data, "state", "run.lock");
                using (var gate = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Change(adapter.Id, "cloudflare", changed.Fingerprint, null, 4);
                }

                File.Delete(lockPath);
                Assert(DnsSettings.Find(adapter.Id).Static4.SequenceEqual(changed.Static4), "DNS change ignored engine lock");
                Change(adapter.Id, "restore", changed.Fingerprint, first, 0);
                var restored = DnsSettings.Find(adapter.Id);
                Assert(restored.Static4.SequenceEqual(before4) && (restored.Index6 == 0 || restored.Static6.SequenceEqual(before6)) && DnsActions.Read(first).Status == "REVERTED", "DNS restore failed: " + restored.Summary);
                Change(adapter.Id, "restore", restored.Fingerprint, first, 4);
            }
            finally
            {
                var current = DnsSettings.Find(adapter.Id);
                if (!current.Static4.SequenceEqual(before4) || (current.Index6 > 0 && !current.Static6.SequenceEqual(before6)))
                    DnsSettings.Apply(current, before4, before6, TextWriter.Null);
            }
        }

        private static string Change(string adapter, string target, string expected, string restore, int code)
        {
            string token = Guid.NewGuid().ToString("N");
            int actual = DnsActions.Worker(new[] { "--dns-worker", adapter, target, expected, token, WindowsIdentity.GetCurrent().User.Value, restore ?? "-" });
            Assert(actual == code, "DNS worker returned " + actual + ": " + File.ReadAllText(Path.Combine(Program.Data, "runtime", token + ".dns.log")));
            return token;
        }
    }

    internal sealed partial class MainWindow
    {
        private async Task DnsSmoke()
        {
            string adapterId = Guid.NewGuid().ToString("B");
            var live = await Task.Run(() => DnsSettings.Read());
            Assert(live.All(a => DnsSettings.ValidAdapter(a.Id) && a.Index4 > 0), "Read-only DNS inventory failed");
            var read = dnsRead;
            var run = dnsRun;
            int calls = 0;
            var adapter = new DnsAdapter
            {
                Id = adapterId,
                Name = "Ethernet",
                Description = "Тестовый адаптер",
                Index4 = 7,
                Index6 = 7,
                Gateway = true,
                Effective = new[]
                {
                    "192.168.1.1"
                }
            };
            string id = Guid.NewGuid().ToString("N"), directory = Path.Combine(Program.Data, "dns-history"), path = Path.Combine(directory, id + ".json");
            try
            {
                dnsRead = () => new[]
                {
                    adapter
                };
                dnsRun = (target, provider, expected, restoreId) =>
                {
                    calls++;
                    Assert(target == adapter.Id && expected == adapter.Fingerprint, "DNS UI sent stale state");
                    if (provider == "restore")
                    {
                        adapter.Static4 = new string[0];
                        adapter.Static6 = new string[0];
                    }
                    else
                    {
                        var chosen = DnsSettings.Provider(provider);
                        adapter.Static4 = chosen.V4;
                        adapter.Static6 = DnsSettings.Normalize(chosen.V6, AddressFamily.InterNetworkV6);
                    }

                    return Task.FromResult(new EngineResult { Code = 0, Output = "Тест: настройки Windows не менялись." });
                };
                ShowPage(10);
                await RefreshDns();
                Assert(dnsCurrent.Text.Contains("автоматически") && !dnsApply.IsEnabled, "Current automatic DNS was unclear or re-applicable");
                dnsProvider.SelectedItem = DnsSettings.Provider("cloudflare");
                Assert(dnsApply.IsEnabled && dnsDescription.Text.Contains("1.1.1.1"), "DNS provider cannot be selected");
                SetBusy(true);
                Assert(!dnsApply.IsEnabled && !dnsRefresh.IsEnabled, "DNS controls ignored operation lock");
                SetBusy(false);
                var cancel = SelectDns();
                Assert(confirmation != null, "DNS change skipped confirmation");
                FinishConfirmation(false);
                await cancel;
                Assert(calls == 0, "Cancelled DNS change executed");
                var apply = SelectDns();
                FinishConfirmation(true);
                await apply;
                Assert(calls == 1 && dnsCurrent.Text.Contains("1.1.1.1") && !dnsApply.IsEnabled && networkHost.Text == "1.1.1.1", "DNS choice not applied or refreshed");
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(new DnsChange { Schema = "wintools/dns-change/1", Id = id, Adapter = adapter.Id, AdapterName = adapter.Name, Target = "cloudflare", TargetName = "Cloudflare · 1.1.1.1", Before4 = new string[0], Before6 = new string[0], After4 = adapter.Static4, After6 = adapter.Static6, Action = "select", Status = "OK", TimeUtc = DateTime.UtcNow.ToString("o") }));
                ReadHistory();
                Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r => r.Run == id && r.DnsChange && r.CanRevert && r.Detail.Contains("DNS")), "DNS record missing from shared history");
                await RefreshDns();
                Assert(dnsRestore.IsEnabled, "DNS restore not offered for the adapter");
                var restore = RestoreDnsHistory(id);
                for (int i = 0; i < 100 && confirmation == null && !restore.IsCompleted; i++)
                    await Task.Delay(20);
                Assert(confirmation != null, "DNS restoration skipped confirmation");
                FinishConfirmation(true);
                await restore;
                Assert(calls == 2 && adapter.Static4.Length == 0 && dnsCurrent.Text.Contains("автоматически"), "DNS history restore did not return automatic DNS");
                // Speed comparison: the router's DNS is measured too, the fastest complete answer is marked, and "Выбрать" only fills the list.
                var measured = new System.Collections.Concurrent.ConcurrentBag<string>();
                dnsMeasure = server =>
                {
                    measured.Add(server.Address.ToString());
                    var ms = server.Address.ToString() == "9.9.9.9" ? 12 : server.Address.ToString() == "1.1.1.1" ? 8 : 30;
                    return Task.FromResult(new DnsTiming { MedianMs = server.Address.ToString() == "77.88.8.8" ? (long?)null : ms, Sent = 5, Lost = server.Address.ToString() == "1.1.1.1" ? 2 : server.Address.ToString() == "77.88.8.8" ? 5 : 0 });
                };
                await CompareDns();
                var timingRows = dnsTimings.Children.OfType<Grid>().ToArray();
                Assert(measured.Contains("192.168.1.1") && measured.Count == DnsSettings.Providers.Count(p => p.V4.Length > 0) + 1, "DNS comparison skipped a server");
                Assert(timingRows.Length == measured.Count && ((TextBlock)timingRows[0].Children[0]).Text.StartsWith("Quad9") && ((TextBlock)timingRows[0].Children[1]).Text.Contains("быстрее всего"), "Fastest complete DNS not ranked first");
                Assert(((TextBlock)timingRows.Last().Children[1]).Text == "Не отвечает", "Silent DNS server not reported");
                timingRows[0].Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(dnsProvider.SelectedItem == DnsSettings.Provider("quad9") && calls == 2, "Choosing a measured DNS changed Windows or did not select it");
                foreach (var size in new[]
                {
                    new Size(1280, 800),
                    new Size(800, 600)
                }

                )
                {
                    Window.Width = size.Width;
                    Window.Height = size.Height;
                    ShowPage(10);
                    Window.UpdateLayout();
                    dnsApply.BringIntoView();
                    await Task.Delay(80);
                    Window.UpdateLayout();
                    Assert(dnsAdapter.ActualWidth > 250 && dnsApply.IsVisible, "DNS manager layout unusable");
                    Capture(size.Width == 800 ? "portable-ui-dns-compact.png" : "portable-ui-dns.png");
                }

                File.WriteAllText(path, "{broken");
                ReadHistory();
                Assert(!Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r => r.DnsChange), "Corrupt DNS history was accepted");
                dnsRead = () =>
                {
                    throw new IOException("Тест ошибки чтения");
                };
                await RefreshDns();
                Assert(!dnsApply.IsEnabled && dnsAdapter.Items.Count == 0, "DNS read failure kept stale actions");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
                dnsRead = read;
                dnsRun = run;
                dnsMeasure = server => DnsBenchmark.Measure(server, DnsBenchmark.Names, 1500);
                ReadHistory();
            }

            Get<ScrollViewer>("NetworkPage").ScrollToTop();
            ShowPage(0);
        }
    }
}
