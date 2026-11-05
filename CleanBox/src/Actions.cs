using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class MainForm {
        bool Idle(){if(!working)return true;MessageBox.Show(this,"请先等待当前操作完成，或取消扫描。","正在处理");return false;}
        void Busy(bool value,bool cancellable=false){working=value;quick.Enabled=disk.Enabled=folder.Enabled=!value;cancel.Enabled=value&&cancellable;cancel.Visible=value&&cancellable;progress.Visible=value;adminButton.Enabled=!value&&!Native.IsAdmin;UpdateFileActions();ShowSelection();UpdateCounts();}
        async Task RunScan(Action<CancellationToken> action,string finished){if(!Idle())return;cts=new CancellationTokenSource();Busy(true,true);try{await Task.Run(()=>action(cts.Token));DrainAll();latestStatus=finished+" · "+apps.Count+" 条";Store.Log(latestStatus);}catch(OperationCanceledException){DrainAll();latestStatus="已停止，保留已发现的 "+apps.Count+" 条结果。";}catch(Exception e){ShowError(e);}finally{Busy(false);cts.Dispose();cts=null;}}
        async Task QuickScan(){if(!Idle())return;apps.Clear();ids.Clear();grid.Rows.Clear();
            ShowSelection();await RunScan(token=>{Scanner.Registered(queue.Enqueue,s=>latestStatus=s,token);try{Scanner.Packages(queue.Enqueue,s=>latestStatus=s,token);}catch(OperationCanceledException){throw;}catch(Exception e){Store.Log("商店应用未能完整读取："+e.Message);}
                Scanner.Shortcuts(queue.Enqueue,s=>latestStatus=s,token);},"基础扫描完成；可用“全盘发现”补充绿色软件。详情见操作记录");}
        async Task ChooseFolder(){if(!Idle())return;using(var f=new FolderBrowserDialog{Description="选择软件存放目录，将递归发现 EXE 程序（只读扫描）",SelectedPath=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),ShowNewFolderButton=false})if(f.ShowDialog(this)==DialogResult.OK){string root=f.SelectedPath;await RunScan(t=>Scanner.Disk(root,queue.Enqueue,s=>latestStatus=s,t),"目录扫描完成");}}
        async Task ChooseDrives(){if(!Idle())return;using(var f=new Form{Text="选择磁盘扫描范围",ClientSize=new Size(540,350),StartPosition=FormStartPosition.CenterParent,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
            var list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true};foreach(var drive in DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed))list.Items.Add(drive.RootDirectory.FullName,true);
            var info=new Label{Dock=DockStyle.Top,Height=118,Padding=new Padding(15),Text="递归查找所选磁盘中的 EXE，补充未登记的软件。\r\n可能需要几分钟，可随时取消并保留结果。\r\n跳过系统保护区、无权限目录、链接及常见开发依赖目录；发现项需人工核对，不保证百分之百识别。"};
            var go=new Button{Text="开始只读扫描",Dock=DockStyle.Bottom,Height=44,DialogResult=DialogResult.OK};f.Controls.Add(list);f.Controls.Add(info);f.Controls.Add(go);if(f.ShowDialog(this)!=DialogResult.OK)return;string[] roots=list.CheckedItems.Cast<string>().ToArray();if(roots.Length==0)return;await RunScan(t=>{foreach(string root in roots)Scanner.Disk(root,queue.Enqueue,s=>latestStatus=s,t);},"磁盘扫描完成（跳过项和限制请见说明）");}}
        void OpenSelected(){var a=Selected();if(a!=null)OpenPath(a.Path.Length>0?a.Path:a.Executable);}
        void OpenPath(string p){try{if(File.Exists(p)||Directory.Exists(p)){Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),(File.Exists(p)?"/select,":"")+"\""+p.TrimEnd('\\')+"\""){UseShellExecute=true});}else MessageBox.Show(this,"路径不存在或未提供。","打开位置");}catch(Exception e){ShowError(e);}}
        void UseSelectedFolder(){var a=Selected();if(a==null)return;if(a.Path.Length==0){MessageBox.Show(this,"该条目没有提供安装目录，请手动选择目标。","清理匣");return;}pathBox.Text=a.Path;SwitchPage(1);}
        async Task UninstallSelected(){if(!Idle())return;var a=Selected();if(a==null)return;if(a.Protected){MessageBox.Show(this,"此项是系统组件，本工具不执行卸载。","受保护项目");return;}
            if(!Confirm("正常卸载",a.Name+"\r\n\r\n"+(a.Package.Length>0?"仅卸载当前用户的软件包：\r\n"+a.Package:"即将启动软件登记的卸载程序：\r\n"+a.Command),false))return;
            Busy(true);try{await Task.Run(()=>Operations.Uninstall(a));MessageBox.Show(this,a.Package.Length>0?"软件包已卸载，重新扫描可更新清单。":"已启动原卸载程序，请在它的窗口完成操作。完成后可检查残留、重新扫描。","卸载");}catch(Exception e){ShowError(e);}finally{Busy(false);}}
        async Task ClearRecord(){if(!Idle())return;var a=Selected();if(a==null)return;if(!Confirm("清除失效记录",a.Name+"\r\n"+a.Hive+"\\"+a.RegistryPath+"\r\n\r\n仅清理不存在的软件留下的卸载列表项，先导出备份；不扫描删除其他注册表键。",true))return;
            Busy(true);try{await Task.Run(()=>Operations.RemoveStaleRecord(a));MessageBox.Show(this,"失效记录已删除，备份保存在 data 目录。重新扫描可刷新列表。","完成");}catch(Exception e){ShowError(e);}finally{Busy(false);}}
        void Export(){using(var f=new SaveFileDialog{Title="导出当前筛选结果",Filter="CSV 表格|*.csv",FileName="软件清单-"+DateTime.Now.ToString("yyyyMMdd")+".csv",InitialDirectory=Store.Home})if(f.ShowDialog(this)==DialogResult.OK)try{Store.Export(apps.Where(Matches),f.FileName);latestStatus="清单已导出："+f.FileName;}catch(Exception e){ShowError(e);}}
        void ResidualDialog(){if(!Idle())return;var a=Selected();if(a==null)return;var paths=Operations.Residuals(a);using(var f=new Form{Text="残留候选 · "+a.Name,ClientSize=new Size(760,440),StartPosition=FormStartPosition.CenterParent,Font=Font,MinimumSize=new Size(660,380)}){
            var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};list.Items.AddRange(paths.Cast<object>().ToArray());
            var note=new Label{Dock=DockStyle.Top,Height=103,Padding=new Padding(15),Text="下列是安装位置和名称完全匹配的配置目录，不代表一定是残留。\r\n请先正常卸载；配置目录可能包含账号、设置和个人数据。\r\n逐个选择后进入文件处理页，核对完整路径再删除。不会自动勾选或删除。"};
            var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(10)};actions.Controls.Add(Button("打开选中位置",()=>{if(list.SelectedItem!=null)OpenPath(list.SelectedItem.ToString());}));actions.Controls.Add(Button("处理选中目录",()=>{if(list.SelectedItem!=null){pathBox.Text=list.SelectedItem.ToString();SwitchPage(1);f.Close();}},true));f.Controls.Add(list);f.Controls.Add(note);f.Controls.Add(actions);if(paths.Count==0)note.Text="未找到安全范围内的明确候选目录。\r\n本工具不做模糊名称批量删除；可手动选择已确认的残留位置。";f.ShowDialog(this);}}
        async Task Inspect(){if(!Idle())return;string p=pathBox.Text;cts=new CancellationTokenSource();Busy(true,true);lockList.Items.Clear();lockPath="";SetStep(1);lockSummary.Text="正在检查文件与进程…";latestStatus="正在检查占用，可随时取消";try{var report=await Task.Run(()=>Native.FindLocks(p,cts.Token));if(pathBox.Text!=p){lockSummary.Text="目标已改变，请重新检查。";return;}lockPath=Safety.Normalize(p);foreach(var o in report.Owners)lockList.Items.Add(o);if(lockList.Items.Count>0)lockList.SelectedIndex=0;lockSummary.Text="检查 "+report.Files+" 个文件，找到 "+report.Owners.Count+" 个相关进程。"+(report.Partial?" 检查不完整（上限 / 权限限制）。":"")+"\r\n"+(report.Note.Length>0?report.Note:"未列出进程不代表没有占用；驱动、文件夹句柄或权限限制可能无法识别。");SetStep(report.Owners.Count>0?1:2);latestStatus=report.Partial?"占用检查部分完成，详情见当前页面":"占用检查完成";Store.Log(latestStatus+"："+lockPath+"；相关进程 "+report.Owners.Count);}catch(OperationCanceledException){lockSummary.Text="已取消检查，请重新检查后再处理。";latestStatus="占用检查已取消";SetStep(0);}catch(Exception e){ShowError(e);lockSummary.Text="检查失败，请查看操作记录。";SetStep(0);}finally{Busy(false);cts.Dispose();cts=null;}}
        async Task StopOwner(bool force){if(!Idle())return;var owner=lockList.SelectedItem as LockOwner;if(owner==null)return;if(!owner.CanStop){MessageBox.Show(this,"该进程属于系统或无法核实身份。可手动退出软件，或对具体文件选择重启后删除。","不能结束此进程");return;}
            if(!Confirm(force?"强制结束进程":"请求正常退出",owner.Display+"\r\n"+owner.Path+(force?"\r\n\r\n该程序尚未保存的内容会丢失。":"\r\n\r\n程序可能会弹出保存提示。"),force))return;
            try{MessageBox.Show(this,Native.Stop(owner,force),"处理结果");await Inspect();}catch(Exception e){ShowError(e);}}
        async Task DeleteTarget(int mode){if(!Idle())return;string p;try{p=Safety.Check(pathBox.Text);}catch(Exception e){ShowError(e);return;}
            string title=mode==0?"移到回收站":mode==1?"永久删除":"登记重启后删除";
            string explanation=mode==0?"将目标及其全部内容交给 Windows 回收站处理。若系统提示无法回收，可取消。":mode==1?"将删除目标及其全部内容，不经过回收站；已删除的项目无法由本工具恢复。":"将在下次系统重启时永久删除目标及其当前内容，不经过回收站。本工具不会替你重启；登记后不能从本工具撤销。";
            if(!Confirm(title,p+"\r\n\r\n"+explanation+"\r\n\r\n请确认这是具体的软件或文件目录，不包含其他需要保留的数据。",mode!=0))return;
            Busy(true);latestStatus="正在"+title+"…";try{
                if(mode==0){Operations.Recycle(p);MessageBox.Show(this,"已完成回收站操作。","完成");}
                else {DeleteResult result=await Task.Run(()=>mode==1?Operations.Permanent(p):Operations.Reboot(p));MessageBox.Show(this,result.Summary+(mode==2?"\r\n成功项已登记在 Windows 中，待重启处理；实际删除结果需重启后确认。":"")+(result.Failed.Count>0?"\r\n\r\n"+string.Join("\r\n",result.Failed.Take(6))+"\r\n完整结果见操作记录。":""),"处理结果",MessageBoxButtons.OK,result.Failed.Count>0?MessageBoxIcon.Warning:MessageBoxIcon.Information);}
                latestStatus=title+"处理结束，详情见操作记录";lockList.Items.Clear();lockPath="";SetStep(2);lockSummary.Text="操作结束；如需再次处理，请先重新检查占用。";
            }catch(Exception e){ShowError(e);}finally{Busy(false);}}
        bool Confirm(string title,string body,bool typeWord){using(var f=BuildConfirmation(title,body,typeWord))return f.ShowDialog(this)==DialogResult.OK;}
        Form BuildConfirmation(string title,string body,bool typeWord){
            var f=new Form{Text=title,ClientSize=new Size(640,typeWord?430:360),StartPosition=FormStartPosition.CenterParent,Font=Font,AutoScaleMode=AutoScaleMode.None,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Padding=new Padding(22)};f.SuspendLayout();
            var heading=new Label{Text=title,Dock=DockStyle.Top,Height=48,Font=new Font(Font.FontFamily,16,FontStyle.Bold),AutoEllipsis=true};
            var target=new TextBox{Name="confirmationTarget",Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=body,BorderStyle=BorderStyle.None};
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=50,Padding=new Padding(0,10,0,0),FlowDirection=FlowDirection.RightToLeft};
            var yes=new UiButton{Name="confirmationAccept",Text="确认继续",Kind=typeWord?"danger":"primary",Width=122,Height=36,DialogResult=DialogResult.OK,Enabled=!typeWord,Margin=new Padding(8,0,0,0)};
            var no=new UiButton{Name="confirmationCancel",Text="取消",Width=100,Height=36,DialogResult=DialogResult.Cancel,Margin=new Padding(8,0,0,0)};bottom.Controls.Add(no);bottom.Controls.Add(yes);f.CancelButton=no;
            f.Controls.Add(target);if(typeWord){var entry=new Panel{Dock=DockStyle.Bottom,Height=64,Padding=new Padding(0,5,0,0)};var label=new Label{Text="输入“确认”以继续",Dock=DockStyle.Top,Height=27,Tag="muted"};var word=new TextBox{Name="confirmationWord",Dock=DockStyle.Top};word.TextChanged+=(s,e)=>yes.Enabled=word.Text=="确认";entry.Controls.Add(word);entry.Controls.Add(label);f.Controls.Add(entry);}
            f.Controls.Add(bottom);f.Controls.Add(heading);ApplyTheme(f);if(dpiScale!=1)f.Scale(new SizeF(dpiScale,dpiScale));f.ResumeLayout(true);f.Shown+=(s,e)=>no.Focus();return f;
        }
        void Elevate(){if(!Idle())return;try{string target=string.IsNullOrWhiteSpace(pathBox.Text)?"":Safety.Normalize(pathBox.Text);Process.Start(new ProcessStartInfo(Application.ExecutablePath,target.Length>0?"--target \""+target+"\"":""){UseShellExecute=true,Verb="runas",WorkingDirectory=Store.Home});Close();}catch(Exception e){ShowError(e);}}
        void ShowError(Exception e){latestStatus=e.Message;Store.Log("操作未完成："+e.Message);MessageBox.Show(this,e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
}
