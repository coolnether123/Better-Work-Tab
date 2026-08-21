using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    /// <summary>
    /// The only boundary that translates Scribe-shaped V2 records into the
    /// side-effect-free Workload V2 model. Runtime records use defName and
    /// thingIDNumber strings; they never retain Pawn, WorkTypeDef, or
    /// WorkGiverDef references.
    /// </summary>
    public static class WorkloadV2RecordConverter
    {
        public static WorkloadOperationResult<WorkloadTemplate> TryToTemplate(
            WorkloadV2PersistenceRecord record,
            bool readOnlyDocument = false)
        {
            if (record == null)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "The workload record is missing.");
            }

            if (string.IsNullOrWhiteSpace(record.StableId))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.MissingStableId,
                    "The workload record has no stable ID.");
            }

            string migrationError;
            if (!WorkloadV2Migration.TryMigrateRecord(record, out migrationError))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.ReadOnlyDiagnostic,
                    migrationError);
            }

            WorkloadV2SchemaState schemaState =
                WorkloadV2SchemaPolicy.Classify(record.SchemaVersion);
            switch (schemaState)
            {
                case WorkloadV2SchemaState.Missing:
                    return WorkloadOperationResult<WorkloadTemplate>.Fail(
                        WorkloadDiagnosticCode.UnsupportedSchema,
                        "The workload record schema version is missing; an explicit migration is required.");
                case WorkloadV2SchemaState.KnownOld:
                    return WorkloadOperationResult<WorkloadTemplate>.Fail(
                        WorkloadDiagnosticCode.UnsupportedSchema,
                        "The workload record uses an older schema with no registered migration.");
                case WorkloadV2SchemaState.Newer:
                    return WorkloadOperationResult<WorkloadTemplate>.Fail(
                        WorkloadDiagnosticCode.NewerSchema,
                        "The workload record uses a newer schema and is read-only.");
                case WorkloadV2SchemaState.Unsupported:
                    return WorkloadOperationResult<WorkloadTemplate>.Fail(
                        WorkloadDiagnosticCode.UnsupportedSchema,
                        "The workload record schema version is unsupported.");
            }

            if (readOnlyDocument)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.ReadOnlyDiagnostic,
                    "The workload is read-only for diagnostics.");
            }

            record.EnsureCollections();
            record.NormalizeStableState();

            string duplicateSpecificTargetError;
            if (HasDuplicateTypedSpecificTargets(record, out duplicateSpecificTargetError))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    duplicateSpecificTargetError);
            }

            if (record.LegacyScheduleRequiresReview || record.LegacyOrderRequiresReview)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.ReadOnlyDiagnostic,
                    string.IsNullOrWhiteSpace(record.MigrationDiagnostic)
                        ? "The workload contains legacy schedule or order data that cannot be losslessly converted."
                        : record.MigrationDiagnostic);
            }

            int knownOwnershipBits = (int)WorkloadOwnershipDimensions.All;
            if ((record.OwnershipDimensions & ~knownOwnershipBits) != 0)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.UnsupportedSchema,
                    "The workload declares an unknown ownership dimension.");
            }

            if (!TryReadScope(record, out WorkloadScope scope, out string scopeError))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.InvalidScopeMode,
                    scopeError);
            }

            var priorities = new List<WorkloadParentPriorityEntry>();
            for (int i = 0; i < record.ParentPriorities.Count; i++)
            {
                WorkloadV2ParentPriorityRecord value = record.ParentPriorities[i];
                if (value == null || !HasIdentity(value.PawnId, value.WorkTypeDefName))
                {
                    return InvalidState("A parent-priority record has a missing pawn or work type identity.");
                }

                priorities.Add(new WorkloadParentPriorityEntry(
                    new PawnKey(value.PawnId),
                    new WorkTypeKey(value.WorkTypeDefName),
                    value.Priority));
            }

            var manualModes = new List<WorkloadManualModeEntry>();
            for (int i = 0; i < record.ManualModes.Count; i++)
            {
                WorkloadV2ManualModeRecord value = record.ManualModes[i];
                if (value == null || !HasIdentity(value.PawnId, value.WorkTypeDefName))
                {
                    return InvalidState("A manual-mode record has a missing pawn or work type identity.");
                }

                manualModes.Add(new WorkloadManualModeEntry(
                    new PawnKey(value.PawnId),
                    new WorkTypeKey(value.WorkTypeDefName),
                    value.Manual));
            }

            var schedules = new List<WorkloadScheduleEntry>();
            for (int i = 0; i < record.Schedules.Count; i++)
            {
                WorkloadV2ScheduleRecord value = record.Schedules[i];
                if (value == null || string.IsNullOrWhiteSpace(value.PawnId) || value.Schedule < 0)
                {
                    return InvalidState("A schedule record has a missing pawn identity or invalid schedule.");
                }

                schedules.Add(new WorkloadScheduleEntry(value.PawnId, value.Schedule));
            }

            var overrides = new List<WorkloadSpecificJobOverrideEntry>();
            for (int i = 0; i < record.SpecificJobOverrides.Count; i++)
            {
                WorkloadV2SpecificJobOverrideRecord value = record.SpecificJobOverrides[i];
                if (value == null || !HasIdentity(value.PawnId, value.WorkTypeDefName, value.WorkGiverDefName))
                {
                    return InvalidState("A specific-job override has a missing identity.");
                }

                if (!TryReadScalar(value.Value, out WorkloadScalarValue scalar, out string scalarError))
                {
                    return InvalidState(scalarError);
                }

                overrides.Add(new WorkloadSpecificJobOverrideEntry(
                    new PawnKey(value.PawnId),
                    new WorkTypeKey(value.WorkTypeDefName),
                    new WorkGiverKey(value.WorkGiverDefName),
                    scalar));
            }

            var order = new List<WorkloadSpecificJobOrderEntry>();
            for (int i = 0; i < record.SpecificJobOrder.Count; i++)
            {
                WorkloadV2SpecificJobOrderRecord value = record.SpecificJobOrder[i];
                if (value == null || !HasIdentity(value.PawnId, value.WorkTypeDefName, value.WorkGiverDefName))
                {
                    return InvalidState("A specific-job order record has a missing identity.");
                }

                order.Add(new WorkloadSpecificJobOrderEntry(
                    new PawnKey(value.PawnId),
                    new WorkTypeKey(value.WorkTypeDefName),
                    new WorkGiverKey(value.WorkGiverDefName),
                    value.Order));
            }

            var presentation = new List<WorkloadPresentationSettingEntry>();
            for (int i = 0; i < record.PresentationSettings.Count; i++)
            {
                WorkloadV2PresentationSettingRecord value = record.PresentationSettings[i];
                if (value == null || string.IsNullOrWhiteSpace(value.Key))
                {
                    return InvalidState("A presentation setting has no key.");
                }

                if (!TryReadScalar(value.Value, out WorkloadScalarValue scalar, out string scalarError))
                {
                    return InvalidState(scalarError);
                }

                presentation.Add(new WorkloadPresentationSettingEntry(value.Key, scalar));
            }

            var parentPriorityIntents = new List<WorkloadParentPriorityIntentEntry>();
            for (int i = 0; i < record.ParentPriorityIntents.Count; i++)
            {
                WorkloadV2ParentPriorityIntentRecord value = record.ParentPriorityIntents[i];
                if (value == null || !HasIdentity(value.PawnId, value.WorkTypeDefName))
                {
                    return InvalidState("A typed parent-priority intent has a missing identity.");
                }

                if (!TryReadPriorityIntent(
                    value.IntentState,
                    value.Priority,
                    out WorkloadIntent<WorkloadSpecificPriorityPayload> intent,
                    out string intentError))
                {
                    return InvalidState(intentError);
                }

                parentPriorityIntents.Add(new WorkloadParentPriorityIntentEntry(
                    new WorkloadParentPriorityKey(new PawnKey(value.PawnId), new WorkTypeKey(value.WorkTypeDefName)),
                    intent));
            }

            var manualModeIntents = new List<WorkloadManualModeIntentEntry>();
            for (int i = 0; i < record.ManualModeIntents.Count; i++)
            {
                WorkloadV2ManualModeIntentRecord value = record.ManualModeIntents[i];
                if (value == null || !HasIdentity(value.PawnId, value.WorkTypeDefName))
                {
                    return InvalidState("A typed manual-mode intent has a missing identity.");
                }

                if (!TryReadManualIntent(
                    value.IntentState,
                    value.Manual,
                    out WorkloadIntent<bool> intent,
                    out string intentError))
                {
                    return InvalidState(intentError);
                }

                manualModeIntents.Add(new WorkloadManualModeIntentEntry(
                    new WorkloadParentPriorityKey(new PawnKey(value.PawnId), new WorkTypeKey(value.WorkTypeDefName)),
                    intent));
            }

            var scheduleIntents = new List<WorkloadScheduleIntentEntry>();
            for (int i = 0; i < record.ScheduleIntents.Count; i++)
            {
                WorkloadV2ScheduleIntentRecord value = record.ScheduleIntents[i];
                if (!TryReadScheduleIntent(value, out WorkloadScheduleIntentEntry intent, out string intentError))
                {
                    return InvalidState(intentError);
                }

                scheduleIntents.Add(intent);
            }

            var specificPriorityIntents = new List<WorkloadSpecificPriorityIntentEntry>();
            for (int i = 0; i < record.SpecificPriorityIntents.Count; i++)
            {
                WorkloadV2SpecificPriorityIntentRecord value = record.SpecificPriorityIntents[i];
                if (!TryReadSpecificPriorityIntent(
                    value,
                    out WorkloadSpecificPriorityIntentEntry intent,
                    out string intentError))
                {
                    return InvalidState(intentError);
                }

                if (!intent.Intent.IsNoOpinion)
                {
                    specificPriorityIntents.Add(intent);
                }
            }

            var workTypeOrderIntents = new List<WorkloadWorkTypeOrderIntentEntry>();
            for (int i = 0; i < record.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadV2WorkTypeOrderIntentRecord value = record.WorkTypeOrderIntents[i];
                if (!TryReadWorkTypeOrderIntent(
                    value,
                    out WorkloadWorkTypeOrderIntentEntry intent,
                    out string intentError))
                {
                    return InvalidState(intentError);
                }

                if (!intent.Intent.IsNoOpinion)
                {
                    workTypeOrderIntents.Add(intent);
                }
            }

            var presentationSettingIntents = new List<WorkloadPresentationSettingIntentEntry>();
            for (int i = 0; i < record.PresentationSettingIntents.Count; i++)
            {
                WorkloadV2PresentationSettingIntentRecord value = record.PresentationSettingIntents[i];
                if (!TryReadPresentationSettingIntent(
                    value,
                    out WorkloadPresentationSettingIntentEntry intent,
                    out string intentError))
                {
                    return InvalidState(intentError);
                }

                presentationSettingIntents.Add(intent);
            }

            var definition = new WorkloadDefinition(
                record.StableId,
                record.Label ?? string.Empty,
                record.SchemaVersion,
                (WorkloadOwnershipDimensions)record.OwnershipDimensions,
                scope);
            var state = new WorkloadProjectedState(
                priorities,
                manualModes,
                schedules,
                overrides,
                order,
                presentation,
                parentPriorityIntents: parentPriorityIntents,
                manualModeIntents: manualModeIntents,
                scheduleIntents: scheduleIntents,
                specificPriorityIntents: specificPriorityIntents,
                workTypeOrderIntents: workTypeOrderIntents,
                presentationSettingIntents: presentationSettingIntents);
            var template = new WorkloadTemplate(definition, state);
            WorkloadValidationResult validation = WorkloadValidator.Validate(template);
            if (validation.HasErrors)
            {
                return InvalidState(GetFirstValidationMessage(validation));
            }

            return WorkloadOperationResult<WorkloadTemplate>.Ok(template);
        }

        public static WorkloadOperationResult<WorkloadV2PersistenceRecord> TryFromTemplate(
            WorkloadTemplate template)
        {
            if (template == null)
            {
                return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "The workload template is missing.");
            }

            WorkloadValidationResult validation = WorkloadValidator.Validate(template);
            if (!validation.CanApply)
            {
                WorkloadDiagnosticCode code = validation.IsNewerSchema
                    ? WorkloadDiagnosticCode.NewerSchema
                    : WorkloadDiagnosticCode.InvalidState;
                return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    code,
                    GetFirstValidationMessage(validation));
            }

            WorkloadDefinition definition = template.Definition;
            WorkloadScope scope = definition.Scope ?? WorkloadScope.Empty;
            var record = new WorkloadV2PersistenceRecord
            {
                StableId = definition.StableId,
                Label = definition.Label,
                SchemaVersion = definition.SchemaVersion,
                OwnershipDimensions = (int)definition.OwnershipDimensions,
                ScopeMode = (int)scope.Mode,
                ExplicitPawnIds = ToPawnIds(scope.ExplicitPawnIds),
                ExcludedPawnIds = ToPawnIds(scope.ExcludedPawnIds)
            };

            WorkloadProjectedState state = template.ProjectedState ?? WorkloadProjectedState.Empty;
            for (int i = 0; i < state.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry value = state.ParentPriorities[i];
                record.ParentPriorities.Add(new WorkloadV2ParentPriorityRecord
                {
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    Priority = value.Priority
                });
            }

            for (int i = 0; i < state.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry value = state.ManualModes[i];
                record.ManualModes.Add(new WorkloadV2ManualModeRecord
                {
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    Manual = value.Manual
                });
            }

            for (int i = 0; i < state.Schedules.Count; i++)
            {
                WorkloadScheduleEntry value = state.Schedules[i];
                record.Schedules.Add(new WorkloadV2ScheduleRecord
                {
                    PawnId = value.Pawn.Value,
                    Schedule = value.Schedule.Value
                });
            }

            for (int i = 0; i < state.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry value = state.SpecificJobOverrides[i];
                record.SpecificJobOverrides.Add(new WorkloadV2SpecificJobOverrideRecord
                {
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    WorkGiverDefName = value.Key.WorkGiver.Value,
                    Value = ToScalarRecord(value.Value)
                });
            }

            for (int i = 0; i < state.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry value = state.SpecificJobOrder[i];
                record.SpecificJobOrder.Add(new WorkloadV2SpecificJobOrderRecord
                {
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    WorkGiverDefName = value.Key.WorkGiver.Value,
                    Order = value.Order
                });
            }

            for (int i = 0; i < state.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry value = state.PresentationSettings[i];
                record.PresentationSettings.Add(new WorkloadV2PresentationSettingRecord
                {
                    Key = value.Key,
                    Value = ToScalarRecord(value.Value)
                });
            }

            for (int i = 0; i < state.ParentPriorityIntents.Count; i++)
            {
                WorkloadParentPriorityIntentEntry value = state.ParentPriorityIntents[i];
                record.ParentPriorityIntents.Add(new WorkloadV2ParentPriorityIntentRecord
                {
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    IntentState = (int)value.Intent.State,
                    Priority = value.Intent.HasValue ? value.Intent.Value.Priority : 0
                });
            }

            for (int i = 0; i < state.ManualModeIntents.Count; i++)
            {
                WorkloadManualModeIntentEntry value = state.ManualModeIntents[i];
                record.ManualModeIntents.Add(new WorkloadV2ManualModeIntentRecord
                {
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    IntentState = (int)value.Intent.State,
                    Manual = value.Intent.HasValue && value.Intent.Value
                });
            }

            for (int i = 0; i < state.ScheduleIntents.Count; i++)
            {
                WorkloadScheduleIntentEntry value = state.ScheduleIntents[i];
                var scheduleRecord = new WorkloadV2ScheduleIntentRecord
                {
                    Scope = (int)value.Key.Scope,
                    PawnId = value.Key.Pawn.Value,
                    TargetKind = (int)value.Key.TargetKind,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    WorkGiverDefName = value.Key.WorkGiver.Value,
                    IntentState = (int)value.Intent.State,
                    PinnedHourMask = value.Intent.HasValue ? value.Intent.Value.PinnedHourMask : 0
                };
                if (value.Intent.HasValue)
                {
                    scheduleRecord.Priorities.AddRange(value.Intent.Value.Priorities);
                }
                record.ScheduleIntents.Add(scheduleRecord);
            }

            for (int i = 0; i < state.SpecificPriorityIntents.Count; i++)
            {
                WorkloadSpecificPriorityIntentEntry value = state.SpecificPriorityIntents[i];
                record.SpecificPriorityIntents.Add(new WorkloadV2SpecificPriorityIntentRecord
                {
                    Scope = (int)value.Key.Scope,
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    WorkGiverDefName = value.Key.WorkGiver.Value,
                    IntentState = (int)value.Intent.State,
                    Priority = value.Intent.HasValue ? value.Intent.Value.Priority : 0
                });
            }

            for (int i = 0; i < state.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadWorkTypeOrderIntentEntry value = state.WorkTypeOrderIntents[i];
                var orderRecord = new WorkloadV2WorkTypeOrderIntentRecord
                {
                    Scope = (int)value.Key.Scope,
                    PawnId = value.Key.Pawn.Value,
                    WorkTypeDefName = value.Key.WorkType.Value,
                    IntentState = (int)value.Intent.State,
                    IsComplete = value.Intent.HasValue && value.Intent.Value.IsComplete
                };
                if (value.Intent.HasValue)
                {
                    for (int orderIndex = 0;
                         orderIndex < value.Intent.Value.OrderedWorkGivers.Count;
                         orderIndex++)
                    {
                        orderRecord.OrderedWorkGiverDefNames.Add(
                            value.Intent.Value.OrderedWorkGivers[orderIndex].Value);
                    }
                }
                record.WorkTypeOrderIntents.Add(orderRecord);
            }

            for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry value = state.PresentationSettingIntents[i];
                record.PresentationSettingIntents.Add(new WorkloadV2PresentationSettingIntentRecord
                {
                    Key = value.Key,
                    IntentState = (int)value.Intent.State,
                    Ownership = value.Intent.HasValue
                        ? (int)value.Intent.Value.Ownership
                        : (int)WorkloadSettingOwnership.WorkloadOwned,
                    Value = value.Intent.HasValue
                        ? ToScalarRecord(value.Intent.Value.Scalar)
                        : new WorkloadV2ScalarRecord()
                });
            }

            record.NormalizeStableState();
            return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Ok(record);
        }

        private static bool TryReadPriorityIntent(
            int stateValue,
            int priority,
            out WorkloadIntent<WorkloadSpecificPriorityPayload> intent,
            out string error)
        {
            intent = WorkloadIntent<WorkloadSpecificPriorityPayload>.NoOpinion;
            if (!TryReadIntentState(stateValue, out WorkloadIntentState state, out error)) return false;
            switch (state)
            {
                case WorkloadIntentState.NoOpinion:
                    intent = WorkloadIntent<WorkloadSpecificPriorityPayload>.NoOpinion;
                    return true;
                case WorkloadIntentState.Clear:
                    intent = WorkloadIntent<WorkloadSpecificPriorityPayload>.Clear;
                    return true;
                case WorkloadIntentState.Set:
                    var payload = new WorkloadSpecificPriorityPayload(priority);
                    if (!payload.IsValid)
                    {
                        error = "A typed priority intent has an invalid priority.";
                        return false;
                    }

                    intent = WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(payload);
                    return true;
                default:
                    error = "A typed priority intent uses an unknown state.";
                    return false;
            }
        }

        private static bool TryReadManualIntent(
            int stateValue,
            bool manual,
            out WorkloadIntent<bool> intent,
            out string error)
        {
            intent = WorkloadIntent<bool>.NoOpinion;
            if (!TryReadIntentState(stateValue, out WorkloadIntentState state, out error)) return false;
            switch (state)
            {
                case WorkloadIntentState.NoOpinion:
                    intent = WorkloadIntent<bool>.NoOpinion;
                    return true;
                case WorkloadIntentState.Clear:
                    intent = WorkloadIntent<bool>.Clear;
                    return true;
                case WorkloadIntentState.Set:
                    intent = WorkloadIntent<bool>.CreateSet(manual);
                    return true;
                default:
                    error = "A typed manual-mode intent uses an unknown state.";
                    return false;
            }
        }

        private static bool TryReadScheduleIntent(
            WorkloadV2ScheduleIntentRecord record,
            out WorkloadScheduleIntentEntry entry,
            out string error)
        {
            entry = null;
            error = string.Empty;
            if (record == null)
            {
                error = "A typed schedule intent is missing.";
                return false;
            }

            if (!TryReadTargetScope(record.Scope, record.PawnId, out WorkloadTargetScope scope, out error) ||
                !TryReadScheduleKind(record.TargetKind, out WorkloadScheduleTargetKind targetKind, out error) ||
                string.IsNullOrWhiteSpace(record.WorkTypeDefName))
            {
                if (string.IsNullOrEmpty(error)) error = "A typed schedule intent has an incomplete target identity.";
                return false;
            }

            WorkGiverKey workGiver = new WorkGiverKey(record.WorkGiverDefName);
            if (targetKind == WorkloadScheduleTargetKind.WorkGiver && !workGiver.IsValid)
            {
                error = "A WorkGiver schedule intent has no WorkGiver identity.";
                return false;
            }
            if (targetKind == WorkloadScheduleTargetKind.ParentWorkType && workGiver.IsValid)
            {
                error = "A parent schedule intent cannot carry a WorkGiver identity.";
                return false;
            }

            var key = new WorkloadScheduleTargetKey(
                scope,
                new PawnKey(record.PawnId),
                targetKind,
                new WorkTypeKey(record.WorkTypeDefName),
                workGiver);
            if (!key.IsValid)
            {
                error = "A typed schedule intent has an invalid target scope or kind.";
                return false;
            }

            if (!TryReadIntentState(record.IntentState, out WorkloadIntentState state, out error)) return false;
            WorkloadIntent<WorkloadSchedulePayload> intent;
            switch (state)
            {
                case WorkloadIntentState.NoOpinion:
                    intent = WorkloadIntent<WorkloadSchedulePayload>.NoOpinion;
                    break;
                case WorkloadIntentState.Clear:
                    intent = WorkloadIntent<WorkloadSchedulePayload>.Clear;
                    break;
                case WorkloadIntentState.Set:
                    var payload = new WorkloadSchedulePayload(record.Priorities, record.PinnedHourMask);
                    if (!payload.IsValid)
                    {
                        error = "A typed schedule intent must contain exactly 24 valid priorities and a valid pinned-hour mask.";
                        return false;
                    }

                    intent = WorkloadIntent<WorkloadSchedulePayload>.CreateSet(payload);
                    break;
                default:
                    error = "A typed schedule intent uses an unknown state.";
                    return false;
            }

            entry = new WorkloadScheduleIntentEntry(key, intent);
            return true;
        }

        private static bool TryReadSpecificPriorityIntent(
            WorkloadV2SpecificPriorityIntentRecord record,
            out WorkloadSpecificPriorityIntentEntry entry,
            out string error)
        {
            entry = null;
            error = string.Empty;
            if (record == null || string.IsNullOrWhiteSpace(record.WorkTypeDefName) ||
                string.IsNullOrWhiteSpace(record.WorkGiverDefName))
            {
                error = "A typed specific-priority intent has an incomplete identity.";
                return false;
            }

            if (!TryReadTargetScope(record.Scope, record.PawnId, out WorkloadTargetScope scope, out error)) return false;
            var key = new WorkloadSpecificJobTargetKey(
                scope,
                new PawnKey(record.PawnId),
                new WorkTypeKey(record.WorkTypeDefName),
                new WorkGiverKey(record.WorkGiverDefName));
            if (!key.IsValid)
            {
                error = "A typed specific-priority intent has an invalid target scope.";
                return false;
            }

            if (!TryReadPriorityIntent(
                record.IntentState,
                record.Priority,
                out WorkloadIntent<WorkloadSpecificPriorityPayload> intent,
                out error))
            {
                return false;
            }

            entry = new WorkloadSpecificPriorityIntentEntry(key, intent);
            return true;
        }

        private static bool TryReadWorkTypeOrderIntent(
            WorkloadV2WorkTypeOrderIntentRecord record,
            out WorkloadWorkTypeOrderIntentEntry entry,
            out string error)
        {
            entry = null;
            error = string.Empty;
            if (record == null || string.IsNullOrWhiteSpace(record.WorkTypeDefName))
            {
                error = "A typed WorkType order intent has an incomplete identity.";
                return false;
            }

            if (!TryReadTargetScope(record.Scope, record.PawnId, out WorkloadTargetScope scope, out error)) return false;
            var key = new WorkloadWorkTypeOrderKey(
                scope,
                new PawnKey(record.PawnId),
                new WorkTypeKey(record.WorkTypeDefName));
            if (!key.IsValid)
            {
                error = "A typed WorkType order intent has an invalid target scope.";
                return false;
            }

            if (!TryReadIntentState(record.IntentState, out WorkloadIntentState state, out error)) return false;
            WorkloadIntent<WorkloadWorkTypeOrderPayload> intent;
            switch (state)
            {
                case WorkloadIntentState.NoOpinion:
                    intent = WorkloadIntent<WorkloadWorkTypeOrderPayload>.NoOpinion;
                    break;
                case WorkloadIntentState.Clear:
                    intent = WorkloadIntent<WorkloadWorkTypeOrderPayload>.Clear;
                    break;
                case WorkloadIntentState.Set:
                    var workGivers = new List<WorkGiverKey>();
                    for (int i = 0; i < (record.OrderedWorkGiverDefNames ?? new List<string>()).Count; i++)
                    {
                        string name = record.OrderedWorkGiverDefNames[i];
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            error = "A typed WorkType order intent contains an empty WorkGiver identity.";
                            return false;
                        }
                        workGivers.Add(new WorkGiverKey(name));
                    }

                    var payload = new WorkloadWorkTypeOrderPayload(workGivers, record.IsComplete);
                    if (!payload.IsValid)
                    {
                        error = "A typed WorkType order intent must contain a complete unique permutation.";
                        return false;
                    }

                    intent = WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(payload);
                    break;
                default:
                    error = "A typed WorkType order intent uses an unknown state.";
                    return false;
            }

            entry = new WorkloadWorkTypeOrderIntentEntry(key, intent);
            return true;
        }

        private static bool TryReadPresentationSettingIntent(
            WorkloadV2PresentationSettingIntentRecord record,
            out WorkloadPresentationSettingIntentEntry entry,
            out string error)
        {
            entry = null;
            error = string.Empty;
            if (record == null || string.IsNullOrWhiteSpace(record.Key))
            {
                error = "A typed presentation-setting intent has no key.";
                return false;
            }

            if (!Enum.IsDefined(typeof(WorkloadSettingOwnership), record.Ownership))
            {
                error = "A typed presentation-setting intent has unknown ownership.";
                return false;
            }

            if (!TryReadIntentState(record.IntentState, out WorkloadIntentState state, out error)) return false;
            WorkloadIntent<WorkloadSettingValue> intent;
            switch (state)
            {
                case WorkloadIntentState.NoOpinion:
                    intent = WorkloadIntent<WorkloadSettingValue>.NoOpinion;
                    break;
                case WorkloadIntentState.Clear:
                    intent = WorkloadIntent<WorkloadSettingValue>.Clear;
                    break;
                case WorkloadIntentState.Set:
                    if (!TryReadScalar(record.Value, out WorkloadScalarValue scalar, out error)) return false;
                    intent = WorkloadIntent<WorkloadSettingValue>.CreateSet(
                        new WorkloadSettingValue(
                            scalar,
                            (WorkloadSettingOwnership)record.Ownership));
                    break;
                default:
                    error = "A typed presentation-setting intent uses an unknown state.";
                    return false;
            }

            entry = new WorkloadPresentationSettingIntentEntry(record.Key, intent);
            return true;
        }

        private static bool TryReadIntentState(
            int value,
            out WorkloadIntentState state,
            out string error)
        {
            state = WorkloadIntentState.NoOpinion;
            error = string.Empty;
            if (!Enum.IsDefined(typeof(WorkloadIntentState), value))
            {
                error = "A workload intent uses an unknown state.";
                return false;
            }

            state = (WorkloadIntentState)value;
            return true;
        }

        private static bool TryReadTargetScope(
            int value,
            string pawnId,
            out WorkloadTargetScope scope,
            out string error)
        {
            scope = WorkloadTargetScope.PawnLocal;
            error = string.Empty;
            if (!Enum.IsDefined(typeof(WorkloadTargetScope), value))
            {
                error = "A workload target uses an unknown scope.";
                return false;
            }

            scope = (WorkloadTargetScope)value;
            if (scope == WorkloadTargetScope.PawnLocal && string.IsNullOrWhiteSpace(pawnId))
            {
                error = "A pawn-local workload target has no pawn identity.";
                return false;
            }

            if (scope == WorkloadTargetScope.GlobalShared && !string.IsNullOrWhiteSpace(pawnId))
            {
                error = "A global/shared workload target must not carry a pawn identity.";
                return false;
            }

            return true;
        }

        private static bool TryReadScheduleKind(
            int value,
            out WorkloadScheduleTargetKind kind,
            out string error)
        {
            kind = WorkloadScheduleTargetKind.ParentWorkType;
            error = string.Empty;
            if (!Enum.IsDefined(typeof(WorkloadScheduleTargetKind), value))
            {
                error = "A schedule target uses an unknown target kind.";
                return false;
            }

            kind = (WorkloadScheduleTargetKind)value;
            return true;
        }

        private static bool TryReadScope(
            WorkloadV2PersistenceRecord record,
            out WorkloadScope scope,
            out string error)
        {
            scope = null;
            error = string.Empty;
            var excluded = record.ExcludedPawnIds ?? new List<string>();
            for (int i = 0; i < excluded.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(excluded[i]))
                {
                    error = "The workload scope contains an empty excluded pawn identity.";
                    return false;
                }
            }

            switch ((WorkloadScopeMode)record.ScopeMode)
            {
                case WorkloadScopeMode.ExplicitPawnIds:
                    var explicitIds = record.ExplicitPawnIds ?? new List<string>();
                    for (int i = 0; i < explicitIds.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(explicitIds[i]))
                        {
                            error = "The explicit workload scope contains an empty pawn identity.";
                            return false;
                        }
                    }

                    scope = WorkloadScope.Explicit(explicitIds, excluded);
                    return true;

                case WorkloadScopeMode.CurrentMapFreeColonists:
                    scope = WorkloadScope.CurrentMapFreeColonists(excluded);
                    return true;

                default:
                    error = "The workload scope mode is not supported by this build.";
                    return false;
            }
        }

        private static bool TryReadScalar(
            WorkloadV2ScalarRecord record,
            out WorkloadScalarValue value,
            out string error)
        {
            value = WorkloadScalarValue.Empty;
            error = string.Empty;
            if (record == null)
            {
                error = "A scalar value is missing.";
                return false;
            }

            switch ((WorkloadScalarKind)record.Kind)
            {
                case WorkloadScalarKind.Empty:
                    value = WorkloadScalarValue.Empty;
                    return true;
                case WorkloadScalarKind.Boolean:
                    value = WorkloadScalarValue.FromBoolean(record.BooleanValue);
                    return true;
                case WorkloadScalarKind.Integer:
                    value = WorkloadScalarValue.FromInteger(record.IntegerValue);
                    return true;
                case WorkloadScalarKind.String:
                    if (record.StringValue == null)
                    {
                        error = "A string scalar value is missing.";
                        return false;
                    }

                    value = WorkloadScalarValue.FromString(record.StringValue);
                    return true;
                default:
                    error = "A scalar value uses an unknown kind.";
                    return false;
            }
        }

        private static WorkloadV2ScalarRecord ToScalarRecord(WorkloadScalarValue value)
        {
            return new WorkloadV2ScalarRecord
            {
                Kind = (int)value.Kind,
                BooleanValue = value.BooleanValue,
                IntegerValue = value.IntegerValue,
                StringValue = value.StringValue ?? string.Empty
            };
        }

        private static List<string> ToPawnIds(IReadOnlyList<PawnKey> values)
        {
            var result = new List<string>();
            if (values == null) return result;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] != null && !string.IsNullOrEmpty(values[i].Value))
                {
                    result.Add(values[i].Value);
                }
            }

            return result;
        }

        private static bool HasDuplicateTypedSpecificTargets(
            WorkloadV2PersistenceRecord record,
            out string error)
        {
            error = string.Empty;
            var priorityKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < record.SpecificPriorityIntents.Count; i++)
            {
                WorkloadV2SpecificPriorityIntentRecord value = record.SpecificPriorityIntents[i];
                if (value == null)
                {
                    error = "The workload record contains a missing typed specific-priority intent.";
                    return true;
                }

                string key = SpecificPriorityKey(value.Scope, value.PawnId, value.WorkTypeDefName, value.WorkGiverDefName);
                if (!priorityKeys.Add(key))
                {
                    error = "The workload record contains duplicate typed specific-priority target '" + key + "'.";
                    return true;
                }
            }

            var orderKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < record.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadV2WorkTypeOrderIntentRecord value = record.WorkTypeOrderIntents[i];
                if (value == null)
                {
                    error = "The workload record contains a missing typed WorkType-order intent.";
                    return true;
                }

                string key = WorkTypeOrderKey(value.Scope, value.PawnId, value.WorkTypeDefName);
                if (!orderKeys.Add(key))
                {
                    error = "The workload record contains duplicate typed WorkType-order target '" + key + "'.";
                    return true;
                }
            }

            return false;
        }

        private static string SpecificPriorityKey(
            int scope,
            string pawnId,
            string workTypeDefName,
            string workGiverDefName)
        {
            return scope + "\u001f" + (pawnId ?? string.Empty) + "\u001f" +
                (workTypeDefName ?? string.Empty) + "\u001f" +
                (workGiverDefName ?? string.Empty);
        }

        private static string WorkTypeOrderKey(
            int scope,
            string pawnId,
            string workTypeDefName)
        {
            return scope + "\u001f" + (pawnId ?? string.Empty) + "\u001f" +
                (workTypeDefName ?? string.Empty);
        }

        private static bool HasIdentity(string pawnId, string workTypeDefName)
        {
            return !string.IsNullOrWhiteSpace(pawnId) && !string.IsNullOrWhiteSpace(workTypeDefName);
        }

        private static bool HasIdentity(string pawnId, string workTypeDefName, string workGiverDefName)
        {
            return HasIdentity(pawnId, workTypeDefName) && !string.IsNullOrWhiteSpace(workGiverDefName);
        }

        private static WorkloadOperationResult<WorkloadTemplate> InvalidState(string message)
        {
            return WorkloadOperationResult<WorkloadTemplate>.Fail(
                WorkloadDiagnosticCode.InvalidState,
                message);
        }

        private static string GetFirstValidationMessage(WorkloadValidationResult validation)
        {
            return validation?.Issues != null && validation.Issues.Count > 0
                ? validation.Issues[0].Message
                : "The workload failed validation.";
        }
    }
}
