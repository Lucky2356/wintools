using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Wintools {
    internal sealed partial class MainWindow {
        private DesktopLaunchInventory desktopLaunches=new DesktopLaunchInventory();
        private Func<DesktopShortcut,DesktopShortcut> desktopReload=e=>DesktopLaunch.ReadShortcut(e.Path);
        private Action<ProcessStartInfo> desktopStart=info=>{using(var process=Process.Start(info)){};};
        private void DesktopSelection(InstalledApplication row){
            applicationLaunchEntry.DisplayMemberPath="Name";var previous=applicationLaunchEntry.SelectedItem as DesktopShortcut;var entries=DesktopLaunch.Match(row,desktopLaunches);applicationLaunchEntry.ItemsSource=entries;
            applicationLaunchEntry.SelectedItem=previous==null?null:entries.FirstOrDefault(e=>e.Path==previous.Path);if(applicationLaunchEntry.SelectedIndex<0&&entries.Length>0)applicationLaunchEntry.SelectedIndex=0;
            applicationLaunch.Visibility=applicationLaunchEntry.Visibility=row==null?Visibility.Collapsed:Visibility.Visible;
            applicationLaunchEntry.IsEnabled=!busy&&!readingApplications&&entries.Length>0;applicationLaunch.IsEnabled=applicationLaunchEntry.IsEnabled;
            applicationLaunch.ToolTip=entries.Length==0?"Подходящий ярлык меню «Пуск» не найден. Программу можно открыть обычным способом.":"Открыть выбранный ярлык меню «Пуск»";
            DesktopLaunchTooltip();
            if(row!=null)applicationDetail.Text+="\n"+(entries.Length==0?"Подходящий ярлык «Пуска» не найден. Откройте программу обычным способом.":"Запуск через ярлык «Пуска». Если их несколько, выберите нужный.")+(desktopLaunches.Errors>0?" Часть ярлыков не удалось прочитать.":"");
        }
        private void DesktopLaunchTooltip(){var entry=applicationLaunchEntry.SelectedItem as DesktopShortcut;applicationLaunchEntry.ToolTip=entry==null?"Выберите ярлык программы":entry.Target+"\n"+entry.Arguments+"\nЯрлык: "+entry.Path;}
        private async Task LaunchDesktopApplication(){
            var row=applicationList.SelectedItem as InstalledApplication;var entry=applicationLaunchEntry.SelectedItem as DesktopShortcut;if(busy||readingApplications||row==null||row.Package!=null||entry==null)return;
            SetBusy(true);try{var current=await Task.Run(()=>desktopReload(entry));var refreshed=applicationReload(row);if(refreshed==null||!DesktopLaunch.Match(refreshed,new DesktopLaunchInventory{Entries=new[]{current}}).Any())throw new IOException("Запись программы или ярлык изменились. Обновите список.");var start=DesktopLaunch.StartInfo(entry,current);desktopStart(start);applicationStatus.Text="Запрос запуска «"+entry.Name+"» передан Windows.";}
            catch(Exception ex){applicationStatus.Text="Не удалось запустить программу: "+ex.Message;}finally{SetBusy(false);Text("Status",applicationStatus.Text);}
        }
    }
}
