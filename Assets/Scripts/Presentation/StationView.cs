using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>A labelled zone of the top-down plan (entry, FIFO queue, exit lanes).</summary>
    [DisallowMultipleComponent]
    public sealed class StationView : MonoBehaviour
    {
        [SerializeField] private Image body;
        [SerializeField] private Text title;
        [SerializeField] private Text value;
        [SerializeField] private Text detail;

        public string Value { get { return value.text; } }
        public string Detail { get { return detail.text; } }

        internal void Wire(Image bodyImage, Text titleLabel, Text valueLabel, Text detailLabel)
        {
            body = bodyImage; title = titleLabel; value = valueLabel; detail = detailLabel;
        }

        public void Set(string valueText, string detailText)
        {
            if (value.text != valueText) value.text = valueText;
            if (detail.text != detailText) detail.text = detailText;
        }
    }
}
