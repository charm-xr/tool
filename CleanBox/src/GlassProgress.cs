using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CleanBox {
    internal sealed class GlassProgress : Control, IMotionVisual {
        double phase;

        internal GlassProgress() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
            AccessibleName = "正在处理";
            AccessibleDescription = "任务进行中，完成进度尚无法确定";
        }

        bool CanAnimate {
            get {
                var form = FindForm();
                return IsHandleCreated && Visible && Enabled && Palette.Animate
                    && (form == null || form.WindowState != FormWindowState.Minimized);
            }
        }

        internal void RefreshMotion() {
            if (CanAnimate) MotionClock.Wake(this);
            else { MotionClock.Remove(this); Settle(); }
            Invalidate();
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); RefreshMotion(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RefreshMotion(); }
        protected override void OnHandleDestroyed(EventArgs e) { MotionClock.Remove(this); base.OnHandleDestroyed(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); RefreshMotion(); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        public Control MotionControl { get { return this; } }
        public bool Advance(double seconds) {
            if (!CanAnimate) { Settle(); return false; }
            phase = (phase + Math.Min(.08, Math.Max(0, seconds)) / 2.8) % 1;
            Invalidate(); return true;
        }
        public void Settle() { phase = .25; Invalidate(); }
        protected override void Dispose(bool disposing) {
            if (disposing) MotionClock.Remove(this);
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var graphics = e.Graphics;
            // Paint the complete underlay in the same frame to clear previous light positions.
            GlassPaint.Underlay(this, graphics);
            if (Width < 10 || Height < 6) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = graphics.DpiX / 96f;
            float height = Math.Max(2, Math.Min(14 * dpi, Height - 6 * dpi));
            var track = new RectangleF(4 * dpi, (Height - height) / 2, Math.Max(2, Width - 8 * dpi), height);
            bool highContrast = SystemInformation.HighContrast;
            if (Palette.Glass) GlassPaint.Surface(this, graphics, track, height / 2, 0);
            else using (var shape = Palette.Shape(track, height / 2)) {
                using (var brush = new SolidBrush(highContrast ? SystemColors.Control : Palette.Side)) graphics.FillPath(brush, shape);
                using (var border = new Pen(highContrast ? SystemColors.ControlText : Palette.Line)) graphics.DrawPath(border, shape);
            }

            var inner = track; inner.Inflate(-2 * dpi, -2 * dpi);
            if (inner.Width <= 1 || inner.Height <= 1) return;
            float travel = (float)((1 - Math.Cos(phase * Math.PI * 2)) / 2);
            float length = inner.Width * .43f;
            var segment = new RectangleF(inner.Left + (inner.Width - length) * travel, inner.Top, length, inner.Height);
            if (Palette.Glass) GlassPaint.ButtonLight(graphics, track, height / 2,
                new PointF((segment.Left + length / 2 - track.Left) / track.Width, .3f), .36f, 0, false);
            using (var shape = Palette.Shape(segment, segment.Height / 2)) {
                if (highContrast) {
                    using (var brush = new SolidBrush(SystemColors.Highlight)) graphics.FillPath(brush, shape);
                } else {
                    using (var brush = new LinearGradientBrush(segment, Palette.Blend(Palette.Accent, Color.White, .22), Palette.Accent, LinearGradientMode.Vertical))
                        graphics.FillPath(brush, shape);
                    using (var border = new Pen(Color.FromArgb(Palette.Dark ? 100 : 155, Color.White))) graphics.DrawPath(border, shape);
                }
            }
            if (Palette.Glass) GlassPaint.ButtonLight(graphics, segment, segment.Height / 2, new PointF(.55f, .2f), .7f, 0, true);
        }
    }
}
