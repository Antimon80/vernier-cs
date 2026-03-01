namespace Backend.Devices.GoDirect
{
    /// <summary>
    /// CCD linearity evaluation based on RAW bytes returned by the 0x02 device command.
    ///
    /// The algorithm:
    /// 1. Try multiple decoding strategies (LE/BE, full sequence vs odd indices only).
    /// 2. For each decoded u16 sequence:
    ///    - Detect strictly increasing runs.
    ///    - Inside the best run, extract the longest "linear core" where
    ///      step sizes stay within median(step) ± tolerance.
    /// 3. Score all candidates and return the best result.
    ///
    /// Intended for sanity-checking CCD ramp linearity during diagnostics.
    /// </summary>
    public static class CcdLinearity
    {
        /// <summary>
        /// Overall quality classification of the detected linear core.
        /// </summary>
        public enum Levels
        {
            Pass,
            Warn,
            Fail
        }

        /// <summary>
        /// Result of one decoder evaluation.
        /// </summary>
        public sealed record Result(
            Levels Level,
            string Decoder,
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
        /// Runs all decoding strategies and returns the best-scoring candidate.
        /// </summary>
        public static Result EvaluateAuto(byte[] rawBytes, int tolerance, int minRunLength)
        {
            List<Result> candidates =
            [
                Evaluate(rawBytes, tolerance, minRunLength, decoderName: "LE",     takeOddIndicesOnly: false, littleEndian: true),
                Evaluate(rawBytes, tolerance, minRunLength, decoderName: "BE",     takeOddIndicesOnly: false, littleEndian: false),
                Evaluate(rawBytes, tolerance, minRunLength, decoderName: "LE_odd", takeOddIndicesOnly: true,  littleEndian: true),
                Evaluate(rawBytes, tolerance, minRunLength, decoderName: "BE_odd", takeOddIndicesOnly: true,  littleEndian: false),
            ];

            return candidates.OrderByDescending(Score).First();

            // Score prioritizes:
            // 1. Pass > Warn > Fail
            // 2. Longer core
            // 3. Fewer out-of-tolerance steps
            // 4. Lower MAD (noise)
            static long Score(Result r)
            {
                long levelBias = r.Level switch
                {
                    Levels.Pass => 1_000_000_000L,
                    Levels.Warn => 500_000_000L,
                    _ => 0L
                };

                return levelBias
                       + r.CoreLength * 1_000_000L
                       - r.OutOfToleranceSteps * 10_000L
                       - r.StepMad * 1_000L;
            }
        }

        /// <summary>
        /// Evaluates a single decoding strategy.
        /// </summary>
        private static Result Evaluate(
            byte[] rawBytes,
            int tolerance,
            int minRunLength,
            string decoderName,
            bool takeOddIndicesOnly,
            bool littleEndian)
        {
            if (rawBytes.Length < 4)
            {
                return new Result(
                    Levels.Fail, decoderName,
                    TotalU16: 0,
                    CoreStartIndex: -1,
                    CoreLength: 0,
                    StepMedian: 0,
                    StepMad: 0,
                    OutOfToleranceSteps: 0,
                    Tolerance: tolerance,
                    Message: "Input too short.");
            }

            // Decode raw bytes into u16 values
            int[] allValues = DecodeU16(rawBytes, littleEndian);

            // Optional filtering (used for devices that interleave data)
            int[] values = takeOddIndicesOnly
                ? allValues.Where((_, index) => (index % 2) == 1).ToArray()
                : allValues;

            if (values.Length == 0)
            {
                return new Result(
                    Levels.Fail, decoderName,
                    TotalU16: 0,
                    CoreStartIndex: -1,
                    CoreLength: 0,
                    StepMedian: 0,
                    StepMad: 0,
                    OutOfToleranceSteps: 0,
                    Tolerance: tolerance,
                    Message: "No decoded values.");
            }

            // Find strictly increasing segments
            List<Run> increasingRuns = FindStrictlyIncreasingRuns(values);

            // Prefer runs that are already "long enough"; otherwise take the longest run we have.
            List<Run> longEnough = [.. increasingRuns.Where(r => r.Length >= minRunLength)];
            Run bestRun = (longEnough.Count > 0)
                ? longEnough.OrderByDescending(r => r.Length).First()
                : increasingRuns.OrderByDescending(r => r.Length).First();

            // Extract most linear contiguous core inside that run
            LinearCore core = ExtractBestLinearCore(values, bestRun.StartIndex, bestRun.Length, tolerance);

            // Basic validation
            if (core.Length < minRunLength || core.StepMedian <= 0)
            {
                string msg = $"linear core too short or invalid: coreLen={core.Length} (<{minRunLength}) or stepMedian={core.StepMedian} (<=0); " +
                             $"bestRunLen={bestRun.Length}, outTol={core.OutOfToleranceSteps}, stepMad={core.StepMad}";
                return new Result(
                    Levels.Fail, decoderName,
                    TotalU16: values.Length,
                    CoreStartIndex: core.StartIndex,
                    CoreLength: core.Length,
                    StepMedian: core.StepMedian,
                    StepMad: core.StepMad,
                    OutOfToleranceSteps: core.OutOfToleranceSteps,
                    Tolerance: tolerance,
                    Message: msg);
            }

            // Classification logic
            if (core.OutOfToleranceSteps == 0 && core.StepMad <= 1)
            {
                string msg = $"coreLen={core.Length}, start={core.StartIndex}, stepMedian={core.StepMedian}, stepMad={core.StepMad}, outTol={core.OutOfToleranceSteps}";
                return new Result(
                    Levels.Pass, decoderName,
                    TotalU16: values.Length,
                    CoreStartIndex: core.StartIndex,
                    CoreLength: core.Length,
                    StepMedian: core.StepMedian,
                    StepMad: core.StepMad,
                    OutOfToleranceSteps: core.OutOfToleranceSteps,
                    Tolerance: tolerance,
                    Message: msg);
            }
            else
            {
                string msg = $"coreLen={core.Length}, start={core.StartIndex}, stepMedian={core.StepMedian}, stepMad={core.StepMad}, outTol={core.OutOfToleranceSteps} (tol=±{tolerance})";
                return new Result(
                    Levels.Warn, decoderName,
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
        /// Decodes raw byte array into u16 values (LE or BE).
        /// </summary>
        private static int[] DecodeU16(byte[] rawBytes, bool littleEndian)
        {
            int count = rawBytes.Length / 2;
            int[] values = new int[count];

            for (int i = 0; i < count; i++)
            {
                int byte0 = rawBytes[i * 2 + 0];
                int byte1 = rawBytes[i * 2 + 1];

                values[i] = littleEndian
                    ? (byte0 | (byte1 << 8))
                    : ((byte0 << 8) | byte1);
            }

            return values;
        }

        private readonly record struct Run(int StartIndex, int Length);

        /// <summary>
        /// Splits sequence into maximal strictly increasing runs.
        /// </summary>
        private static List<Run> FindStrictlyIncreasingRuns(int[] values)
        {
            List<Run> runs = [];
            if (values.Length == 0)
            {
                return runs;
            }

            int currentRunStart = 0;

            for (int index = 1; index < values.Length; index++)
            {
                if (values[index] <= values[index - 1])
                {
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
        /// Within a strictly increasing run, find the longest contiguous
        /// segment whose step sizes stay within median ± tolerance.
        /// </summary>
        private static LinearCore ExtractBestLinearCore(int[] values, int runStart, int runLength, int tolerance)
        {
            if (runLength < 3)
            {
                return new LinearCore(runStart, runLength, StepMedian: 0, StepMad: 0, OutOfToleranceSteps: 0);
            }

            int[] runSteps = ComputeSteps(values, runStart, runLength);

            int stepMedian = Median(runSteps);
            int stepMad = Mad(runSteps, stepMedian);

            int low = stepMedian - tolerance;
            int high = stepMedian + tolerance;

            // Find longest contiguous segment of "good" steps (within [low, high]).
            int bestSegmentStartStepIndex = 0;
            int bestSegmentStepCount = 0;

            int stepIndex = 0;
            while (stepIndex < runSteps.Length)
            {
                if (runSteps[stepIndex] < low || runSteps[stepIndex] > high)
                {
                    stepIndex++;
                    continue;
                }

                int segmentStart = stepIndex;
                int segmentEnd = stepIndex;

                while (segmentEnd < runSteps.Length && runSteps[segmentEnd] >= low && runSteps[segmentEnd] <= high)
                {
                    segmentEnd++;
                }

                int segmentStepCount = segmentEnd - segmentStart;
                if (segmentStepCount > bestSegmentStepCount)
                {
                    bestSegmentStepCount = segmentStepCount;
                    bestSegmentStartStepIndex = segmentStart;
                }

                stepIndex = segmentEnd;
            }

            // If no good segment exists at all, keep the whole run, but report how bad it is.
            if (bestSegmentStepCount == 0)
            {
                int outTol = runSteps.Count(step => step < low || step > high);
                return new LinearCore(runStart, runLength, stepMedian, stepMad, outTol);
            }

            // Convert "steps segment" to "values segment": length = steps + 1
            int coreStartIndex = runStart + bestSegmentStartStepIndex;
            int coreLength = bestSegmentStepCount + 1;

            // Recompute stats on the chosen core
            int[] coreSteps = ComputeSteps(values, coreStartIndex, coreLength);
            int coreMedian = Median(coreSteps);
            int coreMad = Mad(coreSteps, coreMedian);

            int coreLow = coreMedian - tolerance;
            int coreHigh = coreMedian + tolerance;
            int coreOutTol = coreSteps.Count(step => step < coreLow || step > coreHigh);

            return new LinearCore(coreStartIndex, coreLength, coreMedian, coreMad, coreOutTol);
        }

        private static int[] ComputeSteps(int[] values, int startIndex, int length)
        {
            int stepCount = length - 1;
            int[] steps = new int[stepCount];

            for (int i = 0; i < stepCount; i++)
            {
                steps[i] = values[startIndex + i + 1] - values[startIndex + i];
            }

            return steps;
        }

        /// <summary>
        /// Computes the median of an integer array.
        /// </summary>
        private static int Median(int[] values)
        {
            int[] sorted = (int[])values.Clone();
            Array.Sort(sorted);

            int n = sorted.Length;
            int mid = n / 2;

            return (n % 2 == 1)
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        /// <summary>
        /// Median Absolute Deviation (robust dispersion metric).
        /// </summary>
        private static int Mad(int[] values, int median)
        {
            if (values.Length == 0)
            {
                return 0;
            }

            int[] deviations = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                deviations[i] = Math.Abs(values[i] - median);
            }

            return Median(deviations);
        }
    }
}