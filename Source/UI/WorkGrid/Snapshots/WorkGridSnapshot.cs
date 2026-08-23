using System;
using RimWorld;
using Spine.Collections;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Snapshots
{
    public enum WorkGridRowKind : byte
    {
        Pawn,
        Divider
    }

    public enum WorkGridColumnWorkerKind : byte
    {
        Other,
        PawnLabel,
        WorkPriority,
        SubWorkPriority
    }

    [Flags]
    public enum WorkGridRowVisualFlags : byte
    {
        None = 0,
        HasBackground = 1 << 0
    }

    [Flags]
    public enum WorkCellVisualFlags : ushort
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

    public readonly struct WorkGridRowEntry
    {
        public WorkGridRowEntry(
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

        public WorkGridRowKind Kind { get; }
        public int PawnId { get; }
        public string DividerLabel { get; }
        public uint DividerColor { get; }
        public int DividerOrder { get; }
        public bool DividerCollapsed { get; }
        public uint BackgroundColor { get; }
        public WorkGridRowVisualFlags VisualFlags { get; }
    }

    public readonly struct WorkGridColumnEntry
    {
        public WorkGridColumnEntry(
            ushort columnIndex,
            ushort workTypeId,
            ushort workGiverId,
            string workTypeName,
            string workGiverName,
            string workerClass,
            WorkGridColumnWorkerKind workerKind)
        {
            ColumnIndex = columnIndex;
            WorkTypeId = workTypeId;
            WorkGiverId = workGiverId;
            WorkTypeName = workTypeName ?? string.Empty;
            WorkGiverName = workGiverName ?? string.Empty;
            WorkerClass = workerClass ?? string.Empty;
            WorkerKind = workerKind;
        }

        public ushort ColumnIndex { get; }
        public ushort WorkTypeId { get; }
        public ushort WorkGiverId { get; }
        public string WorkTypeName { get; }
        public string WorkGiverName { get; }
        public string WorkerClass { get; }
        public WorkGridColumnWorkerKind WorkerKind { get; }
    }

    public readonly struct WorkCellVisualState
    {
        public WorkCellVisualState(
            Pawn pawn,
            WorkTypeDef workType,
            int pawnId,
            ushort columnIndex,
            byte priority,
            byte skillBand,
            float skillBlend,
            byte passion,
            uint priorityColor,
            WorkCellVisualFlags flags,
            uint revision)
        {
            Pawn = pawn;
            WorkType = workType;
            PawnId = pawnId;
            ColumnIndex = columnIndex;
            Priority = priority;
            SkillBand = skillBand;
            SkillBlend = skillBlend;
            Passion = passion;
            PriorityColor = priorityColor;
            Flags = flags;
            Revision = revision;
        }

        public Pawn Pawn { get; }
        public WorkTypeDef WorkType { get; }
        public int PawnId { get; }
        public ushort ColumnIndex { get; }
        public byte Priority { get; }
        public byte SkillBand { get; }
        public float SkillBlend { get; }
        public byte Passion { get; }
        public uint PriorityColor { get; }
        public WorkCellVisualFlags Flags { get; }
        public uint Revision { get; }
    }

    public sealed class WorkGridSnapshot
    {
        internal WorkGridSnapshot(
            long revision,
            int layoutRevision,
            WorkGridRevisionSet revisions,
            ImmutableSnapshotArray<WorkGridRowEntry> rows,
            ImmutableSnapshotArray<WorkGridColumnEntry> columns,
            ImmutableSnapshotArray<WorkCellVisualState> cells,
            int retainedCapacityBytes,
            bool manualPriorities,
            int maxPriority,
            int uiScaleRevision,
            int fontThemeRevision,
            int priorityRangeRevision)
        {
            Revision = revision;
            LayoutRevision = layoutRevision;
            Revisions = revisions;
            Rows = rows ?? ImmutableSnapshotArray<WorkGridRowEntry>.Empty;
            Columns = columns ?? ImmutableSnapshotArray<WorkGridColumnEntry>.Empty;
            Cells = cells ?? ImmutableSnapshotArray<WorkCellVisualState>.Empty;
            RetainedCapacityBytes = retainedCapacityBytes;
            ManualPriorities = manualPriorities;
            MaxPriority = maxPriority;
            UiScaleRevision = uiScaleRevision;
            FontThemeRevision = fontThemeRevision;
            PriorityRangeRevision = priorityRangeRevision;
        }

        public long Revision { get; }
        public int LayoutRevision { get; }
        public WorkGridRevisionSet Revisions { get; }
        public ImmutableSnapshotArray<WorkGridRowEntry> Rows { get; }
        public ImmutableSnapshotArray<WorkGridColumnEntry> Columns { get; }
        public ImmutableSnapshotArray<WorkCellVisualState> Cells { get; }
        public int RetainedCapacityBytes { get; }
        public bool ManualPriorities { get; }
        public int MaxPriority { get; }
        public int UiScaleRevision { get; }
        public int FontThemeRevision { get; }
        public int PriorityRangeRevision { get; }
    }
}
