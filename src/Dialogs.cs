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
    sealed class SettingsForm : Form {
        readonly CheckBox screenshots; readonly NumericUpDown limit; readonly ComboBox area,appearance;
        public bool ScreenshotEnabled {get{return screenshots.Checked;}}
        public int ScreenshotLimit {get{return (int)limit.Value;}}
        public bool ActiveWindowOnly {get{return area.SelectedIndex==0;}}
        public bool DarkMode {get{return appearance.SelectedIndex==1;}}
        public SettingsForm(bool enabled,int count,bool windowOnly) {
            Theme.Form(this); Text="Settings"; ClientSize=new Size(460,376); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false;
            screenshots=new CheckBox {Text="Capture screenshots",Checked=enabled,AutoSize=true,Location=new Point(28,28)};
            Label areaLabel=new Label {Text="Capture area",AutoSize=true,Location=new Point(28,85),ForeColor=Theme.Muted};
            area=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Theme.Raised,ForeColor=Theme.Text,Location=new Point(235,80),Width=190};
            area.Items.AddRange(new object[]{"Active window","All displays"}); area.SelectedIndex=windowOnly?0:1;
            Label limitLabel=new Label {Text="Recent screenshots",AutoSize=true,Location=new Point(28,145),ForeColor=Theme.Muted};
            limit=new NumericUpDown {Minimum=1,Maximum=999,Value=count,BackColor=Theme.Raised,ForeColor=Theme.Text,Location=new Point(235,140),Width=190};
            Label appearanceLabel=new Label{Text="Appearance",AutoSize=true,ForeColor=Theme.Muted,Location=new Point(28,200)};
            appearance=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Theme.Raised,ForeColor=Theme.Text,Location=new Point(235,195),Width=190};appearance.Items.AddRange(new object[]{"Light","Dark"});appearance.SelectedIndex=Theme.Dark?1:0;
            Label privacy=new Label {Text="Images stay on this device. Review them before sharing.",Location=new Point(28,260),Size=new Size(400,40),ForeColor=Theme.Muted};
            Button save=Theme.Button("Done",110,true); save.Location=new Point(315,318); save.DialogResult=DialogResult.OK;
            Controls.AddRange(new Control[]{screenshots,areaLabel,area,limitLabel,limit,appearanceLabel,appearance,privacy,save}); AcceptButton=save;
        }
    }

    sealed class HighlightView : Control
    {
        public Bitmap Screenshot;
        public Rectangle Selection;
        bool dragging;
        Point anchor;
        public HighlightView()
        { DoubleBuffered = true; BackColor = Theme.Surface; Cursor = Cursors.Cross; }
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
            Text = "Add a note"; StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(620, 480); ClientSize = new Size(860, 620);
            Theme.Form(this); MinimizeBox = false;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Label instruction = new Label { Text = screenshot == null ? "Add a note" : "Highlight an area and add a note", Dock = DockStyle.Fill, AutoSize = true };
            preview = new HighlightView { Screenshot = screenshot, Dock = DockStyle.Fill, Visible = screenshot != null };
            comment = Theme.Input(true); comment.Dock=DockStyle.Fill; comment.ScrollBars=ScrollBars.Vertical; comment.Margin=new Padding(0,12,0,0);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            Button add=Theme.Button("Add note",135,true);
            add.Click += delegate { if (String.IsNullOrWhiteSpace(CommentText)) { comment.Focus(); return; } DialogResult = DialogResult.OK; Close(); };
            Button cancel=Theme.Button("Cancel",95); cancel.DialogResult=DialogResult.Cancel;
            Button clear=Theme.Button("Clear area",125); clear.Enabled=screenshot!=null;
            clear.Click += delegate { preview.Selection = Rectangle.Empty; preview.Invalidate(); };
            buttons.Controls.AddRange(new Control[] { add, cancel, clear });
            layout.Controls.Add(instruction, 0, 0); layout.Controls.Add(preview, 0, 1); layout.Controls.Add(comment, 0, 2); layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout); CancelButton = cancel; Shown += delegate { comment.Focus(); };
        }
    }

}
