using Filters;

var random = new Random(42);
double[] clean = Enumerable.Range(0, 240)
    .Select(i => 20 * Math.Sin(i * 0.06) + (i > 120 ? 15 : 0))
    .ToArray();
double[] noisy = clean.Select((x, i) => x + NextGaussian(random) * 4 + (i % 59 == 0 ? 28 : 0)).ToArray();

var candidates = new Dictionary<string, double[]>
{
    ["Moving average"] = BasicFilters.CenteredMovingAverage(noisy, 7),
    ["Median"] = BasicFilters.Median(noisy, 7),
    ["Gaussian"] = SignalFilters.Gaussian(noisy, 7, 1.5),
    ["Savitzky-Golay"] = SignalFilters.SavitzkyGolay(noisy, 7, 3),
    ["Hampel"] = RobustFilters.Hampel(noisy, 7, 3),
    ["One Euro"] = SignalFilters.OneEuro(noisy, 50, 1, 0.02),
    ["Kalman"] = StateEstimationFilters.ScalarKalman(noisy, 16, 0.2)
};

Console.WriteLine("Filter              RMSE    noise reduction");
Console.WriteLine(new string('-', 47));
foreach (var candidate in candidates.OrderBy(x => FilterMetrics.RootMeanSquareError(clean, x.Value)))
{
    double rmse = FilterMetrics.RootMeanSquareError(clean, candidate.Value);
    double reduction = FilterMetrics.NoiseReductionPercent(clean, noisy, candidate.Value);
    Console.WriteLine($"{candidate.Key,-18} {rmse,6:F2} {reduction,14:F1}%");
}

static double NextGaussian(Random random)
{
    double u1 = 1.0 - random.NextDouble();
    double u2 = 1.0 - random.NextDouble();
    return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
}
