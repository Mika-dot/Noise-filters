using System;
using System.Collections.Generic;

namespace Filters
{
    /// <summary>DSP-oriented, physical-process and control-loop filters.</summary>
    public static class SignalFilters
    {
        public static double[] Gaussian(IReadOnlyList<double> data, int window = 7, double sigma = 1.5)
        {
            FilterMath.OddWindow(window);
            if (sigma <= 0) throw new ArgumentOutOfRangeException(nameof(sigma));
            int radius = window / 2;
            var kernel = new double[window];
            for (int i = 0; i < window; i++)
            {
                double x = i - radius;
                kernel[i] = Math.Exp(-(x * x) / (2 * sigma * sigma));
            }
            return BasicFilters.WeightedMovingAverage(data, kernel);
        }

        public static double[] SavitzkyGolay(IReadOnlyList<double> data, int window = 7, int polynomialOrder = 3)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            if (polynomialOrder < 0 || polynomialOrder >= window)
                throw new ArgumentOutOfRangeException(nameof(polynomialOrder), "Order must be non-negative and smaller than the window.");
            int radius = window / 2;
            int columns = polynomialOrder + 1;
            var ata = new double[columns, columns];
            for (int row = 0; row < columns; row++)
                for (int col = 0; col < columns; col++)
                    for (int x = -radius; x <= radius; x++) ata[row, col] += Math.Pow(x, row + col);
            double[,] inverse = FilterMath.Invert(ata);
            var coefficients = new double[window];
            for (int x = -radius; x <= radius; x++)
            {
                double coefficient = 0;
                for (int power = 0; power < columns; power++) coefficient += inverse[0, power] * Math.Pow(x, power);
                coefficients[x + radius] = coefficient;
            }
            var result = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
                for (int tap = 0; tap < coefficients.Length; tap++)
                    result[i] += coefficients[tap] * input[FilterMath.Mirror(i + tap - radius, input.Length)];
            return result;
        }

        public static double[] Fir(IReadOnlyList<double> data, IReadOnlyList<double> coefficients)
        {
            double[] input = FilterMath.Copy(data);
            if (coefficients == null) throw new ArgumentNullException(nameof(coefficients));
            if (coefficients.Count == 0) throw new ArgumentException("Coefficients cannot be empty.", nameof(coefficients));
            var result = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
                for (int tap = 0; tap < coefficients.Count; tap++)
                    if (i - tap >= 0) result[i] += coefficients[tap] * input[i - tap];
            return result;
        }

        public static double[] LowPassRc(IReadOnlyList<double> data, double cutoffHz, double sampleRateHz)
        {
            double[] input = FilterMath.Copy(data);
            if (cutoffHz <= 0) throw new ArgumentOutOfRangeException(nameof(cutoffHz));
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            double dt = 1.0 / sampleRateHz;
            double rc = 1.0 / (2 * Math.PI * cutoffHz);
            double alpha = dt / (rc + dt);
            result[0] = input[0];
            for (int i = 1; i < input.Length; i++) result[i] = result[i - 1] + alpha * (input[i] - result[i - 1]);
            return result;
        }

        public static double[] HighPassRc(IReadOnlyList<double> data, double cutoffHz, double sampleRateHz)
        {
            double[] input = FilterMath.Copy(data);
            if (cutoffHz <= 0) throw new ArgumentOutOfRangeException(nameof(cutoffHz));
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            double dt = 1.0 / sampleRateHz;
            double rc = 1.0 / (2 * Math.PI * cutoffHz);
            double alpha = rc / (rc + dt);
            for (int i = 1; i < input.Length; i++) result[i] = alpha * (result[i - 1] + input[i] - input[i - 1]);
            return result;
        }

