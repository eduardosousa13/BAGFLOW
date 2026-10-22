using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace AirportBaggageSimulator
{
    /// <summary>Root of the BAGFLOW_UI prefab. Groups the three contract views (World, Dashboard, Controls),
    /// forwards snapshots to the control panel and shows export results. Contains no simulation logic.</summary>
    [DisallowMultipleComponent]
    public sealed class BagflowUi : MonoBehaviour
    {
        [SerializeField] private BaggageWorldView world;
        [SerializeField] private DashboardView dashboard;
        [SerializeField] private SimulationControlPanel controls;
        [Tooltip("Optional. When empty, the first SimulationController in the scene is used on enable.")]
        [SerializeField] private SimulationController controller;

        private SimulationController subscribedTo;

        public BaggageWorldView World { get { return world; } }
        public DashboardView Dashboard { get { return dashboard; } }
        public SimulationControlPanel Controls { get { return controls; } }

        internal void Wire(BaggageWorldView worldView, DashboardView dashboardView, SimulationControlPanel panel)
        {
            world = worldView; dashboard = dashboardView; controls = panel;
            if (isActiveAndEnabled) OnEnable(); // Awake/OnEnable ran at AddComponent, before the views were assigned
        }

        private void Start()
        {
            // Scene objects are all loaded by now, so a controller created after this UI is still found.
            if (subscribedTo == null) TrySubscribeController();
        }

        private void Awake() { EnsureEventSystem(); }

        private void OnEnable()
        {
            Unsubscribe();
            if (dashboard != null) dashboard.Rendered += OnRendered;
            TrySubscribeController();
        }

        private void TrySubscribeController()
        {
            var target = controller != null ? controller : FindFirstObjectByType<SimulationController>();
            if (target == null || target == subscribedTo) return;
            target.ExportCompleted += OnExportCompleted;
            subscribedTo = target;
        }

        private void OnDisable() { Unsubscribe(); }

        private void Unsubscribe()
        {
            if (dashboard != null) dashboard.Rendered -= OnRendered;
            if (subscribedTo != null) subscribedTo.ExportCompleted -= OnExportCompleted;
            subscribedTo = null;
        }

        private void OnRendered(SimulationSnapshot snapshot) { if (controls != null) controls.NotifySnapshot(snapshot); }

        public void OnExportCompleted(string path)
        {
            string message = string.IsNullOrEmpty(path) ? "Exportação concluída." : "Exportado para: " + path;
            if (dashboard != null) dashboard.ShowInfo(message);
            if (controls != null) controls.ShowExportResult(true, message);
        }

        /// <summary>Creates an EventSystem (Input System module) when the scene has none.</summary>
        public static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
