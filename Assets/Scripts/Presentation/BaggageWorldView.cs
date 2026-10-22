using System.Collections.Generic;
using UnityEngine;

namespace AirportBaggageSimulator
{
    /// <summary>Normalised (0..1) anchors of the top-down plan, shared by factory and world view.
    /// The layout is didactic: no physical times are derived from these coordinates.</summary>
    public static class WorldLayout
    {
        public static readonly Vector2 EntryGate = new Vector2(0.085f, 0.50f);
        public static readonly Vector2 BeltStart = new Vector2(0.165f, 0.50f);
        public static readonly Vector2 BeltEnd = new Vector2(0.455f, 0.50f);
        public static readonly Vector2 Scanner = new Vector2(0.79f, 0.50f);
        public const float QueueHeadX = 0.685f;
        public const float QueueSpacingX = 0.034f;
        public const float QueueY = 0.50f;
        public const float WaitingBaseY = 0.71f;
        public const float WaitingSpacingY = 0.062f;
        public const int MaxWaitingVisible = 4;
        public const int MaxQueueVisible = 6;
        public const int MaxExitVisible = 8;
        public const float BagWidthFraction = 0.032f;
        public const float BagAspect = 0.72f;

        public static Vector2 ExitSlot(bool flagged, int slot)
        {
            int column = slot % 2;
            int row = slot / 2;
            float x = 0.915f + column * 0.042f;
            float top = flagged ? 0.35f : 0.80f;
            return new Vector2(x, top - row * 0.058f);
        }
    }

    /// <summary>ISimulationWorldView: maps engine snapshots onto pooled bag visuals. It never alters records,
    /// never draws random numbers and never decides outcomes; positions come from the engine's progress,
    /// FIFO position and state fields.</summary>
    [DisallowMultipleComponent]
    public sealed class BaggageWorldView : MonoBehaviour, ISimulationWorldView
    {
        [SerializeField] private RectTransform bagLayer;
        [SerializeField] private BaggageSpriteView bagPrefab;
        [SerializeField] private BeltView belt;
        [SerializeField] private ScannerView scanner;
        [SerializeField] private StationView entry;
        [SerializeField] private StationView queue;
        [SerializeField] private StationView approvedLane;
        [SerializeField] private StationView flaggedLane;

        private static readonly Vector2 FallbackSize = new Vector2(1300f, 880f);

        private readonly Dictionary<int, BaggageSpriteView> views = new Dictionary<int, BaggageSpriteView>();
        private readonly List<BaggageSpriteView> active = new List<BaggageSpriteView>();
        private readonly BagRecord[][] exits = { new BagRecord[WorldLayout.MaxExitVisible], new BagRecord[WorldLayout.MaxExitVisible] };
        private readonly int[] exitCount = new int[2];
        private BaggageViewPool pool;
        private SimulationSnapshot lastSnapshot;
        private Vector2 layerSize;
        private Vector2 bagSize;
        private int stamp;

        public BeltView Belt { get { return belt; } }
        public ScannerView Scanner { get { return scanner; } }
        public StationView Entry { get { return entry; } }
        public StationView Queue { get { return queue; } }
        public StationView ApprovedLane { get { return approvedLane; } }
        public StationView FlaggedLane { get { return flaggedLane; } }
        public int ActiveBagCount { get { return active.Count; } }
        public int PoolCreatedCount { get { return pool == null ? 0 : pool.TotalCreated; } }
        public int RenderCount { get; private set; }

        internal void Wire(RectTransform layer, BaggageSpriteView prefab, BeltView beltView, ScannerView scannerView,
            StationView entryView, StationView queueView, StationView approved, StationView flagged)
        {
            bagLayer = layer; bagPrefab = prefab; belt = beltView; scanner = scannerView;
            entry = entryView; queue = queueView; approvedLane = approved; flaggedLane = flagged;
        }

        public bool TryGetView(int bagId, out BaggageSpriteView view) { return views.TryGetValue(bagId, out view); }

