using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task DiagnosticBundleSmoke(){
            string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var masked=DiagnosticBundle.Mask("Path "+profile+"\\Desktop by "+Environment.UserName+" on "+Environment.MachineName+" S-1-5-21-1-2-3-1001");
            Assert(!masked.Contains(profile)&&masked.Contains("%USERPROFILE%\\Desktop")&&masked.Contains("<sid>")&&!masked.Contains("S-1-5-21-1-2-3"),"Diagnostics masking incomplete: "+masked);
            var error=Path.Combine(Program.Data,"ui-error.txt");bool existed=File.Exists(error);if(!existed)File.WriteAllText(error,"Test error at "+profile+"\\file.txt");
            string path=null;
            try{Get<Button>("DiagnosticBundle").RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));for(int i=0;i<100&&!Get<TextBlock>("Status").Text.StartsWith("Пакет");i++)await Task.Delay(30);
                Assert(Get<TextBlock>("Status").Text.StartsWith("Пакет диагностики сохранён"),"Diagnostics bundle not created: "+Get<TextBlock>("Status").Text);
                path=new DirectoryInfo(Path.Combine(Program.Data,"reports")).GetFiles("diagnostics-*.zip").OrderByDescending(f=>f.LastWriteTimeUtc).First().FullName;
                using(var archive=ZipFile.OpenRead(path)){Assert(archive.GetEntry("summary.txt")!=null&&archive.GetEntry("ui-error.txt")!=null,"Bundle entries missing");using(var reader=new StreamReader(archive.GetEntry("ui-error.txt").Open())){var text=reader.ReadToEnd();Assert(!text.Contains(profile)&&text.Contains("%USERPROFILE%"),"Bundle not masked");}using(var reader=new StreamReader(archive.GetEntry("summary.txt").Open()))Assert(reader.ReadToEnd().Contains(Program.Version),"Summary without version");}
            }finally{if(!existed&&File.Exists(error))File.Delete(error);if(path!=null)File.Delete(path);Text("Status","Готово к работе");}
        }
    }
}
