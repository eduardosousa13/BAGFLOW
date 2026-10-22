using System;
using System.Collections.Generic;

namespace AirportBaggageSimulator
{
    /// <summary>Fixed-step domain engine. Owns all operational state; receives randomness only via contracts.</summary>
    public sealed class SimulationEngine : ISimulationEngine
    {
        public const int MaxCalendarEventsPerStep = 10000;
        public const int MaxRecordedEvents = 1000000;
        public const long MaxSteps = 10000000;
        private SimulationConfig config;
        private IRandomStreamProvider samplers;
        private readonly List<BagRecord> bags = new List<BagRecord>();
        private readonly List<SimulationEventRecord> events = new List<SimulationEventRecord>();
        private readonly Queue<int> waitingEntry = new Queue<int>();
        private readonly Queue<int> scannerQueue = new Queue<int>();
        private double[] transportWork;
        private double scanWork, time, nextBlockage, blockEnd, blockedSeconds, busySeconds;
        private int nextArrival, scanning = -1, completed, occurrences;
        private long steps;
        private bool initialized, faulted, blocked;
        public bool IsComplete { get; private set; }

        public void Initialize(SimulationConfig configuration, IRandomStreamProvider randomStreams)
        {
            EngineConfiguration.Validate(configuration);
            if (randomStreams == null) throw new ArgumentNullException(nameof(randomStreams));
            initialized = false;
            faulted = false;
            config = EngineConfiguration.Copy(configuration);
            samplers = randomStreams;
            bags.Clear(); events.Clear(); waitingEntry.Clear(); scannerQueue.Clear();
            time = scanWork = blockedSeconds = busySeconds = 0;
            nextArrival = completed = occurrences = 0;
            steps = 0; scanning = -1; blocked = IsComplete = false;
            blockEnd = double.PositiveInfinity;
            transportWork = new double[config.BaggageCount];
            // Attributes are drawn once at creation, including records scheduled for future arrivals.
            double arrival = 0;
            for (int i = 0; i < config.BaggageCount; i++)
            {
                if (i > 0)
                {
                    if (config.ArrivalMode == ArrivalMode.Constant)
                    {
                        double next = i * config.MeanArrivalIntervalSeconds;
                        if (!Finite(next) || next <= arrival) throw new InvalidOperationException("Arrival schedule is not representable.");
                        arrival = next;
                    }
                    else arrival = AddInterval(arrival, DrawExponential(RandomStream.Arrival, 1 / config.MeanArrivalIntervalSeconds));
                }
                double weight = Sampler(RandomStream.Weight).TruncatedNormal(config.WeightBaseMeanKg,
                    config.WeightBaseStdKg, config.MinWeightKg, config.MaxWeightKg);
                double transport = Sampler(RandomStream.Transport).Triangular(config.TransportMinSeconds,
                    config.TransportModeSeconds, config.TransportMaxSeconds);
                RequireSample(weight, config.MinWeightKg, config.MaxWeightKg, "weight");
                RequireSample(transport, config.TransportMinSeconds, config.TransportMaxSeconds, "transport");
                bags.Add(new BagRecord { Id = i + 1, State = BagState.Scheduled, WeightKg = weight,
                    NominalTransportSeconds = transport, ArrivalTimeSeconds = arrival });
            }
            nextBlockage = config.BlockageRatePerSecond == 0 ? double.PositiveInfinity :
                AddInterval(0, DrawExponential(RandomStream.Blockage, config.BlockageRatePerSecond));
            ProcessCalendar(); // First arrival at t=0, first possible movement during [0, step].
            initialized = true;
        }

        public void Step()
        {
            RequireInitialized();
            if (faulted) throw new InvalidOperationException("Engine is faulted; Initialize with fresh streams before continuing.");
            if (IsComplete) return;
            try
            {
                if (steps >= MaxSteps) throw new InvalidOperationException("Simulation step safety limit reached.");
                double before = time;
                double next = (steps + 1) * config.SimulationStepSeconds;
                if (!Finite(next) || next <= before) throw new InvalidOperationException("Logical clock cannot advance.");
                if (next > config.MaxSimulatedSeconds) throw new InvalidOperationException("Maximum simulated duration reached.");
                bool intervalBlocked = blocked;
                int intervalScanner = scanning;
                time = next; steps++;
                ProcessCalendar();
                // Book the interval just elapsed using its boundary state. A blockage applied at
                // this tick stops the NEXT interval; arrivals applied now never move retroactively.
                if (intervalBlocked) blockedSeconds += time - before;
                AdvanceTransport(before, intervalBlocked);
                if (intervalScanner >= 0) AdvanceScanner(intervalScanner, before);
                StartNextScan();
                if (completed == bags.Count)
                {
                    Emit(SimulationEventType.RunCompleted);
                    IsComplete = true;
                }
                else if (time >= config.MaxSimulatedSeconds)
                    throw new InvalidOperationException("Maximum simulated duration reached without completion.");
            }
            catch { faulted = true; throw; }
        }

