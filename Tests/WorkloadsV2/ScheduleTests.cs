using System;
using System.IO;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
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

            WorkloadMutationAuthorization authorization;
            string authorizationReason;
            object requestIdentity = new object();
            object sessionIdentity = new object();
            TestAssert.True(
                WorkloadMutationAuthorization.TryCreateForWorkload(
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
                authorization.IsAcceptedForSchedule(true, 8L, 17),
                "a synchronized schedule transaction with a stale authority must fail closed");
            TestAssert.True(
                authorization.IsAcceptedForSchedule(true, 7L, 17),
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
                authorization.IsAcceptedForSchedule(false, 7L, 17),
                "ordinary non-multiplayer schedule calls must retain legacy authorization behavior");
            TestAssert.True(
                authorization.IsAcceptedForScheduleRollback(true, 7L, 17),
                "rollback authorization remains bound to the transaction's captured schedule revision");
            TestAssert.False(
                authorization.IsAcceptedForScheduleRollback(true, 7L, 18),
                "rollback authorization must not infer ownership from a fixed revision window");
            TestAssert.False(
                authorization.IsAcceptedForScheduleRollback(true, 8L, 17),
                "rollback must reject a different priority authority even at the expected schedule revision");

            var revisionReceipt = new WorkloadScheduleRevisionReceipt(17);
            TestAssert.True(revisionReceipt.Owns(17),
                "a rollback revision receipt starts at the transaction's captured revision");
            TestAssert.True(revisionReceipt.AcceptCommit(true, 18) && revisionReceipt.Owns(18),
                "the provisional schedule publication advances exact transaction ownership once");
            TestAssert.True(revisionReceipt.AcceptCommit(true, 19) && revisionReceipt.Owns(19),
                "a partially successful rollback publication advances ownership for a recovery retry");
            TestAssert.False(revisionReceipt.AcceptCommit(true, 21),
                "a rollback receipt must reject a revision not produced by its next commit");
            TestAssert.True(revisionReceipt.Owns(19),
                "a rejected revision advance must preserve the last exact transaction-owned revision");

            AssertRollbackRegistrationContract();
            AssertObservedScheduleSnapshotContract();
            AssertObservedParentReadContract();
            AssertApplicationScheduleCommitContract();

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
                "WorkloadTimePriorityAdapter.TryApplyLiveScheduleSnapshot(",
                registration,
                StringComparison.Ordinal);
            int verification = backend.IndexOf(
                "WorkloadTimePriorityAdapter.TryCaptureLiveScheduleSnapshot(",
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
                "WorkloadTimePriorityAdapter.TryRestoreLiveScheduleSnapshot(",
                observedAssignment,
                StringComparison.Ordinal);
            int authorizationUse = backend.IndexOf(
                "applied.Authorization",
                restore,
                StringComparison.Ordinal);
            TestAssert.True(
                restore >= 0 && authorizationUse > restore,
                "schedule rollback must carry the transaction-bound authorization");

            int batchedRestore = backend.IndexOf(
                "private static bool RestoreSchedules(",
                StringComparison.Ordinal);
            int rollbackBatch = backend.IndexOf(
                "TimePriorityService.BeginMutationBatch()",
                batchedRestore,
                StringComparison.Ordinal);
            int rollbackWriter = backend.IndexOf(
                "RestoreSchedule(",
                rollbackBatch,
                StringComparison.Ordinal);
            int rollbackCommit = backend.IndexOf(
                "CommitAndPublishScheduleMutation(transaction, out bool revisionOwned)",
                rollbackWriter,
                StringComparison.Ordinal);
            int rollbackPublish = backend.IndexOf(
                "PublishScheduleMutation(",
                rollbackWriter,
                StringComparison.Ordinal);
            TestAssert.True(
                batchedRestore >= 0 && rollbackBatch > batchedRestore &&
                rollbackWriter > rollbackBatch && rollbackCommit > rollbackWriter &&
                rollbackPublish > rollbackWriter,
                "schedule rollback must batch, commit, and publish its restored state");
        }

        private static void AssertObservedScheduleSnapshotContract()
        {
            string service = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "TimePriority", "TimePriorityService.cs")));
            string value = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "TimePriority", "TimePriorityScheduleValue.cs")));
            TestAssert.Contains(service,
                "IDictionary<TimePriorityCacheKey, TimePriorityScheduleValue> _schedules",
                "Observed schedule reads must not retain mutable TimePriorityScheduleData references.");
            TestAssert.Contains(service,
                "captured[entry.Key] = entry.Value;",
                "Observed schedule capture must materialize immutable schedule values once per pass.");
            TestAssert.Contains(value,
                "_priorities = new int[HourCount];",
                "The immutable schedule value must copy its source priorities before capture can share it.");
            TestAssert.Contains(value,
                "if (count < HourCount)",
                "The immutable schedule value must keep a bounded private 24-hour copy.");
            TestAssert.Contains(service,
                "WorkTypeReadSnapshotVersion == version",
                "Observed schedule capture must reuse one immutable view while the service version is stable.");
            TestAssert.Contains(service,
                "if (version != CurrentVersion) return WorkTypeScheduleReadSnapshot.Empty;",
                "Observed schedule capture must reject a snapshot that raced a schedule mutation.");
            TestAssert.False(service.Contains(
                    "IDictionary<TimePriorityCacheKey, TimePriorityScheduleData> _schedules"),
                "A post-capture SetOverride/ClearOverride must not alter an observed schedule snapshot.");
        }

        private static void AssertObservedParentReadContract()
        {
            string parentRead = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "RaisedPriorityMaximum", "ParentPriorityRead.cs")));
            string broker = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "RaisedPriorityMaximum", "PriorityAuthorityBroker.cs")));
            TestAssert.Contains(parentRead,
                "bool? manualMode = CapturedManualModeFor(target);",
                "Observed parent reads must capture manual mode before any fallback path.");
            TestAssert.Contains(parentRead,
                "return ReadStored(pawn, workType, manualMode);",
                "Every observed parent fallback must use captured manual mode rather than rereading PlaySettings.");
            TestAssert.Contains(parentRead,
                "[ThreadStatic] private static Scope currentObservedScope;",
                "Observed parent reads and writes must share one linked scope frame.");
            TestAssert.Contains(parentRead,
                "while (scope != null && scope._disposed) scope = scope._parent;",
                "Out-of-order observed-scope disposal must skip disposed parents instead of resurrecting them.");
            TestAssert.Contains(parentRead,
                "ObservedPass.Capture(null).Read(pawn, workType)",
                "The observational-live parent read must bypass any ambient preview scope.");
            TestAssert.Contains(broker,
                "bool? manualMode",
                "The broker must accept the captured nullable manual-mode value without rereading PlaySettings.");

            string backend = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs")));
            TestAssert.Contains(backend.Replace("\r\n", "\n"),
                "? ParentPriorityRead.GetLive(pawn, workType)\n                    : ParentPriorityRead.GetObservationalLive(pawn, workType);",
                "Live-baseline capture must use the observational reader while template capture keeps the handoff-owning live reader.");

            string gateway = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "UI", "WorkGrid", "Commands", "WorkPriorityCommandGateway.cs")));
            TestAssert.Contains(gateway,
                "WorkTabEffectiveStateDimension.ParentPriority",
                "External parent-priority write rejection must retain its blocked-feedback dimension.");
            TestAssert.Contains(gateway,
                "ExternalPriorityAuthorityReason",
                "External parent-priority write rejection must retain its explanatory feedback.");
        }

        private static void AssertApplicationScheduleCommitContract()
        {
            string application = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "Application", "WorkTabApplication.cs")));
            string service = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "TimePriority", "TimePriorityService.cs")));
            string projection = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "UI", "Schedule", "ScheduleProjection.cs")));
            string priorityPatch = File.ReadAllText(FindRepositoryFile(Path.Combine(
                "Source", "Features", "Patches", "Patch_Pawn_WorkSettings_SetPriority.cs")));
            int apply = application.IndexOf("private WorkTabApplicationResult ApplySchedule(", StringComparison.Ordinal);
            int fullDay = application.IndexOf("private WorkTabApplicationResult SubmitFullDay(", apply, StringComparison.Ordinal);
            string ordinaryWrite = apply >= 0 && fullDay > apply
                ? application.Substring(apply, fullDay - apply)
                : string.Empty;
            int batch = ordinaryWrite.IndexOf("TimePriorityService.BeginMutationBatch()", StringComparison.Ordinal);
            int commit = ordinaryWrite.IndexOf("TimePriorityService.CommitMutationBatch()", StringComparison.Ordinal);
            int publish = ordinaryWrite.IndexOf("Publish(expected.Target, WorkTabApplicationDimensions.Schedule", StringComparison.Ordinal);
            TestAssert.True(
                batch >= 0 && commit > batch && publish > commit,
                "an ordinary schedule write must batch, materialize/revise through commit, then publish once");

            int complete = service.IndexOf("private static bool CompleteMutationBatch()", StringComparison.Ordinal);
            int nextMember = service.IndexOf("/// <summary>", complete + 1, StringComparison.Ordinal);
            string completion = complete >= 0 && nextMember > complete
                ? service.Substring(complete, nextMember - complete)
                : string.Empty;
            int materialize = completion.IndexOf("MaterializeScheduleProjection();", StringComparison.Ordinal);
            int revision = completion.IndexOf("AdvanceScheduleRevision();", StringComparison.Ordinal);
            TestAssert.True(
                materialize >= 0 && revision > materialize,
                "schedule commit must materialize the save projection before advancing its revision");

            TestAssert.False(application.Contains("ApplyPreview"),
                "the live application must not own preview mutations");
            TestAssert.Contains(projection, "ApplyFullDayPreviewParent(",
                "the UI schedule projection must own preview full-day mutation composition");
            TestAssert.Contains(application, "WorkTabApplicationChange Change",
                "applied full-day results must carry a typed coherent application change");
            TestAssert.Contains(application, "WorkTabApplicationRevision Revision",
                "applied full-day results must carry the per-world application revision");
            TestAssert.Contains(application, "WorkTabApplicationRevision(_epoch, _revision)",
                "the application revision must use its component-provided epoch");
            TestAssert.Contains(application, "using (ExternalPriorityMirror.Suspend())",
                "application-owned parent writes must defer external mirroring to the application publication");
            TestAssert.Contains(priorityPatch, "WorkTabApplication.OwnsCurrentParentPrioritySetter",
                "the vanilla setter patch must defer application-owned invalidation to the coherent application publication");

            int moveRuntime = service.IndexOf(
                "MoveWorkGiverScheduleKeys(runtime, legacy, runtime",
                StringComparison.Ordinal);
            int moveLegacy = service.IndexOf(
                "MoveWorkGiverScheduleKeys(runtime, legacy, legacy",
                StringComparison.Ordinal);
            TestAssert.True(moveRuntime >= 0 && moveLegacy > moveRuntime,
                "reassignment rekeying must normalize the runtime map before legacy rows");
            TestAssert.Contains(service, "source[key] = value;",
                "a conflicting load rekey must retain the original row rather than overwrite it");
            TestAssert.Contains(service, "ReplaceSchedules(RuntimeSchedules, plan.Runtime);",
                "a prepared reassignment must atomically install its validated schedule maps");
        }

        private static string FindRepositoryFile(string relativePath)
        {
            string root = TestSupport.FindRepositoryRoot(
                relativePath,
                "the schedule source contract");
            return Path.Combine(root, relativePath);
        }
    }
}
