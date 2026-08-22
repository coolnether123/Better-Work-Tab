using System;
using System.Collections.Generic;
using System.Threading;
using Better_Work_Tab.Features.Workloads.V2;

namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    /// <summary>
    /// Session-local effective-state overlay backed by a WorkloadDraft. The
    /// draft owns projected values; this provider only indexes the immutable
    /// projected snapshot and optionally falls through to a base provider for
    /// values not represented by the draft.
    /// </summary>
    public sealed class ProjectedWorkTabEffectiveStateProvider :
        IWorkTabEffectiveStateProvider,
        IWorkTabEffectiveStateEditor,
        IWorkTabEffectiveStateV2Provider,
        IWorkTabEffectiveStateV2Editor
    {
        private static long NextProviderGeneration;
        private readonly WorkloadDraft _draft;
        private readonly IWorkTabEffectiveStateProvider _baseProvider;
        private readonly WorkloadOwnershipDimensions _ownedDimensions;
        private readonly string _providerId;
        private readonly WorkloadScope _scope;
        private readonly HashSet<PawnKey> _editablePawnIds;
        private bool _hasEditablePawnBoundary;

        private readonly Dictionary<WorkloadParentPriorityKey, int> _parentPriorities =
            new Dictionary<WorkloadParentPriorityKey, int>();
        private readonly Dictionary<WorkloadParentPriorityKey, bool> _manualModes =
            new Dictionary<WorkloadParentPriorityKey, bool>();
        private readonly Dictionary<PawnKey, ScheduleKey> _schedules =
            new Dictionary<PawnKey, ScheduleKey>();
        private readonly Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue> _specificJobOverrides =
            new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
        private readonly Dictionary<WorkloadSpecificJobKey, int> _specificJobOrder =
            new Dictionary<WorkloadSpecificJobKey, int>();
        private readonly Dictionary<string, WorkloadScalarValue> _presentationSettings =
            new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
        private readonly Dictionary<WorkloadParentPriorityKey, WorkloadIntent<WorkloadSpecificPriorityPayload>> _parentPriorityIntents =
            new Dictionary<WorkloadParentPriorityKey, WorkloadIntent<WorkloadSpecificPriorityPayload>>();
        private readonly Dictionary<WorkloadParentPriorityKey, WorkloadIntent<bool>> _manualModeIntents =
            new Dictionary<WorkloadParentPriorityKey, WorkloadIntent<bool>>();
        private readonly Dictionary<WorkloadScheduleTargetKey, WorkloadIntent<WorkloadSchedulePayload>> _scheduleIntents =
            new Dictionary<WorkloadScheduleTargetKey, WorkloadIntent<WorkloadSchedulePayload>>();
        private readonly Dictionary<WorkloadSpecificJobTargetKey, WorkloadIntent<WorkloadSpecificPriorityPayload>> _specificPriorityIntents =
            new Dictionary<WorkloadSpecificJobTargetKey, WorkloadIntent<WorkloadSpecificPriorityPayload>>();
        private readonly Dictionary<WorkloadWorkTypeOrderKey, WorkloadIntent<WorkloadWorkTypeOrderPayload>> _workTypeOrderIntents =
            new Dictionary<WorkloadWorkTypeOrderKey, WorkloadIntent<WorkloadWorkTypeOrderPayload>>();
        private readonly Dictionary<string, WorkloadIntent<WorkloadSettingValue>> _presentationSettingIntents =
            new Dictionary<string, WorkloadIntent<WorkloadSettingValue>>(StringComparer.Ordinal);
        private readonly HashSet<string> _presentationOwnershipKeys =
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
        private long _parentPriorityRevision;
        private long _manualModeRevision;
        private long _scheduleRevision;
        private long _specificRevision;
        private long _settingsRevision;
        private long _membershipRevision;

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
                RefreshProjection();
                return _revision;
            }
        }

        public long ProjectionRevision => Revision;
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
                RefreshProjection();
                WorkTabEffectiveStateRevisionVector baseVector = ReadBaseRevisionVector();
                return new WorkTabEffectiveStateRevisionVector(
                    _providerGeneration,
                    CombineRevisions(baseVector.ProviderGeneration, baseVector.SourceRevision),
                    CombineRevisions(
                        _draftRevision,
                        CombineRevisions(_parentPriorityRevision, _manualModeRevision)),
                    baseVector.PersistenceRevision,
                    baseVector.AuthorityRevision,
                    CombineRevisions(baseVector.ScheduleRevision, _scheduleRevision),
                    CombineRevisions(baseVector.SpecificRevision, _specificRevision),
                    CombineRevisions(baseVector.SettingsRevision, _settingsRevision),
                    CombineRevisions(baseVector.MembershipRevision, _membershipRevision));
            }
        }
        public WorkTabEffectiveStateRevision RevisionToken =>
            new WorkTabEffectiveStateRevision(ProviderId, Revision, Source, RevisionVector);
        public IWorkTabEffectiveStateEditor Editor => this;

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
        /// Returns the projected global manual-priority value from a represented,
        /// currently editable parent key. Manual mode is global in RimWorld, but
        /// the preview model stores it on parent keys; never infer the display
        /// value from an arbitrary live pawn outside this provider's boundary.
        /// </summary>
        public bool TryGetProjectedManualModeForDisplay(out bool manualMode)
        {
            manualMode = false;
            RefreshProjection();
            if (!Owns(WorkloadStateDimension.ManualModes))
            {
                return false;
            }

            IReadOnlyList<WorkloadManualModeEntry> entries =
                _projectedState?.ManualModes;
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                WorkloadManualModeEntry entry = entries[i];
                if (entry != null &&
                    entry.Key != null &&
                    IsEditablePawn(entry.Key.Pawn, _projectedState) &&
                    _manualModes.TryGetValue(entry.Key, out manualMode))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Marks an out-of-band mutation of the exposed draft. Normal preview
        /// edits go through this provider's editor and are stamped
        /// automatically; this method keeps the legacy Draft escape hatch
        /// correct without rescanning the draft on every cell read.
        /// </summary>
        public void InvalidateDraft()
        {
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

            _editablePawnIds.Clear();
            foreach (PawnKey pawn in replacement)
            {
                _editablePawnIds.Add(pawn);
            }

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
            _capturedBasePassId = passId;
            _capturedBaseRevision = _baseProvider?.Revision ?? 0L;
            _capturedBaseRevisionVector = ReadBaseRevisionVectorUncaptured();
            _hasCapturedBaseRevision = true;
            _hasCapturedBaseRevisionVector = true;
        }

        public WorkTabEffectiveStateResolution<int> ResolveParentPriority(
            WorkloadParentPriorityKey key)
        {
            RefreshProjection();
            if (!Owns(WorkloadStateDimension.ParentPriorities) || key == null || !key.IsValid ||
                !IsEditablePawn(key.Pawn, _projectedState))
            {
                return WorkTabEffectiveStateResolution<int>.NoOpinion;
            }

            if (_parentPriorityIntents.TryGetValue(key, out WorkloadIntent<WorkloadSpecificPriorityPayload> intent))
            {
                return ToResolution(intent, value => value.Priority);
            }

            return _parentPriorities.TryGetValue(key, out int priority)
                ? WorkTabEffectiveStateResolution<int>.Set(priority)
                : WorkTabEffectiveStateResolution<int>.NoOpinion;
        }

        public WorkTabEffectiveStateResolution<bool> ResolveManualMode(
            WorkloadParentPriorityKey key)
        {
            RefreshProjection();
            if (!Owns(WorkloadStateDimension.ManualModes) || key == null || !key.IsValid ||
                !IsEditablePawn(key.Pawn, _projectedState))
            {
                return WorkTabEffectiveStateResolution<bool>.NoOpinion;
            }

            if (_manualModeIntents.TryGetValue(key, out WorkloadIntent<bool> intent))
            {
                return ToResolution(intent, value => value);
            }

            return _manualModes.TryGetValue(key, out bool manual)
                ? WorkTabEffectiveStateResolution<bool>.Set(manual)
                : WorkTabEffectiveStateResolution<bool>.NoOpinion;
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

        /// <summary>
        /// Composes the projected layer with the canonical provider. The
        /// projected resolver above remains exact-layer-only; these helpers are
        /// the only place where a NoOpinion/Clear result is allowed to fall
        /// through to the lower provider.
        /// </summary>
        public WorkTabEffectiveStateResolution<int> ResolveEffectiveParentPriority(
            WorkloadParentPriorityKey key)
        {
            WorkTabEffectiveStateResolution<int> projected = ResolveParentPriority(key);
            if (projected.IsSet)
            {
                return projected;
            }

            if (_baseProvider is IWorkTabEffectiveStateV2Provider v2)
            {
                WorkTabEffectiveStateResolution<int> lower = v2.ResolveParentPriority(key);
                if (lower.IsSet)
                {
                    return lower;
                }
            }

            if (_baseProvider != null &&
                _baseProvider.TryGetParentPriority(key, out int priority))
            {
                return WorkTabEffectiveStateResolution<int>.Set(priority);
            }

            return WorkTabEffectiveStateResolution<int>.NoOpinion;
        }

        public WorkTabEffectiveStateResolution<bool> ResolveEffectiveManualMode(
            WorkloadParentPriorityKey key)
        {
            WorkTabEffectiveStateResolution<bool> projected = ResolveManualMode(key);
            if (projected.IsSet)
            {
                return projected;
            }

            if (_baseProvider is IWorkTabEffectiveStateV2Provider v2)
            {
                WorkTabEffectiveStateResolution<bool> lower = v2.ResolveManualMode(key);
                if (lower.IsSet)
                {
                    return lower;
                }
            }

            if (_baseProvider != null &&
                _baseProvider.TryGetManualMode(key, out bool manualMode))
            {
                return WorkTabEffectiveStateResolution<bool>.Set(manualMode);
            }

            return WorkTabEffectiveStateResolution<bool>.NoOpinion;
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

            if (_baseProvider != null &&
                _baseProvider.TryGetPresentationSetting(key, out WorkloadScalarValue scalar))
            {
                return WorkTabEffectiveStateResolution<WorkloadSettingValue>.Set(
                    WorkloadSettingValue.Global(scalar));
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

            if (_baseProvider != null &&
                _baseProvider.TryGetSpecificJobOverride(
                    key.ToLegacyKey(),
                    out WorkloadScalarValue scalar) &&
                scalar.Kind == WorkloadScalarKind.Integer)
            {
                return WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Set(
                    new WorkloadSpecificPriorityPayload(scalar.IntegerValue));
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

        private bool TryResolveEffectiveSpecificJobOrder(
            WorkloadSpecificJobKey key,
            out int order,
            out bool explicitClear)
        {
            order = 0;
            explicitClear = false;
            if (key == null || !key.IsValid ||
                !Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                return false;
            }

            WorkloadWorkTypeOrderKey local = new WorkloadWorkTypeOrderKey(
                key.Scope,
                key.Pawn,
                key.WorkType);
            if (key.IsGlobal)
            {
                local = WorkloadWorkTypeOrderKey.Global(key.WorkType);
            }

            WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> effective =
                ResolveEffectiveWorkTypeOrder(local);
            if (effective.IsClear)
            {
                // Do not let the legacy per-WorkGiver fallback resurrect an
                // order explicitly cleared by the preview.
                explicitClear = true;
                return false;
            }

            if (effective.IsSet &&
                TryGetOrderIndex(effective.Value, key.WorkGiver, out order))
            {
                return true;
            }

            if (_baseProvider != null &&
                _baseProvider.TryGetSpecificJobOrder(key, out order))
            {
                return true;
            }

            return !key.IsGlobal &&
                   _baseProvider != null &&
                   _baseProvider.TryGetSpecificJobOrder(
                       new WorkloadSpecificJobKey(
                           WorkloadTargetScope.GlobalShared,
                           null,
                           key.WorkType,
                           key.WorkGiver),
                       out order);
        }

        private static bool TryGetOrderIndex(
            WorkloadWorkTypeOrderPayload payload,
            WorkGiverKey workGiver,
            out int order)
        {
            order = 0;
            if (payload == null || workGiver == null || !payload.IsValid)
            {
                return false;
            }

            for (int i = 0; i < payload.OrderedWorkGivers.Count; i++)
            {
                if (payload.OrderedWorkGivers[i].Equals(workGiver))
                {
                    order = i;
                    return true;
                }
            }

            return false;
        }

        public int GetParentPriority(WorkloadParentPriorityKey key, int fallbackPriority)
        {
            WorkTabEffectiveStateResolution<int> effective =
                ResolveEffectiveParentPriority(key);
            return effective.IsSet ? effective.Value : fallbackPriority;
        }

        public bool TryGetParentPriority(WorkloadParentPriorityKey key, out int priority)
        {
            priority = 0;
            WorkTabEffectiveStateResolution<int> effective = ResolveEffectiveParentPriority(key);
            if (!effective.IsSet)
            {
                return false;
            }

            priority = effective.Value;
            return true;
        }

        public bool IsManualMode(WorkloadParentPriorityKey key, bool fallbackManualMode)
        {
            return TryGetManualMode(key, out bool manualMode) ? manualMode : fallbackManualMode;
        }

        public bool TryGetManualMode(WorkloadParentPriorityKey key, out bool manualMode)
        {
            manualMode = false;
            WorkTabEffectiveStateResolution<bool> effective = ResolveEffectiveManualMode(key);
            if (!effective.IsSet)
            {
                return false;
            }

            manualMode = effective.Value;
            return true;
        }

        public ScheduleKey GetSchedule(PawnKey key, ScheduleKey fallbackSchedule)
        {
            return TryGetSchedule(key, out ScheduleKey schedule) ? schedule : fallbackSchedule;
        }

        public bool TryGetSchedule(PawnKey key, out ScheduleKey schedule)
        {
            schedule = null;
            RefreshProjection();
            if (Owns(WorkloadStateDimension.Schedules) &&
                key != null && key.IsValid &&
                IsEditablePawn(key, _projectedState) &&
                _schedules.TryGetValue(key, out schedule))
            {
                return true;
            }

            return _baseProvider != null && _baseProvider.TryGetSchedule(key, out schedule);
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
            WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> projected =
                ResolveEffectiveSpecificJobPriority(key == null ? null : key.ToTargetKey());
            if (projected.IsSet)
            {
                value = WorkloadScalarValue.FromInteger(projected.Value.Priority);
                return true;
            }
            return false;
        }

        public int GetSpecificJobOrder(WorkloadSpecificJobKey key, int fallbackOrder)
        {
            return TryGetSpecificJobOrder(key, out int order) ? order : fallbackOrder;
        }

        public bool TryGetSpecificJobOrder(WorkloadSpecificJobKey key, out int order)
        {
            order = 0;
            if (TryResolveEffectiveSpecificJobOrder(key, out order, out bool explicitClear))
            {
                return true;
            }

            if (explicitClear)
            {
                return false;
            }

            return _baseProvider != null && _baseProvider.TryGetSpecificJobOrder(key, out order);
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
            WorkTabEffectiveStateResolution<WorkloadSettingValue> projected =
                ResolveEffectivePresentationSetting(key);
            if (projected.IsSet)
            {
                value = projected.Value.Scalar;
                return true;
            }
            return false;
        }

        public WorkTabEffectiveStateMutationResult SetParentPriority(
            WorkloadParentPriorityKey key,
            int priority)
        {
            return Apply(
                WorkTabEffectiveStateDimension.ParentPriority,
                WorkloadOwnershipDimensions.ParentPriorities,
                key?.Pawn,
                key != null && key.IsValid,
                "A valid parent-priority key is required.",
                draft => draft.SetParentPriority(key, priority));
        }

        public WorkTabEffectiveStateMutationResult ClearParentPriority(
            WorkloadParentPriorityKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.ParentPriority,
                WorkloadOwnershipDimensions.ParentPriorities,
                key?.Pawn,
                key != null && key.IsValid,
                "A valid parent-priority key is required.",
                draft => draft.SetParentPriorityIntent(
                    key,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.Clear));
        }

        public WorkTabEffectiveStateMutationResult SetManualMode(
            WorkloadParentPriorityKey key,
            bool manualMode)
        {
            return Apply(
                WorkTabEffectiveStateDimension.ManualMode,
                WorkloadOwnershipDimensions.ManualModes,
                key?.Pawn,
                key != null && key.IsValid,
                "A valid manual-mode key is required.",
                draft => draft.SetManualMode(key, manualMode));
        }

        /// <summary>
        /// Applies a global-mode preview change as one draft mutation so the
        /// projection is rebuilt and fingerprinted once for the whole scope.
        /// </summary>
        internal WorkTabEffectiveStateMutationResult SetManualModes(
            IReadOnlyList<WorkloadParentPriorityKey> keys,
            bool manualMode)
        {
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

        public WorkTabEffectiveStateMutationResult ClearManualMode(
            WorkloadParentPriorityKey key)
        {
            return Apply(
                WorkTabEffectiveStateDimension.ManualMode,
                WorkloadOwnershipDimensions.ManualModes,
                key?.Pawn,
                key != null && key.IsValid,
                "A valid manual-mode key is required.",
                draft => draft.SetManualModeIntent(
                    key,
                    WorkloadIntent<bool>.Clear));
        }

        public WorkTabEffectiveStateMutationResult SetSchedule(
            PawnKey key,
            ScheduleKey schedule)
        {
            return Apply(
                WorkTabEffectiveStateDimension.Schedule,
                WorkloadOwnershipDimensions.Schedules,
                key,
                key != null && key.IsValid && schedule != null && schedule.IsValid,
                "Valid pawn and schedule keys are required.",
                draft => draft.SetSchedule(key, schedule));
        }

        public WorkTabEffectiveStateMutationResult ClearSchedule(PawnKey key)
        {
            return RejectClear(
                WorkTabEffectiveStateDimension.Schedule,
                "A typed schedule target is required to clear a projected schedule.");
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            WorkloadScalarValue value)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                WorkloadOwnershipDimensions.SpecificJobOverrides,
                key?.Pawn,
                key != null && key.IsValid,
                "A valid specific-job key is required.",
                draft => draft.SetSpecificJobOverride(key, value));
        }

        public WorkTabEffectiveStateMutationResult ClearSpecificJobOverride(
            WorkloadSpecificJobKey key)
        {
            return ClearSpecificJobPriority(key == null ? null : key.ToTargetKey());
        }

        public WorkTabEffectiveStateMutationResult SetSpecificJobOrder(
            WorkloadSpecificJobKey key,
            int order)
        {
            return Apply(
                WorkTabEffectiveStateDimension.SpecificJobOrder,
                WorkloadOwnershipDimensions.SpecificJobOrder,
                key?.Pawn,
                key != null && key.IsValid,
                "A valid specific-job key is required.",
                draft => draft.SetSpecificJobOrder(key, order));
        }

        public WorkTabEffectiveStateMutationResult ClearSpecificJobOrder(
            WorkloadSpecificJobKey key)
        {
            return ClearWorkTypeOrder(key == null
                ? null
                : new WorkloadWorkTypeOrderKey(key.Scope, key.Pawn, key.WorkType));
        }

        public WorkTabEffectiveStateMutationResult SetPresentationSetting(
            string key,
            WorkloadScalarValue value)
        {
            return Apply(
                WorkTabEffectiveStateDimension.PresentationSetting,
                WorkloadOwnershipDimensions.PresentationSettings,
                null,
                !string.IsNullOrWhiteSpace(key),
                "A non-empty presentation-setting key is required.",
                draft => draft.SetPresentationSetting(key, value));
        }

        public WorkTabEffectiveStateMutationResult ClearPresentationSetting(string key)
        {
            return ClearPresentationSettingV2(key);
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
            RefreshProjection();
            if (string.IsNullOrWhiteSpace(key) || value.Kind == WorkloadScalarKind.Empty)
            {
                return WorkTabEffectiveStateMutationResult.Blocked(
                    WorkTabEffectiveStateDimension.PresentationSetting,
                    _revision,
                    "A valid presentation setting key and value are required to acquire workload ownership.");
            }

            bool alreadyOwned = OwnsPresentationSetting(key);
            bool added = _presentationOwnershipKeys.Add(key);
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
                _presentationOwnershipKeys.Remove(key);
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
            RefreshProjection();
            if (!OwnsPresentationSetting(key))
            {
                return WorkTabEffectiveStateMutationResult.NoOp(
                    WorkTabEffectiveStateDimension.PresentationSetting,
                    _revision,
                    "The presentation setting is not owned by this workload preview.");
            }

            bool hadKey = _presentationOwnershipKeys.Remove(key);
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
                _presentationOwnershipKeys.Add(key);
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
                case WorkTabEffectiveStateDimension.ParentPriority:
                    _parentPriorityRevision = unchecked(_parentPriorityRevision + 1L);
                    break;
                case WorkTabEffectiveStateDimension.ManualMode:
                    _manualModeRevision = unchecked(_manualModeRevision + 1L);
                    break;
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

        private WorkTabEffectiveStateMutationResult RejectClear(
            WorkTabEffectiveStateDimension dimension,
            string reason)
        {
            // A clear is a tombstone operation, not the same as omitting a
            // projected entry. Until tombstones are represented by the
            // projection/commit contract, reject every clear so a live fallback
            // can never be mistaken for an intentional projected clear.
            RefreshProjection();
            return WorkTabEffectiveStateMutationResult.Blocked(
                dimension,
                _revision,
                reason);
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
            if (_baseProvider is IWorkTabEffectiveStateV2Provider v2)
            {
                return v2.RevisionVector;
            }

            return WorkTabEffectiveStateRevisionVector.FromRevision(
                _baseProvider?.Revision ?? 0L);
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

        private void RebuildIndexes()
        {
            _parentPriorities.Clear();
            _manualModes.Clear();
            _schedules.Clear();
            _specificJobOverrides.Clear();
            _specificJobOrder.Clear();
            _presentationSettings.Clear();
            _parentPriorityIntents.Clear();
            _manualModeIntents.Clear();
            _scheduleIntents.Clear();
            _specificPriorityIntents.Clear();
            _workTypeOrderIntents.Clear();
            _presentationSettingIntents.Clear();

            if (Owns(WorkloadStateDimension.ParentPriorities))
            {
                for (int i = 0; i < _projectedState.ParentPriorities.Count; i++)
                {
                    WorkloadParentPriorityEntry entry = _projectedState.ParentPriorities[i];
                    if (entry != null && IsEditablePawn(entry.Key.Pawn, _projectedState))
                    {
                        _parentPriorities[entry.Key] = entry.Priority;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.ManualModes))
            {
                for (int i = 0; i < _projectedState.ManualModes.Count; i++)
                {
                    WorkloadManualModeEntry entry = _projectedState.ManualModes[i];
                    if (entry != null && IsEditablePawn(entry.Key.Pawn, _projectedState))
                    {
                        _manualModes[entry.Key] = entry.Manual;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.Schedules))
            {
                for (int i = 0; i < _projectedState.Schedules.Count; i++)
                {
                    WorkloadScheduleEntry entry = _projectedState.Schedules[i];
                    if (entry != null && IsEditablePawn(entry.Pawn, _projectedState))
                    {
                        _schedules[entry.Pawn] = entry.Schedule;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                for (int i = 0; i < _projectedState.SpecificJobOverrides.Count; i++)
                {
                    WorkloadSpecificJobOverrideEntry entry = _projectedState.SpecificJobOverrides[i];
                    if (entry != null &&
                        IsEditableSpecificTarget(entry.Key.ToTargetKey(), _projectedState))
                    {
                        _specificJobOverrides[entry.Key] = entry.Value;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                for (int i = 0; i < _projectedState.SpecificJobOrder.Count; i++)
                {
                    WorkloadSpecificJobOrderEntry entry = _projectedState.SpecificJobOrder[i];
                    if (entry != null &&
                        IsEditableSpecificTarget(entry.Key.ToTargetKey(), _projectedState))
                    {
                        _specificJobOrder[entry.Key] = entry.Order;
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
                        _presentationOwnershipKeys.Add(entry.Key);
                        _presentationSettings[entry.Key] = entry.Value;
                    }
                }

                for (int i = 0; i < _projectedState.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry =
                        _projectedState.PresentationSettingIntents[i];
                    if (entry != null && !entry.Intent.IsNoOpinion)
                    {
                        _presentationOwnershipKeys.Add(entry.Key);
                    }
                }
            }

            if (Owns(WorkloadStateDimension.ParentPriorities))
            {
                for (int i = 0; i < _projectedState.ParentPriorityIntents.Count; i++)
                {
                    WorkloadParentPriorityIntentEntry entry = _projectedState.ParentPriorityIntents[i];
                    if (entry != null && IsEditablePawn(entry.Key.Pawn, _projectedState))
                    {
                        _parentPriorityIntents[entry.Key] = entry.Intent;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.ManualModes))
            {
                for (int i = 0; i < _projectedState.ManualModeIntents.Count; i++)
                {
                    WorkloadManualModeIntentEntry entry = _projectedState.ManualModeIntents[i];
                    if (entry != null && IsEditablePawn(entry.Key.Pawn, _projectedState))
                    {
                        _manualModeIntents[entry.Key] = entry.Intent;
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
                        _scheduleIntents[entry.Key] = entry.Intent;
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
                        _specificPriorityIntents[entry.Key] = entry.Intent;
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
                        _workTypeOrderIntents[entry.Key] = entry.Intent;
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
                        _presentationSettingIntents[entry.Key] = entry.Intent;
                    }
                }
            }
        }
    }
}
