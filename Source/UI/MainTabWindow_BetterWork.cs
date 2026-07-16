using Better_Work_Tab.Features;
using Better_Work_Tab.Mod_Support.Multiplayer;
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts;
#endif
using Better_Work_Tab.Features.Caching;
using Better_Work_Tab.Features.Dividers;
using Better_Work_Tab.Features.Patches;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Testing;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Headers.Vanilla;
using Better_Work_Tab.UI.Input;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.RuleBuilderV2;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Interaction;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using Better_Work_Tab.UI.WorkGiverReassignments;
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
using Multiplayer.API;
#endif
using RimWorld;
using Spine.Profiling;
using Spine.RimWorld.Rendering;
using Spine.UI.ColourPicker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Main work tab window that coordinates PawnOrganizer layout with vanilla rendering.
    /// </summary>
    [StaticConstructorOnStartup]
    public class MainTabWindow_BetterWork : MainTabWindow_Work, IWorkGridInteractionHost
    {
        private readonly WorkGridRendererFacade _workGridRenderer;
        private readonly WorkGridInteractionRouter _workGridInteractionRouter;
        private readonly WorkGridSnapshotProvider _workGridSnapshots = new WorkGridSnapshotProvider();
        private static readonly FieldInfo PawnTableField =
            typeof(MainTabWindow_PawnTable).GetField("table", BindingFlags.NonPublic | BindingFlags.Instance);
        private static PawnColumnDef _lastDraggedColumn;
        private static Material _ruleBuilder2OutlineMaterial;
        private static readonly Dictionary<WorkTypeDef, bool> ColumnMarkerCache =
            new Dictionary<WorkTypeDef, bool>();
        private static BetterWorkTabSettings _columnMarkerCacheSettings;
        private static Game _columnMarkerCacheGame;
        private static int _columnMarkerCacheColumnsRevision = -1;
        private static int _columnMarkerCacheDraggedCount = -1;
        private static bool _columnMarkerCacheEnabled;
        private static int _columnMarkerCacheValidationFrame = -1;

        public MainTabWindow_BetterWork()
        {
            _workGridInteractionRouter = new WorkGridInteractionRouter(this);
            _workGridRenderer = new WorkGridRendererFacade(
                this,
                () => BetterWorkTabMod.Settings?.workGridRendererMode ?? DefaultSettings.workGridRendererMode);
            _workGridRenderer.Register(new OptimizedWorkGridRenderer(this));
        }
        
        /// <summary>
        /// Global notification that header settings (like rotation) have changed.
        /// Flushes all layout and drawing caches.
        /// </summary>
        public static void NotifyAngledHeadersChanged()
        {
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.HeaderText |
                WorkTabDirtyFlags.HeaderGeometry |
                WorkTabDirtyFlags.RenderResources |
                WorkTabDirtyFlags.WindowSize);
            PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
            
            if (Find.MainTabsRoot?.OpenTab?.TabWindow is MainTabWindow_BetterWork workTab)
            {
                var table = workTab.GetPawnTable();
                if (table != null)
                {
                    // Mark the table as dirty to force a full recache of heights and widths
                    var setDirtyMethod = typeof(PawnTable).GetMethod("SetDirty", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (setDirtyMethod != null)
                    {
                        setDirtyMethod.Invoke(table, null);
                    }
                    else
                    {
                        // Fallback if SetDirty not found (unlikely in vanilla but for safety)
                        MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                    }
                }
            }
        }

        private const float RightEdgeMargin = 10f;
        private const float InfoIconSize = 24f;
        private const float MinWorkTabHeight = 200f;
        private const float MinimumPawnRenderHeight = 30f;
        private const float DefaultPawnRowHeight = 30f;
        private const float ScrollViewFitAllowance = 4f;
        private const float HorizontalScrollbarHeight = 16f;
        private const float InlineScheduleFooterReclaim = 18f;
        private const float HostedFirstSubWorkAngledHeaderOffsetX = 5f;
        private const float ChooserPanelHeight = 118f;
        private const float ChooserRememberRowHeight = 24f;
        private const float ChooserNoteHeight = 22f;
        private const float ChooserWindowHeight = ChooserPanelHeight + ChooserRememberRowHeight + ChooserNoteHeight + 8f;
        private const float ChooserWindowGap = 6f;
        private const int ChooserImmediateWindowId = 984361;

        protected override float ExtraTopSpace =>
            Mathf.Clamp(
                BetterWorkTabMod.Settings?.workTabTopSpace ?? DefaultSettings.workTabTopSpace,
                0f,
                80f);

        protected override float ExtraBottomSpace =>
            base.ExtraBottomSpace + FluffyTimeScheduleAssigner.ReservedBottomSpace;

        private PawnColumnDef _lastSortColumn;
        private bool _lastSortDescending;
        private bool _pendingSubWorkGesture;
        private Vector2 _pendingSubWorkStart;
        private Rect _pendingSubWorkBounds;
        private WorkTypeDef _pendingSubWorkOpenType;
        private int _pendingSubWorkButton;
        private bool _pendingSubWorkExit;
        private bool _pendingSubWorkRestoreCursor;
        private int _suppressSubWorkPriorityMouseDownFrame = -1;
        private Rect _stableSubWorkChooserWindowRect;
        private bool _stableHorizontalScrollbarVisible;
        private bool _lastRawHorizontalOverflow;
        private bool _lastHorizontalScrollbarVisible;
        private int _holdHorizontalScrollbarUntilFrame = -1;
        private bool _horizontalScrollbarTransitionActive;
        private bool _horizontalScrollbarTransitionVisible;
        private int _horizontalOverflowBeganFrame = -1;
        private float _lastScrollWindowWidth = -1f;
        private float _lastScrollTotalColumnWidth = -1f;
        private float _lastScrollContentHeight = -1f;
        private bool _hasStableVerticalScrollbarState;
        private bool _stableVerticalScrollbarVisible;
        private bool _horizontalScrollbarDragCaptured;
        private float _horizontalScrollbarDragMouseX;
        private float _horizontalScrollbarDragScrollX;
        private float _horizontalScrollbarDragPixelsToContent = 1f;
        private static Color CurrentRowTextColor = Color.white;
        private readonly List<WorkTabLayoutColumn> _visibleRenderColumns = new List<WorkTabLayoutColumn>(64);
        private WorkTabSnapshot _organizerSnapshot;
        private IReadOnlyList<Pawn> _organizerSnapshotPawns;
        private IReadOnlyList<PawnDivider> _organizerSnapshotDividers;
        private int _requestedTabSizeCacheSignature = int.MinValue;
        private Vector2 _requestedTabSizeCache;
        private Rect _pendingWindowRect;
        private bool _hasPendingWindowRect;
        private PawnTable _warmOpenTable;
        private Game _warmOpenGame;
        private Map _warmOpenMap;
        private int _warmOpenScreenWidth;
        private int _warmOpenScreenHeight;
        private int _warmOpenLayoutRevision = -1;
        private int _warmOpenPawnCount = -1;
        private int _warmOpenColumnCount = -1;
        private bool _warmOpenStateValid;
        private int _draggedColumnsSyncSignature = int.MinValue;
        private PawnTable _cachedPawnTable;
        private Game _cachedPawnTableGame;
        private static string _cachedUiTextLanguage;
        private static int _cachedUiTextMaxPriority = -1;
        private static string _manualPrioritiesText;
        private static string _priorityHelpText;
        private static string _higherPriorityText;
        private static string _lowerPriorityText;

        internal bool LastRawHorizontalOverflow => _lastRawHorizontalOverflow;
        internal bool LastHorizontalScrollbarVisible => _lastHorizontalScrollbarVisible;

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        /// <summary>
        /// Multiplayer registration for column reordering sync.
        /// Uses nested class pattern to keep MP setup organized.
        /// </summary>
        [StaticConstructorOnStartup]
        private static class MPRegistration
        {
            static MPRegistration()
            {
                if (!MP.enabled)
                    return;

                MP.RegisterSyncMethod(typeof(MainTabWindow_BetterWork),
                                      nameof(MarkColumnMoved));
            }
        }
#endif


        public override void PreOpen()
        {
            base.PreOpen();
            var settings = BetterWorkTabMod.Settings;
            bool keepOpen = settings?.disableLeftClickClose ?? false;
            bool allowMapClose = settings?.closeOnMapClick ?? true;
            closeOnClickedOutside = !keepOpen && allowMapClose;
            UpdateTutorialAcceptKeyState();

            _lastSortColumn = null;
            _lastSortDescending = false;
            if (PawnOrganizerSystem.Instance == null)
            {
                var widthStore = new ColumnWidthPersistence();
                new PawnOrganizerSystem(widthStore);
            }

            // Sync the dragged columns list on open in case settings were loaded from disk
            SyncDraggedColumnsWithCurrentOrder();

            // Auto-enable manual priorities if setting is enabled
            if (settings?.autoEnableManualPriorities ?? false)
            {
                if (Current.Game?.playSettings != null)
                {
                    Current.Game.playSettings.useWorkPriorities = true;
                }
            }
        }

        /// <summary>
        /// Synchronizes the player-dragged columns list with the current column order.
        /// Removes any columns from the dragged list that are now back in their baseline position.
        /// This handles the case where saved settings had dragged columns but they've since been reset.
        /// </summary>
        private void SyncDraggedColumnsWithCurrentOrder()
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings?.playerDraggedColumns == null)
                return;

            var baselineOrder = WorkColumnOrderManager.GetBaselineOrder();
            if (baselineOrder == null || baselineOrder.Count == 0)
                return;

            var def = PawnTableDefOf.Work;
            if (def?.columns == null)
                return;

            int inputSignature = ComputeDraggedColumnsSyncSignature(
                def.columns,
                baselineOrder,
                settings.playerDraggedColumns);
            if (_draggedColumnsSyncSignature == inputSignature)
            {
                return;
            }

            var currentOrder = def.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .Select(c => c.workType.defName)
                .ToList();

            var filteredBaseline = baselineOrder.Where(b => currentOrder.Contains(b)).ToList();

            // Remove any dragged columns that are now back in vanilla RELATIVE position
            var toRemove = new List<string>();
            foreach (var defName in settings.playerDraggedColumns)
            {
                int relVanillaPos = filteredBaseline.IndexOf(defName);
                int currentPos = currentOrder.IndexOf(defName);

                // If the column is back in its relative baseline spot, unmark it
                if (relVanillaPos >= 0 && relVanillaPos == currentPos)
                {
                    toRemove.Add(defName);
                }
            }

            // Perform removals
            foreach (var defName in toRemove)
            {
                settings.playerDraggedColumns.Remove(defName);
            }

            // HEAL PHASE: If there is a custom order but NO columns are marked as dragged,
            // then the intent data is lost. Use the Longest Increasing Subsequence (LIS) 
            // approach to find the minimum number of 'moves' to explain the current table.
            if (settings.playerDraggedColumns.Count == 0)
            {
                var currentIndices = currentOrder.Select(c => filteredBaseline.IndexOf(c)).ToList();

                // Simple LIS (Patient Sorting style)
                var tails = new List<int>();
                var prev = new int[currentIndices.Count];
                var tailIdx = new List<int>();

                for (int i = 0; i < currentIndices.Count; i++) {
                    int val = currentIndices[i];
                    int pos = tails.BinarySearch(val);
                    if (pos < 0) pos = ~pos;

                    if (pos < tails.Count) {
                        tails[pos] = val;
                        tailIdx[pos] = i;
                    } else {
                        tails.Add(val);
                        tailIdx.Add(i);
                    }
                    prev[i] = (pos > 0) ? tailIdx[pos-1] : -1;
                }

                // Reconstruct LIS indices
                var lisIndices = new HashSet<int>();
                if (tailIdx.Count > 0) {
                    int curr = tailIdx.Last();
                    while (curr != -1) {
                        lisIndices.Add(curr);
                        curr = prev[curr];
                    }
                }

                // Mark elements NOT in LIS as moved
                for (int i = 0; i < currentOrder.Count; i++) {
                    if (!lisIndices.Contains(i)) {
                        string defName = currentOrder[i];
                        settings.playerDraggedColumns.Add(defName);
                        toRemove.Add(defName); // Trigger save
                    }
                }
            }

            if (toRemove.Count > 0)
            {
                settings.Write();
            }

            _draggedColumnsSyncSignature = ComputeDraggedColumnsSyncSignature(
                def.columns,
                baselineOrder,
                settings.playerDraggedColumns);
        }

        private static int ComputeDraggedColumnsSyncSignature(
            IReadOnlyList<PawnColumnDef> columns,
            IReadOnlyList<string> baselineOrder,
            IEnumerable<string> draggedColumns)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (columns?.Count ?? 0);
                if (columns != null)
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        PawnColumnDef column = columns[i];
                        if (column?.Worker is PawnColumnWorker_WorkPriority && column.workType != null)
                        {
                            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(column.workType.defName ?? string.Empty);
                        }
                    }
                }

                hash = (hash * 31) + (baselineOrder?.Count ?? 0);
                if (baselineOrder != null)
                {
                    for (int i = 0; i < baselineOrder.Count; i++)
                    {
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(baselineOrder[i] ?? string.Empty);
                    }
                }

                if (draggedColumns != null)
                {
                    int draggedCount = 0;
                    int draggedSum = 0;
                    int draggedXor = 0;
                    foreach (string defName in draggedColumns)
                    {
                        int nameHash = StringComparer.Ordinal.GetHashCode(defName ?? string.Empty);
                        draggedCount++;
                        draggedSum += nameHash;
                        draggedXor ^= nameHash;
                    }

                    hash = (hash * 31) + draggedCount;
                    hash = (hash * 31) + draggedSum;
                    hash = (hash * 31) + draggedXor;
                }

                return hash;
            }
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
            if (BWTWorkTabTutorial.TryHandleAcceptKey())
            {
                return;
            }

            base.OnAcceptKeyPressed();
        }

        private void DoWindowContentsProfiled(Rect inRect)
        {
            UpdateTutorialAcceptKeyState();
            PawnTable table = GetPawnTable();
            if (table == null) return;
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
                ? GetHeaderAnchoredContentHeight(organizer.Layout)
                : Mathf.Max(0f, table.Size.y - effectiveHeaderHeight);
            float tableOriginY = inRect.yMax -
                ExtraBottomSpace -
                ScrollViewFitAllowance -
                effectiveHeaderHeight -
                GetHeaderAnchoredPinnedRowsHeight() -
                previousContentHeight;
            Vector2 tableOrigin = new Vector2(inRect.x, tableOriginY);
            WorkGridInvalidationAudit.PollRoster(table);
            var snapshot = BuildSnapshotForOrganizer(table);

            SubWorkDrilldownState.TickTransition();
            RefreshSubWorkLayoutIfNeeded(organizer, table, tableOrigin, snapshot);

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

                float anchoredOriginY = inRect.yMax -
                    ExtraBottomSpace -
                    ScrollViewFitAllowance -
                    organizer.Layout.HeaderHeight -
                    GetHeaderAnchoredPinnedRowsHeight() -
                    GetHeaderAnchoredContentHeight(organizer.Layout);
                if (Mathf.Abs(anchoredOriginY - tableOrigin.y) > 0.01f)
                {
                    tableOrigin.y = anchoredOriginY;
                    organizer.Update(table, tableOrigin, snapshot);
                }
            }

            if (Event.current.type == EventType.Repaint)
            {
                CaptureWarmOpenTableState(table);
            }

            ResizeWindowBottomAnchoredIfRequestedSizeChanged();
            TimePriorityScheduleEditor.TryOpenAgentRequestedSession(organizer?.Layout);
            FluffyTimeScheduleAssigner.ProcessAgentRequest();
            RuleBuilder2AgentHarness.ProcessSelectionRequest(organizer?.Layout);

            Event evt = Event.current;
            BWTWorkTabTutorial.UpdatePointerOwnership(inRect, organizer?.Layout, evt.mousePosition);
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                if (evt.type == EventType.MouseDown || evt.type == EventType.ScrollWheel)
                {
                    WorkTabGeometryDiagnostics.RecordPriorityInputTrace("work-tab input entry", evt);
                }

                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Input", () => RouteWorkTabInput(inRect, organizer, evt));
                }
                else
                {
                    RouteWorkTabInput(inRect, organizer, evt);
                }

                SuppressSubWorkPriorityMouseDownIfNeeded(evt);
                RefreshSubWorkLayoutIfNeeded(organizer, table, tableOrigin, snapshot);
                ResizeWindowBottomAnchoredIfRequestedSizeChanged();
            }

            if (evt.type != EventType.Layout)
            {
                UpdateRuleBuilder2WorkTabHover(organizer?.Layout, inRect);
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
            // scroll control state across Layout, input, and Repaint. DrawWorkTable's
            // Layout path creates that control without painting rows or headers.
            if (evt.type == EventType.Layout)
            {
                return;
            }

            TimePriorityScheduleEditor.Draw(organizer?.Layout);
            FluffyTimeScheduleAssigner.Draw(inRect, organizer?.Layout, base.ExtraBottomSpace);
            DrawSubWorkStyleChooserComparison(organizer?.Layout);

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawDragOverlays", () => organizer?.DrawDragOverlays());
                SpineTiming.Time("WorkTab.DrawExternalWorkTabSwitch", () => FluffyWorkTabGateway.DrawWorkTabSwitchButton(inRect));
                SpineTiming.Time("WorkTab.DrawFluffyStyleTopButtons", () => HeaderButtons.DrawTopRightFluffyStyle(organizer?.Layout, inRect));
                SpineTiming.Time("WorkTab.DrawManualPrioritiesCheckbox", DrawManualPrioritiesCheckbox);
                SpineTiming.Time("WorkTab.DrawPriorityLegend", () => DrawPriorityLegend(inRect));
                SpineTiming.Time("WorkTab.DrawContextSettingsHint", () => DrawContextSettingsHint(inRect));
            }
            else
            {
                organizer?.DrawDragOverlays();
                FluffyWorkTabGateway.DrawWorkTabSwitchButton(inRect);
                HeaderButtons.DrawTopRightFluffyStyle(organizer?.Layout, inRect);
                DrawManualPrioritiesCheckbox();
                DrawPriorityLegend(inRect);
                DrawContextSettingsHint(inRect);
            }

            WorkTabColorPreviewRenderer.Draw(organizer?.Layout, inRect);

            bool mouseInside = !BWTWorkTabTutorial.OwnsCurrentPointer && Mouse.IsOver(inRect);
            Rect infoRect = GetInfoIconRect(inRect);
            DrawBottomRightButtons(organizer?.Layout, inRect, infoRect);
            if (mouseInside)
            {
                DrawInfoButton(infoRect);
            }

            DrawSubWorkExitButton(inRect);
            DrawBottomCounters(inRect, table);
            BWTWorkTabTutorial.TickAndDraw(inRect, organizer?.Layout);
            NativeCursorPosition.ProcessPendingMove();
            NativeCursorPosition.DrawPendingMoveCue();
            Better_Work_Tab.Features.Testing.SubWorkTransitionPerfDiagnostics.RecordWorkTabRepaint();
        }

        private void RouteWorkTabInput(Rect inRect, PawnOrganizerSystem organizer, Event evt)
        {
            _workGridInteractionRouter.Route(inRect, organizer, evt);
        }

        private static bool TryHandleWorkGiverHistoryShortcut(Event evt)
        {
            if (evt.type != EventType.KeyDown ||
                !evt.control ||
                GUIUtility.keyboardControl != 0 ||
                BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false;
            }

            bool redo = evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shift);
            bool undo = evt.keyCode == KeyCode.Z && !evt.shift;
            bool changed = redo
                ? WorkGiverReassignmentManager.TryRedoWorkGiverLayout()
                : undo && WorkGiverReassignmentManager.TryUndoWorkGiverLayout();
            if (!changed)
            {
                return false;
            }

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }

        bool IWorkGridInteractionHost.TryHandleHistoryShortcut(Event evt) =>
            TryHandleWorkGiverHistoryShortcut(evt);
        bool IWorkGridInteractionHost.TryHandleTutorial(Rect inRect, IWorkTabLayoutController layout, Event evt) =>
            BWTWorkTabTutorial.TryHandleInput(inRect, layout, evt);
        void IWorkGridInteractionHost.ReportTutorial(Rect inRect, IWorkTabLayoutController layout, Event evt) =>
            ReportTutorialInteraction(inRect, layout, evt);
        bool IWorkGridInteractionHost.TryHandlePriorityCell(IWorkTabLayoutController layout, Event evt) =>
            TryHandlePriorityCellInput(layout, evt);
        bool IWorkGridInteractionHost.TryHandleTopButtons(IWorkTabLayoutController layout, Rect inRect, Event evt) =>
            HeaderButtons.TryHandleTopRightFluffyStyleInput(layout, inRect, evt);
        bool IWorkGridInteractionHost.TryHandleFluffySchedule(Event evt) =>
            FluffyTimeScheduleAssigner.TryHandleInput(evt);
        bool IWorkGridInteractionHost.TryHandleContextSettings(Rect inRect, IWorkTabLayoutController layout, Event evt) =>
            TryHandleContextSettingsClick(inRect, layout, evt);
        bool IWorkGridInteractionHost.TryHandleRuleTarget(IWorkTabLayoutController layout, Event evt) =>
            TryHandleRuleBuilder2WorkTabInput(layout, evt);
        bool IWorkGridInteractionHost.TryHandleSchedule(IWorkTabLayoutController layout, Event evt) =>
            TimePriorityScheduleEditor.TryHandleInput(layout, evt);
        bool IWorkGridInteractionHost.TryHandleSubWorkBadge(IWorkTabLayoutController layout) =>
            TryHandleSubWorkBadgeClick(layout);
        bool IWorkGridInteractionHost.TryHandleSubWorkExit(IWorkTabLayoutController layout) =>
            TryHandleSubWorkExitGesture(layout);
        bool IWorkGridInteractionHost.TryHandleSubWorkOpen(IWorkTabLayoutController layout) =>
            TryHandleSubWorkHeaderOpen(layout);
        void IWorkGridInteractionHost.ProcessRightClicks(IWorkTabLayoutController layout) => ProcessRightClicks(layout);
        void IWorkGridInteractionHost.HandleOrganizerInput(PawnOrganizerSystem organizer, Event evt) => organizer?.HandleInput(evt);

        private void RefreshSubWorkLayoutIfNeeded(
            PawnOrganizerSystem organizer,
            PawnTable table,
            Vector2 tableOrigin,
            IPawnOrganizerSnapshot snapshot)
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
            _requestedTabSizeCacheSignature = int.MinValue;
            SetDirty();

            if (organizer != null && table != null && snapshot != null && !organizer.IsDragging)
            {
                organizer.Update(table, tableOrigin, snapshot);
            }
        }

        private void UpdateTutorialAcceptKeyState()
        {
            // Keep RimWorld's accept-key dispatch enabled. The override above
            // consumes Enter while a Work tab tutorial is active and otherwise
            // falls back to the vanilla main-tab close behavior.
            closeOnAccept = true;
        }

        private void ResizeWindowBottomAnchoredIfRequestedSizeChanged(bool force = false)
        {
            Vector2 requestedSize = RequestedTabSize;
            if (requestedSize.x <= 0f || requestedSize.y <= 0f)
            {
                return;
            }

            Rect rect = _hasPendingWindowRect ? _pendingWindowRect : windowRect;
            if (!force &&
                Mathf.Abs(rect.width - requestedSize.x) < 0.5f &&
                Mathf.Abs(rect.height - requestedSize.y) < 0.5f)
            {
                return;
            }

            float screenBottom = Verse.UI.screenHeight - 35f;
            rect.width = requestedSize.x;
            rect.height = requestedSize.y;
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Verse.UI.screenWidth - rect.width));
            rect.y = Mathf.Max(0f, screenBottom - rect.height);
            _pendingWindowRect = rect;
            _hasPendingWindowRect = true;
        }

        private IPawnOrganizerSnapshot BuildSnapshotForOrganizer(PawnTable table)
        {
            IReadOnlyList<Pawn> pawns = table.PawnsListForReading;
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

        private void ProcessRightClicks(IWorkTabLayoutController layout)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableContextMenuOnRightClick ?? true))
            {
                return;
            }

            if (layout == null)
            {
                return;
            }

            Event evt = Event.current;
            bool rightMouseDown = evt.type == EventType.MouseDown && evt.button == 1;
            bool rightMouseUp = evt.type == EventType.MouseUp && evt.button == 1;
            if (!rightMouseDown && !rightMouseUp)
            {
                return;
            }

            if (rightMouseDown)
            {
                PawnOrganizerSystem.Instance?.CancelActiveDrag();
            }

            if (!TryGetRowAt(layout, evt.mousePosition, out var row))
            {
                return;
            }

            if (row.Divider != null)
            {
                if (rightMouseDown)
                {
                    ShowDividerContextMenu(row.Divider);
                }

                evt.Use();
                return;
            }

            if (row.Pawn == null)
            {
                return;
            }

            bool overWorkPriorityColumn = TryGetBodyColumnAt(layout, evt.mousePosition, out var column) &&
                column.Column?.Worker is PawnColumnWorker_WorkPriority;
            if (!overWorkPriorityColumn)
            {
                if (rightMouseDown)
                {
                    ShowPawnContextMenu(row.Pawn);
                }

                evt.Use();
            }
        }

        private void ShowPawnContextMenu(Pawn pawn)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Insert divider above", () => InsertDividerAbove(pawn)),
                new FloatMenuOption("Insert divider below", () => InsertDividerBelow(pawn)),
                new FloatMenuOption("Set background color...", () => ShowBackgroundColorPicker(pawn))
            };
            if (PawnTitleUtility.CanEditTitle(pawn))
            {
                options.Insert(2, new FloatMenuOption("Change title...", () =>
                {
                    Find.WindowStack.Add(new Dialog_ChangePawnTitle(pawn));
                }));
            }

            if (PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out _))
            {
                options.Add(new FloatMenuOption("Clear background color", () =>
                {
                    PawnOrganizer.API.PawnColorDatabase.ClearColor(pawn);
                }));
            }
            
            // Multiplayer follow mode: Copy this pawn row
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    $"Copy {pawn.NameShortColored} row position to my layout (stop following)",
                    () => LayoutSharingManager.CopyPawnRowToLocalAndStop(pawn)));
            }
