using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class MainForm {
        [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr PaintTestMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        static Bitmap PaintWindowFrame(Control control,Color seed){
            var image=new Bitmap(control.Width,control.Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(image)){g.Clear(seed);IntPtr dc=g.GetHdc();try{PaintTestMessage(control.Handle,0x000F,dc,IntPtr.Zero);}finally{g.ReleaseHdc(dc);}}
            return image;
        }
        async Task VerifyNativePainting(Action<bool,string> check){
            SwitchPage(0);MotionClock.FinishAll();Directory.CreateDirectory(Path.Combine(Store.Home,"preview"));
            using(var probe=new NativePaintProbe{Text="Native paint",Size=new Size(Px(210),Px(42)),Location=new Point(Px(180),Px(10))}){
                pages[0].Controls.Add(probe);ApplyTheme(probe);probe.CreateControl();probe.BringToFront();await Task.Delay(50);
                probe.BackgroundCalls=probe.ForegroundCalls=0;
                using(var first=PaintWindowFrame(probe,Color.Magenta))using(var second=PaintWindowFrame(probe,Color.Lime)){
                    check(probe.ForegroundCalls>0,"Native WM_PAINT reaches button foreground painting");
                    File.WriteAllText(Path.Combine(Store.Data,"native-paint-dispatch.txt"),"Opaque="+probe.IsOpaque+"; foreground="+probe.ForegroundCalls+"; background="+probe.BackgroundCalls);
                    first.Save(Path.Combine(Store.Home,"preview","native-paint-probe.png"),ImageFormat.Png);
                    check(SamePixels(first,second),"Real WM_PAINT overwrites every old button pixel without manually calling the background method");
                }
            }
            foreach(bool dark in new[]{true,false}){
                if(Palette.Dark!=dark)ToggleTheme();MotionClock.FinishAll();
                foreach(var button in selectionActions.Concat(new[]{quick,filter,themeButton}).Concat(navigation)){
                    using(var first=PaintWindowFrame(button,Color.Magenta))using(var second=PaintWindowFrame(button,Color.Lime))check(SamePixels(first,second)&&CornersMatchParent(button,first),(dark?"Dark":"Light")+" native button repaint clears previous content and matches the parent background: "+button.Text);
                }
            }
        }
        static void PaintPart(Control control,Graphics graphics,Rectangle clip,bool foreground){
            var args=new PaintEventArgs(graphics,clip);
            bool opaque=(bool)typeof(Control).GetMethod("GetStyle",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{ControlStyles.Opaque});
            if(!foreground||!opaque)control.GetType().GetMethod("OnPaintBackground",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{args});
            if(foreground)control.GetType().GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{args});
        }
        static Bitmap PaintFrame(Control control,Color seed,bool foreground){
            var image=new Bitmap(control.Width,control.Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(image)){g.Clear(seed);PaintPart(control,g,control.ClientRectangle,foreground);}return image;
        }
        static bool SamePixels(Bitmap first,Bitmap second){
            if(first.Size!=second.Size)return false;
            for(int y=0;y<first.Height;y++)for(int x=0;x<first.Width;x++)if(first.GetPixel(x,y)!=second.GetPixel(x,y))return false;return true;
        }
        static double PixelDifference(Bitmap first,Bitmap second){
            long difference=0;
            for(int y=0;y<first.Height;y++)for(int x=0;x<first.Width;x++){
                var a=first.GetPixel(x,y);var b=second.GetPixel(x,y);
                difference+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);
            }
            return difference/(double)(first.Width*first.Height*3);
        }
        async Task VerifyGlassLight(Action<bool,string> check){
            bool originalDark=Palette.Dark,originalGlass=Palette.GlassEnabled,originalMotion=Palette.MotionEnabled;
            try{
                Palette.GlassEnabled=true;Palette.MotionEnabled=true;SwitchPage(0);
                foreach(bool dark in new[]{false,true}){
                    Palette.Dark=dark;RefreshMaterial();
                    using(var probe=new NativePaintProbe{Text="玻璃光效 · 轻按回弹",Size=new Size(Px(260),Px(48)),Location=new Point(Px(180),Px(10))}){
                        pages[0].Controls.Add(probe);ApplyTheme(probe);probe.CreateControl();probe.BringToFront();
                        var samples=new System.Collections.Generic.List<Bitmap>();
                        try{
                            samples.Add(PaintWindowFrame(probe,Color.Magenta));
                            probe.HoverAt(new Point(probe.Width/5,probe.Height/3));
                            for(int frame=0;frame<90;frame++)probe.Advance(1.0/60);
                            samples.Add(PaintWindowFrame(probe,Color.Lime));
                            double hoverDifference=PixelDifference(samples[0],samples[1]);
                            check(hoverDifference>5,(dark?"Dark":"Light")+" hover light produces visible contrast (mean channel change "+hoverDifference.ToString("F1")+")");
                            probe.MoveLight(new Point(probe.Width*4/5,probe.Height/3));
                            using(var firstFrame=PaintWindowFrame(probe,Color.Magenta))check(SamePixels(samples[1],firstFrame),"Pointer light follows through spring updates instead of teleporting");
                            for(int frame=0;frame<90;frame++)probe.Advance(1.0/60);
                            samples.Add(PaintWindowFrame(probe,Color.Magenta));
                            check(PixelDifference(samples[1],samples[2])>6,"Moving the pointer visibly relocates the light");
                            probe.PressLight(true);
                            for(int frame=0;frame<45;frame++)probe.Advance(1.0/60);
                            samples.Add(PaintWindowFrame(probe,Color.Magenta));
                            check(probe.VisualScale<.955&&CornersMatchParent(probe,samples[3]),"Press visibly compresses the capsule without dirtying its corners");
                            probe.PressLight(false);int clicks=0;probe.Click+=(s,e)=>clicks++;probe.PerformClick();
                            double peak=0;
                            for(int frame=0;frame<10;frame++){probe.Advance(1.0/60);peak=Math.Max(peak,probe.VisualScale);}
                            samples.Add(PaintWindowFrame(probe,Color.Lime));
                            for(int frame=0;frame<170;frame++){probe.Advance(1.0/60);peak=Math.Max(peak,probe.VisualScale);}
                            check(peak>1.005&&peak<1.04&&clicks==1,"Release spring peak "+peak.ToString("F4")+" dispatches "+clicks+" action");
                            probe.LeaveLight();await Task.Delay(1100);
                            check(MotionClock.ActiveCount==0,"Light and rebound stop repainting after settling");
                            samples.Add(PaintWindowFrame(probe,Color.Magenta));
                            using(var sheet=new Bitmap(probe.Width+Px(150),probe.Height*samples.Count))using(var g=Graphics.FromImage(sheet)){
                                g.Clear(Palette.Bg);string[] labels={"静止","左侧跟光","右侧跟光","按住","松手回弹","移开后"};
                                for(int i=0;i<samples.Count;i++){
                                    TextRenderer.DrawText(g,labels[i],Font,new Rectangle(12,i*probe.Height,Px(130),probe.Height),Palette.Ink,TextFormatFlags.VerticalCenter);
                                    g.DrawImageUnscaled(samples[i],Px(150),i*probe.Height);
                                }
                                sheet.Save(Path.Combine(Store.Home,"preview",dark?"v220-light-dark.png":"v220-light-light.png"),ImageFormat.Png);
                            }
                            check(SamePixels(samples[0],samples[5]),(dark?"Dark":"Light")+" pointer exit restores the exact idle frame with no residual light");
                            Palette.MotionEnabled=false;MotionClock.FinishAll();probe.HoverAt(new Point(0,0));
                            check(MotionClock.ActiveCount==0&&probe.VisualScale==1,"Disabling motion keeps pointer lighting immediate and stationary");
                            probe.LeaveLight();
                            using(var idle=PaintWindowFrame(probe,Color.Lime))check(SamePixels(samples[0],idle),"With motion disabled, pointer exit clears the light immediately");
                            Palette.GlassEnabled=false;
                            using(var unlit=PaintWindowFrame(probe,Color.Magenta)){
                                probe.MoveLight(new Point(probe.Width,probe.Height));
                                using(var moved=PaintWindowFrame(probe,Color.Lime))check(SamePixels(unlit,moved),"Disabling glass removes pointer light");
                            }
                            Palette.GlassEnabled=true;Palette.MotionEnabled=true;
                        }finally{foreach(var sample in samples)sample.Dispose();}
                    }
                }
            }finally{Palette.Dark=originalDark;Palette.GlassEnabled=originalGlass;Palette.MotionEnabled=originalMotion;RefreshMaterial();}
        }
        static bool BackgroundMatchesParent(Control button){
            using(var child=PaintFrame(button,Color.Lime,true))return CornersMatchParent(button,child);
        }
        static bool CornersMatchParent(Control button,Bitmap child){
            using(var parent=PaintFrame(button.Parent,Color.Magenta,false)){
                foreach(var point in new[]{new Point(0,0),new Point(button.Width-1,0),new Point(0,button.Height-1),new Point(button.Width-1,button.Height-1)}){
                    var actual=child.GetPixel(point.X,point.Y);var expected=parent.GetPixel(button.Left+point.X,button.Top+point.Y);
                    if(Math.Abs(actual.R-expected.R)+Math.Abs(actual.G-expected.G)+Math.Abs(actual.B-expected.B)>6)return false;
                }return true;
            }
        }
        async Task VerifyRepainting(Action<bool,string> check){
            SwitchPage(0);MotionClock.FinishAll();
            foreach(bool dark in new[]{true,false}){
                if(Palette.Dark!=dark)ToggleTheme();MotionClock.FinishAll();
                check(selectionActions.Concat(new[]{quick}).Concat(navigation).All(BackgroundMatchesParent),(dark?"Dark":"Light")+" buttons have the same corner background as their container, with no black rectangles");
            }
            using(var probe=new UiButton{Text="Stable label",Size=new Size(Px(210),Px(42)),Location=new Point(Px(180),Px(10))}){
                pages[0].Controls.Add(probe);ApplyTheme(probe);probe.CreateControl();
                using(var painted=PaintFrame(probe,Color.Magenta,true))using(var other=PaintFrame(probe,Color.Lime,true))check(SamePixels(painted,other),"A complete button frame overwrites every old pixel");
                var offset=new Point(37,19);var clip=new Rectangle(0,0,probe.Width/2,probe.Height);
                using(var clipped=new Bitmap(probe.Width+70,probe.Height+40)){
                    using(var g=Graphics.FromImage(clipped)){g.Clear(Color.Magenta);g.TranslateTransform(offset.X,offset.Y);g.SetClip(clip);PaintPart(probe,g,clip,true);}
                    var allowed=new Rectangle(offset,clip.Size);bool contained=true;
                    for(int y=0;y<clipped.Height&&contained;y++)for(int x=0;x<clipped.Width;x++)if(!allowed.Contains(x,y)&&clipped.GetPixel(x,y).ToArgb()!=Color.Magenta.ToArgb()){contained=false;break;}
                    check(contained,"Partial translated button repaint never writes text or pixels outside its clipping rectangle");
                }
                using(var baseline=PaintFrame(probe,Color.Magenta,true)){
                    for(int cycle=0;cycle<10;cycle++){ToggleTheme();await Task.Delay(20);ToggleTheme();await Task.Delay(20);}
                    MotionClock.FinishAll();
                    using(var current=PaintFrame(probe,Color.Lime,true))check(SamePixels(baseline,current),"Ten dark/light round trips return identical button pixels");
                }
                var beforeBounds=probe.Bounds;var beforeFont=probe.Font;
                using(var rest=PaintFrame(probe,Color.Magenta,true)){
                    probe.GetType().GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(probe,new object[]{new MouseEventArgs(MouseButtons.Left,1,10,10,0)});
                    for(int frame=0;frame<18;frame++)probe.Advance(1.0/60);
                    using(var pressed=PaintFrame(probe,Color.Magenta,true)){
                        bool sameInk=true;int inkPixels=0;int ink=Palette.Ink.ToArgb();
                        for(int y=0;y<rest.Height;y++)for(int x=0;x<rest.Width;x++){bool a=rest.GetPixel(x,y).ToArgb()==ink,b=pressed.GetPixel(x,y).ToArgb()==ink;if(a)inkPixels++;if(a!=b)sameInk=false;}
                        check(inkPixels>0&&sameInk&&probe.Bounds==beforeBounds&&probe.Font==beforeFont,"Press animation keeps text pixels, font and hit target stationary");
                    }
                }
                probe.Settle();MotionClock.Remove(probe);
            }
            SwitchPage(1);SwitchPage(0);var bounds=grid.RectangleToScreen(grid.ClientRectangle);var padding=pages[0].Padding;bool stationary=true;
            for(int frame=0;frame<30;frame++){await Task.Delay(16);if(grid.RectangleToScreen(grid.ClientRectangle)!=bounds||pages[0].Padding!=padding)stationary=false;}
            check(stationary,"Page navigation never moves the live list or changes page padding during animation");
            MotionClock.FinishAll();
        }
    }
    internal class NativePaintProbe:UiButton {
        internal int BackgroundCalls,ForegroundCalls;
        internal bool IsOpaque{get{return GetStyle(ControlStyles.Opaque);}}
        internal void HoverAt(Point point){OnMouseEnter(EventArgs.Empty);MoveLight(point);}
        internal void MoveLight(Point point){OnMouseMove(new MouseEventArgs(MouseButtons.None,0,point.X,point.Y,0));}
        internal void LeaveLight(){OnMouseLeave(EventArgs.Empty);}
        internal void PressLight(bool down){var args=new MouseEventArgs(MouseButtons.Left,1,Width/2,Height/2,0);if(down)OnMouseDown(args);else OnMouseUp(args);}
        protected override void OnPaintBackground(PaintEventArgs e){BackgroundCalls++;base.OnPaintBackground(e);}
        protected override void OnPaint(PaintEventArgs e){ForegroundCalls++;base.OnPaint(e);}
    }
}
