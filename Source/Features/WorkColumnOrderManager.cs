using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Columns;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features
{
    public static class WorkColumnOrderManager
    {
        private static List<string> _trueVanillaColumnOrder;
        private static Game _lastInitializedGame;
        private static Game _followedVisualOrderGame;
        private static List<string> _followedVisualOrder;
        private static Dictionary<WorkTypeDef, List<WorkTypeDef>> _similarWorktypeMap;
        private static readonly List<WorkTypeDef> EmptySimilarWorktypeList = new List<WorkTypeDef>(0);

        private static GameComponent_BWTWorldSettings SharedState
            => Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();

        private static List<string> GetSharedOrderOrNull()
            => SharedState?.ColumnCurrentOrder is { Count: > 0 } list ? list : null;

        private static void SetSharedOrder(List<string> order)
            => SharedState?.SetColumnCurrentOrder(order);

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
            ApplySaved();
            BetterWorkTabMod.DebugLog("[BWT] WorkColumnOrderManager initialized for game.", DebugFeature.DragDrop);
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
            if (workType == null)
            {
                return EmptySimilarWorktypeList;
            }

            // This relationship is only consumed by the optional similar-work-type
            // hover interaction. Avoid paying its O(work types squared) construction
            // cost during every game load when that interaction may never be used.
            InitializeSimilarWorktypeMap();

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
        /// Applies the saved custom work column order using shared state.
        /// </summary>
        public static void ApplySaved()
        {
            var order = GetSharedOrderOrNull();

            if (order == null || order.Count == 0)
            {
                return;
            }

            if (TryApplyColumnOrder(order, null))
            {
                Features.Application.WorkTabApplication.Current?.CompleteExecutionOrderMutation();
            }
        }

        /// <summary>Compatibility overload for integrations built against the legacy API.</summary>
        [System.Obsolete("Use ApplySaved(); this overload only projects shared order into the supplied table.")]
        public static void ApplySaved(PawnTableDef tableDef)
        {
            if (ReferenceEquals(tableDef, PawnTableDefOf.Work))
            {
                ApplySaved();
                return;
            }

            List<string> order = GetSharedOrderOrNull();
            if (order != null && order.Count > 0)
            {
                TryApplyOrderToTable(tableDef, order, out _);
            }
        }

        /// <summary>Compatibility entry point routed through the application command boundary.</summary>
        [System.Obsolete("Submit column-order changes through Better Work Tab's application boundary.")]
        public static List<string> CaptureCurrent(PawnTableDef tableDef)
        {
            var order = tableDef?.columns?
                .Where(column => column.Worker is PawnColumnWorker_WorkPriority && column.workType != null)
                .Select(column => column.workType.defName)
                .ToList() ?? new List<string>();
            if (order.Count > 0)
            {
                Features.Application.WorkTabApplication.Current?
                    .SubmitCapturedColumnOrder(order);
            }
            return order;
        }

        /// <summary>
        /// Applies one normalized work-column permutation to both the visible
        /// table definition and the per-world execution-order record.
        /// Completion effects belong to <see cref="Features.Application.WorkTabApplication"/>.
        /// </summary>
        internal static bool TryApplyColumnOrder(
            IReadOnlyList<string> orderedDefNames,
            IReadOnlyList<string> movedDefNames,
            bool persistShared = true)
        {
            PawnTableDef tableDef = PawnTableDefOf.Work;
            if (!TryApplyOrderToTable(
                    tableDef,
                    orderedDefNames,
                    out List<string> applied,
                    out bool orderChanged))
            {
                return false;
            }

            List<string> shared = GetSharedOrderOrNull();
            bool sharedChanged = persistShared &&
                (shared == null || !shared.SequenceEqual(applied));
            if (sharedChanged) SetSharedOrder(applied);
            if (persistShared)
            {
                _followedVisualOrderGame = null;
                _followedVisualOrder = null;
            }

            bool markerRequested = false;
            if (movedDefNames != null)
            {
                foreach (string defName in movedDefNames.Distinct(System.StringComparer.Ordinal))
                {
                    WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
                    if (workType == null)
                    {
                        continue;
                    }

                    WorkColumnCustomizationService.MarkColumnMoved(workType);
                    markerRequested = true;
                }
            }

            return orderChanged || sharedChanged || markerRequested;
        }

        internal static bool TryCaptureSharedColumnOrder(IReadOnlyList<string> orderedDefNames)
        {
            if (orderedDefNames == null || orderedDefNames.Count == 0) return false;
            var normalized = orderedDefNames
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();
            if (normalized.Count == 0) return false;
            List<string> shared = GetSharedOrderOrNull();
            if (shared != null && shared.SequenceEqual(normalized)) return false;
            SetSharedOrder(normalized);
            return true;
        }

        /// <summary>
        /// Applies a followed player's column layout as a client-local visual projection.
        /// The shared column order remains the sole simulation execution tiebreaker.
        /// </summary>
        internal static bool TryApplyLocalFollowedColumnOrder(IReadOnlyList<string> orderedDefNames)
        {
            bool changed = TryApplyColumnOrder(orderedDefNames, null, persistShared: false);
            _followedVisualOrderGame = Current.Game;
            _followedVisualOrder = PawnTableDefOf.Work?.columns?
                .Where(column => column.Worker is PawnColumnWorker_WorkPriority && column.workType != null)
                .Select(column => column.workType.defName)
                .ToList();
            if (changed)
            {
                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                    UI.WorkGrid.Contracts.WorkTabDirtyFlags.Columns |
                    UI.WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
            }
            return changed;
        }

        internal static IReadOnlyList<string> LocalFollowedVisualOrder =>
            ReferenceEquals(_followedVisualOrderGame, Current.Game)
                ? _followedVisualOrder
                : null;

        /// <summary>
        /// Resets work columns to their vanilla RimWorld order.
        /// This also clears all player-dragged column markers since we're going back to the original layout.
        /// </summary>
        public static void ResetToVanilla() => Reset(useTrueVanilla: false);

        /// <summary>
        /// Resets work columns to the per-save baseline captured when the game was first created (includes modded columns).
        /// </summary>
        public static void ResetToBaseline() => ResetToVanilla();

        /// <summary>
        /// Resets work columns to true vanilla RimWorld order.
        /// Modded columns are appended after vanilla, preserving their baseline order when possible.
        /// </summary>
        public static void ResetToTrueVanilla() => Reset(useTrueVanilla: true);

        private static void Reset(bool useTrueVanilla)
        {
            Features.Application.WorkTabApplication app =
                Features.Application.WorkTabApplication.Current;
            app?.SubmitColumnReset(useTrueVanilla);
        }

        internal static bool TryGetResetColumnOrder(bool useTrueVanilla, out List<string> requested)
        {
            requested = null;
            PawnTableDef def = PawnTableDefOf.Work;
            List<string> primary = useTrueVanilla ? GetTrueVanillaOrder() : GetBaselineOrder();
            if (def?.columns == null || primary == null || primary.Count == 0) return false;
            List<string> secondary = useTrueVanilla
                ? GetBaselineOrder().Where(defName => !primary.Contains(defName)).ToList()
                : null;
            requested = new List<string>(primary);
            if (secondary != null) requested.AddRange(secondary);
            return true;
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

        private static bool TryApplyOrderToTable(
            PawnTableDef tableDef,
            IReadOnlyList<string> orderedDefNames,
            out List<string> applied) =>
            TryApplyOrderToTable(tableDef, orderedDefNames, out applied, out _);

        private static bool TryApplyOrderToTable(
            PawnTableDef tableDef,
            IReadOnlyList<string> orderedDefNames,
            out List<string> applied,
            out bool changed)
        {
            applied = null;
            changed = false;
            if (!TrySplitColumns(tableDef, out List<PawnColumnDef> preWork,
                    out List<PawnColumnDef> workColumns, out List<PawnColumnDef> postWork) ||
                orderedDefNames == null || orderedDefNames.Count == 0)
            {
                return false;
            }

            var requested = orderedDefNames
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();
            var current = workColumns
                .Where(column => column.workType != null)
                .Select(column => column.workType.defName)
                .ToList();
            List<PawnColumnDef> reordered = BuildOrderedWorkColumns(workColumns, requested, current);
            applied = reordered
                .Where(column => column.workType != null)
                .Select(column => column.workType.defName)
                .ToList();
            if (applied.Count != current.Count ||
                applied.Distinct(System.StringComparer.Ordinal).Count() != applied.Count)
            {
                return false;
            }

            changed = !current.SequenceEqual(applied);
            if (changed)
            {
                tableDef.columns.Clear();
                tableDef.columns.AddRange(preWork);
                tableDef.columns.AddRange(reordered);
                tableDef.columns.AddRange(postWork);
            }
            return true;
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

        private static bool RemoveStoredWorkColumnWidths(PawnTableDef def)
        {
            if (BetterWorkTabMod.Settings.storedColumnWidths == null)
            {
                return false;
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
            return keysToRemove.Count > 0;
        }

        internal static bool ClearLocalResetPresentationState(PawnTableDef def)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null) return false;
            bool changed = settings.workColumnOrderDefNames?.Count > 0;
            settings.workColumnOrderDefNames?.Clear();
            changed |= WorkColumnCustomizationService.ClearAllColumnMarkers(persist: false);
            changed |= RemoveStoredWorkColumnWidths(def);
            if (changed) settings.Write();
            return changed;
        }
    }
}
