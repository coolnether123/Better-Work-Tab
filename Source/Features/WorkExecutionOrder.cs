using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Foundation.GameState;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Better_Work_Tab.Features
{
    /// <summary>
    /// Rebuilds a pawn's WorkGiver order using the player's saved Work column order
    /// as the tiebreaker among equal manual priorities. Preserves all vanilla logic
    /// regarding emergency vs normal lists and priority handling.
    /// </summary>
    internal static class WorkExecutionOrder
    {
        private struct WorkTypeExecutionRecord
        {
            internal WorkTypeDef WorkType;
            internal int ParentPriority;
            internal int ExecutionPriority;
            internal IReadOnlyList<WorkGiver> OrderedWorkGivers;
        }

        private static readonly BindingFlags InstPriv = BindingFlags.Instance | BindingFlags.NonPublic;
        private static FieldInfo fiEmerg;
        private static FieldInfo fiNormal;
        private static FieldInfo fiDirty;

        private static FieldInfo EmergFI
        {
            get { return fiEmerg ?? (fiEmerg = typeof(Pawn_WorkSettings).GetField("workGiversInOrderEmerg", InstPriv)); }
        }
        private static FieldInfo NormalFI
        {
            get { return fiNormal ?? (fiNormal = typeof(Pawn_WorkSettings).GetField("workGiversInOrderNormal", InstPriv)); }
        }
        private static FieldInfo DirtyFI
        {
            get { return fiDirty ?? (fiDirty = typeof(Pawn_WorkSettings).GetField("workGiversDirty", InstPriv)); }
        }
        private static FieldInfo fiPawn;
        private static Game cachedCustomOrderGame;
        private static int cachedCustomOrderGeneration = int.MinValue;
        private static bool cachedHasCustomOrder;
        private static readonly Dictionary<string, int> CachedColumnOrderIndexes =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<string> EmptyColumnOrder = new List<string>(0);
        private static FieldInfo PawnFI
        {
            get { return fiPawn ?? (fiPawn = typeof(Pawn_WorkSettings).GetField("pawn", InstPriv)); }
        }

        internal static bool HasCustomExecutionOrder
        {
            get
            {
                IWorkTabColumnOrderState state =
                    WorkTabGameRoots.For(Current.Game)?.State.ColumnOrder;
                if (state == null)
                {
                    return false;
                }

                EnsureColumnOrderCache(state);

                return cachedHasCustomOrder;
            }
        }

        /// <summary>
        /// Build and assign WorkGiversInOrder lists honoring saved column order.
        /// </summary>
        internal static void RebuildUsingSavedColumnOrder(Pawn_WorkSettings ws)
        {
            if (ws == null)
                return;
            var pawn = PawnFI.GetValue(ws) as Pawn;

            // 1) Gather active work types and min non-emergency priority like vanilla
            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            var activeWTs = new List<WorkTypeExecutionRecord>(allWorkTypes.Count);
            int minNonEmerg = 999;
            for (int i = 0; i < allWorkTypes.Count; i++)
            {
                var w = allWorkTypes[i];
                int parentPriority = GetPriority(ws, pawn, w);
                int executionPriority = WorkGiverReassignmentManager.GetExecutionPriorityForWorkType(
                    pawn,
                    w,
                    parentPriority);
                if (executionPriority > 0)
                {
                    if (executionPriority < minNonEmerg && WorkGiverReassignmentManager.HasNonEmergencyWorkGiver(w))
                        minNonEmerg = executionPriority;
                    activeWTs.Add(new WorkTypeExecutionRecord
                    {
                        WorkType = w,
                        ParentPriority = parentPriority,
                        ExecutionPriority = executionPriority
                    });
                }
            }

            // 2) Build saved order index map from settings (workType.defName -> index)
            IWorkTabColumnOrderState state =
                WorkTabGameRoots.For(Current.Game)?.State.ColumnOrder;
            IReadOnlyDictionary<string, int> indexMap = GetColumnOrderIndexes(state);

            // 3) Sort active work types: manual priority asc, saved order asc, naturalPriority desc
            activeWTs.Sort((a, b) =>
            {
                int c = a.ExecutionPriority.CompareTo(b.ExecutionPriority);
                if (c != 0) return c;
                int ia = indexMap.TryGetValue(a.WorkType.defName, out int iax) ? iax : int.MaxValue;
                int ib = indexMap.TryGetValue(b.WorkType.defName, out int ibx) ? ibx : int.MaxValue;
                c = ia.CompareTo(ib);
                if (c != 0) return c;
                return b.WorkType.naturalPriority.CompareTo(a.WorkType.naturalPriority);
            });

            // 4) Collect each pawn-specific list once, then compose both outputs in one pass.
            for (int i = 0; i < activeWTs.Count; i++)
            {
                WorkTypeExecutionRecord record = activeWTs[i];
                record.OrderedWorkGivers = WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(
                    record.WorkType,
                    pawn);
                activeWTs[i] = record;
            }

            // 5) Compose emerg and normal lists using sorted work types
            var emerg = new List<WorkGiver>();
            var normal = new List<WorkGiver>();

            for (int i = 0; i < activeWTs.Count; i++)
            {
                WorkTypeExecutionRecord record = activeWTs[i];
                for (int j = 0; j < record.OrderedWorkGivers.Count; j++)
                {
                    var worker = record.OrderedWorkGivers[j];
                    if (worker?.def == null)
                    {
                        continue;
                    }

                    if (!CanUseWorkGiverNow(pawn, record.WorkType, worker.def, record.ParentPriority))
                    {
                        continue;
                    }

                    if (worker.def.emergency && record.ExecutionPriority <= minNonEmerg)
                        emerg.Add(worker);

                    if (!worker.def.emergency || record.ExecutionPriority > minNonEmerg)
                        normal.Add(worker);
                }
            }

            // 6) Assign to instance fields and clear dirty flag
            NormalFI.SetValue(ws, normal);
            EmergFI.SetValue(ws, emerg);
            DirtyFI.SetValue(ws, false);
        }

        private static IReadOnlyDictionary<string, int> GetColumnOrderIndexes(
            IWorkTabColumnOrderState state)
        {
            EnsureColumnOrderCache(state);
            return CachedColumnOrderIndexes;
        }

        private static void EnsureColumnOrderCache(IWorkTabColumnOrderState state)
        {
            Game game = Current.Game;
            if (state != null &&
                ReferenceEquals(cachedCustomOrderGame, game) &&
                cachedCustomOrderGeneration == state.Generation)
            {
                return;
            }

            cachedCustomOrderGame = game;
            cachedCustomOrderGeneration = state?.Generation ?? int.MinValue;
            CachedColumnOrderIndexes.Clear();

            List<string> saved = state?.CurrentOrder ?? EmptyColumnOrder;
            for (int i = 0; i < saved.Count; i++)
            {
                string defName = saved[i];
                if (!string.IsNullOrEmpty(defName) && !CachedColumnOrderIndexes.ContainsKey(defName))
                {
                    CachedColumnOrderIndexes.Add(defName, i);
                }
            }

            cachedHasCustomOrder = state != null && HasDifferentOrder(
                state.CurrentOrder,
                state.BaselineOrder);
        }

        private static bool HasDifferentOrder(List<string> current, List<string> baseline)
        {
            if (current == null || current.Count == 0 || baseline == null || current.Count != baseline.Count)
            {
                return current != null && current.Count > 0;
            }

            for (int i = 0; i < current.Count; i++)
            {
                if (!string.Equals(current[i], baseline[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetPriority(Pawn_WorkSettings workSettings, Pawn pawn, WorkTypeDef workType)
        {
            int basePriority = WorkPrioritySystem.ClampPriority(workSettings.GetPriority(workType));
            return TimePriorityService.GetEffectiveWorkTypePriority(pawn, workType, basePriority);
        }

        private static bool CanUseWorkGiverNow(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, int parentPriority)
        {
            int workGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            workGiverPriority = TimePriorityService.GetEffectiveWorkGiverPriority(pawn, workType, workGiver, workGiverPriority);
            return workGiverPriority > WorkPrioritySystem.DisabledPriority;
        }

        /// <summary>
        /// Mark all pawns' work settings to recache WorkGivers on next use.
        /// Called after column reorder so new UI order is honored by AI.
        /// </summary>
        internal static void MarkAllPawnsWorkGiversDirty()
        {
            foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                try
                {
                    if (p?.Faction == Faction.OfPlayer && p?.workSettings != null)
                        p.workSettings.Notify_UseWorkPrioritiesChanged();
                }
                catch { /* ignore individual pawn issues */ }
            }
        }
    }

    /// <summary>
    /// Prefix-patch CacheWorkGiversInOrder to fully replace list composition, using saved
    /// Work column order as the tie-breaker among equal manual priorities.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.CacheWorkGiversInOrder))]
    internal static class Patch_WorkExecutionOrder_ReplaceCache
    {
        [HarmonyBefore(new[] { "fluffy.worktab" })]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Pawn_WorkSettings __instance)
        {
            if (!PriorityAuthorityBroker.ShouldRunBetterWorkTabOrdering)
            {
                return true;
            }

            try
            {
                WorkExecutionOrder.RebuildUsingSavedColumnOrder(__instance);
                return false; // skip original
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[Better Work Tab/Error] Failed to find workgiver for a worktype. This should not happen. Exception: {ex.Message}");
                return true; // fall back to vanilla
            }
        }
    }
}
