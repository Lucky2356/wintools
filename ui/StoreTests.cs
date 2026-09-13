using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task StoreSmoke(){
            var package=new StorePackage{FullName="Wintools.Fixture_1.0.0.0_x64__abcdefghijklm",FamilyName="Wintools.Fixture_abcdefghijklm",Name="Тестовое приложение",Publisher="Wintools fixture",Version="1.0.0.0",Location=Program.Home,ResetSupported=true,Entries=new[]{new AppEntry{Name="Первое окно",Id="Wintools.Fixture_abcdefghijklm!One"},new AppEntry{Name="Второе окно",Id="Wintools.Fixture_abcdefghijklm!Two"}}};
            Assert(!StorePackages.Identity("bad'; Remove-Item C:\\")&&!StorePackages.Identity("bad\n")&&!StorePackages.EntryValid(package,new AppEntry{Id="Other_abcdefghijklm!One"}),"Unsafe package identity accepted");
            var originalRows=installedApplications;var read=storeRead;var run=storeRun;var launch=storeLaunch;int calls=0;string launched=null;
            try{
                storeRun=(p,a)=>{calls++;return Task.FromResult(new EngineResult{Code=0,Output="Fixture operation completed"});};storeLaunch=id=>launched=id;storeRead=()=>Task.FromResult(new StoreInventory{Rows=new[]{package},Errors=new string[0]});
                installedApplications=new[]{StorePackages.Row(package),new InstalledApplication{Name="Обычная программа",Key="desktop"}};applicationKind.SelectedIndex=0;FilterApplications();ShowPage(9);applicationKind.SelectedIndex=2;Assert(applicationList.Items.Count==1,"Store filter mixes desktop applications");applicationList.SelectedIndex=0;
                Assert(applicationReset.IsEnabled&&applicationRegister.IsEnabled&&applicationRemove.IsEnabled,"Supported Store actions unavailable");package.Protected=true;ApplicationSelection();Assert(!applicationReset.IsEnabled&&!applicationRegister.IsEnabled&&!applicationRemove.IsEnabled,"Protected Store package editable");package.Protected=false;package.ResetSupported=false;ApplicationSelection();Assert(!applicationReset.IsEnabled,"Unsupported reset enabled");package.ResetSupported=true;ApplicationSelection();
                foreach(string action in new[]{"reset","register","remove"}){int before=calls;var task=ChangeStoreApplication(action);Assert(confirmation!=null,"Store action missing confirmation");FinishConfirmation(false);await task;Assert(calls==before,"Cancelled Store action executed");task=ChangeStoreApplication(action);FinishConfirmation(true);await task;Assert(calls==before+1&&!busy,"Confirmed Store action failed");}
                applicationLaunchEntry.SelectedIndex=1;await LaunchStoreApplication();Assert(launched==package.Entries[1].Id,"Selected package entry changed before launch");launched=null;storeRead=()=>Task.FromResult(new StoreInventory{Rows=new StorePackage[0],Errors=new string[0]});await LaunchStoreApplication();Assert(launched==null&&applicationStatus.Text.Contains("изменилась"),"Stale Store launch executed");
                installedApplications=Enumerable.Range(0,60).Select(i=>{var row=StorePackages.Row(package);row.Name="Приложение "+i;row.Key+="-"+i;return row;}).ToArray();FilterApplications();applicationList.SelectedIndex=0;
                foreach(var size in new[]{new Size(1280,800),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();await Task.Delay(60);Assert(applicationList.ActualHeight>120&&applicationList.ActualWidth>400,"Store controls leave no usable list: "+applicationList.ActualWidth+" x "+applicationList.ActualHeight);Capture(size.Width==800?"portable-ui-store-compact.png":"portable-ui-store.png");}
                applicationStoreMore.ContextMenu.PlacementTarget=applicationStoreMore;applicationStoreMore.ContextMenu.IsOpen=true;Window.UpdateLayout();await Task.Delay(80);Assert(applicationRegister.ActualHeight>20&&applicationReset.ActualHeight>20,"Store menu actions not rendered");applicationStoreMore.ContextMenu.IsOpen=false;
                applicationSearch.Text="__missing__";Assert(applicationList.Items.Count==0&&!applicationLaunch.IsEnabled&&!applicationReset.IsEnabled,"Empty Store list leaves actions enabled");applicationSearch.Clear();
                storeRead=()=>{throw new IOException("fixture Store read failure");};await ReadApplications();Assert(applicationStatus.Text.Contains("fixture Store read failure")&&installedApplications.All(r=>r.Package==null),"Store failure hides desktop list or appears as empty success");
            }finally{storeRead=read;storeRun=run;storeLaunch=launch;installedApplications=originalRows;applicationKind.SelectedIndex=0;FilterApplications();ShowPage(0);}
            int writes=0;Func<object,bool,Task<EngineResult>> execute=(request,readOnly)=>{writes++;Assert(!readOnly&&StoreActions.History().Any(r=>r.Status=="PENDING"),"Package changed before durable pending record");return Task.FromResult(new EngineResult{Code=0,Output="Fixture recorded result"});};
            var result=await StoreActions.Run(package,"register",execute);Assert(result.Code==0&&writes==1,"Store journal operation failed");var record=StoreActions.History().Single(r=>r.Status=="OK");ReadHistory();var history=Get<ListBox>("History");history.SelectedItem=history.Items.Cast<HistoryRow>().Single(r=>r.StoreChange&&r.Run==record.Id);Assert(!Get<Button>("HistoryRevert").IsEnabled&&Get<Button>("HistoryReport").IsEnabled,"Store history exposes rollback or hides report");ShowStoreReport(record.Id);Assert(Get<TextBox>("Output").Text.Contains("Fixture recorded result"),"Store report missing saved result");ExpandOutput(false);
            string gate=Path.Combine(Program.Data,"state","run.lock");using(var file=new FileStream(gate,FileMode.CreateNew,FileAccess.Write,FileShare.None)){result=await StoreActions.Run(package,"reset",execute);Assert(result.Code==4&&writes==1,"Store operation ignored common lock");}File.Delete(gate);
            result=await StoreActions.Run(package,"reset",(request,readOnly)=>Task.FromResult(new EngineResult{Code=4,Output="Fixture package failure"}));Assert(result.Code==4&&StoreActions.History().Any(r=>r.Status=="FAILED"&&r.Summary.Contains("Fixture package failure")),"Store failure not retained");
            string path=Path.Combine(Program.Data,"store-history",record.Id+".json"),saved=File.ReadAllText(path);try{File.WriteAllText(path,"{bad");ReadHistory();Assert(Get<TextBlock>("HistoryStatus").Text.Contains("недоступен"),"Corrupt Store record not reported");}finally{File.WriteAllText(path,saved);ReadHistory();}
        }
    }
}
