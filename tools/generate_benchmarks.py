#!/usr/bin/env python3
"""Generate deterministic filter simulations, metrics and README charts.

The reference implementations below mirror the equations used by the C#
library.  The script intentionally fixes the random seed so every chart and
CSV can be regenerated and reviewed.
"""

from __future__ import annotations

import csv
import math
import time
from pathlib import Path
from typing import Callable, Dict

import matplotlib.pyplot as plt
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "assets" / "benchmarks"
SEED = 20260824

COLORS = {
    "clean": "#10b981",
    "noisy": "#94a3b8",
    "Moving average": "#2563eb",
    "Exponential": "#7c3aed",
    "Weighted average": "#0ea5e9",
    "Gaussian": "#06b6d4",
    "Median": "#f59e0b",
    "Hampel": "#ef4444",
    "Savitzky-Golay": "#ec4899",
    "Simple Kalman": "#14b8a6",
    "Alpha-Beta": "#8b5cf6",
    "Deadband": "#64748b",
}


def moving_average(data: np.ndarray, window: int = 9) -> np.ndarray:
    result = np.empty_like(data, dtype=float)
    total = 0.0
    for i, value in enumerate(data):
        total += value
        if i >= window:
            total -= data[i - window]
        result[i] = total / min(i + 1, window)
    return result


def exponential(data: np.ndarray, alpha: float = 0.12) -> np.ndarray:
    result = np.empty_like(data, dtype=float)
    result[0] = data[0]
    for i in range(1, len(data)):
        result[i] = result[i - 1] + alpha * (data[i] - result[i - 1])
    return result


def weighted_average(data: np.ndarray, weights: np.ndarray | None = None) -> np.ndarray:
    weights = np.asarray(weights if weights is not None else [1, 2, 3, 4, 5, 4, 3, 2, 1], dtype=float)
    radius = len(weights) // 2
    result = np.empty_like(data, dtype=float)
    for i in range(len(data)):
        lo, hi = max(0, i - radius), min(len(data), i + radius + 1)
        w_lo = radius - (i - lo)
        selected = weights[w_lo : w_lo + hi - lo]
        result[i] = np.dot(data[lo:hi], selected) / selected.sum()
    return result


def gaussian(data: np.ndarray, size: int = 9, sigma: float = 2.0) -> np.ndarray:
    x = np.arange(size) - size // 2
    kernel = np.exp(-(x * x) / (2.0 * sigma * sigma))
    return weighted_average(data, kernel)


def centered_median(data: np.ndarray, window: int = 9) -> np.ndarray:
    radius = window // 2
    result = np.empty_like(data, dtype=float)
    for i in range(len(data)):
        result[i] = np.median(data[max(0, i - radius) : min(len(data), i + radius + 1)])
    return result


def hampel(data: np.ndarray, radius: int = 4, threshold: float = 3.0) -> np.ndarray:
    result = data.astype(float).copy()
    scale = 1.4826
    for i in range(radius, len(data) - radius):
        segment = data[i - radius : i + radius + 1]
        median = np.median(segment)
        mad = np.median(np.abs(segment - median))
        if mad == 0:
            if data[i] != median:
                result[i] = median
        elif abs(data[i] - median) > threshold * scale * mad:
            result[i] = median
    return result


def savitzky_golay(data: np.ndarray, window: int = 9, degree: int = 3) -> np.ndarray:
    if window % 2 == 0 or window <= degree:
        raise ValueError("window must be odd and greater than degree")
    radius = window // 2
    x = np.arange(-radius, radius + 1, dtype=float)
    design = np.vander(x, degree + 1, increasing=True)
    kernel = np.linalg.pinv(design)[0]
    padded = np.pad(data, radius, mode="edge")
    return np.convolve(padded, kernel[::-1], mode="valid")


def simple_kalman(data: np.ndarray, measurement_error: float = 10.0, q: float = 1.0) -> np.ndarray:
    estimate_error = measurement_error
    last = float(data[0])
    result = np.empty_like(data, dtype=float)
    for i, value in enumerate(data):
        gain = estimate_error / (estimate_error + measurement_error)
        current = last + gain * (value - last)
        estimate_error = (1.0 - gain) * estimate_error + abs(last - current) * q
        last = current
        result[i] = current
    return result


