using System;

namespace AirportBaggageSimulator
{
    // Validation at the domain boundary; no dependency on the Unity integration assembly.
    internal static class EngineConfiguration
    {
        internal static SimulationConfig Copy(SimulationConfig source)
        {
            var copy = new SimulationConfig();
            // Frozen contract has scalar fields only; isolate caller edits without changing its API.
            foreach (var field in typeof(SimulationConfig).GetFields()) field.SetValue(copy, field.GetValue(source));
            return copy;
        }
        internal static void Validate(SimulationConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            foreach (var field in typeof(SimulationConfig).GetFields())
                if (field.FieldType == typeof(double)) Require(Finite((double)field.GetValue(c)), field.Name + " must be finite.");
            Require(c.BaggageCount >= 1 && c.BaggageCount <= 500, "BaggageCount must be in [1,500].");
            Require(c.ArrivalMode == ArrivalMode.Constant || c.ArrivalMode == ArrivalMode.Exponential, "Invalid arrival mode.");
            Require(c.MeanArrivalIntervalSeconds > 0 && Finite(1 / c.MeanArrivalIntervalSeconds), "Invalid arrival mean/rate.");
            Require(c.MeanInspectionSeconds > 0 && Finite(1 / c.MeanInspectionSeconds), "Invalid inspection mean/rate.");
            Require(c.TransportMinSeconds >= 0 && c.TransportMinSeconds <= c.TransportModeSeconds &&
                c.TransportModeSeconds <= c.TransportMaxSeconds, "Require 0 <= transport min <= mode <= max.");
            Require(c.WeightBaseStdKg > 0 && c.MinWeightKg >= 0 && c.MinWeightKg < c.MaxWeightKg, "Invalid weight parameters.");
            Require(c.HeavyWeightThresholdKg >= 0, "Weight threshold must be nonnegative.");
            Require(c.FlagProbability >= 0 && c.FlagProbability <= 1, "Probability must be in [0,1].");
            Require(c.BlockageRatePerSecond >= 0 && c.BlockageDurationSeconds >= 0, "Invalid blockage parameters.");
            Require(c.SimulationStepSeconds > 0 && c.MaxSimulatedSeconds >= c.SimulationStepSeconds, "Require 0 < step <= horizon.");
            Require(c.PlaybackSpeed == 1 || c.PlaybackSpeed == 5 || c.PlaybackSpeed == 10 || c.PlaybackSpeed == 20,
                "Invalid playback speed.");
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    }
}
