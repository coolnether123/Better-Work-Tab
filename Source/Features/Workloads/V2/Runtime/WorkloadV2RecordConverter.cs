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
                presentation);
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

            record.NormalizeStableState();
            return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Ok(record);
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
