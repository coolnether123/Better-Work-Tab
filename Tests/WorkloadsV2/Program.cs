using System;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class Program
    {
        private static readonly TestCase[] Cases =
        {
            new TestCase("schedules", ScheduleTests.Run),
            new TestCase("projection-and-settings", ProjectionAndSettingsTests.Run),
            new TestCase("priority-cycle", PriorityCycleMathTests.Run),
            new TestCase("specific-jobs-and-tombstones", SpecificJobTests.Run),
            new TestCase("session-lifecycle", SessionTests.Run),
            new TestCase("persistence-receipt-recovery", PersistenceReceiptRecoveryTests.Run),
            new TestCase("inspection-semantics", InspectionSemanticsTests.Run),
            new TestCase("persistence-and-migration", PersistenceTests.Run),
            new TestCase("persistence-fingerprint-parity", PersistenceFingerprintParityTests.Run),
            new TestCase("workload-read-catalog-performance", WorkloadReadCatalogPerformanceTests.Run),
            new TestCase("workload-draft-projection-cache", WorkloadDraftProjectionCacheTests.Run),
            new TestCase("workload-pawn-roster-revision-contracts", WorkloadPawnRosterRevisionContractTests.Run),
            new TestCase("work-execution-spawn-performance-contracts", WorkExecutionSpawnPerformanceContractTests.Run),
            new TestCase("work-grid-retained-sparse-audit-performance-contracts", WorkGridRetainedSparseAuditPerformanceContractTests.Run),
            new TestCase("prepared-work-row-packet-performance-contracts", PreparedWorkRowPacketPerformanceContractTests.Run),
            new TestCase("work-grid-snapshot-isolation-contracts", WorkGridSnapshotIsolationContractTests.Run),
            new TestCase("work-grid-snapshot-layout-revision-behavior", WorkGridSnapshotLayoutRevisionBehaviorTests.Run),
            new TestCase("prepared-pawn-label-performance-contracts", PreparedPawnLabelPerformanceContractTests.Run),
            new TestCase("pawn-label-revision-behavior", PawnLabelRevisionBehaviorTests.Run),
            new TestCase("prepared-pawn-label-text", PreparedPawnLabelTextTests.Run),
            new TestCase("tutorial-selector-ownership", TutorialSelectorOwnershipTests.Run),
            new TestCase("tutorial-cancel-behavior", TutorialCancelBehaviorTests.Run),
            new TestCase("shift-wheel-viewport-input-contracts", ShiftWheelViewportInputTests.Run),
            new TestCase("dependency-direction-contracts", DependencyDirectionContractTests.Run),
            new TestCase("workload-feature-isolation-contracts", WorkloadFeatureIsolationContractTests.Run),
            new TestCase("legacy-world-component-dependency-contracts", LegacyWorldComponentDependencyContractTests.Run),
            new TestCase("application-publication-contracts", ApplicationPublicationContractTests.Run),
            new TestCase("reassignment-cleanup", ReassignmentCleanupTests.Run),
            new TestCase("mp-protocol", MpProtocolTests.Run),
            new TestCase("gateway-multiplayer-contracts", GatewayMultiplayerTests.Run),
            new TestCase("gateway-lifecycle-contracts", GatewayLifecycleTests.Run),
            new TestCase("backend-transaction-contracts", BackendTransactionTests.Run),
            new TestCase("workload-performance-instrumentation-contracts", WorkloadPerformanceInstrumentationContractTests.Run),
            new TestCase("presentation-settings-consolidation", PresentationSettingsConsolidationTests.Run),
            new TestCase("chrome-presentation-cache-contracts", ChromePresentationCacheTests.Run),
            new TestCase("layout-history-transaction-contracts", LayoutHistoryTransactionTests.Run),
            new TestCase("presentation-boundary-contracts", PresentationBoundaryTests.Run),
            new TestCase("expand-beside-ownership-contracts", ExpandBesideOwnershipContractTests.Run)
        };

        private static int Main()
        {
            var passed = 0;
            var failed = 0;
            foreach (var test in Cases)
            {
                try
                {
                    test.Body();
                    Console.WriteLine("PASS " + test.Name);
                    passed++;
                }
                catch (Exception error)
                {
                    Console.WriteLine("FAIL " + test.Name + ": " + error.Message);
                    failed++;
                }
            }

            Console.WriteLine("SUMMARY total=" + Cases.Length + " passed=" + passed + " failed=" + failed);
            return failed == 0 ? 0 : 1;
        }

        private sealed class TestCase
        {
            public TestCase(string name, Action body)
            {
                Name = name;
                Body = body;
            }

            public string Name { get; }
            public Action Body { get; }
        }
    }
}
