using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirportBaggageSimulator.Tests
{
    public sealed class ExportTests
    {
        private string root;

        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "bagflow_export_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown] public void TearDown()
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { }
        }

        private static readonly DateTime Clock = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        private static ExportOptions Fixed(bool overwrite = false)
        {
            return new ExportOptions { AppVersion = "test", AllowOverwrite = overwrite, UtcNow = () => Clock };
        }

        private static SimulationRunResult RealRun(int seed = 42, int n = 60)
        {
            return ValidationBatchRunner.Run(new SimulationConfig { Seed = seed, BaggageCount = n });
        }

        // ---- CSV format ---------------------------------------------------------------------------------

        [Test] public void HeadersMatchTheContract()
        {
            string bags = SimulationExporter.BagsCsv(new BagRecord[0]);
            string events = SimulationExporter.EventsCsv(new SimulationEventRecord[0]);
            Assert.AreEqual("id,weight_kg,nominal_transport_s,observed_transport_s,inspection_s,arrival_s,queue_enter_s,scan_start_s,complete_s,queue_wait_s,total_s,flagged\n", bags);
            Assert.AreEqual("time_s,event_type,bag_id,detail,scheduled_time_s\n", events);
        }

        [Test] public void TextFieldsAreEscapedPerRfc4180()
        {
            Assert.AreEqual("plain", CsvFormat.Text("plain"));
            Assert.AreEqual("\"a,b\"", CsvFormat.Text("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", CsvFormat.Text("say \"hi\""));
            Assert.AreEqual("\"two\nlines\"", CsvFormat.Text("two\nlines"));
            Assert.AreEqual("\" edge\"", CsvFormat.Text(" edge"));
            Assert.AreEqual("", CsvFormat.Text(null));
            Assert.AreEqual("ação ✓", CsvFormat.Text("ação ✓"));
            var row = SimulationExporter.EventFields(new SimulationEventRecord
                { EventType = SimulationEventType.BagFlagged, SimulationTimeSeconds = 1.5, BagId = 3, Detail = "x,\"y\"" });
            Assert.AreEqual(new[] { "1.5", "BagFlagged", "3", "\"x,\"\"y\"\"\"", "" }, row);
        }

        [Test] public void MissingValuesAreEmptyFields()
        {
            var scheduled = new BagRecord { Id = 7, WeightKg = 20.25, NominalTransportSeconds = 9.5, ArrivalTimeSeconds = 30 };
            var fields = SimulationExporter.BagFields(scheduled);
            Assert.AreEqual(new[] { "7", "20.25", "9.5", "", "", "30", "", "", "", "", "", "" }, fields);

            var done = new BagRecord
            {
                Id = 1, WeightKg = 18, NominalTransportSeconds = 9, ObservedTransportSeconds = 10, InspectionSeconds = 4.25,
                ArrivalTimeSeconds = 0, TransportStartTimeSeconds = 0, QueueEnterTimeSeconds = 10, ScanStartTimeSeconds = 12.5,
                CompletionTimeSeconds = 16.8, IsFlagged = true, State = BagState.Flagged
            };
            fields = SimulationExporter.BagFields(done);
            Assert.AreEqual("4.25", fields[4]);
            Assert.AreEqual("2.5", fields[9]);
            Assert.AreEqual("16.8", fields[10]);
            Assert.AreEqual("1", fields[11]);
            done.IsFlagged = false;
            Assert.AreEqual("0", SimulationExporter.BagFields(done)[11]);
        }

        [TestCase("pt-PT")]
        [TestCase("de-DE")]
        [TestCase("fr-FR")]
        public void NumbersIgnoreTheSystemCulture(string culture)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Assert.AreEqual("1234.5", CsvFormat.Number(1234.5));
                Assert.AreEqual("-0.001", CsvFormat.Number(-0.001));
                Assert.AreEqual("1E-300", CsvFormat.Number(1e-300));
                var run = RealRun(7, 20);
                foreach (var line in SimulationExporter.BagsCsv(run.Bags).TrimEnd('\n').Split('\n'))
                    Assert.AreEqual(12, line.Split(',').Length, line);
                var json = (Dictionary<string, object>)MiniJson.Parse(SimulationExporter.ConfigJson(
                    new SimulationConfig(), run.Bags, run.Events, "x", Clock));
                Assert.AreEqual(1.0 / 120.0, (double)((Dictionary<string, object>)json["parameters"])["BlockageRatePerSecond"]);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        [Test] public void RawSamplesRoundTripExactly()
        {
            var sampler = new DistributionSampler(12345);
            for (int i = 0; i < 20000; i++)
            {
                double[] values = { sampler.Exponential(1 / 8.0), sampler.Triangular(6, 9, 13), sampler.TruncatedNormal(18, 4, 5, 32) };
                foreach (double v in values)
                    Assert.AreEqual(v, double.Parse(CsvFormat.Number(v), CultureInfo.InvariantCulture));
            }
            Assert.AreEqual(0.1, double.Parse(CsvFormat.Number(0.1), CultureInfo.InvariantCulture));
        }

        [Test] public void NonFiniteValuesAreRejected()
        {
            Assert.Throws<FormatException>(() => CsvFormat.Number(double.NaN));
            Assert.Throws<FormatException>(() => CsvFormat.Number(double.PositiveInfinity));
            var bag = new BagRecord { Id = 1, WeightKg = double.NaN };
            Assert.Throws<FormatException>(() => SimulationExporter.ExportToFolder(Path.Combine(root, "nan"),
                new SimulationConfig(), new[] { bag }, new SimulationEventRecord[0], Fixed()));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "nan")), "nothing written when formatting fails");
        }

        // ---- JSON ---------------------------------------------------------------------------------------

        [Test] public void JsonStringsAreEscaped()
        {
            Assert.AreEqual("\"a\\\"b\\\\c\\n\\t\\u0001é\"", CsvFormat.JsonString("a\"b\\c\n\t\u0001é"));
            Assert.AreEqual("null", CsvFormat.JsonString(null));
        }

        [Test] public void ConfigJsonIsValidAndContainsEveryParameterAndTheSeed()
        {
            var run = RealRun(42, 60);
            string json = SimulationExporter.ConfigJson(run.Config, run.Bags, run.Events, "v\"1", Clock);
            var parsed = (Dictionary<string, object>)MiniJson.Parse(json);
            Assert.AreEqual("1", parsed["schema_version"]);
            Assert.AreEqual(42.0, parsed["seed"]);
            Assert.AreEqual("v\"1", parsed["app_version"]);
            Assert.AreEqual(DeterministicRandomStreamProvider.Algorithm, parsed["prng_algorithm"]);
            Assert.AreEqual("2026-10-22T12:00:00Z", parsed["exported_utc"]);
            var parameters = (Dictionary<string, object>)parsed["parameters"];
            foreach (var field in typeof(SimulationConfig).GetFields())
            {
                Assert.IsTrue(parameters.ContainsKey(field.Name), field.Name);
                object value = field.GetValue(run.Config);
                if (value is double) Assert.AreEqual((double)value, (double)parameters[field.Name], field.Name);
                else if (value is int) Assert.AreEqual((double)(int)value, (double)parameters[field.Name], field.Name);
                else Assert.AreEqual(value.ToString(), parameters[field.Name], field.Name);
            }
            var summary = (Dictionary<string, object>)parsed["summary"];
            Assert.AreEqual(60.0, summary["bag_count"]);
            Assert.AreEqual((double)run.Metrics.Flagged, summary["flagged"]);
            Assert.AreEqual((double)run.Metrics.BlockageOccurrences, summary["blockage_occurrences"]);
            Assert.AreEqual(6, ((List<object>)parsed["random_streams"]).Count);
        }

        // ---- Files ----------------------------------------------------------------------------------------

        [Test] public void RealEngineExportIsConsistentWithTheEngine()
        {
            var run = RealRun(42, 60);
            var result = SimulationExporter.ExportToFolder(Path.Combine(root, "run"), run.Config, run.Bags, run.Events, Fixed());
            string[] bagLines = File.ReadAllLines(result.BagsPath);
            string[] eventLines = File.ReadAllLines(result.EventsPath);
            Assert.AreEqual(61, bagLines.Length);
            Assert.AreEqual(run.Events.Count + 1, eventLines.Length);
            Assert.AreEqual(run.Metrics.Flagged, result.FlaggedCount);
            Assert.AreEqual(run.Metrics.BlockageOccurrences, result.BlockageOccurrences);

            var inv = CultureInfo.InvariantCulture;
            int flagged = 0;
            var ids = new HashSet<int>();
            foreach (string line in bagLines.Skip(1))
            {
                string[] f = line.Split(',');
                Assert.AreEqual(12, f.Length);
                Assert.IsTrue(ids.Add(int.Parse(f[0], inv)));
                double weight = double.Parse(f[1], inv), nominal = double.Parse(f[2], inv), observed = double.Parse(f[3], inv);
                Assert.That(weight, Is.InRange(5.0, 32.0));
                Assert.That(nominal, Is.InRange(6.0, 13.0));
                Assert.GreaterOrEqual(observed + 1e-9, nominal, "blockages can only lengthen the observed transport");
                Assert.Greater(double.Parse(f[4], inv), 0);
                double wait = double.Parse(f[9], inv), total = double.Parse(f[10], inv);
                Assert.GreaterOrEqual(wait, 0);
                Assert.AreEqual(double.Parse(f[7], inv) - double.Parse(f[6], inv), wait, 1e-9);
                Assert.AreEqual(double.Parse(f[8], inv) - double.Parse(f[5], inv), total, 1e-9);
                if (f[11] == "1") flagged++; else Assert.AreEqual("0", f[11]);
            }
            Assert.AreEqual(run.Metrics.Flagged, flagged);
            Assert.AreEqual(run.Events.Count(e => e.EventType == SimulationEventType.BagFlagged), flagged);

            double previous = 0;
            int occurrences = 0;
            foreach (string line in eventLines.Skip(1))
            {
                string[] f = line.Split(',');
                Assert.AreEqual(5, f.Length);
                if (f[1] != "BlockageOccurrence") continue;
                occurrences++;
                double scheduled = double.Parse(f[4], inv);
                Assert.GreaterOrEqual(scheduled, previous);
                Assert.LessOrEqual(scheduled, double.Parse(f[0], inv) + 1e-9, "occurrence is applied at or after its instant");
                previous = scheduled;
            }
            Assert.AreEqual(run.Metrics.BlockageOccurrences, occurrences);
            Assert.IsFalse(Directory.GetFiles(result.Directory, "*.tmp").Any());
        }

        [Test] public void ExistingResultsAreNeverReplacedSilently()
        {
            var run = RealRun(1, 5);
            string dir = Path.Combine(root, "fixed");
            SimulationExporter.ExportToFolder(dir, run.Config, run.Bags, run.Events, Fixed());
            File.AppendAllText(Path.Combine(dir, "bags.csv"), "marker\n");
            string before = File.ReadAllText(Path.Combine(dir, "bags.csv"));
            var error = Assert.Throws<IOException>(() => SimulationExporter.ExportToFolder(dir, run.Config, run.Bags, run.Events, Fixed()));
            StringAssert.Contains("already exists", error.Message);
            Assert.AreEqual(before, File.ReadAllText(Path.Combine(dir, "bags.csv")));
            SimulationExporter.ExportToFolder(dir, run.Config, run.Bags, run.Events, Fixed(true));
            Assert.AreNotEqual(before, File.ReadAllText(Path.Combine(dir, "bags.csv")));

            var a = SimulationExporter.ExportToNewFolder(root, run.Config, run.Bags, run.Events, Fixed());
            var b = SimulationExporter.ExportToNewFolder(root, run.Config, run.Bags, run.Events, Fixed());
            Assert.AreNotEqual(a.Directory, b.Directory);
            StringAssert.EndsWith("bagflow_20261022_120000_seed1_n5", a.Directory);
            StringAssert.EndsWith("bagflow_20261022_120000_seed1_n5_2", b.Directory);
        }

        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        public void FailedPublicationRestoresOriginalsAndRemovesNewFiles(int collisionIndex, bool overwrite)
        {
            var run = RealRun(3, 1);
            string dir = Path.Combine(root, "rollback");
            Directory.CreateDirectory(dir);
            string[] names = { "bags.csv", "events.csv", "config.json" };
            var originals = new Dictionary<string, byte[]>();
            for (int i = 0; i < names.Length; i++)
            {
                string path = Path.Combine(dir, names[i]);
                if (i == collisionIndex) Directory.CreateDirectory(path);
                else if (overwrite)
                {
                    byte[] bytes = { 0, 255, (byte)i, 13, 10 };
                    File.WriteAllBytes(path, bytes);
                    originals.Add(path, bytes);
                }
            }
            string unrelated = Path.Combine(dir, "keep.txt");
            File.WriteAllText(unrelated, "unrelated result");

            // A directory at the second/third target fails AFTER earlier files were published.
            Assert.Throws<IOException>(() => SimulationExporter.ExportToFolder(dir,
                run.Config, run.Bags, run.Events, Fixed(overwrite)));
            foreach (var original in originals)
                CollectionAssert.AreEqual(original.Value, File.ReadAllBytes(original.Key));
            Assert.AreEqual(originals.Count + 1, Directory.GetFiles(dir).Length);
            Assert.AreEqual(1, Directory.GetDirectories(dir).Length, "no staging or backup folder left behind");
            Assert.IsTrue(Directory.Exists(Path.Combine(dir, names[collisionIndex])));
            Assert.AreEqual("unrelated result", File.ReadAllText(unrelated));

            Directory.Delete(Path.Combine(dir, names[collisionIndex]));
            SimulationExporter.ExportToFolder(dir, run.Config, run.Bags, run.Events, Fixed(overwrite));
            Assert.AreEqual(0, Directory.GetDirectories(dir).Length);
            Assert.AreEqual(4, Directory.GetFiles(dir).Length);
            Assert.AreEqual(SimulationExporter.BagsCsv(run.Bags), File.ReadAllText(Path.Combine(dir, "bags.csv")));
        }

        [Test] public void IoAndInputErrorsAreReportedWithoutPartialOutput()
        {
            var run = RealRun(3, 5);
            string file = Path.Combine(root, "not_a_folder");
            File.WriteAllText(file, "x");
            Assert.Throws<IOException>(() => SimulationExporter.ExportToNewFolder(file, run.Config, run.Bags, run.Events, Fixed()));
            Assert.Throws<IOException>(() => SimulationExporter.ExportToFolder(file, run.Config, run.Bags, run.Events, Fixed()));

            var duplicate = new[] { new BagRecord { Id = 1 }, new BagRecord { Id = 1 } };
            Assert.Throws<ArgumentException>(() => SimulationExporter.ExportToNewFolder(root, run.Config, duplicate, run.Events, Fixed()));
            Assert.AreEqual(0, Directory.GetDirectories(root).Length, "failed exports leave no folder behind");
            Assert.Throws<ArgumentNullException>(() => SimulationExporter.ExportToFolder(root, null, run.Bags, run.Events, Fixed()));
            Assert.Throws<ArgumentException>(() => SimulationExporter.ExportToFolder(" ", run.Config, run.Bags, run.Events, Fixed()));
        }

        // ---- Batch collector --------------------------------------------------------------------------------

        [Test] public void BatchRunsAreIndependentReproducibleAndConsistent()
        {
            var template = new SimulationConfig { BaggageCount = 10, BlockageRatePerSecond = 0.05 };
            int[] seeds = ValidationBatchRunner.Seeds(500, 6);
            string a = ValidationBatchRunner.RunBatch(Path.Combine(root, "a"), "a", template, seeds, 2, Fixed());
            string b = ValidationBatchRunner.RunBatch(Path.Combine(root, "b"), "b", template, seeds, 0, Fixed());
            foreach (var name in new[] { "runs.csv", "batch_bags.csv", "blockages.csv" })
                Assert.AreEqual(File.ReadAllText(Path.Combine(a, name)), File.ReadAllText(Path.Combine(b, name)), name);
            Assert.IsTrue(File.Exists(Path.Combine(a, "runs", "seed_501", "config.json")));
            Assert.IsFalse(Directory.Exists(Path.Combine(a, "runs", "seed_502")));
            MiniJson.Parse(File.ReadAllText(Path.Combine(a, "batch.json")));

            var runs = File.ReadAllLines(Path.Combine(a, "runs.csv"));
            Assert.AreEqual(7, runs.Length);
            Assert.AreEqual(string.Join(",", ValidationBatchRunner.RunColumns), runs[0]);
            var bagRows = File.ReadAllLines(Path.Combine(a, "batch_bags.csv")).Skip(1).Select(l => l.Split(',')).ToList();
            var blockageRows = File.ReadAllLines(Path.Combine(a, "blockages.csv")).Skip(1).Select(l => l.Split(',')).ToList();
            Assert.AreEqual(60, bagRows.Count);
            var distinctFlags = new HashSet<string>();
            foreach (string line in runs.Skip(1))
            {
                string[] f = line.Split(',');
                string seed = f[1];
                int flagged = int.Parse(f[10]);
                Assert.AreEqual(flagged, bagRows.Count(r => r[0] == seed && r[12] == "1"), "flagged per run = sum of Bernoulli");
                Assert.AreEqual(int.Parse(f[11]), blockageRows.Count(r => r[0] == seed));
                Assert.AreEqual(10, int.Parse(f[8]));
                distinctFlags.Add(f[7]);
            }
            Assert.Greater(distinctFlags.Count, 1, "different seeds give different runs");
            Assert.Throws<IOException>(() => ValidationBatchRunner.RunBatch(a, "a", template, seeds, 0, Fixed()));
            Assert.Throws<ArgumentException>(() => ValidationBatchRunner.RunBatch(Path.Combine(root, "c"), "c", template, new[] { 1, 1 }, 0, Fixed()));
        }

        // ---- Controller adapter (Play Mode) ----------------------------------------------------------------

        private sealed class Controls : ISimulationControlView
        {
            public event Action<SimulationConfig> StartRequested;
            public event Action PauseRequested;
            public event Action ResumeRequested;
            public event Action ResetRequested;
            public event Action<double> PlaybackSpeedRequested;
            public event Action ExportRequested;
            public bool Running;
            public string Error;
            public void SetRunning(bool running) { Running = running; }
            public void ShowValidationError(string message) { Error = message; }
            public void Start(SimulationConfig c) { StartRequested?.Invoke(c); }
            public void Export() { ExportRequested?.Invoke(); }
            public void Unused() { PauseRequested?.Invoke(); ResumeRequested?.Invoke(); ResetRequested?.Invoke(); PlaybackSpeedRequested?.Invoke(1); }
        }

        [UnityTest] public IEnumerator AdapterExportsThroughBindExporterAndNotifiesCompletion()
        {
            yield return new EnterPlayMode();
            var go = new GameObject("export test");
            string folder = Path.Combine(Path.GetTempPath(), "bagflow_adapter_" + Guid.NewGuid().ToString("N"));
            try
            {
                var controller = go.AddComponent<SimulationController>();
                var controls = new Controls();
                controller.Bind(new SimulationEngine(), null, null, controls);
                var adapter = go.AddComponent<SimulationCsvExportAdapter>();
                adapter.Configure(controller, folder);
                var notified = new List<string>();
                controller.ExportCompleted += notified.Add;

                controls.Start(new SimulationConfig { BaggageCount = 4, Seed = 9, PlaybackSpeed = 20 });
                Assert.IsTrue(controls.Running);
                controls.Export();
                StringAssert.Contains("Complete a simulation", controls.Error, "export before completion is refused by M1");
                controls.Error = null;
                controls.Start(new SimulationConfig { BaggageCount = 4, Seed = 9, PlaybackSpeed = 20 });
                for (int frame = 0; frame < 1000 && controls.Running; frame++) controller.Advance(1);
                Assert.IsFalse(controls.Running);

                controls.Export();
                Assert.IsNull(controls.Error, controls.Error);
                Assert.AreEqual(1, notified.Count);
                Assert.AreEqual(adapter.LastExportDirectory, notified[0]);
                Assert.IsTrue(File.Exists(Path.Combine(notified[0], "bags.csv")));
                Assert.AreEqual(5, File.ReadAllLines(Path.Combine(notified[0], "bags.csv")).Length);
                StringAssert.Contains("\"seed\": 9", File.ReadAllText(Path.Combine(notified[0], "config.json")));

                controls.Export();
                Assert.AreEqual(2, notified.Count);
                Assert.AreNotEqual(notified[0], notified[1], "a second export never overwrites the first");

                adapter.enabled = false;
                controls.Export();
                StringAssert.Contains("exporter adapter is not connected", controls.Error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { }
            }
            yield return new ExitPlayMode();
        }
    }

    /// <summary>Minimal strict JSON parser for tests (objects, arrays, strings, numbers, true/false/null).</summary>
    internal static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            object value = Value(text, ref i);
            Skip(text, ref i);
            if (i != text.Length) throw new FormatException("Trailing characters at " + i);
            return value;
        }

        private static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var o = new Dictionary<string, object>(); i++; Skip(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    Skip(s, ref i);
                    string key = (string)Value(s, ref i);
                    Skip(s, ref i); Expect(s, ref i, ':');
                    if (o.ContainsKey(key)) throw new FormatException("Duplicate key " + key);
                    o[key] = Value(s, ref i); Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    Expect(s, ref i, '}'); return o;
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++; Skip(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i)); Skip(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    Expect(s, ref i, ']'); return a;
                }
            }
            if (c == '"')
            {
                var b = new System.Text.StringBuilder(); i++;
                while (s[i] != '"')
                {
                    if (s[i] < 0x20) throw new FormatException("Raw control character in string");
                    if (s[i] == '\\')
                    {
                        i++;
                        switch (s[i])
                        {
                            case 'n': b.Append('\n'); break; case 't': b.Append('\t'); break;
                            case 'r': b.Append('\r'); break; case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break;
                            case 'u': b.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                            case '"': case '\\': case '/': b.Append(s[i]); break;
                            default: throw new FormatException("Bad escape");
                        }
                        i++;
                    }
                    else b.Append(s[i++]);
                }
                i++; return b.ToString();
            }
            foreach (var literal in new[] { "true", "false", "null" })
                if (string.CompareOrdinal(s, i, literal, 0, literal.Length) == 0)
                { i += literal.Length; return literal == "null" ? null : (object)(literal == "true"); }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("Unexpected character " + c + " at " + i);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException("Expected " + c + " at " + i);
            i++;
        }
    }
}
