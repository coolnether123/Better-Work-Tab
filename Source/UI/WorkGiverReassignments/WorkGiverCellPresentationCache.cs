using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.UI.WorkGrid.Projection;
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
        private static WorkTabEffectiveStateRevision _effectiveStateRevision;
        private static long _effectiveStateRenderPassId = -1L;
        private static bool _externalPriorityAuthority;
        private static bool _hasEffectiveStateRevision;

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
            CellPresentation cached = null;
            bool hasCached = !_externalPriorityAuthority &&
                             Entries.TryGetValue(key, out cached);

            if (hasCached &&
                cached.SubWorkVersion == _subWorkVersion &&
                cached.ScheduleVersion == _scheduleVersion &&
                cached.EffectiveStateRevision == _effectiveStateRevision &&
                cached.ParentPriority == parentPriority &&
                cached.Hour == hour &&
                cached.LockedOverrides == lockedOverrides &&
                cached.DynamicStateVersion == dynamicStateVersion)
            {
                return cached;
            }

            if (!_externalPriorityAuthority && !hasCached && Entries.Count >= MaximumEntries)
            {
                Entries.Clear();
            }

            CellPresentation resolved = Build(
                workGiver,
                workType,
                pawn,
                parentPriority,
                hour,
                lockedOverrides);

            if (!_externalPriorityAuthority)
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
            bool lockedOverrides)
        {
            WorkGiverDef workGiverDef = workGiver?.def;
            WorkloadSpecificJobTargetKey specificTarget = pawn == null
                ? WorkTabEffectiveStateIds.ForGlobalSpecificJobTarget(workType, workGiverDef)
                : WorkTabEffectiveStateIds.ForSpecificJobTarget(pawn, workType, workGiverDef);
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> specificResolution =
                specificTarget.IsValid
                    ? ResolveExactSpecificJobPriority(specificTarget)
                    : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> effectiveResolution =
                specificTarget.IsValid
                    ? WorkTabEffectiveStateRuntime.ResolveSpecificJobPriority(specificTarget)
                    : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            bool hasProjectedOverride = specificResolution.IsSet;
            int projectedPriority = hasProjectedOverride
                ? specificResolution.Value.Priority
                : 0;
            bool hasProjectedClear = specificResolution.IsClear ||
                                     (specificResolution.IsNoOpinion && effectiveResolution.IsClear);
            bool hasProjectedFallbackOverride = false;
            if (pawn != null && hasProjectedClear)
            {
                WorkloadSpecificJobTargetKey globalTarget =
                    WorkTabEffectiveStateIds.ForGlobalSpecificJobTarget(workType, workGiverDef);
                WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> globalResolution =
                    ResolveExactSpecificJobPriority(globalTarget);
                if (globalResolution.IsSet)
                {
                    projectedPriority = globalResolution.Value.Priority;
                    hasProjectedFallbackOverride = true;
                }
            }

            if (pawn != null && !hasProjectedOverride &&
                !hasProjectedFallbackOverride && !hasProjectedClear)
            {
                hasProjectedOverride = WorkTabEffectiveStateRuntime.TryGetSpecificJobPriority(
                    WorkTabEffectiveStateIds.ForSpecificJobTarget(pawn, workType, workGiverDef),
                    out projectedPriority);
            }
            bool hasPawnOverride = pawn != null &&
                                   (hasProjectedOverride ||
                                    hasProjectedFallbackOverride ||
                                    (!hasProjectedClear &&
                                     WorkGiverReassignmentManager.HasPawnWorkGiverOverride(pawn, workGiverDef)));
            bool hasGlobalOverride = pawn == null && hasProjectedOverride;
            int basePriority;
            if (hasProjectedOverride || hasProjectedFallbackOverride)
            {
                basePriority = WorkPrioritySystem.ClampPriority(projectedPriority);
            }
            else if (hasProjectedClear)
            {
                // A typed Clear is an explicit removal of the specific-job
                // opinion. Do not fall through to the live override while the
                // preview is active; the parent priority is the next value.
                basePriority = parentPriority;
            }
            else
            {
                basePriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                    pawn,
                    workGiverDef,
                    parentPriority);
            }
            bool projectedScheduleOwnsCell =
                WorkTabEffectiveStateRuntime.IsPreviewDimensionOwned(
                    WorkTabEffectiveStateDimension.Schedule);
            int effectivePriority = basePriority;

            TimePriorityTarget scheduleTarget = default(TimePriorityTarget);
            int scheduleFallbackPriority = basePriority;
            bool hasScheduleIndicator = false;
            int inheritedPriority = parentPriority;
            if (!projectedScheduleOwnsCell)
            {
                TimePriorityEvaluation evaluation = TimePriorityService.EvaluateWorkGiverPriority(
                    pawn,
                    workType,
                    workGiverDef,
                    basePriority);
                effectivePriority = evaluation.EffectivePriority;

                if (pawn != null)
                {
                    hasScheduleIndicator = TimePriorityService.TryGetWorkGiverScheduleIndicatorTarget(
                        pawn,
                        workType,
                        workGiverDef,
                        basePriority,
                        out scheduleTarget,
                        out scheduleFallbackPriority);
                    inheritedPriority = WorkTabEffectiveStateRuntime.IsPreviewActive
                        ? WorkGiverReassignmentManager.GetWorkGiverPriority(
                            pawn,
                            workGiverDef,
                            parentPriority)
                        : WorkGiverReassignmentManager.GetInheritedWorkGiverPriority(
                            pawn,
                            workType,
                            workGiverDef);
                }
                else
                {
                    scheduleTarget = TimePriorityTarget.ForWorkGiver(null, workGiverDef);
                    hasScheduleIndicator = TimePriorityService.HasLiveCustomSchedule(scheduleTarget);
                }
            }
            else
            {
                TimePriorityTarget target = pawn == null
                    ? TimePriorityTarget.ForWorkGiver(null, workGiverDef)
                    : TimePriorityTarget.ForWorkGiver(pawn, workGiverDef);
                if (WorkloadTimePriorityAdapter.TryGetScheduleTarget(
                    target,
                    out WorkloadScheduleTargetKey scheduleKey,
                        out _))
                {
                    WorkTabEffectiveStateResolution<WorkloadSchedulePayload> scheduleResolution =
                        ResolveExactSchedule(scheduleKey);
                    if (scheduleResolution.IsSet && scheduleResolution.Value != null &&
                        scheduleResolution.Value.IsValid)
                    {
                        int scheduleHour = pawn == null ? 0 : TimePriorityService.GetCurrentHour(pawn);
                        int scheduledPriority = scheduleResolution.Value.IsPinned(scheduleHour)
                            ? scheduleResolution.Value.PriorityAt(scheduleHour)
                            : basePriority;
                        effectivePriority = basePriority > WorkPrioritySystem.DisabledPriority
                            ? scheduledPriority
                            : basePriority;
                        hasScheduleIndicator = true;
                        scheduleTarget = target;
                        scheduleFallbackPriority = basePriority;
                    }
                    else
                    {
                        // The canonical evaluator composes local/global
                        // targets and treats an exact typed Clear as a
                        // tombstone, so a live schedule cannot leak through.
                        TimePriorityEvaluation evaluation = TimePriorityService.EvaluateWorkGiverPriority(
                            pawn,
                            workType,
                            workGiverDef,
                            basePriority);
                        effectivePriority = evaluation.EffectivePriority;
                        hasScheduleIndicator = evaluation.HasSchedule;
                        scheduleTarget = evaluation.Target;
                        scheduleFallbackPriority = evaluation.BasePriority;
                    }
                }

                inheritedPriority = pawn == null
                    ? parentPriority
                    : WorkGiverReassignmentManager.GetWorkGiverPriority(
                        pawn,
                        workGiverDef,
                        parentPriority);
            }

            // A finished Work-grid snapshot can retain this presentation for
            // the rest of its pass. Never mutate a cache entry after it has
            // been returned, or a later live lookup could rewrite that view.
            var presentation = new CellPresentation();
            presentation.SubWorkVersion = _subWorkVersion;
            presentation.ScheduleVersion = _scheduleVersion;
            presentation.EffectiveStateRevision = _effectiveStateRevision;
            presentation.ParentPriority = parentPriority;
            presentation.Hour = hour;
            presentation.LockedOverrides = lockedOverrides;
            presentation.BasePriority = basePriority;
            presentation.EffectivePriority = effectivePriority;
            presentation.HasPawnOverride = hasPawnOverride;
            presentation.HasGlobalOverride = hasGlobalOverride;
            presentation.HasScheduleIndicator = hasScheduleIndicator;
            presentation.ScheduleTarget = scheduleTarget;
            presentation.ScheduleFallbackPriority = scheduleFallbackPriority;
            presentation.InheritedPriority = inheritedPriority;
            RefreshDynamicState(presentation, pawn, workType, workGiver);
            return presentation;
        }

        private static WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveExactSpecificJobPriority(WorkloadSpecificJobTargetKey key)
        {
            IWorkTabEffectiveStateV2Provider provider =
                WorkTabEffectiveStateRuntime.CurrentProvider as IWorkTabEffectiveStateV2Provider;
            return provider == null || key == null
                ? WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion
                : provider.ResolveSpecificJobPriority(key);
        }

        private static WorkTabEffectiveStateResolution<WorkloadSchedulePayload>
            ResolveExactSchedule(WorkloadScheduleTargetKey key)
        {
            IWorkTabEffectiveStateV2Provider provider =
                WorkTabEffectiveStateRuntime.CurrentProvider as IWorkTabEffectiveStateV2Provider;
            return provider == null || key == null
                ? WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion
                : provider.ResolveSchedule(key);
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
                disabledByAge = pawn.IsWorkTypeDisabledByAge(workType, out minimumAge);
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
            long renderPassId = WorkTabEffectiveStateRuntime.CurrentRenderPassId;
            if (!_hasEffectiveStateRevision ||
                _effectiveStateRenderPassId != renderPassId)
            {
                WorkTabEffectiveStateRevision currentRevision =
                    WorkTabEffectiveStateRuntime.CurrentRevision;
                renderPassId = WorkTabEffectiveStateRuntime.CurrentRenderPassId;
                Entries.Clear();
                PawnHours.Clear();
                ParentPriorities.Clear();
                _lastParentPawnId = int.MinValue;
                _effectiveStateRevision = currentRevision;
                _effectiveStateRenderPassId = renderPassId;
                _externalPriorityAuthority =
                    PriorityAuthorityBroker.ExternalWorkTabHasPriorityAuthority;
                _hasEffectiveStateRevision = true;
            }

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

            priority = ParentPriorityRead.GetObserved(pawn, workType);
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
            internal WorkTabEffectiveStateRevision EffectiveStateRevision;
            internal int ParentPriority;
            internal int Hour;
            internal bool LockedOverrides;
            internal int BasePriority;
            internal int EffectivePriority;
            internal bool HasPawnOverride;
            internal bool HasGlobalOverride;
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
