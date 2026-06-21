using Better_Work_Tab;
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
using Better_Work_Tab.Patches;
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
#if v0_16
            return;
#else
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
#endif
        }

        private const float RightEdgeMargin = 10f;
        private const float InfoIconSize = 24f;
#if v0_16
        private const float Legacy016InfoIconEdgeMargin = 2f;
        private const float Legacy016InfoIconBottomMargin = 8f;
#endif
        private const float MinWorkTabHeight = 200f;
#if v0_16
        private const float ExtraTopSpace = 0f;
        private const float ExtraBottomSpace = 0f;
#endif

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

#if v0_16
            Legacy016PreOpen();
#if v0_13
            Legacy013SetMainTabRect();
#endif
            return;
#endif

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
                if (Verse.Current.Game?.playSettings != null)
                {
                    Verse.Current.Game.playSettings.useWorkPriorities = true;
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
#if v0_16
            DoLegacy016WindowContents(inRect);
            return;
#else
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
#endif
        }

        private IPawnOrganizerSnapshot BuildSnapshotForOrganizer(PawnTable table)
        {
            var pawns = new List<Pawn>(PawnTableCompat.GetPawnsListForReading(table));
            var comp = Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
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
            var options = new List<FloatMenuOption>();
            bool organizerAvailable = PawnOrganizerSystem.Instance?.Layout != null;
            if (organizerAvailable)
            {
                options.Add(new FloatMenuOption("Insert divider above", () => InsertDividerAbove(pawn)));
                options.Add(new FloatMenuOption("Insert divider below", () => InsertDividerBelow(pawn)));
            }

            options.Add(new FloatMenuOption("Change title...", () => ShowRenamePawnDialog(pawn)));
            options.Add(new FloatMenuOption("Set background color...", () => ShowBackgroundColorPicker(pawn)));

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
#if vAlpha4
            MessageCompat.Message("[Better Work Tab] Pawn renaming is not available in Alpha4.", MessageTypeDefOf.RejectInput);
#elif v0_18 || v0_17 || v0_16
            Find.WindowStack.Add(new Dialog_ChangeNameTriple(pawn));
#elif v1_3 || v1_2 || v1_1 || (v1_0 || v0_19)
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
            var worklist = Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
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
#if v0_16
                return Legacy016RequestedTabSize;
#else
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
#endif
            }
        }


        private void InsertDividerBelow(Pawn pawn)
        {
            var worklist = Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.CurrentWorklist;
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
                var organizer = PawnOrganizerSystem.Instance;
                if (organizer != null)
                {
                    organizer.SetPawnBackgroundColor(pawn, newColor);
                }
                else
                {
                    PawnOrganizer.API.PawnColorDatabase.SetColor(pawn, newColor);
                    Legacy016InvalidateFrameCaches();
                }
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
                    Better_Work_Tab.WidgetsCompat.DrawBoxSolid(columnRect, useColor);
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
#if v0_13
            Legacy013SetMainTabRect();
#endif
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
                Better_Work_Tab.WidgetsCompat.DrawLineHorizontal(0f, currentY - 1f, viewWidth);
            }
            GUI.color = Color.white;
        }


        private void DrawRowBackground(WorkTabLayoutRow row, Rect rect)
        {
            if (row.Pawn != null && PawnOrganizer.API.PawnColorDatabase.TryGetColor(row.Pawn, out var pawnColor) && pawnColor.a > 0f)
            {
                var overlay = new Color(pawnColor.r, pawnColor.g, pawnColor.b, Mathf.Clamp(pawnColor.a, 0.08f, 0.6f));
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, overlay);

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
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, dividerColor);
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
            if (Better_Work_Tab.WidgetsCompat.ButtonInvisible(arrowRect))
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
            UISoundCompat.TickTiny.PlayOneShotOnCamera();
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

            if (row.Pawn != null && Better_Work_Tab.PawnCompat.IsDowned(row.Pawn))
            {
                GUI.color = new Color(1f, 0f, 0f, 0.5f);
                Better_Work_Tab.WidgetsCompat.DrawLineHorizontal(rowRect.xMin, rowRect.center.y, rowRect.width);
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
            bool wasEnabled = Verse.Current.Game.playSettings.useWorkPriorities;
            Better_Work_Tab.WidgetsCompat.CheckboxLabeled(rect, "ManualPriorities".Translate(), ref Verse.Current.Game.playSettings.useWorkPriorities);
            bool isEnabled = Verse.Current.Game.playSettings.useWorkPriorities;
            if (wasEnabled != isEnabled)
            {
                foreach (Pawn pawn in PawnsFinderCompat.AllMapsWorldAndTemporaryAlive)
                {
                    if (pawn.Faction == FactionCompat.OfPlayer && Better_Work_Tab.PawnCompat.WorkSettings(pawn) != null)
                    {
                        Better_Work_Tab.PawnCompat.WorkSettings(pawn).Notify_UseWorkPrioritiesChanged();
                    }
                }
            }
            if (Verse.Current.Game.playSettings.useWorkPriorities)
            {
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(new Rect(rect.x, rect.yMax - 6f, rect.width, 60f), "PriorityOneDoneFirst".Translate());
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
            bool clicked;
            if (TexButton.Info != null)
            {
                clicked = Better_Work_Tab.WidgetsCompat.ButtonImage(gearRect, TexButton.Info);
            }
            else
            {
                clicked = Better_Work_Tab.WidgetsCompat.ButtonText(gearRect, "i");
            }

            if (clicked)
            {
                if (Better_Work_Tab.Find.WindowStack.TryRemove(typeof(Dialog_ModSettings)))
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
                Map map = MapCompat.CurrentMap;
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

#if v0_16
        private const float Legacy016TopAreaHeight = 40f;
        private const float Legacy016VanillaLabelRowHeight = 50f;
        private const float Legacy016AngledLabelRowHeight = 70f;
#if v0_14
        private const float Legacy016LeftColumnWidth = 165f;
        private const float Legacy016CopyPasteWidth = 0f;
#else
        private const float Legacy016LeftColumnWidth = 201f;
        private const float Legacy016CopyPasteWidth = 36f;
#endif
        private float _legacy016WorkColumnSpacing = -1f;
        private readonly List<WorkTypeDef> _legacy016VisibleWorkTypes = new List<WorkTypeDef>();
        private readonly DefMap<WorkTypeDef, Vector2> _legacy016CachedLabelSizes = new DefMap<WorkTypeDef, Vector2>();
        private static DefMap<WorkTypeDef, int> Legacy016Clipboard;
        private WorkTypeDef _legacy016PendingHeaderClick;
        private int _legacy016PendingHeaderButton = -1;
        private Vector2 _legacy016PendingHeaderMouse;
        private bool _legacy016ColumnDragActive;
        private WorkTypeDef _legacy016DraggingWorkType;
        private int _legacy016ColumnDragTargetIndex = -1;
        private WorkTypeDef _legacy016SortingWorkType;
        private bool _legacy016SortingDescending;
        private bool _alpha4LegacyInitialized;
        private Pawn _legacy016PendingRowPawn;
        private Vector2 _legacy016PendingRowMouse;
        private bool _legacy016RowDragActive;
        private Pawn _legacy016DraggingRowPawn;
        private int _legacy016RowDragTargetIndex = -1;
        private static bool _legacy016PriorityPaintActive;
        private static int _legacy016PriorityPaintValue;
        private const int Legacy016NoSkillLevel = -1;
        private static readonly Dictionary<int, int> Legacy016SkillLevelCache = new Dictionary<int, int>();
        private static readonly Dictionary<int, bool> Legacy016IncapableCache = new Dictionary<int, bool>();
        private static readonly Dictionary<int, int> Legacy016SkillLevelCacheTimestamps = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> Legacy016IncapableCacheTimestamps = new Dictionary<int, int>();
        private const int Legacy016SkillCacheFrameValidity = 60;
        private const int Legacy016IncapableCacheFrameValidity = 120;
        private readonly List<Pawn> _legacy016DisplayPawnsCache = new List<Pawn>();
        private readonly HashSet<int> _legacy016DisplayPawnSeen = new HashSet<int>();
        private int _legacy016DisplayPawnsCacheFrame = -1;
        private WorkTypeDef _legacy016DisplayPawnsSortWorkType;
        private bool _legacy016DisplayPawnsSortDescending;
        private int _legacy016DisplayPawnsSourceCount = -1;

        private Vector2 Legacy016RequestedTabSize
        {
            get
            {
                int pawnCount = pawns?.Count ?? 0;
                return new Vector2(1010f, Legacy016TopAreaHeight + Legacy016HeaderHeight + pawnCount * 30f + 65f);
            }
        }

        private static bool Legacy016UseAngledHeaders
        {
            get
            {
                return BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders;
            }
        }

        private static float Legacy016HeaderHeight => Legacy016UseAngledHeaders ? Legacy016AngledLabelRowHeight : Legacy016VanillaLabelRowHeight;

        private void Legacy016PreOpen()
        {
#if vAlpha4
            Better_Work_Tab.LongEventHandler.RunPending();
#endif
            WorkColumnOrderManager.InitializeOnGameLoad();
            Legacy016RefreshVisibleWorkTypes();

            if (_legacy016VisibleWorkTypes.Count == 0)
            {
                _legacy016VisibleWorkTypes.AddRange(WorkTypeDefsUtility.WorkTypeDefsInPriorityOrder.Where(def => Better_Work_Tab.WorkTypeCompat.IsVisible(def)));
            }

            _legacy016SortingWorkType = null;
            _legacy016SortingDescending = false;
            Legacy016CancelColumnDrag();

            foreach (WorkTypeDef allDef in DefDatabase<WorkTypeDef>.AllDefs)
            {
                _legacy016CachedLabelSizes[allDef] = Text.CalcSize(Better_Work_Tab.WorkTypeCompat.LabelShort(allDef));
            }

            if (BetterWorkTabMod.Settings?.autoEnableManualPriorities ?? false)
            {
                Verse.Current.Game.playSettings.useWorkPriorities = true;
            }
        }

        private void Legacy016RefreshVisibleWorkTypes()
        {
            _legacy016VisibleWorkTypes.Clear();
            PawnTableDef tableDef = PawnTableDefOf.Work;

            if (tableDef?.columns == null)
            {
                return;
            }

            foreach (PawnColumnDef column in tableDef.columns)
            {
                WorkTypeDef workType = column?.workType;
                if (workType != null && Better_Work_Tab.WorkTypeCompat.IsVisible(workType) && column.Worker is PawnColumnWorker_WorkPriority)
                {
                    _legacy016VisibleWorkTypes.Add(workType);
                }
            }
        }

        private void DoLegacy016WindowContents(Rect rect)
        {
#if v0_13
            Legacy013SetMainTabRect();
#else
            SetInitialSizeAndPosition();
#endif
            if (Event.current.type == EventType.Layout)
                return;

            if (Event.current.type == EventType.MouseUp || Event.current.type == EventType.Ignore)
            {
                Legacy016ClearPriorityPaint();
            }

            DrawLegacy016TopArea(new Rect(0f, 0f, rect.width, Legacy016TopAreaHeight));

            Rect workArea = new Rect(0f, Legacy016TopAreaHeight, rect.width, rect.height - Legacy016TopAreaHeight);
            GUI.BeginGroup(workArea);
            try
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                float headerHeight = Legacy016HeaderHeight;
                Rect rowsRect = new Rect(0f, headerHeight, workArea.width, workArea.height - headerHeight);
                _legacy016WorkColumnSpacing = (workArea.width - 16f - Legacy016LeftColumnWidth) / Mathf.Max(1, _legacy016VisibleWorkTypes.Count);
                DrawLegacy016Headers(workArea.width, headerHeight);
                DrawLegacy016Rows(rowsRect);
                DrawLegacy016ColumnDragOverlay(workArea.width, headerHeight, rowsRect.height);
            }
            finally
            {
                GUI.EndGroup();
            }

            DrawLegacy016BottomRightButtons(rect);
        }

#if vAlpha4
        internal void DoAlpha4PanelContents(Rect rect)
        {
            if (!_alpha4LegacyInitialized)
            {
                Legacy016PreOpen();
                _alpha4LegacyInitialized = true;
            }

            pawns.Clear();
            pawns.AddRange(PawnsFinderCompat.AllMapsWorldAndTemporaryAlive
                .Where(pawn => pawn != null && pawn.Faction == FactionCompat.OfPlayer)
                .OrderBy(RowOrderUtility.GetPawnRowOrder)
                .ThenBy(pawn => pawn.thingIDNumber));

            DoLegacy016WindowContents(rect);
        }
#endif

#if v0_13
        private void Legacy013SetMainTabRect()
        {
            Vector2 size = Legacy016RequestedTabSize;
            size.x = Mathf.Min(size.x, Verse.UI.screenWidth);
            size.y = Mathf.Min(size.y, Verse.UI.screenHeight - 35f);

            float x = Anchor == MainTabWindowAnchor.Left
                ? 0f
                : Verse.UI.screenWidth - size.x;

            currentWindowRect = new Rect(
                Mathf.Max(0f, x),
                Mathf.Max(0f, Verse.UI.screenHeight - 35f - size.y),
                size.x,
                size.y);
        }
#endif

        private void DrawLegacy016TopArea(Rect rect)
        {
            GUI.BeginGroup(rect);
            try
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Rect manualRect = new Rect(5f, 5f, 140f, 30f);
                bool wasEnabled = Verse.Current.Game.playSettings.useWorkPriorities;
                Better_Work_Tab.WidgetsCompat.CheckboxLabeled(manualRect, "ManualPriorities".Translate(), ref Verse.Current.Game.playSettings.useWorkPriorities);
                if (wasEnabled != Verse.Current.Game.playSettings.useWorkPriorities)
                {
                    foreach (Pawn pawn in PawnsFinderCompat.AllMapsWorldAndTemporaryAlive)
                    {
                        if (pawn.Faction == FactionCompat.OfPlayer && Better_Work_Tab.PawnCompat.WorkSettings(pawn) != null)
                            Better_Work_Tab.PawnCompat.WorkSettings(pawn).Notify_UseWorkPrioritiesChanged();
                    }
                }

                if (!Verse.Current.Game.playSettings.useWorkPriorities)
                {
                    UIHighlighter.HighlightOpportunity(manualRect, "ManualPriorities-Off");
                }

                float first = rect.width / 3f;
                float second = rect.width * 2f / 3f;
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Text.Anchor = TextAnchor.UpperCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(first - 50f, 5f, 160f, 30f), "<= " + "HigherPriority".Translate());
                Widgets.Label(new Rect(second - 50f, 5f, 160f, 30f), "LowerPriority".Translate() + " =>");
            }
            finally
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                GUI.EndGroup();
            }
        }

        private void DrawLegacy016BottomRightButtons(Rect rect)
        {
            if (!(BetterWorkTabMod.Settings?.enableUIElements ?? true))
            {
                return;
            }

            Rect infoRect = GetLegacy016InfoIconRect(rect);
            HeaderButtons.DrawBottomRightGrouped(rect, infoRect);
            DrawInfoButton(infoRect);
            TooltipHandler.TipRegion(infoRect, "Better Work Tab settings");
        }

        private static Rect GetLegacy016InfoIconRect(Rect rect)
        {
            return new Rect(
                rect.xMax - InfoIconSize - Legacy016InfoIconEdgeMargin,
                rect.yMax - InfoIconSize - Legacy016InfoIconBottomMargin,
                InfoIconSize,
                InfoIconSize);
        }

        private void DrawLegacy016Headers(float width, float headerHeight)
        {
            if (Legacy016UseAngledHeaders)
            {
                DrawLegacy016AngledHeaders(width, headerHeight);
                return;
            }

            float x = Legacy016LeftColumnWidth;
            for (int i = 0; i < _legacy016VisibleWorkTypes.Count; i++)
            {
                WorkTypeDef workType = _legacy016VisibleWorkTypes[i];
                bool showMarker = ShouldShowColumnMarker(workType);
                string label = HeaderUtility.GetHeaderText(workType, showMarker);
                Vector2 size = Text.CalcSize(label);
                float centerX = x + 15f;
                Rect labelRect = new Rect(centerX - size.x / 2f, 0f, size.x, size.y);
                if (i % 2 == 1)
                {
                    labelRect.y += 20f;
                }

                if (Mouse.IsOver(labelRect))
                {
                    Widgets.DrawHighlight(labelRect);
                }

                Legacy016HandleHeaderInput(labelRect, workType);

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = showMarker && (BetterWorkTabMod.Settings?.showMovedColumnColorTint ?? true)
                    ? HeaderUtility.Colors.MovedMarkerColor
                    : (BetterWorkTabMod.Settings?.angledHeaderColor ?? DefaultSettings.Color_AngledHeaderText);
                Widgets.Label(labelRect, label);
                TooltipHandler.TipRegion(labelRect, new TipSignal(() => workType.gerundLabel + "\n\n" + workType.description + "\n\n" + Legacy016SpecificWorkListString(workType), workType.GetHashCode()));

                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                Widgets.DrawLineVertical(centerX, labelRect.yMax - 3f, headerHeight - labelRect.yMax + 3f);
                Widgets.DrawLineVertical(centerX + 1f, labelRect.yMax - 3f, headerHeight - labelRect.yMax + 3f);
                GUI.color = Color.white;
                x += _legacy016WorkColumnSpacing;
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawLegacy016AngledHeaders(float width, float headerHeight)
        {
            float x = Legacy016LeftColumnWidth;
            float columnWidth = Mathf.Max(25f, _legacy016WorkColumnSpacing);
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            bool oldWordWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;

                for (int i = 0; i < _legacy016VisibleWorkTypes.Count; i++)
                {
                    WorkTypeDef workType = _legacy016VisibleWorkTypes[i];
                    bool showMarker = ShouldShowColumnMarker(workType);
                    string label = HeaderUtility.GetHeaderText(workType, showMarker);
                    Vector2 labelSize = Text.CalcSize(label);
                    float centerX = x + 15f;
                    Rect headerRect = new Rect(centerX - columnWidth / 2f, 0f, columnWidth, headerHeight);

                    if (Mouse.IsOver(headerRect))
                    {
                        Widgets.DrawHighlight(headerRect);
                    }

                    Legacy016HandleHeaderInput(headerRect, workType);
                    DrawLegacy016AngledHeaderLabel(headerRect, label, labelSize, showMarker);
                    TooltipHandler.TipRegion(headerRect, new TipSignal(() => workType.gerundLabel + "\n\n" + workType.description + "\n\n" + Legacy016SpecificWorkListString(workType), workType.GetHashCode()));

                    x += _legacy016WorkColumnSpacing;
                }
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWordWrap;
                GUI.color = oldColor;
            }
        }

        private static void DrawLegacy016AngledHeaderLabel(Rect headerRect, string label, Vector2 labelSize, bool showMarker)
        {
            float rotation = BetterWorkTabMod.Settings?.angledHeaderRotation ?? DefaultSettings.angledHeaderRotation;
            float horizontalOffset = BetterWorkTabMod.Settings?.angledHeaderHorizontalOffset ?? DefaultSettings.angledHeaderHorizontalOffset;
            if (Mathf.Abs(rotation + 90f) < 0.1f)
            {
                horizontalOffset = 0f;
            }

            Rect drawRect = new Rect(0f, 0f, headerRect.height, labelSize.y) { center = headerRect.center };
            drawRect.x += horizontalOffset;

            Matrix4x4 originalMatrix = GUI.matrix;
            TextAnchor originalAnchor = Text.Anchor;
            GameFont originalFont = Text.Font;
            Color originalColor = GUI.color;
            bool originalWordWrap = Text.WordWrap;

            try
            {
                GUI.matrix = Matrix4x4.identity;
                Vector2 pivotPoint = GUIClipUtility.Unclip(drawRect.center);

                Matrix4x4 transform = originalMatrix;
                transform *= Matrix4x4.TRS(pivotPoint, Quaternion.identity, Vector3.one);
                transform *= Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, rotation), Vector3.one);
                transform *= Matrix4x4.TRS(-pivotPoint, Quaternion.identity, Vector3.one);
                GUI.matrix = transform;

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                GUI.color = showMarker && (BetterWorkTabMod.Settings?.showMovedColumnColorTint ?? true)
                    ? HeaderUtility.Colors.MovedMarkerColor
                    : (BetterWorkTabMod.Settings?.angledHeaderColor ?? DefaultSettings.Color_AngledHeaderText);
                Widgets.Label(drawRect, label);

                if (!(BetterWorkTabMod.Settings?.removeHeaderUnderline ?? DefaultSettings.removeHeaderUnderline))
                {
                    Widgets.DrawLine(new Vector2(drawRect.xMin, drawRect.yMax), new Vector2(drawRect.xMin + labelSize.x, drawRect.yMax), Color.white, 1f);
                }
            }
            finally
            {
                GUI.matrix = originalMatrix;
                Text.Anchor = originalAnchor;
                Text.Font = originalFont;
                GUI.color = originalColor;
                Text.WordWrap = originalWordWrap;
            }
        }

        private static string Legacy016SpecificWorkListString(WorkTypeDef def)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < Better_Work_Tab.WorkTypeCompat.WorkGiversByPriority(def).Count; i++)
            {
                builder.Append(Better_Work_Tab.WorkTypeCompat.WorkGiverLabelCap(Better_Work_Tab.WorkTypeCompat.WorkGiversByPriority(def)[i]));
#if vAlpha4
                if (def.emergency)
#else
                if (Better_Work_Tab.WorkTypeCompat.WorkGiversByPriority(def)[i].emergency)
#endif
                {
                    builder.Append(" (" + "EmergencyWorkMarker".Translate() + ")");
                }
                if (i < Better_Work_Tab.WorkTypeCompat.WorkGiversByPriority(def).Count - 1)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private void Legacy016HandleHeaderInput(Rect headerRect, WorkTypeDef workType)
        {
            Event evt = Event.current;
            if (evt == null || workType == null)
            {
                return;
            }

            if (_legacy016ColumnDragActive && _legacy016DraggingWorkType == workType)
            {
                if (evt.type == EventType.MouseDrag)
                {
                    _legacy016ColumnDragTargetIndex = Legacy016ColumnTargetIndex(evt.mousePosition.x);
                    evt.Use();
                    return;
                }

                if (evt.type == EventType.MouseUp)
                {
                    Legacy016CommitColumnDrag();
                    evt.Use();
                    return;
                }
            }

            bool overHeader = Mouse.IsOver(headerRect);
            if (evt.type == EventType.MouseDown && overHeader && (evt.button == 0 || evt.button == 1))
            {
                if (evt.shift || (evt.modifiers & EventModifiers.Shift) != 0)
                {
                    Legacy016ApplyHeaderPriority(workType, evt.button);
                    evt.Use();
                    return;
                }

                _legacy016PendingHeaderClick = workType;
                _legacy016PendingHeaderButton = evt.button;
                _legacy016PendingHeaderMouse = evt.mousePosition;
                return;
            }

            if (evt.type == EventType.MouseDrag && _legacy016PendingHeaderClick == workType)
            {
                var settings = BetterWorkTabMod.Settings;
                bool dragEnabled = settings?.enableDragDropReordering ?? true;
                bool columnDragEnabled = settings?.columnDraggingEnabled ?? true;
                bool requireCtrl = settings?.requireCtrlForDrag ?? DefaultSettings.requireCtrlForDrag;
                bool ctrlSatisfied = !requireCtrl || evt.control;
                float threshold = Mathf.Max(1f, settings?.dragThreshold ?? DefaultSettings.dragThreshold);

                if (dragEnabled && columnDragEnabled && ctrlSatisfied &&
                    (evt.mousePosition - _legacy016PendingHeaderMouse).magnitude >= threshold)
                {
                    _legacy016ColumnDragActive = true;
                    _legacy016DraggingWorkType = workType;
                    _legacy016ColumnDragTargetIndex = Legacy016ColumnTargetIndex(evt.mousePosition.x);
                    BetterWorkTabLocalState.IsHeaderDragging = true;
                    evt.Use();
                }

                return;
            }

            if (evt.type == EventType.MouseUp && _legacy016PendingHeaderClick == workType)
            {
                int button = _legacy016PendingHeaderButton;
                Legacy016CancelPendingHeaderClick();

                if (overHeader)
                {
                    Legacy016SortByHeader(workType, button);
                    evt.Use();
                }
            }
        }

        private int Legacy016ColumnTargetIndex(float mouseX)
        {
            int target = _legacy016VisibleWorkTypes.Count;
            float x = Legacy016LeftColumnWidth;

            for (int i = 0; i < _legacy016VisibleWorkTypes.Count; i++)
            {
                float centerX = x + 15f;
                if (mouseX < centerX)
                {
                    target = i;
                    break;
                }

                x += _legacy016WorkColumnSpacing;
            }

            return Mathf.Clamp(target, 0, _legacy016VisibleWorkTypes.Count);
        }

        private void DrawLegacy016ColumnDragOverlay(float width, float headerHeight, float rowsHeight)
        {
            if (!_legacy016ColumnDragActive || _legacy016ColumnDragTargetIndex < 0)
            {
                return;
            }

            DrawLegacy016ColumnBaselineLine(width, headerHeight, rowsHeight);

            float lineX = Legacy016LeftColumnWidth + _legacy016ColumnDragTargetIndex * _legacy016WorkColumnSpacing;
            lineX = Mathf.Clamp(lineX, Legacy016LeftColumnWidth, width - 16f);
            int insetSetting = BetterWorkTabMod.Settings?.columnInsertionLineInset ?? DefaultSettings.columnInsertionLineInset;
            float inset = Mathf.Clamp(insetSetting, 0f, headerHeight);
            float lineY = headerHeight - inset;
            float lineHeight = Mathf.Max(0f, rowsHeight + inset);

            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(new Rect(lineX - 1f, lineY, 2f, lineHeight), Color.white);
        }

        private void DrawLegacy016ColumnBaselineLine(float width, float headerHeight, float rowsHeight)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.showColumnBaselineLine ?? true))
            {
                return;
            }

            WorkTypeDef workType = _legacy016DraggingWorkType;
            if (workType?.defName == null || !IsColumnOutOfBaselinePosition(workType))
            {
                return;
            }

            var baselineOrder = WorkColumnOrderManager.GetBaselineOrder();
            if (baselineOrder == null || baselineOrder.Count == 0)
            {
                return;
            }

            var visibleDefNames = new HashSet<string>(_legacy016VisibleWorkTypes
                .Where(def => def?.defName != null)
                .Select(def => def.defName));
            var filteredBaseline = baselineOrder.Where(visibleDefNames.Contains).ToList();
            int baselineIndex = filteredBaseline.IndexOf(workType.defName);
            if (baselineIndex < 0)
            {
                return;
            }

            var baselineIndexByDef = new Dictionary<string, int>(filteredBaseline.Count);
            for (int i = 0; i < filteredBaseline.Count; i++)
            {
                baselineIndexByDef[filteredBaseline[i]] = i;
            }

            int targetIndex = 0;
            for (int i = 0; i < _legacy016VisibleWorkTypes.Count; i++)
            {
                WorkTypeDef other = _legacy016VisibleWorkTypes[i];
                if (other == null || other == workType || other.defName == null)
                {
                    continue;
                }

                int otherBaselineIndex = baselineIndexByDef.TryGetValue(other.defName, out int idx)
                    ? idx
                    : int.MaxValue;
                if (otherBaselineIndex < baselineIndex)
                {
                    targetIndex++;
                }
            }

            float lineX = Legacy016LeftColumnWidth + targetIndex * _legacy016WorkColumnSpacing;
            lineX = Mathf.Clamp(lineX, Legacy016LeftColumnWidth, width - 16f);
            int insetSetting = settings?.columnInsertionLineInset ?? DefaultSettings.columnInsertionLineInset;
            float inset = Mathf.Clamp(insetSetting, 0f, headerHeight);
            float lineY = headerHeight - inset;
            float lineHeight = Mathf.Max(0f, rowsHeight + inset);

            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(
                new Rect(lineX - 1f, lineY, 2f, lineHeight),
                HeaderUtility.Colors.MovedMarkerColor);
        }

        private void Legacy016CommitColumnDrag()
        {
            WorkTypeDef workType = _legacy016DraggingWorkType;
            int targetIndex = _legacy016ColumnDragTargetIndex;
            Legacy016CancelColumnDrag();

            if (workType == null || targetIndex < 0)
            {
                return;
            }

            int oldIndex = _legacy016VisibleWorkTypes.IndexOf(workType);
            if (oldIndex < 0)
            {
                return;
            }

            int insertIndex = Mathf.Clamp(targetIndex, 0, _legacy016VisibleWorkTypes.Count);
            if (insertIndex > oldIndex)
            {
                insertIndex--;
            }

            if (insertIndex == oldIndex)
            {
                return;
            }

            _legacy016VisibleWorkTypes.RemoveAt(oldIndex);
            _legacy016VisibleWorkTypes.Insert(insertIndex, workType);
            Legacy016ApplyVisibleOrderToTableDef();
            Legacy016InvalidateDisplayPawnCache();
            MarkColumnMoved(workType);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            UISoundCompat.TickHigh.PlayOneShotOnCamera();
        }

        private void Legacy016ApplyVisibleOrderToTableDef()
        {
            PawnTableDef def = PawnTableDefOf.Work;
            if (def?.columns == null)
            {
                return;
            }

            var preWork = new List<PawnColumnDef>();
            var postWork = new List<PawnColumnDef>();
            var workColumns = new List<PawnColumnDef>();
            bool passedWorkColumn = false;

            foreach (PawnColumnDef column in def.columns)
            {
                bool isWorkColumn = column?.workType != null && column.Worker is PawnColumnWorker_WorkPriority;
                if (isWorkColumn)
                {
                    passedWorkColumn = true;
                    workColumns.Add(column);
                }
                else if (!passedWorkColumn)
                {
                    preWork.Add(column);
                }
                else
                {
                    postWork.Add(column);
                }
            }

            var orderedWorkColumns = new List<PawnColumnDef>();
            foreach (WorkTypeDef workType in _legacy016VisibleWorkTypes)
            {
                PawnColumnDef match = workColumns.FirstOrDefault(c => c.workType == workType);
                if (match != null && !orderedWorkColumns.Contains(match))
                {
                    orderedWorkColumns.Add(match);
                }
            }

            foreach (PawnColumnDef column in workColumns)
            {
                if (!orderedWorkColumns.Contains(column))
                {
                    orderedWorkColumns.Add(column);
                }
            }

            def.columns.Clear();
            def.columns.AddRange(preWork);
            def.columns.AddRange(orderedWorkColumns);
            def.columns.AddRange(postWork);
            WorkColumnOrderManager.CaptureCurrent(def);
            BetterWorkTabMod.Settings?.Write();
        }

        private void Legacy016CancelColumnDrag()
        {
            _legacy016ColumnDragActive = false;
            _legacy016DraggingWorkType = null;
            _legacy016ColumnDragTargetIndex = -1;
            Legacy016CancelPendingHeaderClick();
            Legacy016CancelRowDrag();
            BetterWorkTabLocalState.IsHeaderDragging = false;
        }

        private void Legacy016CancelPendingHeaderClick()
        {
            _legacy016PendingHeaderClick = null;
            _legacy016PendingHeaderButton = -1;
        }

        private void Legacy016SortByHeader(WorkTypeDef workType, int button)
        {
            if (workType == null)
            {
                return;
            }

            if (button == 1)
            {
                _legacy016SortingWorkType = workType;
                _legacy016SortingDescending = true;
                UISoundCompat.TickLow.PlayOneShotOnCamera();
                return;
            }

            if (_legacy016SortingWorkType != workType)
            {
                _legacy016SortingWorkType = workType;
                _legacy016SortingDescending = false;
                UISoundCompat.TickHigh.PlayOneShotOnCamera();
            }
            else if (!_legacy016SortingDescending)
            {
                _legacy016SortingDescending = true;
                UISoundCompat.TickLow.PlayOneShotOnCamera();
            }
            else
            {
                _legacy016SortingWorkType = null;
                _legacy016SortingDescending = false;
                UISoundCompat.TickLow.PlayOneShotOnCamera();
            }
        }

        private void Legacy016ApplyHeaderPriority(WorkTypeDef workType, int button)
        {
            if (workType == null)
            {
                return;
            }

            bool useWorkPriorities = Verse.Current.Game?.playSettings?.useWorkPriorities ?? false;
            bool changed = false;

            foreach (Pawn pawn in pawns)
            {
                if (pawn == null || Better_Work_Tab.PawnCompat.IsDead(pawn) || Better_Work_Tab.PawnCompat.WorkSettings(pawn) == null || !Better_Work_Tab.PawnCompat.HasEverWork(pawn) || pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                int currentPriority = WorkPrioritySystem.GetPriority(Better_Work_Tab.PawnCompat.WorkSettings(pawn), workType);
                int nextPriority = WorkPrioritySystem.GetPriorityAfterHeaderMouseButton(currentPriority, button, useWorkPriorities);
                if (nextPriority == currentPriority)
                {
                    continue;
                }

                WorkPrioritySystem.SetPriority(Better_Work_Tab.PawnCompat.WorkSettings(pawn), workType, nextPriority);
                changed = true;
            }

            if (changed)
            {
                if (useWorkPriorities)
                {
                    UISoundCompat.DragSlider.PlayOneShotOnCamera();
                }
                else if (button == 0)
                {
                    UISoundCompat.CheckboxTurnedOn.PlayOneShotOnCamera();
                }
                else
                {
                    UISoundCompat.CheckboxTurnedOff.PlayOneShotOnCamera();
                }

                WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                Legacy016InvalidateFrameCaches();
            }
        }

        private void DrawLegacy016Rows(Rect rect)
        {
            List<Pawn> displayPawns = Legacy016DisplayPawns();
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, displayPawns.Count * 30f);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            try
            {
                float y = 0f;
                for (int i = 0; i < displayPawns.Count; i++)
                {
                    Pawn pawn = displayPawns[i];
                    Rect rowRect = new Rect(0f, y, viewRect.width, 30f);
                    if (!(y - scrollPosition.y + 30f < 0f) && !(y - scrollPosition.y > rect.height))
                    {
                        DrawLegacy016PawnRowBackground(rowRect, pawn, i, displayPawns.Count);
                        DrawPawnRow(rowRect, pawn);
                        DrawLegacy016PawnRowOverlay(rowRect, pawn);
                    }

                    y += 30f;
                }

                DrawLegacy016RowDragOverlay(viewRect.width);
            }
            finally
            {
                Widgets.EndScrollView();
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private List<Pawn> Legacy016DisplayPawns()
        {
            int currentFrame = Time.frameCount;
            int sourceCount = pawns?.Count ?? 0;
            if (_legacy016DisplayPawnsCacheFrame == currentFrame &&
                _legacy016DisplayPawnsSortWorkType == _legacy016SortingWorkType &&
                _legacy016DisplayPawnsSortDescending == _legacy016SortingDescending &&
                _legacy016DisplayPawnsSourceCount == sourceCount)
            {
                return _legacy016DisplayPawnsCache;
            }

            _legacy016DisplayPawnsCacheFrame = currentFrame;
            _legacy016DisplayPawnsSortWorkType = _legacy016SortingWorkType;
            _legacy016DisplayPawnsSortDescending = _legacy016SortingDescending;
            _legacy016DisplayPawnsSourceCount = sourceCount;
            _legacy016DisplayPawnsCache.Clear();
            _legacy016DisplayPawnSeen.Clear();

            foreach (Pawn pawn in pawns)
            {
                if (pawn == null || !_legacy016DisplayPawnSeen.Add(pawn.thingIDNumber))
                {
                    continue;
                }

                _legacy016DisplayPawnsCache.Add(pawn);
            }

            if (_legacy016SortingWorkType == null)
            {
                _legacy016DisplayPawnsCache.Sort((left, right) =>
                {
                    int result = RowOrderUtility.GetPawnRowOrder(left).CompareTo(RowOrderUtility.GetPawnRowOrder(right));
                    return result != 0 ? result : left.thingIDNumber.CompareTo(right.thingIDNumber);
                });
                return _legacy016DisplayPawnsCache;
            }

            _legacy016DisplayPawnsCache.Sort((left, right) =>
            {
                int leftPriority = WorkPrioritySystem.GetPriority(Better_Work_Tab.PawnCompat.WorkSettings(left), _legacy016SortingWorkType);
                int rightPriority = WorkPrioritySystem.GetPriority(Better_Work_Tab.PawnCompat.WorkSettings(right), _legacy016SortingWorkType);
                int result = leftPriority.CompareTo(rightPriority);
                if (result != 0)
                {
                    return _legacy016SortingDescending ? -result : result;
                }

                result = RowOrderUtility.GetPawnRowOrder(left).CompareTo(RowOrderUtility.GetPawnRowOrder(right));
                return result != 0 ? result : left.thingIDNumber.CompareTo(right.thingIDNumber);
            });

            return _legacy016DisplayPawnsCache;
        }

        private void DrawLegacy016PawnRowBackground(Rect rect, Pawn pawn, int visualIndex, int rowCount)
        {
            if (PawnOrganizer.API.PawnColorDatabase.TryGetColor(pawn, out var pawnColor) && pawnColor.a > 0f)
            {
                var overlay = new Color(pawnColor.r, pawnColor.g, pawnColor.b, Mathf.Clamp(pawnColor.a, 0.08f, 0.6f));
                Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, overlay);
            }

            if (Mouse.IsOver(rect))
            {
#if vAlpha4
                Widgets.DrawHighlight(rect);
#else
                GUI.DrawTexture(rect, TexUI.HighlightTex);
#endif
            }

            Rect labelRect = new Rect(rect.x, rect.y, Legacy016LeftColumnWidth - Legacy016CopyPasteWidth, rect.height);
#if !vAlpha4
            if (pawn.health.summaryHealth.SummaryHealthPercent < 0.99f)
            {
                Rect healthRect = new Rect(labelRect);
                healthRect.xMin -= 4f;
                healthRect.yMin += 4f;
                healthRect.yMax -= 6f;
                Widgets.FillableBar(
                    healthRect,
                    pawn.health.summaryHealth.SummaryHealthPercent,
#if v0_13
                    BaseContent.WhiteTex,
#elif v0_15
                    GenWorldUI.OverlayHealthTex,
#else
                    GenMapUI.OverlayHealthTex,
#endif
                    BaseContent.ClearTex,
                    doBorder: false);
            }
#endif

            if (Mouse.IsOver(labelRect))
            {
#if vAlpha4
                Widgets.DrawHighlight(labelRect);
#else
                GUI.DrawTexture(labelRect, TexUI.HighlightTex);
#endif
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Rect textRect = labelRect;
            textRect.xMin += 3f;
            Widgets.Label(textRect, Legacy016PawnLabel(pawn));
            Text.WordWrap = true;

            bool rowInputHandled = Legacy016HandleRowInput(labelRect, pawn, visualIndex, rowCount);
            if (!rowInputHandled && Better_Work_Tab.WidgetsCompat.ButtonInvisible(labelRect))
            {
#if vAlpha4
                Find.MainTabsRoot.EscapeCurrentTab();
                Find.Selector.ClearSelection();
                Find.CameraMap.JumpTo(pawn.Position);
#else
                Find.MainTabsRoot.EscapeCurrentTab(false);
                Find.Selector.ClearSelection();
                JumpToTargetUtility.TryJumpAndSelect(pawn);
#endif
            }
            else if (Mouse.IsOver(labelRect))
            {
                TipSignal tooltip = pawn.GetTooltip();
                tooltip.text = "ClickToJumpTo".Translate() + "\n\n" + tooltip.text;
                TooltipHandler.TipRegion(labelRect, tooltip);
            }
        }

        private bool Legacy016HandleRowInput(Rect labelRect, Pawn pawn, int visualIndex, int rowCount)
        {
            Event evt = Event.current;
            if (evt == null || pawn == null)
            {
                return false;
            }

            var settings = BetterWorkTabMod.Settings;
            bool overLabel = Mouse.IsOver(labelRect);
            if (evt.type == EventType.MouseDown && evt.button == 1 && overLabel &&
                (settings?.enableContextMenuOnRightClick ?? true))
            {
                ShowPawnContextMenu(pawn);
                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 1 && overLabel)
            {
                evt.Use();
                return true;
            }

            if (_legacy016SortingWorkType != null)
            {
                return false;
            }

            bool dragEnabled = settings?.enableDragDropReordering ?? true;
            bool rowDragEnabled = settings?.rowDraggingEnabled ?? true;
            if (!dragEnabled || !rowDragEnabled)
            {
                return false;
            }

            bool requireCtrl = settings?.requireCtrlForDrag ?? DefaultSettings.requireCtrlForDrag;
            bool ctrlSatisfied = !requireCtrl || evt.control;

            if (evt.type == EventType.MouseDown && evt.button == 0 && overLabel && ctrlSatisfied)
            {
                _legacy016PendingRowPawn = pawn;
                _legacy016PendingRowMouse = evt.mousePosition;
                _legacy016RowDragTargetIndex = visualIndex;
                evt.Use();
                return true;
            }

            if (_legacy016PendingRowPawn != pawn && _legacy016DraggingRowPawn != pawn)
            {
                return false;
            }

            if (evt.type == EventType.MouseDrag && _legacy016PendingRowPawn == pawn)
            {
                float threshold = Mathf.Max(1f, settings?.dragThreshold ?? DefaultSettings.dragThreshold);
                if ((evt.mousePosition - _legacy016PendingRowMouse).magnitude >= threshold)
                {
                    _legacy016RowDragActive = true;
                    _legacy016DraggingRowPawn = pawn;
                    BetterWorkTabLocalState.IsHeaderDragging = true;
                }

                if (_legacy016RowDragActive)
                {
                    _legacy016RowDragTargetIndex = Legacy016RowTargetIndex(evt.mousePosition.y, rowCount);
                    evt.Use();
                    return true;
                }
            }

            if (evt.type == EventType.MouseUp && _legacy016PendingRowPawn == pawn)
            {
                if (_legacy016RowDragActive)
                {
                    Legacy016CommitRowDrag(rowCount);
                }
                else if (overLabel)
                {
#if vAlpha4
                    Find.MainTabsRoot.EscapeCurrentTab();
                    Find.Selector.ClearSelection();
                    Find.CameraMap.JumpTo(pawn.Position);
#else
                    Find.MainTabsRoot.EscapeCurrentTab(false);
                    Find.Selector.ClearSelection();
                    JumpToTargetUtility.TryJumpAndSelect(pawn);
#endif
                }

                Legacy016CancelRowDrag();
                evt.Use();
                return true;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape &&
                (_legacy016PendingRowPawn != null || _legacy016RowDragActive))
            {
                Legacy016CancelRowDrag();
                evt.Use();
                return true;
            }

            return _legacy016RowDragActive && _legacy016DraggingRowPawn == pawn;
        }

        private int Legacy016RowTargetIndex(float mouseY, int rowCount)
        {
            return Mathf.Clamp(Mathf.FloorToInt((mouseY + 15f) / 30f), 0, rowCount);
        }

        private void DrawLegacy016RowDragOverlay(float width)
        {
            if (!_legacy016RowDragActive || _legacy016RowDragTargetIndex < 0)
            {
                return;
            }

            float y = Mathf.Max(0f, _legacy016RowDragTargetIndex * 30f);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(new Rect(0f, y - 1f, width, 2f), Color.white);
        }

        private void Legacy016CommitRowDrag(int rowCount)
        {
            Pawn pawn = _legacy016DraggingRowPawn;
            if (pawn == null)
            {
                return;
            }

            List<Pawn> ordered = Legacy016DisplayPawns();
            int oldIndex = ordered.IndexOf(pawn);
            if (oldIndex < 0)
            {
                return;
            }

            int insertIndex = Mathf.Clamp(_legacy016RowDragTargetIndex, 0, rowCount);
            if (insertIndex > oldIndex)
            {
                insertIndex--;
            }

            ordered.RemoveAt(oldIndex);
            ordered.Insert(Mathf.Clamp(insertIndex, 0, ordered.Count), pawn);

            for (int i = 0; i < ordered.Count; i++)
            {
                RowOrderUtility.SetPawnRowOrder(ordered[i], i);
            }

            pawns.Clear();
            pawns.AddRange(ordered);
            Legacy016InvalidateDisplayPawnCache();
            UISoundCompat.TickHigh.PlayOneShotOnCamera();
        }

        private void Legacy016CancelRowDrag()
        {
            _legacy016PendingRowPawn = null;
            _legacy016DraggingRowPawn = null;
            _legacy016RowDragActive = false;
            _legacy016RowDragTargetIndex = -1;
            BetterWorkTabLocalState.IsHeaderDragging = false;
        }

        private static void DrawLegacy016PawnRowOverlay(Rect rect, Pawn pawn)
        {
            if (Better_Work_Tab.PawnCompat.IsDowned(pawn))
            {
                GUI.color = new Color(1f, 0f, 0f, 0.5f);
                Widgets.DrawLineHorizontal(rect.x, rect.center.y, rect.width);
                GUI.color = Color.white;
            }
        }

        private static string Legacy016PawnLabel(Pawn pawn)
        {
#if vAlpha4
            return Better_Work_Tab.PawnCompat.LabelShortCap(pawn);
#else
            if (pawn.RaceProps.Humanlike || pawn.Name == null || pawn.Name.Numerical)
            {
                return pawn.LabelCap;
            }

            return pawn.Name.ToStringShort.CapitalizeFirst() + ", " + pawn.KindLabel;
#endif
        }

        protected override void DrawPawnRow(Rect rect, Pawn pawn)
        {
            float x = 165f;
#if !v0_14
            Action pasteAction = null;
            if (Legacy016Clipboard != null)
            {
                pasteAction = () => Legacy016PasteTo(pawn);
            }

            Rect copyPasteRect = new Rect(x, rect.y, Legacy016CopyPasteWidth, rect.height);
            CopyPasteUI.DoCopyPasteButtons(copyPasteRect, () => Legacy016CopyFrom(pawn), pasteAction);
            x = copyPasteRect.xMax;
#endif
            Text.Font = GameFont.Medium;
            float y = rect.y + 2.5f;

            for (int i = 0; i < _legacy016VisibleWorkTypes.Count; i++)
            {
                WorkTypeDef workType = _legacy016VisibleWorkTypes[i];
                bool incapable = Legacy016IsIncapableOfWholeWorkType(pawn, workType);
                Rect boxRect = new Rect(x, y, 25f, 25f);
                DrawLegacy016WorkBox(boxRect, pawn, workType, incapable);
#if v0_13
                if (Mouse.IsOver(boxRect))
                {
                    TooltipHandler.TipRegion(boxRect, WidgetsWork.TipForPawnWorker(pawn, workType, incapable));
                }
#else
#if vAlpha4
                if (Mouse.IsOver(boxRect))
                {
                    TooltipHandler.TipRegion(boxRect, WidgetsWork.TipForPawnWorker(pawn, workType));
                }
#else
                TooltipHandler.TipRegion(boxRect, () => WidgetsWork.TipForPawnWorker(pawn, workType, incapable), pawn.thingIDNumber ^ workType.GetHashCode());
#endif
#endif
                x += _legacy016WorkColumnSpacing;
            }

            Text.Font = GameFont.Small;
        }

        private static void DrawLegacy016WorkBox(Rect rect, Pawn pawn, WorkTypeDef workType, bool incapable)
        {
            if (pawn == null || workType == null || Better_Work_Tab.PawnCompat.WorkSettings(pawn) == null)
            {
                return;
            }

            if (pawn.WorkTypeIsDisabled(workType))
            {
                WidgetsWorkCompat.DrawWorkBoxFor(rect.x, rect.y, pawn, workType, incapable);
                return;
            }

            bool useManualPriorities = Verse.Current.Game?.playSettings?.useWorkPriorities ?? false;
            int currentPriority = WorkPrioritySystem.GetPriority(Better_Work_Tab.PawnCompat.WorkSettings(pawn), workType);
            int skillLevel = 0;
            bool skillOverlayRequested = !incapable && Legacy016ShouldShowSkillOverlay();
#if vAlpha4
            if (!useManualPriorities)
            {
                Legacy016HandlePriorityInput(rect, pawn, workType, currentPriority, useManualPriorities);

                if (skillOverlayRequested)
                {
                    CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(rect.x, rect.y, pawn, workType, incapable);
                    if (Legacy016TryGetSkillLevel(pawn, workType, out skillLevel))
                    {
                        Legacy016DrawSkillNumber(rect, skillLevel);
                    }

                    return;
                }

                CustomWorkBoxDrawer.DrawWorkBoxForPriorityOnly(rect.x, rect.y, pawn, workType, incapable);
                CustomWorkBoxDrawer.DrawAlpha4CheckboxState(rect, currentPriority > WorkPrioritySystem.DisabledPriority);
                return;
            }
#else
            if (!useManualPriorities)
            {
                WidgetsWorkCompat.DrawWorkBoxFor(rect.x, rect.y, pawn, workType, incapable);
                return;
            }
#endif

            if (skillOverlayRequested)
            {
                Legacy016HandlePriorityInput(rect, pawn, workType, currentPriority, useManualPriorities);

                if (Legacy016TryGetSkillLevel(pawn, workType, out skillLevel))
                {
                    CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(rect.x, rect.y, pawn, workType, incapable);
                    Legacy016DrawSkillNumber(rect, skillLevel);
                    CustomWorkBoxDrawer.DrawCompactPriority(rect, currentPriority);
                }
                else
                {
                    CustomWorkBoxDrawer.DrawWorkBoxForPriorityOnly(rect.x, rect.y, pawn, workType, incapable);
                }

                return;
            }

            CustomWorkBoxDrawer.DrawWorkBoxForPriorityOnly(rect.x, rect.y, pawn, workType, incapable);
            Legacy016HandlePriorityInput(rect, pawn, workType, currentPriority, useManualPriorities);
            CustomWorkBoxDrawer.DrawCenteredPriority(rect, currentPriority);
        }

        private static bool Legacy016ShouldShowSkillOverlay()
        {
            return (BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false) &&
                   ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;
        }

        private static bool Legacy016TryGetSkillLevel(Pawn pawn, WorkTypeDef workType, out int level)
        {
            level = 0;
            if (pawn?.skills == null || workType?.relevantSkills == null || workType.relevantSkills.Count == 0)
            {
                return false;
            }

            int currentFrame = Time.frameCount;
            var settings = BetterWorkTabMod.Settings;
            bool useCache = (settings?.enablePerformanceOptimizations ?? true) &&
                            (settings?.cacheSkillLevels ?? true);

            int key = (pawn.thingIDNumber << 16) ^ workType.shortHash;
            if (useCache &&
                Legacy016SkillLevelCacheTimestamps.TryGetValue(key, out int timestamp) &&
                currentFrame - timestamp < Legacy016SkillCacheFrameValidity &&
                Legacy016SkillLevelCache.TryGetValue(key, out int cachedLevel))
            {
                if (cachedLevel == Legacy016NoSkillLevel)
                {
                    return false;
                }

                level = cachedLevel;
                return true;
            }

            int total = 0;
            int count = 0;
            for (int i = 0; i < workType.relevantSkills.Count; i++)
            {
                SkillRecord skill = pawn.skills.GetSkill(workType.relevantSkills[i]);
                if (skill == null)
                {
                    continue;
                }

                total += SkillCompat.Level(skill);
                count++;
            }

            if (count == 0)
            {
                if (useCache)
                {
                    Legacy016SkillLevelCache[key] = Legacy016NoSkillLevel;
                    Legacy016SkillLevelCacheTimestamps[key] = currentFrame;
                }

                return false;
            }

            level = Mathf.Clamp(Mathf.RoundToInt(total / (float)count), 0, 20);
            if (useCache)
            {
                Legacy016SkillLevelCache[key] = level;
                Legacy016SkillLevelCacheTimestamps[key] = currentFrame;
                Legacy016TrimSkillCacheIfNeeded();
            }

            return true;
        }

        private static void Legacy016TrimSkillCacheIfNeeded()
        {
            if (Legacy016SkillLevelCache.Count <= 2000)
            {
                return;
            }

            Legacy016SkillLevelCache.Clear();
            Legacy016SkillLevelCacheTimestamps.Clear();
        }

        private static void Legacy016DrawSkillNumber(Rect rect, int level)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            string label = level.ToString();
            Text.Font = label.Length > 1 ? GameFont.Small : GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;

            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            Widgets.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label);

            GUI.color = Legacy016SkillColor(level);
            Widgets.Label(rect, label);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static Color Legacy016SkillColor(int level)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return Color.white;
            }

            if (level <= 3)
            {
                return settings.Color_VeryLowSkill;
            }

            if (level <= 9)
            {
                return settings.Color_LowSkill;
            }

            if (level <= 15)
            {
                return settings.Color_GoodLowSkill;
            }

            return settings.Color_ExcellentSkill;
        }

        private static void Legacy016HandlePriorityInput(Rect rect, Pawn pawn, WorkTypeDef workType, int currentPriority, bool useManualPriorities)
        {
            Event evt = Event.current;
            if (evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseUp || evt.type == EventType.Ignore)
            {
                Legacy016ClearPriorityPaint();
                return;
            }

            if (BetterWorkTabMod.Settings.enableScrollWheelPriority && evt.type == EventType.ScrollWheel && Mouse.IsOver(rect))
            {
                int nextPriority = WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, evt.delta.y > 0 ? 1 : 0, useManualPriorities);

                if (nextPriority != currentPriority)
                {
                    WorkPrioritySystem.SetPriority(Better_Work_Tab.PawnCompat.WorkSettings(pawn), workType, nextPriority);
                    if (useManualPriorities)
                    {
                        UISoundCompat.DragSlider.PlayOneShotOnCamera();
                    }
                    else if (nextPriority > WorkPrioritySystem.DisabledPriority)
                    {
                        UISoundCompat.CheckboxTurnedOn.PlayOneShotOnCamera();
                    }
                    else
                    {
                        UISoundCompat.CheckboxTurnedOff.PlayOneShotOnCamera();
                    }

                    WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                    Legacy016InvalidateFrameCaches();
                }

                evt.Use();
                return;
            }

            if (Mouse.IsOver(rect) && evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1))
            {
                int nextPriority = WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, evt.button, useManualPriorities);
                Legacy016ApplyPriority(pawn, workType, nextPriority, currentPriority, useManualPriorities);
                _legacy016PriorityPaintActive = true;
                _legacy016PriorityPaintValue = nextPriority;
                evt.Use();
                return;
            }

            if (_legacy016PriorityPaintActive && Mouse.IsOver(rect) && evt.type == EventType.MouseDrag)
            {
                Legacy016ApplyPriority(pawn, workType, _legacy016PriorityPaintValue, currentPriority, useManualPriorities);
                evt.Use();
            }
        }

        private static void Legacy016ApplyPriority(Pawn pawn, WorkTypeDef workType, int nextPriority, int currentPriority, bool useManualPriorities)
        {
            if (nextPriority == currentPriority)
            {
                return;
            }

            WorkPrioritySystem.SetPriority(Better_Work_Tab.PawnCompat.WorkSettings(pawn), workType, nextPriority);
            if (useManualPriorities)
            {
                UISoundCompat.DragSlider.PlayOneShotOnCamera();
            }
            else if (nextPriority > WorkPrioritySystem.DisabledPriority)
            {
                UISoundCompat.CheckboxTurnedOn.PlayOneShotOnCamera();
            }
            else
            {
                UISoundCompat.CheckboxTurnedOff.PlayOneShotOnCamera();
            }

            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            Legacy016InvalidateFrameCaches();
        }

        private static void Legacy016ClearPriorityPaint()
        {
            _legacy016PriorityPaintActive = false;
            _legacy016PriorityPaintValue = 0;
        }

        private void Legacy016InvalidateDisplayPawnCache()
        {
            _legacy016DisplayPawnsCacheFrame = -1;
            _legacy016DisplayPawnsSourceCount = -1;
            _legacy016DisplayPawnsCache.Clear();
            _legacy016DisplayPawnSeen.Clear();
        }

        private static void Legacy016InvalidateFrameCaches()
        {
            Legacy016SkillLevelCache.Clear();
            Legacy016IncapableCache.Clear();
            Legacy016SkillLevelCacheTimestamps.Clear();
            Legacy016IncapableCacheTimestamps.Clear();
        }

        private static bool Legacy016IsIncapableOfWholeWorkType(Pawn pawn, WorkTypeDef work)
        {
#if vAlpha4
            return false;
#else
            if (pawn?.health?.capacities == null || work == null)
            {
                return false;
            }

            int currentFrame = Time.frameCount;
            var settings = BetterWorkTabMod.Settings;
            bool useCache = (settings?.enablePerformanceOptimizations ?? true) &&
                            (settings?.cacheIncapabilityChecks ?? true);
            int key = (pawn.thingIDNumber << 16) ^ work.shortHash;
            if (useCache &&
                Legacy016IncapableCacheTimestamps.TryGetValue(key, out int timestamp) &&
                currentFrame - timestamp < Legacy016IncapableCacheFrameValidity &&
                Legacy016IncapableCache.TryGetValue(key, out bool cached))
            {
                return cached;
            }

            for (int i = 0; i < Better_Work_Tab.WorkTypeCompat.WorkGiversByPriority(work).Count; i++)
            {
                bool capableOfGiver = true;
                for (int j = 0; j < Better_Work_Tab.WorkTypeCompat.WorkGiversByPriority(work)[i].requiredCapacities.Count; j++)
                {
                    PawnCapacityDef capacity = Better_Work_Tab.WorkTypeCompat.WorkGiversByPriority(work)[i].requiredCapacities[j];
                    if (!pawn.health.capacities.CapableOf(capacity))
                    {
                        capableOfGiver = false;
                        break;
                    }
                }

                if (capableOfGiver)
                {
                    if (useCache)
                    {
                        Legacy016IncapableCache[key] = false;
                        Legacy016IncapableCacheTimestamps[key] = currentFrame;
                    }

                    return false;
                }
            }

            if (useCache)
            {
                Legacy016IncapableCache[key] = true;
                Legacy016IncapableCacheTimestamps[key] = currentFrame;
                Legacy016TrimIncapableCacheIfNeeded();
            }

            return true;
#endif
        }

        private static void Legacy016TrimIncapableCacheIfNeeded()
        {
            if (Legacy016IncapableCache.Count <= 2000)
            {
                return;
            }

            Legacy016IncapableCache.Clear();
            Legacy016IncapableCacheTimestamps.Clear();
        }

        private static void Legacy016CopyFrom(Pawn pawn)
        {
            if (Legacy016Clipboard == null)
            {
                Legacy016Clipboard = new DefMap<WorkTypeDef, int>();
            }

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefs)
            {
                Legacy016Clipboard[workType] = pawn.story.WorkTypeIsDisabled(workType)
                    ? WorkPrioritySystem.GetDefaultEnabledPriority()
                    : WorkPrioritySystem.GetPriority(Better_Work_Tab.PawnCompat.WorkSettings(pawn), workType);
            }
        }

        private static void Legacy016PasteTo(Pawn pawn)
        {
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefs)
            {
                if (!pawn.story.WorkTypeIsDisabled(workType))
                {
                    WorkPrioritySystem.SetPriority(Better_Work_Tab.PawnCompat.WorkSettings(pawn), workType, Legacy016Clipboard[workType]);
                }
            }
        }
#endif

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