#endif

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowRenamePawnDialog(Pawn pawn)
        {
#if v1_3 || v1_2 || v1_1
            Find.WindowStack.Add(new Dialog_NamePawn(pawn));
#else
            Find.WindowStack.Add(pawn.NamePawnDialog());
#endif
        }

        private void ShowDividerContextMenu(PawnDivider divider)
        {
            if (!(BetterWorkTabMod.Settings?.enableDividers ?? true))
            {
                return;
            }

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit...", () =>
                {
                    Find.WindowStack.Add(new Dialog_EditDivider(divider));
                }),
                new FloatMenuOption("Delete", () =>
                {
                    PawnOrganizerSystem.Instance?.Layout.RemoveDivider(divider);
                    NotifyDividerLayoutChanged();
                })
            };
            
            // Multiplayer follow mode: Copy this divider
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    "Copy this divider to my layout (stop following)",
                    () => LayoutSharingManager.CopyDividerToLocalAndStop(divider)));
            }
#endif

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void InsertDividerAbove(Pawn pawn)
        {
            var worklist = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || worklist == null)
            {
                return;
            }

            if (!(BetterWorkTabMod.Settings?.enableDividers ?? true))
            {
                return;
            }

            if (layout.AddDividerBeforePawn(pawn, "New Divider", Color.gray) != null)
            {
                NotifyDividerLayoutChanged();
            }
        }


        /// <summary>
        /// Computes the work tab size; avoids rebuilding layout during an active drag.
        /// </summary>
        public override Vector2 RequestedTabSize
        {
            get
            {
                var table = GetPawnTable();
                if (table == null) return Vector2.zero;

                float finalHeight;
                float finalWidth;

                var organizer = PawnOrganizerSystem.Instance;
                int sizeSignature = ComputeRequestedTabSizeSignature(table, organizer?.Layout);
                if (_requestedTabSizeCacheSignature == sizeSignature)
                {
                    return _requestedTabSizeCache;
                }

                if (organizer?.Layout != null)
                {
                    if (!organizer.IsDragging)
                    {
                        var snapshot = BuildSnapshotForOrganizer(table);
                        organizer.Update(table, new Vector2(0f, ExtraTopSpace), snapshot);
                    }

                    // Use table's current header height (updates dynamically with vanilla staggering)
                    // combined with layout controller's content height (includes dividers)
                    // This is consistent during drag, preventing scrollbar flickers
                    float pinnedRowsHeight = GetHeaderAnchoredPinnedRowsHeight();
                    float layoutHeight = organizer.Layout.HeaderHeight +
                        pinnedRowsHeight +
                        GetHeaderAnchoredContentHeight(organizer.Layout);
                    finalHeight = layoutHeight + ExtraBottomSpace + ExtraTopSpace + Margin * 2f + ScrollViewFitAllowance;
                    float tableScrollWidth = GetVisualTableScrollWidth(organizer.Layout, table);
                    if (BetterWorkTabMod.Settings?.keepVanillaWorkTabMinimumWidth ??
                        DefaultSettings.keepVanillaWorkTabMinimumWidth)
                    {
                        // PawnTable.Size.x is the width vanilla MainTabWindow_PawnTable requests.
                        // Preserve it as a floor while still allowing wider rendered content to grow right.
                        tableScrollWidth = Mathf.Max(tableScrollWidth, table.Size.x);
                    }

                    finalWidth = tableScrollWidth + Margin * 2f;
                }
                else
                {
                    // Fallback to vanilla size if organizer not ready
                    finalHeight = table.Size.y + ExtraBottomSpace + ExtraTopSpace + Margin * 2f + ScrollViewFitAllowance;
                    finalWidth = table.Size.x + Margin * 2f;
                }

                float maxWindowWidth = Mathf.Max(1f, Verse.UI.screenWidth - 2f);
                float tutorialReserveWidth = BWTWorkTabTutorial.PreferredReserveWidth;
                if (tutorialReserveWidth > 0f)
                {
                    if (finalWidth + tutorialReserveWidth <= maxWindowWidth)
                    {
                        finalWidth += tutorialReserveWidth;
                        finalHeight = Mathf.Max(finalHeight, BWTWorkTabTutorial.PreferredReserveHeight);
                    }
                    else
                    {
                        // At 1024-wide/high-scale layouts the selector moves into
                        // a top band instead of covering the highlighted grid.
                        finalHeight += BWTWorkTabTutorial.PreferredReserveHeight;
                    }
                }
                bool needsHorizontalScrollbar = finalWidth > maxWindowWidth + 0.5f;
                if (needsHorizontalScrollbar)
                {
                    finalHeight += HorizontalScrollbarHeight;
                }

                finalHeight = Mathf.Min(
                    finalHeight,
                    GetConfiguredMaxWindowHeight(organizer?.Layout, table, needsHorizontalScrollbar));
                if (tutorialReserveWidth > 0f)
                {
                    finalHeight = Mathf.Min(
                        Verse.UI.screenHeight - 35f,
                        Mathf.Max(finalHeight, BWTWorkTabTutorial.PreferredReserveHeight));
                }
                finalWidth = Mathf.Min(finalWidth, maxWindowWidth);

                _requestedTabSizeCacheSignature = ComputeRequestedTabSizeSignature(table, organizer?.Layout);
                _requestedTabSizeCache = new Vector2(finalWidth, finalHeight);
                return _requestedTabSizeCache;
            }
        }

        private int ComputeRequestedTabSizeSignature(PawnTable table, IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            // This is a cache key, so it must never perform the work it is meant to
            // guard. PawnTable.Size calls RecacheIfDirty and previously made this
            // signature cost up to 15.77 ms on first open. Use already-cached fields
            // plus authoritative dirty/layout revisions; a miss will perform the
            // normal refresh in RequestedTabSize's calculation path.
            Vector2 tableSize = PawnTableCompat.GetCachedSize(table);
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (layout?.LayoutRevision ?? -1);
                hash = (hash * 31) + (PawnTableCompat.IsDirty(table) ? 1 : 0);
                hash = (hash * 31) + PawnTableCompat.GetPawnCount(table);
                hash = (hash * 31) + Mathf.RoundToInt(tableSize.x * 10f);
                hash = (hash * 31) + Mathf.RoundToInt(tableSize.y * 10f);
                hash = (hash * 31) + Mathf.RoundToInt(PawnTableCompat.GetCachedHeaderHeight(table) * 10f);
                hash = (hash * 31) + Verse.UI.screenWidth;
                hash = (hash * 31) + Verse.UI.screenHeight;
                hash = (hash * 31) + (FluffyTimeScheduleAssigner.IsOpen ? 1 : 0);
                hash = (hash * 31) + TimePriorityScheduleEditor.LayoutSignature;
                hash = (hash * 31) + SubWorkDrilldownState.MeasurementSignature;
                hash = (hash * 31) + (settings?.workTabMaxVisiblePawns ?? DefaultSettings.workTabMaxVisiblePawns);
                hash = (hash * 31) + ((settings?.keepVanillaWorkTabMinimumWidth ??
                                       DefaultSettings.keepVanillaWorkTabMinimumWidth) ? 1 : 0);
                hash = (hash * 31) + ((settings?.showGeneralTutorial ?? false) ? 1 : 0);
                hash = (hash * 31) + ((settings?.tutorialWelcomeCompleted ?? false) ? 1 : 0);
                return hash;
            }
        }

        private float GetConfiguredMaxWindowHeight(
            IWorkTabLayoutController layout,
            PawnTable table,
            bool needsHorizontalScrollbar)
        {
            float screenMaxHeight = Mathf.Max(Verse.UI.screenHeight - 35f, MinWorkTabHeight);
            int maxVisiblePawns = BetterWorkTabMod.Settings?.workTabMaxVisiblePawns ??
                DefaultSettings.workTabMaxVisiblePawns;
            if (maxVisiblePawns <= 0)
            {
                return screenMaxHeight;
            }

            float headerHeight = layout?.HeaderHeight ?? PawnTableCompat.GetCachedHeaderHeight(table);
            float pinnedRowsHeight = layout != null ? GetHeaderAnchoredPinnedRowsHeight() : 0f;
            float pawnRowHeight = GetNominalPawnRowHeight(layout);
            float visibleContentHeight = Mathf.Max(1, maxVisiblePawns) * pawnRowHeight;
            float configuredHeight =
                ExtraTopSpace +
                headerHeight +
                pinnedRowsHeight +
                visibleContentHeight +
                ExtraBottomSpace +
                Margin * 2f +
                ScrollViewFitAllowance +
                (needsHorizontalScrollbar ? HorizontalScrollbarHeight : 0f);

            return Mathf.Min(screenMaxHeight, Mathf.Max(configuredHeight, MinWorkTabHeight));
        }

        private static float GetNominalPawnRowHeight(IWorkTabLayoutController layout)
        {
            var descriptors = layout?.GetRowDescriptors();
            if (descriptors != null)
            {
                for (int i = 0; i < descriptors.Count; i++)
                {
                    if (descriptors[i]?.Pawn != null && descriptors[i].Height > 0f)
                    {
                        return Mathf.Max(DefaultPawnRowHeight, descriptors[i].Height);
                    }
                }
            }

            return DefaultPawnRowHeight;
        }

        internal bool TryGetRuleBuilder2HeaderBand(out Rect screenRect)
        {
            screenRect = Rect.zero;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || layout.HeaderHeight <= 0f)
            {
                return false;
            }

            float width = GetVisualColumnWidth(layout);

            screenRect = new Rect(
                windowRect.x + layout.TableOrigin.x,
                windowRect.y + layout.TableOrigin.y,
                Mathf.Max(1f, width),
                layout.HeaderHeight);
            return true;
        }

        internal bool TryGetRuleBuilder2TargetHeaderBounds(WorkTypeDef workType, WorkGiverDef workGiver, out Rect bounds)
        {
            bounds = Rect.zero;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout?.Columns == null || workType == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                ResolveRuleBuilder2Target(column, out WorkTypeDef columnWorkType, out WorkGiverDef columnWorkGiver);
                if (columnWorkType != workType ||
                    (columnWorkGiver?.defName ?? "") != (workGiver?.defName ?? ""))
                {
                    continue;
                }

                return TryGetRuleBuilder2HeaderHighlight(
                    column,
                    GetAnimatedHeaderRect(column),
                    layout.Table,
                    out var headerHighlight) &&
                    IsUsableRect(bounds = headerHighlight.Bounds);
            }

            return false;
        }

        private bool TryHandleSubWorkStyleChooser(IWorkTabLayoutController layout, Event evt)
        {
            if (!FluffyWorkTabGateway.TryHandleSubWorkDrilldownStyleChooserInput(
                    layout,
                    evt,
                    out var workType,
                    out var style,
                    out int sourceWorkColumnSlot))
            {
                return false;
            }

            if (workType == null || style == BetterWorkTabSettings.SubWorkDrilldownStyle.NotChosen)
            {
                return true;
            }

            TimePriorityScheduleEditor.CloseForWorkModeTransition();
            if (style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                if (!SubWorkDrilldownState.IsExpandBesideExpanded(workType))
                {
                    EnterFluffyHostedSubWork(workType);
                }
            }
            else
            {
                if (!SubWorkDrilldownState.IsActive || SubWorkDrilldownState.ActiveWorkType != workType)
                {
                    SubWorkDrilldownState.EnterFromSourceSlot(
                        workType,
                        null,
                        SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(layout?.Table, layout?.HeaderHeight ?? -1f),
                        null,
                        sourceWorkColumnSlot);
                }
            }

            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
            return true;
        }


        private void InsertDividerBelow(Pawn pawn)
        {
            var worklist = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
            var layout = PawnOrganizerSystem.Instance?.Layout;
            if (layout == null || pawn == null || worklist == null)
            {
                return;
            }

            if (!(BetterWorkTabMod.Settings?.enableDividers ?? true))
            {
                return;
            }

            if (layout.AddDividerAfterPawn(pawn, "New Divider", Color.gray) != null)
            {
                NotifyDividerLayoutChanged();
            }
        }

        private void NotifyDividerLayoutChanged()
        {
            PawnOrganizerSystem.Instance?.CancelActiveDrag();
            PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
            SetDirty();
            RefreshOrganizerLayoutForCurrentTable();
            ResizeWindowBottomAnchoredIfRequestedSizeChanged();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
            if (MultiplayerBridge.Active)
                LayoutSharingManager.NotifyLayoutChanged();
#endif
        }

        private void RefreshOrganizerLayoutForCurrentTable()
        {
            var organizer = PawnOrganizerSystem.Instance;
            var table = GetPawnTable();
            if (organizer == null || table == null || organizer.IsDragging)
            {
                return;
            }

            organizer.Update(table, new Vector2(0f, ExtraTopSpace), BuildSnapshotForOrganizer(table));
        }

        private void ShowBackgroundColorPicker(Pawn pawn)
        {
            Color current = PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out var stored) ? stored : Color.white;
            Find.WindowStack.Add(new Dialog_ColourPicker(current, (newColor, _) =>
            {
                PawnOrganizerSystem.Instance?.SetPawnBackgroundColor(pawn, newColor);
            }));
        }

        internal void DrawNativeWorkTable(PawnTable table, IWorkTabLayoutController layout, Rect inRect)
        {
            DrawWorkTable(table, layout, inRect, null);
        }

        internal void DrawSnapshotWorkTable(
            in WorkGridRenderContext context,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            DrawWorkTable(
                context.Presentation.Table,
                context.Layout,
                context.WindowRect,
                snapshotLayer);
        }

        private void DrawWorkTable(
            PawnTable table,
            IWorkTabLayoutController layout,
            Rect inRect,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            if (layout == null || table == null)
            {
                return;
            }

            Rect outRect = default(Rect);
            Rect viewRect = default(Rect);

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.CalculateScrollRects", () => CalculateScrollRects(layout, inRect, out outRect, out viewRect));
                SpineTiming.Time("WorkTab.UpdateSortState", () => UpdateSortState(table));
            }
            else
            {
                CalculateScrollRects(layout, inRect, out outRect, out viewRect);
                UpdateSortState(table);
            }

            bool layoutEvent = Event.current.type == EventType.Layout;
            if (!layoutEvent && SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawHeaders", () => DrawHeaders(layout, table));
            }
            else if (!layoutEvent)
            {
                DrawHeaders(layout, table);
            }
            if (!layoutEvent && SubWorkDrilldownState.HasAnyDrilldown)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.DrawSubWorkGlobalRow", () => SubWorkDrilldownBarRenderer.Draw(layout));
                }
                else
                {
                    SubWorkDrilldownBarRenderer.Draw(layout);
                }
            }
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawRows", () => DrawRows(table, layout, outRect, viewRect, snapshotLayer));
                if (!layoutEvent)
                {
                    SpineTiming.Time("WorkTab.DrawSubWorkTransitionWave", () => DrawSubWorkTransitionPixelWave(layout));
                }
            }
            else
            {
                DrawRows(table, layout, outRect, viewRect, snapshotLayer);
                if (!layoutEvent)
                {
                    DrawSubWorkTransitionPixelWave(layout);
                }
            }

            if (!layoutEvent)
            {
                WorkTabGeometryDiagnostics.DumpHeaderLayoutIfRequested(layout);
            }
        }

        private void DrawSubWorkStyleChooserComparison(IWorkTabLayoutController layout)
        {
            if (!FluffyWorkTabGateway.IsSubWorkStyleChooserActive)
            {
                _stableSubWorkChooserWindowRect = Rect.zero;
                return;
            }

            if (!CanShowChooserComparison(layout))
            {
                return;
            }

            // Hover previews deliberately animate the Work window and its columns. The
            // chooser is an input surface, so its screen-space hitboxes must remain fixed
            // from open until selection; otherwise the cursor can fall off a moving choice
            // and repeatedly reverse the preview animation.
            if (_stableSubWorkChooserWindowRect.width <= 1f || _stableSubWorkChooserWindowRect.height <= 1f)
            {
                _stableSubWorkChooserWindowRect = GetChooserWindowRect();
            }

            Rect chooserWindowRect = _stableSubWorkChooserWindowRect;
            Find.WindowStack.ImmediateWindow(
                ChooserImmediateWindowId,
                chooserWindowRect,
                WindowLayer.Super,
                () => DrawSubWorkStyleChooserImmediateWindow(layout, chooserWindowRect.AtZero()),
                doBackground: false,
                absorbInputAroundWindow: false,
                shadowAlpha: 0.35f);

            if (!FluffyWorkTabGateway.IsSubWorkStyleChooserActive)
            {
                _stableSubWorkChooserWindowRect = Rect.zero;
            }
        }

        private void DrawSubWorkStyleChooserImmediateWindow(
            IWorkTabLayoutController layout,
            Rect inRect)
        {
            ChooserComparisonGeometry geometry = BuildChooserComparisonGeometry(inRect);
            FluffyWorkTabGateway.RegisterSubWorkStyleChooserRegions(
                geometry.FocusRegion,
                geometry.ExpandRegion,
                geometry.RememberRegion);

            Event evt = Event.current;
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                TryHandleSubWorkStyleChooser(layout, evt);
            }

            if (!FluffyWorkTabGateway.IsSubWorkStyleChooserActive)
            {
                return;
            }

            FluffyWorkTabGateway.UpdateSubWorkDrilldownStyleChooserPreview(layout);

            if (evt.type != EventType.Repaint)
            {
                return;
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                DrawChooserChoiceFrame(
                    geometry.FocusRegion,
                    "BWT_SubWork_Chooser_FocusTitle".Translate(),
                    "BWT_SubWork_Chooser_FocusDescription".Translate());
                DrawChooserChoiceFrame(
                    geometry.ExpandRegion,
                    "BWT_SubWork_Chooser_ExpandTitle".Translate(),
                    "BWT_SubWork_Chooser_ExpandDescription".Translate());
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }

            FluffyWorkTabGateway.DrawSubWorkDrilldownStyleChooser(layout, inRect);
        }

        private static bool CanShowChooserComparison(IWorkTabLayoutController layout)
        {
            if (!FluffyWorkTabGateway.IsSubWorkStyleChooserActive ||
                layout?.Table == null ||
                layout.Columns == null)
            {
                return false;
            }

            WorkTypeDef workType = FluffyWorkTabGateway.SubWorkStyleChooserWorkType;
            WorkTabLayoutColumn? sourceColumn = FindChooserSourceColumn(layout, workType);
            if (!sourceColumn.HasValue)
            {
                return false;
            }

            IReadOnlyList<WorkGiver> workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
            if (workGivers == null || workGivers.Count == 0)
            {
                return false;
            }

            if (!FluffyWorkTabGateway.TryBuildHostedColumnSpecs(
                    sourceColumn.Value.Column,
                    workType,
                    out _,
                    out _))
            {
                return false;
            }

            return true;
        }

        private Rect GetChooserWindowRect()
        {
            const float screenMargin = 8f;
            float availableWidth = Mathf.Max(360f, windowRect.width - 80f);
            float width = Mathf.Min(860f, availableWidth);
            width = Mathf.Min(width, Mathf.Max(1f, Verse.UI.screenWidth - (screenMargin * 2f)));
            float x = Mathf.Clamp(
                windowRect.xMax - width - 18f,
                screenMargin,
                Mathf.Max(screenMargin, Verse.UI.screenWidth - width - screenMargin));
            float y = Mathf.Max(
                screenMargin,
                windowRect.yMin - ChooserWindowGap - ChooserWindowHeight);
            return new Rect(x, y, width, ChooserWindowHeight);
        }

        private static WorkTabLayoutColumn? FindChooserSourceColumn(IWorkTabLayoutController layout, WorkTypeDef workType)
        {
            if (layout?.Columns == null || workType == null)
            {
                return null;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!column.IsExpandBesideChild && column.Column?.workType == workType)
                {
                    return column;
                }
            }

            return null;
        }

        private static ChooserComparisonGeometry BuildChooserComparisonGeometry(Rect inRect)
        {
            const float panelGap = 14f;
            Rect panelRect = new Rect(inRect.x, inRect.y, inRect.width, ChooserPanelHeight);
            float choiceWidth = Mathf.Max(160f, (panelRect.width - panelGap) * 0.5f);
            Rect focusRegion = new Rect(panelRect.xMin, panelRect.yMin, choiceWidth, panelRect.height);
            Rect expandRegion = new Rect(focusRegion.xMax + panelGap, panelRect.yMin, choiceWidth, panelRect.height);
            Rect rememberRegion = new Rect(
                panelRect.xMin + 12f,
                panelRect.yMax + 4f,
                Mathf.Max(1f, panelRect.width - 24f),
                ChooserRememberRowHeight);

            return new ChooserComparisonGeometry(
                focusRegion,
                expandRegion,
                rememberRegion);
        }

        private static void DrawChooserChoiceFrame(Rect region, string title, string description)
        {
            Widgets.DrawBoxSolid(region, new Color(0.03f, 0.04f, 0.045f, 1f));
            Widgets.DrawBoxSolid(new Rect(region.xMin, region.yMin, region.width, 2f), new Color(1f, 0.82f, 0.22f, 0.68f));
            Widgets.DrawBoxSolid(new Rect(region.xMin, region.yMax - 2f, region.width, 2f), new Color(1f, 0.82f, 0.22f, 0.38f));
            Widgets.DrawBox(region);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = false;
            GUI.color = Color.white;
            Rect titleRect = new Rect(region.xMin + 14f, region.yMin + 8f, region.width - 28f, 22f);
            Widgets.Label(titleRect, title);

            Text.Font = GameFont.Tiny;
            Text.WordWrap = true;
            GUI.color = new Color(1f, 1f, 1f, 0.72f);
            Widgets.Label(new Rect(region.xMin + 14f, region.yMin + 28f, region.width - 28f, 32f), description);
            Text.WordWrap = false;
            GUI.color = Color.white;
        }

        private readonly struct ChooserComparisonGeometry
        {
            internal ChooserComparisonGeometry(
                Rect focusRegion,
                Rect expandRegion,
                Rect rememberRegion)
            {
                FocusRegion = focusRegion;
                ExpandRegion = expandRegion;
                RememberRegion = rememberRegion;
            }

            internal Rect FocusRegion { get; }
            internal Rect ExpandRegion { get; }
            internal Rect RememberRegion { get; }
        }

        private void UpdateSortState(PawnTable table)
        {
            if (table == null)
            {
                _lastSortColumn = null;
                _lastSortDescending = false;
                return;
            }

            var current = table.SortingBy;
            bool descending = current != null && table.SortingDescending;
            if (!ReferenceEquals(current, _lastSortColumn) || descending != _lastSortDescending)
            {

                _lastSortColumn = current;
                _lastSortDescending = descending;
            }
        }

        private void DrawHeaders(IWorkTabLayoutController layout, PawnTable table)
        {
            float pinnedRowsHeight = GetPinnedRowsHeight();
            float totalHeight = GetVisibleHeaderHighlightHeight(layout, pinnedRowsHeight);
            FluffyWorkTabGateway.PrepareHostedDraw(table);
            float viewportLeft = layout.TableOrigin.x;
            float viewportRight = viewportLeft + GetTableViewportWidth(layout);
            const float HorizontalCullBuffer = 64f;
            bool ruleBuilderListening = RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab;
            bool timePriorityOwnsMouse = TimePriorityScheduleEditor.OwnsCurrentMousePosition;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            bool showCursorHighlight = settings.ShowCursorPawnAndWorktypeHighlight;

            foreach (var column in layout.Columns)
            {
                Rect animatedHeaderRect = GetAnimatedHeaderRect(column);
                if (animatedHeaderRect.xMax < viewportLeft - HorizontalCullBuffer ||
                    animatedHeaderRect.xMin > viewportRight + HorizontalCullBuffer)
                {
                    continue;
                }

                bool isWorkColumn = WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column);
                Rect headerRect = FluffyWorkTabGateway.GetHostedHeaderLaneRect(
                    column.Column,
                    table,
                    animatedHeaderRect);
                bool timePrioritySourceColumn = isWorkColumn && TimePriorityScheduleEditor.ShouldHighlightSourceColumn(column);
                bool shouldHighlightRuleBuilderTarget = false;
                if (ruleBuilderListening && isWorkColumn)
                {
                    ResolveRuleBuilder2Target(column, out WorkTypeDef workType, out WorkGiverDef workGiver);
                    shouldHighlightRuleBuilderTarget =
                        RuleBuilderGateway.ShouldHighlightRuleBuilder2Target(workType, workGiver);
                }
                bool drawRuleBuilderHighlightAfterHeader =
                    shouldHighlightRuleBuilderTarget && AreAngledHeadersEnabled();

                if (showCursorHighlight &&
                    isWorkColumn &&
                    (timePrioritySourceColumn ||
                     (!timePriorityOwnsMouse &&
                      !BWTWorkTabTutorial.OwnsCurrentPointer &&
                      Mouse.IsOver(headerRect))))
                {
                    Color useColor = settings.Color_MouseHoverHighlight;
                    Rect columnRect = new Rect(headerRect.x, layout.TableOrigin.y + layout.HeaderHeight, column.Width, totalHeight);
                    Widgets.DrawBoxSolid(columnRect, useColor);
                }

                if (shouldHighlightRuleBuilderTarget && !drawRuleBuilderHighlightAfterHeader)
                {
                    DrawRuleBuilder2ColumnHighlight(layout, column, headerRect, totalHeight, table);
                }

                if (isWorkColumn)
                {
                    DrawSubWorkBlankTransitionFlash(layout, column, headerRect, totalHeight);
                }

                try
                {
                    SubWorkDrilldownState.SetDrawingColumn(column);
                    if (!TryDrawFluffyHeader(column, headerRect, table))
                    {
                        column.Column.Worker.DoHeader(headerRect, table);
                    }
                }
                finally
                {
                    SubWorkDrilldownState.ClearDrawingColumn();
                }

                if (FluffyWorkTabGateway.WasHostedWorkTypeCollapsed(column.Column))
                {
                    SubWorkDrilldownState.CollapseAllExpandBeside();
                    WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
                }

                if (drawRuleBuilderHighlightAfterHeader)
                {
                    DrawRuleBuilder2ColumnHighlight(layout, column, headerRect, totalHeight, table);
                }
            }
        }

        private float GetVisibleHeaderHighlightHeight(IWorkTabLayoutController layout, float pinnedRowsHeight)
        {
            float bodyTop = layout.TableOrigin.y + layout.HeaderHeight;
            float tableBottomSpace = Mathf.Max(
                0f,
                ExtraBottomSpace - GetInlineTimePriorityReservedHeight(layout));
            float available = Mathf.Max(
                0f,
                windowRect.height - tableBottomSpace - ScrollViewFitAllowance - bodyTop);
            float logicalHeight = pinnedRowsHeight + layout.ContentHeight;
            float clippedHeight = Mathf.Min(logicalHeight, available);
            if (clippedHeight >= logicalHeight - 0.5f)
            {
                return logicalHeight;
            }

            IReadOnlyList<RowDescriptor> rows = layout.GetRowDescriptors();
            if (rows == null || rows.Count == 0)
            {
                return clippedHeight;
            }

            float scrollTop = PawnTableCompat.GetScrollPosition(layout?.Table).y;
            float visibleRowsHeight = Mathf.Max(0f, clippedHeight - pinnedRowsHeight);
            float visibleBottom = scrollTop + visibleRowsHeight;
            float rowBottom = 0f;
            float lastFullyVisibleBottom = scrollTop;
            for (int i = 0; i < rows.Count; i++)
            {
                rowBottom += rows[i].Height;
                if (rowBottom <= visibleBottom + 0.5f)
                {
                    lastFullyVisibleBottom = Mathf.Max(lastFullyVisibleBottom, rowBottom);
                    continue;
                }

                break;
            }

            return Mathf.Min(
                clippedHeight,
                pinnedRowsHeight + Mathf.Max(0f, lastFullyVisibleBottom - scrollTop));
        }

        private static bool TryDrawFluffyHeader(
            WorkTabLayoutColumn column,
            Rect headerRect,
            PawnTable table)
        {
            if (!FluffyWorkTabGateway.IsFluffyColumn(column.Column))
            {
                return false;
            }

            WorkTypeDef parentWorkType = column.SubWorkParent ?? column.Column?.workType;
            WorkGiverDef workGiver = column.SubWorkGiver ?? FluffyWorkTabGateway.TryGetFluffyWorkGiver(column.Column);
            if (parentWorkType == null)
            {
                return true;
            }

            bool isChild = workGiver != null &&
                (column.IsExpandBesideChild || FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column));
            WorkGiverHeaderLabelStyle labelStyle = BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders
                ? WorkGiverHeaderLabelStyle.Standard
                : WorkGiverHeaderLabelStyle.VanillaStaggered;

            string label = isChild
                ? WorkGiverDisplayNameService.HeaderLabel(workGiver, labelStyle)
                : WorkTypeDisplayNameService.HeaderLabel(parentWorkType);

            if (FluffyWorkTabGateway.IsHostedFluffyColumn(column.Column) &&
                ShouldSuppressHostedChildLabel(parentWorkType, workGiver, label, labelStyle))
            {
                if (headerRect.Contains(HeaderInputController.MousePosition))
                {
                    TooltipHandler.TipRegion(headerRect, WorkGiverDisplayNameService.FullLabel(workGiver));
                }
                return true;
            }

            bool isMouseOver = !TimePriorityScheduleEditor.OwnsCurrentMousePosition && headerRect.Contains(HeaderInputController.MousePosition);
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(parentWorkType, headerRect);
            }

            if (BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders)
            {
                DrawHostedAngledHeader(column, headerRect, parentWorkType, label, isMouseOver, table);
            }
            else
            {
                DrawHostedVanillaHeader(column, headerRect, parentWorkType, label, isMouseOver, table);
            }

            if (isMouseOver)
            {
                string tip = isChild
                    ? WorkGiverDisplayNameService.FullLabel(workGiver)
                    : WorkTypeDisplayNameService.FullLabel(parentWorkType);
                TooltipHandler.TipRegion(headerRect, tip);
            }
            return true;
        }

        private static bool ShouldSuppressHostedChildLabel(
            WorkTypeDef parentWorkType,
            WorkGiverDef workGiver,
            string childLabel,
            WorkGiverHeaderLabelStyle labelStyle)
        {
            if (parentWorkType == null || workGiver == null || childLabel.NullOrEmpty())
            {
                return false;
            }

            var workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(parentWorkType);
            if (workGivers == null || workGivers.Count != 1)
            {
                return false;
            }

            string parentLabel = WorkTypeDisplayNameService.HeaderLabel(parentWorkType);
            return string.Equals(childLabel, parentLabel, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       WorkGiverDisplayNameService.FullLabel(workGiver),
                       WorkTypeDisplayNameService.FullLabel(parentWorkType),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void DrawHostedAngledHeader(
            WorkTabLayoutColumn column,
            Rect headerRect,
            WorkTypeDef parentWorkType,
            string label,
            bool isMouseOver,
            PawnTable table)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            AngledHeaderCache.CachedTextMetrics textMetrics =
                AngledHeaderCache.GetLabelTextMetrics(label);
            bool isCJKVertical = textMetrics.IsCJKVertical;
            Vector2 size = textMetrics.Size;

            Rect drawRect = GetHostedAngledHeaderDrawRect(column, headerRect, size, isCJKVertical, table);

            var labelLayout = new AngledLabelDrawer.AngledLabelLayout(
                label,
                size,
                drawRect.center,
                MainTabWindow_BetterWork.ShouldShowColumnMarker(parentWorkType),
                isCJKVertical,
                drawRect)
                .WithAlpha(GetHostedSubWorkHeaderAlpha(column));

            bool isSorted = table != null && table.SortingBy == column.Column;
            bool sortDescending = table != null && table.SortingDescending;
            HeaderDrawingCoordinator.GetActiveRenderer().DrawHeader(
                labelLayout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column.Column,
                labelLayout.ShowMarker);
        }

        internal static Rect GetHostedAngledHeaderDrawRect(
            WorkTabLayoutColumn column,
            Rect headerRect,
            Vector2 labelSize,
            bool isCJKVertical,
            PawnTable table)
        {
            Rect drawRect;
            if (isCJKVertical)
            {
                drawRect = new Rect(
                    headerRect.center.x - (labelSize.x / 2f) + AngledLabelDrawer.EffectiveHorizontalOffset,
                    headerRect.yMax - labelSize.y - AngledLabelDrawer.STEM_BOTTOM_GAP,
                    labelSize.x,
                    labelSize.y);
            }
            else
            {
                float stableDrawWidth = SubWorkDrilldownState.HasAnyDrilldown
                    ? SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(table, headerRect.height)
                    : headerRect.height;
                drawRect = new Rect(0f, 0f, Mathf.Max(stableDrawWidth, labelSize.x), labelSize.y)
                {
                    center = headerRect.center
                };
                drawRect.x += AngledLabelDrawer.EffectiveHorizontalOffset;
                drawRect.position += SubWorkDrilldownHeaderGeometry.GetExpandBesideAngledAnchorOffset(
                    table,
                    headerRect.height,
                    drawRect.width,
                    drawRect.height,
                    AngledLabelDrawer.CurrentRotation);
            }

            if (column.IsExpandBesideChild && column.SubWorkSlot == 0)
            {
                drawRect.x += HostedFirstSubWorkAngledHeaderOffsetX;
            }

            return drawRect;
        }

        private static void DrawHostedVanillaHeader(
            WorkTabLayoutColumn column,
            Rect headerRect,
            WorkTypeDef parentWorkType,
            string label,
            bool isMouseOver,
            PawnTable table)
        {
            var solver = HeaderDrawingCoordinator.GetVanillaSolver();
            bool isMoved = MainTabWindow_BetterWork.ShouldShowColumnMarker(parentWorkType);
            if (Event.current.type == EventType.Layout)
            {
                solver?.CollectHeader(column.Column, headerRect, parentWorkType, isMoved);
            }

            HeaderDrawingCoordinator.EnsureLayoutSolved(table);

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Rect bounds = solver?.GetBounds(column.Column) ?? Rect.zero;
            if (bounds.width <= 0.5f || bounds.height <= 0.5f)
            {
                bounds = headerRect;
            }

            var labelLayout = new AngledLabelDrawer.AngledLabelLayout(
                label,
                bounds.size,
                bounds.center,
                isMoved)
                .WithAlpha(GetHostedSubWorkHeaderAlpha(column));

            bool isSorted = table != null && table.SortingBy == column.Column;
            bool sortDescending = table != null && table.SortingDescending;
            HeaderDrawingCoordinator.GetActiveRenderer().DrawHeader(
                labelLayout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column.Column,
                labelLayout.ShowMarker);
        }

        private static float GetHostedSubWorkHeaderAlpha(WorkTabLayoutColumn column)
        {
            if (!column.IsExpandBesideChild)
            {
                return 1f;
            }

            return SubWorkDrilldownState.GetExpandBesideHeaderAlpha(column.SubWorkParent);
        }

        private static WorkTypeDef ResolveRuleBuilder2WorkType(WorkTabLayoutColumn column)
        {
            ResolveRuleBuilder2Target(column, out WorkTypeDef workType, out _);
            return workType;
        }

        private static void ResolveRuleBuilder2Target(
            WorkTabLayoutColumn column,
            out WorkTypeDef workType,
            out WorkGiverDef workGiver)
        {
            if (column.IsExpandBesideChild)
            {
                workGiver = column.SubWorkGiver;
                workType = workGiver?.workType ?? column.SubWorkParent ?? column.Column?.workType;
                return;
            }

            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    column,
                    out var resolvedWorkGiver,
                    out var parentWorkType,
                    out _))
            {
                workGiver = resolvedWorkGiver.def;
                workType = workGiver?.workType ?? parentWorkType;
                return;
            }

            workType = column.Column?.workType;
            workGiver = null;
        }

        private static bool AreAngledHeadersEnabled()
        {
            return BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders;
        }

        private static Rect GetAnimatedHeaderRect(WorkTabLayoutColumn column)
        {
            float offset = ColumnReorderAnimationState.GetHeaderOffset(column);
            return Mathf.Abs(offset) > 0.01f
                ? new Rect(column.HeaderRect.x + offset, column.HeaderRect.y, column.HeaderRect.width, column.HeaderRect.height)
                : column.HeaderRect;
        }

        private static void DrawRuleBuilder2ColumnHighlight(
            IWorkTabLayoutController layout,
            WorkTabLayoutColumn column,
            Rect headerRect,
            float totalHeight,
            PawnTable table)
        {
            ResolveRuleBuilder2Target(column, out WorkTypeDef workType, out WorkGiverDef workGiver);
            if (RuleBuilderGateway.TryGetRuleBuilder2SelectionTransitionOffset(
                    workType,
                    workGiver,
                    out Vector2 transitionOffset))
            {
                headerRect.position += transitionOffset;
            }

            Rect bodyRect = GetRuleBuilder2ColumnBodyHighlightRect(layout, column, headerRect, totalHeight);
            bool hasHeaderHighlight = TryGetRuleBuilder2HeaderHighlight(column, headerRect, table, out var headerHighlight);
            if (hasHeaderHighlight && headerHighlight.IsAngled)
            {
                Vector2[] quad = headerHighlight.VisibleQuad ?? headerHighlight.Quad;
                if (quad != null && quad.Length >= 4)
                {
                    if (IsUsableRect(bodyRect))
                    {
                        Widgets.DrawBoxSolid(bodyRect, new Color(1f, 0.82f, 0.18f, 0.12f));
                    }

                    Color outline = new Color(1f, 0.82f, 0.18f, 0.62f);
                    Vector2 bodyBottomLeft = new Vector2(bodyRect.xMin, bodyRect.yMax);
                    Vector2 bodyTopLeft = new Vector2(bodyRect.xMin, bodyRect.yMin);
                    Vector2 bodyTopRight = new Vector2(bodyRect.xMax, bodyRect.yMin);
                    Vector2 bodyBottomRight = new Vector2(bodyRect.xMax, bodyRect.yMax);
                    Vector2 columnJoinLeft = bodyTopLeft;
                    Vector2 columnJoinRight = bodyTopRight;
                    float leftCapY = (quad[0].y + quad[3].y) / 2f;
                    float rightCapY = (quad[1].y + quad[2].y) / 2f;
                    Vector2 headerTopLeft = leftCapY <= rightCapY ? quad[0] : quad[1];
                    Vector2 headerTopRight = leftCapY <= rightCapY ? quad[3] : quad[2];
                    if (headerTopRight.x < headerTopLeft.x)
                    {
                        Vector2 swap = headerTopLeft;
                        headerTopLeft = headerTopRight;
                        headerTopRight = swap;
                    }

                    Vector2 headerRun = quad[1] - quad[0];
                    if (headerRun.x < 0f)
                    {
                        headerRun *= -1f;
                    }

                    if (Mathf.Abs(headerRun.x) > 0.001f)
                    {
                        float leftT = (bodyRect.xMin - headerTopLeft.x) / headerRun.x;
                        float rightT = (bodyRect.xMax - headerTopRight.x) / headerRun.x;
                        columnJoinLeft = new Vector2(
                            bodyRect.xMin,
                            Mathf.Min(bodyRect.yMin, headerTopLeft.y + (headerRun.y * leftT)));
                        columnJoinRight = new Vector2(
                            bodyRect.xMax,
                            Mathf.Min(bodyRect.yMin, headerTopRight.y + (headerRun.y * rightT)));
                    }

                    const float rightConnectorDrop = 4f;
                    Vector2 topEdge = headerTopRight - headerTopLeft;
                    Vector2 connectorDirection = headerRun;
                    if ((bodyRect.xMax - headerTopRight.x) * connectorDirection.x < 0f)
                    {
                        connectorDirection *= -1f;
                    }

                    float intersectionDenominator = (topEdge.x * connectorDirection.y) - (topEdge.y * connectorDirection.x);
                    if (Mathf.Abs(intersectionDenominator) > 0.001f &&
                        Mathf.Abs(connectorDirection.x) > 0.001f)
                    {
                        Vector2 shiftedConnectorOrigin = headerTopRight + new Vector2(0f, rightConnectorDrop);
                        Vector2 originDelta = shiftedConnectorOrigin - headerTopLeft;
                        float topEdgeT = ((originDelta.x * connectorDirection.y) - (originDelta.y * connectorDirection.x)) /
                            intersectionDenominator;
                        if (topEdgeT >= 1f && topEdgeT <= 1.5f)
                        {
                            headerTopRight = headerTopLeft + (topEdge * topEdgeT);
                            float connectorT = (bodyRect.xMax - headerTopRight.x) / connectorDirection.x;
                            if (connectorT > 0f)
                            {
                                Vector2 loweredColumnJoinRight = headerTopRight + (connectorDirection * connectorT);
                                if (loweredColumnJoinRight.y > bodyRect.yMin && loweredColumnJoinRight.y < bodyRect.yMax)
                                {
                                    columnJoinRight = loweredColumnJoinRight;
                                }
                            }
                        }
                    }

                    DrawRuleBuilder2ClosedOutline(
                        new[]
                        {
                            bodyBottomLeft,
                            columnJoinLeft,
                            headerTopLeft,
                            headerTopRight,
                            columnJoinRight,
                            bodyBottomRight
                        },
                        outline,
                        2f);
                    return;
                }
            }

            DrawRuleBuilder2HighlightRect(bodyRect);
            if (hasHeaderHighlight)
            {
                DrawRuleBuilder2HighlightRect(headerHighlight.Bounds);
            }
        }

        private static Rect GetRuleBuilder2ColumnBodyHighlightRect(
            IWorkTabLayoutController layout,
            WorkTabLayoutColumn column,
            Rect headerRect,
            float totalHeight)
        {
            return new Rect(
                headerRect.x,
                layout.TableOrigin.y + layout.HeaderHeight,
                column.Width,
                totalHeight);
        }

        private static void DrawRuleBuilder2HighlightRect(Rect rect)
        {
            if (!IsUsableRect(rect))
            {
                return;
            }

            Widgets.DrawBoxSolid(rect, new Color(1f, 0.82f, 0.18f, 0.12f));
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.18f, 0.55f);
            Widgets.DrawBox(rect, 2);
            GUI.color = previousColor;
        }

        private static Material RuleBuilder2OutlineMaterial
        {
            get
            {
                if (_ruleBuilder2OutlineMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored") ?? ShaderDatabase.Transparent;
                    _ruleBuilder2OutlineMaterial = new Material(shader)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    _ruleBuilder2OutlineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _ruleBuilder2OutlineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _ruleBuilder2OutlineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    _ruleBuilder2OutlineMaterial.SetInt("_ZWrite", 0);
                }

                return _ruleBuilder2OutlineMaterial;
            }
        }

        private static void DrawRuleBuilder2ClosedOutline(Vector2[] points, Color color, float width)
        {
            if (Event.current.type != EventType.Repaint || points == null || points.Length < 3 || width <= 0f)
            {
                return;
            }

            float halfWidth = width * 0.5f;
            int count = points.Length;
            var leftOffsets = new Vector2[count];
            var rightOffsets = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                Vector2 current = points[i];
                Vector2 previous = points[(i - 1 + count) % count];
                Vector2 next = points[(i + 1) % count];
                Vector2 incoming = current - previous;
                Vector2 outgoing = next - current;

                if (incoming.sqrMagnitude <= 0.001f || outgoing.sqrMagnitude <= 0.001f)
                {
                    Vector2 fallback = outgoing.sqrMagnitude > 0.001f ? outgoing : incoming;
                    Vector2 normal = fallback.sqrMagnitude > 0.001f
                        ? new Vector2(-fallback.y, fallback.x).normalized
                        : Vector2.up;
                    leftOffsets[i] = current + (normal * halfWidth);
                    rightOffsets[i] = current - (normal * halfWidth);
                    continue;
                }

                incoming.Normalize();
                outgoing.Normalize();
                Vector2 incomingNormal = new Vector2(-incoming.y, incoming.x);
                Vector2 outgoingNormal = new Vector2(-outgoing.y, outgoing.x);
                Vector2 miter = incomingNormal + outgoingNormal;
                if (miter.sqrMagnitude <= 0.001f)
                {
                    miter = outgoingNormal;
                }
                else
                {
                    miter.Normalize();
                }

                float denominator = Vector2.Dot(miter, outgoingNormal);
                float miterLength = Mathf.Abs(denominator) > 0.15f
                    ? halfWidth / denominator
                    : halfWidth;
                miterLength = Mathf.Clamp(miterLength, -halfWidth * 4f, halfWidth * 4f);
                leftOffsets[i] = current + (miter * miterLength);
                rightOffsets[i] = current - (miter * miterLength);
            }

            Material material = RuleBuilder2OutlineMaterial;
            if (material == null || !material.SetPass(0))
            {
                return;
            }

            GL.PushMatrix();
            try
            {
                GL.MultMatrix(GUI.matrix);
                GL.Begin(GL.TRIANGLES);
                GL.Color(color);
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    GL.Vertex3(leftOffsets[i].x, leftOffsets[i].y, 0f);
                    GL.Vertex3(leftOffsets[next].x, leftOffsets[next].y, 0f);
                    GL.Vertex3(rightOffsets[next].x, rightOffsets[next].y, 0f);

                    GL.Vertex3(leftOffsets[i].x, leftOffsets[i].y, 0f);
                    GL.Vertex3(rightOffsets[next].x, rightOffsets[next].y, 0f);
                    GL.Vertex3(rightOffsets[i].x, rightOffsets[i].y, 0f);
                }

                GL.End();
            }
            finally
            {
                GL.PopMatrix();
            }
        }

        private static bool TryGetRuleBuilder2HeaderHighlight(
            WorkTabLayoutColumn column,
            Rect headerRect,
            PawnTable table,
            out RuleBuilder2HeaderHighlight headerHighlight)
        {
            headerHighlight = default;
            PawnColumnDef columnDef = column.Column;
            if (columnDef?.workType == null)
            {
                return false;
            }

            if (AreAngledHeadersEnabled())
            {
                float rotation = AngledLabelDrawer.CurrentRotation;
                float cos = Mathf.Cos(rotation * Mathf.Deg2Rad);
                float sin = Mathf.Sin(rotation * Mathf.Deg2Rad);
                if (AngledHeaderCache.TryGetLayout(
                        headerRect,
                        columnDef.workType,
                        cos,
                        sin,
                        AngledLabelDrawer.STEM_BOTTOM_GAP,
                        AngledLabelDrawer.EffectiveHorizontalOffset,
                        out var cached) &&
                    IsUsableRect(cached.Bounds))
                {
                    headerHighlight = RuleBuilder2HeaderHighlight.Angled(
                        cached.Bounds,
                        cached.Quad,
                        cached.Quad);
                    return true;
                }

                return false;
            }

            HeaderDrawingCoordinator.EnsureLayoutSolved(table);
            Rect vanillaBounds = HeaderDrawingCoordinator.GetVanillaSolver()?.GetBounds(columnDef) ?? Rect.zero;
            if (!IsUsableRect(vanillaBounds))
            {
                return false;
            }

            vanillaBounds.x += headerRect.x - column.HeaderRect.x;

            headerHighlight = RuleBuilder2HeaderHighlight.Rectangular(vanillaBounds);
            return true;
        }

        private readonly struct RuleBuilder2HeaderHighlight
        {
            private RuleBuilder2HeaderHighlight(Rect bounds, Vector2[] quad, Vector2[] visibleQuad, bool isAngled)
            {
                Bounds = bounds;
                Quad = quad;
                VisibleQuad = visibleQuad;
                IsAngled = isAngled;
            }

            internal Rect Bounds { get; }
            internal Vector2[] Quad { get; }
            internal Vector2[] VisibleQuad { get; }
            internal bool IsAngled { get; }

            internal static RuleBuilder2HeaderHighlight Angled(Rect bounds, Vector2[] quad, Vector2[] visibleQuad)
            {
                return new RuleBuilder2HeaderHighlight(bounds, quad, visibleQuad, true);
            }

            internal static RuleBuilder2HeaderHighlight Rectangular(Rect bounds)
            {
                return new RuleBuilder2HeaderHighlight(bounds, null, null, false);
            }

            internal bool Contains(Vector2 point)
            {
                return IsAngled && Quad != null
                    ? AngledHeaderCache.IsMouseOver(Quad, point)
                    : Bounds.Contains(point);
            }
        }

        private static bool IsUsableRect(Rect rect)
        {
            return rect.width > 0f && rect.height > 0f;
        }

        private static float GetPinnedRowsHeight()
        {
            float height = TimePriorityScheduleEditor.HeaderPinnedRowsHeight;
            if (SubWorkDrilldownState.HasAnyDrilldown)
            {
                height += SubWorkDrilldownBarRenderer.ReservedRowHeight;
            }

            return height;
        }

        private static float GetHeaderAnchoredPinnedRowsHeight()
        {
            float height = SubWorkDrilldownState.HasAnyDrilldown
                ? SubWorkDrilldownBarRenderer.ReservedRowHeight
                : 0f;
            float timePriorityHeight = TimePriorityScheduleEditor.HeaderPinnedRowsHeight;
            return height + Mathf.Max(0f, timePriorityHeight - InlineScheduleFooterReclaim);
        }

        private static float GetHeaderAnchoredContentHeight(IWorkTabLayoutController layout)
        {
            float transientHeight = GetTransientTimePriorityRowHeight(layout);
            float reclaimedHeight = Mathf.Min(transientHeight, InlineScheduleFooterReclaim);
            return Mathf.Max(0f, (layout?.ContentHeight ?? 0f) - reclaimedHeight);
        }

        private static float GetTransientTimePriorityRowHeight(IWorkTabLayoutController layout)
        {
            IReadOnlyList<WorkTabLayoutRow> rows = layout?.Rows;
            if (rows == null)
            {
                return 0f;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                WorkTabLayoutRow row = rows[i];
                if (row.Divider != null && TimePriorityScheduleEditor.IsTransientDivider(row.Divider))
                {
                    return row.Height;
                }
            }

            return 0f;
        }

        private static float GetInlineTimePriorityReservedHeight(IWorkTabLayoutController layout)
        {
            float reservedHeight = TimePriorityScheduleEditor.HeaderPinnedRowsHeight +
                GetTransientTimePriorityRowHeight(layout);
            return Mathf.Min(reservedHeight, InlineScheduleFooterReclaim);
        }


        private static float GetVisualTableScrollWidth(IWorkTabLayoutController layout, PawnTable table)
        {
            float tableWidth = table != null ? Mathf.Max(table.Size.x, PawnTableCompat.GetCachedSize(table).x) : 0f;
            float visualColumnWidth = GetVisualColumnWidth(layout);
            if (visualColumnWidth <= 0f)
            {
                return tableWidth;
            }

            return Mathf.Ceil(visualColumnWidth + 16f);
        }

        private static float GetTableViewportWidth(IWorkTabLayoutController layout)
        {
            if (layout?.Table == null)
            {
                return 0f;
            }

            float available = Mathf.Max(1f, Verse.UI.screenWidth - layout.TableOrigin.x - 2f);
            return Mathf.Min(GetVisualTableScrollWidth(layout, layout.Table), available);
        }

        private static float GetVisualColumnWidth(IWorkTabLayoutController layout)
        {
            if (layout?.Columns == null || layout.Columns.Count == 0)
            {
                return 0f;
            }

            float max = 0f;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                max = Mathf.Max(max, column.OffsetX + column.Width);
            }

            return Mathf.Ceil(max);
        }

        private static void DrawSubWorkBlankTransitionFlash(IWorkTabLayoutController layout, WorkTabLayoutColumn column, Rect headerRect, float totalHeight)
        {
            float alpha = SubWorkDrilldownState.GetBlankColumnFlashAlpha(column.Column);
            if (alpha <= 0.001f)
            {
                return;
            }

            Rect rect = new Rect(
                headerRect.x,
                layout.TableOrigin.y,
                column.Width,
                layout.HeaderHeight + totalHeight);
            Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, alpha));
            Widgets.DrawBoxSolid(
                new Rect(rect.center.x - 0.5f, rect.yMin, 1f, rect.height),
                new Color(1f, 1f, 1f, alpha * 0.65f));
        }

        private static void DrawSubWorkTransitionPixelWave(IWorkTabLayoutController layout)
        {
            if (layout?.Columns == null ||
                !SubWorkDrilldownState.TryGetTransitionWave(out float pivotSlot, out float phase) ||
                !TryGetSubWorkWaveGeometry(layout, pivotSlot, out Rect workBounds, out float pivotX))
            {
                return;
            }

            Rect waveRect = new Rect(
                workBounds.xMin,
                layout.TableOrigin.y,
                workBounds.width,
                layout.HeaderHeight + GetPinnedRowsHeight() + layout.ContentHeight);
            if (waveRect.width <= 1f || waveRect.height <= 1f)
            {
                return;
            }

            const float leadingWidth = 10f;
            const float trailWidth = 64f;
            const float leadingAlpha = 0.24f;
            const float trailAlpha = 0.09f;

            float maxDistance = Mathf.Max(pivotX - waveRect.xMin, waveRect.xMax - pivotX);
            float waveCenter = Mathf.Clamp01(phase) * (maxDistance + trailWidth + leadingWidth);
            float overrun = Mathf.Max(0f, waveCenter - maxDistance);
            float fadeOut = 1f - SmoothStep01(overrun / trailWidth);
            if (fadeOut <= 0.001f)
            {
                return;
            }

            Color oldColor = GUI.color;
            try
            {
                float startX = Mathf.Floor(waveRect.xMin);
                float endX = Mathf.Ceil(waveRect.xMax);
                const float stripWidth = 4f;
                for (float x = startX; x < endX; x += stripWidth)
                {
                    float width = Mathf.Min(stripWidth, endX - x);
                    float sampleX = x + width / 2f;
                    float distance = Mathf.Abs(sampleX - pivotX);
                    float leading = SmoothStep01(1f - Mathf.Abs(distance - waveCenter) / leadingWidth);

                    float behind = waveCenter - distance;
                    float trail = behind > 0f
                        ? Mathf.Pow(Mathf.Clamp01(1f - behind / trailWidth), 1.6f)
                        : 0f;

                    float alpha = fadeOut * ((leading * leadingAlpha) + ((1f - leading) * trail * trailAlpha));
                    if (alpha <= 0.002f)
                    {
                        continue;
                    }

                    float grey = Mathf.Lerp(0.52f, 0.82f, leading);
                    Widgets.DrawBoxSolid(
                        new Rect(x, waveRect.yMin, width, waveRect.height),
                        new Color(grey, grey, grey, alpha));
                }
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static bool TryGetSubWorkWaveGeometry(
            IWorkTabLayoutController layout,
            float pivotSlot,
            out Rect workBounds,
            out float pivotX)
        {
            workBounds = Rect.zero;
            pivotX = 0f;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float nearestDistance = float.MaxValue;
            bool foundPivot = false;

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                int slot = SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column);
                if (slot < 0)
                {
                    continue;
                }

                Rect headerRect = GetAnimatedHeaderRect(column);
                minX = Mathf.Min(minX, headerRect.xMin);
                maxX = Mathf.Max(maxX, headerRect.xMax);

                float slotDistance = Mathf.Abs(pivotSlot - slot);
                if (slotDistance < nearestDistance)
                {
                    nearestDistance = slotDistance;
                    float local = Mathf.Clamp01(pivotSlot - slot + 0.5f);
                    pivotX = Mathf.Lerp(headerRect.xMin, headerRect.xMax, local);
                    foundPivot = true;
                }
            }

            if (!foundPivot || minX >= maxX)
            {
                return false;
            }

            workBounds = new Rect(minX, layout.TableOrigin.y, maxX - minX, 1f);
            return true;
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private bool TryHandleContextSettingsClick(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            if (evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0 ||
                !evt.alt ||
                !inRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (!BWTWorkTabContextSettingsRouter.TryBuildFocusRequest(
                    inRect,
                    layout,
                    evt.mousePosition,
                    evt.shift,
                    evt.control,
                    out BWTSettingsFocusRequest request))
            {
                return false;
            }

            BWTSettingsContextFocus.Request(request);
            bool opened = OpenBetterWorkTabSettings(toggleExisting: false);
            if (opened)
            {
                HideContextSettingsHintAfterFirstUse();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            evt.Use();
            return true;
        }

        private void ReportTutorialInteraction(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            if (evt == null || evt.type != EventType.MouseDown)
            {
                return;
            }

            BWTTutorialInteractionKind kind = ClassifyTutorialInteraction(inRect, layout, evt.mousePosition);
            if (kind == BWTTutorialInteractionKind.None)
            {
                return;
            }

            BWTWorkTabTutorial.ObserveInteraction(new BWTTutorialInteraction(
                kind,
                evt.mousePosition,
                evt.button,
                evt.control,
                evt.shift,
                evt.alt));
        }

        private BWTTutorialInteractionKind ClassifyTutorialInteraction(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 mousePosition)
        {
            if (!inRect.Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.OutsideWorkTab;
            }

            if (GetInfoIconRect(inRect).Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.InfoButton;
            }

            Rect infoRect = GetInfoIconRect(inRect);
            HeaderButtons.BottomButtonRects buttons = HeaderButtons.GetBottomButtonRects(inRect, infoRect);
            if (buttons.ContainsWorkload(mousePosition))
            {
                return BWTTutorialInteractionKind.WorkloadButton;
            }

            if (buttons.ContainsRuleset(mousePosition))
            {
                return BWTTutorialInteractionKind.RulesetButton;
            }

            if (new Rect(5f, 5f, 220f, 62f).ExpandedBy(4f).Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.ManualPriorities;
            }

            if (new Rect(inRect.xMax - 300f, inRect.y + 2f, 260f, 28f).Contains(mousePosition))
            {
                return BWTTutorialInteractionKind.ContextSettingsHint;
            }

            if (TimePriorityScheduleEditor.OwnsMousePosition(mousePosition))
            {
                return BWTTutorialInteractionKind.TimePriorityCell;
            }

            if (layout?.Rows != null && TryGetRowAt(layout, mousePosition, out var row))
            {
                if (row.Divider != null)
                {
                    return BWTTutorialInteractionKind.Divider;
                }

                if (row.Pawn != null && TryGetBodyColumnAt(layout, mousePosition, out var bodyColumn))
                {
                    if (bodyColumn.Column?.Worker is PawnColumnWorker_WorkPriority)
                    {
                        return BWTTutorialInteractionKind.PriorityCell;
                    }
                }

                if (row.Pawn != null)
                {
                    return BWTTutorialInteractionKind.PawnRow;
                }
            }

            if (layout?.Columns != null)
            {
                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    WorkTabLayoutColumn column = layout.Columns[i];
                    if (!GetAnimatedHeaderRect(column).Contains(mousePosition))
                    {
                        continue;
                    }

                    if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                    {
                        return BWTTutorialInteractionKind.None;
                    }

                    return SubWorkDrilldownState.IsActive
                        ? BWTTutorialInteractionKind.SubWorkHeader
                        : BWTTutorialInteractionKind.WorkHeader;
                }
            }

            return BWTTutorialInteractionKind.None;
        }

        private bool TryHandleRuleBuilder2WorkTabInput(IWorkTabLayoutController layout, Event evt)
        {
            if (SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return false;
            }

            if (!RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab ||
                layout == null ||
                evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0)
            {
                return false;
            }

            if (TryGetRowAt(layout, evt.mousePosition, out var row) &&
                row.Pawn != null &&
                TryGetBodyColumnAt(layout, evt.mousePosition, out var bodyColumn) &&
                bodyColumn.Column?.Worker is PawnColumnWorker_WorkPriority &&
                TryGetPriorityBoxHit(layout, row, bodyColumn, evt.mousePosition, out Rect priorityBoxRect))
            {
                WorkTypeDef workType = ResolveRuleBuilder2WorkType(bodyColumn);
                WorkGiverDef workGiver = null;
                int priority = WorkPrioritySystem.GetPriorityForPawnWorkType(row.Pawn, workType);
                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(bodyColumn, out var activeWorkGiver, out _, out _))
                {
                    workGiver = activeWorkGiver.def;
                    priority = WorkGiverReassignmentManager.GetWorkGiverPriority(row.Pawn, workGiver, priority);
                }

                WorkPriorityCommandGateway.Execute(new SelectRuleTargetCommand(
                    workType,
                    workGiver,
                    row.Pawn,
                    priority,
                    priorityBoxRect,
                    header: false));
                evt.Use();
                return true;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                Rect headerRect = FluffyWorkTabGateway.GetHostedHeaderLaneRect(
                    column.Column,
                    layout.Table,
                    GetAnimatedHeaderRect(column));
                if (!TryGetRuleBuilder2HeaderHighlight(column, headerRect, layout.Table, out var headerHighlight) ||
                    !headerHighlight.Contains(evt.mousePosition) ||
                    !(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    column.Column.workType == null)
                {
                    continue;
                }

                WorkTypeDef workType = ResolveRuleBuilder2WorkType(column);
                WorkGiverDef workGiver = null;
                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(column, out var activeWorkGiver, out _, out _))
                {
                    workGiver = activeWorkGiver.def;
                }

                WorkPriorityCommandGateway.Execute(new SelectRuleTargetCommand(
                    workType,
                    workGiver,
                    pawn: null,
                    priority: WorkPrioritySystem.DisabledPriority,
                    headerHighlight.Bounds,
                    header: true));
                evt.Use();
                return true;
            }

            return false;
        }

        private void UpdateRuleBuilder2WorkTabHover(IWorkTabLayoutController layout, Rect inRect)
        {
            if (!RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab ||
                layout == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                !Mouse.IsOver(inRect) ||
                BWTWorkTabTutorial.OwnsCurrentPointer ||
                RuleBuilderGateway.RuleBuilder2BlocksWorkTabHover())
            {
                RuleBuilderGateway.ClearRuleBuilder2WorkTabPreview();
                return;
            }

            Vector2 mousePosition = Event.current.mousePosition;
            if (TryGetRowAt(layout, mousePosition, out var row) &&
                row.Pawn != null &&
                TryGetBodyColumnAt(layout, mousePosition, out var bodyColumn) &&
                bodyColumn.Column?.Worker is PawnColumnWorker_WorkPriority &&
                TryGetPriorityBoxHit(layout, row, bodyColumn, mousePosition, out Rect priorityBoxRect))
            {
                WorkTypeDef workType = ResolveRuleBuilder2WorkType(bodyColumn);
                WorkGiverDef workGiver = null;
                int priority = WorkPrioritySystem.GetPriorityForPawnWorkType(row.Pawn, workType);
                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(bodyColumn, out var activeWorkGiver, out _, out _))
                {
                    workGiver = activeWorkGiver.def;
                    priority = WorkGiverReassignmentManager.GetWorkGiverPriority(row.Pawn, workGiver, priority);
                }

                RuleBuilderGateway.PreviewPriorityCellForRuleBuilder2(workType, workGiver, row.Pawn, priority, priorityBoxRect);
                return;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                Rect headerRect = FluffyWorkTabGateway.GetHostedHeaderLaneRect(
                    column.Column,
                    layout.Table,
                    GetAnimatedHeaderRect(column));
                if (!TryGetRuleBuilder2HeaderHighlight(column, headerRect, layout.Table, out var headerHighlight) ||
                    !headerHighlight.Contains(mousePosition) ||
                    !(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    column.Column.workType == null)
                {
                    continue;
                }

                WorkTypeDef workType = ResolveRuleBuilder2WorkType(column);
                WorkGiverDef workGiver = null;
                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(column, out var activeWorkGiver, out _, out _))
                {
                    workGiver = activeWorkGiver.def;
                }

                RuleBuilderGateway.PreviewHeaderForRuleBuilder2(workType, workGiver, headerHighlight.Bounds);
                return;
            }

            RuleBuilderGateway.ClearRuleBuilder2WorkTabPreview();
        }

        private bool TryHandleSubWorkHeaderOpen(IWorkTabLayoutController layout)
        {
            if (layout == null || SubWorkDrilldownState.IsActive)
            {
                return false;
            }

            // Ctrl is shared by header reordering and sub-work gestures. Once a real column
            // drag has crossed its threshold it owns the gesture, even if the pointer later
            // returns inside the original header before MouseUp.
            if (PawnOrganizerSystem.Instance?.IsDraggingColumn == true)
            {
                ClearPendingSubWorkGesture();
                return false;
            }

            Event evt = Event.current;
            if (evt == null)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown)
            {
                if (!SubWorkDrilldownInput.MatchesGesture(evt))
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                if (!TryGetSubWorkOpenTarget(layout, evt.mousePosition, out var workType, out var bounds, out bool fromHeader))
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                Vector2? returnMousePosition = fromHeader
                    ? GuiMousePosition.ToRootUiPosition(evt.mousePosition)
                    : (Vector2?)null;

                BeginPendingSubWorkGesture(
                    start: evt.mousePosition,
                    bounds: bounds,
                    button: evt.button,
                    openType: workType,
                    exit: false,
                    restoreCursor: returnMousePosition.HasValue);
                MarkSubWorkPriorityMouseDownForSuppression();
                return false;
            }

            if (!_pendingSubWorkGesture || _pendingSubWorkExit)
            {
                return false;
            }

            if (evt.type == EventType.MouseDrag)
            {
                CancelPendingSubWorkIfDragged(evt.mousePosition);
                return false;
            }

            if (evt.type != EventType.MouseUp || evt.button != _pendingSubWorkButton)
            {
                return false;
            }

            bool shouldOpen = IsPendingSubWorkClick(evt.mousePosition) &&
                _pendingSubWorkOpenType != null &&
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(_pendingSubWorkOpenType).Count > 0;

            Vector2? storedReturnPosition = _pendingSubWorkRestoreCursor
                ? GuiMousePosition.ToRootUiPosition(_pendingSubWorkStart)
                : (Vector2?)null;
            WorkTypeDef openType = _pendingSubWorkOpenType;
            Rect openBounds = _pendingSubWorkBounds;
            ClearPendingSubWorkGesture();

            if (!shouldOpen)
            {
                return false;
            }

            if (FluffyWorkTabGateway.TryStartSubWorkDrilldownStyleChooser(layout, openType, openBounds))
            {
                evt.Use();
                return true;
            }

            TimePriorityScheduleEditor.CloseForWorkModeTransition();
            if (SubWorkDrilldownState.EffectiveDrilldownStyle() == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                EnterFluffyHostedSubWork(openType);
            }
            else
            {
                SubWorkDrilldownState.Enter(
                    openType,
                    storedReturnPosition,
                    SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(layout.Table, layout.HeaderHeight));
            }
            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
            ShowCtrlClickDefaultNoticeIfNeeded(storedReturnPosition.HasValue, evt);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }

        private bool TryHandleSubWorkBadgeClick(IWorkTabLayoutController layout)
        {
            if (layout == null)
            {
                return false;
            }

            Event evt = Event.current;
            if (evt == null || evt.type != EventType.MouseDown || evt.button != 0)
            {
                return false;
            }

            if (SubWorkDrilldownState.IsActive)
            {
                if (!SubWorkHeaderAffordance.TryGetBackLabelCellRect(layout, out Rect labelCellRect) ||
                    !labelCellRect.Contains(evt.mousePosition))
                {
                    return false;
                }

                ExitSubWorkDrilldown(restoreMousePosition: false);
                evt.Use();
                return true;
            }

            if (!SubWorkHeaderAffordance.TryGetOpenBadgeTarget(layout, evt.mousePosition, out var workType, out var badgeRect))
            {
                return false;
            }

            MarkSubWorkCtrlClickNoticeDismissed();
            if (FluffyWorkTabGateway.TryStartSubWorkDrilldownStyleChooser(layout, workType, badgeRect))
            {
                evt.Use();
                return true;
            }

            TimePriorityScheduleEditor.CloseForWorkModeTransition();
            if (SubWorkDrilldownState.EffectiveDrilldownStyle() == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside)
            {
                EnterFluffyHostedSubWork(workType);
            }
            else
            {
                SubWorkDrilldownState.Enter(
                    workType,
                    GuiMousePosition.ToRootUiPosition(evt.mousePosition),
                    SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(layout.Table, layout.HeaderHeight));
            }
            WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }

        private static void ShowCtrlClickDefaultNoticeIfNeeded(bool fromHeader, Event evt)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null ||
                settings.subWorkCtrlClickNoticeDismissed ||
                !fromHeader ||
                evt == null ||
                evt.button != 0 ||
                !IsControlClick(evt))
            {
                return;
            }

            settings.subWorkCtrlClickNoticeDismissed = true;
            settings.Write();
            Find.WindowStack.Add(new Dialog_MessageBox(
                "BWT_SubWork_CtrlClickNotice_Text".Translate(),
                "BWT_SubWork_CtrlClickNotice_UseCtrl".Translate(),
                () =>
                {
                    settings.subWorkDrilldownModifier = BetterWorkTabSettings.SubWorkDrilldownModifier.Ctrl;
                    settings.subWorkDrilldownButton = BetterWorkTabSettings.SubWorkDrilldownButton.Left;
                    settings.subWorkCtrlClickNoticeDismissed = true;
                    settings.Write();
                },
                "BWT_SubWork_CtrlClickNotice_Never".Translate(),
                () =>
                {
                    settings.subWorkCtrlClickNoticeDismissed = true;
                    settings.Write();
                },
                "BWT_SubWork_CtrlClickNotice_Title".Translate()));
        }

        private static bool IsControlClick(Event evt)
        {
            return evt != null &&
                (evt.control ||
                 (evt.modifiers & EventModifiers.Control) != 0 ||
                 UnityEngine.Input.GetKey(KeyCode.LeftControl) ||
                 UnityEngine.Input.GetKey(KeyCode.RightControl));
        }

        private static void MarkSubWorkCtrlClickNoticeDismissed()
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null || settings.subWorkCtrlClickNoticeDismissed)
            {
                return;
            }

            settings.subWorkCtrlClickNoticeDismissed = true;
            settings.Write();
        }

        private static void EnterFluffyHostedSubWork(WorkTypeDef workType)
        {
            SubWorkDrilldownState.ToggleExpandBeside(workType);
        }

        private bool TryHandleSubWorkExitGesture(IWorkTabLayoutController layout)
        {
            if (!SubWorkDrilldownState.HasAnyDrilldown)
            {
                return false;
            }

            Event evt = Event.current;
            if (evt == null)
            {
                return false;
            }

            // A Ctrl-drag of a BWT sub-work header must reorder the child column. It must
            // never be reinterpreted as the Ctrl-click exit gesture on the final MouseUp.
            if (PawnOrganizerSystem.Instance?.IsDraggingColumn == true)
            {
                ClearPendingSubWorkGesture();
                return false;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                if (SubWorkDrilldownState.IsActive)
                {
                    SubWorkDrilldownBarRenderer.ExitDrilldown();
                }
                else
                {
                    SubWorkDrilldownState.CollapseAllExpandBeside();
                    WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
                }
                evt.Use();
                return true;
            }

            if (!SubWorkDrilldownState.IsActive)
            {
                return false;
            }

            if (layout == null)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown)
            {
                if (!SubWorkDrilldownInput.MatchesGesture(evt))
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                if (!TryGetSubWorkExitTarget(
                        layout,
                        evt.mousePosition,
                        out var bounds,
                        out bool shouldRestoreCursor))
                {
                    ClearPendingSubWorkGesture();
                    return false;
                }

                BeginPendingSubWorkGesture(
                    start: evt.mousePosition,
                    bounds: bounds,
                    button: evt.button,
                    openType: null,
                    exit: true,
                    restoreCursor: shouldRestoreCursor);
                MarkSubWorkPriorityMouseDownForSuppression();
                return false;
            }

            if (!_pendingSubWorkGesture || !_pendingSubWorkExit)
            {
                return false;
            }

            if (evt.type == EventType.MouseDrag)
            {
                CancelPendingSubWorkIfDragged(evt.mousePosition);
                return false;
            }

            if (evt.type != EventType.MouseUp || evt.button != _pendingSubWorkButton)
            {
                return false;
            }

            bool shouldExit = IsPendingSubWorkClick(evt.mousePosition);
            bool pendingRestoreCursor = _pendingSubWorkRestoreCursor;
            ClearPendingSubWorkGesture();

            if (!shouldExit)
            {
                return false;
            }

            SubWorkDrilldownBarRenderer.ExitDrilldown(restoreMousePosition: pendingRestoreCursor);
            evt.Use();
            return true;
        }

        private bool TryGetSubWorkOpenTarget(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out WorkTypeDef workType,
            out Rect bounds,
            out bool fromHeader)
        {
            workType = null;
            bounds = default;
            fromHeader = false;

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (column.HeaderRect.Contains(mousePosition))
                {
                    return TryGetOpenTargetFromColumn(column, column.HeaderRect, true, out workType, out bounds, out fromHeader);
                }
            }

            WorkTabLayoutRow bodyRow;
            WorkTabLayoutColumn bodyColumn;
            if (TryGetRowAt(layout, mousePosition, out bodyRow) &&
                TryGetBodyColumnAt(layout, mousePosition, out bodyColumn) &&
                TryGetPriorityBoxHit(layout, bodyRow, bodyColumn, mousePosition, out Rect priorityBoxRect))
            {
                return TryGetOpenTargetFromColumn(bodyColumn, priorityBoxRect, false, out workType, out bounds, out fromHeader);
            }

            return false;
        }

        private bool TryGetOpenTargetFromColumn(
            WorkTabLayoutColumn column,
            Rect candidateBounds,
            bool isHeader,
            out WorkTypeDef workType,
            out Rect bounds,
            out bool fromHeader)
        {
            workType = null;
            bounds = default;
            fromHeader = false;

            bool isSupportedWorkColumn =
                column.Column?.Worker is PawnColumnWorker_WorkPriority ||
                FluffyWorkTabGateway.IsFluffyColumn(column.Column);
            WorkTypeDef targetWorkType = column.SubWorkParent ?? column.Column?.workType;
            if (!isSupportedWorkColumn || targetWorkType == null)
            {
                return false;
            }

            if (WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(targetWorkType).Count == 0)
            {
                return false;
            }

            workType = targetWorkType;
            bounds = candidateBounds;
            fromHeader = isHeader;
            return true;
        }

        private bool TryGetSubWorkExitTarget(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out Rect bounds,
            out bool restoreCursor)
        {
            bounds = default;
            restoreCursor = false;

            Rect headerArea = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y,
                layout.Table.Size.x,
                layout.HeaderHeight);

            if (headerArea.Contains(mousePosition))
            {
                bounds = headerArea;
                restoreCursor = BetterWorkTabMod.Settings?.restoreCursorOnSubWorkExit ?? true;
                return true;
            }

            Rect globalRowArea = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight + TimePriorityScheduleEditor.HeaderPinnedRowsHeight,
                layout.Table.Size.x,
                SubWorkDrilldownBarRenderer.RowHeight);

            if (globalRowArea.Contains(mousePosition) &&
                TryGetGlobalPriorityBoxHit(layout, globalRowArea, mousePosition, out var globalColumn, out Rect globalPriorityBoxRect))
            {
                bounds = globalPriorityBoxRect;
                restoreCursor = false;
                return true;
            }

            WorkTabLayoutRow bodyRow;
            WorkTabLayoutColumn column;
            if (TryGetRowAt(layout, mousePosition, out bodyRow) &&
                TryGetBodyColumnAt(layout, mousePosition, out column) &&
                TryGetPriorityBoxHit(layout, bodyRow, column, mousePosition, out Rect bodyPriorityBoxRect))
            {
                bounds = bodyPriorityBoxRect;
                restoreCursor = BetterWorkTabMod.Settings?.restoreCursorOnSubWorkPawnCellExit ?? false;
                return true;
            }

            return false;
        }

        internal void ExitSubWorkDrilldown(bool restoreMousePosition)
        {
            SubWorkDrilldownBarRenderer.ExitDrilldown(restoreMousePosition: restoreMousePosition);
        }

        private bool TryGetPriorityBoxHit(
            IWorkTabLayoutController layout,
            WorkTabLayoutRow row,
            WorkTabLayoutColumn column,
            Vector2 mousePosition,
            out Rect priorityBoxRect)
        {
            priorityBoxRect = default;
            if (layout == null ||
                row.Pawn == null ||
                !(column.Column?.Worker is PawnColumnWorker_WorkPriority))
            {
                return false;
            }

            Rect rowRect = layout.GetScreenRect(row);
            Rect cellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, rowRect.height);
            priorityBoxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
            return priorityBoxRect.Contains(mousePosition);
        }

        private bool TryHandlePriorityCellInput(IWorkTabLayoutController layout, Event evt)
        {
            if (layout == null || evt == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel))
            {
                return false;
            }

            if (RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab)
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("rule-builder owner", evt);
                return false;
            }

            if (FluffyTimeScheduleAssigner.IsOpen)
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("fluffy scheduler owner", evt);
                return false;
            }

            if (SubWorkDrilldownInput.MatchesGesture(evt))
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("sub-work gesture", evt);
                return false;
            }

            if (TimePriorityScheduleEditor.OwnsMousePosition(evt.mousePosition))
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("time-priority owner", evt);
                return false;
            }

            if (!TryGetRowAt(layout, evt.mousePosition, out WorkTabLayoutRow row))
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("row miss", evt);
                return false;
            }

            if (row.Pawn == null)
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("non-pawn row", evt);
                return false;
            }

            if (!TryGetBodyColumnAt(layout, evt.mousePosition, out WorkTabLayoutColumn column))
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("column miss", evt);
                return false;
            }

            if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("non-priority column", evt);
                return false;
            }

            if (!TryGetPriorityBoxHit(layout, row, column, evt.mousePosition, out Rect priorityBoxRect))
            {
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace("priority-box miss", evt);
                return false;
            }

            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    column,
                    out WorkGiver workGiver,
                    out WorkTypeDef parentWorkType,
                    out _))
            {
                int parentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(row.Pawn, parentWorkType);
                bool handled = WorkGiverPriorityBoxRenderer.TryHandleRootInput(
                    workGiver,
                    parentWorkType,
                    row.Pawn,
                    priorityBoxRect,
                    parentPriority);
                WorkTabGeometryDiagnostics.RecordPriorityInputTrace(
                    handled ? "sub-work handled" : "sub-work rejected",
                    evt);
                return handled;
            }

            WorkTypeDef workType = column.Column.workType;
            if (workType == null)
            {
                return false;
            }

            Rect rowRect = layout.GetScreenRect(row);
            Rect rootCellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, rowRect.height);
            bool parentHandled = Patch_WorkPriority_DoCell_Unified.TryHandleRootPriorityInput(
                rootCellRect,
                row.Pawn,
                workType);
            WorkTabGeometryDiagnostics.RecordPriorityInputTrace(
                parentHandled ? "parent handled" : "parent rejected",
                evt);
            return parentHandled;
        }

        private bool TryGetGlobalPriorityBoxHit(
            IWorkTabLayoutController layout,
            Rect globalRowRect,
            Vector2 mousePosition,
            out WorkTabLayoutColumn column,
            out Rect priorityBoxRect)
        {
            column = default;
            priorityBoxRect = default;
            if (layout?.Columns == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var candidate = layout.Columns[i];
                if (!(candidate.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    !SubWorkDrilldownState.TryGetWorkGiverForColumn(candidate, out _, out _, out _))
                {
                    continue;
                }

                Rect cellRect = new Rect(candidate.HeaderRect.x, globalRowRect.y, candidate.Width, globalRowRect.height);
                float boxSize = Mathf.Min(SubWorkDrilldownState.GlobalPriorityBoxSize, Mathf.Max(0f, cellRect.height - 4f));
                if (boxSize <= 6f)
                {
                    continue;
                }

                Rect boxRect = WorkPriorityCellGeometry.GetCenteredBoxRect(cellRect, boxSize);
                if (!boxRect.Contains(mousePosition))
                {
                    continue;
                }

                column = candidate;
                priorityBoxRect = boxRect;
                return true;
            }

            return false;
        }

        private static float GetVisualTableBottom(IWorkTabLayoutController layout)
        {
            if (layout == null)
            {
                return 0f;
            }

            return layout.TableOrigin.y +
                layout.HeaderHeight +
                GetPinnedRowsHeight() +
                layout.ContentHeight;
        }

        private void BeginPendingSubWorkGesture(
            Vector2 start,
            Rect bounds,
            int button,
            WorkTypeDef openType,
            bool exit,
            bool restoreCursor)
        {
            NativeCursorPosition.CancelPendingMove();
            _pendingSubWorkGesture = true;
            _pendingSubWorkStart = start;
            _pendingSubWorkBounds = bounds;
            _pendingSubWorkButton = button;
            _pendingSubWorkOpenType = openType;
            _pendingSubWorkExit = exit;
            _pendingSubWorkRestoreCursor = restoreCursor;
        }

        private void CancelPendingSubWorkIfDragged(Vector2 mousePosition)
        {
            if (!_pendingSubWorkGesture)
            {
                return;
            }

            float threshold = Mathf.Max(1f, BetterWorkTabMod.Settings?.dragThreshold ?? DefaultSettings.dragThreshold);
            if ((mousePosition - _pendingSubWorkStart).magnitude >= threshold)
            {
                ClearPendingSubWorkGesture();
            }
        }

        private bool IsPendingSubWorkClick(Vector2 mousePosition)
        {
            if (!_pendingSubWorkGesture)
            {
                return false;
            }

            float threshold = Mathf.Max(1f, BetterWorkTabMod.Settings?.dragThreshold ?? DefaultSettings.dragThreshold);
            return (mousePosition - _pendingSubWorkStart).magnitude < threshold &&
                _pendingSubWorkBounds.Contains(mousePosition);
        }

        private void ClearPendingSubWorkGesture()
        {
            _pendingSubWorkGesture = false;
            _pendingSubWorkStart = Vector2.zero;
            _pendingSubWorkBounds = default;
            _pendingSubWorkOpenType = null;
            _pendingSubWorkButton = -1;
            _pendingSubWorkExit = false;
            _pendingSubWorkRestoreCursor = false;
        }

        private void MarkSubWorkPriorityMouseDownForSuppression()
        {
            _suppressSubWorkPriorityMouseDownFrame = Time.frameCount;
        }

        private void SuppressSubWorkPriorityMouseDownIfNeeded(Event evt)
        {
            if (evt == null ||
                evt.type != EventType.MouseDown ||
                _suppressSubWorkPriorityMouseDownFrame != Time.frameCount)
            {
                return;
            }

            evt.Use();
        }

        /// <summary>
        /// Checks if a column should show the yellow asterisk marker.
        /// A column is marked only if:
        /// 1. The player directly dragged it (recorded in playerDraggedColumns), AND
        /// 2. It is currently out of its baseline position
        /// 
        /// Columns that shifted as a side effect of another drag are NOT marked.
        /// </summary>
        internal static bool ShouldShowColumnMarker(WorkTypeDef workType)
        {
            if (workType?.defName == null)
                return false;

            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
                return false;

            if (!settings.showColumnMovedMarker)
                return false;

            if (SubWorkDrilldownState.IsActive)
            {
                return SubWorkDrilldownState.TryGetWorkGiverForWorkTypeSlot(workType, out var workGiver, out _) &&
                       SubWorkDrilldownState.IsWorkGiverMovedFromBaseline(workGiver.def);
            }

            if (_columnMarkerCacheValidationFrame != Time.frameCount)
            {
                WorkTabInvalidationVersion invalidation = WorkTabInvalidationHub.Current;
                int draggedCount = settings.playerDraggedColumns?.Count ?? 0;
                if (!ReferenceEquals(_columnMarkerCacheSettings, settings) ||
                    !ReferenceEquals(_columnMarkerCacheGame, Current.Game) ||
                    _columnMarkerCacheColumnsRevision != invalidation.Columns ||
                    _columnMarkerCacheDraggedCount != draggedCount ||
                    _columnMarkerCacheEnabled != settings.showColumnMovedMarker)
                {
                    ClearColumnMarkerCache();
                    _columnMarkerCacheSettings = settings;
                    _columnMarkerCacheGame = Current.Game;
                    _columnMarkerCacheColumnsRevision = invalidation.Columns;
                    _columnMarkerCacheDraggedCount = draggedCount;
                    _columnMarkerCacheEnabled = settings.showColumnMovedMarker;
                }
                _columnMarkerCacheValidationFrame = Time.frameCount;
            }

            if (ColumnMarkerCache.TryGetValue(workType, out bool cachedResult))
            {
                return cachedResult;
            }

            // First check: was this column directly dragged by the player?
            if (!settings.WasColumnDraggedByPlayer(workType.defName))
            {
                ColumnMarkerCache[workType] = false;
                return false;
            }

            // Second check: is it currently out of baseline position?
            bool result = IsColumnOutOfBaselinePosition(workType);
            ColumnMarkerCache[workType] = result;
            return result;
        }

        private static void ClearColumnMarkerCache()
        {
            ColumnMarkerCache.Clear();
            _columnMarkerCacheValidationFrame = -1;
        }

        /// <summary>
        /// Checks if a column's current position differs from its baseline position.
        /// This is a pure position check with no marking logic.
        /// </summary>
        private static bool IsColumnInBaselinePosition(WorkTypeDef workType)
        {
            if (workType?.defName == null) return true;

            var baselineOrder = WorkColumnOrderManager.GetBaselineOrder();
            if (baselineOrder?.Count == 0) return true;

            var def = PawnTableDefOf.Work;
            if (def?.columns == null) return true;

            // Compare relative positions without rebuilding three LINQ lists for every header.
            // The baseline remains filtered to work types that are present in the live table,
            // matching the previous behavior when another mod adds or removes a column.
            int currentPos = -1;
            int currentWorkIndex = 0;
            for (int i = 0; i < def.columns.Count; i++)
            {
                PawnColumnDef column = def.columns[i];
                if (!(column?.Worker is PawnColumnWorker_WorkPriority) || column.workType?.defName == null)
                {
                    continue;
                }

                if (column.workType.defName == workType.defName)
                {
                    currentPos = currentWorkIndex;
                    break;
                }

                currentWorkIndex++;
            }

            int relVanillaPos = -1;
            int filteredBaselineIndex = 0;
            for (int baselineIndex = 0; baselineIndex < baselineOrder.Count; baselineIndex++)
            {
                string baselineDefName = baselineOrder[baselineIndex];
                bool present = false;
                for (int columnIndex = 0; columnIndex < def.columns.Count; columnIndex++)
                {
                    PawnColumnDef column = def.columns[columnIndex];
                    if (column?.Worker is PawnColumnWorker_WorkPriority &&
                        column.workType?.defName == baselineDefName)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    continue;
                }

                if (baselineDefName == workType.defName)
                {
                    relVanillaPos = filteredBaselineIndex;
                    break;
                }

                filteredBaselineIndex++;
            }

            if (relVanillaPos < 0 || currentPos < 0) return true;

            return relVanillaPos == currentPos;
        }

        /// <summary>
        /// Returns true if the column is NOT in its baseline position.
        /// </summary>
        internal static bool IsColumnOutOfBaselinePosition(WorkTypeDef workType)
        {
            return !IsColumnInBaselinePosition(workType);
        }

        /// <summary>
        /// Called after a column drag completes. Records that this specific column was
        /// directly dragged by the player, then updates its marking status based on
        /// whether it ended up out of vanilla position.
        /// </summary>
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
        internal static void MarkColumnMoved(WorkTypeDef workType)
        {
            if (workType?.defName == null)
                return;

            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
                return;

            // Record that the player dragged this column
            settings.RecordPlayerDraggedColumn(workType.defName);

            // If the column ended up back in baseline position, remove it from the dragged list
            if (IsColumnInBaselinePosition(workType))
            {
                settings.playerDraggedColumns.Remove(workType.defName);
            }

            settings.Write();
            ClearColumnMarkerCache();
        }

        /// <summary>
        /// Clears all column markers. Called when resetting to vanilla order.
        /// </summary>
        internal static void ClearAllColumnMarkers()
        {
            var settings = BetterWorkTabMod.Settings;
            settings?.ClearPlayerDraggedColumns();
            settings?.Write();
            ClearColumnMarkerCache();
        }

        public override void PostOpen()
        {
            PawnTable table = GetPawnTable();
            bool reuseWarmTable = CanReuseWarmOpenTable(table);
            if (reuseWarmTable)
            {
                WarmOpenPawnTableCache.Begin(table);
            }

            try
            {
                base.PostOpen();
            }
            finally
            {
                if (reuseWarmTable)
                {
                    WarmOpenPawnTableCache.End();
                }
            }

            CaptureWarmOpenTableState(GetPawnTable());
            WorkTabProfilingState.NotifyOpen(true);
        }

        private bool CanReuseWarmOpenTable(PawnTable table)
        {
            IWorkTabLayoutController layout = PawnOrganizerSystem.Instance?.Layout;
            return _warmOpenStateValid &&
                table != null &&
                !PawnTableCompat.IsDirty(table) &&
                ReferenceEquals(table, _warmOpenTable) &&
                ReferenceEquals(Current.Game, _warmOpenGame) &&
                ReferenceEquals(Find.CurrentMap, _warmOpenMap) &&
                Verse.UI.screenWidth == _warmOpenScreenWidth &&
                Verse.UI.screenHeight == _warmOpenScreenHeight &&
                (layout?.LayoutRevision ?? -1) == _warmOpenLayoutRevision &&
                PawnTableCompat.GetPawnCount(table) == _warmOpenPawnCount &&
                PawnTableCompat.GetColumnCount(table) == _warmOpenColumnCount;
        }

        private void CaptureWarmOpenTableState(PawnTable table)
        {
            IWorkTabLayoutController layout = PawnOrganizerSystem.Instance?.Layout;
            _warmOpenTable = table;
            _warmOpenGame = Current.Game;
            _warmOpenMap = Find.CurrentMap;
            _warmOpenScreenWidth = Verse.UI.screenWidth;
            _warmOpenScreenHeight = Verse.UI.screenHeight;
            _warmOpenLayoutRevision = layout?.LayoutRevision ?? -1;
            _warmOpenPawnCount = PawnTableCompat.GetPawnCount(table);
            _warmOpenColumnCount = PawnTableCompat.GetColumnCount(table);
            _warmOpenStateValid = table != null && !PawnTableCompat.IsDirty(table);
        }

        public override void WindowOnGUI()
        {
            // GUI.Window establishes clipping and event coordinates before it invokes
            // DoWindowContents. Resizing from inside that callback splits one IMGUI
            // event across two rectangles, leaving stale black bands and invalidating
            // otherwise-correct priority-cell hit coordinates.
            if (_hasPendingWindowRect)
            {
                windowRect = _pendingWindowRect;
                _hasPendingWindowRect = false;
            }

            base.WindowOnGUI();
        }

        private void DrawRows(
            PawnTable table,
            IWorkTabLayoutController layout,
            Rect outRect,
            Rect viewRect,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            if (layout == null)
            {
                return;
            }

            var rowDescriptors = layout.GetRowDescriptors();
            var columns = layout.Columns;
            if (rowDescriptors == null || rowDescriptors.Count == 0 ||
                columns == null || columns.Count == 0)
            {
                PawnTableCompat.SetScrollPosition(table, Vector2.zero);
                return;
            }

            Vector2 tableScrollPosition = PawnTableCompat.GetScrollPosition(table);

            bool horizontalOverflow = viewRect.width > outRect.width + 0.5f;
            Rect horizontalScrollbarRect = new Rect(
                outRect.x,
                outRect.yMax - HorizontalScrollbarHeight,
                outRect.width,
                HorizontalScrollbarHeight);
            Event evt = Event.current;
            float rootMouseX = evt.mousePosition.x;
            bool captureOnMouseDown = horizontalOverflow &&
                evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                horizontalScrollbarRect.Contains(evt.mousePosition);

            bool applyCapturedScroll = false;
            float capturedScrollX = tableScrollPosition.x;
            if (_horizontalScrollbarDragCaptured)
            {
                bool released = evt.rawType == EventType.MouseUp || !UnityEngine.Input.GetMouseButton(0);
                if (released || !horizontalOverflow)
                {
                    _horizontalScrollbarDragCaptured = false;
                }
                else if (evt.type != EventType.Layout)
                {
                    float maxScrollX = Mathf.Max(0f, viewRect.width - outRect.width);
                    capturedScrollX = Mathf.Clamp(
                        _horizontalScrollbarDragScrollX +
                        (rootMouseX - _horizontalScrollbarDragMouseX) * _horizontalScrollbarDragPixelsToContent,
                        0f,
                        maxScrollX);
                    applyCapturedScroll = true;
                }
            }

            Widgets.BeginScrollView(outRect, ref tableScrollPosition, viewRect);
            try
            {
                if (applyCapturedScroll)
                {
                    tableScrollPosition.x = capturedScrollX;
                }

                if (captureOnMouseDown && GUIUtility.hotControl != 0)
                {
                    _horizontalScrollbarDragCaptured = true;
                    _horizontalScrollbarDragMouseX = rootMouseX;
                    _horizontalScrollbarDragScrollX = tableScrollPosition.x;
                    _horizontalScrollbarDragPixelsToContent =
                        viewRect.width / Mathf.Max(1f, horizontalScrollbarRect.width);
                }

                if (Event.current.type == EventType.Layout)
                {
                    return;
                }

                var nameColumn = FindNameColumn(columns);
                IReadOnlyList<WorkTabLayoutColumn> renderColumns = columns;
                var settings = BetterWorkTabMod.Settings;
                if (snapshotLayer == null &&
                    (settings?.enablePerformanceOptimizations ?? true) &&
                    (settings?.viewportCulling ?? true) &&
                    viewRect.width > outRect.width + 0.5f)
                {
                    const float HorizontalCullBuffer = 64f;
                    float visibleLeft = tableScrollPosition.x - HorizontalCullBuffer;
                    float visibleRight = tableScrollPosition.x + outRect.width + HorizontalCullBuffer;
                    _visibleRenderColumns.Clear();
                    for (int i = 0; i < columns.Count; i++)
                    {
                        WorkTabLayoutColumn column = columns[i];
                        if (column.OffsetX + column.Width >= visibleLeft && column.OffsetX <= visibleRight)
                        {
                            _visibleRenderColumns.Add(column);
                        }
                    }

                    renderColumns = _visibleRenderColumns;
                }

                // Calculate dimensions once for all highlight operations
                float totalWidth = CalculateTotalColumnWidth(columns);
                float totalHeight = layout.ContentHeight; // Already accounts for all descriptor heights

                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawAllHighlights", () => DrawAllHighlights(rowDescriptors, columns, totalWidth, totalHeight));
                    SpineTiming.Time("WorkTab.Rows.DrawAllRowContent", () => DrawAllRowContent(table,
                        rowDescriptors,
                        renderColumns,
                        viewRect.width,
                        nameColumn,
                        outRect,
                        tableScrollPosition,
                        snapshotLayer));
                    SpineTiming.Time("WorkTab.Rows.DrawRowSeparators", () => DrawRowSeparators(rowDescriptors, viewRect.width));
                }
                else
                {
                    // Phase 1: Draw all highlights (selected, hovered, float menu, similar worktypes)
                    DrawAllHighlights(rowDescriptors, columns, totalWidth, totalHeight);

                    // Phase 2: Draw actual row content (pawn data, divider labels, backgrounds)
                    DrawAllRowContent(table,
                        rowDescriptors,
                        renderColumns,
                        viewRect.width,
                        nameColumn,
                        outRect,
                        tableScrollPosition,
                        snapshotLayer);

                    // Phase 3: Draw separator lines between rows
                    DrawRowSeparators(rowDescriptors, viewRect.width);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BWT] DrawRows failed: {ex}");
            }
            finally
            {
                Widgets.EndScrollView();
                PawnTableCompat.SetScrollPosition(table, tableScrollPosition);
            }
        }

        /// <summary>
        /// Calculates the total width of all columns combined.
        /// Called once per frame, not per row.
        /// </summary>
        private float CalculateTotalColumnWidth(IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            float totalWidth = 0f;
            for (int i = 0; i < columns.Count; i++)
            {
                totalWidth += columns[i].Width;
            }
            return totalWidth;
        }

        /// <summary>
        /// Phase 1: Draws all highlighting overlays.
        /// This includes:
        /// - Selected pawn row highlighting (horizontal)
        /// - Hovered row highlighting (horizontal)
        /// - Float menu worktype column highlighting (vertical)
        /// - Hovered column highlighting (vertical)
        /// - Similar worktype highlighting (vertical, dimmer)
        /// </summary>
        private void DrawAllHighlights(
            List<RowDescriptor> rowDescriptors,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float totalWidth,
            float totalHeight)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!settings.ShowPawnAndWorktypeHighlights || !settings.enableRowColumnHighlights) return;

            WorkTabLayoutColumn? hoveredColumn = null;
            WorkTypeDef hoveredWorkType = null;
            bool timePriorityOwnsMouse = TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                                         BWTWorkTabTutorial.OwnsCurrentPointer;

            // 1. Detect Hovered Column
            if (settings.ShowCursorPawnAndWorktypeHighlight && !timePriorityOwnsMouse)
            {
                float currentX = 0f;
                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    float animatedX = currentX + ColumnReorderAnimationState.GetCellOffset(col);
                    var columnRect = new Rect(animatedX, 0f, col.Width, totalHeight);

                    if (Mouse.IsOver(columnRect))
                    {
                        hoveredColumn = col;
                        hoveredWorkType = col.Column?.workType;
                        break;
                    }
                    currentX += col.Width;
                }
            }

            if (hoveredWorkType == null && !timePriorityOwnsMouse)
            {
                hoveredWorkType = PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType;
            }

            MouseStateManager.UpdateHoverState(hoveredColumn);

            var cachedSimilarWorktypes = hoveredWorkType != null
                ? WorkColumnOrderManager.GetSimilarWorktypes(hoveredWorkType)
                : null;

            // Get persistent float menu state
            Pawn highlightedPawn = HighlightState.GetHighlightedPawn();
            WorkTypeDef highlightedWorkType = HighlightState.GetHighlightedWorkType();
            WorkGiverDef highlightedWorkGiver = HighlightState.GetHighlightedWorkGiver();

            // 2. Draw Horizontal Highlights (Rows)
            float currentY = 0f;
            for (int i = 0; i < rowDescriptors.Count; i++)
            {
                var descriptor = rowDescriptors[i];
                Rect rowRect = new Rect(0f, currentY, totalWidth, descriptor.Height);

                bool isFloatMenuPawn = descriptor.IsPawn &&
                    highlightedPawn != null &&
                    descriptor.Pawn == highlightedPawn &&
                    settings.ShowFloatMenuPawnAndWorktypeHighlight;

                if (descriptor.IsPawn && highlightedPawn != null && descriptor.Pawn == highlightedPawn && settings.ShowFloatMenuPawnAndWorktypeHighlight)
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetFloatMenuColor());
                }

                if (settings.ShowCursorPawnAndWorktypeHighlight && !timePriorityOwnsMouse && Mouse.IsOver(rowRect))
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetRowHoverColor());
                }
                else if (!isFloatMenuPawn && descriptor.IsPawn && Find.Selector.IsSelected(descriptor.Pawn) && settings.DoSelectedPawnHighlight)
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetSelectedPawnColor());
                }

                currentY += descriptor.Height;
            }

            // 3. Draw Divider Highlight if active
            if (settings.highlightDividersOnHover && settings.enableDividers)
            {
                currentY = 0f;
                for (int i = 0; i < rowDescriptors.Count; i++)
                {
                    var descriptor = rowDescriptors[i];
                    Rect rowRect = new Rect(0f, currentY, totalWidth, descriptor.Height);

                    if (descriptor.IsDivider && !timePriorityOwnsMouse && Mouse.IsOver(rowRect))
                    {
                        HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetRowHoverColor());
                    }

                    currentY += descriptor.Height;
                }
            }

            // 4. Draw Vertical Highlights (Columns)
            float startingX = 0f;
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                float animatedX = startingX + ColumnReorderAnimationState.GetCellOffset(column);
                Rect columnRect = new Rect(animatedX, 0f, column.Width, totalHeight);
                bool isWorkColumn = WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column);
                bool isFloatMenuColumn = isWorkColumn &&
                    IsColumnHighlightedByFloatMenu(column, highlightedWorkType, highlightedWorkGiver);
                bool isTimePrioritySourceColumn = isWorkColumn && TimePriorityScheduleEditor.ShouldHighlightSourceColumn(column);

                if (isFloatMenuColumn && settings.ShowFloatMenuPawnAndWorktypeHighlight)
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetFloatMenuColor());
                }

                if (isTimePrioritySourceColumn)
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetColumnHoverColor());
                }
                else if (isWorkColumn &&
                         settings.ShowCursorPawnAndWorktypeHighlight &&
                         !timePriorityOwnsMouse &&
                         hoveredWorkType != null &&
                         hoveredWorkType == column.Column.workType)
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetColumnHoverColor());
                }
                else if (!isFloatMenuColumn && isWorkColumn && settings.ShowSimilarWorktypeHighlight && cachedSimilarWorktypes != null && cachedSimilarWorktypes.Contains(column.Column.workType))
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetSimilarWorktypeColor());
                }

                startingX += column.Width;
            }
        }

        private static bool IsColumnHighlightedByFloatMenu(
            WorkTabLayoutColumn column,
            WorkTypeDef highlightedWorkType,
            WorkGiverDef highlightedWorkGiver)
        {
            if (highlightedWorkType == null || !WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column))
            {
                return false;
            }

            if (highlightedWorkGiver != null)
            {
                return SubWorkDrilldownState.TryGetWorkGiverForColumn(column, out var workGiver, out var parentWorkType, out _) &&
                       parentWorkType == highlightedWorkType &&
                       workGiver?.def == highlightedWorkGiver;
            }

            if (column.Column?.workType == null)
            {
                return false;
            }

            return column.Column.workType == highlightedWorkType;
        }

        /// <summary>
        /// Phase 2: Draws the actual content of each row.
        /// This includes:
        /// - Pawn row backgrounds (custom colors if set)
        /// - Divider backgrounds (with divider color)
        /// - Pawn work priority cells (via PawnTable column workers)
        /// - Divider labels and collapse arrows
        /// - Pawn row overlays (selection highlight, hover, downed strike-through)
        /// </summary>
        private void DrawAllRowContent(
            PawnTable table,
            List<RowDescriptor> rowDescriptors,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float viewWidth,
            WorkTabLayoutColumn? nameColumn,
            Rect viewportRect,
            Vector2 scrollOffset,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            var settings = BetterWorkTabMod.Settings;
            bool useCulling = (settings?.enablePerformanceOptimizations ?? true) && (settings?.viewportCulling ?? true);
            // Only render rows that intersect the scroll viewport (with small buffer to avoid pop-in)
            float currentY = 0f;
            float viewportTop = scrollOffset.y;
            float viewportBottom = scrollOffset.y + viewportRect.height;
            const float BufferPixels = 60f; // 2 extra rows for smooth scrolling

            for (int i = 0; i < rowDescriptors.Count; i++)
            {
                var descriptor = rowDescriptors[i];
                float rowBottom = currentY + descriptor.Height;
                bool isVisible = useCulling
                    ? rowBottom >= (viewportTop - BufferPixels) &&
                                 currentY <= (viewportBottom + BufferPixels)
                    : true;

                if (isVisible)
                {
                    Rect rowRect = new Rect(0f, currentY, viewWidth, descriptor.Height);
                    DrawSingleRowContent(table, descriptor, columns, rowRect, nameColumn, i, snapshotLayer);
                }

                currentY += descriptor.Height;
            }
        }

        private void DrawSingleRowContent(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            WorkTabLayoutColumn? nameColumn,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            // Create temporary wrapper objects to maintain compatibility with existing draw methods.
            // These are small allocations; only optimize with pooling if profiling shows it's necessary.
            if (descriptor.IsPawn)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawPawnRowContent", () => DrawPawnRowContent(table, descriptor, columns, rowRect, rowIndex, snapshotLayer));
                }
                else
                {
                    DrawPawnRowContent(table, descriptor, columns, rowRect, rowIndex, snapshotLayer);
                }
            }
            else if (descriptor.IsDivider)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawDividerRowContent", () => DrawDividerRowContent(descriptor, rowRect, nameColumn, rowIndex, snapshotLayer));
                }
                else
                {
                    DrawDividerRowContent(descriptor, rowRect, nameColumn, rowIndex, snapshotLayer);
                }
            }
        }

        private void DrawPawnRowContent(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            if (rowRect.height <= 0.5f)
            {
                return;
            }

            if (rowRect.height < MinimumPawnRenderHeight - 0.5f)
            {
                GUI.BeginGroup(rowRect);
                try
                {
                    Rect clippedRowRect = new Rect(0f, 0f, rowRect.width, MinimumPawnRenderHeight);
                    DrawPawnRowContentUnclipped(table, descriptor, columns, clippedRowRect, rowIndex, snapshotLayer);
                }
                finally
                {
                    GUI.EndGroup();
                }

                return;
            }

            DrawPawnRowContentUnclipped(table, descriptor, columns, rowRect, rowIndex, snapshotLayer);
        }

        private void DrawPawnRowContentUnclipped(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            // 1. Draw row background (custom pawn color if set)
            Color snapshotTextColor;
            if (snapshotLayer == null ||
                !snapshotLayer.TryDrawRowBackground(rowIndex, rowRect, out snapshotTextColor))
            {
                DrawRowBackground(descriptor.Pawn, null, rowRect);
            }
            else
            {
                CurrentRowTextColor = snapshotTextColor;
            }

            // 2. Draw all column cells for this pawn (work priorities, name, etc)
            DrawPawnRow(table, descriptor.Pawn, rowRect, columns, rowIndex, snapshotLayer);

            // 3. Draw overlays (selection glow, hover, downed strike-through)
            DrawPawnRowOverlay(descriptor.Pawn, rowRect);
        }

        private void DrawDividerRowContent(
            RowDescriptor descriptor,
            Rect rowRect,
            WorkTabLayoutColumn? nameColumn,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            // 1. Draw divider background (uses divider color, dimmed if collapsed)
            Color ignoredTextColor;
            if (snapshotLayer == null ||
                !snapshotLayer.TryDrawRowBackground(rowIndex, rowRect, out ignoredTextColor))
            {
                DrawRowBackground(null, descriptor.Divider, rowRect);
            }

            // 2. Draw divider label and collapse arrow (only in name column)
            if (nameColumn.HasValue)
            {
                DrawDividerRow(descriptor.Divider, rowRect, nameColumn.Value);
            }
        }

        /// <summary>
        /// Phase 3: Draws thin separator lines between rows.
        /// This is purely cosmetic and helps visually distinguish rows.
        /// </summary>
        private void DrawRowSeparators(List<RowDescriptor> rowDescriptors, float viewWidth)
        {
            float currentY = 0f;

            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            for (int i = 0; i < rowDescriptors.Count - 1; i++)
            {
                var descriptor = rowDescriptors[i];
                currentY += descriptor.Height;

                // Draw only between rows. A trailing line at the content boundary is
                // clipped inconsistently while the expand-beside window resizes.
                Widgets.DrawLineHorizontal(0f, currentY - 1f, viewWidth);
            }
            GUI.color = Color.white;
        }


        private void DrawRowBackground(Pawn pawn, PawnDivider divider, Rect rect)
        {
            if (pawn != null && PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out var pawnColor) && pawnColor.a > 0f)
            {
                var overlay = new Color(pawnColor.r, pawnColor.g, pawnColor.b, Mathf.Clamp(pawnColor.a, 0.08f, 0.6f));
                Widgets.DrawBoxSolid(rect, overlay);

                CurrentRowTextColor = Spine.UI.TextColorHelper.GetContrastingTextColor(overlay);
            }
            else if (divider != null)
            {
                var settings = BetterWorkTabMod.Settings;
                var dividerColor = divider.DividerColor;
                if (!(settings?.allowCustomDividerColors ?? true))
                {
                    dividerColor = Color.gray;
                }
                float minAlpha = settings?.dividerMinAlpha ?? 0.35f;
                dividerColor.a = Mathf.Max(dividerColor.a, minAlpha);
                if (divider.IsCollapsed)
                {
                    dividerColor.a = Mathf.Clamp01(dividerColor.a * 0.6f);
                }
                Widgets.DrawBoxSolid(rect, dividerColor);
            }
        }

        private void CalculateScrollRects(IWorkTabLayoutController layout, Rect inRect, out Rect outRect, out Rect viewRect)
        {
            float headerHeight = layout.HeaderHeight;
            float tableBottomSpace = Mathf.Max(
                0f,
                ExtraBottomSpace - GetInlineTimePriorityReservedHeight(layout));
            float scrollAreaHeight = Mathf.Max(0f,
                inRect.yMax -
                tableBottomSpace -
                ScrollViewFitAllowance -
                (layout.TableOrigin.y + headerHeight));

            float tableScrollWidth = GetVisualTableScrollWidth(layout, layout.Table);
            float availableWidth = Mathf.Max(1f, inRect.xMax - layout.TableOrigin.x);
            float viewportWidth = Mathf.Min(tableScrollWidth, availableWidth);

            outRect = new Rect( 
                layout.TableOrigin.x,
                layout.TableOrigin.y + headerHeight,
                viewportWidth,
                scrollAreaHeight);

            float pinnedRowsHeight = GetPinnedRowsHeight();
            if (pinnedRowsHeight > 0f)
            {
                outRect.y += pinnedRowsHeight;
                outRect.height = Mathf.Max(0f, outRect.height - pinnedRowsHeight);
            }

            float widthWithoutScrollbar = viewportWidth - 16f;
            float totalColumnWidth = layout.Columns.Count > 0
                ? layout.Columns[layout.Columns.Count - 1].OffsetX + layout.Columns[layout.Columns.Count - 1].Width
                : widthWithoutScrollbar;
            float contentHeight = Mathf.Max(layout.ContentHeight, 1f);
            bool rawHorizontalOverflow = totalColumnWidth > widthWithoutScrollbar + 0.5f;
            bool expandBesideTransitioning = SubWorkDrilldownState.IsExpandBesideTransitioning;
            bool layoutWidthChanged = _lastScrollTotalColumnWidth >= 0f &&
                Mathf.Abs(totalColumnWidth - _lastScrollTotalColumnWidth) > 0.5f;
            bool windowWidthChanged = _lastScrollWindowWidth >= 0f &&
                Mathf.Abs(windowRect.width - _lastScrollWindowWidth) > 0.5f;
            _lastScrollTotalColumnWidth = totalColumnWidth;
            _lastScrollWindowWidth = windowRect.width;
            bool resizingOverflow = rawHorizontalOverflow &&
                (layoutWidthChanged || windowWidthChanged);
            if (expandBesideTransitioning || resizingOverflow)
            {
                if (!_horizontalScrollbarTransitionActive)
                {
                    _horizontalScrollbarTransitionActive = true;
                    // Expand-beside changes column width before the bottom-anchored window
                    // receives its resized rect. A scrollbar in that catch-up interval is
                    // transient and immediately disappears, so never preserve stale visibility.
                    _horizontalScrollbarTransitionVisible = false;
                }

                // Layout advances before Unity supplies the resized window contents for this
                // GUI pass. Suppress transient overflow through the transition and two catch-up
                // frames, then commit the final overflow state once.
                _holdHorizontalScrollbarUntilFrame = Time.frameCount + 2;
            }

            bool preserveScrollbarVisibility = _horizontalScrollbarTransitionActive &&
                (expandBesideTransitioning || resizingOverflow ||
                 Time.frameCount <= _holdHorizontalScrollbarUntilFrame);
            bool needsHorizontalScrollbar;
            if (preserveScrollbarVisibility)
            {
                needsHorizontalScrollbar = _horizontalScrollbarTransitionVisible;
                _horizontalOverflowBeganFrame = -1;
            }
            else
            {
                _horizontalScrollbarTransitionActive = false;
                if (!rawHorizontalOverflow)
                {
                    _horizontalOverflowBeganFrame = -1;
                    needsHorizontalScrollbar = false;
                }
                else if (_stableHorizontalScrollbarVisible)
                {
                    needsHorizontalScrollbar = true;
                }
                else
                {
                    if (_horizontalOverflowBeganFrame < 0)
                    {
                        _horizontalOverflowBeganFrame = Time.frameCount;
                    }

                    // A drill-down can update its column width one GUI pass before its
                    // transition flag and resized window arrive. Require new overflow to
                    // persist before exposing a scrollbar, while still hiding it immediately.
                    needsHorizontalScrollbar =
                        Time.frameCount - _horizontalOverflowBeganFrame > 2;
                }

                _stableHorizontalScrollbarVisible = needsHorizontalScrollbar;
            }

            float naturalViewWidth = Mathf.Max(widthWithoutScrollbar, totalColumnWidth);
            float viewWidth = needsHorizontalScrollbar
                ? naturalViewWidth
                : widthWithoutScrollbar;
            _lastRawHorizontalOverflow = rawHorizontalOverflow;
            _lastHorizontalScrollbarVisible = needsHorizontalScrollbar;
            float fittedViewportHeight = Mathf.Max(
                1f,
                outRect.height - (needsHorizontalScrollbar ? HorizontalScrollbarHeight : 0f));

            bool contentHeightChanged = _lastScrollContentHeight >= 0f &&
                Mathf.Abs(contentHeight - _lastScrollContentHeight) > 0.5f;
            _lastScrollContentHeight = contentHeight;
            bool rawVerticalOverflow = contentHeight > fittedViewportHeight + 0.5f;
            if (!_hasStableVerticalScrollbarState ||
                !preserveScrollbarVisibility ||
                contentHeightChanged)
            {
                _stableVerticalScrollbarVisible = rawVerticalOverflow;
                _hasStableVerticalScrollbarState = true;
            }

            // Expand-beside only changes columns. While the bottom-anchored window catches up,
            // keep the vertical scrollbar in its pre-transition state unless row content really
            // changed. Otherwise Unity briefly measures the same pawn grid against the stale
            // viewport height and flashes a vertical scrollbar during open/close.
            contentHeight = _stableVerticalScrollbarVisible
                ? Mathf.Max(contentHeight, fittedViewportHeight + 1f)
                : fittedViewportHeight;
            viewRect = new Rect(0f, 0f, viewWidth, contentHeight);

        }

        private WorkTabLayoutColumn? FindNameColumn(IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Column?.Worker is PawnColumnWorker_Label)
                {
                    return columns[i];
                }
            }

            return null;
        }


        private bool TryGetBodyColumnAt(IWorkTabLayoutController layout, Vector2 mousePosition, out WorkTabLayoutColumn column)
        {
            return _workGridInteractionRouter.TryGetBodyColumnAt(layout, mousePosition, out column);
        }

        private bool TryGetRowAt(IWorkTabLayoutController layout, Vector2 mousePosition, out WorkTabLayoutRow row)
        {
            return _workGridInteractionRouter.TryGetRowAt(layout, mousePosition, out row);
        }

        private void DrawDividerRow(PawnDivider divider, Rect rowRect, WorkTabLayoutColumn nameColumn)
        {
            Rect cellRect = new Rect(nameColumn.OffsetX, rowRect.y, nameColumn.Width, rowRect.height);
            DrawDividerToggle(divider, cellRect);
            DrawDividerLabel(divider, cellRect);
        }

        private void DrawDividerToggle(PawnDivider divider, Rect labelCellRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.allowDividerCollapse ?? true) ||
                TimePriorityScheduleEditor.IsTransientDivider(divider))
            {
                return;
            }

            Rect arrowRect = new Rect(labelCellRect.xMin + 6f, labelCellRect.y + (labelCellRect.height - 16f) / 2f, 18f, 16f);
            string arrowChar = divider.IsCollapsed ? "▶" : "▼";
            if (Widgets.ButtonInvisible(arrowRect))
            {
                ToggleDividerCollapsed(divider);
                
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
                if (MultiplayerBridge.Active)
                    LayoutSharingManager.NotifyLayoutChanged();
#endif
            }
            var originalAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(arrowRect, arrowChar);
            Text.Anchor = originalAnchor;
            TooltipHandler.TipRegion(arrowRect, divider.IsCollapsed ? "Expand section" : "Collapse section");
        }

        private void ToggleDividerCollapsed(PawnDivider divider)
        {
            if (divider == null)
            {
                return;
            }

            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableDividers ?? true) || !(settings?.allowDividerCollapse ?? true))
            {
                return;
            }

            divider.IsCollapsed = !divider.IsCollapsed;
            DividerCollapseAnimationState.Start(divider, divider.IsCollapsed);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        private void DrawPawnRow(
            PawnTable table,
            Pawn pawn,
            Rect rowRect,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            int rowIndex,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            bool scheduleOpen = FluffyTimeScheduleAssigner.IsOpen;
            WorkTypeDef expandedParentPriorityWorkType = null;
            int expandedParentPriority = WorkPrioritySystem.DisabledPriority;
            snapshotLayer?.BeginRow();
            try
            {
                for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                {
                    WorkTabLayoutColumn column = columns[columnIndex];
                    if (snapshotLayer != null && !snapshotLayer.ShouldVisitCell(rowIndex, columnIndex))
                    {
                        continue;
                    }

                    float animatedOffset = ColumnReorderAnimationState.GetCellOffset(column);
                    Rect cellRect = new Rect(column.OffsetX + animatedOffset, rowRect.y, column.Width, rowRect.height);

                    if (snapshotLayer != null && snapshotLayer.TryDrawCell(rowIndex, columnIndex, cellRect))
                    {
                        continue;
                    }

                    // Expand-beside children are owned by BWT. Sending them through the
                    // vanilla WorkPriority worker only for Harmony to intercept and route
                    // them back here adds a prefix, global drawing scope, and virtual call
                    // per cell. Focus-view columns still use the worker because their
                    // parent/sub-work crossfade is implemented by that patch.
                    if (column.IsExpandBesideChild &&
                        SubWorkDrilldownState.TryGetWorkGiverForColumn(
                            column,
                            out WorkGiver expandedWorkGiver,
                            out WorkTypeDef expandedParentWorkType,
                            out _))
                    {
                        if (expandedParentPriorityWorkType != expandedParentWorkType)
                        {
                            expandedParentPriorityWorkType = expandedParentWorkType;
                            expandedParentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(
                                pawn,
                                expandedParentWorkType);
                        }

                        WorkGiverPriorityBoxRenderer.DrawPriorityBox(
                            expandedWorkGiver,
                            expandedParentWorkType,
                            pawn,
                            WorkPriorityCellGeometry.GetFluffyStyleSubWorkPriorityBoxRect(cellRect),
                            knownParentPriority: expandedParentPriority);
                        continue;
                    }

                    if (scheduleOpen &&
                        !FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column) &&
                        column.Column?.workType != null &&
                        FluffyTimeScheduleAssigner.TryDrawWorkTypeCell(cellRect, pawn, column.Column.workType))
                    {
                        continue;
                    }

                    column.Column.Worker.DoCell(cellRect, pawn, table);
                }
            }
            finally
            {
                snapshotLayer?.EndRow();
            }
        }

        private void DrawPawnRowOverlay(Pawn pawn, Rect rowRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (pawn == null)
            {
                return;
            }

            if (RuleBuilderGateway.ShouldHighlightRuleBuilder2Pawn(pawn))
            {
                HighlightDrawer.DrawHighlight(rowRect, new Color(1f, 0.82f, 0.18f, 0.18f));
            }

            if (Find.Selector.IsSelected(pawn) && (settings?.DoSelectedPawnHighlight ?? true))
            {
                HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetSelectedPawnColor());
            }

            if ((settings?.enableRowColumnHighlights ?? true) &&
                !BWTWorkTabTutorial.OwnsCurrentPointer &&
                Mouse.IsOver(rowRect))
            {
               // Custom row highlight is drawn in DrawAllHighlights (Phase 1).
               // We don't draw vanilla highlight here to avoid yellow overlay.
            }

            if (pawn.Downed)
            {
                GUI.color = new Color(1f, 0f, 0f, 0.5f);
                Widgets.DrawLineHorizontal(rowRect.xMin, rowRect.center.y, rowRect.width);
                GUI.color = Color.white;
            }
        }

        private void DrawDividerLabel(PawnDivider divider, Rect labelCellRect)
        {
            if (!divider.ShowLabel)
            {
                return;
            }

            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.showDividerLabels ?? true))
            {
                return;
            }

            // Skip drawing label if the divider is too small to contain it reasonably
            if (labelCellRect.height < 14f) 
            {
                return;
            }

            var originalAnchor = Text.Anchor;
            var originalFont = Text.Font;
            var originalColor = GUI.color;
            bool originalWordWrap = Text.WordWrap;

            try
            {
                GUI.color = Spine.UI.TextColorHelper.GetContrastingTextColor(divider.DividerColor);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = divider.LabelFont;

                // Increase the left indent to match pawn name padding
                // Only indent for the arrow if collapse is allowed
                bool hasCollapseToggle = (settings?.allowDividerCollapse ?? true) &&
                    !TimePriorityScheduleEditor.IsTransientDivider(divider);
                float indent = hasCollapseToggle ? 33f : 6f;
                labelCellRect.xMin += indent; 

                if (labelCellRect.width > 4f)
                {
                    string label = divider.DividerName ?? "Divider";
                    Text.WordWrap = false;
                    Text.Font = SelectDividerLabelFont(label, labelCellRect, divider.LabelFont);
                    label = TrimDividerLabelToFit(label, labelCellRect);
                    Widgets.Label(labelCellRect, label);
                }
            }
            finally
            {
                Text.Anchor = originalAnchor;
                Text.Font = originalFont;
                GUI.color = originalColor;
                Text.WordWrap = originalWordWrap;
            }
        }

        private static GameFont SelectDividerLabelFont(string label, Rect rect, GameFont preferredFont)
        {
            GameFont originalFont = Text.Font;
            try
            {
                foreach (GameFont font in DividerLabelFontCandidates(preferredFont))
                {
                    Text.Font = font;
                    Vector2 size = Text.CalcSize(label);
                    if (size.x <= rect.width && size.y <= rect.height + 2f)
                    {
                        return font;
                    }
                }

                return GameFont.Tiny;
            }
            finally
            {
                Text.Font = originalFont;
            }
        }

        private static IEnumerable<GameFont> DividerLabelFontCandidates(GameFont preferredFont)
        {
            if (preferredFont == GameFont.Medium)
            {
                yield return GameFont.Medium;
                yield return GameFont.Small;
                yield return GameFont.Tiny;
                yield break;
            }

            if (preferredFont == GameFont.Small)
            {
                yield return GameFont.Small;
                yield return GameFont.Tiny;
                yield break;
            }

            yield return GameFont.Tiny;
        }

        private static string TrimDividerLabelToFit(string label, Rect rect)
        {
            if (string.IsNullOrEmpty(label) || Text.CalcSize(label).x <= rect.width)
            {
                return label;
            }

            const string suffix = "...";
            float suffixWidth = Text.CalcSize(suffix).x;
            if (suffixWidth >= rect.width)
            {
                return string.Empty;
            }

            int low = 0;
            int high = label.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = label.Substring(0, mid) + suffix;
                if (Text.CalcSize(candidate).x <= rect.width)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low <= 0 ? suffix : label.Substring(0, low) + suffix;
        }
        
        private void DrawManualPrioritiesCheckbox()
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableUIElements ?? true) || !(settings?.showManualPrioritiesCheckbox ?? true))
            {
                return;
            }

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Rect rect = new Rect(5f, 5f, 140f, 30f);
            int maxPriority = WorkPrioritySystem.GetMaxPriority();
            EnsureUiTextCache(maxPriority);
            bool wasEnabled = Current.Game.playSettings.useWorkPriorities;
            Widgets.CheckboxLabeled(rect, _manualPrioritiesText, ref Current.Game.playSettings.useWorkPriorities);
            bool isEnabled = Current.Game.playSettings.useWorkPriorities;
            if (wasEnabled != isEnabled)
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                {
                    if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                    {
                        pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                    }
                }

                WorkTabInvalidationHub.Invalidate(
                    WorkTabDirtyFlags.Priority | WorkTabDirtyFlags.Presentation);
            }
            if (Current.Game.playSettings.useWorkPriorities)
            {
                Color previousHelpColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                float helpWidth = maxPriority > 4 ? 220f : rect.width;
                Widgets.Label(new Rect(rect.x, rect.yMax - 6f, helpWidth, 60f), _priorityHelpText);
                GUI.color = previousHelpColor;
            }
            else
            {
                UIHighlighter.HighlightOpportunity(rect, "ManualPriorities-Off");
            }
        }

        private void DrawPriorityLegend(Rect rect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableUIElements ?? true) || !(settings?.showPriorityLegend ?? true))
            {
                return;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            EnsureUiTextCache(WorkPrioritySystem.GetMaxPriority());
            Widgets.Label(new Rect(370f, rect.y + 5f, 160f, 30f), _higherPriorityText);
            Widgets.Label(new Rect(630f, rect.y + 5f, 160f, 30f), _lowerPriorityText);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void EnsureUiTextCache(int maxPriority)
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            if (_cachedUiTextLanguage == language && _cachedUiTextMaxPriority == maxPriority)
            {
                return;
            }

            _cachedUiTextLanguage = language;
            _cachedUiTextMaxPriority = maxPriority;
            _manualPrioritiesText = "ManualPriorities".Translate();
            _priorityHelpText = maxPriority > 4
                ? "BWT_PriorityOneDoneFirstExtended".Translate(maxPriority)
                : "PriorityOneDoneFirst".Translate();
            _higherPriorityText = "<= " + "HigherPriority".Translate();
            _lowerPriorityText = "LowerPriority".Translate() + " =>";
        }

        private void DrawContextSettingsHint(Rect inRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableUIElements ?? true) || !(settings?.showContextSettingsHint ?? true))
            {
                return;
            }

            const float width = 230f;
            float topRightReservedWidth = HeaderButtons.GetTopRightReservedWidth();
            Rect hintRect = new Rect(inRect.xMax - width - 42f - topRightReservedWidth, inRect.y + 5f, width, 24f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(1f, 1f, 1f, 0.42f);
            Widgets.Label(hintRect, "Alt + click anywhere for settings");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawBottomRightButtons(IWorkTabLayoutController layout, Rect inRect, Rect gearRect)
        {
            HeaderButtons.DrawBottomRightGrouped(inRect, gearRect);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.72f);
            Text.Anchor = TextAnchor.LowerLeft;

            var settings = BetterWorkTabMod.Settings;
            if ((settings?.enableUIElements ?? true) &&
                (settings?.showDragInstructions ?? DefaultSettings.showDragInstructions))
            {
                var instructions = new List<string>();
                if (settings?.enableSkillOverlayFeature ?? DefaultSettings.enableSkillOverlayFeature)
                {
                    instructions.Add(
                        ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted
                            ? "BWT_Footer_ReleaseShiftForPriorities".Translate()
                            : "BWT_Footer_HoldShiftForSkills".Translate());
                }

                bool pointerAvailable = layout != null &&
                                        Event.current != null &&
                                        Mouse.IsOver(inRect) &&
                                        !BWTWorkTabTutorial.OwnsCurrentPointer &&
                                        !RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab &&
                                        !FluffyTimeScheduleAssigner.IsOpen &&
                                        !(PawnOrganizerSystem.Instance?.IsDragging ?? false);
                if (pointerAvailable)
                {
                    Vector2 mousePosition = Event.current.mousePosition;
                    if (TimePriorityScheduleEditor.HasToggleTargetAt(layout, mousePosition))
                    {
                        instructions.Add("BWT_Footer_CtrlClickSchedule".Translate());
                    }
                    else if (SubWorkDrilldownInput.IsEnabled)
                    {
                        bool hasDrilldownAction;
                        string action;
                        if (SubWorkDrilldownState.IsActive)
                        {
                            hasDrilldownAction = TryGetSubWorkExitTarget(layout, mousePosition, out _, out _);
                            action = "BWT_Footer_BackToWorkTypes".Translate();
                        }
                        else
                        {
                            hasDrilldownAction = TryGetSubWorkOpenTarget(
                                layout,
                                mousePosition,
                                out _,
                                out _,
                                out _);
                            action = "BWT_Footer_OpenSpecificJobs".Translate();
                        }

                        if (hasDrilldownAction)
                        {
                            instructions.Add(
                                "BWT_Footer_GestureAction".Translate(
                                    SubWorkDrilldownInput.GestureLabel().CapitalizeFirst(),
                                    action));
                        }
                    }
                }

                if (instructions.Count > 0)
                {
                    HeaderButtons.BottomButtonRects buttonRects = HeaderButtons.GetBottomButtonRects(inRect, gearRect);
                    float textRight = gearRect.x - 8f;
                    if (buttonRects.HasRuleset)
                    {
                        textRight = Mathf.Min(textRight, buttonRects.RulesetMain.x - 8f);
                    }
                    if (buttonRects.HasWorkload)
                    {
                        textRight = Mathf.Min(textRight, buttonRects.WorkloadMain.x - 8f);
                    }

                    Rect textRect = new Rect(
                        inRect.x + 6f,
                        inRect.y,
                        Mathf.Max(0f, textRight - inRect.x - 6f),
                        inRect.height);
                    Widgets.Label(textRect, string.Join(" | ", instructions));
                }
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawInfoButton(Rect gearRect)
        {
            if (Widgets.ButtonImage(gearRect, RimWorld.TexButton.Info))
            {
                OpenBetterWorkTabSettings();
            }
        }

        internal static bool OpenBetterWorkTabSettings(bool toggleExisting = true)
        {
            if (toggleExisting && Find.WindowStack != null && Find.WindowStack.TryRemove(typeof(Dialog_ModSettings)))
            {
                return false;
            }

#if v0_16
            Find.WindowStack.Add(new Dialog_ModSettings());
            return true;
#else
            var mod = LoadedModManager.GetMod<BetterWorkTabMod>();
            if (mod != null)
            {
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
                var dialog = new Dialog_ModSettings();
                typeof(Dialog_ModSettings).GetField("selMod", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(dialog, mod);
                Find.WindowStack.Add(dialog);
#else
                Find.WindowStack.Add(new Dialog_ModSettings(mod));
#endif
                return true;
            }
#endif

            return false;
        }

        private static void HideContextSettingsHintAfterFirstUse()
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.showContextSettingsHint ?? false))
            {
                return;
            }

            settings.showContextSettingsHint = false;
            settings.Write();
        }

        private void DrawSubWorkExitButton(Rect inRect)
        {
            if (!SubWorkDrilldownState.IsActive)
            {
                return;
            }

            const float buttonSize = 24f;
            float topRightReservedWidth = HeaderButtons.GetTopRightReservedWidth();
            Rect exitRect = new Rect(
                inRect.xMax - buttonSize - RightEdgeMargin - topRightReservedWidth,
                inRect.y + 8f,
                buttonSize,
                buttonSize);

            if (Widgets.ButtonImage(exitRect, RimWorld.TexButton.CloseXSmall, Color.white, GenUI.MouseoverColor))
            {
                ExitSubWorkDrilldown(restoreMousePosition: false);
            }

            TooltipHandler.TipRegion(exitRect, "Back to work types. " + SubWorkDrilldownInput.GestureLabel() + " or press Escape to return.");
        }

        private static Rect GetInfoIconRect(Rect inRect)
        {
            return new Rect(
                inRect.xMax - InfoIconSize - RightEdgeMargin,
                inRect.yMax - InfoIconSize - 10f,
                InfoIconSize,
                InfoIconSize);
        }

        private void DrawBottomCounters(Rect inRect, PawnTable table)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableUIElements ?? true))
            {
                return;
            }

            bool showPawns = settings.showPawnCountAtBottom;
            bool showBeds = settings.showBedCountAtBottom;
            if (!showPawns && !showBeds)
            {
                return;
            }

            int pawnCount = showPawns ? PawnTableCompat.GetPawnCount(table) : 0;

            // Use cached bed count instead of calculating every frame
            int bedCount = 0;
            if (showBeds)
            {
                Map map = Find.CurrentMap;
                // Cached lookup: invalidated via Harmony patches and time-based expiry
                bedCount = BedCountCache.GetBedCount(map);
            }

            var rect = new Rect(inRect.x + 6f, inRect.yMax - 45f, inRect.width * 0.5f, 20f);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;

            // Draw colonist count in gray
            if (showPawns)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                Widgets.Label(rect, $"Colonists: {pawnCount}");
            }

            // Draw bed count in red if insufficient, otherwise gray
            if (showBeds)
            {
                string bedLabel = showPawns ? $" | Beds: {bedCount}" : $"Beds: {bedCount}";
                float colonistWidth = showPawns ? Text.CalcSize($"Colonists: {pawnCount}").x : 0f;
                Rect bedRect = new Rect(rect.x + colonistWidth, rect.y, rect.width - colonistWidth, rect.height);

                // Red if fewer beds than pawns, gray otherwise
                if (bedCount < pawnCount)
                {
                    GUI.color = new Color(0.8f, 0.1f, 0.1f); // Dark red
                }
                else
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.7f);
                }

                Widgets.Label(bedRect, bedLabel);
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private PawnTable GetPawnTable()
        {
            Game game = Current.Game;
            if (_cachedPawnTable == null || !ReferenceEquals(_cachedPawnTableGame, game))
            {
                _cachedPawnTable = (PawnTable)PawnTableField?.GetValue(this);
                _cachedPawnTableGame = game;
            }

            return _cachedPawnTable;
        }

        public override void PreClose()
        {
            base.PreClose();
            HighlightManager.ClearHighlight();
            _lastSortColumn = null;
            _lastSortDescending = false;
            WorkTabProfilingState.NotifyOpen(false);
            
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
            // Clear float menu highlights when Work tab is closed
            HighlightState.ClearWorktypeHighlight();
            MouseStateManager.ClearHover();
            SubWorkDrilldownState.ExitImmediate();
            FluffyTimeScheduleAssigner.Close();
            _workGridInteractionRouter.ResetSessions();

            // Cancel any active drag operations to ensure priority editing is re-enabled
            PawnOrganizerSystem.Instance?.CancelActiveDrag();
            CaptureWarmOpenTableState(GetPawnTable());
        }
    }
}
