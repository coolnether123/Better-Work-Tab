using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    // Adds drag-and-drop reordering for rows (pawns) and columns (work types) in the Work tab.
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    public static class WorkTabReorder_PawnTableOnGUI
    {
        public static void Postfix(PawnTable __instance, Vector2 position)
        {
            // Disabled: replaced by custom drag-and-drop implementation that does not use ReorderableWidget.
            return;
        }

        // Allow dragging of worktype column headers to reorder only worktype columns
        private static void TryDrawColumnReorder(PawnTable table, Vector2 position, List<PawnColumnDef> visible)
        {
            Log.Message("TryDrawColumnReorder called.");
            float totalWidthSansScrollbar = table.Size.x - 16f; // cachedSize.x
            int xAccum = 0;

            // Group rect = header strip area (screen space inside the window)
            var headerRect = new Rect((int)position.x, (int)position.y, (int)table.Size.x, (int)table.HeaderHeight);
            Log.Message($"TryDrawColumnReorder: headerRect = {headerRect}");

            // Build map of reorderable indices -> visible column indices (work columns only)
            var reorderableToVisibleIndex = new List<int>(visible.Count);
            for (int i = 0; i < visible.Count; i++)
            {
                int width = (i != visible.Count - 1) ? (int)table.cachedColumnWidths[i] : (int)(totalWidthSansScrollbar - xAccum);
                if (visible[i].Worker is PawnColumnWorker_WorkPriority)
                {
                    reorderableToVisibleIndex.Add(i);
                }
                xAccum += width;
            }
            if (reorderableToVisibleIndex.Count == 0)
            {
                Log.Message("TryDrawColumnReorder: No reorderable work priority columns found.");
                return;
            }

            int groupId = ReorderableWidget.NewGroup((from, to) => OnReorderWorktypeColumn(table, visible, reorderableToVisibleIndex, from, to), ReorderableDirection.Horizontal, headerRect);
            Log.Message($"TryDrawColumnReorder: ReorderableWidget.NewGroup for columns created with groupId {groupId}.");

            // Register only work columns as reorderable slots using absolute header rects
            xAccum = 0;
            for (int i = 0, workSlot = 0; i < visible.Count; i++)
            {
                int width = (i != visible.Count - 1) ? (int)table.cachedColumnWidths[i] : (int)(totalWidthSansScrollbar - xAccum);
                var cellRect = new Rect((int)position.x + xAccum, (int)position.y, width, (int)table.HeaderHeight);
                if (visible[i].Worker is PawnColumnWorker_WorkPriority)
                {
                    ReorderableWidget.Reorderable(groupId, cellRect);
                    Log.Message($"TryDrawColumnReorder: Registered column {visible[i].defName} as reorderable.");
                    workSlot++;
                }
                xAccum += width;
            }
        }

        private static void OnReorderWorktypeColumn(PawnTable table, List<PawnColumnDef> visible, List<int> reorderableToVisibleIndex, int from, int to)
        {
            Log.Message($"OnReorderWorktypeColumn called: from {from} to {to}.");
            if (from == to || from < 0 || to < 0 || from >= reorderableToVisibleIndex.Count || to >= reorderableToVisibleIndex.Count)
            {
                Log.Message("OnReorderWorktypeColumn: Invalid 'from' or 'to' indices.");
                return;
            }

            int fromVisibleIndex = reorderableToVisibleIndex[from];
            int toVisibleIndex = reorderableToVisibleIndex[to];

            var def = table.def; // publicized by project Publicizer
            if (def == null || def.columns == null)
            {
                Log.Message("OnReorderWorktypeColumn: PawnTableDef or columns are null.");
                return;
            }

            int fromDefIndex = def.columns.IndexOf(visible[fromVisibleIndex]);
            int toDefIndex = def.columns.IndexOf(visible[toVisibleIndex]);
            if (fromDefIndex < 0 || toDefIndex < 0)
            {
                Log.Message("OnReorderWorktypeColumn: 'fromDefIndex' or 'toDefIndex' is invalid.");
                return;
            }

            var col = def.columns[fromDefIndex];
            def.columns.RemoveAt(fromDefIndex);
            if (fromDefIndex < toDefIndex) toDefIndex--;
            def.columns.Insert(toDefIndex, col);
            Log.Message($"OnReorderWorktypeColumn: Moved column {col.defName} from index {fromDefIndex} to {toDefIndex}.");

            // Persist order (work columns only)
            Features.WorkColumnOrderManager.CaptureCurrent(def);
            Log.Message("OnReorderWorktypeColumn: WorkColumnOrderManager.CaptureCurrent called.");

            table.SetDirty();
            Log.Message("OnReorderWorktypeColumn: PawnTable set dirty.");
        }

        // Allow dragging rows to reorder pawns by PlayerSettings.displayOrder (like ColonistBar)
        private static void TryDrawRowReorder(PawnTable table, Vector2 position)
        {
            Log.Message("TryDrawRowReorder called.");

            // Screen-space rect of the scroll view area
            var outRect = new Rect((int)position.x, (int)(position.y + table.HeaderHeight), (int)table.Size.x, (int)(table.Size.y - table.HeaderHeight));
            Log.Message($"TryDrawRowReorder: outRect = {outRect}");
            int groupId = ReorderableWidget.NewGroup((from, to) => OnReorderPawnRow(table, from, to), ReorderableDirection.Vertical, outRect);
            Log.Message($"TryDrawRowReorder: ReorderableWidget.NewGroup for rows created with groupId {groupId}.");

            // Build absolute screen rectangles for each row, accounting for scroll offset
            float contentY = 0f;
            for (int i = 0; i < table.cachedPawns.Count; i++)
            {
                float rowHeight = table.cachedRowHeights[i];
                float screenY = (int)(position.y + table.HeaderHeight + contentY - table.scrollPosition.y);
                var rowRect = new Rect((int)position.x, screenY, outRect.width - 16f, rowHeight);
                ReorderableWidget.Reorderable(groupId, rowRect, highlightDragged: false);
                Log.Message($"TryDrawRowReorder: Registered row for pawn {table.cachedPawns[i].Name} as reorderable.");
                contentY += rowHeight;
            }
        }

        private static void OnReorderPawnRow(PawnTable table, int from, int to)
        {
            Log.Message($"OnReorderPawnRow called: from {from} to {to}.");
            var pawns = table.cachedPawns;
            if (from == to || from < 0 || to < 0 || from >= pawns.Count || to > pawns.Count)
            {
                Log.Message("OnReorderPawnRow: Invalid 'from' or 'to' indices.");
                return;
            }

            var pawnFrom = pawns[from];
            var pawnTo = to < pawns.Count ? pawns[to] : null;
            var lastPawn = pawns[pawns.Count - 1];
            if (pawnFrom?.playerSettings == null)
            {
                Log.Message("OnReorderPawnRow: 'pawnFrom' or its playerSettings are null.");
                return;
            }

            int baseOrder = lastPawn?.playerSettings?.displayOrder ?? 0;
            int targetOrder = pawnTo?.playerSettings?.displayOrder ?? (baseOrder + 1);
            Log.Message($"OnReorderPawnRow: Moving pawn {pawnFrom.Name} to target order {targetOrder}.");

            // Adjust other pawns' displayOrder in the shown list to maintain uniqueness and relative order
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p?.playerSettings == null || p == pawnFrom) continue;

                if (p.playerSettings.displayOrder >= targetOrder)
                    p.playerSettings.displayOrder++;
            }
            pawnFrom.playerSettings.displayOrder = targetOrder;

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            Log.Message("OnReorderPawnRow: MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged called.");
        }
    }

    // Apply saved column order each time the Work tab opens
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class WorkTabReorder_PostOpen
    {
        private static bool hasAppliedOrder = false;

        public static void Postfix()
        {

            if (!hasAppliedOrder)
            {
                var def = PawnTableDefOf.Work;
                Features.WorkColumnOrderManager.ApplySaved(def);
                hasAppliedOrder = true;
                Log.Message("WorkTabReorder_PostOpen.Postfix: WorkColumnOrderManager.ApplySaved called.");
            }
            else
            {
                // Else order already applied
            }
        }

        public static void ResetAppliedOrderFlag()
        {
            hasAppliedOrder = false;
            Log.Message("WorkTabReorder_PostOpen.ResetAppliedOrderFlag called.");
        }
    }
}
