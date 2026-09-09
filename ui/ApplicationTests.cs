using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Wintools {
    internal static class ApplicationRegistryTests {
        internal static void Run(){
            if(!Program.Hosted)throw new InvalidOperationException("Hosted runner required.");
            const string root=@"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            var name="WintoolsFixture_"+Guid.NewGuid().ToString("N");
            using(var user=Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser,Microsoft.Win32.RegistryView.Registry64)){
                try{
                    using(var key=user.CreateSubKey(root+"\\"+name)){key.SetValue("DisplayName","Wintools inventory fixture");key.SetValue("UninstallString","\""+Program.Exe+"\"");key.SetValue("EstimatedSize",4096);}
                    int unavailable;var row=ApplicationInventory.Read(out unavailable).Single(r=>r.Key==name);if(row.SizeKb!=4096||!row.CanRemove)throw new Exception("Registry inventory metadata incorrect");
                    using(var key=user.OpenSubKey(root+"\\"+name,true))key.SetValue("NoRemove",1);
                    if(ApplicationInventory.Refresh(row).CanRemove)throw new Exception("Registry removal policy ignored");
                    using(var key=user.OpenSubKey(root+"\\"+name,true))key.SetValue("SystemComponent",1);
                    if(ApplicationInventory.Refresh(row)!=null)throw new Exception("Hidden component offered for removal");
                }finally{user.DeleteSubKeyTree(root+"\\"+name,false);}
            }
        }
    }
    internal sealed partial class MainWindow {
        private async Task ApplicationSmoke(){
            var command=ApplicationInventory.ParseCommand("\""+Program.Exe+"\" /remove \"literal & value\"");Assert(command.FileName==Program.Exe&&command.Arguments=="/remove \"literal & value\"","Uninstaller command parsing changed arguments");
            foreach(var input in new[]{"missing.exe",@"\\server\share\remove.exe","https://example.com/remove.exe","\"unterminated"}){bool rejected=false;try{ApplicationInventory.ParseCommand(input);}catch(IOException){rejected=true;}Assert(rejected,"Invalid uninstaller accepted: "+input);}
            var product=new InstalledApplication{Msi=true,Key="{11111111-1111-1111-1111-111111111111}",CanRemove=true};Assert(ApplicationInventory.Removal(product).Arguments=="/x {11111111-1111-1111-1111-111111111111} /norestart","MSI removal arguments incorrect");product.CanRemove=false;bool protectedEntry=false;try{ApplicationInventory.Removal(product);}catch(IOException){protectedEntry=true;}Assert(protectedEntry,"NoRemove ignored");
            installedApplications=new[]{new InstalledApplication{Name="Редактор",Publisher="Example",SizeKb=2048,Key="one",Command="\""+Program.Exe+"\"",CanRemove=true},new InstalledApplication{Name="Плеер",Publisher="Example",SizeKb=8192,Key="two"},new InstalledApplication{Name="Утилита",Publisher="Other",Key="three"}};FilterApplications();ShowPage(9);
            applicationSearch.Text="example";Assert(applicationList.Items.Count==2,"Application publisher search failed");applicationSort.SelectedIndex=1;Assert(((InstalledApplication)applicationList.Items[0]).Key=="two","Application size sort failed");applicationSearch.Text="__missing__";Assert(applicationList.Items.Count==0&&!applicationRemove.IsEnabled,"Empty application results enabled removal");applicationSearch.Clear();applicationList.SelectedItem=installedApplications[0];SetBusy(true);Assert(!applicationRemove.IsEnabled,"Busy application removal enabled");SetBusy(false);
            var reload=applicationReload;var run=applicationRun;int calls=0;applicationRun=info=>{calls++;return Task.FromResult((int?)0);};
            try{
                var fixture=installedApplications[0];int reads=0;applicationReload=row=>++reads==1?fixture:new InstalledApplication{Command="changed"};var pending=RemoveApplication();Assert(confirmation!=null,"Removal had no confirmation");FinishConfirmation(true);await pending;Assert(calls==0&&applicationStatus.Text.Contains("изменилась"),"Changed uninstall registration executed");
                applicationReload=row=>fixture;pending=RemoveApplication();FinishConfirmation(false);await pending;Assert(calls==0,"Cancelled removal executed");
                pending=RemoveApplication();FinishConfirmation(true);await pending;Assert(calls==1&&!busy&&applicationStatus.Text.Contains("Обновите список"),"Uninstaller completion incorrectly reported");
            }finally{applicationReload=reload;applicationRun=run;}
            Text("Status","Готово к работе");await ReadApplications();Assert(applicationsLoaded,"Application inventory failed");Window.Width=1280;Window.Height=800;Window.UpdateLayout();await Task.Delay(100);Capture("portable-ui-applications.png");Window.Width=800;Window.Height=600;Window.UpdateLayout();await Task.Delay(100);Capture("portable-ui-applications-compact.png");ShowPage(0);
        }
    }
}
