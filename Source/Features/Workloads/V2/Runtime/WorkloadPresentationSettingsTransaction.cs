using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    internal sealed class WorkloadPresentationSnapshotToken
    {
        private string _value = string.Empty;
        private long _refreshedRevision = long.MinValue;

        internal string Value => _value;

        internal bool Observe(WorkloadSession session)
        {
            string value = session?.PreviewStamp ?? string.Empty;
            if (StringComparer.Ordinal.Equals(_value, value))
            {
                return false;
            }

            _value = value;
            _refreshedRevision = long.MinValue;
            return true;
        }

        internal bool NeedsRefresh(long revision) => _refreshedRevision != revision;

        internal void MarkRefreshed(long revision) => _refreshedRevision = revision;
    }

    internal delegate bool WorkloadPresentationScalarReader(
        string settingId,
        out WorkloadScalarValue value);

    internal static class WorkloadPresentationValueCache
    {
        internal static IDictionary<string, WorkloadScalarValue> Capture(
            IEnumerable<string> settingIds,
            IDictionary<string, WorkloadScalarValue> previousValues,
            WorkloadPresentationScalarReader reader,
            WorkloadPresentationScalarReader defaults)
        {
            var values = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            if (settingIds == null)
            {
                return values;
            }

            foreach (string settingId in settingIds)
            {
                if (string.IsNullOrEmpty(settingId) || values.ContainsKey(settingId))
                {
                    continue;
                }

                WorkloadScalarValue value;
                if (TryRead(reader, settingId, out value) ||
                    (previousValues != null && previousValues.TryGetValue(settingId, out value)) ||
                    TryRead(defaults, settingId, out value))
                {
                    values[settingId] = value;
                }
            }

            return values;
        }

        private static bool TryRead(
            WorkloadPresentationScalarReader reader,
            string settingId,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            try
            {
                return reader != null && reader(settingId, out value);
            }
            catch
            {
                return false;
            }
        }
    }

    internal interface IWorkloadPresentationSettingsStore
    {
        object Identity { get; }
        long Revision { get; }

        bool TryRead(string settingId, out WorkloadScalarValue value);
        bool TryCanonicalize(
            string settingId,
            WorkloadScalarValue requested,
            out WorkloadScalarValue canonical,
            out string reason);
        bool TryWrite(
            string settingId,
            WorkloadScalarValue value,
            out string reason);
        long BeginOwnedMutation(IEnumerable<string> settingIds);
        IDisposable BeginWriteLease();
        bool TryPersist(out string reason);
    }

    internal sealed class WorkloadPresentationSettingsSnapshot
    {
        internal WorkloadPresentationSettingsSnapshot(
            object storeIdentity,
            IDictionary<string, WorkloadScalarValue> values,
            long revision)
        {
            StoreIdentity = storeIdentity;
            Values = new Dictionary<string, WorkloadScalarValue>(
                values ?? new Dictionary<string, WorkloadScalarValue>(),
                StringComparer.Ordinal);
            OwnedValues = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            Revision = revision;
        }

        internal readonly object StoreIdentity;
        internal readonly IReadOnlyDictionary<string, WorkloadScalarValue> Values;
        internal readonly Dictionary<string, WorkloadScalarValue> OwnedValues;
        internal readonly long Revision;
        internal long OwnedRevision;
        internal bool RecoveryRequired;
        internal bool HasOwnedValues => OwnedValues.Count > 0;
    }

    internal sealed class WorkloadPresentationSettingsMutationReceipt
    {
        internal readonly bool Succeeded;
        internal readonly bool PersistenceRequested;
        internal readonly bool Persisted;
        internal readonly bool CompensationAttempted;
        internal readonly bool CompensationSucceeded;
        internal readonly IReadOnlyList<string> ChangedSettingIds;
        internal readonly string FailureReason;
        internal readonly bool RecoveryRequired;

        internal WorkloadPresentationSettingsMutationReceipt(
            bool succeeded,
            bool persistenceRequested,
            bool persisted,
            bool compensationAttempted,
            bool compensationSucceeded,
            IReadOnlyList<string> changedSettingIds,
            string failureReason,
            bool recoveryRequired = false)
        {
            Succeeded = succeeded;
            PersistenceRequested = persistenceRequested;
            Persisted = persisted;
            CompensationAttempted = compensationAttempted;
            CompensationSucceeded = compensationSucceeded;
            ChangedSettingIds = changedSettingIds ?? Array.Empty<string>();
            FailureReason = failureReason ?? string.Empty;
            RecoveryRequired = recoveryRequired;
        }
    }

    internal class WorkloadPresentationSettingsTransaction
    {
        private readonly IWorkloadPresentationSettingsStore _store;

        internal WorkloadPresentationSettingsTransaction(
            IWorkloadPresentationSettingsStore store)
        {
            _store = store;
        }

        internal bool TryCapture(
            IEnumerable<string> settingIds,
            out WorkloadPresentationSettingsSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = string.Empty;
            if (_store == null || _store.Identity == null || settingIds == null)
            {
                reason = "The workload presentation settings store is not available.";
                return false;
            }

            var values = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            foreach (string settingId in settingIds)
            {
                if (string.IsNullOrEmpty(settingId) || values.ContainsKey(settingId))
                {
                    continue;
                }

                if (!_store.TryRead(settingId, out WorkloadScalarValue value))
                {
                    reason = "The setting '" + settingId + "' could not be captured.";
                    return false;
                }

                values.Add(settingId, value);
            }

            snapshot = new WorkloadPresentationSettingsSnapshot(
                _store.Identity,
                values,
                _store.Revision);
            return true;
        }

        internal WorkloadPresentationSettingsMutationReceipt TryApply(
            WorkloadPresentationSettingsSnapshot snapshot,
            IReadOnlyDictionary<string, WorkloadScalarValue> values,
            bool persist)
        {
            if (!TryPrepare(
                    snapshot,
                    values,
                    out List<string> keys,
                    out Dictionary<string, WorkloadScalarValue> expected,
                    out string reason))
            {
                return new WorkloadPresentationSettingsMutationReceipt(
                    false, persist, false, false, false, null, reason);
            }

            if (keys.Count == 0)
            {
                return new WorkloadPresentationSettingsMutationReceipt(
                    true, persist, false, false, false, Array.Empty<string>(), string.Empty);
            }

            snapshot.OwnedValues.Clear();
            snapshot.RecoveryRequired = false;
            snapshot.OwnedRevision = _store.BeginOwnedMutation(keys);
            bool persisted = false;
            using (_store.BeginWriteLease())
            {
                try
                {
                    TryWriteExactValues(snapshot, keys, snapshot.Values, expected, true, out reason);

                    if (string.IsNullOrEmpty(reason) && persist &&
                        !_store.TryPersist(out reason))
                    {
                        reason = string.IsNullOrEmpty(reason)
                            ? "The workload presentation settings could not be persisted."
                            : reason;
                    }
                    else if (string.IsNullOrEmpty(reason) && persist)
                    {
                        persisted = true;
                    }
                }
                catch (Exception ex)
                {
                    reason = "The workload presentation apply failed: " + ex.Message;
                }
            }

            if (string.IsNullOrEmpty(reason))
            {
                return new WorkloadPresentationSettingsMutationReceipt(
                    true, persist, persisted, false, false, keys, string.Empty);
            }

            bool attempted = snapshot.HasOwnedValues;
            string compensationReason = string.Empty;
            bool compensated = attempted && TryRestoreOwned(
                snapshot, persist, out _, out compensationReason);
            if (!attempted)
            {
                snapshot.OwnedRevision = 0L;
            }
            else if (!compensated)
            {
                snapshot.RecoveryRequired = true;
                reason += " Compensation stopped: " + compensationReason;
            }

            return new WorkloadPresentationSettingsMutationReceipt(
                false,
                persist,
                false,
                attempted,
                compensated,
                null,
                reason,
                attempted && !compensated);
        }

        internal WorkloadPresentationSettingsMutationReceipt TryRollback(
            WorkloadPresentationSettingsSnapshot snapshot,
            bool persist)
        {
            if (!ValidateSnapshot(snapshot, out string reason) || snapshot.RecoveryRequired)
            {
                return new WorkloadPresentationSettingsMutationReceipt(
                    false,
                    persist,
                    false,
                    false,
                    false,
                    null,
                    string.IsNullOrEmpty(reason)
                        ? "The workload presentation writer requires manual recovery."
                        : reason,
                    recoveryRequired: true);
            }

            if (!snapshot.HasOwnedValues)
            {
                return new WorkloadPresentationSettingsMutationReceipt(
                    true, persist, false, false, false, Array.Empty<string>(), string.Empty);
            }

            if (!TryRestoreOwned(snapshot, persist, out bool persisted, out reason))
            {
                snapshot.RecoveryRequired = true;
                return new WorkloadPresentationSettingsMutationReceipt(
                    false, persist, false, true, false, null, reason, true);
            }

            return new WorkloadPresentationSettingsMutationReceipt(
                true, persist, persisted, true, true, Array.Empty<string>(), string.Empty);
        }

        private bool TryPrepare(
            WorkloadPresentationSettingsSnapshot snapshot,
            IReadOnlyDictionary<string, WorkloadScalarValue> values,
            out List<string> keys,
            out Dictionary<string, WorkloadScalarValue> expected,
            out string reason)
        {
            keys = new List<string>();
            expected = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            reason = string.Empty;
            if (!ValidateSnapshot(snapshot, out reason) || values == null ||
                snapshot.RecoveryRequired || snapshot.HasOwnedValues)
            {
                reason = string.IsNullOrEmpty(reason)
                    ? values == null
                        ? "No projected presentation values were supplied."
                        : "The workload presentation writer already owns an unfinished mutation."
                    : reason;
                return false;
            }

            if (snapshot.Revision != _store.Revision)
            {
                reason = "Global Better Work Tab settings changed while the workload apply was being prepared.";
                return false;
            }

            foreach (KeyValuePair<string, WorkloadScalarValue> baseline in snapshot.Values)
            {
                if (!_store.TryRead(baseline.Key, out WorkloadScalarValue current) ||
                    !baseline.Value.Equals(current))
                {
                    reason = "Global Better Work Tab settings changed while the workload apply was being prepared.";
                    return false;
                }
            }

            foreach (KeyValuePair<string, WorkloadScalarValue> pair in values)
            {
                if (!snapshot.Values.ContainsKey(pair.Key) ||
                    !_store.TryCanonicalize(pair.Key, pair.Value, out WorkloadScalarValue canonical, out reason))
                {
                    reason = string.IsNullOrEmpty(reason)
                        ? "The projected presentation payload contains an uncaptured or invalid setting value."
                        : reason;
                    return false;
                }

                if (!snapshot.Values[pair.Key].Equals(canonical))
                {
                    keys.Add(pair.Key);
                    expected[pair.Key] = canonical;
                }
            }

            keys.Sort(StringComparer.Ordinal);
            return true;
        }

        private bool TryRestoreOwned(
            WorkloadPresentationSettingsSnapshot snapshot,
            bool persist,
            out bool persisted,
            out string reason)
        {
            persisted = false;
            reason = string.Empty;
            if (snapshot.OwnedRevision != _store.Revision)
            {
                reason = "Global Better Work Tab settings changed after this writer applied its values.";
                return false;
            }

            var keys = new List<string>(snapshot.OwnedValues.Keys);
            keys.Sort(StringComparer.Ordinal);
            snapshot.OwnedRevision = _store.BeginOwnedMutation(keys);
            using (_store.BeginWriteLease())
            {
                try
                {
                    if (!TryWriteExactValues(
                            snapshot,
                            keys,
                            snapshot.OwnedValues,
                            snapshot.Values,
                            false,
                            out reason))
                    {
                        return false;
                    }

                    if (persist && !_store.TryPersist(out reason))
                    {
                        reason = string.IsNullOrEmpty(reason)
                            ? "The workload presentation rollback could not be persisted."
                            : reason;
                        return false;
                    }

                    persisted = persist;
                }
                catch (Exception ex)
                {
                    reason = "The workload presentation rollback failed: " + ex.Message;
                    return false;
                }
            }

            snapshot.OwnedValues.Clear();
            snapshot.OwnedRevision = 0L;
            snapshot.RecoveryRequired = false;
            return true;
        }

        private bool TryWriteExactValues(
            WorkloadPresentationSettingsSnapshot snapshot,
            IList<string> keys,
            IReadOnlyDictionary<string, WorkloadScalarValue> expected,
            IReadOnlyDictionary<string, WorkloadScalarValue> target,
            bool recordOwnedValues,
            out string reason)
        {
            reason = string.Empty;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (snapshot.OwnedRevision != _store.Revision ||
                    !_store.TryRead(key, out WorkloadScalarValue current) ||
                    !expected[key].Equals(current))
                {
                    reason = "A concurrent setting write replaced '" + key +
                        "'; the workload writer did not overwrite it.";
                    return false;
                }

                if (!_store.TryWrite(key, target[key], out reason))
                {
                    return false;
                }

                if (recordOwnedValues)
                {
                    snapshot.OwnedValues[key] = target[key];
                }

                if (!_store.TryRead(key, out WorkloadScalarValue actual) ||
                    !target[key].Equals(actual))
                {
                    reason = "The setting did not retain the exact workload value after normalization.";
                    return false;
                }
            }

            return true;
        }

        private bool ValidateSnapshot(
            WorkloadPresentationSettingsSnapshot snapshot,
            out string reason)
        {
            reason = string.Empty;
            if (_store == null || snapshot == null ||
                !ReferenceEquals(snapshot.StoreIdentity, _store.Identity))
            {
                reason = "The Better Work Tab settings snapshot is stale or incomplete.";
                return false;
            }

            return true;
        }
    }
}
