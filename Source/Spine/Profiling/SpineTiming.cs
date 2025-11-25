using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

// Alias UnityEngine.Time so it doesn't clash with SpineTiming.Time(...)
using UTime = UnityEngine.Time;

namespace Spine.Profiling
{
    /// <summary>
    /// Lightweight in-mod profiler.
    ///
    /// Usage:
    ///   1) Enable profiling:
    ///        SpineTiming.Enabled = true;
    ///
    ///   2) Wrap code you want to time:
    ///        SpineTiming.Time("DoCell_SkillOverlay", () => {
    ///            // your code here
    ///        });
    ///
    ///   3) Drive it from a GameComponent:
    ///        - Call SpineTiming.OnFrameStart() in GameComponentUpdate().
    ///        - Call SpineTiming.HandleInput() in GameComponentOnGUI().
    ///
    ///   4) Wire Work tab open/close:
    ///        - Call SpineTiming.NotifyWorkTabOpen(true) in PostOpen().
    ///        - Call SpineTiming.NotifyWorkTabOpen(false) in PreClose().
    ///
    ///   5) In-game:
    ///        - Press 1 to print a report.
    ///        - Press Shift+1 to clear data.
    /// </summary>
    public static class SpineTiming
    {
        /// <summary>
        /// Internal timing record for a named section.
        /// Class to avoid struct copies in hot paths.
        /// </summary>
        private class TimingData
        {
            public long TotalTicks;      // Sum of all elapsed ticks
            public int TotalCalls;       // Total call count
            public int CallsThisFrame;   // Calls during the current frame
            public long MaxTicks;        // Longest single call
        }

        private static readonly Dictionary<string, TimingData> _data =
            new Dictionary<string, TimingData>();

        // Stopwatch frequency (ticks per second)
        private static readonly double _tickFrequency = Stopwatch.Frequency;

        // Precomputed ticks per millisecond
        private static readonly double _ticksPerMs = _tickFrequency / 1000.0;

        // Profiling state
        private static bool _enabled;
        private static int _startFrame;            // Frame index when profiling started
        private static double _startRealtime;      // realtimeSinceStartup when profiling started

        // Work tab visibility tracking
        private static bool _workTabOpen;
        private static double _workTabOpenSeconds; // Sum of time Work tab has been open

        /// <summary>
        /// Global toggle for profiling.
        /// When false, Time() just runs the action with minimal overhead.
        /// When set true, resets counters and starts tracking from now.
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (value && !_enabled)
                {
                    // Turning profiling on: reset baseline
                    _startFrame = UTime.frameCount;
                    _startRealtime = UTime.realtimeSinceStartup;
                    _workTabOpenSeconds = 0;
                    _data.Clear();
                }

