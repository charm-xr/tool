using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class MainForm {
        UiButton usageNavigation;
        SmoothGrid usageGrid;
        TextBox usageTarget;
        ChoiceButton usageDrive;
        Label usageSummary, usageLocation, usageDetail;
        Button usageStart, usageFolder, usageUp, usageTop, usageOpen, usageTransfer, usagePrevious, usageNext;
        UsageResult usageResult;
        UsageNode usageCurrent;
        List<UsageNode> usageRows = new List<UsageNode>();
        bool usageLargest;
        int usagePage;
        const int UsagePageSize = 400;

        Panel BuildDiskUsage() {
            var page = Page();
            var heading = Heading("磁盘占用分析", "按文件长度统计，逐层查看目录与大文件");
            usageStart = Button("开始分析", async () => await AnalyzeUsage(), true);
            usageStart.Dock = DockStyle.Top; usageStart.AutoSize = false; usageStart.Height = 38;
            heading.Controls["headingActions"].Controls.Add(usageStart);
            var targetRow = new CanvasTablePanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 3, RowCount = 1 };
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            targetRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            usageDrive = new ChoiceButton { Dock = DockStyle.Fill, AccessibleName = "选择分析磁盘" };
            usageDrive.Items.Add("选择磁盘");
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed)) usageDrive.Items.Add(drive.RootDirectory.FullName);
            usageDrive.SelectedIndex = 0;
            usageTarget = new TextBox { AccessibleName = "要分析的完整目录路径" };
            usageDrive.SelectedIndexChanged += (s, e) => { if (usageDrive.SelectedIndex > 0) usageTarget.Text = usageDrive.Items[usageDrive.SelectedIndex].ToString(); };
            usageFolder = Button("选择文件夹", () => {
                using (var dialog = new FolderBrowserDialog { Description = "选择要分析的本地文件夹", ShowNewFolderButton = false })
                    if (dialog.ShowDialog(this) == DialogResult.OK) usageTarget.Text = dialog.SelectedPath;
            });
            usageFolder.Dock = DockStyle.Fill; usageFolder.AutoSize = false; usageFolder.MinimumSize = Size.Empty;
            targetRow.Controls.Add(usageDrive, 0, 0); targetRow.Controls.Add(Input(usageTarget, new Padding(10,3,10,3)), 1, 0); targetRow.Controls.Add(usageFolder, 2, 0);
            usageSummary = TextLabel("选择磁盘或文件夹后开始分析。可取消并保留已扫描的结果。", 52, "muted");
            usageLocation = TextLabel("尚未分析", 28, "accent");
            var navigationBar = Bar(46);
            usageUp = Button("上一级", () => { if (usageCurrent != null && usageCurrent.Parent != null) ShowUsageDirectory(usageCurrent.Parent); });
            usageTop = Button("大文件 Top 200", () => { if (usageResult == null) return; usageLargest = true; usagePage = 0; RefreshUsageRows(); });
            var rootButton = Button("返回根目录", () => { if (usageResult != null) ShowUsageDirectory(usageResult.Root); });
            var analyzeCurrent = Button("分析此目录", async () => { if (usageCurrent != null && !working) { usageTarget.Text = usageCurrent.Path; await AnalyzeUsage(); } });
            navigationBar.Controls.AddRange(new Control[] { usageUp, rootButton, usageTop, analyzeCurrent });
            usageGrid = new SmoothGrid { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
            usageGrid.Columns.Add("name", "名称 / 双击进入目录"); usageGrid.Columns.Add("size", "已统计大小 ↓"); usageGrid.Columns.Add("kind", "类型 / 状态");
            usageGrid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (DataGridViewColumn column in usageGrid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            usageGrid.CellDoubleClick += (s, e) => { var item = SelectedUsage(); if (item != null && item.Directory && !item.Link) ShowUsageDirectory(item); };
            usageGrid.SelectionChanged += (s, e) => UpdateUsageActions();
            var surface = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(10) }; surface.Controls.Add(usageGrid);
            usageDetail = new Label { Dock = DockStyle.Bottom, Height = 64, AutoEllipsis = true, Tag = "muted", Text = "选择条目查看完整路径与统计范围。" };
            var actions = Bar(48); actions.Dock = DockStyle.Bottom;
            usageOpen = Button("打开位置", () => { var item = SelectedUsage(); if (item != null) OpenPath(item.Path); });
            usageTransfer = Button("转到文件解锁", TransferUsage, true);
            usagePrevious = Button("上一页", () => { if (usagePage > 0) { usagePage--; RefreshUsageRows(); } });
            usageNext = Button("下一页", () => { usagePage++; RefreshUsageRows(); });
            actions.Controls.AddRange(new Control[] { usageOpen, usageTransfer, usagePrevious, usageNext });
            page.Controls.Add(surface); page.Controls.Add(usageDetail); page.Controls.Add(actions);
            page.Controls.Add(usageLocation); page.Controls.Add(navigationBar); page.Controls.Add(usageSummary); page.Controls.Add(targetRow); page.Controls.Add(heading);
            UpdateUsageActions(); return page;
        }

        void StyleUsageGrid() {
            if (usageGrid == null) return;
            usageGrid.BackgroundColor = Palette.Surface;
            usageGrid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Palette.Surface, ForeColor = Ink, SelectionBackColor = Palette.Soft, SelectionForeColor = Ink, Padding = new Padding(Px(10),0,Px(6),0) };
            usageGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Palette.Side, ForeColor = Muted, SelectionBackColor = Palette.Side, SelectionForeColor = Muted, Padding = new Padding(Px(8),0,0,0) };
            usageGrid.Columns[1].Width = Px(160); usageGrid.Columns[2].Width = Px(140);
            usageGrid.ColumnHeadersHeight = Px(35); usageGrid.RowTemplate.Height = Px(48);
            usageGrid.RefreshMaterial();
        }
        UsageNode SelectedUsage() { return usageGrid == null || usageGrid.SelectedRows.Count == 0 ? null : usageGrid.SelectedRows[0].Tag as UsageNode; }
        void UpdateUsageActions() {
            if (usageStart == null || usageOpen == null) return;
            usageStart.Enabled = usageFolder.Enabled = usageDrive.Enabled = !working;
            usageTarget.ReadOnly = working;
            var item = SelectedUsage();
            usageOpen.Enabled = item != null && !working;
            usageTransfer.Enabled = item != null && !item.Link && !working;
            usageTop.Enabled = usageResult != null && !working;
            usageUp.Enabled = usageCurrent != null && usageCurrent.Parent != null && !usageLargest;
            usagePrevious.Enabled = usagePage > 0; usageNext.Enabled = (usagePage + 1) * UsagePageSize < usageRows.Count;
            if (item != null) {
                usageDetail.Text = item.Path + "\r\n" + item.SizeText + (item.Partial ? " · 统计不完整，可能存在无权限文件或链接。" : " · 文件长度合计；实际可释放空间可能不同。");
                tips.SetToolTip(usageDetail, usageDetail.Text);
            }
        }
        void TransferUsage() {
            var item = SelectedUsage();
            if (working || item == null || item.Link) return;
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) { latestStatus = "目标已不存在，请重新分析。"; return; }
            pathBox.Text = item.Path; SwitchPage(1);
        }
        void ShowUsageDirectory(UsageNode directory) { usageCurrent = directory; usageLargest = false; usagePage = 0; RefreshUsageRows(); }
        void RefreshUsageRows() {
            if (usageResult == null) return;
            usageRows = usageLargest ? usageResult.Largest : usageCurrent.Children.OrderByDescending(n => n.Bytes).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            int pages = Math.Max(1, (usageRows.Count + UsagePageSize - 1) / UsagePageSize);
            usagePage = Math.Min(usagePage, pages - 1);
            usageGrid.Rows.Clear();
            foreach (var item in usageRows.Skip(usagePage * UsagePageSize).Take(UsagePageSize)) {
                int row = usageGrid.Rows.Add(item.Name, item.SizeText, item.Link ? "链接 · 已跳过" : (item.Directory ? "文件夹" : "文件") + (item.Partial ? " · 部分" : ""));
                usageGrid.Rows[row].Tag = item; usageGrid.Rows[row].Cells[0].ToolTipText = item.Path;
            }
            usageGrid.ClearSelection(); usageGrid.CurrentCell = null;
            usageLocation.Text = (usageLargest ? "已扫描范围内最大的 200 个文件" : usageCurrent.Path) + " · " + usageRows.Count + " 项 · 第 " + (usagePage + 1) + "/" + pages + " 页";
            tips.SetToolTip(usageLocation, usageLocation.Text);
            usageDetail.Text = usageRows.Count == 0 ? "此目录没有可显示的条目。统计不完整时可查看上方说明。" : "目录按已统计大小降序排列，双击文件夹进入。选择条目可转到文件解锁。";
            UpdateUsageActions();
        }
        async Task AnalyzeUsage() {
            if (!Idle()) return;
            string target = usageTarget.Text;
            cts = new System.Threading.CancellationTokenSource(); Busy(true, true);
            usageSummary.Text = "正在分析，可随时取消…"; latestStatus = "正在分析磁盘占用…";
            try {
                var result = await Task.Run(() => DiskUsage.Scan(target, cts.Token, s => latestStatus = s));
                usageResult = result; ShowUsageDirectory(result.Root);
                usageSummary.Text = result.Root.SizeText + " · " + result.Files + " 个文件 · 跳过或未能读取 " + result.Skipped + " 项\r\n"
                    + (result.Canceled ? "已取消，显示已扫描的部分。" : result.Limited ? "达到 30 万项上限，显示部分结果。可选择具体子目录继续分析。" : result.Root.Partial ? "部分内容无法统计，请结合目录状态查看。" : "分析完成。目录大小为文件长度合计。") ;
                latestStatus = result.Canceled ? "磁盘分析已取消，保留部分结果" : "磁盘占用分析完成";
            } catch (Exception ex) { usageSummary.Text = "本次分析未完成。"; ShowError(ex); }
            finally { Busy(false); cts.Dispose(); cts = null; UpdateUsageActions(); }
        }
    }
}
