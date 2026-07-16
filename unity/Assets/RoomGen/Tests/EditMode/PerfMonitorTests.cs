using System.IO;
using NUnit.Framework;
using RoomGen.Metrics;
using RoomGen.Runner;
using UnityEngine;

namespace RoomGen.Tests
{
    /// <summary>
    /// The "runnable" maths: frame-time → avg/min fps + hitch count, the rolling live window, the
    /// target classification, and the per-trial perf-log CSV. Pure logic, so the numbers are pinned
    /// exactly here rather than eyeballed off a HUD.
    /// </summary>
    public class PerfMonitorTests
    {
        const float F60 = 1f / 60f;

        [Test]
        public void Steady_60fps_reads_60_avg_and_min_with_no_hitches()
        {
            var acc = new PerfAccumulator();
            for (var i = 0; i < 120; i++) acc.Add(F60);
            var s = acc.Snapshot();

            Assert.AreEqual(120, s.FrameCount);
            Assert.AreEqual(60f, s.AvgFps, 0.5f);
            Assert.AreEqual(60f, s.MinFps, 0.5f);
            Assert.AreEqual(0, s.HitchCount);
        }

        [Test]
        public void One_long_frame_drops_min_fps_and_counts_a_hitch_without_wrecking_the_average()
        {
            var acc = new PerfAccumulator();
            for (var i = 0; i < 100; i++) acc.Add(F60);
            acc.Add(0.2f); // a 200 ms stall — 5 fps instantaneous

            var s = acc.Snapshot();
            Assert.AreEqual(5f, s.MinFps, 0.1f, "worst frame is the 200 ms one");
            Assert.AreEqual(1, s.HitchCount, "200 ms > 100 ms hitch threshold");
            Assert.Greater(s.AvgFps, 40f, "one stall shouldn't tank the average of 100 good frames");
        }

        [Test]
        public void Warmup_frames_are_discarded()
        {
            var acc = new PerfAccumulator(warmupFrames: 3);
            acc.Add(0.5f); acc.Add(0.5f); acc.Add(0.5f); // the room-build stalls — must be ignored
            for (var i = 0; i < 60; i++) acc.Add(F60);

            var s = acc.Snapshot();
            Assert.AreEqual(60, s.FrameCount, "the 3 warmup frames are not counted");
            Assert.AreEqual(0, s.HitchCount, "the discarded stalls don't count as hitches");
            Assert.AreEqual(60f, s.MinFps, 0.5f);
        }

        [Test]
        public void Rolling_window_publishes_only_after_the_window_elapses()
        {
            var rolling = new RollingPerf(windowSeconds: 0.5f);
            Assert.IsFalse(rolling.Current.HasData, "nothing published before a full window");

            for (var i = 0; i < 30; i++) rolling.Add(F60); // 0.5 s of 60 fps
            Assert.IsTrue(rolling.Current.HasData, "a window elapsed → a sample is published");
            Assert.AreEqual(60f, rolling.Current.AvgFps, 1f);
        }

        [Test]
        public void Targets_classify_against_the_team_thresholds()
        {
            Assert.AreEqual(PerfTargets.Good, PerfTargets.Rate(new PerfSample { FrameCount = 1, AvgFps = 61f }));
            Assert.AreEqual(PerfTargets.Ok,   PerfTargets.Rate(new PerfSample { FrameCount = 1, AvgFps = 50f }));
            Assert.AreEqual(PerfTargets.Low,  PerfTargets.Rate(new PerfSample { FrameCount = 1, AvgFps = 28f }));
            Assert.AreEqual(PerfTargets.NoData, PerfTargets.Rate(default));
        }

        [Test]
        public void Perf_log_writes_a_joinable_row_per_trial_with_invariant_numbers()
        {
            var path = Path.Combine(Application.temporaryCachePath, "perf-test.perf.csv");
            if (File.Exists(path)) File.Delete(path);

            var log = new PerfLog(path, new MachineInfo("Test GPU, Rev 2", "Windows 11", "TestPC1,1"));
            log.Write("sess-1", "P07", "ceiling_pilot_01", 0, "control",
                new PerfSample { FrameCount = 600, DurationSeconds = 10f, AvgFps = 59.7f, MinFps = 42.3f, HitchCount = 2 });
            log.Write("sess-1", "P07", "ceiling_pilot_01", 1, "treatment",
                new PerfSample { FrameCount = 590, DurationSeconds = 10f, AvgFps = 48.1f, MinFps = 31.0f, HitchCount = 5 });

            var lines = File.ReadAllLines(path);
            Assert.AreEqual(3, lines.Length, "header + 2 rows");
            StringAssert.StartsWith("session_id,participant_id,study_id,trial_index,condition,avg_fps", lines[0]);
            StringAssert.EndsWith("gpu,os,device", lines[0], "machine columns for the cross-machine sweep");
            StringAssert.Contains("0,control,59.7,42.3,2,600", lines[1], "invariant decimals (point, not comma) + joined key");
            StringAssert.Contains("1,treatment,48.1,31.0,5,590", lines[2]);
            StringAssert.Contains("\"Test GPU, Rev 2\",Windows 11,\"TestPC1,1\"", lines[1], "comma-bearing machine fields are quoted");
        }
    }
}
