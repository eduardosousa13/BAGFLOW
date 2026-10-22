using System;

namespace AirportBaggageSimulator
{
    /// <summary>Pure C# transforms of an open uniform. Instances are owned by one logical stream.</summary>
    public sealed class DistributionSampler : IDistributionSampler
    {
        public const int MaxNormalAttempts = 100000;
        private ulong state;

        public DistributionSampler(ulong seed) { state = seed; }

        private double UniformOpen()
        {
            // SplitMix64, explicit modulo-2^64 arithmetic. Midpoints of 2^52 cells
            // give 2^-53 <= U <= 1-2^-53, so logarithms never see 0 or 1.
            ulong z;
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
            }
            return ((z >> 12) + 0.5) * (1.0 / 4503599627370496.0);
        }

        public double Triangular(double min, double mode, double max)
        {
            Finite(min, nameof(min)); Finite(mode, nameof(mode)); Finite(max, nameof(max));
            if (min > mode || mode > max) throw new ArgumentException("Expected min <= mode <= max.");
            if (min == max) return min;
            double width = max - min;
            Finite(width, "triangular width");
            double q = (mode - min) / width;
            double u = UniformOpen();
            // Equivalent inverse CDF, normalized to avoid multiplying large widths together.
            return u < q ? min + width * Math.Sqrt(u * q)
                         : max - width * Math.Sqrt((1.0 - u) * (1.0 - q));
        }

        public double Exponential(double ratePerSecond)
        {
            Positive(ratePerSecond, nameof(ratePerSecond));
            // Inverse CDF; no artificial lower duration and no rounding of raw samples.
            double value = -Math.Log(1.0 - UniformOpen()) / ratePerSecond;
            if (double.IsInfinity(value) || value == 0)
                throw new ArithmeticException("Exponential sample is outside the representable positive double range.");
            return value;
        }

        public double TruncatedNormal(double baseMean, double baseStd, double min, double max)
        {
            Finite(baseMean, nameof(baseMean)); Positive(baseStd, nameof(baseStd));
            Finite(min, nameof(min)); Finite(max, nameof(max));
            if (min >= max) throw new ArgumentException("Expected min < max.");
            for (int attempt = 0; attempt < MaxNormalAttempts; attempt++)
            {
                // Box-Muller: fresh pair per candidate, no hidden cached normal shared by domains.
                double z = Math.Sqrt(-2.0 * Math.Log(UniformOpen())) *
                           Math.Cos(2.0 * Math.PI * UniformOpen());
                double candidate = baseMean + baseStd * z;
                if (!double.IsInfinity(candidate) && candidate >= min && candidate <= max)
                    return candidate;
            }
            // A failure is explicit: never return a clamped value or silently switch models.
            throw new InvalidOperationException("Truncated normal exceeded 100000 Box-Muller rejection attempts; " +
                "the interval has too little representable probability mass. Review mu, sigma and bounds.");
        }

        public bool Bernoulli(double probability)
        {
            Finite(probability, nameof(probability));
            if (probability < 0 || probability > 1) throw new ArgumentOutOfRangeException(nameof(probability));
            return UniformOpen() < probability;
        }

        internal static void Finite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "Must be finite.");
        }

        private static void Positive(double value, string name)
        {
            Finite(value, name);
            if (value <= 0) throw new ArgumentOutOfRangeException(name, "Must be positive.");
        }
    }
}
