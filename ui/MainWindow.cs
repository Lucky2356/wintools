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
    internal sealed partial class MainWindow {
        internal readonly Window Window;
        private readonly Preferences preferences=Preferences.Load();
        private readonly List<Tweak> catalogue=Catalogue.Load();
        private readonly bool smoke;
        private bool ready,busy,checking,closed,downloading,replacing;
        private string stagedDirectory;
        private Update stagedUpdate;
        private Func<Update,Action<int>,Task<string>> download=Updates.Download;
        private HashSet<string> collection;
        private string selectedGroup;
        private bool showAll;
        private readonly System.Windows.Threading.DispatcherTimer updateTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromHours(6)};
        private Update available;
        private TaskCompletionSource<bool> confirmation;
        private int page;
        private readonly string[] pages={"CataloguePage","HistoryPage","CollectionsPage","SettingsPage","PlanPage"};
        private readonly string[] nav={"NavCatalogue","NavHistory","NavCollections","NavSettings","NavPlan"};
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
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Wintools.Icon.ico")){Window.Icon=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);}
            Text("VersionLabel","Версия "+Program.Version+" · x64");
            Text("UpdateStatus","Установлена версия "+Program.Version+". Проверьте наличие обновления.");
            Get<ComboBox>("Category").ItemsSource=Catalogue.Categories;Get<ComboBox>("Category").SelectedIndex=0;
            Get<ComboBox>("Theme").SelectedIndex=preferences.Theme=="dark"?2:preferences.Theme=="light"?1:0;
            Get<CheckBox>("AutoCheck").IsChecked=preferences.AutoCheck;Get<CheckBox>("RestorePoint").IsChecked=preferences.RestorePoint;Get<CheckBox>("PreviewChannel").IsChecked=preferences.IncludePreview;
            Get<CheckBox>("AutoInstall").IsChecked=preferences.AutoInstall;
            Get<ComboBox>("Theme").SelectionChanged+=(s,e)=>{if(!ready)return;preferences.Theme=new[]{"system","light","dark"}[Get<ComboBox>("Theme").SelectedIndex];ApplyTheme();SavePreferences();};
            Get<TextBox>("Search").TextChanged+=(s,e)=>{selectedGroup=null;Filter();};Get<ComboBox>("Category").SelectionChanged+=(s,e)=>{selectedGroup=null;showAll=false;collection=null;Visible("ClearCollection",false);Filter();};
            Get<ItemsControl>("BrowseGroups").AddHandler(Button.ClickEvent,new RoutedEventHandler((s,e)=>{var button=e.OriginalSource as Button;if(button==null||button.Tag==null)return;var key=(string)button.Tag;if(key.StartsWith("category:"))Get<ComboBox>("Category").SelectedValue=key.Substring(9);else{selectedGroup=key;Filter();}}));
            Click("BackToGroups",()=>{collection=null;selectedGroup=null;showAll=false;Get<CheckBox>("Favorites").IsChecked=false;Get<TextBox>("Search").Clear();Get<ComboBox>("Category").SelectedIndex=0;Visible("ClearCollection",false);Filter();});
            Click("ShowAll",()=>{showAll=true;Filter();});
            foreach(var name in new[]{"Favorites","Risky"})Get<CheckBox>(name).Click+=(s,e)=>Filter();
            Get<ListBox>("Items").SelectionChanged+=(s,e)=>SelectItem();Get<ListBox>("History").SelectionChanged+=(s,e)=>RefreshEnabled();
            Get<CheckBox>("AutoCheck").Click+=async(s,e)=>{preferences.AutoCheck=Checked("AutoCheck");SavePreferences();if(preferences.AutoCheck)await CheckUpdates(false);};
            Get<CheckBox>("AutoInstall").Click+=async(s,e)=>{preferences.AutoInstall=Checked("AutoInstall");SavePreferences();if(preferences.AutoInstall){if(stagedDirectory!=null)Text("UpdateStatus","Обновление готово и установится при закрытии приложения.");else await PrepareAutomaticUpdate();}else Text("UpdateStatus","Автоматическая установка выключена. Можно обновить вручную.");};
            Get<CheckBox>("RestorePoint").Click+=(s,e)=>{preferences.RestorePoint=Checked("RestorePoint");SavePreferences();};
            Get<CheckBox>("PreviewChannel").Click+=async(s,e)=>{preferences.IncludePreview=Checked("PreviewChannel");available=null;DiscardStaged();RefreshEnabled();SavePreferences();await CheckUpdates(true);};
            for(int i=0;i<nav.Length;i++){int index=i;Click(nav[i],()=>ShowPage(index));}
            Click("CollectionPrivacy",()=>ChooseCollection(new[]{"PRIV-CDM-SYSPANE","PRIV-CDM-SILENTAPPS","PRIV-SOFTLANDING","PRIV-TAILORED","UI-SYNC-NOTIFY","PRIV-ADVID-USER"}));
            Click("CollectionExplorer",()=>ChooseCollection(new[]{"UI-FILEEXT","UI-LAUNCHTO","UI-COMPACT-VIEW","UI-NO-RECENT","UI-NO-FREQUENT"}));
            Click("CollectionGaming",()=>ChooseCollection(new[]{"PERF-GAMEDVR-USER","PERF-GAMEDVR-POLICY","EDGE-STARTUP-BOOST","EDGE-BACKGROUND-OFF"}));
            Click("ClearCollection",()=>{collection=null;Visible("ClearCollection",false);Filter();});
            ClickAsync("Diagnose",()=>Run("diagnose",null,null,false));ClickAsync("Verify",()=>Run("verify",null,null,false));
            ClickAsync("Preview",async()=>{var item=Selected();if(item!=null)await Run(item.Verb,item.Id,null,true);});
            ClickAsync("Apply",async()=>{var item=Selected();if(item!=null&&await Confirm(item.Title+"\n\n"+item.Description+"\n\n"+item.Caveat+"\n\nОткат: "+item.Rollback))await Run(item.Verb,item.Id,null,false);});
            ClickAsync("Revert",async()=>{var item=Selected();if(item!=null&&await Confirm("Восстановить сохранённое состояние для «"+item.Title+"»?"))await Run("revert",item.Id,null,false);});
            Click("Star",()=>{var item=Selected();if(item==null)return;if(preferences.Favorites.Contains(item.Id))preferences.Favorites.Remove(item.Id);else preferences.Favorites.Add(item.Id);SavePreferences();Filter();});
            Click("RefreshHistory",ReadHistory);
            ClickAsync("HistoryRevert",async()=>{var row=Get<ListBox>("History").SelectedItem as HistoryRow;if(row!=null&&row.CanRevert&&await Confirm("Откатить запуск "+row.Run+"?\n\nСначала откатывайте более новые изменения."))await Run("revert",null,row.Run,false);});
            Click("OpenLogs",()=>OpenFolder("logs"));Click("OpenReports",()=>OpenFolder("reports"));Click("OpenData",()=>OpenFolder(""));
            Click("ReleaseLink",()=>OpenUrl("https://github.com/Lucky2356/wintools/releases"));
            ClickAsync("CheckUpdates",()=>CheckUpdates(true));ClickAsync("InstallUpdate",InstallUpdate);
            Click("LogToggle",()=>ExpandOutput(Get<TextBox>("Output").Visibility!=Visibility.Visible));
            Click("ConfirmYes",()=>FinishConfirmation(true));Click("ConfirmNo",()=>FinishConfirmation(false));
            Window.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&confirmation!=null){FinishConfirmation(false);e.Handled=true;}};
            Window.Closing+=(s,e)=>{if(busy||downloading){e.Cancel=true;Text("Status","Дождитесь завершения операции или загрузки обновления.");return;}FinishConfirmation(false);if(!replacing&&preferences.AutoInstall&&stagedDirectory!=null){try{Updates.LaunchReplacement(stagedDirectory,stagedUpdate.Asset.digest.Substring(7),false);replacing=true;}catch(Exception ex){Text("UpdateStatus",UpdateError(ex));Text("Status","Установка отложена. Можно закрыть приложение повторно.");DiscardStaged();e.Cancel=true;ShowPage(3);}}};
            Window.Closed+=(s,e)=>{closed=true;updateTimer.Stop();SystemEvents.UserPreferenceChanged-=SystemPreferenceChanged;};
            Window.SourceInitialized+=(s,e)=>NativeTheme.TitleBar(new WindowInteropHelper(Window).Handle,PaletteDark());
            Window.SizeChanged+=(s,e)=>{Get<TextBox>("Output").Height=Window.ActualHeight<790?48:100;Window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{if(!closed&&page==0){var list=Get<ListBox>("Items");list.UpdateLayout();if(list.SelectedItem!=null)list.ScrollIntoView(list.SelectedItem);}}));};
            Window.Loaded+=async(s,e)=>{if(smoke){try{await Smoke();}catch(Exception ex){File.WriteAllText(Path.Combine(Program.Home,"portable-error.txt"),ex.ToString());Environment.ExitCode=4;}finally{busy=false;downloading=false;DiscardStaged();Window.Close();}return;}if(preferences.AutoCheck)await CheckUpdates(false);};
            updateTimer.Tick+=async(s,e)=>{if(preferences.AutoCheck&&!closed)await CheckUpdates(false);};if(!smoke)updateTimer.Start();
            InitializePlan();ready=true;ApplyTheme();Filter();ReadHistory();RefreshPlan();ShowPage(0);SystemEvents.UserPreferenceChanged+=SystemPreferenceChanged;
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
            Text("PageTitle",new[]{"Windows под ваши задачи","История изменений","С чего начать","Настройки приложения","Ваш план изменений"}[index]);
            Text("PageEyebrow",new[]{"КАТАЛОГ ДЕЙСТВИЙ","ЖУРНАЛ ЭТОГО КОМПЬЮТЕРА","ПОДБОРКИ","ВАШИ ПРЕДПОЧТЕНИЯ","ПОДГОТОВКА И ВЫПОЛНЕНИЕ"}[index]);
            Text("PageHint",new[]{"Выберите раздел или найдите нужное действие.","Исходные состояния и откат сохранённых запусков.","Небольшие наборы для повседневных задач.","Автообновление, защита и данные приложения.","Соберите действия, проверьте и выполните по порядку."}[index]);
            if(index==0&&ready){Window.UpdateLayout();var list=Get<ListBox>("Items");if(list.SelectedItem!=null)list.ScrollIntoView(list.SelectedItem);}
        }
        private void ChooseCollection(string[] ids){selectedGroup=null;Get<TextBox>("Search").Clear();Get<ComboBox>("Category").SelectedIndex=0;collection=new HashSet<string>(ids);Get<CheckBox>("Favorites").IsChecked=false;Get<CheckBox>("Risky").IsChecked=false;Visible("ClearCollection",true);Filter();ShowPage(0);}
        private Tweak Selected(){var row=Get<ListBox>("Items").SelectedItem as ActionRow;return row==null?null:row.Item;}
        private static string Risk(Tweak item){return item.Risk=="high"?"Высокий риск":item.Risk=="med"?"Средний риск":"Низкий риск";}
        private void Filter() {
            if(!ready)return;var prior=Selected();string id=prior==null?null:prior.Id;
            // SelectedValue can still contain the old key inside SelectionChanged.
            var category=Get<ComboBox>("Category").SelectedItem;string group=category is KeyValuePair<string,string>?((KeyValuePair<string,string>)category).Key:"ALL",query=Get<TextBox>("Search").Text.Trim();
            var scope=catalogue.Where(t=>(collection==null||collection.Contains(t.Id))&&(group=="ALL"||t.Category==group)&&(!Checked("Favorites")||preferences.Favorites.Contains(t.Id))&&(Checked("Risky")||t.Risk!="high")&&(selectedGroup==null||Groups.For(t)==selectedGroup)&&(t.Title+" "+t.Description+" "+t.Caveat+" "+t.Id).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            var groups=scope.GroupBy(t=>group=="ALL"?t.Category:Groups.For(t)).Select(g=>new BrowseGroup{Key=group=="ALL"?"category:"+g.Key:g.Key,Title=group=="ALL"?Catalogue.Categories[g.Key]:g.Key,Detail=g.Count()+" действий · открыть →"}).ToArray();
            bool browsing=!showAll&&collection==null&&selectedGroup==null&&query.Length==0&&!Checked("Favorites")&&(group=="ALL"||groups.Length>1);
            Get<ItemsControl>("BrowseGroups").ItemsSource=groups;Visible("CatalogueGroups",browsing);Visible("CatalogueResults",!browsing);Visible("ShowAll",browsing);Text("BrowseTitle",group=="ALL"?"Выберите раздел":Catalogue.Categories[group]);
            var rows=(browsing?new Tweak[0]:scope).Select(t=>new ActionRow{Item=t,DisplayTitle=(preferences.Favorites.Contains(t.Id)?"★  ":"")+t.Title,Summary=Risk(t)+"  ·  "+Groups.For(t)}).ToArray();
            var list=Get<ListBox>("Items");list.ItemsSource=rows;list.SelectedItem=rows.FirstOrDefault(t=>t.Item.Id==id)??rows.FirstOrDefault();if(list.SelectedItem!=null)list.ScrollIntoView(list.SelectedItem);Visible("EmptyCatalogue",!browsing&&rows.Length==0);Visible("SearchHint",Get<TextBox>("Search").Text.Length==0);Text("Count",scope.Length+" действий");SelectItem();
        }
        private void SelectItem() {
            var selected=Selected();Text("Caveat",selected==null?"—":selected.Caveat);Text("Compatibility",selected==null?"":selected.Compatibility);
            if(!ready)return;var item=Selected();Text("ActionTitle",item==null?"Выберите действие":item.Title);Text("Metadata",item==null?"":Risk(item)+"  ·  Windows "+(item.Os=="any"?"10 / 11":item.Os=="win11"?"11":"10"));Text("Description",item==null?"Результаты поиска появятся слева. Выберите действие, чтобы прочитать его описание.":item.Description);Text("Rollback",item==null?"—":item.Rollback);Get<Button>("Star").Content=item!=null&&preferences.Favorites.Contains(item.Id)?"★":"☆";RefreshEnabled();
        }
        private void RefreshEnabled() {
            if(!ready)return;foreach(var name in operations)Enabled(name,!busy);var item=Selected();Enabled("Apply",!busy&&item!=null);Enabled("Preview",!busy&&item!=null);Enabled("Star",!busy&&item!=null);Enabled("Revert",!busy&&item!=null&&item.Category!="CLEAN"&&item.Kind!="EDGE");var row=Get<ListBox>("History").SelectedItem as HistoryRow;Enabled("HistoryRevert",!busy&&row!=null&&row.CanRevert);
            foreach(var name in new[]{"CheckUpdates","AutoInstall","PreviewChannel"})Enabled(name,!busy&&!checking&&!downloading);Enabled("RestorePoint",!busy);Enabled("InstallUpdate",!busy&&!checking&&!downloading&&available!=null);Visible("InstallUpdate",available!=null);
            RefreshPlanEnabled();
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
            await PrepareAutomaticUpdate();
        }
        private async Task CheckUpdates(bool manual) {
            if(busy||checking||downloading||closed)return;if(stagedDirectory!=null){Text("UpdateStatus","Версия "+stagedUpdate.Release.tag_name+" готова. Установится при закрытии, если включена автоматическая установка.");return;}checking=true;available=null;RefreshEnabled();Text("UpdateStatus","Проверяем новые версии…");Text("Status","Проверка обновлений…");
            try{available=await Updates.Check(preferences.IncludePreview);if(closed)return;string message=available==null?"Новых совместимых версий в выбранном канале нет. Установлена "+Program.Version+".":"Доступна "+available.Release.tag_name+". Установлена "+Program.Version+". Ваши данные сохранятся.";Text("UpdateStatus",message);Text("Status",available==null?"Проверка завершена: обновлений нет":"Доступно обновление "+available.Release.tag_name);}
            catch(Exception ex){if(!closed){Text("UpdateStatus",UpdateError(ex));Text("Status","Не удалось проверить обновления — подробности в настройках.");if(manual)ShowPage(3);}}
            finally{checking=false;if(!closed)RefreshEnabled();}
            await PrepareAutomaticUpdate();
        }
        private static string UpdateError(Exception ex){if(ex is TaskCanceledException)return "Истекло время ожидания GitHub. Проверьте соединение и повторите запрос.";if(ex is System.Net.Http.HttpRequestException)return "Не удалось соединиться с GitHub. Проверьте сеть, прокси и доступ к api.github.com. "+ex.GetBaseException().Message;return ex.Message;}
        private async Task InstallUpdate() {
            if(busy||checking||downloading||available==null)return;SetBusy(true);Visible("DownloadProgress",true);Text("UpdateStatus","Загружаем обновление и проверяем SHA-256…");
            try{var directory=stagedDirectory??await Updates.Download(available,value=>{Get<ProgressBar>("DownloadProgress").Value=value;Text("UpdateStatus","Загрузка обновления: "+value+"%");});Updates.LaunchReplacement(directory,available.Asset.digest.Substring(7));replacing=true;busy=false;Window.Close();}
            catch(Exception ex){Text("UpdateStatus",UpdateError(ex));Text("Status","Обновление не установлено. Текущая версия сохранена.");SetBusy(false);}finally{if(!closed)Visible("DownloadProgress",false);}
        }
        private void DiscardStaged(){stagedDirectory=null;stagedUpdate=null;}
        private async Task PrepareAutomaticUpdate() {
            if(closed||busy||checking||downloading||confirmation!=null||!preferences.AutoInstall||available==null||stagedDirectory!=null)return;
            downloading=true;RefreshEnabled();Visible("DownloadProgress",true);var update=available;
            try{stagedDirectory=await download(update,value=>{Get<ProgressBar>("DownloadProgress").Value=value;Text("UpdateStatus","Загрузка обновления: "+value+"%");});stagedUpdate=update;Text("UpdateStatus","Версия "+update.Release.tag_name+" проверена и готова. Установится при закрытии приложения.");Text("Status","Обновление готово — установится при закрытии.");}
            catch(Exception ex){Text("UpdateStatus",UpdateError(ex));Text("Status","Автообновление отложено. Повторная проверка будет позже.");}
            finally{downloading=false;Visible("DownloadProgress",false);RefreshEnabled();}
        }
        private bool SavePreferences(){try{preferences.Save();return true;}catch(Exception ex){Text("Status","Настройки не сохранены: "+ex.Message);return false;}}
        private void OpenFolder(string relative){try{var path=relative.Length==0?Program.Data:Program.Under(Program.Data,relative);Program.SafeDirectory(path);Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe",Program.Quote(path)){UseShellExecute=true});}catch(Exception ex){Text("Status",ex.Message);}}
        private void OpenUrl(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){Text("Status",ex.Message);}}
        private async Task Smoke() {
            int downloads=0;download=(update,progress)=>{downloads++;return Task.FromResult("fixture-stage");};
            available=new Update{Release=new Release{tag_name="v9.0.0"}};preferences.AutoInstall=true;busy=true;await PrepareAutomaticUpdate();Assert(downloads==0,"Auto update interrupted an operation");busy=false;await PrepareAutomaticUpdate();Assert(downloads==1&&stagedDirectory!=null,"Auto download did not stage");await PrepareAutomaticUpdate();Assert(downloads==1,"Staged update downloaded twice");Get<CheckBox>("AutoInstall").IsChecked=false;Get<CheckBox>("AutoInstall").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));Get<CheckBox>("AutoInstall").IsChecked=true;Get<CheckBox>("AutoInstall").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));Assert(Get<TextBlock>("UpdateStatus").Text.Contains("установится при закрытии"),"Re-enabled automatic update still reported disabled");DiscardStaged();preferences.AutoInstall=false;await PrepareAutomaticUpdate();Assert(downloads==1,"Disabled auto installation downloaded");available=null;preferences.AutoInstall=true;download=Updates.Download;
            Text("UpdateStatus","Установлена версия "+Program.Version+". Автообновление включено.");Text("Status","Готово к работе");
            await Task.Delay(100);Assert(Get<ScrollViewer>("CatalogueGroups").Visibility==Visibility.Visible,"Catalogue did not start with sections");Capture("portable-ui-browse.png");
            await InvokeGroup("category:SVC");Assert((string)Get<ComboBox>("Category").SelectedValue=="SVC","Category card click did not navigate");Assert(Get<ItemsControl>("BrowseGroups").Items.Count>4,"Service subgroups missing");await InvokeGroup("Bluetooth и камера");Assert(Get<ListBox>("Items").Items.Count==5,"Subgroup card click did not navigate");
            Get<Button>("BackToGroups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(Selected()==null,"Back to sections retained hidden selection");
            await NavigationSmoke();
            Get<TextBox>("Search").Text="UI-FILEEXT";Assert(Get<ListBox>("Items").Items.Count==1,"Search from section browser failed");
            var lockedPreferences=Path.Combine(Program.Data,"preferences.json.tmp");using(var locked=new FileStream(lockedPreferences,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){Get<Button>("PlanAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(preferences.Plan.Count==0&&Get<TextBlock>("Status").Text.Contains("не сохранены"),"Failed plan save reported success");}File.Delete(lockedPreferences);
            Get<Button>("PlanAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Get<Button>("PlanAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(preferences.Plan.Count==1&&Preferences.Load().Plan.Count==1,"Plan duplicate or persistence failure");
            Get<TextBox>("Search").Text="UI-LAUNCHTO";Get<Button>("PlanAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(preferences.Plan.Count==2,"Plan add failed");ShowPage(4);await Task.Delay(100);Capture("portable-ui-plan.png");
            Get<ListBox>("PlanItems").SelectedIndex=0;using(var locked=new FileStream(lockedPreferences,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){ChangePlan(0);Assert(preferences.Plan.Count==2&&Preferences.Load().Plan.Count==2,"Failed plan removal diverged from saved plan");}File.Delete(lockedPreferences);
            var realPlanAction=planAction;int planCalls=0;planAction=(item,dry,progress)=>{planCalls++;return Task.FromResult(new EngineResult{Code=0,Output="fixture preview"});};await RunPlan(true);Assert(planCalls==2&&preferences.Plan.Count==2,"Preview changed the saved plan");
            planCalls=0;planAction=(item,dry,progress)=>Task.FromResult(new EngineResult{Code=++planCalls==1?0:4,Output="fixture result"});await RunPlan(false);Assert(planCalls==2&&preferences.Plan.Count==1&&Preferences.Load().Plan.Count==1,"Plan did not stop and retain failed item");
            preferences.Plan.Insert(0,"UI-FILEEXT");planCalls=0;planAction=(item,dry,progress)=>{planCalls++;stopPlan=true;return Task.FromResult(new EngineResult{Code=0,Output="fixture stopped"});};await RunPlan(false);Assert(planCalls==1&&preferences.Plan.Count==1,"Plan stop ignored");preferences.Plan.Clear();preferences.Save();RefreshPlan();planAction=realPlanAction;ExpandOutput(false);Get<TextBox>("Output").Text="Результаты предпросмотра и выполнения появятся здесь.";
            Get<TextBox>("Search").Clear();Get<ComboBox>("Category").SelectedIndex=0;showAll=true;Filter();ShowPage(0);
            await Task.Delay(250);Get<ComboBox>("Theme").SelectedIndex=2;Assert(Preferences.Load().Theme=="dark","Theme persistence");
            Get<ListBox>("Items").SelectedIndex=3;var id=Selected().Id;Get<Button>("Star").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(Selected().Id==id,"Favorite selection lost");Get<Button>("Star").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SetBusy(true);Get<ComboBox>("Theme").SelectedIndex=1;Assert(!Get<Button>("Apply").IsEnabled,"Theme lost operation lock");SetBusy(false);Text("Status","Готово к работе");
            Get<TextBox>("Search").Text="__not_found__";Assert(Selected()==null&&!Get<Button>("Apply").IsEnabled,"Empty search actions");Get<TextBox>("Search").Clear();
            var path=Path.Combine(Program.Data,"state","applied.dat");if(File.Exists(path))throw new IOException("UI smoke requires isolated journal.");using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){ReadHistory();Assert(Get<TextBlock>("HistoryStatus").Text.Contains("недоступен"),"Locked history");}File.Delete(path);ReadHistory();
            foreach(int mode in new[]{1,2}){Get<ComboBox>("Theme").SelectedIndex=mode;ShowPage(0);await Task.Delay(100);Capture(mode==2?"portable-ui.png":"portable-ui-light.png");ShowPage(3);await Task.Delay(100);Capture(mode==2?"portable-ui-settings-dark.png":"portable-ui-settings-light.png");}
            ChooseCollection(new[]{"UI-FILEEXT","UI-LAUNCHTO"});Assert(Get<ListBox>("Items").Items.Count==2,"Collection filter failed");Get<Button>("ClearCollection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(Get<ListBox>("Items").Items.Count>100,"Collection reset failed");
            ShowPage(2);await Task.Delay(100);Capture("portable-ui-collections.png");ShowPage(3);Text("UpdateStatus","Установлена актуальная версия "+Program.Version+". Обновления загружаются автоматически.");await Task.Delay(100);Capture("portable-ui-updates.png");
            ShowPage(0);Window.Width=Window.MinWidth;Window.Height=Window.MinHeight;ExpandOutput(true);Window.UpdateLayout();await Task.Delay(100);Assert(IsVisibleInWindow("Apply")&&IsVisibleInWindow("Star")&&IsVisibleInWindow("ActionTitle")&&IsVisibleInWindow("Metadata")&&IsVisibleInWindow("Verify"),"Compact action details or navigation clipped");Capture("portable-ui-compact.png");
            ExpandOutput(false);Get<ComboBox>("Theme").SelectedIndex=0;
        }
        private bool IsVisibleInWindow(string name){var control=Get<FrameworkElement>(name);for(var parent=VisualTreeHelper.GetParent(control);parent!=null;parent=VisualTreeHelper.GetParent(parent)){var element=parent as FrameworkElement;if(element==null)continue;var bounds=control.TransformToAncestor(element).TransformBounds(new Rect(control.RenderSize));if(bounds.Top< -1||bounds.Left< -1||bounds.Bottom>element.ActualHeight+1||bounds.Right>element.ActualWidth+1)return false;}return true;}
        private static IEnumerable<Button> Buttons(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);var button=child as Button;if(button!=null)yield return button;foreach(var nested in Buttons(child))yield return nested;}}
        private async Task InvokeGroup(string key){Window.UpdateLayout();var button=Buttons(Get<ItemsControl>("BrowseGroups")).FirstOrDefault(b=>(string)b.Tag==key);Assert(button!=null,"Group card not rendered: "+key);var peer=new System.Windows.Automation.Peers.ButtonAutomationPeer(button);((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();await Task.Delay(100);Window.UpdateLayout();}
        private async Task NavigationSmoke(){
            foreach(var category in catalogue.Select(t=>t.Category).Distinct()){
                Get<Button>("BackToGroups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await InvokeGroup("category:"+category);
                if(Get<ScrollViewer>("CatalogueGroups").Visibility==Visibility.Visible){
                    var keys=Get<ItemsControl>("BrowseGroups").Items.Cast<BrowseGroup>().Select(g=>g.Key).ToArray();
                    foreach(var key in keys){await InvokeGroup(key);Assert(Get<ListBox>("Items").Items.Cast<ActionRow>().All(r=>r.Item.Category==category&&Groups.For(r.Item)==key)&&Selected()!=null,"Wrong subgroup results: "+key);Get<Button>("BackToGroups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await InvokeGroup("category:"+category);}
                }else Assert(Selected()!=null&&Get<ListBox>("Items").Items.Cast<ActionRow>().All(r=>r.Item.Category==category),"Wrong category results: "+category);
            }
            ChooseCollection(new[]{"UI-FILEEXT","UI-LAUNCHTO"});Get<ComboBox>("Category").SelectedValue="SVC";Assert(collection==null&&Get<ScrollViewer>("CatalogueGroups").Visibility==Visibility.Visible,"Collection restricted another category");await InvokeGroup("Bluetooth и камера");Assert(Get<ListBox>("Items").Items.Count==5,"Collection leaked into subgroup");
            Get<Button>("BackToGroups").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        private void Capture(string name){Window.UpdateLayout();var root=(FrameworkElement)Window.Content;var bitmap=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(Program.Home,name)))encoder.Save(file);}
        private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
