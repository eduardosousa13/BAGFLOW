using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>Visual of one bag. Pure presentation: it only mirrors values handed over by
    /// BaggageWorldView (which reads them from the engine snapshot) and eases towards its target.</summary>
    [DisallowMultipleComponent]
    public sealed class BaggageSpriteView : MonoBehaviour
    {
        private const float FollowRate = 16f;
        private const float AppearSeconds = 0.25f;

        [SerializeField] private RectTransform rect;
        [SerializeField] private Image body;
        [SerializeField] private Image strap;
        [SerializeField] private Text idLabel;
        [SerializeField] private RectTransform progressBack;
        [SerializeField] private Image progressFill;

        private float appear;
        private float pulse;
        private float progress;

        public int BagId { get; private set; }
        public BagState State { get; private set; }
        public Vector2 Target { get; private set; }
        public Vector2 Position { get { return rect.anchoredPosition; } }
        public Color BodyColor { get { return body.color; } }
        public bool ProgressVisible { get { return progressBack.gameObject.activeSelf; } }
        public float Progress { get { return progress; } }
        /// <summary>Render pass in which the world view last saw this bag (used to release stale views).</summary>
        public int Stamp { get; set; }

        internal void Wire(RectTransform rectTransform, Image bodyImage, Image strapImage, Text label,
            RectTransform barBack, Image barFill)
        {
            rect = rectTransform; body = bodyImage; strap = strapImage; idLabel = label;
            progressBack = barBack; progressFill = barFill;
        }

        public void SetSize(Vector2 size)
        {
            rect.sizeDelta = size;
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(size.y * 0.5f), 9, 22);
            idLabel.resizeTextMaxSize = fontSize;
        }

        /// <summary>Called when a view is (re)acquired for a bag.</summary>
        public void Begin(int bagId, BagState state)
        {
            BagId = bagId;
            State = state;
            appear = 0f;
            pulse = 0f;
            idLabel.text = bagId.ToString();
            ApplyState(state);
            SetProgress(0f, false);
        }

        public void ChangeState(BagState state)
        {
            if (state == State) return;
            State = state;
            ApplyState(state);
            pulse = 1f;
        }

        private void ApplyState(BagState state)
        {
            body.color = UiTheme.StateColor(state);
            strap.color = new Color(0f, 0f, 0f, 0.28f);
        }

        public void SnapTo(Vector2 position) { rect.anchoredPosition = position; }

        public void SetTarget(Vector2 target, bool snap)
        {
            Target = target;
            if (snap) rect.anchoredPosition = target;
        }

        public void SetProgress(float value01, bool visible)
        {
            progress = Mathf.Clamp01(value01);
            if (progressBack.gameObject.activeSelf != visible) progressBack.gameObject.SetActive(visible);
            if (visible) progressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
        }

        public void Animate(float dt)
        {
            Vector2 position = rect.anchoredPosition;
            if ((position - Target).sqrMagnitude > 0.01f)
                rect.anchoredPosition = Vector2.Lerp(position, Target, 1f - Mathf.Exp(-FollowRate * dt));
            else
                rect.anchoredPosition = Target;
            if (appear < 1f) appear = Mathf.Min(1f, appear + dt / AppearSeconds);
            if (pulse > 0f) pulse = Mathf.Max(0f, pulse - dt * 4f);
            float ease = 1f - (1f - appear) * (1f - appear);
            float scale = Mathf.Lerp(0.5f, 1f, ease) * (1f + 0.18f * pulse);
            rect.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
