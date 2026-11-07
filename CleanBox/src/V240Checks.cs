using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class Tests {
        static void VerifyV240(string root) {
            var app = new AppEntry { Name = "State fixture", RegistryPath = "state-test-key", Version = "1", Path = Path.Combine(root, "app"),
                SizeBytes = 83886080, SizePartial = true, SizeKind = AppSizeKind.InstallDirectory, SizeMeasuredUtc = DateTime.UtcNow, SizeNote = "fixture" };
            string statePath = Path.Combine(root, "state");
            var first = new CatalogState(statePath); first.Remember(app); first.SaveMeasurement(app);
            var restarted = new CatalogState(statePath);
            Check(restarted.Pending.Single().Id == app.Id, "Residual snapshot survives a restart with its original identity and path");
            var rescanned = new AppEntry { Name = app.Name, RegistryPath = app.RegistryPath, Version = "1", Path = app.Path, SizeBytes = 1 };
            restarted.RestoreMeasurement(rescanned);
            Check(rescanned.SizeBytes == app.SizeBytes && rescanned.SizePartial && !rescanned.SizeStale && rescanned.SizeMeasuredUtc == app.SizeMeasuredUtc,
                "Rescan restores measured bytes, partial scope and original timestamp");
            rescanned.Version = "2"; restarted.RestoreMeasurement(rescanned);
            Check(rescanned.SizeStale && rescanned.SizeBadge.Contains("待重算") && !MainForm.MatchesSize(rescanned, 3), "A version change marks saved size stale and removes it from numeric size ranges");
            rescanned.Version = "1"; rescanned.Path += "-moved"; restarted.RestoreMeasurement(rescanned);
            Check(rescanned.SizeStale, "A changed install path invalidates saved measurement");
            Check(MainForm.MatchesSize(app, 5) && !MainForm.MatchesSize(app, 3) && !MainForm.MatchesSize(app, 2), "An 80 MB lower bound cannot be classified as a small or medium application");
            app.SizeBytes = 2L * 1024 * 1024 * 1024;
            Check(MainForm.MatchesSize(app, 1) && MainForm.MatchesSize(app, 5), "A partial size above 1 GB remains a confirmed large-app lower bound");
            var other = new AppEntry { Name = "Second task", RegistryPath = "another-task" };
            first.Remember(other); restarted.Finish(app.Id);
            Check(new CatalogState(statePath).Pending.Single().Id == other.Id, "Explicit task completion merges with another instance without dropping unrelated tasks");
            string scanRoot = Path.Combine(root, "usage"); Directory.CreateDirectory(Path.Combine(scanRoot, "child"));
            File.WriteAllBytes(Path.Combine(scanRoot, "large.bin"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(scanRoot, "child", "small.bin"), new byte[4096]);
            var usage = DiskUsage.Scan(scanRoot, CancellationToken.None, null);
            Check(usage.Root.Bytes == 20480 && usage.Files == 2 && !usage.Root.Partial && usage.Largest[0].Name == "large.bin",
                "Disk analysis aggregates nested directories once and ranks files by numeric size");
            var limited = DiskUsage.Scan(scanRoot, CancellationToken.None, null, 2);
            Check(limited.Limited && limited.Root.Partial, "Disk analysis marks entry-limit results incomplete");
            using (var canceled = new CancellationTokenSource()) {
                canceled.Cancel(); var result = DiskUsage.Scan(scanRoot, canceled.Token, null);
                Check(result.Canceled && result.Root.Partial, "Canceled disk analysis returns a clearly incomplete snapshot");
            }
            string link = Path.Combine(scanRoot, "linked");
            Store.RunPowerShell("New-Item -ItemType Junction -Path " + Store.QuotePS(link) + " -Target " + Store.QuotePS(Path.Combine(scanRoot,"child")) + " | Out-Null", CancellationToken.None);
            try {
                usage = DiskUsage.Scan(scanRoot, CancellationToken.None, null);
                Check(usage.Root.Bytes == 20480 && usage.Root.Partial && usage.Skipped > 0, "Disk analysis never traverses junctions or counts their targets twice");
            } finally { Directory.Delete(link, false); }
        }
    }

    public partial class MainForm {
        async Task VerifyUsagePage(Action<bool, string> check) {
            string root = Path.Combine(Store.Home, "tests", "usage-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "child"));
            File.WriteAllBytes(Path.Combine(root, "large.bin"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(root, "child", "small.bin"), new byte[4096]);
            bool previousDark = Palette.Dark;
            try {
                usageNavigation.PerformClick(); usageTarget.Text = root;
                await AnalyzeUsage();
                check(currentPage == 4 && usageNavigation.Selected && usageResult.Root.Bytes == 20480, "Disk analysis works through its normal navigation and scan entry points");
                check(usageGrid.Rows[0].Cells[0].Value.ToString() == "large.bin", "Disk listing starts with the largest entry");
                var child = usageResult.Root.Children.First(n => n.Directory);
                ShowUsageDirectory(child); check(usageUp.Enabled && usageGrid.Rows.Count == 1, "Directory expansion displays its children and enables parent navigation");
                usageUp.PerformClick(); check(usageCurrent == usageResult.Root, "Parent navigation returns to the analyzed root");
                var synthetic = Enumerable.Range(0, 405).Select(i => new UsageNode { Name = "Page fixture " + i, Path = Path.Combine(root, "page-" + i), Bytes = i, Parent = usageResult.Root }).ToList();
                usageResult.Root.Children.AddRange(synthetic); ShowUsageDirectory(usageResult.Root);
                var firstPage = usageGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag).ToArray();
                ScrollSkin.RefreshTree(this);
                check(firstPage.Length == 400 && usageNext.Enabled && ScrollSkin.Find(usageGrid).NativeBarsAreClipped(), "Large directory pages show 400 rows with a clipped scrollbar");
                usageNext.PerformClick();
                check(usageGrid.Rows.Count == 7 && usagePrevious.Enabled && !usageNext.Enabled && !usageGrid.Rows.Cast<DataGridViewRow>().Any(r => firstPage.Contains(r.Tag)), "Directory paging reaches the remaining entries without duplication");
                usageResult.Root.Children.RemoveAll(n => synthetic.Contains(n)); ShowUsageDirectory(usageResult.Root);
                usageTop.PerformClick(); check(usageGrid.Rows.Count == 2 && !usageUp.Enabled, "Largest-files view ranks files across the whole analyzed tree");
                usageGrid.CurrentCell = usageGrid.Rows[0].Cells[0]; usageGrid.Rows[0].Selected = true; usageTransfer.PerformClick();
                check(currentPage == 1 && pathBox.Text == Path.Combine(root, "large.bin"), "A selected disk-analysis file transfers to the existing unlock page without an action on its contents");
                pathBox.Clear(); SwitchPage(4);
                var previousSize = ClientSize;
                ClientSize = new Size(Px(1024), Px(662));
                foreach (bool dark in new[] { false, true }) {
                    Palette.Dark = dark; RefreshMaterial(); PerformLayout(); ScrollSkin.RefreshTree(this); Refresh();
                    check(usageGrid.Parent.Height >= Px(120) && usageTarget.Width >= Px(180), "Disk-analysis list and path input fit a narrow window");
                    foreach (var button in new[] { usageStart, usageFolder, usageOpen, usageTransfer, usagePrevious, usageNext })
                        check(button.Right <= button.Parent.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height, "Disk-analysis action fits its container: " + button.Text);
                    using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(Store.Home, "preview", "v240-usage-" + (dark ? "dark" : "light") + ".png")); }
                    check(ScrollSkin.Find(usageGrid).NativeBarsAreClipped(), "Disk analysis keeps native scrollbar backgrounds clipped in both themes");
                }
                ClientSize = previousSize;
            } finally {
                Palette.Dark = previousDark; RefreshMaterial(); SwitchPage(0);
                if (Safety.Within(root, Path.Combine(Store.Home, "tests"))) Operations.Permanent(root, true);
            }
        }
    }
}
