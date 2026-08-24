using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.Mod_Support.Multiplayer;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal readonly struct WorkTabMutationCommit
    {
        internal WorkTabMutationCommit(
            bool scheduleChanged,
            bool specificJobsChanged,
            bool scheduleRevisionOwned)
        {
            ScheduleChanged = scheduleChanged;
            SpecificJobsChanged = specificJobsChanged;
            ScheduleRevisionOwned = scheduleRevisionOwned;
        }

        internal bool ScheduleChanged { get; }
        internal bool SpecificJobsChanged { get; }
        internal bool ScheduleRevisionOwned { get; }
    }

    /// <summary>
    /// Owns the shared live-write batch boundary for a Work-tab mutation.
    /// Domain features build their own typed plans, while this scope owns
    /// batch commit, schedule revision ownership, and publication.
    /// </summary>
    internal sealed class WorkTabMutationScope : IDisposable
    {
        private IDisposable _scheduleBatch;
        private IDisposable _specificJobBatch;
        private IDisposable _externalMirror;
        private readonly HashSet<WorkGiverReassignmentManager.SpecificJobBatchRollback>
            _unpublishedSpecificJobs =
                new HashSet<WorkGiverReassignmentManager.SpecificJobBatchRollback>();
        private readonly HashSet<TimePriorityCacheKey> _unpublishedSchedules =
            new HashSet<TimePriorityCacheKey>();

        internal WorkTabMutationScope(
            bool includesSchedules,
            bool includesSpecificJobs)
        {
            _externalMirror = ExternalPriorityMirror.Suspend();
            if (includesSpecificJobs)
            {
                _specificJobBatch = WorkGiverReassignmentManager.BeginMutationBatch();
            }

            if (includesSchedules)
            {
                _scheduleBatch = TimePriorityService.BeginMutationBatch();
            }
        }

        internal WorkTabMutationCommit Complete(WorkTabScheduleRevisionReceipt scheduleReceipt = null)
        {
            bool scheduleChanged = false;
            bool scheduleRevisionOwned = false;
            if (_scheduleBatch != null)
            {
                _scheduleBatch.Dispose();
                _scheduleBatch = null;
                scheduleChanged = TimePriorityService.CommitMutationBatch();
                scheduleRevisionOwned = scheduleReceipt != null && scheduleReceipt.AcceptCommit(
                    scheduleChanged,
                    TimePriorityService.CurrentVersion);
            }

            bool specificJobsChanged = false;
            if (_specificJobBatch != null)
            {
                _specificJobBatch.Dispose();
                _specificJobBatch = null;
                specificJobsChanged = WorkGiverReassignmentManager.CommitMutationBatch(
                    notifyDependents: false);
            }

            ReleaseMirror();
            return new WorkTabMutationCommit(
                scheduleChanged,
                specificJobsChanged,
                scheduleRevisionOwned);
        }

        public void Dispose()
        {
            if (_scheduleBatch != null)
            {
                _scheduleBatch.Dispose();
                _scheduleBatch = null;
                TimePriorityService.DiscardMutationBatch();
            }

            if (_specificJobBatch != null)
            {
                _specificJobBatch.Dispose();
                _specificJobBatch = null;
                WorkGiverReassignmentManager.DiscardMutationBatch();
            }

            ReleaseMirror();
        }

        private void ReleaseMirror()
        {
            _externalMirror?.Dispose();
            _externalMirror = null;
        }

        internal bool TryApplySpecificJobs(
            IReadOnlyList<WorkGiverReassignmentManager.SpecificPriorityBatchEntry> priorities,
            IReadOnlyList<WorkGiverReassignmentManager.SpecificOrderBatchEntry> orders,
            int expectedRevision,
            long authorityRevision,
            bool synchronizedReplay,
            WorkTabMutationLease authorization,
            out WorkGiverReassignmentManager.SpecificJobBatchRollback rollback,
            out string reason)
        {
            bool applied = WorkGiverReassignmentManager.TryApplySpecificJobBatch(
                priorities,
                orders,
                expectedRevision,
                authorityRevision,
                synchronizedReplay,
                authorization,
                out rollback,
                out reason);
            if (applied && rollback != null) _unpublishedSpecificJobs.Add(rollback);
            return applied;
        }

        internal bool TryRestoreSpecificJobs(
            WorkGiverReassignmentManager.SpecificJobBatchRollback rollback,
            out string reason)
        {
            if (!_unpublishedSpecificJobs.Contains(rollback))
                return WorkGiverReassignmentManager.TryRestoreSpecificJobBatch(
                    rollback, out reason);
            bool restored = WorkGiverReassignmentManager.RestoreUnpublishedSpecificJobBatch(
                rollback, out reason);
            if (restored) _unpublishedSpecificJobs.Remove(rollback);
            return restored;
        }

        internal bool TrySetManualPriorityMode(bool desired, out bool observed)
        {
            bool accepted = WorkPrioritySystem.SetManualPriorities(desired);
            observed = Find.PlaySettings != null && Find.PlaySettings.useWorkPriorities;
            return accepted;
        }

        internal bool TrySetParentPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int desired,
            out int observed)
        {
            bool accepted = pawn?.workSettings != null && workType != null &&
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, desired);
            observed = pawn?.workSettings == null || workType == null
                ? WorkPrioritySystem.DisabledPriority
                : PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                    pawn.workSettings,
                    workType);
            return accepted;
        }

        internal bool TryRestoreParentPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int expected)
        {
            return WorkPrioritySystem.SetStoredPriorityWithoutMirroring(
                pawn?.workSettings,
                workType,
                expected);
        }

        internal TimePriorityScheduleMutationOutcome ApplySchedule(
            TimePriorityLiveScheduleSnapshot expected,
            TimePriorityScheduleValue desired,
            bool synchronizedReplay,
            WorkTabMutationLease authorization,
            out bool changed,
            out string reason)
        {
            changed = false;
            if (!CanApplySchedule(expected, synchronizedReplay, authorization, out reason))
            {
                return TimePriorityScheduleMutationOutcome.Rejected;
            }

            TimePriorityScheduleMutationOutcome outcome;
            if (desired == null || !desired.HasPinnedHours)
            {
                outcome = TimePriorityService.TryClearLiveScheduleSnapshot(
                    expected, out reason, out changed);
            }
            else if (!desired.IsValid)
            {
                reason = reason ?? "The desired schedule is not a complete 24-hour value.";
                return TimePriorityScheduleMutationOutcome.Rejected;
            }
            else
            {
                outcome = TimePriorityService.TryApplyLiveScheduleSnapshot(
                    expected, desired, out reason, out changed);
            }

            if (outcome != TimePriorityScheduleMutationOutcome.Rejected && changed)
                _unpublishedSchedules.Add(expected.Target.CacheKey);
            return outcome;
        }

        internal bool TryRestoreSchedule(
            TimePriorityLiveScheduleSnapshot snapshot,
            TimePriorityLiveScheduleSnapshot expected,
            bool synchronizedReplay,
            WorkTabMutationLease authorization,
            int initialRevision,
            int ownedRevision,
            out string reason)
        {
            reason = null;
            if (snapshot != null && _unpublishedSchedules.Contains(snapshot.Target.CacheKey))
            {
                bool restored = TimePriorityService.RestoreUnpublishedScheduleSnapshot(snapshot);
                if (restored) _unpublishedSchedules.Remove(snapshot.Target.CacheKey);
                return restored;
            }
            if (expected == null || expected.ServiceVersion != ownedRevision ||
                (MultiplayerBridge.Active
                    ? !synchronizedReplay &&
                      (authorization == null || !authorization.IsAcceptedForSchedule(
                          true, expected.AuthorityRevision, initialRevision))
                    : authorization != null && !authorization.IsAcceptedForSchedule(
                        false, expected.AuthorityRevision, initialRevision)))
            {
                reason = "The atomic mutation no longer owns the schedule revision being rolled back.";
                return false;
            }
            return TimePriorityService.TryRestoreLiveScheduleSnapshot(snapshot, expected, out reason) !=
                TimePriorityScheduleMutationOutcome.Rejected;
        }

        private static bool CanApplySchedule(
            TimePriorityLiveScheduleSnapshot expected,
            bool synchronizedReplay,
            WorkTabMutationLease authorization,
            out string reason)
        {
            reason = null;
            if (expected == null ||
                (MultiplayerBridge.Active
                    ? !synchronizedReplay &&
                      (authorization == null || !authorization.IsAcceptedForSchedule(
                          true, expected.AuthorityRevision, expected.ServiceVersion))
                    : authorization != null && !authorization.IsAcceptedForSchedule(
                        false, expected.AuthorityRevision, expected.ServiceVersion)))
            {
                reason = "The atomic mutation is not authorized for the captured schedule revision.";
                return false;
            }
            return true;
        }
    }
}
