namespace Frontend {
    partial class MainWindow {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            mainToolbar = new ToolStrip();
            openButton = new ToolStripButton();
            saveButton = new ToolStripButton();
            saveAsButton = new ToolStripButton();
            printButton = new ToolStripButton();
            importButton = new ToolStripButton();
            exportButton = new ToolStripButton();
            toolStripSeparator1 = new ToolStripSeparator();
            undoButton = new ToolStripButton();
            redoButton = new ToolStripButton();
            toolStripSeparator2 = new ToolStripSeparator();
            operatingButton = new ToolStripButton();
            acquisitionButton = new ToolStripButton();
            toolStripSeparator3 = new ToolStripSeparator();
            calibrateButton = new ToolStripButton();
            startButton = new ToolStripButton();
            stopButton = new ToolStripButton();
            toolStripSeparator4 = new ToolStripSeparator();
            autoscaleButton = new ToolStripButton();
            crosshairsButton = new ToolStripButton();
            toolStripSeparator5 = new ToolStripSeparator();
            analyzeButton = new ToolStripButton();
            annotationsButton = new ToolStripButton();
            toolStripSeparator6 = new ToolStripSeparator();
            settingsButton = new ToolStripButton();
            helpButton = new ToolStripButton();
            splitContainer1 = new SplitContainer();
            dataGridView1 = new DataGridView();
            wavelength = new DataGridViewTextBoxColumn();
            counts = new DataGridViewTextBoxColumn();
            spectrumPlot = new ScottPlot.WinForms.FormsPlot();
            statusStrip1 = new StatusStrip();
            calibrationLabel = new ToolStripStatusLabel();
            calibrationStatus = new ToolStripStatusLabel();
            mainToolbar.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // mainToolbar
            // 
            mainToolbar.ImageScalingSize = new Size(28, 28);
            mainToolbar.Items.AddRange(new ToolStripItem[] { openButton, saveButton, saveAsButton, printButton, importButton, exportButton, toolStripSeparator1, undoButton, redoButton, toolStripSeparator2, operatingButton, acquisitionButton, toolStripSeparator3, calibrateButton, startButton, stopButton, toolStripSeparator4, autoscaleButton, crosshairsButton, toolStripSeparator5, analyzeButton, annotationsButton, toolStripSeparator6, settingsButton, helpButton });
            mainToolbar.Location = new Point(0, 0);
            mainToolbar.Name = "mainToolbar";
            mainToolbar.Size = new Size(1600, 38);
            mainToolbar.TabIndex = 0;
            mainToolbar.Text = "Main Toolbar";
            // 
            // openButton
            // 
            openButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            openButton.Image = Properties.Resources.open;
            openButton.ImageTransparentColor = Color.Magenta;
            openButton.Name = "openButton";
            openButton.Size = new Size(40, 32);
            openButton.Text = "Open File";
            // 
            // saveButton
            // 
            saveButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            saveButton.Image = Properties.Resources.save;
            saveButton.ImageTransparentColor = Color.Magenta;
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(40, 32);
            saveButton.Text = "Save File";
            // 
            // saveAsButton
            // 
            saveAsButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            saveAsButton.Image = Properties.Resources.save_as;
            saveAsButton.ImageTransparentColor = Color.Magenta;
            saveAsButton.Name = "saveAsButton";
            saveAsButton.Size = new Size(40, 32);
            saveAsButton.Text = "Save As";
            // 
            // printButton
            // 
            printButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            printButton.Image = Properties.Resources.print;
            printButton.ImageTransparentColor = Color.Magenta;
            printButton.Name = "printButton";
            printButton.Size = new Size(40, 32);
            printButton.Text = "Print";
            // 
            // importButton
            // 
            importButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            importButton.Image = Properties.Resources.import;
            importButton.ImageTransparentColor = Color.Magenta;
            importButton.Name = "importButton";
            importButton.Size = new Size(40, 32);
            importButton.Text = "Import File";
            // 
            // exportButton
            // 
            exportButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            exportButton.Image = Properties.Resources.export;
            exportButton.ImageTransparentColor = Color.Magenta;
            exportButton.Name = "exportButton";
            exportButton.Size = new Size(40, 32);
            exportButton.Text = "Export File";
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(6, 38);
            // 
            // undoButton
            // 
            undoButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            undoButton.Image = Properties.Resources.undo;
            undoButton.ImageTransparentColor = Color.Magenta;
            undoButton.Name = "undoButton";
            undoButton.Size = new Size(40, 32);
            undoButton.Text = "Undo";
            // 
            // redoButton
            // 
            redoButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            redoButton.Image = Properties.Resources.redo;
            redoButton.ImageTransparentColor = Color.Magenta;
            redoButton.Name = "redoButton";
            redoButton.Size = new Size(40, 32);
            redoButton.Text = "Redo";
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(6, 38);
            // 
            // operatingButton
            // 
            operatingButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            operatingButton.Image = Properties.Resources.operating_mode;
            operatingButton.ImageTransparentColor = Color.Magenta;
            operatingButton.Name = "operatingButton";
            operatingButton.Size = new Size(40, 32);
            operatingButton.Text = "Operating Mode";
            // 
            // acquisitionButton
            // 
            acquisitionButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            acquisitionButton.Image = Properties.Resources.acquisition_mode;
            acquisitionButton.ImageTransparentColor = Color.Magenta;
            acquisitionButton.Name = "acquisitionButton";
            acquisitionButton.Size = new Size(40, 32);
            acquisitionButton.Text = "Acquisition Mode";
            // 
            // toolStripSeparator3
            // 
            toolStripSeparator3.Name = "toolStripSeparator3";
            toolStripSeparator3.Size = new Size(6, 38);
            // 
            // calibrateButton
            // 
            calibrateButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            calibrateButton.Image = Properties.Resources.calibrate;
            calibrateButton.ImageTransparentColor = Color.Magenta;
            calibrateButton.Name = "calibrateButton";
            calibrateButton.Size = new Size(40, 32);
            calibrateButton.Text = "Calibrate";
            // 
            // startButton
            // 
            startButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            startButton.Image = Properties.Resources.start;
            startButton.ImageTransparentColor = Color.Magenta;
            startButton.Name = "startButton";
            startButton.Size = new Size(40, 32);
            startButton.Text = "Start Acquisition";
            // 
            // stopButton
            // 
            stopButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            stopButton.Image = Properties.Resources.stop;
            stopButton.ImageTransparentColor = Color.Magenta;
            stopButton.Name = "stopButton";
            stopButton.Size = new Size(40, 32);
            stopButton.Text = "Stop Acquisition";
            // 
            // toolStripSeparator4
            // 
            toolStripSeparator4.Name = "toolStripSeparator4";
            toolStripSeparator4.Size = new Size(6, 38);
            // 
            // autoscaleButton
            // 
            autoscaleButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            autoscaleButton.Image = Properties.Resources.autoscale;
            autoscaleButton.ImageTransparentColor = Color.Magenta;
            autoscaleButton.Name = "autoscaleButton";
            autoscaleButton.Size = new Size(40, 32);
            autoscaleButton.Text = "Autoscale";
            // 
            // crosshairsButton
            // 
            crosshairsButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            crosshairsButton.Image = Properties.Resources.crosshair;
            crosshairsButton.ImageTransparentColor = Color.Magenta;
            crosshairsButton.Name = "crosshairsButton";
            crosshairsButton.Size = new Size(40, 32);
            crosshairsButton.Text = "Crosshairs";
            // 
            // toolStripSeparator5
            // 
            toolStripSeparator5.Name = "toolStripSeparator5";
            toolStripSeparator5.Size = new Size(6, 38);
            // 
            // analyzeButton
            // 
            analyzeButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            analyzeButton.Image = Properties.Resources.analyze;
            analyzeButton.ImageTransparentColor = Color.Magenta;
            analyzeButton.Name = "analyzeButton";
            analyzeButton.Size = new Size(40, 32);
            analyzeButton.Text = "Analyze Spectrum";
            // 
            // annotationsButton
            // 
            annotationsButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            annotationsButton.Image = Properties.Resources.annotations;
            annotationsButton.ImageTransparentColor = Color.Magenta;
            annotationsButton.Name = "annotationsButton";
            annotationsButton.Size = new Size(40, 32);
            annotationsButton.Text = "Annotations";
            // 
            // toolStripSeparator6
            // 
            toolStripSeparator6.Name = "toolStripSeparator6";
            toolStripSeparator6.Size = new Size(6, 38);
            // 
            // settingsButton
            // 
            settingsButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            settingsButton.Image = Properties.Resources.settings;
            settingsButton.ImageTransparentColor = Color.Magenta;
            settingsButton.Name = "settingsButton";
            settingsButton.Size = new Size(40, 32);
            settingsButton.Text = "Settings";
            // 
            // helpButton
            // 
            helpButton.DisplayStyle = ToolStripItemDisplayStyle.Image;
            helpButton.Image = Properties.Resources.help;
            helpButton.ImageTransparentColor = Color.Magenta;
            helpButton.Name = "helpButton";
            helpButton.Size = new Size(40, 32);
            helpButton.Text = "Help";
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 38);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(dataGridView1);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(spectrumPlot);
            splitContainer1.Panel2.Controls.Add(statusStrip1);
            splitContainer1.Size = new Size(1600, 862);
            splitContainer1.SplitterDistance = 303;
            splitContainer1.TabIndex = 1;
            // 
            // dataGridView1
            // 
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridView1.Columns.AddRange(new DataGridViewColumn[] { wavelength, counts });
            dataGridView1.Dock = DockStyle.Fill;
            dataGridView1.Location = new Point(0, 0);
            dataGridView1.Name = "dataGridView1";
            dataGridView1.RowHeadersWidth = 72;
            dataGridView1.Size = new Size(303, 862);
            dataGridView1.TabIndex = 0;
            // 
            // wavelength
            // 
            wavelength.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            wavelength.FillWeight = 50F;
            wavelength.HeaderText = "Wavelength [nm]";
            wavelength.MinimumWidth = 9;
            wavelength.Name = "wavelength";
            // 
            // counts
            // 
            counts.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            counts.FillWeight = 50F;
            counts.HeaderText = "ADC [counts]";
            counts.MinimumWidth = 9;
            counts.Name = "counts";
            // 
            // spectrumPlot
            // 
            spectrumPlot.AutoSize = true;
            spectrumPlot.Dock = DockStyle.Fill;
            spectrumPlot.Location = new Point(0, 0);
            spectrumPlot.Name = "spectrumPlot";
            spectrumPlot.Size = new Size(1293, 823);
            spectrumPlot.TabIndex = 1;
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(28, 28);
            statusStrip1.Items.AddRange(new ToolStripItem[] { calibrationLabel, calibrationStatus });
            statusStrip1.Location = new Point(0, 823);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1293, 39);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // calibrationLabel
            // 
            calibrationLabel.Name = "calibrationLabel";
            calibrationLabel.Size = new Size(118, 30);
            calibrationLabel.Text = "Calibrated: ";
            // 
            // calibrationStatus
            // 
            calibrationStatus.Name = "calibrationStatus";
            calibrationStatus.Size = new Size(0, 30);
            // 
            // MainWindow
            // 
            AutoScaleDimensions = new SizeF(12F, 30F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1600, 900);
            Controls.Add(splitContainer1);
            Controls.Add(mainToolbar);
            Name = "MainWindow";
            Text = "Form1";
            mainToolbar.ResumeLayout(false);
            mainToolbar.PerformLayout();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ToolStrip mainToolbar;
        private ToolStripButton openButton;
        private ToolStripButton saveButton;
        private ToolStripButton saveAsButton;
        private ToolStripButton printButton;
        private ToolStripButton importButton;
        private ToolStripButton exportButton;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripButton undoButton;
        private ToolStripButton redoButton;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripButton operatingButton;
        private ToolStripButton acquisitionButton;
        private ToolStripSeparator toolStripSeparator3;
        private ToolStripButton calibrateButton;
        private ToolStripButton startButton;
        private ToolStripButton stopButton;
        private ToolStripButton autoscaleButton;
        private ToolStripButton crosshairsButton;
        private ToolStripSeparator toolStripSeparator4;
        private ToolStripButton analyzeButton;
        private ToolStripButton annotationsButton;
        private ToolStripSeparator toolStripSeparator5;
        private ToolStripButton settingsButton;
        private ToolStripButton helpButton;
        private ToolStripSeparator toolStripSeparator6;
        private SplitContainer splitContainer1;
        private DataGridView dataGridView1;
        private DataGridViewTextBoxColumn wavelength;
        private DataGridViewTextBoxColumn counts;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel calibrationLabel;
        private ToolStripStatusLabel calibrationStatus;
        private ScottPlot.WinForms.FormsPlot spectrumPlot;
    }
}
