using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator.Tests
{
    public sealed class PresentationViewTests
    {
        private BagflowUi ui;

        [SetUp] public void SetUp() { ui = BagflowUiFactory.Build(); }
        [TearDown] public void TearDown() { if (ui != null) Object.DestroyImmediate(ui.gameObject); }

        private static BagRecord Bag(int id, BagState state, double transport = 0, int queue = -1, double scan = 0, double done = 0)
        {
            return new BagRecord { Id = id, State = state, TransportProgress01 = transport, QueuePosition = queue,
                ScanProgress01 = scan, CompletionTimeSeconds = state == BagState.Approved || state == BagState.Flagged ? done : (double?)null };
        }

        private static SimulationSnapshot Snap(double time, bool blocked, params BagRecord[] bags)
        {
            var m = new SimulationMetrics();
            foreach (var b in bags)
            {
                if (b.State != BagState.Scheduled) m.Created++;
                if (b.State == BagState.Transporting) m.OnBelt++;
                if (b.State == BagState.ScannerQueue) m.QueueLength++;
                if (b.State == BagState.Scanning) m.Scanning++;
                if (b.State == BagState.Approved) { m.Approved++; m.Completed++; }
                if (b.State == BagState.Flagged) { m.Flagged++; m.Completed++; }
            }
            return new SimulationSnapshot { SimulationTimeSeconds = time, BeltBlocked = blocked, Metrics = m, Bags = new List<BagRecord>(bags) };
        }

        // ---- World -------------------------------------------------------------------------------------

        [Test] public void WorldAcceptsNullEmptyAndPartialSnapshots()
        {
            Assert.DoesNotThrow(() => ui.World.Render(null));
            Assert.DoesNotThrow(() => ui.World.Render(new SimulationSnapshot()));
            Assert.DoesNotThrow(() => ui.World.Render(SandboxScenarios.Build(0)));
            Assert.DoesNotThrow(() => ui.World.Render(new SimulationSnapshot { Bags = new List<BagRecord> { null } }));
            Assert.DoesNotThrow(() => ui.Dashboard.Render(null));
            Assert.DoesNotThrow(() => ui.Dashboard.Render(new SimulationSnapshot()));
            Assert.AreEqual(0, ui.World.ActiveBagCount);
        }

        [Test] public void ScheduledBagsAreNotRendered()
        {
            ui.World.Render(Snap(0, false, Bag(1, BagState.Scheduled), Bag(2, BagState.WaitingEntry)));
            Assert.AreEqual(1, ui.World.ActiveBagCount);
            BaggageSpriteView view;
            Assert.IsFalse(ui.World.TryGetView(1, out view));
            Assert.IsTrue(ui.World.TryGetView(2, out view));
        }

        [Test] public void ViewsAreCreatedRemovedAndReusedFromPool()
        {
            ui.World.Render(SandboxScenarios.Build(3));
            int visible = ui.World.ActiveBagCount;
            Assert.AreEqual(1 + 6 + 2, visible); // scanning + 6 visible queue slots + 2 on belt
            int created = ui.World.PoolCreatedCount;
            Assert.AreEqual(created, visible);
            ui.World.Render(SandboxScenarios.Build(0));
            Assert.AreEqual(0, ui.World.ActiveBagCount);
            ui.World.Render(SandboxScenarios.Build(3));
            Assert.AreEqual(created, ui.World.PoolCreatedCount, "released views must be reused, not re-instantiated");
            ui.World.ResetView();
            Assert.AreEqual(0, ui.World.ActiveBagCount);
        }

        [Test] public void OverflowOfTheQueueIsReportedNotDrawn()
        {
            ui.World.Render(SandboxScenarios.Build(3));
            Assert.AreEqual("Fila FIFO: 8", ui.World.Queue.Value);
            StringAssert.Contains("+2", ui.World.Queue.Detail);
        }

        [Test] public void SameBagKeepsItsViewAcrossStateTransitions()
        {
            BagState[] path = { BagState.WaitingEntry, BagState.Transporting, BagState.ScannerQueue, BagState.Scanning, BagState.Approved };
            BaggageSpriteView first = null;
            foreach (var state in path)
            {
                ui.World.Render(Snap(1, false, Bag(1, state, transport: 0.5, queue: state == BagState.ScannerQueue ? 0 : -1, scan: 0.3, done: 9)));
                BaggageSpriteView view;
                Assert.IsTrue(ui.World.TryGetView(1, out view), state.ToString());
                if (first == null) first = view;
                Assert.AreSame(first, view);
                Assert.AreEqual(state, view.State);
                Assert.AreEqual(UiTheme.StateColor(state), view.BodyColor);
            }
            Assert.AreEqual(1, ui.World.ActiveBagCount);
        }

        [Test] public void ApprovedAndFlaggedUseDistinctColoursAndLanes()
        {
            ui.World.Render(Snap(10, false, Bag(1, BagState.Approved, done: 5), Bag(2, BagState.Flagged, done: 6)));
            BaggageSpriteView a, f;
            ui.World.TryGetView(1, out a);
            ui.World.TryGetView(2, out f);
            Assert.AreNotEqual(a.BodyColor, f.BodyColor);
            Assert.Greater(a.Target.y, f.Target.y, "approved lane is above flagged lane");
            Assert.AreEqual("1", ui.World.ApprovedLane.Value);
            Assert.AreEqual("1", ui.World.FlaggedLane.Value);
        }

        [Test] public void ExitLanesKeepOnlyTheNewestCompletions()
        {
            var bags = new List<BagRecord>();
            for (int i = 1; i <= 20; i++) bags.Add(Bag(i, BagState.Approved, done: i));
            ui.World.Render(Snap(100, false, bags.ToArray()));
            Assert.AreEqual(WorldLayout.MaxExitVisible, ui.World.ActiveBagCount);
            BaggageSpriteView v;
            Assert.IsTrue(ui.World.TryGetView(20, out v));
            Assert.IsFalse(ui.World.TryGetView(1, out v));
            Assert.AreEqual("20", ui.World.ApprovedLane.Value, "counter comes from the engine metrics, not from visible bags");
        }

        [Test] public void QueueFollowsEngineFifoPositionAndShiftsWhenHeadStartsScanning()
        {
            float headX = ui.World.ToLayer(new Vector2(WorldLayout.QueueHeadX, 0)).x;
            ui.World.Render(Snap(1, false, Bag(1, BagState.ScannerQueue, 1, 0), Bag(2, BagState.ScannerQueue, 1, 1), Bag(3, BagState.ScannerQueue, 1, 2)));
            BaggageSpriteView b1, b2, b3;
            ui.World.TryGetView(1, out b1); ui.World.TryGetView(2, out b2); ui.World.TryGetView(3, out b3);
            Assert.AreEqual(headX, b1.Target.x, 0.01f);
            Assert.Greater(b1.Target.x, b2.Target.x);
            Assert.Greater(b2.Target.x, b3.Target.x);
            Assert.AreEqual(b1.Target.y, b3.Target.y, 0.001f);

            // The engine promotes bag 1; positions are re-reported as 0 and 1.
            ui.World.Render(Snap(2, false, Bag(1, BagState.Scanning, 1, -1, 0.1), Bag(2, BagState.ScannerQueue, 1, 0), Bag(3, BagState.ScannerQueue, 1, 1)));
            Assert.AreEqual(headX, b2.Target.x, 0.01f);
            Assert.Less(b3.Target.x, b2.Target.x);
            Assert.AreEqual(BagState.Scanning, b1.State);
            Assert.AreEqual(ui.World.ToLayer(WorldLayout.Scanner).x, b1.Target.x, 0.01f);
        }

        [Test] public void ScannerShowsBusyWithProgressThenFree()
        {
            ui.World.Render(Snap(1, false, Bag(1, BagState.Scanning, 1, -1, 0.5)));
            Assert.IsTrue(ui.World.Scanner.IsBusy);
            Assert.AreEqual(0.5f, ui.World.Scanner.Progress01, 0.0001f);
            Assert.AreEqual("OCUPADO", ui.World.Scanner.StatusText);
            BaggageSpriteView v;
            ui.World.TryGetView(1, out v);
            Assert.IsTrue(v.ProgressVisible);
            Assert.AreEqual(0.5f, v.Progress, 0.0001f);

            ui.World.Render(Snap(2, false, Bag(1, BagState.Approved, done: 2)));
            Assert.IsFalse(ui.World.Scanner.IsBusy);
            Assert.AreEqual("LIVRE", ui.World.Scanner.StatusText);
            Assert.IsFalse(v.ProgressVisible);
        }

        [Test] public void TransportingBagIsPlacedByEngineProgress()
        {
            ui.World.Render(Snap(1, false, Bag(1, BagState.Transporting, 0.25)));
            BaggageSpriteView v;
            ui.World.TryGetView(1, out v);
            Vector2 expected = ui.World.ToLayer(Vector2.Lerp(WorldLayout.BeltStart, WorldLayout.BeltEnd, 0.25f));
            Assert.AreEqual(expected.x, v.Target.x, 0.01f);
            Assert.AreEqual(expected.y, v.Target.y, 0.01f);
        }

        [Test] public void BlockageFreezesBeltAndBagsThenResumes()
        {
            ui.World.Render(Snap(10, false, Bag(1, BagState.Transporting, 0.4)));
            float running = ui.World.Belt.TargetOffset;
            ui.World.Render(Snap(11, false, Bag(1, BagState.Transporting, 0.5)));
            Assert.Greater(ui.World.Belt.TargetOffset, running);
            Assert.IsFalse(ui.World.Belt.IsBlocked);

            ui.World.Render(Snap(12, true, Bag(1, BagState.Transporting, 0.5)));
            BaggageSpriteView v;
            ui.World.TryGetView(1, out v);
            Assert.IsTrue(ui.World.Belt.IsBlocked);
            float frozenOffset = ui.World.Belt.TargetOffset;
            Vector2 frozenTarget = v.Target;
            for (int i = 0; i < 50; i++) ui.World.Animate(0.1f);
            ui.World.Render(Snap(16, true, Bag(1, BagState.Transporting, 0.5)));
            Assert.AreEqual(frozenOffset, ui.World.Belt.TargetOffset, 1e-6f, "stripes must not advance while blocked");
            Assert.AreEqual(frozenTarget, v.Target);
            Assert.AreEqual(frozenTarget.x, v.Position.x, 0.05f, "bag stops moving while blocked");

            ui.World.Render(Snap(18, false, Bag(1, BagState.Transporting, 0.6)));
            Assert.IsFalse(ui.World.Belt.IsBlocked);
            Assert.Greater(ui.World.Belt.TargetOffset, frozenOffset, "motion resumes only for time after the blockage");
            Assert.Greater(v.Target.x, frozenTarget.x);
        }

        [Test] public void ResetRewindsBeltClockWithoutNegativeMotion()
        {
            ui.World.Render(Snap(50, false, Bag(1, BagState.Transporting, 0.9)));
            ui.World.ResetView();
            ui.World.Render(Snap(0, false, Bag(1, BagState.Transporting, 0.0)));
            Assert.GreaterOrEqual(ui.World.Belt.TargetOffset, 0f);
            Assert.AreEqual(0f, ui.World.Belt.TargetOffset, 1e-6f);
        }

        [TestCase(1, 1)]
        [TestCase(60, 7)]
        [TestCase(500, 8)]
        public void ScalesToLargeRunsWithBoundedVisuals(int n, int scenario)
        {
            var snapshot = n == 1 ? SandboxScenarios.Build(1) : n == 60 ? SandboxScenarios.Mix(60, 0.5, false, false) : SandboxScenarios.Build(scenario);
            ui.World.Render(snapshot);
            ui.Dashboard.Render(snapshot);
            Assert.AreEqual(n.ToString(), ui.Dashboard.TotalText);
            int bound = WorldLayout.MaxWaitingVisible + WorldLayout.MaxQueueVisible + 1 + 40 + 2 * WorldLayout.MaxExitVisible;
            Assert.LessOrEqual(ui.World.ActiveBagCount, bound);
            ui.World.Render(snapshot);
            int created = ui.World.PoolCreatedCount;
            for (int i = 0; i < 20; i++) ui.World.Render(snapshot);
            Assert.AreEqual(created, ui.World.PoolCreatedCount, "re-rendering must not instantiate");
        }

        [Test] public void EveryScenarioKeepsBagsInsideTheWorld()
        {
            for (int s = 0; s < SandboxScenarios.Count; s++)
            {
                ui.World.ResetView();
                ui.World.Render(SandboxScenarios.Build(s));
                Vector2 max = ui.World.ToLayer(Vector2.one);
                foreach (var state in new[] { BagState.WaitingEntry, BagState.Transporting, BagState.ScannerQueue, BagState.Scanning, BagState.Approved, BagState.Flagged })
                    Assert.GreaterOrEqual(ui.World.CountActive(state), 0);
                for (int id = 1; id <= 500; id++)
                {
                    BaggageSpriteView v;
                    if (!ui.World.TryGetView(id, out v)) continue;
                    Assert.That(v.Target.x, Is.InRange(0f, max.x), "scenario " + s + " bag " + id);
                    Assert.That(v.Target.y, Is.InRange(0f, max.y), "scenario " + s + " bag " + id);
                }
            }
        }

        [Test] public void AnimationMovesViewsTowardTargetsAndStops()
        {
            ui.World.Render(Snap(1, false, Bag(1, BagState.WaitingEntry)));
            BaggageSpriteView v;
            ui.World.TryGetView(1, out v);
            ui.World.Render(Snap(2, false, Bag(1, BagState.Transporting, 0.8)));
            Vector2 before = v.Position;
            ui.World.Animate(0.05f);
            Assert.Greater(v.Position.x, before.x);
            for (int i = 0; i < 100; i++) ui.World.Animate(0.1f);
            Assert.AreEqual(v.Target.x, v.Position.x, 0.01f);
        }

        // ---- Dashboard ---------------------------------------------------------------------------------

        [Test] public void DashboardShowsEngineMetricsWithoutRecomputing()
        {
            var snapshot = SandboxScenarios.Build(6);
            snapshot.Metrics.MeanQueueWaitSeconds = 12.34;
            snapshot.Metrics.MeanTotalSeconds = 45.6;
            snapshot.Metrics.ScannerUtilization = 0.4321;
            snapshot.Metrics.TotalBlockedSeconds = 16;
            snapshot.Metrics.BlockageOccurrences = 2;
            snapshot.SimulationTimeSeconds = 75.5;
            ui.Dashboard.Render(snapshot);
            Assert.AreEqual("01:15.5", ui.Dashboard.SimulatedTimeText);
            Assert.AreEqual("5", ui.Dashboard.TotalText);
            Assert.AreEqual(snapshot.Metrics.Completed.ToString(), ui.Dashboard.CompletedText);
            Assert.AreEqual(snapshot.Metrics.Approved.ToString(), ui.Dashboard.ApprovedText);
            Assert.AreEqual(snapshot.Metrics.Flagged.ToString(), ui.Dashboard.FlaggedText);
            Assert.AreEqual("0", ui.Dashboard.QueueText);
            Assert.AreEqual("Livre", ui.Dashboard.ScannerText);
            Assert.AreEqual("2", ui.Dashboard.BlockageText);
            Assert.AreEqual("16.0 s", ui.Dashboard.BlockedTimeText);
            Assert.AreEqual("12.3 s", ui.Dashboard.MeanWaitText);
            Assert.AreEqual("45.6 s", ui.Dashboard.MeanTotalText);
            Assert.AreEqual("43.2 %", ui.Dashboard.UtilizationText);
        }

        [Test] public void DashboardScannerAndStatusFollowSnapshot()
        {
            ui.Dashboard.Render(null);
            Assert.AreEqual("PRONTO", ui.Dashboard.Status);
            ui.Dashboard.Render(SandboxScenarios.Build(3));
            Assert.AreEqual("Ocupado", ui.Dashboard.ScannerText);
            Assert.AreEqual("EM SIMULAÇÃO", ui.Dashboard.Status);
            ui.Dashboard.Render(SandboxScenarios.Build(4));
            Assert.AreEqual("TAPETE BLOQUEADO", ui.Dashboard.Status);
            ui.Dashboard.Render(SandboxScenarios.Build(9));
            Assert.AreEqual("CONCLUÍDA", ui.Dashboard.Status);
        }

        [Test] public void DashboardErrorsAndNoticesAreVisibleThenExpire()
        {
            ui.Dashboard.ShowError("Falha de configuração");
            Assert.AreEqual("Falha de configuração", ui.Dashboard.BannerMessage);
            Assert.IsTrue(ui.Dashboard.BannerIsError);
            ui.Dashboard.Tick(11f);
            Assert.AreEqual(string.Empty, ui.Dashboard.BannerMessage);
            ui.Dashboard.ShowInfo("Exportado");
            Assert.IsFalse(ui.Dashboard.BannerIsError);
            ui.Dashboard.ShowError("");
            Assert.AreEqual(string.Empty, ui.Dashboard.BannerMessage);
        }

        [Test] public void DashboardFormatsClock()
        {
            Assert.AreEqual("00:00.0", DashboardView.FormatClock(0));
            Assert.AreEqual("01:05.9", DashboardView.FormatClock(65.99));
            Assert.AreEqual("1:01:01", DashboardView.FormatClock(3661));
            Assert.AreEqual("00:00.0", DashboardView.FormatClock(double.NaN));
        }

        // ---- Controls ----------------------------------------------------------------------------------

        private sealed class Recorder
        {
            public readonly List<SimulationConfig> Starts = new List<SimulationConfig>();
            public int Pauses, Resumes, Resets, Exports;
            public readonly List<double> Speeds = new List<double>();
            public Recorder(ISimulationControlView v)
            {
                v.StartRequested += c => Starts.Add(c);
                v.PauseRequested += () => Pauses++;
                v.ResumeRequested += () => Resumes++;
                v.ResetRequested += () => Resets++;
                v.PlaybackSpeedRequested += s => Speeds.Add(s);
                v.ExportRequested += () => Exports++;
            }
        }

        [Test] public void DefaultFormMatchesContractDefaultsExactly()
        {
            SimulationConfig config;
            string error;
            Assert.IsTrue(ui.Controls.TryReadConfig(out config, out error), error);
            var expected = new SimulationConfig();
            foreach (var field in typeof(SimulationConfig).GetFields())
                Assert.AreEqual(field.GetValue(expected), field.GetValue(config), field.Name);
        }

        [Test] public void StartEmitsConfigurationFromTheForm()
        {
            var rec = new Recorder(ui.Controls);
            ui.Controls.Fields.BaggageCount.text = "12";
            ui.Controls.Fields.Seed.text = "7";
            ui.Controls.Fields.FlagProbability.text = "0,25";
            ui.Controls.Fields.BlockageInterval.text = "0";
            ui.Controls.ToggleArrivalMode();
            ui.Controls.SelectSpeed(20);
            ui.Controls.ClickStart();
            Assert.AreEqual(1, rec.Starts.Count);
            var c = rec.Starts[0];
            Assert.AreEqual(12, c.BaggageCount);
            Assert.AreEqual(7, c.Seed);
            Assert.AreEqual(0.25, c.FlagProbability, 1e-12);
            Assert.AreEqual(0.0, c.BlockageRatePerSecond);
            Assert.AreEqual(ArrivalMode.Exponential, c.ArrivalMode);
            Assert.AreEqual(20.0, c.PlaybackSpeed);
            Assert.AreEqual(0, rec.Speeds.Count, "speed is part of the start config before a run exists");
        }

        [Test] public void UnreadableFieldsShowReadableErrorAndDoNotStart()
        {
            var rec = new Recorder(ui.Controls);
            ui.Controls.Fields.BaggageCount.text = "abc";
            ui.Controls.ClickStart();
            Assert.AreEqual(0, rec.Starts.Count);
            StringAssert.Contains("Quantidade de malas", ui.Controls.ErrorText);
            ui.Controls.Fields.BaggageCount.text = "5";
            ui.Controls.Fields.MeanInspection.text = "x";
            ui.Controls.ClickStart();
            StringAssert.Contains("Inspeção média", ui.Controls.ErrorText);
            ui.Controls.Fields.MeanInspection.text = "8";
            ui.Controls.ClickStart();
            Assert.AreEqual(1, rec.Starts.Count);
            Assert.AreEqual(string.Empty, ui.Controls.ErrorText);
            ui.Controls.ShowValidationError("Controlador: valor inválido");
            Assert.AreEqual("Controlador: valor inválido", ui.Controls.ErrorText);
        }

        [Test] public void PauseResumeResetSpeedAndExportEmitContractEvents()
        {
            var rec = new Recorder(ui.Controls);
            ui.Controls.ClickPauseResume();
            Assert.AreEqual(0, rec.Pauses + rec.Resumes, "nothing to pause before a run");
            Assert.IsFalse(ui.Controls.PauseResumeInteractable);
            Assert.IsFalse(ui.Controls.ExportInteractable);

            ui.Controls.SetRunning(true);
            Assert.AreEqual("Pausar", ui.Controls.PauseResumeCaption);
            Assert.IsFalse(ui.Controls.StartInteractable);
            ui.Controls.ClickPauseResume();
            Assert.AreEqual(1, rec.Pauses);

            ui.Controls.SetRunning(false);
            Assert.AreEqual("Retomar", ui.Controls.PauseResumeCaption);
            ui.Controls.ClickPauseResume();
            Assert.AreEqual(1, rec.Resumes);

            ui.Controls.SelectSpeed(5);
            Assert.AreEqual(new List<double> { 5 }, rec.Speeds);
            ui.Controls.SelectSpeed(3);
            Assert.AreEqual(1, rec.Speeds.Count, "only 1, 5, 10 and 20 are accepted");
            StringAssert.Contains("inválida", ui.Controls.ErrorText);

            ui.Controls.ClickReset();
            Assert.AreEqual(1, rec.Resets);

            ui.Controls.NotifySnapshot(SandboxScenarios.Build(9));
            Assert.IsTrue(ui.Controls.ExportInteractable);
            Assert.IsFalse(ui.Controls.PauseResumeInteractable, "completed runs cannot be resumed");
            ui.Controls.ClickExport();
            Assert.AreEqual(1, rec.Exports);
            ui.Controls.NotifySnapshot(SandboxScenarios.Build(0));
            Assert.IsFalse(ui.Controls.ExportInteractable);
        }

        [Test] public void ExportResultAndAdvancedPanelAreToggleable()
        {
            ui.Controls.ShowExportResult(true, "Exportado para: x.csv");
            Assert.AreEqual("Exportado para: x.csv", ui.Controls.ExportText);
            ui.Controls.ShowExportResult(false, "Erro de I/O");
            Assert.AreEqual("Erro de I/O", ui.Controls.ExportText);
            Assert.IsFalse(ui.Controls.AdvancedOpen);
            ui.Controls.ToggleAdvanced();
            Assert.IsTrue(ui.Controls.AdvancedOpen);
            ui.Controls.Fields.Seed.text = "99";
            ui.Controls.ApplyDefaults();
            Assert.AreEqual("42", ui.Controls.Fields.Seed.text);
        }

        [Test] public void ThreeContractViewsCanBeDraggedWithoutAmbiguity()
        {
            // Each view sits on its own graphic-free object, so dropping the GameObject on a MonoBehaviour field is unambiguous.
            foreach (Transform t in new[] { ui.World.transform, ui.Dashboard.transform, ui.Controls.transform })
            {
                int behaviours = 0;
                foreach (var b in t.GetComponents<MonoBehaviour>()) behaviours++;
                Assert.AreEqual(1, behaviours, t.name);
                Assert.IsNull(t.GetComponent<Graphic>(), t.name);
            }
            Assert.IsInstanceOf<ISimulationWorldView>(ui.World);
            Assert.IsInstanceOf<ISimulationDashboardView>(ui.Dashboard);
            Assert.IsInstanceOf<ISimulationControlView>(ui.Controls);
        }
    }
}
