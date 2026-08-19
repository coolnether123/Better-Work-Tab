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
            GatewayReturnsAcceptanceAndDefersTerminalHandling(gateway);
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

        private static WorkloadMultiplayerCommitStatus Status(
            WorkloadMultiplayerCommitState state)
        {
            return new WorkloadMultiplayerCommitStatus(
                "request",
                state,
                state == WorkloadMultiplayerCommitState.None
                    ? WorkloadDiagnosticCode.InvalidState
                    : WorkloadDiagnosticCode.None,
                "status message");
        }

        private static string FindRepositoryRoot()
        {
            string[] starts =
            {
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory
            };
            for (int startIndex = 0; startIndex < starts.Length; startIndex++)
            {
                string current = Path.GetFullPath(starts[startIndex]);
                for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
                {
                    string gatewayPath = Path.Combine(
                        current,
                        "Source",
                        "UI",
                        "Workloads",
                        "WorkloadGateway.cs");
                    if (File.Exists(gatewayPath))
                    {
                        return current;
                    }

                    DirectoryInfo parent = Directory.GetParent(current);
                    current = parent?.FullName;
                }
            }

            throw new InvalidOperationException(
                "Could not locate the Better Work Tab repository for gateway contracts.");
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
