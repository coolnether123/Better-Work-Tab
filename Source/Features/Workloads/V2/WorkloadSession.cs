using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Workloads.V2
{
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
            bool hasCapturedLiveBaseline)
        {
            SourceTemplate = sourceTemplate ?? WorkloadTemplate.Empty;
            TemplateBaselineState = templateBaselineState ?? WorkloadProjectedState.Empty;
            LiveBaselineState = liveBaselineState ?? TemplateBaselineState;
            ProjectedState = NormalizeState(SourceTemplate, projectedState ?? WorkloadProjectedState.Empty);
            Status = status;
            _validationContext = validationContext ?? WorkloadValidationContext.Default;
            SourceIdentity = sourceIdentity ?? string.Empty;
            HasCapturedLiveBaseline = hasCapturedLiveBaseline;
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
                false);
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
            WorkloadValidationContext validationContext = null)
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
                true);
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
                    SourceTemplate.Definition.OwnershipDimensions);
            }
        }

        public WorkloadSemanticDiff LiveDiff
        {
            get
            {
                return WorkloadSemanticDiff.Between(
                    LiveBaselineState,
                    BuildLiveImpactState(),
                    SourceTemplate.Definition.OwnershipDimensions);
            }
        }

        public bool IsDirty => !TemplateDiff.IsEmpty;

        /// <summary>
        /// The projected state format has no persisted tombstone representation
        /// yet. Removing an owned value would therefore make the renderer fall
        /// through to live state while Apply interprets the same removal as a
        /// write (for example Disabled for a parent priority). Such removals
        /// are rejected at the runtime boundary instead of being presented as
        /// a misleading staged edit.
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
                HasCapturedLiveBaseline);
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
                false);
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

            return draft.ProjectedState;
        }

        private IReadOnlyList<WorkloadStateDimension> GetUnsupportedClearDimensions()
        {
            var dimensions = new List<WorkloadStateDimension>();
            WorkloadOwnershipDimensions ownership =
                SourceTemplate.Definition.OwnershipDimensions;

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
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedManualMode()
        {
            for (int i = 0; i < TemplateBaselineState.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = TemplateBaselineState.ManualModes[i];
                if (!ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsManualMode(ProjectedState.ManualModes, entry.Key))
                {
                    return true;
                }
            }

            return false;
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

            return false;
        }

        private bool HasRemovedSpecificJobOverride()
        {
            for (int i = 0; i < TemplateBaselineState.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = TemplateBaselineState.SpecificJobOverrides[i];
                if (!ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsSpecificOverride(ProjectedState.SpecificJobOverrides, entry.Key))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRemovedSpecificJobOrder()
        {
            for (int i = 0; i < TemplateBaselineState.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = TemplateBaselineState.SpecificJobOrder[i];
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
                    return true;
                }
            }

            return false;
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
                SourceTemplate.Definition.OwnershipDimensions,
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
                SourceTemplate.Definition.OwnershipDimensions,
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
