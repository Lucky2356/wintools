using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Wintools {
    internal sealed class MainForm : Form {
        private readonly bool smoke;
        private readonly Preferences preferences;
        private readonly List<Tweak> catalogue;
        private readonly TextBox search=new TextBox(), output=new TextBox(), details=new TextBox();
        private readonly ComboBox category=new ComboBox(), theme=new ComboBox();
        private readonly CheckBox favorites=new CheckBox(), risky=new CheckBox(), autoCheck=new CheckBox(), previewChannel=new CheckBox(), restorePoint=new CheckBox();
        private readonly ListBox items=new ListBox();
        private readonly ListView history=new ListView();
        private readonly Label title=new Label(), metadata=new Label(), count=new Label(), status=new Label(), updateStatus=new Label(), historyStatus=new Label(), pageTitle=new Label(), pageHint=new Label();
        private readonly ModernButton apply=Button("Применить",true), preview=Button("Предпросмотр",false), revert=Button("Откатить",false), star=Button("В избранное",false), install=Button("Обновить и перезапустить",true), historyRevert=Button("Откатить запуск",false), check=Button("Проверить обновления",false), logToggle=Button("Вывод операции  ↓",false);
        private readonly List<Control> operations=new List<Control>();
        private readonly List<ModernButton> navigation=new List<ModernButton>();
        private readonly List<Panel> pages=new List<Panel>();
        private readonly ProgressBar progress=new ProgressBar();
        private readonly TableLayoutPanel workspace=new TableLayoutPanel();
        private Palette palette;
        private Update available;
        private bool busy,checking,applyingTheme,expanded;

        internal MainForm(bool smokeMode) {
            smoke=smokeMode;preferences=Preferences.Load();catalogue=Catalogue.Load();
            Text="Wintools — обслуживание Windows";AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(1200,820);MinimumSize=new Size(1040,760);StartPosition=FormStartPosition.CenterScreen;
            Font=new Font("Segoe UI",10);DoubleBuffered=true;
            Build();foreach(var combo in new[]{theme,category}){combo.DrawMode=DrawMode.OwnerDrawFixed;combo.DrawItem+=DrawComboItem;}ApplyTheme();Filter();ReadHistory();ShowPage(0);ClientSizeChanged+=(s,e)=>ResizeOutput();
            SystemEvents.UserPreferenceChanged+=SystemPreferenceChanged;
            FormClosed+=(s,e)=>SystemEvents.UserPreferenceChanged-=SystemPreferenceChanged;
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;MessageBox.Show(this,"Дождитесь завершения операции.","Операция выполняется",MessageBoxButtons.OK,MessageBoxIcon.Information);}};
            Shown+=async(s,e)=>{
                if(smoke){try{await Smoke();}catch(Exception ex){File.WriteAllText(Path.Combine(Program.Home,"portable-error.txt"),ex.ToString());Environment.ExitCode=4;}Close();return;}
                if(preferences.AutoCheck)await CheckUpdates(false);
            };
        }
        private static ModernButton Button(string text,bool primary){return new ModernButton{Text=text,Primary=primary,AutoSize=false,Width=160,Height=40};}
        private static Label Label(string text,int size,bool bold){return new Label{Text=text,AutoSize=false,Dock=DockStyle.Fill,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),TextAlign=ContentAlignment.MiddleLeft};}
        private static TableLayoutPanel Grid(int columns,int rows){return new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=columns,RowCount=rows,Margin=new Padding(0)};}
        private static FlowLayoutPanel Flow(){return new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,Margin=new Padding(0)};}
        private ModernButton ActionButton(string text,Action action){var b=Button(text,false);b.Click+=(s,e)=>action();operations.Add(b);return b;}
        private static void ConfigureCheck(CheckBox box,string text){box.Text=text;box.AutoSize=true;box.FlatStyle=FlatStyle.Flat;box.Margin=new Padding(0,7,18,6);}
        private void Build() {
            var shell=Grid(2,1);shell.Padding=new Padding(16);shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,184));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(shell);
            var sidebar=Grid(1,7);sidebar.Padding=new Padding(0,0,18,0);sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{62,36,52,52,52})sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,height));sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,156));shell.Controls.Add(sidebar,0,0);
            sidebar.Controls.Add(Label("wintools",24,true),0,0);var version=Label("PORTABLE  /  "+Program.Version,9,false);version.Tag="muted";sidebar.Controls.Add(version,0,1);
            string[] names={"Каталог действий","История и откат","Настройки"};
            for(int i=0;i<names.Length;i++){int index=i;var button=Button(names[i],false);button.Dock=DockStyle.Fill;button.Margin=new Padding(0,4,0,4);button.Click+=(s,e)=>ShowPage(index);navigation.Add(button);sidebar.Controls.Add(button,0,i+2);}
            var quick=Grid(1,3);quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));quick.RowStyles.Add(new RowStyle(SizeType.Absolute,48));quick.RowStyles.Add(new RowStyle(SizeType.Absolute,48));quick.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var diagnose=ActionButton("Диагностика ПК",()=>Run("diagnose",null,null,false));diagnose.Dock=DockStyle.Fill;diagnose.Margin=new Padding(0,0,0,8);quick.Controls.Add(diagnose,0,0);
            var verify=ActionButton("Проверить изменения",()=>Run("verify",null,null,false));verify.Dock=DockStyle.Fill;verify.Margin=new Padding(0,0,0,8);verify.Font=new Font("Segoe UI",9);quick.Controls.Add(verify,0,1);
            var note=Label("Ваш ПК. Ваши решения.\nНичего не меняем без запуска.",9,false);note.Tag="muted";quick.Controls.Add(note,0,2);sidebar.Controls.Add(quick,0,6);
            workspace.ColumnCount=1;workspace.RowCount=4;workspace.Dock=DockStyle.Fill;workspace.Margin=new Padding(0);workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,80));workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,42));workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,28));shell.Controls.Add(workspace,1,0);
            var header=Grid(2,2);header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,172));header.RowStyles.Add(new RowStyle(SizeType.Absolute,46));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            pageTitle.Dock=DockStyle.Fill;pageTitle.Font=new Font("Segoe UI",24,FontStyle.Bold);pageTitle.TextAlign=ContentAlignment.MiddleLeft;header.Controls.Add(pageTitle,0,0);
            pageHint.Dock=DockStyle.Fill;pageHint.Tag="muted";pageHint.Font=new Font("Segoe UI",9);header.Controls.Add(pageHint,0,1);
            theme.DropDownStyle=ComboBoxStyle.DropDownList;theme.Dock=DockStyle.Fill;theme.Margin=new Padding(0,12,0,0);theme.AccessibleName="Цветовая тема";theme.Items.AddRange(new object[]{"Тема Windows","Светлая тема","Тёмная тема"});theme.SelectedIndex=preferences.Theme=="dark"?2:preferences.Theme=="light"?1:0;
            theme.SelectedIndexChanged+=(s,e)=>{if(applyingTheme)return;preferences.Theme=new[]{"system","light","dark"}[theme.SelectedIndex];ApplyTheme();SavePreferences();};header.Controls.Add(theme,1,0);workspace.Controls.Add(header,0,0);
            var host=new BufferedPanel{Dock=DockStyle.Fill,Margin=new Padding(0)};workspace.Controls.Add(host,0,1);
            for(int i=0;i<3;i++){var page=new BufferedPanel{Dock=DockStyle.Fill,Padding=new Padding(18),Tag="surface"};pages.Add(page);host.Controls.Add(page);}
            BuildCatalogue(pages[0]);BuildHistory(pages[1]);BuildSettings(pages[2]);
            var logs=Grid(1,2);logs.Padding=new Padding(0,8,0,0);logs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));logs.RowStyles.Add(new RowStyle(SizeType.Absolute,32));logs.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            logToggle.Dock=DockStyle.Fill;logToggle.Margin=new Padding(0);logToggle.Height=30;logToggle.Font=new Font("Segoe UI",9);logToggle.Click+=(s,e)=>ExpandOutput(!expanded);logs.Controls.Add(logToggle,0,0);
            output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Both;output.WordWrap=false;output.Dock=DockStyle.Fill;output.Font=new Font("Consolas",9);output.BorderStyle=BorderStyle.None;output.Text="Выберите действие и откройте предпросмотр.";output.Margin=new Padding(8);output.Visible=false;logs.Controls.Add(output,0,1);workspace.Controls.Add(logs,0,2);
            var footer=Grid(2,1);footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));status.Dock=DockStyle.Fill;status.Font=new Font("Segoe UI",9);status.Tag="muted";status.Text="Готово к работе";status.TextAlign=ContentAlignment.MiddleLeft;status.AutoEllipsis=true;footer.Controls.Add(status,0,0);progress.Dock=DockStyle.Fill;progress.Margin=new Padding(0,9,0,5);progress.Visible=false;footer.Controls.Add(progress,1,0);workspace.Controls.Add(footer,0,3);
        }
        private void BuildCatalogue(Control parent) {
            var layout=Grid(1,4);layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));parent.Controls.Add(layout);
            var caption=Label("НАЙТИ ДЕЙСТВИЕ",9,true);caption.Tag="muted";layout.Controls.Add(caption,0,0);
            var filters=Grid(2,1);filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,200));search.Dock=DockStyle.Fill;search.AccessibleName="Поиск по названию, описанию или ID";search.Margin=new Padding(0,0,12,0);search.TextChanged+=(s,e)=>Filter();filters.Controls.Add(search,0,0);
            category.Dock=DockStyle.Fill;category.DropDownStyle=ComboBoxStyle.DropDownList;category.AccessibleName="Категория действий";category.DisplayMember="Value";foreach(var entry in Catalogue.Categories)category.Items.Add(entry);category.SelectedIndex=0;category.SelectedIndexChanged+=(s,e)=>Filter();filters.Controls.Add(category,1,0);layout.Controls.Add(filters,0,1);
            var toggles=Flow();ConfigureCheck(favorites,"Избранное");ConfigureCheck(risky,"Показывать высокий риск");favorites.CheckedChanged+=(s,e)=>Filter();risky.CheckedChanged+=(s,e)=>Filter();toggles.Controls.Add(favorites);toggles.Controls.Add(risky);layout.Controls.Add(toggles,0,2);
            var content=Grid(2,1);content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,300));layout.Controls.Add(content,0,3);
            var list=Grid(1,2);list.Padding=new Padding(0,0,16,0);list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));list.RowStyles.Add(new RowStyle(SizeType.Absolute,28));list.RowStyles.Add(new RowStyle(SizeType.Percent,100));count.Dock=DockStyle.Fill;count.Font=new Font("Segoe UI",9);count.Tag="muted";list.Controls.Add(count,0,0);content.Controls.Add(list,0,0);
            items.Dock=DockStyle.Fill;items.BorderStyle=BorderStyle.None;items.IntegralHeight=false;items.DrawMode=DrawMode.OwnerDrawFixed;items.ItemHeight=68;items.AccessibleName="Действия";items.DisplayMember="Title";items.DrawItem+=DrawItem;items.SelectedIndexChanged+=(s,e)=>SelectItem();list.Controls.Add(items,0,1);
            var card=Grid(1,5);card.Tag="raised";card.Padding=new Padding(12);card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));card.RowStyles.Add(new RowStyle(SizeType.Absolute,60));card.RowStyles.Add(new RowStyle(SizeType.Absolute,44));card.RowStyles.Add(new RowStyle(SizeType.Percent,100));card.RowStyles.Add(new RowStyle(SizeType.Absolute,44));card.RowStyles.Add(new RowStyle(SizeType.Absolute,44));content.Controls.Add(card,1,0);
            title.Dock=DockStyle.Fill;title.Font=new Font("Segoe UI",16,FontStyle.Bold);title.AutoEllipsis=true;card.Controls.Add(title,0,0);metadata.Dock=DockStyle.Fill;metadata.Font=new Font("Segoe UI",9);metadata.Tag="muted";card.Controls.Add(metadata,0,1);
            details.Dock=DockStyle.Fill;details.Multiline=true;details.ReadOnly=true;details.BorderStyle=BorderStyle.None;details.ScrollBars=ScrollBars.Vertical;details.Font=new Font("Segoe UI",10);details.AccessibleName="Описание и возможность отката";details.Margin=new Padding(0,4,0,8);card.Controls.Add(details,0,2);
            var first=Grid(2,1);first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));preview.Dock=apply.Dock=DockStyle.Fill;preview.Font=apply.Font=new Font("Segoe UI",9);preview.Margin=new Padding(0,0,6,8);apply.Margin=new Padding(6,0,0,8);first.Controls.Add(preview,0,0);first.Controls.Add(apply,1,0);card.Controls.Add(first,0,3);
            var second=Grid(2,1);second.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));second.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));revert.Dock=star.Dock=DockStyle.Fill;revert.Font=star.Font=new Font("Segoe UI",9);revert.Margin=new Padding(0,0,6,8);star.Margin=new Padding(6,0,0,8);second.Controls.Add(revert,0,0);second.Controls.Add(star,1,0);card.Controls.Add(second,0,4);
            operations.AddRange(new Control[]{preview,apply,revert,star});
            preview.Click+=(s,e)=>{var item=Selected();if(item!=null)Run(item.Verb,item.Id,null,true);};
            apply.Click+=(s,e)=>{var item=Selected();if(item!=null)ConfirmAndRun(item.Verb,item.Id,null,item.Title+"\n\n"+item.Description+"\n\nОткат: "+item.Rollback);};
            revert.Click+=(s,e)=>{var item=Selected();if(item!=null)ConfirmAndRun("revert",item.Id,null,"Откатить сохранённые изменения для «"+item.Title+"»?");};
            star.Click+=(s,e)=>{var item=Selected();if(item==null)return;if(preferences.Favorites.Contains(item.Id))preferences.Favorites.Remove(item.Id);else preferences.Favorites.Add(item.Id);SavePreferences();Filter();};
        }
        private void BuildHistory(Control parent) {
            var layout=Grid(1,3);layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,100));parent.Controls.Add(layout);
            historyStatus.Dock=DockStyle.Fill;historyStatus.Tag="muted";layout.Controls.Add(historyStatus,0,0);
            history.Dock=DockStyle.Fill;history.View=View.Details;history.FullRowSelect=true;history.MultiSelect=false;history.HideSelection=false;history.BorderStyle=BorderStyle.None;history.Columns.Add("Запуск",245);history.Columns.Add("Действие",210);history.Columns.Add("Результат",135);history.OwnerDraw=true;
            history.DrawColumnHeader+=(s,e)=>{using(var b=new SolidBrush(palette.Raised))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,e.Header.Text,Font,e.Bounds,palette.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);};
            history.DrawItem+=(s,e)=>{};history.DrawSubItem+=(s,e)=>{bool selected=e.Item.Selected;var background=selected?palette.Selection:palette.Surface;using(var b=new SolidBrush(background))e.Graphics.FillRectangle(b,e.Bounds);var color=e.Item.SubItems[2].Text=="FAILED"||e.Item.SubItems[2].Text=="PENDING"?palette.Danger:palette.Text;TextRenderer.DrawText(e.Graphics,e.SubItem.Text,Font,e.Bounds,color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            history.SelectedIndexChanged+=(s,e)=>historyRevert.Enabled=!busy&&history.SelectedItems.Count>0;layout.Controls.Add(history,0,1);
            var buttons=Flow();buttons.Padding=new Padding(0,12,0,0);buttons.Controls.Add(ActionButton("Обновить журнал",ReadHistory));historyRevert.Click+=(s,e)=>{if(history.SelectedItems.Count>0){var run=(string)history.SelectedItems[0].Tag;ConfirmAndRun("revert",null,run,"Откатить весь запуск "+run+"?\nСначала откатывайте более новые изменения.");}};operations.Add(historyRevert);buttons.Controls.Add(historyRevert);buttons.Controls.Add(ActionButton("Открыть логи",()=>OpenFolder("logs")));buttons.Controls.Add(ActionButton("Открыть отчёты",()=>OpenFolder("reports")));layout.Controls.Add(buttons,0,2);
        }
        private void BuildSettings(Control parent) {
            var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};parent.Controls.Add(panel);
            var heading=Label("Обновления",18,true);heading.Dock=DockStyle.None;heading.Size=new Size(400,44);panel.Controls.Add(heading);
            ConfigureCheck(autoCheck,"Проверять обновления при запуске");autoCheck.Checked=preferences.AutoCheck;autoCheck.CheckedChanged+=(s,e)=>{preferences.AutoCheck=autoCheck.Checked;SavePreferences();};panel.Controls.Add(autoCheck);
            ConfigureCheck(previewChannel,"Получать кандидаты на выпуск (RC)");previewChannel.Checked=preferences.IncludePreview;previewChannel.CheckedChanged+=(s,e)=>{preferences.IncludePreview=previewChannel.Checked;available=null;install.Enabled=false;updateStatus.Text="Канал изменён. Проверьте обновления.";SavePreferences();};panel.Controls.Add(previewChannel);
            updateStatus.Text="Установлена версия "+Program.Version;updateStatus.AutoSize=true;updateStatus.Margin=new Padding(0,16,0,16);panel.Controls.Add(updateStatus);
            check.Width=250;check.Click+=async(s,e)=>await CheckUpdates(true);operations.Add(check);panel.Controls.Add(check);install.Width=250;install.Enabled=false;install.Click+=async(s,e)=>await InstallUpdate();panel.Controls.Add(install);
            var explanation=new Label{AutoSize=true,Tag="muted",Text="Обновления загружаются из Lucky2356/wintools и проверяются по SHA-256. Предыдущий EXE сохраняется рядом как .previous. Ваши настройки, журнал и резервные копии остаются на месте.",Margin=new Padding(0,8,0,22)};panel.Controls.Add(explanation);
            var safety=Label("Перед изменениями",18,true);safety.Dock=DockStyle.None;safety.Size=new Size(400,44);panel.Controls.Add(safety);
            ConfigureCheck(restorePoint,"Запрашивать точку восстановления");restorePoint.Checked=preferences.RestorePoint;restorePoint.CheckedChanged+=(s,e)=>{preferences.RestorePoint=restorePoint.Checked;SavePreferences();};panel.Controls.Add(restorePoint);
            var safetyText=new Label{AutoSize=true,Tag="muted",Text="Если Windows не сможет создать точку, причина появится в выводе операции. Для системных изменений используется штатный запрос UAC. Диагностика доступна без повышения прав.",Margin=new Padding(0,8,0,18)};panel.Controls.Add(safetyText);
            panel.Controls.Add(ActionButton("Папка данных",()=>OpenFolder("")));
            panel.SizeChanged+=(s,e)=>{int width=Math.Max(200,panel.ClientSize.Width-28);explanation.MaximumSize=safetyText.MaximumSize=updateStatus.MaximumSize=new Size(width,0);};
        }
        private void ShowPage(int index) {
            for(int i=0;i<pages.Count;i++){pages[i].Visible=i==index;navigation[i].Chosen=i==index;navigation[i].Invalidate();}
            pageTitle.Text=new[]{"Каталог действий","История и откат","Настройки"}[index];
            pageHint.Text=new[]{"Выберите действие. Изучите последствия. Примените осознанно.","Сохранённые изменения и исходные состояния этого ПК.","Внешний вид, обновления и защита перед изменениями."}[index];
        }
        private void ApplyTheme() {
            applyingTheme=true;palette=Palette.Create(Palette.IsDark(preferences.Theme));SuspendLayout();Style(this,palette.Background);UiPaint.TitleBar(this,palette.Dark);ResumeLayout(true);items.Invalidate();history.Invalidate();applyingTheme=false;
        }
        private void Style(Control control,Color background) {
            if((string)control.Tag=="surface")background=palette.Surface;else if((string)control.Tag=="raised")background=palette.Raised;
            control.BackColor=background;control.ForeColor=(string)control.Tag=="muted"?palette.Muted:palette.Text;
            var button=control as ModernButton;if(button!=null){button.Palette=palette;button.Invalidate();}
            var combo=control as ComboBox;if(combo!=null){combo.BackColor=palette.Raised;combo.FlatStyle=FlatStyle.Flat;}
            if(control==search||control==output)control.BackColor=palette.Raised;
            foreach(Control child in control.Controls)Style(child,background);
        }
        private void SystemPreferenceChanged(object sender,UserPreferenceChangedEventArgs e) {
            if(IsDisposed||!IsHandleCreated)return;
            try{BeginInvoke(new Action(()=>{if(!IsDisposed)ApplyTheme();}));}catch(InvalidOperationException){}
        }
        private void DrawComboItem(object sender,DrawItemEventArgs e) {
            if(e.Index<0||palette==null)return;var combo=(ComboBox)sender;bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var brush=new SolidBrush(selected?palette.Accent:palette.Raised))e.Graphics.FillRectangle(brush,e.Bounds);
            TextRenderer.DrawText(e.Graphics,combo.GetItemText(combo.Items[e.Index]),combo.Font,new Rectangle(e.Bounds.X+6,e.Bounds.Y,e.Bounds.Width-12,e.Bounds.Height),selected?palette.AccentText:palette.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
        private void DrawItem(object sender,DrawItemEventArgs e) {
            if(e.Index<0||palette==null)return;var item=(Tweak)items.Items[e.Index];bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var b=new SolidBrush(palette.Surface))e.Graphics.FillRectangle(b,e.Bounds);
            var bounds=new Rectangle(e.Bounds.X,e.Bounds.Y+2,e.Bounds.Width-4,e.Bounds.Height-4);
            using(var shape=UiPaint.Round(bounds,9))using(var b=new SolidBrush(selected?palette.Selection:palette.Surface))e.Graphics.FillPath(b,shape);
            if(selected)using(var pen=new Pen(palette.Accent,3))e.Graphics.DrawLine(pen,bounds.Left+2,bounds.Top+14,bounds.Left+2,bounds.Bottom-14);
            TextRenderer.DrawText(e.Graphics,(preferences.Favorites.Contains(item.Id)?"★  ":"")+item.Title,Font,new Rectangle(bounds.X+14,bounds.Y+8,bounds.Width-26,26),palette.Text,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            using(var small=new Font("Segoe UI",9))TextRenderer.DrawText(e.Graphics,item.Id+"  ·  "+Risk(item),small,new Rectangle(bounds.X+14,bounds.Y+35,bounds.Width-26,22),item.Risk=="high"?palette.Danger:palette.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
        }
        private static string Risk(Tweak item){return item.Risk=="high"?"Высокий риск":item.Risk=="med"?"Средний риск":"Низкий риск";}
        private Tweak Selected(){return items.SelectedItem as Tweak;}
        private void Filter() {
            if(category.SelectedItem==null)return;var previous=Selected();string id=previous==null?null:previous.Id;
            string group=((KeyValuePair<string,string>)category.SelectedItem).Key,query=search.Text.Trim();
            var matches=catalogue.Where(t=>(group=="ALL"||t.Category==group)&&(!favorites.Checked||preferences.Favorites.Contains(t.Id))&&(risky.Checked||t.Risk!="high")&&(t.Title+" "+t.Description+" "+t.Id).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            items.BeginUpdate();items.Items.Clear();items.Items.AddRange(matches);var selected=Array.FindIndex(matches,t=>t.Id==id);if(matches.Length>0)items.SelectedIndex=selected<0?0:selected;items.EndUpdate();count.Text=matches.Length==0?"Ничего не найдено":"Доступно действий: "+matches.Length;SelectItem();
        }
        private void SelectItem() {
            var item=Selected();title.Text=item==null?"Ничего не найдено":item.Title;
            metadata.Text=item==null?"Попробуйте другой запрос":item.Id+"\n"+Risk(item)+"  ·  Windows "+(item.Os=="any"?"10 / 11":item.Os=="win11"?"11":"10");
            details.Text=item==null?"Измените поиск или отключите фильтры, чтобы увидеть доступные действия.":item.Description+"\r\n\r\nОТКАТ\r\n"+item.Rollback;
            preview.Enabled=apply.Enabled=star.Enabled=item!=null&&!busy;revert.Enabled=item!=null&&item.Category!="CLEAN"&&!busy;star.Text=item!=null&&preferences.Favorites.Contains(item.Id)?"Убрать ★":"В избранное";
        }
        private void ReadHistory() {
            history.Items.Clear();historyRevert.Enabled=false;historyStatus.Text="Изменений пока нет. После выполнения операции запись появится здесь.";
            var path=Path.Combine(Program.Data,"state","applied.dat");
            try{if(!File.Exists(path))return;int invalid=0;foreach(var line in File.ReadAllLines(path).Reverse()){var p=line.Split('|');if(p.Length!=11){if(line.Trim().Length>0)invalid++;continue;}var row=new ListViewItem(p[0]){Tag=p[0]};row.SubItems.Add(p[1]);row.SubItems.Add(p[9]);history.Items.Add(row);}if(history.Items.Count>0)historyStatus.Text="Записей: "+history.Items.Count+". PENDING / FAILED требуют внимания.\nИстория относится только к этому ПК.";if(invalid>0)historyStatus.Text+="\nПропущено повреждённых строк: "+invalid+". Исходный журнал сохранён.";}
            catch(IOException ex){historyStatus.Text="Журнал временно недоступен. Нажмите «Обновить журнал».\n"+ex.Message;}
            catch(UnauthorizedAccessException ex){historyStatus.Text="Нет доступа к журналу.\n"+ex.Message;}
        }
        private void ResizeOutput(){workspace.RowStyles[2].Height=expanded?Math.Min(170,Math.Max(112,ClientSize.Height-700)):42;}
        private void ExpandOutput(bool value){expanded=value;ResizeOutput();output.Visible=value;logToggle.Text=value?"Вывод операции  ↑":"Вывод операции  ↓";}
        private void ConfirmAndRun(string verb,string id,string run,string description){if(busy)return;if(MessageBox.Show(this,description+"\n\nПродолжить?","Подтверждение изменения",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)Run(verb,id,run,false);}
        private async void Run(string verb,string id,string run,bool dry) {
            if(busy)return;SetBusy(true);ExpandOutput(true);output.Text="Запуск "+verb+(dry?" • предпросмотр":"")+"…";
            try{var result=await Engine.Run(verb,id??"-",run??"-",dry,preferences.RestorePoint,text=>{output.Text=text;output.SelectionStart=output.TextLength;output.ScrollToCaret();});output.Text=result.Output;status.Text=result.Code==0?"Операция завершена":"Требуется внимание • код "+result.Code+" • подробности в выводе";ReadHistory();}
            catch(Exception ex){output.Text=ex.Message;status.Text="Операция не завершена";}finally{SetBusy(false);}
        }
        private void SetBusy(bool value){busy=value;foreach(var control in operations)control.Enabled=!value;install.Enabled=!value&&!checking&&available!=null;check.Enabled=!value&&!checking;previewChannel.Enabled=!value&&!checking;restorePoint.Enabled=!value;progress.Visible=value;progress.Style=ProgressBarStyle.Marquee;if(value)status.Text="Выполняется операция…";SelectItem();historyRevert.Enabled=!value&&history.SelectedItems.Count>0;}
        private void OpenFolder(string relative){try{var path=relative.Length==0?Program.Data:Program.Under(Program.Data,relative);Program.SafeDirectory(path);Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo("explorer.exe",Program.Quote(path)){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Wintools");}}
        private void SavePreferences(){try{preferences.Save();}catch(Exception ex){status.Text="Настройки не сохранены: "+ex.Message;}}
        private async Task CheckUpdates(bool manual) {
            if(checking||busy)return;checking=true;available=null;install.Enabled=false;check.Enabled=false;previewChannel.Enabled=false;updateStatus.Text="Проверка GitHub Releases…";
            try{var result=await Updates.Check(preferences.IncludePreview);if(IsDisposed)return;available=result;updateStatus.Text=available==null?"У вас актуальная версия для выбранного канала.":"Доступна версия "+available.Release.tag_name;if(available!=null)status.Text="Доступно обновление "+available.Release.tag_name;}
            catch(Exception ex){if(!IsDisposed){updateStatus.Text="Не удалось проверить обновления: "+ex.Message;if(manual)status.Text="Проверьте подключение к GitHub. Приложение работает офлайн.";}}
            finally{checking=false;if(!IsDisposed){previewChannel.Enabled=check.Enabled=!busy;install.Enabled=!busy&&available!=null;}}
        }
        private async Task InstallUpdate() {
            if(busy||checking||available==null)return;var update=available;SetBusy(true);updateStatus.Text="Загрузка и проверка SHA-256…";
            try{var directory=await Updates.Download(update);Updates.LaunchReplacement(directory,update.Asset.digest.Substring(7));busy=false;Close();}
            catch(Exception ex){updateStatus.Text=ex.Message;SetBusy(false);}
        }
        private async Task Smoke() {
            // Only reachable through the GitHub-hosted entry point in Program.Main.
            UiPaint.ShowForCapture(this);
            await Task.Delay(350);
            theme.SelectedIndex=2;Assert(Palette.IsDark(preferences.Theme)&&Preferences.Load().Theme=="dark","Dark theme persistence");
            items.SelectedIndex=Math.Min(3,items.Items.Count-1);string selected=Selected().Id;star.PerformClick();Assert(Selected().Id==selected,"Favorite keeps selection");star.PerformClick();
            SetBusy(true);theme.SelectedIndex=1;Assert(!apply.Enabled&&!preview.Enabled,"Theme retains operation lock");SetBusy(false);status.Text="Готово к работе";
            search.Text="__no_matching_action__";Assert(items.Items.Count==0&&!apply.Enabled&&!preview.Enabled,"Empty search actions");search.Clear();
            var journal=Path.Combine(Program.Data,"state","applied.dat");if(File.Exists(journal))throw new InvalidOperationException("Smoke requires an isolated data directory.");
            using(var file=new FileStream(journal,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){ReadHistory();Assert(historyStatus.Text.Contains("недоступен"),"Locked history handled");}File.Delete(journal);ReadHistory();
            foreach(int mode in new[]{1,2}){theme.SelectedIndex=mode;ShowPage(0);await Task.Delay(120);CaptureScreenshot(mode==2?"portable-ui.png":"portable-ui-light.png");ShowPage(2);await Task.Delay(100);CaptureScreenshot(mode==2?"portable-ui-settings-dark.png":"portable-ui-settings-light.png");}
            ShowPage(0);Size=MinimumSize;ExpandOutput(true);await Task.Delay(120);Assert(VisibleWithinParents(apply)&&VisibleWithinParents(star)&&apply.Width>=100&&details.Height>=35,"Compact layout");CaptureScreenshot("portable-ui-compact.png");
            ExpandOutput(false);
            theme.SelectedIndex=0;Assert(Preferences.Load().Theme=="system","System theme persistence");
        }
        private static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        private static bool VisibleWithinParents(Control control){var bounds=control.RectangleToScreen(control.ClientRectangle);for(var parent=control.Parent;parent!=null;parent=parent.Parent)if(!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds))return false;return true;}
        private void CaptureScreenshot(string name){Activate();Refresh();using(var bitmap=new Bitmap(Width,Height)){using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(Location,Point.Empty,Size);bitmap.Save(Path.Combine(Program.Home,name));}}
    }
}
