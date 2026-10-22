using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AirportBaggageSimulator.EditorTools
{
    /// <summary>Generates the Unity validation datasets read by validation/BAGFLOW_validation.ipynb.
    /// Menu: BAGFLOW/M5/Generate validation data. Batch:
    /// -executeMethod AirportBaggageSimulator.EditorTools.ValidationDataMenu.GenerateBatch [-bagflowOut folder]</summary>
    public static class ValidationDataMenu
    {
        public const int SampleRuns = 25;        // 25 x 500 = 12 500 raw samples per continuous variable
        public const int BinomialRuns = 2000;    // 2 000 independent N_S observations (N=60, p=0.08)
        public const int ScenarioRuns = 300;

        public static string DefaultOutput
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "validation", "data", "unity")); }
        }

        [MenuItem("BAGFLOW/M5/Generate validation data")]
        public static void GenerateMenu()
        {
            string output = DefaultOutput;
            if (!EditorUtility.DisplayDialog("BAGFLOW M5", "Generate validation datasets in\n" + output +
                "\n\nThe folder must not already contain them.", "Generate", "Cancel")) return;
            try { Generate(output); EditorUtility.RevealInFinder(output); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static void GenerateBatch()
        {
            try
            {
                string output = DefaultOutput;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-bagflowOut") output = args[i + 1];
                Generate(output);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void Generate(string output)
        {
            var options = new ExportOptions { AppVersion = Application.version };
            var defaults = new SimulationConfig();

            // 1. Canonical single run (approved defaults, seed 42) in the exact docs/CONTRACTS.md format.
            var example = ValidationBatchRunner.Run(defaults);
            SimulationExporter.ExportToFolder(Path.Combine(output, "example_seed42_n60"), example.Config,
                example.Bags, example.Events, options);

            // 2. Raw samples of Triangular / Exponential / truncated Normal: N=500 per run, distinct seeds.
            var samples = SimulationExporter.CopyConfig(defaults);
            samples.BaggageCount = 500;
            Batch(output, "samples_n500", samples, ValidationBatchRunner.Seeds(1000, SampleRuns), 1, options);

            // 3. Binomial (one N_S per run) and Poisson (fixed windows) with the approved defaults.
            Batch(output, "binomial_n60", defaults, ValidationBatchRunner.Seeds(100000, BinomialRuns), 0, options);

            // 4. Scenario comparisons (configurable presets, NOT replacements of the approved parameters).
            var exponential = SimulationExporter.CopyConfig(defaults);
            exponential.ArrivalMode = ArrivalMode.Exponential;
            Batch(output, "scenario_exponential_arrivals", exponential, ValidationBatchRunner.Seeds(200000, ScenarioRuns), 0, options);
            var noBlockage = SimulationExporter.CopyConfig(defaults);
            noBlockage.BlockageRatePerSecond = 0;
            Batch(output, "scenario_no_blockages", noBlockage, ValidationBatchRunner.Seeds(300000, ScenarioRuns), 0, options);
            Debug.Log("BAGFLOW M5: validation data written to " + output);
        }

        private static void Batch(string output, string name, SimulationConfig template, int[] seeds, int fullExports, ExportOptions options)
        {
            ValidationBatchRunner.RunBatch(Path.Combine(output, name), name, template, seeds, fullExports, options,
                (done, total) =>
                {
                    if (!Application.isBatchMode && done % 25 == 0)
                        EditorUtility.DisplayProgressBar("BAGFLOW M5", name + " " + done + "/" + total, done / (float)total);
                });
        }
    }
}
