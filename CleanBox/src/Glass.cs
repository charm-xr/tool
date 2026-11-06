using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CleanBox {
    // The material samples a cached, blurred app backdrop. No desktop capture or
    // undocumented window-composition API is required for the glass treatment.
    internal sealed class GlassScene:IDisposable {
        Bitmap background,blurred;Size size;bool dark;
        public int Generation{get;private set;}
        public void Draw(Graphics g,Size canvas,Point origin,bool frosted){
            if(!Palette.Glass){using(var b=new SolidBrush(Palette.Bg))g.FillRectangle(b,-origin.X,-origin.Y,canvas.Width,canvas.Height);return;}
            Ensure(canvas);var state=g.Save();g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            var image=frosted?blurred:background;
            using(var edges=new ImageAttributes()){
                edges.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(image,new Rectangle(-origin.X,-origin.Y,canvas.Width,canvas.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel,edges);
            }
            g.Restore(state);
        }
        void Ensure(Size canvas){
            if(background!=null&&size==canvas&&dark==Palette.Dark)return;Dispose();size=canvas;dark=Palette.Dark;Generation++;
            int w=Math.Max(8,Math.Min(600,canvas.Width/3)),h=Math.Max(8,Math.Min(400,canvas.Height/3));background=new Bitmap(w,h,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(background)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Palette.Bg);
                Glow(g,new RectangleF(-w*.35f,-h*.55f,w*1.1f,h*1.3f),Palette.Dark?Color.FromArgb(100,25,140,150):Color.FromArgb(170,157,225,226));
                Glow(g,new RectangleF(w*.45f,-h*.30f,w*.95f,h*.95f),Palette.Dark?Color.FromArgb(95,92,65,160):Color.FromArgb(160,190,182,239));
                Glow(g,new RectangleF(w*.1f,h*.58f,w*1.0f,h*.9f),Palette.Dark?Color.FromArgb(60,35,113,156):Color.FromArgb(145,218,207,239));
                Glow(g,new RectangleF(w*.7f,h*.52f,w*.7f,h*.9f),Palette.Dark?Color.FromArgb(38,145,99,88):Color.FromArgb(140,249,223,205));
                // A shared top tone joins the native caption to the app backdrop.
                var top=new RectangleF(0,0,w,Math.Max(12,h*.25f));
                using(var fade=new LinearGradientBrush(top,Palette.WindowTop,Color.FromArgb(0,Palette.WindowTop),LinearGradientMode.Vertical)){
                    fade.InterpolationColors=new ColorBlend{Positions=new[]{0f,.12f,1f},Colors=new[]{Palette.WindowTop,Palette.WindowTop,Color.FromArgb(0,Palette.WindowTop)}};
                    g.FillRectangle(fade,top);
                }
            }
            blurred=(Bitmap)background.Clone();Blur(blurred,6);
        }
        static void Glow(Graphics g,RectangleF box,Color color){using(var path=new GraphicsPath()){path.AddEllipse(box);using(var brush=new PathGradientBrush(path)){brush.CenterColor=color;brush.SurroundColors=new[]{Color.FromArgb(0,color)};g.FillPath(brush,path);}}}
        static void Blur(Bitmap bitmap,int radius){
            int w=bitmap.Width,h=bitmap.Height;var data=bitmap.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            try{
                byte[] pixels=new byte[data.Stride*h],temp=new byte[data.Stride*h];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                for(int pass=0;pass<2;pass++){
                    for(int y=0;y<h;y++)for(int channel=0;channel<3;channel++){
                        int sum=0;for(int k=-radius;k<=radius;k++)sum+=pixels[y*data.Stride+Math.Max(0,Math.Min(w-1,k))*4+channel];
                        for(int x=0;x<w;x++){temp[y*data.Stride+x*4+channel]=(byte)(sum/(radius*2+1));sum-=pixels[y*data.Stride+Math.Max(0,x-radius)*4+channel];sum+=pixels[y*data.Stride+Math.Min(w-1,x+radius+1)*4+channel];}
                    }
                    for(int x=0;x<w;x++)for(int channel=0;channel<3;channel++){
                        int sum=0;for(int k=-radius;k<=radius;k++)sum+=temp[Math.Max(0,Math.Min(h-1,k))*data.Stride+x*4+channel];
                        for(int y=0;y<h;y++){pixels[y*data.Stride+x*4+channel]=(byte)(sum/(radius*2+1));sum-=temp[Math.Max(0,y-radius)*data.Stride+x*4+channel];sum+=temp[Math.Min(h-1,y+radius+1)*data.Stride+x*4+channel];}
                    }
                }
                Marshal.Copy(pixels,0,data.Scan0,pixels.Length);
            }finally{bitmap.UnlockBits(data);}
        }
        public void Dispose(){if(background!=null)background.Dispose();if(blurred!=null)blurred.Dispose();background=null;blurred=null;}
    }
    internal static class GlassPaint {
        static Point Origin(Control control){Point origin=Point.Empty;while(control!=null&&!(control is Form)){origin.Offset(control.Left,control.Top);control=control.Parent;}return origin;}
        // Compose only ancestor materials, never their child controls or text. Native
        // ButtonBase transparency can leave undefined pixels during partial repaints.
        public static void Underlay(Control control,Graphics g){
            Point origin=Origin(control);var ancestors=new Stack<SurfacePanel>();bool cachedRow=false;
            for(Control parent=control.Parent;parent!=null;parent=parent.Parent){var surface=parent as SurfacePanel;if(surface!=null)ancestors.Push(surface);if(parent is SettingsGlassRow){cachedRow=true;break;}}
            // Settings rows contain the complete composited background. Their
            // descendants share that frame instead of replaying every ancestor.
            if(!cachedRow)Backdrop(control,g,false);
            foreach(var surface in ancestors){Point at=Origin(surface);var state=g.Save();g.TranslateTransform(at.X-origin.X,at.Y-origin.Y);surface.PaintMaterial(g);g.Restore(state);}
        }
        public static void Backdrop(Control control,Graphics g,bool frosted){
            var form=control.FindForm() as MainForm;if(form==null){using(var b=new SolidBrush(Palette.Bg))g.FillRectangle(b,control.ClientRectangle);return;}
            Point origin=Point.Empty;Control node=control;while(node!=null&&node!=form){origin.Offset(node.Left,node.Top);node=node.Parent;}form.Scene.Draw(g,form.ClientSize,origin,frosted);
        }
        public static void Surface(Control owner,Graphics g,RectangleF rect,float radius,float emphasis,Bitmap frostedBackdrop=null){
            if(rect.Width<2||rect.Height<2)return;var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
            for(int i=3;i>0;i--){var shadow=rect;shadow.Offset(0,i);shadow.Inflate(i*.6f,i*.4f);using(var p=Palette.Shape(shadow,radius))using(var b=new SolidBrush(Color.FromArgb(Palette.Dark?8:5,15,36,67)))g.FillPath(b,p);}
            using(var path=Palette.Shape(rect,radius)){
                g.SetClip(path,CombineMode.Intersect);if(frostedBackdrop==null)Backdrop(owner,g,true);else g.DrawImageUnscaled(frostedBackdrop,Point.Empty);
                using(var tint=new SolidBrush(Palette.Glass?Color.FromArgb(Palette.Dark?162:122,Palette.Surface):Palette.Surface))g.FillPath(tint,path);
                using(var wash=new LinearGradientBrush(rect,Color.FromArgb(Palette.Dark?26:112,Color.White),Color.FromArgb(Palette.Dark?3:28,Palette.Dark?Color.White:Color.FromArgb(44,149,178)),LinearGradientMode.Vertical))g.FillPath(wash,path);
                if(emphasis>0)using(var b=new SolidBrush(Color.FromArgb((int)(Math.Min(1,emphasis)*30),Palette.Accent)))g.FillPath(b,path);
                g.Restore(state);state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var edge=new LinearGradientBrush(rect,Color.FromArgb(Palette.Dark?65:220,Color.White),Color.FromArgb(Palette.Dark?60:80,Palette.Accent),LinearGradientMode.ForwardDiagonal))using(var pen=new Pen(edge,1.2f))g.DrawPath(pen,path);
            }g.Restore(state);
        }
        // Light stays inside the capsule. The foreground text is drawn afterwards
        // at its original position, so the spring never moves the label or hit box.
        public static void ButtonLight(Graphics g,RectangleF rect,float radius,PointF position,float hover,float pulse,bool primary,float maxLightWidth=0){
            float intensity=Math.Max(0,Math.Min(1,Math.Max(hover,pulse)));
            // Pointer lighting has no idle brightness: every layer fades to zero.
            if(intensity<=.001f)return;
            float x=rect.Left+rect.Width*Math.Max(.05f,Math.Min(.95f,position.X));
            float y=rect.Top+rect.Height*Math.Max(.1f,Math.Min(.9f,position.Y));
            float span=maxLightWidth>0?Math.Min(rect.Width,maxLightWidth):rect.Width;
            var state=g.Save();
            using(var path=Palette.Shape(rect,radius)){
                g.SetClip(path,CombineMode.Intersect);
                Color cyan=Palette.Dark?Color.FromArgb(100,231,245):Color.FromArgb(20,161,204);
                Color violet=Palette.Dark?Color.FromArgb(172,158,255):Color.FromArgb(121,99,218);
                int colorAlpha=(int)(intensity*(Palette.Dark?108:115));
                if(primary)colorAlpha/=2;
                SoftLight(g,new RectangleF(x-span*.37f,y-rect.Height*.9f,span*.74f,rect.Height*1.8f),Color.FromArgb(colorAlpha,cyan));
                float violetLeft=maxLightWidth>0?x+span*.03f:rect.Right-rect.Width*.42f-(x-rect.Left)*.23f;
                SoftLight(g,new RectangleF(violetLeft,rect.Top+rect.Height*.16f,span*.63f,rect.Height*1.5f),Color.FromArgb(colorAlpha*2/3,violet));

                float shineWidth=span*(.33f+.14f*pulse);
                int shineAlpha=(int)(intensity*(primary?79:230));
                SoftLight(g,new RectangleF(x-shineWidth/2,rect.Top-rect.Height*.4f+position.Y*rect.Height*.32f,shineWidth,rect.Height*1.22f),Color.FromArgb(shineAlpha,Color.White));
                if(pulse>.01f){
                    float waveWidth=span*(.2f+(1-pulse)*1.4f);
                    SoftLight(g,new RectangleF(x-waveWidth/2,y-rect.Height, waveWidth,rect.Height*2),Color.FromArgb((int)(pulse*(primary?45:80)),Color.White));
                }

                float focus=Math.Max(.12f,Math.Min(.88f,position.X));
                using(var edge=new LinearGradientBrush(rect,Color.White,Color.White,LinearGradientMode.Horizontal)){
                    edge.InterpolationColors=new ColorBlend{
                        Positions=new[]{0f,focus-.11f,focus,focus+.11f,1f},
                        Colors=new[]{Color.FromArgb((int)(20*intensity),cyan),Color.FromArgb((int)(170*intensity),cyan),Color.FromArgb((int)(255*intensity),Color.White),Color.FromArgb((int)(165*intensity),violet),Color.FromArgb((int)(20*intensity),violet)}
                    };
                    using(var pen=new Pen(edge,Math.Max(1.2f,g.DpiX/96f*1.5f)))g.DrawPath(pen,path);
                }
            }
            g.Restore(state);
        }
        static void SoftLight(Graphics g,RectangleF bounds,Color center){
            using(var ellipse=new GraphicsPath()){
                ellipse.AddEllipse(bounds);
                using(var glow=new PathGradientBrush(ellipse)){
                    glow.CenterColor=center;
                    glow.SurroundColors=new[]{Color.FromArgb(0,center)};
                    g.FillPath(glow,ellipse);
                }
            }
        }
    }
    internal class GlassMenuRenderer:ToolStripProfessionalRenderer {
        public GlassMenuRenderer(){RoundedEdges=false;}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){var r=new Rectangle(0,0,e.ToolStrip.Width,e.ToolStrip.Height);using(var b=new LinearGradientBrush(r,Palette.Surface,Palette.Side,LinearGradientMode.Vertical))e.Graphics.FillRectangle(b,r);}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var p=Palette.Shape(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),12))using(var pen=new Pen(Palette.Line))e.Graphics.DrawPath(pen,p);}
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(!e.Item.Selected&&!e.Item.Pressed)return;var r=new RectangleF(3,2,e.Item.Width-6,e.Item.Height-4);using(var p=Palette.Shape(r,9))using(var b=new SolidBrush(Palette.Soft))e.Graphics.FillPath(b,p);}
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?Palette.Ink:Palette.Muted;base.OnRenderItemText(e);}
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var p=new Pen(Palette.Line))e.Graphics.DrawLine(p,12,e.Item.Height/2,e.Item.Width-12,e.Item.Height/2);}
    }
    internal class InputFrame:Panel {
        public InputFrame(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Palette.Bg;Padding=new Padding(14,4,14,4);}
        protected override void OnPaintBackground(PaintEventArgs e){GlassPaint.Underlay(this,e.Graphics);float dpi=e.Graphics.DpiX/96f;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var rect=new RectangleF(1,1,Math.Max(1,Width-2),Math.Max(1,Height-2));using(var p=Palette.Shape(rect,rect.Height/2)){using(var b=new SolidBrush(Palette.Surface))e.Graphics.FillPath(b,p);bool focused=ContainsFocus;using(var pen=new Pen(focused?Palette.Accent:Color.FromArgb(Palette.Dark?75:185,Palette.Dark?Palette.Muted:Color.White),focused?1.5f*dpi:1f))e.Graphics.DrawPath(pen,p);}}
    }
    internal class CanvasPanel:Panel {
        public CanvasPanel(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Palette.Bg;}
        protected override void OnPaintBackground(PaintEventArgs e){GlassPaint.Underlay(this,e.Graphics);}
    }
    internal class CanvasFlowPanel:FlowLayoutPanel {
        public CanvasFlowPanel(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Palette.Bg;}
        protected override void OnPaintBackground(PaintEventArgs e){GlassPaint.Underlay(this,e.Graphics);}
    }
    internal class CanvasTablePanel:TableLayoutPanel {
        public CanvasTablePanel(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Palette.Bg;}
        protected override void OnPaintBackground(PaintEventArgs e){GlassPaint.Underlay(this,e.Graphics);}
    }
    internal static class GlassMenus {
        public static void Style(ContextMenuStrip menu){menu.Renderer=new GlassMenuRenderer();menu.BackColor=Palette.Surface;menu.ForeColor=Palette.Ink;menu.Padding=new Padding(6);menu.ShowImageMargin=false;foreach(ToolStripItem item in menu.Items)item.Padding=new Padding(10,6,10,6);
            if((menu.Tag as string)!="CleanBox.GlassMenu"){menu.Tag="CleanBox.GlassMenu";menu.Opened+=(s,e)=>{using(var p=Palette.Shape(new RectangleF(0,0,menu.Width,menu.Height),12)){var old=menu.Region;menu.Region=new Region(p);if(old!=null)old.Dispose();}};}
        }
    }
}
