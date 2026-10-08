using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private async Task CatalogueStateSmoke()
        {
            // Live read of a disposable key proves hive mapping, the default value and absent keys.
            string key = "Software\\Wintools-smoke-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var created = Registry.CurrentUser.CreateSubKey(key))
                {
                    created.SetValue("Flag", 1, RegistryValueKind.DWord);
                    created.SetValue("", "x", RegistryValueKind.String);
                }

                var items = new[]
                {
                    new Tweak
                    {
                        Id = "SMOKE-LIVE",
                        Kind = "REG",
                        Target = "@HKCU@\\" + key,
                        ValueName = "Flag",
                        ValueType = "REG_DWORD",
                        Value = "1"
                    },
                    new Tweak
                    {
                        Id = "SMOKE-DEFAULT",
                        Kind = "REG",
                        Target = "@HKCU@\\" + key,
                        ValueName = "@DEFAULT@",
                        ValueType = "REG_SZ",
                        Value = "@EMPTY@"
                    },
                    new Tweak
                    {
                        Id = "SMOKE-ABSENT",
                        Kind = "REG",
                        Target = "@HKCU@\\" + key + "\\Missing",
                        ValueName = "Flag",
                        ValueType = "REG_DWORD",
                        Value = "1"
                    },
                    new Tweak
                    {
                        Id = "SMOKE-TASK",
                        Kind = "TASK",
                        Target = "\\Wintools-smoke\\" + Guid.NewGuid().ToString("N"),
                        ValueName = "-",
                        ValueType = "state",
                        Value = "disable"
                    }
                };
                var states = TweakStates.Read(items);
                Assert(states["SMOKE-LIVE"].Applied == true, "Live registry value not read: " + states["SMOKE-LIVE"].Text);
                Assert(states["SMOKE-DEFAULT"].Applied == false && states["SMOKE-DEFAULT"].Full.Contains("«x»"), "Default registry value not read: " + states["SMOKE-DEFAULT"].Full);
                Assert(states["SMOKE-ABSENT"].Applied == false, "Absent key reported as applied");
                Assert(states["SMOKE-TASK"].Applied == null && states["SMOKE-TASK"].Text.Contains("отсутствует"), "Missing task not reported: " + states["SMOKE-TASK"].Text);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(key, false);
            }

            Assert(tweakStates != null && catalogue.Where(TweakStates.Supported).All(t => tweakStates.ContainsKey(t.Id)), "Catalogue state was not read for every registry and task action");
            var saved = tweakStates;
            try
            {
                tweakStates = new Dictionary<string, TweakState>
                {
                    {
                        "UI-FILEEXT",
                        TweakState.Known(true, null)
                    },
                    {
                        "UI-LAUNCHTO",
                        TweakState.Known(false, "сейчас 2")
                    }
                };
                ChooseCollection(new[] { "UI-FILEEXT", "UI-LAUNCHTO" });
                var rows = Get<ListBox>("Items").Items.Cast<ActionRow>().ToArray();
                Assert(rows.Length == 2 && rows.Single(r => r.Item.Id == "UI-FILEEXT").Applied && rows.Single(r => r.Item.Id == "UI-FILEEXT").ServiceStatus.Contains("Уже применено"), "Applied action not marked");
                Assert(!rows.Single(r => r.Item.Id == "UI-LAUNCHTO").Applied && rows.Single(r => r.Item.Id == "UI-LAUNCHTO").ServiceStatus == "Не применено", "Pending action not described");
                Get<ListBox>("Items").SelectedItem = rows.Single(r => r.Item.Id == "UI-FILEEXT");
                Assert(Get<TextBlock>("Description").Text.Contains("ничего не изменит"), "Details hide current state");
                Get<CheckBox>("HideApplied").IsChecked = true;
                Filter();
                Assert(Get<ListBox>("Items").Items.Count == 1 && ((ActionRow)Get<ListBox>("Items").Items[0]).Item.Id == "UI-LAUNCHTO", "Hide applied filter kept applied action");
                tweakStates = null;
                Filter();
                Assert(Get<ListBox>("Items").Items.Count == 2, "Unknown state hidden as applied");
            }
            finally
            {
                Get<CheckBox>("HideApplied").IsChecked = false;
                tweakStates = saved;
                Get<Button>("ClearCollection").RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            }

            await Task.Delay(10);
        }
    }
}
