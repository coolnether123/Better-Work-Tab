using System;
using System.Collections.Generic;
using System.Globalization;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
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
            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(ready.Code, ready.Message);
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

            var priorities = new List<WorkloadParentPriorityEntry>();
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

                        // Read BWT's stored/base dimension only. This is a read
                        // through the authority broker and never mutates live state.
                        int priority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                            pawn.workSettings,
                            workType);
                        priorities.Add(new WorkloadParentPriorityEntry(
                            new PawnKey(pawnId),
                            new WorkTypeKey(workType.defName),
                            priority));
                    }
                }
            }

            var definition = new WorkloadDefinition(
                stableId,
                string.IsNullOrWhiteSpace(label) ? GetDefaultLabel() : label,
                WorkloadSchema.CurrentVersion,
                WorkloadOwnershipDimensions.ParentPriorities,
                WorkloadScope.CurrentMapFreeColonists());
            return WorkloadOperationResult<WorkloadTemplate>.Ok(
                new WorkloadTemplate(definition, new WorkloadProjectedState(parentPriorities: priorities)));
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

            WorkloadOperationResult<WorkloadProjectedState> liveBaseline =
                _applyService.CaptureLiveBaseline(template.Value);
            if (!liveBaseline.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    liveBaseline.Code,
                    liveBaseline.Message);
            }

            WorkloadSession opened = WorkloadSession.OpenCaptured(
                template.Value,
                liveBaseline.Value,
                WorkloadSession.GetSourceIdentity(template.Value));
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

            _previewSession = candidate;
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
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
            if (template == null)
            {
                return WorkloadOperationResult<WorkloadProjectedState>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 live baseline cannot be captured without a template.");
            }

            WorkloadProjectedState templateState =
                template.ProjectedState ?? WorkloadProjectedState.Empty;
            WorkloadOwnershipDimensions ownership =
                template.Definition.OwnershipDimensions;
            bool captureParentPriorities =
                ownership.Owns(WorkloadStateDimension.ParentPriorities) &&
                templateState.ParentPriorities.Count > 0;
            bool captureManualModes =
                ownership.Owns(WorkloadStateDimension.ManualModes) &&
                templateState.ManualModes.Count > 0;

            if (!captureParentPriorities && !captureManualModes)
            {
                return WorkloadOperationResult<WorkloadProjectedState>.Ok(templateState);
            }

            var report = new WorkloadV2CommitReport(
                WorkloadDecisionKind.Apply,
                template.StableId,
                template.StableId);
            try
            {
                RuntimeContext runtime = BuildRuntimeContext(template, report);
                // A baseline is only useful if the same BWT-owned values can
                // later be written through the authority boundary.
                EnsureBetterWorkTabAuthority(report);

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

                var livePriorities = new List<WorkloadParentPriorityEntry>();
                if (captureParentPriorities)
                {
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

                        int priority;
                        try
                        {
                            // Read BWT's stored value, not an external/effective
                            // mirror. This is a pure read and never mutates the
                            // pawn or its work settings.
                            priority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                                pawn.workSettings,
                                workType);
                        }
                        catch (Exception exception)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The live baseline for " + entry.Key +
                                " could not be read safely: " + exception.Message);
                            priority = 0;
                        }

                        livePriorities.Add(new WorkloadParentPriorityEntry(entry.Key, priority));
                    }
                }

                var liveManualModes = new List<WorkloadManualModeEntry>();
                if (captureManualModes)
                {
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

                        liveManualModes.Add(
                            new WorkloadManualModeEntry(entry.Key, liveManualMode));
                    }
                }

                return WorkloadOperationResult<WorkloadProjectedState>.Ok(
                    WithLiveBaselineValues(
                        templateState,
                        captureParentPriorities
                            ? livePriorities
                            : templateState.ParentPriorities,
                        captureManualModes
                            ? liveManualModes
                            : templateState.ManualModes));
            }
            catch (CommitAbortException exception)
            {
                return WorkloadOperationResult<WorkloadProjectedState>.Fail(
                    exception.Code,
                    exception.Message);
            }
            catch (Exception exception)
            {
                return WorkloadOperationResult<WorkloadProjectedState>.Fail(
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

        private static WorkloadProjectedState WithLiveBaselineValues(
            WorkloadProjectedState templateState,
            IReadOnlyList<WorkloadParentPriorityEntry> parentPriorities,
            IReadOnlyList<WorkloadManualModeEntry> manualModes)
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
            for (int i = 0; i < templateState.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = templateState.SpecificJobOverrides[i];
                overrides[entry.Key] = entry.Value;
            }

            var order = new Dictionary<WorkloadSpecificJobKey, int>();
            for (int i = 0; i < templateState.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = templateState.SpecificJobOrder[i];
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
                var runtimePlan = new RuntimeCommitPlan();
                if (decisionKind == WorkloadDecisionKind.Apply)
                {
                    RuntimeContext runtime = BuildRuntimeContext(targetTemplate, report);
                    ValidateScopeEntries(scope, runtime, report);
                    ValidateRuntimeEntries(
                        targetTemplate,
                        scope,
                        runtime,
                        report);

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
                    EnsureBetterWorkTabAuthority(report);
                    live = ApplyLive(runtimePlan, targetTemplate, report);
                    report.LiveStateChanged = live.HasChanges;
                }

                if (runtimePlan.HasLiveMutations && persistence != null)
                {
                    // Do not let a live priority commit cross into template persistence after
                    // the authority generation has changed.  This closes the gap between the
                    // final live-write check and the persistence writer; RollbackLive will only
                    // reverse the live writes if the same BWT authority is still valid.
                    EnsureRuntimeMutationAuthority(runtimePlan, report, "persistence");
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
                        RollbackLive(live, report);
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 workload persistence write failed: " + exception.Message);
                    }
                }

                if (report.LiveStateChanged || report.TemplatePersisted)
                {
                    NotifyCommitChanged();
                }

                report.IsSemanticNoOp = plan.Diff.IsEmpty;
                string temporaryScopeNote = decisionKind != WorkloadDecisionKind.Apply &&
                    session.SessionExcludedPawnIds.Count > 0
                    ? " Temporary pawn exclusions were not saved."
                    : string.Empty;
                string successMessage = decisionKind == WorkloadDecisionKind.Apply
                    ? "The V2 workload was applied."
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
                RollbackPersistence(store, persistence);
                RollbackLive(live, report);
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
                RollbackPersistence(store, persistence);
                RollbackLive(live, report);
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

            return new RuntimeContext(pawns, workTypes);
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
                WorkloadStateDimension.SpecificJobOverrides,
                WorkloadStateDimension.SpecificJobOrder,
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
            WorkloadTemplate targetTemplate,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            WorkloadOwnershipDimensions ownership = targetTemplate.Definition.OwnershipDimensions;
            WorkloadProjectedState state = targetTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            if (ownership.Owns(WorkloadStateDimension.ParentPriorities))
            {
                for (int i = 0; i < state.ParentPriorities.Count; i++)
                {
                    WorkloadParentPriorityEntry entry = state.ParentPriorities[i];
                    TryResolveWritableEntry(
                        entry.Key.Pawn,
                        entry.Key.WorkType,
                        scope,
                        runtime,
                        report,
                        entry.Key.ToString(),
                        out Pawn unusedPawn,
                        out WorkTypeDef unusedWorkType);
                }
            }

            if (ownership.Owns(WorkloadStateDimension.ManualModes))
            {
                for (int i = 0; i < state.ManualModes.Count; i++)
                {
                    WorkloadManualModeEntry entry = state.ManualModes[i];
                    TryResolveWritableEntry(
                        entry.Key.Pawn,
                        entry.Key.WorkType,
                        scope,
                        runtime,
                        report,
                        entry.Key.ToString(),
                        out Pawn unusedPawn,
                        out WorkTypeDef unusedWorkType);
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
                            out workType))
                    {
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
                bool hasManualTarget = false;
                for (int i = 0; i < differences.Count; i++)
                {
                    if (differences[i].HasAfter)
                    {
                        hasManualTarget = true;
                        break;
                    }
                }

                if (hasManualTarget && !IsGenuinelyGlobalManualScope(scope))
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
                            out workType))
                    {
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
                   scope.ExcludedPawnIds.Count == 0;
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
            pawn = null;
            workType = null;
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
            return BuildSortedMapDifferences(
                before.ParentPriorities,
                after.ParentPriorities,
                entry => entry.Key,
                entry => entry.Priority,
                (key, hasAfter, afterValue) =>
                    new ParentPriorityDifference(key, hasAfter, afterValue));
        }

        private static List<ManualModeDifference> BuildManualModeDifferences(
            WorkloadProjectedState before,
            WorkloadProjectedState after)
        {
            return BuildSortedMapDifferences(
                before.ManualModes,
                after.ManualModes,
                entry => entry.Key,
                entry => entry.Manual,
                (key, hasAfter, afterValue) =>
                    new ManualModeDifference(key, hasAfter, afterValue));
        }

        private static List<TDifference> BuildSortedMapDifferences<TEntry, TValue, TDifference>(
            IReadOnlyList<TEntry> beforeEntries,
            IReadOnlyList<TEntry> afterEntries,
            Func<TEntry, WorkloadParentPriorityKey> getKey,
            Func<TEntry, TValue> getValue,
            Func<WorkloadParentPriorityKey, bool, TValue, TDifference> createDifference)
        {
            var beforeValues = new Dictionary<WorkloadParentPriorityKey, TValue>();
            var afterValues = new Dictionary<WorkloadParentPriorityKey, TValue>();
            for (int i = 0; i < beforeEntries.Count; i++)
            {
                TEntry entry = beforeEntries[i];
                beforeValues[getKey(entry)] = getValue(entry);
            }

            for (int i = 0; i < afterEntries.Count; i++)
            {
                TEntry entry = afterEntries[i];
                afterValues[getKey(entry)] = getValue(entry);
            }

            var keys = new HashSet<WorkloadParentPriorityKey>();
            foreach (WorkloadParentPriorityKey key in beforeValues.Keys) keys.Add(key);
            foreach (WorkloadParentPriorityKey key in afterValues.Keys) keys.Add(key);
            var ordered = new List<WorkloadParentPriorityKey>(keys);
            ordered.Sort((left, right) => left.CompareTo(right));

            var result = new List<TDifference>();
            for (int i = 0; i < ordered.Count; i++)
            {
                WorkloadParentPriorityKey key = ordered[i];
                TValue beforeValue;
                TValue afterValue;
                bool hasBefore = beforeValues.TryGetValue(key, out beforeValue);
                bool hasAfter = afterValues.TryGetValue(key, out afterValue);
                if (hasBefore && hasAfter && EqualityComparer<TValue>.Default.Equals(beforeValue, afterValue)) continue;
                result.Add(createDifference(key, hasAfter, afterValue));
            }

            return result;
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

        private LiveMutationTransaction ApplyLive(
            RuntimeCommitPlan runtimePlan,
            WorkloadTemplate targetTemplate,
            WorkloadV2CommitReport report)
        {
            var transaction = new LiveMutationTransaction();
            transaction.AuthorityRevision = runtimePlan?.AuthorityRevision ?? 0L;
            transaction.HasAuthorityRevision = runtimePlan?.HasAuthorityRevision == true;
            WorkloadScope scope = targetTemplate.Definition.Scope ?? WorkloadScope.Empty;
            try
            {
                EnsureRuntimeMutationAuthority(runtimePlan, report, "live-apply.start");
                transaction.AuthorityRevision = runtimePlan.AuthorityRevision;
                transaction.HasAuthorityRevision = runtimePlan.HasAuthorityRevision;

                if (runtimePlan.HasManualTarget)
                {
                    bool hasWritableManualEntry = false;
                    RuntimeContext manualRuntime = BuildRuntimeContext(targetTemplate, report);
                    for (int i = 0; i < runtimePlan.ManualKeys.Count; i++)
                    {
                        Pawn pawn;
                        WorkTypeDef workType;
                        if (TryResolveWritableEntry(
                                runtimePlan.ManualKeys[i].Pawn,
                                runtimePlan.ManualKeys[i].WorkType,
                                scope,
                                manualRuntime,
                                report,
                                runtimePlan.ManualKeys[i].ToString(),
                                out pawn,
                                out workType))
                        {
                            hasWritableManualEntry = true;
                        }
                    }

                    if (hasWritableManualEntry)
                    {
                        EnsureBetterWorkTabAuthority(report);
                        if (Verse.Find.PlaySettings == null)
                        {
                            Abort(
                                WorkloadDiagnosticCode.NoCurrentGame,
                                "Manual-priority state is unavailable at commit time.");
                        }

                        bool previousManualMode = Verse.Find.PlaySettings.useWorkPriorities;
                        if (previousManualMode != runtimePlan.ManualTarget)
                        {
                            EnsureRuntimeMutationAuthority(runtimePlan, report, "manual-mode");
                            transaction.ManualWasChanged = true;
                            transaction.PreviousManualMode = previousManualMode;
                            if (!WorkPrioritySystem.SetManualPriorities(runtimePlan.ManualTarget))
                            {
                                Abort(
                                    WorkloadDiagnosticCode.ExternalPriorityAuthority,
                                    "The manual-priority writer rejected the V2 mutation because priority authority changed.");
                            }

                            EnsureRuntimeMutationAuthority(runtimePlan, report, "manual-mode.after-write");
                            if (Verse.Find.PlaySettings.useWorkPriorities != runtimePlan.ManualTarget)
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
                    RuntimeContext parentRuntime = BuildRuntimeContext(targetTemplate, report);
                    Pawn pawn;
                    WorkTypeDef workType;
                    if (!TryResolveWritableEntry(
                            mutation.Key.Pawn,
                            mutation.Key.WorkType,
                            scope,
                            parentRuntime,
                            report,
                            mutation.Key.ToString(),
                            out pawn,
                            out workType))
                    {
                        continue;
                    }

                    EnsureRuntimeMutationAuthority(runtimePlan, report, mutation.Key.ToString());
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

                    transaction.Priorities.Add(new AppliedPriorityMutation(
                        new ParentPriorityMutation(
                            mutation.Key,
                            pawn,
                            workType,
                            mutation.DesiredPriority),
                        previous));

                    if (!WorkPrioritySystem.SetPriority(
                            pawn.workSettings,
                            workType,
                            mutation.DesiredPriority))
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
                    int observed = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
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

                EnsureRuntimeMutationAuthority(runtimePlan, report, "live-apply.final");

                return transaction;
            }
            catch
            {
                RollbackLive(transaction, report);
                throw;
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

        private void NotifyCommitChanged()
        {
            _component.NotifyWorkloadV2Changed();
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.Priority | WorkTabDirtyFlags.Presentation);
        }

        private static void RollbackPersistence(
            WorkloadV2PersistenceEnvelope store,
            PersistenceMutation mutation)
        {
            if (store?.Records == null || mutation == null) return;

            try
            {
                if (mutation.Kind == PersistenceMutationKind.Replace &&
                    mutation.Index >= 0 && mutation.Index < store.Records.Count &&
                    ReferenceEquals(store.Records[mutation.Index], mutation.Record))
                {
                    store.Records[mutation.Index] = mutation.PreviousRecord;
                }
                else if (mutation.Kind == PersistenceMutationKind.Add && mutation.AddedRecord)
                {
                    store.Records.Remove(mutation.Record);
                }
            }
            catch
            {
                // The commit result still reports the original persistence
                // failure. There is no safe second writer for a broken store.
            }
        }

        private static void RollbackLive(
            LiveMutationTransaction transaction,
            WorkloadV2CommitReport report)
        {
            if (transaction == null || transaction.RollbackAttempted) return;

            transaction.RollbackAttempted = true;

            try
            {
                if (!transaction.HasAuthorityRevision ||
                    !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision))
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Fatal,
                        "rollback.authority",
                        "priority-authority",
                        "Live V2 rollback was not attempted because priority authority changed externally.");
                    return;
                }

                for (int i = transaction.Priorities.Count - 1; i >= 0; i--)
                {
                    AppliedPriorityMutation mutation = transaction.Priorities[i];
                    if (!WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision) ||
                        !WorkPrioritySystem.SetPriority(
                            mutation.Mutation.Pawn.workSettings,
                            mutation.Mutation.WorkType,
                            mutation.PreviousPriority) ||
                        !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Fatal,
                            "rollback.authority",
                            "live-state",
                            "Live V2 rollback stopped because priority authority changed during rollback.");
                        return;
                    }
                }

                if (transaction.ManualWasChanged && Verse.Find.PlaySettings != null)
                {
                    if (!WorkPrioritySystem.SetManualPriorities(transaction.PreviousManualMode) ||
                        !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(transaction.AuthorityRevision))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Fatal,
                            "rollback.authority",
                            "manual-mode",
                            "Live V2 manual-mode rollback stopped because priority authority changed.");
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
            }
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
                Dictionary<string, WorkTypeDef> workTypes)
            {
                Pawns = pawns;
                WorkTypes = workTypes;
            }

            internal Dictionary<string, Pawn> Pawns { get; private set; }
            internal Dictionary<string, WorkTypeDef> WorkTypes { get; private set; }

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
            internal readonly List<WorkloadParentPriorityKey> ManualKeys =
                new List<WorkloadParentPriorityKey>();
            internal long AuthorityRevision;
            internal bool HasAuthorityRevision;
            internal bool RequiresPriorityAuthority;
            internal bool HasManualTarget;
            internal bool ManualTarget;
            internal bool HasLiveMutations => HasManualTarget || ParentPriorities.Count > 0;
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

        private sealed class LiveMutationTransaction
        {
            internal readonly List<AppliedPriorityMutation> Priorities =
                new List<AppliedPriorityMutation>();
            internal long AuthorityRevision;
            internal bool HasAuthorityRevision;
            internal bool RollbackAttempted;
            internal bool ManualWasChanged;
            internal bool PreviousManualMode;
            internal bool HasChanges => ManualWasChanged || Priorities.Count > 0;
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
        }
    }
}
