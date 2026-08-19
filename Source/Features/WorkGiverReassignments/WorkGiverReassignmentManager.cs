using Better_Work_Tab;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2;
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
        private static readonly Dictionary<string, List<WorkGiver>> OrderedWorkGiverCache = new Dictionary<string, List<WorkGiver>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<WorkGiver>> DisplayWorkGiverCache = new Dictionary<string, List<WorkGiver>>(StringComparer.Ordinal);
        private static int _mutationBatchDepth;
        private static bool _mutationBatchChanged;

        private static int _cachedSyncVersion = -1;
        private static int _cachedActivationSyncVersion = int.MinValue;
        private static GameComponent_BWTWorldSettings _cachedActivationComponent;
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

        internal sealed class WorkloadSpecificPriorityBatchEntry
        {
            internal WorkloadSpecificPriorityBatchEntry(
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

        internal sealed class WorkloadSpecificOrderBatchEntry
        {
            internal WorkloadSpecificOrderBatchEntry(
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

        internal sealed class WorkloadSpecificJobBatchRollback
        {
            internal WorkloadSpecificJobBatchRollback(
                WorkGiverReassignmentData previousData,
                string previousFingerprint,
                int previousRevision,
                int appliedRevision,
                string appliedFingerprint,
                long authorityRevision,
                TimePriorityMutationAuthorization authorization)
            {
                PreviousData = previousData;
                PreviousFingerprint = previousFingerprint ?? string.Empty;
                PreviousRevision = previousRevision;
                AppliedRevision = appliedRevision;
                AppliedFingerprint = appliedFingerprint ?? string.Empty;
                AuthorityRevision = authorityRevision;
                Authorization = authorization;
            }

            internal WorkGiverReassignmentData PreviousData { get; private set; }
            internal string PreviousFingerprint { get; private set; }
            internal int PreviousRevision { get; private set; }
            internal int AppliedRevision { get; private set; }
            internal string AppliedFingerprint { get; private set; }
            internal long AuthorityRevision { get; private set; }
            internal TimePriorityMutationAuthorization Authorization { get; private set; }
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
                var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                return component?.WorkGiverReassignments ?? Settings?.LegacyWorkGiverReassignments;
            }
        }

        private static WorkGiverReassignmentData Data
        {
            get
            {
                var component = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                if (component != null)
                {
                    return component.EnsureWorkGiverReassignmentData();
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
            Data?.ComputeStateFingerprint() ?? WorkloadCanonical.Fingerprint(string.Empty);

        internal static bool CanSynchronouslyAcknowledgeExactPawnOrderMutation =>
            !MultiplayerBridge.Active;

        internal static bool CanSynchronouslyAcknowledgeExactGlobalMutation =>
            !MultiplayerBridge.Active;

        internal static bool HasActiveData
        {
            get
            {
                WorkGiverReassignmentData data = ExistingData;
                GameComponent_BWTWorldSettings component =
                    Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                int version = data?.SyncVersion ?? 0;
                if (!ReferenceEquals(component, _cachedActivationComponent) ||
                    version != _cachedActivationSyncVersion)
                {
                    _cachedActivationComponent = component;
                    _cachedActivationSyncVersion = version;
                    _cachedHasAnyData = data != null && data.HasAnyData();
                }

                return _cachedHasAnyData;
            }
        }

        internal static void SetPawnOverrideSynced(int pawnId, string workGiverDefName, int priority)
        {
            if (MultiplayerBridge.Active)
            {
                SyncSetPawnOverride(pawnId, workGiverDefName, priority);
                return;
            }

            ApplyPawnOverride(pawnId, workGiverDefName, priority);
        }

        internal static void ClearPawnOverrideSynced(int pawnId, string workGiverDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncClearPawnOverride(pawnId, workGiverDefName);
                return;
            }

            ApplyClearPawnOverride(pawnId, workGiverDefName);
        }

        internal static void SetPawnOverridesBatchSynced(string workGiverDefName, List<int> pawnIds, List<int> priorities)
        {
            if (MultiplayerBridge.Active)
            {
                SyncSetPawnOverridesBatch(workGiverDefName, pawnIds, priorities);
                return;
            }

            ApplyPawnOverridesBatch(workGiverDefName, pawnIds, priorities);
        }

        internal static void ClearPawnOverridesForWorkTypeSynced(int pawnId, string workTypeDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncClearPawnOverridesForWorkType(pawnId, workTypeDefName);
                return;
            }

            ApplyClearPawnOverridesForWorkType(pawnId, workTypeDefName);
        }

        internal static void EnableParentWorkTypeSynced(int pawnId, string workTypeDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncEnableParentWorkType(pawnId, workTypeDefName);
                return;
            }

            ApplyEnableParentWorkType(pawnId, workTypeDefName);
        }

        internal static void EnableParentAndClearSubOverridesSynced(int pawnId, string workTypeDefName)
        {
            if (MultiplayerBridge.Active)
            {
                SyncEnableParentAndClearSubOverrides(pawnId, workTypeDefName);
                return;
            }

            ApplyEnableParentAndClearSubOverrides(pawnId, workTypeDefName);
        }

        internal static void EnableParentAndSetOnlySubOverrideSynced(int pawnId, string workTypeDefName, string workGiverDefName, int priority)
        {
            if (MultiplayerBridge.Active)
            {
                SyncEnableParentAndSetOnlySubOverride(pawnId, workTypeDefName, workGiverDefName, priority);
                return;
            }

            ApplyEnableParentAndSetOnlySubOverride(pawnId, workTypeDefName, workGiverDefName, priority);
        }

        internal static bool SetPawnWorkGiverOrderSynced(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            if (MultiplayerBridge.Active)
            {
                SyncSetPawnWorkGiverOrder(pawnId, workTypeDefName, orderedWorkGiverNames);
                return true;
            }

            return SetPawnWorkGiverOrder(pawnId, workTypeDefName, orderedWorkGiverNames);
        }

        internal static void MoveWithinWorkTypeSynced(string workTypeDefName, string workGiverDefName, int newIndex, Pawn pawn = null)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (pawnId == -1)
            {
                TryMoveWorkGiverLayout(workGiverDefName, workTypeDefName, newIndex, out _);
                return;
            }

            if (MultiplayerBridge.Active)
            {
                SyncMoveWithinWorkType(workTypeDefName, workGiverDefName, newIndex, pawnId);
                return;
            }

            ApplyMoveWithinWorkType(workTypeDefName, workGiverDefName, newIndex, pawnId);
        }

        /// <summary>
        /// Clear caches when the sync version changes or the settings are reloaded.
        /// </summary>
        internal static void InvalidateCaches()
        {
            WorkGiverTargetCache.Clear();
            OrderedWorkGiverCache.Clear();
            DisplayWorkGiverCache.Clear();
            _cachedActivationSyncVersion = int.MinValue;
            _cachedActivationComponent = null;
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
        internal static bool TryApplyWorkloadSpecificJobBatch(
            IReadOnlyList<WorkloadSpecificPriorityBatchEntry> priorityEntries,
            IReadOnlyList<WorkloadSpecificOrderBatchEntry> orderEntries,
            int expectedSyncVersion,
            long expectedAuthorityRevision,
            TimePriorityMutationAuthorization authorization,
            out WorkloadSpecificJobBatchRollback rollback,
            out string reason)
        {
            rollback = null;
            reason = string.Empty;
            var priorities = (priorityEntries ?? new WorkloadSpecificPriorityBatchEntry[0])
                .Where(entry => entry != null)
                .OrderBy(entry => entry.CanonicalKey, StringComparer.Ordinal)
                .ToList();
            var orders = (orderEntries ?? new WorkloadSpecificOrderBatchEntry[0])
                .Where(entry => entry != null)
                .OrderBy(entry => entry.CanonicalKey, StringComparer.Ordinal)
                .ToList();
            if (priorities.Count == 0 && orders.Count == 0)
            {
                return true;
            }

            if (!CanUseWorkloadSpecificCapability(
                    expectedAuthorityRevision,
                    expectedSyncVersion,
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
                    WorkloadSpecificPriorityBatchEntry entry = priorities[i];
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
                    WorkloadSpecificOrderBatchEntry entry = orders[i];
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
                rollback = new WorkloadSpecificJobBatchRollback(
                    previousData,
                    previousFingerprint,
                    previousRevision,
                    data.SyncVersion,
                    data.ComputeStateFingerprint(),
                    expectedAuthorityRevision,
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

        internal static bool TryRestoreWorkloadSpecificJobBatch(
            WorkloadSpecificJobBatchRollback rollback,
            out string reason)
        {
            reason = string.Empty;
            if (rollback == null)
            {
                return true;
            }

            if (!CanUseWorkloadSpecificCapability(
                    rollback.AuthorityRevision,
                    rollback.AppliedRevision,
                    rollback.Authorization))
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

        private static bool CanUseWorkloadSpecificCapability(
            long expectedAuthorityRevision,
            int expectedSyncVersion,
            TimePriorityMutationAuthorization authorization)
        {
            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(expectedAuthorityRevision) &&
                   (!MultiplayerBridge.Active
                       ? authorization == null || authorization.IsUsable
                       : authorization != null &&
                         authorization.IsAcceptedForSpecificBatch(
                             synchronizedExecution: true,
                             expectedAuthorityRevision,
                             expectedSyncVersion));
        }

        private static bool TryValidateDistinctBatchKeys(
            IReadOnlyList<WorkloadSpecificPriorityBatchEntry> priorities,
            IReadOnlyList<WorkloadSpecificOrderBatchEntry> orders,
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
            IReadOnlyList<WorkloadSpecificPriorityBatchEntry> entries,
            int expectedSyncVersion,
            long expectedAuthorityRevision,
            out string reason)
        {
            reason = string.Empty;
            for (int i = 0; i < entries.Count; i++)
            {
                WorkloadSpecificPriorityBatchEntry entry = entries[i];
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
            IReadOnlyList<WorkloadSpecificOrderBatchEntry> entries,
            int expectedSyncVersion,
            long expectedAuthorityRevision,
            out string reason)
        {
            reason = string.Empty;
            for (int i = 0; i < entries.Count; i++)
            {
                WorkloadSpecificOrderBatchEntry entry = entries[i];
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
            IReadOnlyList<WorkloadSpecificPriorityBatchEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                WorkloadSpecificPriorityBatchEntry entry = entries[i];
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
            IReadOnlyList<WorkloadSpecificOrderBatchEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                WorkloadSpecificOrderBatchEntry entry = entries[i];
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

        internal static void MigrateLegacySettingsDataIfNeeded(GameComponent_BWTWorldSettings component)
        {
            if (component == null)
            {
                OnSettingsLoaded();
                return;
            }

            component.EnsureWorkGiverReassignmentData();

            var settings = Settings;
            var legacy = settings?.LegacyWorkGiverReassignments;
            if (legacy != null && legacy.HasAnyData())
            {
                if (!component.WorkGiverReassignments.HasAnyData())
                {
                    component.WorkGiverReassignments = legacy.Clone();
                    BetterWorkTabMod.DebugLog("Migrated legacy global sub-work reassignment settings into this save.", DebugFeature.General);
                }
                else
                {
                    BetterWorkTabMod.DebugLog("Ignored legacy global sub-work reassignment settings because this save already has sub-work data.", DebugFeature.General);
                }

                ClearLegacySettingsAfterLoad(settings);
            }

            OnWorldDataLoaded();
        }

        private static void ClearLegacySettingsAfterLoad(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.LegacyWorkGiverReassignments = null;
            LongEventHandler.ExecuteWhenFinished(settings.Write);
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

            if (applyPrioritySort && pawn == null && OrderedWorkGiverCache.TryGetValue(workType.defName, out var cached))
            {
                return cached;
            }

            if (!applyPrioritySort && pawn == null && DisplayWorkGiverCache.TryGetValue(workType.defName, out cached))
            {
                return cached;
            }

            var result = new List<WorkGiver>();
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

            var handled = new HashSet<string>(StringComparer.Ordinal);
            if (orderedNames != null)
            {
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

            var remaining = new List<WorkGiverDef>();
            foreach (var def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (GetTargetWorkType(def) != workType)
                {
                    continue;
                }

                if (!handled.Contains(def.defName))
                {
                    remaining.Add(def);
                }
            }

            remaining.Sort((a, b) => b.priorityInType.CompareTo(a.priorityInType));
            for (int i = 0; i < remaining.Count; i++)
            {
                var worker = remaining[i].Worker;
                if (worker != null)
                {
                    result.Add(worker);
                }
            }

            if (applyPrioritySort)
            {
                // Apply sorting based on priorities (pawn-specific or global defaults)
                var sortingPawn = pawn;
                int defaultPrio = pawn == null
                    ? WorkPrioritySystem.GetDefaultEnabledPriority()
                    : WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);

                var indexed = result.Select((g, idx) => new { g, idx }).ToList();
                indexed.Sort((a, b) =>
                {
                    int pa = GetWorkGiverPriority(sortingPawn, a.g.def, defaultPrio);
                    int pb = GetWorkGiverPriority(sortingPawn, b.g.def, defaultPrio);
                    if (sortingPawn != null)
                    {
                        pa = TimePriorityService.GetEffectiveWorkGiverPriority(sortingPawn, workType, a.g.def, pa);
                        pb = TimePriorityService.GetEffectiveWorkGiverPriority(sortingPawn, workType, b.g.def, pb);
                    }

                    // Treat 0 as disabled (lowest priority)
                    int valA = (pa == 0) ? 999 : pa;
                    int valB = (pb == 0) ? 999 : pb;

                    int c = valA.CompareTo(valB);
                    if (c != 0) return c;

                    // Secondary sort: saved/manual order.
                    c = a.idx.CompareTo(b.idx);
                    if (c != 0) return c;

                    return b.g.def.priorityInType.CompareTo(a.g.def.priorityInType);
                });
                result = indexed.Select(x => x.g).ToList();
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

        internal static int CountPawnPriorityOverrides(WorkTypeDef workType)
        {
            var data = ExistingData;
            if (data?.PawnWorkGiverPriorityOverrides == null || workType == null)
            {
                return 0;
            }

            var workGiverNames = new HashSet<string>(
                GetOrderedWorkGiversForWorkType(workType).Select(wg => wg.def.defName),
                StringComparer.Ordinal);

            int count = 0;
            foreach (var pawnEntry in data.PawnWorkGiverPriorityOverrides)
            {
                if (pawnEntry.Key == -1 || pawnEntry.Value == null)
                {
                    continue;
                }

                foreach (string workGiverName in pawnEntry.Value.Keys)
                {
                    if (workGiverNames.Contains(workGiverName))
                    {
                        count++;
                    }
                }
            }

            return count;
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

        /// <summary>
        /// Restores an exact pawn order, including the absence of a pawn-local
        /// override. The caller owns the transaction and may suppress the
        /// normal notification while it is rolling back.
        /// </summary>
        internal static bool RestorePawnWorkGiverOrderSnapshot(
            PawnWorkGiverOrderSnapshot snapshot,
            bool notify = true)
        {
            if (snapshot == null || snapshot.PawnId < 0 || snapshot.WorkTypeDefName.NullOrEmpty())
            {
                return false;
            }

            return snapshot.HasStoredOrder
                ? SetPawnWorkGiverOrderExact(
                    snapshot.PawnId,
                    snapshot.WorkTypeDefName,
                    snapshot.OrderedWorkGiverNames,
                    notify)
                : ClearPawnWorkGiverOrderExact(
                    snapshot.PawnId,
                    snapshot.WorkTypeDefName,
                    notify);
        }

        /// <summary>
        /// Removes only the exact pawn-local order entry. It intentionally does
        /// not touch global ordering, reassignment mapping, or priority
        /// overrides.
        /// </summary>
        internal static bool ClearPawnWorkGiverOrderExact(
            int pawnId,
            string workTypeDefName,
            bool notify = true)
        {
            if (pawnId < 0 || workTypeDefName.NullOrEmpty() ||
                !CanSynchronouslyAcknowledgeExactPawnOrderMutation)
            {
                return false;
            }

            WorkGiverReassignmentData data = ExistingData;
            if (data == null || data.PawnWorkGiverOrdering == null ||
                !data.PawnWorkGiverOrdering.TryGetValue(pawnId, out var pawnOrders) ||
                pawnOrders == null || !pawnOrders.Remove(workTypeDefName))
            {
                return true;
            }

            if (pawnOrders.Count == 0)
            {
                data.PawnWorkGiverOrdering.Remove(pawnId);
            }

            data.SyncVersion++;
            if (notify) NotifySubWorkDataChanged();
            return true;
        }

        /// <summary>
        /// Writes a previously captured exact pawn order without normalizing or
        /// appending entries. The list must still be a permutation of the
        /// currently resolvable work-givers for the work type; otherwise the
        /// operation fails closed.
        /// </summary>
        internal static bool SetPawnWorkGiverOrderExact(
            int pawnId,
            string workTypeDefName,
            IReadOnlyList<string> orderedWorkGiverNames,
            bool notify = true)
        {
            if (pawnId < 0 || workTypeDefName.NullOrEmpty() || orderedWorkGiverNames == null ||
                !CanSynchronouslyAcknowledgeExactPawnOrderMutation)
            {
                return false;
            }

            WorkTypeDef workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null || !IsExactWorkGiverOrder(workType, orderedWorkGiverNames))
            {
                return false;
            }

            WorkGiverReassignmentData data = Data;
            if (data == null) return false;
            data.EnsureCollections();
            if (!data.PawnWorkGiverOrdering.TryGetValue(pawnId, out var pawnOrders) || pawnOrders == null)
            {
                pawnOrders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                data.PawnWorkGiverOrdering[pawnId] = pawnOrders;
            }

            var exact = new List<string>(orderedWorkGiverNames);
            if (pawnOrders.TryGetValue(workTypeDefName, out var existing) &&
                existing != null && existing.SequenceEqual(exact))
            {
                return true;
            }

            pawnOrders[workTypeDefName] = exact;
            data.SyncVersion++;
            if (notify) NotifySubWorkDataChanged();
            return true;
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

        /// <summary>
        /// Applies one exact global WorkGiver priority state after validating
        /// the captured state, BWT authority generation, and reassignment
        /// revision.  Absent removes all global opinion; Clear persists an
        /// explicit suppression; Set stores the clamped priority.
        /// </summary>
        internal static bool TryApplyGlobalWorkGiverPriority(
            GlobalWorkGiverPrioritySnapshot expectedCurrent,
            ExactGlobalStateKind desiredState,
            int desiredPriority = WorkPrioritySystem.DisabledPriority,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                expectedCurrent.WorkGiverDefName.NullOrEmpty() ||
                !CanSynchronouslyAcknowledgeExactGlobalMutation ||
                (desiredState != ExactGlobalStateKind.Absent &&
                 desiredState != ExactGlobalStateKind.Set &&
                 desiredState != ExactGlobalStateKind.Clear))
            {
                return false;
            }

            if (desiredState == ExactGlobalStateKind.Set)
            {
                if (DefDatabase<WorkGiverDef>.GetNamedSilentFail(expectedCurrent.WorkGiverDefName) == null ||
                    WorkPrioritySystem.ClampPriority(desiredPriority) != desiredPriority)
                {
                    return false;
                }
            }

            if (!TryPrepareExactGlobalMutation(
                    expectedCurrent.SyncVersion,
                    expectedCurrent.AuthorityRevision))
            {
                return false;
            }

            GlobalWorkGiverPrioritySnapshot observed =
                CaptureGlobalWorkGiverPrioritySnapshot(expectedCurrent.WorkGiverDefName);
            if (!IsSameGlobalPriorityState(observed, expectedCurrent))
            {
                return false;
            }

            if (observed.State == desiredState &&
                (desiredState != ExactGlobalStateKind.Set || observed.Priority == desiredPriority))
            {
                return true;
            }

            WorkGiverReassignmentData data = Data;
            if (data == null || !ApplyGlobalWorkGiverPriorityState(
                    data,
                    expectedCurrent.WorkGiverDefName,
                    desiredState,
                    desiredPriority))
            {
                return data != null && observed.State == desiredState;
            }

            int expectedRevision = unchecked(expectedCurrent.SyncVersion + 1);
            if (CurrentSyncVersion != expectedRevision ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(expectedCurrent.AuthorityRevision))
            {
                return false;
            }

            MarkMutationChanged(notify);
            return true;
        }

        internal static bool TrySetGlobalWorkGiverPriority(
            string workGiverDefName,
            int priority,
            GlobalWorkGiverPrioritySnapshot expectedCurrent,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                !string.Equals(expectedCurrent.WorkGiverDefName, workGiverDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkGiverPriority(
                expectedCurrent,
                ExactGlobalStateKind.Set,
                priority,
                notify);
        }

        /// <summary>
        /// Persists an explicit global clear.  This differs from the legacy
        /// ClearPawnOverrideSynced(-1, ...) operation, which intentionally
        /// means removal back to absence for existing callers.
        /// </summary>
        internal static bool TryClearGlobalWorkGiverPriority(
            string workGiverDefName,
            GlobalWorkGiverPrioritySnapshot expectedCurrent,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                !string.Equals(expectedCurrent.WorkGiverDefName, workGiverDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkGiverPriority(
                expectedCurrent,
                ExactGlobalStateKind.Clear,
                WorkPrioritySystem.DisabledPriority,
                notify);
        }

        internal static bool TryRemoveGlobalWorkGiverPriority(
            string workGiverDefName,
            GlobalWorkGiverPrioritySnapshot expectedCurrent,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                !string.Equals(expectedCurrent.WorkGiverDefName, workGiverDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkGiverPriority(
                expectedCurrent,
                ExactGlobalStateKind.Absent,
                WorkPrioritySystem.DisabledPriority,
                notify);
        }

        internal static bool TryRestoreGlobalWorkGiverPrioritySnapshot(
            GlobalWorkGiverPrioritySnapshot snapshot,
            GlobalWorkGiverPrioritySnapshot expectedCurrent,
            bool notify = true)
        {
            if (snapshot == null || expectedCurrent == null ||
                !string.Equals(snapshot.WorkGiverDefName, expectedCurrent.WorkGiverDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkGiverPriority(
                expectedCurrent,
                snapshot.State,
                snapshot.Priority,
                notify);
        }

        /// <summary>
        /// Applies one exact global WorkType permutation using the same
        /// authority/revision guard as global priority state.  This seam does
        /// not update WorkGiverToWorkTypeMap or PlayerMovedWorkGiversByWorkType.
        /// </summary>
        internal static bool TryApplyGlobalWorkTypeOrder(
            GlobalWorkTypeOrderSnapshot expectedCurrent,
            ExactGlobalStateKind desiredState,
            IReadOnlyList<string> desiredOrder,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                expectedCurrent.WorkTypeDefName.NullOrEmpty() ||
                !CanSynchronouslyAcknowledgeExactGlobalMutation ||
                (desiredState != ExactGlobalStateKind.Absent &&
                 desiredState != ExactGlobalStateKind.Set &&
                 desiredState != ExactGlobalStateKind.Clear))
            {
                return false;
            }

            if (desiredState == ExactGlobalStateKind.Set &&
                !IsCompleteGlobalWorkTypeOrder(expectedCurrent.WorkTypeDefName, desiredOrder))
            {
                return false;
            }

            if (!TryPrepareExactGlobalMutation(
                    expectedCurrent.SyncVersion,
                    expectedCurrent.AuthorityRevision))
            {
                return false;
            }

            GlobalWorkTypeOrderSnapshot observed =
                CaptureGlobalWorkTypeOrderSnapshot(expectedCurrent.WorkTypeDefName);
            if (!IsSameGlobalOrderState(observed, expectedCurrent))
            {
                return false;
            }

            bool desiredEqualsObserved = observed.State == desiredState &&
                (desiredState != ExactGlobalStateKind.Set ||
                 observed.OrderedWorkGiverNames.SequenceEqual(desiredOrder ?? new string[0]));
            if (desiredEqualsObserved)
            {
                return true;
            }

            WorkGiverReassignmentData data = Data;
            if (data == null || !ApplyGlobalWorkTypeOrderState(
                    data,
                    expectedCurrent.WorkTypeDefName,
                    desiredState,
                    desiredOrder))
            {
                return data != null && desiredEqualsObserved;
            }

            int expectedRevision = unchecked(expectedCurrent.SyncVersion + 1);
            if (CurrentSyncVersion != expectedRevision ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(expectedCurrent.AuthorityRevision))
            {
                return false;
            }

            MarkMutationChanged(notify);
            return true;
        }

        internal static bool TrySetGlobalWorkTypeOrder(
            string workTypeDefName,
            IReadOnlyList<string> orderedWorkGiverNames,
            GlobalWorkTypeOrderSnapshot expectedCurrent,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                !string.Equals(expectedCurrent.WorkTypeDefName, workTypeDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkTypeOrder(
                expectedCurrent,
                ExactGlobalStateKind.Set,
                orderedWorkGiverNames,
                notify);
        }

        internal static bool TryClearGlobalWorkTypeOrder(
            string workTypeDefName,
            GlobalWorkTypeOrderSnapshot expectedCurrent,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                !string.Equals(expectedCurrent.WorkTypeDefName, workTypeDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkTypeOrder(
                expectedCurrent,
                ExactGlobalStateKind.Clear,
                null,
                notify);
        }

        internal static bool TryRemoveGlobalWorkTypeOrder(
            string workTypeDefName,
            GlobalWorkTypeOrderSnapshot expectedCurrent,
            bool notify = true)
        {
            if (expectedCurrent == null ||
                !string.Equals(expectedCurrent.WorkTypeDefName, workTypeDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkTypeOrder(
                expectedCurrent,
                ExactGlobalStateKind.Absent,
                null,
                notify);
        }

        internal static bool TryRestoreGlobalWorkTypeOrderSnapshot(
            GlobalWorkTypeOrderSnapshot snapshot,
            GlobalWorkTypeOrderSnapshot expectedCurrent,
            bool notify = true)
        {
            if (snapshot == null || expectedCurrent == null ||
                !string.Equals(snapshot.WorkTypeDefName, expectedCurrent.WorkTypeDefName, StringComparison.Ordinal))
            {
                return false;
            }

            return TryApplyGlobalWorkTypeOrder(
                expectedCurrent,
                snapshot.State,
                snapshot.OrderedWorkGiverNames,
                notify);
        }

        private static bool TryPrepareExactGlobalMutation(
            int expectedSyncVersion,
            long expectedAuthorityRevision)
        {
            if (!CanSynchronouslyAcknowledgeExactGlobalMutation ||
                CurrentSyncVersion != expectedSyncVersion ||
                !WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision) ||
                authorityRevision != expectedAuthorityRevision)
            {
                return false;
            }

            return WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision);
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

        internal static bool CanPawnUseMappedWorkType(Pawn pawn, WorkGiverDef def)
        {
            if (pawn?.workSettings == null)
            {
                return true;
            }

            var targetWorkType = GetTargetWorkType(def);
            if (targetWorkType == null)
            {
                return true;
            }

            if (pawn.WorkTypeIsDisabled(targetWorkType))
            {
                return false;
            }

            return WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, targetWorkType) > 0;
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

        internal static bool HasPawnWorkGiverOverride(Pawn pawn, WorkGiverDef workGiver)
        {
            return TryGetPawnWorkGiverOverride(pawn, workGiver, out _);
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
            int defaultPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
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

        [SyncMethod]
        public static void SyncSetPawnOverride(int pawnId, string workGiverDefName, int priority)
        {
            ApplyPawnOverride(pawnId, workGiverDefName, priority);
        }

        [SyncMethod]
        public static void SyncClearPawnOverride(int pawnId, string workGiverDefName)
        {
            ApplyClearPawnOverride(pawnId, workGiverDefName);
        }

        [SyncMethod]
        public static void SyncSetPawnOverridesBatch(string workGiverDefName, List<int> pawnIds, List<int> priorities)
        {
            ApplyPawnOverridesBatch(workGiverDefName, pawnIds, priorities);
        }

        [SyncMethod]
        public static void SyncClearPawnOverridesForWorkType(int pawnId, string workTypeDefName)
        {
            ApplyClearPawnOverridesForWorkType(pawnId, workTypeDefName);
        }

        [SyncMethod]
        public static void SyncEnableParentWorkType(int pawnId, string workTypeDefName)
        {
            ApplyEnableParentWorkType(pawnId, workTypeDefName);
        }

        [SyncMethod]
        public static void SyncEnableParentAndClearSubOverrides(int pawnId, string workTypeDefName)
        {
            ApplyEnableParentAndClearSubOverrides(pawnId, workTypeDefName);
        }

        [SyncMethod]
        public static void SyncEnableParentAndSetOnlySubOverride(int pawnId, string workTypeDefName, string workGiverDefName, int priority)
        {
            ApplyEnableParentAndSetOnlySubOverride(pawnId, workTypeDefName, workGiverDefName, priority);
        }

        private static void ApplyPawnOverride(int pawnId, string workGiverDefName, int priority)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver == null)
            {
                return;
            }

            SetPawnOverride(pawnId, workGiver, priority, notify: true);
        }

        private static void ApplyClearPawnOverride(int pawnId, string workGiverDefName)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver == null)
            {
                return;
            }

            ClearPawnOverride(pawnId, workGiver, notify: true);
        }

        private static void ApplyPawnOverridesBatch(string workGiverDefName, List<int> pawnIds, List<int> priorities)
        {
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiver == null || pawnIds == null || priorities == null)
            {
                return;
            }

            int count = Math.Min(pawnIds.Count, priorities.Count);
            if (count <= 0)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                changed |= SetPawnOverride(pawnIds[i], workGiver, priorities[i], notify: false);
            }

            if (changed)
            {
                NotifySubWorkDataChanged();
            }
        }

        private static bool SetPawnOverride(int pawnId, WorkGiverDef workGiver, int priority, bool notify)
        {
            var data = Data;
            if (data == null || workGiver == null)
            {
                return false;
            }

            data.EnsureCollections();
            priority = WorkPrioritySystem.ClampPriority(priority);

            bool removedGlobalClear = pawnId == -1 &&
                data.GlobalWorkGiverPriorityClears.Remove(workGiver.defName);

            if (!data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var dict) || dict == null)
            {
                dict = new Dictionary<string, int>(StringComparer.Ordinal);
                data.PawnWorkGiverPriorityOverrides[pawnId] = dict;
            }

            if (dict.TryGetValue(workGiver.defName, out int currentPriority) &&
                currentPriority == priority &&
                !removedGlobalClear)
            {
                return false;
            }

            dict[workGiver.defName] = priority;

            data.SyncVersion++;
            MirrorWorkGiverToExternalWorkTab(pawnId, workGiver);
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        private static bool ClearPawnOverride(int pawnId, WorkGiverDef workGiver, bool notify)
        {
            var data = Data;
            if (data == null || workGiver?.defName == null)
            {
                return false;
            }

            data.EnsureCollections();
            bool removedGlobalClear = pawnId == -1 &&
                data.GlobalWorkGiverPriorityClears.Remove(workGiver.defName);
            bool removedStoredValue = data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var dict) &&
                dict != null &&
                dict.Remove(workGiver.defName);
            if (!removedStoredValue && !removedGlobalClear)
            {
                return false;
            }

            if (removedStoredValue && dict.Count == 0)
            {
                data.PawnWorkGiverPriorityOverrides.Remove(pawnId);
            }

            data.SyncVersion++;
            MirrorWorkGiverToExternalWorkTab(pawnId, workGiver);
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        /// <summary>
        /// Republishes one pawn's work-giver priority to any external work-tab mod backing the numbers.
        /// Work-giver overrides live only in Better Work Tab, so nothing else propagates them.
        /// </summary>
        private static void MirrorWorkGiverToExternalWorkTab(int pawnId, WorkGiverDef workGiver)
        {
            if (ExternalPriorityMirror.IsSuspended || workGiver == null || pawnId < 0)
            {
                return;
            }

            Pawn pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            if (pawn != null)
            {
                ExternalPriorityMirror.NotifyWorkGiverChanged(pawn, workGiver);
            }
        }

        private static void ApplyClearPawnOverridesForWorkType(int pawnId, string workTypeDefName)
        {
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            ClearPawnOverridesForWorkType(pawnId, workType, notify: true);
        }

        private static void ApplyEnableParentWorkType(int pawnId, string workTypeDefName)
        {
            var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (pawn?.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (parentPriority > WorkPrioritySystem.DisabledPriority)
            {
                return;
            }

            WorkPrioritySystem.SetPriority(pawn.workSettings, workType, WorkPrioritySystem.GetDefaultEnabledPriority());
            NotifySubWorkDataChanged();
        }

        private static void ApplyEnableParentAndClearSubOverrides(int pawnId, string workTypeDefName)
        {
            var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (pawn?.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            bool changed = false;
            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (parentPriority <= WorkPrioritySystem.DisabledPriority)
            {
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, WorkPrioritySystem.GetDefaultEnabledPriority());
                changed = true;
            }

            changed |= ClearPawnOverridesForWorkType(pawnId, workType, notify: false);
            if (changed)
            {
                NotifySubWorkDataChanged();
            }
        }

        private static void ApplyEnableParentAndSetOnlySubOverride(int pawnId, string workTypeDefName, string workGiverDefName, int priority)
        {
            var pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (pawn?.workSettings == null || workType == null || workGiver == null || pawn.WorkTypeIsDisabled(workType))
            {
                return;
            }

            if (GetTargetWorkType(workGiver) != workType)
            {
                return;
            }

            bool changed = false;
            int parentPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            if (parentPriority <= WorkPrioritySystem.DisabledPriority)
            {
                WorkPrioritySystem.SetPriority(pawn.workSettings, workType, WorkPrioritySystem.GetDefaultEnabledPriority());
                changed = true;
            }

            changed |= SetClickedPawnOverrideForWorkType(pawnId, workType, workGiver, priority);
            if (changed)
            {
                NotifySubWorkDataChanged();
            }
        }

        private static bool SetClickedPawnOverrideForWorkType(int pawnId, WorkTypeDef workType, WorkGiverDef enabledWorkGiver, int priority)
        {
            var data = Data;
            if (data == null || workType == null || enabledWorkGiver == null)
            {
                return false;
            }

            data.EnsureCollections();
            priority = WorkPrioritySystem.ClampPriority(priority);
            if (priority <= WorkPrioritySystem.DisabledPriority)
            {
                return false;
            }

            // Enabling a pawn from an unassigned parent work type should only lock the
            // sub-job the player clicked. Other sub-jobs stay inherited/blank so they do
            // not all show gold override rings.
            return SetPawnOverride(pawnId, enabledWorkGiver, priority, notify: false);
        }

        private static bool ClearPawnOverridesForWorkType(int pawnId, WorkTypeDef workType, bool notify)
        {
            var data = Data;
            if (data == null || workType == null)
            {
                return false;
            }

            data.EnsureCollections();
            bool changed = false;
            data.PawnWorkGiverPriorityOverrides.TryGetValue(pawnId, out var dict);
            var workGivers = GetDisplayWorkGiversForWorkType(workType);
            if (dict != null)
            {
                for (int i = 0; i < workGivers.Count; i++)
                {
                    string defName = workGivers[i]?.def?.defName;
                    if (!defName.NullOrEmpty() && dict.Remove(defName))
                    {
                        changed = true;
                    }
                }
            }

            if (pawnId == -1 && data.GlobalWorkGiverPriorityClears != null)
            {
                for (int i = 0; i < workGivers.Count; i++)
                {
                    string defName = workGivers[i]?.def?.defName;
                    if (!defName.NullOrEmpty() && data.GlobalWorkGiverPriorityClears.Remove(defName))
                    {
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                return false;
            }

            if (dict != null && dict.Count == 0)
            {
                data.PawnWorkGiverPriorityOverrides.Remove(pawnId);
            }

            data.SyncVersion++;
            MirrorWorkTypeToExternalWorkTab(pawnId, workType);
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        /// <summary>
        /// Republishes a whole work type for one pawn, used when its overrides were cleared en masse.
        /// </summary>
        private static void MirrorWorkTypeToExternalWorkTab(int pawnId, WorkTypeDef workType)
        {
            if (ExternalPriorityMirror.IsSuspended || workType == null || pawnId < 0)
            {
                return;
            }

            Pawn pawn = PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId);
            if (pawn != null)
            {
                ExternalPriorityMirror.NotifyWorkTypeChanged(pawn, workType);
            }
        }

        internal static bool TryReassignWorkGiver(string workGiverDefName, string targetWorkTypeDefName, int? insertIndex, out string errorMsg)
        {
            errorMsg = null;
            var workGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiverDef == null)
            {
                errorMsg = $"WorkGiver '{workGiverDefName}' not found";
                return false;
            }

            var targetWorkTypeDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeDefName);
            if (targetWorkTypeDef == null)
            {
                errorMsg = $"WorkType '{targetWorkTypeDefName}' not found";
                return false;
            }

            return TryMoveWorkGiverLayout(
                workGiverDef.defName,
                targetWorkTypeDef.defName,
                insertIndex ?? GetOrderedWorkGiversForWorkType(targetWorkTypeDef).Count,
                out errorMsg);
        }

        [SyncMethod]
        public static void SyncSetPawnWorkGiverOrder(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            SetPawnWorkGiverOrder(pawnId, workTypeDefName, orderedWorkGiverNames);
        }

        private static bool SetPawnWorkGiverOrder(int pawnId, string workTypeDefName, List<string> orderedWorkGiverNames, bool notify = true)
        {
            var data = Data;
            if (data == null) return false;
            data.EnsureCollections();

            orderedWorkGiverNames = NormalizeWorkGiverOrder(workTypeDefName, orderedWorkGiverNames);
            if (orderedWorkGiverNames.Count == 0)
            {
                return false;
            }

            if (pawnId == -1)
            {
                // Global order
                bool removedGlobalClear = data.GlobalWorkTypeOrderClears.Remove(workTypeDefName);
                if (data.WorkTypeWorkGiverOrder.TryGetValue(workTypeDefName, out var existing) &&
                    existing != null &&
                    existing.SequenceEqual(orderedWorkGiverNames) &&
                    !removedGlobalClear)
                {
                    return false;
                }

                data.WorkTypeWorkGiverOrder[workTypeDefName] = new List<string>(orderedWorkGiverNames);
            }
            else
            {
                // Pawn-specific order
                if (!data.PawnWorkGiverOrdering.TryGetValue(pawnId, out var pawnOrders) || pawnOrders == null)
                {
                    pawnOrders = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    data.PawnWorkGiverOrdering[pawnId] = pawnOrders;
                }

                if (pawnOrders.TryGetValue(workTypeDefName, out var existing) &&
                    existing != null &&
                    existing.SequenceEqual(orderedWorkGiverNames))
                {
                    return false;
                }

                pawnOrders[workTypeDefName] = new List<string>(orderedWorkGiverNames);
            }

            data.SyncVersion++;
            if (notify)
            {
                NotifySubWorkDataChanged();
            }

            return true;
        }

        internal static void MoveWithinWorkType(string workTypeDefName, string workGiverDefName, int newIndex, Pawn pawn = null)
        {
            MoveWithinWorkTypeSynced(workTypeDefName, workGiverDefName, newIndex, pawn);
        }

        [SyncMethod]
        public static void SyncMoveWithinWorkType(string workTypeDefName, string workGiverDefName, int newIndex, int pawnId)
        {
            ApplyMoveWithinWorkType(workTypeDefName, workGiverDefName, newIndex, pawnId);
        }

        private static void ApplyMoveWithinWorkType(string workTypeDefName, string workGiverDefName, int newIndex, int pawnId)
        {
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null || workGiverDefName.NullOrEmpty())
            {
                return;
            }

            Pawn pawn = pawnId >= 0
                ? PawnsFinder.All_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == pawnId)
                : null;

            var currentOrder = GetDisplayWorkGiversForWorkType(workType, pawn)
                .Where(wg => wg?.def != null)
                .Select(wg => wg.def.defName)
                .ToList();

            if (!currentOrder.Remove(workGiverDefName))
            {
                return;
            }

            newIndex = Math.Max(0, Math.Min(newIndex, currentOrder.Count));
            currentOrder.Insert(newIndex, workGiverDefName);

            bool changed = SetPawnWorkGiverOrder(pawnId, workTypeDefName, currentOrder, notify: false);
            if (!changed)
            {
                return;
            }

            if (pawnId == -1)
            {
                var workGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
                RecordPlayerMovedWorkGiver(workType, workGiver);
                PruneBaselineAlignedMovedWorkGivers(workType);
            }

            NotifySubWorkDataChanged();
        }

        [SyncMethod]
        internal static void SyncReassignWorkGiver(string workGiverDefName, string targetWorkTypeDefName, int insertIndex)
        {
            var wg = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(targetWorkTypeDefName);
            if (wg == null || wt == null)
            {
                return;
            }

            ApplyReassignment(wg, wt, insertIndex);
        }

        /// <summary>
        /// Mutate mapping + ordering and invalidate caches.
        /// </summary>
        private static void ApplyReassignment(WorkGiverDef workGiverDef, WorkTypeDef targetWorkTypeDef, int? insertIndex = null)
        {
            var data = Data;
            if (data == null)
            {
                return;
            }

            data.EnsureCollections();
            data.WorkGiverToWorkTypeMap[workGiverDef.defName] = targetWorkTypeDef.defName;

            if (data.WorkTypeWorkGiverOrder != null)
            {
                foreach (var kv in data.WorkTypeWorkGiverOrder)
                {
                    kv.Value?.Remove(workGiverDef.defName);
                }
            }
            else
            {
                data.WorkTypeWorkGiverOrder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }

            if (!data.WorkTypeWorkGiverOrder.TryGetValue(targetWorkTypeDef.defName, out var targetList) || targetList == null)
            {
                targetList = new List<string>();
                data.WorkTypeWorkGiverOrder[targetWorkTypeDef.defName] = targetList;
            }

            // Reassignment creates/updates a canonical global order entry; it
            // must not leave an older explicit global-order clear shadowing it.
            data.GlobalWorkTypeOrderClears.Remove(targetWorkTypeDef.defName);

            int index = insertIndex.HasValue ? Math.Max(0, Math.Min(insertIndex.Value, targetList.Count)) : targetList.Count;
            targetList.Insert(index, workGiverDef.defName);
            RecordPlayerMovedWorkGiver(targetWorkTypeDef, workGiverDef);
            PruneBaselineAlignedMovedWorkGivers(targetWorkTypeDef);
            RemoveWorkGiverFromPawnOrders(data, workGiverDef.defName);

            data.SyncVersion++;
            NotifySubWorkDataChanged();
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

        private static void PruneBaselineAlignedMovedWorkGivers(WorkTypeDef workType)
        {
            var data = Data;
            if (data?.PlayerMovedWorkGiversByWorkType == null || workType?.defName == null)
            {
                return;
            }

            if (!data.PlayerMovedWorkGiversByWorkType.TryGetValue(workType.defName, out var moved) || moved == null)
            {
                return;
            }

            for (int i = moved.Count - 1; i >= 0; i--)
            {
                var def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(moved[i]);
                if (def == null || !IsWorkGiverOutOfBaselinePosition(workType, def))
                {
                    moved.RemoveAt(i);
                }
            }

            if (moved.Count == 0)
            {
                data.PlayerMovedWorkGiversByWorkType.Remove(workType.defName);
            }
        }

        private static List<string> NormalizeWorkGiverOrder(string workTypeDefName, List<string> orderedWorkGiverNames)
        {
            var workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            if (workType == null)
            {
                return new List<string>();
            }

            var valid = GetDisplayWorkGiversForWorkType(workType)
                .Where(wg => wg?.def != null)
                .Select(wg => wg.def.defName)
                .ToList();
            var validSet = new HashSet<string>(valid, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(valid.Count);

            if (orderedWorkGiverNames != null)
            {
                for (int i = 0; i < orderedWorkGiverNames.Count; i++)
                {
                    string name = orderedWorkGiverNames[i];
                    if (!name.NullOrEmpty() && validSet.Contains(name) && seen.Add(name))
                    {
                        result.Add(name);
                    }
                }
            }

            for (int i = 0; i < valid.Count; i++)
            {
                if (seen.Add(valid[i]))
                {
                    result.Add(valid[i]);
                }
            }

            return result;
        }

        private static void RemoveWorkGiverFromPawnOrders(WorkGiverReassignmentData data, string workGiverDefName)
        {
            if (data?.PawnWorkGiverOrdering == null || workGiverDefName.NullOrEmpty())
            {
                return;
            }

            foreach (var pawnOrders in data.PawnWorkGiverOrdering.Values)
            {
                if (pawnOrders == null)
                {
                    continue;
                }

                foreach (var order in pawnOrders.Values)
                {
                    order?.Remove(workGiverDefName);
                }
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

        private static void NotifySubWorkDataChanged()
        {
            MarkMutationChanged(notifyDependents: true);
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
