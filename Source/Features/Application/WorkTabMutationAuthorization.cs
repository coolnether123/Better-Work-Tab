using System;

namespace Better_Work_Tab.Features.Application
{
    /// <summary>Application-owned optimistic lease for a compound mutation.</summary>
    internal sealed class WorkTabMutationLease
    {
        private readonly long _authorityRevision;
        private readonly int _specificJobRevision;
        private readonly int _scheduleRevision;
        private readonly int _settingsRevision;
        private bool _active = true;

        internal WorkTabMutationLease(long authorityRevision, int specificJobRevision, int scheduleRevision, int settingsRevision)
        {
            _authorityRevision = authorityRevision;
            _specificJobRevision = specificJobRevision;
            _scheduleRevision = scheduleRevision;
            _settingsRevision = settingsRevision;
        }

        internal bool IsUsable => _active;
        internal long AuthorityRevision => _authorityRevision;
        internal int SpecificJobRevision => _specificJobRevision;
        internal int ScheduleRevision => _scheduleRevision;
        internal int SettingsRevision => _settingsRevision;
        internal bool IsAcceptedForSchedule(bool synchronizedExecution, long authorityRevision, int scheduleRevision) =>
            _active && (!synchronizedExecution || (_authorityRevision == authorityRevision && _scheduleRevision == scheduleRevision));
        internal bool IsAcceptedForSpecificBatch(bool synchronizedExecution, long authorityRevision, int specificJobRevision) =>
            _active && (!synchronizedExecution || (_authorityRevision == authorityRevision && _specificJobRevision == specificJobRevision));
        internal bool IsAcceptedForSpecificBatchRollback(
            bool synchronizedExecution,
            long authorityRevision,
            int initialRevision,
            int appliedRevision) =>
            _active && (!synchronizedExecution ||
                (_authorityRevision == authorityRevision &&
                 _specificJobRevision == initialRevision &&
                 appliedRevision == unchecked(initialRevision + 1)));
        internal bool IsAcceptedForSettings(bool synchronizedExecution, long authorityRevision, int settingsRevision) =>
            _active && (!synchronizedExecution || (_authorityRevision == authorityRevision && _settingsRevision == settingsRevision));
        internal void FinalizeLease() => _active = false;
    }

}
