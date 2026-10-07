using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace Wintools
{
    internal static class BackupIntegrationTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        internal static void Run()
        {
            if (!Program.Hosted)
                throw new InvalidOperationException("Backup integration runs only on disposable hosted CI.");
            foreach (var action in new[]
            {
                "list",
                "drivers"
            }

            )
            {
                string token = Guid.NewGuid().ToString("N");
                int code = Backups.Worker(new[] { "--backup-worker", action, token, WindowsIdentity.GetCurrent().User.Value });
                string path = Path.Combine(Program.Data, "runtime", token + ".backup.json");
                var text = File.ReadAllText(path);
                File.Delete(path);
                Assert(code == 0, "Backup worker " + action + " failed: " + text);
                var result = Backups.Validate(new JavaScriptSerializer().Deserialize<BackupResult>(text));
                if (action == "drivers")
                {
                    Assert(result.Folder != null && Directory.Exists(result.Folder) && result.Drivers == Backups.CountDrivers(result.Folder), "Driver export folder missing");
                    SelfTests.Trace("Exported drivers: " + result.Drivers);
                    Directory.Delete(result.Folder, true);
                }
            }

            var batteries = Backups.ReadBatteries(null).GetAwaiter().GetResult();
            SelfTests.Trace("Batteries: " + batteries.Length);
        }
    }

    internal sealed partial class MainWindow
    {
        private async Task BackupSmoke()
        {
            const string xml = "<?xml version='1.0' encoding='utf-8'?><BatteryReport xmlns='http://schemas.microsoft.com/battery/2012'><Batteries><Battery><Id>DELL 1234</Id><Manufacturer>SMP</Manufacturer><DesignCapacity>60000</DesignCapacity><FullChargeCapacity>45000</FullChargeCapacity><CycleCount>310</CycleCount></Battery></Batteries></BatteryReport>";
            var batteries = Backups.ParseBatteries(xml);
            var reader = batteryReader;
            var run = backupRun;
            var actions = new System.Collections.Generic.List<string>();
            string folder = Path.Combine(Program.Data, "drivers", "20260101-000000");
            try
            {
                batteryReader = html => Task.FromResult(batteries);
                ShowPage(6);
                await ReadBattery();
                Assert(batteryStatus.Text.Contains("75 %") && !batteryOpen.IsEnabled, "Battery result not shown or missing report enabled");
                batteryReader = html =>
                {
                    throw new IOException("Тест ошибки powercfg");
                };
                await ReadBattery();
                Assert(batteryStatus.Text.Contains("Тест ошибки"), "Battery failure not shown");
                backupRun = action =>
                {
                    actions.Add(action);
                    if (action == "drivers")
                        Directory.CreateDirectory(folder);
                    return Task.FromResult(new BackupResult { Points = new[] { new RestorePointInfo { Description = "Wintools: тест", TimeUtc = DateTime.UtcNow.ToString("o"), Sequence = 7 } }, UsedBytes = 2147483648, MaxBytes = 10737418240, Message = action == "create" ? "Точка восстановления создана." : null, Folder = action == "drivers" ? folder : null, Drivers = action == "drivers" ? 12 : 0 });
                };
                await RunBackup("list");
                Assert(actions.SequenceEqual(new[] { "list" }) && backupPoints.Items.Count == 1 && backupStatus.Text.Contains("2,0 из 10,0 ГБ"), "Restore points not listed");
                var cancel = RunBackup("create");
                Assert(confirmation != null, "Restore point skipped confirmation");
                FinishConfirmation(false);
                await cancel;
                Assert(actions.Count == 1, "Cancelled restore point executed");
                var create = RunBackup("create");
                FinishConfirmation(true);
                await create;
                Assert(actions.Last() == "create" && backupStatus.Text.Contains("создана"), "Restore point not requested");
                var drivers = RunBackup("drivers");
                FinishConfirmation(true);
                await drivers;
                Assert(actions.Last() == "drivers" && backupFolder.IsEnabled, "Driver export not requested");
                SetBusy(true);
                Assert(!backupCreate.IsEnabled && !batteryRead.IsEnabled, "Backup controls ignored operation lock");
                SetBusy(false);
                backupDrivers.BringIntoView();
                Window.UpdateLayout();
                await Task.Delay(80);
                Capture("portable-ui-backups.png");
            }
            finally
            {
                batteryReader = reader;
                backupRun = run;
                driverFolder = null;
                if (Directory.Exists(Path.Combine(Program.Data, "drivers")))
                    Directory.Delete(Path.Combine(Program.Data, "drivers"), true);
                RefreshBackupsEnabled();
            }

            ShowPage(0);
        }
    }
}
