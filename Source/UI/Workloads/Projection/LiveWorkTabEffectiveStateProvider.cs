using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace Better_Work_Tab.UI.Workloads.Projection
{
    /// <summary>
    /// Narrow callback surface for the authoritative BWT services. The live
    /// provider stores delegates only; it does not copy or own priority,
    /// schedule, sub-work, or settings data.
    /// </summary>
    public sealed class LiveWorkTabEffectiveStateCallbacks
    {
        public LiveWorkTabEffectiveStateCallbacks(string providerId = "bwt.live")
        {
            ProviderId = string.IsNullOrWhiteSpace(providerId) ? "bwt.live" : providerId;
        }

        public string ProviderId { get; }
        public Func<long> Revision { get; set; }
        public Func<WorkTabEffectiveStateRevisionVector> RevisionVector { get; set; }

        public Func<WorkloadScheduleTargetKey, WorkTabEffectiveStateResolution<WorkloadSchedulePayload>> ScheduleV2 { get; set; }
        public Func<WorkloadSpecificJobTargetKey, WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>> SpecificJobPriorityV2 { get; set; }
        public Func<WorkloadWorkTypeOrderKey, WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>> WorkTypeOrderV2 { get; set; }
        public Func<string, WorkTabEffectiveStateResolution<WorkloadSettingValue>> PresentationSettingV2 { get; set; }
    }

    /// <summary>
    /// Live effective-state reader. All values are resolved on demand through
    /// the supplied callbacks, so an authority transition remains owned by the
    /// existing service rather than being duplicated here.
    /// </summary>
    public sealed class LiveWorkTabEffectiveStateProvider :
        IWorkTabEffectiveStateProvider,
        IWorkTabEffectiveStateV2Provider,
        IWorkTabEffectiveStateViewSource
    {
        private readonly LiveWorkTabEffectiveStateCallbacks _callbacks;

        public LiveWorkTabEffectiveStateProvider(
            LiveWorkTabEffectiveStateCallbacks callbacks)
        {
            _callbacks = callbacks ?? new LiveWorkTabEffectiveStateCallbacks();
        }

        public string ProviderId => _callbacks.ProviderId;

        public long Revision => _callbacks.Revision?.Invoke() ?? 0L;

        public WorkTabEffectiveStateRevisionVector RevisionVector =>
            _callbacks.RevisionVector?.Invoke() ??
            WorkTabEffectiveStateRevisionVector.FromRevision(Revision);

        public WorkTabEffectiveStateSource Source => WorkTabEffectiveStateSource.Live;

        public bool IsLive => true;
        public bool IsPreview => false;

        public WorkTabEffectiveStateRevision RevisionToken =>
            new WorkTabEffectiveStateRevision(ProviderId, Revision, Source, RevisionVector);

        /// <summary>
        /// Captures the live callbacks behind a pass-local read cache. A
        /// finished WorkTabView therefore cannot observe a different value for
        /// the same target after input has changed the live state mid-pass.
        /// The view owns the supplied revision token; live revision callbacks
        /// are intentionally never consulted by the captured provider.
        /// </summary>
        public IWorkTabEffectiveStateProvider CaptureEffectiveStateView(
            WorkTabEffectiveStateRevision revision)
        {
            return new CapturedLiveWorkTabEffectiveStateProvider(this, revision);
        }

        public WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveSchedule(
            WorkloadScheduleTargetKey key)
        {
            if (key == null || !key.IsValid || _callbacks.ScheduleV2 == null)
            {
                return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
            }

            return _callbacks.ScheduleV2(key);
        }

        public WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveSpecificJobPriority(WorkloadSpecificJobTargetKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            }

            return _callbacks.SpecificJobPriorityV2 != null
                ? _callbacks.SpecificJobPriorityV2(key)
                : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
        }

        public WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>
            ResolveWorkTypeOrder(WorkloadWorkTypeOrderKey key)
        {
            if (key == null || !key.IsValid || _callbacks.WorkTypeOrderV2 == null)
            {
                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }

            return _callbacks.WorkTypeOrderV2(key);
        }

        public WorkTabEffectiveStateResolution<WorkloadSettingValue>
            ResolvePresentationSetting(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
            }

            return _callbacks.PresentationSettingV2 != null
                ? _callbacks.PresentationSettingV2(key)
                : WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
        }

    }

    /// <summary>
    /// Immutable-revision pass view over a live provider. The effective-state
    /// surface is key based, so snapshots are memoized by semantic key instead
    /// of copying unbounded game state that no pass will read.
    /// </summary>
    internal sealed class CapturedLiveWorkTabEffectiveStateProvider :
        IWorkTabEffectiveStateProvider,
        IWorkTabEffectiveStateV2Provider
    {
        private readonly LiveWorkTabEffectiveStateProvider _live;
        private readonly WorkTabEffectiveStateRevision _revision;
        private Dictionary<WorkloadScheduleTargetKey,
            WorkTabEffectiveStateResolution<WorkloadSchedulePayload>> _schedules;
        private Dictionary<WorkloadSpecificJobTargetKey,
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>> _specificPriorities;
        private Dictionary<WorkloadWorkTypeOrderKey,
            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>> _orders;
        private Dictionary<string,
            WorkTabEffectiveStateResolution<WorkloadSettingValue>> _settings;

        internal CapturedLiveWorkTabEffectiveStateProvider(
            LiveWorkTabEffectiveStateProvider live,
            WorkTabEffectiveStateRevision revision)
        {
            _live = live ?? throw new ArgumentNullException(nameof(live));
            _revision = revision;
        }

        public string ProviderId => _revision.ProviderId;
        public long Revision => _revision.Revision;
        public WorkTabEffectiveStateRevisionVector RevisionVector => _revision.RevisionVector;
        public WorkTabEffectiveStateSource Source => _revision.Source;
        public bool IsLive => true;
        public bool IsPreview => false;
        public WorkTabEffectiveStateRevision RevisionToken => _revision;

        public WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveSchedule(
            WorkloadScheduleTargetKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
            }

            if (_schedules == null)
            {
                _schedules = new Dictionary<WorkloadScheduleTargetKey,
                    WorkTabEffectiveStateResolution<WorkloadSchedulePayload>>();
            }

            if (!_schedules.TryGetValue(key, out WorkTabEffectiveStateResolution<WorkloadSchedulePayload> value))
            {
                value = _live.ResolveSchedule(key);
                _schedules.Add(key, value);
            }

            return value;
        }

        public WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveSpecificJobPriority(WorkloadSpecificJobTargetKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            }

            if (_specificPriorities == null)
            {
                _specificPriorities = new Dictionary<WorkloadSpecificJobTargetKey,
                    WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>>();
            }

            if (!_specificPriorities.TryGetValue(
                    key,
                    out WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> value))
            {
                value = _live.ResolveSpecificJobPriority(key);
                _specificPriorities.Add(key, value);
            }

            return value;
        }

        public WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> ResolveWorkTypeOrder(
            WorkloadWorkTypeOrderKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }

            if (_orders == null)
            {
                _orders = new Dictionary<WorkloadWorkTypeOrderKey,
                    WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>>();
            }

            if (!_orders.TryGetValue(key, out WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> value))
            {
                value = _live.ResolveWorkTypeOrder(key);
                _orders.Add(key, value);
            }

            return value;
        }

        public WorkTabEffectiveStateResolution<WorkloadSettingValue> ResolvePresentationSetting(
            string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
            }

            if (_settings == null)
            {
                _settings = new Dictionary<string,
                    WorkTabEffectiveStateResolution<WorkloadSettingValue>>(
                    StringComparer.Ordinal);
            }

            if (!_settings.TryGetValue(key, out WorkTabEffectiveStateResolution<WorkloadSettingValue> value))
            {
                value = _live.ResolvePresentationSetting(key);
                _settings.Add(key, value);
            }

            return value;
        }
    }

}
