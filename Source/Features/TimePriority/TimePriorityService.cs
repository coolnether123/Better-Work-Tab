using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
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
            if (!IsRuntimeEnabled() || pawn == null || workType == null)
            {
                return false;
            }

            int hour = GetCurrentHour(pawn);
            int baseWorkTypePriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            var workTypeTarget = TimePriorityTarget.ForWorkType(pawn, workType);
            if (TryGetSchedule(workTypeTarget, out var workTypeSchedule) &&
                WorkPrioritySystem.ClampPriority(workTypeSchedule.HourlyPriorities[hour]) <= WorkPrioritySystem.DisabledPriority &&
                baseWorkTypePriority > WorkPrioritySystem.DisabledPriority)
            {
                disabledTarget = workTypeTarget;
                reason = "disabled by time priority for " + FormatHour(hour);
                return true;
            }

            if (workGiver != null)
            {
                int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, baseWorkTypePriority);
                var pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
                if (TryGetSchedule(pawnTarget, out var pawnSchedule) &&
                    WorkPrioritySystem.ClampPriority(pawnSchedule.HourlyPriorities[hour]) <= WorkPrioritySystem.DisabledPriority &&
                    baseWorkGiverPriority > WorkPrioritySystem.DisabledPriority)
                {
                    disabledTarget = pawnTarget;
                    reason = "disabled by time priority for " + FormatHour(hour);
                    return true;
                }

                var globalTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
                if (TryGetSchedule(globalTarget, out var globalSchedule) &&
                    WorkPrioritySystem.ClampPriority(globalSchedule.HourlyPriorities[hour]) <= WorkPrioritySystem.DisabledPriority &&
                    baseWorkGiverPriority > WorkPrioritySystem.DisabledPriority)
                {
                    disabledTarget = globalTarget;
                    reason = "disabled by global time priority for " + FormatHour(hour);
                    return true;
                }
            }

            return false;
        }

        internal static int GetEffectiveWorkTypePriority(Pawn pawn, WorkTypeDef workType, int basePriority)
        {
            basePriority = WorkPrioritySystem.ClampPriority(basePriority);
            if (!IsRuntimeEnabled() || pawn == null || workType == null)
            {
                return basePriority;
            }

            if (basePriority <= WorkPrioritySystem.DisabledPriority)
            {
                return basePriority;
            }

            var target = TimePriorityTarget.ForWorkType(pawn, workType);
            return GetPriorityAtCurrentHourIfScheduled(pawn, target, basePriority);
        }

        internal static int GetEffectiveWorkGiverPriority(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int basePriority)
        {
            basePriority = WorkPrioritySystem.ClampPriority(basePriority);
            if (!IsRuntimeEnabled() || workGiver == null)
            {
                return basePriority;
            }

            if (basePriority <= WorkPrioritySystem.DisabledPriority)
            {
                return basePriority;
            }

            if (pawn != null)
            {
                var pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workType, workGiver);
                if (TryGetSchedule(pawnTarget, out _))
                {
                    return GetPriorityAtCurrentHourIfScheduled(pawn, pawnTarget, basePriority);
                }
            }

            var globalTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiver);
            return GetPriorityAtCurrentHourIfScheduled(pawn, globalTarget, basePriority);
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

            int ticks = Find.TickManager?.TicksAbs ?? 0;
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

            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
        }

        internal static void NotifyLoaded()
        {
            Cache.Clear();
            _cachedVersion = -1;
            CurrentVersion++;
        }

        private static int GetPriorityAtCurrentHourIfScheduled(Pawn pawn, TimePriorityTarget target, int fallbackPriority)
        {
            if (!TryGetSchedule(target, out var schedule))
            {
                return WorkPrioritySystem.ClampPriority(fallbackPriority);
            }

            int hour = GetCurrentHour(pawn);
            return WorkPrioritySystem.ClampPriority(schedule.HourlyPriorities[hour]);
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
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        private static bool IsRuntimeEnabled()
        {
            return BetterWorkTabMod.Settings?.enableTimePriorityPlannerPrototype ??
                   DefaultSettings.enableTimePriorityPlannerPrototype;
        }
    }
}
