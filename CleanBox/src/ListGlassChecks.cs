using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class MainForm {
        static double DifferenceWithin(Bitmap first,Bitmap second,Rectangle region){
            region.Intersect(new Rectangle(Point.Empty,first.Size));
            using(var a=first.Clone(region,first.PixelFormat))using(var b=second.Clone(region,second.PixelFormat))return PixelDifference(a,b);
        }
        static bool SameOutside(Bitmap first,Bitmap second,Rectangle allowed){
            for(int y=0;y<first.Height;y++)for(int x=0;x<first.Width;x++)if(!allowed.Contains(x,y)&&first.GetPixel(x,y)!=second.GetPixel(x,y))return false;
            return true;
        }
        async Task VerifyListGlass(Action<bool,string> check){
            bool originalDark=Palette.Dark,originalGlass=Palette.GlassEnabled,originalMotion=Palette.MotionEnabled;
            try{
                Palette.GlassEnabled=true;Palette.MotionEnabled=true;SwitchPage(0);
                foreach(bool dark in new[]{false,true}){
                    Palette.Dark=dark;RefreshMaterial();string mode=dark?"Dark":"Light";
                    // These DWM colors are documented as setter-only attributes.
                    // Verify Windows accepts them; WM_PRINT/DrawToBitmap cannot
                    // validate the compositor-rendered non-client caption pixels.
                    bool applied=WindowChrome.Apply(Handle);
                    File.AppendAllText(Path.Combine(Store.Data,"window-chrome-checks.txt"),mode+": "+WindowChrome.Diagnostic+Environment.NewLine);
                    if(Environment.OSVersion.Version.Build>=22000)check(applied,mode+" Windows accepts the caption, text and border theme colors: "+WindowChrome.Diagnostic);
                    using(var backdrop=new Bitmap(ClientSize.Width,ClientSize.Height)){
                        using(var g=Graphics.FromImage(backdrop))Scene.Draw(g,ClientSize,Point.Empty,false);
                        bool joined=true;
                        foreach(int x in new[]{10,ClientSize.Width/2,ClientSize.Width-11}){
                            Color c=backdrop.GetPixel(x,2),top=Palette.WindowTop;
                            if(Math.Abs(c.R-top.R)+Math.Abs(c.G-top.G)+Math.Abs(c.B-top.B)>6)joined=false;
                        }
                        check(joined,mode+" client top edge joins the native caption color across the window");
                    }
                    using(var probe=new SmoothGrid{Size=new Size(Px(520),Px(260)),Location=new Point(Px(30),Px(90)),ReadOnly=true,AllowUserToAddRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BorderStyle=BorderStyle.None,EnableHeadersVisualStyles=false,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None}){
                        pages[0].Controls.Add(probe);probe.Font=Font;
                        probe.Columns.Add("name","软件 / 发布者");probe.Columns.Add("version","版本");probe.Columns.Add("source","来源");probe.Columns[0].FillWeight=60;probe.Columns[1].FillWeight=20;probe.Columns[2].FillWeight=20;
                        probe.DefaultCellStyle.ForeColor=Palette.Ink;probe.DefaultCellStyle.SelectionForeColor=Palette.Ink;probe.DefaultCellStyle.BackColor=Palette.Surface;probe.DefaultCellStyle.SelectionBackColor=Palette.Soft;
                        probe.ColumnHeadersDefaultCellStyle.ForeColor=Palette.Muted;probe.ColumnHeadersDefaultCellStyle.BackColor=Palette.Side;probe.ColumnHeadersHeight=Px(35);probe.RowTemplate.Height=Px(45);
                        for(int i=0;i<18;i++)probe.Rows.Add("示例软件 "+i.ToString("00"),"2.1.5","已安装");
                        probe.CreateControl();probe.BringToFront();probe.Rows[0].Selected=true;probe.CurrentCell=probe.Rows[0].Cells[0];
                        using(var idle=PaintWindowFrame(probe,Color.Magenta)){
                            using(var second=PaintWindowFrame(probe,Color.Lime))check(SamePixels(idle,second),mode+" glass list paints complete native frames without old pixels");
                            var row=probe.GetCellDisplayRectangle(0,2,false);int materialGeneration=probe.MaterialGeneration,selected=probe.CurrentCell.RowIndex;var bounds=probe.Bounds;
                            probe.MoveLight(new Point(row.Left+row.Width*3/4,row.Top+row.Height/2));
                            for(int i=0;i<150;i++)probe.Advance(1.0/60);
                            using(var lit=PaintWindowFrame(probe,Color.Lime)){
                                var activeCard=Rectangle.Ceiling(probe.CardBounds(2));
                                check(DifferenceWithin(idle,lit,activeCard)>2,mode+" software row shows visible button-style pointer light");
                                check(SameOutside(idle,lit,activeCard),mode+" row light never spills into adjacent rows, gaps or the header");
                                var corner=new Point(activeCard.Left+2,activeCard.Top+2);
                                check(idle.GetPixel(corner.X,corner.Y)==lit.GetPixel(corner.X,corner.Y),mode+" row light respects the rounded corner cutout");
                                lit.Save(Path.Combine(Store.Home,"preview","v220-list-"+(dark?"dark":"light")+"-hover.png"),ImageFormat.Png);
                            }
                            check(probe.MaterialGeneration==materialGeneration&&probe.Bounds==bounds&&probe.CurrentCell.RowIndex==selected,mode+" list light reuses its material without moving rows or changing selection");
                            var nextRow=probe.GetCellDisplayRectangle(0,3,false);probe.MoveLight(new Point(nextRow.Width/2,nextRow.Top+nextRow.Height/2));
                            for(int i=0;i<150;i++)probe.Advance(1.0/60);
                            using(var moved=PaintWindowFrame(probe,Color.Lime))check(SameOutside(idle,moved,Rectangle.Ceiling(probe.CardBounds(3))),mode+" changing rows clears the previous row instead of carrying a large light cloud");
                            probe.MoveLight(new Point(row.Width/2,row.Top));for(int i=0;i<150;i++)probe.Advance(1.0/60);
                            using(var gap=PaintWindowFrame(probe,Color.Lime))check(SamePixels(idle,gap),mode+" pointer in the spacing between cards does not light a row");
                            probe.MoveLight(new Point(probe.Width/2,probe.ColumnHeadersHeight/2));for(int i=0;i<150;i++)probe.Advance(1.0/60);
                            using(var header=PaintWindowFrame(probe,Color.Lime))check(DifferenceWithin(idle,header,Rectangle.Ceiling(probe.CardBounds(-1)))>2&&SameOutside(idle,header,Rectangle.Ceiling(probe.CardBounds(-1))),mode+" header light stays inside its own rounded glass bar");
                            probe.LeaveLight();await Task.Delay(1100);
                            using(var after=PaintWindowFrame(probe,Color.Lime)){
                                check(SamePixels(idle,after),mode+" list pointer exit restores exact idle pixels with no residual light");
                                after.Save(Path.Combine(Store.Home,"preview","v220-list-"+(dark?"dark":"light")+"-idle.png"),ImageFormat.Png);
                            }
                            check(MotionClock.ActiveCount==0,mode+" list light stops its animation timer after exit");
                            Palette.MotionEnabled=false;probe.MoveLight(new Point(row.Width/2,row.Top+row.Height/2));
                            using(var instant=PaintWindowFrame(probe,Color.Lime))check(DifferenceWithin(idle,instant,Rectangle.Ceiling(probe.CardBounds(2)))>2&&MotionClock.ActiveCount==0,mode+" list light works without animation when motion is disabled");
                            probe.LeaveLight();using(var after=PaintWindowFrame(probe,Color.Magenta))check(SamePixels(idle,after),mode+" motion-disabled list clears light immediately on exit");
                            Palette.GlassEnabled=false;probe.RefreshMaterial();
                            using(var solid=PaintWindowFrame(probe,Color.Lime)){
                                check(PixelDifference(idle,solid)>2,mode+" list glass has a distinct material from solid fallback");
                                probe.MoveLight(new Point(row.Width/2,row.Top));using(var moved=PaintWindowFrame(probe,Color.Magenta))check(SamePixels(solid,moved),mode+" disabling glass removes list lighting");
                            }
                            Palette.GlassEnabled=true;Palette.MotionEnabled=true;probe.RefreshMaterial();
                            probe.FirstDisplayedScrollingRowIndex=6;using(var scrolled=PaintWindowFrame(probe,Color.Magenta))using(var repeat=PaintWindowFrame(probe,Color.Lime))check(SamePixels(scrolled,repeat)&&probe.FirstDisplayedScrollingRowIndex==6,mode+" list scrolling redraws cleanly");
                            probe.Sort(probe.Columns[0],System.ComponentModel.ListSortDirection.Descending);check((string)probe.Rows[0].Cells[0].Value=="示例软件 17",mode+" glass header preserves native column sorting");
                        }
                    }
                    var liveGrid=(SmoothGrid)grid;var liveRow=grid.GetCellDisplayRectangle(0,2,false);
                    liveGrid.MoveLight(new Point(liveRow.Left+liveRow.Width/2,liveRow.Top+liveRow.Height/2));
                    for(int frame=0;frame<150;frame++)liveGrid.Advance(1.0/60);
                    using(var live=PaintWindowFrame(grid,Color.Magenta))live.Save(Path.Combine(Store.Home,"preview","v220-live-rows-"+(dark?"dark":"light")+".png"),ImageFormat.Png);
                    liveGrid.LeaveLight();liveGrid.Settle();MotionClock.Remove(liveGrid);
                }
            }finally{Palette.Dark=originalDark;Palette.GlassEnabled=originalGlass;Palette.MotionEnabled=originalMotion;RefreshMaterial();}
        }
    }
}
