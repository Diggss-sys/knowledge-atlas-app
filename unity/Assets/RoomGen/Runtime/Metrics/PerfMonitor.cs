namespace RoomGen.Metrics
{
    /// <summary>
    /// Frame-time statistics over a measurement window — the numbers that decide whether the demo is
    /// "runnable" (see docs/PERFORMANCE.md). Pure logic: fed a stream of frame durations, it reports
    /// average fps, the worst single frame (min fps), and a hitch count. No UnityEngine dependency, so
    /// EditMode tests pin the maths exactly; the MonoBehaviours just feed it Time.unscaledDeltaTime.
    /// </summary>
    public struct PerfSample
    {
        public int FrameCount;
        public float DurationSeconds;
        public float AvgFps;   // FrameCount / Duration
        public float MinFps;   // 1 / worst-frame-dt — the deepest stutter
        public int HitchCount; // frames longer than PerfAccumulator.HitchSeconds

        public bool HasData => FrameCount > 0;
    }

    /// <summary>Accumulates frame durations into a <see cref="PerfSample"/>. Reset per measurement.</summary>
    public sealed class PerfAccumulator
    {
        /// <summary>A frame longer than this reads as a visible stutter (100 ms ≈ 10 fps instantaneous).</summary>
        public const float HitchSeconds = 0.1f;

        readonly int _warmupFrames;
        int _seen, _frames, _hitches;
        float _sumDt, _maxDt;

        /// <param name="warmupFrames">Discard this many frames after each Reset — used to drop the one-time
        /// room-build hitch on the first frame of a walk so it doesn't dominate MinFps. 0 for exact tests.</param>
        public PerfAccumulator(int warmupFrames = 0) { _warmupFrames = warmupFrames; }

        public void Add(float dt)
        {
            if (dt <= 0f) return;
            if (_seen++ < _warmupFrames) return;
            _frames++;
            _sumDt += dt;
            if (dt > _maxDt) _maxDt = dt;
            if (dt > HitchSeconds) _hitches++;
        }

        public void Reset()
        {
            _seen = 0; _frames = 0; _hitches = 0; _sumDt = 0f; _maxDt = 0f;
        }

        public int FrameCount => _frames;

        public PerfSample Snapshot() => new PerfSample
        {
            FrameCount = _frames,
            DurationSeconds = _sumDt,
            AvgFps = _sumDt > 0f ? _frames / _sumDt : 0f,
            MinFps = _maxDt > 0f ? 1f / _maxDt : 0f,
            HitchCount = _hitches,
        };
    }

    /// <summary>
    /// A live readout: accumulates frames and republishes <see cref="Current"/> every window
    /// (default 0.5 s) so an on-screen counter shows a stable, twice-a-second-updating number rather
    /// than a jittery per-frame value.
    /// </summary>
    public sealed class RollingPerf
    {
        readonly PerfAccumulator _acc = new PerfAccumulator();
        readonly float _window;
        float _elapsed;

        public PerfSample Current { get; private set; }

        public RollingPerf(float windowSeconds = 0.5f) { _window = windowSeconds; }

        public void Add(float dt)
        {
            _acc.Add(dt);
            _elapsed += dt;
            if (_elapsed >= _window)
            {
                Current = _acc.Snapshot();
                _acc.Reset();
                _elapsed = 0f;
            }
        }
    }

    /// <summary>
    /// The team's "runnable" thresholds (docs/PERFORMANCE.md). Advisory today — they colour the HUD and
    /// classify a session; they do not fail a build. Measure in the BUILT app, not the editor.
    /// </summary>
    public static class PerfTargets
    {
        public const float DemoPcFps = 60f;          // lab demo PC, 1080p walk
        public const float LaptopAvgFps = 45f;       // M2 MacBook average — the binding constraint
        public const float LaptopFloorFps = 30f;     // never dip below this on a laptop
        public const float MaxConditionDeltaFraction = 0.10f; // control vs treatment avg fps must stay within 10%

        public const string Good = "good", Ok = "ok", Low = "low", NoData = "—";

        public static string Rate(PerfSample s) =>
            !s.HasData ? NoData :
            s.AvgFps >= DemoPcFps ? Good :
            s.AvgFps >= LaptopAvgFps ? Ok : Low;
    }
}
