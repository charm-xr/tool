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
        async Task VerifyCatalogExperience(Action<bool, string> check) {
            ResetFilters();
            string prefix = "Catalog performance " + Guid.NewGuid().ToString("N");
            var fixture = new AppEntry { Name = prefix + " measure", RegistryPath = "catalog-measure-fixture", InstallLocation = Path.Combine(Store.Home, "tests", "size-ui-" + Guid.NewGuid().ToString("N")), SizeNote = "本轮自动计算已达到时间上限" };
            Directory.CreateDirectory(fixture.InstallLocation);
            File.WriteAllBytes(Path.Combine(fixture.InstallLocation, "sample.bin"), new byte[8192]);
            var fixtures = new List<AppEntry> { fixture };
            for (int i = 0; i < 1500; i++) fixtures.Add(new AppEntry { Name = prefix + " " + i.ToString("D4"), RegistryPath = "catalog-perf-" + i, SizeBytes = i == 0 ? (long?)null : (i % 7 + 1) * 536870912L, SizeKind = AppSizeKind.RegisteredEstimate, Path = @"D:\CatalogFixture\" + i });
            var times = new List<double>();
            try {
                foreach (var app in fixtures) Add(app);
                int before = filterApplications;
                search.Text = prefix.Substring(0, 10); search.Text = prefix.Substring(0, 15); search.Text = prefix;
                check(filterApplications == before, "Typing does not rebuild the list on every keystroke");
                await Task.Delay(350);
                check(filterApplications == before + 1 && grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible) == fixtures.Count, "Debounced search applies the latest query once");
                largeAppsButton.PerformClick();
                var visible = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).Select(r => (AppEntry)r.Tag).ToArray();
                check(visible.All(a => a.SizeBytes > 1073741824L) && visible.Select(a => a.SizeBytes.Value).SequenceEqual(visible.Select(a => a.SizeBytes.Value).OrderByDescending(x => x)), "Large-software shortcut combines the search with numeric descending size order");
                sizeFilter.SelectedIndex = 4;
                check(grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible) == 2, "Unknown-size filter keeps absent sizes separate from zero bytes");
                sizeFilter.SelectedIndex = 0;
                var selected = grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == fixtures[200]);
                var top = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).Skip(70).First();
                grid.ClearSelection(); selected.Selected = true; grid.CurrentCell = selected.Cells[0]; grid.FirstDisplayedScrollingRowIndex = top.Index;
                search.Text = prefix.ToUpperInvariant(); ApplyFilter();
                check(Selected() == fixtures[200] && grid.Rows[grid.FirstDisplayedScrollingRowIndex] == top, "A matching selection and viewport survive a filter refresh");
                for (int i = 0; i < 6; i++) {
                    search.Text = i % 2 == 0 ? prefix + " 00" : prefix;
                    var clock = Stopwatch.StartNew(); ApplyFilter(); clock.Stop(); times.Add(clock.Elapsed.TotalMilliseconds);
                }
                Directory.CreateDirectory(Store.Data);
                File.WriteAllText(Path.Combine(Store.Data, "catalog-performance.txt"), "Rows: " + apps.Count + "\r\nFilter milliseconds: " + string.Join(", ", times.Select(t => t.ToString("0.0"))) + "\r\nMax: " + times.Max().ToString("0.0") + " ms\r\nTyping debounce: 220 ms");
                check(times.Max() < 1500, "Large-list filtering stays bounded (" + apps.Count + " rows, worst " + times.Max().ToString("0.0") + " ms)");
                search.Text = fixture.Name; ApplyFilter();
                check(detail.Text.Contains("时间上限") && measureButton.Enabled, "Unknown size shows a useful reason and an available remeasurement action");
                await RemeasureSelected();
                check(fixture.SizeBytes == 8192 && fixture.SizeKind == AppSizeKind.InstallDirectory && detail.Text.Contains("8 KB") && fixture.SizeBadge == "目录实测", "Single-app remeasurement updates both numeric data and its scope label");
                ApplyRegistrationResult(fixture, RegistrationState.Present);
                check(apps.Contains(fixture), "An existing registration stays in the catalog after follow-up");
                RememberUninstall(fixture); ApplyRegistrationResult(fixture, RegistrationState.Missing);
                check(!apps.Contains(fixture) && pendingUninstalls.Contains(fixture), "An absent registration removes the catalog entry while keeping residual follow-up available");
                FinishUninstallReview(fixture);
                check(!pendingUninstalls.Contains(fixture), "Only explicit completion removes a residual follow-up");
                await VerifyResidualDialog(check);
            } finally {
                var owned = new HashSet<AppEntry>(fixtures);
                for (int i = grid.Rows.Count - 1; i >= 0; i--) if (owned.Contains(grid.Rows[i].Tag as AppEntry)) grid.Rows.RemoveAt(i);
                apps.RemoveAll(a => owned.Contains(a)); foreach (var app in fixtures) ids.Remove(app.Id);
                RebuildCatalogIndex(); ResetFilters();
                if (Safety.Within(fixture.InstallLocation, Path.Combine(Store.Home, "tests"))) Operations.Permanent(fixture.InstallLocation, true);
            }
        }

        async Task VerifyResidualDialog(Action<bool, string> check) {
            string root = Path.Combine(Path.GetTempPath(), "CleanBox-review-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "settings.bin"), new byte[4096]);
            var app = new AppEntry { Name = "CleanBox review fixture", Path = root, InstallLocation = root,
                Hive = Microsoft.Win32.RegistryHive.CurrentUser, View = Microsoft.Win32.RegistryView.Registry64,
                RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\CleanBox-review-" + Guid.NewGuid().ToString("N") };
            bool previousDark = Palette.Dark;
            try {
                foreach (bool dark in new[] { false, true }) {
                    Palette.Dark = dark; RefreshMaterial();
                    Exception failure = null;
                    ShowResidualReview(app, false, form => {
                        try {
                            var list = (DataGridView)form.Controls.Find("reviewList", true)[0];
                            var recycle = (Button)form.Controls.Find("reviewRecycle", true)[0];
                            var preview = (TextBox)form.Controls.Find("reviewPreview", true)[0];
                            var statusLabel = (Label)form.Controls.Find("reviewStatus", true)[0];
                            check(list.Rows.Count == 1 && list.SelectedRows.Count == 0 && !recycle.Enabled, "Residual dialog shows candidates without selecting or scheduling deletion");
                            check(statusLabel.Text.Contains("登记已移除"), "Follow-up reports the actual single-record state");
                            list.CurrentCell = list.Rows[0].Cells[0]; list.Rows[0].Selected = true;
                            check(preview.Text.Contains(root) && preview.Text.Contains("4 KB") && preview.Text.Contains("登记的安装位置") && recycle.Enabled, "Residual selection previews full scope, measured size and discovery evidence");
                            form.ClientSize = new Size(Px(730), Px(510)); form.PerformLayout(); ScrollSkin.RefreshTree(form); form.Refresh();
                            var surface = ScrollSkin.Find(list);
                            check(surface != null && surface.NativeBarsAreClipped(), "Residual dialog uses the same clipped native-scrollbar treatment");
                            check(list.Columns[2].Width >= Px(145) && list.Columns[1].Width <= Px(120), "Residual evidence retains a readable column beside a compact size column");
                            foreach (var button in SettingsDescendants(form).OfType<Button>())
                                check(button.Right <= button.Parent.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height, "Residual action fits the narrow dialog: " + button.Text);
                            using (var bitmap = new Bitmap(form.Width, form.Height)) {
                                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                                bitmap.Save(Path.Combine(Store.Home, "preview", "v230-residual-" + (dark ? "dark" : "light") + ".png"));
                            }
                        } catch (Exception ex) { failure = ex; }
                        finally { form.Close(); }
                    });
                    if (failure != null) throw failure;
                    await Task.Delay(50);
                }
            } finally {
                Palette.Dark = previousDark; RefreshMaterial();
                if (Safety.Within(root, Path.GetTempPath()) && Path.GetFileName(root).StartsWith("CleanBox-review-")) Operations.Permanent(root);
            }
        }
    }
}
