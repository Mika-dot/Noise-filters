using System;
using System.Collections.Generic;

namespace Filters
{
    /// <summary>
    /// Modern dependency-free one-dimensional denoising methods.
    /// These implementations are practical research baselines inspired by recent
    /// adaptive-Kalman, SSA/SVD and wavelet-denoising literature; they are not
    /// claimed to be verbatim reproductions of any single paper.
    /// </summary>
    public static class ModernDenoisingFilters
    {
        /// <summary>
        /// Multilevel orthonormal Haar wavelet shrinkage. The noise scale is estimated
        /// from the finest detail band using MAD and a universal threshold.
        /// </summary>
        public static double[] WaveletHaarShrinkage(
            IReadOnlyList<double> data,
            int levels = 0,
            double thresholdMultiplier = 1.0,
            bool softThreshold = true)
        {
            double[] input = CopyFinite(data);
            if (input.Length <= 1) return input;
            if (thresholdMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(thresholdMultiplier));

            int paddedLength = NextPowerOfTwo(input.Length);
            int maxLevels = 0;
            for (int n = paddedLength; n > 1; n >>= 1) maxLevels++;
            if (levels == 0) levels = maxLevels;
            if (levels < 1 || levels > maxLevels) throw new ArgumentOutOfRangeException(nameof(levels));

            var coeffs = new double[paddedLength];
            for (int i = 0; i < paddedLength; i++) coeffs[i] = input[Mirror(i, input.Length)];

            const double invSqrt2 = 0.70710678118654752440;
            var temp = new double[paddedLength];
            int length = paddedLength;
            for (int level = 0; level < levels; level++)
            {
                int half = length / 2;
                for (int i = 0; i < half; i++)
                {
                    double a = coeffs[2 * i];
                    double b = coeffs[2 * i + 1];
                    temp[i] = (a + b) * invSqrt2;
                    temp[half + i] = (a - b) * invSqrt2;
                }
                Array.Copy(temp, 0, coeffs, 0, length);
                length = half;
            }

            int finestStart = paddedLength / 2;
            var absoluteDetails = new double[paddedLength - finestStart];
            for (int i = finestStart; i < paddedLength; i++)
                absoluteDetails[i - finestStart] = Math.Abs(coeffs[i]);
            double sigma = Median(absoluteDetails) / 0.6744897501960817;
            double threshold = thresholdMultiplier * sigma * Math.Sqrt(2.0 * Math.Log(Math.Max(2, input.Length)));

            int approximationLength = paddedLength >> levels;
            for (int i = approximationLength; i < paddedLength; i++)
            {
                double value = coeffs[i];
                if (softThreshold)
                {
                    double magnitude = Math.Max(0.0, Math.Abs(value) - threshold);
                    coeffs[i] = Math.Sign(value) * magnitude;
                }
                else if (Math.Abs(value) < threshold)
                {
                    coeffs[i] = 0.0;
                }
            }

            int reconstructedLength = approximationLength * 2;
            while (reconstructedLength <= paddedLength)
            {
                int half = reconstructedLength / 2;
                for (int i = 0; i < half; i++)
                {
                    double average = coeffs[i];
                    double detail = coeffs[half + i];
                    temp[2 * i] = (average + detail) * invSqrt2;
                    temp[2 * i + 1] = (average - detail) * invSqrt2;
                }
                Array.Copy(temp, 0, coeffs, 0, reconstructedLength);
                if (reconstructedLength == paddedLength) break;
                reconstructedLength *= 2;
            }

            var result = new double[input.Length];
            Array.Copy(coeffs, result, result.Length);
            return result;
        }

        /// <summary>
        /// Singular Spectrum Analysis (SSA): Hankel embedding, truncated eigenspace
        /// reconstruction and diagonal averaging. Good for smooth/quasi-periodic signals.
        /// </summary>
        public static double[] SingularSpectrumAnalysis(
            IReadOnlyList<double> data,
            int window = 0,
            int rank = 2)
        {
            double[] input = CopyFinite(data);
            if (input.Length < 3) return input;
            ResolveEmbedding(input.Length, ref window, rank, out int columns);

            double[,] trajectory = BuildTrajectory(input, window, columns);
            EigenDecomposition(trajectory, out double[] eigenvalues, out double[,] eigenvectors);
            int[] order = SortEigenvaluesDescending(eigenvalues);
            int usableRank = Math.Min(rank, Math.Min(window, columns));

            return ReconstructFromSubspace(trajectory, eigenvalues, eigenvectors, order, usableRank, null, input.Length);
        }

