using System;
using System.Collections.Generic;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Source-level contracts for the Unity-bound gateway.  The standalone
    /// workload suite deliberately does not reference RimWorld, so these
    /// checks pin the production seams that cannot be exercised by the pure
    /// model tests without inventing a second gateway implementation.
    /// </summary>
    internal static class GatewayLifecycleTests
    {
        public static void Run()
        {
            string root = FindRepositoryRoot();
            string header = Read(root, "Source", "UI", "HeaderButtons.cs");
            string gateway = Read(root, "Source", "UI", "Workloads", "WorkloadGateway.cs");
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string session = Read(root, "Source", "Features", "Workloads", "V2", "WorkloadSession.cs");

            FooterSelectorKeepsManagerAndPreviewActionsSeparate(header);
            FooterActionsAcceptTypedState(gateway, session);
            DynamicOwnershipReachesCommitPayload(backend, session);
            IncludeUsesTheAuthoritativeBaselineAndApplyPath(gateway, backend);
            LegacyPayloadsRemainFailClosed(gateway, backend, session);
        }

        private static void FooterSelectorKeepsManagerAndPreviewActionsSeparate(
            string header)
        {
            int main = header.IndexOf(
                "if (DrawWorkloadMainControl(",
                StringComparison.Ordinal);
            int menu = header.IndexOf(
                "if (rects.HasWorkloadMenu && DrawWorkloadMenuControl(",
                StringComparison.Ordinal);
            int previewActions = header.IndexOf(
                "private static void DrawWorkloadPreviewActions(",
                StringComparison.Ordinal);
            TestAssert.True(
                main >= 0 && menu > main && previewActions > menu,
                "the workload name and ellipsis controls must have independent draw paths");

            string nameButtonPath = header.Substring(main, menu - main);
            string ellipsisButtonPath = header.Substring(menu, previewActions - menu);
            TestAssert.Contains(
                nameButtonPath,
                "() => preview.BeginCurrentPreview(),",
                "the selected modern workload name button must enter the projected preview");
            TestAssert.Contains(
                nameButtonPath,
                "Select a workload with the ... menu before opening a preview.",
                "the empty name button must not open the workload manager");
            TestAssert.False(
                nameButtonPath.IndexOf("OpenWorkloadFooterPicker();", StringComparison.Ordinal) >= 0,
                "the workload name button must never open the workload picker");
            TestAssert.Contains(
                ellipsisButtonPath,
                "OpenWorkloadFooterPicker();",
                "the workload ellipsis button must open the workload picker when preview guards allow it");
            TestAssert.False(
                ellipsisButtonPath.IndexOf("BeginCurrentPreview", StringComparison.Ordinal) >= 0,
                "the workload ellipsis button must never enter projected preview");
            TestAssert.False(
                ellipsisButtonPath.IndexOf(
                    "Select a workload with the ... menu before opening a preview.",
                    StringComparison.Ordinal) >= 0,
                "the no-selection feedback belongs to the workload name button path");
            TestAssert.Contains(
                header,
                "rects.HasWorkloadPreview ? rects.WorkloadApply : Rect.zero",
                "Apply must share the footer inspection hover geometry");
            TestAssert.Contains(
                header,
                "rects.HasWorkloadUpdate ? rects.WorkloadUpdate : Rect.zero",
                "Update must retain the footer inspection hover geometry");
            TestAssert.Contains(
                header,
                "SpineEasing.Move01(",
                "preview footer actions must use the shared easing infrastructure");
            TestAssert.Contains(
                header,
                "finalRect.x += hiddenOffset * (1f - progress);",
                "preview actions must translate horizontally from behind the workload selector");
            TestAssert.Contains(
                header,
                "GUI.BeginGroup(rects.WorkloadActionClip);",
                "the translated action lane must be clipped at the workload selector edge");
            TestAssert.Contains(
                header,
                "if (previewActive || revealProgress > 0.001f)",
                "inactive preview actions must retain geometry while the lane reverses closed");
            TestAssert.Contains(
                header,
                "ClipToWorkloadActionLane(",
                "footer hit-testing must use only the visible translated action rectangles");
            TestAssert.False(
                header.IndexOf("target.width * progress", StringComparison.Ordinal) >= 0,
                "preview reveal must not scale action widths in place");
        }

        private static void FooterActionsAcceptTypedState(
            string gateway,
            string contracts)
        {
            TestAssert.Contains(
                gateway,
                "internal bool CanApplyPreview =>\n            IsActive && !IsMultiplayerCommitInFlight && !HasUnsupportedOwnedPresentationState",
                "Apply must remain enabled for typed schedule/settings payloads while retaining MP and legacy gates");
            TestAssert.Contains(
                gateway,
                "internal bool CanUpdatePreview =>\n            IsActive && !IsMultiplayerCommitInFlight && HasSemanticDiff &&",
                "Update must be semantic-diff driven rather than dimension-presence driven");
            TestAssert.Contains(
                gateway,
                "internal bool CanForkPreview =>\n            IsActive && !IsMultiplayerCommitInFlight && !HasUnsupportedOwnedPresentationState",
                "Fork must share the same typed-state gate as direct commit calls");
            TestAssert.Contains(
                gateway,
                "WorkloadV2OwnershipResolver.HasLegacyPayload(",
                "the footer must fail closed for legacy value-only schedule/presentation payloads");
            TestAssert.Contains(
                contracts,
                "HasTypedPayload(projectedState, WorkloadStateDimension.Schedules)",
                "typed schedules must be the only implicit ownership promotion path");
            TestAssert.Contains(
                contracts,
                "HasTypedPayload(projectedState, WorkloadStateDimension.PresentationSettings)",
                "typed presentation settings must be the only implicit ownership promotion path");
        }

        private static void DynamicOwnershipReachesCommitPayload(
            string backend,
            string session)
        {
            TestAssert.Contains(
                backend,
                "targetTemplate = Workload2Backend.BuildEffectiveTemplate(",
                "commit payloads must be rebuilt with effective ownership");
            TestAssert.Contains(
                backend,
                "WorkloadV2OwnershipResolver.Effective(",
                "dynamic ownership must be resolved by the shared runtime contract");
            TestAssert.Contains(
                backend,
                "WorkloadStateDimension.PresentationSettings",
                "the backend must include workload-owned presentation state in its effective ownership");
            TestAssert.Contains(
                backend,
                "RefreshBackendBaselineIfNeeded(previous, prepared)",
                "preview edits must refresh the authoritative service baseline when a setting key is acquired");
            TestAssert.Contains(
                backend,
                "state.PresentationSettingIntents.Count",
                "dynamic presentation keys must participate in baseline completeness checks");
            TestAssert.Contains(
                backend,
                "_applyService.RememberBackendBaseline(\n                previous.SourceIdentity",
                "the refreshed settings baseline must belong to the active backend session");
            TestAssert.Contains(
                session,
                "WorkloadPresentationSettingIntentEntry",
                "the ownership resolver must inspect typed presentation intent rather than legacy values");
            TestAssert.Contains(
                session,
                "WorkloadOwnershipDimensions ownership = EffectiveOwnership;",
                "session clear validation must observe dynamically acquired ownership");
            TestAssert.Contains(
                session,
                "WorkloadV2OwnershipResolver.Effective(\n                    SourceTemplate,\n                    state,\n                    TemplateBaselineState)",
                "Update and Fork templates must carry effective per-key ownership");
            TestAssert.Contains(
                session,
                "EffectiveOwnership,\n                scope",
                "Apply templates must carry effective per-key ownership");
        }

        private static void IncludeUsesTheAuthoritativeBaselineAndApplyPath(
            string gateway,
            string backend)
        {
            TestAssert.Contains(
                gateway,
                "WorkloadGateway.ExtendV2PreviewBaseline(candidate, pawnKey)",
                "include must hand the candidate to the bound backend instead of constructing a temporary service");
            TestAssert.Contains(
                gateway,
                "CaptureScheduleIntent(",
                "include must retain captured 24-hour schedule state");
            TestAssert.Contains(
                gateway,
                "GetSpecificJobOverride(",
                "include must capture the current specific-job value before planning a commit");
            TestAssert.Contains(
                gateway,
                "SetSpecificJobOrder(",
                "include must retain the current specific-job ordering baseline");
            TestAssert.False(
                gateway.IndexOf("new WorkloadV2ApplyService(", StringComparison.Ordinal) >= 0,
                "the gateway must not install a temporary apply service for included pawns");
            TestAssert.Contains(
                backend,
                "_applyService.RememberBackendBaseline(\n                    previous.SourceIdentity",
                "the real backend apply service must receive the newly captured pawn baseline");
            TestAssert.Contains(
                backend,
                "PreparePreviewCandidate(_previewSession, candidate)",
                "the visible include action must use the same membership/baseline preparation path as edits");
            TestAssert.Contains(
                backend,
                "prepared.Value.RuntimeBaseline.ContainsPawn(pawn)",
                "include must be rejected unless the authoritative runtime baseline contains the pawn");
        }

        private static void LegacyPayloadsRemainFailClosed(
            string gateway,
            string backend,
            string contracts)
        {
            TestAssert.Contains(
                gateway,
                "WorkloadV2OwnershipResolver.HasLegacyPayload(",
                "gateway lifecycle enablement must reject legacy schedule payloads");
            TestAssert.Contains(
                contracts,
                "HasLegacyPayload(state, WorkloadStateDimension.Schedules)",
                "direct template writes must reject legacy schedule payloads");
            TestAssert.Contains(
                contracts,
                "HasLegacyPayload(state, WorkloadStateDimension.PresentationSettings)",
                "direct template writes must reject legacy presentation payloads");
            TestAssert.Contains(
                backend,
                "if (WorkloadV2OwnershipResolver.HasUnsupportedLegacyPayload(targetTemplate))",
                "direct Apply/Update/Fork calls must share the legacy-state gate used by template writes");
            TestAssert.Contains(
                contracts,
                "!HasTypedPayload(state, dimension)",
                "legacy payload detection must not promote value-only state");
            TestAssert.False(
                gateway.IndexOf(
                    "ownership.Owns(WorkloadStateDimension.Schedules) &&\n                        WorkloadV2OwnershipResolver.HasLegacyPayload",
                    StringComparison.Ordinal) >= 0,
                "legacy schedule rejection must not depend on an old ownership bit");
            TestAssert.False(
                gateway.IndexOf(
                    "ownership.Owns(WorkloadStateDimension.PresentationSettings) &&\n                        WorkloadV2OwnershipResolver.HasLegacyPayload",
                    StringComparison.Ordinal) >= 0,
                "legacy presentation rejection must not depend on an old ownership bit");
        }

        private static string FindRepositoryRoot()
        {
            var starts = new List<string>
            {
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory
            };

            for (int startIndex = 0; startIndex < starts.Count; startIndex++)
            {
                string current = Path.GetFullPath(starts[startIndex]);
                for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
                {
                    string gatewayPath = Path.Combine(
                        current,
                        "Source",
                        "UI",
                        "Workloads",
                        "WorkloadGateway.cs");
                    if (File.Exists(gatewayPath))
                    {
                        return current;
                    }

                    DirectoryInfo parent = Directory.GetParent(current);
                    current = parent?.FullName;
                }
            }

            throw new InvalidOperationException(
                "Could not locate the Better Work Tab repository for gateway contracts.");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
            }

            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }
    }
}
