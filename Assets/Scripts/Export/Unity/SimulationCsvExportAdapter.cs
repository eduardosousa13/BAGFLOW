using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AirportBaggageSimulator
{
    /// <summary>M5 adapter for SimulationController.BindExporter: writes bags.csv/events.csv/config.json into a new
    /// folder and reports it with NotifyExportCompleted. Exceptions (I/O, existing files) propagate to the
    /// controller, which pauses and shows them through the dashboard. It never steps or controls the simulation.</summary>
    [DisallowMultipleComponent]
    public sealed class SimulationCsvExportAdapter : MonoBehaviour
    {
        [Tooltip("Optional. Empty: SimulationController on this GameObject, otherwise the first one in the scene.")]
        [SerializeField] private SimulationController controller;
        [Tooltip("Optional absolute folder. Empty: <Application.persistentDataPath>/BAGFLOW/exports")]
        [SerializeField] private string outputFolder = "";

        private SimulationController bound;

        public string LastExportDirectory { get; private set; }

        public string RootDirectory
        {
            get
            {
                return string.IsNullOrWhiteSpace(outputFolder)
                    ? Path.Combine(Application.persistentDataPath, "BAGFLOW", "exports")
                    : outputFolder;
            }
        }

        /// <summary>Explicit set-up (tests or dynamic composition). Rebinds when already enabled.</summary>
        public void Configure(SimulationController target, string folder)
        {
            controller = target;
            outputFolder = folder ?? "";
            if (isActiveAndEnabled) { Unbind(); Bind(); }
        }

        private void OnEnable() { Bind(); }
        private void OnDisable() { Unbind(); }

        private void Bind()
        {
            var target = controller != null ? controller : GetComponent<SimulationController>();
            if (target == null) target = FindAnyObjectByType<SimulationController>();
            if (target == null)
            {
                Debug.LogWarning("BAGFLOW export: no SimulationController found; export button will report it is not connected.", this);
                return;
            }
            target.BindExporter(Export);
            bound = target;
        }

        private void Unbind()
        {
            if (bound != null) bound.BindExporter(null);
            bound = null;
        }

        /// <summary>Signature required by SimulationController.BindExporter.</summary>
        public void Export(SimulationConfig config, IReadOnlyList<BagRecord> bags, IReadOnlyList<SimulationEventRecord> events)
        {
            var result = SimulationExporter.ExportToNewFolder(RootDirectory, config, bags, events,
                new ExportOptions { AppVersion = Application.version });
            LastExportDirectory = result.Directory;
            Debug.Log("BAGFLOW export: " + result.BagCount + " bags, " + result.EventCount + " events -> " + result.Directory, this);
            if (bound != null) bound.NotifyExportCompleted(result.Directory);
        }
    }
}
