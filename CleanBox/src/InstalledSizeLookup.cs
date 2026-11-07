using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CleanBox {
    internal sealed class InstalledSizeLookup {
        readonly Stopwatch scanTime = new Stopwatch();
        readonly Dictionary<string, Tuple<long?, bool>> cache = new Dictionary<string, Tuple<long?, bool>>(StringComparer.OrdinalIgnoreCase);

        internal void Fill(AppEntry app, string installLocation, CancellationToken token) {
            string directory = LocalPath(installLocation);
            if (directory == null || !Directory.Exists(directory)) {
                string command, arguments;
                if (!Scanner.ParseCommand(app.Command, out command, out arguments)) return;
                string icon = LocalPath(app.Executable), uninstaller = LocalPath(command);
                if (icon == null || uninstaller == null) return;
                directory = Path.GetDirectoryName(uninstaller);
                if (!string.Equals(directory, Path.GetDirectoryName(icon), StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(icon) || !File.Exists(uninstaller)) return;
            }
            if (directory.Length < 4 || Safety.IsSharedDirectory(directory) || Safety.IsSystemTree(directory)) return;

            var parent = Directory.GetParent(directory);
            bool engineLauncher = parent != null && parent.Parent != null
                && parent.Name.Equals("app", StringComparison.OrdinalIgnoreCase)
                && parent.Parent.Name.Equals("MobileAppEngine", StringComparison.OrdinalIgnoreCase);

            Tuple<long?, bool> size;
            if (!cache.TryGetValue(directory, out size)) {
                if (scanTime.ElapsedMilliseconds >= 4000) return;
                scanTime.Start();
                try {
                    bool partial;
                    size = Tuple.Create(MeasureDirectory(directory, token, out partial), partial);
                    cache[directory] = size;
                } finally { scanTime.Stop(); }
            }
            app.SizeBytes = size.Item1;
            app.SizePartial = size.Item2;
            app.SizeKind = engineLauncher ? AppSizeKind.EngineLauncher : AppSizeKind.InstallDirectory;
        }

        static string LocalPath(string path) {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try {
                string normalized = Safety.Normalize(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
                return new DriveInfo(normalized.Substring(0, 1)).DriveType == DriveType.Fixed ? normalized : null;
            } catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
        }

        internal static long? MeasureDirectory(string root, CancellationToken token, out bool partial) {
            partial = false;
            if (!Directory.Exists(root) || Safety.HasReparseAncestor(root)) return null;

            // Read only metadata. Bound work on large trees and keep the subtotal.
            var time = Stopwatch.StartNew();
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));
            long bytes = 0;
            int entries = 0, files = 0;
            while (pending.Count > 0) {
                token.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                try {
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { partial = true; continue; }
                    foreach (var item in directory.EnumerateFileSystemInfos()) {
                        token.ThrowIfCancellationRequested();
                        if (++entries > 5000 || time.ElapsedMilliseconds >= 200) {
                            partial = true;
                            return files > 0 ? bytes : (long?)null;
                        }
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { partial = true; continue; }
                        var child = item as DirectoryInfo;
                        if (child != null) { pending.Push(child); continue; }
                        long length = ((FileInfo)item).Length;
                        if (length > long.MaxValue - bytes) { partial = true; continue; }
                        bytes += length;
                        files++;
                    }
                } catch (IOException) { partial = true; }
                catch (UnauthorizedAccessException) { partial = true; }
            }
            return files > 0 || !partial ? bytes : (long?)null;
        }
    }
}
