using Better_Work_Tab.Features;
using Better_Work_Tab.Mod_Support.Multiplayer;
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Layouts;
#endif
using Better_Work_Tab.Features.Caching;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
using Multiplayer.API;
#endif
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

        private PawnColumnDef _lastSortColumn;
        private bool _lastSortDescending;

        private static Color CurrentRowTextColor = Color.white;

#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
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
            PawnTable table = GetPawnTable();
            if (table == null) return;

            var organizer = PawnOrganizerSystem.Instance;
            Vector2 tableOrigin = new Vector2(inRect.x, inRect.y + ExtraTopSpace);
            var snapshot = BuildSnapshotForOrganizer(table);

            // Avoid rebuilding the layout mid-drag so the handler keeps valid positioning data.
            if (organizer != null && !organizer.IsDragging)
            {
                organizer.Update(table, tableOrigin, snapshot);
            }

            Event evt = Event.current;
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                organizer?.HandleInput(evt);
                ProcessRightClicks(organizer?.Layout);
            }

            DrawWorkTable(table, organizer?.Layout, inRect);

            organizer?.DrawDragOverlays();

            DrawManualPrioritiesCheckbox();
            DrawPriorityLegend(inRect);

            bool mouseInside = Mouse.IsOver(inRect);
            Rect infoRect = GetInfoIconRect(inRect);
            DrawBottomRightButtons(inRect, infoRect);
            if (mouseInside)
            {
                DrawInfoButton(infoRect);
            }
            DrawBottomCounters(inRect, table);
        }

        private IPawnOrganizerSnapshot BuildSnapshotForOrganizer(PawnTable table)
        {
            var pawns = new List<Pawn>(PawnTableCompat.GetPawnsListForReading(table));
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

            if (TryGetBodyColumnAt(layout, evt.mousePosition, out var column) &&
                column.Column?.Worker is PawnColumnWorker_Label)
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
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
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
#if v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
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

                    MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                    
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
                    if (MultiplayerBridge.Active)
                        LayoutSharingManager.NotifyLayoutChanged();
#endif
                })
            };
            
            // Multiplayer follow mode: Copy this divider
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
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

            layout.AddDividerBeforePawn(pawn, "New Divider", Color.gray);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
            if (MultiplayerBridge.Active)
                LayoutSharingManager.NotifyLayoutChanged();
