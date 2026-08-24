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
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "BWT_Workload_ModeUnavailable".Translate());
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoSettings,
                    "BWT_Workload_NotReady".Translate());

            if (CurrentMode == targetMode) return WorkloadOperationResult.Ok();

            GameComponent_BWTWorldSettings component =
                Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            Workload2Backend backend = Workload2Backend.MultiplayerBackend;
            if (backend?.Component == component && backend.IsPreviewSessionActive)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.BlockedModeTransition,
                    "BWT_Workload_CloseBeforeModeChange".Translate());
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
