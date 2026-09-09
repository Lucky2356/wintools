using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace Wintools {
    internal sealed class InstalledApplication {
        public string Name {get;set;}
        public string Version {get;set;}
        public string Publisher {get;set;}
        public string Location {get;set;}
        public long? SizeKb {get;set;}
        public string Size {get{return SizeKb.HasValue?(SizeKb.Value/1024.0).ToString("N0")+" МБ":"Не указан";}}
        internal string Key, Command;
        internal RegistryHive Hive;
        internal RegistryView View;
        internal bool Msi, CanRemove;
    }
    internal static class ApplicationInventory {
        private const string Root=@"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        internal static InstalledApplication[] Read(out int unavailable) {
            var rows=new List<InstalledApplication>();unavailable=0;
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
                try{using(var baseKey=RegistryKey.OpenBaseKey(hive,view))using(var root=baseKey.OpenSubKey(Root)){
                    if(root==null)continue;foreach(var name in root.GetSubKeyNames())try{using(var key=root.OpenSubKey(name)){var row=ReadRow(key,name,hive,view);if(row!=null)rows.Add(row);}}catch(System.Security.SecurityException){unavailable++;}catch(UnauthorizedAccessException){unavailable++;}catch(IOException){unavailable++;}
                }}catch(System.Security.SecurityException){unavailable++;}catch(UnauthorizedAccessException){unavailable++;}catch(IOException){unavailable++;}
            }
            return rows.GroupBy(r=>r.Hive+"|"+r.Key+"|"+r.Name+"|"+r.Version+"|"+r.Command).Select(g=>g.First()).OrderBy(r=>r.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        private static string Text(RegistryKey key,string name){return key.GetValue(name) as string ?? "";}
        private static bool Flag(RegistryKey key,string name){return Convert.ToString(key.GetValue(name))=="1";}
        private static InstalledApplication ReadRow(RegistryKey key,string name,RegistryHive hive,RegistryView view){
            if(key==null||Flag(key,"SystemComponent")||string.IsNullOrWhiteSpace(Text(key,"DisplayName")))return null;
            long size;long? amount=long.TryParse(Convert.ToString(key.GetValue("EstimatedSize")),out size)&&size>=0?(long?)size:null;
            return new InstalledApplication{Name=Text(key,"DisplayName"),Version=Text(key,"DisplayVersion"),Publisher=Text(key,"Publisher"),Location=Text(key,"InstallLocation"),SizeKb=amount,Key=name,Hive=hive,View=view,Command=Text(key,"UninstallString"),Msi=Flag(key,"WindowsInstaller"),CanRemove=!Flag(key,"NoRemove")};
        }
        internal static InstalledApplication Refresh(InstalledApplication row){using(var baseKey=RegistryKey.OpenBaseKey(row.Hive,row.View))using(var key=baseKey.OpenSubKey(Root+"\\"+row.Key))return ReadRow(key,row.Key,row.Hive,row.View);}
        internal static ProcessStartInfo Removal(InstalledApplication row){
            if(!row.CanRemove)throw new IOException("Установщик не разрешает удаление этой записи.");Guid product;
            if(row.Msi&&Guid.TryParse(row.Key,out product))return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe"),"/x {"+product.ToString()+"} /norestart"){UseShellExecute=true};
            return ParseCommand(row.Command);
        }
        internal static ProcessStartInfo ParseCommand(string command){
            command=Environment.ExpandEnvironmentVariables(command??"").Trim();string executable,arguments;
            if(command.StartsWith("\"")){int end=command.IndexOf('"',1);if(end<0)throw new IOException("Установщик записал некорректную команду удаления.");executable=command.Substring(1,end-1);arguments=command.Substring(end+1).TrimStart();}
            else {var match=Regex.Match(command,@"^(?<exe>.*?\.exe)(?<args>\s.*)?$",RegexOptions.IgnoreCase);if(!match.Success)throw new IOException("Команда удаления не содержит поддерживаемый EXE-файл.");executable=match.Groups["exe"].Value;arguments=match.Groups["args"].Value.TrimStart();}
            if(string.Equals(executable,"msiexec.exe",StringComparison.OrdinalIgnoreCase))executable=Path.Combine(Environment.SystemDirectory,"msiexec.exe");
            if(!Path.IsPathRooted(executable)||executable.StartsWith(@"\\")||Path.GetExtension(executable).ToLowerInvariant()!=".exe"||!File.Exists(executable))throw new IOException("Файл программы удаления недоступен. Переустановите приложение или используйте его официальный установщик.");
            return new ProcessStartInfo(executable,arguments){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(executable)};
        }
    }
    internal sealed partial class MainWindow {
        private InstalledApplication[] installedApplications=new InstalledApplication[0];
        private ListBox applicationList;
        private TextBox applicationSearch;
        private ComboBox applicationSort;
        private TextBlock applicationStatus,applicationDetail;
        private Button applicationRemove,applicationFolder;
        private bool readingApplications,applicationsLoaded;
        private int inaccessibleApplications;
        private Func<InstalledApplication,InstalledApplication> applicationReload=ApplicationInventory.Refresh;
        private Func<ProcessStartInfo,Task<int?>> applicationRun=RunUninstaller;
        private static async Task<int?> RunUninstaller(ProcessStartInfo start){using(var process=Process.Start(start)){if(process==null)return null;while(!process.HasExited)await Task.Delay(400);return process.ExitCode;}}
        private void InitializeApplications(){
            var root=new Grid{Visibility=Visibility.Collapsed};Window.RegisterName("ApplicationsPage",root);((Grid)Get<FrameworkElement>("SettingsPage").Parent).Children.Add(root);
            foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new RowDefinition{Height=height});
            var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,8)};root.Children.Add(toolbar);applicationSearch=new TextBox{Width=260,Margin=new Thickness(0,0,10,8),ToolTip="Название или издатель"};System.Windows.Automation.AutomationProperties.SetName(applicationSearch,"Поиск установленных приложений");var searchFrame=new Grid();searchFrame.Children.Add(applicationSearch);var searchHint=new TextBlock{Text="Название или издатель",IsHitTestVisible=false,Margin=new Thickness(12,0,20,8),VerticalAlignment=VerticalAlignment.Center};searchHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");searchFrame.Children.Add(searchHint);toolbar.Children.Add(searchFrame);applicationSearch.TextChanged+=(s,e)=>{searchHint.Visibility=applicationSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;FilterApplications();};
            applicationSort=new ComboBox{Width=175,Margin=new Thickness(0,0,10,8),ItemsSource=new[]{"По названию","Сначала крупные","По издателю"},SelectedIndex=0};System.Windows.Automation.AutomationProperties.SetName(applicationSort,"Сортировка приложений");toolbar.Children.Add(applicationSort);applicationSort.SelectionChanged+=(s,e)=>FilterApplications();
            var refresh=new Button{Content="Обновить список",Margin=new Thickness(0,0,0,8)};toolbar.Children.Add(refresh);refresh.Click+=async(s,e)=>await ReadApplications();
            applicationStatus=Paragraph("Загрузите список установленных настольных программ. Приложения Store и portable-программы в этот список не входят.");Grid.SetRow(applicationStatus,1);root.Children.Add(applicationStatus);
            applicationList=new ListBox();VirtualizingPanel.SetIsVirtualizing(applicationList,true);VirtualizingPanel.SetVirtualizationMode(applicationList,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(applicationList,true);Grid.SetRow(applicationList,2);root.Children.Add(applicationList);
            applicationList.ItemTemplate=(DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='110'/><ColumnDefinition Width='95'/></Grid.ColumnDefinitions><StackPanel Margin='0,0,10,0'><TextBlock Text='{Binding Name}' FontWeight='SemiBold' TextWrapping='Wrap'/><TextBlock Text='{Binding Publisher}' Foreground='{DynamicResource Muted}' FontSize='12' Margin='0,4,0,0'/></StackPanel><TextBlock Grid.Column='1' Text='{Binding Version}' ToolTip='{Binding Version}' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis' Margin='0,0,10,0'/><TextBlock Grid.Column='2' Text='{Binding Size}' TextWrapping='Wrap'/></Grid></DataTemplate>");
            var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(footer,3);root.Children.Add(footer);applicationDetail=Paragraph("Выберите программу. Размер сообщён установщиком и может отличаться от занятого места.");footer.Children.Add(applicationDetail);var actions=new WrapPanel();footer.Children.Add(actions);applicationFolder=new Button{Content="Папка программы",IsEnabled=false,Margin=new Thickness(0,0,10,0)};actions.Children.Add(applicationFolder);applicationRemove=new Button{Content="Удалить программу…",IsEnabled=false};actions.Children.Add(applicationRemove);
            applicationList.SelectionChanged+=(s,e)=>ApplicationSelection();applicationFolder.Click+=(s,e)=>{var row=applicationList.SelectedItem as InstalledApplication;if(row==null||busy)return;try{var location=Environment.ExpandEnvironmentVariables(row.Location);if(!Path.IsPathRooted(location)||!Directory.Exists(location))throw new IOException("Папка не найдена.");Process.Start(new ProcessStartInfo(location){UseShellExecute=true});}catch(Exception ex){applicationStatus.Text="Не удалось открыть папку: "+ex.Message;}};applicationRemove.Click+=async(s,e)=>await RemoveApplication();
        }
        private async Task ReadApplications(){if(readingApplications||busy)return;readingApplications=true;applicationStatus.Text="Читаем список программ…";try{int unavailable=0;var rows=await Task.Run(()=>ApplicationInventory.Read(out unavailable));if(closed)return;installedApplications=rows;inaccessibleApplications=unavailable;applicationsLoaded=true;FilterApplications();}catch(Exception ex){if(!closed)applicationStatus.Text="Не удалось прочитать приложения: "+ex.Message;}finally{readingApplications=false;}}
        private void FilterApplications(){if(applicationList==null)return;var previous=applicationList.SelectedItem as InstalledApplication;var query=applicationSearch.Text.Trim();var rows=installedApplications.Where(r=>(r.Name+" "+r.Publisher).IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0);applicationList.ItemsSource=(applicationSort.SelectedIndex==1?rows.OrderByDescending(r=>r.SizeKb??-1).ThenBy(r=>r.Name):applicationSort.SelectedIndex==2?rows.OrderBy(r=>r.Publisher).ThenBy(r=>r.Name):rows.OrderBy(r=>r.Name)).ToArray();applicationStatus.Text="Показано: "+applicationList.Items.Count+" из "+installedApplications.Length+" настольных программ. Store и portable не включены. Недоступных записей: "+inaccessibleApplications+".";if(previous!=null)applicationList.SelectedItem=applicationList.Items.Cast<InstalledApplication>().FirstOrDefault(r=>r.Hive==previous.Hive&&r.View==previous.View&&r.Key==previous.Key);}
        private void ApplicationSelection(){var row=applicationList.SelectedItem as InstalledApplication;applicationRemove.IsEnabled=!busy&&row!=null&&row.CanRemove&&(row.Msi||!string.IsNullOrWhiteSpace(row.Command));applicationFolder.IsEnabled=!busy&&row!=null&&!string.IsNullOrWhiteSpace(row.Location);applicationDetail.Text=row==null?"Выберите программу. Размер сообщён установщиком и может отличаться от занятого места.":row.Name+" · "+(row.Hive==RegistryHive.CurrentUser?"Для текущего пользователя":"Для компьютера")+"\n"+(string.IsNullOrEmpty(row.Location)?"Папка установки не указана.":row.Location);}
        private async Task RemoveApplication(){
            var selected=applicationList.SelectedItem as InstalledApplication;if(selected==null||busy)return;
            try{var current=applicationReload(selected);if(current==null)throw new IOException("Запись уже удалена. Обновите список.");var start=ApplicationInventory.Removal(current);if(!await Confirm("Удалить «"+current.Name+"»?\n\nОткроется программа удаления издателя. Она может удалить настройки и данные приложения. Откат Wintools для удаления программ недоступен.\n\n"+start.FileName+"\n"+start.Arguments))return;
                var checkedRow=applicationReload(current);if(checkedRow==null||checkedRow.Command!=current.Command||checkedRow.Msi!=current.Msi||checkedRow.CanRemove!=current.CanRemove)throw new IOException("Запись изменилась после подтверждения. Обновите список и повторите.");
                SetBusy(true);ApplicationSelection();applicationStatus.Text="Открыта программа удаления. Завершите её шаги.";var code=await applicationRun(start);applicationStatus.Text=code.HasValue?"Программа удаления завершилась с кодом "+code.Value+". Обновите список: некоторые установщики продолжают работу в другом процессе.":"Команда передана установщику. Проверьте его окно и обновите список после завершения.";Text("Status","Программа удаления закрыта. Проверьте её результат и обновите список приложений.");
            }catch(Exception ex){applicationStatus.Text="Удаление не выполнено или прервано: "+ex.Message;}finally{SetBusy(false);ApplicationSelection();}
        }
    }
}
