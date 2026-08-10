using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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

        // Runtime-only mirror of UnlinkedHours. The list remains the save and
        // compatibility contract; reads use this bounded mask instead of
        // repeatedly scanning the legacy collection.
        private int _unlinkedMask;

        private List<int> _hourlyPrioritiesSentinelList;
        private List<int> _unlinkedHoursSentinelList;
        private int _hourlyPrioritiesSentinelVersion;
        private int _unlinkedHoursSentinelVersion;
        // Canonical values are retained only for mutation reconciliation. The
        // stable read path checks List<T> versions and never allocates or scans.
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
        /// Repairs direct legacy list edits only when the public list itself has
        /// changed. List's private mutation version makes this a constant-time,
        /// allocation-free check on the supported CLR. A changed version is
        /// reconciled only then, so equivalent edits do not publish a change.
        /// </summary>
        internal bool EnsureRuntimeIntegrity()
        {
            // The version reader is an optimization, not a correctness
            // dependency. If a CLR refuses the private-field DynamicMethod,
            // the compatibility audit owns the bounded semantic scan. Do not
            // turn every effective-priority read into that scan.
            if (!ListMutationVersion<int>.IsAvailable)
            {
                return false;
            }

            if (IsRuntimeIntegrityCurrent())
            {
                return false;
            }

            // A reader can become unusable after its delegate was created on
            // an unusual runtime. Defer to the same audit fallback rather than
            // repeatedly treating an unreadable version as a dirty hot read.
            if (HourlyPriorities != null &&
                UnlinkedHours != null &&
                (!ListMutationVersion<int>.TryRead(HourlyPriorities, out _) ||
                 !ListMutationVersion<int>.TryRead(UnlinkedHours, out _)))
            {
                return false;
            }

            bool semanticChange = !MatchesCanonicalState();
            EnsureValid();
            return semanticChange;
        }

        /// <summary>
        /// Performs the compatibility-boundary reconciliation that the hot
        /// path deliberately cannot perform when List&lt;T&gt;._version is hidden.
        /// The public lists are scanned only here, then normalized and captured
        /// as the new runtime state.
        /// </summary>
        internal bool ReconcileRuntimeIntegrityFromAudit()
        {
            bool listReferenceChanged =
                !ReferenceEquals(_hourlyPrioritiesSentinelList, HourlyPriorities) ||
                !ReferenceEquals(_unlinkedHoursSentinelList, UnlinkedHours);
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

        private bool IsRuntimeIntegrityCurrent()
        {
            if (!ReferenceEquals(_hourlyPrioritiesSentinelList, HourlyPriorities) ||
                !ReferenceEquals(_unlinkedHoursSentinelList, UnlinkedHours))
            {
                return false;
            }

            if (HourlyPriorities == null || UnlinkedHours == null)
            {
                return false;
            }

            if (!ListMutationVersion<int>.IsAvailable)
            {
                return false;
            }

            if (ListMutationVersion<int>.TryRead(HourlyPriorities, out int hourlyVersion) &&
                ListMutationVersion<int>.TryRead(UnlinkedHours, out int unlinkedVersion))
            {
                return _hourlyPrioritiesSentinelVersion == hourlyVersion &&
                       _unlinkedHoursSentinelVersion == unlinkedVersion;
            }

            // An unavailable or unreadable version is never treated as current.
            // EnsureRuntimeIntegrity defers that case to the compatibility audit
            // so a stable query still remains allocation-free and scan-free.
            return false;
        }

        private void CaptureRuntimeIntegrity()
        {
            _hourlyPrioritiesSentinelList = HourlyPriorities;
            _unlinkedHoursSentinelList = UnlinkedHours;

            if (HourlyPriorities != null &&
                ListMutationVersion<int>.TryRead(HourlyPriorities, out int hourlyVersion))
            {
                _hourlyPrioritiesSentinelVersion = hourlyVersion;
            }

            if (UnlinkedHours != null &&
                ListMutationVersion<int>.TryRead(UnlinkedHours, out int unlinkedVersion))
            {
                _unlinkedHoursSentinelVersion = unlinkedVersion;
            }

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
    /// Reads List&lt;T&gt;'s mutation version without boxing on the hot path. The
    /// version is private framework state, so the reader is isolated here.
    /// </summary>
    internal static class ListMutationVersion<T>
    {
        private static readonly Func<List<T>, int> Reader = CreateReader();
        private static bool _readerFailed;

        internal static bool IsAvailable => Reader != null && !_readerFailed;

        internal static bool TryRead(List<T> values, out int version)
        {
            if (Reader == null || values == null)
            {
                version = 0;
                return false;
            }

            try
            {
                version = Reader(values);
                return true;
            }
            catch
            {
                // Treat a delegate invocation failure like an unavailable
                // field. The audit fallback remains the correctness path.
                _readerFailed = true;
                version = 0;
                return false;
            }
        }

        private static Func<List<T>, int> CreateReader()
        {
            try
            {
                FieldInfo versionField = typeof(List<T>).GetField(
                    "_version",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (versionField == null)
                {
                    return null;
                }

                var method = new DynamicMethod(
                    "ReadListVersion",
                    typeof(int),
                    new[] { typeof(List<T>) },
                    typeof(ListMutationVersion<T>),
                    true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, versionField);
                il.Emit(OpCodes.Ret);
                return (Func<List<T>, int>)method.CreateDelegate(typeof(Func<List<T>, int>));
            }
            catch
            {
                return null;
            }
        }
    }
}
