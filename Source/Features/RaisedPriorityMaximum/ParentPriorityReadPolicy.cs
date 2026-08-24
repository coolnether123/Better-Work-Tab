using System;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    // Stable across map loads and deliberately independent of Workloads.
    internal readonly struct ParentPriorityTarget : IEquatable<ParentPriorityTarget>
    {
        internal readonly int PawnThingId;
        internal readonly string WorkTypeDefName;

        internal ParentPriorityTarget(int pawnThingId, string workTypeDefName)
        {
            PawnThingId = pawnThingId;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
        }

        internal bool IsValid => PawnThingId > 0 && WorkTypeDefName.Length != 0;
        public bool Equals(ParentPriorityTarget other) =>
            PawnThingId == other.PawnThingId &&
            StringComparer.Ordinal.Equals(WorkTypeDefName, other.WorkTypeDefName);
        public override bool Equals(object obj) => obj is ParentPriorityTarget other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                return PawnThingId * 397 ^ StringComparer.Ordinal.GetHashCode(WorkTypeDefName);
            }
        }
    }

    internal enum ParentPriorityOverlayState { NoOpinion, Set, Clear }

    internal readonly struct ParentProjectionValue<T>
    {
        internal readonly ParentPriorityOverlayState State;
        internal readonly T Value;
        internal ParentProjectionValue(ParentPriorityOverlayState state, T value)
        {
            State = state;
            Value = value;
        }

        internal bool IsSet => State == ParentPriorityOverlayState.Set;
        internal static ParentProjectionValue<T> Clear =>
            new ParentProjectionValue<T>(ParentPriorityOverlayState.Clear, default(T));
        internal static ParentProjectionValue<T> Set(T value) =>
            new ParentProjectionValue<T>(ParentPriorityOverlayState.Set, value);
    }

    internal static class ParentPriorityReadPolicy
    {
        internal static int NormalizeForDisplay(bool humanlike, int priority, bool? manualMode)
        {
            if (humanlike && priority > PriorityConstants.Disabled && manualMode == false)
                return PriorityConstants.VanillaDefaultEnabled;
            return priority < PriorityConstants.Disabled ? PriorityConstants.Disabled :
                priority > PriorityConstants.ExtendedHardMax ? PriorityConstants.ExtendedHardMax : priority;
        }

        internal static int Resolve(
            bool external,
            ParentProjectionValue<int> overlay,
            int storedPriority,
            bool hasPinnedSchedule,
            int pinnedSchedulePriority,
            int externalEffectivePriority)
        {
            if (external)
            {
                return overlay.IsSet ? overlay.Value : externalEffectivePriority;
            }

            int basePriority = overlay.IsSet ? overlay.Value : storedPriority;
            return basePriority > 0 && hasPinnedSchedule ? pinnedSchedulePriority : basePriority;
        }
    }
}