        private void ProcessCalendar()
        {
            int count = 0;
            while (true)
            {
                double arrival = nextArrival < bags.Count ? bags[nextArrival].ArrivalTimeSeconds : double.PositiveInfinity;
                double end = blocked ? blockEnd : double.PositiveInfinity;
                double due = Math.Min(arrival, Math.Min(nextBlockage, end));
                if (due > time) return;
                if (++count > MaxCalendarEventsPerStep) throw new InvalidOperationException("Too many calendar events in one tick.");
                // Stable ties: occurrence, end of blockage, arrival (arrivals already ordered by ID).
                if (nextBlockage <= end && nextBlockage <= arrival)
                {
                    double scheduled = nextBlockage;
                    occurrences++;
                    Emit(SimulationEventType.BlockageOccurrence, scheduled: scheduled);
                    double until = scheduled + config.BlockageDurationSeconds;
                    if (!Finite(until)) throw new InvalidOperationException("Blockage end overflow.");
                    if (config.BlockageDurationSeconds > 0)
                    {
                        if (until <= scheduled) throw new InvalidOperationException("Blockage duration is below clock resolution.");
                        if (!blocked) { blocked = true; blockEnd = until; Emit(SimulationEventType.BlockageStarted, scheduled: scheduled); }
                        else blockEnd = Math.Max(blockEnd, until);
                    }
                    nextBlockage = AddInterval(scheduled, DrawExponential(RandomStream.Blockage, config.BlockageRatePerSecond));
                }
                else if (end <= arrival)
                {
                    blocked = false;
                    Emit(SimulationEventType.BlockageEnded, scheduled: end);
                    blockEnd = double.PositiveInfinity;
                    while (waitingEntry.Count > 0) StartTransport(waitingEntry.Dequeue(), end);
                }
                else
                {
                    int index = nextArrival++;
                    var bag = bags[index];
                    Emit(SimulationEventType.BagArrived, bag.Id, arrival);
                    if (blocked) { bag.State = BagState.WaitingEntry; waitingEntry.Enqueue(index); }
                    else StartTransport(index, arrival);
                }
            }
        }

        private void StartTransport(int index, double scheduled)
        {
            var bag = bags[index];
            bag.State = BagState.Transporting;
            bag.TransportStartTimeSeconds = time;
            Emit(SimulationEventType.TransportStarted, bag.Id, scheduled);
        }

        private void AdvanceTransport(double before, bool intervalBlocked)
        {
            var finished = new List<KeyValuePair<int, double>>();
            for (int i = 0; i < bags.Count; i++)
            {
                var bag = bags[i];
                if (bag.State != BagState.Transporting) continue;
                bag.ObservedTransportSeconds = time - bag.TransportStartTimeSeconds.Value;
                if (bag.TransportStartTimeSeconds.Value > before || intervalBlocked) continue;
                double remaining = bag.NominalTransportSeconds - transportWork[i];
                transportWork[i] = Math.Min(bag.NominalTransportSeconds, transportWork[i] + time - before);
                bag.TransportProgress01 = bag.NominalTransportSeconds == 0 ? 1 : transportWork[i] / bag.NominalTransportSeconds;
                if (transportWork[i] >= bag.NominalTransportSeconds)
                    finished.Add(new KeyValuePair<int, double>(i, before + Math.Max(0, remaining)));
            }
            finished.Sort((a, b) => { int order = a.Value.CompareTo(b.Value); return order != 0 ? order : a.Key.CompareTo(b.Key); });
            foreach (var item in finished)
            {
                var bag = bags[item.Key];
                bag.State = BagState.ScannerQueue; bag.TransportProgress01 = 1;
                bag.QueueEnterTimeSeconds = time;
                Emit(SimulationEventType.TransportFinished, bag.Id, item.Value);
                scannerQueue.Enqueue(item.Key);
                Emit(SimulationEventType.ScanQueued, bag.Id);
            }
            UpdateQueuePositions();
        }

        private void AdvanceScanner(int index, double before)
        {
            var bag = bags[index];
            busySeconds += time - before;
            scanWork = Math.Min(bag.InspectionSeconds, scanWork + time - before);
            bag.ScanProgress01 = bag.InspectionSeconds == 0 ? 1 : scanWork / bag.InspectionSeconds;
            if (scanWork < bag.InspectionSeconds) return;
            Emit(SimulationEventType.ScanFinished, bag.Id);
            bool flagged = Sampler(RandomStream.Flagging).Bernoulli(config.FlagProbability);
            bag.IsFlagged = flagged;
            bag.State = flagged ? BagState.Flagged : BagState.Approved;
            bag.CompletionTimeSeconds = time;
            bag.ScanProgress01 = 1;
            completed++; scanning = -1;
            Emit(flagged ? SimulationEventType.BagFlagged : SimulationEventType.BagApproved, bag.Id);
        }

        private void StartNextScan()
        {
            if (scanning >= 0 || scannerQueue.Count == 0) return;
            scanning = scannerQueue.Dequeue();
            var bag = bags[scanning];
            bag.InspectionSeconds = DrawExponential(RandomStream.Inspection, 1 / config.MeanInspectionSeconds);
            bag.State = BagState.Scanning; bag.QueuePosition = -1;
            bag.ScanStartTimeSeconds = time; scanWork = 0;
            Emit(SimulationEventType.ScanStarted, bag.Id);
            UpdateQueuePositions();
        }

