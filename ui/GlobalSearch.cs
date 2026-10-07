using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wintools {
    internal sealed class SearchHit {
        public string Title {get;set;}
        public string Detail {get;set;}
        internal Action Open;
    }
    internal sealed partial class MainWindow {
        private Grid searchOverlay;
        private TextBox searchQuery;
        private ListBox searchResults;
        private void InitializeGlobalSearch(){
            var root=(Grid)Get<Grid>("ConfirmOverlay").Parent;searchOverlay=new Grid{Visibility=Visibility.Collapsed};searchOverlay.SetResourceReference(Panel.BackgroundProperty,"Scrim");root.Children.Insert(root.Children.IndexOf(Get<Grid>("ConfirmOverlay")),searchOverlay);
            searchOverlay.MouseDown+=(s,e)=>{if(e.OriginalSource==searchOverlay)CloseGlobalSearch();};
            var panel=new StackPanel();var card=new Border{Child=panel,MaxWidth=640,Margin=new Thickness(24,60,24,24),Padding=new Thickness(18),CornerRadius=new CornerRadius(14),VerticalAlignment=VerticalAlignment.Top};Card(card);searchOverlay.Children.Add(card);
            var title=Paragraph("Поиск везде");title.FontSize=18;title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,8);panel.Children.Add(title);
            searchQuery=new TextBox{Height=40,Margin=new Thickness(0,0,0,10)};System.Windows.Automation.AutomationProperties.SetName(searchQuery,"Поиск по разделам, настройкам, службам и программам");panel.Children.Add(searchQuery);
            searchResults=new ListBox{MaxHeight=420};System.Windows.Automation.AutomationProperties.SetName(searchResults,"Результаты поиска");searchResults.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel><TextBlock Text='{Binding Title}' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Detail}' Foreground='{DynamicResource Muted}' FontSize='12' TextTrimming='CharacterEllipsis'/></StackPanel></DataTemplate>");panel.Children.Add(searchResults);
            var hint=Paragraph("↑↓ — выбор, Enter — открыть, Esc — закрыть. Ищет по разделам, настройкам каталога, службам, установленным программам и программам для установки.");hint.FontSize=12;hint.Margin=new Thickness(0,8,0,0);hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(hint);
            searchQuery.TextChanged+=(s,e)=>{searchResults.ItemsSource=SearchEverywhere(searchQuery.Text);searchResults.SelectedIndex=searchResults.Items.Count>0?0:-1;};
            searchQuery.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Down&&searchResults.Items.Count>0){searchResults.SelectedIndex=Math.Min(searchResults.Items.Count-1,searchResults.SelectedIndex+1);searchResults.ScrollIntoView(searchResults.SelectedItem);e.Handled=true;}else if(e.Key==Key.Up&&searchResults.Items.Count>0){searchResults.SelectedIndex=Math.Max(0,searchResults.SelectedIndex-1);searchResults.ScrollIntoView(searchResults.SelectedItem);e.Handled=true;}else if(e.Key==Key.Enter){OpenSearchHit(searchResults.SelectedItem as SearchHit);e.Handled=true;}};
            searchResults.MouseDoubleClick+=(s,e)=>OpenSearchHit(searchResults.SelectedItem as SearchHit);searchResults.KeyDown+=(s,e)=>{if(e.Key==Key.Enter){OpenSearchHit(searchResults.SelectedItem as SearchHit);e.Handled=true;}};
            Window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.K&&(Keyboard.Modifiers&ModifierKeys.Control)!=0&&confirmation==null){OpenGlobalSearch();e.Handled=true;}else if(e.Key==Key.Escape&&searchOverlay.Visibility==Visibility.Visible&&confirmation==null){CloseGlobalSearch();e.Handled=true;}};
        }
        private void OpenGlobalSearch(){searchOverlay.Visibility=Visibility.Visible;searchQuery.Clear();searchResults.ItemsSource=SearchEverywhere("");searchQuery.Focus();Keyboard.Focus(searchQuery);}
        private void CloseGlobalSearch(){searchOverlay.Visibility=Visibility.Collapsed;}
        private void OpenSearchHit(SearchHit hit){if(hit==null)return;CloseGlobalSearch();hit.Open();}
        private static bool Matches(string query,params string[] fields){var text=string.Join(" ",fields.Where(f=>f!=null));return query.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).All(word=>text.IndexOf(word,StringComparison.CurrentCultureIgnoreCase)>=0);}
        // Results come from data already in memory; nothing is read from Windows while typing.
        internal SearchHit[] SearchEverywhere(string text){
            var query=(text??"").Trim();var hits=new List<SearchHit>();
            for(int i=0;i<PageTitles.Length;i++){int index=i;var label=Get<Button>(nav[i]).Content as string??PageTitles[i];if(Matches(query,label,PageTitles[i]))hits.Add(new SearchHit{Title=label,Detail="Раздел · "+PageTitles[i],Open=()=>ShowPage(index)});}
            if(query.Length<2)return hits.ToArray();
            foreach(var item in catalogue.Where(t=>Matches(query,t.Title,t.Id,t.Description)).Take(15)){var id=item.Id;hits.Add(new SearchHit{Title=item.Title,Detail="Настройка · "+Catalogue.Categories[item.Category]+" · "+Risk(item),Open=()=>{ShowPage(0);showAll=true;Get<ComboBox>("Category").SelectedIndex=0;Get<TextBox>("Search").Text=id;}});}
            foreach(var service in (services??new ServiceState[0]).Where(s=>Matches(query,s.Label,s.Name)).Take(8)){var name=service.Name;hits.Add(new SearchHit{Title=service.Title,Detail="Служба · "+service.RunningLabel,Open=()=>{ShowPage(5);if(serviceSearch!=null)serviceSearch.Text=name;}});}
            foreach(var application in installedApplications.Where(a=>Matches(query,a.Name,a.Publisher)).Take(8)){var name=application.Name;hits.Add(new SearchHit{Title=application.Name,Detail="Установленная программа · "+application.Publisher,Open=()=>{ShowPage(9);if(applicationSearch!=null)applicationSearch.Text=name;}});}
            foreach(var package in Packages.Catalog.Where(p=>Matches(query,p.Name,p.Id,p.Description))){var id=package.Id;hits.Add(new SearchHit{Title=package.Name,Detail="Установка программ · "+package.Group,Open=()=>{ShowPage(14);CheckBox check;if(packageChecks.TryGetValue(id,out check)){check.IsChecked=true;RefreshPackagesEnabled();check.BringIntoView();}}});}
            return hits.Take(40).ToArray();
        }
    }
}
