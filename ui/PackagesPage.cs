using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private readonly Dictionary<string,CheckBox> packageChecks=new Dictionary<string,CheckBox>();
        private readonly Dictionary<string,TextBlock> packageStates=new Dictionary<string,TextBlock>();
        private HashSet<string> installedPackages;
        private string wingetVersion;
        private bool readingPackages,stopPackages,packagesChecked;
        private TextBlock packageStatus;
        private Button packageInstall,packageUpgrade,packageUpgradeAll,packageCheck,packageStop,packageStore;
        private Func<string> wingetLocate=Packages.Version;
        private Func<Task<HashSet<string>>> packageInventory=Packages.Installed;
        private Func<string,Action<string>,Task<long>> packageRun=Packages.Run;
        private void InitializePackages(){
            var panel=ToolPage("PackagesPage");
            panel.Children.Add(Paragraph("Устанавливает и обновляет программы из официального каталога winget от Microsoft. Установщики скачиваются с сайтов издателей, winget проверяет их контрольные суммы. Отметьте нужные программы и нажмите «Установить выбранные». Удалить программу можно в разделе «Приложения»."));
            packageStatus=Paragraph("Нажмите «Проверить установленные», чтобы узнать, какие программы уже есть.");panel.Children.Add(packageStatus);
            var buttons=new WrapPanel{Margin=new Thickness(0,0,0,8)};panel.Children.Add(buttons);
            packageInstall=new Button{Content="Установить выбранные",Margin=new Thickness(0,0,10,8)};packageInstall.Style=(Style)Window.FindResource("Primary");packageInstall.Click+=async(s,e)=>await RunPackages("install");buttons.Children.Add(packageInstall);
            packageUpgrade=new Button{Content="Обновить выбранные",Margin=new Thickness(0,0,10,8)};packageUpgrade.Click+=async(s,e)=>await RunPackages("upgrade");buttons.Children.Add(packageUpgrade);
            packageUpgradeAll=new Button{Content="Обновить все программы…",Margin=new Thickness(0,0,10,8)};packageUpgradeAll.Click+=async(s,e)=>await RunPackages("upgrade-all");buttons.Children.Add(packageUpgradeAll);
            packageCheck=new Button{Content="Проверить установленные",Margin=new Thickness(0,0,10,8)};packageCheck.Click+=async(s,e)=>await RefreshPackages();buttons.Children.Add(packageCheck);
            packageStop=new Button{Content="Остановить после текущей",Margin=new Thickness(0,0,10,8),Visibility=Visibility.Collapsed};packageStop.Click+=(s,e)=>{stopPackages=true;packageStop.IsEnabled=false;};buttons.Children.Add(packageStop);
            packageStore=new Button{Content="Установить winget из Store ↗",Margin=new Thickness(0,0,0,8),Visibility=Visibility.Collapsed};packageStore.Click+=(s,e)=>OpenTool("ms-windows-store://pdp/?productid=9NBLGGH4NNS1");buttons.Children.Add(packageStore);
            var columns=new System.Windows.Controls.Primitives.UniformGrid{Columns=1};panel.Children.Add(columns);
            panel.SizeChanged+=(s,e)=>columns.Columns=panel.ActualWidth>=1100?3:panel.ActualWidth>=700?2:1;
            foreach(var group in Packages.Catalog.GroupBy(p=>p.Group)){
                var section=new StackPanel();var card=new Border{Child=section,Padding=new Thickness(16),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,0,10,12)};Card(card);columns.Children.Add(card);
                var heading=Paragraph(group.Key);heading.FontSize=16;heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,8);section.Children.Add(heading);
                foreach(var package in group){
                    var content=new StackPanel();content.Children.Add(new TextBlock{Text=package.Name,FontWeight=FontWeights.SemiBold});var detail=new TextBlock{Text=package.Description,TextWrapping=TextWrapping.Wrap,FontSize=12};detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(detail);
                    var state=new TextBlock{FontSize=12,Margin=new Thickness(0,2,0,0)};state.SetResourceReference(TextBlock.ForegroundProperty,"Accent");content.Children.Add(state);packageStates[package.Id]=state;
                    var check=new CheckBox{Content=content,Margin=new Thickness(0,0,0,10),ToolTip="winget: "+package.Id};check.Click+=(s,e)=>RefreshPackagesEnabled();section.Children.Add(check);packageChecks[package.Id]=check;
                }
            }
            Get<ScrollViewer>("PackagesPage").IsVisibleChanged+=async(s,e)=>{if(!smoke&&Get<ScrollViewer>("PackagesPage").IsVisible&&!packagesChecked)await RefreshPackages();};
            RefreshPackageStates();RefreshPackagesEnabled();
        }
        private string[] SelectedPackages(){return Packages.Catalog.Where(p=>packageChecks[p.Id].IsChecked==true).Select(p=>p.Id).ToArray();}
        private void RefreshPackageStates(){foreach(var package in Packages.Catalog)packageStates[package.Id].Text=installedPackages==null?"Не проверено":installedPackages.Contains(package.Id)?"✓ Установлено":"Не установлено";}
        private void RefreshPackagesEnabled(){
            if(packageInstall==null)return;bool idle=!busy&&!readingPackages,available=wingetVersion!=null,selected=SelectedPackages().Length>0;
            packageInstall.IsEnabled=idle&&available&&selected;packageUpgrade.IsEnabled=idle&&available&&selected;packageUpgradeAll.IsEnabled=idle&&available;packageCheck.IsEnabled=idle;foreach(var check in packageChecks.Values)check.IsEnabled=!busy;
            packageStore.Visibility=packagesChecked&&!available?Visibility.Visible:Visibility.Collapsed;
        }
        private async Task RefreshPackages(){
            if(busy||readingPackages||packageStatus==null)return;readingPackages=true;RefreshPackagesEnabled();packageStatus.Text="Ищем winget и установленные программы…";
            try{
                var locate=wingetLocate;wingetVersion=await Task.Run(()=>locate());packagesChecked=true;
                if(wingetVersion==null){installedPackages=null;packageStatus.Text="winget не найден. Он входит в «Установщик приложений» Microsoft Store; установите или обновите его и повторите проверку.";return;}
                packageStatus.Text="winget "+wingetVersion+". Читаем установленные программы… Первый запуск может обновить каталог winget.";
                installedPackages=await packageInventory();packageStatus.Text="winget "+wingetVersion+". Установлено из списка: "+Packages.Catalog.Count(p=>installedPackages.Contains(p.Id))+" из "+Packages.Catalog.Length+".";
            }catch(Exception ex){installedPackages=null;packageStatus.Text="Не удалось проверить программы: "+ex.Message;}
            finally{readingPackages=false;if(!closed){RefreshPackageStates();RefreshPackagesEnabled();}}
        }
        private async Task RunPackages(string action){
            if(busy||readingPackages||wingetVersion==null)return;var ids=action=="upgrade-all"?new string[]{null}:SelectedPackages();if(ids.Length==0)return;
            string names=action=="upgrade-all"?"все программы, для которых winget знает новую версию":string.Join(", ",ids.Select(id=>Packages.Find(id).Name));
            if(!await Confirm((action=="install"?"Установить":"Обновить")+": "+names+"?\n\nУстановщики скачаются с сайтов издателей и запустятся без вопросов; Windows может запросить права администратора. Продолжая, вы принимаете лицензионные соглашения этих программ и условия источника winget."+(action=="upgrade-all"?" Обновление затронет и программы, установленные не через Wintools.":"")+"\n\nАвтоматического отката нет: удалить программу можно в разделе «Приложения»."))return;
            SetBusy(true);stopPackages=false;packageStop.Visibility=Visibility.Visible;packageStop.IsEnabled=true;ExpandOutput(true);var output=Get<TextBox>("Output");output.Text="";int done=0,failed=0;
            try{
                foreach(var id in ids){
                    if(stopPackages)break;string name=id==null?"Все программы":Packages.Find(id).Name;Text("Status",(action=="install"?"Устанавливаем ":"Обновляем ")+name+"…");output.AppendText("▶ "+name+Environment.NewLine);
                    var record=new PackageChange{Schema="wintools/package-change/1",Id=Guid.NewGuid().ToString("N"),Package=id,Name=name,Action=action,Status="PENDING",TimeUtc=DateTime.UtcNow.ToString("o")};Packages.Save(record);
                    long code;try{code=await packageRun(Packages.Arguments(action,id),line=>Window.Dispatcher.BeginInvoke(new Action(()=>{output.AppendText(line+Environment.NewLine);output.ScrollToEnd();})));}catch(Exception ex){code=-1;output.AppendText(ex.Message+Environment.NewLine);}
                    record.Code=code;record.Status=Packages.Succeeded(code)?"OK":"FAILED";Packages.Save(record);if(record.Status=="OK")done++;else failed++;output.AppendText("■ "+name+": "+Packages.Describe(code,action)+Environment.NewLine+Environment.NewLine);
                }
                Text("Status",(stopPackages?"Остановлено. ":"")+"Готово: "+done+(failed>0?", с ошибкой: "+failed+". Подробности — в выводе.":"."));
            }catch(Exception ex){Text("Status","winget не завершил работу: "+ex.Message);}
            finally{packageStop.Visibility=Visibility.Collapsed;SetBusy(false);ReadHistory();}
            await RefreshPackages();
        }
        private static HistoryRow[] PackageHistoryRows(){return Packages.History().Select(r=>new HistoryRow{Run=r.Id,PackageChange=true,TimeUtc=DateTime.Parse(r.TimeUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),Title=(r.Action=="install"?"Установка: ":"Обновление: ")+r.Name,Status=r.Status=="PENDING"?"Прервано":Packages.Describe(r.Code,r.Action)+" · без отката",CanRevert=false}).ToArray();}
    }
}
