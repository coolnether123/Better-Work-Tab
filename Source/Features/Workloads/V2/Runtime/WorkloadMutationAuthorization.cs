using System;
using Better_Work_Tab.Foundation.Transactions;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    internal sealed class WorkTabMutationAuthorization
    {
        private readonly object _request;
        private readonly object _session;
        private readonly string _transactionId;
        private readonly string _requestFingerprint;
        private readonly string _sourceTemplateFingerprint;
        private readonly string _projectedTemplateFingerprint;
        private readonly long _sessionRevision;
        private readonly string _authorityOwner;
        private readonly string _hostSessionEpoch;
        private readonly string _rosterFingerprint;

        private WorkTabMutationAuthorization(
            object request,
            object session,
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
            _request = request;
            _session = session;
            _transactionId = transactionId;
            _requestFingerprint = requestFingerprint ?? string.Empty;
            _sourceTemplateFingerprint = sourceTemplateFingerprint ?? string.Empty;
            _projectedTemplateFingerprint = projectedTemplateFingerprint ?? string.Empty;
            _sessionRevision = sessionRevision;
            _authorityOwner = authorityOwner ?? string.Empty;
            _hostSessionEpoch = hostSessionEpoch ?? string.Empty;
            _rosterFingerprint = rosterFingerprint ?? string.Empty;
            Lease = new WorkTabMutationLease(
                authorityRevision,
                specificJobRevision,
                scheduleRevision,
                settingsRevision);
        }

        internal static bool TryCreate(
            object request,
            object session,
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
            out WorkTabMutationAuthorization authorization,
            out string reason)
        {
            authorization = null;
            reason = null;
            if (request == null || session == null)
            {
                reason = "The workload mutation capability is missing its request or session identity.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(transactionId)
                || string.IsNullOrWhiteSpace(requestFingerprint)
                || string.IsNullOrWhiteSpace(sourceTemplateFingerprint)
                || string.IsNullOrWhiteSpace(projectedTemplateFingerprint)
                || string.IsNullOrWhiteSpace(hostSessionEpoch)
                || string.IsNullOrWhiteSpace(rosterFingerprint))
            {
                reason = "The workload mutation capability is missing a stable transaction fingerprint.";
                return false;
            }

            authorization = new WorkTabMutationAuthorization(
                request,
                session,
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
                rosterFingerprint);
            return true;
        }

        internal bool IsUsable => Lease.IsUsable;

        internal bool IsBoundToTransaction(
            object request,
            object session,
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
            return IsUsable
                && ReferenceEquals(_request, request)
                && ReferenceEquals(_session, session)
                && StringComparer.Ordinal.Equals(_transactionId, transactionId)
                && StringComparer.Ordinal.Equals(_requestFingerprint, requestFingerprint ?? string.Empty)
                && StringComparer.Ordinal.Equals(_sourceTemplateFingerprint, sourceTemplateFingerprint ?? string.Empty)
                && StringComparer.Ordinal.Equals(_projectedTemplateFingerprint, projectedTemplateFingerprint ?? string.Empty)
                && _sessionRevision == sessionRevision
                && Lease.AuthorityRevision == authorityRevision
                && StringComparer.Ordinal.Equals(_authorityOwner, authorityOwner ?? string.Empty)
                && Lease.SpecificJobRevision == specificJobRevision
                && Lease.ScheduleRevision == scheduleRevision
                && Lease.SettingsRevision == settingsRevision
                && StringComparer.Ordinal.Equals(_hostSessionEpoch, hostSessionEpoch ?? string.Empty)
                && StringComparer.Ordinal.Equals(_rosterFingerprint, rosterFingerprint ?? string.Empty);
        }

        internal string TransactionId => _transactionId;
        internal long AuthorityRevision => Lease.AuthorityRevision;
        internal string AuthorityOwner => _authorityOwner;
        internal int SpecificJobRevision => Lease.SpecificJobRevision;
        internal int ScheduleRevision => Lease.ScheduleRevision;
        internal int SettingsRevision => Lease.SettingsRevision;
        internal string SourceTemplateFingerprint => _sourceTemplateFingerprint;
        internal string RequestFingerprint => _requestFingerprint;
        internal string ProjectedTemplateFingerprint => _projectedTemplateFingerprint;
        internal long SessionRevision => _sessionRevision;
        internal string HostSessionEpoch => _hostSessionEpoch;
        internal string RosterFingerprint => _rosterFingerprint;
        internal WorkTabMutationLease Lease { get; }
    }
}
