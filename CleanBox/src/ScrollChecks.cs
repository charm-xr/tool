using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class MainForm {
        async Task VerifyScrollbars(Action<bool,string> check){
            bool originalDark=Palette.Dark;
            try{
                // A hidden startup window has no paintable region. Real-time
                // presentation checks must use a visible HWND, not WM_PRINT.
                if(!ScrollPaintWitness.IsWindowVisible(Handle)){Hide();Show();await Task.Delay(100);}
                check(ScrollPaintWitness.IsWindowVisible(Handle),"Live drag checks use a visible window that can receive WM_PAINT");
                foreach(bool dark in new[]{true,false}){
                    Palette.Dark=dark;RefreshMaterial();SwitchPage(0);ScrollSkin.RefreshTree(this);await Task.Delay(100);
                    var gridSkin=ScrollSkin.Find(grid);
                    check(gridSkin!=null&&gridSkin.Vertical.Visible,"Software grid has an independent themed scrollbar");
                    CheckLiveDrag(grid,gridSkin.Vertical,false,check);
                    CheckScrollInteraction(grid,gridSkin.Vertical,false,dark,check);
                    using(var box=new CanvasPanel{Size=new Size(Px(760),Px(240)),Location=new Point(Px(10),Px(100))}){
                        var text=new TextBox{Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Both,BorderStyle=BorderStyle.None,Location=new Point(0,0),Size=new Size(Px(180),Px(210)),Text=string.Join("\r\n",Enumerable.Range(0,100).Select(i=>i+" "+new string('x',160)))};
                        var list=new ListBox{Location=new Point(Px(190),0),Size=new Size(Px(180),Px(210)),IntegralHeight=false,BorderStyle=BorderStyle.None,HorizontalScrollbar=true,HorizontalExtent=Px(800)};
                        var checkedList=new CheckedListBox{Location=new Point(Px(380),0),Size=new Size(Px(180),Px(210)),IntegralHeight=false,BorderStyle=BorderStyle.None};
                        var wideGrid=new SmoothGrid{Location=new Point(Px(570),0),Size=new Size(Px(180),Px(210)),BorderStyle=BorderStyle.None,RowHeadersVisible=false,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None};
                        wideGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="宽列",Width=Px(600)});
                        for(int i=0;i<80;i++){list.Items.Add("长路径测试 "+i+new string('x',60));checkedList.Items.Add("磁盘选项 "+i);wideGrid.Rows.Add("滚动测试 "+i);}
                        box.Controls.AddRange(new Control[]{text,list,checkedList,wideGrid});pages[0].Controls.Add(box);box.BringToFront();ApplyTheme(box);box.CreateControl();await Task.Delay(100);ScrollSkin.RefreshTree(box);
                        foreach(Control target in new Control[]{text,list,checkedList,wideGrid}){
                            var binding=ScrollSkin.Find(target);
                            check(binding!=null&&binding.Vertical.Visible,(dark?"Dark ":"Light ")+target.GetType().Name+" has a themed vertical scrollbar");
                            CheckLiveDrag(target,binding.Vertical,false,check);
                            CheckScrollInteraction(target,binding.Vertical,false,dark,check);
                        }
                        foreach(Control target in new Control[]{text,list,wideGrid}){
                            var binding=ScrollSkin.Find(target);check(binding.Horizontal.Visible&&binding.Corner.Visible,"Horizontal scrollbar and corner share the theme");
                            CheckLiveDrag(target,binding.Horizontal,true,check);
                            CheckScrollInteraction(target,binding.Horizontal,true,dark,check);CheckScrollVisual(binding.Corner,dark,check);
                        }
                        // Native wheel/keyboard/programmatic scrolling still moves
                        // the custom thumb, without an idle polling timer.
                        var textSkin=ScrollSkin.Find(text);textSkin.Move(false,0);var before=textSkin.Vertical.ThumbBounds;
                        ScrollNative.SendMessage(text.Handle,0xB6,IntPtr.Zero,new IntPtr(30));await Task.Delay(60);
                        check(textSkin.Vertical.ThumbBounds.Top>before.Top,"Native text scrolling synchronizes the managed thumb");
                        var listSkin=ScrollSkin.Find(list);listSkin.Move(false,0);before=listSkin.Vertical.ThumbBounds;list.TopIndex=30;await Task.Delay(60);
                        check(listSkin.Vertical.ThumbBounds.Top>before.Top,"ListBox TopIndex synchronizes the managed thumb");
                        foreach(Control target in new Control[]{text,list,checkedList,wideGrid}){
                            var binding=ScrollSkin.Find(target);var size=binding.Host.Size;
                            for(int i=0;i<6;i++){binding.Host.Size=new Size(size.Width-Px(i%2*25),size.Height-Px(i%2*30));binding.Refresh();check(binding.NativeBarsAreClipped(),"Native scrollbar remains outside the viewport while resizing "+target.GetType().Name);}
                            binding.Host.Size=size;binding.Refresh();
                        }
                        box.Hide();box.Show();ScrollSkin.RefreshTree(box);await Task.Delay(100);
                        check(textSkin.NativeBarsAreClipped()&&listSkin.NativeBarsAreClipped(),"Hide/show does not reveal native scrollbar strips");
                        using(var image=new Bitmap(box.Width,box.Height)){
                            box.DrawToBitmap(image,box.ClientRectangle);image.Save(Path.Combine(Store.Home,"preview","v220-scroll-fixtures-"+(dark?"dark":"light")+".png"),ImageFormat.Png);
                            foreach(Control target in new Control[]{text,list,checkedList,wideGrid}){var binding=ScrollSkin.Find(target);foreach(var view in new[]{binding.Vertical,binding.Horizontal,binding.Corner})if(view.Visible)CheckCompositedScroll(box,image,view,dark,check);}
                        }
                        await Task.Delay(100);int idle=textSkin.RefreshCount;textSkin.Vertical.ProbeHover(true);textSkin.Vertical.ProbeHover(false);await Task.Delay(150);
                        check(textSkin.RefreshCount==idle,"Hover and idle never queue content layout or polling repaints");
                        text.Text="短内容";list.Items.Clear();list.HorizontalExtent=0;await Task.Delay(80);ScrollSkin.RefreshTree(box);
                        check(!textSkin.Vertical.Range.Needed&&!textSkin.Horizontal.Range.Needed&&!listSkin.Vertical.Range.Needed&&!listSkin.Horizontal.Range.Needed,"No-overflow content hides unnecessary scrollbar thumbs");
                        check(textSkin.NativeBarsAreClipped()&&listSkin.NativeBarsAreClipped(),"Disabled native scrollbar strips also stay outside the viewport");
                    }
                    gridSkin.Move(false,0);
                    using(var image=new Bitmap(gridSkin.Host.Width,gridSkin.Host.Height)){gridSkin.Host.DrawToBitmap(image,gridSkin.Host.ClientRectangle);CheckCompositedScroll(gridSkin.Host,image,gridSkin.Vertical,dark,check);}
                    foreach(Control target in new Control[]{grid,detail,lockList,logBox})check(ScrollSkin.Find(target)!=null,"Independent scrollbars attached to "+(target.AccessibleName??target.GetType().Name));
                    for(int page=0;page<3;page++){SwitchPage(page);ScrollSkin.RefreshTree(this);await Task.Delay(50);foreach(Control target in new Control[]{grid,detail,lockList,logBox})if(target.Visible)check(ScrollSkin.Find(target).NativeBarsAreClipped(),"Page "+page+" keeps every native scrollbar clipped");}
                    SwitchPage(0);
                }
            }finally{Palette.Dark=originalDark;RefreshMaterial();SwitchPage(0);}
        }
        void CheckScrollInteraction(Control target,ScrollVisual view,bool horizontal,bool dark,Action<bool,string> check){
            var binding=ScrollSkin.Find(target);binding.Refresh();string label=target.GetType().Name+(horizontal?" H":" V");
            check(binding.NativeBarsAreClipped(),label+" native scrollbar is physically outside the visible viewport");
            var hit=view.PointToScreen(new Point(view.Width/2,view.Height/2));check(ScrollNative.SendMessage(view.Handle,0x84,IntPtr.Zero,new IntPtr((hit.Y<<16)|(hit.X&65535))).ToInt32()==1,label+" is a real pointer target, not a transparent overlay");
            CheckScrollVisual(view,dark,check);
            var bounds=view.Bounds;view.ProbeKey(Keys.Home);view.ProbeHover(true);var thumb=view.ThumbBounds;
            var start=new Point((int)(thumb.Left+thumb.Width/2),(int)(thumb.Top+thumb.Height/2));view.ProbePointer(0,start);
            check(view.Dragging&&view.Capture,label+" owns pointer capture instead of forwarding into native tracking");
            for(int step=1;step<=8;step++){
                Point point=horizontal?new Point(start.X+(int)(view.Width*step/8.0),start.Y):new Point(start.X,start.Y+(int)(view.Height*step/8.0));
                view.ProbePointer(1,point);
                check(view.Bounds==bounds&&binding.NativeBarsAreClipped(),label+" drag frame keeps stable bounds and clips the native bar");
                using(var frame=PaintWindowFrame(view,Color.Magenta))if(dark)check(NoWhitePixels(frame),label+" drag frame contains no white native track");
            }
            view.ProbePointer(2,start);view.ProbeHover(false);
            check(!view.Capture&&!view.Dragging&&view.Range.Value>view.Range.Minimum,label+" drag scrolls content and releases capture");
            view.ProbeKey(Keys.Home);check(view.Range.Value==view.Range.Minimum,label+" Home reaches the beginning");
            view.ProbeWheel(-120);check(view.Range.Value>view.Range.Minimum,label+" wheel over the track scrolls content");
            view.ProbeKey(Keys.End);check(view.Range.Value>=view.Range.Maximum-(horizontal?16:1),label+" End reaches the last page");
            view.ProbeKey(Keys.Home);thumb=view.ThumbBounds;var trackPoint=horizontal?new Point(view.Width-2,view.Height/2):new Point(view.Width/2,view.Height-2);
            view.ProbePointer(0,trackPoint);view.ProbePointer(2,trackPoint);check(view.Range.Value>view.Range.Minimum,label+" clicking the track advances a page");
            for(int i=0;i<12;i++){view.ProbeHover(i%2==0);using(var frame=PaintWindowFrame(view,Color.Magenta))if(dark)check(NoWhitePixels(frame),label+" repeated hover does not expose a white frame");}
            view.ProbeHover(false);view.ProbeKey(Keys.Home);check(view.Bounds==bounds,label+" hover never changes scrollbar layout");
            check(view.AccessibilityObject.Role==AccessibleRole.ScrollBar&&view.AccessibilityObject.Value==view.Range.Value.ToString(),label+" exposes an accessible scrollbar value");
        }
        void CheckLiveDrag(Control target,ScrollVisual view,bool horizontal,Action<bool,string> check){
            view.ProbeKey(Keys.Home);view.ProbeHover(true);target.Update();view.Update();
            var thumb=view.ThumbBounds;var start=new Point((int)(thumb.Left+thumb.Width/2),(int)(thumb.Top+thumb.Height/2));
            using(var trackPaint=new ScrollPaintWitness(view,()=>view.Range.Value))using(var contentPaint=new ScrollPaintWitness(target,()=>ScrollNative.Range(target,horizontal).Value)){
                var watch=Stopwatch.StartNew();int trackFrames=0,contentFrames=0,changes=0;bool follows=true;int layouts=ScrollSkin.Find(target).RefreshCount;
                SendScrollPointer(view,0x201,start);
                for(int i=1;i<=32;i++){
                    int previous=view.Range.Value,trackBefore=trackPaint.Count,contentBefore=contentPaint.Count;
                    var at=horizontal?new Point(start.X+i*2,start.Y):new Point(start.X,start.Y+i*2);
                    SendScrollPointer(view,0x200,at);
                    // Do not render a bitmap, call Update, yield, or release the
                    // mouse here: that would hide starvation of real WM_PAINT.
                    if(trackPaint.Count>trackBefore&&trackPaint.Position==view.Range.Value)trackFrames++;
                    var current=view.ThumbBounds;follows&=Math.Abs((horizontal?current.Left:current.Top)-(horizontal?thumb.Left:thumb.Top)-i*2)<.1;
                    if(view.Range.Value!=previous){changes++;if(contentPaint.Count>contentBefore&&contentPaint.Position==ScrollNative.Range(target,horizontal).Value)contentFrames++;}
                }
                watch.Stop();var beforeRelease=view.ThumbBounds;SendScrollPointer(view,0x202,start);var afterRelease=view.ThumbBounds;
                string sample=target.GetType().Name+(horizontal?" H":" V")+" theme="+(Palette.Dark?"dark":"light")+" trackFrames="+trackFrames+"/32 contentFrames="+contentFrames+"/"+changes+" ms="+watch.ElapsedMilliseconds+" contentMs="+(contentPaint.Ticks*1000/Stopwatch.Frequency)+" thumbMs="+(trackPaint.Ticks*1000/Stopwatch.Frequency)+" visible="+ScrollPaintWitness.IsWindowVisible(view.Handle);
                File.AppendAllText(Path.Combine(Store.Data,"live-scroll-checks.txt"),sample+Environment.NewLine);
                check(trackFrames==32,"Every continuous drag event presents its thumb before mouse-up: "+sample);
                check(changes>0&&contentFrames==changes,"Every changed drag position presents content before mouse-up");
                check(follows,"Thumb follows each two-pixel pointer move without snapping to row/line units");
                check(Math.Abs(beforeRelease.Left-afterRelease.Left)<1&&Math.Abs(beforeRelease.Top-afterRelease.Top)<1,"Releasing the thumb does not snap it backwards: "+target.GetType().Name+" before="+beforeRelease+" after="+afterRelease);
                check(ScrollSkin.Find(target).RefreshCount==layouts,"Continuous dragging does not recalculate the host layout");
            }
            view.ProbeHover(false);view.ProbeKey(Keys.Home);
        }
        void SendScrollPointer(Control view,int message,Point point){ScrollNative.SendMessage(view.Handle,message,new IntPtr(message==0x202?0:1),new IntPtr((point.Y<<16)|(point.X&65535)));}
        bool NoWhitePixels(Bitmap image){for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var c=image.GetPixel(x,y);if(c.R>210&&c.G>210&&c.B>210)return false;}return true;}
        void CheckCompositedScroll(Control host,Bitmap image,ScrollVisual view,bool dark,Action<bool,string> check){
            Point at=host.PointToClient(view.Parent.PointToScreen(view.Location));var bounds=new Rectangle(at,view.Size);bounds.Intersect(new Rectangle(Point.Empty,image.Size));
            using(var skin=PaintWindowFrame(view,Color.Magenta)){
                var point=new Point(view.Width>view.Height?view.Width/2:1,view.Width>view.Height?1:view.Height/2);
                var actual=image.GetPixel(at.X+point.X,at.Y+point.Y);var expected=skin.GetPixel(point.X,point.Y);
                check(Math.Abs(actual.R-expected.R)+Math.Abs(actual.G-expected.G)+Math.Abs(actual.B-expected.B)<6,"Full-window rendering preserves the independent scrollbar track");
            }
            if(dark&&bounds.Width>0&&bounds.Height>0)using(var crop=image.Clone(bounds,PixelFormat.Format32bppArgb))check(NoWhitePixels(crop),"Composited dark scrollbar and corner contain no native white pixels");
        }
        void CheckScrollVisual(ScrollVisual view,bool dark,Action<bool,string> check){
            using(var first=PaintWindowFrame(view,Color.Magenta))using(var second=PaintWindowFrame(view,Color.Lime)){
                check(SamePixels(first,second),"Managed scrollbar painting clears every old pixel");if(dark)check(NoWhitePixels(first),"Dark scrollbar has no white track or corner pixels");
            }
        }
    }
    internal sealed class ScrollPaintWitness:NativeWindow,IDisposable {
        [DllImport("user32.dll")]internal static extern bool IsWindowVisible(IntPtr handle);
        readonly Func<int> position;internal int Count,Position;internal long Ticks;
        internal ScrollPaintWitness(Control owner,Func<int> readPosition){position=readPosition;AssignHandle(owner.Handle);}
        protected override void WndProc(ref Message m){long start=Stopwatch.GetTimestamp();base.WndProc(ref m);if(m.Msg==0xF){Ticks+=Stopwatch.GetTimestamp()-start;Count++;Position=position();}}
        public void Dispose(){if(Handle!=IntPtr.Zero)ReleaseHandle();}
    }
}
