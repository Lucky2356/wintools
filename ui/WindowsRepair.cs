using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace Wintools
{
    internal static partial class WindowsIntegrity
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("DismApi.dll")]
        private static extern int DismRestoreImageHealth(uint session, IntPtr sources, uint count, [MarshalAs(UnmanagedType.Bool)] bool limitAccess, IntPtr cancel, DismProgress progress, IntPtr data);
        internal static IntegrityResult RepairComponents(EventWaitHandle cancel, Action<string> output)
        {
            output(Lang.T("Этап 1: ищем повреждения компонентов перед исправлением."));
            var initial = CheckComponents(true, cancel, output);
            output(initial.Summary);
            if (initial.State == "healthy")
                return initial;
            if (initial.State != "repairable")
                return new IntegrityResult
                {
                    State = initial.State,
                    Summary = Lang.T("Компоненты не допускают исправления этим способом. Восстановление не запускалось. ") + initial.Summary
                };
            if (cancel.WaitOne(0))
                throw new OperationCanceledException(Lang.T("Восстановление отменено до исправления компонентов."));
            output(Lang.T("Этап 2: восстанавливаем компоненты. Windows может скачать файлы из Центра обновления; требуется доступ к интернету, если локального источника недостаточно."));
            output("@progress|0");
            Check(DismInitialize(1, null, null));
            uint session = 0;
            bool opened = false;
            try
            {
                Check(DismOpenSession("DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}", null, null, out session));
                opened = true;
                int previous = -1;
                Exception callbackError = null;
                DismProgress callback = (current, total, data) =>
                {
                    if (total == 0)
                        return;
                    int percent = (int)Math.Min(100, current * 100.0 / total);
                    if (percent == previous)
                        return;
                    previous = percent;
                    try
                    {
                        output("@progress|" + percent);
                    }
                    catch (Exception ex)
                    {
                        callbackError = ex;
                        cancel.Set();
                    }
                };
                int code = DismRestoreImageHealth(session, IntPtr.Zero, 0, false, cancel.SafeWaitHandle.DangerousGetHandle(), callback, IntPtr.Zero);
                GC.KeepAlive(callback);
                if (callbackError != null)
                    throw new IOException(Lang.T("Не удалось сохранить ход восстановления."), callbackError);
                if (code != 0 && cancel.WaitOne(0))
                    throw new OperationCanceledException(Lang.T("Восстановление отменено. Компоненты могли измениться; выполните полную проверку перед дальнейшими действиями."));
                Check(code);
            }
            finally
            {
                if (opened)
                    DismCloseSession(session);
                DismShutdown();
            }

            if (cancel.WaitOne(0))
                return new IntegrityResult
                {
                    State = "review",
                    Summary = Lang.T("DISM завершила восстановление, но после запроса отмены повторная проверка не запускалась. Компоненты могли измениться. Выполните полную проверку.")
                };
            output(Lang.T("Этап 3: повторно проверяем компоненты после восстановления."));
            output("@progress|0");
            var final = CheckComponents(true, cancel, output);
            return new IntegrityResult
            {
                State = final.State == "healthy" ? "repaired" : final.State,
                Summary = final.State == "healthy" ? Lang.T("Компоненты восстановлены. Повторная полная проверка DISM не нашла повреждений.") : Lang.T("Восстановление завершилось, но повторная проверка не подтвердила исправление всех компонентов. ") + final.Summary
            };
        }

        internal static IntegrityResult DescribeSfcRepair(string output, int exitCode)
        {
            string text = (output ?? "").ToLowerInvariant();
            if (text.Contains("system repair pending") || text.Contains("ожидается завершение восстановления системы") || text.Contains("требуется перезагрузка"))
                return new IntegrityResult
                {
                    State = "failed",
                    Summary = Lang.T("Windows требует перезагрузки перед продолжением проверки. Сохраните работу, перезагрузите ПК и повторите обслуживание. Автоматическая перезагрузка не выполняется.")
                };
            if (exitCode != 0)
                return new IntegrityResult
                {
                    State = "failed",
                    Summary = Lang.T("SFC завершилась с кодом ") + exitCode + Lang.T(". Исправление всех файлов не подтверждено; прочитайте отчёт.")
                };
            if (text.Contains("unable to fix") || text.Contains("не может восстановить") || text.Contains("не удалось восстановить"))
                return new IntegrityResult
                {
                    State = "issues",
                    Summary = Lang.T("SFC не смогла исправить все повреждения. Часть файлов могла быть восстановлена. Подробности — в отчёте и журнале CBS Windows.")
                };
            if (text.Contains("successfully repaired") || text.Contains("успешно их восстановила") || text.Contains("успешно восстановила"))
                return new IntegrityResult
                {
                    State = "repaired",
                    Summary = Lang.T("SFC сообщает, что повреждённые системные файлы успешно восстановлены. Подробности сохранены в журнале CBS Windows.")
                };
            var result = DescribeSfc(output, exitCode);
            if (result.State == "healthy")
                result.Summary = Lang.T("SFC не обнаружила нарушений целостности защищённых системных файлов. Восстанавливать их не потребовалось.");
            else if (result.State == "issues")
                result.Summary = Lang.T("SFC сообщила о повреждениях, но успешное исправление не подтверждено. Прочитайте сообщения Windows ниже.");
            return result;
        }

        internal static IntegrityResult RepairWindows(Func<IntegrityResult> components, Func<IntegrityResult> files, Action<string> output)
        {
            var first = components();
            output(first.Summary);
            if (first.State != "healthy" && first.State != "repaired")
                return new IntegrityResult
                {
                    State = first.State,
                    Summary = Lang.T("Обслуживание остановлено на компонентах; исправление системных файлов не запускалось. ") + first.Summary
                };
            output(Lang.T("Следующий этап: проверяем и восстанавливаем защищённые системные файлы SFC."));
            output("@progress|0");
            var second = files();
            return new IntegrityResult
            {
                State = second.State == "healthy" && first.State == "repaired" ? "repaired" : second.State,
                Summary = first.Summary + Environment.NewLine + second.Summary
            };
        }

        internal static bool RestorePointEvent(uint type, Action<string> output)
        {
            try
            {
                using (var restore = new ManagementClass(@"root\default", "SystemRestore", null))
                using (var input = restore.GetMethodParameters("CreateRestorePoint"))
                {
                    input["Description"] = "Wintools: Windows maintenance";
                    input["RestorePointType"] = 12u;
                    input["EventType"] = type;
                    using (var result = restore.InvokeMethod("CreateRestorePoint", input, new InvokeMethodOptions { Timeout = TimeSpan.FromMinutes(2) }))
                    {
                        uint code = Convert.ToUInt32(result["ReturnValue"]);
                        if (code != 0)
                            throw new IOException("Windows: 0x" + code.ToString("X8"));
                    }

                    if (type == 100)
                        output(Lang.T("Запрос точки восстановления принят. Windows может использовать недавнюю точку вместо создания новой."));
                    return true;
                }
            }
            catch (Exception ex)
            {
                output(Lang.T("Точка восстановления: ") + ex.Message + ". " + (type == 100 ? Lang.T("Обслуживание продолжится; отдельного отката исправленных системных файлов в Wintools нет.") : Lang.T("Проверьте состояние защиты системы в Windows.")));
                return false;
            }
        }
    }
}
