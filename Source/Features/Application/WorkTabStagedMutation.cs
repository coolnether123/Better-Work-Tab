using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Foundation.Transactions;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal enum WorkTabSpecificPriorityState
    {
        GlobalAbsent = 0,
        GlobalSet = 1,
        GlobalClear = 2,
        LocalInherit = 3,
        LocalSet = 4
    }

    internal enum WorkTabSpecificOrderState
    {
        GlobalAbsent = 0,
        GlobalSet = 1,
        GlobalClear = 2,
        LocalInherit = 3,
        LocalStored = 4
    }

    internal readonly struct WorkTabSpecificPriorityBaseline
    {
        internal WorkTabSpecificPriorityBaseline(
            WorkTabSpecificPriorityState state,
            int priority)
        {
            State = state;
            Priority = priority;
        }

        internal WorkTabSpecificPriorityState State { get; }
        internal int Priority { get; }
    }

    internal readonly struct WorkTabSpecificOrderBaseline
    {
        internal WorkTabSpecificOrderBaseline(
            WorkTabSpecificOrderState state,
            IReadOnlyList<string> orderedWorkGiverNames)
        {
            State = state;
            OrderedWorkGiverNames = orderedWorkGiverNames ?? new string[0];
        }

        internal WorkTabSpecificOrderState State { get; }
        internal IReadOnlyList<string> OrderedWorkGiverNames { get; }
    }

    internal sealed class WorkTabStagedSpecificPriority
    {
        internal WorkTabStagedSpecificPriority(
            int pawnId,
            string workGiverDefName,
            WorkTabSpecificPriorityState desiredState,
            int desiredPriority,
            WorkTabSpecificPriorityBaseline expected)
        {
            PawnId = pawnId;
            WorkGiverDefName = workGiverDefName ?? string.Empty;
            DesiredState = desiredState;
            DesiredPriority = desiredPriority;
            Expected = expected;
        }

        internal bool IsGlobal => DesiredState == WorkTabSpecificPriorityState.GlobalAbsent ||
            DesiredState == WorkTabSpecificPriorityState.GlobalSet ||
            DesiredState == WorkTabSpecificPriorityState.GlobalClear;
        internal int PawnId { get; }
        internal string WorkGiverDefName { get; }
        internal WorkTabSpecificPriorityState DesiredState { get; }
        internal int DesiredPriority { get; }
        internal WorkTabSpecificPriorityBaseline Expected { get; }
        internal string CanonicalKey =>
            (IsGlobal ? "global" : "local:" + PawnId) + ":priority:" + WorkGiverDefName;
    }

    internal sealed class WorkTabStagedSpecificOrder
    {
        internal WorkTabStagedSpecificOrder(
            int pawnId,
            string workTypeDefName,
            WorkTabSpecificOrderState desiredState,
            IReadOnlyList<string> desiredOrder,
            WorkTabSpecificOrderBaseline expected)
        {
            PawnId = pawnId;
            WorkTypeDefName = workTypeDefName ?? string.Empty;
            DesiredState = desiredState;
            DesiredOrder = desiredOrder ?? new string[0];
            Expected = expected;
        }

        internal bool IsGlobal => DesiredState == WorkTabSpecificOrderState.GlobalAbsent ||
            DesiredState == WorkTabSpecificOrderState.GlobalSet ||
            DesiredState == WorkTabSpecificOrderState.GlobalClear;
        internal int PawnId { get; }
        internal string WorkTypeDefName { get; }
        internal WorkTabSpecificOrderState DesiredState { get; }
        internal IReadOnlyList<string> DesiredOrder { get; }
        internal WorkTabSpecificOrderBaseline Expected { get; }
        internal string CanonicalKey =>
            (IsGlobal ? "global" : "local:" + PawnId) + ":order:" + WorkTypeDefName;
    }

    internal interface IWorkTabSpecificJobRollbackReceipt
    {
        int AppliedRevision { get; }
    }

    internal readonly struct WorkTabStagedParentPriority
    {
        internal WorkTabStagedParentPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int expected,
            int desired)
        {
            Pawn = pawn;
            WorkType = workType;
            Expected = expected;
            Desired = desired;
        }

        internal Pawn Pawn { get; }
        internal WorkTypeDef WorkType { get; }
        internal int Expected { get; }
        internal int Desired { get; }
    }

    internal readonly struct WorkTabStagedSchedule
    {
        internal WorkTabStagedSchedule(
            TimePriorityLiveScheduleSnapshot expected,
            TimePriorityScheduleValue desired)
        {
            Expected = expected;
            Desired = desired;
        }

        internal TimePriorityLiveScheduleSnapshot Expected { get; }
        internal TimePriorityScheduleValue Desired { get; }
    }

    /// <summary>
    /// Application-owned live mutation vocabulary. Feature planners retain
    /// their own diagnostics and identity models, then lower their accepted
    /// writes to this exact-baseline representation.
    /// </summary>
    internal sealed class WorkTabStagedMutation
    {
        internal readonly List<WorkTabStagedParentPriority> ParentPriorities =
            new List<WorkTabStagedParentPriority>();
        internal readonly List<WorkTabStagedSpecificPriority> SpecificPriorities =
            new List<WorkTabStagedSpecificPriority>();
        internal readonly List<WorkTabRuleSpecificPriority> ExternalSpecificPriorities =
            new List<WorkTabRuleSpecificPriority>();
        internal readonly List<WorkTabStagedSpecificOrder> SpecificOrders =
            new List<WorkTabStagedSpecificOrder>();
        internal readonly List<WorkTabStagedSchedule> Schedules =
            new List<WorkTabStagedSchedule>();
        internal readonly List<WorkTabApplicationTargetChange> AffectedTargets =
            new List<WorkTabApplicationTargetChange>();

        internal bool? ManualPriorityTarget;
        // Set only when the staged writer actually attempts a mode transition.
        // ManualPriorityTarget is retained separately for baseline validation
        // and rollback ownership, so an unchanged target does not publish a
        // parent-priority mutation by accident.
        internal bool ManualPriorityModeChanged;
        internal bool ExpectedManualPriorityMode;
        internal long AuthorityRevision;
        internal int SpecificJobRevision;
        internal bool SynchronizedReplay;
        internal WorkTabMutationLease Authorization;
        internal int? RequiredPriorityMaximum;
        internal WorkTabApplicationDimensions AdditionalPublicationDimensions;

        internal bool HasChanges => RequiredPriorityMaximum.HasValue ||
            ManualPriorityTarget.HasValue ||
            ParentPriorities.Count > 0 ||
            ExternalSpecificPriorities.Count > 0 ||
            SpecificPriorities.Count > 0 ||
            SpecificOrders.Count > 0 ||
            Schedules.Count > 0;

        internal WorkTabApplicationDimensions Dimensions
        {
            get
            {
                WorkTabApplicationDimensions dimensions = WorkTabApplicationDimensions.None;
                if (RequiredPriorityMaximum.HasValue || ParentPriorities.Count > 0)
                    dimensions |= WorkTabApplicationDimensions.ParentPriority;
                if (ManualPriorityModeChanged)
                    dimensions |= WorkTabApplicationDimensions.ManualPriorityMode;
                if (SpecificPriorities.Count > 0 || ExternalSpecificPriorities.Count > 0)
                    dimensions |= WorkTabApplicationDimensions.SpecificPriority;
                if (SpecificOrders.Count > 0)
                    dimensions |= WorkTabApplicationDimensions.SpecificOrder |
                        WorkTabApplicationDimensions.ExecutionOrder;
                if (Schedules.Count > 0)
                    dimensions |= WorkTabApplicationDimensions.Schedule;
                return dimensions | AdditionalPublicationDimensions;
            }
        }
    }

    internal sealed class WorkTabStagedMutationReceipt
    {
        private readonly WorkTabApplication _application;
        private readonly WorkTabStagedMutation _mutation;
        private readonly List<WorkTabStagedParentPriority> _appliedParents =
            new List<WorkTabStagedParentPriority>();
        private readonly List<WorkTabStagedSchedule> _appliedSchedules =
            new List<WorkTabStagedSchedule>();
        private readonly List<WorkTabRuleSpecificPriority> _appliedExternalSpecific =
            new List<WorkTabRuleSpecificPriority>();
        private WorkTabMutationScope _scope;
        private IWorkTabSpecificJobRollbackReceipt _specificRollback;
        private WorkTabScheduleRevisionReceipt _scheduleRevision;
        private int _committedSpecificRevision;
        private bool _manualChanged;
        private bool _configurationChanged;
        private WorkTabApplication.PriorityConfigurationSnapshot _configuration;
        private WorkTabApplication.PriorityConfigurationSnapshot _appliedConfiguration;
        private bool _committed;
        private bool _provisional;
        private bool _released;
        private bool _rolledBack;
        private bool _recoveryRequired;

        internal WorkTabStagedMutationReceipt(
            WorkTabApplication application,
            WorkTabStagedMutation mutation,
            WorkTabMutationScope scope)
        {
            _application = application;
            _mutation = mutation;
            _scope = scope;
            _scheduleRevision = mutation.Schedules.Count == 0
                ? null
                : new WorkTabScheduleRevisionReceipt(
                    mutation.Schedules[0].Expected.ServiceVersion);
        }

        internal bool HasChanges => _configurationChanged || _manualChanged ||
            _appliedParents.Count > 0 ||
            _appliedExternalSpecific.Count > 0 || _specificRollback != null ||
            _appliedSchedules.Count > 0;
        internal bool Committed => _committed;
        internal bool StageSucceeded { get; private set; }
        internal bool RecoveryRequired => _recoveryRequired;
        internal bool HasNetChanges => HasChanges && !_rolledBack;
        internal int SpecificJobRevision => _specificRollback?.AppliedRevision ??
            _mutation.SpecificJobRevision;
        internal WorkTabApplicationDimensions Dimensions => _mutation.Dimensions;
        internal IReadOnlyList<WorkTabApplicationTargetChange> AffectedTargets =>
            _mutation.AffectedTargets;

        internal bool TryStage(out string reason)
        {
            reason = null;
            if (_mutation.Schedules.Count > 0)
            {
                TimePriorityLiveScheduleSnapshot first =
                    _mutation.Schedules[0].Expected;
                if (first == null)
                {
                    reason = "A staged schedule baseline is missing.";
                    return false;
                }

                for (int i = 1; i < _mutation.Schedules.Count; i++)
                {
                    TimePriorityLiveScheduleSnapshot expected =
                        _mutation.Schedules[i].Expected;
                    if (expected == null ||
                        expected.ServiceVersion != first.ServiceVersion)
                    {
                        reason = "The staged schedules were not captured from one service revision.";
                        return false;
                    }
                }
            }

            if (!_application.TryStagePriorityConfiguration(
                    _mutation.RequiredPriorityMaximum,
                    out _configuration,
                    out _configurationChanged))
            {
                reason = "The requested priority configuration is unavailable.";
                return false;
            }
            if (_configurationChanged)
                _appliedConfiguration = new WorkTabApplication.PriorityConfigurationSnapshot(
                    BetterWorkTabMod.Settings);
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(
                    _mutation.AuthorityRevision))
            {
                reason = "The priority authority changed before the staged mutation.";
                return false;
            }

            if (_mutation.ManualPriorityTarget.HasValue)
            {
                bool current = Find.PlaySettings?.useWorkPriorities == true;
                if (current != _mutation.ExpectedManualPriorityMode)
                {
                    reason = "The manual-priority baseline changed.";
                    return false;
                }
                if (current != _mutation.ManualPriorityTarget.Value)
                {
                    _manualChanged = true;
                    // The writer owns the vanilla-equivalent pawn notification
                    // loop. Record this before calling it so a partial writer
                    // failure still carries the correct recovery dimensions.
                    _mutation.ManualPriorityModeChanged = true;
                    if (!_scope.TrySetManualPriorityMode(
                            _mutation.ManualPriorityTarget.Value,
                            out bool observed) ||
                        observed != _mutation.ManualPriorityTarget.Value)
                    {
                        reason = "The manual-priority writer rejected the staged mutation.";
                        return false;
                    }
                }
            }

            for (int i = 0; i < _mutation.ParentPriorities.Count; i++)
            {
                WorkTabStagedParentPriority entry = _mutation.ParentPriorities[i];
                if (entry.Pawn?.workSettings == null || entry.WorkType == null ||
                    PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        entry.Pawn.workSettings,
                        entry.WorkType) != entry.Expected)
                {
                    reason = "A staged parent-priority baseline changed.";
                    return false;
                }
                if (entry.Expected == entry.Desired)
                    continue;
                _appliedParents.Add(entry);
                if (!_scope.TrySetParentPriority(
                        entry.Pawn,
                        entry.WorkType,
                        entry.Desired,
                        out int observed) ||
                    observed != entry.Desired)
                {
                    reason = "A staged parent priority could not be written.";
                    return false;
                }
            }

            for (int i = 0; i < _mutation.ExternalSpecificPriorities.Count; i++)
            {
                WorkTabRuleSpecificPriority entry =
                    _mutation.ExternalSpecificPriorities[i];
                bool present = SpecificJobPriorityAuthorityAdapter.TryRead(
                    entry.StorageKind,
                    entry.Pawn,
                    entry.WorkGiver,
                    out int observed);
                if (present != entry.HadOverride || present && observed != entry.Initial)
                {
                    reason = "An external specific-job baseline changed.";
                    return false;
                }
                if ((entry.Clear && !present) ||
                    (!entry.Clear && present && observed == entry.Desired))
                    continue;
                _appliedExternalSpecific.Add(entry);
                if (!SpecificJobPriorityAuthorityAdapter.TryWriteExternal(
                        entry.StorageKind,
                        entry.Pawn,
                        entry.WorkGiver,
                        entry.Clear ? (int?)null : entry.Desired))
                {
                    reason = "An external specific-job priority could not be staged.";
                    return false;
                }
            }

            if ((_mutation.SpecificPriorities.Count > 0 ||
                 _mutation.SpecificOrders.Count > 0) &&
                !_scope.TryApplySpecificJobs(
                    _mutation.SpecificPriorities,
                    _mutation.SpecificOrders,
                    _mutation.SpecificJobRevision,
                    _mutation.AuthorityRevision,
                    _mutation.SynchronizedReplay,
                    _mutation.Authorization,
                    out _specificRollback,
                    out reason))
            {
                reason = reason ?? "The staged specific-job batch was rejected.";
                return false;
            }

            for (int i = 0; i < _mutation.Schedules.Count; i++)
            {
                WorkTabStagedSchedule entry = _mutation.Schedules[i];
                _appliedSchedules.Add(entry);
                if (_scope.ApplySchedule(
                        entry.Expected,
                        entry.Desired,
                        _mutation.SynchronizedReplay,
                        _mutation.Authorization,
                        out bool changed,
                        out reason) == TimePriorityScheduleMutationOutcome.Rejected)
                {
                    reason = reason ?? "A staged schedule was rejected.";
                    return false;
                }
                if (!changed)
                    _appliedSchedules.RemoveAt(_appliedSchedules.Count - 1);
            }

            StageSucceeded = WorkPrioritySystem.IsBwtMutationAuthorityCurrent(
                _mutation.AuthorityRevision);
            if (!StageSucceeded)
                reason = "Priority authority changed after the staged mutation.";
            return StageSucceeded;
        }

        internal bool Commit(
            out WorkTabApplicationChange change,
            out string reason,
            bool provisional = false)
        {
            change = default;
            reason = null;
            if (_released || _scope == null)
            {
                reason = "The staged mutation receipt is no longer active.";
                return false;
            }

            WorkTabMutationCommit commit = _scope.Complete(_scheduleRevision);
            _scope.Dispose();
            _scope = null;
            _committed = true;
            _provisional = provisional;
            _committedSpecificRevision = WorkTabDomainPorts.SpecificJobs.Revision;
            if (_mutation.Schedules.Count > 0 && !commit.ScheduleRevisionOwned)
            {
                reason = "The staged schedule transaction lost its owned revision.";
                _recoveryRequired = true;
                _application.ReleaseStagedMutation();
                _released = true;
                return false;
            }

            _application.ReleaseStagedMutation();
            _released = true;
            if (provisional &&
                (HasChanges || commit.ScheduleChanged || commit.SpecificJobsChanged ||
                 _mutation.AdditionalPublicationDimensions !=
                    WorkTabApplicationDimensions.None))
            {
                _application.InvalidateProvisionalStagedMutation(
                    _mutation,
                    _configurationChanged);
            }
            if (!provisional &&
                (HasChanges || commit.ScheduleChanged || commit.SpecificJobsChanged ||
                _mutation.AdditionalPublicationDimensions !=
                    WorkTabApplicationDimensions.None))
            {
                change = _application.PublishStagedMutation(
                    _mutation,
                    _configurationChanged);
            }
            return true;
        }

        internal bool FinalizeProvisionalCommit(
            out WorkTabApplicationChange change,
            out string reason)
        {
            change = default;
            reason = null;
            if (!_committed || _rolledBack || !_provisional)
            {
                reason = "The staged mutation is not awaiting provisional confirmation.";
                return false;
            }

            if (HasChanges || _mutation.AdditionalPublicationDimensions !=
                WorkTabApplicationDimensions.None)
            {
                change = _application.PublishStagedMutation(
                    _mutation,
                    _configurationChanged);
            }
            _provisional = false;
            return true;
        }

        internal void IncludePublicationDimensions(
            WorkTabApplicationDimensions dimensions)
        {
            if (!_committed && !_released)
                _mutation.AdditionalPublicationDimensions |= dimensions;
        }

        internal bool Rollback(out string reason)
        {
            reason = null;
            if (_rolledBack)
                return true;
            if (_committed)
                return RollbackCommitted(out reason);
            if (_scope == null)
                return true;

            bool restored = RestoreWithScope(_scope, false, out reason);
            if (!restored)
            {
                _recoveryRequired = true;
                return false;
            }
            _scope.Dispose();
            _scope = null;
            _application.ReleaseStagedMutation();
            _released = true;
            _rolledBack = true;
            return true;
        }

        private bool RollbackCommitted(out string reason)
        {
            reason = null;
            bool hadSchedules = _appliedSchedules.Count > 0;
            bool hadSpecificJobs = _specificRollback != null;
            int parentCount = _appliedParents.Count;
            int externalSpecificCount = _appliedExternalSpecific.Count;
            bool hadManual = _manualChanged;
            bool hadConfiguration = _configurationChanged;
            WorkTabMutationScope rollbackScope =
                _application.BeginStagedRollbackScope(
                    hadSchedules,
                    hadSpecificJobs);
            if (rollbackScope == null)
            {
                reason = "The Work-tab application is unavailable for staged rollback.";
                return false;
            }

            bool restored = RestoreWithScope(rollbackScope, true, out reason);
            WorkTabMutationCommit commit = rollbackScope.Complete(
                hadSchedules ? _scheduleRevision : null);
            rollbackScope.Dispose();
            if (hadSchedules && !commit.ScheduleRevisionOwned)
            {
                restored = false;
                reason = "The staged schedule rollback lost its owned revision.";
            }
            bool rollbackChanged = commit.ScheduleChanged ||
                commit.SpecificJobsChanged ||
                parentCount != _appliedParents.Count ||
                externalSpecificCount != _appliedExternalSpecific.Count ||
                hadManual != _manualChanged ||
                hadConfiguration != _configurationChanged;
            _application.ReleaseStagedMutation();
            if (rollbackChanged)
            {
                if (_provisional)
                    _application.InvalidateProvisionalStagedMutation(
                        _mutation,
                        hadConfiguration);
                else
                    _application.PublishStagedMutation(
                        _mutation,
                        hadConfiguration);
            }
            _recoveryRequired = !restored;
            _rolledBack = restored;
            if (restored)
                _provisional = false;
            return restored;
        }

        private bool RestoreWithScope(
            WorkTabMutationScope scope,
            bool committed,
            out string reason)
        {
            reason = null;
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(
                    _mutation.AuthorityRevision))
            {
                reason = "Priority authority changed before staged rollback.";
                return false;
            }
            bool restored = true;
            for (int i = _appliedSchedules.Count - 1; i >= 0; i--)
            {
                WorkTabStagedSchedule entry = _appliedSchedules[i];
                TimePriorityLiveScheduleSnapshot current = null;
                if (committed && !TimePriorityService.TryCaptureLiveScheduleSnapshot(
                        entry.Expected.Target,
                        entry.Expected.FallbackPriority,
                        out current,
                        out reason))
                {
                    restored = false;
                    continue;
                }
                bool scheduleRestored = scope.TryRestoreSchedule(
                    entry.Expected,
                    current,
                    _mutation.SynchronizedReplay,
                    _mutation.Authorization,
                    _scheduleRevision?.InitialRevision ?? 0,
                    _scheduleRevision?.OwnedRevision ?? 0,
                    out reason);
                restored &= scheduleRestored;
                if (scheduleRestored)
                    _appliedSchedules.RemoveAt(i);
            }
            if (_specificRollback != null)
            {
                bool ownsSpecificState = !committed ||
                    WorkTabDomainPorts.SpecificJobs.Revision ==
                        _committedSpecificRevision;
                bool specificRestored = ownsSpecificState &&
                    scope.TryRestoreSpecificJobs(
                        _specificRollback,
                        out reason);
                if (!ownsSpecificState)
                    reason = "Specific-job state changed before staged rollback.";
                restored &= specificRestored;
                if (specificRestored)
                {
                    _specificRollback = null;
                    _committedSpecificRevision = WorkTabDomainPorts.SpecificJobs.Revision;
                }
            }
            for (int i = _appliedExternalSpecific.Count - 1; i >= 0; i--)
            {
                WorkTabRuleSpecificPriority entry = _appliedExternalSpecific[i];
                bool present = SpecificJobPriorityAuthorityAdapter.TryRead(
                    entry.StorageKind,
                    entry.Pawn,
                    entry.WorkGiver,
                    out int observed);
                bool ownsAppliedValue = !committed ||
                    (entry.Clear
                        ? !present
                        : present && observed == entry.Desired);
                bool externalRestored = ownsAppliedValue &&
                    SpecificJobPriorityAuthorityAdapter.TryRestoreExternal(
                    new SpecificJobPriorityStorageSnapshot(
                        entry.StorageKind,
                        entry.HadOverride,
                        entry.Initial),
                    entry.Pawn,
                    entry.WorkGiver);
                restored &= externalRestored;
                if (externalRestored)
                    _appliedExternalSpecific.RemoveAt(i);
            }
            for (int i = _appliedParents.Count - 1; i >= 0; i--)
            {
                WorkTabStagedParentPriority parent = _appliedParents[i];
                bool ownsAppliedValue = !committed ||
                    parent.Pawn?.workSettings != null &&
                    PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        parent.Pawn.workSettings,
                        parent.WorkType) == parent.Desired;
                bool parentRestored = ownsAppliedValue &&
                    scope.TryRestoreParentPriority(
                        parent.Pawn,
                        parent.WorkType,
                        parent.Expected);
                restored &= parentRestored;
                if (parentRestored)
                    _appliedParents.RemoveAt(i);
            }
            if (_manualChanged)
            {
                bool ownsAppliedValue = !committed ||
                    Find.PlaySettings?.useWorkPriorities ==
                        _mutation.ManualPriorityTarget.Value;
                bool manualRestored = ownsAppliedValue &&
                    scope.TrySetManualPriorityMode(
                        _mutation.ExpectedManualPriorityMode,
                        out bool observed) &&
                    observed == _mutation.ExpectedManualPriorityMode;
                restored &= manualRestored;
                if (manualRestored)
                    _manualChanged = false;
            }
            if (_configurationChanged)
            {
                bool configurationRestored =
                    (!committed || _appliedConfiguration.MatchesCurrent()) &&
                    _configuration.Restore();
                restored &= configurationRestored;
                if (configurationRestored)
                    _configurationChanged = false;
            }
            return restored;
        }
    }

    internal sealed partial class WorkTabApplication
    {
        internal WorkTabStagedMutationReceipt StageMutation(
            WorkTabStagedMutation mutation,
            out bool staged,
            out string reason,
            bool applicationLockHeld = false)
        {
            staged = false;
            reason = null;
            if (!IsCurrent || mutation == null || !mutation.HasChanges ||
                (!applicationLockHeld && !Enter()))
            {
                reason = "The staged Work-tab mutation is not actionable.";
                return null;
            }

            WorkTabMutationScope scope = BeginMutationScope(
                mutation.Schedules.Count > 0,
                mutation.SpecificPriorities.Count > 0 ||
                mutation.SpecificOrders.Count > 0);
            if (scope == null)
            {
                Exit();
                reason = "The Work-tab mutation scope is unavailable.";
                return null;
            }

            var receipt = new WorkTabStagedMutationReceipt(this, mutation, scope);
            if (receipt.TryStage(out reason))
            {
                staged = true;
                return receipt;
            }

            string rollbackReason;
            if (!receipt.Rollback(out rollbackReason))
            {
                reason = (reason ?? "The staged mutation failed.") +
                    " Rollback requires recovery: " + rollbackReason;
                return receipt;
            }
            return receipt;
        }

        internal void ReleaseStagedMutation() => Exit();

        internal WorkTabMutationScope BeginStagedRollbackScope(
            bool includesSchedules,
            bool includesSpecificJobs)
        {
            if (!Enter())
                return null;
            WorkTabMutationScope scope = BeginMutationScope(
                includesSchedules,
                includesSpecificJobs);
            if (scope == null)
                Exit();
            return scope;
        }

        internal WorkTabApplicationChange PublishStagedMutation(
            WorkTabStagedMutation mutation,
            bool configurationChanged)
        {
            WorkTabApplicationDimensions dimensions = mutation.Dimensions;
            if (configurationChanged)
                dimensions |= WorkTabApplicationDimensions.Presentation;
            bool broad = configurationChanged || mutation.ManualPriorityTarget.HasValue ||
                mutation.AffectedTargets.Count == 0;
            for (int i = 0; !broad && i < mutation.AffectedTargets.Count; i++)
                broad = mutation.AffectedTargets[i].Target.IsGlobal;
            return Publish(
                default,
                dimensions,
                broad,
                true,
                affectedTargetChanges: mutation.AffectedTargets);
        }

        internal bool TryStagePriorityConfiguration(
            int? requested,
            out PriorityConfigurationSnapshot snapshot,
            out bool changed)
        {
            snapshot = new PriorityConfigurationSnapshot(BetterWorkTabMod.Settings);
            return TryApplyRequiredPriorityMaximum(requested, snapshot, out changed);
        }

        internal void InvalidateProvisionalStagedMutation(
            WorkTabStagedMutation mutation,
            bool configurationChanged)
        {
            WorkTabApplicationDimensions dimensions = mutation.Dimensions;
            if (configurationChanged)
                dimensions |= WorkTabApplicationDimensions.Presentation;
            _publisher.InvalidateTransient(EffectsFor(
                dimensions,
                false,
                false,
                false,
                false,
                false));
        }

        internal void InvalidateProvisionalMutation(
            WorkTabApplicationDimensions dimensions)
        {
            _publisher.InvalidateTransient(EffectsFor(
                dimensions,
                true,
                false,
                false,
                false,
                false));
        }
    }
}
