using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace Better_Work_Tab.UI.Workloads.Projection
{
    /// <summary>
    /// Workload-owned extension of the neutral effective-state provider.
    /// Resolution methods report the exact layer owned by the provider.
    /// </summary>
    public interface IWorkTabEffectiveStateV2Provider
    {
        WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveSchedule(
            WorkloadScheduleTargetKey key);
        WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> ResolveSpecificJobPriority(
            WorkloadSpecificJobTargetKey key);
        WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> ResolveWorkTypeOrder(
            WorkloadWorkTypeOrderKey key);
        WorkTabEffectiveStateResolution<WorkloadSettingValue> ResolvePresentationSetting(string key);
    }

    /// <summary>
    /// Workload projection reads composed with their lower-precedence source.
    /// </summary>
    public interface IWorkTabComposedEffectiveStateProvider
    {
        WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveEffectiveSchedule(
            WorkloadScheduleTargetKey key);
        WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> ResolveEffectiveSpecificJobPriority(
            WorkloadSpecificJobTargetKey key);
        WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> ResolveEffectiveWorkTypeOrder(
            WorkloadWorkTypeOrderKey key);
        WorkTabEffectiveStateResolution<WorkloadSettingValue> ResolveEffectivePresentationSetting(string key);
    }

    /// <summary>
    /// Workload preview writes for schedule, specific-job, and presentation
    /// adapters. The neutral state contract does not know these payload types.
    /// </summary>
    public interface IWorkTabEffectiveStateV2Editor
    {
        WorkTabEffectiveStateMutationResult SetSchedule(
            WorkloadScheduleTargetKey key,
            WorkloadSchedulePayload payload);

        WorkTabEffectiveStateMutationResult ClearSchedule(WorkloadScheduleTargetKey key);

        WorkTabEffectiveStateMutationResult SetScheduleNoOpinion(WorkloadScheduleTargetKey key);

        WorkTabEffectiveStateMutationResult SetSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            WorkloadSpecificPriorityPayload payload);

        WorkTabEffectiveStateMutationResult ClearSpecificJobPriority(
            WorkloadSpecificJobTargetKey key);

        WorkTabEffectiveStateMutationResult SetWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            WorkloadWorkTypeOrderPayload payload);

        WorkTabEffectiveStateMutationResult ClearWorkTypeOrder(WorkloadWorkTypeOrderKey key);

        WorkTabEffectiveStateMutationResult SetPresentationSetting(
            string key,
            WorkloadSettingValue value);

        WorkTabEffectiveStateMutationResult ClearPresentationSettingV2(string key);
    }
}
