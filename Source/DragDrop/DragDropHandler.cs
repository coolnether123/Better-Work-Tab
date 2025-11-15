using Better_Work_Tab.Mod_Support.Multiplayer;
using Multiplayer.API;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.DragDrop
{
    internal static class DragDropHandler
    {
        private static void LogRowDebug(string message)
        {
            // Verse.Log.Message($"[Better Work Tab/DragRow] {message}");
        }

        private static void ResetSession(DragSession session, string reason)
        {
            if (session.Kind == DragSession.DragKind.Row)
            {
                var pawn = session.DraggedItem as Pawn;
                LogRowDebug($"Resetting row drag session ({reason}). Pawn='{pawn?.LabelShort ?? "null"}', FromIndex={session.FromIndex}, ToIndex={session.ToIndex}");
            }

            session.Reset();
        }

        private static readonly MethodInfo RecacheIfDirtyMethod = typeof(PawnTable).GetMethod("RecacheIfDirty", BindingFlags.Instance | BindingFlags.NonPublic);

        private static void EnsureGeometryFresh(PawnTable table)
        {
            RecacheIfDirtyMethod?.Invoke(table, null);
        }

        /// <summary>
        /// Handles mouse input events (down, drag, up).
        /// </summary>
        internal static void OnGUI(PawnTable table, Vector2 origin, DragSession session)
        {
            EnsureGeometryFresh(table);
            var evt = Event.current;
            if (evt.type == EventType.Repaint) return; // Drawing is handled in Postfix for correct layering.

            // Ensure this is the work tab
            if (!table.Columns.Any(c => c.Worker is PawnColumnWorker_WorkPriority))
            {
                if (session.IsDragging())
                {
                    ResetSession(session, "table lost work columns");
                } // If user switches tab while dragging, cancel it.
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.control)
            {
                TryBeginDrag(table, origin, session, evt.mousePosition);
                if (session.IsDragging())
                {
                    evt.Use();
                }
            }
            else if (session.IsDragging()) // Only process these if a drag is active for this table
            {
                if (evt.type == EventType.MouseDrag)
                {
                    session.DragOffset = evt.mousePosition - session.MouseStart;
                    UpdateInsertionIndex(session, evt.mousePosition);
                    if (session.Kind == DragSession.DragKind.Row)
                    {
                        LogRowDebug($"MouseDrag -> DragOffset={session.DragOffset}, Mouse={evt.mousePosition}, ToIndex={session.ToIndex}");
                    }
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp)
                {
                    FinalizeReorder(table, session);
                    ResetSession(session, "mouse up"); // Critical: reset state after operation
                    evt.Use();
                }
                else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                {
                    if (session.Kind == DragSession.DragKind.Row)
                    {
                        LogRowDebug("Escape pressed during row drag.");
                    }
                    ResetSession(session, "escape key"); // Allow canceling with Escape key
                    evt.Use();
                }
            }
        }

        private static void TryBeginDrag(PawnTable table, Vector2 origin, DragSession session, Vector2 mouse)
        {
            // Verse.Log.Message($"[Better Work Tab/DragColumn] Current Column Order: {string.Join(", ", table.Columns.Select(c => c.defName))}");
            // Verse.Log.Message($"[Better Work Tab/DragColumn] Cached Column Widths: {string.Join(", ", table.cachedColumnWidths.Select(w => w.ToString()))}");

            // --- Attempt Column Drag ---
            var headerRect = new Rect(origin.x, origin.y, table.Size.x, table.HeaderHeight);
            if (headerRect.Contains(mouse))
            {
                float currentX = origin.x;
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var colDef = table.Columns[i];
                    // Correctly calculate the width of the current column. The last column fills the remaining space.
                    float availableWidth = table.Size.x - 16f; // Account for scrollbar
                    float accumulatedWidth = currentX - origin.x;
                    float colWidth = (i == table.Columns.Count - 1)
                        ? (availableWidth - accumulatedWidth)
                        : table.cachedColumnWidths[i];

                    var cellRect = new Rect(currentX, origin.y, colWidth, table.HeaderHeight);

                    // Check if this column is a draggable work column and if the mouse is over it.
                    if (colDef.Worker is PawnColumnWorker_WorkPriority && cellRect.Contains(mouse))
                    {
                        // --- DRAG START ---
                        session.Kind = DragSession.DragKind.Column;
                        session.MouseStart = mouse;

                        // Find the index within the subset of ONLY work columns
                        var workColumnsInTable = table.Columns.Where(c => c.Worker is PawnColumnWorker_WorkPriority).ToList();
                        session.FromIndex = workColumnsInTable.IndexOf(colDef);
                        session.ToIndex = session.FromIndex;

                        session.DraggedItem = colDef;
                        session.OriginRect = cellRect;

                        // --- CACHE GEOMETRY ---
                        CacheColumnGeometry(table, origin, session);
                        // Verse.Log.Message($"[Better Work Tab/DragColumn] Begin column drag: '{colDef.defName}'");
                        return; // Found our column, exit.
                    }

                    currentX += colWidth; // Move to the start of the next column.
                }
            }

            // --- Attempt Row Drag if Column Drag Failed ---
            float firstRowY = origin.y + table.cachedHeaderHeight - table.scrollPosition.y;
            LogRowDebug($"Row drag window space startY={firstRowY}, mouse={mouse}, scroll={table.scrollPosition}, headerHeight={table.cachedHeaderHeight}");

            float currentY = firstRowY;
            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                float rowHeight = table.cachedRowHeights[i];
                var rowRect = new Rect(origin.x, currentY, table.cachedSize.x - 16f, rowHeight);

                bool contains = rowRect.Contains(mouse);
                LogRowDebug($"Inspecting row {i}: rowRect={rowRect}, containsMouse={contains}");

                if (contains)
                {
                    session.Kind = DragSession.DragKind.Row;
                    session.MouseStart = mouse;
                    session.FromIndex = i;
                    session.ToIndex = i;
                    session.DraggedItem = table.cachedPawns[i];
                    session.OriginRect = rowRect;
                    CacheRowGeometry(table, origin, session);
                    LogRowDebug($"Begin row drag: Pawn='{table.cachedPawns[i].LabelShort}'");
                    return;
                }
                currentY += rowHeight;
            }

            LogRowDebug($"No match. firstRowY={firstRowY}, mouse={mouse}");
        }


        /// <summary>
        /// PRE-CALCULATES all column drop target positions and stores them in the session.
        /// This is called ONCE at the start of a drag.
        /// </summary>
        private static void CacheColumnGeometry(PawnTable table, Vector2 origin, DragSession session)
        {
            session.CachedTargetRects = new List<Rect>();
            session.CachedTargetBoundaries = new List<float>();

            float currentX = origin.x;
            var workColumns = table.Columns.Where(c => c.Worker is PawnColumnWorker_WorkPriority).ToList();

            // We need to map work column rects accurately.
            float xAccum = origin.x;
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                float colWidth = i == table.Columns.Count - 1 ? (table.Size.x - 16f - (xAccum - origin.x)) : table.cachedColumnWidths[i];
                if (col.Worker is PawnColumnWorker_WorkPriority)
                {
                    var rect = new Rect(xAccum, origin.y, colWidth, table.HeaderHeight);
                    session.CachedTargetRects.Add(rect);
                    // The boundary is the center of the column.
                    session.CachedTargetBoundaries.Add(rect.x + rect.width / 2f);
                }
                xAccum += colWidth;
            }
        }

        /// <summary>
        /// PRE-CALCULATES all row drop target positions.
        /// </summary>
        private static void CacheRowGeometry(PawnTable table, Vector2 origin, DragSession session)
        {
            session.CachedTargetRects = new List<Rect>();
            session.CachedTargetBoundaries = new List<float>();
            float currentY = origin.y + table.cachedHeaderHeight - table.scrollPosition.y;

            LogRowDebug($"CacheRowGeometry start: pawnCount={table.cachedPawns?.Count ?? -1}, scroll={table.scrollPosition}, origin={origin}");

            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                float rowHeight = table.cachedRowHeights[i];
                var rect = new Rect(origin.x, currentY, table.Size.x - 16f, rowHeight);
                session.CachedTargetRects.Add(rect);
                session.CachedTargetBoundaries.Add(rect.y + rect.height / 2f);
                LogRowDebug($"Cached row {i}: rect={rect}, boundary={rect.y + rect.height / 2f}, pawn='{table.cachedPawns[i]?.LabelShort ?? "null"}'");
                currentY += rowHeight;
            }

            LogRowDebug($"CacheRowGeometry complete. CachedRects={session.CachedTargetRects.Count}, CachedBoundaries={session.CachedTargetBoundaries.Count}");
        }

        /// <summary>
        /// Uses the CACHED boundaries to find the insertion index. This is extremely fast.
        /// </summary>
        private static void UpdateInsertionIndex(DragSession session, Vector2 mouse)
        {
            if (session.CachedTargetBoundaries == null) return;

            float mousePos = (session.Kind == DragSession.DragKind.Column) ? mouse.x : mouse.y;

            int newToIndex = session.CachedTargetBoundaries.Count; // Default to the end
            for (int i = 0; i < session.CachedTargetBoundaries.Count; i++)
            {
                if (mousePos < session.CachedTargetBoundaries[i])
                {
                    newToIndex = i;
                    break;
                }
            }
            if (session.Kind == DragSession.DragKind.Row && newToIndex != session.ToIndex)
            {
                LogRowDebug($"UpdateInsertionIndex -> mousePos={mousePos}, newToIndex={newToIndex}, boundarySample={(session.CachedTargetBoundaries.Count > 0 ? session.CachedTargetBoundaries[Mathf.Clamp(newToIndex, 0, session.CachedTargetBoundaries.Count - 1)].ToString() : "n/a")}");
            }
            session.ToIndex = newToIndex;
        }

        private static void FinalizeReorder(PawnTable table, DragSession session)
        {
            if (session.FromIndex == -1 || session.ToIndex == -1 || session.FromIndex == session.ToIndex) return;

            if (session.Kind == DragSession.DragKind.Column)
            {
                var colToMove = session.DraggedItem as PawnColumnDef;
                if (colToMove == null) return;

                // Map the drag indices to a stable "work column index"
                var workColumns = table.def.columns
                    .Where(c => c.Worker is PawnColumnWorker_WorkPriority)
                    .ToList();

                // Clamp session.ToIndex to a valid range
                int targetWorkIndex = session.ToIndex;
                if (targetWorkIndex < 0) targetWorkIndex = 0;
                if (targetWorkIndex > workColumns.Count) targetWorkIndex = workColumns.Count;

                if (MP.enabled && MP.IsInMultiplayer)
                {
                    // This call is intercepted and replicated by MP
                    WorkColumnOrderSync.ApplyWorkColumnMove(colToMove.defName, targetWorkIndex);
                }
                else
                {
                    // Single-player or MP disabled: apply locally using the same code
                    WorkColumnOrderSync.ApplyWorkColumnMove(colToMove.defName, targetWorkIndex);
                }
            }
            else if (session.Kind == DragSession.DragKind.Row)
            {
                LogRowDebug($"FinalizeReorder start: FromIndex={session.FromIndex}, ToIndex={session.ToIndex}, cachedPawnCount={table.cachedPawns.Count}");

                var list = new List<Pawn>(table.cachedPawns);
                var moving = list[session.FromIndex];
                list.RemoveAt(session.FromIndex);
                int to = Mathf.Clamp(session.ToIndex, 0, list.Count);
                list.Insert(to, moving);

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i]?.playerSettings != null)
                        list[i].playerSettings.displayOrder = i;
                }
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

                var fi = typeof(PawnTable).GetField("sortingBy", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fi != null)
                    fi.SetValue(table, null);

                LogRowDebug($"FinalizeReorder complete. Moved pawn='{moving?.LabelShort ?? "null"}' to index={to}. DisplayOrder rewritten.");
            }
        }

        /// <summary>
        /// Handles drawing the ghost, gap, and insertion line using the session state.
        /// This runs during the Postfix Repaint event for correct visual layering.
        /// </summary>
        internal static void DrawDragVisuals(PawnTable table, Vector2 origin, DragSession session)
        {
            // --- Step 1: Draw the "Gap" ---
            // This creates a semi-transparent mask over the original position of the item being dragged.
            Widgets.DrawBoxSolid(session.OriginRect, new Color(0f, 0f, 0f, 0.25f));

            // --- Step 2: Draw the "Ghost" ---
            // This is the visual element that follows the user's cursor.
            var ghostRect = session.OriginRect;
            ghostRect.position += session.DragOffset; // Apply the mouse movement to the original position

            // Draw the ghost's box and outline
            Widgets.DrawBoxSolidWithOutline(ghostRect, new Color(0.15f, 0.15f, 0.15f, 0.75f), new Color(1f, 1f, 1f, 0.6f));

            // Draw the appropriate label inside the ghost box
            if (session.Kind == DragSession.DragKind.Column && session.DraggedItem is PawnColumnDef colDef)
            {
                string label = !colDef.headerTip.NullOrEmpty() ? colDef.headerTip : colDef.defName;
                var labelRect = ghostRect.ContractedBy(2f);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(labelRect, label);
                Text.Anchor = TextAnchor.UpperLeft; // Always reset text anchor
                Text.Font = GameFont.Small;      // Always reset font
            }
            else if (session.Kind == DragSession.DragKind.Row && session.DraggedItem is Pawn pawn)
            {
                var labelRect = new Rect(ghostRect.x + 6f, ghostRect.y, ghostRect.width - 12f, ghostRect.height);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, pawn.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft; // Always reset text anchor
            }

            // --- Step 3: Draw the Insertion Line and Placeholder Gap ---
            // We use the pre-calculated geometry from the session for maximum performance.
            if (session.CachedTargetRects == null || session.ToIndex < 0)
            {
                if (session.Kind == DragSession.DragKind.Row)
                {
                    LogRowDebug($"Skipping DrawDragVisuals because cached data missing (RectsNull={session.CachedTargetRects == null}, ToIndex={session.ToIndex}).");
                }
                return;
            }

            int to = Mathf.Clamp(session.ToIndex, 0, session.CachedTargetRects.Count);
            Rect lineRect;
            Rect placeholderGapRect;

            if (session.Kind == DragSession.DragKind.Column)
            {
                // For columns, the line is vertical.
                float lineX;
                if (to >= session.CachedTargetRects.Count)
                {
                    // If dragging past the last item, place the line at the end.
                    lineX = session.CachedTargetRects.Last().xMax;
                }
                else
                {
                    // Otherwise, place it at the beginning of the target item's rect.
                    lineX = session.CachedTargetRects[to].xMin;
                }

                lineRect = new Rect(lineX - 1f, origin.y, 2f, table.HeaderHeight);
                placeholderGapRect = new Rect(lineX, origin.y, session.OriginRect.width, table.HeaderHeight);
            }
            else // Row
            {
                // For rows, the line is horizontal.
                float lineY;
                // The cached row rects are already adjusted for scroll position when they are created,
                // so we just need their screen-space Y coordinate.
                if (to >= session.CachedTargetRects.Count)
                {
                    lineY = session.CachedTargetRects.Last().yMax;
                }
                else
                {
                    lineY = session.CachedTargetRects[to].yMin;
                }

                lineRect = new Rect(origin.x, lineY - 1f, table.Size.x - 16f, 2f);
                placeholderGapRect = new Rect(origin.x, lineY, table.Size.x - 16f, session.OriginRect.height);
            }

            // Draw the final visual elements.
            Widgets.DrawBoxSolid(placeholderGapRect, new Color(0f, 0f, 0f, 0.15f)); // Draw the dark gap first
            Widgets.DrawBoxSolid(lineRect, new Color(1f, 1f, 1f, 0.75f));            // Draw the bright line on top
        }
    }
}
