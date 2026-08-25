using System;
using System.IO;
using Better_Work_Tab.Features.Workloads.V2.Runtime;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Deterministic coverage for the gateway's immediate MP lifecycle result.
    /// The Unity-bound controller is source-checked here, while the structured
    /// status contract is exercised directly by this standalone test assembly.
    /// </summary>
    internal static class GatewayMultiplayerTests
    {
        public static void Run()
        {
            AcceptedStatesAreNotLifecycleRejections();

            string root = FindRepositoryRoot();
            string gateway = Read(root, "Source", "UI", "Workloads", "WorkloadGateway.cs");
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string actionability = Read(root, "Source", "Features", "Application", "WorkTabActionability.cs");
            string priorityGateway = Read(root, "Source", "UI", "WorkGrid", "Commands", "WorkPriorityCommandGateway.cs");
            string priorityInput = Read(root, "Source", "UI", "WorkGrid", "Interaction", "WorkTabPriorityInputHandler.cs");
            string priorityPatch = Read(root, "Source", "Features", "Patches", "Patch_WorkPriority_DoCell_Unified.cs");
            string prioritySnapshot = Read(root, "Source", "UI", "WorkGrid", "Snapshots", "WorkGridSnapshotProvider.cs");
            string fluffySchedule = Read(root, "Source", "Features", "TimePriority", "FluffyTimeScheduleAssigner.cs");
            GatewayReturnsAcceptanceAndDefersTerminalHandling(gateway);
            TerminalFailuresKeepThePreviewRetryable(gateway);
            PendingDuplicatesUseCanonicalCorrelation(backend);
            MismatchedTerminalReplaysRemainConflicts(backend);
            RollbackFailureKeepsThePreviewLocked(gateway);
            SaveForkRebaseOccursOnlyAtFinalConfirmation(gateway, backend);
            NullResultSuccessUsesAuthoritativeReceiptRecovery(gateway, backend);
            ReceiptRecoveryFailureKeepsThePreviewBlocked(gateway, backend);
            ParentPriorityInputUsesOneCapabilityGate(
                actionability,
                priorityGateway,
                priorityInput,
                priorityPatch,
                prioritySnapshot,
                fluffySchedule);
        }

        private static void ParentPriorityInputUsesOneCapabilityGate(
            string actionability,
            string priorityGateway,
            string priorityInput,
            string priorityPatch,
            string prioritySnapshot,
            string fluffySchedule)
        {
            TestAssert.Contains(
                actionability,
                "pawn.IsWorkTypeDisabledByAge(workType, out _)",
                "parent-priority input must explicitly reject age-disabled cells");
            TestAssert.Contains(
                actionability,
                "CanApplyAnyWorkGiver(pawn, workType);",
                "parent-priority input must defer raw capability to one helper");
            TestAssert.Contains(
                actionability,
                "workType.workGiversByPriority",
                "parent-priority input must inspect the work type's own giver capabilities");
            TestAssert.Contains(
                actionability,
                "pawn.health.capacities.CapableOf(capacities[i])",
                "parent-priority input must reject cells incapable across every work giver");
            TestAssert.Contains(
                priorityGateway,
                "WorkTabActionability.CanApplyParent(pawn, workType)",
                "the UI command gateway must delegate parent actionability to the application policy");
            TestAssert.Contains(
                priorityGateway,
                "if (!CanHandleParentPriorityInput(pawn, workType) ||",
                "preview parent-priority writes must use the canonical capability gate");

            int rootGate = priorityInput.IndexOf(
                "!WorkPriorityCommandGateway.CanHandleParentPriorityInput(row.Pawn, workType)",
                StringComparison.Ordinal);
            int rootMembership = priorityInput.IndexOf(
                "EnsurePreviewMembershipForMutation(view.Preview, row.Pawn, workType, evt)",
                StringComparison.Ordinal);
            TestAssert.True(
                rootGate >= 0 && rootMembership > rootGate,
                "root parent-priority input must reject an unavailable cell before preview membership can change");
            TestAssert.Contains(
                priorityInput,
                "!WorkPriorityCommandGateway.CanHandleSpecificJobInput(\n                        row.Pawn,\n                        parentWorkType,\n                        workGiver?.def)",
                "sub-work input must use the exact WorkGiver capability gate");
            TestAssert.Contains(
                priorityGateway,
                "!CanHandleSpecificJobInput(TimePriorityService.FindPawn(pawnId), workType, workGiver)",
                "direct pawn-specific job commands must fail closed through the same exact capability gate");
            TestAssert.Contains(
                priorityPatch,
                "!WorkPriorityCommandGateway.CanHandleParentPriorityInput(pawn, workType)",
                "the root priority handler must not bypass the canonical capability gate");
            TestAssert.Contains(
                priorityPatch,
                "!WorkTabActionability.CanApplyAnyWorkGiver(p, work)",
                "the patch's visual incapability cache must use the canonical capability computation");
            TestAssert.Contains(
                prioritySnapshot,
                "!WorkTabActionability.CanApplyAnyWorkGiver(pawn, visualWorkType)",
                "snapshot cell presentation must use the canonical capability computation");
            TestAssert.False(
                prioritySnapshot.IndexOf("private static bool IsIncapable(", StringComparison.Ordinal) >= 0,
                "snapshot presentation must not retain a second parent-work incapability loop");
            TestAssert.Contains(
                fluffySchedule,
                "!WorkPriorityCommandGateway.CanHandleParentPriorityInput(pawn, workType)",
                "Fluffy full-day and hourly parent writes must use the canonical actionability gate");
            TestAssert.Contains(
                fluffySchedule,
                "!WorkPriorityCommandGateway.CanHandleSpecificJobInput(",
                "Fluffy pawn-specific writes must use the exact WorkGiver actionability gate");
            TestAssert.Contains(
                fluffySchedule,
                "pawnId != TimePriorityTarget.GlobalPawnId && pawn == null",
                "Fluffy pawn-specific writes must not reinterpret a missing pawn as a global target");
        }

        private static void AcceptedStatesAreNotLifecycleRejections()
        {
            WorkloadMultiplayerCommitState[] acceptedStates =
            {
                WorkloadMultiplayerCommitState.Pending,
                WorkloadMultiplayerCommitState.Prepared,
                WorkloadMultiplayerCommitState.ExecutedAwaitingConfirmation,
                WorkloadMultiplayerCommitState.Succeeded
            };
            for (int i = 0; i < acceptedStates.Length; i++)
            {
                WorkloadMultiplayerCommitStatus status = Status(acceptedStates[i]);
                TestAssert.True(
                    status.IsAccepted,
                    "accepted MP state must be treated as a successful lifecycle action: " +
                    acceptedStates[i]);
            }

            WorkloadMultiplayerCommitState[] rejectedStates =
            {
                WorkloadMultiplayerCommitState.None,
                WorkloadMultiplayerCommitState.Rejected,
                WorkloadMultiplayerCommitState.Aborted,
                WorkloadMultiplayerCommitState.TimedOut,
                WorkloadMultiplayerCommitState.Failed,
                WorkloadMultiplayerCommitState.RolledBack,
                WorkloadMultiplayerCommitState.RollbackFailed
            };
            for (int i = 0; i < rejectedStates.Length; i++)
            {
                WorkloadMultiplayerCommitStatus status = Status(rejectedStates[i]);
                TestAssert.False(
                    status.IsAccepted,
                    "rejected MP state must keep the preview open: " + rejectedStates[i]);
            }
        }

        private static void GatewayReturnsAcceptanceAndDefersTerminalHandling(string gateway)
        {
            string method = Slice(
                gateway,
                "private bool BeginMultiplayerPreviewCommit(",
                "private string CurrentMultiplayerPayloadFingerprint(");
            TestAssert.Contains(
                method,
                "bool accepted = status.IsAccepted;",
                "the gateway boolean must use the structured MP acceptance result");
            TestAssert.Contains(
                method,
                "if (status.IsTerminal)",
                "terminal MP statuses must still be deferred to the next UI frame");
            TestAssert.Contains(
                method,
                "OnMultiplayerCommitStatusPublished(status);",
                "synchronous terminal MP statuses must enter the queued completion path");
            TestAssert.Contains(
                method,
                "if (!accepted)",
                "genuine MP rejection/failure must remain a rejected lifecycle action");
            TestAssert.Contains(
                method,
                "SetMessage(MultiplayerStatusExplanation);",
                "genuine MP rejection/failure must report its structured status message");
            TestAssert.Contains(
                method,
                "return accepted;",
                "the queued lifecycle result must report acceptance rather than terminal completion");
            TestAssert.False(
                method.IndexOf(
                    "status.State >= WorkloadMultiplayerCommitState.Succeeded",
                    StringComparison.Ordinal) >= 0,
                "MP lifecycle classification must not depend on enum ordering");
        }

        private static void TerminalFailuresKeepThePreviewRetryable(string gateway)
        {
            TestAssert.Contains(
                gateway,
                "private void PrepareMultiplayerRetry()",
                "terminal synchronized failures must reset only the operation attempt, not the preview session");
            TestAssert.Contains(
                gateway,
                "PrepareMultiplayerRetry();",
                "the unchanged draft must become retryable after a coherent terminal failure");
            TestAssert.Contains(
                gateway,
                "case WorkloadMultiplayerCommitState.Rejected:",
                "prepare rejection must remain retryable");
            TestAssert.Contains(
                gateway,
                "case WorkloadMultiplayerCommitState.Aborted:",
                "abort must remain retryable after the protocol is terminal");
            TestAssert.Contains(
                gateway,
                "case WorkloadMultiplayerCommitState.TimedOut:",
                "timeout must remain retryable after the protocol is terminal");
            TestAssert.Contains(
                gateway,
                "case WorkloadMultiplayerCommitState.RolledBack:",
                "completed rollback must leave the draft retryable");
            TestAssert.Contains(
                gateway,
                "_multiplayerIdempotencyKey = Guid.NewGuid().ToString(\"N\");",
                "each new logical retry must receive a fresh idempotency key");
            TestAssert.Contains(
                gateway,
                "_multiplayerIdempotencyKey);",
                "the UI must pass its idempotency key through the backend boundary");
            TestAssert.False(
                gateway.IndexOf(
                    "Change the preview before trying another request.",
                    StringComparison.Ordinal) >= 0,
                "a terminal synchronized failure must not require an unrelated draft edit before retry");
            TestAssert.Contains(
                gateway,
                "leaving the preview open",
                "terminal rejection must retain the existing preview session");
        }

        private static void PendingDuplicatesUseCanonicalCorrelation(string backend)
        {
            TestAssert.Contains(
                backend,
                "admission.RegisteredRequest?.RequestId",
                "pending idempotent retries must correlate status to the registered request ID");
            TestAssert.Contains(
                backend,
                "WorkloadMultiplayerCommitState.Pending",
                "pending idempotent retries must retain structured pending state");
        }

        private static void MismatchedTerminalReplaysRemainConflicts(string backend)
        {
            string begin = Slice(
                backend,
                "internal WorkloadMultiplayerCommitStatus Begin(",
                "public void OnRequestAccepted(");
            AssertMismatchPrecedesTerminalReplay(begin, "the initiating backend path");

            string rejected = Slice(
                backend,
                "public void OnAdmissionRejected(",
                "public void OnPrepareRequested(");
            AssertMismatchPrecedesTerminalReplay(rejected, "the incoming callback path");
        }

        private static void AssertMismatchPrecedesTerminalReplay(string method, string label)
        {
            int mismatch = method.IndexOf(
                "WorkloadTransactionAdmissionCode.MismatchedDuplicate",
                StringComparison.Ordinal);
            int terminal = method.IndexOf(
                "TerminalResult != null",
                StringComparison.Ordinal);
            TestAssert.True(
                mismatch >= 0 && terminal > mismatch,
                label + " must reject mismatched payloads before considering retained terminal results");
            TestAssert.Contains(
                method,
                "WorkloadDiagnosticCode.PersistenceConflict",
                label + " must expose a structured persistence conflict");
            TestAssert.Contains(
                method,
                "admission.Request?.RequestId",
                label + " must correlate a mismatch to the conflicting caller request");
        }

        private static void RollbackFailureKeepsThePreviewLocked(string gateway)
        {
            TestAssert.Contains(
                gateway,
                "_multiplayerCommitState == WorkloadMultiplayerCommitState.RollbackFailed",
                "rollback failure must remain an in-flight recovery lock");
            TestAssert.Contains(
                gateway,
                "case WorkloadMultiplayerCommitState.RollbackFailed:",
                "rollback failure must be handled as a structured recovery status");
            TestAssert.Contains(
                gateway,
                "T(\"BWT_Workload_MultiplayerRecovery\")",
                "the UI must explain why a retained rollback lease blocks new commits");
        }

        private static void SaveForkRebaseOccursOnlyAtFinalConfirmation(
            string gateway,
            string backend)
        {
            TestAssert.Contains(
                backend,
                "ConfirmMultiplayerCommit(pending.Lease)",
                "the synchronized lease must be confirmed before the UI rebase path");
            TestAssert.Contains(
                backend,
                "pending.Backend.AbortMultiplayerCommit(pending.Lease)",
                "a failed final rebase must issue the protocol rollback path");
            TestAssert.Contains(
                backend,
                "Keep the lease active so the protocol can still issue",
                "a rejected final confirmation must leave the rollback lease recoverable");
            TestAssert.Contains(
                gateway,
                "status.Result?.PersistenceReceipt != null",
                "a post-confirmation UI rebase failure must enter a recoverable preview block");
            TestAssert.Contains(
                gateway,
                "_previewRecoveryBlocked = true;",
                "a post-confirmation UI rebase failure must block Apply against the old source");

            string terminal = Slice(
                backend,
                "public void OnTerminal(",
                "private bool TryPrepare(");
            int confirmation = terminal.IndexOf(
                "ConfirmMultiplayerCommit(",
                StringComparison.Ordinal);
            int rebase = terminal.IndexOf(
                "RebasePreviewAfterPersistence(",
                StringComparison.Ordinal);
            TestAssert.True(
                confirmation >= 0 && rebase < 0,
                "transport callbacks must not mutate the UI backend or copy a temporary peer session");

            string completion = Slice(
                gateway,
                "private void CompleteMultiplayerCommit(",
                "private void ClearMultiplayerAttempt()");
            TestAssert.Contains(
                completion,
                "if (_multiplayerDecision != WorkloadDecisionKind.Apply)",
                "MP Save/Fork completion must be distinct from Apply completion");
            TestAssert.Contains(
                completion,
                "WorkloadGateway.RebaseV2PreviewAfterPersistence(",
                "MP Save/Fork completion must rebase the UI backend from the committed receipt");
            int completionRebase = completion.IndexOf(
                "WorkloadGateway.RebaseV2PreviewAfterPersistence(",
                StringComparison.Ordinal);
            int completionAdopt = completion.IndexOf(
                "WorkloadGateway.AdoptV2PreviewSession()",
                completionRebase,
                StringComparison.Ordinal);
            TestAssert.True(
                completionRebase >= 0 && completionAdopt > completionRebase,
                "MP Save/Fork must adopt the rebased backend session after final confirmation");
            TestAssert.Contains(
                completion,
                "ClearMultiplayerAttempt();",
                "the MP attempt must clear only after successful preview adoption");
            int saveBranchEnd = completion.IndexOf(
                "return;\n            }\n\n            _multiplayerTerminalHandled = true;",
                StringComparison.Ordinal);
            TestAssert.True(
                saveBranchEnd >= 0,
                "MP Save/Fork completion must leave the Apply close branch after adoption");
            string saveBranch = completion.Substring(0, saveBranchEnd);
            TestAssert.False(
                saveBranch.IndexOf("EndV2Preview()", StringComparison.Ordinal) >= 0 ||
                saveBranch.IndexOf("ClearLocalSession()", StringComparison.Ordinal) >= 0,
                "successful MP Save/Fork must keep the preview open");
        }

        private static void NullResultSuccessUsesAuthoritativeReceiptRecovery(
            string gateway,
            string backend)
        {
            string completion = Slice(
                gateway,
                "private void CompleteMultiplayerCommit(",
                "private void ClearMultiplayerAttempt()");
            int receipt = completion.IndexOf(
                "status?.Result?.PersistenceReceipt",
                StringComparison.Ordinal);
            int rebase = completion.IndexOf(
                "WorkloadGateway.RebaseV2PreviewAfterPersistence(",
                StringComparison.Ordinal);
            TestAssert.True(
                receipt >= 0 && rebase >= 0,
                "MP Save/Fork completion must pass the status receipt into the rebase seam");
            TestAssert.Contains(
                completion,
                "_multiplayerDecision",
                "receipt recovery must use the confirmed Save/Fork decision");
            TestAssert.Contains(
                gateway,
                "modern.RecoverPersistenceReceipt(\n                        decisionKind,\n                        targetStableId,\n                        forkLabel)",
                "a terminal success without a receipt must ask the authoritative UI backend to recover it");
            TestAssert.Contains(
                completion,
                "_multiplayerForkLabel",
                "Save As receipt recovery must carry the requested fork label through completion");
            TestAssert.Contains(
                backend,
                "pending?.Result",
                "a locally completed terminal status must preserve its direct commit result when available");
        }

        private static void ReceiptRecoveryFailureKeepsThePreviewBlocked(
            string gateway,
            string backend)
        {
            string completion = Slice(
                gateway,
                "private void CompleteMultiplayerCommit(",
                "private void ClearMultiplayerAttempt()");
            TestAssert.Contains(
                completion,
                "_previewRecoveryBlocked = true;",
                "receipt recovery or rebase failure must retain the recoverable preview block");
            TestAssert.Contains(
                completion,
                "return;",
                "a failed receipt recovery must stop before preview adoption or clearing");

            string recovery = Slice(
                backend,
                "internal WorkloadOperationResult<WorkloadPersistenceReceipt> RecoverPersistenceReceipt(\n            WorkloadSession session",
                "internal WorkloadOperationResult<WorkloadProjectedState> CaptureLiveBaseline(");
            TestAssert.Contains(
                recovery,
                "BuildPersistenceReceipt(",
                "receipt recovery must reuse the authoritative receipt builder");
            TestAssert.Contains(
                recovery,
                "baseline.HasPersistenceBaseline",
                "receipt recovery must fail closed without the old service-owned baseline");
            TestAssert.Contains(
                recovery,
                "store.IsReadOnlyDiagnostic",
                "receipt recovery must reject non-authoritative persistence metadata");
            TestAssert.Contains(
                recovery,
                "baseline.PersistenceRevision",
                "receipt recovery must pass the captured persistence revision to the pure seam");
            TestAssert.Contains(
                recovery,
                "WorkloadPersistenceReceiptRecovery.TryRecover(",
                "receipt recovery must retain fail-closed revision and identity validation in production code");
            TestAssert.False(
                recovery.IndexOf("CaptureLiveBaseline", StringComparison.Ordinal) >= 0 ||
                recovery.IndexOf("RebasePreviewAfterPersistence", StringComparison.Ordinal) >= 0 ||
                recovery.IndexOf("RekeyBackendBaseline", StringComparison.Ordinal) >= 0,
                "receipt recovery must not recapture live state, rebase, or mutate the baseline");
        }

        private static WorkloadMultiplayerCommitStatus Status(
            WorkloadMultiplayerCommitState state)
        {
            return new WorkloadMultiplayerCommitStatus(
                "request",
                state,
                state == WorkloadMultiplayerCommitState.None
                    ? WorkloadDiagnosticCode.InvalidState
                    : WorkloadDiagnosticCode.None);
        }

        private static string FindRepositoryRoot()
        {
            return TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "UI", "Workloads", "WorkloadGateway.cs"),
                "gateway contracts");
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
            TestAssert.True(start >= 0 && end > start, "expected gateway method boundary is missing");
            return value.Substring(start, end - start);
        }
    }
}
