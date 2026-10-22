using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AirportBaggageSimulator.Tests
{
    /// <summary>Play Mode tests: component lifecycle and the BAGFLOW_UI wired to the real controller/composition/engine
    /// (no fake engine). These verify behaviour, not appearance: a human inspection of the screen is separate.</summary>
    public sealed class PresentationIntegrationTests
    {
        private static void Cleanup(params Object[] objects)
        {
            foreach (var o in objects) if (o != null) Object.DestroyImmediate(o);
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None)) Object.DestroyImmediate(es.gameObject);
        }

        [UnityTest] public IEnumerator EnableDisableCyclesDoNotDuplicateSubscriptions()
        {
            yield return new EnterPlayMode();
            var ui = BagflowUiFactory.Build();
            try
            {
                yield return null;
                int starts = 0;
                ui.Controls.StartRequested += c => starts++;
                for (int i = 0; i < 4; i++) { ui.gameObject.SetActive(false); ui.gameObject.SetActive(true); }
                ui.Controls.gameObject.SetActive(false);
                ui.Controls.gameObject.SetActive(true);

                ui.Dashboard.Render(SandboxScenarios.Build(9));
                Assert.AreEqual(1, ui.Controls.SnapshotNotifications, "snapshot forwarded once despite re-enabling");

                ui.transform.Find("Controls/Start").GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(1, starts, "one click, one StartRequested");
                Assert.IsNotNull(Object.FindFirstObjectByType<EventSystem>());
                Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
            }
            finally { Cleanup(ui.gameObject); }
            yield return new ExitPlayMode();
        }

        [UnityTest] public IEnumerator RealEngineDrivesTheUiThroughControllerAndComposition()
        {
            yield return new EnterPlayMode();
            var ui = BagflowUiFactory.Build();
            var host = new GameObject("Integration");
            host.SetActive(false);
            var controller = host.AddComponent<SimulationController>();
            var composition = host.AddComponent<SimulationComposition>();
            var so = new SerializedObject(composition);
            so.FindProperty("worldViewComponent").objectReferenceValue = ui.World;
            so.FindProperty("dashboardViewComponent").objectReferenceValue = ui.Dashboard;
            so.FindProperty("controlViewComponent").objectReferenceValue = ui.Controls;
            so.ApplyModifiedPropertiesWithoutUndo();
            host.SetActive(true);
            try
            {
                yield return null; // composition Start
                Assert.IsNotNull(composition.Engine);
                foreach (int n in new[] { 1, 60, 500 })
                    RunToCompletion(ui, controller, n);

                // Export without an adapter surfaces the controller's error in the dashboard.
                ui.Controls.ClickExport();
                Assert.IsTrue(ui.Dashboard.BannerIsError);
                StringAssert.Contains("exporter", ui.Dashboard.BannerMessage);

                // With an adapter, the success path reaches both the dashboard and the control panel.
                controller.BindExporter((cfg, bags, events) => controller.NotifyExportCompleted("saida/bags.csv"));
                ui.Controls.ClickExport();
                StringAssert.Contains("bags.csv", ui.Controls.ExportText);
                Assert.IsFalse(ui.Dashboard.BannerIsError);
                StringAssert.Contains("bags.csv", ui.Dashboard.BannerMessage);

                // Reset returns to the initial state, parked.
                ui.Controls.ClickReset();
                Assert.LessOrEqual(ui.World.ActiveBagCount, 1, "only the t=0 arrival may be visible");
                Assert.AreEqual("00:00.0", ui.Dashboard.SimulatedTimeText);
                Assert.AreEqual("PRONTO", ui.Dashboard.Status);
                Assert.IsFalse(ui.Controls.IsComplete);
                Assert.IsFalse(ui.Controls.IsRunning);

                // Controller-side validation errors are shown by the control panel, without starting.
                ui.Controls.Fields.FlagProbability.text = "3";
                ui.Controls.ClickStart();
                Assert.IsFalse(ui.Controls.IsRunning);
                StringAssert.Contains("FlagProbability", ui.Controls.ErrorText);
            }
            finally { Cleanup(host, ui.gameObject); }
            yield return new ExitPlayMode();
        }

        private static void RunToCompletion(BagflowUi ui, SimulationController controller, int n)
        {
            var c = ui.Controls;
            c.ClickReset();
            c.ShowValidationError(null);
            c.ApplyDefaults();
            c.Fields.BaggageCount.text = n.ToString();
            c.Fields.MeanArrival.text = "1";
            c.Fields.MeanInspection.text = "2";
            c.SelectSpeed(20);
            c.ClickStart();
            Assert.IsNull(string.IsNullOrEmpty(c.ErrorText) ? null : c.ErrorText, "start error: " + c.ErrorText);
            Assert.IsTrue(c.IsRunning, "run started for N=" + n);

            int bound = WorldLayout.MaxWaitingVisible + WorldLayout.MaxQueueVisible + 1 + 40 + 2 * WorldLayout.MaxExitVisible;
            bool sawScanning = false, sawQueue = false;
            double last = 0;
            for (int frame = 0; frame < 20000 && !c.IsComplete; frame++)
            {
                controller.Advance(0.05);
                ui.World.Animate(0.016f);
                Assert.LessOrEqual(ui.World.ActiveBagCount, bound);
                Assert.LessOrEqual(ui.World.CountActive(BagState.Scanning), 1, "a single scanner shows one bag");
                sawScanning |= ui.World.Scanner.IsBusy;
                sawQueue |= ui.World.CountActive(BagState.ScannerQueue) > 0;
                Assert.AreEqual(n.ToString(), ui.Dashboard.TotalText);
                Assert.IsFalse(ui.Dashboard.BannerIsError && ui.Dashboard.BannerMessage.Length > 0, ui.Dashboard.BannerMessage);
                last = frame;
            }
            Assert.IsTrue(c.IsComplete, "N=" + n + " completed within " + last + " frames");
            Assert.AreEqual("CONCLUÍDA", ui.Dashboard.Status);
            Assert.AreEqual(n.ToString(), ui.Dashboard.CompletedText);
            int approved = int.Parse(ui.Dashboard.ApprovedText), flagged = int.Parse(ui.Dashboard.FlaggedText);
            Assert.AreEqual(n, approved + flagged);
            Assert.AreEqual(approved.ToString(), ui.World.ApprovedLane.Value);
            Assert.AreEqual(flagged.ToString(), ui.World.FlaggedLane.Value);
            Assert.IsTrue(sawScanning, "scanner was shown busy at some point");
            if (n >= 60) Assert.IsTrue(sawQueue, "the FIFO queue was shown for N=" + n);
            Assert.IsTrue(c.ExportInteractable);
            Assert.IsFalse(ui.World.Scanner.IsBusy);
            Assert.LessOrEqual(ui.World.ActiveBagCount, 2 * WorldLayout.MaxExitVisible);
        }

        [UnityTest] public IEnumerator PauseFreezesTheDisplayedClockAndResumeContinues()
        {
            yield return new EnterPlayMode();
            var ui = BagflowUiFactory.Build();
            var host = new GameObject("Integration");
            host.SetActive(false);
            var controller = host.AddComponent<SimulationController>();
            var composition = host.AddComponent<SimulationComposition>();
            var so = new SerializedObject(composition);
            so.FindProperty("worldViewComponent").objectReferenceValue = ui.World;
            so.FindProperty("dashboardViewComponent").objectReferenceValue = ui.Dashboard;
            so.FindProperty("controlViewComponent").objectReferenceValue = ui.Controls;
            so.ApplyModifiedPropertiesWithoutUndo();
            host.SetActive(true);
            try
            {
                yield return null;
                ui.Controls.Fields.BaggageCount.text = "20";
                ui.Controls.ClickStart();
                for (int i = 0; i < 5; i++) controller.Advance(0.05);
                ui.Controls.ClickPauseResume();
                Assert.IsFalse(ui.Controls.IsRunning);
                string clock = ui.Dashboard.SimulatedTimeText;
                for (int i = 0; i < 5; i++) controller.Advance(0.05);
                Assert.AreEqual(clock, ui.Dashboard.SimulatedTimeText);
                Assert.AreEqual("Retomar", ui.Controls.PauseResumeCaption);
                ui.Controls.ClickPauseResume();
                Assert.IsTrue(ui.Controls.IsRunning);
                for (int i = 0; i < 5; i++) controller.Advance(0.05);
                Assert.AreNotEqual(clock, ui.Dashboard.SimulatedTimeText);
            }
            finally { Cleanup(host, ui.gameObject); }
            yield return new ExitPlayMode();
        }
    }
}
