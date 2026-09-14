using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Wintools {
    internal sealed class TemperatureRow {internal string Name,Error;internal int? Celsius;}
    internal sealed class TemperatureSnapshot {internal TemperatureRow[] Rows=new TemperatureRow[0];internal string Error="";}
    internal static class GpuTemperatures {
        [StructLayout(LayoutKind.Sequential)] internal struct Reading {internal uint Version,Sensor;internal int Celsius;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Device(uint index,out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Name(IntPtr device,[Out] StringBuilder name,uint size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Temperature(IntPtr device,ref Reading reading);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LegacyTemperature(IntPtr device,uint sensor,out uint temperature);
        private static T Function<T>(IntPtr module,string name,bool optional=false) where T:class {var address=GetProcAddress(module,name);if(address==IntPtr.Zero){if(optional)return null;throw new IOException("Установленный драйвер не предоставляет необходимый интерфейс температуры.");}return Marshal.GetDelegateForFunctionPointer(address,typeof(T)) as T;}
        internal static int? ValidTemperature(int value){return value>=-100&&value<=200?(int?)value:null;}
        internal static TemperatureSnapshot Read(){
            var result=new TemperatureSnapshot();IntPtr module=IntPtr.Zero;Initialize shutdown=null;bool initialized=false;
            try{
                string path=null;foreach(var candidate in new[]{Path.Combine(Environment.SystemDirectory,"nvml.dll"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvml.dll")})if(File.Exists(candidate)){path=candidate;break;}
                if(path==null){result.Error="Драйвер NVIDIA с поддержкой чтения температуры не найден.";return result;}
                Program.SafeDirectory(Path.GetDirectoryName(path));if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Библиотека драйвера является ссылкой.");
                module=LoadLibraryEx(path,IntPtr.Zero,0x1100);if(module==IntPtr.Zero)throw new IOException("Не удалось загрузить интерфейс установленного драйвера NVIDIA.");
                var initialize=Function<Initialize>(module,"nvmlInit_v2");shutdown=Function<Initialize>(module,"nvmlShutdown");if(initialize()!=0)throw new IOException("Драйвер NVIDIA не готов к чтению датчиков.");initialized=true;
                var count=Function<Count>(module,"nvmlDeviceGetCount_v2");var device=Function<Device>(module,"nvmlDeviceGetHandleByIndex_v2");var name=Function<Name>(module,"nvmlDeviceGetName");var temperature=Function<Temperature>(module,"nvmlDeviceGetTemperatureV",true);var legacy=Function<LegacyTemperature>(module,"nvmlDeviceGetTemperature",true);
                uint total;if(count(out total)!=0||total>64)throw new IOException("Не удалось получить список видеокарт NVIDIA.");var rows=new List<TemperatureRow>();
                for(uint i=0;i<total;i++){
                    var row=new TemperatureRow{Name="GPU "+(i+1),Error=""};rows.Add(row);IntPtr handle;if(device(i,out handle)!=0||handle==IntPtr.Zero){row.Error="Видеокарта недоступна.";continue;}
                    var label=new StringBuilder(128);if(name(handle,label,128)==0&&label.Length>0)row.Name+=" · "+label.ToString();
                    int code=3;if(temperature!=null){var reading=new Reading{Version=0x0100000c,Sensor=0};code=temperature(handle,ref reading);if(code==0)row.Celsius=ValidTemperature(reading.Celsius);}
                    if(temperature==null&&legacy!=null){uint value;code=legacy(handle,0,out value);if(code==0&&value<=200)row.Celsius=(int)value;}
                    if(!row.Celsius.HasValue)row.Error=code==0?"Драйвер вернул некорректный замер.":"Датчик температуры недоступен (код драйвера "+code+").";
                }
                result.Rows=rows.ToArray();if(total==0)result.Error="Доступные видеокарты NVIDIA не найдены.";
            }catch(Exception ex){result.Error=ex.Message;}
            finally{if(initialized&&shutdown!=null)shutdown();if(module!=IntPtr.Zero)FreeLibrary(module);}
            return result;
        }
    }
}
