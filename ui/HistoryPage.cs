using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace Wintools {
    internal sealed partial class MainWindow {
        private void ReadHistory() {
            var rows=new List<HistoryRow>();var failed=new List<string>();var errors=new List<string>();
            ReadHistorySource("Каталог",()=>HistoryRows(Path.Combine(Program.Data,"state","applied.dat"),catalogue),rows,failed,errors);
            ReadHistorySource("Службы",ServiceHistoryRows,rows,failed,errors);
            ReadHistorySource("Питание",PowerHistoryRows,rows,failed,errors);
            ReadHistorySource("Обслуживание",IntegrityHistoryRows,rows,failed,errors);
            ReadHistorySource("Автозагрузка",StartupHistoryRows,rows,failed,errors);
            ReadHistorySource("Процессы",ProcessHistoryRows,rows,failed,errors);
            ReadHistorySource("Приложения Store",StoreHistoryRows,rows,failed,errors);
            Get<ListBox>("History").ItemsSource=rows.OrderByDescending(r=>r.TimeUtc).ToArray();
            Text("HistoryStatus",failed.Count>0?"Журнал частично недоступен: "+string.Join(", ",failed)+". Доступно записей: "+rows.Count+". Записи недоступных разделов скрыты. Повторите чтение позже; подробности — в подсказке.":rows.Count==0?"Изменений пока нет. После выполнения действия здесь появится запись.":"Запусков: "+rows.Count+". Сначала откатывайте самые новые изменения.");
            Get<TextBlock>("HistoryStatus").ToolTip=errors.Count==0?null:string.Join("\n\n",errors);RefreshEnabled();
        }
        private static void ReadHistorySource(string name,Func<HistoryRow[]> read,List<HistoryRow> rows,List<string> failed,List<string> errors){
            try{var loaded=read();rows.AddRange(loaded);}
            catch(IOException ex){failed.Add(name);errors.Add(name+": "+ex.Message);}
            catch(UnauthorizedAccessException ex){failed.Add(name);errors.Add(name+": нет доступа. "+ex.Message);}
        }
    }
}
