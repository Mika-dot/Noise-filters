using System;
using System.Collections.Generic;
using System.Linq;

namespace Filters
{
    /// <summary>Filters resistant to isolated spikes and heavy-tailed noise.</summary>
    public static class RobustFilters
    {
        public static double[] Hampel(IReadOnlyList<double> data, int window = 7, double threshold = 3.0)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            if (threshold <= 0) throw new ArgumentOutOfRangeException(nameof(threshold));
            var result = (double[])input.Clone();
            int radius = window / 2;
            for (int i = 0; i < input.Length; i++)
            {
                double[] values = FilterMath.Window(input, i, radius);
                double median = FilterMath.Median(values);
                double mad = FilterMath.Median(values.Select(x => Math.Abs(x - median)));
                double limit = threshold * 1.4826 * mad;
                if (Math.Abs(input[i] - median) > Math.Max(limit, 1e-12)) result[i] = median;
            }
            return result;
        }

        public static double[] MedianAbsoluteDeviationCleaner(IReadOnlyList<double> data, double threshold = 3.5)
        {
            double[] input = FilterMath.Copy(data);
            if (input.Length == 0) return input;
            if (threshold <= 0) throw new ArgumentOutOfRangeException(nameof(threshold));
            double median = FilterMath.Median(input);
            double mad = FilterMath.Median(input.Select(x => Math.Abs(x - median)));
            if (mad < 1e-14) return input.Select(x => Math.Abs(x - median) < 1e-14 ? x : median).ToArray();
            return input.Select(x => Math.Abs(0.6745 * (x - median) / mad) > threshold ? median : x).ToArray();
        }

        public static double[] SigmaClip(IReadOnlyList<double> data, double sigma = 3.0, int iterations = 2)
        {
            double[] input = FilterMath.Copy(data);
            if (sigma <= 0) throw new ArgumentOutOfRangeException(nameof(sigma));
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
            var result = (double[])input.Clone();
            for (int pass = 0; pass < iterations && result.Length > 0; pass++)
            {
                double mean = result.Average();
                double deviation = Math.Sqrt(result.Select(x => (x - mean) * (x - mean)).Average());
                if (deviation < 1e-14) break;
                for (int i = 0; i < result.Length; i++)
                    if (Math.Abs(result[i] - mean) > sigma * deviation) result[i] = mean;
            }
            return result;
        }

        public static double[] TukeyFence(IReadOnlyList<double> data, double factor = 1.5)
        {
            double[] input = FilterMath.Copy(data);
            if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
            if (input.Length == 0) return input;
            double q1 = FilterMath.Percentile(input, 0.25);
            double q3 = FilterMath.Percentile(input, 0.75);
            double iqr = q3 - q1;
            double lower = q1 - factor * iqr;
            double upper = q3 + factor * iqr;
            return input.Select(x => Math.Min(upper, Math.Max(lower, x))).ToArray();
        }

        public static double[] TrimmedMean(IReadOnlyList<double> data, int window = 7, double trimFraction = 0.2)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            if (trimFraction < 0 || trimFraction >= 0.5) throw new ArgumentOutOfRangeException(nameof(trimFraction));
            var result = new double[input.Length];
            int radius = window / 2;
            for (int i = 0; i < input.Length; i++)
            {
                double[] values = FilterMath.Window(input, i, radius).OrderBy(x => x).ToArray();
                int trim = (int)Math.Floor(values.Length * trimFraction);
                result[i] = values.Skip(trim).Take(values.Length - 2 * trim).Average();
            }
            return result;
        }

        public static double[] WinsorizedMean(IReadOnlyList<double> data, int window = 7, double fraction = 0.2)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            if (fraction < 0 || fraction >= 0.5) throw new ArgumentOutOfRangeException(nameof(fraction));
            var result = new double[input.Length];
            int radius = window / 2;
            for (int i = 0; i < input.Length; i++)
            {
                double[] values = FilterMath.Window(input, i, radius).OrderBy(x => x).ToArray();
                int trim = (int)Math.Floor(values.Length * fraction);
                double low = values[trim];
                double high = values[values.Length - trim - 1];
                result[i] = values.Select(x => Math.Min(high, Math.Max(low, x))).Average();
            }
            return result;
        }

        public static double[] AdaptiveMedian(IReadOnlyList<double> data, int maxWindow = 9)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(maxWindow, nameof(maxWindow));
            if (maxWindow < 3) throw new ArgumentOutOfRangeException(nameof(maxWindow));
            var result = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = input[i];
                for (int window = 3; window <= maxWindow; window += 2)
                {
                    double[] values = FilterMath.Window(input, i, window / 2);
                    double min = values.Min();
                    double max = values.Max();
                    double median = FilterMath.Median(values);
                    if (median > min && median < max)
                    {
                        result[i] = input[i] > min && input[i] < max ? input[i] : median;
                        break;
                    }
                    if (window == maxWindow) result[i] = median;
                }
            }
            return result;
        }
    }
}