def alpha_beta(data: np.ndarray, dt: float = 0.02, process_sigma: float = 3.0, noise_sigma: float = 0.7) -> np.ndarray:
    lam = process_sigma * dt * dt / noise_sigma
    r = (4.0 + lam - math.sqrt(8.0 * lam + lam * lam)) / 4.0
    alpha = 1.0 - r * r
    beta = 2.0 * (2.0 - alpha) - 4.0 * math.sqrt(1.0 - alpha)
    position = float(data[0])
    velocity = 0.0
    result = np.empty_like(data, dtype=float)
    for i, measurement in enumerate(data):
        predicted = position + velocity * dt
        residual = measurement - predicted
        position = predicted + alpha * residual
        velocity += beta * residual / dt
        result[i] = position
    return result


def deadband(data: np.ndarray, limit: float = 2.0) -> np.ndarray:
    result = np.empty_like(data, dtype=float)
    last = float(data[0])
    for i, value in enumerate(data):
        if abs(value - last) >= limit:
            last = float(value)
        result[i] = last
    return result


FILTERS: Dict[str, Callable[[np.ndarray], np.ndarray]] = {
    "Moving average": moving_average,
    "Exponential": exponential,
    "Weighted average": weighted_average,
    "Gaussian": gaussian,
    "Median": centered_median,
    "Hampel": hampel,
    "Savitzky-Golay": savitzky_golay,
    "Simple Kalman": simple_kalman,
    "Alpha-Beta": alpha_beta,
    "Deadband": deadband,
}


def make_signal(length: int = 720) -> tuple[np.ndarray, np.ndarray]:
    rng = np.random.default_rng(SEED)
    sample = np.arange(length)
    clean = 50.0 + 8.0 * np.sin(2 * np.pi * sample / 150) + 3.0 * np.sin(2 * np.pi * sample / 43)
    clean += np.where(sample >= 245, 11.0, 0.0)
    clean += np.where(sample >= 500, -0.035 * (sample - 500), 0.0)
    noisy = clean + rng.normal(0.0, 3.2, length)
    spike_positions = np.array([63, 117, 184, 271, 338, 421, 527, 604, 676])
    spike_values = np.array([24, -28, 31, -25, 27, -30, 26, -24, 29])
    noisy[spike_positions] += spike_values
    return clean, noisy


def estimate_lag(clean: np.ndarray, filtered: np.ndarray, max_lag: int = 25) -> int:
    best_lag, best_error = 0, float("inf")
    for lag in range(-max_lag, max_lag + 1):
        if lag < 0:
            reference, candidate = clean[-lag:], filtered[:lag]
        elif lag > 0:
            reference, candidate = clean[:-lag], filtered[lag:]
        else:
            reference, candidate = clean, filtered
        error = float(np.mean((reference - candidate) ** 2))
        if error < best_error:
            best_lag, best_error = lag, error
    return best_lag


def calculate_metrics(clean: np.ndarray, noisy: np.ndarray, outputs: Dict[str, np.ndarray]) -> list[dict[str, float | str]]:
    warmup = 20
    reference = clean[warmup:-warmup]
    raw = noisy[warmup:-warmup]
    input_noise_power = float(np.mean((raw - reference) ** 2))
    rows: list[dict[str, float | str]] = []
    for name, values in outputs.items():
        filtered = values[warmup:-warmup]
        error = filtered - reference
        output_noise_power = float(np.mean(error * error))
        rows.append(
            {
                "filter": name,
                "rmse": math.sqrt(output_noise_power),
                "mae": float(np.mean(np.abs(error))),
                "snr_improvement_db": 10.0 * math.log10(input_noise_power / output_noise_power),
                "lag_samples": estimate_lag(reference, filtered),
            }
        )
    return sorted(rows, key=lambda row: float(row["rmse"]))


