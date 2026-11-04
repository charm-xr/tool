using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CleanBox {
    public class LockOwner {
        public int Pid; public long Started; public string Name="", Path="", Kind=""; public bool CanStop;
        public string Display { get { return Name+"  · PID "+Pid+"  · "+Kind+(CanStop?"":" · 受保护/无法核实"); } }
    }
    public class LockReport { public List<LockOwner> Owners=new List<LockOwner>();public int Files;public bool Partial;public string Note=""; }
    public static class Native {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool MoveFileEx(string existing,string replacement,int flags);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool IsProcessCritical(IntPtr process,out bool critical);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
        public static void Dpi() { try { SetProcessDpiAwareness(1); } catch {try {SetProcessDPIAware();}catch{}} }
        public static bool IsAdmin { get { using(var id=WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } }
        [StructLayout(LayoutKind.Sequential)] struct RM_UNIQUE_PROCESS {public int dwProcessId;public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct RM_PROCESS_INFO {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string strServiceShortName;
            public int ApplicationType; public uint AppStatus; public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }
        [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmStartSession(out uint handle,int flags,StringBuilder key);
        [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint handle);
        [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmRegisterResources(uint handle,uint files,string[] names,uint apps,IntPtr appList,uint services,string[] serviceNames);
        [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint handle,out uint needed,ref uint count,[In,Out] RM_PROCESS_INFO[] info,ref uint reasons);
        public static LockReport FindLocks(string root,CancellationToken token) {
            root=Safety.Normalize(root); if(!File.Exists(root)&&!Directory.Exists(root))throw new IOException("目标不存在。");
            if(Safety.HasReparseAncestor(root))throw new IOException("请定位链接对应的真实目录。");
            var report=new LockReport();var files=new List<string>();
            if(File.Exists(root)) files.Add(root);
            else {var stack=new Stack<string>();stack.Push(root);while(stack.Count>0) {
                token.ThrowIfCancellationRequested();string d=stack.Pop();
                try {if(Safety.HasReparseAncestor(d)){report.Partial=true;continue;}foreach(string f in Directory.EnumerateFiles(d)){files.Add(f);if(files.Count>=10000)break;}if(files.Count>=10000){report.Partial=true;break;}foreach(var sub in Directory.GetDirectories(d))stack.Push(sub);}
                catch {report.Partial=true;}
            }}
            report.Files=files.Count;var owners=new Dictionary<int,LockOwner>();uint session;int rc=RmStartSession(out session,0,new StringBuilder(33));
            if(rc!=0)throw new Win32Exception(rc,"无法开启文件占用检查");
            try {
                for(int offset=0;offset<files.Count;offset+=256) {token.ThrowIfCancellationRequested();string[] batch=files.Skip(offset).Take(256).ToArray();rc=RmRegisterResources(session,(uint)batch.Length,batch,0,IntPtr.Zero,0,null);if(rc!=0){report.Partial=true;report.Note="部分文件未能登记："+new Win32Exception(rc).Message;}}
                uint need=0,count=0,reasons=0;rc=RmGetList(session,out need,ref count,null,ref reasons);
                for(int retry=0;rc==234&&retry<6;retry++) {var data=new RM_PROCESS_INFO[need];count=need;rc=RmGetList(session,out need,ref count,data,ref reasons);if(rc==0)for(int i=0;i<count;i++){
                    var r=data[i];long start=((long)(uint)r.Process.ProcessStartTime.dwHighDateTime<<32)|(uint)r.Process.ProcessStartTime.dwLowDateTime;
                    var owner=Describe(r.Process.dwProcessId);owner.Name=string.IsNullOrWhiteSpace(r.strAppName)?owner.Name:r.strAppName;owner.Kind="文件占用";
                    if(owner.Started!=start)owner.CanStop=false;owner.Started=start;
                    if(r.ApplicationType==3||r.ApplicationType==4||r.ApplicationType==1000) owner.CanStop=false;
                    owners[owner.Pid]=owner;
                }}
                if(rc!=0) { report.Partial=true;report.Note="Windows 返回："+new Win32Exception(rc).Message; }
            } finally {RmEndSession(session);}
            foreach(var p in Process.GetProcesses()) using(p) {token.ThrowIfCancellationRequested();try {string executable=p.MainModule.FileName;if(Safety.Within(executable,root)&&!owners.ContainsKey(p.Id)){var owner=Describe(p.Id);owner.Kind="在此目录运行";owners[p.Id]=owner;}}catch{}}
            report.Owners=owners.Values.OrderBy(x=>x.Pid).ToList();return report;
        }
        static LockOwner Describe(int pid) {
            var owner=new LockOwner{Pid=pid,Name="进程 "+pid};
            try {using(var p=Process.GetProcessById(pid)){owner.Name=p.ProcessName;owner.Path=p.MainModule.FileName;owner.Started=p.StartTime.ToFileTimeUtc();bool critical;
                owner.CanStop=pid!=Process.GetCurrentProcess().Id&&pid>4&&!Safety.IsSystemTree(owner.Path)&&!Safety.Within(owner.Path,Store.Home)&&IsProcessCritical(p.Handle,out critical)&&!critical;
            }} catch {owner.CanStop=false;}return owner;
        }
        public static string Stop(LockOwner owner,bool force) {
            var current=Describe(owner.Pid);if(!owner.CanStop||!current.CanStop||current.Started!=owner.Started||!current.Path.Equals(owner.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("进程身份发生变化或属于受保护进程，请重新检查占用。");
            using(var p=Process.GetProcessById(owner.Pid)) {if(force){p.Kill();p.WaitForExit(3000);Store.Log("用户确认强制结束："+owner.Display);return "已发送结束指令。请重新检查占用。";}
                if(!p.CloseMainWindow())return "该程序没有可关闭的主窗口，请手动退出它，或在核对后选择强制结束。";
                Store.Log("请求正常退出："+owner.Display);return "已请求正常退出。如果程序提示保存文件，请先处理该提示。";
            }
        }
    }
}
