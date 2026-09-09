using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Wintools {
    internal sealed class ActionProfile {
        public string Schema;
        public string Version=Program.Version;
        public string[] Actions;
    }
    internal sealed partial class MainWindow {
        private void InitializeProfiles(){
            Click("ProfileExport",()=>{if(busy)return;var dialog=new SaveFileDialog{Title="Сохранить план как профиль",Filter="Профиль Wintools (*.json)|*.json",FileName="wintools-profile.json",OverwritePrompt=true};if(dialog.ShowDialog(Window)!=true)return;try{File.WriteAllText(dialog.FileName,new JavaScriptSerializer().Serialize(new ActionProfile{Schema="wintools/profile/1",Actions=preferences.Plan.ToArray()}),new UTF8Encoding(false));Text("Status","Профиль сохранён. Он содержит список действий, а не резервную копию Windows.");}catch(Exception ex){Text("Status","Не удалось сохранить профиль: "+ex.Message);}});
            ClickAsync("ProfileImport",async()=>{if(busy)return;var dialog=new OpenFileDialog{Title="Загрузить профиль в план",Filter="Профиль Wintools (*.json)|*.json",CheckFileExists=true};if(dialog.ShowDialog(Window)!=true)return;try{if(new FileInfo(dialog.FileName).Length>65536)throw new IOException("Файл профиля слишком большой.");var ids=ReadProfile(File.ReadAllText(dialog.FileName));if(!await Confirm("Добавить в план "+ids.Length+" действий из профиля?\n\nWindows сейчас не изменится. Проверьте действия и ограничения в плане перед применением."))return;var previous=preferences.Plan;var next=previous.Concat(ids).Distinct().ToList();if(next.Count>200)throw new IOException("В объединённом плане больше 200 действий.");preferences.Plan=next;if(!SavePreferences()){preferences.Plan=previous;return;}RefreshPlan();Text("Status","Профиль добавлен в план. Сначала проверьте предпросмотр и ограничения действий.");ShowPage(4);}catch(Exception ex){Text("Status","Не удалось загрузить профиль: "+ex.Message);}});
        }
        private string[] ReadProfile(string json){var profile=new JavaScriptSerializer().Deserialize<ActionProfile>(json);if(profile==null||profile.Schema!="wintools/profile/1"||profile.Actions==null||profile.Actions.Length>200)throw new IOException("Неподдерживаемый формат профиля.");if(profile.Actions.Any(id=>!catalogue.Any(t=>t.Id==id&&CanPlan(t))))throw new IOException("В профиле есть неизвестное или недопустимое действие. Обновите приложение и проверьте файл.");return profile.Actions.Distinct().ToArray();}
    }
}
