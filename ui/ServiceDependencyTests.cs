using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task ServiceDependencySmoke(){
            var parsed=ServiceDependencies.ParseNames("Parent\0+Group\0parent\0\0".ToCharArray());Assert(parsed.Length==2&&parsed.Contains("+Group"),"Group dependency expanded or duplicates retained");bool rejected=false;try{ServiceDependencies.ParseNames("unterminated".ToCharArray());}catch(IOException){rejected=true;}Assert(rejected,"Malformed dependency buffer accepted");
            var native=await Task.Run(()=>ServiceDependencies.Read("EventLog"));Assert(native.Root.Name=="EventLog"&&native.RequiredError.Length==0&&native.DependentError.Length==0,"Native dependency query failed");
            var original=dependencyRead;var inspect=serviceInspect;var latest=serviceLatest;
            try{
                serviceInspect=name=>new ServiceSnapshot{Name=name,Label=name,Description="Тест связи",Mode="Manual",State="Running",Kind="Own Process"};serviceLatest=name=>null;services=new[]{new ServiceState{Name="Parent",Label="Служба подключения",State="Running",Mode="Auto"},new ServiceState{Name="Child",Label="Служба приложения",State="Running",Mode="Manual"}};serviceSearch.Clear();runningOnly.IsChecked=false;serviceMode.SelectedIndex=0;FilterServices();serviceList.SelectedItem=services[0];ShowPage(5);
                dependencyRead=name=>Task.FromResult(new ServiceDependencySnapshot{Root=new ServiceDependencyNode{Name=name,Label=name=="Parent"?"Служба подключения":"Служба приложения",State="Running"},Requires=name=="Child"?new[]{new ServiceDependencyNode{Name="Parent",Label="Служба подключения",State="Running"}}:new ServiceDependencyNode[0],Dependents=name=="Parent"?new[]{new ServiceDependencyNode{Name="Child",Label="Служба приложения",State="Running"}}:new ServiceDependencyNode[0],Groups=new[]{"Тестовая группа"}});
                serviceDependencyToggle.IsChecked=true;LayoutServiceDependencies();await ReadServiceDependencies();Assert(serviceDependencyContent.Children.OfType<Button>().Count()==1,"Dependency navigation node missing");serviceDependencyContent.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await ReadServiceDependencies();while(readingDependencies)await Task.Delay(10);Assert(((ServiceState)serviceList.SelectedItem).Name=="Child","Dependency click did not navigate");
                foreach(var size in new[]{new Size(1600,1000),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();LayoutServiceDependencies();Window.UpdateLayout();Assert(serviceDependencyView.ActualHeight>200&&serviceDependencyView.ActualWidth>250,"Dependency view has no usable area");Assert(serviceList.Visibility==(serviceBody.ActualWidth<1050?Visibility.Collapsed:Visibility.Visible),"Dependency layout does not adapt to width");Capture(size.Width==800?"portable-ui-dependencies-compact.png":"portable-ui-dependencies.png");}
                var pending=new TaskCompletionSource<ServiceDependencySnapshot>();int reads=0;dependencyRead=name=>{reads++;return reads==1?pending.Task:Task.FromResult(new ServiceDependencySnapshot{Root=new ServiceDependencyNode{Name=name,Label=name,State="Stopped"},RequiredError="fixture unavailable"});};var loading=ReadServiceDependencies();NavigateDependency("Parent");pending.SetResult(new ServiceDependencySnapshot{Root=new ServiceDependencyNode{Name="Child",Label="Stale child",State="Running"}});await loading;Assert(reads==2&&!dependencySignature.Contains("Stale child")&&dependencySignature.Contains("fixture unavailable"),"Stale dependency result replaced new selection or partial error lost");
                dependencyRead=name=>{throw new IOException("fixture query failure");};await ReadServiceDependencies();Assert(serviceDependencyContent.Children.Count==0&&serviceDependencyStatus.Text.Contains("fixture query failure"),"Dependency failure leaves stale graph as current");
            }finally{dependencyRead=original;serviceInspect=inspect;serviceLatest=latest;serviceDependencyToggle.IsChecked=false;LayoutServiceDependencies();serviceList.SelectedItem=null;}await RefreshServices();ShowPage(0);
        }
    }
}
