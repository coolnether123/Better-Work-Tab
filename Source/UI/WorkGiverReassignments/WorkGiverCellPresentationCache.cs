using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Retains the non-visual state required to paint a sub-work priority cell. IMGUI still
    /// repaints textures and text every pass, while dictionary, schedule, and inheritance
    /// resolution only reruns when an authoritative version or direct parent value changes.
    /// </summary>
    internal static class WorkGiverCellPresentationCache
    {
        private const int MaximumEntries = 32768;
        private static readonly Dictionary<CellKey, CellPresentation> Entries =
            new Dictionary<CellKey, CellPresentation>(2048);
        private static readonly Dictionary<int, int> PawnHours = new Dictionary<int, int>(64);
        private static readonly Dictionary<ParentKey, int> ParentPriorities =
            new Dictionary<ParentKey, int>(128);

        private static int _frame = -1;
        private static int _subWorkVersion;
        private static int _scheduleVersion;
        private static int _lastParentPawnId = int.MinValue;
        private static ushort _lastParentWorkTypeHash;
        private static int _lastParentPriority;

        internal static CellPresentation Resolve(
            WorkGiver workGiver,
            WorkTypeDef workType,
            Pawn pawn,
            int knownParentPriority = int.MinValue)
        {
            RefreshFrameState();

            int pawnId = pawn?.thingIDNumber ?? -1;
            int parentPriority = knownParentPriority != int.MinValue
                ? knownParentPriority
                : GetParentPriority(pawn, workType, pawnId);
            int hour = GetHour(pawn, pawnId);
            bool lockedOverrides = WorkGiverReassignmentManager.LockedSubWorkOverridesDisabledParent();
            var key = new CellKey(pawnId, workType?.shortHash ?? 0, workGiver?.def?.shortHash ?? 0);
            int dynamicStateVersion = WorkGiverPresentationInvalidation.GetPawnDynamicVersion(pawn);

            if (Entries.TryGetValue(key, out CellPresentation cached) &&
                cached.SubWorkVersion == _subWorkVersion &&
                cached.ScheduleVersion == _scheduleVersion &&
                cached.ParentPriority == parentPriority &&
                cached.Hour == hour &&
                cached.LockedOverrides == lockedOverrides &&
                cached.DynamicStateVersion == dynamicStateVersion)
            {
                return cached;
            }

            if (cached == null && Entries.Count >= MaximumEntries)
            {
                Entries.Clear();
            }

            CellPresentation resolved = Build(
                workGiver,
                workType,
                pawn,
                parentPriority,
                hour,
                lockedOverrides,
                cached);

            if (cached == null)
            {
                Entries[key] = resolved;
            }
            return resolved;
        }

        private static CellPresentation Build(
            WorkGiver workGiver,
            WorkTypeDef workType,
            Pawn pawn,
            int parentPriority,
            int hour,
            bool lockedOverrides,
            CellPresentation presentation)
        {
            WorkGiverDef workGiverDef = workGiver?.def;
            bool hasPawnOverride = pawn != null &&
                                   WorkGiverReassignmentManager.HasPawnWorkGiverOverride(pawn, workGiverDef);
            int basePriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                pawn,
                workGiverDef,
                parentPriority);
            TimePriorityEvaluation evaluation = TimePriorityService.EvaluateWorkGiverPriority(
                pawn,
                workType,
                workGiverDef,
                basePriority);

            TimePriorityTarget scheduleTarget = default(TimePriorityTarget);
            int scheduleFallbackPriority = basePriority;
            bool hasScheduleIndicator;
            int inheritedPriority = parentPriority;
            if (pawn != null)
            {
                hasScheduleIndicator = TimePriorityService.TryGetWorkGiverScheduleIndicatorTarget(
                    pawn,
                    workType,
                    workGiverDef,
                    basePriority,
                    out scheduleTarget,
                    out scheduleFallbackPriority);
                inheritedPriority = WorkGiverReassignmentManager.GetInheritedWorkGiverPriority(
                    pawn,
                    workType,
                    workGiverDef);
            }
            else
            {
                scheduleTarget = TimePriorityTarget.ForWorkGiver(null, workType, workGiverDef);
                hasScheduleIndicator = TimePriorityService.HasCustomSchedule(scheduleTarget, basePriority);
            }

            presentation ??= new CellPresentation();
            presentation.SubWorkVersion = _subWorkVersion;
            presentation.ScheduleVersion = _scheduleVersion;
            presentation.ParentPriority = parentPriority;
            presentation.Hour = hour;
            presentation.LockedOverrides = lockedOverrides;
            presentation.BasePriority = basePriority;
            presentation.EffectivePriority = evaluation.EffectivePriority;
            presentation.HasPawnOverride = hasPawnOverride;
            presentation.HasScheduleIndicator = hasScheduleIndicator;
            presentation.ScheduleTarget = scheduleTarget;
            presentation.ScheduleFallbackPriority = scheduleFallbackPriority;
            presentation.InheritedPriority = inheritedPriority;
            RefreshDynamicState(presentation, pawn, workType, workGiver);
            return presentation;
        }

        private static void RefreshDynamicState(
            CellPresentation presentation,
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiver workGiver)
        {
            bool workTypeDisabled = pawn != null && pawn.WorkTypeIsDisabled(workType);
            bool disabledByAge = false;
            int minimumAge = 0;
            if (workTypeDisabled)
            {
                disabledByAge = WorkGiverPriorityBoxCompatibility.IsWorkTypeDisabledByAge(pawn, workType, out minimumAge);
            }

            presentation.WorkTypeDisabled = workTypeDisabled;
            presentation.DisabledByAge = disabledByAge;
            presentation.MinimumAge = minimumAge;
            presentation.Incapable = pawn != null && IsIncapable(pawn, workGiver);
            // Capacity evaluation can initialize RimWorld's own capacity cache and emit
            // a dirty notification. Stamp after evaluation so this presentation records
            // the exact version of the state it just computed.
            presentation.DynamicStateVersion = WorkGiverPresentationInvalidation.GetPawnDynamicVersion(pawn);
        }

        private static void RefreshFrameState()
        {
            int frame = Time.frameCount;
            if (_frame == frame)
            {
                return;
            }

            _frame = frame;
            _subWorkVersion = WorkGiverReassignmentManager.CurrentSyncVersion;
            _scheduleVersion = TimePriorityService.CurrentVersion;
            PawnHours.Clear();
            ParentPriorities.Clear();
            _lastParentPawnId = int.MinValue;
        }

        private static int GetParentPriority(Pawn pawn, WorkTypeDef workType, int pawnId)
        {
            ushort workTypeHash = workType?.shortHash ?? 0;
            if (_lastParentPawnId == pawnId && _lastParentWorkTypeHash == workTypeHash)
            {
                return _lastParentPriority;
            }

            var key = new ParentKey(pawnId, workTypeHash);
            if (ParentPriorities.TryGetValue(key, out int priority))
            {
                _lastParentPawnId = pawnId;
                _lastParentWorkTypeHash = workTypeHash;
                _lastParentPriority = priority;
                return priority;
            }

            priority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            ParentPriorities[key] = priority;
            _lastParentPawnId = pawnId;
            _lastParentWorkTypeHash = workTypeHash;
            _lastParentPriority = priority;
            return priority;
        }

        private static int GetHour(Pawn pawn, int pawnId)
        {
            if (PawnHours.TryGetValue(pawnId, out int hour))
            {
                return hour;
            }

            hour = TimePriorityService.GetCurrentHour(pawn);
            PawnHours[pawnId] = hour;
            return hour;
        }

        private static bool IsIncapable(Pawn pawn, WorkGiver workGiver)
        {
            if (pawn == null || workGiver?.def?.requiredCapacities == null)
            {
                return false;
            }

            foreach (PawnCapacityDef capacity in workGiver.def.requiredCapacities)
            {
                if (!pawn.health.capacities.CapableOf(capacity))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            private readonly int _pawnId;
            private readonly ushort _workTypeHash;
            private readonly ushort _workGiverHash;

            internal CellKey(int pawnId, ushort workTypeHash, ushort workGiverHash)
            {
                _pawnId = pawnId;
                _workTypeHash = workTypeHash;
                _workGiverHash = workGiverHash;
            }

            public bool Equals(CellKey other)
            {
                return _pawnId == other._pawnId &&
                       _workTypeHash == other._workTypeHash &&
                       _workGiverHash == other._workGiverHash;
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _pawnId;
                    hash = (hash * 397) ^ _workTypeHash;
                    hash = (hash * 397) ^ _workGiverHash;
                    return hash;
                }
            }
        }

        private readonly struct ParentKey : IEquatable<ParentKey>
        {
            private readonly int _pawnId;
            private readonly ushort _workTypeHash;

            internal ParentKey(int pawnId, ushort workTypeHash)
            {
                _pawnId = pawnId;
                _workTypeHash = workTypeHash;
            }

            public bool Equals(ParentKey other)
            {
                return _pawnId == other._pawnId && _workTypeHash == other._workTypeHash;
            }

            public override bool Equals(object obj)
            {
                return obj is ParentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_pawnId * 397) ^ _workTypeHash;
                }
            }
        }

        internal sealed class CellPresentation
        {
            internal int SubWorkVersion;
            internal int ScheduleVersion;
            internal int ParentPriority;
            internal int Hour;
            internal bool LockedOverrides;
            internal int BasePriority;
            internal int EffectivePriority;
            internal bool HasPawnOverride;
            internal bool WorkTypeDisabled;
            internal bool DisabledByAge;
            internal int MinimumAge;
            internal bool Incapable;
            internal bool HasScheduleIndicator;
            internal TimePriorityTarget ScheduleTarget;
            internal int ScheduleFallbackPriority;
            internal int InheritedPriority;
            internal int DynamicStateVersion;
        }
    }
}
