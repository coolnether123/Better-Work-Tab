using Better_Work_Tab.UI;
using HarmonyLib;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    public static class PawnTable_HighlightRowAndColumn
    {
        // Float menu highlighting - persists until cleared
        private static Pawn _floatMenuHighlightedPawn;
        private static WorkTypeDef _floatMenuHighlightedWorkType;

        /// <summary>
        /// Called when a pawn is right-clicked and "Assign Work" is selected from the float menu.
        /// Stores the pawn and work type to highlight until manually cleared.
        /// </summary>
        public static void SetWorktypeToHighlight(Pawn pawn, WorkTypeDef workType)
        {
            _floatMenuHighlightedPawn = pawn;
            _floatMenuHighlightedWorkType = workType;
        }

        /// <summary>
        /// Clears the float menu highlight (call when Work tab is closed or after some time).
        /// </summary>
        public static void ClearWorktypeHighlight()
        {
            _floatMenuHighlightedPawn = null;
            _floatMenuHighlightedWorkType = null;
        }

        /// <summary>
        /// Gets the currently highlighted pawn from float menu selection.
        /// </summary>
        public static Pawn GetHighlightedPawn()
        {
            return _floatMenuHighlightedPawn;
        }

        /// <summary>
        /// Gets the currently highlighted work type from float menu selection.
        /// </summary>
        public static WorkTypeDef GetHighlightedWorkType()
        {
            return _floatMenuHighlightedWorkType;
        }

        static void Prefix(PawnTable __instance, Vector2 position)
        {
            if (!BetterWorkTabMod.Settings.ShowPawnAndWorktypeHighlights)
                return;

            var worktypeColumns = __instance.columns.FindAll(
                a => a.workerClass == typeof(PawnColumnWorker_WorkPriority));

            if (!worktypeColumns.Any())
                return;

            // Handle float menu highlighting first (takes priority over hover highlighting)
            if (BetterWorkTabMod.Settings.ShowFloatMenuPawnAndWorktypeHighlight &&
                _floatMenuHighlightedPawn != null &&
                _floatMenuHighlightedWorkType != null)
            {
                HighlightFloatMenuSelection(__instance, position);
                return; // Skip hover highlighting when float menu highlight is active
            }

            // ===== GET MOUSE POSITION FIRST =====
            Vector2 mousePos = Event.current.mousePosition;

            Rect tableArea = new Rect(
                position.x,
                position.y + __instance.cachedHeaderHeight,
                __instance.cachedSize.x,
                __instance.cachedSize.y);

            // EARLY EXIT if mouse not over table
            if (!Mouse.IsOver(tableArea))
                return;

            // ===== CALCULATE WHICH ROW/COLUMN MOUSE IS OVER =====
            int hoveredRowIndex = GetRowIndexAtMouseY(
                __instance,
                position,
                mousePos.y);

            int hoveredColumnIndex = GetColumnIndexAtMouseX(
                __instance,
                position,
                mousePos.x);

            // ===== ONLY DRAW FOR THAT ROW/COLUMN =====
            if (hoveredRowIndex >= 0)
                HighlightSingleRow(__instance, position, hoveredRowIndex);

            if (hoveredColumnIndex >= 0)
                HighlightSingleColumn(__instance, position, hoveredColumnIndex);
        }

        /// <summary>
        /// Highlights the pawn and work type that were selected from the float menu context.
        /// </summary>
        private static void HighlightFloatMenuSelection(PawnTable table, Vector2 position)
        {
            // Find and highlight the row for the selected pawn
            int pawnRowIndex = table.cachedPawns.IndexOf(_floatMenuHighlightedPawn);
            if (pawnRowIndex >= 0)
                HighlightRowWithColor(table, position, pawnRowIndex, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);

            // Find and highlight the column for the selected work type
            int workTypeColumnIndex = table.columns.FindIndex(
                c => c.Worker is PawnColumnWorker_WorkPriority && c.workType == _floatMenuHighlightedWorkType);

            if (workTypeColumnIndex >= 0)
                HighlightColumnWithColor(table, position, workTypeColumnIndex, BetterWorkTabMod.Settings.Color_FloatMenuHighlight);
        }

        /// <summary>
        /// Highlights a specific row with the given color.
        /// </summary>
        private static void HighlightRowWithColor(
            PawnTable table,
            Vector2 position,
            int rowIndex,
            Color highlightColor)
        {
            if (rowIndex < 0 || rowIndex >= table.cachedRowHeights.Count)
                return;

            float rowY = CalculateRowY(table, position, rowIndex);
            float totalWidth = table.cachedColumnWidths.Sum();

            var rect = new Rect(position.x, rowY, totalWidth, table.cachedRowHeights[rowIndex]);
            HighlightDrawer.DrawHighlight(rect, highlightColor);
        }

        /// <summary>
        /// Highlights a specific column with the given color.
        /// </summary>
        private static void HighlightColumnWithColor(
            PawnTable table,
            Vector2 position,
            int columnIndex,
            Color highlightColor)
        {
            if (columnIndex < 0 || columnIndex >= table.cachedColumnWidths.Count)
                return;

            float columnX = CalculateColumnX(table, position, columnIndex);
            float totalHeight = table.cachedRowHeights.Sum() + table.cachedHeaderHeight;

            var rect = new Rect(
                columnX,
                position.y,
                table.cachedColumnWidths[columnIndex],
                totalHeight);

            HighlightDrawer.DrawHighlight(rect, highlightColor);
        }

        /// <summary>
        /// Calculates the Y position of a row based on cumulative row heights.
        /// </summary>
        private static float CalculateRowY(PawnTable table, Vector2 position, int rowIndex)
        {
            float y = position.y + table.cachedHeaderHeight;
            for (int i = 0; i < rowIndex; i++)
                y += table.cachedRowHeights[i];
            return y;
        }

        /// <summary>
        /// Calculates the X position of a column based on cumulative column widths.
        /// </summary>
        private static float CalculateColumnX(PawnTable table, Vector2 position, int columnIndex)
        {
            float x = position.x;
            for (int i = 0; i < columnIndex; i++)
                x += table.cachedColumnWidths[i];
            return x;
        }

        // Get which row index the mouse Y is currently over
        private static int GetRowIndexAtMouseY(
            PawnTable table,
            Vector2 tableOrigin,
            float mouseY)
        {
            float cumulativeY = tableOrigin.y + table.cachedHeaderHeight;

            for (int i = 0; i < table.cachedRowHeights.Count; i++)
            {
                cumulativeY += table.cachedRowHeights[i];
                if (mouseY < cumulativeY)
                    return i;
            }

            return -1; // Not over any row
        }

        // Get which column index the mouse X is currently over
        private static int GetColumnIndexAtMouseX(
            PawnTable table,
            Vector2 tableOrigin,
            float mouseX)
        {
            float cumulativeX = tableOrigin.x;

            for (int i = 0; i < table.cachedColumnWidths.Count; i++)
            {
                cumulativeX += table.cachedColumnWidths[i];
                if (mouseX < cumulativeX)
                    return i;
            }

            return -1; // Not over any column
        }

        // Only highlight the ONE row mouse is over
        private static void HighlightSingleRow(
            PawnTable table,
            Vector2 position,
            int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= table.cachedPawns.Count)
                return;

            float rowY = CalculateRowY(table, position, rowIndex);
            float totalWidth = table.cachedColumnWidths.Sum();

            var rect = new Rect(position.x, rowY, totalWidth, table.cachedRowHeights[rowIndex]);

            // Highlight logic
            if (BetterWorkTabMod.Settings.DoSelectedPawnHighlight &&
            Find.Selector.IsSelected(table.cachedPawns[rowIndex]))
            {
                HighlightDrawer.DrawHighlight(rect, BetterWorkTabMod.Settings.Color_CursorHighlight);
            }

            if (BetterWorkTabMod.Settings.ShowCursorPawnAndWorktypeHighlight)
            {
                HighlightDrawer.DrawHighlight(rect, BetterWorkTabMod.Settings.Color_MouseHoverHighlight);
                Widgets.DrawHighlight(rect);
            }
        }

        // Only highlight the ONE column mouse is over
        private static void HighlightSingleColumn(
            PawnTable table,
            Vector2 position,
            int colIndex)
        {
            if (colIndex < 0 || colIndex >= table.columns.Count)
                return;

            if (!(table.columns[colIndex].Worker is PawnColumnWorker_WorkPriority))
                return;

            float columnX = CalculateColumnX(table, position, colIndex);
            float totalHeight = table.cachedRowHeights.Sum() + table.cachedHeaderHeight;

            var rect = new Rect(
                columnX,
                position.y,
                table.cachedColumnWidths[colIndex],
                totalHeight);

            HighlightDrawer.DrawHighlight(rect, BetterWorkTabMod.Settings.Color_MouseHoverHighlight);
            Widgets.DrawHighlight(rect);

            // Highlight similar work types
            if (table.columns[colIndex].workType != null)
            {
                HighlightSimilarWorktypes(
                    table.columns[colIndex].workType,
                    table,
                    position,
                    colIndex,
                    totalHeight);
            }
        }

        private static void HighlightSimilarWorktypes(
            WorkTypeDef worktype,
            PawnTable table,
            Vector2 position,
            int myIndex,
            float totalHeight)
        {
            var relevantSkills = worktype.relevantSkills;
            float startingX = position.x;

            for (int i = 0; i < table.columns.Count; i++)
            {
                if (!(table.columns[i].Worker is PawnColumnWorker_WorkPriority))
                {
                    startingX += table.cachedColumnWidths[i];
                    continue;
                }

                if (i != myIndex && table.columns[i].workType != null)
                {
                    // Only check if they share skills
                    foreach (var skill in relevantSkills)
                    {
                        if (table.columns[i].workType.relevantSkills.Contains(skill))
                        {
                            var rect = new Rect(
                                startingX,
                                position.y,
                                table.cachedColumnWidths[i],
                                totalHeight);

                            Widgets.DrawBoxSolid(rect, BetterWorkTabMod.Settings.Color_SimilarWorktypeMouseOver);
                            Widgets.DrawHighlight(rect);
                            break; // Found match, move to next column
                        }
                    }
                }

                startingX += table.cachedColumnWidths[i];
            }
        }
    }
}