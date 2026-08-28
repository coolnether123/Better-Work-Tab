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
            string headerHost = Read(root, "Source", "UI", "HeaderButtons.cs");
            string workloadFooter = Read(
                root,
                "Source",
                "UI",
                "Workloads",
                "WorkloadFooterFeature.cs");
            string header = headerHost + "\n" + workloadFooter;
            string selector = Read(root, "Source", "UI", "BWTBottomBarSelector.cs");
            string gateway = Read(root, "Source", "UI", "Workloads", "WorkloadGateway.cs");
            string renderer = Read(root, "Source", "UI", "WorkGrid", "Rendering", "WorkTabBodyRenderer.cs");
            string chrome = Read(root, "Source", "UI", "Chrome", "WorkTabChrome.cs");
            string contextRouter = Read(root, "Source", "UI", "Settings", "BWTWorkTabContextSettingsRouter.cs");
            string settingsModel = Read(root, "Source", "BetterWorkTabSettings.cs");
            string settingIds = Read(root, "Source", "UI", "Settings", "SettingIDs.cs");
            string settingsTranslations = Read(root, "Languages", "English", "Keyed", "BWT_Settings.xml");
            string mainWindow = Read(root, "Source", "UI", "MainTabWindow_BetterWork.cs");
            string tutorial = Read(
                root,
                "Source",
                "Features",
                "Tutorial",
                "BWTGeneralTutorial.cs");
            string tutorialCoordinator = Read(
                root,
                "Source",
                "Features",
                "Tutorial",
                "BWTWorkTabTutorial.cs");
            string effectiveStateContracts = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Projection",
                "WorkTabEffectiveStateContracts.cs");
            string effectiveStateRuntime = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Projection",
                "WorkTabEffectiveStateRuntime.cs");
            string effectiveStateScope = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Projection",
                "WorkTabEffectiveStateScope.cs");
            string projectedProvider = Read(
                root,
                "Source",
                "UI",
                "Workloads",
                "Projection",
                "ProjectedWorkTabEffectiveStateProvider.cs");
            string liveProvider = Read(
                root,
                "Source",
                "UI",
                "Workloads",
                "Projection",
                "LiveWorkTabEffectiveStateProvider.cs");
            string subWorkPresentationCache = Read(
                root,
                "Source",
                "UI",
                "WorkGiverReassignments",
                "WorkGiverCellPresentationCache.cs");
            string interactionRouter = Read(root, "Source", "UI", "WorkGrid", "Interaction", "WorkGridInteractionRouter.cs");
            string footerContextController = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Interaction",
                "WorkTabContextSettingsInteractionController.cs");
            string inspectionSemantics = Read(
                root,
                "Source",
                "UI",
                "WorkGrid",
                "Rendering",
                "WorkGridInspectionSemantics.cs");
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string session = Read(root, "Source", "Features", "Workloads", "V2", "WorkloadSession.cs");
            string english = Read(root, "Languages", "English", "Keyed", "English.xml");
            string settings = Read(root, "Source", "UI", "Settings", "BWTSettingsRegistry.cs");

            FooterSelectorKeepsManagerAndPreviewActionsSeparate(header, gateway, mainWindow);
            WorkloadMenuUsesStableIds(header);
            WorkloadSelectorUsesOnlyTheWorkloadName(header);
            PreviewActionsUseVisibleHitRects(header);
            LifecycleRefreshUsesApplicationPublicationReceipt(header, gateway, backend);
            RepositoryLifecycleActionsAvoidPawnTableRecache(header, backend);
            NarrowFooterGeometryIsBounded(header);
            SelectorSpacingIsMeasuredWithoutLeadingReserve(selector);
            SelectorUsesLegacyMinimumAndMeasuredGrowth(header);
            SelectorLabelsClipWithoutInjectedEllipsis(selector);
            SelectorLabelsAreCentered(selector);
            WorkloadFooterContextRoutingIsClippedAndSettingsBacked(
                header,
                contextRouter,
                settingsModel,
                settingIds,
                settingsTranslations,
                mainWindow,
                interactionRouter,
                footerContextController,
                settings);
            PreviewStructuralSuppressionStaysCompatible(contextRouter);
            DeletedFeedbackCopyIsAbsent(english, settings);
            WorkloadPresentationOwnsTutorialAndSelectionLifecycle(
                gateway,
                tutorial,
                tutorialCoordinator,
                english);
            FooterActionsAcceptTypedState(gateway, session);
            InspectionUsesRevisionCachesAndContext(
                header,
                gateway,
                renderer,
                chrome,
                inspectionSemantics);
            WorkTabViewCapturesEffectiveStateBeforeInput(
                mainWindow,
                effectiveStateContracts,
                effectiveStateRuntime,
                effectiveStateScope,
                projectedProvider);
            EffectiveStateCachesFollowSemanticRevision(
                effectiveStateRuntime,
                effectiveStateScope,
                liveProvider,
                subWorkPresentationCache);
            DynamicOwnershipReachesCommitPayload(backend, session);
            IncludeUsesTheAuthoritativeBaselineAndApplyPath(gateway, backend);
            LegacyPayloadsRemainFailClosed(gateway, backend, session);
            SaveAndForkRemainActiveAfterPersistence(
                header,
                gateway,
                backend,
                session);
        }

        private static void WorkloadPresentationOwnsTutorialAndSelectionLifecycle(
            string gateway,
            string tutorial,
            string tutorialCoordinator,
            string english)
        {
            string active = MethodBody(tutorial, "internal static bool IsActive");
            TestAssert.Contains(
                active,
                "!WorkTabEffectiveStateRuntime.IsPreviewActive",
                "a workload presentation must temporarily suppress tutorial drawing and input");

            string openSession = MethodBody(gateway, "private void OpenSession(");
            int projection = openSession.IndexOf(
                "RebuildProjection(_session.ProjectedState);",
                StringComparison.Ordinal);
            int clearSelection = openSession.IndexOf(
                "ColumnSelectionManager.Clear();",
                StringComparison.Ordinal);
            int clearTutorial = openSession.IndexOf(
                "BWTWorkTabTutorial.NotifyWorkloadPresentationOpened();",
                StringComparison.Ordinal);
            TestAssert.True(
                projection >= 0 && clearSelection > projection && clearTutorial > clearSelection,
                "only a successfully installed presentation may clear transient header and tutorial selection state");

            string notification = MethodBody(
                tutorial,
                "internal static void NotifyWorkloadPresentationOpened()");
            string transientClear = MethodBody(
                tutorial,
                "private static void ClearTransientInteractionState()");
            TestAssert.Contains(
                notification,
                "ClearTransientInteractionState();",
                "presentation activation must release tutorial pointer and pinned-selector ownership");
            TestAssert.Contains(
                notification,
                "observedLessonId = string.Empty;",
                "a resumed tutorial lesson must recapture its live presentation observation baseline");
            TestAssert.Contains(
                transientClear,
                "Selector.ClearPinnedSelection();",
                "a pinned tutorial popup must not reappear after the presentation closes");
            TestAssert.Contains(
                transientClear,
                "BWTTutorialGestureDemo.Reset();",
                "tutorial gesture presentation must not survive a workload presentation switch");
            TestAssert.False(
                notification.IndexOf("BetterWorkTabMod.Settings", StringComparison.Ordinal) >= 0 ||
                notification.IndexOf("settings.Write", StringComparison.Ordinal) >= 0 ||
                transientClear.IndexOf("BetterWorkTabMod.Settings", StringComparison.Ordinal) >= 0 ||
                transientClear.IndexOf("settings.Write", StringComparison.Ordinal) >= 0,
                "presentation activation must not mutate durable tutorial preferences or progress");

            string coordinatorNotification = MethodBody(
                tutorialCoordinator,
                "internal static void NotifyWorkloadPresentationOpened()");
            TestAssert.Contains(
                coordinatorNotification,
                "BWTGeneralTutorial.NotifyWorkloadPresentationOpened();",
                "workload UI must release tutorial state through its existing coordinator boundary");

            string closeSession = MethodBody(gateway, "private void ClearLocalSession()");
            TestAssert.False(
                closeSession.IndexOf("ColumnSelectionManager.Select", StringComparison.Ordinal) >= 0,
                "closing a presentation must not restore stale header selection from the prior surface");
            TestAssert.False(
                tutorial.IndexOf("BWT_Tutorial_WorkloadPresentation_Body", StringComparison.Ordinal) >= 0 ||
                english.IndexOf("BWT_Tutorial_WorkloadPresentation_Body", StringComparison.Ordinal) >= 0,
                "the old partial tutorial-over-preview presentation must not remain reachable or translated");
        }

        private static void EffectiveStateCachesFollowSemanticRevision(
            string effectiveStateRuntime,
            string effectiveStateScope,
            string liveProvider,
            string subWorkPresentationCache)
        {
            TestAssert.Contains(
                effectiveStateRuntime,
                "pass.Revision == revision",
                "captured effective-state views must be reused while their semantic revision is unchanged");
            TestAssert.Contains(
                effectiveStateRuntime,
                "pass.CapturedView = view;",
                "the effective-state render pass must retain its captured immutable view");

            string dispose = MethodBody(effectiveStateScope, "public void Dispose()");
            TestAssert.False(
                dispose.IndexOf("InvalidateRenderPass", StringComparison.Ordinal) >= 0,
                "restoring a nested scope must not manufacture a new effective-state revision");
            TestAssert.False(
                subWorkPresentationCache.IndexOf(
                    "_effectiveStateRenderPassId",
                    StringComparison.Ordinal) >= 0,
                "sub-work presentation retention must not be keyed to transient render-pass identity");
            TestAssert.Contains(
                subWorkPresentationCache,
                "_effectiveStateRevision != currentRevision",
                "sub-work presentation retention must invalidate on the semantic effective-state revision");
            TestAssert.Contains(
                liveProvider,
                "if (_specificPriorities == null)",
                "captured live views must allocate dimension caches only when that dimension is read");
        }

        private static void WorkTabViewCapturesEffectiveStateBeforeInput(
            string mainWindow,
            string effectiveStateContracts,
            string effectiveStateRuntime,
            string effectiveStateScope,
            string projectedProvider)
        {
            TestAssert.Contains(
                effectiveStateContracts,
                "interface IWorkTabEffectiveStateViewSource",
                "effective-state providers must expose one optional generalized capture seam");
            TestAssert.Contains(
                effectiveStateRuntime,
                "CaptureCurrentView(",
                "the runtime must capture an optional provider view without naming a concrete provider");
            TestAssert.False(
                effectiveStateRuntime.IndexOf(
                    "ProjectedWorkTabEffectiveStateProvider",
                    StringComparison.Ordinal) >= 0,
                "the runtime must not depend on the projected provider implementation");
            TestAssert.Contains(
                projectedProvider,
                "IWorkTabEffectiveStateViewSource",
                "the projected provider must supply the generalized capture capability");
            TestAssert.Contains(
                projectedProvider,
                "Build replacement indexes and publish",
                "projected indexes must be copy-on-write while captured views are still rendering");

            string frame = MethodBody(mainWindow, "private void DoWindowContentsProfiledCoreScoped(");
            int build = frame.IndexOf("BuildWorkTabView(", StringComparison.Ordinal);
            int capturedScope = frame.IndexOf(
                "WorkTabEffectiveStateScope.PushCapturedView(",
                StringComparison.Ordinal);
            int input = frame.IndexOf("RouteFrameInput(in view", StringComparison.Ordinal);
            int render = frame.IndexOf("RenderWorkGrid(in view", StringComparison.Ordinal);
            int overlays = frame.IndexOf("DrawFrameOverlaysAndChrome(in view", StringComparison.Ordinal);
            TestAssert.True(
                build >= 0 && capturedScope > build && input > capturedScope &&
                render > input && overlays > render,
                "one captured WorkTabView scope must own input, rendering, and overlays for the full pass");
            TestAssert.Contains(
                effectiveStateScope,
                "internal static System.IDisposable PushCapturedView(",
                "the scope must distinguish immutable render reads from the mutable input scope");
            TestAssert.Contains(
                effectiveStateScope,
                "if (FollowsPreview && Provider != null && Provider.IsPreview",
                "disposing a captured view must not clear the live preview cache as though it were replaced");
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
                "_cachedLiveDiff = _session.LiveDiff;",
                "Apply inspection must cache the live-impact diff separately");
            TestAssert.Contains(
                gateway,
                "_cachedTemplateDiff = _session.TemplateDiff;",
                "Update inspection must reuse the immutable session template diff");
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
                "public bool HasInspectionRowLevelChanges",
                "legacy schedule-only inspection must expose the cached row-level change index");
            TestAssert.Contains(
                gateway,
                "return _inspectionReadState.HasRowLevelChanges;",
                "row-level inspection must reuse the cached schedule pawn index");
            TestAssert.Contains(
                gateway,
                "separate row indicator path",
                "membership changes must remain outside the priority-cell overlay index");

            int drawStart = renderer.IndexOf(
                "private void DrawPreviewInspectionHighlights(",
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
            TestAssert.False(
                drawPath.IndexOf("Workload", StringComparison.Ordinal) >= 0 ||
                drawPath.IndexOf("BWTWorkTabEffectiveSettings", StringComparison.Ordinal) >= 0,
                "renderer inspection must consume only captured neutral preview state");
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
                "rects.HasOptionalSaveAs ? rects.OptionalSaveAs : Rect.zero",
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
                "HasManualModeChange",
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
            string header,
            string gateway,
            string mainWindow)
        {
            string frameInput = MethodBody(mainWindow, "private void RouteFrameInput(");
            string inputPath = MethodBody(header, "public bool TryHandleInput(");
            string footerDrawPath = MethodBody(header, "public static void DrawBottomRightGrouped(");
            string drawPath = MethodBody(header, "private void DrawWorkloadGroup(");
            string previewDrawPath = MethodBody(header, "private void DrawPreviewActions(");
            string executionPath = MethodBody(header, "private void ExecuteFooterControl(");

            TestAssert.Contains(
                footerDrawPath,
                "HeaderFooterFeatureRegistry.Current?.Draw(rects)",
                "footer drawing must retain the visible workload selector");
            TestAssert.False(
                footerDrawPath.IndexOf("ExecuteFooterControl(", StringComparison.Ordinal) >= 0 ||
                footerDrawPath.IndexOf("QueuePreviewLifecycleAction(", StringComparison.Ordinal) >= 0,
                "footer drawing must not execute lifecycle actions");
            TestAssert.False(
                inputPath.IndexOf("DrawWorkloadGroup(", StringComparison.Ordinal) >= 0 ||
                inputPath.IndexOf("DrawPreviewActions(", StringComparison.Ordinal) >= 0,
                "footer input dispatch must not redraw controls");
            TestAssert.Contains(
                inputPath,
                "TryResolveFooterControl(",
                "footer input must resolve a visible control before executing it");
            TestAssert.Contains(
                inputPath,
                "overFooterControl && pressedControl == hoveredFooterControl",
                "footer input must dispatch only the completed click on the same visible control");
            TestAssert.True(
                CountOccurrences(inputPath, "ExecuteFooterControl(hoveredFooterControl, preview)") == 1,
                "each resolved footer control must have one reachable input dispatch");
            TestAssert.True(
                CountOccurrences(frameInput, "HeaderButtons.TryHandleOptionalFooterInput(") == 1,
                "the Work window must dispatch footer input exactly once per input pass");
            TestAssert.Contains(
                drawPath,
                "DrawOptionalMainControl(",
                "the workload name control must retain a dedicated draw call");
            TestAssert.Contains(
                drawPath,
                "DrawOptionalMenuControl(",
                "the workload ellipsis control must retain a dedicated draw call");
            TestAssert.False(
                drawPath.IndexOf("ExecuteFooterControl", StringComparison.Ordinal) >= 0,
                "selector drawing must not execute footer lifecycle actions");
            TestAssert.False(
                previewDrawPath.IndexOf("QueuePreviewLifecycleAction(", StringComparison.Ordinal) >= 0,
                "preview-action drawing must not execute lifecycle actions");
            TestAssert.Contains(
                executionPath,
                "() => preview.BeginCurrentPreview(),",
                "the selected modern workload name button must enter the projected preview");
            TestAssert.Contains(
                executionPath,
                "BeginWorkloadFooterEditor(createNew: true)",
                "the empty name button must open the save-workload editor");
            int menuBranch = executionPath.IndexOf(
                "if (control == FooterControl.Menu)",
                StringComparison.Ordinal);
            int pickerAction = executionPath.IndexOf(
                "OpenWorkloadFooterPicker();",
                StringComparison.Ordinal);
            int mainAction = executionPath.IndexOf(
                "bool hasWorkload = WorkloadGateway.HasCurrentWorkload();",
                StringComparison.Ordinal);
            int previewAction = executionPath.IndexOf(
                "() => preview.BeginCurrentPreview(),",
                StringComparison.Ordinal);
            TestAssert.True(
                menuBranch >= 0 && pickerAction > menuBranch && mainAction > pickerAction &&
                previewAction > mainAction,
                "the menu branch must return through the picker before main-control preview or editor actions");
            TestAssert.Contains(
                header,
                "rects.HasOptionalPreview ? rects.OptionalApply : Rect.zero",
                "Apply must share the footer inspection hover geometry");
            TestAssert.Contains(
                header,
                "rects.HasOptionalUpdate ? rects.OptionalUpdate : Rect.zero",
                "Update must retain the footer inspection hover geometry");
            int createStart = gateway.IndexOf(
                "internal bool CreateWorkload(string label, out WorkloadDescriptor descriptor)",
                StringComparison.Ordinal);
            int createEnd = gateway.IndexOf(
                "internal bool RenameWorkload(string stableId, string label)",
                createStart,
                StringComparison.Ordinal);
            TestAssert.True(
                createStart >= 0 && createEnd > createStart,
                "workload creation must remain an isolated lifecycle operation");
            string createPath = gateway.Substring(createStart, createEnd - createStart);
            TestAssert.Contains(
                createPath,
                "WorkloadGateway.CreateWorkload(label)",
                "saving a workload must use the current Work-tab capture path");
            TestAssert.False(
                createPath.IndexOf("BeginCurrentPreview()", StringComparison.Ordinal) >= 0,
                "saving the captured Work-tab state must not reopen it as a ghost preview");
            TestAssert.Contains(
                header,
                "if (previewActive)",
                "preview footer geometry must switch with the active preview state");
            TestAssert.Contains(
                header,
                "preview.HasSemanticDiff,",
                "the atomic preview layout must still expose Update when the draft changed");
            TestAssert.False(
                header.IndexOf("SpineEasing.Move01(", StringComparison.Ordinal) >= 0,
                "preview lifecycle state must not be prolonged by a second animated footer state");
            TestAssert.False(
                header.IndexOf("revealProgress", StringComparison.Ordinal) >= 0,
                "preview footer geometry must not interpolate after the preview state changes");
            TestAssert.False(
                header.IndexOf("WorkloadActionClip", StringComparison.Ordinal) >= 0,
                "atomic preview buttons must not retain the old partial-action clipping lane");
            TestAssert.False(
                header.IndexOf("target.width * progress", StringComparison.Ordinal) >= 0,
                "preview reveal must not scale action widths in place");
        }

        private static void WorkloadMenuUsesStableIds(string header)
        {
            int start = header.IndexOf(
                "private List<FloatMenuOption> BuildWorkloadPickerOptions(",
                StringComparison.Ordinal);
            int end = header.IndexOf(
                "private void OpenWorkloadManagementMenu(",
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
                "private void DrawWorkloadPreviewButton(",
                StringComparison.Ordinal);
            int end = header.IndexOf(
                "private void QueuePreviewLifecycleAction(",
                start,
                StringComparison.Ordinal);
            int resolver = header.IndexOf(
                "private bool TryResolvePreviewAction(",
                StringComparison.Ordinal);
            int resolverEnd = header.IndexOf(
                "private bool TryResolveFooterPopoverAction(",
                resolver,
                StringComparison.Ordinal);
            TestAssert.True(
                start >= 0 && end > start && resolver > end && resolverEnd > resolver,
                "preview action drawing and visible-hit resolution must remain separate helpers");

            string drawPath = header.Substring(start, end - start);
            string resolverPath = header.Substring(resolver, resolverEnd - resolver);
            TestAssert.Contains(
                drawPath,
                "Widgets.ButtonText(rect, label, active: enabled)",
                "preview actions must retain the native footer button rendering");
            TestAssert.False(
                drawPath.IndexOf("Widgets.DrawBoxSolid(", StringComparison.Ordinal) >= 0,
                "preview actions must not switch to a custom solid renderer while partially revealed");
            TestAssert.False(
                drawPath.IndexOf("Event.current", StringComparison.Ordinal) >= 0,
                "preview action drawing must not consume input events");
            TestAssert.Contains(
                resolverPath,
                "rects.OptionalApply.Contains(position)",
                "Apply input must use the same final rectangle that is drawn");
            TestAssert.Contains(
                resolverPath,
                "rects.OptionalCancel.Contains(position)",
                "Cancel input must use the same final rectangle that is drawn");
            TestAssert.False(
                resolverPath.IndexOf("DrawWorkloadPreviewButton", StringComparison.Ordinal) >= 0,
                "hit resolution must not redraw preview actions while processing input");
            TestAssert.False(
                drawPath.IndexOf("GUI.BeginGroup(", StringComparison.Ordinal) >= 0,
                "atomic preview actions must not carry a stale clipping group");
        }

        private static void LifecycleRefreshUsesApplicationPublicationReceipt(
            string header,
            string gateway,
            string backend)
        {
            TestAssert.Contains(
                header,
                "WhenApplicationPublicationIsMissing",
                "live preview commits must use a change-aware table refresh policy");
            TestAssert.Contains(
                header,
                "ConsumeLifecycleApplicationPublication()",
                "footer completion must consume the application publication receipt");
            TestAssert.False(
                header.IndexOf("notifyPawnTables:", StringComparison.Ordinal) >= 0,
                "lifecycle refresh policy must not remain an untyped boolean option");
            TestAssert.Contains(
                gateway,
                "RecordLifecycleApplicationPublication(result.ApplicationPublication)",
                "preview commits must forward the backend publication receipt");
            TestAssert.Contains(
                gateway,
                "WorkloadApplicationPublication.Pending",
                "accepted multiplayer commits must defer table refresh until confirmation");
            TestAssert.Contains(
                backend,
                "ApplicationPublication = provisional",
                "commit results must report whether application publication is applied or pending");
            TestAssert.Contains(
                backend,
                "private bool NotifyCommitChanged(LiveMutationTransaction live)",
                "the backend must return publication ownership to its lifecycle caller");
        }

        private static void RepositoryLifecycleActionsAvoidPawnTableRecache(
            string header,
            string backend)
        {
            AssertLifecyclePolicy(
                header,
                "preview.CreateWorkload(label, out unusedDescriptor)",
                "None",
                "creating a repository record must not recache every pawn table");
            AssertLifecyclePolicy(
                header,
                "preview.RenameWorkload(stableId, label)",
                "None",
                "renaming a repository record must not recache every pawn table");
            AssertLifecyclePolicy(
                header,
                "preview.DeleteWorkload(stableId)",
                "None",
                "deleting a repository record must not recache every pawn table");

            int notifyStart = backend.IndexOf(
                "private bool NotifyCommitChanged(LiveMutationTransaction live)",
                StringComparison.Ordinal);
            int notifyEnd = backend.IndexOf(
                "private static bool RollbackPersistence(",
                notifyStart,
                StringComparison.Ordinal);
            TestAssert.True(
                notifyStart >= 0 && notifyEnd > notifyStart,
                "workload commit publication must remain an isolated lifecycle helper");
            string notifyPath = backend.Substring(notifyStart, notifyEnd - notifyStart);
            TestAssert.Contains(
                notifyPath,
                "notifyPawnTables: false",
                "persistence-only workload commits must not recache pawn tables");
        }

        private static void AssertLifecyclePolicy(
            string source,
            string actionMarker,
            string expectedPolicy,
            string message)
        {
            int actionStart = source.IndexOf(actionMarker, StringComparison.Ordinal);
            int actionEnd = source.IndexOf(
                ");",
                actionStart + actionMarker.Length,
                StringComparison.Ordinal);
            TestAssert.True(
                actionStart >= 0 && actionEnd > actionStart,
                "the expected workload lifecycle action must remain present");
            string action = source.Substring(actionStart, actionEnd - actionStart);
            TestAssert.Contains(
                action,
                "LifecycleTableRefreshPolicy." + expectedPolicy,
                message);
        }

        private static void PreviewStructuralSuppressionStaysCompatible(
            string contextRouter)
        {
            TestAssert.False(
                contextRouter.IndexOf("BlocksDescendants", StringComparison.Ordinal) >= 0,
                "preview routing must not depend on an unsupported Spine suppression field");
            int suppressionStart = contextRouter.IndexOf(
                "definition.Suppressions.Add(new SettingSuppression",
                StringComparison.Ordinal);
            int structuralBranch = contextRouter.IndexOf(
                "if (string.Equals(definition.ParentId",
                suppressionStart,
                StringComparison.Ordinal);
            TestAssert.True(
                suppressionStart >= 0 && structuralBranch > suppressionStart,
                "preview suppression registration must remain discoverable");
            string suppression = contextRouter.Substring(
                suppressionStart,
                structuralBranch - suppressionStart);
            TestAssert.Contains(
                suppression,
                "!IsPreviewStructuralDefinition(definition)",
                "structural preview rows must not activate ancestor suppression");
        }

        private static void WorkloadSelectorUsesOnlyTheWorkloadName(string header)
        {
            int start = header.IndexOf(
                "private string WorkloadLabel()",
                StringComparison.Ordinal);
            int end = header.IndexOf(
                "private string WorkloadGeometryLabel()",
                start,
                StringComparison.Ordinal);
            TestAssert.True(
                start >= 0 && end > start,
                "the workload selector label must remain a distinct helper");

            string labelPath = header.Substring(start, end - start);
            TestAssert.Contains(
                labelPath,
                "return preview.SourceLabel;",
                "the workload selector must show only the active workload name");
            TestAssert.False(
                labelPath.IndexOf("MultiplayerPreviewLabelSuffix", StringComparison.Ordinal) >= 0,
                "the workload selector must not append a preview status suffix");
        }

        private static void SelectorSpacingIsMeasuredWithoutLeadingReserve(string selector)
        {
            TestAssert.False(
                selector.IndexOf("LeadingSlotSize", StringComparison.Ordinal) >= 0,
                "selectors must not reserve the removed leading icon slot");
            TestAssert.False(
                selector.IndexOf("LeadingTextGap", StringComparison.Ordinal) >= 0,
                "selectors must not reserve the removed leading icon gap");
            TestAssert.False(
                selector.IndexOf("MinTextWidth", StringComparison.Ordinal) >= 0,
                "selectors must not reserve a second minimum text lane after removing the leading slot");
            TestAssert.Contains(
                selector,
                "float textX = rect.x + SidePadding;",
                "selector labels must begin at the normal side padding");
            TestAssert.Contains(
                selector,
                "(SidePadding * 2f);",
                "selector measurement must include only the two side paddings around text");
        }

        private static void SelectorUsesLegacyMinimumAndMeasuredGrowth(string header)
        {
            TestAssert.Contains(
                header,
                "internal const float PreferredSelectorMainWidth = 150f;",
                "selector main buttons must keep the 1.0.5 minimum width");
            TestAssert.Contains(
                header,
                "BWTBottomBarSelector.MeasureWidth(value),\n                PreferredSelectorMainWidth",
                "selector names must still grow beyond the preferred minimum when measured text needs more room");
            TestAssert.Contains(
                header,
                "PreferredSelectorMainWidth - 0.01f",
                "a selector at the preferred width must retain its name instead of switching to the narrow fallback");
        }

        private static void SelectorLabelsClipWithoutInjectedEllipsis(string selector)
        {
            int drawMainStart = selector.IndexOf(
                "internal static bool DrawMain(",
                StringComparison.Ordinal);
            int drawMenuStart = selector.IndexOf(
                "internal static bool DrawMenu(",
                drawMainStart,
                StringComparison.Ordinal);
            TestAssert.True(
                drawMainStart >= 0 && drawMenuStart > drawMainStart,
                "selector source must keep the main and menu draw methods separate");

            string drawMain = selector.Substring(drawMainStart, drawMenuStart - drawMainStart);
            TestAssert.Contains(
                drawMain,
                "GUI.BeginGroup(",
                "selector names must clip raw text without adding an in-label ellipsis");
            TestAssert.False(
                drawMain.IndexOf("Truncate(", StringComparison.Ordinal) >= 0,
                "selector names must not draw a second ellipsis inside the main button");
        }

        private static void SelectorLabelsAreCentered(string selector)
        {
            TestAssert.Contains(
                selector,
                "Text.Anchor = TextAnchor.MiddleCenter;",
                "workload and ruleset names must be centered in their main buttons");
        }

        private static void WorkloadFooterContextRoutingIsClippedAndSettingsBacked(
            string header,
            string contextRouter,
            string settingsModel,
            string settingIds,
            string settingsTranslations,
            string mainWindow,
            string interactionRouter,
            string footerContextController,
            string settingsRegistry)
        {
            TestAssert.Contains(
                contextRouter,
                "rects.ContainsOptionalFooter(mousePosition)",
                "workload contextual settings must include the visible footer, not only the selector");
            TestAssert.Contains(
                contextRouter,
                "CreateWorkloadContextRequest(",
                "workload footer settings must use the workload ownership-aware context request");

            string[] ids =
            {
                "WorkloadsPreviewRevealAnimation",
                "WorkloadsPreviewRevealSpeed",
                "WorkloadsInspectionHighlights",
                "WorkloadsInspectionOpacity"
            };
            string[] fields =
            {
                "enableWorkloadPreviewRevealAnimation",
                "workloadPreviewRevealSpeed",
                "enableWorkloadInspectionHighlights",
                "workloadInspectionOpacity"
            };
            for (int i = 0; i < ids.Length; i++)
            {
                TestAssert.Contains(settingIds, ids[i], "workload presentation setting ID must be declared");
                TestAssert.Contains(contextRouter, ids[i], "workload presentation setting must be routable by Alt-click");
                TestAssert.Contains(settingsModel, fields[i], "workload presentation setting must have a persisted field/default");
            }

            TestAssert.Contains(
                settingsModel,
                "NormalizeWorkloadPresentationSettings();",
                "workload presentation settings must normalize persisted values on load");
            TestAssert.Contains(
                settingsRegistry,
                ".DefaultTo(DefaultSettings.workloadPreviewRevealSpeed)",
                "workload reveal speed must participate in registered reset behavior");
            TestAssert.Contains(
                settingsRegistry,
                ".ControlsChildren()",
                "inspection highlight settings must own their opacity child");

            TestAssert.Contains(
                contextRouter,
                "StageablePresentationSettingIds",
                "workload presentation settings must remain inside the preview ownership boundary");
            TestAssert.Contains(
                header,
                "rects.ContainsOptionalFooter(evt.mousePosition)",
                "the normal footer input path must reserve Alt-clicks over visible workload controls");
            TestAssert.Contains(
                header,
                "evt.type == EventType.MouseUp &&\n                    (overPopoverAction || _pressedFooterPopoverAction.HasValue)",
                "footer editor cancellation must be handled on the completed click");
            TestAssert.Contains(
                header,
                "_workloadFooterEditCancelRect",
                "editor cancellation must use its visible button rectangle");
            TestAssert.Contains(
                header,
                "evt.Use();",
                "footer cancellation must consume the click before lower Work-tab controls see it");
            int contextualFooter = mainWindow.IndexOf(
                "_workGridInteractionRouter.TryHandleFooterContextSettings(\n                    view.WindowRect,",
                StringComparison.Ordinal);
            int normalFooter = mainWindow.IndexOf(
                "HeaderButtons.TryHandleOptionalFooterInput(\n                view.WindowRect,",
                StringComparison.Ordinal);
            TestAssert.True(
                contextualFooter >= 0 && normalFooter > contextualFooter,
                "the window must give contextual settings first refusal before normal footer actions");
            TestAssert.Contains(
                interactionRouter,
                "_contextSettingsInteractionController.TryHandleFooterInput(inRect, evt)",
                "footer contextual routing must reuse the existing context-settings controller");
            TestAssert.Contains(
                footerContextController,
                "rects.ContainsOptionalFooter(evt.mousePosition)",
                "footer registration must use the authoritative visible footer hit helper");
            TestAssert.Contains(
                footerContextController,
                "rects.HasOptionalSaveAs ? rects.OptionalSaveAs : Rect.zero",
                "Save As contextual routing must use its clipped visible rectangle");
            TestAssert.Contains(
                footerContextController,
                "rects.HasOptionalUpdate ? rects.OptionalUpdate : Rect.zero",
                "Save contextual routing must use its clipped visible rectangle");
            TestAssert.Contains(
                footerContextController,
                "rects.OptionalCancel",
                "Cancel contextual routing must use the shared footer geometry");
            TestAssert.Contains(
                footerContextController,
                "rects.OptionalApply",
                "Apply contextual routing must use the shared footer geometry");
            int tutorialDraw = mainWindow.IndexOf(
                "BWTWorkTabTutorial.TickAndDraw(",
                StringComparison.Ordinal);
            int workloadPopoverDraw = mainWindow.IndexOf(
                "HeaderButtons.DrawOptionalFooterPopoverOnTop(",
                StringComparison.Ordinal);
            TestAssert.True(
                tutorialDraw >= 0 && workloadPopoverDraw > tutorialDraw,
                "the workload editor must be painted after the tutorial overlay so its Save button owns the first click");
            TestAssert.False(
                header.IndexOf("animated: animated", StringComparison.Ordinal) >= 0,
                "preview lifecycle must not leave an animated footer state behind the active presentation");
            TestAssert.Contains(
                settingsTranslations,
                "BWT_Settings_workloads.previewRevealAnimation",
                "workload presentation settings must have player-facing translations");
            TestAssert.Contains(
                settingsTranslations,
                "BWT_Settings_workloads.inspectionOpacity",
                "workload inspection opacity must have a player-facing translation");
        }

        private static void NarrowFooterGeometryIsBounded(string header)
        {
            TestAssert.Contains(
                header,
                "private void LayoutNormal(",
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
                header.IndexOf("float leftEdge = rects.HasWorkload", StringComparison.Ordinal) >= 0,
                "footer hint reservation must not follow clipped preview controls during the reveal");
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
                "modern => modern.Create(label)",
                "modern workload creation must delegate capture and persistence to the workload backend");
            TestAssert.False(
                gateway.IndexOf("CreateModernWorkload(", StringComparison.Ordinal) >= 0 ||
                gateway.IndexOf("CaptureCurrentV2Template(", StringComparison.Ordinal) >= 0,
                "the gateway must not retain a forwarding create or capture wrapper");
            TestAssert.Contains(
                gateway,
                "WorkloadLiveCapture.CapturePawn(",
                "include must use the workload-owned live capture path");
            TestAssert.Contains(
                backend,
                "internal static bool CapturePawn(",
                "the shared capture owner must retain an explicit pawn-baseline entry point");
            TestAssert.False(
                gateway.IndexOf("CompleteCapturedV2Template(", StringComparison.Ordinal) >= 0 ||
                backend.IndexOf("CapturePawnBaseline(", StringComparison.Ordinal) >= 0,
                "gateway and preview capture must not retain duplicate capture wrappers");
            TestAssert.Contains(
                backend,
                "TryCaptureSchedule(",
                "include must retain captured 24-hour schedule state through the shared capture path");
            TestAssert.Contains(
                backend,
                "bool liveManualMode = ownsManualMode &&",
                "the shared capture path must read one coherent manual-mode baseline per pawn");
            TestAssert.Contains(
                backend,
                "draft.SetManualMode(parentKey, liveManualMode)",
                "the shared capture path must retain manual-mode baselines");
            TestAssert.Contains(
                backend,
                "WorkloadScalarValue.FromInteger(inheritedPriority)",
                "include must capture the already-derived effective specific-job value before planning a commit");
            TestAssert.Contains(
                backend,
                "draft.SetSpecificJobOrder(specificKey, workGiverIndex)",
                "include must retain the display-order specific-job baseline");
            int pawnCapture = backend.IndexOf(
                "for (int pawnIndex = 0; pawns != null && pawnIndex < pawns.Count; pawnIndex++)",
                StringComparison.Ordinal);
            int globalCapture = backend.IndexOf(
                "CaptureGlobalState(draft, ownership, ref capturedSchedule);",
                StringComparison.Ordinal);
            TestAssert.True(
                pawnCapture >= 0 && globalCapture > pawnCapture,
                "global specific-job state must be captured after, not inside, pawn membership traversal");
            TestAssert.Contains(
                backend,
                "}\n\n                CaptureGlobalState(draft, ownership, ref capturedSchedule);",
                "the completed capture must leave the pawn loop before reading global state");
            TestAssert.Contains(
                backend,
                "ownership = WorkloadLiveCapturePolicy.CompleteOwnership(\n                    ownership,\n                    capturedSchedule);",
                "a completed capture must claim schedule ownership only after observing a real schedule");
            TestAssert.Contains(
                gateway,
                "WorkloadGateway.ExtendV2PreviewBaseline(candidate, pawnKey)",
                "include must hand the candidate to the bound backend instead of constructing a temporary service");
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
                "\"BWT_Workload_Save\".Translate(),",
                "the visible Update action must be labeled Save");
            TestAssert.Contains(
                header,
                "\"BWT_Workload_SaveTooltip\".Translate()",
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

        private static string MethodBody(string source, string signature)
        {
            int signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(signatureStart >= 0, "expected production method is missing: " + signature);
            if (signatureStart < 0)
            {
                return string.Empty;
            }

            int bodyStart = source.IndexOf('{', signatureStart + signature.Length);
            TestAssert.True(bodyStart >= 0, "expected production method body is missing: " + signature);
            if (bodyStart < 0)
            {
                return string.Empty;
            }

            int depth = 0;
            for (int index = bodyStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}' && --depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }

            TestAssert.True(false, "expected production method body is balanced: " + signature);
            return string.Empty;
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while (true)
            {
                index = source.IndexOf(value, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    return count;
                }

                count++;
                index += value.Length;
            }
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
