using System;

namespace Filters
{
    /// <summary>Compatibility facade retained for the first advanced branch revision.</summary>
    public static class AdvancedFilters
    {
        public static double[] WeightedMovingAverage(double[] data, double[] weights) => BasicFilters.WeightedMovingAverage(data, weights);

        public static double[] GaussianFilter(double[] data, int size = 5, double sigma = 1.0) => SignalFilters.Gaussian(data, size, sigma);

        public static double[] HampelFilter(double[] data, int window = 3, double threshold = 3.0) => RobustFilters.Hampel(data, window * 2 + 1, threshold);

        public static double[] SavitzkyGolay(double[] data, int window = 5) => SignalFilters.SavitzkyGolay(data, window, Math.Min(2, window - 1));

        public static double[] Deadband(double[] data, double limit) => SignalFilters.Deadband(data, limit);

        public static double[] MedianAbsoluteDeviation(double[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length == 0) return new double[0];
            double median = FilterMath.Median(data);
            var result = new double[data.Length];
            for (int i = 0; i < data.Length; i++) result[i] = Math.Abs(data[i] - median);
            return result;
        }
    }
}