        public int CountActive(BagState state)
        {
            int count = 0;
            for (int i = 0; i < active.Count; i++) if (active[i].State == state) count++;
            return count;
        }

        public Vector2 ToLayer(Vector2 normalised)
        {
            return new Vector2(normalised.x * layerSize.x, normalised.y * layerSize.y);
        }

        private void EnsureReady()
        {
            if (pool == null) pool = new BaggageViewPool(bagPrefab, bagLayer);
            RefreshSize();
        }

        private bool RefreshSize()
        {
            Vector2 size = bagLayer.rect.size;
            if (size.x < 10f || size.y < 10f) size = FallbackSize;
            if ((size - layerSize).sqrMagnitude < 0.25f) return false;
            layerSize = size;
            float width = Mathf.Max(26f, size.x * WorldLayout.BagWidthFraction);
            bagSize = new Vector2(width, width * WorldLayout.BagAspect);
            for (int i = 0; i < active.Count; i++) active[i].SetSize(bagSize);
            return true;
        }

        public void ResetView()
        {
            EnsureReady();
            ReleaseAll();
            lastSnapshot = null;
            belt.ResetView();
            scanner.SetState(false, 0f);
            entry.Set("Criadas 0 / 0", "À espera: 0");
            queue.Set("Fila FIFO: 0", "");
            approvedLane.Set("0", "");
            flaggedLane.Set("0", "");
        }

        public void Render(SimulationSnapshot snapshot)
        {
            EnsureReady();
            lastSnapshot = snapshot;
            RenderCount++;
            stamp++;
            exitCount[0] = 0;
            exitCount[1] = 0;
            exitTotal[0] = 0;
            exitTotal[1] = 0;
            IReadOnlyList<BagRecord> bags = snapshot == null ? null : snapshot.Bags;
            int total = bags == null ? 0 : bags.Count;
            int waiting = 0, onBelt = 0, inQueue = 0, queueHidden = 0, created = 0;
            bool scanning = false;
            float scanProgress = 0f;

            for (int i = 0; i < total; i++)
            {
                BagRecord bag = bags[i];
                if (bag == null || bag.State == BagState.Scheduled) continue;
                created++;
                switch (bag.State)
                {
                    case BagState.WaitingEntry:
                        if (waiting < WorldLayout.MaxWaitingVisible)
                            Show(bag, ToLayer(new Vector2(WorldLayout.EntryGate.x, WorldLayout.WaitingBaseY + waiting * WorldLayout.WaitingSpacingY)), true);
                        waiting++;
                        break;
                    case BagState.Transporting:
                        onBelt++;
                        float t = Mathf.Clamp01((float)bag.TransportProgress01);
                        Show(bag, ToLayer(Vector2.Lerp(WorldLayout.BeltStart, WorldLayout.BeltEnd, t)), t < 0.02f);
                        break;
                    case BagState.ScannerQueue:
                        inQueue++;
                        if (bag.QueuePosition >= 0 && bag.QueuePosition < WorldLayout.MaxQueueVisible)
                            Show(bag, ToLayer(new Vector2(WorldLayout.QueueHeadX - bag.QueuePosition * WorldLayout.QueueSpacingX, WorldLayout.QueueY)), false);
                        else queueHidden++;
                        break;
                    case BagState.Scanning:
                        scanning = true;
                        scanProgress = (float)bag.ScanProgress01;
                        Show(bag, ToLayer(WorldLayout.Scanner), false).SetProgress(scanProgress, true);
                        break;
                    case BagState.Approved:
                        CollectExit(0, bag);
                        break;
                    case BagState.Flagged:
                        CollectExit(1, bag);
                        break;
                }
            }

            PlaceExits(false);
            PlaceExits(true);
            ReleaseUnseen();

            SimulationMetrics m = snapshot == null ? null : snapshot.Metrics;
            bool isComplete = snapshot != null && snapshot.IsComplete;
            bool blocked = snapshot != null && snapshot.BeltBlocked;
            double time = snapshot == null ? 0 : snapshot.SimulationTimeSeconds;
            int shownCreated = m != null ? m.Created : created;
            int shownWaiting = m != null ? m.WaitingEntry : waiting;
            int shownOnBelt = m != null ? m.OnBelt : onBelt;
            int shownQueue = m != null ? m.QueueLength : inQueue;
            int shownApproved = m != null ? m.Approved : exitTotal[0];
            int shownFlagged = m != null ? m.Flagged : exitTotal[1];

            belt.Apply(time, blocked, created > 0 && !isComplete, shownOnBelt);
            scanner.SetState(scanning, scanProgress);
            entry.Set("Criadas " + shownCreated + " / " + total, "À espera: " + shownWaiting);
            queue.Set("Fila FIFO: " + shownQueue, queueHidden > 0 ? "+" + queueHidden + " por mostrar" : "");
            approvedLane.Set(shownApproved.ToString(), "");
            flaggedLane.Set(shownFlagged.ToString(), "");
        }

