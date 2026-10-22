using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace AirportBaggageSimulator
{
    public sealed class ExportOptions
    {
        /// <summary>Application version written to config.json (the Unity adapter passes Application.version).</summary>
        public string AppVersion = "unknown";
        /// <summary>When false (default) existing result files are never replaced.</summary>
        public bool AllowOverwrite;
        /// <summary>Injectable clock (UTC) for the folder name and the export timestamp.</summary>
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
    }

    public sealed class ExportResult
    {
        public string Directory;
        public string BagsPath;
        public string EventsPath;
        public string ConfigPath;
        public int BagCount;
        public int EventCount;
        public int FlaggedCount;
        public int BlockageOccurrences;
    }

    /// <summary>M5 exporter: writes bags.csv, events.csv and config.json exactly as specified in docs/CONTRACTS.md.
    /// Pure .NET (no UnityEngine). Raw samples are written unrounded; derived columns are differences of timestamps.</summary>
    public static class SimulationExporter
    {
        public const string SchemaVersion = "1";
        public const string BagsFile = "bags.csv";
        public const string EventsFile = "events.csv";
        public const string ConfigFile = "config.json";

        public static readonly string[] BagColumns =
        {
            "id", "weight_kg", "nominal_transport_s", "observed_transport_s", "inspection_s", "arrival_s",
            "queue_enter_s", "scan_start_s", "complete_s", "queue_wait_s", "total_s", "flagged"
        };

        public static readonly string[] EventColumns = { "time_s", "event_type", "bag_id", "detail", "scheduled_time_s" };

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Exports into a NEW sub-folder of <paramref name="rootDirectory"/> named
        /// bagflow_yyyyMMdd_HHmmss_seed{S}_n{N}; a numeric suffix is added if it already exists. Returns the folder.</summary>
        public static ExportResult ExportToNewFolder(string rootDirectory, SimulationConfig config,
            IReadOnlyList<BagRecord> bags, IReadOnlyList<SimulationEventRecord> events, ExportOptions options = null)
        {
            options = options ?? new ExportOptions();
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Export root directory is empty.");
            RequireConfig(config);
            System.IO.Directory.CreateDirectory(rootDirectory);
            string stem = string.Format(CultureInfo.InvariantCulture, "bagflow_{0:yyyyMMdd_HHmmss}_seed{1}_n{2}",
                options.UtcNow().ToUniversalTime(), config.Seed, config.BaggageCount);
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string candidate = Path.Combine(rootDirectory, attempt == 1 ? stem : stem + "_" + attempt);
                if (System.IO.Directory.Exists(candidate) || File.Exists(candidate)) continue;
                System.IO.Directory.CreateDirectory(candidate);
                try { return ExportToFolder(candidate, config, bags, events, options); }
                catch
                {
                    TryDeleteEmptyDirectory(candidate);
                    throw;
                }
            }
            throw new IOException("Could not find a free export folder name under " + rootDirectory);
        }

        /// <summary>Exports into <paramref name="directory"/> (created if missing). Without AllowOverwrite an existing
        /// bags.csv/events.csv/config.json makes the export fail before anything is written.</summary>
        public static ExportResult ExportToFolder(string directory, SimulationConfig config,
            IReadOnlyList<BagRecord> bags, IReadOnlyList<SimulationEventRecord> events, ExportOptions options = null)
        {
            options = options ?? new ExportOptions();
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Export directory is empty.");
            RequireConfig(config);
            if (bags == null) throw new ArgumentNullException(nameof(bags));
            if (events == null) throw new ArgumentNullException(nameof(events));
            RequireUniqueIds(bags);

            // Format everything first: a formatting error never leaves partial files behind.
            string bagsText = BagsCsv(bags);
            string eventsText = EventsCsv(events);
            string configText = ConfigJson(config, bags, events, options.AppVersion, options.UtcNow().ToUniversalTime());

            System.IO.Directory.CreateDirectory(directory);
            var targets = new[]
            {
                new KeyValuePair<string, string>(Path.Combine(directory, BagsFile), bagsText),
                new KeyValuePair<string, string>(Path.Combine(directory, EventsFile), eventsText),
                new KeyValuePair<string, string>(Path.Combine(directory, ConfigFile), configText)
            };
            if (!options.AllowOverwrite)
                foreach (var target in targets)
                    if (File.Exists(target.Key))
                        throw new IOException("Export refused: " + target.Key + " already exists. Choose another folder or allow overwrite explicitly.");

            // Stage on the same filesystem and keep originals until ALL files are published.
            // The private folder also owns partial writes if StreamWriter.Write/Dispose fails.
            string staging = Path.Combine(directory, ".bagflow-export-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(staging);
            var published = new List<string>();
            var backups = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var target in targets)
                {
                    string temp = Path.Combine(staging, Path.GetFileName(target.Key));
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, Utf8)) writer.Write(target.Value);
                }
                foreach (var target in targets)
                {
                    if (options.AllowOverwrite && File.Exists(target.Key))
                    {
                        string backup = Path.Combine(staging, Path.GetFileName(target.Key) + ".bak");
                        File.Move(target.Key, backup);
                        backups.Add(new KeyValuePair<string, string>(target.Key, backup));
                    }
                    File.Move(Path.Combine(staging, Path.GetFileName(target.Key)), target.Key);
                    published.Add(target.Key);
                }
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                for (int i = published.Count - 1; i >= 0; i--)
                    try { File.Delete(published[i]); }
                    catch (Exception recoveryError) { failures.Add(recoveryError); }
                for (int i = backups.Count - 1; i >= 0; i--)
                    try { File.Move(backups[i].Value, backups[i].Key); }
                    catch (Exception recoveryError) { failures.Add(recoveryError); }
                // Never remove the only surviving copy if filesystem access also prevents rollback.
                if (failures.Count > 1)
                    throw new IOException("Export failed and recovery was incomplete. Preserve the recovery folder: " +
                        Path.GetFullPath(staging), new AggregateException(failures));
                TryDeleteDirectory(staging);
                throw;
            }
            TryDeleteDirectory(staging);

            int flagged = 0, occurrences = 0;
            foreach (var bag in bags) if (bag.IsFlagged == true) flagged++;
            foreach (var e in events) if (e.EventType == SimulationEventType.BlockageOccurrence) occurrences++;
            return new ExportResult
            {
                Directory = Path.GetFullPath(directory), BagsPath = Path.GetFullPath(targets[0].Key),
                EventsPath = Path.GetFullPath(targets[1].Key), ConfigPath = Path.GetFullPath(targets[2].Key),
                BagCount = bags.Count, EventCount = events.Count, FlaggedCount = flagged, BlockageOccurrences = occurrences
            };
        }

        // ---- Content ------------------------------------------------------------------------------------

        public static string BagsCsv(IReadOnlyList<BagRecord> bags)
        {
            var builder = new StringBuilder(CsvFormat.Row(BagColumns));
            foreach (var bag in bags) builder.Append(CsvFormat.Row(BagFields(bag)));
            return builder.ToString();
        }

        /// <summary>One bags.csv row. Values that do not exist yet (bag not scanned, not completed) are empty.</summary>
        public static string[] BagFields(BagRecord bag)
        {
            if (bag == null) throw new ArgumentException("Null bag record.");
            bool started = bag.TransportStartTimeSeconds.HasValue;
            bool scanned = bag.ScanStartTimeSeconds.HasValue;
            double? wait = scanned && bag.QueueEnterTimeSeconds.HasValue
                ? bag.ScanStartTimeSeconds.Value - bag.QueueEnterTimeSeconds.Value : (double?)null;
            double? total = bag.CompletionTimeSeconds.HasValue
                ? bag.CompletionTimeSeconds.Value - bag.ArrivalTimeSeconds : (double?)null;
            return new[]
            {
                CsvFormat.Integer(bag.Id),
                CsvFormat.Number(bag.WeightKg),
                CsvFormat.Number(bag.NominalTransportSeconds),
                started ? CsvFormat.Number(bag.ObservedTransportSeconds) : string.Empty,
                scanned ? CsvFormat.Number(bag.InspectionSeconds) : string.Empty,
                CsvFormat.Number(bag.ArrivalTimeSeconds),
                CsvFormat.Number(bag.QueueEnterTimeSeconds),
                CsvFormat.Number(bag.ScanStartTimeSeconds),
                CsvFormat.Number(bag.CompletionTimeSeconds),
                CsvFormat.Number(wait),
                CsvFormat.Number(total),
                CsvFormat.Flag(bag.IsFlagged)
            };
        }

        public static string EventsCsv(IReadOnlyList<SimulationEventRecord> events)
        {
            var builder = new StringBuilder(CsvFormat.Row(EventColumns));
            foreach (var e in events) builder.Append(CsvFormat.Row(EventFields(e)));
            return builder.ToString();
        }

        public static string[] EventFields(SimulationEventRecord e)
        {
            if (e == null) throw new ArgumentException("Null event record.");
            return new[]
            {
                CsvFormat.Number(e.SimulationTimeSeconds),
                e.EventType.ToString(),
                CsvFormat.Integer(e.BagId),
                CsvFormat.Text(e.Detail),
                CsvFormat.Number(e.ScheduledTimeSeconds)
            };
        }

        public static string ConfigJson(SimulationConfig config, IReadOnlyList<BagRecord> bags,
            IReadOnlyList<SimulationEventRecord> events, string appVersion, DateTime exportedUtc)
        {
            int approved = 0, flagged = 0, completed = 0, occurrences = 0;
            double end = 0;
            foreach (var bag in bags)
            {
                if (bag.State == BagState.Approved) approved++;
                if (bag.State == BagState.Flagged) flagged++;
                if (bag.CompletionTimeSeconds.HasValue) completed++;
            }
            foreach (var e in events)
            {
                if (e.EventType == SimulationEventType.BlockageOccurrence) occurrences++;
                end = Math.Max(end, e.SimulationTimeSeconds);
            }

            var b = new StringBuilder("{\n");
            Pair(b, "schema_version", CsvFormat.JsonString(SchemaVersion));
            Pair(b, "app", CsvFormat.JsonString("BAGFLOW"));
            Pair(b, "app_version", CsvFormat.JsonString(appVersion ?? "unknown"));
            Pair(b, "exported_utc", CsvFormat.JsonString(exportedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
            Pair(b, "prng_algorithm", CsvFormat.JsonString(DeterministicRandomStreamProvider.Algorithm));
            Pair(b, "seed", CsvFormat.Integer(config.Seed));
            b.Append("  \"random_streams\": [");
            var streams = (RandomStream[])Enum.GetValues(typeof(RandomStream));
            for (int i = 0; i < streams.Length; i++) b.Append(i > 0 ? ", " : "").Append(CsvFormat.JsonString(streams[i].ToString()));
            b.Append("],\n");
            b.Append("  \"parameters\": {\n");
            FieldInfo[] fields = typeof(SimulationConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                object value = fields[i].GetValue(config);
                string json = value is double ? CsvFormat.Number((double)value)
                    : value is int ? CsvFormat.Integer((int)value)
                    : CsvFormat.JsonString(Convert.ToString(value, CultureInfo.InvariantCulture));
                b.Append("    ").Append(CsvFormat.JsonString(fields[i].Name)).Append(": ").Append(json)
                    .Append(i < fields.Length - 1 ? ",\n" : "\n");
            }
            b.Append("  },\n");
            b.Append("  \"units\": {\"time\": \"s\", \"weight\": \"kg\", \"rate\": \"1/s\", \"probability\": \"[0,1]\"},\n");
            b.Append("  \"summary\": {");
            b.Append("\"bag_count\": ").Append(CsvFormat.Integer(bags.Count));
            b.Append(", \"event_count\": ").Append(CsvFormat.Integer(events.Count));
            b.Append(", \"completed\": ").Append(CsvFormat.Integer(completed));
            b.Append(", \"approved\": ").Append(CsvFormat.Integer(approved));
            b.Append(", \"flagged\": ").Append(CsvFormat.Integer(flagged));
            b.Append(", \"blockage_occurrences\": ").Append(CsvFormat.Integer(occurrences));
            b.Append(", \"last_event_time_s\": ").Append(CsvFormat.Number(end));
            b.Append("},\n");
            b.Append("  \"files\": {\"bags\": \"").Append(BagsFile).Append("\", \"events\": \"").Append(EventsFile).Append("\"}\n");
            b.Append("}\n");
            return b.ToString();
        }

        private static void Pair(StringBuilder b, string key, string jsonValue)
        {
            b.Append("  ").Append(CsvFormat.JsonString(key)).Append(": ").Append(jsonValue).Append(",\n");
        }

        // ---- Helpers --------------------------------------------------------------------------------------

        public static SimulationConfig CopyConfig(SimulationConfig source)
        {
            RequireConfig(source);
            var copy = new SimulationConfig();
            foreach (var field in typeof(SimulationConfig).GetFields()) field.SetValue(copy, field.GetValue(source));
            return copy;
        }

        private static void RequireConfig(SimulationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
        }

        private static void RequireUniqueIds(IReadOnlyList<BagRecord> bags)
        {
            var ids = new HashSet<int>();
            foreach (var bag in bags)
            {
                if (bag == null) throw new ArgumentException("Null bag record.");
                if (!ids.Add(bag.Id)) throw new ArgumentException("Duplicate bag id " + bag.Id + " in export.");
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { System.IO.Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path) && System.IO.Directory.GetFileSystemEntries(path).Length == 0)
                    System.IO.Directory.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
