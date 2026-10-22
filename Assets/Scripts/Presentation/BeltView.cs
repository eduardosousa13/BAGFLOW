using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>Conveyor belt drawing. Stripe motion is driven by simulated time reported by the engine
    /// snapshot, so it stops while the belt is blocked or the simulation is paused, and resumes afterwards.</summary>
    [DisallowMultipleComponent]
    public sealed class BeltView : MonoBehaviour
    {
        private const float StripesPerSimulatedSecond = 0.9f;

        [SerializeField] private Image body;
        [SerializeField] private Image[] stripes;
        [SerializeField] private Image blockedOverlay;
        [SerializeField] private Text blockedLabel;
        [SerializeField] private Text title;
        [SerializeField] private Text countLabel;

        private double lastSimulationTime = -1;
        private float displayOffset;
        private float pulseClock;

        public bool IsBlocked { get; private set; }
        public bool IsMoving { get; private set; }
        public float TargetOffset { get; private set; }
        public float DisplayOffset { get { return displayOffset; } }
        public string CountText { get { return countLabel.text; } }

        internal void Wire(Image bodyImage, Image[] stripeImages, Image overlay, Text blocked, Text titleLabel, Text count)
        {
            body = bodyImage; stripes = stripeImages; blockedOverlay = overlay;
            blockedLabel = blocked; title = titleLabel; countLabel = count;
            ApplyBlocked(false);
            LayoutStripes();
        }

        public void ResetView()
        {
            lastSimulationTime = -1;
            TargetOffset = 0f;
            displayOffset = 0f;
            IsMoving = false;
            ApplyBlocked(false);
            SetCount(0);
            LayoutStripes();
        }

        /// <summary>Applies one snapshot. Offset advances only for forward simulated time while the belt is free.</summary>
        public void Apply(double simulationTime, bool blocked, bool running, int bagsOnBelt)
        {
            if (simulationTime < lastSimulationTime) lastSimulationTime = simulationTime; // reset/rewind
            bool moving = running && !blocked;
            if (moving && lastSimulationTime >= 0)
                TargetOffset += (float)(simulationTime - lastSimulationTime) * StripesPerSimulatedSecond;
            lastSimulationTime = simulationTime;
            IsMoving = moving;
            ApplyBlocked(blocked);
            SetCount(bagsOnBelt);
        }

        private void SetCount(int bagsOnBelt) { countLabel.text = "Em transporte: " + bagsOnBelt; }

        private void ApplyBlocked(bool blocked)
        {
            IsBlocked = blocked;
            blockedOverlay.gameObject.SetActive(blocked);
            blockedLabel.gameObject.SetActive(blocked);
        }

        public void Animate(float dt)
        {
            if (Mathf.Abs(TargetOffset - displayOffset) > 0.0005f)
            {
                displayOffset = Mathf.Lerp(displayOffset, TargetOffset, 1f - Mathf.Exp(-12f * dt));
                LayoutStripes();
            }
            if (IsBlocked)
            {
                pulseClock += dt;
                var c = blockedLabel.color;
                c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(pulseClock * 4f));
                blockedLabel.color = c;
            }
        }

        private void LayoutStripes()
        {
            if (stripes == null || stripes.Length == 0) return;
            int n = stripes.Length;
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Repeat((i + displayOffset) / n, 1f);
                var rt = stripes[i].rectTransform;
                rt.anchorMin = new Vector2(x, 0.12f);
                rt.anchorMax = new Vector2(x, 0.88f);
            }
        }
    }
}
