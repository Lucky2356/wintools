using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private async Task PlanPersistenceSmoke(){
            var previous=preferences.Plan.ToList();var action=planAction;string path=Path.Combine(Program.Data,"preferences.json.tmp");int calls=0;
            try{
                preferences.Plan=new System.Collections.Generic.List<string>{"UI-FILEEXT","UI-LAUNCHTO"};preferences.Save();RefreshPlan();
                planAction=(item,dry,progress)=>{calls++;return Task.FromResult(new EngineResult{Code=0,Output="Тест: действие выполнено тестовым обработчиком, Windows не менялась."});};
                using(var locked=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){await RunPlan(false);}
                Assert(calls==1,"Plan continued after saving failed");Assert(preferences.Plan.SequenceEqual(Preferences.Load().Plan)&&preferences.Plan.Count==2,"Plan memory and disk diverged after completed action");
                Assert(Get<TextBlock>("PlanStatus").Text.Contains("Действие выполнено")&&Get<TextBlock>("PlanStatus").Text.Contains("не удалось сохранить"),"Completed action was hidden behind generic plan error");
            }finally{if(File.Exists(path))File.Delete(path);planAction=action;preferences.Plan=previous;preferences.Save();RefreshPlan();ExpandOutput(false);}
        }
    }
}
