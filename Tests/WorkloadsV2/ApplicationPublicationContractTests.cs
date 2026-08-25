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

        private static string Read(string root, string fileName) =>
            File.ReadAllText(Path.Combine(
                root,
                "Source",
                "Features",
                "Application",
                fileName));
    }
}
