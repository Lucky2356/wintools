using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private void PrepareHistorySmoke(){
            string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(Program.Data,"integrity-history");Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,id+".json"),new JavaScriptSerializer().Serialize(new IntegrityRecord{Schema="wintools/integrity/1",Id=id,Action="sfc-verify",TimeUtc=DateTime.UtcNow.ToString("o"),State="healthy",Summary="Тестовая запись для проверки независимых журналов; Windows не проверялась."}));
        }
        private void HistoryIsolationSmoke(){
            string good=Guid.NewGuid().ToString("N"),bad=Guid.NewGuid().ToString("N");var integrity=Path.Combine(Program.Data,"integrity-history",good+".json");var power=Path.Combine(Program.Data,"power-history",bad+".json");var journal=Path.Combine(Program.Data,"state","applied.dat");
            ReadHistory();int existing=Get<ListBox>("History").Items.Cast<HistoryRow>().Count(r=>!r.IntegrityCheck);
            if(File.Exists(journal))throw new IOException("History isolation smoke requires isolated journal.");
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(integrity));Directory.CreateDirectory(Path.GetDirectoryName(power));
                File.WriteAllText(integrity,new JavaScriptSerializer().Serialize(new IntegrityRecord{Schema="wintools/integrity/1",Id=good,Action="sfc-verify",TimeUtc=DateTime.UtcNow.ToString("o"),State="healthy",Summary="Сохранённый тестовый отчёт."}));File.WriteAllText(power,"{broken");
                ReadHistory();Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.Run==good)&&Get<TextBlock>("HistoryStatus").Text.Contains("Питание"),"Corrupt power journal hid unrelated report or warning");
                using(var locked=new FileStream(journal,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){ReadHistory();Assert(Get<ListBox>("History").Items.Cast<HistoryRow>().Any(r=>r.Run==good)&&Get<TextBlock>("HistoryStatus").Text.Contains("Каталог"),"Locked catalogue hid unrelated history");}File.Delete(journal);
                ShowPage(1);Window.UpdateLayout();Capture("portable-ui-history-partial.png");
                File.Delete(power);ReadHistory();Assert(Get<TextBlock>("HistoryStatus").ToolTip==null&&!Get<TextBlock>("HistoryStatus").Text.Contains("недоступен"),"Recovered history retained stale error");
                File.WriteAllText(integrity,"{broken");ReadHistory();Assert(Get<ListBox>("History").Items.Count==existing&&Get<TextBlock>("HistoryStatus").Text.Contains("Обслуживание")&&!Get<TextBlock>("HistoryStatus").Text.Contains("Изменений пока нет"),"Unavailable history reported empty success");
            }finally{if(File.Exists(integrity))File.Delete(integrity);if(File.Exists(power))File.Delete(power);if(File.Exists(journal))File.Delete(journal);ReadHistory();}
        }
    }
}
