using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CleanBox {
    internal static class CatalogClassification {
        static readonly Regex runtimeName = new Regex(
            @"^Microsoft Visual C\+\+.*\b(Redistributable|Runtime)\b|^Microsoft \.NET (Runtime|Host|SDK|Targeting Pack)\b|^Microsoft Windows Desktop Runtime\b|^Microsoft ASP\.NET Core .*\b(Runtime|Shared Framework)\b|^Microsoft Edge WebView2 Runtime\b|^DirectX .*Runtime\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static int Kind(AppEntry app) {
            if (app.Protected) return 3;
            return runtimeName.IsMatch(app.Name) ? 2 : 1;
        }
        internal static string Publisher(AppEntry app) {
            return string.IsNullOrWhiteSpace(app.Publisher) ? "发布者未提供" : app.Publisher.Trim();
        }
        internal static string Drive(AppEntry app) {
            foreach (string path in new[] { app.Path, app.Executable }) {
                if (Regex.IsMatch(path ?? "", @"^[A-Za-z]:[\\/]")) return char.ToUpperInvariant(path[0]) + ":\\";
            }
            return "位置未知";
        }
    }

    public partial class MainForm {
        ChoiceButton kindFilter, publisherFilter, driveFilter;
        UiButton advancedButton, resetFiltersButton;
        Panel advancedFilters;
        Label filterSummary;
        bool updatingFilters;

        Panel BuildFilters() {
            advancedFilters = new SurfacePanel {
                Dock = DockStyle.Top, Height = 96, Padding = new Padding(12, 9, 12, 9), Visible = false
            };
            var columns = new CanvasTablePanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            kindFilter = FilterChoice("软件类型", new[] { "全部类型", "主要软件", "运行库与组件", "系统组件" });
            kindFilter.SelectedIndex = 1;
            publisherFilter = FilterChoice("发布者", new[] { "全部发布者" });
            driveFilter = FilterChoice("所在磁盘", new[] { "全部磁盘" });
            columns.Controls.Add(FilterColumn("软件类型", kindFilter), 0, 0);
            columns.Controls.Add(FilterColumn("发布者", publisherFilter), 1, 0);
            columns.Controls.Add(FilterColumn("所在磁盘", driveFilter), 2, 0);
            advancedFilters.Controls.Add(columns);
            tips.SetToolTip(kindFilter, "主要软件会隐藏系统组件和常见运行库。组件按系统标记与名称识别，可切换到全部类型核对。");
            return advancedFilters;
        }

        ChoiceButton FilterChoice(string name, IEnumerable<string> options) {
            var choice = new ChoiceButton { Dock = DockStyle.Top, Height = 39, AccessibleName = name };
            choice.Items.AddRange(options.Cast<object>());
            choice.SelectedIndex = 0;
            choice.SelectedIndexChanged += (s, e) => ApplyFilter();
            return choice;
        }

        Panel FilterColumn(string title, Control choice) {
            var column = new CanvasPanel { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 0) };
            column.Controls.Add(choice);
            column.Controls.Add(TextLabel(title, 24, "muted"));
            return column;
        }

        Panel BuildFilterSummary() {
            var row = new CanvasPanel { Dock = DockStyle.Top, Height = 32 };
            filterSummary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Tag = "muted" };
            resetFiltersButton = new UiButton { Text = "重置", Dock = DockStyle.Right, Width = 64 };
            resetFiltersButton.Click += (s, e) => ResetFilters();
            row.Controls.Add(filterSummary);
            row.Controls.Add(resetFiltersButton);
            UpdateFilterCaption();
            return row;
        }

        void ToggleFilters() {
            RefreshFilterChoices();
            advancedFilters.Visible = !advancedFilters.Visible;
            advancedButton.Selected = advancedFilters.Visible;
        }

        void RefreshFilterChoices() {
            updatingFilters = true;
            try {
                ReplaceChoices(publisherFilter, "全部发布者", apps.Select(CatalogClassification.Publisher));
                ReplaceChoices(driveFilter, "全部磁盘", apps.Select(CatalogClassification.Drive));
            } finally { updatingFilters = false; }
        }

        static void ReplaceChoices(ChoiceButton choice, string all, IEnumerable<string> values) {
            string selected = ChoiceValue(choice);
            var items = values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (selected != null && !items.Contains(selected, StringComparer.OrdinalIgnoreCase)) items.Add(selected);
            choice.SelectedIndex = -1;
            choice.Items.Clear();
            choice.Items.Add(all);
            choice.Items.AddRange(items.Cast<object>());
            choice.SelectedIndex = selected == null ? 0 : items.FindIndex(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase)) + 1;
        }

        static string ChoiceValue(ChoiceButton choice) {
            return choice == null || choice.SelectedIndex <= 0 ? null : choice.Items[choice.SelectedIndex].ToString();
        }

        bool MatchesAdvanced(AppEntry app) {
            if (kindFilter == null) return true;
            int kind = kindFilter.SelectedIndex;
            string publisher = ChoiceValue(publisherFilter), drive = ChoiceValue(driveFilter);
            return (kind <= 0 || CatalogClassification.Kind(app) == kind)
                && (publisher == null || CatalogClassification.Publisher(app).Equals(publisher, StringComparison.OrdinalIgnoreCase))
                && (drive == null || CatalogClassification.Drive(app) == drive);
        }

        void ResetFilters() {
            updatingFilters = true;
            try {
                filter.SelectedIndex = 0;
                kindFilter.SelectedIndex = publisherFilter.SelectedIndex = driveFilter.SelectedIndex = 0;
                search.Clear();
            } finally { updatingFilters = false; }
            ApplyFilter();
        }

        void UpdateFilterCaption() {
            if (filterSummary == null) return;
            var conditions = new List<string>();
            if (filter.SelectedIndex > 0) conditions.Add(filter.Items[filter.SelectedIndex].ToString());
            if (kindFilter.SelectedIndex > 0) conditions.Add(kindFilter.Items[kindFilter.SelectedIndex].ToString());
            if (ChoiceValue(publisherFilter) != null) conditions.Add(ChoiceValue(publisherFilter));
            if (ChoiceValue(driveFilter) != null) conditions.Add(ChoiceValue(driveFilter));
            if (search.Text.Trim().Length > 0) conditions.Add("搜索“" + search.Text.Trim() + "”");
            filterSummary.Text = conditions.Count == 0 ? "全部条目" : string.Join(" · ", conditions);
            tips.SetToolTip(filterSummary, filterSummary.Text);
            advancedButton.Text = conditions.Count == 0 ? "筛选" : "筛选 · " + conditions.Count;
            resetFiltersButton.Enabled = conditions.Count > 0;
        }
    }
}