        /// <summary>
        /// Reweighted singular-value shrinkage over an SSA trajectory matrix.
        /// Large coherent components are preserved while weak singular components are
        /// suppressed. With regularization=0 the strength is estimated from the lower
        /// half of the singular spectrum.
        /// </summary>
        public static double[] ReweightedSvdDenoise(
            IReadOnlyList<double> data,
            int window = 0,
            double regularization = 0.0,
            double epsilon = 1e-8)
        {
            double[] input = CopyFinite(data);
            if (input.Length < 3) return input;
            if (regularization < 0) throw new ArgumentOutOfRangeException(nameof(regularization));
            if (epsilon <= 0) throw new ArgumentOutOfRangeException(nameof(epsilon));

            int requestedRank = 1;
            ResolveEmbedding(input.Length, ref window, requestedRank, out int columns);
            double[,] trajectory = BuildTrajectory(input, window, columns);
            EigenDecomposition(trajectory, out double[] eigenvalues, out double[,] eigenvectors);
            int[] order = SortEigenvaluesDescending(eigenvalues);
            int componentCount = Math.Min(window, columns);
            var singularValues = new double[componentCount];
            for (int i = 0; i < componentCount; i++)
                singularValues[i] = Math.Sqrt(Math.Max(0.0, eigenvalues[order[i]]));

            if (regularization == 0.0)
            {
                int tailStart = componentCount / 2;
                var tail = new double[Math.Max(1, componentCount - tailStart)];
                for (int i = 0; i < tail.Length; i++) tail[i] = singularValues[tailStart + i];
                double noiseFloor = Median(tail);
                regularization = Math.Max(epsilon, noiseFloor * noiseFloor);
            }

            var ratios = new double[componentCount];
            for (int i = 0; i < componentCount; i++)
            {
                double s = singularValues[i];
                if (s <= epsilon)
                {
                    ratios[i] = 0.0;
                    continue;
                }
                double shrunk = Math.Max(0.0, s - regularization / (s + epsilon));
                ratios[i] = shrunk / s;
            }

            return ReconstructFromSubspace(trajectory, eigenvalues, eigenvectors, order, componentCount, ratios, input.Length);
        }

        /// <summary>
        /// One-dimensional Rudin-Osher-Fatemi total-variation denoising solved through
        /// the projected dual problem. Preserves abrupt steps better than ordinary LPF.
        /// </summary>
        public static double[] TotalVariationDenoise(
            IReadOnlyList<double> data,
            double lambda = 1.0,
            int iterations = 120)
        {
            double[] input = CopyFinite(data);
            if (lambda < 0) throw new ArgumentOutOfRangeException(nameof(lambda));
            if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));
            if (input.Length <= 1 || lambda == 0) return input;

