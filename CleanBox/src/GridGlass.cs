using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CleanBox {
    // Native grid behavior with a separate rounded glass surface for each row.
    // The material is shared across columns; light can only paint its active row.
    internal class SmoothGrid:DataGridView,IMotionVisual {
        readonly SpringValue lightX=new SpringValue(0,220,19),lightY=new SpringValue(0,220,19),lightAmount=new SpringValue(0);
        Bitmap material,frosted,rowFrame;Point materialOrigin,lastPointer;Size canvasSize;
        bool materialDark,frameDirty=true,pointerInside;int lightRow=-2;float materialDpi=1;
        internal int MaterialGeneration{get;private set;}
        public SmoothGrid(){DoubleBuffered=true;}
        Point CanvasOrigin(){Point p=Point.Empty;for(Control c=this;c!=null&&!(c is Form);c=c.Parent)p.Offset(c.Left,c.Top);return p;}
        int ContentRight{get{int right=ClientSize.Width;foreach(Control c in Controls)if(c is VScrollBar&&c.Visible)right=Math.Min(right,c.Left);return right;}}
        internal RectangleF CardBounds(int rowIndex){
            float inset=4*materialDpi;
            if(rowIndex==-1)return new RectangleF(inset,2*materialDpi,Math.Max(1,ContentRight-inset*2),Math.Max(1,ColumnHeadersHeight-4*materialDpi));
            if(rowIndex<0||rowIndex>=Rows.Count||!Rows[rowIndex].Visible)return RectangleF.Empty;
            var row=GetRowDisplayRectangle(rowIndex,false);
            return new RectangleF(inset,row.Top+3*materialDpi,Math.Max(1,ContentRight-inset*2),Math.Max(1,row.Height-6*materialDpi));
        }
        float CardRadius(RectangleF rect){return Math.Min(rect.Height/2,20*materialDpi);}
        bool ContainsCard(int row,Point point){var rect=CardBounds(row);if(rect.IsEmpty)return false;using(var path=Palette.Shape(rect,CardRadius(rect)))return path.IsVisible(point);}
        void EnsureFrame(){
            var form=FindForm();Size canvas=form==null?ClientSize:form.ClientSize;Point origin=CanvasOrigin();
            if(material==null||material.Size!=ClientSize||canvas!=canvasSize||origin!=materialOrigin||materialDark!=Palette.Dark){
                ClearMaterial();if(ClientSize.Width<1||ClientSize.Height<1)return;
                material=new Bitmap(ClientSize.Width,ClientSize.Height,PixelFormat.Format32bppPArgb);
                frosted=new Bitmap(ClientSize.Width,ClientSize.Height,PixelFormat.Format32bppPArgb);
                rowFrame=new Bitmap(ClientSize.Width,ClientSize.Height,PixelFormat.Format32bppPArgb);
                using(var screen=CreateGraphics()){materialDpi=screen.DpiX/96f;material.SetResolution(screen.DpiX,screen.DpiY);frosted.SetResolution(screen.DpiX,screen.DpiY);rowFrame.SetResolution(screen.DpiX,screen.DpiY);}
                using(var g=Graphics.FromImage(material))GlassPaint.Underlay(this,g);
                using(var g=Graphics.FromImage(frosted))GlassPaint.Backdrop(this,g,true);
                canvasSize=canvas;materialOrigin=origin;materialDark=Palette.Dark;frameDirty=true;MaterialGeneration++;
            }
            if(!frameDirty)return;
            using(var g=Graphics.FromImage(rowFrame)){
                g.DrawImageUnscaled(material,Point.Empty);g.SmoothingMode=SmoothingMode.AntiAlias;
                var state=g.Save();g.SetClip(new Rectangle(0,ColumnHeadersHeight,ContentRight,Math.Max(0,ClientSize.Height-ColumnHeadersHeight)));
                int row=Rows.GetFirstRow(DataGridViewElementStates.Displayed);
                if(row>=0)for(;row>=0;row=Rows.GetNextRow(row,DataGridViewElementStates.Displayed)){
                    var rect=CardBounds(row);if(rect.Top>=ClientSize.Height)break;
                    PaintCard(g,rect,row,Rows[row].Selected);
                }
                g.Restore(state);
                if(ColumnHeadersVisible)PaintCard(g,CardBounds(-1),-1,false);
            }
            frameDirty=false;
        }
        void PaintCard(Graphics g,RectangleF rect,int row,bool selected){
            if(rect.Width<2||rect.Height<2)return;float radius=CardRadius(rect);
            GlassPaint.Surface(this,g,rect,radius,selected?1:0,frosted);
            if(selected)using(var shape=Palette.Shape(rect,radius))using(var pen=new Pen(Color.FromArgb(Palette.Dark?170:150,Palette.Accent),1.2f*materialDpi))g.DrawPath(pen,shape);
            if(lightRow==row&&lightAmount.Value>.001){
                var point=new PointF((float)((lightX.Value-rect.X)/rect.Width),(float)((lightY.Value-rect.Y)/rect.Height));
                GlassPaint.ButtonLight(g,rect,radius,point,(float)lightAmount.Value,0,false,280*materialDpi);
            }
            if(selected&&Focused&&ShowFocusCues&&CurrentCell!=null&&CurrentCell.RowIndex==row){
                var focus=rect;focus.Inflate(-3*materialDpi,-3*materialDpi);
                using(var shape=Palette.Shape(focus,Math.Max(1,radius-3*materialDpi)))using(var pen=new Pen(Palette.Accent)){pen.DashStyle=DashStyle.Dot;g.DrawPath(pen,shape);}
            }
        }
        void ClearMaterial(){if(material!=null)material.Dispose();if(frosted!=null)frosted.Dispose();if(rowFrame!=null)rowFrame.Dispose();material=null;frosted=null;rowFrame=null;frameDirty=true;}
        internal void RefreshMaterial(){ClearMaterial();pointerInside=false;lightRow=-2;Settle();MotionClock.Remove(this);}
        protected override void OnPaint(PaintEventArgs e){frameDirty=true;base.OnPaint(e);}
        protected override void PaintBackground(Graphics g,Rectangle clipBounds,Rectangle gridBounds){
            if(!Palette.Glass){base.PaintBackground(g,clipBounds,gridBounds);return;}
            EnsureFrame();if(rowFrame==null)return;
            var state=g.Save();g.SetClip(clipBounds,CombineMode.Intersect);g.DrawImageUnscaled(rowFrame,Point.Empty);g.Restore(state);
        }
        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e){
            if(!Palette.Glass||e.ColumnIndex<0){base.OnCellPainting(e);return;}
            EnsureFrame();if(rowFrame==null){base.OnCellPainting(e);return;}
            var g=e.Graphics;var state=g.Save();
            try{
                g.SetClip(e.ClipBounds,CombineMode.Intersect);g.SetClip(e.CellBounds,CombineMode.Intersect);
                var visible=Rectangle.Intersect(e.ClipBounds,e.CellBounds);if(visible.Width>0&&visible.Height>0)g.DrawImage(rowFrame,visible,visible,GraphicsUnit.Pixel);
                var card=CardBounds(e.RowIndex);using(var shape=Palette.Shape(card,CardRadius(card)))g.SetClip(shape,CombineMode.Intersect);
                base.OnCellPainting(e);
                if(!e.Handled)e.Paint(e.ClipBounds,DataGridViewPaintParts.ContentForeground|DataGridViewPaintParts.ErrorIcon);
                e.Handled=true;
            }finally{g.Restore(state);}
        }
        internal void MoveLight(Point point){
            lastPointer=point;var hit=HitTest(point.X,point.Y);
            bool inside=Palette.Glass&&(hit.Type==DataGridViewHitTestType.Cell||hit.Type==DataGridViewHitTestType.ColumnHeader)&&ContainsCard(hit.RowIndex,point);
            if(!inside){LeaveLight();return;}
            if(lightRow!=hit.RowIndex){lightRow=hit.RowIndex;lightAmount.Snap(0);lightX.Snap(point.X);lightY.Snap(point.Y);frameDirty=true;Invalidate();}
            else if(!pointerInside&&lightAmount.Value<=.001){lightX.Snap(point.X);lightY.Snap(point.Y);}
            pointerInside=true;lightX.Target=point.X;lightY.Target=point.Y;lightAmount.Target=1;MotionClock.Wake(this);
        }
        internal void LeaveLight(){pointerInside=false;lightAmount.Target=0;MotionClock.Wake(this);}
        protected override void OnMouseMove(MouseEventArgs e){MoveLight(e.Location);base.OnMouseMove(e);}
        protected override void OnMouseLeave(EventArgs e){LeaveLight();base.OnMouseLeave(e);}
        // Re-evaluate the row below the stationary pointer when content scrolls.
        protected override void OnScroll(ScrollEventArgs e){base.OnScroll(e);frameDirty=true;if(pointerInside)MoveLight(lastPointer);if(Palette.Glass)Invalidate();}
        protected override void OnVisibleChanged(EventArgs e){if(!Visible){pointerInside=false;Settle();MotionClock.Remove(this);}base.OnVisibleChanged(e);}
        public Control MotionControl{get{return this;}}
        public bool Advance(double seconds){bool moving=lightAmount.Step(seconds);moving=lightX.Step(seconds)||moving;moving=lightY.Step(seconds)||moving;frameDirty=true;Invalidate();return moving;}
        public void Settle(){lightAmount.Snap(pointerInside&&Palette.Glass?1:0);lightX.Snap(lightX.Target);lightY.Snap(lightY.Target);frameDirty=true;Invalidate();}
        protected override void Dispose(bool disposing){if(disposing){MotionClock.Remove(this);ClearMaterial();}base.Dispose(disposing);}
    }
}
