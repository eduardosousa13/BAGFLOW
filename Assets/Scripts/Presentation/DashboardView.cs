using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>ISimulationDashboardView: shows engine-provided metrics only. Nothing is recomputed here
    /// except text formatting (and the total number of bags, which is the snapshot's bag count).</summary>
    [DisallowMultipleComponent]
    public sealed class DashboardView : MonoBehaviour, ISimulationDashboardView
    {
        private const float MessageSeconds = 10f;
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        [SerializeField] private Text simulatedTime;
        [SerializeField] private Text total;
        [SerializeField] private Text completed;
        [SerializeField] private Text approved;
        [SerializeField] private Text flagged;
        [SerializeField] private Text waitingEntry;
        [SerializeField] private Text onBelt;
        [SerializeField] private Text queueLength;
        [SerializeField] private Text scannerState;
        [SerializeField] private Text blockageOccurrences;
        [SerializeField] private Text blockedTime;
        [SerializeField] private Text meanWait;
        [SerializeField] private Text meanTotal;
        [SerializeField] private Text scannerUtilization;
        [SerializeField] private Image completionFill;
        [SerializeField] private Text statusChip;
        [SerializeField] private Image statusChipBack;
        [SerializeField] private Image banner;
        [SerializeField] private Text bannerText;

        private float messageTimer;

        /// <summary>Raised after every Render so other UI parts (control panel) can follow the run state.</summary>
        public event Action<SimulationSnapshot> Rendered;

        public string Status { get { return statusChip.text; } }
        public string BannerMessage { get { return banner.gameObject.activeSelf ? bannerText.text : string.Empty; } }
        public bool BannerIsError { get; private set; }
        public string SimulatedTimeText { get { return simulatedTime.text; } }
        public string TotalText { get { return total.text; } }
        public string CompletedText { get { return completed.text; } }
        public string ApprovedText { get { return approved.text; } }
        public string FlaggedText { get { return flagged.text; } }
        public string WaitingEntryText { get { return waitingEntry.text; } }
        public string OnBeltText { get { return onBelt.text; } }
        public string QueueText { get { return queueLength.text; } }
        public string ScannerText { get { return scannerState.text; } }
        public string BlockageText { get { return blockageOccurrences.text; } }
        public string BlockedTimeText { get { return blockedTime.text; } }
        public string MeanWaitText { get { return meanWait.text; } }
        public string MeanTotalText { get { return meanTotal.text; } }
        public string UtilizationText { get { return scannerUtilization.text; } }

        internal void Wire(Text[] values, Image fill, Text chip, Image chipBack, Image bannerImage, Text bannerLabel)
        {
            simulatedTime = values[0]; total = values[1]; completed = values[2]; approved = values[3];
            flagged = values[4]; waitingEntry = values[5]; onBelt = values[6]; queueLength = values[7];
            scannerState = values[8]; blockageOccurrences = values[9]; blockedTime = values[10];
            meanWait = values[11]; meanTotal = values[12]; scannerUtilization = values[13];
            completionFill = fill; statusChip = chip; statusChipBack = chipBack;
            banner = bannerImage; bannerText = bannerLabel;
            banner.gameObject.SetActive(false);
            Render(null);
        }

        public void Render(SimulationSnapshot snapshot)
        {
            var bags = snapshot == null ? null : snapshot.Bags;
            int count = bags == null ? 0 : bags.Count;
            var m = snapshot == null ? null : snapshot.Metrics;

            Set(simulatedTime, FormatClock(snapshot == null ? 0 : snapshot.SimulationTimeSeconds));
            Set(total, count.ToString(Culture));
            if (m == null)
            {
                SetAllDash();
            }
            else
            {
                Set(completed, m.Completed.ToString(Culture));
                Set(approved, m.Approved.ToString(Culture));
                Set(flagged, m.Flagged.ToString(Culture));
                Set(waitingEntry, m.WaitingEntry.ToString(Culture));
                Set(onBelt, m.OnBelt.ToString(Culture));
                Set(queueLength, m.QueueLength.ToString(Culture));
                Set(scannerState, m.Scanning > 0 ? "Ocupado" : "Livre");
                scannerState.color = m.Scanning > 0 ? UiTheme.ScannerBusy : UiTheme.Good;
                Set(blockageOccurrences, m.BlockageOccurrences.ToString(Culture));
                Set(blockedTime, Seconds(m.TotalBlockedSeconds));
                bool any = m.Created > 0;
                Set(meanWait, any ? Seconds(m.MeanQueueWaitSeconds) : "—");
                Set(meanTotal, any ? Seconds(m.MeanTotalSeconds) : "—");
                Set(scannerUtilization, any ? (m.ScannerUtilization * 100.0).ToString("0.0", Culture) + " %" : "—");
            }

            float fraction = m != null && count > 0 ? Mathf.Clamp01((float)m.Completed / count) : 0f;
            completionFill.rectTransform.anchorMax = new Vector2(fraction, 1f);
            ApplyStatus(snapshot, count);
            var handler = Rendered;
            if (handler != null) handler(snapshot);
        }

        private void ApplyStatus(SimulationSnapshot snapshot, int count)
        {
            string text;
            Color color;
            bool notStarted = snapshot == null || count == 0 ||
                (snapshot.SimulationTimeSeconds <= 0 && !snapshot.IsComplete); // t = 0: nothing has been simulated yet
            if (notStarted) { text = "PRONTO"; color = UiTheme.TextDim; }
            else if (snapshot.IsComplete) { text = "CONCLUÍDA"; color = UiTheme.Good; }
            else if (snapshot.BeltBlocked) { text = "TAPETE BLOQUEADO"; color = UiTheme.Bad; }
            else { text = "EM SIMULAÇÃO"; color = UiTheme.Accent; }
            Set(statusChip, text);
            statusChipBack.color = color;
        }

        private void SetAllDash()
        {
            Set(completed, "—"); Set(approved, "—"); Set(flagged, "—"); Set(waitingEntry, "—"); Set(onBelt, "—");
            Set(queueLength, "—"); Set(scannerState, "—"); Set(blockageOccurrences, "—"); Set(blockedTime, "—");
            Set(meanWait, "—"); Set(meanTotal, "—"); Set(scannerUtilization, "—");
            scannerState.color = UiTheme.Text;
        }

        public void ShowError(string message) { ShowMessage(message, true); }

        /// <summary>Non-error notice (e.g. successful export). Extra to the shared interface.</summary>
        public void ShowInfo(string message) { ShowMessage(message, false); }

        public void ClearMessage()
        {
            banner.gameObject.SetActive(false);
            messageTimer = 0f;
        }

        private void ShowMessage(string message, bool error)
        {
            if (string.IsNullOrEmpty(message)) { ClearMessage(); return; }
            BannerIsError = error;
            bannerText.text = message;
            banner.color = error ? UiTheme.Bad : UiTheme.Good;
            banner.gameObject.SetActive(true);
            messageTimer = MessageSeconds;
        }

        public void Tick(float dt)
        {
            if (messageTimer <= 0f) return;
            messageTimer -= dt;
            if (messageTimer <= 0f) banner.gameObject.SetActive(false);
        }

        private void Update() { Tick(Time.unscaledDeltaTime); }

        private static void Set(Text target, string value) { if (target.text != value) target.text = value; }

        private static string Seconds(double value) { return value.ToString("0.0", Culture) + " s"; }

        public static string FormatClock(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            int whole = (int)Math.Floor(seconds);
            int h = whole / 3600, mi = (whole % 3600) / 60, s = whole % 60;
            int tenth = (int)((seconds - Math.Floor(seconds)) * 10.0);
            return h > 0 ? string.Format(Culture, "{0}:{1:00}:{2:00}", h, mi, s)
                         : string.Format(Culture, "{0:00}:{1:00}.{2}", mi, s, tenth);
        }
    }
}
