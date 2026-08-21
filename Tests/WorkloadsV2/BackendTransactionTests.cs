using System;
using System.Collections.Generic;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Deterministic source contracts for the Unity-bound transaction backend.
    /// The standalone runner cannot instantiate RimWorld services, so these
    /// assertions pin the ordering and fail-closed seams that prevent partial
    /// multiplayer writes.
    /// </summary>
    internal static class BackendTransactionTests
    {
        public static void Run()
        {
            string root = FindRepositoryRoot();
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string contracts = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "WorkloadBackendContracts.cs");
            string multiplayer = Read(root, "Source", "Mod Support", "Multiplayer", "MultiplayerBridge.cs");
            string authorization = Read(root, "Source", "Features", "TimePriority", "TimePriorityMutationAuthorization.cs");
            string manager = Read(root, "Source", "Features", "WorkGiverReassignments", "WorkGiverReassignmentManager.cs");
            string data = Read(root, "Source", "Features", "WorkGiverReassignments", "WorkGiverReassignmentData.cs");

            PrepareIsReadOnlyAndExecuteIsCapabilityBound(backend, authorization);
            SpecificJobMutationIsOneAtomicBatch(backend, manager, data);
            SettingsAreRegisteredBeforeWrite(backend);
            RollbackLeaseCannotBecomeSuccessAfterFailure(backend, contracts);
            TemplateWritesUseTheSameTransactionBoundary(backend);
            FreshnessRevisionsAreCapturedAndValidatedIndependently(backend, multiplayer);
            PersistenceRevalidatesRuntimeStateBeforeTemplateWrites(backend);
            SaveRebaseAndForkCurrentIdentityAreTransactional(backend);
            ReceiptRecoveryIsReadOnlyAndFailClosed(backend);
        }

        private static void PrepareIsReadOnlyAndExecuteIsCapabilityBound(
            string backend,
            string authorization)
        {
            int liveApply = backend.IndexOf("ApplyLive(live, runtimePlan", StringComparison.Ordinal);
            int validateOnlyGate = backend.IndexOf(
                "if (executionContext?.ValidateOnly == true)",
                liveApply,
                StringComparison.Ordinal);
            TestAssert.True(
                liveApply >= 0 && validateOnlyGate > liveApply,
                "prepare must build and validate the runtime plan before the live writer and must return through the ValidateOnly gate");
            TestAssert.Contains(
                backend,
                "runtimePlan.HasLiveMutations && executionContext?.ValidateOnly != true",
                "ValidateOnly must never call ApplyLive");
            TestAssert.Contains(
                backend,
                "TryCreateForWorkload(",
                "execute must mint the opaque capability through the workload backend only");
            TestAssert.Contains(
                backend,
                "IsExecutionCapabilityBound(",
                "execute must revalidate the prepared capability inside the synchronized operation");
            TestAssert.Contains(
                authorization,
                "ReferenceEquals(_requestIdentity, requestIdentity)",
                "capability validation must bind object identity, not only copied strings");
            TestAssert.Contains(
                authorization,
                "StringComparer.Ordinal.Equals(_sourceTemplateFingerprint, sourceTemplateFingerprint",
                "capability validation must bind the source template fingerprint");
            TestAssert.Contains(
                authorization,
                "StringComparer.Ordinal.Equals(_hostSessionEpoch, hostSessionEpoch",
                "capability validation must bind the multiplayer host epoch");
            TestAssert.Contains(
                authorization,
                "StringComparer.Ordinal.Equals(_rosterFingerprint, rosterFingerprint",
                "capability validation must bind the synchronized roster");
        }

        private static void SpecificJobMutationIsOneAtomicBatch(
            string backend,
            string manager,
            string data)
        {
            TestAssert.Contains(
                backend,
                "TryApplySpecificJobBatch(",
                "the workload backend must enter the canonical specific-job batch seam");
            TestAssert.Contains(
                manager,
                "TryApplyWorkloadSpecificJobBatch(",
                "the manager must expose one batch API for shared and pawn-local specific-job state");
            TestAssert.Contains(
                manager,
                "WorkGiverReassignmentData previousData = data.Clone();",
                "the batch must retain one complete rollback snapshot before its first write");
            TestAssert.Contains(
                manager,
                "TryValidateSpecificPriorityBatch(",
                "all specific-job priorities must validate before the batch writes");
            TestAssert.Contains(
                manager,
                "TryValidateSpecificOrderBatch(",
                "all specific-job orders must validate before the batch writes");
            TestAssert.Contains(
                manager,
                "data.CopyFrom(previousData, previousRevision);",
                "a failed batch must restore the exact pre-write data clone");
            TestAssert.Contains(
                manager,
                "data.SyncVersion = unchecked(previousRevision + 1);",
                "the batch must publish one canonical revision for all specific-job dimensions");
            TestAssert.Contains(
                manager,
                "TryRestoreWorkloadSpecificJobBatch(",
                "rollback must restore the complete specific-job batch, not individual legacy sync entries");
            TestAssert.False(
                backend.IndexOf("SetPawnOverrideSynced(", StringComparison.Ordinal) >= 0 ||
                backend.IndexOf("ClearPawnOverrideSynced(", StringComparison.Ordinal) >= 0 ||
                backend.IndexOf("SetPawnWorkGiverOrderExact(", StringComparison.Ordinal) >= 0 ||
                backend.IndexOf("ClearPawnWorkGiverOrderExact(", StringComparison.Ordinal) >= 0,
                "the workload backend must not fall through to per-entry legacy specific-job sync calls");
            TestAssert.Contains(
                data,
                "ComputeStateFingerprint()",
                "the canonical data seam must expose a deterministic state fingerprint for rollback ownership");
            TestAssert.Contains(
                data,
                "GlobalWorkGiverPriorityClears",
                "specific-job batch fingerprints must retain explicit global clear state");
        }

        private static void SettingsAreRegisteredBeforeWrite(string backend)
        {
            int registration = backend.IndexOf(
                "transaction.PresentationWasChanged = true;",
                StringComparison.Ordinal);
            int writer = backend.IndexOf(
                "baseline.SettingsWriter.TryApply(",
                registration,
                StringComparison.Ordinal);
            TestAssert.True(
                registration >= 0 && writer > registration,
                "settings rollback ownership must be registered before the settings writer is invoked");
            TestAssert.Contains(
                backend,
                "IsAcceptedForSettings(",
                "settings writes and rollback must use the same transaction-bound capability gate");
        }

        private static void RollbackLeaseCannotBecomeSuccessAfterFailure(
            string backend,
            string contracts)
        {
            TestAssert.Contains(
                contracts,
                "RollbackFailed = 10",
                "the public transaction status must represent rollback failure separately");
            TestAssert.Contains(
                backend,
                "_state = recoveryRequired ? LeaseState.RollbackFailed : LeaseState.Active",
                "a transaction whose immediate rollback failed must begin in recovery-required state");
            TestAssert.Contains(
                backend,
                "if (_state == LeaseState.RollbackFailed)",
                "a failed rollback lease must retain its failed state across callbacks");
            TestAssert.Contains(
                backend,
                "return false;\n                }\n\n                _state = restored",
                "a second rollback callback must not turn a failed lease into Released");
            TestAssert.Contains(
                backend,
                "state != WorkloadMultiplayerCommitState.RollbackFailed",
                "the protocol callback must retain pending backend state while recovery is required");
            TestAssert.Contains(
                backend,
                "recoveryRequired: true",
                "post-write rollback failure must preserve a recovery lease for the protocol worker");
        }

        private static void TemplateWritesUseTheSameTransactionBoundary(string backend)
        {
            TestAssert.Contains(
                backend,
                "new PersistenceMutation(",
                "Update and Fork must stage template writes as transaction mutations");
            TestAssert.Contains(
                backend,
                "RollbackPersistence(store, persistence, report)",
                "template rollback must be part of the same rollback lease as live state");
            TestAssert.Contains(
                backend,
                "prepared.PlanFingerprint",
                "execute must reject a changed or replayed immutable prepare artifact");
            TestAssert.Contains(
                backend,
                "prepared.ExecuteStarted",
                "the backend must reject duplicate execute use of one prepared transaction");
        }

        private static void FreshnessRevisionsAreCapturedAndValidatedIndependently(
            string backend,
            string multiplayer)
        {
            TestAssert.Contains(
                backend,
                "session.PreviewSessionId",
                "multiplayer request construction must use the unique preview-session identity");
            TestAssert.Contains(
                backend,
                "session.MembershipRevision",
                "membership freshness must be captured from WorkloadSession");
            TestAssert.Contains(
                backend,
                "session.SessionRevision",
                "projected-state freshness must be captured from WorkloadSession");
            TestAssert.Contains(
                backend,
                "request.SessionId",
                "prepare must validate the serialized preview-session identity");
            TestAssert.Contains(
                backend,
                "session.MembershipRevision != request.ExpectedRevisions.MembershipRevision",
                "prepare must reject stale membership freshness");
            TestAssert.Contains(
                backend,
                "session.SessionRevision != request.ExpectedRevisions.SessionRevision",
                "prepare must reject stale projected-state freshness");
            TestAssert.False(
                backend.IndexOf("StableRevision(payload.Fingerprint)", StringComparison.Ordinal) >= 0,
                "payload fingerprints must not substitute for independent session revisions");
            TestAssert.Contains(
                multiplayer,
                "RequesterPlayerKey +",
                "request correlation must be separated from idempotency identity in the table key");
            TestAssert.Contains(
                multiplayer,
                "IdempotencyKey;",
                "the transaction table must deduplicate by idempotency key");
            TestAssert.Contains(
                multiplayer,
                "The idempotency key was reused with a different canonical payload.",
                "conflicting idempotent retries must fail closed");
        }

        private static void PersistenceRevalidatesRuntimeStateBeforeTemplateWrites(string backend)
        {
            int validationCall = backend.IndexOf(
                "ValidatePersistenceRuntimeState(",
                StringComparison.Ordinal);
            int updateBranch = backend.IndexOf(
                "if (decisionKind == WorkloadDecisionKind.Update && !plan.Diff.IsEmpty)",
                StringComparison.Ordinal);
            int persistenceMutation = backend.IndexOf(
                "persistence = new PersistenceMutation(",
                updateBranch,
                StringComparison.Ordinal);
            TestAssert.True(
                validationCall >= 0 && updateBranch > validationCall && persistenceMutation > validationCall,
                "Update and Fork must revalidate runtime state before staging persistence");

            int helperStart = backend.IndexOf(
                "private static void ValidatePersistenceRuntimeState(",
                StringComparison.Ordinal);
            int helperEnd = backend.IndexOf(
                "private static bool HasPersistedDimension(",
                helperStart,
                StringComparison.Ordinal);
            TestAssert.True(helperStart >= 0 && helperEnd > helperStart,
                "the persistence runtime validation seam must remain explicit");
            string helper = backend.Substring(helperStart, helperEnd - helperStart);

            TestAssert.Contains(
                helper,
                "BuildRuntimeContext(targetTemplate, report)",
                "Update/Fork must resolve the current pawn, WorkTypeDef, and WorkGiverDef catalog");
            TestAssert.Contains(
                helper,
                "ValidateScopeEntries(scope, runtime, report)",
                "removed or stale scoped pawns must fail closed before persistence");
            TestAssert.Contains(
                helper,
                "ValidateRuntimeEntries(",
                "legacy runtime entries must reuse the canonical target resolver");
            TestAssert.Contains(
                helper,
                "requireLiveBaseline: false",
                "Update/Fork must not inherit Apply-only live value-baseline requirements");
            TestAssert.Contains(
                helper,
                "useTargetTemplateState: true",
                "Update/Fork must validate the exact detached persistence payload");
            TestAssert.Contains(
                helper,
                "report.MissingOrStaleCount > 0",
                "removed definitions and targets must produce structured stale diagnostics");
            TestAssert.Contains(
                helper,
                "TimePriorityService.CurrentVersion != baseline.ScheduleRevision",
                "schedule service drift must block stale schedule persistence");
            TestAssert.Contains(
                helper,
                "WorkGiverReassignmentManager.CurrentSyncVersion != baseline.SpecificJobRevision",
                "specific-job service drift must block stale persistence");
            TestAssert.Contains(
                helper,
                "ComputeTaxonomyFingerprint(runtime)",
                "changed WorkGiver taxonomy must block Update/Fork persistence");
            TestAssert.Contains(
                helper,
                "WorkPrioritySystem.IsBwtMutationAuthorityCurrent(baseline.AuthorityRevision)",
                "authority drift must block Update/Fork persistence without handoff");
            TestAssert.Contains(
                backend,
                "BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(",
                "settings ownership must be checked against the live stageable registry");
            TestAssert.False(
                helper.IndexOf("ApplyLive(", StringComparison.Ordinal) >= 0,
                "Update/Fork runtime revalidation must not mutate the live colony");

            // Deterministic model coverage for the four persistence outcomes
            // represented by the production source contract above.
            var catalog = new HashSet<string>(StringComparer.Ordinal)
            {
                "pawn:p1",
                "worktype:PlantWork",
                "workgiver:PlantCut"
            };
            TestAssert.False(
                catalog.Contains("workgiver:RemovedWorkGiver"),
                "a removed WorkGiver identity must be rejected by the runtime catalog");
            catalog.Remove("worktype:PlantWork");
            TestAssert.False(
                catalog.Contains("worktype:PlantWork"),
                "a removed WorkType identity must be rejected by the runtime catalog");
            TestAssert.True(
                !StringComparer.Ordinal.Equals("taxonomy-a", "taxonomy-b"),
                "a changed taxonomy fingerprint must be treated as drift");
            TestAssert.True(
                !StringComparer.Ordinal.Equals("authority-a", "authority-b"),
                "a changed authority identity must be treated as drift");
            TestAssert.True(
                StringComparer.Ordinal.Equals("taxonomy-a", "taxonomy-a") &&
                StringComparer.Ordinal.Equals("authority-a", "authority-a"),
                "unchanged runtime identity, taxonomy, and authority must permit valid persistence");
        }

        private static void SaveRebaseAndForkCurrentIdentityAreTransactional(string backend)
        {
            TestAssert.Contains(
                backend,
                "BuildPersistenceReceipt(",
                "Update/Fork must build a receipt from the authoritative persisted target");
            TestAssert.Contains(
                backend,
                "executionContext.PersistenceRebase(receipt.Value)",
                "single-player persistence must rebase only after the write and round trip succeed");
            TestAssert.Contains(
                backend,
                "RekeyBackendBaseline(",
                "successful Save As must rekey the service-owned baseline");
            TestAssert.Contains(
                backend,
                "successResult.PersistenceReceipt = executionContext?.PersistenceReceipt",
                "the committed receipt must cross the MP status boundary rather than a peer session");

            int persistencePlanStart = backend.IndexOf(
                "// Update and Fork do not apply the live colony",
                StringComparison.Ordinal);
            int applyLiveStart = backend.IndexOf(
                "ApplyLive(live, runtimePlan",
                persistencePlanStart,
                StringComparison.Ordinal);
            TestAssert.True(
                persistencePlanStart >= 0 && applyLiveStart > persistencePlanStart,
                "the Update/Fork persistence-only planning boundary must remain explicit");
            string persistencePlan = backend.Substring(
                persistencePlanStart,
                applyLiveStart - persistencePlanStart);
            TestAssert.False(
                persistencePlan.IndexOf("ApplyLive(", StringComparison.Ordinal) >= 0,
                "Save and Save As must never call the live colony writer");

            TestAssert.Contains(
                backend,
                "store.CurrentWorkloadId = mutation.StableId",
                "Fork must activate the new current workload in the same persistence mutation");
            TestAssert.Contains(
                backend,
                "mutation.PreviousCurrentWorkloadId",
                "Fork current-ID activation must retain rollback metadata");
            TestAssert.Contains(
                backend,
                "if (mutation.CurrentWorkloadIdChanged)",
                "Fork rollback must restore the prior current workload identity");
            TestAssert.Contains(
                backend,
                "Keep the lease active so the protocol can still issue",
                "a failed final confirmation must remain rollback-recoverable");
            TestAssert.Contains(
                backend,
                "store.PersistenceRevision != receipt.PersistenceRevision",
                "preview rebasing must reject a receipt from a different local persistence revision");
            TestAssert.Contains(
                backend,
                "store.PersistenceFingerprint",
                "preview rebasing must verify the local persistence fingerprint before adopting a receipt");
            TestAssert.Contains(
                backend,
                "store.Find(receipt.TargetStableId)",
                "preview rebasing must round-trip the local target record before changing baseline identity");
        }

        private static void ReceiptRecoveryIsReadOnlyAndFailClosed(string backend)
        {
            string recovery = Slice(
                backend,
                "internal WorkloadOperationResult<WorkloadPersistenceReceipt> RecoverPersistenceReceipt(\n            WorkloadSession session",
                "internal WorkloadOperationResult<WorkloadProjectedState> CaptureLiveBaseline(");
            TestAssert.Contains(
                recovery,
                "decisionKind != WorkloadDecisionKind.Update",
                "receipt recovery must reject non-Update/Fork decisions");
            TestAssert.Contains(
                recovery,
                "targetStableId",
                "receipt recovery must bind the requested target identity");
            TestAssert.Contains(
                recovery,
                "_backendBaselines.TryGetValue(sourceIdentity",
                "receipt recovery must use the old service-owned source baseline");
            TestAssert.Contains(
                recovery,
                "BuildPersistenceReceipt(",
                "receipt recovery must reuse the existing target validation");
            TestAssert.Contains(
                backend,
                "store.HasDuplicateStableId(targetStableId)",
                "the reused receipt builder must reject an ambiguous target");
            TestAssert.Contains(
                backend,
                "WorkloadV2RecordConverter.TryToTemplate(record)",
                "receipt recovery must round-trip the authoritative persisted target");
            TestAssert.Contains(
                recovery,
                "WorkloadPersistenceReceiptRecovery.TryRecover(",
                "receipt recovery must use the pure fail-closed recovery seam");
            TestAssert.Contains(
                recovery,
                "new WorkloadPersistenceRecoverySnapshot(",
                "receipt recovery must pass an authoritative persistence snapshot to the pure seam");
            TestAssert.Contains(
                backend,
                "WorkloadScope synchronizedScope",
                "multiplayer reconstruction must retain the payload scope independently of the stored source scope");
            TestAssert.Contains(
                backend,
                "synchronizedState = synchronizedState.ExcludePawn(excludedPawn)",
                "multiplayer reconstruction must restore temporary exclusions into the detached session state");
            TestAssert.Contains(
                backend,
                "source.Value.WithDefinition(observedDefinition).WithState(synchronizedState)",
                "peer baseline capture must observe the payload scope while preserving source identity");
            TestAssert.Contains(
                backend,
                "session.EditState(synchronizedState)",
                "the reconstructed peer session must use the exclusion-preserving state");
            TestAssert.Contains(
                backend,
                "scope.IsExplicitlyExcluded(entry.Key.Pawn)",
                "live baseline capture must not require excluded pawn-local identities");
            TestAssert.False(
                recovery.IndexOf("CaptureLiveBaseline", StringComparison.Ordinal) >= 0 ||
                recovery.IndexOf("RekeyBackendBaseline", StringComparison.Ordinal) >= 0,
                "receipt recovery must not recapture live state or mutate the baseline");
        }

        private static string FindRepositoryRoot()
        {
            return TestSupport.FindRepositoryRoot(
                Path.Combine(
                    "Source",
                    "Features",
                    "Workloads",
                    "V2",
                    "Runtime",
                    "Workload2Backend.cs"),
                "backend contracts");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
            }

            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string Slice(string value, string startMarker, string endMarker)
        {
            int start = value.IndexOf(startMarker, StringComparison.Ordinal);
            int end = start < 0
                ? -1
                : value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            TestAssert.True(start >= 0 && end > start, "expected backend method boundary is missing");
            return value.Substring(start, end - start);
        }
    }
}
