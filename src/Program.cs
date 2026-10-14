using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace StepVela
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                string folder = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-output");
                try { SelfTest.Run(folder); return 0; }
                catch (Exception error)
                {
                    try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "self-test-results.txt"), "FAIL\r\n" + error, Encoding.UTF8); } catch { }
                    return 1;
                }
            }
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool created;string identity=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            using(Mutex instance=new Mutex(true,"Local\\StepVela-"+identity,out created)) {
                if(!created) {
                    foreach(Process process in Process.GetProcessesByName("StepVela"))using(process)try{if(process.Id!=Process.GetCurrentProcess().Id&&process.MainWindowHandle!=IntPtr.Zero){Native.ShowWindow(process.MainWindowHandle,9);Native.SetForegroundWindow(process.MainWindowHandle);break;}}catch{}
                    return 0;
                }
                RecorderForm form=null;
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs) {
                    if(form!=null)form.FailSafeStop(); MessageBox.Show(eventArgs.Exception.Message,"StepVela",MessageBoxButtons.OK,MessageBoxIcon.Error);
                };
                try { form=new RecorderForm(); Application.Run(form); return 0; }
                catch (Exception error) { if(form!=null)form.FailSafeStop();MessageBox.Show(error.Message,"StepVela",MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
                finally { instance.ReleaseMutex(); }
            }
        }
    }
}
