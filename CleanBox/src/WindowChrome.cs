using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CleanBox {
    internal static class WindowChrome {
        // Documented Windows 11 DWM attributes; older systems keep their native frame.
        [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        [DllImport("dwmapi.dll")]static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)]out bool enabled);
        internal static string Diagnostic="Not applied";
        internal static bool CompositionEnabled{get{bool enabled;return DwmIsCompositionEnabled(out enabled)==0&&enabled;}}
        internal static int ColorValue(Color c){return c.R|(c.G<<8)|(c.B<<16);}
        internal static bool Apply(IntPtr window){
            try{
                int dark=Palette.Dark&&!SystemInformation.HighContrast?1:0;
                DwmSetWindowAttribute(window,20,ref dark,4);
                int caption=SystemInformation.HighContrast?-1:ColorValue(Palette.WindowTop),text=SystemInformation.HighContrast?-1:ColorValue(Palette.Ink),border=caption;
                int result=DwmSetWindowAttribute(window,35,ref caption,4);
                int textResult=DwmSetWindowAttribute(window,36,ref text,4),borderResult=DwmSetWindowAttribute(window,34,ref border,4);
                Diagnostic="Composition="+CompositionEnabled+"; caption=0x"+result.ToString("X8")+"; text=0x"+textResult.ToString("X8")+"; border=0x"+borderResult.ToString("X8");
                return result==0&&textResult==0&&borderResult==0;
            }catch(DllNotFoundException){return false;}catch(EntryPointNotFoundException){return false;}
        }
    }
    public partial class MainForm {
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);WindowChrome.Apply(Handle);}
    }
}
