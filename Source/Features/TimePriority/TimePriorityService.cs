using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.Mod_Support.Multiplayer;
#if !v1_2 && !v1_1 && !v1_0 && !v0_19
using Multiplayer.API;
#endif
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    internal static class TimePriorityService
    {
        internal const int HoursPerDay = 24;
        private static readonly Dictionary<string, TimePriorityScheduleData> Cache =
            new Dictionary<string, TimePriorityScheduleData>(StringComparer.Ordinal);
        private static int _cachedVersion = -1;

        internal static int CurrentVersion { get; private set; }

        internal static string BuildKey(int pawnId, TimePriorityTargetKind kind, string workTypeDefName, string targetDefName)
        {
            return pawnId + ":" + kind + ":" + (workTypeDefName ?? string.Empty) + ":" + (targetDefName ?? string.Empty);
        }

        internal static bool HasAnySchedule()
        {
            var schedules = GetSchedules(create: false);
            return schedules != null && schedules.Count > 0;
        }

        internal static int[] GetPrioritiesForDisplay(TimePriorityTarget target, int fallbackPriority)
        {
            if (!TryGetSchedule(target, out var schedule))
            {
                return CreateFallbackPriorities(fallbackPriority);
            }

            return schedule.HourlyPriorities.ToArray();
        }

        internal static int GetPriorityAtHour(TimePriorityTarget target, int fallbackPriority, int hour)
        {
            if (!TryGetSchedule(target, out var schedule))
            {
                return WorkPrioritySystem.ClampPriority(fallbackPriority);
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            return WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour]);
        }

        internal static bool HasCustomSchedule(TimePriorityTarget target, int fallbackPriority)
        {
            if (!TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            schedule.EnsureValid();
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                if (WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour]) != fallbackPriority)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsCustomScheduledHour(TimePriorityTarget target, int hour, int fallbackPriority)
        {
            if (!TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            schedule.EnsureValid();
            return WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour]) != fallbackPriority;
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
            if (!IsRuntimeEnabled || workType == null || workGiver == null)
            {
                return false;
            }

            if (pawn != null)
            {
                TimePriorityTarget pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
                if (HasCustomSchedule(pawnTarget, fallbackPriority))
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
            if (!HasCustomSchedule(globalTarget, globalFallback))
            {
                return false;
            }

            target = globalTarget;
            fallbackPriority = globalFallback;
            return true;
        }

        internal static void SetPriorityAtHourSynced(TimePriorityTarget target, int hour, int priority, int fallbackPriority)
        {
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

        internal static void SetPrioritiesSynced(TimePriorityTarget target, int[] priorities, int fallbackPriority)
        {
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

        internal static void ClearSchedule(TimePriorityTarget target)
        {
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

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
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

#if !v1_2 && !v1_1 && !v1_0 && !v0_19
        [SyncMethod]
#endif
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

        private static void SetPriorityAtHour(TimePriorityTarget target, int hour, int priority, int fallbackPriority)
        {
            priority = WorkPrioritySystem.ClampPriority(priority);
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            if (!TryGetSchedule(target, out _) && priority == fallbackPriority)
            {
                return;
            }

            var schedule = GetOrCreateSchedule(target, fallbackPriority);
            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            if (schedule.HourlyPriorities[hour] == priority)
            {
                return;
            }

            schedule.HourlyPriorities[hour] = priority;
            MirrorTargetToExternalWorkTab(target);
            NotifyChanged();
        }

        private static void SetPriorities(TimePriorityTarget target, int[] priorities, int fallbackPriority)
        {
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
                if (schedule.HourlyPriorities[i] == normalizedPriorities[i])
                {
                    continue;
                }

                schedule.HourlyPriorities[i] = normalizedPriorities[i];
                changed = true;
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
            foreach (Pawn pawn in PawnsFinderCompat.AllAliveOrDead)
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
            if (!IsRuntimeEnabled || pawn == null || workType == null)
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
            TimePriorityTarget target = TimePriorityTarget.ForWorkType(pawn, workType);
            int hour = GetCurrentHour(pawn);
            if (!IsRuntimeEnabled || pawn == null || workType == null)
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
            int hour = GetCurrentHour(pawn);
            if (!IsRuntimeEnabled || workGiver == null)
            {
                return new TimePriorityEvaluation(
                    TimePriorityTarget.FromRaw(
                        pawn?.thingIDNumber ?? TimePriorityTarget.GlobalPawnId,
                        TimePriorityTargetKind.WorkGiver,
                        workType?.defName,
                        workGiver?.defName,
                        workType?.labelShort?.CapitalizeFirst() ?? "Sub-work"),
                    hour,
                    basePriority,
                    basePriority,
                    false,
                    basePriority,
                    TimePriorityScheduleScopes.None);
            }

            TimePriorityTarget pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
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

            TimePriorityTarget globalTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
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
#if !(v0_15 || v0_14 || v0_13 || vAlpha4)
            try
            {
                if (pawn != null)
                {
                    return Mathf.Clamp(TimeCompat.HourOfDay(pawn), 0, HoursPerDay - 1);
                }
            }
            catch
            {
                // Fall back to absolute game ticks when local date APIs are unavailable.
            }
#endif

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
            Cache.Clear();
            _cachedVersion = -1;
            CurrentVersion++;
        }

        internal static bool TryGetScheduledPriority(TimePriorityTarget target, int hour, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            if (!TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            priority = WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour]);
            return true;
        }

        private static TimePriorityScheduleData GetOrCreateSchedule(TimePriorityTarget target, int fallbackPriority)
        {
            EnsureCache();
            if (Cache.TryGetValue(target.Key, out var schedule))
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
            Cache[schedule.Key] = schedule;
            NotifyChanged();
            return schedule;
        }

        private static bool TryGetSchedule(TimePriorityTarget target, out TimePriorityScheduleData schedule)
        {
            EnsureCache();
            return Cache.TryGetValue(target.Key, out schedule);
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
                    Cache[schedule.Key] = schedule;
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
            CurrentVersion++;
            _cachedVersion = -1;
            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.ScheduleHour);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        internal static int ComputePresentationAuditSignature()
        {
            unchecked
            {
                int hash = 17;
                var schedules = GetSchedules(create: false);
                if (schedules == null)
                {
                    return hash;
                }

                for (int i = 0; i < schedules.Count; i++)
                {
                    TimePriorityScheduleData schedule = schedules[i];
                    if (schedule == null) continue;
                    hash = (hash * 397) ^ schedule.PawnId;
                    hash = (hash * 397) ^ (int)schedule.Kind;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(schedule.WorkTypeDefName ?? string.Empty);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(schedule.TargetDefName ?? string.Empty);
                    if (schedule.HourlyPriorities == null) continue;
                    for (int hour = 0; hour < schedule.HourlyPriorities.Count; hour++)
                    {
                        hash = (hash * 397) ^ schedule.HourlyPriorities[hour];
                    }
                }
                return hash;
            }
        }

    }
}
