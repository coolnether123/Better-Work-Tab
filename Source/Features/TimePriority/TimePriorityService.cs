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
        private static Game _cachedGame;
        private static int _cachedVersion = -1;
        private static bool _scheduleActivityKnown;
        private static bool _scheduleDataActive;
        private static List<TimePriorityScheduleData> _observedSchedules;
        private static TimePriorityScheduleData[] _observedScheduleEntries;
        private static int _observedScheduleCount = -1;
        private static bool _scheduleCollectionStateKnown;
        private static bool _loadBoundaryHandled;
        private static int _mutationBatchDepth;
        private static bool _mutationBatchChanged;

        internal static int CurrentVersion { get; private set; }

        internal static bool IsRuntimeActive => IsRuntimeEnabled && HasAnySchedule();

        internal static string BuildKey(int pawnId, TimePriorityTargetKind kind, string workTypeDefName, string targetDefName)
        {
            return pawnId + ":" + kind + ":" + (workTypeDefName ?? string.Empty) + ":" + (targetDefName ?? string.Empty);
        }

        internal static bool HasAnySchedule()
        {
            return RefreshScheduleActivity();
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
            PublishScheduleChange(
                GetSchedules(create: false),
                rebuildCache: false,
                notifyConsumers: false);
            return true;
        }

        internal static int[] GetPrioritiesForDisplay(TimePriorityTarget target, int fallbackPriority)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            if (!TryGetSchedule(target, out var schedule))
            {
                return CreateFallbackPriorities(fallbackPriority);
            }

            // Linked hours are not stored values that happen to agree with the
            // box; they have no value of their own and read straight from it.
            var priorities = new int[HoursPerDay];
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                priorities[hour] = schedule.TryGetStoredPriority(hour, out int storedPriority)
                    ? storedPriority
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
            if (!TryGetSchedule(target, out var schedule))
            {
                return unlinked;
            }

            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                unlinked[hour] = schedule.IsUnlinked(hour);
            }

            return unlinked;
        }

        internal static int GetPriorityAtHour(TimePriorityTarget target, int fallbackPriority, int hour)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            if (!TryGetSchedule(target, out var schedule))
            {
                return fallbackPriority;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            return schedule.TryGetStoredPriority(hour, out int storedPriority)
                ? storedPriority
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
            if (!TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            return schedule.HasAnyUnlinkedHour;
        }

        internal static bool IsCustomScheduledHour(TimePriorityTarget target, int hour, int fallbackPriority)
        {
            if (!TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
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
            if (!IsRuntimeActive || workType == null || workGiver == null)
            {
                return false;
            }

            if (pawn != null)
            {
                TimePriorityTarget runtimePawnTarget =
                    TimePriorityTarget.ForRuntimeWorkGiver(pawn, workType, workGiver);
                if (HasCustomSchedule(runtimePawnTarget, fallbackPriority))
                {
                    // This target crosses into the editor when its ring is
                    // clicked, so resolve its display label only on the UI
                    // path that actually needs it.
                    target = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
                    return true;
                }
            }

            TimePriorityTarget runtimeGlobalTarget =
                TimePriorityTarget.ForRuntimeWorkGiver(null, workType, workGiver);
            int globalFallback = WorkGiverReassignmentManager.GetWorkGiverPriority(
                null,
                workGiver,
                WorkPrioritySystem.GetDefaultEnabledPriority());
            if (!HasCustomSchedule(runtimeGlobalTarget, globalFallback))
            {
                return false;
            }

            target = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
            fallbackPriority = globalFallback;
            return true;
        }

        internal static bool SetPriorityAtHourSynced(TimePriorityTarget target, int hour, int priority, int fallbackPriority)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out _))
            {
                return false;
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
                return true;
            }

            return SetPriorityAtHour(target, hour, priority, fallbackPriority);
        }

        internal static bool ClearPriorityAtHourSynced(TimePriorityTarget target, int hour)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out _))
            {
                return false;
            }

            if (MultiplayerBridge.Active)
            {
                SyncClearPriorityAtHour(
                    target.PawnId,
                    (int)target.Kind,
                    target.WorkTypeDefName,
                    target.TargetDefName,
                    hour);
                return true;
            }

            return ClearPriorityAtHour(target, hour);
        }

        internal static bool SetPrioritiesSynced(TimePriorityTarget target, int[] priorities, int fallbackPriority)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out _))
            {
                return false;
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
                return true;
            }

            return SetPriorities(target, normalizedPriorities, fallbackPriority);
        }

        /// <summary>
        /// Creates link state for a bulk source whose 24 values are all
        /// explicit. Numeric equality with the fallback must not turn one of
        /// those explicit values into an inherited hour.
        /// </summary>
        internal static bool[] CreateAllHoursPinnedState()
        {
            var pinned = new bool[HoursPerDay];
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                pinned[hour] = true;
            }

            return pinned;
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
        internal static bool SetScheduleSynced(
            TimePriorityTarget target,
            int[] priorities,
            bool[] unlinkedHours,
            int fallbackPriority)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out _))
            {
                return false;
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
                return true;
            }

            return SetSchedule(target, normalizedPriorities, pinnedHours, fallbackPriority);
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
            TimePriorityTargetKind kind = Enum.IsDefined(typeof(TimePriorityTargetKind), kindValue)
                ? (TimePriorityTargetKind)kindValue
                : TimePriorityTargetKind.WorkType;
            var target = TimePriorityTarget.FromRaw(pawnId, kind, workTypeDefName, targetDefName);
            SetSchedule(target, priorities, pinnedHours, fallbackPriority);
        }

        private static bool SetSchedule(
            TimePriorityTarget target,
            int[] priorities,
            int[] pinnedHours,
            int fallbackPriority)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }

            return SetSchedule(
                target,
                priorities,
                pinnedHours,
                fallbackPriority,
                authorityRevision);
        }

        private static bool SetSchedule(
            TimePriorityTarget target,
            int[] priorities,
            int[] pinnedHours,
            int fallbackPriority,
            long authorityRevision)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            int[] normalizedPriorities = NormalizePriorities(priorities, fallbackPriority);
            if (pinnedHours == null || pinnedHours.Length == 0)
            {
                return ClearSchedule(target, authorityRevision);
            }

            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            var schedule = GetOrCreateSchedule(target, fallbackPriority);
            bool changed = false;
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                bool shouldPin = Array.IndexOf(pinnedHours, hour) >= 0;
                bool wasPinned = schedule.IsUnlinked(hour);
                if (shouldPin && (!wasPinned || schedule.HourlyPriorities[hour] != normalizedPriorities[hour]))
                {
                    if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
                    {
                        return false;
                    }

                    schedule.SetOverride(hour, normalizedPriorities[hour]);
                    changed = true;
                }
                else if (!shouldPin && wasPinned)
                {
                    if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
                    {
                        return false;
                    }

                    schedule.ClearOverride(hour);
                    changed = true;
                }
            }

            if (changed)
            {
                if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
                {
                    return false;
                }

                MirrorTargetToExternalWorkTab(target);
                NotifyChanged();
            }

            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
        }

        internal static bool ClearSchedule(TimePriorityTarget target)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }

            return ClearSchedule(target, authorityRevision);
        }

        private static bool ClearSchedule(TimePriorityTarget target, long authorityRevision)
        {
            var schedules = GetSchedules(create: false);
            if (schedules == null)
            {
                return true;
            }

            bool changed = false;
            for (int i = schedules.Count - 1; i >= 0; i--)
            {
                TimePriorityScheduleData schedule = schedules[i];
                if (schedule == null || schedule.Key == target.Key)
                {
                    if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
                    {
                        return false;
                    }

                    schedules.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
                {
                    return false;
                }

                MirrorTargetToExternalWorkTab(target);
                NotifyChanged();
            }

            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
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
            TimePriorityTargetKind kind = Enum.IsDefined(typeof(TimePriorityTargetKind), kindValue)
                ? (TimePriorityTargetKind)kindValue
                : TimePriorityTargetKind.WorkType;
            var target = TimePriorityTarget.FromRaw(pawnId, kind, workTypeDefName, targetDefName);
            SetPriorities(target, priorities, fallbackPriority);
        }

        private static bool SetPriorityAtHour(TimePriorityTarget target, int hour, int priority, int fallbackPriority)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);

            // No early-out when the chosen number matches the box. Pinning an
            // hour to the default is a real instruction -- it means "stay here
            // when the box moves" -- and it is only expressible as an override.
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            var schedule = GetOrCreateSchedule(target, fallbackPriority);
            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            if (schedule.IsUnlinked(hour) && schedule.HourlyPriorities[hour] == priority)
            {
                return true;
            }

            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            schedule.SetOverride(hour, priority);
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            MirrorTargetToExternalWorkTab(target);
            NotifyChanged();
            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
        }

        /// <summary>
        /// Puts an hour back under the priority box, so it follows whatever the
        /// box says from now on.
        /// </summary>
        private static bool ClearPriorityAtHour(TimePriorityTarget target, int hour)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }

            if (!TryGetSchedule(target, out var schedule))
            {
                return true;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            if (!schedule.ClearOverride(hour))
            {
                return true;
            }

            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            // A schedule with nothing pinned is indistinguishable from no
            // schedule, and leaving the row behind would keep reporting the
            // target as scheduled to every indicator that asks.
            if (!schedule.HasAnyUnlinkedHour)
            {
                return ClearSchedule(target, authorityRevision);
            }

            MirrorTargetToExternalWorkTab(target);
            NotifyChanged();
            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
        }

        private static bool SetPriorities(TimePriorityTarget target, int[] priorities, int fallbackPriority)
        {
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }

            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            int[] normalizedPriorities = NormalizePriorities(priorities, fallbackPriority);
            var pinnedHours = new List<int>();
            for (int hour = 0; hour < normalizedPriorities.Length; hour++)
            {
                // Numeric bulk writes carry no link state, so a value that differs
                // from the clamped fallback is the only value that can infer a pin.
                if (normalizedPriorities[hour] != fallbackPriority)
                {
                    pinnedHours.Add(hour);
                }
            }

            return SetSchedule(
                target,
                normalizedPriorities,
                pinnedHours.ToArray(),
                fallbackPriority,
                authorityRevision);
        }

        /// <summary>
        /// Republishes an hourly schedule to any external work-tab mod backing the priority numbers.
        /// Hourly schedules live only in Better Work Tab, so nothing else propagates them.
        /// </summary>
        private static void MirrorTargetToExternalWorkTab(TimePriorityTarget target)
        {
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

        internal static bool IsRuntimeEnabled =>
            BetterWorkTabMod.Settings?.enableTimePrioritySchedules ??
            DefaultSettings.enableTimePrioritySchedules;

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

            TimePriorityTarget target = TimePriorityTarget.ForRuntimeWorkType(pawn, workType);
            int hour = GetCurrentHour(pawn);

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

            int hour = GetCurrentHour(pawn);

            TimePriorityTarget pawnTarget = TimePriorityTarget.ForRuntimeWorkGiver(pawn, workType, workGiver);
            if (pawn != null && TryGetScheduledPriority(pawnTarget, hour, out int pawnScheduledPriority))
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

            TimePriorityTarget globalTarget = TimePriorityTarget.ForRuntimeWorkGiver(null, workType, workGiver);
            if (TryGetScheduledPriority(globalTarget, hour, out int globalScheduledPriority))
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

        internal static string FormatHour(int hour)
        {
            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            return hour.ToString("00") + ":00";
        }

        internal static void NotifyHourBoundaryIfNeeded()
        {
            if (!HasAnySchedule())
            {
                return;
            }

            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.ScheduleHour);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
        }

        internal static void NotifyLoaded()
        {
            EnsureGameState();
            List<TimePriorityScheduleData> schedules = GetSchedules(create: true);
            bool collectionChanged = HasScheduleCollectionChangedFromAudit(schedules);
            bool normalized = NormalizeScheduleCollection(schedules);
            bool migrated = MigrateLinkStateFromLegacySaves(schedules);

            // FinalizeInit and PostLoadInit can both reach this boundary. A
            // genuinely loaded schedule publishes once; an empty vanilla save
            // only prepares the cache and must not dirty every pawn just because
            // the component has no feature data to publish.
            bool publish = collectionChanged || normalized || migrated ||
                           (!_loadBoundaryHandled && schedules != null && schedules.Count > 0);
            _loadBoundaryHandled = true;
            if (publish)
            {
                PublishScheduleChange(schedules, rebuildCache: true);
            }
            else
            {
                Cache.Clear();
                _cachedVersion = -1;
                RebuildRuntimeCache(schedules, normalize: false);
                RecordScheduleCollectionState(schedules);
            }
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
        private static bool MigrateLinkStateFromLegacySaves(List<TimePriorityScheduleData> schedules)
        {
            if (schedules == null)
            {
                return false;
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

                schedule.NeedsLinkMigration = false;
                schedule.EnsureValid();
                int fallback = ResolveFallbackPriority(schedule);
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
                return true;
            }

            return false;
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
            if (!TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            if (!schedule.IsUnlinked(hour))
            {
                // A linked hour has no scheduled priority of its own; callers
                // without a fallback to hand cannot be told what it resolves to.
                return false;
            }

            if (!schedule.TryGetStoredPriority(hour, out priority))
            {
                return false;
            }

            return true;
        }

        private static TimePriorityScheduleData GetOrCreateSchedule(TimePriorityTarget target, int fallbackPriority)
        {
            EnsureCache();
            if (Cache.TryGetValue(target.CacheKey, out var schedule))
            {
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
            return schedule;
        }

        private static bool TryGetSchedule(TimePriorityTarget target, out TimePriorityScheduleData schedule)
        {
            EnsureCache();
            if (!Cache.TryGetValue(target.CacheKey, out schedule))
            {
                return false;
            }

            return true;
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
            RefreshScheduleActivity();
            if (_cachedVersion == CurrentVersion)
            {
                return;
            }

            // Lifecycle, mutation, save, and external-audit boundaries own
            // normalization. A cache rebuild is deliberately only a key index
            // rebuild so warm reads never walk or repair every schedule.
            RebuildRuntimeCache(GetSchedules(create: false), normalize: false);
        }

        private static void RebuildRuntimeCache(
            List<TimePriorityScheduleData> schedules,
            bool normalize)
        {
            Cache.Clear();
            if (schedules != null)
            {
                for (int i = schedules.Count - 1; i >= 0; i--)
                {
                    TimePriorityScheduleData schedule = schedules[i];
                    if (schedule == null)
                    {
                        schedules.RemoveAt(i);
                        continue;
                    }

                    if (normalize)
                    {
                        schedule.EnsureValid();
                    }

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

        private static void NotifyChanged()
        {
            if (_mutationBatchDepth > 0)
            {
                QueueMutation();
                return;
            }

            PublishScheduleChange(GetSchedules(create: false), rebuildCache: false);
        }

        private static void QueueMutation()
        {
            _mutationBatchChanged = true;
            _cachedVersion = -1;
            Cache.Clear();
            _scheduleActivityKnown = false;
        }

        internal static void NormalizeBeforeSave()
        {
            EnsureGameState();
            List<TimePriorityScheduleData> schedules = GetSchedules(create: true);
            bool collectionChanged = HasScheduleCollectionChangedFromAudit(schedules);
            bool normalized = NormalizeScheduleCollection(schedules);
            if (collectionChanged || normalized)
            {
                PublishScheduleChange(schedules, rebuildCache: true);
                return;
            }

            RecordScheduleCollectionState(schedules);
        }

        /// <summary>
        /// Reconciles the public schedule collection at the existing
        /// low-frequency compatibility-audit boundary. Normal priority reads
        /// use per-list version sentinels and never call this scan.
        /// </summary>
        internal static void ReconcileDirectMutationsFromAudit()
        {
            EnsureGameState();
            List<TimePriorityScheduleData> schedules = GetSchedules(create: false);
            bool active = schedules != null && schedules.Count > 0;
            if (!_scheduleActivityKnown)
            {
                _scheduleDataActive = active;
                _scheduleActivityKnown = true;
            }

            if (!_scheduleCollectionStateKnown)
            {
                RecordScheduleCollectionState(schedules);
            }

            bool collectionChanged = HasScheduleCollectionChangedFromAudit(schedules);
            bool nestedDataChanged = NormalizeScheduleCollection(schedules);
            if (collectionChanged || nestedDataChanged)
            {
                PublishScheduleChange(schedules, rebuildCache: true);
                return;
            }

            // Keep the outer snapshot current even when the audit found no
            // semantic change.
            RecordScheduleCollectionState(schedules);
        }

        private static void PublishScheduleChange(
            List<TimePriorityScheduleData> schedules,
            bool rebuildCache)
        {
            PublishScheduleChange(schedules, rebuildCache, notifyConsumers: true);
        }

        private static void PublishScheduleChange(
            List<TimePriorityScheduleData> schedules,
            bool rebuildCache,
            bool notifyConsumers)
        {
            CurrentVersion++;
            _cachedVersion = -1;
            Cache.Clear();
            _scheduleDataActive = schedules != null && schedules.Count > 0;
            _scheduleActivityKnown = true;
            if (rebuildCache)
            {
                RebuildRuntimeCache(schedules, normalize: false);
            }

            RecordScheduleCollectionState(schedules);

            if (!notifyConsumers)
            {
                return;
            }

            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.ScheduleHour);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        private static bool RefreshScheduleActivity()
        {
            EnsureGameState();
            List<TimePriorityScheduleData> schedules = GetSchedules(create: false);
            bool active = schedules != null && schedules.Count > 0;
            if (!_scheduleActivityKnown)
            {
                _scheduleDataActive = active;
                _scheduleActivityKnown = true;
            }

            if (_mutationBatchDepth > 0)
            {
                _scheduleDataActive = active;
                _scheduleActivityKnown = true;
                return _scheduleDataActive;
            }

            if (!_scheduleCollectionStateKnown)
            {
                RecordScheduleCollectionState(schedules);
            }

            return _scheduleDataActive;
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

        private static void EnsureGameState()
        {
            Game game = Current.Game;
            if (ReferenceEquals(_cachedGame, game))
            {
                return;
            }

            _cachedGame = game;
            Cache.Clear();
            _cachedVersion = -1;
            CurrentVersion++;
            _scheduleActivityKnown = false;
            _scheduleDataActive = false;
            _observedSchedules = null;
            _observedScheduleEntries = null;
            _observedScheduleCount = -1;
            _scheduleCollectionStateKnown = false;
            _loadBoundaryHandled = false;
        }

        private static bool HasScheduleCollectionChangedFromAudit(
            List<TimePriorityScheduleData> schedules)
        {
            if (!_scheduleCollectionStateKnown)
            {
                return false;
            }

            if (!ReferenceEquals(_observedSchedules, schedules) ||
                _observedScheduleCount != (schedules?.Count ?? 0))
            {
                return true;
            }

            if (schedules == null)
            {
                return _observedScheduleEntries != null;
            }

            if (_observedScheduleEntries == null ||
                _observedScheduleEntries.Length != schedules.Count)
            {
                return true;
            }

            for (int i = 0; i < schedules.Count; i++)
            {
                if (!ReferenceEquals(_observedScheduleEntries[i], schedules[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RecordScheduleCollectionState(List<TimePriorityScheduleData> schedules)
        {
            _observedSchedules = schedules;
            _observedScheduleCount = schedules?.Count ?? 0;
            if (schedules != null)
            {
                if (_observedScheduleEntries == null ||
                    _observedScheduleEntries.Length != schedules.Count)
                {
                    _observedScheduleEntries =
                        new TimePriorityScheduleData[schedules.Count];
                }

                for (int i = 0; i < schedules.Count; i++)
                {
                    _observedScheduleEntries[i] = schedules[i];
                }
            }
            else
            {
                _observedScheduleEntries = null;
            }

            _scheduleCollectionStateKnown = true;
        }

        private static bool NormalizeScheduleCollection(List<TimePriorityScheduleData> schedules)
        {
            if (schedules == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = schedules.Count - 1; i >= 0; i--)
            {
                TimePriorityScheduleData schedule = schedules[i];
                if (schedule == null)
                {
                    schedules.RemoveAt(i);
                    changed = true;
                    continue;
                }

                changed |= schedule.ReconcileRuntimeIntegrityFromAudit();
            }

            return changed;
        }

        // Kept for the existing invalidation-audit seam. Runtime reads use the
        // cached mask; this low-frequency fingerprint includes link state even
        // when a pinned number equals the current fallback.
        internal static int ComputePresentationAuditSignature()
        {
            unchecked
            {
                int hash = 17;
                List<TimePriorityScheduleData> schedules = GetSchedules(create: false);
                if (schedules == null)
                {
                    return hash;
                }

                hash = (hash * 397) ^ schedules.Count;

                for (int i = 0; i < schedules.Count; i++)
                {
                    TimePriorityScheduleData schedule = schedules[i];
                    if (schedule == null)
                    {
                        hash = (hash * 397) ^ 0;
                        continue;
                    }

                    hash = (hash * 397) ^ schedule.PawnId;
                    hash = (hash * 397) ^ (int)schedule.Kind;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(schedule.WorkTypeDefName ?? string.Empty);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(schedule.TargetDefName ?? string.Empty);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(schedule.Key ?? string.Empty);

                    List<int> hourlyPriorities = schedule.HourlyPriorities;
                    hash = (hash * 397) ^ (hourlyPriorities?.Count ?? -1);
                    if (hourlyPriorities != null)
                    {
                        int hourlyCount = Math.Min(hourlyPriorities.Count, HoursPerDay);
                        for (int hour = 0; hour < hourlyCount; hour++)
                        {
                            hash = (hash * 397) ^ hourlyPriorities[hour];
                        }
                    }

                    List<int> unlinkedHours = schedule.UnlinkedHours;
                    hash = (hash * 397) ^ (unlinkedHours?.Count ?? -1);
                    if (unlinkedHours != null)
                    {
                        int unlinkedCount = Math.Min(unlinkedHours.Count, HoursPerDay);
                        for (int hour = 0; hour < unlinkedCount; hour++)
                        {
                            hash = (hash * 397) ^ (unlinkedHours[hour] + 1);
                        }
                    }
                }

                return hash;
            }
        }

    }
}
