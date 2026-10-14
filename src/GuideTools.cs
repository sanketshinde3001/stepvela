using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
namespace StepVela {
    static class Images {
        public static Bitmap Load(string path) {
            FileInfo file=new FileInfo(path); if(file.Length>16*1024*1024) throw new InvalidDataException("Choose an image smaller than 16 MB.");
            using(FileStream stream=File.OpenRead(path)) using(Image image=Image.FromStream(stream,false,true)) {
                if(image.Width<1 || image.Height<1 || image.Width>8192 || image.Height>8192 || (long)image.Width*image.Height>16000000) throw new InvalidDataException("Image dimensions are too large.");
                if(image.RawFormat.Guid!=ImageFormat.Png.Guid && image.RawFormat.Guid!=ImageFormat.Jpeg.Guid && image.RawFormat.Guid!=ImageFormat.Bmp.Guid) throw new InvalidDataException("Choose a PNG, JPEG, or BMP image.");
                double scale=Math.Min(1.0,1600.0/Math.Max(image.Width,image.Height));
                Bitmap result=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)),PixelFormat.Format24bppRgb);
                using(Graphics graphics=Graphics.FromImage(result)) graphics.DrawImage(image,new Rectangle(0,0,result.Width,result.Height));
                return result;
            }
        }
        public static void Replace(Bitmap bitmap,string destination) {
            string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { ScreenCapture.SaveJpeg(bitmap,temporary); if(File.Exists(destination)) File.Replace(temporary,destination,null); else File.Move(temporary,destination); }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void Redact(Bitmap bitmap,Rectangle region) {
            region=Rectangle.Intersect(new Rectangle(Point.Empty,bitmap.Size),region);
            if(region.Width<=0 || region.Height<=0) throw new InvalidOperationException("Select an area first.");
            using(Graphics graphics=Graphics.FromImage(bitmap)) using(Brush brush=new SolidBrush(Color.FromArgb(18,23,31))) graphics.FillRectangle(brush,region);
        }
    }
    static class GuideTools {
        public static void Renumber(List<Step> steps) { for(int i=0;i<steps.Count;i++) steps[i].Number=i+1; }
        public static void Move(List<Step> steps,int index,int direction) {
            int other=index+direction; if(index<0||index>=steps.Count||other<0||other>=steps.Count) return;
            Step step=steps[index]; steps[index]=steps[other]; steps[other]=step; Renumber(steps);
        }
    }
    static class ProjectStore {
        public static Report Load(string path,string destination) {
            if(new FileInfo(path).Length>256L*1024*1024) throw new InvalidDataException("This guide exceeds the 256 MB limit.");
            using(ZipArchive archive=ZipFile.OpenRead(path)) {
                if(archive.Entries.Count>12000 || archive.Entries.Select(e=>e.FullName).Distinct(StringComparer.Ordinal).Count()!=archive.Entries.Count) throw new InvalidDataException("Invalid guide archive.");
                if(archive.Entries.Sum(e=>e.Length)>256L*1024*1024) throw new InvalidDataException("Guide contents exceed the size limit.");
                ZipArchiveEntry data=archive.GetEntry("data.json"); if(data==null) throw new InvalidDataException("This is not a StepVela guide.");
                byte[] bytes=Read(data,16*1024*1024); Report report;
                using(MemoryStream stream=new MemoryStream(bytes)) report=(Report)new DataContractJsonSerializer(typeof(Report)).ReadObject(stream);
                if(report==null || report.Format!="StepVela/1" || report.Steps==null || report.Steps.Count>10000) throw new InvalidDataException("Unsupported or invalid guide format.");
                if((report.Title??"").Length>512 || report.ScreenshotLimit<1 || report.ScreenshotLimit>999) throw new InvalidDataException("Invalid guide settings.");
                long stored=0;
                foreach(Step step in report.Steps) {
                    if(step==null || (step.Description??"").Length>4096 || (step.Comment??"").Length>10000 || (step.WindowTitle??"").Length>4096 || (step.ControlName??"").Length>4096) throw new InvalidDataException("Invalid step metadata.");
                    if(String.IsNullOrEmpty(step.Screenshot)) continue;
                    if(!Regex.IsMatch(step.Screenshot,@"\Ascreenshots/step-[0-9]{4,5}\.(png|jpg)\z")) throw new InvalidDataException("Invalid image path in guide.");
                    ZipArchiveEntry image=archive.GetEntry(step.Screenshot); if(image==null) throw new InvalidDataException("A guide image is missing.");
                    byte[] imageBytes=Read(image,16*1024*1024); stored+=imageBytes.Length; if(stored>256L*1024*1024) throw new InvalidDataException("Guide image limit exceeded.");
                    string staging=Path.Combine(destination,Guid.NewGuid().ToString("N")+Path.GetExtension(step.Screenshot));
                    File.WriteAllBytes(staging,imageBytes);
                    try {
                        using(Bitmap decoded=Images.Load(staging)) {
                            step.LocalScreenshot=Path.Combine(destination,Guid.NewGuid().ToString("N")+".jpg"); Images.Replace(decoded,step.LocalScreenshot);
                        }
                    } finally { File.Delete(staging); }
                }
                GuideTools.Renumber(report.Steps); return report;
            }
        }
        static byte[] Read(ZipArchiveEntry entry,int limit) {
            if(entry.Length>limit) throw new InvalidDataException("A guide entry is too large.");
            using(Stream source=entry.Open()) using(MemoryStream output=new MemoryStream()) {
                byte[] buffer=new byte[65536]; int count;
                while((count=source.Read(buffer,0,buffer.Length))>0) { if(output.Length+count>limit) throw new InvalidDataException("Guide entry exceeds its limit."); output.Write(buffer,0,count); }
                return output.ToArray();
            }
        }
    }
}
