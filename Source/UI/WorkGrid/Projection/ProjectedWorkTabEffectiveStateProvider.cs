using System;
using System.Collections.Generic;
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
        IWorkTabEffectiveStateEditor
    {
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

        private WorkloadProjectedState _projectedState;
        private string _semanticFingerprint;
        private long _baseRevision;
        private long _draftRevision;
        private long _observedDraftRevision;
        private long _revision;
        private bool _hasProjection;
        private long _capturedBaseRevision;
        private long _capturedBasePassId;
        private bool _hasCapturedBaseRevision;

        public ProjectedWorkTabEffectiveStateProvider(
            WorkloadDraft draft,
            IWorkTabEffectiveStateProvider baseProvider = null,
            WorkloadOwnershipDimensions ownedDimensions = WorkloadOwnershipDimensions.All,
            string providerId = "bwt.preview",
            WorkloadScope scope = null,
            IEnumerable<PawnKey> editablePawnIds = null)
        {
            _draft = draft ?? new WorkloadDraft(WorkloadProjectedState.Empty);
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
        public WorkTabEffectiveStateRevision RevisionToken =>
            new WorkTabEffectiveStateRevision(ProviderId, Revision, Source);
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
            _hasCapturedBaseRevision = true;
        }

        public int GetParentPriority(WorkloadParentPriorityKey key, int fallbackPriority)
        {
            return TryGetParentPriority(key, out int priority) ? priority : fallbackPriority;
        }

        public bool TryGetParentPriority(WorkloadParentPriorityKey key, out int priority)
        {
            priority = 0;
            RefreshProjection();
            if (Owns(WorkloadStateDimension.ParentPriorities) &&
                key != null && key.IsValid &&
                IsEditablePawn(key.Pawn, _projectedState) &&
                _parentPriorities.TryGetValue(key, out priority))
            {
                return true;
            }

            return _baseProvider != null && _baseProvider.TryGetParentPriority(key, out priority);
        }

        public bool IsManualMode(WorkloadParentPriorityKey key, bool fallbackManualMode)
        {
            return TryGetManualMode(key, out bool manualMode) ? manualMode : fallbackManualMode;
        }

        public bool TryGetManualMode(WorkloadParentPriorityKey key, out bool manualMode)
        {
            manualMode = false;
            RefreshProjection();
            if (Owns(WorkloadStateDimension.ManualModes) &&
                key != null && key.IsValid &&
                IsEditablePawn(key.Pawn, _projectedState) &&
                _manualModes.TryGetValue(key, out manualMode))
            {
                return true;
            }

            return _baseProvider != null && _baseProvider.TryGetManualMode(key, out manualMode);
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
            RefreshProjection();
            if (Owns(WorkloadStateDimension.SpecificJobOverrides) &&
                key != null && key.IsValid &&
                IsEditablePawn(key.Pawn, _projectedState) &&
                _specificJobOverrides.TryGetValue(key, out value))
            {
                return true;
            }

            return _baseProvider != null && _baseProvider.TryGetSpecificJobOverride(key, out value);
        }

        public int GetSpecificJobOrder(WorkloadSpecificJobKey key, int fallbackOrder)
        {
            return TryGetSpecificJobOrder(key, out int order) ? order : fallbackOrder;
        }

        public bool TryGetSpecificJobOrder(WorkloadSpecificJobKey key, out int order)
        {
            order = 0;
            RefreshProjection();
            if (Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                // An owned ordering dimension is authoritative for the
                // preview. Falling through to the live reassignment manager
                // would make the rendered column order disagree with the
                // staged workload. Missing or out-of-scope projected entries
                // therefore fail closed and let callers use their safe
                // non-reordered fallback.
                return key != null && key.IsValid &&
                       IsEditablePawn(key.Pawn, _projectedState) &&
                       _specificJobOrder.TryGetValue(key, out order);
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
            RefreshProjection();
            if (Owns(WorkloadStateDimension.PresentationSettings) &&
                !string.IsNullOrWhiteSpace(key) &&
                _presentationSettings.TryGetValue(key, out value))
            {
                return true;
            }

            return _baseProvider != null && _baseProvider.TryGetPresentationSetting(key, out value);
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
            return RejectClear(
                WorkTabEffectiveStateDimension.ParentPriority,
                "Clearing a projected parent priority is not supported yet; the preview would otherwise fall through to live state.");
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

        public WorkTabEffectiveStateMutationResult ClearManualMode(
            WorkloadParentPriorityKey key)
        {
            return RejectClear(
                WorkTabEffectiveStateDimension.ManualMode,
                "Clearing a projected manual-mode value is not supported yet; the preview would otherwise fall through to live state.");
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
                "Clearing a projected schedule is not supported yet; the preview would otherwise fall through to live state.");
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
            return RejectClear(
                WorkTabEffectiveStateDimension.SpecificJobOverride,
                "Clearing a projected specific-job override is not supported yet; the preview would otherwise fall through to live state.");
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
            return RejectClear(
                WorkTabEffectiveStateDimension.SpecificJobOrder,
                "Clearing a projected specific-job order is not supported yet; the preview would otherwise fall through to live state.");
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
            return RejectClear(
                WorkTabEffectiveStateDimension.PresentationSetting,
                "Clearing a projected presentation setting is not supported yet; the preview would otherwise fall through to live state.");
        }

        private WorkTabEffectiveStateMutationResult Apply(
            WorkTabEffectiveStateDimension dimension,
            WorkloadOwnershipDimensions ownership,
            PawnKey pawn,
            bool valid,
            string invalidReason,
            Action<WorkloadDraft> mutation)
        {
            RefreshProjection();
            long previousRevision = _revision;
            if ((_ownedDimensions & ownership) != ownership)
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

            string beforeFingerprint = _semanticFingerprint;
            mutation(_draft);
            _draftRevision = unchecked(_draftRevision + 1L);
            RefreshProjection();
            if (StringComparer.Ordinal.Equals(beforeFingerprint, _semanticFingerprint))
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

        private void RefreshProjection()
        {
            long baseRevision = ReadBaseRevision();
            bool firstProjection = !_hasProjection;
            bool draftChanged = firstProjection || _observedDraftRevision != _draftRevision;
            bool baseChanged = firstProjection || _baseRevision != baseRevision;
            if (!draftChanged && !baseChanged)
            {
                return;
            }

            bool projectedChanged = false;
            if (draftChanged)
            {
                WorkloadProjectedState projected =
                    _draft.ProjectedState ?? WorkloadProjectedState.Empty;
                string fingerprint = projected.GetSemanticFingerprint(_ownedDimensions);
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
                _hasProjection = true;
                return;
            }

            if (projectedChanged || baseChanged)
            {
                _revision = unchecked(_revision + 1L);
                _baseRevision = baseRevision;
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

        private void RebuildIndexes()
        {
            _parentPriorities.Clear();
            _manualModes.Clear();
            _schedules.Clear();
            _specificJobOverrides.Clear();
            _specificJobOrder.Clear();
            _presentationSettings.Clear();

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
                    if (entry != null && IsEditablePawn(entry.Key.Pawn, _projectedState))
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
                    if (entry != null && IsEditablePawn(entry.Key.Pawn, _projectedState))
                    {
                        _specificJobOrder[entry.Key] = entry.Order;
                    }
                }
            }

            if (Owns(WorkloadStateDimension.PresentationSettings))
            {
                for (int i = 0; i < _projectedState.PresentationSettings.Count; i++)
                {
                    WorkloadPresentationSettingEntry entry = _projectedState.PresentationSettings[i];
                    if (entry != null) _presentationSettings[entry.Key] = entry.Value;
                }
            }
        }
    }
}