def runtime_benchmark(length: int = 12_000, repeats: int = 5) -> list[dict[str, float | str]]:
    rng = np.random.default_rng(SEED + 1)
    data = 50 + 10 * np.sin(np.arange(length) / 25) + rng.normal(0, 3, length)
    rows: list[dict[str, float | str]] = []
    for name, function in FILTERS.items():
        function(data[:500])
        timings = []
        for _ in range(repeats):
            start = time.perf_counter()
            function(data)
            timings.append((time.perf_counter() - start) * 1000.0)
        rows.append({"filter": name, "median_ms": float(np.median(timings)), "samples": length})
    return sorted(rows, key=lambda row: float(row["median_ms"]))


def style_axis(axis: plt.Axes, title: str) -> None:
    axis.set_title(title, loc="left", fontsize=12, fontweight="bold")
    axis.grid(True, color="#cbd5e1", alpha=0.45, linewidth=0.7)
    axis.spines[["top", "right"]].set_visible(False)
    axis.set_facecolor("#f8fafc")


def save_figure(figure: plt.Figure, name: str) -> None:
    figure.tight_layout()
    figure.savefig(OUTPUT / name, dpi=170, bbox_inches="tight", facecolor="white")
    plt.close(figure)


def plot_overview(clean: np.ndarray, noisy: np.ndarray, outputs: Dict[str, np.ndarray]) -> None:
    groups = [
        ("Smoothing filters", ["Moving average", "Exponential", "Weighted average", "Gaussian"]),
        ("Robust filters", ["Median", "Hampel", "Savitzky-Golay"]),
        ("State estimators", ["Simple Kalman", "Alpha-Beta"]),
    ]
    figure, axes = plt.subplots(3, 1, figsize=(14, 11), sharex=True)
    x = np.arange(len(clean))
    for axis, (title, names) in zip(axes, groups):
        axis.plot(x, noisy, color=COLORS["noisy"], linewidth=0.8, alpha=0.55, label="Noisy input")
        axis.plot(x, clean, color=COLORS["clean"], linewidth=2.2, label="Clean reference")
        for name in names:
            axis.plot(x, outputs[name], color=COLORS[name], linewidth=1.25, label=name)
        style_axis(axis, title)
        axis.legend(ncol=3, fontsize=8, frameon=False, loc="upper right")
        axis.set_ylabel("Value")
    axes[-1].set_xlabel("Sample")
    figure.suptitle("Noise-filter simulation on the same synthetic sensor signal", fontsize=17, fontweight="bold", y=1.01)
    save_figure(figure, "signal-comparison.png")


def plot_impulses(clean: np.ndarray, noisy: np.ndarray, outputs: Dict[str, np.ndarray]) -> None:
    figure, axes = plt.subplots(2, 1, figsize=(14, 7), sharex=False)
    for axis, (lo, hi) in zip(axes, [(40, 205), (390, 550)]):
        x = np.arange(lo, hi)
        axis.plot(x, noisy[lo:hi], color=COLORS["noisy"], linewidth=1.0, alpha=0.8, label="Noisy input")
        axis.plot(x, clean[lo:hi], color=COLORS["clean"], linewidth=2.2, label="Clean reference")
        for name in ["Moving average", "Median", "Hampel"]:
            axis.plot(x, outputs[name][lo:hi], color=COLORS[name], linewidth=1.45, label=name)
        style_axis(axis, f"Impulse-noise detail: samples {lo}–{hi - 1}")
        axis.legend(ncol=5, fontsize=8, frameon=False)
        axis.set_ylabel("Value")
    axes[-1].set_xlabel("Sample")
    figure.suptitle("Median and Hampel filters reject spikes without smearing them", fontsize=16, fontweight="bold", y=1.01)
    save_figure(figure, "impulse-rejection.png")


