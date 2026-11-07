using System;
using System.Collections.Generic;

namespace CleanBox {
    public partial class MainForm {
        readonly HashSet<string> registeredEntries = new HashSet<string>();
        readonly HashSet<string> installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> knownExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> diskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string RegisteredKey(AppEntry app) {
            return string.Join("\0", new[] { app.RegistryPath, app.Hive.ToString(), app.Name, app.Version, app.Path, app.Command });
        }
        static string LocationKey(AppEntry app) { return app.Path + "\0" + app.Name; }

        bool AcceptCatalogEntry(AppEntry app) {
            if (ids.Contains(app.Id)) return false;
            if (app.RegistryPath.Length > 0 && registeredEntries.Contains(RegisteredKey(app))) return false;
            if (IsCandidate(app) && (installedNames.Contains(LocationKey(app)) || app.Executable.Length > 0 && knownExecutables.Contains(app.Executable))) return false;
            return !app.Source.StartsWith("磁盘") || !diskNames.Contains(LocationKey(app));
        }

        void IndexCatalogEntry(AppEntry app) {
            if (app.RegistryPath.Length > 0) { registeredEntries.Add(RegisteredKey(app)); installedNames.Add(LocationKey(app)); }
            if (app.Executable.Length > 0) knownExecutables.Add(app.Executable);
            if (app.Source.StartsWith("磁盘")) diskNames.Add(LocationKey(app));
        }

        void RebuildCatalogIndex() {
            registeredEntries.Clear(); installedNames.Clear(); knownExecutables.Clear(); diskNames.Clear();
            foreach (var app in apps) IndexCatalogEntry(app);
        }
    }
}
