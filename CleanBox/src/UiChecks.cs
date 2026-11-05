using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace CleanBox {
    public partial class MainForm {
        async Task VerifyInterface(){
            var result=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);result.Add("PASS: "+name);};
            bool originalDark=Palette.Dark,originalCompact=compact,originalGlass=Palette.GlassEnabled,originalMotion=Palette.MotionEnabled;loadingPreferences=true;
            try{
                check(apps.Count>0,"Live inventory populated");check(grid.Rows.Count==apps.Count,"Inventory and grid agree");check(sidebar.Width>=Px(170),"Sidebar scales to the screen DPI");
                for(int i=0;i<3;i++){navigation[i].PerformClick();check(currentPage==i&&pages[i].Visible&&navigation[i].Selected,"Sidebar navigates to page "+i);}SwitchPage(0);
                search.Text="__CleanBox_No_Match_"+Guid.NewGuid().ToString("N");check(grid.Rows.Cast<DataGridViewRow>().All(r=>!r.Visible)&&emptyList.Visible,"Empty search has a visible explanation");check(Selected()==null&&selectionActions.All(b=>!b.Enabled),"Empty results clear stale selection and disable actions");search.Clear();
                filter.SelectedIndex=1;check(grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible).All(r=>((AppEntry)r.Tag).RegistryPath.Length>0),"Installed source filter excludes candidates");
                filter.SelectedIndex=2;check(grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible).All(r=>((AppEntry)r.Tag).Package.Length>0),"Store source filter selects packages");
                filter.SelectedIndex=3;check(grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible).All(r=>IsCandidate((AppEntry)r.Tag)),"Candidate filter includes only unregistered discoveries");filter.SelectedIndex=0;
                search.Text=apps[0].Name;check(grid.Rows.Cast<DataGridViewRow>().Any(r=>r.Visible),"Name search finds an installed entry");check(Selected()!=null&&detailTitle.Text==Selected().Name,"Details follow the selected entry");search.Clear();
                var protectedApp=new AppEntry{Name="UI protected fixture",RegistryPath="UI-fixture-protected",Protected=true,Source="已安装",Command="fake.exe"};
                var candidate=new AppEntry{Name="UI candidate fixture",Executable=Path.Combine(Store.Home,"tests","ui-candidate.exe"),Path=Path.Combine(Store.Home,"tests"),Source="磁盘发现"};
                foreach(var a in new[]{protectedApp,candidate})Add(a);
                search.Text=protectedApp.Name;check(!uninstallButton.Enabled&&!residualButton.Enabled&&!appMenu.Items[0].Enabled&&!appMenu.Items[1].Enabled,"System components keep destructive entry points disabled");
                search.Text=candidate.Name;check(uninstallButton.Text=="核对并处理目录"&&uninstallButton.Enabled&&!appMenu.Items[1].Enabled,"Candidate uses directory verification instead of a fake uninstall action");
                uninstallButton.PerformClick();check(currentPage==1&&pathBox.Text==candidate.Path,"Candidate action opens the correct target in file tools");
                search.Clear();foreach(var a in new[]{protectedApp,candidate}){var row=grid.Rows.Cast<DataGridViewRow>().First(r=>r.Tag==a);grid.Rows.Remove(row);apps.Remove(a);ids.Remove(a.Id);}ApplyFilter();grid.ClearSelection();grid.Rows[0].Selected=true;grid.CurrentCell=grid.Rows[0].Cells[0];grid.FirstDisplayedScrollingRowIndex=0;ShowSelection();
                pathBox.Clear();check(!inspectButton.Enabled&&!recycleButton.Enabled&&!stopButton.Enabled,"Empty target disables file actions");
                pathBox.Text=Path.Combine(Store.Home,"tests","ui-target.txt");lockPath=pathBox.Text;lockList.Items.Add(new LockOwner{Name="UI protected fixture",Pid=4,CanStop=false});lockList.SelectedIndex=0;check(!stopButton.Enabled&&!fileMenu.Items[0].Enabled,"Protected or unverifiable process cannot be stopped");
                pathBox.Text=Path.Combine(Store.Home,"tests","ui-next.txt");check(lockList.Items.Count==0&&lockPath==""&&!stopButton.Enabled,"Changing target invalidates previous lock result");
                Busy(true,true);check(pathBox.ReadOnly&&!inspectButton.Enabled&&!recycleButton.Enabled&&cancel.Visible&&cancel.Enabled&&!quick.Enabled,"Busy state prevents conflicting work and exposes cancellation");Busy(false);check(!pathBox.ReadOnly&&inspectButton.Enabled&&!cancel.Visible,"Idle state restores file actions");
                using(var confirmation=BuildConfirmation("永久删除",@"D:\Example\file.txt",true)){var accept=(Button)confirmation.Controls.Find("confirmationAccept",true)[0];var word=(TextBox)confirmation.Controls.Find("confirmationWord",true)[0];check(!accept.Enabled,"Destructive confirmation starts disabled");word.Text="confirm";check(!accept.Enabled,"Unrelated confirmation text cannot enable deletion");word.Text="确认";check(accept.Enabled,"Explicit confirmation enables only the reviewed operation");}
                string fixture=Path.Combine(Store.Home,"tests","ui-lock-"+Guid.NewGuid().ToString("N")+".txt");Directory.CreateDirectory(Path.GetDirectoryName(fixture));File.WriteAllText(fixture,"CleanBox UI lock fixture");
                check(Safety.Within(fixture,Path.Combine(Store.Home,"tests")),"UI lock fixture stays inside the test directory");
                using(var helper=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--hold-test \""+fixture+"\""){UseShellExecute=false,CreateNoWindow=true})){
                    try{for(int retry=0;retry<100&&!File.Exists(fixture+".ready");retry++)await Task.Delay(50);check(File.Exists(fixture+".ready"),"Independent UI lock fixture started");pathBox.Text=fixture;await Inspect();check(lockList.Items.Cast<LockOwner>().Any(o=>o.Pid==helper.Id)&&lockPath==fixture,"File page shows the actual locking process");}
                    finally{if(!helper.HasExited){helper.Kill();helper.WaitForExit();}if(Safety.Within(fixture,Path.Combine(Store.Home,"tests"))){File.Delete(fixture);File.Delete(fixture+".ready");}}
                }
                pathBox.Clear();
                SwitchPage(0);compact=false;UpdateDensity();int normal=grid.Rows[0].Height;compact=true;UpdateDensity();check(grid.Rows[0].Height<normal,"Compact mode reduces actual row heights");compact=false;UpdateDensity();
                var spring=new SpringValue(.94);spring.Target=1;spring.Velocity=.65;double peak=0;for(int frame=0;frame<180;frame++){spring.Step(1.0/60);peak=Math.Max(peak,spring.Value);}check(peak>1&&peak<1.05&&Math.Abs(spring.Value-1)<.001,"Elastic spring overshoots gently and settles without drift");
                Palette.MotionEnabled=true;var animationProbe=new UiButton{Text="Motion check",Size=new Size(Px(130),Px(42)),Location=new Point(0,0)};pages[currentPage].Controls.Add(animationProbe);int clicks=0;animationProbe.Click+=(s,e)=>clicks++;
                animationProbe.PerformClick();if(Palette.Animate)check(animationProbe.VisualScale<1,"Click starts a real press/rebound presentation");await Task.Delay(900);check(clicks==1&&Math.Abs(animationProbe.VisualScale-1)<.001,"Spring animation never repeats the underlying action");
                animationProbe.PerformClick();Palette.MotionEnabled=false;MotionClock.FinishAll();check(MotionClock.ActiveCount==0&&animationProbe.VisualScale==1,"Disabling motion immediately stops animations");animationProbe.Dispose();Palette.MotionEnabled=true;
                SwitchPage(1);SwitchPage(2);SwitchPage(0);await Task.Delay(1000);check(currentPage==0&&pages[0].Visible&&MotionClock.ActiveCount==0,"Rapid navigation settles on the requested page and stops the clock");
                Palette.GlassEnabled=true;Refresh();int generation=Scene.Generation;Refresh();check(Scene.Generation==generation,"Glass backdrop is cached between repaint frames");
                glassButton.PerformClick();check(!Palette.GlassEnabled,"Glass toggle switches to the solid fallback");glassButton.PerformClick();check(Palette.GlassEnabled,"Glass toggle restores the material");
                check(scanMenu.Renderer is GlassMenuRenderer&&appMenu.Renderer is GlassMenuRenderer,"Action menus use the unified glass renderer");
                check(search.BorderStyle==BorderStyle.None&&search.Parent is InputFrame&&pathBox.Parent is InputFrame,"Native editable fields sit inside rounded input surfaces");
                await VerifyRepainting(check);
                await VerifyNativePainting(check);
                await Task.Delay(900);Directory.CreateDirectory(Path.Combine(Store.Home,"preview"));
                Action<string> capture=name=>{PerformLayout();Refresh();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(Store.Home,"preview",name+".png"),System.Drawing.Imaging.ImageFormat.Png);}};
                Palette.Dark=false;ApplyTheme(this);StyleGrid();UpdateThemeCaption();ClientSize=new Size(Px(1220),Px(780));SwitchPage(0);MotionClock.FinishAll();capture("v212-software-light");
                check(detailPanel.Left>=grid.Parent.Right&&detailPanel.Right<=catalogBody.ClientSize.Width,"List and detail panel do not overlap");
                check(quick.Left>=0&&quick.Right<=quick.Parent.ClientSize.Width,"Scan action stays inside the heading");
                check(search.Parent.Bottom<=search.Parent.Parent.ClientSize.Height&&search.Top>=0&&search.Bottom<=search.Parent.ClientSize.Height&&filter.Bottom<=filter.Parent.ClientSize.Height,"Search field and source filter fit the visible row at screen DPI");
                ClientSize=new Size(Px(1024),Px(662));PerformLayout();ArrangeCatalog();capture("v212-software-compact");
                check(grid.Width>=Px(300)&&detail.Width>Px(180)&&detailTitle.Right<=detailTitle.Parent.ClientSize.Width,"Narrow window preserves usable list and complete detail layout");
                SwitchPage(1);pathBox.Text="";MotionClock.FinishAll();capture("v212-file-tools");check(pathBox.Right<=pathBox.Parent.ClientSize.Width&&lockList.Height>=Px(80),"Narrow file page keeps target and results visible");
                check(pathBox.Parent.Bottom<=pathBox.Parent.Parent.ClientSize.Height&&pathBox.Top>=0&&pathBox.Bottom<=pathBox.Parent.ClientSize.Height,"Path entry fits inside its visible rounded surface");
                foreach(var b in selectionActions)check(b.Top>=0&&b.Bottom<=b.Parent.ClientSize.Height&&b.Right<=b.Parent.ClientSize.Width,"Detail action fits its container: "+b.Text);
                Palette.Dark=true;ApplyTheme(this);StyleGrid();UpdateThemeCaption();SwitchPage(0);MotionClock.FinishAll();capture("v212-software-dark");check(grid.BackgroundColor==Palette.Surface&&detail.BackColor==Palette.Surface&&Ink.GetBrightness()>.5,"Dark theme covers data surfaces and text");
                SwitchPage(1);MotionClock.FinishAll();capture("v212-file-tools-dark");Palette.GlassEnabled=false;ApplyTheme(this);StyleGrid();capture("v212-solid-fallback");
                result.Add("UI checks changed only their own temporary fixture and helper process; no installed software, user files or reboot queue modified.");File.WriteAllLines(Path.Combine(Store.Data,"ui-results.txt"),result,System.Text.Encoding.UTF8);
            }finally{MotionClock.FinishAll();Palette.Dark=originalDark;compact=originalCompact;Palette.GlassEnabled=originalGlass;Palette.MotionEnabled=originalMotion;loadingPreferences=false;}
        }
    }
}
