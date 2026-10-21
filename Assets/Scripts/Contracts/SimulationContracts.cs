using System;
using System.Collections.Generic;

namespace AirportBaggageSimulator
{
    // Central contracts. Changes require coordinator approval.
    public enum ArrivalMode { Constant, Exponential }
    public enum BagState { Scheduled, WaitingEntry, Transporting, ScannerQueue, Scanning, Approved, Flagged }
    public enum RandomStream { Arrival, Transport, Weight, Inspection, Flagging, Blockage }
    public enum SimulationEventType
    {
        BagArrived, TransportStarted, TransportFinished, ScanQueued, ScanStarted,
        ScanFinished, BagApproved, BagFlagged, BlockageStarted,
        BlockageOccurrence, BlockageEnded, RunCompleted
    }

    [Serializable]
    public sealed class SimulationConfig
    {
        public int BaggageCount = 60;
        public int Seed = 42;
        public ArrivalMode ArrivalMode = ArrivalMode.Constant;
        public double MeanArrivalIntervalSeconds = 5.0;
        public double TransportMinSeconds = 6.0;
        public double TransportModeSeconds = 9.0;
        public double TransportMaxSeconds = 13.0;
        public double MeanInspectionSeconds = 8.0;
        public double WeightBaseMeanKg = 18.0;
        public double WeightBaseStdKg = 4.0;
        public double MinWeightKg = 5.0;
        public double MaxWeightKg = 32.0;
        public double HeavyWeightThresholdKg = 23.0;
        public double FlagProbability = 0.08;
        public double BlockageRatePerSecond = 1.0 / 120.0;
        public double BlockageDurationSeconds = 8.0;
        public double SimulationStepSeconds = 0.1;
        public double PlaybackSpeed = 10.0;
        public double MaxSimulatedSeconds = 10800.0;
    }

    [Serializable]
    public sealed class BagRecord
    {
        public int Id;
        public BagState State;
        public double WeightKg;
        public double NominalTransportSeconds;
        public double ObservedTransportSeconds;
        // Presentation-only values calculated by the engine, never by the view.
        public double TransportProgress01;   // 0..1 while Transporting; 1 after transport
        public double ScanProgress01;        // 0..1 while Scanning; 1 after scanning
        public int QueuePosition = -1;       // 0-based when ScannerQueue, -1 otherwise
        public double InspectionSeconds;
        public double ArrivalTimeSeconds;
        public double? TransportStartTimeSeconds;
        public double? QueueEnterTimeSeconds;
        public double? ScanStartTimeSeconds;
        public double? CompletionTimeSeconds;
        public bool? IsFlagged;
    }

    [Serializable]
    public sealed class SimulationEventRecord
    {
        public SimulationEventType EventType;
        public double SimulationTimeSeconds;
        public double? ScheduledTimeSeconds;
        public int? BagId;
        public string Detail;
    }

    public sealed class SimulationMetrics
    {
        public int Created;
        public int Completed;
        public int Approved;
        public int Flagged;
        public int WaitingEntry;
        public int OnBelt;
        public int QueueLength;
        public int Scanning;
        public int BlockageOccurrences;
        public double MeanQueueWaitSeconds;
        public double MeanTotalSeconds;
        public double ScannerUtilization;
        public double TotalBlockedSeconds;
    }

    public sealed class SimulationSnapshot
    {
        public double SimulationTimeSeconds;
        // Engine must provide detached BagRecord copies; UI must not mutate domain state.
        public bool BeltBlocked;
        public bool IsComplete;
        public SimulationMetrics Metrics;
        public IReadOnlyList<BagRecord> Bags;
    }

    public interface IDistributionSampler
    {
        double Triangular(double min, double mode, double max);
        double Exponential(double ratePerSecond);
        double TruncatedNormal(double baseMean, double baseStd, double min, double max);
        bool Bernoulli(double probability);
    }

    public interface IRandomStreamProvider
    {
        IDistributionSampler Get(RandomStream stream);
    }

    // These interfaces contain no UnityEngine dependency, but may be implemented
    // by MonoBehaviours in the UI/Presentation layer.
    public interface ISimulationWorldView
    {
        void Render(SimulationSnapshot snapshot);
        void ResetView();
    }

    public interface ISimulationDashboardView
    {
        void Render(SimulationSnapshot snapshot);
        void ShowError(string message);
    }

    public interface ISimulationControlView
    {
        event Action<SimulationConfig> StartRequested;
        event Action PauseRequested;
        event Action ResumeRequested;
        event Action ResetRequested;
        event Action<double> PlaybackSpeedRequested;
        event Action ExportRequested;
        void SetRunning(bool running);
        void ShowValidationError(string message);
    }

    public interface ISimulationEngine
    {
        void Initialize(SimulationConfig config, IRandomStreamProvider samplers);
        void Step();
        SimulationSnapshot GetSnapshot();
        IReadOnlyList<BagRecord> GetBagRecords();
        IReadOnlyList<SimulationEventRecord> GetEventRecords();
        bool IsComplete { get; }
    }
}
