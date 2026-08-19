using System;
using System.IO;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.TimePriority;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class ScheduleTests
    {
        public static void Run()
        {
            var pawn = TestSupport.Pawn("p1");
            var workType = TestSupport.WorkType("PlantWork");
            var workGiver = TestSupport.WorkGiver("PlantCut");
            var parent = WorkloadScheduleTargetKey.ForParent(pawn, workType);
            var localWorkGiver = WorkloadScheduleTargetKey.ForWorkGiver(pawn, workType, workGiver);
            var globalWorkGiver = WorkloadScheduleTargetKey.GlobalWorkGiver(workType, workGiver);

            TestAssert.True(parent.IsValid && localWorkGiver.IsValid && globalWorkGiver.IsValid,
                "parent, local WorkGiver, and global WorkGiver schedule targets must be valid");
            TestAssert.Equal(WorkloadScheduleTargetKind.ParentWorkType, parent.TargetKind,
                "parent schedule target kind must be retained");
            TestAssert.Equal(WorkloadTargetScope.GlobalShared, globalWorkGiver.Scope,
                "global WorkGiver schedule must retain shared scope");

            var pinned = TestSupport.ScheduleWithValue(3, 1 << 14);
            var linked = TestSupport.ScheduleWithValue(3, 0);
            var linkedWithDifferentRetainedValue = TestSupport.ScheduleWithValue(4, 0);
            TestAssert.True(pinned.IsValid && linked.IsValid, "24-hour schedule payloads must validate");
            TestAssert.True(pinned.IsPinned(14), "hour 14 must remain explicitly pinned");
            TestAssert.False(linked.IsPinned(14), "the equal-valued linked hour must remain linked");
            TestAssert.False(pinned.Equals(linked), "pin state must participate in schedule equality");
            TestAssert.True(
                linked.Equals(linkedWithDifferentRetainedValue),
                "numeric values retained for linked hours must not participate in semantic equality");
            TestAssert.True(
                string.Equals(
                    linked.CanonicalForm,
                    linkedWithDifferentRetainedValue.CanonicalForm,
                    System.StringComparison.Ordinal),
                "linked-hour numeric values must be absent from the canonical schedule form");
            TestAssert.False(
                string.Equals(pinned.CanonicalForm, linked.CanonicalForm, System.StringComparison.Ordinal),
                "pin state must participate in the canonical schedule form");

            var pinnedAtBasePriority = TestSupport.ScheduleWithValue(3, 1 << 14);
            TestAssert.True(
                pinnedAtBasePriority.IsPinned(14),
                "a pinned hour equal to the current base must retain its pin metadata");
            TestAssert.False(
                pinnedAtBasePriority.Equals(linked),
                "a pinned hour equal to the current base must remain distinct from a linked hour");

            TimePriorityMutationAuthorization authorization;
            string authorizationReason;
            object requestIdentity = new object();
            object sessionIdentity = new object();
            TestAssert.True(
                TimePriorityMutationAuthorization.TryCreateForWorkload(
                    requestIdentity,
                    sessionIdentity,
                    "schedule-transaction-1",
                    "request-fingerprint",
                    "source-fingerprint",
                    "projected-fingerprint",
                    11L,
                    7L,
                    "BWT",
                    13,
                    17,
                    19,
                    "host-epoch",
                    "roster-fingerprint",
                    out authorization,
                    out authorizationReason),
                "a fully-bound schedule transaction authorization must be constructible");
            TestAssert.True(
                authorization.IsBoundTo(7L) && !authorization.IsBoundTo(8L),
                "a schedule authorization must reject a different authority revision");
            TestAssert.False(
                TimePriorityMutationAuthorization.IsAcceptedFor(true, 7L, null),
                "an ordinary synchronized schedule call without a token must fail closed");
            TestAssert.False(
                TimePriorityMutationAuthorization.IsAcceptedFor(true, 8L, authorization),
                "a synchronized schedule call with a stale token must fail closed");
            TestAssert.True(
                TimePriorityMutationAuthorization.IsAcceptedFor(true, 7L, authorization),
                "a synchronized schedule call with the bound token must be accepted");
            TestAssert.False(
                authorization.IsBoundToTransaction(
                    new object(),
                    sessionIdentity,
                    "schedule-transaction-1",
                    "request-fingerprint",
                    "source-fingerprint",
                    "projected-fingerprint",
                    11L,
                    7L,
                    "BWT",
                    13,
                    17,
                    19,
                    "host-epoch",
                    "roster-fingerprint"),
                "a capability must reject a different request object even when strings match");
            TestAssert.True(
                TimePriorityMutationAuthorization.IsAcceptedFor(false, 7L, null),
                "ordinary non-multiplayer schedule calls must retain legacy authorization behavior");

            AssertRollbackRegistrationContract();

            var source = new WorkloadProjectedState(
                scheduleIntents: new[]
                {
                    TestSupport.ScheduleIntent(parent,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(pinned)),
                    TestSupport.ScheduleIntent(localWorkGiver,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(linked)),
                    TestSupport.ScheduleIntent(globalWorkGiver,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(pinned))
                },
                representedPawnIds: new[] { pawn });
            TestAssert.Equal(3, source.ScheduleIntents.Count,
                "parent/local/global schedule intents must be represented");

            var changedPayload = TestSupport.ScheduleWithValue(4, 1 << 14);
            var edited = new WorkloadDraft(source)
                .SetScheduleIntent(parent,
                    WorkloadIntent<WorkloadSchedulePayload>.CreateSet(changedPayload))
                .ProjectedState;
            var diff = WorkloadSemanticDiff.Between(source, edited, WorkloadOwnershipDimensions.Schedules);
            TestAssert.False(diff.IsEmpty, "changing a schedule must produce a semantic diff");
            TestAssert.True(diff.Changes.Count > 0, "schedule diff must identify at least one change");

            var reverted = new WorkloadDraft(edited)
                .SetScheduleIntent(parent,
                    WorkloadIntent<WorkloadSchedulePayload>.CreateSet(pinned))
                .ProjectedState;
            TestAssert.True(source.SemanticallyEquals(reverted),
                "reverting a schedule to its stored linked/pinned state must be semantically empty");

            var clearThenReadd = new WorkloadDraft(source)
                .ClearSchedule(localWorkGiver)
                .SetScheduleIntent(localWorkGiver,
                    WorkloadIntent<WorkloadSchedulePayload>.CreateSet(changedPayload))
                .ProjectedState;
            var readded = TestSupport.Find(
                clearThenReadd.ScheduleIntents,
                entry => entry.Key.Equals(localWorkGiver));
            TestAssert.NotNull(readded, "set-clear-readd must leave a typed schedule intent");
            TestAssert.True(readded.Intent.HasValue && readded.Intent.Value.Equals(changedPayload),
                "set-clear-readd must end with the re-added schedule payload");

            var noOpinion = new WorkloadDraft(source)
                .ClearSchedule(localWorkGiver)
                .SetScheduleNoOpinion(localWorkGiver)
                .ProjectedState;
            TestAssert.True(
                TestSupport.Find(noOpinion.ScheduleIntents, entry => entry.Key.Equals(localWorkGiver)) == null,
                "returning a cleared target to NoOpinion must remove the workload opinion");
            TestAssert.False(source.SemanticallyEquals(noOpinion),
                "removing a stored workload opinion must remain distinct from the stored Set baseline");
        }

        private static void AssertRollbackRegistrationContract()
        {
            string backendPath = FindRepositoryFile(
                Path.Combine(
                    "Source",
                    "Features",
                    "Workloads",
                    "V2",
                    "Runtime",
                    "Workload2Backend.cs"));
            string backend = File.ReadAllText(backendPath);
            int applyTypedLive = backend.IndexOf(
                "private static void ApplyTypedLive(",
                StringComparison.Ordinal);
            int registration = backend.IndexOf(
                "transaction.Schedules.Add(appliedMutation);",
                applyTypedLive,
                StringComparison.Ordinal);
            int writer = backend.IndexOf(
                "TimePriorityService.TryApplyLiveScheduleSnapshot(",
                registration,
                StringComparison.Ordinal);
            int verification = backend.IndexOf(
                "TimePriorityService.TryCaptureLiveScheduleSnapshot(",
                writer,
                StringComparison.Ordinal);
            int observedAssignment = backend.IndexOf(
                "appliedMutation.ObservedSnapshot = observed;",
                verification,
                StringComparison.Ordinal);

            TestAssert.True(
                applyTypedLive >= 0 &&
                registration > applyTypedLive &&
                writer > registration &&
                verification > writer &&
                observedAssignment > verification,
                "schedule rollback entries must be registered before writer and post-write verification");

            int restore = backend.IndexOf(
                "TryRestoreLiveScheduleSnapshot(",
                observedAssignment,
                StringComparison.Ordinal);
            int authorizationUse = backend.IndexOf(
                "applied.Authorization",
                restore,
                StringComparison.Ordinal);
            TestAssert.True(
                restore >= 0 && authorizationUse > restore,
                "schedule rollback must carry the transaction-bound authorization");
        }

        private static string FindRepositoryFile(string relativePath)
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository source contract: " + relativePath);
        }
    }
}
