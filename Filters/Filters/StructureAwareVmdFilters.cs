using System;
using System.Collections.Generic;
using System.Linq;

namespace Filters
{
    /// <summary>
    /// Structure-aware adaptive VMD pipelines. These methods use multiple diagnostics
    /// (reconstruction, permutation entropy, correlation and mode overlap) instead of
    /// relying on a single hand-tuned frequency or amplitude threshold.
    /// </summary>
    public static class StructureAwareVmdFilters
    {
        /// <summary>
        /// Searches K and alpha, decomposes the signal, then scores each IMF by
        /// |corr(IMF,input)| * (1 - normalized permutation entropy).
        /// Strong structured modes are retained, borderline modes are wavelet-refined,
        /// and weak high-entropy modes are rejected.
        /// </summary>
        public static AdaptiveVmdResult AdaptiveVmdEntropyCorrelationDenoise(
            IReadOnlyList<double> data,
            int maxModes = 6,
            IReadOnlyList<double> alphaCandidates = null,
            int searchIterations = 80,
            int finalIterations = 180,
            double retainRatio = 0.10,
            double refineRatio = 0.075,
            double minCorrelation = 0.08,
            double waveletThresholdMultiplier = 0.8)
        {
            double[] input = CopyFinite(data);
            if (input.Length < 8) throw new ArgumentException("Adaptive VMD requires at least 8 samples.", nameof(data));
            if (maxModes < 2) throw new ArgumentOutOfRangeException(nameof(maxModes));
            if (searchIterations < 1 || finalIterations < 1) throw new ArgumentOutOfRangeException("Iteration counts must be positive.");
            if (retainRatio <= 0 || retainRatio > 1) throw new ArgumentOutOfRangeException(nameof(retainRatio));
            if (refineRatio < 0 || refineRatio > retainRatio) throw new ArgumentOutOfRangeException(nameof(refineRatio));
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
                foreach (double alpha in alphas)
                {
                    VmdResult candidate = AdaptiveDecompositionFilters.VariationalModeDecomposition(
                        input, k, alpha, tau: 0.1, tolerance: 2e-4, maxIterations: searchIterations);
                    double reconstructionPenalty = candidate.ReconstructionRmse / signalRms;
                    double meanEntropy = candidate.Modes
                        .Select(m => AdaptiveDecompositionFilters.PermutationEntropy(m, order: 3, delay: 1))
                        .Average();
                    double overlap = MeanAbsoluteInterModeCorrelation(candidate.Modes);
                    double score = 2.5 * reconstructionPenalty + 0.35 * meanEntropy + 0.65 * overlap + 0.025 * k;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestAlpha = alpha;
                        bestModes = k;
                    }
                }
            }

            VmdResult decomposition = AdaptiveDecompositionFilters.VariationalModeDecomposition(
                input, bestModes, bestAlpha, tau: 0.1, tolerance: 5e-6, maxIterations: finalIterations);

            var entropies = new double[bestModes];
            var correlations = new double[bestModes];
            var structureScores = new double[bestModes];
            for (int k = 0; k < bestModes; k++)
            {
                entropies[k] = AdaptiveDecompositionFilters.PermutationEntropy(decomposition.Modes[k], order: 3, delay: 1);
                correlations[k] = Math.Abs(PearsonCorrelation(input, decomposition.Modes[k]));
                structureScores[k] = correlations[k] * Math.Max(0.0, 1.0 - entropies[k]);
            }

            double strongestScore = structureScores.Max();
            int strongestMode = Array.IndexOf(structureScores, strongestScore);
            var retained = new bool[bestModes];
            var refined = new bool[bestModes];
            var output = new double[input.Length];
            int kept = 0;

            for (int k = 0; k < bestModes; k++)
            {
                double relativeScore = strongestScore <= 1e-20 ? 0.0 : structureScores[k] / strongestScore;
                double[] contribution = null;
                if (relativeScore >= retainRatio)
                {
                    contribution = decomposition.Modes[k];
                    retained[k] = true;
                }
                else if (relativeScore >= refineRatio && correlations[k] >= minCorrelation)
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
                retained[strongestMode] = true;
                for (int i = 0; i < output.Length; i++) output[i] = decomposition.Modes[strongestMode][i];
            }

            return new AdaptiveVmdResult(
                output, decomposition, bestAlpha, bestScore, entropies, correlations, retained, refined);
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
            double meanA = 0.0;
            double meanB = 0.0;
            for (int i = 0; i < a.Count; i++)
            {
                meanA += a[i];
                meanB += b[i];
            }
            meanA /= a.Count;
            meanB /= b.Count;

            double numerator = 0.0;
            double energyA = 0.0;
            double energyB = 0.0;
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
    }
}
