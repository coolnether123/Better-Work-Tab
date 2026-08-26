using System;
using Spine.Collections;
using Better_Work_Tab.UI.WorkGrid.Rendering;

namespace Better_Work_Tab.UI.WorkGrid.Snapshots
{
    internal enum WorkGridRowKind : byte
    {
        Pawn,
        Divider
    }

    internal enum WorkGridColumnWorkerKind : byte
    {
        Other,
        PawnLabel,
        WorkPriority,
        SubWorkPriority
    }

    [Flags]
    internal enum WorkGridRowVisualFlags : byte
    {
        None = 0,
        HasBackground = 1 << 0
    }

    [Flags]
    internal enum WorkCellVisualFlags : ushort
    {
        None = 0,
        Disabled = 1 << 0,
        Incapable = 1 << 1,
        AgeDisabled = 1 << 2,
        OverrideRing = 1 << 3,
        BestPawn = 1 << 4,
        HasPassion = 1 << 5,
        IdeologyWarning = 1 << 6,
        LowSkillWarning = 1 << 7,
        ManualPriorityMode = 1 << 8
    }

    internal readonly struct WorkGridRowEntry
    {
        internal WorkGridRowEntry(
            WorkGridRowKind kind,
            int pawnId,
            string dividerLabel,
            uint dividerColor,
            int dividerOrder,
            bool dividerCollapsed,
            uint backgroundColor = 0,
            WorkGridRowVisualFlags visualFlags = WorkGridRowVisualFlags.None)
        {
            Kind = kind;
            PawnId = pawnId;
            DividerLabel = dividerLabel ?? string.Empty;
            DividerColor = dividerColor;
            DividerOrder = dividerOrder;
            DividerCollapsed = dividerCollapsed;
            BackgroundColor = backgroundColor;
            VisualFlags = visualFlags;
        }

        internal WorkGridRowKind Kind { get; }
        internal int PawnId { get; }
        internal string DividerLabel { get; }
        internal uint DividerColor { get; }
        internal int DividerOrder { get; }
        internal bool DividerCollapsed { get; }
        internal uint BackgroundColor { get; }
        internal WorkGridRowVisualFlags VisualFlags { get; }
    }

    internal readonly struct WorkGridPreparedRowSpan
    {
        internal WorkGridPreparedRowSpan(int firstCellIndex, int cellCount, uint revision)
        {
            FirstCellIndex = firstCellIndex;
            CellCount = cellCount;
            Revision = revision;
        }

        internal int FirstCellIndex { get; }
        internal int CellCount { get; }
        internal uint Revision { get; }

        internal WorkGridPreparedRowSpan WithRevision(uint revision)
        {
            return new WorkGridPreparedRowSpan(FirstCellIndex, CellCount, revision);
        }
    }

    internal readonly struct WorkGridColumnEntry
    {
        internal WorkGridColumnEntry(
            ushort columnIndex,
            ushort workTypeId,
            ushort workGiverId,
            string workTypeName,
            string workGiverName,
            string workerClass,
            WorkGridColumnWorkerKind workerKind,
            bool isExpandBesideChild = false)
        {
            ColumnIndex = columnIndex;
            WorkTypeId = workTypeId;
            WorkGiverId = workGiverId;
            WorkTypeName = workTypeName ?? string.Empty;
            WorkGiverName = workGiverName ?? string.Empty;
            WorkerClass = workerClass ?? string.Empty;
            WorkerKind = workerKind;
            IsExpandBesideChild = isExpandBesideChild;
        }

        internal ushort ColumnIndex { get; }
        internal ushort WorkTypeId { get; }
        internal ushort WorkGiverId { get; }
        internal string WorkTypeName { get; }
        internal string WorkGiverName { get; }
        internal string WorkerClass { get; }
        internal WorkGridColumnWorkerKind WorkerKind { get; }
        internal bool IsExpandBesideChild { get; }
    }

    internal readonly struct WorkBoxVisualState
    {
        internal WorkBoxVisualState(
            byte priority,
            byte skillBand,
            float skillBlend,
            byte passion,
            uint priorityColor,
            WorkCellVisualFlags flags)
        {
            Priority = priority;
            SkillBand = skillBand;
            SkillBlend = skillBlend;
            Passion = passion;
            PriorityColor = priorityColor;
            Flags = flags;
        }

        internal byte Priority { get; }
        internal byte SkillBand { get; }
        internal float SkillBlend { get; }
        internal byte Passion { get; }
        internal uint PriorityColor { get; }
        internal WorkCellVisualFlags Flags { get; }
    }

    /// <summary>
    /// Stable child-cell pixels and sparse overlay flags captured before drawing.
    /// Live pawn and definition references belong to the renderer's layout lookup,
    /// not to this immutable presentation value.
    /// </summary>
    internal readonly struct WorkGridSubWorkVisualState
    {
        internal WorkGridSubWorkVisualState(
            WorkBoxVisualState workBoxVisual,
            byte effectivePriority,
            bool hasPawnOverride,
            bool hasScheduleIndicator,
            bool canUseStablePresentation)
        {
            WorkBoxVisual = workBoxVisual;
            EffectivePriority = effectivePriority;
            HasPawnOverride = hasPawnOverride;
            HasScheduleIndicator = hasScheduleIndicator;
            CanUseStablePresentation = canUseStablePresentation;
            IsPrepared = true;
        }

        internal WorkBoxVisualState WorkBoxVisual { get; }
        internal byte EffectivePriority { get; }
        internal bool HasPawnOverride { get; }
        internal bool HasScheduleIndicator { get; }
        internal bool CanUseStablePresentation { get; }
        internal bool IsPrepared { get; }
        internal bool HasDynamicRing => HasPawnOverride || HasScheduleIndicator;
    }

