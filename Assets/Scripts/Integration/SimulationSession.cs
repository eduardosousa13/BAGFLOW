using System;

namespace AirportBaggageSimulator
{
    /// <summary>Frame-to-tick adapter. All domain progression remains inside ISimulationEngine.</summary>
    public sealed class SimulationSession
    {
        private readonly ISimulationEngine engine;
        private SimulationConfig original;
        private double accumulatedSeconds;
        private long ticks;
        private long maxTicks;
        public bool IsRunning { get; private set; }
        public bool IsInitialized { get; private set; }
        public double PlaybackSpeed { get; private set; }
        public double PendingSeconds { get { return accumulatedSeconds; } }
        public SimulationSnapshot Snapshot { get { return engine.GetSnapshot(); } }
        public SimulationConfig Configuration { get { return SimulationConfiguration.Copy(original); } }

        public SimulationSession(ISimulationEngine engine)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public void Start(SimulationConfig config)
        {
            SimulationConfiguration.Validate(config); // Invalid input does not disturb the active run.
            Initialize(SimulationConfiguration.Copy(config), true);
        }

        private void Initialize(SimulationConfig config, bool run)
        {
            IsRunning = false;
            IsInitialized = false;
            engine.Initialize(SimulationConfiguration.Copy(config), new DeterministicRandomStreamProvider(config.Seed));
            original = config;
            PlaybackSpeed = config.PlaybackSpeed;
            accumulatedSeconds = 0;
            ticks = 0;
            maxTicks = (long)Math.Ceiling(config.MaxSimulatedSeconds / config.SimulationStepSeconds);
            IsInitialized = true;
            IsRunning = run && !engine.IsComplete;
        }

        public void Pause() { IsRunning = false; }
        public void Resume()
        {
            if (IsInitialized && !engine.IsComplete) IsRunning = true;
        }
        public void Reset()
        {
            if (original != null) Initialize(SimulationConfiguration.Copy(original), false);
        }
        public void SetPlaybackSpeed(double speed)
        {
            if (!SimulationConfiguration.IsPlaybackSpeed(speed)) throw new ArgumentException("Playback speed must be 1, 5, 10 or 20.");
            PlaybackSpeed = speed;
        }

        public void Advance(double realSeconds, int maxTicksPerFrame = 1000)
        {
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(realSeconds));
            if (maxTicksPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame));
            if (!IsRunning) return;
            try
            {
                accumulatedSeconds += realSeconds * PlaybackSpeed;
                if (double.IsInfinity(accumulatedSeconds)) throw new InvalidOperationException("Playback time accumulator overflow.");
                int frameTicks = 0;
                SimulationSnapshot previous = null;
                while (accumulatedSeconds >= original.SimulationStepSeconds && frameTicks < maxTicksPerFrame && !engine.IsComplete)
                {
                    if (ticks >= maxTicks) throw new InvalidOperationException("Maximum simulated duration/tick budget reached without completion.");
                    // One initial copy per Advance with ticks, then reuse each result as
                    // the baseline for the next tick. No snapshot allocation on idle frames.
                    if (previous == null) previous = engine.GetSnapshot();
                    double before = previous.SimulationTimeSeconds;
                    if (double.IsNaN(before) || double.IsInfinity(before))
                        throw new InvalidOperationException("Engine clock must be finite before stepping.");
                    engine.Step();
                    previous = engine.GetSnapshot();
                    double after = previous.SimulationTimeSeconds;
                    if (double.IsNaN(after) || double.IsInfinity(after) || after <= before)
                        throw new InvalidOperationException("Engine clock did not advance to a finite increasing time.");
                    accumulatedSeconds -= original.SimulationStepSeconds;
                    ticks++;
                    frameTicks++;
                    if (after >= original.MaxSimulatedSeconds && !engine.IsComplete)
                        throw new InvalidOperationException("Maximum simulated duration reached without completion.");
                }
                if (engine.IsComplete) IsRunning = false;
            }
            catch { IsRunning = false; throw; }
        }
    }
}
