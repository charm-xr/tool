using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace CleanBox {
    internal static class Palette {
        public static bool Dark;
        public static Color Ink {get{return Dark?Color.FromArgb(229,237,243):Color.FromArgb(32,48,61);}}
        public static Color Muted {get{return Dark?Color.FromArgb(170,184,195):Color.FromArgb(99,115,128);}}
        public static Color Accent {get{return Dark?Color.FromArgb(102,214,196):Color.FromArgb(8,123,115);}}
        public static Color OnAccent {get{return Dark?Color.FromArgb(15,42,37):Color.White;}}
        public static Color Bg {get{return Dark?Color.FromArgb(23,27,32):Color.FromArgb(246,248,250);}}
        public static Color Surface {get{return Dark?Color.FromArgb(34,39,46):Color.White;}}
        public static Color Side {get{return Dark?Color.FromArgb(28,33,39):Color.FromArgb(237,241,244);}}
        public static Color Line {get{return Dark?Color.FromArgb(57,67,77):Color.FromArgb(222,229,235);}}
        public static Color Soft {get{return Dark?Color.FromArgb(35,63,58):Color.FromArgb(225,241,237);}}
        public static Color Danger {get{return Dark?Color.FromArgb(255,157,174):Color.FromArgb(174,52,70);}}
        public static GraphicsPath Shape(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));if(d<=0){p.AddRectangle(r);return p;}p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    }
    internal class UiButton:Button {
        public string Kind="secondary",Glyph="";public bool Selected;bool hover;
        public UiButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;Padding=new Padding(12,0,12,0);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        public override Size GetPreferredSize(Size proposedSize){var s=TextRenderer.MeasureText(Text,Font);return new Size(Math.Max(MinimumSize.Width,s.Width+Padding.Horizontal+(Glyph.Length>0?28:0)),Math.Max(MinimumSize.Height,s.Height+16));}
        protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Palette.Bg:Parent.BackColor);float scale=g.DpiX/96f;bool primary=Kind=="primary",nav=Kind=="nav";Color fill=primary?Palette.Accent:Selected?Palette.Soft:nav?Palette.Side:Palette.Surface;if(hover&&Enabled&&!primary)fill=Palette.Soft;Color fg=!Enabled?Palette.Muted:primary?Palette.OnAccent:Kind=="danger"?Palette.Danger:Selected?Palette.Accent:Palette.Ink;if(!Enabled)fill=Palette.Side;
            using(var p=Palette.Shape(new RectangleF(.5f,.5f,Width-1,Height-1),7*scale)){using(var b=new SolidBrush(fill))g.FillPath(b,p);if(!nav&&!primary)using(var pen=new Pen(Palette.Line))g.DrawPath(pen,p);}
            int left=(int)(12*scale);if(Glyph.Length>0){using(var f=new Font("Segoe MDL2 Assets",11f))TextRenderer.DrawText(g,Glyph,f,new Rectangle(left,0,(int)(23*scale),Height),fg,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding);left+=(int)(28*scale);}
            TextRenderer.DrawText(g,Text,Font,new Rectangle(left,0,Math.Max(0,Width-left-(int)(10*scale)),Height),fg,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|(nav?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(5,5,Width-10,Height-10),fg,fill);
        }
    }
    internal class SurfacePanel:Panel {public SurfacePanel(){DoubleBuffered=true;ResizeRedraw=true;Padding=new Padding(16);Tag="surface";}protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Palette.Line))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);}}
    internal class ChoiceButton:UiButton {
        public readonly List<object> Items=new List<object>();readonly ContextMenuStrip choices=new ContextMenuStrip();int selected=-1;public event EventHandler SelectedIndexChanged;
        public int SelectedIndex {get{return selected;}set{if(value==selected)return;selected=value;Text=value>=0&&value<Items.Count?Items[value]+"  ▾":"选择来源";var handler=SelectedIndexChanged;if(handler!=null)handler(this,EventArgs.Empty);}}
        public ChoiceButton(){AutoSize=false;MinimumSize=Size.Empty;AccessibleName="筛选软件来源";Click+=(s,e)=>{choices.Items.Clear();choices.BackColor=Palette.Surface;choices.ForeColor=Palette.Ink;choices.RenderMode=ToolStripRenderMode.System;for(int i=0;i<Items.Count;i++){int index=i;var item=new ToolStripMenuItem(Items[i].ToString()){Checked=i==SelectedIndex};item.Click+=(sender,args)=>SelectedIndex=index;choices.Items.Add(item);}choices.Show(this,new Point(0,Height));};}
        protected override void Dispose(bool disposing){if(disposing)choices.Dispose();base.Dispose(disposing);}
    }
    internal class SmoothGrid:DataGridView {public SmoothGrid(){DoubleBuffered=true;}}
}
