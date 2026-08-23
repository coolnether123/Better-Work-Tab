using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    // Pure decisions shared by the RimWorld capture traversal and deterministic contracts.
    internal static class WorkloadLiveCapturePolicy
    {
        internal static string ResolveLabel(string label, Func<string> defaultLabel) =>
            string.IsNullOrWhiteSpace(label) ? defaultLabel?.Invoke() ?? string.Empty : label;

        internal static WorkloadOperationResult ValidateTemplateCapture(
            string stableId,
            bool hasCurrentMap,
            Func<bool> hasBwtMutationAuthority,
            bool hasPlaySettings)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                return Failure(
                    WorkloadDiagnosticCode.MissingStableId,
                    "A stable workload ID is required for capture.");

            if (!hasCurrentMap)
                return Failure(
                    WorkloadDiagnosticCode.NoCurrentMap,
                    "A current map is required to capture a workload.");

            if (hasBwtMutationAuthority == null || !hasBwtMutationAuthority())
                return Failure(
                    WorkloadDiagnosticCode.ExternalPriorityAuthority,
                    "BWT cannot capture priority state while an external priority authority is active.");

            return hasPlaySettings
                ? WorkloadOperationResult.Ok()
                : Failure(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "Global manual-priority state is unavailable for workload capture.");
        }

        internal static int SelectParentPriority(
            bool templateCapture,
            int storedPriority,
            int effectivePriority) => templateCapture ? storedPriority : effectivePriority;

        internal static WorkloadOwnershipDimensions CompleteOwnership(
            WorkloadOwnershipDimensions ownership,
            bool capturedSchedule) => capturedSchedule
                ? ownership | WorkloadOwnershipDimensions.Schedules : ownership;

        internal static bool TryCaptureSchedule(
            WorkloadDraft draft,
            WorkloadScheduleTargetKey key,
            int fallbackPriority,
            Func<WorkloadScheduleTargetKey, int, WorkloadSchedulePayload> capture)
        {
            if (draft == null || key == null || !key.IsValid || capture == null)
            {
                return false;
            }

            WorkloadSchedulePayload payload = capture(key, fallbackPriority);
            if (payload == null || !payload.IsValid) return false;
            draft.SetSchedule(key, payload);
            return true;
        }

        internal static void ApplySpecificPrioritySnapshot(
            WorkloadDraft draft,
            WorkloadSpecificJobTargetKey key,
            bool hasStoredValue,
            bool isExplicitlyCleared,
            int priority)
        {
            if (draft == null || key == null || !key.IsValid) return;
            if (hasStoredValue) draft.SetSpecificPriority(key, priority);
            else if (isExplicitlyCleared) draft.ClearSpecificPriority(key);
        }

        internal static bool ApplyWorkTypeOrderSnapshot(
            WorkloadDraft draft,
            WorkloadWorkTypeOrderKey key,
            bool hasStoredValue,
            bool isExplicitlyCleared,
            IReadOnlyList<string> orderedNames)
        {
            if (draft == null || key == null || !key.IsValid) return false;
            if (hasStoredValue)
            {
                if (orderedNames == null || orderedNames.Count == 0) return false;
                var workGivers = new List<WorkGiverKey>(orderedNames.Count);
                for (int i = 0; i < orderedNames.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(orderedNames[i])) return false;
                    workGivers.Add(new WorkGiverKey(orderedNames[i]));
                }

                var payload = new WorkloadWorkTypeOrderPayload(workGivers);
                if (!payload.IsValid) return false;
                draft.SetWorkTypeOrder(key, payload);
                return true;
            }

            if (!isExplicitlyCleared) return false;
            draft.ClearWorkTypeOrder(key);
            return true;
        }

        private static WorkloadOperationResult Failure(
            WorkloadDiagnosticCode code,
            string message) => WorkloadOperationResult.Fail(code, message);
    }
}
