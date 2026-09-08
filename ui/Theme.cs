using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Wintools {
    internal sealed class Palette {
        internal bool Dark;
        internal Color Background, Surface, Raised, Border, Text, Muted, Accent, AccentText, Selection, Danger;
        internal static Palette Create(bool dark) {
            if(SystemInformation.HighContrast)return new Palette{Dark=dark,Background=SystemColors.Window,Surface=SystemColors.Window,Raised=SystemColors.Control,Border=SystemColors.WindowText,Text=SystemColors.WindowText,Muted=SystemColors.WindowText,Accent=SystemColors.Highlight,AccentText=SystemColors.HighlightText,Selection=SystemColors.Highlight,Danger=SystemColors.WindowText};
            return dark
                ? new Palette{Dark=true,Background=Color.FromArgb(20,23,29),Surface=Color.FromArgb(29,34,43),Raised=Color.FromArgb(39,45,56),Border=Color.FromArgb(57,65,79),Text=Color.FromArgb(238,241,248),Muted=Color.FromArgb(174,184,201),Accent=Color.FromArgb(166,178,255),AccentText=Color.FromArgb(21,27,49),Selection=Color.FromArgb(48,59,85),Danger=Color.FromArgb(255,181,177)}
                : new Palette{Background=Color.FromArgb(244,246,250),Surface=Color.White,Raised=Color.FromArgb(235,239,247),Border=Color.FromArgb(215,222,232),Text=Color.FromArgb(26,35,53),Muted=Color.FromArgb(83,99,122),Accent=Color.FromArgb(65,82,203),AccentText=Color.White,Selection=Color.FromArgb(230,235,255),Danger=Color.FromArgb(163,39,49)};
        }
        internal static bool IsDark(string mode) {
            if(mode=="dark")return true;if(mode=="light")return false;
            try {using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))return key!=null&&Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;}
            catch{return false;}
        }
    }
    internal static class UiPaint {
        internal static GraphicsPath Round(Rectangle rectangle,int radius) {
            var path=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height));
            if(d<2){path.AddRectangle(rectangle);return path;}
            path.AddArc(rectangle.Left,rectangle.Top,d,d,180,90);path.AddArc(rectangle.Right-d,rectangle.Top,d,d,270,90);
            path.AddArc(rectangle.Right-d,rectangle.Bottom-d,d,d,0,90);path.AddArc(rectangle.Left,rectangle.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
        internal static void ShowForCapture(Form form){ShowWindow(form.Handle,5);form.TopMost=true;form.Activate();}
        internal static void TitleBar(Form form,bool dark){try{int value=dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref value,sizeof(int));}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
    }
    internal sealed class ModernButton : Button {
        internal Palette Palette=Palette.Create(false);
        internal bool Primary,Chosen;
        private bool hover;
        internal ModernButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;Height=40;Padding=new Padding(12,0,12,0);Margin=new Padding(0,0,8,8);SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnPaint(PaintEventArgs e) {
            var p=Palette;var back=Primary?p.Accent:Chosen?p.Selection:hover?p.Raised:p.Surface;
            var foreground=Enabled?(Primary?p.AccentText:p.Text):p.Muted;
            if(!Enabled)back=p.Raised;
            e.Graphics.Clear(Parent==null?p.Background:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=UiPaint.Round(new Rectangle(0,0,Width-1,Height-1),8))using(var brush=new SolidBrush(back))using(var pen=new Pen(Chosen?p.Accent:p.Border)){e.Graphics.FillPath(brush,shape);if(!Primary)e.Graphics.DrawPath(pen,shape);}
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(10,0,Width-20,Height),foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-11,Height-11),foreground,back);
        }
    }
    internal sealed class BufferedPanel : Panel {
        internal BufferedPanel(){DoubleBuffered=true;ResizeRedraw=true;}
    }
}
