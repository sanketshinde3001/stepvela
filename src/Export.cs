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
        public static void Save(string destination, List<Step> steps, bool enabled, int limit, DateTimeOffset? started = null, DateTimeOffset? stopped = null, int skipped = 0, string title = "Untitled guide")
        {
            destination = Path.GetFullPath(destination);
            string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Report report = new Report { Title=String.IsNullOrWhiteSpace(title)?"Untitled guide":title, GeneratedAt = DateTimeOffset.Now.ToString("o"), ScreenshotLimit = limit, ScreenshotsEnabled = enabled, Steps = steps,
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
                            step.Screenshot = "screenshots/step-" + step.Number.ToString("D4") + Path.GetExtension(step.LocalScreenshot).ToLowerInvariant();
                            zip.CreateEntryFromFile(step.LocalScreenshot, step.Screenshot, CompressionLevel.Optimal);
                        }
                    }
                    ZipArchiveEntry data = zip.CreateEntry("data.json", CompressionLevel.Optimal);
                    using (Stream json = data.Open()) new DataContractJsonSerializer(typeof(Report)).WriteObject(json, report);
                    ZipArchiveEntry html = zip.CreateEntry("report.html", CompressionLevel.Optimal);
                    using (StreamWriter writer = new StreamWriter(html.Open(), new UTF8Encoding(false))) writer.Write(Html(report));
                    ZipArchiveEntry markdown=zip.CreateEntry("guide.md",CompressionLevel.Optimal);
                    using(StreamWriter writer=new StreamWriter(markdown.Open(),new UTF8Encoding(false))) writer.Write(Markdown(report));
                    ZipArchiveEntry help = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
                    using (StreamWriter writer = new StreamWriter(help.Open(), new UTF8Encoding(false)))
                        writer.Write("Extract this ZIP, then open report.html in a browser. data.json contains structured step metadata. All assets are local; no internet connection is needed.  Screenshots and accessibility labels may contain private information; review before sharing.\r\n");
                }
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static string Md(string text) {
            string value=Escape(text).Replace("\\","\\\\");
            foreach(char c in new char[]{'[',']','*','_','#','`'}) value=value.Replace(c.ToString(),"\\"+c);
            return value;
        }
        public static string Markdown(Report report) {
            StringBuilder text=new StringBuilder("# "+Md(report.Title)+"\n\n");
            if(report.SkippedEventCount>0) text.Append("> ").Append(report.SkippedEventCount).Append(" input events were skipped during capture.\n\n");
            foreach(Step step in report.Steps) {
                text.Append("## ").Append(step.Number).Append(". ").Append(Md(step.Description)).Append("\n\n");
                if(!String.IsNullOrEmpty(step.Comment)) text.Append(Md(step.Comment)).Append("\n\n");
                if(!String.IsNullOrEmpty(step.Screenshot)) text.Append("![Step ").Append(step.Number).Append("](").Append(step.Screenshot).Append(")\n\n");
            }
            return text.Append("---\nCreated with StepVela.\n").ToString();
        }
        public static string Html(Report report)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>").Append(Escape(report.Title)).Append("</title><style>");
            html.Append("*{box-sizing:border-box}body{margin:0;background:#f9f8f6;color:#272522;font:16px/1.6 system-ui,-apple-system,Segoe UI,sans-serif}main{max-width:1100px;margin:auto;padding:40px 24px}header{padding:32px;background:#292622;color:white;border-radius:16px;margin-bottom:24px}.eyebrow{font-size:12px;letter-spacing:.16em;text-transform:uppercase;color:#d1b99e}h1{margin:8px 0;font-size:34px}h2{margin:0;font-size:20px}header p{color:#e9e1d7;margin:6px 0}.step{background:white;border:1px solid #e4e0da;border-radius:12px;margin:18px 0;overflow:hidden}.body{padding:24px}.number{display:inline-block;background:#f4f0ea;color:#805b3c;padding:3px 10px;border-radius:6px;margin-right:10px;font-size:14px}.meta{font-size:13px;color:#757069;margin-top:12px;overflow-wrap:anywhere}.shot{display:block;width:100%;height:auto;border-top:1px solid #e4e0da}.comment{white-space:pre-wrap;overflow-wrap:anywhere;background:#fff4d4;border-left:4px solid #d49d22;padding:12px 16px;margin-top:18px}.note{color:#757069;font-size:13px}.warning{color:#8b5300}footer{padding:22px 0;color:#757069;font-size:13px}@media print{body{background:white}main{padding:0}.step{break-inside:avoid}header{color:#292622;background:white;border:1px solid #ccd5e0}header p,.eyebrow{color:#52637a}}");
            html.Append("</style></head><body><main><header><div class=\"eyebrow\">StepVela</div><h1>").Append(Escape(report.Title)).Append("</h1><p>");
            html.Append(report.Steps.Count).Append(" steps · Generated ").Append(Escape(report.GeneratedAt));
            html.Append("</p>");
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
            html.Append("<footer>Created with StepVela. Printable key content is not stored. Review images and labels before sharing.</footer></main></body></html>");
            return html.ToString();
        }
    }

}
