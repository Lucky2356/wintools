using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Wintools {
    internal sealed class MainForm : Form {
        private readonly bool smoke;
        private readonly Preferences preferences;
        private readonly List<Tweak> catalogue;
        private readonly ListBox navigation=new ListBox();
        private readonly TextBox search=new TextBox();
        private readonly CheckBox favorites=new CheckBox(), risky=new CheckBox();
        private readonly ListView items=new ListView(), history=new ListView();
        private readonly Label title=new Label(), details=new Label(), status=new Label(), updateStatus=new Label();
        private readonly TextBox output=new TextBox();
        private readonly ProgressBar progress=new ProgressBar();
        private readonly ToolTip hints=new ToolTip{AutoPopDelay=30000,InitialDelay=250,ReshowDelay=100};
        private readonly Button apply=Button("Применить",true), preview=Button("Предпросмотр",false), revert=Button("Откатить",false), star=Button("В избранное",false);
        private readonly Button install=Button("Обновить и перезапустить",true);
        private readonly CheckBox autoCheck=new CheckBox(), previewChannel=new CheckBox(), restorePoint=new CheckBox();
        private readonly List<Control> operations=new List<Control>();
        private Update available;
        private bool busy,checking;
        private readonly Color ink=Color.FromArgb(24,39,56), muted=Color.FromArgb(82,98,114);

        internal MainForm(bool smokeMode) {
            smoke=smokeMode;preferences=Preferences.Load();catalogue=Catalogue.Load();
            if(preferences.Favorites==null)preferences.Favorites=new List<string>();
            Text="Wintools • обслуживание Windows";ClientSize=new Size(1220,850);MinimumSize=new Size(1040,760);
            StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(244,247,250);ForeColor=ink;
            AutoScaleMode=AutoScaleMode.Dpi;
            Build();Filter();ReadHistory();
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show("Дождитесь завершения операции. Прерывание может оставить частично выполненные изменения.","Операция выполняется");}};
            Shown+=async(s,e)=>{
                if(smoke){await Task.Delay(400);using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(Program.Home,"portable-ui.png"));}Close();return;}
                if(preferences.AutoCheck)await CheckUpdates(false);
            };
        }
        private static Button Button(string text,bool primary) {
            return new Button{Text=text,AutoSize=true,MinimumSize=new Size(112,38),FlatStyle=FlatStyle.Flat,
                BackColor=primary?Color.FromArgb(0,112,106):Color.FromArgb(232,239,245),ForeColor=primary?Color.White:Color.FromArgb(24,39,56),
                Padding=new Padding(10,2,10,2),Margin=new Padding(0,0,8,8),Cursor=Cursors.Hand};
        }
        private static FlowLayoutPanel Flow() {return new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0),WrapContents=true};}
        private Button ActionButton(string text,Action action) {var button=Button(text,false);button.Click+=(s,e)=>action();operations.Add(button);return button;}
        private void Build() {
            var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20)};shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,70));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,36));Controls.Add(shell);
            var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
            header.Controls.Add(new Label{Text="Wintools",Font=new Font("Segoe UI",26,FontStyle.Bold),AutoSize=true},0,0);
            header.Controls.Add(new Label{Text="PORTABLE  /  "+Program.Version+"\nИзменения под вашим контролем",AutoSize=true,Anchor=AnchorStyles.Right,TextAlign=ContentAlignment.TopRight,ForeColor=muted},1,0);shell.Controls.Add(header,0,0);
            var body=new SplitContainer{Size=new Size(1180,700),Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,SplitterDistance=225,Panel1MinSize=205,Panel2MinSize=700};shell.Controls.Add(body,0,1);
            var sidebar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(0,0,16,0)};sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,36));sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,90));sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,58));body.Panel1.Controls.Add(sidebar);
            sidebar.Controls.Add(new Label{Text="КАТАЛОГ ДЕЙСТВИЙ",ForeColor=muted,AutoSize=true},0,0);
            navigation.Dock=DockStyle.Fill;navigation.BorderStyle=BorderStyle.None;navigation.BackColor=BackColor;navigation.ItemHeight=34;navigation.IntegralHeight=false;navigation.DrawMode=DrawMode.OwnerDrawFixed;
            foreach(var category in Catalogue.Categories)navigation.Items.Add(category);
            navigation.DrawItem+=(s,e)=>{if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;using(var brush=new SolidBrush(selected?Color.FromArgb(218,238,234):BackColor))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,((KeyValuePair<string,string>)navigation.Items[e.Index]).Value,Font,new Rectangle(e.Bounds.X+10,e.Bounds.Y+7,e.Bounds.Width-14,e.Bounds.Height),ink,TextFormatFlags.EndEllipsis);};
            navigation.SelectedIndexChanged+=(s,e)=>Filter();navigation.SelectedIndex=0;sidebar.Controls.Add(navigation,0,1);
            var quick=Flow();quick.Controls.Add(ActionButton("Диагностика",()=>Run("diagnose",null,null,false)));quick.Controls.Add(ActionButton("Проверить изменения",()=>Run("verify",null,null,false)));sidebar.Controls.Add(quick,0,2);
            sidebar.Controls.Add(new Label{Text="Без обещаний роста FPS.\nСначала проблема — затем действие.",ForeColor=muted,Dock=DockStyle.Fill,Font=new Font("Segoe UI",9)},0,3);
            var right=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Percent,72));right.RowStyles.Add(new RowStyle(SizeType.Percent,28));body.Panel2.Controls.Add(right);
            var tabs=new TabControl{Dock=DockStyle.Fill};right.Controls.Add(tabs,0,0);
            var catalogueTab=new TabPage("Действия"){BackColor=Color.White,Padding=new Padding(14)};tabs.TabPages.Add(catalogueTab);BuildCatalogue(catalogueTab);
            var historyTab=new TabPage("Журнал и откат"){BackColor=Color.White,Padding=new Padding(14)};tabs.TabPages.Add(historyTab);BuildHistory(historyTab);
            var settingsTab=new TabPage("Обновления и настройки"){BackColor=Color.White,Padding=new Padding(18)};tabs.TabPages.Add(settingsTab);BuildSettings(settingsTab);
            var logs=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Padding=new Padding(0,12,0,0)};logs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));logs.RowStyles.Add(new RowStyle(SizeType.Absolute,28));logs.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.Controls.Add(logs,0,1);
            logs.Controls.Add(new Label{Text="ВЫВОД ОПЕРАЦИИ",ForeColor=muted,AutoSize=true},0,0);
            output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Both;output.WordWrap=false;output.Dock=DockStyle.Fill;output.BackColor=Color.FromArgb(24,39,56);output.ForeColor=Color.FromArgb(220,234,240);output.Font=new Font("Consolas",9);output.BorderStyle=BorderStyle.None;output.Text="Выберите действие и откройте предпросмотр. Системные изменения автоматически не запускаются.";logs.Controls.Add(output,0,1);
            var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));
            status.Text="Готово • "+catalogue.Count+" действий";status.AutoSize=true;status.Padding=new Padding(0,8,0,0);footer.Controls.Add(status,0,0);progress.Dock=DockStyle.Fill;progress.Visible=false;footer.Controls.Add(progress,1,0);shell.Controls.Add(footer,0,2);
        }
        private void BuildCatalogue(Control parent) {
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,178));parent.Controls.Add(layout);
            search.Dock=DockStyle.Fill;search.AccessibleName="Поиск по названию, описанию или ID";search.TextChanged+=(s,e)=>Filter();layout.Controls.Add(search,0,0);
            var filters=Flow();favorites.Text="Только избранное";favorites.AutoSize=true;risky.Text="Показывать высокий риск";risky.AutoSize=true;favorites.CheckedChanged+=(s,e)=>Filter();risky.CheckedChanged+=(s,e)=>Filter();filters.Controls.Add(favorites);filters.Controls.Add(risky);layout.Controls.Add(filters,0,1);
            items.Dock=DockStyle.Fill;items.View=View.Details;items.FullRowSelect=true;items.MultiSelect=false;items.HideSelection=false;items.BorderStyle=BorderStyle.FixedSingle;items.Columns.Add("Действие — поиск по названию или ID",470);items.Columns.Add("Риск",90);items.Columns.Add("Windows",75);items.SelectedIndexChanged+=(s,e)=>SelectItem();layout.Controls.Add(items,0,2);
            var card=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(0,8,0,0)};card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));card.RowStyles.Add(new RowStyle(SizeType.Absolute,29));card.RowStyles.Add(new RowStyle(SizeType.Percent,100));card.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.Controls.Add(card,0,3);
            title.Dock=DockStyle.Fill;title.Font=new Font("Segoe UI",11,FontStyle.Bold);title.AutoEllipsis=true;card.Controls.Add(title,0,0);
            details.Dock=DockStyle.Fill;details.AutoEllipsis=true;details.ForeColor=muted;details.Font=new Font("Segoe UI",9);card.Controls.Add(details,0,1);
            var buttons=Flow();buttons.Controls.Add(preview);buttons.Controls.Add(apply);buttons.Controls.Add(revert);buttons.Controls.Add(star);card.Controls.Add(buttons,0,2);operations.AddRange(new Control[]{preview,apply,revert,star});
            preview.Click+=(s,e)=>{var item=Selected();if(item!=null)Run(item.Verb,item.Id,null,true);};
            apply.Click+=(s,e)=>{var item=Selected();if(item!=null)ConfirmAndRun(item.Verb,item.Id,null,item.Title+"\n\n"+item.Description+"\n\nОткат: "+item.Rollback);};
            revert.Click+=(s,e)=>{var item=Selected();if(item!=null)ConfirmAndRun("revert",item.Id,null,"Откатить сохранённые изменения для «"+item.Title+"»?");};
            star.Click+=(s,e)=>{var item=Selected();if(item==null)return;if(preferences.Favorites.Contains(item.Id))preferences.Favorites.Remove(item.Id);else preferences.Favorites.Add(item.Id);SavePreferences();Filter();};
        }
        private void BuildHistory(Control parent) {
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));parent.Controls.Add(layout);
            layout.Controls.Add(new Label{Text="История относится к этому portable-каталогу. Не переносите её на другой ПК.\nPENDING / FAILED требуют внимания; сначала откатывайте более новые изменения.",Dock=DockStyle.Fill,ForeColor=muted},0,0);
            history.Dock=DockStyle.Fill;history.View=View.Details;history.FullRowSelect=true;history.MultiSelect=false;history.Columns.Add("Запуск",265);history.Columns.Add("ID",230);history.Columns.Add("Результат",130);history.HideSelection=false;layout.Controls.Add(history,0,1);
            var buttons=Flow();buttons.Padding=new Padding(0,8,0,0);buttons.Controls.Add(ActionButton("Обновить журнал",ReadHistory));buttons.Controls.Add(ActionButton("Откатить запуск",()=>{if(history.SelectedItems.Count>0){var run=(string)history.SelectedItems[0].Tag;ConfirmAndRun("revert",null,run,"Откатить весь запуск "+run+"?\nИсходные состояния берутся из сохранённого журнала.");}}));buttons.Controls.Add(ActionButton("Открыть логи",()=>OpenFolder("logs")));buttons.Controls.Add(ActionButton("Открыть отчёты",()=>OpenFolder("reports")));layout.Controls.Add(buttons,0,2);
        }
        private void BuildSettings(Control parent) {
            var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};parent.Controls.Add(panel);
            panel.Controls.Add(new Label{Text="Обновления из Lucky2356/wintools",Font=new Font("Segoe UI",14,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,16)});
            autoCheck.Text="Проверять обновления при запуске";autoCheck.AutoSize=true;autoCheck.Checked=preferences.AutoCheck;autoCheck.CheckedChanged+=(s,e)=>{preferences.AutoCheck=autoCheck.Checked;SavePreferences();};panel.Controls.Add(autoCheck);
            previewChannel.Text="Получать кандидаты на выпуск (RC)";previewChannel.AutoSize=true;previewChannel.Checked=preferences.IncludePreview;previewChannel.CheckedChanged+=(s,e)=>{preferences.IncludePreview=previewChannel.Checked;available=null;install.Enabled=false;SavePreferences();};panel.Controls.Add(previewChannel);
            updateStatus.Text="Текущая версия: "+Program.Version;updateStatus.AutoSize=true;updateStatus.MaximumSize=new Size(650,0);updateStatus.Margin=new Padding(0,14,0,10);panel.Controls.Add(updateStatus);
            var check=Button("Проверить обновления",false);check.Click+=async(s,e)=>await CheckUpdates(true);panel.Controls.Add(check);operations.Add(check);
            install.Enabled=false;install.Click+=async(s,e)=>await InstallUpdate();panel.Controls.Add(install);
            panel.Controls.Add(new Label{Text="Перед заменой проверяется SHA-256 из GitHub Releases. Прежний EXE сохраняется\nрядом как .previous. Журнал, резервные копии и избранное остаются в WintoolsData.\nУстановка обновления запускается этой кнопкой; во время операции она запрещена.",AutoSize=true,ForeColor=muted,Margin=new Padding(0,4,0,20)});
            restorePoint.Text="Запрашивать создание точки восстановления перед изменениями";restorePoint.AutoSize=true;restorePoint.Checked=preferences.RestorePoint;restorePoint.CheckedChanged+=(s,e)=>{preferences.RestorePoint=restorePoint.Checked;SavePreferences();};panel.Controls.Add(restorePoint);
            panel.Controls.Add(new Label{Text="Windows может отказать в создании точки — причина будет в логе.\nДля системных операций появится штатный запрос UAC. Диагностика работает без него.\nПрограмма не устанавливает службы, автозапуск или фоновые задания.",AutoSize=true,ForeColor=muted,Margin=new Padding(0,12,0,12)});
            panel.Controls.Add(ActionButton("Папка portable-данных",()=>OpenFolder("")));
        }
        private Tweak Selected(){return items.SelectedItems.Count==0?null:(Tweak)items.SelectedItems[0].Tag;}
        private void Filter() {
            if(navigation.SelectedItem==null)return;
            string category=((KeyValuePair<string,string>)navigation.SelectedItem).Key,query=search.Text.Trim();
            items.BeginUpdate();items.Items.Clear();
            foreach(var item in catalogue.Where(t=>(category=="ALL" || t.Category==category) && (!favorites.Checked || preferences.Favorites.Contains(t.Id)) && (risky.Checked || t.Risk!="high") && (t.Title+" "+t.Description+" "+t.Id).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)) {
                var row=new ListViewItem((preferences.Favorites.Contains(item.Id)?"★ ":"")+item.Title){Tag=item};row.SubItems.Add(item.Risk=="high"?"Высокий":item.Risk=="med"?"Средний":"Низкий");row.SubItems.Add(item.Os=="any"?"10 / 11":item.Os=="win11"?"11":"10");items.Items.Add(row);
            }
            items.EndUpdate();if(items.Items.Count>0)items.Items[0].Selected=true;SelectItem();
        }
        private void SelectItem() {
            var item=Selected();title.Text=item==null?"Нет подходящих действий":item.Title;
            details.Text=item==null?"Измените поиск или фильтры.":item.Id+" • Откат: "+item.Rollback+"\n"+item.Description;
            hints.SetToolTip(details,item==null?"":item.Description+"\nОткат: "+item.Rollback);
            preview.Enabled=apply.Enabled=star.Enabled=item!=null&&!busy;revert.Enabled=item!=null&&item.Category!="CLEAN"&&!busy;
            star.Text=item!=null&&preferences.Favorites.Contains(item.Id)?"Убрать ★":"В избранное";
        }
        private void ReadHistory() {
            history.Items.Clear();var path=Path.Combine(Program.Data,"state","applied.dat");if(!File.Exists(path))return;
            foreach(var line in File.ReadAllLines(path).Reverse()) {
                var p=line.Split('|');if(p.Length!=11)continue;
                var row=new ListViewItem(p[0]){Tag=p[0]};row.SubItems.Add(p[1]);row.SubItems.Add(p[9]);if(p[9]=="PENDING"||p[9]=="FAILED")row.ForeColor=Color.Firebrick;history.Items.Add(row);
            }
        }
        private void ConfirmAndRun(string verb,string id,string run,string description) {
            if(busy)return;
            if(MessageBox.Show(description+"\n\nПродолжить?","Подтверждение изменения",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)Run(verb,id,run,false);
        }
        private async void Run(string verb,string id,string run,bool dry) {
            if(busy)return;SetBusy(true);output.Text="Запуск "+verb+(dry?" • предпросмотр":"")+"…";
            try {
                var result=await Engine.Run(verb,id??"-",run??"-",dry,preferences.RestorePoint,text=>{output.Text=text;output.SelectionStart=output.TextLength;output.ScrollToCaret();});
                output.Text=result.Output;status.Text=result.Code==0?"Завершено • код 0":"Требуется внимание • код "+result.Code+" • подробности в выводе";
                ReadHistory();
            }catch(Exception ex){output.Text=ex.Message;status.Text="Операция не завершена";}finally{SetBusy(false);}
        }
        private void SetBusy(bool value){busy=value;foreach(var control in operations)control.Enabled=!value;install.Enabled=!value&&available!=null;progress.Visible=value;progress.Style=ProgressBarStyle.Marquee;if(value)status.Text="Выполняется операция…";SelectItem();}
        private void OpenFolder(string relative){try{var path=relative.Length==0?Program.Data:Program.Under(Program.Data,relative);Program.SafeDirectory(path);Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe",Program.Quote(path)){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message,"Wintools");}}
        private void SavePreferences(){try{preferences.Save();}catch(Exception ex){status.Text="Настройки не сохранены: "+ex.Message;}}
        private async Task CheckUpdates(bool manual) {
            if(checking||busy)return;checking=true;previewChannel.Enabled=false;updateStatus.Text="Проверка GitHub Releases…";
            try {available=await Updates.Check(preferences.IncludePreview);updateStatus.Text=available==null?"Новой совместимой версии в выбранном канале нет.":"Доступна "+available.Release.tag_name+". Можно обновить и перезапустить.";install.Enabled=available!=null&&!busy;if(available!=null)status.Text="Доступно обновление "+available.Release.tag_name;}
            catch(Exception ex){updateStatus.Text="Обновления недоступны: "+ex.Message;if(manual)status.Text="Проверьте подключение к GitHub. Текущая версия работает офлайн.";}finally{checking=false;previewChannel.Enabled=true;}
        }
        private async Task InstallUpdate() {
            if(busy||available==null)return;var update=available;SetBusy(true);updateStatus.Text="Загрузка и проверка SHA-256…";
            try {var directory=await Updates.Download(update);Updates.LaunchReplacement(directory,update.Asset.digest.Substring(7));busy=false;Close();}
            catch(Exception ex){updateStatus.Text=ex.Message;SetBusy(false);}
        }
    }
}
