using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Wintools
{
    internal sealed class FolderUsage
    {
        public string Name { get; set; }
        public string Detail { get; set; }

        internal string Path;
        internal long Bytes;
        internal int Files;
    }

    internal sealed class UsageReport
    {
        internal FolderUsage[] Folders = new FolderUsage[0];
        internal long Bytes;
        internal bool Partial;
    }

    // Sizes of the direct children of a folder, without following links or junctions; read-only.
    internal static class DiskUsage
    {
        internal static UsageReport Measure(string root, CancellationToken cancel, int limit)
        {
            if (!Directory.Exists(root))
                throw new IOException("Папка не найдена.");
            var report = new UsageReport();
            var result = new List<FolderUsage>();
            int visited = 0;
            var loose = new FolderUsage
            {
                Name = "Файлы в самой папке",
                Path = root
            };
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(root).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                throw new IOException("Нет доступа к папке.");
            }

            foreach (var child in children)
            {
                cancel.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch (Exception)
                {
                    report.Partial = true;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    try
                    {
                        loose.Bytes += new FileInfo(child).Length;
                        loose.Files++;
                    }
                    catch (Exception)
                    {
                        report.Partial = true;
                    }

                    continue;
                }

                var usage = new FolderUsage
                {
                    Name = System.IO.Path.GetFileName(child),
                    Path = child
                };
                var pending = new Stack<string>();
                pending.Push(child);
                while (pending.Count > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    var current = pending.Pop();
                    try
                    {
                        foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                        {
                            if (++visited > limit)
                            {
                                report.Partial = true;
                                pending.Clear();
                                break;
                            }

                            try
                            {
                                var entryAttributes = File.GetAttributes(entry);
                                if ((entryAttributes & FileAttributes.ReparsePoint) != 0)
                                    continue;
                                if ((entryAttributes & FileAttributes.Directory) != 0)
                                    pending.Push(entry);
                                else
                                {
                                    usage.Bytes += new FileInfo(entry).Length;
                                    usage.Files++;
                                }
                            }
                            catch (Exception)
                            {
                                report.Partial = true;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        report.Partial = true;
                    }
                    catch (IOException)
                    {
                        report.Partial = true;
                    }
                }

                result.Add(usage);
                if (visited > limit)
                    break;
            }

            if (loose.Files > 0)
                result.Add(loose);
            report.Bytes = result.Sum(f => f.Bytes);
            foreach (var folder in result)
                folder.Detail = Size(folder.Bytes) + " · файлов: " + folder.Files + (report.Bytes > 0 ? " · " + (100.0 * folder.Bytes / report.Bytes).ToString("0") + " %" : "");
            report.Folders = result.OrderByDescending(f => f.Bytes).Take(20).ToArray();
            return report;
        }

        internal static string Size(long bytes)
        {
            return bytes >= 1073741824 ? (bytes / 1073741824.0).ToString("0.0") + " ГБ" : (bytes / 1048576.0).ToString("0.0") + " МБ";
        }
    }

    // Disk caches of Chromium-based browsers and Firefox for the current user. Profiles, history, passwords and cookies stay untouched.
    internal static class BrowserCache
    {
        private static readonly string[][] Chromium =
        {
            new[]
            {
                "Google Chrome",
                "chrome",
                @"Google\Chrome\User Data"
            },
            new[]
            {
                "Microsoft Edge",
                "msedge",
                @"Microsoft\Edge\User Data"
            },
            new[]
            {
                "Brave",
                "brave",
                @"BraveSoftware\Brave-Browser\User Data"
            },
            new[]
            {
                "Яндекс Браузер",
                "browser",
                @"Yandex\YandexBrowser\User Data"
            },
            new[]
            {
                "Vivaldi",
                "vivaldi",
                @"Vivaldi\User Data"
            }
        };
        internal sealed class Source
        {
            internal string Browser, Process;
            internal string[] Folders;
        }

        internal static Source[] Sources(string local)
        {
            local = local ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var result = new List<Source>();
            foreach (var browser in Chromium)
            {
                var data = Path.Combine(local, browser[2]);
                if (!Directory.Exists(data))
                    continue;
                var folders = new List<string>();
                foreach (var profile in Directory.GetDirectories(data).Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
                }))
                    foreach (var cache in new[]
                    {
                        "Cache",
                        "Code Cache",
                        "GPUCache"
                    }

                    )
                    {
                        var folder = Path.Combine(profile, cache);
                        if (Directory.Exists(folder))
                            folders.Add(folder);
                    }

                if (folders.Count > 0)
                    result.Add(new Source { Browser = browser[0], Process = browser[1], Folders = folders.ToArray() });
            }

            var firefox = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(firefox))
            {
                var folders = Directory.GetDirectories(firefox).Select(p => Path.Combine(p, "cache2")).Where(Directory.Exists).ToArray();
                if (folders.Length > 0)
                    result.Add(new Source { Browser = "Firefox", Process = "firefox", Folders = folders });
            }

            return result.ToArray();
        }

        internal static bool Running(Source source)
        {
            var processes = Process.GetProcessesByName(source.Process);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        internal static CleanupEstimate Estimate(CancellationToken cancel)
        {
            return Estimate(cancel, null);
        }

        internal static CleanupEstimate Estimate(CancellationToken cancel, string local)
        {
            var result = new CleanupEstimate
            {
                Captured = DateTime.Now
            };
            var names = new List<string>();
            foreach (var source in Sources(local))
            {
                if (Running(source))
                {
                    names.Add(source.Browser + " (открыт — пропущен)");
                    continue;
                }

                names.Add(source.Browser);
                foreach (var folder in source.Folders)
                {
                    var part = CleanupPreview.Scan(folder, DateTime.Now.AddMinutes(1), cancel, 250000);
                    result.Bytes += part.Bytes;
                    result.Files += part.Files;
                    result.SkippedLinks += part.SkippedLinks;
                    result.Errors += part.Errors;
                    if (part.Errors > 0)
                        result.Error = part.Error;
                }
            }

            result.Source = names.Count == 0 ? "Поддерживаемые браузеры не найдены" : string.Join(", ", names);
            return result;
        }

        // Deletes cache files only; folders stay so the browser can reuse them. Locked files are skipped.
        internal static EngineResult Clean()
        {
            return Clean(null);
        }

        internal static EngineResult Clean(string local)
        {
            var output = new StringBuilder();
            int deleted = 0, skipped = 0;
            long bytes = 0;
            foreach (var source in Sources(local))
            {
                if (Running(source))
                {
                    output.AppendLine(source.Browser + ": открыт, кэш не очищался. Закройте браузер и повторите.");
                    continue;
                }

                foreach (var folder in source.Folders)
                {
                    var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
                    if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        continue;
                    }

                    var pending = new Stack<string>();
                    pending.Push(root);
                    while (pending.Count > 0)
                    {
                        var current = pending.Pop();
                        string[] entries;
                        try
                        {
                            entries = Directory.GetFileSystemEntries(current);
                        }
                        catch (Exception)
                        {
                            skipped++;
                            continue;
                        }

                        foreach (var entry in entries)
                        {
                            try
                            {
                                var path = Path.GetFullPath(entry);
                                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                {
                                    skipped++;
                                    continue;
                                }

                                var attributes = File.GetAttributes(path);
                                if ((attributes & FileAttributes.ReparsePoint) != 0)
                                {
                                    skipped++;
                                    continue;
                                }

                                if ((attributes & FileAttributes.Directory) != 0)
                                {
                                    pending.Push(path);
                                    continue;
                                }

                                long length = new FileInfo(path).Length;
                                if ((attributes & FileAttributes.ReadOnly) != 0)
                                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                                File.Delete(path);
                                deleted++;
                                bytes += length;
                            }
                            catch (Exception)
                            {
                                skipped++;
                            }
                        }
                    }
                }

                output.AppendLine(source.Browser + ": кэш очищен.");
            }

            output.AppendLine("Удалено файлов: " + deleted + " (" + DiskUsage.Size(bytes) + "). Пропущено занятых или недоступных: " + skipped + ".");
            return new EngineResult
            {
                Code = 0,
                Output = output.ToString()
            };
        }
    }
}