            int n = input.Length;
            var dual = new double[n - 1];
            var x = new double[n];
            const double step = 0.24;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                ApplyPrimal(input, dual, x);
                for (int i = 0; i < dual.Length; i++)
                {
                    double value = dual[i] + step * (x[i + 1] - x[i]);
                    if (value > lambda) value = lambda;
                    else if (value < -lambda) value = -lambda;
                    dual[i] = value;
                }
            }

            ApplyPrimal(input, dual, x);
            return x;
        }

        /// <summary>
        /// Robust innovation-adaptive scalar Kalman filter. Measurement variance is
        /// updated online and large normalized innovations receive Huber-style downweighting.
        /// </summary>
        public static double[] RobustAdaptiveKalman(
            IReadOnlyList<double> data,
            double measurementNoise = 4.0,
            double processNoise = 0.05,
            double adaptation = 0.03,
            double huberK = 2.5)
        {
            double[] input = CopyFinite(data);
            if (measurementNoise <= 0) throw new ArgumentOutOfRangeException(nameof(measurementNoise));
            if (processNoise < 0) throw new ArgumentOutOfRangeException(nameof(processNoise));
            if (adaptation <= 0 || adaptation > 1) throw new ArgumentOutOfRangeException(nameof(adaptation));
            if (huberK <= 0) throw new ArgumentOutOfRangeException(nameof(huberK));
            if (input.Length == 0) return input;

            var result = new double[input.Length];
            double state = input[0];
            double covariance = measurementNoise;
            double adaptiveMeasurementNoise = measurementNoise;
            result[0] = state;

            for (int i = 1; i < input.Length; i++)
            {
                double predictedCovariance = covariance + processNoise;
                double innovation = input[i] - state;
                double innovationVariance = Math.Max(1e-12, predictedCovariance + adaptiveMeasurementNoise);
                double normalizedInnovation = Math.Abs(innovation) / Math.Sqrt(innovationVariance);
                double robustWeight = normalizedInnovation <= huberK ? 1.0 : huberK / normalizedInnovation;
                double effectiveMeasurementNoise = adaptiveMeasurementNoise / Math.Max(1e-12, robustWeight * robustWeight);

                double gain = predictedCovariance / (predictedCovariance + effectiveMeasurementNoise);
                state += gain * innovation;
                covariance = Math.Max(1e-12, (1.0 - gain) * predictedCovariance);

                double robustInnovation = robustWeight * innovation;
                double candidateNoise = Math.Max(1e-12, robustInnovation * robustInnovation - predictedCovariance);
                candidateNoise = Math.Max(adaptiveMeasurementNoise / 25.0,
                    Math.Min(adaptiveMeasurementNoise * 25.0, candidateNoise));
                adaptiveMeasurementNoise = (1.0 - adaptation) * adaptiveMeasurementNoise + adaptation * candidateNoise;
                result[i] = state;
            }

            return result;
        }

        private static void ResolveEmbedding(int sampleCount, ref int window, int rank, out int columns)
        {
            if (rank < 1) throw new ArgumentOutOfRangeException(nameof(rank));
            if (window == 0) window = Math.Min(64, Math.Max(2, sampleCount / 4));
            if (window < 2 || window >= sampleCount) throw new ArgumentOutOfRangeException(nameof(window));
            columns = sampleCount - window + 1;
            if (rank > Math.Min(window, columns)) throw new ArgumentOutOfRangeException(nameof(rank));
        }

        private static double[,] BuildTrajectory(double[] input, int rows, int columns)
        {
            var trajectory = new double[rows, columns];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < columns; j++)
                    trajectory[i, j] = input[i + j];
            return trajectory;
        }

        private static void EigenDecomposition(double[,] trajectory, out double[] eigenvalues, out double[,] eigenvectors)
        {
            int rows = trajectory.GetLength(0);
            int columns = trajectory.GetLength(1);
            var covariance = new double[rows, rows];
            for (int i = 0; i < rows; i++)
                for (int j = i; j < rows; j++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < columns; k++) sum += trajectory[i, k] * trajectory[j, k];
                    covariance[i, j] = sum;
                    covariance[j, i] = sum;
                }

            JacobiSymmetric(covariance, out eigenvalues, out eigenvectors);
        }

        private static void JacobiSymmetric(double[,] matrix, out double[] eigenvalues, out double[,] eigenvectors)
        {
            int n = matrix.GetLength(0);
            var a = (double[,])matrix.Clone();
            eigenvectors = new double[n, n];
            for (int i = 0; i < n; i++) eigenvectors[i, i] = 1.0;

            int maxIterations = Math.Max(32, 30 * n * n);
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                int p = 0;
                int q = 1;
                double largest = 0.0;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        double magnitude = Math.Abs(a[i, j]);
                        if (magnitude > largest)
                        {
                            largest = magnitude;
                            p = i;
                            q = j;
                        }
                    }

                if (largest < 1e-12) break;
                double phi = 0.5 * Math.Atan2(2.0 * a[p, q], a[q, q] - a[p, p]);
                double c = Math.Cos(phi);
                double s = Math.Sin(phi);

                double app = c * c * a[p, p] - 2.0 * s * c * a[p, q] + s * s * a[q, q];
                double aqq = s * s * a[p, p] + 2.0 * s * c * a[p, q] + c * c * a[q, q];

                for (int k = 0; k < n; k++)
                {
                    if (k == p || k == q) continue;
                    double akp = a[k, p];
                    double akq = a[k, q];
                    a[k, p] = a[p, k] = c * akp - s * akq;
                    a[k, q] = a[q, k] = s * akp + c * akq;
                }
                a[p, p] = app;
                a[q, q] = aqq;
                a[p, q] = a[q, p] = 0.0;

                for (int k = 0; k < n; k++)
                {
                    double vkp = eigenvectors[k, p];
                    double vkq = eigenvectors[k, q];
                    eigenvectors[k, p] = c * vkp - s * vkq;
                    eigenvectors[k, q] = s * vkp + c * vkq;
                }
            }

            eigenvalues = new double[n];
            for (int i = 0; i < n; i++) eigenvalues[i] = a[i, i];
        }

        private static int[] SortEigenvaluesDescending(double[] eigenvalues)
        {
            var order = new int[eigenvalues.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = 0; i < order.Length - 1; i++)
            {
                int best = i;
                for (int j = i + 1; j < order.Length; j++)
                    if (eigenvalues[order[j]] > eigenvalues[order[best]]) best = j;
                if (best != i)
                {
                    int tmp = order[i];
                    order[i] = order[best];
                    order[best] = tmp;
                }
            }
            return order;
        }

        private static double[] ReconstructFromSubspace(
            double[,] trajectory,
            double[] eigenvalues,
            double[,] eigenvectors,
            int[] order,
            int componentCount,
            double[] ratios,
            int outputLength)
        {
            int rows = trajectory.GetLength(0);
            int columns = trajectory.GetLength(1);
            var reconstructed = new double[rows, columns];

            for (int component = 0; component < componentCount; component++)
            {
                int index = order[component];
                if (eigenvalues[index] <= 1e-14) continue;
                double ratio = ratios == null ? 1.0 : ratios[component];
                if (ratio <= 0) continue;

                for (int column = 0; column < columns; column++)
                {
                    double projection = 0.0;
                    for (int row = 0; row < rows; row++)
                        projection += eigenvectors[row, index] * trajectory[row, column];
                    projection *= ratio;
                    for (int row = 0; row < rows; row++)
                        reconstructed[row, column] += eigenvectors[row, index] * projection;
                }
            }

            var result = new double[outputLength];
            var counts = new int[outputLength];
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int index = row + column;
                    result[index] += reconstructed[row, column];
                    counts[index]++;
                }
            for (int i = 0; i < result.Length; i++) result[i] /= Math.Max(1, counts[i]);
            return result;
        }

        private static void ApplyPrimal(double[] input, double[] dual, double[] result)
        {
            int n = input.Length;
            result[0] = input[0] + dual[0];
            for (int i = 1; i < n - 1; i++) result[i] = input[i] - dual[i - 1] + dual[i];
            result[n - 1] = input[n - 1] - dual[n - 2];
        }

        private static double[] CopyFinite(IReadOnlyList<double> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var result = new double[data.Count];
            for (int i = 0; i < result.Length; i++)
            {
                double value = data[i];
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("Input contains NaN or Infinity.", nameof(data));
                result[i] = value;
            }
            return result;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value) result <<= 1;
            return result;
        }

        private static int Mirror(int index, int length)
        {
            if (length <= 1) return 0;
            while (index < 0 || index >= length)
            {
                if (index < 0) index = -index - 1;
                if (index >= length) index = 2 * length - index - 1;
            }
            return index;
        }

        private static double Median(double[] values)
        {
            if (values.Length == 0) return 0.0;
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            int middle = copy.Length / 2;
            return copy.Length % 2 == 1 ? copy[middle] : 0.5 * (copy[middle - 1] + copy[middle]);
        }
    }
}
