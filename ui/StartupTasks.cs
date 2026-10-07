using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;

namespace Wintools
{
    internal static class StartupTasks
    {
        internal const string Source = "scheduled-task";
        private const string Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        private static object Invoke(object obj, string name, BindingFlags flags, object[] args)
        {
            try
            {
                return obj.GetType().InvokeMember(name, flags, null, obj, args);
            }
            catch (TargetInvocationException ex)
            {
                throw new IOException(ex.InnerException == null ? ex.Message : ex.InnerException.Message, ex);
            }
        }

        internal static object Call(object obj, string name, params object[] args)
        {
            return Invoke(obj, name, BindingFlags.InvokeMethod, args);
        }

        internal static object Get(object obj, string name, params object[] args)
        {
            return Invoke(obj, name, BindingFlags.GetProperty, args);
        }

        internal static void Release(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
                Marshal.FinalReleaseComObject(obj);
        }

        internal static object Connect()
        {
            var service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            try
            {
                Call(service, "Connect");
                return service;
            }
            catch
            {
                Release(service);
                throw;
            }
        }

        internal static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 1024 || !path.StartsWith("\\") || path.EndsWith("\\") || path.Split('\\').Skip(1).Any(p => p.Length == 0 || p == "." || p == ".." || p.Any(char.IsControl)))
                throw new ArgumentException("Некорректный путь задачи.");
        }

        private static string Value(XmlNode node, string path, XmlNamespaceManager ns, string fallback = "")
        {
            var child = node.SelectSingleNode(path, ns);
            return child == null ? fallback : child.InnerText;
        }

        private static bool Flag(XmlNode node, string path, XmlNamespaceManager ns, bool fallback)
        {
            return XmlConvert.ToBoolean(Value(node, path, ns, fallback ? "true" : "false"));
        }

        internal static string Encode(bool enabled)
        {
            var bytes = new byte[12];
            bytes[0] = (byte)(enabled ? 2 : 3);
            return Convert.ToBase64String(bytes);
        }

        private static string Duration(string value)
        {
            try
            {
                var time = XmlConvert.ToTimeSpan(value);
                return (time.Days > 0 ? time.Days + " д " : "") + (time.Hours > 0 ? time.Hours + " ч " : "") + (time.Minutes > 0 ? time.Minutes + " мин " : "") + time.Seconds + " с";
            }
            catch (FormatException)
            {
                return value;
            }
        }

        private static string Boundary(string value)
        {
            DateTimeOffset date;
            return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date) ? date.ToString("dd.MM.yyyy HH:mm zzz") : value;
        }

        internal static StartupEntry Parse(string path, string xml, bool enabled, string sid, string user)
        {
            ValidatePath(path);
            if (xml == null || xml.Length > 1048576)
                throw new IOException("Описание задачи слишком велико.");
            var doc = new XmlDocument
            {
                XmlResolver = null
            };
            using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 }))
                doc.Load(reader);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("t", Ns);
            var task = doc.SelectSingleNode("/t:Task", ns);
            if (task == null)
                throw new IOException("Формат задачи не распознан.");
            var triggers = task.SelectNodes("t:Triggers/*", ns).Cast<XmlNode>().ToArray();
            var logons = triggers.Where(t => t.LocalName == "LogonTrigger").ToArray();
            if (logons.Length == 0)
                return null;
            var actions = task.SelectNodes("t:Actions/*", ns).Cast<XmlNode>().ToArray();
            string owner = Value(task, "t:Principals/t:Principal/t:UserId", ns), group = Value(task, "t:Principals/t:Principal/t:GroupId", ns);
            bool own = string.Equals(owner, sid, StringComparison.OrdinalIgnoreCase) || string.Equals(owner, user, StringComparison.OrdinalIgnoreCase);
            string restriction = path.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase) ? "Системная задача Windows: только просмотр." : !own || group.Length > 0 ? "Задача другого пользователя или системной учётной записи: только просмотр." : triggers.Any(t => t.LocalName != "LogonTrigger") ? "Есть другие условия запуска. Отключение всей задачи затронет их: только просмотр." : actions.Length == 0 || actions.Any(a => a.LocalName != "Exec") ? "Неподдерживаемый тип действия: только просмотр." : !logons.Any(t => Flag(t, "t:Enabled", ns, true)) ? "Все условия входа отключены. Включение задачи не включит эти условия: только просмотр." : null;
            var bytes = new byte[12];
            bytes[0] = (byte)(enabled ? 2 : 3);
            var conditions = logons.Select(t =>
            {
                string who = Value(t, "t:UserId", ns, "любой пользователь");
                return "Вход: " + (who == sid || string.Equals(who, user, StringComparison.OrdinalIgnoreCase) ? "текущий пользователь" : who) + " · условие " + (Flag(t, "t:Enabled", ns, true) ? "включено" : "отключено") + (Value(t, "t:Delay", ns).Length > 0 ? " · задержка " + Duration(Value(t, "t:Delay", ns)) : "");
            }).ToList();
            foreach (var trigger in logons)
            {
                foreach (var field in new[]
                {
                    "StartBoundary",
                    "EndBoundary",
                    "Repetition/Interval",
                    "Repetition/Duration"
                }

                )
                {
                    string value = Value(trigger, "t:" + field.Replace("/", "/t:"), ns);
                    if (value.Length > 0)
                        conditions.Add((field == "StartBoundary" ? "Начало периода" : field == "EndBoundary" ? "Конец периода" : field == "Repetition/Interval" ? "Интервал повтора" : "Длительность повторов") + ": " + (field.Contains("Boundary") ? Boundary(value) : Duration(value)));
                }
            }

            conditions.Add("От имени: " + (own ? "текущего пользователя" : owner.Length > 0 ? owner : group));
            conditions.Add("Питание от сети: " + (Flag(task, "t:Settings/t:DisallowStartIfOnBatteries", ns, true) ? "требуется" : "не требуется") + " · подключение к сети: " + (Flag(task, "t:Settings/t:RunOnlyIfNetworkAvailable", ns, false) ? "требуется" : "не требуется") + " · простой ПК: " + (Flag(task, "t:Settings/t:RunOnlyIfIdle", ns, false) ? "требуется" : "не требуется"));
            var state = task.SelectSingleNode("t:Settings/t:Enabled", ns);
            if (state != null)
                state.ParentNode.RemoveChild(state);
            return new StartupEntry
            {
                Source = Source,
                Name = path,
                Command = string.Join("; ", actions.Select(a => a.LocalName == "Exec" ? Value(a, "t:Command", ns) + " " + Value(a, "t:Arguments", ns) : "Действие: " + a.LocalName)),
                Approval = Convert.ToBase64String(bytes),
                Identity = StartupEntries.Hash(path + "|" + doc.OuterXml),
                Restriction = restriction,
                Details = string.Join("\n", conditions)
            };
        }

        private static StartupEntry ReadTask(object task)
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return Parse((string)Get(task, "Path"), (string)Get(task, "Xml"), (bool)Get(task, "Enabled"), identity.User.Value, identity.Name);
        }

        internal static StartupEntry Inspect(string path)
        {
            ValidatePath(path);
            object service = null, folder = null, task = null;
            try
            {
                service = Connect();
                folder = Call(service, "GetFolder", "\\");
                task = Call(folder, "GetTask", path);
                var entry = ReadTask(task);
                if (entry == null)
                    throw new IOException("У задачи больше нет условия входа в Windows.");
                return entry;
            }
            finally
            {
                Release(task);
                Release(folder);
                Release(service);
            }
        }

        internal static void Write(string path, string value, string expected)
        {
            ValidatePath(path);
            if (value == null || !StartupEntries.Decode(value).HasValue)
                throw new IOException("Некорректное состояние задачи.");
            object service = null, folder = null, task = null;
            try
            {
                service = Connect();
                folder = Call(service, "GetFolder", "\\");
                task = Call(folder, "GetTask", path);
                var entry = ReadTask(task);
                if (entry == null || entry.Restriction != null)
                    throw new IOException(entry == null ? "Условие входа не найдено." : entry.Restriction);
                if (expected == null || entry.Fingerprint != expected)
                    throw new IOException("Задача изменилась после чтения.");
                Invoke(task, "Enabled", BindingFlags.SetProperty, new object[] { StartupEntries.Decode(value).Value });
            }
            finally
            {
                Release(task);
                Release(folder);
                Release(service);
            }
        }

        internal static StartupSnapshot Read()
        {
            var rows = new List<StartupEntry>();
            var errors = new List<string>();
            object service = null;
            try
            {
                service = Connect();
                var pending = new Stack<string>();
                pending.Push("\\");
                int count = 0;
                while (pending.Count > 0)
                {
                    string path = pending.Pop();
                    object folder = null, tasks = null, folders = null;
                    try
                    {
                        folder = Call(service, "GetFolder", path);
                        tasks = Call(folder, "GetTasks", 1);
                        int size = (int)Get(tasks, "Count");
                        for (int i = 1; i <= size; i++)
                        {
                            if (++count > 5000)
                                throw new IOException("Достигнут предел чтения задач.");
                            object task = null;
                            try
                            {
                                task = Get(tasks, "Item", i);
                                var row = ReadTask(task);
                                if (row != null)
                                    rows.Add(row);
                            }
                            catch (Exception)
                            {
                                errors.Add("Не удалось прочитать задачу в " + path);
                            }
                            finally
                            {
                                Release(task);
                            }
                        }

                        folders = Call(folder, "GetFolders", 0);
                        for (int i = 1; i <= (int)Get(folders, "Count"); i++)
                        {
                            object child = null;
                            try
                            {
                                child = Get(folders, "Item", i);
                                pending.Push((string)Get(child, "Path"));
                            }
                            finally
                            {
                                Release(child);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add("Планировщик " + path + ": " + ex.Message);
                        if (count > 5000)
                            break;
                    }
                    finally
                    {
                        Release(folders);
                        Release(tasks);
                        Release(folder);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add("Планировщик недоступен: " + ex.Message);
            }
            finally
            {
                Release(service);
            }

            return new StartupSnapshot
            {
                Entries = rows.ToArray(),
                Errors = errors.Distinct().Take(10).ToArray()
            };
        }
    }
}
