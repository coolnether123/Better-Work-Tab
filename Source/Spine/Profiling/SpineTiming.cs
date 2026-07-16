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
    ///   4) Optionally configure a log delegate and generic activity-seconds provider.
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
        private static long _startManagedBytes;
        private static long _peakManagedBytes;
        private static long _startPrivateBytes;
        private static long _peakPrivateBytes;
        private static long _startWorkingSetBytes;
        private static long _peakWorkingSetBytes;
        private static readonly int[] _startCollectionCounts = new int[3];
        private static int _nextMemorySampleFrame;

        private static Action<string> _log = _ => { };
        private static Func<double> _activitySecondsProvider;
        private static string _activityLabel;
        private static double _activitySecondsBaseline;

        public static void Configure(
            Action<string> log,
            Func<double> activitySecondsProvider = null,
            string activityLabel = null)
        {
            _log = log ?? (_ => { });
            _activitySecondsProvider = activitySecondsProvider;
            _activityLabel = activityLabel;
            _activitySecondsBaseline = ReadActivitySeconds();
        }

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
                    _activitySecondsBaseline = ReadActivitySeconds();
                    _data.Clear();
                    ResetMemoryBaseline();
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
                Record(name, Stopwatch.GetTimestamp() - start);
            }
        }

        public static T Time<T>(string name, Func<T> action)
        {
            if (!Enabled)
            {
                return action();
            }

            long start = Stopwatch.GetTimestamp();

            try
            {
                return action();
            }
            finally
            {
                Record(name, Stopwatch.GetTimestamp() - start);
            }
        }

        private static void Record(string name, long elapsed)
        {
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

        /// <summary>
        /// Called once per frame.
        /// Resets per-frame call counters.
        ///
        /// Hook this from GameComponentUpdate().
        /// </summary>
        public static void OnFrameStart()
        {
            if (!Enabled) return;

            if (UTime.frameCount >= _nextMemorySampleFrame)
            {
                SampleMemory();
                _nextMemorySampleFrame = UTime.frameCount + 30;
            }

            foreach (var entry in _data.Values)
            {
                entry.CallsThisFrame = 0;
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
        /// Clear all timing data and reset the reference frame/time.
        /// </summary>
        public static void Clear()
        {
            _data.Clear();
            _startFrame = UTime.frameCount;
            _startRealtime = UTime.realtimeSinceStartup;
            _activitySecondsBaseline = ReadActivitySeconds();
            ResetMemoryBaseline();

            _log("[SpineTiming] Data cleared.");
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
            _log(GetReport());
        }

        public static string GetReport()
        {
            if (_data.Count == 0)
            {
                return "[SpineTiming] No data collected.";
            }

            var sb = new StringBuilder();
            int frames = Math.Max(1, UTime.frameCount - _startFrame);
            double elapsedSeconds = Math.Max(0.0, UTime.realtimeSinceStartup - _startRealtime);

            sb.AppendLine("[SpineTiming] ======== PERFORMANCE REPORT ========");
            sb.AppendLine($"Elapsed real time: {elapsedSeconds:F1} s");
            sb.AppendLine($"Approx frames recorded: {frames}");
            if (_activitySecondsProvider != null && !string.IsNullOrEmpty(_activityLabel))
            {
                double activitySeconds = Math.Max(0.0, ReadActivitySeconds() - _activitySecondsBaseline);
                sb.AppendLine($"{_activityLabel}: {activitySeconds:F1} s");
            }
            long managedNow = GC.GetTotalMemory(false);
            long privateNow;
            long workingSetNow;
            ReadProcessMemory(out privateNow, out workingSetNow);
            sb.AppendLine(
                $"Managed memory: start {FormatBytes(_startManagedBytes)} | now {FormatBytes(managedNow)} | " +
                $"delta {FormatSignedBytes(managedNow - _startManagedBytes)} | sampled peak {FormatBytes(Math.Max(_peakManagedBytes, managedNow))}");
            if (_startPrivateBytes > 0 || privateNow > 0)
            {
                sb.AppendLine(
                    $"Process private: start {FormatBytes(_startPrivateBytes)} | now {FormatBytes(privateNow)} | " +
                    $"delta {FormatSignedBytes(privateNow - _startPrivateBytes)} | sampled peak {FormatBytes(Math.Max(_peakPrivateBytes, privateNow))}");
            }
            else
            {
                sb.AppendLine("Process private: unavailable from this Mono runtime; sample it from the external harness.");
            }

            if (_startWorkingSetBytes > 0 || workingSetNow > 0)
            {
                sb.AppendLine(
                    $"Working set: start {FormatBytes(_startWorkingSetBytes)} | now {FormatBytes(workingSetNow)} | " +
                    $"delta {FormatSignedBytes(workingSetNow - _startWorkingSetBytes)} | sampled peak {FormatBytes(Math.Max(_peakWorkingSetBytes, workingSetNow))}");
            }
            else
            {
                sb.AppendLine("Working set: unavailable from this Mono runtime; sample it from the external harness.");
            }
            sb.AppendLine(
                $"GC collections: gen0 {GC.CollectionCount(0) - _startCollectionCounts[0]} | " +
                $"gen1 {GC.CollectionCount(1) - _startCollectionCounts[1]} | " +
                $"gen2 {GC.CollectionCount(2) - _startCollectionCounts[2]}");
            sb.AppendLine("Sorted by highest total cost over time.");
            sb.AppendLine();

            foreach (var kv in _data.OrderByDescending(k => k.Value.TotalTicks))
            {
                var name = kv.Key;
                var t = kv.Value;

                double totalMs = t.TotalTicks / _ticksPerMs;
                double maxMs = t.MaxTicks / _ticksPerMs;
                double avgMs = totalMs / (t.TotalCalls > 0 ? t.TotalCalls : 1);
                double callsPerFrame = t.TotalCalls / (double)frames;
                double avgMsPerFrame = totalMs / frames;

                // Approximate share of 16.6 ms (60 FPS) in the current frame.
                // Uses average per call * callsThisFrame as an estimate.
                double estimatedFrameMs = avgMs * t.CallsThisFrame;
                double frameBudgetPct = (estimatedFrameMs / 16.6) * 100.0;

                sb.AppendLine($"[{name}]");
                sb.AppendLine($"   Calls: {t.CallsThisFrame} this frame / {t.TotalCalls} total");
                sb.AppendLine($"   Per frame: {callsPerFrame:F3} calls/frame | {avgMsPerFrame:F4} ms/frame");

                string spikeWarning = maxMs > 2.0 ? " << SPIKE >>" : string.Empty;
                sb.AppendLine($"   Time:  Avg {avgMs:F4} ms | Max {maxMs:F4} ms{spikeWarning}");

                if (t.CallsThisFrame > 0)
                {
                    string impactWarning = frameBudgetPct > 5.0 ? " << HIGH IMPACT >>" : string.Empty;
                    sb.AppendLine($"   Frame Budget (approx): {frameBudgetPct:F2}%{impactWarning}");
                }

                sb.AppendLine("----------------------------------");
            }

            return sb.ToString();
        }

        private static void ResetMemoryBaseline()
        {
            _startManagedBytes = GC.GetTotalMemory(false);
            _peakManagedBytes = _startManagedBytes;
            ReadProcessMemory(out _startPrivateBytes, out _startWorkingSetBytes);
            _peakPrivateBytes = _startPrivateBytes;
            _peakWorkingSetBytes = _startWorkingSetBytes;
            for (int generation = 0; generation < _startCollectionCounts.Length; generation++)
            {
                _startCollectionCounts[generation] = GC.CollectionCount(generation);
            }

            _nextMemorySampleFrame = UTime.frameCount;
        }

        private static void SampleMemory()
        {
            _peakManagedBytes = Math.Max(_peakManagedBytes, GC.GetTotalMemory(false));
            ReadProcessMemory(out long privateBytes, out long workingSetBytes);
            _peakPrivateBytes = Math.Max(_peakPrivateBytes, privateBytes);
            _peakWorkingSetBytes = Math.Max(_peakWorkingSetBytes, workingSetBytes);
        }

        private static void ReadProcessMemory(out long privateBytes, out long workingSetBytes)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                privateBytes = process.PrivateMemorySize64;
                workingSetBytes = process.WorkingSet64;
            }
        }

        private static string FormatBytes(long bytes)
        {
            return (bytes / (1024d * 1024d)).ToString("F2") + " MiB";
        }

        private static string FormatSignedBytes(long bytes)
        {
            string sign = bytes >= 0 ? "+" : "-";
            return sign + FormatBytes(Math.Abs(bytes));
        }

        private static double ReadActivitySeconds()
        {
            if (_activitySecondsProvider == null)
            {
                return 0.0;
            }

            try
            {
                return _activitySecondsProvider();
            }
            catch
            {
                return 0.0;
            }
        }
    }
}
