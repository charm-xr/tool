using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CleanBox {
    internal enum RegistrationState { Present, Missing, Unknown, Unregistered }

    internal sealed class ResidualCandidate {
        internal string Path, Evidence;
        internal SizeMeasurement Size;
        internal string DisplaySize {
            get { return Size == null || !Size.Bytes.HasValue ? "未能读取" : (Size.Partial ? "至少 " : "") + FileSizes.Format(Size.Bytes.Value); }
        }
    }

    internal static class ResidualReview {
        internal static RegistrationState Probe(AppEntry app, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            try {
                if (app.Package.Length > 0) {
                    string count = Store.RunPowerShell("@(Get-AppxPackage | Where-Object { $_.PackageFullName -eq " + Store.QuotePS(app.Package) + " }).Count", token, 30000).Trim();
                    return count == "0" ? RegistrationState.Missing : count == "1" ? RegistrationState.Present : RegistrationState.Unknown;
                }
                if (app.RegistryPath.Length == 0) return RegistrationState.Unregistered;
                if (!app.RegistryPath.StartsWith(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase)) return RegistrationState.Unknown;
                using (var hive = RegistryKey.OpenBaseKey(app.Hive, app.View))
                using (var key = hive.OpenSubKey(app.RegistryPath))
                    return key == null ? RegistrationState.Missing : RegistrationState.Present;
            } catch (OperationCanceledException) { throw; }
            catch (Exception) { return RegistrationState.Unknown; }
        }

        internal static string Describe(RegistrationState state) {
            switch (state) {
                case RegistrationState.Missing: return "此项安装登记已移除；下方仅列出仍存在的候选目录。";
                case RegistrationState.Present: return "安装登记仍存在，可能尚未完成或已取消卸载。完成后可再次核对。";
                case RegistrationState.Unregistered: return "该项未登记卸载信息，请核对候选路径是否确实属于此软件。";
                default: return "暂时无法核实安装登记，未将此项标记为已卸载。";
            }
        }

        internal static List<ResidualCandidate> Find(AppEntry app, CancellationToken token, IEnumerable<string> configRoots = null, bool test = false) {
            var found = new Dictionary<string, ResidualCandidate>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = (path, evidence) => {
                token.ThrowIfCancellationRequested();
                try {
                    if (!Directory.Exists(path) && !File.Exists(path)) return;
                    string safe = Safety.Check(path, test);
                    ResidualCandidate existing;
                    if (found.TryGetValue(safe, out existing)) { if (!existing.Evidence.Contains(evidence)) existing.Evidence += "；" + evidence; return; }
                    found.Add(safe, new ResidualCandidate { Path = safe, Evidence = evidence });
                } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
            };
            if (app.Path.Length > 0) add(app.Path, Scanner.CleanPath(app.InstallLocation).Equals(app.Path, StringComparison.OrdinalIgnoreCase)
                ? "登记的安装位置" : "从程序或卸载器推定的位置，需核对");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> name = value => { if (value.Length >= 4 && value.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0) names.Add(value); };
            name(app.Name);
            if (app.Path.Length > 0) name(System.IO.Path.GetFileName(app.Path));
            var roots = configRoots ?? new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
            foreach (string root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
                foreach (string candidate in names) add(System.IO.Path.Combine(root, candidate), "配置根目录内与软件名或安装文件夹名完全匹配");
            foreach (var candidate in found.Values) {
                token.ThrowIfCancellationRequested();
                candidate.Size = Directory.Exists(candidate.Path)
                    ? InstalledSizeLookup.Measure(candidate.Path, token, 50000, 1200, null)
                    : new SizeMeasurement { Bytes = FileSizes.ReadLength(candidate.Path), Note = "仅此文件" };
            }
            return found.Values.OrderBy(c => c.Path, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public partial class MainForm {
        readonly List<AppEntry> pendingUninstalls = new List<AppEntry>();
        Panel uninstallFollowup;
        Button followupButton;

        Panel BuildUninstallFollowup() {
            uninstallFollowup = new CanvasPanel { Dock = DockStyle.Top, Height = 38, Visible = false };
            followupButton = Button("卸载结果待核对", () => {
                if (!Idle()) return;
                if (pendingUninstalls.Count == 1) { ShowResidualReview(pendingUninstalls[0], true); return; }
                var menu = new ContextMenuStrip();
                foreach (var app in pendingUninstalls.ToArray()) {
                    var target = app;
                    menu.Items.Add(app.Name, null, (s, e) => ShowResidualReview(target, true));
                }
                GlassMenus.Style(menu); menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
                menu.Show(followupButton, new Point(0, followupButton.Height));
            });
            followupButton.Dock = DockStyle.Fill; followupButton.AutoSize = false;
            uninstallFollowup.Controls.Add(followupButton);
            pendingUninstalls.AddRange(catalogState.Pending); UpdateUninstallFollowup(); return uninstallFollowup;
        }

        void RememberUninstall(AppEntry app) {
            if (!pendingUninstalls.Any(a => a.Id == app.Id)) pendingUninstalls.Add(app);
            try { catalogState.Remember(app); } catch (Exception ex) { ShowError(new IOException("核对任务未保存，关闭程序前请重试。" + ex.Message)); }
            UpdateUninstallFollowup();
        }

        void FinishUninstallReview(AppEntry app) {
            catalogState.Finish(app.Id);
            pendingUninstalls.RemoveAll(a => a.Id == app.Id); UpdateUninstallFollowup();
        }

        void UpdateUninstallFollowup() {
            uninstallFollowup.Visible = pendingUninstalls.Count > 0;
            followupButton.Text = "卸载结果待核对 · " + pendingUninstalls.Count + " 项";
        }

        void ApplyRegistrationResult(AppEntry app, RegistrationState state) {
            if (state != RegistrationState.Missing) return;
            RememberUninstall(app);
            var old = apps.FirstOrDefault(a => a.Id == app.Id);
            if (old != null) {
                var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag == old);
                if (row != null) grid.Rows.Remove(row);
                apps.Remove(old); ids.Remove(old.Id); RebuildCatalogIndex(); ApplyFilter();
            }
            UpdateUninstallFollowup();
        }

        void ShowResidualReview(AppEntry app, bool afterLaunch, Action<Form> ready = null) {
            using (var f = new Form { Text = "卸载核对与残留 · " + app.Name, ClientSize = new Size(830, 600), MinimumSize = new Size(730, 510), StartPosition = FormStartPosition.CenterParent, Font = Font, AutoScaleMode = AutoScaleMode.None, Padding = new Padding(18) })
            using (var cancellation = new CancellationTokenSource()) {
                var note = new Label { Name = "reviewStatus", Dock = DockStyle.Top, Height = 80, Text = afterLaunch && app.Package.Length == 0
                    ? "请先在原卸载器中完成操作，再点击“核对结果与残留”。\r\n可关闭此窗口，稍后从软件列表上方继续核对。"
                    : "正在核对安装登记与仍存在的目录…", AutoEllipsis = true };
                var list = new SmoothGrid { Name = "reviewList", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None, EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None };
                list.Columns.Add("path", "候选路径"); list.Columns.Add("size", "大小"); list.Columns.Add("evidence", "发现依据");
                list.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
                list.Columns[0].AutoSizeMode = list.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                list.Columns[0].FillWeight = 65; list.Columns[2].FillWeight = 35;
                list.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                foreach (DataGridViewColumn column in list.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
                list.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Palette.Side, ForeColor = Muted, Padding = new Padding(8,0,0,0), SelectionBackColor = Palette.Side, SelectionForeColor = Muted };
                list.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Palette.Surface, ForeColor = Ink, SelectionBackColor = Palette.Soft, SelectionForeColor = Ink, Padding = new Padding(9,0,4,0) };
                list.BackgroundColor = Palette.Surface; list.ColumnHeadersHeight = Px(36); list.RowTemplate.Height = Px(48);
                var preview = new TextBox { Name = "reviewPreview", Dock = DockStyle.Bottom, Height = 135, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Text = "选择一项查看完整路径、大小依据与处理范围。\r\n候选目录可能包含账号、配置、存档等个人数据，请逐项核对。" };
                var actions = new CanvasFlowPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(0,8,0,0), WrapContents = false };
                bool running = false, closeRequested = false;
                RegistrationState state = RegistrationState.Unknown;
                Button refresh = null, open = null, recycle = null, fileTools = null;
                Func<ResidualCandidate> selected = () => list.SelectedRows.Count == 0 ? null : list.SelectedRows[0].Tag as ResidualCandidate;
                Action selectionChanged = () => {
                    var candidate = selected();
                    open.Enabled = fileTools.Enabled = candidate != null && !running;
                    recycle.Enabled = candidate != null && !running && (state == RegistrationState.Missing || state == RegistrationState.Unregistered);
                    if (candidate != null) preview.Text = candidate.Path + "\r\n" + candidate.DisplaySize + " · " + candidate.Evidence + "\r\n" + candidate.Size.Note
                        + "\r\n处理范围：该目标及其全部内容。配置目录可能包含个人数据；不会自动批量清理。";
                };
                Func<Task> load = async () => {
                    if (running) return;
                    running = true; refresh.Enabled = false; selectionChanged(); note.Text = "正在核对登记、候选路径与大小…";
                    try {
                        var token = cancellation.Token;
                        var result = await Task.Run(() => Tuple.Create(ResidualReview.Probe(app, token), ResidualReview.Find(app, token)));
                        token.ThrowIfCancellationRequested();
                        state = result.Item1;
                        list.Rows.Clear();
                        foreach (var candidate in result.Item2) {
                            int index = list.Rows.Add(candidate.Path, candidate.DisplaySize, candidate.Evidence);
                            list.Rows[index].Tag = candidate; list.Rows[index].Cells[0].ToolTipText = candidate.Path;
                        }
                        list.ClearSelection(); list.CurrentCell = null;
                        note.Text = ResidualReview.Describe(state) + "\r\n" + (result.Item2.Count == 0 ? "未找到安全范围内的明确候选路径。" : "找到 " + result.Item2.Count + " 个候选位置。逐项核对后优先移到回收站。")
                            + (state == RegistrationState.Present || state == RegistrationState.Unknown ? " 如需手动处理，可进入文件解锁页。" : "");
                        preview.Text = "选择一项查看完整路径、大小依据与处理范围。\r\n同名目录仅是候选，不能据此确认归属。";
                        ApplyRegistrationResult(app, state);
                        Store.Log("卸载核对：" + app.Name + "；" + ResidualReview.Describe(state) + "；候选 " + result.Item2.Count);
                    } catch (OperationCanceledException) { note.Text = "已停止核对。"; }
                    catch (Exception ex) { note.Text = "核对未完成：" + ex.Message; }
                    finally { running = false; refresh.Enabled = true; selectionChanged(); if (closeRequested) f.Close(); }
                };
                refresh = Button("核对结果与残留", async () => await load(), true);
                open = Button("打开位置", () => { var item = selected(); if (item != null) OpenPath(item.Path); });
                fileTools = Button("文件解锁…", () => { var item = selected(); if (item != null) { pathBox.Text = item.Path; SwitchPage(1); f.Close(); } });
                recycle = Button("移到回收站…", async () => {
                    var item = selected(); if (running || item == null) return;
                    using (var confirmation = BuildConfirmation("移到回收站", item.Path + "\r\n\r\n" + item.DisplaySize + "（当前统计，可能变化）\r\n发现依据：" + item.Evidence + "\r\n\r\n将处理该目标及其全部内容。配置目录可能含个人数据，请确认归属及保留需要。Windows 若提示无法回收，可取消。", false))
                        if (confirmation.ShowDialog(f) != DialogResult.OK) return;
                    running = true; refresh.Enabled = false; selectionChanged();
                    try { await RecycleInBackground(item.Path); note.Text = "已完成回收站操作。"; }
                    catch (Exception ex) { MessageBox.Show(f, ex.Message, "未能完成回收"); }
                    finally { running = false; refresh.Enabled = true; }
                    if (closeRequested) f.Close(); else await load();
                });
                actions.Controls.AddRange(new Control[] { refresh, open, recycle, fileTools });
                recycle.Name = "reviewRecycle";
                actions.Controls.Add(Button("结束本次核对", () => {
                    if (running) return;
                    try { FinishUninstallReview(app); f.Close(); } catch (Exception ex) { ShowError(ex); }
                }));
                f.Controls.Add(list); f.Controls.Add(preview); f.Controls.Add(actions); f.Controls.Add(note);
                list.SelectionChanged += (s, e) => selectionChanged();
                f.FormClosing += (s, e) => { if (running) { closeRequested = true; cancellation.Cancel(); e.Cancel = true; } };
                f.Shown += async (s, e) => { WindowChrome.Apply(f.Handle); if (!afterLaunch || app.Package.Length > 0) await load(); if (!f.IsDisposed && ready != null) ready(f); };
                ApplyTheme(f); if (dpiScale != 1) f.Scale(new SizeF(dpiScale, dpiScale));
                f.Icon = Icon; list.Columns[1].Width = Px(112); list.Columns[2].MinimumWidth = Px(145);
                selectionChanged(); f.ShowDialog(this);
            }
        }

        // The shell recycle UI requires an STA thread; directory preflight must not freeze the main window.
        static Task RecycleInBackground(string path) {
            var completion = new TaskCompletionSource<bool>();
            var thread = new Thread(() => { try { Operations.Recycle(path); completion.SetResult(true); } catch (Exception ex) { completion.SetException(ex); } });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
        }
    }
}
