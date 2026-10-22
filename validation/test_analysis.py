"""Tests for bagflow_analysis.py and for the committed Unity exports.

Run from the repository root:  python -m unittest discover -s validation -p "test_*.py" -v
"""
import json
import math
import os
import shutil
import sys
import tempfile
import unittest

import numpy as np
import pandas as pd
from scipy import integrate, stats

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bagflow_analysis as ba  # noqa: E402

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data", "unity")
HAS_DATA = os.path.isdir(os.path.join(DATA, "example_seed42_n60"))


class TheoryFormulas(unittest.TestCase):
    def test_triangular_matches_formulas_and_scipy(self):
        law = ba.Triangular(6, 9, 13)
        self.assertAlmostEqual(integrate.quad(law.pdf, 5, 14, points=[6, 9, 13])[0], 1, places=10)
        ref = stats.triang((9 - 6) / (13 - 6), loc=6, scale=7)
        xs = np.linspace(5, 14, 301)
        np.testing.assert_allclose(law.pdf(xs), ref.pdf(xs), atol=1e-12)
        np.testing.assert_allclose(law.cdf(xs), ref.cdf(xs), atol=1e-12)
        self.assertAlmostEqual(law.mean, ref.mean(), places=12)
        self.assertAlmostEqual(law.var, ref.var(), places=12)
        self.assertAlmostEqual(law.mean, 28 / 3)
        with self.assertRaises(ValueError):
            ba.Triangular(6, 6, 13)

    def test_exponential(self):
        law = ba.Exponential(1 / 8)
        self.assertAlmostEqual(integrate.quad(law.pdf, 0, np.inf)[0], 1, places=10)
        self.assertEqual(law.mean, 8)
        self.assertEqual(law.var, 64)
        self.assertEqual(float(law.cdf(-1)), 0.0)
        np.testing.assert_allclose(law.cdf([0, 8, 80]), stats.expon(scale=8).cdf([0, 8, 80]))

    def test_truncated_normal_is_not_the_plain_normal(self):
        law = ba.TruncatedNormal(18, 4, 5, 32)
        ref = stats.truncnorm((5 - 18) / 4, (32 - 18) / 4, loc=18, scale=4)
        xs = np.linspace(0, 40, 401)
        np.testing.assert_allclose(law.pdf(xs), ref.pdf(xs), rtol=1e-10, atol=1e-14)
        np.testing.assert_allclose(law.cdf(xs), ref.cdf(xs), atol=1e-12)
        self.assertAlmostEqual(law.mean, ref.mean(), places=10)
        self.assertAlmostEqual(law.var, ref.var(), places=10)
        self.assertAlmostEqual(integrate.quad(law.pdf, 5, 32)[0], 1, places=10)
        self.assertLess(law.var, 16)  # truncation reduces variance below sigma^2

    def test_truncated_normal_far_tail_is_stable(self):
        law = ba.TruncatedNormal(0, 1, 30, 31)
        self.assertGreater(law.z, 0)
        ref = stats.truncnorm(30, 31)
        self.assertAlmostEqual(law.mean, ref.mean(), places=6)
        self.assertAlmostEqual(float(law.cdf(31)), 1.0)

    def test_discrete_pmfs_sum_to_one(self):
        self.assertAlmostEqual(ba.binomial_pmf(np.arange(61), 60, 0.08).sum(), 1, places=12)
        self.assertAlmostEqual(ba.poisson_pmf(np.arange(200), 1.0).sum(), 1, places=12)


class EmpiricalTools(unittest.TestCase):
    def test_density_histogram_is_normalised(self):
        x = np.random.default_rng(1).exponential(8, 5000)
        density, edges = ba.density_histogram(x, bins=37)
        self.assertAlmostEqual(float((density * np.diff(edges)).sum()), 1.0, places=12)

    def test_ecdf_and_moments(self):
        x = np.array([3.0, 1.0, 2.0, 2.0])
        xs, f = ba.ecdf(x)
        np.testing.assert_array_equal(xs, [1, 2, 2, 3])
        np.testing.assert_array_equal(f, [0.25, 0.5, 0.75, 1])
        m = ba.compare_moments("x", x, 2, 0.5)
        self.assertEqual(m.sample_mean, 2)
        self.assertAlmostEqual(m.sample_var, np.var(x, ddof=1))
        self.assertEqual(m.z_mean, 0)

    def test_chi_square_accepts_true_law_and_rejects_wrong_law(self):
        counts = np.random.default_rng(3).binomial(60, 0.08, 3000)
        _, _, p_ok = ba.chi_square_discrete(counts, lambda k: ba.binomial_pmf(k, 60, 0.08), 60)
        _, _, p_bad = ba.chi_square_discrete(counts, lambda k: ba.binomial_pmf(k, 60, 0.12), 60)
        self.assertGreater(p_ok, 1e-3)
        self.assertLess(p_bad, 1e-6)


