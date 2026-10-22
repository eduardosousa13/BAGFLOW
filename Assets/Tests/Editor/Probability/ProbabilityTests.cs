using System;
using NUnit.Framework;

namespace AirportBaggageSimulator.Tests
{
    public sealed class ProbabilityTests
    {
        private const int Samples = 100000;
        private static IDistributionSampler Sampler(RandomStream stream = RandomStream.Transport, int seed = 42)
        { return new DeterministicRandomStreamProvider(seed).Get(stream); }

        // Five standard errors for the mean. Variance bound uses the fourth central moment:
        // asymptotic SE(s²) = sqrt((mu4 - variance²)/n); n=100000 (or 10000 lots).
        private static void Moments(Func<double> draw, int n, double mean, double variance, double fourthMoment)
        {
            double observedMean = 0, m2 = 0;
            for (int i = 1; i <= n; i++)
            {
                double x = draw();
                Assert.That(double.IsNaN(x) || double.IsInfinity(x), Is.False);
                double delta = x - observedMean;
                observedMean += delta / i;
                m2 += delta * (x - observedMean);
            }
            double observedVariance = m2 / (n - 1);
            TestContext.WriteLine("n={0}; mean={1:R} (theory {2:R}); variance={3:R} (theory {4:R})",
                n, observedMean, mean, observedVariance, variance);
            Assert.That(observedMean, Is.EqualTo(mean).Within(5 * Math.Sqrt(variance / n)));
            Assert.That(observedVariance, Is.EqualTo(variance).Within(5 * Math.Sqrt((fourthMoment - variance * variance) / n)));
        }

