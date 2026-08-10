using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Dividers;
using Better_Work_Tab.Features.Migration;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Input;
using Better_Work_Tab.UI.Columns;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.RuleBuilderV2;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Interaction;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WindowSession;
using RimWorld;
using Spine.Profiling;
using Spine.RimWorld.Rendering;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Main work tab window that coordinates PawnOrganizer layout with vanilla rendering.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MainTabWindow_BetterWork : MainTabWindow_Work
    {
        private readonly WorkGridRendererFacade _workGridRenderer;
        private readonly WorkGridInteractionRouter _workGridInteractionRouter;
        private readonly WorkGridSnapshotProvider _workGridSnapshots = new WorkGridSnapshotProvider();
        private readonly WorkTabViewportController _viewportController = new WorkTabViewportController();
        private readonly WorkTabBodyRenderer _bodyRenderer;
        private readonly WorkTabTutorialInteractionController _tutorialInteractionController;
        private readonly WorkTabPriorityInputHandler _priorityInputHandler;
        private readonly RuleBuilder2WorkTabInteractionController _ruleBuilder2InteractionController;
        private readonly WorkTabContextSettingsInteractionController _contextSettingsInteractionController;
        private readonly SubWorkInteractionController _subWorkInteractionController;
        private readonly WorkGridContextActionController _contextActionController;
        private readonly SubWorkStyleChooserPresenter _subWorkStyleChooserPresenter;
        private readonly WorkTabChrome _workTabChrome;
        private readonly WorkTabWindowSessionState _windowSession;
        private readonly WorkTabWindowSizingController _windowSizingController;

        public MainTabWindow_BetterWork()
        {
            _windowSession = new WorkTabWindowSessionState(this);
            _windowSizingController = new WorkTabWindowSizingController(
                () => _windowSession.GetPawnTable(Current.Game),
                () => PawnOrganizerSystem.Instance,
                () => ExtraTopSpace,
                () => ExtraBottomSpace,
                () => Margin,
                () => windowRect,
                () => Verse.UI.screenWidth,
                () => Verse.UI.screenHeight,
                () => FluffyTimeScheduleAssigner.IsOpen,
                () => TimePriorityScheduleEditor.LayoutSignature,
                () => SubWorkDrilldownState.MeasurementSignature,
                () => BetterWorkTabMod.Settings?.workTabMaxVisiblePawns ??
                    DefaultSettings.workTabMaxVisiblePawns,
                () => BetterWorkTabMod.Settings?.keepVanillaWorkTabMinimumWidth ??
                    DefaultSettings.keepVanillaWorkTabMinimumWidth);
            _bodyRenderer = new WorkTabBodyRenderer(_viewportController);
            WorkGridDrawingSurface drawingSurface = new WorkGridDrawingSurface(
                _viewportController,
                _bodyRenderer,
                new WorkTabHeaderRenderer(),
                () => windowRect,
                () => ExtraBottomSpace);
            _workGridRenderer = new WorkGridRendererFacade(
                drawingSurface,
                () => BetterWorkTabMod.Settings?.workGridRendererMode ?? DefaultSettings.workGridRendererMode);
            _workGridRenderer.Register(new OptimizedWorkGridRenderer(drawingSurface));
            _tutorialInteractionController = new WorkTabTutorialInteractionController(_bodyRenderer);
            _priorityInputHandler = new WorkTabPriorityInputHandler(_bodyRenderer);
            _ruleBuilder2InteractionController = new RuleBuilder2WorkTabInteractionController(_bodyRenderer);
            _contextSettingsInteractionController = new WorkTabContextSettingsInteractionController();
            _subWorkInteractionController = new SubWorkInteractionController(
                _bodyRenderer,
                _priorityInputHandler);
            _workTabChrome = new WorkTabChrome(_subWorkInteractionController);
            _contextActionController = new WorkGridContextActionController(
                _bodyRenderer,
                () => SetDirty(),
                () => _windowSizingController.StageBottomAnchoredResizeIfRequestedSizeChanged());
            _subWorkStyleChooserPresenter = new SubWorkStyleChooserPresenter(_subWorkInteractionController);
            _workGridInteractionRouter = new WorkGridInteractionRouter(
                _tutorialInteractionController,
                _priorityInputHandler,
                _ruleBuilder2InteractionController,
                _contextSettingsInteractionController,
                _subWorkInteractionController,
                _contextActionController);
        }

        // Vanilla's gap above the header lane, and nothing else.
        //
        // The tutorial band used to be added here as well, on the reasoning that
        // the window must grow to hold it. It already does: the band is a pinned
        // row, so the drawing surface's pinned-row metric reports it and it is inside the layout
        // height every window measurement is built from. Adding it a second time
        // counted the height twice and, because this value is also the table's
        // vertical origin, pushed the headers a whole band's height down the tab
        // whenever the tutorial was on.
        protected override float ExtraTopSpace =>
            Mathf.Clamp(
                BetterWorkTabMod.Settings?.workTabTopSpace ?? DefaultSettings.workTabTopSpace,
                0f,
                80f);

        protected override float ExtraBottomSpace =>
            base.ExtraBottomSpace + FluffyTimeScheduleAssigner.ReservedBottomSpace;

        private WorkTabSnapshot _organizerSnapshot;
        private IReadOnlyList<Pawn> _organizerSnapshotPawns;
        private IReadOnlyList<PawnDivider> _organizerSnapshotDividers;

        internal bool LastRawHorizontalOverflow => _viewportController.LastRawHorizontalOverflow;
        internal bool LastHorizontalScrollbarVisible => _viewportController.LastHorizontalScrollbarVisible;

        public override void PreOpen()
        {
            base.PreOpen();
            var settings = BetterWorkTabMod.Settings;
            bool keepOpen = settings?.disableLeftClickClose ?? false;
            bool allowMapClose = settings?.closeOnMapClick ?? true;
            closeOnClickedOutside = !keepOpen && allowMapClose;
            UpdateTutorialAcceptKeyState();

            if (PawnOrganizerSystem.Instance == null)
            {
                var widthStore = new ColumnWidthPersistence();
                new PawnOrganizerSystem(widthStore);
            }

            // Sync the dragged columns list on open in case settings were loaded from disk
            WorkColumnCustomizationService.SyncDraggedColumnsWithCurrentOrder();

            // Auto-enable manual priorities if setting is enabled
            if (settings?.autoEnableManualPriorities ?? false)
            {
                WorkPrioritySystem.SetManualPriorities(true);
            }

            // Sub-work is the one thing here a player cannot find by looking:
            // the header gives no sign that a modifier-click opens it. Offered
            // as good-to-know, so RimWorld holds it back while its own readout
            // is busy with something more urgent.
            Features.Tutorial.BWTConcepts.TeachSpecificJobs();
        }

        /// <summary>
        /// Draws the work tab contents and routes input while honoring drag state.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Patch_WorkPriority_DoCell_Unified.TrimCacheIfNeeded();
            }

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DoWindowContents", () => DoWindowContentsProfiled(inRect));
                return;
            }

            DoWindowContentsProfiled(inRect);
        }

        public override void OnAcceptKeyPressed()
        {
            if (_tutorialInteractionController.TryHandleAcceptKey())
            {
                return;
            }

            base.OnAcceptKeyPressed();
        }

        private void DoWindowContentsProfiled(Rect inRect)
        {
            PawnTable table = _windowSession.GetPawnTable(Current.Game);
            if (table == null)
            {
                return;
            }

            using (SleekWorkTabHostedFrame.Enter(this, inRect, table))
            {
                DoWindowContentsProfiledCore(inRect, table);
                SleekWorkTabHostedFrame.SyncFocusCompanion(this);
            }
        }

        private void DoWindowContentsProfiledCore(Rect inRect, PawnTable table)
        {
            UpdateTutorialAcceptKeyState();
            // The tutorial band contributes reserved height, so latch its
            // presence before any geometry below derives a table origin from it.
            BWTWorkTabTutorial.RefreshStripReservation();
            _workGridRenderer.PrepareFrame(WorkTabInvalidationHub.Current);

            bool dividerAnimationChanged = DividerCollapseAnimationState.Tick();
            dividerAnimationChanged |= DividerInsertionAnimationState.Tick();
            if (dividerAnimationChanged)
            {
                PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
                WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Animation);
            }

            var organizer = PawnOrganizerSystem.Instance;
            float effectiveHeaderHeight = SubWorkDrilldownHeaderGeometry.GetEffectiveHeaderHeight(table);
            float previousContentHeight = organizer?.Layout != null
                ? WorkGridLayoutMetrics.GetHeaderAnchoredContentHeight(organizer.Layout)
                : Mathf.Max(0f, table.Size.y - effectiveHeaderHeight);
            float tableOriginY = WorkGridViewportOriginMath.ResolveTableOriginY(
                inRect.yMin,
                inRect.yMax,
                ExtraTopSpace,
                ExtraBottomSpace,
                WorkGridLayoutMetrics.ScrollViewFitAllowance,
                effectiveHeaderHeight,
                WorkGridLayoutMetrics.GetHeaderAnchoredPinnedRowsHeight(),
                previousContentHeight);
            Vector2 tableOrigin = new Vector2(inRect.x, tableOriginY);
            WorkGridInvalidationAudit.PollRoster(table);
            var snapshot = BuildSnapshotForOrganizer(table);

            SubWorkDrilldownState.TickTransition();
            RefreshSubWorkLayoutIfNeeded(organizer);

            // Avoid rebuilding the layout mid-drag so the handler keeps valid positioning data.
            if (organizer != null && !organizer.IsDragging)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Layout.Update", () => organizer.Update(table, tableOrigin, snapshot));
                }
                else
                {
                    organizer.Update(table, tableOrigin, snapshot);
                }

                float anchoredOriginY = WorkGridViewportOriginMath.ResolveTableOriginY(
                    inRect.yMin,
                    inRect.yMax,
                    ExtraTopSpace,
                    ExtraBottomSpace,
                    WorkGridLayoutMetrics.ScrollViewFitAllowance,
                    organizer.Layout.HeaderHeight,
                    WorkGridLayoutMetrics.GetHeaderAnchoredPinnedRowsHeight(),
                    WorkGridLayoutMetrics.GetHeaderAnchoredContentHeight(organizer.Layout));
                if (Mathf.Abs(anchoredOriginY - tableOrigin.y) > 0.01f)
                {
                    tableOrigin.y = anchoredOriginY;
                    organizer.Update(table, tableOrigin, snapshot);
                }
            }

            if (Event.current.type == EventType.Repaint)
            {
                _windowSession.CaptureWarmOpenTableState(
                    table,
                    Current.Game,
                    Find.CurrentMap,
                    Verse.UI.screenWidth,
                    Verse.UI.screenHeight,
                    PawnOrganizerSystem.Instance?.Layout?.LayoutRevision ?? -1);
            }

            _windowSizingController.StageBottomAnchoredResizeIfRequestedSizeChanged();
            Event evt = Event.current;
            BWTWorkTabTutorial.UpdatePointerOwnership(inRect, organizer?.Layout, evt.mousePosition);
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                if (evt.type == EventType.MouseDown || evt.type == EventType.ScrollWheel)
                {
                    WorkTabDiagnostics.RecordPriorityInput("work-tab input entry", evt);
                }

                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time(
                        "WorkTab.Input",
                        () => _workGridInteractionRouter.Route(inRect, organizer, evt));
                }
                else
                {
                    _workGridInteractionRouter.Route(inRect, organizer, evt);
                }

                _subWorkInteractionController.SuppressPriorityMouseDownIfNeeded(evt);
                RefreshSubWorkLayoutIfNeeded(organizer);
                _windowSizingController.StageBottomAnchoredResizeIfRequestedSizeChanged();
            }

            if (evt.type != EventType.Layout)
            {
                _ruleBuilder2InteractionController.UpdateHover(organizer?.Layout, inRect);
            }

            WorkGridFeatureFlags renderFeatures = WorkGridFeatureFlags.None;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings?.enableSkillOverlayFeature ?? DefaultSettings.enableSkillOverlayFeature)
                renderFeatures |= WorkGridFeatureFlags.SkillOverlay;
            if (settings?.enableDividers ?? DefaultSettings.enableDividers)
                renderFeatures |= WorkGridFeatureFlags.Dividers;
            if (settings?.enableSubWorkDrilldown ?? DefaultSettings.enableSubWorkDrilldown)
                renderFeatures |= WorkGridFeatureFlags.SubWork;

            WorkGridSnapshot presentationSnapshot = null;
            try
            {
                WorkGridInvalidationAudit.Poll(table);
                presentationSnapshot = _workGridSnapshots.Prepare(
                    organizer?.Layout,
                    table,
                    WorkTabInvalidationHub.Current);
            }
            catch (Exception exception)
            {
                _workGridSnapshots.Clear();
                Log.ErrorOnce(
                    "[BWT] Work-grid snapshot construction failed; vanilla rendering remains active.\n" + exception,
                    0x42575453);
            }

            var renderContext = new WorkGridRenderContext(
                ImGuiEventPhases.Classify(evt.type),
                evt.type,
                organizer?.Layout,
                new WorkGridPresentationAccess(table, presentationSnapshot, organizer?.Layout?.GeometrySnapshot),
                inRect,
                inRect,
                Time.frameCount,
                WorkTabInvalidationHub.Current,
                new WorkGridRenderConfiguration(renderFeatures, WorkGridLayerFlags.All),
                WorkGridSelectionScope.Window);
            _workGridRenderer.Render(in renderContext);

            // Begin/EndScrollView must participate in Layout so Unity keeps the same
            // scroll control state across Layout, input, and Repaint. The drawing surface's
            // Layout path creates that control without painting rows or headers.
            if (evt.type == EventType.Layout)
            {
                return;
            }

            TimePriorityScheduleEditor.Draw(organizer?.Layout);
            FluffyTimeScheduleAssigner.Draw(inRect, organizer?.Layout, base.ExtraBottomSpace);
            _subWorkStyleChooserPresenter.Draw(organizer?.Layout, windowRect, inRect);

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawDragOverlays", () => organizer?.DrawDragOverlays());
            }
            else
            {
                organizer?.DrawDragOverlays();
            }

            _workTabChrome.DrawTopControls(organizer?.Layout, inRect);

            _workTabChrome.DrawBottomControls(organizer?.Layout, inRect);
            _workTabChrome.DrawSubWorkExitButton(inRect);
            _workTabChrome.DrawBottomCounters(inRect, table);
            BWTWorkTabTutorial.TickAndDraw(inRect, organizer?.Layout);
            if (BWTWorkTabTutorial.OwnsCurrentPointer && evt.type == EventType.Repaint)
            {
                Vector2 pointer = evt.mousePosition;
                TooltipHandler.ClearTooltipsFrom(new Rect(pointer.x - 1f, pointer.y - 1f, 2f, 2f));
            }
            NativeCursorPosition.ProcessPendingMove();
            NativeCursorPosition.DrawPendingMoveCue();
            WorkTabDiagnostics.RecordWorkTabRepaint();
        }

        private void RefreshSubWorkLayoutIfNeeded(PawnOrganizerSystem organizer)
        {
            if (!SubWorkDrilldownState.ConsumeLayoutRefresh())
            {
                return;
            }

            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.Rows |
                WorkTabDirtyFlags.Columns |
                WorkTabDirtyFlags.HeaderGeometry |
                WorkTabDirtyFlags.Viewport |
                WorkTabDirtyFlags.WindowSize);
            _workGridRenderer.PrepareFrame(WorkTabInvalidationHub.Current);
            organizer?.Layout?.InvalidateRowDescriptors();
            _windowSizingController.InvalidateRequestedTabSizeCache();
            SetDirty();
        }

        private void UpdateTutorialAcceptKeyState()
        {
            // Keep RimWorld's accept-key dispatch enabled. The override above
            // consumes Enter while a Work tab tutorial is active and otherwise
            // falls back to the vanilla main-tab close behavior.
            closeOnAccept = true;
        }

        private IPawnOrganizerSnapshot BuildSnapshotForOrganizer(PawnTable table)
        {
            IReadOnlyList<Pawn> pawns = table.PawnsListForReading;
            pawns = SleekWorkTabGateway.ApplyMixedSearch(pawns);
            var comp = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            var settings = BetterWorkTabMod.Settings;
            bool useDividers = (settings?.enableDividers ?? true) && (settings?.showDividers ?? true);
            IReadOnlyList<PawnDivider> dividers = useDividers
                ? comp?.ActiveDividers ?? (IReadOnlyList<PawnDivider>)Array.Empty<PawnDivider>()
                : Array.Empty<PawnDivider>();

            if (_organizerSnapshot == null ||
                !ReferenceEquals(_organizerSnapshotPawns, pawns) ||
                !ReferenceEquals(_organizerSnapshotDividers, dividers))
            {
                _organizerSnapshotPawns = pawns;
                _organizerSnapshotDividers = dividers;
                _organizerSnapshot = new WorkTabSnapshot(pawns, dividers);
            }

            return _organizerSnapshot;
        }

        /// <summary>
        /// Computes the work tab size from the layout published by this window.
        /// </summary>
        public override Vector2 RequestedTabSize => _windowSizingController.RequestedTabSize;

        public override void Notify_ResolutionChanged()
        {
            // MainTabWindow_PawnTable replaces its private table before calling
            // its base notification, and that base immediately measures this
            // window through RequestedTabSize. Invalidate before entering base
            // so that measurement reflects the replacement table during the
            // preserved vanilla lifecycle ordering.
            _windowSession.InvalidatePawnTableCache();
            _windowSizingController.InvalidateRequestedTabSizeCache();
            base.Notify_ResolutionChanged();
        }

        public override void PostOpen()
        {
            PawnTable table = _windowSession.GetPawnTable(Current.Game);
            _windowSession.BeginWarmOpenIfEligible(
                table,
                Current.Game,
                Find.CurrentMap,
                Verse.UI.screenWidth,
                Verse.UI.screenHeight,
                PawnOrganizerSystem.Instance?.Layout?.LayoutRevision ?? -1);

            try
            {
                base.PostOpen();
            }
            finally
            {
                _windowSession.EndWarmOpen();
            }

            _windowSession.CaptureWarmOpenTableState(
                _windowSession.GetPawnTable(Current.Game),
                Current.Game,
                Find.CurrentMap,
                Verse.UI.screenWidth,
                Verse.UI.screenHeight,
                PawnOrganizerSystem.Instance?.Layout?.LayoutRevision ?? -1);
            WorkTabActivityState.NotifyOpen(true);
            BWT20UpgradePrompt.ShowIfNeeded(
                BetterWorkTabMod.Settings,
                Current.Game?.GetComponent<GameComponent_BWTWorldSettings>());
        }

        public override void WindowOnGUI()
        {
            // GUI.Window establishes clipping and event coordinates before it invokes
            // DoWindowContents. Resizing from inside that callback splits one IMGUI
            // event across two rectangles, leaving stale black bands and invalidating
            // otherwise-correct priority-cell hit coordinates.
            if (_windowSizingController.TryConsumePendingWindowRect(out Rect pendingWindowRect))
            {
                windowRect = pendingWindowRect;
            }

            base.WindowOnGUI();
        }

        public override void OnCancelKeyPressed()
        {
            if (_subWorkInteractionController.TryExitSubWorkMode(restoreMousePosition: false))
            {
                Event.current?.Use();
                return;
            }

            base.OnCancelKeyPressed();
        }

        public override void PreClose()
        {
            base.PreClose();
            HighlightManager.ClearHighlight();
            WorkTabActivityState.NotifyOpen(false);

            // === PRESENCE FEATURE DISABLED ===
            /*
            if (MultiplayerBridge.Active)
            {
                Mod_Support.Multiplayer.Features.Presence.WorkTabPresenceRegistry.ClearPresence();
                Log.Message($"[BWT-MP] Work tab closed");
            }
            */
        }

        public override void PostClose()
        {
            base.PostClose();
            BWTWorkTabTutorial.NotifyWorkTabClosed();
            BWTBetaFeedbackButton.NotifyTabClosed();
            // Clear float menu highlights when Work tab is closed
            HighlightState.ClearWorktypeHighlight();
            MouseStateManager.ClearHover();
            ResetTransientWindowState();
            _windowSession.CaptureWarmOpenTableState(
                _windowSession.GetPawnTable(Current.Game),
                Current.Game,
                Find.CurrentMap,
                Verse.UI.screenWidth,
                Verse.UI.screenHeight,
                PawnOrganizerSystem.Instance?.Layout?.LayoutRevision ?? -1);
        }

        private void ResetTransientWindowState()
        {
            // Cancel active input before clearing the state it may reference.
            PawnOrganizerSystem.Instance?.CancelActiveDrag();
            _workGridInteractionRouter.ResetSessions();
            _subWorkInteractionController.ResetForWindowClose();
            _subWorkStyleChooserPresenter.ResetForWindowClose();
            SubWorkDrilldownState.ResetForWindowClose();
            TimePriorityScheduleEditor.ResetForWindowClose();
            FluffyTimeScheduleAssigner.ResetForWindowClose();

            _viewportController.ResetForWindowClose();
            _windowSizingController.ResetForWindowClose();
            AngledHeaderInteraction.ResetForWindowClose();
            HeaderInputController.ResetForWindowClose();
            ColumnSelectionManager.Clear();
            ColumnReorderAnimationState.Clear();
            DividerCollapseAnimationState.ResetForWindowClose();
            DividerInsertionAnimationState.ResetForWindowClose();
            SubWorkCrossWorkDropTargetRenderer.ResetForWindowClose();
            WorkGiverPriorityBoxRenderer.ResetForWindowClose();
            NativeCursorPosition.CancelPendingMove();
            // Work-grid snapshots and audit state belong to the game session, not this window.
            // GameCacheResetUtility owns their load/new-game teardown boundary.
        }
    }
}
