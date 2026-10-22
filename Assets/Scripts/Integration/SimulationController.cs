using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirportBaggageSimulator
{
    /// <summary>M1 wiring only. Bind a pure C# engine from the M3 composition root.</summary>
    public sealed class SimulationController : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour engineComponent;
        [SerializeField] private MonoBehaviour worldViewComponent;
        [SerializeField] private MonoBehaviour dashboardViewComponent;
        [SerializeField] private MonoBehaviour controlViewComponent;
        [SerializeField, Min(1)] private int maxTicksPerFrame = 1000;
        private ISimulationEngine engine;
        private ISimulationWorldView world;
        private ISimulationDashboardView dashboard;
        private ISimulationControlView controls;
        private SimulationSession session;
        private bool subscribed;
        private bool hasBinding;
        private Action<SimulationConfig, IReadOnlyList<BagRecord>, IReadOnlyList<SimulationEventRecord>> exporter;

        // Local composition hooks, not replacements for the shared engine/view contracts.
        // Export completion can be connected by M4 without misusing ShowError for a success message.
        public event Action<string> ExportCompleted;

        private void Awake()
        {
            // Explicit composition on an inactive object may precede Awake.
            // Never overwrite that binding with the serialized Inspector defaults.
            if (hasBinding) return;
            try
            {
                Bind(engineComponent == null ? null : RequireInterface<ISimulationEngine>(engineComponent),
                    RequireInterface<ISimulationWorldView>(worldViewComponent),
                    RequireInterface<ISimulationDashboardView>(dashboardViewComponent),
                    RequireInterface<ISimulationControlView>(controlViewComponent));
            }
            catch (Exception error) { Report(error); }
        }

        /// <summary>Explicit binding takes precedence over Inspector defaults, even before Awake.
        /// Compose in the root's Start (after view Awake/OnEnable), or bind while inactive
        /// and then activate. Views must wait for composition before sending commands.</summary>
        public void Bind(ISimulationEngine simulationEngine, ISimulationWorldView worldView,
            ISimulationDashboardView dashboardView, ISimulationControlView controlView)
        {
            hasBinding = true;
            Unsubscribe();
            if (session != null) session.Pause();
            controls?.SetRunning(false);
            engine = simulationEngine;
            world = worldView;
            dashboard = dashboardView;
            controls = controlView;
            session = engine == null ? null : new SimulationSession(engine);
            if (isActiveAndEnabled) Subscribe();
            controls?.SetRunning(false);
        }

        /// <summary>M5 adapter may be registered once its export API has been agreed.</summary>
        public void BindExporter(Action<SimulationConfig, IReadOnlyList<BagRecord>, IReadOnlyList<SimulationEventRecord>> export)
        {
            exporter = export;
        }

        public void NotifyExportCompleted(string path) { ExportCompleted?.Invoke(path); }
        private void OnEnable() { Subscribe(); }
        private void OnDisable() { session?.Pause(); controls?.SetRunning(false); Unsubscribe(); }

        private void Subscribe()
        {
            if (subscribed || controls == null) return;
            controls.StartRequested += StartSimulation;
            controls.PauseRequested += PauseSimulation;
            controls.ResumeRequested += ResumeSimulation;
            controls.ResetRequested += ResetSimulation;
            controls.PlaybackSpeedRequested += SetPlaybackSpeed;
            controls.ExportRequested += Export;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed || controls == null) return;
            controls.StartRequested -= StartSimulation;
            controls.PauseRequested -= PauseSimulation;
            controls.ResumeRequested -= ResumeSimulation;
            controls.ResetRequested -= ResetSimulation;
            controls.PlaybackSpeedRequested -= SetPlaybackSpeed;
            controls.ExportRequested -= Export;
            subscribed = false;
        }

        private void StartSimulation(SimulationConfig config)
        {
            try { SimulationConfiguration.Validate(config); }
            catch (Exception error) { controls?.ShowValidationError(error.Message); return; }
            Execute(() => { RequireReady(); session.Start(config); world?.ResetView(); Render(); });
        }
        private void PauseSimulation() { Execute(() => { RequireReady(); session.Pause(); }); }
        private void ResumeSimulation() { Execute(() => { RequireReady(); session.Resume(); }); }
        private void ResetSimulation()
        {
            Execute(() => { RequireReady(); session.Reset(); if (session.IsInitialized) { world?.ResetView(); Render(); } });
        }
        private void SetPlaybackSpeed(double speed) { Execute(() => { RequireReady(); session.SetPlaybackSpeed(speed); }); }
        private void Update() { Advance(Time.unscaledDeltaTime); }

        /// <summary>Advance one frame through the same path used by Update. The supplied duration
        /// is real, unscaled seconds; logical ticks and randomness remain inside session/engine.</summary>
        public void Advance(double realSeconds)
        {
            if (session == null || !session.IsInitialized || !session.IsRunning) return;
            Execute(() => { session.Advance(realSeconds, maxTicksPerFrame); Render(); });
        }
        private void Render()
        {
            var snapshot = session.Snapshot;
            world?.Render(snapshot);
            dashboard?.Render(snapshot);
        }
        private void Export()
        {
            Execute(() =>
            {
                RequireReady();
                if (!session.IsInitialized || !engine.IsComplete) throw new InvalidOperationException("Complete a simulation before exporting.");
                if (exporter == null) throw new InvalidOperationException("M5 exporter adapter is not connected; the shared export contract is pending.");
                // Contract records contain only scalar/string fields. Always detach before external code.
                exporter(session.Configuration, CopyRecords(engine.GetBagRecords()), CopyRecords(engine.GetEventRecords()));
            });
        }
        private static IReadOnlyList<T> CopyRecords<T>(IReadOnlyList<T> source) where T : new()
        {
            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = new T();
                foreach (var field in typeof(T).GetFields()) field.SetValue(copy[i], field.GetValue(source[i]));
            }
            return Array.AsReadOnly(copy);
        }
        private void RequireReady()
        {
            if (session == null) throw new InvalidOperationException("M3 ISimulationEngine is not connected. Bind the engine before starting.");
        }
        private void Execute(Action action)
        {
            try { action(); }
            catch (Exception error) { session?.Pause(); Report(error); }
            finally { controls?.SetRunning(session != null && session.IsRunning); }
        }
        private void Report(Exception error)
        {
            if (dashboard != null) dashboard.ShowError(error.Message);
            else if (controls != null) controls.ShowValidationError(error.Message);
            else Debug.LogWarning("BAGFLOW integration: " + error.Message, this);
        }
        private static T RequireInterface<T>(MonoBehaviour component) where T : class
        {
            if (component == null) return null;
            var value = component as T;
            if (value == null) throw new ArgumentException(component.name + " must implement " + typeof(T).Name);
            return value;
        }
    }
}
