using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CleanBox {
    internal sealed class UiToggle : Button {
        bool isChecked;

        public bool Checked {
            get { return isChecked; }
            set {
                if (isChecked == value) return;
                isChecked = value;
                Text = value ? "开启" : "关闭";
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            }
        }

        public UiToggle() {
            Text = "关闭";
            AccessibleRole = AccessibleRole.CheckButton;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            GlassPaint.Underlay(this, g);
            float scale = g.DpiX / 96f;
            float height = Math.Min(28 * scale, Height - 6 * scale);
            var track = new RectangleF(Width - 52 * scale, (Height - height) / 2,
                48 * scale, height);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Checked ? Palette.Accent : Palette.Blend(Palette.Surface, Palette.Muted, .28);
            using (var path = Palette.Shape(track, height / 2)) {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var pen = new Pen(Checked ? Palette.Accent : Palette.Muted)) g.DrawPath(pen, path);
            }
            float diameter = height - 6 * scale;
            float left = Checked ? track.Right - diameter - 3 * scale : track.Left + 3 * scale;
            using (var brush = new SolidBrush(Color.White))
                g.FillEllipse(brush, left, track.Top + 3 * scale, diameter, diameter);
            TextRenderer.DrawText(g, Checked ? "开启" : "关闭", Font,
                new Rectangle(0, 0, Math.Max(0, (int)track.Left - 6), Height), Palette.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
                TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
            if (Focused && ShowFocusCues) {
                var focus = ClientRectangle;
                focus.Inflate(-1, -1);
                ControlPaint.DrawFocusRectangle(g, focus, Palette.Accent, Palette.Bg);
            }
        }

        protected override AccessibleObject CreateAccessibilityInstance() { return new ToggleAccessibility(this); }
        sealed class ToggleAccessibility : ControlAccessibleObject {
            readonly UiToggle toggle;
            public ToggleAccessibility(UiToggle owner) : base(owner) { toggle = owner; }
            public override AccessibleStates State {
                get { return base.State | (toggle.Checked ? AccessibleStates.Checked : AccessibleStates.None); }
            }
            public override string DefaultAction { get { return "切换"; } }
            public override void DoDefaultAction() { toggle.PerformClick(); }
        }
    }

    public partial class MainForm {
        UiButton settingsButton;

        Panel BuildSettings() {
            var page = Page();
            var heading = Heading("设置", "调整后立即生效，外观偏好会自动保存");
            var card = new SurfacePanel { Dock = DockStyle.Top, Height = 406, Padding = new Padding(20, 12, 20, 12) };
            var rows = new CanvasTablePanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) rows.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            themeButton = SettingsToggle("深色外观", ToggleTheme);
            densityButton = SettingsToggle("紧凑列表", () => { compact = !compact; UpdateDensity(); SavePreferences(); });
            glassButton = SettingsToggle("玻璃质感", ToggleGlass);
            motionButton = SettingsToggle("弹性动效", ToggleMotion);
            rows.Controls.Add(SettingsRow("深色外观", "开启使用深色，关闭使用浅色。", themeButton), 0, 0);
            rows.Controls.Add(SettingsRow("紧凑列表", "缩小行间距，一屏显示更多软件。", densityButton), 0, 1);
            rows.Controls.Add(SettingsRow("玻璃质感", "磨砂玻璃、透明层次和鼠标高光。", glassButton), 0, 2);
            rows.Controls.Add(SettingsRow("弹性动效", "按钮回弹与高光的柔和跟随。", motionButton), 0, 3);
            adminButton = Button(Native.IsAdmin ? "已是管理员" : "以管理员重启", Elevate);
            adminButton.AutoSize = false;
            adminButton.Width = 144;
            adminButton.Height = 40;
            adminButton.Enabled = !Native.IsAdmin;
            rows.Controls.Add(SettingsRow("管理员权限", Native.IsAdmin ? "当前已使用管理员权限。" : "通过 Windows 授权后，重新打开 CleanBox。", adminButton), 0, 4);
            card.Controls.Add(rows);
            page.Controls.Add(card);
            page.Controls.Add(heading);
            return page;
        }

        UiToggle SettingsToggle(string name, Action change) {
            var toggle = new UiToggle { Width = 108, Height = 40, AccessibleName = name };
            toggle.Click += (s, e) => change();
            return toggle;
        }

        Panel SettingsRow(string title, string description, Control action) {
            var row = new SettingsGlassRow { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), Padding = new Padding(16, 2, 16, 2) };
            var right = new CanvasTablePanel { Dock = DockStyle.Right, Width = 156, ColumnCount = 1, RowCount = 1 };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            action.Anchor = AnchorStyles.Right;
            action.Margin = Padding.Empty;
            right.Controls.Add(action, 0, 0);
            var words = new CanvasPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 12, 2) };
            var detailLabel = TextLabel(description, 25, "muted");
            words.Controls.Add(detailLabel);
            words.Controls.Add(TextLabel(title, 27));
            row.Controls.Add(words);
            row.Controls.Add(right);
            row.ObserveChildren();
            return row;
        }
    }
}
