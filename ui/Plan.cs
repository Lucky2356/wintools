using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private bool stopPlan;
        private bool runningPlan;
        private Func<Tweak,bool,Action<string>,Task<EngineResult>> planAction;
        private static bool CanPlan(Tweak item){return item!=null&&item.Category!="CLEAN"&&item.Kind!="EDGE";}
        private void InitializePlan() {
            planAction=(item,dry,progress)=>Engine.Run(item.Verb,item.Id,"-",dry,preferences.RestorePoint,progress);
            preferences.Plan=preferences.Plan.Where(id=>catalogue.Any(t=>t.Id==id&&CanPlan(t))).Distinct().Take(50).ToList();
            Click("PlanAdd",()=>{var item=Selected();if(busy||!CanPlan(item)||preferences.Plan.Contains(item.Id))return;if(preferences.Plan.Count>=50){Text("Status","В плане уже 50 действий. Выполните или сократите его.");return;}preferences.Plan.Add(item.Id);SavePreferences();RefreshPlan();Text("Status","Добавлено в план: "+item.Title);});
            Get<ListBox>("PlanItems").SelectionChanged+=(s,e)=>RefreshEnabled();
            Click("PlanRemove",()=>ChangePlan(0));Click("PlanUp",()=>ChangePlan(-1));Click("PlanDown",()=>ChangePlan(1));
            Click("PlanStop",()=>{stopPlan=true;Enabled("PlanStop",false);Text("PlanStatus","Остановимся после текущего действия. Уже выполненные изменения сохранятся в истории.");});
            ClickAsync("PlanPreview",()=>RunPlan(true));
            ClickAsync("PlanApply",async()=>{if(busy||preferences.Plan.Count==0)return;var items=PlanItems();if(await Confirm("Выполнить по порядку "+items.Length+" действий?\n\n"+string.Join("\n\n",items.Select(t=>t.Title+"\n"+Risk(t)+". "+t.Caveat))+"\n\nПри ошибке выполнение остановится. Выполненные действия останутся в истории для отдельного отката."))await RunPlan(false);});
        }
        private Tweak[] PlanItems(){return preferences.Plan.Select(id=>catalogue.First(t=>t.Id==id)).ToArray();}
        private void ChangePlan(int direction) {
            if(busy)return;var list=Get<ListBox>("PlanItems");int index=list.SelectedIndex;if(index<0)return;
            if(direction==0)preferences.Plan.RemoveAt(index);
            else {int target=index+direction;if(target<0||target>=preferences.Plan.Count)return;string id=preferences.Plan[index];preferences.Plan.RemoveAt(index);preferences.Plan.Insert(target,id);index=target;}
            SavePreferences();RefreshPlan();list.SelectedIndex=Math.Min(index,preferences.Plan.Count-1);
        }
        private void RefreshPlan() {
            var items=PlanItems();Get<ListBox>("PlanItems").ItemsSource=items.Select((t,i)=>new ActionRow{Item=t,DisplayTitle=(i+1)+". "+t.Title,Summary=Risk(t)+" · "+t.Description}).ToArray();
            Get<Button>("NavPlan").Content=items.Length==0?"☷    План изменений":"☷    Мой план · "+items.Length;
            Text("PlanStatus",items.Length==0?"План пуст. Откройте действие в каталоге и нажмите «В план». Список сохраняется между запусками.":"В плане: "+items.Length+". Сначала проверьте предпросмотр. Очистка файлов и удаление Edge выполняются отдельно.");
            RefreshEnabled();
        }
        private void RefreshPlanEnabled() {
            var item=Selected();Enabled("PlanAdd",!busy&&CanPlan(item)&&!preferences.Plan.Contains(item.Id));
            int index=Get<ListBox>("PlanItems").SelectedIndex,count=preferences.Plan.Count;
            Enabled("PlanRemove",!busy&&index>=0);Enabled("PlanUp",!busy&&index>0);Enabled("PlanDown",!busy&&index>=0&&index<count-1);
            Enabled("PlanPreview",!busy&&count>0);Enabled("PlanApply",!busy&&count>0);Visible("PlanStop",runningPlan);Enabled("PlanStop",runningPlan&&!stopPlan);
        }
        private async Task RunPlan(bool dry) {
            if(busy||preferences.Plan.Count==0)return;
            var items=PlanItems();runningPlan=true;stopPlan=false;SetBusy(true);ExpandOutput(true);var output=new StringBuilder();int completed=0;bool failed=false;
            try {
                foreach(var item in items) {
                    if(stopPlan)break;
                    string header=(completed+1)+" / "+items.Length+" · "+item.Title;
                    Text("PlanStatus",(dry?"Предпросмотр: ":"Выполнение: ")+header);
                    string previous=output.ToString();
                    var result=await planAction(item,dry,value=>{Get<TextBox>("Output").Text=previous+"\n"+header+"\n"+value;Get<TextBox>("Output").ScrollToEnd();});
                    output.AppendLine(header).AppendLine(result.Output);
                    if(output.Length>150000)output.Remove(0,output.Length-150000);
                    Get<TextBox>("Output").Text=output.ToString();
                    if(result.Code!=0){failed=true;break;}
                    completed++;
                    if(!dry){preferences.Plan.Remove(item.Id);preferences.Save();}
                }
            } catch(Exception ex){failed=true;Get<TextBox>("Output").AppendText("\n"+ex.Message);}
            finally{runningPlan=false;SetBusy(false);ReadHistory();RefreshPlan();}
            string message=(dry?"Проверено: ":"Выполнено: ")+completed+" из "+items.Length+(failed?". Остановлено из-за ошибки; подробности в выводе.":stopPlan?". Остановлено по вашему запросу.":". Готово.");
            Text("PlanStatus",message);Text("Status",message);await PrepareAutomaticUpdate();
        }
    }
}
