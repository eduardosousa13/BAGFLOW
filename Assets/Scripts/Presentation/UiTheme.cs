using UnityEngine;

namespace AirportBaggageSimulator
{
    /// <summary>Single source of colours, labels, font and sprites for the M4 presentation layer.
    /// Bag colours/labels are keyed by the shared BagState; no second state system exists.</summary>
    public static class UiTheme
    {
        public static readonly Color Background = Hex("0E1621");
        public static readonly Color Panel = Hex("152130");
        public static readonly Color PanelAlt = Hex("1C2B3D");
        public static readonly Color Stroke = Hex("2B3C52");
        public static readonly Color Text = Hex("E8EEF5");
        public static readonly Color TextDim = Hex("8FA1B5");
        public static readonly Color Accent = Hex("3D8BFD");
        public static readonly Color Good = Hex("2FBF71");
        public static readonly Color Warn = Hex("F2B33D");
        public static readonly Color Bad = Hex("F0524F");
        public static readonly Color Belt = Hex("27384C");
        public static readonly Color ScannerBusy = Hex("B26CFF");

        public static Color StateColor(BagState state)
        {
            switch (state)
            {
                case BagState.WaitingEntry: return Hex("8FA1B5");
                case BagState.Transporting: return Hex("3D8BFD");
                case BagState.ScannerQueue: return Hex("F2B33D");
                case BagState.Scanning: return Hex("B26CFF");
                case BagState.Approved: return Hex("2FBF71");
                case BagState.Flagged: return Hex("F0524F");
                default: return Hex("5B6B7D");
            }
        }

        public static string StateLabel(BagState state)
        {
            switch (state)
            {
                case BagState.Scheduled: return "Agendada";
                case BagState.WaitingEntry: return "À entrada";
                case BagState.Transporting: return "Em transporte";
                case BagState.ScannerQueue: return "Na fila";
                case BagState.Scanning: return "A inspecionar";
                case BagState.Approved: return "Aprovada";
                case BagState.Flagged: return "Sinalizada";
                default: return state.ToString();
            }
        }

        private static Font font;
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        private static Sprite rounded;
        private static Sprite circle;

        /// <summary>9-sliced rounded rectangle. The editor builder replaces it with a saved asset.</summary>
        public static Sprite Rounded
        {
            get { if (rounded == null) rounded = Create(32, 12, true); return rounded; }
            set { rounded = value; }
        }

        public static Sprite Circle
        {
            get { if (circle == null) circle = Create(32, 16, false); return circle; }
            set { circle = value; }
        }

        public static Texture2D CreateTexture(int size, float radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float dy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude
                        + Mathf.Min(Mathf.Max(dx, dy), 0f);
                    float alpha = Mathf.Clamp01(radius - outside);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            texture.Apply();
            return texture;
        }

        private static Sprite Create(int size, float radius, bool sliced)
        {
            var texture = CreateTexture(size, radius);
            var border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }

        public static Color Hex(string hex)
        {
            Color color;
            ColorUtility.TryParseHtmlString("#" + hex, out color);
            return color;
        }
    }
}
