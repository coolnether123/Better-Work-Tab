using System;
using System.Collections.Generic;
using System.Globalization;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.Workloads.Projection
{
    /// <summary>
    /// Adapts Workload keys, payloads, and mutations to the neutral provider
    /// scoped around the current Work-grid pass.
    /// </summary>
    public static class WorkloadProjectionRuntime
    {
        private static IWorkTabEffectiveStateProvider CurrentProvider =>
            WorkTabEffectiveStateRuntime.CurrentProvider;

        private static bool IsPreviewActive =>
            WorkTabEffectiveStateRuntime.IsPreviewActive;

        public static bool TryGetPreviewV2Editor(
            out IWorkTabEffectiveStateV2Editor editor)
        {
            editor = null;
            IWorkTabEffectiveStateProvider provider = WorkTabEffectiveStateScope.Current;
            if (provider == null || !provider.IsPreview)
            {
                return false;
            }

            editor = provider as IWorkTabEffectiveStateV2Editor;
            if (editor != null)
            {
                return true;
            }

            WorkTabEffectiveStateRuntime.ReportBlocked(
                WorkTabEffectiveStateDimension.Schedule,
                "BWT_Workload_HourlyPriorityUnavailable".Translate());
            return false;
        }

        public static WorkTabEffectiveStateResolution<WorkloadSchedulePayload>
            ResolveSchedule(WorkloadScheduleTargetKey key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectiveSchedule(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveSchedule(key)
                : WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
        }

        internal static WorkTabEffectiveStateResolution<WorkloadSchedulePayload>
            ResolvePreviewScheduleIntent(WorkloadScheduleTargetKey key)
        {
            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveSchedule(key)
                : WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
        }

        public static WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveSpecificJobPriority(WorkloadSpecificJobTargetKey key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectiveSpecificJobPriority(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveSpecificJobPriority(key)
                : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
        }

        public static WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>
            ResolveWorkTypeOrder(WorkloadWorkTypeOrderKey key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectiveWorkTypeOrder(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolveWorkTypeOrder(key)
                : WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
        }

        public static WorkTabEffectiveStateResolution<WorkloadSettingValue>
            ResolvePresentationSettingV2(string key)
        {
            if (CurrentProvider is IWorkTabComposedEffectiveStateProvider composed)
            {
                return composed.ResolveEffectivePresentationSetting(key);
            }

            return CurrentProvider is IWorkTabEffectiveStateV2Provider v2
                ? v2.ResolvePresentationSetting(key)
                : WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
        }

        public static bool TrySetSpecificJobPriority(
            int pawnId,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int priority,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (pawnId < 0 || workType == null || workGiver == null ||
                !IsPreviewActive)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A pawn-scoped specific-job key is required.");
                }
                return false;
            }

            var key = WorkloadSpecificJobTargetKey.ForPawn(
                new PawnKey(pawnId.ToString(CultureInfo.InvariantCulture)),
                WorkTabEffectiveStateIds.ForWorkType(workType),
                WorkTabEffectiveStateIds.ForWorkGiver(workGiver));
            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor))
            {
                result = editor.SetSpecificJobPriority(
                    key,
                    new WorkloadSpecificPriorityPayload(priority));
            }
            else
            {
                result = Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    "The active preview provider has no specific-job editor.");
            }
            return Accept(result);
        }

        public static bool TrySetSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            int priority,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!IsPreviewActive || key == null || !key.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A valid typed specific-job key is required.");
                }
                return false;
            }

            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor))
            {
                result = editor.SetSpecificJobPriority(
                    key,
                    new WorkloadSpecificPriorityPayload(priority));
                return Accept(result);
            }

            result = Blocked(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                "The active preview provider does not support typed specific-job state.");
            return false;
        }

        public static bool TryClearSpecificJobPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!IsPreviewActive || pawn == null || workType == null || workGiver == null)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A pawn-scoped specific-job key is required.");
                }
                return false;
            }

            return TryClearSpecificJobPriority(
                WorkTabEffectiveStateIds.ForSpecificJobTarget(pawn, workType, workGiver),
                out result);
        }

        public static bool TryClearSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!IsPreviewActive || key == null || !key.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        "A valid typed specific-job key is required.");
                }
                return false;
            }

            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor))
            {
                result = editor.ClearSpecificJobPriority(key);
                return Accept(result);
            }

            result = Blocked(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                "The active preview provider does not support typed specific-job tombstones.");
            return false;
        }

        public static bool TrySetSpecificJobOrder(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int order,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (pawn == null || workType == null || workGiver == null || !IsPreviewActive)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "A pawn-scoped specific-job key is required.");
                }
                return false;
            }

            if (TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor))
            {
                WorkloadWorkTypeOrderKey key =
                    WorkTabEffectiveStateIds.ForWorkTypeOrder(pawn, workType);
                WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> resolution =
                    ResolveWorkTypeOrder(key);
                WorkloadWorkTypeOrderPayload payload = resolution.IsSet
                    ? resolution.Value
                    : null;
                if (payload == null || !payload.IsValid)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "No complete WorkGiver order is available for this preview target.");
                    return false;
                }

                var ordered = new List<WorkGiverKey>(payload.OrderedWorkGivers);
                WorkGiverKey target = WorkTabEffectiveStateIds.ForWorkGiver(workGiver);
                if (!ordered.Remove(target))
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "The WorkGiver is not present in the complete order.");
                    return false;
                }

                int targetIndex = Math.Max(0, Math.Min(order, ordered.Count));
                ordered.Insert(targetIndex, target);
                result = editor.SetWorkTypeOrder(
                    key,
                    new WorkloadWorkTypeOrderPayload(ordered));
            }
            else
            {
                result = Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    "The active preview provider has no specific-job order editor.");
            }
            return Accept(result);
        }

        internal static bool TrySetScheduleIntent(
            WorkloadScheduleTargetKey key,
            WorkTabEffectiveStateResolution<WorkloadSchedulePayload> intent,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                key == null || !key.IsValid ||
                (intent.IsSet && (intent.Value == null || !intent.Value.IsValid)))
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.Schedule,
                        "A valid typed schedule intent is required.");
                }
                return false;
            }

            result = intent.IsSet
                ? editor.SetSchedule(key, intent.Value)
                : intent.IsClear
                    ? editor.ClearSchedule(key)
                    : editor.SetScheduleNoOpinion(key);
            return Accept(result);
        }

        public static bool TryGetSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            out int priority)
        {
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> resolution =
                ResolveSpecificJobPriority(key);
            priority = resolution.IsSet ? resolution.Value.Priority : 0;
            return resolution.IsSet;
        }

        public static bool TryGetWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            out WorkloadWorkTypeOrderPayload payload)
        {
            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> resolution =
                ResolveWorkTypeOrder(key);
            payload = resolution.IsSet ? resolution.Value : null;
            return resolution.IsSet;
        }

        public static bool TrySetWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            WorkloadWorkTypeOrderPayload payload,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                key == null || !key.IsValid || payload == null || !payload.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "A valid complete WorkType order target and payload are required.");
                }
                return false;
            }

            result = editor.SetWorkTypeOrder(key, payload);
            return Accept(result);
        }

        public static bool TryClearWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                key == null || !key.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        "A valid typed WorkType order target is required.");
                }
                return false;
            }

            result = editor.ClearWorkTypeOrder(key);
            return Accept(result);
        }

        public static bool TryGetPresentationSettingV2(
            string key,
            out WorkloadSettingValue value)
        {
            WorkTabEffectiveStateResolution<WorkloadSettingValue> resolution =
                ResolvePresentationSettingV2(key);
            value = resolution.IsSet ? resolution.Value : default(WorkloadSettingValue);
            return resolution.IsSet;
        }

        public static bool TrySetPresentationSettingV2(
            string key,
            WorkloadSettingValue value,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                string.IsNullOrWhiteSpace(key) || !value.IsValid)
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.PresentationSetting,
                        "A valid owned presentation-setting key and value are required.");
                }
                return false;
            }

            result = editor.SetPresentationSetting(key, value);
            return Accept(result);
        }

        public static bool TryClearPresentationSettingV2(
            string key,
            out WorkTabEffectiveStateMutationResult result)
        {
            result = default(WorkTabEffectiveStateMutationResult);
            if (!TryGetPreviewV2Editor(out IWorkTabEffectiveStateV2Editor editor) ||
                string.IsNullOrWhiteSpace(key))
            {
                if (IsPreviewActive)
                {
                    result = Blocked(
                        WorkTabEffectiveStateDimension.PresentationSetting,
                        "A non-empty presentation-setting key is required.");
                }
                return false;
            }

            result = editor.ClearPresentationSettingV2(key);
            return Accept(result);
        }

        private static bool Accept(WorkTabEffectiveStateMutationResult result)
        {
            return WorkTabEffectiveStateRuntime.AcceptPreviewMutation(result);
        }

        private static WorkTabEffectiveStateMutationResult Blocked(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            return WorkTabEffectiveStateRuntime.Blocked(dimension, reason);
        }
    }

    /// <summary>
    /// Workload-owned translation boundary for the small neutral preview
    /// capability. Providers use this helper at their typed capability edge;
    /// normal Work-grid callers never construct workload keys or payloads.
    /// </summary>
    internal static class WorkloadPreviewStateAdapter
    {
        internal static bool TryGetScheduleKey(
            TimePriorityTarget target,
            out WorkloadScheduleTargetKey key,
            out string reason)
        {
            return WorkloadTimePriorityAdapter.TryGetScheduleTarget(
                target,
                out key,
                out reason);
        }

        internal static bool TryGetSpecificJobKey(
            WorkTabSpecificJobTarget target,
            out WorkloadSpecificJobTargetKey key)
        {
            key = null;
            if (!target.IsValid)
            {
                return false;
            }

            key = target.IsGlobal
                ? WorkTabEffectiveStateIds.ForGlobalSpecificJobTarget(
                    target.WorkType,
                    target.WorkGiver)
                : WorkTabEffectiveStateIds.ForSpecificJobTarget(
                    target.Pawn,
                    target.WorkType,
                    target.WorkGiver);
            return key != null && key.IsValid;
        }

        internal static bool TryGetWorkTypeOrderKey(
            WorkTabWorkTypeOrderTarget target,
            out WorkloadWorkTypeOrderKey key)
        {
            key = null;
            if (!target.IsValid)
            {
                return false;
            }

            key = target.IsGlobal
                ? WorkTabEffectiveStateIds.ForGlobalWorkTypeOrder(target.WorkType)
                : WorkTabEffectiveStateIds.ForWorkTypeOrder(
                    target.Pawn,
                    target.WorkType);
            return key != null && key.IsValid;
        }

        internal static WorkTabEffectiveStateResolution<TimePriorityScheduleValue>
            ToScheduleResolution(
                WorkTabEffectiveStateResolution<WorkloadSchedulePayload> resolution)
        {
            if (resolution.IsClear)
            {
                return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.Clear;
            }

            if (!resolution.IsSet || resolution.Value == null ||
                !resolution.Value.IsValid ||
                !WorkloadTimePriorityAdapter.TryGetScheduleValue(
                    resolution.Value,
                    out TimePriorityScheduleValue value,
                    out _))
            {
                return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.NoOpinion;
            }

            return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.Set(value);
        }

        internal static WorkTabEffectiveStateResolution<int>
            ToSpecificPriorityResolution(
                WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> resolution)
        {
            if (resolution.IsClear)
            {
                return WorkTabEffectiveStateResolution<int>.Clear;
            }

            if (!resolution.IsSet || !resolution.Value.IsValid)
            {
                return WorkTabEffectiveStateResolution<int>.NoOpinion;
            }

            return WorkTabEffectiveStateResolution<int>.Set(resolution.Value.Priority);
        }

        internal static WorkTabEffectiveStateResolution<IReadOnlyList<string>>
            ToWorkTypeOrderResolution(
                WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> resolution)
        {
            if (resolution.IsClear)
            {
                return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.Clear;
            }

            if (!resolution.IsSet || resolution.Value == null ||
                !resolution.Value.IsValid)
            {
                return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.NoOpinion;
            }

            WorkloadWorkTypeOrderPayload payload = resolution.Value;
            var names = new string[payload.OrderedWorkGivers.Count];
            for (int i = 0; i < names.Length; i++)
            {
                WorkGiverKey key = payload.OrderedWorkGivers[i];
                if (key == null || !key.IsValid)
                {
                    return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.NoOpinion;
                }

                names[i] = key.Value;
            }

            return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.Set(names);
        }

        internal static WorkloadSchedulePayload ToSchedulePayload(
            TimePriorityScheduleValue value)
        {
            return value == null || !value.IsValid
                ? null
                : WorkloadTimePriorityAdapter.ToPayload(value);
        }

        internal static WorkloadWorkTypeOrderPayload ToWorkTypeOrderPayload(
            IReadOnlyList<string> orderedWorkGiverNames)
        {
            if (orderedWorkGiverNames == null)
            {
                return null;
            }

            var keys = new List<WorkGiverKey>(orderedWorkGiverNames.Count);
            for (int i = 0; i < orderedWorkGiverNames.Count; i++)
            {
                keys.Add(new WorkGiverKey(orderedWorkGiverNames[i]));
            }

            var payload = new WorkloadWorkTypeOrderPayload(keys);
            return payload.IsValid ? payload : null;
        }
    }
}
