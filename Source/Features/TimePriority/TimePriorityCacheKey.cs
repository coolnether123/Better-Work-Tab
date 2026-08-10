using System;

namespace Better_Work_Tab.Features.TimePriority
{
    /// <summary>
    /// Runtime-only schedule identity. The persisted string key remains a save/UI contract;
    /// lookups on the AI path use this value key so they do not concatenate strings.
    /// </summary>
    internal readonly struct TimePriorityCacheKey : IEquatable<TimePriorityCacheKey>
    {
        internal TimePriorityCacheKey(
            int pawnId,
            TimePriorityTargetKind kind,
            string workTypeDefName,
            string targetDefName)
        {
            PawnId = pawnId;
            Kind = kind;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            TargetDefName = targetDefName ?? string.Empty;
        }

        internal int PawnId { get; }
        internal TimePriorityTargetKind Kind { get; }
        internal string WorkTypeDefName { get; }
        internal string TargetDefName { get; }

        public bool Equals(TimePriorityCacheKey other)
        {
            return PawnId == other.PawnId &&
                   Kind == other.Kind &&
                   string.Equals(WorkTypeDefName, other.WorkTypeDefName, StringComparison.Ordinal) &&
                   string.Equals(TargetDefName, other.TargetDefName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TimePriorityCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PawnId;
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkTypeDefName);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(TargetDefName);
                return hash;
            }
        }
    }
}
