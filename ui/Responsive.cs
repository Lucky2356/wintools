using System;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private Grid settingsCards;
        private void InitializeResponsive(){
            var settings=Get<ScrollViewer>("SettingsPage");var original=(StackPanel)settings.Content;settingsCards=new Grid();settingsCards.ColumnDefinitions.Add(new ColumnDefinition());settingsCards.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<3;i++)settingsCards.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});while(original.Children.Count>0){var child=original.Children[0];original.Children.RemoveAt(0);settingsCards.Children.Add(child);}settings.Content=settingsCards;
            var area=SystemParameters.WorkArea;
            Window.MinWidth=Math.Min(800,area.Width);Window.MinHeight=Math.Min(560,area.Height);
            Window.Width=Math.Min(Window.Width,area.Width);Window.Height=Math.Min(Window.Height,area.Height);
            Window.SizeChanged+=(s,e)=>AdaptLayout();
            Window.Loaded+=(s,e)=>AdaptLayout();
        }
        private void AdaptLayout(){
            var body=Get<Grid>("Body");bool compact=Window.ActualWidth<1100||Window.ActualHeight<740;
            body.ColumnDefinitions[0].Width=new GridLength(compact?174:220);
            var sidebar=(Border)body.Children[0];sidebar.Padding=compact?new Thickness(6,12,6,8):new Thickness(18,26,18,18);
            string[] labels={"Каталог действий","История и откат","Подборки","Настройки","План изменений","Службы сейчас","Диагностика ПК","Сверить настройки","Ускорение Windows"};
            for(int i=0;i<nav.Length;i++){var button=Get<Button>(nav[i]);button.Content=labels[i];button.ToolTip=labels[i];button.FontSize=compact?12:14;button.Padding=compact?new Thickness(8):new Thickness(14,12,14,12);}
            var dock=(DockPanel)sidebar.Child;dock.Children[0].Visibility=compact?Visibility.Collapsed:Visibility.Visible;dock.Children[1].Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            var main=(Grid)body.Children[1];main.Margin=compact?new Thickness(12):new Thickness(24,22,24,14);
            if(collectionCards!=null)collectionCards.Columns=main.ActualWidth>=1500?3:main.ActualWidth>=1000?2:1;
            if(settingsCards!=null){bool wide=main.ActualWidth>=1000;settingsCards.ColumnDefinitions[0].Width=new GridLength(1,GridUnitType.Star);settingsCards.ColumnDefinitions[1].Width=wide?new GridLength(1,GridUnitType.Star):new GridLength(0);for(int i=0;i<settingsCards.Children.Count;i++){var card=(Border)settingsCards.Children[i];Grid.SetColumn(card,wide&&i>0?1:0);Grid.SetRow(card,wide?Math.Max(0,i-1):i);Grid.SetRowSpan(card,wide&&i==0?2:1);card.Padding=new Thickness(18);card.Margin=new Thickness(0,0,wide&&i==0?12:0,12);}}
            var header=(Grid)main.Children[0];header.Margin=new Thickness(0,0,0,compact?10:24);header.ColumnDefinitions[1].Width=new GridLength(compact?130:168);
            Get<TextBlock>("PageTitle").FontSize=compact?21:27;Visible("PageEyebrow",!compact);Visible("PageHint",!compact);
            var results=Get<Grid>("CatalogueResults");results.ColumnDefinitions[1].Width=new GridLength(compact?8:18);((Border)results.Children[1]).Padding=new Thickness(compact?10:18);
            var title=Get<TextBlock>("ActionTitle");title.FontSize=compact?16:20;((StackPanel)title.Parent).Children[0].Visibility=compact?Visibility.Collapsed:Visibility.Visible;
            Get<TextBlock>("Metadata").Margin=compact?new Thickness(0,4,0,6):new Thickness(0,8,0,12);
            ((Border)main.Children[2]).Margin=new Thickness(0,compact?4:16,0,0);
            Get<TextBox>("Output").Height=compact?28:100;
            Get<TextBox>("Output").Margin=compact?new Thickness(0):new Thickness(0,0,0,8);
            Get<TextBlock>("Status").Margin=new Thickness(2,compact?6:12,0,0);
        }
    }
}
