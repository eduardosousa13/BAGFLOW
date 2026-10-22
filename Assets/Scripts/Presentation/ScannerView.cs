using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>The single scanner: busy/free LED, label, progress bar and a sweep line.
    /// The sweep position is a function of the engine's ScanProgress01, so it freezes when paused.</summary>
    [DisallowMultipleComponent]
    public sealed class ScannerView : MonoBehaviour
    {
        [SerializeField] private Image body;
        [SerializeField] private Image led;
        [SerializeField] private Text statusLabel;
        [SerializeField] private Image sweep;
        [SerializeField] private RectTransform progressFill;
        [SerializeField] private Text progressLabel;

        private float displayProgress;

        public bool IsBusy { get; private set; }
        public float Progress01 { get; private set; }
        public string StatusText { get { return statusLabel.text; } }
        public Color LedColor { get { return led.color; } }

        internal void Wire(Image bodyImage, Image ledImage, Text status, Image sweepLine, RectTransform fill, Text progressText)
        {
            body = bodyImage; led = ledImage; statusLabel = status; sweep = sweepLine;
            progressFill = fill; progressLabel = progressText;
            SetState(false, 0f);
        }

        public void SetState(bool busy, float progress01)
        {
            IsBusy = busy;
            Progress01 = busy ? Mathf.Clamp01(progress01) : 0f;
            led.color = busy ? UiTheme.ScannerBusy : UiTheme.Good;
            statusLabel.text = busy ? "OCUPADO" : "LIVRE";
            statusLabel.color = busy ? UiTheme.ScannerBusy : UiTheme.Good;
            progressFill.anchorMax = new Vector2(Progress01, 1f);
            progressLabel.text = busy ? Mathf.RoundToInt(Progress01 * 100f) + "%" : "";
            sweep.gameObject.SetActive(busy);
            if (!busy) displayProgress = 0f;
        }

        public void Animate(float dt)
        {
            if (!IsBusy) return;
            displayProgress = Mathf.Lerp(displayProgress, Progress01, 1f - Mathf.Exp(-14f * dt));
            float wave = Mathf.PingPong(displayProgress * 3f, 1f);
            var rt = sweep.rectTransform;
            float y = Mathf.Lerp(0.2f, 0.82f, wave);
            rt.anchorMin = new Vector2(0.06f, y);
            rt.anchorMax = new Vector2(0.94f, y);
        }
    }
}
