using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Wintools {
    internal sealed class ProcessRow {
        public int Id {get;set;}
        public string Name {get;set;}
        public long Started {get;set;}
        public long Memory {get;set;}
        public string Detail {get{return "PID "+Id+" · "+(Memory<0?"Память недоступна":(Memory/1048576.0).ToString("N0")+" МБ памяти");}}
    }
    internal sealed class ProcessSettings {
        internal int Id;
        internal long Started;
        internal uint Priority;
        internal ulong Affinity,SystemAffinity;
        internal bool SupportsAffinity;
    }
    internal sealed class ProcessHandle : SafeHandleZeroOrMinusOneIsInvalid {
        public ProcessHandle():base(true){}
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr value);
        protected override bool ReleaseHandle(){return CloseHandle(handle);}
    }
    internal static class ProcessControl {
        [DllImport("kernel32.dll",SetLastError=true)] private static extern ProcessHandle OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetProcessTimes(ProcessHandle process,out long creation,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern uint GetPriorityClass(ProcessHandle process);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetPriorityClass(ProcessHandle process,uint priority);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetProcessAffinityMask(ProcessHandle process,out UIntPtr mask,out UIntPtr system);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetProcessAffinityMask(ProcessHandle process,UIntPtr mask);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool IsProcessCritical(ProcessHandle process,out bool critical);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(ProcessHandle process,uint milliseconds);
        [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("advapi32.dll",SetLastError=true)] private static extern bool OpenProcessToken(ProcessHandle process,uint access,out ProcessHandle token);
        internal static readonly uint[] Priorities={64,16384,32,32768,128};
        internal static string PriorityName(uint value){return value==64?"Низкий":value==16384?"Ниже обычного":value==32?"Обычный":value==32768?"Выше обычного":value==128?"Высокий":"Неизвестно";}
        internal static ProcessRow[] Read(){return Process.GetProcesses().Select(p=>{using(p){var row=new ProcessRow{Id=p.Id,Name="Процесс "+p.Id,Memory=-1};try{row.Name=p.ProcessName;row.Memory=p.WorkingSet64;row.Started=p.StartTime.ToUniversalTime().ToFileTimeUtc();}catch(Win32Exception){}catch(InvalidOperationException){}return row;}}).OrderByDescending(p=>p.Memory).ToArray();}
        internal static ProcessHandle Open(int id,long started){
            if(id<=4||started<=0||id==GetCurrentProcessId())throw new InvalidOperationException("Этот процесс не поддерживает управление из Wintools.");var handle=OpenProcess(0x100000|0x1000|0x400|0x200,false,id);
            try{if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());long creation,exit,kernel,user;if(!GetProcessTimes(handle,out creation,out exit,out kernel,out user))throw new Win32Exception(Marshal.GetLastWin32Error());if(creation!=started||WaitForSingleObject(handle,0)!=258)throw new InvalidOperationException("Выбранный запуск программы уже завершён. Обновите список.");bool critical;if(!IsProcessCritical(handle,out critical))throw new Win32Exception(Marshal.GetLastWin32Error());if(critical)throw new InvalidOperationException("Критически важный процесс Windows не изменяется.");
                ProcessHandle token;if(!OpenProcessToken(handle,8,out token))throw new Win32Exception(Marshal.GetLastWin32Error());using(token)using(var identity=new WindowsIdentity(token.DangerousGetHandle()))using(var current=WindowsIdentity.GetCurrent()){if(identity.User!=current.User)throw new InvalidOperationException("Доступно управление только процессами вашего пользователя.");}return handle;
            }catch{handle.Dispose();throw;}
        }
        internal static ProcessSettings Inspect(int id,long started){using(var handle=Open(id,started))return Inspect(handle,id,started);}
        internal static ProcessSettings Inspect(ProcessHandle handle,int id,long started){
            if(WaitForSingleObject(handle,0)!=258)throw new InvalidOperationException("Процесс завершён.");uint priority=GetPriorityClass(handle);if(priority==0)throw new Win32Exception(Marshal.GetLastWin32Error());if(!Priorities.Contains(priority))throw new InvalidOperationException("Режим приоритета этого процесса не поддерживается.");UIntPtr mask,system;if(!GetProcessAffinityMask(handle,out mask,out system))throw new Win32Exception(Marshal.GetLastWin32Error());return new ProcessSettings{Id=id,Started=started,Priority=priority,Affinity=mask.ToUInt64(),SystemAffinity=system.ToUInt64(),SupportsAffinity=GetActiveProcessorCount(0xffff)<=64&&mask!=UIntPtr.Zero&&system!=UIntPtr.Zero};
        }
        internal static void ValidateTarget(string action,ulong target,ProcessSettings before){if(action=="priority"){if(target>uint.MaxValue||!Priorities.Contains((uint)target))throw new ArgumentException("Недопустимый приоритет.");}else if(action=="affinity"){if(!before.SupportsAffinity||target==0||(target&~before.SystemAffinity)!=0)throw new ArgumentException("Выберите хотя бы один доступный логический процессор. Системы с несколькими группами CPU этим редактором не поддерживаются.");}else throw new ArgumentException("Неизвестное действие процесса.");}
        internal static ulong Value(ProcessSettings settings,string action){return action=="priority"?settings.Priority:settings.Affinity;}
        internal static void Set(ProcessHandle handle,string action,ulong target,ProcessSettings before){ValidateTarget(action,target,before);bool result=action=="priority"?SetPriorityClass(handle,(uint)target):SetProcessAffinityMask(handle,new UIntPtr(target));if(!result)throw new Win32Exception(Marshal.GetLastWin32Error());}
    }
}
