using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private ScrollViewer cleanupPanel;
        private FrameworkElement[] integrityContent;
        private readonly string[] cleanupTitles={"Временные файлы пользователя","Временные файлы Windows","Отчёты о сбоях приложений"};
        private readonly CheckBox[] cleanupChecks=new CheckBox[3];
        private readonly TextBlock[] cleanupValues=new TextBlock[3];
        private readonly CleanupEstimate[] cleanupEstimates=new CleanupEstimate[3];
        private Button cleanupScan,cleanupApply,cleanupStop;
        private TextBlock cleanupStatus;
        private CancellationTokenSource cleanupCancel;
        private Func<string,CancellationToken,CleanupEstimate> cleanupRead=CleanupPreview.Read;
        private Func<string,Action<string>,Task<EngineResult>> cleanupRun=(id,progress)=>Engine.Run("cleanup",id,"-",false,false,progress);
        private void InitializeCleanup(Grid root){
            integrityContent=root.Children.Cast<FrameworkElement>().Where(c=>c!=integrityChoice).ToArray();var panel=new StackPanel();cleanupPanel=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed};Grid.SetRow(cleanupPanel,1);Grid.SetRowSpan(cleanupPanel,4);root.Children.Add(cleanupPanel);
            panel.Children.Add(Paragraph("Сначала рассчитайте объём, затем отметьте нужные категории. Удаление необратимо. Возраст определяется по последнему изменению файла; занятые файлы могут остаться."));
            var controls=new WrapPanel();panel.Children.Add(controls);cleanupScan=ToolButton(controls,"Рассчитать объём",async()=>await ScanCleanup());cleanupApply=ToolButton(controls,"Удалить выбранное…",async()=>await ApplyCleanup());cleanupStop=ToolButton(controls,"Остановить расчёт",()=>{if(cleanupCancel!=null)cleanupCancel.Cancel();RefreshCleanupEnabled();});
            foreach(Button button in controls.Children)button.Margin=new Thickness(0,0,8,8);
            cleanupStatus=Paragraph("Расчёт ещё не выполнен. Загрузки, корзина, документы и профили браузеров в эти категории не входят.");panel.Children.Add(cleanupStatus);
            for(int i=0;i<3;i++){var card=new StackPanel();cleanupChecks[i]=new CheckBox{Content=cleanupTitles[i],IsEnabled=false,Margin=new Thickness(0,0,0,6)};cleanupChecks[i].Click+=(s,e)=>RefreshCleanupEnabled();card.Children.Add(cleanupChecks[i]);card.Children.Add(Paragraph(i==2?"Файлы CrashDumps старше 7 дней. Они могут понадобиться для выяснения причин сбоев.":"Файлы Temp старше 3 дней. Папки и ссылки пропускаются."));cleanupValues[i]=Paragraph("Объём неизвестен");card.Children.Add(cleanupValues[i]);var border=new Border{Child=card,Padding=new Thickness(16),Margin=new Thickness(0,0,0,10),CornerRadius=new CornerRadius(12)};border.SetResourceReference(Border.BackgroundProperty,"Surface");panel.Children.Add(border);}RefreshCleanupEnabled();
        }
        private void MaintenanceView(){if(cleanupPanel==null)return;bool cleanup=integrityChoice.SelectedIndex==integrityActions.Length;cleanupPanel.Visibility=cleanup?Visibility.Visible:Visibility.Collapsed;foreach(var item in integrityContent)item.Visibility=cleanup?Visibility.Collapsed:Visibility.Visible;}
        private void RefreshCleanupEnabled(){if(cleanupScan==null)return;cleanupScan.IsEnabled=!busy;cleanupApply.IsEnabled=!busy&&Enumerable.Range(0,3).Any(i=>cleanupChecks[i]!=null&&cleanupChecks[i].IsChecked==true&&cleanupEstimates[i]!=null&&cleanupEstimates[i].Errors==0&&cleanupEstimates[i].Files>0);cleanupStop.Visibility=cleanupCancel==null?Visibility.Collapsed:Visibility.Visible;cleanupStop.IsEnabled=cleanupCancel!=null&&!cleanupCancel.IsCancellationRequested;for(int i=0;i<3;i++)if(cleanupChecks[i]!=null)cleanupChecks[i].IsEnabled=!busy&&cleanupEstimates[i]!=null&&cleanupEstimates[i].Errors==0&&cleanupEstimates[i].Files>0;}
        private async Task ScanCleanup(){
            if(busy)return;cleanupCancel=new CancellationTokenSource();SetBusy(true);for(int i=0;i<3;i++){cleanupEstimates[i]=null;cleanupChecks[i].IsChecked=false;cleanupValues[i].Text="Ожидает расчёта";}RefreshCleanupEnabled();
            try{for(int i=0;i<3;i++){cleanupStatus.Text="Считаем: "+cleanupTitles[i]+"… Ничего не удаляем.";var id=CleanupPreview.Ids[i];var read=cleanupRead;var token=cleanupCancel.Token;var result=await Task.Run(()=>read(id,token));token.ThrowIfCancellationRequested();cleanupEstimates[i]=result;cleanupValues[i].Text=result.Source+"\n"+CleanupPreview.Size(result.Bytes)+" · Файлов: "+result.Files+" · Пропущено ссылок: "+result.SkippedLinks+(result.Errors>0?"\nРасчёт неполный. "+result.Error:"");}cleanupStatus.Text="Расчёт завершён. Объём приблизительный: это размеры файлов, а не гарантированно освобождаемое место. Перед удалением состав будет проверен заново.";}
            catch(OperationCanceledException){cleanupStatus.Text="Расчёт остановлен. Ничего не удалено; доступны только завершённые категории.";}catch(Exception ex){cleanupStatus.Text="Расчёт не завершён: "+ex.Message;}
            finally{cleanupCancel.Dispose();cleanupCancel=null;SetBusy(false);RefreshCleanupEnabled();Text("Status",cleanupStatus.Text);}
        }
        private async Task ApplyCleanup(){
            if(busy)return;var selected=Enumerable.Range(0,3).Where(i=>cleanupChecks[i].IsChecked==true&&cleanupEstimates[i]!=null&&cleanupEstimates[i].Errors==0&&cleanupEstimates[i].Files>0).ToArray();if(selected.Length==0)return;
            if(!await Confirm("Удалить файлы из выбранных категорий?\n\n"+string.Join("\n",selected.Select(i=>cleanupTitles[i]+": примерно "+CleanupPreview.Size(cleanupEstimates[i].Bytes)+"\n"+cleanupEstimates[i].Source))+"\n\nФайлы старше 3 дней (отчёты о сбоях — 7 дней) будут удалены без корзины и отката. Состав мог измениться после расчёта. Windows может запросить права администратора."))return;
            SetBusy(true);var output=new StringBuilder();bool success=true;
            try{foreach(int i in selected){cleanupStatus.Text="Очищаем: "+cleanupTitles[i];var result=await cleanupRun(CleanupPreview.Ids[i],text=>{Get<TextBox>("Output").Text=output.ToString()+text;});output.AppendLine(cleanupTitles[i]).AppendLine(result.Output);cleanupEstimates[i]=null;cleanupChecks[i].IsChecked=false;cleanupValues[i].Text="Повторите расчёт после очистки";if(result.Code!=0){success=false;break;}}cleanupStatus.Text=success?"Команды очистки завершены. Повторите расчёт, чтобы проверить оставшиеся файлы. Подробности — в выводе.":"Очистка выполнена не полностью. Часть файлов могла быть удалена; последующие категории не запускались. Подробности — в выводе.";}
            catch(Exception ex){success=false;output.AppendLine(ex.Message);cleanupStatus.Text="Очистка не завершена: "+ex.Message+". Часть файлов могла быть удалена; повторите расчёт.";for(int i=0;i<3;i++){cleanupEstimates[i]=null;cleanupChecks[i].IsChecked=false;cleanupValues[i].Text="Объём неизвестен — повторите расчёт";}}
            finally{SetBusy(false);RefreshCleanupEnabled();Get<TextBox>("Output").Text=output.ToString();if(!success)ExpandOutput(true);Text("Status",cleanupStatus.Text);}
        }
    }
}
