using Better_Work_Tab;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
using Multiplayer.API;
#endif
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Central coordinator for workgiver reassignment lookups, caching, and mutation.
    /// </summary>
    internal static class WorkGiverReassignmentManager
    {
        private static readonly Dictionary<int, WorkTypeDef> WorkGiverTargetCache = new Dictionary<int, WorkTypeDef>();
        private static readonly Dictionary<int, bool> ReassignedCache = new Dictionary<int, bool>();
        private static readonly Dictionary<string, List<WorkGiver>> OrderedWorkGiverCache = new Dictionary<string, List<WorkGiver>>(StringComparer.Ordinal);

        private static int _cachedSyncVersion = -1;
        private static BetterWorkTabSettings Settings => BetterWorkTabMod.Settings;

        private static WorkGiverReassignmentData Data
        {
            get
            {
                var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                if (component != null)
                {
                    return component.EnsureWorkGiverReassignmentData();
                }

                if (Settings == null)
                {
                    return null;
                }

                return Settings.LegacyWorkGiverReassignments;
            }
        }

        internal static int CurrentSyncVersion => Data?.SyncVersion ?? 0;

        /// <summary>
        /// Clear caches when the sync version changes or the settings are reloaded.
        /// </summary>
        internal static void InvalidateCaches()
        {
            WorkGiverTargetCache.Clear();
            ReassignedCache.Clear();
            OrderedWorkGiverCache.Clear();
        }

        internal static void OnSettingsLoaded()
        {
            InvalidateCaches();
            _cachedSyncVersion = Data?.SyncVersion ?? 0;
        }

        internal static void MigrateLegacySettingsDataIfNeeded(GameComponent_BWTWorldSettings component)
        {
            if (component == null)
            {
                OnSettingsLoaded();
                return;
            }

            component.EnsureWorkGiverReassignmentData();

            var settings = Settings;
            var legacy = settings?.LegacyWorkGiverReassignments;
            if (legacy != null && legacy.HasAnyData())
            {
                if (!component.WorkGiverReassignments.HasAnyData())
                {
                    component.WorkGiverReassignments = legacy.Clone();
                    BetterWorkTabMod.DebugLog("Migrated legacy global sub-work reassignment settings into this save.", DebugFeature.General);
                }
                else
                {
                    BetterWorkTabMod.DebugLog("Ignored legacy global sub-work reassignment settings because this save already has sub-work data.", DebugFeature.General);
                }

                ClearLegacySettingsAfterLoad(settings);
            }

            OnWorldDataLoaded();
        }

        private static void ClearLegacySettingsAfterLoad(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.LegacyWorkGiverReassignments = null;
            LongEventHandler.ExecuteWhenFinished(settings.Write);
        }

        internal static void OnWorldDataLoaded()
        {
            InvalidateCaches();
            _cachedSyncVersion = Data?.SyncVersion ?? 0;
            CleanupOrphanedReassignments();
        }

        private static void EnsureVersion()
        {
            int version = Data?.SyncVersion ?? 0;
            if (version == _cachedSyncVersion)
            {
                return;
            }

            InvalidateCaches();
            _cachedSyncVersion = version;
        }

        internal static WorkTypeDef GetTargetWorkType(WorkGiverDef def)
        {
            if (def == null)
            {
                return null;
            }

            EnsureVersion();

            if (WorkGiverTargetCache.TryGetValue(def.shortHash, out var cached))
            {
                return cached;
            }

            WorkTypeDef target = def.workType;
            var data = Data;

            if (data?.WorkGiverToWorkTypeMap != null &&
                data.WorkGiverToWorkTypeMap.TryGetValue(def.defName, out var targetWorkTypeName) &&
                !string.IsNullOrEmpty(targetWorkTypeName))
            {
                var mapped = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeName);
                if (mapped != null)
                {
                    target = mapped;
                }
            }

            WorkGiverTargetCache[def.shortHash] = target;
            ReassignedCache[def.shortHash] = target != def.workType;
            return target;
        }

        internal static bool IsReassigned(WorkGiverDef def)
        {
            if (def == null)
            {
                return false;
            }

            EnsureVersion();

            if (ReassignedCache.TryGetValue(def.shortHash, out var cached))
            {
                return cached;
            }

            var target = GetTargetWorkType(def);
            bool reassigned = target != null && target != def.workType;
            ReassignedCache[def.shortHash] = reassigned;
            return reassigned;
        }

        internal static List<WorkGiver> GetOrderedWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn = null)
        {
            return GetWorkGiversForWorkType(workType, pawn, applyPrioritySort: true);
        }

        internal static List<WorkGiver> GetDisplayWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn = null)
        {
            return GetWorkGiversForWorkType(workType, pawn, applyPrioritySort: false);
        }

        private static List<WorkGiver> GetWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn, bool applyPrioritySort)
        {
            EnsureVersion();

            if (workType == null)
            {
                return new List<WorkGiver>();
            }

            if (applyPrioritySort && pawn == null && OrderedWorkGiverCache.TryGetValue(workType.defName, out var cached))
            {
                return cached;
            }

            var result = new List<WorkGiver>();
            var data = Data;

            List<string> orderedNames = null;
            if (pawn != null && data?.PawnWorkGiverOrdering != null)
            {
                if (data.PawnWorkGiverOrdering.TryGetValue(pawn.thingIDNumber, out var pawnOrders) && pawnOrders != null)
                {
                    pawnOrders.TryGetValue(workType.defName, out orderedNames);
                }
            }

            if (orderedNames == null && data?.WorkTypeWorkGiverOrder != null)
            {
                data.WorkTypeWorkGiverOrder.TryGetValue(workType.defName, out orderedNames);
            }

            var handled = new HashSet<string>(StringComparer.Ordinal);
            if (orderedNames != null)
            {
                for (int i = 0; i < orderedNames.Count; i++)
                {
                    var name = orderedNames[i];
                    if (handled.Contains(name))
                    {
                        continue;
                    }

                    var def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                    if (def != null && GetTargetWorkType(def) == workType)
                    {
                        var worker = def.Worker;
                        if (worker != null)
                        {
                            result.Add(worker);
                            handled.Add(name);
                        }
                    }
                }
            }

            var remaining = new List<WorkGiverDef>();
            foreach (var def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (GetTargetWorkType(def) != workType)
                {
                    continue;
                }

                if (!handled.Contains(def.defName))
                {
                    remaining.Add(def);
                }
            }

            remaining.Sort((a, b) => b.priorityInType.CompareTo(a.priorityInType));
            for (int i = 0; i < remaining.Count; i++)
            {
                var worker = remaining[i].Worker;
                if (worker != null)
                {
                    result.Add(worker);
                }
            }

            if (applyPrioritySort)
            {
                // Apply sorting based on priorities (pawn-specific or global defaults)
                var sortingPawn = pawn;
                int defaultPrio = pawn == null
                    ? WorkPrioritySystem.GetDefaultEnabledPriority()
                    : WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);

                var indexed = result.Select((g, idx) => new { g, idx }).ToList();
                indexed.Sort((a, b) =>
                {
                    int pa = GetWorkGiverPriority(sortingPawn, a.g.def, defaultPrio);
                    int pb = GetWorkGiverPriority(sortingPawn, b.g.def, defaultPrio);

                    // Treat 0 as disabled (lowest priority)
                    int valA = (pa == 0) ? 999 : pa;
                    int valB = (pb == 0) ? 999 : pb;

                    int c = valA.CompareTo(valB);
                    if (c != 0) return c;

                    // Secondary sort: saved/manual order.
                    c = a.idx.CompareTo(b.idx);
                    if (c != 0) return c;

                    return b.g.def.priorityInType.CompareTo(a.g.def.priorityInType);
                });
                result = indexed.Select(x => x.g).ToList();
            }

            if (applyPrioritySort && pawn == null)
            {
                OrderedWorkGiverCache[workType.defName] = result;
            }

            return result;
        }

        internal static List<Pawn> GetPawnsWithOverrides(WorkTypeDef workType)
        {
            var data = Data;
            if (data == null || workType == null)
            {
                return new List<Pawn>();
            }

            var results = new List<Pawn>();
            var pawnIds = new HashSet<int>();

            // Check priority overrides
            if (data.PawnWorkGiverPriorityOverrides != null)
            {
                var workGiversInType = new HashSet<string>(GetOrderedWorkGiversForWorkType(workType).Select(wg => wg.def.defName));
                foreach (var kv in data.PawnWorkGiverPriorityOverrides)
                {
                    if (kv.Key == -1) continue;
                    if (kv.Value != null && kv.Value.Keys.Any(name => workGiversInType.Contains(name)))
                    {
                        pawnIds.Add(kv.Key);
                    }
                }
            }

            // Check ordering overrides
            if (data.PawnWorkGiverOrdering != null)
            {
                foreach (var kv in data.PawnWorkGiverOrdering)
                {
                    if (kv.Key == -1) continue;
                    if (kv.Value != null && kv.Value.ContainsKey(workType.defName))
                    {
                        pawnIds.Add(kv.Key);
                    }
                }
            }

            foreach (int id in pawnIds)
            {
                var pawn = FindPawnById(id);
                if (pawn != null)
                {
                    results.Add(pawn);
                }
            }

            return results;
        }

        internal static bool HasAnyPawnOverride(WorkTypeDef workType, Pawn pawn)
        {
            var data = Data;
            if (data?.PawnWorkGiverPriorityOverrides == null || workType == null || pawn == null)
            {
                return false;
            }

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawn.thingIDNumber, out var pawnDict) || pawnDict == null)
            {
                return false;
            }

            var wgs = GetOrderedWorkGiversForWorkType(workType);
            foreach (var g in wgs)
            {
                if (pawnDict.ContainsKey(g.def.defName)) return true;
            }

            return false;
        }

        internal static bool HasPawnOrdering(Pawn pawn, WorkTypeDef workType)
        {
            var data = Data;
            if (data?.PawnWorkGiverOrdering == null || pawn == null || workType == null)
            {
                return false;
            }

            return data.PawnWorkGiverOrdering.TryGetValue(pawn.thingIDNumber, out var orders) && 
                   orders != null && 
                   orders.ContainsKey(workType.defName);
        }

        internal static bool HasNonEmergencyWorkGiver(WorkTypeDef workType)
        {
            return GetOrderedWorkGiversForWorkType(workType).Any(w => w?.def != null && !w.def.emergency);
        }

        internal static bool CanPawnUseMappedWorkType(Pawn pawn, WorkGiverDef def)
        {
            if (pawn?.workSettings == null)
            {
                return true;
            }

            var targetWorkType = GetTargetWorkType(def);
            if (targetWorkType == null)
            {
                return true;
            }

            if (pawn.WorkTypeIsDisabled(targetWorkType))
            {
                return false;
            }

            return WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, targetWorkType) > 0;
        }

        internal static int GetWorkGiverPriority(Pawn pawn, WorkGiverDef workGiver, int defaultPriority)
        {
            if (workGiver == null)
            {
                return WorkPrioritySystem.ClampPriority(defaultPriority);
            }

            var data = Data;
            if (data == null) return WorkPrioritySystem.ClampPriority(defaultPriority);

            // 1. Pawn-specific override
            if (pawn != null &&
                data.PawnWorkGiverPriorityOverrides.TryGetValue(pawn.thingIDNumber, out var pawnDict) &&
                pawnDict != null &&
                pawnDict.TryGetValue(workGiver.defName, out int pawnPriority))
            {
                return WorkPrioritySystem.ClampPriority(pawnPriority);
            }
            
            // 2. Global override (Pawn ID -1)
            if (data.PawnWorkGiverPriorityOverrides.TryGetValue(-1, out var globalDict) &&
                globalDict != null &&
                globalDict.TryGetValue(workGiver.defName, out int globalPriority))
            {
                return WorkPrioritySystem.ClampPriority(globalPriority);
            }

            return WorkPrioritySystem.ClampPriority(defaultPriority);
        }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
        public static void SyncSetPawnOverride(int pawnId, string workGiverDefName, int priority)
        {
            var pawn = pawnId == -1 ? null : FindPawnById(pawnId);
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver == null) return;
            
            SetPawnOverride(pawn, workGiver, priority);
        }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
        public static void SyncClearPawnOverridesForWorkType(int pawnId, string workTypeDefName)
        {
            var pawn = FindPawnById(pawnId);
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            ClearPawnOverridesForWorkType(pawn, workType);
        }

        private static void SetPawnOverride(Pawn pawn, WorkGiverDef workGiver, int priority)
        {
            var data = Data;
            if (data == null || workGiver == null) return;
            int pawnId = pawn?.thingIDNumber ?? -1;

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var dict) || dict == null)
            {
                dict = new Dictionary<string, int>(StringComparer.Ordinal);
                data.PawnWorkGiverPriorityOverrides[pawnId] = dict;
            }

            dict[workGiver.defName] = WorkPrioritySystem.ClampPriority(priority);

            data.SyncVersion++;
            InvalidateCaches();
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
        }

        private static void ClearPawnOverridesForWorkType(Pawn pawn, WorkTypeDef workType)
        {
            var data = Data;
            if (data == null || pawn == null || workType == null)
            {
                return;
            }

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawn.thingIDNumber, out var dict) ||
                dict == null ||
                dict.Count == 0)
            {
                return;
            }

            bool changed = false;
            var workGivers = GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                string defName = workGivers[i]?.def?.defName;
                if (!defName.NullOrEmpty() && dict.Remove(defName))
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            if (dict.Count == 0)
            {
                data.PawnWorkGiverPriorityOverrides.Remove(pawn.thingIDNumber);
            }

            data.SyncVersion++;
            InvalidateCaches();
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
        }

        internal static bool TryReassignWorkGiver(string workGiverDefName, string targetWorkTypeDefName, int? insertIndex, out string errorMsg)
        {
            errorMsg = null;
            var workGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiverDef == null)
            {
                errorMsg = $"WorkGiver '{workGiverDefName}' not found";
                return false;
            }

            var targetWorkTypeDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeDefName);
            if (targetWorkTypeDef == null)
            {
                errorMsg = $"WorkType '{targetWorkTypeDefName}' not found";
                return false;
            }

            if (MultiplayerBridge.Active)
            {
                SyncReassignWorkGiver(workGiverDef.defName, targetWorkTypeDef.defName, insertIndex ?? GetOrderedWorkGiversForWorkType(targetWorkTypeDef).Count);
                return true;
            }

            ApplyReassignment(workGiverDef, targetWorkTypeDef, insertIndex);
            BetterWorkTabMod.DebugLog($"Reassigned {workGiverDef.defName} -> {targetWorkTypeDef.defName}", DebugFeature.General);
            return true;
        }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
        public static void SyncSetPawnWorkGiverOrder(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            SetPawnWorkGiverOrder(pawnId, workTypeDefName, orderedWorkGiverNames);
        }

        private static void SetPawnWorkGiverOrder(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            var data = Data;
            if (data == null) return;

            if (pawnId == -1)
            {
                // Global order
                data.WorkTypeWorkGiverOrder[workTypeDefName] = orderedWorkGiverNames;
            }
            else
            {
                // Pawn-specific order
                if (!data.PawnWorkGiverOrdering.TryGetValue(pawnId, out var pawnOrders) || pawnOrders == null)
                {
                    pawnOrders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    data.PawnWorkGiverOrdering[pawnId] = pawnOrders;
                }
                pawnOrders[workTypeDefName] = orderedWorkGiverNames;
            }

            data.SyncVersion++;
            InvalidateCaches();
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
        }

        internal static void MoveWithinWorkType(string workTypeDefName, string workGiverDefName, int newIndex, Pawn pawn = null)
        {
            var currentOrder = GetDisplayWorkGiversForWorkType(DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName), pawn)
                .Select(wg => wg.def.defName)
                .ToList();

            currentOrder.Remove(workGiverDefName);
            newIndex = Math.Max(0, Math.Min(newIndex, currentOrder.Count));
            currentOrder.Insert(newIndex, workGiverDefName);

            int pawnId = pawn?.thingIDNumber ?? -1;
            if (MultiplayerBridge.Active)
            {
                SyncSetPawnWorkGiverOrder(pawnId, workTypeDefName, currentOrder);
                return;
            }

            SetPawnWorkGiverOrder(pawnId, workTypeDefName, currentOrder);
        }

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
        internal static void SyncReassignWorkGiver(string workGiverDefName, string targetWorkTypeDefName, int insertIndex)
        {
            var wg = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeDefName);
            if (wg == null || wt == null)
            {
                return;
            }

            ApplyReassignment(wg, wt, insertIndex);
        }

        /// <summary>
        /// Mutate mapping + ordering and invalidate caches.
        /// </summary>
        private static void ApplyReassignment(WorkGiverDef workGiverDef, WorkTypeDef targetWorkTypeDef, int? insertIndex = null)
        {
            var data = Data;
            if (data == null)
            {
                return;
            }

            data.WorkGiverToWorkTypeMap[workGiverDef.defName] = targetWorkTypeDef.defName;

            if (data.WorkTypeWorkGiverOrder != null)
            {
                foreach (var kv in data.WorkTypeWorkGiverOrder)
                {
                    kv.Value?.Remove(workGiverDef.defName);
                }
            }
            else
            {
                data.WorkTypeWorkGiverOrder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            if (!data.WorkTypeWorkGiverOrder.TryGetValue(targetWorkTypeDef.defName, out var targetList) || targetList == null)
            {
                targetList = new List<string>();
                data.WorkTypeWorkGiverOrder[targetWorkTypeDef.defName] = targetList;
            }

            int index = insertIndex.HasValue ? Math.Max(0, Math.Min(insertIndex.Value, targetList.Count)) : targetList.Count;
            targetList.Insert(index, workGiverDef.defName);

            data.SyncVersion++;
            InvalidateCaches();
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
        }

        internal static void CleanupOrphanedReassignments()
        {
            var data = Data;
            if (data?.WorkGiverToWorkTypeMap == null)
            {
                return;
            }

            var orphaned = new List<string>();
            foreach (var wgName in data.WorkGiverToWorkTypeMap.Keys.ToList())
            {
                if (DefDatabase<WorkGiverDef>.GetNamedSilentFail(wgName) == null)
                {
                    orphaned.Add(wgName);
                }
            }

            foreach (var wgName in orphaned)
            {
                data.WorkGiverToWorkTypeMap.Remove(wgName);
                foreach (var list in data.WorkTypeWorkGiverOrder.Values)
                {
                    list?.Remove(wgName);
                }

                foreach (var pawnDict in data.PawnWorkGiverPriorityOverrides.Values)
                {
                    pawnDict?.Remove(wgName);
                }

                foreach (var pawnDict in data.PawnWorkGiverOrdering.Values)
                {
                    pawnDict?.Remove(wgName);
                }
            }

            if (orphaned.Count > 0)
            {
                InvalidateCaches();
                BetterWorkTabMod.DebugLog($"Cleaned up {orphaned.Count} orphaned WorkGiver reassignments", DebugFeature.General);
            }
        }

        private static Pawn FindPawnById(int pawnId)
        {
            return AllKnownPawns().FirstOrDefault(p => p != null && p.thingIDNumber == pawnId);
        }

        private static IEnumerable<Pawn> AllKnownPawns()
        {
#if v0_18 || v0_17 || v0_16 || v0_15 || v0_14 || v0_13 || vAlpha4
            return PawnsFinderCompat.AllMapsWorldAndTemporaryAliveOrDead;
#else
            return PawnsFinder.All_AliveOrDead;
#endif
        }
    }
}
