using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Wintools
{
    internal sealed class DnsAdapter
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        internal int Index4, Index6;
        internal bool Gateway;
        internal string[] Static4 = new string[0], Static6 = new string[0], Effective = new string[0];
        public string Label
        {
            get
            {
                return Name + " · " + Description;
            }
        }

        internal string Fingerprint
        {
            get
            {
                return DnsSettings.Fingerprint(Static4, Static6);
            }
        }

        internal string Summary
        {
            get
            {
                return (Static4.Length + Static6.Length == 0 ? "автоматически (адреса выдаёт сеть)" : "вручную: " + string.Join(", ", Static4.Concat(Static6))) + (Effective.Length > 0 ? ". Используются: " + string.Join(", ", Effective.Take(4)) : "");
            }
        }
    }

    internal sealed class DnsProvider
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        internal string[] V4, V6;
    }

    // Reads per-adapter static DNS from the TCP/IP registry and changes it through netsh, so IPv4 and IPv6 stay consistent.
    internal static class DnsSettings
    {
        internal static readonly DnsProvider[] Providers =
        {
            new DnsProvider
            {
                Key = "auto",
                Name = "Автоматически (от роутера или провайдера)",
                Description = "DNS-серверы выдаёт сеть. Так Windows настроена по умолчанию.",
                V4 = new string[0],
                V6 = new string[0]
            },
            new DnsProvider
            {
                Key = "cloudflare",
                Name = "Cloudflare · 1.1.1.1",
                Description = "Публичный DNS Cloudflare без фильтрации сайтов.",
                V4 = new[]
                {
                    "1.1.1.1",
                    "1.0.0.1"
                },
                V6 = new[]
                {
                    "2606:4700:4700::1111",
                    "2606:4700:4700::1001"
                }
            },
            new DnsProvider
            {
                Key = "google",
                Name = "Google · 8.8.8.8",
                Description = "Публичный DNS Google без фильтрации сайтов.",
                V4 = new[]
                {
                    "8.8.8.8",
                    "8.8.4.4"
                },
                V6 = new[]
                {
                    "2001:4860:4860::8888",
                    "2001:4860:4860::8844"
                }
            },
            new DnsProvider
            {
                Key = "quad9",
                Name = "Quad9 · 9.9.9.9",
                Description = "Не открывает домены из списков вредоносных сайтов Quad9. Обычные сайты не фильтруются.",
                V4 = new[]
                {
                    "9.9.9.9",
                    "149.112.112.112"
                },
                V6 = new[]
                {
                    "2620:fe::fe",
                    "2620:fe::9"
                }
            },
            new DnsProvider
            {
                Key = "adguard",
                Name = "AdGuard DNS · 94.140.14.14",
                Description = "Блокирует домены рекламы и трекеров во всех программах. Отдельные сайты и приложения могут работать неправильно.",
                V4 = new[]
                {
                    "94.140.14.14",
                    "94.140.15.15"
                },
                V6 = new[]
                {
                    "2a10:50c0::ad1:ff",
                    "2a10:50c0::ad2:ff"
                }
            },
            new DnsProvider
            {
                Key = "yandex",
                Name = "Яндекс DNS · 77.88.8.8",
                Description = "Базовый Яндекс DNS без фильтрации сайтов.",
                V4 = new[]
                {
                    "77.88.8.8",
                    "77.88.8.1"
                },
                V6 = new[]
                {
                    "2a02:6b8::feed:0ff",
                    "2a02:6b8:0:1::feed:0ff"
                }
            }
        };
        internal static DnsProvider Provider(string key)
        {
            return Providers.FirstOrDefault(p => p.Key == key);
        }

        internal static bool ValidAdapter(string id)
        {
            Guid value;
            return id != null && id.Length == 38 && Guid.TryParseExact(id, "B", out value) && value != Guid.Empty;
        }

        internal static string Fingerprint(string[] v4, string[] v6)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join(",", v4) + "|" + string.Join(",", v6)))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        // Rejects anything that is not a literal address of the expected family; netsh receives only normalized text.
        internal static string[] Normalize(IEnumerable<string> values, AddressFamily family)
        {
            var result = new List<string>();
            foreach (var value in values ?? new string[0])
            {
                IPAddress address;
                if (value == null || !IPAddress.TryParse(value, out address) || address.AddressFamily != family || (family == AddressFamily.InterNetworkV6 && address.ScopeId != 0))
                    throw new ArgumentException("Некорректный адрес DNS: " + value);
                string text = address.ToString();
                if (!result.Contains(text))
                    result.Add(text);
            }

            if (result.Count > 8)
                throw new ArgumentException("Слишком много адресов DNS.");
            return result.ToArray();
        }

        internal static string[] ParseRegistry(string value, AddressFamily family)
        {
            var result = new List<string>();
            foreach (var part in (value ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                IPAddress address;
                if (IPAddress.TryParse(part, out address) && address.AddressFamily == family && !result.Contains(address.ToString()))
                    result.Add(address.ToString());
            }

            return result.ToArray();
        }

        private static string[] Static(bool v6, string id)
        {
            using (var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + (v6 ? "Tcpip6" : "Tcpip") + "\\Parameters\\Interfaces\\" + id, false))
            {
                return key == null ? new string[0] : ParseRegistry(Convert.ToString(key.GetValue("NameServer", "")), v6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
            }
        }

        internal static DnsAdapter[] Read()
        {
            var result = new List<DnsAdapter>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel || !nic.Supports(NetworkInterfaceComponent.IPv4))
                    continue;
                Guid guid;
                if (!Guid.TryParse(nic.Id, out guid))
                    continue;
                string id = guid.ToString("B");
                var properties = nic.GetIPProperties();
                var v4 = properties.GetIPv4Properties();
                if (v4 == null)
                    continue;
                IPv6InterfaceProperties v6 = null;
                if (nic.Supports(NetworkInterfaceComponent.IPv6))
                {
                    try
                    {
                        v6 = properties.GetIPv6Properties();
                    }
                    catch (NetworkInformationException)
                    {
                    }
                }

                result.Add(new DnsAdapter { Id = id, Name = nic.Name, Description = nic.Description, Index4 = v4.Index, Index6 = v6 == null ? 0 : v6.Index, Gateway = properties.GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(IPAddress.Any)), Static4 = Static(false, id), Static6 = v6 == null ? new string[0] : Static(true, id), Effective = properties.DnsAddresses.Select(a => a.ToString()).ToArray() });
            }

            return result.OrderByDescending(a => a.Gateway).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        internal static DnsAdapter Find(string id)
        {
            var adapter = Read().FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (adapter == null)
                throw new IOException("Сетевой адаптер отключён или больше не найден.");
            return adapter;
        }

        [DllImport("dnsapi.dll")]
        private static extern bool DnsFlushResolverCache();
        // Requires elevation. An empty list returns the address family to DHCP-provided servers.
        internal static void Apply(DnsAdapter adapter, string[] v4, string[] v6, TextWriter log)
        {
            v4 = Normalize(v4, AddressFamily.InterNetwork);
            v6 = Normalize(v6, AddressFamily.InterNetworkV6);
            SetFamily("ipv4", adapter.Index4, v4, log);
            if (adapter.Index6 > 0)
                SetFamily("ipv6", adapter.Index6, v6, log);
            else if (v6.Length > 0)
                log.WriteLine("IPv6 на адаптере выключен: адреса IPv6 не заданы.");
            try
            {
                DnsFlushResolverCache();
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }

            var after = Find(adapter.Id);
            if (!after.Static4.SequenceEqual(v4) || (adapter.Index6 > 0 && !after.Static6.SequenceEqual(v6)))
                throw new IOException("Windows не подтвердила новые адреса DNS. Сейчас: " + after.Summary);
        }

        private static void SetFamily(string family, int index, string[] servers, TextWriter log)
        {
            if (index <= 0)
                throw new IOException("Windows не сообщила номер сетевого интерфейса.");
            if (servers.Length == 0)
            {
                Netsh("interface " + family + " set dnsservers name=" + index + " source=dhcp", log);
                return;
            }

            Netsh("interface " + family + " set dnsservers name=" + index + " source=static address=" + servers[0] + " register=primary validate=no", log);
            for (int i = 1; i < servers.Length; i++)
                Netsh("interface " + family + " add dnsservers name=" + index + " address=" + servers[i] + " index=" + (i + 1) + " validate=no", log);
        }

        private static void Netsh(string arguments, TextWriter log)
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                var error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(30000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    throw new IOException("netsh не ответил за 30 секунд.");
                }

                string text = (output + " " + error.Result).Trim();
                if (process.ExitCode != 0)
                    throw new IOException("netsh " + arguments + " завершился с кодом " + process.ExitCode + (text.Length > 0 ? ": " + text : "."));
                if (text.Length > 0)
                    log.WriteLine(text);
            }
        }
    }
}
