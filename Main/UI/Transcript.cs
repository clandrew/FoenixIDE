using System;
using System.IO;
using System.Collections.Generic;
using FoenixIDE.Processor;
using FoenixIDE.UI;
using System.Drawing;
using System.Windows.Forms;
using System.Text;

namespace FoenixIDE.Simulator.UI
{
    class TranscriptDebugLine
    {
    }

    class CPUTranscriptDebugLine  : TranscriptDebugLine
    {
        public int PC;
        byte[] instructionBytes;
        private string source; // Source assembly language
        private int A, X, Y;
        private FoenixIDE.Processor.Flags P;
        private string evaled = null; // Cache

        public CPUTranscriptDebugLine(int pc, int a, int x, int y, FoenixIDE.Processor.Flags p)
        {
            PC = pc;
            A = a;
            X = x;
            Y = y;
            P = p;
        }
        public void SetInstructionBytes(byte[] cmd)
        {
            instructionBytes = cmd;
        }

        public void SetSourceAssemblyLanguage(string value)
        {
            value = value.Trim(new char[] { ' ' });
            // Detect if the lines contains a label
            string[] tokens = value.Split();
            source = value;
        }

        override public string ToString()
        {
            if (evaled != null)
                return evaled;

            StringBuilder c = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                if (i < instructionBytes.Length)
                    c.Append(instructionBytes[i].ToString("X2")).Append(" ");
                else
                    c.Append("   ");
            }

            // Pad out source
            const int sourceColumnWidth = 12;
            StringBuilder sourceString = new StringBuilder();
            sourceString.Append(source);
            for (int i = 0; i < sourceColumnWidth - source.Length; ++i)
            {
                sourceString.Append(' ');
            }

            StringBuilder flagsString = new StringBuilder();
            flagsString.Append(P.Emulation ? 'E' : 'e');
            flagsString.Append(P.Negative ? 'N' : 'n');
            flagsString.Append(P.oVerflow ? 'V' : 'v');
            flagsString.Append(P.accumulatorShort ? 'M' : 'm');
            flagsString.Append(P.xRegisterShort ? 'X' : 'x');
            flagsString.Append(P.Decimal ? 'D' : 'd');
            flagsString.Append(P.IrqDisable ? 'I' : 'i');
            flagsString.Append(P.Zero ? 'Z' : 'z');
            flagsString.Append(P.Carry ? 'C' : 'c');

            evaled = string.Format("{0}  {1} {2}  {3} A:{4:X4} X:{5:X4} Y:{6:X4} P:{7}", PC.ToString("X6"), c.ToString(), sourceString.ToString(), null, A, X, Y, flagsString);

