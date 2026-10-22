using System.Collections.Generic;

namespace AirportBaggageSimulator
{
    /// <summary>Artificial, static snapshots for the VisualSandbox and tests. They stand in for the engine:
    /// they are never used by the production scene and contain no randomness.</summary>
    public static class SandboxScenarios
    {
        public static readonly string[] Names =
        {
            "Vazio (0 malas)", "N=1 a transportar", "50 a transportar", "Fila FIFO + scanner a 50%",
            "Bloqueio ativo", "Scanner livre com fila vazia", "5 malas terminais", "N=60 a meio", "N=500 a meio", "Concluída (N=60)"
        };

        public static int Count { get { return Names.Length; } }

        public static SimulationSnapshot Build(int index)
        {
            switch (index)
            {
                case 0: return Finish(new List<BagRecord>(), 0, false, false);
                case 1: return Finish(new List<BagRecord> { Transporting(1, 0.4) }, 4.0, false, false);
                case 2:
                    {
                        var bags = new List<BagRecord>();
                        for (int i = 1; i <= 50; i++) bags.Add(Transporting(i, (i - 0.5) / 50.0));
                        return Finish(bags, 60, false, false);
                    }
                case 3:
                    {
                        var bags = new List<BagRecord>();
                        bags.Add(Scanning(1, 0.5));
                        for (int i = 0; i < 8; i++) bags.Add(Queued(i + 2, i));
                        bags.Add(Transporting(10, 0.7));
                        bags.Add(Transporting(11, 0.2));
                        return Finish(bags, 70, false, false);
                    }
                case 4:
                    {
                        var bags = new List<BagRecord>();
                        for (int i = 1; i <= 5; i++) bags.Add(Transporting(i, i * 0.16));
                        bags.Add(Queued(6, 0));
                        return Finish(bags, 95, true, false);
                    }
                case 5:
                    {
                        var bags = new List<BagRecord>();
                        bags.Add(Terminal(1, BagState.Approved, 20));
                        bags.Add(Transporting(2, 0.5));
                        return Finish(bags, 30, false, false);
                    }
                case 6:
                    {
                        var bags = new List<BagRecord>();
                        for (int i = 1; i <= 5; i++) bags.Add(Terminal(i, i % 4 == 0 ? BagState.Flagged : BagState.Approved, 10 + i * 8));
                        bags[4] = Terminal(5, BagState.Flagged, 50);
                        return Finish(bags, 60, false, false);
                    }
                case 7: return Mix(60, 0.45, false, false);
                case 8: return Mix(500, 0.5, false, false);
                default: return Mix(60, 1.0, false, true);
            }
        }

        /// <summary>N bags partitioned by progress: terminal first, then scanner, queue, belt, waiting, scheduled.</summary>
        public static SimulationSnapshot Mix(int n, double progress, bool blocked, bool complete)
        {
            var bags = new List<BagRecord>(n);
            int done = complete ? n : (int)(n * progress * 0.8);
            int cursor = 0;
            for (int i = 1; i <= done; i++, cursor++)
                bags.Add(Terminal(i, i % 12 == 0 ? BagState.Flagged : BagState.Approved, 5.0 + i * 3.0));
            int scanning = !complete && cursor < n ? 1 : 0;
            if (scanning == 1) { bags.Add(Scanning(++cursor, 0.5)); }
            int queue = complete ? 0 : System.Math.Min(7, n - cursor);
            for (int i = 0; i < queue; i++) bags.Add(Queued(++cursor, i));
            int belt = complete ? 0 : System.Math.Min(6, n - cursor);
            for (int i = 0; i < belt; i++) bags.Add(Transporting(++cursor, (i + 0.5) / 6.0));
            int waiting = complete ? 0 : System.Math.Min(3, n - cursor);
            for (int i = 0; i < waiting; i++) bags.Add(Record(++cursor, BagState.WaitingEntry));
            while (cursor < n) bags.Add(Record(++cursor, BagState.Scheduled));
            return Finish(bags, 5.0 + done * 3.0, blocked, complete);
        }

        private static BagRecord Record(int id, BagState state)
        {
            return new BagRecord { Id = id, State = state, WeightKg = 15 + id % 10 };
        }

        private static BagRecord Transporting(int id, double progress)
        {
            var b = Record(id, BagState.Transporting);
            b.TransportProgress01 = progress;
            return b;
        }

        private static BagRecord Queued(int id, int position)
        {
            var b = Record(id, BagState.ScannerQueue);
            b.TransportProgress01 = 1;
            b.QueuePosition = position;
            return b;
        }

        private static BagRecord Scanning(int id, double progress)
        {
            var b = Record(id, BagState.Scanning);
            b.TransportProgress01 = 1;
            b.ScanProgress01 = progress;
            return b;
        }

        private static BagRecord Terminal(int id, BagState state, double completionTime)
        {
            var b = Record(id, state);
            b.TransportProgress01 = 1;
            b.ScanProgress01 = 1;
            b.CompletionTimeSeconds = completionTime;
            b.IsFlagged = state == BagState.Flagged;
            return b;
        }

        /// <summary>Fills metrics from the artificial records (sandbox only; the engine does this in production).</summary>
        private static SimulationSnapshot Finish(List<BagRecord> bags, double time, bool blocked, bool complete)
        {
            var m = new SimulationMetrics();
            foreach (var b in bags)
            {
                if (b.State != BagState.Scheduled) m.Created++;
                switch (b.State)
                {
                    case BagState.WaitingEntry: m.WaitingEntry++; break;
                    case BagState.Transporting: m.OnBelt++; break;
                    case BagState.ScannerQueue: m.QueueLength++; break;
                    case BagState.Scanning: m.Scanning++; break;
                    case BagState.Approved: m.Approved++; m.Completed++; break;
                    case BagState.Flagged: m.Flagged++; m.Completed++; break;
                }
            }
            m.BlockageOccurrences = blocked ? 3 : 1;
            m.TotalBlockedSeconds = blocked ? 24 : 8;
            m.MeanQueueWaitSeconds = 11.5;
            m.MeanTotalSeconds = 31.2;
            m.ScannerUtilization = 0.62;
            return new SimulationSnapshot
            {
                SimulationTimeSeconds = time, BeltBlocked = blocked, IsComplete = complete,
                Metrics = m, Bags = bags
            };
        }
    }
}
