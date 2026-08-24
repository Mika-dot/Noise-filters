using System;
using System.Collections.Generic;

namespace Filters
{
    /// <summary>Helpers used by examples to compare filters objectively.</summary>
    public static class FilterMetrics
    {
        public static double RootMeanSquareError(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
        {
            ValidatePair(expected, actual);
            if (expected.Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < expected.Count; i++)
            {
                double error = expected[i] - actual[i];
                sum += error * error;
            }
            return Math.Sqrt(sum / expected.Count);
        }

        public static double MeanAbsoluteError(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
        {
            ValidatePair(expected, actual);
            if (expected.Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < expected.Count; i++) sum += Math.Abs(expected[i] - actual[i]);
            return sum / expected.Count;
        }

        public static double NoiseReductionPercent(IReadOnlyList<double> clean, IReadOnlyList<double> noisy, IReadOnlyList<double> filtered)
        {
            double before = RootMeanSquareError(clean, noisy);
            double after = RootMeanSquareError(clean, filtered);
            return before < 1e-14 ? 0 : 100 * (1 - after / before);
        }

        private static void ValidatePair(IReadOnlyList<double> first, IReadOnlyList<double> second)
        {
            if (first == null || second == null) throw new ArgumentNullException();
            if (first.Count != second.Count) throw new ArgumentException("Signals must have equal length.");
        }
    }
}
