using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Contracts
{
    /// <summary>
    /// Runtime identity for one pawn/work-type/work-giver specific-job value.
    /// A null pawn identifies the shared (global) value. Persistence keys
    /// belong to the workload adapter that implements this contract.
    /// </summary>
    internal readonly struct WorkTabSpecificJobTarget : IEquatable<WorkTabSpecificJobTarget>
    {
        internal WorkTabSpecificJobTarget(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver)
        {
            Pawn = pawn;
            WorkType = workType;
            WorkGiver = workGiver;
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal WorkGiverDef WorkGiver { get; }
        internal bool IsGlobal => Pawn == null;
        internal bool IsValid =>
            WorkType != null && WorkGiver != null &&
            (IsGlobal || Pawn.thingIDNumber > 0);

        public bool Equals(WorkTabSpecificJobTarget other)
        {
            return ReferenceEquals(Pawn, other.Pawn) &&
                   ReferenceEquals(WorkType, other.WorkType) &&
                   ReferenceEquals(WorkGiver, other.WorkGiver);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkTabSpecificJobTarget &&
                   Equals((WorkTabSpecificJobTarget)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Pawn?.thingIDNumber ?? -1;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkType?.defName ?? string.Empty);
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkGiver?.defName ?? string.Empty);
            }
        }

        internal static WorkTabSpecificJobTarget For(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver)
        {
            return new WorkTabSpecificJobTarget(pawn, workType, workGiver);
        }
    }

    /// <summary>Runtime identity for one pawn or shared WorkType order.</summary>
    internal readonly struct WorkTabWorkTypeOrderTarget : IEquatable<WorkTabWorkTypeOrderTarget>
    {
        internal WorkTabWorkTypeOrderTarget(Pawn pawn, WorkTypeDef workType)
        {
            Pawn = pawn;
            WorkType = workType;
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal bool IsGlobal => Pawn == null;
        internal bool IsValid =>
            WorkType != null && (IsGlobal || Pawn.thingIDNumber > 0);

        public bool Equals(WorkTabWorkTypeOrderTarget other)
        {
            return ReferenceEquals(Pawn, other.Pawn) &&
                   ReferenceEquals(WorkType, other.WorkType);
        }

        public override bool Equals(object obj)
        {
            return obj is WorkTabWorkTypeOrderTarget &&
                   Equals((WorkTabWorkTypeOrderTarget)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Pawn?.thingIDNumber ?? -1) * 397) ^
                       StringComparer.Ordinal.GetHashCode(WorkType?.defName ?? string.Empty);
            }
        }

        internal static WorkTabWorkTypeOrderTarget For(
            Pawn pawn,
            WorkTypeDef workType)
        {
            return new WorkTabWorkTypeOrderTarget(pawn, workType);
        }
    }

    /// <summary>
    /// Neutral read-side preview capabilities, separate from the membership
    /// and history command port.
    /// </summary>
    internal interface IWorkTabPreviewStateReader
    {
        WorkTabEffectiveStateResolution<TimePriorityScheduleValue> ResolveSchedule(
            TimePriorityTarget target);

        WorkTabEffectiveStateResolution<TimePriorityScheduleValue>
            ResolvePreviewScheduleIntent(TimePriorityTarget target);

        WorkTabEffectiveStateResolution<int> ResolveSpecificJobPriority(
            WorkTabSpecificJobTarget target);

        WorkTabEffectiveStateResolution<int>
            ResolvePreviewSpecificJobPriorityIntent(WorkTabSpecificJobTarget target);

        WorkTabEffectiveStateResolution<IReadOnlyList<string>> ResolveWorkTypeOrder(
            WorkTabWorkTypeOrderTarget target);

        WorkTabEffectiveStateResolution<IReadOnlyList<string>>
            ResolvePreviewWorkTypeOrderIntent(WorkTabWorkTypeOrderTarget target);
    }

    /// <summary>
    /// Neutral preview mutations. Resolution values preserve NoOpinion, Set,
    /// and Clear instead of collapsing an absent value into a reset.
    /// </summary>
    internal interface IWorkTabPreviewStateEditor
    {
        WorkTabEffectiveStateMutationResult SetScheduleIntent(
            TimePriorityTarget target,
            WorkTabEffectiveStateResolution<TimePriorityScheduleValue> intent);

        WorkTabEffectiveStateMutationResult SetSpecificJobPriority(
            WorkTabSpecificJobTarget target,
            int priority);

        WorkTabEffectiveStateMutationResult SetSpecificJobPriorityIntent(
            WorkTabSpecificJobTarget target,
            WorkTabEffectiveStateResolution<int> intent);

        WorkTabEffectiveStateMutationResult ClearSpecificJobPriority(
            WorkTabSpecificJobTarget target);

        WorkTabEffectiveStateMutationResult SetWorkTypeOrder(
            WorkTabWorkTypeOrderTarget target,
            IReadOnlyList<string> orderedWorkGiverNames);

        WorkTabEffectiveStateMutationResult SetWorkTypeOrderIntent(
            WorkTabWorkTypeOrderTarget target,
            WorkTabEffectiveStateResolution<IReadOnlyList<string>> intent);

        WorkTabEffectiveStateMutationResult ClearWorkTypeOrder(
            WorkTabWorkTypeOrderTarget target);
    }
}
