using System;
using System.Collections.Generic;
using Better_Work_Tab.API;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.ModSupport;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal readonly struct ParentPriorityScheduleOverlay
    {
        private readonly int[] _priorities;
        internal readonly ParentPriorityOverlayState State;
        internal readonly int PinnedHourMask;

        internal ParentPriorityScheduleOverlay(
            ParentPriorityOverlayState state,
            int pinnedHourMask,
            int[] priorities)
        {
            State = state;
            PinnedHourMask = pinnedHourMask;
            _priorities = priorities;
        }

        internal static ParentPriorityScheduleOverlay Clear =>
            new ParentPriorityScheduleOverlay(ParentPriorityOverlayState.Clear, 0, null);

        internal bool TryGetPinnedPriority(int hour, out int priority)
        {
            priority = 0;
            if (State != ParentPriorityOverlayState.Set || hour < 0 ||
                hour >= TimePriorityService.HoursPerDay ||
                (PinnedHourMask & (1 << hour)) == 0 || _priorities == null ||
                hour >= _priorities.Length)
            {
                return false;
            }

            priority = _priorities[hour];
            return true;
        }
    }

    // Immutable neutral patch captured once by the composition boundary.
    internal sealed class ParentPriorityProjectionSnapshot
    {
        internal static readonly ParentPriorityProjectionSnapshot Empty =
            new ParentPriorityProjectionSnapshot(0L, null, null, null,
                default(ParentProjectionValue<bool>), false);

        private readonly IDictionary<ParentPriorityTarget, ParentProjectionValue<int>> _priorities;
        private readonly IDictionary<ParentPriorityTarget, ParentPriorityScheduleOverlay> _schedules;
        private readonly IDictionary<ParentPriorityTarget, ParentProjectionValue<bool>> _manualModes;

        internal ParentPriorityProjectionSnapshot(
            long revision,
            IDictionary<ParentPriorityTarget, ParentProjectionValue<int>> priorities,
            IDictionary<ParentPriorityTarget, ParentPriorityScheduleOverlay> schedules,
            IDictionary<ParentPriorityTarget, ParentProjectionValue<bool>> manualModes,
            ParentProjectionValue<bool> displayManualMode,
            bool hasConflictingManualModes)
        {
            Revision = revision;
            _priorities = priorities;
            _schedules = schedules;
            _manualModes = manualModes;
            DisplayManualMode = displayManualMode;
            HasConflictingManualModes = hasConflictingManualModes;
        }

        internal long Revision { get; }
        internal ParentProjectionValue<bool> DisplayManualMode { get; }
        internal bool HasConflictingManualModes { get; }
        internal ParentProjectionValue<int> PriorityFor(ParentPriorityTarget target) =>
            _priorities != null && _priorities.TryGetValue(target, out ParentProjectionValue<int> value)
                ? value : default(ParentProjectionValue<int>);
        internal ParentPriorityScheduleOverlay ScheduleFor(ParentPriorityTarget target) =>
            _schedules != null && _schedules.TryGetValue(target, out ParentPriorityScheduleOverlay value)
                ? value : default(ParentPriorityScheduleOverlay);
        internal ParentProjectionValue<bool> ManualModeFor(ParentPriorityTarget target) =>
            _manualModes != null && _manualModes.TryGetValue(target, out ParentProjectionValue<bool> value)
                ? value : default(ParentProjectionValue<bool>);
    }

    internal interface IParentPriorityProjection
    {
        long Revision { get; }
        ParentPriorityProjectionSnapshot Capture();
    }

    internal static class ParentPriorityRead
    {
        [ThreadStatic] private static Scope currentObservedScope;

        internal static int GetLive(Pawn pawn, WorkTypeDef workType)
        {
            for (int attempt = 0; attempt != 2; attempt++)
            {
                if (!TryCaptureAuthority(false, out PriorityAuthoritySnapshot authority,
                        out long authorityRevision)) break;
                int scheduleVersion = TimePriorityService.CurrentVersion;
                int priority = authority.Owner != PriorityAuthorityOwner.BetterWorkTab
                    ? ReadExternal(authority.AuthoritativeStore, pawn, workType)
                    : ReadLiveBwt(pawn, workType);
                if (scheduleVersion == TimePriorityService.CurrentVersion &&
                    TryCaptureAuthority(false, out PriorityAuthoritySnapshot current,
                        out long currentRevision) &&
                    Matches(authority, authorityRevision, current, currentRevision))
                {
                    return priority;
                }
            }

            return ReadStored(pawn, workType);
        }

        // Fresh observational capture deliberately bypasses any ambient preview scope.
        internal static int GetObservationalLive(Pawn pawn, WorkTypeDef workType) =>
            ObservedPass.Capture(null).Read(pawn, workType);

        internal static int GetObserved(Pawn pawn, WorkTypeDef workType) =>
            CurrentObservedPass().Read(pawn, workType);

        internal static bool GetLiveManualMode(bool fallback) =>
            Find.PlaySettings == null ? fallback : Find.PlaySettings.useWorkPriorities;

        internal static bool GetObservedManualMode(
            Pawn pawn,
            WorkTypeDef workType,
            bool fallback)
            => CurrentObservedPass().ReadManual(pawn, workType, fallback);

        internal static bool GetObservedManualModeForDisplay(bool fallback) =>
            CurrentObservedPass().ReadDisplayManual(fallback);

        internal static bool TrySetObserved(Pawn pawn, WorkTypeDef workType, int priority) =>
            currentObservedScope?.Writer != null &&
            currentObservedScope.Writer(pawn, workType, priority);

        internal static IDisposable PushObservedPass(
            IParentPriorityProjection projection,
            Func<Pawn, WorkTypeDef, int, bool> writer)
        {
            var scope = new Scope(ObservedPass.Capture(projection), writer, currentObservedScope);
            currentObservedScope = scope;
            return scope;
        }

        internal static ParentPriorityTarget TargetFor(Pawn pawn, WorkTypeDef workType) =>
            new ParentPriorityTarget(pawn?.thingIDNumber ?? 0, workType?.defName);

        private static ObservedPass CurrentObservedPass() =>
            currentObservedScope?.Pass ?? ObservedPass.Capture(null);

        private static int ReadLiveBwt(Pawn pawn, WorkTypeDef workType)
        {
            int priority = ReadStored(pawn, workType);
            return priority > 0 && TimePriorityService.TryGetLiveWorkTypeScheduledPriority(
                pawn, workType, TimePriorityService.GetCurrentHour(pawn), out int scheduled)
                ? scheduled : priority;
        }

        private static int ReadExternal(IExternalWorkTabStore store, Pawn pawn, WorkTypeDef workType)
        {
            return store != null && ExternalWorkTabRegistry.TryGetWorkTypePriority(
                store, pawn, workType, TimePriorityService.GetCurrentHour(pawn), out int priority)
                ? Clamp(priority) : ReadStored(pawn, workType);
        }

        private static int ReadExternal(
            IExternalWorkTabStore store,
            Pawn pawn,
            WorkTypeDef workType,
            bool? manualMode)
        {
            return store != null && ExternalWorkTabRegistry.TryGetWorkTypePriority(
                store, pawn, workType, TimePriorityService.GetCurrentHour(pawn), out int priority)
                ? Clamp(priority) : ReadStored(pawn, workType, manualMode);
        }

        // Checkbox mode is normalized here; raw 1/2 values stay in command/capture paths.
        private static int ReadStored(Pawn pawn, WorkTypeDef workType)
        {
            return pawn?.workSettings == null || workType == null
                ? PriorityConstants.VanillaDefaultEnabled
                : Clamp(PriorityAuthorityBroker.GetVanillaCompatibleStoredPriority(
                    pawn, pawn.workSettings, workType));
        }

        private static int ReadStored(Pawn pawn, WorkTypeDef workType, bool? manualMode)
        {
            return pawn?.workSettings == null || workType == null
                ? PriorityConstants.VanillaDefaultEnabled
                : Clamp(PriorityAuthorityBroker.GetVanillaCompatibleStoredPriority(
                    pawn, pawn.workSettings, workType, manualMode));
        }

        private static int Clamp(int priority) => priority < PriorityConstants.Disabled
            ? PriorityConstants.Disabled : priority > PriorityConstants.ExtendedHardMax
                ? PriorityConstants.ExtendedHardMax : priority;

        private static bool TryCaptureAuthority(
            bool observational,
            out PriorityAuthoritySnapshot snapshot,
            out long revision)
        {
            if (observational)
                PriorityAuthorityBroker.CaptureObservationalAuthority(out snapshot, out revision);
            else
            {
                PriorityAuthorityOwner owner = PriorityAuthorityBroker.CurrentAuthority;
                snapshot = PriorityAuthorityResolver.Resolve();
                revision = PriorityAuthorityResolver.CurrentAuthorityRevision;
                if (owner != snapshot.Owner) return false;
            }

            return snapshot.IsCoherent && (snapshot.Owner == PriorityAuthorityOwner.BetterWorkTab ||
                snapshot.AuthoritativeStore != null && !string.IsNullOrEmpty(snapshot.StoreId) &&
                snapshot.StoreRegistrationGeneration != 0L);
        }

        private static bool Matches(
            PriorityAuthoritySnapshot left,
            long leftRevision,
            PriorityAuthoritySnapshot right,
            long rightRevision)
        {
            return leftRevision == rightRevision && left.Owner == right.Owner &&
                ReferenceEquals(left.AuthoritativeStore, right.AuthoritativeStore) &&
                left.StoreRegistrationGeneration == right.StoreRegistrationGeneration &&
                StringComparer.Ordinal.Equals(left.StoreId, right.StoreId);
        }

        private sealed class ObservedPass
        {
            private readonly bool _valid;
            private readonly PriorityAuthoritySnapshot _authority;
            private readonly ParentPriorityProjectionSnapshot _projection;
            private readonly TimePriorityService.WorkTypeScheduleReadSnapshot _schedules;
            private readonly bool _hasLiveManualMode;
            private readonly bool _liveManualMode;

            private ObservedPass(
                bool valid,
                PriorityAuthoritySnapshot authority,
                ParentPriorityProjectionSnapshot projection,
                TimePriorityService.WorkTypeScheduleReadSnapshot schedules,
                bool hasLiveManualMode,
                bool liveManualMode)
            {
                _valid = valid;
                _authority = authority;
                _projection = projection ?? ParentPriorityProjectionSnapshot.Empty;
                _schedules = schedules ?? TimePriorityService.WorkTypeScheduleReadSnapshot.Empty;
                _hasLiveManualMode = hasLiveManualMode;
                _liveManualMode = liveManualMode;
            }

            internal static ObservedPass Capture(IParentPriorityProjection projection)
            {
                var settings = Find.PlaySettings;
                bool hasLiveManualMode = settings != null;
                bool liveManualMode = hasLiveManualMode && settings.useWorkPriorities;
                for (int attempt = 0; attempt != 2; attempt++)
                {
                    if (!TryCaptureAuthority(true, out PriorityAuthoritySnapshot authority,
                            out long authorityRevision)) break;
                    int scheduleVersion = TimePriorityService.CurrentVersion;
                    ParentPriorityProjectionSnapshot snapshot = projection?.Capture() ??
                        ParentPriorityProjectionSnapshot.Empty;
                    TimePriorityService.WorkTypeScheduleReadSnapshot schedules =
                        authority.Owner != PriorityAuthorityOwner.BetterWorkTab
                        ? TimePriorityService.WorkTypeScheduleReadSnapshot.Empty
                        : TimePriorityService.CaptureLiveWorkTypeScheduleSnapshot();
                    if (scheduleVersion == TimePriorityService.CurrentVersion &&
                        snapshot.Revision == (projection?.Revision ?? 0L) &&
                        TryCaptureAuthority(true, out PriorityAuthoritySnapshot current,
                            out long currentRevision) &&
                        Matches(authority, authorityRevision, current, currentRevision))
                    {
                        return new ObservedPass(true, authority, snapshot, schedules,
                            hasLiveManualMode, liveManualMode);
                    }
                }

                return new ObservedPass(false, default(PriorityAuthoritySnapshot), projection?.Capture() ??
                    ParentPriorityProjectionSnapshot.Empty,
                    TimePriorityService.WorkTypeScheduleReadSnapshot.Empty,
                    hasLiveManualMode,
                    liveManualMode);
            }

            internal int Read(Pawn pawn, WorkTypeDef workType)
            {
                ParentPriorityTarget target = TargetFor(pawn, workType);
                bool? manualMode = CapturedManualModeFor(target);
                if (!target.IsValid || pawn?.workSettings == null)
                    return ReadStored(pawn, workType, manualMode);
                if (!_valid) return ReadStored(pawn, workType, manualMode);
                ParentProjectionValue<int> overlay = _projection.PriorityFor(target);
                if (_authority.Owner != PriorityAuthorityOwner.BetterWorkTab)
                {
                    return ParentPriorityReadPolicy.Resolve(true, overlay,
                        0, false, 0, ReadExternal(_authority.AuthoritativeStore, pawn, workType, manualMode));
                }

                int hour = TimePriorityService.GetCurrentHour(pawn);
                bool pinned = TryGetPinned(target, pawn, workType, hour, out int scheduled);
                return ParentPriorityReadPolicy.Resolve(false, overlay,
                    overlay.IsSet
                        ? ParentPriorityReadPolicy.NormalizeForDisplay(
                            pawn?.RaceProps != null && pawn.RaceProps.Humanlike,
                            overlay.Value,
                            manualMode)
                        : ReadStored(pawn, workType, manualMode),
                    pinned, scheduled, 0);
            }

            internal bool ReadManual(Pawn pawn, WorkTypeDef workType, bool fallback)
            {
                ParentPriorityTarget target = TargetFor(pawn, workType);
                ParentProjectionValue<bool> overlay = target.IsValid
                    ? _projection.ManualModeFor(target) : default(ParentProjectionValue<bool>);
                return overlay.IsSet ? overlay.Value : LiveManualModeOr(fallback);
            }

            internal bool ReadDisplayManual(bool fallback)
            {
                return !_projection.HasConflictingManualModes && _projection.DisplayManualMode.IsSet
                    ? _projection.DisplayManualMode.Value : LiveManualModeOr(fallback);
            }

            private bool LiveManualModeOr(bool fallback) =>
                _hasLiveManualMode ? _liveManualMode : fallback;

            private bool? CapturedManualModeFor(ParentPriorityTarget target)
            {
                ParentProjectionValue<bool> overlay = _projection.ManualModeFor(target);
                return overlay.IsSet ? (bool?)overlay.Value :
                    _hasLiveManualMode ? (bool?)_liveManualMode : null;
            }

            private bool TryGetPinned(
                ParentPriorityTarget target,
                Pawn pawn,
                WorkTypeDef workType,
                int hour,
                out int priority)
            {
                ParentPriorityScheduleOverlay overlay = _projection.ScheduleFor(target);
                if (overlay.State == ParentPriorityOverlayState.Set)
                    return overlay.TryGetPinnedPriority(hour, out priority);
                if (overlay.State == ParentPriorityOverlayState.Clear)
                {
                    priority = 0;
                    return false;
                }

                return _schedules.TryGetPinnedPriority(pawn, workType, hour, out priority);
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly Scope _parent;
            private bool _disposed;
            internal Scope(
                ObservedPass pass,
                Func<Pawn, WorkTypeDef, int, bool> writer,
                Scope parent)
            {
                Pass = pass;
                Writer = writer;
                _parent = parent;
            }

            internal ObservedPass Pass { get; }
            internal Func<Pawn, WorkTypeDef, int, bool> Writer { get; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (ReferenceEquals(currentObservedScope, this)) RestoreNearestLiveParent();
            }

            private void RestoreNearestLiveParent()
            {
                Scope scope = _parent;
                while (scope != null && scope._disposed) scope = scope._parent;
                currentObservedScope = scope;
            }
        }

    }
}
