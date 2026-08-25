using System;
using System.Collections.Generic;
using System.Threading;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace Better_Work_Tab.UI.Workloads.Projection
{
    /// <summary>
    /// Session-local effective-state overlay backed by a WorkloadDraft. The
    /// draft owns projected values; this provider only indexes the immutable
    /// projected snapshot and optionally falls through to a base provider for
    /// values not represented by the draft.
    /// </summary>
    public sealed class ProjectedWorkTabEffectiveStateProvider :
        IWorkTabEffectiveStateProvider,
        IWorkTabEffectiveStateV2Provider,
        IWorkTabEffectiveStateV2Editor,
        IWorkTabPreviewStateReader,
        IWorkTabPreviewStateEditor,
        IWorkTabComposedEffectiveStateProvider,
        IWorkTabPreviewOwnership,
        IWorkTabEffectiveStatePassParticipant,
        IWorkTabEffectiveStateViewSource
    {
        private static long NextProviderGeneration;
        private readonly WorkloadDraft _draft;
        private readonly IWorkTabEffectiveStateProvider _baseProvider;
        private readonly WorkloadOwnershipDimensions _ownedDimensions;
        private readonly string _providerId;
        private readonly WorkloadScope _scope;
        private HashSet<PawnKey> _editablePawnIds;
        private bool _hasEditablePawnBoundary;

        private Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue> _specificJobOverrides =
            new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
        private Dictionary<string, WorkloadScalarValue> _presentationSettings =
            new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
        private Dictionary<WorkloadScheduleTargetKey, WorkloadIntent<WorkloadSchedulePayload>> _scheduleIntents =
            new Dictionary<WorkloadScheduleTargetKey, WorkloadIntent<WorkloadSchedulePayload>>();
        private Dictionary<WorkloadSpecificJobTargetKey, WorkloadIntent<WorkloadSpecificPriorityPayload>> _specificPriorityIntents =
            new Dictionary<WorkloadSpecificJobTargetKey, WorkloadIntent<WorkloadSpecificPriorityPayload>>();
        private Dictionary<WorkloadWorkTypeOrderKey, WorkloadIntent<WorkloadWorkTypeOrderPayload>> _workTypeOrderIntents =
            new Dictionary<WorkloadWorkTypeOrderKey, WorkloadIntent<WorkloadWorkTypeOrderPayload>>();
        private Dictionary<string, WorkloadIntent<WorkloadSettingValue>> _presentationSettingIntents =
            new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(StringComparer.Ordinal);
        private HashSet<string> _presentationOwnershipKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private bool _presentationOwnershipTouched;

        private WorkloadProjectedState _projectedState;
        private string _semanticFingerprint;
        private long _baseRevision;
        private long _draftRevision;
        private long _observedDraftRevision;
        private long _revision;
        private bool _hasProjection;
        private long _capturedBaseRevision;
        private WorkTabEffectiveStateRevisionVector _capturedBaseRevisionVector;
        private long _capturedBasePassId;
        private bool _hasCapturedBaseRevision;
        private bool _hasCapturedBaseRevisionVector;
        private WorkTabEffectiveStateRevisionVector _baseRevisionVector;
        private bool _hasBaseRevisionVector;
        private readonly long _providerGeneration;
        private long _scheduleRevision;
        private long _specificRevision;
        private long _settingsRevision;
        private long _membershipRevision;
        private readonly bool _isCapturedView;
        private readonly WorkTabEffectiveStateRevision _capturedViewRevision;

        public ProjectedWorkTabEffectiveStateProvider(
            WorkloadDraft draft,
            IWorkTabEffectiveStateProvider baseProvider = null,
            WorkloadOwnershipDimensions ownedDimensions = WorkloadOwnershipDimensions.All,
            string providerId = "bwt.preview",
            WorkloadScope scope = null,
            IEnumerable<PawnKey> editablePawnIds = null)
        {
            _draft = draft ?? new WorkloadDraft(WorkloadProjectedState.Empty);
            _providerGeneration = Interlocked.Increment(ref NextProviderGeneration);
            _baseProvider = baseProvider;
            _ownedDimensions = ownedDimensions;
            _providerId = string.IsNullOrWhiteSpace(providerId) ? "bwt.preview" : providerId;
            _scope = scope;
            _hasEditablePawnBoundary = editablePawnIds != null;
            _editablePawnIds = new HashSet<PawnKey>();
            if (editablePawnIds != null)
            {
                foreach (PawnKey pawn in editablePawnIds)
                {
                    if (pawn != null && pawn.IsValid)
                    {
                        _editablePawnIds.Add(pawn);
                    }
                }
            }
            RefreshProjection();
        }

        private ProjectedWorkTabEffectiveStateProvider(
            ProjectedWorkTabEffectiveStateProvider source,
            IWorkTabEffectiveStateProvider baseProvider,
            WorkTabEffectiveStateRevision revision)
        {
            _draft = source._draft;
            _baseProvider = baseProvider;
            _ownedDimensions = source._ownedDimensions;
            _providerId = source._providerId;
            _scope = source._scope;
            _editablePawnIds = source._editablePawnIds;
            _hasEditablePawnBoundary = source._hasEditablePawnBoundary;
            _specificJobOverrides = source._specificJobOverrides;
            _presentationSettings = source._presentationSettings;
            _scheduleIntents = source._scheduleIntents;
            _specificPriorityIntents = source._specificPriorityIntents;
            _workTypeOrderIntents = source._workTypeOrderIntents;
            _presentationSettingIntents = source._presentationSettingIntents;
            _presentationOwnershipKeys = source._presentationOwnershipKeys;
            _presentationOwnershipTouched = source._presentationOwnershipTouched;
            _projectedState = source._projectedState;
            _semanticFingerprint = source._semanticFingerprint;
            _baseRevision = source._baseRevision;
            _draftRevision = source._draftRevision;
            _observedDraftRevision = source._observedDraftRevision;
            _revision = source._revision;
            _hasProjection = source._hasProjection;
            _capturedBaseRevision = source._capturedBaseRevision;
            _capturedBaseRevisionVector = source._capturedBaseRevisionVector;
            _capturedBasePassId = source._capturedBasePassId;
            _hasCapturedBaseRevision = source._hasCapturedBaseRevision;
            _hasCapturedBaseRevisionVector = source._hasCapturedBaseRevisionVector;
            _baseRevisionVector = source._baseRevisionVector;
            _hasBaseRevisionVector = source._hasBaseRevisionVector;
            _providerGeneration = source._providerGeneration;
            _scheduleRevision = source._scheduleRevision;
            _specificRevision = source._specificRevision;
            _settingsRevision = source._settingsRevision;
            _membershipRevision = source._membershipRevision;
            _isCapturedView = true;
            _capturedViewRevision = revision;
        }

        public ProjectedWorkTabEffectiveStateProvider(
            WorkloadTemplate template,
            IWorkTabEffectiveStateProvider baseProvider = null,
            string providerId = "bwt.preview")
            : this(
                new WorkloadDraft(template?.ProjectedState ?? WorkloadProjectedState.Empty),
                baseProvider,
                template?.Definition?.OwnershipDimensions ?? WorkloadOwnershipDimensions.All,
                providerId,
                template?.Definition?.Scope,
                template?.ProjectedState?.RepresentedPawnIds)
        {
        }

        public string ProviderId => _providerId;
        public long Revision
        {
            get
            {
                if (_isCapturedView)
                {
                    return _capturedViewRevision.Revision;
                }

                RefreshProjection();
                return _revision;
            }
        }

        // Draft mutations refresh the projection synchronously. Consumers that
        // only need to detect another projected edit must not poll Revision:
        // that property also observes the live base provider and is intentionally
        // more expensive.
        public long ProjectionRevision => _draftRevision;
        public WorkloadDraft Draft => _draft;
        public WorkloadProjectedState ProjectedState
        {
            get
            {
                RefreshProjection();
                return _projectedState;
            }
        }

        public IWorkTabEffectiveStateProvider BaseProvider => _baseProvider;
        public WorkloadOwnershipDimensions OwnedDimensions => _ownedDimensions;
        public WorkTabEffectiveStateSource Source => WorkTabEffectiveStateSource.Preview;
        public bool IsLive => false;
        public bool IsPreview => true;
        public WorkTabEffectiveStateRevisionVector RevisionVector
        {
            get
            {
                if (_isCapturedView)
                {
                    return _capturedViewRevision.RevisionVector;
                }

                RefreshProjection();
                WorkTabEffectiveStateRevisionVector baseVector = ReadBaseRevisionVector();
                return new WorkTabEffectiveStateRevisionVector(
                    _providerGeneration,
                    CombineRevisions(baseVector.ProviderGeneration, baseVector.SourceRevision),
                    _draftRevision,
                    baseVector.PersistenceRevision,
                    baseVector.AuthorityRevision,
                    CombineRevisions(baseVector.ScheduleRevision, _scheduleRevision),
                    CombineRevisions(baseVector.SpecificRevision, _specificRevision),
                    CombineRevisions(baseVector.SettingsRevision, _settingsRevision),
                    CombineRevisions(baseVector.MembershipRevision, _membershipRevision));
            }
        }
        public WorkTabEffectiveStateRevision RevisionToken =>
            _isCapturedView
                ? _capturedViewRevision
                : new WorkTabEffectiveStateRevision(ProviderId, Revision, Source, RevisionVector);

        public IWorkTabEffectiveStateProvider CaptureEffectiveStateView(
            WorkTabEffectiveStateRevision revision)
        {
            RefreshProjection();
            IWorkTabEffectiveStateProvider capturedBase =
                _baseProvider is IWorkTabEffectiveStateViewSource source
                    ? source.CaptureEffectiveStateView(_baseProvider.RevisionToken)
                    : _baseProvider;
            return new ProjectedWorkTabEffectiveStateProvider(this, capturedBase, revision);
        }

        /// <summary>
        /// Returns whether a pawn is a legal target for a projected mutation.
        /// The gateway supplies the live membership result for current-map
        /// scopes; the fallback uses the saved scope plus represented projected
        /// membership so model callers still receive a deterministic boundary.
        /// </summary>
        public bool CanEditPawn(PawnKey pawn)
        {
            RefreshProjection();
            return IsEditablePawn(pawn, _projectedState);
        }

        /// <summary>
        /// Marks an out-of-band mutation of the exposed draft. Normal preview
        /// edits go through this provider's editor and are stamped
        /// automatically; this method keeps the legacy Draft escape hatch
        /// correct without rescanning the draft on every cell read.
        /// </summary>
        public void InvalidateDraft()
        {
            if (_isCapturedView)
            {
                return;
            }

            _draftRevision = unchecked(_draftRevision + 1L);
            WorkTabEffectiveStateRuntime.InvalidateRenderPass();
        }

        /// <summary>
        /// Replaces the live editable-pawn boundary without rebuilding the
        /// provider or mutating the draft. A null or invalid source is treated
        /// as an empty boundary, which fails closed. Indexes and the provider
        /// revision are refreshed together so a pawn cannot retain stale
        /// projected editability or stale render-pass data.
        /// </summary>
        public bool ReplaceEditablePawnBoundary(IEnumerable<PawnKey> editablePawnIds)
        {
            if (_isCapturedView)
            {
                return false;
            }

            RefreshProjection();
            var replacement = new HashSet<PawnKey>();
            if (editablePawnIds != null)
            {
                foreach (PawnKey pawn in editablePawnIds)
                {
                    if (pawn != null && pawn.IsValid)
                    {
                        replacement.Add(pawn);
                    }
                }
            }

            if (_hasEditablePawnBoundary && _editablePawnIds.SetEquals(replacement))
            {
                return false;
            }

            _editablePawnIds = replacement;
            _hasEditablePawnBoundary = true;
            _membershipRevision = unchecked(_membershipRevision + 1L);
            RebuildIndexes();
            _revision = unchecked(_revision + 1L);
            WorkTabEffectiveStateRuntime.InvalidateRenderPass();
            return true;
        }

        /// <summary>
        /// Captures the live dependency once before a new effective-state pass
        /// starts. Reads made by the projected provider during that pass reuse
        /// this value, keeping revision observation out of the per-cell path.
        /// </summary>
        internal void CaptureBaseRevisionForRenderPass(long passId)
        {
            if (_isCapturedView)
            {
                return;
            }

            _capturedBasePassId = passId;
            _capturedBaseRevision = _baseProvider?.Revision ?? 0L;
            _capturedBaseRevisionVector = ReadBaseRevisionVectorUncaptured();
            _hasCapturedBaseRevision = true;
            _hasCapturedBaseRevisionVector = true;
        }

        void IWorkTabEffectiveStatePassParticipant.PrepareRenderPass(long passId)
        {
            CaptureBaseRevisionForRenderPass(passId);
        }

        bool IWorkTabPreviewOwnership.OwnsDimension(WorkTabEffectiveStateDimension dimension)
        {
            switch (dimension)
            {
                case WorkTabEffectiveStateDimension.ManualMode:
                    return (_ownedDimensions & WorkloadOwnershipDimensions.ManualModes) != 0;
                case WorkTabEffectiveStateDimension.Schedule:
                    return (_ownedDimensions & WorkloadOwnershipDimensions.Schedules) != 0;
                case WorkTabEffectiveStateDimension.SpecificJobOverride:
                    return (_ownedDimensions & WorkloadOwnershipDimensions.SpecificJobOverrides) != 0;
                case WorkTabEffectiveStateDimension.SpecificJobOrder:
                    return (_ownedDimensions & WorkloadOwnershipDimensions.SpecificJobOrder) != 0;
                case WorkTabEffectiveStateDimension.PresentationSetting:
                    return (_ownedDimensions & WorkloadOwnershipDimensions.PresentationSettings) != 0 ||
                           _presentationOwnershipTouched ||
                           HasProjectedPresentationSettings(_projectedState);
                default:
                    return true;
            }
        }

        public WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveSchedule(
            WorkloadScheduleTargetKey key)
        {
            RefreshProjection();
            if (!Owns(WorkloadStateDimension.Schedules) || key == null || !key.IsValid ||
                !IsEditableScheduleTarget(key, _projectedState))
            {
                return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
            }

            return _scheduleIntents.TryGetValue(
                       key,
                       out WorkloadIntent<WorkloadSchedulePayload> intent)
                ? ToResolution(intent, value => value)
                : WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
        }

        public WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> ResolveSpecificJobPriority(
            WorkloadSpecificJobTargetKey key)
        {
            RefreshProjection();
            if (!Owns(WorkloadStateDimension.SpecificJobOverrides) ||
                key == null || !key.IsValid || !IsEditableSpecificTarget(key, _projectedState))
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            }

            if (_specificPriorityIntents.TryGetValue(
                    key,
                    out WorkloadIntent<WorkloadSpecificPriorityPayload> intent))
            {
                return ToResolution(intent, value => value);
            }

            WorkloadSpecificJobKey legacyKey = key.ToLegacyKey();
            if (_specificJobOverrides.TryGetValue(legacyKey, out WorkloadScalarValue scalar) &&
                scalar.Kind == WorkloadScalarKind.Integer)
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Set(
                    new WorkloadSpecificPriorityPayload(scalar.IntegerValue));
            }

            return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
        }

        public WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> ResolveWorkTypeOrder(
            WorkloadWorkTypeOrderKey key)
        {
            RefreshProjection();
            if (!Owns(WorkloadStateDimension.SpecificJobOrder) ||
                key == null || !key.IsValid || !IsEditableOrderTarget(key, _projectedState))
            {
                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }

            return _workTypeOrderIntents.TryGetValue(
                       key,
                       out WorkloadIntent<WorkloadWorkTypeOrderPayload> intent)
                ? ToResolution(intent, value => value)
                : WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
        }

        public WorkTabEffectiveStateResolution<WorkloadSettingValue> ResolvePresentationSetting(
            string key)
        {
            RefreshProjection();
            if (!OwnsPresentationSetting(key))
            {
                return WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
            }

            if (_presentationSettingIntents.TryGetValue(
                    key,
                    out WorkloadIntent<WorkloadSettingValue> intent))
            {
                return ToResolution(intent, value => value);
            }

            return _presentationSettings.TryGetValue(key, out WorkloadScalarValue scalar)
                ? WorkTabEffectiveStateResolution<WorkloadSettingValue>.Set(
                    WorkloadSettingValue.WorkloadOwned(scalar))
                : WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
        }

        WorkTabEffectiveStateResolution<TimePriorityScheduleValue>
            IWorkTabPreviewStateReader.ResolveSchedule(TimePriorityTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetScheduleKey(
                    target,
                    out WorkloadScheduleTargetKey key,
                    out _))
            {
                return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.NoOpinion;
            }

            return WorkloadPreviewStateAdapter.ToScheduleResolution(
                ResolveEffectiveSchedule(key));
        }

        WorkTabEffectiveStateResolution<TimePriorityScheduleValue>
            IWorkTabPreviewStateReader.ResolvePreviewScheduleIntent(
                TimePriorityTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetScheduleKey(
                    target,
                    out WorkloadScheduleTargetKey key,
                    out _))
            {
                return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.NoOpinion;
            }

            return WorkloadPreviewStateAdapter.ToScheduleResolution(
                ResolveSchedule(key));
        }

        WorkTabEffectiveStateResolution<int>
            IWorkTabPreviewStateReader.ResolveSpecificJobPriority(
                WorkTabSpecificJobTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetSpecificJobKey(
                    target,
                    out WorkloadSpecificJobTargetKey key))
            {
                return WorkTabEffectiveStateResolution<int>.NoOpinion;
            }

            return WorkloadPreviewStateAdapter.ToSpecificPriorityResolution(
                ResolveEffectiveSpecificJobPriority(key));
        }

        WorkTabEffectiveStateResolution<int>
            IWorkTabPreviewStateReader.ResolvePreviewSpecificJobPriorityIntent(
                WorkTabSpecificJobTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetSpecificJobKey(
                    target,
                    out WorkloadSpecificJobTargetKey key))
            {
                return WorkTabEffectiveStateResolution<int>.NoOpinion;
            }

            return WorkloadPreviewStateAdapter.ToSpecificPriorityResolution(
                ResolveSpecificJobPriority(key));
        }

        WorkTabEffectiveStateResolution<IReadOnlyList<string>>
            IWorkTabPreviewStateReader.ResolveWorkTypeOrder(
                WorkTabWorkTypeOrderTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetWorkTypeOrderKey(
                    target,
                    out WorkloadWorkTypeOrderKey key))
            {
                return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.NoOpinion;
            }

            return WorkloadPreviewStateAdapter.ToWorkTypeOrderResolution(
                ResolveEffectiveWorkTypeOrder(key));
        }

        WorkTabEffectiveStateResolution<IReadOnlyList<string>>
            IWorkTabPreviewStateReader.ResolvePreviewWorkTypeOrderIntent(
                WorkTabWorkTypeOrderTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetWorkTypeOrderKey(
                    target,
                    out WorkloadWorkTypeOrderKey key))
            {
                return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.NoOpinion;
            }

            return WorkloadPreviewStateAdapter.ToWorkTypeOrderResolution(
                ResolveWorkTypeOrder(key));
        }

        WorkTabEffectiveStateMutationResult IWorkTabPreviewStateEditor.SetScheduleIntent(
            TimePriorityTarget target,
            WorkTabEffectiveStateResolution<TimePriorityScheduleValue> intent)
        {
            if (!WorkloadPreviewStateAdapter.TryGetScheduleKey(
                    target,
                    out WorkloadScheduleTargetKey key,
                    out string reason))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.Schedule,
                    Revision,
                    reason ?? "A valid schedule target is required.");
            }

            if (intent.IsSet)
            {
                WorkloadSchedulePayload payload =
                    WorkloadPreviewStateAdapter.ToSchedulePayload(intent.Value);
                if (payload == null)
                {
                    return WorkTabEffectiveStateMutationResult.Blocked(
                        WorkTabEffectiveStateDimension.Schedule,
                        Revision,
                        "A complete 24-hour schedule value is required.");
                }

                return SetSchedule(key, payload);
            }

            return intent.IsClear
                ? ClearSchedule(key)
                : SetScheduleNoOpinion(key);
        }

        WorkTabEffectiveStateMutationResult IWorkTabPreviewStateEditor.SetSpecificJobPriority(
            WorkTabSpecificJobTarget target,
            int priority)
        {
            if (!WorkloadPreviewStateAdapter.TryGetSpecificJobKey(
                    target,
                    out WorkloadSpecificJobTargetKey key))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    Revision,
                    "A valid specific-job target is required.");
            }

            return SetSpecificJobPriority(
                key,
                new WorkloadSpecificPriorityPayload(priority));
        }

        WorkTabEffectiveStateMutationResult IWorkTabPreviewStateEditor.SetSpecificJobPriorityIntent(
            WorkTabSpecificJobTarget target,
            WorkTabEffectiveStateResolution<int> intent)
        {
            if (!WorkloadPreviewStateAdapter.TryGetSpecificJobKey(
                    target,
                    out WorkloadSpecificJobTargetKey key))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    Revision,
                    "A valid specific-job target is required.");
            }

            if (intent.IsSet)
            {
                if (intent.Value < 0)
                {
                    return WorkTabEffectiveStateMutationResult.Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOverride,
                        Revision,
                        "A valid specific-job priority is required.");
                }

                return SetSpecificJobPriority(
                    key,
                    new WorkloadSpecificPriorityPayload(intent.Value));
            }

            return intent.IsClear
                ? ClearSpecificJobPriority(key)
                : SetSpecificJobPriorityNoOpinion(key);
        }

        WorkTabEffectiveStateMutationResult IWorkTabPreviewStateEditor.ClearSpecificJobPriority(
            WorkTabSpecificJobTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetSpecificJobKey(
                    target,
                    out WorkloadSpecificJobTargetKey key))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    Revision,
                    "A valid specific-job target is required.");
            }

            return ClearSpecificJobPriority(key);
        }

        WorkTabEffectiveStateMutationResult IWorkTabPreviewStateEditor.SetWorkTypeOrder(
            WorkTabWorkTypeOrderTarget target,
            IReadOnlyList<string> orderedWorkGiverNames)
        {
            if (!WorkloadPreviewStateAdapter.TryGetWorkTypeOrderKey(
                    target,
                    out WorkloadWorkTypeOrderKey key))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    Revision,
                    "A valid WorkType order target is required.");
            }

            WorkloadWorkTypeOrderPayload payload =
                WorkloadPreviewStateAdapter.ToWorkTypeOrderPayload(
                    orderedWorkGiverNames);
            return payload == null
                ? WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    Revision,
                    "A complete WorkType order is required.")
                : SetWorkTypeOrder(key, payload);
        }

        WorkTabEffectiveStateMutationResult IWorkTabPreviewStateEditor.SetWorkTypeOrderIntent(
            WorkTabWorkTypeOrderTarget target,
            WorkTabEffectiveStateResolution<IReadOnlyList<string>> intent)
        {
            if (!WorkloadPreviewStateAdapter.TryGetWorkTypeOrderKey(
                    target,
                    out WorkloadWorkTypeOrderKey key))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    Revision,
                    "A valid WorkType order target is required.");
            }

            if (intent.IsSet)
            {
                WorkloadWorkTypeOrderPayload payload =
                    WorkloadPreviewStateAdapter.ToWorkTypeOrderPayload(intent.Value);
                return payload == null
                    ? WorkTabEffectiveStateMutationResult.Blocked(
                        WorkTabEffectiveStateDimension.SpecificJobOrder,
                        Revision,
                        "A complete WorkType order is required.")
                    : SetWorkTypeOrder(key, payload);
            }

            return intent.IsClear
                ? ClearWorkTypeOrder(key)
                : SetWorkTypeOrderNoOpinion(key);
        }

        WorkTabEffectiveStateMutationResult IWorkTabPreviewStateEditor.ClearWorkTypeOrder(
            WorkTabWorkTypeOrderTarget target)
        {
            if (!WorkloadPreviewStateAdapter.TryGetWorkTypeOrderKey(
                    target,
                    out WorkloadWorkTypeOrderKey key))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    Revision,
                    "A valid WorkType order target is required.");
            }

            return ClearWorkTypeOrder(key);
        }

        private static WorkTabEffectiveStateResolution<TResult> ToResolution<TValue, TResult>(
            WorkloadIntent<TValue> intent,
            Func<TValue, TResult> selector)
        {
            if (intent.IsClear)
            {
                return WorkTabEffectiveStateResolution<TResult>.Clear;
            }

            if (!intent.HasValue)
            {
                return WorkTabEffectiveStateResolution<TResult>.NoOpinion;
            }

            return WorkTabEffectiveStateResolution<TResult>.Set(selector(intent.Value));
        }

        public WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveEffectiveSchedule(
            WorkloadScheduleTargetKey key)
        {
            WorkTabEffectiveStateResolution<WorkloadSchedulePayload> projected =
                ResolveSchedule(key);
            if (projected.IsSet || projected.IsClear)
            {
                return projected;
            }

            return ResolveBaseSchedule(key);
        }

        public WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveEffectiveSpecificJobPriority(WorkloadSpecificJobTargetKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            }

            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> projected =
                ResolveSpecificJobPriority(key);
            if (projected.IsSet)
            {
                return projected;
            }

            if (key.IsGlobal)
            {
                // A global Clear is an explicit tombstone. It must not
                // resurrect the live shared override.
                return projected.IsClear
                    ? projected
                    : ResolveBaseSpecificJobPriorityExact(key);
            }

            WorkloadSpecificJobTargetKey globalTarget =
                WorkloadSpecificJobTargetKey.Global(key.WorkType, key.WorkGiver);
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> globalProjected =
                ResolveSpecificJobPriority(globalTarget);

            if (projected.IsClear)
            {
                // Clearing a local target may reveal a projected shared
                // target, but it must never fall through to the live local
                // override. With no projected shared opinion, the explicit
                // local tombstone remains the effective result.
                if (globalProjected.IsSet || globalProjected.IsClear)
                {
                    return globalProjected;
                }

                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Clear;
            }

            if (globalProjected.IsSet)
            {
                return globalProjected;
            }

            if (globalProjected.IsClear)
            {
                // A shared Clear suppresses the lower-precedence live shared
                // value, while a genuinely more-specific live local value is
                // still allowed to win.
                WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> localLive =
                    ResolveBaseSpecificJobPriorityExact(key);
                return localLive.IsSet || localLive.IsClear
                    ? localLive
                    : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Clear;
            }

            return ResolveBaseSpecificJobPriority(key);
        }

        public WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>
            ResolveEffectiveWorkTypeOrder(WorkloadWorkTypeOrderKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }

            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> projected =
                ResolveWorkTypeOrder(key);
            if (projected.IsSet)
            {
                return projected;
            }

            if (key.IsGlobal)
            {
                return projected.IsClear
                    ? projected
                    : ResolveBaseWorkTypeOrderExact(key);
            }

            WorkloadWorkTypeOrderKey globalTarget =
                WorkloadWorkTypeOrderKey.Global(key.WorkType);
            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> globalProjected =
                ResolveWorkTypeOrder(globalTarget);

            if (projected.IsClear)
            {
                if (globalProjected.IsSet || globalProjected.IsClear)
                {
                    return globalProjected;
                }

                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.Clear;
            }

            if (globalProjected.IsSet)
            {
                return globalProjected;
            }

            if (globalProjected.IsClear)
            {
                WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> localLive =
                    ResolveBaseWorkTypeOrderExact(key);
                return localLive.IsSet || localLive.IsClear
                    ? localLive
                    : WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.Clear;
            }

            return ResolveBaseWorkTypeOrder(key);
        }

        public WorkTabEffectiveStateResolution<WorkloadSettingValue>
            ResolveEffectivePresentationSetting(string key)
        {
            WorkTabEffectiveStateResolution<WorkloadSettingValue> projected =
                ResolvePresentationSetting(key);
            if (projected.IsSet)
            {
                return projected;
            }

            if (_baseProvider is IWorkTabEffectiveStateV2Provider v2)
            {
                WorkTabEffectiveStateResolution<WorkloadSettingValue> lower =
                    v2.ResolvePresentationSetting(key);
                if (lower.IsSet)
                {
                    return lower;
                }
            }

            return WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
        }

        private WorkTabEffectiveStateResolution<WorkloadSchedulePayload> ResolveBaseSchedule(
            WorkloadScheduleTargetKey key)
        {
            if (_baseProvider is IWorkTabEffectiveStateV2Provider v2)
            {
                WorkTabEffectiveStateResolution<WorkloadSchedulePayload> lower =
                    v2.ResolveSchedule(key);
                return lower.IsSet
                    ? lower
                    : WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
            }

            return WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
        }

        private WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveBaseSpecificJobPriority(WorkloadSpecificJobTargetKey key)
        {
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> local =
                ResolveBaseSpecificJobPriorityExact(key);
            if (local.IsSet || local.IsClear || key.IsGlobal)
            {
                return local;
            }

            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> global =
                ResolveBaseSpecificJobPriorityExact(
                    WorkloadSpecificJobTargetKey.Global(key.WorkType, key.WorkGiver));
            return global.IsSet || global.IsClear
                ? global
                : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
        }

        private WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>
            ResolveBaseSpecificJobPriorityExact(WorkloadSpecificJobTargetKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
            }

            if (_baseProvider is IWorkTabEffectiveStateV2Provider v2)
            {
                WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> lower =
                    v2.ResolveSpecificJobPriority(key);
                if (lower.IsSet || lower.IsClear)
                {
                    return lower;
                }
            }

            return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
        }

        private WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>
            ResolveBaseWorkTypeOrder(WorkloadWorkTypeOrderKey key)
        {
            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> local =
                ResolveBaseWorkTypeOrderExact(key);
            if (local.IsSet || local.IsClear || key.IsGlobal)
            {
                return local;
            }

            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> global =
                ResolveBaseWorkTypeOrderExact(
                    WorkloadWorkTypeOrderKey.Global(key.WorkType));
            return global.IsSet || global.IsClear
                ? global
                : WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
        }

        private WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>
            ResolveBaseWorkTypeOrderExact(WorkloadWorkTypeOrderKey key)
        {
            if (key == null || !key.IsValid)
            {
                return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
            }

            if (_baseProvider is IWorkTabEffectiveStateV2Provider v2)
            {
                WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> lower =
                    v2.ResolveWorkTypeOrder(key);
                if (lower.IsSet || lower.IsClear)
                {
                    return lower;
                }
            }

            return WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
        }

        /// <summary>
        /// Applies a global-mode preview change as one draft mutation so the
        /// projection is rebuilt and fingerprinted once for the whole scope.
        /// </summary>
        internal WorkTabEffectiveStateMutationResult SetManualModes(
            IReadOnlyList<WorkloadParentPriorityKey> keys,
            bool manualMode)
        {
            if (_isCapturedView)
            {
                return CapturedViewMutationBlocked(WorkTabEffectiveStateDimension.ManualMode);
            }

            RefreshProjection();
            if (keys == null || keys.Count == 0)
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.ManualMode,
                    _revision,
                    "At least one valid manual-mode key is required.");
            }

            for (int i = 0; i < keys.Count; i++)
            {
                WorkloadParentPriorityKey key = keys[i];
                if (key == null || !key.IsValid)
                {
                    return WorkTabEffectiveStateMutationResult.Blocked(
                        WorkTabEffectiveStateDimension.ManualMode,
                        _revision,
                        "A valid manual-mode key is required.");
                }

                if (!IsEditablePawn(key.Pawn, _projectedState))
                {
                    return WorkTabEffectiveStateMutationResult.Blocked(
                        WorkTabEffectiveStateDimension.ManualMode,
                        _revision,
                        "A pawn is outside the active workload scope or is excluded for this preview.");
                }
            }

            return Apply(
                WorkTabEffectiveStateDimension.ManualMode,
                WorkloadOwnershipDimensions.ManualModes,
                null,
                true,
                "At least one valid manual-mode key is required.",
                draft =>
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        draft.SetManualMode(keys[i], manualMode);
                    }
                });
        }

        public WorkTabEffectiveStateMutationResult SetSchedule(
            WorkloadScheduleTargetKey key,
            WorkloadSchedulePayload payload)
        {
            return Apply(
                WorkTabEffectiveStateDimension.Schedule,
                WorkloadOwnershipDimensions.Schedules,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid && payload != null && payload.IsValid,
                "A valid schedule target and complete 24-hour payload are required.",
                draft => draft.SetSchedule(key, payload));
        }

        public WorkTabEffectiveStateMutationResult ClearSchedule(
            WorkloadScheduleTargetKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.Schedule,
                WorkloadOwnershipDimensions.Schedules,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid,
                "A valid schedule target is required.",
                draft => draft.ClearSchedule(key));
        }

        public WorkTabEffectiveStateMutationResult SetScheduleNoOpinion(
            WorkloadScheduleTargetKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.Schedule,
                WorkloadOwnershipDimensions.Schedules,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid,
                "A valid schedule target is required.",
                draft => draft.SetScheduleNoOpinion(key));
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobPriority(
            WorkloadSpecificJobTargetKey key,
            WorkloadSpecificPriorityPayload payload)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                WorkloadOwnershipDimensions.SpecificJobOverrides,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid && payload.IsValid,
                "A valid specific-job target and priority payload are required.",
                draft => draft.SetSpecificPriorityIntent(
                    key,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(payload)));
        }

        public WorkTabEffectiveStateMutationResult ClearSpecificJobPriority(
            WorkloadSpecificJobTargetKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                WorkloadOwnershipDimensions.SpecificJobOverrides,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid,
                "A valid specific-job target is required.",
                draft => draft.ClearSpecificPriority(key));
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobPriorityNoOpinion(
            WorkloadSpecificJobTargetKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                WorkloadOwnershipDimensions.SpecificJobOverrides,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid,
                "A valid specific-job target is required.",
                draft => draft.SetSpecificPriorityNoOpinion(key));
        }

        public WorkTabEffectiveStateMutationResult SetWorkTypeOrder(
            WorkloadWorkTypeOrderKey key,
            WorkloadWorkTypeOrderPayload payload)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOrder,
                WorkloadOwnershipDimensions.SpecificJobOrder,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid && payload != null && payload.IsValid,
                "A valid WorkType order target and complete permutation are required.",
                draft => draft.SetWorkTypeOrder(key, payload));
        }

        public WorkTabEffectiveStateMutationResult ClearWorkTypeOrder(
            WorkloadWorkTypeOrderKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOrder,
                WorkloadOwnershipDimensions.SpecificJobOrder,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid,
                "A valid WorkType order target is required.",
                draft => draft.ClearWorkTypeOrder(key));
        }

        public WorkTabEffectiveStateMutationResult SetWorkTypeOrderNoOpinion(
            WorkloadWorkTypeOrderKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOrder,
                WorkloadOwnershipDimensions.SpecificJobOrder,
                key?.IsGlobal == true ? null : key?.Pawn,
                key != null && key.IsValid,
                "A valid WorkType order target is required.",
                draft => draft.SetWorkTypeOrderNoOpinion(key));
        }

        public WorkTabEffectiveStateMutationResult SetPresentationSetting(
            string key,
            WorkloadSettingValue value)
        {
            return Apply(
                WorkTabEffectiveStateDimension.PresentationSetting,
                WorkloadOwnershipDimensions.PresentationSettings,
                null,
                !string.IsNullOrWhiteSpace(key) && value.IsValid,
                "A valid workload-owned presentation setting is required.",
                draft => draft.SetPresentationSettingIntent(
                    key,
                    WorkloadIntent<WorkloadSettingValue>.CreateSet(value)));
        }

        /// <summary>
        /// Acquires one allowlisted presentation key for a preview whose
        /// template does not yet own the presentation dimension. Ownership is
        /// intentionally per-key; this never opts the workload into the whole
        /// Better Work Tab settings object.
        /// </summary>
        public WorkTabEffectiveStateMutationResult AcquirePresentationSetting(
            string key,
            WorkloadScalarValue value)
        {
            if (_isCapturedView)
            {
                return CapturedViewMutationBlocked(
                    WorkTabEffectiveStateDimension.PresentationSetting);
            }

            RefreshProjection();
            if (string.IsNullOrWhiteSpace(key) || value.Kind == WorkloadScalarKind.Empty)
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.PresentationSetting,
                    _revision,
                    "A valid presentation setting key and value are required to acquire workload ownership.");
            }

            bool alreadyOwned = OwnsPresentationSetting(key);
            bool added = SetPresentationOwnershipKey(key, owned: true);
            _presentationOwnershipTouched = true;
            WorkTabEffectiveStateMutationResult result = Apply(
                WorkTabEffectiveStateDimension.PresentationSetting,
                WorkloadOwnershipDimensions.PresentationSettings,
                null,
                true,
                "A valid workload-owned presentation setting is required.",
                draft => draft.SetPresentationSettingIntent(
                    key,
                    WorkloadIntent<WorkloadSettingValue>.CreateSet(
                        WorkloadSettingValue.WorkloadOwned(value))),
                allowPresentationOwnershipAcquisition: true);
            if (result.IsBlocked && added && !alreadyOwned)
            {
                SetPresentationOwnershipKey(key, owned: false);
            }

            return result;
        }

        /// <summary>
        /// Releases one owned presentation key back to the global settings
        /// layer. This is an ownership removal, not a tombstone.
        /// </summary>
        public WorkTabEffectiveStateMutationResult ReleasePresentationSetting(
            string key)
        {
            if (_isCapturedView)
            {
                return CapturedViewMutationBlocked(
                    WorkTabEffectiveStateDimension.PresentationSetting);
            }

            RefreshProjection();
            if (!OwnsPresentationSetting(key))
            {
                return WorkTabEffectiveStateMutationResult.NoOp(
                    WorkTabEffectiveStateDimension.PresentationSetting,
                    _revision,
                    "The presentation setting is not owned by this workload preview.");
            }

            bool hadKey = SetPresentationOwnershipKey(key, owned: false);
            _presentationOwnershipTouched = true;
            WorkTabEffectiveStateMutationResult result = Apply(
                WorkTabEffectiveStateDimension.PresentationSetting,
                WorkloadOwnershipDimensions.PresentationSettings,
                null,
                true,
                "A valid workload-owned presentation setting is required.",
                draft => draft.ReleasePresentationSetting(key),
                allowPresentationOwnershipAcquisition: true);
            if (result.IsBlocked && hadKey)
            {
                SetPresentationOwnershipKey(key, owned: true);
            }

            return result;
        }

        public WorkTabEffectiveStateMutationResult ClearPresentationSettingV2(string key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.PresentationSetting,
                WorkloadOwnershipDimensions.PresentationSettings,
                null,
                !string.IsNullOrWhiteSpace(key),
                "A non-empty presentation-setting key is required.",
                draft => draft.ClearPresentationSetting(key));
        }

        private WorkTabEffectiveStateMutationResult Apply(
            WorkTabEffectiveStateDimension dimension,
            WorkloadOwnershipDimensions ownership,
            PawnKey pawn,
            bool valid,
            string invalidReason,
            Action<WorkloadDraft> mutation,
            bool allowPresentationOwnershipAcquisition = false)
        {
            if (_isCapturedView)
            {
                return CapturedViewMutationBlocked(dimension);
            }

            RefreshProjection();
            long previousRevision = _revision;
            bool ownsDimension = (_ownedDimensions & ownership) == ownership;
            bool acquiringPresentationOwnership =
                allowPresentationOwnershipAcquisition &&
                dimension == WorkTabEffectiveStateDimension.PresentationSetting;
            if (!ownsDimension && !acquiringPresentationOwnership)
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    dimension,
                    previousRevision,
                    "The projected provider does not own the " + dimension + " dimension.");
            }

            if (!valid)
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    dimension,
                    previousRevision,
                    invalidReason);
            }

            if (pawn != null && !IsEditablePawn(pawn, _projectedState))
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    dimension,
                    previousRevision,
                    "The pawn is outside the active workload scope or is excluded for this preview.");
            }

            WorkloadOwnershipDimensions beforeDimensions =
                EffectiveOwnedDimensions(_projectedState);
            string beforeFingerprint = _projectedState.GetSemanticFingerprint(beforeDimensions);
            mutation(_draft);
            _draftRevision = unchecked(_draftRevision + 1L);
            IncrementDimensionRevision(dimension);
            RefreshProjection();
            WorkloadOwnershipDimensions afterDimensions =
                EffectiveOwnedDimensions(_projectedState);
            string afterFingerprint = _projectedState.GetSemanticFingerprint(afterDimensions);
            if (StringComparer.Ordinal.Equals(beforeFingerprint, afterFingerprint))
            {
                // The draft and dimension revisions are part of the captured
                // revision vector even when the projected semantics are equal.
                // Keep the pass cache coherent with that newly published token.
                WorkTabEffectiveStateRuntime.InvalidateRenderPass();
                return WorkTabEffectiveStateMutationResult.NoOp(
                    dimension,
                    _revision,
                    "The projected value is already semantically equal.");
            }

            return WorkTabEffectiveStateMutationResult.AcceptedChange(
                dimension,
                previousRevision,
                _revision,
                "The projected value was accepted.");
        }

        private void IncrementDimensionRevision(WorkTabEffectiveStateDimension dimension)
        {
            switch (dimension)
            {
                case WorkTabEffectiveStateDimension.Schedule:
                    _scheduleRevision = unchecked(_scheduleRevision + 1L);
                    break;
                case WorkTabEffectiveStateDimension.SpecificJobOverride:
                case WorkTabEffectiveStateDimension.SpecificJobOrder:
                    _specificRevision = unchecked(_specificRevision + 1L);
                    break;
                case WorkTabEffectiveStateDimension.PresentationSetting:
                    _settingsRevision = unchecked(_settingsRevision + 1L);
                    break;
            }
        }

        private bool Owns(WorkloadStateDimension dimension)
        {
            return _ownedDimensions.Owns(dimension);
        }

        private bool OwnsPresentationSetting(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return _presentationOwnershipKeys.Contains(key) ||
                ContainsProjectedPresentationSetting(key);
        }

        private bool ContainsProjectedPresentationSetting(string key)
        {
            if (_projectedState == null)
            {
                return false;
            }

            for (int i = 0; i < _projectedState.PresentationSettings.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(
                        _projectedState.PresentationSettings[i]?.Key,
                        key))
                {
                    return true;
                }
            }

            for (int i = 0; i < _projectedState.PresentationSettingIntents.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry =
                    _projectedState.PresentationSettingIntents[i];
                if (entry != null &&
                    StringComparer.Ordinal.Equals(entry.Key, key) &&
                    !entry.Intent.IsNoOpinion)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasProjectedPresentationSettings(
            WorkloadProjectedState state)
        {
            return state != null &&
                ((state.PresentationSettings != null && state.PresentationSettings.Count > 0) ||
                 (state.PresentationSettingIntents != null &&
                  state.PresentationSettingIntents.Count > 0));
        }

        private WorkloadOwnershipDimensions EffectiveOwnedDimensions(
            WorkloadProjectedState state)
        {
            WorkloadOwnershipDimensions dimensions = _ownedDimensions;
            if (_presentationOwnershipTouched || HasProjectedPresentationSettings(state))
            {
                dimensions |= WorkloadOwnershipDimensions.PresentationSettings;
            }

            return dimensions;
        }

        private bool IsEditablePawn(
            PawnKey pawn,
            WorkloadProjectedState projectedState)
        {
            if (pawn == null || !pawn.IsValid)
            {
                return false;
            }

            if (_scope != null && _scope.IsExplicitlyExcluded(pawn))
            {
                return false;
            }

            WorkloadProjectedState state = projectedState ?? WorkloadProjectedState.Empty;
            if (state.IsExcluded(pawn))
            {
                return false;
            }

            if (_hasEditablePawnBoundary)
            {
                return _editablePawnIds.Contains(pawn);
            }

            if (_scope == null)
            {
                return true;
            }

            if (_scope.Mode == WorkloadScopeMode.ExplicitPawnIds)
            {
                for (int i = 0; i < _scope.ExplicitPawnIds.Count; i++)
                {
                    if (_scope.ExplicitPawnIds[i].Equals(pawn))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (int i = 0; i < state.RepresentedPawnIds.Count; i++)
            {
                if (state.RepresentedPawnIds[i].Equals(pawn))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsEditableScheduleTarget(
            WorkloadScheduleTargetKey key,
            WorkloadProjectedState projectedState)
        {
            return key != null && key.IsValid &&
                   (key.IsGlobal || IsEditablePawn(key.Pawn, projectedState));
        }

        private bool IsEditableSpecificTarget(
            WorkloadSpecificJobTargetKey key,
            WorkloadProjectedState projectedState)
        {
            return key != null && key.IsValid &&
                   (key.IsGlobal || IsEditablePawn(key.Pawn, projectedState));
        }

        private bool IsEditableOrderTarget(
            WorkloadWorkTypeOrderKey key,
            WorkloadProjectedState projectedState)
        {
            return key != null && key.IsValid &&
                   (key.IsGlobal || IsEditablePawn(key.Pawn, projectedState));
        }

        private void RefreshProjection()
        {
            if (_isCapturedView)
            {
                return;
            }

            long baseRevision = ReadBaseRevision();
            WorkTabEffectiveStateRevisionVector baseRevisionVector = ReadBaseRevisionVector();
            bool firstProjection = !_hasProjection;
            bool draftChanged = firstProjection || _observedDraftRevision != _draftRevision;
            bool baseChanged = firstProjection ||
                               _baseRevision != baseRevision ||
                               !_hasBaseRevisionVector ||
                               !_baseRevisionVector.Equals(baseRevisionVector);
            if (!draftChanged && !baseChanged)
            {
                return;
            }

            bool projectedChanged = false;
            if (draftChanged)
            {
                WorkloadProjectedState projected =
                    _draft.ProjectedState ?? WorkloadProjectedState.Empty;
                string fingerprint = projected.GetSemanticFingerprint(
                    EffectiveOwnedDimensions(projected));
                projectedChanged = firstProjection ||
                                   !StringComparer.Ordinal.Equals(
                                       _semanticFingerprint,
                                       fingerprint);

                if (projectedChanged)
                {
                    _projectedState = projected;
                    _semanticFingerprint = fingerprint;
                    RebuildIndexes();
                }

                _observedDraftRevision = _draftRevision;
            }

            if (firstProjection)
            {
                _projectedState = _projectedState ?? WorkloadProjectedState.Empty;
                _semanticFingerprint = _semanticFingerprint ?? string.Empty;
                _baseRevision = baseRevision;
                _baseRevisionVector = baseRevisionVector;
                _hasBaseRevisionVector = true;
                _hasProjection = true;
                return;
            }

            if (projectedChanged || baseChanged)
            {
                _revision = unchecked(_revision + 1L);
                _baseRevision = baseRevision;
                _baseRevisionVector = baseRevisionVector;
                _hasBaseRevisionVector = true;
                WorkTabEffectiveStateRuntime.InvalidateRenderPass();
            }

            if (baseChanged)
            {
                _baseRevision = baseRevision;
            }
        }

        private long ReadBaseRevision()
        {
            long passId = WorkTabEffectiveStateRuntime.PreparingRenderPassId;
            if (passId == 0L)
            {
                passId = WorkTabEffectiveStateRuntime.CurrentRenderPassId;
            }

            if (_hasCapturedBaseRevision &&
                passId != 0L &&
                passId == _capturedBasePassId)
            {
                return _capturedBaseRevision;
            }

            return _baseProvider?.Revision ?? 0L;
        }

        private WorkTabEffectiveStateRevisionVector ReadBaseRevisionVector()
        {
            long passId = WorkTabEffectiveStateRuntime.PreparingRenderPassId;
            if (passId == 0L)
            {
                passId = WorkTabEffectiveStateRuntime.CurrentRenderPassId;
            }

            if (_hasCapturedBaseRevisionVector &&
                passId != 0L &&
                passId == _capturedBasePassId)
            {
                return _capturedBaseRevisionVector;
            }

            return ReadBaseRevisionVectorUncaptured();
        }

        private WorkTabEffectiveStateRevisionVector ReadBaseRevisionVectorUncaptured()
        {
            return _baseProvider?.RevisionVector ??
                WorkTabEffectiveStateRevisionVector.FromRevision(0L);
        }

        private static long CombineRevisions(long left, long right)
        {
            unchecked
            {
                long value = left;
                value = (value * 397L) ^ right;
                return value;
            }
        }

        private WorkTabEffectiveStateMutationResult CapturedViewMutationBlocked(
            WorkTabEffectiveStateDimension dimension)
        {
            return WorkTabEffectiveStateMutationResult.Blocked(
                dimension,
                _capturedViewRevision.Revision,
                "The completed Work-tab view is read-only.");
        }

        private bool SetPresentationOwnershipKey(string key, bool owned)
        {
            bool contained = _presentationOwnershipKeys.Contains(key);
            if (contained == owned)
            {
                return false;
            }

            var replacement = new HashSet<string>(
                _presentationOwnershipKeys,
                StringComparer.Ordinal);
            if (owned)
            {
                replacement.Add(key);
            }
            else
            {
                replacement.Remove(key);
            }

            _presentationOwnershipKeys = replacement;
            return true;
        }

        private void RebuildIndexes()
        {
            // A completed WorkTabView may still hold the prior index set while
            // input edits this provider. Build replacement indexes and publish
            // them together so that view remains allocation-free and stable.
            var specificJobOverrides =
                new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
            var presentationSettings =
                new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            var scheduleIntents =
                new Dictionary<WorkloadScheduleTargetKey, WorkloadIntent<WorkloadSchedulePayload>>();
            var specificPriorityIntents =
                new Dictionary<WorkloadSpecificJobTargetKey, WorkloadIntent<WorkloadSpecificPriorityPayload>>();
            var workTypeOrderIntents =
                new Dictionary<WorkloadWorkTypeOrderKey, WorkloadIntent<WorkloadWorkTypeOrderPayload>>();
            var presentationSettingIntents =
                new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(StringComparer.Ordinal);
            var presentationOwnershipKeys =
                new HashSet<string>(_presentationOwnershipKeys, StringComparer.Ordinal);

            if (Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                for (int i = 0; i < _projectedState.SpecificJobOverrides.Count; i++)
                {
                    WorkloadSpecificJobOverrideEntry entry = _projectedState.SpecificJobOverrides[i];
                    if (entry != null &&
                        IsEditableSpecificTarget(entry.Key.ToTargetKey(), _projectedState))
                    {
                        specificJobOverrides[entry.Key] = entry.Value;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.PresentationSettings) ||
                HasProjectedPresentationSettings(_projectedState))
            {
                for (int i = 0; i < _projectedState.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry = _projectedState.PresentationSettings[i];
                    if (entry != null)
                    {
                        presentationOwnershipKeys.Add(entry.Key);
                        presentationSettings[entry.Key] = entry.Value;
                    }
                }

                for (int i = 0; i < _projectedState.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry =
                        _projectedState.PresentationSettingIntents[i];
                    if (entry != null && !entry.Intent.IsNoOpinion)
                    {
                        presentationOwnershipKeys.Add(entry.Key);
                    }
                }
            }

            if (Owns(WorkloadStateDimension.Schedules))
            {
                for (int i = 0; i < _projectedState.ScheduleIntents.Count; i++)
                {
                    WorkloadScheduleIntentEntry entry = _projectedState.ScheduleIntents[i];
                    if (entry != null && IsEditableScheduleTarget(entry.Key, _projectedState))
                    {
                        scheduleIntents[entry.Key] = entry.Intent;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                for (int i = 0; i < _projectedState.SpecificPriorityIntents.Count; i++)
                {
                    WorkloadSpecificPriorityIntentEntry entry = _projectedState.SpecificPriorityIntents[i];
                    if (entry != null && IsEditableSpecificTarget(entry.Key, _projectedState))
                    {
                        specificPriorityIntents[entry.Key] = entry.Intent;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                for (int i = 0; i < _projectedState.WorkTypeOrderIntents.Count; i++)
                {
                    WorkloadWorkTypeOrderIntentEntry entry = _projectedState.WorkTypeOrderIntents[i];
                    if (entry != null && IsEditableOrderTarget(entry.Key, _projectedState))
                    {
                        workTypeOrderIntents[entry.Key] = entry.Intent;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.PresentationSettings) ||
                HasProjectedPresentationSettings(_projectedState))
            {
                for (int i = 0; i < _projectedState.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry = _projectedState.PresentationSettingIntents[i];
                    if (entry != null)
                    {
                        presentationSettingIntents[entry.Key] = entry.Intent;
                    }
                }
            }

            _specificJobOverrides = specificJobOverrides;
            _presentationSettings = presentationSettings;
            _scheduleIntents = scheduleIntents;
            _specificPriorityIntents = specificPriorityIntents;
            _workTypeOrderIntents = workTypeOrderIntents;
            _presentationSettingIntents = presentationSettingIntents;
            _presentationOwnershipKeys = presentationOwnershipKeys;
        }
    }
}
