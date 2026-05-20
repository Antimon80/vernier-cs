namespace Frontend {
    public partial class MainWindow : Form {
        public MainWindow() {
            InitializeComponent();

            ConfigureStatusStrip();
            ConfigureSpectrumPlot();
        }

        private void ConfigureSpectrumPlot() {
            spectrumPlot.Plot.YLabel("ADC [counts]");
            spectrumPlot.Plot.XLabel("Wavelength [nm]");
        }

        private void ConfigureStatusStrip() {
            calibrationStatus.Text = "■";
            calibrationStatus.ForeColor = Color.Red;

            calibrationLabel.Text = "Calibrated: ";
        }

        private void SetCalibrationStatus(bool isCalibrated) {
            calibrationStatus.ForeColor = isCalibrated ? Color.Green : Color.Red;
        }
    }
}
