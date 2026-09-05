#!/usr/bin/env python3
"""Reproducible benchmark for adaptive VMD research branch.

Two deliberately different scenarios are generated:
A) structured/narrow-band interference on a non-stationary signal;
B) broadband Gaussian + impulsive contamination.

The point is not to crown a universal winner, but to expose where adaptive
mode decomposition helps and where robust local statistics remain superior.
"""
from __future__ import annotations

import csv
import math
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np

SEED = 20260905
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "charts"
OUT.mkdir(parents=True, exist_ok=True)


def rmse(clean: np.ndarray, estimate: np.ndarray) -> float:
    return float(np.sqrt(np.mean((clean - estimate) ** 2)))


def snr_db(clean: np.ndarray, estimate: np.ndarray) -> float:
    error = np.sum((clean - estimate) ** 2)
    return float(10.0 * np.log10(np.sum(clean**2) / max(error, 1e-30)))


def correlation(a: np.ndarray, b: np.ndarray) -> float:
    if np.std(a) < 1e-14 or np.std(b) < 1e-14:
        return 0.0
    return float(np.corrcoef(a, b)[0, 1])


def mirror_index(i: int, n: int) -> int:
    if n <= 1:
        return 0
    while i < 0 or i >= n:
        if i < 0:
            i = -i
        if i >= n:
            i = 2 * n - i - 2
    return i


def moving_average(x: np.ndarray, window: int = 7) -> np.ndarray:
    radius = window // 2
    return np.array([
        np.mean([x[mirror_index(i + j, len(x))] for j in range(-radius, radius + 1)])
        for i in range(len(x))
    ])


def median_filter(x: np.ndarray, window: int = 7) -> np.ndarray:
    radius = window // 2
    return np.array([
        np.median([x[mirror_index(i + j, len(x))] for j in range(-radius, radius + 1)])
        for i in range(len(x))
    ])


def savitzky_golay(x: np.ndarray, window: int = 9, order: int = 3) -> np.ndarray:
    radius = window // 2
    positions = np.arange(-radius, radius + 1, dtype=float)
    design = np.vander(positions, N=order + 1, increasing=True)
    coefficients = np.linalg.pinv(design)[0]
    result = np.empty_like(x)
    for i in range(len(x)):
        result[i] = sum(
            coefficients[tap] * x[mirror_index(i + tap - radius, len(x))]
            for tap in range(window)
        )
    return result


