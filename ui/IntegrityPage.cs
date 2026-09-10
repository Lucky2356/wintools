using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private readonly string[] integrityActions={"dism-scan","sfc-verify","dism-status"};
        private ComboBox integrityChoice;
        private TextBlock integrityDescription,integrityStatus;
        private TextBox integrityReport;
        private Button integrityStart,integrityStop;
        private ProgressBar integrityProgress;
        private bool integrityRunning,integrityCancelRequested;
        private Action integrityCancel;
        private Func<string,Action<string>,Action<Action>,Task<EngineResult>> integrityRun=IntegrityActions.Run;
        private void InitializeIntegrity(){
            var root=new Grid{Visibility=Visibility.Collapsed};Window.RegisterName("IntegrityPage",root);((Grid)Get<FrameworkElement>("SettingsPage").Parent).Children.Add(root);
            foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})root.RowDefinitions.Add(new RowDefinition{Height=height});
            integrityChoice=new ComboBox{ItemsSource=new[]{"Компоненты Windows · полная проверка","Системные файлы · проверка без исправления","Компоненты · статус прошлой проверки"},SelectedIndex=0,Margin=new Thickness(0,0,0,12)};System.Windows.Automation.AutomationProperties.SetName(integrityChoice,"Вид проверки целостности Windows");root.Children.Add(integrityChoice);
            integrityDescription=Paragraph("");Grid.SetRow(integrityDescription,1);root.Children.Add(integrityDescription);integrityChoice.SelectionChanged+=(s,e)=>DescribeIntegrityChoice();DescribeIntegrityChoice();
            var buttons=new WrapPanel{Margin=new Thickness(0,0,0,8)};Grid.SetRow(buttons,2);root.Children.Add(buttons);integrityStart=new Button{Content="Начать проверку",Margin=new Thickness(0,0,10,8)};integrityStart.Style=(Style)Window.FindResource("Primary");integrityStart.Click+=async(s,e)=>await StartIntegrity();buttons.Children.Add(integrityStart);integrityStop=new Button{Content="Остановить проверку",IsEnabled=false,Margin=new Thickness(0,0,0,8)};integrityStop.Click+=(s,e)=>CancelIntegrity();buttons.Children.Add(integrityStop);
            var status=new StackPanel();Grid.SetRow(status,3);root.Children.Add(status);integrityProgress=new ProgressBar{Height=5,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,10),Minimum=0,Maximum=100};integrityProgress.SetResourceReference(Control.ForegroundProperty,"Accent");status.Children.Add(integrityProgress);integrityStatus=Paragraph("Проверка ещё не выполнялась. Результаты сохраняются в истории приложения.");status.Children.Add(integrityStatus);
            integrityReport=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Text="Здесь появится итог проверки и сообщения Windows.",Padding=new Thickness(14)};System.Windows.Automation.AutomationProperties.SetName(integrityReport,"Отчёт проверки Windows");Grid.SetRow(integrityReport,4);root.Children.Add(integrityReport);
            Click("HistoryReport",()=>{var row=Get<ListBox>("History").SelectedItem as HistoryRow;if(!busy&&row!=null&&row.IntegrityCheck)ShowIntegrityReport(row.Run);});
        }
        private void DescribeIntegrityChoice(){if(integrityDescription==null)return;integrityDescription.Text=integrityChoice.SelectedIndex==0?"DISM проверит хранилище компонентов, из которого Windows восстанавливает системные файлы. Это может занять несколько минут. Файлы не исправляются. Windows запросит права администратора.":integrityChoice.SelectedIndex==1?"SFC проверит защищённые системные файлы без исправления. Проверка может занять несколько минут; остановить SFC из приложения нельзя. Не закрывайте Windows до завершения. Потребуются права администратора.":"Быстро читает отметку о повреждении компонентов, сохранённую Windows ранее. Новое сканирование не выполняется. Для проверки текущего состояния выберите полную проверку. Потребуются права администратора.";}
        private void RefreshIntegrityEnabled(){if(integrityStart==null)return;integrityStart.IsEnabled=integrityChoice.IsEnabled=!busy;integrityStop.IsEnabled=integrityRunning&&integrityCancel!=null&&!integrityCancelRequested;}
        private void CancelIntegrity(){if(!integrityRunning||integrityCancel==null||integrityCancelRequested)return;integrityCancelRequested=true;integrityCancel();integrityStatus.Text="Отмена запрошена. Ждём, пока Windows завершит допустимый этап; процесс принудительно не прерывается.";RefreshIntegrityEnabled();}
        private async Task StartIntegrity(){
            if(busy||integrityChoice.SelectedIndex<0)return;string action=integrityActions[integrityChoice.SelectedIndex];integrityRunning=true;integrityCancelRequested=false;integrityCancel=null;SetBusy(true);integrityReport.Text="";Get<TextBox>("Output").Text="Ход проверки показан в разделе «Обслуживание».";integrityProgress.Visibility=Visibility.Visible;integrityProgress.IsIndeterminate=true;integrityStatus.Text="Запускаем проверку. Подтвердите запрос Windows на права администратора.";var started=DateTime.UtcNow;
            try{var result=await integrityRun(action,value=>{integrityReport.Text=IntegrityActions.CleanLog(value);integrityReport.ScrollToEnd();var percent=IntegrityPercent(value);if(percent.HasValue){integrityProgress.IsIndeterminate=false;integrityProgress.Value=percent.Value;}if(!integrityCancelRequested)integrityStatus.Text="Проверяем: "+IntegrityActions.Title(action)+" · Прошло "+(DateTime.UtcNow-started).ToString(@"hh\:mm\:ss")+". Отдельный этап может долго оставаться на одном значении.";},cancel=>{integrityCancel=cancel;RefreshIntegrityEnabled();});integrityReport.Text=result.Output;Get<TextBox>("Output").Text=result.Output;integrityStatus.Text=result.Code==0?"Проверка завершена. Итог — в отчёте ниже; результат не означает оценку быстродействия ПК.":result.Code==2?"Проверка отменена. Полного результата нет.":"Проверка не завершена успешно. Причина — в отчёте ниже.";}
            catch(Exception ex){integrityStatus.Text="Не удалось выполнить проверку: "+ex.Message;integrityReport.Text=ex.Message;}
            finally{integrityCancel=null;integrityRunning=false;integrityProgress.Visibility=Visibility.Collapsed;SetBusy(false);ReadHistory();Text("Status",integrityStatus.Text);}
        }
        private static int? IntegrityPercent(string text){var matches=Regex.Matches(text??"",@"@progress\|(\d{1,3})\b|(?<![\d.])(\d{1,3})\s*%");if(matches.Count==0)return null;var match=matches[matches.Count-1];int value=int.Parse(match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value);return value<=100?(int?)value:null;}
        private void ShowIntegrityReport(string id){try{integrityReport.Text=IntegrityActions.Report(id);Get<TextBox>("Output").Text=integrityReport.Text;integrityStatus.Text="Сохранённый отчёт. Новая проверка не запускалась.";ShowPage(11);}catch(Exception ex){Text("Status","Не удалось открыть отчёт: "+ex.Message);}}
        private static string IntegrityStateLabel(string state){return state=="healthy"?"Повреждения не найдены":state=="not-marked"?"Прошлый статус":state=="cancelled"?"Отменено":state=="pending"?"Не завершено":state=="review"?"Прочитайте отчёт":state=="failed"?"Ошибка проверки":"Нуждается во внимании";}
        private static HistoryRow[] IntegrityHistoryRows(){return IntegrityActions.History().Select(r=>new HistoryRow{Run=r.Id,IntegrityCheck=true,Title=IntegrityActions.Title(r.Action),TimeUtc=DateTime.Parse(r.TimeUtc,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind),Status=IntegrityStateLabel(r.State),CanRevert=false}).ToArray();}
    }
}
