# Noise-filters
Algorithms for filtering input data from noise and outliers.

---
Realizable algorithms.

- [x] Average
- [x] Stretched selection
- [x] Running average
- [x] Exponential running average
- [x] Adaptive factor
- [x] Median filter
- [x] Least square method
- [x] Simple Kalman
- [x] Alpha Beta Filter
- [x] Values / Noise calculation

## Advanced filters

Added in `AdvancedFilters.cs`:

- [x] Weighted Moving Average
- [x] Gaussian Filter
- [x] Hampel Filter
- [x] Savitzky-Golay smoothing
- [x] Deadband filter
- [x] Median Absolute Deviation analysis

---

# AdvancedFilters

Additional algorithms for industrial sensors, telemetry, robotics and measurement systems.

## Weighted Moving Average

Weighted averaging where every sample has an individual coefficient. Allows giving higher priority to newer or more reliable measurements.

## Gaussian Filter

A smoothing filter based on Gaussian distribution. It removes high-frequency noise while preserving the general signal shape.

## Hampel Filter

A robust statistical outlier detector. Impulse noise and abnormal measurements are replaced with a local median value.

## Savitzky-Golay Filter

A polynomial smoothing algorithm used in scientific and engineering measurements where preservation of signal form is important.

## Deadband Filter

Suppresses small changes below a configurable threshold. Useful for eliminating sensor jitter in control systems.

## Median Absolute Deviation

A statistical noise estimation method based on deviation from the median. Can be used for adaptive anomaly detection.

## Example

```csharp
var smooth = AdvancedFilters.GaussianFilter(signal);
var clean = AdvancedFilters.HampelFilter(signal);
var stable = AdvancedFilters.Deadband(signal, 0.05);
```

## Applications

- Industrial automation
- Embedded systems
- IoT sensors
- Robotics
- Data acquisition
- Control systems
