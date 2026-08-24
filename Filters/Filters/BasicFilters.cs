using System;
using System.Collections.Generic;
using System.Linq;

namespace Filters
{
    /// <summary>General-purpose smoothing and statistical window filters.</summary>
    public static class BasicFilters
    {
        public static double[] MovingAverage(IReadOnlyList<double> data, int window = 5)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.PositiveWindow(window);
            var result = new double[input.Length];
            double sum = 0;
            for (int i = 0; i < input.Length; i++)
            {
                sum += input[i];
                if (i >= window) sum -= input[i - window];
                result[i] = sum / Math.Min(i + 1, window);
            }
            return result;
        }

        public static double[] CenteredMovingAverage(IReadOnlyList<double> data, int window = 5)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            var result = new double[input.Length];
            int radius = window / 2;
            for (int i = 0; i < input.Length; i++) result[i] = FilterMath.Window(input, i, radius).Average();
            return result;
        }

        public static double[] WeightedMovingAverage(IReadOnlyList<double> data, IReadOnlyList<double> weights, bool centered = true)
        {
            double[] input = FilterMath.Copy(data);
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (weights.Count == 0) throw new ArgumentException("Weights cannot be empty.", nameof(weights));
            double total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] < 0 || double.IsNaN(weights[i]) || double.IsInfinity(weights[i]))
                    throw new ArgumentException("Weights must be finite and non-negative.", nameof(weights));
                total += weights[i];
            }
            if (total <= 0) throw new ArgumentException("At least one weight must be positive.", nameof(weights));
            var result = new double[input.Length];
            int anchor = centered ? weights.Count / 2 : weights.Count - 1;
            for (int i = 0; i < input.Length; i++)
            {
                double sum = 0;
                for (int tap = 0; tap < weights.Count; tap++)
                    sum += input[FilterMath.Mirror(i + tap - anchor, input.Length)] * weights[tap];
                result[i] = sum / total;
            }
            return result;
        }

        public static double[] TriangularMovingAverage(IReadOnlyList<double> data, int window = 5)
        {
            FilterMath.OddWindow(window);
            int radius = window / 2;
            var weights = new double[window];
            for (int i = 0; i < window; i++) weights[i] = radius + 1 - Math.Abs(i - radius);
            return WeightedMovingAverage(data, weights);
        }

        public static double[] ExponentialMovingAverage(IReadOnlyList<double> data, double alpha = 0.2)
        {
            double[] input = FilterMath.Copy(data);
            if (alpha <= 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be in (0, 1].");
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            result[0] = input[0];
            for (int i = 1; i < input.Length; i++) result[i] = alpha * input[i] + (1 - alpha) * result[i - 1];
            return result;
        }

        public static double[] DoubleExponentialMovingAverage(IReadOnlyList<double> data, double alpha = 0.2)
        {
            double[] first = ExponentialMovingAverage(data, alpha);
            double[] second = ExponentialMovingAverage(first, alpha);
            var result = new double[first.Length];
            for (int i = 0; i < result.Length; i++) result[i] = 2 * first[i] - second[i];
            return result;
        }

        public static double[] HoltLinearTrend(IReadOnlyList<double> data, double alpha = 0.3, double beta = 0.1)
        {
            double[] input = FilterMath.Copy(data);
            if (alpha <= 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
            if (beta < 0 || beta > 1) throw new ArgumentOutOfRangeException(nameof(beta));
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            double level = input[0];
            double trend = input.Length > 1 ? input[1] - input[0] : 0;
            result[0] = level;
            for (int i = 1; i < input.Length; i++)
            {
                double previousLevel = level;
                level = alpha * input[i] + (1 - alpha) * (level + trend);
                trend = beta * (level - previousLevel) + (1 - beta) * trend;
                result[i] = level;
            }
            return result;
        }

        public static double[] Median(IReadOnlyList<double> data, int window = 5)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            var result = new double[input.Length];
            int radius = window / 2;
            for (int i = 0; i < input.Length; i++) result[i] = FilterMath.Median(FilterMath.Window(input, i, radius));
            return result;
        }

        public static double[] Percentile(IReadOnlyList<double> data, int window = 5, double percentile = 0.5)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            var result = new double[input.Length];
            int radius = window / 2;
            for (int i = 0; i < input.Length; i++) result[i] = FilterMath.Percentile(FilterMath.Window(input, i, radius), percentile);
            return result;
        }

        public static double[] Mode(IReadOnlyList<double> data, int window = 5, double binWidth = 1.0)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            if (binWidth <= 0) throw new ArgumentOutOfRangeException(nameof(binWidth));
            var result = new double[input.Length];
            int radius = window / 2;
            for (int i = 0; i < input.Length; i++)
            {
                double[] values = FilterMath.Window(input, i, radius);
                var best = values.GroupBy(x => Math.Round(x / binWidth)).OrderByDescending(g => g.Count())
                    .ThenBy(g => Math.Abs(g.Average() - input[i])).First();
                result[i] = best.Average();
            }
            return result;
        }
    }
}
