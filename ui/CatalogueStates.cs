using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Wintools {
    internal sealed partial class MainWindow {
        private Dictionary<string,TweakState> tweakStates;
        private bool readingTweakStates,tweakStatesPending;
        private Func<IEnumerable<Tweak>,Dictionary<string,TweakState>> tweakStateRead=TweakStates.Read;
        // Re-reads registry values and scheduler tasks after startup and every finished operation; a request during a read queues one more read.
        private async Task RefreshTweakStates(){
            if(closed||catalogue==null)return;if(readingTweakStates){tweakStatesPending=true;return;}
            readingTweakStates=true;
            try{var read=tweakStateRead;var items=catalogue.Where(TweakStates.Supported).ToArray();var result=await Task.Run(()=>read(items));if(!closed)tweakStates=result;}
            catch(Exception ex){if(!closed){tweakStates=null;Text("Status","Не удалось прочитать текущее состояние настроек: "+ex.Message);}}
            finally{readingTweakStates=false;if(!closed)Filter();}
            if(tweakStatesPending&&!closed){tweakStatesPending=false;await RefreshTweakStates();}
        }
        private TweakState CurrentState(Tweak item){
            if(item==null)return null;
            if(item.Kind=="SVC"){
                if(services==null)return TweakState.Unknown(readingServices?"Читаем состояние службы…":"Состояние неизвестно · нажмите ↻");
                var service=services.FirstOrDefault(s=>string.Equals(s.Name,item.Target,StringComparison.OrdinalIgnoreCase));
                if(service==null)return TweakState.Unknown("Не установлена на этом ПК");
                return item.Value=="disabled"?new TweakState{Applied=service.Mode=="Disabled",Text=service.Detail}:TweakState.Unknown(service.Detail);
            }
            if(!TweakStates.Supported(item))return null;
            TweakState state;
            if(tweakStates!=null&&tweakStates.TryGetValue(item.Id,out state))return state;
            return TweakState.Unknown(readingTweakStates?"Читаем текущее состояние…":"Состояние неизвестно · нажмите ↻");
        }
        private string InlineStatus(Tweak item){var state=CurrentState(item);return state==null?"":state.Text;}
        private bool KnownApplied(Tweak item){var state=CurrentState(item);return state!=null&&state.Applied==true;}
        private string StateDetail(Tweak item){
            if(item==null)return "";var state=CurrentState(item);if(state==null)return "";
            if(item.Kind=="SVC")return "\n"+(services==null?"Состояние пока неизвестно. Нажмите «↻ Состояние» над списком.":state.Applied==null&&state.Text.StartsWith("Не установлена")?"Служба не установлена на этом ПК.":state.Text+" (снимок; обновить кнопкой ↻ над списком)");
            return "\nСейчас: "+state.Text+(state.Applied==true?". Повторное применение ничего не изменит.":"");
        }
    }
}
