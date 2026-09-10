using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Wintools {
    internal sealed class IntegrityResult {
        internal string State,Summary;
    }
    internal static partial class WindowsIntegrity {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DismProgress(uint current,uint total,IntPtr data);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("DismApi.dll",CharSet=CharSet.Unicode)] private static extern int DismInitialize(int level,string log,string scratch);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("DismApi.dll",CharSet=CharSet.Unicode)] private static extern int DismOpenSession(string image,string windows,string drive,out uint session);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("DismApi.dll")] private static extern int DismCheckImageHealth(uint session,[MarshalAs(UnmanagedType.Bool)]bool scan,IntPtr cancel,DismProgress progress,IntPtr data,out int health);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("DismApi.dll")] private static extern int DismCloseSession(uint session);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("DismApi.dll")] private static extern int DismShutdown();
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("DismApi.dll")] private static extern int DismGetLastErrorMessage(out IntPtr message);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32),DllImport("DismApi.dll")] private static extern int DismDelete(IntPtr data);
        private static void Check(int code){if(code==0)return;string detail="";IntPtr message;if(DismGetLastErrorMessage(out message)==0&&message!=IntPtr.Zero){try{detail=Marshal.PtrToStringUni(Marshal.ReadIntPtr(message));}finally{DismDelete(message);}}throw new IOException("DISM: 0x"+code.ToString("X8")+". "+detail);}
        internal static IntegrityResult DescribeDism(int state,bool scan){
            if(state==0)return new IntegrityResult{State=scan?"healthy":"not-marked",Summary=scan?"Повреждения хранилища компонентов не найдены. Это проверка компонентов Windows, а не всех программ и оборудования.":"Windows не хранит отметку о повреждении компонентов. Новое сканирование не выполнялось; для проверки сейчас выберите полную проверку."};
            if(state==1)return new IntegrityResult{State="repairable",Summary="Windows сообщает о повреждении хранилища компонентов, которое допускает восстановление. Эта проверка ничего не исправляла."};
            if(state==2)return new IntegrityResult{State="unrepairable",Summary="Windows сообщает о повреждении компонентов, которое DISM не считает исправимым этим способом. Потребуется отдельное восстановление Windows. Эта проверка ничего не исправляла."};
            throw new IOException("Windows вернула неизвестное состояние компонентов.");
        }
        internal static IntegrityResult CheckComponents(bool scan,EventWaitHandle cancel,Action<string> output){
            Check(DismInitialize(1,null,null));uint session=0;bool opened=false;
            try{Check(DismOpenSession("DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}",null,null,out session));opened=true;int previous=-1;Exception callbackError=null;
                DismProgress callback=(current,total,data)=>{if(total==0)return;int percent=(int)Math.Min(100,current*100.0/total);if(percent==previous)return;previous=percent;try{output("@progress|"+percent);}catch(Exception ex){callbackError=ex;cancel.Set();}};
                int state;int code=DismCheckImageHealth(session,scan,cancel.SafeWaitHandle.DangerousGetHandle(),callback,IntPtr.Zero,out state);GC.KeepAlive(callback);if(callbackError!=null)throw new IOException("Не удалось сохранить ход проверки.",callbackError);if(code!=0&&cancel.WaitOne(0))throw new OperationCanceledException("Проверка компонентов остановлена. Итоговое состояние не определено; повторите проверку позже.");Check(code);return DescribeDism(state,scan);
            }finally{if(opened)DismCloseSession(session);DismShutdown();}
        }
        internal static IntegrityResult DescribeSfc(string output,int exitCode){
            if(exitCode!=0)return new IntegrityResult{State="failed",Summary="SFC завершилась с кодом "+exitCode+". Проверка не подтверждена; изучите сообщения Windows в отчёте."};
            string text=(output??"").ToLowerInvariant();
            if(text.Contains("did not find any integrity violations")||text.Contains("не обнаружила нарушений целостности"))return new IntegrityResult{State="healthy",Summary="SFC не обнаружила нарушений целостности защищённых системных файлов. Исправление файлов не выполнялось."};
            if(text.Contains("found integrity violations")||text.Contains("обнаружила нарушения целостности")||text.Contains("found corrupt files")||text.Contains("обнаружила поврежденные файлы")||text.Contains("обнаружила повреждённые файлы"))return new IntegrityResult{State="issues",Summary="SFC сообщила о нарушениях целостности системных файлов. Режим проверки ничего не исправляет; подробности сохранены в отчёте и журнале CBS Windows."};
            return new IntegrityResult{State="review",Summary="SFC завершила команду. Автоматически определить итог по этому сообщению Windows не удалось — прочитайте результат ниже. Отсутствие повреждений не подтверждено."};
        }
        internal static IntegrityResult CheckFiles(Action<string> output,bool fixture){return RunFiles(output,fixture,false);}
        internal static IntegrityResult RepairFiles(Action<string> output,bool fixture){return RunFiles(output,fixture,true);}
        private static IntegrityResult RunFiles(Action<string> output,bool fixture,bool repair){
            if(fixture&&!Program.Hosted)throw new InvalidOperationException("SFC fixture is limited to hosted CI.");
            string arguments=fixture?(repair?"/scanfile=\"":"/verifyfile=\"")+Path.Combine(Environment.SystemDirectory,"kernel32.dll")+"\"":repair?"/scannow":"/verifyonly";
            var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"sfc.exe"),arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.Unicode,StandardErrorEncoding=Encoding.Unicode};
            var text=new StringBuilder();var sync=new object();Exception error=null;
            using(var process=new Process{StartInfo=info}){DataReceivedEventHandler append=(sender,e)=>{if(e.Data==null)return;lock(sync){try{text.AppendLine(e.Data);if(text.Length>150000)text.Remove(0,text.Length-150000);output(e.Data);}catch(Exception ex){error=ex;}}};process.OutputDataReceived+=append;process.ErrorDataReceived+=append;process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();if(error!=null)throw new IOException("Не удалось сохранить вывод SFC.",error);return repair?DescribeSfcRepair(text.ToString(),process.ExitCode):DescribeSfc(text.ToString(),process.ExitCode);}
        }
    }
}
