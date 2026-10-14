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

}
