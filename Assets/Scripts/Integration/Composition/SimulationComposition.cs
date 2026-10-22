using System;
using UnityEngine;

namespace AirportBaggageSimulator
{
    /// <summary>M1 composition root: the only Unity component that constructs the concrete M3 engine.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SimulationController))]
    public sealed class SimulationComposition : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour worldViewComponent;
        [SerializeField] private MonoBehaviour dashboardViewComponent;
        [SerializeField] private MonoBehaviour controlViewComponent;

        public ISimulationEngine Engine { get; private set; }

        private void Start()
        {
            // Explicit composition on an inactive object takes precedence over Inspector defaults.
            if (Engine != null) return;
            try
            {
                Compose(AsInterface<ISimulationWorldView>(worldViewComponent),
                    AsInterface<ISimulationDashboardView>(dashboardViewComponent),
                    AsInterface<ISimulationControlView>(controlViewComponent));
            }
            catch (Exception error)
            {
                Debug.LogError("BAGFLOW composition: " + error.Message, this);
            }
        }

        /// <summary>Bind real M3 to the controller, with M4 contracts supplied by Inspector or composition.
        /// Called once from Start by default, after all views have run Awake/OnEnable.
        /// Calling it explicitly replaces the previous engine/run; it never starts a simulation.</summary>
        public void Compose(ISimulationWorldView world, ISimulationDashboardView dashboard,
            ISimulationControlView controls)
        {
            ISimulationEngine engine = new SimulationEngine();
            GetComponent<SimulationController>().Bind(engine, world, dashboard, controls);
            Engine = engine;
        }

        private static T AsInterface<T>(MonoBehaviour component) where T : class
        {
            if (component == null) return null; // M4 can be connected later; no fake production views.
            var view = component as T;
            if (view == null) throw new ArgumentException(component.name + " must implement " + typeof(T).Name);
            return view;
        }
    }
}
