using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal enum WorkTabApplicationOutcome { Rejected, NoOp, Submitted, Applied, AppliedAfterAuthorityChange, Partial }
    [System.Flags]
    internal enum WorkTabApplicationDimensions
    {
        None = 0,
        Schedule = 1,
        ParentPriority = 2,
        SpecificPriority = 4,
        ExecutionOrder = 8
    }

    internal readonly struct WorkTabApplicationRevision
    {
        internal WorkTabApplicationRevision(int epoch, int value) { Epoch = epoch; Value = value; }
        internal int Epoch { get; }
        internal int Value { get; }
    }

    internal readonly struct WorkTabApplicationChange
    {
        internal WorkTabApplicationChange(
            TimePriorityTarget target,
            WorkTabApplicationDimensions dimensions,
            bool broadScope)
        {
            Target = target;
            Dimensions = dimensions;
            BroadScope = broadScope;
        }

        internal TimePriorityTarget Target { get; }
        internal WorkTabApplicationDimensions Dimensions { get; }
        internal bool BroadScope { get; }
        internal bool IsEmpty => Dimensions == WorkTabApplicationDimensions.None;
    }

    internal readonly struct WorkTabApplicationResult
    {
        internal WorkTabApplicationResult(
            WorkTabApplicationOutcome outcome,
            string reason,
            WorkTabApplicationChange change,
            WorkTabApplicationRevision revision)
        {
            Outcome = outcome;
            Reason = reason;
            Change = change;
            Revision = revision;
        }
        internal WorkTabApplicationOutcome Outcome { get; }
        internal string Reason { get; }
        internal WorkTabApplicationChange Change { get; }
        internal WorkTabApplicationRevision Revision { get; }
        internal bool Changed => !Change.IsEmpty;
        internal bool Accepted => Outcome == WorkTabApplicationOutcome.NoOp ||
                                  Outcome == WorkTabApplicationOutcome.Submitted ||
                                  Outcome == WorkTabApplicationOutcome.Applied ||
                                  Outcome == WorkTabApplicationOutcome.AppliedAfterAuthorityChange;

        internal static WorkTabApplicationResult Rejected(string reason, WorkTabApplicationRevision revision) =>
            new WorkTabApplicationResult(WorkTabApplicationOutcome.Rejected, reason, default, revision);

    }

    internal readonly struct WorkTabScheduleCommand
    {
        internal WorkTabScheduleCommand(
            TimePriorityTarget target,
            TimePriorityScheduleValue value,
            int fallbackPriority)
        {
            Target = target;
            Value = value;
            FallbackPriority = fallbackPriority;
        }

        internal TimePriorityTarget Target { get; }
        internal TimePriorityScheduleValue Value { get; }
        internal int FallbackPriority { get; }
    }

    /// <summary>Per-world command boundary for priority and schedule writes.</summary>
    internal sealed class WorkTabApplication
    {
        private readonly Game _game;
        private readonly TimePriorityScheduleRuntime _scheduleRuntime;
        private bool _applying;
        private bool _ownsParentPrioritySetter;
        private int _epoch;
        private int _revision;

        internal WorkTabApplication(Game game, TimePriorityScheduleRuntime scheduleRuntime, int epoch = 0)
        {
            _game = game;
            _scheduleRuntime = scheduleRuntime;
            _epoch = epoch;
        }

        internal static WorkTabApplication Current =>
            Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>()?.Application;
        internal static bool OwnsCurrentParentPrioritySetter =>
            Current?._ownsParentPrioritySetter == true;
        internal WorkTabApplicationRevision Revision => new WorkTabApplicationRevision(_epoch, _revision);
        internal void SetRevisionEpoch(int epoch) { _epoch = epoch; }

        internal bool SetStoredParentPriority(Pawn pawn, WorkTypeDef workType, int priority) =>
            ApplyParent(pawn, workType, priority, -1, null);

        internal bool SetDisplayedParentPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (!TryCaptureParent(pawn, workType, priority, out ParentPriorityCommandExpectation expected)) return false;
            int fallback = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            TimePriorityEvaluation presentation = TimePriorityService.EvaluateWorkTypePriority(pawn, workType, fallback);
            if (!presentation.HasSchedule || expected.StoredPriority <= WorkPrioritySystem.DisabledPriority ||
                !TimePriorityService.TryCaptureLiveScheduleSnapshot(presentation.Target, fallback,
                    out TimePriorityLiveScheduleSnapshot schedule, out _) || !schedule.HadSchedule ||
                !schedule.Schedule.IsPinned(presentation.Hour) || schedule.AuthorityRevision != expected.AuthorityRevision)
                return ApplyParent(pawn, workType, priority, -1, expected);

            return ApplyParent(pawn, workType, priority, presentation.Hour,
                new ParentPriorityCommandExpectation(schedule.AuthorityRevision, expected.StoredPriority,
                    schedule.FallbackPriority, schedule.ServiceVersion, schedule.Schedule.CopyPriorities(), schedule.Schedule.PinnedHourMask));
        }

        internal WorkTabApplicationResult SubmitSchedule(
            TimePriorityTarget target, TimePriorityScheduleValue value, int fallback)
        {
            if (value == null || !value.IsValid || Current == null)
                return Reject("The hourly schedule command is not available for this world.");
            if (!WorkTabActionability.CanApplySchedule(target))
                return Reject("The hourly schedule target is not currently actionable.");
            if (!TimePriorityService.TryCaptureLiveScheduleSnapshot(target, fallback,
                    out TimePriorityLiveScheduleSnapshot expected, out string reason))
                return Reject(reason);
            if (expected.Schedule.Equals(value))
                return new WorkTabApplicationResult(WorkTabApplicationOutcome.NoOp, null, default, Revision);
            if (MultiplayerBridge.Active)
            {
                SyncSubmitSchedule(target.PawnId, (int)target.Kind, target.WorkTypeDefName, target.TargetDefName,
                    expected.FallbackPriority, expected.HadSchedule, expected.ServiceVersion, expected.AuthorityRevision,
                    expected.Schedule.PinnedHourMask, expected.Schedule.CopyPriorities(), value.PinnedHourMask, value.CopyPriorities());
                return new WorkTabApplicationResult(WorkTabApplicationOutcome.Submitted, null, default, Revision);
            }
            return ApplySchedule(expected, value);
        }

        /// <summary>Trusted compatibility import boundary; normal callers use <see cref="SubmitSchedule"/>.</summary>
        internal int ApplyImportedScheduleBatch(
            IReadOnlyList<WorkTabScheduleCommand> commands,
            WorkTabApplicationDimensions additionalDimensions,
            bool broadScope)
        {
            if (!IsCurrent || commands == null || MultiplayerBridge.Active) return 0;
            if (!Enter()) return 0;
            try
            {
                int changedCount = 0;
                var affectedTargets = new List<TimePriorityTarget>();
                using (TimePriorityService.BeginMutationBatch())
                {
                    for (int i = 0; i < commands.Count; i++)
                    {
                        WorkTabScheduleCommand command = commands[i];
                        if (command.Value?.IsValid != true ||
                            !TimePriorityService.TryCaptureLiveScheduleSnapshot(command.Target, command.FallbackPriority,
                                out TimePriorityLiveScheduleSnapshot expected, out _)) continue;
                        TimePriorityScheduleMutationOutcome outcome = command.Value.HasPinnedHours
                            ? TimePriorityService.TryApplyLiveScheduleSnapshot(expected, command.Value, out _, out bool changed)
                            : TimePriorityService.TryClearLiveScheduleSnapshot(expected, out _, out changed);
                        if (outcome == TimePriorityScheduleMutationOutcome.Rejected || !changed) continue;
                        changedCount++;
                        affectedTargets.Add(command.Target);
                    }
                }
                bool committed = TimePriorityService.CommitMutationBatch();
                if (changedCount > 0 && !committed)
                    throw new System.InvalidOperationException("A changed hourly schedule batch was not committed.");
                WorkTabApplicationDimensions dimensions = additionalDimensions |
                    (committed ? WorkTabApplicationDimensions.Schedule : WorkTabApplicationDimensions.None);
                if (dimensions != WorkTabApplicationDimensions.None)
                    Publish(default, dimensions, broadScope, true, broadScope ? null : affectedTargets);
                return changedCount;
            }
            finally { Exit(); }
        }

        // Sync entrypoints call this direct executor; they never invoke a
        // command method and therefore cannot recursively submit themselves.
        internal void ApplySynchronizedSchedule(TimePriorityLiveScheduleSnapshot expected, TimePriorityScheduleValue desired)
        {
            if (expected?.Schedule?.IsValid == true && desired?.IsValid == true) ApplySchedule(expected, desired);
        }

        internal WorkTabApplicationResult ApplyFullDayParent(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (!TryCaptureParent(pawn, workType, priority, out ParentPriorityCommandExpectation parent)) return Reject("The parent priority baseline changed.");
            TimePriorityTarget target = TimePriorityTarget.ForWorkType(pawn, workType);
            int fallback = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (!TimePriorityService.TryCaptureLiveScheduleSnapshot(target, fallback,
                    out TimePriorityLiveScheduleSnapshot schedule, out string reason) || parent.AuthorityRevision != schedule.AuthorityRevision)
                return Reject(reason ?? "The hourly schedule baseline changed.");
            return SubmitFullDay(new FullDayRequest(pawn, workType, null, priority, parent.StoredPriority, 0, 0,
                WorkPrioritySystem.DisabledPriority, schedule));
        }

        internal WorkTabApplicationResult ApplyFullDaySpecific(Pawn pawn, WorkGiverDef workGiver, int priority)
        {
            WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            if (workType == null || (pawn != null && !WorkTabActionability.CanApplySpecific(pawn, workType, workGiver)) ||
                WorkPrioritySystem.ClampPriority(priority) != priority) return Reject("The sub-work target is invalid.");
            TimePriorityTarget target = TimePriorityTarget.ForWorkGiver(pawn, workGiver);
            int parent = pawn == null ? WorkPrioritySystem.GetDefaultEnabledPriority() : WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            int fallback = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parent);
            if (!TimePriorityService.TryCaptureLiveScheduleSnapshot(target, fallback,
                    out TimePriorityLiveScheduleSnapshot schedule, out string reason) ||
                !TryCaptureSpecific(pawn, workGiver, schedule.AuthorityRevision, out int revision, out int state, out int previous))
                return Reject(reason ?? "The sub-work priority baseline changed.");
            return SubmitFullDay(new FullDayRequest(pawn, workType, workGiver, priority,
                WorkPrioritySystem.DisabledPriority, revision, state, previous, schedule));
        }

        internal void ReportObservedScheduleChange()
        {
            if (!IsCurrent) return;
            TimePriorityService.AdvanceObservedScheduleRevision();
            Publish(default, WorkTabApplicationDimensions.Schedule, true, true);
        }

        internal static void PublishCompletedScheduleMutation(
            bool changed,
            bool broadScope,
            WorkTabApplicationDimensions dimensions = WorkTabApplicationDimensions.Schedule,
            IEnumerable<TimePriorityTarget> affectedTargets = null)
        {
            if (changed) Current?.Publish(default, dimensions, broadScope, true, affectedTargets);
        }

        internal void NotifyHourBoundary()
        {
            if (IsCurrent) Publish(default, WorkTabApplicationDimensions.Schedule, true, false);
        }

        private WorkTabApplicationResult ApplySchedule(
            TimePriorityLiveScheduleSnapshot expected, TimePriorityScheduleValue desired)
        {
            if (!WorkTabActionability.CanApplySchedule(expected.Target))
                return Reject("The hourly schedule target is no longer actionable.");
            if (!Enter()) return Reject("Another work-tab command is active.");
            try
            {
                bool changed;
                string reason;
                TimePriorityScheduleMutationOutcome outcome;
                using (TimePriorityService.BeginMutationBatch())
                {
                    outcome = desired.HasPinnedHours
                        ? TimePriorityService.TryApplyLiveScheduleSnapshot(expected, desired, out reason, out changed)
                        : TimePriorityService.TryClearLiveScheduleSnapshot(expected, out reason, out changed);
                }
                if (outcome == TimePriorityScheduleMutationOutcome.Rejected)
                    return Reject(reason);
                bool committed = changed && TimePriorityService.CommitMutationBatch();
                if (changed && !committed)
                    throw new System.InvalidOperationException("A changed hourly schedule was not committed.");
                WorkTabApplicationChange change = committed
                    ? Publish(expected.Target, WorkTabApplicationDimensions.Schedule, false, true)
                    : default;
                return new WorkTabApplicationResult(
                    !changed
                        ? WorkTabApplicationOutcome.NoOp
                        : outcome == TimePriorityScheduleMutationOutcome.AppliedAfterAuthorityChange
                            ? WorkTabApplicationOutcome.AppliedAfterAuthorityChange
                            : WorkTabApplicationOutcome.Applied,
                    null, change, Revision);
            }
            finally { Exit(); }
        }

        private WorkTabApplicationResult SubmitFullDay(FullDayRequest request)
        {
            if (MultiplayerBridge.Active)
            {
                SyncApplyFullDay(request.Pawn?.thingIDNumber ?? TimePriorityTarget.GlobalPawnId,
                    request.WorkGiver == null ? request.WorkType.defName : null, request.WorkGiver?.defName,
                    request.Priority, request.ParentPriority, request.SpecificRevision, request.SpecificState,
                    request.SpecificPriority, request.Schedule.FallbackPriority, request.Schedule.HadSchedule,
                    request.Schedule.ServiceVersion, request.Schedule.AuthorityRevision, request.Schedule.Schedule.PinnedHourMask,
                    request.Schedule.Schedule.CopyPriorities());
                return new WorkTabApplicationResult(
                    WorkTabApplicationOutcome.Submitted, null, default, Revision);
            }
            return ApplyFullDay(request);
        }

        private WorkTabApplicationResult ApplyFullDay(FullDayRequest request)
        {
            if (!Enter()) return Reject("Another work-tab command is active.");
            try
            {
                bool scheduleChanged = false;
                bool priorityChanged = false;
                bool applied = false;
                bool restored = false;
                string reason = null;
                PriorityMutationOutcome outcome = PriorityMutationOutcome.Rejected;
                System.IDisposable specificBatch = request.WorkGiver == null
                    ? null : WorkGiverReassignmentManager.BeginMutationBatch();
                try
                {
                    if (!Validate(request))
                    {
                        reason = "The priority baseline changed before the schedule could be cleared.";
                    }
                    else
                    {
                        using (ExternalPriorityMirror.Suspend())
                        using (TimePriorityService.BeginMutationBatch())
                        {
                            if (TimePriorityService.TryClearLiveScheduleSnapshot(request.Schedule, out reason, out scheduleChanged) ==
                                TimePriorityScheduleMutationOutcome.Rejected)
                                reason = reason ?? "The hourly schedule clear was rejected.";
                            else
                                outcome = ApplyPriority(request);
                            if (outcome == PriorityMutationOutcome.Rejected)
                            {
                                restored = scheduleChanged &&
                                    TimePriorityService.RestoreUnpublishedScheduleSnapshot(request.Schedule);
                                reason = reason ?? "The priority baseline changed; the hourly schedule was restored.";
                            }
                            else
                            {
                                priorityChanged = WouldChange(request);
                                applied = true;
                            }
                        }
                    }
                }
                finally { specificBatch?.Dispose(); }

                if (!applied && scheduleChanged && restored)
                {
                    TimePriorityService.DiscardMutationBatch();
                    WorkGiverReassignmentManager.CommitMutationBatch();
                    return Reject(reason);
                }

                bool committedSchedule = TimePriorityService.CommitMutationBatch();
                bool committedSpecific = WorkGiverReassignmentManager.CommitMutationBatch();
                WorkTabApplicationDimensions dimensions =
                    (committedSchedule ? WorkTabApplicationDimensions.Schedule : WorkTabApplicationDimensions.None) |
                    (priorityChanged || committedSpecific
                        ? request.WorkGiver == null
                            ? WorkTabApplicationDimensions.ParentPriority
                            : WorkTabApplicationDimensions.SpecificPriority
                        : WorkTabApplicationDimensions.None);
                WorkTabApplicationChange resultChange = dimensions == WorkTabApplicationDimensions.None
                    ? default
                    : Publish(request.Schedule.Target, dimensions, false, true);
                if (!applied && resultChange.IsEmpty)
                    return Reject(reason);
                if (!applied)
                    return new WorkTabApplicationResult(
                        WorkTabApplicationOutcome.Partial, reason, resultChange, Revision);
                if (resultChange.IsEmpty)
                    return new WorkTabApplicationResult(
                        WorkTabApplicationOutcome.NoOp, null, default, Revision);
                return new WorkTabApplicationResult(
                    outcome == PriorityMutationOutcome.AppliedAfterAuthorityChange
                        ? WorkTabApplicationOutcome.AppliedAfterAuthorityChange
                        : WorkTabApplicationOutcome.Applied,
                    null,
                    resultChange,
                    Revision);
            }
            finally { Exit(); }
        }

        private bool ApplyParent(Pawn pawn, WorkTypeDef workType, int priority, int hour, ParentPriorityCommandExpectation expected)
        {
            if (!TryCaptureParent(pawn, workType, priority, out ParentPriorityCommandExpectation captured)) return false;
            expected = expected ?? captured;
            if (MultiplayerBridge.Active)
            {
                SyncSetParentPriority(pawn.thingIDNumber, workType.defName, priority, hour, expected.AuthorityRevision,
                    expected.StoredPriority, expected.ScheduleFallbackPriority, expected.ScheduleVersion,
                    expected.PinnedHourMask, expected.CopySchedulePriorities());
                return true;
            }
            if (!Enter()) return false;
            try
            {
                PriorityMutationOutcome outcome;
                if (hour < 0)
                {
                    outcome = ApplyParentPriority(pawn.workSettings, workType, priority, expected);
                }
                else
                {
                    using (TimePriorityService.BeginMutationBatch())
                    {
                        outcome = TimePriorityService.ApplyPriorityAtHour(TimePriorityTarget.ForWorkType(pawn, workType), hour, priority,
                            expected.ScheduleFallbackPriority, expected, pawn.workSettings, workType);
                    }
                }
                if (outcome == PriorityMutationOutcome.Rejected) return false;
                bool changed = hour < 0 ? expected.StoredPriority != priority : TimePriorityService.CommitMutationBatch();
                if (changed)
                {
                    Publish(TimePriorityTarget.ForWorkType(pawn, workType),
                        WorkTabApplicationDimensions.ParentPriority |
                        (hour >= 0 ? WorkTabApplicationDimensions.Schedule : WorkTabApplicationDimensions.None),
                        false, true);
                }
                return true;
            }
            finally { Exit(); }
        }

        private static bool TryCaptureParent(Pawn pawn, WorkTypeDef workType, int priority,
            out ParentPriorityCommandExpectation expected)
        {
            expected = null;
            return CanApplyParent(pawn, workType, priority) && WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authority) &&
                   WorkPrioritySystem.TryGetRawStoredPriority(pawn.workSettings, workType, out int stored) &&
                   WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authority) &&
                   (expected = new ParentPriorityCommandExpectation(authority, stored)) != null;
        }

        private static bool CanApplyParent(Pawn pawn, WorkTypeDef workType, int priority)
        {
            return WorkTabActionability.CanApplyParent(pawn, workType) &&
                   priority >= WorkPrioritySystem.DisabledPriority &&
                   priority <= WorkPrioritySystem.GetRequestableMaxPriority() &&
                   PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData;
        }

        private static bool TryCaptureSpecific(Pawn pawn, WorkGiverDef workGiver, long authority,
            out int revision, out int state, out int priority)
        {
            revision = WorkGiverReassignmentManager.CurrentSyncVersion; state = 0; priority = WorkPrioritySystem.DisabledPriority;
            if (pawn == null)
            {
                WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot global =
                    WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(workGiver.defName);
                revision = global.SyncVersion; state = (int)global.State; priority = global.Priority;
                return global.AuthorityRevision == authority;
            }
            if (!WorkGiverReassignmentManager.TryCapturePawnWorkGiverPriority(pawn, workGiver,
                    out revision, out bool hasOverride, out priority, out long captured) || captured != authority) return false;
            state = hasOverride ? 1 : 0;
            return true;
        }

        private PriorityMutationOutcome ApplyPriority(FullDayRequest request)
        {
            if (request.WorkGiver == null)
                return ApplyParentPriority(request.Pawn.workSettings, request.WorkType, request.Priority,
                    new ParentPriorityCommandExpectation(request.Schedule.AuthorityRevision, request.ParentPriority));
            if (request.Pawn == null)
                return WorkGiverReassignmentManager.TrySetGlobalWorkGiverPriority(request.WorkGiver.defName, request.Priority,
                    new WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot(request.WorkGiver.defName,
                        (WorkGiverReassignmentManager.ExactGlobalStateKind)request.SpecificState, request.SpecificPriority,
                        request.SpecificRevision, request.Schedule.AuthorityRevision), notify: true);
            return WorkGiverReassignmentManager.TrySetPawnWorkGiverPriority(request.Pawn, request.WorkGiver, request.Priority,
                request.SpecificRevision, request.SpecificState == 1, request.SpecificPriority, request.Schedule.AuthorityRevision);
        }

        private PriorityMutationOutcome ApplyParentPriority(
            Pawn_WorkSettings workSettings,
            WorkTypeDef workType,
            int priority,
            ParentPriorityCommandExpectation expectation)
        {
            using (ExternalPriorityMirror.Suspend())
            {
                _ownsParentPrioritySetter = true;
                try
                {
                    return WorkPrioritySystem.ApplyPriority(
                        workSettings,
                        workType,
                        priority,
                        expectation);
                }
                finally
                {
                    _ownsParentPrioritySetter = false;
                }
            }
        }

        private static bool Validate(FullDayRequest request)
        {
            if (request.WorkGiver == null)
                return TryCaptureParent(request.Pawn, request.WorkType, request.Priority, out ParentPriorityCommandExpectation parent) &&
                       parent.StoredPriority == request.ParentPriority && parent.AuthorityRevision == request.Schedule.AuthorityRevision;
            return WorkGiverReassignmentManager.GetTargetWorkType(request.WorkGiver) == request.WorkType &&
                   (request.Pawn == null || WorkTabActionability.CanApplySpecific(
                       request.Pawn, request.WorkType, request.WorkGiver)) &&
                   TryCaptureSpecific(request.Pawn, request.WorkGiver, request.Schedule.AuthorityRevision,
                       out int revision, out int state, out int priority) && revision == request.SpecificRevision &&
                   state == request.SpecificState && priority == request.SpecificPriority;
        }

        private static bool WouldChange(FullDayRequest request) => request.WorkGiver == null ? request.Priority != request.ParentPriority :
            request.Pawn == null ? request.SpecificState != (int)WorkGiverReassignmentManager.ExactGlobalStateKind.Set || request.Priority != request.SpecificPriority :
            request.SpecificState == 0 || request.Priority != request.SpecificPriority;

        private WorkTabApplicationChange Publish(
            TimePriorityTarget target,
            WorkTabApplicationDimensions dimensions,
            bool broad,
            bool durable,
            IEnumerable<TimePriorityTarget> affectedTargets = null)
        {
            if (durable) _revision = unchecked(_revision + 1);
            WorkTabDirtyFlags flags = WorkTabDirtyFlags.Priority;
            if ((dimensions & WorkTabApplicationDimensions.Schedule) != 0 || durable)
            {
                flags |= WorkTabDirtyFlags.ScheduleHour | WorkTabDirtyFlags.Presentation;
            }
            if ((dimensions & WorkTabApplicationDimensions.ParentPriority) != 0)
                flags |= WorkTabDirtyFlags.Presentation;
            if ((dimensions & WorkTabApplicationDimensions.SpecificPriority) != 0)
                flags |= WorkTabDirtyFlags.SubWorkOverride |
                    WorkTabDirtyFlags.Columns |
                    WorkTabDirtyFlags.HeaderGeometry;
            WorkTabInvalidationHub.Invalidate(flags);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            if (durable) MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            if (durable && affectedTargets == null)
            {
                TimePriorityService.NotifyExternalMirror(target, broad);
            }
            else if (durable)
            {
                var mirrored = new HashSet<TimePriorityCacheKey>();
                foreach (TimePriorityTarget affectedTarget in affectedTargets)
                {
                    if (mirrored.Add(affectedTarget.CacheKey))
                        TimePriorityService.NotifyExternalMirror(affectedTarget, false);
                }
            }
            return new WorkTabApplicationChange(target, dimensions, broad);
        }

        private bool Enter() => IsCurrent && !_applying && (_applying = true);
        private void Exit() { _applying = false; }
        private bool IsCurrent => ReferenceEquals(_game, Verse.Current.Game) && _scheduleRuntime != null;
        private static WorkTabApplicationResult Reject(string reason) =>
            WorkTabApplicationResult.Rejected(reason, Current?.Revision ?? default);
        [SyncMethod]
        internal static void SyncSubmitSchedule(int pawnId, int kind, string workType, string target, int fallback,
            bool hadSchedule, int version, long authority, int expectedMask, int[] expectedPriorities, int desiredMask, int[] desiredPriorities)
        {
            if (Current == null || !System.Enum.IsDefined(typeof(TimePriorityTargetKind), kind)) return;
            var expected = new TimePriorityLiveScheduleSnapshot(TimePriorityTarget.FromRaw(pawnId, (TimePriorityTargetKind)kind, workType, target),
                new TimePriorityScheduleValue(expectedPriorities, expectedMask), hadSchedule, fallback, version, authority);
            var desired = new TimePriorityScheduleValue(desiredPriorities, desiredMask);
            if (expected.Schedule.IsValid && desired.IsValid) Current.ApplySynchronizedSchedule(expected, desired);
        }

        [SyncMethod]
        internal static void SyncSetParentPriority(int pawnId, string workTypeName, int priority, int hour, long authority,
            int stored, int fallback, int version, int mask, int[] priorities)
        {
            Pawn pawn = TimePriorityService.FindPawn(pawnId); WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeName);
            if (pawn == null || workType == null || Current == null) return;
            var expected = hour < 0 ? new ParentPriorityCommandExpectation(authority, stored) :
                new ParentPriorityCommandExpectation(authority, stored, fallback, version, priorities, mask);
            Current.ApplyParent(pawn, workType, priority, hour, expected);
        }

        [SyncMethod]
        internal static void SyncApplyFullDay(int pawnId, string parentWorkType, string workGiverName, int priority,
            int parentPriority, int specificRevision, int specificState, int specificPriority, int fallback, bool hadSchedule,
            int version, long authority, int mask, int[] priorities)
        {
            WorkTabApplication app = Current;
            Pawn pawn = pawnId == TimePriorityTarget.GlobalPawnId ? null : TimePriorityService.FindPawn(pawnId);
            WorkGiverDef giver = string.IsNullOrEmpty(workGiverName) ? null : DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverName);
            WorkTypeDef workType = giver == null ? DefDatabase<WorkTypeDef>.GetNamedSilentFail(parentWorkType) :
                WorkGiverReassignmentManager.GetTargetWorkType(giver);
            if (app == null || workType == null || (pawnId >= 0 && pawn == null) || (giver == null && pawn == null)) return;
            TimePriorityTarget target = giver == null ? TimePriorityTarget.ForWorkType(pawn, workType) : TimePriorityTarget.ForWorkGiver(pawn, giver);
            var schedule = new TimePriorityLiveScheduleSnapshot(target, new TimePriorityScheduleValue(priorities, mask),
                hadSchedule, fallback, version, authority);
            if (!schedule.Schedule.IsValid) return;
            app.ApplyFullDay(new FullDayRequest(pawn, workType, giver, priority, parentPriority, specificRevision,
                specificState, specificPriority, schedule));
        }

        private readonly struct FullDayRequest
        {
            internal FullDayRequest(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver, int priority, int parentPriority,
                int specificRevision, int specificState, int specificPriority, TimePriorityLiveScheduleSnapshot schedule)
            {
                Pawn = pawn; WorkType = workType; WorkGiver = workGiver; Priority = priority; ParentPriority = parentPriority;
                SpecificRevision = specificRevision; SpecificState = specificState; SpecificPriority = specificPriority; Schedule = schedule;
            }
            internal Pawn Pawn { get; }
            internal WorkTypeDef WorkType { get; }
            internal WorkGiverDef WorkGiver { get; }
            internal int Priority { get; }
            internal int ParentPriority { get; }
            internal int SpecificRevision { get; }
            internal int SpecificState { get; }
            internal int SpecificPriority { get; }
            internal TimePriorityLiveScheduleSnapshot Schedule { get; }
        }
    }
}
