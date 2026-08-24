using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    /// <summary>Persisted compatibility projection of one schedule value.</summary>
    public sealed class TimePriorityScheduleData : IExposable
    {
        public string Key;
        public int PawnId;
        public TimePriorityTargetKind Kind;
        public string WorkTypeDefName;
        public string TargetDefName;

        public List<int> HourlyPriorities = new List<int>(TimePriorityService.HoursPerDay);
        public List<int> UnlinkedHours = new List<int>();

        internal TimePriorityCacheKey CacheKey =>
            new TimePriorityCacheKey(PawnId, Kind, WorkTypeDefName, TargetDefName);

        internal bool NeedsLinkMigration;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Key, "key");
            Scribe_Values.Look(ref PawnId, "pawnId", TimePriorityTarget.GlobalPawnId);
            Scribe_Values.Look(ref Kind, "kind", TimePriorityTargetKind.WorkType);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName");
            Scribe_Values.Look(ref TargetDefName, "targetDefName");
            Scribe_Collections.Look(ref HourlyPriorities, "hourlyPriorities", LookMode.Value);

            // Deliberately left null before the read so an absent element is
            // distinguishable from an empty one: absent means a pre-link save,
            // empty means every hour is genuinely linked.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                UnlinkedHours = null;
            }

            Scribe_Collections.Look(ref UnlinkedHours, "unlinkedHours", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NeedsLinkMigration = UnlinkedHours == null;
                Key = TimePriorityService.BuildKey(
                    PawnId,
                    Kind,
                    WorkTypeDefName,
                    TargetDefName);
            }
        }
    }

    /// <summary>
    /// Exact live baseline captured at the canonical schedule-service seam.
    /// The value carries all 24 displayed values and the independent pinned
    /// mask; the service and authority revisions make stale writers fail
    /// closed before they can mutate live state.
    /// </summary>
    internal sealed class TimePriorityLiveScheduleSnapshot
    {
        internal TimePriorityLiveScheduleSnapshot(
            TimePriorityTarget target,
            TimePriorityScheduleValue schedule,
            bool hadSchedule,
            int fallbackPriority,
            int serviceVersion,
            long authorityRevision)
        {
            Target = target;
            Schedule = schedule;
            HadSchedule = hadSchedule;
            FallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            ServiceVersion = serviceVersion;
            AuthorityRevision = authorityRevision;
        }

        internal TimePriorityTarget Target { get; }
        internal TimePriorityScheduleValue Schedule { get; }
        internal bool HadSchedule { get; }
        internal int FallbackPriority { get; }
        internal int ServiceVersion { get; }
        internal long AuthorityRevision { get; }

        internal bool Matches(TimePriorityLiveScheduleSnapshot other)
        {
            return other != null &&
                   Target.Matches(other.Target) &&
                   HadSchedule == other.HadSchedule &&
                   FallbackPriority == other.FallbackPriority &&
                   Schedule != null &&
                   Schedule.Equals(other.Schedule);
        }
    }

}
