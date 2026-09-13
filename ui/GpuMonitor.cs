using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Wintools {
    internal sealed class GpuAdapter {
        public string Name {get;set;}
        internal string Id;
        internal ulong Dedicated,SharedLimit;
    }
    internal static class GpuInventory {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        internal struct Description {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] internal string Name;
            internal uint Vendor,Device,Subsystem,Revision;
            internal UIntPtr DedicatedVideo,DedicatedSystem,SharedSystem;
            internal uint LuidLow;
            internal int LuidHigh;
            internal uint Flags;
        }
        [DllImport("dxgi.dll",ExactSpelling=true)] private static extern int CreateDXGIFactory1(ref Guid id,out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(IntPtr factory,uint index,out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Describe(IntPtr adapter,out Description description);
        private static Delegate Method(IntPtr instance,int slot,Type type){return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),type);}
        internal static GpuAdapter[] Read(){
            var id=new Guid("770aae78-f26f-4dba-a829-253c83d1b387");IntPtr factory;Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref id,out factory));var rows=new List<GpuAdapter>();
            try{var enumerate=(Enumerate)Method(factory,12,typeof(Enumerate));for(uint index=0;index<64;index++){IntPtr adapter;int result=enumerate(factory,index,out adapter);if(result==unchecked((int)0x887a0002))return rows.ToArray();Marshal.ThrowExceptionForHR(result);
                    try{Description desc;Marshal.ThrowExceptionForHR(((Describe)Method(adapter,10,typeof(Describe)))(adapter,out desc));if((desc.Flags&2)==0)rows.Add(new GpuAdapter{Name=desc.Name,Id="0x"+unchecked((uint)desc.LuidHigh).ToString("x8")+"_0x"+desc.LuidLow.ToString("x8"),Dedicated=desc.DedicatedVideo.ToUInt64(),SharedLimit=desc.SharedSystem.ToUInt64()});}finally{Marshal.Release(adapter);}}
                throw new IOException("Windows вернула слишком много видеоадаптеров.");
            }finally{Marshal.Release(factory);}
        }
    }
    internal sealed class GpuSample {internal Dictionary<string,double> Usage=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);internal string Error="";}
    internal sealed class GpuReader {
        private Dictionary<string,CounterSample> previous=new Dictionary<string,CounterSample>();
        private long stamp;
        private static readonly Regex Instance=new Regex(@"\Apid_\d+_luid_(?<id>0x[0-9a-f]+_0x[0-9a-f]+)_phys_(?<physical>\d+)_eng_(?<engine>\d+)_",RegexOptions.IgnoreCase);
        internal static Dictionary<string,double> Aggregate(IEnumerable<KeyValuePair<string,double>> values){
            var engines=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);var adapters=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var value in values){var match=Instance.Match(value.Key);if(!match.Success||double.IsNaN(value.Value)||double.IsInfinity(value.Value)||value.Value<0||value.Value>100)continue;string key=match.Groups["id"].Value+"_"+match.Groups["physical"].Value+"_"+match.Groups["engine"].Value;double prior;engines.TryGetValue(key,out prior);engines[key]=Math.Min(100,prior+value.Value);adapters[key]=match.Groups["id"].Value;}
            var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);foreach(var engine in engines){string adapter=adapters[engine.Key];double prior;result.TryGetValue(adapter,out prior);result[adapter]=Math.Max(prior,engine.Value);}return result;
        }
        internal GpuSample Read(){
            var result=new GpuSample();long now=Stopwatch.GetTimestamp();bool consecutive=stamp!=0&&(now-stamp)/(double)Stopwatch.Frequency<=5;var next=new Dictionary<string,CounterSample>();var values=new List<KeyValuePair<string,double>>();
            try{{var category=new PerformanceCounterCategory("GPU Engine");var counters=category.ReadCategory();var utilization=counters["Utilization Percentage"];if(utilization==null)throw new IOException("Счётчик загрузки GPU недоступен.");
                    foreach(InstanceData data in utilization.Values){var current=data.Sample;next[data.InstanceName]=current;CounterSample old;if(consecutive&&previous.TryGetValue(data.InstanceName,out old)&&current.RawValue>=old.RawValue&&current.TimeStamp100nSec>old.TimeStamp100nSec)values.Add(new KeyValuePair<string,double>(data.InstanceName,CounterSample.Calculate(old,current)));}}
                result.Usage=Aggregate(values);if(result.Usage.Count==0)result.Error=consecutive?"Драйвер не предоставил подходящих счётчиков нагрузки GPU.":"Первый замер GPU; нужен следующий интервал.";
            }catch(Exception ex){result.Error="Загрузка GPU недоступна: "+ex.Message;next.Clear();}
            previous=next;stamp=now;return result;
        }
    }
}
