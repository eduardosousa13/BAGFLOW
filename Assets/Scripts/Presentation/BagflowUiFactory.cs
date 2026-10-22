using UnityEngine;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>Builds the BAGFLOW_UI hierarchy (canvas, top-down plan, dashboard, controls). Used by the editor
    /// prefab builder and directly by tests, so the prefab and runtime construction are identical.</summary>
    public static class BagflowUiFactory
    {
        private const int StripeCount = 24;

        public static BagflowUi Build(Transform parent = null, BaggageSpriteView bagPrefab = null, string name = "BAGFLOW_UI")
        {
            var rootRect = UiBuilder.Node(name, parent);
            var root = rootRect.gameObject;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            var ui = root.AddComponent<BagflowUi>();

            var background = UiBuilder.Box(rootRect, "Background", UiTheme.Background, false);
            UiBuilder.Stretch(background.rectTransform);

            Image chipBack;
            Text chip;
            BuildHeader(rootRect, out chipBack, out chip);

            var world = BuildWorld(rootRect, bagPrefab);
            var banner = BuildBanner(rootRect);
            var dashboard = BuildDashboard(rootRect, chip, chipBack, banner.Key, banner.Value);
            var advancedRoot = BuildAdvancedPanel(rootRect, out var advancedFields, out var closeButton, out var defaultsButton);
            var controls = BuildControls(rootRect, advancedFields, advancedRoot, closeButton, defaultsButton);

            advancedRoot.transform.SetAsLastSibling(); // modal overlay above every other panel
            ui.Wire(world, dashboard, controls);
            world.ResetView();
            return ui;
        }

        // ---- Header -----------------------------------------------------------------------------------

        private static void BuildHeader(RectTransform root, out Image chipBack, out Text chip)
        {
            var header = UiBuilder.Node("Header", root);
            UiBuilder.Anchor(header, 0.015f, 0.94f, 0.985f, 0.995f);
            var title = UiBuilder.Label(header, "Title", "BAGFLOW", 38, UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiBuilder.Anchor(title.rectTransform, 0f, 0f, 0.22f, 1f);
            var subtitle = UiBuilder.Label(header, "Subtitle",
                "Simulador de bagagens aeroportuárias  ·  vista superior", 20, UiTheme.TextDim);
            UiBuilder.Anchor(subtitle.rectTransform, 0.22f, 0f, 0.74f, 1f);
            chipBack = UiBuilder.Box(header, "StatusChip", UiTheme.TextDim);
            UiBuilder.Anchor(chipBack.rectTransform, 0.78f, 0.18f, 1f, 0.82f);
            chip = UiBuilder.Label(chipBack.transform, "Text", "PRONTO", 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiBuilder.Stretch(chip.rectTransform, 8, 2, 8, 2);
        }

        // ---- World ----------------------------------------------------------------------------------------

        private static BaggageWorldView BuildWorld(RectTransform root, BaggageSpriteView bagPrefab)
        {
            var node = UiBuilder.Node("World", root);
            UiBuilder.Anchor(node, 0.015f, 0.02f, 0.69f, 0.925f);
            var view = node.gameObject.AddComponent<BaggageWorldView>(); // no Graphic on this object: drag-safe

            var backdrop = UiBuilder.Box(node, "Backdrop", UiTheme.Panel);
            UiBuilder.Stretch(backdrop.rectTransform);

            // Connectors (drawn first so the stations sit on top).
            Line(node, 0.14f, 0.50f, 0.15f, 0.50f);
            Line(node, 0.47f, 0.50f, 0.485f, 0.50f);
            Line(node, 0.705f, 0.50f, 0.735f, 0.50f);
            Line(node, 0.845f, 0.50f, 0.87f, 0.50f);
            Line(node, 0.87f, 0.255f, 0.87f, 0.745f);
            Line(node, 0.87f, 0.745f, 0.895f, 0.745f);
            Line(node, 0.87f, 0.255f, 0.895f, 0.255f);

            var entry = Station(node, "Entry", "ENTRADA", UiTheme.Accent, 0.03f, 0.38f, 0.14f, 0.62f);
            var belt = BuildBelt(node);
            var queue = Station(node, "Queue", "FILA DO SCANNER (FIFO)", UiTheme.Warn, 0.485f, 0.37f, 0.705f, 0.63f);
            var scanner = BuildScanner(node);
            var approved = Station(node, "Approved", "APROVADAS", UiTheme.Good, 0.895f, 0.56f, 0.985f, 0.93f);
            var flagged = Station(node, "Flagged", "SINALIZADAS", UiTheme.Bad, 0.895f, 0.07f, 0.985f, 0.44f);
            BuildLegend(node);

            var layer = UiBuilder.Node("BagLayer", node);
            UiBuilder.Stretch(layer);
            // Bags use bottom-left anchors so anchoredPosition equals layer pixels.
            var templateRoot = UiBuilder.Node("PoolTemplate", node);
            templateRoot.gameObject.SetActive(false);
            var prefab = bagPrefab != null ? bagPrefab : BuildBagTemplate(templateRoot);

            view.Wire(layer, prefab, belt, scanner, entry, queue, approved, flagged);
            return view;
        }

        private static void Line(RectTransform parent, float x0, float y0, float x1, float y1)
        {
            var image = UiBuilder.Box(parent, "Link", UiTheme.Stroke, false);
            var rt = image.rectTransform;
            rt.anchorMin = new Vector2(Mathf.Min(x0, x1), Mathf.Min(y0, y1));
            rt.anchorMax = new Vector2(Mathf.Max(x0, x1), Mathf.Max(y0, y1));
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.sizeDelta = Mathf.Approximately(y0, y1) ? new Vector2(0f, 6f) : new Vector2(6f, 0f);
        }

        private static StationView Station(RectTransform parent, string name, string title, Color accent,
            float x0, float y0, float x1, float y1)
        {
            var body = UiBuilder.Box(parent, name, UiTheme.PanelAlt);
            UiBuilder.Anchor(body.rectTransform, x0, y0, x1, y1);
            var bar = UiBuilder.Box(body.transform, "Accent", accent);
            bar.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.offsetMin = new Vector2(0f, -5f);
            bar.rectTransform.offsetMax = Vector2.zero;
            var titleLabel = Strip(body.transform, "Title", title, 17, accent, 10, 26, FontStyle.Bold);
            var value = Strip(body.transform, "Value", "", 24, UiTheme.Text, 36, 30, FontStyle.Bold);
            var detail = Strip(body.transform, "Detail", "", 16, UiTheme.TextDim, 66, 22, FontStyle.Normal);
            var station = body.gameObject.AddComponent<StationView>();
            station.Wire(body, titleLabel, value, detail);
            return station;
        }

        private static Text Strip(Transform parent, string name, string text, int size, Color color, float top, float height, FontStyle style)
        {
            var label = UiBuilder.Label(parent, name, text, size, color, TextAnchor.MiddleLeft, style);
            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(12f, -(top + height));
            rt.offsetMax = new Vector2(-8f, -top);
            return label;
        }

        private static BeltView BuildBelt(RectTransform parent)
        {
            var body = UiBuilder.Box(parent, "Belt", UiTheme.Belt);
            UiBuilder.Anchor(body.rectTransform, 0.15f, 0.44f, 0.47f, 0.56f);
            var stripes = new Image[StripeCount];
            for (int i = 0; i < StripeCount; i++)
            {
                stripes[i] = UiBuilder.Box(body.transform, "Stripe", new Color(1f, 1f, 1f, 0.10f), false);
                stripes[i].rectTransform.sizeDelta = new Vector2(4f, 0f);
            }
            var railTop = UiBuilder.Box(body.transform, "RailTop", UiTheme.Stroke, false);
            railTop.rectTransform.anchorMin = new Vector2(0f, 1f);
            railTop.rectTransform.anchorMax = new Vector2(1f, 1f);
            railTop.rectTransform.offsetMin = new Vector2(0f, -4f);
            railTop.rectTransform.offsetMax = Vector2.zero;
            var railBottom = UiBuilder.Box(body.transform, "RailBottom", UiTheme.Stroke, false);
            railBottom.rectTransform.anchorMin = Vector2.zero;
            railBottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            railBottom.rectTransform.offsetMin = Vector2.zero;
            railBottom.rectTransform.offsetMax = new Vector2(0f, 4f);

            var overlay = UiBuilder.Box(body.transform, "BlockedOverlay", new Color(UiTheme.Bad.r, UiTheme.Bad.g, UiTheme.Bad.b, 0.30f));
            UiBuilder.Stretch(overlay.rectTransform);
            var title = Strip(body.transform, "Title", "TAPETE TRANSPORTADOR", 17, UiTheme.Accent, 4, 22, FontStyle.Bold);
            title.rectTransform.offsetMax = new Vector2(-8f, -4f);
            var count = UiBuilder.Label(body.transform, "Count", "Em transporte: 0", 16, UiTheme.TextDim, TextAnchor.LowerRight);
            count.rectTransform.anchorMin = new Vector2(0f, 0f);
            count.rectTransform.anchorMax = new Vector2(1f, 0f);
            count.rectTransform.offsetMin = new Vector2(8f, 4f);
            count.rectTransform.offsetMax = new Vector2(-10f, 26f);
            var blocked = UiBuilder.Label(body.transform, "BlockedLabel", "BLOQUEADO", 26, UiTheme.Bad, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiBuilder.Stretch(blocked.rectTransform);

            var belt = body.gameObject.AddComponent<BeltView>();
            belt.Wire(body, stripes, overlay, blocked, title, count);
            return belt;
        }

        private static ScannerView BuildScanner(RectTransform parent)
        {
            var body = UiBuilder.Box(parent, "Scanner", UiTheme.PanelAlt);
            UiBuilder.Anchor(body.rectTransform, 0.735f, 0.33f, 0.845f, 0.67f);
            var bar = UiBuilder.Box(body.transform, "Accent", UiTheme.ScannerBusy);
            bar.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.offsetMin = new Vector2(0f, -5f);
            bar.rectTransform.offsetMax = Vector2.zero;
            Strip(body.transform, "Title", "SCANNER", 17, UiTheme.ScannerBusy, 10, 26, FontStyle.Bold);

            var led = UiBuilder.Box(body.transform, "Led", UiTheme.Good, false);
            led.sprite = UiTheme.Circle;
            led.rectTransform.anchorMin = led.rectTransform.anchorMax = new Vector2(0f, 1f);
            led.rectTransform.pivot = new Vector2(0f, 1f);
            led.rectTransform.anchoredPosition = new Vector2(12f, -42f);
            led.rectTransform.sizeDelta = new Vector2(16f, 16f);
            var status = Strip(body.transform, "Status", "LIVRE", 18, UiTheme.Good, 38, 24, FontStyle.Bold);
            status.rectTransform.offsetMin = new Vector2(34f, status.rectTransform.offsetMin.y);

            var bay = UiBuilder.Box(body.transform, "Bay", new Color(0f, 0f, 0f, 0.25f));
            UiBuilder.Anchor(bay.rectTransform, 0.08f, 0.17f, 0.92f, 0.70f);
            var sweep = UiBuilder.Box(body.transform, "Sweep", new Color(UiTheme.ScannerBusy.r, UiTheme.ScannerBusy.g, UiTheme.ScannerBusy.b, 0.9f), false);
            sweep.rectTransform.anchorMin = new Vector2(0.06f, 0.5f);
            sweep.rectTransform.anchorMax = new Vector2(0.94f, 0.5f);
            sweep.rectTransform.offsetMin = sweep.rectTransform.offsetMax = Vector2.zero;
            sweep.rectTransform.sizeDelta = new Vector2(0f, 4f);

            var back = UiBuilder.Box(body.transform, "ProgressBack", new Color(0f, 0f, 0f, 0.35f));
            back.rectTransform.anchorMin = new Vector2(0.08f, 0f);
            back.rectTransform.anchorMax = new Vector2(0.92f, 0f);
            back.rectTransform.offsetMin = new Vector2(0f, 10f);
            back.rectTransform.offsetMax = new Vector2(0f, 24f);
            var fill = UiBuilder.Box(back.transform, "Fill", UiTheme.ScannerBusy);
            UiBuilder.Stretch(fill.rectTransform);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            var percent = UiBuilder.Label(body.transform, "Percent", "", 15, UiTheme.TextDim, TextAnchor.MiddleCenter);
            percent.rectTransform.anchorMin = new Vector2(0f, 0f);
            percent.rectTransform.anchorMax = new Vector2(1f, 0f);
            percent.rectTransform.offsetMin = new Vector2(0f, 26f);
            percent.rectTransform.offsetMax = new Vector2(0f, 46f);

            var scanner = body.gameObject.AddComponent<ScannerView>();
            scanner.Wire(body, led, status, sweep, fill.rectTransform, percent);
            return scanner;
        }

        private static void BuildLegend(RectTransform parent)
        {
            var legend = UiBuilder.Node("Legend", parent);
            UiBuilder.Anchor(legend, 0.02f, 0.012f, 0.86f, 0.058f);
            BagState[] states = { BagState.WaitingEntry, BagState.Transporting, BagState.ScannerQueue,
                BagState.Scanning, BagState.Approved, BagState.Flagged };
            for (int i = 0; i < states.Length; i++)
            {
                var item = UiBuilder.Node("Item", legend);
                UiBuilder.Anchor(item, i / (float)states.Length, 0f, (i + 1) / (float)states.Length, 1f);
                var dot = UiBuilder.Box(item, "Dot", UiTheme.StateColor(states[i]), false);
                dot.sprite = UiTheme.Circle;
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                dot.rectTransform.pivot = new Vector2(0f, 0.5f);
                dot.rectTransform.anchoredPosition = new Vector2(4f, 0f);
                dot.rectTransform.sizeDelta = new Vector2(14f, 14f);
                var label = UiBuilder.Label(item, "Name", UiTheme.StateLabel(states[i]), 16, UiTheme.TextDim);
                UiBuilder.Stretch(label.rectTransform, 24, 0, 0, 0);
            }
        }

        public static BaggageSpriteView BuildBagTemplate(Transform parent)
        {
            var body = UiBuilder.Box(parent, "BaggageSpriteView", UiTheme.StateColor(BagState.WaitingEntry));
            var rect = body.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero; // bottom-left anchors: anchoredPosition == layer pixels
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(44f, 32f);
            var strap = UiBuilder.Box(body.transform, "Strap", new Color(0f, 0f, 0f, 0.28f), false);
            UiBuilder.Anchor(strap.rectTransform, 0.08f, 0.15f, 0.16f, 0.85f);
            var label = UiBuilder.Label(body.transform, "Id", "0", 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiBuilder.Stretch(label.rectTransform, 6, 4, 2, 4);
            var back = UiBuilder.Box(body.transform, "ProgressBack", new Color(0f, 0f, 0f, 0.45f), false);
            back.rectTransform.anchorMin = new Vector2(0.1f, 0f);
            back.rectTransform.anchorMax = new Vector2(0.9f, 0f);
            back.rectTransform.offsetMin = new Vector2(0f, 2f);
            back.rectTransform.offsetMax = new Vector2(0f, 6f);
            var fill = UiBuilder.Box(back.transform, "Fill", Color.white, false);
            UiBuilder.Stretch(fill.rectTransform);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            var view = body.gameObject.AddComponent<BaggageSpriteView>();
            view.Wire(rect, body, strap, label, back.rectTransform, fill);
            back.gameObject.SetActive(false);
            return view;
        }

        // ---- Dashboard ------------------------------------------------------------------------------------

        private static readonly string[] MetricNames =
        {
            "Tempo simulado", "Total de malas", "Concluídas", "Aprovadas", "Sinalizadas", "À entrada", "Em transporte",
            "Fila do scanner", "Scanner", "Bloqueios (ocorrências)", "Tempo bloqueado", "Espera média na fila",
            "Tempo total médio", "Utilização do scanner"
        };

        private static DashboardView BuildDashboard(RectTransform root, Text chip, Image chipBack, Image bannerImage, Text bannerText)
        {
            var node = UiBuilder.Node("Dashboard", root);
            UiBuilder.Anchor(node, 0.70f, 0.43f, 0.985f, 0.925f);
            var view = node.gameObject.AddComponent<DashboardView>();
            var backdrop = UiBuilder.Box(node, "Backdrop", UiTheme.Panel);
            UiBuilder.Stretch(backdrop.rectTransform);
            var title = UiBuilder.Label(node, "Title", "MÉTRICAS", 20, UiTheme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiBuilder.Anchor(title.rectTransform, 0.04f, 0.925f, 0.6f, 0.995f);

            var progressBack = UiBuilder.Box(node, "CompletionBack", UiTheme.PanelAlt);
            UiBuilder.Anchor(progressBack.rectTransform, 0.04f, 0.895f, 0.96f, 0.915f);
            var fill = UiBuilder.Box(progressBack.transform, "Fill", UiTheme.Good);
            UiBuilder.Stretch(fill.rectTransform);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);

            var values = new Text[MetricNames.Length];
            const int rows = 7;
            float top = 0.875f, bottom = 0.02f, gap = 0.012f;
            float rowHeight = (top - bottom - gap * (rows - 1)) / rows;
            for (int i = 0; i < MetricNames.Length; i++)
            {
                int row = i / 2, column = i % 2;
                float x0 = column == 0 ? 0.04f : 0.52f;
                float y1 = top - row * (rowHeight + gap);
                var card = UiBuilder.Box(node, "Card", UiTheme.PanelAlt);
                UiBuilder.Anchor(card.rectTransform, x0, y1 - rowHeight, x0 + 0.44f, y1);
                var label = UiBuilder.Label(card.transform, "Label", MetricNames[i], 15, UiTheme.TextDim);
                UiBuilder.Anchor(label.rectTransform, 0.06f, 0.55f, 0.96f, 0.95f);
                var value = UiBuilder.Label(card.transform, "Value", "—", 26, ValueColor(i), TextAnchor.MiddleLeft, FontStyle.Bold);
                UiBuilder.Anchor(value.rectTransform, 0.06f, 0.05f, 0.96f, 0.58f);
                values[i] = value;
            }
            view.Wire(values, fill, chip, chipBack, bannerImage, bannerText);
            return view;
        }

        private static Color ValueColor(int index)
        {
            if (index == 3) return UiTheme.Good;
            if (index == 4) return UiTheme.Bad;
            if (index == 9 || index == 10) return UiTheme.Warn;
            return UiTheme.Text;
        }

        private static System.Collections.Generic.KeyValuePair<Image, Text> BuildBanner(RectTransform root)
        {
            var banner = UiBuilder.Box(root, "Banner", UiTheme.Bad);
            UiBuilder.Anchor(banner.rectTransform, 0.10f, 0.855f, 0.60f, 0.915f);
            var text = UiBuilder.Label(banner.transform, "Text", "", 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiBuilder.Stretch(text.rectTransform, 14, 4, 14, 4);
            banner.gameObject.SetActive(false);
            return new System.Collections.Generic.KeyValuePair<Image, Text>(banner, text);
        }

        // ---- Controls -------------------------------------------------------------------------------------

        private static GameObject BuildAdvancedPanel(RectTransform root, out SimulationControlPanel.FieldSet fields,
            out Button closeButton, out Button defaultsButton)
        {
            var overlay = UiBuilder.Box(root, "AdvancedPanel", new Color(0f, 0f, 0f, 0.62f), false);
            overlay.raycastTarget = true;
            UiBuilder.Stretch(overlay.rectTransform);
            var card = UiBuilder.Box(overlay.transform, "Card", UiTheme.Panel);
            UiBuilder.Anchor(card.rectTransform, 0.22f, 0.10f, 0.78f, 0.90f);
            var title = UiBuilder.Label(card.transform, "Title", "PARÂMETROS AVANÇADOS", 26, UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiBuilder.Anchor(title.rectTransform, 0.04f, 0.92f, 0.96f, 0.99f);
            var note = UiBuilder.Label(card.transform, "Note", "Aplicados ao iniciar. Valores em segundos, kg ou probabilidade [0,1]; bloqueios: 0 = sem bloqueios.", 16, UiTheme.TextDim);
            UiBuilder.Anchor(note.rectTransform, 0.04f, 0.865f, 0.96f, 0.925f);

            fields = new SimulationControlPanel.FieldSet();
            string[] labels =
            {
                "Intervalo médio de chegada (s)", "Transporte mínimo (s)", "Transporte mais provável (s)", "Transporte máximo (s)",
                "Inspeção média (s)", "Peso médio base (kg)", "Desvio-padrão do peso (kg)", "Peso mínimo (kg)",
                "Peso máximo (kg)", "Limiar de peso elevado (kg)", "Probabilidade de sinalização",
                "Intervalo médio entre bloqueios (s)", "Duração do bloqueio (s)", "Passo de simulação (s)",
                "Tempo máximo simulado (s)"
            };
            var inputs = new InputField[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int row = i / 2, column = i % 2;
                float x0 = column == 0 ? 0.04f : 0.52f;
                float top = 0.85f - row * 0.1f;
                var label = UiBuilder.Label(card.transform, "Label", labels[i], 15, UiTheme.TextDim);
                UiBuilder.Anchor(label.rectTransform, x0, top - 0.04f, x0 + 0.44f, top);
                inputs[i] = UiBuilder.MakeInput(card.transform, "Field", "");
                UiBuilder.Anchor(inputs[i].GetComponent<Image>().rectTransform, x0, top - 0.092f, x0 + 0.44f, top - 0.042f);
            }
            fields.MeanArrival = inputs[0]; fields.TransportMin = inputs[1]; fields.TransportMode = inputs[2];
            fields.TransportMax = inputs[3]; fields.MeanInspection = inputs[4]; fields.WeightMean = inputs[5];
            fields.WeightStd = inputs[6]; fields.MinWeight = inputs[7]; fields.MaxWeight = inputs[8];
            fields.HeavyThreshold = inputs[9]; fields.FlagProbability = inputs[10]; fields.BlockageInterval = inputs[11];
            fields.BlockageDuration = inputs[12]; fields.StepSeconds = inputs[13]; fields.MaxSimulatedSeconds = inputs[14];

            Text caption;
            defaultsButton = UiBuilder.MakeButton(card.transform, "Defaults", "Valores por defeito", UiTheme.PanelAlt, out caption);
            UiBuilder.Anchor(defaultsButton.GetComponent<Image>().rectTransform, 0.04f, 0.03f, 0.48f, 0.095f);
            closeButton = UiBuilder.MakeButton(card.transform, "Close", "Fechar", UiTheme.Accent, out caption);
            UiBuilder.Anchor(closeButton.GetComponent<Image>().rectTransform, 0.52f, 0.03f, 0.96f, 0.095f);
            overlay.gameObject.SetActive(false);
            return overlay.gameObject;
        }

        private static SimulationControlPanel BuildControls(RectTransform root, SimulationControlPanel.FieldSet fields,
            GameObject advancedRoot, Button closeButton, Button defaultsButton)
        {
            var node = UiBuilder.Node("Controls", root);
            UiBuilder.Anchor(node, 0.70f, 0.02f, 0.985f, 0.42f);
            var view = node.gameObject.AddComponent<SimulationControlPanel>();
            var backdrop = UiBuilder.Box(node, "Backdrop", UiTheme.Panel);
            UiBuilder.Stretch(backdrop.rectTransform);
            var title = UiBuilder.Label(node, "Title", "CONTROLOS", 20, UiTheme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiBuilder.Anchor(title.rectTransform, 0.04f, 0.92f, 0.6f, 0.995f);

            fields.BaggageCount = Cell(node, "Malas", 0.04f, 0.29f);
            fields.Seed = Cell(node, "Seed", 0.32f, 0.57f);
            Text caption;
            var arrival = UiBuilder.MakeButton(node, "Arrival", "Chegadas", UiTheme.PanelAlt, out var arrivalText);
            UiBuilder.Anchor(arrival.image.rectTransform, 0.61f, 0.69f, 0.96f, 0.82f);
            var arrivalLabel = UiBuilder.Label(node, "ArrivalLabel", "Modo de chegadas", 14, UiTheme.TextDim);
            UiBuilder.Anchor(arrivalLabel.rectTransform, 0.61f, 0.83f, 0.96f, 0.90f);

            var speedLabel = UiBuilder.Label(node, "SpeedLabel", "Velocidade", 16, UiTheme.TextDim);
            UiBuilder.Anchor(speedLabel.rectTransform, 0.04f, 0.53f, 0.26f, 0.65f);
            var speedButtons = new Button[SimulationControlPanel.Speeds.Length];
            for (int i = 0; i < speedButtons.Length; i++)
            {
                speedButtons[i] = UiBuilder.MakeButton(node, "Speed" + SimulationControlPanel.Speeds[i], SimulationControlPanel.Speeds[i] + "×", UiTheme.PanelAlt, out caption);
                float x0 = 0.28f + i * 0.17f;
                UiBuilder.Anchor(speedButtons[i].image.rectTransform, x0, 0.53f, x0 + 0.155f, 0.65f);
            }

            var start = UiBuilder.MakeButton(node, "Start", "Iniciar", UiTheme.Good, out caption);
            UiBuilder.Anchor(start.image.rectTransform, 0.04f, 0.34f, 0.35f, 0.49f);
            var pause = UiBuilder.MakeButton(node, "PauseResume", "Retomar", UiTheme.Warn, out var pauseText);
            UiBuilder.Anchor(pause.image.rectTransform, 0.37f, 0.34f, 0.67f, 0.49f);
            var reset = UiBuilder.MakeButton(node, "Reset", "Reiniciar", UiTheme.Bad, out caption);
            UiBuilder.Anchor(reset.image.rectTransform, 0.69f, 0.34f, 0.96f, 0.49f);

            var export = UiBuilder.MakeButton(node, "Export", "Exportar resultados", UiTheme.Accent, out caption);
            UiBuilder.Anchor(export.image.rectTransform, 0.04f, 0.17f, 0.50f, 0.30f);
            var advanced = UiBuilder.MakeButton(node, "Advanced", "Parâmetros avançados", UiTheme.PanelAlt, out caption);
            UiBuilder.Anchor(advanced.image.rectTransform, 0.52f, 0.17f, 0.96f, 0.30f);

            var error = UiBuilder.Label(node, "Error", "", 16, UiTheme.Bad, TextAnchor.UpperLeft);
            UiBuilder.Anchor(error.rectTransform, 0.04f, 0.075f, 0.96f, 0.16f);
            error.gameObject.SetActive(false);
            var exportStatus = UiBuilder.Label(node, "ExportStatus", "", 16, UiTheme.Good, TextAnchor.UpperLeft);
            UiBuilder.Anchor(exportStatus.rectTransform, 0.04f, 0.0f, 0.96f, 0.075f);
            exportStatus.gameObject.SetActive(false);

            view.Wire(fields, start, pause, pauseText, reset, export, arrival, arrivalText, advanced, closeButton,
                defaultsButton, advancedRoot, speedButtons, error, exportStatus);
            return view;
        }

        private static InputField Cell(RectTransform parent, string label, float x0, float x1)
        {
            var text = UiBuilder.Label(parent, label + "Label", label, 14, UiTheme.TextDim);
            UiBuilder.Anchor(text.rectTransform, x0, 0.83f, x1, 0.90f);
            var input = UiBuilder.MakeInput(parent, label + "Input", "");
            UiBuilder.Anchor(input.GetComponent<Image>().rectTransform, x0, 0.69f, x1, 0.82f);
            return input;
        }
    }
}
