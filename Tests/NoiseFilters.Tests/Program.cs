using Filters;

var tests = new (string Name, Action Run)[]
{
    ("moving average", () => Equal(BasicFilters.MovingAverage(new double[] { 1, 2, 3, 4 }, 2), 1, 1.5, 2.5, 3.5)),
    ("EMA identity at alpha=1", () => Equal(BasicFilters.ExponentialMovingAverage(new double[] { 2, -1, 7 }, 1), 2, -1, 7)),
    ("median removes spike", () => Near(BasicFilters.Median(new double[] { 1, 1, 99, 1, 1 }, 3)[2], 1)),
    ("Hampel removes spike", () => Near(RobustFilters.Hampel(new double[] { 1, 1, 99, 1, 1 }, 5)[2], 1)),
    ("Savitzky-Golay preserves quadratic", () =>
    {
        double[] x = Enumerable.Range(-10, 21).Select(i => (double)(i * i + 2 * i + 3)).ToArray();
        double[] y = SignalFilters.SavitzkyGolay(x, 7, 2);
        for (int i = 3; i < x.Length - 3; i++) Near(y[i], x[i], 1e-8);
    }),
    ("deadband suppresses chatter", () => Equal(SignalFilters.Deadband(new double[] { 10, 10.1, 10.2, 11 }, .5), 10, 10, 10, 11)),
    ("slew rate limits", () => Equal(SignalFilters.SlewRateLimiter(new double[] { 0, 10, -10 }, 2, 3), 0, 2, -1)),
    ("debounce waits", () => Equal(SignalFilters.Debounce(new double[] { 0, 1, 0, 1, 1, 1 }, 3), 0, 0, 0, 0, 0, 1)),
    ("Kalman output finite", () => Finite(StateEstimationFilters.ScalarKalman(new double[] { 0, 1, 2, 3 }))),
    ("wavelet preserves constant", () => Equal(ModernDenoisingFilters.WaveletHaarShrinkage(new double[] { 5, 5, 5, 5, 5, 5, 5, 5 }), 5, 5, 5, 5, 5, 5, 5, 5)),
    ("SSA rank-1 preserves constant", () =>
    {
        double[] y = ModernDenoisingFilters.SingularSpectrumAnalysis(Enumerable.Repeat(7.0, 24).ToArray(), window: 8, rank: 1);
        foreach (double value in y) Near(value, 7.0, 1e-7);
    }),
    ("reweighted SVD keeps length and finite", () =>
    {
        double[] input = Enumerable.Range(0, 40).Select(i => Math.Sin(i * 0.2) + (i % 7 == 0 ? 0.3 : 0)).ToArray();
        double[] y = ModernDenoisingFilters.ReweightedSvdDenoise(input, window: 10);
        if (y.Length != input.Length) throw new Exception("length changed");
        Finite(y);
    }),
    ("TV denoising lowers total variation", () =>
    {
        double[] input = { 0, 0.4, -0.3, 0.5, 5, 5.5, 4.6, 5.2 };
        double[] y = ModernDenoisingFilters.TotalVariationDenoise(input, lambda: 0.8, iterations: 200);
        if (TotalVariation(y) > TotalVariation(input) + 1e-9) throw new Exception("variation increased");
    }),
    ("robust adaptive Kalman rejects isolated spike", () =>
    {
        double[] y = ModernDenoisingFilters.RobustAdaptiveKalman(new double[] { 0, 0, 0, 100, 0, 0, 0 }, measurementNoise: 1, processNoise: 0.01);
        if (Math.Abs(y[3]) >= 30) throw new Exception($"spike leaked too strongly: {y[3]}");
        Finite(y);
    }),
    ("empty input supported", () => Equal(BasicFilters.MovingAverage(Array.Empty<double>(), 3))),
    ("invalid window rejected", () => Throws<ArgumentException>(() => BasicFilters.Median(new double[] { 1 }, 2))),
    ("metrics", () => Near(FilterMetrics.RootMeanSquareError(new double[] { 0, 0 }, new double[] { 3, 4 }), Math.Sqrt(12.5)))
};

int failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL  {test.Name}: {exception.Message}"); }
}
Console.WriteLine($"\n{tests.Length - failed}/{tests.Length} tests passed");
return failed == 0 ? 0 : 1;

static void Equal(IReadOnlyList<double> actual, params double[] expected)
{
    if (actual.Count != expected.Length) throw new Exception($"length {actual.Count} != {expected.Length}");
    for (int i = 0; i < expected.Length; i++) Near(actual[i], expected[i]);
}
static void Near(double actual, double expected, double tolerance = 1e-10)
{
    if (Math.Abs(actual - expected) > tolerance) throw new Exception($"{actual} != {expected}");
}
static void Finite(IEnumerable<double> values)
{
    if (values.Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new Exception("non-finite result");
}
static double TotalVariation(IReadOnlyList<double> values)
{
    double total = 0;
    for (int i = 1; i < values.Count; i++) total += Math.Abs(values[i] - values[i - 1]);
    return total;
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"{typeof(T).Name} was not thrown");
}
