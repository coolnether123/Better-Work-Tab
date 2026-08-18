using System;
using System.Collections.Generic;
using System.Globalization;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    public enum WorkloadV2CommitMessageKind
    {
        Changed = 0,
        Unchanged = 1,
        Skipped = 2,
        Excluded = 3,
        MissingOrStale = 4,
        Unsupported = 5,
        Fatal = 6
    }

    public sealed class WorkloadV2CommitMessage
    {
        internal WorkloadV2CommitMessage(
            WorkloadV2CommitMessageKind kind,
            string code,
            string subject,
            string message)
        {
            Kind = kind;
            Code = code ?? string.Empty;
            Subject = subject ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public WorkloadV2CommitMessageKind Kind { get; private set; }
        public string Code { get; private set; }
        public string Subject { get; private set; }
        public string Message { get; private set; }
    }

    /// <summary>
    /// Structured diagnostics for one V2 commit attempt. Runtime entries that
    /// cannot be applied are retained as explicit diagnostics instead of being
    /// counted as successful writes.
    /// </summary>
    public sealed class WorkloadV2CommitReport
    {
        private readonly List<WorkloadV2CommitMessage> _messages =
            new List<WorkloadV2CommitMessage>();
        private readonly List<WorkloadV2CommitMessage> _changed =
            new List<WorkloadV2CommitMessage>();
        private readonly List<WorkloadV2CommitMessage> _unchanged =
            new List<WorkloadV2CommitMessage>();
        private readonly List<WorkloadV2CommitMessage> _skipped =
            new List<WorkloadV2CommitMessage>();
        private readonly List<WorkloadV2CommitMessage> _excluded =
            new List<WorkloadV2CommitMessage>();
        private readonly List<WorkloadV2CommitMessage> _missingOrStale =
            new List<WorkloadV2CommitMessage>();
        private readonly List<WorkloadV2CommitMessage> _unsupported =
            new List<WorkloadV2CommitMessage>();
        private readonly List<WorkloadV2CommitMessage> _fatal =
            new List<WorkloadV2CommitMessage>();

        internal WorkloadV2CommitReport(
            WorkloadDecisionKind decisionKind,
            string sourceStableId,
            string targetStableId)
        {
            DecisionKind = decisionKind;
            SourceStableId = sourceStableId ?? string.Empty;
            TargetStableId = targetStableId ?? string.Empty;
        }

        public WorkloadDecisionKind DecisionKind { get; private set; }
        public string SourceStableId { get; private set; }
        public string TargetStableId { get; private set; }
        public bool LiveStateChanged { get; internal set; }
        public bool TemplatePersisted { get; internal set; }
        public bool IsSemanticNoOp { get; internal set; }
        public bool RollbackAttempted { get; internal set; }
        public bool LiveStateRestored { get; internal set; } = true;
        public bool TemplateRestored { get; internal set; } = true;
        public bool HasNetStateChange => LiveStateChanged || TemplatePersisted;
        public bool IsPartial => SkippedCount > 0 || ExcludedCount > 0;
        public bool RollbackSucceeded => !RollbackAttempted || (LiveStateRestored && TemplateRestored);
        public bool HasFatal => _fatal.Count > 0;

        public int ChangedCount => _changed.Count;
        public int UnchangedCount => _unchanged.Count;
        public int SkippedCount => _skipped.Count;
        public int ExcludedCount => _excluded.Count;
        public int MissingOrStaleCount => _missingOrStale.Count;
        public int MissingStaleCount => MissingOrStaleCount;
        public int UnsupportedCount => _unsupported.Count;
        public int FatalCount => _fatal.Count;

        public IReadOnlyList<WorkloadV2CommitMessage> Messages => _messages.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Changed => _changed.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Unchanged => _unchanged.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Skipped => _skipped.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Excluded => _excluded.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> MissingOrStale => _missingOrStale.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> MissingStale => MissingOrStale;
        public IReadOnlyList<WorkloadV2CommitMessage> Unsupported => _unsupported.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Fatal => _fatal.AsReadOnly();

        internal void Add(
            WorkloadV2CommitMessageKind kind,
            string code,
            string subject,
            string message)
        {
            for (int i = 0; i < _messages.Count; i++)
            {
                WorkloadV2CommitMessage existing = _messages[i];
                if (existing.Kind == kind &&
                    StringComparer.Ordinal.Equals(existing.Code, code ?? string.Empty) &&
                    StringComparer.Ordinal.Equals(existing.Subject, subject ?? string.Empty))
                {
                    return;
                }
            }

            var diagnostic = new WorkloadV2CommitMessage(kind, code, subject, message);
            _messages.Add(diagnostic);
            switch (kind)
            {
                case WorkloadV2CommitMessageKind.Changed:
                    _changed.Add(diagnostic);
                    break;
                case WorkloadV2CommitMessageKind.Unchanged:
                    _unchanged.Add(diagnostic);
                    break;
                case WorkloadV2CommitMessageKind.Skipped:
                    _skipped.Add(diagnostic);
                    break;
                case WorkloadV2CommitMessageKind.Excluded:
                    _excluded.Add(diagnostic);
                    break;
                case WorkloadV2CommitMessageKind.MissingOrStale:
                    _missingOrStale.Add(diagnostic);
                    break;
                case WorkloadV2CommitMessageKind.Unsupported:
                    _unsupported.Add(diagnostic);
                    break;
                case WorkloadV2CommitMessageKind.Fatal:
                    _fatal.Add(diagnostic);
                    break;
            }
        }
    }

    /// <summary>
    /// Result of a V2 commit. The report is present for both success and
    /// failure, so callers can distinguish nonfatal skips from a fail-closed
    /// authority, conflict, or persistence failure.
    /// </summary>
    public sealed class WorkloadV2CommitResult
    {
        internal WorkloadV2CommitResult(
            bool succeeded,
            WorkloadDiagnosticCode code,
            string message,
            WorkloadV2CommitReport report,
            WorkloadPreviewPlan plan,
            WorkloadTemplate resultTemplate,
            string stableId)
        {
            Succeeded = succeeded;
            Code = code;
            Message = message ?? string.Empty;
            Report = report;
            Plan = plan;
            ResultTemplate = resultTemplate;
            StableId = stableId ?? string.Empty;
        }

        public bool Succeeded { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public string Message { get; private set; }
        public WorkloadV2CommitReport Report { get; private set; }
        public WorkloadPreviewPlan Plan { get; private set; }
        public WorkloadTemplate ResultTemplate { get; private set; }
        public string StableId { get; private set; }
        public bool PreviewCleared { get; internal set; }
        public bool IsSemanticNoOp => Report?.IsSemanticNoOp == true;
        public bool LiveStateChanged => Report?.LiveStateChanged == true;
        public bool TemplatePersisted => Report?.TemplatePersisted == true;
        public bool HasNetStateChange => Report?.HasNetStateChange == true;
    }

    /// <summary>
    /// Modern workload persistence and preview boundary. Live writes are kept
    /// behind WorkloadV2ApplyService so preview operations remain pure.
    /// </summary>
    internal sealed class Workload2Backend
    {
        private readonly GameComponent_BWTWorldSettings _component;
        private readonly WorkloadV2ApplyService _applyService;
        private WorkloadSession _previewSession;

        internal Workload2Backend(GameComponent_BWTWorldSettings component)
        {
            _component = component;
            _applyService = new WorkloadV2ApplyService(component);
        }

        internal bool IsPreviewSessionActive
        {
            get { return _previewSession != null; }
        }

        internal WorkloadSession PreviewSession
        {
            get { return _previewSession; }
        }

        internal IReadOnlyList<WorkloadDescriptor> List()
        {
            var result = new List<WorkloadDescriptor>();
            WorkloadV2PersistenceEnvelope store = Store;
            if (store?.Records == null) return result;
            store.RefreshDiagnostics();

            for (int i = 0; i < store.Records.Count; i++)
            {
                WorkloadV2PersistenceRecord record = store.Records[i];
                if (record == null) continue;
                result.Add(ToDescriptor(
                    store,
                    record,
                    string.Equals(store.CurrentWorkloadId, record.StableId, StringComparison.Ordinal)));
            }

            return result;
        }

        internal WorkloadOperationResult<WorkloadDescriptor> Current()
        {
            WorkloadV2PersistenceEnvelope store = Store;
            if (store == null)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no Better Work Tab world component.");
            }

            store.RefreshDiagnostics();

            if (string.IsNullOrEmpty(store.CurrentWorkloadId))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.MissingCurrentWorkloadId,
                    "There is no current V2 workload ID.");
            }

            if (store.HasDuplicateStableId(store.CurrentWorkloadId))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId,
                    "The current V2 workload ID is duplicated.");
            }

            WorkloadV2PersistenceRecord record = store.Find(store.CurrentWorkloadId);
            return record == null
                ? WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId,
                    "The current V2 workload ID was not found.")
                : WorkloadOperationResult<WorkloadDescriptor>.Ok(ToDescriptor(store, record, true));
        }

        internal WorkloadOperationResult Select(string workloadId)
        {
            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded) return ready;

            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(workloadId);
            if (!found.Succeeded) return WorkloadOperationResult.Fail(found.Code, found.Message);

            WorkloadOperationResult<WorkloadTemplate> template =
                WorkloadV2RecordConverter.TryToTemplate(found.Value);
            if (!template.Succeeded)
            {
                return WorkloadOperationResult.Fail(template.Code, template.Message);
            }

            Store.CurrentWorkloadId = found.Value.StableId;
            NotifyChanged();
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult<WorkloadDescriptor> Create(string label)
        {
            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(ready.Code, ready.Message);
            }

            string finalLabel = string.IsNullOrWhiteSpace(label)
                ? GetDefaultLabel()
                : label;
            string stableId = Guid.NewGuid().ToString("N");
            WorkloadOperationResult<WorkloadTemplate> captured = CaptureCurrentTemplate(stableId, finalLabel);
            if (!captured.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(captured.Code, captured.Message);
            }

            return SaveTemplate(captured.Value, true);
        }

        internal WorkloadOperationResult<WorkloadDescriptor> SaveTemplate(
            WorkloadTemplate template,
            bool makeCurrent)
        {
            if (_previewSession != null)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The active V2 preview must be finished or cancelled before writing a template directly.");
            }

            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(ready.Code, ready.Message);
            }

            WorkloadValidationResult validation = WorkloadValidator.Validate(template);
            if (validation.HasErrors || validation.IsNewerSchema)
            {
                WorkloadValidationIssue issue = validation.Issues.Count > 0
                    ? validation.Issues[0]
                    : null;
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    validation.IsNewerSchema
                        ? WorkloadDiagnosticCode.NewerSchema
                        : WorkloadDiagnosticCode.InvalidState,
                    issue?.Message ?? "The V2 template failed validation and was not written.");
            }

            if (HasUnsupportedDirectSaveState(template))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "Schedules and presentation settings remain fail-closed because this backend has no safe live writer for them.");
            }

            WorkloadOperationResult<WorkloadV2PersistenceRecord> converted =
                WorkloadV2RecordConverter.TryFromTemplate(template);
            if (!converted.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(converted.Code, converted.Message);
            }

            if (Store.HasDuplicateStableId(converted.Value.StableId))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId,
                    "The workload stable ID is already duplicated in the store.");
            }

            WorkloadV2PersistenceRecord existing = Store.Find(converted.Value.StableId);
            if (existing != null)
            {
                WorkloadOperationResult<WorkloadTemplate> existingTemplate =
                    WorkloadV2RecordConverter.TryToTemplate(existing, Store.IsReadOnlyDiagnostic);
                if (!existingTemplate.Succeeded)
                {
                    return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                        existingTemplate.Code,
                        existingTemplate.Message);
                }

                int index = Store.Records.IndexOf(existing);
                Store.Records[index] = converted.Value;
            }
            else
            {
                Store.Records.Add(converted.Value);
            }

            if (makeCurrent)
            {
                Store.CurrentWorkloadId = converted.Value.StableId;
            }

            NotifyChanged();
            return WorkloadOperationResult<WorkloadDescriptor>.Ok(
                ToDescriptor(Store, converted.Value, makeCurrent));
        }

        private static bool HasUnsupportedDirectSaveState(WorkloadTemplate template)
        {
            if (template?.Definition == null) return true;
            WorkloadProjectedState state = template.ProjectedState ?? WorkloadProjectedState.Empty;
            return (template.Definition.OwnershipDimensions.Owns(WorkloadStateDimension.Schedules) &&
                    state.Schedules.Count > 0) ||
                   (template.Definition.OwnershipDimensions.Owns(WorkloadStateDimension.PresentationSettings) &&
                    state.PresentationSettings.Count > 0);
        }

        internal WorkloadOperationResult Rename(string workloadId, string newLabel)
        {
            if (string.IsNullOrWhiteSpace(newLabel))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidLabel,
                    "A workload label is required.");
            }

            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded) return ready;
            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(workloadId);
            if (!found.Succeeded) return WorkloadOperationResult.Fail(found.Code, found.Message);

            WorkloadOperationResult<WorkloadTemplate> template =
                WorkloadV2RecordConverter.TryToTemplate(found.Value);
            if (!template.Succeeded) return WorkloadOperationResult.Fail(template.Code, template.Message);

            found.Value.Label = newLabel;
            NotifyChanged();
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult Delete(string workloadId)
        {
            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded) return ready;
            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(workloadId);
            if (!found.Succeeded) return WorkloadOperationResult.Fail(found.Code, found.Message);

            WorkloadOperationResult<WorkloadTemplate> template =
                WorkloadV2RecordConverter.TryToTemplate(found.Value, Store.IsReadOnlyDiagnostic);
            if (!template.Succeeded)
            {
                return WorkloadOperationResult.Fail(template.Code, template.Message);
            }

            Store.Records.Remove(found.Value);
            if (string.Equals(Store.CurrentWorkloadId, workloadId, StringComparison.Ordinal))
            {
                Store.CurrentWorkloadId = FindFirstStableId();
            }

            NotifyChanged();
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult Apply(string workloadId = null)
        {
            if (_previewSession == null)
            {
                WorkloadOperationResult<WorkloadSession> opened = BeginPreview(workloadId);
                if (!opened.Succeeded)
                {
                    return WorkloadOperationResult.Fail(opened.Code, opened.Message);
                }
            }
            else if (!string.IsNullOrEmpty(workloadId) &&
                     !string.Equals(_previewSession.SourceTemplate.StableId, workloadId, StringComparison.Ordinal))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The active V2 preview belongs to a different workload stable ID.");
            }

            WorkloadV2CommitResult result = CommitPreview(WorkloadDecisionKind.Apply, null, null);
            return result.Succeeded
                ? WorkloadOperationResult.Ok(result.Message)
                : WorkloadOperationResult.Fail(result.Code, result.Message);
        }

        internal WorkloadOperationResult<WorkloadTemplate> CaptureCurrentTemplate(
            string stableId,
            string label)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.MissingStableId,
                    "A stable workload ID is required for capture.");
            }

            if (Verse.Find.CurrentMap == null)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.NoCurrentMap,
                    "A current map is required to capture a workload.");
            }

            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out _))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.ExternalPriorityAuthority,
                    "BWT cannot capture priority state while an external priority authority is active.");
            }

            if (Verse.Find.PlaySettings == null)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "Global manual-priority state is unavailable for workload capture.");
            }

            var priorities = new List<WorkloadParentPriorityEntry>();
            var manualModes = new List<WorkloadManualModeEntry>();
            var specificOverrides = new List<WorkloadSpecificJobOverrideEntry>();
            var specificOrder = new List<WorkloadSpecificJobOrderEntry>();
            bool manualMode = Verse.Find.PlaySettings.useWorkPriorities;
            List<Pawn> pawns = Verse.Find.CurrentMap.mapPawns?.FreeColonists;
            List<WorkTypeDef> workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            if (pawns != null && workTypes != null)
            {
                for (int pawnIndex = 0; pawnIndex < pawns.Count; pawnIndex++)
                {
                    Pawn pawn = pawns[pawnIndex];
                    if (pawn?.workSettings == null) continue;
                    string pawnId = pawn.thingIDNumber.ToString(CultureInfo.InvariantCulture);
                    for (int workIndex = 0; workIndex < workTypes.Count; workIndex++)
                    {
                        WorkTypeDef workType = workTypes[workIndex];
                        if (workType == null || string.IsNullOrEmpty(workType.defName)) continue;
                        var parentKey = new WorkloadParentPriorityKey(
                            new PawnKey(pawnId),
                            new WorkTypeKey(workType.defName));

                        // Read BWT's stored/base dimension only. This is a read
                        // through the authority broker and never mutates live state.
                        int priority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                            pawn.workSettings,
                            workType);
                        priorities.Add(new WorkloadParentPriorityEntry(
                            parentKey,
                            priority));
                        manualModes.Add(new WorkloadManualModeEntry(parentKey, manualMode));

                        IReadOnlyList<WorkGiver> workGivers =
                            WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType, pawn);
                        for (int workGiverIndex = 0;
                             workGiverIndex < workGivers.Count;
                             workGiverIndex++)
                        {
                            WorkGiverDef workGiver = workGivers[workGiverIndex]?.def;
                            if (workGiver == null || string.IsNullOrEmpty(workGiver.defName)) continue;

                            if (WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                                    pawn,
                                    workGiver,
                                    out int specificPriority))
                            {
                                specificOverrides.Add(new WorkloadSpecificJobOverrideEntry(
                                    new WorkloadSpecificJobKey(
                                        new PawnKey(pawnId),
                                        new WorkTypeKey(workType.defName),
                                        new WorkGiverKey(workGiver.defName)),
                                    WorkloadScalarValue.FromInteger(specificPriority)));
                            }
                        }

                        if (WorkGiverReassignmentManager.HasPawnOrdering(pawn, workType))
                        {
                            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot snapshot =
                                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                                    pawn,
                                    workType);
                            for (int orderIndex = 0;
                                 orderIndex < snapshot.OrderedWorkGiverNames.Count;
                                 orderIndex++)
                            {
                                string workGiverName = snapshot.OrderedWorkGiverNames[orderIndex];
                                if (string.IsNullOrEmpty(workGiverName)) continue;
                                specificOrder.Add(new WorkloadSpecificJobOrderEntry(
                                    new WorkloadSpecificJobKey(
                                        new PawnKey(pawnId),
                                        new WorkTypeKey(workType.defName),
                                        new WorkGiverKey(workGiverName)),
                                    orderIndex));
                            }
                        }
                    }
                }
            }

            WorkloadOwnershipDimensions ownership =
                WorkloadOwnershipDimensions.ParentPriorities |
                WorkloadOwnershipDimensions.ManualModes |
                WorkloadOwnershipDimensions.SpecificJobOverrides |
                WorkloadOwnershipDimensions.SpecificJobOrder;

            var definition = new WorkloadDefinition(
                stableId,
                string.IsNullOrWhiteSpace(label) ? GetDefaultLabel() : label,
                WorkloadSchema.CurrentVersion,
                ownership,
                WorkloadScope.CurrentMapFreeColonists());
            return WorkloadOperationResult<WorkloadTemplate>.Ok(
                new WorkloadTemplate(
                    definition,
                    new WorkloadProjectedState(
                        parentPriorities: priorities,
                        manualModes: manualModes,
                        specificJobOverrides: specificOverrides,
                        specificJobOrder: specificOrder)));
        }

        internal WorkloadOperationResult<WorkloadTemplate> GetTemplate(string workloadId = null)
        {
            string id = workloadId;
            if (string.IsNullOrEmpty(id))
            {
                WorkloadOperationResult<WorkloadDescriptor> current = Current();
                if (!current.Succeeded)
                {
                    return WorkloadOperationResult<WorkloadTemplate>.Fail(current.Code, current.Message);
                }

                id = current.Value.StableId;
            }

            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(id);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(found.Code, found.Message);
            }

            return WorkloadV2RecordConverter.TryToTemplate(found.Value, Store.IsReadOnlyDiagnostic);
        }

        internal WorkloadOperationResult<WorkloadSession> BeginPreview(string workloadId = null)
        {
            if (_previewSession != null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "A V2 preview is already active.");
            }

            WorkloadOperationResult<WorkloadTemplate> template = GetTemplate(workloadId);
            if (!template.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(template.Code, template.Message);
            }

            WorkloadOperationResult<WorkloadLiveBaselineCapture> liveCapture =
                _applyService.CaptureLiveBaselineCapture(template.Value);
            if (!liveCapture.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    liveCapture.Code,
                    liveCapture.Message);
            }

            WorkloadSession opened = WorkloadSession.OpenCaptured(
                template.Value,
                liveCapture.Value.State,
                WorkloadSession.GetSourceIdentity(template.Value),
                runtimeBaseline: liveCapture.Value.RuntimeBaseline);
            if (opened == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview could not establish an explicit live baseline and source identity.");
            }

            _previewSession = opened;
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
        }

        internal WorkloadOperationResult<WorkloadSession> SetPreviewSession(WorkloadSession session)
        {
            if (session == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "Use explicit V2 cancel to close the active preview.");
            }

            if (session.IsTerminal ||
                session.Status == WorkloadSessionStatus.Rejected ||
                !session.HasCapturedLiveBaseline ||
                string.IsNullOrWhiteSpace(session.SourceIdentity) ||
                !StringComparer.Ordinal.Equals(
                    session.SourceIdentity,
                    WorkloadSession.GetSourceIdentity(session.SourceTemplate)))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview session is not backed by an explicit live baseline and source identity.");
            }

            if (_previewSession?.RuntimeBaseline != null &&
                (session.RuntimeBaseline == null ||
                 !session.RuntimeBaseline.Preserves(_previewSession.RuntimeBaseline)))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The incoming V2 preview did not preserve the previously captured runtime baseline.");
            }

            WorkloadOwnershipDimensions ownership =
                session.SourceTemplate.Definition.OwnershipDimensions;
            bool requiresPawnRuntimeBaseline =
                ownership.Owns(WorkloadStateDimension.ParentPriorities) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);
            if (requiresPawnRuntimeBaseline && _previewSession != null)
            {
                IReadOnlyList<PawnKey> represented =
                    session.ProjectedState.RepresentedPawnIds;
                for (int i = 0; i < represented.Count; i++)
                {
                    PawnKey pawn = represented[i];
                    if (!ContainsPawnKey(
                            _previewSession.ProjectedState.RepresentedPawnIds,
                            pawn) &&
                        !session.RuntimeBaseline.ContainsPawn(pawn))
                    {
                        return WorkloadOperationResult<WorkloadSession>.Fail(
                            WorkloadDiagnosticCode.InvalidState,
                            "The incoming V2 preview added a pawn without extending its captured runtime baseline: " + pawn + ".");
                    }
                }
            }

            WorkloadOperationResult<WorkloadTemplate> source =
                GetTemplate(session.SourceTemplate.StableId);
            if (!source.Succeeded ||
                !StringComparer.Ordinal.Equals(
                    WorkloadSession.GetSourceIdentity(source.Value),
                    session.SourceIdentity))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview source no longer matches the stored workload template.");
            }

            _previewSession = session;
            return WorkloadOperationResult<WorkloadSession>.Ok(session);
        }

        internal WorkloadOperationResult<WorkloadSession> EditPreview(Action<WorkloadDraft> edit)
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            WorkloadSession candidate = _previewSession.Edit(edit);
            if (candidate.HasUnsupportedClears)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    candidate.GetUnsupportedClearMessage());
            }

            WorkloadOperationResult<WorkloadSession> extended =
                ExtendRuntimeBaselineForNewPawns(_previewSession, candidate);
            if (!extended.Succeeded)
            {
                return extended;
            }

            _previewSession = extended.Value;
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
        }

        private WorkloadOperationResult<WorkloadSession> ExtendRuntimeBaselineForNewPawns(
            WorkloadSession previous,
            WorkloadSession candidate)
        {
            var added = new List<PawnKey>();
            IReadOnlyList<PawnKey> previousPawns =
                previous?.ProjectedState?.RepresentedPawnIds ?? new PawnKey[0];
            IReadOnlyList<PawnKey> candidatePawns =
                candidate?.ProjectedState?.RepresentedPawnIds ?? new PawnKey[0];
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                PawnKey pawn = candidatePawns[i];
                bool wasPreviouslyCaptured =
                    ContainsPawnKey(previousPawns, pawn) ||
                    ContainsPawnKey(previous.LiveBaselineState.RepresentedPawnIds, pawn) ||
                    ContainsPawnKey(previous.TemplateBaselineState.RepresentedPawnIds, pawn) ||
                    (previous.RuntimeBaseline?.ContainsPawn(pawn) ?? false);
                if (!wasPreviouslyCaptured)
                {
                    added.Add(pawn);
                }
            }

            if (added.Count == 0)
            {
                return WorkloadOperationResult<WorkloadSession>.Ok(candidate);
            }

            if (previous?.RuntimeBaseline == null || !previous.HasCapturedLiveBaseline)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "A newly included pawn cannot be accepted because the preview has no captured runtime baseline to extend.");
            }

            WorkloadOperationResult<WorkloadLiveBaselineCapture> capture =
                _applyService.CaptureLiveBaselineCapture(candidate.TargetTemplate);
            if (!capture.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    capture.Code,
                    "The newly included pawn runtime baseline could not be captured: " + capture.Message);
            }

            WorkloadRuntimeBaseline extension = capture.Value.RuntimeBaseline;
            WorkloadRuntimeBaseline existing = previous.RuntimeBaseline;
            if (existing.HasManualMode && extension.HasManualMode &&
                existing.ManualMode != extension.ManualMode)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The global manual-priority mode changed while the new pawn baseline was being extended.");
            }

            WorkloadOwnershipDimensions ownership =
                candidate.SourceTemplate.Definition.OwnershipDimensions;
            bool ownsSpecific =
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);
            if (ownsSpecific &&
                (!existing.HasSpecificJobRevision ||
                 !extension.HasSpecificJobRevision ||
                 existing.SpecificJobRevision != extension.SpecificJobRevision))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "BWT specific-job state changed while the new pawn baseline was being extended.");
            }

            bool requiresPawnRuntimeBaseline =
                ownership.Owns(WorkloadStateDimension.ParentPriorities) ||
                ownsSpecific;
            if (requiresPawnRuntimeBaseline)
            {
                for (int i = 0; i < added.Count; i++)
                {
                    if (!extension.ContainsPawn(added[i]))
                    {
                        return WorkloadOperationResult<WorkloadSession>.Fail(
                            WorkloadDiagnosticCode.InvalidState,
                            "The newly included pawn has no complete captured runtime baseline: " + added[i] + ".");
                    }
                }
            }

            WorkloadRuntimeBaseline merged = existing.ExtendForPawns(extension, added);
            if (merged == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The newly included pawn baseline was captured under a different manual-mode or specific-job revision.");
            }

            return WorkloadOperationResult<WorkloadSession>.Ok(
                candidate.ExtendCapturedBaseline(
                    added,
                    candidate.ProjectedState,
                    merged));
        }

        private static bool ContainsPawnKey(
            IReadOnlyList<PawnKey> values,
            PawnKey key)
        {
            PawnKey safeKey = key ?? new PawnKey(null);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Equals(safeKey)) return true;
            }

            return false;
        }

        internal WorkloadOperationResult<WorkloadSession> SetPreviewState(WorkloadProjectedState projectedState)
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            WorkloadSession candidate = _previewSession.EditState(projectedState);
            if (candidate.HasUnsupportedClears)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    candidate.GetUnsupportedClearMessage());
            }

            _previewSession = candidate;
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
        }

        internal WorkloadOperationResult<WorkloadSession> RevertPreview()
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            _previewSession = _previewSession.Revert();
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
        }

        internal WorkloadOperationResult<WorkloadPreviewPlan> PreviewPlan(WorkloadDecisionKind decisionKind)
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            switch (decisionKind)
            {
                case WorkloadDecisionKind.Apply:
                    return WorkloadOperationResult<WorkloadPreviewPlan>.Ok(_previewSession.Preview());
                case WorkloadDecisionKind.Update:
                    return WorkloadOperationResult<WorkloadPreviewPlan>.Ok(_previewSession.Update().Plan);
                case WorkloadDecisionKind.Fork:
                    return WorkloadOperationResult<WorkloadPreviewPlan>.Ok(
                        _previewSession.Fork(
                            Guid.NewGuid().ToString("N"),
                            _previewSession.SourceTemplate.Label).Plan);
                default:
                    return WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                        WorkloadDiagnosticCode.UnsupportedOperation,
                        "The requested V2 preview decision is not supported.");
            }
        }

        internal WorkloadOperationResult<WorkloadSemanticDiff> PreviewDiff()
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            return WorkloadOperationResult<WorkloadSemanticDiff>.Ok(_previewSession.SemanticDiff);
        }

        internal WorkloadOperationResult<WorkloadSemanticDiff> PreviewImpactDiff()
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            return WorkloadOperationResult<WorkloadSemanticDiff>.Ok(_previewSession.LiveDiff);
        }

        internal WorkloadV2CommitResult CommitApply()
        {
            return CommitPreview(WorkloadDecisionKind.Apply, null, null);
        }

        internal WorkloadV2CommitResult CommitUpdate()
        {
            return CommitPreview(WorkloadDecisionKind.Update, null, null);
        }

        internal WorkloadV2CommitResult CommitFork(string stableId, string label)
        {
            return CommitPreview(WorkloadDecisionKind.Fork, stableId, label);
        }

        internal WorkloadOperationResult EndPreview()
        {
            _previewSession = null;
            return WorkloadOperationResult.Ok();
        }

        private WorkloadV2CommitResult CommitPreview(
            WorkloadDecisionKind decisionKind,
            string forkStableId,
            string forkLabel)
        {
            if (_previewSession == null)
            {
                return WorkloadV2ApplyService.MissingPreview(decisionKind);
            }

            WorkloadV2CommitResult result = _applyService.Commit(
                _previewSession,
                decisionKind,
                forkStableId,
                forkLabel);
            if (result.Succeeded)
            {
                _previewSession = null;
                result.PreviewCleared = true;
            }

            return result;
        }

        private WorkloadOperationResult<WorkloadV2PersistenceRecord> Find(string workloadId)
        {
            if (string.IsNullOrEmpty(workloadId))
            {
                return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.MissingStableId,
                    "A V2 workload stable ID is required.");
            }

            WorkloadV2PersistenceEnvelope store = Store;
            if (store == null)
            {
                return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no Better Work Tab world component.");
            }

            if (store.HasDuplicateStableId(workloadId))
            {
                return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId,
                    "The V2 workload stable ID is duplicated.");
            }

            WorkloadV2PersistenceRecord record = store.Find(workloadId);
            return record == null
                ? WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId,
                    "The V2 workload stable ID was not found.")
                : WorkloadOperationResult<WorkloadV2PersistenceRecord>.Ok(record);
        }

        private WorkloadOperationResult RequireMutableStore()
        {
            if (_component == null)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no Better Work Tab world component.");
            }

            WorkloadV2PersistenceEnvelope store = Store;
            store.RefreshDiagnostics();
            if (store.IsReadOnlyDiagnostic)
            {
                return WorkloadOperationResult.Fail(
                    store.DiagnosticCode == WorkloadDiagnosticCode.None
                        ? WorkloadDiagnosticCode.ReadOnlyDiagnostic
                        : store.DiagnosticCode,
                    string.IsNullOrEmpty(store.Diagnostic)
                        ? "The V2 workload store is read-only for diagnostics."
                        : store.Diagnostic);
            }

            return WorkloadOperationResult.Ok();
        }

        private WorkloadV2PersistenceEnvelope Store
        {
            get { return _component?.EnsureWorkloadV2Persistence(); }
        }

        private void NotifyChanged()
        {
            _component?.NotifyWorkloadV2Changed();
        }

        private string FindFirstStableId()
        {
            WorkloadV2PersistenceRecord first = null;
            if (Store?.Records != null)
            {
                for (int i = 0; i < Store.Records.Count; i++)
                {
                    WorkloadV2PersistenceRecord candidate = Store.Records[i];
                    if (candidate == null || string.IsNullOrEmpty(candidate.StableId)) continue;
                    if (first == null || StringComparer.Ordinal.Compare(candidate.StableId, first.StableId) < 0)
                    {
                        first = candidate;
                    }
                }
            }

            return first?.StableId ?? string.Empty;
        }

        private string GetDefaultLabel()
        {
            int index = 1;
            while (true)
            {
                string candidate = "Workload " + index.ToString(CultureInfo.InvariantCulture);
                bool used = false;
                if (Store?.Records != null)
                {
                    for (int i = 0; i < Store.Records.Count; i++)
                    {
                        if (string.Equals(Store.Records[i]?.Label, candidate, StringComparison.Ordinal))
                        {
                            used = true;
                            break;
                        }
                    }
                }

                if (!used) return candidate;
                index++;
            }
        }

        private static WorkloadDescriptor ToDescriptor(
            WorkloadV2PersistenceEnvelope store,
            WorkloadV2PersistenceRecord record,
            bool isCurrent)
        {
            bool newerRecord = record.SchemaVersion > WorkloadSchema.CurrentVersion;
            bool readOnly = store.IsReadOnlyDiagnostic || newerRecord;
            WorkloadDiagnosticCode code = store.IsReadOnlyDiagnostic
                ? store.DiagnosticCode
                : newerRecord ? WorkloadDiagnosticCode.NewerSchema : WorkloadDiagnosticCode.None;
            string diagnostic = store.IsReadOnlyDiagnostic
                ? store.Diagnostic
                : newerRecord ? "The workload record uses a newer schema." : string.Empty;

            return new WorkloadDescriptor(
                WorkloadBackendMode.Modern,
                record.StableId,
                record.Label,
                isCurrent,
                readOnly,
                !string.IsNullOrWhiteSpace(record.StableId),
                record.SchemaVersion,
                code,
                diagnostic);
        }
    }

    /// <summary>
    /// Transaction boundary for the modern workload runtime. Everything before
    /// ApplyLive is read-only or staged in detached persistence records. The
    /// stored source template is never used as a write target for Apply.
    /// </summary>
    internal sealed class WorkloadLiveBaselineCapture
    {
        internal WorkloadLiveBaselineCapture(
            WorkloadProjectedState state,
            WorkloadRuntimeBaseline runtimeBaseline)
        {
            State = state ?? WorkloadProjectedState.Empty;
            RuntimeBaseline = runtimeBaseline;
        }

        internal WorkloadProjectedState State { get; private set; }
        internal WorkloadRuntimeBaseline RuntimeBaseline { get; private set; }
    }

    internal sealed class WorkloadV2ApplyService
    {
        private readonly GameComponent_BWTWorldSettings _component;

        internal WorkloadV2ApplyService(GameComponent_BWTWorldSettings component)
        {
            _component = component;
        }

        internal WorkloadOperationResult<WorkloadProjectedState> CaptureLiveBaseline(
            WorkloadTemplate template)
        {
            WorkloadOperationResult<WorkloadLiveBaselineCapture> capture =
                CaptureLiveBaselineCapture(template);
            return capture.Succeeded
                ? WorkloadOperationResult<WorkloadProjectedState>.Ok(capture.Value.State)
                : WorkloadOperationResult<WorkloadProjectedState>.Fail(capture.Code, capture.Message);
        }

        internal WorkloadOperationResult<WorkloadLiveBaselineCapture> CaptureLiveBaselineCapture(
            WorkloadTemplate template)
        {
            if (template == null)
            {
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 live baseline cannot be captured without a template.");
            }

            WorkloadProjectedState templateState =
                template.ProjectedState ?? WorkloadProjectedState.Empty;
            WorkloadOwnershipDimensions ownership =
                template.Definition.OwnershipDimensions;
            bool captureParentPriorities =
                ownership.Owns(WorkloadStateDimension.ParentPriorities);
            bool captureManualModes =
                ownership.Owns(WorkloadStateDimension.ManualModes);
            bool captureSpecificOverrides =
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides);
            bool captureSpecificOrder =
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);

            if (!captureParentPriorities &&
                !captureManualModes &&
                !captureSpecificOverrides &&
                !captureSpecificOrder)
            {
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Ok(
                    new WorkloadLiveBaselineCapture(
                        templateState,
                        new WorkloadRuntimeBaseline()));
            }

            var report = new WorkloadV2CommitReport(
                WorkloadDecisionKind.Apply,
                template.StableId,
                template.StableId);
            try
            {
                RuntimeContext runtime = BuildRuntimeContext(template, report);
                WorkloadScope scope = template.Definition.Scope ?? WorkloadScope.Empty;
                // A baseline is only useful if the same BWT-owned values can
                // later be written through the authority boundary.
                if (captureParentPriorities ||
                    captureSpecificOverrides ||
                    captureSpecificOrder)
                {
                    EnsureBetterWorkTabAuthority(report);
                }

                bool liveManualMode = false;
                if (captureManualModes)
                {
                    if (Verse.Find.PlaySettings == null)
                    {
                        Abort(
                            WorkloadDiagnosticCode.NoCurrentGame,
                            "Global manual-priority state is unavailable while opening the V2 preview.");
                    }

                    liveManualMode = Verse.Find.PlaySettings.useWorkPriorities;
                }

                var runtimePriorities = new List<WorkloadParentPriorityEntry>();
                var runtimeSpecificOverrides = new List<WorkloadSpecificJobRuntimeBaseline>();
                var runtimeSpecificOrders = new List<WorkloadSpecificJobOrderRuntimeBaseline>();
                var livePriorities = new List<WorkloadParentPriorityEntry>();
                var liveManualModes = new List<WorkloadManualModeEntry>();
                var liveSpecificOverrides = new List<WorkloadSpecificJobOverrideEntry>();
                var liveSpecificOrder = new List<WorkloadSpecificJobOrderEntry>();

                foreach (Pawn pawn in runtime.Pawns.Values)
                {
                    if (pawn == null || scope.IsExplicitlyExcluded(WorkTabEffectiveStateIds.ForPawn(pawn)) ||
                        !IsInScope(scope, pawn, runtime))
                    {
                        continue;
                    }

                    List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                    if (allWorkTypes == null) continue;
                    for (int workTypeIndex = 0; workTypeIndex < allWorkTypes.Count; workTypeIndex++)
                    {
                        WorkTypeDef workType = allWorkTypes[workTypeIndex];
                        if (workType == null || string.IsNullOrEmpty(workType.defName)) continue;
                        WorkloadParentPriorityKey parentKey = new WorkloadParentPriorityKey(
                            WorkTabEffectiveStateIds.ForPawn(pawn),
                            new WorkTypeKey(workType.defName));

                        if (captureParentPriorities && pawn.workSettings != null)
                        {
                            int priority;
                            try
                            {
                                priority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                                    pawn.workSettings,
                                    workType);
                            }
                            catch (Exception exception)
                            {
                                Abort(
                                    WorkloadDiagnosticCode.InvalidState,
                                    "The live parent-priority baseline could not be read safely: " + exception.Message);
                                priority = 0;
                            }

                            runtimePriorities.Add(new WorkloadParentPriorityEntry(parentKey, priority));
                        }

                        if (captureSpecificOrder)
                        {
                            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot orderSnapshot =
                                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                                    pawn,
                                    workType);
                            runtimeSpecificOrders.Add(new WorkloadSpecificJobOrderRuntimeBaseline(
                                parentKey,
                                orderSnapshot.HasStoredOrder,
                                orderSnapshot.OrderedWorkGiverNames));
                        }

                        if (!captureSpecificOverrides && !captureSpecificOrder) continue;
                        IReadOnlyList<WorkGiver> workGivers =
                            WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(
                                workType,
                                pawn);
                        for (int workGiverIndex = 0;
                             workGiverIndex < workGivers.Count;
                             workGiverIndex++)
                        {
                            WorkGiverDef workGiver = workGivers[workGiverIndex]?.def;
                            if (workGiver == null || string.IsNullOrEmpty(workGiver.defName)) continue;
                            WorkloadSpecificJobKey key = new WorkloadSpecificJobKey(
                                parentKey.Pawn,
                                parentKey.WorkType,
                                new WorkGiverKey(workGiver.defName));
                            bool hasOverride = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                                pawn,
                                workGiver,
                                out int specificPriority);
                            if (captureSpecificOverrides)
                            {
                                runtimeSpecificOverrides.Add(
                                    new WorkloadSpecificJobRuntimeBaseline(
                                        key,
                                        hasOverride,
                                        specificPriority));
                            }
                        }
                    }
                }

                for (int i = 0; i < templateState.ParentPriorities.Count; i++)
                {
                    WorkloadParentPriorityEntry entry = templateState.ParentPriorities[i];
                    if (!TryResolveLiveEntry(
                            entry.Key,
                            runtime,
                            out Pawn pawn,
                            out WorkTypeDef workType,
                            out string failure))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live baseline for " + entry.Key +
                            " could not be captured safely: " + failure);
                    }

                    if (pawn.workSettings == null)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live baseline for " + entry.Key +
                            " could not be captured safely because pawn work settings are unavailable.");
                    }

                    int priority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
                    livePriorities.Add(new WorkloadParentPriorityEntry(entry.Key, priority));
                }

                for (int i = 0; i < templateState.ManualModes.Count; i++)
                {
                    WorkloadManualModeEntry entry = templateState.ManualModes[i];
                    if (!TryResolveLiveEntry(
                            entry.Key,
                            runtime,
                            out Pawn unusedPawn,
                            out WorkTypeDef unusedWorkType,
                            out string failure))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live baseline for " + entry.Key +
                            " could not be captured safely: " + failure);
                    }

                    liveManualModes.Add(new WorkloadManualModeEntry(entry.Key, liveManualMode));
                }

                for (int i = 0; i < templateState.SpecificJobOverrides.Count; i++)
                {
                    WorkloadSpecificJobOverrideEntry entry = templateState.SpecificJobOverrides[i];
                    if (!TryResolveSpecificLiveEntry(
                            entry.Key,
                            runtime,
                            out Pawn pawn,
                            out WorkTypeDef workType,
                            out WorkGiverDef workGiver,
                            out string failure))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live baseline for " + entry.Key +
                            " could not be captured safely: " + failure);
                    }

                    if (WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                            pawn,
                            workGiver,
                            out int specificPriority))
                    {
                        liveSpecificOverrides.Add(new WorkloadSpecificJobOverrideEntry(
                            entry.Key,
                            WorkloadScalarValue.FromInteger(specificPriority)));
                    }
                }

                for (int i = 0; i < templateState.SpecificJobOrder.Count; i++)
                {
                    WorkloadSpecificJobOrderEntry entry = templateState.SpecificJobOrder[i];
                    if (!TryResolveSpecificLiveEntry(
                            entry.Key,
                            runtime,
                            out Pawn pawn,
                            out WorkTypeDef workType,
                            out WorkGiverDef workGiver,
                            out string failure))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live baseline for " + entry.Key +
                            " could not be captured safely: " + failure);
                    }

                    if (!TryGetEffectiveWorkGiverOrder(
                            pawn,
                            workType,
                            workGiver.defName,
                            out int order))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live baseline for " + entry.Key +
                            " could not resolve its current work-giver order.");
                    }

                    liveSpecificOrder.Add(new WorkloadSpecificJobOrderEntry(entry.Key, order));
                }

                var runtimeBaseline = new WorkloadRuntimeBaseline(
                    runtimePriorities,
                    captureManualModes,
                    liveManualMode,
                    runtimeSpecificOverrides,
                    runtimeSpecificOrders,
                    captureSpecificOverrides || captureSpecificOrder,
                    WorkGiverReassignmentManager.CurrentSyncVersion);
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Ok(
                    new WorkloadLiveBaselineCapture(
                        WithLiveBaselineValues(
                            templateState,
                            captureParentPriorities
                                ? livePriorities
                                : templateState.ParentPriorities,
                            captureManualModes
                                ? liveManualModes
                                : templateState.ManualModes,
                            captureSpecificOverrides
                                ? liveSpecificOverrides
                                : templateState.SpecificJobOverrides,
                            captureSpecificOrder
                                ? liveSpecificOrder
                                : templateState.SpecificJobOrder),
                        runtimeBaseline));
            }
            catch (CommitAbortException exception)
            {
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Fail(
                    exception.Code,
                    exception.Message);
            }
            catch (Exception exception)
            {
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 live baseline could not be captured safely: " + exception.Message);
            }
        }

        private static bool TryResolveLiveEntry(
            WorkloadParentPriorityKey key,
            RuntimeContext runtime,
            out Pawn pawn,
            out WorkTypeDef workType,
            out string failure)
        {
            pawn = null;
            workType = null;
            failure = string.Empty;
            if (key == null || !key.IsValid)
            {
                failure = "the stable pawn/work-type key is invalid";
                return false;
            }

            if (runtime == null || !runtime.Pawns.TryGetValue(key.Pawn.Value, out pawn))
            {
                failure = "the stable pawn ID is missing from the runtime";
                return false;
            }

            if (!runtime.WorkTypes.TryGetValue(key.WorkType.Value, out workType))
            {
                failure = "the stable WorkTypeDef ID is missing from the runtime";
                return false;
            }

            if (!WorkTabEffectiveStateIds.ForPawn(pawn).Equals(key.Pawn) ||
                !WorkTabEffectiveStateIds.ForWorkType(workType).Equals(key.WorkType))
            {
                failure = "the runtime identity did not match the stable key";
                return false;
            }

            return true;
        }

        private static bool TryResolveSpecificLiveEntry(
            WorkloadSpecificJobKey key,
            RuntimeContext runtime,
            out Pawn pawn,
            out WorkTypeDef workType,
            out WorkGiverDef workGiver,
            out string failure)
        {
            pawn = null;
            workType = null;
            workGiver = null;
            failure = string.Empty;
            if (key == null || !key.IsValid)
            {
                failure = "the stable pawn/work-type/work-giver key is invalid";
                return false;
            }

            if (!TryResolveLiveEntry(
                    new WorkloadParentPriorityKey(key.Pawn, key.WorkType),
                    runtime,
                    out pawn,
                    out workType,
                    out failure))
            {
                return false;
            }

            if (runtime == null || !runtime.WorkGivers.TryGetValue(key.WorkGiver.Value, out workGiver))
            {
                failure = "the stable WorkGiverDef ID is missing from the runtime";
                return false;
            }

            if (WorkGiverReassignmentManager.GetTargetWorkType(workGiver) != workType)
            {
                failure = "the WorkGiverDef no longer belongs to the stable WorkTypeDef";
                return false;
            }

            if (!TryGetEffectiveWorkGiverOrder(
                    pawn,
                    workType,
                    workGiver.defName,
                    out int unusedOrder))
            {
                failure = "the WorkGiverDef is not currently available in the stable WorkTypeDef";
                return false;
            }

            return true;
        }

        private static bool TryGetEffectiveWorkGiverOrder(
            Pawn pawn,
            WorkTypeDef workType,
            string workGiverDefName,
            out int order)
        {
            order = -1;
            if (workType == null || string.IsNullOrEmpty(workGiverDefName)) return false;
            IReadOnlyList<WorkGiver> workGivers =
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType, pawn);
            for (int i = 0; i < workGivers.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(workGivers[i]?.def?.defName, workGiverDefName))
                {
                    order = i;
                    return true;
                }
            }

            return false;
        }

        private static WorkloadProjectedState WithLiveBaselineValues(
            WorkloadProjectedState templateState,
            IReadOnlyList<WorkloadParentPriorityEntry> parentPriorities,
            IReadOnlyList<WorkloadManualModeEntry> manualModes,
            IReadOnlyList<WorkloadSpecificJobOverrideEntry> specificJobOverrides,
            IReadOnlyList<WorkloadSpecificJobOrderEntry> specificJobOrder)
        {
            var priorities = new Dictionary<WorkloadParentPriorityKey, int>();
            for (int i = 0; i < parentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = parentPriorities[i];
                priorities[entry.Key] = entry.Priority;
            }

            var manual = new Dictionary<WorkloadParentPriorityKey, bool>();
            for (int i = 0; i < manualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = manualModes[i];
                manual[entry.Key] = entry.Manual;
            }

            var schedules = new Dictionary<PawnKey, ScheduleKey>();
            for (int i = 0; i < templateState.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = templateState.Schedules[i];
                schedules[entry.Pawn] = entry.Schedule;
            }

            var overrides = new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
            for (int i = 0; i < specificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = specificJobOverrides[i];
                overrides[entry.Key] = entry.Value;
            }

            var order = new Dictionary<WorkloadSpecificJobKey, int>();
            for (int i = 0; i < specificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = specificJobOrder[i];
                order[entry.Key] = entry.Order;
            }

            var presentation = new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            for (int i = 0; i < templateState.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = templateState.PresentationSettings[i];
                presentation[entry.Key] = entry.Value;
            }

            return WorkloadProjectedState.FromMaps(
                priorities,
                manual,
                schedules,
                overrides,
                order,
                presentation,
                templateState.RepresentedPawnIds,
                templateState.ExcludedPawnIds,
                templateState.CopyExcludedStagedStates());
        }

        internal static WorkloadV2CommitResult MissingPreview(WorkloadDecisionKind decisionKind)
        {
            return Failure(
                decisionKind,
                WorkloadDiagnosticCode.NotFound,
                "preview.missing",
                "There is no active V2 preview session to commit.");
        }

        internal static WorkloadV2CommitResult GatewayFailure(
            WorkloadDecisionKind decisionKind,
            WorkloadDiagnosticCode code,
            string subject,
            string message)
        {
            return Failure(decisionKind, code, subject, message);
        }

        private static WorkloadV2CommitResult Failure(
            WorkloadDecisionKind decisionKind,
            WorkloadDiagnosticCode code,
            string subject,
            string message)
        {
            var report = new WorkloadV2CommitReport(decisionKind, string.Empty, string.Empty);
            report.Add(
                WorkloadV2CommitMessageKind.Fatal,
                subject,
                string.Empty,
                message);
            return new WorkloadV2CommitResult(
                false,
                code,
                message,
                report,
                null,
                null,
                string.Empty);
        }

        internal WorkloadV2CommitResult Commit(
            WorkloadSession session,
            WorkloadDecisionKind decisionKind,
            string forkStableId,
            string forkLabel)
        {
            string sourceStableId = session?.SourceTemplate?.StableId ?? string.Empty;
            string targetStableId = decisionKind == WorkloadDecisionKind.Fork
                ? (string.IsNullOrWhiteSpace(forkStableId)
                    ? Guid.NewGuid().ToString("N")
                    : forkStableId)
                : sourceStableId;
            var report = new WorkloadV2CommitReport(
                decisionKind,
                sourceStableId,
                targetStableId);
            WorkloadPreviewPlan plan = null;
            WorkloadTemplate targetTemplate = null;
            WorkloadV2PersistenceEnvelope store = null;
            PersistenceMutation persistence = null;
            LiveMutationTransaction live = null;

            try
            {
                if (_component == null)
                {
                    Abort(
                        WorkloadDiagnosticCode.NoCurrentGame,
                        "There is no Better Work Tab world component.");
                }

                if (session == null)
                {
                    Abort(
                        WorkloadDiagnosticCode.NotFound,
                        "The V2 preview session is missing.");
                }

                if (!session.HasCapturedLiveBaseline ||
                    string.IsNullOrWhiteSpace(session.SourceIdentity) ||
                    !StringComparer.Ordinal.Equals(
                        session.SourceIdentity,
                        WorkloadSession.GetSourceIdentity(session.SourceTemplate)))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 preview session is missing its explicit live baseline/source identity.");
                }

                if (session.HasUnsupportedClears)
                {
                    Abort(
                        WorkloadDiagnosticCode.UnsupportedOperation,
                        session.GetUnsupportedClearMessage());
                }

                if (decisionKind != WorkloadDecisionKind.Apply &&
                    decisionKind != WorkloadDecisionKind.Update &&
                    decisionKind != WorkloadDecisionKind.Fork)
                {
                    Abort(
                        WorkloadDiagnosticCode.UnsupportedOperation,
                        "The requested V2 commit decision is not supported.");
                }

                if (decisionKind == WorkloadDecisionKind.Fork &&
                    string.Equals(targetStableId, sourceStableId, StringComparison.Ordinal))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "A V2 fork must use a different stable ID.");
                }

                WorkloadSessionDecision decision = BuildDecision(
                    session,
                    decisionKind,
                    targetStableId,
                    forkLabel);
                plan = decision.Plan;
                if (plan == null || !decision.Accepted || !plan.CanProceed)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        decision?.RejectionReason ??
                        "The V2 preview did not pass model validation.");
                }

                targetTemplate = decisionKind == WorkloadDecisionKind.Apply
                    ? session.BuildApplyTemplate()
                    : decision.ResultTemplate;
                RequireValidTemplate(targetTemplate, report);

                if (decisionKind != WorkloadDecisionKind.Apply &&
                    session.SessionExcludedPawnIds.Count > 0)
                {
                    for (int i = 0; i < session.SessionExcludedPawnIds.Count; i++)
                    {
                        PawnKey pawn = session.SessionExcludedPawnIds[i];
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "scope.session-excluded",
                            pawn.Value,
                            "This temporary application exclusion was not written to the workload template.");
                    }
                }

                store = _component.EnsureWorkloadV2Persistence();
                if (store == null)
                {
                    Abort(
                        WorkloadDiagnosticCode.NoCurrentGame,
                        "The Better Work Tab V2 workload store is unavailable.");
                }

                store.RefreshDiagnostics();
                if (store.IsReadOnlyDiagnostic)
                {
                    Abort(
                        store.DiagnosticCode == WorkloadDiagnosticCode.None
                            ? WorkloadDiagnosticCode.ReadOnlyDiagnostic
                            : store.DiagnosticCode,
                        string.IsNullOrEmpty(store.Diagnostic)
                            ? "The V2 workload store is read-only for diagnostics."
                            : store.Diagnostic);
                }

                WorkloadV2PersistenceRecord currentRecord = FindUniqueRecord(
                    store,
                    sourceStableId,
                    report);
                WorkloadOperationResult<WorkloadTemplate> currentTemplateResult =
                    WorkloadV2RecordConverter.TryToTemplate(currentRecord);
                if (!currentTemplateResult.Succeeded)
                {
                    Abort(currentTemplateResult.Code, currentTemplateResult.Message);
                }

                WorkloadTemplate currentTemplate = currentTemplateResult.Value;
                if (!TemplatesMatchPreviewSource(currentTemplate, session.SourceTemplate))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The stored V2 template or fingerprint changed while the preview was open.");
                }

                bool unsupportedState = AddUnsupportedDimensionDiagnostics(
                    targetTemplate.Definition.OwnershipDimensions,
                    plan.Diff,
                    targetTemplate.ProjectedState,
                    report);
                if (unsupportedState)
                {
                    Abort(
                        WorkloadDiagnosticCode.UnsupportedOperation,
                        "The workload owns a V2 dimension with no supported live writer; " +
                        "the commit was blocked so an unappliable state cannot be persisted.");
                }

                WorkloadScope scope = targetTemplate.Definition.Scope ?? WorkloadScope.Empty;
                ValidateManualModeConsistency(targetTemplate, scope, report);
                var runtimePlan = new RuntimeCommitPlan();
                if (decisionKind == WorkloadDecisionKind.Apply)
                {
                    RuntimeContext runtime = BuildRuntimeContext(targetTemplate, report);
                    ValidateScopeEntries(scope, runtime, report);
                    if (report.MissingOrStaleCount > 0)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 apply was blocked because a nonexcluded scope entry is stale or missing.");
                    }

                    ValidateRuntimeEntries(
                        session,
                        targetTemplate,
                        plan,
                        scope,
                        runtime,
                        report);

                    if (report.MissingOrStaleCount > 0)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 apply was blocked during preflight because a nonexcluded entry is stale or missing.");
                    }

                    runtimePlan = PrepareRuntimeChanges(
                        session,
                        targetTemplate,
                        plan,
                        scope,
                        runtime,
                        report);

                    if (runtimePlan.RequiresPriorityAuthority)
                    {
                        EnsureBetterWorkTabAuthority(report);
                    }

                    RevalidateRuntimeBaselines(
                        session,
                        runtimePlan,
                        targetTemplate,
                        report,
                        "preflight");
                }

                if (decisionKind == WorkloadDecisionKind.Update && !plan.Diff.IsEmpty)
                {
                    WorkloadOperationResult<WorkloadV2PersistenceRecord> converted =
                        WorkloadV2RecordConverter.TryFromTemplate(targetTemplate);
                    if (!converted.Succeeded)
                    {
                        Abort(converted.Code, converted.Message);
                    }

                    EnsureUpdateTarget(store, sourceStableId, report);
                    persistence = new PersistenceMutation(
                        PersistenceMutationKind.Replace,
                        sourceStableId,
                        converted.Value);
                }
                else if (decisionKind == WorkloadDecisionKind.Fork)
                {
                    WorkloadOperationResult<WorkloadV2PersistenceRecord> converted =
                        WorkloadV2RecordConverter.TryFromTemplate(targetTemplate);
                    if (!converted.Succeeded)
                    {
                        Abort(converted.Code, converted.Message);
                    }

                    EnsureForkTargetIsNew(store, targetStableId, report);
                    persistence = new PersistenceMutation(
                        PersistenceMutationKind.Add,
                        targetStableId,
                        converted.Value);
                }

                if (runtimePlan.HasLiveMutations)
                {
                    // This is intentionally repeated after all validation and
                    // persistence staging, immediately before the first writer.
                    if (runtimePlan.RequiresPriorityAuthority)
                    {
                        EnsureBetterWorkTabAuthority(report);
                    }

                    live = new LiveMutationTransaction(runtimePlan);
                    ApplyLive(live, runtimePlan, targetTemplate, session, report);
                    report.LiveStateChanged = live.HasChanges;
                }

                if (runtimePlan.HasLiveMutations && persistence != null)
                {
                    // Do not let a live commit cross into template persistence after either
                    // BWT-owned runtime revision has changed.
                    if (runtimePlan.RequiresPriorityAuthority)
                    {
                        EnsureRuntimeMutationAuthority(runtimePlan, report, "persistence.priority");
                    }

                    if (runtimePlan.RequiresSpecificJobRevision)
                    {
                        EnsureSpecificJobRevision(runtimePlan, report, "persistence.specific-job");
                    }
                }

                if (persistence != null)
                {
                    try
                    {
                        ApplyPersistence(store, persistence);
                        report.TemplatePersisted = true;
                        report.Add(
                            WorkloadV2CommitMessageKind.Changed,
                            persistence.Kind == PersistenceMutationKind.Replace
                                ? "template.updated"
                                : "template.forked",
                            persistence.StableId,
                            persistence.Kind == PersistenceMutationKind.Replace
                                ? "The projected V2 template replaced the same stable ID."
                                : "The projected V2 template was written under a new stable ID.");
                    }
                    catch (Exception exception)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 workload persistence write failed: " + exception.Message);
                    }
                }

                if (report.MissingOrStaleCount > 0)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 commit was not reported as successful because stale entries were observed.");
                }

                if (runtimePlan.RequiresSpecificJobRevision)
                {
                    EnsureSpecificJobRevision(runtimePlan, report, "commit.final");
                }

                if (report.LiveStateChanged || report.TemplatePersisted)
                {
                    NotifyCommitChanged(plan, live);
                }

                report.IsSemanticNoOp = plan.Diff.IsEmpty;
                string temporaryScopeNote = decisionKind != WorkloadDecisionKind.Apply &&
                    session.SessionExcludedPawnIds.Count > 0
                    ? " Temporary pawn exclusions were not saved."
                    : string.Empty;
                string successMessage = decisionKind == WorkloadDecisionKind.Apply
                    ? (report.SkippedCount > 0
                        ? "The V2 workload was applied with explicit skipped entries."
                        : "The V2 workload was applied.")
                    : decisionKind == WorkloadDecisionKind.Update
                        ? (plan.Diff.IsEmpty
                            ? "The V2 workload update was a semantic no-op."
                            : "The V2 workload template was updated.") + temporaryScopeNote
                        : "The V2 workload template was forked." + temporaryScopeNote;
                return new WorkloadV2CommitResult(
                    true,
                    WorkloadDiagnosticCode.None,
                    successMessage,
                    report,
                    plan,
                    targetTemplate,
                    targetStableId);
            }
            catch (CommitAbortException exception)
            {
                bool persistenceRestored = RollbackPersistence(store, persistence, report);
                RollbackLive(live, report);
                bool liveNetChanged = HasLiveNetChanges(live);
                report.RollbackAttempted =
                    (persistence != null && persistence.WasApplied) ||
                    (live != null && live.RollbackAttempted);
                report.TemplateRestored = persistenceRestored;
                report.LiveStateRestored = !liveNetChanged;
                report.TemplatePersisted = persistence != null && persistence.WasApplied && !persistenceRestored;
                report.LiveStateChanged = liveNetChanged;
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    exception.Code.ToString(),
                    targetStableId,
                    exception.Message);
                return new WorkloadV2CommitResult(
                    false,
                    exception.Code,
                    exception.Message,
                    report,
                    plan,
                    null,
                    targetStableId);
            }
            catch (Exception exception)
            {
                bool persistenceRestored = RollbackPersistence(store, persistence, report);
                RollbackLive(live, report);
                bool liveNetChanged = HasLiveNetChanges(live);
                report.RollbackAttempted =
                    (persistence != null && persistence.WasApplied) ||
                    (live != null && live.RollbackAttempted);
                report.TemplateRestored = persistenceRestored;
                report.LiveStateRestored = !liveNetChanged;
                report.TemplatePersisted = persistence != null && persistence.WasApplied && !persistenceRestored;
                report.LiveStateChanged = liveNetChanged;
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "runtime.exception",
                    targetStableId,
                    "The V2 commit failed safely: " + exception.Message);
                return new WorkloadV2CommitResult(
                    false,
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 commit failed safely: " + exception.Message,
                    report,
                    plan,
                    null,
                    targetStableId);
            }
        }

        private static WorkloadSessionDecision BuildDecision(
            WorkloadSession session,
            WorkloadDecisionKind decisionKind,
            string targetStableId,
            string forkLabel)
        {
            switch (decisionKind)
            {
                case WorkloadDecisionKind.Apply:
                    return session.Apply();
                case WorkloadDecisionKind.Update:
                    return session.Update();
                case WorkloadDecisionKind.Fork:
                    return session.Fork(targetStableId, forkLabel);
                default:
                    return null;
            }
        }

        private static void RequireValidTemplate(
            WorkloadTemplate template,
            WorkloadV2CommitReport report)
        {
            WorkloadValidationResult validation = WorkloadValidator.Validate(template);
            if (!validation.HasErrors && !validation.IsNewerSchema)
            {
                return;
            }

            if (validation.Issues != null)
            {
                for (int i = 0; i < validation.Issues.Count; i++)
                {
                    WorkloadValidationIssue issue = validation.Issues[i];
                    report.Add(
                        WorkloadV2CommitMessageKind.Fatal,
                        issue.Code.ToString(),
                        issue.Path,
                        issue.Message);
                }
            }

            Abort(
                validation.IsNewerSchema
                    ? WorkloadDiagnosticCode.NewerSchema
                    : WorkloadDiagnosticCode.InvalidState,
                "The projected V2 workload failed validation.");
        }

        private static WorkloadV2PersistenceRecord FindUniqueRecord(
            WorkloadV2PersistenceEnvelope store,
            string stableId,
            WorkloadV2CommitReport report)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                Abort(
                    WorkloadDiagnosticCode.MissingStableId,
                    "A V2 workload stable ID is required for commit.");
            }

            if (store.HasDuplicateStableId(stableId))
            {
                Abort(
                    WorkloadDiagnosticCode.AmbiguousStableId,
                    "The V2 workload stable ID is duplicated in the store.");
            }

            WorkloadV2PersistenceRecord record = store.Find(stableId);
            if (record == null)
            {
                Abort(
                    WorkloadDiagnosticCode.UnknownWorkloadId,
                    "The V2 workload source stable ID is no longer present.");
            }

            return record;
        }

        private static bool TemplatesMatchPreviewSource(
            WorkloadTemplate current,
            WorkloadTemplate source)
        {
            if (current == null || source == null)
            {
                return false;
            }

            if (!StringComparer.Ordinal.Equals(
                    current.SemanticFingerprint,
                    source.SemanticFingerprint))
            {
                return false;
            }

            return StringComparer.Ordinal.Equals(
                GetTemplateFingerprint(current),
                GetTemplateFingerprint(source));
        }

        private static string GetTemplateFingerprint(WorkloadTemplate template)
        {
            if (template == null)
            {
                return string.Empty;
            }

            string definition = template.Definition?.CanonicalForm ?? string.Empty;
            string state = template.ProjectedState?.CanonicalForm ?? string.Empty;
            return WorkloadCanonical.Fingerprint(
                WorkloadCanonical.Encode(definition) + WorkloadCanonical.Encode(state));
        }

        private static RuntimeContext BuildRuntimeContext(
            WorkloadTemplate targetTemplate,
            WorkloadV2CommitReport report)
        {
            WorkloadScope scope = targetTemplate.Definition.Scope ?? WorkloadScope.Empty;
            if (!scope.IsValidMode)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidScopeMode,
                    "The V2 workload scope mode is not supported by this runtime.");
            }

            if (scope.Mode == WorkloadScopeMode.CurrentMapFreeColonists &&
                Verse.Find.CurrentMap == null)
            {
                Abort(
                    WorkloadDiagnosticCode.NoCurrentMap,
                    "The V2 workload scope requires a current map.");
            }

            var pawns = new Dictionary<string, Pawn>(StringComparer.Ordinal);
            List<Pawn> availablePawns = PawnsFinder.All_AliveOrDead;
            if (availablePawns != null)
            {
                for (int i = 0; i < availablePawns.Count; i++)
                {
                    Pawn pawn = availablePawns[i];
                    if (pawn == null) continue;
                    string pawnId = pawn.thingIDNumber.ToString(CultureInfo.InvariantCulture);
                    Pawn existing;
                    if (pawns.TryGetValue(pawnId, out existing) && !ReferenceEquals(existing, pawn))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The runtime contains duplicate pawn identity " + pawnId + ".");
                    }

                    pawns[pawnId] = pawn;
                }
            }

            var workTypes = new Dictionary<string, WorkTypeDef>(StringComparer.Ordinal);
            List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            if (allWorkTypes != null)
            {
                for (int i = 0; i < allWorkTypes.Count; i++)
                {
                    WorkTypeDef workType = allWorkTypes[i];
                    if (workType == null || string.IsNullOrWhiteSpace(workType.defName)) continue;
                    WorkTypeDef existing;
                    if (workTypes.TryGetValue(workType.defName, out existing))
                    {
                        if (!ReferenceEquals(existing, workType))
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The runtime contains duplicate WorkTypeDef identity " + workType.defName + ".");
                        }
                    }
                    else
                    {
                        workTypes.Add(workType.defName, workType);
                    }
                }
            }

            var workGivers = new Dictionary<string, WorkGiverDef>(StringComparer.Ordinal);
            List<WorkGiverDef> allWorkGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            if (allWorkGivers != null)
            {
                for (int i = 0; i < allWorkGivers.Count; i++)
                {
                    WorkGiverDef workGiver = allWorkGivers[i];
                    if (workGiver == null || string.IsNullOrWhiteSpace(workGiver.defName)) continue;
                    WorkGiverDef existing;
                    if (workGivers.TryGetValue(workGiver.defName, out existing))
                    {
                        if (!ReferenceEquals(existing, workGiver))
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The runtime contains duplicate WorkGiverDef identity " + workGiver.defName + ".");
                        }
                    }
                    else
                    {
                        workGivers.Add(workGiver.defName, workGiver);
                    }
                }
            }

            return new RuntimeContext(pawns, workTypes, workGivers);
        }

        private static void ValidateScopeEntries(
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            for (int i = 0; i < scope.ExplicitPawnIds.Count; i++)
            {
                PawnKey pawnKey = scope.ExplicitPawnIds[i];
                if (scope.IsExplicitlyExcluded(pawnKey))
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Excluded,
                        "scope.excluded",
                        pawnKey.Value,
                        "The explicitly scoped pawn is excluded from this workload.");
                    continue;
                }

                if (!runtime.Pawns.ContainsKey(pawnKey.Value))
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.MissingOrStale,
                        "scope.pawn.missing",
                        pawnKey.Value,
                        "The explicitly scoped pawn is stale or missing at commit time.");
                }
            }
        }

        private static bool AddUnsupportedDimensionDiagnostics(
            WorkloadOwnershipDimensions ownership,
            WorkloadSemanticDiff diff,
            WorkloadProjectedState targetState,
            WorkloadV2CommitReport report)
        {
            bool hasUnsupportedChanges = false;
            WorkloadProjectedState safeState = targetState ?? WorkloadProjectedState.Empty;
            WorkloadStateDimension[] dimensions =
            {
                WorkloadStateDimension.Schedules,
                WorkloadStateDimension.PresentationSettings
            };

            for (int i = 0; i < dimensions.Length; i++)
            {
                WorkloadStateDimension dimension = dimensions[i];
                if (!ownership.Owns(dimension)) continue;

                bool changed = HasChange(diff, dimension);
                bool hasState = HasStateEntries(safeState, dimension);
                report.Add(
                    WorkloadV2CommitMessageKind.Unsupported,
                    "dimension." + dimension,
                    dimension.ToString(),
                    changed || hasState
                        ? "This owned V2 dimension has state but no supported live writer; commit is blocked."
                        : "This owned V2 dimension is owned but empty; no state was written.");
                hasUnsupportedChanges |= changed || hasState;
            }

            return hasUnsupportedChanges;
        }

        private static bool HasStateEntries(
            WorkloadProjectedState state,
            WorkloadStateDimension dimension)
        {
            switch (dimension)
            {
                case WorkloadStateDimension.Schedules:
                    return state.Schedules.Count > 0;
                case WorkloadStateDimension.SpecificJobOverrides:
                    return state.SpecificJobOverrides.Count > 0;
                case WorkloadStateDimension.SpecificJobOrder:
                    return state.SpecificJobOrder.Count > 0;
                case WorkloadStateDimension.PresentationSettings:
                    return state.PresentationSettings.Count > 0;
                default:
                    return false;
            }
        }

        private static void ValidateRuntimeEntries(
            WorkloadSession session,
            WorkloadTemplate targetTemplate,
            WorkloadPreviewPlan plan,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            WorkloadOwnershipDimensions ownership = targetTemplate.Definition.OwnershipDimensions;
            WorkloadProjectedState before = plan?.BeforeState ?? WorkloadProjectedState.Empty;
            WorkloadProjectedState state = session?.ProjectedState ?? targetTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            if (ownership.Owns(WorkloadStateDimension.ParentPriorities))
            {
                List<ParentPriorityDifference> differences = BuildParentPriorityDifferences(before, state);
                for (int i = 0; i < differences.Count; i++)
                {
                    ParentPriorityDifference difference = differences[i];
                    if (session != null && session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged entry belongs to a pawn excluded for this application; the live pawn was left unchanged.");
                        continue;
                    }

                    bool stale;
                    TryResolveWritableEntry(
                        difference.Key.Pawn,
                        difference.Key.WorkType,
                        scope,
                        runtime,
                        report,
                        difference.Key.ToString(),
                        out Pawn unusedPawn,
                        out WorkTypeDef unusedWorkType,
                        out stale);
                    if (stale)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 apply was blocked during preflight because " +
                            difference.Key + " is stale or missing.");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.ManualModes))
            {
                List<ManualModeDifference> differences = BuildManualModeDifferences(before, state);
                for (int i = 0; i < differences.Count; i++)
                {
                    ManualModeDifference difference = differences[i];
                    if (session != null && session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged manual-mode entry belongs to a pawn excluded for this application; the live pawn was left unchanged.");
                        continue;
                    }

                    bool stale;
                    TryResolveWritableEntry(
                        difference.Key.Pawn,
                        difference.Key.WorkType,
                        scope,
                        runtime,
                        report,
                        difference.Key.ToString(),
                        out Pawn unusedPawn,
                        out WorkTypeDef unusedWorkType,
                        out stale);
                    if (stale)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 apply was blocked during preflight because " +
                            difference.Key + " is stale or missing.");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                List<SpecificJobOverrideDifference> differences =
                    BuildSpecificJobOverrideDifferences(before, state);
                for (int i = 0; i < differences.Count; i++)
                {
                    SpecificJobOverrideDifference difference = differences[i];
                    if (session != null && session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged specific-job entry belongs to a pawn excluded for this application; the live pawn was left unchanged.");
                        continue;
                    }

                    RejectUnsupportedSpecificJobClear(
                        difference.HasAfter,
                        WorkloadStateDimension.SpecificJobOverrides,
                        difference.Key,
                        report);

                    bool stale;
                    TryResolveSpecificWritableEntry(
                        difference.Key,
                        scope,
                        runtime,
                        report,
                        difference.Key.ToString(),
                        out Pawn unusedPawn,
                        out WorkTypeDef unusedWorkType,
                        out WorkGiverDef unusedWorkGiver,
                        out stale);
                    if (stale)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 apply was blocked during preflight because " +
                            difference.Key + " is stale or missing.");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                List<SpecificJobOrderDifference> differences =
                    BuildSpecificJobOrderDifferences(before, state);
                for (int i = 0; i < differences.Count; i++)
                {
                    SpecificJobOrderDifference difference = differences[i];
                    if (session != null && session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged specific-job order belongs to a pawn excluded for this application; the live pawn was left unchanged.");
                        continue;
                    }

                    RejectUnsupportedSpecificJobClear(
                        difference.HasAfter,
                        WorkloadStateDimension.SpecificJobOrder,
                        difference.Key,
                        report);

                    bool stale;
                    TryResolveSpecificWritableEntry(
                        difference.Key,
                        scope,
                        runtime,
                        report,
                        difference.Key.ToString(),
                        out Pawn unusedPawn,
                        out WorkTypeDef unusedWorkType,
                        out WorkGiverDef unusedWorkGiver,
                        out stale);
                    if (stale)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 apply was blocked during preflight because " +
                            difference.Key + " is stale or missing.");
                    }
                }
            }
        }

        private static bool HasChange(
            WorkloadSemanticDiff diff,
            WorkloadStateDimension dimension)
        {
            if (diff?.Changes == null) return false;
            for (int i = 0; i < diff.Changes.Count; i++)
            {
                if (diff.Changes[i].Dimension == dimension) return true;
            }

            return false;
        }

        private static void RejectUnsupportedSpecificJobClear(
            bool hasProjectedEntry,
            WorkloadStateDimension dimension,
            WorkloadSpecificJobKey key,
            WorkloadV2CommitReport report)
        {
            if (hasProjectedEntry) return;
            string message =
                "The projected " + dimension + " entry for " + key +
                " is absent, but V2 projection has no tombstone that can distinguish an intentional clear from live fallback. The commit was blocked without mutation.";
            report.Add(
                WorkloadV2CommitMessageKind.Unsupported,
                "specific-job.clear.requires-tombstone",
                key?.ToString() ?? string.Empty,
                message);
            Abort(WorkloadDiagnosticCode.UnsupportedOperation, message);
        }

        private static RuntimeCommitPlan PrepareRuntimeChanges(
            WorkloadSession session,
            WorkloadTemplate targetTemplate,
            WorkloadPreviewPlan plan,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            var result = new RuntimeCommitPlan();
            WorkloadOwnershipDimensions ownership = targetTemplate.Definition.OwnershipDimensions;
            // Apply is planned against the live state captured when the
            // preview opened. TemplateBaselineState is only the persistence
            // comparison used by Update and Fork.
            WorkloadProjectedState before = plan.BeforeState ?? WorkloadProjectedState.Empty;
            WorkloadProjectedState after = session.ProjectedState ?? WorkloadProjectedState.Empty;

            if (ownership.Owns(WorkloadStateDimension.ParentPriorities))
            {
                List<ParentPriorityDifference> differences = BuildParentPriorityDifferences(before, after);
                for (int i = 0; i < differences.Count; i++)
                {
                    ParentPriorityDifference difference = differences[i];
                    if (session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged entry belongs to a pawn excluded for this application; " +
                            "the live pawn was left unchanged.");
                        continue;
                    }

                    Pawn pawn;
                    WorkTypeDef workType;
                    if (!TryResolveWritableEntry(
                            difference.Key.Pawn,
                            difference.Key.WorkType,
                            scope,
                            runtime,
                             report,
                             difference.Key.ToString(),
                             out pawn,
                             out workType,
                             out bool stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The V2 apply was blocked before mutation because " +
                                difference.Key + " is stale or missing.");
                        }
                        continue;
                    }

                    EnsureRuntimeMutationAuthority(result, report, difference.Key.ToString());
                    int desired = difference.HasAfter
                        ? WorkPrioritySystem.ClampPriority(difference.After)
                        : WorkPrioritySystem.DisabledPriority;
                    int current = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
                    if (current == desired)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Unchanged,
                            "parent-priority.unchanged",
                            difference.Key.ToString(),
                            "The current parent priority already matches the preview.");
                        continue;
                    }

                    result.ParentPriorities.Add(new ParentPriorityMutation(
                        difference.Key,
                        pawn,
                        workType,
                        desired));
                }
                result.RequiresPriorityAuthority |= result.ParentPriorities.Count > 0;
            }

            if (ownership.Owns(WorkloadStateDimension.ManualModes))
            {
                List<ManualModeDifference> differences = BuildManualModeDifferences(before, after);
                bool hasAnyManualAfter = false;
                for (int i = 0; i < differences.Count; i++)
                {
                    if (differences[i].HasAfter)
                    {
                        hasAnyManualAfter = true;
                        break;
                    }
                }

                if (hasAnyManualAfter && !IsGenuinelyGlobalManualScope(scope))
                {
                    const string message =
                        "Manual-priority mode is global in RimWorld and cannot be applied from a pawn-scoped workload.";
                    report.Add(
                        WorkloadV2CommitMessageKind.Unsupported,
                        "manual-mode.pawn-scoped",
                        scope.Mode.ToString(),
                        message);
                    Abort(WorkloadDiagnosticCode.UnsupportedOperation, message);
                }

                bool hasManualTarget = false;
                bool manualTarget = false;
                if (differences.Count > 0 && Verse.Find.PlaySettings == null)
                {
                    Abort(
                        WorkloadDiagnosticCode.NoCurrentGame,
                        "Manual-priority state is unavailable at commit time.");
                }

                if (differences.Count > 0)
                {
                    EnsureRuntimeMutationAuthority(result, report, "manual-mode");
                }

                bool currentManual = Verse.Find.PlaySettings?.useWorkPriorities ?? true;
                for (int i = 0; i < differences.Count; i++)
                {
                    ManualModeDifference difference = differences[i];
                    if (session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged manual-mode entry belongs to a pawn excluded for this application; " +
                            "the live pawn was left unchanged.");
                        continue;
                    }

                    Pawn pawn;
                    WorkTypeDef workType;
                    if (!TryResolveWritableEntry(
                            difference.Key.Pawn,
                            difference.Key.WorkType,
                            scope,
                            runtime,
                             report,
                             difference.Key.ToString(),
                             out pawn,
                             out workType,
                             out bool stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The V2 apply was blocked before mutation because " +
                                difference.Key + " is stale or missing.");
                        }
                        continue;
                    }

                    if (!difference.HasAfter)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Skipped,
                            "manual-mode.removed",
                            difference.Key.ToString(),
                            "The per-key manual overlay was removed; the global manual-priority authority was left unchanged.");
                        continue;
                    }

                    result.ManualKeys.Add(difference.Key);

                    if (hasManualTarget && manualTarget != difference.After)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Unsupported,
                            "manual-mode.conflict",
                            difference.Key.ToString(),
                            "The V2 manual-mode entries disagree, but the live authority is global.");
                        Abort(
                            WorkloadDiagnosticCode.UnsupportedOperation,
                            "Conflicting V2 manual-mode entries cannot be represented by the global priority authority.");
                    }

                    hasManualTarget = true;
                    manualTarget = difference.After;
                    if (currentManual == difference.After)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Unchanged,
                            "manual-mode.unchanged",
                            difference.Key.ToString(),
                            "The global manual-priority mode already matches the preview.");
                    }
                    else
                    {
                        result.HasManualTarget = true;
                        result.ManualTarget = difference.After;
                    }
                }
                result.RequiresPriorityAuthority |= result.HasManualTarget;
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                List<SpecificJobOverrideDifference> differences =
                    BuildSpecificJobOverrideDifferences(before, after);
                for (int i = 0; i < differences.Count; i++)
                {
                    SpecificJobOverrideDifference difference = differences[i];
                    if (session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged specific-job entry belongs to a pawn excluded for this application; " +
                            "the live pawn was left unchanged.");
                        continue;
                    }

                    RejectUnsupportedSpecificJobClear(
                        difference.HasAfter,
                        WorkloadStateDimension.SpecificJobOverrides,
                        difference.Key,
                        report);

                    Pawn pawn;
                    WorkTypeDef workType;
                    WorkGiverDef workGiver;
                    bool stale;
                    if (!TryResolveSpecificWritableEntry(
                            difference.Key,
                            scope,
                            runtime,
                            report,
                            difference.Key.ToString(),
                            out pawn,
                            out workType,
                            out workGiver,
                            out stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The V2 apply was blocked before mutation because " +
                                difference.Key + " is stale or missing.");
                        }

                        continue;
                    }

                    int desired = WorkPrioritySystem.DisabledPriority;
                    if (difference.HasAfter)
                    {
                        if (difference.After.Kind != WorkloadScalarKind.Integer)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The specific-job priority for " + difference.Key +
                                " is not an integer priority.");
                        }

                        desired = difference.After.IntegerValue;
                        if (WorkPrioritySystem.ClampPriority(desired) != desired)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The specific-job priority for " + difference.Key +
                                " is outside the current BWT priority range.");
                        }
                    }

                    bool hasCurrent = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                        pawn,
                        workGiver,
                        out int current);
                    if (difference.HasAfter && hasCurrent && current == desired)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Unchanged,
                            "specific-job-priority.unchanged",
                            difference.Key.ToString(),
                            "The current BWT specific-job priority already matches the preview.");
                        continue;
                    }

                    if (!difference.HasAfter && !hasCurrent)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Unchanged,
                            "specific-job-priority.unchanged",
                            difference.Key.ToString(),
                            "The current BWT specific-job override is already absent.");
                        continue;
                    }

                    result.SpecificJobOverrides.Add(new SpecificJobOverrideMutation(
                        difference.Key,
                        pawn,
                        workType,
                        workGiver,
                        difference.HasAfter,
                        desired));
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                List<SpecificJobOrderDifference> differences =
                    BuildSpecificJobOrderDifferences(before, after);
                var grouped = new Dictionary<WorkloadParentPriorityKey, List<SpecificJobOrderDifference>>();
                for (int i = 0; i < differences.Count; i++)
                {
                    SpecificJobOrderDifference difference = differences[i];
                    if (session.IsExcludedForApply(difference.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString(),
                            "The staged specific-job order belongs to a pawn excluded for this application; " +
                            "the live pawn was left unchanged.");
                        continue;
                    }

                    RejectUnsupportedSpecificJobClear(
                        difference.HasAfter,
                        WorkloadStateDimension.SpecificJobOrder,
                        difference.Key,
                        report);

                    if (!grouped.TryGetValue(
                            new WorkloadParentPriorityKey(difference.Key.Pawn, difference.Key.WorkType),
                            out List<SpecificJobOrderDifference> entries))
                    {
                        entries = new List<SpecificJobOrderDifference>();
                        grouped.Add(
                            new WorkloadParentPriorityKey(difference.Key.Pawn, difference.Key.WorkType),
                            entries);
                    }

                    entries.Add(difference);
                }

                var parents = new List<WorkloadParentPriorityKey>(grouped.Keys);
                parents.Sort((left, right) => left.CompareTo(right));
                for (int parentIndex = 0; parentIndex < parents.Count; parentIndex++)
                {
                    WorkloadParentPriorityKey parent = parents[parentIndex];
                    List<SpecificJobOrderDifference> entries = grouped[parent];
                    Pawn pawn;
                    WorkTypeDef workType;
                    if (!TryResolveWritableEntry(
                            parent.Pawn,
                            parent.WorkType,
                            scope,
                            runtime,
                            report,
                            parent.ToString(),
                            out pawn,
                            out workType,
                            out bool parentStale))
                    {
                        if (parentStale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The V2 apply was blocked before mutation because " +
                                parent + " is stale or missing.");
                        }

                        continue;
                    }

                    bool groupEligible = true;
                    for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                    {
                        SpecificJobOrderDifference entry = entries[entryIndex];
                        if (!TryResolveSpecificWritableEntry(
                                entry.Key,
                                scope,
                                runtime,
                                report,
                                entry.Key.ToString(),
                                out Pawn unusedPawn,
                                out WorkTypeDef unusedWorkType,
                                out WorkGiverDef unusedWorkGiver,
                                out bool stale))
                        {
                            if (stale)
                            {
                                Abort(
                                    WorkloadDiagnosticCode.InvalidState,
                                    "The V2 apply was blocked before mutation because " +
                                    entry.Key + " is stale or missing.");
                            }

                            groupEligible = false;
                        }
                    }

                    if (!groupEligible)
                    {
                        continue;
                    }

                    SpecificJobOrderMutation orderMutation =
                        BuildSpecificJobOrderMutation(
                            parent,
                            after,
                            pawn,
                            workType,
                            report);
                    if (orderMutation != null)
                    {
                        result.SpecificJobOrder.Add(orderMutation);
                    }
                }
            }

            bool ownsSpecificJobState =
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);
            ValidateSpecificParentMode(result, report);
            result.RequiresPriorityAuthority |=
                ownership.Owns(WorkloadStateDimension.ParentPriorities) ||
                ownership.Owns(WorkloadStateDimension.ManualModes) ||
                ownsSpecificJobState;
            if (result.RequiresPriorityAuthority)
            {
                EnsureRuntimeMutationAuthority(result, report, "runtime-plan.authority");
            }

            if (result.HasLiveMutations && MultiplayerBridge.Active)
            {
                const string message =
                    "V2 Apply live mutations require immediate authoritative acknowledgement and are unavailable in the active multiplayer route. Update and Fork remain persistence-only operations.";
                report.Add(
                    WorkloadV2CommitMessageKind.Unsupported,
                    "apply.multiplayer-acknowledgement",
                    "live-state",
                    message);
                Abort(WorkloadDiagnosticCode.UnsupportedOperation, message);
            }

            result.RequiresSpecificJobRevision = ownsSpecificJobState;
            if (result.RequiresSpecificJobRevision)
            {
                if (session.RuntimeBaseline == null ||
                    !session.RuntimeBaseline.HasSpecificJobRevision)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 preview is missing the BWT specific-job revision required for a safe commit.");
                }

                result.SpecificJobRevision = session.RuntimeBaseline.SpecificJobRevision;
                result.HasSpecificJobRevision = true;
                EnsureSpecificJobRevision(result, report, "runtime-plan.specific-job");
            }

            if (result.HasAuthorityRevision)
            {
                EnsureRuntimeMutationAuthority(result, report, "runtime-plan.final");
            }

            return result;
        }

        private static bool IsGenuinelyGlobalManualScope(WorkloadScope scope)
        {
            return scope != null &&
                   scope.Mode == WorkloadScopeMode.CurrentMapFreeColonists &&
                   scope.ExplicitPawnIds.Count == 0 &&
                   scope.ExcludedPawnIds.Count == 0;
        }

        private static void ValidateManualModeConsistency(
            WorkloadTemplate targetTemplate,
            WorkloadScope scope,
            WorkloadV2CommitReport report)
        {
            if (targetTemplate?.Definition == null ||
                !targetTemplate.Definition.OwnershipDimensions.Owns(WorkloadStateDimension.ManualModes))
            {
                return;
            }

            WorkloadProjectedState state = targetTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            if (state.ManualModes.Count > 0 && !IsGenuinelyGlobalManualScope(scope))
            {
                const string message =
                    "Manual-priority mode is global in RimWorld and requires the unexcluded current-map free-colonist scope.";
                report.Add(
                    WorkloadV2CommitMessageKind.Unsupported,
                    "manual-mode.scope",
                    scope?.Mode.ToString() ?? string.Empty,
                    message);
                Abort(WorkloadDiagnosticCode.UnsupportedOperation, message);
            }

            bool hasValue = false;
            bool value = false;
            for (int i = 0; i < state.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = state.ManualModes[i];
                if (!hasValue)
                {
                    hasValue = true;
                    value = entry.Manual;
                    continue;
                }

                if (value == entry.Manual) continue;
                const string message =
                    "Conflicting V2 manual-mode entries cannot be represented by RimWorld's global priority mode.";
                report.Add(
                    WorkloadV2CommitMessageKind.Unsupported,
                    "manual-mode.conflict",
                    "global",
                    message);
                Abort(WorkloadDiagnosticCode.UnsupportedOperation, message);
            }
        }

        private static bool TryResolveWritableEntry(
            PawnKey pawnKey,
            WorkTypeKey workTypeKey,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report,
            string subject,
            out Pawn pawn,
            out WorkTypeDef workType)
        {
            return TryResolveWritableEntry(
                pawnKey,
                workTypeKey,
                scope,
                runtime,
                report,
                subject,
                out pawn,
                out workType,
                out bool unusedMissingOrStale);
        }

        private static bool TryResolveWritableEntry(
            PawnKey pawnKey,
            WorkTypeKey workTypeKey,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report,
            string subject,
            out Pawn pawn,
            out WorkTypeDef workType,
            out bool missingOrStale)
        {
            pawn = null;
            workType = null;
            missingOrStale = false;
            if (scope.IsExplicitlyExcluded(pawnKey))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Excluded,
                    "scope.excluded",
                    subject,
                    "The V2 entry is explicitly excluded from the workload scope.");
                return false;
            }

            if (!runtime.Pawns.TryGetValue(pawnKey.Value, out pawn))
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitMessageKind.MissingOrStale,
                    "pawn.missing",
                    subject,
                    "The V2 pawn ID is stale or missing at commit time.");
                return false;
            }

            if (pawn.Dead)
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Skipped,
                    "pawn.dead",
                    subject,
                    "The V2 entry was skipped because the pawn is dead.");
                return false;
            }

            if (!IsInScope(scope, pawn, runtime))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Skipped,
                    "scope.outside",
                    subject,
                    "The V2 entry was skipped because the pawn is outside the current workload scope.");
                return false;
            }

            if (!runtime.WorkTypes.TryGetValue(workTypeKey.Value, out workType))
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitMessageKind.MissingOrStale,
                    "work-type.missing",
                    subject,
                    "The V2 WorkTypeDef ID is stale or missing at commit time.");
                return false;
            }

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Skipped,
                    "pawn.work-settings",
                    subject,
                    "The V2 entry was skipped because the pawn has no writable work settings.");
                return false;
            }

            if (pawn.WorkTypeIsDisabled(workType))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Skipped,
                    "work-type.disabled",
                    subject,
                    "The V2 entry was skipped because the work type is disabled for the pawn.");
                return false;
            }

            return true;
        }

        private static bool TryResolveSpecificWritableEntry(
            WorkloadSpecificJobKey key,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report,
            string subject,
            out Pawn pawn,
            out WorkTypeDef workType,
            out WorkGiverDef workGiver,
            out bool missingOrStale)
        {
            pawn = null;
            workType = null;
            workGiver = null;
            missingOrStale = false;
            if (key == null || !key.IsValid)
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitMessageKind.MissingOrStale,
                    "specific-job.invalid",
                    subject,
                    "The V2 specific-job key is invalid at commit time.");
                return false;
            }

            if (!TryResolveWritableEntry(
                    key.Pawn,
                    key.WorkType,
                    scope,
                    runtime,
                    report,
                    subject,
                    out pawn,
                    out workType,
                    out missingOrStale))
            {
                return false;
            }

            if (!runtime.WorkGivers.TryGetValue(key.WorkGiver.Value, out workGiver))
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitMessageKind.MissingOrStale,
                    "work-giver.missing",
                    subject,
                    "The V2 WorkGiverDef ID is stale or missing at commit time.");
                return false;
            }

            if (WorkGiverReassignmentManager.GetTargetWorkType(workGiver) != workType)
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitMessageKind.MissingOrStale,
                    "work-giver.mapping.changed",
                    subject,
                    "The V2 WorkGiverDef no longer belongs to the stable WorkTypeDef at commit time.");
                return false;
            }

            if (!TryGetEffectiveWorkGiverOrder(
                    pawn,
                    workType,
                    workGiver.defName,
                    out int unusedOrder))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Skipped,
                    "work-giver.unavailable",
                    subject,
                    "The V2 specific-job entry was skipped because the WorkGiverDef is not currently available in the work type.");
                return false;
            }

            return true;
        }

        private static bool IsInScope(
            WorkloadScope scope,
            Pawn pawn,
            RuntimeContext runtime)
        {
            if (scope.Mode == WorkloadScopeMode.ExplicitPawnIds)
            {
                return ContainsPawnKey(scope.ExplicitPawnIds, pawn);
            }

            return runtime.IsCurrentMapFreeColonist(pawn);
        }

        private static bool ContainsPawnKey(
            IReadOnlyList<PawnKey> keys,
            Pawn pawn)
        {
            string pawnId = pawn.thingIDNumber.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < keys.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(keys[i].Value, pawnId)) return true;
            }

            return false;
        }

        private static List<ParentPriorityDifference> BuildParentPriorityDifferences(
            WorkloadProjectedState before,
            WorkloadProjectedState after)
        {
            var beforeValues = new Dictionary<WorkloadParentPriorityKey, int>();
            var afterValues = new Dictionary<WorkloadParentPriorityKey, int>();
            for (int i = 0; i < before.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = before.ParentPriorities[i];
                beforeValues[entry.Key] = entry.Priority;
            }

            for (int i = 0; i < after.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = after.ParentPriorities[i];
                afterValues[entry.Key] = entry.Priority;
            }

            var keys = new HashSet<WorkloadParentPriorityKey>();
            foreach (WorkloadParentPriorityKey key in beforeValues.Keys) keys.Add(key);
            foreach (WorkloadParentPriorityKey key in afterValues.Keys) keys.Add(key);
            var ordered = new List<WorkloadParentPriorityKey>(keys);
            ordered.Sort((left, right) => left.CompareTo(right));

            var result = new List<ParentPriorityDifference>();
            for (int i = 0; i < ordered.Count; i++)
            {
                WorkloadParentPriorityKey key = ordered[i];
                int beforeValue;
                int afterValue;
                bool hasBefore = beforeValues.TryGetValue(key, out beforeValue);
                bool hasAfter = afterValues.TryGetValue(key, out afterValue);
                if (hasBefore && hasAfter && beforeValue == afterValue) continue;
                result.Add(new ParentPriorityDifference(key, hasAfter, afterValue));
            }

            return result;
        }

        private static List<ManualModeDifference> BuildManualModeDifferences(
            WorkloadProjectedState before,
            WorkloadProjectedState after)
        {
            var beforeValues = new Dictionary<WorkloadParentPriorityKey, bool>();
            var afterValues = new Dictionary<WorkloadParentPriorityKey, bool>();
            for (int i = 0; i < before.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = before.ManualModes[i];
                beforeValues[entry.Key] = entry.Manual;
            }

            for (int i = 0; i < after.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = after.ManualModes[i];
                afterValues[entry.Key] = entry.Manual;
            }

            var keys = new HashSet<WorkloadParentPriorityKey>();
            foreach (WorkloadParentPriorityKey key in beforeValues.Keys) keys.Add(key);
            foreach (WorkloadParentPriorityKey key in afterValues.Keys) keys.Add(key);
            var ordered = new List<WorkloadParentPriorityKey>(keys);
            ordered.Sort((left, right) => left.CompareTo(right));

            var result = new List<ManualModeDifference>();
            for (int i = 0; i < ordered.Count; i++)
            {
                WorkloadParentPriorityKey key = ordered[i];
                bool beforeValue;
                bool afterValue;
                bool hasBefore = beforeValues.TryGetValue(key, out beforeValue);
                bool hasAfter = afterValues.TryGetValue(key, out afterValue);
                if (hasBefore && hasAfter && beforeValue == afterValue) continue;
                result.Add(new ManualModeDifference(key, hasAfter, afterValue));
            }

            return result;
        }

        private static void ValidateSpecificParentMode(
            RuntimeCommitPlan plan,
            WorkloadV2CommitReport report)
        {
            var parents = new Dictionary<WorkloadParentPriorityKey, ParentPriorityMutation>();
            for (int i = 0; i < plan.SpecificJobOverrides.Count; i++)
            {
                SpecificJobOverrideMutation mutation = plan.SpecificJobOverrides[i];
                var parent = new WorkloadParentPriorityKey(
                    mutation.Key.Pawn,
                    mutation.Key.WorkType);
                if (!parents.ContainsKey(parent))
                {
                    parents.Add(
                        parent,
                        new ParentPriorityMutation(
                            parent,
                            mutation.Pawn,
                            mutation.WorkType,
                            GetPlannedParentPriority(plan, mutation.Pawn, mutation.WorkType)));
                }
            }

            for (int i = 0; i < plan.SpecificJobOrder.Count; i++)
            {
                SpecificJobOrderMutation mutation = plan.SpecificJobOrder[i];
                if (!parents.ContainsKey(mutation.Parent))
                {
                    parents.Add(
                        mutation.Parent,
                        new ParentPriorityMutation(
                            mutation.Parent,
                            mutation.Pawn,
                            mutation.WorkType,
                            GetPlannedParentPriority(plan, mutation.Pawn, mutation.WorkType)));
                }
            }

            foreach (ParentPriorityMutation parent in parents.Values)
            {
                if (parent.DesiredPriority > WorkPrioritySystem.DisabledPriority ||
                    WorkGiverReassignmentManager.LockedSubWorkOverridesDisabledParent())
                {
                    continue;
                }

                string message =
                    "Specific-job state for " + parent.Key +
                    " cannot be committed while its parent priority is disabled unless BWT's locked-specific override mode is active.";
                report.Add(
                    WorkloadV2CommitMessageKind.Unsupported,
                    "specific-job.parent-disabled",
                    parent.Key.ToString(),
                    message);
                Abort(WorkloadDiagnosticCode.UnsupportedOperation, message);
            }
        }

        private static int GetPlannedParentPriority(
            RuntimeCommitPlan plan,
            Pawn pawn,
            WorkTypeDef workType)
        {
            for (int i = 0; i < plan.ParentPriorities.Count; i++)
            {
                ParentPriorityMutation mutation = plan.ParentPriorities[i];
                if (mutation.Pawn == pawn && mutation.WorkType == workType)
                {
                    return mutation.DesiredPriority;
                }
            }

            return PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                pawn.workSettings,
                workType);
        }

        private static List<SpecificJobOverrideDifference> BuildSpecificJobOverrideDifferences(
            WorkloadProjectedState before,
            WorkloadProjectedState after)
        {
            var beforeValues = new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
            var afterValues = new Dictionary<WorkloadSpecificJobKey, WorkloadScalarValue>();
            for (int i = 0; i < before.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = before.SpecificJobOverrides[i];
                beforeValues[entry.Key] = entry.Value;
            }

            for (int i = 0; i < after.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = after.SpecificJobOverrides[i];
                afterValues[entry.Key] = entry.Value;
            }

            var keys = new HashSet<WorkloadSpecificJobKey>();
            foreach (WorkloadSpecificJobKey key in beforeValues.Keys) keys.Add(key);
            foreach (WorkloadSpecificJobKey key in afterValues.Keys) keys.Add(key);
            var ordered = new List<WorkloadSpecificJobKey>(keys);
            ordered.Sort((left, right) => left.CompareTo(right));

            var result = new List<SpecificJobOverrideDifference>();
            for (int i = 0; i < ordered.Count; i++)
            {
                WorkloadSpecificJobKey key = ordered[i];
                WorkloadScalarValue beforeValue;
                WorkloadScalarValue afterValue = WorkloadScalarValue.Empty;
                bool hasBefore = beforeValues.TryGetValue(key, out beforeValue);
                bool hasAfter = afterValues.TryGetValue(key, out afterValue);
                if (hasBefore && hasAfter && beforeValue.Equals(afterValue)) continue;
                result.Add(new SpecificJobOverrideDifference(key, hasAfter, afterValue));
            }

            return result;
        }

        private static List<SpecificJobOrderDifference> BuildSpecificJobOrderDifferences(
            WorkloadProjectedState before,
            WorkloadProjectedState after)
        {
            var beforeValues = new Dictionary<WorkloadSpecificJobKey, int>();
            var afterValues = new Dictionary<WorkloadSpecificJobKey, int>();
            for (int i = 0; i < before.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = before.SpecificJobOrder[i];
                beforeValues[entry.Key] = entry.Order;
            }

            for (int i = 0; i < after.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = after.SpecificJobOrder[i];
                afterValues[entry.Key] = entry.Order;
            }

            var keys = new HashSet<WorkloadSpecificJobKey>();
            foreach (WorkloadSpecificJobKey key in beforeValues.Keys) keys.Add(key);
            foreach (WorkloadSpecificJobKey key in afterValues.Keys) keys.Add(key);
            var ordered = new List<WorkloadSpecificJobKey>(keys);
            ordered.Sort((left, right) => left.CompareTo(right));

            var result = new List<SpecificJobOrderDifference>();
            for (int i = 0; i < ordered.Count; i++)
            {
                WorkloadSpecificJobKey key = ordered[i];
                int beforeValue;
                int afterValue = 0;
                bool hasBefore = beforeValues.TryGetValue(key, out beforeValue);
                bool hasAfter = afterValues.TryGetValue(key, out afterValue);
                if (hasBefore && hasAfter && beforeValue == afterValue) continue;
                result.Add(new SpecificJobOrderDifference(key, hasAfter, afterValue));
            }

            return result;
        }

        private static SpecificJobOrderMutation BuildSpecificJobOrderMutation(
            WorkloadParentPriorityKey parent,
            WorkloadProjectedState after,
            Pawn pawn,
            WorkTypeDef workType,
            WorkloadV2CommitReport report)
        {
            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot snapshot =
                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(pawn, workType);
            IReadOnlyList<WorkGiver> currentWorkGivers =
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType, pawn);
            var currentNames = new List<string>();
            for (int i = 0; i < currentWorkGivers.Count; i++)
            {
                string name = currentWorkGivers[i]?.def?.defName;
                if (!string.IsNullOrEmpty(name)) currentNames.Add(name);
            }

            var requestedByIndex = new Dictionary<int, string>();
            var requestedNames = new HashSet<string>(StringComparer.Ordinal);
            int requestedCount = 0;
            for (int i = 0; i < after.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = after.SpecificJobOrder[i];
                if (!new WorkloadParentPriorityKey(entry.Key.Pawn, entry.Key.WorkType).Equals(parent))
                {
                    continue;
                }

                if (entry.Order < 0 || entry.Order >= currentNames.Count ||
                    !requestedNames.Add(entry.Key.WorkGiver.Value) ||
                    requestedByIndex.ContainsKey(entry.Order) ||
                    !currentNames.Contains(entry.Key.WorkGiver.Value))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The specific-job order for " + parent +
                        " is not a valid permutation of the current BWT work-giver set.");
                }

                requestedByIndex.Add(entry.Order, entry.Key.WorkGiver.Value);
                requestedCount++;
            }

            if (requestedCount == 0)
            {
                if (!snapshot.HasStoredOrder)
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Unchanged,
                        "specific-job-order.unchanged",
                        parent.ToString(),
                        "The exact BWT pawn-specific work-giver order is already absent.");
                    return null;
                }

                return new SpecificJobOrderMutation(
                    parent,
                    pawn,
                    workType,
                    null,
                    snapshot);
            }

            var desired = new string[currentNames.Count];
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<int, string> requested in requestedByIndex)
            {
                desired[requested.Key] = requested.Value;
                used.Add(requested.Value);
            }

            int nextCurrent = 0;
            for (int desiredIndex = 0; desiredIndex < desired.Length; desiredIndex++)
            {
                if (!string.IsNullOrEmpty(desired[desiredIndex])) continue;
                while (nextCurrent < currentNames.Count && used.Contains(currentNames[nextCurrent]))
                {
                    nextCurrent++;
                }

                if (nextCurrent >= currentNames.Count)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The specific-job order for " + parent + " could not be completed safely.");
                }

                desired[desiredIndex] = currentNames[nextCurrent];
                used.Add(currentNames[nextCurrent]);
                nextCurrent++;
            }

            if (snapshot.HasStoredOrder && SequenceEqual(snapshot.OrderedWorkGiverNames, desired))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Unchanged,
                    "specific-job-order.unchanged",
                    parent.ToString(),
                    "The exact BWT pawn-specific work-giver order already matches the preview.");
                return null;
            }

            return new SpecificJobOrderMutation(
                parent,
                pawn,
                workType,
                desired,
                snapshot);
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

        private static void EnsureRuntimeMutationAuthority(
            RuntimeCommitPlan plan,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (plan == null)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 runtime mutation plan is unavailable.");
            }

            if (!plan.HasAuthorityRevision)
            {
                if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long revision))
                {
                    EnsureBetterWorkTabAuthority(report);
                    return;
                }

                plan.AuthorityRevision = revision;
                plan.HasAuthorityRevision = true;
            }

            if (WorkPrioritySystem.IsBwtMutationAuthorityCurrent(plan.AuthorityRevision))
            {
                return;
            }

            string message =
                "The V2 live mutation was blocked because priority authority changed during " +
                (string.IsNullOrEmpty(subject) ? "planning" : subject) + "; no external handoff was attempted.";
            report.Add(
                WorkloadV2CommitMessageKind.Fatal,
                "priority-authority.changed",
                subject ?? string.Empty,
                message);
            Abort(WorkloadDiagnosticCode.ExternalPriorityAuthority, message);
        }

        private static void EnsureSpecificJobRevision(
            RuntimeCommitPlan plan,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (plan == null || !plan.RequiresSpecificJobRevision) return;
            if (!plan.HasSpecificJobRevision)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 specific-job mutation plan is missing its BWT sync revision.");
            }

            if (WorkGiverReassignmentManager.CurrentSyncVersion == plan.SpecificJobRevision)
            {
                return;
            }

            string message =
                "The V2 specific-job mutation was blocked because BWT work-giver state changed during " +
                (string.IsNullOrEmpty(subject) ? "planning" : subject) + ".";
            report.Add(
                WorkloadV2CommitMessageKind.Fatal,
                "specific-job.revision.changed",
                subject ?? string.Empty,
                message);
            Abort(WorkloadDiagnosticCode.InvalidState, message);
        }

        private static void RevalidateRuntimeBaselines(
            WorkloadSession session,
            RuntimeCommitPlan plan,
            WorkloadTemplate targetTemplate,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (plan == null || targetTemplate?.Definition == null) return;
            WorkloadOwnershipDimensions ownership =
                targetTemplate.Definition.OwnershipDimensions;
            bool validatesRuntimeState =
                ownership.Owns(WorkloadStateDimension.ParentPriorities) ||
                ownership.Owns(WorkloadStateDimension.ManualModes) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);
            if (!validatesRuntimeState) return;

            WorkloadRuntimeBaseline baseline = session?.RuntimeBaseline;
            if (baseline == null)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview is missing its captured live runtime baseline.");
            }

            RuntimeContext runtime = BuildRuntimeContext(targetTemplate, report);
            WorkloadScope scope = targetTemplate.Definition.Scope ?? WorkloadScope.Empty;

            if (plan.RequiresPriorityAuthority)
            {
                EnsureRuntimeMutationAuthority(plan, report, subject + ".priority");
            }

            if (ownership.Owns(WorkloadStateDimension.ManualModes))
            {
                if (!baseline.HasManualMode || Verse.Find.PlaySettings == null)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 preview is missing the captured global manual-priority baseline.");
                }

                if (Verse.Find.PlaySettings.useWorkPriorities != baseline.ManualMode)
                {
                    AbortBaselineChanged(report, "manual-mode", "The global manual-priority mode changed after preview capture.");
                }
            }

            if (ownership.Owns(WorkloadStateDimension.ParentPriorities))
            {
                foreach (KeyValuePair<WorkloadParentPriorityKey, int> captured in
                         baseline.ParentPriorities)
                {
                    WorkloadParentPriorityKey key = captured.Key;
                    if (session.IsExcludedForApply(key.Pawn)) continue;
                    if (!TryResolveWritableEntry(
                            key.Pawn,
                            key.WorkType,
                            scope,
                            runtime,
                            report,
                            key.ToString(),
                            out Pawn pawn,
                            out WorkTypeDef workType,
                            out bool stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The captured parent-priority baseline became stale for " + key + ".");
                        }

                        continue;
                    }

                    int current = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
                    if (current != captured.Value)
                    {
                        AbortBaselineChanged(
                            report,
                            key.ToString(),
                            "The BWT parent priority changed after preview capture.");
                    }
                }
            }

            if (plan.RequiresSpecificJobRevision)
            {
                EnsureSpecificJobRevision(plan, report, subject + ".specific-job");
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                foreach (WorkloadSpecificJobRuntimeBaseline captured in
                         baseline.SpecificJobOverrides)
                {
                    WorkloadSpecificJobKey key = captured.Key;
                    if (session.IsExcludedForApply(key.Pawn)) continue;
                    if (!TryResolveSpecificWritableEntry(
                            key,
                            scope,
                            runtime,
                            report,
                            key.ToString(),
                            out Pawn pawn,
                            out WorkTypeDef unusedWorkType,
                            out WorkGiverDef workGiver,
                            out bool stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The captured specific-job baseline became stale for " + key + ".");
                        }

                        continue;
                    }

                    bool currentHasOverride =
                        WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                            pawn,
                            workGiver,
                            out int currentPriority);
                    if (currentHasOverride != captured.HasOverride ||
                        (currentHasOverride && currentPriority != captured.Priority))
                    {
                        AbortBaselineChanged(
                            report,
                            key.ToString(),
                            "The BWT specific-job priority changed after preview capture.");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                foreach (WorkloadSpecificJobOrderRuntimeBaseline captured in
                         baseline.SpecificJobOrders)
                {
                    WorkloadParentPriorityKey parent = captured.Parent;
                    if (session.IsExcludedForApply(parent.Pawn)) continue;
                    if (!TryResolveWritableEntry(
                            parent.Pawn,
                            parent.WorkType,
                            scope,
                            runtime,
                            report,
                            parent.ToString(),
                            out Pawn pawn,
                            out WorkTypeDef workType,
                            out bool stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The captured specific-job order baseline became stale for " + parent + ".");
                        }

                        continue;
                    }

                    WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot current =
                        WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                            pawn,
                            workType);
                    if (current.HasStoredOrder != captured.HasStoredOrder ||
                        (current.HasStoredOrder &&
                         !SequenceEqual(
                             current.OrderedWorkGiverNames,
                             captured.OrderedWorkGiverNames)))
                    {
                        AbortBaselineChanged(
                            report,
                            parent.ToString(),
                            "The BWT pawn-specific work-giver order changed after preview capture.");
                    }
                }
            }
        }

        private static void RevalidateParentBaseline(
            WorkloadSession session,
            RuntimeCommitPlan plan,
            ParentPriorityMutation mutation,
            WorkloadV2CommitReport report,
            string subject)
        {
            EnsureRuntimeMutationAuthority(plan, report, subject + ".authority");
            if (!session.RuntimeBaseline.TryGetParentPriority(
                    mutation.Key,
                    out int expected))
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The captured parent-priority baseline is missing " + mutation.Key + ".");
            }

            int current = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                mutation.Pawn.workSettings,
                mutation.WorkType);
            if (current != expected)
            {
                AbortBaselineChanged(report, mutation.Key.ToString(), "The BWT parent priority changed immediately before the write.");
            }
        }

        private static void RevalidateManualBaseline(
            WorkloadSession session,
            RuntimeCommitPlan plan,
            WorkloadV2CommitReport report,
            string subject)
        {
            EnsureRuntimeMutationAuthority(plan, report, subject + ".authority");
            if (session?.RuntimeBaseline == null ||
                !session.RuntimeBaseline.HasManualMode ||
                Verse.Find.PlaySettings == null)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The captured global manual-priority baseline is unavailable immediately before the write.");
            }

            if (Verse.Find.PlaySettings.useWorkPriorities != session.RuntimeBaseline.ManualMode)
            {
                AbortBaselineChanged(
                    report,
                    "manual-mode",
                    "The global manual-priority mode changed immediately before the write.");
            }
        }

        private static void RevalidateSpecificOverrideBaseline(
            WorkloadSession session,
            RuntimeCommitPlan plan,
            SpecificJobOverrideMutation mutation,
            WorkloadV2CommitReport report,
            string subject)
        {
            EnsureRuntimeMutationAuthority(plan, report, subject + ".authority");
            EnsureSpecificJobRevision(plan, report, subject + ".revision");
            if (!session.RuntimeBaseline.TryGetSpecificJobOverride(
                    mutation.Key,
                    out bool expectedHasOverride,
                    out int expectedPriority))
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The captured specific-job baseline is missing " + mutation.Key + ".");
            }

            bool currentHasOverride = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                mutation.Pawn,
                mutation.WorkGiver,
                out int currentPriority);
            if (currentHasOverride != expectedHasOverride ||
                (currentHasOverride && currentPriority != expectedPriority))
            {
                AbortBaselineChanged(report, mutation.Key.ToString(), "The BWT specific-job priority changed immediately before the write.");
            }
        }

        private static void RevalidateSpecificOrderBaseline(
            WorkloadSession session,
            RuntimeCommitPlan plan,
            SpecificJobOrderMutation mutation,
            WorkloadV2CommitReport report,
            string subject)
        {
            EnsureRuntimeMutationAuthority(plan, report, subject + ".authority");
            EnsureSpecificJobRevision(plan, report, subject + ".revision");
            if (!session.RuntimeBaseline.TryGetSpecificJobOrder(
                    mutation.Parent,
                    out WorkloadSpecificJobOrderRuntimeBaseline expected))
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The captured specific-job order baseline is missing " + mutation.Parent + ".");
            }

            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot current =
                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                    mutation.Pawn,
                    mutation.WorkType);
            if (current.HasStoredOrder != expected.HasStoredOrder ||
                (current.HasStoredOrder &&
                 !SequenceEqual(current.OrderedWorkGiverNames, expected.OrderedWorkGiverNames)))
            {
                AbortBaselineChanged(report, mutation.Parent.ToString(), "The BWT pawn-specific work-giver order changed immediately before the write.");
            }
        }

        private static void AdvanceSpecificJobRevision(
            RuntimeCommitPlan plan,
            WorkloadV2CommitReport report,
            int previousRevision,
            string subject)
        {
            if (plan == null || !plan.RequiresSpecificJobRevision ||
                previousRevision != plan.SpecificJobRevision)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The BWT specific-job revision changed before " + subject + ".");
            }

            int observedRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            int expectedRevision = unchecked(previousRevision + 1);
            if (observedRevision != expectedRevision)
            {
                string message =
                    "The BWT specific-job revision changed unexpectedly around " + subject + ".";
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "specific-job.revision.changed",
                    subject ?? string.Empty,
                    message);
                Abort(WorkloadDiagnosticCode.InvalidState, message);
            }

            plan.SpecificJobRevision = observedRevision;
        }

        private static void AbortBaselineChanged(
            WorkloadV2CommitReport report,
            string subject,
            string message)
        {
            report.Add(
                WorkloadV2CommitMessageKind.Fatal,
                "runtime-baseline.changed",
                subject ?? string.Empty,
                message);
            Abort(WorkloadDiagnosticCode.InvalidState, message);
        }

        private static void EnsureBetterWorkTabAuthority(WorkloadV2CommitReport report)
        {
            try
            {
                if (WorkPrioritySystem.TryCaptureBwtMutationAuthority(out _))
                {
                    return;
                }

                PriorityAuthoritySnapshot snapshot = PriorityAuthorityResolver.Resolve();
                string authority = snapshot.IsCoherent
                    ? snapshot.Owner.ToString()
                    : "an incoherent authority registry";
                string message = "V2 priority commit is blocked because " + authority +
                    " owns or prevents coherent priority authority; no external handoff was attempted.";
                report.Add(
                    WorkloadV2CommitMessageKind.Unsupported,
                    "priority-authority",
                    authority,
                    message);
                Abort(WorkloadDiagnosticCode.ExternalPriorityAuthority, message);
            }
            catch (CommitAbortException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Abort(
                    WorkloadDiagnosticCode.ExternalPriorityAuthority,
                    "Priority authority could not be resolved safely: " + exception.Message);
            }
        }

        private void ApplyLive(
            LiveMutationTransaction transaction,
            RuntimeCommitPlan runtimePlan,
            WorkloadTemplate targetTemplate,
            WorkloadSession session,
            WorkloadV2CommitReport report)
        {
            if (transaction == null || runtimePlan == null || targetTemplate == null)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 live mutation transaction is unavailable.");
            }

            WorkloadScope scope = targetTemplate.Definition.Scope ?? WorkloadScope.Empty;
            RuntimeContext runtime = BuildRuntimeContext(targetTemplate, report);
            IDisposable specificBatch = null;
            try
            {
                if (runtimePlan.SpecificJobOverrides.Count > 0 ||
                    runtimePlan.SpecificJobOrder.Count > 0)
                {
                    specificBatch = WorkGiverReassignmentManager.BeginMutationBatch();
                }

                if (runtimePlan.RequiresPriorityAuthority)
                {
                    EnsureRuntimeMutationAuthority(runtimePlan, report, "live-apply.start");
                }

                if (runtimePlan.RequiresSpecificJobRevision)
                {
                    EnsureSpecificJobRevision(runtimePlan, report, "live-apply.specific-job.start");
                }

                ValidateSpecificMutationsPrewrite(
                    runtimePlan,
                    scope,
                    runtime,
                    report);

                if (runtimePlan.HasManualTarget)
                {
                    bool hasWritableManualEntry = false;
                    for (int i = 0; i < runtimePlan.ManualKeys.Count; i++)
                    {
                        bool stale;
                        if (TryResolveWritableEntry(
                                runtimePlan.ManualKeys[i].Pawn,
                                runtimePlan.ManualKeys[i].WorkType,
                                scope,
                                runtime,
                                report,
                                runtimePlan.ManualKeys[i].ToString(),
                                out Pawn unusedPawn,
                                out WorkTypeDef unusedWorkType,
                                out stale))
                        {
                            hasWritableManualEntry = true;
                        }
                        else if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The V2 apply was blocked immediately before mutation because " +
                                runtimePlan.ManualKeys[i] + " is stale or missing.");
                        }
                    }

                    if (hasWritableManualEntry)
                    {
                        RevalidateManualBaseline(
                            session,
                            runtimePlan,
                            report,
                            "manual-mode");
                        bool previousManualMode = Verse.Find.PlaySettings.useWorkPriorities;
                        if (previousManualMode != runtimePlan.ManualTarget)
                        {
                            bool writeAccepted =
                                WorkPrioritySystem.SetManualPriorities(runtimePlan.ManualTarget);
                            bool observedManualMode = Verse.Find.PlaySettings.useWorkPriorities;
                            if (observedManualMode != previousManualMode)
                            {
                                transaction.ManualWasChanged = true;
                                transaction.PreviousManualMode = previousManualMode;
                            }

                            if (!writeAccepted)
                            {
                                Abort(
                                    WorkloadDiagnosticCode.ExternalPriorityAuthority,
                                    "The manual-priority writer rejected the V2 mutation because priority authority changed.");
                            }

                            EnsureRuntimeMutationAuthority(runtimePlan, report, "manual-mode.after-write");
                            if (observedManualMode != runtimePlan.ManualTarget)
                            {
                                Abort(
                                    WorkloadDiagnosticCode.InvalidState,
                                    "The manual-priority writer did not commit its requested value.");
                            }

                            report.Add(
                                WorkloadV2CommitMessageKind.Changed,
                                "manual-mode.changed",
                                "global",
                                "The global manual-priority mode was committed through WorkPrioritySystem.");
                        }
                        else
                        {
                            report.Add(
                                WorkloadV2CommitMessageKind.Unchanged,
                                "manual-mode.unchanged",
                                "global",
                                "The global manual-priority mode already matches the preview at write time.");
                        }
                    }
                }

                for (int i = 0; i < runtimePlan.ParentPriorities.Count; i++)
                {
                    ParentPriorityMutation mutation = runtimePlan.ParentPriorities[i];
                    bool stale;
                    if (!TryResolveWritableEntry(
                            mutation.Key.Pawn,
                            mutation.Key.WorkType,
                            scope,
                            runtime,
                            report,
                            mutation.Key.ToString(),
                            out Pawn pawn,
                            out WorkTypeDef workType,
                            out stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The V2 apply was blocked immediately before mutation because " +
                                mutation.Key + " is stale or missing.");
                        }

                        continue;
                    }

                    ParentPriorityMutation resolvedMutation =
                        new ParentPriorityMutation(
                            mutation.Key,
                            pawn,
                            workType,
                            mutation.DesiredPriority);
                    RevalidateParentBaseline(
                        session,
                        runtimePlan,
                        resolvedMutation,
                        report,
                        mutation.Key.ToString());
                    int previous = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
                    if (previous == mutation.DesiredPriority)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Unchanged,
                            "parent-priority.unchanged",
                            mutation.Key.ToString(),
                            "The current parent priority already matches the preview at write time.");
                        continue;
                    }

                    bool writeAccepted = WorkPrioritySystem.SetPriority(
                            pawn.workSettings,
                            workType,
                            mutation.DesiredPriority);
                    int observed = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
                    if (observed != previous)
                    {
                        transaction.Priorities.Add(new AppliedPriorityMutation(
                            resolvedMutation,
                            previous));
                    }

                    if (!writeAccepted)
                    {
                        Abort(
                            WorkloadDiagnosticCode.ExternalPriorityAuthority,
                            "The parent-priority writer rejected " + mutation.Key +
                            " because priority authority changed.");
                    }

                    EnsureRuntimeMutationAuthority(
                        runtimePlan,
                        report,
                        mutation.Key + ".after-write");
                    if (observed != mutation.DesiredPriority)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The parent-priority writer did not commit " + mutation.Key + ".");
                    }

                    report.Add(
                        WorkloadV2CommitMessageKind.Changed,
                        "parent-priority.changed",
                        mutation.Key.ToString(),
                        "The parent priority was committed through WorkPrioritySystem.");
                }

                for (int i = 0; i < runtimePlan.SpecificJobOverrides.Count; i++)
                {
                    SpecificJobOverrideMutation mutation = runtimePlan.SpecificJobOverrides[i];
                    RevalidateSpecificOverrideBaseline(
                        session,
                        runtimePlan,
                        mutation,
                        report,
                        mutation.Key.ToString());
                    bool hadPrevious = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                        mutation.Pawn,
                        mutation.WorkGiver,
                        out int previousPriority);
                    int previousRevision = WorkGiverReassignmentManager.CurrentSyncVersion;

                    if (mutation.HasAfter)
                    {
                        WorkGiverReassignmentManager.SetPawnOverrideSynced(
                            mutation.Pawn.thingIDNumber,
                            mutation.WorkGiver.defName,
                            mutation.DesiredPriority);
                    }
                    else
                    {
                        WorkGiverReassignmentManager.ClearPawnOverrideSynced(
                            mutation.Pawn.thingIDNumber,
                            mutation.WorkGiver.defName);
                    }

                    bool observedHasOverride =
                        WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                            mutation.Pawn,
                            mutation.WorkGiver,
                            out int observedPriority);
                    if (observedHasOverride != hadPrevious ||
                        (observedHasOverride && observedPriority != previousPriority))
                    {
                        transaction.SpecificJobOverrides.Add(
                            new AppliedSpecificJobOverrideMutation(
                                mutation,
                                hadPrevious,
                                previousPriority));
                    }

                    if (mutation.HasAfter
                        ? !observedHasOverride || observedPriority != mutation.DesiredPriority
                        : observedHasOverride)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The BWT specific-job priority writer did not commit " + mutation.Key + ".");
                    }

                    AdvanceSpecificJobRevision(
                        runtimePlan,
                        report,
                        previousRevision,
                        "specific-job-priority.after-write");
                    transaction.SpecificJobRevision = runtimePlan.SpecificJobRevision;
                    EnsureRuntimeMutationAuthority(
                        runtimePlan,
                        report,
                        mutation.Key + ".after-write");
                    report.Add(
                        WorkloadV2CommitMessageKind.Changed,
                        mutation.HasAfter
                            ? "specific-job-priority.changed"
                            : "specific-job-priority.cleared",
                        mutation.Key.ToString(),
                        mutation.HasAfter
                            ? "The BWT specific-job priority was committed through WorkGiverReassignmentManager."
                            : "The BWT specific-job priority override was cleared exactly.");
                }

                for (int i = 0; i < runtimePlan.SpecificJobOrder.Count; i++)
                {
                    SpecificJobOrderMutation mutation = runtimePlan.SpecificJobOrder[i];
                    RevalidateSpecificOrderBaseline(
                        session,
                        runtimePlan,
                        mutation,
                        report,
                        mutation.Parent.ToString());

                    int previousRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
                    bool writeSucceeded = mutation.DesiredOrder == null
                        ? WorkGiverReassignmentManager.ClearPawnWorkGiverOrderExact(
                            mutation.Pawn.thingIDNumber,
                            mutation.WorkType.defName)
                        : WorkGiverReassignmentManager.SetPawnWorkGiverOrderExact(
                            mutation.Pawn.thingIDNumber,
                            mutation.WorkType.defName,
                            mutation.DesiredOrder);
                    WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot observed =
                        WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                            mutation.Pawn,
                            mutation.WorkType);
                    if (observed.HasStoredOrder != mutation.PreviousSnapshot.HasStoredOrder ||
                        (observed.HasStoredOrder &&
                         !SequenceEqual(
                             observed.OrderedWorkGiverNames,
                             mutation.PreviousSnapshot.OrderedWorkGiverNames)))
                    {
                        transaction.SpecificJobOrder.Add(
                            new AppliedSpecificJobOrderMutation(mutation));
                    }

                    if (!writeSucceeded)
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The BWT specific-job order writer rejected " + mutation.Parent + ".");
                    }

                    if (mutation.DesiredOrder == null
                        ? observed.HasStoredOrder
                        : !observed.HasStoredOrder ||
                          !SequenceEqual(observed.OrderedWorkGiverNames, mutation.DesiredOrder))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The BWT specific-job order writer did not commit " + mutation.Parent + ".");
                    }

                    AdvanceSpecificJobRevision(
                        runtimePlan,
                        report,
                        previousRevision,
                        mutation.Parent + ".after-write");
                    transaction.SpecificJobRevision = runtimePlan.SpecificJobRevision;
                    EnsureRuntimeMutationAuthority(
                        runtimePlan,
                        report,
                        mutation.Parent + ".after-write");

                    report.Add(
                        WorkloadV2CommitMessageKind.Changed,
                        mutation.DesiredOrder == null
                            ? "specific-job-order.cleared"
                            : "specific-job-order.changed",
                        mutation.Parent.ToString(),
                        mutation.DesiredOrder == null
                            ? "The exact BWT pawn-specific work-giver order was cleared."
                            : "The exact BWT pawn-specific work-giver order was committed.");
                }

                if (runtimePlan.RequiresPriorityAuthority)
                {
                    EnsureRuntimeMutationAuthority(runtimePlan, report, "live-apply.final");
                }

                if (runtimePlan.RequiresSpecificJobRevision)
                {
                    EnsureSpecificJobRevision(runtimePlan, report, "live-apply.specific-job.final");
                }
            }
            finally
            {
                if (specificBatch != null)
                {
                    specificBatch.Dispose();
                    WorkGiverReassignmentManager.CommitMutationBatch(
                        notifyDependents: true);
                }
            }
        }

        private static void ValidateSpecificMutationsPrewrite(
            RuntimeCommitPlan plan,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            for (int i = 0; i < plan.SpecificJobOverrides.Count; i++)
            {
                SpecificJobOverrideMutation mutation = plan.SpecificJobOverrides[i];
                if (!TryResolveSpecificWritableEntry(
                        mutation.Key,
                        scope,
                        runtime,
                        report,
                        mutation.Key.ToString(),
                        out Pawn pawn,
                        out WorkTypeDef workType,
                        out WorkGiverDef workGiver,
                        out bool stale) ||
                    pawn != mutation.Pawn ||
                    workType != mutation.WorkType ||
                    workGiver != mutation.WorkGiver)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 apply was blocked before its first write because specific-job priority " +
                        mutation.Key + (stale ? " is stale or missing." : " is no longer coherently writable."));
                }
            }

            for (int i = 0; i < plan.SpecificJobOrder.Count; i++)
            {
                SpecificJobOrderMutation mutation = plan.SpecificJobOrder[i];
                if (!TryResolveWritableEntry(
                        mutation.Parent.Pawn,
                        mutation.Parent.WorkType,
                        scope,
                        runtime,
                        report,
                        mutation.Parent.ToString(),
                        out Pawn pawn,
                        out WorkTypeDef workType,
                        out bool stale) ||
                    pawn != mutation.Pawn ||
                    workType != mutation.WorkType)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 apply was blocked before its first write because specific-job order " +
                        mutation.Parent + (stale ? " is stale or missing." : " is no longer coherently writable."));
                }
            }
        }

        private void ApplyPersistence(
            WorkloadV2PersistenceEnvelope store,
            PersistenceMutation mutation)
        {
            if (store?.Records == null || mutation?.Record == null)
            {
                throw new InvalidOperationException("The V2 persistence target is unavailable.");
            }

            if (mutation.Kind == PersistenceMutationKind.Replace)
            {
                int index = FindRecordIndex(store, mutation.StableId);
                if (index < 0)
                {
                    throw new InvalidOperationException("The V2 update target disappeared before persistence.");
                }

                mutation.Index = index;
                mutation.PreviousRecord = store.Records[index];
                store.Records[index] = mutation.Record;
            }
            else
            {
                store.Records.Add(mutation.Record);
                mutation.AddedRecord = true;
            }

            mutation.WasApplied = true;
        }

        private static void EnsureUpdateTarget(
            WorkloadV2PersistenceEnvelope store,
            string stableId,
            WorkloadV2CommitReport report)
        {
            if (store.HasDuplicateStableId(stableId) || FindRecordIndex(store, stableId) < 0)
            {
                Abort(
                    store.HasDuplicateStableId(stableId)
                        ? WorkloadDiagnosticCode.AmbiguousStableId
                        : WorkloadDiagnosticCode.UnknownWorkloadId,
                    "The V2 update target is no longer unique and present.");
            }
        }

        private static void EnsureForkTargetIsNew(
            WorkloadV2PersistenceEnvelope store,
            string stableId,
            WorkloadV2CommitReport report)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                Abort(
                    WorkloadDiagnosticCode.MissingStableId,
                    "A V2 fork requires a stable ID.");
            }

            if (store.HasDuplicateStableId(stableId) || FindRecordIndex(store, stableId) >= 0)
            {
                Abort(
                    WorkloadDiagnosticCode.AmbiguousStableId,
                    "The requested V2 fork stable ID is already in use.");
            }
        }

        private static int FindRecordIndex(
            WorkloadV2PersistenceEnvelope store,
            string stableId)
        {
            if (store?.Records == null) return -1;
            for (int i = 0; i < store.Records.Count; i++)
            {
                if (StringComparer.Ordinal.Equals(store.Records[i]?.StableId, stableId)) return i;
            }

            return -1;
        }

        private void NotifyCommitChanged(
            WorkloadPreviewPlan plan,
            LiveMutationTransaction live)
        {
            _component.NotifyWorkloadV2Changed();
            WorkTabDirtyFlags flags = 0;
            bool priorityChanged = live != null &&
                (live.ManualWasChanged || live.Priorities.Count > 0);
            bool specificChanged = live != null &&
                (live.SpecificJobOverrides.Count > 0 || live.SpecificJobOrder.Count > 0);
            if (priorityChanged)
            {
                flags |= WorkTabDirtyFlags.Priority | WorkTabDirtyFlags.Presentation;
            }

            if (specificChanged)
            {
                flags |= WorkTabDirtyFlags.SubWorkOverride |
                    WorkTabDirtyFlags.Columns |
                    WorkTabDirtyFlags.HeaderGeometry;
            }

            if (flags == 0)
            {
                // Preserve the existing persistence-only refresh contract.
                flags = WorkTabDirtyFlags.Priority | WorkTabDirtyFlags.Presentation;
            }

            WorkTabInvalidationHub.Invalidate(flags);
        }

        private static bool RollbackPersistence(
            WorkloadV2PersistenceEnvelope store,
            PersistenceMutation mutation,
            WorkloadV2CommitReport report)
        {
            if (mutation == null || !mutation.WasApplied) return true;
            if (store?.Records == null)
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.persistence",
                    mutation.StableId,
                    "The V2 persistence write could not be rolled back because its store disappeared.");
                return false;
            }

            try
            {
                if (mutation.Kind == PersistenceMutationKind.Replace &&
                    mutation.Index >= 0 && mutation.Index < store.Records.Count &&
                    ReferenceEquals(store.Records[mutation.Index], mutation.Record))
                {
                    store.Records[mutation.Index] = mutation.PreviousRecord;
                    return true;
                }

                if (mutation.Kind == PersistenceMutationKind.Add && mutation.AddedRecord)
                {
                    if (store.Records.Remove(mutation.Record)) return true;
                }

                if (mutation.Kind == PersistenceMutationKind.Replace &&
                    mutation.Index >= 0 && mutation.Index < store.Records.Count &&
                    ReferenceEquals(store.Records[mutation.Index], mutation.PreviousRecord))
                {
                    return true;
                }

                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.persistence.conflict",
                    mutation.StableId,
                    "The V2 persistence target changed during rollback; the previous record was not restored.");
                return false;
            }
            catch (Exception exception)
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.persistence.failed",
                    mutation.StableId,
                    "The V2 persistence rollback failed: " + exception.Message);
                return false;
            }
        }

        private static bool RollbackLive(
            LiveMutationTransaction transaction,
            WorkloadV2CommitReport report)
        {
            if (transaction == null || !transaction.HasChanges) return true;
            if (transaction.RollbackAttempted) return !HasLiveNetChanges(transaction);

            transaction.RollbackAttempted = true;
            bool restored = true;

            try
            {
                bool hasPriorityChanges = transaction.ManualWasChanged || transaction.Priorities.Count > 0;
                bool hasSpecificChanges = transaction.SpecificJobOverrides.Count > 0 ||
                    transaction.SpecificJobOrder.Count > 0;
                bool requiresPriorityAuthority = hasPriorityChanges || hasSpecificChanges;
                if (requiresPriorityAuthority &&
                    (!transaction.HasAuthorityRevision ||
                     !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision)))
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Fatal,
                        "rollback.authority",
                        "priority-authority",
                        "Live V2 rollback was not attempted because priority authority changed externally.");
                    restored = false;
                }

                if (hasSpecificChanges &&
                    (!transaction.HasSpecificJobRevision ||
                     WorkGiverReassignmentManager.CurrentSyncVersion != transaction.SpecificJobRevision))
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Fatal,
                        "rollback.specific-job.revision",
                        "specific-job",
                        "Specific-job rollback was not attempted because BWT work-giver state changed externally.");
                    restored = false;
                }

                if (hasSpecificChanges && restored && transaction.HasSpecificJobRevision &&
                    WorkGiverReassignmentManager.CurrentSyncVersion == transaction.SpecificJobRevision)
                {
                    for (int i = transaction.SpecificJobOrder.Count - 1; i >= 0; i--)
                    {
                        AppliedSpecificJobOrderMutation applied = transaction.SpecificJobOrder[i];
                        if (!RestoreSpecificJobOrder(
                                transaction,
                                applied.Mutation,
                                report,
                                "rollback.specific-job-order"))
                        {
                            restored = false;
                            break;
                        }
                    }

                    if (restored)
                    {
                        for (int i = transaction.SpecificJobOverrides.Count - 1; i >= 0; i--)
                        {
                            AppliedSpecificJobOverrideMutation applied = transaction.SpecificJobOverrides[i];
                            if (!RestoreSpecificJobOverride(
                                    transaction,
                                    applied,
                                    report,
                                    "rollback.specific-job-priority"))
                            {
                                restored = false;
                                break;
                            }
                        }
                    }
                }

                if (hasPriorityChanges && restored)
                {
                    if (!transaction.HasAuthorityRevision ||
                        !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Fatal,
                            "rollback.authority",
                            "live-state",
                            "Live V2 rollback stopped because priority authority changed during rollback.");
                        restored = false;
                    }
                    else
                    {
                        for (int i = transaction.Priorities.Count - 1; i >= 0; i--)
                        {
                            AppliedPriorityMutation mutation = transaction.Priorities[i];
                            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision) ||
                                !WorkPrioritySystem.SetPriority(
                                    mutation.Mutation.Pawn.workSettings,
                                    mutation.Mutation.WorkType,
                                    mutation.PreviousPriority) ||
                                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision) ||
                                PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                                    mutation.Mutation.Pawn.workSettings,
                                    mutation.Mutation.WorkType) != mutation.PreviousPriority)
                            {
                                report.Add(
                                    WorkloadV2CommitMessageKind.Fatal,
                                    "rollback.authority",
                                    "live-state",
                                    "Live V2 rollback stopped because priority authority changed during rollback.");
                                restored = false;
                                break;
                            }
                        }

                        if (restored && transaction.ManualWasChanged)
                        {
                            if (Verse.Find.PlaySettings == null ||
                                !WorkPrioritySystem.SetManualPriorities(transaction.PreviousManualMode) ||
                                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision) ||
                                Verse.Find.PlaySettings.useWorkPriorities != transaction.PreviousManualMode)
                            {
                                report.Add(
                                    WorkloadV2CommitMessageKind.Fatal,
                                    "rollback.authority",
                                    "manual-mode",
                                    "Live V2 manual-mode rollback stopped because priority authority changed.");
                                restored = false;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.failed",
                    "live-state",
                    "The V2 live-state rollback failed: " + exception.Message);
                restored = false;
            }

            return !HasLiveNetChanges(transaction);
        }

        private static bool HasLiveNetChanges(LiveMutationTransaction transaction)
        {
            if (transaction == null || !transaction.HasChanges) return false;
            try
            {
                if (transaction.ManualWasChanged &&
                    (Verse.Find.PlaySettings == null ||
                     Verse.Find.PlaySettings.useWorkPriorities != transaction.PreviousManualMode))
                {
                    return true;
                }

                for (int i = 0; i < transaction.Priorities.Count; i++)
                {
                    AppliedPriorityMutation applied = transaction.Priorities[i];
                    if (applied.Mutation.Pawn?.workSettings == null ||
                        PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                            applied.Mutation.Pawn.workSettings,
                            applied.Mutation.WorkType) != applied.PreviousPriority)
                    {
                        return true;
                    }
                }

                for (int i = 0; i < transaction.SpecificJobOverrides.Count; i++)
                {
                    AppliedSpecificJobOverrideMutation applied =
                        transaction.SpecificJobOverrides[i];
                    bool hasOverride =
                        WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                            applied.Mutation.Pawn,
                            applied.Mutation.WorkGiver,
                            out int priority);
                    if (hasOverride != applied.HadPrevious ||
                        (hasOverride && priority != applied.PreviousPriority))
                    {
                        return true;
                    }
                }

                for (int i = 0; i < transaction.SpecificJobOrder.Count; i++)
                {
                    SpecificJobOrderMutation mutation =
                        transaction.SpecificJobOrder[i].Mutation;
                    WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot current =
                        WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                            mutation.Pawn,
                            mutation.WorkType);
                    if (current.HasStoredOrder != mutation.PreviousSnapshot.HasStoredOrder ||
                        (current.HasStoredOrder &&
                         !SequenceEqual(
                             current.OrderedWorkGiverNames,
                             mutation.PreviousSnapshot.OrderedWorkGiverNames)))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool RestoreSpecificJobOverride(
            LiveMutationTransaction transaction,
            AppliedSpecificJobOverrideMutation applied,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (!transaction.HasAuthorityRevision ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.authority",
                    subject,
                    "BWT priority authority changed during specific-job rollback.");
                return false;
            }

            if (WorkGiverReassignmentManager.CurrentSyncVersion != transaction.SpecificJobRevision)
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.specific-job.revision",
                    subject,
                    "BWT work-giver state changed during specific-job rollback.");
                return false;
            }

            int previousRevision = transaction.SpecificJobRevision;
            if (applied.HadPrevious)
            {
                WorkGiverReassignmentManager.SetPawnOverrideSynced(
                    applied.Mutation.Pawn.thingIDNumber,
                    applied.Mutation.WorkGiver.defName,
                    applied.PreviousPriority);
            }
            else
            {
                WorkGiverReassignmentManager.ClearPawnOverrideSynced(
                    applied.Mutation.Pawn.thingIDNumber,
                    applied.Mutation.WorkGiver.defName);
            }

            int observedRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision) ||
                observedRevision != unchecked(previousRevision + 1))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.specific-job.revision",
                    subject,
                    "BWT work-giver state changed unexpectedly during specific-job rollback.");
                return false;
            }

            bool hasOverride = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                applied.Mutation.Pawn,
                applied.Mutation.WorkGiver,
                out int observedPriority);
            if (hasOverride != applied.HadPrevious ||
                (hasOverride && observedPriority != applied.PreviousPriority))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.specific-job.failed",
                    subject,
                    "The exact BWT specific-job priority could not be restored.");
                return false;
            }

            transaction.SpecificJobRevision = observedRevision;
            return true;
        }

        private static bool RestoreSpecificJobOrder(
            LiveMutationTransaction transaction,
            SpecificJobOrderMutation mutation,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (!transaction.HasAuthorityRevision ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.authority",
                    subject,
                    "BWT priority authority changed during specific-job order rollback.");
                return false;
            }

            if (WorkGiverReassignmentManager.CurrentSyncVersion != transaction.SpecificJobRevision)
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.specific-job.revision",
                    subject,
                    "BWT work-giver state changed during specific-job rollback.");
                return false;
            }

            int previousRevision = transaction.SpecificJobRevision;
            if (!WorkGiverReassignmentManager.RestorePawnWorkGiverOrderSnapshot(
                    mutation.PreviousSnapshot))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.specific-job-order.failed",
                    subject,
                    "The exact BWT pawn-specific work-giver order could not be restored.");
                return false;
            }

            int observedRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision) ||
                observedRevision != unchecked(previousRevision + 1))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.specific-job.revision",
                    subject,
                    "BWT work-giver state changed unexpectedly during specific-job rollback.");
                return false;
            }

            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot observed =
                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                    mutation.Pawn,
                    mutation.WorkType);
            if (observed.HasStoredOrder != mutation.PreviousSnapshot.HasStoredOrder ||
                (observed.HasStoredOrder &&
                 !SequenceEqual(
                     observed.OrderedWorkGiverNames,
                     mutation.PreviousSnapshot.OrderedWorkGiverNames)))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.specific-job-order.failed",
                    subject,
                    "The exact BWT pawn-specific work-giver order could not be restored.");
                return false;
            }

            transaction.SpecificJobRevision = observedRevision;
            return true;
        }

        private static void Abort(
            WorkloadDiagnosticCode code,
            string message)
        {
            throw new CommitAbortException(code, message);
        }

        private sealed class CommitAbortException : Exception
        {
            internal CommitAbortException(WorkloadDiagnosticCode code, string message)
                : base(message)
            {
                Code = code;
            }

            internal WorkloadDiagnosticCode Code { get; private set; }
        }

        private sealed class RuntimeContext
        {
            internal RuntimeContext(
                Dictionary<string, Pawn> pawns,
                Dictionary<string, WorkTypeDef> workTypes,
                Dictionary<string, WorkGiverDef> workGivers)
            {
                Pawns = pawns;
                WorkTypes = workTypes;
                WorkGivers = workGivers;
            }

            internal Dictionary<string, Pawn> Pawns { get; private set; }
            internal Dictionary<string, WorkTypeDef> WorkTypes { get; private set; }
            internal Dictionary<string, WorkGiverDef> WorkGivers { get; private set; }

            internal bool IsCurrentMapFreeColonist(Pawn pawn)
            {
                return pawn != null &&
                       Verse.Find.CurrentMap?.mapPawns?.FreeColonists != null &&
                       Verse.Find.CurrentMap.mapPawns.FreeColonists.Contains(pawn);
            }
        }

        private sealed class RuntimeCommitPlan
        {
            internal readonly List<ParentPriorityMutation> ParentPriorities =
                new List<ParentPriorityMutation>();
            internal readonly List<SpecificJobOverrideMutation> SpecificJobOverrides =
                new List<SpecificJobOverrideMutation>();
            internal readonly List<SpecificJobOrderMutation> SpecificJobOrder =
                new List<SpecificJobOrderMutation>();
            internal readonly List<WorkloadParentPriorityKey> ManualKeys =
                new List<WorkloadParentPriorityKey>();
            internal long AuthorityRevision;
            internal bool HasAuthorityRevision;
            internal bool RequiresPriorityAuthority;
            internal int SpecificJobRevision;
            internal bool HasSpecificJobRevision;
            internal bool RequiresSpecificJobRevision;
            internal bool HasManualTarget;
            internal bool ManualTarget;
            internal bool HasLiveMutations => HasManualTarget ||
                ParentPriorities.Count > 0 ||
                SpecificJobOverrides.Count > 0 ||
                SpecificJobOrder.Count > 0;
        }

        private sealed class ParentPriorityDifference
        {
            internal ParentPriorityDifference(
                WorkloadParentPriorityKey key,
                bool hasAfter,
                int after)
            {
                Key = key;
                HasAfter = hasAfter;
                After = after;
            }

            internal WorkloadParentPriorityKey Key { get; private set; }
            internal bool HasAfter { get; private set; }
            internal int After { get; private set; }
        }

        private sealed class ManualModeDifference
        {
            internal ManualModeDifference(
                WorkloadParentPriorityKey key,
                bool hasAfter,
                bool after)
            {
                Key = key;
                HasAfter = hasAfter;
                After = after;
            }

            internal WorkloadParentPriorityKey Key { get; private set; }
            internal bool HasAfter { get; private set; }
            internal bool After { get; private set; }
        }

        private sealed class SpecificJobOverrideDifference
        {
            internal SpecificJobOverrideDifference(
                WorkloadSpecificJobKey key,
                bool hasAfter,
                WorkloadScalarValue after)
            {
                Key = key;
                HasAfter = hasAfter;
                After = after;
            }

            internal WorkloadSpecificJobKey Key { get; private set; }
            internal bool HasAfter { get; private set; }
            internal WorkloadScalarValue After { get; private set; }
        }

        private sealed class SpecificJobOrderDifference
        {
            internal SpecificJobOrderDifference(
                WorkloadSpecificJobKey key,
                bool hasAfter,
                int after)
            {
                Key = key;
                HasAfter = hasAfter;
                After = after;
            }

            internal WorkloadSpecificJobKey Key { get; private set; }
            internal bool HasAfter { get; private set; }
            internal int After { get; private set; }
        }

        private sealed class ParentPriorityMutation
        {
            internal ParentPriorityMutation(
                WorkloadParentPriorityKey key,
                Pawn pawn,
                WorkTypeDef workType,
                int desiredPriority)
            {
                Key = key;
                Pawn = pawn;
                WorkType = workType;
                DesiredPriority = desiredPriority;
            }

            internal WorkloadParentPriorityKey Key { get; private set; }
            internal Pawn Pawn { get; private set; }
            internal WorkTypeDef WorkType { get; private set; }
            internal int DesiredPriority { get; private set; }
        }

        private sealed class AppliedPriorityMutation
        {
            internal AppliedPriorityMutation(
                ParentPriorityMutation mutation,
                int previousPriority)
            {
                Mutation = mutation;
                PreviousPriority = previousPriority;
            }

            internal ParentPriorityMutation Mutation { get; private set; }
            internal int PreviousPriority { get; private set; }
        }

        private sealed class SpecificJobOverrideMutation
        {
            internal SpecificJobOverrideMutation(
                WorkloadSpecificJobKey key,
                Pawn pawn,
                WorkTypeDef workType,
                WorkGiverDef workGiver,
                bool hasAfter,
                int desiredPriority)
            {
                Key = key;
                Pawn = pawn;
                WorkType = workType;
                WorkGiver = workGiver;
                HasAfter = hasAfter;
                DesiredPriority = desiredPriority;
            }

            internal WorkloadSpecificJobKey Key { get; private set; }
            internal Pawn Pawn { get; private set; }
            internal WorkTypeDef WorkType { get; private set; }
            internal WorkGiverDef WorkGiver { get; private set; }
            internal bool HasAfter { get; private set; }
            internal int DesiredPriority { get; private set; }
        }

        private sealed class AppliedSpecificJobOverrideMutation
        {
            internal AppliedSpecificJobOverrideMutation(
                SpecificJobOverrideMutation mutation,
                bool hadPrevious,
                int previousPriority)
            {
                Mutation = mutation;
                HadPrevious = hadPrevious;
                PreviousPriority = previousPriority;
            }

            internal SpecificJobOverrideMutation Mutation { get; private set; }
            internal bool HadPrevious { get; private set; }
            internal int PreviousPriority { get; private set; }
        }

        private sealed class SpecificJobOrderMutation
        {
            internal SpecificJobOrderMutation(
                WorkloadParentPriorityKey parent,
                Pawn pawn,
                WorkTypeDef workType,
                IReadOnlyList<string> desiredOrder,
                WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot previousSnapshot)
            {
                Parent = parent;
                Pawn = pawn;
                WorkType = workType;
                DesiredOrder = desiredOrder == null ? null : new List<string>(desiredOrder);
                PreviousSnapshot = previousSnapshot;
            }

            internal WorkloadParentPriorityKey Parent { get; private set; }
            internal Pawn Pawn { get; private set; }
            internal WorkTypeDef WorkType { get; private set; }
            internal IReadOnlyList<string> DesiredOrder { get; private set; }
            internal WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot PreviousSnapshot { get; private set; }
        }

        private sealed class AppliedSpecificJobOrderMutation
        {
            internal AppliedSpecificJobOrderMutation(SpecificJobOrderMutation mutation)
            {
                Mutation = mutation;
            }

            internal SpecificJobOrderMutation Mutation { get; private set; }
        }

        private sealed class LiveMutationTransaction
        {
            internal LiveMutationTransaction(RuntimeCommitPlan plan)
            {
                AuthorityRevision = plan?.AuthorityRevision ?? 0L;
                HasAuthorityRevision = plan?.HasAuthorityRevision == true;
                SpecificJobRevision = plan?.SpecificJobRevision ?? 0;
                HasSpecificJobRevision = plan?.HasSpecificJobRevision == true;
            }

            internal readonly List<AppliedPriorityMutation> Priorities =
                new List<AppliedPriorityMutation>();
            internal readonly List<AppliedSpecificJobOverrideMutation> SpecificJobOverrides =
                new List<AppliedSpecificJobOverrideMutation>();
            internal readonly List<AppliedSpecificJobOrderMutation> SpecificJobOrder =
                new List<AppliedSpecificJobOrderMutation>();
            internal long AuthorityRevision;
            internal bool HasAuthorityRevision;
            internal int SpecificJobRevision;
            internal bool HasSpecificJobRevision;
            internal bool RollbackAttempted;
            internal bool ManualWasChanged;
            internal bool PreviousManualMode;
            internal bool HasChanges => ManualWasChanged ||
                Priorities.Count > 0 ||
                SpecificJobOverrides.Count > 0 ||
                SpecificJobOrder.Count > 0;
        }

        private enum PersistenceMutationKind
        {
            Replace = 0,
            Add = 1
        }

        private sealed class PersistenceMutation
        {
            internal PersistenceMutation(
                PersistenceMutationKind kind,
                string stableId,
                WorkloadV2PersistenceRecord record)
            {
                Kind = kind;
                StableId = stableId ?? string.Empty;
                Record = record;
                Index = -1;
            }

            internal PersistenceMutationKind Kind { get; private set; }
            internal string StableId { get; private set; }
            internal WorkloadV2PersistenceRecord Record { get; private set; }
            internal WorkloadV2PersistenceRecord PreviousRecord { get; set; }
            internal int Index { get; set; }
            internal bool AddedRecord { get; set; }
            internal bool WasApplied { get; set; }
        }
    }
}
