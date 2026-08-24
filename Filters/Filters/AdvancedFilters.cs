using System;
using System.Collections.Generic;
using System.Linq;

namespace Filters
{
    /// <summary>
    /// Additional signal processing filters.
    /// Designed for sensor data, embedded systems and measurement processing.
    /// </summary>
    public static class AdvancedFilters
    {
        public static double[] WeightedMovingAverage(double[] data, double[] weights)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (weights.Length == 0) throw new ArgumentException("Weights cannot be empty");
            if (weights.Any(x => x < 0)) throw new ArgumentException("Weights cannot be negative");
            if (weights.All(x => x == 0)) throw new ArgumentException("At least one weight must be positive");
            double[] result = new double[data.Length];
            int radius = weights.Length / 2;

            for (int i = 0; i < data.Length; i++)
            {
                double sum = 0;
                double weight = 0;
                for (int j = 0; j < weights.Length; j++)
                {
                    int index = i + j - radius;
                    if (index >= 0 && index < data.Length)
                    {
                        sum += data[index] * weights[j];
                        weight += weights[j];
                    }
                }
                result[i] = sum / weight;
            }
            return result;
        }

        public static double[] GaussianFilter(double[] data, int size = 5, double sigma = 1.0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (size < 3 || size % 2 == 0) throw new ArgumentException("Size must be an odd number greater than one");
            if (sigma <= 0) throw new ArgumentOutOfRangeException(nameof(sigma), "Sigma must be positive");
            double[] kernel = new double[size];
            int center = size / 2;
            double total = 0;
            for (int i = 0; i < size; i++)
            {
                double x = i - center;
                kernel[i] = Math.Exp(-(x * x) / (2 * sigma * sigma));
                total += kernel[i];
            }
            for (int i = 0; i < size; i++) kernel[i] /= total;
            return WeightedMovingAverage(data, kernel);
        }

        public static double[] HampelFilter(double[] data, int window = 3, double threshold = 3.0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (window < 1) throw new ArgumentOutOfRangeException(nameof(window));
            if (threshold <= 0) throw new ArgumentOutOfRangeException(nameof(threshold));
            double[] result = (double[])data.Clone();
            for (int i = window; i < data.Length - window; i++)
            {
                var segment = data.Skip(i - window).Take(window * 2 + 1).OrderBy(x => x).ToArray();
                double median = segment[segment.Length / 2];
                double mad = segment.Select(x => Math.Abs(x - median)).OrderBy(x => x).ElementAt(segment.Length / 2);
                // 1.4826 converts MAD to a robust estimate of standard deviation.
                double limit = threshold * 1.4826 * mad;
                if ((mad == 0 && data[i] != median) || (mad > 0 && Math.Abs(data[i] - median) > limit))
                    result[i] = median;
            }
            return result;
        }

        public static double[] SavitzkyGolay(double[] data, int window = 5, int polynomialOrder = 2)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (window < 3 || window % 2 == 0) throw new ArgumentException("Window must be an odd number greater than one");
            if (polynomialOrder < 0 || polynomialOrder >= window)
                throw new ArgumentOutOfRangeException(nameof(polynomialOrder), "Polynomial order must be smaller than the window");

            double[] result = new double[data.Length];
            int radius = window / 2;
            double[] coefficients = SmoothingCoefficients(window, polynomialOrder);

            for (int i = 0; i < data.Length; i++)
            {
                double sum = 0;
                for (int j = 0; j < window; j++)
                {
                    int index = Math.Max(0, Math.Min(data.Length - 1, i + j - radius));
                    sum += coefficients[j] * data[index];
                }
                result[i] = sum;
            }
            return result;
        }

        private static double[] SmoothingCoefficients(int window, int order)
        {
            int radius = window / 2;
            double[,] normal = new double[order + 1, order + 1];
            double[] rhs = new double[order + 1];
            rhs[0] = 1.0;

            for (int row = 0; row <= order; row++)
                for (int col = 0; col <= order; col++)
                    for (int x = -radius; x <= radius; x++)
                        normal[row, col] += Math.Pow(x, row + col);

            double[] polynomial = Solve(normal, rhs);
            double[] coefficients = new double[window];
            for (int i = 0; i < window; i++)
            {
                double x = i - radius;
                for (int power = 0; power <= order; power++)
                    coefficients[i] += polynomial[power] * Math.Pow(x, power);
            }
            return coefficients;
        }

        private static double[] Solve(double[,] matrix, double[] vector)
        {
            int size = vector.Length;
            double[,] augmented = new double[size, size + 1];
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++) augmented[row, col] = matrix[row, col];
                augmented[row, size] = vector[row];
            }

            for (int pivot = 0; pivot < size; pivot++)
            {
                int best = pivot;
                for (int row = pivot + 1; row < size; row++)
                    if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot])) best = row;
                if (Math.Abs(augmented[best, pivot]) < 1e-12) throw new ArgumentException("Cannot build Savitzky-Golay coefficients");

                for (int col = pivot; col <= size; col++)
                {
                    double swap = augmented[pivot, col];
                    augmented[pivot, col] = augmented[best, col];
                    augmented[best, col] = swap;
                }

                double divisor = augmented[pivot, pivot];
                for (int col = pivot; col <= size; col++) augmented[pivot, col] /= divisor;
                for (int row = 0; row < size; row++)
                {
                    if (row == pivot) continue;
                    double factor = augmented[row, pivot];
                    for (int col = pivot; col <= size; col++) augmented[row, col] -= factor * augmented[pivot, col];
                }
            }

            double[] solution = new double[size];
            for (int row = 0; row < size; row++) solution[row] = augmented[row, size];
            return solution;
        }

        public static double[] Deadband(double[] data, double limit)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
            double[] result = new double[data.Length];
            double last = data.Length > 0 ? data[0] : 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (Math.Abs(data[i] - last) >= limit)
                    last = data[i];
                result[i] = last;
            }
            return result;
        }

        public static double[] MedianAbsoluteDeviation(double[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return new double[0];
            double median = data.OrderBy(x => x).ElementAt(data.Length / 2);
            return data.Select(x => Math.Abs(x - median)).ToArray();
        }
    }
}
