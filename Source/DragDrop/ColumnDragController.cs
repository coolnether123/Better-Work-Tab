using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    /// <summary>
    /// Handles custom drag-and-drop for worktype columns in the Work tab header.
    /// Only allow dragging columns whose Worker is PawnColumnWorker_WorkPriority.
    /// </summary>
    internal static class ColumnDragController
    {
        internal static void OnGUI(PawnTable table, Vector2 origin)
        {
            // Only act for Work tab with visible work-priority columns.
            var visible = table.Columns;
            if (visible == null || visible.Count == 0 || !visible.Any(c => c.Worker is PawnColumnWorker_WorkPriority))
                return;

            var evt = Event.current;
            var headerRect = new Rect((int)origin.x, (int)origin.y, (int)table.Size.x, (int)table.HeaderHeight);

            // Short-circuit if a row drag is in progress.
            if (WorkTabDragState.Kind == WorkTabDragState.DragKind.Row)
                return;

            // Only start a column drag when Ctrl is held to preserve normal header behavior
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control && headerRect.Contains(evt.mousePosition))
            {
                TryBeginColumnDrag(table, origin, visible, evt.mousePosition);
                if (WorkTabDragState.Kind == WorkTabDragState.DragKind.Column)
                {
                    Util.WorkTabLogger.Info(Util.WorkTabLogger.Categories.DragColumn, $"Begin column drag: fromWorkSlot={WorkTabDragState.FromWorkSlot}, visIdx={WorkTabDragState.FromVisibleIndex}, def={(WorkTabDragState.DragColumnDef?.defName ?? "?")}, rect={WorkTabDragState.ColumnOriginRect}");
                    evt.Use();
                    return;
                }
            }

            if (WorkTabDragState.Kind == WorkTabDragState.DragKind.Column && WorkTabDragState.IsActiveFor(table))
            {
                if (evt.type == EventType.MouseDrag)
                {
                    WorkTabDragState.ColumnDragDX = evt.mousePosition.x - WorkTabDragState.MouseStart.x;
                    UpdateColumnInsertionIndex(table, origin, visible, evt.mousePosition);
                    Util.WorkTabLogger.Debug(Util.WorkTabLogger.Categories.DragColumn, $"Drag column move: dx={WorkTabDragState.ColumnDragDX:F1}, toWorkSlot={WorkTabDragState.ToWorkSlot}");
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp)
                {
                    Util.WorkTabLogger.Info(Util.WorkTabLogger.Categories.DragColumn, $"End column drag: fromWorkSlot={WorkTabDragState.FromWorkSlot} -> toWorkSlot={WorkTabDragState.ToWorkSlot}");
                    FinalizeColumnReorder(table, visible);
                    WorkTabDragState.Reset();
                    evt.Use();
                }
                else if (evt.type == EventType.Repaint)
                {
                    DrawColumnDragVisuals(table, origin, visible);
                }
            }
            else if (evt.type == EventType.Repaint)
            {
                // Not dragging columns.
            }
        }

        private static void TryBeginColumnDrag(PawnTable table, Vector2 origin, List<PawnColumnDef> visible, Vector2 mouse)
        {
            // Build per-column header rects and map work-only slots.
            float totalWidthSansScrollbar = table.Size.x - 16f;
            int xAccum = 0;
            var workSlotToVisible = new List<int>();
            var headerRects = new List<Rect>(visible.Count);
            for (int i = 0; i < visible.Count; i++)
            {
                int width = (i != visible.Count - 1) ? (int)table.cachedColumnWidths[i] : (int)(totalWidthSansScrollbar - xAccum);
                var cellRect = new Rect((int)origin.x + xAccum, (int)origin.y, width, (int)table.HeaderHeight);
                headerRects.Add(cellRect);
                if (visible[i].Worker is PawnColumnWorker_WorkPriority)
                    workSlotToVisible.Add(i);
                xAccum += width;
            }

            // Hit test: only start drag if clicked a work-priority column.
            for (int s = 0; s < workSlotToVisible.Count; s++)
            {
                int visIdx = workSlotToVisible[s];
                if (headerRects[visIdx].Contains(mouse))
                {
                    WorkTabDragState.Kind = WorkTabDragState.DragKind.Column;
                    WorkTabDragState.ActiveTable = table;
                    WorkTabDragState.MouseStart = mouse;
                    WorkTabDragState.MouseDown = true;
                    WorkTabDragState.FromWorkSlot = s;
                    WorkTabDragState.ToWorkSlot = s;
                    WorkTabDragState.FromVisibleIndex = visIdx;
                    WorkTabDragState.ColumnOriginRect = headerRects[visIdx];
                    WorkTabDragState.ColumnDragDX = 0f;
                    WorkTabDragState.DragColumnDef = visible[visIdx];
                    WorkTabDragState.WorkSlotToVisible = workSlotToVisible;
                    return;
                }
            }

            // Mouse was in header but not on a draggable work column.
            if (new Rect((int)origin.x, (int)origin.y, (int)table.Size.x, (int)table.HeaderHeight).Contains(mouse))
            {
                Better_Work_Tab.Util.WorkTabLogger.Debug(Better_Work_Tab.Util.WorkTabLogger.Categories.DragColumn,
                    $"MouseDown on header but not on work column. pos={mouse}");
            }
        }

        private static void UpdateColumnInsertionIndex(PawnTable table, Vector2 origin, List<PawnColumnDef> visible, Vector2 mouse)
        {
            // Compute midpoints of each work column header; pick nearest slot boundary.
            float totalWidthSansScrollbar = table.Size.x - 16f;
            int xAccum = 0;
            var workRects = new List<Rect>(WorkTabDragState.WorkSlotToVisible.Count);
            for (int i = 0; i < visible.Count; i++)
            {
                int width = (i != visible.Count - 1) ? (int)table.cachedColumnWidths[i] : (int)(totalWidthSansScrollbar - xAccum);
                var cellRect = new Rect((int)origin.x + xAccum, (int)origin.y, width, (int)table.HeaderHeight);
                if (visible[i].Worker is PawnColumnWorker_WorkPriority)
                    workRects.Add(cellRect);
                xAccum += width;
            }

            // Decide insertion index: before the first whose center is right of the mouse X.
            int to = workRects.Count;
            for (int s = 0; s < workRects.Count; s++)
            {
                float centerX = workRects[s].center.x;
                if (mouse.x < centerX)
                {
                    to = s;
                    break;
                }
            }
            WorkTabDragState.ToWorkSlot = to;
        }

        private static void FinalizeColumnReorder(PawnTable table, List<PawnColumnDef> visible)
        {
            int from = WorkTabDragState.FromWorkSlot;
            int to = WorkTabDragState.ToWorkSlot;
            if (from == -1 || to == -1)
            {
                Util.WorkTabLogger.Debug(Util.WorkTabLogger.Categories.DragColumn, $"Finalize skipped: invalid indices from={from}, to={to}");
                return;
            }
            if (from == to)
            {
                Util.WorkTabLogger.Debug(Util.WorkTabLogger.Categories.DragColumn, $"Finalize no-op: from==to ({from})");
                return;
            }

            // Map work slots to visible indices then to def.columns indices.
            int fromVisibleIndex = WorkTabDragState.WorkSlotToVisible[from];
            var def = table.def;
            if (def == null || def.columns == null)
            {
                Util.WorkTabLogger.Warn(Util.WorkTabLogger.Categories.DragColumn, "PawnTable.def or columns was null during finalize.");
                return;
            }

            int fromDefIndex = def.columns.IndexOf(visible[fromVisibleIndex]);
            int toDefIndex;
            if (to >= WorkTabDragState.WorkSlotToVisible.Count)
            {
                // Insert at end of the work columns band (after the last work column visible index)
                int lastVis = WorkTabDragState.WorkSlotToVisible.Last();
                toDefIndex = def.columns.IndexOf(visible[lastVis]) + 1;
            }
            else
            {
                toDefIndex = def.columns.IndexOf(visible[WorkTabDragState.WorkSlotToVisible[to]]);
            }
            if (fromDefIndex < 0 || toDefIndex < 0)
            {
                Util.WorkTabLogger.Warn(Util.WorkTabLogger.Categories.DragColumn, $"Invalid def indices: fromDef={fromDefIndex}, toDef={toDefIndex}");
                return;
            }

            var col = def.columns[fromDefIndex];
            def.columns.RemoveAt(fromDefIndex);
            if (fromDefIndex < toDefIndex) toDefIndex--;
            def.columns.Insert(toDefIndex, col);

            Util.WorkTabLogger.Info(Util.WorkTabLogger.Categories.DragColumn, $"Column reorder applied: '{col.defName}' defIndex {fromDefIndex} -> {toDefIndex}");

            // Persist order of work columns only.
            Features.WorkColumnOrderManager.CaptureCurrent(def);

            // Ensure AI work scanning order rebuilds to reflect the new column order.
            Features.WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();

            table.SetDirty();
        }

        private static void DrawColumnDragVisuals(PawnTable table, Vector2 origin, List<PawnColumnDef> visible)
        {
            var dragRect = WorkTabDragState.ColumnOriginRect;
            dragRect.x += WorkTabDragState.ColumnDragDX;

            // Mask original header cell to create a "gap" visual.
            Widgets.DrawBoxSolid(WorkTabDragState.ColumnOriginRect, new Color(0f, 0f, 0f, 0.25f));

            // Draw the dragged ghost header.
            using (new TextBlock(new Color(1f, 1f, 1f, 0.85f)))
            {
                Widgets.DrawBoxSolidWithOutline(dragRect, new Color(0.15f, 0.15f, 0.15f, 0.75f), new Color(1f, 1f, 1f, 0.6f));
                string label = null;
                if (WorkTabDragState.DragColumnDef != null)
                    label = !WorkTabDragState.DragColumnDef.headerTip.NullOrEmpty() ? WorkTabDragState.DragColumnDef.headerTip : WorkTabDragState.DragColumnDef.defName;
                if (!label.NullOrEmpty())
                {
                    var labelRect = dragRect.ContractedBy(2f);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(labelRect, label);
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                }
            }

            // Draw insertion line between work columns.
            float totalWidthSansScrollbar = table.Size.x - 16f;
            int xAccum = 0;
            var workRects = new List<Rect>();
            for (int i = 0; i < visible.Count; i++)
            {
                int width = (i != visible.Count - 1) ? (int)table.cachedColumnWidths[i] : (int)(totalWidthSansScrollbar - xAccum);
                var cellRect = new Rect((int)origin.x + xAccum, (int)origin.y, width, (int)table.HeaderHeight);
                if (visible[i].Worker is PawnColumnWorker_WorkPriority)
                    workRects.Add(cellRect);
                xAccum += width;
            }
            int to = Mathf.Clamp(WorkTabDragState.ToWorkSlot, 0, workRects.Count);
            float lineX = to <= 0 ? workRects[0].xMin : (to >= workRects.Count ? workRects.Last().xMax : workRects[to].xMin);
            var lineRect = new Rect(lineX - 1f, origin.y, 2f, table.HeaderHeight);
            Widgets.DrawBoxSolid(lineRect, new Color(1f, 1f, 1f, 0.6f));

            // Draw a temporary gap placeholder to visualize where the column will land.
            var gapRect = new Rect(lineX, origin.y, WorkTabDragState.ColumnOriginRect.width, table.HeaderHeight);
            Widgets.DrawBoxSolid(gapRect, new Color(0f, 0f, 0f, 0.15f));
        }
    }
}
