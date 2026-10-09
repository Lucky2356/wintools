using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Wintools
{
    // How long the start takes and where the time goes; the smoke run saves it so a slowdown shows up in CI.
    internal static class Timings
    {
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly List<KeyValuePair<string, long>> steps = new List<KeyValuePair<string, long>>();

        internal static long Elapsed
        {
            get
            {
                return clock.ElapsedMilliseconds;
            }
        }

        internal static void Measure(string name, Action step)
        {
            long start = clock.ElapsedMilliseconds;
            step();
            steps.Add(new KeyValuePair<string, long>(name, clock.ElapsedMilliseconds - start));
        }

        internal static void Mark(string name)
        {
            steps.Add(new KeyValuePair<string, long>("since start: " + name, clock.ElapsedMilliseconds));
        }

        internal static IEnumerable<KeyValuePair<string, long>> Steps
        {
            get
            {
                return steps.ToArray();
            }
        }

        internal static string Report()
        {
            var text = new StringBuilder();
            using (var process = Process.GetCurrentProcess())
            {
                text.AppendLine("Working set MB: " + (process.WorkingSet64 / 1048576));
                text.AppendLine("Private MB: " + (process.PrivateMemorySize64 / 1048576));
            }

            foreach (var step in steps.OrderByDescending(s => s.Value))
                text.AppendLine(step.Key + ": " + step.Value + " ms");
            return text.ToString();
        }

        internal static void Save(string path)
        {
            try
            {
                File.WriteAllText(path, Report(), new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
        }
    }
}
