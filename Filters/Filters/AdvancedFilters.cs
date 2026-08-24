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
            if (weights.Length == 0) throw new ArgumentException("Weights cannot be empty");
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
            double[] result = (double[])data.Clone();
            for (int i = window; i < data.Length - window; i++)
            {
                var segment = data.Skip(i - window).Take(window * 2 + 1).OrderBy(x => x).ToArray();
                double median = segment[segment.Length / 2];
                double mad = segment.Select(x => Math.Abs(x - median)).OrderBy(x => x).ElementAt(segment.Length / 2);
                if (Math.Abs(data[i] - median) > threshold * mad)
                    result[i] = median;
            }
            return result;
        }

        public static double[] SavitzkyGolay(double[] data, int window = 5)
        {
            double[] result = new double[data.Length];
            int radius = window / 2;
            for (int i = 0; i < data.Length; i++)
            {
                double sum = 0;
                int count = 0;
                for (int j = -radius; j <= radius; j++)
                {
                    int index = i + j;
                    if (index >= 0 && index < data.Length)
                    {
                        sum += data[index];
                        count++;
                    }
                }
                result[i] = sum / count;
            }
            return result;
        }

        public static double[] Deadband(double[] data, double limit)
        {
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
            double median = data.OrderBy(x => x).ElementAt(data.Length / 2);
            return data.Select(x => Math.Abs(x - median)).ToArray();
        }
    }
}