                _enabled = value;
            }
        }

        /// <summary>
        /// Wrap a block of code and measure its execution time.
        ///
        /// Example:
        ///   SpineTiming.Time("DoCell_DrawSkill", () => {
        ///       DrawSkillOverlay(rect, pawn, workType);
        ///   });
        /// </summary>
        public static void Time(string name, Action action)
        {
            if (!Enabled)
            {
                // Profiling disabled: just run the code
                action();
                return;
            }

            long start = Stopwatch.GetTimestamp();

            try
            {
                action();
            }
            finally
            {
                long end = Stopwatch.GetTimestamp();
                long elapsed = end - start;

                if (!_data.TryGetValue(name, out var entry))
                {
                    entry = new TimingData();
                    _data[name] = entry;
                }

                entry.TotalTicks += elapsed;
                entry.TotalCalls++;
                entry.CallsThisFrame++;

                if (elapsed > entry.MaxTicks)
                {
                    entry.MaxTicks = elapsed;
                }
            }
        }

        /// <summary>
        /// Called once per frame.
        /// Resets per-frame call counters and accumulates "work tab open" time.
        ///
        /// Hook this from GameComponentUpdate().
        /// </summary>
        public static void OnFrameStart()
        {
            if (!Enabled) return;

            foreach (var entry in _data.Values)
            {
                entry.CallsThisFrame = 0;
            }

            // Track how long the Work tab has been open
            if (_workTabOpen)
            {
                _workTabOpenSeconds += UTime.deltaTime;
            }
        }

        /// <summary>
        /// Input handler.
        /// Call from GameComponentOnGUI().
        ///
        /// Controls:
        ///   1        -> log timing report
        ///   Shift+1  -> clear data and restart timing
        /// </summary>
        public static void HandleInput()
        {
            if (!Enabled) return;

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (shift)
                {
                    Clear();
                }
                else
                {
                    LogResults();
                }
            }
        }

        /// <summary>
        /// Called by Better Work Tab when its window opens or closes.
        /// Lets the profiler know when to count "Work tab open" time.
        /// </summary>
        public static void NotifyWorkTabOpen(bool open)
        {
            _workTabOpen = open;
        }

        /// <summary>
        /// Clear all timing data and reset the reference frame/time.
        /// </summary>
        public static void Clear()
        {
            _data.Clear();
            _startFrame = UTime.frameCount;
            _startRealtime = UTime.realtimeSinceStartup;
            _workTabOpenSeconds = 0;

            Log.Message("[SpineTiming] Data cleared.");
        }

        /// <summary>
        /// Log a summary of all recorded timings, sorted by total cost.
        ///
        /// For each section prints:
        ///   - Calls this frame / total calls
        ///   - Average and max ms per call
        ///   - Approximate share of a 60 FPS frame budget
        /// </summary>
        public static void LogResults()
        {
            if (_data.Count == 0)
            {
                Log.Message("[SpineTiming] No data collected.");
                return;
            }

            var sb = new StringBuilder();
            int frames = Math.Max(1, UTime.frameCount - _startFrame);
            double elapsedSeconds = Math.Max(0.0, UTime.realtimeSinceStartup - _startRealtime);

            sb.AppendLine("[SpineTiming] ======== PERFORMANCE REPORT ========");
            sb.AppendLine($"Elapsed real time: {elapsedSeconds:F1} s");
            sb.AppendLine($"Approx frames recorded: {frames}");
            sb.AppendLine($"Work tab open: {_workTabOpenSeconds:F1} s");
            sb.AppendLine("Sorted by highest total cost over time.");
            sb.AppendLine();

            foreach (var kv in _data.OrderByDescending(k => k.Value.TotalTicks))
            {
                var name = kv.Key;
                var t = kv.Value;

                double totalMs = t.TotalTicks / _ticksPerMs;
                double maxMs = t.MaxTicks / _ticksPerMs;
                double avgMs = totalMs / (t.TotalCalls > 0 ? t.TotalCalls : 1);

                // Approximate share of 16.6 ms (60 FPS) in the current frame.
                // Uses average per call * callsThisFrame as an estimate.
                double estimatedFrameMs = avgMs * t.CallsThisFrame;
                double frameBudgetPct = (estimatedFrameMs / 16.6) * 100.0;

                sb.AppendLine($"[{name}]");
                sb.AppendLine($"   Calls: {t.CallsThisFrame} this frame / {t.TotalCalls} total");

                string spikeWarning = maxMs > 2.0 ? " << SPIKE >>" : string.Empty;
                sb.AppendLine($"   Time:  Avg {avgMs:F4} ms | Max {maxMs:F4} ms{spikeWarning}");

                if (t.CallsThisFrame > 0)
                {
                    string impactWarning = frameBudgetPct > 5.0 ? " << HIGH IMPACT >>" : string.Empty;
                    sb.AppendLine($"   Frame Budget (approx): {frameBudgetPct:F2}%{impactWarning}");
                }

                sb.AppendLine("----------------------------------");
            }

            Log.Message(sb.ToString());
        }
    }
}
