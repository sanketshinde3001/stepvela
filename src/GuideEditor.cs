using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace StepVela {
    sealed partial class RecorderForm {
        TextBox guideTitle,stepTitle,stepNotes;
        Button openGuide,saveGuide,manualStep,editImage,replaceImage,moveUp,moveDown,removeStep;
        bool bindingEditor;
        string projectPath;
        TableLayoutPanel guideEditorPanel;
        Panel titleField,notesField;
        void BuildWorkspace() {
            TableLayoutPanel shell=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(26,18,26,20),ColumnCount=1,RowCount=4};
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute,66)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute,54)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute,65)); shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Panel header=new Panel {Dock=DockStyle.Fill};
            Label brand=new Label {Text="StepVela",Font=new Font("Segoe UI",22,FontStyle.Bold),ForeColor=Theme.Text,AutoSize=true,Location=new Point(0,0)};
            Label tagline=new Label {Text="DYNAMIC STEP RECORDER",Font=Theme.SmallBold,ForeColor=Theme.Muted,AutoSize=true,Location=new Point(3,42)};
            FlowLayoutPanel files=new FlowLayoutPanel {Dock=DockStyle.Right,Width=310,Padding=new Padding(0,8,0,0),WrapContents=false};
            openGuide=Theme.Button("Open",78); saveGuide=Theme.Button("Save guide",105); settings=Theme.Button("Settings",82);
            files.Controls.AddRange(new Control[]{openGuide,saveGuide,settings}); header.Controls.AddRange(new Control[]{brand,tagline,files});
            Panel titleRow=new Panel {Dock=DockStyle.Fill}; guideTitle=Theme.Input(); guideTitle.BorderStyle=BorderStyle.None; guideTitle.BackColor=Theme.Background; guideTitle.Font=new Font("Segoe UI",17); guideTitle.Text="Untitled guide"; guideTitle.MaxLength=512; guideTitle.AccessibleName="Guide title"; guideTitle.Location=new Point(2,8); guideTitle.Width=530;
            stateLabel=new Label {AutoSize=true,Location=new Point(650,15),Font=Theme.SmallBold}; countLabel=new Label {AutoSize=true,ForeColor=Theme.Muted,Location=new Point(650,15)};
            titleRow.Controls.AddRange(new Control[]{guideTitle,stateLabel});
            titleRow.Resize+=delegate { guideTitle.Width=Math.Max(320,titleRow.Width-220); stateLabel.Location=new Point(titleRow.Width-150,15); };
            FlowLayoutPanel toolbar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,4,0,0)};
            start=Theme.Button("● Record",120,true); pause=Theme.Button("Pause",86); stop=Theme.Button("Finish",86); comment=Theme.Button("Add note",105); save=Theme.Button("Export",102,true);
            toolbar.Controls.AddRange(new Control[]{start,pause,stop,comment,save});
            Panel metrics=new Panel {Width=240,Height=40}; countLabel.Dock=DockStyle.Fill; countLabel.Padding=new Padding(12,10,0,0); metrics.Controls.Add(countLabel); toolbar.Controls.Add(metrics);
            SplitContainer body=new SplitContainer {Size=new Size(1128,570),SplitterDistance=322,SplitterWidth=18,Dock=DockStyle.Fill,BackColor=Theme.Background,Panel1MinSize=260,Panel2MinSize=470};
            Panel rail=new Panel {Dock=DockStyle.Fill,BackColor=Theme.Surface,Padding=new Padding(8)};
            Panel railHeader=new Panel {Dock=DockStyle.Top,Height=52}; Label stepsLabel=new Label {Text="Steps",AutoSize=true,ForeColor=Theme.Muted,Location=new Point(14,16)};
            manualStep=Theme.Button("+ Add step",120); manualStep.Dock=DockStyle.Right; railHeader.Controls.AddRange(new Control[]{stepsLabel,manualStep});
            list=new StepList(); rail.Controls.Add(list); rail.Controls.Add(railHeader); body.Panel1.Controls.Add(rail);
            TableLayoutPanel editor=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(18),BackColor=Theme.Surface,ColumnCount=1,RowCount=5};guideEditorPanel=editor;
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute,50)); editor.RowStyles.Add(new RowStyle(SizeType.Percent,100)); editor.RowStyles.Add(new RowStyle(SizeType.Absolute,38)); editor.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); editor.RowStyles.Add(new RowStyle(SizeType.Absolute,100));
            FlowLayoutPanel tools=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false}; editImage=Theme.Button("Edit image",108); replaceImage=Theme.Button("Replace",88); moveUp=Theme.Button("↑",42); moveDown=Theme.Button("↓",42); removeStep=Theme.Button("Remove",82);
            moveUp.AccessibleName="Move step up"; moveDown.AccessibleName="Move step down"; tools.Controls.AddRange(new Control[]{editImage,replaceImage,moveUp,moveDown,removeStep});
            preview=new PreviewSurface(); detail=new Label {Dock=DockStyle.Fill,ForeColor=Theme.Muted,Padding=new Padding(0,10,0,0),AutoEllipsis=true};
            stepTitle=Theme.Input(); stepTitle.Dock=DockStyle.Fill; stepTitle.MaxLength=4096; stepTitle.AccessibleName="Step title";
            stepNotes=Theme.Input(true); stepNotes.Dock=DockStyle.Fill; stepNotes.ScrollBars=ScrollBars.Vertical; stepNotes.AccessibleName="Step description";
            titleField=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,22,0,8)};Label titleLabel=new Label{Text="TITLE",ForeColor=Theme.Muted,Font=Theme.SmallBold,AutoSize=true,Location=Point.Empty};titleField.Controls.Add(stepTitle);titleField.Controls.Add(titleLabel);
            notesField=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,22,0,0)};Label notesLabel=new Label{Text="DESCRIPTION",ForeColor=Theme.Muted,Font=Theme.SmallBold,AutoSize=true,Location=Point.Empty};notesField.Controls.Add(stepNotes);notesField.Controls.Add(notesLabel);
            editor.Controls.Add(tools,0,0); editor.Controls.Add(preview,0,1); editor.Controls.Add(detail,0,2); editor.Controls.Add(titleField,0,3); editor.Controls.Add(notesField,0,4); body.Panel2.Controls.Add(editor);
            shell.Controls.Add(header,0,0); shell.Controls.Add(titleRow,0,1); shell.Controls.Add(toolbar,0,2); shell.Controls.Add(body,0,3); Controls.Add(shell);
            openGuide.Click+=delegate { OpenProject(); }; saveGuide.Click+=delegate { SaveProject(); }; manualStep.Click+=delegate { AddManualStep(); }; editImage.Click+=delegate { EditSelectedImage(); }; replaceImage.Click+=delegate { ReplaceSelectedImage(); };
            moveUp.Click+=delegate { MoveSelected(-1); }; moveDown.Click+=delegate { MoveSelected(1); }; removeStep.Click+=delegate { RemoveSelected(); };
            guideTitle.TextChanged+=delegate { if(!bindingEditor&&steps.Count>0) dirty=true; };
            stepTitle.TextChanged+=delegate { Step step=SelectedStep(); if(bindingEditor||step==null)return; step.Description=stepTitle.Text; dirty=true; list.Invalidate(); };
            stepNotes.TextChanged+=delegate { Step step=SelectedStep(); if(bindingEditor||step==null)return; step.Comment=stepNotes.Text; dirty=true; };
        }
        Step SelectedStep() {return list.SelectedItems.Count==0?null:list.SelectedItems[0].Tag as Step;}
        void BindStep(Step step) {bindingEditor=true; try {stepTitle.Text=step==null?"":step.Description??"";stepNotes.Text=step==null?"":step.Comment??"";}finally{bindingEditor=false;}}
        void SetGuideTitle(string title) {bindingEditor=true;try{guideTitle.Text=title??"Untitled guide";}finally{bindingEditor=false;}}
        void UpdateEditorControls(bool editable) {
            Step step=SelectedStep(); bool selected=editable&&step!=null; openGuide.Enabled=editable; saveGuide.Enabled=editable&&steps.Count>0; manualStep.Enabled=editable; guideTitle.ReadOnly=!editable;
            titleField.Visible=notesField.Visible=step!=null;guideEditorPanel.RowStyles[3].Height=step==null?0:64;guideEditorPanel.RowStyles[4].Height=step==null?0:116;guideEditorPanel.Controls[0].Visible=step!=null;guideEditorPanel.RowStyles[0].Height=step==null?0:50;
            stepTitle.ReadOnly=!selected; stepNotes.ReadOnly=!selected; editImage.Enabled=selected&&!String.IsNullOrEmpty(step.LocalScreenshot)&&File.Exists(step.LocalScreenshot); replaceImage.Enabled=selected;
            moveUp.Enabled=selected&&step.Number>1; moveDown.Enabled=selected&&step.Number<steps.Count; removeStep.Enabled=selected;
        }
        void EnsureFolder() {
            if(String.IsNullOrEmpty(sessionFolder)) {sessionFolder=Path.Combine(sessionsRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sessionFolder);}
            if(session.State==RecordingState.Ready) {session.Start();session.Stop();startedAt=DateTimeOffset.Now;stoppedAt=startedAt;activeScreenshots=screenshotsEnabled;activeLimit=screenshotLimit;}
        }
        void RefreshSteps(int selected) {
            bindingEditor=true;list.BeginUpdate();try{list.Items.Clear();GuideTools.Renumber(steps);foreach(Step step in steps){ListViewItem row=new ListViewItem(step.Description??""){Tag=step};list.Items.Add(row);}if(selected>=0&&selected<list.Items.Count)list.Items[selected].Selected=true;}finally{list.EndUpdate();bindingEditor=false;}
            ShowSelection();UpdateControls();
        }
        void AddManualStep() {try{EnsureFolder();Step step=new Step{Timestamp=DateTimeOffset.Now.ToString("o"),Action="Manual",Description="New step",Comment=""};steps.Add(step);dirty=true;RefreshSteps(steps.Count-1);stepTitle.Focus();stepTitle.SelectAll();}catch(Exception error){ShowError("Could not add step",error);}}
        void MoveSelected(int direction) {Step step=SelectedStep();if(step==null)return;int index=steps.IndexOf(step);GuideTools.Move(steps,index,direction);dirty=true;RefreshSteps(Math.Max(0,Math.Min(steps.Count-1,index+direction)));}
        void RemoveSelected() {Step step=SelectedStep();if(step==null)return;if(MessageBox.Show(this,"Remove this step?","StepVela",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;int index=steps.IndexOf(step);steps.Remove(step);dirty=true;RefreshSteps(Math.Min(index,steps.Count-1));}
        void ReplaceSelectedImage() {
            Step step=SelectedStep();if(step==null)return;
            using(OpenFileDialog dialog=new OpenFileDialog{Title="Replace screenshot",Filter="Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{EnsureFolder();using(Bitmap image=Images.Load(dialog.FileName)){string path=Path.Combine(sessionFolder,Guid.NewGuid().ToString("N")+".jpg");Images.Replace(image,path);step.LocalScreenshot=path;step.ScreenshotOmitted=false;step.Redacted=false;step.Highlight=null;step.DesktopBounds=null;step.ClickX=step.ClickY=null;step.CaptureWarning=null;}dirty=true;ShowSelection();UpdateControls();}catch(Exception error){ShowError("Could not replace image",error);}
            }
        }
        void EditSelectedImage() {
            Step step=SelectedStep();if(step==null||String.IsNullOrEmpty(step.LocalScreenshot))return;
            try{using(ImageEditor editor=new ImageEditor(Images.Load(step.LocalScreenshot),step.Redacted)){if(editor.ShowDialog(this)!=DialogResult.OK||!editor.Edited)return;string path=Path.Combine(sessionFolder,Guid.NewGuid().ToString("N")+".jpg");Images.Replace(editor.Result,path);string old=step.LocalScreenshot;step.LocalScreenshot=path;step.Redacted=editor.Redacted;step.Highlight=null;step.DesktopBounds=null;if(File.Exists(old))File.Delete(old);dirty=true;ShowSelection();list.Invalidate();UpdateControls();}}catch(Exception error){ShowError("Could not edit image",error);}
        }
        void SaveProject() {
            using(SaveFileDialog dialog=new SaveFileDialog{Title="Save guide",Filter="StepVela guide (*.stepvela)|*.stepvela",DefaultExt="stepvela",AddExtension=true,OverwritePrompt=true,FileName=String.IsNullOrEmpty(projectPath)?"my-guide.stepvela":Path.GetFileName(projectPath)}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                try{UseWaitCursor=true;Exporter.Save(dialog.FileName,steps,activeScreenshots,activeLimit,startedAt,stoppedAt,dropped,guideTitle.Text);projectPath=dialog.FileName;dirty=false;detail.Text="Saved "+Path.GetFileName(projectPath);}catch(Exception error){ShowError("Could not save guide",error);}finally{UseWaitCursor=false;}
            }
        }
        void OpenProject() {
            if(dirty&&steps.Count>0&&MessageBox.Show(this,"Open another guide and discard unsaved changes?","StepVela",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            using(OpenFileDialog dialog=new OpenFileDialog{Title="Open guide",Filter="StepVela guides (*.stepvela;*.zip)|*.stepvela;*.zip",CheckFileExists=true}) {
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                string folder=Path.Combine(sessionsRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                try{
                    UseWaitCursor=true;Report report=ProjectStore.Load(dialog.FileName,folder);ClearPreview();CleanupSession();sessionFolder=folder;steps.Clear();steps.AddRange(report.Steps);SetGuideTitle(report.Title);activeScreenshots=report.ScreenshotsEnabled;activeLimit=report.ScreenshotLimit;dropped=report.SkippedEventCount;
                    DateTimeOffset time;startedAt=DateTimeOffset.TryParse(report.SessionStartedAt,out time)?time:DateTimeOffset.Now;stoppedAt=DateTimeOffset.TryParse(report.SessionStoppedAt,out time)?time:DateTimeOffset.Now;
                    if(session.State==RecordingState.Paused){RemoveHook();session.Stop();}if(session.State==RecordingState.Ready){session.Start();session.Stop();}
                    projectPath=dialog.FileName;dirty=false;RefreshSteps(steps.Count>0?0:-1);
                }catch(Exception error){try{Directory.Delete(folder,true);}catch{}ShowError("Could not open guide",error);}finally{UseWaitCursor=false;}
            }
        }
    }
}
