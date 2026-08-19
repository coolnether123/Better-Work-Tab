using System;
using System.Collections.Generic;
using System.IO;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    /// <summary>
    /// Deterministic source contracts for the Unity-bound transaction backend.
    /// The standalone runner cannot instantiate RimWorld services, so these
    /// assertions pin the ordering and fail-closed seams that prevent partial
    /// multiplayer writes.
    /// </summary>
    internal static class BackendTransactionTests
    {
        public static void Run()
        {
            string root = FindRepositoryRoot();
            string backend = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "Workload2Backend.cs");
            string contracts = Read(root, "Source", "Features", "Workloads", "V2", "Runtime", "WorkloadBackendContracts.cs");
            string authorization = Read(root, "Source", "Features", "TimePriority", "TimePriorityMutationAuthorization.cs");
            string manager = Read(root, "Source", "Features", "WorkGiverReassignments", "WorkGiverReassignmentManager.cs");
            string data = Read(root, "Source", "Features", "WorkGiverReassignments", "WorkGiverReassignmentData.cs");

            PrepareIsReadOnlyAndExecuteIsCapabilityBound(backend, authorization);
            SpecificJobMutationIsOneAtomicBatch(backend, manager, data);
            SettingsAreRegisteredBeforeWrite(backend);
            RollbackLeaseCannotBecomeSuccessAfterFailure(backend, contracts);
            TemplateWritesUseTheSameTransactionBoundary(backend);
        }

        private static void PrepareIsReadOnlyAndExecuteIsCapabilityBound(
            string backend,
            string authorization)
        {
            int liveApply = backend.IndexOf("ApplyLive(live, runtimePlan", StringComparison.Ordinal);
            int validateOnlyGate = backend.IndexOf(
                "if (executionContext?.ValidateOnly == true)",
                liveApply,
                StringComparison.Ordinal);
            TestAssert.True(
                liveApply >= 0 && validateOnlyGate > liveApply,
                "prepare must build and validate the runtime plan before the live writer and must return through the ValidateOnly gate");
            TestAssert.Contains(
                backend,
                "runtimePlan.HasLiveMutations && executionContext?.ValidateOnly != true",
                "ValidateOnly must never call ApplyLive");
            TestAssert.Contains(
                backend,
                "TryCreateForWorkload(",
                "execute must mint the opaque capability through the workload backend only");
            TestAssert.Contains(
                backend,
                "IsExecutionCapabilityBound(",
                "execute must revalidate the prepared capability inside the synchronized operation");
            TestAssert.Contains(
                authorization,
                "ReferenceEquals(_requestIdentity, requestIdentity)",
                "capability validation must bind object identity, not only copied strings");
            TestAssert.Contains(
                authorization,
                "StringComparer.Ordinal.Equals(_sourceTemplateFingerprint, sourceTemplateFingerprint",
                "capability validation must bind the source template fingerprint");
            TestAssert.Contains(
                authorization,
                "StringComparer.Ordinal.Equals(_hostSessionEpoch, hostSessionEpoch",
                "capability validation must bind the multiplayer host epoch");
            TestAssert.Contains(
                authorization,
                "StringComparer.Ordinal.Equals(_rosterFingerprint, rosterFingerprint",
                "capability validation must bind the synchronized roster");
        }

        private static void SpecificJobMutationIsOneAtomicBatch(
            string backend,
            string manager,
            string data)
        {
            TestAssert.Contains(
                backend,
                "TryApplySpecificJobBatch(",
                "the workload backend must enter the canonical specific-job batch seam");
            TestAssert.Contains(
                manager,
                "TryApplyWorkloadSpecificJobBatch(",
                "the manager must expose one batch API for shared and pawn-local specific-job state");
            TestAssert.Contains(
                manager,
                "WorkGiverReassignmentData previousData = data.Clone();",
                "the batch must retain one complete rollback snapshot before its first write");
            TestAssert.Contains(
                manager,
                "TryValidateSpecificPriorityBatch(",
                "all specific-job priorities must validate before the batch writes");
            TestAssert.Contains(
                manager,
                "TryValidateSpecificOrderBatch(",
                "all specific-job orders must validate before the batch writes");
            TestAssert.Contains(
                manager,
                "data.CopyFrom(previousData, previousRevision);",
                "a failed batch must restore the exact pre-write data clone");
            TestAssert.Contains(
                manager,
                "data.SyncVersion = unchecked(previousRevision + 1);",
                "the batch must publish one canonical revision for all specific-job dimensions");
            TestAssert.Contains(
                manager,
                "TryRestoreWorkloadSpecificJobBatch(",
                "rollback must restore the complete specific-job batch, not individual legacy sync entries");
            TestAssert.False(
                backend.IndexOf("SetPawnOverrideSynced(", StringComparison.Ordinal) >= 0 ||
                backend.IndexOf("ClearPawnOverrideSynced(", StringComparison.Ordinal) >= 0 ||
                backend.IndexOf("SetPawnWorkGiverOrderExact(", StringComparison.Ordinal) >= 0 ||
                backend.IndexOf("ClearPawnWorkGiverOrderExact(", StringComparison.Ordinal) >= 0,
                "the workload backend must not fall through to per-entry legacy specific-job sync calls");
            TestAssert.Contains(
                data,
                "ComputeStateFingerprint()",
                "the canonical data seam must expose a deterministic state fingerprint for rollback ownership");
            TestAssert.Contains(
                data,
                "GlobalWorkGiverPriorityClears",
                "specific-job batch fingerprints must retain explicit global clear state");
        }

        private static void SettingsAreRegisteredBeforeWrite(string backend)
        {
            int registration = backend.IndexOf(
                "transaction.PresentationWasChanged = true;",
                StringComparison.Ordinal);
            int writer = backend.IndexOf(
                "baseline.SettingsWriter.TryApply(",
                registration,
                StringComparison.Ordinal);
            TestAssert.True(
                registration >= 0 && writer > registration,
                "settings rollback ownership must be registered before the settings writer is invoked");
            TestAssert.Contains(
                backend,
                "IsAcceptedForSettings(",
                "settings writes and rollback must use the same transaction-bound capability gate");
        }

        private static void RollbackLeaseCannotBecomeSuccessAfterFailure(
            string backend,
            string contracts)
        {
            TestAssert.Contains(
                contracts,
                "RollbackFailed = 10",
                "the public transaction status must represent rollback failure separately");
            TestAssert.Contains(
                backend,
                "_state = recoveryRequired ? LeaseState.RollbackFailed : LeaseState.Active",
                "a transaction whose immediate rollback failed must begin in recovery-required state");
            TestAssert.Contains(
                backend,
                "if (_state == LeaseState.RollbackFailed)",
                "a failed rollback lease must retain its failed state across callbacks");
            TestAssert.Contains(
                backend,
                "return false;\n                }\n\n                _state = restored",
                "a second rollback callback must not turn a failed lease into Released");
            TestAssert.Contains(
                backend,
                "state != WorkloadMultiplayerCommitState.RollbackFailed",
                "the protocol callback must retain pending backend state while recovery is required");
            TestAssert.Contains(
                backend,
                "recoveryRequired: true",
                "post-write rollback failure must preserve a recovery lease for the protocol worker");
        }

        private static void TemplateWritesUseTheSameTransactionBoundary(string backend)
        {
            TestAssert.Contains(
                backend,
                "new PersistenceMutation(",
                "Update and Fork must stage template writes as transaction mutations");
            TestAssert.Contains(
                backend,
                "RollbackPersistence(store, persistence, report)",
                "template rollback must be part of the same rollback lease as live state");
            TestAssert.Contains(
                backend,
                "prepared.PlanFingerprint",
                "execute must reject a changed or replayed immutable prepare artifact");
            TestAssert.Contains(
                backend,
                "prepared.ExecuteStarted",
                "the backend must reject duplicate execute use of one prepared transaction");
        }

        private static string FindRepositoryRoot()
        {
            var starts = new List<string>
            {
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory
            };

            for (int startIndex = 0; startIndex < starts.Count; startIndex++)
            {
                string current = Path.GetFullPath(starts[startIndex]);
                for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
                {
                    string backendPath = Path.Combine(
                        current,
                        "Source",
                        "Features",
                        "Workloads",
                        "V2",
                        "Runtime",
                        "Workload2Backend.cs");
                    if (File.Exists(backendPath)) return current;

                    DirectoryInfo parent = Directory.GetParent(current);
                    current = parent?.FullName;
                }
            }

            throw new InvalidOperationException(
                "Could not locate the Better Work Tab repository for backend contracts.");
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
    }
}
