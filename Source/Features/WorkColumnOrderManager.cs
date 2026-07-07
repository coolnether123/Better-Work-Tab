using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.UI;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features
{
    public static class WorkColumnOrderManager
    {
        private static List<string> _trueVanillaColumnOrder;
        private static Game _lastInitializedGame;
        private static Dictionary<WorkTypeDef, List<WorkTypeDef>> _similarWorktypeMap;
        private static readonly List<WorkTypeDef> EmptySimilarWorktypeList = new List<WorkTypeDef>(0);

        private static GameComponent_BWTWorldSettings SharedState
            => Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();

        private static List<string> GetSharedOrderOrNull()
            => SharedState?.ColumnCurrentOrder is { Count: > 0 } list ? list : null;

        private static void SetSharedOrder(List<string> order)
        {
            if (SharedState == null)
                return;

            SharedState.ColumnCurrentOrder = order;
        }

        /// <summary>
        /// Initialize vanilla order and apply saved order. Called after defs are loaded.
        /// </summary>
        public static void InitializeOnGameLoad()
        {
            CaptureVanillaOrder();

            var game = Current.Game;
            if (game == null)
            {
                return;
            }

            if (ReferenceEquals(_lastInitializedGame, game))
            {
                return;
            }

            _lastInitializedGame = game;

            EnsureColumnsMatchTrueVanillaShape();
            var component = game.GetComponent<GameComponent_BWTWorldSettings>();
            ColumnBaselineManager.EnsureBaseline(component);
            ApplySaved(PawnTableDefOf.Work);
            BetterWorkTabMod.DebugLog("[BWT] WorkColumnOrderManager initialized for game.", DebugFeature.DragDrop);
        }

        /// <summary>
        /// Gets the current column order from the Work table def.
        /// Returns a list of work type defNames in current order.
        /// </summary>
        internal static List<string> GetCurrentOrder()
        {
            var tableDef = DefDatabase<PawnTableDef>.GetNamed("Work");
            if (tableDef?.columns == null || tableDef.columns.Count == 0)
            {
                return new List<string>();
            }

            var order = new List<string>();
            foreach (var col in tableDef.columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                {
                    order.Add(col.workType.defName);
                }
            }

            return order;
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
            if (_trueVanillaColumnOrder != null && _trueVanillaColumnOrder.Count > 0)
                return;

            _trueVanillaColumnOrder = new List<string>(ColumnBaselineManager.GetTrueVanillaOrder());

            if (_trueVanillaColumnOrder == null || _trueVanillaColumnOrder.Count == 0)
            {
                Log.Warning("WorkColumnOrderManager.CaptureVanillaOrder: Could not capture vanilla order.");
                _trueVanillaColumnOrder = new List<string>();
                return;
            }

            BetterWorkTabMod.DebugLog($"WorkColumnOrderManager.CaptureVanillaOrder: Captured vanilla order: {string.Join(", ", _trueVanillaColumnOrder)}", DebugFeature.DragDrop);
        }

        /// <summary>
        /// Gets the stored per-save baseline order (includes modded work types), or falls back to vanilla if missing.
        /// </summary>
        public static List<string> GetVanillaOrder()
        {
            return GetBaselineOrder();
        }

        /// <summary>
        /// Gets the per-save baseline order if available, otherwise returns true vanilla.
        /// </summary>
        public static List<string> GetBaselineOrder()
        {
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            var baseline = ColumnBaselineManager.GetBaselineOrder(component);
            if (baseline != null && baseline.Count > 0)
            {
                return baseline;
            }

            return GetTrueVanillaOrder();
        }

        /// <summary>
        /// Gets the static vanilla order (official RimWorld work types).
        /// </summary>
        public static List<string> GetTrueVanillaOrder()
        {
            CaptureVanillaOrder();
            return _trueVanillaColumnOrder ?? new List<string>();
        }

        /// <summary>
        /// Captures the current order of work type columns and saves to the shared component.
        /// </summary>
        public static List<string> CaptureCurrent(PawnTableDef tableDef)
        {
            BetterWorkTabMod.DebugLog("WorkColumnOrderManager.CaptureCurrent called.", DebugFeature.DragDrop);

            if (tableDef?.columns == null)
            {
                BetterWorkTabMod.DebugLog("WorkColumnOrderManager.CaptureCurrent: def or columns are null.", DebugFeature.DragDrop);
                return new List<string>();
            }

            var order = new List<string>();
            foreach (var col in tableDef.columns)
            {
                if (col.Worker is PawnColumnWorker_WorkPriority && col.workType != null)
                    order.Add(col.workType.defName);
            }

            SetSharedOrder(order);

            BetterWorkTabMod.DebugLog($"WorkColumnOrderManager.CaptureCurrent: Captured order: {string.Join(", ", order)}", DebugFeature.DragDrop);
            return order;
        }

        /// <summary>
        /// Applies the saved custom work column order using shared state.
        /// </summary>
        public static void ApplySaved(PawnTableDef tableDef)
        {
            var order = GetSharedOrderOrNull();

            if (order == null || order.Count == 0)
            {
                return;
            }

            ApplyOrderToTable(tableDef, order);
        }

        private static void ApplyOrderToTable(PawnTableDef tableDef, List<string> order)
        {
            if (tableDef?.columns == null)
                return;

            var workColumns = tableDef.columns
                .Where(c => c.Worker is PawnColumnWorker_WorkPriority && c.workType != null)
                .ToList();

            var nonWork = tableDef.columns
                .Where(c => !(c.Worker is PawnColumnWorker_WorkPriority))
                .ToList();

            var sorted = new List<PawnColumnDef>();

            foreach (var defName in order)
            {
                var match = workColumns.FirstOrDefault(w => w.workType?.defName == defName);
                if (match != null)
                    sorted.Add(match);
            }

            foreach (var wc in workColumns)
            {
                if (!sorted.Contains(wc))
                    sorted.Add(wc);
            }

            tableDef.columns.Clear();
            tableDef.columns.AddRange(nonWork);
            tableDef.columns.AddRange(sorted);

            SetSharedOrder(sorted
                .Where(c => c.workType != null)
                .Select(c => c.workType.defName)
                .ToList());

            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        /// <summary>
        /// Resets work columns to their vanilla RimWorld order.
        /// This also clears all player-dragged column markers since we're going back to the original layout.
        /// </summary>
        public static void ResetToVanilla()
        {
            ResetToBaseline();
        }

        /// <summary>
        /// Resets work columns to the per-save baseline captured when the game was first created (includes modded columns).
        /// </summary>
        public static void ResetToBaseline()
        {
            BetterWorkTabMod.DebugLog("WorkColumnOrderManager.ResetToBaseline called.", DebugFeature.DragDrop);

            var def = PawnTableDefOf.Work;
            if (def?.columns == null)
            {
                Log.Warning("WorkColumnOrderManager.ResetToBaseline: def or columns are null.");
                return;
            }

            var baselineOrder = GetBaselineOrder();
            if (baselineOrder == null || baselineOrder.Count == 0)
            {
                Log.Warning("WorkColumnOrderManager.ResetToBaseline: No baseline order available.");
                return;
            }

            ResetColumnsToOrder(def, baselineOrder, null, "ResetToBaseline");
        }

        /// <summary>
        /// Resets work columns to true vanilla RimWorld order.
        /// Modded columns are appended after vanilla, preserving their baseline order when possible.
        /// </summary>
        public static void ResetToTrueVanilla()
        {
            BetterWorkTabMod.DebugLog("WorkColumnOrderManager.ResetToTrueVanilla called.", DebugFeature.DragDrop);

            var def = PawnTableDefOf.Work;
            if (def?.columns == null)
            {
                Log.Warning("WorkColumnOrderManager.ResetToTrueVanilla: def or columns are null.");
                return;
            }

            var vanillaOrder = GetTrueVanillaOrder();
            if (vanillaOrder == null || vanillaOrder.Count == 0)
            {
                Log.Warning("WorkColumnOrderManager.ResetToTrueVanilla: No vanilla order available.");
                return;
            }

            // Use baseline order as a hint for modded columns so they keep their initial relative order.
            var baselineOrder = GetBaselineOrder();
            var extrasInBaselineOrder = baselineOrder
                .Where(defName => !vanillaOrder.Contains(defName))
                .ToList();

            ResetColumnsToOrder(def, vanillaOrder, extrasInBaselineOrder, "ResetToTrueVanilla");
        }

        /// <summary>
        /// Ensures the work columns are arranged in true vanilla order (official work types),
        /// with any modded extras appended in their current order. Used on game load so the
        /// baseline capture compares against an unmodified vanilla layout.
        /// </summary>
        private static void EnsureColumnsMatchTrueVanillaShape()
        {
            var def = PawnTableDefOf.Work;
            if (!TrySplitColumns(def, out var preWork, out var workCols, out var postWork))
            {
                return;
            }

            var vanillaOrder = GetTrueVanillaOrder();
            if (vanillaOrder == null || vanillaOrder.Count == 0)
            {
                return;
            }

            var currentOrder = workCols
                .Where(c => c.workType != null)
                .Select(c => c.workType.defName)
                .ToList();

            var reorderedWork = BuildOrderedWorkColumns(workCols, vanillaOrder, currentOrder);

            def.columns.Clear();
            def.columns.AddRange(preWork);
            def.columns.AddRange(reorderedWork);
            def.columns.AddRange(postWork);
        }

        private static bool TrySplitColumns(PawnTableDef def, out List<PawnColumnDef> preWork, out List<PawnColumnDef> workCols, out List<PawnColumnDef> postWork)
        {
            preWork = new List<PawnColumnDef>();
            workCols = new List<PawnColumnDef>();
            postWork = new List<PawnColumnDef>();

            if (def?.columns == null)
            {
                return false;
            }

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

            return workCols.Count > 0;
        }

        private static List<PawnColumnDef> BuildOrderedWorkColumns(List<PawnColumnDef> workCols, List<string> primaryOrder, List<string> secondaryOrder)
        {
            var colMap = new Dictionary<string, Queue<PawnColumnDef>>();
            foreach (var col in workCols)
            {
                string defName = col.workType?.defName;
                if (string.IsNullOrEmpty(defName))
                {
                    continue;
                }

                if (!colMap.TryGetValue(defName, out var queue))
                {
                    queue = new Queue<PawnColumnDef>();
                    colMap[defName] = queue;
                }

                queue.Enqueue(col);
            }

            var reorderedWork = new List<PawnColumnDef>();

            void AppendOrder(IEnumerable<string> order)
            {
                if (order == null)
                {
                    return;
                }

                foreach (var defName in order)
                {
                    if (colMap.TryGetValue(defName, out var columns) && columns.Count > 0)
                    {
                        var col = columns.Dequeue();
                        reorderedWork.Add(col);
                        if (columns.Count == 0)
                        {
                            colMap.Remove(defName);
                        }
                    }
                }
            }

            AppendOrder(primaryOrder);
            AppendOrder(secondaryOrder);

            // Append any missing/unknown in their current order
            foreach (var col in workCols)
            {
                if (!reorderedWork.Contains(col))
                {
                    reorderedWork.Add(col);
                }
            }

            return reorderedWork;
        }

        private static void RemoveStoredWorkColumnWidths(PawnTableDef def)
        {
            if (BetterWorkTabMod.Settings.storedColumnWidths == null)
            {
                return;
            }

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

        private static void ResetColumnsToOrder(PawnTableDef def, List<string> primaryOrder, List<string> secondaryOrder, string debugContext)
        {
            if (!TrySplitColumns(def, out var preWork, out var workCols, out var postWork))
            {
                Log.Warning($"WorkColumnOrderManager.{debugContext}: No work columns found.");
                return;
            }

            var reorderedWork = BuildOrderedWorkColumns(workCols, primaryOrder, secondaryOrder);

            // Rebuild def.columns with target order
            def.columns.Clear();
            def.columns.AddRange(preWork);
            def.columns.AddRange(reorderedWork);
            def.columns.AddRange(postWork);

            SetSharedOrder(reorderedWork
                .Where(c => c.workType != null)
                .Select(c => c.workType.defName)
                .ToList());

            // Clear the saved custom order
            BetterWorkTabMod.Settings.workColumnOrderDefNames.Clear();

            // Clear all player-dragged column markers since we're back to baseline/vanilla
            MainTabWindow_BetterWork.ClearAllColumnMarkers();

            // Remove stored column widths for work columns
            RemoveStoredWorkColumnWidths(def);

            // Save changes
            BetterWorkTabMod.Settings.Write();

            // Notify UI to rebuild
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

            BetterWorkTabMod.DebugLog($"WorkColumnOrderManager.{debugContext}: Complete. Columns reset.", DebugFeature.DragDrop);
        }
    }
}