        public static double[] Complementary(IReadOnlyList<double> lowFrequencySignal, IReadOnlyList<double> highFrequencySignal, double alpha = 0.98)
        {
            double[] low = FilterMath.Copy(lowFrequencySignal);
            double[] high = FilterMath.Copy(highFrequencySignal);
            if (low.Length != high.Length) throw new ArgumentException("Signals must have equal length.");
            if (alpha < 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
            var result = new double[low.Length];
            for (int i = 0; i < result.Length; i++) result[i] = alpha * high[i] + (1 - alpha) * low[i];
            return result;
        }

        public static double[] OneEuro(IReadOnlyList<double> data, double sampleRateHz, double minCutoff = 1.0, double beta = 0.01, double derivativeCutoff = 1.0)
        {
            double[] input = FilterMath.Copy(data);
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (minCutoff <= 0 || derivativeCutoff <= 0) throw new ArgumentOutOfRangeException("Cutoffs must be positive.");
            if (beta < 0) throw new ArgumentOutOfRangeException(nameof(beta));
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            double previousDerivative = 0;
            result[0] = input[0];
            for (int i = 1; i < input.Length; i++)
            {
                double derivative = (input[i] - input[i - 1]) * sampleRateHz;
                double derivativeAlpha = SmoothingFactor(derivativeCutoff, sampleRateHz);
                previousDerivative += derivativeAlpha * (derivative - previousDerivative);
                double cutoff = minCutoff + beta * Math.Abs(previousDerivative);
                double alpha = SmoothingFactor(cutoff, sampleRateHz);
                result[i] = result[i - 1] + alpha * (input[i] - result[i - 1]);
            }
            return result;
        }

        public static double[] Bilateral(IReadOnlyList<double> data, int window = 7, double spatialSigma = 2.0, double rangeSigma = 5.0)
        {
            double[] input = FilterMath.Copy(data);
            FilterMath.OddWindow(window);
            if (spatialSigma <= 0 || rangeSigma <= 0) throw new ArgumentOutOfRangeException("Sigmas must be positive.");
            int radius = window / 2;
            var result = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                double sum = 0;
                double weights = 0;
                for (int offset = -radius; offset <= radius; offset++)
                {
                    double neighbor = input[FilterMath.Mirror(i + offset, input.Length)];
                    double delta = neighbor - input[i];
                    double weight = Math.Exp(-(offset * offset) / (2 * spatialSigma * spatialSigma))
                                    * Math.Exp(-(delta * delta) / (2 * rangeSigma * rangeSigma));
                    sum += neighbor * weight;
                    weights += weight;
                }
                result[i] = sum / weights;
            }
            return result;
        }

        public static double[] Deadband(IReadOnlyList<double> data, double width)
        {
            double[] input = FilterMath.Copy(data);
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            result[0] = input[0];
            for (int i = 1; i < input.Length; i++) result[i] = Math.Abs(input[i] - result[i - 1]) >= width ? input[i] : result[i - 1];
            return result;
        }

        public static double[] SlewRateLimiter(IReadOnlyList<double> data, double maxRisePerSample, double maxFallPerSample)
        {
            double[] input = FilterMath.Copy(data);
            if (maxRisePerSample <= 0 || maxFallPerSample <= 0) throw new ArgumentOutOfRangeException("Rates must be positive.");
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            result[0] = input[0];
            for (int i = 1; i < input.Length; i++)
            {
                double delta = input[i] - result[i - 1];
                result[i] = result[i - 1] + Math.Min(maxRisePerSample, Math.Max(-maxFallPerSample, delta));
            }
            return result;
        }

        public static double[] Debounce(IReadOnlyList<double> data, int stableSamples = 3, double tolerance = 0.0)
        {
            double[] input = FilterMath.Copy(data);
            if (stableSamples < 1) throw new ArgumentOutOfRangeException(nameof(stableSamples));
            if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            var result = new double[input.Length];
            if (input.Length == 0) return result;
            double accepted = input[0];
            double candidate = accepted;
            int count = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (Math.Abs(input[i] - accepted) <= tolerance) count = 0;
                else if (Math.Abs(input[i] - candidate) <= tolerance) count++;
                else { candidate = input[i]; count = 1; }
                if (count >= stableSamples) { accepted = candidate; count = 0; }
                result[i] = accepted;
            }
            return result;
        }

        private static double SmoothingFactor(double cutoffHz, double sampleRateHz)
        {
            double tau = 1.0 / (2 * Math.PI * cutoffHz);
            double dt = 1.0 / sampleRateHz;
            return 1.0 / (1.0 + tau / dt);
        }
    }
}
