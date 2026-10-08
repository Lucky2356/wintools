using System;
using System.Collections.Generic;
using System.Linq;

namespace Wintools
{
    internal sealed class BrowseGroup
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
    }

    internal static class Groups
    {
        private static readonly Dictionary<string, string[]> services = new Dictionary<string, string[]>
        {
            {
                Lang.T("Печать и сканирование"),
                new[]
                {
                    "SPOOLER",
                    "PRINTNOTIFY",
                    "PRINTSCANBROKERSERVICE",
                    "PRINTDEVICECONFIGURATIONSERVICE",
                    "STISVC",
                    "WIARPC",
                    "FAX"
                }
            },
            {
                Lang.T("Bluetooth и камера"),
                new[]
                {
                    "BTHSERV",
                    "BTHAVCTPSVC",
                    "BTAGSERVICE",
                    "FRAMESERVER",
                    "FRAMESERVERMONITOR"
                }
            },
            {
                Lang.T("Xbox и игры"),
                new[]
                {
                    "XBL-AUTH",
                    "XBL-SAVE",
                    "XBL-NETAPI",
                    "XBOX-GIP",
                    "GAMINGSERVICES",
                    "GAMINGSERVICESNET"
                }
            },
            {
                Lang.T("Сеть и удалённый доступ"),
                new[]
                {
                    "SESSIONENV",
                    "TERMSERVICE",
                    "UMRDPSERVICE",
                    "ICSSVC",
                    "WFDSCONMGRSVC",
                    "QWAVE",
                    "SSDPSRV",
                    "UPNPHOST",
                    "LLTDSVC",
                    "FDPHOST",
                    "FDRESPUB",
                    "WEBCLIENT",
                    "WINRM",
                    "CDPSVC",
                    "RASMAN",
                    "SHAREDACCESS",
                    "MSISCSI",
                    "PEERDISTSVC",
                    "SNMPTRAP",
                    "DUSMSVC"
                }
            },
            {
                Lang.T("Диагностика и обслуживание"),
                new[]
                {
                    "DPS",
                    "WDISERVICEHOST",
                    "WDISYSTEMHOST",
                    "TROUBLESHOOTINGSVC",
                    "WERCPLSUPPORT",
                    "WERSVC",
                    "PCASVC",
                    "INVENTORYSVC",
                    "DEFRAGSVC",
                    "TRKWKS"
                }
            },
            {
                Lang.T("Вход, безопасность и датчики"),
                new[]
                {
                    "SCARDSVR",
                    "SCDEVICEENUM",
                    "SCPOLICYSVC",
                    "CERTPROPSVC",
                    "SENSORDATASERVICE",
                    "SENSRSVC",
                    "SENSORSERVICE",
                    "NATURALAUTHENTICATION",
                    "WBIOSRVC",
                    "WLIDSVC",
                    "WPCMONSVC"
                }
            },
            {
                Lang.T("Обновления и уведомления"),
                new[]
                {
                    "DOSVC",
                    "EDGEUPDATE",
                    "EDGEUPDATEM",
                    "WPNSERVICE",
                    "WISVC",
                    "PUSHTOINSTALL"
                }
            }
        };
        internal static string For(Tweak item)
        {
            string id = item.Id;
            if (item.Category == "SVC")
            {
                if (id.StartsWith("PERF-"))
                    return Lang.T("Диск и поиск");
                foreach (var pair in services)
                    if (pair.Value.Contains(id.Substring(4)))
                        return pair.Key;
                return Lang.T("Дополнительные возможности");
            }

            if (item.Category == "PRIV")
            {
                if (id.Contains("CDM") || id.Contains("ADVID") || id == "PRIV-CONSUMER" || id == "PRIV-SOFTLANDING" || id == "PRIV-TAILORED")
                    return Lang.T("Реклама и советы");
                if (id.Contains("SEARCH") || id.Contains("CLOUD"))
                    return Lang.T("Поиск и облако");
                if (id.Contains("INK"))
                    return Lang.T("Ввод текста");
                return Lang.T("Диагностика и активность");
            }

            if (item.Category == "UI")
            {
                if (new[]
                {
                    "UI-FILEEXT",
                    "UI-HIDDEN",
                    "UI-LAUNCHTO",
                    "UI-CLASSIC-CONTEXT",
                    "UI-SYNC-NOTIFY",
                    "UI-COMPACT-VIEW",
                    "UI-NO-RECENT",
                    "UI-NO-FREQUENT",
                    "UI-HIDE-GALLERY"
                }.Contains(id))
                    return Lang.T("Проводник");
                if (id.Contains("BING") || id.Contains("CORTANA"))
                    return Lang.T("Поиск");
                if (id.Contains("DARK") || id.Contains("SPOTLIGHT"))
                    return Lang.T("Тема и экран блокировки");
                if (id.Contains("TASKBAR") || id.Contains("SEARCHBOX") || id.Contains("WIDGETS") || id.Contains("FEEDS") || id.Contains("CHATICON") || id.Contains("SECONDS") || id.Contains("END-TASK") || id.Contains("TASKVIEW") || id.Contains("COPILOT-BUTTON"))
                    return Lang.T("Панель задач");
                return Lang.T("Меню и запуск");
            }

            if (item.Category == "APPS")
            {
                if (id.Contains("XBOX") || id.Contains("GAMEASSIST") || id.Contains("SOLITAIRE"))
                    return Lang.T("Игры и Xbox");
                if (new[]
                {
                    "APP-TEAMS",
                    "APP-TEAMS-NEW",
                    "APP-SKYPE",
                    "APP-PEOPLE",
                    "APP-PHONELINK",
                    "APP-CROSSDEVICE"
                }.Contains(id))
                    return Lang.T("Общение и телефон");
                if (new[]
                {
                    "APP-MEDIAPLAYER",
                    "APP-MOVIES",
                    "APP-CLIPCHAMP",
                    "APP-3DVIEWER",
                    "APP-PAINT3D",
                    "APP-CAMERA",
                    "APP-MIXEDREALITY"
                }.Contains(id))
                    return Lang.T("Фото, видео и творчество");
                return Lang.T("Приложения Microsoft");
            }

            if (item.Category == "SYS")
                return id.Contains("POWER") || id.Contains("HIBERNATE") || id.Contains("FASTSTARTUP") ? Lang.T("Питание и запуск") : Lang.T("Сеть и файловая система");
            if (item.Category == "CLEAN")
                return new[]
                {
                    "CLN-DO-CACHE",
                    "CLN-WU-DOWNLOAD",
                    "CLN-DISM"
                }.Contains(id) ? Lang.T("Обновления и компоненты") : Lang.T("Временные файлы и корзина");
            if (item.Category == "EDGE")
                return id.Contains("TASK") ? Lang.T("Обновление браузера") : Lang.T("Работа браузера");
            return Catalogue.Categories[item.Category];
        }
    }
}
