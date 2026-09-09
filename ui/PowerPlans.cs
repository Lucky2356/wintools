using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Wintools {
    internal sealed class PowerPlan {
        public string Id {get;set;}
        public string Name {get;set;}
        public string Description {get;set;}
        public bool Active {get;set;}
        public string Label {get{return Name+(Active?" · Используется сейчас":"");}}
    }
    internal sealed class PowerSnapshot {
        internal string Active;
        internal PowerPlan[] Plans;
    }
    internal static class PowerPlans {
        [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root,IntPtr scheme,IntPtr subgroup,uint access,uint index,byte[] buffer,ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root,ref Guid scheme,IntPtr subgroup,IntPtr setting,byte[] buffer,ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerReadDescription(IntPtr root,ref Guid scheme,IntPtr subgroup,IntPtr setting,byte[] buffer,ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root,ref Guid scheme);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        internal static bool ValidId(string id){Guid value;return Guid.TryParseExact(id,"D",out value)&&value!=Guid.Empty;}
        internal static string Active(){IntPtr pointer;Check(PowerGetActiveScheme(IntPtr.Zero,out pointer));if(pointer==IntPtr.Zero)throw new InvalidOperationException("Windows не вернула схему питания.");try{return ((Guid)Marshal.PtrToStructure(pointer,typeof(Guid))).ToString("D");}finally{LocalFree(pointer);}}
        private static void Check(uint code){if(code!=0)throw new Win32Exception((int)code);}
        private static string Text(Guid id,bool description){
            uint size=0;uint code=description?PowerReadDescription(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,null,ref size):PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,null,ref size);
            if(code!=0&&code!=234)Check(code);if(size==0)return "";if(size>65536)throw new InvalidOperationException("Слишком длинное описание схемы питания.");
            var buffer=new byte[size];code=description?PowerReadDescription(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,buffer,ref size):PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,buffer,ref size);Check(code);return Encoding.Unicode.GetString(buffer,0,(int)size).TrimEnd('\0');
        }
        internal static PowerSnapshot Read(){
            string active=Active();var result=new List<PowerPlan>();
            for(uint index=0;index<4096;index++){uint size=16;var buffer=new byte[16];uint code=PowerEnumerate(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,16,index,buffer,ref size);if(code==259)return new PowerSnapshot{Active=active,Plans=result.ToArray()};Check(code);if(size!=16)throw new InvalidOperationException("Некорректный идентификатор схемы питания.");var guid=new Guid(buffer);string name=Text(guid,false),description;try{description=Text(guid,true);}catch(Win32Exception){description="Описание Windows недоступно.";}result.Add(new PowerPlan{Id=guid.ToString("D"),Name=string.IsNullOrWhiteSpace(name)?guid.ToString("D"):name,Description=description,Active=guid.ToString("D")==active});}
            throw new InvalidOperationException("Не удалось полностью перечислить схемы питания.");
        }
        internal static void Select(string id){if(!ValidId(id))throw new ArgumentException("Некорректная схема питания.");var guid=new Guid(id);Check(PowerSetActiveScheme(IntPtr.Zero,ref guid));if(Active()!=id.ToLowerInvariant())throw new InvalidOperationException("Windows не подтвердила переключение схемы. Обновите список.");}
    }
}
