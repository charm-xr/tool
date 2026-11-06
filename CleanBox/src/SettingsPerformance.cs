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
        static IEnumerable<Control> SettingsDescendants(Control parent) {
            yield return parent;
            foreach (Control child in parent.Controls)
                foreach (var nested in SettingsDescendants(child)) yield return nested;
        }

        // Measures synchronous real HWND paint work, not synthetic bitmap time
        // or monitor FPS. No inventory scan, filesystem action or saved setting.
        async Task MeasureSettingsPerformance() {
            if (!ScrollPaintWitness.IsWindowVisible(Handle)) { Hide(); Show(); }
            timer.Stop(); loadingPreferences = true;
            var rows = SettingsDescendants(pages[3]).OfType<SettingsGlassRow>().ToArray();
            var report = new List<string> { "Version " + Application.ProductVersion,
                "Live window; continuous movement between five rows; UI frame work includes pointer, spring and child HWND painting; excludes pacing waits." };
            foreach (bool dark in new[] { true, false }) {
                Palette.Dark = dark; Palette.GlassEnabled = true; Palette.MotionEnabled = true;
                SwitchPage(3); RefreshMaterial(); await Task.Delay(700); MotionClock.FinishAll();
                var active = new HashSet<SettingsGlassRow>();
                var samples = new List<double>(); int presented = 0;
                var witnesses = rows.Select(r => new ScrollPaintWitness(r, () => 0)).ToArray();
                try {
                    int previous = -1;
                    for (int frame = 0; frame < 136; frame++) {
                        int selected = (frame / 24) % rows.Length;
                        int before = witnesses.Sum(w => w.Count);
                        var clock = Stopwatch.StartNew();
                        if (previous != selected && previous >= 0) { rows[previous].LeaveLight(); active.Add(rows[previous]); }
                        var row = rows[selected];
                        row.MoveLight(new Point((int)(row.Width * (.12 + .76 * (frame % 24) / 23)), row.Height / 2));
                        active.Add(row); previous = selected;
                        var painting = active.ToArray();
                        foreach (var current in painting) {
                            if (!current.Advance(1.0 / 60)) active.Remove(current);
                            MotionClock.Remove(current);
                        }
                        foreach (var current in painting)
                            foreach (var child in SettingsDescendants(current)) child.Update();
                        clock.Stop();
                        if (frame >= 16) { samples.Add(clock.Elapsed.TotalMilliseconds); if (witnesses.Sum(w => w.Count) > before) presented++; }
                        await Task.Delay(1);
                    }
                    var sorted = samples.OrderBy(n => n).ToArray();
                    report.Add((dark ? "dark" : "light") + " size=" + ClientSize + " row=" + rows[0].Size
                        + " frames=" + presented + "/" + samples.Count + " meanMs=" + samples.Average().ToString("F2")
                        + " p95Ms=" + sorted[(int)(sorted.Length * .95) - 1].ToString("F2") + " maxMs=" + sorted.Last().ToString("F2"));
                    if (presented != samples.Count) throw new Exception("Performance probe missed live frames");
                } finally {
                    foreach (var witness in witnesses) witness.Dispose();
                    foreach (var row in rows) { row.LeaveLight(); row.Settle(); MotionClock.Remove(row); }
                }
            }
            Directory.CreateDirectory(Store.Data);
            File.WriteAllLines(Path.Combine(Store.Data, "settings-performance-" + Application.ProductVersion + ".txt"), report);
        }
    }
}
