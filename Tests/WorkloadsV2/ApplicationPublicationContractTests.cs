using System;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class ApplicationPublicationContractTests
    {
        public static void Run()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "Application", "WorkTabApplication.cs"),
                "application publication contracts");
            string application = Read(root, "WorkTabApplication.cs");
            string change = Read(root, "WorkTabApplicationChange.cs");
            string publisher = Read(root, "WorkTabApplicationPublisher.cs");
            string staged = Read(root, "WorkTabStagedMutation.cs");

            ChangeCarriesPrecisePublicationData(change);
            PublisherOwnsDownstreamEffects(application, publisher);
            FailureOutcomesDistinguishRecovery(application);
            SpecificJobStorageUsesNeutralAuthority(application, staged);
            ManualModePublicationAvoidsDuplicateRecaches(application, staged);
            StagedRequestsAreSeparatedFromAppliedChanges(application, staged);
            StagedPublicationUsesAppliedState(application, staged);
            FailedRollbackReleasesApplicationAdmission(application, staged);
        }

        private static void ChangeCarriesPrecisePublicationData(string change)
        {
            TestAssert.Contains(change, "internal enum WorkTabApplicationEffects",
                "application changes must identify downstream publication effects");
            TestAssert.Contains(change, "WorkTabRevisionVector BeforeRevisionVector",
                "application changes must carry a before revision vector");
            TestAssert.Contains(change, "WorkTabRevisionVector AfterRevisionVector",
                "application changes must carry an after revision vector");
            TestAssert.Contains(change, "IReadOnlyList<WorkTabApplicationTargetChange> AffectedTargets",
                "application changes must carry precise affected targets");
            TestAssert.Contains(change, "copied.Sort(CompareTargetChanges)",
                "affected targets must be deterministic before publication");
            TestAssert.Contains(change, "CompareTargetChanges(copied[i - 1], copied[i]) != 0",
                "duplicate affected targets must not reach the publisher");
        }

        private static void PublisherOwnsDownstreamEffects(
            string application,
            string publisher)
        {
            TestAssert.Contains(application, "return _publisher.Publish(",
                "the application must delegate accepted change publication");
            TestAssert.False(
                application.IndexOf("WorkTabInvalidationHub.", StringComparison.Ordinal) >= 0,
                "mutation execution must not publish invalidation directly");
            TestAssert.False(
                application.IndexOf("MainTabWindowUtility.NotifyAllPawnTables", StringComparison.Ordinal) >= 0,
                "mutation execution must not publish pawn-table recaches directly");
            TestAssert.False(
                application.IndexOf("TimePriorityService.NotifyExternalMirror", StringComparison.Ordinal) >= 0,
                "mutation execution must not publish external mirrors directly");

            TestAssert.Contains(publisher, "change.Effects",
                "the publisher must consume the effects recorded on the change");
            TestAssert.Contains(publisher, "WorkTabInvalidationHub.InvalidatePriority(",
                "exact parent changes must support sparse priority invalidation");
            TestAssert.Contains(publisher, "WorkExecutionOrder.MarkAllPawnsWorkGiversDirty()",
                "the publisher must preserve execution recache behavior");
            TestAssert.Contains(publisher, "layout.InvalidateRowDescriptors()",
                "the publisher must preserve row layout refresh behavior");
            TestAssert.Contains(publisher, "MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged()",
                "the publisher must preserve pawn-table refresh behavior");
            TestAssert.Contains(publisher, "TimePriorityService.NotifyExternalMirror(",
                "the publisher must preserve external mirror behavior");
        }

        private static void FailureOutcomesDistinguishRecovery(string application)
        {
            TestAssert.Contains(application, "FailedRolledBack",
                "a failed mutation with verified rollback needs a distinct outcome");
            TestAssert.Contains(application, "RecoveryRequired",
                "a failed mutation with unverified residual state needs a distinct outcome");
        }

        private static void SpecificJobStorageUsesNeutralAuthority(
            string application,
            string staged)
        {
            TestAssert.Contains(staged, "SpecificJobPriorityAuthorityAdapter.TryRead(",
                "application baselines must use the neutral specific-job authority adapter");
            TestAssert.Contains(staged, "SpecificJobPriorityAuthorityAdapter.TryWriteExternal(",
                "external specific-job writes must use the neutral authority adapter");
            TestAssert.Contains(staged, "SpecificJobPriorityAuthorityAdapter.TryRestoreExternal(",
                "external specific-job rollback must use the neutral authority adapter");
            TestAssert.False(
                application.IndexOf("SleekWorkTabGateway", StringComparison.Ordinal) >= 0 ||
                staged.IndexOf("SleekWorkTabGateway", StringComparison.Ordinal) >= 0,
                "the application boundary must not name an optional specific-job provider");
        }

        private static void ManualModePublicationAvoidsDuplicateRecaches(
            string application,
            string staged)
        {
            TestAssert.Contains(
                application,
                "ManualPriorityMode = 128",
                "manual-mode changes need a distinct application dimension");
            TestAssert.Contains(
                application,
                "Publish(default, WorkTabApplicationDimensions.ManualPriorityMode",
                "the direct manual-mode command must publish its own dimension");
            TestAssert.Contains(
                application,
                "nonManualDimensions = dimensions &",
                "downstream effects must distinguish manual-only from combined changes");
            TestAssert.Contains(
                application,
                "nonManualDimensions != WorkTabApplicationDimensions.None",
                "combined mutations must retain their non-manual table refresh");

            TestAssert.Contains(
                staged,
                "_appliedDimensions |= WorkTabApplicationDimensions.ManualPriorityMode",
                "staged manual-mode writes must publish the manual dimension");
            TestAssert.Contains(
                staged,
                "_mutation.ManualPriorityModeChanged = true",
                "the staged receipt must record the writer-owned notifier boundary");
        }

        private static void StagedRequestsAreSeparatedFromAppliedChanges(
            string application,
            string staged)
        {
            int requestStart = staged.IndexOf(
                "internal bool HasRequests =>",
                StringComparison.Ordinal);
            TestAssert.True(
                requestStart >= 0,
                "staged mutation must expose a request boundary");

            int receiptStart = staged.IndexOf(
                "internal sealed class WorkTabStagedMutationReceipt",
                requestStart,
                StringComparison.Ordinal);
            TestAssert.True(
                receiptStart > requestStart,
                "staged request state must end before the applied receipt");
            string requests = staged.Substring(requestStart, receiptStart - requestStart);
            TestAssert.Contains(
                requests,
                "ManualPriorityTarget.HasValue",
                "manual-mode targets must remain admissible for baseline validation");
            TestAssert.False(
                staged.IndexOf("internal bool HasChanges =>", StringComparison.Ordinal) >= 0,
                "a dead staged change flag must not preserve request-only semantics");
            TestAssert.Contains(
                staged,
                "internal bool HasAppliedChanges =>",
                "the receipt must report writer-confirmed changes separately");
            TestAssert.Contains(
                staged,
                "HasAppliedChanges || commit.ScheduleChanged",
                "publication must require an applied staged change");
            TestAssert.Contains(
                application,
                "if (!staged.HasRequests)",
                "atomic execution must still admit idempotent targets for validation");
            TestAssert.Contains(
                staged,
                "!mutation.HasRequests",
                "direct staging must validate request presence rather than a derived change flag");
            TestAssert.False(
                requests.IndexOf("AffectedTargets", StringComparison.Ordinal) >= 0,
                "request admission must not retain a request-based publication target list");
            TestAssert.False(
                requests.IndexOf("internal WorkTabApplicationDimensions Dimensions", StringComparison.Ordinal) >= 0,
                "request admission must not expose request-based publication dimensions");
            TestAssert.Contains(
                application,
                "receipt.Dimensions",
                "recovery publication must include only dimensions proven by staging");
        }

        private static void StagedPublicationUsesAppliedState(
            string application,
            string staged)
        {
            TestAssert.Contains(
                staged,
                "_appliedDimensions | _mutation.AdditionalPublicationDimensions",
                "staged publication dimensions must come from writer-confirmed state");
            TestAssert.Contains(
                staged,
                "IReadOnlyList<WorkTabApplicationTargetChange> ChangedTargets",
                "specific-job rollback must return neutral changed targets");
            TestAssert.Contains(
                staged,
                "_appliedTargetChanges",
                "staged receipts must retain only effective target identities");
            TestAssert.Contains(
                staged,
                "PublishStagedMutation(this)",
                "publication must consume the receipt rather than request lists");
            TestAssert.Contains(
                application,
                "changedCount = receipt.AppliedChangeCount",
                "atomic results must report writer-confirmed change counts");
            TestAssert.Contains(
                application,
                "WorkTabApplicationDimensions.PriorityConfiguration",
                "priority configuration must have an explicit publication dimension");
            TestAssert.Contains(
                application,
                "WorkTabApplicationDimensions.PriorityConfiguration)) != 0",
                "priority configuration must invalidate effective priority presentation");
        }

        private static void FailedRollbackReleasesApplicationAdmission(
            string application,
            string staged)
        {
            TestAssert.Contains(
                staged,
                "ReleaseAfterRollbackFailure();",
                "failed staged rollback must release the application admission lock");
            TestAssert.Contains(
                staged,
                "scope?.Dispose();",
                "failed staged rollback must dispose its active mutation scope");
            TestAssert.Contains(
                staged,
                "_application.ReleaseStagedMutation();",
                "failed staged rollback must release the owning application admission");
            TestAssert.Contains(
                staged,
                "_recoveryRequired = true;\n                ReleaseAfterRollbackFailure();",
                "recovery-required state must be recorded before admission is released");
            TestAssert.Contains(
                staged,
                "if (_scope == null)\n                return !_recoveryRequired;",
                "a released recovery receipt must remain observable without retaining the lock");
            TestAssert.Contains(
                application,
                "Current.ExecuteAtomicMutationPlan(mutation, true, out _)",
                "synchronized replay must use the same recovery-safe executor");
        }

        private static string Read(string root, string fileName) =>
            File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "Application",
                fileName));
    }
}
