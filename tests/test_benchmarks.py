import importlib.util
import unittest
from pathlib import Path

import numpy as np


SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "generate_benchmarks.py"
SPEC = importlib.util.spec_from_file_location("generate_benchmarks", SCRIPT)
BENCH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BENCH)


class FilterReferenceTests(unittest.TestCase):
    def setUp(self):
        self.clean, self.noisy = BENCH.make_signal()

    def test_all_filters_preserve_sample_count_and_return_finite_values(self):
        for name, function in BENCH.FILTERS.items():
            with self.subTest(filter=name):
                result = function(self.noisy)
                self.assertEqual(len(result), len(self.noisy))
                self.assertTrue(np.isfinite(result).all())

    def test_fixed_seed_is_reproducible(self):
        clean_again, noisy_again = BENCH.make_signal()
        np.testing.assert_array_equal(self.clean, clean_again)
        np.testing.assert_array_equal(self.noisy, noisy_again)

    def test_median_rejects_isolated_impulse(self):
        signal = np.full(9, 10.0)
        signal[len(signal) // 2] = 100.0
        result = BENCH.centered_median(signal, window=5)
        self.assertEqual(result[len(signal) // 2], 10.0)

    def test_deadband_holds_small_changes(self):
        result = BENCH.deadband(np.array([10.0, 10.4, 10.8, 12.1]), limit=2.0)
        np.testing.assert_allclose(result, [10.0, 10.0, 10.0, 12.1])

    def test_savitzky_golay_preserves_quadratic_interior(self):
        x = np.arange(-20, 21, dtype=float)
        quadratic = 2.0 + 0.5 * x + 0.1 * x * x
        result = BENCH.savitzky_golay(quadratic, window=9, degree=2)
        np.testing.assert_allclose(result[4:-4], quadratic[4:-4], atol=1e-10)


if __name__ == "__main__":
    unittest.main()
