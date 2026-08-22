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
            new TestCase("reassignment-cleanup", ReassignmentCleanupTests.Run),
            new TestCase("mp-protocol", MpProtocolTests.Run),
            new TestCase("gateway-multiplayer-contracts", GatewayMultiplayerTests.Run),
            new TestCase("gateway-lifecycle-contracts", GatewayLifecycleTests.Run),
            new TestCase("backend-transaction-contracts", BackendTransactionTests.Run)
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
