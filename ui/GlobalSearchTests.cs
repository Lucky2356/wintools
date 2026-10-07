using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Wintools
{
    internal sealed partial class MainWindow
    {
        private async Task GlobalSearchSmoke()
        {
            Assert(SearchEverywhere("").Length == PageTitles.Length, "Empty search should list sections");
            var hits = SearchEverywhere("расширения файлов");
            Assert(hits.Any(h => h.Detail.StartsWith("Настройка") && h.Title.Contains("расширения")), "Catalogue setting not found");
            Assert(SearchEverywhere("7-zip").Any(h => h.Detail.StartsWith("Установка программ")), "Installable program not found");
            Assert(SearchEverywhere("dns").Any(h => h.Detail.StartsWith("Раздел")), "Section not found by name");
            Assert(SearchEverywhere("zzzz-не-существует").Length == 0, "Unrelated query returned results");
            OpenGlobalSearch();
            Assert(searchOverlay.Visibility == Visibility.Visible, "Search overlay not shown");
            searchQuery.Text = "расширения файлов";
            Assert(searchResults.SelectedIndex == 0, "First result not preselected");
            var target = (SearchHit)searchResults.Items.Cast<SearchHit>().First(h => h.Detail.StartsWith("Настройка"));
            searchResults.SelectedItem = target;
            Window.UpdateLayout();
            await Task.Delay(50);
            Capture("portable-ui-search.png");
            OpenSearchHit(target);
            Assert(searchOverlay.Visibility == Visibility.Collapsed && page == 0 && Get<ListBox>("Items").Items.Count == 1 && Selected().Title == target.Title, "Search result did not open the setting");
            OpenGlobalSearch();
            searchQuery.Text = "7-zip";
            OpenSearchHit(searchResults.Items.Cast<SearchHit>().First(h => h.Detail.StartsWith("Установка программ")));
            Assert(page == 14 && packageChecks["7zip.7zip"].IsChecked == true, "Search did not select the program");
            packageChecks["7zip.7zip"].IsChecked = false;
            OpenGlobalSearch();
            CloseGlobalSearch();
            Assert(searchOverlay.Visibility == Visibility.Collapsed, "Search overlay did not close");
            Get<TextBox>("Search").Clear();
            showAll = false;
            Filter();
            ShowPage(0);
        }
    }
}
