using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    /// <summary>
    /// One work target's 24-hour schedule.
    ///
    /// An hour is either <em>linked</em> or <em>unlinked</em>. A linked hour has
    /// no opinion: it shows whatever the work's priority box currently says and
    /// follows it forever. An unlinked hour holds a number the player chose and
    /// keeps it when the box moves.
    ///
    /// That distinction is stored rather than worked out from the numbers,
    /// because the two cannot be told apart by value. An hour pinned at 3 while
    /// the box also reads 3 is still pinned, and must stay at 3 when the box
    /// becomes 4. Deriving it by comparing against the current default -- which
    /// is what this used to do -- also meant that moving the box relabelled
    /// every inherited hour as an override at once.
    /// </summary>
    public sealed class TimePriorityScheduleData : IExposable
    {
        public string Key;
        public int PawnId;
        public TimePriorityTargetKind Kind;
        public string WorkTypeDefName;
        public string TargetDefName;

        /// <summary>
        /// Per-hour priorities. Only meaningful where the hour is unlinked;
        /// linked hours keep their last value purely so that unlinking one again
        /// starts somewhere sensible.
        /// </summary>
        public List<int> HourlyPriorities = new List<int>(TimePriorityService.HoursPerDay);

        /// <summary>Hours the player has taken off the priority box.</summary>
        public List<int> UnlinkedHours = new List<int>();

        internal TimePriorityCacheKey CacheKey =>
            new TimePriorityCacheKey(PawnId, Kind, WorkTypeDefName, TargetDefName);

        /// <summary>
        /// Set while loading a save written before hours carried their link
        /// state. Those saves cannot describe an override that matched the
        /// default, so the service reconstructs the only thing they could ever
        /// show: an hour was an override exactly when it differed. Cleared once
        /// <see cref="TimePriorityService"/> has resolved the target's default.
        /// </summary>
        internal bool NeedsLinkMigration;

        // The public schedule lists are retained for save and import compatibility, but runtime
        // writers must use the mutation API or notify the service after direct edits. The service
        // Supported invalidation paths advance the service generation when the active priority
        // range changes, so normalization remains settings- and authority-sensitive without
        // repeating the full repair on every cell read.
        private int _validatedVersion = -1;

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
                _validatedVersion = -1;
                UnlinkedHours = null;
            }

            Scribe_Collections.Look(ref UnlinkedHours, "unlinkedHours", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NeedsLinkMigration = UnlinkedHours == null;
                EnsureValid();
            }
        }

        internal bool IsUnlinked(int hour)
        {
            return UnlinkedHours != null && UnlinkedHours.Contains(hour);
        }

        internal bool HasAnyUnlinkedHour => UnlinkedHours != null && UnlinkedHours.Count > 0;

        /// <summary>Pins an hour to a number of its own.</summary>
        internal void SetOverride(int hour, int priority)
        {
            if (hour < 0 || hour >= TimePriorityService.HoursPerDay)
            {
                return;
            }

            EnsureValid();
            HourlyPriorities[hour] = WorkPrioritySystem.ClampPriority(priority);
            if (!UnlinkedHours.Contains(hour))
            {
                UnlinkedHours.Add(hour);
                UnlinkedHours.Sort();
            }
        }

        /// <summary>Returns an hour to following the priority box.</summary>
        internal bool ClearOverride(int hour)
        {
            EnsureValid();
            return UnlinkedHours.Remove(hour);
        }

        internal void EnsureValid()
        {
            if (_validatedVersion == TimePriorityService.CurrentVersion)
            {
                return;
            }

            if (HourlyPriorities == null)
            {
                HourlyPriorities = new List<int>(TimePriorityService.HoursPerDay);
            }

            while (HourlyPriorities.Count < TimePriorityService.HoursPerDay)
            {
                HourlyPriorities.Add(WorkPrioritySystem.GetDefaultEnabledPriority());
            }

            if (HourlyPriorities.Count > TimePriorityService.HoursPerDay)
            {
                HourlyPriorities.RemoveRange(TimePriorityService.HoursPerDay, HourlyPriorities.Count - TimePriorityService.HoursPerDay);
            }

            for (int i = 0; i < HourlyPriorities.Count; i++)
            {
                HourlyPriorities[i] = WorkPrioritySystem.ClampPriority(HourlyPriorities[i]);
            }

            if (UnlinkedHours == null)
            {
                UnlinkedHours = new List<int>();
            }

            for (int i = UnlinkedHours.Count - 1; i >= 0; i--)
            {
                int hour = UnlinkedHours[i];
                if (hour < 0 || hour >= TimePriorityService.HoursPerDay ||
                    UnlinkedHours.IndexOf(hour) != i)
                {
                    UnlinkedHours.RemoveAt(i);
                }
            }

            Key = TimePriorityService.BuildKey(PawnId, Kind, WorkTypeDefName, TargetDefName);
            _validatedVersion = TimePriorityService.CurrentVersion;
        }
    }
}
