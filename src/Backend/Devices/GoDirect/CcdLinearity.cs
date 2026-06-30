namespace Backend.Devices.GoDirect {
    /// <summary>
    /// CCD linearity evaluation based on RAW bytes returned by the 0x02 device command.
    ///
    /// The device returns measurement data as little-endian unsigned 16-bit values.
    ///
    /// The algorithm:
    /// 1. Decode the raw byte stream as u16 little endian.
    /// 2. Detect strictly increasing runs.
    /// 3. Inside the best run, extract the longest "linear core" where
    ///    step sizes stay within median(step) ± tolerance.
    /// 4. Classify the detected linear core as Pass, Warn, or Fail.
    ///
    /// Intended for sanity-checking CCD ramp linearity during diagnostics.
    /// </summary>
    public static class CcdLinearity {
        /// <summary>
        /// Overall quality classification of the detected linear core.
        /// </summary>
        public enum Levels {
            Pass,
            Warn,
            Fail
        }

        /// <summary>
        /// Result of the CCD linearity evaluation.
        /// </summary>
        public sealed record CcdLinResult(
            Levels Level,
            int TotalU16,
            int CoreStartIndex,
            int CoreLength,
            int StepMedian,
            int StepMad,
            int OutOfToleranceSteps,
            int Tolerance,
            string Message
        );

        /// <summary>
        /// Evaluates the CCD raw byte sequence.
        ///
        /// The input data is expected to contain consecutive little-endian u16 values.
        /// </summary>
        public static CcdLinResult Evaluate(byte[] rawBytes, int tolerance, int minRunLength) {
            if (rawBytes.Length < 4) {
                return new CcdLinResult(
                    Levels.Fail,
                    TotalU16: 0,
                    CoreStartIndex: -1,
                    CoreLength: 0,
                    StepMedian: 0,
                    StepMad: 0,
                    OutOfToleranceSteps: 0,
                    Tolerance: tolerance,
                    Message: "Input too short.");
            }

            if ((rawBytes.Length % 2) != 0) {
                return new CcdLinResult(
                    Levels.Fail,
                    TotalU16: rawBytes.Length / 2,
                    CoreStartIndex: -1,
                    CoreLength: 0,
                    StepMedian: 0,
                    StepMad: 0,
                    OutOfToleranceSteps: 0,
                    Tolerance: tolerance,
                    Message: $"Input length must be even for u16 decoding, but was {rawBytes.Length} bytes.");
            }

            int[] values = DecodeU16LittleEndian(rawBytes);

            if (values.Length == 0) {
                return new CcdLinResult(
                    Levels.Fail,
                    TotalU16: 0,
                    CoreStartIndex: -1,
                    CoreLength: 0,
                    StepMedian: 0,
                    StepMad: 0,
                    OutOfToleranceSteps: 0,
                    Tolerance: tolerance,
                    Message: "No decoded values.");
            }

            List<Run> increasingRuns = FindStrictlyIncreasingRuns(values);

            if (increasingRuns.Count == 0) {
                return new CcdLinResult(
                    Levels.Fail,
                    TotalU16: values.Length,
                    CoreStartIndex: -1,
                    CoreLength: 0,
                    StepMedian: 0,
                    StepMad: 0,
                    OutOfToleranceSteps: 0,
                    Tolerance: tolerance,
                    Message: "No increasing run found.");
            }

            List<Run> longEnough = [.. increasingRuns.Where(r => r.Length >= minRunLength)];

            Run bestRun = (longEnough.Count > 0)
                ? longEnough.OrderByDescending(r => r.Length).First()
                : increasingRuns.OrderByDescending(r => r.Length).First();

            LinearCore core = ExtractBestLinearCore(values, bestRun.StartIndex, bestRun.Length, tolerance);

            if (core.Length < minRunLength || core.StepMedian <= 0) {
                string msg =
                    $"linear core too short or invalid: " +
                    $"coreLen={core.Length} (<{minRunLength}) or " +
                    $"stepMedian={core.StepMedian} (<=0); " +
                    $"bestRunLen={bestRun.Length}, " +
                    $"outTol={core.OutOfToleranceSteps}, " +
                    $"stepMad={core.StepMad}";

                return new CcdLinResult(
                    Levels.Fail,
                    TotalU16: values.Length,
                    CoreStartIndex: core.StartIndex,
                    CoreLength: core.Length,
                    StepMedian: core.StepMedian,
                    StepMad: core.StepMad,
                    OutOfToleranceSteps: core.OutOfToleranceSteps,
                    Tolerance: tolerance,
                    Message: msg);
            }

            if (core.OutOfToleranceSteps == 0 && core.StepMad <= 1) {
                string msg =
                    $"coreLen={core.Length}, " +
                    $"start={core.StartIndex}, " +
                    $"stepMedian={core.StepMedian}, " +
                    $"stepMad={core.StepMad}, " +
                    $"outTol={core.OutOfToleranceSteps}";

                return new CcdLinResult(
                    Levels.Pass,
                    TotalU16: values.Length,
                    CoreStartIndex: core.StartIndex,
                    CoreLength: core.Length,
                    StepMedian: core.StepMedian,
                    StepMad: core.StepMad,
                    OutOfToleranceSteps: core.OutOfToleranceSteps,
                    Tolerance: tolerance,
                    Message: msg);
            } else {
                string msg =
                    $"coreLen={core.Length}, " +
                    $"start={core.StartIndex}, " +
                    $"stepMedian={core.StepMedian}, " +
                    $"stepMad={core.StepMad}, " +
                    $"outTol={core.OutOfToleranceSteps} " +
                    $"(tol=±{tolerance})";

                return new CcdLinResult(
                    Levels.Warn,
                    TotalU16: values.Length,
                    CoreStartIndex: core.StartIndex,
                    CoreLength: core.Length,
                    StepMedian: core.StepMedian,
                    StepMad: core.StepMad,
                    OutOfToleranceSteps: core.OutOfToleranceSteps,
                    Tolerance: tolerance,
                    Message: msg);
            }
        }

        /// <summary>
        /// Decodes the raw byte stream into unsigned 16-bit values using little endian byte order.
        /// </summary>
        private static int[] DecodeU16LittleEndian(byte[] rawBytes) {
            int count = rawBytes.Length / 2;
            int[] values = new int[count];

            for (int i = 0; i < count; i++) {
                int lowByte = rawBytes[i * 2 + 0];
                int highByte = rawBytes[i * 2 + 1];

                values[i] = lowByte | (highByte << 8);
            }

            return values;
        }

        private readonly record struct Run(int StartIndex, int Length);

        /// <summary>
        /// Splits the sequence into maximal strictly increasing runs.
        /// </summary>
        private static List<Run> FindStrictlyIncreasingRuns(int[] values) {
            List<Run> runs = [];

            if (values.Length == 0) {
                return runs;
            }

            int currentRunStart = 0;

            for (int index = 1; index < values.Length; index++) {
                if (values[index] <= values[index - 1]) {
                    runs.Add(new Run(currentRunStart, index - currentRunStart));
                    currentRunStart = index;
                }
            }

            runs.Add(new Run(currentRunStart, values.Length - currentRunStart));
            return runs;
        }

        private readonly record struct LinearCore(
            int StartIndex,
            int Length,
            int StepMedian,
            int StepMad,
            int OutOfToleranceSteps);

        /// <summary>
        /// Within a strictly increasing run, finds the longest contiguous segment
        /// whose step sizes stay within median ± tolerance.
        /// </summary>
        private static LinearCore ExtractBestLinearCore(int[] values, int runStart, int runLength, int tolerance) {
            if (runLength < 3) {
                return new LinearCore(
                    StartIndex: runStart,
                    Length: runLength,
                    StepMedian: 0,
                    StepMad: 0,
                    OutOfToleranceSteps: 0);
            }

            int[] runSteps = ComputeSteps(values, runStart, runLength);

            int stepMedian = Median(runSteps);
            int stepMad = Mad(runSteps, stepMedian);

            int low = stepMedian - tolerance;
            int high = stepMedian + tolerance;

            int bestSegmentStartStepIndex = 0;
            int bestSegmentStepCount = 0;

            int stepIndex = 0;

            while (stepIndex < runSteps.Length) {
                if (runSteps[stepIndex] < low || runSteps[stepIndex] > high) {
                    stepIndex++;
                    continue;
                }

                int segmentStart = stepIndex;
                int segmentEnd = stepIndex;

                while (segmentEnd < runSteps.Length &&
                       runSteps[segmentEnd] >= low &&
                       runSteps[segmentEnd] <= high) {
                    segmentEnd++;
                }

                int segmentStepCount = segmentEnd - segmentStart;

                if (segmentStepCount > bestSegmentStepCount) {
                    bestSegmentStepCount = segmentStepCount;
                    bestSegmentStartStepIndex = segmentStart;
                }

                stepIndex = segmentEnd;
            }

            if (bestSegmentStepCount == 0) {
                int outTol = runSteps.Count(step => step < low || step > high);

                return new LinearCore(
                    StartIndex: runStart,
                    Length: runLength,
                    StepMedian: stepMedian,
                    StepMad: stepMad,
                    OutOfToleranceSteps: outTol);
            }

            int coreStartIndex = runStart + bestSegmentStartStepIndex;
            int coreLength = bestSegmentStepCount + 1;

            int[] coreSteps = ComputeSteps(values, coreStartIndex, coreLength);

            int coreMedian = Median(coreSteps);
            int coreMad = Mad(coreSteps, coreMedian);

            int coreLow = coreMedian - tolerance;
            int coreHigh = coreMedian + tolerance;

            int coreOutTol = coreSteps.Count(step => step < coreLow || step > coreHigh);

            return new LinearCore(
                StartIndex: coreStartIndex,
                Length: coreLength,
                StepMedian: coreMedian,
                StepMad: coreMad,
                OutOfToleranceSteps: coreOutTol);
        }

        private static int[] ComputeSteps(int[] values, int startIndex, int length) {
            int stepCount = length - 1;
            int[] steps = new int[stepCount];

            for (int i = 0; i < stepCount; i++) {
                steps[i] = values[startIndex + i + 1] - values[startIndex + i];
            }

            return steps;
        }

        /// <summary>
        /// Computes the median of an integer array.
        /// </summary>
        private static int Median(int[] values) {
            int[] sorted = (int[])values.Clone();
            Array.Sort(sorted);

            int n = sorted.Length;
            int mid = n / 2;

            return (n % 2 == 1)
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        /// <summary>
        /// Median Absolute Deviation.
        /// </summary>
        private static int Mad(int[] values, int median) {
            if (values.Length == 0) {
                return 0;
            }

            int[] deviations = new int[values.Length];

            for (int i = 0; i < values.Length; i++) {
                deviations[i] = Math.Abs(values[i] - median);
            }

            return Median(deviations);
        }
    }
}