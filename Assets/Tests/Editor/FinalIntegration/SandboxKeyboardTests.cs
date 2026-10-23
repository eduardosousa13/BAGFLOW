using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AirportBaggageSimulator.Tests
{
    public sealed class SandboxKeyboardTests
    {
        [UnityTest] public IEnumerator SandboxShortcutsRespectTextAndOtherKeyboardControls()
        {
            yield return new EnterPlayMode();
            const string path = "Assets/Scenes/VisualSandbox.unity";
            // Sandbox is an editor demonstration scene, deliberately absent from build settings.
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            var scene = SceneManager.GetSceneByPath(path);
            var roots = scene.GetRootGameObjects();
            var demo = roots.SelectMany(g => g.GetComponentsInChildren<VisualSandboxDemo>()).Single();
            var ui = roots.SelectMany(g => g.GetComponentsInChildren<BagflowUi>()).Single();
            var controller = roots.SelectMany(g => g.GetComponentsInChildren<SimulationController>()).Single();
            var previousFocusPolicy = InputSystem.settings.editorInputBehaviorInPlayMode;
            var previousBackgroundPolicy = InputSystem.settings.backgroundBehavior;
            // Batch mode has no focused Game view. Route only this test's input to the player.
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Assert.AreEqual(-1, demo.Scenario);
                EventSystem.current.SetSelectedGameObject(ui.Controls.Fields.BaggageCount.gameObject);
                ui.Controls.Fields.BaggageCount.ActivateInputField();
                Press(keyboard, Key.Digit1, demo);
                Assert.AreEqual(-1, demo.Scenario, "typing 1 must not select the empty scenario");
                Assert.IsTrue(controller.enabled);
                Press(keyboard, Key.RightArrow, demo);
                Assert.AreEqual(-1, demo.Scenario, "editing arrow navigation belongs to the field");

                // Buttons also own keyboard navigation/activation while selected.
                EventSystem.current.SetSelectedGameObject(ui.transform.Find("Controls/Start").gameObject);
                Press(keyboard, Key.Digit2, demo);
                Assert.AreEqual(-1, demo.Scenario);

                EventSystem.current.SetSelectedGameObject(null);
                Press(keyboard, Key.Digit1, demo);
                Assert.AreEqual(0, demo.Scenario, "unfocused numeric shortcut still works");
                Assert.IsFalse(controller.enabled);
                Press(keyboard, Key.RightArrow, demo);
                Assert.AreEqual(1, demo.Scenario);

                EventSystem.current.SetSelectedGameObject(ui.Controls.Fields.Seed.gameObject);
                Press(keyboard, Key.Digit0, demo);
                Assert.AreEqual(1, demo.Scenario, "typing 0 must not switch modes");
                EventSystem.current.SetSelectedGameObject(null);
                Press(keyboard, Key.Digit0, demo);
                Assert.AreEqual(-1, demo.Scenario);
                Assert.IsTrue(controller.enabled);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = previousFocusPolicy;
                InputSystem.settings.backgroundBehavior = previousBackgroundPolicy;
                foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                foreach (var es in Object.FindObjectsByType<EventSystem>()) Object.DestroyImmediate(es.gameObject);
            }
            yield return SceneManager.UnloadSceneAsync(scene);
            yield return new ExitPlayMode();
        }

        private static void Press(Keyboard keyboard, Key key, VisualSandboxDemo demo)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.Update();
            Assert.IsTrue(keyboard[key].wasPressedThisFrame, "Injected key " + key);
            Assert.AreSame(keyboard, Keyboard.current);
            // Exercise the same Update method with a real Input System keyboard event.
            demo.SendMessage("Update");
        }
    }
}
