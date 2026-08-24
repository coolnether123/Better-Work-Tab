using System;
using System.Collections.Generic;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.Mod_Support.Multiplayer;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    internal static class TimePriorityService
    {
        internal const int HoursPerDay = 24;
        private static readonly Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> EmptySchedules =
            new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>();
        private static TimePriorityScheduleRuntime State =>
            Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.ScheduleRuntime;
        private static Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> RuntimeSchedules =>
            State?.RuntimeSchedules ?? EmptySchedules;
        private static Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> LegacySchedules =>
            State?.LegacySchedules ?? EmptySchedules;
        private static WorkTypeScheduleReadSnapshot WorkTypeReadSnapshot
        {
            get => State?.WorkTypeReadSnapshot;
            set { if (State != null) State.WorkTypeReadSnapshot = value; }
        }
        private static int WorkTypeReadSnapshotVersion
        {
            get => State?.WorkTypeReadSnapshotVersion ?? -1;
            set { if (State != null) State.WorkTypeReadSnapshotVersion = value; }
        }
        private static bool RuntimeSchedulesKnown
        {
            get => State?.RuntimeSchedulesKnown ?? false;
            set { if (State != null) State.RuntimeSchedulesKnown = value; }
        }
        private static bool LoadBoundaryHandled
        {
            get => State?.LoadBoundaryHandled ?? false;
            set { if (State != null) State.LoadBoundaryHandled = value; }
        }
        private static int MutationBatchDepth
        {
            get => State?.MutationBatchDepth ?? 0;
            set { if (State != null) State.MutationBatchDepth = value; }
        }
        private static bool MutationBatchChanged
        {
            get => State?.MutationBatchChanged ?? false;
            set { if (State != null) State.MutationBatchChanged = value; }
        }
        internal static bool HasActiveMutationBatch => MutationBatchDepth != 0;

        internal sealed class WorkTypeScheduleReadSnapshot
        {
            internal static readonly WorkTypeScheduleReadSnapshot Empty =
                new WorkTypeScheduleReadSnapshot(null);
            private readonly IDictionary<TimePriorityCacheKey, TimePriorityScheduleValue> _schedules;
            private WorkTypeScheduleReadSnapshot(IDictionary<TimePriorityCacheKey, TimePriorityScheduleValue> schedules) =>
                _schedules = schedules;

            internal bool TryGetPinnedPriority(Pawn pawn, WorkTypeDef workType, int hour, out int priority)
            {
                priority = WorkPrioritySystem.DisabledPriority;
                return pawn != null && workType != null && hour >= 0 && hour < HoursPerDay &&
                    _schedules != null && _schedules.TryGetValue(
                        TimePriorityTarget.ForWorkType(pawn, workType).CacheKey,
                        out TimePriorityScheduleValue schedule) && schedule.IsPinned(hour) &&
                    (priority = schedule.PriorityAt(hour)) >= WorkPrioritySystem.DisabledPriority;
            }

            internal static WorkTypeScheduleReadSnapshot Capture(
                IDictionary<TimePriorityCacheKey, TimePriorityScheduleValue> schedules)
            {
                if (schedules == null || schedules.Count == 0) return Empty;
                var captured = new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>();
                foreach (KeyValuePair<TimePriorityCacheKey, TimePriorityScheduleValue> entry in schedules)
                {
                    if (entry.Key.Kind == TimePriorityTargetKind.WorkType &&
                        entry.Value != null && entry.Value.HasPinnedHours)
                    {
                        captured[entry.Key] = entry.Value;
                    }
                }
                return captured.Count == 0 ? Empty : new WorkTypeScheduleReadSnapshot(captured);
            }

        }

        internal static int CurrentVersion
        {
            get => State?.CurrentVersion ?? 0;
            private set { if (State != null) State.CurrentVersion = value; }
        }

        internal static bool IsRuntimeActive => IsRuntimeEnabled && HasAnySchedule();

        internal static string BuildKey(int pawnId, TimePriorityTargetKind kind, string workTypeDefName, string targetDefName)
        {
            return pawnId + ":" + kind + ":" + (workTypeDefName ?? string.Empty) + ":" + (targetDefName ?? string.Empty);
        }

        internal static bool HasAnySchedule()
        {
            return RuntimeSchedules.Count != 0;
        }

        internal static IDisposable BeginMutationBatch()
        {
            if (MutationBatchDepth == 0)
            {
                MutationBatchChanged = false;
            }

            MutationBatchDepth++;
            return new MutationBatchScope();
        }

        internal static bool CommitMutationBatch()
        {
            return CompleteMutationBatch();
        }

        /// <summary>
        /// Drops a completed transaction after its exact rollback restored the
        /// original schedule state. This never advances a revision or emits a
        /// consumer notification.
        /// </summary>
        internal static bool DiscardMutationBatch()
        {
            if (MutationBatchDepth != 0 || !MutationBatchChanged)
            {
                return false;
            }

            MutationBatchChanged = false;
            MaterializeScheduleProjection();
            InvalidateWorkTypeReadSnapshot();
            return true;
        }

        private static bool CompleteMutationBatch()
        {
            if (MutationBatchDepth != 0 || !MutationBatchChanged)
            {
                return false;
            }

            MutationBatchChanged = false;
            MaterializeScheduleProjection();
            AdvanceScheduleRevision();
            return true;
        }

        /// <summary>
        /// Captures the authoritative 24-hour state for one identity.  Linked
        /// hours resolve to the supplied fallback but remain unpinned in the
        /// returned value, so callers never infer pin state from numeric
        /// equality.
        /// </summary>
        internal static TimePriorityScheduleValue ReadLiveSchedule(
            TimePriorityTarget target,
            int fallbackPriority)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            return RuntimeSchedules.TryGetValue(target.CacheKey, out TimePriorityScheduleValue schedule)
                ? ResolveScheduleValue(schedule, fallbackPriority)
                : CreateFallbackValue(fallbackPriority);
        }

        internal static int GetLivePriorityAtHour(
            TimePriorityTarget target,
            int fallbackPriority,
            int hour)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            return RuntimeSchedules.TryGetValue(target.CacheKey, out TimePriorityScheduleValue schedule) &&
                   schedule.IsPinned(hour)
                ? schedule.PriorityAt(hour)
                : fallbackPriority;
        }

        internal static bool HasLiveCustomSchedule(TimePriorityTarget target)
        {
            return RuntimeSchedules.TryGetValue(target.CacheKey, out TimePriorityScheduleValue schedule) &&
                   schedule.HasPinnedHours;
        }

        internal static bool TryGetLiveWorkTypeScheduledPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int hour,
            out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            return IsRuntimeEnabled && pawn != null && workType != null &&
                RuntimeSchedules.TryGetValue(
                    TimePriorityTarget.ForWorkType(pawn, workType).CacheKey,
                    out TimePriorityScheduleValue schedule) &&
                schedule.IsPinned(Mathf.Clamp(hour, 0, HoursPerDay - 1)) &&
                (priority = schedule.PriorityAt(Mathf.Clamp(hour, 0, HoursPerDay - 1))) >= 0;
        }

        internal static WorkTypeScheduleReadSnapshot CaptureLiveWorkTypeScheduleSnapshot()
        {
            if (!IsRuntimeEnabled) return WorkTypeScheduleReadSnapshot.Empty;
            int version = CurrentVersion;
            if (WorkTypeReadSnapshot != null && WorkTypeReadSnapshotVersion == version)
                return WorkTypeReadSnapshot;
            WorkTypeScheduleReadSnapshot snapshot = WorkTypeScheduleReadSnapshot.Capture(RuntimeSchedules);
            if (version != CurrentVersion) return WorkTypeScheduleReadSnapshot.Empty;
            WorkTypeReadSnapshot = snapshot;
            WorkTypeReadSnapshotVersion = version;
            return snapshot;
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
                    TimePriorityTarget.ForWorkGiver(pawn, workGiver);
                if (HasLiveCustomSchedule(runtimePawnTarget))
                {
                    // This target crosses into the editor when its ring is
                    // clicked, so resolve its display label only on the UI
                    // path that actually needs it.
                    target = TimePriorityTarget.ForWorkGiver(pawn, workGiver);
                    return true;
                }
            }

            TimePriorityTarget runtimeGlobalTarget =
                TimePriorityTarget.ForWorkGiver(null, workGiver);
            int globalFallback = WorkGiverReassignmentManager.GetWorkGiverPriority(
                null,
                workGiver,
                WorkPrioritySystem.GetDefaultEnabledPriority());
            if (!HasLiveCustomSchedule(runtimeGlobalTarget))
            {
                return false;
            }

            target = TimePriorityTarget.ForWorkGiver(null, workGiver);
            fallbackPriority = globalFallback;
            return true;
        }

        internal static TimePriorityScheduleValue CreateAllHoursPinnedValue(
            int[] priorities,
            int fallbackPriority)
        {
            return new TimePriorityScheduleValue(
                NormalizePriorities(priorities, fallbackPriority),
                TimePriorityScheduleValue.AllHoursMask);
        }

        internal static WorkTabApplicationResult SubmitSchedule(
            TimePriorityTarget target,
            TimePriorityScheduleValue desired,
            int fallbackPriority)
        {
            WorkTabApplication application = WorkTabApplication.Current;
            return application == null
                ? WorkTabApplicationResult.Rejected("The world application is unavailable.", default)
                : application.SubmitSchedule(target, desired, fallbackPriority);
        }

        /// <summary>
        /// Captures the live schedule baseline used by an exact writer.  This
        /// method never resolves an overlay or any presentation state.
        /// </summary>
        internal static bool TryCaptureLiveScheduleSnapshot(
            TimePriorityTarget target,
            int fallbackPriority,
            out TimePriorityLiveScheduleSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = null;
            if (!IsValidLiveTarget(target))
            {
                reason = "The live schedule target is invalid.";
                return false;
            }

            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                reason = "Better Work Tab does not currently own priority mutation authority.";
                return false;
            }

            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            bool hadSchedule = RuntimeSchedules.TryGetValue(
                target.CacheKey,
                out TimePriorityScheduleValue stored) && stored.HasPinnedHours;
            TimePriorityScheduleValue scheduleValue = ReadLiveSchedule(target, fallbackPriority);
            if (!scheduleValue.IsValid)
            {
                reason = "The live schedule service returned an invalid 24-hour state.";
                return false;
            }

            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                reason = "Priority mutation authority changed while the live schedule was captured.";
                return false;
            }

            snapshot = new TimePriorityLiveScheduleSnapshot(
                target,
                scheduleValue,
                hadSchedule,
                fallbackPriority,
                CurrentVersion,
                authorityRevision);
            return true;
        }

        /// <summary>
        /// Applies an exact 24-hour value after checking the captured live
        /// baseline and its authority and service revisions. A caller batching values may
        /// use <see cref="BeginMutationBatch"/> and <see cref="CommitMutationBatch"/>
        /// to publish a single revision.
        /// </summary>
        internal static TimePriorityScheduleMutationOutcome TryApplyLiveScheduleSnapshot(
            TimePriorityLiveScheduleSnapshot expectedCurrent,
            TimePriorityScheduleValue desired,
            out string reason,
            out bool changed)
        {
            reason = null;
            changed = false;
            if (desired == null || !desired.IsValid)
            {
                reason = "The desired schedule is not a complete 24-hour value.";
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            if (!TryValidateExpectedLiveSnapshot(
                    expectedCurrent,
                    out long authorityRevision,
                    out reason))
            {
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            TimePriorityScheduleMutationOutcome outcome = WriteSchedule(
                expectedCurrent.Target,
                desired,
                expectedCurrent.FallbackPriority,
                authorityRevision,
                out changed);
            if (outcome == TimePriorityScheduleMutationOutcome.Rejected)
            {
                reason = "The canonical hourly schedule service rejected the validated write.";
            }

            return outcome;
        }

        internal static TimePriorityScheduleMutationOutcome TryClearLiveScheduleSnapshot(
            TimePriorityLiveScheduleSnapshot expectedCurrent,
            out string reason,
            out bool changed)
        {
            reason = null;
            changed = false;
            if (!TryValidateExpectedLiveSnapshot(
                    expectedCurrent,
                    out long authorityRevision,
                    out reason))
            {
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            TimePriorityScheduleMutationOutcome outcome = WriteSchedule(
                expectedCurrent.Target,
                TimePriorityScheduleValue.AllLinked,
                expectedCurrent.FallbackPriority,
                authorityRevision,
                out changed);
            if (outcome == TimePriorityScheduleMutationOutcome.Rejected)
            {
                reason = "The canonical hourly schedule service rejected the validated clear.";
            }

            return outcome;
        }

        /// <summary>
        /// Restores the exact pre-transaction snapshot only when the caller's
        /// expected current live snapshot still matches. This is the rollback
        /// seam for a multi-target commit; it never writes through a stale or
        /// externally-owned authority.
        /// </summary>
        internal static TimePriorityScheduleMutationOutcome TryRestoreLiveScheduleSnapshot(
            TimePriorityLiveScheduleSnapshot snapshot,
            TimePriorityLiveScheduleSnapshot expectedCurrent,
            out string reason)
        {
            reason = null;
            if (snapshot == null || snapshot.Schedule == null || !snapshot.Schedule.IsValid)
            {
                reason = "The schedule rollback snapshot is invalid.";
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            if (!TryValidateExpectedLiveSnapshot(
                    expectedCurrent,
                    out long authorityRevision,
                    out reason))
            {
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            if (!snapshot.Target.Matches(expectedCurrent.Target))
            {
                reason = "The schedule rollback target does not match the validated live target.";
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            TimePriorityScheduleMutationOutcome outcome = WriteSchedule(
                snapshot.Target,
                snapshot.HadSchedule ? snapshot.Schedule : TimePriorityScheduleValue.AllLinked,
                snapshot.FallbackPriority,
                authorityRevision,
                out _);
            if (outcome == TimePriorityScheduleMutationOutcome.Rejected)
            {
                reason = "The canonical hourly schedule service rejected the rollback.";
            }

            return outcome;
        }

        /// <summary>Restores a transaction's own unpublished write without reopening authority validation.</summary>
        internal static bool RestoreUnpublishedScheduleSnapshot(TimePriorityLiveScheduleSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Schedule == null || !snapshot.Schedule.IsValid || !HasActiveMutationBatch)
            {
                return false;
            }

            TimePriorityCacheKey key = snapshot.Target.CacheKey;
            if (snapshot.HadSchedule && snapshot.Schedule.HasPinnedHours)
            {
                RuntimeSchedules[key] = snapshot.Schedule;
            }
            else
            {
                RuntimeSchedules.Remove(key);
            }

            LegacySchedules.Remove(key);
            RecordMutation();
            return true;
        }

        private static bool TryValidateExpectedLiveSnapshot(
            TimePriorityLiveScheduleSnapshot expectedCurrent,
            out long authorityRevision,
            out string reason)
        {
            authorityRevision = 0L;
            reason = null;
            if (expectedCurrent == null ||
                expectedCurrent.Schedule == null ||
                !expectedCurrent.Schedule.IsValid)
            {
                reason = "The expected live schedule snapshot is invalid.";
                return false;
            }

            if (CurrentVersion != expectedCurrent.ServiceVersion)
            {
                reason = "The live hourly schedule changed after the baseline was captured.";
                return false;
            }

            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(expectedCurrent.AuthorityRevision))
            {
                reason = "Priority mutation authority changed after the workload baseline was captured.";
                return false;
            }

            if (!TryCaptureLiveScheduleSnapshot(
                    expectedCurrent.Target,
                    expectedCurrent.FallbackPriority,
                    out TimePriorityLiveScheduleSnapshot current,
                    out reason))
            {
                return false;
            }

            if (!expectedCurrent.Matches(current) ||
                current.ServiceVersion != expectedCurrent.ServiceVersion ||
                current.AuthorityRevision != expectedCurrent.AuthorityRevision)
            {
                reason = current.AuthorityRevision != expectedCurrent.AuthorityRevision
                    ? "Priority mutation authority changed after the workload baseline was captured."
                    : "The live hourly schedule no longer matches the captured baseline.";
                return false;
            }

            authorityRevision = expectedCurrent.AuthorityRevision;
            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
        }

        private static bool IsValidLiveTarget(TimePriorityTarget target)
        {
            if ((target.PawnId != TimePriorityTarget.GlobalPawnId && target.PawnId < 0) ||
                (target.Kind != TimePriorityTargetKind.WorkType && target.Kind != TimePriorityTargetKind.WorkGiver) ||
                string.IsNullOrEmpty(target.WorkTypeDefName) ||
                (target.Kind == TimePriorityTargetKind.WorkType && target.IsGlobal) ||
                (target.Kind == TimePriorityTargetKind.WorkGiver && string.IsNullOrEmpty(target.TargetDefName)))
            {
                return false;
            }

            if (target.Kind != TimePriorityTargetKind.WorkGiver)
            {
                return true;
            }

            WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(target.TargetDefName);
            WorkTypeDef currentWorkType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            return currentWorkType != null && string.Equals(
                target.WorkTypeDefName,
                currentWorkType.defName,
                StringComparison.Ordinal);
        }

        private static int BuildPinnedHourMask(IList<int> pinnedHours)
        {
            int mask = 0;
            if (pinnedHours == null)
            {
                return mask;
            }

            for (int index = 0; index < pinnedHours.Count; index++)
            {
                int hour = pinnedHours[index];
                if (hour >= 0 && hour < HoursPerDay)
                {
                    mask |= 1 << hour;
                }
            }

            return mask;
        }

        private static TimePriorityScheduleMutationOutcome WriteSchedule(
            TimePriorityTarget target,
            TimePriorityScheduleValue value,
            int fallbackPriority,
            long authorityRevision,
            out bool changed)
        {
            changed = false;
            if (!TryPrepareTrustedScheduleMutation(out _) ||
                value == null || !value.IsValid ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            TimePriorityCacheKey key = target.CacheKey;
            if (!value.HasPinnedHours)
            {
                changed = RuntimeSchedules.Remove(key);
                changed |= LegacySchedules.Remove(key);
            }
            else
            {
                TimePriorityScheduleValue normalized = NormalizeScheduleValue(value, fallbackPriority);
                changed = !RuntimeSchedules.TryGetValue(key, out TimePriorityScheduleValue current) ||
                          !current.Equals(normalized);
                if (changed)
                {
                    RuntimeSchedules[key] = normalized;
                    LegacySchedules.Remove(key);
                }
            }

            if (changed)
            {
                bool authorityCurrent = WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
                RecordMutation();
                return authorityCurrent
                    ? TimePriorityScheduleMutationOutcome.Applied
                    : TimePriorityScheduleMutationOutcome.AppliedAfterAuthorityChange;
            }

            return TimePriorityScheduleMutationOutcome.Applied;
        }

        internal static PriorityMutationOutcome ApplyPriorityAtHour(
            TimePriorityTarget target,
            int hour,
            int priority,
            int fallbackPriority,
            ParentPriorityCommandExpectation expectation,
            Pawn_WorkSettings expectedWorkSettings,
            WorkTypeDef expectedWorkType)
        {
            if (!TryPrepareTrustedScheduleMutation(out _) ||
                (expectation == null
                ? !WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision)
                : !WorkPrioritySystem.TryCaptureExpectedStoredState(
                    expectedWorkSettings,
                    expectedWorkType,
                    expectation,
                    out authorityRevision)))
            {
                return PriorityMutationOutcome.Rejected;
            }

            if (expectation != null &&
                (!expectation.IsPinnedHour(hour) ||
                 authorityRevision != expectation.AuthorityRevision ||
                 CurrentVersion != expectation.ScheduleVersion ||
                 !TryCaptureLiveScheduleSnapshot(
                     target,
                     expectation.ScheduleFallbackPriority,
                     out TimePriorityLiveScheduleSnapshot current,
                     out _) ||
                 !current.HadSchedule ||
                 !expectation.MatchesSchedule(
                     current.AuthorityRevision,
                     current.FallbackPriority,
                     current.ServiceVersion,
                     current.Schedule)))
            {
                return PriorityMutationOutcome.Rejected;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);

            // No early-out when the chosen number matches the box. Pinning an
            // hour to the default is a real instruction -- it means "stay here
            // when the box moves" -- and it is only expressible as an override.
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision) ||
                (expectation != null &&
                 (!WorkPrioritySystem.TryCaptureExpectedStoredState(
                      expectedWorkSettings,
                      expectedWorkType,
                      expectation,
                      out authorityRevision) ||
                  CurrentVersion != expectation.ScheduleVersion)))
            {
                return PriorityMutationOutcome.Rejected;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            TimePriorityScheduleValue schedule = RuntimeSchedules.TryGetValue(
                target.CacheKey,
                out TimePriorityScheduleValue existing)
                ? ResolveScheduleValue(existing, fallbackPriority)
                : CreateFallbackValue(fallbackPriority);
            if (schedule.IsPinned(hour) && schedule.PriorityAt(hour) == priority)
            {
                return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision)
                    ? PriorityMutationOutcome.Applied
                    : PriorityMutationOutcome.Rejected;
            }

            RuntimeSchedules[target.CacheKey] = schedule.WithPinnedHour(hour, priority);
            LegacySchedules.Remove(target.CacheKey);

            RecordMutation();
            return PriorityMutationOutcomePolicy.AfterWrite(
                WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision));
        }

        /// <summary>
        /// Republishes an hourly schedule to any external work-tab mod backing the priority numbers.
        /// Hourly schedules live only in Better Work Tab, so nothing else propagates them.
        /// </summary>
        internal static void NotifyExternalMirror(TimePriorityTarget target, bool broadScope)
        {
            if (!broadScope)
            {
                MirrorTargetToExternalWorkTab(target);
                return;
            }

            foreach (TimePriorityCacheKey key in RuntimeSchedules.Keys)
            {
                MirrorTargetToExternalWorkTab(TimePriorityTarget.FromRaw(
                    key.PawnId, key.Kind, key.WorkTypeDefName, key.TargetDefName));
            }
        }

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

        internal static Pawn FindPawn(int pawnId)
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

            TimePriorityTarget target = TimePriorityTarget.ForWorkType(pawn, workType);
            int hour = GetCurrentHour(pawn);

            if (basePriority <= WorkPrioritySystem.DisabledPriority)
            {
                bool hasDisabledBaseSchedule = TryGetLiveScheduledPriority(target, hour, out int disabledBaseScheduledPriority);
                return new TimePriorityEvaluation(
                    target,
                    hour,
                    basePriority,
                    basePriority,
                    hasDisabledBaseSchedule,
                    disabledBaseScheduledPriority,
                    hasDisabledBaseSchedule ? TimePriorityScheduleScopes.Pawn : TimePriorityScheduleScopes.None);
            }

            if (TryGetLiveScheduledPriority(target, hour, out int scheduledPriority))
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

            TimePriorityTarget pawnTarget = TimePriorityTarget.ForWorkGiver(pawn, workGiver);
            if (pawn != null && TryGetLiveScheduledPriority(pawnTarget, hour, out int pawnScheduledPriority))
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

            TimePriorityTarget globalTarget = TimePriorityTarget.ForWorkGiver(null, workGiver);
            if (TryGetLiveScheduledPriority(globalTarget, hour, out int globalScheduledPriority))
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

        /// <summary>
        /// Handles only post-Scribe shape and legacy-link migration. Target
        /// pruning waits until FinalizeInit, when pawn and def resolution is
        /// stable across maps.
        /// </summary>
        internal static void NotifyPostLoad()
        {
            EnsureLoadedScheduleState();
            MigrateWorkGiverScheduleKeys();
            MigrateLegacyScheduleKeys(finalize: false);
        }

        /// <summary>
        /// Finalizes schedule loading after RimWorld has restored the target
        /// registries. This is the only lifecycle point that prunes removed
        /// pawns, defs, invalid global-parent rows, or duplicates.
        /// </summary>
        internal static bool NotifyLoaded()
        {
            EnsureLoadedScheduleState();
            if (LoadBoundaryHandled)
            {
                return false;
            }

            bool changed = MigrateWorkGiverScheduleKeys();
            changed |= MigrateLegacyScheduleKeys(finalize: true);
            changed |= PruneFinalizedScheduleKeys();
            LoadBoundaryHandled = true;
            MaterializeScheduleProjection();
            return changed || RuntimeSchedules.Count != 0;
        }

        /// <summary>
        /// Admits the component's deserialized schedule collection only after
        /// each deep row completed its own PostLoadInit normalization. Generic
        /// read and cache paths are deliberately not load-admission paths.
        /// </summary>
        internal static void AcceptTrustedLoadedScheduleCollection(
            List<TimePriorityScheduleData> schedules)
        {
            if (!ReferenceEquals(GetSchedules(create: false), schedules))
            {
                throw new InvalidOperationException(
                    "The deserialized hourly schedule collection is not owned by the active world component.");
            }

            ImportScheduleProjection(schedules);
        }

        internal sealed class WorkGiverScheduleRetargetPlan
        {
            internal WorkGiverScheduleRetargetPlan(
                Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> runtime,
                Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> legacy)
            {
                Runtime = runtime;
                Legacy = legacy;
            }

            internal Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> Runtime { get; }
            internal Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> Legacy { get; }
            internal bool Changed => Runtime != null;
        }

        internal static bool TryPrepareWorkGiverScheduleRetarget(
            WorkGiverDef workGiver,
            WorkTypeDef sourceWorkType,
            WorkTypeDef targetWorkType,
            out WorkGiverScheduleRetargetPlan plan,
            out string reason)
        {
            if (workGiver == null || sourceWorkType == null || targetWorkType == null)
            {
                plan = null;
                reason = "The WorkGiver reassignment target is invalid.";
                return false;
            }

            if (string.Equals(sourceWorkType.defName, targetWorkType.defName, StringComparison.Ordinal))
            {
                plan = new WorkGiverScheduleRetargetPlan(null, null);
                reason = null;
                return true;
            }

            EnsureLoadedScheduleState();
            if (!TryBuildWorkGiverScheduleRetarget(
                    workGiver.defName,
                    sourceWorkType.defName,
                    targetWorkType.defName,
                    preserveConflicts: false,
                    out plan))
            {
                reason = "A conflicting hourly schedule already belongs to the reassignment destination.";
                return false;
            }

            reason = null;
            return true;
        }

        internal static bool CommitWorkGiverScheduleRetarget(WorkGiverScheduleRetargetPlan plan)
        {
            return ApplyWorkGiverScheduleRetarget(plan, recordMutation: true);
        }

        private static bool MigrateWorkGiverScheduleKeys()
        {
            TryBuildWorkGiverScheduleRetarget(null, null, null, preserveConflicts: true, out WorkGiverScheduleRetargetPlan plan);
            return ApplyWorkGiverScheduleRetarget(plan, recordMutation: false);
        }

        private static bool TryBuildWorkGiverScheduleRetarget(
            string workGiverName,
            string sourceWorkTypeName,
            string targetWorkTypeName,
            bool preserveConflicts,
            out WorkGiverScheduleRetargetPlan plan)
        {
            var runtime = new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>(RuntimeSchedules);
            var legacy = new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>(LegacySchedules);
            bool changed = false;
            if (!MoveWorkGiverScheduleKeys(runtime, legacy, runtime, workGiverName, sourceWorkTypeName,
                    targetWorkTypeName, preserveConflicts, ref changed) ||
                !MoveWorkGiverScheduleKeys(runtime, legacy, legacy, workGiverName, sourceWorkTypeName,
                    targetWorkTypeName, preserveConflicts, ref changed))
            {
                plan = null;
                return false;
            }

            plan = new WorkGiverScheduleRetargetPlan(changed ? runtime : null, changed ? legacy : null);
            return true;
        }

        private static bool MoveWorkGiverScheduleKeys(
            Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> runtime,
            Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> legacy,
            Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> source,
            string workGiverName,
            string sourceWorkTypeName,
            string targetWorkTypeName,
            bool preserveConflicts,
            ref bool changed)
        {
            var keys = new List<TimePriorityCacheKey>(source.Keys);
            keys.Sort(CompareScheduleKeys);
            for (int index = 0; index < keys.Count; index++)
            {
                TimePriorityCacheKey key = keys[index];
                if (!source.TryGetValue(key, out TimePriorityScheduleValue value) ||
                    key.Kind != TimePriorityTargetKind.WorkGiver) continue;
                TimePriorityCacheKey current;
                if (workGiverName == null)
                {
                    current = ResolveCurrentWorkGiverKey(key);
                }
                else if (string.Equals(key.TargetDefName, workGiverName, StringComparison.Ordinal) &&
                         string.Equals(key.WorkTypeDefName, sourceWorkTypeName, StringComparison.Ordinal))
                {
                    current = new TimePriorityCacheKey(key.PawnId, key.Kind, targetWorkTypeName, key.TargetDefName);
                }
                else continue;
                if (current.Equals(key)) continue;
                source.Remove(key);
                if (TryPlaceSchedule(runtime, legacy, current, value, ReferenceEquals(source, runtime)))
                {
                    changed = true;
                    continue;
                }

                if (!preserveConflicts) return false;
                source[key] = value;
                Log.Warning("Better Work Tab retained a conflicting hourly schedule under its former WorkType for " +
                    key.TargetDefName + ".");
            }

            return true;
        }

        private static bool ApplyWorkGiverScheduleRetarget(
            WorkGiverScheduleRetargetPlan plan,
            bool recordMutation)
        {
            if (plan == null || !plan.Changed) return false;
            ReplaceSchedules(RuntimeSchedules, plan.Runtime);
            ReplaceSchedules(LegacySchedules, plan.Legacy);
            if (recordMutation) RecordMutation(); else InvalidateWorkTypeReadSnapshot();
            return true;
        }

        private static bool TryPlaceSchedule(
            Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> runtime,
            Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> legacy,
            TimePriorityCacheKey key,
            TimePriorityScheduleValue value,
            bool preferRuntime)
        {
            if (runtime.TryGetValue(key, out TimePriorityScheduleValue existing))
            {
                if (!existing.EqualsExact(value)) return false;
                legacy.Remove(key);
                return true;
            }

            if (legacy.TryGetValue(key, out existing))
            {
                if (!existing.EqualsExact(value)) return false;
                if (preferRuntime)
                {
                    legacy.Remove(key);
                    runtime[key] = value;
                }
                return true;
            }

            (preferRuntime ? runtime : legacy)[key] = value;
            return true;
        }

        private static void ReplaceSchedules(
            Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> destination,
            Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> source)
        {
            destination.Clear();
            foreach (KeyValuePair<TimePriorityCacheKey, TimePriorityScheduleValue> entry in source)
                destination[entry.Key] = entry.Value;
        }

        private static TimePriorityCacheKey ResolveCurrentWorkGiverKey(TimePriorityCacheKey key)
        {
            if (key.Kind != TimePriorityTargetKind.WorkGiver) return key;
            WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(key.TargetDefName);
            WorkTypeDef current = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            return current == null ? key : new TimePriorityCacheKey(
                key.PawnId,
                key.Kind,
                current.defName,
                key.TargetDefName);
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
        private static bool MigrateLegacyScheduleKeys(bool finalize)
        {
            if (LegacySchedules.Count == 0)
            {
                return false;
            }

            bool changed = false;
            var legacyKeys = new List<TimePriorityCacheKey>(LegacySchedules.Keys);
            legacyKeys.Sort(CompareScheduleKeys);
            for (int index = 0; index < legacyKeys.Count; index++)
            {
                TimePriorityCacheKey key = legacyKeys[index];
                if (!LegacySchedules.TryGetValue(key, out TimePriorityScheduleValue raw))
                {
                    continue;
                }

                if (!TryResolveFallbackPriority(key, out int fallback))
                {
                    if (finalize && !IsFinalizedRetainedScheduleKey(key))
                    {
                        LegacySchedules.Remove(key);
                        changed = true;
                    }

                    continue;
                }

                int[] priorities = raw.CopyPriorities();
                int pinnedHourMask = 0;
                for (int hour = 0; hour < HoursPerDay; hour++)
                {
                    priorities[hour] = WorkPrioritySystem.ClampPriority(priorities[hour]);
                    if (priorities[hour] != fallback)
                    {
                        pinnedHourMask |= 1 << hour;
                    }
                    else
                    {
                        priorities[hour] = fallback;
                    }
                }

                LegacySchedules.Remove(key);
                if (pinnedHourMask == 0)
                {
                    // The legacy row described no independent schedule.
                }
                else
                {
                    var migrated = new TimePriorityScheduleValue(
                        priorities,
                        pinnedHourMask);
                    if (!RuntimeSchedules.TryGetValue(key, out TimePriorityScheduleValue existing))
                    {
                        RuntimeSchedules[key] = migrated;
                    }
                    else if (!existing.EqualsExact(migrated))
                    {
                        Log.Warning("Better Work Tab retained the current hourly schedule instead of a conflicting legacy row for " +
                            key.TargetDefName + ".");
                    }
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// The priority a schedule's hours fall back to. Global rows have no
        /// pawn, so they use the work's default rather than a pawn's setting.
        /// </summary>
        private static bool TryResolveFallbackPriority(
            TimePriorityCacheKey key,
            out int fallback)
        {
            int defaultEnabled = WorkPrioritySystem.GetDefaultEnabledPriority();
            fallback = defaultEnabled;
            if (!IsStructurallyValidScheduleKey(key))
            {
                return false;
            }

            Pawn pawn = key.PawnId == TimePriorityTarget.GlobalPawnId
                ? null
                : FindPawn(key.PawnId);
            if (key.PawnId != TimePriorityTarget.GlobalPawnId && pawn == null)
            {
                return false;
            }

            if (key.Kind == TimePriorityTargetKind.WorkGiver)
            {
                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(key.TargetDefName);
                WorkTypeDef targetWorkType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
                if (workGiver == null || targetWorkType == null ||
                    !string.Equals(
                        key.WorkTypeDefName,
                        targetWorkType.defName,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                int parentPriority = pawn != null
                    ? WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, targetWorkType)
                    : defaultEnabled;
                fallback = WorkPrioritySystem.ClampPriority(
                    WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority));
                return true;
            }

            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(key.WorkTypeDefName);
            if (pawn == null || workType == null)
            {
                return false;
            }

            fallback = WorkPrioritySystem.ClampPriority(
                WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType));
            return true;
        }

        internal static bool TryGetLiveScheduledPriority(TimePriorityTarget target, int hour, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            if (!RuntimeSchedules.TryGetValue(target.CacheKey, out TimePriorityScheduleValue schedule))
            {
                return false;
            }

            hour = Mathf.Clamp(hour, 0, HoursPerDay - 1);
            if (!schedule.IsPinned(hour))
            {
                // A linked hour has no scheduled priority of its own; callers
                // without a fallback to hand cannot be told what it resolves to.
                return false;
            }

            priority = schedule.PriorityAt(hour);
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

        private static int[] NormalizePriorities(IList<int> priorities, int fallbackPriority)
        {
            fallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            var normalized = new int[HoursPerDay];
            for (int i = 0; i < HoursPerDay; i++)
            {
                normalized[i] = WorkPrioritySystem.ClampPriority(
                    priorities != null && i < priorities.Count
                        ? priorities[i]
                        : fallbackPriority);
            }

            return normalized;
        }

        private static TimePriorityScheduleValue CreateFallbackValue(int fallbackPriority)
        {
            return new TimePriorityScheduleValue(CreateFallbackPriorities(fallbackPriority), 0);
        }

        internal static TimePriorityScheduleValue ResolveScheduleValue(
            TimePriorityScheduleValue schedule,
            int fallbackPriority)
        {
            if (schedule == null || !schedule.HasPinnedHours)
            {
                return CreateFallbackValue(fallbackPriority);
            }

            int[] priorities = CreateFallbackPriorities(fallbackPriority);
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                if (schedule.IsPinned(hour))
                {
                    priorities[hour] = schedule.PriorityAt(hour);
                }
            }

            return new TimePriorityScheduleValue(priorities, schedule.PinnedHourMask);
        }

        private static TimePriorityScheduleValue NormalizeScheduleValue(
            TimePriorityScheduleValue value,
            int fallbackPriority)
        {
            int[] priorities = CreateFallbackPriorities(fallbackPriority);
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                if (value.IsPinned(hour))
                {
                    priorities[hour] = WorkPrioritySystem.ClampPriority(value.PriorityAt(hour));
                }
            }

            return new TimePriorityScheduleValue(priorities, value.PinnedHourMask);
        }

        private static List<TimePriorityScheduleData> GetSchedules(bool create)
        {
            GameComponent_BWTWorldSettings component = State?.Component;
            if (component?.TimePrioritySchedules == null && create)
                component.TimePrioritySchedules = new List<TimePriorityScheduleData>();
            return component?.TimePrioritySchedules;
        }

        private static void RecordMutation()
        {
            MutationBatchChanged = true;
            InvalidateWorkTypeReadSnapshot();
        }

        internal static void NormalizeBeforeSave()
        {
            if (!RuntimeSchedulesKnown)
            {
                throw new InvalidOperationException(
                    "Hourly schedule state has not completed its trusted load admission.");
            }

            MaterializeScheduleProjection();
        }

        /// <summary>
        /// Reconciles the public schedule collection at the existing
        /// low-frequency compatibility-audit boundary. Normal priority reads
        /// use per-list version sentinels and never call this scan.
        /// </summary>
        internal static bool ReconcileDirectMutationsFromAudit()
        {
            if (MultiplayerBridge.Active || HasActiveMutationBatch)
            {
                return false;
            }

            var before = new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>(RuntimeSchedules);
            var legacyBefore = new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>(LegacySchedules);
            ImportScheduleProjection(GetSchedules(create: false));
            bool changed = !ScheduleMapsEqual(before, legacyBefore);
            MaterializeScheduleProjection();
            return changed;
        }

        /// <summary>
        /// Advances the schedule's per-game revision after an observed ingress
        /// was accepted by the application. The service never invalidates UI
        /// consumers itself.
        /// </summary>
        internal static void AdvanceObservedScheduleRevision()
        {
            if (State != null) AdvanceScheduleRevision();
        }

        private static void AdvanceScheduleRevision()
        {
            CurrentVersion = unchecked(CurrentVersion + 1);
            InvalidateWorkTypeReadSnapshot();
        }

        private static bool TryPrepareTrustedScheduleMutation(out string reason)
        {
            reason = null;
            if (State == null || !RuntimeSchedulesKnown)
            {
                reason = "Hourly schedule state was not admitted by its load boundary.";
                return false;
            }

            return true;
        }

        private static void EndMutationBatch()
        {
            if (MutationBatchDepth > 0)
            {
                MutationBatchDepth--;
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

        private static void InvalidateWorkTypeReadSnapshot()
        {
            WorkTypeReadSnapshot = null;
            WorkTypeReadSnapshotVersion = -1;
        }

        private static void EnsureLoadedScheduleState()
        {
            if (RuntimeSchedulesKnown)
            {
                return;
            }

            List<TimePriorityScheduleData> schedules = GetSchedules(create: false);
            if (MultiplayerBridge.Active && schedules != null && schedules.Count != 0)
            {
                throw new InvalidOperationException(
                    "Hourly schedule state was not admitted by the trusted load boundary.");
            }

            ImportScheduleProjection(schedules);
        }

        private static void ImportScheduleProjection(List<TimePriorityScheduleData> schedules)
        {
            RuntimeSchedules.Clear();
            LegacySchedules.Clear();
            if (schedules != null)
            {
                var retainedKeys = new HashSet<TimePriorityCacheKey>();
                for (int index = 0; index < schedules.Count; index++)
                {
                    if (!TryReadScheduleProjection(
                            schedules[index],
                            out TimePriorityCacheKey key,
                            out TimePriorityScheduleValue value,
                            out bool legacy) ||
                        !retainedKeys.Add(key))
                    {
                        continue;
                    }

                    if (legacy)
                    {
                        LegacySchedules[key] = value;
                    }
                    else if (value.HasPinnedHours)
                    {
                        RuntimeSchedules[key] = value;
                    }
                }
            }

            RuntimeSchedulesKnown = true;
            InvalidateWorkTypeReadSnapshot();
        }

        private static bool TryReadScheduleProjection(
            TimePriorityScheduleData schedule,
            out TimePriorityCacheKey key,
            out TimePriorityScheduleValue value,
            out bool legacy)
        {
            key = default;
            value = null;
            legacy = false;
            if (schedule == null)
            {
                return false;
            }

            key = schedule.CacheKey;
            if (!IsStructurallyValidScheduleKey(key))
            {
                return false;
            }

            legacy = schedule.NeedsLinkMigration || schedule.UnlinkedHours == null;
            value = new TimePriorityScheduleValue(
                NormalizePriorities(schedule.HourlyPriorities, WorkPrioritySystem.GetDefaultEnabledPriority()),
                legacy ? 0 : BuildPinnedHourMask(schedule.UnlinkedHours));
            return value.IsValid;
        }

        private static void MaterializeScheduleProjection()
        {
            List<TimePriorityScheduleData> schedules = GetSchedules(create: true);
            if (schedules == null)
            {
                return;
            }

            var keySet = new HashSet<TimePriorityCacheKey>(RuntimeSchedules.Keys);
            keySet.UnionWith(LegacySchedules.Keys);
            var keys = new List<TimePriorityCacheKey>(keySet);
            keys.Sort(CompareScheduleKeys);
            schedules.Clear();
            for (int index = 0; index < keys.Count; index++)
            {
                TimePriorityCacheKey key = keys[index];
                bool legacy = !RuntimeSchedules.TryGetValue(key, out TimePriorityScheduleValue value);
                if (legacy) value = LegacySchedules[key];
                if (!legacy && !value.HasPinnedHours)
                {
                    continue;
                }

                var schedule = new TimePriorityScheduleData
                {
                    Key = BuildKey(key.PawnId, key.Kind, key.WorkTypeDefName, key.TargetDefName),
                    PawnId = key.PawnId,
                    Kind = key.Kind,
                    WorkTypeDefName = key.WorkTypeDefName,
                    TargetDefName = key.TargetDefName,
                    HourlyPriorities = new List<int>(value.CopyPriorities()),
                    UnlinkedHours = legacy ? null : CreatePinnedHourList(value.PinnedHourMask),
                    NeedsLinkMigration = legacy
                };
                schedules.Add(schedule);
            }
        }

        private static List<int> CreatePinnedHourList(int pinnedHourMask)
        {
            var hours = new List<int>();
            for (int hour = 0; hour < HoursPerDay; hour++)
            {
                if ((pinnedHourMask & (1 << hour)) != 0)
                {
                    hours.Add(hour);
                }
            }

            return hours;
        }

        private static bool PruneFinalizedScheduleKeys()
        {
            bool changed = false;
            var keys = new List<TimePriorityCacheKey>(RuntimeSchedules.Keys);
            for (int index = 0; index < keys.Count; index++)
            {
                TimePriorityCacheKey key = keys[index];
                if (IsFinalizedRetainedScheduleKey(key))
                {
                    continue;
                }

                RuntimeSchedules.Remove(key);
                changed = true;
            }

            keys = new List<TimePriorityCacheKey>(LegacySchedules.Keys);
            for (int index = 0; index < keys.Count; index++)
            {
                TimePriorityCacheKey key = keys[index];
                if (IsFinalizedRetainedScheduleKey(key))
                {
                    continue;
                }

                LegacySchedules.Remove(key);
                changed = true;
            }

            return changed;
        }

        private static bool ScheduleMapsEqual(
            IDictionary<TimePriorityCacheKey, TimePriorityScheduleValue> before,
            IDictionary<TimePriorityCacheKey, TimePriorityScheduleValue> legacyBefore)
        {
            if (before.Count != RuntimeSchedules.Count ||
                legacyBefore.Count != LegacySchedules.Count)
            {
                return false;
            }

            foreach (KeyValuePair<TimePriorityCacheKey, TimePriorityScheduleValue> entry in before)
            {
                if (!RuntimeSchedules.TryGetValue(entry.Key, out TimePriorityScheduleValue current) ||
                    !entry.Value.Equals(current))
                {
                    return false;
                }
            }

            foreach (KeyValuePair<TimePriorityCacheKey, TimePriorityScheduleValue> entry in legacyBefore)
            {
                if (!LegacySchedules.TryGetValue(entry.Key, out TimePriorityScheduleValue current) ||
                    !entry.Value.EqualsExact(current))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsStructurallyValidScheduleKey(TimePriorityCacheKey key)
        {
            return key.PawnId >= TimePriorityTarget.GlobalPawnId &&
                   Enum.IsDefined(typeof(TimePriorityTargetKind), key.Kind) &&
                   !string.IsNullOrEmpty(key.WorkTypeDefName) &&
                   (key.Kind != TimePriorityTargetKind.WorkType ||
                    key.PawnId != TimePriorityTarget.GlobalPawnId) &&
                   (key.Kind != TimePriorityTargetKind.WorkGiver ||
                    !string.IsNullOrEmpty(key.TargetDefName));
        }

        private static bool IsFinalizedRetainedScheduleKey(TimePriorityCacheKey key)
        {
            if (!IsStructurallyValidScheduleKey(key) ||
                DefDatabase<WorkTypeDef>.GetNamedSilentFail(key.WorkTypeDefName) == null)
            {
                return false;
            }

            if (key.Kind == TimePriorityTargetKind.WorkGiver &&
                DefDatabase<WorkGiverDef>.GetNamedSilentFail(key.TargetDefName) == null)
            {
                return false;
            }

            return key.PawnId == TimePriorityTarget.GlobalPawnId ||
                   FindPawn(key.PawnId) != null;
        }

        private static int CompareScheduleKeys(
            TimePriorityCacheKey left,
            TimePriorityCacheKey right)
        {
            int comparison = left.PawnId.CompareTo(right.PawnId);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = ((int)left.Kind).CompareTo((int)right.Kind);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(left.WorkTypeDefName, right.WorkTypeDefName);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.TargetDefName, right.TargetDefName);
        }

    }

    /// <summary>
    /// Non-serialized schedule runtime owned by one world component. Persisted
    /// rows remain on the component; this state never retains a prior game.
    /// </summary>
    internal sealed class TimePriorityScheduleRuntime
    {
        internal TimePriorityScheduleRuntime(GameComponent_BWTWorldSettings component)
        {
            Component = component;
        }

        internal GameComponent_BWTWorldSettings Component { get; }
        internal readonly Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> RuntimeSchedules =
            new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>();
        internal readonly Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue> LegacySchedules =
            new Dictionary<TimePriorityCacheKey, TimePriorityScheduleValue>();
        internal TimePriorityService.WorkTypeScheduleReadSnapshot WorkTypeReadSnapshot;
        internal int WorkTypeReadSnapshotVersion = -1;
        internal int CurrentVersion;
        internal bool RuntimeSchedulesKnown;
        internal bool LoadBoundaryHandled;
        internal int MutationBatchDepth;
        internal bool MutationBatchChanged;
    }
}
