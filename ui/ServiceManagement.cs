using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private ServiceSnapshot selectedServiceSnapshot;
        private ServiceChange selectedServiceRestore;
        private WrapPanel serviceControls;
        private Button serviceStart,serviceStop,serviceRestart,serviceModeApply,serviceRestore;
        private ComboBox serviceStartMode;
        private Func<string,string,string,Task<EngineResult>> serviceRun=ServiceActions.Run;
        private Func<string,ServiceSnapshot> serviceInspect=ServiceActions.Inspect;
        private Func<string,ServiceChange> serviceLatest=ServiceActions.Latest;
        private void InitializeServiceManagement(Grid footer){
            footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            serviceControls=new WrapPanel{Margin=new Thickness(0,10,0,0),Visibility=Visibility.Collapsed};Grid.SetRow(serviceControls,1);Grid.SetColumnSpan(serviceControls,2);footer.Children.Add(serviceControls);
            serviceStart=ServiceControlButton("Запустить","start");serviceStop=ServiceControlButton("Остановить","stop");serviceRestart=ServiceControlButton("Перезапустить","restart");
            serviceStartMode=new ComboBox{Width=195,ItemsSource=new[]{"Автоматически","Авто, с задержкой","Вручную / по запросу","Запуск отключён"},SelectedIndex=0,Margin=new Thickness(0,0,8,6)};System.Windows.Automation.AutomationProperties.SetName(serviceStartMode,"Новый тип запуска службы");serviceControls.Children.Add(serviceStartMode);
            serviceModeApply=ServiceControlButton("Изменить запуск",null);serviceModeApply.Click+=async(s,e)=>await ChangeSelectedService(new[]{"auto","delayed","manual","disabled"}[serviceStartMode.SelectedIndex]);serviceRestore=ServiceControlButton("Откат службы","restore");
            serviceList.SelectionChanged+=async(s,e)=>await ReadServiceSelection();
        }
        private Button ServiceControlButton(string title,string action){var button=new Button{Content=title,Padding=new Thickness(10,6,10,6),MinHeight=34,Margin=new Thickness(0,0,8,6)};serviceControls.Children.Add(button);if(action!=null)button.Click+=async(s,e)=>await ChangeSelectedService(action);return button;}
        private void RefreshServiceControls(){
            if(serviceControls==null)return;var current=selectedServiceSnapshot;bool ready=!busy&&ServiceActions.Manageable(current);
            serviceControls.Visibility=serviceList.SelectedItem==null?Visibility.Collapsed:Visibility.Visible;serviceStart.IsEnabled=ready&&current.State=="Stopped"&&current.Mode!="Disabled";serviceStop.IsEnabled=ready&&current.State=="Running"&&current.CanStop;serviceRestart.IsEnabled=serviceStop.IsEnabled&&current.Mode!="Disabled";serviceModeApply.IsEnabled=serviceStartMode.IsEnabled=ready;serviceRestore.IsEnabled=ready&&selectedServiceRestore!=null;
        }
        private async Task ReadServiceSelection(){
            var row=serviceList.SelectedItem as ServiceState;selectedServiceSnapshot=null;selectedServiceRestore=null;RefreshServiceControls();if(row==null)return;
            try{var inspect=serviceInspect;var history=serviceLatest;var current=await Task.Run(()=>inspect(row.Name));ServiceChange original=null;string historyError=null;try{original=await Task.Run(()=>history(row.Name));}catch(Exception ex){historyError="История недоступна: "+ex.Message;}if(closed||(serviceList.SelectedItem as ServiceState)!=row)return;selectedServiceSnapshot=current;selectedServiceRestore=original;serviceStartMode.SelectedIndex=current.Mode=="Auto"?(current.Delayed?1:0):current.Mode=="Manual"?2:3;
                string description=string.IsNullOrWhiteSpace(current.Description)?"Описание Windows не указано. Проверьте назначение службы перед изменением.":current.Description;serviceSelection.Text=historyError??(description.Length>230?description.Substring(0,230)+"…":description);serviceSelection.ToolTip=description;serviceRestore.ToolTip=historyError??(original==null?"Сохранённых изменений этой службы пока нет.":"Восстановить состояние перед действием "+original.Action+" от "+original.TimeUtc);RefreshServiceControls();
            }catch(Exception ex){if(!closed&&(serviceList.SelectedItem as ServiceState)==row){serviceSelection.Text="Не удалось подготовить управление: "+ex.Message;RefreshServiceControls();}}
        }
        private async Task ChangeSelectedService(string action){
            var snapshot=selectedServiceSnapshot;var original=selectedServiceRestore;if(busy||snapshot==null||(action=="restore"&&original==null))return;
            string effect=action=="start"?"Windows запустит службу и при необходимости её зависимости.":action=="stop"?"Служба остановится сейчас. Связанные функции и подключения могут перестать работать. Зависимые службы автоматически не останавливаются.":action=="restart"?"Служба остановится и запустится снова. Её подключения будут прерваны.":action=="restore"?"Вернём тип запуска и состояние выбранной службы перед последним сохранённым действием. Более поздние изменения других программ могут быть перезаписаны. Зависимости отдельно не восстанавливаются.":"Тип запуска станет: "+(action=="auto"?"автоматический":action=="delayed"?"автоматический с задержкой":action=="manual"?"вручную / по запросу":"отключённый")+". Работающая служба сама по себе не остановится.";
            if(!await Confirm(snapshot.Label+" ("+snapshot.Name+")\n\n"+snapshot.Description+"\n\n"+effect+"\n\nПеред изменением сохраним состояние для отката этой службы."))return;
            await RunServiceChange(snapshot.Name,action,action=="restore"?original.Id:null);
        }
        private async Task RunServiceChange(string name,string action,string id){
            if(busy)return;
            SetBusy(true);RefreshServiceControls();serviceStatus.Text="Windows выполняет действие службы…";
            try{var result=await serviceRun(name,action,id);Get<TextBox>("Output").Text=result.Output;Text("Status",result.Code==0?"Действие службы завершено. Состояние обновляется.":"Действие службы требует внимания. Подробности — в выводе.");if(result.Code!=0)ExpandOutput(true);}
            catch(Exception ex){Text("Status","Действие службы не завершено: "+ex.Message);}
            finally{SetBusy(false);RefreshServiceControls();}
            ReadHistory();await RefreshServices();await ReadServiceSelection();
        }
        private static DateTime HistoryTime(string run){DateTime time;return run.Length>=19&&DateTime.TryParseExact(run.Substring(0,19),"yyyyMMdd_HHmmss_fff",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeLocal,out time)?time.ToUniversalTime():DateTime.MinValue;}
        private static string ServiceActionTitle(string action){return action=="start"?"Запуск":action=="stop"?"Остановка":action=="restart"?"Перезапуск":action=="restore"?"Восстановление":action=="auto"?"Автоматический запуск":action=="delayed"?"Отложенный запуск":action=="manual"?"Ручной запуск":"Отключение запуска";}
        private static HistoryRow[] ServiceHistoryRows(){return ServiceActions.History().Select(record=>new HistoryRow{Run=record.Id,ServiceName=record.Name,TimeUtc=DateTime.Parse(record.TimeUtc,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),Title=ServiceActionTitle(record.Action)+" службы · "+record.Before.Label,Status=record.Status=="REVERTED"?"Откат выполнен":record.Status=="OK"?"Выполнено":"Требует внимания",CanRevert=record.Action!="restore"&&record.Status!="REVERTED"}).ToArray();}
        private async Task RestoreServiceHistory(HistoryRow row){
            if(busy||!row.CanRevert)return;try{var record=ServiceActions.Read(row.Run);if(record.Name!=row.ServiceName||record.Status=="REVERTED")throw new InvalidOperationException("Запись изменилась. Обновите историю.");if(await Confirm("Восстановить службу «"+record.Before.Label+"» ("+record.Name+")?\n\nВернём тип запуска и состояние перед выбранным действием. Сначала откатывайте самые новые изменения; изменения зависимостей отдельно не восстанавливаются."))await RunServiceChange(record.Name,"restore",record.Id);}catch(Exception ex){Text("Status","Откат службы не выполнен: "+ex.Message);}
        }
    }
}
