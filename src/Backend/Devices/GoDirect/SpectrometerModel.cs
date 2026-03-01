namespace Backend.Devices.GoDirect
{
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
        private double[]? _cachedAxis;

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

        public double[] BuildWavelengthAxis()
        {
            if (CCDPixelIndexMin <= 0 && CCDPixelIndexMax <= 0)
            {
                throw new InvalidOperationException($"Model '{Name}' does not define CCD pixel index range.");
            }

            int lo = CCDPixelIndexMin;
            int hi = CCDPixelIndexMax;

            if (hi < lo)
            {
                (lo, hi) = (hi, lo);
            }

            int n = hi - lo + 1;
            if (n <= 1)
            {
                throw new InvalidOperationException("Invalid CCD pixel index range.");
            }

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