def plot_step_response() -> None:
    rng = np.random.default_rng(SEED + 2)
    x = np.arange(220)
    clean = np.where(x < 80, 30.0, np.where(x < 150, 55.0, 40.0))
    noisy = clean + rng.normal(0.0, 1.7, len(x))
    figure, axis = plt.subplots(figsize=(14, 5.2))
    axis.plot(x, noisy, color=COLORS["noisy"], linewidth=0.8, alpha=0.55, label="Noisy input")
    axis.plot(x, clean, color=COLORS["clean"], linewidth=2.5, label="Clean step")
    for name in ["Moving average", "Exponential", "Gaussian", "Simple Kalman", "Deadband"]:
        axis.plot(x, FILTERS[name](noisy), color=COLORS[name], linewidth=1.5, label=name)
    style_axis(axis, "Step response and filter delay")
    axis.set_xlabel("Sample")
    axis.set_ylabel("Value")
    axis.legend(ncol=4, fontsize=9, frameon=False)
    save_figure(figure, "step-response.png")


def plot_metrics(metrics: list[dict[str, float | str]]) -> None:
    ordered = sorted(metrics, key=lambda row: float(row["rmse"]), reverse=True)
    names = [str(row["filter"]) for row in ordered]
    rmse = [float(row["rmse"]) for row in ordered]
    improvement = [float(row["snr_improvement_db"]) for row in ordered]
    y = np.arange(len(names))
    figure, axes = plt.subplots(1, 2, figsize=(14, 6.7))
    axes[0].barh(y, rmse, color=[COLORS[name] for name in names])
    axes[0].set_yticks(y, names)
    axes[0].set_xlabel("RMSE (lower is better)")
    style_axis(axes[0], "Error against clean reference")
    for i, value in enumerate(rmse):
        axes[0].text(value + 0.04, i, f"{value:.2f}", va="center", fontsize=8)
    axes[1].barh(y, improvement, color=[COLORS[name] for name in names])
    axes[1].set_yticks(y, [""] * len(names))
    axes[1].set_xlabel("SNR improvement, dB (higher is better)")
    style_axis(axes[1], "Noise reduction")
    for i, value in enumerate(improvement):
        axes[1].text(value + 0.04, i, f"{value:.1f}", va="center", fontsize=8)
    figure.suptitle("Quality metrics: identical input and parameters", fontsize=16, fontweight="bold", y=1.01)
    save_figure(figure, "quality-metrics.png")


def plot_runtime(rows: list[dict[str, float | str]]) -> None:
    ordered = sorted(rows, key=lambda row: float(row["median_ms"]), reverse=True)
    names = [str(row["filter"]) for row in ordered]
    values = [float(row["median_ms"]) for row in ordered]
    figure, axis = plt.subplots(figsize=(11, 6.7))
    y = np.arange(len(names))
    axis.barh(y, values, color=[COLORS[name] for name in names])
    axis.set_yticks(y, names)
    axis.set_xlabel("Median execution time, ms (12,000 samples)")
    style_axis(axis, "Python reference benchmark — relative cost, not C# wall-clock time")
    for i, value in enumerate(values):
        axis.text(value + max(values) * 0.008, i, f"{value:.2f}", va="center", fontsize=8)
    save_figure(figure, "runtime-reference.png")


def write_csv(name: str, rows: list[dict[str, float | str]]) -> None:
    if not rows:
        return
    with (OUTPUT / name).open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    plt.rcParams.update({"font.family": "DejaVu Sans", "axes.labelcolor": "#334155", "text.color": "#0f172a"})
    clean, noisy = make_signal()
    outputs = {name: function(noisy) for name, function in FILTERS.items()}
    metrics = calculate_metrics(clean, noisy, outputs)
    runtime = runtime_benchmark()
    plot_overview(clean, noisy, outputs)
    plot_impulses(clean, noisy, outputs)
    plot_step_response()
    plot_metrics(metrics)
    plot_runtime(runtime)
    write_csv("quality-metrics.csv", metrics)
    write_csv("runtime-reference.csv", runtime)
    print(f"Generated {len(list(OUTPUT.iterdir()))} benchmark artifacts in {OUTPUT.relative_to(ROOT)}")
    for row in metrics:
        print(f"{row['filter']:<20} RMSE={row['rmse']:.3f}  SNR+={row['snr_improvement_db']:.2f} dB  lag={row['lag_samples']}")


if __name__ == "__main__":
    main()
