using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;

namespace Wintools {
    internal sealed class DesktopShortcut {
        public string Name {get;set;}
        internal string Path,Target,Arguments,Directory;
    }
    internal sealed class DesktopLaunchInventory {
        internal DesktopShortcut[] Entries=new DesktopShortcut[0];
        internal int Errors;
    }
    internal static class DesktopLaunch {
        [ComImport,Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink {}
        [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface Link {
            void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int size,IntPtr data,uint flags);
            void GetIDList(out IntPtr id);void SetIDList(IntPtr id);
            void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder text,int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
            void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder text,int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetHotkey(out short key);void SetHotkey(short key);void GetShowCmd(out int value);void SetShowCmd(int value);
            void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int size,out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path,int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path,uint reserved);void Resolve(IntPtr window,uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
        private static void SafeDirectory(string path){for(var item=new DirectoryInfo(path);item!=null;item=item.Parent)if(item.Exists&&(item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка ярлыков проходит через ссылку или junction.");}
        internal static bool Local(string path){return !string.IsNullOrWhiteSpace(path)&&Regex.IsMatch(path,@"^[A-Za-z]:[\\/]");}
        internal static DesktopShortcut ReadShortcut(string path){
            if(!Local(path)||!string.Equals(System.IO.Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase))throw new IOException("Ярлык недоступен.");
            SafeDirectory(System.IO.Path.GetDirectoryName(path));var file=new FileInfo(path);if(!file.Exists||file.Length>1048576||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Ярлык недоступен или является ссылкой.");
            var value=(Link)new ShellLink();try{
                ((IPersistFile)value).Load(path,0);var target=new StringBuilder(32768);var args=new StringBuilder(32768);var directory=new StringBuilder(32768);
                value.GetPath(target,target.Capacity,IntPtr.Zero,4);value.GetArguments(args,args.Capacity);value.GetWorkingDirectory(directory,directory.Capacity);
                if(target.Length>=259||args.Length>=32767||directory.Length>=32767)throw new IOException("Слишком длинная команда ярлыка.");
                return new DesktopShortcut{Name=System.IO.Path.GetFileNameWithoutExtension(path),Path=System.IO.Path.GetFullPath(path),Target=Environment.ExpandEnvironmentVariables(target.ToString()),Arguments=args.ToString(),Directory=Environment.ExpandEnvironmentVariables(directory.ToString())};
            }finally{Marshal.FinalReleaseComObject(value);}
        }
        internal static DesktopLaunchInventory Read(){
            var result=new DesktopLaunchInventory();var rows=new List<DesktopShortcut>();int count=0;
            foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)}.Distinct(StringComparer.OrdinalIgnoreCase)){
                if(!Local(root)||!System.IO.Directory.Exists(root))continue;var pending=new Stack<string>();pending.Push(root);
                while(pending.Count>0){var directory=pending.Pop();try{SafeDirectory(directory);foreach(var path in System.IO.Directory.EnumerateFileSystemEntries(directory)){
                    if(++count>10000){result.Errors++;result.Entries=rows.ToArray();return result;}var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)continue;
                    if((attributes&FileAttributes.Directory)!=0){pending.Push(path);continue;}if(!string.Equals(System.IO.Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase))continue;
                    try{var entry=ReadShortcut(path);if(Launchable(entry))rows.Add(entry);}catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException||ex is COMException||ex is ArgumentException||ex is System.Security.SecurityException))throw;result.Errors++;}
                }}catch(IOException){result.Errors++;}catch(UnauthorizedAccessException){result.Errors++;}catch(System.Security.SecurityException){result.Errors++;}}
            }result.Entries=rows.ToArray();return result;
        }
        internal static bool Launchable(DesktopShortcut entry){return entry!=null&&Local(entry.Target)&&string.Equals(System.IO.Path.GetExtension(entry.Target),".exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(entry.Target)&&!Regex.IsMatch(entry.Name+" "+System.IO.Path.GetFileNameWithoutExtension(entry.Target),@"\b(unins\w*|uninstall\w*|удал\w*|repair|восстанов\w*|setup|install|update\w*)\b",RegexOptions.IgnoreCase);}
        internal static DesktopShortcut[] Match(InstalledApplication app,DesktopLaunchInventory inventory){
            if(app==null||app.Package!=null)return new DesktopShortcut[0];string root=Environment.ExpandEnvironmentVariables(app.Location??"").TrimEnd('\\','/');
            bool folder=Local(root)&&root.Length>3&&!new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.Windows)}.Any(p=>string.Equals(p,root,StringComparison.OrdinalIgnoreCase));
            return inventory.Entries.Where(e=>Launchable(e)&&(string.Equals(e.Name,app.Name,StringComparison.CurrentCultureIgnoreCase)||(folder&&e.Target.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)))).OrderBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).GroupBy(e=>e.Target.ToUpperInvariant()+"\n"+e.Arguments+"\n"+e.Directory.ToUpperInvariant(),StringComparer.Ordinal).Select(g=>g.First()).ToArray();
        }
        internal static ProcessStartInfo StartInfo(DesktopShortcut before,DesktopShortcut current){
            if(!Launchable(current)||before.Path!=current.Path||before.Target!=current.Target||before.Arguments!=current.Arguments||before.Directory!=current.Directory)throw new IOException("Ярлык изменился или файл программы исчез. Обновите список и выберите его снова.");
            return new ProcessStartInfo(current.Target,current.Arguments){UseShellExecute=true,WorkingDirectory=Local(current.Directory)&&System.IO.Directory.Exists(current.Directory)?current.Directory:System.IO.Path.GetDirectoryName(current.Target)};
        }
        internal static void Fixture(string path,string target,string arguments){
            var value=(Link)new ShellLink();try{value.SetPath(target);value.SetArguments(arguments);value.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));((IPersistFile)value).Save(path,true);}finally{Marshal.FinalReleaseComObject(value);}
        }
    }
}
