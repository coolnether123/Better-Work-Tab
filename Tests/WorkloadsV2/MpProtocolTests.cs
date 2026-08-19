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
            ConfirmationBarrierWaitsForEveryPeer();
            TimeoutAndRosterChangeAbortCoherently();
            RollbackFailureRemainsTerminalAndReplayable();
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
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded, confirmationPeerB.State.TerminalState,
                "the host becomes successful only after the full acknowledgement barrier");
            TestAssert.NotNull(protocol.LastResult,
                "terminal host success must retain an idempotent result");

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
                "duplicate-terminal", "duplicate-terminal-original", "host", "host", "peer-a");
            var protocol = CompleteTwoPartySuccess(request, "host", "peer-a");
            var replay = Request(
                request.RequestId, "duplicate-terminal-replay", "host", "host", "peer-a");
            var duplicate = protocol.TryBegin(replay, 10L, "host");

            TestAssert.Equal(WorkloadTransactionAdmissionCode.Duplicate, duplicate.Code,
                "same request ID and semantic payload must replay idempotently");
            TestAssert.NotNull(duplicate.TerminalResult,
                "terminal duplicate replay must carry the retained result");
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded,
                duplicate.TerminalResult.TerminalState,
                "terminal duplicate replay must preserve the original outcome");

            var mismatch = TestSupport.Request(
                requestId: request.RequestId,
                idempotencyKey: "duplicate-terminal-mismatch",
                targetId: "different-target",
                requesterPlayerKey: "host",
                participantKeys: new[] { "host", "peer-a" });
            var mismatched = protocol.TryBegin(mismatch, 11L, "host");
            TestAssert.Equal(WorkloadTransactionAdmissionCode.MismatchedDuplicate, mismatched.Code,
                "reusing a request ID with different canonical semantics must be rejected");
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
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded, second.State.TerminalState,
                "the last peer acknowledgement completes the barrier");
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
                request.RequestId, "rollback-failure-replay", "host", "host", "peer-a");
            var duplicate = protocol.TryBegin(replay, 8L, "host");
            TestAssert.Equal(WorkloadTransactionAdmissionCode.Duplicate, duplicate.Code,
                "rollback-failed requests must remain inside the idempotency horizon");
            TestAssert.Equal(WorkloadTransactionTerminalState.RollbackFailed,
                duplicate.TerminalResult.TerminalState,
                "duplicate replay must preserve rollback-failed recovery state");
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
            TestAssert.Equal(WorkloadTransactionTerminalState.Succeeded,
                confirmation.State.TerminalState,
                "two-party success must complete after the peer confirmation acknowledgement");
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
            bool requiresRollback)
        {
            return protocol.RecordExecuteResult(
                request.RequestId,
                request.RequestFingerprint,
                participant,
                accepted,
                accepted ? "executed" : "execution-failed",
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
            bool requiresRollback)
        {
            return protocol.RecordConfirmationAcknowledgement(
                request.RequestId,
                request.RequestFingerprint,
                participant,
                accepted,
                accepted ? "confirmation-ready" : "confirmation-rejected",
                string.Empty,
                "confirmation-report",
                sequence,
                true,
                requiresRollback);
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
