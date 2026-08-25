using System.Collections.Generic;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Verse;

namespace Better_Work_Tab.UI.Workloads
{
    /// <summary>
    /// The one boundary that turns workload facts into player-facing text.
    /// Runtime and persistence code return codes, identifiers, and paths only.
    /// </summary>
    internal static class WorkloadPresentationResolver
    {
        internal static string Resolve(WorkloadOperationResult result) =>
            Resolve(result?.Code ?? WorkloadDiagnosticCode.InvalidState, result?.Context);

        internal static string Resolve<TValue>(WorkloadOperationResult<TValue> result) =>
            Resolve(result?.Code ?? WorkloadDiagnosticCode.InvalidState, result?.Context);

        internal static string Resolve<TValue>(
            WorkloadOperationResult<TValue> result,
            string presentationFallback)
        {
            string resolved = Resolve(result);
            string generic = T("BWT_Workload_OperationFailed");
            if (resolved.AnyNonWhitespace() && resolved != generic)
            {
                return resolved;
            }

            return presentationFallback.AnyNonWhitespace()
                ? presentationFallback
                : generic;
        }

        internal static string Resolve(WorkloadV2CommitResult result)
        {
            if (result == null)
            {
                return Resolve(WorkloadDiagnosticCode.InvalidState);
            }

            if (!result.Succeeded)
            {
                return Resolve(result.Code, result.Context);
            }

            WorkloadV2CommitReport report = result.Report;
            switch (report?.DecisionKind)
            {
                case WorkloadDecisionKind.Apply:
                    return T(report.IsPartial
                        ? "BWT_Workload_AppliedPartial"
                        : "BWT_Workload_Applied");
                case WorkloadDecisionKind.Update:
                    if (report.IsSemanticNoOp)
                    {
                        return T("BWT_Workload_UpdateNoOp");
                    }
                    return T(report.IsPartial
                        ? "BWT_Workload_UpdatedWithExclusions"
                        : "BWT_Workload_Updated");
                case WorkloadDecisionKind.Fork:
                    return T(report.IsPartial
                        ? "BWT_Workload_ForkedWithExclusions"
                        : "BWT_Workload_Forked");
                default:
                    return T("BWT_Workload_OperationCompleted");
            }
        }

        internal static string Resolve(WorkloadMultiplayerCommitStatus status)
        {
            if (status == null)
            {
                return Resolve(WorkloadDiagnosticCode.InvalidState);
            }

            return status.Result != null && status.IsTerminal &&
                   (status.State == WorkloadMultiplayerCommitState.Succeeded ||
                    !status.Result.Succeeded)
                ? Resolve(status.Result)
                : Resolve(status.Code, status.Context);
        }

        internal static string Resolve(WorkloadDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return Resolve(WorkloadDiagnosticCode.InvalidState);
            }

            if (descriptor.DiagnosticCode == WorkloadDiagnosticCode.MissingStableId)
            {
                return T("BWT_Workload_UnsupportedData");
            }

            return Resolve(
                descriptor.DiagnosticCode,
                new WorkloadDiagnosticContext(
                    stableId: descriptor.StableId,
                    actualVersion: descriptor.SchemaVersion));
        }

        internal static string Resolve(WorkloadValidationIssue issue)
        {
            if (issue == null)
            {
                return T("BWT_Workload_ValidationFailed");
            }

            return ResolveValidation(issue.Code, issue.Path);
        }

        internal static string ResolveUnsupportedClear(
            IReadOnlyList<WorkloadStateDimension> dimensions)
        {
            if (dimensions == null || dimensions.Count == 0)
            {
                return string.Empty;
            }

            var labels = new List<string>(dimensions.Count);
            for (int i = 0; i < dimensions.Count; i++)
            {
                labels.Add(ResolveDimension(dimensions[i]));
            }

            return "BWT_Workload_UnsupportedClear".Translate(
                string.Join(", ", labels.ToArray())).ToString();
        }

