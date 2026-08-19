using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads.V2;
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

        // Runtime-only mirror of UnlinkedHours. The list remains the save and
        // compatibility contract; reads use this bounded mask instead of
        // repeatedly scanning the legacy collection.
        private int _unlinkedMask;

        private List<int> _observedHourlyPriorities;
        private List<int> _observedUnlinkedHours;
        // Canonical values are retained for bounded compatibility-audit
        // reconciliation. Normal reads use the mask and never scan public lists.
        private int[] _canonicalHourlyPriorities;

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
                EnsureValid();
            }
        }

        internal bool IsUnlinked(int hour)
        {
            return hour >= 0 && hour < TimePriorityService.HoursPerDay &&
                   (_unlinkedMask & (1 << hour)) != 0;
        }

        internal bool TryGetStoredPriority(int hour, out int priority)
        {
            priority = WorkPrioritySystem.DisabledPriority;
            if (!IsUnlinked(hour) ||
                HourlyPriorities == null ||
                hour >= HourlyPriorities.Count)
            {
                return false;
            }

            // Clamping is normally redundant because all writes pass through
            // EnsureValid. It also keeps a direct legacy value safe until the
            // next compatibility audit without adding a scan or allocation.
            priority = WorkPrioritySystem.ClampPriority(HourlyPriorities[hour]);
            return true;
        }

        internal bool HasAnyUnlinkedHour => _unlinkedMask != 0;

        /// <summary>
        /// Performs the compatibility-boundary reconciliation that the hot
        /// path deliberately does not perform.
        /// The public lists are scanned only here, then normalized and captured
        /// as the new runtime state.
        /// </summary>
        internal bool ReconcileRuntimeIntegrityFromAudit()
        {
            bool listReferenceChanged =
                !ReferenceEquals(_observedHourlyPriorities, HourlyPriorities) ||
                !ReferenceEquals(_observedUnlinkedHours, UnlinkedHours);
            bool metadataChanged = Key != TimePriorityService.BuildKey(
                PawnId,
                Kind,
                WorkTypeDefName,
                TargetDefName);
            bool semanticChange = !MatchesCanonicalState();

            EnsureValid();
            return listReferenceChanged || metadataChanged || semanticChange;
        }

        /// <summary>Pins an hour to a number of its own.</summary>
        internal void SetOverride(int hour, int priority)
        {
            if (hour < 0 || hour >= TimePriorityService.HoursPerDay)
            {
                return;
            }

            // Writes are cold relative to priority evaluation. Normalize here
            // as a last safety net for a compatibility CLR whose version reader
            // cannot protect a malformed public list before the next audit.
            EnsureValid();
            HourlyPriorities[hour] = WorkPrioritySystem.ClampPriority(priority);
            int bit = 1 << hour;
            if ((_unlinkedMask & bit) == 0)
            {
                UnlinkedHours.Add(hour);
                UnlinkedHours.Sort();
                _unlinkedMask |= bit;
            }

            CaptureRuntimeIntegrity();
        }

        /// <summary>Returns an hour to following the priority box.</summary>
        internal bool ClearOverride(int hour)
        {
            EnsureValid();
            if (!IsUnlinked(hour))
            {
                return false;
            }

            UnlinkedHours.Remove(hour);
            _unlinkedMask &= ~(1 << hour);
            CaptureRuntimeIntegrity();
            return true;
        }

        internal void EnsureValid()
        {
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

            int unlinkedMask = 0;
            for (int i = UnlinkedHours.Count - 1; i >= 0; i--)
            {
                int hour = UnlinkedHours[i];
                if (hour < 0 || hour >= TimePriorityService.HoursPerDay ||
                    (unlinkedMask & (1 << hour)) != 0)
                {
                    UnlinkedHours.RemoveAt(i);
                    continue;
                }

                unlinkedMask |= 1 << hour;
            }

            UnlinkedHours.Sort();
            _unlinkedMask = unlinkedMask;
            Key = TimePriorityService.BuildKey(PawnId, Kind, WorkTypeDefName, TargetDefName);
            CaptureRuntimeIntegrity();
        }

        private void CaptureRuntimeIntegrity()
        {
            _observedHourlyPriorities = HourlyPriorities;
            _observedUnlinkedHours = UnlinkedHours;

            if (_canonicalHourlyPriorities == null)
            {
                _canonicalHourlyPriorities = new int[TimePriorityService.HoursPerDay];
            }

            for (int hour = 0; hour < TimePriorityService.HoursPerDay; hour++)
            {
                _canonicalHourlyPriorities[hour] = HourlyPriorities != null && hour < HourlyPriorities.Count
                    ? WorkPrioritySystem.ClampPriority(HourlyPriorities[hour])
                    : WorkPrioritySystem.GetDefaultEnabledPriority();
            }
        }

        private bool MatchesCanonicalState()
        {
            if (_canonicalHourlyPriorities == null ||
                HourlyPriorities == null ||
                HourlyPriorities.Count != TimePriorityService.HoursPerDay ||
                UnlinkedHours == null ||
                UnlinkedHours.Count > TimePriorityService.HoursPerDay)
            {
                return false;
            }

            for (int hour = 0; hour < TimePriorityService.HoursPerDay; hour++)
            {
                if (HourlyPriorities[hour] != _canonicalHourlyPriorities[hour])
                {
                    return false;
                }
            }

            int expectedIndex = 0;
            for (int hour = 0; hour < TimePriorityService.HoursPerDay; hour++)
            {
                if ((_unlinkedMask & (1 << hour)) == 0)
                {
                    continue;
                }

                if (expectedIndex >= UnlinkedHours.Count ||
                    UnlinkedHours[expectedIndex] != hour)
                {
                    return false;
                }

                expectedIndex++;
            }

            return expectedIndex == UnlinkedHours.Count;
        }
    }

    /// <summary>
    /// Exact live baseline captured at the canonical schedule-service seam.
    /// The payload carries all 24 displayed values and the independent pinned
    /// mask; the service version and authority revision make stale workload
    /// commits fail closed before they can mutate live state.
    /// </summary>
    internal sealed class TimePriorityLiveScheduleSnapshot
    {
        internal TimePriorityLiveScheduleSnapshot(
            TimePriorityTarget target,
            WorkloadSchedulePayload payload,
            bool hadSchedule,
            int fallbackPriority,
            int serviceVersion,
            long authorityRevision)
        {
            Target = target;
            Payload = payload;
            HadSchedule = hadSchedule;
            FallbackPriority = WorkPrioritySystem.ClampPriority(fallbackPriority);
            ServiceVersion = serviceVersion;
            AuthorityRevision = authorityRevision;
        }

        internal TimePriorityTarget Target { get; }
        internal WorkloadSchedulePayload Payload { get; }
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
                   Payload != null &&
                   Payload.Equals(other.Payload);
        }
    }

}
