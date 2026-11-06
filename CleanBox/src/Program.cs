using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
namespace CleanBox {
    static class Program {
        [STAThread] static void Main(string[] args) {
            if(args.Length>0&&args[0]=="--self-test") {try {Tests.Run();Environment.ExitCode=0;}catch(Exception e){Directory.CreateDirectory(Store.Data);File.WriteAllText(Path.Combine(Store.Data,"test-results.txt"),"FAIL\r\n"+e);Environment.ExitCode=1;}return;}
            if(args.Length>0&&args[0]=="--scan-report") {Tests.ScanReport();return;}
            if(args.Length==2&&args[0]=="--hold-test") {string p=Safety.Check(args[1],true);if(!Safety.Within(p,Path.Combine(Store.Home,"tests")))return;using(var file=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.None)){File.WriteAllText(p+".ready","ready");Thread.Sleep(20000);}return;}
            Native.Dpi();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException+=(s,e)=>{Store.Log("界面异常："+e.Exception);MessageBox.Show(e.Exception.Message,"清理匣",MessageBoxButtons.OK,MessageBoxIcon.Warning);};
            Application.Run(new MainForm(args.Length==2&&args[0]=="--target"?args[1]:null,args.Length==1&&args[0]=="--ui-check",args.Length==1&&args[0]=="--settings-perf"));
        }
    }
}
