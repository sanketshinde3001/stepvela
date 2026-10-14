using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace StepVela {
    sealed class ImageEditor : Form {
        readonly HighlightView canvas;
        readonly ComboBox tool;
        Bitmap undo;
        bool undoRedacted;
        public bool Redacted;
        public bool Edited;
        public Bitmap Result { get { return canvas.Screenshot; } }
        public ImageEditor(Bitmap image,bool redacted=false) {
            Redacted=redacted;
            Theme.Form(this); Text="Edit image"; Size=new Size(960,700); MinimumSize=new Size(650,480); MinimizeBox=false;
            TableLayoutPanel layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(24),RowCount=3,ColumnCount=1};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
            FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
            tool=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=155,Height=38,BackColor=Theme.Raised,ForeColor=Theme.Text};
            tool.Items.AddRange(new object[]{"Highlight","Redact","Crop"}); tool.SelectedIndex=0;
            Button apply=Theme.Button("Apply area",110); apply.Click+=delegate{ApplyArea();};
            Button revert=Theme.Button("Undo",80); revert.Click+=delegate { if(undo==null)return; Bitmap previous=canvas.Screenshot; canvas.Screenshot=undo; undo=null; previous.Dispose(); Redacted=undoRedacted; canvas.Selection=Rectangle.Empty; canvas.Invalidate(); };
            Label hint=new Label {Text="Drag to select an area",ForeColor=Theme.Muted,AutoSize=true,Padding=new Padding(10,9,0,0)};
            actions.Controls.AddRange(new Control[]{tool,apply,revert,hint});
            canvas=new HighlightView {Dock=DockStyle.Fill,Screenshot=image,BackColor=Theme.Surface};
            FlowLayoutPanel bottom=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,16,0,0)};
            Button save=Theme.Button("Save changes",140,true); save.DialogResult=DialogResult.OK;
            Button cancel=Theme.Button("Cancel",90); cancel.DialogResult=DialogResult.Cancel;
            bottom.Controls.AddRange(new Control[]{save,cancel}); layout.Controls.Add(actions,0,0); layout.Controls.Add(canvas,0,1); layout.Controls.Add(bottom,0,2); Controls.Add(layout); CancelButton=cancel;
        }
        void ApplyArea() {
            Rectangle area=canvas.Selection; if(area.Width<3||area.Height<3) return;
            if(undo!=null) undo.Dispose(); undo=new Bitmap(canvas.Screenshot); undoRedacted=Redacted;
            if(tool.SelectedIndex==2) {
                Bitmap crop=canvas.Screenshot.Clone(area,System.Drawing.Imaging.PixelFormat.Format24bppRgb); canvas.Screenshot.Dispose(); canvas.Screenshot=crop;
            } else if(tool.SelectedIndex==1) { Images.Redact(canvas.Screenshot,area); Redacted=true; }
            else using(Graphics graphics=Graphics.FromImage(canvas.Screenshot)) using(Pen pen=new Pen(Theme.Accent,4)) using(Brush brush=new SolidBrush(Color.FromArgb(55,Theme.Accent))) { graphics.FillRectangle(brush,area); graphics.DrawRectangle(pen,area); }
            Edited=true; canvas.Selection=Rectangle.Empty; canvas.Invalidate();
        }
        protected override void Dispose(bool disposing) { if(disposing) { if(undo!=null)undo.Dispose(); if(canvas!=null&&canvas.Screenshot!=null)canvas.Screenshot.Dispose(); } base.Dispose(disposing); }
    }
}