#endif
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
                        organizer.Update(table, Vector2.zero, snapshot);
                    }

                    // Use table's current header height (updates dynamically with vanilla staggering)
                    // combined with layout controller's content height (includes dividers)
                    // This is consistent during drag, preventing scrollbar flickers
                    float layoutHeight = PawnTableCompat.GetHeaderHeight(table) + organizer.Layout.ContentHeight;
                    finalHeight = layoutHeight + ExtraBottomSpace + ExtraTopSpace + Margin * 2f;
                    finalWidth = PawnTableCompat.GetSize(table).x + Margin * 2f + 25f; // Added 20f to stop headers from clipping edge
                }
                else
                {
                    // Fallback to vanilla size if organizer not ready
                    finalHeight = PawnTableCompat.GetSize(table).y + ExtraBottomSpace + ExtraTopSpace + Margin * 2f;
                    finalWidth = PawnTableCompat.GetSize(table).x + Margin * 2f + 25f; // Same as above
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

            layout.AddDividerAfterPawn(pawn, "New Divider", Color.gray);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
            if (MultiplayerBridge.Active)
                LayoutSharingManager.NotifyLayoutChanged();
#endif
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

            CalculateScrollRects(layout, inRect, out var outRect, out var viewRect);
            UpdateSortState(table);

            DrawHeaders(layout, table);
            DrawRows(table, layout, outRect, viewRect);
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
            float totalHeight = layout.ContentHeight;

            foreach (var column in layout.Columns)
            {
                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;
                var workType = column.Column?.workType;

                if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && isWorkColumn && Mouse.IsOver(column.HeaderRect))
                {
                    Color useColor = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
                    Rect columnRect = new Rect(column.OffsetX, layout.TableOrigin.y + layout.HeaderHeight, column.Width, totalHeight);
                    Widgets.DrawBoxSolid(columnRect, useColor);
                }

                column.Column.Worker.DoHeader(column.HeaderRect, table);
            }
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
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
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

            PawnTableCompat.BeginScrollView(table, outRect, viewRect);
            try
            {
                // Get the row descriptors (single source of truth for what rows exist and their heights)
                var rowDescriptors = layout.GetRowDescriptors();
                if (rowDescriptors == null || rowDescriptors.Count == 0)
                {
                    return;
                }

                var nameColumn = FindNameColumn(layout.Columns);

                // Calculate dimensions once for all highlight operations
                float totalWidth = CalculateTotalColumnWidth(layout.Columns);
                float totalHeight = layout.ContentHeight; // Already accounts for all descriptor heights

                // Phase 1: Draw all highlights (selected, hovered, float menu, similar worktypes)
                DrawAllHighlights(rowDescriptors, layout.Columns, totalWidth, totalHeight);

                // Phase 2: Draw actual row content (pawn data, divider labels, backgrounds)
                DrawAllRowContent(table,
                    rowDescriptors,
                    layout.Columns,
                    viewRect.width,
                    nameColumn,
                    outRect,
                    PawnTableCompat.GetScrollPosition(table));

                // Phase 3: Draw separator lines between rows
                DrawRowSeparators(rowDescriptors, viewRect.width);
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
        private float CalculateTotalColumnWidth(IList<WorkTabLayoutColumn> columns)
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
            IList<WorkTabLayoutColumn> columns,
            float totalWidth,
            float totalHeight)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!settings.ShowPawnAndWorktypeHighlights || !settings.enableRowColumnHighlights) return;

            WorkTabLayoutColumn? hoveredColumn = null;
            WorkTypeDef hoveredWorkType = null;

            // 1. Detect Hovered Column
            if (settings.ShowCursorPawnAndWorktypeHighlight)
            {
                float currentX = 0f;
                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i];
                    var columnRect = new Rect(currentX, 0f, col.Width, totalHeight);

                    if (Mouse.IsOver(columnRect))
                    {
                        hoveredColumn = col;
                        hoveredWorkType = col.Column?.workType;
                        break;
                    }
                    currentX += col.Width;
                }
            }

            if (hoveredWorkType == null)
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

            // 2. Draw Horizontal Highlights (Rows)
            float currentY = 0f;
            for (int i = 0; i < rowDescriptors.Count; i++)
            {
                var descriptor = rowDescriptors[i];
                Rect rowRect = new Rect(0f, currentY, totalWidth, descriptor.Height);

                if (descriptor.IsPawn && highlightedPawn != null && descriptor.Pawn == highlightedPawn && settings.ShowFloatMenuPawnAndWorktypeHighlight)
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetFloatMenuColor());
                }
                else if (settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rowRect))
                {
                    HighlightDrawer.DrawHighlight(rowRect, HighlightDrawer.GetRowHoverColor());
                }
                else if (descriptor.IsPawn && Find.Selector.IsSelected(descriptor.Pawn) && settings.DoSelectedPawnHighlight)
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

                    if (descriptor.IsDivider && Mouse.IsOver(rowRect))
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
                Rect columnRect = new Rect(startingX, 0f, column.Width, totalHeight);
                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;

                if (isWorkColumn && highlightedWorkType != null && column.Column.workType == highlightedWorkType && settings.ShowFloatMenuPawnAndWorktypeHighlight)
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetFloatMenuColor());
                }
                else if (isWorkColumn && settings.ShowCursorPawnAndWorktypeHighlight && hoveredWorkType != null && hoveredWorkType == column.Column.workType)
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetColumnHoverColor());
                }
                else if (isWorkColumn && settings.ShowSimilarWorktypeHighlight && cachedSimilarWorktypes != null && cachedSimilarWorktypes.Contains(column.Column.workType))
                {
                    HighlightDrawer.DrawHighlight(columnRect, HighlightDrawer.GetSimilarWorktypeColor());
                }

                startingX += column.Width;
            }
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
            IList<WorkTabLayoutColumn> columns,
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
            IList<WorkTabLayoutColumn> columns,
            Rect rowRect,
            WorkTabLayoutColumn? nameColumn,
            int rowIndex)
        {
            // Create temporary wrapper objects to maintain compatibility with existing draw methods.
            // These are small allocations; only optimize with pooling if profiling shows it's necessary.
            if (descriptor.IsPawn)
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
            else if (descriptor.IsDivider)
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

            float widthWithoutScrollbar = layout.Table.Size.x - 16f;
            float totalColumnWidth = layout.Columns.Count > 0
                ? layout.Columns[layout.Columns.Count - 1].OffsetX + layout.Columns[layout.Columns.Count - 1].Width
                : widthWithoutScrollbar;
            float viewWidth = Mathf.Max(widthWithoutScrollbar, totalColumnWidth);
            float contentHeight = Mathf.Max(layout.ContentHeight, 1f);
            viewRect = new Rect(0f, 0f, viewWidth, contentHeight);

        }

        private WorkTabLayoutColumn? FindNameColumn(IList<WorkTabLayoutColumn> columns)
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

            float headerTop = layout.TableOrigin.y + layout.HeaderHeight;
            float bodyBottom = layout.TableOrigin.y + layout.Table.Size.y;
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
            if (!(settings?.allowDividerCollapse ?? true))
            {
                return;
            }

            Rect arrowRect = new Rect(labelCellRect.xMin + 6f, labelCellRect.y + (labelCellRect.height - 16f) / 2f, 18f, 16f);
            string arrowChar = divider.IsCollapsed ? "\u25B6" : "\u25BC";
            if (Widgets.ButtonInvisible(arrowRect))
            {
                ToggleDividerCollapsed(divider);
                
#if !v1_2 && !v1_1 && !(v1_0 || v0_19)
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
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        private void DrawPawnRow(PawnTable table, WorkTabLayoutRow row, Rect rowRect, IList<WorkTabLayoutColumn> columns)
        {
            foreach (var column in columns)
            {
                Rect cellRect = new Rect(column.OffsetX, rowRect.y, column.Width, rowRect.height);
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

            try
            {
                GUI.color = Spine.UI.TextColorHelper.GetContrastingTextColor(divider.DividerColor);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = divider.LabelFont;

                // Increase the left indent to match pawn name padding
                // Only indent for the arrow if collapse is allowed
                float indent = (settings?.allowDividerCollapse ?? true) ? 33f : 6f;
                labelCellRect.xMin += indent; 

                // Clip text to avoid overflow on small-width columns
                if (labelCellRect.width > 0)
                {
                    string label = divider.DividerName ?? "Divider";
                    // If height is small, force Tiny font to try and fit
                    if (labelCellRect.height < 18f)
                    {
                          Text.Font = GameFont.Tiny;
                    }
                    Widgets.Label(labelCellRect, label);
                }
            }
            finally
            {
                Text.Anchor = originalAnchor;
                Text.Font = originalFont;
                GUI.color = originalColor;
            }
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
                Color oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                int maxPriority = WorkPrioritySystem.GetMaxPriority();
                string priorityHelp = maxPriority > 4
                    ? "BWT_PriorityOneDoneFirstExtended".Translate(maxPriority)
                    : "PriorityOneDoneFirst".Translate();

                float helpWidth = maxPriority > 4 ? 220f : rect.width;
                Widgets.Label(new Rect(rect.x, rect.yMax - 6f, helpWidth, 60f), priorityHelp);
                GUI.color = oldColor;
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
                if (Find.WindowStack != null && Find.WindowStack.TryRemove(typeof(Dialog_ModSettings)))
                {
                    return;
                }

#if v0_16
                Find.WindowStack.Add(new Dialog_ModSettings());
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
                }
#endif
            }
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

            // Cancel any active drag operations to ensure priority editing is re-enabled
            PawnOrganizerSystem.Instance?.CancelActiveDrag();
        }
    }
}
