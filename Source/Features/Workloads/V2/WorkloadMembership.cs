using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Better_Work_Tab.Features.Workloads.V2
{
    public enum WorkloadMembershipClassification
    {
        Included = 0,
        UnchangedOutsideScope = 1,
        UnrepresentedNew = 2,
        ExplicitlyExcluded = 3,
        StaleMissing = 4
    }

    public sealed class WorkloadMembershipRecord
    {
        public WorkloadMembershipRecord(
            PawnKey pawnId,
            WorkloadMembershipClassification classification,
            bool isRepresented,
            bool isAvailable)
        {
            PawnId = pawnId ?? new PawnKey(null);
            Classification = classification;
            IsRepresented = isRepresented;
            IsAvailable = isAvailable;
        }

        public PawnKey PawnId { get; private set; }
        public WorkloadMembershipClassification Classification { get; private set; }
        public bool IsRepresented { get; private set; }
        public bool IsAvailable { get; private set; }
    }

    public sealed class WorkloadMembershipResult
    {
        private readonly ReadOnlyCollection<WorkloadMembershipRecord> _records;

        internal WorkloadMembershipResult(IEnumerable<WorkloadMembershipRecord> records)
        {
            var ordered = new List<WorkloadMembershipRecord>(records ?? new WorkloadMembershipRecord[0]);
            ordered.Sort((left, right) => left.PawnId.CompareTo(right.PawnId));
            _records = ordered.AsReadOnly();
        }

        public IReadOnlyList<WorkloadMembershipRecord> Records => _records;

        public int GetCount(WorkloadMembershipClassification classification)
        {
            int count = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Classification == classification) count++;
            }

            return count;
        }

        public WorkloadMembershipRecord Find(PawnKey pawnId)
        {
            PawnKey safePawnId = pawnId ?? new PawnKey(null);
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].PawnId.Equals(safePawnId)) return _records[i];
            }

            return null;
        }
    }

    /// <summary>
    /// Immutable, session-local membership projection. The controller owns
    /// when a snapshot is replaced; consumers can read records, counts, and
    /// row classifications without asking the classifier to rebuild its
    /// dictionaries for each label, popover, or render pass.
    /// </summary>
    public sealed class WorkloadMembershipSnapshot
    {
        private readonly WorkloadMembershipResult _result;
        private readonly Dictionary<PawnKey, WorkloadMembershipClassification> _classifications =
            new Dictionary<PawnKey, WorkloadMembershipClassification>();
        private readonly int[] _counts = new int[5];

        public WorkloadMembershipSnapshot(WorkloadMembershipResult result)
        {
            _result = result ?? WorkloadMembershipClassifier.EmptyResult;
            IReadOnlyList<WorkloadMembershipRecord> records = _result.Records;
            for (int i = 0; i < records.Count; i++)
            {
                WorkloadMembershipRecord record = records[i];
                if (record == null || record.PawnId == null || !record.PawnId.IsValid)
                {
                    continue;
                }

                _classifications[record.PawnId] = record.Classification;
                int classification = (int)record.Classification;
                if (classification >= 0 && classification < _counts.Length)
                {
                    _counts[classification]++;
                }
            }
        }

        public IReadOnlyList<WorkloadMembershipRecord> Records => _result.Records;

        public int GetCount(WorkloadMembershipClassification classification)
        {
            int index = (int)classification;
            return index >= 0 && index < _counts.Length ? _counts[index] : 0;
        }

        public WorkloadMembershipRecord Find(PawnKey pawnId)
        {
            return _result.Find(pawnId);
        }

        public bool TryGetClassification(
            PawnKey pawnId,
            out WorkloadMembershipClassification classification)
        {
            classification = default(WorkloadMembershipClassification);
            return pawnId != null && _classifications.TryGetValue(pawnId, out classification);
        }
    }

    public static class WorkloadMembershipClassifier
    {
        internal static WorkloadMembershipResult EmptyResult =>
            new WorkloadMembershipResult(new WorkloadMembershipRecord[0]);

        public static WorkloadMembershipResult Classify(
            WorkloadTemplate template,
            IEnumerable<PawnScopeCandidate> availablePawns)
        {
            WorkloadTemplate safeTemplate = template ?? WorkloadTemplate.Empty;
            WorkloadScope scope = safeTemplate.Definition.Scope ?? WorkloadScope.Empty;
            WorkloadProjectedState state = safeTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            var candidates = new Dictionary<PawnKey, PawnScopeCandidate>();
            if (availablePawns != null)
            {
                foreach (PawnScopeCandidate candidate in availablePawns)
                {
                    if (candidate == null) continue;
                    PawnKey key = candidate.PawnId ?? new PawnKey(null);
                    PawnScopeCandidate existing;
                    if (!candidates.TryGetValue(key, out existing)
                        || StringComparer.Ordinal.Compare(candidate.CanonicalForm, existing.CanonicalForm) < 0)
                    {
                        candidates[key] = candidate;
                    }
                }
            }

            var represented = new HashSet<PawnKey>();
            for (int i = 0; i < state.RepresentedPawnIds.Count; i++) represented.Add(state.RepresentedPawnIds[i]);
            var projectedExcluded = new HashSet<PawnKey>();
            for (int i = 0; i < state.ExcludedPawnIds.Count; i++)
            {
                projectedExcluded.Add(state.ExcludedPawnIds[i]);
                represented.Remove(state.ExcludedPawnIds[i]);
            }

            var universe = new Dictionary<PawnKey, PawnKey>();
            foreach (KeyValuePair<PawnKey, PawnScopeCandidate> item in candidates) universe[item.Key] = item.Key;
            foreach (PawnKey key in state.RepresentedPawnIds) universe[key] = key;
            foreach (PawnKey key in state.ExcludedPawnIds) universe[key] = key;
            foreach (PawnKey key in scope.ExplicitPawnIds) universe[key] = key;
            foreach (PawnKey key in scope.ExcludedPawnIds) universe[key] = key;

            var keys = new List<PawnKey>(universe.Values);
            keys.Sort((left, right) => left.CompareTo(right));
            var records = new List<WorkloadMembershipRecord>();
            for (int i = 0; i < keys.Count; i++)
            {
                PawnKey key = keys[i];
                PawnScopeCandidate candidate;
                bool isAvailable = candidates.TryGetValue(key, out candidate);
                bool isRepresented = represented.Contains(key);
                WorkloadMembershipClassification classification;

                if (projectedExcluded.Contains(key) || scope.IsExplicitlyExcluded(key))
                {
                    classification = WorkloadMembershipClassification.ExplicitlyExcluded;
                }
                else if (!isAvailable)
                {
                    if (isRepresented || Contains(scope.ExplicitPawnIds, key))
                    {
                        classification = WorkloadMembershipClassification.StaleMissing;
                    }
                    else
                    {
                        classification = WorkloadMembershipClassification.ExplicitlyExcluded;
                    }
                }
                else if (!scope.IsInScope(candidate))
                {
                    classification = WorkloadMembershipClassification.UnchangedOutsideScope;
                }
                else if (isRepresented)
                {
                    classification = WorkloadMembershipClassification.Included;
                }
                else
                {
                    classification = WorkloadMembershipClassification.UnrepresentedNew;
                }

                records.Add(new WorkloadMembershipRecord(key, classification, isRepresented, isAvailable));
            }

            return new WorkloadMembershipResult(records);
        }

        private static bool Contains(IReadOnlyList<PawnKey> keys, PawnKey candidate)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Equals(candidate)) return true;
            }

            return false;
        }
    }
}
