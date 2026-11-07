using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CleanBox {
    internal sealed class UsageNode {
        internal string Path, Name;
        internal bool Directory, Partial, Link;
        internal long Bytes;
        internal UsageNode Parent;
        internal readonly List<UsageNode> Children = new List<UsageNode>();
        internal string SizeText { get { return Link ? "已跳过" : (Partial ? "至少 " : "") + FileSizes.Format(Bytes); } }
    }
    internal sealed class UsageResult {
        internal UsageNode Root;
        internal List<UsageNode> Largest;
        internal int Files, Skipped;
        internal bool Canceled, Limited;
    }
    internal static class DiskUsage {
        internal static UsageResult Scan(string input, CancellationToken token, Action<string> progress, int maxEntries = 300000) {
            string path = Safety.Normalize(input);
            if (path.Length == 2) path += "\\";
            var drive = new DriveInfo(System.IO.Path.GetPathRoot(path));
            if (drive.DriveType != DriveType.Fixed) throw new IOException("请选择本机固定磁盘上的文件夹。");
            if (!System.IO.Directory.Exists(path) || Safety.HasReparseAncestor(path)) throw new IOException("目录不存在、无法访问或经过目录链接。");
            var root = new UsageNode { Path = path, Name = path, Directory = true, Partial = true };
            var result = new UsageResult { Root = root };
            var nodes = new List<UsageNode> { root };
            var pending = new Stack<UsageNode>(); pending.Push(root);
            var clock = Stopwatch.StartNew(); long reported = 0;
            while (pending.Count > 0 && !result.Limited && !token.IsCancellationRequested) {
                var current = pending.Pop();
                try {
                    if (Safety.HasReparseAncestor(current.Path)) { current.Link = true; result.Skipped++; continue; }
                    current.Partial = false;
                    foreach (var item in new DirectoryInfo(current.Path).EnumerateFileSystemInfos()) {
                        if (token.IsCancellationRequested) { current.Partial = true; break; }
                        if (nodes.Count >= maxEntries) { current.Partial = true; result.Limited = true; break; }
                        try {
                            var attributes = item.Attributes;
                            bool directory = (attributes & FileAttributes.Directory) != 0;
                            var child = new UsageNode { Path = item.FullName, Name = item.Name, Parent = current, Directory = directory,
                                Link = (attributes & FileAttributes.ReparsePoint) != 0, Partial = directory };
                            current.Children.Add(child); nodes.Add(child);
                            if (child.Link) { child.Partial = true; result.Skipped++; }
                            else if (directory) pending.Push(child);
                            else {
                                try { child.Bytes = ((FileInfo)item).Length; result.Files++; }
                                catch (IOException) { child.Partial = true; result.Skipped++; }
                                catch (UnauthorizedAccessException) { child.Partial = true; result.Skipped++; }
                            }
                        } catch (IOException) { current.Partial = true; result.Skipped++; }
                        catch (UnauthorizedAccessException) { current.Partial = true; result.Skipped++; }
                        if (progress != null && clock.ElapsedMilliseconds - reported >= 160) {
                            reported = clock.ElapsedMilliseconds; progress("已发现 " + nodes.Count + " 项 · " + result.Files + " 个文件");
                        }
                    }
                } catch (IOException) { current.Partial = true; result.Skipped++; }
                catch (UnauthorizedAccessException) { current.Partial = true; result.Skipped++; }
                catch (System.Security.SecurityException) { current.Partial = true; result.Skipped++; }
            }
            result.Canceled = token.IsCancellationRequested;
            // Every child was appended after its parent, so reverse order aggregates each tree once.
            for (int i = nodes.Count - 1; i > 0; i--) {
                var node = nodes[i];
                if (node.Bytes <= long.MaxValue - node.Parent.Bytes) node.Parent.Bytes += node.Bytes;
                else node.Parent.Partial = true;
                node.Parent.Partial |= node.Partial;
            }
            root.Partial |= result.Canceled || result.Limited;
            result.Largest = nodes.Where(n => !n.Directory && !n.Link && !n.Partial).OrderByDescending(n => n.Bytes).ThenBy(n => n.Path, StringComparer.OrdinalIgnoreCase).Take(200).ToList();
            return result;
        }
    }
}
