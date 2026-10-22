using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AirportBaggageSimulator.Tests
{
    public sealed class SimulationEngineTests
    {
        // Scripted values only, not alternative probability algorithms.
        private sealed class ScriptedSampler : IDistributionSampler
        {
            public readonly Queue<double> Values = new Queue<double>();
            public double Default = 1;
            public int TriangularCalls, ExponentialCalls, NormalCalls, BernoulliCalls;
            public double Triangular(double a, double c, double b) { TriangularCalls++; return Next(); }
            public double Exponential(double rate) { ExponentialCalls++; return Next(); }
            public double TruncatedNormal(double mean, double std, double min, double max) { NormalCalls++; return Next(); }
            public bool Bernoulli(double probability) { BernoulliCalls++; return probability == 1; }
            private double Next() { return Values.Count == 0 ? Default : Values.Dequeue(); }
        }
        private sealed class ScriptedStreams : IRandomStreamProvider
        {
            public readonly ScriptedSampler[] Streams = new ScriptedSampler[6];
            public ScriptedStreams()
            {
                for (int i = 0; i < Streams.Length; i++) Streams[i] = new ScriptedSampler();
                Streams[(int)RandomStream.Weight].Default = 18;
                Streams[(int)RandomStream.Blockage].Default = 100000;
            }
            public ScriptedSampler this[RandomStream stream] { get { return Streams[(int)stream]; } }
            public IDistributionSampler Get(RandomStream stream) { return this[stream]; }
        }
        private static SimulationConfig Config(int n = 3)
        {
            return new SimulationConfig { BaggageCount = n, SimulationStepSeconds = 1,
                TransportMinSeconds = 0, TransportModeSeconds = 1, TransportMaxSeconds = 10,
                MeanArrivalIntervalSeconds = 1, BlockageRatePerSecond = 0 };
        }
        private static SimulationEngine Start(SimulationConfig c, IRandomStreamProvider streams)
        { var e = new SimulationEngine(); e.Initialize(c, streams); return e; }
        private static void Finish(SimulationEngine e, int max = 200000)
        {
            for (int i = 0; i < max && !e.IsComplete; i++) e.Step();
            Assert.That(e.IsComplete, Is.True, "Engine did not finish within the test budget.");
        }
        private static void Invariants(SimulationSnapshot s, int n)
        {
            var m = s.Metrics;
            Assert.That(s.Bags.Count, Is.EqualTo(n));
            Assert.That(m.Completed, Is.EqualTo(m.Approved + m.Flagged));
            Assert.That(m.Created, Is.InRange(m.Completed, n));
            Assert.That(m.Created, Is.EqualTo(m.WaitingEntry + m.OnBelt + m.QueueLength + m.Scanning + m.Completed));
            Assert.That(m.Scanning, Is.InRange(0, 1));
            Assert.That(m.ScannerUtilization, Is.InRange(0, 1.000000000001));
            Assert.That(m.TotalBlockedSeconds, Is.InRange(0, s.SimulationTimeSeconds));
            Assert.That(s.Bags.Select(b => b.Id).Distinct().Count(), Is.EqualTo(n));
            foreach (var bag in s.Bags)
            {
                Assert.That(bag.TransportProgress01, Is.InRange(0, 1)); Assert.That(bag.ScanProgress01, Is.InRange(0, 1));
                Assert.That(bag.WeightKg, Is.InRange(5, 32));
                if (bag.State != BagState.ScannerQueue) Assert.That(bag.QueuePosition, Is.EqualTo(-1));
                if (bag.TransportStartTimeSeconds.HasValue) Assert.That(bag.TransportStartTimeSeconds.Value, Is.GreaterThanOrEqualTo(bag.ArrivalTimeSeconds));
                if (bag.QueueEnterTimeSeconds.HasValue) Assert.That(bag.QueueEnterTimeSeconds.Value, Is.GreaterThanOrEqualTo(bag.TransportStartTimeSeconds.Value));
                if (bag.ScanStartTimeSeconds.HasValue) Assert.That(bag.ScanStartTimeSeconds.Value, Is.GreaterThanOrEqualTo(bag.QueueEnterTimeSeconds.Value));
                if (bag.CompletionTimeSeconds.HasValue) Assert.That(bag.CompletionTimeSeconds.Value, Is.GreaterThanOrEqualTo(bag.ScanStartTimeSeconds.Value));
            }
            Assert.That(s.Bags.Where(b => b.State == BagState.ScannerQueue).Select(b => b.QueuePosition).OrderBy(x => x),
                Is.EqualTo(Enumerable.Range(0, m.QueueLength)));
        }

        [TestCase(1, ArrivalMode.Constant)] [TestCase(60, ArrivalMode.Constant)] [TestCase(500, ArrivalMode.Constant)]
        [TestCase(1, ArrivalMode.Exponential)] [TestCase(60, ArrivalMode.Exponential)] [TestCase(500, ArrivalMode.Exponential)]
        public void RealProbabilityRunsCompleteWithEveryInvariant(int n, ArrivalMode mode)
        {
            var c = new SimulationConfig { BaggageCount = n, ArrivalMode = mode };
            var e = Start(c, new DeterministicRandomStreamProvider(c.Seed));
            var old = e.GetSnapshot();
            for (int tick = 0; tick < 200000 && !e.IsComplete; tick++)
            {
                e.Step();
                // Check all states regularly, and progress monotonicity on every logical tick.
                var s = e.GetSnapshot();
                for (int i = 0; i < n; i++)
                {
                    if (s.Bags[i].TransportProgress01 < old.Bags[i].TransportProgress01 ||
                        s.Bags[i].ScanProgress01 < old.Bags[i].ScanProgress01)
                        Assert.Fail("Progress decreased for bag " + s.Bags[i].Id);
                }
                if (tick % 100 == 0 || e.IsComplete) Invariants(s, n);
                old = s;
            }
            Assert.That(e.IsComplete, Is.True);
            Assert.That(old.Metrics.Created, Is.EqualTo(n)); Assert.That(old.Metrics.Completed, Is.EqualTo(n));
            Assert.That(e.GetEventRecords().Count(x => x.EventType == SimulationEventType.RunCompleted), Is.EqualTo(1));
            Assert.That(e.GetEventRecords().Count(x => x.EventType == SimulationEventType.ScanFinished), Is.EqualTo(n));
            Assert.That(e.GetEventRecords().Count(x => x.EventType == SimulationEventType.BagApproved || x.EventType == SimulationEventType.BagFlagged), Is.EqualTo(n));
        }
        [Test] public void FiveHundredRapidArrivalsAndFrequentBlockagesRemainConsistent()
        {
            var c = new SimulationConfig { BaggageCount = 500, MeanArrivalIntervalSeconds = .001,
                MeanInspectionSeconds = .01, BlockageRatePerSecond = 2, BlockageDurationSeconds = 1 };
            var e = Start(c, new DeterministicRandomStreamProvider(c.Seed));
            for (int i = 0; i < 10; i++) e.Step();
            Assert.That(e.GetSnapshot().Metrics.Created, Is.EqualTo(500));
            Finish(e); var s = e.GetSnapshot(); Invariants(s, 500);
            Assert.That(s.Metrics.Completed, Is.EqualTo(500)); Assert.That(s.Metrics.BlockageOccurrences, Is.GreaterThan(0));
            Assert.That(s.Metrics.BlockageOccurrences, Is.EqualTo(e.GetEventRecords().Count(x => x.EventType == SimulationEventType.BlockageOccurrence)));
            var starts = e.GetEventRecords().Where(x => x.EventType == SimulationEventType.ScanStarted).ToArray();
            var completions = e.GetEventRecords().Where(x => x.EventType == SimulationEventType.ScanFinished).ToArray();
            for (int i = 1; i < starts.Length; i++)
                Assert.That(starts[i].SimulationTimeSeconds, Is.GreaterThanOrEqualTo(completions[i - 1].SimulationTimeSeconds));
        }
        [Test] public void InitializationCreatesAttributesOnceAndFirstArrivalIsAtZero()
        {
            var streams = new ScriptedStreams(); var e = Start(Config(), streams); var s = e.GetSnapshot();
            Assert.That(s.SimulationTimeSeconds, Is.Zero); Assert.That(s.Metrics.Created, Is.EqualTo(1));
            Assert.That(s.Bags[0].State, Is.EqualTo(BagState.Transporting));
            Assert.That(s.Bags[1].State, Is.EqualTo(BagState.Scheduled));
            Assert.That(s.Bags.Select(b => b.ArrivalTimeSeconds), Is.EqualTo(new[] { 0d, 1d, 2d }));
            Assert.That(streams[RandomStream.Weight].NormalCalls, Is.EqualTo(3));
            Assert.That(streams[RandomStream.Transport].TriangularCalls, Is.EqualTo(3));
            Assert.That(streams[RandomStream.Inspection].ExponentialCalls, Is.Zero);
            Assert.That(streams[RandomStream.Flagging].BernoulliCalls, Is.Zero);
            Finish(e);
            Assert.That(streams[RandomStream.Weight].NormalCalls, Is.EqualTo(3));
            Assert.That(streams[RandomStream.Transport].TriangularCalls, Is.EqualTo(3));
            Assert.That(streams[RandomStream.Inspection].ExponentialCalls, Is.EqualTo(3));
            Assert.That(streams[RandomStream.Flagging].BernoulliCalls, Is.EqualTo(3));
            Assert.That(streams[RandomStream.Blockage].ExponentialCalls, Is.Zero);
            Assert.That(streams[RandomStream.Arrival].ExponentialCalls, Is.Zero);
        }
        [Test] public void ExponentialArrivalsPreserveRawGapsAndQuantizedApplicationTimes()
        {
            var c = Config(); c.ArrivalMode = ArrivalMode.Exponential;
            var streams = new ScriptedStreams(); streams[RandomStream.Arrival].Values.Enqueue(0.25); streams[RandomStream.Arrival].Values.Enqueue(0.5);
            var e = Start(c, streams);
            Assert.That(e.GetBagRecords().Select(b => b.ArrivalTimeSeconds), Is.EqualTo(new[] { 0d, .25, .75 }));
            e.Step(); var arrivals = e.GetEventRecords().Where(x => x.EventType == SimulationEventType.BagArrived).ToArray();
            Assert.That(arrivals.Select(x => x.ScheduledTimeSeconds.Value), Is.EqualTo(new[] { 0d, .25, .75 }));
            Assert.That(arrivals.Select(x => x.SimulationTimeSeconds), Is.EqualTo(new[] { 0d, 1d, 1d }));
            Assert.That(e.GetBagRecords()[1].TransportProgress01, Is.Zero, "No retroactive movement for new arrivals.");
            Assert.That(streams[RandomStream.Arrival].ExponentialCalls, Is.EqualTo(2));
        }
        [Test] public void FifoUsesTransportCompletionTimeThenIdForTies()
        {
            var c = Config(); c.MeanArrivalIntervalSeconds = .1;
            var streams = new ScriptedStreams();
            foreach (double x in new[] { 2.8, 1.2, 1.2 }) streams[RandomStream.Transport].Values.Enqueue(x);
            streams[RandomStream.Inspection].Default = 5;
            var e = Start(c, streams); e.Step(); e.Step(); e.Step();
            var s = e.GetSnapshot();
            Assert.That(s.Bags[1].State, Is.EqualTo(BagState.Scanning));
            Assert.That(s.Bags[2].QueuePosition, Is.Zero); Assert.That(s.Bags[0].QueuePosition, Is.EqualTo(1));
            Finish(e);
            Assert.That(e.GetEventRecords().Where(x => x.EventType == SimulationEventType.ScanStarted).Select(x => x.BagId.Value), Is.EqualTo(new[] { 2, 3, 1 }));
            var finished = e.GetBagRecords();
            Assert.That(finished[1].InspectionSeconds, Is.EqualTo(5));
            Assert.That(finished.All(b => b.QueuePosition == -1), Is.True);
        }
        [TestCase(0)] [TestCase(1)] public void FlagCountsAreExactlyTheIndividualDecisions(double p)
        {
            var c = Config(60); c.FlagProbability = p; var streams = new ScriptedStreams(); var e = Start(c, streams);
            Finish(e); var s = e.GetSnapshot();
            Assert.That(s.Metrics.Flagged, Is.EqualTo(p == 1 ? 60 : 0));
            Assert.That(s.Metrics.Flagged, Is.EqualTo(s.Bags.Count(b => b.IsFlagged == true)));
            Assert.That(streams[RandomStream.Flagging].BernoulliCalls, Is.EqualTo(60));
        }
        [Test] public void OverlappingBlockagesFreezeTransportButNotScannerAndReleaseEntryInFifo()
        {
            var c = Config(4); c.BlockageRatePerSecond = 1; c.BlockageDurationSeconds = 2;
            var streams = new ScriptedStreams(); streams[RandomStream.Inspection].Default = 3;
            foreach (double duration in new[] { 1d, 4d, 1d, 1d }) streams[RandomStream.Transport].Values.Enqueue(duration);
            streams[RandomStream.Blockage].Values.Enqueue(1.5); streams[RandomStream.Blockage].Values.Enqueue(1);
            var e = Start(c, streams);
            e.Step(); Assert.That(e.GetSnapshot().Bags[0].State, Is.EqualTo(BagState.Scanning));
            e.Step(); var start = e.GetSnapshot(); Assert.That(start.BeltBlocked, Is.True);
            Assert.That(start.Metrics.WaitingEntry, Is.EqualTo(1));
            Assert.That(start.Bags[1].State, Is.EqualTo(BagState.Transporting));
            Assert.That(start.Bags[1].TransportProgress01, Is.EqualTo(.25));
            e.Step(); var frozen = e.GetSnapshot();
            Assert.That(frozen.BeltBlocked, Is.True); Assert.That(frozen.Metrics.BlockageOccurrences, Is.EqualTo(2));
            Assert.That(frozen.Bags[1].TransportProgress01, Is.EqualTo(start.Bags[1].TransportProgress01));
            Assert.That(frozen.Bags[0].ScanProgress01, Is.GreaterThan(start.Bags[0].ScanProgress01));
            e.Step(); Assert.That(e.GetBagRecords()[0].State, Is.EqualTo(BagState.Approved));
            e.Step(); var released = e.GetSnapshot(); Assert.That(released.BeltBlocked, Is.False);
            Assert.That(released.Metrics.WaitingEntry, Is.Zero); Assert.That(released.Metrics.TotalBlockedSeconds, Is.EqualTo(3));
            Assert.That(e.GetBagRecords()[2].TransportStartTimeSeconds, Is.EqualTo(5));
            var occurrences = e.GetEventRecords().Where(x => x.EventType == SimulationEventType.BlockageOccurrence).ToArray();
            Assert.That(occurrences.Select(x => x.ScheduledTimeSeconds.Value), Is.EqualTo(new[] { 1.5, 2.5 }));
            Assert.That(occurrences.Select(x => x.SimulationTimeSeconds), Is.EqualTo(new[] { 2d, 3d }));
            Assert.That(e.GetEventRecords().Count(x => x.EventType == SimulationEventType.BlockageStarted), Is.EqualTo(1));
            Assert.That(e.GetEventRecords().Single(x => x.EventType == SimulationEventType.BlockageEnded).ScheduledTimeSeconds, Is.EqualTo(4.5));
            Finish(e); Invariants(e.GetSnapshot(), 4);
            Assert.That(e.GetEventRecords().Where(x => x.EventType == SimulationEventType.TransportStarted && x.BagId >= 3).Select(x => x.BagId.Value), Is.EqualTo(new[] { 3, 4 }));
        }
        [Test] public void ZeroBlockDurationCountsOccurrencesWithoutStoppingTheBelt()
        {
            var c = Config(); c.BlockageRatePerSecond = 1; c.BlockageDurationSeconds = 0;
            var streams = new ScriptedStreams(); streams[RandomStream.Blockage].Values.Enqueue(.5);
            var e = Start(c, streams); Finish(e); var s = e.GetSnapshot();
            Assert.That(s.Metrics.BlockageOccurrences, Is.EqualTo(1)); Assert.That(s.Metrics.TotalBlockedSeconds, Is.Zero);
            Assert.That(e.GetEventRecords().Any(x => x.EventType == SimulationEventType.BlockageStarted), Is.False);
        }
        [Test] public void EqualTimeOccurrencePrecedesArrivalAndEventsRemainChronological()
        {
            var c = Config(2); c.BlockageRatePerSecond = 1; c.BlockageDurationSeconds = 2;
            var streams = new ScriptedStreams(); streams[RandomStream.Blockage].Values.Enqueue(1);
            var e = Start(c, streams); e.Step();
            Assert.That(e.GetBagRecords()[1].State, Is.EqualTo(BagState.WaitingEntry));
            var sameTick = e.GetEventRecords().Where(x => x.SimulationTimeSeconds == 1).ToArray();
            Assert.That(Array.FindIndex(sameTick, x => x.EventType == SimulationEventType.BlockageOccurrence),
                Is.LessThan(Array.FindIndex(sameTick, x => x.EventType == SimulationEventType.BagArrived)));
            Finish(e);
            var log = e.GetEventRecords();
            Assert.That(log.Select(x => x.SimulationTimeSeconds), Is.Ordered);
            Assert.That(e.GetSnapshot().Metrics.BlockageOccurrences,
                Is.EqualTo(log.Count(x => x.EventType == SimulationEventType.BlockageOccurrence)));
        }
        [Test] public void SubTickBlockageRetainsOccurrenceWithoutInventingAStoppedInterval()
        {
            var c = Config(); c.BlockageRatePerSecond = 1; c.BlockageDurationSeconds = .1;
            var streams = new ScriptedStreams(); streams[RandomStream.Blockage].Values.Enqueue(.2);
            var e = Start(c, streams); e.Step(); var s = e.GetSnapshot();
            Assert.That(s.Metrics.BlockageOccurrences, Is.EqualTo(1)); Assert.That(s.BeltBlocked, Is.False);
            Assert.That(s.Metrics.TotalBlockedSeconds, Is.Zero);
            Assert.That(e.GetEventRecords().Single(x => x.EventType == SimulationEventType.BlockageEnded).ScheduledTimeSeconds, Is.EqualTo(.3).Within(1e-15));
        }
        [Test] public void ZeroTransportAndInspectionCompleteAtSubsequentTicksWithoutLoops()
        {
            var c = Config(1); c.TransportMinSeconds = c.TransportModeSeconds = c.TransportMaxSeconds = 0;
            var streams = new ScriptedStreams(); streams[RandomStream.Transport].Default = 0; streams[RandomStream.Inspection].Default = 0;
            var e = Start(c, streams); e.Step(); Assert.That(e.IsComplete, Is.False);
            e.Step(); Assert.That(e.IsComplete, Is.True);
            Assert.That(e.GetBagRecords()[0].InspectionSeconds, Is.Zero);
            Assert.That(e.GetSnapshot().SimulationTimeSeconds, Is.EqualTo(2));
        }
        [Test] public void RawSamplesAreNotReplacedWithQuantizedDurations()
        {
            var streams = new ScriptedStreams(); streams[RandomStream.Transport].Default = .25; streams[RandomStream.Inspection].Default = .125;
            var e = Start(Config(1), streams); Finish(e); var b = e.GetBagRecords()[0];
            Assert.That(b.NominalTransportSeconds, Is.EqualTo(.25)); Assert.That(b.InspectionSeconds, Is.EqualTo(.125));
            Assert.That(b.ObservedTransportSeconds, Is.EqualTo(1)); Assert.That(b.CompletionTimeSeconds - b.ScanStartTimeSeconds, Is.EqualTo(1));
            Assert.That(e.GetSnapshot().Metrics.ScannerUtilization, Is.EqualTo(.5));
        }
        [Test] public void MetricsMatchAHandCalculatedSingleScannerRun()
        {
            var streams = new ScriptedStreams(); streams[RandomStream.Inspection].Default = 3;
            var e = Start(Config(), streams); Finish(e); var s = e.GetSnapshot();
            Assert.That(s.SimulationTimeSeconds, Is.EqualTo(10));
            Assert.That(s.Bags.Select(b => b.QueueEnterTimeSeconds.Value), Is.EqualTo(new[] { 1d, 2d, 3d }));
            Assert.That(s.Bags.Select(b => b.ScanStartTimeSeconds.Value), Is.EqualTo(new[] { 1d, 4d, 7d }));
            Assert.That(s.Bags.Select(b => b.CompletionTimeSeconds.Value), Is.EqualTo(new[] { 4d, 7d, 10d }));
            Assert.That(s.Metrics.MeanQueueWaitSeconds, Is.EqualTo(2));
            Assert.That(s.Metrics.MeanTotalSeconds, Is.EqualTo(6));
            Assert.That(s.Metrics.ScannerUtilization, Is.EqualTo(.9));
            Assert.That(s.Metrics.TotalBlockedSeconds, Is.Zero);
        }
        [Test] public void ArrivalScheduleOverflowAndLossOfResolutionAreExplicitErrors()
        {
            foreach (double interval in new[] { double.MaxValue, 1d })
            {
                var streams = new ScriptedStreams();
                streams[RandomStream.Arrival].Values.Enqueue(double.MaxValue);
                streams[RandomStream.Arrival].Values.Enqueue(interval);
                var c = Config(); c.ArrivalMode = ArrivalMode.Exponential;
                Assert.Throws<InvalidOperationException>(() => new SimulationEngine().Initialize(c, streams));
            }
        }
        [Test] public void SnapshotsAndRecordsAreDetachedFromEngineAndEachOther()
        {
            var e = Start(Config(), new ScriptedStreams()); var a = e.GetSnapshot(); var b = e.GetSnapshot();
            a.Bags[0].WeightKg = 999; a.Bags[0].State = BagState.Flagged; a.Metrics.Completed = 999;
            e.GetBagRecords()[1].ArrivalTimeSeconds = -999;
            e.GetEventRecords()[0].Detail = "mutated";
            Assert.That(b.Bags[0].WeightKg, Is.EqualTo(18)); Assert.That(b.Metrics.Completed, Is.Zero);
            Assert.That(e.GetBagRecords()[1].ArrivalTimeSeconds, Is.EqualTo(1));
            Assert.That(e.GetEventRecords()[0].Detail, Is.Not.EqualTo("mutated"));
            Finish(e); Assert.That(b.Bags[0].State, Is.EqualTo(BagState.Transporting));
        }
        private static string Fingerprint(SimulationEngine e)
        {
            return string.Join("|", e.GetBagRecords().Select(b => string.Join(",", typeof(BagRecord).GetFields().Select(f => f.GetValue(b))))) +
                "#" + string.Join("|", e.GetEventRecords().Select(x => x.EventType + "," + x.SimulationTimeSeconds.ToString("R") + "," + x.ScheduledTimeSeconds + "," + x.BagId));
        }
        [Test] public void ReinitializeWithFreshSeededStreamsReproducesEntireRunAndIgnoresPlayback()
        {
            var c = new SimulationConfig { ArrivalMode = ArrivalMode.Exponential };
            var e = Start(c, new DeterministicRandomStreamProvider(c.Seed)); Finish(e); string first = Fingerprint(e);
            c.PlaybackSpeed = 20;
            e.Initialize(c, new DeterministicRandomStreamProvider(c.Seed)); Finish(e); Assert.That(Fingerprint(e), Is.EqualTo(first));
            c.Seed++; e.Initialize(c, new DeterministicRandomStreamProvider(c.Seed)); Finish(e); Assert.That(Fingerprint(e), Is.Not.EqualTo(first));
        }
        [Test] public void MutatingCallerConfigDoesNotAffectTheRun()
        {
            var c = Config(); var e = Start(c, new ScriptedStreams());
            c.BaggageCount = 500; c.FlagProbability = 1; c.SimulationStepSeconds = 100;
            Finish(e); Assert.That(e.GetSnapshot().Metrics.Completed, Is.EqualTo(3)); Assert.That(e.GetSnapshot().Metrics.Flagged, Is.Zero);
        }
        [Test] public void StepAfterCompletionIsAnIdempotentNoOp()
        {
            var streams = new ScriptedStreams(); var e = Start(Config(1), streams); Finish(e); string first = Fingerprint(e);
            double time = e.GetSnapshot().SimulationTimeSeconds; e.Step(); e.Step();
            Assert.That(e.GetSnapshot().SimulationTimeSeconds, Is.EqualTo(time)); Assert.That(Fingerprint(e), Is.EqualTo(first));
            Assert.That(streams[RandomStream.Flagging].BernoulliCalls, Is.EqualTo(1));
        }
        [Test] public void HorizonFailureIsExplicitAndEngineCannotContinueUntilReinitialized()
        {
            var c = Config(1); c.MaxSimulatedSeconds = 1; var e = Start(c, new ScriptedStreams());
            Assert.Throws<InvalidOperationException>(() => e.Step()); Assert.That(e.IsComplete, Is.False);
            Assert.Throws<InvalidOperationException>(() => e.Step());
            e.Initialize(Config(1), new ScriptedStreams()); Finish(e);
        }
        [Test] public void DensePoissonCalendarStopsInsteadOfLoopingForever()
        {
            var c = Config(); c.BlockageRatePerSecond = 1;
            var streams = new ScriptedStreams(); streams[RandomStream.Blockage].Default = .00001;
            var e = Start(c, streams);
            var error = Assert.Throws<InvalidOperationException>(() => e.Step());
            Assert.That(error.Message, Does.Contain("calendar events")); Assert.That(e.IsComplete, Is.False);
        }
        [Test] public void InvalidParametersAreRejectedBeforeUsingStreams()
        {
            var mutations = new Action<SimulationConfig>[] { c => c.BaggageCount = 0, c => c.BaggageCount = 501,
                c => c.ArrivalMode = (ArrivalMode)99, c => c.MeanArrivalIntervalSeconds = 0,
                c => c.MeanInspectionSeconds = -1, c => c.WeightBaseStdKg = 0, c => c.MinWeightKg = 50,
                c => c.TransportModeSeconds = 30, c => c.TransportMinSeconds = -1,
                c => c.FlagProbability = -1, c => c.FlagProbability = 2,
                c => c.BlockageRatePerSecond = -1, c => c.BlockageDurationSeconds = -1,
                c => c.SimulationStepSeconds = 0, c => c.MaxSimulatedSeconds = 0,
                c => c.PlaybackSpeed = 2, c => c.WeightBaseMeanKg = double.NaN,
                c => c.BlockageRatePerSecond = double.PositiveInfinity, c => c.MeanInspectionSeconds = double.Epsilon };
            foreach (var mutate in mutations)
            {
                var c = Config(); mutate(c); var streams = new ScriptedStreams();
                Assert.Throws<ArgumentException>(() => new SimulationEngine().Initialize(c, streams));
                Assert.That(streams[RandomStream.Weight].NormalCalls, Is.Zero);
            }
            Assert.Throws<ArgumentNullException>(() => new SimulationEngine().Initialize(null, new ScriptedStreams()));
            Assert.Throws<ArgumentNullException>(() => new SimulationEngine().Initialize(Config(), null));
        }
        [TestCase(RandomStream.Weight, double.NaN)] [TestCase(RandomStream.Weight, 99)]
        [TestCase(RandomStream.Transport, -1)] [TestCase(RandomStream.Inspection, -1)]
        [TestCase(RandomStream.Blockage, 0)] [TestCase(RandomStream.Arrival, 0)]
        public void InvalidSamplerValuesAreReportedWithoutClamping(RandomStream stream, double value)
        {
            var c = Config(); c.ArrivalMode = ArrivalMode.Exponential; c.BlockageRatePerSecond = 1;
            var streams = new ScriptedStreams(); streams[stream].Default = value;
            var e = new SimulationEngine();
            Assert.Throws<InvalidOperationException>(() => { e.Initialize(c, streams); Finish(e); });
        }
        [Test] public void UninitializedEngineCannotStepOrExposeRecords()
        {
            var e = new SimulationEngine(); Assert.That(e.IsComplete, Is.False);
            Assert.Throws<InvalidOperationException>(() => e.Step()); Assert.Throws<InvalidOperationException>(() => e.GetSnapshot());
            Assert.Throws<InvalidOperationException>(() => e.GetBagRecords()); Assert.Throws<InvalidOperationException>(() => e.GetEventRecords());
        }
    }
}
