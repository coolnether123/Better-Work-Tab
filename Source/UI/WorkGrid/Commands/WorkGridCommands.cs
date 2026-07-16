using Better_Work_Tab.Features.TimePriority;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Commands
{
    internal enum WorkGridCommandKind
    {
        SetPriority,
        StepPriority,
        PaintPriorityRange,
        TogglePriority,
        SetWorkGiverPriority,
        OpenSchedule,
        SelectRuleTarget,
        MoveWorkGiver
    }

    internal interface IWorkGridCommand
    {
        WorkGridCommandKind Kind { get; }
    }

    internal readonly struct SetPriorityCommand : IWorkGridCommand
    {
        internal SetPriorityCommand(Pawn pawn, WorkTypeDef workType, int priority)
        {
            Pawn = pawn;
            WorkType = workType;
            Priority = priority;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.SetPriority;
        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal int Priority { get; }
    }

    internal readonly struct StepPriorityCommand : IWorkGridCommand
    {
        internal StepPriorityCommand(Pawn pawn, WorkTypeDef workType, int currentPriority, int direction)
        {
            Pawn = pawn;
            WorkType = workType;
            CurrentPriority = currentPriority;
            Direction = direction;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.StepPriority;
        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal int CurrentPriority { get; }
        internal int Direction { get; }
    }

    internal readonly struct TogglePriorityCommand : IWorkGridCommand
    {
        internal TogglePriorityCommand(Pawn pawn, WorkTypeDef workType, int currentPriority)
        {
            Pawn = pawn;
            WorkType = workType;
            CurrentPriority = currentPriority;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.TogglePriority;
        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal int CurrentPriority { get; }
    }

    internal readonly struct PriorityPaintTarget
    {
        internal PriorityPaintTarget(Pawn pawn, WorkTypeDef workType)
        {
            Pawn = pawn;
            WorkType = workType;
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
    }

    internal readonly struct PaintPriorityRangeCommand : IWorkGridCommand
    {
        internal PaintPriorityRangeCommand(IReadOnlyList<PriorityPaintTarget> targets, int priority)
        {
            Targets = targets;
            Priority = priority;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.PaintPriorityRange;
        internal IReadOnlyList<PriorityPaintTarget> Targets { get; }
        internal int Priority { get; }
    }

    internal readonly struct SetWorkGiverPriorityCommand : IWorkGridCommand
    {
        internal SetWorkGiverPriorityCommand(int pawnId, WorkGiverDef workGiver, int priority)
        {
            PawnId = pawnId;
            WorkGiver = workGiver;
            Priority = priority;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.SetWorkGiverPriority;
        internal int PawnId { get; }
        internal WorkGiverDef WorkGiver { get; }
        internal int Priority { get; }
    }

    internal readonly struct OpenScheduleCommand : IWorkGridCommand
    {
        internal OpenScheduleCommand(TimePriorityTarget target, Rect anchor, int fallbackPriority)
        {
            Target = target;
            Anchor = anchor;
            FallbackPriority = fallbackPriority;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.OpenSchedule;
        internal TimePriorityTarget Target { get; }
        internal Rect Anchor { get; }
        internal int FallbackPriority { get; }
    }

    internal readonly struct SelectRuleTargetCommand : IWorkGridCommand
    {
        internal SelectRuleTargetCommand(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            Pawn pawn,
            int priority,
            Rect bounds,
            bool header)
        {
            WorkType = workType;
            WorkGiver = workGiver;
            Pawn = pawn;
            Priority = priority;
            Bounds = bounds;
            Header = header;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.SelectRuleTarget;
        internal WorkTypeDef WorkType { get; }
        internal WorkGiverDef WorkGiver { get; }
        internal Pawn Pawn { get; }
        internal int Priority { get; }
        internal Rect Bounds { get; }
        internal bool Header { get; }
    }

    internal readonly struct MoveWorkGiverCommand : IWorkGridCommand
    {
        internal MoveWorkGiverCommand(string workGiverDefName, string workTypeDefName, int insertIndex)
        {
            WorkGiverDefName = workGiverDefName;
            WorkTypeDefName = workTypeDefName;
            InsertIndex = insertIndex;
        }

        public WorkGridCommandKind Kind => WorkGridCommandKind.MoveWorkGiver;
        internal string WorkGiverDefName { get; }
        internal string WorkTypeDefName { get; }
        internal int InsertIndex { get; }
    }
}
