using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Workloads.V2
{
    internal static class WorkloadV2OwnershipResolver
    {
        internal static WorkloadOwnershipDimensions Effective(
            WorkloadTemplate template,
            WorkloadProjectedState projectedState,
            WorkloadProjectedState baselineState = null)
        {
            WorkloadOwnershipDimensions ownership = template?.Definition == null
                ? WorkloadOwnershipDimensions.None
                : template.Definition.OwnershipDimensions;
            if (!ownership.Owns(WorkloadStateDimension.Schedules) &&
                (HasTypedPayload(projectedState, WorkloadStateDimension.Schedules) ||
                 HasTypedPayload(baselineState, WorkloadStateDimension.Schedules)))
                ownership |= WorkloadOwnershipDimensions.Schedules;
            if (!ownership.Owns(WorkloadStateDimension.PresentationSettings) &&
                (HasTypedPayload(projectedState, WorkloadStateDimension.PresentationSettings) ||
                 HasTypedPayload(baselineState, WorkloadStateDimension.PresentationSettings)))
                ownership |= WorkloadOwnershipDimensions.PresentationSettings;
            return ownership;
        }

        internal static bool HasLegacyPayload(WorkloadProjectedState state, WorkloadStateDimension dimension)
        {
            if (state == null) return false;
            switch (dimension)
            {
                case WorkloadStateDimension.Schedules:
                    return state.Schedules.Count > 0 && !HasTypedPayload(state, dimension);
                case WorkloadStateDimension.PresentationSettings:
                    return state.PresentationSettings.Count > 0 && !HasTypedPayload(state, dimension);
                default:
                    return false;
            }
        }

        internal static bool HasUnsupportedLegacyPayload(WorkloadTemplate template)
        {
            if (template?.Definition == null) return true;
            WorkloadProjectedState state = template.ProjectedState ?? WorkloadProjectedState.Empty;
            return HasLegacyPayload(state, WorkloadStateDimension.Schedules) ||
                HasLegacyPayload(state, WorkloadStateDimension.PresentationSettings);
        }

        internal static bool HasTypedPayload(WorkloadProjectedState state, WorkloadStateDimension dimension)
        {
            if (state == null) return false;
            if (dimension == WorkloadStateDimension.Schedules)
            {
                for (int i = 0; i < state.ScheduleIntents.Count; i++)
                    if (state.ScheduleIntents[i] != null && !state.ScheduleIntents[i].Intent.IsNoOpinion) return true;
                return false;
            }
            if (dimension == WorkloadStateDimension.PresentationSettings)
            {
                for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
                    if (state.PresentationSettingIntents[i] != null && !state.PresentationSettingIntents[i].Intent.IsNoOpinion) return true;
            }
            return false;
        }
    }

    public enum WorkloadSessionStatus
    {
        Open = 0,
        Editing = 1,
        Reverted = 2,
        Applied = 3,
        Updated = 4,
        Forked = 5,
        Rejected = 6
    }

    public enum WorkloadDecisionKind
    {
        Apply = 0,
        Update = 1,
        Fork = 2
    }

    public sealed class WorkloadPreviewPlan
    {
        internal WorkloadPreviewPlan(
            WorkloadDecisionKind decisionKind,
            WorkloadTemplate sourceTemplate,
            WorkloadProjectedState beforeState,
            WorkloadProjectedState afterState,
            WorkloadSemanticDiff diff,
            WorkloadValidationResult validation)
        {
            DecisionKind = decisionKind;
            SourceTemplate = sourceTemplate ?? WorkloadTemplate.Empty;
            BeforeState = beforeState ?? WorkloadProjectedState.Empty;
            AfterState = afterState ?? WorkloadProjectedState.Empty;
            Diff = diff ?? WorkloadSemanticDiff.Between(BeforeState, AfterState);
            Validation = validation ?? new WorkloadValidationResult(new WorkloadValidationIssue[0]);
        }

        public WorkloadDecisionKind DecisionKind { get; private set; }
        public WorkloadTemplate SourceTemplate { get; private set; }
        public WorkloadProjectedState BeforeState { get; private set; }
        public WorkloadProjectedState AfterState { get; private set; }
        public WorkloadSemanticDiff Diff { get; private set; }
        public WorkloadValidationResult Validation { get; private set; }
        public IReadOnlyList<WorkloadChange> Changes => Diff.Changes;
        public bool HasChanges => !Diff.IsEmpty;
        public bool CanProceed => Validation.CanApply;

        // A plan is only data. No live writer, game object, or runtime callback is
        // reachable from this model boundary.
        public bool IsSideEffectFree => true;
        public bool HasLiveSideEffects => false;
    }

    public sealed class WorkloadSessionDecision
    {
        internal WorkloadSessionDecision(
            WorkloadDecisionKind kind,
            bool accepted,
            WorkloadPreviewPlan plan,
            WorkloadSession session,
            WorkloadTemplate resultTemplate,
            WorkloadProjectedState appliedState,
            string rejectionReason)
        {
            Kind = kind;
            Accepted = accepted;
            Plan = plan;
            Session = session;
            ResultTemplate = resultTemplate;
            AppliedState = appliedState;
            RejectionReason = rejectionReason ?? string.Empty;
        }

        public WorkloadDecisionKind Kind { get; private set; }
        public bool Accepted { get; private set; }
        public WorkloadPreviewPlan Plan { get; private set; }
        public WorkloadSession Session { get; private set; }
        public WorkloadTemplate ResultTemplate { get; private set; }
        public WorkloadTemplate TargetTemplate => ResultTemplate;
        public WorkloadProjectedState AppliedState { get; private set; }
        public string RejectionReason { get; private set; }
        public bool IsSideEffectFree => true;
    }

    /// <summary>
    /// Runtime-only optimistic-concurrency capture for a preview session. This
    /// is deliberately not part of the workload model or its persistence
    /// fingerprint: it describes the live BWT-owned values that must still be
    /// true when an Apply crosses into the writer boundary.
    /// </summary>
    internal sealed class WorkloadRuntimeBaseline
    {
        private readonly Dictionary<WorkloadParentPriorityKey, int> _parentPriorities;
        private readonly Dictionary<WorkloadSpecificJobKey, WorkloadSpecificJobRuntimeBaseline> _specificJobOverrides;
        private readonly Dictionary<WorkloadParentPriorityKey, WorkloadSpecificJobOrderRuntimeBaseline> _specificJobOrders;

        internal WorkloadRuntimeBaseline(
            IEnumerable<WorkloadParentPriorityEntry> parentPriorities = null,
            bool hasManualMode = false,
            bool manualMode = false,
            IEnumerable<WorkloadSpecificJobRuntimeBaseline> specificJobOverrides = null,
            IEnumerable<WorkloadSpecificJobOrderRuntimeBaseline> specificJobOrders = null,
            bool hasSpecificJobRevision = false,
            int specificJobRevision = 0)
        {
            _parentPriorities = new Dictionary<WorkloadParentPriorityKey, int>();
            if (parentPriorities != null)
            {
                foreach (WorkloadParentPriorityEntry entry in parentPriorities)
                {
                    if (entry?.Key != null) _parentPriorities[entry.Key] = entry.Priority;
                }
            }

            _specificJobOverrides = new Dictionary<WorkloadSpecificJobKey, WorkloadSpecificJobRuntimeBaseline>();
            if (specificJobOverrides != null)
            {
                foreach (WorkloadSpecificJobRuntimeBaseline entry in specificJobOverrides)
                {
                    if (entry?.Key != null) _specificJobOverrides[entry.Key] = entry;
                }
            }

            _specificJobOrders = new Dictionary<WorkloadParentPriorityKey, WorkloadSpecificJobOrderRuntimeBaseline>();
            if (specificJobOrders != null)
            {
                foreach (WorkloadSpecificJobOrderRuntimeBaseline entry in specificJobOrders)
                {
                    if (entry?.Parent != null) _specificJobOrders[entry.Parent] = entry;
                }
            }

            HasManualMode = hasManualMode;
            ManualMode = manualMode;
            HasSpecificJobRevision = hasSpecificJobRevision;
            SpecificJobRevision = specificJobRevision;
        }

        internal bool HasManualMode { get; private set; }
        internal bool ManualMode { get; private set; }
        internal bool HasSpecificJobRevision { get; private set; }
        internal int SpecificJobRevision { get; private set; }
        internal IEnumerable<KeyValuePair<WorkloadParentPriorityKey, int>> ParentPriorities =>
            _parentPriorities;
        internal IEnumerable<WorkloadSpecificJobRuntimeBaseline> SpecificJobOverrides =>
            _specificJobOverrides.Values;
        internal IEnumerable<WorkloadSpecificJobOrderRuntimeBaseline> SpecificJobOrders =>
            _specificJobOrders.Values;

        internal bool TryGetParentPriority(WorkloadParentPriorityKey key, out int priority)
        {
            return _parentPriorities.TryGetValue(key, out priority);
        }

        internal bool TryGetSpecificJobOverride(
            WorkloadSpecificJobKey key,
            out bool hasOverride,
            out int priority)
        {
            WorkloadSpecificJobRuntimeBaseline baseline;
            if (_specificJobOverrides.TryGetValue(key, out baseline))
            {
                hasOverride = baseline.HasOverride;
                priority = baseline.Priority;
                return true;
            }

            hasOverride = false;
            priority = WorkloadScalarValue.Empty.IntegerValue;
            return false;
        }

        internal bool TryGetSpecificJobOrder(
            WorkloadParentPriorityKey parent,
            out WorkloadSpecificJobOrderRuntimeBaseline baseline)
        {
            return _specificJobOrders.TryGetValue(parent, out baseline);
        }

        internal bool ContainsPawn(PawnKey pawn)
        {
            PawnKey safePawn = pawn ?? new PawnKey(null);
            foreach (WorkloadParentPriorityKey key in _parentPriorities.Keys)
            {
                if (key.Pawn.Equals(safePawn)) return true;
            }

            foreach (WorkloadSpecificJobKey key in _specificJobOverrides.Keys)
            {
                if (key.Pawn.Equals(safePawn)) return true;
            }

            foreach (WorkloadParentPriorityKey key in _specificJobOrders.Keys)
            {
                if (key.Pawn.Equals(safePawn)) return true;
            }

            return false;
        }

        internal WorkloadRuntimeBaseline ExtendForPawns(
            WorkloadRuntimeBaseline extension,
            IEnumerable<PawnKey> pawns)
        {
            if (extension == null) return this;

            // Extensions must belong to the same optimistic global snapshot.
            // Mixing manual mode or specific-job revisions would make the
            // newly included pawn appear validated against a state that never
            // existed atomically.
            if (HasManualMode != extension.HasManualMode
                || (HasManualMode && ManualMode != extension.ManualMode)
                || HasSpecificJobRevision != extension.HasSpecificJobRevision
                || (HasSpecificJobRevision
                    && SpecificJobRevision != extension.SpecificJobRevision))
            {
                return null;
            }

            var included = new HashSet<PawnKey>(pawns ?? new PawnKey[0]);
            var parentPriorities = new List<WorkloadParentPriorityEntry>();
            foreach (KeyValuePair<WorkloadParentPriorityKey, int> entry in _parentPriorities)
            {
                parentPriorities.Add(new WorkloadParentPriorityEntry(entry.Key, entry.Value));
            }

            foreach (KeyValuePair<WorkloadParentPriorityKey, int> entry in extension._parentPriorities)
            {
                if (included.Contains(entry.Key.Pawn))
                {
                    parentPriorities.Add(new WorkloadParentPriorityEntry(entry.Key, entry.Value));
                }
            }

            var specificOverrides = new List<WorkloadSpecificJobRuntimeBaseline>(
                _specificJobOverrides.Values);
            foreach (WorkloadSpecificJobRuntimeBaseline entry in extension._specificJobOverrides.Values)
            {
                if (included.Contains(entry.Key.Pawn)) specificOverrides.Add(entry);
            }

            var specificOrders = new List<WorkloadSpecificJobOrderRuntimeBaseline>(
                _specificJobOrders.Values);
            foreach (WorkloadSpecificJobOrderRuntimeBaseline entry in extension._specificJobOrders.Values)
            {
                if (included.Contains(entry.Parent.Pawn)) specificOrders.Add(entry);
            }

            return new WorkloadRuntimeBaseline(
                parentPriorities,
                HasManualMode || extension.HasManualMode,
                HasManualMode ? ManualMode : extension.ManualMode,
                specificOverrides,
                specificOrders,
                HasSpecificJobRevision || extension.HasSpecificJobRevision,
                HasSpecificJobRevision ? SpecificJobRevision : extension.SpecificJobRevision);
        }

        internal bool Preserves(WorkloadRuntimeBaseline original)
        {
            if (original == null) return true;
            if (HasManualMode != original.HasManualMode ||
                (HasManualMode && ManualMode != original.ManualMode) ||
                HasSpecificJobRevision != original.HasSpecificJobRevision ||
                (HasSpecificJobRevision &&
                 SpecificJobRevision != original.SpecificJobRevision))
            {
                return false;
            }

            foreach (KeyValuePair<WorkloadParentPriorityKey, int> entry in
                     original._parentPriorities)
            {
                if (!_parentPriorities.TryGetValue(entry.Key, out int value) ||
                    value != entry.Value)
                {
                    return false;
                }
            }

            foreach (KeyValuePair<WorkloadSpecificJobKey, WorkloadSpecificJobRuntimeBaseline> entry in
                     original._specificJobOverrides)
            {
                if (!_specificJobOverrides.TryGetValue(entry.Key, out WorkloadSpecificJobRuntimeBaseline value) ||
                    value.HasOverride != entry.Value.HasOverride ||
                    value.Priority != entry.Value.Priority)
                {
                    return false;
                }
            }

            foreach (KeyValuePair<WorkloadParentPriorityKey, WorkloadSpecificJobOrderRuntimeBaseline> entry in
                     original._specificJobOrders)
            {
                if (!_specificJobOrders.TryGetValue(entry.Key, out WorkloadSpecificJobOrderRuntimeBaseline value) ||
                    value.HasStoredOrder != entry.Value.HasStoredOrder ||
                    !SequenceEqual(
                        value.OrderedWorkGiverNames,
                        entry.Value.OrderedWorkGiverNames))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SequenceEqual(
            IReadOnlyList<string> left,
            IReadOnlyList<string> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!StringComparer.Ordinal.Equals(left[i], right[i])) return false;
            }

            return true;
        }
    }

    internal sealed class WorkloadSpecificJobRuntimeBaseline
    {
        internal WorkloadSpecificJobRuntimeBaseline(
            WorkloadSpecificJobKey key,
            bool hasOverride,
            int priority)
        {
            Key = key ?? new WorkloadSpecificJobKey(null, null, null);
            HasOverride = hasOverride;
            Priority = priority;
        }

        internal WorkloadSpecificJobKey Key { get; private set; }
        internal bool HasOverride { get; private set; }
        internal int Priority { get; private set; }
    }

    internal sealed class WorkloadSpecificJobOrderRuntimeBaseline
    {
        internal WorkloadSpecificJobOrderRuntimeBaseline(
            WorkloadParentPriorityKey parent,
            bool hasStoredOrder,
            IEnumerable<string> orderedWorkGiverNames)
        {
            Parent = parent ?? new WorkloadParentPriorityKey(null, null);
            HasStoredOrder = hasStoredOrder;
            OrderedWorkGiverNames = new List<string>(orderedWorkGiverNames ?? new string[0]).AsReadOnly();
        }

        internal WorkloadParentPriorityKey Parent { get; private set; }
        internal bool HasStoredOrder { get; private set; }
        internal IReadOnlyList<string> OrderedWorkGiverNames { get; private set; }
    }

    public sealed class WorkloadSession
    {
        private readonly WorkloadValidationContext _validationContext;

        private WorkloadSession(
            WorkloadTemplate sourceTemplate,
            WorkloadProjectedState templateBaselineState,
            WorkloadProjectedState liveBaselineState,
            WorkloadProjectedState projectedState,
            WorkloadSessionStatus status,
            WorkloadValidationContext validationContext,
            string sourceIdentity,
            bool hasCapturedLiveBaseline,
            WorkloadRuntimeBaseline runtimeBaseline)
        {
            SourceTemplate = sourceTemplate ?? WorkloadTemplate.Empty;
            TemplateBaselineState = templateBaselineState ?? WorkloadProjectedState.Empty;
            LiveBaselineState = liveBaselineState ?? TemplateBaselineState;
            ProjectedState = NormalizeState(SourceTemplate, projectedState ?? WorkloadProjectedState.Empty);
            Status = status;
            _validationContext = validationContext ?? WorkloadValidationContext.Default;
            SourceIdentity = sourceIdentity ?? string.Empty;
            HasCapturedLiveBaseline = hasCapturedLiveBaseline;
            RuntimeBaseline = runtimeBaseline;
        }

        public static WorkloadSession Open(
            WorkloadTemplate template,
            WorkloadValidationContext validationContext = null,
            WorkloadProjectedState liveBaselineState = null)
        {
            WorkloadTemplate safeTemplate = template ?? WorkloadTemplate.Empty;
            WorkloadProjectedState templateState = NormalizeState(safeTemplate, safeTemplate.ProjectedState);
            WorkloadProjectedState liveState = NormalizeState(
                safeTemplate,
                liveBaselineState ?? templateState);
            return new WorkloadSession(
                safeTemplate,
                templateState,
                liveState,
                templateState,
                WorkloadSessionStatus.Open,
                validationContext ?? WorkloadValidationContext.Default,
                string.Empty,
                false,
                null);
        }

        /// <summary>
        /// Production preview entry point. A deterministic model session may
        /// use <see cref="Open"/> with the template as its synthetic baseline,
        /// but a session that can cross into the runtime must carry both an
        /// explicit live capture and the identity of the template that was
        /// captured. Returning null is intentional: callers must fail closed
        /// instead of manufacturing a live baseline from the template.
        /// </summary>
        internal static WorkloadSession OpenCaptured(
            WorkloadTemplate template,
            WorkloadProjectedState liveBaselineState,
            string sourceIdentity,
            WorkloadValidationContext validationContext = null,
            WorkloadRuntimeBaseline runtimeBaseline = null)
        {
            WorkloadTemplate safeTemplate = template ?? WorkloadTemplate.Empty;
            if (liveBaselineState == null ||
                string.IsNullOrWhiteSpace(sourceIdentity) ||
                !StringComparer.Ordinal.Equals(
                    sourceIdentity,
                    GetSourceIdentity(safeTemplate)))
            {
                return null;
            }

            WorkloadProjectedState templateState = NormalizeState(
                safeTemplate,
                safeTemplate.ProjectedState);
            WorkloadProjectedState liveState = NormalizeState(
                safeTemplate,
                liveBaselineState);
            return new WorkloadSession(
                safeTemplate,
                templateState,
                liveState,
                templateState,
                WorkloadSessionStatus.Open,
                validationContext ?? WorkloadValidationContext.Default,
                sourceIdentity,
                true,
                runtimeBaseline);
        }

        internal static string GetSourceIdentity(WorkloadTemplate template)
        {
            WorkloadTemplate safeTemplate = template ?? WorkloadTemplate.Empty;
            string definition = safeTemplate.Definition?.CanonicalForm ?? string.Empty;
            string state = safeTemplate.ProjectedState?.CanonicalForm ?? string.Empty;
            return WorkloadCanonical.Fingerprint(
                WorkloadCanonical.Encode(definition) + WorkloadCanonical.Encode(state));
        }

        public WorkloadTemplate SourceTemplate { get; private set; }
        public WorkloadProjectedState TemplateBaselineState { get; private set; }
        public WorkloadProjectedState LiveBaselineState { get; private set; }
        public WorkloadProjectedState BaseState => TemplateBaselineState;
        public WorkloadProjectedState ProjectedState { get; private set; }
        public bool HasCapturedLiveBaseline { get; private set; }
        internal WorkloadRuntimeBaseline RuntimeBaseline { get; private set; }
        internal string SourceIdentity { get; private set; }

        /// <summary>
        /// Pawns excluded from this session but not excluded by the saved
        /// workload scope. These are application-only choices until a future
        /// explicit scope-editing action opts into persisting them.
        /// </summary>
        public IReadOnlyList<PawnKey> SessionExcludedPawnIds
        {
            get { return GetSessionOnlyExcludedPawnIds(); }
        }

        internal bool IsExcludedForApply(PawnKey pawn)
        {
            return ProjectedState.IsExcluded(pawn);
        }

        public WorkloadSessionStatus Status { get; private set; }
        public bool IsTerminal
        {
            get
            {
                return Status == WorkloadSessionStatus.Applied
                    || Status == WorkloadSessionStatus.Updated
                    || Status == WorkloadSessionStatus.Forked;
            }
        }

        // Update compares the staged state with the persisted template. Apply
        // compares it with the live game state captured when the preview opened.
        public WorkloadSemanticDiff SemanticDiff
        {
            get { return TemplateDiff; }
        }

        public WorkloadSemanticDiff TemplateDiff
        {
            get
            {
                return WorkloadSemanticDiff.Between(
                    TemplateBaselineState,
                    BuildPersistenceState(),
                    EffectiveOwnership);
            }
        }

        public WorkloadSemanticDiff LiveDiff
        {
            get
            {
                return WorkloadSemanticDiff.Between(
                    LiveBaselineState,
                    BuildLiveImpactState(),
                    EffectiveOwnership);
            }
        }

        public bool IsDirty => !TemplateDiff.IsEmpty;

        /// <summary>
        /// Legacy value-only entries still fail closed when a removal cannot be
        /// mapped to an explicit typed Clear intent. Typed entries carry their
        /// own tombstone, so a deliberate clear is a valid draft operation.
        /// </summary>
        public bool HasUnsupportedClears
        {
            get { return GetUnsupportedClearDimensions().Count > 0; }
        }

        public IReadOnlyList<WorkloadStateDimension> UnsupportedClearDimensions
        {
            get { return GetUnsupportedClearDimensions(); }
        }

        public WorkloadTemplate TargetTemplate
        {
            get { return BuildTargetTemplate(BuildPersistenceState(), SourceTemplate.StableId, SourceTemplate.Label); }
        }

        public WorkloadValidationResult Validation
        {
            get { return WorkloadValidator.Validate(TargetTemplate, _validationContext); }
        }

        public WorkloadSession Edit(Action<WorkloadDraft> edit)
        {
            if (IsTerminal) return this;
            var draft = new WorkloadDraft(ProjectedState);
            if (edit != null) edit(draft);
            return NewSession(draft.ProjectedState, WorkloadSessionStatus.Editing);
        }

        public WorkloadSession EditState(WorkloadProjectedState projectedState)
        {
            if (IsTerminal) return this;
            return NewSession(projectedState ?? WorkloadProjectedState.Empty, WorkloadSessionStatus.Editing);
        }

        public WorkloadSession ExcludePawn(PawnKey pawn)
        {
            if (IsTerminal) return this;
            return NewSession(ProjectedState.ExcludePawn(pawn), WorkloadSessionStatus.Editing);
        }

        public WorkloadSession IncludePawn(PawnKey pawn)
        {
            if (IsTerminal) return this;
            return NewSession(ProjectedState.IncludePawn(pawn), WorkloadSessionStatus.Editing);
        }

        internal WorkloadSession ExtendCapturedBaseline(
            IEnumerable<PawnKey> pawns,
            WorkloadProjectedState liveBaselineExtension,
            WorkloadRuntimeBaseline runtimeBaseline)
        {
            if (IsTerminal || runtimeBaseline == null) return this;
            var included = new HashSet<PawnKey>(pawns ?? new PawnKey[0]);
            if (included.Count == 0) return this;

            var draft = new WorkloadDraft(LiveBaselineState);
            WorkloadProjectedState extensionState =
                liveBaselineExtension ?? WorkloadProjectedState.Empty;
            for (int i = 0; i < extensionState.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = extensionState.ParentPriorities[i];
                if (included.Contains(entry.Key.Pawn))
                {
                    draft.SetParentPriority(entry.Key, entry.Priority);
                }
            }

            for (int i = 0; i < extensionState.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = extensionState.ManualModes[i];
                if (included.Contains(entry.Key.Pawn))
                {
                    draft.SetManualMode(entry.Key, entry.Manual);
                }
            }

            for (int i = 0; i < extensionState.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = extensionState.SpecificJobOverrides[i];
                if (included.Contains(entry.Key.Pawn))
                {
                    draft.SetSpecificJobOverride(entry.Key, entry.Value);
                }
            }

            for (int i = 0; i < extensionState.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = extensionState.SpecificJobOrder[i];
                if (included.Contains(entry.Key.Pawn))
                {
                    draft.SetSpecificJobOrder(entry.Key, entry.Order);
                }
            }

            return new WorkloadSession(
                SourceTemplate,
                TemplateBaselineState,
                draft.ProjectedState,
                ProjectedState,
                Status,
                _validationContext,
                SourceIdentity,
                HasCapturedLiveBaseline,
                runtimeBaseline);
        }

        public WorkloadSession Revert()
        {
            if (IsTerminal) return this;
            return NewSession(TemplateBaselineState, WorkloadSessionStatus.Reverted);
        }

        public WorkloadPreviewPlan Preview()
        {
            return BuildPlan(WorkloadDecisionKind.Apply);
        }

        public WorkloadSessionDecision Apply()
        {
            WorkloadPreviewPlan plan = BuildPlan(WorkloadDecisionKind.Apply);
            if (!plan.CanProceed)
            {
                return Rejected(WorkloadDecisionKind.Apply, plan, "The projected state did not pass validation.");
            }

            WorkloadTemplate resultTemplate = BuildApplyTemplate();
            WorkloadSession applied = TerminalSession(resultTemplate, WorkloadSessionStatus.Applied);
            return new WorkloadSessionDecision(
                WorkloadDecisionKind.Apply,
                true,
                plan,
                applied,
                resultTemplate,
                plan.AfterState,
                null);
        }

        public WorkloadSessionDecision Update()
        {
            WorkloadPreviewPlan plan = BuildPlan(WorkloadDecisionKind.Update);
            if (!plan.CanProceed)
            {
                return Rejected(WorkloadDecisionKind.Update, plan, "The updated template did not pass validation.");
            }

            WorkloadTemplate updatedTemplate = TargetTemplate;
            WorkloadSession updated = TerminalSession(updatedTemplate, WorkloadSessionStatus.Updated);
            return new WorkloadSessionDecision(
                WorkloadDecisionKind.Update,
                true,
                plan,
                updated,
                updatedTemplate,
                null,
                null);
        }

        public WorkloadSessionDecision Fork(string stableId, string label)
        {
            WorkloadPreviewPlan plan = BuildPlan(WorkloadDecisionKind.Fork);
            string safeId = stableId ?? string.Empty;
            if (!safeId.AnyNonWhitespace())
            {
                return Rejected(WorkloadDecisionKind.Fork, plan, "A fork must have a stable ID.");
            }

            if (StringComparer.Ordinal.Equals(safeId, SourceTemplate.StableId))
            {
                return Rejected(WorkloadDecisionKind.Fork, plan, "A fork must use a different stable ID.");
            }

            if (!plan.CanProceed)
            {
                return Rejected(WorkloadDecisionKind.Fork, plan, "The forked template did not pass validation.");
            }

            string safeLabel = (label ?? string.Empty).AnyNonWhitespace() ? label : SourceTemplate.Label;
            WorkloadTemplate forkedTemplate = BuildTargetTemplate(BuildPersistenceState(), safeId, safeLabel);
            WorkloadSession forked = TerminalSession(forkedTemplate, WorkloadSessionStatus.Forked);
            return new WorkloadSessionDecision(
                WorkloadDecisionKind.Fork,
                true,
                plan,
                forked,
                forkedTemplate,
                null,
                null);
        }

        private WorkloadPreviewPlan BuildPlan(WorkloadDecisionKind kind)
        {
            WorkloadTemplate target = TargetTemplate;
            WorkloadValidationResult validation = WorkloadValidator.Validate(target, _validationContext);
            WorkloadProjectedState after = kind == WorkloadDecisionKind.Apply
                ? BuildLiveImpactState()
                : BuildPersistenceState();
            WorkloadProjectedState before = kind == WorkloadDecisionKind.Apply
                ? LiveBaselineState
                : TemplateBaselineState;
            WorkloadSemanticDiff diff = kind == WorkloadDecisionKind.Apply
                ? LiveDiff
                : TemplateDiff;
            return new WorkloadPreviewPlan(kind, SourceTemplate, before, after, diff, validation);
        }

        private WorkloadSessionDecision Rejected(
            WorkloadDecisionKind kind,
            WorkloadPreviewPlan plan,
            string reason)
        {
            return new WorkloadSessionDecision(
                kind,
                false,
                plan,
                NewSession(ProjectedState, WorkloadSessionStatus.Rejected),
                null,
                null,
                reason);
        }

        private WorkloadSession NewSession(WorkloadProjectedState projectedState, WorkloadSessionStatus status)
        {
            return new WorkloadSession(
                SourceTemplate,
                TemplateBaselineState,
                LiveBaselineState,
                projectedState ?? WorkloadProjectedState.Empty,
                status,
                _validationContext,
                SourceIdentity,
                HasCapturedLiveBaseline,
                RuntimeBaseline);
        }

        private WorkloadSession TerminalSession(WorkloadTemplate resultTemplate, WorkloadSessionStatus status)
        {
            WorkloadProjectedState cleanState = resultTemplate.ProjectedState;
            return new WorkloadSession(
                resultTemplate,
                cleanState,
                cleanState,
                cleanState,
                status,
                _validationContext,
                string.Empty,
                false,
                null);
        }

        private static WorkloadProjectedState NormalizeState(
            WorkloadTemplate template,
            WorkloadProjectedState state)
        {
            WorkloadProjectedState normalized = state ?? WorkloadProjectedState.Empty;
            WorkloadScope scope = template == null || template.Definition == null
                ? WorkloadScope.Empty
                : template.Definition.Scope ?? WorkloadScope.Empty;
            normalized = normalized.LimitToScope(scope);
            var excluded = new HashSet<PawnKey>();
            for (int i = 0; i < scope.ExcludedPawnIds.Count; i++) excluded.Add(scope.ExcludedPawnIds[i]);
            for (int i = 0; i < normalized.ExcludedPawnIds.Count; i++) excluded.Add(normalized.ExcludedPawnIds[i]);

            foreach (PawnKey pawn in excluded)
            {
                if (!normalized.IsExcluded(pawn)) normalized = normalized.ExcludePawn(pawn);
            }

            return normalized;
        }

        /// <summary>
        /// Builds the detached state that Update and Fork are allowed to write.
        /// A session-only exclusion restores that pawn's stored baseline rather
        /// than turning the temporary omission into a deletion. This keeps the
        /// apply scope and the persistence scope intentionally separate.
        /// </summary>
        private WorkloadProjectedState BuildPersistenceState()
        {
            return BuildStatePreservingSessionExclusions(TemplateBaselineState);
        }

        private WorkloadProjectedState BuildLiveImpactState()
        {
            return BuildStatePreservingSessionExclusions(LiveBaselineState);
        }

        private WorkloadProjectedState BuildStatePreservingSessionExclusions(
            WorkloadProjectedState exclusionBaseline)
        {
            IReadOnlyList<PawnKey> sessionExcluded = GetSessionOnlyExcludedPawnIds();
            if (sessionExcluded.Count == 0)
            {
                return ProjectedState;
            }

            WorkloadProjectedState safeBaseline =
                exclusionBaseline ?? WorkloadProjectedState.Empty;
            var draft = new WorkloadDraft(safeBaseline);

            for (int i = 0; i < safeBaseline.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = safeBaseline.ParentPriorities[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsParentPriority(ProjectedState.ParentPriorities, entry.Key))
                {
                    draft.RemoveParentPriority(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = ProjectedState.ParentPriorities[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetParentPriority(entry.Key, entry.Priority);
                }
            }

            for (int i = 0; i < safeBaseline.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = safeBaseline.ManualModes[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsManualMode(ProjectedState.ManualModes, entry.Key))
                {
                    draft.RemoveManualMode(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = ProjectedState.ManualModes[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetManualMode(entry.Key, entry.Manual);
                }
            }

            for (int i = 0; i < safeBaseline.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = safeBaseline.Schedules[i];
                if (!IsSessionExcluded(entry.Pawn, sessionExcluded) &&
                    !ContainsSchedule(ProjectedState.Schedules, entry.Pawn))
                {
                    draft.RemoveSchedule(entry.Pawn);
                }
            }

            for (int i = 0; i < ProjectedState.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = ProjectedState.Schedules[i];
                if (!IsSessionExcluded(entry.Pawn, sessionExcluded))
                {
                    draft.SetSchedule(entry.Pawn, entry.Schedule);
                }
            }

            for (int i = 0; i < safeBaseline.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = safeBaseline.SpecificJobOverrides[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsSpecificOverride(ProjectedState.SpecificJobOverrides, entry.Key))
                {
                    draft.RemoveSpecificJobOverride(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = ProjectedState.SpecificJobOverrides[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetSpecificJobOverride(entry.Key, entry.Value);
                }
            }

            for (int i = 0; i < safeBaseline.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = safeBaseline.SpecificJobOrder[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsSpecificOrder(ProjectedState.SpecificJobOrder, entry.Key))
                {
                    draft.RemoveSpecificJobOrder(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = ProjectedState.SpecificJobOrder[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetSpecificJobOrder(entry.Key, entry.Order);
                }
            }

            for (int i = 0; i < safeBaseline.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = safeBaseline.PresentationSettings[i];
                if (!ContainsPresentationSetting(ProjectedState.PresentationSettings, entry.Key))
                {
                    draft.RemovePresentationSetting(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = ProjectedState.PresentationSettings[i];
                draft.SetPresentationSetting(entry.Key, entry.Value);
            }

            // Preserve the typed contract alongside the legacy compatibility
            // lists. A session-only pawn exclusion must not erase a staged
            // local intent, while global intents must remain untouched.
            for (int i = 0; i < safeBaseline.ParentPriorityIntents.Count; i++)
            {
                WorkloadParentPriorityIntentEntry entry = safeBaseline.ParentPriorityIntents[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsParentPriorityIntent(ProjectedState.ParentPriorityIntents, entry.Key))
                {
                    draft.SetParentPriorityNoOpinion(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.ParentPriorityIntents.Count; i++)
            {
                WorkloadParentPriorityIntentEntry entry = ProjectedState.ParentPriorityIntents[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetParentPriorityIntent(entry.Key, entry.Intent);
                }
            }

            for (int i = 0; i < safeBaseline.ManualModeIntents.Count; i++)
            {
                WorkloadManualModeIntentEntry entry = safeBaseline.ManualModeIntents[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsManualModeIntent(ProjectedState.ManualModeIntents, entry.Key))
                {
                    draft.SetManualModeNoOpinion(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.ManualModeIntents.Count; i++)
            {
                WorkloadManualModeIntentEntry entry = ProjectedState.ManualModeIntents[i];
                if (!IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetManualModeIntent(entry.Key, entry.Intent);
                }
            }

            for (int i = 0; i < safeBaseline.ScheduleIntents.Count; i++)
            {
                WorkloadScheduleIntentEntry entry = safeBaseline.ScheduleIntents[i];
                if (!entry.Key.IsGlobal &&
                    !IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsScheduleIntent(ProjectedState.ScheduleIntents, entry.Key))
                {
                    draft.SetScheduleNoOpinion(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.ScheduleIntents.Count; i++)
            {
                WorkloadScheduleIntentEntry entry = ProjectedState.ScheduleIntents[i];
                if (entry.Key.IsGlobal || !IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetScheduleIntent(entry.Key, entry.Intent);
                }
            }

            for (int i = 0; i < safeBaseline.SpecificPriorityIntents.Count; i++)
            {
                WorkloadSpecificPriorityIntentEntry entry = safeBaseline.SpecificPriorityIntents[i];
                if (!entry.Key.IsGlobal &&
                    !IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsSpecificPriorityIntent(ProjectedState.SpecificPriorityIntents, entry.Key))
                {
                    draft.SetSpecificPriorityNoOpinion(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.SpecificPriorityIntents.Count; i++)
            {
                WorkloadSpecificPriorityIntentEntry entry = ProjectedState.SpecificPriorityIntents[i];
                if (entry.Key.IsGlobal || !IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetSpecificPriorityIntent(entry.Key, entry.Intent);
                }
            }

            for (int i = 0; i < safeBaseline.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadWorkTypeOrderIntentEntry entry = safeBaseline.WorkTypeOrderIntents[i];
                if (!entry.Key.IsGlobal &&
                    !IsSessionExcluded(entry.Key.Pawn, sessionExcluded) &&
                    !ContainsWorkTypeOrderIntent(ProjectedState.WorkTypeOrderIntents, entry.Key))
                {
                    draft.SetWorkTypeOrderNoOpinion(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadWorkTypeOrderIntentEntry entry = ProjectedState.WorkTypeOrderIntents[i];
                if (entry.Key.IsGlobal || !IsSessionExcluded(entry.Key.Pawn, sessionExcluded))
                {
                    draft.SetWorkTypeOrderIntent(entry.Key, entry.Intent);
                }
            }

            for (int i = 0; i < safeBaseline.PresentationSettingIntents.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry = safeBaseline.PresentationSettingIntents[i];
                if (!ContainsPresentationSettingIntent(ProjectedState.PresentationSettingIntents, entry.Key))
                {
                    draft.SetPresentationSettingNoOpinion(entry.Key);
                }
            }

            for (int i = 0; i < ProjectedState.PresentationSettingIntents.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry = ProjectedState.PresentationSettingIntents[i];
                draft.SetPresentationSettingIntent(entry.Key, entry.Intent);
            }

            return draft.ProjectedState;
        }

        private WorkloadOwnershipDimensions EffectiveOwnership
        {
            get
            {
                return WorkloadV2OwnershipResolver.Effective(
                    SourceTemplate,
                    ProjectedState,
                    TemplateBaselineState);
            }
        }

        private IReadOnlyList<WorkloadStateDimension> GetUnsupportedClearDimensions()
        {
            var dimensions = new List<WorkloadStateDimension>();
            WorkloadOwnershipDimensions ownership = EffectiveOwnership;

            if (ownership.Owns(WorkloadStateDimension.ParentPriorities) &&
                HasRemovedParentPriority())
            {
                dimensions.Add(WorkloadStateDimension.ParentPriorities);
            }

            if (ownership.Owns(WorkloadStateDimension.ManualModes) &&
                HasRemovedManualMode())
            {
                dimensions.Add(WorkloadStateDimension.ManualModes);
            }

            if (ownership.Owns(WorkloadStateDimension.Schedules) &&
                HasRemovedSchedule())
            {
                dimensions.Add(WorkloadStateDimension.Schedules);
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) &&
                HasRemovedSpecificJobOverride())
            {
                dimensions.Add(WorkloadStateDimension.SpecificJobOverrides);
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder) &&
                HasRemovedSpecificJobOrder())
            {
                dimensions.Add(WorkloadStateDimension.SpecificJobOrder);
            }

            if (ownership.Owns(WorkloadStateDimension.PresentationSettings) &&
                HasRemovedPresentationSetting())
            {
                dimensions.Add(WorkloadStateDimension.PresentationSettings);
            }

            return dimensions.AsReadOnly();
        }

        internal string GetUnsupportedClearMessage()
        {
            IReadOnlyList<WorkloadStateDimension> dimensions =
                GetUnsupportedClearDimensions();
            if (dimensions.Count == 0)
            {
                return string.Empty;
            }

            var labels = new List<string>();
            for (int i = 0; i < dimensions.Count; i++)
            {
                labels.Add(dimensions[i].ToString());
            }

            return "The preview cannot clear owned values yet (" +
                string.Join(", ", labels.ToArray()) +
                "). The staged state was not accepted because a clear would " +
                "otherwise fall through to live state.";
        }

        private bool HasRemovedParentPriority()
        {
            for (int i = 0; i < TemplateBaselineState.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = TemplateBaselineState.ParentPriorities[i];
                if (!ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsParentPriority(ProjectedState.ParentPriorities, entry.Key))
                {
                    if (!HasParentPriorityClear(ProjectedState, entry.Key)) return true;
                }
            }

            return HasRemovedParentPriorityIntent(TemplateBaselineState);
        }

        private bool HasRemovedManualMode()
        {
            for (int i = 0; i < TemplateBaselineState.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = TemplateBaselineState.ManualModes[i];
                if (!ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsManualMode(ProjectedState.ManualModes, entry.Key))
                {
                    if (!HasManualModeClear(ProjectedState, entry.Key)) return true;
                }
            }

            return HasRemovedManualModeIntent(TemplateBaselineState);
        }

        private bool HasRemovedSchedule()
        {
            for (int i = 0; i < TemplateBaselineState.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = TemplateBaselineState.Schedules[i];
                if (!ProjectedState.IsExcluded(entry.Pawn) &&
                    !ContainsSchedule(ProjectedState.Schedules, entry.Pawn))
                {
                    return true;
                }
            }

            return HasRemovedScheduleIntent(TemplateBaselineState) ||
                HasRemovedScheduleIntent(LiveBaselineState);
        }

        private bool HasRemovedSpecificJobOverride()
        {
            return HasRemovedSpecificJobOverride(TemplateBaselineState) ||
                HasRemovedSpecificJobOverride(LiveBaselineState);
        }

        private bool HasRemovedSpecificJobOverride(WorkloadProjectedState baseline)
        {
            for (int i = 0; i < baseline.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = baseline.SpecificJobOverrides[i];
                if (!ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsSpecificOverride(ProjectedState.SpecificJobOverrides, entry.Key))
                {
                    if (!HasSpecificPriorityClear(ProjectedState, entry.Key.ToTargetKey())) return true;
                }
            }

            return HasRemovedSpecificPriorityIntent(baseline);
        }

        private bool HasRemovedSpecificJobOrder()
        {
            return HasRemovedSpecificJobOrder(TemplateBaselineState) ||
                HasRemovedSpecificJobOrder(LiveBaselineState) ||
                HasRemovedWorkTypeOrderIntent(TemplateBaselineState) ||
                HasRemovedWorkTypeOrderIntent(LiveBaselineState);
        }

        private bool HasRemovedSpecificJobOrder(WorkloadProjectedState baseline)
        {
            for (int i = 0; i < baseline.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = baseline.SpecificJobOrder[i];
                if (!ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsSpecificOrder(ProjectedState.SpecificJobOrder, entry.Key))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedPresentationSetting()
        {
            for (int i = 0; i < TemplateBaselineState.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = TemplateBaselineState.PresentationSettings[i];
                if (!ContainsPresentationSetting(ProjectedState.PresentationSettings, entry.Key))
                {
                    if (!HasPresentationSettingClear(ProjectedState, entry.Key)) return true;
                }
            }

            return HasRemovedPresentationSettingIntent(TemplateBaselineState);
        }

        private bool HasRemovedParentPriorityIntent(WorkloadProjectedState baseline)
        {
            if (baseline == null) return false;
            for (int i = 0; i < baseline.ParentPriorityIntents.Count; i++)
            {
                WorkloadParentPriorityIntentEntry entry = baseline.ParentPriorityIntents[i];
                WorkloadParentPriorityIntentEntry projected;
                if (entry.Intent.State != WorkloadIntentState.NoOpinion &&
                    (!TryGetParentPriorityIntent(ProjectedState, entry.Key, out projected) ||
                     projected.Intent.State == WorkloadIntentState.NoOpinion))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedManualModeIntent(WorkloadProjectedState baseline)
        {
            if (baseline == null) return false;
            for (int i = 0; i < baseline.ManualModeIntents.Count; i++)
            {
                WorkloadManualModeIntentEntry entry = baseline.ManualModeIntents[i];
                WorkloadManualModeIntentEntry projected;
                if (entry.Intent.State != WorkloadIntentState.NoOpinion &&
                    (!TryGetManualModeIntent(ProjectedState, entry.Key, out projected) ||
                     projected.Intent.State == WorkloadIntentState.NoOpinion))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedScheduleIntent(WorkloadProjectedState baseline)
        {
            if (baseline == null) return false;
            for (int i = 0; i < baseline.ScheduleIntents.Count; i++)
            {
                WorkloadScheduleIntentEntry entry = baseline.ScheduleIntents[i];
                WorkloadScheduleIntentEntry projected;
                if (entry.Intent.State != WorkloadIntentState.NoOpinion &&
                    (!TryGetScheduleIntent(ProjectedState, entry.Key, out projected) ||
                     projected.Intent.State == WorkloadIntentState.NoOpinion))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedSpecificPriorityIntent(WorkloadProjectedState baseline)
        {
            if (baseline == null) return false;
            for (int i = 0; i < baseline.SpecificPriorityIntents.Count; i++)
            {
                WorkloadSpecificPriorityIntentEntry entry = baseline.SpecificPriorityIntents[i];
                WorkloadSpecificPriorityIntentEntry projected;
                if (entry.Intent.State != WorkloadIntentState.NoOpinion &&
                    (!TryGetSpecificPriorityIntent(ProjectedState, entry.Key, out projected) ||
                     projected.Intent.State == WorkloadIntentState.NoOpinion))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedWorkTypeOrderIntent(WorkloadProjectedState baseline)
        {
            if (baseline == null) return false;
            for (int i = 0; i < baseline.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadWorkTypeOrderIntentEntry entry = baseline.WorkTypeOrderIntents[i];
                WorkloadWorkTypeOrderIntentEntry projected;
                if (entry.Intent.State != WorkloadIntentState.NoOpinion &&
                    (!TryGetWorkTypeOrderIntent(ProjectedState, entry.Key, out projected) ||
                     projected.Intent.State == WorkloadIntentState.NoOpinion))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedPresentationSettingIntent(WorkloadProjectedState baseline)
        {
            if (baseline == null) return false;
            for (int i = 0; i < baseline.PresentationSettingIntents.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry = baseline.PresentationSettingIntents[i];
                WorkloadPresentationSettingIntentEntry projected;
                if (entry.Intent.State != WorkloadIntentState.NoOpinion &&
                    (!TryGetPresentationSettingIntent(ProjectedState, entry.Key, out projected) ||
                     projected.Intent.State == WorkloadIntentState.NoOpinion))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasParentPriorityClear(
            WorkloadProjectedState state,
            WorkloadParentPriorityKey key)
        {
            WorkloadParentPriorityIntentEntry entry;
            return TryGetParentPriorityIntent(state, key, out entry) &&
                entry.Intent.State == WorkloadIntentState.Clear;
        }

        private static bool HasManualModeClear(
            WorkloadProjectedState state,
            WorkloadParentPriorityKey key)
        {
            WorkloadManualModeIntentEntry entry;
            return TryGetManualModeIntent(state, key, out entry) &&
                entry.Intent.State == WorkloadIntentState.Clear;
        }

        private static bool HasSpecificPriorityClear(
            WorkloadProjectedState state,
            WorkloadSpecificJobTargetKey key)
        {
            WorkloadSpecificPriorityIntentEntry entry;
            return TryGetSpecificPriorityIntent(state, key, out entry) &&
                entry.Intent.State == WorkloadIntentState.Clear;
        }

        private static bool HasPresentationSettingClear(
            WorkloadProjectedState state,
            string key)
        {
            WorkloadPresentationSettingIntentEntry entry;
            return TryGetPresentationSettingIntent(state, key, out entry) &&
                entry.Intent.State == WorkloadIntentState.Clear;
        }

        /// <summary>
        /// Builds the runtime-only template used by Apply. It carries temporary
        /// exclusions in its scope so downstream writers can fail closed, but
        /// it is never sent to persistence.
        /// </summary>
        internal WorkloadTemplate BuildApplyTemplate()
        {
            WorkloadScope sourceScope = SourceTemplate.Definition.Scope ?? WorkloadScope.Empty;
            var excluded = new List<PawnKey>(sourceScope.ExcludedPawnIds);
            IReadOnlyList<PawnKey> sessionExcluded = GetSessionOnlyExcludedPawnIds();
            for (int i = 0; i < sessionExcluded.Count; i++)
            {
                if (!ContainsPawn(excluded, sessionExcluded[i])) excluded.Add(sessionExcluded[i]);
            }

            var scope = new WorkloadScope(
                sourceScope.Mode,
                sourceScope.ExplicitPawnIds,
                excluded);
            var definition = new WorkloadDefinition(
                SourceTemplate.StableId,
                SourceTemplate.Label,
                SourceTemplate.Definition.SchemaVersion,
                EffectiveOwnership,
                scope);
            return new WorkloadTemplate(definition, ProjectedState);
        }

        private WorkloadTemplate BuildTargetTemplate(
            WorkloadProjectedState state,
            string stableId,
            string label)
        {
            WorkloadScope sourceScope = SourceTemplate.Definition.Scope ?? WorkloadScope.Empty;
            var scope = new WorkloadScope(
                sourceScope.Mode,
                sourceScope.ExplicitPawnIds,
                sourceScope.ExcludedPawnIds);
            var definition = new WorkloadDefinition(
                stableId,
                label,
                SourceTemplate.Definition.SchemaVersion,
                WorkloadV2OwnershipResolver.Effective(
                    SourceTemplate,
                    state,
                    TemplateBaselineState),
                scope);
            return new WorkloadTemplate(definition, state);
        }

        private IReadOnlyList<PawnKey> GetSessionOnlyExcludedPawnIds()
        {
            WorkloadScope sourceScope = SourceTemplate.Definition.Scope ?? WorkloadScope.Empty;
            var savedExcluded = new HashSet<PawnKey>(sourceScope.ExcludedPawnIds);
            var result = new List<PawnKey>();
            for (int i = 0; i < ProjectedState.ExcludedPawnIds.Count; i++)
            {
                PawnKey pawn = ProjectedState.ExcludedPawnIds[i];
                if (!savedExcluded.Contains(pawn)) result.Add(pawn);
            }

            return result.AsReadOnly();
        }

        private static bool IsSessionExcluded(
            PawnKey pawn,
            IReadOnlyList<PawnKey> sessionExcluded)
        {
            return ContainsPawn(sessionExcluded, pawn);
        }

        private static bool ContainsPawn(IReadOnlyList<PawnKey> values, PawnKey value)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Equals(value)) return true;
            }

            return false;
        }

        private static bool ContainsParentPriority(
            IReadOnlyList<WorkloadParentPriorityEntry> values,
            WorkloadParentPriorityKey key)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Key.Equals(key)) return true;
            }

            return false;
        }

        private static bool ContainsManualMode(
            IReadOnlyList<WorkloadManualModeEntry> values,
            WorkloadParentPriorityKey key)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Key.Equals(key)) return true;
            }

            return false;
        }

        private static bool ContainsSchedule(
            IReadOnlyList<WorkloadScheduleEntry> values,
            PawnKey pawn)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Pawn.Equals(pawn)) return true;
            }

            return false;
        }

        private static bool ContainsSpecificOverride(
            IReadOnlyList<WorkloadSpecificJobOverrideEntry> values,
            WorkloadSpecificJobKey key)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Key.Equals(key)) return true;
            }

            return false;
        }

        private static bool ContainsSpecificOrder(
            IReadOnlyList<WorkloadSpecificJobOrderEntry> values,
            WorkloadSpecificJobKey key)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Key.Equals(key)) return true;
            }

            return false;
        }

        private static bool ContainsPresentationSetting(
            IReadOnlyList<WorkloadPresentationSettingEntry> values,
            string key)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(values[i].Key, key)) return true;
            }

            return false;
        }

        private static bool ContainsParentPriorityIntent(
            IReadOnlyList<WorkloadParentPriorityIntentEntry> values,
            WorkloadParentPriorityKey key)
        {
            WorkloadParentPriorityIntentEntry ignored;
            return TryGetParentPriorityIntent(values, key, out ignored);
        }

        private static bool ContainsManualModeIntent(
            IReadOnlyList<WorkloadManualModeIntentEntry> values,
            WorkloadParentPriorityKey key)
        {
            WorkloadManualModeIntentEntry ignored;
            return TryGetManualModeIntent(values, key, out ignored);
        }

        private static bool ContainsScheduleIntent(
            IReadOnlyList<WorkloadScheduleIntentEntry> values,
            WorkloadScheduleTargetKey key)
        {
            WorkloadScheduleIntentEntry ignored;
            return TryGetScheduleIntent(values, key, out ignored);
        }

        private static bool ContainsSpecificPriorityIntent(
            IReadOnlyList<WorkloadSpecificPriorityIntentEntry> values,
            WorkloadSpecificJobTargetKey key)
        {
            WorkloadSpecificPriorityIntentEntry ignored;
            return TryGetSpecificPriorityIntent(values, key, out ignored);
        }

        private static bool ContainsWorkTypeOrderIntent(
            IReadOnlyList<WorkloadWorkTypeOrderIntentEntry> values,
            WorkloadWorkTypeOrderKey key)
        {
            WorkloadWorkTypeOrderIntentEntry ignored;
            return TryGetWorkTypeOrderIntent(values, key, out ignored);
        }

        private static bool ContainsPresentationSettingIntent(
            IReadOnlyList<WorkloadPresentationSettingIntentEntry> values,
            string key)
        {
            WorkloadPresentationSettingIntentEntry ignored;
            return TryGetPresentationSettingIntent(values, key, out ignored);
        }

        private static bool TryGetParentPriorityIntent(
            WorkloadProjectedState state,
            WorkloadParentPriorityKey key,
            out WorkloadParentPriorityIntentEntry result)
        {
            return TryGetParentPriorityIntent(state?.ParentPriorityIntents, key, out result);
        }

        private static bool TryGetParentPriorityIntent(
            IReadOnlyList<WorkloadParentPriorityIntentEntry> values,
            WorkloadParentPriorityKey key,
            out WorkloadParentPriorityIntentEntry result)
        {
            for (int i = 0; values != null && i < values.Count; i++)
            {
                if (values[i].Key.Equals(key))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryGetManualModeIntent(
            WorkloadProjectedState state,
            WorkloadParentPriorityKey key,
            out WorkloadManualModeIntentEntry result)
        {
            return TryGetManualModeIntent(state?.ManualModeIntents, key, out result);
        }

        private static bool TryGetManualModeIntent(
            IReadOnlyList<WorkloadManualModeIntentEntry> values,
            WorkloadParentPriorityKey key,
            out WorkloadManualModeIntentEntry result)
        {
            for (int i = 0; values != null && i < values.Count; i++)
            {
                if (values[i].Key.Equals(key))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryGetScheduleIntent(
            WorkloadProjectedState state,
            WorkloadScheduleTargetKey key,
            out WorkloadScheduleIntentEntry result)
        {
            return TryGetScheduleIntent(state?.ScheduleIntents, key, out result);
        }

        private static bool TryGetScheduleIntent(
            IReadOnlyList<WorkloadScheduleIntentEntry> values,
            WorkloadScheduleTargetKey key,
            out WorkloadScheduleIntentEntry result)
        {
            for (int i = 0; values != null && i < values.Count; i++)
            {
                if (values[i].Key.Equals(key))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryGetSpecificPriorityIntent(
            WorkloadProjectedState state,
            WorkloadSpecificJobTargetKey key,
            out WorkloadSpecificPriorityIntentEntry result)
        {
            return TryGetSpecificPriorityIntent(state?.SpecificPriorityIntents, key, out result);
        }

        private static bool TryGetSpecificPriorityIntent(
            IReadOnlyList<WorkloadSpecificPriorityIntentEntry> values,
            WorkloadSpecificJobTargetKey key,
            out WorkloadSpecificPriorityIntentEntry result)
        {
            for (int i = 0; values != null && i < values.Count; i++)
            {
                if (values[i].Key.Equals(key))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryGetWorkTypeOrderIntent(
            WorkloadProjectedState state,
            WorkloadWorkTypeOrderKey key,
            out WorkloadWorkTypeOrderIntentEntry result)
        {
            return TryGetWorkTypeOrderIntent(state?.WorkTypeOrderIntents, key, out result);
        }

        private static bool TryGetWorkTypeOrderIntent(
            IReadOnlyList<WorkloadWorkTypeOrderIntentEntry> values,
            WorkloadWorkTypeOrderKey key,
            out WorkloadWorkTypeOrderIntentEntry result)
        {
            for (int i = 0; values != null && i < values.Count; i++)
            {
                if (values[i].Key.Equals(key))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryGetPresentationSettingIntent(
            WorkloadProjectedState state,
            string key,
            out WorkloadPresentationSettingIntentEntry result)
        {
            return TryGetPresentationSettingIntent(state?.PresentationSettingIntents, key, out result);
        }

        private static bool TryGetPresentationSettingIntent(
            IReadOnlyList<WorkloadPresentationSettingIntentEntry> values,
            string key,
            out WorkloadPresentationSettingIntentEntry result)
        {
            for (int i = 0; values != null && i < values.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(values[i].Key, key))
                {
                    result = values[i];
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool Contains(IReadOnlyList<PawnKey> values, PawnKey value)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Equals(value)) return true;
            }

            return false;
        }
    }
}
