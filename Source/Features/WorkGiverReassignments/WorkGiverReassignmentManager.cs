using Better_Work_Tab;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.ModSupport;
using Multiplayer.API;
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
    internal static partial class WorkGiverReassignmentManager
    {
        private static readonly Dictionary<int, WorkTypeDef> WorkGiverTargetCache = new Dictionary<int, WorkTypeDef>();
        private static readonly Dictionary<string, List<WorkGiver>> OrderedWorkGiverCache = new Dictionary<string, List<WorkGiver>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<WorkGiver>> DisplayWorkGiverCache = new Dictionary<string, List<WorkGiver>>(StringComparer.Ordinal);
        private static int _mutationBatchDepth;
        private static bool _mutationBatchChanged;

        private static int _cachedSyncVersion = -1;
        private static int _cachedActivationSyncVersion = int.MinValue;
        private static GameComponent_BWTWorldSettings _cachedActivationComponent;
        private static bool _cachedHasAnyData;
        private static BetterWorkTabSettings Settings => BetterWorkTabMod.Settings;

        private static WorkGiverReassignmentData ExistingData
        {
            get
            {
                var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                return component?.WorkGiverReassignments ?? Settings?.LegacyWorkGiverReassignments;
            }
        }

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

        internal static int CurrentSyncVersion => ExistingData?.SyncVersion ?? 0;

        internal static bool HasActiveData
        {
            get
            {
                WorkGiverReassignmentData data = ExistingData;
                GameComponent_BWTWorldSettings component =
                    Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                int version = data?.SyncVersion ?? 0;
                if (!ReferenceEquals(component, _cachedActivationComponent) ||
                    version != _cachedActivationSyncVersion)
                {
                    _cachedActivationComponent = component;
                    _cachedActivationSyncVersion = version;
                    _cachedHasAnyData = data != null && data.HasAnyData();
                }

                return _cachedHasAnyData;
            }
        }

        internal static void SetPawnOverrideSynced(int pawnId, string workGiverDefName, int priority)
        {
            if (MultiplayerBridge.Active)
            {
                SyncSetPawnOverride(pawnId, workGiverDefName, priority);
                return;
            }

            ApplyPawnOverride(pawnId, workGiverDefName, priority);
        }

        internal static void ClearPawnOverrideSynced(int pawnId, string workGiverDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncClearPawnOverride(pawnId, workGiverDefName);
                return;
            }

            ApplyClearPawnOverride(pawnId, workGiverDefName);
        }

        internal static void SetPawnOverridesBatchSynced(string workGiverDefName, List<int> pawnIds, List<int> priorities)
        {
            if (MultiplayerBridge.Active)
            {
                SyncSetPawnOverridesBatch(workGiverDefName, pawnIds, priorities);
                return;
            }

            ApplyPawnOverridesBatch(workGiverDefName, pawnIds, priorities);
        }

        internal static void ClearPawnOverridesForWorkTypeSynced(int pawnId, string workTypeDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncClearPawnOverridesForWorkType(pawnId, workTypeDefName);
                return;
            }

            ApplyClearPawnOverridesForWorkType(pawnId, workTypeDefName);
        }

        internal static void EnableParentWorkTypeSynced(int pawnId, string workTypeDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncEnableParentWorkType(pawnId, workTypeDefName);
                return;
            }

            ApplyEnableParentWorkType(pawnId, workTypeDefName);
        }

        internal static void EnableParentAndClearSubOverridesSynced(int pawnId, string workTypeDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncEnableParentAndClearSubOverrides(pawnId, workTypeDefName);
                return;
            }

            ApplyEnableParentAndClearSubOverrides(pawnId, workTypeDefName);
        }

        internal static void EnableParentAndSetOnlySubOverrideSynced(int pawnId, string workTypeDefName, string workGiverDefName, int priority)
        {
            if (MultiplayerBridge.Active)
            {
                SyncEnableParentAndSetOnlySubOverride(pawnId, workTypeDefName, workGiverDefName, priority);
                return;
            }

            ApplyEnableParentAndSetOnlySubOverride(pawnId, workTypeDefName, workGiverDefName, priority);
        }

        internal static bool SetPawnWorkGiverOrderSynced(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            if (MultiplayerBridge.Active)
            {
                SyncSetPawnWorkGiverOrder(pawnId, workTypeDefName, orderedWorkGiverNames);
                return true;
            }

            return SetPawnWorkGiverOrder(pawnId, workTypeDefName, orderedWorkGiverNames);
        }

        internal static void MoveWithinWorkTypeSynced(string workTypeDefName, string workGiverDefName, int newIndex, Pawn pawn = null)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId == -1)
            {
                TryMoveWorkGiverLayout(workGiverDefName, workTypeDefName, newIndex, out _);
                return;
            }

            if (MultiplayerBridge.Active)
            {
                SyncMoveWithinWorkType(workTypeDefName, workGiverDefName, newIndex, pawnId);
                return;
            }

            ApplyMoveWithinWorkType(workTypeDefName, workGiverDefName, newIndex, pawnId);
        }

        /// <summary>
        /// Clear caches when the sync version changes or the settings are reloaded.
        /// </summary>
        internal static void InvalidateCaches()
        {
            WorkGiverTargetCache.Clear();
            OrderedWorkGiverCache.Clear();
            DisplayWorkGiverCache.Clear();
            _cachedActivationSyncVersion = int.MinValue;
            _cachedActivationComponent = null;
        }

        internal static IDisposable BeginMutationBatch()
        {
            _mutationBatchDepth++;
            return new MutationBatchScope();
        }

        internal static bool CommitMutationBatch()
        {
            if (_mutationBatchDepth != 0 || !_mutationBatchChanged)
            {
                return false;
            }

            _mutationBatchChanged = false;
            InvalidateCaches();
            return true;
        }

        internal static void OnSettingsLoaded()
        {
            InvalidateCaches();
            WorkGiverLayoutHistory.Clear();
            _cachedSyncVersion = ExistingData?.SyncVersion ?? 0;
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
            _cachedSyncVersion = ExistingData?.SyncVersion ?? 0;
            CleanupOrphanedReassignments();
        }

        private static void EnsureVersion()
        {
            int version = ExistingData?.SyncVersion ?? 0;
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
            var data = ExistingData;

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
            return target;
        }

        internal static bool IsReassigned(WorkGiverDef def)
        {
            if (def == null)
            {
                return false;
            }

            EnsureVersion();

            var target = GetTargetWorkType(def);
            return target != null && target != def.workType;
        }

        internal static IReadOnlyList<WorkGiver> GetOrderedWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn = null)
        {
            return GetWorkGiversForWorkType(workType, pawn, applyPrioritySort: true);
        }

        internal static IReadOnlyList<WorkGiver> GetDisplayWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn = null)
        {
            return GetWorkGiversForWorkType(workType, pawn, applyPrioritySort: false);
        }

        private static IReadOnlyList<WorkGiver> GetWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn, bool applyPrioritySort)
        {
            EnsureVersion();

            if (workType == null)
            {
                return Array.Empty<WorkGiver>();
            }

            if (applyPrioritySort && pawn == null && OrderedWorkGiverCache.TryGetValue(workType.defName, out var cached))
            {
                return cached;
            }

            if (!applyPrioritySort && pawn == null && DisplayWorkGiverCache.TryGetValue(workType.defName, out cached))
            {
                return cached;
            }

            var result = new List<WorkGiver>();
            var data = ExistingData;

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
                    : WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);

                var indexed = result.Select((g, idx) => new { g, idx }).ToList();
                indexed.Sort((a, b) =>
                {
                    int pa = GetWorkGiverPriority(sortingPawn, a.g.def, defaultPrio);
                    int pb = GetWorkGiverPriority(sortingPawn, b.g.def, defaultPrio);
                    if (sortingPawn != null)
                    {
                        pa = TimePriorityService.GetEffectiveWorkGiverPriority(sortingPawn, workType, a.g.def, pa);
                        pb = TimePriorityService.GetEffectiveWorkGiverPriority(sortingPawn, workType, b.g.def, pb);
                    }

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

            if (pawn == null)
            {
                if (applyPrioritySort)
                {
                    OrderedWorkGiverCache[workType.defName] = result;
                }
                else
                {
                    DisplayWorkGiverCache[workType.defName] = result;
                }
            }

            return result;
        }

        internal static List<Pawn> GetPawnsWithOverrides(WorkTypeDef workType)
        {
            var data = ExistingData;
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
                var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == id);
                if (pawn != null)
                {
                    results.Add(pawn);
                }
            }

            return results;
        }

        internal static int CountPawnPriorityOverrides(WorkTypeDef workType)
        {
            var data = ExistingData;
            if (data?.PawnWorkGiverPriorityOverrides == null || workType == null)
            {
                return 0;
            }

            var workGiverNames = new HashSet<string>(
                GetOrderedWorkGiversForWorkType(workType).Select(wg => wg.def.defName),
                StringComparer.Ordinal);

            int count = 0;
            foreach (var pawnEntry in data.PawnWorkGiverPriorityOverrides)
            {
                if (pawnEntry.Key == -1 || pawnEntry.Value == null)
                {
                    continue;
                }

                foreach (string workGiverName in pawnEntry.Value.Keys)
                {
                    if (workGiverNames.Contains(workGiverName))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        internal static bool HasAnyPawnOverride(WorkTypeDef workType, Pawn pawn)
        {
            var data = ExistingData;
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
            var data = ExistingData;
            if (data?.PawnWorkGiverOrdering == null || pawn == null || workType == null)
            {
                return false;
            }

            return data.PawnWorkGiverOrdering.TryGetValue(pawn.thingIDNumber, out var orders) && 
                   orders != null && 
                   orders.ContainsKey(workType.defName);
        }

        internal static bool ShouldShowMovedWorkGiverMarker(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            return (GetTargetWorkType(workGiverDef) == workType && IsReassigned(workGiverDef)) ||
                   WasWorkGiverDraggedByPlayer(workType, workGiverDef) &&
                   IsWorkGiverOutOfBaselinePosition(workType, workGiverDef);
        }

        internal static bool WasWorkGiverDraggedByPlayer(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            var data = ExistingData;
            if (data?.PlayerMovedWorkGiversByWorkType == null ||
                workType?.defName == null ||
                workGiverDef?.defName == null)
            {
                return false;
            }

            return data.PlayerMovedWorkGiversByWorkType.TryGetValue(workType.defName, out var moved) &&
                   moved != null &&
                   moved.Contains(workGiverDef.defName);
        }

        internal static bool IsWorkGiverOutOfBaselinePosition(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            if (workType == null || workGiverDef == null)
            {
                return false;
            }

            var current = GetDisplayWorkGiversForWorkType(workType)
                .Where(wg => wg?.def != null)
                .Select(wg => wg.def.defName)
                .ToList();

            int currentIndex = current.IndexOf(workGiverDef.defName);
            if (currentIndex < 0)
            {
                return false;
            }

            int baselineIndex = GetBaselineWorkGiverOrder(workType).IndexOf(workGiverDef.defName);
            return baselineIndex >= 0 && baselineIndex != currentIndex;
        }

        internal static int CalculateBaselineTargetIndex(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            if (workType == null || workGiverDef == null)
            {
                return -1;
            }

            var baseline = GetBaselineWorkGiverOrder(workType);
            int baselineIndex = baseline.IndexOf(workGiverDef.defName);
            if (baselineIndex < 0)
            {
                return -1;
            }

            var current = GetDisplayWorkGiversForWorkType(workType);
            int targetIndex = 0;
            for (int i = 0; i < current.Count; i++)
            {
                var defName = current[i]?.def?.defName;
                if (defName.NullOrEmpty() || defName == workGiverDef.defName)
                {
                    continue;
                }

                int otherBaselineIndex = baseline.IndexOf(defName);
                if (otherBaselineIndex >= 0 && otherBaselineIndex < baselineIndex)
                {
                    targetIndex++;
                }
            }

            return targetIndex;
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

            return WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, targetWorkType) > 0;
        }

        internal static bool TryGetPawnWorkGiverOverride(Pawn pawn, WorkGiverDef workGiver, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            var data = ExistingData;
            if (data?.PawnWorkGiverPriorityOverrides == null || pawn == null || workGiver?.defName == null)
            {
                return false;
            }

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawn.thingIDNumber, out var pawnDict) ||
                pawnDict == null ||
                !pawnDict.TryGetValue(workGiver.defName, out priority))
            {
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            return true;
        }

        internal static bool HasPawnWorkGiverOverride(Pawn pawn, WorkGiverDef workGiver)
        {
            return TryGetPawnWorkGiverOverride(pawn, workGiver, out _);
        }

        internal static bool HasEnabledPawnOverrideForWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            var workGivers = GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef def = workGivers[i]?.def;
                if (TryGetPawnWorkGiverOverride(pawn, def, out int priority) &&
                    priority > WorkPrioritySystem.DisabledPriority)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool LockedSubWorkOverridesDisabledParent()
        {
            return !MultiplayerBridge.Active &&
                   BetterWorkTabMod.Settings?.subWorkDisabledParentMode == BetterWorkTabSettings.SubWorkDisabledParentMode.LockedSubWorkOverridesParent;
        }

        internal static int GetExecutionPriorityForWorkType(Pawn pawn, WorkTypeDef workType, int parentPriority)
        {
            parentPriority = WorkPrioritySystem.ClampPriority(parentPriority);
            if (TimePriorityService.IsWorkTypeDisabledBySchedule(pawn, workType))
            {
                return WorkPrioritySystem.DisabledPriority;
            }

            if (parentPriority > WorkPrioritySystem.DisabledPriority ||
                !LockedSubWorkOverridesDisabledParent() ||
                !TryGetHighestEnabledPawnOverridePriorityForWorkType(pawn, workType, out int lockedPriority))
            {
                return parentPriority;
            }

            return lockedPriority;
        }

        internal static bool LockedPawnOverrideCanRunWhenParentDisabled(Pawn pawn, WorkGiverDef workGiver, WorkTypeDef workType)
        {
            return LockedSubWorkOverridesDisabledParent() &&
                   !TimePriorityService.IsWorkTypeDisabledBySchedule(pawn, workType) &&
                   TryGetPawnWorkGiverOverride(pawn, workGiver, out int priority) &&
                   priority > WorkPrioritySystem.DisabledPriority &&
                   workType != null &&
                   GetTargetWorkType(workGiver) == workType;
        }

        private static bool TryGetHighestEnabledPawnOverridePriorityForWorkType(Pawn pawn, WorkTypeDef workType, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            if (pawn == null || workType == null)
            {
                return false;
            }

            bool found = false;
            var workGivers = GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef def = workGivers[i]?.def;
                if (!TryGetPawnWorkGiverOverride(pawn, def, out int overridePriority) ||
                    overridePriority <= WorkPrioritySystem.DisabledPriority)
                {
                    continue;
                }

                priority = found
                    ? Math.Min(priority, overridePriority)
                    : overridePriority;
                found = true;
            }

            return found;
        }

        internal static int GetWorkGiverPriority(Pawn pawn, WorkGiverDef workGiver, int defaultPriority)
        {
            if (workGiver == null)
            {
                return WorkPrioritySystem.ClampPriority(defaultPriority);
            }

            var data = ExistingData;
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

        internal static int GetInheritedWorkGiverPriority(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            int defaultPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            if (workGiver == null)
            {
                return WorkPrioritySystem.ClampPriority(defaultPriority);
            }

            var data = ExistingData;
            if (data?.PawnWorkGiverPriorityOverrides != null &&
                data.PawnWorkGiverPriorityOverrides.TryGetValue(-1, out var globalDict) &&
                globalDict != null &&
                globalDict.TryGetValue(workGiver.defName, out int globalPriority))
            {
                return TimePriorityService.GetEffectiveWorkGiverPriority(
                    null,
                    workType,
                    workGiver,
                    globalPriority);
            }

            return WorkPrioritySystem.ClampPriority(defaultPriority);
        }

        [SyncMethod]
        public static void SyncSetPawnOverride(int pawnId, string workGiverDefName, int priority)
        {
            ApplyPawnOverride(pawnId, workGiverDefName, priority);
        }

        [SyncMethod]
        public static void SyncClearPawnOverride(int pawnId, string workGiverDefName)
        {
            ApplyClearPawnOverride(pawnId, workGiverDefName);
        }

        [SyncMethod]
        public static void SyncSetPawnOverridesBatch(string workGiverDefName, List<int> pawnIds, List<int> priorities)
        {
            ApplyPawnOverridesBatch(workGiverDefName, pawnIds, priorities);
        }

        [SyncMethod]
        public static void SyncClearPawnOverridesForWorkType(int pawnId, string workTypeDefName)
        {
            ApplyClearPawnOverridesForWorkType(pawnId, workTypeDefName);
        }

        [SyncMethod]
        public static void SyncEnableParentWorkType(int pawnId, string workTypeDefName)
        {
            ApplyEnableParentWorkType(pawnId, workTypeDefName);
        }

        [SyncMethod]
        public static void SyncEnableParentAndClearSubOverrides(int pawnId, string workTypeDefName)
        {
            ApplyEnableParentAndClearSubOverrides(pawnId, workTypeDefName);
        }

        [SyncMethod]
        public static void SyncEnableParentAndSetOnlySubOverride(int pawnId, string workTypeDefName, string workGiverDefName, int priority)
        {
            ApplyEnableParentAndSetOnlySubOverride(pawnId, workTypeDefName, workGiverDefName, priority);
        }

        private static void ApplyPawnOverride(int pawnId, string workGiverDefName, int priority)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver == null)
            {
                return;
            }

            SetPawnOverride(pawnId, workGiver, priority, notify: true);
        }

        private static void ApplyClearPawnOverride(int pawnId, string workGiverDefName)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver == null)
            {
                return;
            }

            ClearPawnOverride(pawnId, workGiver, notify: true);
        }

        private static void ApplyPawnOverridesBatch(string workGiverDefName, List<int> pawnIds, List<int> priorities)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver == null || pawnIds == null || priorities == null)
            {
                return;
            }

            int count = Math.Min(pawnIds.Count, priorities.Count);
            if (count <= 0)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                changed |= SetPawnOverride(pawnIds[i], workGiver, priorities[i], notify: false);
            }

            if (changed)
            {
                NotifySubWorkDataChanged();
            }
        }

        private static bool SetPawnOverride(int pawnId, WorkGiverDef workGiver, int priority, bool notify)
        {
            var data = Data;
            if (data == null || workGiver == null)
            {
                return false;
            }

            data.EnsureCollections();
            priority = WorkPrioritySystem.ClampPriority(priority);

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var dict) || dict == null)
            {
                dict = new Dictionary<string, int>(StringComparer.Ordinal);
                data.PawnWorkGiverPriorityOverrides[pawnId] = dict;
            }

            if (dict.TryGetValue(workGiver.defName, out int currentPriority) && currentPriority == priority)
            {
                return false;
            }

            dict[workGiver.defName] = priority;

            data.SyncVersion++;
            MirrorWorkGiverToExternalWorkTab(pawnId, workGiver);
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        private static bool ClearPawnOverride(int pawnId, WorkGiverDef workGiver, bool notify)
        {
            var data = Data;
            if (data == null || workGiver?.defName == null)
            {
                return false;
            }

            data.EnsureCollections();
            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var dict) ||
                dict == null ||
                !dict.Remove(workGiver.defName))
            {
                return false;
            }

            if (dict.Count == 0)
            {
                data.PawnWorkGiverPriorityOverrides.Remove(pawnId);
            }

            data.SyncVersion++;
            MirrorWorkGiverToExternalWorkTab(pawnId, workGiver);
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        /// <summary>
        /// Republishes one pawn's work-giver priority to any external work-tab mod backing the numbers.
        /// Work-giver overrides live only in Better Work Tab, so nothing else propagates them.
        /// </summary>
        private static void MirrorWorkGiverToExternalWorkTab(int pawnId, WorkGiverDef workGiver)
        {
            if (ExternalPriorityMirror.IsSuspended || workGiver == null || pawnId < 0)
            {
                return;
            }

            Pawn pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            if (pawn != null)
            {
                ExternalPriorityMirror.NotifyWorkGiverChanged(pawn, workGiver);
            }
        }

        private static void ApplyClearPawnOverridesForWorkType(int pawnId, string workTypeDefName)
        {
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            ClearPawnOverridesForWorkType(pawnId, workType, notify: true);
        }

        private static void ApplyEnableParentWorkType(int pawnId, string workTypeDefName)
        {
            var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (pawn?.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (parentPriority > WorkPrioritySystem.DisabledPriority)
            {
                return;
            }

            WorkPrioritySystem.SetPriority(pawn.workSettings, workType, WorkPrioritySystem.GetDefaultEnabledPriority());
            NotifySubWorkDataChanged();
        }

        private static void ApplyEnableParentAndClearSubOverrides(int pawnId, string workTypeDefName)
        {
            var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (pawn?.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            bool changed = false;
            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (parentPriority <= WorkPrioritySystem.DisabledPriority)
            {
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, WorkPrioritySystem.GetDefaultEnabledPriority());
                changed = true;
            }

            changed |= ClearPawnOverridesForWorkType(pawnId, workType, notify: false);
            if (changed)
            {
                NotifySubWorkDataChanged();
            }
        }

        private static void ApplyEnableParentAndSetOnlySubOverride(int pawnId, string workTypeDefName, string workGiverDefName, int priority)
        {
            var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (pawn?.workSettings == null || workType == null || workGiver == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            if (GetTargetWorkType(workGiver) != workType)
            {
                return;
            }

            bool changed = false;
            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (parentPriority <= WorkPrioritySystem.DisabledPriority)
            {
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, WorkPrioritySystem.GetDefaultEnabledPriority());
                changed = true;
            }

            changed |= SetClickedPawnOverrideForWorkType(pawnId, workType, workGiver, priority);
            if (changed)
            {
                NotifySubWorkDataChanged();
            }
        }

        private static bool SetClickedPawnOverrideForWorkType(int pawnId, WorkTypeDef workType, WorkGiverDef enabledWorkGiver, int priority)
        {
            var data = Data;
            if (data == null || workType == null || enabledWorkGiver == null)
            {
                return false;
            }

            data.EnsureCollections();
            priority = WorkPrioritySystem.ClampPriority(priority);
            if (priority <= WorkPrioritySystem.DisabledPriority)
            {
                return false;
            }

            // Enabling a pawn from an unassigned parent work type should only lock the
            // sub-job the player clicked. Other sub-jobs stay inherited/blank so they do
            // not all show gold override rings.
            return SetPawnOverride(pawnId, enabledWorkGiver, priority, notify: false);
        }

        private static bool ClearPawnOverridesForWorkType(int pawnId, WorkTypeDef workType, bool notify)
        {
            var data = Data;
            if (data == null || workType == null)
            {
                return false;
            }

            data.EnsureCollections();
            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var dict) ||
                dict == null ||
                dict.Count == 0)
            {
                return false;
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
                return false;
            }

            if (dict.Count == 0)
            {
                data.PawnWorkGiverPriorityOverrides.Remove(pawnId);
            }

            data.SyncVersion++;
            MirrorWorkTypeToExternalWorkTab(pawnId, workType);
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        /// <summary>
        /// Republishes a whole work type for one pawn, used when its overrides were cleared en masse.
        /// </summary>
        private static void MirrorWorkTypeToExternalWorkTab(int pawnId, WorkTypeDef workType)
        {
            if (ExternalPriorityMirror.IsSuspended || workType == null || pawnId < 0)
            {
                return;
            }

            Pawn pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            if (pawn != null)
            {
                ExternalPriorityMirror.NotifyWorkTypeChanged(pawn, workType);
            }
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

            return TryMoveWorkGiverLayout(
                workGiverDef.defName,
                targetWorkTypeDef.defName,
                insertIndex ?? GetOrderedWorkGiversForWorkType(targetWorkTypeDef).Count,
                out errorMsg);
        }

        [SyncMethod]
        public static void SyncSetPawnWorkGiverOrder(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            SetPawnWorkGiverOrder(pawnId, workTypeDefName, orderedWorkGiverNames);
        }

        private static bool SetPawnWorkGiverOrder(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames, bool notify = true)
        {
            var data = Data;
            if (data == null) return false;
            data.EnsureCollections();

            orderedWorkGiverNames = NormalizeWorkGiverOrder(workTypeDefName, orderedWorkGiverNames);
            if (orderedWorkGiverNames.Count == 0)
            {
                return false;
            }

            if (pawnId == -1)
            {
                // Global order
                if (data.WorkTypeWorkGiverOrder.TryGetValue(workTypeDefName, out var existing) &&
                    existing != null &&
                    existing.SequenceEqual(orderedWorkGiverNames))
                {
                    return false;
                }

                data.WorkTypeWorkGiverOrder[workTypeDefName] = new List<string>(orderedWorkGiverNames);
            }
            else
            {
                // Pawn-specific order
                if (!data.PawnWorkGiverOrdering.TryGetValue(pawnId, out var pawnOrders) || pawnOrders == null)
                {
                    pawnOrders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    data.PawnWorkGiverOrdering[pawnId] = pawnOrders;
                }

                if (pawnOrders.TryGetValue(workTypeDefName, out var existing) &&
                    existing != null &&
                    existing.SequenceEqual(orderedWorkGiverNames))
                {
                    return false;
                }

                pawnOrders[workTypeDefName] = new List<string>(orderedWorkGiverNames);
            }

            data.SyncVersion++;
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        internal static void MoveWithinWorkType(string workTypeDefName, string workGiverDefName, int newIndex, Pawn pawn = null)
        {
            MoveWithinWorkTypeSynced(workTypeDefName, workGiverDefName, newIndex, pawn);
        }

        [SyncMethod]
        public static void SyncMoveWithinWorkType(string workTypeDefName, string workGiverDefName, int newIndex, int pawnId)
        {
            ApplyMoveWithinWorkType(workTypeDefName, workGiverDefName, newIndex, pawnId);
        }

        private static void ApplyMoveWithinWorkType(string workTypeDefName, string workGiverDefName, int newIndex, int pawnId)
        {
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null || workGiverDefName.NullOrEmpty())
            {
                return;
            }

            Pawn pawn = pawnId >= 0
                ? PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId)
                : null;

            var currentOrder = GetDisplayWorkGiversForWorkType(workType, pawn)
                .Where(wg => wg?.def != null)
                .Select(wg => wg.def.defName)
                .ToList();

            if (!currentOrder.Remove(workGiverDefName))
            {
                return;
            }

            newIndex = Math.Max(0, Math.Min(newIndex, currentOrder.Count));
            currentOrder.Insert(newIndex, workGiverDefName);

            bool changed = SetPawnWorkGiverOrder(pawnId, workTypeDefName, currentOrder, notify: false);
            if (!changed)
            {
                return;
            }

            if (pawnId == -1)
            {
                var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
                RecordPlayerMovedWorkGiver(workType, workGiver);
                PruneBaselineAlignedMovedWorkGivers(workType);
            }

            NotifySubWorkDataChanged();
        }

        [SyncMethod]
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

            data.EnsureCollections();
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
            RecordPlayerMovedWorkGiver(targetWorkTypeDef, workGiverDef);
            PruneBaselineAlignedMovedWorkGivers(targetWorkTypeDef);
            RemoveWorkGiverFromPawnOrders(data, workGiverDef.defName);

            data.SyncVersion++;
            NotifySubWorkDataChanged();
        }

        private static List<string> GetBaselineWorkGiverOrder(WorkTypeDef workType)
        {
            if (workType == null)
            {
                return new List<string>();
            }

            return DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(def => GetTargetWorkType(def) == workType)
                .OrderByDescending(def => def.priorityInType)
                .Select(def => def.defName)
                .ToList();
        }

        private static void RecordPlayerMovedWorkGiver(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            var data = Data;
            if (data == null || workType?.defName == null || workGiverDef?.defName == null)
            {
                return;
            }

            data.EnsureCollections();
            if (!data.PlayerMovedWorkGiversByWorkType.TryGetValue(workType.defName, out var moved) || moved == null)
            {
                moved = new List<string>();
                data.PlayerMovedWorkGiversByWorkType[workType.defName] = moved;
            }

            if (!moved.Contains(workGiverDef.defName))
            {
                moved.Add(workGiverDef.defName);
            }
        }

        private static void PruneBaselineAlignedMovedWorkGivers(WorkTypeDef workType)
        {
            var data = Data;
            if (data?.PlayerMovedWorkGiversByWorkType == null || workType?.defName == null)
            {
                return;
            }

            if (!data.PlayerMovedWorkGiversByWorkType.TryGetValue(workType.defName, out var moved) || moved == null)
            {
                return;
            }

            for (int i = moved.Count - 1; i >= 0; i--)
            {
                var def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(moved[i]);
                if (def == null || !IsWorkGiverOutOfBaselinePosition(workType, def))
                {
                    moved.RemoveAt(i);
                }
            }

            if (moved.Count == 0)
            {
                data.PlayerMovedWorkGiversByWorkType.Remove(workType.defName);
            }
        }

        private static List<string> NormalizeWorkGiverOrder(string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null)
            {
                return new List<string>();
            }

            var valid = GetDisplayWorkGiversForWorkType(workType)
                .Where(wg => wg?.def != null)
                .Select(wg => wg.def.defName)
                .ToList();
            var validSet = new HashSet<string>(valid, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(valid.Count);

            if (orderedWorkGiverNames != null)
            {
                for (int i = 0; i < orderedWorkGiverNames.Count; i++)
                {
                    string name = orderedWorkGiverNames[i];
                    if (!name.NullOrEmpty() && validSet.Contains(name) && seen.Add(name))
                    {
                        result.Add(name);
                    }
                }
            }

            for (int i = 0; i < valid.Count; i++)
            {
                if (seen.Add(valid[i]))
                {
                    result.Add(valid[i]);
                }
            }

            return result;
        }

        private static void RemoveWorkGiverFromPawnOrders(WorkGiverReassignmentData data, string workGiverDefName)
        {
            if (data?.PawnWorkGiverOrdering == null || workGiverDefName.NullOrEmpty())
            {
                return;
            }

            foreach (var pawnOrders in data.PawnWorkGiverOrdering.Values)
            {
                if (pawnOrders == null)
                {
                    continue;
                }

                foreach (var order in pawnOrders.Values)
                {
                    order?.Remove(workGiverDefName);
                }
            }
        }

        private static void NotifySubWorkDataChanged()
        {
            InvalidateCaches();
            if (_mutationBatchDepth > 0)
            {
                _mutationBatchChanged = true;
                return;
            }

            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.SubWorkOverride |
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.Columns |
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        private static void EndMutationBatch()
        {
            if (_mutationBatchDepth > 0)
            {
                _mutationBatchDepth--;
            }
        }

        private sealed class MutationBatchScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                EndMutationBatch();
            }
        }

        internal static int ComputePresentationAuditSignature()
        {
            unchecked
            {
                int hash = 17;
                WorkGiverReassignmentData data = ExistingData;
                if (data == null)
                {
                    return hash;
                }

                if (data.WorkGiverToWorkTypeMap != null)
                {
                    foreach (var entry in data.WorkGiverToWorkTypeMap)
                    {
                        hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(entry.Key ?? string.Empty);
                        hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(entry.Value ?? string.Empty);
                    }
                }

                if (data.PawnWorkGiverPriorityOverrides != null)
                {
                    foreach (var pawnEntry in data.PawnWorkGiverPriorityOverrides)
                    {
                        hash = (hash * 397) ^ pawnEntry.Key;
                        if (pawnEntry.Value == null) continue;
                        foreach (var priorityEntry in pawnEntry.Value)
                        {
                            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(priorityEntry.Key ?? string.Empty);
                            hash = (hash * 397) ^ priorityEntry.Value;
                        }
                    }
                }

                return hash;
            }
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
    }
}