        [Test] public void TriangularBoundsAndMoments()
        {
            var s = Sampler();
            double variance = 37.0 / 18;
            Moments(() => { double x = s.Triangular(6, 9, 13); Assert.That(x, Is.InRange(6, 13)); return x; },
                Samples, 28.0 / 3, variance, 2.4 * variance * variance);
        }
        [TestCase(6, 6, 13)] [TestCase(6, 13, 13)] [TestCase(6, 6, 6)]
        public void TriangularDegenerateModes(double a, double c, double b)
        {
            var s = Sampler();
            for (int i = 0; i < 10000; i++) Assert.That(s.Triangular(a, c, b), Is.InRange(a, b));
        }
        [Test] public void TriangularRejectsInvalidParameters()
        {
            var s = Sampler();
            Assert.Throws<ArgumentException>(() => s.Triangular(2, 1, 3));
            Assert.Throws<ArgumentException>(() => s.Triangular(1, 4, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Triangular(0, double.NaN, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Triangular(0, 1, double.PositiveInfinity));
        }
        [Test] public void ExponentialBoundsAndMoments()
        {
            var s = Sampler(RandomStream.Inspection);
            Moments(() => { double x = s.Exponential(0.125); Assert.That(x, Is.GreaterThan(0)); return x; }, Samples, 8, 64, 9 * 64 * 64);
        }
        [TestCase(0)] [TestCase(-1)] [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)]
        public void ExponentialRejectsInvalidRates(double rate)
        { Assert.Throws<ArgumentOutOfRangeException>(() => Sampler().Exponential(rate)); }

        [Test] public void TruncatedNormalBoundsAndMoments()
        {
            var s = Sampler(RandomStream.Weight);
            // Analytic phi/Phi formulas in MATH_MODELS, evaluated independently with Python math.erf.
            const double mean = 18.004629209507574;
            const double variance = 15.845472744238233;
            // Bounded support gives mu4 <= max(|L-E|,|H-E|)^2 * variance.
            double fourthBound = Math.Pow(32 - mean, 2) * variance;
            Moments(() => { double x = s.TruncatedNormal(18, 4, 5, 32); Assert.That(x, Is.InRange(5, 32)); return x; },
                Samples, mean, variance, fourthBound);
        }
        [Test] public void NarrowTruncationSucceedsWithoutEndpointMass()
        {
            var s = Sampler(RandomStream.Weight);
            for (int i = 0; i < 50; i++) Assert.That(s.TruncatedNormal(0, 1, -0.001, 0.001), Is.InRange(-0.001, 0.001));
        }
        [Test] public void TailTruncationKeepsConditionalNormalMoments()
        {
            var s = Sampler(RandomStream.Weight);
            const double mean = 2.3158213267437833;
            const double variance = 0.06152077957448565;
            Moments(() => { double x = s.TruncatedNormal(0, 1, 2, 3); Assert.That(x, Is.InRange(2, 3)); return x; },
                10000, mean, variance, Math.Pow(3 - mean, 2) * variance);
        }
        [Test] public void ExtremeTruncationFailsExplicitlyAndTerminates()
        { Assert.Throws<InvalidOperationException>(() => Sampler().TruncatedNormal(0, 1, 40, 41)); }
        [Test] public void NormalRejectsInvalidParameters()
        {
            var s = Sampler();
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TruncatedNormal(0, 0, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TruncatedNormal(0, -1, -1, 1));
            Assert.Throws<ArgumentException>(() => s.TruncatedNormal(0, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => s.TruncatedNormal(0, 1, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TruncatedNormal(double.NaN, 1, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TruncatedNormal(0, double.PositiveInfinity, -1, 1));
        }
        [Test] public void BinomialIsSumOfActualIndependentBernoulliTrials()
        {
            var s = Sampler(RandomStream.Flagging);
            const double variance = 60 * 0.08 * 0.92;
            double fourth = 3 * variance * variance + variance * (1 - 6 * 0.08 * 0.92);
            Moments(() => { int count = 0; for (int i = 0; i < 60; i++) if (s.Bernoulli(0.08)) count++;
                Assert.That(count, Is.InRange(0, 60)); return count; }, 10000, 4.8, variance, fourth);
        }
        [Test] public void BernoulliExtremesAndInvalidProbabilities()
        {
            var s = Sampler();
            for (int i = 0; i < 1000; i++) { Assert.That(s.Bernoulli(0), Is.False); Assert.That(s.Bernoulli(1), Is.True); }
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Bernoulli(-0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Bernoulli(1.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Bernoulli(double.NaN));
        }
        [Test] public void PoissonCountsUseOnlyIncreasingExponentialArrivalTimes()
        {
            var s = Sampler(RandomStream.Blockage);
            Moments(() =>
            {
                int count = 0;
                double time = 0;
                while (true)
                {
                    double next = time + s.Exponential(0.1);
                    Assert.That(next, Is.GreaterThan(time));
                    time = next;
                    if (time > 60) return count;
                    count++;
                }
            }, 10000, 6, 6, 3 * 6 * 6 + 6);
        }
        [Test] public void ZeroPoissonRateSchedulesNothingAndDoesNotConsumeStream()
        {
            // Scheduling belongs to M3: lambda=0 bypasses Exponential(0), which must reject zero.
            var a = Sampler(RandomStream.Blockage);
            var b = Sampler(RandomStream.Blockage);
            double rate = new SimulationConfig { BlockageRatePerSecond = 0 }.BlockageRatePerSecond;
            double next = rate == 0 ? double.PositiveInfinity : a.Exponential(rate);
            Assert.That(next, Is.EqualTo(double.PositiveInfinity));
            Assert.That(a.Exponential(0.1), Is.EqualTo(b.Exponential(0.1)));
        }
        [Test] public void SameSeedReproducesEveryTransformAndStream()
        {
            var a = new DeterministicRandomStreamProvider(-42);
            var b = new DeterministicRandomStreamProvider(-42);
            foreach (RandomStream stream in Enum.GetValues(typeof(RandomStream)))
                for (int i = 0; i < 1000; i++)
                {
                    var x = a.Get(stream); var y = b.Get(stream);
                    Assert.That(x.Triangular(6, 9, 13), Is.EqualTo(y.Triangular(6, 9, 13)));
                    Assert.That(x.Exponential(0.2), Is.EqualTo(y.Exponential(0.2)));
                    Assert.That(x.TruncatedNormal(18, 4, 5, 32), Is.EqualTo(y.TruncatedNormal(18, 4, 5, 32)));
                    Assert.That(x.Bernoulli(0.08), Is.EqualTo(y.Bernoulli(0.08)));
                }
        }
        [Test] public void ConsumingOneStreamDoesNotMoveAnyOtherStream()
        {
            foreach (RandomStream consumed in Enum.GetValues(typeof(RandomStream)))
            {
                var a = new DeterministicRandomStreamProvider(42); var b = new DeterministicRandomStreamProvider(42);
                for (int i = 0; i < 100; i++) a.Get(consumed).TruncatedNormal(0, 1, -1, 1);
                foreach (RandomStream other in Enum.GetValues(typeof(RandomStream)))
                    if (other != consumed)
                        Assert.That(a.Get(other).Exponential(1), Is.EqualTo(b.Get(other).Exponential(1)));
            }
        }
        [Test] public void StableSeedDerivationHasKnownFirstArrival()
        {
            // Independently evaluated SplitMix64 fixed seed/domain vector, protects algorithm version.
            Assert.That(Sampler(RandomStream.Arrival).Exponential(1), Is.EqualTo(1.4725407378410533).Within(1e-14));
        }
        [Test] public void SeedsAndDomainsProduceDifferentSequences()
        {
            Assert.That(Sampler(seed: 42).Exponential(1), Is.Not.EqualTo(Sampler(seed: 43).Exponential(1)));
            Assert.That(Sampler(RandomStream.Arrival).Exponential(1), Is.Not.EqualTo(Sampler(RandomStream.Blockage).Exponential(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandomStreamProvider(42).Get((RandomStream)99));
        }
        [Test] public void AuxiliaryArrivalHasItsOwnExponentialLaw()
        { var s = Sampler(RandomStream.Arrival); Moments(() => s.Exponential(0.2), Samples, 5, 25, 9 * 25 * 25); }
    }
}
