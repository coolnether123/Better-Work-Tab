using System;
using Better_Work_Tab.Features.Workloads.V2;

namespace Better_Work_Tab.UI.WorkGrid.Projection
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

        public WorkTabEffectiveStateResolver<PawnKey, ScheduleKey> Schedule { get; set; }
        public WorkTabEffectiveStateResolver<WorkloadSpecificJobKey, WorkloadScalarValue> SpecificJobOverride { get; set; }
        public WorkTabEffectiveStateResolver<WorkloadSpecificJobKey, int> SpecificJobOrder { get; set; }
        public WorkTabEffectiveStateResolver<string, WorkloadScalarValue> PresentationSetting { get; set; }
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
        IWorkTabEffectiveStateEditor
    {
        private readonly LiveWorkTabEffectiveStateCallbacks _callbacks;
        private readonly IWorkTabEffectiveStateEditor _editor;

        public LiveWorkTabEffectiveStateProvider(
            LiveWorkTabEffectiveStateCallbacks callbacks,
            IWorkTabEffectiveStateEditor editor = null)
        {
            _callbacks = callbacks ?? new LiveWorkTabEffectiveStateCallbacks();
            _editor = editor ?? new BlockedWorkTabEffectiveStateEditor(
                () => Revision);
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

        public IWorkTabEffectiveStateEditor Editor => _editor;

        public ScheduleKey GetSchedule(PawnKey key, ScheduleKey fallbackSchedule)
        {
            return TryGetSchedule(key, out ScheduleKey schedule) ? schedule : fallbackSchedule;
        }

        public bool TryGetSchedule(PawnKey key, out ScheduleKey schedule)
        {
            schedule = null;
            return key != null && key.IsValid &&
                   _callbacks.Schedule != null &&
                   _callbacks.Schedule(key, out schedule);
        }

        public WorkloadScalarValue GetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            WorkloadScalarValue fallbackValue)
        {
            return TryGetSpecificJobOverride(key, out WorkloadScalarValue value)
                ? value
                : fallbackValue;
        }

        public bool TryGetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            return key != null && key.IsValid &&
                   _callbacks.SpecificJobOverride != null &&
                   _callbacks.SpecificJobOverride(key, out value);
        }

        public int GetSpecificJobOrder(WorkloadSpecificJobKey key, int fallbackOrder)
        {
            return TryGetSpecificJobOrder(key, out int order) ? order : fallbackOrder;
        }

        public bool TryGetSpecificJobOrder(WorkloadSpecificJobKey key, out int order)
        {
            order = 0;
            return key != null && key.IsValid &&
                   _callbacks.SpecificJobOrder != null &&
                   _callbacks.SpecificJobOrder(key, out order);
        }

        public WorkloadScalarValue GetPresentationSetting(
            string key,
            WorkloadScalarValue fallbackValue)
        {
            return TryGetPresentationSetting(key, out WorkloadScalarValue value)
                ? value
                : fallbackValue;
        }

        public bool TryGetPresentationSetting(string key, out WorkloadScalarValue value)
        {
            value = WorkloadScalarValue.Empty;
            return !string.IsNullOrWhiteSpace(key) &&
                   _callbacks.PresentationSetting != null &&
                   _callbacks.PresentationSetting(key, out value);
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

            if (_callbacks.SpecificJobPriorityV2 != null)
            {
                return _callbacks.SpecificJobPriorityV2(key);
            }

            WorkloadScalarValue value;
            return _callbacks.SpecificJobOverride != null &&
                   _callbacks.SpecificJobOverride(key.ToLegacyKey(), out value) &&
                   value.Kind == WorkloadScalarKind.Integer
                ? WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Set(
                    new WorkloadSpecificPriorityPayload(value.IntegerValue))
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

            if (_callbacks.PresentationSettingV2 != null)
            {
                return _callbacks.PresentationSettingV2(key);
            }

            return _callbacks.PresentationSetting != null &&
                   _callbacks.PresentationSetting(key, out WorkloadScalarValue value)
                ? WorkTabEffectiveStateResolution<WorkloadSettingValue>.Set(
                    WorkloadSettingValue.Global(value))
                : WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
        }

        public WorkTabEffectiveStateMutationResult SetSchedule(
            PawnKey key,
            ScheduleKey schedule)
        {
            return _editor.SetSchedule(key, schedule);
        }

        public WorkTabEffectiveStateMutationResult ClearSchedule(PawnKey key)
        {
            return _editor.ClearSchedule(key);
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            WorkloadScalarValue value)
        {
            return _editor.SetSpecificJobOverride(key, value);
        }

        public WorkTabEffectiveStateMutationResult ClearSpecificJobOverride(
            WorkloadSpecificJobKey key)
        {
            return _editor.ClearSpecificJobOverride(key);
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobOrder(
            WorkloadSpecificJobKey key,
            int order)
        {
            return _editor.SetSpecificJobOrder(key, order);
        }

        public WorkTabEffectiveStateMutationResult ClearSpecificJobOrder(
            WorkloadSpecificJobKey key)
        {
            return _editor.ClearSpecificJobOrder(key);
        }

        public WorkTabEffectiveStateMutationResult SetPresentationSetting(
            string key,
            WorkloadScalarValue value)
        {
            return _editor.SetPresentationSetting(key, value);
        }

        public WorkTabEffectiveStateMutationResult ClearPresentationSetting(string key)
        {
            return _editor.ClearPresentationSetting(key);
        }
    }

    public sealed class LiveWorkTabEffectiveStateEditorCallbacks
    {
        public Func<PawnKey, ScheduleKey, WorkTabEffectiveStateMutationResult> SetSchedule { get; set; }
        public Func<PawnKey, WorkTabEffectiveStateMutationResult> ClearSchedule { get; set; }
        public Func<WorkloadSpecificJobKey, WorkloadScalarValue, WorkTabEffectiveStateMutationResult> SetSpecificJobOverride { get; set; }
        public Func<WorkloadSpecificJobKey, WorkTabEffectiveStateMutationResult> ClearSpecificJobOverride { get; set; }
        public Func<WorkloadSpecificJobKey, int, WorkTabEffectiveStateMutationResult> SetSpecificJobOrder { get; set; }
        public Func<WorkloadSpecificJobKey, WorkTabEffectiveStateMutationResult> ClearSpecificJobOrder { get; set; }
        public Func<string, WorkloadScalarValue, WorkTabEffectiveStateMutationResult> SetPresentationSetting { get; set; }
        public Func<string, WorkTabEffectiveStateMutationResult> ClearPresentationSetting { get; set; }
    }

    /// <summary>
    /// Adapts mutation callbacks owned by the live authority. Missing callbacks
    /// fail closed as Blocked, which keeps a read-only live provider from
    /// silently pretending that a virtual write was applied.
    /// </summary>
    public sealed class CallbackWorkTabEffectiveStateEditor : IWorkTabEffectiveStateEditor
    {
        private readonly LiveWorkTabEffectiveStateEditorCallbacks _callbacks;
        private readonly Func<long> _revision;

        public CallbackWorkTabEffectiveStateEditor(
            LiveWorkTabEffectiveStateEditorCallbacks callbacks,
            Func<long> revision = null)
        {
            _callbacks = callbacks ?? new LiveWorkTabEffectiveStateEditorCallbacks();
            _revision = revision;
        }

        public WorkTabEffectiveStateMutationResult SetSchedule(PawnKey key, ScheduleKey schedule)
        {
            return Invoke(WorkTabEffectiveStateDimension.Schedule, _callbacks.SetSchedule, key, schedule);
        }

        public WorkTabEffectiveStateMutationResult ClearSchedule(PawnKey key)
        {
            return Invoke(WorkTabEffectiveStateDimension.Schedule, _callbacks.ClearSchedule, key);
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobOverride(WorkloadSpecificJobKey key, WorkloadScalarValue value)
        {
            return Invoke(WorkTabEffectiveStateDimension.SpecificJobOverride, _callbacks.SetSpecificJobOverride, key, value);
        }

        public WorkTabEffectiveStateMutationResult ClearSpecificJobOverride(WorkloadSpecificJobKey key)
        {
            return Invoke(WorkTabEffectiveStateDimension.SpecificJobOverride, _callbacks.ClearSpecificJobOverride, key);
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobOrder(WorkloadSpecificJobKey key, int order)
        {
            return Invoke(WorkTabEffectiveStateDimension.SpecificJobOrder, _callbacks.SetSpecificJobOrder, key, order);
        }

        public WorkTabEffectiveStateMutationResult ClearSpecificJobOrder(WorkloadSpecificJobKey key)
        {
            return Invoke(WorkTabEffectiveStateDimension.SpecificJobOrder, _callbacks.ClearSpecificJobOrder, key);
        }

        public WorkTabEffectiveStateMutationResult SetPresentationSetting(string key, WorkloadScalarValue value)
        {
            return Invoke(WorkTabEffectiveStateDimension.PresentationSetting, _callbacks.SetPresentationSetting, key, value);
        }

        public WorkTabEffectiveStateMutationResult ClearPresentationSetting(string key)
        {
            return Invoke(WorkTabEffectiveStateDimension.PresentationSetting, _callbacks.ClearPresentationSetting, key);
        }

        private WorkTabEffectiveStateMutationResult Invoke<TKey, TValue>(
            WorkTabEffectiveStateDimension dimension,
            Func<TKey, TValue, WorkTabEffectiveStateMutationResult> callback,
            TKey key,
            TValue value)
        {
            return callback == null
                ? Blocked(dimension, "The live authority did not provide this mutation.")
                : callback(key, value);
        }

        private WorkTabEffectiveStateMutationResult Invoke<TKey>(
            WorkTabEffectiveStateDimension dimension,
            Func<TKey, WorkTabEffectiveStateMutationResult> callback,
            TKey key)
        {
            return callback == null
                ? Blocked(dimension, "The live authority did not provide this mutation.")
                : callback(key);
        }

        private WorkTabEffectiveStateMutationResult Blocked(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            return WorkTabEffectiveStateMutationResult.Blocked(
                dimension,
                _revision?.Invoke() ?? 0L,
                reason);
        }
    }

    public sealed class BlockedWorkTabEffectiveStateEditor : IWorkTabEffectiveStateEditor
    {
        private readonly Func<long> _revision;

        public BlockedWorkTabEffectiveStateEditor(Func<long> revision = null)
        {
            _revision = revision;
        }

        public WorkTabEffectiveStateMutationResult SetSchedule(PawnKey key, ScheduleKey schedule) =>
            Blocked(WorkTabEffectiveStateDimension.Schedule);
        public WorkTabEffectiveStateMutationResult ClearSchedule(PawnKey key) =>
            Blocked(WorkTabEffectiveStateDimension.Schedule);
        public WorkTabEffectiveStateMutationResult SetSpecificJobOverride(WorkloadSpecificJobKey key, WorkloadScalarValue value) =>
            Blocked(WorkTabEffectiveStateDimension.SpecificJobOverride);
        public WorkTabEffectiveStateMutationResult ClearSpecificJobOverride(WorkloadSpecificJobKey key) =>
            Blocked(WorkTabEffectiveStateDimension.SpecificJobOverride);
        public WorkTabEffectiveStateMutationResult SetSpecificJobOrder(WorkloadSpecificJobKey key, int order) =>
            Blocked(WorkTabEffectiveStateDimension.SpecificJobOrder);
        public WorkTabEffectiveStateMutationResult ClearSpecificJobOrder(WorkloadSpecificJobKey key) =>
            Blocked(WorkTabEffectiveStateDimension.SpecificJobOrder);
        public WorkTabEffectiveStateMutationResult SetPresentationSetting(string key, WorkloadScalarValue value) =>
            Blocked(WorkTabEffectiveStateDimension.PresentationSetting);
        public WorkTabEffectiveStateMutationResult ClearPresentationSetting(string key) =>
            Blocked(WorkTabEffectiveStateDimension.PresentationSetting);

        private WorkTabEffectiveStateMutationResult Blocked(WorkTabEffectiveStateDimension dimension)
        {
            return WorkTabEffectiveStateMutationResult.Blocked(
                dimension,
                _revision?.Invoke() ?? 0L,
                "This effective-state source is read-only.");
        }
    }
}
