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
    public class MainForm:Form {
        static readonly Color Ink=Color.FromArgb(23,40,61), Muted=Color.FromArgb(98,115,135), Teal=Color.FromArgb(0,123,117), Bg=Color.FromArgb(242,246,250), Line=Color.FromArgb(221,229,237);
        readonly List<AppEntry> apps=new List<AppEntry>();readonly HashSet<string> ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentQueue<AppEntry> queue=new ConcurrentQueue<AppEntry>();readonly ConcurrentQueue<string> logs=new ConcurrentQueue<string>();
        DataGridView grid;TextBox search,detail,pathBox,logBox;ComboBox filter;Label status,totalLabel,installedLabel,foundLabel,lockSummary;ListBox lockList;TabControl tabs;Button quick,disk,folder,cancel,adminButton;ProgressBar progress;
        CancellationTokenSource cts;bool working;volatile string latestStatus="准备就绪";System.Windows.Forms.Timer timer;string lockPath="";
        public MainForm(string initialTarget=null,bool uiCheck=false) {
            Text="清理匣 CleanBox · 软件扫描与文件清理";ClientSize=new Size(1180,840);MinimumSize=new Size(1040,760);StartPosition=FormStartPosition.CenterScreen;BackColor=Bg;Font=new Font("Microsoft YaHei UI",9F);ForeColor=Ink;AutoScaleMode=AutoScaleMode.Dpi;
            try{Icon=new Icon(Path.Combine(Store.Home,"assets","cleanbox.ico"));}catch{}
            var header=new Panel{Dock=DockStyle.Top,Height=93,BackColor=Ink,Padding=new Padding(25,12,25,10)};
            header.Controls.Add(new Label{Text="清理匣  /  CleanBox",AutoSize=true,Font=new Font(Font.FontFamily,20,FontStyle.Bold),ForeColor=Color.White,Location=new Point(25,10)});
            header.Controls.Add(new Label{Text="找到藏起来的软件，让占用和残留有处可查。",AutoSize=true,ForeColor=Color.FromArgb(180,201,217),Location=new Point(28,58)});
            var admin=Button("管理员模式",()=>Elevate(),false);adminButton=admin;admin.AutoSize=false;admin.Dock=DockStyle.Top;admin.Height=37;admin.BackColor=Color.FromArgb(48,68,91);admin.ForeColor=Color.White;
            if(Native.IsAdmin){admin.Text="管理员模式 · 已启用";admin.Enabled=false;}var adminBox=new Panel{Dock=DockStyle.Right,Width=215,Padding=new Padding(0,13,0,0)};adminBox.Controls.Add(admin);header.Controls.Add(adminBox);
            var foot=new Panel{Dock=DockStyle.Bottom,Height=40,Padding=new Padding(20,9,20,6),BackColor=Color.White};
            progress=new ProgressBar{Dock=DockStyle.Right,Width=120,Style=ProgressBarStyle.Marquee,Visible=false};status=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,Text="准备就绪",ForeColor=Muted};foot.Controls.Add(status);foot.Controls.Add(progress);
            var stats=new TableLayoutPanel{Dock=DockStyle.Top,Height=78,Padding=new Padding(20,10,20,6),ColumnCount=3};for(int i=0;i<3;i++)stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33F));
            totalLabel=Stat(stats,0,"0","当前条目");installedLabel=Stat(stats,1,"0","已登记 / 商店应用");foundLabel=Stat(stats,2,"0","额外发现 · 待核对");
            tabs=new TabControl{Dock=DockStyle.Fill,Padding=new Point(22,9)};tabs.TabPages.Add(BuildCatalog());tabs.TabPages.Add(BuildFiles());tabs.TabPages.Add(BuildLog());
            var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(20,3,20,12)};body.Controls.Add(tabs);
            Controls.Add(body);Controls.Add(stats);Controls.Add(header);Controls.Add(foot);
            timer=new System.Windows.Forms.Timer{Interval=250};timer.Tick+=(s,e)=>Drain();timer.Start();Store.Logged+=OnLog;
            Shown+=async(s,e)=>{if(!string.IsNullOrEmpty(initialTarget)){pathBox.Text=initialTarget;tabs.SelectedIndex=1;}await QuickScan();if(uiCheck){try{await Task.Delay(400);VerifyInterface();}catch(Exception ex){File.WriteAllText(Path.Combine(Store.Data,"ui-results.txt"),"FAIL\r\n"+ex);Environment.ExitCode=1;}Close();}};
            FormClosing+=(s,e)=>{if(working){MessageBox.Show(this,"请先取消扫描，或等待当前操作完成，再退出。","任务仍在进行");e.Cancel=true;}};
            FormClosed+=(s,e)=>{timer.Stop();Store.Logged-=OnLog;if(cts!=null)cts.Dispose();};
        }
        void OnLog(string s){logs.Enqueue(s);}
        Label Stat(TableLayoutPanel table,int column,string value,string caption){var p=new Panel{Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(column==0?0:8,0,0,0)};var l=new Label{Text=value,Font=new Font(Font.FontFamily,21,FontStyle.Bold),ForeColor=Teal,AutoSize=true,Location=new Point(15,9)};p.Controls.Add(l);p.Controls.Add(new Label{Text=caption,AutoSize=true,ForeColor=Muted,Location=new Point(110,22)});table.Controls.Add(p,column,0);return l;}
        static Button Button(string text,Action click,bool primary=false){var b=new Button{Text=text,AutoSize=true,Height=34,MinimumSize=new Size(100,34),FlatStyle=FlatStyle.Flat,BackColor=primary?Teal:Color.White,ForeColor=primary?Color.White:Ink,Padding=new Padding(9,0,9,0),Margin=new Padding(0,0,8,7),Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=primary?Teal:Line;b.Click+=(s,e)=>click();return b;}
        static FlowLayoutPanel Bar(int height=45){return new FlowLayoutPanel{Dock=DockStyle.Top,Height=height,Padding=new Padding(0,5,0,0),WrapContents=true};}
        TabPage Page(string title){return new TabPage(title){BackColor=Bg,Padding=new Padding(12)};}
        TabPage BuildCatalog(){
            var p=Page("软件清单");var toolbar=Bar();quick=Button("重新扫描",async()=>await QuickScan(),true);disk=Button("全盘发现",async()=>await ChooseDrives());folder=Button("扫描文件夹",async()=>await ChooseFolder());cancel=Button("取消扫描",()=>{if(cts!=null)cts.Cancel();});cancel.Enabled=false;
            toolbar.Controls.AddRange(new Control[]{quick,disk,folder,Button("导出清单",Export),cancel});
            var filters=new TableLayoutPanel{Dock=DockStyle.Top,Height=42,ColumnCount=2};filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));
            search=new TextBox{Dock=DockStyle.Fill,Margin=new Padding(0,4,12,5)};search.TextChanged+=(s,e)=>ApplyFilter();search.AccessibleName="搜索软件名称、发布者或路径";
            filter=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,4,0,5)};filter.Items.AddRange(new object[]{"全部来源","已安装","商店 / MSIX","快捷方式","磁盘发现"});filter.SelectedIndex=0;filter.SelectedIndexChanged+=(s,e)=>ApplyFilter();filters.Controls.Add(search,0,0);filters.Controls.Add(filter,1,0);
            var hint=new Label{Dock=DockStyle.Top,Height=27,Text="搜索名称、发布者或路径。磁盘发现项是程序候选，需核对目录；不会自动删除。",ForeColor=Muted};
            grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EnableHeadersVisualStyles=false};
            grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.FromArgb(229,237,244),ForeColor=Ink,Font=new Font(Font,FontStyle.Bold)};grid.ColumnHeadersHeight=36;grid.RowTemplate.Height=32;grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(213,238,235);grid.DefaultCellStyle.SelectionForeColor=Ink;grid.DefaultCellStyle.Padding=new Padding(5,0,0,0);grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,252);grid.GridColor=Line;
            AddColumn("软件名称",28);AddColumn("版本",13);AddColumn("发布者",20);AddColumn("发现来源",20);AddColumn("位置",32);grid.SelectionChanged+=(s,e)=>ShowSelection();grid.CellDoubleClick+=(s,e)=>OpenSelected();
            var bottom=new Panel{Dock=DockStyle.Bottom,Height=155,Padding=new Padding(0,9,0,0)};
            detail=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle,Text="选择软件后，可查看位置、卸载入口和发现来源。"};
            var actions=Bar(45);actions.Dock=DockStyle.Bottom;actions.Controls.AddRange(new Control[]{Button("正常卸载",async()=>await UninstallSelected(),true),Button("打开位置",OpenSelected),Button("检查残留",ResidualDialog),Button("处理安装目录",UseSelectedFolder),Button("清除失效记录",async()=>await ClearRecord())});bottom.Controls.Add(detail);bottom.Controls.Add(actions);
            p.Controls.Add(grid);p.Controls.Add(bottom);p.Controls.Add(hint);p.Controls.Add(filters);p.Controls.Add(toolbar);return p;
        }
        void AddColumn(string title,float weight){int i=grid.Columns.Add("col"+grid.Columns.Count,title);grid.Columns[i].FillWeight=weight;grid.Columns[i].MinimumWidth=85;}
        TabPage BuildFiles(){
            var p=Page("文件占用与删除");var intro=new Label{Dock=DockStyle.Top,Height=50,Text="文件删不掉？先找到谁在使用它。\r\n选择或拖入文件 / 文件夹；所有删除操作都会再次显示完整目标。",ForeColor=Muted};
            var pathRow=new TableLayoutPanel{Dock=DockStyle.Top,Height=43,ColumnCount=3};pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));
            pathBox=new TextBox{Dock=DockStyle.Fill,Margin=new Padding(0,7,10,0),AllowDrop=true};pathBox.TextChanged+=(s,e)=>{lockList.Items.Clear();lockPath="";lockSummary.Text="路径已改变，点击“检查占用”。";};
            pathBox.DragEnter+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};pathBox.DragDrop+=(s,e)=>{var paths=(string[])e.Data.GetData(DataFormats.FileDrop);if(paths.Length>0)pathBox.Text=paths[0];};
            pathRow.Controls.Add(pathBox,0,0);pathRow.Controls.Add(Button("选择文件",()=>{using(var f=new OpenFileDialog{Title="选择需要处理的文件"})if(f.ShowDialog(this)==DialogResult.OK)pathBox.Text=f.FileName;}),1,0);
            pathRow.Controls.Add(Button("选择文件夹",()=>{using(var f=new FolderBrowserDialog{Description="选择需要处理的具体文件夹",ShowNewFolderButton=false})if(f.ShowDialog(this)==DialogResult.OK)pathBox.Text=f.SelectedPath;}),2,0);
            var inspect=Bar();inspect.Controls.AddRange(new Control[]{Button("检查占用",async()=>await Inspect(),true),Button("打开目标位置",()=>OpenPath(pathBox.Text)),Button("正常退出选中程序",async()=>await StopOwner(false)),Button("强制结束选中程序",async()=>await StopOwner(true))});
            lockSummary=new Label{Dock=DockStyle.Top,Height=48,ForeColor=Muted,Text="尚未检查。Windows 的占用检测可能无法显示驱动或文件夹句柄。"};
            lockList=new ListBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle,HorizontalScrollbar=true,BackColor=Color.White,IntegralHeight=false,DisplayMember="Display",ItemHeight=28};
            var bottom=new Panel{Dock=DockStyle.Bottom,Height=140,Padding=new Padding(0,14,0,0)};var deleteBar=Bar(48);deleteBar.Controls.AddRange(new Control[]{Button("移到回收站",async()=>await DeleteTarget(0),true),Button("永久删除…",async()=>await DeleteTarget(1)),Button("重启后删除…",async()=>await DeleteTarget(2))});
            bottom.Controls.Add(new Label{Dock=DockStyle.Fill,ForeColor=Muted,Text="先正常退出程序，再删除。强制结束会丢失未保存内容。\r\n重启后删除需管理员权限，不经回收站，不会立即重启；此处不支持撤销。\r\n对软件优先正常卸载；系统进程和目录链接不会被强行处理。",Padding=new Padding(0,3,0,0)});bottom.Controls.Add(deleteBar);
            p.Controls.Add(lockList);p.Controls.Add(bottom);p.Controls.Add(lockSummary);p.Controls.Add(inspect);p.Controls.Add(pathRow);p.Controls.Add(intro);return p;
        }
        TabPage BuildLog(){var p=Page("操作记录与说明");var bar=Bar();bar.Controls.AddRange(new Control[]{Button("打开日志目录",()=>OpenPath(Store.Data)),Button("使用说明",()=>{try{Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"notepad.exe"),"\""+Path.Combine(Store.Home,"使用说明.txt")+"\""){UseShellExecute=true});}catch(Exception e){ShowError(e);}})});
            logBox=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BackColor=Color.White,Font=new Font("Consolas",10)};
            try{string file=Path.Combine(Store.Data,"operations.log");if(File.Exists(file)){string text=File.ReadAllText(file);logBox.Text=text.Length>100000?text.Substring(text.Length-100000):text;}}catch{}
            p.Controls.Add(logBox);p.Controls.Add(bar);return p;}
        void Drain(){AppEntry a;int n=0;while(n++<300&&queue.TryDequeue(out a))Add(a);status.Text=latestStatus;string l;while(logs.TryDequeue(out l))logBox.AppendText(l+Environment.NewLine);totalLabel.Text=apps.Count.ToString();installedLabel.Text=apps.Count(x=>x.RegistryPath.Length>0||x.Package.Length>0).ToString();foundLabel.Text=apps.Count(x=>x.RegistryPath.Length==0&&x.Package.Length==0).ToString();}
        void DrainAll(){while(!queue.IsEmpty)Drain();}
        void Add(AppEntry a){if(ids.Contains(a.Id))return;
            if(a.RegistryPath.Length>0&&apps.Any(x=>x.RegistryPath==a.RegistryPath&&x.Hive==a.Hive&&x.Name==a.Name&&x.Version==a.Version&&x.Path==a.Path&&x.Command==a.Command))return;
            if(a.RegistryPath.Length==0&&a.Package.Length==0&&apps.Any(x=>(x.RegistryPath.Length>0&&x.Path.Equals(a.Path,StringComparison.OrdinalIgnoreCase)&&x.Name.Equals(a.Name,StringComparison.OrdinalIgnoreCase))||(x.Executable.Equals(a.Executable,StringComparison.OrdinalIgnoreCase)&&a.Executable.Length>0)))return;
            if(a.Source.StartsWith("磁盘")&&apps.Any(x=>x.Source.StartsWith("磁盘")&&x.Path.Equals(a.Path,StringComparison.OrdinalIgnoreCase)&&x.Name.Equals(a.Name,StringComparison.OrdinalIgnoreCase)))return;
            ids.Add(a.Id);apps.Add(a);int i=grid.Rows.Add(a.Name+(a.Protected?"  [系统组件]":""),a.Version,a.Publisher,a.Source,a.Path);grid.Rows[i].Tag=a;if(!Matches(a))grid.Rows[i].Visible=false;
        }
        bool Matches(AppEntry a){string term=search.Text.Trim();string category=filter.SelectedIndex==0?"":filter.Text;return (category.Length==0||a.Source.StartsWith(category))&&(term.Length==0||(a.Name+" "+a.Publisher+" "+a.Path+" "+a.Executable).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0);}
        void ApplyFilter(){grid.CurrentCell=null;foreach(DataGridViewRow row in grid.Rows)row.Visible=Matches((AppEntry)row.Tag);}
        AppEntry Selected(){return grid.SelectedRows.Count>0?grid.SelectedRows[0].Tag as AppEntry:null;}
        void ShowSelection(){var a=Selected();if(a==null)return;detail.Text=a.Name+(a.Protected?"  · Windows 系统组件":"")+"\r\n来源："+a.Source+"\r\n位置（请核对）："+(a.Path.Length>0?a.Path:"未提供")+"\r\n"+(a.Package.Length>0?"软件包："+a.Package:"卸载入口："+(a.Command.Length>0?a.Command:"未登记；仅发现了程序文件或快捷方式"));}
        bool Idle(){if(!working)return true;MessageBox.Show(this,"请先等待当前操作完成，或取消扫描。","正在处理");return false;}
        void Busy(bool value,bool cancellable=false){working=value;quick.Enabled=disk.Enabled=folder.Enabled=!value;cancel.Enabled=value&&cancellable;progress.Visible=value;}
        async Task RunScan(Action<CancellationToken> action,string finished){if(!Idle())return;cts=new CancellationTokenSource();Busy(true,true);try{await Task.Run(()=>action(cts.Token));DrainAll();latestStatus=finished+" · "+apps.Count+" 条";Store.Log(latestStatus);}catch(OperationCanceledException){DrainAll();latestStatus="已停止，保留已发现的 "+apps.Count+" 条结果。";}catch(Exception e){ShowError(e);}finally{Busy(false);cts.Dispose();cts=null;}}
        async Task QuickScan(){if(!Idle())return;apps.Clear();ids.Clear();grid.Rows.Clear();
            await RunScan(token=>{Scanner.Registered(queue.Enqueue,s=>latestStatus=s,token);try{Scanner.Packages(queue.Enqueue,s=>latestStatus=s,token);}catch(OperationCanceledException){throw;}catch(Exception e){Store.Log("商店应用未能完整读取："+e.Message);}
                Scanner.Shortcuts(queue.Enqueue,s=>latestStatus=s,token);},"基础扫描完成；可用“全盘发现”补充绿色软件。详情见操作记录");}
        async Task ChooseFolder(){if(!Idle())return;using(var f=new FolderBrowserDialog{Description="选择软件存放目录，将递归发现 EXE 程序（只读扫描）",SelectedPath=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),ShowNewFolderButton=false})if(f.ShowDialog(this)==DialogResult.OK){string root=f.SelectedPath;await RunScan(t=>Scanner.Disk(root,queue.Enqueue,s=>latestStatus=s,t),"目录扫描完成");}}
        async Task ChooseDrives(){if(!Idle())return;using(var f=new Form{Text="选择磁盘扫描范围",ClientSize=new Size(540,350),StartPosition=FormStartPosition.CenterParent,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
            var list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true};foreach(var drive in DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed))list.Items.Add(drive.RootDirectory.FullName,true);
            var info=new Label{Dock=DockStyle.Top,Height=118,Padding=new Padding(15),Text="递归查找所选磁盘中的 EXE，补充未登记的软件。\r\n可能需要几分钟，可随时取消并保留结果。\r\n跳过系统保护区、无权限目录、链接及常见开发依赖目录；发现项需人工核对，不保证百分之百识别。"};
            var go=new Button{Text="开始只读扫描",Dock=DockStyle.Bottom,Height=44,DialogResult=DialogResult.OK};f.Controls.Add(list);f.Controls.Add(info);f.Controls.Add(go);if(f.ShowDialog(this)!=DialogResult.OK)return;string[] roots=list.CheckedItems.Cast<string>().ToArray();if(roots.Length==0)return;await RunScan(t=>{foreach(string root in roots)Scanner.Disk(root,queue.Enqueue,s=>latestStatus=s,t);},"磁盘扫描完成（跳过项和限制请见说明）");}}
        void OpenSelected(){var a=Selected();if(a!=null)OpenPath(a.Path.Length>0?a.Path:a.Executable);}
        void OpenPath(string p){try{if(File.Exists(p)||Directory.Exists(p)){Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),(File.Exists(p)?"/select,":"")+"\""+p.TrimEnd('\\')+"\""){UseShellExecute=true});}else MessageBox.Show(this,"路径不存在或未提供。","打开位置");}catch(Exception e){ShowError(e);}}
        void UseSelectedFolder(){var a=Selected();if(a==null)return;if(a.Path.Length==0){MessageBox.Show(this,"该条目没有提供安装目录，请手动选择目标。","清理匣");return;}pathBox.Text=a.Path;tabs.SelectedIndex=1;}
        async Task UninstallSelected(){if(!Idle())return;var a=Selected();if(a==null)return;if(a.Protected){MessageBox.Show(this,"此项是系统组件，本工具不执行卸载。","受保护项目");return;}
            if(!Confirm("正常卸载",a.Name+"\r\n\r\n"+(a.Package.Length>0?"仅卸载当前用户的软件包：\r\n"+a.Package:"即将启动软件登记的卸载程序：\r\n"+a.Command),false))return;
            Busy(true);try{await Task.Run(()=>Operations.Uninstall(a));MessageBox.Show(this,a.Package.Length>0?"软件包已卸载，重新扫描可更新清单。":"已启动原卸载程序，请在它的窗口完成操作。完成后可检查残留、重新扫描。","卸载");}catch(Exception e){ShowError(e);}finally{Busy(false);}}
        async Task ClearRecord(){if(!Idle())return;var a=Selected();if(a==null)return;if(!Confirm("清除失效记录",a.Name+"\r\n"+a.Hive+"\\"+a.RegistryPath+"\r\n\r\n仅清理不存在的软件留下的卸载列表项，先导出备份；不扫描删除其他注册表键。",true))return;
            Busy(true);try{await Task.Run(()=>Operations.RemoveStaleRecord(a));MessageBox.Show(this,"失效记录已删除，备份保存在 data 目录。重新扫描可刷新列表。","完成");}catch(Exception e){ShowError(e);}finally{Busy(false);}}
        void Export(){using(var f=new SaveFileDialog{Title="导出当前筛选结果",Filter="CSV 表格|*.csv",FileName="软件清单-"+DateTime.Now.ToString("yyyyMMdd")+".csv",InitialDirectory=Store.Home})if(f.ShowDialog(this)==DialogResult.OK)try{Store.Export(apps.Where(Matches),f.FileName);latestStatus="清单已导出："+f.FileName;}catch(Exception e){ShowError(e);}}
        void ResidualDialog(){if(!Idle())return;var a=Selected();if(a==null)return;var paths=Operations.Residuals(a);using(var f=new Form{Text="残留候选 · "+a.Name,ClientSize=new Size(760,440),StartPosition=FormStartPosition.CenterParent,Font=Font,MinimumSize=new Size(660,380)}){
            var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};list.Items.AddRange(paths.Cast<object>().ToArray());
            var note=new Label{Dock=DockStyle.Top,Height=103,Padding=new Padding(15),Text="下列是安装位置和名称完全匹配的配置目录，不代表一定是残留。\r\n请先正常卸载；配置目录可能包含账号、设置和个人数据。\r\n逐个选择后进入文件处理页，核对完整路径再删除。不会自动勾选或删除。"};
            var actions=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(10)};actions.Controls.Add(Button("打开选中位置",()=>{if(list.SelectedItem!=null)OpenPath(list.SelectedItem.ToString());}));actions.Controls.Add(Button("处理选中目录",()=>{if(list.SelectedItem!=null){pathBox.Text=list.SelectedItem.ToString();tabs.SelectedIndex=1;f.Close();}},true));f.Controls.Add(list);f.Controls.Add(note);f.Controls.Add(actions);if(paths.Count==0)note.Text="未找到安全范围内的明确候选目录。\r\n本工具不做模糊名称批量删除；可手动选择已确认的残留位置。";f.ShowDialog(this);}}
        async Task Inspect(){if(!Idle())return;string p=pathBox.Text;cts=new CancellationTokenSource();Busy(true,true);lockList.Items.Clear();lockPath="";lockSummary.Text="正在检查文件与进程…";try{var report=await Task.Run(()=>Native.FindLocks(p,cts.Token));lockPath=Safety.Normalize(p);foreach(var o in report.Owners)lockList.Items.Add(o);lockSummary.Text="检查 "+report.Files+" 个文件，找到 "+report.Owners.Count+" 个相关进程。"+(report.Partial?" 检查不完整（上限 10,000 个文件 / 权限限制）。":"")+"\r\n"+(report.Note.Length>0?report.Note:"未列出进程不代表没有占用；驱动、文件夹句柄或权限限制可能无法识别。");latestStatus="占用检查完成";}catch(OperationCanceledException){lockSummary.Text="已取消检查。";}catch(Exception e){ShowError(e);lockSummary.Text="检查失败，请查看操作记录。";}finally{Busy(false);cts.Dispose();cts=null;}}
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
                latestStatus=title+"处理结束，详情见操作记录";lockList.Items.Clear();lockSummary.Text="操作后请重新检查占用。";
            }catch(Exception e){ShowError(e);}finally{Busy(false);}}
        bool Confirm(string title,string body,bool typeWord){using(var f=new Form{Text=title,ClientSize=new Size(700,typeWord?370:320),StartPosition=FormStartPosition.CenterParent,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}){
            var text=new TextBox{Dock=DockStyle.Top,Height=230,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=body,BackColor=Color.White,BorderStyle=BorderStyle.None,Margin=new Padding(15)};f.Padding=new Padding(18);f.Controls.Add(text);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft};var yes=new Button{Text="确认继续",Width=120,Height=34,DialogResult=DialogResult.OK,Enabled=!typeWord};var no=new Button{Text="取消",Width=100,Height=34,DialogResult=DialogResult.Cancel};bottom.Controls.Add(no);bottom.Controls.Add(yes);f.Controls.Add(bottom);f.CancelButton=no;
            if(typeWord){var word=new TextBox{Location=new Point(244,260),Width=180};var label=new Label{Text="输入“确认”以继续：",Location=new Point(18,263),AutoSize=true};word.TextChanged+=(s,e)=>yes.Enabled=word.Text=="确认";f.Controls.Add(word);f.Controls.Add(label);}return f.ShowDialog(this)==DialogResult.OK;}}
        void Elevate(){if(!Idle())return;try{string target=string.IsNullOrWhiteSpace(pathBox.Text)?"":Safety.Normalize(pathBox.Text);Process.Start(new ProcessStartInfo(Application.ExecutablePath,target.Length>0?"--target \""+target+"\"":""){UseShellExecute=true,Verb="runas",WorkingDirectory=Store.Home});Close();}catch(Exception e){ShowError(e);}}
        void ShowError(Exception e){latestStatus=e.Message;Store.Log("操作未完成："+e.Message);MessageBox.Show(this,e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        void VerifyInterface(){
            var result=new List<string>();Action<bool,string> assert=(ok,name)=>{if(!ok)throw new Exception(name);result.Add("PASS: "+name);};
            assert(apps.Count>0,"Live software inventory populated");assert(grid.Rows.Count==apps.Count,"Grid and inventory remain consistent");assert(adminButton.Left>=0&&adminButton.Right<=adminButton.Parent.ClientSize.Width,"Administrator action stays in visible header");
            search.Text="__CleanBox_No_Matching_App_"+Guid.NewGuid().ToString("N");assert(grid.Rows.Cast<DataGridViewRow>().All(r=>!r.Visible),"Search handles empty result");search.Clear();
            filter.SelectedIndex=1;assert(grid.Rows.Cast<DataGridViewRow>().Where(r=>r.Visible).All(r=>((AppEntry)r.Tag).Source.StartsWith("已安装")),"Source filter shows only selected source");filter.SelectedIndex=0;
            search.Text=apps[0].Name;assert(grid.Rows.Cast<DataGridViewRow>().Any(r=>r.Visible),"Search finds known installed app");search.Clear();
            Directory.CreateDirectory(Path.Combine(Store.Home,"preview"));
            Action<string> capture=name=>{Refresh();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(Store.Home,"preview",name+".png"),System.Drawing.Imaging.ImageFormat.Png);}};
            tabs.SelectedIndex=0;grid.ClearSelection();if(grid.Rows.Count>0){grid.Rows[0].Selected=true;grid.CurrentCell=grid.Rows[0].Cells[0];}capture("software-list");
            tabs.SelectedIndex=1;pathBox.Text=@"D:\请选择要处理的具体文件或文件夹";capture("file-tools");
            assert(lockList.Items.Count==0&&lockPath=="","Changing target clears stale lock owners");
            ClientSize=new Size(1024,720);PerformLayout();capture("file-tools-compact");
            assert(pathBox.Right<=pathBox.Parent.ClientSize.Width,"Path control stays within parent");
            tabs.SelectedIndex=0;capture("software-list-compact");
            result.Add("Read-only UI checks: no uninstall, delete, process termination or reboot scheduling.");File.WriteAllLines(Path.Combine(Store.Data,"ui-results.txt"),result,System.Text.Encoding.UTF8);
        }
    }
}
