using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    internal static class TimePriorityService
    {
        internal const int HoursPerDay = 24;
        private static readonly Dictionary<TimePriorityCacheKey, TimePriorityScheduleData> Cache =
            new Dictionary<TimePriorityCacheKey, TimePriorityScheduleData>();
        private static int _cachedVersion = -1;
        private static int _materialActivityGeneration;
        private static int _materialActivityBuiltGeneration = -1;
        private static bool _materialActivityActive;
        private static bool _savedSchedulePresent;
        private static readonly HashSet<TimePriorityCacheKey> MaterialPawnWorkTypeTargets =
            new HashSet<TimePriorityCacheKey>();
        private static readonly HashSet<TimePriorityCacheKey> MaterialGlobalWorkGiverTargets =
            new HashSet<TimePriorityCacheKey>();
        private static readonly HashSet<TimePriorityCacheKey> MaterialPawnWorkGiverTargets =
            new HashSet<TimePriorityCacheKey>();
        private static readonly List<TimePriorityCacheKey> GlobalWorkGiverTransitionTargets =
            new List<TimePriorityCacheKey>();
        private static readonly Dictionary<int, List<TimePriorityCacheKey>> PawnTransitionTargets =
            new Dictionary<int, List<TimePriorityCacheKey>>();
        private static readonly List<int> PawnTransitionIds = new List<int>();
        private static readonly Dictionary<int, Pawn> TrackedPawnsById =
            new Dictionary<int, Pawn>();
        private static readonly Dictionary<int, int> LastObservedPawnHours =
            new Dictionary<int, int>();
        private static readonly List<Map> TrackedGlobalMaps = new List<Map>();
        private static readonly HashSet<Map> TrackedGlobalMapSet = new HashSet<Map>();
        private static readonly Dictionary<Map, int> LastObservedGlobalMapHours =
            new Dictionary<Map, int>();
        private static readonly HashSet<int> ChangedPawnIds = new HashSet<int>();
        private static readonly Dictionary<int, Pawn> ChangedPawnsById =
            new Dictionary<int, Pawn>();
        private static readonly List<int> ChangedPawnIdsInOrder = new List<int>();
        private static readonly List<Pawn> GlobalRelevantPawnsBuffer = new List<Pawn>();
        private static readonly Dictionary<int, int> LastObservedGlobalPawnHours =
            new Dictionary<int, int>();
        private static int _lastObservedAbsoluteHour = -1;
        private static readonly Comparison<Pawn> PawnIdComparison = ComparePawnsById;
        private static readonly Comparison<Map> MapIdComparison = CompareMapsById;
        private static bool _runtimeEnabledKnown;
        private static bool _lastRuntimeEnabled = DefaultSettings.enableTimePrioritySchedules;

        internal static int CurrentVersion { get; private set; }

        internal static bool IsRuntimeActive => IsRuntimeEnabled && _materialActivityActive;

        internal static string BuildKey(int pawnId, TimePriorityTargetKind kind, string workTypeDefName, string targetDefName)
        {
            return pawnId + ":" + kind + ":" + (workTypeDefName ?? string.Empty) + ":" + (targetDefName ?? string.Empty);
        }

        internal static bool HasAnySchedule()
        {
            if (!IsRuntimeEnabled)
            {
                return false;
            }

            return _savedSchedulePresent;
        }

        internal static bool CanPawnWorkTypeBeAffected(Pawn pawn, WorkTypeDef workType)
        {
            if (!IsRuntimeActive || workType == null)
            {
                return false;
            }

            string workTypeDefName = workType.defName;
            return pawn != null && MaterialPawnWorkTypeTargets.Contains(new TimePriorityCacheKey(
                pawn.thingIDNumber,
                TimePriorityTargetKind.WorkType,
                workTypeDefName,
                workTypeDefName));
        }

        // This is intentionally limited to vanilla work-givers: callers use it
        // only while sub-work reassignment is disabled. It keeps the ordering
        // path allocation-free unless a material work-giver schedule exists.
        internal static bool HasMaterialWorkGiverOrdering(Pawn pawn, WorkTypeDef workType)
        {
            if (!IsRuntimeActive || workType?.workGiversByPriority == null)
            {
                return false;
            }

            string workTypeDefName = workType.defName;
            for (int i = 0; i < workType.workGiversByPriority.Count; i++)
            {
                WorkGiverDef workGiver = workType.workGiversByPriority[i];
                if (workGiver == null)
                {
                    continue;
                }

                if (pawn != null && MaterialPawnWorkGiverTargets.Contains(new TimePriorityCacheKey(
                    pawn.thingIDNumber,
                    TimePriorityTargetKind.WorkGiver,
                    workTypeDefName,
                    workGiver.defName)))
                {
                    return true;
                }

                if (MaterialGlobalWorkGiverTargets.Contains(new TimePriorityCacheKey(
                    TimePriorityTarget.GlobalPawnId,
                    TimePriorityTargetKind.WorkGiver,
                    workTypeDefName,
                    workGiver.defName)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static int[] GetPrioritiesForDisplay(TimePriorityTarget target, int fallbackPriority)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            if (!IsRuntimeEnabled || !TryGetSchedule(target, out var schedule))
            {
                return CreateFallbackPriorities(fallbackPriority);
            }

            // Linked hours are not stored values that happen to agree with the
            // box; they have no value of their own and read straight from it.
            schedule.EnsureValid();
            var priorities = new int[HoursPerDay];
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                priorities[hour] = schedule.IsUnlinked(hour)
                    ? WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour])
                    : fallbackPriority;
            }

            return priorities;
        }

        /// <summary>
        /// Which hours are pinned rather than following the priority box.
        /// Needed by anything copying a schedule, since the numbers alone cannot
        /// say whether an hour matching the box was pinned there or inheriting.
        /// </summary>
        internal static bool[] GetLinkStateForDisplay(TimePriorityTarget target)
        {
            var unlinked = new bool[HoursPerDay];
            if (!IsRuntimeEnabled || !TryGetSchedule(target, out var schedule))
            {
                return unlinked;
            }

            schedule.EnsureValid();
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                unlinked[hour] = schedule.IsUnlinked(hour);
            }

            return unlinked;
        }

        internal static int GetPriorityAtHour(TimePriorityTarget target, int fallbackPriority, int hour)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            if (!IsRuntimeEnabled || !TryGetSchedule(target, out var schedule))
            {
                return fallbackPriority;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            schedule.EnsureValid();
            return schedule.IsUnlinked(hour)
                ? WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour])
                : fallbackPriority;
        }

        /// <summary>
        /// Whether any hour has been taken off the priority box.
        ///
        /// Note what this no longer asks: whether any hour's number differs from
        /// the current default. Under that older reading, moving the box turned
        /// an entire untouched schedule custom at once.
        /// </summary>
        internal static bool HasCustomSchedule(TimePriorityTarget target, int fallbackPriority)
        {
            if (!IsRuntimeEnabled || !TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            schedule.EnsureValid();
            return schedule.HasAnyUnlinkedHour;
        }

        internal static bool IsCustomScheduledHour(TimePriorityTarget target, int hour, int fallbackPriority)
        {
            if (!IsRuntimeEnabled || !TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            schedule.EnsureValid();
            return schedule.IsUnlinked(hour);
        }

        internal static bool TryGetWorkGiverScheduleIndicatorTarget(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int pawnFallbackPriority,
            out TimePriorityTarget target,
            out int fallbackPriority)
        {
            target = default;
            fallbackPriority = WorkPrioritySystem.ClampPriority(pawnFallbackPriority);
            if (!IsRuntimeEnabled || !HasAnySchedule() || workType == null || workGiver == null)
            {
                return false;
            }

            if (pawn != null)
            {
                TimePriorityTarget pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
                if (MaterialPawnWorkGiverTargets.Contains(pawnTarget.CacheKey))
                {
                    target = pawnTarget;
                    return true;
                }
            }

            TimePriorityTarget globalTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
            int globalFallback = WorkGiverReassignmentManager.GetWorkGiverPriority(
                null,
                workGiver,
                WorkPrioritySystem.GetDefaultEnabledPriority());
            if (!MaterialGlobalWorkGiverTargets.Contains(globalTarget.CacheKey))
            {
                return false;
            }

            target = globalTarget;
            fallbackPriority = globalFallback;
            return true;
        }

        internal static void SetPriorityAtHourSynced(TimePriorityTarget target, int hour, int priority, int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            if (MultiplayerBridge.Active)
            {
                SyncSetPriorityAtHour(
                    target.PawnId,
                    (int)target.Kind,
                    target.WorkTypeDefName,
                    target.TargetDefName,
                    hour,
                    priority,
                    fallbackPriority);
                return;
            }

            SetPriorityAtHour(target, hour, priority, fallbackPriority);
        }

        internal static void ClearPriorityAtHourSynced(TimePriorityTarget target, int hour)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            if (MultiplayerBridge.Active)
            {
                SyncClearPriorityAtHour(
                    target.PawnId,
                    (int)target.Kind,
                    target.WorkTypeDefName,
                    target.TargetDefName,
                    hour);
                return;
            }

            ClearPriorityAtHour(target, hour);
        }

        internal static void SetPrioritiesSynced(TimePriorityTarget target, int[] priorities, int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            int[] normalizedPriorities = NormalizePriorities(priorities, fallbackPriority);
            if (MultiplayerBridge.Active)
            {
                SyncSetPriorities(
                    target.PawnId,
                    (int)target.Kind,
                    target.WorkTypeDefName,
                    target.TargetDefName,
                    normalizedPriorities,
                    fallbackPriority);
                return;
            }

            SetPriorities(target, normalizedPriorities, fallbackPriority);
        }

        /// <summary>
        /// Writes a whole schedule including which hours are pinned.
        ///
        /// <see cref="SetPrioritiesSynced"/> takes numbers only and has to infer
        /// the rest, which is right for callers that genuinely have no link
        /// state -- an external work tab, a rule's schedule. A caller that does
        /// know, such as a paste of a copied schedule, should say so here rather
        /// than let the inference silently drop an hour pinned at the default.
        /// </summary>
        internal static void SetScheduleSynced(
            TimePriorityTarget target,
            int[] priorities,
            bool[] unlinkedHours,
            int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            int[] normalizedPriorities = NormalizePriorities(priorities, fallbackPriority);
            int[] pinnedHours = BuildPinnedHourList(unlinkedHours);
            if (MultiplayerBridge.Active)
            {
                SyncSetSchedule(
                    target.PawnId,
                    (int)target.Kind,
                    target.WorkTypeDefName,
                    target.TargetDefName,
                    normalizedPriorities,
                    pinnedHours,
                    fallbackPriority);
                return;
            }

            SetSchedule(target, normalizedPriorities, pinnedHours, fallbackPriority);
        }

        // Multiplayer sync marshals plain arrays, so the pinned hours travel as
        // indices rather than a parallel bool[].
        private static int[] BuildPinnedHourList(bool[] unlinkedHours)
        {
            var pinned = new List<int>();
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                if (unlinkedHours != null && hour < unlinkedHours.Length && unlinkedHours[hour])
                {
                    pinned.Add(hour);
                }
            }

            return pinned.ToArray();
        }

        [SyncMethod]
        public static void SyncSetSchedule(
            int pawnId,
            int kindValue,
            string workTypeDefName,
            string targetDefName,
            int[] priorities,
            int[] pinnedHours,
            int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            TimePriorityTargetKind kind = Enum.IsDefined(typeof(TimePriorityTargetKind), kindValue)
                ? (TimePriorityTargetKind)kindValue
                : TimePriorityTargetKind.WorkType;
            var target = TimePriorityTarget.FromRaw(pawnId, kind, workTypeDefName, targetDefName);
            SetSchedule(target, priorities, pinnedHours, fallbackPriority);
        }

        private static void SetSchedule(
            TimePriorityTarget target,
            int[] priorities,
            int[] pinnedHours,
            int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            int[] normalizedPriorities = NormalizePriorities(priorities, fallbackPriority);
            if (pinnedHours == null || pinnedHours.Length == 0)
            {
                ClearSchedule(target);
                return;
            }

            var schedule = GetOrCreateSchedule(target, fallbackPriority);
            bool changed = false;
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                bool shouldPin = Array.IndexOf(pinnedHours, hour) >= 0;
                bool wasPinned = schedule.IsUnlinked(hour);
                if (shouldPin && (!wasPinned || schedule.HourlyPriorities[hour] != normalizedPriorities[hour]))
                {
                    schedule.SetOverride(hour, normalizedPriorities[hour]);
                    changed = true;
                }
                else if (!shouldPin && wasPinned)
                {
                    schedule.ClearOverride(hour);
                    changed = true;
                }
            }

            if (changed)
            {
                MirrorTargetToExternalWorkTab(target);
                NotifyChanged();
            }
        }

        internal static void ClearSchedule(TimePriorityTarget target)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            var schedules = GetSchedules(create: false);
            if (schedules == null)
            {
                return;
            }

            bool changed = false;
            for (int i = schedules.Count - 1; i >= 0; i--)
            {
                TimePriorityScheduleData schedule = schedules[i];
                if (schedule == null || schedule.Key == target.Key)
                {
                    schedules.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                MirrorTargetToExternalWorkTab(target);
                NotifyChanged();
            }
        }

        [SyncMethod]
        public static void SyncSetPriorityAtHour(
            int pawnId,
            int kindValue,
            string workTypeDefName,
            string targetDefName,
            int hour,
            int priority,
            int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            TimePriorityTargetKind kind = Enum.IsDefined(typeof(TimePriorityTargetKind), kindValue)
                ? (TimePriorityTargetKind)kindValue
                : TimePriorityTargetKind.WorkType;
            var target = TimePriorityTarget.FromRaw(pawnId, kind, workTypeDefName, targetDefName);
            SetPriorityAtHour(target, hour, priority, fallbackPriority);
        }

        [SyncMethod]
        public static void SyncClearPriorityAtHour(
            int pawnId,
            int kindValue,
            string workTypeDefName,
            string targetDefName,
            int hour)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            TimePriorityTargetKind kind = Enum.IsDefined(typeof(TimePriorityTargetKind), kindValue)
                ? (TimePriorityTargetKind)kindValue
                : TimePriorityTargetKind.WorkType;
            var target = TimePriorityTarget.FromRaw(pawnId, kind, workTypeDefName, targetDefName);
            ClearPriorityAtHour(target, hour);
        }

        [SyncMethod]
        public static void SyncSetPriorities(
            int pawnId,
            int kindValue,
            string workTypeDefName,
            string targetDefName,
            int[] priorities,
            int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            TimePriorityTargetKind kind = Enum.IsDefined(typeof(TimePriorityTargetKind), kindValue)
                ? (TimePriorityTargetKind)kindValue
                : TimePriorityTargetKind.WorkType;
            var target = TimePriorityTarget.FromRaw(pawnId, kind, workTypeDefName, targetDefName);
            SetPriorities(target, priorities, fallbackPriority);
        }

        private static void SetPriorityAtHour(TimePriorityTarget target, int hour, int priority, int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);

            // No early-out when the chosen number matches the box. Pinning an
            // hour to the default is a real instruction -- it means "stay here
            // when the box moves" -- and it is only expressible as an override.
            var schedule = GetOrCreateSchedule(target, fallbackPriority);
            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            if (schedule.IsUnlinked(hour) && schedule.HourlyPriorities[hour] == priority)
            {
                return;
            }

            schedule.SetOverride(hour, priority);
            MirrorTargetToExternalWorkTab(target);
            NotifyChanged();
        }

        /// <summary>
        /// Puts an hour back under the priority box, so it follows whatever the
        /// box says from now on.
        /// </summary>
        private static void ClearPriorityAtHour(TimePriorityTarget target, int hour)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            if (!TryGetSchedule(target, out var schedule))
            {
                return;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            if (!schedule.ClearOverride(hour))
            {
                return;
            }

            // A schedule with nothing pinned is indistinguishable from no
            // schedule, and leaving the row behind would keep reporting the
            // target as scheduled to every indicator that asks.
            if (!schedule.HasAnyUnlinkedHour)
            {
                ClearSchedule(target);
                return;
            }

            MirrorTargetToExternalWorkTab(target);
            NotifyChanged();
        }

        private static void SetPriorities(TimePriorityTarget target, int[] priorities, int fallbackPriority)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            int[] normalizedPriorities = NormalizePriorities(priorities, fallbackPriority);
            bool allFallback = true;
            for (int i = 0; i < normalizedPriorities.Length; i++)
            {
                if (normalizedPriorities[i] != fallbackPriority)
                {
                    allFallback = false;
                    break;
                }
            }

            if (allFallback)
            {
                ClearSchedule(target);
                return;
            }

            var schedule = GetOrCreateSchedule(target, fallbackPriority);
            bool changed = false;
            for (int i = 0; i < HoursPerDay; i++)
            {
                // A bulk write carries plain numbers with no link state, so the
                // only safe reading is the one it used to have: an hour that
                // differs from the box was pinned, the rest follow it. Pasting a
                // schedule therefore cannot yet reproduce an hour deliberately
                // pinned at the default -- that needs the clipboard to carry
                // link state, and is tracked separately.
                bool shouldPin = normalizedPriorities[i] != fallbackPriority;
                bool wasPinned = schedule.IsUnlinked(i);
                if (shouldPin && (!wasPinned || schedule.HourlyPriorities[i] != normalizedPriorities[i]))
                {
                    schedule.SetOverride(i, normalizedPriorities[i]);
                    changed = true;
                }
                else if (!shouldPin && wasPinned)
                {
                    schedule.ClearOverride(i);
                    changed = true;
                }
            }

            if (changed)
            {
                MirrorTargetToExternalWorkTab(target);
                NotifyChanged();
            }
        }

        /// <summary>
        /// Republishes an hourly schedule to any external work-tab mod backing the priority numbers.
        /// Hourly schedules live only in Better Work Tab, so nothing else propagates them.
        /// </summary>
        private static void MirrorTargetToExternalWorkTab(TimePriorityTarget target)
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            if (ExternalPriorityMirror.IsSuspended)
            {
                return;
            }

            if (!ExternalPriorityMirror.ShouldMirrorTimePrioritySchedules)
            {
                return;
            }

            if (target.Kind == TimePriorityTargetKind.WorkGiver)
            {
                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(target.TargetDefName);
                if (workGiver == null)
                {
                    return;
                }

                if (target.IsGlobal)
                {
                    ExternalPriorityMirror.NotifyWorkGiverChangedForAllPawns(workGiver);
                    return;
                }

                Pawn workGiverPawn = FindPawn(target.PawnId);
                if (workGiverPawn != null)
                {
                    ExternalPriorityMirror.NotifyWorkGiverChanged(workGiverPawn, workGiver);
                }

                return;
            }

            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(target.WorkTypeDefName);
            if (workType == null)
            {
                return;
            }

            if (target.IsGlobal)
            {
                ExternalPriorityMirror.NotifyWorkTypeChangedForAllPawns(workType);
                return;
            }

            Pawn pawn = FindPawn(target.PawnId);
            if (pawn != null)
            {
                ExternalPriorityMirror.NotifyWorkTypeChanged(pawn, workType);
            }
        }

        private static Pawn FindPawn(int pawnId)
        {
            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                if (pawn != null && pawn.thingIDNumber == pawnId)
                {
                    return pawn;
                }
            }

            return null;
        }

        internal static bool IsRuntimeEnabled => _lastRuntimeEnabled;

        internal static void OnRuntimeSettingChanged()
        {
            bool enabled = ReadRuntimeEnabled();
            if (!_runtimeEnabledKnown)
            {
                _runtimeEnabledKnown = true;
                _lastRuntimeEnabled = enabled;
                return;
            }

            if (_lastRuntimeEnabled != enabled)
            {
                _lastRuntimeEnabled = enabled;
                Cache.Clear();
                _cachedVersion = -1;
                InvalidateMaterialActivity();
                CurrentVersion++;
                if (enabled)
                {
                    NormalizeLoadedSchedules();
                    MigrateLinkStateFromLegacySaves();
                    RebuildMaterialActivityIndex();
                }

                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                    UI.WorkGrid.Contracts.WorkTabDirtyFlags.ScheduleHour);
                WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            }
        }

        private static bool ReadRuntimeEnabled()
        {
            return BetterWorkTabMod.Settings?.enableTimePrioritySchedules ??
                DefaultSettings.enableTimePrioritySchedules;
        }

        internal static void NotifyFallbacksChanged()
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            InvalidateMaterialActivity();
            RebuildMaterialActivityIndex();
            CurrentVersion++;
            _cachedVersion = -1;
            Cache.Clear();
        }

        /// <summary>
        /// Invalidates runtime state after an integration has directly edited the
        /// public saved schedule list. Direct list edits are supported for save
        /// import compatibility, but the integration must call this seam once the
        /// edit is complete; steady-state polling is intentionally not provided.
        /// </summary>
        internal static void NotifyExternalDataChanged()
        {
            NotifyChanged();
        }

        internal static bool TryGetDisabledByTime(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            out string reason,
            out TimePriorityTarget disabledTarget)
        {
            reason = null;
            disabledTarget = default;
            if (!IsRuntimeActive || pawn == null || workType == null)
            {
                return false;
            }

            int baseWorkTypePriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            TimePriorityEvaluation workTypeEvaluation = EvaluateWorkTypePriority(pawn, workType, baseWorkTypePriority);
            if (workTypeEvaluation.DisabledBySchedule)
            {
                disabledTarget = workTypeEvaluation.Target;
                reason = workTypeEvaluation.DisabledReason;
                return true;
            }

            if (workGiver != null)
            {
                int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                    pawn,
                    workGiver,
                    workTypeEvaluation.EffectivePriority);
                TimePriorityEvaluation workGiverEvaluation =
                    EvaluateWorkGiverPriority(pawn, workType, workGiver, baseWorkGiverPriority);
                if (workGiverEvaluation.DisabledBySchedule)
                {
                    disabledTarget = workGiverEvaluation.Target;
                    reason = workGiverEvaluation.DisabledReason;
                    return true;
                }
            }

            return false;
        }

        internal static TimePriorityEvaluation EvaluateWorkTypePriority(Pawn pawn, WorkTypeDef workType, int basePriority)
        {
            basePriority = WorkPrioritySystem.ClampPriority(basePriority);
            if (!IsRuntimeActive || pawn == null || workType == null)
            {
                return new TimePriorityEvaluation(
                    default,
                    0,
                    basePriority,
                    basePriority,
                    false,
                    basePriority,
                    TimePriorityScheduleScopes.None);
            }

            TimePriorityTarget target = TimePriorityTarget.ForWorkType(pawn, workType);
            int hour = GetCurrentHour(pawn);
            if (!CanPawnWorkTypeBeAffected(pawn, workType))
            {
                return new TimePriorityEvaluation(
                    target,
                    hour,
                    basePriority,
                    basePriority,
                    false,
                    basePriority,
                    TimePriorityScheduleScopes.None);
            }

            if (basePriority <= WorkPrioritySystem.DisabledPriority)
            {
                bool hasDisabledBaseSchedule = TryGetScheduledPriority(target, hour, out int disabledBaseScheduledPriority);
                return new TimePriorityEvaluation(
                    target,
                    hour,
                    basePriority,
                    basePriority,
                    hasDisabledBaseSchedule,
                    disabledBaseScheduledPriority,
                    hasDisabledBaseSchedule ? TimePriorityScheduleScopes.Pawn : TimePriorityScheduleScopes.None);
            }

            if (TryGetScheduledPriority(target, hour, out int scheduledPriority))
            {
                return new TimePriorityEvaluation(
                    target,
                    hour,
                    basePriority,
                    scheduledPriority,
                    true,
                    scheduledPriority,
                    TimePriorityScheduleScopes.Pawn);
            }

            return new TimePriorityEvaluation(
                target,
                hour,
                basePriority,
                basePriority,
                false,
                basePriority,
                TimePriorityScheduleScopes.None);
        }

        internal static int GetEffectiveWorkTypePriority(Pawn pawn, WorkTypeDef workType, int basePriority)
        {
            return EvaluateWorkTypePriority(pawn, workType, basePriority).EffectivePriority;
        }

        internal static bool IsWorkTypeDisabledBySchedule(Pawn pawn, WorkTypeDef workType)
        {
            int basePriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            return EvaluateWorkTypePriority(pawn, workType, basePriority).DisabledBySchedule;
        }

        internal static TimePriorityEvaluation EvaluateWorkGiverPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int basePriority)
        {
            basePriority = WorkPrioritySystem.ClampPriority(basePriority);
            if (!IsRuntimeActive || workGiver == null)
            {
                return new TimePriorityEvaluation(
                    default,
                    0,
                    basePriority,
                    basePriority,
                    false,
                    basePriority,
                    TimePriorityScheduleScopes.None);
            }

            TimePriorityTarget pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
            int hour = GetCurrentHour(pawn);
            TimePriorityCacheKey pawnKey = pawnTarget.CacheKey;
            TimePriorityTarget globalTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
            TimePriorityCacheKey globalKey = globalTarget.CacheKey;
            bool hasMaterialPawnSchedule = pawn != null && MaterialPawnWorkGiverTargets.Contains(pawnKey);
            bool hasMaterialGlobalSchedule = MaterialGlobalWorkGiverTargets.Contains(globalKey);
            if (!hasMaterialPawnSchedule && !hasMaterialGlobalSchedule)
            {
                return new TimePriorityEvaluation(
                    pawnTarget,
                    hour,
                    basePriority,
                    basePriority,
                    false,
                    basePriority,
                    TimePriorityScheduleScopes.None);
            }

            if (hasMaterialPawnSchedule &&
                TryGetScheduledPriority(pawnTarget, hour, out int pawnScheduledPriority))
            {
                return new TimePriorityEvaluation(
                    pawnTarget,
                    hour,
                    basePriority,
                    basePriority > WorkPrioritySystem.DisabledPriority ? pawnScheduledPriority : basePriority,
                    true,
                    pawnScheduledPriority,
                    TimePriorityScheduleScopes.Pawn);
            }

            if (hasMaterialGlobalSchedule &&
                TryGetScheduledPriority(globalTarget, hour, out int globalScheduledPriority))
            {
                return new TimePriorityEvaluation(
                    globalTarget,
                    hour,
                    basePriority,
                    basePriority > WorkPrioritySystem.DisabledPriority ? globalScheduledPriority : basePriority,
                    true,
                    globalScheduledPriority,
                    TimePriorityScheduleScopes.Global);
            }

            return new TimePriorityEvaluation(
                pawnTarget,
                hour,
                basePriority,
                basePriority,
                false,
                basePriority,
                TimePriorityScheduleScopes.None);
        }

        internal static int GetEffectiveWorkGiverPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int basePriority)
        {
            return EvaluateWorkGiverPriority(pawn, workType, workGiver, basePriority).EffectivePriority;
        }

        internal static int GetCurrentHour(Pawn pawn)
        {
            try
            {
                if (pawn != null)
                {
                    return Mathf.Clamp(GenLocalDate.HourOfDay(pawn), 0, HoursPerDay - 1);
                }
            }
            catch
            {
                // Fall back to absolute game ticks when local date APIs are unavailable.
            }

            int ticks = GenTicks.TicksAbs;
            return Mathf.Abs(ticks / GenDate.TicksPerHour) % HoursPerDay;
        }

        private static int GetCurrentHour(Map map)
        {
            try
            {
                if (map != null)
                {
                    return Mathf.Clamp(GenLocalDate.HourOfDay(map), 0, HoursPerDay - 1);
                }
            }
            catch
            {
                // Fall back to absolute game ticks when a map is being removed.
            }

            int ticks = GenTicks.TicksAbs;
            return Mathf.Abs(ticks / GenDate.TicksPerHour) % HoursPerDay;
        }

        internal static string FormatHour(int hour)
        {
            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            return hour.ToString("00") + ":00";
        }

        internal static void NotifyHourBoundaryIfNeeded()
        {
            if (!IsRuntimeActive ||
                (GlobalWorkGiverTransitionTargets.Count == 0 && PawnTransitionIds.Count == 0))
            {
                return;
            }

            ChangedPawnIds.Clear();
            if (GlobalWorkGiverTransitionTargets.Count > 0)
            {
                bool localHourChanged = SyncTrackedGlobalMaps();
                int absoluteHour = GetCurrentAbsoluteHour();
                bool absoluteHourChanged = _lastObservedAbsoluteHour >= 0 &&
                    _lastObservedAbsoluteHour != absoluteHour;
                _lastObservedAbsoluteHour = absoluteHour;

                // The relevant population is deliberately enumerated only after a
                // local-hour transition. This matches WorkExecutionOrder's player
                // population and keeps closed-game frames free of pawn scans.
                if (localHourChanged || absoluteHourChanged)
                {
                    ScanGlobalTransitionPopulation();
                }
            }

            for (int pawnIndex = PawnTransitionIds.Count - 1; pawnIndex >= 0; pawnIndex--)
            {
                int pawnId = PawnTransitionIds[pawnIndex];
                if (!TrackedPawnsById.TryGetValue(pawnId, out Pawn pawn) ||
                    pawn == null || pawn.DestroyedOrNull())
                {
                    pawn = FindPawn(pawnId);
                    if (pawn != null && !pawn.DestroyedOrNull())
                    {
                        TrackedPawnsById[pawnId] = pawn;
                    }
                    else
                    {
                        // A pawn can be absent while a map/world is being
                        // reconstructed. Keep the deterministic target id and
                        // retry on the next boundary instead of losing its save.
                        continue;
                    }
                }

                if (!LastObservedPawnHours.TryGetValue(pawnId, out int previousHour))
                {
                    LastObservedPawnHours[pawnId] = GetCurrentHour(pawn);
                    continue;
                }

                int currentHour = GetCurrentHour(pawn);
                if (previousHour == currentHour)
                {
                    continue;
                }

                LastObservedPawnHours[pawnId] = currentHour;
                List<TimePriorityCacheKey> targets = PawnTransitionTargets[pawnId];
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    TimePriorityCacheKey target = targets[targetIndex];
                    if (GetEffectivePriorityAtHour(pawn, target, previousHour) !=
                        GetEffectivePriorityAtHour(pawn, target, currentHour))
                    {
                        ChangedPawnIds.Add(pawnId);
                        ChangedPawnsById[pawnId] = pawn;
                        break;
                    }
                }
            }

            ChangedPawnIdsInOrder.Clear();
            ChangedPawnIdsInOrder.AddRange(ChangedPawnIds);
            ChangedPawnIdsInOrder.Sort();
            for (int i = 0; i < ChangedPawnIdsInOrder.Count; i++)
            {
                ChangedPawnsById.TryGetValue(ChangedPawnIdsInOrder[i], out Pawn pawn);
                if (pawn?.Faction == Faction.OfPlayer && pawn.workSettings != null)
                {
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                }
            }

            ChangedPawnsById.Clear();

            if (ChangedPawnIdsInOrder.Count > 0)
            {
                UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                    UI.WorkGrid.Contracts.WorkTabDirtyFlags.ScheduleHour);
            }
        }

        private static void ScanGlobalTransitionPopulation()
        {
            GlobalRelevantPawnsBuffer.Clear();
            GlobalRelevantPawnsBuffer.AddRange(PawnsFinder.AllMapsWorldAndTemporary_Alive);
            GlobalRelevantPawnsBuffer.Sort(PawnIdComparison);

            for (int pawnIndex = 0; pawnIndex < GlobalRelevantPawnsBuffer.Count; pawnIndex++)
            {
                Pawn pawn = GlobalRelevantPawnsBuffer[pawnIndex];
                if (pawn?.Faction != Faction.OfPlayer || pawn.workSettings == null)
                {
                    continue;
                }

                int pawnId = pawn.thingIDNumber;
                int currentHour = GetCurrentHour(pawn);
                if (!LastObservedGlobalPawnHours.TryGetValue(pawnId, out int previousHour))
                {
                    LastObservedGlobalPawnHours[pawnId] = currentHour;
                    continue;
                }

                LastObservedGlobalPawnHours[pawnId] = currentHour;
                if (previousHour == currentHour ||
                    !HasGlobalWorkGiverPriorityChange(pawn, previousHour, currentHour))
                {
                    continue;
                }

                ChangedPawnIds.Add(pawnId);
                ChangedPawnsById[pawnId] = pawn;
            }
        }

        private static bool SyncTrackedGlobalMaps()
        {
            bool mapSetChanged = false;
            bool localHourChanged = false;
            for (int i = TrackedGlobalMaps.Count - 1; i >= 0; i--)
            {
                Map map = TrackedGlobalMaps[i];
                if (map != null && Find.Maps != null && Find.Maps.Contains(map))
                {
                    continue;
                }

                TrackedGlobalMaps.RemoveAt(i);
                TrackedGlobalMapSet.Remove(map);
                LastObservedGlobalMapHours.Remove(map);
                mapSetChanged = true;
            }

            if (Find.Maps == null)
            {
                return false;
            }

            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map == null || !TrackedGlobalMapSet.Add(map))
                {
                    continue;
                }

                TrackedGlobalMaps.Add(map);
                LastObservedGlobalMapHours[map] = GetCurrentHour(map);
                mapSetChanged = true;
            }

            if (mapSetChanged)
            {
                TrackedGlobalMaps.Sort(MapIdComparison);
            }

            for (int i = 0; i < TrackedGlobalMaps.Count; i++)
            {
                Map map = TrackedGlobalMaps[i];
                int currentHour = GetCurrentHour(map);
                if (LastObservedGlobalMapHours.TryGetValue(map, out int previousHour) &&
                    previousHour != currentHour)
                {
                    localHourChanged = true;
                }

                LastObservedGlobalMapHours[map] = currentHour;
            }

            return localHourChanged;
        }

        private static int GetCurrentAbsoluteHour()
        {
            return Mathf.Abs(GenTicks.TicksAbs / GenDate.TicksPerHour) % HoursPerDay;
        }

        private static bool HasGlobalWorkGiverPriorityChange(Pawn pawn, int previousHour, int currentHour)
        {
            for (int i = 0; i < GlobalWorkGiverTransitionTargets.Count; i++)
            {
                TimePriorityCacheKey target = GlobalWorkGiverTransitionTargets[i];
                if (GetEffectivePriorityAtHour(pawn, target, previousHour) !=
                    GetEffectivePriorityAtHour(pawn, target, currentHour))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetEffectivePriorityAtHour(Pawn pawn, TimePriorityCacheKey target, int hour)
        {
            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(target.WorkTypeDefName);
            if (pawn == null || workType == null)
            {
                return WorkPrioritySystem.DisabledPriority;
            }

            int effectiveWorkTypePriority = PriorityAuthorityBroker.GetEffectivePriorityAtHour(
                pawn,
                workType,
                hour);
            if (target.Kind == TimePriorityTargetKind.WorkType)
            {
                return effectiveWorkTypePriority;
            }

            WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(target.TargetDefName);
            if (workGiver == null)
            {
                return WorkPrioritySystem.DisabledPriority;
            }

            int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                pawn,
                workGiver,
                effectiveWorkTypePriority);
            if (baseWorkGiverPriority <= WorkPrioritySystem.DisabledPriority)
            {
                return baseWorkGiverPriority;
            }

            TimePriorityTarget pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
            if (pawn != null &&
                MaterialPawnWorkGiverTargets.Contains(pawnTarget.CacheKey) &&
                TryGetScheduledPriority(pawnTarget, hour, out int pawnScheduledPriority))
            {
                return pawnScheduledPriority;
            }

            TimePriorityTarget globalTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
            return MaterialGlobalWorkGiverTargets.Contains(globalTarget.CacheKey) &&
                   TryGetScheduledPriority(globalTarget, hour, out int globalScheduledPriority)
                ? globalScheduledPriority
                : baseWorkGiverPriority;
        }

        private static int ComparePawnsById(Pawn a, Pawn b)
        {
            int aId = a?.thingIDNumber ?? int.MinValue;
            int bId = b?.thingIDNumber ?? int.MinValue;
            return aId.CompareTo(bId);
        }

        internal static void NotifyLoaded()
        {
            OnRuntimeSettingChanged();
            if (!IsRuntimeEnabled)
            {
                Cache.Clear();
                _cachedVersion = -1;
                InvalidateMaterialActivity();
                return;
            }

            NormalizeLoadedSchedules();
            Cache.Clear();
            _cachedVersion = -1;
            CurrentVersion++;
            InvalidateMaterialActivity();
            MigrateLinkStateFromLegacySaves();
            RebuildMaterialActivityIndex();
        }

        /// <summary>
        /// Gives hours their link state in saves written before it was stored.
        ///
        /// Those saves held 24 plain numbers and worked out which were overrides
        /// by comparing each against the work's default, so that comparison is
        /// the only thing they can be read back as -- an hour differed, so it
        /// was an override. Applying it once, here, means the rest of the mod
        /// never has to ask again.
        ///
        /// One case cannot be recovered: an hour deliberately pinned at the
        /// default was already indistinguishable from an inherited one, and was
        /// already displayed as inherited. It stays linked. Nothing the player
        /// could previously see changes.
        /// </summary>
        private static void MigrateLinkStateFromLegacySaves()
        {
            List<TimePriorityScheduleData> schedules = GetSchedules(create: false);
            if (schedules == null)
            {
                return;
            }

            bool changed = false;
            for (int i = schedules.Count - 1; i >= 0; i--)
            {
                TimePriorityScheduleData schedule = schedules[i];
                if (schedule == null)
                {
                    continue;
                }

                if (!schedule.NeedsLinkMigration)
                {
                    continue;
                }

                schedule.EnsureValid();
                if (schedule.Kind == TimePriorityTargetKind.WorkGiver &&
                    !CanResolveLegacyWorkGiverFallback(schedule))
                {
                    // Sub-work's fallback can differ from vanilla. Keep the
                    // migration marker until both the feature and its target
                    // data are available, so a disabled-load does not corrupt
                    // the saved schedule.
                    continue;
                }

                int fallback = ResolveFallbackPriority(schedule);
                schedule.NeedsLinkMigration = false;
                for (int hour = 0; hour < HoursPerDay; hour++)
                {
                    if (WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour]) != fallback)
                    {
                        schedule.SetOverride(hour, schedule.HourlyPriorities[hour]);
                    }
                }

                // Every hour matched the default, so the row only ever described
                // "no schedule" in a more expensive way.
                if (!schedule.HasAnyUnlinkedHour)
                {
                    schedules.RemoveAt(i);
                }

                changed = true;
            }

            if (changed)
            {
                Cache.Clear();
                _cachedVersion = -1;
                CurrentVersion++;
                InvalidateMaterialActivity();
                RebuildMaterialActivityIndex();
            }
        }

        private static bool CanResolveLegacyWorkGiverFallback(TimePriorityScheduleData schedule)
        {
            if (!WorkGiverReassignmentManager.IsRuntimeEnabled ||
                schedule == null ||
                DefDatabase<WorkGiverDef>.GetNamedSilentFail(schedule.TargetDefName) == null)
            {
                return false;
            }

            return schedule.PawnId == TimePriorityTarget.GlobalPawnId ||
                   FindPawn(schedule.PawnId) != null;
        }

        /// <summary>
        /// The priority a schedule's hours fall back to. Global rows have no
        /// pawn, so they use the work's default rather than a pawn's setting.
        /// </summary>
        private static int ResolveFallbackPriority(TimePriorityScheduleData schedule)
        {
            int defaultEnabled = WorkPrioritySystem.GetDefaultEnabledPriority();
            Pawn pawn = schedule.PawnId == TimePriorityTarget.GlobalPawnId
                ? null
                : FindPawn(schedule.PawnId);

            if (schedule.Kind == TimePriorityTargetKind.WorkGiver)
            {
                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(schedule.TargetDefName);
                if (workGiver == null)
                {
                    return defaultEnabled;
                }

                int parentPriority = pawn != null && workGiver.workType != null
                    ? WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workGiver.workType)
                    : defaultEnabled;
                return WorkPrioritySystem.ClampPriority(
                    WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority));
            }

            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(schedule.WorkTypeDefName);
            if (pawn == null || workType == null)
            {
                return defaultEnabled;
            }

            return WorkPrioritySystem.ClampPriority(
                WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType));
        }

        internal static bool TryGetScheduledPriority(TimePriorityTarget target, int hour, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            if (!IsRuntimeEnabled || !TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            schedule.EnsureValid();
            if (!schedule.IsUnlinked(hour))
            {
                // A linked hour has no scheduled priority of its own; callers
                // without a fallback to hand cannot be told what it resolves to.
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour]);
            return true;
        }

        private static TimePriorityScheduleData GetOrCreateSchedule(TimePriorityTarget target, int fallbackPriority)
        {
            EnsureCache();
            if (Cache.TryGetValue(target.CacheKey, out var schedule))
            {
                schedule.EnsureValid();
                return schedule;
            }

            var schedules = GetSchedules(create: true);
            schedule = new TimePriorityScheduleData
            {
                PawnId = target.PawnId,
                Kind = target.Kind,
                WorkTypeDefName = target.WorkTypeDefName,
                TargetDefName = target.TargetDefName,
                HourlyPriorities = new List<int>(HoursPerDay)
            };

            schedule.HourlyPriorities.AddRange(CreateFallbackPriorities(fallbackPriority));

            schedule.EnsureValid();
            schedules.Add(schedule);
            Cache[schedule.CacheKey] = schedule;
            NotifyChanged();
            return schedule;
        }

        private static bool TryGetSchedule(TimePriorityTarget target, out TimePriorityScheduleData schedule)
        {
            EnsureCache();
            return Cache.TryGetValue(target.CacheKey, out schedule);
        }

        private static int[] CreateFallbackPriorities(int fallbackPriority)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            var priorities = new int[HoursPerDay];
            for (int i = 0; i < HoursPerDay; i++)
            {
                priorities[i] = fallbackPriority;
            }

            return priorities;
        }

        private static int[] NormalizePriorities(int[] priorities, int fallbackPriority)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            var normalized = new int[HoursPerDay];
            for (int i = 0; i < HoursPerDay; i++)
            {
                normalized[i] = WorkPrioritySystem.ClampPriority(
                    priorities != null && i < priorities.Length
                        ? priorities[i]
                        : fallbackPriority);
            }

            return normalized;
        }

        private static void EnsureCache()
        {
            if (_cachedVersion == CurrentVersion)
            {
                return;
            }

            Cache.Clear();
            if (!IsRuntimeEnabled)
            {
                _cachedVersion = CurrentVersion;
                return;
            }

            var schedules = GetSchedules(create: false);
            if (schedules != null)
            {
                for (int i = schedules.Count - 1; i >= 0; i--)
                {
                    var schedule = schedules[i];
                    if (schedule == null)
                    {
                        schedules.RemoveAt(i);
                        continue;
                    }

                    schedule.EnsureValid();
                    Cache[new TimePriorityCacheKey(
                        schedule.PawnId,
                        schedule.Kind,
                        schedule.WorkTypeDefName,
                        schedule.TargetDefName)] = schedule;
                }
            }

            _cachedVersion = CurrentVersion;
        }

        private static List<TimePriorityScheduleData> GetSchedules(bool create)
        {
            // The public component list is retained for save compatibility. Runtime writers must
            // use this service's mutation API; direct list edits cannot invalidate caches without
            // reintroducing a steady-state content audit.
            var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (component == null)
            {
                return null;
            }

            if (component.TimePrioritySchedules == null && create)
            {
                component.TimePrioritySchedules = new List<TimePriorityScheduleData>();
            }

            return component.TimePrioritySchedules;
        }

        private static void NormalizeLoadedSchedules()
        {
            List<TimePriorityScheduleData> schedules = GetSchedules(create: true);
            if (schedules == null)
            {
                return;
            }

            for (int i = schedules.Count - 1; i >= 0; i--)
            {
                TimePriorityScheduleData schedule = schedules[i];
                if (schedule == null)
                {
                    schedules.RemoveAt(i);
                    continue;
                }

                schedule.EnsureValid();
            }
        }

        private static void NotifyChanged()
        {
            if (!IsRuntimeEnabled)
            {
                return;
            }

            CurrentVersion++;
            _cachedVersion = -1;
            InvalidateMaterialActivity();
            RebuildMaterialActivityIndex();
            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.ScheduleHour);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        private static void InvalidateMaterialActivity()
        {
            _materialActivityGeneration++;
            _materialActivityBuiltGeneration = -1;
            _materialActivityActive = false;
            _savedSchedulePresent = false;
        }

        private static void RebuildMaterialActivityIndex()
        {
            if (_materialActivityBuiltGeneration == _materialActivityGeneration)
            {
                return;
            }

            ClearMaterialActivityIndex();
            if (!IsRuntimeEnabled)
            {
                _materialActivityBuiltGeneration = _materialActivityGeneration;
                _materialActivityActive = false;
                _savedSchedulePresent = false;
                return;
            }

            List<TimePriorityScheduleData> schedules = GetSchedules(create: false);
            bool active = false;
            bool savedSchedulePresent = false;
            if (schedules != null)
            {
                for (int i = 0; i < schedules.Count; i++)
                {
                    TimePriorityScheduleData schedule = schedules[i];
                    if (schedule == null)
                    {
                        continue;
                    }

                    savedSchedulePresent = true;
                    bool isGlobal = schedule.PawnId == TimePriorityTarget.GlobalPawnId;
                    schedule.EnsureValid();
                    if (isGlobal && schedule.Kind == TimePriorityTargetKind.WorkType)
                    {
                        // Global WorkType rows remain persisted and visible, but base
                        // evaluation never consumes them.
                        continue;
                    }

                    int fallback = ResolveFallbackPriority(schedule);
                    bool material = false;
                    for (int j = 0; j < schedule.UnlinkedHours.Count; j++)
                    {
                        int hour = schedule.UnlinkedHours[j];
                        if (schedule.HourlyPriorities[hour] != fallback)
                        {
                            material = true;
                        }
                    }

                    TimePriorityCacheKey key = schedule.CacheKey;
                    if (material)
                    {
                        active = true;
                        if (isGlobal)
                        {
                            if (schedule.Kind == TimePriorityTargetKind.WorkGiver)
                            {
                                MaterialGlobalWorkGiverTargets.Add(key);
                            }
                        }
                        else if (schedule.Kind == TimePriorityTargetKind.WorkGiver)
                        {
                            MaterialPawnWorkGiverTargets.Add(key);
                        }
                        else
                        {
                            MaterialPawnWorkTypeTargets.Add(key);
                        }
                    }

                    bool hasTransition = false;
                    int previousHour = HoursPerDay - 1;
                    for (int hour = 0; hour < HoursPerDay; hour++)
                    {
                        int previousValue = GetEffectiveSchedulePriority(schedule, previousHour, fallback);
                        int enteringValue = GetEffectiveSchedulePriority(schedule, hour, fallback);
                        if (previousValue != enteringValue)
                        {
                            hasTransition = true;
                        }

                        previousHour = hour;
                    }

                    if (!hasTransition)
                    {
                        continue;
                    }

                    if (isGlobal && schedule.Kind == TimePriorityTargetKind.WorkGiver)
                    {
                        GlobalWorkGiverTransitionTargets.Add(key);
                    }
                    else if (!isGlobal)
                    {
                        if (!PawnTransitionTargets.TryGetValue(schedule.PawnId, out List<TimePriorityCacheKey> targets))
                        {
                            targets = new List<TimePriorityCacheKey>();
                            PawnTransitionTargets[schedule.PawnId] = targets;
                            PawnTransitionIds.Add(schedule.PawnId);
                        }

                        targets.Add(key);
                    }
                }
            }

            GlobalWorkGiverTransitionTargets.Sort(CompareTargets);
            PawnTransitionIds.Sort();
            for (int i = 0; i < PawnTransitionIds.Count; i++)
            {
                PawnTransitionTargets[PawnTransitionIds[i]].Sort(CompareTargets);
                Pawn pawn = FindPawn(PawnTransitionIds[i]);
                if (pawn != null)
                {
                    TrackedPawnsById[PawnTransitionIds[i]] = pawn;
                    LastObservedPawnHours[PawnTransitionIds[i]] = GetCurrentHour(pawn);
                }
            }

            if (GlobalWorkGiverTransitionTargets.Count > 0 && Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    Map map = Find.Maps[i];
                    if (map == null || !TrackedGlobalMapSet.Add(map))
                    {
                        continue;
                    }

                    TrackedGlobalMaps.Add(map);
                    LastObservedGlobalMapHours[map] = GetCurrentHour(map);
                }

                TrackedGlobalMaps.Sort(MapIdComparison);
            }

            _lastObservedAbsoluteHour = GetCurrentAbsoluteHour();
            _materialActivityActive = active;
            _savedSchedulePresent = savedSchedulePresent;
            _materialActivityBuiltGeneration = _materialActivityGeneration;
        }

        private static void ClearMaterialActivityIndex()
        {
            MaterialPawnWorkTypeTargets.Clear();
            MaterialGlobalWorkGiverTargets.Clear();
            MaterialPawnWorkGiverTargets.Clear();
            GlobalWorkGiverTransitionTargets.Clear();
            PawnTransitionTargets.Clear();
            PawnTransitionIds.Clear();
            TrackedPawnsById.Clear();
            LastObservedPawnHours.Clear();
            LastObservedGlobalPawnHours.Clear();
            TrackedGlobalMaps.Clear();
            TrackedGlobalMapSet.Clear();
            LastObservedGlobalMapHours.Clear();
            ChangedPawnIds.Clear();
            ChangedPawnsById.Clear();
            ChangedPawnIdsInOrder.Clear();
            GlobalRelevantPawnsBuffer.Clear();
            _lastObservedAbsoluteHour = -1;
        }

        private static int GetEffectiveSchedulePriority(
            TimePriorityScheduleData schedule,
            int hour,
            int fallback)
        {
            return schedule.IsUnlinked(hour)
                ? schedule.HourlyPriorities[hour]
                : fallback;
        }

        private static int CompareTargets(TimePriorityCacheKey a, TimePriorityCacheKey b)
        {
            int comparison = a.PawnId.CompareTo(b.PawnId);
            if (comparison != 0) return comparison;
            comparison = a.Kind.CompareTo(b.Kind);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(a.WorkTypeDefName, b.WorkTypeDefName);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(a.TargetDefName, b.TargetDefName);
        }

        private static int CompareMapsById(Map a, Map b)
        {
            int aId = a?.uniqueID ?? int.MinValue;
            int bId = b?.uniqueID ?? int.MinValue;
            return aId.CompareTo(bId);
        }

    }
}
