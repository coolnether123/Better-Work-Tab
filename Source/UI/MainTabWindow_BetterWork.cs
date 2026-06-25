using Better_Work_Tab.Features;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts;
using Better_Work_Tab.Features.Caching;
using Better_Work_Tab.Features.Dividers;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Testing;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Input;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Multiplayer.API;
using RimWorld;
using Spine.Profiling;
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
    public class MainTabWindow_BetterWork : MainTabWindow_Work
    {
        private static PawnColumnDef _lastDraggedColumn;
        
        /// <summary>
        /// Global notification that header settings (like rotation) have changed.
        /// Flushes all layout and drawing caches.
        /// </summary>
        public static void NotifyAngledHeadersChanged()
        {
            HeaderDrawingCoordinator.NotifyAngledHeadersChanged(); 
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

        protected override float ExtraTopSpace =>
            Mathf.Clamp(
                BetterWorkTabMod.Settings?.workTabTopSpace ?? DefaultSettings.workTabTopSpace,
                0f,
                80f);

        private PawnColumnDef _lastSortColumn;
        private bool _lastSortDescending;
        private bool _pendingSubWorkGesture;
        private Vector2 _pendingSubWorkStart;
        private Rect _pendingSubWorkBounds;
        private WorkTypeDef _pendingSubWorkOpenType;
        private int _pendingSubWorkButton;
        private bool _pendingSubWorkExit;
        private bool _pendingSubWorkRestoreCursor;
        private int _pendingSubWorkExitColumnSlot = -1;
        private float _pendingSubWorkExitWaveSlotPosition = -1f;
        private int _suppressSubWorkPriorityMouseDownFrame = -1;

        private static Color CurrentRowTextColor = Color.white;

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
            if (BWTBetaTutorial.TryHandleAcceptKey())
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

            bool dividerAnimationChanged = DividerCollapseAnimationState.Tick();
            dividerAnimationChanged |= DividerInsertionAnimationState.Tick();
            if (dividerAnimationChanged)
            {
                PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
            }

            SubWorkDrilldownState.TickTransition();
            if (SubWorkDrilldownState.ConsumeLayoutRefresh())
            {
                HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
                PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();
                SetDirty();
            }

            var organizer = PawnOrganizerSystem.Instance;
            Vector2 tableOrigin = new Vector2(inRect.x, inRect.y + ExtraTopSpace);
            var snapshot = BuildSnapshotForOrganizer(table);

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
            }

            ResizeWindowBottomAnchoredIfRequestedSizeChanged();
            TimePriorityPlannerPrototype.TryOpenAgentRequestedSession(organizer?.Layout);

            Event evt = Event.current;
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Input", () =>
                    {
                        bool handledSubWorkGesture = BWTBetaTutorial.TryHandleInput(inRect, organizer?.Layout, evt)
                            || TryHandleContextSettingsClick(inRect, organizer?.Layout, evt)
                            || TimePriorityPlannerPrototype.TryHandleInput(organizer?.Layout, evt)
                            || TryHandleSubWorkExitGesture(organizer?.Layout)
                            || TryHandleSubWorkHeaderOpen(organizer?.Layout);
                        if (!handledSubWorkGesture)
                        {
                            ProcessRightClicks(organizer?.Layout);
                            if (evt.type != EventType.Used)
                            {
                                organizer?.HandleInput(evt);
                            }
                        }
                    });
                }
                else
                {
                    bool handledSubWorkGesture = BWTBetaTutorial.TryHandleInput(inRect, organizer?.Layout, evt)
                        || TryHandleContextSettingsClick(inRect, organizer?.Layout, evt)
                        || TimePriorityPlannerPrototype.TryHandleInput(organizer?.Layout, evt)
                        || TryHandleSubWorkExitGesture(organizer?.Layout)
                        || TryHandleSubWorkHeaderOpen(organizer?.Layout);
                    if (!handledSubWorkGesture)
                    {
                        ProcessRightClicks(organizer?.Layout);
                        if (evt.type != EventType.Used)
                        {
                            organizer?.HandleInput(evt);
                        }
                    }
                }

                SuppressSubWorkPriorityMouseDownIfNeeded(evt);
            }

            DrawWorkTable(table, organizer?.Layout, inRect);

            TimePriorityPlannerPrototype.Draw(organizer?.Layout);

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawDragOverlays", () => organizer?.DrawDragOverlays());
                SpineTiming.Time("WorkTab.DrawManualPrioritiesCheckbox", DrawManualPrioritiesCheckbox);
                SpineTiming.Time("WorkTab.DrawPriorityLegend", () => DrawPriorityLegend(inRect));
                SpineTiming.Time("WorkTab.DrawContextSettingsHint", () => DrawContextSettingsHint(inRect));
            }
            else
            {
                organizer?.DrawDragOverlays();
                DrawManualPrioritiesCheckbox();
                DrawPriorityLegend(inRect);
                DrawContextSettingsHint(inRect);
            }

            bool mouseInside = Mouse.IsOver(inRect);
            Rect infoRect = GetInfoIconRect(inRect);
            DrawBottomRightButtons(inRect, infoRect);
            if (mouseInside)
            {
                DrawInfoButton(infoRect);
            }

            DrawSubWorkExitButton(inRect);
            DrawBottomCounters(inRect, table);
            BWTBetaTutorial.TickAndDraw(inRect, organizer?.Layout);
            NativeCursorPosition.ProcessPendingMove();
            NativeCursorPosition.DrawPendingMoveCue();
        }

        private void UpdateTutorialAcceptKeyState()
        {
            // Keep RimWorld's accept-key dispatch enabled. The override above
            // consumes Enter while the beta tutorial is active and otherwise
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

            Rect rect = windowRect;
            if (!force &&
                Mathf.Abs(rect.width - requestedSize.x) < 0.5f &&
                Mathf.Abs(rect.height - requestedSize.y) < 0.5f)
            {
                return;
            }

            float screenBottom = Verse.UI.screenHeight - 35f;
            rect.width = requestedSize.x;
            rect.height = requestedSize.y;
            rect.y = Mathf.Max(0f, screenBottom - rect.height);
            windowRect = rect;
        }

        private IPawnOrganizerSnapshot BuildSnapshotForOrganizer(PawnTable table)
        {
            var pawns = new List<Pawn>(table.PawnsListForReading);
            var comp = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            var settings = BetterWorkTabMod.Settings;
            bool useDividers = (settings?.enableDividers ?? true) && (settings?.showDividers ?? true);
            var dividers = useDividers ? comp?.ActiveDividers ?? new List<PawnDivider>() : new List<PawnDivider>();
            return new WorkTabSnapshot(pawns, dividers);
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

            if (!layout.TryGetRowAt(evt.mousePosition, out var row))
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
                new FloatMenuOption("Change title...", () => ShowRenamePawnDialog(pawn)),
                new FloatMenuOption("Set background color...", () => ShowBackgroundColorPicker(pawn))
            };

            if (PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out _))
            {
                options.Add(new FloatMenuOption("Clear background color", () =>
                {
                    PawnOrganizer.API.PawnColorDatabase.ClearColor(pawn);
                }));
            }
            
            // Multiplayer follow mode: Copy this pawn row
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    $"Copy {pawn.NameShortColored} row position to my layout (stop following)",
                    () => LayoutSharingManager.CopyPawnRowToLocalAndStop(pawn)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowRenamePawnDialog(Pawn pawn)
        {
            Find.WindowStack.Add(pawn.NamePawnDialog());
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
            if (LayoutSharingManager.IsFollowing)
            {
                options.Add(new FloatMenuOption(
                    "Copy this divider to my layout (stop following)",
                    () => LayoutSharingManager.CopyDividerToLocalAndStop(divider)));
            }

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
                    float pinnedRowsHeight = GetPinnedRowsHeight();
                    float layoutHeight = organizer.Layout.HeaderHeight + pinnedRowsHeight + organizer.Layout.ContentHeight;
                    finalHeight = layoutHeight + ExtraBottomSpace + ExtraTopSpace + Margin * 2f;
                    finalWidth = table.Size.x + Margin * 2f + 25f; // Added 20f to stop headers from clipping edge
                }
                else
                {
                    // Fallback to vanilla size if organizer not ready
                    finalHeight = table.Size.y + ExtraBottomSpace + ExtraTopSpace + Margin * 2f;
                    finalWidth = table.Size.x + Margin * 2f + 25f; // Same as above
                }

                // Determine max height: use setting if configured, otherwise vanilla default (fill screen)
                float configuredMaxHeight = BetterWorkTabMod.Settings.workTabMaxHeight;
                float targetMaxHeight = configuredMaxHeight > 0f
                    ? Mathf.Max(configuredMaxHeight, MinWorkTabHeight)
                    : Mathf.Max(Verse.UI.screenHeight - 35f, MinWorkTabHeight);  // Vanilla default: screen height minus top bar

                finalHeight = Mathf.Min(finalHeight, targetMaxHeight);

                return new Vector2(finalWidth, finalHeight);
            }
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
            ResizeWindowBottomAnchoredIfRequestedSizeChanged(force: true);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

            if (MultiplayerBridge.Active)
                LayoutSharingManager.NotifyLayoutChanged();
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

        private void DrawWorkTable(PawnTable table, IWorkTabLayoutController layout, Rect inRect)
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

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawHeaders", () => DrawHeaders(layout, table));
            }
            else
            {
                DrawHeaders(layout, table);
            }
            if (SubWorkDrilldownState.IsActive)
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
            WorkTabGeometryDiagnostics.DumpHeaderLayoutIfRequested(layout);
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawRows", () => DrawRows(table, layout, outRect, viewRect));
                SpineTiming.Time("WorkTab.DrawSubWorkTransitionWave", () => DrawSubWorkTransitionPixelWave(layout));
            }
            else
            {
                DrawRows(table, layout, outRect, viewRect);
                DrawSubWorkTransitionPixelWave(layout);
            }
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
            float totalHeight = pinnedRowsHeight + layout.ContentHeight;

            foreach (var column in layout.Columns)
            {
                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;
                var workType = column.Column?.workType;
                Rect headerRect = GetAnimatedHeaderRect(column);
                bool timePriorityOwnsMouse = TimePriorityPlannerPrototype.OwnsCurrentMousePosition;
                bool timePrioritySourceColumn = isWorkColumn && TimePriorityPlannerPrototype.ShouldHighlightSourceColumn(column);

                if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight &&
                    isWorkColumn &&
                    (timePrioritySourceColumn || (!timePriorityOwnsMouse && Mouse.IsOver(headerRect))))
                {
                    Color useColor = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
                    Rect columnRect = new Rect(headerRect.x, layout.TableOrigin.y + layout.HeaderHeight, column.Width, totalHeight);
                    Widgets.DrawBoxSolid(columnRect, useColor);
                }

                if (isWorkColumn)
                {
                    DrawSubWorkBlankTransitionFlash(layout, column, headerRect, totalHeight);
                }

                column.Column.Worker.DoHeader(headerRect, table);
            }
        }

        private static Rect GetAnimatedHeaderRect(WorkTabLayoutColumn column)
        {
            float offset = ColumnReorderAnimationState.GetHeaderOffset(column);
            return Mathf.Abs(offset) > 0.01f
                ? new Rect(column.HeaderRect.x + offset, column.HeaderRect.y, column.HeaderRect.width, column.HeaderRect.height)
                : column.HeaderRect;
        }

        private static float GetPinnedRowsHeight()
        {
            float height = TimePriorityPlannerPrototype.HeaderPinnedRowsHeight;
            if (SubWorkDrilldownState.IsActive)
            {
                height += SubWorkDrilldownBarRenderer.ReservedRowHeight;
            }

            return height;
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
                for (float x = startX; x < endX; x += 1f)
                {
                    float sampleX = x + 0.5f;
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
                        new Rect(x, waveRect.yMin, 1f, waveRect.height),
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

        private bool TryHandleSubWorkHeaderOpen(IWorkTabLayoutController layout)
        {
            if (layout == null || SubWorkDrilldownState.IsActive)
            {
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
            ClearPendingSubWorkGesture();

            if (!shouldOpen)
            {
                return false;
            }

            TimePriorityPlannerPrototype.CloseForWorkModeTransition();
            SubWorkDrilldownState.Enter(
                openType,
                storedReturnPosition,
                SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(layout.Table, layout.HeaderHeight));
            HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }

        private bool TryHandleSubWorkExitGesture(IWorkTabLayoutController layout)
        {
            if (!SubWorkDrilldownState.IsActive)
            {
                return false;
            }

            Event evt = Event.current;
            if (evt == null)
            {
                return false;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                SubWorkDrilldownBarRenderer.ExitDrilldown();
                evt.Use();
                return true;
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
                        out bool shouldRestoreCursor,
                        out int detectedExitColumnSlot,
                        out float detectedExitWaveSlotPosition))
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
                _pendingSubWorkExitColumnSlot = detectedExitColumnSlot;
                _pendingSubWorkExitWaveSlotPosition = detectedExitWaveSlotPosition;
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
            int pendingExitColumnSlot = _pendingSubWorkExitColumnSlot;
            float pendingExitWaveSlotPosition = _pendingSubWorkExitWaveSlotPosition;
            ClearPendingSubWorkGesture();

            if (!shouldExit)
            {
                return false;
            }

            SubWorkDrilldownBarRenderer.ExitDrilldown(
                restoreMousePosition: pendingRestoreCursor,
                exitWorkColumnSlot: pendingExitColumnSlot,
                exitWaveSlotPosition: pendingExitWaveSlotPosition);
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
            if (layout.TryGetRowAt(mousePosition, out bodyRow) &&
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

            if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority) || column.Column.workType == null)
            {
                return false;
            }

            if (WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(column.Column.workType).Count == 0)
            {
                return false;
            }

            workType = column.Column.workType;
            bounds = candidateBounds;
            fromHeader = isHeader;
            return true;
        }

        private bool TryGetSubWorkExitTarget(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out Rect bounds,
            out bool restoreCursor,
            out int exitColumnSlot,
            out float exitWaveSlotPosition)
        {
            bounds = default;
            restoreCursor = false;
            exitColumnSlot = -1;
            exitWaveSlotPosition = -1f;

            Rect headerArea = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y,
                layout.Table.Size.x,
                layout.HeaderHeight);

            if (headerArea.Contains(mousePosition))
            {
                bounds = headerArea;
                restoreCursor = BetterWorkTabMod.Settings?.restoreCursorOnSubWorkExit ?? true;
                exitColumnSlot = GetSubWorkColumnSlotAt(layout, mousePosition);
                exitWaveSlotPosition = GetSubWorkColumnWavePositionAt(layout, mousePosition, exitColumnSlot);
                return true;
            }

            Rect globalRowArea = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight + TimePriorityPlannerPrototype.HeaderPinnedRowsHeight,
                layout.Table.Size.x,
                SubWorkDrilldownBarRenderer.RowHeight);

            if (globalRowArea.Contains(mousePosition) &&
                TryGetGlobalPriorityBoxHit(layout, globalRowArea, mousePosition, out var globalColumn, out Rect globalPriorityBoxRect))
            {
                bounds = globalPriorityBoxRect;
                restoreCursor = false;
                exitColumnSlot = SubWorkDrilldownState.GetVisibleWorkColumnSlot(globalColumn.Column);
                exitWaveSlotPosition = GetSubWorkColumnWavePositionAt(layout, mousePosition, exitColumnSlot);
                return true;
            }

            WorkTabLayoutRow bodyRow;
            WorkTabLayoutColumn column;
            if (layout.TryGetRowAt(mousePosition, out bodyRow) &&
                TryGetBodyColumnAt(layout, mousePosition, out column) &&
                TryGetPriorityBoxHit(layout, bodyRow, column, mousePosition, out Rect bodyPriorityBoxRect))
            {
                bounds = bodyPriorityBoxRect;
                restoreCursor = BetterWorkTabMod.Settings?.restoreCursorOnSubWorkPawnCellExit ?? false;
                exitColumnSlot = SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column);
                exitWaveSlotPosition = GetSubWorkColumnWavePositionAt(layout, mousePosition, exitColumnSlot);
                return true;
            }

            return false;
        }

        private int GetSubWorkColumnSlotAt(IWorkTabLayoutController layout, Vector2 mousePosition)
        {
            if (layout?.Columns == null)
            {
                return -1;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (mousePosition.x >= column.HeaderRect.xMin &&
                    mousePosition.x <= column.HeaderRect.xMax &&
                    column.Column?.Worker is PawnColumnWorker_WorkPriority)
                {
                    return SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column);
                }
            }

            return -1;
        }

        private float GetSubWorkColumnWavePositionAt(IWorkTabLayoutController layout, Vector2 mousePosition, int fallbackSlot)
        {
            if (layout?.Columns == null)
            {
                return fallbackSlot;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                int slot = SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column);
                if (slot < 0)
                {
                    continue;
                }

                if (mousePosition.x >= column.HeaderRect.xMin &&
                    mousePosition.x <= column.HeaderRect.xMax)
                {
                    float local = Mathf.Clamp01((mousePosition.x - column.HeaderRect.xMin) / Mathf.Max(1f, column.HeaderRect.width));
                    return slot + local - 0.5f;
                }
            }

            return fallbackSlot;
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
                    !SubWorkDrilldownState.TryGetWorkGiverForColumn(candidate.Column, out _, out _))
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
            _pendingSubWorkExitColumnSlot = -1;
            _pendingSubWorkExitWaveSlotPosition = -1f;
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

            // First check: was this column directly dragged by the player?
            if (!settings.WasColumnDraggedByPlayer(workType.defName))
                return false;

            // Second check: is it currently out of baseline position?
            return IsColumnOutOfBaselinePosition(workType);
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

            var currentOrder = def.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .Select(c => c.workType.defName)
                .ToList();

            // FILTERED BASELINE: Only compare against columns that are actually present.
            // This prevents columns from being marked 'moved' just because a mod added/removed 
            // a different column that shifted our absolute index.
            var filteredBaseline = baselineOrder.Where(b => currentOrder.Contains(b)).ToList();

            int relVanillaPos = filteredBaseline.IndexOf(workType.defName);
            int currentPos = currentOrder.IndexOf(workType.defName);

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
        [SyncMethod]
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
        }

        /// <summary>
        /// Clears all column markers. Called when resetting to vanilla order.
        /// </summary>
        internal static void ClearAllColumnMarkers()
        {
            var settings = BetterWorkTabMod.Settings;
            settings?.ClearPlayerDraggedColumns();
            settings?.Write();
        }

        public override void PostOpen()
        {
            base.PostOpen();
            SpineTiming.NotifyWorkTabOpen(true);
        }

        private void DrawRows(PawnTable table, IWorkTabLayoutController layout, Rect outRect, Rect viewRect)
        {
            if (layout == null)
            {
                return;
            }

            Widgets.BeginScrollView(outRect, ref table.scrollPosition, viewRect);
            try
            {
                // Get the row descriptors (single source of truth for what rows exist and their heights)
                var rowDescriptors = layout.GetRowDescriptors()?.ToList();
                var columns = layout.Columns?.ToList();
                if (rowDescriptors == null || rowDescriptors.Count == 0 ||
                    columns == null || columns.Count == 0)
                {
                    return;
                }

                var nameColumn = FindNameColumn(columns);

                // Calculate dimensions once for all highlight operations
                float totalWidth = CalculateTotalColumnWidth(columns);
                float totalHeight = layout.ContentHeight; // Already accounts for all descriptor heights

                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawAllHighlights", () => DrawAllHighlights(rowDescriptors, columns, totalWidth, totalHeight));
                    SpineTiming.Time("WorkTab.Rows.DrawAllRowContent", () => DrawAllRowContent(table,
                        rowDescriptors,
                        columns,
                        viewRect.width,
                        nameColumn,
                        outRect,
                        table.scrollPosition));
                    SpineTiming.Time("WorkTab.Rows.DrawRowSeparators", () => DrawRowSeparators(rowDescriptors, viewRect.width));
                }
                else
                {
                    // Phase 1: Draw all highlights (selected, hovered, float menu, similar worktypes)
                    DrawAllHighlights(rowDescriptors, columns, totalWidth, totalHeight);

                    // Phase 2: Draw actual row content (pawn data, divider labels, backgrounds)
                    DrawAllRowContent(table,
                        rowDescriptors,
                        columns,
                        viewRect.width,
                        nameColumn,
                        outRect,
                        table.scrollPosition);

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
            bool timePriorityOwnsMouse = TimePriorityPlannerPrototype.OwnsCurrentMousePosition;

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
                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;
                bool isFloatMenuColumn = isWorkColumn &&
                    IsColumnHighlightedByFloatMenu(column, highlightedWorkType, highlightedWorkGiver);
                bool isTimePrioritySourceColumn = isWorkColumn && TimePriorityPlannerPrototype.ShouldHighlightSourceColumn(column);

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
            if (column.Column?.workType == null || highlightedWorkType == null)
            {
                return false;
            }

            if (SubWorkDrilldownState.IsActive && highlightedWorkGiver != null)
            {
                return SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out var workGiver, out _) &&
                       workGiver?.def == highlightedWorkGiver;
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
            Vector2 scrollOffset)
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
                    DrawSingleRowContent(table, descriptor, columns, rowRect, nameColumn, i);
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
            int rowIndex)
        {
            // Create temporary wrapper objects to maintain compatibility with existing draw methods.
            // These are small allocations; only optimize with pooling if profiling shows it's necessary.
            if (descriptor.IsPawn)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawPawnRowContent", () => DrawPawnRowContent(table, descriptor, columns, rowRect, rowIndex));
                }
                else
                {
                    DrawPawnRowContent(table, descriptor, columns, rowRect, rowIndex);
                }
            }
            else if (descriptor.IsDivider)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.Rows.DrawDividerRowContent", () => DrawDividerRowContent(descriptor, rowRect, nameColumn, rowIndex));
                }
                else
                {
                    DrawDividerRowContent(descriptor, rowRect, nameColumn, rowIndex);
                }
            }
        }

        private void DrawPawnRowContent(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            int rowIndex)
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
                    DrawPawnRowContentUnclipped(table, descriptor, columns, clippedRowRect, rowIndex);
                }
                finally
                {
                    GUI.EndGroup();
                }

                return;
            }

            DrawPawnRowContentUnclipped(table, descriptor, columns, rowRect, rowIndex);
        }

        private void DrawPawnRowContentUnclipped(
            PawnTable table,
            RowDescriptor descriptor,
            IReadOnlyList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            int rowIndex)
        {
            // Wrap pawn in element and row for rendering (preserves selection/highlight state)
            var pawnElement = new PawnElement(descriptor.Pawn);
            var renderRow = new WorkTabLayoutRow(pawnElement, rowRect.y, descriptor.Height, rowIndex);

            // 1. Draw row background (custom pawn color if set)
            DrawRowBackground(renderRow, rowRect);

            // 2. Draw all column cells for this pawn (work priorities, name, etc)
            DrawPawnRow(table, renderRow, rowRect, columns);

            // 3. Draw overlays (selection glow, hover, downed strike-through)
            DrawPawnRowOverlay(renderRow, rowRect);
        }

        private void DrawDividerRowContent(
            RowDescriptor descriptor,
            Rect rowRect,
            WorkTabLayoutColumn? nameColumn,
            int rowIndex)
        {
            // Wrap divider in element and row for rendering (preserves collapse state and styling)
            var dividerElement = new DividerElement(descriptor.Divider);
            var renderRow = new WorkTabLayoutRow(dividerElement, rowRect.y, descriptor.Height, rowIndex);

            // 1. Draw divider background (uses divider color, dimmed if collapsed)
            DrawRowBackground(renderRow, rowRect);

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
            for (int i = 0; i < rowDescriptors.Count; i++)
            {
                var descriptor = rowDescriptors[i];
                currentY += descriptor.Height;

                // Draw horizontal line at the bottom of this row
                Widgets.DrawLineHorizontal(0f, currentY - 1f, viewWidth);
            }
            GUI.color = Color.white;
        }


        private void DrawRowBackground(WorkTabLayoutRow row, Rect rect)
        {
            if (row.Pawn != null && PawnOrganizer.API.PawnColorDatabase.TryGetColor(row.Pawn, out var pawnColor) && pawnColor.a > 0f)
            {
                var overlay = new Color(pawnColor.r, pawnColor.g, pawnColor.b, Mathf.Clamp(pawnColor.a, 0.08f, 0.6f));
                Widgets.DrawBoxSolid(rect, overlay);

                CurrentRowTextColor = Spine.UI.TextColorHelper.GetContrastingTextColor(overlay);
            }
            else if (row.Divider != null)
            {
                var settings = BetterWorkTabMod.Settings;
                var dividerColor = row.Divider.DividerColor;
                if (!(settings?.allowCustomDividerColors ?? true))
                {
                    dividerColor = Color.gray;
                }
                float minAlpha = settings?.dividerMinAlpha ?? 0.35f;
                dividerColor.a = Mathf.Max(dividerColor.a, minAlpha);
                if (row.Divider.IsCollapsed)
                {
                    dividerColor.a = Mathf.Clamp01(dividerColor.a * 0.6f);
                }
                Widgets.DrawBoxSolid(rect, dividerColor);
            }
        }

        private void CalculateScrollRects(IWorkTabLayoutController layout, Rect inRect, out Rect outRect, out Rect viewRect)
        {
            float headerHeight = layout.HeaderHeight;
            float scrollAreaHeight = Mathf.Max(0f,
        inRect.height - ExtraTopSpace - headerHeight - ExtraBottomSpace);

            outRect = new Rect( 
                layout.TableOrigin.x,
                layout.TableOrigin.y + headerHeight,
                layout.Table.Size.x,
                scrollAreaHeight);

            float pinnedRowsHeight = GetPinnedRowsHeight();
            if (pinnedRowsHeight > 0f)
            {
                outRect.y += pinnedRowsHeight;
                outRect.height = Mathf.Max(0f, outRect.height - pinnedRowsHeight);
            }

            float widthWithoutScrollbar = layout.Table.Size.x - 16f;
            float totalColumnWidth = layout.Columns.Count > 0
                ? layout.Columns[layout.Columns.Count - 1].OffsetX + layout.Columns[layout.Columns.Count - 1].Width
                : widthWithoutScrollbar;
            float viewWidth = Mathf.Max(widthWithoutScrollbar, totalColumnWidth);
            float contentHeight = Mathf.Max(layout.ContentHeight, 1f);
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
            column = default;
            if (layout?.Table == null)
            {
                return false;
            }

            float headerTop = layout.TableOrigin.y + layout.HeaderHeight + GetPinnedRowsHeight();
            float bodyBottom = GetVisualTableBottom(layout);
            if (mousePosition.y < headerTop || mousePosition.y > bodyBottom)
            {
                return false;
            }

            float localX = mousePosition.x - layout.TableOrigin.x;
            if (localX < 0f)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var candidate = layout.Columns[i];
                if (localX >= candidate.OffsetX && localX <= candidate.OffsetX + candidate.Width)
                {
                    column = candidate;
                    return true;
                }
            }

            return false;
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
                TimePriorityPlannerPrototype.IsTransientDivider(divider))
            {
                return;
            }

            Rect arrowRect = new Rect(labelCellRect.xMin + 6f, labelCellRect.y + (labelCellRect.height - 16f) / 2f, 18f, 16f);
            string arrowChar = divider.IsCollapsed ? "▶" : "▼";
            if (Widgets.ButtonInvisible(arrowRect))
            {
                ToggleDividerCollapsed(divider);
                
                if (MultiplayerBridge.Active)
                    LayoutSharingManager.NotifyLayoutChanged();
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

        private void DrawPawnRow(PawnTable table, WorkTabLayoutRow row, Rect rowRect, IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            foreach (var column in columns)
            {
                float animatedOffset = ColumnReorderAnimationState.GetCellOffset(column);
                Rect cellRect = new Rect(column.OffsetX + animatedOffset, rowRect.y, column.Width, rowRect.height);
                column.Column.Worker.DoCell(cellRect, row.Pawn, table);
            }
        }

        private void DrawPawnRowOverlay(WorkTabLayoutRow row, Rect rowRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (row.Pawn == null)
            {
                return;
            }

            if (Find.Selector.IsSelected(row.Pawn) && (settings?.DoSelectedPawnHighlight ?? true))
            {
                HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetSelectedPawnColor());
            }

            if ((settings?.enableRowColumnHighlights ?? true) && Mouse.IsOver(rowRect))
            {
               // Custom row highlight is drawn in DrawAllHighlights (Phase 1).
               // We don't draw vanilla highlight here to avoid yellow overlay.
            }

            if (row.Pawn != null && row.Pawn.Downed)
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
                    !TimePriorityPlannerPrototype.IsTransientDivider(divider);
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
            bool wasEnabled = Current.Game.playSettings.useWorkPriorities;
            Widgets.CheckboxLabeled(rect, "ManualPriorities".Translate(), ref Current.Game.playSettings.useWorkPriorities);
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
            }
            if (Current.Game.playSettings.useWorkPriorities)
            {
                using (new TextBlock(new Color(1f, 1f, 1f, 0.5f)))
                {
                    int maxPriority = WorkPrioritySystem.GetMaxPriority();
                    TaggedString priorityHelp = maxPriority > 4
                        ? "BWT_PriorityOneDoneFirstExtended".Translate(maxPriority)
                        : "PriorityOneDoneFirst".Translate();

                    float helpWidth = maxPriority > 4 ? 220f : rect.width;
                    Widgets.Label(new Rect(rect.x, rect.yMax - 6f, helpWidth, 60f), priorityHelp);
                }
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
            Widgets.Label(new Rect(370f, rect.y + 5f, 160f, 30f), "<= " + "HigherPriority".Translate());
            Widgets.Label(new Rect(630f, rect.y + 5f, 160f, 30f), "LowerPriority".Translate() + " =>");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawContextSettingsHint(Rect inRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableUIElements ?? true) || !(settings?.showContextSettingsHint ?? true))
            {
                return;
            }

            const float width = 230f;
            Rect hintRect = new Rect(inRect.xMax - width - 42f, inRect.y + 5f, width, 24f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(1f, 1f, 1f, 0.42f);
            Widgets.Label(hintRect, "Alt + click anywhere for settings");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawBottomRightButtons(Rect inRect, Rect gearRect)
        {
            HeaderButtons.DrawBottomRightGrouped(inRect, gearRect);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.LowerLeft;

            var settings = BetterWorkTabMod.Settings;
            if (settings?.enableUIElements ?? true)
            {
                if (settings.showDragInstructions)
                {
                    bool overlayEnabled = settings.enableSkillOverlayFeature;
                    bool dragEnabled = settings.enableDragDropReordering && (settings.rowDraggingEnabled || settings.columnDraggingEnabled);

                    string overlayText = overlayEnabled ? "Shift toggles overlay" : string.Empty;
                    string dragInstruction = dragEnabled
                        ? (settings.requireCtrlForDrag ? "Ctrl + drag to reorder" : "Drag to reorder")
                        : string.Empty;

                    string combined = string.IsNullOrEmpty(overlayText)
                        ? dragInstruction
                        : (string.IsNullOrEmpty(dragInstruction) ? overlayText : $"{overlayText} | {dragInstruction}");

                    if (!string.IsNullOrEmpty(combined))
                    {
                        Rect textRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
                        Widgets.Label(textRect, combined);
                    }
                }
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawInfoButton(Rect gearRect)
        {
            if (Widgets.ButtonImage(gearRect, TexButton.Info))
            {
                OpenBetterWorkTabSettings();
            }
        }

        private bool OpenBetterWorkTabSettings(bool toggleExisting = true)
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
            Rect exitRect = new Rect(
                inRect.xMax - buttonSize - RightEdgeMargin,
                inRect.y + 8f,
                buttonSize,
                buttonSize);

            if (Widgets.ButtonImage(exitRect, TexButton.CloseXSmall, Color.white, GenUI.MouseoverColor))
            {
                SubWorkDrilldownBarRenderer.ExitDrilldown();
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

            int pawnCount = showPawns ? table?.cachedPawns?.Count ?? 0 : 0;

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
            var field = typeof(MainTabWindow_PawnTable).GetField("table", BindingFlags.NonPublic | BindingFlags.Instance);
            return (PawnTable)field?.GetValue(this);
        }

        public override void PreClose()
        {
            base.PreClose();
            HighlightManager.ClearHighlight();
            _lastSortColumn = null;
            _lastSortDescending = false;
            SpineTiming.NotifyWorkTabOpen(false);
            
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

            // Cancel any active drag operations to ensure priority editing is re-enabled
            PawnOrganizerSystem.Instance?.CancelActiveDrag();
        }
    }
}
