namespace Backend.Devices.GoDirect
{
    /// <summary>
    /// Immutable model descriptor for a Vernier Go Direct spectrometer variant.
    ///
    /// This record holds device- and model-specific constants that are required to:
    /// - identify the device (PID, human-readable name),
    /// - decode measurement transfers (packet count and payload size),
    /// - determine supported illumination (white lamp, fluorescence LEDs),
    /// - map CCD pixel indices to a wavelength axis (min/max nm + ROI pixel range),
    /// - choose reasonable defaults (e.g., typical integration time).
    ///
    /// The wavelength axis for the configured ROI is cached after first construction.
    /// </summary>
    public sealed record SpectrometerModel(
     ushort Pid,
     string Name,
     int PacketCount,
     int PacketPayloadBytes,
     bool HasWhiteLamp,
     bool HasLed405,
     bool HasLed500,
     double WavelengthMinNm,
     double WavelengthMaxNm,
     int CCDPixelIndexMin,
     int CCDPixelIndexMax,
     int IntegrationTimeMsMean
 )
    {
        /// <summary>
        /// Cached wavelength axis for this model's ROI (length = ROI pixel count).
        /// </summary>
        private double[]? _cachedAxis;

        /// <summary>
        /// Returns the wavelength axis for this model's ROI.
        /// The axis is computed once and then cached for subsequent calls.
        /// </summary>
        /// <remarks>
        /// This method is not strictly thread-safe for the first call (benign race):
        /// in concurrent scenarios it may build the axis more than once, but the
        /// resulting arrays are identical and the last write wins.
        /// </remarks>
        public double[] GetWavelengthAxis()
        {
            double[]? axis = _cachedAxis;
            if (axis is not null)
            {
                return axis;
            }

            axis = BuildWavelengthAxis();
            _cachedAxis = axis;

            return axis;
        }

        /// <summary>
        /// Builds a linearly spaced wavelength axis for the configured CCD ROI.
        /// </summary>
        /// <returns>
        /// A new array of wavelengths (nm) with length (hi - lo + 1), where lo/hi are the ROI pixel bounds.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the model does not define a valid CCD ROI (pixel range missing or invalid).
        /// </exception>
        /// <remarks>
        /// The mapping is linear between <see cref="WavelengthMinNm"/> and <see cref="WavelengthMaxNm"/>.
        /// This is a simplification; if a model requires a polynomial calibration, this method would
        /// be the natural extension point.
        /// </remarks>
        public double[] BuildWavelengthAxis()
        {
            // ROI bounds must be configured to compute the axis.
            if (CCDPixelIndexMin <= 0 && CCDPixelIndexMax <= 0)
            {
                throw new InvalidOperationException($"Model '{Name}' does not define CCD pixel index range.");
            }

            int lo = CCDPixelIndexMin;
            int hi = CCDPixelIndexMax;

            // Normalize ordering.
            if (hi < lo)
            {
                (lo, hi) = (hi, lo);
            }

            int n = hi - lo + 1;
            if (n <= 1)
            {
                throw new InvalidOperationException("Invalid CCD pixel index range.");
            }

            // Linear wavelength mapping over the ROI.
            double[] wl = new double[n];
            double step = (WavelengthMaxNm - WavelengthMinNm) / (n - 1);

            for (int i = 0; i < n; i++)
            {
                wl[i] = WavelengthMinNm + i * step;
            }

            return wl;
        }
    }

}

