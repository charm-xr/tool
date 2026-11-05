using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace CleanBox {
    public partial class MainForm {
        static Button Button(string text,Action click,bool primary=false){var b=new UiButton{Text=text,Kind=primary?"primary":"secondary",AutoSize=true,Height=36,MinimumSize=new Size(90,36),Margin=new Padding(0,0,8,7)};b.Click+=(s,e)=>click();return b;}
        static FlowLayoutPanel Bar(int height=46){return new CanvasFlowPanel{Dock=DockStyle.Top,Height=height,Padding=new Padding(0,4,0,0),WrapContents=true};}
        Label TextLabel(string text,int height,string role=null){return new Label{Text=text,Dock=DockStyle.Top,Height=height,AutoEllipsis=true,Tag=role,TextAlign=ContentAlignment.MiddleLeft};}
        Panel Page(){return new CanvasPanel{Dock=DockStyle.Fill,Padding=new Padding(24,20,24,16),Visible=false};}
        Panel Input(TextBox field,Padding margin){var frame=new InputFrame{Dock=DockStyle.Fill,Margin=margin};field.Dock=DockStyle.None;field.BorderStyle=BorderStyle.None;field.Margin=Padding.Empty;field.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;frame.Controls.Add(field);Action arrange=()=>{field.Left=frame.Padding.Left;field.Width=Math.Max(1,frame.ClientSize.Width-frame.Padding.Horizontal);field.Top=Math.Max(0,(frame.ClientSize.Height-field.Height)/2);};frame.Resize+=(s,e)=>arrange();field.Enter+=(s,e)=>frame.Invalidate();field.Leave+=(s,e)=>frame.Invalidate();arrange();return frame;}
        Panel Heading(string title,string subtitle){var p=new CanvasPanel{Dock=DockStyle.Top,Height=78};var words=new CanvasPanel{Dock=DockStyle.Fill};words.Controls.Add(new Label{Text=subtitle,Dock=DockStyle.Bottom,Height=30,Tag="muted",AutoEllipsis=true});words.Controls.Add(new Label{Text=title,Dock=DockStyle.Top,Height=40,Font=new Font(Font.FontFamily,21,FontStyle.Bold)});var actions=new CanvasPanel{Dock=DockStyle.Right,Width=146,Name="headingActions",Padding=new Padding(8,3,0,0)};p.Controls.Add(words);p.Controls.Add(actions);return p;}
        void BuildShell(){
            var sidebarFrame=new CanvasPanel{Dock=DockStyle.Left,Width=204,Padding=new Padding(12,16,4,16)};
            sidebar=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(13,24,13,16),Tag="sidebar"};sidebarFrame.Controls.Add(sidebar);
            var brand=new Label{Text="清理匣",Dock=DockStyle.Top,Height=43,Font=new Font(Font.FontFamily,22,FontStyle.Bold),Tag="accent",TextAlign=ContentAlignment.MiddleLeft};
            var brandSub=TextLabel("CleanBox  2.1.2",34,"muted");var nav=new CanvasFlowPanel{Dock=DockStyle.Top,Height=160,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            navigation=new UiButton[3];string[] names={"软件管理","文件解锁","操作记录"},glyphs={"\uE8A5","\uE785","\uE81C"};
            for(int i=0;i<3;i++){int target=i;var b=new UiButton{Text=names[i],Glyph=glyphs[i],Kind="nav",AutoSize=false,Width=162,Height=45,Margin=new Padding(0,0,0,6)};b.Click+=(s,e)=>SwitchPage(target);navigation[i]=b;nav.Controls.Add(b);}
            nav.Resize+=(s,e)=>{foreach(var b in navigation)b.Width=nav.ClientSize.Width;};
            var options=new CanvasPanel{Dock=DockStyle.Bottom,Height=252,Tag="sidebar"};
            themeButton=Button("深色外观",ToggleTheme);themeButton.Dock=DockStyle.Top;themeButton.AutoSize=false;themeButton.Height=40;
            densityButton=Button("紧凑列表",()=>{compact=!compact;UpdateDensity();SavePreferences();});densityButton.Dock=DockStyle.Top;densityButton.AutoSize=false;densityButton.Height=40;
            glassButton=Button("玻璃质感：开启",ToggleGlass);glassButton.Dock=DockStyle.Top;glassButton.AutoSize=false;glassButton.Height=40;
            motionButton=Button("弹性动效：开启",ToggleMotion);motionButton.Dock=DockStyle.Top;motionButton.AutoSize=false;motionButton.Height=40;
            adminButton=Button(Native.IsAdmin?"已是管理员":"管理员模式",Elevate);adminButton.Dock=DockStyle.Top;adminButton.AutoSize=false;adminButton.Height=38;adminButton.Enabled=!Native.IsAdmin;
            var permissions=TextLabel(Native.IsAdmin?"管理员权限":"普通权限 · 按需提权",37,"muted");permissions.Dock=DockStyle.Bottom;
            options.Controls.Add(permissions);options.Controls.Add(adminButton);options.Controls.Add(motionButton);options.Controls.Add(glassButton);options.Controls.Add(densityButton);options.Controls.Add(themeButton);
            sidebar.Controls.Add(nav);sidebar.Controls.Add(brandSub);sidebar.Controls.Add(brand);sidebar.Controls.Add(options);
            var foot=new CanvasPanel{Dock=DockStyle.Bottom,Height=38,Padding=new Padding(18,8,18,7),Tag="surface"};
            progress=new ProgressBar{Dock=DockStyle.Right,Width=100,Style=ProgressBarStyle.Marquee,Visible=false};status=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,Text="准备就绪",Tag="muted"};
            cancel=Button("取消任务",()=>{if(cts!=null)cts.Cancel();});cancel.AutoSize=false;cancel.Dock=DockStyle.Right;cancel.Width=96;cancel.MinimumSize=Size.Empty;cancel.Height=24;cancel.Visible=false;
            foot.Controls.Add(status);foot.Controls.Add(cancel);foot.Controls.Add(progress);
            host=new CanvasPanel{Dock=DockStyle.Fill};pages=new[]{BuildCatalog(),BuildFiles(),BuildLog()};foreach(var p in pages)host.Controls.Add(p);
            Controls.Add(host);Controls.Add(sidebarFrame);Controls.Add(foot);
        }
        Panel BuildCatalog(){
            var p=Page();var heading=Heading("软件管理","已安装软件与磁盘发现，集中查看");
            quick=Button("扫描软件  ▾",()=>scanMenu.Show(quick,new Point(0,quick.Height)),true);quick.AutoSize=false;quick.Dock=DockStyle.Top;quick.Height=38;heading.Controls["headingActions"].Controls.Add(quick);
            disk=Button("全盘发现",async()=>await ChooseDrives());folder=Button("扫描文件夹",async()=>await ChooseFolder());
            scanMenu=new ContextMenuStrip();scanMenu.Items.Add("基础扫描   F5",null,async(s,e)=>await QuickScan());scanMenu.Items.Add("全盘发现…",null,async(s,e)=>await ChooseDrives());scanMenu.Items.Add("扫描指定文件夹…",null,async(s,e)=>await ChooseFolder());scanMenu.Items.Add(new ToolStripSeparator());scanMenu.Items.Add("导出当前清单…",null,(s,e)=>Export());
            catalogBody=new CanvasPanel{Dock=DockStyle.Fill};catalogBody.Resize+=(s,e)=>ArrangeCatalog();
            var listing=new CanvasPanel{Dock=DockStyle.Fill,Padding=new Padding(0,0,16,0)};
            var filters=new CanvasTablePanel{Dock=DockStyle.Top,Height=43,ColumnCount=2,RowCount=1};filters.RowStyles.Add(new RowStyle(SizeType.Percent,100));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,153));
            search=new TextBox{Dock=DockStyle.Fill,Margin=new Padding(0,5,12,4),AccessibleName="搜索名称、发布者或路径"};search.TextChanged+=(s,e)=>ApplyFilter();
            filter=new ChoiceButton{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,0)};filter.Items.AddRange(new object[]{"全部来源","已安装","应用商店","绿色候选","快捷方式","磁盘发现"});filter.SelectedIndex=0;filter.SelectedIndexChanged+=(s,e)=>ApplyFilter();filters.Controls.Add(Input(search,new Padding(0,3,12,3)),0,0);filters.Controls.Add(filter,1,0);
            var searchHint=TextLabel("搜索名称、发布者或路径  ·  Ctrl + F",25,"muted");
            var counts=new CanvasFlowPanel{Dock=DockStyle.Top,Height=35,WrapContents=false};totalLabel=new Label{AutoSize=true,Margin=new Padding(0,4,15,0),Tag="muted"};installedLabel=new Label{AutoSize=true,Margin=new Padding(0,4,15,0),Tag="muted"};foundLabel=new Label{AutoSize=true,Margin=new Padding(0,4,0,0),Tag="accent"};counts.Controls.AddRange(new Control[]{totalLabel,installedLabel,foundLabel});
            grid=new SmoothGrid{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EnableHeadersVisualStyles=false,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal};
            AddColumn("软件 / 发布者",62,180);AddColumn("版本",18,70);AddColumn("来源",20,95);grid.SelectionChanged+=(s,e)=>ShowSelection();grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)OpenSelected();};grid.CellPainting+=PaintAppCell;grid.Sorted+=(s,e)=>ShowSelection();
            var gridHost=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(10,13,10,13)};emptyList=new Label{Text="正在读取软件清单…",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Tag="muted",Visible=false};gridHost.Controls.Add(grid);gridHost.Controls.Add(emptyList);
            listCount=TextLabel("准备扫描",27,"muted");listCount.Dock=DockStyle.Bottom;
            listing.Controls.Add(gridHost);listing.Controls.Add(listCount);listing.Controls.Add(counts);listing.Controls.Add(filters);listing.Controls.Add(searchHint);
            detailPanel=BuildDetail();detailPanel.Dock=DockStyle.Right;detailPanel.Width=292;catalogBody.Controls.Add(listing);catalogBody.Controls.Add(detailPanel);
            p.Controls.Add(catalogBody);p.Controls.Add(heading);return p;
        }
        void AddColumn(string title,float weight,int min){int i=grid.Columns.Add("col"+grid.Columns.Count,title);grid.Columns[i].FillWeight=weight;grid.Columns[i].MinimumWidth=min;}
        Panel BuildDetail(){
            var p=new SurfacePanel();var title=new CanvasPanel{Dock=DockStyle.Top,Height=90,Tag="surface"};detailIcon=new PictureBox{Location=new Point(0,2),Size=new Size(40,40),SizeMode=PictureBoxSizeMode.Zoom};
            detailTitle=new Label{Text="选择软件",Location=new Point(52,0),Size=new Size(207,47),Font=new Font(Font.FontFamily,12,FontStyle.Bold),AutoEllipsis=true,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};detailSubtitle=new Label{Text="查看详细信息与操作",Dock=DockStyle.Bottom,Height=32,Tag="muted",AutoEllipsis=true};title.Controls.Add(detailIcon);title.Controls.Add(detailTitle);title.Controls.Add(detailSubtitle);title.Resize+=(s,e)=>detailTitle.Width=Math.Max(10,title.Width-detailTitle.Left);
            detail=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,Text="选择软件后，可查看位置、版本、发现来源和卸载入口。",AccessibleName="软件详细信息"};
            var actions=new CanvasTablePanel{Dock=DockStyle.Bottom,Height=189,RowCount=4,ColumnCount=1,Padding=new Padding(0,12,0,0),Tag="surface"};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<4;i++)actions.RowStyles.Add(new RowStyle(SizeType.Percent,25));
            uninstallButton=Button("正常卸载",async()=>{if(IsCandidate(Selected()))UseSelectedFolder();else await UninstallSelected();},true);openButton=Button("打开位置",OpenSelected);residualButton=Button("检查残留",ResidualDialog);var more=Button("更多操作  ▾",()=>appMenu.Show(selectionActions.Last(),new Point(0,selectionActions.Last().Height)));
            appMenu=new ContextMenuStrip();appMenu.Items.Add("处理安装目录…",null,(s,e)=>UseSelectedFolder());appMenu.Items.Add("清除失效记录…",null,async(s,e)=>await ClearRecord());
            foreach(var b in new[]{uninstallButton,openButton,residualButton,more}){b.Dock=DockStyle.Fill;b.AutoSize=false;b.MinimumSize=Size.Empty;b.Margin=new Padding(0,0,0,6);selectionActions.Add(b);actions.Controls.Add(b);}
            p.Controls.Add(detail);p.Controls.Add(actions);p.Controls.Add(title);return p;
        }
        Panel BuildFiles(){
            var p=Page();var heading=Heading("文件解锁","找到占用它的程序，再处理文件");heading.Height=68;var stepBar=new CanvasTablePanel{Dock=DockStyle.Top,Height=38,ColumnCount=3};steps=new Label[3];string[] captions={"1   选择文件","2   检查并解除占用","3   确认删除"};for(int i=0;i<3;i++){stepBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));steps[i]=new Label{Text=captions[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Tag="muted"};stepBar.Controls.Add(steps[i],i,0);}
            dropZone=new SurfacePanel{Dock=DockStyle.Top,Height=105,Padding=new Padding(12),AllowDrop=true};dropText=TextLabel("拖入删不掉的文件或文件夹",34,"accent");dropText.Font=new Font(Font.FontFamily,11,FontStyle.Bold);dropText.AllowDrop=true;
            var pathRow=new CanvasTablePanel{Dock=DockStyle.Top,Height=41,ColumnCount=3,RowCount=1};pathRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,106));pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,118));
            pathBox=new TextBox{Dock=DockStyle.Fill,Margin=new Padding(0,8,12,0),AllowDrop=true,AccessibleName="需要处理的完整文件或文件夹路径"};pathBox.TextChanged+=(s,e)=>{if(lockList==null)return;lockList.Items.Clear();lockPath="";lockSummary.Text="目标已改变，请先检查占用。";SetStep(0);UpdateFileActions();};
            pathRow.Controls.Add(Input(pathBox,new Padding(0,2,10,4)),0,0);pathRow.Controls.Add(Button("选择文件",()=>ChooseTarget(false)),1,0);pathRow.Controls.Add(Button("选择文件夹",()=>ChooseTarget(true)),2,0);
            dropZone.Controls.Add(pathRow);dropZone.Controls.Add(dropText);foreach(var target in new Control[]{p,dropZone,dropText,pathBox,pathBox.Parent}){target.AllowDrop=true;target.DragEnter+=TargetDragEnter;target.DragDrop+=TargetDrop;}
            var inspectBar=Bar(48);inspectButton=Button("检查占用",async()=>await Inspect(),true);inspectBar.Controls.Add(inspectButton);inspectBar.Controls.Add(Button("打开目标位置",()=>OpenPath(pathBox.Text)));
            var result=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(16,10,16,12)};lockSummary=TextLabel("尚未检查。选择目标后，点击“检查占用”。",54,"muted");
            lockList=new ListBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,HorizontalScrollbar=true,IntegralHeight=false,DisplayMember="Display",DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=54};lockList.DrawItem+=PaintLockOwner;lockList.SelectedIndexChanged+=(s,e)=>UpdateFileActions();
            var ownerBar=Bar(46);ownerBar.Dock=DockStyle.Bottom;stopButton=Button("正常退出选中程序",async()=>await StopOwner(false));ownerBar.Controls.Add(stopButton);
            result.Controls.Add(lockList);result.Controls.Add(ownerBar);result.Controls.Add(lockSummary);
            var bottom=new CanvasPanel{Dock=DockStyle.Bottom,Height=93,Padding=new Padding(0,8,0,0)};var deleteBar=Bar();recycleButton=Button("移到回收站",async()=>await DeleteTarget(0),true);fileMore=Button("高级处理  ▾",()=>{UpdateFileActions();fileMenu.Show(fileMore,new Point(0,fileMore.Height));});deleteBar.Controls.AddRange(new Control[]{recycleButton,fileMore});
            fileMenu=new ContextMenuStrip();fileMenu.Items.Add("强制结束选中程序…",null,async(s,e)=>await StopOwner(true));fileMenu.Items.Add(new ToolStripSeparator());fileMenu.Items.Add("永久删除…",null,async(s,e)=>await DeleteTarget(1));fileMenu.Items.Add("重启后删除…",null,async(s,e)=>await DeleteTarget(2));
            stepLabel=TextLabel("优先正常退出程序，再移到回收站。检测可能受权限、驱动和文件夹句柄限制。",43,"muted");stepLabel.Dock=DockStyle.Fill;bottom.Controls.Add(stepLabel);bottom.Controls.Add(deleteBar);
            p.Controls.Add(result);p.Controls.Add(bottom);p.Controls.Add(inspectBar);p.Controls.Add(dropZone);p.Controls.Add(stepBar);p.Controls.Add(heading);UpdateFileActions();return p;
        }
        Panel BuildLog(){var p=Page();var heading=Heading("操作记录","查看扫描进展、处理结果和失败原因");var bar=Bar();bar.Controls.Add(Button("打开日志目录",()=>OpenPath(Store.Data)));bar.Controls.Add(Button("使用说明",()=>{try{Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"notepad.exe"),"\""+Path.Combine(Store.Home,"使用说明.txt")+"\""){UseShellExecute=true});}catch(Exception e){ShowError(e);}}));
            logBox=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",10)};
            try{string file=Path.Combine(Store.Data,"operations.log");if(File.Exists(file)){string s=File.ReadAllText(file);logBox.Text=s.Length>100000?s.Substring(s.Length-100000):s;}}catch{}
            p.Controls.Add(logBox);p.Controls.Add(bar);p.Controls.Add(heading);return p;}
        void ChooseTarget(bool directory){if(working)return;if(directory){using(var f=new FolderBrowserDialog{Description="选择具体的软件或文件目录",ShowNewFolderButton=false})if(f.ShowDialog(this)==DialogResult.OK)pathBox.Text=f.SelectedPath;}else using(var f=new OpenFileDialog{Title="选择删不掉的文件"})if(f.ShowDialog(this)==DialogResult.OK)pathBox.Text=f.FileName;}
        void TargetDragEnter(object sender,DragEventArgs e){if(!working&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;}
        void TargetDrop(object sender,DragEventArgs e){if(working)return;var paths=e.Data.GetData(DataFormats.FileDrop) as string[];if(paths!=null&&paths.Length>0){pathBox.Text=paths[0];SwitchPage(1);if(paths.Length>1)latestStatus="已选择第一个目标；请逐个核对并处理。";}}
    }
}
