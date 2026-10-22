using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirportBaggageSimulator.Tests
{
    public sealed class SimulationIntegrationTests
    {
        // Test double only. This does not implement operational states, queues or scanner rules.
        private sealed class EngineProbe : ISimulationEngine
        {
            public SimulationConfig Config;
            public IRandomStreamProvider Provider;
            public int Steps;
            public int SnapshotCalls;
            public double? ForcedStepTime;
            public int CompleteAt = 100;
            public bool Stall;
            public double Time;
            public double FirstWeight;
            public readonly List<double> Draws = new List<double>();
            public readonly BagRecord Bag = new BagRecord { Id = 1, WeightKg = 18 };
            public readonly SimulationEventRecord Event = new SimulationEventRecord { Detail = "original" };
            public void Initialize(SimulationConfig config, IRandomStreamProvider samplers)
            {
                Config = config; Provider = samplers; Steps = 0; Time = 0; Draws.Clear();
                FirstWeight = samplers.Get(RandomStream.Weight).TruncatedNormal(18, 4, 5, 32);
            }
            public void Step()
            {
                Steps++; if (!Stall) Time = ForcedStepTime ?? (Time + Config.SimulationStepSeconds);
                Draws.Add(Provider.Get(RandomStream.Inspection).Exponential(0.125));
            }
            public SimulationSnapshot GetSnapshot()
            {
                SnapshotCalls++;
                return new SimulationSnapshot { SimulationTimeSeconds = Time,
                    IsComplete = IsComplete, Bags = new BagRecord[0], Metrics = new SimulationMetrics() };
            }
            public IReadOnlyList<BagRecord> GetBagRecords() { return new[] { Bag }; }
            public IReadOnlyList<SimulationEventRecord> GetEventRecords() { return new[] { Event }; }
            public bool IsComplete { get { return Steps >= CompleteAt; } }
        }
        private sealed class Views : ISimulationControlView, ISimulationWorldView, ISimulationDashboardView
        {
            public event Action<SimulationConfig> StartRequested;
            public event Action PauseRequested;
            public event Action ResumeRequested;
            public event Action ResetRequested;
            public event Action<double> PlaybackSpeedRequested;
            public event Action ExportRequested;
            public int Resets, Renders;
            public string Error;
            public bool Running;
            public void Start(SimulationConfig c) { StartRequested?.Invoke(c); }
            public void Pause() { PauseRequested?.Invoke(); }
            public void Resume() { ResumeRequested?.Invoke(); }
            public void Reset() { ResetRequested?.Invoke(); }
            public void Speed(double v) { PlaybackSpeedRequested?.Invoke(v); }
            public void Export() { ExportRequested?.Invoke(); }
            public void SetRunning(bool running) { Running = running; }
            public void ShowValidationError(string message) { Error = message; }
            public void ShowError(string message) { Error = message; }
            public void Render(SimulationSnapshot snapshot) { Renders++; }
            public void ResetView() { Resets++; }
        }
        [Test] public void ResetRebuildsProviderAndPreservesOriginalConfig()
        {
            var e = new EngineProbe(); var s = new SimulationSession(e); var c = new SimulationConfig();
            s.Start(c); var provider = e.Provider; double weight = e.FirstWeight;
            s.Advance(0.1); var draws = e.Draws.ToArray();
            c.Seed = 1234; c.MeanArrivalIntervalSeconds = 99;
            e.Config.Seed = 5678; // Engine also receives a separate config copy.
            s.Reset();
            Assert.That(s.IsRunning, Is.False);
            Assert.That(e.Provider, Is.Not.SameAs(provider));
            Assert.That(e.FirstWeight, Is.EqualTo(weight));
            Assert.That(e.Config.Seed, Is.EqualTo(42));
            Assert.That(e.Config.MeanArrivalIntervalSeconds, Is.EqualTo(5));
            s.Resume(); s.Advance(0.1); Assert.That(e.Draws, Is.EqualTo(draws));
        }
        [Test] public void PauseConsumesNeitherTicksNorRandomNumbers()
        {
            var e = new EngineProbe(); var s = new SimulationSession(e); s.Start(new SimulationConfig());
            s.Advance(0.02); int before = e.Steps; double pending = s.PendingSeconds;
            s.Pause(); s.Advance(100);
            Assert.That(e.Steps, Is.EqualTo(before)); Assert.That(s.PendingSeconds, Is.EqualTo(pending));
            s.Resume(); s.Advance(0.02); Assert.That(e.Steps, Is.GreaterThan(before));
        }
        [TestCase(1)] [TestCase(5)] [TestCase(10)] [TestCase(20)]
        public void PlaybackAndFramePartitionKeepTheSameLogicalDraws(double speed)
        {
            var e = new EngineProbe(); var reference = new EngineProbe();
            var s = new SimulationSession(e); var r = new SimulationSession(reference);
            s.Start(new SimulationConfig { PlaybackSpeed = speed }); r.Start(new SimulationConfig());
            while (s.IsRunning) s.Advance(0.016, 7);
            while (r.IsRunning) r.Advance(0.071, 13);
            Assert.That(e.Draws, Is.EqualTo(reference.Draws)); Assert.That(e.Time, Is.EqualTo(reference.Time));
        }
        [Test] public void FrameBudgetKeepsBacklogAndCompletionStopsStepping()
        {
            var e = new EngineProbe { CompleteAt = 3 }; var s = new SimulationSession(e); s.Start(new SimulationConfig());
            s.Advance(1, 1); Assert.That(e.Steps, Is.EqualTo(1)); Assert.That(s.PendingSeconds, Is.GreaterThan(9));
            s.Advance(0, 1); Assert.That(e.Steps, Is.EqualTo(2)); s.Advance(0, 1);
            Assert.That(s.IsRunning, Is.False); s.Advance(1); Assert.That(e.Steps, Is.EqualTo(3));
        }
        [Test] public void MultipleTicksReuseSnapshotsAndIdleFramesDoNotRequestCopies()
        {
            var e = new EngineProbe(); var s = new SimulationSession(e);
            s.Start(new SimulationConfig { BaggageCount = 500, PlaybackSpeed = 1 });
            Assert.That(e.SnapshotCalls, Is.Zero);
            s.Advance(0.01); Assert.That(e.SnapshotCalls, Is.Zero);
            s.Advance(1, 5);
            Assert.That(e.Steps, Is.EqualTo(5));
            Assert.That(e.SnapshotCalls, Is.EqualTo(6), "N ticks require N+1 snapshots, not 2N.");
            s.Advance(0, 3);
            Assert.That(e.Steps, Is.EqualTo(8)); Assert.That(e.SnapshotCalls, Is.EqualTo(10));
            s.Pause(); s.Advance(100); Assert.That(e.SnapshotCalls, Is.EqualTo(10));
            e.CompleteAt = 10; s.Resume(); s.Advance(1);
            Assert.That(e.Steps, Is.EqualTo(10)); Assert.That(e.SnapshotCalls, Is.EqualTo(13));
            Assert.That(s.IsRunning, Is.False);
            s.Advance(1); Assert.That(e.SnapshotCalls, Is.EqualTo(13));
        }
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(double.NegativeInfinity)]
        public void NonFiniteClockBeforeFirstTickStopsWithoutStepping(double time)
        {
            var e = new EngineProbe(); var s = new SimulationSession(e); s.Start(new SimulationConfig());
            e.Time = time;
            Assert.Throws<InvalidOperationException>(() => s.Advance(1));
            Assert.That(e.Steps, Is.Zero); Assert.That(s.IsRunning, Is.False);
        }
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(double.NegativeInfinity)] [TestCase(-1)]
        public void InvalidClockAfterStepStillStopsWithReusedSnapshots(double time)
        {
            var e = new EngineProbe { ForcedStepTime = time }; var s = new SimulationSession(e);
            s.Start(new SimulationConfig());
            Assert.Throws<InvalidOperationException>(() => s.Advance(1));
            Assert.That(e.Steps, Is.EqualTo(1)); Assert.That(e.SnapshotCalls, Is.EqualTo(2));
            Assert.That(s.IsRunning, Is.False);
        }
        [Test] public void InvalidConfigurationIsRejectedBeforeEngineInitialization()
        {
            var e = new EngineProbe(); var s = new SimulationSession(e);
            Assert.Throws<ArgumentException>(() => s.Start(new SimulationConfig { BaggageCount = 501 }));
            Assert.That(e.Provider, Is.Null);
            var cases = new Action<SimulationConfig>[] {
                c => c.BaggageCount = 0, c => c.ArrivalMode = (ArrivalMode)99,
                c => c.MeanArrivalIntervalSeconds = 0, c => c.MeanInspectionSeconds = -1,
                c => c.TransportModeSeconds = 100, c => c.TransportMinSeconds = -1,
                c => c.WeightBaseStdKg = 0, c => c.MinWeightKg = 40, c => c.MinWeightKg = -1,
                c => c.FlagProbability = 2, c => c.BlockageRatePerSecond = -1,
                c => c.BlockageDurationSeconds = -1, c => c.PlaybackSpeed = 2,
                c => c.SimulationStepSeconds = 0, c => c.MaxSimulatedSeconds = 0,
                c => c.HeavyWeightThresholdKg = double.NaN, c => c.WeightBaseMeanKg = double.PositiveInfinity,
                c => c.MeanInspectionSeconds = double.Epsilon,
                c => c.SimulationStepSeconds = double.Epsilon };
            foreach (var mutate in cases)
            { var c = new SimulationConfig(); mutate(c); Assert.Throws<ArgumentException>(() => SimulationConfiguration.Validate(c)); }
            SimulationConfiguration.Validate(new SimulationConfig { BlockageRatePerSecond = 0, FlagProbability = 0 });
            SimulationConfiguration.Validate(new SimulationConfig { FlagProbability = 1 });
        }
        [Test] public void StalledClockAndMaximumDurationStopWithExplicitError()
        {
            var e = new EngineProbe { Stall = true }; var s = new SimulationSession(e); s.Start(new SimulationConfig());
            Assert.Throws<InvalidOperationException>(() => s.Advance(1)); Assert.That(s.IsRunning, Is.False);
            e.Stall = false; s.Start(new SimulationConfig { MaxSimulatedSeconds = 0.3 });
            Assert.Throws<InvalidOperationException>(() => s.Advance(1)); Assert.That(s.IsRunning, Is.False);
        }
        [Test] public void InvalidPlaybackAndFrameValuesAreRejected()
        {
            var s = new SimulationSession(new EngineProbe()); s.Start(new SimulationConfig());
            Assert.Throws<ArgumentException>(() => s.SetPlaybackSpeed(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Advance(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Advance(1, 0));
        }
        [Test] public void ControllerWiresEventsAndExportsDetachedCopies()
        {
            var go = new GameObject("Integration test");
            try
            {
                var c = go.AddComponent<SimulationController>(); var e = new EngineProbe { CompleteAt = 0 }; var v = new Views();
                c.Bind(e, v, v, v);
                v.Start(new SimulationConfig { Seed = 7 }); Assert.That(v.Resets, Is.EqualTo(1)); Assert.That(v.Renders, Is.EqualTo(2));
                bool exported = false;
                c.BindExporter((config, bags, events) => { exported = true; config.Seed = 123; bags[0].WeightKg = 99; events[0].Detail = "changed"; });
                v.Export(); Assert.That(exported, Is.True); Assert.That(e.Bag.WeightKg, Is.EqualTo(18)); Assert.That(e.Event.Detail, Is.EqualTo("original"));
                v.Reset(); Assert.That(e.Config.Seed, Is.EqualTo(7)); Assert.That(v.Running, Is.False);
                v.Pause(); v.Resume(); v.Speed(5);
                v.Start(new SimulationConfig { FlagProbability = 9 }); Assert.That(v.Error, Does.Contain("FlagProbability"));
                c.BindExporter(null); v.Export(); Assert.That(v.Error, Does.Contain("M5"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [UnityTest] public IEnumerator RealPlayModeLifecyclePausesAndUnsubscribesWithoutDuplicates()
        {
            yield return new EnterPlayMode();
            var go = new GameObject("Play lifecycle test");
            try
            {
                var c = go.AddComponent<SimulationController>(); var e = new EngineProbe(); var v = new Views();
                c.Bind(e, v, v, v); v.Start(new SimulationConfig());
                Assert.That(v.Running, Is.True);
                go.SetActive(false);
                Assert.That(v.Running, Is.False);
                int resets = v.Resets; v.Reset(); Assert.That(v.Resets, Is.EqualTo(resets));
                go.SetActive(true); v.Reset(); Assert.That(v.Resets, Is.EqualTo(resets + 1));
                Assert.That(v.Running, Is.False);
                v.Resume(); Assert.That(v.Running, Is.True);
                go.SetActive(false); go.SetActive(true);
                v.Reset(); Assert.That(v.Resets, Is.EqualTo(resets + 2));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            yield return new ExitPlayMode();
        }
        [UnityTest] public IEnumerator BindingBeforeAwakeSurvivesActivationAndRebindingRemovesOldControls()
        {
            yield return new EnterPlayMode();
            var go = new GameObject("Pre-Awake composition test");
            go.SetActive(false);
            try
            {
                var c = go.AddComponent<SimulationController>(); var e = new EngineProbe(); var first = new Views();
                c.Bind(e, first, first, first);
                first.Start(new SimulationConfig()); Assert.That(e.Provider, Is.Null, "Inactive controller must not subscribe.");
                go.SetActive(true); // Awake must preserve explicit Bind, OnEnable subscribes once.
                first.Start(new SimulationConfig { Seed = 7 });
                Assert.That(e.Config.Seed, Is.EqualTo(7)); Assert.That(first.Resets, Is.EqualTo(1));
                var replacement = new EngineProbe(); var second = new Views();
                c.Bind(replacement, second, second, second);
                Assert.That(first.Running, Is.False, "Rebinding pauses and updates the previous controls.");
                first.Start(new SimulationConfig { Seed = 99 }); Assert.That(e.Config.Seed, Is.EqualTo(7));
                Assert.That(replacement.Provider, Is.Null);
                second.Start(new SimulationConfig { Seed = 8 });
                Assert.That(replacement.Config.Seed, Is.EqualTo(8)); Assert.That(second.Resets, Is.EqualTo(1));
                Assert.That(first.Resets, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            yield return new ExitPlayMode();
        }
        [Test] public void MainSimulationIsTheFirstEnabledBuildScene()
        {
            var scenes = UnityEditor.EditorBuildSettings.scenes;
            var first = Array.Find(scenes, scene => scene.enabled);
            Assert.That(first, Is.Not.Null);
            Assert.That(first.path, Is.EqualTo("Assets/Scenes/MainSimulation.unity"));
            Assert.That(UnityEditor.AssetDatabase.AssetPathToGUID(first.path), Is.EqualTo(first.guid.ToString()));
        }
        [Test] public void MissingEngineProducesClearErrorAndCanBeBoundLater()
        {
            var go = new GameObject("Missing engine test");
            try
            {
                var c = go.AddComponent<SimulationController>(); var v = new Views(); c.Bind(null, v, v, v);
                v.Start(new SimulationConfig()); Assert.That(v.Error, Does.Contain("M3"));
                c.Bind(new EngineProbe(), v, v, v); v.Start(new SimulationConfig()); Assert.That(v.Running, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [Test] public void MainSceneContainsControllerAndPreservesCameraAndLight()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainSimulation.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(Array.Find(roots, r => r.name == "Main Camera").GetComponent<Camera>(), Is.Not.Null);
                Assert.That(Array.Find(roots, r => r.name == "Global Light 2D"), Is.Not.Null);
                Assert.That(Array.Find(roots, r => r.name == "BAGFLOW Integration").GetComponent<SimulationController>(), Is.Not.Null);
                foreach (var root in roots)
                    foreach (var component in root.GetComponentsInChildren<Component>(true)) Assert.That(component, Is.Not.Null, "Missing script in scene");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
