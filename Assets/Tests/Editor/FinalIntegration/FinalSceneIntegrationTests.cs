using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AirportBaggageSimulator.Tests
{
    public sealed class FinalSceneIntegrationTests
    {
        private const string Main = "Assets/Scenes/MainSimulation.unity";

        private static T[] Components<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        }

        [Test] public void SavedMainSceneHasOnePrefabCompositionAndExporterWithExplicitReferences()
        {
            var scene = EditorSceneManager.OpenScene(Main, OpenSceneMode.Additive);
            try
            {
                var ui = Components<BagflowUi>(scene).Single();
                var controller = Components<SimulationController>(scene).Single();
                var composition = Components<SimulationComposition>(scene).Single();
                var exporter = Components<SimulationCsvExportAdapter>(scene).Single();
                Assert.AreEqual("BAGFLOW Integration", controller.name);
                Assert.AreSame(controller.gameObject, composition.gameObject);
                Assert.AreSame(controller.gameObject, exporter.gameObject);
                Assert.AreEqual("Assets/Prefabs/BAGFLOW_UI.prefab",
                    AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(ui.gameObject)));
                var binding = new SerializedObject(composition);
                Assert.AreSame(ui.World, binding.FindProperty("worldViewComponent").objectReferenceValue);
                Assert.AreSame(ui.Dashboard, binding.FindProperty("dashboardViewComponent").objectReferenceValue);
                Assert.AreSame(ui.Controls, binding.FindProperty("controlViewComponent").objectReferenceValue);
                Assert.AreSame(controller, new SerializedObject(ui).FindProperty("controller").objectReferenceValue);
                Assert.AreSame(controller, new SerializedObject(exporter).FindProperty("controller").objectReferenceValue);
                Assert.AreEqual(Path.Combine(Application.persistentDataPath, "BAGFLOW", "exports"), exporter.RootDirectory);
                var legacy = new SerializedObject(controller);
                foreach (string field in new[] { "engineComponent", "worldViewComponent", "dashboardViewComponent", "controlViewComponent" })
                    Assert.IsNull(legacy.FindProperty(field).objectReferenceValue, "Composition owns wiring");
                Assert.AreEqual(1, Components<Camera>(scene).Length);
                Assert.AreEqual("Main Camera", Components<Camera>(scene)[0].name);
                Assert.IsTrue(scene.GetRootGameObjects().Any(g => g.name == "Global Light 2D"));
                Assert.LessOrEqual(Components<EventSystem>(scene).Length, 1);
                Assert.IsEmpty(Components<VisualSandboxDemo>(scene));
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                        Assert.IsNotNull(component, "Missing script in " + root.name);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [UnityTest] public IEnumerator MainSceneRealControlsRunPauseResetReplayAndExportFor1_60_500()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync(Main, LoadSceneMode.Additive);
            yield return null; // Serialized composition and export adapter run their normal lifecycle.
            var scene = SceneManager.GetSceneByPath(Main);
            string exports = Path.Combine(Path.GetTempPath(), "bagflow_final_" + Guid.NewGuid().ToString("N"));
            try
            {
                var ui = Components<BagflowUi>(scene).Single();
                var controller = Components<SimulationController>(scene).Single();
                var composition = Components<SimulationComposition>(scene).Single();
                var adapter = Components<SimulationCsvExportAdapter>(scene).Single();
                Assert.IsInstanceOf<SimulationEngine>(composition.Engine);
                Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>().Length);
                // Only redirect disk output; keep the serialized real controller, views and engine binding.
                adapter.Configure(controller, exports);
                var notices = new List<string>();
                controller.ExportCompleted += notices.Add;
                foreach (int n in new[] { 1, 60, 500 })
                {
                    var controls = ui.Controls;
                    controls.ApplyDefaults();
                    controls.Fields.BaggageCount.text = n.ToString(CultureInfo.InvariantCulture);
                    controls.SelectSpeed(20);
                    Click(ui, "Start");
                    Assert.IsTrue(controls.IsRunning, controls.ErrorText);
                    yield return new WaitForSecondsRealtime(.05f); // Exercise the actual Update clock.
                    Assert.Greater(composition.Engine.GetSnapshot().SimulationTimeSeconds, 0);
                    Click(ui, "PauseResume");
                    Assert.IsFalse(controls.IsRunning);
                    double paused = composition.Engine.GetSnapshot().SimulationTimeSeconds;
                    string clock = ui.Dashboard.SimulatedTimeText;
                    controller.Advance(10);
                    yield return null;
                    Assert.AreEqual(paused, composition.Engine.GetSnapshot().SimulationTimeSeconds);
                    Assert.AreEqual(clock, ui.Dashboard.SimulatedTimeText);
                    Click(ui, "PauseResume");
                    Assert.IsTrue(controls.IsRunning);
                    foreach (double speed in new[] { 1.0, 5, 10, 20 })
                    {
                        Click(ui, "Speed" + speed);
                        double before = composition.Engine.GetSnapshot().SimulationTimeSeconds;
                        controller.Advance(.1);
                        Assert.AreEqual(speed * .1,
                            composition.Engine.GetSnapshot().SimulationTimeSeconds - before, .100001);
                        Assert.AreEqual(speed, controls.SelectedSpeed);
                    }
                    bool sawQueue = false, sawBlock = false, sawScanner = false;
                    Finish(ui, controller, composition.Engine, ref sawQueue, ref sawBlock, ref sawScanner);
                    Assert.IsTrue(sawScanner);
                    if (n >= 60) { Assert.IsTrue(sawQueue); Assert.IsTrue(sawBlock); }
                    var final = composition.Engine.GetSnapshot();
                    Assert.AreEqual(n, final.Metrics.Completed);
                    Assert.AreEqual(n, final.Metrics.Approved + final.Metrics.Flagged);
                    Assert.IsTrue(final.Bags.All(b => b.State == BagState.Approved || b.State == BagState.Flagged));
                    Assert.AreEqual(n.ToString(), ui.Dashboard.CompletedText);
                    Assert.AreEqual("CONCLUÍDA", ui.Dashboard.Status);
                    string bags = SimulationExporter.BagsCsv(composition.Engine.GetBagRecords());
                    string events = SimulationExporter.EventsCsv(composition.Engine.GetEventRecords());
                    Click(ui, "Export");
                    Assert.AreEqual(notices.Last(), adapter.LastExportDirectory);
                    Assert.AreEqual(bags, File.ReadAllText(Path.Combine(adapter.LastExportDirectory, "bags.csv")));
                    Assert.AreEqual(events, File.ReadAllText(Path.Combine(adapter.LastExportDirectory, "events.csv")));
                    string config = File.ReadAllText(Path.Combine(adapter.LastExportDirectory, "config.json"));
                    StringAssert.Contains("\"BaggageCount\": " + n, config);
                    StringAssert.Contains("\"seed\": 42", config);
                    Assert.AreEqual(3, Directory.GetFiles(adapter.LastExportDirectory).Length);
                    StringAssert.Contains("Exportado para: " + adapter.LastExportDirectory, controls.ExportText);
                    Assert.AreEqual(controls.ExportText, ui.Dashboard.BannerMessage);
                    Assert.IsFalse(ui.Dashboard.BannerIsError);
                    Debug.Log("FINAL_RUN N=" + n + " approved=" + final.Metrics.Approved + " flagged=" + final.Metrics.Flagged +
                        " blockages=" + final.Metrics.BlockageOccurrences + " logicalSeconds=" + final.SimulationTimeSeconds +
                        " export=" + adapter.LastExportDirectory);

                    // Reset must retain original seed/config/speed, despite edited form fields.
                    controls.Fields.Seed.text = "999";
                    controls.SelectSpeed(1);
                    Click(ui, "Reset");
                    Assert.AreEqual(0, composition.Engine.GetSnapshot().SimulationTimeSeconds);
                    Assert.IsFalse(controls.IsRunning);
                    Assert.AreEqual(20, controls.SelectedSpeed);
                    Assert.AreEqual("PRONTO", ui.Dashboard.Status);
                    controls.SelectSpeed(1);
                    Click(ui, "PauseResume");
                    Finish(ui, controller, composition.Engine, ref sawQueue, ref sawBlock, ref sawScanner);
                    Assert.AreEqual(bags, SimulationExporter.BagsCsv(composition.Engine.GetBagRecords()), "Seed replay at 1x");
                    Assert.AreEqual(events, SimulationExporter.EventsCsv(composition.Engine.GetEventRecords()), "Logical history at 1x/20x");
                }
                Assert.AreEqual(3, notices.Count);
            }
            finally
            {
                foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                foreach (var es in Object.FindObjectsByType<EventSystem>()) Object.DestroyImmediate(es.gameObject);
                if (Directory.Exists(exports)) Directory.Delete(exports, true);
            }
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return new ExitPlayMode();
        }

        private static void Click(BagflowUi ui, string name)
        {
            var button = ui.transform.Find("Controls/" + name).GetComponent<Button>();
            Assert.IsTrue(button.interactable, name + " must be available");
            button.onClick.Invoke();
        }

        private static void Finish(BagflowUi ui, SimulationController controller, ISimulationEngine engine,
            ref bool queue, ref bool block, ref bool scanner)
        {
            int frames = 0;
            while (!engine.IsComplete && frames++ < 10000)
            {
                // Scripted frame durations use the same production Advance path; no extra Unity frames elapse here.
                controller.Advance(frames < 150 ? .1 / ui.Controls.SelectedSpeed : 5 / ui.Controls.SelectedSpeed);
                var s = engine.GetSnapshot();
                queue |= s.Metrics.QueueLength > 0;
                block |= s.BeltBlocked;
                scanner |= s.Metrics.Scanning > 0;
                Assert.LessOrEqual(s.Metrics.Scanning, 1);
                Assert.AreEqual(s.Metrics.Completed, s.Metrics.Approved + s.Metrics.Flagged);
                Assert.AreEqual(s.BeltBlocked, ui.World.Belt.IsBlocked);
                Assert.AreEqual(s.Metrics.Scanning > 0, ui.World.Scanner.IsBusy);
                Assert.AreEqual(s.Metrics.QueueLength.ToString(), ui.Dashboard.QueueText);
                Assert.IsFalse(ui.Dashboard.BannerIsError, ui.Dashboard.BannerMessage);
            }
            Assert.IsTrue(engine.IsComplete, "Run did not complete");
        }
    }
}
