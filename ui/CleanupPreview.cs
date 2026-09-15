using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Wintools {
    internal sealed class CleanupEstimate {internal long Bytes;internal int Files,SkippedLinks,Errors;internal string Error="",Source;internal DateTime Captured;}
    internal static class CleanupPreview {
        internal static readonly string[] Ids={"CLN-USERTEMP","CLN-WINTEMP","CLN-CRASHDUMPS"};
        internal static CleanupEstimate Read(string id,CancellationToken cancel){
            string directory;int days;if(id==Ids[0]){directory=Environment.GetEnvironmentVariable("TEMP");days=3;}else if(id==Ids[1]){directory=Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"),"Temp");days=3;}else if(id==Ids[2]){directory=Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"),"CrashDumps");days=7;}else throw new ArgumentException("Неизвестная категория очистки.");
            return Scan(directory,DateTime.Now.AddDays(-days),cancel,250000);
        }
        internal static CleanupEstimate Scan(string directory,DateTime cutoff,CancellationToken cancel,int limit){
            var result=new CleanupEstimate{Captured=DateTime.Now,Source=directory};
            try{
                cancel.ThrowIfCancellationRequested();if(!Path.IsPathRooted(directory)||directory.StartsWith(@"\\"))throw new IOException("Расчёт поддерживает только локальные папки.");string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);if(root==Path.GetPathRoot(root).TrimEnd(Path.DirectorySeparatorChar))throw new IOException("Корень диска не является папкой временных файлов.");
                for(var ancestor=new DirectoryInfo(root);ancestor!=null;ancestor=ancestor.Parent){try{if((File.GetAttributes(ancestor.FullName)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка проходит через ссылку или junction.");}catch(DirectoryNotFoundException){if(ancestor.FullName==root)return result;throw;}catch(FileNotFoundException){if(ancestor.FullName==root)return result;throw;}}
                var pending=new Stack<string>();pending.Push(root);int visited=0;
                while(pending.Count>0){cancel.ThrowIfCancellationRequested();var current=pending.Pop();try{
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0){result.SkippedLinks++;continue;}
                    foreach(string child in Directory.EnumerateFileSystemEntries(current)){cancel.ThrowIfCancellationRequested();if(++visited>limit)throw new InvalidOperationException("Папка содержит слишком много объектов; расчёт неполный.");
                        try{string path=Path.GetFullPath(child);if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Объект вне папки очистки.");var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0){result.SkippedLinks++;continue;}if((attributes&FileAttributes.Directory)!=0){pending.Push(path);continue;}var file=new FileInfo(path);if(file.LastWriteTime>=cutoff)continue;result.Bytes=checked(result.Bytes+file.Length);result.Files++;}
                        catch(IOException){result.Errors++;}catch(UnauthorizedAccessException){result.Errors++;}
                    }
                }catch(IOException){result.Errors++;}catch(UnauthorizedAccessException){result.Errors++;}}
                if(result.Errors>0)result.Error="Не удалось прочитать часть файлов или папок. Для системных папок могут понадобиться права администратора.";
            }catch(OperationCanceledException){throw;}catch(Exception ex){result.Errors++;result.Error=ex.Message;}return result;
        }
        internal static string Size(long bytes){return (bytes/1048576.0).ToString("N1")+" МБ";}
    }
}
