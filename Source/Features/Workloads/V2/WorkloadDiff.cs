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

    internal sealed class WorkloadChangeInspectionTarget
    {
        internal WorkloadChangeInspectionTarget(
            PawnKey pawn = null,
            WorkTypeKey workType = null,
            WorkGiverKey workGiver = null,
            WorkloadTargetScope scope = WorkloadTargetScope.PawnLocal,
            WorkloadScheduleTargetKind scheduleKind = WorkloadScheduleTargetKind.ParentWorkType)
        {
            Pawn = pawn;
            WorkType = workType;
            WorkGiver = workGiver;
            Scope = scope;
            ScheduleKind = scheduleKind;
        }

        public PawnKey Pawn { get; private set; }
        public WorkTypeKey WorkType { get; private set; }
        public WorkGiverKey WorkGiver { get; private set; }
        public WorkloadTargetScope Scope { get; private set; }
        public WorkloadScheduleTargetKind ScheduleKind { get; private set; }
    }

    public sealed class WorkloadChange
    {
        public WorkloadChange(
            WorkloadStateDimension dimension,
            WorkloadChangeKind kind,
            string canonicalKey,
            string beforeValue,
            string afterValue)
            : this(dimension, kind, canonicalKey, beforeValue, afterValue, null)
        {
        }

        internal WorkloadChange(
            WorkloadStateDimension dimension,
            WorkloadChangeKind kind,
            string canonicalKey,
            string beforeValue,
            string afterValue,
            WorkloadChangeInspectionTarget inspectionTarget)
        {
            Dimension = dimension;
            Kind = kind;
            CanonicalKey = canonicalKey ?? string.Empty;
            BeforeValue = beforeValue;
            AfterValue = afterValue;
            InspectionTarget = inspectionTarget;
        }

        public WorkloadStateDimension Dimension { get; private set; }
        public WorkloadChangeKind Kind { get; private set; }
        public string CanonicalKey { get; private set; }
        public string BeforeValue { get; private set; }
        public string AfterValue { get; private set; }
        internal WorkloadChangeInspectionTarget InspectionTarget { get; private set; }
    }

    public sealed class WorkloadSemanticDiff
    {
        private readonly ReadOnlyCollection<WorkloadChange> _changes;

        private readonly struct DiffValue
        {
            public DiffValue(string value, WorkloadChangeInspectionTarget inspectionTarget)
            {
                Value = value;
                InspectionTarget = inspectionTarget;
            }

            public string Value { get; }
            public WorkloadChangeInspectionTarget InspectionTarget { get; }
        }

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
                DiffValue beforeEntry;
                DiffValue afterEntry;
                bool hasBefore = beforeValues.TryGetValue(compositeKey, out beforeEntry);
                bool hasAfter = afterValues.TryGetValue(compositeKey, out afterEntry);
                string beforeValue = hasBefore ? beforeEntry.Value : null;
                string afterValue = hasAfter ? afterEntry.Value : null;
                if (hasBefore && hasAfter && StringComparer.Ordinal.Equals(beforeValue, afterValue)) continue;

                WorkloadStateDimension dimension = GetDimension(compositeKey);
                string key = GetKey(compositeKey);
                WorkloadChangeKind kind = !hasBefore
                    ? WorkloadChangeKind.Added
                    : !hasAfter
                        ? WorkloadChangeKind.Removed
                        : WorkloadChangeKind.Changed;
                changes.Add(new WorkloadChange(
                    dimension,
                    kind,
                    key,
                    beforeValue,
                    afterValue,
                    beforeEntry.InspectionTarget ?? afterEntry.InspectionTarget));
            }

            changes.Sort(CompareChanges);
            return new WorkloadSemanticDiff(
                beforeCanonical,
                afterCanonical,
                WorkloadCanonical.Fingerprint(beforeCanonical),
                WorkloadCanonical.Fingerprint(afterCanonical),
                changes);
        }

        private static Dictionary<string, DiffValue> BuildValues(
            WorkloadProjectedState state,
            WorkloadOwnershipDimensions dimensions)
        {
            var values = new Dictionary<string, DiffValue>(StringComparer.Ordinal);
            for (int i = 0; i < state.RepresentedPawnIds.Count; i++)
            {
                SetValue(values,
                    WorkloadStateDimension.Membership,
                    "I:" + WorkloadCanonical.Encode(state.RepresentedPawnIds[i].Value),
                    "1",
                    new WorkloadChangeInspectionTarget(
                        new PawnKey(state.RepresentedPawnIds[i].Value)));
            }

            for (int i = 0; i < state.ExcludedPawnIds.Count; i++)
            {
                SetValue(values,
                    WorkloadStateDimension.Membership,
                    "E:" + WorkloadCanonical.Encode(state.ExcludedPawnIds[i].Value),
                    "1",
                    new WorkloadChangeInspectionTarget(
                        new PawnKey(state.ExcludedPawnIds[i].Value)));
            }

            if ((dimensions & WorkloadOwnershipDimensions.ParentPriorities) != 0)
            {
                for (int i = 0; i < state.ParentPriorities.Count; i++)
                {
                    WorkloadParentPriorityEntry entry = state.ParentPriorities[i];
                    SetValue(
                        values,
                        WorkloadStateDimension.ParentPriorities,
                        entry.Key.CanonicalKey,
                        WorkloadCanonical.Integer(entry.Priority),
                        new WorkloadChangeInspectionTarget(entry.Key.Pawn, entry.Key.WorkType));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.ManualModes) != 0)
            {
                for (int i = 0; i < state.ManualModes.Count; i++)
                {
                    WorkloadManualModeEntry entry = state.ManualModes[i];
                    SetValue(
                        values,
                        WorkloadStateDimension.ManualModes,
                        entry.Key.CanonicalKey,
                        WorkloadCanonical.Boolean(entry.Manual),
                        new WorkloadChangeInspectionTarget(entry.Key.Pawn, entry.Key.WorkType));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.Schedules) != 0)
            {
                for (int i = 0; i < state.Schedules.Count; i++)
                {
                    WorkloadScheduleEntry entry = state.Schedules[i];
                    SetValue(
                        values,
                        WorkloadStateDimension.Schedules,
                        WorkloadCanonical.Encode(entry.Pawn.Value),
                        WorkloadCanonical.Integer(entry.Schedule.Value),
                        new WorkloadChangeInspectionTarget(pawn: entry.Pawn));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOverrides) != 0)
            {
                for (int i = 0; i < state.SpecificJobOverrides.Count; i++)
                {
                    WorkloadSpecificJobOverrideEntry entry = state.SpecificJobOverrides[i];
                    SetValue(
                        values,
                        WorkloadStateDimension.SpecificJobOverrides,
                        entry.Key.CanonicalKey,
                        entry.Value.CanonicalValue,
                        new WorkloadChangeInspectionTarget(
                            entry.Key.IsGlobal ? null : entry.Key.Pawn,
                            entry.Key.WorkType,
                            entry.Key.WorkGiver,
                            entry.Key.Scope));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOrder) != 0)
            {
                for (int i = 0; i < state.SpecificJobOrder.Count; i++)
                {
                    WorkloadSpecificJobOrderEntry entry = state.SpecificJobOrder[i];
                    SetValue(
                        values,
                        WorkloadStateDimension.SpecificJobOrder,
                        entry.Key.CanonicalKey,
                        WorkloadCanonical.Integer(entry.Order),
                        new WorkloadChangeInspectionTarget(
                            entry.Key.IsGlobal ? null : entry.Key.Pawn,
                            entry.Key.WorkType,
                            entry.Key.WorkGiver,
                            entry.Key.Scope));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.PresentationSettings) != 0)
            {
                for (int i = 0; i < state.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry = state.PresentationSettings[i];
                    SetValue(
                        values,
                        WorkloadStateDimension.PresentationSettings,
                        WorkloadCanonical.Encode(entry.Key),
                        entry.Value.CanonicalValue);
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.ParentPriorities) != 0)
            {
                for (int i = 0; i < state.ParentPriorityIntents.Count; i++)
                {
                    WorkloadParentPriorityIntentEntry entry = state.ParentPriorityIntents[i];
                    SetValue(values,
                        WorkloadStateDimension.ParentPriorities,
                        "intent:" + entry.Key.CanonicalKey,
                        entry.Intent.CanonicalForm,
                        new WorkloadChangeInspectionTarget(entry.Key.Pawn, entry.Key.WorkType));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.ManualModes) != 0)
            {
                for (int i = 0; i < state.ManualModeIntents.Count; i++)
                {
                    WorkloadManualModeIntentEntry entry = state.ManualModeIntents[i];
                    SetValue(values,
                        WorkloadStateDimension.ManualModes,
                        "intent:" + entry.Key.CanonicalKey,
                        entry.Intent.CanonicalForm,
                        new WorkloadChangeInspectionTarget(entry.Key.Pawn, entry.Key.WorkType));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.Schedules) != 0)
            {
                for (int i = 0; i < state.ScheduleIntents.Count; i++)
                {
                    WorkloadScheduleIntentEntry entry = state.ScheduleIntents[i];
                    SetValue(values,
                        WorkloadStateDimension.Schedules,
                        "intent:" + entry.Key.CanonicalKey,
                        entry.Intent.CanonicalForm,
                        new WorkloadChangeInspectionTarget(
                            entry.Key.IsGlobal ? null : entry.Key.Pawn,
                            entry.Key.WorkType,
                            entry.Key.WorkGiver,
                            entry.Key.Scope,
                            entry.Key.TargetKind));
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOverrides) != 0)
            {
                for (int i = 0; i < state.SpecificPriorityIntents.Count; i++)
                {
                    WorkloadSpecificPriorityIntentEntry entry = state.SpecificPriorityIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    SetValue(values,
                        WorkloadStateDimension.SpecificJobOverrides,
                        "intent:" + entry.Key.CanonicalKey,
                        entry.Intent.CanonicalForm,
                        new WorkloadChangeInspectionTarget(
                            entry.Key.IsGlobal ? null : entry.Key.Pawn,
                            entry.Key.WorkType,
                            entry.Key.WorkGiver,
                            entry.Key.Scope));
                }

                if (state.HasAmbiguousSpecificPriorityIntents)
                {
                    SetValue(values,
                        WorkloadStateDimension.SpecificJobOverrides,
                        "invalid:duplicate-target",
                        "1");
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOrder) != 0)
            {
                for (int i = 0; i < state.WorkTypeOrderIntents.Count; i++)
                {
                    WorkloadWorkTypeOrderIntentEntry entry = state.WorkTypeOrderIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    SetValue(values,
                        WorkloadStateDimension.SpecificJobOrder,
                        "intent:" + entry.Key.CanonicalKey,
                        entry.Intent.CanonicalForm,
                        new WorkloadChangeInspectionTarget(
                            entry.Key.IsGlobal ? null : entry.Key.Pawn,
                            entry.Key.WorkType,
                            scope: entry.Key.Scope));
                }

                if (state.HasAmbiguousWorkTypeOrderIntents)
                {
                    SetValue(values,
                        WorkloadStateDimension.SpecificJobOrder,
                        "invalid:duplicate-target",
                        "1");
                }
            }

            if ((dimensions & WorkloadOwnershipDimensions.PresentationSettings) != 0)
            {
                for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry = state.PresentationSettingIntents[i];
                    SetValue(values,
                        WorkloadStateDimension.PresentationSettings,
                        "intent:" + WorkloadCanonical.Encode(entry.Key),
                        entry.Intent.CanonicalForm);
                }
            }

            return values;
        }

        private static void SetValue(
            Dictionary<string, DiffValue> values,
            WorkloadStateDimension dimension,
            string key,
            string value,
            WorkloadChangeInspectionTarget inspectionTarget = null)
        {
            values[ComposeKey(dimension, key)] = new DiffValue(value, inspectionTarget);
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
