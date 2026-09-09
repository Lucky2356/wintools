using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace Wintools {
    internal sealed partial class MainWindow {
        private ComboBox serviceMode;
        private Button serviceAction;
        private TextBlock serviceSelection;
        private void InitializeServiceBrowser(){
            var pageRoot=new Grid{Visibility=Visibility.Collapsed};Window.RegisterName("ServicesPage",pageRoot);((Grid)Get<FrameworkElement>("SettingsPage").Parent).Children.Add(pageRoot);
            foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})pageRoot.RowDefinitions.Add(new RowDefinition{Height=height});
            serviceStatus=Paragraph("Текущее состояние и способ запуска — разные свойства службы. Чтение ничего не отключает.");pageRoot.Children.Add(serviceStatus);
            var searchRow=new Grid{Margin=new Thickness(0,0,0,10)};searchRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});searchRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetRow(searchRow,1);pageRoot.Children.Add(searchRow);
            var searchBox=new Grid{Margin=new Thickness(0,0,12,0)};serviceSearch=new TextBox{ToolTip="Поиск по названию или системному имени службы"};System.Windows.Automation.AutomationProperties.SetName(serviceSearch,"Поиск служб");searchBox.Children.Add(serviceSearch);var hint=new TextBlock{Text="Поиск службы по названию или имени",IsHitTestVisible=false,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");searchBox.Children.Add(hint);serviceSearch.TextChanged+=(s,e)=>{hint.Visibility=serviceSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;FilterServices();};searchRow.Children.Add(searchBox);
            serviceRefresh=new Button{Content="↻ Обновить",Padding=new Thickness(14,8,14,8)};serviceRefresh.Click+=async(s,e)=>await RefreshServices();Grid.SetColumn(serviceRefresh,1);searchRow.Children.Add(serviceRefresh);
            var filters=new WrapPanel{Margin=new Thickness(0,0,0,10)};Grid.SetRow(filters,2);pageRoot.Children.Add(filters);runningOnly=new CheckBox{Content="Только работающие",Margin=new Thickness(0,0,16,0)};runningOnly.Click+=(s,e)=>FilterServices();filters.Children.Add(runningOnly);
            serviceMode=new ComboBox{Width=205,ItemsSource=new[]{"Все способы запуска","Автоматический запуск","Ручной запуск","Запуск отключён","Есть действие в каталоге"},SelectedIndex=0};System.Windows.Automation.AutomationProperties.SetName(serviceMode,"Фильтр служб");serviceMode.SelectionChanged+=(s,e)=>FilterServices();filters.Children.Add(serviceMode);
            serviceList=new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch};Grid.SetRow(serviceList,3);pageRoot.Children.Add(serviceList);VirtualizingPanel.SetIsVirtualizing(serviceList,true);VirtualizingPanel.SetVirtualizationMode(serviceList,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(serviceList,true);
            var rowStyle=new Style(typeof(ListBoxItem),(Style)Window.FindResource(typeof(ListBoxItem)));rowStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));rowStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,0,4)));serviceList.ItemContainerStyle=rowStyle;
            serviceList.ItemTemplate=(DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='120'/><ColumnDefinition Width='140'/></Grid.ColumnDefinitions><StackPanel Margin='0,0,12,0'><TextBlock Text='{Binding Label}' FontWeight='SemiBold' TextWrapping='Wrap'/><TextBlock Text='{Binding Name}' FontSize='12' Foreground='{DynamicResource Muted}' Margin='0,3,0,0'/></StackPanel><StackPanel Grid.Column='1'><TextBlock Text='Сейчас' FontSize='11' Foreground='{DynamicResource Muted}'/><TextBlock Text='{Binding RunningLabel}' TextWrapping='Wrap' Margin='0,3,6,0'/></StackPanel><StackPanel Grid.Column='2'><TextBlock Text='Запуск Windows' FontSize='11' Foreground='{DynamicResource Muted}'/><TextBlock Text='{Binding StartLabel}' TextWrapping='Wrap' Margin='0,3,0,0'/></StackPanel></Grid></DataTemplate>");
            var footer=new Grid{Margin=new Thickness(0,10,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetRow(footer,4);pageRoot.Children.Add(footer);serviceSelection=Paragraph("Выберите службу для описания и доступного действия.");serviceSelection.Margin=new Thickness(0,0,12,0);serviceSelection.FontSize=12;footer.Children.Add(serviceSelection);serviceAction=new Button{Content="Описание и действие →",IsEnabled=false};Grid.SetColumn(serviceAction,1);footer.Children.Add(serviceAction);
            serviceList.SelectionChanged+=(s,e)=>{var state=serviceList.SelectedItem as ServiceState;var tweak=state==null?null:catalogue.FirstOrDefault(t=>t.Kind=="SVC"&&string.Equals(t.Target,state.Name,StringComparison.OrdinalIgnoreCase));serviceAction.IsEnabled=tweak!=null;serviceSelection.Text=state==null?"Выберите службу для описания и доступного действия.":tweak==null?state.Title+" · Доступен только просмотр; действия в каталоге нет.":tweak.Caveat;};
            serviceAction.Click+=(s,e)=>{var state=serviceList.SelectedItem as ServiceState;if(state==null)return;var tweak=catalogue.FirstOrDefault(t=>t.Kind=="SVC"&&string.Equals(t.Target,state.Name,StringComparison.OrdinalIgnoreCase));if(tweak==null)return;ChooseCollection(new[]{tweak.Id});Get<CheckBox>("Risky").IsChecked=tweak.Risk=="high";Filter();};
        }
    }
}
