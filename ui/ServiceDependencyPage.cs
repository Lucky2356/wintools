using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private Grid serviceBody,serviceManagementFooter;
        private CheckBox serviceDependencyToggle;
        private ScrollViewer serviceDependencyView;
        private StackPanel serviceDependencyContent;
        private TextBlock serviceDependencyStatus;
        private bool readingDependencies;
        private int dependencyEpoch;
        private string dependencySignature;
        private Func<string,Task<ServiceDependencySnapshot>> dependencyRead=name=>Task.Run(()=>ServiceDependencies.Read(name));
        private void InitializeServiceDependencies(Grid root,Panel filters,Grid footer){
            serviceManagementFooter=footer;
            root.Children.Remove(serviceList);serviceBody=new Grid();serviceBody.ColumnDefinitions.Add(new ColumnDefinition());serviceBody.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});Grid.SetRow(serviceBody,3);root.Children.Add(serviceBody);Grid.SetRow(serviceList,0);serviceBody.Children.Add(serviceList);
            serviceDependencyToggle=new CheckBox{Content="Связи выбранной службы",Margin=new Thickness(12,6,0,6)};filters.Children.Add(serviceDependencyToggle);
            var panel=new StackPanel();var back=new Button{Content="← К списку служб",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8)};panel.Children.Add(back);back.Click+=(s,e)=>{serviceDependencyToggle.IsChecked=false;LayoutServiceDependencies();};serviceDependencyStatus=Paragraph("Выберите службу в списке.");panel.Children.Add(serviceDependencyStatus);serviceDependencyContent=new StackPanel();panel.Children.Add(serviceDependencyContent);serviceDependencyView=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Visibility=Visibility.Collapsed,Padding=new Thickness(12,0,0,0)};Grid.SetColumn(serviceDependencyView,1);serviceBody.Children.Add(serviceDependencyView);
            serviceDependencyToggle.Click+=async(s,e)=>{LayoutServiceDependencies();await ReadServiceDependencies();};serviceBody.SizeChanged+=(s,e)=>LayoutServiceDependencies();serviceList.SelectionChanged+=async(s,e)=>{dependencySignature=null;serviceDependencyContent.Children.Clear();await ReadServiceDependencies();};
        }
        private void LayoutServiceDependencies(){
            if(serviceBody==null)return;bool show=serviceDependencyToggle.IsChecked==true;bool split=show&&serviceBody.ActualWidth>=1050;serviceBody.ColumnDefinitions[0].Width=new GridLength(show&&!split?0:1,show&&!split?GridUnitType.Pixel:GridUnitType.Star);serviceBody.ColumnDefinitions[1].Width=new GridLength(show?1:0,show?GridUnitType.Star:GridUnitType.Pixel);serviceList.Visibility=show&&!split?Visibility.Collapsed:Visibility.Visible;serviceManagementFooter.Visibility=show&&!split?Visibility.Collapsed:Visibility.Visible;serviceDependencyView.Visibility=show?Visibility.Visible:Visibility.Collapsed;serviceDependencyView.Padding=new Thickness(split?12:0,0,0,0);
        }
        private async Task ReadServiceDependencies(){
            dependencyEpoch++;if(serviceDependencyToggle==null||serviceDependencyToggle.IsChecked!=true)return;var row=serviceList.SelectedItem as ServiceState;if(row==null){serviceDependencyContent.Children.Clear();serviceDependencyStatus.Text="Выберите службу в списке. Кнопка выше возвращает список.";return;}if(readingDependencies||busy||closed)return;readingDependencies=true;
            try{while(!closed&&!busy&&serviceDependencyToggle.IsChecked==true){row=serviceList.SelectedItem as ServiceState;if(row==null)return;int epoch=dependencyEpoch;serviceDependencyStatus.Text="Читаем связи «"+row.Label+"»…";
                    try{var snapshot=await dependencyRead(row.Name);if(epoch!=dependencyEpoch)continue;if(closed||busy||serviceDependencyToggle.IsChecked!=true)return;if(snapshot==null||snapshot.Root==null||!string.Equals(snapshot.Root.Name,row.Name,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Ответ не соответствует выбранной службе.");RenderServiceDependencies(snapshot);}
                    catch(Exception ex){if(epoch!=dependencyEpoch)continue;dependencySignature=null;serviceDependencyContent.Children.Clear();serviceDependencyStatus.Text="Связи недоступны: "+ex.Message;}return;}
            }finally{readingDependencies=false;}
        }
        private void RenderServiceDependencies(ServiceDependencySnapshot snapshot){
            serviceDependencyStatus.Text="Снимок связей · "+DateTime.Now.ToString("HH:mm:ss")+" · Нажмите связанную службу для перехода.";
            string signature=snapshot.Root.Description+string.Join("|",snapshot.Requires.Select(n=>n.Description))+string.Join("|",snapshot.Groups)+string.Join("|",snapshot.Dependents.Select(n=>n.Description))+snapshot.RequiredError+snapshot.DependentError;if(signature==dependencySignature)return;dependencySignature=signature;serviceDependencyContent.Children.Clear();serviceDependencyView.ScrollToTop();
            var upper=Paragraph("Для её запуска нужны ↓");upper.FontWeight=FontWeights.SemiBold;serviceDependencyContent.Children.Add(upper);if(snapshot.RequiredError.Length>0)serviceDependencyContent.Children.Add(Paragraph("Список зависимостей неполон: "+snapshot.RequiredError));
            foreach(var node in snapshot.Requires.OrderBy(n=>n.Label))DependencyNode(node,false);
            foreach(var group in snapshot.Groups)serviceDependencyContent.Children.Add(Paragraph("Группа загрузки: "+group+"\nWindows пытается запустить участников группы; достаточно хотя бы одного работающего участника."));
            if(snapshot.Requires.Length==0&&snapshot.Groups.Length==0&&snapshot.RequiredError.Length==0)serviceDependencyContent.Children.Add(Paragraph("Зависимости в конфигурации Windows не указаны."));
            DependencyNode(snapshot.Root,true);var lower=Paragraph("От неё зависят ↓");lower.FontWeight=FontWeights.SemiBold;serviceDependencyContent.Children.Add(lower);if(snapshot.DependentError.Length>0)serviceDependencyContent.Children.Add(Paragraph("Список зависимых служб неполон: "+snapshot.DependentError));foreach(var node in snapshot.Dependents.OrderBy(n=>n.Label))DependencyNode(node,false);
            if(snapshot.Dependents.Length==0&&snapshot.DependentError.Length==0)serviceDependencyContent.Children.Add(Paragraph("Зависимые службы Windows не указаны."));serviceDependencyContent.Children.Add(Paragraph("Это зарегистрированные связи служб, а не полный список программ, использующих их. Остановка может нарушить их работу. Wintools не останавливает зависимые службы автоматически."));
        }
        private void DependencyNode(ServiceDependencyNode node,bool selected){
            var text=Paragraph((selected?"Выбранная служба\n":"")+node.Description);text.Margin=new Thickness(0);text.FontSize=12;var button=new Button{Content=text,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(10),Margin=new Thickness(0,0,0,8),Tag=node.Name};bool exists=!node.Driver&&(services??new ServiceState[0]).Any(s=>string.Equals(s.Name,node.Name,StringComparison.OrdinalIgnoreCase));button.IsEnabled=!selected&&exists;button.ToolTip=selected?"Выбранная служба":exists?"Перейти к службе":node.Driver?"Драйвер показан для сведения; управление драйверами здесь недоступно.":"Служба не найдена в текущем списке. Обновите службы.";if(selected||!exists){button.Content=null;var border=new Border{Child=text,Padding=new Thickness(12),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,6,0,12)};border.ToolTip=button.ToolTip;border.SetResourceReference(Border.BackgroundProperty,selected?"Selection":"Surface");serviceDependencyContent.Children.Add(border);}else{button.Click+=(s,e)=>NavigateDependency(node.Name);serviceDependencyContent.Children.Add(button);}
        }
        private void NavigateDependency(string name){var row=(services??new ServiceState[0]).FirstOrDefault(s=>string.Equals(s.Name,name,StringComparison.OrdinalIgnoreCase));if(row==null){serviceDependencyStatus.Text="Служба больше не найдена. Обновите список.";return;}serviceSearch.Clear();runningOnly.IsChecked=false;serviceMode.SelectedIndex=0;FilterServices();serviceList.SelectedItem=row;serviceList.ScrollIntoView(row);}
    }
}
