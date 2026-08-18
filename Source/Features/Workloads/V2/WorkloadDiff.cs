using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Better_Work_Tab.Features.Workloads.V2
{
    public enum WorkloadChangeKind
    {
        Added = 0,
        Removed = 1,
        Changed = 2
    }

    public sealed class WorkloadChange
    {
        public WorkloadChange(
            WorkloadStateDimension dimension,
            WorkloadChangeKind kind,
            string canonicalKey,
            string beforeValue,
            string afterValue)
        {
            Dimension = dimension;
            Kind = kind;
            CanonicalKey = canonicalKey ?? string.Empty;
            BeforeValue = beforeValue;
            AfterValue = afterValue;
        }

        public WorkloadStateDimension Dimension { get; private set; }
        public WorkloadChangeKind Kind { get; private set; }
        public string CanonicalKey { get; private set; }
        public string BeforeValue { get; private set; }
        public string AfterValue { get; private set; }
    }

    public sealed class WorkloadSemanticDiff
    {
        private readonly ReadOnlyCollection<WorkloadChange> _changes;

        private WorkloadSemanticDiff(
            string beforeCanonical,
            string afterCanonical,
            string beforeFingerprint,
            string afterFingerprint,
            IEnumerable<WorkloadChange> changes)
        {
            BeforeCanonical = beforeCanonical ?? string.Empty;
            AfterCanonical = afterCanonical ?? string.Empty;
            BeforeFingerprint = beforeFingerprint ?? string.Empty;
            AfterFingerprint = afterFingerprint ?? string.Empty;
            _changes = new List<WorkloadChange>(changes ?? new WorkloadChange[0]).AsReadOnly();
        }

        public IReadOnlyList<WorkloadChange> Changes => _changes;
        public string BeforeCanonical { get; private set; }
        public string AfterCanonical { get; private set; }
        public string BeforeFingerprint { get; private set; }
        public string AfterFingerprint { get; private set; }
        public bool IsEmpty => _changes.Count == 0;
        public bool SemanticallyEqual => StringComparer.Ordinal.Equals(BeforeCanonical, AfterCanonical);

        public static WorkloadSemanticDiff Between(
            WorkloadProjectedState before,
            WorkloadProjectedState after,
            WorkloadOwnershipDimensions dimensions = WorkloadOwnershipDimensions.All)
        {
            WorkloadProjectedState safeBefore = before ?? WorkloadProjectedState.Empty;
            WorkloadProjectedState safeAfter = after ?? WorkloadProjectedState.Empty;
            string beforeCanonical = safeBefore.GetCanonicalForm(dimensions);
            string afterCanonical = safeAfter.GetCanonicalForm(dimensions);
            var beforeValues = BuildValues(safeBefore, dimensions);
            var afterValues = BuildValues(safeAfter, dimensions);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in beforeValues.Keys) keys.Add(key);
            foreach (string key in afterValues.Keys) keys.Add(key);

            var changes = new List<WorkloadChange>();
            foreach (string compositeKey in keys)
            {
                string beforeValue;
                string afterValue;
                bool hasBefore = beforeValues.TryGetValue(compositeKey, out beforeValue);
                bool hasAfter = afterValues.TryGetValue(compositeKey, out afterValue);
                if (hasBefore && hasAfter && StringComparer.Ordinal.Equals(beforeValue, afterValue)) continue;

                WorkloadStateDimension dimension = GetDimension(compositeKey);
                string key = GetKey(compositeKey);
                WorkloadChangeKind kind = !hasBefore
                    ? WorkloadChangeKind.Added
                    : !hasAfter
                        ? WorkloadChangeKind.Removed
                        : WorkloadChangeKind.Changed;
                changes.Add(new WorkloadChange(dimension, kind, key, hasBefore ? beforeValue : null, hasAfter ? afterValue : null));
            }

            changes.Sort(CompareChanges);
            return new WorkloadSemanticDiff(
                beforeCanonical,
                afterCanonical,
                WorkloadCanonical.Fingerprint(beforeCanonical),
                WorkloadCanonical.Fingerprint(afterCanonical),
                changes);
        }

        private static Dictionary<string, string> BuildValues(
            WorkloadProjectedState state,
            WorkloadOwnershipDimensions dimensions)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < state.RepresentedPawnIds.Count; i++)
            {
                values[ComposeKey(
                    WorkloadStateDimension.Membership,
                    "I:" + WorkloadCanonical.Encode(state.RepresentedPawnIds[i].Value))] = "1";
            }

            for (int i = 0; i < state.ExcludedPawnIds.Count; i++)
            {
                values[ComposeKey(
                    WorkloadStateDimension.Membership,
                    "E:" + WorkloadCanonical.Encode(state.ExcludedPawnIds[i].Value))] = "1";
            }

            if ((dimensions & WorkloadOwnershipDimensions.ParentPriorities) != 0)
            {
                for (int i = 0; i < state.ParentPriorities.Count; i++)
                {
                    WorkloadParentPriorityEntry entry = state.ParentPriorities[i];
                    values[ComposeKey(WorkloadStateDimension.ParentPriorities, entry.Key.CanonicalKey)] =
                        WorkloadCanonical.Integer(entry.Priority);
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.ManualModes) != 0)
            {
                for (int i = 0; i < state.ManualModes.Count; i++)
                {
                    WorkloadManualModeEntry entry = state.ManualModes[i];
                    values[ComposeKey(WorkloadStateDimension.ManualModes, entry.Key.CanonicalKey)] =
                        WorkloadCanonical.Boolean(entry.Manual);
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.Schedules) != 0)
            {
                for (int i = 0; i < state.Schedules.Count; i++)
                {
                    WorkloadScheduleEntry entry = state.Schedules[i];
                    values[ComposeKey(WorkloadStateDimension.Schedules, WorkloadCanonical.Encode(entry.Pawn.Value))] =
                        WorkloadCanonical.Integer(entry.Schedule.Value);
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOverrides) != 0)
            {
                for (int i = 0; i < state.SpecificJobOverrides.Count; i++)
                {
                    WorkloadSpecificJobOverrideEntry entry = state.SpecificJobOverrides[i];
                    values[ComposeKey(WorkloadStateDimension.SpecificJobOverrides, entry.Key.CanonicalKey)] =
                        entry.Value.CanonicalValue;
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOrder) != 0)
            {
                for (int i = 0; i < state.SpecificJobOrder.Count; i++)
                {
                    WorkloadSpecificJobOrderEntry entry = state.SpecificJobOrder[i];
                    values[ComposeKey(WorkloadStateDimension.SpecificJobOrder, entry.Key.CanonicalKey)] =
                        WorkloadCanonical.Integer(entry.Order);
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.PresentationSettings) != 0)
            {
                for (int i = 0; i < state.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry = state.PresentationSettings[i];
                    values[ComposeKey(WorkloadStateDimension.PresentationSettings, WorkloadCanonical.Encode(entry.Key))] =
                        entry.Value.CanonicalValue;
                }
            }

            return values;
        }

        private static string ComposeKey(WorkloadStateDimension dimension, string key)
        {
            return ((int)dimension).ToString() + "|" + (key ?? string.Empty);
        }

        private static WorkloadStateDimension GetDimension(string compositeKey)
        {
            int separator = compositeKey.IndexOf('|');
            if (separator < 0) return WorkloadStateDimension.ParentPriorities;
            int value;
            return int.TryParse(compositeKey.Substring(0, separator), out value)
                && value >= 0
                && value <= (int)WorkloadStateDimension.Membership
                ? (WorkloadStateDimension)value
                : WorkloadStateDimension.ParentPriorities;
        }

        private static string GetKey(string compositeKey)
        {
            int separator = compositeKey.IndexOf('|');
            return separator < 0 ? compositeKey : compositeKey.Substring(separator + 1);
        }

        private static int CompareChanges(WorkloadChange left, WorkloadChange right)
        {
            int dimensionComparison = left.Dimension.CompareTo(right.Dimension);
            if (dimensionComparison != 0) return dimensionComparison;
            int keyComparison = StringComparer.Ordinal.Compare(left.CanonicalKey, right.CanonicalKey);
            if (keyComparison != 0) return keyComparison;
            return left.Kind.CompareTo(right.Kind);
        }
    }
}
