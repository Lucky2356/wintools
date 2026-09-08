using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Wintools {
    internal sealed class ActionRow {
        internal Tweak Item;
        public string DisplayTitle {get;set;}
        public string Summary {get;set;}
    }
    internal sealed class HistoryRow {
        public string Run {get;set;}
        public string Title {get;set;}
        public string Status {get;set;}
        public bool CanRevert {get;set;}
    }
    internal sealed class MainWindow {
        internal readonly Window Window;
        private readonly Preferences preferences=Preferences.Load();
        private readonly List<Tweak> catalogue=Catalogue.Load();
        private readonly bool smoke;
        private bool ready,busy,checking,closed;
        private Update available;
        private TaskCompletionSource<bool> confirmation;
        private int page;
        private readonly string[] pages={"CataloguePage","HistoryPage","UpdatesPage","SettingsPage"};
        private readonly string[] nav={"NavCatalogue","NavHistory","NavUpdates","NavSettings"};
        private readonly string[] operations={"Apply","Preview","Revert","Star","HistoryRevert","Diagnose","Verify","RefreshHistory"};
        private T Get<T>(string name) where T:class{return (T)Window.FindName(name);}
        private void Text(string name,string value){Get<TextBlock>(name).Text=value;}
        private void Click(string name,Action action){Get<Button>(name).Click+=(s,e)=>action();}
        private void ClickAsync(string name,Func<Task> action){Get<Button>(name).Click+=async(s,e)=>await action();}
        private bool Checked(string name){return Get<CheckBox>(name).IsChecked==true;}
        private void Enabled(string name,bool value){Get<Control>(name).IsEnabled=value;}
        private void Visible(string name,bool value){Get<UIElement>(name).Visibility=value?Visibility.Visible:Visibility.Collapsed;}

        internal MainWindow(bool smokeMode) {
            smoke=smokeMode;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Wintools.Shell.xaml"))Window=(Window)XamlReader.Load(stream);
            Text("VersionLabel","Версия "+Program.Version+" · x64");
            Text("UpdateStatus","Установлена версия "+Program.Version+". Проверьте наличие обновления.");
            Get<ComboBox>("Category").ItemsSource=Catalogue.Categories;Get<ComboBox>("Category").SelectedIndex=0;
            Get<ComboBox>("Theme").SelectedIndex=preferences.Theme=="dark"?2:preferences.Theme=="light"?1:0;
            Get<CheckBox>("AutoCheck").IsChecked=preferences.AutoCheck;Get<CheckBox>("RestorePoint").IsChecked=preferences.RestorePoint;Get<CheckBox>("PreviewChannel").IsChecked=preferences.IncludePreview;
            Get<ComboBox>("Theme").SelectionChanged+=(s,e)=>{if(!ready)return;preferences.Theme=new[]{"system","light","dark"}[Get<ComboBox>("Theme").SelectedIndex];ApplyTheme();SavePreferences();};
            Get<TextBox>("Search").TextChanged+=(s,e)=>Filter();Get<ComboBox>("Category").SelectionChanged+=(s,e)=>Filter();
            foreach(var name in new[]{"Favorites","Risky"})Get<CheckBox>(name).Click+=(s,e)=>Filter();
            Get<ListBox>("Items").SelectionChanged+=(s,e)=>SelectItem();Get<ListBox>("History").SelectionChanged+=(s,e)=>RefreshEnabled();
            Get<CheckBox>("AutoCheck").Click+=(s,e)=>{preferences.AutoCheck=Checked("AutoCheck");SavePreferences();};
            Get<CheckBox>("RestorePoint").Click+=(s,e)=>{preferences.RestorePoint=Checked("RestorePoint");SavePreferences();};
            Get<CheckBox>("PreviewChannel").Click+=(s,e)=>{preferences.IncludePreview=Checked("PreviewChannel");available=null;RefreshEnabled();Text("UpdateStatus","Канал изменён. Проверьте обновления.");SavePreferences();};
            for(int i=0;i<nav.Length;i++){int index=i;Click(nav[i],()=>ShowPage(index));}
            ClickAsync("Diagnose",()=>Run("diagnose",null,null,false));ClickAsync("Verify",()=>Run("verify",null,null,false));
            ClickAsync("Preview",async()=>{var item=Selected();if(item!=null)await Run(item.Verb,item.Id,null,true);});
            ClickAsync("Apply",async()=>{var item=Selected();if(item!=null&&await Confirm(item.Title+"\n\n"+item.Description+"\n\nОткат: "+item.Rollback))await Run(item.Verb,item.Id,null,false);});
            ClickAsync("Revert",async()=>{var item=Selected();if(item!=null&&await Confirm("Восстановить сохранённое состояние для «"+item.Title+"»?"))await Run("revert",item.Id,null,false);});
            Click("Star",()=>{var item=Selected();if(item==null)return;if(preferences.Favorites.Contains(item.Id))preferences.Favorites.Remove(item.Id);else preferences.Favorites.Add(item.Id);SavePreferences();Filter();});
            Click("RefreshHistory",ReadHistory);
            ClickAsync("HistoryRevert",async()=>{var row=Get<ListBox>("History").SelectedItem as HistoryRow;if(row!=null&&row.CanRevert&&await Confirm("Откатить запуск "+row.Run+"?\n\nСначала откатывайте более новые изменения."))await Run("revert",null,row.Run,false);});
            Click("OpenLogs",()=>OpenFolder("logs"));Click("OpenReports",()=>OpenFolder("reports"));Click("OpenData",()=>OpenFolder(""));
            Click("ReleaseLink",()=>OpenUrl("https://github.com/Lucky2356/wintools/releases"));Click("TokenHelp",()=>OpenUrl("https://github.com/settings/personal-access-tokens/new"));
            ClickAsync("CheckUpdates",()=>CheckUpdates(true));ClickAsync("InstallUpdate",InstallUpdate);
            ClickAsync("SaveAccess",async()=>{try{if(string.IsNullOrWhiteSpace(Get<PasswordBox>("AccessToken").Password))throw new ArgumentException("Введите токен. Для удаления сохранённого доступа используйте кнопку «Удалить».");Access.Save(Get<PasswordBox>("AccessToken").Password);Get<PasswordBox>("AccessToken").Clear();ReadAccess();await CheckUpdates(true);}catch(Exception ex){Text("AccessStatus",ex.Message);}});
            Click("ClearAccess",()=>{try{Access.Save(null);Get<PasswordBox>("AccessToken").Clear();available=null;ReadAccess();RefreshEnabled();Text("UpdateStatus","Доступ удалён. Для закрытого репозитория потребуется новый токен.");}catch(Exception ex){Text("AccessStatus",ex.Message);}});
            Click("LogToggle",()=>ExpandOutput(Get<TextBox>("Output").Visibility!=Visibility.Visible));
            Click("ConfirmYes",()=>FinishConfirmation(true));Click("ConfirmNo",()=>FinishConfirmation(false));
            Window.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&confirmation!=null){FinishConfirmation(false);e.Handled=true;}};
            Window.Closing+=(s,e)=>{if(busy){e.Cancel=true;Text("Status","Дождитесь завершения операции — приложение ещё работает.");}else FinishConfirmation(false);};
            Window.Closed+=(s,e)=>{closed=true;SystemEvents.UserPreferenceChanged-=SystemPreferenceChanged;};
            Window.SourceInitialized+=(s,e)=>NativeTheme.TitleBar(new WindowInteropHelper(Window).Handle,PaletteDark());
            Window.SizeChanged+=(s,e)=>Get<TextBox>("Output").Height=Window.ActualHeight<790?48:100;
            Window.Loaded+=async(s,e)=>{if(smoke){try{await Smoke();}catch(Exception ex){File.WriteAllText(Path.Combine(Program.Home,"portable-error.txt"),ex.ToString());Environment.ExitCode=4;}Window.Close();return;}if(preferences.AutoCheck)await CheckUpdates(false);};
            ready=true;ApplyTheme();Filter();ReadHistory();ReadAccess();ShowPage(0);SystemEvents.UserPreferenceChanged+=SystemPreferenceChanged;
        }
        private bool PaletteDark(){return NativeTheme.IsDark(preferences.Theme);}
        private void ApplyTheme() {
            foreach(var pair in NativeTheme.Colors(PaletteDark()))Window.Resources[pair.Key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
            if(new WindowInteropHelper(Window).Handle!=IntPtr.Zero)NativeTheme.TitleBar(new WindowInteropHelper(Window).Handle,PaletteDark());
            ShowPage(page);
        }
        private void SystemPreferenceChanged(object sender,UserPreferenceChangedEventArgs e){if(!closed)Window.Dispatcher.BeginInvoke(new Action(()=>{if(!closed)ApplyTheme();}));}
        private void ShowPage(int index) {
            page=index;for(int i=0;i<pages.Length;i++){Visible(pages[i],i==index);Get<Button>(nav[i]).SetResourceReference(Control.BackgroundProperty,i==index?"Selection":"Sidebar");Get<Button>(nav[i]).SetResourceReference(Control.ForegroundProperty,i==index?"Text":"Muted");}
            Text("PageTitle",new[]{"Настройте Windows под себя","История изменений","Обновления приложения","Настройки приложения"}[index]);
            Text("PageEyebrow",new[]{"КАТАЛОГ ДЕЙСТВИЙ","ЖУРНАЛ ЭТОГО КОМПЬЮТЕРА","ОБНОВЛЕНИЯ WINTOOLS","ВАШИ ПРЕДПОЧТЕНИЯ"}[index]);
            Text("PageHint",new[]{"Выберите действие и посмотрите, что изменится.","Исходные состояния и откат сохранённых запусков.","Проверка, безопасная загрузка и сохранение ваших данных.","Внешний вид, защита перед изменениями и доступ к GitHub."}[index]);
        }
        private Tweak Selected(){var row=Get<ListBox>("Items").SelectedItem as ActionRow;return row==null?null:row.Item;}
        private static string Risk(Tweak item){return item.Risk=="high"?"Высокий риск":item.Risk=="med"?"Средний риск":"Низкий риск";}
        private void Filter() {
            if(!ready)return;var prior=Selected();string id=prior==null?null:prior.Id;string group=(string)Get<ComboBox>("Category").SelectedValue??"ALL",query=Get<TextBox>("Search").Text.Trim();
            var rows=catalogue.Where(t=>(group=="ALL"||t.Category==group)&&(!Checked("Favorites")||preferences.Favorites.Contains(t.Id))&&(Checked("Risky")||t.Risk!="high")&&(t.Title+" "+t.Description+" "+t.Id).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).Select(t=>new ActionRow{Item=t,DisplayTitle=(preferences.Favorites.Contains(t.Id)?"★  ":"")+t.Title,Summary=Risk(t)+"  ·  "+t.Id}).ToArray();
            var list=Get<ListBox>("Items");list.ItemsSource=rows;list.SelectedItem=rows.FirstOrDefault(t=>t.Item.Id==id)??rows.FirstOrDefault();Visible("EmptyCatalogue",rows.Length==0);Visible("SearchHint",Get<TextBox>("Search").Text.Length==0);Text("Count",rows.Length+" действий");SelectItem();
        }
        private void SelectItem() {
            if(!ready)return;var item=Selected();Text("ActionTitle",item==null?"Выберите действие":item.Title);Text("Metadata",item==null?"":Risk(item)+"  ·  Windows "+(item.Os=="any"?"10 / 11":item.Os=="win11"?"11":"10"));Text("Description",item==null?"Результаты поиска появятся слева. Выберите действие, чтобы прочитать его описание.":item.Description);Text("Rollback",item==null?"—":item.Rollback);Get<Button>("Star").Content=item!=null&&preferences.Favorites.Contains(item.Id)?"★ Убрать":"☆ Избранное";RefreshEnabled();
        }
        private void RefreshEnabled() {
            if(!ready)return;foreach(var name in operations)Enabled(name,!busy);var item=Selected();Enabled("Apply",!busy&&item!=null);Enabled("Preview",!busy&&item!=null);Enabled("Star",!busy&&item!=null);Enabled("Revert",!busy&&item!=null&&item.Category!="CLEAN"&&item.Kind!="EDGE");var row=Get<ListBox>("History").SelectedItem as HistoryRow;Enabled("HistoryRevert",!busy&&row!=null&&row.CanRevert);
            foreach(var name in new[]{"CheckUpdates","SaveAccess","ClearAccess","PreviewChannel"})Enabled(name,!busy&&!checking);Enabled("RestorePoint",!busy);Enabled("InstallUpdate",!busy&&!checking&&available!=null);Visible("InstallUpdate",available!=null);
        }
        private void SetBusy(bool value){busy=value;RefreshEnabled();if(value)Text("Status","Выполняется операция…");}
        private void ReadHistory() {
            try{var rows=HistoryRows(Path.Combine(Program.Data,"state","applied.dat"),catalogue);Get<ListBox>("History").ItemsSource=rows;Text("HistoryStatus",rows.Length==0?"Изменений пока нет. После выполнения действия здесь появится запись.":"Запусков: "+rows.Length+". Сначала откатывайте самые новые изменения.");}
            catch(IOException ex){Get<ListBox>("History").ItemsSource=null;Text("HistoryStatus","Журнал недоступен. Повторите чтение позже. "+ex.Message);}
            catch(UnauthorizedAccessException ex){Get<ListBox>("History").ItemsSource=null;Text("HistoryStatus","Журнал недоступен. Нет доступа к файлу. "+ex.Message);}
            RefreshEnabled();
        }
        internal static HistoryRow[] HistoryRows(string path,List<Tweak> catalogue) {
            if(!File.Exists(path))return new HistoryRow[0];var lines=File.ReadAllLines(path,Encoding.GetEncoding(28591)).Where(l=>!string.IsNullOrWhiteSpace(l)).Select(l=>l.Split('|')).ToArray();
            if(lines.Any(p=>p.Length!=11||p.Any(string.IsNullOrWhiteSpace)||!new[]{"OK","PENDING","FAILED","REVERTED","MANUAL"}.Contains(p[9])||!Regex.IsMatch(p[0],"^[A-Za-z0-9_.-]{1,100}$")||!Regex.IsMatch(p[1],"^[A-Z][A-Z0-9-]{1,63}$")))throw new IOException("Журнал содержит повреждённые записи. Откат из интерфейса отключён до проверки файла.");
            return lines.Reverse().GroupBy(p=>p[0]).Select(g=>{var entries=g.ToArray();bool pending=entries.Any(p=>p[9]=="PENDING"||p[9]=="FAILED");bool active=entries.Any(p=>p[9]=="OK");var item=catalogue.FirstOrDefault(t=>t.Id==entries[0][1]);return new HistoryRow{Run=g.Key,Title=entries.Length>1?entries.Length+" действий":item==null?entries[0][1]:item.Title,Status=pending?"Требует внимания":active?"Применено":entries.All(p=>p[9]=="REVERTED")?"Откат выполнен":"Ручной откат",CanRevert=pending||active};}).ToArray();
        }
        private Task<bool> Confirm(string message){if(busy||confirmation!=null)return Task.FromResult(false);confirmation=new TaskCompletionSource<bool>();Text("ConfirmText",message);Get<Grid>("Body").IsEnabled=false;Visible("ConfirmOverlay",true);Get<Button>("ConfirmNo").Focus();return confirmation.Task;}
        private void FinishConfirmation(bool accepted){if(confirmation==null)return;var pending=confirmation;confirmation=null;Visible("ConfirmOverlay",false);Get<Grid>("Body").IsEnabled=true;pending.SetResult(accepted);}
        private void ExpandOutput(bool value){Visible("Output",value);Get<Button>("LogToggle").Content=value?"Вывод операции  ▴":"Вывод операции  ▾";}
        private async Task Run(string verb,string id,string run,bool dry) {
            if(busy)return;SetBusy(true);ExpandOutput(true);Get<TextBox>("Output").Text="Запуск "+verb+"…";
            try{var result=await Engine.Run(verb,id??"-",run??"-",dry,preferences.RestorePoint,value=>{Get<TextBox>("Output").Text=value;Get<TextBox>("Output").ScrollToEnd();});Get<TextBox>("Output").Text=result.Output;Text("Status",result.Code==0?(dry?"Предпросмотр завершён":"Операция завершена. Результат — в выводе."):"Операция требует внимания: код "+result.Code+". Подробности — в выводе.");ReadHistory();}
            catch(Win32Exception ex){Text("Status",ex.NativeErrorCode==1223?"Запрос администратора отменён. Действие не запускалось.":ex.Message);Get<TextBox>("Output").Text=ex.Message;}
            catch(Exception ex){Text("Status","Не удалось выполнить операцию");Get<TextBox>("Output").Text=ex.Message;}finally{SetBusy(false);}
        }
        private async Task CheckUpdates(bool manual) {
            if(busy||checking)return;checking=true;available=null;RefreshEnabled();Text("UpdateStatus","Проверяем доступ к GitHub и новые версии…");Text("Status","Проверка обновлений…");
            try{available=await Updates.Check(preferences.IncludePreview);if(closed)return;string message=available==null?"Новых совместимых версий в выбранном канале нет. Установлена "+Program.Version+".":"Доступна "+available.Release.tag_name+". Установлена "+Program.Version+". Ваши данные сохранятся.";Text("UpdateStatus",message);Text("Status",available==null?"Проверка завершена: обновлений нет":"Доступно обновление "+available.Release.tag_name);}
            catch(Exception ex){if(!closed){Text("UpdateStatus",UpdateError(ex));Text("Status","Обновление недоступно — подробности в разделе «Обновления».");if(manual)ShowPage(2);}}
            finally{checking=false;if(!closed)RefreshEnabled();}
        }
        private static string UpdateError(Exception ex){if(ex is TaskCanceledException)return "Истекло время ожидания GitHub. Проверьте соединение и повторите запрос.";if(ex is System.Net.Http.HttpRequestException)return "Не удалось соединиться с GitHub. Проверьте сеть, прокси и доступ к api.github.com. "+ex.GetBaseException().Message;return ex.Message;}
        private async Task InstallUpdate() {
            if(busy||checking||available==null)return;SetBusy(true);Visible("DownloadProgress",true);Text("UpdateStatus","Загружаем обновление и проверяем SHA-256…");
            try{var directory=await Updates.Download(available,value=>{Get<ProgressBar>("DownloadProgress").Value=value;Text("UpdateStatus","Загрузка обновления: "+value+"%");});Updates.LaunchReplacement(directory,available.Asset.digest.Substring(7));busy=false;Window.Close();}
            catch(Exception ex){Text("UpdateStatus",UpdateError(ex));Text("Status","Обновление не установлено. Текущая версия сохранена.");SetBusy(false);}finally{if(!closed)Visible("DownloadProgress",false);}
        }
        private void ReadAccess(){try{Text("AccessStatus",string.IsNullOrEmpty(Access.Load())?"Токен не сохранён.":"Токен сохранён для текущего пользователя Windows.");}catch(Exception ex){Text("AccessStatus",ex.Message);}}
        private void SavePreferences(){try{preferences.Save();}catch(Exception ex){Text("Status","Настройки не сохранены: "+ex.Message);}}
        private void OpenFolder(string relative){try{var path=relative.Length==0?Program.Data:Program.Under(Program.Data,relative);Program.SafeDirectory(path);Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe",Program.Quote(path)){UseShellExecute=true});}catch(Exception ex){Text("Status",ex.Message);}}
        private void OpenUrl(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){Text("Status",ex.Message);}}
        private async Task Smoke() {
            await Task.Delay(250);Get<ComboBox>("Theme").SelectedIndex=2;Assert(Preferences.Load().Theme=="dark","Theme persistence");
            Get<ListBox>("Items").SelectedIndex=3;var id=Selected().Id;Get<Button>("Star").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(Selected().Id==id,"Favorite selection lost");Get<Button>("Star").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SetBusy(true);Get<ComboBox>("Theme").SelectedIndex=1;Assert(!Get<Button>("Apply").IsEnabled,"Theme lost operation lock");SetBusy(false);Text("Status","Готово к работе");
            Get<TextBox>("Search").Text="__not_found__";Assert(Selected()==null&&!Get<Button>("Apply").IsEnabled,"Empty search actions");Get<TextBox>("Search").Clear();
            var path=Path.Combine(Program.Data,"state","applied.dat");if(File.Exists(path))throw new IOException("UI smoke requires isolated journal.");using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){ReadHistory();Assert(Get<TextBlock>("HistoryStatus").Text.Contains("недоступен"),"Locked history");}File.Delete(path);ReadHistory();
            foreach(int mode in new[]{1,2}){Get<ComboBox>("Theme").SelectedIndex=mode;ShowPage(0);await Task.Delay(100);Capture(mode==2?"portable-ui.png":"portable-ui-light.png");ShowPage(3);await Task.Delay(100);Capture(mode==2?"portable-ui-settings-dark.png":"portable-ui-settings-light.png");}
            ShowPage(2);Text("UpdateStatus","GitHub не открыл закрытый репозиторий (404). Сохраните токен с доступом Contents: Read в настройках.");await Task.Delay(100);Capture("portable-ui-updates.png");
            ShowPage(0);Window.Width=Window.MinWidth;Window.Height=Window.MinHeight;ExpandOutput(true);Window.UpdateLayout();await Task.Delay(100);Assert(IsVisibleInWindow("Apply")&&IsVisibleInWindow("Star")&&IsVisibleInWindow("ActionTitle")&&IsVisibleInWindow("Metadata"),"Compact action details clipped");Capture("portable-ui-compact.png");
            ExpandOutput(false);Get<ComboBox>("Theme").SelectedIndex=0;
        }
        private bool IsVisibleInWindow(string name){var control=Get<FrameworkElement>(name);for(var parent=VisualTreeHelper.GetParent(control);parent!=null;parent=VisualTreeHelper.GetParent(parent)){var element=parent as FrameworkElement;if(element==null)continue;var bounds=control.TransformToAncestor(element).TransformBounds(new Rect(control.RenderSize));if(bounds.Top< -1||bounds.Left< -1||bounds.Bottom>element.ActualHeight+1||bounds.Right>element.ActualWidth+1)return false;}return true;}
        private void Capture(string name){Window.UpdateLayout();var root=(FrameworkElement)Window.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(Program.Home,name)))encoder.Save(file);}
        private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
