using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CleanBox {
    public class AppEntry {
        public string Name = "", Version = "", Publisher = "", Source = "", Path = "", Executable = "", Command = "", Package = "", RegistryPath = "";
        public RegistryHive Hive; public RegistryView View; public bool Protected;
        public string Id { get { return Package.Length > 0 ? "appx:" + Package : RegistryPath.Length > 0 ? Hive + ":" + View + ":" + RegistryPath : "exe:" + Executable.ToLowerInvariant(); } }
    }
    public static class Store {
        public static readonly string Home = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static readonly string Data = System.IO.Path.Combine(Home, "data");
        static readonly object gate = new object();
        public static event Action<string> Logged;
        public static void Log(string s) {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s;
            lock(gate) { try { Directory.CreateDirectory(Data); File.AppendAllText(System.IO.Path.Combine(Data, "operations.log"), line + Environment.NewLine, Encoding.UTF8); } catch {} }
            var h = Logged; if(h != null) h(line);
        }
        public static string RunPowerShell(string script, CancellationToken token, int timeout = 90000) {
            string exe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(exe, "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { " + script + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }")));
            psi.UseShellExecute=false; psi.CreateNoWindow=true; psi.RedirectStandardOutput=true; psi.RedirectStandardError=true; psi.StandardOutputEncoding=Encoding.UTF8; psi.StandardErrorEncoding=Encoding.UTF8;
            using(var p=Process.Start(psi)) {
                Task<string> output=p.StandardOutput.ReadToEndAsync(), errors=p.StandardError.ReadToEndAsync(); var clock=Stopwatch.StartNew();
                while(!p.WaitForExit(150)) { if(token.IsCancellationRequested || clock.ElapsedMilliseconds > timeout) { try { p.Kill(); } catch {} token.ThrowIfCancellationRequested(); throw new TimeoutException("Windows 应用查询超时，请重试。"); } }
                Task.WaitAll(output,errors); if(p.ExitCode!=0) throw new IOException(errors.Result.Trim()); return output.Result;
            }
        }
        public static string QuotePS(string s) { return "'"+s.Replace("'","''")+"'"; }
        public static void Export(IEnumerable<AppEntry> apps,string file) {
            Func<string,string> q=s=>{s=s??"";if(s.Length>0&&"=+-@\t\r\n".IndexOf(s[0])>=0)s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";};
            var lines=new List<string>{"名称,版本,发布者,来源,安装位置,程序路径"};
            foreach(var a in apps) lines.Add(string.Join(",",new[]{a.Name,a.Version,a.Publisher,a.Source,a.Path,a.Executable}.Select(q)));
            File.WriteAllLines(file,lines, new UTF8Encoding(true));
        }
    }
    public static class Scanner {
        const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        public static void Registered(Action<AppEntry> add, Action<string> status, CancellationToken token) {
            foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser}) foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
                token.ThrowIfCancellationRequested(); status("读取 " + hive + " · " + view);
                try { using(var b=RegistryKey.OpenBaseKey(hive,view)) using(var root=b.OpenSubKey(Uninstall)) {
                    if(root==null) continue;
                    foreach(var key in root.GetSubKeyNames()) { token.ThrowIfCancellationRequested(); try { using(var k=root.OpenSubKey(key)) {
                        if(k==null) continue; string name=V(k,"DisplayName"); if(string.IsNullOrWhiteSpace(name)) continue;
                        var a=new AppEntry{Name=name,Version=V(k,"DisplayVersion"),Publisher=V(k,"Publisher"),Source=(hive==RegistryHive.LocalMachine?"已安装 · 全机":"已安装 · 当前用户")+(view==RegistryView.Registry64?" · 64位":" · 32位"),Command=V(k,"UninstallString"),Hive=hive,View=view,RegistryPath=Uninstall+"\\"+key,Protected=V(k,"SystemComponent")=="1"};
                        a.Path=CleanPath(V(k,"InstallLocation")); a.Executable=IconPath(V(k,"DisplayIcon"));
                        if(a.Path.Length==0 && File.Exists(a.Executable)) a.Path=System.IO.Path.GetDirectoryName(a.Executable);
                        if(a.Path.Length==0) { string exe,args; if(ParseCommand(a.Command,out exe,out args) && System.IO.Path.IsPathRooted(exe) && File.Exists(exe)) a.Path=System.IO.Path.GetDirectoryName(exe); }
                        add(a);
                    }} catch(Exception e) { Store.Log("跳过安装记录 " + key + "："+e.Message); } }
                }} catch(Exception e) { Store.Log("注册表读取失败："+e.Message); }
            }
        }
        static string V(RegistryKey k,string name) { return Convert.ToString(k.GetValue(name,"")); }
        public static string CleanPath(string p) { try { p=Environment.ExpandEnvironmentVariables((p??"").Trim().Trim('"')); return string.IsNullOrWhiteSpace(p)?"":System.IO.Path.GetFullPath(p).TrimEnd('\\'); } catch { return ""; } }
        static string IconPath(string p) { return CleanPath(Regex.Replace(p??"",@",\s*-?\d+\s*$", "")); }
        public static bool ParseCommand(string command,out string exe,out string args) {
            exe="";args=""; string s=Environment.ExpandEnvironmentVariables((command??"").Trim());
            if(s.StartsWith("\"")) { int end=s.IndexOf('"',1); if(end<2) return false; exe=s.Substring(1,end-1);args=s.Substring(end+1).Trim(); }
            else { var m=Regex.Match(s,@"^(.+?\.exe)(?:\s+(.*))?$",RegexOptions.IgnoreCase); if(!m.Success) return false; exe=m.Groups[1].Value;args=m.Groups[2].Value; }
            return exe.EndsWith(".exe",StringComparison.OrdinalIgnoreCase);
        }
        public static void Packages(Action<AppEntry> add, Action<string> status, CancellationToken token) {
            status("读取当前用户的 Microsoft Store / MSIX 应用…");
            string json=Store.RunPowerShell("@(Get-AppxPackage | Select-Object Name,Version,Publisher,InstallLocation,PackageFullName,IsFramework,NonRemovable) | ConvertTo-Json -Compress",token);
            if(string.IsNullOrWhiteSpace(json)) return;
            var ser=new JavaScriptSerializer{MaxJsonLength=16777216}; var obj=ser.DeserializeObject(json); var list=obj as object[] ?? new[]{obj};
            foreach(var item in list) { token.ThrowIfCancellationRequested(); var d=item as Dictionary<string,object>; if(d==null) continue; Func<string,string> v=k=>d.ContainsKey(k)?Convert.ToString(d[k]):"";
                add(new AppEntry{Name=v("Name"),Version=v("Version"),Publisher=v("Publisher"),Path=v("InstallLocation"),Package=v("PackageFullName"),Source="商店 / MSIX · 当前用户",Protected=v("IsFramework")=="True"||v("NonRemovable")=="True"});
            }
        }
        public static void Shortcuts(Action<AppEntry> add,Action<string> status,CancellationToken token) {
            status("检查开始菜单和桌面快捷方式…");
            var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)};
            Type t=Type.GetTypeFromProgID("WScript.Shell"); if(t==null) return; dynamic shell=Activator.CreateInstance(t);
            try { foreach(string root in roots) foreach(string file in WalkFiles(root,"*.lnk",token,null,12)) {
                object shortcut=null; try { shortcut=shell.CreateShortcut(file); dynamic l=shortcut; string p=Convert.ToString(l.TargetPath);
                    if(!p.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(p)||Safety.IsSystemTree(p)) continue;
                    add(FromExe(p,"快捷方式 · 待核对",System.IO.Path.GetFileNameWithoutExtension(file)));
                } catch {} finally { if(shortcut!=null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
            }} finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        public static AppEntry FromExe(string p,string source,string title=null) {
            var a=new AppEntry{Executable=p,Path=System.IO.Path.GetDirectoryName(p),Source=source,Name=title??System.IO.Path.GetFileNameWithoutExtension(p)};
            try { var v=FileVersionInfo.GetVersionInfo(p); if(title==null&&!string.IsNullOrWhiteSpace(v.ProductName)) a.Name=v.ProductName.Trim(); a.Version=v.ProductVersion??"";a.Publisher=v.CompanyName??""; } catch {}
            return a;
        }
        public static IEnumerable<string> WalkFiles(string root,string pattern,CancellationToken token,Action<string> status,int maxDepth=int.MaxValue) {
            var pending=new Stack<Tuple<string,int>>(); pending.Push(Tuple.Create(root,0));
            while(pending.Count>0) { token.ThrowIfCancellationRequested(); var node=pending.Pop(); string dir=node.Item1;
                string[] files=new string[0],dirs=new string[0];
                try { if(!Directory.Exists(dir)||Safety.HasReparseAncestor(dir)||Safety.IsSystemTree(dir)) continue; files=Directory.GetFiles(dir,pattern); if(node.Item2<maxDepth) dirs=Directory.GetDirectories(dir); }
                catch(UnauthorizedAccessException) { if(status!=null) status("无权限，已跳过："+dir); Store.Log("扫描跳过（无权限）："+dir); continue; }
                catch(IOException e) { Store.Log("扫描跳过："+dir+"；"+e.Message); continue; }
                if(status!=null) status("扫描："+dir);
                foreach(var f in files) { token.ThrowIfCancellationRequested(); yield return f; }
                foreach(var d in dirs) { string n=System.IO.Path.GetFileName(d); if(n==".git"||n=="node_modules"||n==".venv"||n=="__pycache__"||n=="$RECYCLE.BIN"||n=="System Volume Information") continue; pending.Push(Tuple.Create(d,node.Item2+1)); }
            }
        }
        public static void Disk(string root,Action<AppEntry> add,Action<string> status,CancellationToken token) {
            foreach(string exe in WalkFiles(root,"*.exe",token,status)) {
                if(Safety.Within(exe,Store.Home)) continue;
                string name=System.IO.Path.GetFileNameWithoutExtension(exe);
                if(Regex.IsMatch(name,@"^(unins\d*|uninstall|setup|update|updater|crashpad_handler|crashreporter|vcredist.*|vc_redist.*)$",RegexOptions.IgnoreCase)) continue;
                add(FromExe(exe,"磁盘发现 · 待核对"));
            }
        }
    }
    public static class Safety {
        public static string Normalize(string p) {
            if(string.IsNullOrWhiteSpace(p)||!Regex.IsMatch(p,@"^[A-Za-z]:[\\/]")) throw new IOException("请选择本机磁盘上的绝对路径。");
            if(p.Substring(2).Contains(":")) throw new IOException("不支持设备路径或文件数据流。");
            foreach(var part in p.Replace('/','\\').Substring(2).Split('\\')) if(part.EndsWith(".")||part.EndsWith(" ")||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)) throw new IOException("路径包含不明确或保留的名称，无法安全处理。");
            string n=System.IO.Path.GetFullPath(p).TrimEnd('\\');
            foreach(var part in n.Substring(2).Split('\\')) if(part.EndsWith(".")||part.EndsWith(" ")) throw new IOException("路径尾部有空格或点，无法安全处理。");
            return n;
        }
        public static bool Within(string p,string parent) { if(string.IsNullOrEmpty(p)||string.IsNullOrEmpty(parent)) return false; p=p.TrimEnd('\\');parent=parent.TrimEnd('\\');return p.Equals(parent,StringComparison.OrdinalIgnoreCase)||p.StartsWith(parent+"\\",StringComparison.OrdinalIgnoreCase); }
        public static bool HasReparseAncestor(string p) {
            try { for(string n=System.IO.Path.GetFullPath(p);!string.IsNullOrEmpty(n);n=System.IO.Path.GetDirectoryName(n)) if((Directory.Exists(n)||File.Exists(n))&&(File.GetAttributes(n)&FileAttributes.ReparsePoint)!=0) return true; }
            catch { return true; } return false;
        }
        public static bool IsSystemTree(string p) {
            string win=Environment.GetFolderPath(Environment.SpecialFolder.Windows), pd=Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var roots=new[]{win,System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Windows Defender"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Windows NT"),System.IO.Path.Combine(pd,"Microsoft"),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages")};
            if(roots.Any(r=>Within(p,r))) return true;
            return Regex.IsMatch(p,@"^[A-Za-z]:\\(\$RECYCLE\.BIN|System Volume Information|Recovery|Boot|Config\.Msi)(\\|$)",RegexOptions.IgnoreCase);
        }
        public static string Check(string input,bool allowTest=false) {
            string p=Normalize(input); if(p.Length<4||System.IO.Path.GetDirectoryName(p)==null) throw new IOException("禁止删除磁盘根目录。");
            if(IsSystemTree(p)||Regex.IsMatch(p,@"^[A-Za-z]:\\(pagefile\.sys|swapfile\.sys|hiberfil\.sys|bootmgr|BOOTNXT|DumpStack\.log(\.tmp)?)$",RegexOptions.IgnoreCase)) throw new IOException("这是受保护的系统位置。商店应用请使用正常卸载。");
            string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var shared=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),user,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),System.IO.Path.Combine(user,"Downloads"),System.IO.Path.Combine(user,"AppData"),System.IO.Path.GetDirectoryName(user)};
            if(shared.Where(s=>!string.IsNullOrEmpty(s)).Any(s=>Within(s,p))||new[]{"Program Files","Program Files (x86)","ProgramData","Users","Windows Kits","WindowsApps","Programs","Common Files"}.Contains(System.IO.Path.GetFileName(p),StringComparer.OrdinalIgnoreCase)) throw new IOException("这是共享目录或用户资料根目录，请选择更具体的软件文件夹。");
            if((Within(Store.Home,p)||Within(p,Store.Home))&&!(allowTest&&Within(p,System.IO.Path.Combine(Store.Home,"tests")))) throw new IOException("不能删除清理匣自身及其父目录。");
            if(HasReparseAncestor(p)) throw new IOException("目标或父目录是链接/联接点。请定位真实目录后再处理。");
            if(!Directory.Exists(p)&&!File.Exists(p)) throw new FileNotFoundException("目标已经不存在。",p);
            return p;
        }
        public static List<string> Plan(string root,bool test=false) {
            root=Check(root,test); var result=new List<string>(); var stack=new Stack<string>();stack.Push(root);
            while(stack.Count>0) { string p=stack.Pop(); if(!Within(p,root)||HasReparseAncestor(p)) throw new IOException("检测到越界或目录链接，操作停止："+p);
                result.Add(p); if(Directory.Exists(p)) foreach(string child in Directory.GetFileSystemEntries(p)) { if(IsSystemTree(child)) throw new IOException("目录内包含受保护项目，操作停止。"); stack.Push(child); }
            }
            return result.OrderByDescending(s=>s.Length).ToList();
        }
    }
    public class DeleteResult { public int Done; public List<string> Failed=new List<string>(); public string Summary { get { return "成功 "+Done+" 项；失败 "+Failed.Count+" 项"; } } }
    public static class Operations {
        public static DeleteResult Permanent(string root,bool test=false) {
            root=Safety.Check(root,test); var plan=Safety.Plan(root,test); var r=new DeleteResult();
            foreach(string p in plan) try {
                if(!Safety.Within(p,root)||Safety.HasReparseAncestor(p)) throw new IOException("路径边界已变化");
                if(!File.Exists(p)&&!Directory.Exists(p)) continue;
                File.SetAttributes(p,File.GetAttributes(p)&~FileAttributes.ReadOnly);
                if(Directory.Exists(p)) Directory.Delete(p,false);else File.Delete(p);r.Done++;
            } catch(Exception e) { r.Failed.Add(p+"："+e.Message); }
            Store.Log("永久删除 "+root+"；"+r.Summary+"\n"+string.Join("\n",r.Failed));return r;
        }
        public static DeleteResult Reboot(string root) {
            if(!Native.IsAdmin) throw new IOException("重启后删除需要管理员权限，请点击右上角的管理员模式。");
            root=Safety.Check(root);var plan=Safety.Plan(root);var r=new DeleteResult();
            foreach(string p in plan) {
                if(Safety.HasReparseAncestor(p)) { r.Failed.Add(p+"：路径已变化"); continue; }
                if(Native.MoveFileEx(p,null,4)) {r.Done++;Store.Log("已登记重启后永久删除："+p);} else r.Failed.Add(p+"："+new System.ComponentModel.Win32Exception().Message);
            }
            Store.Log("重启删除登记 "+root+"；"+r.Summary);return r;
        }
        public static void Recycle(string root) {
            root=Safety.Check(root);Safety.Plan(root); // Preflight rejects links and inaccessible subtrees before handing to the shell.
            if(Directory.Exists(root)) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(root,Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(root,Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            Store.Log("移至回收站："+root);
        }
        public static void Uninstall(AppEntry a) {
            if(a.Protected) throw new IOException("此项被 Windows 标记为系统组件，本工具不卸载它。");
            if(a.Package.Length>0) { Store.RunPowerShell("Remove-AppxPackage -Package "+Store.QuotePS(a.Package)+" -ErrorAction Stop",CancellationToken.None,180000);Store.Log("已卸载当前用户的商店应用："+a.Name); return; }
            string exe,args; if(!Scanner.ParseCommand(a.Command,out exe,out args)) throw new IOException("没有可识别的卸载入口。可打开安装目录核对，或使用文件处理功能。");
            if(System.IO.Path.GetFileName(exe).Equals("msiexec.exe",StringComparison.OrdinalIgnoreCase)) { var m=Regex.Match(args,@"\{[0-9a-fA-F-]{36}\}"); if(!m.Success) throw new IOException("无法识别 MSI 产品编号。");exe=System.IO.Path.Combine(Environment.SystemDirectory,"msiexec.exe");args="/x "+m.Value; }
            else { if(!System.IO.Path.IsPathRooted(exe)||!File.Exists(exe)) throw new IOException("卸载程序不存在："+exe); if(new[]{"cmd.exe","powershell.exe","pwsh.exe","wscript.exe","cscript.exe","rundll32.exe"}.Contains(System.IO.Path.GetFileName(exe),StringComparer.OrdinalIgnoreCase)) throw new IOException("卸载入口是脚本或系统命令，请从 Windows 设置中核对处理。"); }
            Process.Start(new ProcessStartInfo(exe,args){UseShellExecute=true,WorkingDirectory=System.IO.Path.GetDirectoryName(exe)});Store.Log("已启动卸载器（完成状态由原卸载器决定）："+a.Name);
        }
        public static List<string> Residuals(AppEntry a) {
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase); Action<string> add=p=>{try { if(Directory.Exists(p)||File.Exists(p)) paths.Add(Safety.Check(p)); }catch{} };
            if(a.Path.Length>0)add(a.Path);
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase); if(a.Name.Length>=4&&a.Name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())<0) names.Add(a.Name);
            if(a.Path.Length>0) { string name=System.IO.Path.GetFileName(a.Path);if(name.Length>=4)names.Add(name); }
            foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}) foreach(string name in names) add(System.IO.Path.Combine(root,name));
            return paths.OrderBy(x=>x).ToList();
        }
        public static void RemoveStaleRecord(AppEntry a) {
            if(a.RegistryPath.Length==0||!a.RegistryPath.StartsWith(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\",StringComparison.OrdinalIgnoreCase)) throw new IOException("不是可清理的卸载记录。");
            if(a.Protected) throw new IOException("不能清理系统组件记录。");
            if(a.Path.Length>0&&Directory.Exists(a.Path)) throw new IOException("安装目录仍然存在，请先正常卸载并核对。");
            string exe,args; if(Scanner.ParseCommand(a.Command,out exe,out args)) { if(System.IO.Path.GetFileName(exe).Equals("msiexec.exe",StringComparison.OrdinalIgnoreCase)) throw new IOException("MSI 记录应由 Windows Installer 管理。");if(!System.IO.Path.IsPathRooted(exe)||File.Exists(exe))throw new IOException("卸载程序可能仍然有效，不能作为失效记录删除。"); }
            Directory.CreateDirectory(Store.Data); string backup=System.IO.Path.Combine(Store.Data,"registry-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".reg");
            string root=a.Hive==RegistryHive.LocalMachine?"HKLM":"HKCU";
            var psi=new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,"reg.exe"),"export \""+root+"\\"+a.RegistryPath+"\" \""+backup+"\" /y /reg:"+(a.View==RegistryView.Registry64?"64":"32")){UseShellExecute=false,CreateNoWindow=true};
            using(var p=Process.Start(psi)){p.WaitForExit();if(p.ExitCode!=0||!File.Exists(backup))throw new IOException("注册表备份失败，未删除记录。");}
            using(var b=RegistryKey.OpenBaseKey(a.Hive,a.View)) {
                using(var key=b.OpenSubKey(a.RegistryPath)) {
                    if(key==null) throw new IOException("卸载记录已经不存在。");
                    if(Convert.ToString(key.GetValue("DisplayName",""))!=a.Name||Convert.ToString(key.GetValue("UninstallString",""))!=a.Command||Convert.ToString(key.GetValue("SystemComponent",""))=="1")throw new IOException("卸载记录已经变化，请重新扫描。");
                }
                b.DeleteSubKeyTree(a.RegistryPath,false);
            }
            Store.Log("清理失效卸载记录："+a.Name+"；备份："+backup);
        }
    }
}
