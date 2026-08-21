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
            string renderer = Read(root, "Source", "UI", "WorkGrid", "Rendering", "WorkTabBodyRenderer.cs");
            string chrome = Read(root, "Source", "UI", "Chrome", "WorkTabChrome.cs");
            string inspectionSemantics = Read(
                root,
                "Source",
                "Features",
                "Workloads",
                "V2",
                "WorkloadInspectionSemantics.cs");
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string session = Read(root, "Source", "Features", "Workloads", "V2", "WorkloadSession.cs");
            string english = Read(root, "Languages", "English", "Keyed", "English.xml");
            string settings = Read(root, "Source", "UI", "Settings", "BWTSettingsRegistry.cs");

            FooterSelectorKeepsManagerAndPreviewActionsSeparate(header);
            WorkloadMenuUsesStableIds(header);
            PreviewActionsUseVisibleHitRects(header);
            NarrowFooterGeometryIsBounded(header);
            DeletedFeedbackCopyIsAbsent(english, settings);
            FooterActionsAcceptTypedState(gateway, session);
            InspectionUsesRevisionCachesAndContext(
                header,
                gateway,
                renderer,
                chrome,
                inspectionSemantics);
            DynamicOwnershipReachesCommitPayload(backend, session);
            IncludeUsesTheAuthoritativeBaselineAndApplyPath(gateway, backend);
            LegacyPayloadsRemainFailClosed(gateway, backend, session);
            SaveAndForkRemainActiveAfterPersistence(
                header,
                gateway,
                backend,
                session);
        }

        private static void InspectionUsesRevisionCachesAndContext(
            string header,
            string gateway,
            string renderer,
            string chrome,
            string inspectionSemantics)
        {
            TestAssert.Contains(
                gateway,
                "private void EnsureSemanticDiffCache()",
                "inspection must have an explicit semantic-diff cache boundary");
            TestAssert.Contains(
                gateway,
                "_semanticDiffSessionRevision == _session.SessionRevision",
                "semantic diffs must be reused for an unchanged session revision");
            TestAssert.Contains(
                gateway,
                "_inspectionIndexContext == _inspectionContext",
                "the inspection index must invalidate when Apply/Update context changes");
            TestAssert.Contains(
                gateway,
                "_cachedLiveDiff = liveResult.Succeeded && liveResult.Value != null",
                "Apply inspection must cache the live-impact diff separately");
            TestAssert.Contains(
                gateway,
                "overApply && HasLiveImpact",
                "Apply hover must select the live-impact inspection context");
            TestAssert.Contains(
                gateway,
                "overUpdate && HasTemplateDiff",
                "Save/Update hover must select the template inspection context");
            TestAssert.Contains(
                gateway,
                "overSaveAs && HasTemplateDiff",
                "Save As hover and scroll must select the template inspection context");
            TestAssert.Contains(
                gateway,
                "internal bool HasInspectionRowLevelChanges",
                "legacy schedule-only inspection must expose the cached row-level change index");
            TestAssert.Contains(
                gateway,
                "return _changedSchedulePawnIds.Count > 0",
                "row-level inspection must reuse the cached schedule pawn index");
            TestAssert.Contains(
                gateway,
                "separate row indicator path",
                "membership changes must remain outside the priority-cell overlay index");

            int drawStart = renderer.IndexOf(
                "private void DrawWorkloadInspectionHighlights(",
                StringComparison.Ordinal);
            int bindingStart = renderer.IndexOf(
                "private void EnsureInspectionColumnBindings(",
                drawStart,
                StringComparison.Ordinal);
            TestAssert.True(
                drawStart >= 0 && bindingStart > drawStart,
                "inspection drawing and binding-cache seams must remain explicit");
            string drawPath = renderer.Substring(drawStart, bindingStart - drawStart);
            TestAssert.Contains(
                drawPath,
                "preview.HasInspectionCellTargets",
                "no-cell and membership-only inspections must avoid the draw walk");
            TestAssert.Contains(
                drawPath,
                "preview.HasInspectionRowLevelChanges",
                "legacy schedule-only inspection must use a row-level fast path");
            TestAssert.Contains(
                drawPath,
                "preview.IsInspectionRowLevelChanged(descriptor.Pawn)",
                "legacy schedule-only inspection must draw only affected visible rows");
            TestAssert.True(
                drawPath.IndexOf(
                    "preview.HasInspectionRowLevelChanges",
                    StringComparison.Ordinal) <
                drawPath.IndexOf(
                    "if (!preview.HasInspectionCellTargets)",
                    StringComparison.Ordinal),
                "row-level schedule inspection must run even when normal cell targets also exist");
            TestAssert.Contains(
                drawPath,
                "preview.InspectionTargets",
                "cell inspection must enumerate the cached changed targets");
            TestAssert.Contains(
                drawPath,
                "_inspectionGlobalColumns",
                "global inspection changes must aggregate affected columns before row drawing");
            TestAssert.Contains(
                drawPath,
                "_inspectionCellMasks",
                "inspection changes must aggregate one semantic mask per visible cell");
            TestAssert.Contains(
                drawPath,
                "WorkGridInteractionGeometry.GetAnimatedBodyContentRect",
                "inspection must recalculate animated body geometry each draw");
            TestAssert.False(
                drawPath.IndexOf("TryGetWorkGiverForColumn", StringComparison.Ordinal) >= 0,
                "sub-work semantic resolution must not occur inside the row/cell hot loop");
            TestAssert.Contains(
                header,
                "rects.HasWorkloadSaveAs ? rects.WorkloadSaveAs : Rect.zero",
                "Save As inspection routing must use its clipped visible hit rectangle");

            TestAssert.Contains(
                renderer,
                "WorkPriorityCellGeometry.GetDrawnPriorityBoxRect",
                "priority inspection must use the authoritative drawn box geometry");
            TestAssert.Contains(
                renderer,
                "GetSpecificColumns(target.WorkType, target.WorkGiver)",
                "specific inspection targets must use the direct semantic index");
            TestAssert.Contains(
                renderer,
                "GetOrderingColumns(target.WorkType)",
                "ordering inspection targets must use the direct semantic index");
            TestAssert.Contains(
                renderer,
                "right: true",
                "ordering inspection must retain a distinct right-edge marker");
            TestAssert.Contains(
                inspectionSemantics,
                "_values[key] = existing | kind",
                "inspection semantic kinds must compose rather than use precedence");
            TestAssert.Contains(
                gateway,
                "_hasManualModeInspectionChange",
                "manual mode inspection must remain a single global semantic marker");
            TestAssert.Contains(
                chrome,
                "GetManualPrioritiesCheckboxRect",
                "manual-priority input and inspection geometry must share the chrome helper");
            TestAssert.Contains(
                renderer,
                "_inspectionBindingSubWorkRevision = subWorkRevision",
                "sub-work binding semantics must be cached at the existing layout revision boundary");
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

        private static void WorkloadMenuUsesStableIds(string header)
        {
            int start = header.IndexOf(
                "private static List<FloatMenuOption> BuildWorkloadPickerOptions(",
                StringComparison.Ordinal);
            int end = header.IndexOf(
                "private static void OpenWorkloadManagementMenu(",
                start,
                StringComparison.Ordinal);
            TestAssert.True(
                start >= 0 && end > start,
                "the workload picker must remain a distinct FloatMenu construction path");

            string rowPath = header.Substring(start, end - start);
            TestAssert.Contains(
                rowPath,
                "() => SelectWorkloadInline(stableId)",
                "a workload menu option must capture the stable ID for selection");
            TestAssert.Contains(
                rowPath,
                "() => OpenWorkloadManagementMenu(stableId)",
                "the management menu option must capture the current stable ID");
            TestAssert.False(
                rowPath.IndexOf("preview.SelectWorkload", StringComparison.Ordinal) >= 0,
                "a workload menu option must not start or switch a projected preview");
            TestAssert.False(
                rowPath.IndexOf("BeginCurrentPreview", StringComparison.Ordinal) >= 0,
                "only the workload main button may enter preview");
            TestAssert.Contains(
                header,
                "WorkloadGateway.SelectWorkload(stableId)",
                "a workload menu selection must route through the gateway by stable ID");
            TestAssert.Contains(
                header,
                "stableId: stableId",
                "rename callbacks must retain stable workload identity");
            TestAssert.Contains(
                header,
                "new FloatMenu(BuildWorkloadPickerOptions())",
                "the visible workload picker must use the native FloatMenu stack");
            TestAssert.False(
                header.IndexOf("DrawWorkloadFooterPicker", StringComparison.Ordinal) >= 0,
                "the bespoke workload picker panel must be removed");
        }

        private static void PreviewActionsUseVisibleHitRects(string header)
        {
            int start = header.IndexOf(
                "private static void DrawWorkloadPreviewButton(",
                StringComparison.Ordinal);
            int end = header.IndexOf(
                "private static Rect ToWorkloadActionGroup(",
                start,
                StringComparison.Ordinal);
            TestAssert.True(
                start >= 0 && end > start,
                "the preview action painter must remain a single shared draw/input helper");

            string actionPath = header.Substring(start, end - start);
            TestAssert.Contains(
                actionPath,
                "Widgets.ButtonInvisible(hitRect)",
                "partially revealed actions must only accept input in their visible hit rectangle");
            TestAssert.Contains(
                actionPath,
                "Widgets.ButtonText(drawRect, label, active: enabled)",
                "fully revealed actions must retain the native footer button rendering");
            TestAssert.Contains(
                actionPath,
                "Widgets.DrawBoxSolid(\n                    drawRect",
                "partially revealed actions must still draw from their translated rectangle");
            TestAssert.Contains(
                actionPath,
                "Widgets.DrawHighlight(hitRect)",
                "action hover feedback must follow the visible hit rectangle");
        }

        private static void NarrowFooterGeometryIsBounded(string header)
        {
            TestAssert.Contains(
                header,
                "private static void LayoutNormalFooter(",
                "normal footer geometry must have an explicit bounded layout pass");
            TestAssert.Contains(
                header,
                "float minimumBothWidth",
                "normal footer geometry must reserve compact selector minimums before placing controls");
            TestAssert.Contains(
                header,
                "TakeFromRight(",
                "normal footer controls must be placed through the shared bounded geometry helper");
            TestAssert.Contains(
                header,
                "float minimumWorkloadWidth",
                "narrow footer geometry must compact only the workload naming half");
            TestAssert.False(
                header.IndexOf("CompactRulesetMain", StringComparison.Ordinal) >= 0,
                "ruleset selector metrics must not acquire workload-specific compact rendering");
            TestAssert.Contains(
                header,
                "Mathf.Max(1f, inRect.width - 8f)",
                "the workload editor surface must be bounded by the available Work-tab content");
            TestAssert.False(
                header.IndexOf("Widgets.BeginScrollView", StringComparison.Ordinal) >= 0,
                "the workload picker must not retain bespoke scroll-panel rendering");
        }

        private static void DeletedFeedbackCopyIsAbsent(string english, string settings)
        {
            TestAssert.False(
                english.IndexOf("feedback", StringComparison.OrdinalIgnoreCase) >= 0,
                "deleted feedback feature copy must not remain in shipped English text");
            TestAssert.False(
                settings.IndexOf("Beta Testing Phase", StringComparison.OrdinalIgnoreCase) >= 0,
                "deleted beta-testing copy must not remain in shipped settings text");
            TestAssert.Contains(
                english,
                "BWT_Tutorial_FinishLesson",
                "unrelated tutorial lesson text must remain available");
        }

        private static void FooterActionsAcceptTypedState(
            string gateway,
            string contracts)
        {
            TestAssert.Contains(
                gateway,
                "internal bool CanApplyPreview =>\n            IsActive && !IsUnsafePreviewInputBlocked && !HasUnsupportedOwnedPresentationState",
                "Apply must remain enabled for typed schedule/settings payloads while retaining MP and legacy gates");
            TestAssert.Contains(
                gateway,
                "internal bool CanUpdatePreview =>\n            IsActive && !IsUnsafePreviewInputBlocked && HasSemanticDiff &&",
                "Update must be semantic-diff driven rather than dimension-presence driven");
            TestAssert.Contains(
                gateway,
                "internal bool CanForkPreview =>\n            IsActive && !IsUnsafePreviewInputBlocked && !HasUnsupportedOwnedPresentationState",
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

        private static void SaveAndForkRemainActiveAfterPersistence(
            string header,
            string gateway,
            string backend,
            string session)
        {
            TestAssert.Contains(
                header,
                "\"Save\",",
                "the visible Update action must be labeled Save");
            TestAssert.Contains(
                header,
                "Save applies this semantic diff",
                "Save hover text must describe persistence-only replacement");
            TestAssert.Contains(
                session,
                "internal WorkloadSession RebaseAfterPersistence(",
                "Save/Fork must use an explicit nonterminal session rebase seam");

            int updateStart = session.IndexOf(
                "public WorkloadSessionDecision Update()",
                StringComparison.Ordinal);
            int planStart = session.IndexOf(
                "private WorkloadPreviewPlan BuildPlan(",
                updateStart,
                StringComparison.Ordinal);
            TestAssert.True(
                updateStart >= 0 && planStart > updateStart,
                "the nonterminal Update/Fork planning region must remain explicit");
            string updateForkPlanning = session.Substring(updateStart, planStart - updateStart);
            TestAssert.False(
                updateForkPlanning.IndexOf("TerminalSession(", StringComparison.Ordinal) >= 0,
                "Save and Save As planning must not terminalize the preview");
            TestAssert.Contains(
                session,
                "return rebased.TemplateDiff.IsEmpty ? rebased : null;",
                "session rebase must fail closed unless the committed target is the new baseline");

            TestAssert.Contains(
                backend,
                "BuildPersistenceReceipt(",
                "persistence commits must produce a receipt from the round-tripped target");
            TestAssert.Contains(
                backend,
                "RekeyBackendBaseline(",
                "the backend baseline must be rekeyed from the old source identity");
            TestAssert.Contains(
                backend,
                "CurrentWorkloadIdChanged",
                "Fork current-ID activation must belong to the rollback-aware persistence mutation");

            int updatePreviewStart = gateway.IndexOf(
                "internal bool UpdatePreview()",
                StringComparison.Ordinal);
            int forkPreviewStart = gateway.IndexOf(
                "internal bool ForkPreview(",
                updatePreviewStart,
                StringComparison.Ordinal);
            int resetStart = gateway.IndexOf(
                "internal void ResetForWindowClose()",
                forkPreviewStart,
                StringComparison.Ordinal);
            TestAssert.True(
                updatePreviewStart >= 0 && forkPreviewStart > updatePreviewStart &&
                resetStart > forkPreviewStart,
                "Save and Save As controller boundaries must remain explicit");
            string updatePreview = gateway.Substring(
                updatePreviewStart,
                forkPreviewStart - updatePreviewStart);
            string forkPreview = gateway.Substring(
                forkPreviewStart,
                resetStart - forkPreviewStart);
            TestAssert.Contains(
                updatePreview,
                "AdoptRebasedPreview(result)",
                "Save must adopt the backend-rebased preview instead of closing it");
            TestAssert.Contains(
                forkPreview,
                "AdoptRebasedPreview(result)",
                "Save As must adopt the new active identity without closing the preview");
            TestAssert.False(
                updatePreview.IndexOf("ClearLocalSession()", StringComparison.Ordinal) >= 0 ||
                forkPreview.IndexOf("ClearLocalSession()", StringComparison.Ordinal) >= 0,
                "successful Save and Save As must not clear the local preview session");
            TestAssert.Contains(
                gateway,
                "WorkloadGateway.AdoptV2PreviewSession(result.RebasedSession)",
                "the controller must adopt the backend-authoritative rebased session");
            TestAssert.Contains(
                gateway,
                "WorkloadGateway.RebaseV2PreviewAfterPersistence(",
                "the controller must rebase the UI backend from the final MP persistence receipt");
            TestAssert.Contains(
                gateway,
                "RebuildProjection(_session.ProjectedState)",
                "rebasing must rebuild the projected provider and invalidate inspection state");
        }

        private static string FindRepositoryRoot()
        {
            return TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Workloads", "WorkloadGateway.cs"),
                "gateway contracts");
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
