using Filters;

const int n = 64;
double[] input = Enumerable.Range(0, n)
    .Select(i => Math.Sin(2 * Math.PI * i / 18.0)
               + 0.35 * Math.Sin(2 * Math.PI * i / 7.0)
               + 0.65 * Math.Sin(2 * Math.PI * i / 3.0))
    .ToArray();

AdaptiveVmdResult result = StructureAwareVmdFilters.AdaptiveVmdEntropyCorrelationDenoise(
    input,
    maxModes: 3,
    alphaCandidates: new double[] { 250, 1000 },
    searchIterations: 20,
    finalIterations: 40);

if (result.Signal.Length != input.Length) throw new Exception("output length changed");
if (result.ModeCount < 2 || result.ModeCount > 3) throw new Exception("invalid mode count");
if (result.PermutationEntropies.Length != result.ModeCount) throw new Exception("entropy diagnostics mismatch");
if (result.Correlations.Length != result.ModeCount) throw new Exception("correlation diagnostics mismatch");
if (!result.Retained.Any(x => x)) throw new Exception("no mode retained");
if (result.Signal.Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new Exception("non-finite output");
if (result.Decomposition.CenterFrequencies.Any(f => f < 0 || f > 0.5)) throw new Exception("invalid center frequency");

Console.WriteLine($"Adaptive VMD smoke test passed: K={result.ModeCount}, alpha={result.Alpha}, score={result.Score:F4}");
for (int k = 0; k < result.ModeCount; k++)
    Console.WriteLine($"  mode {k}: f={result.Decomposition.CenterFrequencies[k]:F4}, PE={result.PermutationEntropies[k]:F4}, corr={result.Correlations[k]:F4}, keep={result.Retained[k]}, wavelet={result.WaveletRefined[k]}");
