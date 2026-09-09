using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task WorkspaceSmoke(){
            Assert(collectionChoices.Length==3,"Unexpected top-level collections");Assert(collectionChoices.Select(c=>c.Sections.SelectMany(s=>s.Ids).Distinct().Count()).SequenceEqual(new[]{40,26,38}),"Expanded collection sizes changed");
            foreach(var choice in collectionChoices){for(int i=0;i<choice.Sections.Length;i++)Assert(choice.Checks[i].IsChecked==choice.Sections[i].Default,"Optional collection section enabled unexpectedly");}
            var crossVersion=new[]{"UI-FILEEXT","UI-COMPACT-VIEW","UI-FEEDS","UI-FILEEXT"};
            Assert(CollectionAvailableIds(crossVersion,19045).SequenceEqual(new[]{"UI-FILEEXT","UI-FEEDS"}),"Windows 10 collection includes Windows 11 actions");
            Assert(CollectionAvailableIds(crossVersion,26100).SequenceEqual(new[]{"UI-FILEEXT","UI-COMPACT-VIEW"}),"Windows 11 collection includes Windows 10 actions");
            Assert(CollectionAvailableIds(new[]{"UI-COMPACT-VIEW"},21999).Length==0&&CollectionAvailableIds(new[]{"UI-COMPACT-VIEW"},22000).Length==1,"Collection OS boundary differs from engine");
            foreach(var choice in collectionChoices)Assert(CollectionIds(choice).All(id=>catalogue.Any(t=>t.Id==id&&(t.Os=="any"||t.Os==(Environment.OSVersion.Version.Build>=22000?"win11":"win10")))),"Collection selection ignores host OS");
            var ids=ReadProfile("{\"Schema\":\"wintools/profile/1\",\"Actions\":[\"UI-FILEEXT\",\"UI-FILEEXT\"]}");Assert(ids.Length==1,"Profile duplicates not removed");
            foreach(var invalid in new[]{"{\"Actions\":[\"UI-FILEEXT\"]}","{\"Schema\":\"wintools/profile/1\",\"Actions\":[\"RUN-UNTRUSTED\"]}"}){bool rejected=false;try{ReadProfile(invalid);}catch(IOException){rejected=true;}Assert(rejected,"Unsafe profile accepted");}
            var previous=preferences.Plan.ToList();preferences.Plan.Clear();var choice0=collectionChoices[0];AddCollectionToPlan(choice0);Assert(preferences.Plan.Count==20,"Collection bulk addition failed");AddCollectionToPlan(choice0);Assert(preferences.Plan.Count==20,"Collection bulk addition duplicated actions");var lockPath=Path.Combine(Program.Data,"preferences.json.tmp");using(var locked=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){AddCollectionToPlan(collectionChoices[1]);Assert(preferences.Plan.Count==20,"Failed collection save mutated plan");}File.Delete(lockPath);preferences.Plan=previous;preferences.Save();RefreshPlan();
            Text("Status","Готово к работе");
            serviceMode.SelectedIndex=1;Assert(serviceList.Items.Cast<ServiceState>().All(s=>s.Mode=="Auto"),"Service startup filter is incorrect");serviceMode.SelectedIndex=3;Assert(serviceList.Items.Cast<ServiceState>().All(s=>s.Mode=="Disabled"),"Disabled startup filter is incorrect");serviceMode.SelectedIndex=0;
            double smallHeight=0;foreach(var size in new[]{new Size(800,600),new Size(1280,800),new Size(2560,1440)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();await Task.Delay(100);AdaptLayout();
                foreach(int index in new[]{2,3,5}){ShowPage(index);Window.UpdateLayout();await Task.Delay(80);if(index==5){if(size.Width==800)smallHeight=serviceList.ActualHeight;else if(size.Width==2560 && Window.ActualHeight>=1000)Assert(serviceList.ActualHeight>smallHeight+300,"Service list does not grow with window");}if(size.Width==2560){var main=(Grid)Get<Grid>("Body").Children[1];if(index==2){Assert(collectionCards.Columns==(main.ActualWidth>=1500?3:main.ActualWidth>=1000?2:1)&&collectionCards.ActualWidth>main.ActualWidth*0.9,"Collections wasted wide viewport");}if(index==3)Assert(settingsCards.ActualWidth>main.ActualWidth*0.9,"Settings wasted wide viewport");Capture("portable-ui-wide-"+index+".png");}}
            }Window.Width=1220;Window.Height=820;ShowPage(0);
        }
    }
}
