using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Better_Work_Tab.Features.Workloads.V2
{
    public enum WorkloadScopeMode
    {
        ExplicitPawnIds = 0,
        CurrentMapFreeColonists = 1
    }

    public sealed class WorkloadScope
    {
        private readonly ReadOnlyCollection<PawnKey> _explicitPawnIds;
        private readonly ReadOnlyCollection<PawnKey> _excludedPawnIds;

        public WorkloadScope(
            WorkloadScopeMode mode,
            IEnumerable<PawnKey> explicitPawnIds = null,
            IEnumerable<PawnKey> excludedPawnIds = null)
        {
            Mode = mode;
            _explicitPawnIds = NormalizeKeys(explicitPawnIds);
            _excludedPawnIds = NormalizeKeys(excludedPawnIds);
        }

        public static WorkloadScope Empty
        {
            get { return new WorkloadScope(WorkloadScopeMode.ExplicitPawnIds); }
        }

        public static WorkloadScope Explicit(IEnumerable<string> pawnIds, IEnumerable<string> excludedPawnIds = null)
        {
            return new WorkloadScope(
                WorkloadScopeMode.ExplicitPawnIds,
                ToPawnKeys(pawnIds),
                ToPawnKeys(excludedPawnIds));
        }

        public static WorkloadScope CurrentMapFreeColonists(IEnumerable<string> excludedPawnIds = null)
        {
            return new WorkloadScope(
                WorkloadScopeMode.CurrentMapFreeColonists,
                null,
                ToPawnKeys(excludedPawnIds));
        }

        public WorkloadScopeMode Mode { get; private set; }
        public IReadOnlyList<PawnKey> ExplicitPawnIds => _explicitPawnIds;
        public IReadOnlyList<PawnKey> ExcludedPawnIds => _excludedPawnIds;
        public bool IsValidMode => Mode == WorkloadScopeMode.ExplicitPawnIds || Mode == WorkloadScopeMode.CurrentMapFreeColonists;

        public bool IsExplicitlyExcluded(PawnKey pawnId)
        {
            PawnKey safePawnId = pawnId ?? new PawnKey(null);
            for (int i = 0; i < _excludedPawnIds.Count; i++)
            {
                if (_excludedPawnIds[i].Equals(safePawnId)) return true;
            }

            return false;
        }

        public bool IsInScope(PawnScopeCandidate candidate)
        {
            if (candidate == null || IsExplicitlyExcluded(candidate.PawnId)) return false;

            if (Mode == WorkloadScopeMode.ExplicitPawnIds)
            {
                return Contains(_explicitPawnIds, candidate.PawnId);
            }

            if (Mode == WorkloadScopeMode.CurrentMapFreeColonists)
            {
                return candidate.IsCurrentMap && candidate.IsColonist && candidate.IsFreeColonist;
            }

            return false;
        }

        public string CanonicalForm
        {
            get
            {
                var builder = new StringBuilder();
                builder.Append((int)Mode).Append('|').Append(_explicitPawnIds.Count).Append('|');
                AppendKeys(builder, _explicitPawnIds);
                builder.Append('|').Append(_excludedPawnIds.Count).Append('|');
                AppendKeys(builder, _excludedPawnIds);
                return builder.ToString();
            }
        }

        private static bool Contains(IReadOnlyList<PawnKey> keys, PawnKey candidate)
        {
            PawnKey safeCandidate = candidate ?? new PawnKey(null);
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Equals(safeCandidate)) return true;
            }

            return false;
        }

        private static ReadOnlyCollection<PawnKey> NormalizeKeys(IEnumerable<PawnKey> source)
        {
            var unique = new Dictionary<PawnKey, PawnKey>();
            if (source != null)
            {
                foreach (PawnKey key in source)
                {
                    PawnKey safeKey = key ?? new PawnKey(null);
                    unique[safeKey] = safeKey;
                }
            }

            var result = new List<PawnKey>(unique.Values);
            result.Sort((left, right) => left.CompareTo(right));
            return result.AsReadOnly();
        }

        private static IEnumerable<PawnKey> ToPawnKeys(IEnumerable<string> values)
        {
            if (values == null) return null;

            var result = new List<PawnKey>();
            foreach (string value in values)
            {
                result.Add(new PawnKey(value));
            }

            return result;
        }

        private static void AppendKeys(StringBuilder builder, IReadOnlyList<PawnKey> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                builder.Append(WorkloadCanonical.Encode(keys[i].Value)).Append(';');
            }
        }
    }

    public sealed class PawnScopeCandidate
    {
        public PawnScopeCandidate(
            PawnKey pawnId,
            bool isCurrentMap,
            bool isColonist,
            bool isFreeColonist)
        {
            PawnId = pawnId ?? new PawnKey(null);
            IsCurrentMap = isCurrentMap;
            IsColonist = isColonist;
            IsFreeColonist = isFreeColonist;
        }

        public PawnScopeCandidate(
            string pawnId,
            bool isCurrentMap,
            bool isColonist,
            bool isFreeColonist)
            : this(new PawnKey(pawnId), isCurrentMap, isColonist, isFreeColonist)
        {
        }

        public PawnKey PawnId { get; private set; }
        public bool IsCurrentMap { get; private set; }
        public bool IsColonist { get; private set; }
        public bool IsFreeColonist { get; private set; }

        public string CanonicalForm
        {
            get
            {
                return WorkloadCanonical.Encode(PawnId.Value)
                    + WorkloadCanonical.Boolean(IsCurrentMap)
                    + WorkloadCanonical.Boolean(IsColonist)
                    + WorkloadCanonical.Boolean(IsFreeColonist);
            }
        }
    }
}
