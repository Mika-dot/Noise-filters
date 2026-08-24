#!/usr/bin/env python3
"""Rebuild deterministic README charts and benchmark.csv.

The simulation intentionally mirrors the documented C# parameter sets. It is a
documentation benchmark, not a universal ranking: another signal changes winners.
"""
from __future__ import annotations

import csv
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "charts"
OUT.mkdir(parents=True, exist_ok=True)
SEED = 20260824


def mirror_index(index: int, length: int) -> int:
    while index < 0 or index >= length:
        index = -index if index < 0 else 2 * length - index - 2
    return index


def window(signal: np.ndarray, center: int, size: int) -> np.ndarray:
    radius = size // 2
    return np.array([signal[mirror_index(center + offset, len(signal))] for offset in range(-radius, radius + 1)])


def centered_mean(signal: np.ndarray, size: int = 9) -> np.ndarray:
    return np.array([window(signal, i, size).mean() for i in range(len(signal))])


def median(signal: np.ndarray, size: int = 7) -> np.ndarray:
    return np.array([np.median(window(signal, i, size)) for i in range(len(signal))])


def gaussian(signal: np.ndarray, size: int = 9, sigma: float = 1.8) -> np.ndarray:
    radius = size // 2
    kernel = np.exp(-(np.arange(-radius, radius + 1) ** 2) / (2 * sigma**2))
    kernel /= kernel.sum()
    return np.array([(window(signal, i, size) * kernel).sum() for i in range(len(signal))])


def hampel(signal: np.ndarray, size: int = 7, threshold: float = 3.0) -> np.ndarray:
    result = signal.copy()
    for i, value in enumerate(signal):
        values = window(signal, i, size)
        center = np.median(values)
        mad = np.median(np.abs(values - center))
        if abs(value - center) > max(threshold * 1.4826 * mad, 1e-12):
            result[i] = center
    return result


def ema(signal: np.ndarray, alpha: float = 0.18) -> np.ndarray:
    result = np.empty_like(signal)
    result[0] = signal[0]
    for i in range(1, len(signal)):
        result[i] = alpha * signal[i] + (1 - alpha) * result[i - 1]
    return result


def savgol(signal: np.ndarray, size: int = 9, order: int = 3) -> np.ndarray:
    radius = size // 2
    x = np.arange(-radius, radius + 1, dtype=float)
    design = np.vander(x, order + 1, increasing=True)
    coefficients = np.linalg.pinv(design)[0]
    return np.array([(window(signal, i, size) * coefficients).sum() for i in range(len(signal))])


def kalman(signal: np.ndarray, measurement_noise: float = 25.0, process_noise: float = 0.25) -> np.ndarray:
    estimate, variance = signal[0], 1.0
    result = np.empty_like(signal)
    result[0] = estimate
    for i in range(1, len(signal)):
        variance += process_noise
        gain = variance / (variance + measurement_noise)
        estimate += gain * (signal[i] - estimate)
        variance *= 1 - gain
        result[i] = estimate
    return result


def one_euro(signal: np.ndarray, rate: float = 50.0, min_cutoff: float = 1.0, beta: float = 0.025) -> np.ndarray:
    def factor(cutoff: float) -> float:
        tau, dt = 1 / (2 * np.pi * cutoff), 1 / rate
        return 1 / (1 + tau / dt)
    result = np.empty_like(signal)
    result[0], derivative = signal[0], 0.0
    derivative_alpha = factor(1.0)
    for i in range(1, len(signal)):
        raw_derivative = (signal[i] - signal[i - 1]) * rate
        derivative += derivative_alpha * (raw_derivative - derivative)
        alpha = factor(min_cutoff + beta * abs(derivative))
        result[i] = result[i - 1] + alpha * (signal[i] - result[i - 1])
    return result


def rmse(clean: np.ndarray, candidate: np.ndarray) -> float:
    return float(np.sqrt(np.mean((clean - candidate) ** 2)))


def lag(clean: np.ndarray, candidate: np.ndarray, maximum: int = 30) -> int:
    scores = [np.mean((clean[:-shift or None] - candidate[shift:]) ** 2) for shift in range(maximum + 1)]
    return int(np.argmin(scores))


def theme() -> None:
    plt.rcParams.update({
        "font.family": "DejaVu Sans", "font.size": 10, "axes.titlesize": 15,
        "axes.labelcolor": "#65717d", "axes.edgecolor": "#d8d9d5",
        "xtick.color": "#65717d", "ytick.color": "#65717d", "grid.color": "#e7e7e2",
        "figure.facecolor": "#f5f3ed", "axes.facecolor": "#ffffff",
    })


def save(fig: plt.Figure, name: str) -> None:
    fig.savefig(OUT / name, dpi=160, bbox_inches="tight", facecolor=fig.get_facecolor())
    plt.close(fig)


