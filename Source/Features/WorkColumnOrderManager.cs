using Better_Work_Tab.UI;
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
        private static Dictionary<WorkTypeDef, List<WorkTypeDef>> _similarWorktypeMap;
        private static readonly List<WorkTypeDef> EmptySimilarWorktypeList = new List<WorkTypeDef>(0);

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
            BetterWorkTabMod.DebugLog("[BWT] WorkColumnOrderManager initialized.", DebugFeature.DragDrop);
        }

        /// <summary>
        /// Pre-compute which work types share any relevant skills. Runs once after defs load.
        /// </summary>
        public static void InitializeSimilarWorktypeMap()
        {
            if (_similarWorktypeMap != null)
            {
                return;
            }

            _similarWorktypeMap = new Dictionary<WorkTypeDef, List<WorkTypeDef>>();
            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            foreach (var mainWorkType in allWorkTypes)
            {
                var similarList = new List<WorkTypeDef>();

                if (mainWorkType.relevantSkills == null || mainWorkType.relevantSkills.Count == 0)
                {
                    _similarWorktypeMap[mainWorkType] = similarList;
                    continue;
                }

                foreach (var otherWorkType in allWorkTypes)
                {
                    if (mainWorkType == otherWorkType || otherWorkType.relevantSkills == null)
                    {
                        continue;
                    }

                    if (mainWorkType.relevantSkills.Any(s => otherWorkType.relevantSkills.Contains(s)))
                    {
                        similarList.Add(otherWorkType);
                    }
                }

                _similarWorktypeMap[mainWorkType] = similarList;
            }

            BetterWorkTabMod.DebugLog("[BWT] SimilarWorktypeMap initialized (one-time cost).", DebugFeature.DragDrop);
        }

        public static List<WorkTypeDef> GetSimilarWorktypes(WorkTypeDef workType)
        {
            if (_similarWorktypeMap == null || workType == null)
            {
                return EmptySimilarWorktypeList;
            }

            return _similarWorktypeMap.TryGetValue(workType, out var list)
                ? list
                : EmptySimilarWorktypeList;
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

            BetterWorkTabMod.DebugLog($"WorkColumnOrderManager.CaptureVanillaOrder: Captured vanilla order: {string.Join(", ", _vanillaColumnOrder)}", DebugFeature.DragDrop);
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
            BetterWorkTabMod.DebugLog("WorkColumnOrderManager.CaptureCurrent called.", DebugFeature.DragDrop);
            if (def?.columns == null)
            {
                BetterWorkTabMod.DebugLog("WorkColumnOrderManager.CaptureCurrent: def or columns are null.", DebugFeature.DragDrop);
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
            BetterWorkTabMod.DebugLog($"WorkColumnOrderManager.CaptureCurrent: Captured order: {string.Join(", ", order)}", DebugFeature.DragDrop);
        }

        /// <summary>
        /// Applies the saved custom work column order from settings to def.columns.
        /// </summary>
        public static void ApplySaved(PawnTableDef def)
        {
            if (def?.columns == null)
            {
                BetterWorkTabMod.DebugLog("WorkColumnOrderManager.ApplySaved: def or columns are null.", DebugFeature.DragDrop);
                return;
            }

            var saved = BetterWorkTabMod.Settings.workColumnOrderDefNames;
            if (saved == null || saved.Count == 0)
            {
                BetterWorkTabMod.DebugLog("WorkColumnOrderManager.ApplySaved: No saved order found.", DebugFeature.DragDrop);
                return;
            }

            BetterWorkTabMod.DebugLog($"WorkColumnOrderManager.ApplySaved: Saved order: {string.Join(", ", saved)}", DebugFeature.DragDrop);

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

            BetterWorkTabMod.DebugLog($"WorkColumnOrderManager.ApplySaved: Sorted columns: {string.Join(", ", sorted.Select(c => c.defName))}", DebugFeature.DragDrop);

            // Rebuild def.columns
            def.columns.Clear();
            def.columns.AddRange(preWork);
            def.columns.AddRange(sorted);
            def.columns.AddRange(postWork);

            BetterWorkTabMod.DebugLog("WorkColumnOrderManager.ApplySaved: Columns reordered.", DebugFeature.DragDrop);
        }

        /// <summary>
        /// Resets work columns to their vanilla RimWorld order.
        /// This also clears all player-dragged column markers since we're going back to the original layout.
        /// </summary>
        public static void ResetToVanilla()
        {
            BetterWorkTabMod.DebugLog("WorkColumnOrderManager.ResetToVanilla called.", DebugFeature.DragDrop);

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

            // Clear all player-dragged column markers since we're back to vanilla
            MainTabWindow_BetterWork.ClearAllColumnMarkers();

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

            BetterWorkTabMod.DebugLog("WorkColumnOrderManager.ResetToVanilla: Complete. Columns reset to vanilla order.", DebugFeature.DragDrop);
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
