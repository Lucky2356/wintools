using System;
using System.Collections.Generic;
using System.Linq;

namespace Wintools {
    internal sealed class BrowseGroup {
        public string Key {get;set;}
        public string Title {get;set;}
        public string Detail {get;set;}
    }
    internal static class Groups {
        private static readonly Dictionary<string,string[]> services=new Dictionary<string,string[]> {
            {"Печать и сканирование",new[]{"SPOOLER","PRINTNOTIFY","PRINTSCANBROKERSERVICE","PRINTDEVICECONFIGURATIONSERVICE","STISVC","WIARPC","FAX"}},
            {"Bluetooth и камера",new[]{"BTHSERV","BTHAVCTPSVC","BTAGSERVICE","FRAMESERVER","FRAMESERVERMONITOR"}},
            {"Xbox и игры",new[]{"XBL-AUTH","XBL-SAVE","XBL-NETAPI","XBOX-GIP","GAMINGSERVICES","GAMINGSERVICESNET"}},
            {"Сеть и удалённый доступ",new[]{"SESSIONENV","TERMSERVICE","UMRDPSERVICE","ICSSVC","WFDSCONMGRSVC","QWAVE","SSDPSRV","UPNPHOST","LLTDSVC","FDPHOST","FDRESPUB","WEBCLIENT","WINRM","CDPSVC","RASMAN","SHAREDACCESS","MSISCSI","PEERDISTSVC","SNMPTRAP","DUSMSVC"}},
            {"Диагностика и обслуживание",new[]{"DPS","WDISERVICEHOST","WDISYSTEMHOST","TROUBLESHOOTINGSVC","WERCPLSUPPORT","WERSVC","PCASVC","INVENTORYSVC","DEFRAGSVC","TRKWKS"}},
            {"Вход, безопасность и датчики",new[]{"SCARDSVR","SCDEVICEENUM","SCPOLICYSVC","CERTPROPSVC","SENSORDATASERVICE","SENSRSVC","SENSORSERVICE","NATURALAUTHENTICATION","WBIOSRVC","WLIDSVC","WPCMONSVC"}},
            {"Обновления и уведомления",new[]{"DOSVC","EDGEUPDATE","EDGEUPDATEM","WPNSERVICE","WISVC","PUSHTOINSTALL"}}
        };
        internal static string For(Tweak item) {
            string id=item.Id;
            if(item.Category=="SVC"){
                if(id.StartsWith("PERF-"))return "Диск и поиск";
                foreach(var pair in services)if(pair.Value.Contains(id.Substring(4)))return pair.Key;
                return "Дополнительные возможности";
            }
            if(item.Category=="PRIV"){
                if(id.Contains("CDM")||id.Contains("ADVID")||id=="PRIV-CONSUMER"||id=="PRIV-SOFTLANDING"||id=="PRIV-TAILORED")return "Реклама и советы";
                if(id.Contains("SEARCH")||id.Contains("CLOUD"))return "Поиск и облако";
                if(id.Contains("INK"))return "Ввод текста";
                return "Диагностика и активность";
            }
            if(item.Category=="UI"){
                if(new[]{"UI-FILEEXT","UI-HIDDEN","UI-LAUNCHTO","UI-CLASSIC-CONTEXT","UI-SYNC-NOTIFY","UI-COMPACT-VIEW","UI-NO-RECENT","UI-NO-FREQUENT"}.Contains(id))return "Проводник";
                if(id.Contains("BING")||id.Contains("CORTANA"))return "Поиск";
                if(id.Contains("DARK")||id.Contains("SPOTLIGHT"))return "Тема и экран блокировки";
                if(id.Contains("TASKBAR")||id.Contains("SEARCHBOX")||id.Contains("WIDGETS")||id.Contains("FEEDS")||id.Contains("CHATICON")||id.Contains("SECONDS")||id.Contains("END-TASK"))return "Панель задач";
                return "Меню и запуск";
            }
            if(item.Category=="APPS"){
                if(id.Contains("XBOX")||id.Contains("GAMEASSIST")||id.Contains("SOLITAIRE"))return "Игры и Xbox";
                if(new[]{"APP-TEAMS","APP-TEAMS-NEW","APP-SKYPE","APP-PEOPLE","APP-PHONELINK","APP-CROSSDEVICE"}.Contains(id))return "Общение и телефон";
                if(new[]{"APP-MEDIAPLAYER","APP-MOVIES","APP-CLIPCHAMP","APP-3DVIEWER","APP-PAINT3D","APP-CAMERA","APP-MIXEDREALITY"}.Contains(id))return "Фото, видео и творчество";
                return "Приложения Microsoft";
            }
            if(item.Category=="SYS")return id.Contains("POWER")||id.Contains("HIBERNATE")||id.Contains("FASTSTARTUP")?"Питание и запуск":"Сеть и файловая система";
            if(item.Category=="CLEAN")return new[]{"CLN-DO-CACHE","CLN-WU-DOWNLOAD","CLN-DISM"}.Contains(id)?"Обновления и компоненты":"Временные файлы и корзина";
            if(item.Category=="EDGE")return id.Contains("TASK")?"Обновление браузера":"Работа браузера";
            return Catalogue.Categories[item.Category];
        }
    }
}
