using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task ServiceWatchingSmoke(){
            var read=serviceRead;var inspect=serviceInspect;var latest=serviceLatest;var state=Window.WindowState;
            try{
                string currentState="Running";int calls=0;
                serviceRead=()=>{calls++;return Enumerable.Range(0,100).Select(i=>new ServiceState{Name="Fixture"+i,Label="Служба "+i.ToString("D3"),State=currentState,Mode="Manual"}).ToArray();};
                serviceInspect=name=>new ServiceSnapshot{Name=name,Label=name,Description="Тест обновления без изменения Windows.",State=currentState,Mode="Manual",Kind="Own Process",CanStop=true};serviceLatest=name=>null;
                serviceSearch.Clear();runningOnly.IsChecked=false;serviceMode.SelectedIndex=0;ShowPage(5);serviceWatch.IsChecked=true;
                Assert(CanWatchServices()&&!serviceTimer.IsEnabled,"Service watch eligibility or smoke isolation failed");
                serviceWatch.IsChecked=false;await ServiceWatchTick();Assert(calls==0,"Paused service watch queried Windows");serviceWatch.IsChecked=true;
                SetBusy(true);await ServiceWatchTick();Assert(calls==0,"Service watch overlapped a mutation");SetBusy(false);
                ShowPage(2);await ServiceWatchTick();Assert(calls==0,"Hidden service watch queried Windows");ShowPage(5);
                Window.WindowState=WindowState.Minimized;Assert(!CanWatchServices(),"Minimized service watch remains active");Window.WindowState=state;
                await RefreshServices();Window.Width=800;Window.Height=600;Window.UpdateLayout();serviceList.SelectedIndex=60;await ReadServiceSelection();serviceList.ScrollIntoView(serviceList.SelectedItem);Window.UpdateLayout();
                var selected=serviceList.SelectedItem;var source=serviceList.ItemsSource;var scroll=ServiceScroll(serviceList);double offset=scroll.VerticalOffset;int notifications=0;((ServiceState)selected).PropertyChanged+=(s,e)=>notifications++;
                serviceStartMode.SelectedIndex=0;currentState="Stopped";await ServiceWatchTick();Window.UpdateLayout();
                Assert(ReferenceEquals(selected,serviceList.SelectedItem)&&ReferenceEquals(source,serviceList.ItemsSource),"Service refresh replaced stable rows or selection");
                Assert(Math.Abs(scroll.VerticalOffset-offset)<1&&notifications>0,"Service refresh lost scroll or binding notification");
                Assert(serviceStart.IsEnabled&&!serviceStop.IsEnabled&&serviceStartMode.SelectedIndex==0,"Service refresh lost command state or user's pending startup choice");
                Text("Status","Готово к работе");serviceStatus.Text="Тест: 100 служб, изменения состояния получены автоматически.";Capture("portable-ui-service-watch.png");
                using(var gate=new ManualResetEventSlim(false)){
                    var started=new TaskCompletionSource<bool>();int reads=0;
                    serviceInspect=name=>{int index=Interlocked.Increment(ref reads);if(index==1){started.SetResult(true);if(!gate.Wait(10000))throw new TimeoutException("Service selection test gate");}return new ServiceSnapshot{Name=name,Label=name,State=index==1?"Stopped":"Running",Mode="Manual",Kind="Own Process",CanStop=true};};
                    var old=ReadServiceSelection();await started.Task;await ReadServiceSelection();gate.Set();await old;
                    Assert(selectedServiceSnapshot.State=="Running"&&serviceStop.IsEnabled,"Late service selection overwrote newer result");
                }
                using(var gate=new ManualResetEventSlim(false)){
                    var started=new TaskCompletionSource<bool>();int reads=0;var current=services;
                    serviceRead=()=>{Interlocked.Increment(ref reads);started.SetResult(true);if(!gate.Wait(10000))throw new TimeoutException("Service snapshot test gate");return current;};
                    var pending=RefreshServices(true);await started.Task;await ServiceWatchTick();gate.Set();await pending;Assert(reads==1&&!servicesPending,"Service watch queued overlapping reads");
                }
                runningOnly.IsChecked=true;FilterServices();Assert(serviceList.Items.Count==0&&serviceList.SelectedItem==null,"Service filter kept a stopped service");runningOnly.IsChecked=false;FilterServices();serviceList.SelectedIndex=0;await ReadServiceSelection();
                serviceRead=()=>{throw new IOException("Тест недоступного источника");};await ServiceWatchTick();Assert(services==null&&serviceList.Items.Count==0&&selectedServiceSnapshot==null,"Failed service watch left stale service controls");
            }finally{serviceRead=read;serviceInspect=inspect;serviceLatest=latest;serviceWatch.IsChecked=true;Window.WindowState=state;serviceList.SelectedItem=null;}
            await RefreshServices();ShowPage(0);
        }
    }
}
