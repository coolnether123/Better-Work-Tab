using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal enum PriorityMutationOutcome
    {
        Rejected,
        Applied,
        AppliedAfterAuthorityChange
    }

    internal static class PriorityMutationOutcomePolicy
    {
        internal static PriorityMutationOutcome AfterWrite(bool authorityCurrent) =>
            authorityCurrent ? PriorityMutationOutcome.Applied : PriorityMutationOutcome.AppliedAfterAuthorityChange;
    }

    internal static class PriorityMutationTransaction
    {
        internal static bool TryPrepareFinalWrite(
            Func<int> normalize,
            Func<bool> revalidate,
            out int priority)
        {
            priority = normalize();
            return revalidate();
        }
    }

    internal sealed class ParentPriorityCommandExpectation
    {
        internal ParentPriorityCommandExpectation(long authorityRevision, int storedPriority)
        {
            AuthorityRevision = authorityRevision;
            StoredPriority = storedPriority;
        }

        internal ParentPriorityCommandExpectation(
            long authorityRevision,
            int storedPriority,
            int scheduleFallbackPriority,
            int scheduleVersion,
            IEnumerable<int> schedulePriorities,
            int pinnedHourMask)
            : this(authorityRevision, storedPriority)
        {
            ScheduleFallbackPriority = scheduleFallbackPriority;
            ScheduleVersion = scheduleVersion;
            Schedule = new TimePriorityScheduleValue(schedulePriorities, pinnedHourMask);
        }

        internal long AuthorityRevision { get; }
        internal int StoredPriority { get; }
        internal int ScheduleFallbackPriority { get; }
        internal int ScheduleVersion { get; }
        internal TimePriorityScheduleValue Schedule { get; }
        internal int PinnedHourMask => Schedule?.PinnedHourMask ?? 0;
        internal bool HasSchedule => Schedule?.IsValid == true;

        internal bool MatchesStored(long authorityRevision, int storedPriority) =>
            AuthorityRevision == authorityRevision && StoredPriority == storedPriority;

        internal bool IsPinnedHour(int hour) => Schedule != null && Schedule.IsPinned(hour);

        internal bool MatchesSchedule(
            long authorityRevision,
            int scheduleFallbackPriority,
            int scheduleVersion,
            TimePriorityScheduleValue schedule) =>
            AuthorityRevision == authorityRevision &&
            HasSchedule &&
            ScheduleFallbackPriority == scheduleFallbackPriority &&
            ScheduleVersion == scheduleVersion &&
            Schedule.Equals(schedule);

        internal int[] CopySchedulePriorities()
        {
            return HasSchedule ? Schedule.CopyPriorities() : null;
        }
    }
}
