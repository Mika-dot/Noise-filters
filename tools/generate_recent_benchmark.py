#!/usr/bin/env python3
"""Deterministic benchmark for feature/recent-denoising-2026."""
from pathlib import Path
import csv
import math
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "charts"
OUT.mkdir(parents=True, exist_ok=True)
SEED = 20260905


def mirror(index, length):
    if length <= 1:
        return 0
    while index < 0 or index >= length:
        if index < 0:
            index = -index - 1
        if index >= length:
            index = 2 * length - index - 1
    return index


def savitzky_golay(data, window=11, order=3):
    radius = window // 2
    x = np.arange(-radius, radius + 1, dtype=float)
    design = np.vander(x, N=order + 1, increasing=True)
    coeff = np.linalg.pinv(design)[0]
    out = np.zeros_like(data)
    for i in range(len(data)):
        out[i] = sum(coeff[j] * data[mirror(i + j - radius, len(data))] for j in range(window))
    return out


def wavelet_haar(data, levels=4, multiplier=0.8):
    n = len(data)
    padded = 1
    while padded < n:
        padded <<= 1
    coeff = np.array([data[mirror(i, n)] for i in range(padded)], dtype=float)
    temp = np.zeros(padded)
    inv = 1.0 / math.sqrt(2.0)
    length = padded
    for _ in range(levels):
        half = length // 2
        for i in range(half):
            a, b = coeff[2 * i], coeff[2 * i + 1]
            temp[i] = (a + b) * inv
            temp[half + i] = (a - b) * inv
        coeff[:length] = temp[:length]
        length = half
    sigma = np.median(np.abs(coeff[padded // 2:])) / 0.6744897501960817
    threshold = multiplier * sigma * math.sqrt(2.0 * math.log(max(2, n)))
    approximation = padded >> levels
    detail = coeff[approximation:]
    coeff[approximation:] = np.sign(detail) * np.maximum(0.0, np.abs(detail) - threshold)
    reconstructed = approximation * 2
    while reconstructed <= padded:
        half = reconstructed // 2
        average = coeff[:half].copy()
        detail = coeff[half:reconstructed].copy()
        temp[:reconstructed:2] = (average + detail) * inv
        temp[1:reconstructed:2] = (average - detail) * inv
        coeff[:reconstructed] = temp[:reconstructed]
        if reconstructed == padded:
            break
        reconstructed *= 2
    return coeff[:n].copy()


def trajectory(data, window):
    columns = len(data) - window + 1
    return np.column_stack([data[j:j + window] for j in range(columns)])


def diagonal_average(matrix, n):
    rows, columns = matrix.shape
    result = np.zeros(n)
    counts = np.zeros(n)
    for row in range(rows):
        for column in range(columns):
            result[row + column] += matrix[row, column]
            counts[row + column] += 1
    return result / counts


def ssa(data, window=48, rank=6):
    matrix = trajectory(data, window)
    values, vectors = np.linalg.eigh(matrix @ matrix.T)
    order = np.argsort(values)[::-1][:rank]
    basis = vectors[:, order]
    return diagonal_average(basis @ (basis.T @ matrix), len(data))


def reweighted_svd(data, window=48, epsilon=1e-8):
    matrix = trajectory(data, window)
    values, vectors = np.linalg.eigh(matrix @ matrix.T)
    order = np.argsort(values)[::-1]
    component_count = min(matrix.shape)
    singular = np.sqrt(np.maximum(0.0, values[order[:component_count]]))
    noise_floor = np.median(singular[component_count // 2:])
    regularization = max(epsilon, noise_floor * noise_floor)
    ratios = np.maximum(0.0, singular - regularization / (singular + epsilon)) / np.maximum(singular, epsilon)
    reconstructed = np.zeros_like(matrix)
    for component, index in enumerate(order[:component_count]):
        if values[index] <= 1e-14 or ratios[component] <= 0:
            continue
        u = vectors[:, index:index + 1]
        reconstructed += ratios[component] * (u @ (u.T @ matrix))
    return diagonal_average(reconstructed, len(data))


def total_variation(data, lam=1.3, iterations=200):
    dual = np.zeros(len(data) - 1)
    x = np.zeros_like(data)
    for _ in range(iterations):
        x[0] = data[0] + dual[0]
        x[1:-1] = data[1:-1] - dual[:-1] + dual[1:]
        x[-1] = data[-1] - dual[-1]
        dual[:] = np.clip(dual + 0.24 * (x[1:] - x[:-1]), -lam, lam)
    x[0] = data[0] + dual[0]
    x[1:-1] = data[1:-1] - dual[:-1] + dual[1:]
    x[-1] = data[-1] - dual[-1]
    return x


def robust_adaptive_kalman(data, measurement_noise=4.0, process_noise=0.05, adaptation=0.03, huber_k=2.5):
    result = np.zeros_like(data)
    state = data[0]
    covariance = measurement_noise
    adaptive_r = measurement_noise
    result[0] = state
    for i in range(1, len(data)):
        predicted_covariance = covariance + process_noise
        innovation = data[i] - state
        innovation_variance = max(1e-12, predicted_covariance + adaptive_r)
        normalized = abs(innovation) / math.sqrt(innovation_variance)
        weight = 1.0 if normalized <= huber_k else huber_k / normalized
        effective_r = adaptive_r / max(1e-12, weight * weight)
        gain = predicted_covariance / (predicted_covariance + effective_r)
        state += gain * innovation
        covariance = max(1e-12, (1.0 - gain) * predicted_covariance)
        robust_innovation = weight * innovation
        candidate = max(1e-12, robust_innovation * robust_innovation - predicted_covariance)
        candidate = max(adaptive_r / 25.0, min(adaptive_r * 25.0, candidate))
        adaptive_r = (1.0 - adaptation) * adaptive_r + adaptation * candidate
        result[i] = state
    return result


def metrics(name, output, clean, spike_mask):
    error = output - clean
    return {
        "filter": name,
        "rmse": float(np.sqrt(np.mean(error ** 2))),
        "mae": float(np.mean(np.abs(error))),
        "spike_rmse": float(np.sqrt(np.mean(error[spike_mask] ** 2))),
        "non_spike_rmse": float(np.sqrt(np.mean(error[~spike_mask] ** 2))),
    }


def svg_chart(rows, path):
    width, height = 960, 460
    left, top, bottom = 70, 45, 145
    plot_h = height - top - bottom
    plot_w = width - left - 25
    max_v = max(row["rmse"] for row in rows) * 1.12
    slot = plot_w / len(rows)
    bar_w = slot * 0.64
    esc = lambda s: s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
        '<rect width="100%" height="100%" fill="white"/>',
        '<style>text{font-family:Arial,sans-serif;fill:#222}.grid{stroke:#ddd;stroke-width:1}.axis{stroke:#333;stroke-width:1.2}.bar{fill:#4c78a8}</style>',
        '<text x="480" y="25" text-anchor="middle" font-size="18" font-weight="700">Recent denoising branch — deterministic benchmark</text>'
    ]
    for tick in range(0, 6):
        value = max_v * tick / 5
        y = top + plot_h - (value / max_v) * plot_h
        parts.append(f'<line class="grid" x1="{left}" y1="{y:.1f}" x2="{left+plot_w}" y2="{y:.1f}"/>')
        parts.append(f'<text x="{left-10}" y="{y+4:.1f}" text-anchor="end" font-size="12">{value:.1f}</text>')
    parts.append(f'<line class="axis" x1="{left}" y1="{top}" x2="{left}" y2="{top+plot_h}"/>')
    parts.append(f'<line class="axis" x1="{left}" y1="{top+plot_h}" x2="{left+plot_w}" y2="{top+plot_h}"/>')
    for i, row in enumerate(rows):
        x = left + i * slot + (slot - bar_w) / 2
        h = row["rmse"] / max_v * plot_h
        y = top + plot_h - h
        cx = x + bar_w / 2
        parts.append(f'<rect class="bar" x="{x:.1f}" y="{y:.1f}" width="{bar_w:.1f}" height="{h:.1f}" rx="2"/>')
        parts.append(f'<text x="{cx:.1f}" y="{y-7:.1f}" text-anchor="middle" font-size="12" font-weight="700">{row["rmse"]:.2f}</text>')
        parts.append(f'<text x="{cx:.1f}" y="{top+plot_h+18}" transform="rotate(35 {cx:.1f} {top+plot_h+18})" font-size="11">{esc(row["filter"])}</text>')
    parts.append('<text x="20" y="180" transform="rotate(-90 20 180)" font-size="13">RMSE (lower is better)</text>')
    parts.append('</svg>')
    path.write_text('\n'.join(parts), encoding='utf-8')


def main():
    rng = np.random.default_rng(SEED)
    n, sample_rate = 600, 50.0
    t = np.arange(n) / sample_rate
    clean = 3.0 * np.sin(2 * np.pi * 0.42 * t) + 1.2 * np.sin(2 * np.pi * 1.25 * t + 0.5)
    clean += np.where(t >= 5.0, 2.5, 0.0) + 0.07 * t
    noisy = clean + rng.normal(0.0, 1.6, n)
    spike_indexes = rng.choice(np.arange(15, n - 15), size=22, replace=False)
    noisy[spike_indexes] += rng.choice([-1, 1], size=len(spike_indexes)) * rng.uniform(6, 11, len(spike_indexes))
    spike_mask = np.zeros(n, dtype=bool)
    for index in spike_indexes:
        spike_mask[max(0, index - 1):min(n, index + 2)] = True

    outputs = {
        "Raw": noisy,
        "Savitzky-Golay": savitzky_golay(noisy),
        "Wavelet Haar": wavelet_haar(noisy),
        "SSA rank 6": ssa(noisy),
        "Reweighted SVD": reweighted_svd(noisy),
        "TV": total_variation(noisy),
        "Robust adaptive Kalman": robust_adaptive_kalman(noisy),
    }
    rows = [metrics(name, output, clean, spike_mask) for name, output in outputs.items()]
    rows.sort(key=lambda row: row["rmse"])

    csv_path = OUT / "recent_benchmark.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["filter", "rmse", "mae", "spike_rmse", "non_spike_rmse"])
        writer.writeheader()
        for row in rows:
            writer.writerow({key: (f"{value:.6f}" if isinstance(value, float) else value) for key, value in row.items()})
    svg_chart(rows, OUT / "recent_benchmark.svg")
    for row in rows:
        print(f'{row["filter"]:24s} RMSE={row["rmse"]:.4f} MAE={row["mae"]:.4f}')


if __name__ == "__main__":
    main()
