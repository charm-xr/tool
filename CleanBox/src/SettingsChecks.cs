using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanBox {
    public partial class MainForm {
        async Task VerifySettingsRowLight(Action<bool, string> check) {
            bool darkBefore = Palette.Dark, glassBefore = Palette.GlassEnabled, motionBefore = Palette.MotionEnabled;
            var actions = new Control[] { themeButton, densityButton, glassButton, motionButton, adminButton };
            var rows = actions.Select(action => {
                Control parent = action.Parent;
                while (!(parent is SettingsGlassRow)) parent = parent.Parent;
                return (SettingsGlassRow)parent;
            }).ToArray();
            try {
                SwitchPage(3);
                Palette.GlassEnabled = true; Palette.MotionEnabled = true;
                foreach (bool dark in new[] { false, true }) {
                    Palette.Dark = dark; RefreshMaterial();
                    string mode = dark ? "Dark" : "Light";
                    for (int index = 0; index < rows.Length; index++) {
                        var row = rows[index];
                        var action = actions[index];
                        var bounds = row.Bounds; var actionBounds = action.Bounds;
                        row.LeaveLight(); row.Settle(); MotionClock.Remove(row);
                        using (var idle = PaintFrame(row, Color.Magenta, true))
                        using (var neighbor = PaintFrame(rows[(index + 1) % rows.Length], Color.Magenta, true))
                        using (var idleAction = PaintWindowFrame(action, Color.Magenta)) {
                            using (var otherSeed = PaintFrame(row, Color.Lime, true))
                                check(SamePixels(idle, otherSeed), mode + " settings row paints its complete background: " + index);
                            int materialGeneration = row.MaterialGeneration;
                            Control text = row.Controls[0].Controls[0];
                            var move = typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);
                            move.Invoke(text, new object[] { new MouseEventArgs(MouseButtons.None, 0, text.Width / 3, text.Height / 2, 0) });
                            for (int frame = 0; frame < 150; frame++) row.Advance(1.0 / 60);
                            using (var lit = PaintFrame(row, Color.Lime, true)) {
                                check(PixelDifference(idle, lit) > .8, mode + " hovering the setting text lights its row: " + index);
                                check(idle.GetPixel(0, 0) == lit.GetPixel(0, 0), mode + " setting light preserves rounded outside corners: " + index);
                                using (var next = PaintFrame(rows[(index + 1) % rows.Length], Color.Lime, true))
                                    check(SamePixels(neighbor, next), mode + " setting light never reaches another row: " + index);
                                move.Invoke(action, new object[] { new MouseEventArgs(MouseButtons.None, 0, action.Width / 2, action.Height / 2, 0) });
                                using (var pending = PaintFrame(row, Color.Lime, true))
                                    check(SamePixels(lit, pending), mode + " row light moves through spring frames instead of jumping: " + index);
                                for (int frame = 0; frame < 150; frame++) row.Advance(1.0 / 60);
                                using (var moved = PaintFrame(row, Color.Lime, true))
                                    check(PixelDifference(lit, moved) > .8, mode + " setting light follows across text and action: " + index);
                                using (var litAction = PaintWindowFrame(action, Color.Lime))
                                    check(PixelDifference(idleAction, litAction) > 1, mode + " action background shares the row light without a rectangular cutout: " + index);
                                int frameGeneration = row.FrameGeneration;
                                foreach (var child in SettingsDescendants(row))
                                    using (var repeated = PaintFrame(child, Color.Magenta, true)) { }
                                check(row.FrameGeneration == frameGeneration, mode + " every child reuses the composed row frame without redrawing the light: " + index);
                                using (var beforeReentry = PaintFrame(row, Color.Magenta, true)) {
                                    row.LeaveLight();
                                    row.MoveLight(new Point(Px(140), row.Height / 2));
                                    row.Advance(1.0 / 60);
                                    using (var reentered = PaintFrame(row, Color.Lime, true))
                                        check(PixelDifference(beforeReentry, reentered) > .8, mode + " immediate reentry refreshes the cached light position: " + index);
                                }
                            }
                            check(row.MaterialGeneration == materialGeneration && row.Bounds == bounds && action.Bounds == actionBounds,
                                mode + " setting light reuses its material and keeps controls fixed: " + index);
                            row.LeaveLight();
                            for (int frame = 0; frame < 150; frame++) row.Advance(1.0 / 60);
                            using (var cleared = PaintFrame(row, Color.Lime, true))
                                check(SamePixels(idle, cleared), mode + " settings exit clears every light pixel: " + index);
                            MotionClock.Remove(row);
                        }
                    }
                    rows[1].MoveLight(new Point(Px(260), rows[1].Height / 2));
                    for (int frame = 0; frame < 150; frame++) rows[1].Advance(1.0 / 60);
                    CaptureSettingsPreview("v222-settings-light-" + (dark ? "dark" : "light"));
                    rows[1].LeaveLight();
                    await Task.Delay(1100);
                    check(MotionClock.ActiveCount == 0, mode + " setting hover stops the clock after leaving");
                    Palette.MotionEnabled = false;
                    using (var idle = PaintFrame(rows[0], Color.Magenta, true)) {
                        rows[0].MoveLight(new Point(Px(220), rows[0].Height / 2));
                        using (var lit = PaintFrame(rows[0], Color.Lime, true))
                            check(PixelDifference(idle, lit) > .8 && MotionClock.ActiveCount == 0, mode + " setting lighting follows immediately when motion is disabled");
                        rows[0].LeaveLight();
                        using (var cleared = PaintFrame(rows[0], Color.Lime, true))
                            check(SamePixels(idle, cleared), mode + " motion-disabled setting has no remaining light");
                    }
                    Palette.GlassEnabled = false; RefreshMaterial();
                    using (var solid = PaintFrame(rows[0], Color.Magenta, true)) {
                        rows[0].MoveLight(new Point(Px(220), rows[0].Height / 2));
                        using (var moved = PaintFrame(rows[0], Color.Lime, true))
                            check(SamePixels(solid, moved), mode + " disabling glass also disables setting lighting");
                    }
                    Palette.GlassEnabled = true; Palette.MotionEnabled = true; RefreshMaterial();
                    using (var idle = PaintFrame(rows[0], Color.Magenta, true)) {
                        rows[0].MoveLight(new Point(Px(220), rows[0].Height / 2));
                        SwitchPage(0); SwitchPage(3);
                        using (var returned = PaintFrame(rows[0], Color.Lime, true))
                            check(SamePixels(idle, returned), mode + " returning to settings does not restore an old hover light");
                    }
                }
            } finally {
                foreach (var row in rows) { row.LeaveLight(); row.Settle(); MotionClock.Remove(row); }
                Palette.Dark = darkBefore; Palette.GlassEnabled = glassBefore; Palette.MotionEnabled = motionBefore;
                SwitchPage(0); RefreshMaterial();
            }
        }

        void VerifySettingsAndFilters(Action<bool, string> check) {
            check(kindFilter.SelectedIndex == 1 && grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible)
                .All(r => CatalogClassification.Kind((AppEntry)r.Tag) == 1), "Default view shows primary software and labels its active filter");
            check(filterSummary.Text.Contains("主要软件") && resetFiltersButton.Enabled, "Default filtering is visible and reversible");
            settingsButton.PerformClick();
            check(currentPage == 3 && pages[3].Visible && settingsButton.Selected, "Settings button opens the settings page");
            foreach (var button in new[] { themeButton, densityButton, glassButton, motionButton }) {
                var toggle = (UiToggle)button;
                bool previous = toggle.Checked;
                toggle.PerformClick();
                check(toggle.Checked != previous && toggle.Text == (toggle.Checked ? "开启" : "关闭"), "Settings switch shows its new state: " + toggle.AccessibleName);
                check((toggle.AccessibilityObject.State & AccessibleStates.Checked) != 0 == toggle.Checked, "Switch exposes checked state to accessibility");
                toggle.PerformClick();
                check(toggle.Checked == previous, "Settings switch returns to the previous state");
            }
            check(((UiToggle)themeButton).Checked == Palette.Dark && ((UiToggle)densityButton).Checked == compact
                && ((UiToggle)glassButton).Checked == Palette.GlassEnabled && ((UiToggle)motionButton).Checked == Palette.MotionEnabled,
                "Settings switches reflect the actual preference values");
            check(adminButton.Text == (Native.IsAdmin ? "已是管理员" : "以管理员重启"), "Administrator control describes its restart action");

            bool originalDark = Palette.Dark;
            Directory.CreateDirectory(Path.Combine(Store.Home, "preview"));
            foreach (bool dark in new[] { true, false }) {
                Palette.Dark = dark;
                RefreshMaterial();
                CaptureSettingsPreview("v220-settings-" + (dark ? "dark" : "light"));
                foreach (var toggle in new[] { themeButton, densityButton, glassButton, motionButton }) {
                    using (var first = PaintWindowFrame(toggle, Color.Magenta))
                    using (var second = PaintWindowFrame(toggle, Color.Lime)) {
                        if (!SamePixels(first, second)) {
                            first.Save(Path.Combine(Store.Home, "preview", "toggle-first.png"));
                            second.Save(Path.Combine(Store.Home, "preview", "toggle-second.png"));
                        }
                        check(SamePixels(first, second), "Switch paints a complete frame in both themes: " + toggle.AccessibleName + " " + toggle.Bounds + " parent=" + toggle.Parent.ClientSize);
                    }
                    check(toggle.Left >= 0 && toggle.Right <= toggle.Parent.ClientSize.Width
                        && toggle.Bottom <= toggle.Parent.ClientSize.Height, "Settings switch fits its row at screen DPI");
                }
                CaptureSettingsPreview("v220-settings-" + (dark ? "dark" : "light"));
            }
            Palette.Dark = originalDark;
            RefreshMaterial();
            SwitchPage(0);
            ResetFilters();
            check(grid.Rows.Cast<DataGridViewRow>().All(r => r.Visible), "Reset exposes every collected entry");

            var fixtures = new[] {
                new AppEntry { Name="Filter sample", Publisher="Filter Publisher A", RegistryPath="filter-check-a", Path=@"D:\ExampleA" },
                new AppEntry { Name="Filter sample", Publisher="Filter Publisher A", RegistryPath="filter-check-b", Path=@"C:\ExampleB" },
                new AppEntry { Name="Filter sample", Publisher="Filter Publisher B", Executable=@"D:\ExampleC\app.exe", Path=@"D:\ExampleC", Source="磁盘发现" },
                new AppEntry { Name="Microsoft Visual C++ 2022 Redistributable", Publisher="Microsoft Corporation", RegistryPath="filter-check-runtime", Path=@"C:\ExampleD" },
                new AppEntry { Name="Protected filter sample", Protected=true, RegistryPath="filter-check-protected" }
            };
            foreach (var app in fixtures) Add(app);
            try {
                RefreshFilterChoices();
                kindFilter.SelectedIndex = 1;
                publisherFilter.SelectedIndex = publisherFilter.Items.FindIndex(x => x.ToString() == "Filter Publisher A");
                driveFilter.SelectedIndex = driveFilter.Items.FindIndex(x => x.ToString() == @"D:\");
                filter.SelectedIndex = 1;
                search.Text = "Filter sample";
                var visible = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).ToArray();
                check(visible.Length == 1 && visible[0].Tag == fixtures[0], "Type, publisher, drive, source and search combine correctly");
                check(apps.Where(Matches).SequenceEqual(new[] { fixtures[0] }), "Export uses the same combined filter as the visible list");
                driveFilter.SelectedIndex = driveFilter.Items.FindIndex(x => x.ToString() == @"C:\");
                check(Selected() == fixtures[1], "Changing a filter updates selection and details");
                search.Text = "nonexistent filter target";
                check(Selected() == null && emptyList.Visible && selectionActions.All(b => !b.Enabled), "Empty combined results disable stale actions");
                resetFiltersButton.PerformClick();
                check(grid.Rows.Cast<DataGridViewRow>().All(r => r.Visible) && !resetFiltersButton.Enabled, "One-click reset clears every condition and search");
                check(CatalogClassification.Kind(fixtures[3]) == 2 && CatalogClassification.Kind(fixtures[4]) == 3
                    && CatalogClassification.Kind(new AppEntry { Name="Microsoft Visual Studio", Publisher="Microsoft Corporation" }) == 1,
                    "Runtime classification preserves full applications from the same publisher");
                check(CatalogClassification.Drive(new AppEntry()) == "位置未知" && CatalogClassification.Publisher(new AppEntry()) == "发布者未提供", "Missing metadata has explicit filter categories");
            } finally {
                ResetFilters();
                foreach (var app in fixtures) {
                    var row = grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == app);
                    grid.Rows.Remove(row); apps.Remove(app); ids.Remove(app.Id);
                }
                RefreshFilterChoices();
                ApplyFilter();
            }
            kindFilter.SelectedIndex = 1;
            advancedButton.PerformClick();
            foreach (bool dark in new[] { true, false }) {
                Palette.Dark = dark;
                RefreshMaterial();
                CaptureSettingsPreview("v220-filters-" + (dark ? "dark" : "light"));
            }
            var fullSize = ClientSize;
            ClientSize = new Size(Px(1024), Px(662));
            PerformLayout();
            ArrangeCatalog();
            check(advancedFilters.Bottom < grid.Parent.Parent.Bottom && grid.Parent.Height >= Px(160), "Expanded filters leave usable list space in a narrow window");
            foreach (var choice in new[] { kindFilter, publisherFilter, driveFilter })
                check(choice.Right <= choice.Parent.ClientSize.Width && choice.Bottom <= choice.Parent.ClientSize.Height, "Advanced filter fits its column in a narrow window");
            CaptureSettingsPreview("v220-filters-narrow");
            settingsButton.PerformClick();
            check(themeButton.Top >= 0 && adminButton.RectangleToScreen(adminButton.ClientRectangle).Bottom
                <= pages[3].RectangleToScreen(pages[3].ClientRectangle).Bottom, "All settings fit vertically in a narrow window");
            CaptureSettingsPreview("v220-settings-narrow");
            ClientSize = fullSize;
            SwitchPage(0);
            advancedButton.PerformClick();
            ResetFilters();
            Palette.Dark = originalDark;
            RefreshMaterial();
        }

        void CaptureSettingsPreview(string name) {
            PerformLayout();
            ScrollSkin.RefreshTree(this);
            Refresh();
            using (var image = new Bitmap(Width, Height)) {
                DrawToBitmap(image, new Rectangle(Point.Empty, Size));
                image.Save(Path.Combine(Store.Home, "preview", name + ".png"), ImageFormat.Png);
            }
        }
    }
}