    /// <summary>
    /// Exact presentation inputs shared by every retained work-box surface.
    /// Keep these values separate: cache correctness must not depend on a
    /// collision-prone precomputed hash masquerading as a revision.
    /// </summary>
    internal readonly struct WorkGridRetainedVisualKey : IEquatable<WorkGridRetainedVisualKey>
    {
        internal WorkGridRetainedVisualKey(
            int uiScaleMilli,
            long settingsThemeLanguageScaleRevision,
            int maximumPriority)
        {
            UiScaleMilli = uiScaleMilli;
            SettingsThemeLanguageScaleRevision = settingsThemeLanguageScaleRevision;
            MaximumPriority = maximumPriority;
        }

        internal int UiScaleMilli { get; }
        internal long SettingsThemeLanguageScaleRevision { get; }
        internal int MaximumPriority { get; }

        public bool Equals(WorkGridRetainedVisualKey other)
        {
            return UiScaleMilli == other.UiScaleMilli &&
                   SettingsThemeLanguageScaleRevision ==
                       other.SettingsThemeLanguageScaleRevision &&
                   MaximumPriority == other.MaximumPriority;
        }

        public override bool Equals(object obj)
        {
            return obj is WorkGridRetainedVisualKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = UiScaleMilli;
                hash = (hash * 397) ^ SettingsThemeLanguageScaleRevision.GetHashCode();
                hash = (hash * 397) ^ MaximumPriority;
                return hash;
            }
        }
    }

    internal readonly struct WorkCellVisualState
    {

        internal WorkCellVisualState(
            int pawnId,
            ushort workTypeId,
            ushort columnIndex,
            byte priority,
            byte skillBand,
            float skillBlend,
            byte passion,
            uint priorityColor,
            WorkCellVisualFlags flags,
            WorkGridSubWorkVisualState subWork = default)
        {
            PawnId = pawnId;
            WorkTypeId = workTypeId;
            ColumnIndex = columnIndex;
            Priority = priority;
            SkillBand = skillBand;
            SkillBlend = skillBlend;
            Passion = passion;
            PriorityColor = priorityColor;
            Flags = flags;
            SubWork = subWork;
        }

        internal int PawnId { get; }
        internal ushort WorkTypeId { get; }
        internal ushort ColumnIndex { get; }
        internal byte Priority { get; }
        internal byte SkillBand { get; }
        internal float SkillBlend { get; }
        internal byte Passion { get; }
        internal uint PriorityColor { get; }
        internal WorkCellVisualFlags Flags { get; }
        internal WorkGridSubWorkVisualState SubWork { get; }
    }

    /// <summary>
    /// Immutable, domain-derived prepared state reused until invalidated.
    /// Per-pass geometry and live interaction references remain separate in
    /// <c>WorkTabView</c>; this type never owns RimWorld entities or cache objects.
    /// </summary>
    internal sealed class WorkGridSnapshot
    {
        internal WorkGridSnapshot(
            long topologyRevision,
            int layoutRevision,
            ImmutableSnapshotArray<WorkGridRowEntry> rows,
            ImmutableSnapshotArray<WorkGridColumnEntry> columns,
            ImmutableSnapshotArray<WorkCellVisualState> cells,
            ImmutableSnapshotArray<int> cellIndexes,
            ImmutableSnapshotArray<WorkGridPreparedRowSpan> preparedRows,
            ImmutableSnapshotArray<PreparedPawnLabelPresentation> pawnLabels,
            WorkGridRetainedVisualKey retainedVisualKey)
        {
            TopologyRevision = topologyRevision;
            LayoutRevision = layoutRevision;
            Rows = rows;
            Columns = columns;
            Cells = cells;
            CellIndexes = cellIndexes;
            PreparedRows = preparedRows;
            PawnLabels = pawnLabels;
            RetainedVisualKey = retainedVisualKey;
        }

        internal long TopologyRevision { get; }
        internal int LayoutRevision { get; }
        internal ImmutableSnapshotArray<WorkGridRowEntry> Rows { get; }
        internal ImmutableSnapshotArray<WorkGridColumnEntry> Columns { get; }
        internal ImmutableSnapshotArray<WorkCellVisualState> Cells { get; }
        internal ImmutableSnapshotArray<int> CellIndexes { get; }
        internal ImmutableSnapshotArray<WorkGridPreparedRowSpan> PreparedRows { get; }
        internal ImmutableSnapshotArray<PreparedPawnLabelPresentation> PawnLabels { get; }
        internal WorkGridRetainedVisualKey RetainedVisualKey { get; }
    }
}
