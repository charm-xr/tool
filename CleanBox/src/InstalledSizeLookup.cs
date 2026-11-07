using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CleanBox {
    internal sealed class SizeMeasurement {
        internal long? Bytes;
        internal bool Partial;
        internal int Files;
        internal string Note = "";
    }

    internal sealed class InstalledSizeLookup {
        readonly Stopwatch scanTime = new Stopwatch();
        readonly Dictionary<string, SizeMeasurement> cache = new Dictionary<string, SizeMeasurement>(StringComparer.OrdinalIgnoreCase);

        internal void Fill(AppEntry app, string installLocation, CancellationToken token) {
            app.InstallLocation = installLocation;
            string reason;
            string directory = ResolveDirectory(app, out reason);
            if (directory == null) { app.SizeNote = reason; return; }
            SizeMeasurement size;
            if (!cache.TryGetValue(directory, out size)) {
                if (scanTime.ElapsedMilliseconds >= 4000) {
                    app.SizeNote = "本轮自动计算已达到时间上限，可选择此软件重新计算。";
                    return;
                }
                scanTime.Start();
                try { size = Measure(directory, token, 5000, 200, null); cache[directory] = size; }
                finally { scanTime.Stop(); }
            }
            Apply(app, directory, size);
        }

        internal static void Apply(AppEntry app, string directory, SizeMeasurement size) {
            app.SizeBytes = size.Bytes;
            app.SizePartial = size.Partial;
            var parent = Directory.GetParent(directory);
            app.SizeKind = parent != null && parent.Parent != null
                && parent.Name.Equals("app", StringComparison.OrdinalIgnoreCase)
                && parent.Parent.Name.Equals("MobileAppEngine", StringComparison.OrdinalIgnoreCase)
                ? AppSizeKind.EngineLauncher : AppSizeKind.InstallDirectory;
            app.SizeNote = size.Note + (size.Note.Length > 0 ? "\r\n" : "") + "统计目录：" + directory;
        }

        // Re-resolve from registration evidence; the action path may be inferred from one icon.
        internal static string ResolveDirectory(AppEntry app, out string reason) {
            reason = "未登记可信安装目录，图标与卸载器也未能定位到同一文件夹。";
            if (app.Protected) { reason = "系统组件，未自动计算目录大小。"; return null; }
            string directory = LocalPath(app.InstallLocation);
            if (directory == null || !Directory.Exists(directory)) {
                string command, arguments;
                if (!Scanner.ParseCommand(app.Command, out command, out arguments)) return null;
                string icon = LocalPath(app.Executable), uninstaller = LocalPath(command);
                if (icon == null || uninstaller == null) return null;
                directory = Path.GetDirectoryName(uninstaller);
                if (!string.Equals(directory, Path.GetDirectoryName(icon), StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(icon) || !File.Exists(uninstaller)) return null;
            }
            if (directory.Length < 4 || Safety.IsSharedDirectory(directory) || Safety.IsSystemTree(directory)) {
                reason = "位置属于系统管理区或共享根目录，无法归为单个软件的占用。";
                return null;
            }
            if (Safety.HasReparseAncestor(directory)) { reason = "安装位置经过目录链接，未跨越链接统计。"; return null; }
            return directory;
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
            var result = Measure(root, token, 5000, 200, null);
            partial = result.Partial;
            return result.Bytes;
        }

        internal static SizeMeasurement Measure(string root, CancellationToken token, int maxEntries, int maxMilliseconds, Action<string> progress) {
            token.ThrowIfCancellationRequested();
            var result = new SizeMeasurement();
            if (!Directory.Exists(root) || Safety.HasReparseAncestor(root)) {
                result.Partial = true; result.Note = "目录不存在、不可访问或经过目录链接。"; return result;
            }
            var time = Stopwatch.StartNew();
            long lastProgress = 0, bytes = 0;
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));
            int entries = 0;
            bool limited = false, skipped = false;
            while (pending.Count > 0 && !limited) {
                token.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                try {
                    if (Safety.HasReparseAncestor(directory.FullName)) { skipped = true; continue; }
                    foreach (var item in directory.EnumerateFileSystemInfos()) {
                        token.ThrowIfCancellationRequested();
                        if (++entries > maxEntries || time.ElapsedMilliseconds >= maxMilliseconds) { limited = true; break; }
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { skipped = true; continue; }
                        var child = item as DirectoryInfo;
                        if (child != null) { pending.Push(child); continue; }
                        long length = ((FileInfo)item).Length;
                        if (length > long.MaxValue - bytes) { skipped = true; continue; }
                        bytes += length; result.Files++;
                        if (progress != null && time.ElapsedMilliseconds - lastProgress >= 120) {
                            lastProgress = time.ElapsedMilliseconds;
                            progress("已统计 " + result.Files + " 个文件 · " + FileSizes.Format(bytes));
                        }
                    }
                } catch (IOException) { skipped = true; }
                catch (UnauthorizedAccessException) { skipped = true; }
                catch (System.Security.SecurityException) { skipped = true; }
                if (time.ElapsedMilliseconds >= maxMilliseconds && pending.Count > 0) limited = true;
            }
            result.Partial = limited || skipped;
            result.Bytes = result.Files > 0 || !result.Partial ? bytes : (long?)null;
            result.Note = limited ? "达到本次计算上限，保留已统计的部分。" : "";
            if (skipped) result.Note += "部分文件无权限、已变化或属于链接，未计入。";
            if (!result.Partial) result.Note = "共 " + result.Files + " 个文件；统计文件长度，不含目录外的数据。";
            return result;
        }
    }
}
