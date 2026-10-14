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
    sealed partial class RecorderForm : Form
    {
        readonly RecordingSession session = new RecordingSession();
        readonly List<Step> steps = new List<Step>();
        readonly BlockingCollection<CaptureRequest> queue = new BlockingCollection<CaptureRequest>(16);
        readonly Thread worker;
        readonly Native.HookProc hookProc;
        readonly Native.HookProc keyboardProc;
        readonly int ownProcess = Process.GetCurrentProcess().Id;
        Button start, pause, stop, comment, save, settings;
        Label stateLabel, countLabel, detail;
        ListView list;
        PictureBox preview;
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
        bool activeWindowOnly = true;
        bool activeWindowCapture = true;
        bool activeScreenshots = true;
        int activeLimit = 25;
        DateTimeOffset startedAt;
        DateTimeOffset? stoppedAt;
        string savedPath;

        public RecorderForm()
        {
            Theme.Form(this); Text="StepVela"; ClientSize=new Size(1180,790); MinimumSize=new Size(960,680); StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Dpi;
            try { Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            sessionsRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StepVela","Sessions");
            Directory.CreateDirectory(sessionsRoot); LoadSettings(); BackColor=Theme.Background;ForeColor=Theme.Text;BuildWorkspace();
            start.Click+=delegate { StartRecording(); }; pause.Click+=delegate { TogglePause(); }; stop.Click+=delegate { StopRecording(); };
            comment.Click+=delegate { AddComment(); }; save.Click+=delegate { SaveRecording(); }; settings.Click+=delegate { EditSettings(); };
            list.SelectedIndexChanged+=delegate { ShowSelection(); };
            FormClosing+=OnClosing; hookProc=MouseHook; keyboardProc=KeyboardHook;
            worker=new Thread(Work) {IsBackground=true,Name="StepVela capture"}; worker.Start();
            timer=new System.Windows.Forms.Timer {Interval=500}; timer.Tick+=delegate {
                if(typingRequest!=null && DateTimeOffset.Now-lastTyping>TimeSpan.FromMilliseconds(600)) FlushTyping();
                if(wheelRequest!=null && DateTimeOffset.Now-lastWheel>TimeSpan.FromMilliseconds(400)) FlushWheel();
                UpdateControls();
            };
            UpdateControls();
            Shown+=delegate { Native.RegisterHotKey(Handle,8101,0x4006,0x77); Native.RegisterHotKey(Handle,8102,0x4006,0x78); };
        }
        protected override void WndProc(ref Message message) {
            if(message.Msg==0x0312&&Enabled&&!commentBusy) {
                int id=message.WParam.ToInt32();
                if(id==8101) { if(session.State==RecordingState.Recording||session.State==RecordingState.Paused)TogglePause();else if(Volatile.Read(ref pending)==0)StartRecording(); }
                if(id==8102&&(session.State==RecordingState.Recording||session.State==RecordingState.Paused))StopRecording();
            }
            base.WndProc(ref message);
        }
        internal void FailSafeStop() { try {RemoveHook();if(session.State==RecordingState.Recording||session.State==RecordingState.Paused)session.Stop();UpdateControls();}catch{} }

        void UpdateControls()
        {
            bool active = session.State == RecordingState.Recording, paused = session.State == RecordingState.Paused;
            int remaining = Volatile.Read(ref pending) + (typingRequest != null ? 1 : 0) + (wheelRequest != null ? 1 : 0);
            start.Enabled = !active && !paused && remaining == 0; pause.Enabled = active || paused; stop.Enabled = active || paused;
            comment.Enabled = (active || paused) && remaining == 0; save.Enabled = session.State == RecordingState.Stopped && remaining == 0 && steps.Count > 0;
            settings.Enabled = !active && !paused && remaining == 0; pause.Text = paused ? "Resume" : "Pause";
            if (commentBusy) { start.Enabled = pause.Enabled = stop.Enabled = comment.Enabled = save.Enabled = settings.Enabled = false; }
            stateLabel.Text = active ? "● Recording" : paused ? "● Paused" : remaining>0 ? "Finishing…" : "● Ready";
            stateLabel.ForeColor = active ? Color.FromArgb(197, 40, 54) : paused ? Color.FromArgb(153, 106, 15) : Theme.Muted;
            TimeSpan elapsed = startedAt == default(DateTimeOffset) ? TimeSpan.Zero : (stoppedAt ?? DateTimeOffset.Now) - startedAt;
            countLabel.Text = steps.Count + " steps" + (remaining > 0 ? " · " + remaining + " pending" : "") + " · " + elapsed.ToString(@"hh\:mm\:ss") + (dropped > 0 ? " · " + dropped + " events skipped (busy)" : "");
            Text = (active ? "● " : "") + "StepVela"; UpdateEditorControls(!active && remaining==0 && !commentBusy);
            if(active || paused || remaining>0) timer.Start(); else timer.Stop();
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
                steps.Clear(); list.Items.Clear(); dirty = false; savedPath = null; projectPath=null; dropped = 0; SetGuideTitle("Untitled guide");
                activeScreenshots = screenshotsEnabled; activeLimit = screenshotLimit;
                activeWindowCapture = activeWindowOnly;
                InstallHook(); session.Start(); startedAt = DateTimeOffset.Now; stoppedAt = null;
                detail.Text = "Capturing your flow";
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
            else detail.Text = "Ready to edit and export";
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
            request.ActiveWindowOnly=activeWindowCapture;
            if(steps.Count+Volatile.Read(ref pending)>=10000) { dropped++; dirty=true; return; }
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
                    if(Down(0x11)&&Down(0x10)&&(key==0x77||key==0x78))return Native.CallNextHookEx(keyboardHook,code,wParam,lParam);
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
                    try {
                        // Input hooks run before the target app processes the event.
                        // Give its next paint a short window without blocking input.
                        int settle=(int)(request.Time.AddMilliseconds(150)-DateTimeOffset.Now).TotalMilliseconds;
                        if(settle>0) Thread.Sleep(settle);
                        result = ScreenCapture.MakeStep(request);
                    }
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
            ClearPreview(); if (list.SelectedItems.Count == 0) { BindStep(null); if(timer!=null)UpdateControls(); return; }
            Step step = (Step)list.SelectedItems[0].Tag;
            if (!String.IsNullOrEmpty(step.LocalScreenshot) && File.Exists(step.LocalScreenshot))
            {
                try { using (Image file = Image.FromFile(step.LocalScreenshot)) preview.Image = new Bitmap(file); }
                catch (Exception error) { step.CaptureWarning = ScreenCapture.JoinWarning(step.CaptureWarning, "Preview failed: " + error.Message); }
            }
            detail.Text=(step.ProcessName??"")+(step.ScreenshotOmitted?" · Screenshot limit reached":"");
            BindStep(step);
            UpdateControls();
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
                    try {
                        IntPtr target=activeWindowCapture?Native.GetForegroundWindow():IntPtr.Zero;
                        uint processId;Native.GetWindowThreadProcessId(target,out processId);
                        if(activeWindowCapture&&(target==IntPtr.Zero||processId==ownProcess))throw new InvalidOperationException("Select another application before adding a screenshot note.");
                        bitmap = ScreenCapture.Desktop(out bounds,target);
                    }
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
                                step.Highlight = new ScreenBounds(new Rectangle(highlight.X*bounds.Width/bitmap.Width + bounds.X, highlight.Y*bounds.Height/bitmap.Height + bounds.Y, highlight.Width*bounds.Width/bitmap.Width, highlight.Height*bounds.Height/bitmap.Height));
                                using (Graphics graphics = Graphics.FromImage(bitmap))
                                using (Brush brush = new SolidBrush(Color.FromArgb(60, 255, 205, 40)))
                                using (Pen pen = new Pen(Color.FromArgb(235, 170, 0), 4))
                                { graphics.FillRectangle(brush, highlight); graphics.DrawRectangle(pen, highlight); }
                            }
                            step.DesktopBounds = new ScreenBounds(bounds); step.LocalScreenshot = Path.Combine(sessionFolder, Guid.NewGuid().ToString("N") + ".jpg");
                            ScreenCapture.SaveJpeg(bitmap,step.LocalScreenshot);
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
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "Export guide", Filter = "Guide ZIP — HTML + Markdown (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true, FileName = "stepvela-guide.zip", OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UseWaitCursor = true; Exporter.Save(dialog.FileName, steps, activeScreenshots, activeLimit, startedAt, stoppedAt, dropped, guideTitle.Text);
                    savedPath = dialog.FileName; detail.Text = "Exported " + Path.GetFileName(savedPath);
                }
                catch (Exception error) { ShowError("Could not save report", error); }
                finally { UseWaitCursor = false; }
            }
        }
        void EditSettings()
        {
            using (SettingsForm dialog = new SettingsForm(screenshotsEnabled, screenshotLimit, activeWindowOnly))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                screenshotsEnabled = dialog.ScreenshotEnabled; screenshotLimit = dialog.ScreenshotLimit; activeWindowOnly=dialog.ActiveWindowOnly;Theme.Switch(this,dialog.DarkMode);
                try
                {
                    string root = Path.GetDirectoryName(sessionsRoot);
                    File.WriteAllLines(Path.Combine(root, "settings.txt"), new string[] { screenshotsEnabled.ToString(), screenshotLimit.ToString(), activeWindowOnly.ToString(),Theme.Dark.ToString() });
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
                { screenshotsEnabled = enabled; screenshotLimit = count; bool windowOnly; if(values.Length>2 && Boolean.TryParse(values[2],out windowOnly)) activeWindowOnly=windowOnly;bool darkMode;if(values.Length>3&&Boolean.TryParse(values[3],out darkMode))Theme.SetDark(darkMode); }
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
            Native.UnregisterHotKey(Handle,8101);Native.UnregisterHotKey(Handle,8102);
            if (worker.Join(1600)) CleanupSession();
        }
        void ShowError(string title, Exception error)
        { MessageBox.Show(this, error.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

}
