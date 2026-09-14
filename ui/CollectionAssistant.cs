using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private StackPanel collectionAssistant;
        private FrameworkElement collectionIntro;
        private Grid collectionAssistantGrid;
        private TextBlock assistantTitle,assistantQuestion,assistantDetail,assistantSummary;
        private ComboBox assistantAnswer;
        private Button assistantBack,assistantNext;
        private CollectionChoice assistantChoice;
        private int assistantStep,assistantIndex;
        private int[] assistantAnswers;
        private bool renderingAssistant;
        private static readonly string[][] CollectionQuestions={
            new[]{"Хотите убрать рекламу и рекомендации Windows?","Нужна персонализация текста и рукописного ввода?","Пользуетесь историей активности или облачным поиском Windows?","Хотите ограничить диагностические службы и задачи? На рабочем ПК сначала согласуйте это с администратором.","Пользуетесь Recall или Copilot?"},
            new[]{"Хотите видеть расширения файлов и открывать Проводник со списком дисков?","Нужны списки недавних файлов и часто открываемых папок?","Хотите видеть скрытые файлы и классическое контекстное меню?","Хотите тёмное оформление и дополнительные настройки панели задач?","Пользуетесь сетевыми медиатеками или обнаружением устройств в сети?","Печатаете документы, сохраняете их через печать в PDF или пользуетесь сканером?"},
            new[]{"Пользуетесь встроенной записью игр Windows?","Хотите убрать виджеты, новости, анимацию панели задач и предложения Windows?","Нужна хотя бы одна функция: офлайн-карты, факс, медиатека, NFC, Insider или удалённая установка приложений?","Пользуетесь Xbox, Game Pass, облачными сохранениями или аксессуарами Xbox?","Пользуетесь Bluetooth: наушниками, мышью, клавиатурой или контроллером?","Подключаетесь к этому ПК через удалённый рабочий стол (RDP)?","Печатаете документы, сохраняете их через печать в PDF или пользуетесь сканером?"}
        };
        private static readonly bool[][] CollectionSelectOnYes={new[]{true,false,false,true,false},new[]{true,false,true,true,false,false},new[]{false,true,false,false,false,false,false}};
        private void InitializeCollectionAssistant(StackPanel root){
            collectionIntro=(FrameworkElement)root.Children[0];collectionAssistant=new StackPanel{Visibility=Visibility.Collapsed};root.Children.Add(collectionAssistant);assistantTitle=Paragraph("");assistantTitle.FontSize=21;assistantTitle.FontWeight=FontWeights.SemiBold;collectionAssistant.Children.Add(assistantTitle);collectionAssistant.Children.Add(Paragraph("Ответы только отметят группы внутри выбранной подборки. Они не меняют Windows и не добавляют действия в план. Перед применением вы сможете проверить каждое действие."));
            collectionAssistantGrid=new Grid();collectionAssistantGrid.ColumnDefinitions.Add(new ColumnDefinition());collectionAssistantGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});collectionAssistantGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});collectionAssistantGrid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});collectionAssistant.Children.Add(collectionAssistantGrid);
            var questionPanel=new StackPanel{Margin=new Thickness(0,0,16,16)};collectionAssistantGrid.Children.Add(questionPanel);assistantQuestion=Paragraph("");assistantQuestion.FontSize=18;assistantQuestion.FontWeight=FontWeights.SemiBold;questionPanel.Children.Add(assistantQuestion);assistantDetail=Paragraph("");questionPanel.Children.Add(assistantDetail);assistantAnswer=new ComboBox{Width=290,HorizontalAlignment=HorizontalAlignment.Left,ItemsSource=new[]{"Не уверен — не добавлять","Да","Нет"},SelectedIndex=0,Margin=new Thickness(0,0,0,12)};System.Windows.Automation.AutomationProperties.SetName(assistantAnswer,"Ответ на вопрос подборки");questionPanel.Children.Add(assistantAnswer);assistantAnswer.SelectionChanged+=(s,e)=>{if(renderingAssistant||assistantChoice==null||assistantStep>=assistantAnswers.Length)return;assistantAnswers[assistantStep]=assistantAnswer.SelectedIndex;RenderAssistantSummary();};
            var controls=new WrapPanel();questionPanel.Children.Add(controls);assistantBack=new Button{Content="← Назад",Margin=new Thickness(0,0,8,8)};controls.Children.Add(assistantBack);assistantBack.Click+=(s,e)=>{if(assistantStep>0){assistantStep--;RenderAssistant();Get<ScrollViewer>("CollectionsPage").ScrollToTop();}};assistantNext=new Button{Content="Далее →",Margin=new Thickness(0,0,8,8)};controls.Children.Add(assistantNext);assistantNext.Click+=(s,e)=>NextAssistant();var cancel=new Button{Content="Отмена",Margin=new Thickness(0,0,0,8)};controls.Children.Add(cancel);cancel.Click+=(s,e)=>CloseCollectionAssistant();
            assistantSummary=Paragraph("");assistantSummary.Margin=new Thickness(0);var summaryCard=new Border{Child=assistantSummary,Padding=new Thickness(16),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,0,0,16),VerticalAlignment=VerticalAlignment.Top};summaryCard.SetResourceReference(Border.BackgroundProperty,"Surface");Grid.SetRow(summaryCard,1);collectionAssistantGrid.Children.Add(summaryCard);
            collectionAssistantGrid.SizeChanged+=(s,e)=>{bool wide=collectionAssistantGrid.ActualWidth>=950;collectionAssistantGrid.ColumnDefinitions[1].Width=new GridLength(wide?1:0,wide?GridUnitType.Star:GridUnitType.Pixel);Grid.SetColumn(summaryCard,wide?1:0);Grid.SetRow(summaryCard,wide?0:1);};
        }
        private void OpenCollectionAssistant(CollectionChoice choice){
            if(busy)return;assistantIndex=Array.IndexOf(collectionChoices,choice);if(assistantIndex<0||CollectionQuestions[assistantIndex].Length!=choice.Sections.Length||CollectionSelectOnYes[assistantIndex].Length!=choice.Sections.Length)throw new InvalidOperationException("Вопросы не соответствуют подборке.");assistantChoice=choice;assistantAnswers=new int[choice.Sections.Length];assistantStep=0;collectionIntro.Visibility=Visibility.Collapsed;collectionCards.Visibility=Visibility.Collapsed;collectionAssistant.Visibility=Visibility.Visible;RenderAssistant();Get<ScrollViewer>("CollectionsPage").ScrollToTop();
        }
        private bool AssistantSelected(int index){return assistantAnswers[index]==(CollectionSelectOnYes[assistantIndex][index]?1:2)&&CollectionAvailableIds(assistantChoice.Sections[index].Ids,Environment.OSVersion.Version.Build).Length>0;}
        private string[] AssistantIds(){return CollectionAvailableIds(assistantChoice.Sections.Where((s,i)=>AssistantSelected(i)).SelectMany(s=>s.Ids),Environment.OSVersion.Version.Build);}
        private void RenderAssistant(){
            if(assistantChoice==null)return;renderingAssistant=true;bool review=assistantStep==assistantAnswers.Length;assistantTitle.Text=assistantChoice.Title+" · "+(review?"Проверка выбора":"Вопрос "+(assistantStep+1)+" из "+assistantAnswers.Length);assistantQuestion.Text=review?"Проверьте предложенные группы":CollectionQuestions[assistantIndex][assistantStep];assistantDetail.Text=review?"Кнопка ниже отметит эти группы в подборке и заменит её прежний выбор. План и настройки Windows останутся прежними. Если нужная функция попала в отключения, вернитесь назад и измените ответ.":assistantChoice.Sections[assistantStep].Detail;assistantAnswer.Visibility=review?Visibility.Collapsed:Visibility.Visible;if(!review)assistantAnswer.SelectedIndex=assistantAnswers[assistantStep];assistantNext.Content=review?"Отметить группы в подборке":"Далее →";renderingAssistant=false;RefreshCollectionAssistant();RenderAssistantSummary();
        }
        private void RefreshCollectionAssistant(){if(assistantNext==null)return;assistantNext.IsEnabled=!busy;assistantBack.IsEnabled=!busy&&assistantChoice!=null&&assistantStep>0;assistantAnswer.IsEnabled=!busy;}
        private void RenderAssistantSummary(){if(assistantChoice==null)return;var sections=assistantChoice.Sections.Where((s,i)=>AssistantSelected(i)).ToArray();assistantSummary.Text="Предложено действий: "+AssistantIds().Length+"\n\n"+(sections.Length==0?"Группы пока не выбраны. При ответе «Не уверен» мастер ничего не добавляет.":string.Join("\n\n",sections.Select(s=>s.Title+"\n"+s.Detail)))+"\n\nДействия для другой версии Windows исключены. Наличие компонентов проверяется при выполнении. Уменьшение числа служб само по себе не гарантирует ускорения.";}
        private void NextAssistant(){
            if(assistantChoice==null||busy)return;if(assistantStep<assistantAnswers.Length){assistantStep++;RenderAssistant();Get<ScrollViewer>("CollectionsPage").ScrollToTop();return;}for(int i=0;i<assistantChoice.Checks.Count;i++)assistantChoice.Checks[i].IsChecked=AssistantSelected(i);RefreshCollectionCounts();var title=assistantChoice.Title;CloseCollectionAssistant();Text("Status","Группы отмечены в подборке «"+title+"». Просмотрите действия перед добавлением в план.");
        }
        private void CloseCollectionAssistant(){assistantChoice=null;collectionAssistant.Visibility=Visibility.Collapsed;collectionCards.Visibility=Visibility.Visible;collectionIntro.Visibility=Visibility.Visible;Window.UpdateLayout();((FrameworkElement)collectionCards.Children[assistantIndex]).BringIntoView(new Rect(0,0,1,1));}
    }
}
