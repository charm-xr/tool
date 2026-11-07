using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class MainForm {
        System.Windows.Forms.Timer searchDelay;
        string appliedSearch = "";
        bool changingCatalog;
        int filterApplications;
        Button measureButton;

        void ScheduleSearch() {
            if (updatingFilters) return;
            if (searchDelay == null) {
                searchDelay = new System.Windows.Forms.Timer { Interval = 220 };
                searchDelay.Tick += (s, e) => ApplyFilter();
            }
            searchDelay.Stop(); searchDelay.Start();
        }

        void ApplyFilter() {
            if (grid == null || updatingFilters) return;
            if (searchDelay != null) searchDelay.Stop();
            appliedSearch = search.Text.Trim();
            filterApplications++;
            var selected = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0] : null;
            int topIndex = grid.FirstDisplayedScrollingRowIndex;
            var top = topIndex >= 0 ? grid.Rows[topIndex] : null;
            DataGridViewRow first = null;
            changingCatalog = true;
            var binding = ScrollSkin.Find(grid);
            if (binding != null) binding.BeginBatch();
            bool redraw = grid.IsHandleCreated && grid.Visible;
            if (redraw) ScrollNative.SendMessage(grid.Handle, 0xB, IntPtr.Zero, IntPtr.Zero);
            grid.SuspendLayout();
            var sizing = grid.AutoSizeColumnsMode;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            try {
                foreach (DataGridViewRow row in grid.Rows) {
                    bool visible = Matches(row.Tag as AppEntry);
                    if (row.Visible != visible) {
                        if (!visible && grid.CurrentCell != null && grid.CurrentCell.OwningRow == row) grid.CurrentCell = null;
                        row.Visible = visible;
                    }
                    if (visible && first == null) first = row;
                }
                var chosen = selected != null && selected.Visible ? selected : first;
                if (chosen != null) {
                    if (!chosen.Selected || grid.CurrentCell == null) {
                        grid.ClearSelection(); chosen.Selected = true; grid.CurrentCell = chosen.Cells[0];
                    }
                } else { grid.CurrentCell = null; grid.ClearSelection(); }
                if (top != null && top.Visible && top.Index >= 0) grid.FirstDisplayedScrollingRowIndex = top.Index;
            } finally {
                grid.AutoSizeColumnsMode = sizing;
                grid.ResumeLayout(); changingCatalog = false;
                if (redraw) { ScrollNative.SendMessage(grid.Handle, 0xB, new IntPtr(1), IntPtr.Zero); grid.Invalidate(); }
                if (binding != null) binding.EndBatch();
            }
            ShowSelection(); UpdateCounts(); UpdateFilterCaption();
        }

        void SortBySize() {
            ApplyFilter();
            grid.Sort(sizeColumn, ListSortDirection.Descending);
            var first = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => row.Visible);
            if (first != null) {
                grid.ClearSelection(); first.Selected = true; grid.CurrentCell = first.Cells[0];
                grid.FirstDisplayedScrollingRowIndex = first.Index;
            }
        }

        void UpdateSizeCell(AppEntry app) {
            var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag == app);
            if (row == null) return;
            row.Cells[sizeColumn.Index].Value = app.SizeBytes;
            row.Cells[sizeColumn.Index].ToolTipText = app.SizeBadge + "\r\n" + app.SizeExplanation;
            grid.InvalidateRow(row.Index);
        }

        async Task RemeasureSelected() {
            if (!Idle()) return;
            var app = Selected();
            if (app == null || app.Protected) return;
            cts = new CancellationTokenSource(); Busy(true, true);
            latestStatus = "正在计算「" + app.Name + "」的大小，可取消…";
            try {
                var token = cts.Token;
                string directory = null, reason = "";
                SizeMeasurement result = await Task.Run(() => {
                    if (IsCandidate(app)) {
                        token.ThrowIfCancellationRequested();
                        return new SizeMeasurement { Bytes = FileSizes.ReadLength(app.Executable), Note = "候选项仅计算程序文件；未确认独立安装目录。" };
                    }
                    directory = InstalledSizeLookup.ResolveDirectory(app, out reason);
                    return directory == null ? null : InstalledSizeLookup.Measure(directory, token, 250000, 30000, s => latestStatus = app.Name + " · " + s);
                });
                token.ThrowIfCancellationRequested();
                if (result == null || !result.Bytes.HasValue) {
                    string failure = result == null ? reason : result.Note;
                    app.SizeNote = (app.SizeBytes.HasValue ? "本次计算未完成，保留原大小。\r\n" : "") + failure;
                    latestStatus = "未能取得新的大小，原因见右侧详情。";
                } else {
                    if (directory != null) InstalledSizeLookup.Apply(app, directory, result);
                    else { app.SizeBytes = result.Bytes; app.SizeKind = AppSizeKind.Executable; app.SizePartial = false; app.SizeNote = result.Note; }
                    app.SizeStale = false;
                    app.SizeMeasuredUtc = DateTime.UtcNow;
                    latestStatus = "大小已更新：" + app.Name + (app.SizePartial ? "（部分统计）" : "");
                    try { catalogState.SaveMeasurement(app); }
                    catch (Exception ex) { MessageBox.Show(this, "本次大小已更新，但历史结果未保存：" + ex.Message, "保存未完成"); }
                }
                UpdateSizeCell(app);
                if (grid.SortedColumn != null) grid.Sort(grid.SortedColumn, grid.SortOrder == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);
                ApplyFilter(); Store.Log(latestStatus);
            } catch (OperationCanceledException) { latestStatus = "已取消大小计算，保留原结果。"; }
            catch (Exception ex) { ShowError(ex); }
            finally { Busy(false); cts.Dispose(); cts = null; }
        }

        void PaintSizeCell(DataGridViewCellPaintingEventArgs e) {
            var app = grid.Rows[e.RowIndex].Tag as AppEntry;
            if (app == null) return;
            if (!Palette.Glass) e.PaintBackground(e.ClipBounds, true);
            var bounds = e.CellBounds;
            var flags = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            int left = bounds.Left + Px(8), width = bounds.Width - Px(14), top = bounds.Top + Math.Max(Px(4), (bounds.Height - Px(36)) / 2);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(e.FormattedValue), Font, new Rectangle(left, top, width, Px(20)), Ink, flags);
            using (var small = new Font(Font.FontFamily, 7.5f))
                TextRenderer.DrawText(e.Graphics, app.SizeBadge, small, new Rectangle(left, top + Px(21), width, Px(15)), Muted, flags);
            e.Handled = true;
        }
    }
}
