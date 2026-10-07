using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task PackageSmoke(){
            Assert(Packages.Catalog.All(p=>Packages.ValidId(p.Id))&&Packages.Catalog.Select(p=>p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()==Packages.Catalog.Length,"Package catalogue IDs invalid or duplicated");
            foreach(var unsafeId in new[]{"7zip.7zip --override x","a&b","\"x\"","../x"})Assert(!Packages.ValidId(unsafeId),"Unsafe package ID accepted: "+unsafeId);
            bool rejected=false;try{Packages.Arguments("install","Unknown.Package");}catch(ArgumentException){rejected=true;}Assert(rejected,"Package outside the catalogue accepted");
            Assert(Packages.Arguments("install","7zip.7zip")=="install --id 7zip.7zip --exact --source winget --silent "+Packages.Agreements,"Install arguments changed");
            Assert(Packages.Succeeded(0)&&Packages.Succeeded(unchecked((int)0x8A150061))&&Packages.Succeeded(unchecked((int)0x8A15002B))&&!Packages.Succeeded(1)&&Packages.Describe(unchecked((int)0x8A150061),"install")=="Уже установлено","winget result codes misread");
            Assert(Packages.ParseExport("{\"Sources\":[{\"Packages\":[{\"PackageIdentifier\":\"7zip.7zip\"},{\"PackageIdentifier\":\"bad id\"}],\"SourceDetails\":{\"Name\":\"winget\"}}]}").SetEquals(new[]{"7zip.7zip"}),"winget export parsed incorrectly");
            Assert(!Packages.Meaningful("  \\ ")&&!Packages.Meaningful("██████▒▒▒  1.2 MB / 3 MB")&&Packages.Meaningful("Successfully installed"),"winget progress filter incorrect");
            var locate=wingetLocate;var inventory=packageInventory;var run=packageRun;var calls=new List<string>();var installed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"VideoLAN.VLC"};
            try{
                wingetLocate=()=>null;ShowPage(14);await RefreshPackages();Assert(!packageUpgradeAll.IsEnabled&&packageStore.Visibility==Visibility.Visible&&packageStatus.Text.Contains("не найден"),"Missing winget not explained");
                wingetLocate=()=>"v1.11.400";packageInventory=()=>Task.FromResult(new HashSet<string>(installed,StringComparer.OrdinalIgnoreCase));
                packageRun=(arguments,output)=>{calls.Add(arguments);output("Successfully installed");if(arguments.Contains("Discord.Discord"))return Task.FromResult((long)unchecked((int)0x8A150011));var id=arguments.Split(' ').SkipWhile(a=>a!="--id").Skip(1).FirstOrDefault();if(id!=null)installed.Add(id);return Task.FromResult(0L);};
                await RefreshPackages();Assert(packageStates["VideoLAN.VLC"].Text.Contains("Установлено")&&packageStates["7zip.7zip"].Text=="Не установлено"&&packageStore.Visibility==Visibility.Collapsed,"Installed packages not marked");
                Assert(!packageInstall.IsEnabled,"Install enabled without selection");packageChecks["7zip.7zip"].IsChecked=true;packageChecks["Discord.Discord"].IsChecked=true;RefreshPackagesEnabled();Assert(packageInstall.IsEnabled,"Install disabled with selection");
                SetBusy(true);Assert(!packageInstall.IsEnabled&&!packageCheck.IsEnabled,"Package controls ignored operation lock");SetBusy(false);
                var cancel=RunPackages("install");Assert(confirmation!=null&&Get<TextBlock>("ConfirmText").Text.Contains("лицензионные"),"Install skipped licence confirmation");FinishConfirmation(false);await cancel;Assert(calls.Count==0,"Cancelled install executed");
                var install=RunPackages("install");FinishConfirmation(true);await install;
                Assert(calls.Count==2&&calls[0].StartsWith("install --id 7zip.7zip ")&&calls[1].StartsWith("install --id Discord.Discord "),"Selected packages not installed in order: "+string.Join(" | ",calls));
                Assert(packageStates["7zip.7zip"].Text.Contains("Установлено")&&Get<TextBlock>("Status").Text.Contains("с ошибкой: 1")&&Get<TextBox>("Output").Text.Contains("Ошибка winget 0x8A150011"),"Install result not reported");
                ReadHistory();var rows=Get<ListBox>("History").Items.Cast<HistoryRow>().Where(r=>r.PackageChange).ToArray();Assert(rows.Length==2&&rows.Count(r=>r.CanRevert)==1&&rows.Single(r=>r.CanRevert).Title.Contains("7-Zip"),"Package history missing or failed install revertible");
                var uninstall=UninstallPackageFromHistory(rows.Single(r=>r.CanRevert).Run);for(int i=0;i<100&&confirmation==null&&!uninstall.IsCompleted;i++)await Task.Delay(20);Assert(confirmation!=null&&Get<TextBlock>("ConfirmText").Text.Contains("7-Zip"),"Uninstall skipped confirmation");FinishConfirmation(true);await uninstall;
                Assert(calls.Last().StartsWith("uninstall --id 7zip.7zip --exact")&&!calls.Last().Contains("package-agreements"),"Uninstall arguments incorrect: "+calls.Last());ReadHistory();rows=Get<ListBox>("History").Items.Cast<HistoryRow>().Where(r=>r.PackageChange).ToArray();Assert(rows.Length==3&&!rows.Any(r=>r.CanRevert)&&rows.Any(r=>r.Title.StartsWith("Удаление: 7-Zip")),"Uninstall not recorded or install still revertible");
                calls.Clear();installed.Add("7zip.7zip");packageChecks["Discord.Discord"].IsChecked=false;packageChecks["VideoLAN.VLC"].IsChecked=true;packageRun=(arguments,output)=>{calls.Add(arguments);stopPackages=true;return Task.FromResult(0L);};var stop=RunPackages("upgrade");FinishConfirmation(true);await stop;Assert(calls.Count==1&&calls[0].StartsWith("upgrade --id 7zip.7zip "),"Stop request ignored");
                calls.Clear();var all=RunPackages("upgrade-all");Assert(Get<TextBlock>("ConfirmText").Text.Contains("не через Wintools"),"Upgrade-all scope not explained");FinishConfirmation(true);await all;Assert(calls.Count==1&&calls[0].StartsWith("upgrade --all "),"Upgrade all not requested");
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;ShowPage(14);Window.UpdateLayout();await Task.Delay(80);Assert(packageInstall.IsVisible&&packageChecks["7zip.7zip"].ActualWidth>200,"Package page layout unusable");Capture(size.Width==800?"portable-ui-packages-compact.png":"portable-ui-packages.png");}
                var directory=Path.Combine(Program.Data,"package-history");File.WriteAllText(Path.Combine(directory,Guid.NewGuid().ToString("N")+".json"),"{broken");ReadHistory();Assert(!Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.PackageChange),"Corrupt package history was accepted");
                packageInventory=()=>{throw new IOException("Тест ошибки winget");};await RefreshPackages();Assert(packageStates["7zip.7zip"].Text=="Не проверено"&&packageStatus.Text.Contains("Тест ошибки"),"Inventory failure kept stale states");
            }finally{wingetLocate=locate;packageInventory=inventory;packageRun=run;foreach(var check in packageChecks.Values)check.IsChecked=false;var directory=Path.Combine(Program.Data,"package-history");if(Directory.Exists(directory))Directory.Delete(directory,true);ReadHistory();}
            ShowPage(0);
        }
    }
}
