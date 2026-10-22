using System;

namespace AirportBaggageSimulator
{
    public static class SimulationConfiguration
    {
        public static bool IsPlaybackSpeed(double speed)
        {
            return speed == 1 || speed == 5 || speed == 10 || speed == 20;
        }

        public static void Validate(SimulationConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            // All current config fields are scalar values; check every double, including visual fields.
            foreach (var field in typeof(SimulationConfig).GetFields())
                if (field.FieldType == typeof(double))
                {
                    double value = (double)field.GetValue(c);
                    Require(!double.IsNaN(value) && !double.IsInfinity(value), field.Name + " must be finite.");
                }
            Require(c.BaggageCount >= 1 && c.BaggageCount <= 500, "BaggageCount must be in [1,500].");
            Require(c.ArrivalMode == ArrivalMode.Constant || c.ArrivalMode == ArrivalMode.Exponential, "Invalid ArrivalMode.");
            Require(c.MeanArrivalIntervalSeconds > 0 && IsPositiveFinite(1 / c.MeanArrivalIntervalSeconds), "Arrival interval/rate must be positive and representable.");
            Require(c.MeanInspectionSeconds > 0 && IsPositiveFinite(1 / c.MeanInspectionSeconds), "Inspection mean/rate must be positive and representable.");
            Require(c.TransportMinSeconds >= 0 && c.TransportMinSeconds <= c.TransportModeSeconds &&
                c.TransportModeSeconds <= c.TransportMaxSeconds, "Transport requires 0 <= min <= mode <= max.");
            Require(c.WeightBaseStdKg > 0 && c.MinWeightKg >= 0 && c.MinWeightKg < c.MaxWeightKg,
                "Weight requires sigma > 0 and 0 <= min < max.");
            Require(c.HeavyWeightThresholdKg >= 0, "Weight threshold must be nonnegative.");
            Require(c.FlagProbability >= 0 && c.FlagProbability <= 1, "FlagProbability must be in [0,1].");
            Require(c.BlockageRatePerSecond >= 0 && c.BlockageDurationSeconds >= 0, "Blockage rate and duration must be nonnegative.");
            Require(c.SimulationStepSeconds > 0 && c.MaxSimulatedSeconds >= c.SimulationStepSeconds,
                "Require 0 < step <= maximum simulated time.");
            Require(c.MaxSimulatedSeconds / c.SimulationStepSeconds < long.MaxValue,
                "The configured tick budget exceeds the representable limit.");
            Require(IsPlaybackSpeed(c.PlaybackSpeed), "Playback speed must be 1, 5, 10 or 20.");
        }

        public static SimulationConfig Copy(SimulationConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new SimulationConfig();
            // The frozen contract consists entirely of public scalar fields.
            foreach (var field in typeof(SimulationConfig).GetFields()) field.SetValue(copy, field.GetValue(source));
            return copy;
        }

        private static bool IsPositiveFinite(double value) { return value > 0 && !double.IsInfinity(value); }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new ArgumentException(message);
        }
    }
}
