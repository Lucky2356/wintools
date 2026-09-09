using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Wintools {
    internal sealed partial class MainWindow {
        private readonly DispatcherTimer serviceTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        private CheckBox serviceWatch;
        private Func<ServiceState[]> serviceRead=ReadServiceSnapshot;
        private static ServiceState[] ReadServiceSnapshot(){
            var result=new List<ServiceState>();
            using(var searcher=new ManagementObjectSearcher("SELECT Name,DisplayName,State,StartMode FROM Win32_Service")){
                searcher.Options.Timeout=TimeSpan.FromSeconds(20);
                using(var rows=searcher.Get())foreach(ManagementObject row in rows)using(row)result.Add(new ServiceState{Name=Convert.ToString(row["Name"]),Label=Convert.ToString(row["DisplayName"]),State=Convert.ToString(row["State"]),Mode=Convert.ToString(row["StartMode"])});
            }
            return result.OrderBy(r=>r.Label).ToArray();
        }
        private void InitializeServiceWatching(Panel filters){
            serviceWatch=new CheckBox{Content="Автообновление · 5 с",IsChecked=true,Margin=new Thickness(12,6,0,6),ToolTip="Пока открыт раздел служб. При сворачивании и выполнении изменений обновление приостанавливается."};
            filters.Children.Add(serviceWatch);serviceWatch.Click+=(s,e)=>ServiceVisibility();
            serviceTimer.Tick+=async(s,e)=>await ServiceWatchTick();Window.StateChanged+=(s,e)=>ServiceVisibility();Window.Closed+=(s,e)=>serviceTimer.Stop();
        }
        private bool CanWatchServices(){return serviceWatch!=null&&serviceWatch.IsChecked==true&&page==5&&!closed&&!busy&&Window.WindowState!=WindowState.Minimized;}
        private void ServiceVisibility(){if(!smoke&&CanWatchServices())serviceTimer.Start();else serviceTimer.Stop();}
        private async Task ServiceWatchTick(){if(CanWatchServices()&&!readingServices)await RefreshServices(true);}
        private async Task RefreshServices(bool automatic=false){
            if(busy)return;if(readingServices){if(!automatic)servicesPending=true;return;}int epoch=serviceEpoch;readingServices=true;serviceRefresh.IsEnabled=false;Enabled("RefreshCatalogueServices",false);
            if(!automatic){Filter();serviceStatus.Text="Читаем установленные службы и способы их запуска…";}
            try{
                var reader=serviceRead;var snapshot=await Task.Run(()=>reader());if(closed||busy||epoch!=serviceEpoch)return;
                var prior=(services??new ServiceState[0]).ToDictionary(r=>r.Name,StringComparer.OrdinalIgnoreCase);
                services=snapshot.Select(row=>{ServiceState existing;if(!prior.TryGetValue(row.Name,out existing))return row;existing.Label=row.Label;existing.State=row.State;existing.Mode=row.Mode;return existing;}).ToArray();
                FilterServices(true);
                serviceStatus.Text="Обновлено в "+DateTime.Now.ToString("HH:mm:ss")+" · Всего: "+services.Length+" · Работают: "+services.Count(r=>r.State=="Running")+" · Автозапуск: "+services.Count(r=>r.Mode=="Auto")+". Работа и запуск Windows показаны отдельно.";
                if(!automatic)Filter();
                if(serviceList.SelectedItem!=null)await ReadServiceSelection(true);
            }catch(Exception ex){if(!closed&&!busy&&epoch==serviceEpoch){services=null;FilterServices();serviceStatus.Text="Не удалось прочитать службы. Их состояние неизвестно. "+ex.Message;Filter();}}
            finally{readingServices=false;if(!closed){serviceRefresh.IsEnabled=!busy;Enabled("RefreshCatalogueServices",!busy);if(!automatic)Filter();if(servicesPending&&!busy){servicesPending=false;var pendingRefresh=Window.Dispatcher.BeginInvoke(new Action(async()=>await RefreshServices()));}}}
        }
        private static ScrollViewer ServiceScroll(DependencyObject root){if(root is ScrollViewer)return (ScrollViewer)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=ServiceScroll(VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}return null;}
        private void FilterServices(bool preserveScroll=false){
            if(serviceList==null)return;var query=serviceSearch.Text.Trim();var selected=serviceList.SelectedItem as ServiceState;int mode=serviceMode==null?0:serviceMode.SelectedIndex;
            var next=(services??new ServiceState[0]).Where(r=>(mode==0||(mode==1&&r.Mode=="Auto")||(mode==2&&r.Mode=="Manual")||(mode==3&&r.Mode=="Disabled")||(mode==4&&catalogue.Any(t=>t.Kind=="SVC"&&string.Equals(t.Target,r.Name,StringComparison.OrdinalIgnoreCase))))&&(runningOnly.IsChecked!=true||r.State=="Running")&&(query.Length==0||r.Title.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0)).ToArray();
            if(serviceList.Items.Cast<ServiceState>().SequenceEqual(next))return;
            var scroll=preserveScroll?ServiceScroll(serviceList):null;double offset=scroll==null?0:scroll.VerticalOffset;
            serviceList.ItemsSource=next;if(selected!=null)serviceList.SelectedItem=next.FirstOrDefault(r=>string.Equals(r.Name,selected.Name,StringComparison.OrdinalIgnoreCase));
            if(scroll!=null){serviceList.UpdateLayout();scroll.ScrollToVerticalOffset(offset);}
        }
    }
}
