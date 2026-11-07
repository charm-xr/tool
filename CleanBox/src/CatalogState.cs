using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CleanBox {
    internal sealed class SavedMeasurement {
        public string Id, Signature, Note;
        public long Bytes;
        public bool Partial;
        public AppSizeKind Kind;
        public long MeasuredUtcTicks;
    }

    internal sealed class CatalogStateData {
        public int Format = 1;
        public List<AppEntry> Pending = new List<AppEntry>();
        public List<SavedMeasurement> Measurements = new List<SavedMeasurement>();
    }

    internal sealed class CatalogState {
        readonly string directory;
        CatalogStateData data;
        internal CatalogState(string directory) {
            this.directory = directory;
            try { data = Read(); }
            catch (Exception ex) { data = new CatalogStateData(); Store.Log("历史记录读取失败：" + ex.Message); }
        }
        internal IEnumerable<AppEntry> Pending { get { return data.Pending; } }
        string FileName { get { return Path.Combine(directory, "catalog-state.json"); } }
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 16777216 }; }
        CatalogStateData Read() {
            if (directory == null || !File.Exists(FileName)) return new CatalogStateData();
            var value = Serializer().Deserialize<CatalogStateData>(File.ReadAllText(FileName, Encoding.UTF8));
            if (value == null || value.Format != 1 || value.Pending == null || value.Measurements == null)
                throw new IOException("历史记录格式无法识别，原文件已保留。");
            return value;
        }
        void Update(Action<CatalogStateData> change) {
            if (directory == null) { change(data); return; }
            Directory.CreateDirectory(directory);
            // Merge against the latest file while holding an exclusive cross-process lock.
            using (var gate = new FileStream(Path.Combine(directory, "catalog-state.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
                var current = Read();
                change(current);
                string temporary = FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    File.WriteAllText(temporary, Serializer().Serialize(current), new UTF8Encoding(false));
                    if (File.Exists(FileName)) File.Replace(temporary, FileName, null);
                    else File.Move(temporary, FileName);
                    data = current;
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        internal void Remember(AppEntry app) {
            Update(value => {
                if (!value.Pending.Any(a => a.Id == app.Id)) value.Pending.Add(app);
            });
        }
        internal void Finish(string id) { Update(value => value.Pending.RemoveAll(a => a.Id == id)); }
        static string Signature(AppEntry app) {
            return string.Join("\0", new[] { app.Name, app.Version, Scanner.CleanPath(app.InstallLocation),
                Scanner.CleanPath(app.Path), Scanner.CleanPath(app.Executable), app.Command, app.Package });
        }
        internal void SaveMeasurement(AppEntry app) {
            if (!app.SizeBytes.HasValue) return;
            var saved = new SavedMeasurement { Id = app.Id, Signature = Signature(app), Bytes = app.SizeBytes.Value,
                Partial = app.SizePartial, Kind = app.SizeKind, Note = app.SizeNote, MeasuredUtcTicks = app.SizeMeasuredUtc.Ticks };
            Update(value => { value.Measurements.RemoveAll(m => m.Id == saved.Id); value.Measurements.Add(saved); });
        }
        internal void RestoreMeasurement(AppEntry app) {
            var saved = data.Measurements.FirstOrDefault(m => m.Id == app.Id);
            if (saved == null || saved.Bytes < 0 || saved.MeasuredUtcTicks < DateTime.MinValue.Ticks || saved.MeasuredUtcTicks > DateTime.MaxValue.Ticks) return;
            app.SizeBytes = saved.Bytes; app.SizeKind = saved.Kind; app.SizePartial = saved.Partial;
            app.SizeMeasuredUtc = new DateTime(saved.MeasuredUtcTicks, DateTimeKind.Utc);
            app.SizeStale = !string.Equals(saved.Signature, Signature(app), StringComparison.OrdinalIgnoreCase);
            app.SizeNote = (app.SizeStale ? "版本、位置或安装信息已变化，显示上次实测值，请重新计算。\r\n" : "") + saved.Note;
        }
    }
}
