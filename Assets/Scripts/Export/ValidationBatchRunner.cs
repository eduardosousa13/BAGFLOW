using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AirportBaggageSimulator
{
    public sealed class SimulationRunResult
    {
        public int Seed;
        public SimulationConfig Config;
        public IReadOnlyList<BagRecord> Bags;
        public IReadOnlyList<SimulationEventRecord> Events;
        public SimulationMetrics Metrics;
        public double EndTimeSeconds;
    }

    /// <summary>Offline collector for validation (Binomial needs many independent runs, Poisson needs many fixed windows).
    /// Each run uses the real M3 engine with a fresh M2 provider for its own seed, stepped to completion exactly as the
    /// controller would; it never touches a scene or the interactive session. Outputs aggregate CSVs written with the
    /// same formatter as the single-run exporter.</summary>
    public static class ValidationBatchRunner
    {
        public static readonly string[] RunColumns =
        {
            "run", "seed", "n", "arrival_mode", "flag_probability", "blockage_rate_per_s", "blockage_duration_s",
            "end_time_s", "completed", "approved", "flagged", "blockage_occurrences", "blocked_seconds",
            "mean_queue_wait_s", "mean_total_s", "scanner_utilization"
        };

        /// <summary>Runs one complete simulation with the real engine and real streams.</summary>
        public static SimulationRunResult Run(SimulationConfig config)
        {
            var copy = SimulationExporter.CopyConfig(config);
            var engine = new SimulationEngine();
            engine.Initialize(copy, new DeterministicRandomStreamProvider(copy.Seed));
            while (!engine.IsComplete) engine.Step();
            var snapshot = engine.GetSnapshot();
            return new SimulationRunResult
            {
                Seed = copy.Seed, Config = copy, Bags = engine.GetBagRecords(), Events = engine.GetEventRecords(),
                Metrics = snapshot.Metrics, EndTimeSeconds = snapshot.SimulationTimeSeconds
            };
        }

        /// <summary>Seeds base, base+1, ...: distinct, reproducible and independent of run order.</summary>
        public static int[] Seeds(int baseSeed, int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var seeds = new int[count];
            for (int i = 0; i < count; i++) seeds[i] = checked(baseSeed + i);
            return seeds;
        }

        /// <summary>Writes batch.json, runs.csv, batch_bags.csv (seed + bags.csv columns) and blockages.csv
        /// (every Poisson occurrence with its scheduled instant) into a new folder. The first
        /// <paramref name="fullExports"/> runs are also exported in the single-run format under runs/.</summary>
        public static string RunBatch(string directory, string name, SimulationConfig template, IList<int> seeds,
            int fullExports = 0, ExportOptions options = null, Action<int, int> progress = null)
        {
            options = options ?? new ExportOptions();
            if (seeds == null || seeds.Count == 0) throw new ArgumentException("At least one seed is required.");
            if (new HashSet<int>(seeds).Count != seeds.Count) throw new ArgumentException("Seeds must be distinct.");
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0 && !options.AllowOverwrite)
                throw new IOException("Batch folder " + directory + " is not empty; refusing to overwrite results.");
            Directory.CreateDirectory(directory);

            var utf8 = new UTF8Encoding(false);
            using (var runs = new StreamWriter(Path.Combine(directory, "runs.csv"), false, utf8))
            using (var bags = new StreamWriter(Path.Combine(directory, "batch_bags.csv"), false, utf8))
            using (var blockages = new StreamWriter(Path.Combine(directory, "blockages.csv"), false, utf8))
            {
                runs.Write(CsvFormat.Row(RunColumns));
                var bagHeader = new List<string> { "seed" };
                bagHeader.AddRange(SimulationExporter.BagColumns);
                bags.Write(CsvFormat.Row(bagHeader));
                blockages.Write(CsvFormat.Row(new[] { "seed", "occurrence", "scheduled_time_s", "time_s" }));

                for (int i = 0; i < seeds.Count; i++)
                {
                    var config = SimulationExporter.CopyConfig(template);
                    config.Seed = seeds[i];
                    var result = Run(config);
                    var m = result.Metrics;
                    runs.Write(CsvFormat.Row(new[]
                    {
                        CsvFormat.Integer(i), CsvFormat.Integer(config.Seed), CsvFormat.Integer(config.BaggageCount),
                        config.ArrivalMode.ToString(), CsvFormat.Number(config.FlagProbability),
                        CsvFormat.Number(config.BlockageRatePerSecond), CsvFormat.Number(config.BlockageDurationSeconds),
                        CsvFormat.Number(result.EndTimeSeconds), CsvFormat.Integer(m.Completed), CsvFormat.Integer(m.Approved),
                        CsvFormat.Integer(m.Flagged), CsvFormat.Integer(m.BlockageOccurrences), CsvFormat.Number(m.TotalBlockedSeconds),
                        CsvFormat.Number(m.MeanQueueWaitSeconds), CsvFormat.Number(m.MeanTotalSeconds), CsvFormat.Number(m.ScannerUtilization)
                    }));
                    string seedText = CsvFormat.Integer(config.Seed);
                    foreach (var bag in result.Bags)
                    {
                        var row = new List<string> { seedText };
                        row.AddRange(SimulationExporter.BagFields(bag));
                        bags.Write(CsvFormat.Row(row));
                    }
                    int occurrence = 0;
                    foreach (var e in result.Events)
                        if (e.EventType == SimulationEventType.BlockageOccurrence)
                            blockages.Write(CsvFormat.Row(new[]
                            {
                                seedText, CsvFormat.Integer(occurrence++), CsvFormat.Number(e.ScheduledTimeSeconds),
                                CsvFormat.Number(e.SimulationTimeSeconds)
                            }));
                    if (i < fullExports)
                        SimulationExporter.ExportToFolder(Path.Combine(directory, "runs", "seed_" + seedText), config,
                            result.Bags, result.Events, options);
                    if (progress != null) progress(i + 1, seeds.Count);
                }
            }

            var json = new StringBuilder("{\n");
            json.Append("  \"name\": ").Append(CsvFormat.JsonString(name)).Append(",\n");
            json.Append("  \"created_utc\": ").Append(CsvFormat.JsonString(options.UtcNow().ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))).Append(",\n");
            json.Append("  \"app_version\": ").Append(CsvFormat.JsonString(options.AppVersion)).Append(",\n");
            json.Append("  \"prng_algorithm\": ").Append(CsvFormat.JsonString(DeterministicRandomStreamProvider.Algorithm)).Append(",\n");
            json.Append("  \"runs\": ").Append(CsvFormat.Integer(seeds.Count)).Append(",\n");
            json.Append("  \"first_seed\": ").Append(CsvFormat.Integer(seeds[0])).Append(",\n");
            json.Append("  \"last_seed\": ").Append(CsvFormat.Integer(seeds[seeds.Count - 1])).Append(",\n");
            json.Append("  \"full_exports\": ").Append(CsvFormat.Integer(Math.Min(fullExports, seeds.Count))).Append(",\n");
            json.Append("  \"template\": ");
            // Reuse the single-run parameter block (seed field is the template value; per-run seeds are in runs.csv).
            string templateJson = SimulationExporter.ConfigJson(template, new BagRecord[0], new SimulationEventRecord[0],
                options.AppVersion, options.UtcNow().ToUniversalTime());
            int start = templateJson.IndexOf("\"parameters\":", StringComparison.Ordinal);
            int open = templateJson.IndexOf('{', start);
            int close = templateJson.IndexOf('}', open);
            json.Append(templateJson.Substring(open, close - open + 1).Replace("\n", "\n  ")).Append("\n}\n");
            File.WriteAllText(Path.Combine(directory, "batch.json"), json.ToString(), utf8);
            return Path.GetFullPath(directory);
        }
    }
}
