using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools
{
    internal static class WindowsUpdateIntegrationTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        // Pauses updates on the disposable runner, resumes and restores the exact previous values.
        internal static void Run()
        {
            if (!Program.Hosted)
                throw new InvalidOperationException("Windows Update integration runs only on disposable hosted CI.");
            var original = WindowsUpdates.Read();
            try
            {
                string pause = Change("pause", "7", original.Fingerprint, null, 0);
                var paused = WindowsUpdates.Read();
                Assert(paused.PausedUntil.HasValue && paused.PausedUntil.Value > DateTime.UtcNow.AddDays(6.9) && paused.PausedUntil.Value < DateTime.UtcNow.AddDays(7.1), "Pause not written");
                Change("hours", "8-23", original.Fingerprint, null, 4);
                Assert(WindowsUpdates.Read().Fingerprint == paused.Fingerprint, "Stale update request changed settings");
                string hours = Change("hours", "9-21", paused.Fingerprint, null, 0);
                var withHours = WindowsUpdates.Read();
                Assert(withHours.ActiveStart == 9 && withHours.ActiveEnd == 21 && withHours.PausedUntil.HasValue, "Active hours not written");
                Change("restore", "-", withHours.Fingerprint, pause, 4);
                Change("restore", "-", withHours.Fingerprint, hours, 0);
                Assert(WindowsUpdates.Read().Fingerprint == paused.Fingerprint, "Active hours restore failed");
                string resume = Change("resume", "-", paused.Fingerprint, null, 0);
                Assert(!WindowsUpdates.Read().PausedUntil.HasValue, "Resume did not clear the pause");
                Change("restore", "-", WindowsUpdates.Read().Fingerprint, resume, 0);
                Change("restore", "-", paused.Fingerprint, pause, 0);
                Assert(WindowsUpdates.Read().Fingerprint == original.Fingerprint, "Update settings not restored to the original values");
            }
            finally
            {
                if (WindowsUpdates.Read().Fingerprint != original.Fingerprint)
                    Trace("Update settings differ from the original after the test");
            }

            WindowsUpdates.InstalledHistory(5);
        }

        private static void Trace(string message)
        {
            SelfTests.Trace(message);
        }

        private static string Change(string action, string argument, string expected, string restore, int code)
        {
            string token = Guid.NewGuid().ToString("N");
            int actual = WindowsUpdates.Worker(new[] { "--update-worker", action, argument, expected, token, WindowsIdentity.GetCurrent().User.Value, restore ?? "-" });
            Assert(actual == code, "Update worker returned " + actual + " for " + action + ": " + File.ReadAllText(Path.Combine(Program.Data, "runtime", token + ".update.log")));
            return token;
        }
    }

    internal sealed partial class MainWindow
    {
        private async Task WindowsUpdateSmoke()
        {
            var live = await Task.Run(() => WindowsUpdates.Read());
            Assert(live.Values.All(v => WindowsUpdates.Names.Contains(v.Name)), "Read-only update settings failed");
            var read = updateRead;
            var installed = updateInstalled;
            var run = updateRun;
            var values = new UpdateValue[0];
            int calls = 0;
            string id = Guid.NewGuid().ToString("N"), directory = Path.Combine(Program.Data, "update-history");
            try
            {
                updateRead = () => WindowsUpdates.Describe(values);
                updateInstalled = count => new[]
                {
                    new UpdateHistoryRow
                    {
                        Title = "2026-10 Cumulative Update",
                        Detail = "01.10.2026 · установка · успешно"
                    }
                };
                updateRun = (action, argument, expected, restoreId) =>
                {
                    calls++;
                    Assert(expected == WindowsUpdates.Describe(values).Fingerprint, "Update UI sent stale state");
                    values = action == "restore" ? new UpdateValue[0] : WindowsUpdates.Plan(action, argument, DateTime.UtcNow).Values.Where(v => v.Data != null).ToArray();
                    return Task.FromResult(new EngineResult { Code = 0, Output = "Тест: настройки Windows не менялись." });
                };
                OpenMaintenance(UpdatesChoice);
                await RefreshUpdates();
                Assert(updateCurrent.Text.Contains("не приостановлены") && !updateResume.IsEnabled && updatePause.IsEnabled && updateHistory.Items.Count == 1, "Update state unclear");
                SetBusy(true);
                Assert(!updatePause.IsEnabled && !updateRefresh.IsEnabled, "Update controls ignored operation lock");
                SetBusy(false);
                updatePauseDays.SelectedIndex = 1;
                var cancel = ChangeUpdates("pause");
                Assert(confirmation != null && Get<TextBlock>("ConfirmText").Text.Contains("14"), "Pause skipped confirmation");
                FinishConfirmation(false);
                await cancel;
                Assert(calls == 0, "Cancelled pause executed");
                var pause = ChangeUpdates("pause");
                FinishConfirmation(true);
                await pause;
                Assert(calls == 1 && updateCurrent.Text.Contains("приостановлены до") && updateResume.IsEnabled, "Pause not applied");
                updateHoursStart.SelectedIndex = 9;
                updateHoursEnd.SelectedIndex = 9;
                Assert(!updateHours.IsEnabled, "Empty active hours accepted");
                updateHoursEnd.SelectedIndex = 21;
                Assert(updateHours.IsEnabled, "Valid active hours rejected");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, id + ".json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new UpdateChange { Schema = "wintools/update-change/1", Id = id, Action = "pause", Detail = "Пауза обновлений на 14 дн.", Before = new UpdateValue[0], BeforeHash = WindowsUpdates.Fingerprint(new UpdateValue[0]), AfterHash = WindowsUpdates.Describe(values).Fingerprint, Status = "OK", TimeUtc = DateTime.UtcNow.ToString("o") }));
                ReadHistory();
                Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r => r.Run == id && r.UpdateChange && r.CanRevert), "Update record missing from shared history");
                await RefreshUpdates();
                Assert(updateRestore.IsEnabled, "Update restore not offered");
                var restore = RestoreUpdateHistory(id);
                for (int i = 0; i < 100 && confirmation == null && !restore.IsCompleted; i++)
                    await Task.Delay(20);
                Assert(confirmation != null, "Update restore skipped confirmation");
                FinishConfirmation(true);
                await restore;
                Assert(calls == 2 && values.Length == 0 && updateCurrent.Text.Contains("не приостановлены"), "Update restore not applied");
                foreach (var size in new[]
                {
                    new Size(1280, 800),
                    new Size(800, 600)
                }

                )
                {
                    Window.Width = size.Width;
                    Window.Height = size.Height;
                    OpenMaintenance(UpdatesChoice);
                    Window.UpdateLayout();
                    updatePause.BringIntoView();
                    await Task.Delay(80);
                    Window.UpdateLayout();
                    Assert(updatePause.IsVisible && updateHoursStart.ActualWidth > 60, "Update card layout unusable");
                    Capture(size.Width == 800 ? "portable-ui-updates-compact.png" : "portable-ui-windows-update.png");
                }

                integrityChoice.SelectedIndex = 0;
                File.WriteAllText(Path.Combine(directory, id + ".json"), "{broken");
                ReadHistory();
                Assert(!Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r => r.UpdateChange), "Corrupt update history was accepted");
                updateRead = () =>
                {
                    throw new IOException("Тест ошибки чтения");
                };
                await RefreshUpdates();
                Assert(!updatePause.IsEnabled && updateCurrent.Text.Contains("Тест ошибки"), "Update read failure kept stale actions");
            }
            finally
            {
                updateRead = read;
                updateInstalled = installed;
                updateRun = run;
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
                ReadHistory();
            }

            Get<ScrollViewer>("OptimizationPage").ScrollToTop();
            ShowPage(0);
        }
    }
}
