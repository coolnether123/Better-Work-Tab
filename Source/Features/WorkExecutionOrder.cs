using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
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
        private static FieldInfo PawnFI
        {
            get { return fiPawn ?? (fiPawn = typeof(Pawn_WorkSettings).GetField("pawn", InstPriv)); }
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
            var activeWTs = new List<WorkTypeDef>(allWorkTypes.Count);
            int minNonEmerg = 999;
            for (int i = 0; i < allWorkTypes.Count; i++)
            {
                var w = allWorkTypes[i];
                int prio = GetExecutionPriority(ws, pawn, w);
                if (prio > 0)
                {
                    if (prio < minNonEmerg && WorkGiverReassignmentManager.HasNonEmergencyWorkGiver(w))
                        minNonEmerg = prio;
                    activeWTs.Add(w);
                }
            }

            // 2) Build saved order index map from settings (workType.defName -> index)
            var comp = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            var saved = comp?.ColumnCurrentOrder ?? new List<string>();
            var indexMap = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < saved.Count; i++)
            {
                if (!string.IsNullOrEmpty(saved[i]) && !indexMap.ContainsKey(saved[i]))
                    indexMap.Add(saved[i], i);
            }

            // 3) Sort active work types: manual priority asc, saved order asc, naturalPriority desc
            activeWTs.Sort((a, b) =>
            {
                int pa = GetExecutionPriority(ws, pawn, a);
                int pb = GetExecutionPriority(ws, pawn, b);
                int c = pa.CompareTo(pb);
                if (c != 0) return c;
                int ia = indexMap.TryGetValue(a.defName, out int iax) ? iax : int.MaxValue;
                int ib = indexMap.TryGetValue(b.defName, out int ibx) ? ibx : int.MaxValue;
                c = ia.CompareTo(ib);
                if (c != 0) return c;
                return b.naturalPriority.CompareTo(a.naturalPriority);
            });

            // 4) Compose emerg and normal lists using sorted work types
            var emerg = new List<WorkGiver>();
            var normal = new List<WorkGiver>();

            for (int i = 0; i < activeWTs.Count; i++)
            {
                var wt = activeWTs[i];
                int wtPriority = GetPriority(ws, pawn, wt);
                var list = WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(wt, pawn);
                for (int j = 0; j < list.Count; j++)
                {
                    var worker = list[j];
                    if (worker?.def == null)
                    {
                        continue;
                    }

                    if (!CanUseWorkGiverNow(pawn, wt, worker.def, wtPriority))
                    {
                        continue;
                    }

                    if (worker.def.emergency && GetExecutionPriority(ws, pawn, wt) <= minNonEmerg)
                        emerg.Add(worker);
                }
            }
            for (int i = 0; i < activeWTs.Count; i++)
            {
                var wt = activeWTs[i];
                int wtPriority = GetPriority(ws, pawn, wt);
                var list = WorkGiverReassignmentManager.GetOrderedWorkGiversForWorkType(wt, pawn);
                for (int j = 0; j < list.Count; j++)
                {
                    var worker = list[j];
                    if (worker?.def == null)
                    {
                        continue;
                    }

                    if (!CanUseWorkGiverNow(pawn, wt, worker.def, wtPriority))
                    {
                        continue;
                    }

                    if (!worker.def.emergency || GetExecutionPriority(ws, pawn, wt) > minNonEmerg)
                        normal.Add(worker);
                }
            }

            // 5) Assign to instance fields and clear dirty flag
            NormalFI.SetValue(ws, normal);
            EmergFI.SetValue(ws, emerg);
            DirtyFI.SetValue(ws, false);
        }

        private static int GetPriority(Pawn_WorkSettings workSettings, Pawn pawn, WorkTypeDef workType)
        {
            int basePriority = WorkPrioritySystem.ClampPriority(workSettings.GetPriority(workType));
            return TimePriorityService.GetEffectiveWorkTypePriority(pawn, workType, basePriority);
        }

        private static int GetExecutionPriority(Pawn_WorkSettings workSettings, Pawn pawn, WorkTypeDef workType)
        {
            int parentPriority = GetPriority(workSettings, pawn, workType);
            return WorkGiverReassignmentManager.GetExecutionPriorityForWorkType(pawn, workType, parentPriority);
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
    [HarmonyPatch(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder")]
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
