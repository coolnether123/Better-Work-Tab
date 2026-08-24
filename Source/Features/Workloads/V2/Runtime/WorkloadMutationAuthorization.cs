using System;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    /// <summary>
    /// Opaque transaction capability owned by the workload runtime.  It keeps
    /// workload request identity, replay fingerprints, and rollback scope out
    /// of individual feature domains.
    /// </summary>
    internal sealed class WorkloadMutationAuthorization
    {
        private readonly object _requestIdentity;
        private readonly object _sessionIdentity;
        private readonly string _transactionId;
        private readonly string _requestFingerprint;
        private readonly string _sourceTemplateFingerprint;
        private readonly string _projectedTemplateFingerprint;
        private readonly long _sessionRevision;
        private readonly long _authorityRevision;
        private readonly string _authorityOwner;
        private readonly int _specificJobRevision;
        private readonly int _scheduleRevision;
        private readonly int _settingsRevision;
        private readonly string _hostSessionEpoch;
        private readonly string _rosterFingerprint;
        private bool _executionActive;
        private bool _finalized;

        private WorkloadMutationAuthorization(
            object requestIdentity,
            object sessionIdentity,
            string transactionId,
            string requestFingerprint,
            string sourceTemplateFingerprint,
            string projectedTemplateFingerprint,
            long sessionRevision,
            long authorityRevision,
            string authorityOwner,
            int specificJobRevision,
            int scheduleRevision,
            int settingsRevision,
            string hostSessionEpoch,
            string rosterFingerprint)
        {
            _requestIdentity = requestIdentity;
            _sessionIdentity = sessionIdentity;
            _transactionId = transactionId;
            _requestFingerprint = requestFingerprint ?? string.Empty;
            _sourceTemplateFingerprint = sourceTemplateFingerprint ?? string.Empty;
            _projectedTemplateFingerprint = projectedTemplateFingerprint ?? string.Empty;
            _sessionRevision = sessionRevision;
            _authorityRevision = authorityRevision;
            _authorityOwner = authorityOwner ?? string.Empty;
            _specificJobRevision = specificJobRevision;
            _scheduleRevision = scheduleRevision;
            _settingsRevision = settingsRevision;
            _hostSessionEpoch = hostSessionEpoch ?? string.Empty;
            _rosterFingerprint = rosterFingerprint ?? string.Empty;
        }

        internal static bool TryCreateForWorkload(
            object requestIdentity,
            object sessionIdentity,
            string transactionId,
            string requestFingerprint,
            string sourceTemplateFingerprint,
            string projectedTemplateFingerprint,
            long sessionRevision,
            long authorityRevision,
            string authorityOwner,
            int specificJobRevision,
            int scheduleRevision,
            int settingsRevision,
            string hostSessionEpoch,
            string rosterFingerprint,
            out WorkloadMutationAuthorization authorization,
            out string reason)
        {
            authorization = null;
            reason = null;
            if (requestIdentity == null || sessionIdentity == null)
            {
                reason = "The workload mutation capability is missing its request or session identity.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(transactionId) ||
                string.IsNullOrWhiteSpace(requestFingerprint) ||
                string.IsNullOrWhiteSpace(sourceTemplateFingerprint) ||
                string.IsNullOrWhiteSpace(projectedTemplateFingerprint) ||
                string.IsNullOrWhiteSpace(hostSessionEpoch) ||
                string.IsNullOrWhiteSpace(rosterFingerprint))
            {
                reason = "The workload mutation capability is missing a stable transaction fingerprint.";
                return false;
            }

            authorization = new WorkloadMutationAuthorization(
                requestIdentity,
                sessionIdentity,
                transactionId,
                requestFingerprint,
                sourceTemplateFingerprint,
                projectedTemplateFingerprint,
                sessionRevision,
                authorityRevision,
                authorityOwner,
                specificJobRevision,
                scheduleRevision,
                settingsRevision,
                hostSessionEpoch,
                rosterFingerprint)
            {
                _executionActive = true
            };
            return true;
        }

        internal bool IsBoundTo(long authorityRevision)
        {
            return _authorityRevision == authorityRevision && IsUsable;
        }

        internal bool IsUsable => _executionActive && !_finalized;

        internal bool IsBoundToTransaction(
            object requestIdentity,
            object sessionIdentity,
            string transactionId,
            string requestFingerprint,
            string sourceTemplateFingerprint,
            string projectedTemplateFingerprint,
            long sessionRevision,
            long authorityRevision,
            string authorityOwner,
            int specificJobRevision,
            int scheduleRevision,
            int settingsRevision,
            string hostSessionEpoch,
            string rosterFingerprint)
        {
            return IsUsable &&
                   ReferenceEquals(_requestIdentity, requestIdentity) &&
                   ReferenceEquals(_sessionIdentity, sessionIdentity) &&
                   StringComparer.Ordinal.Equals(_transactionId, transactionId) &&
                   StringComparer.Ordinal.Equals(_requestFingerprint, requestFingerprint ?? string.Empty) &&
                   StringComparer.Ordinal.Equals(_sourceTemplateFingerprint, sourceTemplateFingerprint ?? string.Empty) &&
                   StringComparer.Ordinal.Equals(_projectedTemplateFingerprint, projectedTemplateFingerprint ?? string.Empty) &&
                   _sessionRevision == sessionRevision &&
                   _authorityRevision == authorityRevision &&
                   StringComparer.Ordinal.Equals(_authorityOwner, authorityOwner ?? string.Empty) &&
                   _specificJobRevision == specificJobRevision &&
                   _scheduleRevision == scheduleRevision &&
                   _settingsRevision == settingsRevision &&
                   StringComparer.Ordinal.Equals(_hostSessionEpoch, hostSessionEpoch ?? string.Empty) &&
                   StringComparer.Ordinal.Equals(_rosterFingerprint, rosterFingerprint ?? string.Empty);
        }

        internal bool IsAcceptedForSchedule(
            bool synchronizedExecution,
            long authorityRevision,
            int scheduleRevision)
        {
            return !synchronizedExecution
                ? !_finalized
                : IsUsable &&
                  _authorityRevision == authorityRevision &&
                  _scheduleRevision == scheduleRevision;
        }

        internal bool IsAcceptedForScheduleRollback(
            bool synchronizedExecution,
            long authorityRevision,
            int transactionScheduleRevision)
        {
            return !synchronizedExecution
                ? !_finalized
                : IsUsable &&
                  _authorityRevision == authorityRevision &&
                  _scheduleRevision == transactionScheduleRevision;
        }

        internal bool IsAcceptedForSpecificBatch(
            bool synchronizedExecution,
            long authorityRevision,
            int specificJobRevision)
        {
            return !synchronizedExecution
                ? !_finalized
                : IsUsable &&
                  _authorityRevision == authorityRevision &&
                  _specificJobRevision == specificJobRevision;
        }

        internal bool IsAcceptedForSettings(
            bool synchronizedExecution,
            long authorityRevision,
            int settingsRevision)
        {
            return !synchronizedExecution
                ? !_finalized
                : IsUsable &&
                  _authorityRevision == authorityRevision &&
                  _settingsRevision == settingsRevision;
        }

        internal void FinalizeSuccess()
        {
            _finalized = true;
            _executionActive = false;
        }

        internal void FinalizeRollback()
        {
            _finalized = true;
            _executionActive = false;
        }

        internal string TransactionId => _transactionId;
        internal long AuthorityRevision => _authorityRevision;
        internal string AuthorityOwner => _authorityOwner;
        internal int SpecificJobRevision => _specificJobRevision;
        internal int ScheduleRevision => _scheduleRevision;
        internal int SettingsRevision => _settingsRevision;
        internal string SourceTemplateFingerprint => _sourceTemplateFingerprint;
        internal string RequestFingerprint => _requestFingerprint;
        internal string ProjectedTemplateFingerprint => _projectedTemplateFingerprint;
        internal long SessionRevision => _sessionRevision;
        internal string HostSessionEpoch => _hostSessionEpoch;
        internal string RosterFingerprint => _rosterFingerprint;
    }

    internal sealed class WorkloadScheduleRevisionReceipt
    {
        internal WorkloadScheduleRevisionReceipt(int initialRevision)
        {
            InitialRevision = initialRevision;
            OwnedRevision = initialRevision;
        }

        internal int InitialRevision { get; }
        internal int OwnedRevision { get; private set; }
        internal bool Owns(int revision) => revision == OwnedRevision;

        internal bool AcceptCommit(bool changed, int observedRevision)
        {
            int expected = changed ? unchecked(OwnedRevision + 1) : OwnedRevision;
            if (observedRevision != expected)
            {
                return false;
            }

            OwnedRevision = observedRevision;
            return true;
        }
    }
}
