using Verse;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    internal static class WorkloadModeService
    {
        internal static WorkloadBackendMode CurrentMode =>
            BetterWorkTabMod.Settings?.useLegacyWorkloads == true
                ? WorkloadBackendMode.Legacy
                : WorkloadBackendMode.Modern;

        internal static WorkloadOperationResult TryTransition(
            WorkloadBackendMode targetMode,
            bool persistSettings = true)
        {
            if (targetMode != WorkloadBackendMode.Legacy && targetMode != WorkloadBackendMode.Modern)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.ModeUnavailable);
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoSettings);

            if (CurrentMode == targetMode) return WorkloadOperationResult.Ok();

            IWorkloadWorldState component = WorkloadWorldStates.For(Current.Game);
            Workload2Backend backend = Workload2Backend.MultiplayerBackend;
            if (backend?.WorldState == component && backend.IsPreviewSessionActive)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.BlockedModeTransition);
            }

            settings.useLegacyWorkloads = targetMode == WorkloadBackendMode.Legacy;
            if (persistSettings)
            {
                settings.Write();
            }
            WorkloadPresentationServices.NotifyGlobalSettingsChanged();
            return WorkloadOperationResult.Ok();
        }
    }
}
