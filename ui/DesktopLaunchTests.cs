using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task DesktopLaunchSmoke(){
            var directory=Path.Combine(Program.Data,"launch-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var path=Path.Combine(directory,"Редактор.lnk");var other=Path.Combine(directory,"Редактор — профиль.lnk");
            var saved=desktopLaunches;var reload=desktopReload;var start=desktopStart;var refresh=applicationReload;var apps=installedApplications;
            try{
                DesktopLaunch.Fixture(path,Program.Exe,"--fixture \"literal & value\"");var first=DesktopLaunch.ReadShortcut(path);Assert(first.Target==Program.Exe&&first.Arguments=="--fixture \"literal & value\"","Native shortcut changed path or arguments");
                DesktopLaunch.Fixture(other,Program.Exe,"--other");var second=DesktopLaunch.ReadShortcut(other);var app=new InstalledApplication{Name="Редактор",Key="launch-fixture",Location=Path.GetDirectoryName(Program.Exe)};desktopLaunches=new DesktopLaunchInventory{Entries=new[]{first,second}};
                Assert(DesktopLaunch.Match(app,desktopLaunches).Length==2,"Installed-folder shortcut matching failed");Assert(DesktopLaunch.Match(new InstalledApplication{Name="Unknown",Location=Path.GetPathRoot(Program.Exe)},desktopLaunches).Length==0,"Drive root matched unrelated apps");
                Assert(!DesktopLaunch.Launchable(new DesktopShortcut{Name="Uninstall",Target=Program.Exe})&&!DesktopLaunch.Local(@"\\server\app.exe"),"Unsupported launch target accepted");
                installedApplications=new[]{app,new InstalledApplication{Name="Без ярлыка",Key="none"}};applicationKind.SelectedIndex=0;applicationSearch.Clear();FilterApplications();ShowPage(9);applicationList.SelectedItem=app;applicationLaunchEntry.SelectedIndex=1;
                SetBusy(true);Assert(!applicationLaunch.IsEnabled&&applicationLaunchEntry.SelectedIndex==1,"Operation lock lost desktop shortcut choice");SetBusy(false);Assert(applicationLaunch.IsEnabled&&applicationLaunchEntry.SelectedIndex==1,"Shortcut selection reset");
                int calls=0;desktopStart=info=>{calls++;Assert(info.FileName==Program.Exe&&info.Arguments=="--other","Wrong desktop shortcut launched");};applicationReload=row=>app;
                await LaunchDesktopApplication();Assert(calls==1&&!busy&&applicationStatus.Text.Contains("передан"),"Desktop launch not reported");
                DesktopLaunch.Fixture(other,Program.Exe,"--changed");await LaunchDesktopApplication();Assert(calls==1&&applicationStatus.Text.Contains("изменился"),"Changed shortcut executed");
                applicationList.SelectedItem=installedApplications[1];Assert(!applicationLaunch.IsEnabled&&applicationDetail.Text.Contains("не найден"),"Missing shortcut not explained");applicationList.SelectedItem=app;
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();Assert(applicationList.ActualHeight>100&&applicationLaunch.ActualWidth>50,"Desktop launch clipped application list");Capture(size.Width==800?"portable-ui-desktop-launch-compact.png":"portable-ui-desktop-launch.png");}
                File.Delete(other);applicationLaunchEntry.SelectedIndex=1;await LaunchDesktopApplication();Assert(calls==1,"Missing shortcut executed");
            }finally{desktopLaunches=saved;desktopReload=reload;desktopStart=start;applicationReload=refresh;installedApplications=apps;File.Delete(path);File.Delete(other);Directory.Delete(directory);FilterApplications();ShowPage(0);}
        }
    }
}
