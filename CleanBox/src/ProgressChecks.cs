using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

namespace CleanBox {
    public partial class MainForm {
        async Task VerifyProgressAppearance(Action<bool, string> check) {
            bool dark = Palette.Dark, glass = Palette.GlassEnabled, motion = Palette.MotionEnabled;
            try {
                Palette.GlassEnabled = true; Palette.MotionEnabled = true;
                Busy(true);
                foreach (bool theme in new[] { false, true }) {
                    Palette.Dark = theme; RefreshMaterial();
                    MotionClock.FinishAll(); progress.RefreshMotion();
                    using (var first = PaintWindowFrame(progress, Color.Magenta))
                    using (var second = PaintWindowFrame(progress, Color.Lime)) {
                        check(SamePixels(first, second) && CornersMatchParent(progress, first), "Progress paints a complete rounded frame over its parent in " + (theme ? "dark" : "light") + " mode");
                        for (int i = 0; i < 35; i++) progress.Advance(1.0 / 60);
                        using (var moved = PaintWindowFrame(progress, Color.Magenta)) {
                            check(PixelDifference(first, moved) > 2, "The progress light visibly moves without leaving old pixels");
                            moved.Save(Path.Combine(Store.Home, "preview", "v241-progress-" + (theme ? "dark" : "light") + ".png"));
                        }
                    }
                    check(progress.AccessibleRole == System.Windows.Forms.AccessibleRole.ProgressBar, "The custom progress exposes its purpose to accessibility");
                    Palette.MotionEnabled = false; MotionClock.FinishAll(); UpdateThemeCaption();
                    using (var paused = PaintWindowFrame(progress, Color.Magenta)) {
                        check(!progress.Advance(.03), "Motion-disabled progress stays static");
                        using (var after = PaintWindowFrame(progress, Color.Lime)) check(SamePixels(paused, after), "Disabling motion keeps a stable progress frame");
                    }
                    Palette.MotionEnabled = true; UpdateThemeCaption();
                    check(MotionClock.ActiveCount > 0, "Enabling motion resumes an ongoing task indicator");
                    Palette.GlassEnabled = false; RefreshMaterial();
                    using (var plain = PaintWindowFrame(progress, Color.Magenta)) check(CornersMatchParent(progress, plain), "Glass-disabled progress retains clean rounded corners");
                    Palette.GlassEnabled = true;
                }
                MotionClock.FinishAll(); progress.RefreshMotion();
                Busy(false); await Task.Delay(80);
                check(!progress.Visible && MotionClock.ActiveCount == 0, "Completing a task hides its indicator and stops its animation clock");
            } finally {
                Busy(false); Palette.Dark = dark; Palette.GlassEnabled = glass; Palette.MotionEnabled = motion;
                RefreshMaterial();
            }
        }
    }
}