            return evaled;
        }
    }

    public enum WriteToTranscriptEnablement
    {
        Enabled,
        Disabled
    }

    class CPULogger : IDisposable
    {
        StreamWriter stream = null;
        int fileIndex = 0;
        int lineIndex = 0;

        const int lineLimit = 9000;

        public void Log(string line)
        {
            if (lineIndex == lineLimit)
            {
                FinalizeCurrentFile();
            }

            if (stream == null)
            {
                stream = new StreamWriter(GetFilename(fileIndex), false);
            }

            stream.WriteLine(line);
            lineIndex++;
        }

        private static string GetFilename(int fileIndex)
        {
            return "cpuLog_" + fileIndex.ToString("D4") + ".txt";
        }

        private void FinalizeCurrentFile()
        {
            stream.Dispose();
            stream = null;
            lineIndex = 0;
            fileIndex++;
        }

        public void Flush()
        {
            if (stream != null)
            {
                FinalizeCurrentFile();
            }
        }

        public void Dispose()
        {
            if (stream != null)
            {
                stream.Dispose();
            }
        }
    }

    public class Transcript
    {
        private List<TranscriptDebugLine> TranscriptLines = null;

        private CPULogger CpuLogger;

        FoenixSystem Kernel;

        // Backchannels to talk to UI
        private CPUWindow CpuWindow;
        private MainWindow MainWindow;
        private System.Windows.Forms.PictureBox DebugPanel; // For querying sizes
        private System.Windows.Forms.Label HeaderTextbox;
        private System.Windows.Forms.Button AddBPOverlayButton;
        private System.Windows.Forms.Button DeleteBPOverlayButton;
        private System.Windows.Forms.Button InspectOverlayButton;
        private System.Windows.Forms.Button LabelOverlayButton;

        // UI items we own
        public System.Windows.Forms.CheckBox CpuLogCheckBox;
        public System.Windows.Forms.ToolStripMenuItem TranscriptModeDebuggerToolStripMenuItem;
        public System.Windows.Forms.ToolStripMenuItem OpenExecutableFileCPULogToolStripMenuItem;

        public Transcript(
            CPUWindow cw,
            System.Windows.Forms.PictureBox dp,
            System.Windows.Forms.Label htb,
            System.Windows.Forms.Button addBPOverlayButton,
            System.Windows.Forms.Button deleteBPOverlayButton,
            System.Windows.Forms.Button inspectOverlayButton,
            System.Windows.Forms.Button labelOverlayButton)
        {
            CpuWindow = cw;
            DebugPanel = dp;
            HeaderTextbox = htb;
            AddBPOverlayButton = addBPOverlayButton;
            DeleteBPOverlayButton = deleteBPOverlayButton;
            InspectOverlayButton = inspectOverlayButton;
            LabelOverlayButton = labelOverlayButton;

            this.CpuLogCheckBox = new System.Windows.Forms.CheckBox();

            // 
            // cpuLogCheckBox
            // 
            this.CpuLogCheckBox.AutoSize = true;
            this.CpuLogCheckBox.BackColor = System.Drawing.SystemColors.Control;
            this.CpuLogCheckBox.Location = new System.Drawing.Point(280, 3);
            this.CpuLogCheckBox.Name = "cpuLogCheckBox";
            this.CpuLogCheckBox.Size = new System.Drawing.Size(69, 17);
            this.CpuLogCheckBox.TabIndex = 35;
            this.CpuLogCheckBox.Text = "CPU Log";
            this.CpuLogCheckBox.UseVisualStyleBackColor = false;
            this.CpuLogCheckBox.CheckedChanged += new System.EventHandler(this.cpuLogCheckBox_CheckedChanged);

            this.TranscriptModeDebuggerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            // 
            // transcriptModeDebuggerToolStripMenuItem
            // 
            this.TranscriptModeDebuggerToolStripMenuItem.CheckOnClick = true;
            this.TranscriptModeDebuggerToolStripMenuItem.Name = "TranscriptModeDebuggerToolStripMenuItem";
            this.TranscriptModeDebuggerToolStripMenuItem.Size = new System.Drawing.Size(216, 22);
            this.TranscriptModeDebuggerToolStripMenuItem.Text = "Transcript-Mode Debugger";
            this.TranscriptModeDebuggerToolStripMenuItem.Click += new System.EventHandler(this.TranscriptModeDebuggerToolStripMenuItem_Click);
            this.TranscriptModeDebuggerToolStripMenuItem.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;

            this.OpenExecutableFileCPULogToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            // 
            // openExecutableFileCPULogToolStripMenuItem
            // 
            this.OpenExecutableFileCPULogToolStripMenuItem.Name = "openExecutableFileCPULogToolStripMenuItem";
            this.OpenExecutableFileCPULogToolStripMenuItem.Size = new System.Drawing.Size(241, 22);
            this.OpenExecutableFileCPULogToolStripMenuItem.Text = "Open Executable File + CPU log";
            this.OpenExecutableFileCPULogToolStripMenuItem.Click += new System.EventHandler(this.OpenExecutableFileCPULogToolStripMenuItem_Click);
        }

        void UpdateDebugWindowHeader()
        {
            if (TranscriptModeDebuggerToolStripMenuItem.Checked)
            {
                HeaderTextbox.Text = "PC      OPCODES      SOURCE            REGISTERS";
            }
            else
            {
                HeaderTextbox.Text = "LABEL          PC      OPCODES      SOURCE";
            }
        }

        public void OnLoadForm()
        {
            // Load option from settings
            if (Simulator.Properties.Settings.Default.TranscriptModeDebugger)
            {
                TranscriptModeDebuggerToolStripMenuItem.Checked = true;
            }
            else
            {
                TranscriptModeDebuggerToolStripMenuItem.Checked = false;
            }

            UpdateDebugWindowHeader();
        }

        public void OnNewKernel(FoenixSystem kernel)
        {
            Kernel = kernel;

            // Initialize a new transcript.
            // Transcript size is based on whatever will fit in the window.
            TranscriptLines = new List<TranscriptDebugLine>(DebugPanel.Height / ROW_HEIGHT);
        }

        public void OnExecuteStep(
            WriteToTranscriptEnablement writeToTranscriptEnablement)
        {
            bool evaluateDebugLine =
                writeToTranscriptEnablement == WriteToTranscriptEnablement.Enabled ||
                CpuLogCheckBox.Checked;

            if (!evaluateDebugLine)
                return;

            CPUTranscriptDebugLine transcriptLine = GetDebugLineFromPC();

            // Implicitly, this update transcript if unpaused only, don't want to incur the overhead unconditionally.
            if (writeToTranscriptEnablement == WriteToTranscriptEnablement.Enabled)
            {
                PushLineToTranscript(transcriptLine);
            }

            if (CpuLogCheckBox.Checked)
            {
                CpuLogger.Log(transcriptLine.ToString());
            }
        }
        const int ROW_HEIGHT = 13;
        private const int LABEL_WIDTH = 100;

        private void HighlightLine(PaintEventArgs e, int yIndex, bool isInterrupt)
        {
            e.Graphics.FillRectangle(isInterrupt ? Brushes.Orange : Brushes.LightBlue, 0, yIndex * ROW_HEIGHT, DebugPanel.Width, ROW_HEIGHT);
        }

        // Returns whether the event was handled
        public bool OnPaint(PaintEventArgs e)
        {
            if (!TranscriptModeDebuggerToolStripMenuItem.Checked)
                return false;

            if (!Kernel.CPU.DebugPause)
                return false;

            if (TranscriptLines == null)
                return true;

            if (TranscriptLines.Count == 0)
                return true;

            int lastLineIndex = TranscriptLines.Count - 1;
            HighlightLine(e, lastLineIndex, false);

            int index = 0;
            foreach (TranscriptDebugLine line in TranscriptLines)
            {
                if (line == null) // Line can be null for invalid opcodes
                {
                    e.Graphics.DrawString("<invalid opcode>", HeaderTextbox.Font, Brushes.Black, 0, index * ROW_HEIGHT);
                    index++;
                }
                else
                {
                    e.Graphics.DrawString(line.ToString(), HeaderTextbox.Font, Brushes.Black, 0, index * ROW_HEIGHT);
                    index++;
                }
            }

            return true;
        }

        // Returns whether the event was handled
        public bool OnMouseMove()
        {
            return TranscriptModeDebuggerToolStripMenuItem.Checked;
        }

        void PushLineToTranscript(TranscriptDebugLine line)
        {
            if (TranscriptLines.Count == TranscriptLines.Capacity)
            {
                TranscriptLines.RemoveAt(0);
            }
            TranscriptLines.Add(line);

            if (TranscriptModeDebuggerToolStripMenuItem.Checked)
            {
                DebugPanel.Invalidate();
            }
        }

        CPUTranscriptDebugLine GetDebugLineFromPC()
        {
            CPUTranscriptDebugLine line = null;
            OpCode oc = Kernel.CPU.PreFetch();

            int instructionLength;
            if (oc != null)
            {
                instructionLength = oc.Length;
            }
            else
            {
                instructionLength = 1;
            }
            byte[] instructionBytes = new byte[instructionLength];
            for (int i = 0; i < instructionLength; i++)
            {
                instructionBytes[i] = Kernel.MemMgr.ReadByte(Kernel.CPU.PC + i);
            }

            line = new CPUTranscriptDebugLine(Kernel.CPU.PC, Kernel.CPU.A.Value, Kernel.CPU.X.Value, Kernel.CPU.Y.Value, Kernel.CPU.P);
            line.SetInstructionBytes(instructionBytes);

            if (oc == null)
            {
                line.SetSourceAssemblyLanguage("<Invalid opcode>");
            }
            else
            {
                string sourceAssemblyLanguage = oc.ToString(Kernel.CPU.ReadSignature(instructionLength, Kernel.CPU.PC));

                line.SetSourceAssemblyLanguage(sourceAssemblyLanguage);
            }
            return line;
        }
        public void cpuLogCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Start a new log
            if (CpuLogCheckBox.Checked)
            {
                CpuLogger = new CPULogger();
            }
            else // End a log
            {
                CpuLogger.Flush();
                CpuLogger.Dispose();
            }
        }

        public void TranscriptModeDebuggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Simulator.Properties.Settings.Default.TranscriptModeDebugger = TranscriptModeDebuggerToolStripMenuItem.Checked;
            Simulator.Properties.Settings.Default.Save();
            CpuWindow.Refresh();
            UpdateDebugWindowHeader();

            if (TranscriptModeDebuggerToolStripMenuItem.Checked)
            {
                AddBPOverlayButton.Visible = false;
                DeleteBPOverlayButton.Visible = false;
                InspectOverlayButton.Visible = false;
                LabelOverlayButton.Visible = false;
            }
        }

        private void OpenExecutableFileCPULogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Hex Files|*.hex|PGX Files|*.pgx|PGZ Files|*.pgz",
                Title = "Select an Executable File",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                CpuLogCheckBox.Checked = true;
                MainWindow.LoadExecutableFile(dialog.FileName);
            }
        }

        public void AddMainWindowUI(
            MainWindow mw,
            ToolStripItemCollection fileToolStripMenuItem,
            ToolStripItemCollection settingsToolStripMenuItem)
        {
            MainWindow = mw;
            fileToolStripMenuItem.Insert(3, OpenExecutableFileCPULogToolStripMenuItem );
            settingsToolStripMenuItem.AddRange(new System.Windows.Forms.ToolStripItem[] {TranscriptModeDebuggerToolStripMenuItem});
        } 

        public void AddCPUWindowUI(Control.ControlCollection controls)
        {
            controls.Add(CpuLogCheckBox);
        }

        public bool OnClearTrace()
        {
            if (TranscriptModeDebuggerToolStripMenuItem.Checked)
            {
                TranscriptLines.Clear();
                DebugPanel.Invalidate();
                return true;
            }
            return false;
        }

        public bool OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return false;

            if (!Kernel.CPU.DebugPause)
                return false;

            StringBuilder clipboardText = new StringBuilder();

            foreach (TranscriptDebugLine line in TranscriptLines)
            {
                if (line == null) // Line can be null for invalid opcodes
                {
                    clipboardText.AppendLine("<invalid opcode>");
                }
                else
                {
                    clipboardText.AppendLine(line.ToString());
                }
            }

            Clipboard.SetText(clipboardText.ToString());
            return true;
        }
    }
}
