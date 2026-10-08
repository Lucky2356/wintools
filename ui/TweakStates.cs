using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Wintools
{
    internal sealed class TweakState
    {
        public bool? Applied;
        // Text is the short verdict for list rows; Detail keeps the raw current value for the details panel only.
        public string Text, Detail;

        public string Full
        {
            get
            {
                return string.IsNullOrEmpty(Detail) ? Text : Text + " · " + Detail;
            }
        }

        internal static TweakState Known(bool applied, string detail)
        {
            return new TweakState
            {
                Applied = applied,
                Text = applied ? Lang.T("✓ Уже применено") : Lang.T("Не применено"),
                Detail = detail
            };
        }

        internal static TweakState Unknown(string text)
        {
            return new TweakState
            {
                Text = text
            };
        }
    }

    // Read-only snapshot of registry and Task Scheduler actions. "Applied" matches the engine's skip rule:
    // the engine would leave such a value or task unchanged.
    internal static class TweakStates
    {
        internal static bool Supported(Tweak item)
        {
            return item != null && (item.Kind == "REG" || item.Kind == "TASK");
        }

        internal static Dictionary<string, TweakState> Read(IEnumerable<Tweak> items)
        {
            var result = new Dictionary<string, TweakState>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.Where(Supported))
            {
                try
                {
                    result[item.Id] = item.Kind == "REG" ? ReadRegistry(item) : ReadTask(item.Target);
                }
                catch (Exception ex)
                {
                    result[item.Id] = TweakState.Unknown(Lang.T("Состояние не прочитано: ") + ex.Message);
                }
            }

            return result;
        }

        private static TweakState ReadRegistry(Tweak item)
        {
            string path = item.Target;
            RegistryHive hive;
            if (path.StartsWith("@HKCU@\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.CurrentUser;
                path = path.Substring(7);
            }
            else if (path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.LocalMachine;
                path = path.Substring(5);
            }
            else
                return TweakState.Unknown(Lang.T("Состояние этого раздела реестра не проверяется"));
            string name = item.ValueName == "@DEFAULT@" ? "" : item.ValueName;
            using (var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (var key = root.OpenSubKey(path, false))
            {
                if (key == null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                    return Compare(item, RegistryValueKind.Unknown, null);
                return Compare(item, key.GetValueKind(name), key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
        }

        internal static TweakState Compare(Tweak item, RegistryValueKind kind, object value)
        {
            if (value == null)
                return TweakState.Known(false, Lang.T("стандартное значение Windows"));
            if (item.ValueType == "REG_DWORD")
            {
                uint wanted;
                if (!TryDword(item.Value, out wanted))
                    return TweakState.Unknown(Lang.T("Ожидаемое значение не распознано"));
                if (kind != RegistryValueKind.DWord)
                    return TweakState.Known(false, Lang.T("сейчас значение другого типа (") + kind + ")");
                uint current = unchecked((uint)(int)value);
                return TweakState.Known(current == wanted, current == wanted ? null : Lang.T("сейчас ") + current.ToString(CultureInfo.InvariantCulture));
            }

            if (item.ValueType == "REG_SZ")
            {
                string wanted = item.Value == "@EMPTY@" ? "" : item.Value;
                if (kind != RegistryValueKind.String)
                    return TweakState.Known(false, Lang.T("сейчас значение другого типа (") + kind + ")");
                string current = Convert.ToString(value, CultureInfo.InvariantCulture);
                return TweakState.Known(current == wanted, current == wanted ? null : Lang.T("сейчас «") + (current.Length > 40 ? current.Substring(0, 40) + "…" : current) + "»");
            }

            return TweakState.Unknown(Lang.T("Тип значения не проверяется"));
        }

        internal static bool TryDword(string text, out uint value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
            int signed;
            if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out signed))
            {
                value = unchecked((uint)signed);
                return true;
            }

            return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static TweakState ReadTask(string path)
        {
            object service = null, folder = null, task = null;
            try
            {
                service = StartupTasks.Connect();
                folder = StartupTasks.Call(service, "GetFolder", "\\");
                try
                {
                    task = StartupTasks.Call(folder, "GetTask", path);
                }
                catch (IOException ex)
                {
                    if (Missing(ex))
                        return TweakState.Unknown(Lang.T("Задача отсутствует на этом ПК"));
                    throw;
                }

                bool enabled = (bool)StartupTasks.Get(task, "Enabled");
                return TweakState.Known(!enabled, enabled ? Lang.T("задача включена") : Lang.T("задача отключена"));
            }
            finally
            {
                StartupTasks.Release(task);
                StartupTasks.Release(folder);
                StartupTasks.Release(service);
            }
        }

        // ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND from the scheduler mean the task or its folder is absent.
        // COM interop surfaces them as FileNotFoundException / DirectoryNotFoundException, so the HRESULT is checked on every wrapper.
        private static bool Missing(Exception ex)
        {
            for (var inner = ex; inner != null; inner = inner.InnerException)
            {
                if (inner.HResult == unchecked((int)0x80070002) || inner.HResult == unchecked((int)0x80070003))
                    return true;
            }

            return false;
        }
    }
}
