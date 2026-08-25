using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    /// <summary>
    /// Immutable read model for workload selector and footer queries. The
    /// persistence envelope still owns validation. This catalog only projects
    /// an already validated revision into stable descriptors and ID indexes.
    /// </summary>
    internal sealed class WorkloadV2DescriptorCatalog
    {
        private static readonly IReadOnlyList<WorkloadDescriptor> EmptyDescriptors =
            new List<WorkloadDescriptor>().AsReadOnly();

        private WorkloadV2PersistenceEnvelope _store;
        private List<WorkloadV2PersistenceRecord> _records;
        private int _recordCount = -1;
        private int _persistenceRevision = int.MinValue;
        private long _diagnosticsRevision = long.MinValue;
        private string _currentWorkloadId = string.Empty;
        private IReadOnlyList<WorkloadDescriptor> _descriptors = EmptyDescriptors;
        private WorkloadOperationResult<WorkloadDescriptor> _current =
            WorkloadOperationResult<WorkloadDescriptor>.Fail(
                WorkloadDiagnosticCode.MissingCurrentWorkloadId);

        internal IReadOnlyList<WorkloadDescriptor> Descriptors => _descriptors;
        internal WorkloadOperationResult<WorkloadDescriptor> Current => _current;

        internal void EnsureCurrent(WorkloadV2PersistenceEnvelope store)
        {
            if (store == null)
            {
                _store = null;
                _records = null;
                _recordCount = -1;
                _persistenceRevision = int.MinValue;
                _diagnosticsRevision = long.MinValue;
                _currentWorkloadId = string.Empty;
                _descriptors = EmptyDescriptors;
                _current = WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame);
                return;
            }

            store.EnsureDiagnosticsCurrent();
            List<WorkloadV2PersistenceRecord> records = store.Records;
            int recordCount = records?.Count ?? 0;
            string currentWorkloadId = store.CurrentWorkloadId ?? string.Empty;
            if (ReferenceEquals(_store, store) &&
                ReferenceEquals(_records, records) &&
                _recordCount == recordCount &&
                _persistenceRevision == store.PersistenceRevision &&
                _diagnosticsRevision == store.DiagnosticsRevision &&
                StringComparer.Ordinal.Equals(_currentWorkloadId, currentWorkloadId))
            {
                return;
            }

            Rebuild(store, records, recordCount, currentWorkloadId);
        }

        private void Rebuild(
            WorkloadV2PersistenceEnvelope store,
            List<WorkloadV2PersistenceRecord> records,
            int recordCount,
            string currentWorkloadId)
        {
            var stableIdCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < recordCount; i++)
            {
                string stableId = records[i]?.StableId;
                if (string.IsNullOrEmpty(stableId)) continue;
                stableIdCounts.TryGetValue(stableId, out int count);
                stableIdCounts[stableId] = count + 1;
            }

            var descriptors = new List<WorkloadDescriptor>(recordCount);
            var uniqueByStableId = new Dictionary<string, WorkloadDescriptor>(StringComparer.Ordinal);
            for (int i = 0; i < recordCount; i++)
            {
                WorkloadV2PersistenceRecord record = records[i];
                if (record == null) continue;

                WorkloadDescriptor descriptor = CreateDescriptor(
                    store,
                    record,
                    StringComparer.Ordinal.Equals(currentWorkloadId, record.StableId));
                descriptors.Add(descriptor);
                if (!string.IsNullOrEmpty(record.StableId) &&
                    stableIdCounts.TryGetValue(record.StableId, out int count) &&
                    count == 1)
                {
                    uniqueByStableId.Add(record.StableId, descriptor);
                }
            }

            _store = store;
            _records = records;
            _recordCount = recordCount;
            _persistenceRevision = store.PersistenceRevision;
            _diagnosticsRevision = store.DiagnosticsRevision;
            _currentWorkloadId = currentWorkloadId;
            _descriptors = descriptors.AsReadOnly();
            _current = ResolveCurrent(currentWorkloadId, stableIdCounts, uniqueByStableId);
        }

        private static WorkloadOperationResult<WorkloadDescriptor> ResolveCurrent(
            string currentWorkloadId,
            IReadOnlyDictionary<string, int> stableIdCounts,
            IReadOnlyDictionary<string, WorkloadDescriptor> uniqueByStableId)
        {
            if (string.IsNullOrEmpty(currentWorkloadId))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.MissingCurrentWorkloadId);
            }

            if (stableIdCounts.TryGetValue(currentWorkloadId, out int count) && count > 1)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId);
            }

            return uniqueByStableId.TryGetValue(currentWorkloadId, out WorkloadDescriptor descriptor)
                ? WorkloadOperationResult<WorkloadDescriptor>.Ok(descriptor)
                : WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId);
        }

        internal static WorkloadDescriptor CreateDescriptor(
            WorkloadV2PersistenceEnvelope store,
            WorkloadV2PersistenceRecord record,
            bool isCurrent)
        {
            bool newerRecord = record.SchemaVersion > WorkloadSchema.CurrentVersion;
            bool readOnly = store.IsReadOnlyDiagnostic || newerRecord;
            WorkloadDiagnosticCode code = store.IsReadOnlyDiagnostic
                ? store.DiagnosticCode
                : newerRecord ? WorkloadDiagnosticCode.NewerSchema : WorkloadDiagnosticCode.None;
            return new WorkloadDescriptor(
                WorkloadBackendMode.Modern,
                record.StableId,
                record.Label,
                isCurrent,
                readOnly,
                !string.IsNullOrWhiteSpace(record.StableId),
                record.SchemaVersion,
                code);
        }
    }
}
