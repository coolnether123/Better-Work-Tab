using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features
{
    public static class WorkColumnOrderManager
    {
        private static List<string> _vanillaColumnOrder;
        private static bool _initialized = false;

        /// <summary>
        /// Initialize vanilla order and apply saved order. Called after defs are loaded.
        /// </summary>
        public static void InitializeOnGameLoad()
        {
            if (_initialized)
                return;

            _initialized = true;
            CaptureVanillaOrder();
            ApplySaved(PawnTableDefOf.Work);
            Log.Message("[BWT] WorkColumnOrderManager initialized.");
        }

        /// <summary>
        /// Captures and stores the vanilla column order from current def.columns.
        /// </summary>
        public static void CaptureVanillaOrder()
        {
            if (_vanillaColumnOrder != null)
                return;

            _vanillaColumnOrder = new List<string>();
            var def = PawnTableDefOf.Work;

            if (def?.columns == null)
            {
                Log.Warning("WorkColumnOrderManager.CaptureVanillaOrder: Could not capture vanilla order.");
                return;
            }

            foreach (var col in def.columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    _vanillaColumnOrder.Add(col.workType.defName);
                }
            }

            Log.Message($"WorkColumnOrderManager.CaptureVanillaOrder: Captured vanilla order: {string.Join(", ", _vanillaColumnOrder)}");
        }

        /// <summary>
        /// Gets the stored vanilla column order.
        /// </summary>
        public static List<string> GetVanillaOrder()
        {
            return _vanillaColumnOrder ?? new List<string>();
        }

        /// <summary>
        /// Captures the current order of work type columns and saves to settings.
        /// </summary>
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
        /// Applies the saved custom work column order from settings to def.columns.
        /// </summary>
        public static void ApplySaved(PawnTableDef def)
        {
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

            // Separate columns into pre-work, work, and post-work
            var preWork = new List<PawnColumnDef>();
            var workCols = new List<PawnColumnDef>();
            var postWork = new List<PawnColumnDef>();

            bool passedFirstWork = false;

            foreach (var col in def.columns)
            {
                bool isWorkCol = col.Worker is PawnColumnWorker_WorkPriority && col.workType != null;

                if (isWorkCol)
                {
                    passedFirstWork = true;
                    workCols.Add(col);
                }
                else if (!passedFirstWork)
                {
                    preWork.Add(col);
                }
                else
                {
                    postWork.Add(col);
                }
            }

            if (workCols.Count == 0)
            {
                return;
            }

            // Build map of defName -> column
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

            // Rebuild def.columns
            def.columns.Clear();
            def.columns.AddRange(preWork);
            def.columns.AddRange(sorted);
            def.columns.AddRange(postWork);

            Log.Message("WorkColumnOrderManager.ApplySaved: Columns reordered.");
        }

        /// <summary>
        /// Resets work columns to their vanilla RimWorld order.
        /// </summary>
        public static void ResetToVanilla()
        {
            Log.Message("WorkColumnOrderManager.ResetToVanilla called.");

            var def = PawnTableDefOf.Work;
            if (def?.columns == null)
            {
                Log.Warning("WorkColumnOrderManager.ResetToVanilla: def or columns are null.");
                return;
            }

            var vanillaOrder = GetVanillaOrder();
            if (vanillaOrder == null || vanillaOrder.Count == 0)
            {
                Log.Warning("WorkColumnOrderManager.ResetToVanilla: No vanilla order available.");
                return;
            }

            // Separate columns into pre-work, work, and post-work
            var preWork = new List<PawnColumnDef>();
            var workCols = new List<PawnColumnDef>();
            var postWork = new List<PawnColumnDef>();

            bool passedFirstWork = false;

            foreach (var col in def.columns)
            {
                bool isWorkCol = col.Worker is PawnColumnWorker_WorkPriority && col.workType != null;

                if (isWorkCol)
                {
                    passedFirstWork = true;
                    workCols.Add(col);
                }
                else if (!passedFirstWork)
                {
                    preWork.Add(col);
                }
                else
                {
                    postWork.Add(col);
                }
            }

            if (workCols.Count == 0)
            {
                Log.Warning("WorkColumnOrderManager.ResetToVanilla: No work columns found.");
                return;
            }

            // Build a map of defName -> column for reordering
            var colMap = new Dictionary<string, PawnColumnDef>();
            foreach (var col in workCols)
            {
                colMap[col.workType.defName] = col;
            }

            // Build the reordered work columns list based on vanilla order
            var reorderedWork = new List<PawnColumnDef>();
            foreach (var defName in vanillaOrder)
            {
                if (colMap.TryGetValue(defName, out var col))
                {
                    reorderedWork.Add(col);
                }
            }

            // Rebuild def.columns with vanilla order
            def.columns.Clear();
            def.columns.AddRange(preWork);
            def.columns.AddRange(reorderedWork);
            def.columns.AddRange(postWork);

            // Clear the saved custom order
            BetterWorkTabMod.Settings.workColumnOrderDefNames.Clear();

            // Remove stored column widths for work columns
            if (BetterWorkTabMod.Settings.storedColumnWidths != null)
            {
                var keysToRemove = new List<string>();
                foreach (var kvp in BetterWorkTabMod.Settings.storedColumnWidths)
                {
                    var col = def.columns.FirstOrDefault(c => c.defName == kvp.Key);
                    if (col != null && col.Worker is PawnColumnWorker_WorkPriority)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    BetterWorkTabMod.Settings.storedColumnWidths.Remove(key);
                }
            }

            // Save changes
            BetterWorkTabMod.Settings.Write();

            // Notify UI to rebuild
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            UI.MainTabWindow_BetterWork.ClearColumnReorderFlag();
            UI.MainTabWindow_BetterWork.FlagWindowSnap();

            Log.Message("WorkColumnOrderManager.ResetToVanilla: Complete. Columns reset to vanilla order.");
        }

        public static Dictionary<WorkTypeDef, int> WorkTypeOrder = new Dictionary<WorkTypeDef, int>();

        public static void SetWorkTypeOrder(WorkTypeDef workType, int newOrder)
        {
            int changedCount = 0;
            if (!WorkTypeOrder.ContainsKey(workType))
            {
                WorkTypeOrder.Add(workType, newOrder);
                return;
            }

            int oldOrder = WorkTypeOrder[workType];
            WorkTypeOrder[workType] = newOrder;

            if (newOrder > oldOrder)
            {
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