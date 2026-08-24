using System;
using System.Collections.Generic;
using Better_Work_Tab.UI.WorkGrid.Projection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Contracts
{
    internal enum WorkGridPreviewMembershipAction
    {
        None = 0,
        ExplicitlyExcluded = 1,
        PawnUnavailable = 2,
        Include = 3,
        Exclude = 4,
        OutsideScope = 5,
        Unavailable = 6
    }

    internal enum WorkGridPreviewMembershipState
    {
        None = 0,
        Included = 1,
        New = 2,
        OutsideScope = 3,
        ExplicitlyExcluded = 4,
        Missing = 5
    }

    internal enum WorkGridInspectionTargetKind
    {
        None = 0,
        ParentPriority = 1,
        SpecificPriority = 2,
        Schedule = 3,
        Ordering = 4
    }

    internal readonly struct WorkGridInspectionTarget : IEquatable<WorkGridInspectionTarget>
    {
        internal WorkGridInspectionTarget(
            WorkGridInspectionTargetKind kind,
            bool isGlobal,
            bool isSpecificSchedule,
            int pawnId,
            string workType,
            string workGiver)
        {
            Kind = kind;
            IsGlobal = isGlobal;
            IsSpecificSchedule = isSpecificSchedule;
            PawnId = pawnId;
            WorkType = workType ?? string.Empty;
            WorkGiver = workGiver ?? string.Empty;
        }

        internal WorkGridInspectionTargetKind Kind { get; }
        internal bool IsGlobal { get; }
        internal bool IsSpecificSchedule { get; }
        internal int PawnId { get; }
        internal string WorkType { get; }
        internal string WorkGiver { get; }

        public bool Equals(WorkGridInspectionTarget other)
        {
            return Kind == other.Kind &&
                IsGlobal == other.IsGlobal &&
                IsSpecificSchedule == other.IsSpecificSchedule &&
                PawnId == other.PawnId &&
                StringComparer.Ordinal.Equals(WorkType, other.WorkType) &&
                StringComparer.Ordinal.Equals(WorkGiver, other.WorkGiver);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkGridInspectionTarget &&
                Equals((WorkGridInspectionTarget)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ IsGlobal.GetHashCode();
                hash = (hash * 397) ^ IsSpecificSchedule.GetHashCode();
                hash = (hash * 397) ^ PawnId;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkType);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkGiver);
                return hash;
            }
        }
    }

    /// <summary>
    /// Frame-local Work-grid capabilities supplied by an active preview owner.
    /// The grid depends only on this neutral contract and never on workload
    /// sessions, controllers, or persistence types.
    /// </summary>
    internal interface IWorkGridPreviewPort
    {
        IWorkTabEffectiveStateProvider ScopedProvider { get; }
        bool IsActive { get; }
        string LastMessage { get; }
        bool TryHandleHistoryShortcut(Event evt);
        bool TrySetManualMode(bool manualMode);
        bool EnsurePawnIncludedForPriorityEdit(Pawn pawn);
        WorkGridPreviewMembershipAction GetMembershipAction(Pawn pawn);
        bool ToggleMembership(Pawn pawn);
        bool TryGetMembershipPresentation(
            Pawn pawn,
            out WorkGridPreviewMembershipState state);
        bool IsInspectionActive { get; }
        bool HasInspectionRowLevelChanges { get; }
        bool HasInspectionCellTargets { get; }
        IReadOnlyList<WorkGridInspectionTarget> InspectionTargets { get; }
        bool IsInspectionRowLevelChanged(Pawn pawn);
    }

    /// <summary>
    /// Optional read-side capture for a finished WorkTabView. Input continues
    /// through the live preview port, while rendering reads the membership and
    /// inspection state that existed when the view was assembled.
    /// </summary>
    internal interface IWorkGridPreviewViewSource
    {
        IWorkGridPreviewPort CapturePreviewView();
    }
}
