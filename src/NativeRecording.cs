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
    enum RecordingState { Ready, Recording, Paused, Stopped }

    sealed class RecordingSession
    {
        public RecordingState State { get; private set; }
        public RecordingSession() { State = RecordingState.Ready; }
        public void Start()
        {
            if (State != RecordingState.Ready && State != RecordingState.Stopped)
                throw new InvalidOperationException("Stop the current recording before starting another.");
            State = RecordingState.Recording;
        }
        public void Pause()
        {
            if (State != RecordingState.Recording) throw new InvalidOperationException("Recording is not active.");
            State = RecordingState.Paused;
        }
        public void Resume()
        {
            if (State != RecordingState.Paused) throw new InvalidOperationException("Recording is not paused.");
            State = RecordingState.Recording;
        }
        public void Stop()
        {
            if (State != RecordingState.Recording && State != RecordingState.Paused)
                throw new InvalidOperationException("There is no active recording.");
            State = RecordingState.Stopped;
        }
    }

    [DataContract]
    sealed class ScreenBounds
    {
        [DataMember(Order = 0)] public int X;
        [DataMember(Order = 1)] public int Y;
        [DataMember(Order = 2)] public int Width;
        [DataMember(Order = 3)] public int Height;
        public ScreenBounds() { }
        public ScreenBounds(Rectangle r) { X = r.X; Y = r.Y; Width = r.Width; Height = r.Height; }
        public Rectangle Rectangle { get { return new Rectangle(X, Y, Width, Height); } }
    }

    [DataContract]
    sealed class Step
    {
        [DataMember(Order = 0)] public int Number;
        [DataMember(Order = 1)] public string Timestamp;
        [DataMember(Order = 2)] public string Action;
        [DataMember(Order = 3)] public string Description;
        [DataMember(Order = 4)] public string WindowTitle;
        [DataMember(Order = 5)] public string ProcessName;
        [DataMember(Order = 6)] public string ControlName;
        [DataMember(Order = 7)] public string ControlType;
        [DataMember(Order = 8)] public int? ClickX;
        [DataMember(Order = 9)] public int? ClickY;
        [DataMember(Order = 10)] public string Comment;
        [DataMember(Order = 11)] public ScreenBounds Highlight;
        [DataMember(Order = 12)] public ScreenBounds DesktopBounds;
        [DataMember(Order = 13)] public string Screenshot;
        [DataMember(Order = 14)] public bool ScreenshotOmitted;
        [DataMember(Order = 15)] public string CaptureWarning;
        [IgnoreDataMember] public string LocalScreenshot;
    }

    [DataContract]
    sealed class Report
    {
        [DataMember(Order = 0)] public string Format = "StepVela/1";
        [DataMember(Order = 1)] public string GeneratedAt;
        [DataMember(Order = 2)] public string Title = "Steps recording";
        [DataMember(Order = 3)] public int ScreenshotLimit;
        [DataMember(Order = 4)] public bool ScreenshotsEnabled;
        [DataMember(Order = 5)] public List<Step> Steps;
        [DataMember(Order = 6)] public string SessionStartedAt;
        [DataMember(Order = 7)] public string SessionStoppedAt;
        [DataMember(Order = 8)] public int SkippedEventCount;
    }

    sealed class CaptureRequest
    {
        public Point Point;
        public bool Keyboard;
        public IntPtr Window;
        public uint ProcessId;
        public string Action;
        public DateTimeOffset Time;
        public string WindowTitle;
        public string ProcessName;
        public string Folder;
        public bool ScreenshotEnabled;
        public bool ActiveWindowOnly;
    }

    static class Native
    {
        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const uint GA_ROOT = 2;
        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MouseData
        { public POINT Point; public uint MouseDataValue; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct KeyboardData
        { public uint VirtualKey; public uint ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string module);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError=true)] public static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int swidth, int sheight, uint operation);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);

        public static string WindowText(IntPtr hwnd)
        {
            StringBuilder value = new StringBuilder(1024);
            GetWindowText(hwnd, value, value.Capacity);
            return value.ToString();
        }
    }

    static class ScreenCapture
    {
        // Only one UI Automation operation may be outstanding. A hung provider must
        // never create an unbounded collection of threads or hold the mouse hook.
        static int automationBusy;
        public static Step MakeStep(CaptureRequest request)
        {
            if (request.Window != IntPtr.Zero) request.WindowTitle = Native.WindowText(request.Window);
            if (request.ProcessId != 0)
                try { using (Process process = Process.GetProcessById((int)request.ProcessId)) request.ProcessName = process.ProcessName; } catch { }
            Step step = new Step
            {
                Timestamp = request.Time.ToString("o"), Action = request.Action,
                WindowTitle = request.WindowTitle, ProcessName = request.ProcessName,
                ClickX = request.Keyboard ? (int?)null : request.Point.X, ClickY = request.Keyboard ? (int?)null : request.Point.Y
            };
            if (request.ScreenshotEnabled)
            {
                try
                {
                    Rectangle bounds;
                    using (Bitmap bitmap = Desktop(out bounds, request.ActiveWindowOnly ? request.Window : IntPtr.Zero))
                    {
                        if (!request.Keyboard) DrawMarker(bitmap, new Point((request.Point.X - bounds.X)*bitmap.Width/bounds.Width, (request.Point.Y - bounds.Y)*bitmap.Height/bounds.Height));
                        step.DesktopBounds = new ScreenBounds(bounds);
                        step.LocalScreenshot = Path.Combine(request.Folder, Guid.NewGuid().ToString("N") + ".jpg");
                        SaveJpeg(bitmap, step.LocalScreenshot);
                    }
                }
                catch (Exception error) { step.CaptureWarning = "Screenshot failed: " + error.Message; }
            }
            if (Interlocked.CompareExchange(ref automationBusy, 1, 0) == 0)
            {
                Task<string[]> lookup = Task.Factory.StartNew(delegate
                {
                    try
                    {
                        AutomationElement element = request.Keyboard ? AutomationElement.FocusedElement : AutomationElement.FromPoint(new System.Windows.Point(request.Point.X, request.Point.Y));
                        if (element == null) return new string[2];
                        AutomationElement.AutomationElementInformation current = element.Current;
                        if (request.ProcessId != 0 && current.ProcessId != request.ProcessId) return new string[2];
                        // Never query ValuePattern or TextPattern: typed contents and
                        // passwords are outside this recorder's input scope.
                        string name = current.IsPassword ? "Password field" : current.ControlType == ControlType.Edit ? "Text field" : current.Name;
                        return new string[] { name, current.ControlType == null ? "" : current.ControlType.ProgrammaticName.Replace("ControlType.", "") };
                    }
                    catch { return new string[2]; }
                    finally { Interlocked.Exchange(ref automationBusy, 0); }
                });
                if (lookup.Wait(400))
                {
                    step.ControlName = lookup.Result[0];
                    step.ControlType = lookup.Result[1];
                }
                else step.CaptureWarning = JoinWarning(step.CaptureWarning, "Control details were unavailable within the time limit.");
            }
            else step.CaptureWarning = JoinWarning(step.CaptureWarning, "Control details were skipped because an application accessibility provider is busy.");
            string target = String.IsNullOrWhiteSpace(step.ControlName) ? step.WindowTitle : step.ControlName;
            step.Description = request.Action + (String.IsNullOrWhiteSpace(target) ? " at the recorded position" : " on \"" + target + "\"");
            return step;
        }
        public static Bitmap Desktop(out Rectangle bounds, IntPtr target = default(IntPtr))
        {
            bounds = SystemInformation.VirtualScreen;
            if (target != IntPtr.Zero) {
                Native.RECT rect;
                if(!Native.GetWindowRect(target, out rect)) throw new InvalidOperationException("The recorded window is no longer available.");
                Rectangle selected=Rectangle.Intersect(bounds, Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom));
                if(selected.Width<=0 || selected.Height<=0) throw new InvalidOperationException("The recorded window is outside the visible desktop.");
                bounds=selected;
            }
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("No interactive desktop is available.");
            double scale = Math.Min(1.0, 1600.0 / Math.Max(bounds.Width, bounds.Height));
            Bitmap bitmap = new Bitmap(Math.Max(1,(int)(bounds.Width*scale)), Math.Max(1,(int)(bounds.Height*scale)), PixelFormat.Format24bppRgb);
            try
            {
                IntPtr source=Native.GetDC(IntPtr.Zero);
                if(source==IntPtr.Zero) throw new System.ComponentModel.Win32Exception("Screen capture is unavailable.");
                try {
                    using(Graphics graphics=Graphics.FromImage(bitmap)) {
                        IntPtr destination=graphics.GetHdc();
                        try {
                            Native.SetStretchBltMode(destination,4);
                            if(!Native.StretchBlt(destination,0,0,bitmap.Width,bitmap.Height,source,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x00CC0020))
                                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                        } finally { graphics.ReleaseHdc(destination); }
                    }
                } finally { Native.ReleaseDC(IntPtr.Zero,source); }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
        public static void SaveJpeg(Bitmap bitmap,string path) {
            ImageCodecInfo encoder=ImageCodecInfo.GetImageEncoders().First(e=>e.MimeType=="image/jpeg");
            using(EncoderParameters parameters=new EncoderParameters(1)) {
                parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,82L);
                bitmap.Save(path,encoder,parameters);
            }
        }
        public static void DrawMarker(Bitmap bitmap, Point point)
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen outer = new Pen(Color.White, 6))
            using (Pen inner = new Pen(Color.FromArgb(226, 54, 65), 3))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle circle = new Rectangle(point.X - 14, point.Y - 14, 28, 28);
                graphics.DrawEllipse(outer, circle);
                graphics.DrawEllipse(inner, circle);
                graphics.DrawLine(inner, point.X - 20, point.Y, point.X - 10, point.Y);
                graphics.DrawLine(inner, point.X + 10, point.Y, point.X + 20, point.Y);
                graphics.DrawLine(inner, point.X, point.Y - 20, point.X, point.Y - 10);
                graphics.DrawLine(inner, point.X, point.Y + 10, point.X, point.Y + 20);
            }
        }
        public static string JoinWarning(string first, string second)
        { return String.IsNullOrEmpty(first) ? second : first + " " + second; }
    }

}
