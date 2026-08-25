using Better_Work_Tab;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Foundation.Canonicalization;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.Foundation.Transactions;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.ModSupport;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Central coordinator for workgiver reassignment lookups, caching, and mutation.
    /// </summary>
    internal static partial class WorkGiverReassignmentManager
    {
        private static readonly Dictionary<int, WorkTypeDef> WorkGiverTargetCache = new Dictionary<int, WorkTypeDef>();
        private static readonly Dictionary<string, List<WorkGiverDef>> WorkGiverTopologyCache =
            new Dictionary<string, List<WorkGiverDef>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<WorkGiver>> OrderedWorkGiverCache = new Dictionary<string, List<WorkGiver>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<WorkGiver>> DisplayWorkGiverCache = new Dictionary<string, List<WorkGiver>>(StringComparer.Ordinal);
        private static bool _workGiverTopologyBuilt;
        private static int _mutationBatchDepth;
        private static bool _mutationBatchChanged;

        private static int _cachedSyncVersion = -1;
        private static int _cachedActivationSyncVersion = int.MinValue;
        private static IWorkTabReassignmentState _cachedActivationState;
        private static bool _cachedHasAnyData;
        private static BetterWorkTabSettings Settings => BetterWorkTabMod.Settings;

        /// <summary>
        /// Exact persisted state for one pawn/work-type order. The effective
        /// display order is not enough for rollback because it can be inherited
        /// from the global order; HasStoredOrder preserves that distinction.
        /// </summary>
        internal sealed class PawnWorkGiverOrderSnapshot
        {
            internal PawnWorkGiverOrderSnapshot(
                int pawnId,
                string workTypeDefName,
                bool hasStoredOrder,
                IEnumerable<string> orderedWorkGiverNames)
            {
                PawnId = pawnId;
                WorkTypeDefName = workTypeDefName ?? string.Empty;
                HasStoredOrder = hasStoredOrder;
                OrderedWorkGiverNames = new List<string>(
                    orderedWorkGiverNames ?? new string[0]);
            }

            internal int PawnId { get; private set; }
            internal string WorkTypeDefName { get; private set; }
            internal bool HasStoredOrder { get; private set; }
            internal IReadOnlyList<string> OrderedWorkGiverNames { get; private set; }
        }

        private sealed class SpecificPriorityBatchEntry
        {
            internal SpecificPriorityBatchEntry(
                bool isGlobal,
                int pawnId,
                string workGiverDefName,
                ExactGlobalStateKind desiredState,
                int desiredPriority,
                GlobalWorkGiverPrioritySnapshot expectedGlobal,
                bool expectedHasLocalOverride,
                int expectedLocalPriority)
            {
                IsGlobal = isGlobal;
                PawnId = pawnId;
                WorkGiverDefName = workGiverDefName ?? string.Empty;
                DesiredState = desiredState;
                DesiredPriority = desiredPriority;
                ExpectedGlobal = expectedGlobal;
                ExpectedHasLocalOverride = expectedHasLocalOverride;
                ExpectedLocalPriority = expectedLocalPriority;
            }

            internal bool IsGlobal { get; private set; }
            internal int PawnId { get; private set; }
            internal string WorkGiverDefName { get; private set; }
            internal ExactGlobalStateKind DesiredState { get; private set; }
            internal int DesiredPriority { get; private set; }
            internal GlobalWorkGiverPrioritySnapshot ExpectedGlobal { get; private set; }
            internal bool ExpectedHasLocalOverride { get; private set; }
            internal int ExpectedLocalPriority { get; private set; }
            internal string CanonicalKey =>
                (IsGlobal ? "global" : "local:" + PawnId.ToString()) + ":priority:" + WorkGiverDefName;
        }

        private sealed class SpecificOrderBatchEntry
        {
            internal SpecificOrderBatchEntry(
                bool isGlobal,
                int pawnId,
                string workTypeDefName,
                ExactGlobalStateKind desiredState,
                IReadOnlyList<string> desiredOrder,
                GlobalWorkTypeOrderSnapshot expectedGlobal,
                PawnWorkGiverOrderSnapshot expectedLocal)
            {
                IsGlobal = isGlobal;
                PawnId = pawnId;
                WorkTypeDefName = workTypeDefName ?? string.Empty;
                DesiredState = desiredState;
                DesiredOrder = desiredOrder == null
                    ? null
                    : new List<string>(desiredOrder);
                ExpectedGlobal = expectedGlobal;
                ExpectedLocal = expectedLocal;
            }

            internal bool IsGlobal { get; private set; }
            internal int PawnId { get; private set; }
            internal string WorkTypeDefName { get; private set; }
            internal ExactGlobalStateKind DesiredState { get; private set; }
            internal IReadOnlyList<string> DesiredOrder { get; private set; }
            internal GlobalWorkTypeOrderSnapshot ExpectedGlobal { get; private set; }
            internal PawnWorkGiverOrderSnapshot ExpectedLocal { get; private set; }
            internal string CanonicalKey =>
                (IsGlobal ? "global" : "local:" + PawnId.ToString()) + ":order:" + WorkTypeDefName;
        }

        private sealed class SpecificJobBatchRollback : IWorkTabSpecificJobRollbackReceipt
        {
            internal SpecificJobBatchRollback(
                WorkGiverReassignmentData previousData,
                string previousFingerprint,
                int previousRevision,
                int appliedRevision,
                string appliedFingerprint,
                long authorityRevision,
                bool synchronizedReplay,
                WorkTabMutationLease authorization)
            {
                PreviousData = previousData;
                PreviousFingerprint = previousFingerprint ?? string.Empty;
                PreviousRevision = previousRevision;
                AppliedRevision = appliedRevision;
                AppliedFingerprint = appliedFingerprint ?? string.Empty;
                AuthorityRevision = authorityRevision;
                SynchronizedReplay = synchronizedReplay;
                Authorization = authorization;
            }

            internal WorkGiverReassignmentData PreviousData { get; private set; }
            internal string PreviousFingerprint { get; private set; }
            internal int PreviousRevision { get; private set; }
            internal int AppliedRevision { get; private set; }
            internal string AppliedFingerprint { get; private set; }
            internal long AuthorityRevision { get; private set; }
            internal bool SynchronizedReplay { get; private set; }
            internal WorkTabMutationLease Authorization { get; private set; }
            int IWorkTabSpecificJobRollbackReceipt.AppliedRevision => AppliedRevision;
        }

        /// <summary>
        /// Exact state carried by the global specific-job seams.  Absent means
        /// no stored global opinion, Set means a stored value exists, and Clear
        /// means the stored global value is explicitly suppressed.
        /// </summary>
        internal enum ExactGlobalStateKind
        {
            Absent = 0,
            Set = 1,
            Clear = 2
        }

        /// <summary>
        /// Immutable exact snapshot for one global WorkGiver priority.  The
        /// sync and authority revisions are part of the snapshot so a later
        /// apply or rollback can fail closed if anything changed meanwhile.
        /// </summary>
        internal sealed class GlobalWorkGiverPrioritySnapshot
        {
            internal GlobalWorkGiverPrioritySnapshot(
                string workGiverDefName,
                ExactGlobalStateKind state,
                int priority,
                int syncVersion,
                long authorityRevision)
            {
                WorkGiverDefName = workGiverDefName ?? string.Empty;
                State = state;
                Priority = priority;
                SyncVersion = syncVersion;
                AuthorityRevision = authorityRevision;
            }

            internal string WorkGiverDefName { get; private set; }
            internal ExactGlobalStateKind State { get; private set; }
            internal int Priority { get; private set; }
            internal int SyncVersion { get; private set; }
            internal long AuthorityRevision { get; private set; }
            internal bool HasStoredValue => State == ExactGlobalStateKind.Set;
            internal bool IsExplicitlyCleared => State == ExactGlobalStateKind.Clear;
        }

        /// <summary>
        /// Immutable exact snapshot for one global whole-WorkType order.
        /// OrderedWorkGiverNames is a complete permutation when State is Set;
        /// it is empty for Absent and Clear.
        /// </summary>
        internal sealed class GlobalWorkTypeOrderSnapshot
        {
            internal GlobalWorkTypeOrderSnapshot(
                string workTypeDefName,
                ExactGlobalStateKind state,
                IEnumerable<string> orderedWorkGiverNames,
                int syncVersion,
                long authorityRevision)
            {
                WorkTypeDefName = workTypeDefName ?? string.Empty;
                State = state;
                OrderedWorkGiverNames = new List<string>(
                    orderedWorkGiverNames ?? new string[0]);
                SyncVersion = syncVersion;
                AuthorityRevision = authorityRevision;
            }

            internal string WorkTypeDefName { get; private set; }
            internal ExactGlobalStateKind State { get; private set; }
            internal IReadOnlyList<string> OrderedWorkGiverNames { get; private set; }
            internal int SyncVersion { get; private set; }
            internal long AuthorityRevision { get; private set; }
            internal bool HasStoredValue => State == ExactGlobalStateKind.Set;
            internal bool IsExplicitlyCleared => State == ExactGlobalStateKind.Clear;
        }

        private static WorkGiverReassignmentData ExistingData
        {
            get
            {
                IWorkTabReassignmentState state =
                    WorkTabGameRoots.For(Current.Game)?.State.Reassignments;
                return state?.Data ?? Settings?.LegacyWorkGiverReassignments;
            }
        }

        private static WorkGiverReassignmentData Data
        {
            get
            {
                IWorkTabReassignmentState state =
                    WorkTabGameRoots.For(Current.Game)?.State.Reassignments;
                if (state != null)
                {
                    return state.EnsureData();
                }

                if (Settings == null)
                {
                    return null;
                }

                return Settings.LegacyWorkGiverReassignments;
            }
        }

        internal static int CurrentSyncVersion => ExistingData?.SyncVersion ?? 0;

        internal static string CurrentStateFingerprint =>
            Data?.ComputeStateFingerprint() ?? DeterministicCanonical.Fingerprint(string.Empty);

        internal static bool CanSynchronouslyAcknowledgeExactGlobalMutation =>
            !MultiplayerBridge.Active;

        internal static bool HasActiveData
        {
            get
            {
                WorkGiverReassignmentData data = ExistingData;
                IWorkTabReassignmentState state =
                    WorkTabGameRoots.For(Current.Game)?.State.Reassignments;
                int version = data?.SyncVersion ?? 0;
                if (!ReferenceEquals(state, _cachedActivationState) ||
                    version != _cachedActivationSyncVersion)
                {
                    _cachedActivationState = state;
                    _cachedActivationSyncVersion = version;
                    _cachedHasAnyData = data != null && data.HasAnyData();
                }

                return _cachedHasAnyData;
            }
        }

        /// <summary>
        /// Clear caches when the sync version changes or the settings are reloaded.
        /// </summary>
        internal static void InvalidateCaches()
        {
            WorkGiverTargetCache.Clear();
            WorkGiverTopologyCache.Clear();
            OrderedWorkGiverCache.Clear();
            DisplayWorkGiverCache.Clear();
            _workGiverTopologyBuilt = false;
            _cachedActivationSyncVersion = int.MinValue;
            _cachedActivationState = null;
        }

        internal static IDisposable BeginMutationBatch()
        {
            _mutationBatchDepth++;
            return new MutationBatchScope();
        }

        internal static bool CommitMutationBatch(bool notifyDependents = false)
        {
            if (_mutationBatchDepth != 0 || !_mutationBatchChanged)
            {
                return false;
            }

            _mutationBatchChanged = false;
            InvalidateCaches();
            if (notifyDependents)
            {
                NotifySubWorkDependentsChanged();
            }
            return true;
        }

        /// <summary>
        /// Applies all workload-owned specific-job priorities and whole-type
        /// orders as one canonical mutation.  Every target is validated before
        /// the first backing-store write.  The data clone is retained as one
        /// rollback unit so a later verification failure cannot leave a
        /// partially applied local/global batch.
        /// </summary>
        internal static bool TryApplySpecificJobBatch(
            IReadOnlyList<WorkTabStagedSpecificPriority> priorityEntries,
            IReadOnlyList<WorkTabStagedSpecificOrder> orderEntries,
            int expectedSyncVersion,
            long expectedAuthorityRevision,
            bool synchronizedReplay,
            WorkTabMutationLease authorization,
            out IWorkTabSpecificJobRollbackReceipt rollback,
            out string reason)
        {
            rollback = null;
            reason = string.Empty;
            var priorities = (priorityEntries ?? new WorkTabStagedSpecificPriority[0])
                .Where(entry => entry != null)
                .OrderBy(entry => entry.CanonicalKey, StringComparer.Ordinal)
                .Select(entry => ToManagerEntry(
                    entry,
                    expectedSyncVersion,
                    expectedAuthorityRevision))
                .ToList();
            var orders = (orderEntries ?? new WorkTabStagedSpecificOrder[0])
                .Where(entry => entry != null)
                .OrderBy(entry => entry.CanonicalKey, StringComparer.Ordinal)
                .Select(entry => ToManagerEntry(
                    entry,
                    expectedSyncVersion,
                    expectedAuthorityRevision))
                .ToList();
            if (priorities.Count == 0 && orders.Count == 0)
            {
                return true;
            }

            if (!CanUseSpecificBatchCapability(
                    expectedAuthorityRevision,
                    expectedSyncVersion,
                    synchronizedReplay,
                    authorization))
            {
                reason = "The specific-job batch lacks the transaction-bound workload mutation capability.";
                return false;
            }

            WorkGiverReassignmentData data = Data;
            if (data == null || CurrentSyncVersion != expectedSyncVersion)
            {
                reason = "The BWT specific-job revision changed before the atomic batch began.";
                return false;
            }

            if (!TryValidateDistinctBatchKeys(priorities, orders, out reason) ||
                !TryValidateSpecificPriorityBatch(
                    priorities,
                    expectedSyncVersion,
                    expectedAuthorityRevision,
                    out reason) ||
                !TryValidateSpecificOrderBatch(
                    orders,
                    expectedSyncVersion,
                    expectedAuthorityRevision,
                    out reason))
            {
                return false;
            }

            WorkGiverReassignmentData previousData = data.Clone();
            string previousFingerprint = previousData.ComputeStateFingerprint();
            int previousRevision = expectedSyncVersion;
            bool changed = false;
            try
            {
                for (int i = 0; i < priorities.Count; i++)
                {
                    SpecificPriorityBatchEntry entry = priorities[i];
                    if (entry.IsGlobal)
                    {
                        changed |= ApplyGlobalWorkGiverPriorityState(
                            data,
                            entry.WorkGiverDefName,
                            entry.DesiredState,
                            entry.DesiredPriority,
                            advanceRevision: false);
                    }
                    else
                    {
                        changed |= ApplyLocalWorkGiverPriorityState(
                            data,
                            entry.PawnId,
                            entry.WorkGiverDefName,
                            entry.DesiredState == ExactGlobalStateKind.Set
                                ? entry.DesiredPriority
                                : (int?)null);
                    }
                }

                for (int i = 0; i < orders.Count; i++)
                {
                    SpecificOrderBatchEntry entry = orders[i];
                    if (entry.IsGlobal)
                    {
                        changed |= ApplyGlobalWorkTypeOrderState(
                            data,
                            entry.WorkTypeDefName,
                            entry.DesiredState,
                            entry.DesiredOrder,
                            advanceRevision: false);
                    }
                    else
                    {
                        changed |= ApplyLocalWorkTypeOrderState(
                            data,
                            entry.PawnId,
                            entry.WorkTypeDefName,
                            entry.DesiredState == ExactGlobalStateKind.Set
                                ? entry.DesiredOrder
                                : null);
                    }
                }

                if (!changed)
                {
                    return true;
                }

                data.SyncVersion = unchecked(previousRevision + 1);
                if (!VerifySpecificPriorityBatch(priorities) ||
                    !VerifySpecificOrderBatch(orders))
                {
                    throw new InvalidOperationException(
                        "The canonical specific-job batch failed post-write verification.");
                }

                InvalidateCaches();
                MarkMutationChanged(notifyDependents: true);
                rollback = new SpecificJobBatchRollback(
                    previousData,
                    previousFingerprint,
                    previousRevision,
                    data.SyncVersion,
                    data.ComputeStateFingerprint(),
                    expectedAuthorityRevision,
                    synchronizedReplay,
                    authorization);
                return true;
            }
            catch (Exception exception)
            {
                data.CopyFrom(previousData, previousRevision);
                InvalidateCaches();
                reason = "The atomic specific-job batch was rejected without a partial write: " +
                    exception.Message;
                return false;
            }
        }

        internal static bool TryRestoreSpecificJobBatch(
            IWorkTabSpecificJobRollbackReceipt receipt,
            out string reason)
        {
            reason = string.Empty;
            if (receipt == null)
            {
                return true;
            }

            SpecificJobBatchRollback rollback = receipt as SpecificJobBatchRollback;
            if (rollback == null)
            {
                reason = "The specific-job rollback receipt is not owned by this domain.";
                return false;
            }

            if (!CanUseSpecificRollbackCapability(rollback))
            {
                reason = "The specific-job rollback capability is stale or unavailable.";
                return false;
            }

            WorkGiverReassignmentData data = Data;
            if (data == null || CurrentSyncVersion != rollback.AppliedRevision)
            {
                reason = "The BWT specific-job state changed before rollback and was not overwritten.";
                return false;
            }

            if (!StringComparer.Ordinal.Equals(
                    data.ComputeStateFingerprint(),
                    rollback.AppliedFingerprint))
            {
                reason = "The BWT specific-job state changed before rollback and was not overwritten.";
                return false;
            }

            try
            {
                data.CopyFrom(rollback.PreviousData, unchecked(rollback.AppliedRevision + 1));
                if (!StringComparer.Ordinal.Equals(
                        data.ComputeStateFingerprint(),
                        rollback.PreviousFingerprint))
                {
                    reason = "The BWT specific-job rollback did not restore the exact prior state.";
                    return false;
                }

                InvalidateCaches();
                MarkMutationChanged(notifyDependents: true);
                return true;
            }
            catch (Exception exception)
            {
                reason = "The BWT specific-job rollback failed: " + exception.Message;
                return false;
            }
        }

        internal static bool RestoreUnpublishedSpecificJobBatch(
            IWorkTabSpecificJobRollbackReceipt receipt,
            out string reason)
        {
            reason = string.Empty;
            SpecificJobBatchRollback rollback = receipt as SpecificJobBatchRollback;
            WorkGiverReassignmentData data = Data;
            if (_mutationBatchDepth <= 0 || rollback == null || data == null ||
                CurrentSyncVersion != rollback.AppliedRevision ||
                !StringComparer.Ordinal.Equals(
                    data.ComputeStateFingerprint(),
                    rollback.AppliedFingerprint))
            {
                reason = "The unpublished specific-job state no longer matches its transaction receipt.";
                return false;
            }

            data.CopyFrom(rollback.PreviousData, rollback.PreviousRevision);
            if (!StringComparer.Ordinal.Equals(
                    data.ComputeStateFingerprint(),
                    rollback.PreviousFingerprint))
            {
                reason = "The unpublished specific-job rollback did not restore its exact baseline.";
                return false;
            }

            InvalidateCaches();
            return true;
        }

        private static bool CanUseSpecificBatchCapability(
            long expectedAuthorityRevision,
            int expectedSyncVersion,
            bool synchronizedReplay,
            WorkTabMutationLease authorization)
        {
            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(expectedAuthorityRevision) &&
                   (!MultiplayerBridge.Active
                         ? authorization == null || authorization.IsUsable
                         : synchronizedReplay || authorization != null &&
                           authorization.IsAcceptedForSpecificBatch(
                             synchronizedExecution: true,
                             expectedAuthorityRevision,
                             expectedSyncVersion));
        }

        private static SpecificPriorityBatchEntry ToManagerEntry(
            WorkTabStagedSpecificPriority entry,
            int expectedSyncVersion,
            long expectedAuthorityRevision)
        {
            WorkTabSpecificPriorityBaseline baseline = entry.Expected;
            return new SpecificPriorityBatchEntry(
                entry.IsGlobal,
                entry.PawnId,
                entry.WorkGiverDefName,
                ToManagerState(entry.DesiredState),
                entry.DesiredPriority,
                entry.IsGlobal
                    ? new GlobalWorkGiverPrioritySnapshot(
                        entry.WorkGiverDefName,
                        ToManagerState(baseline.State),
                        baseline.Priority,
                        expectedSyncVersion,
                        expectedAuthorityRevision)
                    : null,
                baseline.State == WorkTabSpecificPriorityState.LocalSet,
                baseline.Priority);
        }

        private static SpecificOrderBatchEntry ToManagerEntry(
            WorkTabStagedSpecificOrder entry,
            int expectedSyncVersion,
            long expectedAuthorityRevision)
        {
            WorkTabSpecificOrderBaseline baseline = entry.Expected;
            return new SpecificOrderBatchEntry(
                entry.IsGlobal,
                entry.PawnId,
                entry.WorkTypeDefName,
                ToManagerState(entry.DesiredState),
                entry.DesiredOrder,
                entry.IsGlobal
                    ? new GlobalWorkTypeOrderSnapshot(
                        entry.WorkTypeDefName,
                        ToManagerState(baseline.State),
                        baseline.OrderedWorkGiverNames,
                        expectedSyncVersion,
                        expectedAuthorityRevision)
                    : null,
                entry.IsGlobal
                    ? null
                    : new PawnWorkGiverOrderSnapshot(
                        entry.PawnId,
                        entry.WorkTypeDefName,
                        baseline.State == WorkTabSpecificOrderState.LocalStored,
                        baseline.OrderedWorkGiverNames));
        }

        private static ExactGlobalStateKind ToManagerState(
            WorkTabSpecificPriorityState state)
        {
            switch (state)
            {
                case WorkTabSpecificPriorityState.GlobalSet:
                case WorkTabSpecificPriorityState.LocalSet:
                    return ExactGlobalStateKind.Set;
                case WorkTabSpecificPriorityState.GlobalClear:
                case WorkTabSpecificPriorityState.LocalInherit:
                    return ExactGlobalStateKind.Clear;
                default:
                    return ExactGlobalStateKind.Absent;
            }
        }

        private static ExactGlobalStateKind ToManagerState(
            WorkTabSpecificOrderState state)
        {
            switch (state)
            {
                case WorkTabSpecificOrderState.GlobalSet:
                case WorkTabSpecificOrderState.LocalStored:
                    return ExactGlobalStateKind.Set;
                case WorkTabSpecificOrderState.GlobalClear:
                case WorkTabSpecificOrderState.LocalInherit:
                    return ExactGlobalStateKind.Clear;
                default:
                    return ExactGlobalStateKind.Absent;
            }
        }

        private static bool TryValidateDistinctBatchKeys(
            IReadOnlyList<SpecificPriorityBatchEntry> priorities,
            IReadOnlyList<SpecificOrderBatchEntry> orders,
            out string reason)
        {
            reason = string.Empty;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < priorities.Count; i++)
            {
                if (!keys.Add(priorities[i].CanonicalKey))
                {
                    reason = "The specific-job priority batch contains a duplicate target.";
                    return false;
                }
            }

            for (int i = 0; i < orders.Count; i++)
            {
                if (!keys.Add(orders[i].CanonicalKey))
                {
                    reason = "The specific-job order batch contains a duplicate target.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateSpecificPriorityBatch(
            IReadOnlyList<SpecificPriorityBatchEntry> entries,
            int expectedSyncVersion,
            long expectedAuthorityRevision,
            out string reason)
        {
            reason = string.Empty;
            for (int i = 0; i < entries.Count; i++)
            {
                SpecificPriorityBatchEntry entry = entries[i];
                if (entry.WorkGiverDefName.NullOrEmpty() ||
                    DefDatabase<WorkGiverDef>.GetNamedSilentFail(entry.WorkGiverDefName) == null ||
                    (entry.DesiredState == ExactGlobalStateKind.Set &&
                     WorkPrioritySystem.ClampPriority(entry.DesiredPriority) != entry.DesiredPriority))
                {
                    reason = "The specific-job priority batch contains an invalid WorkGiver or priority.";
                    return false;
                }

                if (entry.IsGlobal)
                {
                    if (entry.ExpectedGlobal == null ||
                        entry.ExpectedGlobal.SyncVersion != expectedSyncVersion ||
                        entry.ExpectedGlobal.AuthorityRevision != expectedAuthorityRevision ||
                        !IsSameGlobalPriorityState(
                            CaptureGlobalWorkGiverPrioritySnapshot(entry.WorkGiverDefName),
                            entry.ExpectedGlobal))
                    {
                        reason = "A global specific-job priority baseline changed before the batch.";
                        return false;
                    }
                }
                else
                {
                    if (entry.PawnId < 0 ||
                        !TryGetPawnOverrideById(
                            entry.PawnId,
                            entry.WorkGiverDefName,
                            out bool hasOverride,
                            out int priority) ||
                        hasOverride != entry.ExpectedHasLocalOverride ||
                        (hasOverride && priority != entry.ExpectedLocalPriority))
                    {
                        reason = "A local specific-job priority baseline changed before the batch.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryValidateSpecificOrderBatch(
            IReadOnlyList<SpecificOrderBatchEntry> entries,
            int expectedSyncVersion,
            long expectedAuthorityRevision,
            out string reason)
        {
            reason = string.Empty;
            for (int i = 0; i < entries.Count; i++)
            {
                SpecificOrderBatchEntry entry = entries[i];
                WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(entry.WorkTypeDefName);
                if (workType == null ||
                    (entry.DesiredState == ExactGlobalStateKind.Set &&
                     !IsExactWorkGiverOrder(workType, entry.DesiredOrder)))
                {
                    reason = "The specific-job order batch contains an invalid WorkType permutation.";
                    return false;
                }

                if (entry.IsGlobal)
                {
                    if (entry.ExpectedGlobal == null ||
                        entry.ExpectedGlobal.SyncVersion != expectedSyncVersion ||
                        entry.ExpectedGlobal.AuthorityRevision != expectedAuthorityRevision ||
                        !IsSameGlobalOrderState(
                            CaptureGlobalWorkTypeOrderSnapshot(entry.WorkTypeDefName),
                            entry.ExpectedGlobal))
                    {
                        reason = "A global WorkGiver-order baseline changed before the batch.";
                        return false;
                    }
                }
                else if (entry.PawnId < 0 || entry.ExpectedLocal == null ||
                         !IsSamePawnOrderState(
                             CapturePawnWorkGiverOrderSnapshot(
                                 entry.PawnId,
                                 entry.WorkTypeDefName),
                             entry.ExpectedLocal))
                {
                    reason = "A local WorkGiver-order baseline changed before the batch.";
                    return false;
                }
            }

            return true;
        }

        private static bool VerifySpecificPriorityBatch(
            IReadOnlyList<SpecificPriorityBatchEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                SpecificPriorityBatchEntry entry = entries[i];
                if (entry.IsGlobal)
                {
                    GlobalWorkGiverPrioritySnapshot observed =
                        CaptureGlobalWorkGiverPrioritySnapshot(entry.WorkGiverDefName);
                    if (observed.State != entry.DesiredState ||
                        (entry.DesiredState == ExactGlobalStateKind.Set &&
                         observed.Priority != entry.DesiredPriority))
                    {
                        return false;
                    }
                }
                else
                {
                    TryGetPawnOverrideById(
                        entry.PawnId,
                        entry.WorkGiverDefName,
                        out bool hasOverride,
                        out int priority);
                    if ((entry.DesiredState == ExactGlobalStateKind.Set) != hasOverride ||
                        (hasOverride && priority != entry.DesiredPriority))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool VerifySpecificOrderBatch(
            IReadOnlyList<SpecificOrderBatchEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                SpecificOrderBatchEntry entry = entries[i];
                if (entry.IsGlobal)
                {
                    GlobalWorkTypeOrderSnapshot observed =
                        CaptureGlobalWorkTypeOrderSnapshot(entry.WorkTypeDefName);
                    if (observed.State != entry.DesiredState ||
                        (entry.DesiredState == ExactGlobalStateKind.Set &&
                         !observed.OrderedWorkGiverNames.SequenceEqual(entry.DesiredOrder ?? new string[0])))
                    {
                        return false;
                    }
                }
                else
                {
                    PawnWorkGiverOrderSnapshot observed = CapturePawnWorkGiverOrderSnapshot(
                        entry.PawnId,
                        entry.WorkTypeDefName);
                    if (entry.DesiredState == ExactGlobalStateKind.Set
                        ? !observed.HasStoredOrder ||
                          !observed.OrderedWorkGiverNames.SequenceEqual(entry.DesiredOrder ?? new string[0])
                        : observed.HasStoredOrder)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryGetPawnOverrideById(
            int pawnId,
            string workGiverDefName,
            out bool hasOverride,
            out int priority)
        {
            hasOverride = false;
            priority = WorkPrioritySystem.DisabledPriority;
            WorkGiverReassignmentData data = ExistingData;
            if (data?.PawnWorkGiverPriorityOverrides == null ||
                !data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var priorities) ||
                priorities == null)
            {
                return true;
            }

            hasOverride = priorities.TryGetValue(workGiverDefName ?? string.Empty, out priority);
            return true;
        }

        private static bool IsSamePawnOrderState(
            PawnWorkGiverOrderSnapshot left,
            PawnWorkGiverOrderSnapshot right)
        {
            return left != null && right != null &&
                left.PawnId == right.PawnId &&
                StringComparer.Ordinal.Equals(left.WorkTypeDefName, right.WorkTypeDefName) &&
                left.HasStoredOrder == right.HasStoredOrder &&
                (!left.HasStoredOrder || left.OrderedWorkGiverNames.SequenceEqual(right.OrderedWorkGiverNames));
        }

        private static bool ApplyLocalWorkGiverPriorityState(
            WorkGiverReassignmentData data,
            int pawnId,
            string workGiverDefName,
            int? desiredPriority)
        {
            data.EnsureCollections();
            data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var priorities);
            bool changed = false;
            if (desiredPriority.HasValue)
            {
                if (priorities == null)
                {
                    priorities = new Dictionary<string, int>(StringComparer.Ordinal);
                    data.PawnWorkGiverPriorityOverrides[pawnId] = priorities;
                }

                if (!priorities.TryGetValue(workGiverDefName, out int current) ||
                    current != desiredPriority.Value)
                {
                    priorities[workGiverDefName] = desiredPriority.Value;
                    changed = true;
                }
            }
            else if (priorities != null && priorities.Remove(workGiverDefName))
            {
                changed = true;
                if (priorities.Count == 0)
                {
                    data.PawnWorkGiverPriorityOverrides.Remove(pawnId);
                }
            }

            return changed;
        }

        private static bool ApplyLocalWorkTypeOrderState(
            WorkGiverReassignmentData data,
            int pawnId,
            string workTypeDefName,
            IReadOnlyList<string> desiredOrder)
        {
            data.EnsureCollections();
            data.PawnWorkGiverOrdering.TryGetValue(pawnId, out var orders);
            if (desiredOrder != null)
            {
                if (orders == null)
                {
                    orders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    data.PawnWorkGiverOrdering[pawnId] = orders;
                }

                if (orders.TryGetValue(workTypeDefName, out var current) &&
                    current != null && current.SequenceEqual(desiredOrder))
                {
                    return false;
                }

                orders[workTypeDefName] = new List<string>(desiredOrder);
                return true;
            }

            if (orders == null || !orders.Remove(workTypeDefName))
            {
                return false;
            }

            if (orders.Count == 0)
            {
                data.PawnWorkGiverOrdering.Remove(pawnId);
            }

            return true;
        }

        internal static void OnSettingsLoaded()
        {
            InvalidateCaches();
            WorkGiverLayoutHistory.Clear();
            _cachedSyncVersion = ExistingData?.SyncVersion ?? 0;
        }

        internal static void OnWorldDataLoaded()
        {
            InvalidateCaches();
            _cachedSyncVersion = ExistingData?.SyncVersion ?? 0;
            CleanupOrphanedReassignments();
        }

        private static void EnsureVersion()
        {
            int version = ExistingData?.SyncVersion ?? 0;
            if (version == _cachedSyncVersion)
            {
                return;
            }

            InvalidateCaches();
            _cachedSyncVersion = version;
        }

        internal static WorkTypeDef GetTargetWorkType(WorkGiverDef def)
        {
            if (def == null)
            {
                return null;
            }

            EnsureVersion();

            if (WorkGiverTargetCache.TryGetValue(def.shortHash, out var cached))
            {
                return cached;
            }

            WorkTypeDef target = def.workType;
            var data = ExistingData;

            if (data?.WorkGiverToWorkTypeMap != null &&
                data.WorkGiverToWorkTypeMap.TryGetValue(def.defName, out var targetWorkTypeName) &&
                !string.IsNullOrEmpty(targetWorkTypeName))
            {
                var mapped = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeName);
                if (mapped != null)
                {
                    target = mapped;
                }
            }

            WorkGiverTargetCache[def.shortHash] = target;
            return target;
        }

        internal static bool IsReassigned(WorkGiverDef def)
        {
            if (def == null)
            {
                return false;
            }

            EnsureVersion();

            var target = GetTargetWorkType(def);
            return target != null && target != def.workType;
        }

        internal static IReadOnlyList<WorkGiver> GetOrderedWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn = null)
        {
            return GetWorkGiversForWorkType(workType, pawn, applyPrioritySort: true);
        }

        internal static IReadOnlyList<WorkGiver> GetDisplayWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn = null)
        {
            return GetWorkGiversForWorkType(workType, pawn, applyPrioritySort: false);
        }

        private static IReadOnlyList<WorkGiver> GetWorkGiversForWorkType(WorkTypeDef workType, Pawn pawn, bool applyPrioritySort)
        {
            EnsureVersion();

            if (workType == null)
            {
                return Array.Empty<WorkGiver>();
            }

            // Display order differs per pawn only when that pawn actually has
            // an order stored for this work type. Exact-state capture asks for
            // display lists across the whole roster, so retaining a non-null
            // pawn here otherwise defeats the shared cache thousands of times.
            if (!applyPrioritySort && pawn != null && !HasPawnOrdering(pawn, workType))
            {
                pawn = null;
            }

            if (applyPrioritySort && pawn == null && OrderedWorkGiverCache.TryGetValue(workType.defName, out var cached))
            {
                return cached;
            }

            if (!applyPrioritySort && pawn == null && DisplayWorkGiverCache.TryGetValue(workType.defName, out cached))
            {
                return cached;
            }

            IReadOnlyList<WorkGiverDef> topology = GetWorkGiverTopology(workType);
            var result = new List<WorkGiver>(topology.Count);
            var data = ExistingData;

            List<string> orderedNames = null;
            if (pawn != null && data?.PawnWorkGiverOrdering != null)
            {
                if (data.PawnWorkGiverOrdering.TryGetValue(pawn.thingIDNumber, out var pawnOrders) && pawnOrders != null)
                {
                    pawnOrders.TryGetValue(workType.defName, out orderedNames);
                }
            }

            bool globalOrderCleared = data?.GlobalWorkTypeOrderClears != null &&
                data.GlobalWorkTypeOrderClears.Contains(workType.defName);
            if (orderedNames == null && !globalOrderCleared && data?.WorkTypeWorkGiverOrder != null)
            {
                data.WorkTypeWorkGiverOrder.TryGetValue(workType.defName, out orderedNames);
            }

            HashSet<string> handled = null;
            if (orderedNames != null)
            {
                handled = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < orderedNames.Count; i++)
                {
                    var name = orderedNames[i];
                    if (handled.Contains(name))
                    {
                        continue;
                    }

                    var def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(name);
                    if (def != null && GetTargetWorkType(def) == workType)
                    {
                        var worker = def.Worker;
                        if (worker != null)
                        {
                            result.Add(worker);
                            handled.Add(name);
                        }
                    }
                }
            }

            for (int i = 0; i < topology.Count; i++)
            {
                WorkGiverDef def = topology[i];
                if (handled == null || !handled.Contains(def.defName))
                {
                    WorkGiver worker = def.Worker;
                    if (worker != null)
                    {
                        result.Add(worker);
                    }
                }
            }

            if (applyPrioritySort)
            {
                // Apply sorting based on priorities (pawn-specific or global defaults)
                var sortingPawn = pawn;
                int defaultPrio = pawn == null
                    ? WorkPrioritySystem.GetDefaultEnabledPriority()
                    : ParentPriorityRead.GetLive(pawn, workType);

                var indexed = new List<WorkGiverPriorityRecord>(result.Count);
                for (int i = 0; i < result.Count; i++)
                {
                    WorkGiver workGiver = result[i];
                    int priority = GetWorkGiverPriority(sortingPawn, workGiver.def, defaultPrio);
                    if (sortingPawn != null)
                    {
                        priority = TimePriorityService.GetEffectiveWorkGiverPriority(
                            sortingPawn,
                            workType,
                            workGiver.def,
                            priority);
                    }

                    indexed.Add(new WorkGiverPriorityRecord(
                        workGiver,
                        i,
                        priority == WorkPrioritySystem.DisabledPriority ? 999 : priority));
                }

                indexed.Sort((a, b) =>
                {
                    int c = a.Priority.CompareTo(b.Priority);
                    if (c != 0) return c;

                    // Secondary sort: saved/manual order.
                    c = a.OriginalIndex.CompareTo(b.OriginalIndex);
                    if (c != 0) return c;

                    return b.WorkGiver.def.priorityInType.CompareTo(a.WorkGiver.def.priorityInType);
                });
                result.Clear();
                for (int i = 0; i < indexed.Count; i++)
                {
                    result.Add(indexed[i].WorkGiver);
                }
            }

            if (pawn == null)
            {
                if (applyPrioritySort)
                {
                    OrderedWorkGiverCache[workType.defName] = result;
                }
                else
                {
                    DisplayWorkGiverCache[workType.defName] = result;
                }
            }

            return result;
        }

        private static IReadOnlyList<WorkGiverDef> GetWorkGiverTopology(WorkTypeDef workType)
        {
            EnsureWorkGiverTopology();
            if (workType?.defName != null &&
                WorkGiverTopologyCache.TryGetValue(workType.defName, out List<WorkGiverDef> topology))
            {
                return topology;
            }

            return Array.Empty<WorkGiverDef>();
        }

        private static void EnsureWorkGiverTopology()
        {
            if (_workGiverTopologyBuilt)
            {
                return;
            }

            List<WorkGiverDef> allWorkGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < allWorkGivers.Count; i++)
            {
                WorkGiverDef def = allWorkGivers[i];
                WorkTypeDef target = GetTargetWorkType(def);
                if (target?.defName == null)
                {
                    continue;
                }

                if (!WorkGiverTopologyCache.TryGetValue(target.defName, out List<WorkGiverDef> topology))
                {
                    topology = new List<WorkGiverDef>();
                    WorkGiverTopologyCache.Add(target.defName, topology);
                }

                topology.Add(def);
            }

            foreach (List<WorkGiverDef> topology in WorkGiverTopologyCache.Values)
            {
                topology.Sort((a, b) => b.priorityInType.CompareTo(a.priorityInType));
            }

            _workGiverTopologyBuilt = true;
        }

        private readonly struct WorkGiverPriorityRecord
        {
            internal WorkGiverPriorityRecord(WorkGiver workGiver, int originalIndex, int priority)
            {
                WorkGiver = workGiver;
                OriginalIndex = originalIndex;
                Priority = priority;
            }

            internal WorkGiver WorkGiver { get; }
            internal int OriginalIndex { get; }
            internal int Priority { get; }
        }

        internal static List<Pawn> GetPawnsWithOverrides(WorkTypeDef workType)
        {
            var data = ExistingData;
            if (data == null || workType == null)
            {
                return new List<Pawn>();
            }

            var results = new List<Pawn>();
            var pawnIds = new HashSet<int>();

            // Check priority overrides
            if (data.PawnWorkGiverPriorityOverrides != null)
            {
                var workGiversInType = new HashSet<string>(GetOrderedWorkGiversForWorkType(workType).Select(wg => wg.def.defName));
                foreach (var kv in data.PawnWorkGiverPriorityOverrides)
                {
                    if (kv.Key == -1) continue;
                    if (kv.Value != null && kv.Value.Keys.Any(name => workGiversInType.Contains(name)))
                    {
                        pawnIds.Add(kv.Key);
                    }
                }
            }

            // Check ordering overrides
            if (data.PawnWorkGiverOrdering != null)
            {
                foreach (var kv in data.PawnWorkGiverOrdering)
                {
                    if (kv.Key == -1) continue;
                    if (kv.Value != null && kv.Value.ContainsKey(workType.defName))
                    {
                        pawnIds.Add(kv.Key);
                    }
                }
            }

            foreach (int id in pawnIds)
            {
                var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == id);
                if (pawn != null)
                {
                    results.Add(pawn);
                }
            }

            return results;
        }

        internal static bool HasAnyPawnOverride(WorkTypeDef workType, Pawn pawn)
        {
            var data = ExistingData;
            if (data?.PawnWorkGiverPriorityOverrides == null || workType == null || pawn == null)
            {
                return false;
            }

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawn.thingIDNumber, out var pawnDict) || pawnDict == null)
            {
                return false;
            }

            var wgs = GetOrderedWorkGiversForWorkType(workType);
            foreach (var g in wgs)
            {
                if (pawnDict.ContainsKey(g.def.defName)) return true;
            }

            return false;
        }

        internal static bool HasPawnOrdering(Pawn pawn, WorkTypeDef workType)
        {
            var data = ExistingData;
            if (data?.PawnWorkGiverOrdering == null || pawn == null || workType == null)
            {
                return false;
            }

            return data.PawnWorkGiverOrdering.TryGetValue(pawn.thingIDNumber, out var orders) &&
                   orders != null &&
                   orders.ContainsKey(workType.defName);
        }

        /// <summary>
        /// Reads the exact BWT-owned pawn order without falling back to the
        /// global order. This is a runtime transaction seam, not a new
        /// persistence shape.
        /// </summary>
        internal static PawnWorkGiverOrderSnapshot CapturePawnWorkGiverOrderSnapshot(
            int pawnId,
            string workTypeDefName)
        {
            WorkGiverReassignmentData data = ExistingData;
            bool hasStoredOrder = false;
            List<string> orderedNames = null;
            if (data?.PawnWorkGiverOrdering != null &&
                data.PawnWorkGiverOrdering.TryGetValue(pawnId, out var pawnOrders) &&
                pawnOrders != null &&
                pawnOrders.TryGetValue(workTypeDefName ?? string.Empty, out orderedNames))
            {
                hasStoredOrder = true;
            }

            return new PawnWorkGiverOrderSnapshot(
                pawnId,
                workTypeDefName,
                hasStoredOrder,
                orderedNames);
        }

        internal static PawnWorkGiverOrderSnapshot CapturePawnWorkGiverOrderSnapshot(
            Pawn pawn,
            WorkTypeDef workType)
        {
            return CapturePawnWorkGiverOrderSnapshot(
                pawn?.thingIDNumber ?? -1,
                workType?.defName);
        }

        private static bool IsExactWorkGiverOrder(
            WorkTypeDef workType,
            IReadOnlyList<string> orderedWorkGiverNames)
        {
            var expected = GetDisplayWorkGiversForWorkType(workType)
                .Where(workGiver => workGiver?.def != null)
                .Select(workGiver => workGiver.def.defName)
                .ToList();
            if (expected.Count != orderedWorkGiverNames.Count)
            {
                return false;
            }

            var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < orderedWorkGiverNames.Count; i++)
            {
                string name = orderedWorkGiverNames[i];
                if (name.NullOrEmpty() || !expectedSet.Contains(name) || !seen.Add(name))
                {
                    return false;
                }
            }

            return seen.Count == expectedSet.Count;
        }

        /// <summary>
        /// Validates a complete global WorkType permutation without changing
        /// the stored order, reassignment map, or moved-work-giver taxonomy.
        /// </summary>
        internal static bool IsCompleteGlobalWorkTypeOrder(
            string workTypeDefName,
            IReadOnlyList<string> orderedWorkGiverNames)
        {
            if (workTypeDefName.NullOrEmpty() || orderedWorkGiverNames == null)
            {
                return false;
            }

            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            return workType != null && IsExactWorkGiverOrder(workType, orderedWorkGiverNames);
        }

        /// <summary>
        /// Captures the exact global WorkGiver priority state.  The legacy
        /// global storage remains the -1 priority bucket, while explicit
        /// clears are carried by the dedicated clear set in the data owner.
        /// </summary>
        internal static GlobalWorkGiverPrioritySnapshot CaptureGlobalWorkGiverPrioritySnapshot(
            string workGiverDefName)
        {
            string normalizedName = workGiverDefName ?? string.Empty;
            WorkGiverReassignmentData data = ExistingData;
            ExactGlobalStateKind state = ExactGlobalStateKind.Absent;
            int priority = WorkPrioritySystem.DisabledPriority;

            if (data?.GlobalWorkGiverPriorityClears != null &&
                data.GlobalWorkGiverPriorityClears.Contains(normalizedName))
            {
                state = ExactGlobalStateKind.Clear;
            }
            else if (data?.PawnWorkGiverPriorityOverrides != null &&
                     data.PawnWorkGiverPriorityOverrides.TryGetValue(-1, out var globalPriorities) &&
                     globalPriorities != null &&
                     globalPriorities.TryGetValue(normalizedName, out priority))
            {
                state = ExactGlobalStateKind.Set;
            }

            return new GlobalWorkGiverPrioritySnapshot(
                normalizedName,
                state,
                priority,
                CurrentSyncVersion,
                PriorityAuthorityBroker.GetObservationalAuthorityRevision());
        }

        /// <summary>
        /// Captures the exact global whole-WorkType order.  The resulting
        /// list is copied and never aliases persisted data.
        /// </summary>
        internal static GlobalWorkTypeOrderSnapshot CaptureGlobalWorkTypeOrderSnapshot(
            string workTypeDefName)
        {
            string normalizedName = workTypeDefName ?? string.Empty;
            WorkGiverReassignmentData data = ExistingData;
            ExactGlobalStateKind state = ExactGlobalStateKind.Absent;
            List<string> orderedNames = null;

            if (data?.GlobalWorkTypeOrderClears != null &&
                data.GlobalWorkTypeOrderClears.Contains(normalizedName))
            {
                state = ExactGlobalStateKind.Clear;
            }
            else if (data?.WorkTypeWorkGiverOrder != null &&
                     data.WorkTypeWorkGiverOrder.TryGetValue(normalizedName, out orderedNames))
            {
                state = ExactGlobalStateKind.Set;
            }

            return new GlobalWorkTypeOrderSnapshot(
                normalizedName,
                state,
                orderedNames,
                CurrentSyncVersion,
                PriorityAuthorityBroker.GetObservationalAuthorityRevision());
        }

        private static bool IsSameGlobalPriorityState(
            GlobalWorkGiverPrioritySnapshot left,
            GlobalWorkGiverPrioritySnapshot right)
        {
            return left != null && right != null &&
                string.Equals(left.WorkGiverDefName, right.WorkGiverDefName, StringComparison.Ordinal) &&
                left.State == right.State &&
                (left.State != ExactGlobalStateKind.Set || left.Priority == right.Priority) &&
                left.SyncVersion == right.SyncVersion &&
                left.AuthorityRevision == right.AuthorityRevision;
        }

        private static bool IsSameGlobalOrderState(
            GlobalWorkTypeOrderSnapshot left,
            GlobalWorkTypeOrderSnapshot right)
        {
            return left != null && right != null &&
                string.Equals(left.WorkTypeDefName, right.WorkTypeDefName, StringComparison.Ordinal) &&
                left.State == right.State &&
                (left.State != ExactGlobalStateKind.Set ||
                 left.OrderedWorkGiverNames.SequenceEqual(right.OrderedWorkGiverNames)) &&
                left.SyncVersion == right.SyncVersion &&
                left.AuthorityRevision == right.AuthorityRevision;
        }

        private static bool ApplyGlobalWorkGiverPriorityState(
            WorkGiverReassignmentData data,
            string workGiverDefName,
            ExactGlobalStateKind state,
            int priority,
            bool advanceRevision = true)
        {
            data.EnsureCollections();
            bool changed = false;
            data.PawnWorkGiverPriorityOverrides.TryGetValue(-1, out var globalPriorities);

            if (state == ExactGlobalStateKind.Set)
            {
                if (globalPriorities == null)
                {
                    globalPriorities = new Dictionary<string, int>(StringComparer.Ordinal);
                    data.PawnWorkGiverPriorityOverrides[-1] = globalPriorities;
                }

                if (!globalPriorities.TryGetValue(workGiverDefName, out int current) || current != priority)
                {
                    globalPriorities[workGiverDefName] = priority;
                    changed = true;
                }

                if (data.GlobalWorkGiverPriorityClears.Remove(workGiverDefName))
                {
                    changed = true;
                }
            }
            else
            {
                if (globalPriorities != null && globalPriorities.Remove(workGiverDefName))
                {
                    changed = true;
                    if (globalPriorities.Count == 0)
                    {
                        data.PawnWorkGiverPriorityOverrides.Remove(-1);
                    }
                }

                if (state == ExactGlobalStateKind.Clear)
                {
                    changed |= data.GlobalWorkGiverPriorityClears.Add(workGiverDefName);
                }
                else
                {
                    changed |= data.GlobalWorkGiverPriorityClears.Remove(workGiverDefName);
                }
            }

            if (changed && advanceRevision)
            {
                data.SyncVersion++;
            }

            return changed;
        }

        private static bool ApplyGlobalWorkTypeOrderState(
            WorkGiverReassignmentData data,
            string workTypeDefName,
            ExactGlobalStateKind state,
            IReadOnlyList<string> orderedWorkGiverNames,
            bool advanceRevision = true)
        {
            data.EnsureCollections();
            bool changed = false;

            if (state == ExactGlobalStateKind.Set)
            {
                var exact = new List<string>(orderedWorkGiverNames);
                if (!data.WorkTypeWorkGiverOrder.TryGetValue(workTypeDefName, out var current) ||
                    current == null || !current.SequenceEqual(exact))
                {
                    data.WorkTypeWorkGiverOrder[workTypeDefName] = exact;
                    changed = true;
                }

                if (data.GlobalWorkTypeOrderClears.Remove(workTypeDefName))
                {
                    changed = true;
                }
            }
            else
            {
                if (data.WorkTypeWorkGiverOrder.Remove(workTypeDefName))
                {
                    changed = true;
                }

                if (state == ExactGlobalStateKind.Clear)
                {
                    changed |= data.GlobalWorkTypeOrderClears.Add(workTypeDefName);
                }
                else
                {
                    changed |= data.GlobalWorkTypeOrderClears.Remove(workTypeDefName);
                }
            }

            if (changed && advanceRevision)
            {
                data.SyncVersion++;
            }

            return changed;
        }

        internal static bool ShouldShowMovedWorkGiverMarker(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            return (GetTargetWorkType(workGiverDef) == workType && IsReassigned(workGiverDef)) ||
                   WasWorkGiverDraggedByPlayer(workType, workGiverDef) &&
                   IsWorkGiverOutOfBaselinePosition(workType, workGiverDef);
        }

        internal static bool WasWorkGiverDraggedByPlayer(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            var data = ExistingData;
            if (data?.PlayerMovedWorkGiversByWorkType == null ||
                workType?.defName == null ||
                workGiverDef?.defName == null)
            {
                return false;
            }

            return data.PlayerMovedWorkGiversByWorkType.TryGetValue(workType.defName, out var moved) &&
                   moved != null &&
                   moved.Contains(workGiverDef.defName);
        }

        internal static bool IsWorkGiverOutOfBaselinePosition(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            if (workType == null || workGiverDef == null)
            {
                return false;
            }

            var current = GetDisplayWorkGiversForWorkType(workType)
                .Where(wg => wg?.def != null)
                .Select(wg => wg.def.defName)
                .ToList();

            int currentIndex = current.IndexOf(workGiverDef.defName);
            if (currentIndex < 0)
            {
                return false;
            }

            int baselineIndex = GetBaselineWorkGiverOrder(workType).IndexOf(workGiverDef.defName);
            return baselineIndex >= 0 && baselineIndex != currentIndex;
        }

        internal static int CalculateBaselineTargetIndex(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            if (workType == null || workGiverDef == null)
            {
                return -1;
            }

            var baseline = GetBaselineWorkGiverOrder(workType);
            int baselineIndex = baseline.IndexOf(workGiverDef.defName);
            if (baselineIndex < 0)
            {
                return -1;
            }

            var current = GetDisplayWorkGiversForWorkType(workType);
            int targetIndex = 0;
            for (int i = 0; i < current.Count; i++)
            {
                var defName = current[i]?.def?.defName;
                if (defName.NullOrEmpty() || defName == workGiverDef.defName)
                {
                    continue;
                }

                int otherBaselineIndex = baseline.IndexOf(defName);
                if (otherBaselineIndex >= 0 && otherBaselineIndex < baselineIndex)
                {
                    targetIndex++;
                }
            }

            return targetIndex;
        }

        internal static bool HasNonEmergencyWorkGiver(WorkTypeDef workType)
        {
            return GetOrderedWorkGiversForWorkType(workType).Any(w => w?.def != null && !w.def.emergency);
        }

        internal static bool TryGetPawnWorkGiverOverride(Pawn pawn, WorkGiverDef workGiver, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            var data = ExistingData;
            if (data?.PawnWorkGiverPriorityOverrides == null || pawn == null || workGiver?.defName == null)
            {
                return false;
            }

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawn.thingIDNumber, out var pawnDict) ||
                pawnDict == null ||
                !pawnDict.TryGetValue(workGiver.defName, out priority))
            {
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            return true;
        }

        /// <summary>
        /// Captures one pawn-specific override with the reassignment and
        /// authority revisions that protect an exact follow-up mutation.
        /// </summary>
        internal static bool TryCapturePawnWorkGiverPriority(
            Pawn pawn,
            WorkGiverDef workGiver,
            out int syncVersion,
            out bool hasOverride,
            out int priority,
            out long authorityRevision)
        {
            syncVersion = CurrentSyncVersion;
            hasOverride = false;
            priority = WorkPrioritySystem.DisabledPriority;
            authorityRevision = 0L;
            if (pawn == null || workGiver == null ||
                !WorkPrioritySystem.TryCaptureBwtMutationAuthority(out authorityRevision))
            {
                return false;
            }

            hasOverride = TryGetPawnWorkGiverOverride(pawn, workGiver, out priority);
            return syncVersion == CurrentSyncVersion &&
                   WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
        }

        internal static bool HasPawnWorkGiverOverride(Pawn pawn, WorkGiverDef workGiver)
        {
            return TryGetPawnWorkGiverOverride(pawn, workGiver, out _);
        }

        private static bool CanUseSpecificRollbackCapability(
            SpecificJobBatchRollback rollback)
        {
            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(rollback.AuthorityRevision) &&
                   (!MultiplayerBridge.Active
                        ? rollback.Authorization == null || rollback.Authorization.IsUsable
                       : rollback.SynchronizedReplay || rollback.Authorization != null &&
                          rollback.Authorization.IsAcceptedForSpecificBatchRollback(
                             synchronizedExecution: true,
                             rollback.AuthorityRevision,
                             rollback.PreviousRevision,
                             rollback.AppliedRevision));
        }

        internal static bool HasEnabledPawnOverrideForWorkType(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            var workGivers = GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef def = workGivers[i]?.def;
                if (TryGetPawnWorkGiverOverride(pawn, def, out int priority) &&
                    priority > WorkPrioritySystem.DisabledPriority)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool LockedSubWorkOverridesDisabledParent()
        {
            return !MultiplayerBridge.Active &&
                   BetterWorkTabMod.Settings?.subWorkDisabledParentMode == BetterWorkTabSettings.SubWorkDisabledParentMode.LockedSubWorkOverridesParent;
        }

        internal static int GetExecutionPriorityForWorkType(Pawn pawn, WorkTypeDef workType, int parentPriority)
        {
            parentPriority = WorkPrioritySystem.ClampPriority(parentPriority);
            if (TimePriorityService.IsWorkTypeDisabledBySchedule(pawn, workType))
            {
                return WorkPrioritySystem.DisabledPriority;
            }

            if (parentPriority > WorkPrioritySystem.DisabledPriority ||
                !LockedSubWorkOverridesDisabledParent() ||
                !TryGetHighestEnabledPawnOverridePriorityForWorkType(pawn, workType, out int lockedPriority))
            {
                return parentPriority;
            }

            return lockedPriority;
        }

        internal static bool LockedPawnOverrideCanRunWhenParentDisabled(Pawn pawn, WorkGiverDef workGiver, WorkTypeDef workType)
        {
            return LockedSubWorkOverridesDisabledParent() &&
                   !TimePriorityService.IsWorkTypeDisabledBySchedule(pawn, workType) &&
                   TryGetPawnWorkGiverOverride(pawn, workGiver, out int priority) &&
                   priority > WorkPrioritySystem.DisabledPriority &&
                   workType != null &&
                   GetTargetWorkType(workGiver) == workType;
        }

        private static bool TryGetHighestEnabledPawnOverridePriorityForWorkType(Pawn pawn, WorkTypeDef workType, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            if (pawn == null || workType == null)
            {
                return false;
            }

            bool found = false;
            var workGivers = GetDisplayWorkGiversForWorkType(workType);
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef def = workGivers[i]?.def;
                if (!TryGetPawnWorkGiverOverride(pawn, def, out int overridePriority) ||
                    overridePriority <= WorkPrioritySystem.DisabledPriority)
                {
                    continue;
                }

                priority = found
                    ? Math.Min(priority, overridePriority)
                    : overridePriority;
                found = true;
            }

            return found;
        }

        internal static int GetWorkGiverPriority(Pawn pawn, WorkGiverDef workGiver, int defaultPriority)
        {
            if (workGiver == null)
            {
                return WorkPrioritySystem.ClampPriority(defaultPriority);
            }

            var data = ExistingData;
            if (data == null) return WorkPrioritySystem.ClampPriority(defaultPriority);

            // 1. Pawn-specific override
            if (pawn != null &&
                data.PawnWorkGiverPriorityOverrides.TryGetValue(pawn.thingIDNumber, out var pawnDict) &&
                pawnDict != null &&
                pawnDict.TryGetValue(workGiver.defName, out int pawnPriority))
            {
                return WorkPrioritySystem.ClampPriority(pawnPriority);
            }
            
            // 2. Global override (Pawn ID -1)
            bool globalPriorityCleared = data.GlobalWorkGiverPriorityClears != null &&
                data.GlobalWorkGiverPriorityClears.Contains(workGiver.defName);
            if (!globalPriorityCleared &&
                data.PawnWorkGiverPriorityOverrides.TryGetValue(-1, out var globalDict) &&
                globalDict != null &&
                globalDict.TryGetValue(workGiver.defName, out int globalPriority))
            {
                return WorkPrioritySystem.ClampPriority(globalPriority);
            }

            return WorkPrioritySystem.ClampPriority(defaultPriority);
        }

        internal static int GetInheritedWorkGiverPriority(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            int defaultPriority = ParentPriorityRead.GetLive(pawn, workType);
            if (workGiver == null)
            {
                return WorkPrioritySystem.ClampPriority(defaultPriority);
            }

            var data = ExistingData;
            bool globalPriorityCleared = data?.GlobalWorkGiverPriorityClears != null &&
                data.GlobalWorkGiverPriorityClears.Contains(workGiver.defName);
            if (!globalPriorityCleared &&
                data?.PawnWorkGiverPriorityOverrides != null &&
                data.PawnWorkGiverPriorityOverrides.TryGetValue(-1, out var globalDict) &&
                globalDict != null &&
                globalDict.TryGetValue(workGiver.defName, out int globalPriority))
            {
                return TimePriorityService.GetEffectiveWorkGiverPriority(
                    null,
                    workType,
                    workGiver,
                    globalPriority);
            }

            return WorkPrioritySystem.ClampPriority(defaultPriority);
        }

        internal static bool DiscardMutationBatch()
        {
            if (_mutationBatchDepth != 0 || !_mutationBatchChanged)
            {
                return false;
            }

            _mutationBatchChanged = false;
            InvalidateCaches();
            return true;
        }

        private static List<string> GetBaselineWorkGiverOrder(WorkTypeDef workType)
        {
            if (workType == null)
            {
                return new List<string>();
            }

            return DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(def => GetTargetWorkType(def) == workType)
                .OrderByDescending(def => def.priorityInType)
                .Select(def => def.defName)
                .ToList();
        }

        private static void RecordPlayerMovedWorkGiver(WorkTypeDef workType, WorkGiverDef workGiverDef)
        {
            var data = Data;
            if (data == null || workType?.defName == null || workGiverDef?.defName == null)
            {
                return;
            }

            data.EnsureCollections();
            if (!data.PlayerMovedWorkGiversByWorkType.TryGetValue(workType.defName, out var moved) || moved == null)
            {
                moved = new List<string>();
                data.PlayerMovedWorkGiversByWorkType[workType.defName] = moved;
            }

            if (!moved.Contains(workGiverDef.defName))
            {
                moved.Add(workGiverDef.defName);
            }
        }

        private static void MarkMutationChanged(bool notifyDependents)
        {
            InvalidateCaches();
            if (_mutationBatchDepth > 0)
            {
                _mutationBatchChanged = true;
                return;
            }

            if (notifyDependents)
            {
                NotifySubWorkDependentsChanged();
            }
        }

        private static void NotifySubWorkDependentsChanged()
        {
            UI.WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.SubWorkOverride |
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.Columns |
                UI.WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
            WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
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

        internal static int ComputePresentationAuditSignature()
        {
            unchecked
            {
                int hash = 17;
                WorkGiverReassignmentData data = ExistingData;
                if (data == null)
                {
                    return hash;
                }

                if (data.WorkGiverToWorkTypeMap != null)
                {
                    foreach (var entry in data.WorkGiverToWorkTypeMap)
                    {
                        hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(entry.Key ?? string.Empty);
                        hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(entry.Value ?? string.Empty);
                    }
                }

                if (data.PawnWorkGiverPriorityOverrides != null)
                {
                    foreach (var pawnEntry in data.PawnWorkGiverPriorityOverrides)
                    {
                        hash = (hash * 397) ^ pawnEntry.Key;
                        if (pawnEntry.Value == null) continue;
                        foreach (var priorityEntry in pawnEntry.Value)
                        {
                            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(priorityEntry.Key ?? string.Empty);
                            hash = (hash * 397) ^ priorityEntry.Value;
                        }
                    }
                }

                if (data.WorkTypeWorkGiverOrder != null)
                {
                    foreach (var orderEntry in data.WorkTypeWorkGiverOrder.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                    {
                        hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(orderEntry.Key ?? string.Empty);
                        if (orderEntry.Value == null) continue;
                        for (int i = 0; i < orderEntry.Value.Count; i++)
                        {
                            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(orderEntry.Value[i] ?? string.Empty);
                        }
                    }
                }

                if (data.GlobalWorkGiverPriorityClears != null)
                {
                    foreach (string workGiverDefName in data.GlobalWorkGiverPriorityClears.OrderBy(name => name, StringComparer.Ordinal))
                    {
                        hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(workGiverDefName ?? string.Empty);
                        hash = (hash * 397) ^ 1;
                    }
                }

                if (data.GlobalWorkTypeOrderClears != null)
                {
                    foreach (string workTypeDefName in data.GlobalWorkTypeOrderClears.OrderBy(name => name, StringComparer.Ordinal))
                    {
                        hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(workTypeDefName ?? string.Empty);
                        hash = (hash * 397) ^ 2;
                    }
                }

                return hash;
            }
        }

        internal static void CleanupOrphanedReassignments()
        {
            var data = Data;
            if (data == null)
            {
                return;
            }

            data.EnsureCollections();
            var orphanedWorkGivers = new List<string>();
            foreach (var wgName in data.WorkGiverToWorkTypeMap.Keys.ToList())
            {
                if (DefDatabase<WorkGiverDef>.GetNamedSilentFail(wgName) == null)
                {
                    orphanedWorkGivers.Add(wgName);
                }
            }

            var orphanedGlobalPriorityClears = new List<string>();
            foreach (string wgName in data.GlobalWorkGiverPriorityClears.ToList())
            {
                WorkGiverDef workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(wgName);
                if (workGiver == null || GetTargetWorkType(workGiver) == null)
                {
                    orphanedGlobalPriorityClears.Add(wgName);
                }
            }

            var orphanedGlobalOrderClears = new List<string>();
            foreach (string workTypeName in data.GlobalWorkTypeOrderClears.ToList())
            {
                if (DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeName) == null)
                {
                    orphanedGlobalOrderClears.Add(workTypeName);
                }
            }

            foreach (var wgName in orphanedWorkGivers)
            {
                data.WorkGiverToWorkTypeMap.Remove(wgName);
                foreach (var list in data.WorkTypeWorkGiverOrder.Values)
                {
                    list?.Remove(wgName);
                }

                foreach (var pawnDict in data.PawnWorkGiverPriorityOverrides.Values)
                {
                    pawnDict?.Remove(wgName);
                }

                foreach (var pawnDict in data.PawnWorkGiverOrdering.Values)
                {
                    pawnDict?.Remove(wgName);
                }
            }

            foreach (string wgName in orphanedGlobalPriorityClears)
            {
                data.GlobalWorkGiverPriorityClears.Remove(wgName);
            }

            foreach (string workTypeName in orphanedGlobalOrderClears)
            {
                data.GlobalWorkTypeOrderClears.Remove(workTypeName);
            }

            int removedCount = orphanedWorkGivers.Count +
                orphanedGlobalPriorityClears.Count +
                orphanedGlobalOrderClears.Count;
            if (removedCount > 0)
            {
                data.SyncVersion++;
                InvalidateCaches();
                BetterWorkTabMod.DebugLog(
                    $"Cleaned up {removedCount} orphaned WorkGiver reassignment and global tombstone entries",
                    DebugFeature.General);
            }
        }
    }
}
