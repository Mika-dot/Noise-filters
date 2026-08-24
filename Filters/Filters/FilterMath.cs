using System;
using System.Collections.Generic;
using System.Linq;

namespace Filters
{
    internal static class FilterMath
    {
        public static double[] Copy(IReadOnlyList<double> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var result = new double[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                if (double.IsNaN(data[i]) || double.IsInfinity(data[i]))
                    throw new ArgumentException("Signal contains NaN or Infinity.", nameof(data));
                result[i] = data[i];
            }
            return result;
        }

        public static void PositiveWindow(int window, string name = "window")
        {
            if (window < 1) throw new ArgumentOutOfRangeException(name, "Window must be positive.");
        }

        public static void OddWindow(int window, string name = "window")
        {
            PositiveWindow(window, name);
            if ((window & 1) == 0) throw new ArgumentException("Window must be odd.", name);
        }

        public static int Mirror(int index, int length)
        {
            if (length <= 1) return 0;
            while (index < 0 || index >= length)
                index = index < 0 ? -index : 2 * length - index - 2;
            return index;
        }

        public static double Median(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(x => x).ToArray();
            if (sorted.Length == 0) throw new ArgumentException("Sequence cannot be empty.", nameof(values));
            int middle = sorted.Length / 2;
            return (sorted.Length & 1) == 1
                ? sorted[middle]
                : (sorted[middle - 1] + sorted[middle]) / 2.0;
        }

        public static double Percentile(IEnumerable<double> values, double percentile)
        {
            if (percentile < 0 || percentile > 1)
                throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be in [0, 1].");
            double[] sorted = values.OrderBy(x => x).ToArray();
            if (sorted.Length == 0) throw new ArgumentException("Sequence cannot be empty.", nameof(values));
            double position = percentile * (sorted.Length - 1);
            int lo = (int)Math.Floor(position);
            int hi = (int)Math.Ceiling(position);
            if (lo == hi) return sorted[lo];
            double fraction = position - lo;
            return sorted[lo] * (1 - fraction) + sorted[hi] * fraction;
        }

        public static double[] Window(double[] data, int center, int radius)
        {
            var window = new double[radius * 2 + 1];
            for (int offset = -radius; offset <= radius; offset++)
                window[offset + radius] = data[Mirror(center + offset, data.Length)];
            return window;
        }

        public static double[,] Invert(double[,] matrix)
        {
            int n = matrix.GetLength(0);
            if (n != matrix.GetLength(1)) throw new ArgumentException("Matrix must be square.");
            var augmented = new double[n, 2 * n];
            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++) augmented[row, col] = matrix[row, col];
                augmented[row, n + row] = 1;
            }

            for (int pivot = 0; pivot < n; pivot++)
            {
                int best = pivot;
                for (int row = pivot + 1; row < n; row++)
                    if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot])) best = row;
                if (Math.Abs(augmented[best, pivot]) < 1e-14) throw new ArgumentException("Matrix is singular.");
                if (best != pivot)
                    for (int col = 0; col < 2 * n; col++)
                    {
                        double tmp = augmented[pivot, col];
                        augmented[pivot, col] = augmented[best, col];
                        augmented[best, col] = tmp;
                    }

                double divisor = augmented[pivot, pivot];
                for (int col = 0; col < 2 * n; col++) augmented[pivot, col] /= divisor;
                for (int row = 0; row < n; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    for (int col = 0; col < 2 * n; col++)
                        augmented[row, col] -= factor * augmented[pivot, col];
                }
            }

            var inverse = new double[n, n];
            for (int row = 0; row < n; row++)
                for (int col = 0; col < n; col++) inverse[row, col] = augmented[row, n + col];
            return inverse;
        }
    }
}