def main() -> None:
    theme()
    rng = np.random.default_rng(SEED)
    n = 600
    t = np.arange(n) / 50
    clean = 18 * np.sin(2 * np.pi * .32 * t) + 6 * np.sin(2 * np.pi * .06 * t) + np.where(t > 6, 15, 0)
    noisy = clean + rng.normal(0, 5.2, n)
    spike_indices = rng.choice(np.arange(15, n - 15), size=25, replace=False)
    noisy[spike_indices] += rng.choice([-1, 1], len(spike_indices)) * rng.uniform(30, 55, len(spike_indices))

    algorithms = {
        "Moving average": centered_mean(noisy, 9),
        "Median": median(noisy, 7),
        "Gaussian": gaussian(noisy, 9, 1.8),
        "Hampel": hampel(noisy, 7, 3),
        "Savitzky–Golay": savgol(noisy, 9, 3),
        "EMA": ema(noisy, .18),
        "One Euro": one_euro(noisy),
        "Kalman": kalman(noisy),
    }

    fig, axes = plt.subplots(2, 2, figsize=(14, 8), sharex=True, sharey=True)
    for ax, (name, filtered) in zip(axes.flat, list(algorithms.items())[:4]):
        ax.plot(t, noisy, color="#c5c9cc", lw=.8, label="Шум")
        ax.plot(t, clean, color="#00a878", lw=1.7, ls="--", label="Идеал")
        ax.plot(t, filtered, color="#1268fb", lw=2, label=name)
        ax.set_title(f"{name} · RMSE {rmse(clean, filtered):.2f}", loc="left", fontweight="bold")
        ax.grid(True, alpha=.8); ax.set_xlim(0, t[-1])
    axes[0, 0].legend(ncol=3, frameon=False, loc="upper left")
    fig.suptitle("Один сигнал — четыре характера фильтра", x=.08, ha="left", fontsize=22, fontweight="bold")
    fig.supxlabel("Время, с"); fig.supylabel("Значение")
    fig.tight_layout(rect=(0, 0, 1, .95)); save(fig, "overview.png")

    zoom = slice(225, 335)
    fig, ax = plt.subplots(figsize=(14, 5.2))
    ax.plot(t[zoom], noisy[zoom], color="#b7bdc2", lw=1.2, label="Сырые данные")
    ax.plot(t[zoom], clean[zoom], color="#00a878", lw=2, ls="--", label="Идеал")
    for name, color in [("Median", "#1268fb"), ("Hampel", "#ff5c35"), ("Savitzky–Golay", "#7c4dff")]:
        ax.plot(t[zoom], algorithms[name][zoom], color=color, lw=2, label=name)
    ax.set_title("Импульсные выбросы: Median и Hampel против сглаживающего фильтра", loc="left", fontweight="bold")
    ax.grid(True); ax.legend(ncol=5, frameon=False, loc="upper left"); ax.set_xlabel("Время, с"); ax.set_ylabel("Значение")
    fig.tight_layout(); save(fig, "impulse-comparison.png")

    step = np.r_[np.zeros(100), np.ones(200) * 50] + rng.normal(0, 2, 300)
    step_clean = np.r_[np.zeros(100), np.ones(200) * 50]
    responses = {"EMA α=0.18": ema(step, .18), "Gaussian": gaussian(step, 9, 1.8), "One Euro": one_euro(step), "Kalman": kalman(step)}
    fig, ax = plt.subplots(figsize=(14, 5.2))
    ax.plot(step_clean, color="#101820", lw=2.5, label="Идеальная ступень")
    for (name, values), color in zip(responses.items(), ["#1268fb", "#00a878", "#ff5c35", "#7c4dff"]):
        ax.plot(values, lw=2, color=color, label=name)
    ax.axvline(100, color="#aeb4b9", ls=":"); ax.set_xlim(70, 175); ax.grid(True)
    ax.set_title("Переходная характеристика: цена подавления шума — задержка", loc="left", fontweight="bold")
    ax.legend(ncol=5, frameon=False, loc="lower right"); ax.set_xlabel("Отсчёт"); ax.set_ylabel("Значение")
    fig.tight_layout(); save(fig, "step-response.png")

    before = rmse(clean, noisy)
    rows = []
    for name, values in algorithms.items():
        rows.append((name, rmse(clean, values), 100 * (1 - rmse(clean, values) / before), lag(clean, values)))
    rows.sort(key=lambda row: row[1])
    with (OUT / "benchmark.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream, lineterminator="\n"); writer.writerow(["filter", "rmse", "error_reduction_percent", "estimated_lag_samples"])
        writer.writerows([[name, f"{error:.4f}", f"{reduction:.2f}", delay] for name, error, reduction, delay in rows])
    names = [row[0] for row in rows][::-1]; errors = [row[1] for row in rows][::-1]
    fig, ax = plt.subplots(figsize=(12, 6)); bars = ax.barh(names, errors, color=["#1268fb" if e == min(errors) else "#9abcf8" for e in errors])
    ax.axvline(before, color="#ff5c35", ls="--", lw=2, label=f"Без фильтра: {before:.2f}")
    ax.bar_label(bars, fmt="%.2f", padding=5); ax.set_xlabel("RMSE, меньше — лучше"); ax.grid(axis="x"); ax.legend(frameon=False)
    ax.set_title("Детерминированная симуляция: синус + ступень + Gaussian noise + 25 выбросов", loc="left", fontweight="bold")
    fig.tight_layout(); save(fig, "benchmark.png")
    print(f"Generated 4 charts and benchmark.csv in {OUT}")


if __name__ == "__main__":
    main()
