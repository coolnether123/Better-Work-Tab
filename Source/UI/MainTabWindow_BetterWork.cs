using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.Dividers;
using Better_Work_Tab.Features.Migration;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Input;
using Better_Work_Tab.UI.Columns;
using Better_Work_Tab.UI.Chrome;
using Better_Work_Tab.UI.RuleBuilderV2;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Interaction;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Workloads;
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
        private readonly WorkloadPreviewController _workloadPreviewController;
        private WorkTabApplication _application;

        public MainTabWindow_BetterWork()
        {
            _workloadPreviewController = new WorkloadPreviewController();
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
                () => BWTWorkTabEffectiveSettings.GetInt(SettingIDs.LayoutWorkTabMaxVisiblePawns),
                () => BWTWorkTabEffectiveSettings.GetBool(SettingIDs.LayoutWorkTabMinimumWidth));
            _bodyRenderer = new WorkTabBodyRenderer(_viewportController);
            WorkGridDrawingSurface drawingSurface = new WorkGridDrawingSurface(
                _viewportController,
                _bodyRenderer,
                new WorkTabHeaderRenderer(() => _application));
            _workGridRenderer = new WorkGridRendererFacade(
                drawingSurface,
                () => BetterWorkTabMod.Settings?.workGridRendererMode ?? DefaultSettings.workGridRendererMode);
            _workGridRenderer.Register(new OptimizedWorkGridRenderer(drawingSurface));
            _tutorialInteractionController = new WorkTabTutorialInteractionController(_bodyRenderer);
            _priorityInputHandler = new WorkTabPriorityInputHandler(
                _bodyRenderer,
                () => _application);
            _ruleBuilder2InteractionController = new RuleBuilder2WorkTabInteractionController(_bodyRenderer);
            _contextSettingsInteractionController = new WorkTabContextSettingsInteractionController();
            _subWorkInteractionController = new SubWorkInteractionController(
                _bodyRenderer,
                _priorityInputHandler);
            _workTabChrome = new WorkTabChrome(
                _subWorkInteractionController,
                () => _application);
            _contextActionController = new WorkGridContextActionController(
                _bodyRenderer,
                () => SetDirty(),
                () => _windowSizingController.StageBottomAnchoredResizeIfRequestedSizeChanged(),
                () => WorkTabGameRoots.For(Current.Game)?.State?.Dividers != null);
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
            _application = WorkTabGameRoots.For(Current.Game)?.Application;
            BWTWorkloadSettingsOwnershipPolicy.BindApplication(_application);
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
                _application?.SetManualPriorityMode(true);
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
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.WorkloadPreview.PrepareFrame",
                    () => _workloadPreviewController.PrepareFrame());
            }
            else
            {
                _workloadPreviewController.PrepareFrame();
            }
            IDisposable effectiveStateScope =
                _workloadPreviewController.PushEffectiveStateScope();
            try
            {
                DoWindowContentsProfiledCoreScoped(inRect, table);
            }
            finally
            {
                // The preview provider is valid only for this complete Work-tab
                // pass. Pop it even when a renderer or input owner throws.
                effectiveStateScope.Dispose();
                try
                {
                    if (SpineTiming.Enabled)
                    {
                        SpineTiming.Time(
                            "WorkTab.WorkloadPreview.SynchronizeAfterInput",
                            () => _workloadPreviewController.SynchronizeAfterInput());
                    }
                    else
                    {
                        _workloadPreviewController.SynchronizeAfterInput();
                    }
                }
                finally
                {
                    // Lifecycle actions are deliberately last: they must not
                    // observe the scoped preview provider as still installed.
                    _workloadPreviewController.FlushQueuedLifecycleActions();
                }
            }
        }

        private void DoWindowContentsProfiledCoreScoped(Rect inRect, PawnTable table)
        {
            AdvanceFrameState();
            PawnOrganizerSystem organizer = PrepareAndUpdateLayout(inRect, table, out Rect workGridRect);

            _windowSizingController.StageBottomAnchoredResizeIfRequestedSizeChanged();
            Event evt = Event.current;
            WorkTabView view = BuildWorkTabView(inRect, table, organizer, workGridRect, evt);
            using (WorkTabEffectiveStateScope.PushCapturedView(
                       view.EffectiveState,
                       view.Preview))
            {
                RouteFrameInput(in view, organizer, evt);
                UpdateFrameHover(in view, organizer, evt);
                RenderWorkGrid(in view);

                // Begin/EndScrollView must participate in Layout so Unity keeps the same
                // scroll control state across Layout, input, and Repaint. The drawing surface's
                // Layout path creates that control without painting rows or headers.
                if (evt.type != EventType.Layout)
                {
                    DrawFrameOverlaysAndChrome(in view, organizer, evt);
                }
            }
        }

        // These single-caller methods make the immediate-mode frame order explicit.
        // They stay on the window because the sequence is its lifecycle contract.
        private void AdvanceFrameState()
        {
            UpdateTutorialAcceptKeyState();
            // The tutorial band contributes reserved height, so latch its
            // presence before any geometry below derives a table origin from it.
            BWTWorkTabTutorial.RefreshStripReservation();

            bool dividerAnimationChanged = DividerCollapseAnimationState.Tick();
            dividerAnimationChanged |= DividerInsertionAnimationState.Tick();
            if (dividerAnimationChanged)
            {
                PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
                WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Animation);
            }

            // Reorder animation is a shared header/body coordinate concern. Keep
            // its lifecycle driven by the window rather than by whichever render
            // layer happens to query a column first. This is required by the
            // optimized renderer because its visible-column culling can otherwise
            // prevent the animation state from being advanced consistently.
            if (ColumnReorderAnimationState.Tick())
            {
                WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Animation);
            }

            // Prepare after transient animation state has published its
            // invalidation so the active renderer and header solver observe the
            // same frame's lifecycle version.
            _workGridRenderer.PrepareFrame(WorkTabInvalidationHub.Current);
        }

        private PawnOrganizerSystem PrepareAndUpdateLayout(
            Rect inRect,
            PawnTable table,
            out Rect workGridRect)
        {
            PawnOrganizerSystem organizer = PawnOrganizerSystem.Instance;
            workGridRect = inRect;
            float effectiveHeaderHeight = SubWorkDrilldownHeaderGeometry.GetEffectiveHeaderHeight(table);
            float previousContentHeight = organizer?.Layout != null
                ? WorkGridLayoutMetrics.GetHeaderAnchoredContentHeight(organizer.Layout)
                : Mathf.Max(0f, table.Size.y - effectiveHeaderHeight);
            float tableOriginY = WorkGridViewportOriginMath.ResolveTableOriginY(
                workGridRect.yMin,
                workGridRect.yMax,
                ExtraTopSpace,
                ExtraBottomSpace,
                WorkGridLayoutMetrics.ScrollViewFitAllowance,
                effectiveHeaderHeight,
                WorkGridLayoutMetrics.GetHeaderAnchoredPinnedRowsHeight(),
                previousContentHeight);
            Vector2 tableOrigin = new Vector2(workGridRect.x, tableOriginY);
            WorkGridInvalidationAudit.PollRoster(table);
            IPawnOrganizerSnapshot snapshot = BuildSnapshotForOrganizer(table);

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
                    workGridRect.yMin,
                    workGridRect.yMax,
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

            return organizer;
        }

        private void RouteFrameInput(
            in WorkTabView view,
            PawnOrganizerSystem organizer,
            Event evt)
        {
            BWTWorkTabTutorial.UpdatePointerOwnership(view.WindowRect, view.Layout, evt.mousePosition);
            if (evt.type == EventType.Repaint)
            {
                // Contextual settings binds its hit regions during Repaint. Keep
                // this dispatch ahead of gameplay input so the following Alt-click
                // can resolve the binding that was registered for this frame.
                _workGridInteractionRouter.Route(in view, organizer, evt);
                _workGridInteractionRouter.TryHandleFooterContextSettings(
                    view.WindowRect,
                    evt);
                return;
            }

            if (evt.type == EventType.Layout)
            {
                return;
            }

            Rect gearRect = WorkTabChromeGeometry.GetInfoIconRect(view.WindowRect);
            if (evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                evt.alt &&
                !_workloadPreviewController.IsUnsafePreviewInputBlocked &&
                HeaderButtons.GetBottomButtonRects(view.WindowRect, gearRect)
                    .ContainsWorkloadFooter(evt.mousePosition) &&
                _workGridInteractionRouter.TryHandleFooterContextSettings(
                    view.WindowRect,
                    evt))
            {
                return;
            }

            bool routedWorkloadFooterInput = HeaderButtons.TryHandleWorkloadFooterInput(
                view.WindowRect,
                gearRect,
                evt);
            bool routedPreviewScroll = !routedWorkloadFooterInput &&
                TryRouteWorkloadPreviewScroll(
                     evt,
                     view.Table,
                     view.WindowRect,
                    gearRect);
            if (!routedWorkloadFooterInput &&
                !routedPreviewScroll &&
                _workloadPreviewController.IsUnsafePreviewInputBlocked)
            {
                // A synchronized Apply/Update/Fork owns the detached preview
                // until its terminal acknowledgement. Keep repaint/scroll
                // behavior alive, but do not let grid, schedule, context, or
                // settings input mutate the draft mid-transaction.
                return;
            }

            if (!routedWorkloadFooterInput &&
                !routedPreviewScroll &&
                SpineTiming.Enabled)
            {
                WorkTabView routedView = view;
                SpineTiming.Time(
                    "WorkTab.Input",
                    () => _workGridInteractionRouter.Route(in routedView, organizer, evt));
            }
            else if (!routedWorkloadFooterInput &&
                     !routedPreviewScroll)
            {
                _workGridInteractionRouter.Route(in view, organizer, evt);
            }

            _subWorkInteractionController.SuppressPriorityMouseDownIfNeeded(evt);
            _workloadPreviewController.SynchronizeAfterInput();
            _windowSizingController.StageBottomAnchoredResizeIfRequestedSizeChanged();
        }

        private void UpdateFrameHover(in WorkTabView view, PawnOrganizerSystem organizer, Event evt)
        {
            if (evt.type != EventType.Layout)
            {
                _ruleBuilder2InteractionController.UpdateHover(in view);
            }
        }

        private WorkTabView BuildWorkTabView(
            Rect windowRect,
            PawnTable table,
            PawnOrganizerSystem organizer,
            Rect workGridRect,
            Event evt)
        {
            WorkGridFeatureFlags renderFeatures = WorkGridFeatureFlags.None;
            if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesOverlay))
                renderFeatures |= WorkGridFeatureFlags.SkillOverlay;
            if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesDividers))
                renderFeatures |= WorkGridFeatureFlags.Dividers;
            if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesSubWorkJobs))
                renderFeatures |= WorkGridFeatureFlags.SubWork;

            WorkGridSnapshot presentationSnapshot = null;
            try
            {
                WorkGridInvalidationAudit.Poll(table);
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time(
                        "WorkTab.Snapshot.Prepare",
                        () => presentationSnapshot = _workGridSnapshots.Prepare(
                            organizer?.Layout,
                            table,
                            WorkTabInvalidationHub.Current));
                }
                else
                {
                    presentationSnapshot = _workGridSnapshots.Prepare(
                        organizer?.Layout,
                        table,
                        WorkTabInvalidationHub.Current);
                }
            }
            catch (Exception exception)
            {
                _workGridSnapshots.Clear();
                Log.ErrorOnce(
                    "[BWT] Work-grid snapshot construction failed; vanilla rendering remains active.\n" + exception,
                    0x42575453);
            }

            WorkTabEffectiveStateRevision effectiveRevision;
            IWorkTabEffectiveStateProvider effectiveState;
            if (SpineTiming.Enabled)
            {
                effectiveRevision = SpineTiming.Time(
                    "WorkTab.EffectiveState.BeginPass",
                    WorkTabEffectiveStateRuntime.BeginRenderPass);
                effectiveState = SpineTiming.Time(
                    "WorkTab.EffectiveState.CaptureView",
                    () => WorkTabEffectiveStateRuntime.CaptureCurrentView(effectiveRevision));
            }
            else
            {
                effectiveRevision = WorkTabEffectiveStateRuntime.BeginRenderPass();
                effectiveState = WorkTabEffectiveStateRuntime.CaptureCurrentView(effectiveRevision);
            }
            IWorkGridPreviewPort preview = WorkTabEffectiveStateScope.CurrentPreview;
            if (preview is IWorkGridPreviewViewSource previewViewSource)
            {
                preview = SpineTiming.Enabled
                    ? SpineTiming.Time(
                        "WorkTab.WorkloadPreview.CaptureView",
                        previewViewSource.CapturePreviewView)
                    : previewViewSource.CapturePreviewView();
            }
            return new WorkTabView(
                ImGuiEventPhases.Classify(evt.type),
                evt.type,
                organizer?.Layout,
                table,
                presentationSnapshot,
                organizer?.Layout?.GeometrySnapshot,
                effectiveState,
                effectiveRevision,
                preview,
                workGridRect,
                windowRect,
                ExtraBottomSpace,
                Time.frameCount,
                WorkTabInvalidationHub.Current,
                new WorkGridRenderConfiguration(renderFeatures, WorkGridLayerFlags.All),
                WorkGridSelectionScope.Window);
        }

        private void RenderWorkGrid(in WorkTabView view)
        {
            _workGridRenderer.Render(in view);
        }

        private void DrawFrameOverlaysAndChrome(
            in WorkTabView view,
            PawnOrganizerSystem organizer,
            Event evt)
        {
            if (SpineTiming.Enabled)
            {
                WorkTabView capturedView = view;
                SpineTiming.Time(
                    "WorkTab.DrawOverlaysAndChrome",
                    () => DrawFrameOverlaysAndChromeCore(in capturedView, organizer, evt));
                return;
            }

            DrawFrameOverlaysAndChromeCore(in view, organizer, evt);
        }

        private void DrawFrameOverlaysAndChromeCore(
            in WorkTabView view,
            PawnOrganizerSystem organizer,
            Event evt)
        {
            WorkTabView profiledView = view;
            if (!_workloadPreviewController.IsUnsafePreviewInputBlocked)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time(
                        "WorkTab.DrawTimePrioritySchedule",
                        () => TimePriorityScheduleEditor.Draw(profiledView.Layout));
                    SpineTiming.Time(
                        "WorkTab.DrawFluffyTimeSchedule",
                        () => FluffyTimeScheduleAssigner.Draw(
                            profiledView.WindowRect,
                            profiledView.Layout,
                            profiledView.ExtraBottomSpace));
                }
                else
                {
                    TimePriorityScheduleEditor.Draw(view.Layout);
                    FluffyTimeScheduleAssigner.Draw(
                        view.WindowRect,
                        view.Layout,
                        view.ExtraBottomSpace);
                }
            }
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.DrawSubWorkStyleChooser",
                    () => _subWorkStyleChooserPresenter.Draw(
                        profiledView.Layout,
                        windowRect,
                        profiledView.WindowRect));
            }
            else
            {
                _subWorkStyleChooserPresenter.Draw(view.Layout, windowRect, view.WindowRect);
            }

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawDragOverlays", () => organizer?.DrawDragOverlays());
            }
            else
            {
                organizer?.DrawDragOverlays();
            }

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.DrawChrome.Top",
                    () => _workTabChrome.DrawTopControls(
                        profiledView.Layout,
                        profiledView.WindowRect));
                SpineTiming.Time(
                    "WorkTab.DrawChrome.Bottom",
                    () => _workTabChrome.DrawBottomControls(in profiledView));
                SpineTiming.Time(
                    "WorkTab.DrawChrome.SubWorkExit",
                    () => _workTabChrome.DrawSubWorkExitButton(profiledView.WindowRect));
                SpineTiming.Time(
                    "WorkTab.DrawChrome.Counters",
                    () => _workTabChrome.DrawBottomCounters(
                        profiledView.WindowRect,
                        profiledView.Table));
                SpineTiming.Time(
                    "WorkTab.DrawTutorial",
                    () => BWTWorkTabTutorial.TickAndDraw(
                        profiledView.WindowRect,
                        profiledView.Layout));
            }
            else
            {
                _workTabChrome.DrawTopControls(view.Layout, view.WindowRect);
                _workTabChrome.DrawBottomControls(in view);
                _workTabChrome.DrawSubWorkExitButton(view.WindowRect);
                _workTabChrome.DrawBottomCounters(view.WindowRect, view.Table);
                BWTWorkTabTutorial.TickAndDraw(view.WindowRect, view.Layout);
            }
            // Serviced after the tutorial has drawn, so the harness resolves
            // targets against the geometry the player is actually looking at.
            if (BWTWorkTabTutorial.OwnsCurrentPointer && evt.type == EventType.Repaint)
            {
                Vector2 pointer = evt.mousePosition;
                TooltipHandler.ClearTooltipsFrom(new Rect(pointer.x - 1f, pointer.y - 1f, 2f, 2f));
            }
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.DrawWorkloadFooterPopover",
                    () => HeaderButtons.DrawWorkloadFooterPopoverOnTop(
                        profiledView.WindowRect,
                        WorkTabChromeGeometry.GetInfoIconRect(profiledView.WindowRect)));
            }
            else
            {
                HeaderButtons.DrawWorkloadFooterPopoverOnTop(
                    view.WindowRect,
                    WorkTabChromeGeometry.GetInfoIconRect(view.WindowRect));
            }
            NativeCursorPosition.ProcessPendingMove();
            NativeCursorPosition.DrawPendingMoveCue();
        }

        private bool TryRouteWorkloadPreviewScroll(
            Event evt,
            PawnTable table,
            Rect inRect,
            Rect gearRect)
        {
            HeaderButtons.BottomButtonRects buttonRects =
                HeaderButtons.GetBottomButtonRects(inRect, gearRect);
            if (!_workloadPreviewController.IsActive ||
                evt == null ||
                evt.type != EventType.ScrollWheel ||
                !buttonRects.ContainsWorkloadFooter(evt.mousePosition) ||
                table == null)
            {
                return false;
            }

            // Keep scrolling continuous when the pointer reaches the preview
            // footer. The viewport remains the sole scroll owner and the event
            // never reaches priority-cell input beneath the footer.
            Vector2 scrollPosition = table.scrollPosition;
            _viewportController.TryApplyScrollWheel(ref scrollPosition, evt);
            table.scrollPosition = scrollPosition;
            // Consume even if the previous frame did not publish a viewport.
            evt.Use();
            return true;
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
            WorkTabGameRoot gameRoot = WorkTabGameRoots.For(Current.Game);
            var settings = BetterWorkTabMod.Settings;
            bool useDividers = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.FeaturesDividers);
            IReadOnlyList<PawnDivider> dividers = useDividers
                ? gameRoot?.State?.Dividers?.ActiveDividers ??
                  (IReadOnlyList<PawnDivider>)Array.Empty<PawnDivider>()
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
            WorkTabUsageState.NotifyOpen(true);
            BWT20UpgradePrompt.ShowIfNeeded(BetterWorkTabMod.Settings);
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
            WorkTabUsageState.NotifyOpen(false);

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
            BWTWorkloadSettingsOwnershipPolicy.BindApplication(null);
            BWTWorkTabTutorial.NotifyWorkTabClosed();
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
            _workloadPreviewController.ResetForWindowClose();
            HeaderButtons.ResetWorkloadFooterState();
            // Work-grid snapshots and audit state belong to the game session, not this window.
            // GameCacheResetUtility owns their load/new-game teardown boundary.
        }
    }
}
