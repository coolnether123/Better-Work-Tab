using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal readonly struct WorkTabRuleSpecificPriority
    {
        internal WorkTabRuleSpecificPriority(
            Pawn pawn,
            WorkGiverDef workGiver,
            SpecificJobPriorityStorageKind storageKind,
            bool hadOverride,
            int initial,
            int desired,
            bool clear,
            bool clampDesired = true)
        {
            Pawn = pawn;
            WorkGiver = workGiver;
            StorageKind = storageKind;
            HadOverride = hadOverride;
            Initial = initial;
            Desired = clampDesired ? WorkPrioritySystem.ClampPriority(desired) : desired;
            Clear = clear;
        }

        internal Pawn Pawn { get; }
        internal WorkGiverDef WorkGiver { get; }
        internal SpecificJobPriorityStorageKind StorageKind { get; }
        internal bool HadOverride { get; }
        internal int Initial { get; }
        internal int Desired { get; }
        internal bool Clear { get; }
        internal WorkTabRuleSpecificPriority WithDesired(
            int desired,
            bool clear,
            bool clampDesired = true) =>
            new WorkTabRuleSpecificPriority(
                Pawn,
                WorkGiver,
                StorageKind,
                HadOverride,
                Initial,
                desired,
                clear,
                clampDesired);
    }
}
