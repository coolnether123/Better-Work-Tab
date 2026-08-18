using System;
using System.Globalization;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Projection
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
        private readonly WorkTabEffectiveStateResolver<PawnKey, ScheduleKey> _scheduleResolver;
        private readonly WorkTabEffectiveStateResolver<string, WorkloadScalarValue> _presentationResolver;
        private readonly Func<long> _revisionResolver;
        private readonly BwtLiveWorkTabEffectiveStateRevisionClock _revisionClock;

        public BwtLiveWorkTabEffectiveStateAdapter(
            Func<PawnKey, Pawn> pawnResolver = null,
            Func<WorkTypeKey, WorkTypeDef> workTypeResolver = null,
            Func<WorkGiverKey, WorkGiverDef> workGiverResolver = null,
            WorkTabEffectiveStateResolver<PawnKey, ScheduleKey> scheduleResolver = null,
            WorkTabEffectiveStateResolver<string, WorkloadScalarValue> presentationResolver = null,
            Func<long> revisionResolver = null)
        {
            _pawnResolver = pawnResolver ?? ResolvePawn;
            _workTypeResolver = workTypeResolver ?? ResolveWorkType;
            _workGiverResolver = workGiverResolver ?? ResolveWorkGiver;
            _scheduleResolver = scheduleResolver;
            _presentationResolver = presentationResolver;
            _revisionResolver = revisionResolver;
            _revisionClock = new BwtLiveWorkTabEffectiveStateRevisionClock();
        }

        public LiveWorkTabEffectiveStateCallbacks CreateCallbacks(string providerId = "bwt.live")
        {
            var callbacks = new LiveWorkTabEffectiveStateCallbacks(providerId)
            {
                Revision = _revisionResolver ?? _revisionClock.Read,
                ParentPriority = TryGetParentPriority,
                ManualMode = TryGetManualMode,
                Schedule = _scheduleResolver,
                SpecificJobOverride = TryGetSpecificJobOverride,
                SpecificJobOrder = TryGetSpecificJobOrder,
                PresentationSetting = _presentationResolver
            };
            return callbacks;
        }

        public LiveWorkTabEffectiveStateProvider CreateProvider(
            string providerId = "bwt.live",
            IWorkTabEffectiveStateEditor editor = null)
        {
            return new LiveWorkTabEffectiveStateProvider(CreateCallbacks(providerId), editor);
        }

        private bool TryGetParentPriority(
            WorkloadParentPriorityKey key,
            out int priority)
        {
            priority = 0;
            if (key == null || !key.IsValid)
            {
                return false;
            }

            Pawn pawn = _pawnResolver(key.Pawn);
            WorkTypeDef workType = _workTypeResolver(key.WorkType);
            if (pawn == null || workType == null)
            {
                return false;
            }

            // A preview may fall through to the live provider for a value that it does not own.
            // Keep that fallback observational: the ordinary priority read owns transition
            // processing, while the preview read only observes the current authority snapshot.
            priority = WorkTabEffectiveStateRuntime.IsPreviewActive
                ? PriorityAuthorityBroker.GetObservationalEffectivePriority(pawn, workType)
                : WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            return true;
        }

        private bool TryGetManualMode(
            WorkloadParentPriorityKey key,
            out bool manualMode)
        {
            manualMode = false;
            if (key == null || !key.IsValid || Find.PlaySettings == null)
            {
                return false;
            }

            // Vanilla stores manual mode globally. The parent key is retained
            // in the callback shape so a future authority can scope it without
            // changing the provider contract.
            manualMode = Find.PlaySettings.useWorkPriorities;
            return true;
        }

        private bool TryGetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            if (key == null || !key.IsValid)
            {
                return false;
            }

            Pawn pawn = _pawnResolver(key.Pawn);
            WorkGiverDef workGiver = _workGiverResolver(key.WorkGiver);
            if (pawn == null || workGiver == null ||
                !WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                    pawn,
                    workGiver,
                    out int priority))
            {
                return false;
            }

            value = WorkloadScalarValue.FromInteger(priority);
            return true;
        }

        private bool TryGetSpecificJobOrder(
            WorkloadSpecificJobKey key,
            out int order)
        {
            order = 0;
            if (key == null || !key.IsValid)
            {
                return false;
            }

            Pawn pawn = _pawnResolver(key.Pawn);
            WorkTypeDef workType = _workTypeResolver(key.WorkType);
            if (workType == null)
            {
                return false;
            }

            var workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(
                workType,
                pawn);
            for (int i = 0; i < workGivers.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(
                    workGivers[i]?.def?.defName,
                    key.WorkGiver.Value))
                {
                    order = i;
                    return true;
                }
            }

            return false;
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

            var pawns = PawnsFinder.All_AliveOrDead;
            if (pawns == null)
            {
                return null;
            }

            foreach (Pawn pawn in pawns)
            {
                if (pawn != null && pawn.thingIDNumber == thingId)
                {
                    return pawn;
                }
            }

            return null;
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

            private long EffectiveState { get; }
            private int TimeVersion { get; }
            private int SubWorkVersion { get; }
            private long AuthorityRevision { get; }
            private bool ManualMode { get; }
            private long RegistryGeneration { get; }
            private long ExplicitAuthorityGeneration { get; }

            internal static Inputs Read()
            {
                return new Inputs(
                    WorkTabInvalidationHub.EffectiveStateRevision,
                    TimePriorityService.CurrentVersion,
                    WorkGiverReassignmentManager.CurrentSyncVersion,
                    PriorityAuthorityBroker.GetObservationalAuthorityRevision(),
                    Find.PlaySettings?.useWorkPriorities ?? true,
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
