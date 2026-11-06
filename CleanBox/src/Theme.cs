using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace CleanBox {
    internal static class Palette {
        public static bool Dark,GlassEnabled=true,MotionEnabled=true;
        public static bool Glass{get{return GlassEnabled&&!SystemInformation.HighContrast;}}
        public static bool Animate{get{return MotionEnabled&&!SystemInformation.HighContrast;}}
        public static Color Ink {get{return Dark?Color.FromArgb(229,237,243):Color.FromArgb(32,48,61);}}
        public static Color Muted {get{return Dark?Color.FromArgb(170,184,195):Color.FromArgb(99,115,128);}}
        public static Color Accent {get{return Dark?Color.FromArgb(102,214,196):Color.FromArgb(8,123,115);}}
        public static Color OnAccent {get{return Dark?Color.FromArgb(15,42,37):Color.White;}}
        public static Color Bg {get{return Dark?Color.FromArgb(15,24,37):Color.FromArgb(230,239,248);}}
        public static Color WindowTop {get{return Glass?(Dark?Color.FromArgb(20,42,55):Color.FromArgb(216,237,244)):Bg;}}
        public static Color Surface {get{return Dark?Color.FromArgb(31,42,57):Color.FromArgb(248,251,255);}}
        public static Color Side {get{return Dark?Color.FromArgb(29,43,57):Color.FromArgb(235,244,249);}}
        public static Color Line {get{return Dark?Color.FromArgb(57,67,77):Color.FromArgb(222,229,235);}}
        public static Color Soft {get{return Dark?Color.FromArgb(35,63,58):Color.FromArgb(225,241,237);}}
        public static Color Danger {get{return Dark?Color.FromArgb(255,157,174):Color.FromArgb(174,52,70);}}
        public static Color Blend(Color a,Color b,double amount){amount=Math.Max(0,Math.Min(1,amount));return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));}
        public static GraphicsPath Shape(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));if(d<=0){p.AddRectangle(r);return p;}p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    }
    internal class UiButton:Button,IMotionVisual {
        public string Kind="secondary",Glyph="";
        readonly SpringValue scaleSpring=new SpringValue(1),hoverSpring=new SpringValue(0),selectionSpring=new SpringValue(0);
        readonly SpringValue lightX=new SpringValue(.35,220,19),lightY=new SpringValue(.25,220,19);
        readonly SpringValue lightPulse=new SpringValue(0,180,20);
        bool selected,hover,pressed;
        public bool Selected{get{return selected;}set{if(selected==value)return;selected=value;selectionSpring.Target=value?1:0;MotionClock.Wake(this);Invalidate();}}
        internal double VisualScale{get{return scaleSpring.Value;}}
        public UiButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;Padding=new Padding(16,0,16,0);SetStyle(ControlStyles.Opaque|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Palette.Bg;}
        // ButtonBase is opaque: WM_PAINT skips OnPaintBackground. The complete
        // background must be painted by OnPaint, in the same buffered frame.
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnMouseEnter(EventArgs e){hover=true;hoverSpring.Target=1;MotionClock.Wake(this);base.OnMouseEnter(e);}
        protected override void OnMouseMove(MouseEventArgs e){
            lightX.Target=Math.Max(0,Math.Min(1,e.X/(double)Math.Max(1,Width)));
            lightY.Target=Math.Max(0,Math.Min(1,e.Y/(double)Math.Max(1,Height)));
            if(Palette.Glass)MotionClock.Wake(this);
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e){hover=false;hoverSpring.Target=0;MotionClock.Wake(this);base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left&&Enabled)Press(true);base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){Press(false);base.OnMouseUp(e);}
        protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture)Press(false);base.OnMouseCaptureChanged(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space&&Enabled)Press(true);base.OnKeyDown(e);}
        protected override void OnKeyUp(KeyEventArgs e){if(e.KeyCode==Keys.Space)Press(false);base.OnKeyUp(e);}
        protected override void OnClick(EventArgs e){
            if(Enabled&&Palette.Animate){
                scaleSpring.Value=.955;scaleSpring.Target=1;scaleSpring.Velocity=1.2;
                lightPulse.Snap(1);lightPulse.Target=0;
                if(!hover){lightX.Snap(.5);lightY.Snap(.5);}
                MotionClock.Wake(this);
            }
            base.OnClick(e);
        }
        void Press(bool value){
            if(pressed==value)return;
            pressed=value;scaleSpring.Target=value?.945:1;
            if(!value&&Palette.Animate)scaleSpring.Velocity=Math.Max(.5,scaleSpring.Velocity);
            MotionClock.Wake(this);
        }
        protected override void OnEnabledChanged(EventArgs e){if(!Enabled){hover=false;pressed=false;hoverSpring.Target=0;Settle();MotionClock.Remove(this);}base.OnEnabledChanged(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Press(false);Invalidate();base.OnLostFocus(e);}
        public override Size GetPreferredSize(Size proposedSize){var s=TextRenderer.MeasureText(Text,Font);return new Size(Math.Max(MinimumSize.Width,s.Width+Padding.Horizontal+(Glyph.Length>0?28:0)+8),Math.Max(MinimumSize.Height,s.Height+20));}
        public Control MotionControl{get{return this;}}
        public bool Advance(double seconds){
            bool moving=scaleSpring.Step(seconds);
            moving=hoverSpring.Step(seconds)||moving;
            moving=selectionSpring.Step(seconds)||moving;
            moving=lightX.Step(seconds)||moving;
            moving=lightY.Step(seconds)||moving;
            moving=lightPulse.Step(seconds)||moving;
            Invalidate();return moving;
        }
        public void Settle(){
            scaleSpring.Snap(1);hoverSpring.Snap(hover?1:0);selectionSpring.Snap(selected?1:0);
            lightX.Snap(lightX.Target);lightY.Snap(lightY.Target);lightPulse.Snap(0);Invalidate();
        }
        protected override void Dispose(bool disposing){if(disposing)MotionClock.Remove(this);base.Dispose(disposing);}
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;GlassPaint.Underlay(this,g);g.SmoothingMode=SmoothingMode.AntiAlias;float dpi=g.DpiX/96f;
            float zoom=(float)Math.Max(.94,Math.Min(1.025,scaleSpring.Value)),lift=(float)Math.Max(0,Math.Min(1,hoverSpring.Value));
            float inset=3*dpi;var rect=new RectangleF(inset,inset,Math.Max(1,Width-inset*2),Math.Max(1,Height-inset*2-1));
            var contentRect=rect;rect.Inflate(-rect.Width*(1-zoom)/2,-rect.Height*(1-zoom)/2);
            float radius=Math.Min(rect.Height/2,Kind=="nav"?18*dpi:22*dpi);bool primary=Kind=="primary",nav=Kind=="nav";
            Color fg=!Enabled?Palette.Muted:primary?Palette.OnAccent:Kind=="danger"?Palette.Danger:selected?Palette.Accent:Palette.Ink;
            if(primary&&Enabled){
                using(var p=Palette.Shape(rect,radius)){
                    var shade=rect;shade.Offset(0,2*dpi);using(var shadow=Palette.Shape(shade,radius))using(var b=new SolidBrush(Color.FromArgb(32,Palette.Accent)))g.FillPath(b,shadow);
                    using(var b=new LinearGradientBrush(rect,Palette.Blend(Palette.Accent,Color.White,Palette.Dark?.10:.17),Palette.Accent,LinearGradientMode.ForwardDiagonal))g.FillPath(b,p);
                    using(var edge=new Pen(Color.FromArgb(110,Color.White)))g.DrawPath(edge,p);
                }
            }else if(!nav||selected||lift>.01){GlassPaint.Surface(this,g,rect,radius,(float)Math.Max(0,selectionSpring.Value));}
            if(Palette.Glass&&Enabled&&(lift>.001||lightPulse.Value>.001)){
                GlassPaint.ButtonLight(g,rect,radius,new PointF((float)lightX.Value,(float)lightY.Value),lift,(float)Math.Max(0,lightPulse.Value),primary);
            }
            int left=(int)(contentRect.Left+13*dpi),right=(int)(contentRect.Right-13*dpi);var textRect=new Rectangle(left,(int)contentRect.Top,Math.Max(1,right-left),(int)contentRect.Height);
            const TextFormatFlags preserve=TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
            if(Glyph.Length>0){using(var f=new Font("Segoe MDL2 Assets",11f))TextRenderer.DrawText(g,Glyph,f,new Rectangle(left,(int)contentRect.Top,(int)(22*dpi),(int)contentRect.Height),fg,preserve|TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding);textRect.X+=(int)(27*dpi);textRect.Width-=(int)(27*dpi);}
            TextRenderer.DrawText(g,Text,Font,textRect,fg,preserve|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|(nav?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
            if(Focused&&ShowFocusCues){var focus=Rectangle.Round(rect);focus.Inflate(-4,-4);ControlPaint.DrawFocusRectangle(g,focus,Palette.Accent,Palette.Surface);}
        }
    }
    internal class SurfacePanel:CanvasPanel {
        public SurfacePanel(){Padding=new Padding(18);Tag="surface";}
        protected override void OnPaintBackground(PaintEventArgs e){base.OnPaintBackground(e);PaintMaterial(e.Graphics);}
        internal void PaintMaterial(Graphics g){float dpi=g.DpiX/96f;GlassPaint.Surface(this,g,new RectangleF(3*dpi,3*dpi,Math.Max(1,Width-6*dpi),Math.Max(1,Height-7*dpi)),22*dpi,0);}
    }
    internal class ChoiceButton:UiButton {
        public readonly List<object> Items=new List<object>();readonly ContextMenuStrip choices=new ContextMenuStrip();int selected=-1;public event EventHandler SelectedIndexChanged;
        public int SelectedIndex {get{return selected;}set{if(value==selected)return;selected=value;Text=value>=0&&value<Items.Count?Items[value]+"  ▾":"选择来源";var handler=SelectedIndexChanged;if(handler!=null)handler(this,EventArgs.Empty);}}
        public ChoiceButton(){AutoSize=false;MinimumSize=Size.Empty;AccessibleName="筛选软件来源";Click+=(s,e)=>{choices.Items.Clear();for(int i=0;i<Items.Count;i++){int index=i;var item=new ToolStripMenuItem(Items[i].ToString()){Checked=i==SelectedIndex};item.Click+=(sender,args)=>SelectedIndex=index;choices.Items.Add(item);}GlassMenus.Style(choices);choices.Show(this,new Point(0,Height));};}
        protected override void Dispose(bool disposing){if(disposing)choices.Dispose();base.Dispose(disposing);}
    }
}
