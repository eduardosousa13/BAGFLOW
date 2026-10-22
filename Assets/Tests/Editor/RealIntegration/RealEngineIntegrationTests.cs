using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AirportBaggageSimulator.Tests
{
    public sealed class RealEngineIntegrationTests
    {
        // Receivers/control emitters only. The engine, session and probability streams are real.
        private sealed class TestViews : ISimulationWorldView, ISimulationDashboardView, ISimulationControlView
        {
            public event Action<SimulationConfig> StartRequested;
            public event Action PauseRequested;
            public event Action ResumeRequested;
            public event Action ResetRequested;
            public event Action<double> PlaybackSpeedRequested;
            public event Action ExportRequested;
            public SimulationSnapshot Last;
            public bool Running;
            public string Error;
            public int Resets;
            public void Render(SimulationSnapshot s) { Last = s; }
            public void ResetView() { Resets++; Last = null; }
            public void ShowError(string message) { Error = message; }
            public void ShowValidationError(string message) { Error = message; }
            public void SetRunning(bool running) { Running = running; }
            public void Start(SimulationConfig c) { StartRequested?.Invoke(c); }
            public void Pause() { PauseRequested?.Invoke(); }
            public void Resume() { ResumeRequested?.Invoke(); }
            public void Reset() { ResetRequested?.Invoke(); }
            public void Speed(double value) { PlaybackSpeedRequested?.Invoke(value); }
            public void Export() { ExportRequested?.Invoke(); }
        }

        private sealed class ComposedRun : IDisposable
        {
            public readonly GameObject Object;
            public readonly SimulationComposition Composition;
            public readonly SimulationController Controller;
            public readonly TestViews Views = new TestViews();
            public ISimulationEngine Engine { get { return Composition.Engine; } }
            public ComposedRun()
            {
                Object = new GameObject("Real BAGFLOW integration test");
                Composition = Object.AddComponent<SimulationComposition>();
                Controller = Object.GetComponent<SimulationController>();
                Composition.Compose(Views, Views, Views);
                Assert.That(Engine, Is.TypeOf<SimulationEngine>());
            }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(Object); }
            public void Finish(double frameSeconds)
            {
                for (int frame = 0; frame < 20000 && Views.Running; frame++)
                {
                    Controller.Advance(frameSeconds);
                    Assert.That(Views.Error, Is.Null);
                    AssertLive(Views.Last);
                }
                Assert.That(Engine.IsComplete, Is.True, "Real engine did not complete within frame budget.");
                Assert.That(Views.Running, Is.False);
            }
        }

        private static void AssertLive(SimulationSnapshot s)
        {
            Assert.That(s, Is.Not.Null);
            var m = s.Metrics;
            Assert.That(m.Created, Is.EqualTo(m.Completed + m.WaitingEntry + m.OnBelt + m.QueueLength + m.Scanning));
            Assert.That(m.Completed, Is.EqualTo(m.Approved + m.Flagged));
            Assert.That(m.Created, Is.InRange(m.Completed, s.Bags.Count));
            Assert.That(m.Scanning, Is.InRange(0, 1));
            Assert.That(s.Bags.Count(b => b.State == BagState.Scanning), Is.EqualTo(m.Scanning));
            Assert.That(s.Bags.Where(b => b.State == BagState.ScannerQueue).Select(b => b.QueuePosition).OrderBy(x => x),
                Is.EqualTo(Enumerable.Range(0, m.QueueLength)));
        }

        private static void AssertFinal(ISimulationEngine engine, int n)
        {
            var s = engine.GetSnapshot(); AssertLive(s);
            Assert.That(s.IsComplete, Is.True); Assert.That(s.Metrics.Completed, Is.EqualTo(n));
            Assert.That(s.Metrics.Created, Is.EqualTo(n));
            Assert.That(s.Bags.Select(b => b.Id).OrderBy(x => x), Is.EqualTo(Enumerable.Range(1, n)));
            Assert.That(s.Bags.All(b => b.State == BagState.Approved || b.State == BagState.Flagged), Is.True);
            Assert.That(s.Bags.All(b => b.TransportProgress01 == 1 && b.ScanProgress01 == 1 && b.QueuePosition == -1), Is.True);
            Assert.That(s.Metrics.Flagged, Is.EqualTo(s.Bags.Count(b => b.IsFlagged == true)));
            Assert.That(s.Metrics.MeanQueueWaitSeconds, Is.EqualTo(s.Bags.Average(b => b.ScanStartTimeSeconds.Value - b.QueueEnterTimeSeconds.Value)).Within(1e-9));
            Assert.That(s.Metrics.MeanTotalSeconds, Is.EqualTo(s.Bags.Average(b => b.CompletionTimeSeconds.Value - b.ArrivalTimeSeconds)).Within(1e-9));

            var queue = new Queue<int>();
            var arrivals = new HashSet<int>(); var terminal = new HashSet<int>();
            int? scanning = null; double scanStart = 0, busy = 0, blockStart = 0, blocked = 0;
            bool beltBlocked = false; int occurrences = 0, runCompleted = 0;
            double previousTime = 0, previousOccurrence = 0;
            foreach (var e in engine.GetEventRecords())
            {
                Assert.That(e.SimulationTimeSeconds, Is.GreaterThanOrEqualTo(previousTime)); previousTime = e.SimulationTimeSeconds;
                switch (e.EventType)
                {
                    case SimulationEventType.BagArrived: Assert.That(arrivals.Add(e.BagId.Value), Is.True); break;
                    case SimulationEventType.ScanQueued: queue.Enqueue(e.BagId.Value); break;
                    case SimulationEventType.ScanStarted:
                        Assert.That(scanning, Is.Null, "Two simultaneous inspections.");
                        Assert.That(queue.Count, Is.GreaterThan(0));
                        Assert.That(queue.Dequeue(), Is.EqualTo(e.BagId.Value), "Scanner violated event FIFO.");
                        scanning = e.BagId; scanStart = e.SimulationTimeSeconds; break;
                    case SimulationEventType.ScanFinished:
                        Assert.That(scanning, Is.EqualTo(e.BagId)); scanning = null;
                        busy += e.SimulationTimeSeconds - scanStart; break;
                    case SimulationEventType.BagApproved:
                    case SimulationEventType.BagFlagged: Assert.That(terminal.Add(e.BagId.Value), Is.True); break;
                    case SimulationEventType.BlockageOccurrence:
                        occurrences++;
                        Assert.That(e.ScheduledTimeSeconds.Value, Is.GreaterThan(previousOccurrence));
                        Assert.That(e.ScheduledTimeSeconds.Value, Is.LessThanOrEqualTo(e.SimulationTimeSeconds));
                        previousOccurrence = e.ScheduledTimeSeconds.Value; break;
                    case SimulationEventType.BlockageStarted:
                        Assert.That(beltBlocked, Is.False); beltBlocked = true; blockStart = e.SimulationTimeSeconds; break;
                    case SimulationEventType.BlockageEnded:
                        Assert.That(beltBlocked, Is.True); beltBlocked = false; blocked += e.SimulationTimeSeconds - blockStart; break;
                    case SimulationEventType.RunCompleted: runCompleted++; break;
                }
            }
            if (beltBlocked) blocked += s.SimulationTimeSeconds - blockStart;
            Assert.That(arrivals.Count, Is.EqualTo(n)); Assert.That(terminal.Count, Is.EqualTo(n));
            Assert.That(queue, Is.Empty); Assert.That(scanning, Is.Null); Assert.That(runCompleted, Is.EqualTo(1));
            Assert.That(s.Metrics.BlockageOccurrences, Is.EqualTo(occurrences));
            Assert.That(s.Metrics.TotalBlockedSeconds, Is.EqualTo(blocked).Within(1e-7));
            Assert.That(s.Metrics.ScannerUtilization, Is.EqualTo(busy / s.SimulationTimeSeconds).Within(1e-9));
        }

        private static void AppendFields(StringBuilder text, object record)
        {
            foreach (var field in record.GetType().GetFields().OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                var value = field.GetValue(record);
                text.Append(field.Name).Append('=');
                if (value is double) text.Append(((double)value).ToString("R", CultureInfo.InvariantCulture));
                else text.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                text.Append(';');
            }
            text.AppendLine();
        }
        private static string History(ISimulationEngine engine)
        {
            var text = new StringBuilder();
            foreach (var bag in engine.GetBagRecords()) AppendFields(text, bag);
            foreach (var e in engine.GetEventRecords()) AppendFields(text, e);
            var s = engine.GetSnapshot(); AppendFields(text, s.Metrics);
            text.Append(s.SimulationTimeSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(s.IsComplete).Append(s.BeltBlocked);
            return text.ToString();
        }

        [TestCase(1, ArrivalMode.Constant)] [TestCase(60, ArrivalMode.Constant)] [TestCase(500, ArrivalMode.Constant)]
        [TestCase(1, ArrivalMode.Exponential)] [TestCase(60, ArrivalMode.Exponential)] [TestCase(500, ArrivalMode.Exponential)]
        public void RealCompositionCompletesAndResetAt20xReproducesThe1xHistory(int n, ArrivalMode mode)
        {
            using (var run = new ComposedRun())
            {
                var c = new SimulationConfig { BaggageCount = n, ArrivalMode = mode, PlaybackSpeed = 1 };
                run.Views.Start(c); Assert.That(run.Views.Error, Is.Null);
                string initial = History(run.Engine);
                run.Controller.Advance(.35);
                string paused = History(run.Engine);
                run.Views.Pause(); run.Controller.Advance(1000);
                Assert.That(History(run.Engine), Is.EqualTo(paused)); Assert.That(run.Views.Running, Is.False);
                run.Views.Resume(); run.Finish(2);
                AssertFinal(run.Engine, n); string at1x = History(run.Engine);
                run.Controller.Advance(100); Assert.That(History(run.Engine), Is.EqualTo(at1x));
                // Editing the caller's config must not change the original seed used by Reset.
                c.Seed = -123; c.BaggageCount = 2;
                run.Views.Reset(); Assert.That(run.Views.Running, Is.False);
                Assert.That(History(run.Engine), Is.EqualTo(initial));
                run.Views.Speed(20); run.Views.Resume(); run.Finish(.137);
                AssertFinal(run.Engine, n);
                Assert.That(History(run.Engine), Is.EqualTo(at1x), "Playback changed logical records, events or metrics.");
                Assert.That(run.Views.Resets, Is.EqualTo(2));
            }
        }

        [Test] public void DirectSessionAndComposedControllerAgreeWithTheSameSeed()
        {
            var config = new SimulationConfig { ArrivalMode = ArrivalMode.Exponential, PlaybackSpeed = 20 };
            var engine = new SimulationEngine(); var session = new SimulationSession(engine); session.Start(config);
            for (int frame = 0; frame < 20000 && session.IsRunning; frame++) session.Advance(1, 7);
            Assert.That(engine.IsComplete, Is.True); AssertFinal(engine, 60);
            using (var run = new ComposedRun())
            {
                run.Views.Start(config); run.Finish(.731);
                Assert.That(History(run.Engine), Is.EqualTo(History(engine)));
            }
        }

        [TestCase(0)] [TestCase(1)] public void RealBlockagesAndIndividualDecisionsRemainConsistent(double p)
        {
            using (var run = new ComposedRun())
            {
                var c = new SimulationConfig { BaggageCount = 60, FlagProbability = p, PlaybackSpeed = 20,
                    MeanArrivalIntervalSeconds = .02, MeanInspectionSeconds = .5,
                    BlockageRatePerSecond = 1, BlockageDurationSeconds = 2 };
                run.Views.Start(c); run.Finish(.5); AssertFinal(run.Engine, 60);
                var s = run.Engine.GetSnapshot();
                Assert.That(s.Metrics.Flagged, Is.EqualTo(p == 1 ? 60 : 0));
                Assert.That(s.Metrics.TotalBlockedSeconds, Is.GreaterThan(0));
                var log = run.Engine.GetEventRecords();
                Assert.That(s.Metrics.BlockageOccurrences, Is.GreaterThan(log.Count(e => e.EventType == SimulationEventType.BlockageStarted)));
                var blocks = log.Where(e => e.EventType == SimulationEventType.BlockageStarted).Select(e => e.SimulationTimeSeconds).ToArray();
                Assert.That(s.Bags.Any(b => blocks.Any(t => t > b.ScanStartTimeSeconds.Value && t < b.CompletionTimeSeconds.Value)), Is.True,
                    "The real scanner should have served a bag across a blockage start.");
            }
        }

        [Test] public void RealEngineErrorIsReportedAndInvalidStartDoesNotReplaceAnActiveRun()
        {
            using (var run = new ComposedRun())
            {
                run.Views.Start(new SimulationConfig()); run.Controller.Advance(.5);
                string current = History(run.Engine);
                run.Views.Start(new SimulationConfig { BaggageCount = 0 });
                Assert.That(run.Views.Error, Does.Contain("BaggageCount"));
                Assert.That(History(run.Engine), Is.EqualTo(current)); Assert.That(run.Views.Running, Is.True);
                run.Views.Error = null;
                run.Views.Start(new SimulationConfig { MaxSimulatedSeconds = .1 }); run.Controller.Advance(1);
                Assert.That(run.Views.Error, Does.Contain("duration")); Assert.That(run.Views.Running, Is.False);
                Assert.That(run.Engine.IsComplete, Is.False);
            }
        }

        [Test] public void ExportHookReceivesRealFinalCopiesAndReportsIoFailure()
        {
            using (var run = new ComposedRun())
            {
                run.Views.Start(new SimulationConfig { BaggageCount = 1, PlaybackSpeed = 20 });
                run.Views.Export(); Assert.That(run.Views.Error, Does.Contain("Complete"));
                Assert.That(run.Views.Running, Is.False); run.Views.Error = null; run.Views.Resume();
                run.Finish(1); string history = History(run.Engine);
                bool received = false; string shownPath = null;
                run.Controller.ExportCompleted += path => shownPath = path;
                run.Controller.BindExporter((config, bags, events) =>
                {
                    received = true; Assert.That(config.Seed, Is.EqualTo(42));
                    Assert.That(bags.Count, Is.EqualTo(1)); Assert.That(bags[0].CompletionTimeSeconds.HasValue, Is.True);
                    Assert.That(events.Any(e => e.EventType == SimulationEventType.RunCompleted), Is.True);
                    config.Seed = 0; bags[0].WeightKg = -1; events[0].Detail = "modified";
                    run.Controller.NotifyExportCompleted("test-only/result");
                });
                run.Views.Export(); Assert.That(run.Views.Error, Is.Null);
                Assert.That(received, Is.True); Assert.That(shownPath, Is.EqualTo("test-only/result"));
                Assert.That(History(run.Engine), Is.EqualTo(history));
                run.Controller.BindExporter((c, b, e) => { throw new System.IO.IOException("test I/O error"); });
                run.Views.Export(); Assert.That(run.Views.Error, Is.EqualTo("test I/O error"));
                Assert.That(History(run.Engine), Is.EqualTo(history));
            }
        }

        [Test] public void MainSceneContainsTheRealCompositionWithoutMissingScripts()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainSimulation.unity", OpenSceneMode.Additive);
            try
            {
                var root = scene.GetRootGameObjects().Single(go => go.name == "BAGFLOW Integration");
                Assert.That(root.GetComponent<SimulationComposition>(), Is.Not.Null);
                Assert.That(root.GetComponent<SimulationController>(), Is.Not.Null);
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var component in go.GetComponentsInChildren<Component>(true)) Assert.That(component, Is.Not.Null);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [UnityTest] public IEnumerator ExplicitCompositionBeforeActivationSurvivesAwakeAndStart()
        {
            yield return new EnterPlayMode();
            var go = new GameObject("Inactive real composition"); go.SetActive(false);
            try
            {
                var composition = go.AddComponent<SimulationComposition>(); var views = new TestViews();
                composition.Compose(views, views, views); var engine = composition.Engine;
                go.SetActive(true);
                yield return null;
                Assert.That(composition.Engine, Is.SameAs(engine), "Start replaced explicit real composition.");
                views.Start(new SimulationConfig { BaggageCount = 1, PlaybackSpeed = 20 });
                Assert.That(views.Running, Is.True); Assert.That(views.Error, Is.Null);
                var controller = go.GetComponent<SimulationController>();
                for (int frame = 0; frame < 100 && views.Running; frame++) controller.Advance(1);
                Assert.That(engine.IsComplete, Is.True); AssertFinal(engine, 1);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator MainSceneStartComposesTheRealEngineAndUpdateConsumesTheRealClock()
        {
            yield return new EnterPlayMode();
            var load = SceneManager.LoadSceneAsync("Assets/Scenes/MainSimulation.unity", LoadSceneMode.Additive);
            yield return load;
            yield return null; // Allow the composition's real Start to run.
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/MainSimulation.unity");
            var root = scene.GetRootGameObjects().Single(go => go.name == "BAGFLOW Integration");
            try
            {
                var composition = root.GetComponent<SimulationComposition>();
                Assert.That(composition.Engine, Is.TypeOf<SimulationEngine>());
                var views = new TestViews();
                composition.Compose(views, views, views); // Future M4 hooks; no fake engine or sampler.
                var controller = root.GetComponent<SimulationController>();
                views.Start(new SimulationConfig { BaggageCount = 1, PlaybackSpeed = 20 });
                yield return new WaitForSecondsRealtime(.1f);
                Assert.That(composition.Engine.GetSnapshot().SimulationTimeSeconds, Is.GreaterThan(0));
                views.Pause(); double pausedAt = composition.Engine.GetSnapshot().SimulationTimeSeconds;
                yield return null;
                Assert.That(composition.Engine.GetSnapshot().SimulationTimeSeconds, Is.EqualTo(pausedAt));
                views.Resume();
                for (int frame = 0; frame < 100 && views.Running; frame++) controller.Advance(1);
                Assert.That(views.Error, Is.Null); Assert.That(composition.Engine.IsComplete, Is.True);
                AssertFinal(composition.Engine, 1);
            }
            finally { root.SetActive(false); }
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return new ExitPlayMode();
        }
    }
}
