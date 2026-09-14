using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace Wintools {
    internal sealed class ServiceDependencyNode {
        internal string Name,Label,State,Error="";
        internal bool Driver;
        internal string Description {get{return Label+" ("+Name+")\n"+(Error.Length>0?"Состояние недоступно: "+Error:new ServiceState{State=State}.RunningLabel)+(Driver?" · Драйвер":"");}}
    }
    internal sealed class ServiceDependencySnapshot {
        internal ServiceDependencyNode Root;
        internal ServiceDependencyNode[] Requires=new ServiceDependencyNode[0],Dependents=new ServiceDependencyNode[0];
        internal string[] Groups=new string[0];
        internal string RequiredError="",DependentError="";
    }
    internal static class ServiceDependencies {
        [StructLayout(LayoutKind.Sequential)] private struct Configuration {internal uint Type,Start,Error;internal IntPtr Binary,Group;internal uint Tag;internal IntPtr Dependencies,Account,Display;}
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr OpenSCManager(string machine,string database,uint access);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr OpenService(IntPtr manager,string name,uint access);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool QueryServiceConfig(IntPtr service,IntPtr buffer,uint size,out uint needed);
        [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
        internal static string[] ParseNames(char[] data){
            var names=new List<string>();int start=0;for(int i=0;i<data.Length;i++)if(data[i]=='\0'){if(i==start)return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var name=new string(data,start,i-start);if(name.Length>257||!ServiceActions.ValidName(name[0]=='+'?name.Substring(1):name))throw new IOException("Некорректная зависимость службы.");names.Add(name);start=i+1;}throw new IOException("Список зависимостей Windows не завершён.");
        }
        private static string[] RequiredNames(string name){
            var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new Win32Exception();try{var service=OpenService(manager,name,1);if(service==IntPtr.Zero)throw new Win32Exception();try{uint needed;QueryServiceConfig(service,IntPtr.Zero,0,out needed);int error=Marshal.GetLastWin32Error();if(error!=122)throw new Win32Exception(error);if(needed<Marshal.SizeOf(typeof(Configuration))||needed>65536)throw new IOException("Неожиданный размер конфигурации службы.");var buffer=Marshal.AllocHGlobal((int)needed);try{uint actual;if(!QueryServiceConfig(service,buffer,needed,out actual))throw new Win32Exception();var config=(Configuration)Marshal.PtrToStructure(buffer,typeof(Configuration));if(config.Dependencies==IntPtr.Zero)return new string[0];long offset=config.Dependencies.ToInt64()-buffer.ToInt64();if(offset<Marshal.SizeOf(typeof(Configuration))||offset>=needed||(offset&1)!=0)throw new IOException("Некорректный указатель зависимостей.");var chars=new char[(needed-(int)offset)/2];Marshal.Copy(config.Dependencies,chars,0,chars.Length);return ParseNames(chars);}finally{Marshal.FreeHGlobal(buffer);}}finally{CloseServiceHandle(service);}}finally{CloseServiceHandle(manager);}
        }
        private static ServiceDependencyNode Node(ServiceController service){
            var node=new ServiceDependencyNode{Name=service.ServiceName,Label=service.ServiceName,State="Unknown"};try{node.Label=service.DisplayName;node.State=service.Status.ToString();node.Driver=(service.ServiceType&(ServiceType.KernelDriver|ServiceType.FileSystemDriver|ServiceType.RecognizerDriver))!=0;}catch(Exception ex){node.Error=ex.Message;}return node;
        }
        internal static ServiceDependencySnapshot Read(string name){
            if(!ServiceActions.ValidName(name))throw new ArgumentException("Некорректное имя службы.");var result=new ServiceDependencySnapshot();using(var root=new ServiceController(name)){result.Root=Node(root);
                try{var required=RequiredNames(name);result.Groups=required.Where(n=>n.StartsWith("+",StringComparison.Ordinal)).Select(n=>n.Substring(1)).ToArray();var rows=new List<ServiceDependencyNode>();foreach(var item in required.Where(n=>!n.StartsWith("+",StringComparison.Ordinal))){using(var service=new ServiceController(item))rows.Add(Node(service));}result.Requires=rows.ToArray();}catch(Exception ex){result.RequiredError=ex.Message;}
                try{var dependents=root.DependentServices;try{result.Dependents=dependents.Take(200).Select(Node).ToArray();if(dependents.Length>200)result.DependentError="Показаны первые 200 из "+dependents.Length+" связей.";}finally{foreach(var service in dependents)service.Dispose();}}catch(Exception ex){result.DependentError=ex.Message;}
            }return result;
        }
    }
}
