using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CleanBox {
    // Children sample this same material through GlassPaint.Underlay, so labels
    // and switches cannot cut rectangular holes in the moving row light.
    internal sealed class SettingsGlassRow : SurfacePanel, IMotionVisual {
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        readonly SpringValue lightX = new SpringValue(.5, 220, 19);
        readonly SpringValue lightY = new SpringValue(.5, 220, 19);
        readonly SpringValue lightAmount = new SpringValue(0);
        Bitmap material, frame;
        bool frameDirty = true;
        Point materialOrigin;
        Size materialCanvas;
        bool materialDark, materialGlass, pointerInside;
        float materialDpi;
        internal int MaterialGeneration { get; private set; }
        internal int FrameGeneration { get; private set; }

        internal void ObserveChildren() {
            foreach (Control child in Controls) Observe(child);
        }

        void Observe(Control child) {
            child.MouseEnter += ChildEnter;
            child.MouseMove += ChildMove;
            child.MouseLeave += ChildLeave;
            foreach (Control nested in child.Controls) Observe(nested);
        }

        void ChildEnter(object sender, EventArgs e) { UpdatePointer(); }
        void ChildLeave(object sender, EventArgs e) { UpdatePointer(); }
        void ChildMove(object sender, MouseEventArgs e) {
            MoveLight(PointToClient(((Control)sender).PointToScreen(e.Location)));
        }
        void UpdatePointer() {
            Point screen = Cursor.Position;
            Control hit = Control.FromChildHandle(WindowFromPoint(screen));
            // Leaving for another window or popup must clear the light even
            // when the pointer's screen coordinates still overlap this row.
            if (hit == this || (hit != null && Contains(hit))) MoveLight(PointToClient(screen));
            else LeaveLight();
        }
        protected override void OnMouseEnter(EventArgs e) { UpdatePointer(); base.OnMouseEnter(e); }
        protected override void OnMouseMove(MouseEventArgs e) { MoveLight(e.Location); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { UpdatePointer(); base.OnMouseLeave(e); }

        internal void MoveLight(Point point) {
            float dpi = DeviceDpi / 96f;
            using (var shape = Palette.Shape(CardBounds(dpi), 22 * dpi)) {
                if (!Visible || !Enabled || !Palette.Glass || !shape.IsVisible(point)) { LeaveLight(); return; }
            }
            double x = point.X / (double)Math.Max(1, Width), y = point.Y / (double)Math.Max(1, Height);
            if (!pointerInside) { lightX.Snap(x); lightY.Snap(y); RefreshFrame(); }
            pointerInside = true;
            lightX.Target = x; lightY.Target = y; lightAmount.Target = 1;
            MotionClock.Wake(this);
        }

        internal void LeaveLight() {
            if (!pointerInside && lightAmount.Target == 0) return;
            pointerInside = false; lightAmount.Target = 0;
            MotionClock.Wake(this);
        }

        RectangleF CardBounds(float dpi) {
            return new RectangleF(3 * dpi, 3 * dpi, Math.Max(1, Width - 6 * dpi), Math.Max(1, Height - 7 * dpi));
        }

        protected override void OnPaintBackground(PaintEventArgs e) { PaintMaterial(e.Graphics); }

        internal override void PaintMaterial(Graphics g) {
            var form = FindForm();
            Point origin = Point.Empty;
            for (Control node = this; node != null && node != form; node = node.Parent) origin.Offset(node.Left, node.Top);
            Size canvas = form == null ? Size : form.ClientSize;
            if (material == null || material.Size != Size || materialOrigin != origin || materialCanvas != canvas
                || materialDark != Palette.Dark || materialGlass != Palette.Glass || materialDpi != g.DpiX) {
                if (material != null) material.Dispose();
                if (frame != null) frame.Dispose();
                material = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
                material.SetResolution(g.DpiX, g.DpiY);
                // Cache the outside corners and all ancestor glass as well.
                // Every child can then copy an opaque slice of this row.
                using (var background = Graphics.FromImage(material)) {
                    GlassPaint.Underlay(this, background);
                    base.PaintMaterial(background);
                }
                frame = new Bitmap(material.Width, material.Height, PixelFormat.Format32bppPArgb);
                frame.SetResolution(g.DpiX, g.DpiY);
                frameDirty = true;
                materialOrigin = origin; materialCanvas = canvas;
                materialDark = Palette.Dark; materialGlass = Palette.Glass; materialDpi = g.DpiX;
                MaterialGeneration++;
            }
            if (frameDirty) {
                using (var composed = Graphics.FromImage(frame)) {
                    composed.DrawImageUnscaled(material, Point.Empty);
                    if (Palette.Glass) {
                        float dpi = g.DpiX / 96f;
                        GlassPaint.ButtonLight(composed, CardBounds(dpi), 22 * dpi,
                            new PointF((float)lightX.Value, (float)lightY.Value), (float)lightAmount.Value, 0, false, 280 * dpi);
                    }
                }
                FrameGeneration++;
                frameDirty = false;
            }
            g.DrawImageUnscaled(frame, Point.Empty);
        }

        public Control MotionControl { get { return this; } }
        public bool Advance(double seconds) {
            double x = lightX.Value, y = lightY.Value, amount = lightAmount.Value;
            bool moving = lightX.Step(seconds);
            moving = lightY.Step(seconds) || moving;
            moving = lightAmount.Step(seconds) || moving;
            if (x != lightX.Value || y != lightY.Value || amount != lightAmount.Value) RefreshFrame();
            return moving;
        }
        public void Settle() {
            lightX.Snap(lightX.Target); lightY.Snap(lightY.Target);
            lightAmount.Snap(pointerInside && Visible && Palette.Glass ? 1 : 0);
            RefreshFrame();
        }
        void RefreshFrame() { frameDirty = true; Invalidate(true); }
        protected override void OnVisibleChanged(EventArgs e) {
            if (!Visible) { pointerInside = false; Settle(); MotionClock.Remove(this); }
            base.OnVisibleChanged(e);
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { MotionClock.Remove(this); if (material != null) material.Dispose(); if (frame != null) frame.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
