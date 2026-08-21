using System;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PersistenceReceiptRecoveryTests
    {
        public static void Run()
        {
            UpdateRecoveryFromAuthoritativeSnapshotRebasesWithoutLiveRecapture();
            ForkRecoveryFromAuthoritativeSnapshotUsesForkIdentity();
            WrongForkLabelIsRejected();
            WrongForkIdIsRejected();
            StalePersistenceRevisionIsRejected();
            WrongPersistedTargetIdentityIsRejected();
        }

        private static void UpdateRecoveryFromAuthoritativeSnapshotRebasesWithoutLiveRecapture()
        {
            WorkloadSession session = OpenEditedSession(out WorkloadSemanticDiff liveDiff);
            WorkloadTemplate target = session.TargetTemplate;
            WorkloadPersistenceRecoverySnapshot snapshot =
                new WorkloadPersistenceRecoverySnapshot(
                    target,
                    8,
                    "after-update",
                    session.SourceTemplate.StableId);

            WorkloadOperationResult<WorkloadPersistenceReceipt> recovered =
                WorkloadPersistenceReceiptRecovery.TryRecover(
                    session,
                    WorkloadDecisionKind.Update,
                    session.SourceTemplate.StableId,
                    null,
                    7,
                    "before-update",
                    snapshot);

            TestAssert.True(
                recovered.Succeeded && recovered.Value != null,
                "Update recovery must construct a receipt from the authoritative snapshot");
            TestAssert.True(
                session.IsDirty,
                "receipt recovery must not mutate the active preview before rebase validation");

            WorkloadSession rebased = session.RebaseAfterPersistence(recovered.Value);
            TestAssert.NotNull(
                rebased,
                "a recovered Update receipt must be accepted by the session rebase seam");
            TestAssert.True(
                rebased.TemplateDiff.IsEmpty,
                "a recovered Update receipt must leave the template diff clean");
            TestAssert.Equal(
                liveDiff.BeforeFingerprint,
                rebased.LiveDiff.BeforeFingerprint,
                "Update recovery must preserve the captured live baseline before Apply");
            TestAssert.Equal(
                liveDiff.AfterFingerprint,
                rebased.LiveDiff.AfterFingerprint,
                "Update recovery must preserve the projected live impact after Apply");
        }

        private static void ForkRecoveryFromAuthoritativeSnapshotUsesForkIdentity()
        {
            WorkloadSession session = OpenEditedSession(out WorkloadSemanticDiff liveDiff);
            const string forkId = "forked-night-shift";
            const string forkLabel = "Forked Night Shift";
            WorkloadSessionDecision fork = session.Fork(forkId, forkLabel);
            TestAssert.True(fork.Accepted, "the deterministic fork target must be valid");

            WorkloadPersistenceRecoverySnapshot snapshot =
                new WorkloadPersistenceRecoverySnapshot(
                    fork.ResultTemplate,
                    8,
                    "after-fork",
                    forkId);
            WorkloadOperationResult<WorkloadPersistenceReceipt> recovered =
                WorkloadPersistenceReceiptRecovery.TryRecover(
                    session,
                    WorkloadDecisionKind.Fork,
                    forkId,
                    forkLabel,
                    7,
                    "before-fork",
                    snapshot);

            TestAssert.True(
                recovered.Succeeded && recovered.Value != null,
                "Save As recovery must validate the fork target reconstructed with its label");
            TestAssert.Equal(
                WorkloadSession.GetSourceIdentity(fork.ResultTemplate),
                recovered.Value.TargetIdentity,
                "Save As recovery must preserve the new fork identity");

            WorkloadSession rebased = session.RebaseAfterPersistence(recovered.Value);
            TestAssert.NotNull(
                rebased,
                "a recovered Save As receipt must be accepted by the session rebase seam");
            TestAssert.Equal(
                forkId,
                rebased.SourceTemplate.StableId,
                "Save As recovery must adopt the persisted fork as the active preview template");
            TestAssert.Equal(
                forkLabel,
                rebased.SourceTemplate.Label,
                "Save As recovery must retain the persisted fork label");
            TestAssert.True(
                rebased.TemplateDiff.IsEmpty,
                "a recovered Save As receipt must leave the template diff clean");
            TestAssert.Equal(
                liveDiff.BeforeFingerprint,
                rebased.LiveDiff.BeforeFingerprint,
                "Save As recovery must preserve the captured live baseline before Apply");
            TestAssert.Equal(
                liveDiff.AfterFingerprint,
                rebased.LiveDiff.AfterFingerprint,
                "Save As recovery must preserve the projected live impact after Apply");
        }

        private static void WrongForkLabelIsRejected()
        {
            WorkloadSession session = OpenEditedSession(out _);
            WorkloadSessionDecision fork = session.Fork(
                "forked-night-shift",
                "Persisted Fork Label");
            WorkloadPersistenceRecoverySnapshot snapshot =
                new WorkloadPersistenceRecoverySnapshot(
                    fork.ResultTemplate,
                    8,
                    "after-fork",
                    fork.ResultTemplate.StableId);

            WorkloadOperationResult<WorkloadPersistenceReceipt> recovered =
                WorkloadPersistenceReceiptRecovery.TryRecover(
                    session,
                    WorkloadDecisionKind.Fork,
                    fork.ResultTemplate.StableId,
                    "Wrong Fork Label",
                    7,
                    "before-fork",
                    snapshot);

            TestAssert.False(
                recovered.Succeeded,
                "recovery must reject a persisted fork whose label does not match the transaction target");
        }

        private static void WrongForkIdIsRejected()
        {
            WorkloadSession session = OpenEditedSession(out _);
            WorkloadSessionDecision fork = session.Fork(
                "persisted-fork-id",
                "Persisted Fork");
            WorkloadPersistenceRecoverySnapshot snapshot =
                new WorkloadPersistenceRecoverySnapshot(
                    fork.ResultTemplate,
                    8,
                    "after-fork",
                    fork.ResultTemplate.StableId);

            WorkloadOperationResult<WorkloadPersistenceReceipt> recovered =
                WorkloadPersistenceReceiptRecovery.TryRecover(
                    session,
                    WorkloadDecisionKind.Fork,
                    "different-requested-id",
                    "Persisted Fork",
                    7,
                    "before-fork",
                    snapshot);

            TestAssert.False(
                recovered.Succeeded,
                "recovery must reject a persisted fork whose stable ID differs from the transaction target");
        }

        private static void StalePersistenceRevisionIsRejected()
        {
            WorkloadSession session = OpenEditedSession(out _);
            WorkloadPersistenceRecoverySnapshot snapshot =
                new WorkloadPersistenceRecoverySnapshot(
                    session.TargetTemplate,
                    7,
                    "after-update",
                    session.SourceTemplate.StableId);

            WorkloadOperationResult<WorkloadPersistenceReceipt> recovered =
                WorkloadPersistenceReceiptRecovery.TryRecover(
                    session,
                    WorkloadDecisionKind.Update,
                    session.SourceTemplate.StableId,
                    null,
                    7,
                    "before-update",
                    snapshot);

            TestAssert.False(
                recovered.Succeeded,
                "recovery must reject a snapshot that does not prove the post-write revision");
        }

        private static void WrongPersistedTargetIdentityIsRejected()
        {
            WorkloadSession session = OpenEditedSession(out _);
            WorkloadTemplate expected = session.TargetTemplate;
            WorkloadTemplate wrongTarget = expected.WithDefinition(
                new WorkloadDefinition(
                    expected.StableId,
                    "Wrong persisted label",
                    expected.SchemaVersion,
                    expected.Definition.OwnershipDimensions,
                    expected.Definition.Scope));
            WorkloadPersistenceRecoverySnapshot snapshot =
                new WorkloadPersistenceRecoverySnapshot(
                    wrongTarget,
                    8,
                    "after-update",
                    expected.StableId);

            WorkloadOperationResult<WorkloadPersistenceReceipt> recovered =
                WorkloadPersistenceReceiptRecovery.TryRecover(
                    session,
                    WorkloadDecisionKind.Update,
                    expected.StableId,
                    null,
                    7,
                    "before-update",
                    snapshot);

            TestAssert.False(
                recovered.Succeeded,
                "recovery must reject a target record whose canonical identity is wrong");
        }

        private static WorkloadSession OpenEditedSession(out WorkloadSemanticDiff liveDiff)
        {
            WorkloadParentPriorityKey key = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p1"),
                TestSupport.WorkType("PlantWork"));
            WorkloadProjectedState sourceState = State(key, 3);
            WorkloadProjectedState liveState = State(key, 2);
            WorkloadTemplate template = TestSupport.Template(sourceState);
            WorkloadSession session = WorkloadSession.OpenCaptured(
                template,
                liveState,
                WorkloadSession.GetSourceIdentity(template));
            WorkloadSession edited = session.Edit(draft =>
                draft.SetParentPriorityIntent(
                    key,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                        new WorkloadSpecificPriorityPayload(4))));
            liveDiff = edited.LiveDiff;
            return edited;
        }

        private static WorkloadProjectedState State(
            WorkloadParentPriorityKey key,
            int priority)
        {
            return new WorkloadProjectedState(
                parentPriorities: new[]
                {
                    new WorkloadParentPriorityEntry(key, priority)
                },
                parentPriorityIntents: new[]
                {
                    TestSupport.ParentPriorityIntent(
                        key.Pawn,
                        key.WorkType,
                        WorkloadIntentState.Set,
                        priority)
                },
                representedPawnIds: new[] { key.Pawn });
        }
    }
}
