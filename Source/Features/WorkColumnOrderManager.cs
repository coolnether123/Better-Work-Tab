using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// Keeps a persisted ordering of worktype columns for the Work tab.
    /// - CaptureCurrent: read current def.columns and store the order in settings
    /// - ApplySaved: reorder def.columns to match settings if present
    ///
    /// Only touch PawnColumnDef entries whose Worker is PawnColumnWorker_WorkPriority.
    /// Non-work columns keep their relative pre/post position.
    /// </summary>
    public static class WorkColumnOrderManager
    {
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
        /// Assign default manual priorities (1..4) across all player pawns based on
        /// the current Work column order: leftmost columns get priority 1, then 2, 3, 4.
        /// Columns are split into 4 bands using ceil(count/4). Disabled work types are skipped.
        /// </summary>
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

            Better_Work_Tab.Util.WorkTabLogger.Info(Better_Work_Tab.Util.WorkTabLogger.Categories.DragColumn,
                $"Default manual priorities applied from column order (changed {changed} entries).");
        }
    }
}
