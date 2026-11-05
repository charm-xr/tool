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
        void PaintAppCell(object sender,DataGridViewCellPaintingEventArgs e){if(e.RowIndex<0||e.ColumnIndex!=0)return;var a=grid.Rows[e.RowIndex].Tag as AppEntry;if(a==null)return;e.PaintBackground(e.ClipBounds,true);var g=e.Graphics;int size=Px(compact?25:32),x=e.CellBounds.Left+Px(10),y=e.CellBounds.Top+(e.CellBounds.Height-size)/2;Image img;
            if(appIcons.TryGetValue(a.Id,out img)&&img!=null)g.DrawImage(img,new Rectangle(x,y,size,size));else{using(var b=new SolidBrush(Palette.Soft))g.FillRectangle(b,x,y,size,size);TextRenderer.DrawText(g,a.Name.Length>0?a.Name.Substring(0,1):"?",Font,new Rectangle(x,y,size,size),Teal,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);RequestIcon(a);}
            int textLeft=x+size+Px(11),width=Math.Max(0,e.CellBounds.Right-textLeft-Px(7));int nameTop=e.CellBounds.Top+Px(compact?2:7);TextRenderer.DrawText(g,a.Name,Font,new Rectangle(textLeft,nameTop,width,Px(22)),Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
            using(var small=new Font(Font.FontFamily,8F))TextRenderer.DrawText(g,a.Publisher.Length>0?a.Publisher:"发布者未提供",small,new Rectangle(textLeft,nameTop+Px(22),width,Px(18)),Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);e.Handled=true;
        }
        void PaintLockOwner(object sender,DrawItemEventArgs e){if(e.Index<0)return;var o=lockList.Items[e.Index] as LockOwner;if(o==null)return;bool selected=(e.State&DrawItemState.Selected)!=0;using(var b=new SolidBrush(selected?Palette.Soft:Palette.Surface))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,o.Display,Font,new Rectangle(e.Bounds.X+Px(10),e.Bounds.Y+Px(7),e.Bounds.Width-Px(20),Px(23)),Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);using(var small=new Font(Font.FontFamily,8F))TextRenderer.DrawText(e.Graphics,o.Path,small,new Rectangle(e.Bounds.X+Px(10),e.Bounds.Y+Px(30),e.Bounds.Width-Px(20),Px(19)),Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);e.DrawFocusRectangle();}
        void StyleGrid(){grid.BackgroundColor=Palette.Surface;grid.GridColor=Line;grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Palette.Side,ForeColor=Muted,Font=Font,Padding=new Padding(8,0,0,0),SelectionBackColor=Palette.Side,SelectionForeColor=Muted};grid.ColumnHeadersHeight=Px(35);grid.DefaultCellStyle.BackColor=Palette.Surface;grid.DefaultCellStyle.ForeColor=Ink;grid.DefaultCellStyle.SelectionBackColor=Palette.Soft;grid.DefaultCellStyle.SelectionForeColor=Ink;grid.DefaultCellStyle.Padding=new Padding(8,0,3,0);grid.RowTemplate.Height=Px(compact?44:57);grid.Invalidate();}
        void ApplyTheme(Control control){string role=control.Tag as string;control.ForeColor=role=="muted"?Muted:role=="accent"?Teal:Ink;if(control is TextBox||control is ComboBox||control is ListBox||role=="surface")control.BackColor=Palette.Surface;else if(role=="sidebar")control.BackColor=Palette.Side;else control.BackColor=control.Parent==null?Bg:control.Parent.BackColor;
            if(control==this||control==host||(pages!=null&&pages.Contains(control)))control.BackColor=Bg;foreach(Control child in control.Controls)ApplyTheme(child);control.Invalidate();
        }
        void ToggleTheme(){Palette.Dark=!Palette.Dark;ApplyTheme(this);StyleGrid();UpdateThemeCaption();SetStep(lockPath.Length>0?(lockList.Items.Count>0?1:2):0);SavePreferences();}
        void UpdateThemeCaption(){themeButton.Text=Palette.Dark?"切换浅色外观":"切换深色外观";foreach(var menu in new[]{scanMenu,appMenu,fileMenu}){menu.BackColor=Palette.Surface;menu.ForeColor=Ink;menu.RenderMode=ToolStripRenderMode.System;}}
        void UpdateDensity(){if(grid==null)return;grid.RowTemplate.Height=Px(compact?44:57);foreach(DataGridViewRow row in grid.Rows)row.Height=Px(compact?44:57);densityButton.Text=compact?"切换舒适列表":"切换紧凑列表";lockList.ItemHeight=Px(54);grid.Invalidate();UpdateThemeCaption();}
        void ReadPreferences(){try{foreach(string line in File.ReadAllLines(Path.Combine(Store.Data,"ui-preferences.txt"))){if(line=="theme=dark")Palette.Dark=true;if(line=="density=compact")compact=true;}}catch{}}
        void SavePreferences(){if(loadingPreferences)return;try{Directory.CreateDirectory(Store.Data);File.WriteAllLines(Path.Combine(Store.Data,"ui-preferences.txt"),new[]{"theme="+(Palette.Dark?"dark":"light"),"density="+(compact?"compact":"comfortable")});}catch(Exception e){latestStatus="界面设置未能保存："+e.Message;}}
    }
}