def reweighted_ssa(x: np.ndarray, window: int = 64, epsilon: float = 1e-8) -> np.ndarray:
    n = len(x)
    rows = min(window, n - 1)
    columns = n - rows + 1
    trajectory = np.column_stack([x[j : j + rows] for j in range(columns)])
    u, singular, vt = np.linalg.svd(trajectory, full_matrices=False)
    tail = singular[len(singular) // 2 :]
    noise_floor = float(np.median(tail)) if len(tail) else 0.0
    regularization = max(epsilon, noise_floor * noise_floor)
    shrunk = np.maximum(0.0, singular - regularization / (singular + epsilon))
    reconstructed = (u * shrunk) @ vt
    output = np.zeros(n)
    count = np.zeros(n)
    for i in range(rows):
        for j in range(columns):
            output[i + j] += reconstructed[i, j]
            count[i + j] += 1
    return output / count


def permutation_entropy(x: np.ndarray, order: int = 3, delay: int = 1) -> float:
    vectors = len(x) - (order - 1) * delay
    if vectors <= 0:
        return 0.0
    counts: dict[tuple[int, ...], int] = {}
    for start in range(vectors):
        values = [(float(x[start + j * delay]), j) for j in range(order)]
        pattern = tuple(index for _, index in sorted(values, key=lambda z: (z[0], z[1])))
        counts[pattern] = counts.get(pattern, 0) + 1
    probabilities = np.array(list(counts.values()), dtype=float) / vectors
    entropy = -np.sum(probabilities * np.log(probabilities))
    return float(entropy / math.log(math.factorial(order)))


def analytic_spectrum(x: np.ndarray) -> np.ndarray:
    n = len(x)
    positive = np.fft.fft(x)[: n // 2 + 1].copy()
    if n % 2 == 0:
        if len(positive) > 2:
            positive[1:-1] *= 2.0
    elif len(positive) > 1:
        positive[1:] *= 2.0
    return positive


def inverse_analytic_real(spectrum: np.ndarray, n: int) -> np.ndarray:
    full = np.zeros(n, dtype=complex)
    full[: len(spectrum)] = spectrum
    return np.fft.ifft(full).real


def vmd(
    x: np.ndarray,
    modes: int,
    alpha: float,
    tau: float = 0.1,
    tolerance: float = 2e-4,
    iterations: int = 80,
) -> tuple[np.ndarray, np.ndarray, float]:
    n = len(x)
    f_hat = analytic_spectrum(x)
    bins = len(f_hat)
    frequencies = np.arange(bins, dtype=float) / n
    u = np.zeros((modes, bins), dtype=complex)
    lagrange = np.zeros(bins, dtype=complex)
    centers = np.array([0.5 * (k + 0.5) / modes for k in range(modes)], dtype=float)
    spectral_energy = max(1e-20, float(np.sum(np.abs(f_hat) ** 2)))

    for _ in range(iterations):
        previous = u.copy()
        sum_modes = np.sum(u, axis=0)
        for k in range(modes):
            old = u[k].copy()
            residual = f_hat - (sum_modes - old) - 0.5 * lagrange
            denominator = 1.0 + alpha * (frequencies - centers[k]) ** 2
            updated = residual / denominator
            u[k] = updated
            sum_modes += updated - old
            energy = np.abs(updated[1:]) ** 2
            if np.sum(energy) > 1e-20:
                centers[k] = float(np.sum(frequencies[1:] * energy) / np.sum(energy))
        lagrange += tau * (sum_modes - f_hat)
        change = math.sqrt(float(np.sum(np.abs(u - previous) ** 2)) / spectral_energy)
        if change <= tolerance:
            break

    reconstructed_modes = np.array([inverse_analytic_real(mode, n) for mode in u])
    reconstruction = np.sum(reconstructed_modes, axis=0)
    return reconstructed_modes, centers, rmse(x, reconstruction)


def inter_mode_overlap(modes: np.ndarray) -> float:
    values = []
    for i in range(len(modes)):
        for j in range(i + 1, len(modes)):
            values.append(abs(correlation(modes[i], modes[j])))
    return float(np.mean(values)) if values else 0.0


def candidate_score(x: np.ndarray, modes: np.ndarray, reconstruction_rmse: float) -> float:
    rms = max(1e-12, float(np.sqrt(np.mean(x**2))))
    entropy = float(np.mean([permutation_entropy(mode) for mode in modes]))
    return (
        2.5 * reconstruction_rmse / rms
        + 0.35 * entropy
        + 0.65 * inter_mode_overlap(modes)
        + 0.025 * len(modes)
    )


def adaptive_vmd_structure(
    x: np.ndarray,
    max_modes: int = 6,
    alphas: tuple[float, ...] = (250.0, 500.0, 1000.0, 2000.0, 4000.0),
) -> tuple[np.ndarray, int, float, np.ndarray, np.ndarray, np.ndarray]:
    best: tuple[float, int, float] | None = None
    for k in range(2, max_modes + 1):
        for alpha in alphas:
            modes, _, reconstruction_rmse = vmd(
                x, k, alpha, tolerance=2e-4, iterations=80
            )
            score = candidate_score(x, modes, reconstruction_rmse)
            if best is None or score < best[0]:
                best = (score, k, alpha)

    assert best is not None
    _, k, alpha = best
    modes, centers, _ = vmd(
        x, k, alpha, tolerance=5e-6, iterations=180
    )
    entropies = np.array([permutation_entropy(mode) for mode in modes])
    correlations = np.array([abs(correlation(x, mode)) for mode in modes])
    structure = correlations * np.maximum(0.0, 1.0 - entropies)
    strongest = max(1e-20, float(np.max(structure)))
    relative = structure / strongest

    keep = relative >= 0.10
    if not np.any(keep):
        keep[int(np.argmax(structure))] = True
    output = np.sum(modes[keep], axis=0)
    return output, k, alpha, entropies, correlations, centers


def scenario_structured(rng: np.random.Generator) -> tuple[np.ndarray, np.ndarray]:
    fs = 100.0
    n = 512
    t = np.arange(n) / fs
    chirp_phase = 2 * np.pi * (4 * t + 0.5 * (14 / t[-1]) * t * t)
    clean = 1.3 * np.sin(2 * np.pi * 1.7 * t) + 0.7 * np.sin(chirp_phase)
    clean += 0.35 * (t > 2.5)
    clean += 1.4 * np.exp(-0.5 * ((t - 1.5) / 0.025) ** 2)
    clean -= 1.0 * np.exp(-0.5 * ((t - 3.8) / 0.04) ** 2)

    noise = rng.normal(0, 0.30, n)
    noise += 0.90 * np.sin(2 * np.pi * 31 * t)
    noise += 0.35 * np.sin(2 * np.pi * 42 * t)
    indices = rng.choice(n, 6, replace=False)
    noise[indices] += rng.choice([-1, 1], 6) * rng.uniform(1.2, 2.0, 6)
    return clean, clean + noise


def scenario_broadband(rng: np.random.Generator) -> tuple[np.ndarray, np.ndarray]:
    fs = 100.0
    n = 512
    t = np.arange(n) / fs
    chirp_phase = 2 * np.pi * (5 * t + 0.5 * (8 / t[-1]) * t * t)
    clean = 1.6 * np.sin(2 * np.pi * 1.8 * t) + 0.65 * np.sin(chirp_phase)
    clean += 0.45 * (t > 2.5)
    clean += 1.0 * np.exp(-0.5 * ((t - 1.5) / 0.04) ** 2)
    clean -= 0.8 * np.exp(-0.5 * ((t - 3.8) / 0.06) ** 2)

    noise = rng.normal(0, 0.85, n)
    noise += 0.45 * np.sin(2 * np.pi * 28 * t)
    indices = rng.choice(n, 18, replace=False)
    noise[indices] += rng.choice([-1, 1], 18) * rng.uniform(2.5, 5.0, 18)
    return clean, clean + noise


def evaluate(name: str, clean: np.ndarray, noisy: np.ndarray) -> list[dict[str, object]]:
    adaptive, k, alpha, _, _, _ = adaptive_vmd_structure(noisy)
    methods = {
        "Raw": noisy,
        "Moving average": moving_average(noisy, 7),
        "Median": median_filter(noisy, 7),
        "Savitzky-Golay": savitzky_golay(noisy, 9, 3),
        "Reweighted SVD": reweighted_ssa(noisy, 64),
        "Adaptive VMD structure": adaptive,
    }
    rows = []
    for method, signal in methods.items():
        rows.append(
            {
                "scenario": name,
                "method": method,
                "rmse": rmse(clean, signal),
                "snr_db": snr_db(clean, signal),
                "correlation": correlation(clean, signal),
                "selected_k": k if method == "Adaptive VMD structure" else "",
                "selected_alpha": alpha if method == "Adaptive VMD structure" else "",
            }
        )
    return rows


def main() -> None:
    rng = np.random.default_rng(SEED)
    scenarios = {
        "structured_interference": scenario_structured(rng),
        "broadband_impulsive": scenario_broadband(rng),
    }
    rows: list[dict[str, object]] = []
    for name, (clean, noisy) in scenarios.items():
        rows.extend(evaluate(name, clean, noisy))

    csv_path = OUT / "adaptive_vmd_benchmark.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        for row in rows:
            formatted = dict(row)
            for key in ("rmse", "snr_db", "correlation"):
                formatted[key] = f"{float(row[key]):.6f}"
            writer.writerow(formatted)

    methods = [
        "Raw",
        "Moving average",
        "Median",
        "Savitzky-Golay",
        "Reweighted SVD",
        "Adaptive VMD structure",
    ]
    fig, axes = plt.subplots(1, 2, figsize=(13, 4.6), sharey=False)
    for ax, scenario in zip(axes, scenarios):
        data = {row["method"]: float(row["rmse"]) for row in rows if row["scenario"] == scenario}
        values = [data[m] for m in methods]
        ax.bar(np.arange(len(methods)), values)
        ax.set_title(scenario.replace("_", " "))
        ax.set_ylabel("RMSE (lower is better)")
        ax.set_xticks(np.arange(len(methods)))
        ax.set_xticklabels(methods, rotation=38, ha="right")
        ax.grid(axis="y", alpha=0.25)
    fig.suptitle("Adaptive VMD benchmark — two noise regimes")
    fig.tight_layout()
    fig.savefig(OUT / "adaptive_vmd_benchmark.svg", format="svg", bbox_inches="tight")
    plt.close(fig)

    for row in rows:
        print(
            f"{row['scenario']:24s} {row['method']:24s} "
            f"RMSE={float(row['rmse']):.4f} SNR={float(row['snr_db']):6.2f} dB "
            f"r={float(row['correlation']):.4f}"
        )


if __name__ == "__main__":
    main()
