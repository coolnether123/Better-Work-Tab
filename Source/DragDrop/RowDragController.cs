using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles custom drag-and-drop for Work tab rows (pawns).
    /// Drag sets a new order visually and on drop we rewrite displayOrder for visible pawns.
    /// </summary>
    internal static class RowDragController
    {
        internal static void OnGUI(PawnTable table, Vector2 origin)
        {
            // Only act for Work tab (must have work-priority columns)
            var columns = table.Columns;
            bool isWork = columns != null && columns.Exists(c => c.Worker is PawnColumnWorker_WorkPriority);
            if (!isWork)
                return;

            var evt = Event.current;
            var outRect = new Rect((int)origin.x, (int)(origin.y + table.HeaderHeight), (int)table.Size.x, (int)(table.Size.y - table.HeaderHeight));
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, (int)(table.HeightNoScrollbar - table.HeaderHeight));

            // Short-circuit if a column drag is in progress.
            if (WorkTabDragState.Kind == WorkTabDragState.DragKind.Column)
                return;

            // Only start a row drag when Ctrl is held to preserve normal cell interactions
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control && outRect.Contains(evt.mousePosition))
            {
                TryBeginRowDrag(table, origin, evt.mousePosition);
                if (WorkTabDragState.Kind == WorkTabDragState.DragKind.Row)
                {
                    Util.WorkTabLogger.Info(Util.WorkTabLogger.Categories.DragRow, $"Begin row drag: fromRow={WorkTabDragState.FromRow}, pawn='{WorkTabDragState.DragPawn?.LabelShort ?? "?"}', rect={WorkTabDragState.RowOriginRect}");
                    evt.Use();
                    return;
                }
            }

            if (WorkTabDragState.Kind == WorkTabDragState.DragKind.Row && WorkTabDragState.IsActiveFor(table))
            {
                if (evt.type == EventType.MouseDrag)
                {
                    WorkTabDragState.RowDragDY = evt.mousePosition.y - WorkTabDragState.MouseStart.y;
                    UpdateRowInsertionIndex(table, origin, evt.mousePosition);
                    Util.WorkTabLogger.Debug(Util.WorkTabLogger.Categories.DragRow, $"Drag row move: dy={WorkTabDragState.RowDragDY:F1}, toRow={WorkTabDragState.ToRow}");
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp)
                {
                    Util.WorkTabLogger.Info(Util.WorkTabLogger.Categories.DragRow, $"End row drag: fromRow={WorkTabDragState.FromRow} -> toRow={WorkTabDragState.ToRow}");
                    FinalizeRowReorder(table);
                    WorkTabDragState.Reset();
                    evt.Use();
                }
                else if (evt.type == EventType.Repaint)
                {
                    DrawRowDragVisuals(table, origin, outRect);
                }
            }
        }

        private static void TryBeginRowDrag(PawnTable table, Vector2 origin, Vector2 mouse)
        {
            float contentY = 0f;
            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                float rowHeight = table.cachedRowHeights[i];
                float screenY = (int)(origin.y + table.HeaderHeight + contentY - table.scrollPosition.y);
                var rowRect = new Rect((int)origin.x, screenY, table.Size.x - 16f, rowHeight);
                if (rowRect.Contains(mouse))
                {
                    WorkTabDragState.Kind = WorkTabDragState.DragKind.Row;
                    WorkTabDragState.ActiveTable = table;
                    WorkTabDragState.MouseStart = mouse;
                    WorkTabDragState.MouseDown = true;
                    WorkTabDragState.FromRow = i;
                    WorkTabDragState.ToRow = i;
                    WorkTabDragState.RowOriginRect = rowRect;
                    WorkTabDragState.RowDragDY = 0f;
                    WorkTabDragState.DragPawn = table.cachedPawns[i];
                    return;
                }
                contentY += rowHeight;
            }

            // Mouse was in the rows area, but did not hit any row slot (possible tiny gap or bad Y due to scroll rounding)
            var outRect = new Rect((int)origin.x, (int)(origin.y + table.HeaderHeight), (int)table.Size.x, (int)(table.Size.y - table.HeaderHeight));
            if (outRect.Contains(mouse))
            {
                Better_Work_Tab.Util.WorkTabLogger.Debug(Better_Work_Tab.Util.WorkTabLogger.Categories.DragRow,
                    $"MouseDown in rows area but no row matched. pos={mouse}");
            }
        }

        private static void UpdateRowInsertionIndex(PawnTable table, Vector2 origin, Vector2 mouse)
        {
            // Iterate visible rows and choose the slot before the first whose center Y is below the mouse.
            float contentY = 0f;
            int to = table.cachedPawns.Count;
            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                float rowHeight = table.cachedRowHeights[i];
                float screenY = (int)(origin.y + table.HeaderHeight + contentY - table.scrollPosition.y);
                var rowRect = new Rect((int)origin.x, screenY, table.Size.x - 16f, rowHeight);
                float centerY = rowRect.center.y;
                if (mouse.y < centerY)
                {
                    to = i;
                    break;
                }
                contentY += rowHeight;
            }
            WorkTabDragState.ToRow = to;
        }

        private static void FinalizeRowReorder(PawnTable table)
        {
            int from = WorkTabDragState.FromRow;
            int to = WorkTabDragState.ToRow;
            if (from == -1 || to == -1)
            {
                Util.WorkTabLogger.Debug(Util.WorkTabLogger.Categories.DragRow, $"Finalize skipped: invalid indices from={from}, to={to}");
                return;
            }
            if (from == to)
            {
                Util.WorkTabLogger.Debug(Util.WorkTabLogger.Categories.DragRow, $"Finalize no-op: from==to ({from})");
                return;
            }

            // Build a new order for the visible pawn list and rewrite displayOrder gaplessly (0..N-1).
            var list = new List<Pawn>(table.cachedPawns);
            var moving = list[from];
            list.RemoveAt(from);
            if (to > list.Count) to = list.Count;
            if (to < 0) to = 0;
            list.Insert(to, moving);

            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p?.playerSettings != null)
                    p.playerSettings.displayOrder = i;
            }

            Util.WorkTabLogger.Info(Util.WorkTabLogger.Categories.DragRow, $"Row reorder applied: moved '{moving?.LabelShort ?? "?"}' to index {to}, rewrote displayOrder for {list.Count} pawns.");
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        private static void DrawRowDragVisuals(PawnTable table, Vector2 origin, Rect outRect)
        {
            var dragRect = WorkTabDragState.RowOriginRect;
            dragRect.y += WorkTabDragState.RowDragDY;

            // Mask original row to indicate it is being dragged.
            Widgets.DrawBoxSolid(WorkTabDragState.RowOriginRect, new Color(0f, 0f, 0f, 0.20f));

            // Draw ghost row as a highlight bar with pawn label.
            Widgets.DrawBoxSolidWithOutline(dragRect, new Color(0.2f, 0.2f, 0.2f, 0.6f), new Color(1f, 1f, 1f, 0.5f));
            if (WorkTabDragState.DragPawn != null)
            {
                var labelRect = new Rect(dragRect.x + 6f, dragRect.y, dragRect.width - 12f, dragRect.height);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, WorkTabDragState.DragPawn.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            // Draw insertion line at target and a gap placeholder.
            int to = Mathf.Clamp(WorkTabDragState.ToRow, 0, table.cachedPawns.Count);
            float lineY = origin.y + table.HeaderHeight;
            for (int i = 0; i < to; i++)
                lineY += table.cachedRowHeights[i];
            lineY -= table.scrollPosition.y;
            var lineRect = new Rect(origin.x, lineY - 1f, outRect.width - 16f, 2f);
            Widgets.DrawBoxSolid(lineRect, new Color(1f, 1f, 1f, 0.6f));

            var gapRect = new Rect(origin.x, lineY, outRect.width - 16f, WorkTabDragState.RowOriginRect.height);
            Widgets.DrawBoxSolid(gapRect, new Color(0f, 0f, 0f, 0.10f));
        }
    }
}
