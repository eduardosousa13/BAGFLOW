using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AirportBaggageSimulator
{
    /// <summary>ISimulationControlView: collects configuration and emits the six contract events.
    /// It validates only that typed text is a number; domain validation belongs to the controller.</summary>
    [DisallowMultipleComponent]
    public sealed class SimulationControlPanel : MonoBehaviour, ISimulationControlView
    {
        public static readonly double[] Speeds = { 1, 5, 10, 20 };
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        [Serializable]
        public sealed class FieldSet
        {
            public InputField BaggageCount, Seed, MeanArrival, TransportMin, TransportMode, TransportMax,
                MeanInspection, WeightMean, WeightStd, MinWeight, MaxWeight, HeavyThreshold, FlagProbability,
                BlockageInterval, BlockageDuration, StepSeconds, MaxSimulatedSeconds;
        }

        [SerializeField] private FieldSet fields = new FieldSet();
        [SerializeField] private Button startButton;
        [SerializeField] private Button pauseResumeButton;
        [SerializeField] private Text pauseResumeText;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button exportButton;
        [SerializeField] private Button arrivalButton;
        [SerializeField] private Text arrivalText;
        [SerializeField] private Button advancedButton;
        [SerializeField] private Button advancedCloseButton;
        [SerializeField] private Button advancedDefaultsButton;
        [SerializeField] private GameObject advancedPanel;
        [SerializeField] private Button[] speedButtons;
        [SerializeField] private Text errorLabel;
        [SerializeField] private Text exportLabel;

        private UnityAction onStart, onPauseResume, onReset, onExport, onArrival, onAdvanced, onDefaults;
        private UnityAction[] onSpeed;
        private bool listening;
        private bool running;
        private bool started;
        private bool complete;
        private ArrivalMode arrivalMode = ArrivalMode.Constant;
        private double speed = 10;
        private double initialRunSpeed = 10;
        private double? pendingStartSpeed;

        public event Action<SimulationConfig> StartRequested;
        public event Action PauseRequested;
        public event Action ResumeRequested;
        public event Action ResetRequested;
        public event Action<double> PlaybackSpeedRequested;
        public event Action ExportRequested;

        public bool IsRunning { get { return running; } }
        public bool HasRun { get { return started; } }
        public bool IsComplete { get { return complete; } }
        public double SelectedSpeed { get { return speed; } }
        public ArrivalMode SelectedArrivalMode { get { return arrivalMode; } }
        public string ErrorText { get { return errorLabel.text; } }
        public string ExportText { get { return exportLabel.text; } }
        public bool AdvancedOpen { get { return advancedPanel.activeSelf; } }
        public int SnapshotNotifications { get; private set; }
        public FieldSet Fields { get { return fields; } }
        public bool StartInteractable { get { return startButton.interactable; } }
        public bool PauseResumeInteractable { get { return pauseResumeButton.interactable; } }
        public bool ExportInteractable { get { return exportButton.interactable; } }
        public bool ResetInteractable { get { return resetButton.interactable; } }
        public string PauseResumeCaption { get { return pauseResumeText.text; } }

        internal void Wire(FieldSet fieldSet, Button start, Button pauseResume, Text pauseResumeCaption, Button reset,
            Button export, Button arrival, Text arrivalCaption, Button advanced, Button advancedClose,
            Button advancedDefaults, GameObject advancedRoot, Button[] speedBtns, Text error, Text exportStatus)
        {
            fields = fieldSet; startButton = start; pauseResumeButton = pauseResume; pauseResumeText = pauseResumeCaption;
            resetButton = reset; exportButton = export; arrivalButton = arrival; arrivalText = arrivalCaption;
            advancedButton = advanced; advancedCloseButton = advancedClose; advancedDefaultsButton = advancedDefaults;
            advancedPanel = advancedRoot; speedButtons = speedBtns; errorLabel = error; exportLabel = exportStatus;
            ApplyDefaults();
            if (isActiveAndEnabled) Listen();
            Refresh();
        }

        private void OnEnable() { Listen(); Refresh(); }
        private void OnDisable() { Unlisten(); }

        private void Listen()
        {
            if (listening || startButton == null) return;
            onStart = ClickStart; onPauseResume = ClickPauseResume; onReset = ClickReset; onExport = ClickExport;
            onArrival = ToggleArrivalMode; onAdvanced = ToggleAdvanced; onDefaults = ApplyDefaults;
            startButton.onClick.AddListener(onStart);
            pauseResumeButton.onClick.AddListener(onPauseResume);
            resetButton.onClick.AddListener(onReset);
            exportButton.onClick.AddListener(onExport);
            arrivalButton.onClick.AddListener(onArrival);
            advancedButton.onClick.AddListener(onAdvanced);
            advancedCloseButton.onClick.AddListener(onAdvanced);
            advancedDefaultsButton.onClick.AddListener(onDefaults);
            onSpeed = new UnityAction[speedButtons.Length];
            for (int i = 0; i < speedButtons.Length; i++)
            {
                double value = Speeds[i];
                onSpeed[i] = () => SelectSpeed(value);
                speedButtons[i].onClick.AddListener(onSpeed[i]);
            }
            listening = true;
        }

        private void Unlisten()
        {
            if (!listening) return;
            startButton.onClick.RemoveListener(onStart);
            pauseResumeButton.onClick.RemoveListener(onPauseResume);
            resetButton.onClick.RemoveListener(onReset);
            exportButton.onClick.RemoveListener(onExport);
            arrivalButton.onClick.RemoveListener(onArrival);
            advancedButton.onClick.RemoveListener(onAdvanced);
            advancedCloseButton.onClick.RemoveListener(onAdvanced);
            advancedDefaultsButton.onClick.RemoveListener(onDefaults);
            for (int i = 0; i < speedButtons.Length; i++) speedButtons[i].onClick.RemoveListener(onSpeed[i]);
            listening = false;
        }

        // ---- ISimulationControlView -------------------------------------------------------------------

        public void SetRunning(bool isRunning)
        {
            running = isRunning;
            // SetRunning(true) during StartRequested acknowledges an accepted start.
            // Rejected form/domain values must not overwrite the run restored by Reset.
            if (isRunning && pendingStartSpeed.HasValue) initialRunSpeed = pendingStartSpeed.Value;
            if (isRunning) started = true;
            Refresh();
        }

        public void ShowValidationError(string message)
        {
            errorLabel.text = message ?? string.Empty;
            errorLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        // ---- Extra presentation hooks (not part of the shared interface) --------------------------------

        /// <summary>Follows the run through rendered snapshots so Resume/Export are enabled only when meaningful.</summary>
        public void NotifySnapshot(SimulationSnapshot snapshot)
        {
            SnapshotNotifications++;
            bool nowComplete = snapshot != null && snapshot.IsComplete;
            if (nowComplete != complete) { complete = nowComplete; Refresh(); }
        }

        public void ShowExportResult(bool success, string message)
        {
            exportLabel.text = message ?? string.Empty;
            exportLabel.color = success ? UiTheme.Good : UiTheme.Bad;
            exportLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        // ---- Actions (wired to buttons, callable from tests) --------------------------------------------

        public void ClickStart()
        {
            ShowValidationError(null);
            ShowExportResult(true, null);
            SimulationConfig config;
            string error;
            if (!TryReadConfig(out config, out error)) { ShowValidationError(error); return; }
            var handler = StartRequested;
            if (handler == null) return;
            pendingStartSpeed = config.PlaybackSpeed;
            try { handler(config); }
            finally { pendingStartSpeed = null; }
        }

        public void ClickPauseResume()
        {
            ShowValidationError(null);
            if (running) { var p = PauseRequested; if (p != null) p(); }
            else if (started && !complete) { var r = ResumeRequested; if (r != null) r(); }
        }

        public void ClickReset()
        {
            ShowValidationError(null);
            ShowExportResult(true, null);
            var handler = ResetRequested;
            if (handler != null)
            {
                handler();
                // The controller restores the original config, including playback speed.
                // Mirror it without emitting a speed change that would override the reset.
                if (started) { speed = initialRunSpeed; Refresh(); }
            }
        }

        public void ClickExport()
        {
            ShowValidationError(null);
            var handler = ExportRequested;
            if (handler != null) handler();
        }

        public void SelectSpeed(double value)
        {
            if (Array.IndexOf(Speeds, value) < 0) { ShowValidationError("Velocidade inválida: use 1×, 5×, 10× ou 20×."); return; }
            speed = value;
            Refresh();
            if (started) { var handler = PlaybackSpeedRequested; if (handler != null) handler(value); }
        }

        public void ToggleArrivalMode()
        {
            arrivalMode = arrivalMode == ArrivalMode.Constant ? ArrivalMode.Exponential : ArrivalMode.Constant;
            Refresh();
        }

        public void ToggleAdvanced() { advancedPanel.SetActive(!advancedPanel.activeSelf); }

        public void ApplyDefaults()
        {
            var d = new SimulationConfig();
            fields.BaggageCount.text = d.BaggageCount.ToString(Culture);
            fields.Seed.text = d.Seed.ToString(Culture);
            arrivalMode = d.ArrivalMode;
            fields.MeanArrival.text = Format(d.MeanArrivalIntervalSeconds);
            fields.TransportMin.text = Format(d.TransportMinSeconds);
            fields.TransportMode.text = Format(d.TransportModeSeconds);
            fields.TransportMax.text = Format(d.TransportMaxSeconds);
            fields.MeanInspection.text = Format(d.MeanInspectionSeconds);
            fields.WeightMean.text = Format(d.WeightBaseMeanKg);
            fields.WeightStd.text = Format(d.WeightBaseStdKg);
            fields.MinWeight.text = Format(d.MinWeightKg);
            fields.MaxWeight.text = Format(d.MaxWeightKg);
            fields.HeavyThreshold.text = Format(d.HeavyWeightThresholdKg);
            fields.FlagProbability.text = Format(d.FlagProbability);
            fields.BlockageInterval.text = d.BlockageRatePerSecond > 0 ? Format(1.0 / d.BlockageRatePerSecond) : "0";
            fields.BlockageDuration.text = Format(d.BlockageDurationSeconds);
            fields.StepSeconds.text = Format(d.SimulationStepSeconds);
            fields.MaxSimulatedSeconds.text = Format(d.MaxSimulatedSeconds);
            if (arrivalText != null) Refresh();
        }

        // ---- Configuration parsing ------------------------------------------------------------------------

        public bool TryReadConfig(out SimulationConfig config, out string error)
        {
            config = new SimulationConfig();
            error = null;
            int count, seed;
            double v;
            if (!TryInt(fields.BaggageCount, "Quantidade de malas", out count, ref error) ||
                !TryInt(fields.Seed, "Seed", out seed, ref error)) return false;
            config.BaggageCount = count;
            config.Seed = seed;
            config.ArrivalMode = arrivalMode;
            if (!TryDouble(fields.MeanArrival, "Intervalo médio de chegada", out v, ref error)) return false;
            config.MeanArrivalIntervalSeconds = v;
            if (!TryDouble(fields.TransportMin, "Transporte mínimo", out v, ref error)) return false;
            config.TransportMinSeconds = v;
            if (!TryDouble(fields.TransportMode, "Transporte mais provável", out v, ref error)) return false;
            config.TransportModeSeconds = v;
            if (!TryDouble(fields.TransportMax, "Transporte máximo", out v, ref error)) return false;
            config.TransportMaxSeconds = v;
            if (!TryDouble(fields.MeanInspection, "Inspeção média", out v, ref error)) return false;
            config.MeanInspectionSeconds = v;
            if (!TryDouble(fields.WeightMean, "Peso médio base", out v, ref error)) return false;
            config.WeightBaseMeanKg = v;
            if (!TryDouble(fields.WeightStd, "Desvio-padrão do peso", out v, ref error)) return false;
            config.WeightBaseStdKg = v;
            if (!TryDouble(fields.MinWeight, "Peso mínimo", out v, ref error)) return false;
            config.MinWeightKg = v;
            if (!TryDouble(fields.MaxWeight, "Peso máximo", out v, ref error)) return false;
            config.MaxWeightKg = v;
            if (!TryDouble(fields.HeavyThreshold, "Limiar de peso elevado", out v, ref error)) return false;
            config.HeavyWeightThresholdKg = v;
            if (!TryDouble(fields.FlagProbability, "Probabilidade de sinalização", out v, ref error)) return false;
            config.FlagProbability = v;
            if (!TryDouble(fields.BlockageInterval, "Intervalo médio entre bloqueios", out v, ref error)) return false;
            if (v < 0) { error = "Intervalo médio entre bloqueios não pode ser negativo (0 = sem bloqueios)."; return false; }
            config.BlockageRatePerSecond = v == 0 ? 0 : 1.0 / v;
            if (!TryDouble(fields.BlockageDuration, "Duração do bloqueio", out v, ref error)) return false;
            config.BlockageDurationSeconds = v;
            if (!TryDouble(fields.StepSeconds, "Passo de simulação", out v, ref error)) return false;
            config.SimulationStepSeconds = v;
            if (!TryDouble(fields.MaxSimulatedSeconds, "Tempo máximo simulado", out v, ref error)) return false;
            config.MaxSimulatedSeconds = v;
            config.PlaybackSpeed = speed;
            return true;
        }

        private static bool TryInt(InputField field, string label, out int value, ref string error)
        {
            if (int.TryParse((field.text ?? "").Trim(), NumberStyles.Integer, Culture, out value)) return true;
            error = label + ": indique um número inteiro (recebido \"" + field.text + "\").";
            return false;
        }

        private static bool TryDouble(InputField field, string label, out double value, ref string error)
        {
            string text = (field.text ?? "").Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, Culture, out value) && !double.IsNaN(value) && !double.IsInfinity(value))
                return true;
            error = label + ": indique um número válido (recebido \"" + field.text + "\").";
            return false;
        }

        private static string Format(double value) { return Math.Round(value, 6).ToString("0.######", Culture); }

        // ---- State -> widgets -----------------------------------------------------------------------------

        private void Refresh()
        {
            if (startButton == null) return;
            startButton.interactable = !running;
            pauseResumeText.text = running ? "Pausar" : "Retomar";
            pauseResumeButton.interactable = running || (started && !complete);
            resetButton.interactable = started;
            exportButton.interactable = complete;
            arrivalText.text = "Chegadas: " + (arrivalMode == ArrivalMode.Constant ? "constantes" : "exponenciais");
            for (int i = 0; i < speedButtons.Length; i++)
                speedButtons[i].image.color = Speeds[i] == speed ? UiTheme.Accent : UiTheme.PanelAlt;
        }
    }
}
