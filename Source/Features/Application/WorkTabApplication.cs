using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.Settings;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal enum WorkTabApplicationOutcome
    {
        Rejected,
        NoOp,
        Submitted,
        Applied,
        AppliedAfterAuthorityChange,
        Partial,
        FailedRolledBack,
        RecoveryRequired
    }
    [System.Flags]
    internal enum WorkTabApplicationDimensions
    {
        None = 0,
        Schedule = 1,
        ParentPriority = 2,
        SpecificPriority = 4,
        ExecutionOrder = 8,
        SpecificOrder = 16,
        Presentation = 32,
        ColumnPresentation = 64,
        // WorkPrioritySystem already notifies every player pawn when this
        // global switch changes. Keeping it distinct from ParentPriority
        // lets publication retain the broad visual invalidation without
        // scheduling a second execution-wide notification pass.
        ManualPriorityMode = 128,
        // A raised priority maximum changes effective priority calculations,
        // but it is not a parent-priority target and must not trigger an
        // external target mirror by itself.
        PriorityConfiguration = 256
    }

    internal readonly struct WorkTabApplicationRevision
    {
        internal WorkTabApplicationRevision(int epoch, int value) { Epoch = epoch; Value = value; }
        internal int Epoch { get; }
        internal int Value { get; }
    }

    internal readonly struct WorkTabApplicationResult
    {
        internal WorkTabApplicationResult(
            WorkTabApplicationOutcome outcome,
            string reason,
            WorkTabApplicationChange change,
            WorkTabApplicationRevision revision)
            : this(
                outcome,
                reason,
                change,
                WorkTabRevisionVector.FromApplication(revision))
        {
        }

        internal WorkTabApplicationResult(
            WorkTabApplicationOutcome outcome,
            string reason,
            WorkTabApplicationChange change,
            WorkTabRevisionVector revision)
        {
            Outcome = outcome;
            Reason = reason;
            Change = change;
            RevisionVector = revision;
            Revision = revision.Application;
            BeforeRevisionVector = change.IsEmpty ? revision : change.BeforeRevisionVector;
            AfterRevisionVector = change.IsEmpty ? revision : change.AfterRevisionVector;
            BeforeRevision = BeforeRevisionVector.Application;
            AfterRevision = AfterRevisionVector.Application;
        }
        internal WorkTabApplicationOutcome Outcome { get; }
        internal string Reason { get; }
        internal WorkTabApplicationChange Change { get; }
        internal WorkTabApplicationRevision Revision { get; }
        internal WorkTabRevisionVector RevisionVector { get; }
        internal WorkTabApplicationRevision BeforeRevision { get; }
        internal WorkTabApplicationRevision AfterRevision { get; }
        internal WorkTabRevisionVector BeforeRevisionVector { get; }
        internal WorkTabRevisionVector AfterRevisionVector { get; }
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

    /// <summary>
    /// Typed compatibility-import payload. Integrations describe values only;
    /// this application boundary owns validation, batching, rollback, and
    /// publication of the resulting Work-tab mutation.
    /// </summary>
    internal sealed class WorkTabTrustedImport
    {
        internal readonly List<WorkTabTrustedParentPriority> ParentPriorities =
            new List<WorkTabTrustedParentPriority>();
        internal readonly List<WorkTabTrustedSpecificPriority> SpecificPriorities =
            new List<WorkTabTrustedSpecificPriority>();
        internal readonly List<WorkTabTrustedSpecificOrder> SpecificOrders =
            new List<WorkTabTrustedSpecificOrder>();
        internal readonly List<WorkTabScheduleCommand> Schedules =
            new List<WorkTabScheduleCommand>();
        internal int? RequiredPriorityMaximum;
    }

    internal readonly struct WorkTabTrustedParentPriority
    {
        internal WorkTabTrustedParentPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            Pawn = pawn;
            WorkType = workType;
            Priority = priority;
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal int Priority { get; }
    }

    internal readonly struct WorkTabTrustedSpecificPriority
    {
        internal WorkTabTrustedSpecificPriority(Pawn pawn, WorkGiverDef workGiver, int? priority)
        {
            Pawn = pawn;
            WorkGiver = workGiver;
            Priority = priority;
        }

        internal Pawn Pawn { get; }
        internal WorkGiverDef WorkGiver { get; }
        internal int? Priority { get; }
    }

    internal readonly struct WorkTabTrustedSpecificOrder
    {
        internal WorkTabTrustedSpecificOrder(
            Pawn pawn,
            WorkTypeDef workType,
            IEnumerable<string> orderedWorkGivers)
        {
            Pawn = pawn;
            WorkType = workType;
            OrderedWorkGivers = new List<string>(orderedWorkGivers ?? Enumerable.Empty<string>());
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal IReadOnlyList<string> OrderedWorkGivers { get; }
    }

    internal enum WorkTabSpecificPriorityIntent
    {
        Set = 0,
        RemoveOverride = 1,
        ClearWorkType = 2,
        EnableParent = 3,
        EnableParentAndSet = 4
    }

    internal readonly struct WorkTabSpecificPriorityCommand
    {
        internal WorkTabSpecificPriorityCommand(
            WorkTabSpecificPriorityIntent intent,
            int pawnId,
            string workTypeDefName,
            string workGiverDefName,
            int priority,
            int expectedSpecificRevision,
            long authorityRevision,
            int expectedParentPriority)
        {
            Intent = intent; PawnId = pawnId;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            WorkGiverDefName = workGiverDefName ?? string.Empty; Priority = priority;
            ExpectedSpecificRevision = expectedSpecificRevision; AuthorityRevision = authorityRevision;
            ExpectedParentPriority = expectedParentPriority;
        }

        internal WorkTabSpecificPriorityIntent Intent { get; }
        internal int PawnId { get; }
        internal string WorkTypeDefName { get; }
        internal string WorkGiverDefName { get; }
        internal int Priority { get; }
        internal int ExpectedSpecificRevision { get; }
        internal long AuthorityRevision { get; }
        internal int ExpectedParentPriority { get; }
    }

    /// <summary>Per-world command boundary for priority and schedule writes.</summary>
    internal sealed partial class WorkTabApplication
    {
        private readonly Game _game;
        private readonly TimePriorityScheduleRuntime _scheduleRuntime;
        private readonly WorkTabApplicationPublisher _publisher;
        private bool _applying;
        private bool _ownsParentPrioritySetter;
        private int _epoch;
        private int _revision;

        internal WorkTabApplication(Game game, TimePriorityScheduleRuntime scheduleRuntime, int epoch = 0)
        {
            _game = game;
            _scheduleRuntime = scheduleRuntime;
            _publisher = new WorkTabApplicationPublisher();
            _epoch = epoch;
        }

        internal static WorkTabApplication Current =>
            WorkTabGameRoots.For(Verse.Current.Game)?.Application;
        internal static bool OwnsCurrentParentPrioritySetter =>
            Current?._ownsParentPrioritySetter == true;
        internal WorkTabApplicationRevision Revision => new WorkTabApplicationRevision(_epoch, _revision);
        internal WorkTabRevisionVector RevisionVector => CaptureRevisionVector();
        internal void SetRevisionEpoch(int epoch) { _epoch = epoch; }

        internal bool SetStoredParentPriority(Pawn pawn, WorkTypeDef workType, int priority) =>
            ApplyParent(pawn, workType, priority, -1, null);

        internal bool SetManualPriorityMode(bool enabled)
        {
            if (!IsCurrent || Find.PlaySettings == null)
            {
                return false;
            }

            bool expected = Find.PlaySettings.useWorkPriorities;
            if (expected == enabled)
            {
                return true;
            }
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return false;
            }
            if (MultiplayerBridge.Active)
            {
                SyncSetManualPriorityMode(enabled, expected, authorityRevision);
                return true;
            }

            return ApplyManualPriorityMode(enabled, expected, authorityRevision);
        }

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

        internal WorkTabApplicationResult SubmitDisplayedParentPriorityBatch(
            IReadOnlyList<Pawn> pawns,
            WorkTypeDef workType,
            IReadOnlyList<int> priorities)
        {
            if (!IsCurrent || workType == null || pawns == null || priorities == null ||
                pawns.Count == 0 || pawns.Count != priorities.Count)
                return Reject("The parent-priority batch is invalid.");

            var uniquePawns = new HashSet<int>();
            WorkTabAtomicMutationPlan mutation = WorkTabAtomicMutationPlan.Capture(
                pawns,
                new[] { workType });
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                int priority = priorities[i];
                if (!WorkTabActionability.CanApplyParent(pawn, workType) ||
                    !uniquePawns.Add(pawn.thingIDNumber) ||
                    WorkPrioritySystem.ClampPriority(priority) != priority)
                    return Reject("The parent-priority batch contains an invalid target.");

                int stored = mutation.GetPriority(pawn, workType);
                int fallback = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
                TimePriorityEvaluation presentation =
                    TimePriorityService.EvaluateWorkTypePriority(pawn, workType, fallback);
                if (presentation.HasSchedule && stored > WorkPrioritySystem.DisabledPriority &&
                    TimePriorityService.TryCaptureLiveScheduleSnapshot(
                        presentation.Target,
                        fallback,
                        out TimePriorityLiveScheduleSnapshot schedule,
                        out _) &&
                    schedule.HadSchedule &&
                    schedule.Schedule.IsPinned(presentation.Hour))
                {
                    if (!mutation.SetSchedule(
                            presentation.Target,
                            schedule.Schedule.WithPinnedHour(presentation.Hour, priority),
                            schedule.FallbackPriority))
                        return Reject("A scheduled parent-priority target could not be captured.");
                }
                else if (!mutation.SetPriority(pawn, workType, priority))
                {
                    return Reject("A parent-priority target could not be captured.");
                }
            }

            return ApplyAtomicMutationPlan(mutation);
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
                return Result(WorkTabApplicationOutcome.NoOp);
            if (MultiplayerBridge.Active)
            {
                SyncSubmitSchedule(target.PawnId, (int)target.Kind, target.WorkTypeDefName, target.TargetDefName,
                    expected.FallbackPriority, expected.HadSchedule, expected.ServiceVersion, expected.AuthorityRevision,
                    expected.Schedule.PinnedHourMask, expected.Schedule.CopyPriorities(), value.PinnedHourMask, value.CopyPriorities());
                return Result(WorkTabApplicationOutcome.Submitted);
            }
            return ApplySchedule(expected, value);
        }

        internal WorkTabApplicationResult SubmitColumnOrder(
            List<string> orderedWorkTypes,
            List<string> movedWorkTypes) =>
            SubmitColumnOrder(orderedWorkTypes, movedWorkTypes, applyToWorkTable: true, clearResetPresentation: false);

        internal WorkTabApplicationResult SubmitCapturedColumnOrder(List<string> orderedWorkTypes) =>
            SubmitColumnOrder(orderedWorkTypes, null, applyToWorkTable: false, clearResetPresentation: false);

        private WorkTabApplicationResult SubmitColumnOrder(
            List<string> orderedWorkTypes,
            List<string> movedWorkTypes,
            bool applyToWorkTable,
            bool clearResetPresentation)
        {
            IWorkTabColumnOrderState columnOrder =
                WorkTabGameRoots.For(_game)?.State?.ColumnOrder;
            if (!IsCurrent || columnOrder == null || orderedWorkTypes == null ||
                orderedWorkTypes.Count == 0)
            {
                return Reject("The work-column order command is not available for this world.");
            }

            int expectedGeneration = columnOrder.Generation;
            if (MultiplayerBridge.Active)
            {
                SyncApplyColumnOrder(
                    new List<string>(orderedWorkTypes),
                    movedWorkTypes == null ? null : new List<string>(movedWorkTypes),
                    expectedGeneration,
                    applyToWorkTable,
                    clearResetPresentation);
                return Result(WorkTabApplicationOutcome.Submitted);
            }

            return ApplyColumnOrder(
                orderedWorkTypes,
                movedWorkTypes,
                expectedGeneration,
                applyToWorkTable,
                clearResetPresentation);
        }

        internal WorkTabApplicationResult SubmitColumnReset(bool useTrueVanilla)
        {
            return WorkColumnOrderManager.TryGetResetColumnOrder(useTrueVanilla, out List<string> order)
                ? SubmitColumnOrder(order, null, applyToWorkTable: true, clearResetPresentation: true)
                : Reject("The work-column reset target is unavailable.");
        }

        internal WorkTabApplicationResult SubmitSpecificPriority(int pawnId, WorkGiverDef workGiver, int priority) =>
            SubmitSpecificPriorityCommand(WorkTabSpecificPriorityIntent.Set, pawnId, null, workGiver, priority);

        internal WorkTabApplicationResult RemoveSpecificPriority(int pawnId, WorkGiverDef workGiver) =>
            SubmitSpecificPriorityCommand(WorkTabSpecificPriorityIntent.RemoveOverride, pawnId, null, workGiver,
                WorkPrioritySystem.DisabledPriority);

        internal WorkTabApplicationResult ClearSpecificPriorities(Pawn pawn, WorkTypeDef workType) =>
            SubmitSpecificPriorityCommand(WorkTabSpecificPriorityIntent.ClearWorkType,
                pawn?.thingIDNumber ?? TimePriorityTarget.GlobalPawnId, workType, null, WorkPrioritySystem.DisabledPriority);

        internal WorkTabApplicationResult EnableParentFromSpecific(
            Pawn pawn, WorkGiverDef workGiver, int? specificPriority) =>
            SubmitSpecificPriorityCommand(specificPriority.HasValue
                    ? WorkTabSpecificPriorityIntent.EnableParentAndSet
                    : WorkTabSpecificPriorityIntent.EnableParent,
                pawn?.thingIDNumber ?? TimePriorityTarget.GlobalPawnId, null, workGiver,
                specificPriority ?? WorkPrioritySystem.DisabledPriority);

        internal WorkTabApplicationResult SubmitSpecificPriorityBatch(
            WorkGiverDef workGiver,
            List<int> pawnIds,
            List<int> priorities)
        {
            if (!IsCurrent || workGiver == null || pawnIds == null || priorities == null ||
                pawnIds.Count == 0 || pawnIds.Count != priorities.Count ||
                !WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                return Reject("The specific-job priority batch is invalid.");
            }

            int expectedRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (MultiplayerBridge.Active)
            {
                SyncApplySpecificPriorityBatch(
                    workGiver.defName,
                    new List<int>(pawnIds),
                    new List<int>(priorities),
                    expectedRevision,
                    authorityRevision);
                return Result(WorkTabApplicationOutcome.Submitted);
            }

            return ApplySpecificPriorityBatch(
                workGiver.defName,
                pawnIds,
                priorities,
                expectedRevision,
                authorityRevision,
                synchronizedReplay: false);
        }

        internal WorkTabApplicationResult SubmitSpecificOrder(
            Pawn pawn,
            WorkTypeDef workType,
            List<string> orderedWorkGivers)
        {
            if (!IsCurrent || workType == null || orderedWorkGivers == null || orderedWorkGivers.Count == 0)
            {
                return Reject("The specific-job order command is invalid.");
            }

            int expectedRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
                return Reject("The specific-job order authority is unavailable.");
            WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot expectedGlobal = pawn == null
                ? WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(workType.defName)
                : null;
            int pawnId = pawn?.thingIDNumber ?? TimePriorityTarget.GlobalPawnId;
            if (MultiplayerBridge.Active)
            {
                SyncApplySpecificOrder(
                    pawnId,
                    workType.defName,
                    new List<string>(orderedWorkGivers),
                    expectedRevision,
                    expectedGlobal == null ? -1 : (int)expectedGlobal.State,
                    expectedGlobal == null ? null : new List<string>(expectedGlobal.OrderedWorkGiverNames),
                    authorityRevision);
                return Result(WorkTabApplicationOutcome.Submitted);
            }

            return ApplySpecificOrder(
                pawnId,
                workType.defName,
                orderedWorkGivers,
                expectedRevision,
                expectedGlobal,
                authorityRevision,
                synchronizedReplay: false);
        }

        internal WorkTabApplicationResult SubmitSpecificLayout(
            WorkGiverLayoutCommand command, WorkGiverLayoutSnapshot snapshot, int historyAction)
        {
            if (!IsCurrent || command == null || snapshot == null || historyAction < 0 || historyAction > 2)
                return Reject("The specific-job layout command is invalid.");
            WorkGiverLayoutSnapshot expected = historyAction == 1
                ? command.After
                : command.Before;
            List<string> encodedExpected = WorkGiverReassignmentManager.EncodeSnapshot(expected);
            List<string> encoded = WorkGiverReassignmentManager.EncodeSnapshot(snapshot);
            int expectedRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (MultiplayerBridge.Active)
            {
                SyncApplySpecificLayout(command.Id, command.WorkGiverDefName, expectedRevision,
                    expected.TargetWorkTypeDefName, encodedExpected,
                    snapshot.TargetWorkTypeDefName, encoded, historyAction);
                return Result(WorkTabApplicationOutcome.Submitted);
            }
            return ApplySpecificLayout(command.Id, command.WorkGiverDefName, expectedRevision,
                expected.TargetWorkTypeDefName, encodedExpected,
                snapshot.TargetWorkTypeDefName, encoded, historyAction, command);
        }

        private WorkTabApplicationResult SubmitSpecificPriorityCommand(
            WorkTabSpecificPriorityIntent intent,
            int pawnId,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int priority)
        {
            if (!TryCaptureSpecificPriorityCommand(
                    intent,
                    pawnId,
                    workType,
                    workGiver,
                    priority,
                    out WorkTabSpecificPriorityCommand command,
                    out string reason))
            {
                return Reject(reason);
            }

            if (MultiplayerBridge.Active)
            {
                SyncApplySpecificPriority(
                    (int)command.Intent,
                    command.PawnId,
                    command.WorkTypeDefName,
                    command.WorkGiverDefName,
                    command.Priority,
                    command.ExpectedSpecificRevision,
                    command.AuthorityRevision,
                    command.ExpectedParentPriority);
                return Result(WorkTabApplicationOutcome.Submitted);
            }

            return ApplySpecificPriority(command, synchronizedReplay: false);
        }

        internal void CompleteExecutionOrderMutation()
        {
            if (IsCurrent) Publish(default, WorkTabApplicationDimensions.ExecutionOrder, true, true);
        }

        /// <summary>
        /// Starts the application-owned batch lifecycle for a compound live
        /// mutation.  Callers retain their domain plan; this boundary retains
        /// shared revision, notification, and publication ownership.
        /// </summary>
        internal WorkTabMutationScope BeginMutationScope(
            bool includesSchedules,
            bool includesSpecificJobs)
        {
            return IsCurrent
                ? new WorkTabMutationScope(includesSchedules, includesSpecificJobs)
                : null;
        }

        /// <summary>
        /// Applies an atomic plan as one application-owned mutation. Every
        /// plan compiler targets the same stable values; this boundary alone
        /// revalidates, writes, rolls back, and publishes.
        /// </summary>
        internal WorkTabApplicationResult ApplyAtomicMutationPlan(WorkTabAtomicMutationPlan mutation)
        {
            if (mutation == null) return Reject("The ruleset mutation is unavailable.");
            if (MultiplayerBridge.Active)
            {
                if (!mutation.TryEncode(out string payload))
                    return Reject("The ruleset mutation could not be synchronized.");
                SyncApplyAtomicMutationPlan(payload);
                return Result(WorkTabApplicationOutcome.Submitted);
            }

            return ExecuteAtomicMutationPlan(mutation, false, out _);
        }

        private WorkTabApplicationResult ExecuteAtomicMutationPlan(
            WorkTabAtomicMutationPlan mutation,
            bool synchronizedReplay,
            out int changedCount)
        {
            changedCount = 0;
            if (!IsCurrent || mutation == null ||
                (MultiplayerBridge.Active && !synchronizedReplay))
                return Reject("The atomic mutation belongs to another world or transport.");
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(
                    out long authorityRevision))
                return Reject("The priority authority changed.");

            WorkTabStagedMutation staged = LowerAtomicMutationPlan(
                mutation,
                authorityRevision,
                synchronizedReplay,
                out string reason);
            if (staged == null)
                return Reject(reason ?? "The atomic mutation baseline changed.");

            if (!Enter())
                return Reject("Another work-tab command is active.");
            if (!staged.HasRequests)
            {
                Exit();
                return Result(WorkTabApplicationOutcome.NoOp);
            }

            WorkTabStagedMutationReceipt receipt = StageMutation(
                staged,
                out bool stageSucceeded,
                out reason,
                applicationLockHeld: true);
            if (!stageSucceeded)
            {
                if (receipt?.RecoveryRequired == true)
                {
                    WorkTabApplicationChange recoveryChange = Publish(
                        default,
                        receipt.Dimensions,
                        true,
                        true,
                        affectedTargetChanges: receipt.AffectedTargets);
                    changedCount = -1;
                    return Result(
                        WorkTabApplicationOutcome.RecoveryRequired,
                        recoveryChange,
                        reason);
                }
                return Result(
                    WorkTabApplicationOutcome.Rejected,
                    reason: reason);
            }

            if (!receipt.Commit(out WorkTabApplicationChange change, out reason))
            {
                bool restored = receipt.Rollback(out string rollbackReason);
                if (!restored)
                {
                    changedCount = -1;
                    return Result(
                        WorkTabApplicationOutcome.RecoveryRequired,
                        change,
                        reason + " " + rollbackReason);
                }
                return Result(
                    WorkTabApplicationOutcome.FailedRolledBack,
                    reason: reason);
            }

            if (change.IsEmpty)
                return Result(WorkTabApplicationOutcome.NoOp);

            changedCount = receipt.AppliedChangeCount;
            return Result(WorkTabApplicationOutcome.Applied, change);
        }

        private static WorkTabStagedMutation LowerAtomicMutationPlan(
            WorkTabAtomicMutationPlan mutation,
            long authorityRevision,
            bool synchronizedReplay,
            out string reason)
        {
            reason = null;
            var staged = new WorkTabStagedMutation
            {
                AuthorityRevision = authorityRevision,
                SpecificJobRevision = WorkGiverReassignmentManager.CurrentSyncVersion,
                SynchronizedReplay = synchronizedReplay,
                RequiredPriorityMaximum = mutation.RequiredPriorityMaximum,
                ExpectedManualPriorityMode = Find.PlaySettings?.useWorkPriorities == true,
                ManualPriorityTarget = mutation.ManualPrioritiesTarget ??
                    (mutation.RequiresManualPriorities ? true : (bool?)null)
            };
            IReadOnlyList<WorkTabRuleParentPriority> parents = mutation.ParentPriorities;
            for (int i = 0; i < parents.Count; i++)
            {
                WorkTabRuleParentPriority entry = parents[i];
                if (!WorkTabActionability.CanApplyParent(entry.Pawn, entry.WorkType) ||
                    !WorkPrioritySystem.TryGetRawStoredPriority(
                        entry.Pawn.workSettings,
                        entry.WorkType,
                        out int observed) || observed != entry.Expected)
                {
                    reason = "A parent-priority baseline changed.";
                    return null;
                }
                staged.ParentPriorities.Add(new WorkTabStagedParentPriority(
                    entry.Pawn,
                    entry.WorkType,
                    entry.Expected,
                    WorkPrioritySystem.ClampPriority(entry.Desired)));
            }

            IReadOnlyList<WorkTabRuleSpecificPriority> specifics = mutation.SpecificPriorities;
            for (int i = 0; i < specifics.Count; i++)
            {
                WorkTabRuleSpecificPriority entry = specifics[i];
                WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(
                    entry.WorkGiver);
                if (!WorkTabActionability.CanApplySpecific(
                        entry.Pawn,
                        workType,
                        entry.WorkGiver))
                {
                    reason = "A specific-job baseline changed.";
                    return null;
                }
                if (entry.StorageKind == SpecificJobPriorityStorageKind.ExternalAuthority)
                    staged.ExternalSpecificPriorities.Add(entry);
                else
                    staged.SpecificPriorities.Add(
                        new WorkTabStagedSpecificPriority(
                            entry.Pawn.thingIDNumber,
                            entry.WorkGiver.defName,
                            entry.Clear
                                ? WorkTabSpecificPriorityState.LocalInherit
                                : WorkTabSpecificPriorityState.LocalSet,
                            entry.Desired,
                            new WorkTabSpecificPriorityBaseline(
                                entry.HadOverride
                                    ? WorkTabSpecificPriorityState.LocalSet
                                    : WorkTabSpecificPriorityState.LocalInherit,
                                entry.Initial)));
            }

            IReadOnlyList<WorkTabRuleSpecificOrder> orders = mutation.SpecificOrders;
            for (int i = 0; i < orders.Count; i++)
            {
                WorkTabRuleSpecificOrder entry = orders[i];
                staged.SpecificOrders.Add(
                    new WorkTabStagedSpecificOrder(
                        entry.Pawn.thingIDNumber,
                        entry.WorkType.defName,
                        WorkTabSpecificOrderState.LocalStored,
                        entry.Desired,
                        new WorkTabSpecificOrderBaseline(
                            entry.Expected?.HasStoredOrder == true
                                ? WorkTabSpecificOrderState.LocalStored
                                : WorkTabSpecificOrderState.LocalInherit,
                            entry.Expected?.OrderedWorkGiverNames)));
            }

            IReadOnlyList<WorkTabRuleSchedule> schedules = mutation.Schedules;
            for (int i = 0; i < schedules.Count; i++)
            {
                WorkTabRuleSchedule entry = schedules[i];
                staged.Schedules.Add(new WorkTabStagedSchedule(
                    entry.Expected,
                    entry.Desired));
            }
            return staged;
        }

        private static bool TryApplyRequiredPriorityMaximum(
            int? requested,
            PriorityConfigurationSnapshot snapshot,
            out bool changed)
        {
            changed = false;
            if (!requested.HasValue || requested.Value <= PriorityConstants.VanillaMax)
                return true;
            BetterWorkTabSettings settings = snapshot.Settings;
            if (settings == null) return false;
            int required = PriorityAuthorityBroker.ClampMaxPriority(requested.Value);
            if (settings.maxPriorityInt < required)
            {
                settings.maxPriorityInt = required;
                changed = true;
            }
            if (settings.priorityMode == PriorityMode.Vanilla)
            {
                settings.SetPriorityMode(PriorityMode.Auto);
                changed = true;
            }
            if (changed)
            {
                settings.NormalizePrioritySettings();
                PriorityAuthorityBroker.InvalidateCaches();
            }
            return true;
        }

        internal readonly struct PriorityConfigurationSnapshot
        {
            internal PriorityConfigurationSnapshot(BetterWorkTabSettings settings)
            {
                Settings = settings;
                Maximum = settings?.maxPriorityInt ?? PriorityConstants.VanillaMax;
                Mode = settings?.priorityMode ?? PriorityMode.Vanilla;
                Extended = settings?.enableExtendedPriorities ?? false;
                Delegated = settings?.delegateToExternalPriorityMods ?? false;
                ProviderId = settings?.selectedPriorityProviderId;
            }

            internal BetterWorkTabSettings Settings { get; }
            private int Maximum { get; }
            private PriorityMode Mode { get; }
            private bool Extended { get; }
            private bool Delegated { get; }
            private string ProviderId { get; }

            internal bool Restore()
            {
                if (Settings == null) return false;
                Settings.maxPriorityInt = Maximum;
                Settings.priorityMode = Mode;
                Settings.enableExtendedPriorities = Extended;
                Settings.delegateToExternalPriorityMods = Delegated;
                Settings.selectedPriorityProviderId = ProviderId;
                Settings.NormalizePrioritySettings();
                PriorityAuthorityBroker.InvalidateCaches();
                return Settings.maxPriorityInt == Maximum && Settings.priorityMode == Mode &&
                    Settings.enableExtendedPriorities == Extended &&
                    Settings.delegateToExternalPriorityMods == Delegated &&
                    StringComparer.Ordinal.Equals(Settings.selectedPriorityProviderId, ProviderId);
            }

            internal bool MatchesCurrent()
            {
                return Settings != null && Settings.maxPriorityInt == Maximum &&
                    Settings.priorityMode == Mode &&
                    Settings.enableExtendedPriorities == Extended &&
                    Settings.delegateToExternalPriorityMods == Delegated &&
                    StringComparer.Ordinal.Equals(
                        Settings.selectedPriorityProviderId,
                        ProviderId);
            }
        }

        /// <summary>
        /// Completes an already accepted atomic mutation through the single
        /// application publisher.  This is deliberately not a feature-level
        /// "completion" callback: the application decides revision and
        /// invalidation for every dimension.
        /// </summary>
        internal WorkTabApplicationResult PublishAtomicMutation(
            WorkTabApplicationDimensions dimensions,
            bool durable,
            bool broadScope,
            bool mirrorExternal,
            IEnumerable<TimePriorityTarget> affectedTargets = null,
            bool? notifyPawnTables = null)
        {
            if (!IsCurrent)
            {
                return Reject("The completed work-tab mutation belongs to another world.");
            }
            if (dimensions == WorkTabApplicationDimensions.None)
            {
                return Result(WorkTabApplicationOutcome.NoOp);
            }

            WorkTabApplicationChange change = Publish(
                default,
                dimensions,
                broadScope,
                durable,
                affectedTargets: affectedTargets,
                mirrorExternal: mirrorExternal,
                notifyPawnTables: notifyPawnTables);
            return Result(WorkTabApplicationOutcome.Applied, change);
        }

        /// <summary>
        /// Applies one compatibility-import payload. A negative return value
        /// means the import was rejected before publication; zero is a valid
        /// no-op. Multiplayer imports are deliberately refused because their
        /// external sources do not supply a synchronized input contract.
        /// </summary>
        internal int ApplyTrustedCompatibilityImport(WorkTabTrustedImport import)
        {
            if (!IsCurrent || import == null || MultiplayerBridge.Active) return -1;
            WorkTabApplicationResult result = ExecuteAtomicMutationPlan(
                WorkTabAtomicMutationPlan.CaptureTrustedImport(import),
                false,
                out int changedCount);
            return result.Accepted ? changedCount : -1;
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
                using (WorkTabMutationScope mutationScope = BeginMutationScope(
                    includesSchedules: true,
                    includesSpecificJobs: false))
                {
                    var scheduleReceipt = new WorkTabScheduleRevisionReceipt(expected.ServiceVersion);
                    outcome = mutationScope.ApplySchedule(
                        expected, desired, false, null, out changed, out reason);
                    if (outcome == TimePriorityScheduleMutationOutcome.Rejected)
                        return Reject(reason);
                    WorkTabMutationCommit commit = mutationScope.Complete(scheduleReceipt);
                    bool committed = commit.ScheduleChanged;
                    if (!commit.ScheduleRevisionOwned)
                        throw new System.InvalidOperationException("The hourly schedule mutation lost its application-owned revision.");
                    if (changed && !committed)
                        throw new System.InvalidOperationException("A changed hourly schedule was not committed.");
                    WorkTabApplicationChange change = committed
                        ? Publish(default, WorkTabApplicationDimensions.Schedule, true, true,
                            affectedTargets: new[] { expected.Target })
                        : default;
                    return Result(!changed
                        ? WorkTabApplicationOutcome.NoOp
                        : outcome == TimePriorityScheduleMutationOutcome.AppliedAfterAuthorityChange
                            ? WorkTabApplicationOutcome.AppliedAfterAuthorityChange
                            : WorkTabApplicationOutcome.Applied, change);
                }
            }
            finally { Exit(); }
        }

        private WorkTabApplicationResult ApplyColumnOrder(
            IReadOnlyList<string> orderedWorkTypes,
            IReadOnlyList<string> movedWorkTypes,
            int expectedGeneration,
            bool applyToWorkTable,
            bool clearResetPresentation)
        {
            IWorkTabColumnOrderState columnOrder =
                WorkTabGameRoots.For(_game)?.State?.ColumnOrder;
            if (columnOrder == null || columnOrder.Generation != expectedGeneration)
            {
                return Reject("The work-column order changed before this command was applied.");
            }
            if (!Enter())
            {
                return Reject("Another work-tab command is active.");
            }

            try
            {
                bool orderChanged = applyToWorkTable
                    ? WorkColumnOrderManager.TryApplyColumnOrder(
                        orderedWorkTypes,
                        movedWorkTypes)
                    : WorkColumnOrderManager.TryCaptureSharedColumnOrder(orderedWorkTypes);
                bool presentationChanged = clearResetPresentation &&
                    WorkColumnOrderManager.ClearLocalResetPresentationState(PawnTableDefOf.Work);
                if (!orderChanged && !presentationChanged)
                {
                    return Result(WorkTabApplicationOutcome.NoOp);
                }

                if (orderChanged)
                {
                    WorkTabApplicationChange change = Publish(
                        default,
                        WorkTabApplicationDimensions.ExecutionOrder,
                        true,
                        true);
                    return Result(WorkTabApplicationOutcome.Applied, change);
                }

                WorkTabApplicationChange presentationChange = Publish(
                    default,
                    WorkTabApplicationDimensions.ColumnPresentation,
                    true,
                    false,
                    mirrorExternal: false,
                    notifyPawnTables: true);
                return Result(WorkTabApplicationOutcome.Applied, presentationChange);
            }
            finally
            {
                Exit();
            }
        }

        private bool TryCaptureSpecificPriorityCommand(
            WorkTabSpecificPriorityIntent intent,
            int pawnId,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            int priority,
            out WorkTabSpecificPriorityCommand command,
            out string reason)
        {
            command = default;
            reason = null;
            if (!IsCurrent || !System.Enum.IsDefined(typeof(WorkTabSpecificPriorityIntent), intent) ||
                !WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision))
            {
                reason = "The specific-job priority command is not available for this world.";
                return false;
            }

            Pawn pawn = pawnId >= 0 ? TimePriorityService.FindPawn(pawnId) : null;
            if (intent == WorkTabSpecificPriorityIntent.ClearWorkType)
            {
                bool globalClear = intent == WorkTabSpecificPriorityIntent.ClearWorkType && pawnId < 0;
                if (workType == null ||
                    (!globalClear && (pawn == null ||
                     !WorkTabActionability.CanApplyParent(pawn, workType))))
                {
                    reason = "The specific-job work type is not currently actionable.";
                    return false;
                }
            }
            else
            {
                WorkTypeDef resolvedWorkType =
                    WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
                if (workGiver == null || resolvedWorkType == null ||
                    (pawnId >= 0 &&
                     !WorkTabActionability.CanApplySpecific(pawn, resolvedWorkType, workGiver)))
                {
                    reason = "The specific-job target is not currently actionable.";
                    return false;
                }

                workType = resolvedWorkType;
            }

            if ((intent == WorkTabSpecificPriorityIntent.Set ||
                 intent == WorkTabSpecificPriorityIntent.EnableParentAndSet) &&
                (WorkPrioritySystem.ClampPriority(priority) != priority ||
                 priority > WorkPrioritySystem.GetRequestableMaxPriority()))
            {
                reason = "The requested specific-job priority is outside the active range.";
                return false;
            }

            int expectedParentPriority = WorkPrioritySystem.DisabledPriority;
            if (intent == WorkTabSpecificPriorityIntent.EnableParent ||
                intent == WorkTabSpecificPriorityIntent.EnableParentAndSet)
            {
                if (pawn == null ||
                    !WorkPrioritySystem.TryGetRawStoredPriority(
                        pawn.workSettings,
                        workType,
                        out expectedParentPriority))
                {
                    reason = "The parent priority baseline could not be captured.";
                    return false;
                }
            }

            command = new WorkTabSpecificPriorityCommand(
                intent,
                pawnId,
                workType?.defName,
                workGiver?.defName,
                priority,
                WorkGiverReassignmentManager.CurrentSyncVersion,
                authorityRevision,
                expectedParentPriority);
            return true;
        }

        private WorkTabApplicationResult ApplySpecificPriority(
            WorkTabSpecificPriorityCommand command,
            bool synchronizedReplay)
        {
            if (WorkGiverReassignmentManager.CurrentSyncVersion !=
                    command.ExpectedSpecificRevision ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(
                    command.AuthorityRevision))
            {
                return Reject("The specific-job priority baseline changed.");
            }

            Pawn pawn = command.PawnId >= 0
                ? TimePriorityService.FindPawn(command.PawnId)
                : null;
            WorkGiverDef workGiver = command.WorkGiverDefName.NullOrEmpty()
                ? null
                : DefDatabase<WorkGiverDef>.GetNamedSilentFail(
                    command.WorkGiverDefName);
            bool targetsWorkType = command.Intent == WorkTabSpecificPriorityIntent.ClearWorkType;
            bool globalWorkTypeClear = command.Intent == WorkTabSpecificPriorityIntent.ClearWorkType &&
                command.PawnId < 0;
            WorkTypeDef workType = targetsWorkType
                ? DefDatabase<WorkTypeDef>.GetNamedSilentFail(command.WorkTypeDefName)
                : WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            if (workType == null || workType.defName != command.WorkTypeDefName ||
                (targetsWorkType
                    ? !globalWorkTypeClear &&
                      (pawn == null || !WorkTabActionability.CanApplyParent(pawn, workType))
                    : workGiver == null ||
                      (command.PawnId >= 0 &&
                       !WorkTabActionability.CanApplySpecific(pawn, workType, workGiver))))
            {
                return Reject("The specific-job identity or actionability changed.");
            }

            bool activatesParent =
                command.Intent == WorkTabSpecificPriorityIntent.EnableParent ||
                command.Intent == WorkTabSpecificPriorityIntent.EnableParentAndSet;
            if (activatesParent &&
                (!WorkPrioritySystem.TryGetRawStoredPriority(
                     pawn.workSettings,
                     workType,
                     out int storedParent) ||
                 storedParent != command.ExpectedParentPriority))
            {
                return Reject("The parent priority baseline changed.");
            }
            var priorityEntries = new List<WorkTabStagedSpecificPriority>();
            var affectedTargets = new List<TimePriorityTarget>();
            if (targetsWorkType)
            {
                IReadOnlyList<WorkGiver> workGivers =
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
                for (int i = 0; i < workGivers.Count; i++)
                {
                    WorkGiverDef target = workGivers[i]?.def;
                    if (target == null)
                        continue;
                    if (!TryCreateSpecificPriorityBatchEntry(
                            command.PawnId,
                            target,
                            WorkTabSpecificPriorityState.LocalInherit,
                            WorkPrioritySystem.DisabledPriority,
                            command.ExpectedSpecificRevision,
                            command.AuthorityRevision,
                            out WorkTabStagedSpecificPriority entry))
                        return Reject("The specific-job priority baseline changed.");
                    priorityEntries.Add(entry);
                    affectedTargets.Add(TimePriorityTarget.ForWorkGiver(pawn, target));
                }
            }
            else if (command.Intent != WorkTabSpecificPriorityIntent.EnableParent)
            {
                WorkTabSpecificPriorityState desired =
                    command.Intent == WorkTabSpecificPriorityIntent.Set ||
                    command.Intent == WorkTabSpecificPriorityIntent.EnableParentAndSet
                        ? WorkTabSpecificPriorityState.LocalSet
                        : WorkTabSpecificPriorityState.LocalInherit;
                if (!TryCreateSpecificPriorityBatchEntry(
                        command.PawnId,
                        workGiver,
                        desired,
                        command.Priority,
                        command.ExpectedSpecificRevision,
                        command.AuthorityRevision,
                        out WorkTabStagedSpecificPriority entry))
                    return Reject("The specific-job priority baseline changed.");
                priorityEntries.Add(entry);
                affectedTargets.Add(TimePriorityTarget.ForWorkGiver(pawn, workGiver));
            }

            if (!Enter())
            {
                return Reject("Another work-tab command is active.");
            }

            try
            {
                bool parentChanged = false;
                IWorkTabSpecificJobRollbackReceipt specificRollback = null;
                int defaultParentPriority = WorkPrioritySystem.GetDefaultEnabledPriority();
                using (WorkTabMutationScope scope = BeginMutationScope(
                           includesSchedules: false,
                           includesSpecificJobs: priorityEntries.Count > 0))
                {
                    if (priorityEntries.Count > 0 && !scope.TryApplySpecificJobs(
                            priorityEntries,
                            new WorkTabStagedSpecificOrder[0],
                            command.ExpectedSpecificRevision,
                            command.AuthorityRevision,
                            synchronizedReplay,
                            null,
                            out specificRollback,
                            out string reason))
                        return Reject(reason ?? "The specific-job priority batch was rejected.");

                    if (activatesParent &&
                        command.ExpectedParentPriority <= WorkPrioritySystem.DisabledPriority)
                    {
                        if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(command.AuthorityRevision) ||
                            !scope.TrySetParentPriority(
                                pawn,
                                workType,
                                defaultParentPriority,
                                out int observed) ||
                            observed != defaultParentPriority)
                        {
                            bool restored = true;
                            if (specificRollback != null)
                                restored = scope.TryRestoreSpecificJobs(specificRollback, out _);
                            const string failure =
                                "The parent priority changed before it could be enabled.";
                            if (specificRollback == null)
                            {
                                return Reject(failure);
                            }
                            if (restored)
                            {
                                return Result(
                                    WorkTabApplicationOutcome.FailedRolledBack,
                                    reason: failure);
                            }

                            WorkTabMutationCommit recoveryCommit = scope.Complete();
                            WorkTabApplicationDimensions recoveryDimensions =
                                recoveryCommit.SpecificJobsChanged
                                    ? WorkTabApplicationDimensions.SpecificPriority
                                    : WorkTabApplicationDimensions.None;
                            WorkTabApplicationChange recoveryChange =
                                recoveryDimensions == WorkTabApplicationDimensions.None
                                    ? default
                                    : Publish(
                                        TimePriorityTarget.ForWorkGiver(pawn, workGiver),
                                        recoveryDimensions,
                                        true,
                                        true,
                                        affectedTargets);
                            return Result(
                                WorkTabApplicationOutcome.RecoveryRequired,
                                recoveryChange,
                                failure);
                        }

                        parentChanged = true;
                    }

                    WorkTabMutationCommit commit = scope.Complete();
                    WorkTabApplicationDimensions dimensions =
                        (parentChanged
                            ? WorkTabApplicationDimensions.ParentPriority
                            : WorkTabApplicationDimensions.None) |
                        (commit.SpecificJobsChanged
                            ? WorkTabApplicationDimensions.SpecificPriority
                            : WorkTabApplicationDimensions.None);
                    if (dimensions == WorkTabApplicationDimensions.None)
                        return Result(WorkTabApplicationOutcome.NoOp);

                    TimePriorityTarget primaryTarget = workGiver == null
                        ? TimePriorityTarget.ForWorkType(pawn, workType)
                        : TimePriorityTarget.ForWorkGiver(pawn, workGiver);
                    if (parentChanged)
                        affectedTargets.Add(TimePriorityTarget.ForWorkType(pawn, workType));
                    WorkTabApplicationChange change = Publish(
                        primaryTarget,
                        dimensions,
                        command.PawnId < 0,
                        true,
                        affectedTargets);
                    return Result(WorkPrioritySystem.IsBwtMutationAuthorityCurrent(
                            command.AuthorityRevision)
                        ? WorkTabApplicationOutcome.Applied
                        : WorkTabApplicationOutcome.AppliedAfterAuthorityChange,
                        change);
                }
            }
            finally
            {
                Exit();
            }
        }

        private static bool TryCreateSpecificPriorityBatchEntry(
            int pawnId,
            WorkGiverDef workGiver,
            WorkTabSpecificPriorityState desiredState,
            int desiredPriority,
            int expectedRevision,
            long authorityRevision,
            out WorkTabStagedSpecificPriority entry)
        {
            entry = null;
            if (workGiver == null)
                return false;
            if (pawnId < 0)
            {
                WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot expected =
                    WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(
                        workGiver.defName);
                if (expected.SyncVersion != expectedRevision ||
                    expected.AuthorityRevision != authorityRevision)
                    return false;
                entry = new WorkTabStagedSpecificPriority(
                    TimePriorityTarget.GlobalPawnId,
                    workGiver.defName,
                    desiredState,
                    desiredPriority,
                    new WorkTabSpecificPriorityBaseline(
                        ToApplicationPriorityState(expected.State, global: true),
                        expected.Priority));
                return true;
            }

            Pawn pawn = TimePriorityService.FindPawn(pawnId);
            if (!WorkGiverReassignmentManager.TryCapturePawnWorkGiverPriority(
                    pawn,
                    workGiver,
                    out int revision,
                    out bool hadOverride,
                    out int priority,
                    out long capturedAuthority) ||
                revision != expectedRevision ||
                capturedAuthority != authorityRevision)
                return false;
            entry = new WorkTabStagedSpecificPriority(
                pawnId,
                workGiver.defName,
                desiredState == WorkTabSpecificPriorityState.GlobalSet ||
                desiredState == WorkTabSpecificPriorityState.LocalSet
                    ? WorkTabSpecificPriorityState.LocalSet
                    : WorkTabSpecificPriorityState.LocalInherit,
                desiredPriority,
                new WorkTabSpecificPriorityBaseline(
                    hadOverride
                        ? WorkTabSpecificPriorityState.LocalSet
                        : WorkTabSpecificPriorityState.LocalInherit,
                    priority));
            return true;
        }

        private static WorkTabSpecificPriorityState ToApplicationPriorityState(
            WorkGiverReassignmentManager.ExactGlobalStateKind state,
            bool global)
        {
            if (!global)
            {
                return state == WorkGiverReassignmentManager.ExactGlobalStateKind.Set
                    ? WorkTabSpecificPriorityState.LocalSet
                    : WorkTabSpecificPriorityState.LocalInherit;
            }

            switch (state)
            {
                case WorkGiverReassignmentManager.ExactGlobalStateKind.Set:
                    return WorkTabSpecificPriorityState.GlobalSet;
                case WorkGiverReassignmentManager.ExactGlobalStateKind.Clear:
                    return WorkTabSpecificPriorityState.GlobalClear;
                default:
                    return WorkTabSpecificPriorityState.GlobalAbsent;
            }
        }

        private static WorkTabSpecificOrderState ToApplicationOrderState(
            WorkGiverReassignmentManager.ExactGlobalStateKind state)
        {
            switch (state)
            {
                case WorkGiverReassignmentManager.ExactGlobalStateKind.Set:
                    return WorkTabSpecificOrderState.GlobalSet;
                case WorkGiverReassignmentManager.ExactGlobalStateKind.Clear:
                    return WorkTabSpecificOrderState.GlobalClear;
                default:
                    return WorkTabSpecificOrderState.GlobalAbsent;
            }
        }

        private WorkTabApplicationResult ApplySpecificPriorityBatch(
            string workGiverDefName,
            IReadOnlyList<int> pawnIds,
            IReadOnlyList<int> priorities,
            int expectedRevision,
            long authorityRevision,
            bool synchronizedReplay)
        {
            WorkGiverDef workGiver =
                DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            WorkTypeDef workType =
                WorkGiverReassignmentManager.GetTargetWorkType(workGiver);
            if (workGiver == null || workType == null || pawnIds == null ||
                priorities == null || pawnIds.Count == 0 ||
                pawnIds.Count != priorities.Count ||
                expectedRevision != WorkGiverReassignmentManager.CurrentSyncVersion ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return Reject("The specific-job priority batch baseline changed.");
            }

            var pawns = new List<Pawn>(pawnIds.Count);
            var entries = new List<WorkTabStagedSpecificPriority>(
                pawnIds.Count);
            var uniquePawnIds = new HashSet<int>();
            for (int i = 0; i < pawnIds.Count; i++)
            {
                Pawn pawn = TimePriorityService.FindPawn(pawnIds[i]);
                if (!uniquePawnIds.Add(pawnIds[i]) ||
                    !WorkTabActionability.CanApplySpecific(pawn, workType, workGiver) ||
                    WorkPrioritySystem.ClampPriority(priorities[i]) != priorities[i] ||
                    priorities[i] > WorkPrioritySystem.GetRequestableMaxPriority())
                {
                    return Reject("The specific-job priority batch contains an invalid target.");
                }

                pawns.Add(pawn);
                if (!TryCreateSpecificPriorityBatchEntry(
                        pawnIds[i],
                        workGiver,
                        WorkTabSpecificPriorityState.LocalSet,
                        priorities[i],
                        expectedRevision,
                        authorityRevision,
                        out WorkTabStagedSpecificPriority entry))
                    return Reject("The specific-job priority batch baseline changed.");
                entries.Add(entry);
            }
            if (!Enter())
            {
                return Reject("Another work-tab command is active.");
            }

            try
            {
                var affectedTargets = new List<TimePriorityTarget>(pawns.Count);
                for (int i = 0; i < pawns.Count; i++)
                    affectedTargets.Add(
                        TimePriorityTarget.ForWorkGiver(pawns[i], workGiver));

                using (WorkTabMutationScope scope = BeginMutationScope(
                           includesSchedules: false,
                           includesSpecificJobs: true))
                {
                    if (!scope.TryApplySpecificJobs(
                            entries,
                            new WorkTabStagedSpecificOrder[0],
                            expectedRevision,
                            authorityRevision,
                            synchronizedReplay,
                            null,
                            out _,
                            out string reason))
                        return Reject(reason ?? "The specific-job priority batch was rejected.");
                    WorkTabMutationCommit commit = scope.Complete();
                    if (!commit.SpecificJobsChanged)
                        return Result(WorkTabApplicationOutcome.NoOp);

                    WorkTabApplicationChange change = Publish(
                        affectedTargets[0],
                        WorkTabApplicationDimensions.SpecificPriority,
                        false,
                        true,
                        affectedTargets);
                    return Result(WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision)
                        ? WorkTabApplicationOutcome.Applied
                        : WorkTabApplicationOutcome.AppliedAfterAuthorityChange, change);
                }
            }
            finally
            {
                Exit();
            }
        }

        private WorkTabApplicationResult ApplySpecificOrder(
            int pawnId,
            string workTypeDefName,
            IReadOnlyList<string> orderedWorkGivers,
            int expectedRevision,
            WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot expectedGlobal = null,
            long authorityRevision = 0L,
            bool synchronizedReplay = false)
        {
            bool isGlobal = pawnId == TimePriorityTarget.GlobalPawnId;
            Pawn pawn = isGlobal ? null : TimePriorityService.FindPawn(pawnId);
            WorkTypeDef workType =
                DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null ||
                (!isGlobal && (pawn?.workSettings == null || pawn.WorkTypeIsDisabled(workType))) ||
                orderedWorkGivers == null ||
                expectedRevision != WorkGiverReassignmentManager.CurrentSyncVersion ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return Reject("The specific-job order baseline changed.");
            }
            if (!Enter())
            {
                return Reject("Another work-tab command is active.");
            }

            try
            {
                WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot expectedLocal =
                    isGlobal
                        ? null
                        : WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                            pawn,
                            workType);
                if (isGlobal && (expectedGlobal == null ||
                                 expectedGlobal.SyncVersion != expectedRevision ||
                                 expectedGlobal.AuthorityRevision != authorityRevision))
                    return Reject("The global specific-job order baseline is missing.");
                var entry = new WorkTabStagedSpecificOrder(
                    pawnId,
                    workTypeDefName,
                    isGlobal
                        ? WorkTabSpecificOrderState.GlobalSet
                        : WorkTabSpecificOrderState.LocalStored,
                    orderedWorkGivers,
                    isGlobal
                        ? new WorkTabSpecificOrderBaseline(
                            ToApplicationOrderState(expectedGlobal.State),
                            expectedGlobal.OrderedWorkGiverNames)
                        : new WorkTabSpecificOrderBaseline(
                            expectedLocal?.HasStoredOrder == true
                                ? WorkTabSpecificOrderState.LocalStored
                                : WorkTabSpecificOrderState.LocalInherit,
                            expectedLocal?.OrderedWorkGiverNames));
                using (WorkTabMutationScope scope = BeginMutationScope(
                           includesSchedules: false,
                           includesSpecificJobs: true))
                {
                    if (!scope.TryApplySpecificJobs(
                            new WorkTabStagedSpecificPriority[0],
                            new[] { entry },
                            expectedRevision,
                            authorityRevision,
                            synchronizedReplay,
                            null,
                            out _,
                            out string reason))
                        return Reject(reason ?? "The specific-job order baseline changed.");
                    WorkTabMutationCommit commit = scope.Complete();
                    if (!commit.SpecificJobsChanged)
                        return Result(WorkTabApplicationOutcome.NoOp);

                    WorkTabApplicationChange change = Publish(
                        default,
                        WorkTabApplicationDimensions.SpecificOrder |
                        WorkTabApplicationDimensions.ExecutionOrder,
                        isGlobal,
                        true);
                    return Result(WorkTabApplicationOutcome.Applied, change);
                }
            }
            finally
            {
                Exit();
            }
        }

        private WorkTabApplicationResult ApplySpecificLayout(
            long commandId, string workGiverDefName, int expectedRevision,
            string expectedTargetWorkTypeDefName, List<string> encodedExpectedOrders,
            string targetWorkTypeDefName, List<string> encodedOrders, int historyAction,
            WorkGiverLayoutCommand localCommand)
        {
            WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (!IsCurrent || workGiver == null || historyAction < 0 || historyAction > 2 || !Enter())
                return Reject("The specific-job layout command is no longer actionable.");
            try
            {
                if (!WorkGiverReassignmentManager.ApplySpecificLayout(
                        commandId, workGiverDefName, expectedRevision,
                        expectedTargetWorkTypeDefName, encodedExpectedOrders,
                        targetWorkTypeDefName, encodedOrders, historyAction,
                        localCommand, out bool schedulesChanged))
                    return Reject("The specific-job layout baseline changed.");

                WorkTabApplicationDimensions dimensions = WorkTabApplicationDimensions.SpecificOrder |
                    WorkTabApplicationDimensions.ExecutionOrder |
                    (schedulesChanged ? WorkTabApplicationDimensions.Schedule : WorkTabApplicationDimensions.None);
                TimePriorityTarget target = TimePriorityTarget.ForWorkGiver(null, workGiver);
                WorkTabApplicationChange change = Publish(
                    target,
                    dimensions,
                    true,
                    true,
                    forceExternalMirror: true);
                return Result(WorkTabApplicationOutcome.Applied, change);
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
                return Result(WorkTabApplicationOutcome.Submitted);
            }
            return ApplyFullDay(request, synchronizedReplay: false);
        }

        private WorkTabApplicationResult ApplyFullDay(
            FullDayRequest request,
            bool synchronizedReplay)
        {
            if (!Enter()) return Reject("Another work-tab command is active.");
            try
            {
                using (WorkTabMutationScope scope = BeginMutationScope(
                           includesSchedules: true,
                           includesSpecificJobs: request.WorkGiver != null))
                {
                    if (!Validate(request))
                        return Reject("The priority baseline changed before the schedule could be cleared.");

                    TimePriorityScheduleMutationOutcome scheduleOutcome = scope.ApplySchedule(
                        request.Schedule,
                        null,
                        synchronizedReplay,
                        null,
                        out bool scheduleChanged,
                        out string reason);
                    if (scheduleOutcome == TimePriorityScheduleMutationOutcome.Rejected)
                        return Reject(reason ?? "The hourly schedule clear was rejected.");

                    PriorityMutationOutcome outcome;
                    bool applied;
                    bool priorityChanged;
                    if (request.WorkGiver == null)
                    {
                        outcome = ApplyParentPriority(
                            request.Pawn.workSettings,
                            request.WorkType,
                            request.Priority,
                            new ParentPriorityCommandExpectation(
                                request.Schedule.AuthorityRevision,
                                request.ParentPriority));
                        applied = outcome != PriorityMutationOutcome.Rejected;
                        priorityChanged = applied && WouldChange(request);
                    }
                    else
                    {
                        IWorkTabSpecificJobRollbackReceipt rollback = null;
                        applied = TryCreateSpecificPriorityBatchEntry(
                                request.Pawn?.thingIDNumber ?? TimePriorityTarget.GlobalPawnId,
                                request.WorkGiver,
                                request.Pawn == null
                                    ? WorkTabSpecificPriorityState.GlobalSet
                                    : WorkTabSpecificPriorityState.LocalSet,
                                request.Priority,
                                request.SpecificRevision,
                                request.Schedule.AuthorityRevision,
                                out WorkTabStagedSpecificPriority entry) &&
                            scope.TryApplySpecificJobs(
                                new[] { entry },
                                new WorkTabStagedSpecificOrder[0],
                                request.SpecificRevision,
                                request.Schedule.AuthorityRevision,
                                synchronizedReplay,
                                null,
                                out rollback,
                                out reason);
                        priorityChanged = applied && rollback != null;
                        outcome = !applied
                            ? PriorityMutationOutcome.Rejected
                            : WorkPrioritySystem.IsBwtMutationAuthorityCurrent(
                                request.Schedule.AuthorityRevision)
                                ? PriorityMutationOutcome.Applied
                                : PriorityMutationOutcome.AppliedAfterAuthorityChange;
                    }
                    if (!applied)
                    {
                        reason = reason ?? "The priority baseline changed; the hourly schedule was restored.";
                        if (!scheduleChanged)
                            return Reject(reason);
                        if (scope.TryRestoreSchedule(
                                request.Schedule, null, synchronizedReplay, null, 0, 0, out _))
                            return Result(
                                WorkTabApplicationOutcome.FailedRolledBack,
                                reason: reason);
                    }

                    WorkTabMutationCommit commit = scope.Complete(
                        new WorkTabScheduleRevisionReceipt(request.Schedule.ServiceVersion));
                    WorkTabApplicationDimensions dimensions =
                        (commit.ScheduleChanged ? WorkTabApplicationDimensions.Schedule : WorkTabApplicationDimensions.None) |
                        (priorityChanged || commit.SpecificJobsChanged
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
                        return Result(
                            WorkTabApplicationOutcome.RecoveryRequired,
                            resultChange,
                            reason);
                    if (resultChange.IsEmpty)
                        return Result(WorkTabApplicationOutcome.NoOp);
                    return Result(outcome == PriorityMutationOutcome.AppliedAfterAuthorityChange
                        ? WorkTabApplicationOutcome.AppliedAfterAuthorityChange
                        : WorkTabApplicationOutcome.Applied, resultChange);
                }
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
                    if (outcome == PriorityMutationOutcome.Rejected) return false;
                    if (expected.StoredPriority == priority) return true;
                }
                else
                {
                    using (WorkTabMutationScope scope = BeginMutationScope(
                               includesSchedules: true,
                               includesSpecificJobs: false))
                    {
                        outcome = TimePriorityService.ApplyPriorityAtHour(TimePriorityTarget.ForWorkType(pawn, workType), hour, priority,
                            expected.ScheduleFallbackPriority, expected, pawn.workSettings, workType);
                        if (outcome == PriorityMutationOutcome.Rejected) return false;
                        WorkTabMutationCommit commit = scope.Complete(
                            new WorkTabScheduleRevisionReceipt(expected.ScheduleVersion));
                        if (!commit.ScheduleRevisionOwned)
                            throw new System.InvalidOperationException("The hourly parent mutation lost its application-owned revision.");
                        if (!commit.ScheduleChanged) return true;
                    }
                }
                Publish(TimePriorityTarget.ForWorkType(pawn, workType),
                    WorkTabApplicationDimensions.ParentPriority |
                    (hour >= 0 ? WorkTabApplicationDimensions.Schedule : WorkTabApplicationDimensions.None),
                    false, true);
                return true;
            }
            finally { Exit(); }
        }

        private bool ApplyManualPriorityMode(bool enabled, bool expected, long authorityRevision)
        {
            if (Find.PlaySettings == null ||
                Find.PlaySettings.useWorkPriorities != expected ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision) ||
                !Enter())
            {
                return false;
            }

            try
            {
                if (!WorkPrioritySystem.SetManualPriorities(enabled) ||
                    Find.PlaySettings.useWorkPriorities != enabled)
                {
                    return false;
                }
                Publish(default, WorkTabApplicationDimensions.ManualPriorityMode, true, true);
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
            IEnumerable<TimePriorityTarget> affectedTargets = null,
            bool mirrorExternal = true,
            IEnumerable<WorkTabApplicationTargetChange> affectedTargetChanges = null,
            bool forceExternalMirror = false,
            bool? notifyPawnTables = null)
        {
            WorkTabRevisionVector beforeRevision = CaptureRevisionVector();
            if (durable)
            {
                _revision = unchecked(_revision + 1);
            }
            WorkTabRevisionVector afterRevision = CaptureRevisionVector();
            IEnumerable<WorkTabApplicationTargetChange> targets = affectedTargetChanges ??
                BuildTargetChanges(target, dimensions, affectedTargets);
            WorkTabApplicationEffects effects = EffectsFor(
                dimensions,
                broad,
                durable,
                notifyPawnTables ?? durable,
                mirrorExternal && durable,
                forceExternalMirror);
            return _publisher.Publish(
                target,
                dimensions,
                broad,
                effects,
                beforeRevision,
                afterRevision,
                targets);
        }

        private static WorkTabApplicationEffects EffectsFor(
            WorkTabApplicationDimensions dimensions,
            bool broad,
            bool durable,
            bool notifyPawnTables,
            bool mirrorExternal,
            bool forceExternalMirror)
        {
            WorkTabApplicationEffects effects = WorkTabApplicationEffects.None;
            if (durable)
                effects |= WorkTabApplicationEffects.Persistence;
            bool sparseParentPriorityChange =
                !broad && dimensions == WorkTabApplicationDimensions.ParentPriority;
            if ((dimensions & (WorkTabApplicationDimensions.Schedule |
                               WorkTabApplicationDimensions.ParentPriority |
                               WorkTabApplicationDimensions.SpecificPriority |
                               WorkTabApplicationDimensions.ManualPriorityMode |
                               WorkTabApplicationDimensions.PriorityConfiguration)) != 0)
                effects |= WorkTabApplicationEffects.PriorityInvalidation |
                    WorkTabApplicationEffects.PresentationInvalidation;
            if ((dimensions & WorkTabApplicationDimensions.Schedule) != 0)
                effects |= WorkTabApplicationEffects.ScheduleHourInvalidation;
            if ((dimensions & (WorkTabApplicationDimensions.SpecificPriority |
                               WorkTabApplicationDimensions.SpecificOrder)) != 0)
                effects |= WorkTabApplicationEffects.SubWorkInvalidation |
                    WorkTabApplicationEffects.ColumnLayoutInvalidation |
                    WorkTabApplicationEffects.HeaderGeometryInvalidation;
            if ((dimensions & WorkTabApplicationDimensions.ExecutionOrder) != 0)
                effects |= WorkTabApplicationEffects.ColumnLayoutInvalidation |
                    WorkTabApplicationEffects.HeaderGeometryInvalidation |
                    WorkTabApplicationEffects.RowDescriptorRecache;
            if ((dimensions & WorkTabApplicationDimensions.Presentation) != 0)
                effects |= WorkTabApplicationEffects.PresentationInvalidation |
                    WorkTabApplicationEffects.HeaderTextInvalidation |
                    WorkTabApplicationEffects.HeaderGeometryInvalidation |
                    WorkTabApplicationEffects.RenderResourceInvalidation |
                    WorkTabApplicationEffects.ThemeSettingsInvalidation;
            if ((dimensions & WorkTabApplicationDimensions.ColumnPresentation) != 0)
                effects |= WorkTabApplicationEffects.ColumnLayoutInvalidation |
                    WorkTabApplicationEffects.HeaderGeometryInvalidation;
            bool manualPriorityModeChanged =
                (dimensions & WorkTabApplicationDimensions.ManualPriorityMode) != 0;
            WorkTabApplicationDimensions nonManualDimensions = dimensions &
                ~WorkTabApplicationDimensions.ManualPriorityMode;
            // A precise parent write already invalidates its target through the sparse
            // path; keep it from fanning out into global execution/table refreshes.
            if (!sparseParentPriorityChange &&
                (nonManualDimensions & (WorkTabApplicationDimensions.Schedule |
                                        WorkTabApplicationDimensions.ParentPriority |
                                        WorkTabApplicationDimensions.SpecificPriority |
                                        WorkTabApplicationDimensions.SpecificOrder |
                                        WorkTabApplicationDimensions.ExecutionOrder |
                                        WorkTabApplicationDimensions.PriorityConfiguration)) != 0)
                effects |= WorkTabApplicationEffects.ExecutionRecache;
            // SetManualPriorities performs the vanilla-equivalent pawn
            // notification loop. A manual-only publication therefore must
            // not enqueue the separate global table refresh; combined changes
            // retain it for their non-manual dimensions.
            if (notifyPawnTables &&
                !sparseParentPriorityChange &&
                (!manualPriorityModeChanged ||
                 nonManualDimensions != WorkTabApplicationDimensions.None))
                effects |= WorkTabApplicationEffects.PawnTableRecache;
            if (mirrorExternal &&
                (forceExternalMirror ||
                 (dimensions & (WorkTabApplicationDimensions.Schedule |
                                WorkTabApplicationDimensions.ParentPriority |
                                WorkTabApplicationDimensions.SpecificPriority)) != 0))
                effects |= WorkTabApplicationEffects.ExternalMirror;
            if (!broad && dimensions == WorkTabApplicationDimensions.ParentPriority)
                effects |= WorkTabApplicationEffects.SparseParentPriorityInvalidation;
            return effects;
        }

        private WorkTabRevisionVector CaptureRevisionVector() =>
            new WorkTabRevisionVector(
                Revision,
                PriorityAuthorityBroker.GetObservationalAuthorityRevision(),
                TimePriorityService.CurrentVersion,
                WorkGiverReassignmentManager.CurrentSyncVersion,
                WorkTabPresentationRevision.Current);

        private static IEnumerable<WorkTabApplicationTargetChange> BuildTargetChanges(
            TimePriorityTarget primaryTarget,
            WorkTabApplicationDimensions dimensions,
            IEnumerable<TimePriorityTarget> affectedTargets)
        {
            if (affectedTargets != null)
            {
                var changes = new List<WorkTabApplicationTargetChange>();
                foreach (TimePriorityTarget affectedTarget in affectedTargets)
                {
                    WorkTabApplicationDimensions targetDimensions =
                        DimensionsForTarget(affectedTarget, dimensions);
                    if (targetDimensions != WorkTabApplicationDimensions.None)
                    {
                        changes.Add(new WorkTabApplicationTargetChange(
                            affectedTarget,
                            targetDimensions));
                    }
                }
                return changes;
            }

            WorkTabApplicationDimensions primaryDimensions =
                DimensionsForTarget(primaryTarget, dimensions);
            return primaryDimensions == WorkTabApplicationDimensions.None
                ? new WorkTabApplicationTargetChange[0]
                : new[]
                {
                    new WorkTabApplicationTargetChange(
                        primaryTarget,
                        primaryDimensions)
                };
        }

        private static WorkTabApplicationDimensions DimensionsForTarget(
            TimePriorityTarget target,
            WorkTabApplicationDimensions dimensions)
        {
            if (string.IsNullOrEmpty(target.TargetDefName))
            {
                return WorkTabApplicationDimensions.None;
            }

            const WorkTabApplicationDimensions shared =
                WorkTabApplicationDimensions.Schedule |
                WorkTabApplicationDimensions.SpecificOrder |
                WorkTabApplicationDimensions.ExecutionOrder;
            return target.Kind == TimePriorityTargetKind.WorkType
                ? dimensions & (shared | WorkTabApplicationDimensions.ParentPriority)
                : target.Kind == TimePriorityTargetKind.WorkGiver
                    ? dimensions & (shared | WorkTabApplicationDimensions.SpecificPriority)
                    : WorkTabApplicationDimensions.None;
        }

        private bool Enter() => IsCurrent && !_applying && (_applying = true);
        private void Exit() { _applying = false; }
        private bool IsCurrent => ReferenceEquals(_game, Verse.Current.Game) && _scheduleRuntime != null;
        private WorkTabApplicationResult Result(WorkTabApplicationOutcome outcome,
            WorkTabApplicationChange change = default, string reason = null) =>
            new WorkTabApplicationResult(outcome, reason, change, RevisionVector);
        private static WorkTabApplicationResult Reject(string reason) =>
            WorkTabApplicationResult.Rejected(reason, Current?.Revision ?? default);
        [SyncMethod]
        internal static void SyncApplyAtomicMutationPlan(string payload)
        {
            if (Current != null && WorkTabAtomicMutationPlan.TryDecode(payload, out WorkTabAtomicMutationPlan mutation))
                Current.ExecuteAtomicMutationPlan(mutation, true, out _);
        }

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
        internal static void SyncSetManualPriorityMode(
            bool enabled,
            bool expected,
            long authorityRevision)
        {
            Current?.ApplyManualPriorityMode(enabled, expected, authorityRevision);
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
                specificState, specificPriority, schedule), synchronizedReplay: true);
        }

        [SyncMethod]
        internal static void SyncApplyColumnOrder(
            List<string> orderedWorkTypes, List<string> movedWorkTypes, int expectedGeneration,
            bool applyToWorkTable, bool clearResetPresentation) =>
            Current?.ApplyColumnOrder(
                orderedWorkTypes,
                movedWorkTypes,
                expectedGeneration,
                applyToWorkTable,
                clearResetPresentation);

        [SyncMethod]
        internal static void SyncApplySpecificPriority(
            int intent,
            int pawnId,
            string workTypeDefName,
            string workGiverDefName,
            int priority,
            int expectedSpecificRevision,
            long authorityRevision,
            int expectedParentPriority)
        {
            if (Current == null || !System.Enum.IsDefined(typeof(WorkTabSpecificPriorityIntent), intent)) return;
            Current.ApplySpecificPriority(new WorkTabSpecificPriorityCommand(
                (WorkTabSpecificPriorityIntent)intent, pawnId, workTypeDefName, workGiverDefName,
                priority, expectedSpecificRevision, authorityRevision, expectedParentPriority),
                synchronizedReplay: true);
        }

        [SyncMethod]
        internal static void SyncApplySpecificPriorityBatch(
            string workGiverDefName, List<int> pawnIds, List<int> priorities,
            int expectedSpecificRevision, long authorityRevision) =>
            Current?.ApplySpecificPriorityBatch(workGiverDefName, pawnIds, priorities,
                expectedSpecificRevision, authorityRevision, synchronizedReplay: true);

        [SyncMethod]
        internal static void SyncApplySpecificOrder(
            int pawnId, string workTypeDefName, List<string> orderedWorkGivers,
            int expectedSpecificRevision, int expectedGlobalState,
            List<string> expectedGlobalOrder, long authorityRevision)
        {
            WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot expectedGlobal = expectedGlobalState < 0
                ? null
                : new WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot(
                    workTypeDefName,
                    (WorkGiverReassignmentManager.ExactGlobalStateKind)expectedGlobalState,
                    expectedGlobalOrder,
                    expectedSpecificRevision,
                    authorityRevision);
            Current?.ApplySpecificOrder(pawnId, workTypeDefName, orderedWorkGivers,
                expectedSpecificRevision, expectedGlobal, authorityRevision,
                synchronizedReplay: true);
        }

        [SyncMethod]
        internal static void SyncApplySpecificLayout(
            long commandId, string workGiverDefName, int expectedRevision,
            string expectedTargetWorkTypeDefName, List<string> encodedExpectedOrders,
            string targetWorkTypeDefName, List<string> encodedOrders, int historyAction) =>
            Current?.ApplySpecificLayout(commandId, workGiverDefName, expectedRevision,
                expectedTargetWorkTypeDefName, encodedExpectedOrders,
                targetWorkTypeDefName, encodedOrders, historyAction, null);

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
