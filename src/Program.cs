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
                    using (Bitmap bitmap = Desktop(out bounds))
                    {
                        if (!request.Keyboard) DrawMarker(bitmap, new Point(request.Point.X - bounds.X, request.Point.Y - bounds.Y));
                        step.DesktopBounds = new ScreenBounds(bounds);
                        step.LocalScreenshot = Path.Combine(request.Folder, Guid.NewGuid().ToString("N") + ".png");
                        bitmap.Save(step.LocalScreenshot, ImageFormat.Png);
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
        public static Bitmap Desktop(out Rectangle bounds)
        {
            bounds = SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("No interactive desktop is available.");
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
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

    static class Exporter
    {
        public static string Escape(string text)
        { return (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;"); }
        public static void ApplyRetention(List<Step> steps, int limit)
        {
            List<Step> captures = steps.Where(s => !String.IsNullOrEmpty(s.LocalScreenshot)).ToList();
            for (int i = 0; i < captures.Count - limit; i++)
            {
                Step step = captures[i];
                if (File.Exists(step.LocalScreenshot)) File.Delete(step.LocalScreenshot);
                step.LocalScreenshot = null;
                step.Screenshot = null;
                step.ScreenshotOmitted = true;
            }
        }
        public static void Save(string destination, List<Step> steps, bool enabled, int limit, DateTimeOffset? started = null, DateTimeOffset? stopped = null, int skipped = 0)
        {
            destination = Path.GetFullPath(destination);
            string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Report report = new Report { GeneratedAt = DateTimeOffset.Now.ToString("o"), ScreenshotLimit = limit, ScreenshotsEnabled = enabled, Steps = steps,
                SessionStartedAt = started.HasValue ? started.Value.ToString("o") : null, SessionStoppedAt = stopped.HasValue ? stopped.Value.ToString("o") : null, SkippedEventCount = skipped };
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (Step step in steps)
                    {
                        step.Screenshot = null;
                        if (!String.IsNullOrEmpty(step.LocalScreenshot) && File.Exists(step.LocalScreenshot))
                        {
                            step.Screenshot = "screenshots/step-" + step.Number.ToString("D4") + ".png";
                            zip.CreateEntryFromFile(step.LocalScreenshot, step.Screenshot, CompressionLevel.Optimal);
                        }
                    }
                    ZipArchiveEntry data = zip.CreateEntry("data.json", CompressionLevel.Optimal);
                    using (Stream json = data.Open()) new DataContractJsonSerializer(typeof(Report)).WriteObject(json, report);
                    ZipArchiveEntry html = zip.CreateEntry("report.html", CompressionLevel.Optimal);
                    using (StreamWriter writer = new StreamWriter(html.Open(), new UTF8Encoding(false))) writer.Write(Html(report));
                    ZipArchiveEntry help = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
                    using (StreamWriter writer = new StreamWriter(help.Open(), new UTF8Encoding(false)))
                        writer.Write("Extract this ZIP, then open report.html in a browser. data.json contains structured step metadata. All assets are local; no internet connection is needed.  Screenshots and accessibility labels may contain private information; review before sharing.\r\n");
                }
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string Html(Report report)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Steps recording</title><style>");
            html.Append("*{box-sizing:border-box}body{margin:0;background:#eef2f7;color:#1b293d;font:16px/1.6 system-ui,-apple-system,Segoe UI,sans-serif}main{max-width:1100px;margin:auto;padding:40px 24px}header{padding:32px;background:#142840;color:white;border-radius:16px;margin-bottom:24px}.eyebrow{font-size:12px;letter-spacing:.16em;text-transform:uppercase;color:#aac5e7}h1{margin:8px 0;font-size:34px}h2{margin:0;font-size:20px}header p{color:#d8e3f1;margin:6px 0}.step{background:white;border:1px solid #dbe2ec;border-radius:12px;margin:18px 0;overflow:hidden}.body{padding:24px}.number{display:inline-block;background:#e8f0ff;color:#265eba;padding:3px 10px;border-radius:6px;margin-right:10px;font-size:14px}.meta{font-size:13px;color:#58677b;margin-top:12px;overflow-wrap:anywhere}.shot{display:block;width:100%;height:auto;border-top:1px solid #dbe2ec}.comment{white-space:pre-wrap;overflow-wrap:anywhere;background:#fff4d4;border-left:4px solid #d49d22;padding:12px 16px;margin-top:18px}.note{color:#68778b;font-size:13px}.warning{color:#8b5300}footer{padding:22px 0;color:#68778b;font-size:13px}@media print{body{background:white}main{padding:0}.step{break-inside:avoid}header{color:#142840;background:white;border:1px solid #ccd5e0}header p,.eyebrow{color:#52637a}}");
            html.Append("</style></head><body><main><header><div class=\"eyebrow\">StepVela · Offline report</div><h1>Steps recording</h1><p>");
            html.Append(report.Steps.Count).Append(" steps · Generated ").Append(Escape(report.GeneratedAt));
            html.Append("</p><p>Mouse actions, keyboard events, window details, screenshots, and comments.</p>");
            if (report.SessionStartedAt != null) html.Append("<p>Recording: ").Append(Escape(report.SessionStartedAt)).Append(" → ").Append(Escape(report.SessionStoppedAt)).Append("</p>");
            if (report.SkippedEventCount > 0) html.Append("<p>").Append(report.SkippedEventCount).Append(" input events were skipped because the recorder was busy. This report may be incomplete.</p>");
            html.Append("</header>");
            foreach (Step step in report.Steps)
            {
                html.Append("<article class=\"step\"><div class=\"body\"><h2><span class=\"number\">Step ").Append(step.Number).Append("</span>");
                html.Append(Escape(step.Description)).Append("</h2><div class=\"meta\">").Append(Escape(step.Timestamp));
                if (!String.IsNullOrEmpty(step.ProcessName)) html.Append(" · App: ").Append(Escape(step.ProcessName));
                if (!String.IsNullOrEmpty(step.WindowTitle)) html.Append("<br>Window: ").Append(Escape(step.WindowTitle));
                if (!String.IsNullOrEmpty(step.ControlType)) html.Append("<br>Control: ").Append(Escape(step.ControlType));
                if (step.ClickX.HasValue) html.Append(" · Position: (").Append(step.ClickX.Value).Append(", ").Append(step.ClickY.Value).Append(")");
                if (step.Highlight != null) html.Append("<br>Highlight: x=").Append(step.Highlight.X).Append(", y=").Append(step.Highlight.Y).Append(", width=").Append(step.Highlight.Width).Append(", height=").Append(step.Highlight.Height);
                html.Append("</div>");
                if (!String.IsNullOrEmpty(step.Comment)) html.Append("<div class=\"comment\">").Append(Escape(step.Comment)).Append("</div>");
                if (step.ScreenshotOmitted) html.Append("<p class=\"note\">Screenshot removed by the recent-screenshot limit. Step details were retained.</p>");
                if (!String.IsNullOrEmpty(step.CaptureWarning)) html.Append("<p class=\"note warning\">").Append(Escape(step.CaptureWarning)).Append("</p>");
                html.Append("</div>");
                if (!String.IsNullOrEmpty(step.Screenshot)) html.Append("<img class=\"shot\" src=\"").Append(Escape(step.Screenshot)).Append("\" alt=\"Screenshot for step ").Append(step.Number).Append("\" loading=\"lazy\">");
                html.Append("</article>");
            }
            html.Append("<footer>Created with StepVela. Typed keys are not captured. Review screenshots and window/control labels for sensitive content before sharing.</footer></main></body></html>");
            return html.ToString();
        }
    }

    sealed class SettingsForm : Form
    {
        public bool ScreenshotEnabled { get { return screenshots.Checked; } }
        public int ScreenshotLimit { get { return (int)limit.Value; } }
        readonly CheckBox screenshots;
        readonly NumericUpDown limit;
        public SettingsForm(bool enabled, int count)
        {
            Text = "Recorder settings"; ClientSize = new Size(440, 245); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10); BackColor = Color.White;
            screenshots = new CheckBox { Text = "Capture screenshots", Checked = enabled, AutoSize = true, Location = new Point(24, 26) };
            Label label = new Label { Text = "Keep the most recent screenshots", AutoSize = true, Location = new Point(24, 76) };
            limit = new NumericUpDown { Minimum = 1, Maximum = 999, Value = count, Location = new Point(302, 72), Width = 100 };
            Label detail = new Label { Text = "Earlier steps keep their descriptions and comments.\r\nDefault: 25 screenshots. Settings apply to new recordings.", Location = new Point(24, 117), Size = new Size(392, 55), ForeColor = Color.FromArgb(90, 108, 130) };
            Button save = new Button { Text = "Save settings", DialogResult = DialogResult.OK, Location = new Point(274, 190), Size = new Size(130, 34) };
            Controls.AddRange(new Control[] { screenshots, label, limit, detail, save }); AcceptButton = save;
        }
    }

    sealed class HighlightView : Control
    {
        public Bitmap Screenshot;
        public Rectangle Selection;
        bool dragging;
        Point anchor;
        public HighlightView()
        { DoubleBuffered = true; BackColor = Color.FromArgb(231, 236, 243); Cursor = Cursors.Cross; }
        RectangleF ImageArea()
        {
            if (Screenshot == null) return RectangleF.Empty;
            float scale = Math.Min((float)Width / Screenshot.Width, (float)Height / Screenshot.Height);
            float width = Screenshot.Width * scale, height = Screenshot.Height * scale;
            return new RectangleF((Width - width) / 2, (Height - height) / 2, width, height);
        }
        Point ImagePoint(Point point)
        {
            RectangleF area = ImageArea();
            return new Point(Math.Max(0, Math.Min(Screenshot.Width - 1, (int)((point.X - area.X) * Screenshot.Width / area.Width))),
                Math.Max(0, Math.Min(Screenshot.Height - 1, (int)((point.Y - area.Y) * Screenshot.Height / area.Height))));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Screenshot == null) return;
            RectangleF area = ImageArea(); e.Graphics.DrawImage(Screenshot, area);
            if (Selection.Width > 0 && Selection.Height > 0)
            {
                float scale = area.Width / Screenshot.Width;
                RectangleF rect = new RectangleF(area.X + Selection.X * scale, area.Y + Selection.Y * scale, Selection.Width * scale, Selection.Height * scale);
                using (Brush brush = new SolidBrush(Color.FromArgb(65, 255, 205, 40))) e.Graphics.FillRectangle(brush, rect);
                using (Pen pen = new Pen(Color.FromArgb(229, 165, 5), 3)) e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Screenshot == null || e.Button != MouseButtons.Left || !ImageArea().Contains(e.Location)) return;
            anchor = ImagePoint(e.Location); dragging = true; Capture = true; Selection = Rectangle.Empty; Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            Point p = ImagePoint(e.Location);
            Selection = Rectangle.FromLTRB(Math.Min(anchor.X, p.X), Math.Min(anchor.Y, p.Y), Math.Max(anchor.X, p.X), Math.Max(anchor.Y, p.Y));
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }
    }

    sealed class CommentForm : Form
    {
        readonly TextBox comment;
        readonly HighlightView preview;
        public string CommentText { get { return comment.Text.Trim(); } }
        public Rectangle Highlight { get { return preview.Selection; } }
        public CommentForm(Bitmap screenshot)
        {
            Text = "Add a comment"; StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(620, 480); ClientSize = new Size(860, 620);
            Font = new Font("Segoe UI", 10); BackColor = Color.White; MinimizeBox = false;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Label instruction = new Label { Text = screenshot == null ? "Describe what happened. Screenshots are disabled or unavailable." : "Drag over the screenshot to highlight an area, then add your comment.", Dock = DockStyle.Fill, AutoSize = true };
            preview = new HighlightView { Screenshot = screenshot, Dock = DockStyle.Fill, Visible = screenshot != null };
            comment = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 12, 0, 0), MaxLength = 10000 };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            Button add = new Button { Text = "Add comment", Size = new Size(135, 33) };
            add.Click += delegate { if (String.IsNullOrWhiteSpace(CommentText)) { comment.Focus(); return; } DialogResult = DialogResult.OK; Close(); };
            Button cancel = new Button { Text = "Cancel", Size = new Size(95, 33), DialogResult = DialogResult.Cancel };
            Button clear = new Button { Text = "Clear highlight", Size = new Size(135, 33), Enabled = screenshot != null };
            clear.Click += delegate { preview.Selection = Rectangle.Empty; preview.Invalidate(); };
            buttons.Controls.AddRange(new Control[] { add, cancel, clear });
            layout.Controls.Add(instruction, 0, 0); layout.Controls.Add(preview, 0, 1); layout.Controls.Add(comment, 0, 2); layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout); CancelButton = cancel; Shown += delegate { comment.Focus(); };
        }
    }

    sealed class RecorderForm : Form
    {
        readonly RecordingSession session = new RecordingSession();
        readonly List<Step> steps = new List<Step>();
        readonly BlockingCollection<CaptureRequest> queue = new BlockingCollection<CaptureRequest>(64);
        readonly Thread worker;
        readonly Native.HookProc hookProc;
        readonly Native.HookProc keyboardProc;
        readonly int ownProcess = Process.GetCurrentProcess().Id;
        readonly Button start, pause, stop, comment, save, settings;
        readonly Label stateLabel, countLabel, privacy, detail;
        readonly ListView list;
        readonly PictureBox preview;
        readonly System.Windows.Forms.Timer timer;
        IntPtr hook;
        IntPtr keyboardHook;
        CaptureRequest typingRequest;
        DateTimeOffset lastTyping;
        CaptureRequest wheelRequest;
        DateTimeOffset lastWheel;
        string sessionFolder;
        string sessionsRoot;
        int pending;
        int dropped;
        bool exiting;
        bool dirty;
        bool commentBusy;
        bool screenshotsEnabled = true;
        int screenshotLimit = 25;
        bool activeScreenshots = true;
        int activeLimit = 25;
        DateTimeOffset startedAt;
        DateTimeOffset? stoppedAt;
        string savedPath;

        public RecorderForm()
        {
            Text = "StepVela"; ClientSize = new Size(1080, 735); MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(241, 244, 249);
            AutoScaleMode = AutoScaleMode.Dpi;
            sessionsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StepVela", "Sessions");
            Directory.CreateDirectory(sessionsRoot);
            LoadSettings();
            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(22) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 94)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            Panel heading = new Panel { Dock = DockStyle.Fill };
            Label title = new Label { Text = "StepVela", Font = new Font("Segoe UI", 25, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0), ForeColor = Color.FromArgb(25, 43, 67) };
            Label subtitle = new Label { Text = "Turn desktop actions into a clear, shareable walkthrough.", AutoSize = true, Location = new Point(2, 53), ForeColor = Color.FromArgb(81, 101, 125) };
            Label badge = new Label { Text = "DYNAMIC STEP RECORDER", AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(75, 100, 131), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            heading.Controls.AddRange(new Control[] { title, subtitle, badge });
            heading.Resize += delegate { badge.Location = new Point(Math.Max(0, heading.Width - badge.Width), 17); };
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            start = Button("Start recording", 158, true); pause = Button("Pause", 105, false); stop = Button("Stop", 86, false);
            comment = Button("Add comment", 137, false); save = Button("Save ZIP", 112, false); settings = Button("Settings", 99, false);
            toolbar.Controls.AddRange(new Control[] { start, pause, stop, comment, save, settings });
            Panel status = new Panel { Dock = DockStyle.Fill };
            stateLabel = new Label { AutoSize = true, Location = new Point(2, 8), Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            countLabel = new Label { AutoSize = true, Location = new Point(252, 8), ForeColor = Color.FromArgb(84, 102, 124) };
            status.Controls.AddRange(new Control[] { stateLabel, countLabel });
            SplitContainer body = new SplitContainer { Size = new Size(1036, 430), SplitterDistance = 430, Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 10, BackColor = BackColor, Panel1MinSize = 280, Panel2MinSize = 340 };
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None, BackColor = Color.White };
            list.Columns.Add("Step", 48); list.Columns.Add("Action / application", 286); list.Columns.Add("Time", 92);
            Panel previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14) };
            TableLayoutPanel previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(231, 237, 245) };
            detail = new Label { Dock = DockStyle.Fill, Padding = new Padding(2, 14, 0, 0), Text = "Start recording, then click in another app.\r\nSelect a step here to inspect its screenshot and details.", ForeColor = Color.FromArgb(79, 99, 124), AutoEllipsis = true };
            previewLayout.Controls.Add(preview, 0, 0); previewLayout.Controls.Add(detail, 0, 1); previewPanel.Controls.Add(previewLayout);
            body.Panel1.Controls.Add(list); body.Panel2.Controls.Add(previewPanel);
            privacy = new Label { Dock = DockStyle.Fill, Text = "Local only. Typed characters are never stored. Screenshots and app labels can include private information.", ForeColor = Color.FromArgb(91, 109, 130), Font = new Font("Segoe UI", 9), Padding = new Padding(2, 16, 0, 0) };
            shell.Controls.Add(heading, 0, 0); shell.Controls.Add(toolbar, 0, 1); shell.Controls.Add(status, 0, 2); shell.Controls.Add(body, 0, 3); shell.Controls.Add(privacy, 0, 4);
            Controls.Add(shell);
            Shown += delegate { body.SplitterDistance = Math.Min(430, body.Width - 340); };
            start.Click += delegate { StartRecording(); }; pause.Click += delegate { TogglePause(); }; stop.Click += delegate { StopRecording(); };
            comment.Click += delegate { AddComment(); }; save.Click += delegate { SaveRecording(); }; settings.Click += delegate { EditSettings(); };
            list.SelectedIndexChanged += delegate { ShowSelection(); };
            FormClosing += OnClosing; hookProc = MouseHook; keyboardProc = KeyboardHook;
            worker = new Thread(Work) { IsBackground = true, Name = "Steps screenshot worker" }; worker.Start();
            timer = new System.Windows.Forms.Timer { Interval = 200 }; timer.Tick += delegate
            {
                if (typingRequest != null && DateTimeOffset.Now - lastTyping > TimeSpan.FromMilliseconds(600)) FlushTyping();
                if (wheelRequest != null && DateTimeOffset.Now - lastWheel > TimeSpan.FromMilliseconds(400)) FlushWheel();
                UpdateControls();
            }; timer.Start();
            UpdateControls();
        }

        Button Button(string title, int width, bool primary)
        {
            Button button = new Button { Text = title, Width = width, Height = 40, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 10, 0), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(198, 209, 224); button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.BackColor = primary ? Color.FromArgb(37, 91, 177) : Color.White; button.ForeColor = primary ? Color.White : Color.FromArgb(36, 57, 84);
            return button;
        }
        void UpdateControls()
        {
            bool active = session.State == RecordingState.Recording, paused = session.State == RecordingState.Paused;
            int remaining = Volatile.Read(ref pending) + (typingRequest != null ? 1 : 0) + (wheelRequest != null ? 1 : 0);
            start.Enabled = !active && !paused && remaining == 0; pause.Enabled = active || paused; stop.Enabled = active || paused;
            comment.Enabled = (active || paused) && remaining == 0; save.Enabled = session.State == RecordingState.Stopped && remaining == 0 && steps.Count > 0;
            settings.Enabled = !active && !paused && remaining == 0; pause.Text = paused ? "Resume" : "Pause";
            if (commentBusy) { start.Enabled = pause.Enabled = stop.Enabled = comment.Enabled = save.Enabled = settings.Enabled = false; }
            stateLabel.Text = active ? "●  Recording" : paused ? "●  Paused" : session.State == RecordingState.Stopped ? (remaining > 0 ? "●  Finishing captures…" : "●  Recording stopped") : "●  Ready to record";
            stateLabel.ForeColor = active ? Color.FromArgb(197, 40, 54) : paused ? Color.FromArgb(153, 106, 15) : Color.FromArgb(61, 98, 135);
            TimeSpan elapsed = startedAt == default(DateTimeOffset) ? TimeSpan.Zero : (stoppedAt ?? DateTimeOffset.Now) - startedAt;
            countLabel.Text = steps.Count + " steps" + (remaining > 0 ? " · " + remaining + " pending" : "") + " · " + elapsed.ToString(@"hh\:mm\:ss") + (dropped > 0 ? " · " + dropped + " events skipped (busy)" : "");
            Text = (active ? "[Recording] " : paused ? "[Paused] " : "") + "StepVela";
        }
        void InstallHook()
        {
            if (hook != IntPtr.Zero) return;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, hookProc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows could not install the mouse recorder hook.");
            keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, Native.GetModuleHandle(null), 0);
            if (keyboardHook == IntPtr.Zero) { int error = Marshal.GetLastWin32Error(); RemoveHook(); throw new System.ComponentModel.Win32Exception(error, "Windows could not install the keyboard event hook."); }
        }
        void RemoveHook()
        {
            FlushTyping();
            FlushWheel();
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        }
        void StartRecording()
        {
            if (dirty && steps.Count > 0 && MessageBox.Show(this, "Start a new recording and discard the current unsaved steps?", "New recording", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                ClearPreview(); CleanupSession();
                sessionFolder = Path.Combine(sessionsRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sessionFolder);
                steps.Clear(); list.Items.Clear(); dirty = false; savedPath = null; dropped = 0;
                activeScreenshots = screenshotsEnabled; activeLimit = screenshotLimit;
                InstallHook(); session.Start(); startedAt = DateTimeOffset.Now; stoppedAt = null;
                detail.Text = "Recording mouse clicks in other apps. Use Pause to take a break or Stop to prepare the report.";
            }
            catch (Exception error) { RemoveHook(); ShowError("Could not start recording", error); }
            UpdateControls();
        }
        void TogglePause()
        {
            try
            {
                if (session.State == RecordingState.Recording) { RemoveHook(); session.Pause(); }
                else if (session.State == RecordingState.Paused) { InstallHook(); session.Resume(); }
            }
            catch (Exception error) { RemoveHook(); ShowError("Could not resume recording", error); }
            UpdateControls();
        }
        void StopRecording()
        {
            RemoveHook(); session.Stop(); stoppedAt = DateTimeOffset.Now; UpdateControls();
            if (steps.Count == 0 && Volatile.Read(ref pending) == 0) detail.Text = "No actions were recorded. Start again and click in another application.";
            else detail.Text = "Review your steps, then save the recording as an offline ZIP report.";
        }
        IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                int message = wParam.ToInt32();
                if (code >= 0 && session.State == RecordingState.Recording && !exiting &&
                    (message == Native.WM_LBUTTONUP || message == Native.WM_RBUTTONUP || message == Native.WM_MBUTTONUP || message == Native.WM_MOUSEWHEEL || message == Native.WM_MOUSEHWHEEL))
                {
                    Native.MouseData data = (Native.MouseData)Marshal.PtrToStructure(lParam, typeof(Native.MouseData));
                    IntPtr target = Native.WindowFromPoint(data.Point); target = Native.GetAncestor(target, Native.GA_ROOT);
                    if (target == IntPtr.Zero) target = Native.GetForegroundWindow();
                    uint processId; Native.GetWindowThreadProcessId(target, out processId);
                    if (processId != ownProcess)
                    {
                        FlushTyping();
                        bool wheel = message == Native.WM_MOUSEWHEEL || message == Native.WM_MOUSEHWHEEL;
                        int delta = (short)((data.MouseDataValue >> 16) & 0xffff);
                        string action = wheel ? (message == Native.WM_MOUSEHWHEEL ? (delta > 0 ? "Scroll right" : "Scroll left") : (delta > 0 ? "Scroll up" : "Scroll down")) :
                            message == Native.WM_RBUTTONUP ? "Right click" : message == Native.WM_MBUTTONUP ? "Middle click" : "Left click";
                        CaptureRequest request = new CaptureRequest
                        {
                            Point = new Point(data.Point.X, data.Point.Y), Time = DateTimeOffset.Now,
                            Action = action,
                            Window = target, ProcessId = processId, Folder = sessionFolder, ScreenshotEnabled = activeScreenshots
                        };
                        if (wheel)
                        {
                            if (wheelRequest != null && (wheelRequest.Action != action || wheelRequest.Window != target || request.Time - lastWheel > TimeSpan.FromMilliseconds(400))) FlushWheel();
                            if (wheelRequest == null) wheelRequest = request;
                            else wheelRequest.Point = request.Point;
                            lastWheel = request.Time; dirty = true;
                        }
                        else { FlushWheel(); Enqueue(request); }
                    }
                }
            }
            catch { /* A recorder failure must never prevent delivery of the user's click. */ }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }
        void Enqueue(CaptureRequest request)
        {
            if (exiting || queue.IsAddingCompleted) return;
            dirty = true; Interlocked.Increment(ref pending);
            if (!queue.TryAdd(request)) { Interlocked.Decrement(ref pending); dropped++; }
        }
        void FlushTyping()
        {
            CaptureRequest request = typingRequest; typingRequest = null;
            if (request != null) Enqueue(request);
        }
        void FlushWheel()
        {
            CaptureRequest request = wheelRequest; wheelRequest = null;
            if (request != null) Enqueue(request);
        }
        static bool Down(int key) { return (Native.GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static string KeyAction(uint key, bool ctrl, bool alt, bool shift, bool win)
        {
            string prefix = (win ? "Win+" : "") + (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "");
            if (key >= 0x70 && key <= 0x87) return "Key " + prefix + "F" + (key - 0x6F);
            // Ctrl+Alt can be AltGr on international keyboards, so never spell out
            // its printable key. Only an explicit list of common shortcuts is named.
            if (ctrl && !alt && !win && "ACVXZYSOPFNWTLRQH".IndexOf((char)key) >= 0) return "Shortcut " + prefix + (char)key;
            if (win && !ctrl && !alt && "DERL".IndexOf((char)key) >= 0) return "Shortcut " + prefix + (char)key;
            string named = key == 0x09 ? "Tab" : key == 0x0D ? "Enter" : key == 0x1B ? "Escape" : key == 0x08 ? "Backspace" : key == 0x2E ? "Delete" :
                key == 0x24 ? "Home" : key == 0x23 ? "End" : key == 0x21 ? "PageUp" : key == 0x22 ? "PageDown" : key == 0x25 ? "Left" : key == 0x26 ? "Up" :
                key == 0x27 ? "Right" : key == 0x28 ? "Down" : key == 0x2D ? "Insert" : null;
            return named == null ? null : "Key " + prefix + named;
        }
        IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                int message = wParam.ToInt32();
                if (code >= 0 && !exiting && session.State == RecordingState.Recording && (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN))
                {
                    Native.KeyboardData data = (Native.KeyboardData)Marshal.PtrToStructure(lParam, typeof(Native.KeyboardData));
                    uint key = data.VirtualKey;
                    if (key == 0x10 || key == 0x11 || key == 0x12 || key == 0x5B || key == 0x5C || (key >= 0xA0 && key <= 0xA5)) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
                    IntPtr target = Native.GetForegroundWindow(); uint processId; Native.GetWindowThreadProcessId(target, out processId);
                    if (processId != ownProcess)
                    {
                        FlushWheel();
                        DateTimeOffset now = DateTimeOffset.Now;
                        string action = KeyAction(key, Down(0x11), Down(0x12), Down(0x10), Down(0x5B) || Down(0x5C));
                        CaptureRequest request = new CaptureRequest { Keyboard = true, Action = action ?? "Keyboard input (characters not recorded)",
                            Time = now, Window = target, ProcessId = processId, Folder = sessionFolder, ScreenshotEnabled = activeScreenshots };
                        if (action != null) { FlushTyping(); Enqueue(request); }
                        else
                        {
                            if (typingRequest != null && (typingRequest.Window != target || now - lastTyping > TimeSpan.FromMilliseconds(600))) FlushTyping();
                            if (typingRequest == null) typingRequest = request;
                            lastTyping = now; dirty = true;
                        }
                    }
                }
            }
            catch { }
            return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
        }
        void Work()
        {
            try
            {
                foreach (CaptureRequest request in queue.GetConsumingEnumerable())
                {
                    if (exiting) { Interlocked.Decrement(ref pending); continue; }
                    Step result;
                    try { result = ScreenCapture.MakeStep(request); }
                    catch (Exception error)
                    {
                        result = new Step { Timestamp = request.Time.ToString("o"), Action = request.Action, Description = request.Action,
                            WindowTitle = request.WindowTitle, ProcessName = request.ProcessName, ClickX = request.Point.X, ClickY = request.Point.Y, CaptureWarning = error.Message };
                    }
                    if (exiting) { Interlocked.Decrement(ref pending); continue; }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            try { if (!exiting) Append(result); }
                            finally { Interlocked.Decrement(ref pending); UpdateControls(); }
                        });
                    }
                    catch (InvalidOperationException) { Interlocked.Decrement(ref pending); }
                }
            }
            finally { if (exiting) CleanupSession(); }
        }
        void Append(Step step)
        {
            step.Number = steps.Count + 1; steps.Add(step); dirty = true;
            try { Exporter.ApplyRetention(steps, activeLimit); }
            catch (Exception error) { step.CaptureWarning = ScreenCapture.JoinWarning(step.CaptureWarning, "Old screenshot cleanup failed: " + error.Message); }
            ListViewItem row = new ListViewItem(step.Number.ToString()); row.SubItems.Add(step.Description + (String.IsNullOrEmpty(step.ProcessName) ? "" : " · " + step.ProcessName));
            DateTimeOffset stamp; row.SubItems.Add(DateTimeOffset.TryParse(step.Timestamp, out stamp) ? stamp.ToString("HH:mm:ss") : ""); row.Tag = step;
            list.Items.Add(row); row.Selected = true; row.EnsureVisible(); UpdateControls();
        }
        void ClearPreview()
        { Image previous = preview.Image; preview.Image = null; if (previous != null) previous.Dispose(); }
        void ShowSelection()
        {
            ClearPreview(); if (list.SelectedItems.Count == 0) return;
            Step step = (Step)list.SelectedItems[0].Tag;
            if (!String.IsNullOrEmpty(step.LocalScreenshot) && File.Exists(step.LocalScreenshot))
            {
                try { using (Image file = Image.FromFile(step.LocalScreenshot)) preview.Image = new Bitmap(file); }
                catch (Exception error) { step.CaptureWarning = ScreenCapture.JoinWarning(step.CaptureWarning, "Preview failed: " + error.Message); }
            }
            detail.Text = step.Description + "\r\n" + (String.IsNullOrEmpty(step.WindowTitle) ? step.ProcessName : step.WindowTitle + " · " + step.ProcessName)
                + "\r\n" + (!String.IsNullOrEmpty(step.Comment) ? step.Comment : step.ScreenshotOmitted ? "Screenshot removed by the recent-screenshot limit." : step.CaptureWarning ?? "");
        }
        async void AddComment()
        {
            bool resume = session.State == RecordingState.Recording;
            if (resume) { RemoveHook(); session.Pause(); }
            commentBusy = true;
            UpdateControls(); comment.Enabled = false;
            Bitmap bitmap = null; Rectangle bounds = Rectangle.Empty; string warning = null;
            try
            {
                while (Volatile.Read(ref pending) > 0 && !exiting) await Task.Delay(30);
                if (exiting) return;
                if (activeScreenshots)
                {
                    Hide();
                    await Task.Delay(220);
                    try { bitmap = ScreenCapture.Desktop(out bounds); }
                    catch (Exception error) { warning = "Comment screenshot failed: " + error.Message; }
                    Show(); Activate();
                }
                using (CommentForm dialog = new CommentForm(bitmap))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        Step step = new Step { Timestamp = DateTimeOffset.Now.ToString("o"), Action = "Comment", Description = "User comment", Comment = dialog.CommentText, CaptureWarning = warning };
                        if (bitmap != null)
                        {
                            Rectangle highlight = dialog.Highlight;
                            if (highlight.Width > 2 && highlight.Height > 2)
                            {
                                step.Highlight = new ScreenBounds(new Rectangle(highlight.X + bounds.X, highlight.Y + bounds.Y, highlight.Width, highlight.Height));
                                using (Graphics graphics = Graphics.FromImage(bitmap))
                                using (Brush brush = new SolidBrush(Color.FromArgb(60, 255, 205, 40)))
                                using (Pen pen = new Pen(Color.FromArgb(235, 170, 0), 4))
                                { graphics.FillRectangle(brush, highlight); graphics.DrawRectangle(pen, highlight); }
                            }
                            step.DesktopBounds = new ScreenBounds(bounds); step.LocalScreenshot = Path.Combine(sessionFolder, Guid.NewGuid().ToString("N") + ".png");
                            bitmap.Save(step.LocalScreenshot, ImageFormat.Png);
                        }
                        Append(step);
                    }
                }
            }
            catch (Exception error) { Show(); ShowError("Could not add comment", error); }
            finally
            {
                if (bitmap != null) bitmap.Dispose();
                commentBusy = false;
                if (resume && session.State == RecordingState.Paused && !exiting)
                {
                    try { InstallHook(); session.Resume(); }
                    catch (Exception error) { ShowError("Recording remains paused", error); }
                }
                UpdateControls();
            }
        }
        void SaveRecording()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "Save steps report", Filter = "Steps report ZIP (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true, FileName = "steps-recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UseWaitCursor = true; Exporter.Save(dialog.FileName, steps, activeScreenshots, activeLimit, startedAt, stoppedAt, dropped);
                    dirty = false; savedPath = dialog.FileName; detail.Text = "Saved " + savedPath + "\r\nExtract the ZIP and open report.html. Structured step data is in data.json.";
                }
                catch (Exception error) { ShowError("Could not save report", error); }
                finally { UseWaitCursor = false; }
            }
        }
        void EditSettings()
        {
            using (SettingsForm dialog = new SettingsForm(screenshotsEnabled, screenshotLimit))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                screenshotsEnabled = dialog.ScreenshotEnabled; screenshotLimit = dialog.ScreenshotLimit;
                try
                {
                    string root = Path.GetDirectoryName(sessionsRoot);
                    File.WriteAllLines(Path.Combine(root, "settings.txt"), new string[] { screenshotsEnabled.ToString(), screenshotLimit.ToString() });
                }
                catch (Exception error) { ShowError("Settings apply for this launch but could not be saved", error); }
            }
        }
        void LoadSettings()
        {
            try
            {
                string[] values = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(sessionsRoot), "settings.txt")); bool enabled; int count;
                if (values.Length >= 2 && Boolean.TryParse(values[0], out enabled) && Int32.TryParse(values[1], out count) && count >= 1 && count <= 999)
                { screenshotsEnabled = enabled; screenshotLimit = count; }
            }
            catch { }
        }
        void CleanupSession()
        {
            // Only a generated child of the app's Sessions directory may be deleted.
            string folder = sessionFolder; if (String.IsNullOrEmpty(folder)) return;
            string root = Path.GetFullPath(sessionsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(folder);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(full).Length != 32) return;
            Guid id; if (!Guid.TryParseExact(Path.GetFileName(full), "N", out id)) return;
            try { if (Directory.Exists(full)) Directory.Delete(full, true); }
            catch { /* A crash or external file lock can leave only this app-owned folder. */ }
        }
        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (dirty && (steps.Count > 0 || Volatile.Read(ref pending) > 0 || typingRequest != null || wheelRequest != null) && MessageBox.Show(this, "Close and discard the current unsaved recording?", "Unsaved recording", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            { e.Cancel = true; return; }
            exiting = true; RemoveHook(); timer.Stop(); ClearPreview(); queue.CompleteAdding();
            if (worker.Join(1600)) CleanupSession();
        }
        void ShowError(string title, Exception error)
        { MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    static class SelfTest
    {
        static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Self-test failed: " + message); }
        public static void Run(string folder)
        {
            Directory.CreateDirectory(folder);
            string sessionFolder = Path.Combine(folder, "synthetic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sessionFolder);
            List<Step> steps = new List<Step>();
            try
            {
                RecordingSession state = new RecordingSession(); Assert(state.State == RecordingState.Ready, "initial state");
                bool rejected = false; try { state.Resume(); } catch (InvalidOperationException) { rejected = true; } Assert(rejected, "invalid resume rejected");
                state.Start(); Assert(state.State == RecordingState.Recording, "start state"); state.Pause(); Assert(state.State == RecordingState.Paused, "pause state");
                state.Resume(); Assert(state.State == RecordingState.Recording, "resume state"); state.Stop(); Assert(state.State == RecordingState.Stopped, "stop state");
                state.Start(); state.Pause(); state.Stop(); Assert(state.State == RecordingState.Stopped, "stop while paused");
                Assert(RecorderForm.KeyAction(0x41, false, false, false, false) == null && RecorderForm.KeyAction(0x37, false, false, false, false) == null, "ordinary printable keys never named");
                Assert(RecorderForm.KeyAction(0x41, true, true, false, false) == null, "AltGr input never exposes printable key");
                Assert(RecorderForm.KeyAction(0x53, true, false, false, false) == "Shortcut Ctrl+S" && RecorderForm.KeyAction(0x70, false, false, false, false) == "Key F1", "safe named shortcuts/functions");
                for (int i = 1; i <= 3; i++)
                {
                    string path = Path.Combine(sessionFolder, i + ".png");
                    using (Bitmap bitmap = new Bitmap(480, 270))
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    using (Font font = new Font("Segoe UI", 16))
                    {
                        graphics.Clear(Color.FromArgb(231, 238, 247)); graphics.DrawString("Synthetic screenshot " + i, font, Brushes.Navy, 30, 30);
                        ScreenCapture.DrawMarker(bitmap, new Point(160, 120)); bitmap.Save(path, ImageFormat.Png);
                    }
                    steps.Add(new Step { Number = i, Timestamp = "2026-10-14T11:00:00+05:30", Action = i == 3 ? "Comment" : "Left click",
                        Description = "Click <script>alert('x')</script> & \"दस्तावेज़\"", WindowTitle = "测试 & Window", ProcessName = "synthetic",
                        ControlName = "दस्तावेज़", ControlType = "Button", ClickX = -160, ClickY = 120,
                        Comment = i == 3 ? "Check <this> & Unicode: नमस्ते\nSecond line" : null,
                        Highlight = i == 3 ? new ScreenBounds(new Rectangle(-200, 40, 100, 60)) : null,
                        DesktopBounds = new ScreenBounds(new Rectangle(-320, 0, 480, 270)), LocalScreenshot = path });
                }
                Exporter.ApplyRetention(steps, 2);
                Assert(steps[0].ScreenshotOmitted && steps[0].LocalScreenshot == null && !File.Exists(Path.Combine(sessionFolder, "1.png")), "retention deletes earliest image and keeps metadata");
                DateTimeOffset start = DateTimeOffset.Parse("2026-10-14T11:00:00+05:30"), stop = start.AddMinutes(1);
                string destination = Path.Combine(folder, "self-test-report.zip"); Exporter.Save(destination, steps, true, 2, start, stop, 2);
                Exporter.Save(destination, steps, true, 2, start, stop, 2); // Exercise atomic replacement of an existing export.
                using (ZipArchive zip = ZipFile.OpenRead(destination))
                {
                    Assert(zip.Entries.Count(e => e.FullName.EndsWith(".png")) == 2, "ZIP contains only retained screenshots");
                    string html; using (StreamReader reader = new StreamReader(zip.GetEntry("report.html").Open(), Encoding.UTF8)) html = reader.ReadToEnd();
                    Assert(html.Contains("&lt;script&gt;") && !html.Contains("<script>"), "HTML injection escaped");
                    Assert(html.Contains("दस्तावेज़") && html.Contains("测试") && html.Contains("नमस्ते"), "Unicode labels and comments preserved");
                    Assert(html.Contains("screenshots/step-0002.png") && html.Contains("Screenshot removed"), "HTML has relative screenshot paths and retention note");
                    Report report; using (Stream json = zip.GetEntry("data.json").Open()) report = (Report)new DataContractJsonSerializer(typeof(Report)).ReadObject(json);
                    Assert(report.Steps.Count == 3 && report.Steps[2].Comment == steps[2].Comment, "JSON step/comment round trip");
                    Assert(report.Steps[2].Highlight.X == -200 && report.Steps[0].ClickX == -160, "negative-monitor coordinates preserved");
                    Assert(report.Steps[1].LocalScreenshot == null && report.Steps[1].Screenshot == "screenshots/step-0002.png", "JSON exposes only portable relative screenshot path");
                    Assert(report.SessionStartedAt == start.ToString("o") && report.SessionStoppedAt == stop.ToString("o"), "session start/stop times preserved");
                    Assert(report.SkippedEventCount == 2 && html.Contains("input events were skipped"), "incomplete-event warning retained in report");
                    Assert(zip.GetEntry("README.txt") != null, "offline instructions included");
                }
                Exporter.Save(Path.Combine(folder, "self-test-no-screenshots.zip"), new List<Step> { new Step { Number = 1, Action = "Comment", Description = "Metadata-only comment", Comment = "No screen capture", Timestamp = DateTimeOffset.Now.ToString("o") } }, false, 25);
                using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(folder, "self-test-no-screenshots.zip"))) Assert(zip.Entries.Count == 3, "metadata-only export has no images");
                File.WriteAllText(Path.Combine(folder, "self-test-results.txt"), "PASS\r\nState transitions and invalid transitions; pause-stop; printable/AltGr key privacy; safe shortcut naming; generated screenshot marker; retention; PNG ZIP entries; atomic overwrite; HTML escaping; Unicode; JSON round trip; negative monitor coordinates; relative paths; session start/stop; dropped-event report warnings; metadata-only export.\r\nSynthetic images only: no desktop capture, hook, or UI Automation calls were used.\r\n", Encoding.UTF8);
            }
            finally { Directory.Delete(sessionFolder, true); }
        }
    }

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
