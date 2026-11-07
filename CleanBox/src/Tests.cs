using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
namespace CleanBox {
    public static class Tests {
        static List<string> results=new List<string>();
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);results.Add("PASS: "+name);}
        static void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,name);}
        public static void Run(){
            results.Clear();Directory.CreateDirectory(Store.Data);
            string root=Path.Combine(Store.Home,"tests","run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            Check(Safety.Within(root,Path.Combine(Store.Home,"tests")),"Fixture is inside the designated test directory");
            Reject(()=>Safety.Check(@"D:\"),"Drive root protected");
            Reject(()=>Safety.Check(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),"Windows protected");
            Reject(()=>Safety.Check(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),"User profile protected");
            Reject(()=>Safety.Check(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),"Shared application root protected");
            Reject(()=>Safety.Check(Store.Home),"CleanBox protects itself");
            Reject(()=>Safety.Normalize(@"\\server\share\folder"),"UNC / remote targets rejected");
            Reject(()=>Safety.Normalize(@"D:\a.txt:stream"),"Alternate data streams rejected");
            Reject(()=>Safety.Normalize(@"D:\folder.\file"),"Ambiguous trailing-dot path rejected");
            Check(!Safety.Within(@"D:\AppSibling\file",@"D:\App"),"Path prefix collision cannot cross boundary");
            string exe,arg;
            Check(Scanner.ParseCommand("\"D:\\App Name\\uninstall.exe\" /remove",out exe,out arg)&&exe==@"D:\App Name\uninstall.exe"&&arg=="/remove","Quoted uninstall path parsed");
            Check(Scanner.ParseCommand(@"D:\App Name\uninstall.exe /remove",out exe,out arg)&&exe==@"D:\App Name\uninstall.exe","Unquoted path with spaces parsed");
            Check(!Scanner.ParseCommand("https://example.com/remove",out exe,out arg),"Non-program uninstall string rejected");
            Check(Store.QuotePS("a'b")=="'a''b'","PowerShell literal quoting preserves apostrophes");
            Reject(()=>Safety.Check(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory),"pagefile.sys")),"System paging file protected");
            string linkHolder=Path.Combine(root,"link-holder"),destination=Path.Combine(root,"sentinel"),junction=Path.Combine(linkHolder,"jump");Directory.CreateDirectory(linkHolder);Directory.CreateDirectory(destination);File.WriteAllText(Path.Combine(destination,"keep.txt"),"must survive link rejection");
            Store.RunPowerShell("New-Item -ItemType Junction -Path "+Store.QuotePS(junction)+" -Target "+Store.QuotePS(destination)+" | Out-Null",CancellationToken.None);
            File.WriteAllBytes(Path.Combine(linkHolder,"local.bin"),new byte[4096]);
            var partialSize=Native.FindLocks(linkHolder,CancellationToken.None);
            Check(partialSize.SizeBytes==4096&&partialSize.SizePartial,"Directory size excludes linked files and reports the measured subtotal");
            var lookup=new InstalledSizeLookup();
            var missingEstimate=new AppEntry();
            lookup.Fill(missingEstimate,linkHolder,CancellationToken.None);
            Check(missingEstimate.SizeBytes==4096&&missingEstimate.SizePartial&&missingEstimate.SizeKind==AppSizeKind.InstallDirectory,"Missing install estimate falls back to a bounded directory subtotal without following links");
            string launcherDir=Path.Combine(root,"MobileAppEngine","app","com.example");Directory.CreateDirectory(launcherDir);
            var engineApp=new AppEntry{Executable=Path.Combine(launcherDir,"icon.ico"),Command="\""+Path.Combine(launcherDir,"uninstall.exe")+"\""};
            File.WriteAllBytes(engineApp.Executable,new byte[64]);File.WriteAllBytes(Path.Combine(launcherDir,"uninstall.exe"),new byte[32]);
            lookup.Fill(engineApp,"",CancellationToken.None);
            Check(engineApp.SizeBytes==96&&engineApp.SizeKind==AppSizeKind.EngineLauncher&&!engineApp.SizePartial,"Matching icon and uninstall paths identify engine launcher files without claiming whole-app size");
            Reject(()=>Safety.Plan(linkHolder,true),"Deletion preflight rejects a real directory junction");
            Check(File.Exists(Path.Combine(destination,"keep.txt")),"Junction destination was not changed");Directory.Delete(junction,false);
            string csv=Path.Combine(root,"export.csv");Store.Export(new[]{new AppEntry{Name="=1+1"}},csv);Check(File.ReadAllText(csv).Contains("'=1+1"),"CSV formula-like names are neutralized");
            string folder=Path.Combine(root,"nested"),sub=Path.Combine(folder,"child");Directory.CreateDirectory(sub);string f=Path.Combine(sub,"只读文件.txt");File.WriteAllText(f,"test");File.SetAttributes(f,FileAttributes.ReadOnly);
            var plan=Safety.Plan(folder,true);Check(plan.First()==f&&plan.Last()==folder,"Deletion plan orders children before parent");
            var deleted=Operations.Permanent(folder,true);Check(deleted.Failed.Count==0&&!Directory.Exists(folder),"Read-only Unicode fixture deleted successfully");
            string disk=Path.Combine(root,"scan");Directory.CreateDirectory(Path.Combine(disk,"node_modules"));File.WriteAllText(Path.Combine(disk,"portable.exe"),"fixture");File.WriteAllText(Path.Combine(disk,"node_modules","helper.exe"),"fixture");
            var files=Scanner.WalkFiles(disk,"*.exe",CancellationToken.None,null).ToList();Check(files.Count==1&&files[0].EndsWith("portable.exe"),"Portable EXE discovered; dependency directory excluded");
            Check(Scanner.FromExe(files[0],"磁盘发现").SizeBytes==7,"Disk discovery measures the EXE file rather than its directory");
            Check(FileSizes.FromEstimatedKilobytes(2048)==2097152&&FileSizes.FromEstimatedKilobytes(0)==null,"Installed size converts registered KB and distinguishes unknown size");
            var cancel=new CancellationTokenSource();cancel.Cancel();Reject(()=>Scanner.WalkFiles(disk,"*.exe",cancel.Token,null).ToList(),"Scan cancellation honored");cancel.Dispose();
            string locked=Path.Combine(root,"held.txt");File.WriteAllText(locked,"locked fixture");
            using(var helper=Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"--hold-test \""+locked+"\""){UseShellExecute=false,CreateNoWindow=true})){
                try{for(int i=0;i<100&&!File.Exists(locked+".ready");i++)Thread.Sleep(100);Check(File.Exists(locked+".ready"),"Independent lock fixture started");
                    var report=Native.FindLocks(locked,CancellationToken.None);Check(report.Owners.Any(o=>o.Pid==helper.Id),"Restart Manager identifies actual locking process");
                    Check(report.SizeBytes==14&&!report.SizePartial,"File size is available while a process holds an exclusive lock");
                    var fail=Operations.Permanent(locked,true);Check(fail.Failed.Count==1&&File.Exists(locked),"Locked delete reports failure without claiming success");
                }finally{if(!helper.HasExited){helper.Kill();helper.WaitForExit();}}
            }
            var clean=Operations.Permanent(root,true);Check(clean.Failed.Count==0&&!Directory.Exists(root),"Only test fixture removed after verification");
            results.Add("No installed software, user data, reboot queue or registry entries were modified.");
            File.WriteAllLines(Path.Combine(Store.Data,"test-results.txt"),results,new UTF8Encoding(true));
        }
        public static void ScanReport(){Directory.CreateDirectory(Store.Data);var apps=new List<AppEntry>();var messages=new List<string>();
            try{Scanner.Registered(apps.Add,messages.Add,CancellationToken.None);Scanner.Packages(apps.Add,messages.Add,CancellationToken.None);Scanner.Shortcuts(apps.Add,messages.Add,CancellationToken.None);
                Store.Export(apps,Path.Combine(Store.Data,"initial-scan.csv"));messages.Add("Registry entries: "+apps.Count(a=>a.RegistryPath.Length>0));messages.Add("Appx entries: "+apps.Count(a=>a.Package.Length>0));messages.Add("Shortcut candidates: "+apps.Count(a=>a.Source.StartsWith("快捷")));messages.Add("Read-only scan completed.");
                File.WriteAllLines(Path.Combine(Store.Data,"scan-results.txt"),messages,new UTF8Encoding(true));
            }catch(Exception e){File.WriteAllText(Path.Combine(Store.Data,"scan-results.txt"),"FAIL\n"+e);Environment.ExitCode=1;}
        }
    }
}
