# Noise-filters

## Visual handbook of signal filtering algorithms

A C# library containing practical algorithms for cleaning noisy measurements, sensor data and industrial signals.

The goal of this repository is not only implementation, but also **understanding how filters change a real signal**.

---

# Signal filtering concept

Raw measurements usually contain:

- useful signal
- random noise
- spikes / outliers
- measurement jitter
- drift

Example:

```mermaid
xychart-beta
    title "Raw sensor signal vs filtered signal"
    x-axis [1,2,3,4,5,6,7,8,9,10]
    y-axis "Value" 0 --> 100
    line "Raw" [20,70,25,80,30,75,35,90,40,85]
    line "Filtered" [20,35,38,48,52,60,65,72,75,80]
```

---

# Implemented algorithms

| Algorithm | Noise type | Delay | CPU cost | Typical usage |
|-|-|-|-|-|
| Average | Random noise | Medium | Very low | Basic sensors |
| Moving Average | Random noise | Medium | Low | Telemetry |
| Exponential Average | Random noise | Low | Very low | Embedded |
| Median | Spikes | Medium | Low | Industrial sensors |
| Kalman | Dynamic systems | Low | Medium | Tracking |
| Alpha-Beta | Motion estimation | Low | Low | Robotics |
| Least Squares | Trend noise | High | Medium | Calibration |
| Gaussian | High frequency noise | Medium | Medium | Signal processing |
| Hampel | Outliers | Low | Medium | Fault detection |
| Savitzky-Golay | Shape distortion | Low | Medium | Scientific data |
| Deadband | Small oscillations | Zero | Very low | Control systems |

---

# Filter comparison

## Moving Average

Removes random fluctuations by averaging recent samples.

```mermaid
xychart-beta
    title "Moving Average"
    x-axis [1,2,3,4,5,6,7,8]
    y-axis 0 --> 10
    line "Input" [2,8,3,9,4,8,5,9]
    line "Output" [2,5,5,6,6,7,7,8]
```

Advantages:

+ extremely simple
+ works on microcontrollers
+ predictable

Disadvantages:

- introduces delay
- destroys sharp changes

---

# Median filter

Best against impulse noise.

```mermaid
xychart-beta
    title "Median removes spikes"
    x-axis [1,2,3,4,5,6,7]
    y-axis 0 --> 20
    line "Before" [5,6,20,7,6,5,6]
    line "After" [5,6,7,7,6,6,6]
```

---

# Kalman family

Used when the signal has a model and prediction is possible.

```mermaid
flowchart LR
A[Measurement] --> B[Prediction]
B --> C[Kalman Gain]
C --> D[Correction]
D --> E[Filtered value]
```

---

# Advanced filters

## Weighted Moving Average

Gives different importance to samples.

Useful when newer measurements are more valuable.

## Gaussian Filter

Smooths high frequency noise using Gaussian weights.

## Hampel Filter

Robust removal of abnormal measurements.

Example:

```mermaid
xychart-beta
    title "Hampel removes outlier"
    x-axis [1,2,3,4,5,6]
    y-axis 0 --> 50
    line "Input" [10,12,11,45,13,12]
    line "Filtered" [10,12,11,12,13,12]
```

## Savitzky-Golay

Preserves signal shape better than ordinary averaging.

Used in:

- spectroscopy
- scientific measurements
- industrial analysis

## Deadband filter

Ignores insignificant changes.

Example:

```
Input:
10.00
10.01
10.02
10.01

Output with deadband 0.05:
10.00
10.00
10.00
10.00
```

---

# Interactive playground (planned)

Future version will include a browser playground:

- generate noisy signal
- select filter
- change parameters
- compare response
- measure delay
- measure noise reduction

---

# Library structure

```
Filters/
 ├── BasicFilters.cs
 ├── AdvancedFilters.cs
 ├── StatisticalFilters.cs
 ├── SignalFilters.cs
 └── Examples/
```

---

# Applications

- Industrial automation
- PLC systems
- Robotics
- IoT devices
- Embedded controllers
- Measurement equipment
- Computer vision preprocessing

---

# Philosophy

A filter is always a compromise between:

```
Noise reduction <-----> Signal preservation <-----> Response speed
```

The correct algorithm depends on the physical process, sensor and required response time.
