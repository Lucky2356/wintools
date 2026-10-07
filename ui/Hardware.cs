using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace Wintools
{
    internal sealed class HardwareSection
    {
        internal string Title, Text;
        internal bool Unavailable;
    }

    internal static class HardwareReader
    {
        internal static string Value(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) || text.Equals("Default string", StringComparison.OrdinalIgnoreCase))
                return Lang.T("нет данных");
            return text;
        }

        internal static string Size(object value)
        {
            ulong bytes;
            if (!ulong.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bytes) || bytes == 0)
                return Lang.T("нет данных");
            return (bytes / 1073741824.0).ToString("N1") + Lang.T(" ГиБ");
        }

        internal static string Positive(object value, string unit)
        {
            ulong number;
            return ulong.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out number) && number > 0 ? number.ToString() + unit : Lang.T("нет данных");
        }

        private static IEnumerable<IDictionary<string, object>> Query(string query)
        {
            using (var search = new ManagementObjectSearcher("root\\cimv2", query, new EnumerationOptions { Timeout = TimeSpan.FromSeconds(5), ReturnImmediately = false }))
            using (var rows = search.Get())
                foreach (ManagementObject row in rows)
                    using (row)
                    {
                        var values = new Dictionary<string, object>();
                        foreach (PropertyData property in row.Properties)
                            values[property.Name] = property.Value;
                        yield return values;
                    }
        }

        internal static HardwareSection Section(string title, string query, Func<IDictionary<string, object>, string> describe, Func<string, IEnumerable<IDictionary<string, object>>> read)
        {
            var lines = new List<string>();
            bool unavailable = false;
            try
            {
                foreach (var row in read(query))
                {
                    string line = describe(row);
                    lines.Add(line);
                    if (line.Contains(Lang.T("нет данных")))
                        unavailable = true;
                }

                if (lines.Count == 0)
                {
                    unavailable = true;
                    lines.Add(Lang.T("Windows не предоставила сведения об устройствах этой группы."));
                }
            }
            catch (Exception ex)
            {
                unavailable = true;
                lines.Add(Lang.T("Часть сведений недоступна: ") + ex.Message);
            }

            return new HardwareSection
            {
                Title = title,
                Text = string.Join("\n\n", lines),
                Unavailable = unavailable
            };
        }

        internal static Task<HardwareSection[]> Read()
        {
            return Task.WhenAll(Task.Run(() => Section(Lang.T("Процессор"), "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor", r => Value(r["Name"]) + Lang.T("\nЯдер: ") + Positive(r["NumberOfCores"], "") + Lang.T(" · Логических процессоров: ") + Positive(r["NumberOfLogicalProcessors"], ""), Query)), Task.Run(() => Section(Lang.T("Видеокарты"), "SELECT Name,DriverVersion FROM Win32_VideoController", r => Value(r["Name"]) + Lang.T("\nДрайвер: ") + Value(r["DriverVersion"]), Query)), Task.Run(() => Section(Lang.T("Модули памяти"), "SELECT DeviceLocator,Manufacturer,PartNumber,Capacity,ConfiguredClockSpeed FROM Win32_PhysicalMemory", r => Value(r["DeviceLocator"]) + " · " + Size(r["Capacity"]) + "\n" + Value(r["Manufacturer"]) + " · " + Value(r["PartNumber"]) + Lang.T("\nНастроенная частота по данным Windows: ") + Positive(r["ConfiguredClockSpeed"], Lang.T(" МГц")), Query)), Task.Run(() => Section(Lang.T("Материнская плата"), "SELECT Manufacturer,Product FROM Win32_BaseBoard", r => Value(r["Manufacturer"]) + "\n" + Value(r["Product"]), Query)), Task.Run(() => Section(Lang.T("BIOS / прошивка"), "SELECT Manufacturer,SMBIOSBIOSVersion FROM Win32_BIOS", r => Value(r["Manufacturer"]) + Lang.T("\nВерсия: ") + Value(r["SMBIOSBIOSVersion"]), Query)), Task.Run(() => Section(Lang.T("Физические диски"), "SELECT Model,Size FROM Win32_DiskDrive", r => Value(r["Model"]) + Lang.T("\nЁмкость: ") + Size(r["Size"]), Query)));
        }

        internal static string Report(HardwareSection[] sections, DateTime captured)
        {
            var text = new StringBuilder(Lang.T("Характеристики ПК · Wintools\nСнимок: ") + captured.ToString("yyyy-MM-dd HH:mm:ss zzz") + Lang.T("\nИсточник: сведения Windows. ГиБ = 1024³ байт.\n"));
            foreach (var section in sections)
                text.Append("\n").AppendLine(section.Title).AppendLine(section.Text);
            return text.ToString();
        }
    }
}
