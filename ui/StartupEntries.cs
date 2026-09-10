using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Wintools {
    internal sealed class StartupEntry {
        public string Source {get;set;}
        public string Name {get;set;}
        public string Command {get;set;}
        public string Error {get;set;}
        internal string Approval,Identity;
        public bool? Enabled {get{return Error==null?StartupEntries.Decode(Approval):null;}}
        public string State {get{return Enabled.HasValue?(Enabled.Value?"Включено":"Отключено"):"Неизвестно";}}
        public string Location {get{return StartupEntries.Location(Source);}}
        public string Key {get{return Source+"|"+Name;}}
        internal string Fingerprint {get{return StartupEntries.Hash(Identity+"|"+(Approval??"missing"));}}
    }
    internal sealed class StartupSnapshot {
        internal StartupEntry[] Entries;
        internal string[] Errors;
    }
    internal static class StartupEntries {
        internal const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ApprovedPath=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        internal static readonly string[] Sources={"user-run","machine-run","machine-run32","user-folder","machine-folder"};
        internal static void Validate(string source,string name){if(!Sources.Contains(source)||string.IsNullOrWhiteSpace(name)||name.Length>16383||name.Any(char.IsControl)||name.IndexOf('\0')>=0)throw new ArgumentException("Некорректная запись автозагрузки.");if(source.EndsWith("folder")&&(name!=Path.GetFileName(name)||name=="."||name==".."))throw new ArgumentException("Некорректное имя файла автозагрузки.");}
        internal static string Location(string source){return source=="user-run"?"Мой вход · реестр":source=="machine-run"?"Все пользователи · реестр":source=="machine-run32"?"Все пользователи · реестр 32 бит":source=="user-folder"?"Мой вход · папка":"Все пользователи · папка";}
        internal static string ApprovalKey(string source){return ApprovedPath+(source.EndsWith("folder")?"StartupFolder":source=="machine-run32"?"Run32":"Run");}
        internal static RegistryKey Root(string source,bool approval){return RegistryKey.OpenBaseKey(source.StartsWith("user-")?RegistryHive.CurrentUser:RegistryHive.LocalMachine,!approval&&source=="machine-run32"?RegistryView.Registry32:RegistryView.Registry64);}
        internal static string Folder(string source){return Environment.GetFolderPath(source=="user-folder"?Environment.SpecialFolder.Startup:Environment.SpecialFolder.CommonStartup);}
        internal static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        internal static bool? Decode(string encoded){
            if(encoded==null)return true;
            byte[] bytes;try{bytes=Convert.FromBase64String(encoded);}catch(FormatException){return null;}
            if(bytes.Length!=12||bytes[1]!=0||bytes[2]!=0||bytes[3]!=0)return null;
            if(bytes[0]==2||bytes[0]==6)return true;if(bytes[0]==3||bytes[0]==7)return false;return null;
        }
        internal static string Encode(string previous,bool enabled){
            if(!Decode(previous).HasValue)throw new IOException("Формат состояния Windows не распознан. Изменение недоступно.");
            var bytes=new byte[12];byte family=previous!=null&&Convert.FromBase64String(previous)[0]>=6?(byte)6:(byte)2;bytes[0]=(byte)(family+(enabled?0:1));if(!enabled)Buffer.BlockCopy(BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()),0,bytes,4,8);return Convert.ToBase64String(bytes);
        }
        internal static StartupEntry Inspect(string source,string name){
            Validate(source,name);var row=new StartupEntry{Source=source,Name=name};
            if(source.EndsWith("folder")){
                string folder=Folder(source);if(string.IsNullOrEmpty(folder))throw new IOException("Папка автозагрузки недоступна.");string path=Path.Combine(folder,name);var info=new FileInfo(path);if(!info.Exists)throw new IOException("Запись больше не существует.");if((info.Attributes&(FileAttributes.Directory|FileAttributes.ReparsePoint))!=0||info.Length>2097152)throw new IOException("Файл автозагрузки нельзя надёжно проверить.");
                row.Command=path;using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())row.Identity=Hash(source+"|"+name+"|"+Convert.ToBase64String(sha.ComputeHash(stream)));
            }else using(var root=Root(source,false))using(var key=root.OpenSubKey(RunPath)){
                if(key==null||!key.GetValueNames().Contains(name,StringComparer.OrdinalIgnoreCase))throw new IOException("Запись больше не существует.");var kind=key.GetValueKind(name);if(kind!=RegistryValueKind.String&&kind!=RegistryValueKind.ExpandString)throw new IOException("Неизвестный тип команды автозагрузки.");row.Command=(string)key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);row.Identity=Hash(source+"|"+name+"|"+kind+"|"+row.Command);
            }
            using(var root=Root(source,true))using(var key=root.OpenSubKey(ApprovalKey(source))){
                if(key!=null&&key.GetValueNames().Contains(name,StringComparer.OrdinalIgnoreCase)){if(key.GetValueKind(name)!=RegistryValueKind.Binary)throw new IOException("Неизвестный тип состояния автозагрузки.");row.Approval=Convert.ToBase64String((byte[])key.GetValue(name));}
            }
            if(!row.Enabled.HasValue)row.Error="Windows использует незнакомый формат состояния. Изменение недоступно.";return row;
        }
        internal static StartupSnapshot Read(){
            var entries=new List<StartupEntry>();var errors=new List<string>();
            foreach(string source in Sources){if(source=="machine-run32"&&!Environment.Is64BitOperatingSystem)continue;try{
                string[] names;if(source.EndsWith("folder")){string folder=Folder(source);if(string.IsNullOrEmpty(folder))throw new IOException("Папка не определена.");names=Directory.Exists(folder)?Directory.GetFiles(folder).Select(Path.GetFileName).Where(n=>!n.Equals("desktop.ini",StringComparison.OrdinalIgnoreCase)).ToArray():new string[0];}
                else using(var root=Root(source,false))using(var key=root.OpenSubKey(RunPath))names=key==null?new string[0]:key.GetValueNames();
                foreach(string name in names)try{entries.Add(Inspect(source,name));}catch(Exception ex){entries.Add(new StartupEntry{Source=source,Name=name,Error=ex.Message,Command="Не удалось прочитать запись"});}
            }catch(Exception ex){errors.Add(Location(source)+": "+ex.Message);}}
            return new StartupSnapshot{Entries=entries.OrderBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ToArray(),Errors=errors.ToArray()};
        }
        internal static void WriteApproval(string source,string name,string value){
            Validate(source,name);if(value!=null&&!Decode(value).HasValue)throw new IOException("Некорректное сохраняемое состояние.");
            using(var root=Root(source,true))using(var key=root.CreateSubKey(ApprovalKey(source))){if(value==null)key.DeleteValue(name,false);else key.SetValue(name,Convert.FromBase64String(value),RegistryValueKind.Binary);key.Flush();}
        }
    }
}
