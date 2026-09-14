using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task CollectionAssistantSmoke(){
            bool wasBusy=busy;SetBusy(false);var before=collectionChoices.Select(c=>c.Checks.Select(check=>check.IsChecked).ToArray()).ToArray();var plan=preferences.Plan.ToArray();
            try{
                ShowPage(2);foreach(var choice in collectionChoices){OpenCollectionAssistant(choice);Assert(AssistantIds().Length==0,"Unanswered wizard enables actions");CloseCollectionAssistant();}
                var gaming=collectionChoices[2];OpenCollectionAssistant(gaming);assistantAnswers[3]=1;assistantAnswers[4]=1;assistantAnswers[6]=1;Assert(!AssistantIds().Any(id=>id.StartsWith("SVC-")),"Used Xbox/Bluetooth/printing proposed for disabling");assistantAnswers[4]=2;Assert(AssistantIds().Contains("SVC-BTHSERV")&&!AssistantIds().Contains("SVC-SPOOLER"),"Explicit Bluetooth opt-in mapping incorrect");CloseCollectionAssistant();Assert(gaming.Checks.Select(c=>c.IsChecked).SequenceEqual(before[2]),"Cancelling wizard changed collection");
                OpenCollectionAssistant(gaming);assistantAnswer.SelectedIndex=2;NextAssistant();Assert(assistantStep==1&&assistantAnswers[0]==2,"Wizard next lost answer");assistantBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert(assistantStep==0&&assistantAnswer.SelectedIndex==2,"Wizard back lost answer");assistantAnswers[1]=1;assistantAnswers[4]=1;while(assistantStep<assistantAnswers.Length)NextAssistant();Assert(assistantAnswer.Visibility==Visibility.Collapsed&&assistantChoice!=null,"Wizard skipped review");
                foreach(var size in new[]{new Size(1600,1000),new Size(800,600)}){Window.Width=size.Width;Window.Height=size.Height;Window.UpdateLayout();Get<ScrollViewer>("CollectionsPage").ScrollToTop();Assert(collectionAssistantGrid.ActualWidth<=Get<ScrollViewer>("CollectionsPage").ActualWidth,"Collection wizard overflows viewport");Capture(size.Width==800?"portable-ui-collection-assistant-compact.png":"portable-ui-collection-assistant.png");}
                var expected=AssistantIds();NextAssistant();Assert(assistantChoice==null&&CollectionIds(gaming).SequenceEqual(expected)&&preferences.Plan.SequenceEqual(plan),"Wizard changed plan or did not apply reviewed groups");Assert(collectionChoices.Length==3,"Wizard added a top-level collection");
            }finally{if(assistantChoice!=null)CloseCollectionAssistant();for(int i=0;i<collectionChoices.Length;i++)for(int j=0;j<collectionChoices[i].Checks.Count;j++)collectionChoices[i].Checks[j].IsChecked=before[i][j];RefreshCollectionCounts();SetBusy(wasBusy);}await Task.Delay(30);ShowPage(0);
        }
    }
}
