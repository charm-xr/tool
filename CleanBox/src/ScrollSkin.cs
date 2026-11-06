using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CleanBox {
    internal static class ScrollSkin {
        static readonly Dictionary<Control,ScrollBinding> bindings=new Dictionary<Control,ScrollBinding>();
        internal static bool Supports(Control c){return c is DataGridView||c is ListBox||(c is TextBox&&((TextBox)c).Multiline);}
        internal static void Attach(Control c){
            if(!Supports(c)||c.IsDisposed||c.Parent==null)return;ScrollBinding binding;
            if(!bindings.TryGetValue(c,out binding)){binding=new ScrollBinding(c);bindings.Add(c,binding);}
            binding.Refresh();
        }
        internal static ScrollBinding Find(Control c){ScrollBinding result;return bindings.TryGetValue(c,out result)?result:null;}
        internal static void Forget(Control c){bindings.Remove(c);}
        internal static void RefreshTree(Control root){foreach(Control child in root.Controls.Cast<Control>().ToArray())RefreshTree(child);ScrollBinding b;if(bindings.TryGetValue(root,out b))b.Refresh();}
    }
    internal static class ScrollNative {
        [StructLayout(LayoutKind.Sequential)]internal struct RECT{public int Left,Top,Right,Bottom;public Rectangle Bounds{get{return Rectangle.FromLTRB(Left,Top,Right,Bottom);}}}
        [StructLayout(LayoutKind.Sequential)]internal struct BarInfo{
            public int Size;public RECT Rect;public int ArrowSize,ThumbStart,ThumbEnd,Reserved;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)]public uint[] State;
        }
        [StructLayout(LayoutKind.Sequential)]internal struct Info{public int Size,Mask,Min,Max,Page,Position,Track;}
        [DllImport("user32.dll",SetLastError=true)]static extern bool GetScrollBarInfo(IntPtr window,int objectId,ref BarInfo info);
        [DllImport("user32.dll")]static extern bool GetScrollInfo(IntPtr window,int bar,ref Info info);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        internal static bool Read(Control owner,bool horizontal,out BarInfo info,out Control native){
            info=new BarInfo{Size=Marshal.SizeOf(typeof(BarInfo)),State=new uint[6]};native=owner;
            if(owner is DataGridView){native=owner.Controls.Cast<Control>().FirstOrDefault(c=>horizontal?c is HScrollBar:c is VScrollBar);if(native==null||!native.Visible)return false;}
            if(!native.IsHandleCreated)return false;
            return GetScrollBarInfo(native.Handle,native==owner?(horizontal?-6:-5):-4,ref info)&&(info.State[0]&0x8000)==0&&info.Rect.Right>info.Rect.Left&&info.Rect.Bottom>info.Rect.Top;
        }
        internal static ScrollRange Range(Control owner,bool horizontal){
            var grid=owner as DataGridView;
            if(grid!=null){
                if(!horizontal){
                    var rows=Rows(grid);
                    int page=Math.Max(1,grid.DisplayedRowCount(false)),value=Array.BinarySearch(rows,grid.FirstDisplayedScrollingRowIndex);
                    return new ScrollRange(0,Math.Max(0,rows.Length-page),page,Math.Max(0,value));
                }
                var bar=grid.Controls.OfType<HScrollBar>().FirstOrDefault();
                return bar==null||!bar.Visible?new ScrollRange():new ScrollRange(bar.Minimum,Math.Max(bar.Minimum,bar.Maximum-bar.LargeChange+1),bar.LargeChange,bar.Value);
            }
            var list=owner as ListBox;
            if(list!=null&&!horizontal&&list.DrawMode!=DrawMode.OwnerDrawVariable){
                // The native range can retain a page size computed before the
                // inherited font/DPI settles. Use the current item and client
                // sizes, otherwise the first live scroll changes thumb length.
                int page=Math.Max(1,list.ClientSize.Height/Math.Max(1,list.ItemHeight));
                return new ScrollRange(0,Math.Max(0,list.Items.Count-page),page,Math.Max(0,list.TopIndex));
            }
            var info=new Info{Size=Marshal.SizeOf(typeof(Info)),Mask=7};
            if(!owner.IsHandleCreated||!GetScrollInfo(owner.Handle,horizontal?0:1,ref info))return new ScrollRange();
            return new ScrollRange(info.Min,Math.Max(info.Min,info.Max-Math.Max(1,info.Page)+1),Math.Max(1,info.Page),info.Position);
        }
        internal static int[] Rows(DataGridView grid){var binding=ScrollSkin.Find(grid);return binding!=null?binding.ScrollRows:grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&!r.IsNewRow&&!r.Frozen).Select(r=>r.Index).ToArray();}
        internal static void SetPosition(Control owner,bool horizontal,int value){
            var range=Range(owner,horizontal);value=Math.Max(range.Minimum,Math.Min(range.Maximum,value));
            if(value==range.Value)return;
            var grid=owner as DataGridView;
            if(grid!=null){
                if(horizontal)grid.HorizontalScrollingOffset=value;
                else{var rows=Rows(grid);if(value<rows.Length)grid.FirstDisplayedScrollingRowIndex=rows[value];}
            }else if(!horizontal){
                var list=owner as ListBox;if(list!=null)list.TopIndex=value;
                else SendMessage(owner.Handle,0xB6,IntPtr.Zero,new IntPtr(value-Range(owner,false).Value));
            }else if(value<=ushort.MaxValue){
                SendMessage(owner.Handle,0x114,new IntPtr((value<<16)|4),IntPtr.Zero);
            }else if(owner is TextBox){
                // Edit controls scroll horizontally in character units. Avoid the
                // 16-bit WM_HSCROLL thumb-position limit for exceptionally long lines.
                int unit=Math.Max(1,TextRenderer.MeasureText("0000000000",owner.Font,Size.Empty,TextFormatFlags.NoPadding).Width/10);
                for(int attempt=0;attempt<8;attempt++){
                    int before=Range(owner,true).Value,delta=value-before;if(Math.Abs(delta)<unit)break;
                    int characters=delta/unit;if(characters==0)characters=Math.Sign(delta);
                    SendMessage(owner.Handle,0xB6,new IntPtr(characters),IntPtr.Zero);
                    int moved=Range(owner,true).Value-before;if(moved==0)break;unit=Math.Max(1,Math.Abs(moved/characters));
                }
            }else{
                // ListBox horizontal extents are normally small. Page messages
                // still reach large extents without truncating a thumb position.
                for(int attempt=0;attempt<256;attempt++){int before=Range(owner,true).Value;if(Math.Abs(value-before)<range.Page)break;SendMessage(owner.Handle,0x114,new IntPtr(value>before?3:2),IntPtr.Zero);if(Range(owner,true).Value==before)break;}
            }
        }
    }
    internal struct ScrollRange {
        internal int Minimum,Maximum,Page,Value;
        internal ScrollRange(int minimum,int maximum,int page,int value){Minimum=minimum;Maximum=maximum;Page=page;Value=value;}
        internal bool Needed{get{return Maximum>Minimum;}}
    }
    // Native content keeps its wheel, selection, keyboard and text behavior. Its
    // scrollbar strips live OUTSIDE a child viewport and cannot be hit or painted
    // in the visible track. The separate managed scrollbar owns all pointer input.
    internal sealed class ScrollBinding:IDisposable {
        readonly Control owner;ScrollObserver observer;bool queued,syncing,moving,disposed;int[] scrollRows;
        internal readonly ScrollSurface Host,Viewport;
        internal readonly ScrollVisual Vertical,Horizontal,Corner;
        internal int RefreshCount{get;private set;}
        internal int[] ScrollRows{get{if(scrollRows==null)scrollRows=((DataGridView)owner).Rows.Cast<DataGridViewRow>().Where(r=>r.Visible&&!r.IsNewRow&&!r.Frozen).Select(r=>r.Index).ToArray();return scrollRows;}}
        internal ScrollBinding(Control control){
            owner=control;var parent=owner.Parent;var bounds=owner.Bounds;int index=parent.Controls.GetChildIndex(owner);
            Host=new ScrollSurface(owner){Name="ScrollHost",Bounds=bounds,Dock=owner.Dock,Anchor=owner.Anchor,Margin=owner.Margin,TabIndex=owner.TabIndex};
            Viewport=new ScrollSurface(owner){Name="ScrollViewport",TabStop=false};
            Vertical=new ScrollVisual(owner,false,false,v=>Move(false,v));Horizontal=new ScrollVisual(owner,true,false,v=>Move(true,v));Corner=new ScrollVisual(owner,false,true,null);
            parent.SuspendLayout();Host.SuspendLayout();
            owner.Dock=DockStyle.None;owner.Anchor=AnchorStyles.Top|AnchorStyles.Left;owner.Margin=Padding.Empty;
            var text=owner as TextBox;if(text!=null)text.BorderStyle=BorderStyle.None;
            var list=owner as ListBox;if(list!=null){list.BorderStyle=BorderStyle.None;list.IntegralHeight=false;}
            Host.Controls.AddRange(new Control[]{Viewport,Vertical,Horizontal,Corner});Viewport.Controls.Add(owner);parent.Controls.Add(Host);parent.Controls.SetChildIndex(Host,index);
            Host.ResumeLayout();parent.ResumeLayout(true);
            Host.SizeChanged+=Changed;Host.VisibleChanged+=Changed;owner.HandleCreated+=HandleCreated;owner.HandleDestroyed+=HandleGone;owner.SizeChanged+=Changed;owner.TextChanged+=Changed;owner.Disposed+=OwnerDisposed;
            if(owner is DataGridView){var grid=(DataGridView)owner;grid.Scroll+=GridScroll;grid.RowsAdded+=RowsAdded;grid.RowsRemoved+=RowsRemoved;grid.RowStateChanged+=RowChanged;grid.ColumnWidthChanged+=ColumnChanged;grid.RowHeightChanged+=RowHeightChanged;grid.Sorted+=RowsSorted;}
        }
        void Changed(object sender,EventArgs e){Refresh();}
        void HandleCreated(object sender,EventArgs e){InstallObserver();Refresh();}
        void HandleGone(object sender,EventArgs e){if(observer!=null){observer.Detach();observer=null;}}
        void GridScroll(object sender,ScrollEventArgs e){if(!moving&&!syncing)SyncPositions();}
        void RowsAdded(object sender,DataGridViewRowsAddedEventArgs e){scrollRows=null;Queue();}
        void RowsRemoved(object sender,DataGridViewRowsRemovedEventArgs e){scrollRows=null;Queue();}
        void RowsSorted(object sender,EventArgs e){scrollRows=null;Queue();}
        void RowChanged(object sender,DataGridViewRowStateChangedEventArgs e){if((e.StateChanged&(DataGridViewElementStates.Visible|DataGridViewElementStates.Frozen))!=0){scrollRows=null;Queue();}}
        void ColumnChanged(object sender,DataGridViewColumnEventArgs e){Queue();}
        void RowHeightChanged(object sender,DataGridViewRowEventArgs e){Queue();}
        void OwnerDisposed(object sender,EventArgs e){Dispose();}
        void InstallObserver(){if(observer==null&&owner.IsHandleCreated)observer=new ScrollObserver(owner.Handle,this);}
        internal void Queue(){
            if(disposed||syncing||moving||queued||!owner.IsHandleCreated||owner.IsDisposed)return;queued=true;
            try{owner.BeginInvoke((Action)(()=>{queued=false;if(!disposed)Refresh();}));}catch(InvalidOperationException){queued=false;}
        }
        void SyncPositions(){
            Vertical.UpdateRange(Vertical.Visible?ScrollNative.Range(owner,false):new ScrollRange());
            Horizontal.UpdateRange(Horizontal.Visible?ScrollNative.Range(owner,true):new ScrollRange());
        }
        internal void ContentScrolled(){
            if(disposed||syncing||moving)return;SyncPositions();
            owner.Update();Vertical.Update();Horizontal.Update();
        }
        internal void Move(bool horizontal,int value){
            if(disposed||moving)return;moving=true;
            try{
                int before=ScrollNative.Range(owner,horizontal).Value;
                ScrollNative.SetPosition(owner,horizontal,value);SyncPositions();
                // WM_PAINT is lower priority than pending pointer input. Present
                // this content change before returning to the input queue, without
                // DoEvents/reentrancy or re-laying out the whole host on each move.
                if(ScrollNative.Range(owner,horizontal).Value!=before){owner.Invalidate();owner.Update();}
                Vertical.Update();Horizontal.Update();
            }finally{moving=false;}
        }
        internal void Refresh(){
            if(disposed||syncing||owner.IsDisposed)return;syncing=true;
            try{
                RefreshCount++;InstallObserver();if(Host.ClientSize.Width<1||Host.ClientSize.Height<1)return;
                float dpi=1;if(Host.IsHandleCreated)using(var g=Host.CreateGraphics())dpi=g.DpiX/96f;int track=Math.Max(14,(int)Math.Round(14*dpi));
                // Resize can change overflow on the other axis. Settle both axes
                // before returning to the message loop; never resize on hover.
                for(int pass=0;pass<5;pass++){
                    ScrollNative.BarInfo vi,hi;Control vn,hn;
                    bool nativeV=ScrollNative.Read(owner,false,out vi,out vn),nativeH=ScrollNative.Read(owner,true,out hi,out hn);
                    // Hidden native bars can retain an obsolete range after a list
                    // is cleared; never expose that stale range to input/accessibility.
                    var vr=nativeV&&(vi.State[0]&1)==0?ScrollNative.Range(owner,false):new ScrollRange();var hr=nativeH&&(hi.State[0]&1)==0?ScrollNative.Range(owner,true):new ScrollRange();
                    // Keep the whole native window within Host as well as clipping
                    // its strips in Viewport. This also prevents WM_PRINT from
                    // spilling non-client pixels into adjacent panels in captures.
                    int verticalTrack=Math.Max(track,nativeV?vi.Rect.Right-vi.Rect.Left:0),horizontalTrack=Math.Max(track,nativeH?hi.Rect.Bottom-hi.Rect.Top:0);
                    int width=Math.Max(1,Host.ClientSize.Width-(nativeV?verticalTrack:0)),height=Math.Max(1,Host.ClientSize.Height-(nativeH?horizontalTrack:0));
                    var viewport=new Rectangle(0,0,width,height);
                    var size=new Size(width+(nativeV?vi.Rect.Right-vi.Rect.Left:0),height+(nativeH?hi.Rect.Bottom-hi.Rect.Top:0));
                    bool stable=Viewport.Bounds==viewport&&owner.Size==size;
                    Viewport.Bounds=viewport;owner.Bounds=new Rectangle(Point.Empty,size);
                    Vertical.Bounds=new Rectangle(width,0,verticalTrack,height);Horizontal.Bounds=new Rectangle(0,height,width,horizontalTrack);Corner.Bounds=new Rectangle(width,height,verticalTrack,horizontalTrack);
                    Vertical.UpdateRange(vr);Horizontal.UpdateRange(hr);Vertical.Visible=nativeV;Horizontal.Visible=nativeH;Corner.Visible=nativeV&&nativeH;
                    if(stable)break;
                }
                // Palette changes never create controls or change their z-order.
                Host.Invalidate();Viewport.Invalidate();Vertical.Invalidate();Horizontal.Invalidate();Corner.Invalidate();
            }finally{syncing=false;}
        }
        internal bool NativeBarsAreClipped(){
            var visible=new Rectangle(Viewport.PointToScreen(Point.Empty),Viewport.ClientSize);
            foreach(bool horizontal in new[]{false,true}){ScrollNative.BarInfo info;Control native;if(ScrollNative.Read(owner,horizontal,out info,out native)&&visible.IntersectsWith(info.Rect.Bounds))return false;}return true;
        }
        public void Dispose(){
            if(disposed)return;disposed=true;if(observer!=null)observer.Detach();
            Host.SizeChanged-=Changed;Host.VisibleChanged-=Changed;owner.HandleCreated-=HandleCreated;owner.HandleDestroyed-=HandleGone;owner.SizeChanged-=Changed;owner.TextChanged-=Changed;owner.Disposed-=OwnerDisposed;
            if(owner is DataGridView){var grid=(DataGridView)owner;grid.Scroll-=GridScroll;grid.RowsAdded-=RowsAdded;grid.RowsRemoved-=RowsRemoved;grid.RowStateChanged-=RowChanged;grid.ColumnWidthChanged-=ColumnChanged;grid.RowHeightChanged-=RowHeightChanged;grid.Sorted-=RowsSorted;}
            Vertical.Dispose();Horizontal.Dispose();Corner.Dispose();ScrollSkin.Forget(owner);
        }
    }
    // Observe content changes only. No paint interception, native DC repaint,
    // mouse-transparent windows, BringToFront loop, or polling timer.
    internal sealed class ScrollObserver:NativeWindow {
        readonly ScrollBinding binding;
        internal ScrollObserver(IntPtr handle,ScrollBinding owner){binding=owner;AssignHandle(handle);}
        internal void Detach(){if(Handle!=IntPtr.Zero)ReleaseHandle();}
        protected override void WndProc(ref Message m){
            int message=m.Msg;base.WndProc(ref m);
            if(message==0x114||message==0x115||message==0x20A||message==0x20E||message==0x100||message==0xB6||message==0xB7||message==0x197)binding.ContentScrolled();
            else if(message==5||message==0xC||message==0x102||message==0xC2||(message>=0x180&&message<=0x186)||message==0x194)binding.Queue();
        }
    }
    internal sealed class ScrollSurface:Panel {
        readonly Control owner;
        internal ScrollSurface(Control content){owner=content;TabStop=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.ResizeRedraw,true);}
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnPaint(PaintEventArgs e){if(owner is DataGridView)GlassPaint.Underlay(this,e.Graphics);else e.Graphics.Clear(owner.BackColor);}
    }
    internal sealed class ScrollVisual:Control {
        readonly Control owner;readonly bool horizontal,corner;readonly Action<int> move;
        ScrollRange range;bool hot,dragging;float grab,dragStart,dragSize,dragTravel;float dpi=1;int wheelRemainder;double pointerFraction=double.NaN;
        Bitmap backdrop;Size backdropCanvas;Point backdropOrigin;bool backdropDark,backdropGlass;float backdropDpi;
        internal ScrollVisual(Control target,bool isHorizontal,bool isCorner,Action<int> moveTo){
            owner=target;horizontal=isHorizontal;corner=isCorner;move=moveTo;TabStop=!corner;Visible=false;
            Name="CleanBoxScroll"+(corner?"Corner":horizontal?"Horizontal":"Vertical");AccessibleName=horizontal?"水平滚动条":"垂直滚动条";AccessibleRole=corner?AccessibleRole.None:AccessibleRole.ScrollBar;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        }
        internal void UpdateRange(ScrollRange value){bool changed=range.Minimum!=value.Minimum||range.Maximum!=value.Maximum||range.Page!=value.Page||range.Value!=value.Value;if(changed&&!dragging)pointerFraction=double.NaN;range=value;TabStop=!corner&&range.Needed;if(!range.Needed&&dragging){dragging=false;Capture=false;pointerFraction=double.NaN;}if(changed)Invalidate();}
        internal ScrollRange Range{get{return range;}}
        internal bool Dragging{get{return dragging;}}
        float Length{get{return horizontal?Width:Height;}}
        internal RectangleF ThumbBounds{
            get{
                float pad=4*dpi,available=Math.Max(1,Length-2*pad),size=(float)Math.Min(available,Math.Max(28*dpi,available*Math.Max(1,range.Page)/(Math.Max(1,(double)range.Maximum-range.Minimum)+Math.Max(1,range.Page))));
                float start=pad+(available-size)*(range.Value-range.Minimum)/Math.Max(1,range.Maximum-range.Minimum),thickness=Math.Max(1,Math.Min((horizontal?Height:Width)-4,(hot||dragging?7:5)*dpi));
                if(dragging){start=dragStart;size=dragSize;}
                else if(!double.IsNaN(pointerFraction))start=pad+(float)((available-size)*pointerFraction);
                return horizontal?new RectangleF(start,(Height-thickness)/2,size,thickness):new RectangleF((Width-thickness)/2,start,thickness,size);
            }
        }
        float Axis(Point p){return horizontal?p.X:p.Y;}
        void Change(int value,bool pointer=false){if(!pointer&&!double.IsNaN(pointerFraction)){pointerFraction=double.NaN;Invalidate();}if(move!=null&&owner.Enabled)move(Math.Max(range.Minimum,Math.Min(range.Maximum,value)));}
        protected override void OnMouseEnter(EventArgs e){hot=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hot=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){
            base.OnMouseDown(e);if(corner||e.Button!=MouseButtons.Left||!range.Needed||!owner.Enabled)return;
            Focus();var thumb=ThumbBounds;float axis=Axis(e.Location),start=horizontal?thumb.Left:thumb.Top,end=horizontal?thumb.Right:thumb.Bottom;
            if(axis>=start&&axis<=end){grab=axis-start;dragStart=start;dragSize=end-start;dragTravel=Math.Max(1,Length-8*dpi-dragSize);dragging=true;Capture=true;}
            else Change(range.Value+(axis<start?-1:1)*Math.Max(1,range.Page));Invalidate();Update();
        }
        protected override void OnMouseMove(MouseEventArgs e){
            base.OnMouseMove(e);if(!dragging)return;
            dragStart=Math.Max(4*dpi,Math.Min(4*dpi+dragTravel,Axis(e.Location)-grab));
            pointerFraction=(dragStart-4*dpi)/dragTravel;
            // The visual follows the pointer in pixels even when the content
            // control has not yet crossed its next integral row/line boundary.
            Invalidate();Change(range.Minimum+(int)Math.Round(pointerFraction*(range.Maximum-range.Minimum)),true);Update();
        }
        void EndDrag(){if(!dragging)return;dragging=false;var thumb=ThumbBounds;float size=horizontal?thumb.Width:thumb.Height;pointerFraction=Math.Max(0,Math.Min(1,(dragStart-4*dpi)/Math.Max(1,Length-8*dpi-size)));}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left){EndDrag();Capture=false;Invalidate();Update();}}
        protected override void OnMouseCaptureChanged(EventArgs e){if(!Capture){EndDrag();Invalidate();}base.OnMouseCaptureChanged(e);}
        protected override void OnSizeChanged(EventArgs e){ClearBackdrop();pointerFraction=double.NaN;if(dragging){dragging=false;Capture=false;}base.OnSizeChanged(e);}
        protected override void OnMouseWheel(MouseEventArgs e){wheelRemainder+=e.Delta;int steps=wheelRemainder/120;wheelRemainder%=120;int lines=SystemInformation.MouseWheelScrollLines;if(steps!=0&&lines!=0)Change(range.Value-steps*(lines<0?range.Page:lines*(horizontal?16:1)));var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;base.OnMouseWheel(e);}
        protected override bool IsInputKey(Keys key){switch(key&Keys.KeyCode){case Keys.Up:case Keys.Down:case Keys.Left:case Keys.Right:case Keys.Home:case Keys.End:case Keys.PageUp:case Keys.PageDown:return true;}return base.IsInputKey(key);}
        protected override void OnKeyDown(KeyEventArgs e){int value=range.Value;switch(e.KeyCode){case Keys.Up:case Keys.Left:value-=horizontal?16:1;break;case Keys.Down:case Keys.Right:value+=horizontal?16:1;break;case Keys.PageUp:value-=range.Page;break;case Keys.PageDown:value+=range.Page;break;case Keys.Home:value=range.Minimum;break;case Keys.End:value=range.Maximum;break;default:base.OnKeyDown(e);return;}Change(value);e.Handled=true;e.SuppressKeyPress=true;}
        protected override AccessibleObject CreateAccessibilityInstance(){return new ScrollAccessibility(this);}
        sealed class ScrollAccessibility:ControlAccessibleObject{
            readonly ScrollVisual bar;internal ScrollAccessibility(ScrollVisual control):base(control){bar=control;}
            public override string Value{get{return bar.Range.Value.ToString();}set{int position;if(int.TryParse(value,out position))bar.Change(position);}}
        }
        protected override void OnPaintBackground(PaintEventArgs e){}
        void ClearBackdrop(){if(backdrop!=null)backdrop.Dispose();backdrop=null;}
        void PaintBackdrop(Graphics g){
            if(!(owner is DataGridView)){g.Clear(owner.BackColor);return;}
            var form=FindForm();Size canvas=form==null?ClientSize:form.ClientSize;Point origin=Point.Empty;
            for(Control c=this;c!=null&&!(c is Form);c=c.Parent)origin.Offset(c.Left,c.Top);
            if(backdrop==null||backdropCanvas!=canvas||backdropOrigin!=origin||backdropDark!=Palette.Dark||backdropGlass!=Palette.Glass||backdropDpi!=g.DpiX){
                ClearBackdrop();backdrop=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),System.Drawing.Imaging.PixelFormat.Format32bppPArgb);backdrop.SetResolution(g.DpiX,g.DpiY);
                using(var paint=Graphics.FromImage(backdrop))GlassPaint.Underlay(this,paint);
                backdropCanvas=canvas;backdropOrigin=origin;backdropDark=Palette.Dark;backdropGlass=Palette.Glass;backdropDpi=g.DpiX;
            }
            g.DrawImageUnscaled(backdrop,Point.Empty);
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;dpi=g.DpiX/96f;
            PaintBackdrop(g);
            if(corner||!range.Needed)return;g.SmoothingMode=SmoothingMode.AntiAlias;var thumb=ThumbBounds;
            Color color=SystemInformation.HighContrast?SystemColors.Highlight:(hot||dragging?Palette.Accent:Palette.Blend(Palette.Muted,Palette.Accent,Palette.Dark?.25:.18));
            using(var path=Palette.Shape(thumb,Math.Min(thumb.Width,thumb.Height)/2))using(var brush=new LinearGradientBrush(thumb,Palette.Blend(color,Color.White,hot?.17:.08),color,horizontal?LinearGradientMode.Vertical:LinearGradientMode.Horizontal))g.FillPath(brush,path);
            if(Focused&&ShowFocusCues){var rect=ClientRectangle;rect.Inflate(-1,-1);ControlPaint.DrawFocusRectangle(g,rect,Palette.Accent,owner.BackColor);}
        }
        // Exercise the very same event handlers as real pointer/keyboard input.
        internal void ProbePointer(int message,Point point){var e=new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0);if(message==0)OnMouseDown(e);else if(message==1)OnMouseMove(e);else OnMouseUp(e);}
        internal void ProbeHover(bool entered){if(entered)OnMouseEnter(EventArgs.Empty);else OnMouseLeave(EventArgs.Empty);}
        internal void ProbeKey(Keys key){OnKeyDown(new KeyEventArgs(key));}
        internal void ProbeWheel(int delta){OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,0,0,delta));}
        protected override void Dispose(bool disposing){if(disposing)ClearBackdrop();base.Dispose(disposing);}
    }
}
