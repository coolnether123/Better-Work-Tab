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
            RecomputeMovedColumnsFromCurrentOrder();
            // Ensure saved order matches current live table:
            WorkColumnOrderManager.CaptureCurrent(PawnTableDefOf.Work);
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

                var organizer = PawnOrganizerSystem.Instance;
                if (organizer?.Layout != null)
                {
                    var snapshot = BuildSnapshotForOrganizer(table);
                    organizer.Update(table, Vector2.zero, snapshot);
                }

                // Read table.Size - this triggers RecacheIfDirty and our postfix
                float tableHeight = table.Size.y;
                float tableWidth = table.Size.x;

                float finalHeight = tableHeight + ExtraBottomSpace + ExtraTopSpace + Margin * 2f;

                return new Vector2(tableWidth + Margin * 2f, finalHeight);
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
            var vanillaOrder = WorkColumnOrderManager.GetVanillaOrder();

            if (vanillaOrder == null || vanillaOrder.Count == 0)
                return true; // Assume vanilla position if we can't determine

            var currentOrder = BetterWorkTabMod.Settings.workColumnOrderDefNames;
            if (currentOrder == null || currentOrder.Count == 0)
                return true; // No custom order, so it's vanilla

            // Find positions in both lists
            int vanillaPos = vanillaOrder.IndexOf(workType.defName);
            int currentPos = currentOrder.IndexOf(workType.defName);

            // If not found in vanilla or current, assume vanilla
            if (vanillaPos < 0 || currentPos < 0)
                return true;

            return vanillaPos == currentPos;
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

        internal static bool IsColumnMarkedAsMoved(WorkTypeDef workType)
        {
            if (workType == null) return false;
            return _movedColumns.Contains(workType.defName);
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
                // This is the same list used by the sizing system, guaranteeing layout consistency
                var rowDescriptors = layout.GetRowDescriptors();
                if (rowDescriptors == null || rowDescriptors.Count == 0)
                {
                    return;
                }

                var nameColumn = FindNameColumn(layout.Columns);

                // === CALCULATE TOTALS FOR HIGHLIGHTING ===
                float totalWidth = 0f;
                foreach (var col in layout.Columns)
                {
                    totalWidth += col.Width;
                }

                float totalHeight = layout.ContentHeight;

                // === HIGHLIGHT ROWS (HORIZONTAL) ===
                // Loop through descriptors to highlight selected and hovered rows
                // Calculate Y positions as we go (same as your original startingY)
                float currentY = 0f;
                for (int i = 0; i < rowDescriptors.Count; i++)
                {
                    var descriptor = rowDescriptors[i];
                    Rect rowRect = new Rect(0f, currentY, totalWidth, descriptor.Height);

                    // Highlight selected row (only for pawns, dividers can't be selected)
                    if (descriptor.IsPawn && Find.Selector.IsSelected(descriptor.Pawn))
                    {
                        if (BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight && PawnTable_HighlightRowAndColumn.worktypeToHighlight != null)
                            Widgets.DrawBoxSolid(rowRect, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);
                        else if (BetterWorkTabMod.Settings.DoSelectedPawnHighlight)
                            Widgets.DrawBoxSolid(rowRect, BetterWorkTabMod.Settings.Color_CursorHighlight);
                    }

                    // Highlight hovered row
                    if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(rowRect))
                        Widgets.DrawBoxSolid(rowRect, BetterWorkTabMod.Settings.Color_MouseHoverHighlight);

                    currentY += descriptor.Height;
                }

                // === HIGHLIGHT COLUMNS (VERTICAL) ===
                float startingX = 0f;
                foreach (var column in layout.Columns)
                {
                    Rect columnRect = new Rect(startingX, 0f, column.Width, totalHeight);

                    bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;

                    // Highlight float menu worktype column
                    if (isWorkColumn && BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight &&
                        PawnTable_HighlightRowAndColumn.worktypeToHighlight == column.Column.workType)
                        Widgets.DrawBoxSolid(columnRect, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);

                    // Highlight hovered column and related worktypes
                    if (isWorkColumn && BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight && Mouse.IsOver(columnRect))
                    {
                        Color useColor = BetterWorkTabMod.Settings.Color_MouseHoverHighlight;
                        Widgets.DrawBoxSolid(columnRect, useColor);
                        Widgets.DrawHighlight(columnRect);

                        // Highlight other columns that share relevant skills (similar worktypes)
                        HighlightSimilarWorktypes(column.Column.workType, layout.Columns, column, layout);
                    }

                    startingX += column.Width;
                }

                // === NOW DRAW ACTUAL CONTENT ===
                // Loop through descriptors in the same order as highlighting, creating temporary wrappers for rendering
                currentY = 0f;
                for (int i = 0; i < rowDescriptors.Count; i++)
                {
                    var descriptor = rowDescriptors[i];
                    Rect rowRect = new Rect(0f, currentY, viewRect.width, descriptor.Height);

                    // Create temporary wrapper objects to maintain compatibility with existing draw methods
                    // These are small allocations; only optimize with pooling if profiling shows it's necessary
                    if (descriptor.IsPawn)
                    {
                        // Create wrapper for pawn rendering (preserves all selection/highlight state behavior)
                        var pawnElement = new PawnElement(descriptor.Pawn);
                        var renderRow = new WorkTabLayoutRow(pawnElement, currentY, descriptor.Height, i);

                        DrawRowBackground(renderRow, rowRect);
                        DrawPawnRow(table, renderRow, rowRect, layout.Columns);
                        DrawPawnRowOverlay(renderRow, rowRect);
                    }
                    else if (descriptor.IsDivider)
                    {
                        // Create wrapper for divider rendering (preserves collapse state and styling)
                        var dividerElement = new DividerElement(descriptor.Divider);
                        var renderRow = new WorkTabLayoutRow(dividerElement, currentY, descriptor.Height, i);

                        DrawRowBackground(renderRow, rowRect);
                        if (nameColumn.HasValue)
                        {
                            DrawDividerRow(descriptor.Divider, rowRect, nameColumn.Value);
                        }
                    }

                    // Draw horizontal separator line between rows
                    GUI.color = new Color(1f, 1f, 1f, 0.12f);
                    Widgets.DrawLineHorizontal(0f, rowRect.yMax - 1f, viewRect.width);
                    GUI.color = Color.white;

                    currentY += descriptor.Height;
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

        private void HighlightSimilarWorktypes(WorkTypeDef worktype, IReadOnlyList<WorkTabLayoutColumn> columns, WorkTabLayoutColumn myColumn, IWorkTabLayoutController layout)
        {
            var relevantSkills = worktype.relevantSkills;
            float startingX = 0f;
            float totalHeight = layout.ContentHeight;

            foreach (var column in columns)
            {
                bool isWorkColumn = column.Column?.Worker is PawnColumnWorker_WorkPriority;

                if (isWorkColumn && column.Column.workType != worktype)
                {
                    var rect = new Rect(startingX, 0f, column.Width, totalHeight);

                    foreach (var skill in relevantSkills)
                    {
                        if (column.Column.workType.relevantSkills.Contains(skill))
                        {
                            Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_SimilarWorktypeMouseOver);
                            Widgets.DrawHighlight(rect);
                            break;
                        }
                    }
                }

                startingX += column.Width;
            }
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

        /// <summary>
        /// Check if a specific column is out of its vanilla position
        /// </summary>
        internal static bool IsColumnOutOfVanillaPosition(WorkTypeDef workType)
        {
            return IsColumnMarkedAsMoved(workType);
        }
    }
}
