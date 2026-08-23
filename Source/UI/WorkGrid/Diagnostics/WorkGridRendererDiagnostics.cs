using System;
using System.Collections.Generic;
using System.Threading;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using Better_Work_Tab.Foundation;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Diagnostics
{
    public readonly struct WorkGridSnapshotDiagnosticStats
    {
        internal WorkGridSnapshotDiagnosticStats(
            long revision,
            long buildCount,
            long lastBuildTicks,
            int snapshotRetainedBytes,
            int geometryRetainedBytes,
            int priorityDirtyCount,
            bool incremental,
            int updatedCellCount)
        {
            Revision = revision;
            BuildCount = buildCount;
            LastBuildTicks = lastBuildTicks;
            SnapshotRetainedBytes = snapshotRetainedBytes;
            GeometryRetainedBytes = geometryRetainedBytes;
            PriorityDirtyCount = priorityDirtyCount;
            Incremental = incremental;
            UpdatedCellCount = updatedCellCount;
        }

        public long Revision { get; }
        public long BuildCount { get; }
        public long LastBuildTicks { get; }
        public int SnapshotRetainedBytes { get; }
        public int GeometryRetainedBytes { get; }
        public int PriorityDirtyCount { get; }
        public bool Incremental { get; }
        public int UpdatedCellCount { get; }
    }

    public sealed class WorkGridRendererDiagnosticSnapshot
    {
        internal WorkGridRendererDiagnosticSnapshot(
            string activeRendererId,
            WorkGridRendererMode selectionMode,
            WorkGridForcedRendererMode forcedMode,
            WorkGridFallbackReason[] fallbackReasons,
            bool quarantined)
        {
            ActiveRendererId = activeRendererId ?? string.Empty;
            SelectionMode = selectionMode;
            ForcedMode = forcedMode;
            _fallbackReasons = fallbackReasons ?? Array.Empty<WorkGridFallbackReason>();
            IsQuarantined = quarantined;
        }

        public string ActiveRendererId { get; }
        public WorkGridRendererMode SelectionMode { get; }
        public WorkGridForcedRendererMode ForcedMode { get; }
        private readonly WorkGridFallbackReason[] _fallbackReasons;
        public IReadOnlyList<WorkGridFallbackReason> FallbackReasons => _fallbackReasons;
        public bool IsQuarantined { get; }
    }

    public static class WorkGridRendererDiagnostics
    {
        private static WorkGridRendererDiagnosticSnapshot _current =
            new WorkGridRendererDiagnosticSnapshot(
                VanillaWorkGridRenderer.RendererId,
                WorkGridRendererMode.Auto,
                WorkGridForcedRendererMode.None,
                new[] { new WorkGridFallbackReason(WorkGridFallbackReasonCode.OptimizedUnavailable) },
                false);
        private static int _forcedMode;
        private static long _snapshotRevision;
        private static long _snapshotBuildCount;
        private static long _snapshotLastBuildTicks;
        private static int _snapshotRetainedBytes;
        private static int _geometryRetainedBytes;
        private static int _priorityDirtyCount;
        private static int _snapshotIncremental;
        private static int _snapshotUpdatedCellCount;

        public static WorkGridRendererDiagnosticSnapshot Current => Volatile.Read(ref _current);

        public static WorkGridSnapshotDiagnosticStats SnapshotStats => new WorkGridSnapshotDiagnosticStats(
            Interlocked.Read(ref _snapshotRevision),
            Interlocked.Read(ref _snapshotBuildCount),
            Interlocked.Read(ref _snapshotLastBuildTicks),
            Volatile.Read(ref _snapshotRetainedBytes),
            Volatile.Read(ref _geometryRetainedBytes),
            Volatile.Read(ref _priorityDirtyCount),
            Volatile.Read(ref _snapshotIncremental) != 0,
            Volatile.Read(ref _snapshotUpdatedCellCount));

        public static WorkGridForcedRendererMode ForcedMode
        {
            get => (WorkGridForcedRendererMode)Volatile.Read(ref _forcedMode);
            set => Volatile.Write(ref _forcedMode, (int)value);
        }

        public static void CycleForcedMode()
        {
            int next = ((int)ForcedMode + 1) % Enum.GetValues(typeof(WorkGridForcedRendererMode)).Length;
            ForcedMode = (WorkGridForcedRendererMode)next;
            Log.Message("[BWT] Work-grid renderer diagnostic mode: " + ForcedMode + ".");
        }

        public static void LogCurrent()
        {
            WorkGridRendererDiagnosticSnapshot snapshot = Current;
            WorkGridSnapshotDiagnosticStats snapshotStats = SnapshotStats;
            string fallback = snapshot.FallbackReasons.Count == 0
                ? "none"
                : snapshot.FallbackReasons[0].Code +
                  (string.IsNullOrEmpty(snapshot.FallbackReasons[0].Detail)
                      ? string.Empty
                      : " (" + snapshot.FallbackReasons[0].Detail + ")");
            Log.Message(
                "[BWT] Work-grid renderer: active=" + snapshot.ActiveRendererId +
                ", setting=" + snapshot.SelectionMode +
                ", forced=" + snapshot.ForcedMode +
                ", fallback=" + fallback +
                ", quarantined=" + snapshot.IsQuarantined +
                ", snapshotRevision=" + snapshotStats.Revision +
                ", snapshotBuilds=" + snapshotStats.BuildCount +
                ", snapshotTicks=" + snapshotStats.LastBuildTicks +
                ", priorityDirty=" + snapshotStats.PriorityDirtyCount +
                ", incremental=" + snapshotStats.Incremental +
                ", updatedCells=" + snapshotStats.UpdatedCellCount + ".");
        }

        internal static bool Publish(
            string activeRendererId,
            WorkGridRendererMode selectionMode,
            WorkGridForcedRendererMode forcedMode,
            WorkGridFallbackReason fallback,
            bool quarantined)
        {
            WorkGridRendererDiagnosticSnapshot current = Current;
            WorkGridFallbackReason currentFallback = current.FallbackReasons.Count == 0
                ? default
                : current.FallbackReasons[0];
            if (string.Equals(current.ActiveRendererId, activeRendererId, StringComparison.Ordinal) &&
                current.SelectionMode == selectionMode &&
                current.ForcedMode == forcedMode &&
                current.IsQuarantined == quarantined &&
                currentFallback.Code == fallback.Code &&
                string.Equals(currentFallback.Detail, fallback.Detail, StringComparison.Ordinal))
            {
                return false;
            }

            WorkGridFallbackReason[] reasons = fallback.Code == WorkGridFallbackReasonCode.None
                ? Array.Empty<WorkGridFallbackReason>()
                : new[] { fallback };
            Volatile.Write(ref _current, new WorkGridRendererDiagnosticSnapshot(
                activeRendererId,
                selectionMode,
                forcedMode,
                reasons,
                quarantined));
            return true;
        }

        internal static void RecordSnapshotBuild(
            long revision,
            long buildTicks,
            int snapshotRetainedBytes,
            int priorityDirtyCount,
            int geometryRetainedBytes,
            bool incremental,
            int updatedCellCount)
        {
            Interlocked.Exchange(ref _snapshotRevision, revision);
            Interlocked.Increment(ref _snapshotBuildCount);
            Interlocked.Exchange(ref _snapshotLastBuildTicks, buildTicks);
            Volatile.Write(ref _snapshotRetainedBytes, snapshotRetainedBytes);
            Volatile.Write(ref _geometryRetainedBytes, geometryRetainedBytes);
            Volatile.Write(ref _priorityDirtyCount, priorityDirtyCount);
            Volatile.Write(ref _snapshotIncremental, incremental ? 1 : 0);
            Volatile.Write(ref _snapshotUpdatedCellCount, updatedCellCount);

            IRenderDiagnosticsSink sink = BwtWorkGridDiagnosticsSink.Instance;
            if (sink.Enabled)
            {
                sink.Record(new RenderDiagnostic(
                    RenderDiagnosticSeverity.Information,
                    "work-grid-snapshot",
                    "revision=" + revision +
                    ", builds=" + Interlocked.Read(ref _snapshotBuildCount) +
                    ", ticks=" + buildTicks +
                    ", retainedBytes=" + snapshotRetainedBytes +
                    ", geometryBytes=" + geometryRetainedBytes +
                    ", priorityDirty=" + priorityDirtyCount +
                    ", incremental=" + incremental +
                    ", updatedCells=" + updatedCellCount + "."));
            }
        }

        internal static void RecordSnapshotCleared()
        {
            Interlocked.Exchange(ref _snapshotRevision, 0);
            Volatile.Write(ref _snapshotRetainedBytes, 0);
            Volatile.Write(ref _geometryRetainedBytes, 0);
            Volatile.Write(ref _priorityDirtyCount, 0);
            Volatile.Write(ref _snapshotIncremental, 0);
            Volatile.Write(ref _snapshotUpdatedCellCount, 0);
        }

    }

    internal sealed class BwtWorkGridDiagnosticsSink : IRenderDiagnosticsSink
    {
        internal static readonly BwtWorkGridDiagnosticsSink Instance = new BwtWorkGridDiagnosticsSink();

        public bool Enabled
        {
            get
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return settings != null &&
                       settings.enableDebugLogging &&
                       settings.debugFeatureToggles.TryGetValue(DebugFeature.Performance, out bool enabled) &&
                       enabled;
            }
        }

        public void Record(RenderDiagnostic diagnostic)
        {
            if (!Enabled)
            {
                return;
            }

            string exception = diagnostic.Exception == null ? string.Empty : " " + diagnostic.Exception;
            BetterWorkTabMod.DebugLog(
                "[WorkGridRenderer:" + diagnostic.SourceId + "] " + diagnostic.Message + exception,
                DebugFeature.Performance);
        }
    }
}
