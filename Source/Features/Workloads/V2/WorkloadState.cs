using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Better_Work_Tab.Features.Workloads.V2
{
    public sealed class WorkloadParentPriorityEntry
    {
        public WorkloadParentPriorityEntry(WorkloadParentPriorityKey key, int priority)
        {
            Key = key ?? new WorkloadParentPriorityKey(null, null);
            Priority = priority;
        }

        public WorkloadParentPriorityEntry(PawnKey pawn, WorkTypeKey workType, int priority)
            : this(new WorkloadParentPriorityKey(pawn, workType), priority)
        {
        }

        public WorkloadParentPriorityKey Key { get; private set; }
        public int Priority { get; private set; }
    }

    public sealed class WorkloadManualModeEntry
    {
        public WorkloadManualModeEntry(WorkloadParentPriorityKey key, bool manual)
        {
            Key = key ?? new WorkloadParentPriorityKey(null, null);
            Manual = manual;
        }

        public WorkloadManualModeEntry(PawnKey pawn, WorkTypeKey workType, bool manual)
            : this(new WorkloadParentPriorityKey(pawn, workType), manual)
        {
        }

        public WorkloadParentPriorityKey Key { get; private set; }
        public bool Manual { get; private set; }
    }

    public sealed class WorkloadScheduleEntry
    {
        public WorkloadScheduleEntry(PawnKey pawn, ScheduleKey schedule)
        {
            Pawn = pawn ?? new PawnKey(null);
            Schedule = schedule ?? new ScheduleKey(-1);
        }

        public WorkloadScheduleEntry(string pawn, int schedule)
            : this(new PawnKey(pawn), new ScheduleKey(schedule))
        {
        }

        public PawnKey Pawn { get; private set; }
        public ScheduleKey Schedule { get; private set; }
    }

    public sealed class WorkloadSpecificJobOverrideEntry
    {
        public WorkloadSpecificJobOverrideEntry(WorkloadSpecificJobKey key, WorkloadScalarValue value)
        {
            Key = key ?? new WorkloadSpecificJobKey(null, null, null);
            Value = value;
        }

        public WorkloadSpecificJobOverrideEntry(
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver,
            WorkloadScalarValue value)
            : this(new WorkloadSpecificJobKey(pawn, workType, workGiver), value)
        {
        }

        public WorkloadSpecificJobKey Key { get; private set; }
        public WorkloadScalarValue Value { get; private set; }
    }

    public sealed class WorkloadSpecificJobOrderEntry
    {
        public WorkloadSpecificJobOrderEntry(WorkloadSpecificJobKey key, int order)
        {
            Key = key ?? new WorkloadSpecificJobKey(null, null, null);
            Order = order;
        }

        public WorkloadSpecificJobOrderEntry(
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver,
            int order)
            : this(new WorkloadSpecificJobKey(pawn, workType, workGiver), order)
        {
        }

        public WorkloadSpecificJobKey Key { get; private set; }
        public int Order { get; private set; }
    }

    public sealed class WorkloadPresentationSettingEntry
    {
        public WorkloadPresentationSettingEntry(string key, WorkloadScalarValue value)
        {
            Key = key ?? string.Empty;
            Value = value;
        }

        public string Key { get; private set; }
        public WorkloadScalarValue Value { get; private set; }
    }

    internal sealed class WorkloadPawnStateSnapshot
    {
        public WorkloadPawnStateSnapshot(
            IEnumerable<WorkloadParentPriorityEntry> parentPriorities,
            IEnumerable<WorkloadManualModeEntry> manualModes,
            IEnumerable<WorkloadScheduleEntry> schedules,
            IEnumerable<WorkloadSpecificJobOverrideEntry> specificJobOverrides,
            IEnumerable<WorkloadSpecificJobOrderEntry> specificJobOrder,
            IEnumerable<WorkloadPresentationSettingEntry> presentationSettings,
            bool represented)
        {
            ParentPriorities = new List<WorkloadParentPriorityEntry>(parentPriorities ?? new WorkloadParentPriorityEntry[0]).AsReadOnly();
            ManualModes = new List<WorkloadManualModeEntry>(manualModes ?? new WorkloadManualModeEntry[0]).AsReadOnly();
            Schedules = new List<WorkloadScheduleEntry>(schedules ?? new WorkloadScheduleEntry[0]).AsReadOnly();
            SpecificJobOverrides = new List<WorkloadSpecificJobOverrideEntry>(specificJobOverrides ?? new WorkloadSpecificJobOverrideEntry[0]).AsReadOnly();
            SpecificJobOrder = new List<WorkloadSpecificJobOrderEntry>(specificJobOrder ?? new WorkloadSpecificJobOrderEntry[0]).AsReadOnly();
            PresentationSettings = new List<WorkloadPresentationSettingEntry>(presentationSettings ?? new WorkloadPresentationSettingEntry[0]).AsReadOnly();
            Represented = represented;
        }

        public IReadOnlyList<WorkloadParentPriorityEntry> ParentPriorities { get; private set; }
        public IReadOnlyList<WorkloadManualModeEntry> ManualModes { get; private set; }
        public IReadOnlyList<WorkloadScheduleEntry> Schedules { get; private set; }
        public IReadOnlyList<WorkloadSpecificJobOverrideEntry> SpecificJobOverrides { get; private set; }
        public IReadOnlyList<WorkloadSpecificJobOrderEntry> SpecificJobOrder { get; private set; }
        public IReadOnlyList<WorkloadPresentationSettingEntry> PresentationSettings { get; private set; }
        public bool Represented { get; private set; }
    }

    public sealed class WorkloadProjectedState
    {
        private readonly ReadOnlyCollection<WorkloadParentPriorityEntry> _parentPriorities;
        private readonly ReadOnlyCollection<WorkloadManualModeEntry> _manualModes;
        private readonly ReadOnlyCollection<WorkloadScheduleEntry> _schedules;
        private readonly ReadOnlyCollection<WorkloadSpecificJobOverrideEntry> _specificJobOverrides;
        private readonly ReadOnlyCollection<WorkloadSpecificJobOrderEntry> _specificJobOrder;
        private readonly ReadOnlyCollection<WorkloadPresentationSettingEntry> _presentationSettings;
        private readonly ReadOnlyCollection<PawnKey> _representedPawnIds;
        private readonly ReadOnlyCollection<PawnKey> _excludedPawnIds;
        private readonly Dictionary<PawnKey, WorkloadPawnStateSnapshot> _excludedStagedStates;

        public WorkloadProjectedState(
            IEnumerable<WorkloadParentPriorityEntry> parentPriorities = null,
            IEnumerable<WorkloadManualModeEntry> manualModes = null,
            IEnumerable<WorkloadScheduleEntry> schedules = null,
            IEnumerable<WorkloadSpecificJobOverrideEntry> specificJobOverrides = null,
            IEnumerable<WorkloadSpecificJobOrderEntry> specificJobOrder = null,
            IEnumerable<WorkloadPresentationSettingEntry> presentationSettings = null,
            IEnumerable<PawnKey> representedPawnIds = null,
            IEnumerable<PawnKey> excludedPawnIds = null)
            : this(
                parentPriorities,
                manualModes,
                schedules,
                specificJobOverrides,
                specificJobOrder,
                presentationSettings,
                representedPawnIds,
                excludedPawnIds,
                null)
        {
        }

        private WorkloadProjectedState(
            IEnumerable<WorkloadParentPriorityEntry> parentPriorities,
            IEnumerable<WorkloadManualModeEntry> manualModes,
            IEnumerable<WorkloadScheduleEntry> schedules,
            IEnumerable<WorkloadSpecificJobOverrideEntry> specificJobOverrides,
            IEnumerable<WorkloadSpecificJobOrderEntry> specificJobOrder,
            IEnumerable<WorkloadPresentationSettingEntry> presentationSettings,
            IEnumerable<PawnKey> representedPawnIds,
            IEnumerable<PawnKey> excludedPawnIds,
            IDictionary<PawnKey, WorkloadPawnStateSnapshot> stagedStates)
        {
            ReadOnlyCollection<WorkloadParentPriorityEntry> rawParentPriorities = NormalizeParentPriorities(parentPriorities);
            ReadOnlyCollection<WorkloadManualModeEntry> rawManualModes = NormalizeManualModes(manualModes);
            ReadOnlyCollection<WorkloadScheduleEntry> rawSchedules = NormalizeSchedules(schedules);
            ReadOnlyCollection<WorkloadSpecificJobOverrideEntry> rawSpecificJobOverrides = NormalizeSpecificJobOverrides(specificJobOverrides);
            ReadOnlyCollection<WorkloadSpecificJobOrderEntry> rawSpecificJobOrder = NormalizeSpecificJobOrder(specificJobOrder);
            ReadOnlyCollection<WorkloadPresentationSettingEntry> rawPresentationSettings = NormalizePresentationSettings(presentationSettings);

            _excludedPawnIds = NormalizePawnIds(excludedPawnIds);
            _excludedStagedStates = new Dictionary<PawnKey, WorkloadPawnStateSnapshot>();
            for (int i = 0; i < _excludedPawnIds.Count; i++)
            {
                PawnKey pawn = _excludedPawnIds[i];
                WorkloadPawnStateSnapshot staged;
                if (stagedStates != null && stagedStates.TryGetValue(pawn, out staged))
                {
                    _excludedStagedStates[pawn] = staged;
                }
                else
                {
                    _excludedStagedStates[pawn] = CaptureSnapshot(
                        pawn,
                        rawParentPriorities,
                        rawManualModes,
                        rawSchedules,
                        rawSpecificJobOverrides,
                        rawSpecificJobOrder,
                        rawPresentationSettings,
                        representedPawnIds);
                }
            }

            _parentPriorities = RemoveExcluded(rawParentPriorities, _excludedPawnIds);
            _manualModes = RemoveExcluded(rawManualModes, _excludedPawnIds);
            _schedules = RemoveExcluded(rawSchedules, _excludedPawnIds);
            _specificJobOverrides = RemoveExcluded(rawSpecificJobOverrides, _excludedPawnIds);
            _specificJobOrder = RemoveExcluded(rawSpecificJobOrder, _excludedPawnIds);
            _presentationSettings = rawPresentationSettings;
            _representedPawnIds = BuildRepresentedPawnIds(
                _parentPriorities,
                _manualModes,
                _schedules,
                _specificJobOverrides,
                _specificJobOrder,
                representedPawnIds,
                _excludedPawnIds,
                _excludedStagedStates);
        }

        public static WorkloadProjectedState Empty
        {
            get { return new WorkloadProjectedState(); }
        }

        public IReadOnlyList<WorkloadParentPriorityEntry> ParentPriorities => _parentPriorities;
        public IReadOnlyList<WorkloadManualModeEntry> ManualModes => _manualModes;
        public IReadOnlyList<WorkloadScheduleEntry> Schedules => _schedules;
        public IReadOnlyList<WorkloadSpecificJobOverrideEntry> SpecificJobOverrides => _specificJobOverrides;
        public IReadOnlyList<WorkloadSpecificJobOrderEntry> SpecificJobOrder => _specificJobOrder;
        public IReadOnlyList<WorkloadPresentationSettingEntry> PresentationSettings => _presentationSettings;
        public IReadOnlyList<PawnKey> RepresentedPawnIds => _representedPawnIds;
        public IReadOnlyList<PawnKey> ExcludedPawnIds => _excludedPawnIds;

        public string CanonicalForm => GetCanonicalForm(WorkloadOwnershipDimensions.All);
        public string SemanticFingerprint => GetSemanticFingerprint(WorkloadOwnershipDimensions.All);

        public string GetCanonicalForm(WorkloadOwnershipDimensions dimensions)
        {
            return WorkloadCanonicalState.For(this, dimensions);
        }

        public string GetSemanticFingerprint(WorkloadOwnershipDimensions dimensions)
        {
            return WorkloadCanonical.Fingerprint(GetCanonicalForm(dimensions));
        }

        public bool SemanticallyEquals(WorkloadProjectedState other)
        {
            return SemanticallyEquals(other, WorkloadOwnershipDimensions.All);
        }

        public bool SemanticallyEquals(WorkloadProjectedState other, WorkloadOwnershipDimensions dimensions)
        {
            WorkloadProjectedState safeOther = other ?? Empty;
            return StringComparer.Ordinal.Equals(GetCanonicalForm(dimensions), safeOther.GetCanonicalForm(dimensions));
        }

        internal static WorkloadProjectedState FromMaps(
            IDictionary<WorkloadParentPriorityKey, int> parentPriorities,
            IDictionary<WorkloadParentPriorityKey, bool> manualModes,
            IDictionary<PawnKey, ScheduleKey> schedules,
            IDictionary<WorkloadSpecificJobKey, WorkloadScalarValue> specificJobOverrides,
            IDictionary<WorkloadSpecificJobKey, int> specificJobOrder,
            IDictionary<string, WorkloadScalarValue> presentationSettings,
            IEnumerable<PawnKey> representedPawnIds = null,
            IEnumerable<PawnKey> excludedPawnIds = null,
            IDictionary<PawnKey, WorkloadPawnStateSnapshot> stagedStates = null)
        {
            var priorities = new List<WorkloadParentPriorityEntry>();
            if (parentPriorities != null)
            {
                foreach (KeyValuePair<WorkloadParentPriorityKey, int> item in parentPriorities)
                {
                    priorities.Add(new WorkloadParentPriorityEntry(item.Key, item.Value));
                }
            }

            var manual = new List<WorkloadManualModeEntry>();
            if (manualModes != null)
            {
                foreach (KeyValuePair<WorkloadParentPriorityKey, bool> item in manualModes)
                {
                    manual.Add(new WorkloadManualModeEntry(item.Key, item.Value));
                }
            }

            var schedulesList = new List<WorkloadScheduleEntry>();
            if (schedules != null)
            {
                foreach (KeyValuePair<PawnKey, ScheduleKey> item in schedules)
                {
                    schedulesList.Add(new WorkloadScheduleEntry(item.Key, item.Value));
                }
            }

            var overrides = new List<WorkloadSpecificJobOverrideEntry>();
            if (specificJobOverrides != null)
            {
                foreach (KeyValuePair<WorkloadSpecificJobKey, WorkloadScalarValue> item in specificJobOverrides)
                {
                    overrides.Add(new WorkloadSpecificJobOverrideEntry(item.Key, item.Value));
                }
            }

            var order = new List<WorkloadSpecificJobOrderEntry>();
            if (specificJobOrder != null)
            {
                foreach (KeyValuePair<WorkloadSpecificJobKey, int> item in specificJobOrder)
                {
                    order.Add(new WorkloadSpecificJobOrderEntry(item.Key, item.Value));
                }
            }

            var presentation = new List<WorkloadPresentationSettingEntry>();
            if (presentationSettings != null)
            {
                foreach (KeyValuePair<string, WorkloadScalarValue> item in presentationSettings)
                {
                    presentation.Add(new WorkloadPresentationSettingEntry(item.Key, item.Value));
                }
            }

            return new WorkloadProjectedState(
                priorities,
                manual,
                schedulesList,
                overrides,
                order,
                presentation,
                representedPawnIds,
                excludedPawnIds,
                stagedStates);
        }

        /// <summary>
        /// Keeps a projected state inside an explicit-pawn workload scope.
        /// Current-map scopes are deliberately left dynamic; their membership
        /// depends on live RimWorld state and is validated at the runtime
        /// commit boundary instead of being guessed by this value object.
        /// </summary>
        internal WorkloadProjectedState LimitToScope(WorkloadScope scope)
        {
            if (scope == null || scope.Mode != WorkloadScopeMode.ExplicitPawnIds)
            {
                return this;
            }

            var allowed = new HashSet<PawnKey>();
            for (int i = 0; i < scope.ExplicitPawnIds.Count; i++)
            {
                allowed.Add(scope.ExplicitPawnIds[i]);
            }

            // An explicit exclusion is still part of the saved scope even if
            // older data omitted it from ExplicitPawnIds. Retain its tombstone
            // while continuing to reject all state entries outside the scope.
            for (int i = 0; i < scope.ExcludedPawnIds.Count; i++)
            {
                allowed.Add(scope.ExcludedPawnIds[i]);
            }

            var priorities = new Dictionary<WorkloadParentPriorityKey, int>();
            for (int i = 0; i < _parentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = _parentPriorities[i];
                if (allowed.Contains(entry.Key.Pawn)) priorities[entry.Key] = entry.Priority;
            }

            var manual = new Dictionary<WorkloadParentPriorityKey, bool>();
            for (int i = 0; i < _manualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = _manualModes[i];
                if (allowed.Contains(entry.Key.Pawn)) manual[entry.Key] = entry.Manual;
            }

            var schedules = new Dictionary<PawnKey, ScheduleKey>();
            for (int i = 0; i < _schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = _schedules[i];
                if (allowed.Contains(entry.Pawn)) schedules[entry.Pawn] = entry.Schedule;
            }

            var overrides = new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
            for (int i = 0; i < _specificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = _specificJobOverrides[i];
                if (allowed.Contains(entry.Key.Pawn)) overrides[entry.Key] = entry.Value;
            }

            var order = new Dictionary<WorkloadSpecificJobKey, int>();
            for (int i = 0; i < _specificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = _specificJobOrder[i];
                if (allowed.Contains(entry.Key.Pawn)) order[entry.Key] = entry.Order;
            }

            var presentation = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            for (int i = 0; i < _presentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = _presentationSettings[i];
                presentation[entry.Key] = entry.Value;
            }

            var represented = new List<PawnKey>();
            for (int i = 0; i < _representedPawnIds.Count; i++)
            {
                if (allowed.Contains(_representedPawnIds[i])) represented.Add(_representedPawnIds[i]);
            }

            var excluded = new List<PawnKey>();
            for (int i = 0; i < _excludedPawnIds.Count; i++)
            {
                if (allowed.Contains(_excludedPawnIds[i])) excluded.Add(_excludedPawnIds[i]);
            }

            Dictionary<PawnKey, WorkloadPawnStateSnapshot> staged = CopyExcludedStagedStates();
            var stagedKeys = new List<PawnKey>(staged.Keys);
            for (int i = 0; i < stagedKeys.Count; i++)
            {
                if (!allowed.Contains(stagedKeys[i])) staged.Remove(stagedKeys[i]);
            }

            return FromMaps(
                priorities,
                manual,
                schedules,
                overrides,
                order,
                presentation,
                represented,
                excluded,
                staged);
        }

        internal WorkloadProjectedState ExcludePawn(PawnKey pawn)
        {
            PawnKey safePawn = pawn ?? new PawnKey(null);
            if (Contains(_excludedPawnIds, safePawn)) return this;

            var excluded = new List<PawnKey>(_excludedPawnIds) { safePawn };
            Dictionary<PawnKey, WorkloadPawnStateSnapshot> staged = CopyExcludedStagedStates();
            staged[safePawn] = CapturePawnState(safePawn);
            return new WorkloadProjectedState(
                _parentPriorities,
                _manualModes,
                _schedules,
                _specificJobOverrides,
                _specificJobOrder,
                _presentationSettings,
                _representedPawnIds,
                excluded,
                staged);
        }

        internal WorkloadProjectedState IncludePawn(PawnKey pawn)
        {
            PawnKey safePawn = pawn ?? new PawnKey(null);
            if (!Contains(_excludedPawnIds, safePawn)) return this;

            WorkloadPawnStateSnapshot staged = CapturePawnState(safePawn);
            var excluded = new List<PawnKey>(_excludedPawnIds);
            excluded.Remove(safePawn);
            var represented = new List<PawnKey>(_representedPawnIds);
            if (staged.Represented && !Contains(represented, safePawn)) represented.Add(safePawn);

            var priorities = new List<WorkloadParentPriorityEntry>(_parentPriorities);
            priorities.AddRange(staged.ParentPriorities);
            var manual = new List<WorkloadManualModeEntry>(_manualModes);
            manual.AddRange(staged.ManualModes);
            var schedules = new List<WorkloadScheduleEntry>(_schedules);
            schedules.AddRange(staged.Schedules);
            var overrides = new List<WorkloadSpecificJobOverrideEntry>(_specificJobOverrides);
            overrides.AddRange(staged.SpecificJobOverrides);
            var order = new List<WorkloadSpecificJobOrderEntry>(_specificJobOrder);
            order.AddRange(staged.SpecificJobOrder);
            var presentation = new List<WorkloadPresentationSettingEntry>(_presentationSettings);
            presentation.AddRange(staged.PresentationSettings);

            Dictionary<PawnKey, WorkloadPawnStateSnapshot> stagedStates = CopyExcludedStagedStates();
            stagedStates.Remove(safePawn);
            return new WorkloadProjectedState(
                priorities,
                manual,
                schedules,
                overrides,
                order,
                presentation,
                represented,
                excluded,
                stagedStates);
        }

        internal bool IsExcluded(PawnKey pawn)
        {
            return Contains(_excludedPawnIds, pawn ?? new PawnKey(null));
        }

        internal Dictionary<PawnKey, WorkloadPawnStateSnapshot> CopyExcludedStagedStates()
        {
            return new Dictionary<PawnKey, WorkloadPawnStateSnapshot>(_excludedStagedStates);
        }

        private WorkloadPawnStateSnapshot CapturePawnState(PawnKey pawn)
        {
            WorkloadPawnStateSnapshot staged;
            if (_excludedStagedStates.TryGetValue(pawn, out staged)) return staged;

            var priorities = new List<WorkloadParentPriorityEntry>();
            for (int i = 0; i < _parentPriorities.Count; i++)
            {
                if (Equals(_parentPriorities[i].Key.Pawn, pawn)) priorities.Add(_parentPriorities[i]);
            }
            var manual = new List<WorkloadManualModeEntry>();
            for (int i = 0; i < _manualModes.Count; i++)
            {
                if (Equals(_manualModes[i].Key.Pawn, pawn)) manual.Add(_manualModes[i]);
            }
            var schedules = new List<WorkloadScheduleEntry>();
            for (int i = 0; i < _schedules.Count; i++)
            {
                if (Equals(_schedules[i].Pawn, pawn)) schedules.Add(_schedules[i]);
            }
            var overrides = new List<WorkloadSpecificJobOverrideEntry>();
            for (int i = 0; i < _specificJobOverrides.Count; i++)
            {
                if (Equals(_specificJobOverrides[i].Key.Pawn, pawn)) overrides.Add(_specificJobOverrides[i]);
            }
            var order = new List<WorkloadSpecificJobOrderEntry>();
            for (int i = 0; i < _specificJobOrder.Count; i++)
            {
                if (Equals(_specificJobOrder[i].Key.Pawn, pawn)) order.Add(_specificJobOrder[i]);
            }

            return new WorkloadPawnStateSnapshot(
                priorities,
                manual,
                schedules,
                overrides,
                order,
                new WorkloadPresentationSettingEntry[0],
                Contains(_representedPawnIds, pawn));
        }

        private static ReadOnlyCollection<WorkloadParentPriorityEntry> NormalizeParentPriorities(
            IEnumerable<WorkloadParentPriorityEntry> source)
        {
            var values = new Dictionary<WorkloadParentPriorityKey, int>();
            if (source != null)
            {
                foreach (WorkloadParentPriorityEntry entry in source)
                {
                    if (entry == null) continue;
                    int existing;
                    if (!values.TryGetValue(entry.Key, out existing) || entry.Priority < existing)
                    {
                        values[entry.Key] = entry.Priority;
                    }
                }
            }

            var result = new List<WorkloadParentPriorityEntry>();
            foreach (KeyValuePair<WorkloadParentPriorityKey, int> item in values)
            {
                result.Add(new WorkloadParentPriorityEntry(item.Key, item.Value));
            }

            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadManualModeEntry> NormalizeManualModes(
            IEnumerable<WorkloadManualModeEntry> source)
        {
            var values = new Dictionary<WorkloadParentPriorityKey, bool>();
            if (source != null)
            {
                foreach (WorkloadManualModeEntry entry in source)
                {
                    if (entry == null) continue;
                    bool existing;
                    if (!values.TryGetValue(entry.Key, out existing) || (!entry.Manual && existing))
                    {
                        values[entry.Key] = entry.Manual;
                    }
                }
            }

            var result = new List<WorkloadManualModeEntry>();
            foreach (KeyValuePair<WorkloadParentPriorityKey, bool> item in values)
            {
                result.Add(new WorkloadManualModeEntry(item.Key, item.Value));
            }

            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadScheduleEntry> NormalizeSchedules(
            IEnumerable<WorkloadScheduleEntry> source)
        {
            var values = new Dictionary<PawnKey, ScheduleKey>();
            if (source != null)
            {
                foreach (WorkloadScheduleEntry entry in source)
                {
                    if (entry == null) continue;
                    ScheduleKey existing;
                    if (!values.TryGetValue(entry.Pawn, out existing) || entry.Schedule.Value < existing.Value)
                    {
                        values[entry.Pawn] = entry.Schedule;
                    }
                }
            }

            var result = new List<WorkloadScheduleEntry>();
            foreach (KeyValuePair<PawnKey, ScheduleKey> item in values)
            {
                result.Add(new WorkloadScheduleEntry(item.Key, item.Value));
            }

            result.Sort((left, right) => left.Pawn.CompareTo(right.Pawn));
            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadSpecificJobOverrideEntry> NormalizeSpecificJobOverrides(
            IEnumerable<WorkloadSpecificJobOverrideEntry> source)
        {
            var values = new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
            if (source != null)
            {
                foreach (WorkloadSpecificJobOverrideEntry entry in source)
                {
                    if (entry == null) continue;
                    WorkloadScalarValue existing;
                    if (!values.TryGetValue(entry.Key, out existing)
                        || StringComparer.Ordinal.Compare(entry.Value.CanonicalValue, existing.CanonicalValue) < 0)
                    {
                        values[entry.Key] = entry.Value;
                    }
                }
            }

            var result = new List<WorkloadSpecificJobOverrideEntry>();
            foreach (KeyValuePair<WorkloadSpecificJobKey, WorkloadScalarValue> item in values)
            {
                result.Add(new WorkloadSpecificJobOverrideEntry(item.Key, item.Value));
            }

            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadSpecificJobOrderEntry> NormalizeSpecificJobOrder(
            IEnumerable<WorkloadSpecificJobOrderEntry> source)
        {
            var values = new Dictionary<WorkloadSpecificJobKey, int>();
            if (source != null)
            {
                foreach (WorkloadSpecificJobOrderEntry entry in source)
                {
                    if (entry == null) continue;
                    int existing;
                    if (!values.TryGetValue(entry.Key, out existing) || entry.Order < existing)
                    {
                        values[entry.Key] = entry.Order;
                    }
                }
            }

            var result = new List<WorkloadSpecificJobOrderEntry>();
            foreach (KeyValuePair<WorkloadSpecificJobKey, int> item in values)
            {
                result.Add(new WorkloadSpecificJobOrderEntry(item.Key, item.Value));
            }

            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadPresentationSettingEntry> NormalizePresentationSettings(
            IEnumerable<WorkloadPresentationSettingEntry> source)
        {
            var values = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            if (source != null)
            {
                foreach (WorkloadPresentationSettingEntry entry in source)
                {
                    if (entry == null) continue;
                    WorkloadScalarValue existing;
                    if (!values.TryGetValue(entry.Key, out existing)
                        || StringComparer.Ordinal.Compare(entry.Value.CanonicalValue, existing.CanonicalValue) < 0)
                    {
                        values[entry.Key] = entry.Value;
                    }
                }
            }

            var result = new List<WorkloadPresentationSettingEntry>();
            foreach (KeyValuePair<string, WorkloadScalarValue> item in values)
            {
                result.Add(new WorkloadPresentationSettingEntry(item.Key, item.Value));
            }

            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<PawnKey> NormalizePawnIds(IEnumerable<PawnKey> source)
        {
            var unique = new Dictionary<PawnKey, PawnKey>();
            if (source != null)
            {
                foreach (PawnKey pawn in source)
                {
                    PawnKey safePawn = pawn ?? new PawnKey(null);
                    unique[safePawn] = safePawn;
                }
            }

            var result = new List<PawnKey>(unique.Values);
            result.Sort((left, right) => left.CompareTo(right));
            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadParentPriorityEntry> RemoveExcluded(
            IReadOnlyList<WorkloadParentPriorityEntry> source,
            IReadOnlyList<PawnKey> excluded)
        {
            var result = new List<WorkloadParentPriorityEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (!Contains(excluded, source[i].Key.Pawn)) result.Add(source[i]);
            }

            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadManualModeEntry> RemoveExcluded(
            IReadOnlyList<WorkloadManualModeEntry> source,
            IReadOnlyList<PawnKey> excluded)
        {
            var result = new List<WorkloadManualModeEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (!Contains(excluded, source[i].Key.Pawn)) result.Add(source[i]);
            }

            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadScheduleEntry> RemoveExcluded(
            IReadOnlyList<WorkloadScheduleEntry> source,
            IReadOnlyList<PawnKey> excluded)
        {
            var result = new List<WorkloadScheduleEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (!Contains(excluded, source[i].Pawn)) result.Add(source[i]);
            }

            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadSpecificJobOverrideEntry> RemoveExcluded(
            IReadOnlyList<WorkloadSpecificJobOverrideEntry> source,
            IReadOnlyList<PawnKey> excluded)
        {
            var result = new List<WorkloadSpecificJobOverrideEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (!Contains(excluded, source[i].Key.Pawn)) result.Add(source[i]);
            }

            return result.AsReadOnly();
        }

        private static ReadOnlyCollection<WorkloadSpecificJobOrderEntry> RemoveExcluded(
            IReadOnlyList<WorkloadSpecificJobOrderEntry> source,
            IReadOnlyList<PawnKey> excluded)
        {
            var result = new List<WorkloadSpecificJobOrderEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                if (!Contains(excluded, source[i].Key.Pawn)) result.Add(source[i]);
            }

            return result.AsReadOnly();
        }

        private static WorkloadPawnStateSnapshot CaptureSnapshot(
            PawnKey pawn,
            IReadOnlyList<WorkloadParentPriorityEntry> parentPriorities,
            IReadOnlyList<WorkloadManualModeEntry> manualModes,
            IReadOnlyList<WorkloadScheduleEntry> schedules,
            IReadOnlyList<WorkloadSpecificJobOverrideEntry> specificJobOverrides,
            IReadOnlyList<WorkloadSpecificJobOrderEntry> specificJobOrder,
            IReadOnlyList<WorkloadPresentationSettingEntry> presentationSettings,
            IEnumerable<PawnKey> representedPawnIds)
        {
            var priorities = new List<WorkloadParentPriorityEntry>();
            for (int i = 0; i < parentPriorities.Count; i++)
            {
                if (Equals(parentPriorities[i].Key.Pawn, pawn)) priorities.Add(parentPriorities[i]);
            }
            var manual = new List<WorkloadManualModeEntry>();
            for (int i = 0; i < manualModes.Count; i++)
            {
                if (Equals(manualModes[i].Key.Pawn, pawn)) manual.Add(manualModes[i]);
            }
            var schedulesForPawn = new List<WorkloadScheduleEntry>();
            for (int i = 0; i < schedules.Count; i++)
            {
                if (Equals(schedules[i].Pawn, pawn)) schedulesForPawn.Add(schedules[i]);
            }
            var overrides = new List<WorkloadSpecificJobOverrideEntry>();
            for (int i = 0; i < specificJobOverrides.Count; i++)
            {
                if (Equals(specificJobOverrides[i].Key.Pawn, pawn)) overrides.Add(specificJobOverrides[i]);
            }
            var order = new List<WorkloadSpecificJobOrderEntry>();
            for (int i = 0; i < specificJobOrder.Count; i++)
            {
                if (Equals(specificJobOrder[i].Key.Pawn, pawn)) order.Add(specificJobOrder[i]);
            }

            return new WorkloadPawnStateSnapshot(
                priorities,
                manual,
                schedulesForPawn,
                overrides,
                order,
                new WorkloadPresentationSettingEntry[0],
                Contains(representedPawnIds, pawn));
        }

        private static bool Contains<T>(IEnumerable<T> values, T value)
        {
            if (values == null) return false;
            foreach (T item in values)
            {
                if (EqualityComparer<T>.Default.Equals(item, value)) return true;
            }

            return false;
        }

        private static ReadOnlyCollection<PawnKey> BuildRepresentedPawnIds(
            IReadOnlyList<WorkloadParentPriorityEntry> parentPriorities,
            IReadOnlyList<WorkloadManualModeEntry> manualModes,
            IReadOnlyList<WorkloadScheduleEntry> schedules,
            IReadOnlyList<WorkloadSpecificJobOverrideEntry> specificJobOverrides,
            IReadOnlyList<WorkloadSpecificJobOrderEntry> specificJobOrder,
            IEnumerable<PawnKey> representedPawnIds,
            IReadOnlyList<PawnKey> excludedPawnIds,
            IDictionary<PawnKey, WorkloadPawnStateSnapshot> stagedStates)
        {
            var unique = new Dictionary<PawnKey, PawnKey>();
            if (representedPawnIds != null)
            {
                foreach (PawnKey pawn in representedPawnIds)
                {
                    PawnKey safePawn = pawn ?? new PawnKey(null);
                    unique[safePawn] = safePawn;
                }
            }
            for (int i = 0; i < parentPriorities.Count; i++) unique[parentPriorities[i].Key.Pawn] = parentPriorities[i].Key.Pawn;
            for (int i = 0; i < manualModes.Count; i++) unique[manualModes[i].Key.Pawn] = manualModes[i].Key.Pawn;
            for (int i = 0; i < schedules.Count; i++) unique[schedules[i].Pawn] = schedules[i].Pawn;
            for (int i = 0; i < specificJobOverrides.Count; i++) unique[specificJobOverrides[i].Key.Pawn] = specificJobOverrides[i].Key.Pawn;
            for (int i = 0; i < specificJobOrder.Count; i++) unique[specificJobOrder[i].Key.Pawn] = specificJobOrder[i].Key.Pawn;

            if (stagedStates != null)
            {
                foreach (KeyValuePair<PawnKey, WorkloadPawnStateSnapshot> item in stagedStates)
                {
                    if (item.Value != null && item.Value.Represented) unique[item.Key] = item.Key;
                }
            }

            if (excludedPawnIds != null)
            {
                for (int i = 0; i < excludedPawnIds.Count; i++) unique.Remove(excludedPawnIds[i]);
            }

            var result = new List<PawnKey>(unique.Values);
            result.Sort((left, right) => left.CompareTo(right));
            return result.AsReadOnly();
        }
    }

    public sealed class WorkloadDraft
    {
        private readonly WorkloadProjectedState _baseState;
        private readonly Dictionary<WorkloadParentPriorityKey, int> _parentPriorityOverlay = new Dictionary<WorkloadParentPriorityKey, int>();
        private readonly HashSet<WorkloadParentPriorityKey> _removedParentPriorities = new HashSet<WorkloadParentPriorityKey>();
        private readonly Dictionary<WorkloadParentPriorityKey, bool> _manualModeOverlay = new Dictionary<WorkloadParentPriorityKey, bool>();
        private readonly HashSet<WorkloadParentPriorityKey> _removedManualModes = new HashSet<WorkloadParentPriorityKey>();
        private readonly Dictionary<PawnKey, ScheduleKey> _scheduleOverlay = new Dictionary<PawnKey, ScheduleKey>();
        private readonly HashSet<PawnKey> _removedSchedules = new HashSet<PawnKey>();
        private readonly Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue> _specificJobOverrideOverlay = new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
        private readonly HashSet<WorkloadSpecificJobKey> _removedSpecificJobOverrides = new HashSet<WorkloadSpecificJobKey>();
        private readonly Dictionary<WorkloadSpecificJobKey, int> _specificJobOrderOverlay = new Dictionary<WorkloadSpecificJobKey, int>();
        private readonly HashSet<WorkloadSpecificJobKey> _removedSpecificJobOrder = new HashSet<WorkloadSpecificJobKey>();
        private readonly Dictionary<string, WorkloadScalarValue> _presentationSettingOverlay = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
        private readonly HashSet<string> _removedPresentationSettings = new HashSet<string>(StringComparer.Ordinal);

        public WorkloadDraft(WorkloadProjectedState baseState)
        {
            _baseState = baseState ?? WorkloadProjectedState.Empty;
        }

        public WorkloadProjectedState BaseState => _baseState;
        public WorkloadProjectedState ProjectedState => BuildProjectedState();
        public bool HasSemanticChanges => !WorkloadSemanticDiff.Between(_baseState, ProjectedState).IsEmpty;

        public WorkloadDraft SetParentPriority(PawnKey pawn, WorkTypeKey workType, int priority)
        {
            return SetParentPriority(new WorkloadParentPriorityKey(pawn, workType), priority);
        }

        public WorkloadDraft SetParentPriority(WorkloadParentPriorityKey key, int priority)
        {
            WorkloadParentPriorityKey safeKey = key ?? new WorkloadParentPriorityKey(null, null);
            _removedParentPriorities.Remove(safeKey);
            _parentPriorityOverlay[safeKey] = priority;
            return this;
        }

        public WorkloadDraft RemoveParentPriority(PawnKey pawn, WorkTypeKey workType)
        {
            return RemoveParentPriority(new WorkloadParentPriorityKey(pawn, workType));
        }

        public WorkloadDraft RemoveParentPriority(WorkloadParentPriorityKey key)
        {
            WorkloadParentPriorityKey safeKey = key ?? new WorkloadParentPriorityKey(null, null);
            _parentPriorityOverlay.Remove(safeKey);
            _removedParentPriorities.Add(safeKey);
            return this;
        }

        public WorkloadDraft SetManualMode(PawnKey pawn, WorkTypeKey workType, bool manual)
        {
            return SetManualMode(new WorkloadParentPriorityKey(pawn, workType), manual);
        }

        public WorkloadDraft SetManualMode(WorkloadParentPriorityKey key, bool manual)
        {
            WorkloadParentPriorityKey safeKey = key ?? new WorkloadParentPriorityKey(null, null);
            _removedManualModes.Remove(safeKey);
            _manualModeOverlay[safeKey] = manual;
            return this;
        }

        public WorkloadDraft RemoveManualMode(PawnKey pawn, WorkTypeKey workType)
        {
            return RemoveManualMode(new WorkloadParentPriorityKey(pawn, workType));
        }

        public WorkloadDraft RemoveManualMode(WorkloadParentPriorityKey key)
        {
            WorkloadParentPriorityKey safeKey = key ?? new WorkloadParentPriorityKey(null, null);
            _manualModeOverlay.Remove(safeKey);
            _removedManualModes.Add(safeKey);
            return this;
        }

        public WorkloadDraft SetSchedule(PawnKey pawn, ScheduleKey schedule)
        {
            PawnKey safePawn = pawn ?? new PawnKey(null);
            _removedSchedules.Remove(safePawn);
            _scheduleOverlay[safePawn] = schedule ?? new ScheduleKey(-1);
            return this;
        }

        public WorkloadDraft RemoveSchedule(PawnKey pawn)
        {
            PawnKey safePawn = pawn ?? new PawnKey(null);
            _scheduleOverlay.Remove(safePawn);
            _removedSchedules.Add(safePawn);
            return this;
        }

        public WorkloadDraft SetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            WorkloadScalarValue value)
        {
            WorkloadSpecificJobKey safeKey = key ?? new WorkloadSpecificJobKey(null, null, null);
            _removedSpecificJobOverrides.Remove(safeKey);
            _specificJobOverrideOverlay[safeKey] = value;
            return this;
        }

        public WorkloadDraft SetSpecificJobOverride(
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver,
            WorkloadScalarValue value)
        {
            return SetSpecificJobOverride(new WorkloadSpecificJobKey(pawn, workType, workGiver), value);
        }

        public WorkloadDraft RemoveSpecificJobOverride(WorkloadSpecificJobKey key)
        {
            WorkloadSpecificJobKey safeKey = key ?? new WorkloadSpecificJobKey(null, null, null);
            _specificJobOverrideOverlay.Remove(safeKey);
            _removedSpecificJobOverrides.Add(safeKey);
            return this;
        }

        public WorkloadDraft SetSpecificJobOrder(WorkloadSpecificJobKey key, int order)
        {
            WorkloadSpecificJobKey safeKey = key ?? new WorkloadSpecificJobKey(null, null, null);
            _removedSpecificJobOrder.Remove(safeKey);
            _specificJobOrderOverlay[safeKey] = order;
            return this;
        }

        public WorkloadDraft SetSpecificJobOrder(
            PawnKey pawn,
            WorkTypeKey workType,
            WorkGiverKey workGiver,
            int order)
        {
            return SetSpecificJobOrder(new WorkloadSpecificJobKey(pawn, workType, workGiver), order);
        }

        public WorkloadDraft RemoveSpecificJobOrder(WorkloadSpecificJobKey key)
        {
            WorkloadSpecificJobKey safeKey = key ?? new WorkloadSpecificJobKey(null, null, null);
            _specificJobOrderOverlay.Remove(safeKey);
            _removedSpecificJobOrder.Add(safeKey);
            return this;
        }

        public WorkloadDraft SetPresentationSetting(string key, WorkloadScalarValue value)
        {
            string safeKey = key ?? string.Empty;
            _removedPresentationSettings.Remove(safeKey);
            _presentationSettingOverlay[safeKey] = value;
            return this;
        }

        public WorkloadDraft RemovePresentationSetting(string key)
        {
            string safeKey = key ?? string.Empty;
            _presentationSettingOverlay.Remove(safeKey);
            _removedPresentationSettings.Add(safeKey);
            return this;
        }

        private WorkloadProjectedState BuildProjectedState()
        {
            var priorities = new Dictionary<WorkloadParentPriorityKey, int>();
            for (int i = 0; i < _baseState.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = _baseState.ParentPriorities[i];
                priorities[entry.Key] = entry.Priority;
            }
            foreach (WorkloadParentPriorityKey key in _removedParentPriorities) priorities.Remove(key);
            foreach (KeyValuePair<WorkloadParentPriorityKey, int> item in _parentPriorityOverlay) priorities[item.Key] = item.Value;

            var manual = new Dictionary<WorkloadParentPriorityKey, bool>();
            for (int i = 0; i < _baseState.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = _baseState.ManualModes[i];
                manual[entry.Key] = entry.Manual;
            }
            foreach (WorkloadParentPriorityKey key in _removedManualModes) manual.Remove(key);
            foreach (KeyValuePair<WorkloadParentPriorityKey, bool> item in _manualModeOverlay) manual[item.Key] = item.Value;

            var schedules = new Dictionary<PawnKey, ScheduleKey>();
            for (int i = 0; i < _baseState.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = _baseState.Schedules[i];
                schedules[entry.Pawn] = entry.Schedule;
            }
            foreach (PawnKey key in _removedSchedules) schedules.Remove(key);
            foreach (KeyValuePair<PawnKey, ScheduleKey> item in _scheduleOverlay) schedules[item.Key] = item.Value;

            var overrides = new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
            for (int i = 0; i < _baseState.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = _baseState.SpecificJobOverrides[i];
                overrides[entry.Key] = entry.Value;
            }
            foreach (WorkloadSpecificJobKey key in _removedSpecificJobOverrides) overrides.Remove(key);
            foreach (KeyValuePair<WorkloadSpecificJobKey, WorkloadScalarValue> item in _specificJobOverrideOverlay) overrides[item.Key] = item.Value;

            var order = new Dictionary<WorkloadSpecificJobKey, int>();
            for (int i = 0; i < _baseState.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = _baseState.SpecificJobOrder[i];
                order[entry.Key] = entry.Order;
            }
            foreach (WorkloadSpecificJobKey key in _removedSpecificJobOrder) order.Remove(key);
            foreach (KeyValuePair<WorkloadSpecificJobKey, int> item in _specificJobOrderOverlay) order[item.Key] = item.Value;

            var presentation = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            for (int i = 0; i < _baseState.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = _baseState.PresentationSettings[i];
                presentation[entry.Key] = entry.Value;
            }
            foreach (string key in _removedPresentationSettings) presentation.Remove(key);
            foreach (KeyValuePair<string, WorkloadScalarValue> item in _presentationSettingOverlay) presentation[item.Key] = item.Value;

            return WorkloadProjectedState.FromMaps(
                priorities,
                manual,
                schedules,
                overrides,
                order,
                presentation,
                _baseState.RepresentedPawnIds,
                _baseState.ExcludedPawnIds,
                _baseState.CopyExcludedStagedStates());
        }
    }

    internal static class WorkloadCanonicalState
    {
        public static string For(WorkloadProjectedState state, WorkloadOwnershipDimensions dimensions)
        {
            WorkloadProjectedState safeState = state ?? WorkloadProjectedState.Empty;
            var builder = new System.Text.StringBuilder();

            builder.Append("X[I");
            for (int i = 0; i < safeState.RepresentedPawnIds.Count; i++)
            {
                builder.Append(WorkloadCanonical.Encode(safeState.RepresentedPawnIds[i].Value)).Append(';');
            }
            builder.Append("]E[");
            for (int i = 0; i < safeState.ExcludedPawnIds.Count; i++)
            {
                builder.Append(WorkloadCanonical.Encode(safeState.ExcludedPawnIds[i].Value)).Append(';');
            }
            builder.Append("]");

            if ((dimensions & WorkloadOwnershipDimensions.ParentPriorities) != 0)
            {
                builder.Append("P[");
                for (int i = 0; i < safeState.ParentPriorities.Count; i++)
                {
                    WorkloadParentPriorityEntry entry = safeState.ParentPriorities[i];
                    builder.Append(WorkloadCanonical.Encode(entry.Key.CanonicalKey))
                        .Append('=')
                        .Append(WorkloadCanonical.Integer(entry.Priority))
                        .Append(';');
                }
                builder.Append(']');
            }

            if ((dimensions & WorkloadOwnershipDimensions.ManualModes) != 0)
            {
                builder.Append("M[");
                for (int i = 0; i < safeState.ManualModes.Count; i++)
                {
                    WorkloadManualModeEntry entry = safeState.ManualModes[i];
                    builder.Append(WorkloadCanonical.Encode(entry.Key.CanonicalKey))
                        .Append('=')
                        .Append(WorkloadCanonical.Boolean(entry.Manual))
                        .Append(';');
                }
                builder.Append(']');
            }

            if ((dimensions & WorkloadOwnershipDimensions.Schedules) != 0)
            {
                builder.Append("S[");
                for (int i = 0; i < safeState.Schedules.Count; i++)
                {
                    WorkloadScheduleEntry entry = safeState.Schedules[i];
                    builder.Append(WorkloadCanonical.Encode(entry.Pawn.Value))
                        .Append('=')
                        .Append(WorkloadCanonical.Integer(entry.Schedule.Value))
                        .Append(';');
                }
                builder.Append(']');
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOverrides) != 0)
            {
                builder.Append("O[");
                for (int i = 0; i < safeState.SpecificJobOverrides.Count; i++)
                {
                    WorkloadSpecificJobOverrideEntry entry = safeState.SpecificJobOverrides[i];
                    builder.Append(WorkloadCanonical.Encode(entry.Key.CanonicalKey))
                        .Append('=')
                        .Append(WorkloadCanonical.Encode(entry.Value.CanonicalValue))
                        .Append(';');
                }
                builder.Append(']');
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOrder) != 0)
            {
                builder.Append("R[");
                for (int i = 0; i < safeState.SpecificJobOrder.Count; i++)
                {
                    WorkloadSpecificJobOrderEntry entry = safeState.SpecificJobOrder[i];
                    builder.Append(WorkloadCanonical.Encode(entry.Key.CanonicalKey))
                        .Append('=')
                        .Append(WorkloadCanonical.Integer(entry.Order))
                        .Append(';');
                }
                builder.Append(']');
            }

            if ((dimensions & WorkloadOwnershipDimensions.PresentationSettings) != 0)
            {
                builder.Append("T[");
                for (int i = 0; i < safeState.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry = safeState.PresentationSettings[i];
                    builder.Append(WorkloadCanonical.Encode(entry.Key))
                        .Append('=')
                        .Append(WorkloadCanonical.Encode(entry.Value.CanonicalValue))
                        .Append(';');
                }
                builder.Append(']');
            }

            return builder.ToString();
        }
    }
}
