using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Wintools
{
    internal sealed class DnsTiming
    {
        public string Title { get; set; }
        public string Result { get; set; }
        public bool Fastest { get; set; }
        internal DnsProvider Provider;
        internal long? MedianMs;
        internal int Lost, Sent;
    }

    // Asks each DNS server for the address of a few popular sites, the way Windows would, and times the answers.
    // Plain UDP on port 53: no Windows setting changes and the system resolver cache is not involved.
    internal static class DnsBenchmark
    {
        internal static readonly string[] Names = { "www.microsoft.com", "www.google.com", "www.wikipedia.org", "github.com", "ya.ru" };

        internal static byte[] Query(ushort id, string name)
        {
            var packet = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (var label in name.TrimEnd('.').Split('.'))
            {
                var bytes = Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63)
                    throw new ArgumentException("Invalid DNS name.");
                packet.Add((byte)bytes.Length);
                packet.AddRange(bytes);
            }

            packet.AddRange(new byte[] { 0, 0, 1, 0, 1 });
            return packet.ToArray();
        }

        // A real answer to our question: the same ID, the response bit, and "found" or "no such name".
        internal static bool Answers(byte[] response, ushort id)
        {
            if (response == null || response.Length < 12 || response[0] != (byte)(id >> 8) || response[1] != (byte)id || (response[2] & 0x80) == 0)
                return false;
            int code = response[3] & 0x0F;
            return code == 0 || code == 3;
        }

        internal static long? Median(IEnumerable<long> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0)
                return null;
            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }

        internal static async Task<DnsTiming> Measure(IPEndPoint server, string[] names, int timeoutMs)
        {
            var times = new List<long>();
            var random = new Random();
            foreach (var name in names)
            {
                var id = (ushort)random.Next(1, 65535);
                // A socket per question: a late answer can never be taken for the reply to the next one.
                using (var client = new UdpClient(server.AddressFamily))
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        client.Connect(server);
                        var query = Query(id, name);
                        await client.SendAsync(query, query.Length);
                        while (true)
                        {
                            var receive = client.ReceiveAsync();
                            var left = timeoutMs - (int)watch.ElapsedMilliseconds;
                            if (left <= 0 || await Task.WhenAny(receive, Task.Delay(left)) != receive)
                            {
                                var observed = receive.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                                break;
                            }

                            if (Answers((await receive).Buffer, id))
                            {
                                times.Add(watch.ElapsedMilliseconds);
                                break;
                            }
                        }
                    }
                    catch (SocketException)
                    {
                        // Nothing listens there, or the network refused: counted as a lost answer.
                    }
                }
            }

            return new DnsTiming
            {
                MedianMs = Median(times),
                Sent = names.Length,
                Lost = names.Length - times.Count
            };
        }

        internal static string Describe(DnsTiming timing)
        {
            if (!timing.MedianMs.HasValue)
                return Lang.T("Не отвечает");
            return timing.MedianMs + Lang.T(" мс") + (timing.Lost == 0 ? "" : Lang.T(" · без ответа ") + timing.Lost + Lang.T(" из ") + timing.Sent);
        }

        // Fewer lost answers first, then the shorter median; only servers that answered every question can be "fastest".
        internal static void Rank(IList<DnsTiming> timings)
        {
            var best = timings.Where(t => t.MedianMs.HasValue && t.Lost == 0).OrderBy(t => t.MedianMs.Value).FirstOrDefault();
            foreach (var timing in timings)
            {
                timing.Fastest = timing == best;
                timing.Result = Describe(timing) + (timing.Fastest ? Lang.T(" · быстрее всего у вас") : "");
            }
        }
    }
}
