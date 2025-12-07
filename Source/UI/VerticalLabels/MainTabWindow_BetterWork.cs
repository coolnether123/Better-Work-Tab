using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Patches;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer.Data;
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

        private const float RightEdgeMargin = 10f;
        private const float InfoIconSize = 24f;

        private static bool _columnsReordered;
        private static readonly HashSet<string> _movedColumns = new HashSet<string>();
        private static readonly Color ColumnReorderTint = new Color(1f, 0.85f, 0.2f, 0.28f);

        private PawnColumnDef _lastSortColumn;
        private bool _lastSortDescending;

        private static Color CurrentRowTextColor = Color.white;

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


        public override void PreOpen()
        {
            base.PreOpen();
            closeOnClickedOutside = !BetterWorkTabMod.Settings.disableLeftClickClose;

            _lastSortColumn = null;
            _lastSortDescending = false;

            if (PawnOrganizerSystem.Instance == null)
            {
                var widthStore = new ColumnWidthPersistence();
                new PawnOrganizerSystem(widthStore);
            }

            // Check if columns are already out of order from a saved game
            CheckAndMarkReorderedColumns();
        }

        private void CheckAndMarkReorderedColumns()
        {
            // Ensure saved order matches current live table:
            WorkColumnOrderManager.CaptureCurrent(PawnTableDefOf.Work);

            RecomputeMovedColumnsFromCurrentOrder();
        }

        public override void DoWindowContents(Rect inRect)
        {
            PawnTable table = GetPawnTable();
            if (table == null)
            {
                return;
            }


            var organizer = PawnOrganizerSystem.Instance;
            Vector2 tableOrigin = new Vector2(inRect.x, inRect.y + ExtraTopSpace);
            var snapshot = BuildSnapshotForOrganizer(table);

            organizer?.Update(table, tableOrigin, snapshot);

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

            Rect infoRect = GetInfoIconRect(inRect);
            DrawBottomRightButtons(inRect, infoRect);
            DrawInfoButton(infoRect);
            DrawBottomCounters(inRect, table);
        }

        private IPawnOrganizerSnapshot BuildSnapshotForOrganizer(PawnTable table)
        {
            var pawns = new List<Pawn>(table.PawnsListForReading);
            var comp = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            var dividers = comp?.CurrentWorklist?.Dividers ?? new List<PawnDivider>();
            return new WorkTabSnapshot(pawns, dividers);
        }

        private void ProcessRightClicks(IWorkTabLayoutController layout)
        {
            if (layout == null)
            {
                return;
            }

            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 1)
            {
                return;
            }


            if (!layout.TryGetRowAt(evt.mousePosition, out var row))
            {
                return;
            }

            if (row.Divider != null)
            {
                ShowDividerContextMenu(row.Divider);
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
                ShowPawnContextMenu(row.Pawn);
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

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ShowRenamePawnDialog(Pawn pawn)
        {
            Find.WindowStack.Add(pawn.NamePawnDialog());
        }

        private void ShowDividerContextMenu(PawnDivider divider)
        {
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
                })
            };

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

            layout.AddDividerBeforePawn(pawn, "New Divider", Color.gray);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }


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
                    var snapshot = BuildSnapshotForOrganizer(table);
                    organizer.Update(table, Vector2.zero, snapshot);

                    // Use layout controller's content height (includes dividers)
                    float layoutHeight = organizer.Layout.HeaderHeight + organizer.Layout.ContentHeight;
                    finalHeight = layoutHeight + ExtraBottomSpace + ExtraTopSpace + Margin * 2f;
                    finalWidth = table.Size.x + Margin * 2f + 20f; // Added 20f to stop headers from clipping edge
                }
                else
                {
                    // Fallback to vanilla size if organizer not ready
                    finalHeight = table.Size.y + ExtraBottomSpace + ExtraTopSpace + Margin * 2f;
                    finalWidth = table.Size.x + Margin * 2f + 20f; // Same as above
                }

                // Determine max height: use setting if configured, otherwise vanilla default (fill screen)
                float targetMaxHeight = BetterWorkTabMod.Settings.workTabMaxHeight > 0f
                    ? BetterWorkTabMod.Settings.workTabMaxHeight
                    : Verse.UI.screenHeight - 35f;  // Vanilla default: screen height minus top bar

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

            layout.AddDividerAfterPawn(pawn, "New Divider", Color.gray);
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
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
                // Check if THIS specific column is out of vanilla position
                bool isOutOfVanilla = workType != null && IsColumnOutOfVanillaPosition(workType);
                bool showReorder = _columnsReordered && isWorkColumn && workType != null && isOutOfVanilla;

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
        /// Checks if the column order has been modified from vanilla.
        /// </summary>
        private static bool IsColumnInVanillaPosition(WorkTypeDef workType)
        {
            if (workType?.defName == null) return true;  // Assume vanilla if unknown

            var vanillaOrder = WorkColumnOrderManager.GetVanillaOrder();
            if (vanillaOrder?.Count == 0) return true;  // Can't determine, assume vanilla

            var def = PawnTableDefOf.Work;
            if (def?.columns == null) return true;

            var currentOrder = def.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .Select(c => c.workType.defName)
                .ToList();

            int vanillaPos = vanillaOrder.IndexOf(workType.defName);
            int currentPos = currentOrder.IndexOf(workType.defName);

            if (vanillaPos < 0 || currentPos < 0) return true;  // Not found, assume vanilla

            return vanillaPos == currentPos;
        }

        // Use the single method everywhere
        internal static bool IsColumnOutOfVanillaPosition(WorkTypeDef workType)
        {
            return !IsColumnInVanillaPosition(workType);
        }

        internal static bool IsColumnMarkedAsMoved(WorkTypeDef workType)
        {
            return !IsColumnInVanillaPosition(workType);  // Consistent
        }


        private void DrawColumnReorderMarker(Rect headerRect)
        {
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            var prevColor = GUI.color;

            Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 0.92f, 0.25f);

            Rect starRect = new Rect(headerRect.x, headerRect.y + 2f, headerRect.width, 12f);
            Widgets.Label(starRect, "*");

            GUI.color = prevColor;
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        [SyncMethod]
        internal static void MarkColumnMoved(WorkTypeDef workType)
        {
            if (workType?.defName == null) return;

            var vanillaOrder = WorkColumnOrderManager.GetVanillaOrder();
            if (vanillaOrder?.Count == 0)
            {
                Log.Warning("[BWT] Vanilla column order not available.");
                return;
            }

            // Read from LIVE table
            var def = PawnTableDefOf.Work;
            if (def?.columns == null) return;

            var liveOrder = def.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .Select(c => c.workType.defName)
                .ToList();

            if (liveOrder.Count == 0) return;

            int vanillaPos = vanillaOrder.IndexOf(workType.defName);
            int livePos = liveOrder.IndexOf(workType.defName);

            if (vanillaPos < 0)
            {
                Log.Warning($"[BWT] Worktype {workType.defName} not in vanilla order.");
                return;
            }

            if (livePos < 0)
            {
                Log.Warning($"[BWT] Worktype {workType.defName} not in live order.");
                return;
            }

            if (vanillaPos == livePos)
            {
                _movedColumns.Remove(workType.defName);
            }
            else
            {
                _movedColumns.Add(workType.defName);
            }

            _columnsReordered = _movedColumns.Count > 0;
        }


        internal static void ClearAllMovedMarks()
        {
            _movedColumns.Clear();
            _columnsReordered = false;
        }

        internal static bool ColumnsReordered => _columnsReordered;

        private static void RecomputeMovedColumnsFromCurrentOrder()
        {
            _movedColumns.Clear();

            var def = PawnTableDefOf.Work;
            if (def?.columns == null)
            {
                _columnsReordered = false;
                return;
            }

            // Use the actual current table order instead of relying solely on settings
            var currentOrder = def.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .Select(c => c.workType.defName)
                .ToList();

            var vanillaOrder = WorkColumnOrderManager.GetVanillaOrder();

            if (vanillaOrder == null || vanillaOrder.Count == 0 ||
                currentOrder == null || currentOrder.Count == 0)
            {
                _columnsReordered = false;
                return;
            }

            for (int i = 0; i < currentOrder.Count; i++)
            {
                string defName = currentOrder[i];
                int vanillaPos = vanillaOrder.IndexOf(defName);
                if (vanillaPos >= 0 && vanillaPos != i)
                {
                    _movedColumns.Add(defName);
                }
            }

            _columnsReordered = _movedColumns.Count > 0;
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
                DrawAllRowContent(table, rowDescriptors, layout.Columns, viewRect.width, nameColumn);

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
            // Get float menu highlight state (persists across frames)
            Pawn highlightedPawn = PawnTable_HighlightRowAndColumn.GetHighlightedPawn();
            WorkTypeDef highlightedWorkType = PawnTable_HighlightRowAndColumn.GetHighlightedWorkType();

            // === HORIZONTAL HIGHLIGHTS (Rows) ===
            // Highlight selected or hovered pawn rows.
            // Uses descriptor heights so dividers are properly accounted for.
            float currentY = 0f;
            for (int i = 0; i < rowDescriptors.Count; i++)
            {
                var descriptor = rowDescriptors[i];
                Rect rowRect = new Rect(0f, currentY, totalWidth, descriptor.Height);

                // Highlight float menu selected pawn
                if (descriptor.IsPawn &&
                    highlightedPawn != null &&
                    descriptor.Pawn == highlightedPawn &&
                    BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight)
                {
                    Widgets.DrawBoxSolid(rowRect, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);
                }
                // Highlight selected row (only pawns can be selected)
                else if (descriptor.IsPawn && Find.Selector.IsSelected(descriptor.Pawn))
                {
                    if (BetterWorkTabMod.Settings.DoSelectedPawnHighlight)
                    {
                        Widgets.DrawBoxSolid(rowRect, BetterWorkTabMod.Settings.Color_CursorHighlight);
                    }
                }

                // Highlight hovered row (works for both pawns and dividers)
                if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rowRect))
                {
                    Widgets.DrawBoxSolid(rowRect, BetterWorkTabMod.Settings.Color_MouseHoverHighlight);
                }

                currentY += descriptor.Height;
            }

            // === VERTICAL HIGHLIGHTS (Columns) ===
            // Highlight entire columns for worktype-related interactions.
            // totalHeight already accounts for all rows including variable-height dividers.
            float startingX = 0f;
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                Rect columnRect = new Rect(startingX, 0f, column.Width, totalHeight);

                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;

                // Highlight float menu worktype column (when opened via right-click on work cell)
                if (isWorkColumn &&
                    highlightedWorkType != null &&
                    column.Column.workType == highlightedWorkType &&
                    BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight)
                {
                    Widgets.DrawBoxSolid(columnRect, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);
                }
                // Highlight hovered column and related worktypes
                else if (isWorkColumn &&
                    BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight &&
                    Mouse.IsOver(columnRect))
                {
                    Color useColor = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
                    Widgets.DrawBoxSolid(columnRect, useColor);
                    Widgets.DrawHighlight(columnRect);

                    // Also highlight columns that share relevant skills (dimmer highlight)
                    DrawSimilarWorktypeHighlights(column, columns, totalHeight);
                }

                startingX += column.Width;
            }
        }

        /// <summary>
        /// Helper for DrawAllHighlights: highlights other columns that share relevant skills
        /// with the currently hovered worktype column.
        /// </summary>
        private void DrawSimilarWorktypeHighlights(
            WorkTabLayoutColumn hoveredColumn,
            IReadOnlyList<WorkTabLayoutColumn> allColumns,
            float totalHeight)
        {
            var hoveredWorkType = hoveredColumn.Column.workType;
            var relevantSkills = hoveredWorkType.relevantSkills;

            float startingX = 0f;
            for (int i = 0; i < allColumns.Count; i++)
            {
                var column = allColumns[i];
                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;

                // Skip the column we're already hovering over
                if (isWorkColumn && column.Column.workType != hoveredWorkType)
                {
                    // Check if this column shares any relevant skills
                    foreach (var skill in relevantSkills)
                    {
                        if (column.Column.workType.relevantSkills.Contains(skill))
                        {
                            Rect columnRect = new Rect(startingX, 0f, column.Width, totalHeight);
                            Widgets.DrawBoxSolid(columnRect, BetterWorkTabMod.Settings.Color_SimilarWorktypeMouseOver);
                            Widgets.DrawHighlight(columnRect);
                            break; // Only highlight once per column
                        }
                    }
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
            IReadOnlyList<WorkTabLayoutColumn> columns,
            float viewWidth,
            WorkTabLayoutColumn? nameColumn)
        {
            float currentY = 0f;

            for (int i = 0; i < rowDescriptors.Count; i++)
            {
                var descriptor = rowDescriptors[i];
                Rect rowRect = new Rect(0f, currentY, viewWidth, descriptor.Height);

                // Create temporary wrapper objects to maintain compatibility with existing draw methods.
                // These are small allocations; only optimize with pooling if profiling shows it's necessary.
                if (descriptor.IsPawn)
                {
                    // Wrap pawn in element and row for rendering (preserves selection/highlight state)
                    var pawnElement = new PawnElement(descriptor.Pawn);
                    var renderRow = new WorkTabLayoutRow(pawnElement, currentY, descriptor.Height, i);

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
                    var renderRow = new WorkTabLayoutRow(dividerElement, currentY, descriptor.Height, i);

                    // 1. Draw divider background (uses divider color, dimmed if collapsed)
                    DrawRowBackground(renderRow, rowRect);

                    // 2. Draw divider label and collapse arrow (only in name column)
                    if (nameColumn.HasValue)
                    {
                        DrawDividerRow(descriptor.Divider, rowRect, nameColumn.Value);
                    }
                }

                currentY += descriptor.Height;
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
                var dividerColor = row.Divider.DividerColor;
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
            viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(contentHeight, outRect.height));

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
            Rect arrowRect = new Rect(labelCellRect.xMin + 6f, labelCellRect.y + (labelCellRect.height - 16f) / 2f, 18f, 16f);
            string arrowChar = divider.IsCollapsed ? "▶" : "▼";
            if (Widgets.ButtonInvisible(arrowRect))
            {
                ToggleDividerCollapsed(divider);
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

            divider.IsCollapsed = !divider.IsCollapsed;
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        private void DrawPawnRow(PawnTable table, WorkTabLayoutRow row, Rect rowRect, IReadOnlyList<WorkTabLayoutColumn> columns)
        {
            foreach (var column in columns)
            {
                Rect cellRect = new Rect(column.OffsetX, rowRect.y, column.Width, rowRect.height);
                column.Column.Worker.DoCell(cellRect, row.Pawn, table);
            }
        }

        private void DrawPawnRowOverlay(WorkTabLayoutRow row, Rect rowRect)
        {
            if (row.Pawn == null)
            {
                return;
            }

            if (Find.Selector.IsSelected(row.Pawn))
            {
                Widgets.DrawHighlight(rowRect, 0.6f);
            }

            if (BetterWorkTabMod.Settings.enableRowColumnHighlights && Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
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

            var originalAnchor = Text.Anchor;
            var originalFont = Text.Font;
            var originalColor = GUI.color;

            try
            {
                GUI.color = Spine.UI.TextColorHelper.GetContrastingTextColor(divider.DividerColor);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = divider.LabelFont;

                // Increase the left indent to match pawn name padding
                labelCellRect.xMin += 33f; 

                Widgets.Label(labelCellRect, divider.DividerName ?? "Divider");
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
                    Widgets.Label(new Rect(rect.x, rect.yMax - 6f, rect.width, 60f), "PriorityOneDoneFirst".Translate());
                }
            }
            else
            {
                UIHighlighter.HighlightOpportunity(rect, "ManualPriorities-Off");
            }
        }

        private void DrawPriorityLegend(Rect rect)
        {
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
            Rect textRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            string dragInstruction = BetterWorkTabMod.Settings.requireCtrlForDrag
                ? "Ctrl + drag to reorder"
                : "Drag to reorder";
            Widgets.Label(textRect, $"Shift toggles overlay | {dragInstruction}");
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawInfoButton(Rect gearRect)
        {
            if (Widgets.ButtonImage(gearRect, TexButton.Info))
            {
                var mod = LoadedModManager.GetMod<BetterWorkTabMod>();
                if (mod != null)
                {
                    Find.WindowStack.Add(new Dialog_ModSettings(mod));
                }
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
            bool showPawns = BetterWorkTabMod.Settings.showPawnCountAtBottom;
            bool showBeds = BetterWorkTabMod.Settings.showBedCountAtBottom;
            if (!showPawns && !showBeds)
            {
                return;
            }

            int pawnCount = showPawns ? table?.cachedPawns?.Count ?? 0 : 0;
            int bedCount = 0;
            if (showBeds)
            {
                Map map = Find.CurrentMap;
                if (map?.listerBuildings != null)
                {
                    var beds = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>();
                    if (beds != null)
                    {
                        foreach (var bed in beds)
                        {
                            if (bed == null || bed.ForPrisoners || bed.Faction != Faction.OfPlayer)
                            {
                                continue;
                            }

                            bedCount += bed.SleepingSlotsCount;
                        }
                    }
                }
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

            // Draw bed count in red if less than pawns, otherwise gray
            if (showBeds)
            {
                string bedLabel = showPawns ? $" | Beds: {bedCount}" : $"Beds: {bedCount}";
                float colonistWidth = showPawns ? Text.CalcSize($"Colonists: {pawnCount}").x : 0f;
                Rect bedRect = new Rect(rect.x + colonistWidth, rect.y, rect.width - colonistWidth, rect.height);

                if (bedCount < pawnCount)
                {
                    GUI.color = new Color(0.8f, 0.1f, 0.1f); // Darker red
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
        }

        public override void PostClose()
        {
            base.PostClose();
            // Clear float menu highlights when Work tab is closed
            PawnTable_HighlightRowAndColumn.ClearWorktypeHighlight();
        }
    }
}