class SimulationDerived(unittest.TestCase):
    def runs(self, seeds, ends, n=60, p=0.08, flagged=None):
        return pd.DataFrame({"seed": seeds, "n": n, "flag_probability": p, "completed": n,
                             "end_time_s": ends, "flagged": flagged if flagged is not None else [0] * len(seeds)})

    def test_poisson_windows_count_overlaps_and_respect_boundaries(self):
        blockages = pd.DataFrame({"seed": [1, 1, 1, 1, 2, 2], "occurrence": [0, 1, 2, 3, 0, 1],
                                  "scheduled_time_s": [10.0, 12.0, 120.0, 250.0, 119.999, 239.0],
                                  "time_s": [0, 0, 0, 0, 0, 0]})
        counts = ba.poisson_window_counts(blockages, self.runs([1, 2, 3], [300, 300, 300]), 120, 240)
        # seed 1: [0,120) -> 10, 12 (two overlapping stoppages still count twice); [120,240) -> 120; 250 outside horizon
        np.testing.assert_array_equal(counts, [2, 1, 1, 1, 0, 0])
        with self.assertRaises(ValueError):
            ba.poisson_window_counts(blockages, self.runs([1], [200]), 120, 240)

    def test_binomial_counts_need_constant_n_and_p(self):
        counts, n, p = ba.binomial_counts(self.runs([1, 2], [1, 1], flagged=[3, 5]))
        np.testing.assert_array_equal(counts, [3, 5])
        self.assertEqual((n, p), (60, 0.08))
        mixed = self.runs([1, 2], [1, 1])
        mixed.loc[1, "n"] = 61
        with self.assertRaises(ValueError):
            ba.binomial_counts(mixed)

    def test_queue_length_reconstruction(self):
        events = pd.DataFrame({"time_s": [1.0, 2.0, 2.0, 3.0, 5.0], "event_type":
                               ["ScanQueued", "ScanQueued", "ScanStarted", "ScanStarted", "BagApproved"]})
        times, lengths = ba.queue_length_series(events)
        np.testing.assert_array_equal(times, [0, 1, 2, 3])
        np.testing.assert_array_equal(lengths, [0, 1, 1, 0])
        self.assertAlmostEqual(ba.time_average(times, lengths, 4.0), 0.5)

    def test_loader_rejects_wrong_header_and_parses_missing_values(self):
        folder = tempfile.mkdtemp()
        try:
            src = os.path.join(DATA, "example_seed42_n60")
            if HAS_DATA:
                for name in ("bags.csv", "events.csv", "config.json"):
                    shutil.copy(os.path.join(src, name), folder)
                with open(os.path.join(folder, "bags.csv"), "a", encoding="utf-8") as handle:
                    handle.write("999,10.5,8,,,7,,,,,,\n")  # bag that never left the entry
                export = ba.load_export(folder)
                last = export.bags.iloc[-1]
                self.assertTrue(math.isnan(export.bags["inspection_s"].iloc[-1]))
                self.assertTrue(pd.isna(last["flagged"]))
            with open(os.path.join(folder, "bags.csv"), "w", encoding="utf-8") as handle:
                handle.write("id;weight_kg\n1;2,5\n")
            with self.assertRaises(ba.ExportFormatError):
                ba.read_checked_csv(os.path.join(folder, "bags.csv"), ba.BAG_COLUMNS)
        finally:
            shutil.rmtree(folder)


@unittest.skipUnless(HAS_DATA, "Unity exports not generated (see docs/M5_VALIDATION_EXPORT.md)")
class UnityExports(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.example = ba.load_export(os.path.join(DATA, "example_seed42_n60"))
        cls.samples = ba.load_batch(os.path.join(DATA, "samples_n500"))
        cls.binomial = ba.load_batch(os.path.join(DATA, "binomial_n60"), with_bags=False)

    def test_single_export_is_consistent(self):
        self.assertEqual(ba.check_export(self.example), [])
        self.assertEqual(self.example.config["seed"], 42)
        self.assertEqual(self.example.parameters["BaggageCount"], 60)
        self.assertEqual(len(self.example.bags), 60)
        self.assertEqual(self.example.config["prng_algorithm"], "SplitMix64-open52-v1")

    def test_raw_samples_follow_the_approved_laws(self):
        p = self.samples.template
        bags = self.samples.bags
        self.assertGreaterEqual(len(bags), 10000)
        cases = [
            (bags.nominal_transport_s, ba.Triangular(p["TransportMinSeconds"], p["TransportModeSeconds"], p["TransportMaxSeconds"])),
            (bags.inspection_s, ba.Exponential(1 / p["MeanInspectionSeconds"])),
            (bags.weight_kg, ba.TruncatedNormal(p["WeightBaseMeanKg"], p["WeightBaseStdKg"], p["MinWeightKg"], p["MaxWeightKg"])),
        ]
        for x, law in cases:
            m = ba.compare_moments("x", x, law.mean, law.var)
            self.assertLess(abs(m.z_mean), 4.5, law)
            self.assertLess(abs(m.z_var), 4.5, law)
            self.assertGreater(ba.ks_test(x, law.cdf).pvalue, 1e-4, law)

    def test_binomial_and_poisson_from_runs(self):
        counts, n, p = ba.binomial_counts(self.binomial.runs)
        self.assertEqual(len(counts), 2000)
        se = math.sqrt(n * p * (1 - p) / len(counts))
        self.assertLess(abs(counts.mean() - n * p), 4.5 * se)
        rate = self.binomial.template["BlockageRatePerSecond"]
        windows = ba.poisson_window_counts(self.binomial.blockages, self.binomial.runs, 120, 240)
        self.assertEqual(len(windows), 4000)
        self.assertLess(abs(windows.mean() - rate * 120), 4.5 * math.sqrt(rate * 120 / len(windows)))

    def test_analysis_is_reproducible(self):
        again = ba.load_batch(os.path.join(DATA, "binomial_n60"), with_bags=False)
        a = ba.poisson_window_counts(self.binomial.blockages, self.binomial.runs, 120, 240)
        b = ba.poisson_window_counts(again.blockages, again.runs, 120, 240)
        np.testing.assert_array_equal(a, b)
        with open(os.path.join(DATA, "binomial_n60", "batch.json"), encoding="utf-8") as handle:
            self.assertEqual(json.load(handle)["runs"], 2000)


if __name__ == "__main__":
    unittest.main()
