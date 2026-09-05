# Adaptive decomposition research — 2026

This branch continues the repository from fixed/windowed filters toward **data-adaptive decomposition of non-stationary signals**.

The implementation is deliberately dependency-free on the C# side. It is intended as an inspectable research/reference baseline, not as a claim that a naive DFT implementation should replace an FFT-optimized production VMD library.

## Why this direction

Recent 2025–2026 denoising work repeatedly converges on the same architecture:

1. decompose a non-stationary signal into modes;
2. select decomposition parameters automatically rather than by hand;
3. classify modes using entropy/correlation/energy-type diagnostics;
4. keep informative modes, refine ambiguous modes, reject noise-dominated modes;
5. reconstruct the signal.

Examples:

- Li, Wu, Pei — **Parameter-Adaptive VMD and Wavelet Thresholding**, Sensors 2026, 26(13), 3974. DOI: `10.3390/s26133974`.
- Ibrahim et al. — **Adaptive successive VMD for denoising ECG and arterial pulse waves**, Signal Processing 240 (2026), 110368. DOI: `10.1016/j.sigpro.2025.110368`.
- **ICFO–SVMD and Improved Wavelet Thresholding**, Sensors 2026, 26(2), 750. DOI: `10.3390/s26020750`.
- **Adaptive VMD + wavelet threshold for gear health monitoring**, Structural Durability & Health Monitoring 19(4), 2025. DOI: `10.32604/sdhm.2025.061805`.
- Yang et al. — **Adaptive VMD + multiscale PCA for borehole radar**, Remote Sensing 2025, 17(3), 525. DOI: `10.3390/rs17030525`.
- Zhang et al. — **VMD + NLMS joint optimization for weak signals**, Electronics 2025, 14(24), 4914. DOI: `10.3390/electronics14244914`.

The branch does **not** reproduce these papers verbatim. It extracts their useful architectural ideas into small, readable C# baselines.

## Added APIs

### `AdaptiveDecompositionFilters.VariationalModeDecomposition`

A dependency-free VMD reference implementation in the analytic frequency domain.

Returns `VmdResult` with:

- reconstructed modes;
- estimated center frequencies in cycles/sample;
- iteration count;
- final relative change;
- reconstruction RMSE.

The implementation uses a direct DFT/IDFT to keep the algorithm inspectable and NuGet-free. Complexity is therefore roughly quadratic in signal length per iteration. For production/high-rate use, replace the DFT backend with FFT while keeping the same public algorithmic structure.

### `AdaptiveDecompositionFilters.AdaptiveVmdWaveletDenoise`

Grid-searches `K` and `alpha` using a deterministic score built from:

- normalized reconstruction error;
- mean permutation entropy;
- inter-mode correlation/overlap;
- a small model-complexity penalty.

Then it applies wavelet refinement to selected high-entropy modes.

### `StructureAwareVmdFilters.AdaptiveVmdEntropyCorrelationDenoise`

The preferred research pipeline in this branch.

For each mode:

```text
structure_score = |corr(IMF, input)| * (1 - normalized_permutation_entropy)
```

Modes with strong relative structure scores are kept. Borderline modes can be wavelet-refined. Weak high-entropy modes are removed.

This is intentionally multi-criteria: correlation alone can preserve strong interference, while entropy alone can reject legitimate high-frequency structure.

### `AdaptiveDecompositionFilters.NormalizedLmsNoiseCancel`

A classical NLMS adaptive noise canceller for the two-channel case where a reference signal is available that is correlated with the contamination but ideally not with the desired signal.

It complements the decomposition methods: VMD is useful when the unwanted content can be separated by modal structure; NLMS is preferable when a physical/reference noise channel exists.

### `AdaptiveDecompositionFilters.PermutationEntropy`

Normalized ordinal-pattern permutation entropy in `[0,1]`, used as a lightweight complexity diagnostic for mode selection.

## Benchmark philosophy

A single synthetic benchmark is easy to overfit. This branch therefore uses **two deterministic regimes** with seed `20260905`.

### Regime A — structured interference

Useful signal:

- low-frequency sinusoid;
- chirp;
- step;
- short transients.

Noise:

- moderate white noise;
- strong 31 Hz narrow-band interference;
- additional 42 Hz component;
- a few impulses.

Result:

| Method | RMSE | SNR, dB | Correlation |
|---|---:|---:|---:|
| Raw | 0.7629 | 2.95 | 0.8104 |
| Moving average | 0.4105 | 8.33 | 0.9207 |
| Median | 0.4813 | 6.95 | 0.8907 |
| Savitzky–Golay | 0.2639 | 12.17 | 0.9679 |
| Reweighted SVD | 0.7065 | 3.62 | 0.8236 |
| **Adaptive VMD structure** | **0.2242** | **13.59** | **0.9778** |

The adaptive search selected `K=4`, `alpha=250`.

### Regime B — broadband + impulsive noise

Noise is dominated by stronger Gaussian contamination and more frequent impulses.

| Method | RMSE | SNR, dB | Correlation |
|---|---:|---:|---:|
| Raw | 1.1901 | 0.41 | 0.7178 |
| **Moving average** | **0.4456** | **8.94** | **0.9319** |
| Median | 0.4850 | 8.20 | 0.9214 |
| Savitzky–Golay | 0.5205 | 7.59 | 0.9176 |
| Reweighted SVD | 0.6374 | 5.83 | 0.8724 |
| Adaptive VMD structure | 0.6087 | 6.23 | 0.8949 |

This is deliberately left in the documentation because it shows the limitation clearly: **VMD is not a universal replacement for local robust smoothing**.

![Two-regime benchmark](charts/adaptive_vmd_benchmark.svg)

Raw data: [`charts/adaptive_vmd_benchmark.csv`](charts/adaptive_vmd_benchmark.csv).

Regenerate:

```bash
python tools/generate_adaptive_vmd_benchmark.py
```

## Practical selection guide

| Signal/problem | Prefer |
|---|---|
| Narrow-band interference mixed with non-stationary useful content | Adaptive VMD structure |
| A few isolated spikes | Hampel / Median |
| Broadband sensor noise and latency matters | EMA / Gaussian / tuned moving average |
| Smooth waveform, peak geometry matters | Savitzky–Golay |
| Quasi-periodic low-rank batch signal | SSA / reweighted SVD |
| Time-varying measurement covariance, online | Robust adaptive Kalman |
| A separate physical noise reference is available | NLMS |
| Sharp piecewise-constant transitions | Total Variation |

## Limitations and next research steps

Current VMD is intentionally readable rather than fast. The next meaningful upgrades are:

1. FFT backend with the same API;
2. mirror extension to reduce boundary artifacts;
3. multiscale permutation entropy instead of one scale/order;
4. automatic `tau` and stopping-criterion selection;
5. mode-merging / mode-mixing diagnostics;
6. Successive VMD (SVMD) baseline;
7. adaptive VMD + NLMS cascade for a real reference-noise channel;
8. multichannel extensions (PCA/MSSA) without forcing them into the single-channel API;
9. C# BenchmarkDotNet measurements after the mathematical reference stabilizes;
10. real datasets: bearing/gear vibration, IMU, ECG/PPG, acoustic and industrial sensor traces.

## Reproducibility statement

The benchmark values in this branch are scenario-specific. They must not be compared numerically with tables from other branches as if all experiments used the same signal/noise model. The goal of the repository is to expose the trade-offs and implementation details, not to manufacture a single global leaderboard.
