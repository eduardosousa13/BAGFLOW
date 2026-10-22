using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>Small uGUI construction helpers shared by the factory and the components.</summary>
    public static class UiBuilder
    {
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Stretch(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static void Anchor(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static Image Box(Transform parent, string name, Color color, bool rounded = true)
        {
            var image = Node(name, parent).gameObject.AddComponent<Image>();
            if (rounded) { image.sprite = UiTheme.Rounded; image.type = Image.Type.Sliced; }
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var label = Node(name, parent).gameObject.AddComponent<Text>();
            label.font = UiTheme.Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = anchor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(8, size / 2);
            label.resizeTextMaxSize = size;
            return label;
        }

        public static Button MakeButton(Transform parent, string name, string caption, Color color, out Text captionText)
        {
            var image = Box(parent, name, color);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
            button.colors = colors;
            captionText = Label(image.transform, "Text", caption, 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(captionText.rectTransform, 6, 2, 6, 2);
            return button;
        }

        public static InputField MakeInput(Transform parent, string name, string initial)
        {
            var image = Box(parent, name, UiTheme.PanelAlt);
            image.raycastTarget = true;
            var field = image.gameObject.AddComponent<InputField>();
            var text = Label(image.transform, "Text", "", 20, UiTheme.Text, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.resizeTextForBestFit = false;
            Stretch(text.rectTransform, 10, 4, 10, 4);
            var placeholder = Label(image.transform, "Placeholder", "", 18, UiTheme.TextDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            placeholder.resizeTextForBestFit = false;
            Stretch(placeholder.rectTransform, 10, 4, 10, 4);
            field.textComponent = text;
            field.placeholder = placeholder;
            field.caretColor = UiTheme.Text;
            field.selectionColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.45f);
            field.text = initial;
            return field;
        }
    }
}
