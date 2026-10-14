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

}
