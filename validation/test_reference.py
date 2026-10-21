"""Run with: python -m unittest discover -s validation -p 'test_reference.py' -v"""
import math
import random
import statistics
import unittest
from reference_samplers import triangular, exponential, truncated_normal, bernoulli, binomial_batch, poisson_events

class ReferenceDistributionTests(unittest.TestCase):
    def test_triangular_mean_variance(self):
        rng=random.Random(42)
        x=[triangular(rng,6,9,13) for _ in range(50_000)]
        self.assertTrue(all(6<=v<=13 for v in x))
        self.assertAlmostEqual(statistics.mean(x),28/3,delta=0.035)
        self.assertAlmostEqual(statistics.pvariance(x),37/18,delta=0.065)

    def test_exponential_mean(self):
        rng=random.Random(42)
        x=[exponential(rng,1/8) for _ in range(50_000)]
        self.assertTrue(all(v>=0 for v in x))
        self.assertAlmostEqual(statistics.mean(x),8.0,delta=0.15)

    def test_truncated_normal_limits(self):
        rng=random.Random(42)
        x=[truncated_normal(rng,18,4,5,32) for _ in range(50_000)]
        self.assertTrue(all(5<=v<=32 for v in x))
        self.assertAlmostEqual(statistics.mean(x),18.0,delta=0.10)

    def test_binomial_batch(self):
        rng=random.Random(42)
        x=[binomial_batch(rng,60,.08) for _ in range(10000)]
        self.assertAlmostEqual(statistics.mean(x),4.8,delta=0.13)
        self.assertAlmostEqual(statistics.pvariance(x),4.416,delta=0.25)
        self.assertEqual(binomial_batch(rng,60,0),0)
        self.assertEqual(binomial_batch(rng,60,1),60)

    def test_poisson_fixed_window(self):
        rng=random.Random(42)
        x=[len(poisson_events(rng,1/120,120)) for _ in range(10000)]
        self.assertAlmostEqual(statistics.mean(x),1.0,delta=0.04)
        self.assertAlmostEqual(statistics.pvariance(x),1.0,delta=0.09)
        self.assertEqual(poisson_events(rng,0,120),[])

    def test_edge_parameters(self):
        rng=random.Random(99)
        self.assertEqual(triangular(rng,5,5,5),5)
        for _ in range(100):
            self.assertTrue(1<=triangular(rng,1,1,4)<=4)
            self.assertTrue(1<=triangular(rng,1,4,4)<=4)
        for invalid in [0,-1,float('inf'),float('nan')]:
            with self.assertRaises(ValueError):
                exponential(rng,invalid)
        with self.assertRaises(ValueError):
            truncated_normal(rng,0,0,-1,1)
        with self.assertRaises(ValueError):
            truncated_normal(rng,0,1,2,2)
        with self.assertRaises(ValueError):
            bernoulli(rng,1.1)

    def test_repeatable_random_streams(self):
        def sequence(seed):
            rng=random.Random(seed)
            return [triangular(rng,6,9,13),exponential(rng,1/8),
                    truncated_normal(rng,18,4,5,32),bernoulli(rng,.08),
                    len(poisson_events(rng,1/120,120))]
        self.assertEqual(sequence(42),sequence(42))

if __name__ == '__main__':
    unittest.main()
