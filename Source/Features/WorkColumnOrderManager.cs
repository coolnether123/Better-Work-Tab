using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// Static utility class for managing and persisting the custom ordering of work type columns in the RimWorld Work tab.
    /// Handles capturing the current column order from a PawnTableDef.columns to the mod's settings (workColumnOrderDefNames list),
    /// applying saved order to reorder def.columns (preserving non-work column positions), and assigning default manual priorities (1-4)
    /// based on the current left-to-right order (higher left, lower right). Only operates on PawnColumnDef with Worker of type PawnColumnWorker_WorkPriority.
    /// Non-work columns (e.g., pawn ID) maintain their relative pre/post positions around the work block. Called from drag finalize and mod init/load.
    /// Logs operations via Verse.Log for debugging. Integrates with BetterWorkTabSettings for serialization.
    /// </summary>
    public static class WorkColumnOrderManager
    {
        /// <summary>
        /// Captures the current order of work type columns from the provided PawnTableDef and saves it to the mod's settings.
        /// Iterates def.columns, collects defName of work types (PawnColumnWorker_WorkPriority only), stores in Settings.workColumnOrderDefNames.
        /// Writes settings immediately. Logs the captured order. Early return if def or columns null.
        /// Called after drag-and-drop reordering to persist the new order for future sessions.
        /// </summary>
        /// <param name="def">The PawnTableDef (typically PawnTableDefOf.Work) from which to capture the current columns order.</param>
        public static void CaptureCurrent(PawnTableDef def)
        {
            Log.Message("WorkColumnOrderManager.CaptureCurrent called.");
            if (def?.columns == null)
            {
                Log.Message("WorkColumnOrderManager.CaptureCurrent: def or columns are null.");
                return;
            }
            var order = new List<string>();
            foreach (var c in def.columns)
            {
                if (c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                    order.Add(c.workType.defName);
            }
            BetterWorkTabMod.Settings.workColumnOrderDefNames = order;
            BetterWorkTabMod.Settings.Write();
            Log.Message($"WorkColumnOrderManager.CaptureCurrent: Captured order: {string.Join(", ", order)}");
        }

        /// <summary>
        /// Applies the saved custom work column order from settings to the provided PawnTableDef.columns.
        /// Splits columns into pre-work, work (PawnColumnWorker_WorkPriority), post-work bands based on current positions.
        /// Sorts work columns by saved defNames order, falling back to current order for unknown/missing; rebuilds def.columns preserving bands.
        /// Logs the saved/sorted order and result. Early return if no saved order, no def/columns, or no work columns.
        /// Called on mod load or after manual order changes to load persisted user preferences into the UI.
        /// </summary>
        /// <param name="def">The PawnTableDef to reorder columns for (e.g., PawnTableDefOf.Work).</param>
        public static void ApplySaved(PawnTableDef def)
        {
            //Log.Message("WorkColumnOrderManager.ApplySaved called.");
            if (def?.columns == null)
            {
                Log.Message("WorkColumnOrderManager.ApplySaved: def or columns are null.");
                return;
            }
            var saved = BetterWorkTabMod.Settings.workColumnOrderDefNames;
            if (saved == null || saved.Count == 0)
            {
                Log.Message("WorkColumnOrderManager.ApplySaved: No saved order found.");
                return;
            }
            Log.Message($"WorkColumnOrderManager.ApplySaved: Saved order: {string.Join(", ", saved)}");

            // Split into bands: pre-work, work, post-work (by current visibility/definition)
            var workCols = new List<PawnColumnDef>();
            int minIdx = int.MaxValue, maxIdx = -1;
            for (int i = 0; i < def.columns.Count; i++)
            {
                var col = def.columns[i];
                if (col.Worker is PawnColumnWorker_WorkPriority)
                {
                    workCols.Add(col);
                    if (i < minIdx) minIdx = i;
                    if (i > maxIdx) maxIdx = i;
                }
            }
            if (workCols.Count == 0)
            {
                Log.Message("WorkColumnOrderManager.ApplySaved: No work columns found to reorder.");
                return;
            }

            var pre = def.columns.Take(minIdx).ToList();
            var post = def.columns.Skip(maxIdx + 1).ToList();

            // Sort work columns by saved order, fallback to current order for unknowns
            var byDefName = workCols.ToDictionary(c => c.workType?.defName ?? "", c => c);
            var sorted = new List<PawnColumnDef>();
            foreach (var defName in saved)
            {
                if (byDefName.TryGetValue(defName, out var c))
                {
                    sorted.Add(c);
                    byDefName.Remove(defName);
                }
            }
            // Append any missing/unknown in their current order
            foreach (var c in workCols)
            {
                if (!sorted.Contains(c))
                    sorted.Add(c);
            }
            Log.Message($"WorkColumnOrderManager.ApplySaved: Sorted columns: {string.Join(", ", sorted.Select(c => c.defName))}");

            // Rebuild def.columns respecting pre and post bands
            def.columns.Clear();
            def.columns.AddRange(pre);
            def.columns.AddRange(sorted);
            def.columns.AddRange(post);
            Log.Message("WorkColumnOrderManager.ApplySaved: Columns reordered.");
        }

        /// <summary>
        /// Applies default manual priority values (1 to 4) across all player pawns for work types based on the current column order in the provided PawnTableDef.
        /// Divides work columns into 4 bands using ceil(work column count / 4), assigns priority = band + 1 (capped at 4), leftmost band = 1 (highest).
        /// Initializes workSettings if needed; skips player non-pawns, dead, disabled work types. Logs total changes via WorkTabLogger.
        /// Called after reordering to align priorities with new UI layout (e.g., after drag finalize or load). Early return if no columns or no work columns.
        /// Affects all alive pawns in all maps/temp (via PawnsFinder), ensuring consistent initial assignments.
        /// </summary>
        /// <param name="def">The PawnTableDef defining the current work column order for band calculation.</param>
        public static void ApplyDefaultPrioritiesFromCurrentOrder(PawnTableDef def)
        {
            if (def?.columns == null) return;

            var workCols = new List<PawnColumnDef>();
            foreach (var c in def.columns)
            {
                if (c?.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                    workCols.Add(c);
            }
            if (workCols.Count == 0) return;

            var orderedWT = workCols.Select(c => c.workType).ToList();
            int n = orderedWT.Count;
            int bandSize = (n + 3) / 4; // ceil(n/4)
            if (bandSize <= 0) bandSize = 1;

            int changed = 0;
            foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (p?.Faction != Faction.OfPlayer || p.workSettings == null) continue;

                p.workSettings.EnableAndInitializeIfNotAlreadyInitialized();

                for (int i = 0; i < orderedWT.Count; i++)
                {
                    var wt = orderedWT[i];
                    if (wt == null || p.WorkTypeIsDisabled(wt)) continue;
                    int band = i / bandSize; // 0..3+
                    int prio = band + 1; // 1..4+
                    if (prio > 4) prio = 4;
                    p.workSettings.SetPriority(wt, prio);
                    changed++;
                }
            }

            Verse.Log.Message($"[Better Work Tab/DragColumn] Default manual priorities applied from column order (changed {changed} entries).");
        }

        public static Dictionary<WorkTypeDef, int> WorkTypeOrder = new Dictionary<WorkTypeDef, int>();

        public static void SetWorkTypeOrder(WorkTypeDef workType, int newOrder)
        {
            int changedCount = 0; // Added this line
            if (!WorkTypeOrder.ContainsKey(workType))
            {
                WorkTypeOrder.Add(workType, newOrder);
                return;
            }

            int oldOrder = WorkTypeOrder[workType];
            WorkTypeOrder[workType] = newOrder;

            if (newOrder > oldOrder)
            {
                // Shift all work types between oldOrder and newOrder down by 1
                foreach (var kvp in WorkTypeOrder.ToList())
                {
                    if (kvp.Key != workType && kvp.Value > oldOrder && kvp.Value <= newOrder)
                    {
                        WorkTypeOrder[kvp.Key]--;
                        changedCount++;
                    }
                }
            }
            else if (newOrder < oldOrder)
            {
                // Shift all work types between newOrder and oldOrder up by 1
                foreach (var kvp in WorkTypeOrder.ToList())
                {
                    if (kvp.Key != workType && kvp.Value >= newOrder && kvp.Value < oldOrder)
                    {
                        WorkTypeOrder[kvp.Key]++;
                        changedCount++;
                    }
                }
            }
        }
    }
}