        private void UpdateQueuePositions()
        {
            int position = 0;
            foreach (int index in scannerQueue) bags[index].QueuePosition = position++;
        }

        public SimulationSnapshot GetSnapshot()
        {
            RequireInitialized();
            var metrics = new SimulationMetrics { BlockageOccurrences = occurrences, TotalBlockedSeconds = blockedSeconds,
                ScannerUtilization = time == 0 ? 0 : busySeconds / time };
            double wait = 0, total = 0; int startedScans = 0;
            foreach (var bag in bags)
            {
                if (bag.State != BagState.Scheduled) metrics.Created++;
                switch (bag.State)
                {
                    case BagState.WaitingEntry: metrics.WaitingEntry++; break;
                    case BagState.Transporting: metrics.OnBelt++; break;
                    case BagState.ScannerQueue: metrics.QueueLength++; break;
                    case BagState.Scanning: metrics.Scanning++; break;
                    case BagState.Approved: metrics.Approved++; metrics.Completed++; break;
                    case BagState.Flagged: metrics.Flagged++; metrics.Completed++; break;
                }
                if (bag.ScanStartTimeSeconds.HasValue)
                { startedScans++; wait += bag.ScanStartTimeSeconds.Value - bag.QueueEnterTimeSeconds.Value; }
                if (bag.CompletionTimeSeconds.HasValue) total += bag.CompletionTimeSeconds.Value - bag.ArrivalTimeSeconds;
            }
            metrics.MeanQueueWaitSeconds = startedScans == 0 ? 0 : wait / startedScans;
            metrics.MeanTotalSeconds = completed == 0 ? 0 : total / completed;
            return new SimulationSnapshot { SimulationTimeSeconds = time, BeltBlocked = blocked,
                IsComplete = IsComplete, Metrics = metrics, Bags = GetBagRecords() };
        }

        public IReadOnlyList<BagRecord> GetBagRecords()
        {
            RequireInitialized();
            var copies = new BagRecord[bags.Count];
            for (int i = 0; i < copies.Length; i++) copies[i] = CopyRecord(bags[i]);
            return Array.AsReadOnly(copies);
        }
        public IReadOnlyList<SimulationEventRecord> GetEventRecords()
        {
            RequireInitialized();
            var copies = new SimulationEventRecord[events.Count];
            for (int i = 0; i < copies.Length; i++)
            {
                var e = events[i];
                copies[i] = new SimulationEventRecord { EventType = e.EventType, SimulationTimeSeconds = e.SimulationTimeSeconds,
                    ScheduledTimeSeconds = e.ScheduledTimeSeconds, BagId = e.BagId, Detail = e.Detail };
            }
            return Array.AsReadOnly(copies);
        }
        private static BagRecord CopyRecord(BagRecord b)
        {
            return new BagRecord { Id = b.Id, State = b.State, WeightKg = b.WeightKg,
                NominalTransportSeconds = b.NominalTransportSeconds, ObservedTransportSeconds = b.ObservedTransportSeconds,
                TransportProgress01 = b.TransportProgress01, ScanProgress01 = b.ScanProgress01, QueuePosition = b.QueuePosition,
                InspectionSeconds = b.InspectionSeconds, ArrivalTimeSeconds = b.ArrivalTimeSeconds,
                TransportStartTimeSeconds = b.TransportStartTimeSeconds, QueueEnterTimeSeconds = b.QueueEnterTimeSeconds,
                ScanStartTimeSeconds = b.ScanStartTimeSeconds, CompletionTimeSeconds = b.CompletionTimeSeconds, IsFlagged = b.IsFlagged };
        }
        private void Emit(SimulationEventType type, int? bag = null, double? scheduled = null)
        {
            if (events.Count >= MaxRecordedEvents) throw new InvalidOperationException("Event log safety limit reached.");
            events.Add(new SimulationEventRecord { EventType = type, BagId = bag, SimulationTimeSeconds = time,
                ScheduledTimeSeconds = scheduled, Detail = type.ToString() });
        }
        private IDistributionSampler Sampler(RandomStream stream)
        { return samplers.Get(stream) ?? throw new InvalidOperationException("Missing random stream: " + stream); }
        private double DrawExponential(RandomStream stream, double rate)
        {
            double value = Sampler(stream).Exponential(rate);
            if (!Finite(value) || value < 0 || (stream != RandomStream.Inspection && value == 0))
                throw new InvalidOperationException("Invalid exponential sample in " + stream);
            return value;
        }
        private static double AddInterval(double previous, double interval)
        {
            double next = previous + interval;
            if (!Finite(next) || next <= previous) throw new InvalidOperationException("Occurrence schedule cannot advance.");
            return next;
        }
        private static void RequireSample(double value, double min, double max, string name)
        {
            if (!Finite(value) || value < min || value > max) throw new InvalidOperationException("Invalid " + name + " sample.");
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private void RequireInitialized()
        { if (!initialized) throw new InvalidOperationException("Initialize the engine before use."); }
    }
}
