using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AirportBaggageSimulator
{
    /// <summary>Sandbox-only helper. Left/Right arrows (or keys 1-9) push artificial snapshots to the UI;
    /// key 0 hands control back to the real engine through the sandbox's SimulationController.
    /// Never add this component to MainSimulation.</summary>
    public sealed class VisualSandboxDemo : MonoBehaviour
    {
        [SerializeField] private BagflowUi ui;
        [SerializeField] private SimulationController controller;
        [SerializeField] private int scenario = -1;

        private Text hud;

        public int Scenario { get { return scenario; } }

        private void Start()
        {
            var canvasRect = UiBuilder.Node("SandboxHud", ui.transform);
            UiBuilder.Anchor(canvasRect, 0.015f, 0f, 0.69f, 0.02f);
            hud = UiBuilder.Label(canvasRect, "Hint", "", 15, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UiBuilder.Stretch(hud.rectTransform);
            UpdateHud();
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.rightArrowKey.wasPressedThisFrame) Show(scenario + 1);
            if (keyboard.leftArrowKey.wasPressedThisFrame) Show(scenario - 1);
            if (keyboard.digit0Key.wasPressedThisFrame) UseRealEngine();
            for (int i = 1; i <= 9; i++)
                if (keyboard[(Key)((int)Key.Digit1 + i - 1)].wasPressedThisFrame) Show(i - 1);
#endif
        }

        public void Show(int index)
        {
            scenario = ((index % SandboxScenarios.Count) + SandboxScenarios.Count) % SandboxScenarios.Count;
            if (controller != null) controller.enabled = false; // stop the real run while a static scenario is shown
            var snapshot = SandboxScenarios.Build(scenario);
            ui.World.ResetView();
            ui.World.Render(snapshot);
            ui.Dashboard.Render(snapshot);
            UpdateHud();
        }

        public void UseRealEngine()
        {
            scenario = -1;
            if (controller != null) controller.enabled = true;
            ui.World.ResetView();
            ui.Dashboard.Render(null);
            UpdateHud();
        }

        private void UpdateHud()
        {
            if (hud == null) return;
            hud.text = scenario < 0
                ? "Sandbox · motor real ativo (use Iniciar) · ←/→ ou 1-9: cenários fictícios"
                : "Sandbox · cenário " + (scenario + 1) + "/" + SandboxScenarios.Count + ": " + SandboxScenarios.Names[scenario] + " · 0: motor real";
        }
    }
}
