using System;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using Multiplayer.API;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class MpProtocolTests
    {
        public static void Run()
        {
            AllRosterSuccessRequiresEveryPrepareAndExecuteReport();
            MissingHostPrepareAcknowledgementStaysPending();
            ForgedSenderIsRejectedWithoutProgress();
            DuplicateTerminalReplayAndMismatchedDuplicateAreDistinct();
            PendingIdempotentReplayRetainsCanonicalTransaction();
            TerminalFailureReplaysUseTheIdempotencyContract();
            ConfirmationBarrierWaitsForEveryPeer();
            MultiPeerNoChangeCompletesWithoutRollback();
            MixedNoChangeAndMutationFailsClosed();
            FinalDeliveryRetriesWithoutRollbackAfterCommitDecision();
            DuplicateFinalConfirmationIsAcknowledgedIdempotently();
            TimeoutAndRosterChangeAbortCoherently();
            RollbackFailureRemainsTerminalAndReplayable();
            InvalidRostersDoNotPoisonTransactionCapacity();
            InvalidRevisionContextIsRejectedAtRequestConstruction();
            LegacyApiRegistrationFailsClosedWithoutPartialWorkers();
        }

        private static void AllRosterSuccessRequiresEveryPrepareAndExecuteReport()
        {
            var request = Request("success", "success-idempotency", "host", "host", "peer-a", "peer-b");
            var protocol = BeginHost(request, "host");

            var preparePeerA = Prepare(protocol, request, "peer-a", 2L);
            TestAssert.Equal(WorkloadTransactionTransitionAction.None, preparePeerA.Action,
                "one peer prepare report must not cross the all-roster barrier");
            var prepareHost = Prepare(protocol, request, "host", 3L);
            TestAssert.Equal(WorkloadTransactionTransitionAction.None, prepareHost.Action,
                "host plus one peer must remain pending while another peer is missing");
            var preparePeerB = Prepare(protocol, request, "peer-b", 4L);
            TestAssert.Equal(WorkloadTransactionTransitionAction.SendExecute, preparePeerB.Action,
                "the final all-roster prepare report must dispatch execute");

            var executeHost = Execute(protocol, request, "host", 5L, true, false);
            TestAssert.Equal(WorkloadTransactionTransitionAction.None, executeHost.Action,
                "host execution alone must remain inside the execute barrier");
            var executePeerA = Execute(protocol, request, "peer-a", 6L, true, false);
            TestAssert.Equal(WorkloadTransactionTransitionAction.None, executePeerA.Action,
                "host plus one peer execute report must remain pending");
            var executePeerB = Execute(protocol, request, "peer-b", 7L, true, false);
            TestAssert.Equal(WorkloadTransactionTransitionAction.SendConfirm, executePeerB.Action,
                "the final all-roster execute report must start confirmation");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, executePeerB.State.TerminalState,
                "entering confirmation must not clear the session as success");

            var confirmationPeerA = Confirm(protocol, request, "peer-a", 8L, true, false);
            TestAssert.Equal(WorkloadTransactionTransitionAction.None, confirmationPeerA.Action,
                "the first peer confirmation acknowledgement must remain pending");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, confirmationPeerA.State.TerminalState,
                "the host must wait for every peer confirmation acknowledgement");
            var confirmationPeerB = Confirm(protocol, request, "peer-b", 9L, true, false);
            TestAssert.Equal(WorkloadTransactionTransitionAction.SendFinalConfirmation, confirmationPeerB.Action,
                "the complete confirmation barrier must dispatch final confirmation");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, confirmationPeerB.State.TerminalState,
                "the ready barrier is not terminal until every peer reports final delivery");
            TestAssert.True(protocol.LastResult == null,
                "the host must not publish terminal success before final delivery");
            var deliveredPeerA = FinalDelivery(protocol, request, "peer-a", 10L);
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, deliveredPeerA.State.TerminalState,
                "one final delivery report cannot complete a multi-peer transaction");
            var deliveredPeerB = FinalDelivery(protocol, request, "peer-b", 11L);
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded, deliveredPeerB.State.TerminalState,
                "the host succeeds after every peer reports the final commit decision");
            TestAssert.NotNull(protocol.LastResult,
                "final-delivery success must retain an idempotent result");

            var peerProtocol = new WorkloadTransactionProtocol();
            var incoming = peerProtocol.AcceptIncoming(
                request, 0L, true, WorkloadTransactionAdmissionCode.Accepted, null, "host");
            TestAssert.True(incoming.Accepted, "an authenticated incoming request must be admitted");
            TestAssert.Equal(WorkloadTransactionPhase.Prepare, peerProtocol.CurrentState.Phase,
                "incoming peers must immediately enter local prepare");
            var executeControl = peerProtocol.RecordExecuteControl(
                request.RequestId, request.RequestFingerprint, true, "execute", string.Empty, 2L, true);
            TestAssert.Equal(WorkloadTransactionTransitionAction.ExecuteLocally, executeControl.Action,
                "authenticated host execute control must advance an incoming peer");
            var confirmationControl = peerProtocol.RecordConfirmationControl(
                request.RequestId, request.RequestFingerprint, true, "confirm", string.Empty,
                "confirm-report", 3L, true);
            TestAssert.Equal(WorkloadTransactionTransitionAction.ConfirmLocally, confirmationControl.Action,
                "confirmation control must request a peer acknowledgement without ending the session");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, confirmationControl.State.TerminalState,
                "a peer must retain its rollback lease until final confirmation");
            var finalConfirmation = peerProtocol.RecordFinalConfirmation(
                request.RequestId, request.RequestFingerprint, true, "confirmed", string.Empty, 4L, true);
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded, finalConfirmation.State.TerminalState,
                "an incoming peer succeeds only after authenticated final confirmation");
        }

        private static void MissingHostPrepareAcknowledgementStaysPending()
        {
            var request = Request(
                "missing-host-prepare", "missing-host-prepare-idempotency", "host", "host", "peer-a");
            var protocol = BeginHost(request, "host");
            var peerOnly = Prepare(protocol, request, "peer-a", 2L);

            TestAssert.Equal(WorkloadTransactionTransitionAction.None, peerOnly.Action,
                "a peer report cannot stand in for the host prepare report");
            TestAssert.Equal(WorkloadTransactionPhase.Prepare, peerOnly.State.Phase,
                "the transaction must remain in prepare while the host report is missing");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, peerOnly.State.TerminalState,
                "the missing-host prepare barrier must remain nonterminal");
        }

        private static void ForgedSenderIsRejectedWithoutProgress()
        {
            var request = Request("forged-sender", "forged-sender-idempotency", "host", "host", "peer-a");
            var protocol = BeginHost(request, "host");
            var rejected = protocol.RecordPrepareAcknowledgement(
                request.RequestId, request.RequestFingerprint, "peer-a", true, "forged", string.Empty,
                "prepare-report", 2L, false);

            TestAssert.Equal(WorkloadTransactionTransitionDisposition.Rejected, rejected.Disposition,
                "a report without authenticated runtime sender binding must be rejected");
            TestAssert.Equal(0, protocol.CurrentState.PreparedParticipants.Count,
                "a forged report must not count toward the prepare barrier");
            TestAssert.Equal(WorkloadTransactionPhase.Prepare, protocol.CurrentState.Phase,
                "a forged report must not advance protocol phase");
        }

        private static void DuplicateTerminalReplayAndMismatchedDuplicateAreDistinct()
        {
            var request = Request(
                "duplicate-terminal-original", "duplicate-terminal-key", "host", "host", "peer-a");
            var protocol = CompleteTwoPartySuccess(request, "host", "peer-a");
            var replay = Request(
                "duplicate-terminal-retry", request.IdempotencyKey, "host", "host", "peer-a");
            var duplicate = protocol.TryBegin(replay, 10L, "host");

            TestAssert.Equal(WorkloadTransactionAdmissionCode.Duplicate, duplicate.Code,
                "a new request ID with the same idempotency key must replay idempotently");
            TestAssert.Equal(replay.RequestId, duplicate.Request.RequestId,
                "the replay admission must retain the new request ID for caller correlation");
            TestAssert.Equal(request.RequestId, duplicate.RegisteredRequest.RequestId,
                "the protocol must retain the original request identity separately");
            TestAssert.NotNull(duplicate.TerminalResult,
                "terminal duplicate replay must carry the retained result");
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded,
                duplicate.TerminalResult.TerminalState,
                "terminal duplicate replay must preserve the original outcome");
            TestAssert.Equal(request.IdempotencyKey, duplicate.TerminalResult.IdempotencyKey,
                "the terminal result must retain the idempotency identity");
            AssertConflictingTerminalReplay(
                protocol,
                request,
                "duplicate-terminal-conflict",
                11L,
                "successful");
        }

        private static void PendingIdempotentReplayRetainsCanonicalTransaction()
        {
            var original = Request(
                "pending-original", "pending-idempotency", "host", "host", "peer-a");
            var protocol = BeginHost(original, "host");
            var replay = Request(
                "pending-retry", original.IdempotencyKey, "host", "host", "peer-a");

            var pending = protocol.TryBegin(replay, 2L, "host");
            TestAssert.Equal(WorkloadTransactionAdmissionCode.Duplicate, pending.Code,
                "a pending idempotent retry must attach to the existing transaction");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending,
                pending.State.TerminalState,
                "a pending idempotent retry must retain the canonical pending state");
            TestAssert.Equal(replay.RequestId, pending.Request.RequestId,
                "the replay envelope must retain the caller's new request ID");
            TestAssert.Equal(original.RequestId, pending.RegisteredRequest.RequestId,
                "the replay must expose the canonical request identity for terminal correlation");

            Prepare(protocol, original, "host", 3L);
            Prepare(protocol, original, "peer-a", 4L);
            Execute(protocol, original, "host", 5L, true, false);
            Execute(protocol, original, "peer-a", 6L, true, false);
            Confirm(protocol, original, "peer-a", 7L, true, false);
            FinalDelivery(protocol, original, "peer-a", 8L);

            var terminalReplay = protocol.TryBegin(replay, 9L, "host");
            TestAssert.Equal(WorkloadTransactionAdmissionCode.Duplicate, terminalReplay.Code,
                "the same pending retry must remain replayable after completion");
            TestAssert.NotNull(terminalReplay.TerminalResult,
                "the pending retry must eventually receive the retained terminal result");
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded,
                terminalReplay.TerminalResult.TerminalState,
                "the replayed terminal result must reflect the canonical transaction outcome");
        }

        private static void TerminalFailureReplaysUseTheIdempotencyContract()
        {
            var rejectedRequest = Request(
                "terminal-rejected", "terminal-rejected-key", "host", "host", "peer-a");
            var rejectedProtocol = BeginHost(rejectedRequest, "host");
            var rejected = rejectedProtocol.RecordPrepareAcknowledgement(
                rejectedRequest.RequestId,
                rejectedRequest.RequestFingerprint,
                "peer-a",
                false,
                "prepare-rejected",
                "The peer rejected the immutable workload plan.",
                "prepare-report",
                2L,
                true);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.Rejected,
                rejected.State.TerminalState,
                "a prepare rejection must be terminal before mutation");
            AssertTerminalReplay(
                rejectedProtocol,
                rejectedRequest,
                WorkloadTransactionTerminalState.Rejected,
                "rejected");
            AssertConflictingTerminalReplay(
                rejectedProtocol,
                rejectedRequest,
                "terminal-rejected-conflict",
                201L,
                "failed");

            var abortedRequest = Request(
                "terminal-aborted", "terminal-aborted-key", "host", "host", "peer-a");
            var abortedProtocol = BeginHost(abortedRequest, "host");
            var aborted = abortedProtocol.RequestAbort(
                abortedRequest.RequestId,
                abortedRequest.RequestFingerprint,
                "user-aborted",
                "The synchronized operation was aborted before mutation.",
                2L);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.Aborted,
                aborted.State.TerminalState,
                "an explicit pre-mutation abort must be terminal");
            AssertTerminalReplay(
                abortedProtocol,
                abortedRequest,
                WorkloadTransactionTerminalState.Aborted,
                "aborted");

            var timeoutRequest = Request(
                "terminal-timeout", "terminal-timeout-key", "host", "host", "peer-a");
            var timeoutProtocol = BeginHost(timeoutRequest, "host");
            var timeout = timeoutProtocol.CheckTimeout(100L);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.TimedOut,
                timeout.State.TerminalState,
                "a pre-mutation timeout must be terminal");
            AssertTerminalReplay(
                timeoutProtocol,
                timeoutRequest,
                WorkloadTransactionTerminalState.TimedOut,
                "timed out");

            var rolledBackRequest = Request(
                "terminal-rolled-back", "terminal-rolled-back-key", "host", "host", "peer-a");
            var rolledBackProtocol = BeginHost(rolledBackRequest, "host");
            Prepare(rolledBackProtocol, rolledBackRequest, "host", 2L);
            Prepare(rolledBackProtocol, rolledBackRequest, "peer-a", 3L);
            Execute(rolledBackProtocol, rolledBackRequest, "host", 4L, true, false);
            var executeFailure = Execute(
                rolledBackProtocol,
                rolledBackRequest,
                "peer-a",
                5L,
                false,
                true);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.RollbackRequired,
                executeFailure.State.TerminalState,
                "a post-mutation failure must retain the rollback barrier");
            rolledBackProtocol.RecordRollback(
                rolledBackRequest.RequestId,
                rolledBackRequest.RequestFingerprint,
                "host",
                true,
                "rolled-back",
                string.Empty,
                6L,
                true);
            var rolledBack = rolledBackProtocol.RecordRollback(
                rolledBackRequest.RequestId,
                rolledBackRequest.RequestFingerprint,
                "peer-a",
                true,
                "rolled-back",
                string.Empty,
                7L,
                true);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.RolledBack,
                rolledBack.State.TerminalState,
                "a complete rollback acknowledgement must be terminal");
            AssertTerminalReplay(
                rolledBackProtocol,
                rolledBackRequest,
                WorkloadTransactionTerminalState.RolledBack,
                "rolled back");
        }

        private static void AssertTerminalReplay(
            WorkloadTransactionProtocol protocol,
            WorkloadTransactionRequest original,
            WorkloadTransactionTerminalState expected,
            string label)
        {
            var replay = Request(
                original.RequestId + "-retry",
                original.IdempotencyKey,
                "host",
                "host",
                "peer-a");
            var duplicate = protocol.TryBegin(replay, 200L, "host");
            TestAssert.Equal(
                WorkloadTransactionAdmissionCode.Duplicate,
                duplicate.Code,
                "a " + label + " operation must remain replayable by idempotency key");
            TestAssert.Equal(
                expected,
                duplicate.TerminalResult.TerminalState,
                "a " + label + " replay must preserve the original terminal state");
            TestAssert.Equal(
                replay.RequestId,
                duplicate.Request.RequestId,
                "a " + label + " replay must correlate to its new request ID");
        }

        private static void AssertConflictingTerminalReplay(
            WorkloadTransactionProtocol protocol,
            WorkloadTransactionRequest original,
            string conflictingRequestId,
            long sequence,
            string label)
        {
            var conflict = TestSupport.Request(
                requestId: conflictingRequestId,
                idempotencyKey: original.IdempotencyKey,
                targetId: "different-target",
                requesterPlayerKey: "host",
                participantKeys: new[] { "host", "peer-a" });
            var mismatched = protocol.TryBegin(conflict, sequence, "host");

            TestAssert.Equal(
                WorkloadTransactionAdmissionCode.MismatchedDuplicate,
                mismatched.Code,
                "a conflicting replay after terminal " + label + " must be rejected as a mismatch");
            TestAssert.Equal(
                conflictingRequestId,
                mismatched.Request.RequestId,
                "a conflicting replay must retain its new caller request ID");
            TestAssert.Equal(
                original.RequestId,
                mismatched.RegisteredRequest.RequestId,
                "a conflicting replay may identify the canonical transaction without adopting its request ID");
            TestAssert.True(
                mismatched.TerminalResult == null,
                "a conflicting replay must never receive the canonical transaction's terminal result");
        }

        private static void ConfirmationBarrierWaitsForEveryPeer()
        {
            var request = Request(
                "confirmation-barrier", "confirmation-barrier-idempotency", "host",
                "host", "peer-a", "peer-b");
            var protocol = BeginHost(request, "host");
            Prepare(protocol, request, "host", 2L);
            Prepare(protocol, request, "peer-a", 3L);
            Prepare(protocol, request, "peer-b", 4L);
            Execute(protocol, request, "host", 5L, true, false);
            Execute(protocol, request, "peer-a", 6L, true, false);
            Execute(protocol, request, "peer-b", 7L, true, false);

            var first = Confirm(protocol, request, "peer-a", 8L, true, false);
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, first.State.TerminalState,
                "one peer confirmation acknowledgement cannot terminate a multi-peer transaction");
            TestAssert.True(protocol.LastResult == null,
                "no terminal result may be published before the confirmation barrier completes");
            var second = Confirm(protocol, request, "peer-b", 9L, true, false);
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, second.State.TerminalState,
                "the last readiness acknowledgement reaches a commit decision but is not terminal");
            TestAssert.True(protocol.LastResult == null,
                "the commit decision must remain nonterminal before final delivery reports");
            FinalDelivery(protocol, request, "peer-a", 10L);
            var delivered = FinalDelivery(protocol, request, "peer-b", 11L);
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded, delivered.State.TerminalState,
                "the last final delivery report completes the transaction");
        }

        private static void MultiPeerNoChangeCompletesWithoutRollback()
        {
            var request = Request(
                "multi-peer-no-change", "multi-peer-no-change-idempotency", "host",
                "host", "peer-a", "peer-b");
            var protocol = BeginHost(request, "host");
            Prepare(protocol, request, "host", 2L);
            Prepare(protocol, request, "peer-a", 3L);
            Prepare(protocol, request, "peer-b", 4L);

            Execute(protocol, request, "host", 5L, true, false, true);
            Execute(protocol, request, "peer-a", 6L, true, false, true);
            var executePeerB = Execute(protocol, request, "peer-b", 7L, true, false, true);

            TestAssert.Equal(
                WorkloadTransactionTransitionAction.SendConfirm,
                executePeerB.Action,
                "all no-change execute reports must still cross the synchronized confirmation barrier");
            TestAssert.True(
                executePeerB.State.IsNoChange,
                "the host must retain the all-peer no-change mode for lease-free confirmation");
            TestAssert.False(
                executePeerB.State.MutationStarted,
                "a provisional no-op must never enter the mutation-started rollback path");
            TestAssert.Equal(
                0,
                executePeerB.State.RollbackParticipants.Count,
                "a provisional no-op must not enroll rollback participants");

            var confirmation = Confirm(protocol, request, "peer-a", 8L, true, false, true);
            TestAssert.Equal(
                WorkloadTransactionTransitionAction.None,
                confirmation.Action,
                "the first no-change confirmation acknowledgement must remain pending");
            confirmation = Confirm(protocol, request, "peer-b", 9L, true, false, true);
            TestAssert.Equal(
                WorkloadTransactionTransitionAction.SendFinalConfirmation,
                confirmation.Action,
                "the final no-change readiness acknowledgement must dispatch final delivery");
            TestAssert.False(
                confirmation.State.MutationStarted,
                "no-change confirmation must not mark the host mutation as started");
            TestAssert.Equal(
                0,
                confirmation.State.RollbackParticipants.Count,
                "no-change confirmation must remain outside rollback/recovery");

            var duplicateConfirmation = Confirm(protocol, request, "peer-a", 10L, true, false, true);
            TestAssert.Equal(
                WorkloadTransactionTransitionDisposition.Ignored,
                duplicateConfirmation.Disposition,
                "a replayed no-change confirmation acknowledgement must remain idempotent");
            TestAssert.Equal(
                WorkloadTransactionTerminalState.Pending,
                duplicateConfirmation.State.TerminalState,
                "a replayed no-change confirmation must not advance the barrier");

            var firstDelivery = FinalDelivery(protocol, request, "peer-a", 11L);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.Pending,
                firstDelivery.State.TerminalState,
                "one no-change final acknowledgement must not complete early");
            var duplicateDelivery = FinalDelivery(protocol, request, "peer-a", 12L);
            TestAssert.Equal(
                WorkloadTransactionTransitionDisposition.Ignored,
                duplicateDelivery.Disposition,
                "a replayed no-change final acknowledgement must remain idempotent");
            TestAssert.Equal(
                WorkloadTransactionTerminalState.Pending,
                duplicateDelivery.State.TerminalState,
                "a replayed no-change final acknowledgement must not complete early");
            var delivered = FinalDelivery(protocol, request, "peer-b", 13L);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.Succeeded,
                delivered.State.TerminalState,
                "all peers must reach terminal success for a synchronized no-op");
            TestAssert.True(
                protocol.LastResult != null && protocol.LastResult.Accepted,
                "a synchronized no-op must publish an accepted terminal result");
            TestAssert.False(
                protocol.LastResult.RequiresRollback,
                "a synchronized no-op terminal result must not require rollback");

            var peerProtocol = new WorkloadTransactionProtocol();
            peerProtocol.AcceptIncoming(
                request, 0L, true, WorkloadTransactionAdmissionCode.Accepted, null, "host");
            peerProtocol.RecordExecuteControl(
                request.RequestId, request.RequestFingerprint, true, "execute", string.Empty, 2L, true);
            var peerConfirmation = peerProtocol.RecordConfirmationControl(
                request.RequestId,
                request.RequestFingerprint,
                true,
                WorkloadTransactionCodes.NoChange,
                "confirm-no-change",
                string.Empty,
                3L,
                true);
            TestAssert.True(
                peerConfirmation.State.IsNoChange,
                "the peer must retain the host's no-change confirmation mode");
            TestAssert.False(
                peerConfirmation.State.MutationStarted,
                "the peer no-change mode must not imply a mutation or rollback lease");
            var duplicatePeerConfirmation = peerProtocol.RecordConfirmationControl(
                request.RequestId,
                request.RequestFingerprint,
                true,
                WorkloadTransactionCodes.NoChange,
                "confirm-no-change-replay",
                string.Empty,
                4L,
                true);
            TestAssert.Equal(
                WorkloadTransactionTransitionDisposition.Ignored,
                duplicatePeerConfirmation.Disposition,
                "a replayed no-change confirmation control must remain idempotent");

            var peerFinal = peerProtocol.RecordFinalConfirmation(
                request.RequestId,
                request.RequestFingerprint,
                true,
                WorkloadTransactionCodes.NoChange,
                "confirmed-no-change",
                4L,
                true);
            TestAssert.Equal(
                WorkloadTransactionTerminalState.Succeeded,
                peerFinal.State.TerminalState,
                "a peer must accept the lease-free final no-change decision");
            var duplicatePeerFinal = peerProtocol.RecordFinalConfirmation(
                request.RequestId,
                request.RequestFingerprint,
                true,
                WorkloadTransactionCodes.NoChange,
                "confirmed-no-change-replay",
                5L,
                true);
            TestAssert.Equal(
                WorkloadTransactionTransitionAction.SendFinalConfirmationAcknowledgement,
                duplicatePeerFinal.Action,
                "a replayed no-change final control must be acknowledged idempotently");
        }

        private static void MixedNoChangeAndMutationFailsClosed()
        {
            var request = Request(
                "mixed-no-change", "mixed-no-change-idempotency", "host",
                "host", "peer-a", "peer-b");
            var protocol = BeginHost(request, "host");
            Prepare(protocol, request, "host", 2L);
            Prepare(protocol, request, "peer-a", 3L);
            Prepare(protocol, request, "peer-b", 4L);

            Execute(protocol, request, "host", 5L, true, false, true);
            Execute(protocol, request, "peer-a", 6L, true, false);
            var mixed = Execute(protocol, request, "peer-b", 7L, true, false, true);

            TestAssert.Equal(
                WorkloadTransactionTransitionAction.SendAbort,
                mixed.Action,
                "mixed no-change and mutation reports must abort before confirmation");
            TestAssert.Equal(
                WorkloadTransactionTerminalState.RollbackRequired,
                mixed.State.TerminalState,
                "a mixed report must fail closed through rollback/recovery");
            TestAssert.True(
                mixed.State.MutationStarted,
                "a mixed report containing a real mutation must retain mutation ownership");
            TestAssert.Equal(
                "execute-mode-mismatch",
                mixed.Code,
                "mixed mode must expose a stable fail-closed diagnostic");
            TestAssert.True(
                mixed.State.RollbackParticipants.Count > 0,
                "a mixed report must retain rollback participants for the real provisional write");
        }

        private static void FinalDeliveryRetriesWithoutRollbackAfterCommitDecision()
        {
            var request = Request(
                "final-delivery-retry", "final-delivery-retry-idempotency", "host",
                "host", "peer-a");
            var protocol = BeginHost(request, "host");
            var callbacks = new ThrowingFinalSendCallbacks();
            protocol.SetCallbacks(callbacks);
            Prepare(protocol, request, "host", 2L);
            Prepare(protocol, request, "peer-a", 3L);
            Execute(protocol, request, "host", 4L, true, false);
            Execute(protocol, request, "peer-a", 5L, true, false);
            var decision = Confirm(protocol, request, "peer-a", 6L, true, false);

            TestAssert.Equal(1, callbacks.FinalSendAttempts,
                "the commit decision must attempt final delivery once");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, decision.State.TerminalState,
                "a failed final send callback must leave the commit decision pending");
            TestAssert.True(decision.State.CommitDecisionReached,
                "the irreversible commit decision must remain explicit while delivery is pending");

            var retry = protocol.CheckTimeout(decision.State.DeadlineSequence);
            TestAssert.Equal(WorkloadTransactionTransitionAction.SendFinalConfirmation, retry.Action,
                "a post-decision timeout must retry final delivery");
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending, retry.State.TerminalState,
                "a post-decision timeout must never request rollback");
            TestAssert.Equal(2, callbacks.FinalSendAttempts,
                "the retry must invoke final delivery again");
            TestAssert.Equal(0, retry.State.RollbackParticipants.Count,
                "final delivery retry must not enroll committed peers for rollback");

            var delivered = FinalDelivery(protocol, request, "peer-a", retry.State.Sequence + 1L);
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded, delivered.State.TerminalState,
                "a later delivery acknowledgement must complete the retained decision");

            var peerProtocol = new WorkloadTransactionProtocol();
            peerProtocol.AcceptIncoming(
                request, 0L, true, WorkloadTransactionAdmissionCode.Accepted, null, "host");
            peerProtocol.RecordExecuteControl(
                request.RequestId, request.RequestFingerprint, true, "execute", string.Empty, 2L, true);
            peerProtocol.RecordConfirmationControl(
                request.RequestId, request.RequestFingerprint, true, "confirm", string.Empty,
                "confirm-report", 3L, true);
            var peerTimeout = peerProtocol.CheckTimeout(
                peerProtocol.CurrentState.DeadlineSequence);
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending,
                peerTimeout.State.TerminalState,
                "a peer that voted ready must not roll back unilaterally while the host decision is delayed");
            TestAssert.Equal(WorkloadTransactionTransitionAction.ConfirmLocally,
                peerTimeout.Action,
                "a ready peer timeout must re-acknowledge readiness and keep its rollback lease");
            TestAssert.Equal(0, peerTimeout.State.RollbackParticipants.Count,
                "a ready peer timeout must not enroll itself for rollback");
        }

        private static void DuplicateFinalConfirmationIsAcknowledgedIdempotently()
        {
            var request = Request(
                "duplicate-final-control", "duplicate-final-control-idempotency", "host",
                "host", "peer-a");
            var peerProtocol = new WorkloadTransactionProtocol();
            TestAssert.True(
                peerProtocol.AcceptIncoming(
                    request, 0L, true, WorkloadTransactionAdmissionCode.Accepted, null, "host").Accepted,
                "the peer fixture must admit the authenticated request");
            peerProtocol.RecordExecuteControl(
                request.RequestId, request.RequestFingerprint, true, "execute", string.Empty, 2L, true);
            peerProtocol.RecordConfirmationControl(
                request.RequestId, request.RequestFingerprint, true, "confirm", string.Empty,
                "confirm-report", 3L, true);
            var first = peerProtocol.RecordFinalConfirmation(
                request.RequestId, request.RequestFingerprint, true, "confirmed", string.Empty, 4L, true);
            TestAssert.Equal(
                WorkloadTransactionTransitionAction.SendFinalConfirmationAcknowledgement,
                first.Action,
                "the peer must acknowledge the first final commit decision after applying it");
            var duplicate = peerProtocol.RecordFinalConfirmation(
                request.RequestId, request.RequestFingerprint, true, "confirmed", string.Empty, 5L, true);
            TestAssert.Equal(WorkloadTransactionTransitionDisposition.Ignored, duplicate.Disposition,
                "a duplicate final control must not apply the commit twice");
            TestAssert.Equal(
                WorkloadTransactionTransitionAction.SendFinalConfirmationAcknowledgement,
                duplicate.Action,
                "a duplicate final control must still be acknowledged so a lost acknowledgement can recover");
        }

        private static void TimeoutAndRosterChangeAbortCoherently()
        {
            var timeoutRequest = Request(
                "timeout-before-mutation", "timeout-before-mutation-idempotency", "host", "host", "peer-a");
            var timeoutProtocol = BeginHost(timeoutRequest, "host");
            var timedOut = timeoutProtocol.CheckTimeout(100L);
            TestAssert.Equal(WorkloadTransactionTerminalState.TimedOut, timedOut.State.TerminalState,
                "prepare timeout before mutation must end as timed out");
            TestAssert.Equal(WorkloadTransactionTransitionAction.SendAbort, timedOut.Action,
                "timeout must dispatch one coherent abort");
            var duplicateTimeout = timeoutProtocol.CheckTimeout(101L);
            TestAssert.Equal(WorkloadTransactionTransitionAction.None, duplicateTimeout.Action,
                "a terminal timeout must not dispatch abort twice");

            var rosterRequest = Request(
                "roster-change", "roster-change-idempotency", "host", "host", "peer-a");
            var rosterProtocol = BeginHost(rosterRequest, "host");
            Prepare(rosterProtocol, rosterRequest, "host", 2L);
            Prepare(rosterProtocol, rosterRequest, "peer-a", 3L);
            var rosterChanged = rosterProtocol.Fail(
                rosterRequest.RequestId,
                "session-context-changed",
                "The authenticated roster changed during execution.",
                4L);
            TestAssert.Equal(WorkloadTransactionTerminalState.RollbackRequired,
                rosterChanged.State.TerminalState,
                "roster change after execute authorization must fail closed into rollback");
            TestAssert.Equal(WorkloadTransactionTransitionAction.SendAbort, rosterChanged.Action,
                "roster change must dispatch a coherent abort");
            var repeated = rosterProtocol.Fail(
                rosterRequest.RequestId, "session-context-changed", "Repeated roster notification.", 5L);
            TestAssert.Equal(WorkloadTransactionTransitionAction.None, repeated.Action,
                "repeated roster invalidation must not dispatch abort twice");
        }

        private static void RollbackFailureRemainsTerminalAndReplayable()
        {
            var request = Request(
                "rollback-failure", "rollback-failure-idempotency", "host", "host", "peer-a");
            var protocol = BeginHost(request, "host");
            Prepare(protocol, request, "host", 2L);
            Prepare(protocol, request, "peer-a", 3L);
            Execute(protocol, request, "host", 4L, true, false);
            var failedExecute = Execute(protocol, request, "peer-a", 5L, false, true);
            TestAssert.Equal(WorkloadTransactionTerminalState.RollbackRequired,
                failedExecute.State.TerminalState,
                "an execution failure after mutation must require rollback");

            var rollbackFailed = protocol.RecordRollback(
                request.RequestId, request.RequestFingerprint, "host", false, "rollback-failed",
                "The retained rollback lease could not restore state.", 6L, true);
            TestAssert.Equal(WorkloadTransactionTerminalState.RollbackFailed,
                rollbackFailed.State.TerminalState,
                "failed rollback must enter terminal recovery-required state");
            var lateSuccess = protocol.RecordRollback(
                request.RequestId, request.RequestFingerprint, "peer-a", true, "rolled-back",
                string.Empty, 7L, true);
            TestAssert.Equal(WorkloadTransactionTransitionDisposition.Ignored, lateSuccess.Disposition,
                "late rollback success cannot overwrite terminal rollback failure");
            TestAssert.Equal(WorkloadTransactionTerminalState.RollbackFailed,
                protocol.CurrentState.TerminalState,
                "rollback failure must remain terminal");

            var replay = Request(
                request.RequestId + "-replay",
                request.IdempotencyKey,
                "host",
                "host",
                "peer-a");
            var duplicate = protocol.TryBegin(replay, 8L, "host");
            TestAssert.Equal(WorkloadTransactionAdmissionCode.Duplicate, duplicate.Code,
                "rollback-failed requests must remain inside the idempotency horizon");
            TestAssert.Equal(WorkloadTransactionTerminalState.RollbackFailed,
                duplicate.TerminalResult.TerminalState,
                "duplicate replay must preserve rollback-failed recovery state");

            var blockedRequest = Request(
                "rollback-failure-new", "rollback-failure-new-idempotency", "host", "host", "peer-a");
            var blocked = protocol.TryBegin(blockedRequest, 9L, "host");
            TestAssert.Equal(WorkloadTransactionAdmissionCode.RecoveryRequired, blocked.Code,
                "a retained rollback lease must block a new operation");
            TestAssert.Equal(WorkloadTransactionTerminalState.RollbackFailed,
                blocked.State.TerminalState,
                "recovery blocking must preserve the retained rollback-failed state");
            TestAssert.Equal(1, protocol.RequestTable.Count,
                "a recovery-blocked operation must not consume transaction-table capacity");
        }

        private static void InvalidRostersDoNotPoisonTransactionCapacity()
        {
            var protocol = new WorkloadTransactionProtocol(1);
            for (int index = 0; index < 40; index++)
            {
                var malformed = Request(
                    "invalid-roster-" + index,
                    "invalid-roster-key-" + index,
                    "host",
                    "peer-a");
                var admission = protocol.TryBegin(malformed, index, "host");
                TestAssert.Equal(WorkloadTransactionAdmissionCode.InvalidRequest,
                    admission.Code,
                    "a requester absent from the roster must be rejected before registration");
                TestAssert.Equal(0, protocol.RequestTable.Count,
                    "an invalid roster must not consume a transaction-table slot");
            }

            var valid = Request(
                "after-invalid-rosters", "after-invalid-rosters-key", "host", "host");
            var accepted = protocol.TryBegin(valid, 100L, "host");
            TestAssert.True(accepted.Accepted,
                "a valid request must remain admissible after repeated malformed rosters");
            TestAssert.Equal(1, protocol.RequestTable.Count,
                "only the valid request should occupy the bounded transaction table");
        }

        private static void InvalidRevisionContextIsRejectedAtRequestConstruction()
        {
            WorkloadTransactionPayload payload;
            string diagnostic;
            TestAssert.True(
                WorkloadTransactionPayload.TryCreate(new byte[] { 1 }, null, out payload, out diagnostic),
                "revision test payload must be valid");
            var invalid = new WorkloadTransactionRevisionVector(
                1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L,
                "bwt", "", "roster", "template", 1L, "taxonomy");
            WorkloadTransactionRequest request;
            TestAssert.False(
                WorkloadTransactionRequest.TryCreateCanonical(
                    WorkloadTransactionOperation.Apply,
                    "request-invalid-context",
                    "idempotency-invalid-context",
                    "session-1",
                    "night-shift",
                    "night-shift",
                    "host",
                    100,
                    payload,
                    invalid,
                    new[] { "host", "peer-a" },
                    out request,
                    out diagnostic),
                "a missing host session context must fail closed before synchronization");
            TestAssert.True(!string.IsNullOrEmpty(diagnostic),
                "invalid revision construction must return a diagnostic");
        }

        private static void LegacyApiRegistrationFailsClosedWithoutPartialWorkers()
        {
            var oldEnabled = MP.enabled;
            var oldMultiplayer = MP.IsInMultiplayer;
            var oldHosting = MP.IsHosting;
            var oldPlayerName = MP.PlayerName;
            try
            {
                MP.enabled = true;
                MP.IsInMultiplayer = true;
                MP.IsHosting = true;
                MP.PlayerName = "host";
                MP.RegisteredSyncWorkerCount = 0;
                MultiplayerBridge.SetWorkloadTransactionAuthentication(new TestAuthentication());

                TestAssert.False(WorkloadTransactionMultiplayer.TryRegisterProtocol(),
                    "an API surface without SetHostOnly must not advertise workload synchronization");
                TestAssert.False(WorkloadTransactionMultiplayer.ProtocolAvailable,
                    "legacy API registration must remain unavailable");
                TestAssert.Equal(0, MP.RegisteredSyncWorkerCount,
                    "legacy API rejection must occur before any workload sync worker is registered");
            }
            finally
            {
                MultiplayerBridge.SetWorkloadTransactionAuthentication(null);
                MP.enabled = oldEnabled;
                MP.IsInMultiplayer = oldMultiplayer;
                MP.IsHosting = oldHosting;
                MP.PlayerName = oldPlayerName;
            }
        }

        private static WorkloadTransactionRequest Request(
            string requestId,
            string idempotencyKey,
            string requester,
            params string[] participants)
        {
            return TestSupport.Request(
                requestId: requestId,
                idempotencyKey: idempotencyKey,
                requesterPlayerKey: requester,
                participantKeys: participants);
        }

        private static WorkloadTransactionProtocol BeginHost(
            WorkloadTransactionRequest request,
            string host)
        {
            var protocol = new WorkloadTransactionProtocol();
            var admission = protocol.TryBegin(request, 0L, host);
            TestAssert.True(admission.Accepted, "a valid all-roster request must be admitted");
            var sent = protocol.MarkRequestSent(request.RequestId, 1L);
            TestAssert.Equal(WorkloadTransactionTransitionAction.SendPrepare, sent.Action,
                "request dispatch must enter the prepare phase");
            return protocol;
        }

        private static WorkloadTransactionProtocol CompleteTwoPartySuccess(
            WorkloadTransactionRequest request,
            string host,
            string peer)
        {
            var protocol = BeginHost(request, host);
            Prepare(protocol, request, host, 2L);
            Prepare(protocol, request, peer, 3L);
            Execute(protocol, request, host, 4L, true, false);
            Execute(protocol, request, peer, 5L, true, false);
            var confirmation = Confirm(protocol, request, peer, 6L, true, false);
            TestAssert.Equal(WorkloadTransactionTerminalState.Pending,
                confirmation.State.TerminalState,
                "two-party success must wait for final delivery after readiness");
            var delivery = FinalDelivery(protocol, request, peer, 7L);
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded,
                delivery.State.TerminalState,
                "two-party success completes after the peer reports final delivery");
            return protocol;
        }

        private static WorkloadTransactionTransitionResult Prepare(
            WorkloadTransactionProtocol protocol,
            WorkloadTransactionRequest request,
            string participant,
            long sequence)
        {
            return protocol.RecordPrepareAcknowledgement(
                request.RequestId, request.RequestFingerprint, participant, true,
                "prepared", string.Empty, "prepare-report", sequence, true);
        }

        private static WorkloadTransactionTransitionResult Execute(
            WorkloadTransactionProtocol protocol,
            WorkloadTransactionRequest request,
            string participant,
            long sequence,
            bool accepted,
            bool requiresRollback,
            bool noChange = false)
        {
            return protocol.RecordExecuteResult(
                request.RequestId,
                request.RequestFingerprint,
                participant,
                accepted,
                accepted
                    ? noChange
                        ? WorkloadTransactionCodes.NoChange
                        : "executed"
                    : "execution-failed",
                accepted ? string.Empty : "Execution failed after retaining a rollback lease.",
                "execute-report",
                sequence,
                true,
                requiresRollback);
        }

        private static WorkloadTransactionTransitionResult Confirm(
            WorkloadTransactionProtocol protocol,
            WorkloadTransactionRequest request,
            string participant,
            long sequence,
            bool accepted,
            bool requiresRollback,
            bool noChange = false)
        {
            return protocol.RecordConfirmationAcknowledgement(
                request.RequestId,
                request.RequestFingerprint,
                participant,
                accepted,
                accepted
                    ? noChange
                        ? WorkloadTransactionCodes.NoChange
                        : "confirmation-ready"
                    : "confirmation-rejected",
                string.Empty,
                "confirmation-report",
                sequence,
                true,
                requiresRollback);
        }

        private static WorkloadTransactionTransitionResult FinalDelivery(
            WorkloadTransactionProtocol protocol,
            WorkloadTransactionRequest request,
            string participant,
            long sequence)
        {
            return protocol.RecordFinalConfirmationAcknowledgement(
                request.RequestId,
                request.RequestFingerprint,
                participant,
                true,
                "final-confirmation-delivered",
                string.Empty,
                sequence,
                true);
        }

        private sealed class ThrowingFinalSendCallbacks : IWorkloadTransactionCallbacks
        {
            internal int FinalSendAttempts { get; private set; }

            public void OnRequestAccepted(WorkloadTransactionRequest request) { }
            public void OnAdmissionRejected(WorkloadTransactionAdmission admission) { }
            public void OnPrepareRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
            public void OnExecuteRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
            public void OnConfirmationControlReceived(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
            public void OnConfirmRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
            public void OnAbortRequested(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
            public void OnRollbackRequired(WorkloadTransactionRequest request, WorkloadTransactionState state) { }
            public void OnTerminal(WorkloadTransactionResult result) { }

            public void OnFinalConfirmationRequested(
                WorkloadTransactionRequest request,
                WorkloadTransactionState state)
            {
                FinalSendAttempts++;
                throw new InvalidOperationException("injected final-delivery send failure");
            }
        }

        private sealed class TestAuthentication : IWorkloadTransactionAuthentication
        {
            public bool IsAvailable => true;

            public bool TryGetSynchronizedSenderKey(out string senderKey)
            {
                senderKey = "host";
                return true;
            }

            public bool TryGetAuthenticatedHostKey(
                string hostSessionEpoch,
                string rosterFingerprint,
                out string hostKey)
            {
                hostKey = "host";
                return true;
            }

            public bool IsAuthenticatedInitiator(
                string senderKey,
                string hostSessionEpoch,
                string rosterFingerprint)
            {
                return string.Equals(senderKey, "host", StringComparison.Ordinal);
            }

            public bool IsAuthenticatedHost(
                string senderKey,
                string hostSessionEpoch,
                string rosterFingerprint)
            {
                return string.Equals(senderKey, "host", StringComparison.Ordinal);
            }

            public bool IsAuthenticatedPeer(
                string senderKey,
                string hostSessionEpoch,
                string rosterFingerprint)
            {
                return string.Equals(senderKey, "host", StringComparison.Ordinal) ||
                       string.Equals(senderKey, "peer-a", StringComparison.Ordinal) ||
                       string.Equals(senderKey, "peer-b", StringComparison.Ordinal);
            }
        }
    }
}
