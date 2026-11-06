using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace CleanBox {
    public partial class MainForm {
        async void RequestIcon(AppEntry a){if(a==null||closing||appIcons.ContainsKey(a.Id)||iconPending.Contains(a.Id)||activeIcons>=2)return;iconPending.Add(a.Id);activeIcons++;
            Image image=await Task.Run<Image>(()=>{try{string p=a.Executable;if(string.IsNullOrWhiteSpace(p)||p.StartsWith("\\\\")||!Path.IsPathRooted(p)||!File.Exists(p))return null;using(var icon=Icon.ExtractAssociatedIcon(p))return icon==null?null:icon.ToBitmap();}catch{return null;}});
            activeIcons--;iconPending.Remove(a.Id);if(closing||IsDisposed){if(image!=null)image.Dispose();return;}appIcons[a.Id]=image;grid.Invalidate();var current=Selected();if(current!=null&&current.Id==a.Id)detailIcon.Image=image;
        }
        void PaintAppCell(object sender,DataGridViewCellPaintingEventArgs e){if(e.RowIndex<0||e.ColumnIndex!=0)return;var a=grid.Rows[e.RowIndex].Tag as AppEntry;if(a==null)return;if(!Palette.Glass)e.PaintBackground(e.ClipBounds,true);var g=e.Graphics;int size=Px(compact?25:32),x=e.CellBounds.Left+Px(10),y=e.CellBounds.Top+(e.CellBounds.Height-size)/2;Image img;
            const TextFormatFlags preserve=TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform;
            if(appIcons.TryGetValue(a.Id,out img)&&img!=null)g.DrawImage(img,new Rectangle(x,y,size,size));else{using(var b=new SolidBrush(Palette.Soft))g.FillRectangle(b,x,y,size,size);TextRenderer.DrawText(g,a.Name.Length>0?a.Name.Substring(0,1):"?",Font,new Rectangle(x,y,size,size),Teal,preserve|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);RequestIcon(a);}
            int textLeft=x+size+Px(11),width=Math.Max(0,e.CellBounds.Right-textLeft-Px(7));int nameTop=e.CellBounds.Top+Px(compact?2:7);TextRenderer.DrawText(g,a.Name,Font,new Rectangle(textLeft,nameTop,width,Px(22)),Ink,preserve|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
            using(var small=new Font(Font.FontFamily,8F))TextRenderer.DrawText(g,a.Publisher.Length>0?a.Publisher:"发布者未提供",small,new Rectangle(textLeft,nameTop+Px(22),width,Px(18)),Muted,preserve|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);e.Handled=true;
        }
        void PaintLockOwner(object sender,DrawItemEventArgs e){if(e.Index<0)return;var o=lockList.Items[e.Index] as LockOwner;if(o==null)return;bool selected=(e.State&DrawItemState.Selected)!=0;using(var b=new SolidBrush(selected?Palette.Soft:Palette.Surface))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,o.Display,Font,new Rectangle(e.Bounds.X+Px(10),e.Bounds.Y+Px(7),e.Bounds.Width-Px(20),Px(23)),Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);using(var small=new Font(Font.FontFamily,8F))TextRenderer.DrawText(e.Graphics,o.Path,small,new Rectangle(e.Bounds.X+Px(10),e.Bounds.Y+Px(30),e.Bounds.Width-Px(20),Px(19)),Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);e.DrawFocusRectangle();}
        void StyleGrid(){grid.BackgroundColor=Palette.Surface;grid.GridColor=Line;grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Palette.Side,ForeColor=Muted,Font=Font,Padding=new Padding(8,0,0,0),SelectionBackColor=Palette.Side,SelectionForeColor=Muted};grid.ColumnHeadersHeight=Px(35);grid.DefaultCellStyle.BackColor=Palette.Surface;grid.DefaultCellStyle.ForeColor=Ink;grid.DefaultCellStyle.SelectionBackColor=Palette.Soft;grid.DefaultCellStyle.SelectionForeColor=Ink;grid.DefaultCellStyle.Padding=new Padding(8,0,3,0);grid.RowTemplate.Height=Px(compact?44:57);((SmoothGrid)grid).RefreshMaterial();grid.Invalidate();}
        void ApplyTheme(Control control){if(control==this&&IsHandleCreated)WindowChrome.Apply(Handle);string role=control.Tag as string;control.ForeColor=role=="muted"?Muted:role=="accent"?Teal:Ink;
            if(control is TextBox||control is ComboBox||control is ListBox||control is DataGridView)control.BackColor=Palette.Surface;
            else if(control is UiButton||control is CanvasPanel||control is CanvasFlowPanel||control is CanvasTablePanel||control is InputFrame)control.BackColor=Bg;
            else if(control is Panel||control is Label||control is PictureBox)control.BackColor=Color.Transparent;
            else control.BackColor=Bg;
            foreach(Control child in control.Controls.Cast<Control>().ToArray())ApplyTheme(child);ScrollSkin.Attach(control);control.Invalidate();
        }
        void RefreshMaterial(){MotionClock.FinishAll();SuspendLayout();try{Scene.Dispose();ApplyTheme(this);StyleGrid();UpdateThemeCaption();SetStep(lockPath.Length>0?(lockList.Items.Count>0?1:2):0);}finally{ResumeLayout(true);}Invalidate(true);Update();}
        void ToggleTheme(){Palette.Dark=!Palette.Dark;RefreshMaterial();SavePreferences();}
        void ToggleGlass(){Palette.GlassEnabled=!Palette.GlassEnabled;RefreshMaterial();SavePreferences();}
        void ToggleMotion(){Palette.MotionEnabled=!Palette.MotionEnabled;if(!Palette.Animate)MotionClock.FinishAll();UpdateThemeCaption();SavePreferences();}
        void UpdateThemeCaption(){((UiToggle)themeButton).Checked=Palette.Dark;((UiToggle)densityButton).Checked=compact;((UiToggle)glassButton).Checked=Palette.GlassEnabled;((UiToggle)motionButton).Checked=Palette.MotionEnabled;foreach(var menu in new[]{scanMenu,appMenu,fileMenu})GlassMenus.Style(menu);}
        void UpdateDensity(){if(grid==null)return;grid.RowTemplate.Height=Px(compact?44:57);foreach(DataGridViewRow row in grid.Rows)row.Height=Px(compact?44:57);lockList.ItemHeight=Px(54);grid.Invalidate();UpdateThemeCaption();}
        void ReadPreferences(){try{foreach(string line in File.ReadAllLines(Path.Combine(Store.Data,"ui-preferences.txt"))){if(line=="theme=dark")Palette.Dark=true;if(line=="density=compact")compact=true;if(line=="glass=off")Palette.GlassEnabled=false;if(line=="motion=off")Palette.MotionEnabled=false;}}catch{}}
        void SavePreferences(){if(loadingPreferences)return;try{Directory.CreateDirectory(Store.Data);File.WriteAllLines(Path.Combine(Store.Data,"ui-preferences.txt"),new[]{"theme="+(Palette.Dark?"dark":"light"),"density="+(compact?"compact":"comfortable"),"glass="+(Palette.GlassEnabled?"on":"off"),"motion="+(Palette.MotionEnabled?"on":"off")});}catch(Exception e){latestStatus="界面设置未能保存："+e.Message;}}
    }
}