        private readonly int[] exitTotal = new int[2];

        private void CollectExit(int lane, BagRecord bag)
        {
            exitTotal[lane]++;
            BagRecord[] list = exits[lane];
            int count = exitCount[lane];
            int pos = count;
            // Keep the newest completions first (completion time, then id).
            while (pos > 0 && IsNewer(bag, list[pos - 1])) pos--;
            if (pos >= WorldLayout.MaxExitVisible) return;
            int last = Mathf.Min(count, WorldLayout.MaxExitVisible - 1);
            for (int i = last; i > pos; i--) list[i] = list[i - 1];
            list[pos] = bag;
            if (count < WorldLayout.MaxExitVisible) exitCount[lane] = count + 1;
        }

        private static bool IsNewer(BagRecord a, BagRecord b)
        {
            double ta = a.CompletionTimeSeconds ?? 0, tb = b.CompletionTimeSeconds ?? 0;
            return ta > tb || (ta == tb && a.Id > b.Id);
        }

        private void PlaceExits(bool flagged)
        {
            int lane = flagged ? 1 : 0;
            for (int slot = 0; slot < exitCount[lane]; slot++)
                Show(exits[lane][slot], ToLayer(WorldLayout.ExitSlot(flagged, slot)), false);
        }

        private BaggageSpriteView Show(BagRecord bag, Vector2 target, bool spawnAtGate)
        {
            BaggageSpriteView view;
            if (!views.TryGetValue(bag.Id, out view))
            {
                view = pool.Acquire();
                view.SetSize(bagSize);
                view.Begin(bag.Id, bag.State);
                views[bag.Id] = view;
                active.Add(view);
                if (spawnAtGate) { view.SnapTo(ToLayer(WorldLayout.EntryGate)); view.SetTarget(target, false); }
                else view.SetTarget(target, true);
            }
            else
            {
                view.ChangeState(bag.State);
                view.SetTarget(target, false);
            }
            if (bag.State != BagState.Scanning) view.SetProgress(0f, false);
            view.Stamp = stamp;
            return view;
        }

        private void ReleaseUnseen()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                BaggageSpriteView view = active[i];
                if (view.Stamp == stamp) continue;
                views.Remove(view.BagId);
                pool.Release(view);
                int last = active.Count - 1;
                active[i] = active[last];
                active.RemoveAt(last);
            }
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < active.Count; i++) pool.Release(active[i]);
            active.Clear();
            views.Clear();
        }

        /// <summary>Eases visuals towards the latest snapshot. Called from Update; public so tests can step it.</summary>
        public void Animate(float dt)
        {
            if (pool == null) return;
            if (RefreshSize() && lastSnapshot != null) Render(lastSnapshot);
            for (int i = 0; i < active.Count; i++) active[i].Animate(dt);
            belt.Animate(dt);
            scanner.Animate(dt);
        }

        private void Update() { Animate(Time.unscaledDeltaTime); }
    }
}
