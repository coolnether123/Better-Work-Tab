using System;
using System.Collections.Generic;
using System.Globalization;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Workloads;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.Workloads.Projection
{
    /// <summary>
    /// Optional BWT-owned wiring for the callback live provider. This adapter
    /// resolves stable runtime identifiers and delegates reads to the existing
    /// authority, schedule, and reassignment services. It retains no state
    /// beyond resolver delegates and a revision observer.
    /// </summary>
    public sealed class BwtLiveWorkTabEffectiveStateAdapter
    {
        private readonly Func<PawnKey, Pawn> _pawnResolver;
        private readonly Func<WorkTypeKey, WorkTypeDef> _workTypeResolver;
        private readonly Func<WorkGiverKey, WorkGiverDef> _workGiverResolver;
        private readonly Func<WorkloadScheduleTargetKey, WorkTabEffectiveStateResolution<WorkloadSchedulePayload>> _scheduleV2Resolver;
        private readonly Func<WorkloadSpecificJobTargetKey, WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>> _specificPriorityV2Resolver;
        private readonly Func<WorkloadWorkTypeOrderKey, WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>> _workTypeOrderV2Resolver;
        private readonly Func<string, WorkTabEffectiveStateResolution<WorkloadSettingValue>> _presentationV2Resolver;
        private readonly Func<long> _revisionResolver;
        private readonly BwtLiveWorkTabEffectiveStateRevisionClock _revisionClock;

        public BwtLiveWorkTabEffectiveStateAdapter(
            Func<PawnKey, Pawn> pawnResolver = null,
            Func<WorkTypeKey, WorkTypeDef> workTypeResolver = null,
            Func<WorkGiverKey, WorkGiverDef> workGiverResolver = null,
            Func<long> revisionResolver = null,
            Func<WorkloadScheduleTargetKey, WorkTabEffectiveStateResolution<WorkloadSchedulePayload>> scheduleV2Resolver = null,
            Func<WorkloadSpecificJobTargetKey, WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>> specificPriorityV2Resolver = null,
            Func<WorkloadWorkTypeOrderKey, WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>> workTypeOrderV2Resolver = null,
            Func<string, WorkTabEffectiveStateResolution<WorkloadSettingValue>> presentationV2Resolver = null)
        {
            _pawnResolver = pawnResolver ?? ResolvePawn;
            _workTypeResolver = workTypeResolver ?? ResolveWorkType;
            _workGiverResolver = workGiverResolver ?? ResolveWorkGiver;
            _scheduleV2Resolver = scheduleV2Resolver;
            _specificPriorityV2Resolver = specificPriorityV2Resolver;
            _workTypeOrderV2Resolver = workTypeOrderV2Resolver;
            _presentationV2Resolver = presentationV2Resolver;
            _revisionResolver = revisionResolver;
            _revisionClock = new BwtLiveWorkTabEffectiveStateRevisionClock();
        }

        public LiveWorkTabEffectiveStateCallbacks CreateCallbacks(string providerId = "bwt.live")
        {
            var callbacks = new LiveWorkTabEffectiveStateCallbacks(providerId)
            {
                Revision = _revisionResolver ?? _revisionClock.Read,
                RevisionVector = _revisionResolver == null
                    ? _revisionClock.ReadVector
                    : () => WorkTabEffectiveStateRevisionVector.FromRevision(_revisionResolver()),
                ScheduleV2 = _scheduleV2Resolver ?? ResolveScheduleV2,
                SpecificJobPriorityV2 = _specificPriorityV2Resolver ?? ResolveSpecificJobPriorityV2,
                WorkTypeOrderV2 = _workTypeOrderV2Resolver ?? ResolveWorkTypeOrderV2,
                PresentationSettingV2 = _presentationV2Resolver
            };
            return callbacks;
        }

        public LiveWorkTabEffectiveStateProvider CreateProvider(
            string providerId = "bwt.live")
        {
            return new LiveWorkTabEffectiveStateProvider(CreateCallbacks(providerId));
        }

        private WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveScheduleV2(
            WorkloadScheduleTargetKey key)
        {
            if (key == null || !key.IsValid ||
                !WorkloadTimePriorityAdapter.TryGetTimePriorityTarget(
                    key,
                    out TimePriorityTarget target,
                    out _))
            {
                return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
            }

            WorkTypeDef workType = _workTypeResolver(key.WorkType);
            WorkGiverDef workGiver = key.TargetKind == WorkloadScheduleTargetKind.WorkGiver
                ? _workGiverResolver(key.WorkGiver)
                : null;
            Pawn pawn = key.IsGlobal ? null : _pawnResolver(key.Pawn);
            if (workType == null ||
                (key.TargetKind == WorkloadScheduleTargetKind.WorkGiver && workGiver == null) ||
                (!key.IsGlobal && pawn == null))
            {
                return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
            }

            int fallbackPriority;
            if (key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType)
            {
                fallbackPriority = ParentPriorityRead.GetLive(pawn, workType);
            }
            else
            {
                fallbackPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                    pawn,
                    workGiver,
                    WorkPrioritySystem.GetDefaultEnabledPriority());
            }

            if (!TimePriorityService.HasLiveCustomSchedule(target))
            {
                return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
            }

            TimePriorityScheduleValue schedule = TimePriorityService.ReadLiveSchedule(
                target,
                fallbackPriority);

            return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.Set(
                WorkloadTimePriorityAdapter.ToPayload(schedule));
        }

        private WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveSpecificJobPriorityV2(WorkloadSpecificJobTargetKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            }

            if (key.IsGlobal)
            {
                WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot snapshot =
                    WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(
                        key.WorkGiver.Value);
                switch (snapshot.State)
                {
                    case WorkGiverReassignmentManager.ExactGlobalStateKind.Set:
                        return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Set(
                            new WorkloadSpecificPriorityPayload(snapshot.Priority));
                    case WorkGiverReassignmentManager.ExactGlobalStateKind.Clear:
                        return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Clear;
                    default:
                        return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
                }
            }

            Pawn pawn = _pawnResolver(key.Pawn);
            WorkGiverDef workGiver = _workGiverResolver(key.WorkGiver);
            if (pawn != null && workGiver != null &&
                WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                    pawn,
                    workGiver,
                    out int priority))
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Set(
                    new WorkloadSpecificPriorityPayload(priority));
            }

            return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
        }

        private WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> ResolveWorkTypeOrderV2(
            WorkloadWorkTypeOrderKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }

            if (key.IsGlobal)
            {
                WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot snapshot =
                    WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(
                        key.WorkType.Value);
                return ResolveOrderSnapshot(snapshot.State, snapshot.OrderedWorkGiverNames);
            }

            if (!int.TryParse(
                    key.Pawn.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int pawnId) || pawnId < 0)
            {
                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }

            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot pawnSnapshot =
                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                    pawnId,
                    key.WorkType.Value);
            return pawnSnapshot.HasStoredOrder
                ? WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.Set(
                    new WorkloadWorkTypeOrderPayload(
                        ToWorkGiverKeys(pawnSnapshot.OrderedWorkGiverNames)))
                : WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
        }

        private static WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> ResolveOrderSnapshot(
            WorkGiverReassignmentManager.ExactGlobalStateKind state,
            IReadOnlyList<string> orderedNames)
        {
            switch (state)
            {
                case WorkGiverReassignmentManager.ExactGlobalStateKind.Set:
                    return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.Set(
                        new WorkloadWorkTypeOrderPayload(ToWorkGiverKeys(orderedNames)));
                case WorkGiverReassignmentManager.ExactGlobalStateKind.Clear:
                    return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.Clear;
                default:
                    return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }
        }

        private static IEnumerable<WorkGiverKey> ToWorkGiverKeys(
            IReadOnlyList<string> names)
        {
            var result = new List<WorkGiverKey>();
            if (names != null)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    result.Add(new WorkGiverKey(names[i]));
                }
            }

            return result;
        }

        private static Pawn ResolvePawn(PawnKey key)
        {
            if (key == null || !int.TryParse(
                    key.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int thingId))
            {
                return null;
            }

            return WorkloadPawnRosterCache.ResolvePawn(thingId);
        }

        private static WorkTypeDef ResolveWorkType(WorkTypeKey key)
        {
            return key == null || string.IsNullOrEmpty(key.Value)
                ? null
                : DefDatabase<WorkTypeDef>.GetNamedSilentFail(key.Value);
        }

        private static WorkGiverDef ResolveWorkGiver(WorkGiverKey key)
        {
            return key == null || string.IsNullOrEmpty(key.Value)
                ? null
                : DefDatabase<WorkGiverDef>.GetNamedSilentFail(key.Value);
        }
    }

    /// <summary>
    /// Turns the live invalidation/service versions into a monotonic local
    /// observation revision. It is only a cache stamp; it does not publish an
    /// invalidation and does not replace any authority-owned version.
    /// </summary>
    internal sealed class BwtLiveWorkTabEffectiveStateRevisionClock
    {
        private bool _initialized;
        private Inputs _last;
        private long _revision;

        internal long Read()
        {
            Inputs current = Inputs.Read();
            if (!_initialized)
            {
                _initialized = true;
                _last = current;
                return _revision;
            }

            if (!_last.Equals(current))
            {
                _last = current;
                _revision = unchecked(_revision + 1L);
            }

            return _revision;
        }

        internal WorkTabEffectiveStateRevisionVector ReadVector()
        {
            Read();
            Inputs current = Inputs.Read();
            WorkGridRevisionSet categories = WorkTabInvalidationHub.Current.CategoryRevisions;
            long persistenceRevision = Current.Game
                ?.GetComponent<GameComponent_BWTWorldSettings>()
                ?.EnsureWorkloadV2Persistence()
                ?.PersistenceRevision ?? 0L;
            return new WorkTabEffectiveStateRevisionVector(
                0L,
                current.EffectiveState,
                0L,
                persistenceRevision,
                current.AuthorityRevision,
                current.TimeVersion,
                current.SubWorkVersion,
                categories.SettingsThemeLanguageScale,
                categories.PawnListOrder);
        }

        private readonly struct Inputs : IEquatable<Inputs>
        {
            private Inputs(
                long effectiveState,
                int timeVersion,
                int subWorkVersion,
                long authorityRevision,
                bool manualMode,
                long registryGeneration,
                long explicitAuthorityGeneration)
            {
                EffectiveState = effectiveState;
                TimeVersion = timeVersion;
                SubWorkVersion = subWorkVersion;
                AuthorityRevision = authorityRevision;
                ManualMode = manualMode;
                RegistryGeneration = registryGeneration;
                ExplicitAuthorityGeneration = explicitAuthorityGeneration;
            }

            internal long EffectiveState { get; }
            internal int TimeVersion { get; }
            internal int SubWorkVersion { get; }
            internal long AuthorityRevision { get; }
            private bool ManualMode { get; }
            private long RegistryGeneration { get; }
            private long ExplicitAuthorityGeneration { get; }

            internal long EffectiveStateRevision => EffectiveState;
            internal int ScheduleRevision => TimeVersion;
            internal int SpecificRevision => SubWorkVersion;

            internal static Inputs Read()
            {
                return new Inputs(
                    WorkTabInvalidationHub.EffectiveStateRevision,
                    TimePriorityService.CurrentVersion,
                    WorkGiverReassignmentManager.CurrentSyncVersion,
                    PriorityAuthorityBroker.GetObservationalAuthorityRevision(),
                    ParentPriorityRead.GetLiveManualMode(true),
                    ExternalWorkTabRegistry.RegistryGeneration,
                    PriorityAuthorityResolver.ExplicitAuthorityGeneration);
            }

            public bool Equals(Inputs other)
            {
                return EffectiveState == other.EffectiveState &&
                       TimeVersion == other.TimeVersion &&
                       SubWorkVersion == other.SubWorkVersion &&
                       AuthorityRevision == other.AuthorityRevision &&
                       ManualMode == other.ManualMode &&
                       RegistryGeneration == other.RegistryGeneration &&
                       ExplicitAuthorityGeneration == other.ExplicitAuthorityGeneration;
            }

            public override bool Equals(object obj)
            {
                return obj is Inputs other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = EffectiveState.GetHashCode();
                    hash = (hash * 397) ^ TimeVersion;
                    hash = (hash * 397) ^ SubWorkVersion;
                    hash = (hash * 397) ^ AuthorityRevision.GetHashCode();
                    hash = (hash * 397) ^ (ManualMode ? 1 : 0);
                    hash = (hash * 397) ^ RegistryGeneration.GetHashCode();
                    return (hash * 397) ^ ExplicitAuthorityGeneration.GetHashCode();
                }
            }
        }
    }
}
