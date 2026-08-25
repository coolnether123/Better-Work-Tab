using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Better_Work_Tab.Features.Workloads.V2
{
    public sealed class WorkloadCatalog
    {
        private readonly ReadOnlyCollection<PawnKey> _pawnIds;
        private readonly ReadOnlyCollection<WorkTypeKey> _workTypeIds;
        private readonly ReadOnlyCollection<WorkGiverKey> _workGiverIds;
        private readonly ReadOnlyCollection<ScheduleKey> _scheduleKeys;
        private readonly HashSet<PawnKey> _pawnSet;
        private readonly HashSet<WorkTypeKey> _workTypeSet;
        private readonly HashSet<WorkGiverKey> _workGiverSet;
        private readonly HashSet<ScheduleKey> _scheduleSet;

        public WorkloadCatalog(
            IEnumerable<PawnKey> pawnIds = null,
            IEnumerable<WorkTypeKey> workTypeIds = null,
            IEnumerable<WorkGiverKey> workGiverIds = null,
            IEnumerable<ScheduleKey> scheduleKeys = null)
        {
            HasPawnIds = pawnIds != null;
            HasWorkTypeIds = workTypeIds != null;
            HasWorkGiverIds = workGiverIds != null;
            HasScheduleKeys = scheduleKeys != null;
            _pawnIds = Normalize(pawnIds, out _pawnSet);
            _workTypeIds = Normalize(workTypeIds, out _workTypeSet);
            _workGiverIds = Normalize(workGiverIds, out _workGiverSet);
            _scheduleKeys = Normalize(scheduleKeys, out _scheduleSet);
        }

        public static WorkloadCatalog Empty
        {
            get { return new WorkloadCatalog(); }
        }

        public bool HasPawnIds { get; private set; }
        public bool HasWorkTypeIds { get; private set; }
        public bool HasWorkGiverIds { get; private set; }
        public bool HasScheduleKeys { get; private set; }
        public IReadOnlyList<PawnKey> PawnIds => _pawnIds;
        public IReadOnlyList<WorkTypeKey> WorkTypeIds => _workTypeIds;
        public IReadOnlyList<WorkGiverKey> WorkGiverIds => _workGiverIds;
        public IReadOnlyList<ScheduleKey> ScheduleKeys => _scheduleKeys;

        public bool ContainsPawn(PawnKey key)
        {
            return _pawnSet.Contains(key ?? new PawnKey(null));
        }

        public bool ContainsWorkType(WorkTypeKey key)
        {
            return _workTypeSet.Contains(key ?? new WorkTypeKey(null));
        }

        public bool ContainsWorkGiver(WorkGiverKey key)
        {
            return _workGiverSet.Contains(key ?? new WorkGiverKey(null));
        }

        public bool ContainsSchedule(ScheduleKey key)
        {
            return _scheduleSet.Contains(key ?? new ScheduleKey(-1));
        }

        private static ReadOnlyCollection<T> Normalize<T>(IEnumerable<T> source, out HashSet<T> set)
        {
            set = new HashSet<T>();
            if (source != null)
            {
                foreach (T item in source)
                {
                    if (!ReferenceEquals(item, null)) set.Add(item);
                }
            }

            var result = new List<T>(set);
            result.Sort(CompareValues);
            return result.AsReadOnly();
        }

        private static int CompareValues<T>(T left, T right)
        {
            var leftComparable = left as IComparable<T>;
            if (leftComparable != null) return leftComparable.CompareTo(right);
            return StringComparer.Ordinal.Compare(Convert.ToString(left), Convert.ToString(right));
        }
    }

    public enum WorkloadValidationSeverity
    {
        Warning = 0,
        Error = 1
    }

    public enum WorkloadValidationCode
    {
        MissingTemplate = 0,
        MissingStableId = 1,
        MissingLabel = 2,
        InvalidSchemaVersion = 3,
        NewerSchema = 4,
        InvalidScopeMode = 5,
        MissingPawnId = 6,
        UnknownPawnId = 7,
        MissingWorkTypeId = 8,
        UnknownWorkTypeId = 9,
        MissingWorkGiverId = 10,
        UnknownWorkGiverId = 11,
        InvalidScheduleKey = 12,
        UnknownScheduleKey = 13,
        MissingPresentationSettingKey = 14,
        UnknownOwnershipDimension = 15,
        InvalidSpecificJobValue = 16,
        InvalidSpecificJobOrder = 17,
        ConflictingManualModes = 18,
        ScopeStateMismatch = 19,
        UnownedStateDimension = 20,
        InvalidManualModeScope = 21,
        InvalidIntentState = 22,
        InvalidTargetScope = 23,
        InvalidSchedulePayload = 24,
        InvalidOrderPayload = 25,
        InvalidSettingOwnership = 26,
        DuplicateSpecificJobIntent = 27,
        DuplicateSpecificJobOrderIntent = 28
    }

    public sealed class WorkloadValidationIssue
    {
        public WorkloadValidationIssue(
            WorkloadValidationSeverity severity,
            WorkloadValidationCode code,
            string path)
        {
            Severity = severity;
            Code = code;
            Path = path ?? string.Empty;
        }

        public WorkloadValidationSeverity Severity { get; private set; }
        public WorkloadValidationCode Code { get; private set; }
        public string Path { get; private set; }
    }

    public sealed class WorkloadValidationResult
    {
        private readonly ReadOnlyCollection<WorkloadValidationIssue> _issues;

        internal WorkloadValidationResult(IEnumerable<WorkloadValidationIssue> issues)
        {
            _issues = new List<WorkloadValidationIssue>(issues ?? new WorkloadValidationIssue[0]).AsReadOnly();
        }

        public IReadOnlyList<WorkloadValidationIssue> Issues => _issues;
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < _issues.Count; i++)
                {
                    if (_issues[i].Severity == WorkloadValidationSeverity.Error) return true;
                }

                return false;
            }
        }

        public bool IsNewerSchema
        {
            get { return Contains(WorkloadValidationCode.NewerSchema); }
        }

        public bool CanApply => !HasErrors && !IsNewerSchema;

        public bool Contains(WorkloadValidationCode code)
        {
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Code == code) return true;
            }

            return false;
        }
    }

    public sealed class WorkloadValidationContext
    {
        public WorkloadValidationContext(
            WorkloadCatalog catalog = null,
            int supportedSchemaVersion = WorkloadSchema.CurrentVersion)
        {
            Catalog = catalog ?? WorkloadCatalog.Empty;
            SupportedSchemaVersion = supportedSchemaVersion < 0 ? 0 : supportedSchemaVersion;
        }

        public static WorkloadValidationContext Default
        {
            get { return new WorkloadValidationContext(); }
        }

        public WorkloadCatalog Catalog { get; private set; }
        public int SupportedSchemaVersion { get; private set; }
    }

    public static class WorkloadValidator
    {
        public static WorkloadValidationResult Validate(
            WorkloadTemplate template,
            WorkloadValidationContext context = null)
        {
            var issues = new List<WorkloadValidationIssue>();
            if (template == null)
            {
                issues.Add(new WorkloadValidationIssue(
                    WorkloadValidationSeverity.Error,
                    WorkloadValidationCode.MissingTemplate,
                    "template"));
                return new WorkloadValidationResult(issues);
            }

            WorkloadValidationContext safeContext = context ?? WorkloadValidationContext.Default;
            WorkloadCatalog catalog = safeContext.Catalog ?? WorkloadCatalog.Empty;
            WorkloadDefinition definition = template.Definition ?? WorkloadDefinition.Empty;
            if (!definition.StableId.AnyNonWhitespace())
            {
                Add(issues, WorkloadValidationCode.MissingStableId, "definition.stableId");
            }

            if (!definition.Label.AnyNonWhitespace())
            {
                Add(issues, WorkloadValidationCode.MissingLabel, "definition.label");
            }

            if (definition.SchemaVersion <= 0)
            {
                Add(issues, WorkloadValidationCode.InvalidSchemaVersion, "definition.schemaVersion");
            }
            else if (definition.SchemaVersion > safeContext.SupportedSchemaVersion)
            {
                Add(issues, WorkloadValidationCode.NewerSchema, "definition.schemaVersion");
            }

            if (!definition.Scope.IsValidMode)
            {
                Add(issues, WorkloadValidationCode.InvalidScopeMode, "definition.scope.mode");
            }

            int knownOwnershipBits = (int)WorkloadOwnershipDimensions.All;
            if ((((int)definition.OwnershipDimensions) & ~knownOwnershipBits) != 0)
            {
                Add(issues, WorkloadValidationCode.UnknownOwnershipDimension, "definition.ownership");
            }

            if (definition.Scope.Mode == WorkloadScopeMode.CurrentMapFreeColonists &&
                definition.Scope.ExplicitPawnIds.Count > 0)
            {
                Add(
                    issues,
                    WorkloadValidationCode.InvalidScopeMode,
                    "definition.scope.explicitPawnIds");
            }

            for (int i = 0; i < definition.Scope.ExplicitPawnIds.Count; i++)
            {
                CheckPawn(issues, catalog, definition.Scope.ExplicitPawnIds[i], "definition.scope.explicitPawnIds[" + i + "]");
            }

            for (int i = 0; i < definition.Scope.ExcludedPawnIds.Count; i++)
            {
                CheckPawn(issues, catalog, definition.Scope.ExcludedPawnIds[i], "definition.scope.excludedPawnIds[" + i + "]");
            }

            WorkloadProjectedState state = template.ProjectedState ?? WorkloadProjectedState.Empty;
            bool hasManualSet = WorkloadManualModeSemantics.TryGetGlobalMode(
                state, out _, out _, out bool manualConflict);
            if (definition.OwnershipDimensions.Owns(WorkloadStateDimension.ManualModes) &&
                hasManualSet && (definition.Scope.Mode != WorkloadScopeMode.CurrentMapFreeColonists ||
                    definition.Scope.ExcludedPawnIds.Count > 0))
            {
                Add(
                    issues,
                    WorkloadValidationCode.InvalidManualModeScope,
                    "definition.scope");
            }

            if (state.HasAmbiguousSpecificPriorityIntents)
            {
                Add(
                    issues,
                    WorkloadValidationCode.DuplicateSpecificJobIntent,
                    "state.specificPriorityIntents");
            }

            if (state.HasAmbiguousWorkTypeOrderIntents)
            {
                Add(
                    issues,
                    WorkloadValidationCode.DuplicateSpecificJobOrderIntent,
                    "state.workTypeOrderIntents");
            }

            CheckOwnedDimension(
                issues,
                definition.OwnershipDimensions,
                WorkloadStateDimension.ParentPriorities,
                state.ParentPriorities.Count + state.ParentPriorityIntents.Count,
                "state.parentPriorities");
            CheckOwnedDimension(
                issues,
                definition.OwnershipDimensions,
                WorkloadStateDimension.ManualModes,
                state.ManualModes.Count + state.ManualModeIntents.Count,
                "state.manualModes");
            CheckOwnedDimension(
                issues,
                definition.OwnershipDimensions,
                WorkloadStateDimension.Schedules,
                state.Schedules.Count + state.ScheduleIntents.Count,
                "state.schedules");
            CheckOwnedDimension(
                issues,
                definition.OwnershipDimensions,
                WorkloadStateDimension.SpecificJobOverrides,
                state.SpecificJobOverrides.Count + state.SpecificPriorityIntents.Count,
                "state.specificJobOverrides");
            CheckOwnedDimension(
                issues,
                definition.OwnershipDimensions,
                WorkloadStateDimension.SpecificJobOrder,
                state.SpecificJobOrder.Count + state.WorkTypeOrderIntents.Count,
                "state.specificJobOrder");
            CheckOwnedDimension(
                issues,
                definition.OwnershipDimensions,
                WorkloadStateDimension.PresentationSettings,
                state.PresentationSettings.Count + state.PresentationSettingIntents.Count,
                "state.presentationSettings");

            for (int i = 0; i < state.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = state.ParentPriorities[i];
                CheckPawn(issues, catalog, entry.Key.Pawn, "state.parentPriorities[" + i + "].pawn");
                CheckWorkType(issues, catalog, entry.Key.WorkType, "state.parentPriorities[" + i + "].workType");
                CheckScopePawn(issues, definition.Scope, entry.Key.Pawn, "state.parentPriorities[" + i + "].pawn");
            }

            for (int i = 0; i < state.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = state.ManualModes[i];
                CheckPawn(issues, catalog, entry.Key.Pawn, "state.manualModes[" + i + "].pawn");
                CheckWorkType(issues, catalog, entry.Key.WorkType, "state.manualModes[" + i + "].workType");
                CheckScopePawn(issues, definition.Scope, entry.Key.Pawn, "state.manualModes[" + i + "].pawn");
            }

            for (int i = 0; i < state.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = state.Schedules[i];
                CheckPawn(issues, catalog, entry.Pawn, "state.schedules[" + i + "].pawn");
                CheckScopePawn(issues, definition.Scope, entry.Pawn, "state.schedules[" + i + "].pawn");
                if (entry.Schedule == null || !entry.Schedule.IsValid)
                {
                    Add(issues, WorkloadValidationCode.InvalidScheduleKey, "state.schedules[" + i + "].schedule");
                }
                else if (catalog.HasScheduleKeys && !catalog.ContainsSchedule(entry.Schedule))
                {
                    Add(issues, WorkloadValidationCode.UnknownScheduleKey, "state.schedules[" + i + "].schedule");
                }
            }

            for (int i = 0; i < state.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = state.SpecificJobOverrides[i];
                CheckSpecificJob(issues, catalog, entry.Key, "state.specificJobOverrides[" + i + "]");
                CheckScopePawn(issues, definition.Scope, entry.Key.Pawn, "state.specificJobOverrides[" + i + "].pawn");
                if (entry.Value.Kind != WorkloadScalarKind.Integer)
                {
                    Add(
                        issues,
                        WorkloadValidationCode.InvalidSpecificJobValue,
                        "state.specificJobOverrides[" + i + "].value");
                }
            }

            var specificOrderValues = new Dictionary<WorkloadParentPriorityKey, HashSet<int>>();
            for (int i = 0; i < state.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = state.SpecificJobOrder[i];
                CheckSpecificJob(issues, catalog, entry.Key, "state.specificJobOrder[" + i + "]");
                CheckScopePawn(issues, definition.Scope, entry.Key.Pawn, "state.specificJobOrder[" + i + "].pawn");
                if (entry.Order < 0)
                {
                    Add(
                        issues,
                        WorkloadValidationCode.InvalidSpecificJobOrder,
                        "state.specificJobOrder[" + i + "].order");
                }

                WorkloadParentPriorityKey parent =
                    new WorkloadParentPriorityKey(entry.Key.Pawn, entry.Key.WorkType);
                if (!specificOrderValues.TryGetValue(parent, out HashSet<int> orders))
                {
                    orders = new HashSet<int>();
                    specificOrderValues[parent] = orders;
                }

                if (!orders.Add(entry.Order))
                {
                    Add(
                        issues,
                        WorkloadValidationCode.InvalidSpecificJobOrder,
                        "state.specificJobOrder[" + i + "].order");
                }
            }

            for (int i = 0; i < state.PresentationSettings.Count; i++)
            {
                if (!state.PresentationSettings[i].Key.AnyNonWhitespace())
                {
                    Add(issues, WorkloadValidationCode.MissingPresentationSettingKey, "state.presentationSettings[" + i + "].key");
                }
            }

            for (int i = 0; i < state.ParentPriorityIntents.Count; i++)
            {
                WorkloadParentPriorityIntentEntry entry = state.ParentPriorityIntents[i];
                CheckPawn(issues, catalog, entry.Key.Pawn, "state.parentPriorityIntents[" + i + "].pawn");
                CheckWorkType(issues, catalog, entry.Key.WorkType, "state.parentPriorityIntents[" + i + "].workType");
                CheckScopePawn(issues, definition.Scope, entry.Key.Pawn, "state.parentPriorityIntents[" + i + "].pawn");
                CheckIntentState(issues, entry.Intent.State, "state.parentPriorityIntents[" + i + "].intent");
                if (entry.Intent.HasValue && !entry.Intent.Value.IsValid)
                {
                    Add(issues, WorkloadValidationCode.InvalidSpecificJobValue, "state.parentPriorityIntents[" + i + "].value");
                }
            }

            for (int i = 0; i < state.ManualModeIntents.Count; i++)
            {
                WorkloadManualModeIntentEntry entry = state.ManualModeIntents[i];
                CheckPawn(issues, catalog, entry.Key.Pawn, "state.manualModeIntents[" + i + "].pawn");
                CheckWorkType(issues, catalog, entry.Key.WorkType, "state.manualModeIntents[" + i + "].workType");
                CheckScopePawn(issues, definition.Scope, entry.Key.Pawn, "state.manualModeIntents[" + i + "].pawn");
                CheckIntentState(issues, entry.Intent.State, "state.manualModeIntents[" + i + "].intent");
            }

            if (manualConflict)
            {
                Add(
                    issues,
                    WorkloadValidationCode.ConflictingManualModes,
                    "state.manualModes");
            }

            for (int i = 0; i < state.ScheduleIntents.Count; i++)
            {
                WorkloadScheduleIntentEntry entry = state.ScheduleIntents[i];
                CheckScheduleTarget(issues, catalog, definition.Scope, entry.Key, "state.scheduleIntents[" + i + "]");
                CheckIntentState(issues, entry.Intent.State, "state.scheduleIntents[" + i + "].intent");
                if (entry.Intent.HasValue && !entry.Intent.Value.IsValid)
                {
                    Add(issues, WorkloadValidationCode.InvalidSchedulePayload, "state.scheduleIntents[" + i + "].value");
                }
            }

            for (int i = 0; i < state.SpecificPriorityIntents.Count; i++)
            {
                WorkloadSpecificPriorityIntentEntry entry = state.SpecificPriorityIntents[i];
                CheckSpecificTarget(issues, catalog, definition.Scope, entry.Key, "state.specificPriorityIntents[" + i + "]");
                CheckIntentState(issues, entry.Intent.State, "state.specificPriorityIntents[" + i + "].intent");
                if (entry.Intent.HasValue && !entry.Intent.Value.IsValid)
                {
                    Add(issues, WorkloadValidationCode.InvalidSpecificJobValue, "state.specificPriorityIntents[" + i + "].value");
                }
            }

            for (int i = 0; i < state.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadWorkTypeOrderIntentEntry entry = state.WorkTypeOrderIntents[i];
                CheckOrderTarget(issues, catalog, definition.Scope, entry.Key, "state.workTypeOrderIntents[" + i + "]");
                CheckIntentState(issues, entry.Intent.State, "state.workTypeOrderIntents[" + i + "].intent");
                if (entry.Intent.HasValue && !entry.Intent.Value.IsValid)
                {
                    Add(issues, WorkloadValidationCode.InvalidOrderPayload, "state.workTypeOrderIntents[" + i + "].value");
                }
                if (entry.Intent.HasValue && catalog.HasWorkGiverIds)
                {
                    for (int orderIndex = 0;
                         orderIndex < entry.Intent.Value.OrderedWorkGivers.Count;
                         orderIndex++)
                    {
                        if (!catalog.ContainsWorkGiver(entry.Intent.Value.OrderedWorkGivers[orderIndex]))
                        {
                            Add(
                                issues,
                                WorkloadValidationCode.UnknownWorkGiverId,
                                "state.workTypeOrderIntents[" + i + "].value[" + orderIndex + "]");
                        }
                    }
                }
            }

            for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry = state.PresentationSettingIntents[i];
                if (!entry.Key.AnyNonWhitespace())
                {
                    Add(issues, WorkloadValidationCode.MissingPresentationSettingKey, "state.presentationSettingIntents[" + i + "].key");
                }
                CheckIntentState(issues, entry.Intent.State, "state.presentationSettingIntents[" + i + "].intent");
                if (entry.Intent.HasValue && !entry.Intent.Value.IsValid)
                {
                    Add(issues, WorkloadValidationCode.InvalidSettingOwnership, "state.presentationSettingIntents[" + i + "].ownership");
                }
            }

            return new WorkloadValidationResult(issues);
        }

        private static void CheckIntentState(
            List<WorkloadValidationIssue> issues,
            WorkloadIntentState state,
            string path)
        {
            if (state < WorkloadIntentState.NoOpinion || state > WorkloadIntentState.Clear)
            {
                Add(issues, WorkloadValidationCode.InvalidIntentState, path);
            }
        }

        private static void CheckScheduleTarget(
            List<WorkloadValidationIssue> issues,
            WorkloadCatalog catalog,
            WorkloadScope scope,
            WorkloadScheduleTargetKey key,
            string path)
        {
            WorkloadScheduleTargetKey safeKey = key ?? new WorkloadScheduleTargetKey(
                WorkloadTargetScope.PawnLocal,
                null,
                WorkloadScheduleTargetKind.ParentWorkType,
                null);
            if (!safeKey.IsValid)
            {
                Add(issues, WorkloadValidationCode.InvalidTargetScope, path);
                return;
            }

            if (safeKey.IsGlobal)
            {
                CheckWorkType(issues, catalog, safeKey.WorkType, path + ".workType");
                CheckWorkGiver(issues, catalog, safeKey.WorkGiver, path + ".workGiver");
                return;
            }

            CheckPawn(issues, catalog, safeKey.Pawn, path + ".pawn");
            CheckWorkType(issues, catalog, safeKey.WorkType, path + ".workType");
            CheckScopePawn(issues, scope, safeKey.Pawn, path + ".pawn");
            if (safeKey.TargetKind == WorkloadScheduleTargetKind.WorkGiver)
            {
                CheckWorkGiver(issues, catalog, safeKey.WorkGiver, path + ".workGiver");
            }
        }

        private static void CheckSpecificTarget(
            List<WorkloadValidationIssue> issues,
            WorkloadCatalog catalog,
            WorkloadScope scope,
            WorkloadSpecificJobTargetKey key,
            string path)
        {
            WorkloadSpecificJobTargetKey safeKey = key ?? new WorkloadSpecificJobTargetKey(
                WorkloadTargetScope.PawnLocal,
                null,
                null,
                null);
            if (!safeKey.IsValid)
            {
                Add(issues, WorkloadValidationCode.InvalidTargetScope, path);
                return;
            }

            if (!safeKey.IsGlobal)
            {
                CheckPawn(issues, catalog, safeKey.Pawn, path + ".pawn");
                CheckScopePawn(issues, scope, safeKey.Pawn, path + ".pawn");
            }
            CheckWorkType(issues, catalog, safeKey.WorkType, path + ".workType");
            CheckWorkGiver(issues, catalog, safeKey.WorkGiver, path + ".workGiver");
        }

        private static void CheckOrderTarget(
            List<WorkloadValidationIssue> issues,
            WorkloadCatalog catalog,
            WorkloadScope scope,
            WorkloadWorkTypeOrderKey key,
            string path)
        {
            WorkloadWorkTypeOrderKey safeKey = key ?? new WorkloadWorkTypeOrderKey(
                WorkloadTargetScope.PawnLocal,
                null,
                null);
            if (!safeKey.IsValid)
            {
                Add(issues, WorkloadValidationCode.InvalidTargetScope, path);
                return;
            }

            if (!safeKey.IsGlobal)
            {
                CheckPawn(issues, catalog, safeKey.Pawn, path + ".pawn");
                CheckScopePawn(issues, scope, safeKey.Pawn, path + ".pawn");
            }
            CheckWorkType(issues, catalog, safeKey.WorkType, path + ".workType");
        }

        private static void CheckSpecificJob(
            List<WorkloadValidationIssue> issues,
            WorkloadCatalog catalog,
            WorkloadSpecificJobKey key,
            string path)
        {
            WorkloadSpecificJobKey safeKey = key ?? new WorkloadSpecificJobKey(null, null, null);
            CheckPawn(issues, catalog, safeKey.Pawn, path + ".pawn");
            CheckWorkType(issues, catalog, safeKey.WorkType, path + ".workType");
            CheckWorkGiver(issues, catalog, safeKey.WorkGiver, path + ".workGiver");
        }

        private static void CheckOwnedDimension(
            List<WorkloadValidationIssue> issues,
            WorkloadOwnershipDimensions ownership,
            WorkloadStateDimension dimension,
            int count,
            string path)
        {
            if (count > 0 && !ownership.Owns(dimension))
            {
                Add(
                    issues,
                    WorkloadValidationCode.UnownedStateDimension,
                    path);
            }
        }

        private static void CheckScopePawn(
            List<WorkloadValidationIssue> issues,
            WorkloadScope scope,
            PawnKey pawn,
            string path)
        {
            if (scope == null || pawn == null || !pawn.IsValid || scope.IsExplicitlyExcluded(pawn))
            {
                return;
            }

            if (scope.Mode == WorkloadScopeMode.ExplicitPawnIds &&
                !Contains(scope.ExplicitPawnIds, pawn))
            {
                Add(
                    issues,
                    WorkloadValidationCode.ScopeStateMismatch,
                    path);
            }
        }

        private static bool Contains(IReadOnlyList<PawnKey> values, PawnKey candidate)
        {
            if (values == null || candidate == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Equals(candidate)) return true;
            }

            return false;
        }

        private static void CheckPawn(
            List<WorkloadValidationIssue> issues,
            WorkloadCatalog catalog,
            PawnKey key,
            string path)
        {
            if (key == null || !key.IsValid)
            {
                Add(issues, WorkloadValidationCode.MissingPawnId, path);
            }
            else if (catalog.HasPawnIds && !catalog.ContainsPawn(key))
            {
                Add(issues, WorkloadValidationCode.UnknownPawnId, path);
            }
        }

        private static void CheckWorkType(
            List<WorkloadValidationIssue> issues,
            WorkloadCatalog catalog,
            WorkTypeKey key,
            string path)
        {
            if (key == null || !key.IsValid)
            {
                Add(issues, WorkloadValidationCode.MissingWorkTypeId, path);
            }
            else if (catalog.HasWorkTypeIds && !catalog.ContainsWorkType(key))
            {
                Add(issues, WorkloadValidationCode.UnknownWorkTypeId, path);
            }
        }

        private static void CheckWorkGiver(
            List<WorkloadValidationIssue> issues,
            WorkloadCatalog catalog,
            WorkGiverKey key,
            string path)
        {
            if (key == null || !key.IsValid)
            {
                Add(issues, WorkloadValidationCode.MissingWorkGiverId, path);
            }
            else if (catalog.HasWorkGiverIds && !catalog.ContainsWorkGiver(key))
            {
                Add(issues, WorkloadValidationCode.UnknownWorkGiverId, path);
            }
        }

        private static void Add(
            List<WorkloadValidationIssue> issues,
            WorkloadValidationCode code,
            string path)
        {
            issues.Add(new WorkloadValidationIssue(WorkloadValidationSeverity.Error, code, path));
        }
    }

    internal static class WorkloadValidationTextExtensions
    {
        public static bool AnyNonWhitespace(this string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i])) return true;
            }

            return false;
        }
    }
}
