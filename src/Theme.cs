using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StepVela {
    static class Theme {
        public static Color Background=Color.FromArgb(249,248,246), Surface=Color.White, Raised=Color.FromArgb(244,240,234), Border=Color.FromArgb(228,224,218), Text=Color.FromArgb(39,37,34), Muted=Color.FromArgb(117,112,105), Accent=Color.FromArgb(128,91,60);
        public static bool Dark;
        public static readonly Font SmallBold=new Font("Segoe UI",9,FontStyle.Bold);
        public static void SetDark(bool dark) {
            Dark=dark;
            Background=dark?Color.FromArgb(25,24,23):Color.FromArgb(249,248,246);
            Surface=dark?Color.FromArgb(33,32,30):Color.White;
            Raised=dark?Color.FromArgb(46,42,38):Color.FromArgb(244,240,234);
            Border=dark?Color.FromArgb(64,60,55):Color.FromArgb(228,224,218);
            Text=dark?Color.FromArgb(237,234,229):Color.FromArgb(39,37,34);
            Muted=dark?Color.FromArgb(177,170,160):Color.FromArgb(117,112,105);
            Accent=dark?Color.FromArgb(141,101,69):Color.FromArgb(128,91,60);
        }
        public static void Switch(Form form,bool dark) {
            Color[] old=new Color[]{Background,Surface,Raised,Border,Text,Muted,Accent};SetDark(dark);
            Color[] current=new Color[]{Background,Surface,Raised,Border,Text,Muted,Accent};
            Remap(form,old,current);ApplyTitle(form);
        }
        static void Remap(Control control,Color[] old,Color[] current) {
            for(int i=0;i<old.Length;i++){if(control.BackColor==old[i]){control.BackColor=current[i];break;}}
            for(int i=0;i<old.Length;i++){if(control.ForeColor==old[i]){control.ForeColor=current[i];break;}}
            foreach(Control child in control.Controls)Remap(child,old,current);control.Invalidate();
        }
        public static GraphicsPath Round(RectangleF r,float radius) {
            GraphicsPath p=new GraphicsPath(); float d=radius*2;
            p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        public static void Form(Form form) {
            form.BackColor=Background; form.ForeColor=Text; form.Font=new Font("Segoe UI",10); form.StartPosition=FormStartPosition.CenterParent;
            form.HandleCreated+=delegate {ApplyTitle(form);};
        }
        static void ApplyTitle(Form form) {try{int enabled=Dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref enabled,4);}catch{}}
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        public static TextBox Input(bool multi=false) { return new TextBox { BackColor=Raised,ForeColor=Text,BorderStyle=BorderStyle.FixedSingle,Multiline=multi,Font=new Font("Segoe UI",11),MaxLength=10000 }; }
        public static Button Button(string text,int width=110,bool primary=false) { return new VelaButton { Text=text,Width=width,Height=36,Primary=primary,Margin=new Padding(0,0,8,0),AccessibleName=text }; }
        public static void DialogButtons(Form f, params Button[] buttons) { foreach(Button b in buttons) { b.Font=new Font("Segoe UI",10); } }
    }
    sealed class VelaButton : Button {
        public bool Primary; bool hover;
        public VelaButton() { SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true); FlatStyle=FlatStyle.Flat; BackColor=Theme.Background; Cursor=Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent==null?Theme.Background:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=!Enabled?Theme.Raised:Primary?Theme.Accent:(hover?Theme.Raised:Theme.Surface);
            using(GraphicsPath path=Theme.Round(new RectangleF(0,0,Width-1,Height-1),9)) {
                using(Brush brush=new SolidBrush(fill)) e.Graphics.FillPath(brush,path);
                if(!Primary) using(Pen pen=new Pen(Focused?Theme.Accent:Theme.Border)) e.Graphics.DrawPath(pen,path);
            }
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,!Enabled?Theme.Muted:Primary?Color.White:Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        }
    }
    sealed class CardPanel : Panel {
        public CardPanel() { DoubleBuffered=true; BackColor=Theme.Surface; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(GraphicsPath path=Theme.Round(new RectangleF(0,0,Width-1,Height-1),10))
            using(Pen pen=new Pen(Theme.Border)) e.Graphics.DrawPath(pen,path);
        }
    }
    sealed class StepList : ListView {
        readonly ImageList rowHeight=new ImageList();
        public StepList() {
            DoubleBuffered=true; Dock=DockStyle.Fill; View=View.Details; OwnerDraw=true; HeaderStyle=ColumnHeaderStyle.None; FullRowSelect=true; HideSelection=false; MultiSelect=false; BorderStyle=BorderStyle.None; BackColor=Theme.Surface; ForeColor=Theme.Text;
            rowHeight.ImageSize=new Size(1,78); SmallImageList=rowHeight; Columns.Add("Steps",300); AccessibleName="Guide steps";
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if(Columns.Count>0) Columns[0].Width=Math.Max(50,ClientSize.Width-2); }
        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e) { }
        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) { }
        protected override void OnDrawItem(DrawListViewItemEventArgs e) {
            Step step=e.Item.Tag as Step; if(step==null) return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Rectangle r=new Rectangle(e.Bounds.X+8,e.Bounds.Y+4,ClientSize.Width-16,e.Bounds.Height-8);
            using(GraphicsPath p=Theme.Round(r,5)) using(Brush b=new SolidBrush(e.Item.Selected?Theme.Raised:Theme.Surface)) e.Graphics.FillPath(b,p);
            using(Brush b=new SolidBrush(e.Item.Selected?Theme.Accent:Theme.Border)) e.Graphics.FillEllipse(b,r.X+12,r.Y+20,28,28);
            TextRenderer.DrawText(e.Graphics,step.Number.ToString(),Theme.SmallBold,new Rectangle(r.X+12,r.Y+20,28,28),e.Item.Selected?Color.White:Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics,step.Description??"Untitled step",Font,new Rectangle(r.X+52,r.Y+14,r.Width-64,25),Theme.Text,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
            string meta=(step.Action??"Step")+(step.Redacted?" · Redacted":"");
            TextRenderer.DrawText(e.Graphics,meta,Font,new Rectangle(r.X+52,r.Y+40,r.Width-64,23),Theme.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        }
        protected override void Dispose(bool disposing) { if(disposing) rowHeight.Dispose(); base.Dispose(disposing); }
    }
    sealed class PreviewSurface : PictureBox {
        public string EmptyTitle="No steps yet",EmptyCaption="Record a workflow or add a step.";
        public PreviewSurface() { DoubleBuffered=true; Dock=DockStyle.Fill; SizeMode=PictureBoxSizeMode.Zoom; BackColor=Theme.Background; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); if(Image!=null) return;
            Graphics g=e.Graphics;int y=Height/2-30;
            using(Font title=new Font("Segoe UI",14,FontStyle.Regular)) TextRenderer.DrawText(g,EmptyTitle,title,new Rectangle(10,y,Width-20,35),Theme.Text,TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g,EmptyCaption,Font,new Rectangle(10,y+42,Width-20,25),Theme.Muted,TextFormatFlags.HorizontalCenter);
        }
    }
}
