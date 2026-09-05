using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Filters
{
    /// <summary>Diagnostics returned by the dependency-free VMD reference implementation.</summary>
    public sealed class VmdResult
    {
        internal VmdResult(double[][] modes, double[] centerFrequencies, int iterations, double relativeChange, double reconstructionRmse)
        {
            Modes = modes;
            CenterFrequencies = centerFrequencies;
            Iterations = iterations;
            RelativeChange = relativeChange;
            ReconstructionRmse = reconstructionRmse;
        }

        public double[][] Modes { get; }
        public double[] CenterFrequencies { get; }
        public int Iterations { get; }
        public double RelativeChange { get; }
        public double ReconstructionRmse { get; }

        public double[] Reconstruct()
        {
            if (Modes.Length == 0) return Array.Empty<double>();
            var result = new double[Modes[0].Length];
            for (int k = 0; k < Modes.Length; k++)
                for (int i = 0; i < result.Length; i++)
                    result[i] += Modes[k][i];
            return result;
        }
    }

    /// <summary>Diagnostics for automatic VMD parameter selection and mode-wise refinement.</summary>
    public sealed class AdaptiveVmdResult
    {
        internal AdaptiveVmdResult(
            double[] signal,
            VmdResult decomposition,
            double alpha,
            double score,
            double[] permutationEntropies,
            double[] correlations,
            bool[] retained,
            bool[] waveletRefined)
        {
            Signal = signal;
            Decomposition = decomposition;
            Alpha = alpha;
            Score = score;
            PermutationEntropies = permutationEntropies;
            Correlations = correlations;
            Retained = retained;
            WaveletRefined = waveletRefined;
        }

        public double[] Signal { get; }
        public VmdResult Decomposition { get; }
        public int ModeCount => Decomposition.Modes.Length;
        public double Alpha { get; }
        public double Score { get; }
        public double[] PermutationEntropies { get; }
        public double[] Correlations { get; }
        public bool[] Retained { get; }
        public bool[] WaveletRefined { get; }
    }

    /// <summary>
    /// Adaptive decomposition filters for non-stationary one-dimensional signals.
    /// The VMD implementation deliberately uses a dependency-free DFT so the mathematics
    /// stays inspectable. It is a research/reference implementation, not a high-throughput FFT engine.
    /// </summary>
    public static class AdaptiveDecompositionFilters
    {
        /// <summary>
        /// Variational Mode Decomposition in the analytic frequency domain.
        /// Center frequencies are reported in cycles/sample in [0, 0.5].
        /// </summary>
        public static VmdResult VariationalModeDecomposition(
            IReadOnlyList<double> data,
            int modes = 4,
            double alpha = 2000.0,
            double tau = 0.0,
            double tolerance = 1e-6,
            int maxIterations = 250,
            bool dcMode = false)
        {
            double[] input = CopyFinite(data);
            if (modes < 1) throw new ArgumentOutOfRangeException(nameof(modes));
            if (alpha <= 0) throw new ArgumentOutOfRangeException(nameof(alpha));
            if (tau < 0) throw new ArgumentOutOfRangeException(nameof(tau));
            if (tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            if (maxIterations < 1) throw new ArgumentOutOfRangeException(nameof(maxIterations));
            if (input.Length == 0) return new VmdResult(Array.Empty<double[]>(), Array.Empty<double>(), 0, 0, 0);
            if (input.Length == 1)
                return new VmdResult(new[] { new[] { input[0] } }, new[] { 0.0 }, 0, 0, 0);
            if (modes > input.Length / 2) throw new ArgumentOutOfRangeException(nameof(modes), "Mode count is too large for the signal length.");

            int n = input.Length;
            int positiveBins = n / 2 + 1;
            Complex[] spectrum = AnalyticSpectrum(input, positiveBins);
            var modeSpectra = new Complex[modes][];
            var previous = new Complex[modes][];
            for (int k = 0; k < modes; k++)
            {
                modeSpectra[k] = new Complex[positiveBins];
                previous[k] = new Complex[positiveBins];
            }

            var lambda = new Complex[positiveBins];
            var center = new double[modes];
            for (int k = 0; k < modes; k++)
                center[k] = dcMode && k == 0 ? 0.0 : 0.5 * (k + (dcMode ? 0 : 0.5)) / modes;

            double inputSpectralEnergy = 0.0;
            for (int f = 0; f < positiveBins; f++) inputSpectralEnergy += spectrum[f].Magnitude * spectrum[f].Magnitude;
            inputSpectralEnergy = Math.Max(1e-20, inputSpectralEnergy);

            double relativeChange = double.PositiveInfinity;
            int iterations = 0;
            var sumModes = new Complex[positiveBins];

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                iterations = iteration + 1;
                for (int k = 0; k < modes; k++) Array.Copy(modeSpectra[k], previous[k], positiveBins);
                Array.Clear(sumModes, 0, sumModes.Length);
                for (int k = 0; k < modes; k++)
                    for (int f = 0; f < positiveBins; f++)
                        sumModes[f] += modeSpectra[k][f];

                for (int k = 0; k < modes; k++)
                {
                    double weightedFrequency = 0.0;
                    double modeEnergy = 0.0;
                    for (int f = 0; f < positiveBins; f++)
                    {
                        double frequency = (double)f / n;
                        Complex old = modeSpectra[k][f];
                        Complex residual = spectrum[f] - (sumModes[f] - old) - 0.5 * lambda[f];
                        double distance = frequency - center[k];
                        double denominator = 1.0 + alpha * distance * distance;
                        Complex updated = residual / denominator;
                        modeSpectra[k][f] = updated;
                        sumModes[f] += updated - old;

                        if (!(dcMode && k == 0) && f > 0)
                        {
                            double e = updated.Magnitude * updated.Magnitude;
                            weightedFrequency += frequency * e;
                            modeEnergy += e;
                        }
                    }
                    if (!(dcMode && k == 0) && modeEnergy > 1e-20)
                        center[k] = weightedFrequency / modeEnergy;
                }

                if (tau > 0)
                    for (int f = 0; f < positiveBins; f++)
                        lambda[f] += tau * (sumModes[f] - spectrum[f]);

                double changeEnergy = 0.0;
                for (int k = 0; k < modes; k++)
                    for (int f = 0; f < positiveBins; f++)
                    {
                        Complex delta = modeSpectra[k][f] - previous[k][f];
                        changeEnergy += delta.Magnitude * delta.Magnitude;
                    }
                relativeChange = Math.Sqrt(changeEnergy / inputSpectralEnergy);
                if (relativeChange <= tolerance) break;
            }

            var reconstructedModes = new double[modes][];
            for (int k = 0; k < modes; k++) reconstructedModes[k] = InverseAnalyticReal(modeSpectra[k], n);

            var reconstruction = new double[n];
            for (int k = 0; k < modes; k++)
                for (int i = 0; i < n; i++) reconstruction[i] += reconstructedModes[k][i];

            return new VmdResult(
                reconstructedModes,
                center,
                iterations,
                relativeChange,
                RootMeanSquareError(input, reconstruction));
        }

        /// <summary>
        /// Deterministic parameter-adaptive VMD followed by mode-wise wavelet refinement.
        /// K and alpha are selected from a compact search grid using reconstruction error,
        /// normalized permutation entropy, inter-mode correlation and a parsimony penalty.
        /// High-entropy modes are wavelet-shrunk when still correlated with the input;
        /// weak high-entropy modes are rejected.
        /// </summary>
        public static AdaptiveVmdResult AdaptiveVmdWaveletDenoise(
            IReadOnlyList<double> data,
            int maxModes = 6,
            IReadOnlyList<double> alphaCandidates = null,
            int searchIterations = 80,
            int finalIterations = 180,
            double minCorrelation = 0.08,
            double waveletThresholdMultiplier = 0.8)
        {
            double[] input = CopyFinite(data);
            if (input.Length < 8) throw new ArgumentException("Adaptive VMD requires at least 8 samples.", nameof(data));
            if (maxModes < 2) throw new ArgumentOutOfRangeException(nameof(maxModes));
            if (searchIterations < 1 || finalIterations < 1) throw new ArgumentOutOfRangeException("Iteration counts must be positive.");
            if (minCorrelation < 0 || minCorrelation > 1) throw new ArgumentOutOfRangeException(nameof(minCorrelation));
            if (waveletThresholdMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(waveletThresholdMultiplier));

            double[] alphas = alphaCandidates == null
                ? new[] { 250.0, 500.0, 1000.0, 2000.0, 4000.0 }
                : alphaCandidates.ToArray();
            if (alphas.Length == 0 || alphas.Any(a => a <= 0 || double.IsNaN(a) || double.IsInfinity(a)))
                throw new ArgumentException("Alpha candidates must contain finite positive values.", nameof(alphaCandidates));

            int upperModes = Math.Min(maxModes, Math.Max(2, input.Length / 8));
            double signalRms = Math.Max(1e-12, RootMeanSquare(input));
            double bestScore = double.PositiveInfinity;
            double bestAlpha = alphas[0];
            int bestModes = 2;

            for (int k = 2; k <= upperModes; k++)
            {
                foreach (double candidateAlpha in alphas)
                {
                    VmdResult candidate = VariationalModeDecomposition(
                        input, k, candidateAlpha, tau: 0.1, tolerance: 2e-4, maxIterations: searchIterations);
                    double reconstructionPenalty = candidate.ReconstructionRmse / signalRms;
                    double entropy = 0.0;
                    for (int mode = 0; mode < candidate.Modes.Length; mode++)
                        entropy += PermutationEntropy(candidate.Modes[mode], order: 3, delay: 1);
                    entropy /= candidate.Modes.Length;

                    double overlap = MeanAbsoluteInterModeCorrelation(candidate.Modes);
                    double score = 2.5 * reconstructionPenalty + 0.35 * entropy + 0.65 * overlap + 0.025 * k;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestAlpha = candidateAlpha;
                        bestModes = k;
                    }
                }
            }

            VmdResult decomposition = VariationalModeDecomposition(
                input, bestModes, bestAlpha, tau: 0.1, tolerance: 5e-6, maxIterations: finalIterations);

            var entropies = new double[bestModes];
            var correlations = new double[bestModes];
            for (int k = 0; k < bestModes; k++)
            {
                entropies[k] = PermutationEntropy(decomposition.Modes[k], order: 3, delay: 1);
                correlations[k] = Math.Abs(PearsonCorrelation(input, decomposition.Modes[k]));
            }

            double entropyMedian = Median(entropies);
            var entropyDeviation = entropies.Select(x => Math.Abs(x - entropyMedian)).ToArray();
            double entropyMad = Median(entropyDeviation);
            double entropyBoundary = Math.Min(0.98, entropyMedian + 0.75 * 1.4826 * entropyMad);

            var output = new double[input.Length];
            var retained = new bool[bestModes];
            var refined = new bool[bestModes];
            int kept = 0;
            int strongest = 0;
            for (int k = 1; k < bestModes; k++)
                if (correlations[k] > correlations[strongest]) strongest = k;

            for (int k = 0; k < bestModes; k++)
            {
                double[] contribution = null;
                if (entropies[k] <= entropyBoundary && correlations[k] >= minCorrelation * 0.5)
                {
                    contribution = decomposition.Modes[k];
                    retained[k] = true;
                }
                else if (correlations[k] >= minCorrelation)
                {
                    contribution = ModernDenoisingFilters.WaveletHaarShrinkage(
                        decomposition.Modes[k], thresholdMultiplier: waveletThresholdMultiplier);
                    retained[k] = true;
                    refined[k] = true;
                }

                if (contribution != null)
                {
                    kept++;
                    for (int i = 0; i < output.Length; i++) output[i] += contribution[i];
                }
            }

            if (kept == 0)
            {
                retained[strongest] = true;
                for (int i = 0; i < output.Length; i++) output[i] = decomposition.Modes[strongest][i];
            }

            return new AdaptiveVmdResult(
                output, decomposition, bestAlpha, bestScore, entropies, correlations, retained, refined);
        }

        /// <summary>
        /// Normalized LMS adaptive noise canceller. The reference channel must contain
        /// noise correlated with the contamination in the primary channel but ideally not
        /// with the desired signal. The returned signal is the NLMS prediction error.
        /// </summary>
        public static double[] NormalizedLmsNoiseCancel(
            IReadOnlyList<double> primary,
            IReadOnlyList<double> referenceNoise,
            int taps = 16,
            double stepSize = 0.5,
            double regularization = 1e-8)
        {
            double[] d = CopyFinite(primary);
            double[] x = CopyFinite(referenceNoise);
            if (d.Length != x.Length) throw new ArgumentException("Primary and reference channels must have equal length.");
            if (taps < 1) throw new ArgumentOutOfRangeException(nameof(taps));
            if (stepSize <= 0 || stepSize >= 2) throw new ArgumentOutOfRangeException(nameof(stepSize), "NLMS step size must be in (0, 2).");
            if (regularization <= 0) throw new ArgumentOutOfRangeException(nameof(regularization));

            var weights = new double[taps];
            var result = new double[d.Length];
            for (int i = 0; i < d.Length; i++)
            {
                double prediction = 0.0;
                double norm = regularization;
                for (int tap = 0; tap < taps; tap++)
                {
                    int index = i - tap;
                    double sample = index >= 0 ? x[index] : 0.0;
                    prediction += weights[tap] * sample;
                    norm += sample * sample;
                }

                double error = d[i] - prediction;
                result[i] = error;
                double scale = stepSize * error / norm;
                for (int tap = 0; tap < taps; tap++)
                {
                    int index = i - tap;
                    if (index >= 0) weights[tap] += scale * x[index];
                }
            }
            return result;
        }

        /// <summary>Normalized ordinal-pattern permutation entropy in [0,1].</summary>
        public static double PermutationEntropy(IReadOnlyList<double> data, int order = 3, int delay = 1)
        {
            double[] input = CopyFinite(data);
            if (order < 2 || order > 7) throw new ArgumentOutOfRangeException(nameof(order));
            if (delay < 1) throw new ArgumentOutOfRangeException(nameof(delay));
            int vectors = input.Length - (order - 1) * delay;
            if (vectors <= 0) return 0.0;

            var counts = new Dictionary<int, int>();
            var indices = new int[order];
            for (int start = 0; start < vectors; start++)
            {
                for (int j = 0; j < order; j++) indices[j] = j;
                for (int a = 0; a < order - 1; a++)
                    for (int b = a + 1; b < order; b++)
                    {
                        double va = input[start + indices[a] * delay];
                        double vb = input[start + indices[b] * delay];
                        if (vb < va || (vb == va && indices[b] < indices[a]))
                        {
                            int tmp = indices[a];
                            indices[a] = indices[b];
                            indices[b] = tmp;
                        }
                    }

                int code = 0;
                for (int j = 0; j < order; j++) code = code * order + indices[j];
                counts.TryGetValue(code, out int count);
                counts[code] = count + 1;
            }

            double entropy = 0.0;
            foreach (int count in counts.Values)
            {
                double p = (double)count / vectors;
                entropy -= p * Math.Log(p);
            }
            return entropy / Math.Log(Factorial(order));
        }

        private static Complex[] AnalyticSpectrum(double[] input, int positiveBins)
        {
            int n = input.Length;
            var result = new Complex[positiveBins];
            for (int k = 0; k < positiveBins; k++)
            {
                Complex sum = Complex.Zero;
                for (int t = 0; t < n; t++)
                {
                    double angle = -2.0 * Math.PI * k * t / n;
                    sum += input[t] * new Complex(Math.Cos(angle), Math.Sin(angle));
                }
                bool isDc = k == 0;
                bool isNyquist = n % 2 == 0 && k == n / 2;
                result[k] = isDc || isNyquist ? sum : 2.0 * sum;
            }
            return result;
        }

        private static double[] InverseAnalyticReal(Complex[] positiveSpectrum, int n)
        {
            var result = new double[n];
            for (int t = 0; t < n; t++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < positiveSpectrum.Length; k++)
                {
                    double angle = 2.0 * Math.PI * k * t / n;
                    sum += positiveSpectrum[k] * new Complex(Math.Cos(angle), Math.Sin(angle));
                }
                result[t] = sum.Real / n;
            }
            return result;
        }

        private static double MeanAbsoluteInterModeCorrelation(double[][] modes)
        {
            if (modes.Length < 2) return 0.0;
            double total = 0.0;
            int pairs = 0;
            for (int i = 0; i < modes.Length; i++)
                for (int j = i + 1; j < modes.Length; j++)
                {
                    total += Math.Abs(PearsonCorrelation(modes[i], modes[j]));
                    pairs++;
                }
            return pairs == 0 ? 0.0 : total / pairs;
        }

        private static double PearsonCorrelation(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            if (a.Count != b.Count) throw new ArgumentException("Signals must have equal length.");
            if (a.Count == 0) return 0.0;
            double meanA = 0.0, meanB = 0.0;
            for (int i = 0; i < a.Count; i++) { meanA += a[i]; meanB += b[i]; }
            meanA /= a.Count;
            meanB /= b.Count;
            double numerator = 0.0, energyA = 0.0, energyB = 0.0;
            for (int i = 0; i < a.Count; i++)
            {
                double da = a[i] - meanA;
                double db = b[i] - meanB;
                numerator += da * db;
                energyA += da * da;
                energyB += db * db;
            }
            double denominator = Math.Sqrt(energyA * energyB);
            return denominator <= 1e-20 ? 0.0 : numerator / denominator;
        }

        private static double RootMeanSquare(IReadOnlyList<double> data)
        {
            if (data.Count == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < data.Count; i++) sum += data[i] * data[i];
            return Math.Sqrt(sum / data.Count);
        }

        private static double RootMeanSquareError(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            if (a.Count != b.Count) throw new ArgumentException("Signals must have equal length.");
            if (a.Count == 0) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < a.Count; i++)
            {
                double d = a[i] - b[i];
                sum += d * d;
            }
            return Math.Sqrt(sum / a.Count);
        }

        private static double[] CopyFinite(IReadOnlyList<double> data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var result = new double[data.Count];
            for (int i = 0; i < result.Length; i++)
            {
                double value = data[i];
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("Signal must contain only finite values.", nameof(data));
                result[i] = value;
            }
            return result;
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0) return 0.0;
            double[] copy = values.ToArray();
            Array.Sort(copy);
            int middle = copy.Length / 2;
            return copy.Length % 2 == 0 ? 0.5 * (copy[middle - 1] + copy[middle]) : copy[middle];
        }

        private static int Factorial(int n)
        {
            int result = 1;
            for (int i = 2; i <= n; i++) result *= i;
            return result;
        }
    }
}
