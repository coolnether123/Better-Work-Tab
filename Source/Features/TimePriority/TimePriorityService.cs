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
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            if (!TryGetSchedule(target, out var schedule))
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
            if (!TryGetSchedule(target, out var schedule))
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
            if (!TryGetSchedule(target, out var schedule))
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
            if (!TryGetSchedule(target, out var schedule))
            {
                return false;
            }

            schedule.EnsureValid();
            return schedule.HasAnyUnlinkedHour;
        }

        internal static bool IsCustomScheduledHour(TimePriorityTarget target, int hour, int fallbackPriority)
        {
            if (!TryGetSchedule(target, out var schedule))
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

        internal static void ClearPriorityAtHourSynced(TimePriorityTarget target, int hour)
        {
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

        private static void SetPriorityAtHour(TimePriorityTarget target, int hour, int priority, int fallbackPriority)
        {
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
            Cache.Clear();
            _cachedVersion = -1;
            CurrentVersion++;
            MigrateLinkStateFromLegacySaves();
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
                Cache.Clear();
                _cachedVersion = -1;
                CurrentVersion++;
            }
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
                    // Link state is presentation, not just bookkeeping: an hour
                    // changing from following the box to pinned repaints it even
                    // when the number it shows is identical.
                    if (schedule.UnlinkedHours != null)
                    {
                        for (int j = 0; j < schedule.UnlinkedHours.Count; j++)
                        {
                            hash = (hash * 397) ^ (schedule.UnlinkedHours[j] + 1);
                        }
                    }

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