        internal static string Resolve(
            WorkloadDiagnosticCode code,
            WorkloadDiagnosticContext context = null)
        {
            switch (code)
            {
                case WorkloadDiagnosticCode.None:
                    return string.Empty;
                case WorkloadDiagnosticCode.NoCurrentGame:
                case WorkloadDiagnosticCode.NoCurrentMap:
                    return T("BWT_Workload_NoCurrentGame");
                case WorkloadDiagnosticCode.InvalidLabel:
                    return T("BWT_Workload_NameRequired");
                case WorkloadDiagnosticCode.MissingStableId:
                    return T("BWT_Workload_UnsupportedData");
                case WorkloadDiagnosticCode.MissingCurrentWorkloadId:
                case WorkloadDiagnosticCode.UnknownWorkloadId:
                case WorkloadDiagnosticCode.NotFound:
                    return T("BWT_Workload_NotFound");
                case WorkloadDiagnosticCode.AmbiguousStableId:
                case WorkloadDiagnosticCode.PersistenceConflict:
                case WorkloadDiagnosticCode.BaselineChanged:
                    return T("BWT_Workload_Conflict");
                case WorkloadDiagnosticCode.UnsupportedSchema:
                case WorkloadDiagnosticCode.NewerSchema:
                case WorkloadDiagnosticCode.ReadOnlyDiagnostic:
                case WorkloadDiagnosticCode.UnsupportedLegacyState:
                case WorkloadDiagnosticCode.UnsupportedPresentationData:
                    return T("BWT_Workload_UnsupportedData");
                case WorkloadDiagnosticCode.ExternalPriorityAuthority:
                case WorkloadDiagnosticCode.MutationCapabilityRejected:
                    return T("BWT_Workload_AuthorityBlocked");
                case WorkloadDiagnosticCode.BlockedModeTransition:
                    return T("BWT_Workload_CloseBeforeModeChange");
                case WorkloadDiagnosticCode.ModeUnavailable:
                    return T("BWT_Workload_ModeUnavailable");
                case WorkloadDiagnosticCode.MultiplayerUnavailable:
                    return T("BWT_Workload_MultiplayerStartFailed");
                case WorkloadDiagnosticCode.CaptureFailed:
                    return T("BWT_Workload_CaptureFailed");
                case WorkloadDiagnosticCode.UnsupportedClear:
                    return T("BWT_Workload_UnsupportedClearGeneric");
                case WorkloadDiagnosticCode.UnsupportedRuntimeState:
                    return T("BWT_Workload_UnsupportedRuntimeState");
                case WorkloadDiagnosticCode.UnsupportedDecision:
                    return T("BWT_Workload_OperationFailed");
                case WorkloadDiagnosticCode.RollbackRequired:
                case WorkloadDiagnosticCode.RollbackFailed:
                    return T("BWT_Workload_MultiplayerRecovery");
                case WorkloadDiagnosticCode.NoSettings:
                    return T("BWT_Workload_NotReady");
                default:
                    if (context?.ValidationCode != null)
                    {
                        return ResolveValidation(
                            context.ValidationCode.Value,
                            context.Path);
                    }

                    return T("BWT_Workload_OperationFailed");
            }
        }

        private static string ResolveValidation(
            WorkloadValidationCode code,
            string path)
        {
            switch (code)
            {
                case WorkloadValidationCode.NewerSchema:
                case WorkloadValidationCode.InvalidSchemaVersion:
                case WorkloadValidationCode.UnknownOwnershipDimension:
                    return T("BWT_Workload_UnsupportedData");
                case WorkloadValidationCode.MissingTemplate:
                case WorkloadValidationCode.MissingStableId:
                case WorkloadValidationCode.MissingLabel:
                case WorkloadValidationCode.MissingPawnId:
                case WorkloadValidationCode.MissingWorkTypeId:
                case WorkloadValidationCode.MissingWorkGiverId:
                case WorkloadValidationCode.MissingPresentationSettingKey:
                    return AtPathOrFallback(
                        "BWT_Workload_ValidationMissingAtPath",
                        path);
                case WorkloadValidationCode.UnknownPawnId:
                case WorkloadValidationCode.UnknownWorkTypeId:
                case WorkloadValidationCode.UnknownWorkGiverId:
                case WorkloadValidationCode.UnknownScheduleKey:
                    return AtPathOrFallback(
                        "BWT_Workload_ValidationUnknownAtPath",
                        path);
                case WorkloadValidationCode.ConflictingManualModes:
                case WorkloadValidationCode.DuplicateSpecificJobIntent:
                case WorkloadValidationCode.DuplicateSpecificJobOrderIntent:
                    return AtPathOrFallback(
                        "BWT_Workload_ValidationConflictAtPath",
                        path);
                default:
                    return AtPathOrFallback(
                        "BWT_Workload_ValidationAtPath",
                        path);
            }
        }

        private static string AtPathOrFallback(string key, string path) =>
            path.AnyNonWhitespace()
                ? key.Translate(path).ToString()
                : T("BWT_Workload_ValidationFailed");

        private static string ResolveDimension(WorkloadStateDimension dimension)
        {
            switch (dimension)
            {
                case WorkloadStateDimension.ParentPriorities:
                    return T("BWT_Workload_DimensionParentPriorities");
                case WorkloadStateDimension.ManualModes:
                    return T("BWT_Workload_DimensionManualModes");
                case WorkloadStateDimension.Schedules:
                    return T("BWT_Workload_DimensionSchedules");
                case WorkloadStateDimension.SpecificJobOverrides:
                    return T("BWT_Workload_DimensionSpecificJobPriorities");
                case WorkloadStateDimension.SpecificJobOrder:
                    return T("BWT_Workload_DimensionSpecificJobOrder");
                case WorkloadStateDimension.PresentationSettings:
                    return T("BWT_Workload_DimensionPresentationSettings");
                case WorkloadStateDimension.Membership:
                    return T("BWT_Workload_DimensionMembership");
                default:
                    return T("BWT_Workload_DimensionSettings");
            }
        }

        private static string T(string key) => key.Translate().ToString();
    }
}
