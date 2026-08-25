using System;
using Better_Work_Tab.Features.Workloads.V2;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    /// <summary>
    /// Immutable persistence data captured after a store round-trip. The
    /// backend supplies this only after it has verified the authoritative
    /// record and store fingerprint; the recovery seam itself remains pure.
    /// </summary>
    internal sealed class WorkloadPersistenceRecoverySnapshot
    {
        internal WorkloadPersistenceRecoverySnapshot(
            WorkloadTemplate targetTemplate,
            int persistenceRevision,
            string persistenceFingerprint,
            string currentWorkloadId)
        {
            TargetTemplate = targetTemplate;
            PersistenceRevision = persistenceRevision;
            PersistenceFingerprint = persistenceFingerprint ?? string.Empty;
            CurrentWorkloadId = currentWorkloadId ?? string.Empty;
        }

        internal WorkloadTemplate TargetTemplate { get; private set; }
        internal int PersistenceRevision { get; private set; }
        internal string PersistenceFingerprint { get; private set; }
        internal string CurrentWorkloadId { get; private set; }
    }

    /// <summary>
    /// Pure, fail-closed construction of a recovery receipt from an
    /// authoritative persistence snapshot. No session, store, or baseline is
    /// mutated here; the caller must apply the resulting receipt only after
    /// its own backend rekey/round-trip checks succeed.
    /// </summary>
    internal static class WorkloadPersistenceReceiptRecovery
    {
        internal static WorkloadOperationResult<WorkloadPersistenceReceipt> TryRecover(
            WorkloadSession session,
            WorkloadDecisionKind decisionKind,
            string targetStableId,
            string forkLabel,
            int previousPersistenceRevision,
            string previousPersistenceFingerprint,
            WorkloadPersistenceRecoverySnapshot snapshot)
        {
            if (session == null || session.SourceTemplate == null ||
                session.SourceTemplate.Definition == null || snapshot == null ||
                snapshot.TargetTemplate == null ||
                snapshot.TargetTemplate.Definition == null)
            {
                return Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (decisionKind != WorkloadDecisionKind.Update &&
                decisionKind != WorkloadDecisionKind.Fork)
            {
                return Fail(
                    WorkloadDiagnosticCode.UnsupportedDecision);
            }

            string sourceStableId = session.SourceTemplate.StableId ?? string.Empty;
            string sourceIdentity = session.SourceIdentity ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceStableId) ||
                string.IsNullOrWhiteSpace(sourceIdentity) ||
                !StringComparer.Ordinal.Equals(
                    sourceIdentity,
                    WorkloadSession.GetSourceIdentity(session.SourceTemplate)))
            {
                return Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            string safeTargetStableId = targetStableId ?? string.Empty;
            if (decisionKind == WorkloadDecisionKind.Update)
            {
                if (!StringComparer.Ordinal.Equals(safeTargetStableId, sourceStableId))
                {
                    return Fail(
                        WorkloadDiagnosticCode.InvalidState);
                }
            }
            else if (string.IsNullOrWhiteSpace(safeTargetStableId) ||
                     StringComparer.Ordinal.Equals(safeTargetStableId, sourceStableId))
            {
                return Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (previousPersistenceRevision < 0 ||
                string.IsNullOrWhiteSpace(previousPersistenceFingerprint) ||
                snapshot.PersistenceRevision < 0 ||
                string.IsNullOrWhiteSpace(snapshot.PersistenceFingerprint))
            {
                return Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            WorkloadTemplate expectedTarget = decisionKind == WorkloadDecisionKind.Update
                ? session.TargetTemplate
                : BuildForkTarget(session, safeTargetStableId, forkLabel);
            if (expectedTarget == null || expectedTarget.Definition == null)
            {
                return Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }

            string persistedTargetIdentity =
                WorkloadSession.GetSourceIdentity(snapshot.TargetTemplate);
            if (!StringComparer.Ordinal.Equals(
                    snapshot.TargetTemplate.StableId,
                    safeTargetStableId) ||
                !StringComparer.Ordinal.Equals(
                    persistedTargetIdentity,
                    WorkloadSession.GetSourceIdentity(expectedTarget)))
            {
                return Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            bool requiresPostWriteRevision = decisionKind == WorkloadDecisionKind.Fork ||
                !StringComparer.Ordinal.Equals(
                    persistedTargetIdentity,
                    sourceIdentity);
            int expectedRevision = previousPersistenceRevision;
            if (requiresPostWriteRevision)
            {
                if (expectedRevision == int.MaxValue)
                {
                    return Fail(
                        WorkloadDiagnosticCode.PersistenceConflict);
                }

                expectedRevision++;
            }

            if (snapshot.PersistenceRevision != expectedRevision)
            {
                return Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            if (decisionKind == WorkloadDecisionKind.Fork &&
                !StringComparer.Ordinal.Equals(
                    snapshot.CurrentWorkloadId,
                    safeTargetStableId))
            {
                return Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            return WorkloadOperationResult<WorkloadPersistenceReceipt>.Ok(
                new WorkloadPersistenceReceipt(
                    decisionKind,
                    sourceStableId,
                    safeTargetStableId,
                    sourceIdentity,
                    persistedTargetIdentity,
                    snapshot.TargetTemplate,
                    previousPersistenceRevision,
                    previousPersistenceFingerprint,
                    snapshot.PersistenceRevision,
                    snapshot.PersistenceFingerprint,
                    snapshot.CurrentWorkloadId));
        }

        private static WorkloadTemplate BuildForkTarget(
            WorkloadSession session,
            string targetStableId,
            string forkLabel)
        {
            WorkloadSessionDecision decision = session.Fork(targetStableId, forkLabel);
            return decision != null && decision.Accepted
                ? decision.ResultTemplate
                : null;
        }

        private static WorkloadOperationResult<WorkloadPersistenceReceipt> Fail(
            WorkloadDiagnosticCode code)
        {
            return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(code);
        }
    }
}
