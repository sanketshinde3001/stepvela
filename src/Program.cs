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
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs)
            { MessageBox.Show(eventArgs.Exception.Message, "Recorder error", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            try { Application.Run(new RecorderForm()); return 0; }
            catch (Exception error) { MessageBox.Show(error.Message, "StepVela", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }
}
