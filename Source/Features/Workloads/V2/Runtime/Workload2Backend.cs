using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using Better_Work_Tab.UI.Settings;
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
        Fatal = 6,
        Cleared = 7
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
        private readonly List<WorkloadV2CommitMessage> _cleared =
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
        public int ClearedCount => _cleared.Count;

        public IReadOnlyList<WorkloadV2CommitMessage> Messages => _messages.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Changed => _changed.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Unchanged => _unchanged.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Skipped => _skipped.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Excluded => _excluded.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> MissingOrStale => _missingOrStale.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> MissingStale => MissingOrStale;
        public IReadOnlyList<WorkloadV2CommitMessage> Unsupported => _unsupported.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Fatal => _fatal.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitMessage> Cleared => _cleared.AsReadOnly();

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
                case WorkloadV2CommitMessageKind.Cleared:
                    _cleared.Add(diagnostic);
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
        internal WorkloadPersistenceReceipt PersistenceReceipt { get; set; }
        internal WorkloadSession RebasedSession { get; set; }
        public bool IsSemanticNoOp => Report?.IsSemanticNoOp == true;
        public bool LiveStateChanged => Report?.LiveStateChanged == true;
        public bool TemplatePersisted => Report?.TemplatePersisted == true;
        public bool HasNetStateChange => Report?.HasNetStateChange == true;
    }

    /// <summary>
    /// Immutable backend-side prepare artifact.  The protocol worker carries
    /// this object from Prepare to Execute; it never reconstructs a payload
    /// from wire strings and it cannot mint the mutation capability itself.
    /// </summary>
    internal sealed class WorkloadPreparedTransaction
    {
        internal WorkloadPreparedTransaction(
            WorkloadTransactionRequest request,
            WorkloadSession session,
            WorkloadDecisionKind decision,
            string targetStableId,
            string forkLabel,
            WorkloadV2CommitResult validation,
            string planFingerprint)
        {
            Request = request;
            Session = session;
            Decision = decision;
            TargetStableId = targetStableId ?? string.Empty;
            ForkLabel = forkLabel ?? string.Empty;
            Validation = validation;
            PlanFingerprint = planFingerprint ?? string.Empty;
        }

        internal WorkloadTransactionRequest Request { get; private set; }
        internal WorkloadSession Session { get; private set; }
        internal WorkloadDecisionKind Decision { get; private set; }
        internal string TargetStableId { get; private set; }
        internal string ForkLabel { get; private set; }
        internal WorkloadV2CommitResult Validation { get; private set; }
        internal string PlanFingerprint { get; private set; }
        internal bool ExecuteStarted { get; set; }
        internal bool ExecuteCompleted { get; set; }
    }

    /// <summary>
    /// Modern workload persistence and preview boundary. Live writes are kept
    /// behind WorkloadV2ApplyService so preview operations remain pure.
    /// </summary>
    internal sealed class Workload2Backend
    {
        private static Workload2Backend _multiplayerBackend;
        private static readonly WorkloadMultiplayerBackendCallbacks MultiplayerCallbacks =
            new WorkloadMultiplayerBackendCallbacks();
        private readonly GameComponent_BWTWorldSettings _component;
        private readonly WorkloadV2ApplyService _applyService;
        private WorkloadSession _previewSession;

        internal Workload2Backend(GameComponent_BWTWorldSettings component)
            : this(component, true)
        {
        }

        private Workload2Backend(
            GameComponent_BWTWorldSettings component,
            bool registerAsMultiplayerUiBackend)
        {
            _component = component;
            _applyService = new WorkloadV2ApplyService(component);
            if (registerAsMultiplayerUiBackend) _multiplayerBackend = this;
        }

        internal static event Action<WorkloadMultiplayerCommitStatus> MultiplayerCommitStatusChanged;

        internal static void RegisterMultiplayerProtocolCallbacks()
        {
            WorkloadTransactionMultiplayer.SetCallbacks(MultiplayerCallbacks);
        }

        internal WorkloadMultiplayerCommitStatus BeginMultiplayerCommit(
            WorkloadDecisionKind decisionKind,
            string forkStableId,
            string forkLabel,
            string idempotencyKey = null)
        {
            return MultiplayerCallbacks.Begin(
                this,
                _previewSession,
                decisionKind,
                forkStableId,
                forkLabel,
                idempotencyKey);
        }

        internal static void PublishMultiplayerStatus(WorkloadMultiplayerCommitStatus status)
        {
            MultiplayerCommitStatusChanged?.Invoke(status);
        }

        internal static Workload2Backend MultiplayerBackend => _multiplayerBackend;
        internal GameComponent_BWTWorldSettings Component => _component;

        internal static Workload2Backend CreateMultiplayerTransactionBackend()
        {
            GameComponent_BWTWorldSettings component =
                Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            return component == null ? null : new Workload2Backend(component, false);
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

            string finalLabel = WorkloadLiveCapturePolicy.ResolveLabel(label, GetDefaultLabel);
            string stableId = Guid.NewGuid().ToString("N");
            WorkloadOperationResult<WorkloadTemplate> captured =
                WorkloadLiveCapture.CaptureCurrentTemplate(stableId, finalLabel);
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

            if (WorkloadV2OwnershipResolver.HasUnsupportedLegacyPayload(template))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "The workload contains legacy schedule or presentation state without a typed transaction intent.");
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

            _applyService.RememberBackendBaseline(
                template.Value,
                liveCapture.Value.BackendBaseline);
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
            WorkloadOperationResult<WorkloadSession> prepared =
                PreparePreviewCandidate(_previewSession, candidate);
            if (!prepared.Succeeded)
            {
                return prepared;
            }

            _previewSession = prepared.Value;
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
        }

        internal WorkloadOperationResult<WorkloadSession> ExtendPreviewBaseline(
            WorkloadSession candidate,
            PawnKey pawn)
        {
            if (_previewSession == null || candidate == null || pawn == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The active V2 preview cannot extend its captured live baseline safely.");
            }

            if (!StringComparer.Ordinal.Equals(
                    _previewSession.SourceIdentity,
                    candidate.SourceIdentity) ||
                !candidate.HasCapturedLiveBaseline ||
                candidate.RuntimeBaseline == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The included pawn candidate does not preserve the active preview identity or baseline.");
            }

            WorkloadOperationResult<WorkloadSession> prepared =
                PreparePreviewCandidate(_previewSession, candidate);
            if (!prepared.Succeeded)
            {
                return prepared;
            }

            if (!prepared.Value.RuntimeBaseline.ContainsPawn(pawn))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The newly included pawn has no authoritative live runtime baseline.");
            }

            _previewSession = prepared.Value;
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

            WorkloadTemplate captureTemplate = BuildEffectiveTemplate(
                candidate.SourceTemplate.WithState(candidate.ProjectedState),
                candidate.ProjectedState,
                candidate.TemplateBaselineState);
            WorkloadOperationResult<WorkloadLiveBaselineCapture> capture =
                _applyService.CaptureLiveBaselineCapture(captureTemplate);
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

            if (capture.Value.BackendBaseline != null)
            {
                _applyService.RememberBackendBaseline(
                    previous.SourceIdentity,
                    capture.Value.BackendBaseline);
            }

            return WorkloadOperationResult<WorkloadSession>.Ok(
                candidate.ExtendCapturedBaseline(
                    added,
                    candidate.ProjectedState,
                    merged));
        }

        private WorkloadOperationResult<WorkloadSession> PreparePreviewCandidate(
            WorkloadSession previous,
            WorkloadSession candidate)
        {
            WorkloadOperationResult<WorkloadSession> extended =
                ExtendRuntimeBaselineForNewPawns(previous, candidate);
            if (!extended.Succeeded)
            {
                return extended;
            }

            WorkloadSession prepared = extended.Value;
            WorkloadOperationResult refresh =
                RefreshBackendBaselineIfNeeded(previous, prepared);
            if (!refresh.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    refresh.Code,
                    refresh.Message);
            }

            return WorkloadOperationResult<WorkloadSession>.Ok(prepared);
        }

        private WorkloadOperationResult RefreshBackendBaselineIfNeeded(
            WorkloadSession previous,
            WorkloadSession candidate)
        {
            if (candidate == null || previous == null)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview baseline refresh has no session candidate.");
            }

            if (!_applyService.TryGetBackendBaseline(
                    previous,
                    out WorkloadBackendDimensionBaseline existing))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview is missing its service-owned dimension baseline.");
            }

            if (!NeedsBackendBaselineRefresh(candidate, existing))
            {
                return WorkloadOperationResult.Ok();
            }

            WorkloadTemplate captureTemplate = BuildEffectiveTemplate(
                candidate.SourceTemplate.WithState(candidate.ProjectedState),
                candidate.ProjectedState,
                candidate.TemplateBaselineState);
            WorkloadOperationResult<WorkloadLiveBaselineCapture> capture =
                _applyService.CaptureLiveBaselineCapture(captureTemplate);
            if (!capture.Succeeded || capture.Value?.BackendBaseline == null)
            {
                return WorkloadOperationResult.Fail(
                    capture.Code,
                    "The V2 service-owned baseline could not be refreshed: " +
                    capture.Message);
            }

            _applyService.RememberBackendBaseline(
                previous.SourceIdentity,
                capture.Value.BackendBaseline);
            return WorkloadOperationResult.Ok();
        }

        private static bool NeedsBackendBaselineRefresh(
            WorkloadSession session,
            WorkloadBackendDimensionBaseline baseline)
        {
            if (session == null || baseline == null)
            {
                return true;
            }

            WorkloadProjectedState state = session.ProjectedState ?? WorkloadProjectedState.Empty;
            WorkloadOwnershipDimensions ownership = WorkloadV2OwnershipResolver.Effective(
                session.SourceTemplate,
                state,
                session.TemplateBaselineState);

            if (ownership.Owns(WorkloadStateDimension.Schedules))
            {
                for (int i = 0; i < state.ScheduleIntents.Count; i++)
                {
                    WorkloadScheduleIntentEntry entry = state.ScheduleIntents[i];
                    if (entry != null && !entry.Intent.IsNoOpinion &&
                        !baseline.Schedules.ContainsKey(entry.Key))
                    {
                        return true;
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                for (int i = 0; i < state.SpecificPriorityIntents.Count; i++)
                {
                    WorkloadSpecificPriorityIntentEntry entry =
                        state.SpecificPriorityIntents[i];
                    if (entry != null && !entry.Intent.IsNoOpinion &&
                        !baseline.SpecificPriorities.ContainsKey(entry.Key))
                    {
                        return true;
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                for (int i = 0; i < state.WorkTypeOrderIntents.Count; i++)
                {
                    WorkloadWorkTypeOrderIntentEntry entry =
                        state.WorkTypeOrderIntents[i];
                    if (entry != null && !entry.Intent.IsNoOpinion &&
                        !baseline.WorkTypeOrders.ContainsKey(entry.Key))
                    {
                        return true;
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.PresentationSettings))
            {
                for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry =
                        state.PresentationSettingIntents[i];
                    if (entry != null && !entry.Intent.IsNoOpinion &&
                        !baseline.PresentationSettingIds.Contains(entry.Key))
                    {
                        return true;
                    }
                }
            }

            return false;
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
            WorkloadOperationResult<WorkloadSession> prepared =
                PreparePreviewCandidate(_previewSession, candidate);
            if (!prepared.Succeeded)
            {
                return prepared;
            }

            _previewSession = prepared.Value;
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

            WorkloadSessionDecision decision;
            switch (decisionKind)
            {
                case WorkloadDecisionKind.Apply:
                    decision = _previewSession.Apply();
                    break;
                case WorkloadDecisionKind.Update:
                    decision = _previewSession.Update();
                    break;
                case WorkloadDecisionKind.Fork:
                    decision = _previewSession.Fork(
                        Guid.NewGuid().ToString("N"),
                        _previewSession.SourceTemplate.Label);
                    break;
                default:
                    return WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                        WorkloadDiagnosticCode.UnsupportedOperation,
                        "The requested V2 preview decision is not supported.");
            }

            if (decision?.Plan == null)
            {
                return WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview could not construct a lifecycle plan.");
            }

            WorkloadTemplate targetTemplate = decisionKind == WorkloadDecisionKind.Apply
                ? BuildEffectiveTemplate(
                    _previewSession.BuildApplyTemplate(),
                    _previewSession.ProjectedState,
                    _previewSession.TemplateBaselineState)
                : BuildEffectiveTemplate(
                    decision.ResultTemplate,
                    decision.ResultTemplate?.ProjectedState,
                    _previewSession.TemplateBaselineState);
            WorkloadOwnershipDimensions ownership = WorkloadV2OwnershipResolver.Effective(
                targetTemplate,
                decision.Plan.AfterState,
                _previewSession.TemplateBaselineState);
            WorkloadSemanticDiff diff = WorkloadSemanticDiff.Between(
                decision.Plan.BeforeState,
                decision.Plan.AfterState,
                ownership);
            WorkloadValidationResult validation = WorkloadValidator.Validate(targetTemplate);
            return WorkloadOperationResult<WorkloadPreviewPlan>.Ok(
                new WorkloadPreviewPlan(
                    decisionKind,
                    _previewSession.SourceTemplate,
                    decision.Plan.BeforeState,
                    decision.Plan.AfterState,
                    diff,
                    validation));
        }

        internal WorkloadOperationResult<WorkloadSemanticDiff> PreviewDiff()
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            WorkloadOperationResult<WorkloadPreviewPlan> plan =
                PreviewPlan(WorkloadDecisionKind.Update);
            return plan.Succeeded
                ? WorkloadOperationResult<WorkloadSemanticDiff>.Ok(plan.Value.Diff)
                : WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    plan.Code,
                    plan.Message);
        }

        internal WorkloadOperationResult<WorkloadSemanticDiff> PreviewImpactDiff()
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session.");
            }

            WorkloadOperationResult<WorkloadPreviewPlan> plan =
                PreviewPlan(WorkloadDecisionKind.Apply);
            return plan.Succeeded
                ? WorkloadOperationResult<WorkloadSemanticDiff>.Ok(plan.Value.Diff)
                : WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    plan.Code,
                    plan.Message);
        }

        internal static WorkloadTemplate BuildEffectiveTemplate(
            WorkloadTemplate template,
            WorkloadProjectedState projectedState,
            WorkloadProjectedState baselineState)
        {
            if (template == null || template.Definition == null)
            {
                return template;
            }

            WorkloadProjectedState state = projectedState ?? template.ProjectedState ??
                WorkloadProjectedState.Empty;
            WorkloadOwnershipDimensions ownership = WorkloadV2OwnershipResolver.Effective(
                template,
                state,
                baselineState);
            if (ownership == template.Definition.OwnershipDimensions &&
                ReferenceEquals(state, template.ProjectedState))
            {
                return template;
            }

            WorkloadDefinition definition = new WorkloadDefinition(
                template.Definition.StableId,
                template.Definition.Label,
                template.Definition.SchemaVersion,
                ownership,
                template.Definition.Scope);
            return new WorkloadTemplate(definition, state);
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

        internal WorkloadOperationResult<WorkloadSession> RebasePreviewAfterPersistence(
            WorkloadPersistenceReceipt receipt)
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session to rebase.");
            }

            WorkloadSession rebased = _previewSession.RebaseAfterPersistence(receipt);
            if (rebased == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The V2 persistence receipt does not match the active preview source or target.");
            }

            WorkloadOperationResult rekey = _applyService.RekeyBackendBaseline(
                _previewSession,
                receipt);
            if (!rekey.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    rekey.Code,
                    rekey.Message);
            }

            _previewSession = rebased;
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
        }

        internal WorkloadOperationResult<WorkloadPersistenceReceipt> RecoverPersistenceReceipt(
            WorkloadDecisionKind decisionKind,
            string targetStableId,
            string forkLabel)
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.NotFound,
                    "There is no active V2 preview session from which to recover a persistence receipt.");
            }

            return _applyService.RecoverPersistenceReceipt(
                _previewSession,
                decisionKind,
                targetStableId,
                forkLabel);
        }

        internal WorkloadOperationResult<WorkloadSession> BuildMultiplayerSession(
            WorkloadTransactionRequest request,
            WorkloadTemplate projectedTemplate)
        {
            if (request == null || projectedTemplate == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The synchronized workload request or projected payload is missing.");
            }

            if (string.IsNullOrWhiteSpace(request.SessionId) ||
                request.ExpectedRevisions == null ||
                request.ExpectedRevisions.SessionRevision <= 0 ||
                request.ExpectedRevisions.MembershipRevision <= 0)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The synchronized workload request has no valid preview-session identity or independent freshness revisions.");
            }

            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(request.SourceWorkloadId);
            if (!found.Succeeded)
                return WorkloadOperationResult<WorkloadSession>.Fail(found.Code, found.Message);
            WorkloadOperationResult<WorkloadTemplate> source = WorkloadV2RecordConverter.TryToTemplate(found.Value);
            if (!source.Succeeded)
                return WorkloadOperationResult<WorkloadSession>.Fail(source.Code, source.Message);
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    WorkloadSession.GetSourceIdentity(source.Value),
                    request.ExpectedRevisions.SourceTemplateFingerprint))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The synchronized workload source fingerprint is stale.");
            }

            WorkloadProjectedState synchronizedState =
                projectedTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            WorkloadScope synchronizedScope =
                projectedTemplate.Definition.Scope ?? WorkloadScope.Empty;
            for (int i = 0; i < synchronizedScope.ExcludedPawnIds.Count; i++)
            {
                PawnKey excludedPawn = synchronizedScope.ExcludedPawnIds[i];
                if (!synchronizedState.IsExcluded(excludedPawn))
                {
                    synchronizedState = synchronizedState.ExcludePawn(excludedPawn);
                }
            }

            // The payload scope carries temporary application exclusions that
            // are deliberately not part of the persisted source definition.
            // Use that scope for peer baseline capture, while keeping the
            // stored source template as the session identity authority.
            WorkloadDefinition observedDefinition = new WorkloadDefinition(
                source.Value.Definition.StableId,
                source.Value.Definition.Label,
                source.Value.Definition.SchemaVersion,
                source.Value.Definition.OwnershipDimensions,
                synchronizedScope);
            WorkloadTemplate observedSource = BuildEffectiveTemplate(
                source.Value.WithDefinition(observedDefinition).WithState(synchronizedState),
                synchronizedState,
                source.Value.ProjectedState);
            WorkloadOperationResult<WorkloadLiveBaselineCapture> capture =
                _applyService.CaptureLiveBaselineCapture(observedSource);
            if (!capture.Succeeded)
                return WorkloadOperationResult<WorkloadSession>.Fail(capture.Code, capture.Message);
            WorkloadBackendDimensionBaseline observed = capture.Value.BackendBaseline;
            WorkloadV2PersistenceEnvelope store = _component.EnsureWorkloadV2Persistence();
            if (store == null || store.PersistenceRevision != request.ExpectedRevisions.StoreRevision ||
                observed == null ||
                observed.AuthorityRevision != request.ExpectedRevisions.AuthorityRevision ||
                observed.AuthorityRevision != request.ExpectedRevisions.PriorityServiceRevision ||
                observed.ScheduleRevision != request.ExpectedRevisions.ScheduleServiceRevision ||
                observed.SpecificJobRevision != request.ExpectedRevisions.SpecificJobServiceRevision ||
                observed.SpecificJobRevision != request.ExpectedRevisions.TaxonomyRevision ||
                ComputeSettingsRevision(observed.SettingsSnapshot) !=
                    request.ExpectedRevisions.SettingsServiceRevision ||
                !StringComparer.Ordinal.Equals(
                    PriorityAuthorityResolver.Resolve().Owner.ToString(),
                    request.ExpectedRevisions.AuthorityOwner) ||
                !StringComparer.OrdinalIgnoreCase.Equals(
                    observed.TaxonomyFingerprint ?? string.Empty,
                    request.ExpectedRevisions.TaxonomyFingerprint))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.BaselineChanged,
                    "A synchronized workload store, authority, service, or taxonomy revision changed before prepare.");
            }
            WorkloadSession session = WorkloadSession.OpenCaptured(
                source.Value,
                capture.Value.State,
                WorkloadSession.GetSourceIdentity(source.Value),
                runtimeBaseline: capture.Value.RuntimeBaseline,
                previewSessionId: request.SessionId,
                sessionRevision: request.ExpectedRevisions.SessionRevision,
                membershipRevision: request.ExpectedRevisions.MembershipRevision);
            if (session == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The synchronized workload session could not be reconstructed.");
            }

            if (!StringComparer.Ordinal.Equals(
                    session.PreviewSessionId,
                    request.SessionId) ||
                session.SessionRevision != request.ExpectedRevisions.SessionRevision ||
                session.MembershipRevision != request.ExpectedRevisions.MembershipRevision)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.BaselineChanged,
                    "The synchronized preview-session identity or freshness revisions changed before prepare.");
            }

            _applyService.RememberBackendBaseline(source.Value, capture.Value.BackendBaseline);
            return WorkloadOperationResult<WorkloadSession>.Ok(
                session.EditState(synchronizedState));
        }

        internal WorkloadV2CommitResult ValidateMultiplayerCommit(
            WorkloadSession session,
            WorkloadDecisionKind decision,
            string targetStableId,
            string forkLabel)
        {
            return _applyService.ValidatePrepared(session, decision, targetStableId, forkLabel);
        }

        internal WorkloadOperationResult<WorkloadPreparedTransaction> PrepareMultiplayerCommit(
            WorkloadTransactionRequest request,
            WorkloadSession session,
            WorkloadDecisionKind decision,
            string targetStableId,
            string forkLabel)
        {
            if (request == null || session == null)
            {
                return WorkloadOperationResult<WorkloadPreparedTransaction>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The synchronized workload prepare is missing its request or session identity.");
            }

            WorkloadV2CommitResult validation = _applyService.ValidatePrepared(
                session,
                decision,
                targetStableId,
                forkLabel);
            if (!validation.Succeeded || validation.Plan == null)
            {
                return WorkloadOperationResult<WorkloadPreparedTransaction>.Fail(
                    validation.Code,
                    validation.Message);
            }

            return WorkloadOperationResult<WorkloadPreparedTransaction>.Ok(
                new WorkloadPreparedTransaction(
                    request,
                    session,
                    decision,
                    targetStableId,
                    forkLabel,
                    validation,
                    BuildPreparedPlanFingerprint(validation.Plan, decision, targetStableId)));
        }

        internal WorkloadV2CommitResult ExecuteMultiplayerCommit(
            WorkloadPreparedTransaction prepared,
            out WorkloadV2ApplyService.WorkloadCommitRollbackLease lease)
        {
            lease = null;
            if (prepared == null || prepared.Request == null || prepared.Session == null ||
                prepared.Validation == null || prepared.Validation.Plan == null)
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared?.Decision ?? WorkloadDecisionKind.Apply,
                    WorkloadDiagnosticCode.InvalidState,
                    "multiplayer.execute.prepare",
                    "The synchronized workload execute has no immutable prepare artifact.");
            }

            if (prepared.ExecuteStarted ||
                !StringComparer.Ordinal.Equals(
                    BuildPreparedPlanFingerprint(
                        prepared.Validation.Plan,
                        prepared.Decision,
                        prepared.TargetStableId),
                    prepared.PlanFingerprint))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.InvalidState,
                    "multiplayer.execute.prepare",
                    "The synchronized workload prepare artifact was reused or changed.");
            }

            WorkloadTransactionRequest request = prepared.Request;
            WorkloadTransactionRevisionVector revisions = request.ExpectedRevisions;
            WorkloadTemplate targetTemplate = prepared.Validation.ResultTemplate;
            string authorizationReason;
            if (revisions == null)
            {
                authorizationReason =
                    "The synchronized workload request has no revision vector.";
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.MutationCapabilityRejected,
                    "multiplayer.execute.authorization",
                    authorizationReason);
            }

            if (targetTemplate == null)
            {
                authorizationReason =
                    "The synchronized workload prepare has no result template.";
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.MutationCapabilityRejected,
                    "multiplayer.execute.authorization",
                    authorizationReason);
            }

            if (!TryConvertCapabilityRevision(
                    revisions.SpecificJobServiceRevision,
                    "specific-job",
                    out int specificJobRevision,
                    out authorizationReason) ||
                !TryConvertCapabilityRevision(
                    revisions.ScheduleServiceRevision,
                    "schedule",
                    out int scheduleRevision,
                    out authorizationReason) ||
                !TryConvertCapabilityRevision(
                    revisions.SettingsServiceRevision,
                    "settings",
                    out int settingsRevision,
                    out authorizationReason))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.MutationCapabilityRejected,
                    "multiplayer.execute.authorization",
                    authorizationReason);
            }

            if (!WorkloadMutationAuthorization.TryCreateForWorkload(
                    request,
                    prepared.Session,
                    request.RequestId,
                    request.RequestFingerprint,
                    revisions.SourceTemplateFingerprint,
                    targetTemplate.SemanticFingerprint,
                    revisions.SessionRevision,
                    revisions.AuthorityRevision,
                    revisions.AuthorityOwner,
                    specificJobRevision,
                    scheduleRevision,
                    settingsRevision,
                    revisions.HostSessionEpoch,
                    revisions.RosterFingerprint,
                    out WorkloadMutationAuthorization authorization,
                    out authorizationReason))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.MutationCapabilityRejected,
                    "multiplayer.execute.authorization",
                    authorizationReason ??
                    "The synchronized workload mutation capability could not be minted.");
            }

            prepared.ExecuteStarted = true;
            return _applyService.ExecutePrepared(
                prepared.Session,
                prepared.Decision,
                prepared.TargetStableId,
                prepared.ForkLabel,
                authorization,
                prepared.PlanFingerprint,
                prepared.Request,
                out lease);
        }

        internal bool AbortMultiplayerCommit(WorkloadV2ApplyService.WorkloadCommitRollbackLease lease)
        {
            return _applyService.AbortPrepared(lease);
        }

        internal bool ConfirmMultiplayerCommit(
            WorkloadV2ApplyService.WorkloadCommitRollbackLease lease)
        {
            return _applyService.ConfirmPrepared(lease);
        }

        internal static string BuildPreparedPlanFingerprint(
            WorkloadPreviewPlan plan,
            WorkloadDecisionKind decision,
            string targetStableId)
        {
            if (plan == null)
            {
                return string.Empty;
            }

            return WorkloadCanonical.Fingerprint(
                ((int)decision).ToString(CultureInfo.InvariantCulture) + "\u001f" +
                (targetStableId ?? string.Empty) + "\u001f" +
                (plan.BeforeState?.SemanticFingerprint ?? string.Empty) + "\u001f" +
                (plan.AfterState?.SemanticFingerprint ?? string.Empty) + "\u001f" +
                (plan.Diff?.BeforeFingerprint ?? string.Empty) + "\u001f" +
                (plan.Diff?.AfterFingerprint ?? string.Empty));
        }

        private static bool TryConvertCapabilityRevision(
            long revision,
            string dimension,
            out int converted,
            out string reason)
        {
            converted = 0;
            if (revision < 0 || revision > int.MaxValue)
            {
                reason =
                    "The synchronized " + (dimension ?? "service") +
                    " revision is outside the supported transaction capability range.";
                return false;
            }

            converted = (int)revision;
            reason = null;
            return true;
        }

        internal bool TryCreateMultiplayerRevisionVector(
            WorkloadSession session,
            string hostEpoch,
            string rosterFingerprint,
            out WorkloadTransactionRevisionVector revisions)
        {
            revisions = null;
            if (session == null ||
                string.IsNullOrWhiteSpace(session.PreviewSessionId) ||
                session.SessionRevision <= 0 ||
                session.MembershipRevision <= 0 ||
                !_applyService.TryGetBackendBaseline(session, out var baseline) ||
                baseline == null)
                return false;
            WorkloadV2PersistenceEnvelope store = _component?.EnsureWorkloadV2Persistence();
            if (store == null) return false;
            revisions = new WorkloadTransactionRevisionVector(
                store.PersistenceRevision,
                session.SessionRevision,
                baseline.AuthorityRevision,
                baseline.AuthorityRevision,
                baseline.ScheduleRevision,
                baseline.SpecificJobRevision,
                ComputeSettingsRevision(baseline.SettingsSnapshot),
                session.MembershipRevision,
                PriorityAuthorityResolver.Resolve().Owner.ToString(),
                hostEpoch,
                rosterFingerprint,
                WorkloadSession.GetSourceIdentity(session.SourceTemplate),
                baseline.SpecificJobRevision,
                baseline.TaxonomyFingerprint ?? WorkloadCanonical.Fingerprint(string.Empty));
            return true;
        }

        private static long ComputeSettingsRevision(WorkloadPresentationSettingsSnapshot snapshot)
        {
            if (snapshot?.Values == null || snapshot.Values.Count == 0) return 0;
            var builder = new StringBuilder();
            var keys = new List<string>(snapshot.Values.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                builder.Append(WorkloadCanonical.Encode(key));
                builder.Append(WorkloadCanonical.Encode(snapshot.Values[key].CanonicalValue));
            }
            string fingerprint = WorkloadCanonical.Fingerprint(builder.ToString());
            return long.TryParse(fingerprint.Substring(0, 15), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var value) ? value : 0;
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

            if (MultiplayerBridge.Active)
            {
                WorkloadMultiplayerCommitStatus pending = BeginMultiplayerCommit(
                    decisionKind, forkStableId, forkLabel);
                return WorkloadV2ApplyService.GatewayFailure(
                    decisionKind,
                    pending.Code,
                    "multiplayer.pending",
                    pending.Message);
            }

            WorkloadV2CommitResult result = _applyService.Commit(
                _previewSession,
                decisionKind,
                forkStableId,
                forkLabel,
                decisionKind == WorkloadDecisionKind.Apply
                    ? null
                    : receipt => RebasePreviewAfterPersistence(receipt).Succeeded);
            if (result.Succeeded)
            {
                if (decisionKind == WorkloadDecisionKind.Apply)
                {
                    _previewSession = null;
                    result.PreviewCleared = true;
                }
                else
                {
                    result.RebasedSession = _previewSession;
                }
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
    internal sealed class WorkloadMultiplayerBackendCallbacks : IWorkloadTransactionCallbacks
    {
        private sealed class PendingPeerCommit
        {
            internal string TableKey;
            internal Workload2Backend Backend;
            internal WorkloadSession Session;
            internal WorkloadPreparedTransaction Prepared;
            internal WorkloadDecisionKind Decision;
            internal string TargetStableId;
            internal string ForkLabel;
            internal WorkloadV2ApplyService.WorkloadCommitRollbackLease Lease;
            internal WorkloadV2CommitResult Result;
            internal string ReportFingerprint;
        }

        private readonly Dictionary<string, PendingPeerCommit> _pending =
            new Dictionary<string, PendingPeerCommit>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingPeerCommit> _pendingByRequestId =
            new Dictionary<string, PendingPeerCommit>(StringComparer.Ordinal);

        internal WorkloadMultiplayerCommitStatus Begin(
            Workload2Backend backend,
            WorkloadSession session,
            WorkloadDecisionKind decision,
            string forkStableId,
            string forkLabel,
            string idempotencyKey)
        {
            if (!MultiplayerBridge.Active || !MultiplayerBridge.Host)
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "Only the authenticated multiplayer host may request a workload transaction.");
            if (backend == null || session == null)
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.NotFound, "There is no active workload preview session.");
            if (!MultiplayerBridge.TryGetWorkloadSessionContext(out var epoch, out var roster) ||
                !MultiplayerBridge.TryGetWorkloadParticipantKeys(out var participants))
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "The synchronized multiplayer session roster is unavailable.");

            string targetId = decision == WorkloadDecisionKind.Fork
                ? (string.IsNullOrWhiteSpace(forkStableId) ? Guid.NewGuid().ToString("N") : forkStableId)
                : session.SourceTemplate.StableId;
            WorkloadTemplate payloadTemplate = decision == WorkloadDecisionKind.Apply
                ? session.BuildApplyTemplate()
                : decision == WorkloadDecisionKind.Update
                    ? session.TargetTemplate
                    : session.Fork(targetId, forkLabel).ResultTemplate;
            WorkloadOperationResult<WorkloadV2PersistenceRecord> converted =
                WorkloadV2RecordConverter.TryFromTemplate(payloadTemplate);
            if (!converted.Succeeded)
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    converted.Code, converted.Message);
            string payloadError = null;
            if (!WorkloadMultiplayerPayloadCodec.TrySerialize(converted.Value, out var bytes, out var codecError) ||
                !WorkloadTransactionPayload.TryCreate(bytes, null, out var payload, out payloadError))
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.InvalidState, codecError ?? payloadError);

            WorkloadV2PersistenceEnvelope store = backend.Component?.EnsureWorkloadV2Persistence();
            if (store == null)
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.NoCurrentGame, "The workload store is unavailable.");
            store.RefreshDiagnostics();
            string requestId = Guid.NewGuid().ToString("N");
            string effectiveIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey;
            if (!backend.TryCreateMultiplayerRevisionVector(
                session, epoch, roster, out var revisions))
                return Status(requestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.BaselineChanged,
                    "The workload service revisions captured for preview are unavailable.");
            if (!WorkloadTransactionRequest.TryCreateCanonical(
                    ToOperation(decision), requestId, effectiveIdempotencyKey,
                    session.PreviewSessionId, session.SourceTemplate.StableId, targetId,
                    MultiplayerBridge.LocalPlayerName, 3600, payload, revisions,
                    participants, out var request, out var diagnostic))
                return Status(requestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.InvalidState, diagnostic);

            long sequence = Verse.Find.TickManager?.TicksGame ?? 0;
            WorkloadTransactionAdmission admission = WorkloadTransactionMultiplayer.TryBegin(request, sequence);
            if (!admission.Accepted)
            {
                string correlatedRequestId = admission.RegisteredRequest?.RequestId ?? requestId;
                if (admission.Code == WorkloadTransactionAdmissionCode.MismatchedDuplicate)
                {
                    return Status(admission.Request?.RequestId ?? requestId,
                        WorkloadMultiplayerCommitState.Rejected,
                        WorkloadDiagnosticCode.PersistenceConflict, admission.Diagnostic);
                }

                if (admission.TerminalResult != null)
                {
                    return Status(
                        correlatedRequestId,
                        ToCommitState(admission.TerminalResult.TerminalState),
                        admission.TerminalResult.Accepted
                            ? WorkloadDiagnosticCode.None
                            : admission.TerminalResult.TerminalState ==
                              WorkloadTransactionTerminalState.RollbackFailed
                                ? WorkloadDiagnosticCode.RollbackFailed
                                : WorkloadDiagnosticCode.InvalidState,
                        admission.TerminalResult.Detail,
                        null);
                }

                if (admission.Code == WorkloadTransactionAdmissionCode.Duplicate &&
                    admission.State != null &&
                    !admission.State.IsFinal)
                {
                    return Status(
                        correlatedRequestId,
                        WorkloadMultiplayerCommitState.Pending,
                        WorkloadDiagnosticCode.None,
                        "The idempotent workload transaction is already in progress.");
                }

                if (admission.Code == WorkloadTransactionAdmissionCode.RecoveryRequired)
                {
                    return Status(
                        correlatedRequestId,
                        WorkloadMultiplayerCommitState.RollbackFailed,
                        WorkloadDiagnosticCode.RollbackFailed,
                        admission.Diagnostic);
                }

                return Status(requestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.UnsupportedOperation, admission.Diagnostic);
            }
            return Status(requestId, WorkloadMultiplayerCommitState.Pending,
                WorkloadDiagnosticCode.None,
                "The workload transaction is pending synchronized prepare/execute acknowledgement.");
        }

        public void OnRequestAccepted(WorkloadTransactionRequest request) { }

        public void OnAdmissionRejected(WorkloadTransactionAdmission admission)
        {
            if (admission?.Code == WorkloadTransactionAdmissionCode.MismatchedDuplicate)
            {
                Status(admission.Request?.RequestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.PersistenceConflict, admission.Diagnostic);
                return;
            }

            if (admission?.TerminalResult != null)
            {
                Status(
                    admission.RegisteredRequest?.RequestId ??
                        admission.Request?.RequestId ??
                        admission.TerminalResult.RequestId,
                    ToCommitState(admission.TerminalResult.TerminalState),
                    admission.TerminalResult.Accepted
                        ? WorkloadDiagnosticCode.None
                        : admission.TerminalResult.TerminalState ==
                          WorkloadTransactionTerminalState.RollbackFailed
                            ? WorkloadDiagnosticCode.RollbackFailed
                            : WorkloadDiagnosticCode.InvalidState,
                    admission.TerminalResult.Detail,
                    null);
                return;
            }

            if (admission?.Code == WorkloadTransactionAdmissionCode.Duplicate &&
                admission.State != null && !admission.State.IsFinal)
            {
                Status(
                    admission.RegisteredRequest?.RequestId ?? admission.Request?.RequestId,
                    WorkloadMultiplayerCommitState.Pending,
                    WorkloadDiagnosticCode.None,
                    "The idempotent workload transaction is already in progress.");
                return;
            }

            if (admission?.Code == WorkloadTransactionAdmissionCode.RecoveryRequired)
            {
                Status(
                    admission.RegisteredRequest?.RequestId ?? admission.Request?.RequestId,
                    WorkloadMultiplayerCommitState.RollbackFailed,
                    WorkloadDiagnosticCode.RollbackFailed,
                    admission.Diagnostic);
                return;
            }

            Status(admission?.Request?.RequestId, WorkloadMultiplayerCommitState.Rejected,
                WorkloadDiagnosticCode.UnsupportedOperation,
                admission?.Diagnostic ?? "The synchronized workload request was rejected.");
        }

        public void OnPrepareRequested(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            bool accepted = TryPrepare(request, out var pending, out var code, out var detail);
            if (accepted) _pending[request.TableKey] = pending;
            if (accepted) _pendingByRequestId[request.RequestId] = pending;
            if (accepted)
                Status(request.RequestId, WorkloadMultiplayerCommitState.Prepared,
                    WorkloadDiagnosticCode.None, detail);
            else
                Status(request.RequestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.InvalidState,
                    detail ?? "The synchronized workload prepare was rejected.");
            var acknowledgement = new WorkloadTransactionWireAcknowledgement
            {
                Phase = (byte)WorkloadTransactionPhase.Prepare,
                Accepted = accepted,
                RequestId = request.RequestId,
                RequestFingerprint = request.RequestFingerprint,
                HostSessionEpoch = request.ExpectedRevisions.HostSessionEpoch,
                RosterFingerprint = request.ExpectedRevisions.RosterFingerprint,
                PeerKey = MultiplayerBridge.LocalPlayerName,
                Code = code,
                Detail = detail,
                ReportFingerprint = pending?.ReportFingerprint,
                Sequence = Next(state)
            };
            WorkloadTransactionMultiplayer.PopulateContext(request, acknowledgement);
            if (MultiplayerBridge.Host &&
                string.Equals(
                    MultiplayerBridge.LocalPlayerName,
                    request.RequesterPlayerKey,
                    StringComparison.Ordinal))
            {
                WorkloadTransactionMultiplayer.Protocol.RecordLocalPrepareAcknowledgement(
                    MultiplayerBridge.LocalPlayerName,
                    accepted,
                    code,
                    detail,
                    pending?.ReportFingerprint,
                    acknowledgement.Sequence);
            }
            else
            {
                WorkloadTransactionMultiplayer.SendPrepareAcknowledgement(acknowledgement);
            }
        }

        public void OnExecuteRequested(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            if (MultiplayerBridge.Host)
            {
                var executeControl = Control(
                    request,
                    state,
                    WorkloadTransactionPhase.Execute,
                    WorkloadTransactionControlKind.Execute,
                    true,
                    false,
                    "execute",
                    "The host authorized the prepared workload transaction to execute.");
                WorkloadTransactionMultiplayer.SendControl(executeControl);
            }

            bool accepted = _pending.TryGetValue(request.TableKey, out var pending);
            string detail = "The synchronized workload transaction was not prepared on this peer.";
            if (accepted)
            {
                pending.Result = pending.Backend.ExecuteMultiplayerCommit(
                    pending.Prepared,
                    out pending.Lease);
                accepted = pending.Result.Succeeded;
                detail = pending.Result.Message;
                pending.ReportFingerprint = ReportFingerprint(pending.Result);
                if (accepted)
                    Status(request.RequestId,
                        WorkloadMultiplayerCommitState.ExecutedAwaitingConfirmation,
                        WorkloadDiagnosticCode.None, detail, pending.Result);
                else
                    Status(request.RequestId,
                        pending.Result.Code == WorkloadDiagnosticCode.RollbackFailed
                            ? WorkloadMultiplayerCommitState.RollbackFailed
                            : WorkloadMultiplayerCommitState.Failed,
                        pending.Result.Code,
                        detail,
                        pending.Result);
            }
            var result = new WorkloadTransactionWireResult
            {
                Phase = (byte)WorkloadTransactionPhase.Execute,
                Accepted = accepted,
                RequiresRollback = pending?.Lease != null && !accepted,
                RequestId = request.RequestId,
                RequestFingerprint = request.RequestFingerprint,
                HostSessionEpoch = request.ExpectedRevisions.HostSessionEpoch,
                RosterFingerprint = request.ExpectedRevisions.RosterFingerprint,
                PeerKey = MultiplayerBridge.LocalPlayerName,
                Code = accepted ? "executed" : "execute-failed",
                Detail = detail,
                ReportFingerprint = pending?.ReportFingerprint,
                Sequence = Next(state)
            };
            WorkloadTransactionMultiplayer.PopulateContext(request, result);
            if (MultiplayerBridge.Host &&
                string.Equals(
                    MultiplayerBridge.LocalPlayerName,
                    request.RequesterPlayerKey,
                    StringComparison.Ordinal))
            {
                WorkloadTransactionMultiplayer.Protocol.RecordLocalExecuteResult(
                    MultiplayerBridge.LocalPlayerName,
                    accepted,
                    accepted ? "executed" : "execute-failed",
                    detail,
                    pending?.ReportFingerprint,
                    result.Sequence,
                    result.RequiresRollback);
            }
            else
            {
                WorkloadTransactionMultiplayer.SendExecuteResult(result);
            }
        }

        public void OnConfirmRequested(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            if (!MultiplayerBridge.Host) return;
            WorkloadTransactionMultiplayer.SendControl(Control(
                request,
                state,
                WorkloadTransactionPhase.Confirm,
                WorkloadTransactionControlKind.ConfirmationRequest,
                true,
                false,
                "confirm-requested",
                "The host is requesting per-peer confirmation before terminal success."));
        }

        public void OnConfirmationControlReceived(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            _pending.TryGetValue(request.TableKey, out var pending);
            bool accepted = state != null && state.ConfirmationControlAccepted &&
                            pending != null && pending.Lease != null &&
                            !pending.Lease.RecoveryRequired;
            string detail = accepted
                ? "The peer retained its rollback lease and acknowledged host confirmation."
                : "The peer could not acknowledge host confirmation safely.";
            var acknowledgement = new WorkloadTransactionWireAcknowledgement
            {
                Phase = (byte)WorkloadTransactionPhase.Confirm,
                Accepted = accepted,
                RequiresRollback = !accepted,
                RequestId = request.RequestId,
                RequestFingerprint = request.RequestFingerprint,
                HostSessionEpoch = request.ExpectedRevisions.HostSessionEpoch,
                RosterFingerprint = request.ExpectedRevisions.RosterFingerprint,
                PeerKey = MultiplayerBridge.LocalPlayerName,
                Code = accepted ? "confirmation-ready" : "confirmation-failed",
                Detail = detail,
                ReportFingerprint = pending?.ReportFingerprint,
                Sequence = Next(state)
            };
            WorkloadTransactionMultiplayer.PopulateContext(request, acknowledgement);
            if (MultiplayerBridge.Host &&
                string.Equals(
                    MultiplayerBridge.LocalPlayerName,
                    request.RequesterPlayerKey,
                    StringComparison.Ordinal))
            {
                // The host is counted when Execute enters Confirm.  It never
                // sends a forged peer acknowledgement to itself.
                return;
            }

            WorkloadTransactionMultiplayer.SendConfirmationAcknowledgement(acknowledgement);
        }

        public void OnFinalConfirmationRequested(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            if (!MultiplayerBridge.Host) return;
            WorkloadTransactionMultiplayer.SendControl(Control(
                request,
                state,
                WorkloadTransactionPhase.Confirm,
                WorkloadTransactionControlKind.FinalConfirmation,
                true,
                false,
                "confirmed",
                "The synchronized workload transaction passed the peer confirmation barrier."));
        }

        public void OnAbortRequested(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            bool restored = Rollback(request);
            bool rollbackRequired = state != null && state.RequiresRollback;
            if (rollbackRequired)
            {
                WorkloadTransactionMultiplayer.Protocol.RecordLocalRollback(
                    MultiplayerBridge.LocalPlayerName,
                    restored,
                    restored ? "rolled-back" : "rollback-failed",
                    restored
                        ? "The local workload transaction rollback completed."
                        : "The local workload rollback failed and requires recovery.",
                    Next(state));
            }

            if (MultiplayerBridge.Host)
            {
                WorkloadTransactionMultiplayer.SendControl(Control(
                    request,
                    state,
                    WorkloadTransactionPhase.Abort,
                    WorkloadTransactionControlKind.Abort,
                    restored,
                    rollbackRequired,
                    restored ? "aborted" : "rollback-failed",
                    state?.Detail,
                    false));
            }
            else if (rollbackRequired)
            {
                WorkloadTransactionMultiplayer.SendControl(Control(
                    request,
                    state,
                    WorkloadTransactionPhase.Abort,
                    WorkloadTransactionControlKind.Abort,
                    restored,
                    true,
                    restored ? "rolled-back" : "rollback-failed",
                    state?.Detail,
                    true));
            }
        }

        public void OnRollbackRequired(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            Status(
                request?.RequestId,
                WorkloadMultiplayerCommitState.RollbackFailed,
                WorkloadDiagnosticCode.RollbackRequired,
                "The synchronized workload transaction is awaiting exactly-once rollback reports.");
        }

        public void OnTerminal(WorkloadTransactionResult result)
        {
            if (result == null) return;
            WorkloadMultiplayerCommitState state;
            switch (result.TerminalState)
            {
                case WorkloadTransactionTerminalState.Succeeded:
                    state = WorkloadMultiplayerCommitState.Succeeded;
                    break;
                case WorkloadTransactionTerminalState.Rejected:
                    state = WorkloadMultiplayerCommitState.Rejected;
                    break;
                case WorkloadTransactionTerminalState.Aborted:
                    state = WorkloadMultiplayerCommitState.Aborted;
                    break;
                case WorkloadTransactionTerminalState.TimedOut:
                    state = WorkloadMultiplayerCommitState.TimedOut;
                    break;
                case WorkloadTransactionTerminalState.RolledBack:
                    state = WorkloadMultiplayerCommitState.RolledBack;
                    break;
                case WorkloadTransactionTerminalState.RollbackFailed:
                    state = WorkloadMultiplayerCommitState.RollbackFailed;
                    break;
                default:
                    state = WorkloadMultiplayerCommitState.Failed;
                    break;
            }
            _pendingByRequestId.TryGetValue(result.RequestId, out var pending);
            bool confirmed = true;
            bool rollbackAfterConfirmationFailure = false;
            string terminalDetail = result.Detail;
            if (result.Accepted &&
                result.TerminalState == WorkloadTransactionTerminalState.Succeeded &&
                pending != null)
            {
                confirmed = pending.Backend == null ||
                    pending.Backend.ConfirmMultiplayerCommit(pending.Lease);
                if (!confirmed)
                {
                    rollbackAfterConfirmationFailure = pending.Backend == null ||
                        pending.Backend.AbortMultiplayerCommit(pending.Lease);
                    state = rollbackAfterConfirmationFailure
                        ? WorkloadMultiplayerCommitState.RolledBack
                        : WorkloadMultiplayerCommitState.RollbackFailed;
                }
            }
            if (pending?.Lease?.RecoveryRequired == true)
            {
                state = WorkloadMultiplayerCommitState.RollbackFailed;
            }
            Status(result.RequestId, state,
                state == WorkloadMultiplayerCommitState.RollbackFailed
                    ? WorkloadDiagnosticCode.RollbackFailed
                    : result.Accepted && !rollbackAfterConfirmationFailure
                        ? WorkloadDiagnosticCode.None
                        : WorkloadDiagnosticCode.InvalidState,
                terminalDetail, pending?.Result);
            if (pending != null && state != WorkloadMultiplayerCommitState.RollbackFailed)
            {
                _pendingByRequestId.Remove(result.RequestId);
                _pending.Remove(pending.TableKey ?? string.Empty);
            }
        }

        private bool TryPrepare(
            WorkloadTransactionRequest request,
            out PendingPeerCommit pending,
            out string code,
            out string detail)
        {
            pending = null;
            code = "prepare-rejected";
            detail = null;
            Workload2Backend backend = Workload2Backend.CreateMultiplayerTransactionBackend();
            if (backend == null ||
                !WorkloadMultiplayerPayloadCodec.TryDeserialize(
                    request.Payload.CopyBytes(), out var record, out detail)) return false;
            WorkloadOperationResult<WorkloadTemplate> payload = WorkloadV2RecordConverter.TryToTemplate(record);
            if (!payload.Succeeded) { detail = payload.Message; return false; }
            WorkloadOperationResult<WorkloadSession> rebuilt = backend.BuildMultiplayerSession(request, payload.Value);
            if (!rebuilt.Succeeded) { detail = rebuilt.Message; return false; }
            WorkloadDecisionKind decision = ToDecision(request.Operation);
            WorkloadOperationResult<WorkloadPreparedTransaction> prepared =
                backend.PrepareMultiplayerCommit(
                    request,
                    rebuilt.Value,
                    decision,
                    request.TargetWorkloadId,
                    payload.Value.Label);
            if (!prepared.Succeeded)
            {
                detail = prepared.Message;
                return false;
            }
            pending = new PendingPeerCommit
            {
                TableKey = request.TableKey,
                Backend = backend,
                Session = rebuilt.Value,
                Prepared = prepared.Value,
                Decision = decision,
                TargetStableId = request.TargetWorkloadId,
                ForkLabel = payload.Value.Label,
                Result = prepared.Value.Validation,
                ReportFingerprint = ReportFingerprint(prepared.Value.Validation)
            };
            code = "prepared";
            detail = prepared.Value.Validation.Message;
            return true;
        }

        private bool Rollback(WorkloadTransactionRequest request)
        {
            if (request == null || !_pending.TryGetValue(request.TableKey, out var pending)) return true;
            return pending.Backend == null || pending.Backend.AbortMultiplayerCommit(pending.Lease);
        }

        private static WorkloadTransactionWireControl Control(
            WorkloadTransactionRequest request, WorkloadTransactionState state,
            WorkloadTransactionPhase phase,
            WorkloadTransactionControlKind controlKind,
            bool accepted,
            bool rollback,
            string code,
            string detail,
            bool isRollbackReport = false)
        {
            var control = new WorkloadTransactionWireControl
            {
                Phase = (byte)phase,
                ControlKind = (byte)controlKind,
                Accepted = accepted,
                RequiresRollback = rollback,
                IsRollbackReport = isRollbackReport,
                RequestId = request.RequestId,
                RequestFingerprint = request.RequestFingerprint,
                HostSessionEpoch = request.ExpectedRevisions.HostSessionEpoch,
                RosterFingerprint = request.ExpectedRevisions.RosterFingerprint,
                PeerKey = MultiplayerBridge.LocalPlayerName,
                Code = code,
                Detail = detail,
                Sequence = Next(state)
            };
            WorkloadTransactionMultiplayer.PopulateContext(request, control);
            return control;
        }

        private static string ReportFingerprint(WorkloadV2CommitResult result)
        {
            string canonical = (result?.Succeeded == true ? "1" : "0") + "\u001f" +
                (result?.StableId ?? string.Empty) + "\u001f" +
                (result?.ResultTemplate?.SemanticFingerprint ?? string.Empty) + "\u001f" +
                (result?.Report?.ChangedCount ?? 0).ToString(CultureInfo.InvariantCulture) + "\u001f" +
                (result?.Report?.ClearedCount ?? 0).ToString(CultureInfo.InvariantCulture);
            return WorkloadCanonical.Fingerprint(canonical);
        }

        private static long Next(WorkloadTransactionState state) =>
            state == null || state.Sequence == long.MaxValue ? long.MaxValue : state.Sequence + 1;

        private static WorkloadTransactionOperation ToOperation(WorkloadDecisionKind decision) =>
            decision == WorkloadDecisionKind.Apply ? WorkloadTransactionOperation.Apply :
            decision == WorkloadDecisionKind.Update ? WorkloadTransactionOperation.Update :
            WorkloadTransactionOperation.Fork;

        private static WorkloadMultiplayerCommitState ToCommitState(
            WorkloadTransactionTerminalState terminalState)
        {
            switch (terminalState)
            {
                case WorkloadTransactionTerminalState.Succeeded:
                    return WorkloadMultiplayerCommitState.Succeeded;
                case WorkloadTransactionTerminalState.Rejected:
                    return WorkloadMultiplayerCommitState.Rejected;
                case WorkloadTransactionTerminalState.Aborted:
                    return WorkloadMultiplayerCommitState.Aborted;
                case WorkloadTransactionTerminalState.TimedOut:
                    return WorkloadMultiplayerCommitState.TimedOut;
                case WorkloadTransactionTerminalState.RolledBack:
                    return WorkloadMultiplayerCommitState.RolledBack;
                case WorkloadTransactionTerminalState.RollbackFailed:
                    return WorkloadMultiplayerCommitState.RollbackFailed;
                default:
                    return WorkloadMultiplayerCommitState.Failed;
            }
        }

        private static WorkloadDecisionKind ToDecision(WorkloadTransactionOperation operation) =>
            operation == WorkloadTransactionOperation.Apply ? WorkloadDecisionKind.Apply :
            operation == WorkloadTransactionOperation.Update ? WorkloadDecisionKind.Update :
            WorkloadDecisionKind.Fork;

        private static WorkloadMultiplayerCommitStatus Status(
            string requestId, WorkloadMultiplayerCommitState state,
            WorkloadDiagnosticCode code, string message, WorkloadV2CommitResult result = null)
        {
            var status = new WorkloadMultiplayerCommitStatus(requestId, state, code, message, result);
            Workload2Backend.PublishMultiplayerStatus(status);
            return status;
        }
    }

    internal static class WorkloadMultiplayerPayloadCodec
    {
        internal static bool TrySerialize(
            WorkloadV2PersistenceRecord record, out byte[] bytes, out string diagnostic)
        {
            bytes = null;
            diagnostic = null;
            try
            {
                record?.NormalizeStableState();
                if (record == null || record.SchemaVersion != WorkloadSchema.CurrentVersion)
                    throw new InvalidOperationException("Only current schema-v2 workload payloads are synchronized.");
                var serializer = new XmlSerializer(typeof(WorkloadV2PersistenceRecord));
                using (var stream = new MemoryStream())
                {
                    serializer.Serialize(stream, record);
                    bytes = stream.ToArray();
                }
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "The canonical schema-v2 workload payload could not be encoded: " + exception.Message;
                return false;
            }
        }

        internal static bool TryDeserialize(
            byte[] bytes, out WorkloadV2PersistenceRecord record, out string diagnostic)
        {
            record = null;
            diagnostic = null;
            try
            {
                if (bytes == null || bytes.Length == 0 || bytes.Length > WorkloadTransactionPayload.MaxPayloadBytes)
                    throw new InvalidOperationException("The synchronized workload payload size is invalid.");
                var serializer = new XmlSerializer(typeof(WorkloadV2PersistenceRecord));
                using (var stream = new MemoryStream(bytes, false))
                    record = serializer.Deserialize(stream) as WorkloadV2PersistenceRecord;
                record?.NormalizeStableState();
                if (record == null || record.SchemaVersion != WorkloadSchema.CurrentVersion)
                    throw new InvalidOperationException("The synchronized workload payload schema is unsupported.");
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "The canonical schema-v2 workload payload could not be decoded: " + exception.Message;
                record = null;
                return false;
            }
        }
    }

    internal sealed class WorkloadLiveBaselineCapture
    {
        internal WorkloadLiveBaselineCapture(
            WorkloadProjectedState state,
            WorkloadRuntimeBaseline runtimeBaseline,
            WorkloadBackendDimensionBaseline backendBaseline = null)
        {
            State = state ?? WorkloadProjectedState.Empty;
            RuntimeBaseline = runtimeBaseline;
            BackendBaseline = backendBaseline;
        }

        internal WorkloadProjectedState State { get; private set; }
        internal WorkloadRuntimeBaseline RuntimeBaseline { get; private set; }
        internal WorkloadBackendDimensionBaseline BackendBaseline { get; private set; }
    }

    /// <summary>
    /// Exact live baselines for typed V2 dimensions.  WorkloadSession is the
    /// public projection/session contract and deliberately keeps its original
    /// shape; this adapter carries the service-owned snapshots required by the
    /// central transaction without exposing or mutating backing dictionaries.
    /// </summary>
    internal sealed class WorkloadBackendDimensionBaseline
    {
        internal readonly Dictionary<WorkloadScheduleTargetKey, TimePriorityLiveScheduleSnapshot> Schedules =
            new Dictionary<WorkloadScheduleTargetKey, TimePriorityLiveScheduleSnapshot>();
        internal readonly Dictionary<WorkloadSpecificJobTargetKey, WorkloadSpecificPriorityBaseline> SpecificPriorities =
            new Dictionary<WorkloadSpecificJobTargetKey, WorkloadSpecificPriorityBaseline>();
        internal readonly Dictionary<WorkloadWorkTypeOrderKey, WorkloadWorkTypeOrderBaseline> WorkTypeOrders =
            new Dictionary<WorkloadWorkTypeOrderKey, WorkloadWorkTypeOrderBaseline>();
        internal readonly HashSet<string> PresentationSettingIds =
            new HashSet<string>(StringComparer.Ordinal);

        internal WorkloadPresentationSettingsTransaction SettingsWriter { get; set; }
        internal WorkloadPresentationSettingsSnapshot SettingsSnapshot { get; set; }
        internal int SpecificJobRevision { get; set; }
        internal int ScheduleRevision { get; set; }
        internal long AuthorityRevision { get; set; }
        internal string TaxonomyFingerprint { get; set; }
        internal int PersistenceRevision { get; set; }
        internal string PersistenceFingerprint { get; set; }
        internal bool HasPersistenceBaseline { get; set; }

        internal bool HasTypedRuntimeState =>
            Schedules.Count > 0 || SpecificPriorities.Count > 0 ||
            WorkTypeOrders.Count > 0 || SettingsSnapshot != null;
    }

    internal sealed class WorkloadSpecificPriorityBaseline
    {
        internal WorkloadSpecificJobTargetKey Key { get; set; }
        internal Pawn Pawn { get; set; }
        internal WorkTypeDef WorkType { get; set; }
        internal WorkGiverDef WorkGiver { get; set; }
        internal bool IsGlobal { get; set; }
        internal bool HasLocalOverride { get; set; }
        internal int LocalPriority { get; set; }
        internal WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot GlobalSnapshot { get; set; }
    }

    internal sealed class WorkloadWorkTypeOrderBaseline
    {
        internal WorkloadWorkTypeOrderKey Key { get; set; }
        internal Pawn Pawn { get; set; }
        internal WorkTypeDef WorkType { get; set; }
        internal bool IsGlobal { get; set; }
        internal WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot LocalSnapshot { get; set; }
        internal WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot GlobalSnapshot { get; set; }
    }

    internal sealed class WorkloadV2ApplyService
    {
        private readonly GameComponent_BWTWorldSettings _component;
        private readonly Dictionary<string, WorkloadBackendDimensionBaseline> _backendBaselines =
            new Dictionary<string, WorkloadBackendDimensionBaseline>(StringComparer.Ordinal);

        internal WorkloadV2ApplyService(GameComponent_BWTWorldSettings component)
        {
            _component = component;
        }

        internal void RememberBackendBaseline(
            WorkloadTemplate template,
            WorkloadBackendDimensionBaseline baseline)
        {
            RememberBackendBaseline(
                WorkloadSession.GetSourceIdentity(template),
                baseline);
        }

        internal void RememberBackendBaseline(
            string identity,
            WorkloadBackendDimensionBaseline baseline)
        {
            if (string.IsNullOrWhiteSpace(identity) || baseline == null)
            {
                return;
            }

            _backendBaselines[identity] = baseline;
        }

        internal WorkloadOperationResult RekeyBackendBaseline(
            WorkloadSession session,
            WorkloadPersistenceReceipt receipt)
        {
            string oldIdentity = session?.SourceIdentity ?? string.Empty;
            string newIdentity = receipt?.TargetIdentity ?? string.Empty;
            if (session == null || receipt == null ||
                string.IsNullOrWhiteSpace(oldIdentity) ||
                string.IsNullOrWhiteSpace(newIdentity) ||
                !StringComparer.Ordinal.Equals(oldIdentity, receipt.SourceIdentity) ||
                !StringComparer.Ordinal.Equals(
                    WorkloadSession.GetSourceIdentity(session.SourceTemplate),
                    oldIdentity) ||
                receipt.PersistenceRevision < 0 ||
                string.IsNullOrWhiteSpace(receipt.PersistenceFingerprint))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The V2 service-owned baseline rekey receipt is not authoritative.");
            }

            if (!_backendBaselines.TryGetValue(oldIdentity, out var baseline) ||
                baseline == null ||
                !baseline.HasPersistenceBaseline ||
                baseline.PersistenceRevision != receipt.PreviousPersistenceRevision ||
                !StringComparer.Ordinal.Equals(
                    baseline.PersistenceFingerprint,
                    receipt.PreviousPersistenceFingerprint))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The V2 service-owned baseline no longer matches the committed persistence receipt.");
            }

            WorkloadV2PersistenceEnvelope store = _component?.EnsureWorkloadV2Persistence();
            if (store == null || store.IsReadOnlyDiagnostic ||
                store.PersistenceRevision != receipt.PersistenceRevision ||
                !StringComparer.Ordinal.Equals(
                    store.PersistenceFingerprint,
                    receipt.PersistenceFingerprint) ||
                store.HasDuplicateStableId(receipt.TargetStableId))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The local V2 persistence envelope does not match the committed receipt.");
            }

            WorkloadV2PersistenceRecord targetRecord =
                store.Find(receipt.TargetStableId);
            WorkloadOperationResult<WorkloadTemplate> targetTemplate =
                WorkloadV2RecordConverter.TryToTemplate(targetRecord);
            if (!targetTemplate.Succeeded || targetTemplate.Value == null ||
                !StringComparer.Ordinal.Equals(
                    WorkloadSession.GetSourceIdentity(targetTemplate.Value),
                    newIdentity) ||
                (receipt.DecisionKind == WorkloadDecisionKind.Fork &&
                 !StringComparer.Ordinal.Equals(
                     store.CurrentWorkloadId,
                     receipt.TargetStableId)))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The local V2 target record does not match the committed receipt.");
            }

            if (!StringComparer.Ordinal.Equals(oldIdentity, newIdentity) &&
                _backendBaselines.ContainsKey(newIdentity))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The V2 target identity already has a service-owned baseline.");
            }

            // The typed/runtime snapshots remain the exact opening snapshots.
            // Only persistence metadata is advanced after the authoritative
            // round trip; no live recapture is permitted on this path.
            if (!StringComparer.Ordinal.Equals(oldIdentity, newIdentity))
            {
                _backendBaselines.Remove(oldIdentity);
                _backendBaselines.Add(newIdentity, baseline);
            }

            baseline.PersistenceRevision = receipt.PersistenceRevision;
            baseline.PersistenceFingerprint = receipt.PersistenceFingerprint;
            baseline.HasPersistenceBaseline = true;
            return WorkloadOperationResult.Ok();
        }

        internal bool TryGetBackendBaseline(
            WorkloadSession session,
            out WorkloadBackendDimensionBaseline baseline)
        {
            baseline = null;
            string identity = session?.SourceIdentity ?? string.Empty;
            return !string.IsNullOrWhiteSpace(identity) &&
                _backendBaselines.TryGetValue(identity, out baseline) &&
                baseline != null;
        }

        internal WorkloadOperationResult<WorkloadPersistenceReceipt> RecoverPersistenceReceipt(
            WorkloadSession session,
            WorkloadDecisionKind decisionKind,
            string targetStableId,
            string forkLabel)
        {
            if (session == null || session.SourceTemplate == null ||
                session.SourceTemplate.Definition == null)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 preview source required for persistence receipt recovery is missing.");
            }

            if (decisionKind != WorkloadDecisionKind.Update &&
                decisionKind != WorkloadDecisionKind.Fork)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "A V2 persistence receipt can only be recovered for Update or Fork.");
            }

            string sourceStableId = session.SourceTemplate.StableId ?? string.Empty;
            string sourceIdentity = session.SourceIdentity ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceStableId) ||
                string.IsNullOrWhiteSpace(sourceIdentity) ||
                !StringComparer.Ordinal.Equals(
                    sourceIdentity,
                    WorkloadSession.GetSourceIdentity(session.SourceTemplate)))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The V2 preview source identity is stale or unavailable for persistence receipt recovery.");
            }

            targetStableId = targetStableId ?? string.Empty;
            if (decisionKind == WorkloadDecisionKind.Update)
            {
                if (!StringComparer.Ordinal.Equals(targetStableId, sourceStableId))
                {
                    return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 Update receipt target does not match the active preview source.");
                }
            }
            else if (string.IsNullOrWhiteSpace(targetStableId) ||
                     StringComparer.Ordinal.Equals(targetStableId, sourceStableId))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 Fork receipt target is missing or reuses the preview source ID.");
            }

            if (!_backendBaselines.TryGetValue(sourceIdentity, out var baseline) ||
                baseline == null ||
                !baseline.HasPersistenceBaseline ||
                baseline.PersistenceRevision < 0 ||
                string.IsNullOrWhiteSpace(baseline.PersistenceFingerprint))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The old V2 service-owned persistence baseline is unavailable for receipt recovery.");
            }

            WorkloadV2PersistenceEnvelope store = _component?.EnsureWorkloadV2Persistence();
            store?.RefreshDiagnostics();
            if (store == null || store.IsReadOnlyDiagnostic ||
                store.PersistenceRevision < 0)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The current V2 persistence envelope is not authoritative for receipt recovery.");
            }

            WorkloadOperationResult<WorkloadPersistenceReceipt> receipt =
                BuildPersistenceReceipt(
                    store,
                    null,
                    decisionKind,
                    sourceStableId,
                    targetStableId,
                    sourceIdentity,
                    baseline);
            if (!receipt.Succeeded || receipt.Value == null)
            {
                return receipt;
            }

            return WorkloadPersistenceReceiptRecovery.TryRecover(
                session,
                decisionKind,
                targetStableId,
                forkLabel,
                baseline.PersistenceRevision,
                baseline.PersistenceFingerprint,
                new WorkloadPersistenceRecoverySnapshot(
                    receipt.Value.TargetTemplate,
                    receipt.Value.PersistenceRevision,
                    receipt.Value.PersistenceFingerprint,
                    receipt.Value.CurrentWorkloadId));
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
            bool captureSchedules =
                ownership.Owns(WorkloadStateDimension.Schedules);
            bool capturePresentationSettings =
                ownership.Owns(WorkloadStateDimension.PresentationSettings);

            if (!captureParentPriorities &&
                !captureManualModes &&
                !captureSpecificOverrides &&
                !captureSpecificOrder &&
                !captureSchedules &&
                !capturePresentationSettings)
            {
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Ok(
                    new WorkloadLiveBaselineCapture(
                        templateState,
                        new WorkloadRuntimeBaseline(),
                        CapturePersistenceBaseline()));
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
                    captureSpecificOrder ||
                    captureSchedules)
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

                    liveManualMode = ParentPriorityRead.GetLiveManualMode(true);
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
                    if (scope.IsExplicitlyExcluded(entry.Key.Pawn)) continue;
                    if (!IsLiveBaselineEntryInScope(scope, entry.Key.Pawn, runtime)) continue;
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
                    if (scope.IsExplicitlyExcluded(entry.Key.Pawn)) continue;
                    if (!IsLiveBaselineEntryInScope(scope, entry.Key.Pawn, runtime)) continue;
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
                    if (scope.IsExplicitlyExcluded(entry.Key.Pawn)) continue;
                    if (!IsLiveBaselineEntryInScope(scope, entry.Key.Pawn, runtime)) continue;
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
                    if (scope.IsExplicitlyExcluded(entry.Key.Pawn)) continue;
                    if (!IsLiveBaselineEntryInScope(scope, entry.Key.Pawn, runtime)) continue;
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
                WorkloadBackendDimensionBaseline backendBaseline =
                    CaptureBackendDimensions(
                        template,
                        templateState,
                        ownership,
                        runtime,
                        report);
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
                        runtimeBaseline,
                        backendBaseline));
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

        private static bool IsLiveBaselineEntryInScope(
            WorkloadScope scope,
            PawnKey pawnKey,
            RuntimeContext runtime)
        {
            // Dynamic scopes can retain identities from an earlier roster. The
            // commit path already skips those entries; baseline capture must
            // apply the same boundary before dereferencing pawn-local state.
            // Preserve the existing fail-closed validation for malformed or
            // missing identities by treating them as candidates for resolve.
            if (scope == null || pawnKey == null || !pawnKey.IsValid || runtime == null)
            {
                return true;
            }

            if (!runtime.Pawns.TryGetValue(pawnKey.Value, out Pawn pawn) || pawn == null)
            {
                return true;
            }

            return IsInScope(scope, pawn, runtime);
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

        private WorkloadBackendDimensionBaseline CapturePersistenceBaseline()
        {
            var baseline = new WorkloadBackendDimensionBaseline();
            WorkloadV2PersistenceEnvelope store = _component?.EnsureWorkloadV2Persistence();
            if (store != null)
            {
                store.RefreshDiagnostics();
                baseline.PersistenceRevision = store.PersistenceRevision;
                baseline.PersistenceFingerprint = store.ComputeContentFingerprint();
                baseline.HasPersistenceBaseline = !store.IsReadOnlyDiagnostic;
            }

            baseline.SpecificJobRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            baseline.ScheduleRevision = TimePriorityService.CurrentVersion;
            baseline.AuthorityRevision =
                PriorityAuthorityBroker.GetObservationalAuthorityRevision();
            return baseline;
        }

        private WorkloadBackendDimensionBaseline CaptureBackendDimensions(
            WorkloadTemplate template,
            WorkloadProjectedState state,
            WorkloadOwnershipDimensions ownership,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            WorkloadBackendDimensionBaseline baseline = CapturePersistenceBaseline();
            baseline.TaxonomyFingerprint = ComputeTaxonomyFingerprint(runtime);
            WorkloadScope scope = template?.Definition?.Scope ?? WorkloadScope.Empty;

            if (ownership.Owns(WorkloadStateDimension.Schedules))
            {
                if (state.Schedules.Count > 0 && state.ScheduleIntents.Count == 0)
                {
                    AbortUnsupportedLegacyDimension(
                        WorkloadStateDimension.Schedules,
                        "The workload contains legacy schedule entries without a complete linked/pinned payload.",
                        report);
                }

                for (int i = 0; i < state.ScheduleIntents.Count; i++)
                {
                    WorkloadScheduleIntentEntry entry = state.ScheduleIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion ||
                        (!entry.Key.IsGlobal && scope.IsExplicitlyExcluded(entry.Key.Pawn))) continue;
                    if (!TryCaptureScheduleBaseline(
                            entry.Key,
                            runtime,
                            out TimePriorityLiveScheduleSnapshot snapshot,
                            out string reason))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live schedule baseline for " + entry.Key +
                            " could not be captured safely: " + reason);
                    }

                    baseline.Schedules[entry.Key] = snapshot;
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) &&
                state.SpecificPriorityIntents.Count > 0)
            {
                for (int i = 0; i < state.SpecificPriorityIntents.Count; i++)
                {
                    WorkloadSpecificPriorityIntentEntry entry =
                        state.SpecificPriorityIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion ||
                        (!entry.Key.IsGlobal && scope.IsExplicitlyExcluded(entry.Key.Pawn))) continue;
                    if (!TryCaptureSpecificPriorityBaseline(
                            entry.Key,
                            runtime,
                            out WorkloadSpecificPriorityBaseline captured,
                            out string reason))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live specific-job baseline for " + entry.Key +
                            " could not be captured safely: " + reason);
                    }

                    baseline.SpecificPriorities[entry.Key] = captured;
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder) &&
                state.WorkTypeOrderIntents.Count > 0)
            {
                for (int i = 0; i < state.WorkTypeOrderIntents.Count; i++)
                {
                    WorkloadWorkTypeOrderIntentEntry entry =
                        state.WorkTypeOrderIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion ||
                        (!entry.Key.IsGlobal && scope.IsExplicitlyExcluded(entry.Key.Pawn))) continue;
                    if (!TryCaptureWorkTypeOrderBaseline(
                            entry.Key,
                            runtime,
                            out WorkloadWorkTypeOrderBaseline captured,
                            out string reason))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The live work-giver order baseline for " + entry.Key +
                            " could not be captured safely: " + reason);
                    }

                    baseline.WorkTypeOrders[entry.Key] = captured;
                }
            }

            if (ownership.Owns(WorkloadStateDimension.PresentationSettings))
            {
                var settingIds = new List<string>();
                for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry =
                        state.PresentationSettingIntents[i];
                    if (entry != null && !entry.Intent.IsNoOpinion &&
                        !string.IsNullOrWhiteSpace(entry.Key) &&
                        !settingIds.Contains(entry.Key))
                    {
                        settingIds.Add(entry.Key);
                    }
                }

                // Older current-schema records may still carry a scalar legacy
                // presentation list. It is safe to capture it only through the
                // selected BWT writer; persistence conversion will retain it
                // until the next typed Update/Fork.
                if (state.PresentationSettingIntents.Count == 0)
                {
                    for (int i = 0; i < state.PresentationSettings.Count; i++)
                    {
                        string key = state.PresentationSettings[i]?.Key;
                        if (!string.IsNullOrWhiteSpace(key) && !settingIds.Contains(key))
                        {
                            settingIds.Add(key);
                        }
                    }
                }

                if (settingIds.Count > 0)
                {
                    baseline.SettingsWriter = BWTWorkloadSettingsOwnershipPolicy.CreateApplyWriter();
                    if (!baseline.SettingsWriter.TryCapture(
                            settingIds,
                            out WorkloadPresentationSettingsSnapshot snapshot,
                            out string reason))
                    {
                        Abort(
                            WorkloadDiagnosticCode.UnsupportedOperation,
                            "The workload-owned presentation baseline could not be captured: " + reason);
                    }

                    baseline.SettingsSnapshot = snapshot;
                    for (int i = 0; i < settingIds.Count; i++)
                    {
                        baseline.PresentationSettingIds.Add(settingIds[i]);
                    }
                }
            }

            return baseline;
        }

        private static void AbortUnsupportedLegacyDimension(
            WorkloadStateDimension dimension,
            string message,
            WorkloadV2CommitReport report)
        {
            report?.Add(
                WorkloadV2CommitMessageKind.Unsupported,
                "dimension." + dimension + ".legacy",
                dimension.ToString(),
                message);
            Abort(WorkloadDiagnosticCode.UnsupportedLegacyState, message);
        }

        private static string ComputeTaxonomyFingerprint(RuntimeContext runtime)
        {
            var builder = new System.Text.StringBuilder();
            if (runtime?.WorkTypes == null) return WorkloadCanonical.Fingerprint(string.Empty);
            var names = new List<string>(runtime.WorkTypes.Keys);
            names.Sort(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                WorkTypeDef workType = runtime.WorkTypes[names[i]];
                builder.Append(WorkloadCanonical.Encode(names[i])).Append(':');
                IReadOnlyList<WorkGiver> workGivers =
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
                for (int j = 0; j < workGivers.Count; j++)
                {
                    WorkGiverDef workGiver = workGivers[j]?.def;
                    if (workGiver == null) continue;
                    builder.Append(WorkloadCanonical.Encode(workGiver.defName)).Append(',');
                    builder.Append(WorkloadCanonical.Encode(
                        WorkGiverReassignmentManager.GetTargetWorkType(workGiver)?.defName));
                }
                builder.Append(';');
            }

            return WorkloadCanonical.Fingerprint(builder.ToString());
        }

        private static bool TryCaptureScheduleBaseline(
            WorkloadScheduleTargetKey key,
            RuntimeContext runtime,
            out TimePriorityLiveScheduleSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = string.Empty;
            if (key == null || !key.IsValid)
            {
                reason = "the typed schedule target is invalid";
                return false;
            }

            int fallbackPriority;
            if (key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType)
            {
                if (!TryResolveLiveEntry(
                        new WorkloadParentPriorityKey(key.Pawn, key.WorkType),
                        runtime,
                        out Pawn pawn,
                        out WorkTypeDef workType,
                        out reason))
                {
                    return false;
                }

                fallbackPriority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                    pawn.workSettings,
                    workType);
            }
            else if (key.IsGlobal)
            {
                if (!TryResolveGlobalWorkGiver(
                        key.WorkType,
                        key.WorkGiver,
                        runtime,
                        out WorkTypeDef workType,
                        out WorkGiverDef workGiver,
                        out reason))
                {
                    return false;
                }

                // A global WorkGiver schedule inherits the exact shared
                // WorkGiver priority when one exists, otherwise the normal
                // enabled default.  Capturing the default unconditionally
                // made an existing global override look like a schedule
                // change and could rebase linked hours incorrectly.
                fallbackPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                    null,
                    workGiver,
                    WorkPrioritySystem.GetDefaultEnabledPriority());
            }
            else
            {
                if (!TryResolveSpecificLiveEntry(
                        new WorkloadSpecificJobKey(
                            key.Pawn,
                            key.WorkType,
                            key.WorkGiver),
                        runtime,
                        out Pawn pawn,
                        out WorkTypeDef workType,
                        out WorkGiverDef workGiver,
                        out reason))
                {
                    return false;
                }

                int parentPriority = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                    pawn.workSettings,
                    workType);
                fallbackPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(
                    pawn,
                    workGiver,
                    parentPriority);
            }

            return WorkloadTimePriorityAdapter.TryCaptureLiveScheduleSnapshot(
                key,
                fallbackPriority,
                out snapshot,
                out reason);
        }

        private static bool TryCaptureSpecificPriorityBaseline(
            WorkloadSpecificJobTargetKey key,
            RuntimeContext runtime,
            out WorkloadSpecificPriorityBaseline baseline,
            out string reason)
        {
            baseline = null;
            reason = string.Empty;
            if (key == null || !key.IsValid)
            {
                reason = "the typed specific-job target is invalid";
                return false;
            }

            if (key.IsGlobal)
            {
                if (!TryResolveGlobalWorkGiver(
                        key.WorkType,
                        key.WorkGiver,
                        runtime,
                        out WorkTypeDef workType,
                        out WorkGiverDef workGiver,
                        out reason))
                {
                    return false;
                }

                baseline = new WorkloadSpecificPriorityBaseline
                {
                    Key = key,
                    WorkType = workType,
                    WorkGiver = workGiver,
                    IsGlobal = true,
                    GlobalSnapshot = WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(
                        workGiver.defName)
                };
                return true;
            }

            if (!TryResolveSpecificLiveEntry(
                    key.ToLegacyKey(),
                    runtime,
                    out Pawn pawn,
                    out WorkTypeDef localWorkType,
                    out WorkGiverDef localWorkGiver,
                    out reason))
            {
                return false;
            }

            baseline = new WorkloadSpecificPriorityBaseline
            {
                Key = key,
                Pawn = pawn,
                WorkType = localWorkType,
                WorkGiver = localWorkGiver,
                IsGlobal = false,
                HasLocalOverride = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                    pawn,
                    localWorkGiver,
                    out int priority),
                LocalPriority = priority
            };
            return true;
        }

        private static bool TryCaptureWorkTypeOrderBaseline(
            WorkloadWorkTypeOrderKey key,
            RuntimeContext runtime,
            out WorkloadWorkTypeOrderBaseline baseline,
            out string reason)
        {
            baseline = null;
            reason = string.Empty;
            if (key == null || !key.IsValid)
            {
                reason = "the typed WorkType order target is invalid";
                return false;
            }

            if (key.IsGlobal)
            {
                if (runtime == null || !runtime.WorkTypes.TryGetValue(
                        key.WorkType.Value,
                        out WorkTypeDef globalWorkType) || globalWorkType == null)
                {
                    reason = "the global WorkTypeDef identity is missing";
                    return false;
                }

                baseline = new WorkloadWorkTypeOrderBaseline
                {
                    Key = key,
                    WorkType = globalWorkType,
                    IsGlobal = true,
                    GlobalSnapshot = WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(
                        globalWorkType.defName)
                };
                return true;
            }

            if (!TryResolveLiveEntry(
                    new WorkloadParentPriorityKey(key.Pawn, key.WorkType),
                    runtime,
                    out Pawn pawn,
                    out WorkTypeDef workType,
                    out reason))
            {
                return false;
            }

            baseline = new WorkloadWorkTypeOrderBaseline
            {
                Key = key,
                Pawn = pawn,
                WorkType = workType,
                IsGlobal = false,
                LocalSnapshot = WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                    pawn,
                    workType)
            };
            return true;
        }

        private static bool TryResolveGlobalWorkGiver(
            WorkTypeKey workTypeKey,
            WorkGiverKey workGiverKey,
            RuntimeContext runtime,
            out WorkTypeDef workType,
            out WorkGiverDef workGiver,
            out string reason)
        {
            workType = null;
            workGiver = null;
            reason = string.Empty;
            if (runtime == null || workTypeKey == null || workGiverKey == null ||
                !runtime.WorkTypes.TryGetValue(workTypeKey.Value, out workType) ||
                !runtime.WorkGivers.TryGetValue(workGiverKey.Value, out workGiver) ||
                workType == null || workGiver == null)
            {
                reason = "the global WorkTypeDef or WorkGiverDef identity is missing";
                return false;
            }

            if (WorkGiverReassignmentManager.GetTargetWorkType(workGiver) != workType)
            {
                reason = "the WorkGiverDef taxonomy no longer matches the WorkTypeDef identity";
                return false;
            }

            return true;
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
                templateState.CopyExcludedStagedStates(),
                templateState.ParentPriorityIntents,
                templateState.ManualModeIntents,
                templateState.ScheduleIntents,
                templateState.SpecificPriorityIntents,
                templateState.WorkTypeOrderIntents,
                templateState.PresentationSettingIntents);
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
            string forkLabel,
            Func<WorkloadPersistenceReceipt, bool> rebaseAfterPersistence = null)
        {
            return CommitCore(
                session,
                decisionKind,
                forkStableId,
                forkLabel,
                new WorkloadCommitExecutionContext
                {
                    PersistenceRebase = rebaseAfterPersistence
                });
        }

        internal WorkloadV2CommitResult ValidatePrepared(
            WorkloadSession session,
            WorkloadDecisionKind decisionKind,
            string forkStableId,
            string forkLabel)
        {
            return CommitCore(
                session, decisionKind, forkStableId, forkLabel,
                new WorkloadCommitExecutionContext
                {
                    ValidateOnly = true,
                    MultiplayerAuthorized = true
                });
        }

        internal WorkloadV2CommitResult ExecutePrepared(
            WorkloadSession session,
            WorkloadDecisionKind decisionKind,
            string forkStableId,
            string forkLabel,
            WorkloadMutationAuthorization authorization,
            string preparedPlanFingerprint,
            object requestIdentity,
            out WorkloadCommitRollbackLease rollbackLease)
        {
            var context = new WorkloadCommitExecutionContext
            {
                MultiplayerAuthorized = true,
                RetainRollbackUntilConfirmation = true,
                MutationAuthorization = authorization,
                PreparedPlanFingerprint = preparedPlanFingerprint ?? string.Empty,
                RequestIdentity = requestIdentity,
                SessionIdentity = session
            };
            WorkloadV2CommitResult result = CommitCore(
                session, decisionKind, forkStableId, forkLabel, context);
            rollbackLease = context.RollbackLease;
            return result;
        }

        internal bool AbortPrepared(WorkloadCommitRollbackLease lease)
        {
            return lease == null || lease.Rollback();
        }

        internal bool ConfirmPrepared(WorkloadCommitRollbackLease lease)
        {
            return lease == null || lease.Confirm();
        }

        private WorkloadCommitRollbackLease CreateRollbackLease(
            WorkloadCommitExecutionContext executionContext,
            WorkloadV2PersistenceEnvelope store,
            PersistenceMutation persistence,
            LiveMutationTransaction live,
            WorkloadPreviewPlan plan,
            WorkloadV2CommitReport report,
            bool recoveryRequired = false)
        {
            return new WorkloadCommitRollbackLease(
                () =>
                {
                    bool persistenceRestored = RollbackPersistence(store, persistence, report);
                    bool liveRestored = RollbackLive(live, report);
                    report.RollbackAttempted = true;
                    report.TemplateRestored = persistenceRestored;
                    report.LiveStateRestored = liveRestored;
                    report.TemplatePersisted = persistence != null &&
                        persistence.WasApplied && !persistenceRestored;
                    report.LiveStateChanged = !liveRestored;
                    NotifyCommitChanged(plan, live);
                    if (persistenceRestored && liveRestored)
                    {
                        executionContext?.MutationAuthorization?.FinalizeRollback();
                    }
                    return persistenceRestored && liveRestored;
                },
                () =>
                {
                    executionContext?.MutationAuthorization?.FinalizeSuccess();
                    return true;
                },
                recoveryRequired);
        }

        private WorkloadV2CommitResult CommitCore(
            WorkloadSession session,
            WorkloadDecisionKind decisionKind,
            string forkStableId,
            string forkLabel,
            WorkloadCommitExecutionContext executionContext)
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
            WorkloadBackendDimensionBaseline backendBaseline = null;

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
                if (decision?.Plan == null || !decision.Accepted)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        decision?.RejectionReason ??
                        "The V2 preview did not pass model validation.");
                }

                targetTemplate = decisionKind == WorkloadDecisionKind.Apply
                    ? session.BuildApplyTemplate()
                    : decision.ResultTemplate;
                targetTemplate = Workload2Backend.BuildEffectiveTemplate(
                    targetTemplate,
                    targetTemplate?.ProjectedState,
                    session.TemplateBaselineState);
                if (WorkloadV2OwnershipResolver.HasUnsupportedLegacyPayload(targetTemplate))
                {
                    Abort(
                        WorkloadDiagnosticCode.UnsupportedOperation,
                        "The V2 commit contains legacy schedule or presentation state without a typed transaction intent.");
                }
                WorkloadOwnershipDimensions effectiveOwnership =
                    WorkloadV2OwnershipResolver.Effective(
                        targetTemplate,
                        decision.Plan.AfterState,
                        session.TemplateBaselineState);
                plan = new WorkloadPreviewPlan(
                    decisionKind,
                    session.SourceTemplate,
                    decision.Plan.BeforeState,
                    decision.Plan.AfterState,
                    WorkloadSemanticDiff.Between(
                        decision.Plan.BeforeState,
                        decision.Plan.AfterState,
                        effectiveOwnership),
                    WorkloadValidator.Validate(targetTemplate));
                if (!plan.CanProceed)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The projected V2 workload did not pass effective-state validation.");
                }
                RequireValidTemplate(targetTemplate, report);

                if (executionContext?.PreparedPlanFingerprint.AnyNonWhitespace() == true &&
                    !StringComparer.Ordinal.Equals(
                        Workload2Backend.BuildPreparedPlanFingerprint(
                            plan,
                            decisionKind,
                            targetStableId),
                        executionContext.PreparedPlanFingerprint))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The synchronized workload execute no longer matches its immutable prepare plan.");
                }

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

                if (!TryGetBackendBaseline(
                        session,
                        out backendBaseline))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The V2 preview is missing the service-owned dimension baselines required by the central transaction.");
                }

                ValidateTypedStateForCommit(
                    targetTemplate,
                    backendBaseline,
                    decisionKind == WorkloadDecisionKind.Apply,
                    report);

                if (MultiplayerBridge.Active && executionContext?.ValidateOnly != true &&
                    !IsExecutionCapabilityBound(
                        executionContext,
                        session,
                        requestIdentity: executionContext?.RequestIdentity,
                        targetTemplate,
                        decisionKind,
                        targetStableId,
                        report))
                {
                    Abort(
                        WorkloadDiagnosticCode.MutationCapabilityRejected,
                        "The synchronized workload transaction capability is stale or not bound to this execute.");
                }

                if (decisionKind != WorkloadDecisionKind.Apply)
                {
                    EnsurePersistenceBaseline(
                        store,
                        backendBaseline,
                        report,
                        "persistence.preflight");
                }

                WorkloadScope scope = targetTemplate.Definition.Scope ?? WorkloadScope.Empty;
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

                    ValidateTypedRuntimeEntries(
                        session,
                        targetTemplate,
                        backendBaseline,
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
                        backendBaseline,
                        report,
                        executionContext?.MultiplayerAuthorized == true);
                    runtimePlan.MutationAuthorization = executionContext?.MutationAuthorization;

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
                else
                {
                    // Update and Fork do not apply the live colony, so they do
                    // not use Apply's optimistic value-baseline comparisons.
                    // They still must resolve every persisted runtime identity
                    // against the current catalog and reject taxonomy,
                    // service-revision, settings-ownership, or authority drift
                    // before staging a persistence mutation.
                    ValidatePersistenceRuntimeState(
                        session,
                        targetTemplate,
                        plan,
                        scope,
                        backendBaseline,
                        report);
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
                        converted.Value,
                        backendBaseline);
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
                        converted.Value,
                        backendBaseline);
                }

                if (runtimePlan.HasLiveMutations && executionContext?.ValidateOnly != true)
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

                if (executionContext?.ValidateOnly == true)
                {
                    report.IsSemanticNoOp = plan.Diff.IsEmpty;
                    return new WorkloadV2CommitResult(
                        true,
                        WorkloadDiagnosticCode.None,
                        "The V2 workload transaction prepared without mutation.",
                        report,
                        plan,
                        targetTemplate,
                        targetStableId);
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

                if (decisionKind != WorkloadDecisionKind.Apply &&
                    ((persistence != null && persistence.WasApplied) ||
                     (decisionKind == WorkloadDecisionKind.Update && plan.Diff.IsEmpty)))
                {
                    WorkloadOperationResult<WorkloadPersistenceReceipt> receipt =
                        BuildPersistenceReceipt(
                            store,
                            persistence,
                            decisionKind,
                            sourceStableId,
                            targetStableId,
                            session.SourceIdentity,
                            backendBaseline);
                    if (!receipt.Succeeded)
                    {
                        Abort(receipt.Code, receipt.Message);
                    }

                    executionContext.PersistenceReceipt = receipt.Value;
                    if (executionContext.RetainRollbackUntilConfirmation != true &&
                        (executionContext.PersistenceRebase == null ||
                         !executionContext.PersistenceRebase(receipt.Value)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 preview could not rebase from the authoritative persistence receipt.");
                    }
                }

                report.IsSemanticNoOp = plan.Diff.IsEmpty;
                if (executionContext?.RetainRollbackUntilConfirmation == true)
                {
                    executionContext.RollbackLease = CreateRollbackLease(
                        executionContext,
                        store,
                        persistence,
                        live,
                        plan,
                        report);
                }
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
                            ? "The V2 workload save was a semantic no-op."
                            : "The V2 workload template was saved.") + temporaryScopeNote
                        : "The V2 workload template was forked." + temporaryScopeNote;
                var successResult = new WorkloadV2CommitResult(
                    true,
                    WorkloadDiagnosticCode.None,
                    successMessage,
                    report,
                    plan,
                    targetTemplate,
                    targetStableId);
                successResult.PersistenceReceipt = executionContext?.PersistenceReceipt;
                return successResult;
            }
            catch (CommitAbortException exception)
            {
                bool rollbackWasNeeded =
                    (persistence != null &&
                     (persistence.WasApplied || persistence.RevisionAdvanced)) ||
                    (live != null && live.HasChanges);
                bool persistenceRestored = RollbackPersistence(store, persistence, report);
                bool liveRestored = RollbackLive(live, report);
                bool rollbackSucceeded = persistenceRestored && liveRestored;
                bool liveNetChanged = !liveRestored;
                report.RollbackAttempted = rollbackWasNeeded;
                report.TemplateRestored = persistenceRestored;
                report.LiveStateRestored = liveRestored;
                report.TemplatePersisted = persistence != null && persistence.WasApplied && !persistenceRestored;
                report.LiveStateChanged = liveNetChanged;
                if (!rollbackSucceeded && executionContext?.RetainRollbackUntilConfirmation == true)
                {
                    executionContext.RollbackLease = CreateRollbackLease(
                        executionContext,
                        store,
                        persistence,
                        live,
                        plan,
                        report,
                        recoveryRequired: true);
                }
                WorkloadDiagnosticCode resultCode = rollbackSucceeded
                    ? exception.Code
                    : WorkloadDiagnosticCode.RollbackFailed;
                string resultMessage = rollbackSucceeded
                    ? exception.Message
                    : exception.Message + " Rollback is incomplete and requires recovery.";
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    resultCode.ToString(),
                    targetStableId,
                    resultMessage);
                return new WorkloadV2CommitResult(
                    false,
                    resultCode,
                    resultMessage,
                    report,
                    plan,
                    null,
                    targetStableId);
            }
            catch (Exception exception)
            {
                bool rollbackWasNeeded =
                    (persistence != null &&
                     (persistence.WasApplied || persistence.RevisionAdvanced)) ||
                    (live != null && live.HasChanges);
                bool persistenceRestored = RollbackPersistence(store, persistence, report);
                bool liveRestored = RollbackLive(live, report);
                bool rollbackSucceeded = persistenceRestored && liveRestored;
                bool liveNetChanged = !liveRestored;
                report.RollbackAttempted = rollbackWasNeeded;
                report.TemplateRestored = persistenceRestored;
                report.LiveStateRestored = liveRestored;
                report.TemplatePersisted = persistence != null && persistence.WasApplied && !persistenceRestored;
                report.LiveStateChanged = liveNetChanged;
                if (!rollbackSucceeded && executionContext?.RetainRollbackUntilConfirmation == true)
                {
                    executionContext.RollbackLease = CreateRollbackLease(
                        executionContext,
                        store,
                        persistence,
                        live,
                        plan,
                        report,
                        recoveryRequired: true);
                }
                string resultMessage = rollbackSucceeded
                    ? "The V2 commit failed safely: " + exception.Message
                    : "The V2 commit failed and rollback requires recovery: " + exception.Message;
                WorkloadDiagnosticCode resultCode = rollbackSucceeded
                    ? WorkloadDiagnosticCode.InvalidState
                    : WorkloadDiagnosticCode.RollbackFailed;
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    resultCode.ToString(),
                    targetStableId,
                    resultMessage);
                return new WorkloadV2CommitResult(
                    false,
                    resultCode,
                    resultMessage,
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

        private static WorkloadOperationResult<WorkloadPersistenceReceipt> BuildPersistenceReceipt(
            WorkloadV2PersistenceEnvelope store,
            PersistenceMutation mutation,
            WorkloadDecisionKind decisionKind,
            string sourceStableId,
            string targetStableId,
            string sourceIdentity,
            WorkloadBackendDimensionBaseline baseline)
        {
            if (store == null ||
                (mutation == null && baseline == null) ||
                (mutation != null && !mutation.WasApplied) ||
                (baseline != null &&
                 (!baseline.HasPersistenceBaseline ||
                  baseline.PersistenceRevision < 0 ||
                  string.IsNullOrWhiteSpace(baseline.PersistenceFingerprint))) ||
                (decisionKind != WorkloadDecisionKind.Update &&
                 decisionKind != WorkloadDecisionKind.Fork))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 persistence receipt was requested before a committed mutation existed.");
            }

            if (store.HasDuplicateStableId(targetStableId))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId,
                    "The committed V2 target stable ID is no longer unique.");
            }

            WorkloadV2PersistenceRecord record = store.Find(targetStableId);
            if (record == null)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId,
                    "The committed V2 target record could not be round-tripped.");
            }

            WorkloadOperationResult<WorkloadTemplate> target =
                WorkloadV2RecordConverter.TryToTemplate(record);
            if (!target.Succeeded || target.Value == null)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    target.Code,
                    target.Message);
            }

            string persistenceFingerprint = store.ComputeContentFingerprint();
            if (string.IsNullOrWhiteSpace(store.PersistenceFingerprint) ||
                !StringComparer.Ordinal.Equals(
                    store.PersistenceFingerprint,
                    persistenceFingerprint))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The V2 persistence metadata was not authoritative after the write.");
            }

            string targetIdentity = WorkloadSession.GetSourceIdentity(target.Value);
            if (!StringComparer.Ordinal.Equals(
                    target.Value.StableId,
                    targetStableId) ||
                string.IsNullOrWhiteSpace(targetIdentity))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The committed V2 target identity could not be established safely.");
            }

            return WorkloadOperationResult<WorkloadPersistenceReceipt>.Ok(
                new WorkloadPersistenceReceipt(
                    decisionKind,
                    sourceStableId,
                    targetStableId,
                    sourceIdentity,
                    targetIdentity,
                    target.Value,
                    mutation?.PreviousRevision ?? baseline.PersistenceRevision,
                    mutation?.PreviousFingerprint ?? baseline.PersistenceFingerprint,
                    store.PersistenceRevision,
                    persistenceFingerprint,
                    store.CurrentWorkloadId));
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

        private static void ValidateTypedStateForCommit(
            WorkloadTemplate targetTemplate,
            WorkloadBackendDimensionBaseline baseline,
            bool requireLiveBaseline,
            WorkloadV2CommitReport report)
        {
            if (targetTemplate?.Definition == null)
            {
                Abort(WorkloadDiagnosticCode.InvalidState, "The typed V2 commit has no workload definition.");
            }

            WorkloadProjectedState state = targetTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            WorkloadOwnershipDimensions ownership = targetTemplate.Definition.OwnershipDimensions;
            if (ownership.Owns(WorkloadStateDimension.Schedules))
            {
                if (state.Schedules.Count > 0 && state.ScheduleIntents.Count == 0)
                {
                    AbortUnsupportedLegacyDimension(
                        WorkloadStateDimension.Schedules,
                        "The workload schedule payload is legacy-only and cannot preserve linked/pinned hour state.",
                        report);
                }

                for (int i = 0; i < state.ScheduleIntents.Count; i++)
                {
                    WorkloadScheduleIntentEntry entry = state.ScheduleIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (entry.Key == null || !entry.Key.IsValid ||
                        (entry.Intent.HasValue &&
                         (entry.Intent.Value == null || !entry.Intent.Value.IsValid)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The workload contains an invalid linked/pinned schedule intent.");
                    }

                    if (requireLiveBaseline &&
                        (baseline == null || !baseline.Schedules.ContainsKey(entry.Key)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The workload schedule intent has no exact live baseline: " + entry.Key + ".");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                for (int i = 0; i < state.SpecificPriorityIntents.Count; i++)
                {
                    WorkloadSpecificPriorityIntentEntry entry =
                        state.SpecificPriorityIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (entry.Key == null || !entry.Key.IsValid ||
                        (entry.Intent.HasValue &&
                         (!entry.Intent.Value.IsValid ||
                          WorkPrioritySystem.ClampPriority(entry.Intent.Value.Priority) !=
                          entry.Intent.Value.Priority)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The workload contains an invalid local/global specific-job priority intent.");
                    }

                    if (requireLiveBaseline &&
                        (baseline == null || !baseline.SpecificPriorities.ContainsKey(entry.Key)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The specific-job priority intent has no exact live baseline: " + entry.Key + ".");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                for (int i = 0; i < state.WorkTypeOrderIntents.Count; i++)
                {
                    WorkloadWorkTypeOrderIntentEntry entry = state.WorkTypeOrderIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (entry.Key == null || !entry.Key.IsValid ||
                        (entry.Intent.HasValue &&
                         (entry.Intent.Value == null || !entry.Intent.Value.IsValid)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The workload contains an invalid complete WorkGiver-order intent.");
                    }

                    if (requireLiveBaseline &&
                        (baseline == null || !baseline.WorkTypeOrders.ContainsKey(entry.Key)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The WorkGiver-order intent has no exact live baseline: " + entry.Key + ".");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.PresentationSettings))
            {
                for (int i = 0; i < state.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry =
                        state.PresentationSettingIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        Abort(WorkloadDiagnosticCode.InvalidState, "A workload presentation intent has no setting ID.");
                    }

                    if (entry.Intent.HasValue &&
                        (entry.Intent.Value.Ownership != WorkloadSettingOwnership.WorkloadOwned ||
                         !entry.Intent.Value.IsValid))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidOwnership,
                            "The workload presentation setting '" + entry.Key +
                            "' is not explicitly workload-owned.");
                    }

                    if (!BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                            entry.Key,
                            out _))
                    {
                        Abort(
                            WorkloadDiagnosticCode.UnsupportedOperation,
                            "The workload presentation setting '" + entry.Key +
                            "' is outside the BWT workload-owned allowlist.");
                    }

                    if (requireLiveBaseline &&
                        (baseline?.SettingsSnapshot == null ||
                         !baseline.PresentationSettingIds.Contains(entry.Key)))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The workload presentation setting has no exact global baseline: " + entry.Key + ".");
                    }
                }
            }
        }

        private static void ValidateTypedRuntimeEntries(
            WorkloadSession session,
            WorkloadTemplate targetTemplate,
            WorkloadBackendDimensionBaseline baseline,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report,
            bool requireLiveBaseline = true,
            bool useTargetTemplateState = false,
            string operationName = "apply")
        {
            if (targetTemplate?.Definition == null) return;
            WorkloadProjectedState state = useTargetTemplateState
                ? targetTemplate.ProjectedState ?? WorkloadProjectedState.Empty
                : session?.ProjectedState ?? targetTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            WorkloadOwnershipDimensions ownership = targetTemplate.Definition.OwnershipDimensions;
            if (ownership.Owns(WorkloadStateDimension.Schedules))
            {
                if (requireLiveBaseline && baseline != null && baseline.Schedules.Count > 0)
                {
                    EnsureTypedBaselineCurrent(baseline, runtime, report, "schedule");
                }

                for (int i = 0; i < state.ScheduleIntents.Count; i++)
                {
                    WorkloadScheduleIntentEntry entry = state.ScheduleIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (!entry.Key.IsGlobal && !useTargetTemplateState && session.IsExcludedForApply(entry.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            entry.Key.ToString(),
                            "The staged schedule belongs to a pawn excluded for this application.");
                        continue;
                    }

                    if (!TryValidateScheduleTarget(entry.Key, runtime, scope, report))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 " + operationName + " was blocked because schedule target " + entry.Key + " is stale or outside scope.");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
            {
                for (int i = 0; i < state.SpecificPriorityIntents.Count; i++)
                {
                    WorkloadSpecificPriorityIntentEntry entry = state.SpecificPriorityIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (!entry.Key.IsGlobal && !useTargetTemplateState && session.IsExcludedForApply(entry.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            entry.Key.ToString(),
                            "The staged specific-job priority belongs to a pawn excluded for this application.");
                        continue;
                    }

                    if (!entry.Key.IsGlobal &&
                        !TryResolveSpecificWritableEntry(
                            entry.Key.ToLegacyKey(),
                            scope,
                            runtime,
                            report,
                            entry.Key.ToString(),
                            out _,
                            out _,
                            out _,
                            out bool stale))
                    {
                        if (stale)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The V2 " + operationName + " was blocked because specific-job target " + entry.Key + " is stale.");
                        }
                    }
                    else if (entry.Key.IsGlobal &&
                             !TryResolveGlobalWorkGiver(
                                 entry.Key.WorkType,
                                 entry.Key.WorkGiver,
                                 runtime,
                                 out _,
                                 out _,
                                 out string reason))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 " + operationName + " was blocked because global specific-job target " + entry.Key +
                            " is stale: " + reason);
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                for (int i = 0; i < state.WorkTypeOrderIntents.Count; i++)
                {
                    WorkloadWorkTypeOrderIntentEntry entry = state.WorkTypeOrderIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (!entry.Key.IsGlobal && !useTargetTemplateState && session.IsExcludedForApply(entry.Key.Pawn))
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Excluded,
                            "apply.session-excluded",
                            entry.Key.ToString(),
                            "The staged WorkGiver order belongs to a pawn excluded for this application.");
                        continue;
                    }

                    if (!TryValidateOrderTarget(entry.Key, entry.Intent, scope, runtime, report))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The V2 " + operationName + " was blocked because WorkGiver order target " + entry.Key + " is stale or invalid.");
                    }
                }
            }

            if (requireLiveBaseline && ownership.Owns(WorkloadStateDimension.PresentationSettings) &&
                baseline?.SettingsSnapshot != null)
            {
                EnsureSettingsBaselineCurrent(baseline, report);
            }
        }

        private static void ValidatePersistenceRuntimeState(
            WorkloadSession session,
            WorkloadTemplate targetTemplate,
            WorkloadPreviewPlan plan,
            WorkloadScope scope,
            WorkloadBackendDimensionBaseline baseline,
            WorkloadV2CommitReport report)
        {
            if (baseline == null)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 persistence operation is missing its service-owned runtime baseline.");
            }

            RuntimeContext runtime = BuildRuntimeContext(targetTemplate, report);
            ValidateScopeEntries(scope, runtime, report);
            ValidateRuntimeEntries(
                session,
                targetTemplate,
                plan,
                scope,
                runtime,
                report,
                operationName: "persistence");
            ValidateTypedRuntimeEntries(
                session,
                targetTemplate,
                baseline,
                scope,
                runtime,
                report,
                requireLiveBaseline: false,
                useTargetTemplateState: true,
                operationName: "persistence");

            if (report.MissingOrStaleCount > 0)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 persistence operation was blocked because a workload-owned runtime identity is stale or missing.");
            }

            WorkloadProjectedState state = targetTemplate.ProjectedState ?? WorkloadProjectedState.Empty;
            WorkloadOwnershipDimensions ownership = targetTemplate.Definition.OwnershipDimensions;
            bool hasSchedules = HasPersistedDimension(state, WorkloadStateDimension.Schedules);
            bool hasSpecificJobs =
                HasPersistedDimension(state, WorkloadStateDimension.SpecificJobOverrides) ||
                HasPersistedDimension(state, WorkloadStateDimension.SpecificJobOrder);
            bool hasPriorityRuntime =
                hasSchedules ||
                hasSpecificJobs ||
                HasPersistedDimension(state, WorkloadStateDimension.ParentPriorities) ||
                HasPersistedDimension(state, WorkloadStateDimension.ManualModes);

            if (hasSchedules && TimePriorityService.CurrentVersion != baseline.ScheduleRevision)
            {
                AbortBaselineChanged(
                    report,
                    "schedule",
                    "The hourly schedule service revision changed while the workload preview was open.");
            }

            if (hasSpecificJobs)
            {
                if (WorkGiverReassignmentManager.CurrentSyncVersion != baseline.SpecificJobRevision)
                {
                    AbortBaselineChanged(
                        report,
                        "specific-job.revision",
                        "The WorkGiver reassignment revision changed while the workload preview was open.");
                }

                if (!StringComparer.Ordinal.Equals(
                        baseline.TaxonomyFingerprint,
                        ComputeTaxonomyFingerprint(runtime)))
                {
                    AbortBaselineChanged(
                        report,
                        "specific-job.taxonomy",
                        "The WorkGiver taxonomy changed while the workload preview was open.");
                }
            }

            if (hasPriorityRuntime &&
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(baseline.AuthorityRevision))
            {
                AbortBaselineChanged(
                    report,
                    "priority-authority",
                    "Priority mutation authority changed while the workload preview was open; the projected workload was not persisted.");
            }

            // Keep the ownership value referenced here so this persistence
            // boundary remains explicit when a dimension has no current entry.
            // ValidateTypedStateForCommit has already checked the live
            // stageable-settings registry before this runtime pass.
            if (ownership.Owns(WorkloadStateDimension.PresentationSettings))
            {
                ValidateTypedStateForCommit(targetTemplate, baseline, false, report);
            }
        }

        private static bool HasPersistedDimension(
            WorkloadProjectedState state,
            WorkloadStateDimension dimension)
        {
            if (state == null) return false;
            switch (dimension)
            {
                case WorkloadStateDimension.ParentPriorities:
                    return state.ParentPriorities.Count > 0 || state.ParentPriorityIntents.Count > 0;
                case WorkloadStateDimension.ManualModes:
                    return state.ManualModes.Count > 0 || state.ManualModeIntents.Count > 0;
                case WorkloadStateDimension.Schedules:
                    return state.Schedules.Count > 0 || state.ScheduleIntents.Count > 0;
                case WorkloadStateDimension.SpecificJobOverrides:
                    return state.SpecificJobOverrides.Count > 0 || state.SpecificPriorityIntents.Count > 0;
                case WorkloadStateDimension.SpecificJobOrder:
                    return state.SpecificJobOrder.Count > 0 || state.WorkTypeOrderIntents.Count > 0;
                case WorkloadStateDimension.PresentationSettings:
                    return state.PresentationSettings.Count > 0 || state.PresentationSettingIntents.Count > 0;
                default:
                    return false;
            }
        }

        private static bool TryValidateScheduleTarget(
            WorkloadScheduleTargetKey key,
            RuntimeContext runtime,
            WorkloadScope scope,
            WorkloadV2CommitReport report)
        {
            if (key == null || !key.IsValid) return false;
            if (key.IsGlobal)
            {
                return TryResolveGlobalWorkGiver(
                    key.WorkType,
                    key.WorkGiver,
                    runtime,
                    out _,
                    out _,
                    out _);
            }

            if (scope.IsExplicitlyExcluded(key.Pawn)) return true;
            if (key.TargetKind == WorkloadScheduleTargetKind.ParentWorkType)
            {
                return TryResolveWritableEntry(
                    key.Pawn,
                    key.WorkType,
                    scope,
                    runtime,
                    report,
                    key.ToString(),
                    out _,
                    out _,
                    out _);
            }

            return TryResolveSpecificWritableEntry(
                new WorkloadSpecificJobKey(key.Pawn, key.WorkType, key.WorkGiver),
                scope,
                runtime,
                report,
                key.ToString(),
                out _,
                out _,
                out _,
                out _);
        }

        private static bool TryValidateOrderTarget(
            WorkloadWorkTypeOrderKey key,
            WorkloadIntent<WorkloadWorkTypeOrderPayload> intent,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            if (key == null || !key.IsValid) return false;
            if (intent.HasValue &&
                !IsCompleteOrderPayload(key, intent.Value, runtime))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Unsupported,
                    "specific-job-order.permutation",
                    key.ToString(),
                    "The workload order is not a complete permutation of the current WorkGiver taxonomy.");
                return false;
            }

            if (key.IsGlobal)
            {
                return runtime.WorkTypes.ContainsKey(key.WorkType.Value);
            }

            if (scope.IsExplicitlyExcluded(key.Pawn)) return true;
            return TryResolveWritableEntry(
                key.Pawn,
                key.WorkType,
                scope,
                runtime,
                report,
                key.ToString(),
                out _,
                out _,
                out _);
        }

        private static bool IsCompleteOrderPayload(
            WorkloadWorkTypeOrderKey key,
            WorkloadWorkTypeOrderPayload payload,
            RuntimeContext runtime)
        {
            if (payload == null || !payload.IsValid || runtime == null ||
                !runtime.WorkTypes.TryGetValue(key.WorkType.Value, out WorkTypeDef workType))
            {
                return false;
            }

            if (key.IsGlobal)
            {
                var orderedNames = new List<string>();
                for (int i = 0; i < payload.OrderedWorkGivers.Count; i++)
                {
                    orderedNames.Add(payload.OrderedWorkGivers[i].Value);
                }

                return WorkGiverReassignmentManager.IsCompleteGlobalWorkTypeOrder(
                    workType.defName,
                    orderedNames);
            }

            if (!runtime.Pawns.TryGetValue(key.Pawn.Value, out Pawn pawn) || pawn == null)
            {
                return false;
            }

            IReadOnlyList<WorkGiver> expected =
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType, pawn);
            if (expected.Count != payload.OrderedWorkGivers.Count) return false;
            for (int i = 0; i < expected.Count; i++)
            {
                WorkGiverDef workGiver = expected[i]?.def;
                if (workGiver == null ||
                    !StringComparer.Ordinal.Equals(
                        workGiver.defName,
                        payload.OrderedWorkGivers[i].Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureTypedBaselineCurrent(
            WorkloadBackendDimensionBaseline baseline,
            RuntimeContext runtime,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (baseline == null) return;
            if (baseline.Schedules.Count > 0 &&
                TimePriorityService.CurrentVersion != baseline.ScheduleRevision)
            {
                AbortBaselineChanged(
                    report,
                    subject,
                    "The hourly schedule service revision changed after preview capture.");
            }

            if ((baseline.SpecificPriorities.Count > 0 || baseline.WorkTypeOrders.Count > 0) &&
                WorkGiverReassignmentManager.CurrentSyncVersion != baseline.SpecificJobRevision)
            {
                AbortBaselineChanged(
                    report,
                    subject,
                    "The WorkGiver reassignment revision changed after preview capture.");
            }

            if ((baseline.SpecificPriorities.Count > 0 || baseline.WorkTypeOrders.Count > 0) &&
                !StringComparer.Ordinal.Equals(
                    baseline.TaxonomyFingerprint,
                    ComputeTaxonomyFingerprint(runtime)))
            {
                AbortBaselineChanged(
                    report,
                    subject,
                    "The WorkGiver taxonomy changed after preview capture.");
            }

            if ((baseline.Schedules.Count > 0 || baseline.SpecificPriorities.Count > 0 ||
                 baseline.WorkTypeOrders.Count > 0) &&
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(baseline.AuthorityRevision))
            {
                AbortBaselineChanged(
                    report,
                    subject,
                    "Priority mutation authority changed after preview capture.");
            }
        }

        private static void EnsureSettingsBaselineCurrent(
            WorkloadBackendDimensionBaseline baseline,
            WorkloadV2CommitReport report)
        {
            if (baseline?.SettingsWriter == null || baseline.SettingsSnapshot == null) return;
            if (!baseline.SettingsWriter.TryCapture(
                    baseline.PresentationSettingIds,
                    out WorkloadPresentationSettingsSnapshot current,
                    out string reason))
            {
                AbortBaselineChanged(report, "presentation", reason);
            }

            foreach (KeyValuePair<string, WorkloadScalarValue> value in baseline.SettingsSnapshot.Values)
            {
                if (!current.Values.TryGetValue(value.Key, out WorkloadScalarValue observed) ||
                    !observed.Equals(value.Value))
                {
                    AbortBaselineChanged(
                        report,
                        value.Key,
                        "The global Better Work Tab setting changed after preview capture.");
                }
            }
        }

        private static void ValidateRuntimeEntries(
            WorkloadSession session,
            WorkloadTemplate targetTemplate,
            WorkloadPreviewPlan plan,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report,
            string operationName = "apply")
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
                             "The V2 " + operationName + " was blocked during preflight because " +
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
                             "The V2 " + operationName + " was blocked during preflight because " +
                            difference.Key + " is stale or missing.");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) &&
                state.SpecificPriorityIntents.Count == 0)
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
                             "The V2 " + operationName + " was blocked during preflight because " +
                            difference.Key + " is stale or missing.");
                    }
                }
            }

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder) &&
                state.WorkTypeOrderIntents.Count == 0)
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
                             "The V2 " + operationName + " was blocked during preflight because " +
                            difference.Key + " is stale or missing.");
                    }
                }
            }
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
            WorkloadBackendDimensionBaseline backendBaseline,
            WorkloadV2CommitReport report,
            bool multiplayerAuthorized = false)
        {
            var result = new RuntimeCommitPlan
            {
                BackendBaseline = backendBaseline,
                MultiplayerAuthorized = multiplayerAuthorized
            };
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

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) &&
                after.SpecificPriorityIntents.Count == 0)
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

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder) &&
                after.WorkTypeOrderIntents.Count == 0)
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

            PrepareTypedRuntimeChanges(
                session,
                after,
                scope,
                runtime,
                backendBaseline,
                result,
                report);

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

            if (result.HasLiveMutations && MultiplayerBridge.Active && !multiplayerAuthorized)
            {
                const string message =
                    "V2 Apply live mutations require immediate authoritative acknowledgement and are unavailable in the active multiplayer route. Save and Fork remain persistence-only operations.";
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

        private static void PrepareTypedRuntimeChanges(
            WorkloadSession session,
            WorkloadProjectedState after,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadBackendDimensionBaseline baseline,
            RuntimeCommitPlan result,
            WorkloadV2CommitReport report)
        {
            if (after == null || baseline == null || result == null) return;

            for (int i = 0; i < after.ScheduleIntents.Count; i++)
            {
                WorkloadScheduleIntentEntry entry = after.ScheduleIntents[i];
                if (entry == null || entry.Intent.IsNoOpinion) continue;
                if (!entry.Key.IsGlobal && session.IsExcludedForApply(entry.Key.Pawn)) continue;
                if (!baseline.Schedules.TryGetValue(
                        entry.Key,
                        out TimePriorityLiveScheduleSnapshot previous))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The typed schedule plan has no captured baseline: " + entry.Key + ".");
                }

                bool desiredHasSchedule = entry.Intent.HasValue &&
                    entry.Intent.Value != null &&
                    entry.Intent.Value.PinnedHourMask != 0;
                bool unchanged = entry.Intent.IsClear
                    ? !previous.HadSchedule
                    : desiredHasSchedule
                        ? previous.HadSchedule &&
                          WorkloadTimePriorityAdapter.Matches(previous, entry.Intent.Value)
                        : !previous.HadSchedule;
                if (unchanged)
                {
                    report.Add(
                        entry.Intent.IsClear
                            ? WorkloadV2CommitMessageKind.Unchanged
                            : WorkloadV2CommitMessageKind.Unchanged,
                        "schedule.unchanged",
                        entry.Key.ToString(),
                        "The live 24-hour linked/pinned schedule already matches the preview.");
                    continue;
                }

                result.Schedules.Add(
                    new ScheduleMutation(
                        entry.Key,
                        previous,
                        entry.Intent));
            }

            for (int i = 0; i < after.SpecificPriorityIntents.Count; i++)
            {
                WorkloadSpecificPriorityIntentEntry entry = after.SpecificPriorityIntents[i];
                if (entry == null || entry.Intent.IsNoOpinion) continue;
                if (!entry.Key.IsGlobal && session.IsExcludedForApply(entry.Key.Pawn)) continue;
                if (!baseline.SpecificPriorities.TryGetValue(
                        entry.Key,
                        out WorkloadSpecificPriorityBaseline previous))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The typed specific-job priority plan has no captured baseline: " + entry.Key + ".");
                }

                WorkGiverReassignmentManager.ExactGlobalStateKind desiredGlobalState =
                    entry.Intent.IsClear
                        ? WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                        : WorkGiverReassignmentManager.ExactGlobalStateKind.Set;
                int desiredPriority = entry.Intent.HasValue
                    ? entry.Intent.Value.Priority
                    : WorkPrioritySystem.DisabledPriority;
                bool unchanged;
                if (previous.IsGlobal)
                {
                    WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot current =
                        WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(
                            previous.WorkGiver.defName);
                    unchanged = current.State == desiredGlobalState &&
                        (desiredGlobalState != WorkGiverReassignmentManager.ExactGlobalStateKind.Set ||
                         current.Priority == desiredPriority);
                }
                else
                {
                    bool hasCurrent = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                        previous.Pawn,
                        previous.WorkGiver,
                        out int currentPriority);
                    unchanged = entry.Intent.IsClear
                        ? !hasCurrent
                        : hasCurrent && currentPriority == desiredPriority;
                }

                if (unchanged)
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Unchanged,
                        "specific-job-priority.unchanged",
                        entry.Key.ToString(),
                        "The exact local/global specific-job priority already matches the preview.");
                    continue;
                }

                result.TypedSpecificPriorities.Add(
                    new TypedSpecificPriorityMutation(
                        entry.Key,
                        previous,
                        desiredGlobalState,
                        desiredPriority));
            }

            for (int i = 0; i < after.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadWorkTypeOrderIntentEntry entry = after.WorkTypeOrderIntents[i];
                if (entry == null || entry.Intent.IsNoOpinion) continue;
                if (!entry.Key.IsGlobal && session.IsExcludedForApply(entry.Key.Pawn)) continue;
                if (!baseline.WorkTypeOrders.TryGetValue(
                        entry.Key,
                        out WorkloadWorkTypeOrderBaseline previous))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The typed WorkGiver-order plan has no captured baseline: " + entry.Key + ".");
                }

                IReadOnlyList<string> desiredOrder = null;
                if (entry.Intent.HasValue)
                {
                    var names = new List<string>();
                    for (int orderIndex = 0;
                         orderIndex < entry.Intent.Value.OrderedWorkGivers.Count;
                         orderIndex++)
                    {
                        names.Add(entry.Intent.Value.OrderedWorkGivers[orderIndex].Value);
                    }

                    desiredOrder = names;
                }

                bool unchanged;
                if (previous.IsGlobal)
                {
                    WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot current =
                        WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(
                            previous.WorkType.defName);
                    WorkGiverReassignmentManager.ExactGlobalStateKind desiredState =
                        entry.Intent.IsClear
                            ? WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                            : WorkGiverReassignmentManager.ExactGlobalStateKind.Set;
                    unchanged = current.State == desiredState &&
                        (desiredState != WorkGiverReassignmentManager.ExactGlobalStateKind.Set ||
                         SequenceEqual(current.OrderedWorkGiverNames, desiredOrder));
                }
                else
                {
                    WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot current =
                        WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                            previous.Pawn,
                            previous.WorkType);
                    unchanged = entry.Intent.IsClear
                        ? !current.HasStoredOrder
                        : current.HasStoredOrder &&
                          SequenceEqual(current.OrderedWorkGiverNames, desiredOrder);
                }

                if (unchanged)
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Unchanged,
                        "specific-job-order.unchanged",
                        entry.Key.ToString(),
                        "The exact local/global WorkGiver order already matches the preview.");
                    continue;
                }

                result.TypedWorkTypeOrders.Add(
                    new TypedWorkTypeOrderMutation(
                        entry.Key,
                        previous,
                        desiredOrder,
                        entry.Intent.IsClear));
            }

            if (baseline.SettingsSnapshot != null)
            {
                var settingValues = new Dictionary<string, WorkloadScalarValue>(
                    StringComparer.Ordinal);
                for (int i = 0; i < after.PresentationSettingIntents.Count; i++)
                {
                    WorkloadPresentationSettingIntentEntry entry =
                        after.PresentationSettingIntents[i];
                    if (entry == null || entry.Intent.IsNoOpinion) continue;
                    if (!TryAddProjectedSettingValue(
                            entry.Key,
                            entry.Intent,
                            baseline,
                            settingValues,
                            report))
                    {
                        Abort(
                            WorkloadDiagnosticCode.InvalidState,
                            "The projected presentation setting could not be planned: " + entry.Key + ".");
                    }
                }

                if (after.PresentationSettingIntents.Count == 0)
                {
                    for (int i = 0; i < after.PresentationSettings.Count; i++)
                    {
                        WorkloadPresentationSettingEntry entry = after.PresentationSettings[i];
                        if (entry == null) continue;
                        TryAddProjectedSettingValue(
                            entry.Key,
                            WorkloadIntent<WorkloadSettingValue>.CreateSet(
                                WorkloadSettingValue.WorkloadOwned(entry.Value)),
                            baseline,
                            settingValues,
                            report);
                    }
                }

                foreach (KeyValuePair<string, WorkloadScalarValue> value in settingValues)
                {
                    if (!baseline.SettingsSnapshot.Values.TryGetValue(
                            value.Key,
                            out WorkloadScalarValue previous) ||
                        !previous.Equals(value.Value))
                    {
                        result.PresentationValues[value.Key] = value.Value;
                    }
                }
            }

            result.RequiresPriorityAuthority |= result.Schedules.Count > 0 ||
                result.TypedSpecificPriorities.Count > 0 ||
                result.TypedWorkTypeOrders.Count > 0;
            if (result.TypedSpecificPriorities.Count > 0 ||
                result.TypedWorkTypeOrders.Count > 0)
            {
                result.RequiresSpecificJobRevision = true;
                result.SpecificJobRevision = baseline.SpecificJobRevision;
                result.HasSpecificJobRevision = true;
            }
        }

        private static bool TryAddProjectedSettingValue(
            string settingId,
            WorkloadIntent<WorkloadSettingValue> intent,
            WorkloadBackendDimensionBaseline baseline,
            IDictionary<string, WorkloadScalarValue> values,
            WorkloadV2CommitReport report)
        {
            if (string.IsNullOrWhiteSpace(settingId) ||
                !baseline.PresentationSettingIds.Contains(settingId))
            {
                return false;
            }

            if (intent.IsClear)
            {
                if (!baseline.SettingsSnapshot.Values.TryGetValue(
                        settingId,
                        out WorkloadScalarValue fallback))
                {
                    return false;
                }

                values[settingId] = fallback;
                report.Add(
                    WorkloadV2CommitMessageKind.Cleared,
                    "presentation.clear",
                    settingId,
                    "The workload-owned presentation override will restore the captured global value.");
                return true;
            }

            BWTWorkloadSettingDefinitionState definition = null;
            if (!intent.HasValue ||
                intent.Value.Ownership != WorkloadSettingOwnership.WorkloadOwned ||
                !BWTWorkloadSettingsOwnershipPolicy.TryGetStageableDefinition(
                    settingId,
                    out definition) ||
                intent.Value.Kind != definition.ScalarKind)
            {
                return false;
            }

            values[settingId] = intent.Value.Scalar;
            return true;
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
            IDisposable scheduleBatch = null;
            try
            {
                if (runtimePlan.SpecificJobOverrides.Count > 0 ||
                    runtimePlan.SpecificJobOrder.Count > 0 ||
                    runtimePlan.TypedSpecificPriorities.Count > 0 ||
                    runtimePlan.TypedWorkTypeOrders.Count > 0)
                {
                    specificBatch = WorkGiverReassignmentManager.BeginMutationBatch();
                }

                if (runtimePlan.Schedules.Count > 0)
                {
                    scheduleBatch = TimePriorityService.BeginMutationBatch();
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
                ValidateTypedMutationsPrewrite(
                    runtimePlan,
                    scope,
                    runtime,
                    report);

                if (!TryApplySpecificJobBatch(
                        transaction,
                        runtimePlan,
                        session,
                        report))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The canonical specific-job batch rejected the complete workload payload.");
                }

                if (runtimePlan.Schedules.Count > 0 && MultiplayerBridge.Active)
                {
                    if (!runtimePlan.MultiplayerAuthorized ||
                        runtimePlan.MutationAuthorization == null ||
                        !runtimePlan.MutationAuthorization.IsUsable)
                    {
                        Abort(
                            WorkloadDiagnosticCode.MutationCapabilityRejected,
                            "The synchronized workload schedule transaction was not authorized by its opaque capability.");
                    }
                }

                transaction.WorkloadAuthorization = runtimePlan.MutationAuthorization;

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
                            // Register the reversible mutation before the
                            // canonical writer.  The writer may change live
                            // state and still return a late authority error.
                            transaction.ManualWasChanged = true;
                            transaction.PreviousManualMode = previousManualMode;
                            bool writeAccepted =
                                WorkPrioritySystem.SetManualPriorities(runtimePlan.ManualTarget);
                            bool observedManualMode = Verse.Find.PlaySettings.useWorkPriorities;

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

                    AppliedPriorityMutation appliedPriority = new AppliedPriorityMutation(
                        resolvedMutation,
                        previous);
                    transaction.Priorities.Add(appliedPriority);
                    bool writeAccepted = WorkPrioritySystem.SetPriority(
                            pawn.workSettings,
                            workType,
                            mutation.DesiredPriority);
                    int observed = PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
                    if (observed == previous)
                    {
                        transaction.Priorities.Remove(appliedPriority);
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

                ApplyTypedLive(
                    transaction,
                    runtimePlan,
                    session,
                    runtime,
                    report,
                    runtimePlan.MutationAuthorization);

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
                if (scheduleBatch != null)
                {
                    scheduleBatch.Dispose();
                    transaction.SchedulePublished = CommitAndPublishScheduleMutation(
                        transaction,
                        out bool revisionOwned);
                    if (!revisionOwned)
                    {
                        throw new InvalidOperationException(
                            "The schedule transaction lost its revision while publishing its live batch.");
                    }
                }

                if (specificBatch != null)
                {
                    specificBatch.Dispose();
                    WorkGiverReassignmentManager.CommitMutationBatch(
                        notifyDependents: true);
                }
            }
        }

        private static bool IsExecutionCapabilityBound(
            WorkloadCommitExecutionContext executionContext,
            WorkloadSession session,
            object requestIdentity,
            WorkloadTemplate targetTemplate,
            WorkloadDecisionKind decisionKind,
            string targetStableId,
            WorkloadV2CommitReport report)
        {
            WorkloadMutationAuthorization authorization =
                executionContext?.MutationAuthorization;
            if (authorization == null || !authorization.IsUsable ||
                requestIdentity == null || session == null || targetTemplate == null)
            {
                report?.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "transaction-capability.missing",
                    targetStableId,
                    "The synchronized workload execute did not supply its opaque mutation capability.");
                return false;
            }

            if (!StringComparer.Ordinal.Equals(
                    authorization.ProjectedTemplateFingerprint,
                    targetTemplate.SemanticFingerprint) ||
                !StringComparer.Ordinal.Equals(
                    authorization.SourceTemplateFingerprint,
                    WorkloadSession.GetSourceIdentity(session.SourceTemplate)))
            {
                report?.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "transaction-capability.fingerprint",
                    targetStableId,
                    "The synchronized workload capability is bound to a different template fingerprint.");
                return false;
            }

            return authorization.IsBoundToTransaction(
                requestIdentity,
                executionContext.SessionIdentity,
                authorization.TransactionId,
                authorization.RequestFingerprint,
                authorization.SourceTemplateFingerprint,
                authorization.ProjectedTemplateFingerprint,
                authorization.SessionRevision,
                authorization.AuthorityRevision,
                authorization.AuthorityOwner,
                authorization.SpecificJobRevision,
                authorization.ScheduleRevision,
                authorization.SettingsRevision,
                authorization.HostSessionEpoch,
                authorization.RosterFingerprint);
        }

        private static bool TryApplySpecificJobBatch(
            LiveMutationTransaction transaction,
            RuntimeCommitPlan plan,
            WorkloadSession session,
            WorkloadV2CommitReport report)
        {
            if (plan == null ||
                (plan.SpecificJobOverrides.Count == 0 &&
                 plan.SpecificJobOrder.Count == 0 &&
                 plan.TypedSpecificPriorities.Count == 0 &&
                 plan.TypedWorkTypeOrders.Count == 0))
            {
                return true;
            }

            var priorities = new List<WorkGiverReassignmentManager.WorkloadSpecificPriorityBatchEntry>();
            var orders = new List<WorkGiverReassignmentManager.WorkloadSpecificOrderBatchEntry>();

            for (int i = 0; i < plan.SpecificJobOverrides.Count; i++)
            {
                SpecificJobOverrideMutation mutation = plan.SpecificJobOverrides[i];
                RevalidateSpecificOverrideBaseline(
                    session,
                    plan,
                    mutation,
                    report,
                    mutation.Key.ToString());
                bool hasPrevious = WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                    mutation.Pawn,
                    mutation.WorkGiver,
                    out int previousPriority);
                priorities.Add(
                    new WorkGiverReassignmentManager.WorkloadSpecificPriorityBatchEntry(
                        false,
                        mutation.Pawn.thingIDNumber,
                        mutation.WorkGiver.defName,
                        mutation.HasAfter
                            ? WorkGiverReassignmentManager.ExactGlobalStateKind.Set
                            : WorkGiverReassignmentManager.ExactGlobalStateKind.Clear,
                        mutation.DesiredPriority,
                        null,
                        hasPrevious,
                        previousPriority));
            }

            for (int i = 0; i < plan.SpecificJobOrder.Count; i++)
            {
                SpecificJobOrderMutation mutation = plan.SpecificJobOrder[i];
                RevalidateSpecificOrderBaseline(
                    session,
                    plan,
                    mutation,
                    report,
                    mutation.Parent.ToString());
                orders.Add(
                    new WorkGiverReassignmentManager.WorkloadSpecificOrderBatchEntry(
                        false,
                        mutation.Pawn.thingIDNumber,
                        mutation.WorkType.defName,
                        mutation.DesiredOrder == null
                            ? WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                            : WorkGiverReassignmentManager.ExactGlobalStateKind.Set,
                        mutation.DesiredOrder,
                        null,
                        mutation.PreviousSnapshot));
            }

            for (int i = 0;
                 i < plan.TypedSpecificPriorities.Count &&
                 !plan.SpecificJobBatchApplied;
                 i++)
            {
                TypedSpecificPriorityMutation mutation = plan.TypedSpecificPriorities[i];
                WorkloadSpecificPriorityBaseline previous = mutation.Previous;
                if (previous == null || previous.WorkGiver == null)
                {
                    return false;
                }

                priorities.Add(
                    new WorkGiverReassignmentManager.WorkloadSpecificPriorityBatchEntry(
                        previous.IsGlobal,
                        previous.IsGlobal ? -1 : previous.Pawn.thingIDNumber,
                        previous.WorkGiver.defName,
                        mutation.DesiredGlobalState,
                        mutation.DesiredPriority,
                        previous.GlobalSnapshot,
                        previous.HasLocalOverride,
                        previous.LocalPriority));
            }

            for (int i = 0;
                 i < plan.TypedWorkTypeOrders.Count &&
                 !plan.SpecificJobBatchApplied;
                 i++)
            {
                TypedWorkTypeOrderMutation mutation = plan.TypedWorkTypeOrders[i];
                WorkloadWorkTypeOrderBaseline previous = mutation.Previous;
                if (previous == null || previous.WorkType == null)
                {
                    return false;
                }

                orders.Add(
                    new WorkGiverReassignmentManager.WorkloadSpecificOrderBatchEntry(
                        previous.IsGlobal,
                        previous.IsGlobal ? -1 : previous.Pawn.thingIDNumber,
                        previous.WorkType.defName,
                        mutation.IsClear
                            ? WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                            : WorkGiverReassignmentManager.ExactGlobalStateKind.Set,
                        mutation.DesiredOrder,
                        previous.GlobalSnapshot,
                        previous.LocalSnapshot));
            }

            if (!WorkGiverReassignmentManager.TryApplyWorkloadSpecificJobBatch(
                    priorities,
                    orders,
                    plan.SpecificJobRevision,
                    plan.AuthorityRevision,
                    plan.MutationAuthorization,
                    out WorkGiverReassignmentManager.WorkloadSpecificJobBatchRollback rollback,
                    out string reason))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "specific-job.batch",
                    "specific-job",
                    reason);
                return false;
            }

            plan.SpecificJobBatchApplied = true;
            plan.SpecificJobRevision = WorkGiverReassignmentManager.CurrentSyncVersion;
            plan.HasSpecificJobRevision = true;
            if (rollback != null)
            {
                transaction.SpecificJobBatchRollback = rollback;
                transaction.SpecificJobRevision = plan.SpecificJobRevision;
            }

            for (int i = 0; i < priorities.Count; i++)
            {
                WorkGiverReassignmentManager.WorkloadSpecificPriorityBatchEntry entry =
                    priorities[i];
                report.Add(
                    entry.DesiredState == WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                        ? WorkloadV2CommitMessageKind.Cleared
                        : WorkloadV2CommitMessageKind.Changed,
                    entry.DesiredState == WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                        ? "specific-job-priority.cleared"
                        : "specific-job-priority.changed",
                    entry.CanonicalKey,
                    "The specific-job priority was committed through the canonical atomic batch seam.");
            }

            for (int i = 0; i < orders.Count; i++)
            {
                WorkGiverReassignmentManager.WorkloadSpecificOrderBatchEntry entry =
                    orders[i];
                report.Add(
                    entry.DesiredState == WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                        ? WorkloadV2CommitMessageKind.Cleared
                        : WorkloadV2CommitMessageKind.Changed,
                    entry.DesiredState == WorkGiverReassignmentManager.ExactGlobalStateKind.Clear
                        ? "specific-job-order.cleared"
                        : "specific-job-order.changed",
                    entry.CanonicalKey,
                    "The WorkGiver order was committed through the canonical atomic batch seam.");
            }

            return true;
        }

        private static void ApplyTypedLive(
            LiveMutationTransaction transaction,
            RuntimeCommitPlan plan,
            WorkloadSession session,
            RuntimeContext runtime,
            WorkloadV2CommitReport report,
            WorkloadMutationAuthorization scheduleAuthorization)
        {
            if (plan == null || plan.BackendBaseline == null) return;
            WorkloadBackendDimensionBaseline baseline = plan.BackendBaseline;

            for (int i = 0; i < plan.Schedules.Count; i++)
            {
                ScheduleMutation mutation = plan.Schedules[i];
                EnsureRuntimeMutationAuthority(plan, report, mutation.Key.ToString() + ".authority");
                var appliedMutation = new AppliedScheduleMutation(
                    mutation,
                    null,
                    scheduleAuthorization);
                // Register the reversible entry before the writer or any
                // post-write verification.  A writer is allowed to mutate
                // first and report a failed authority/revision check after;
                // the transaction must still retain enough state to restore
                // that partial write.
                transaction.Schedules.Add(appliedMutation);
                bool applied;
                string reason;
                if (mutation.Intent.IsClear)
                {
                    applied = WorkloadTimePriorityAdapter.TryClearLiveScheduleSnapshot(
                        mutation.PreviousSnapshot,
                        scheduleAuthorization,
                        out reason);
                }
                else
                {
                    applied = WorkloadTimePriorityAdapter.TryApplyLiveScheduleSnapshot(
                        mutation.PreviousSnapshot,
                        mutation.Intent.Value,
                        scheduleAuthorization,
                        out reason);
                }

                if (!applied)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The canonical hourly schedule writer rejected " +
                        mutation.Key + ": " + reason);
                }

                if (!WorkloadTimePriorityAdapter.TryCaptureLiveScheduleSnapshot(
                        mutation.Key,
                        mutation.PreviousSnapshot.FallbackPriority,
                        out TimePriorityLiveScheduleSnapshot observed,
                        out reason))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The canonical hourly schedule writer could not verify " +
                        mutation.Key + ": " + reason);
                }

                bool expectedHasSchedule = mutation.Intent.HasValue &&
                    mutation.Intent.Value != null &&
                    mutation.Intent.Value.PinnedHourMask != 0;
                if (expectedHasSchedule
                    ? !observed.HadSchedule ||
                      !WorkloadTimePriorityAdapter.Matches(observed, mutation.Intent.Value)
                    : observed.HadSchedule)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The canonical hourly schedule writer did not commit " +
                        mutation.Key + " exactly.");
                }

                appliedMutation.ObservedSnapshot = observed;
                if (observed.Matches(mutation.PreviousSnapshot))
                {
                    // The entry was conservatively registered before the
                    // write.  A verified no-op does not make the transaction
                    // dirty and should not force a rollback lease to write.
                    transaction.Schedules.Remove(appliedMutation);
                }
                report.Add(
                    mutation.Intent.IsClear || !expectedHasSchedule
                        ? WorkloadV2CommitMessageKind.Cleared
                        : WorkloadV2CommitMessageKind.Changed,
                    mutation.Intent.IsClear || !expectedHasSchedule
                        ? "schedule.cleared"
                        : "schedule.changed",
                    mutation.Key.ToString(),
                    mutation.Intent.IsClear || !expectedHasSchedule
                        ? "The exact 24-hour schedule was cleared through TimePriorityService."
                        : "The exact 24-hour linked/pinned schedule was committed through TimePriorityService.");
            }

            if (plan.PresentationValues.Count > 0)
            {
                if (MultiplayerBridge.Active &&
                    (plan.MutationAuthorization == null ||
                     !plan.MutationAuthorization.IsAcceptedForSettings(
                         synchronizedExecution: true,
                         plan.AuthorityRevision,
                         plan.MutationAuthorization.SettingsRevision)))
                {
                    Abort(
                        WorkloadDiagnosticCode.MutationCapabilityRejected,
                        "The workload-owned presentation write requires the synchronized transaction capability.");
                }

                // Register the exact settings snapshot before invoking the
                // writer.  A writer may persist part of a batch and then
                // report a late failure; rollback must still own the complete
                // pre-write snapshot in that case.
                transaction.PresentationWasChanged = true;
                transaction.PresentationSnapshot = baseline.SettingsSnapshot;
                transaction.PresentationWriter = baseline.SettingsWriter;
                WorkloadPresentationSettingsMutationReceipt presentationReceipt =
                    baseline.SettingsWriter == null || baseline.SettingsSnapshot == null
                        ? null
                        : baseline.SettingsWriter.TryApply(
                            baseline.SettingsSnapshot,
                        plan.PresentationValues,
                            persist: true);
                if (presentationReceipt == null || !presentationReceipt.Succeeded)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The workload-owned presentation writer rejected the projected settings: " +
                        (presentationReceipt?.FailureReason ??
                         "The writer baseline is unavailable."));
                }

                transaction.PresentationWasChanged =
                    presentationReceipt.ChangedSettingIds.Count > 0;
                foreach (string settingId in presentationReceipt.ChangedSettingIds)
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Changed,
                        "presentation.changed",
                        settingId,
                        "The workload-owned presentation setting was committed through the BWT settings writer.");
                }
            }
        }

        private static void ValidateTypedMutationsPrewrite(
            RuntimeCommitPlan plan,
            WorkloadScope scope,
            RuntimeContext runtime,
            WorkloadV2CommitReport report)
        {
            if (plan == null || plan.BackendBaseline == null) return;
            EnsureTypedBaselineCurrent(
                plan.BackendBaseline,
                runtime,
                report,
                "live-apply");
            if (plan.PresentationValues.Count > 0)
            {
                EnsureSettingsBaselineCurrent(plan.BackendBaseline, report);
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

        private static void EnsurePersistenceBaseline(
            WorkloadV2PersistenceEnvelope store,
            WorkloadBackendDimensionBaseline baseline,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (store == null || baseline == null || !baseline.HasPersistenceBaseline)
            {
                Abort(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    "The V2 persistence baseline is unavailable for " + subject + ".");
            }

            store.RefreshDiagnostics();
            string error = string.Empty;
            if (store.IsReadOnlyDiagnostic ||
                !store.TryValidateCompareAndSwap(
                    baseline.PersistenceRevision,
                    baseline.PersistenceFingerprint,
                    out error))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "persistence.cas",
                    subject,
                    string.IsNullOrEmpty(error)
                        ? "The V2 persistence compare-and-swap validation failed."
                        : error);
                Abort(
                    WorkloadDiagnosticCode.PersistenceConflict,
                    string.IsNullOrEmpty(error)
                        ? "The V2 persistence compare-and-swap validation failed."
                        : error);
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

            mutation.PreviousRevision = store.PersistenceRevision;
            mutation.PreviousFingerprint = store.ComputeContentFingerprint();
            mutation.PreviousCurrentWorkloadId = store.CurrentWorkloadId;
            if (!store.TryCommitRevision(
                    mutation.ExpectedRevision,
                    mutation.ExpectedFingerprint,
                    out string revisionError))
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(revisionError)
                        ? "The V2 persistence compare-and-swap failed."
                        : revisionError);
            }

            mutation.RevisionAdvanced = true;

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
                // Fork is one CAS-owned persistence mutation: the new record
                // becomes current together with the add. The target stable ID
                // is already part of the canonical MP request/fingerprint.
                store.CurrentWorkloadId = mutation.StableId;
                mutation.CurrentWorkloadIdChanged = true;
            }

            mutation.WasApplied = true;
            store.RefreshPersistenceMetadata(false);
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
                (live.SpecificJobOverrides.Count > 0 || live.SpecificJobOrder.Count > 0 ||
                 live.TypedSpecificPriorities.Count > 0 || live.TypedWorkTypeOrders.Count > 0);
            bool scheduleChanged = live?.SchedulePublished == true;
            bool presentationChanged = live != null && live.PresentationWasChanged;
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

            if (scheduleChanged)
            {
                flags |= WorkTabDirtyFlags.ScheduleHour |
                    WorkTabDirtyFlags.Priority |
                    WorkTabDirtyFlags.Presentation;
            }

            if (presentationChanged)
            {
                flags |= WorkTabDirtyFlags.Presentation |
                    WorkTabDirtyFlags.HeaderText |
                    WorkTabDirtyFlags.HeaderGeometry |
                    WorkTabDirtyFlags.RenderResources |
                    WorkTabDirtyFlags.SettingsThemeLanguageScale;
            }

            if (flags == 0 && !scheduleChanged)
            {
                // Preserve the existing persistence-only refresh contract.
                flags = WorkTabDirtyFlags.Priority | WorkTabDirtyFlags.Presentation;
            }

            if (flags != 0) WorkTabInvalidationHub.Invalidate(flags);
        }

        private static bool RollbackPersistence(
            WorkloadV2PersistenceEnvelope store,
            PersistenceMutation mutation,
            WorkloadV2CommitReport report)
        {
            if (mutation == null ||
                (!mutation.WasApplied && !mutation.RevisionAdvanced)) return true;
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
                string currentFingerprint = store.ComputeContentFingerprint();
                bool revisionStillOwned = mutation.RevisionAdvanced &&
                    mutation.PreviousRevision < int.MaxValue &&
                    store.PersistenceRevision == mutation.PreviousRevision + 1 &&
                    StringComparer.Ordinal.Equals(
                        store.PersistenceFingerprint ?? string.Empty,
                        currentFingerprint);
                if (!revisionStillOwned)
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Fatal,
                        "rollback.persistence.conflict",
                        mutation.StableId,
                        "The V2 persistence target changed during rollback; the previous record was not restored.");
                    return false;
                }

                if (mutation.WasApplied)
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
                    else
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Fatal,
                            "rollback.persistence.conflict",
                            mutation.StableId,
                            "The V2 persistence target changed during rollback; the previous record was not restored.");
                        return false;
                    }
                }

                if (mutation.CurrentWorkloadIdChanged)
                {
                    store.CurrentWorkloadId = mutation.PreviousCurrentWorkloadId ?? string.Empty;
                }

                store.PersistenceRevision = mutation.PreviousRevision;
                store.PersistenceFingerprint = mutation.PreviousFingerprint ?? string.Empty;
                store.RefreshDiagnostics();
                bool restored = store.PersistenceRevision == mutation.PreviousRevision &&
                    StringComparer.Ordinal.Equals(
                        store.PersistenceFingerprint ?? string.Empty,
                        mutation.PreviousFingerprint ?? string.Empty) &&
                    StringComparer.Ordinal.Equals(
                        store.ComputeContentFingerprint(),
                        mutation.PreviousFingerprint ?? string.Empty);
                if (!restored)
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Fatal,
                        "rollback.persistence.verification",
                        mutation.StableId,
                        "The V2 persistence rollback completed without restoring the exact prior revision and fingerprint.");
                }

                if (restored)
                {
                    // Make a later recovery attempt idempotent.  Without
                    // clearing these ownership flags, a persistence rollback
                    // that succeeded while another live dimension failed
                    // would be treated as a stale second write.
                    mutation.WasApplied = false;
                    mutation.CurrentWorkloadIdChanged = false;
                    mutation.RevisionAdvanced = false;
                }

                return restored;
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
            // A failed rollback remains retryable while the lease remains
            // recovery-required.  Only the exact no-net-change state can
            // short-circuit a later recovery attempt.
            if (transaction.RollbackAttempted && !HasLiveNetChanges(transaction)) return true;

            transaction.RollbackAttempted = true;
            bool restored = true;

            try
            {
                bool hasPriorityChanges = transaction.ManualWasChanged || transaction.Priorities.Count > 0;
                bool hasSpecificChanges =
                    (transaction.SpecificJobBatchRollback != null &&
                     !transaction.SpecificJobBatchRolledBack) ||
                    transaction.SpecificJobOverrides.Count > 0 ||
                    transaction.SpecificJobOrder.Count > 0 ||
                    transaction.TypedSpecificPriorities.Count > 0 ||
                    transaction.TypedWorkTypeOrders.Count > 0;
                bool hasScheduleChanges = transaction.Schedules.Count > 0;
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
                    if (transaction.SpecificJobBatchRollback == null)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Fatal,
                            "rollback.specific-job.batch",
                            "specific-job",
                            "The transaction did not retain the canonical specific-job batch rollback lease.");
                        restored = false;
                    }
                    else
                    {
                        if (!WorkGiverReassignmentManager.TryRestoreWorkloadSpecificJobBatch(
                                transaction.SpecificJobBatchRollback,
                                out string batchRollbackReason))
                        {
                            report.Add(
                                WorkloadV2CommitMessageKind.Fatal,
                                "rollback.specific-job.batch",
                                "specific-job",
                                batchRollbackReason);
                            restored = false;
                        }
                        else
                        {
                            transaction.SpecificJobRevision =
                                WorkGiverReassignmentManager.CurrentSyncVersion;
                            transaction.SpecificJobBatchRolledBack = true;
                        }
                    }
                }

                if (hasScheduleChanges && restored)
                {
                    restored = RestoreSchedules(transaction, report);
                }

                if (transaction.PresentationWasChanged && restored)
                {
                    bool settingsCapabilityValid = !MultiplayerBridge.Active ||
                        (transaction.WorkloadAuthorization != null &&
                         transaction.WorkloadAuthorization.IsAcceptedForSettings(
                             synchronizedExecution: true,
                             transaction.AuthorityRevision,
                             transaction.WorkloadAuthorization.SettingsRevision));
                    WorkloadPresentationSettingsMutationReceipt rollbackReceipt =
                        settingsCapabilityValid
                            ? transaction.PresentationWriter?.TryRollback(
                                transaction.PresentationSnapshot,
                                persist: true)
                            : null;
                    if (rollbackReceipt == null || !rollbackReceipt.Succeeded)
                    {
                        report.Add(
                            WorkloadV2CommitMessageKind.Fatal,
                            "rollback.presentation",
                            "presentation",
                            "The workload-owned presentation settings could not be restored: " +
                            (settingsCapabilityValid
                                ? rollbackReceipt?.FailureReason ??
                                  "The presentation rollback writer or baseline is unavailable."
                                : "The synchronized transaction capability is unavailable for presentation rollback."));
                        restored = false;
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

            return restored && !HasLiveNetChanges(transaction);
        }

        private static bool RestoreSchedules(
            LiveMutationTransaction transaction,
            WorkloadV2CommitReport report)
        {
            bool restored = true;
            IDisposable batch = TimePriorityService.BeginMutationBatch();
            try
            {
                for (int i = transaction.Schedules.Count - 1; i >= 0; i--)
                {
                    if (RestoreSchedule(
                            transaction,
                            transaction.Schedules[i],
                            report,
                            "rollback.schedule"))
                    {
                        continue;
                    }

                    restored = false;
                    break;
                }
            }
            finally
            {
                batch.Dispose();
                CommitAndPublishScheduleMutation(transaction, out bool revisionOwned);
                if (!revisionOwned)
                {
                    report.Add(
                        WorkloadV2CommitMessageKind.Fatal,
                        "rollback.schedule.revision",
                        "rollback.schedule",
                        "The schedule rollback lost its exact transaction-owned revision while publishing restored state.");
                    restored = false;
                }
            }

            return restored;
        }

        private static void PublishScheduleMutation(
            LiveMutationTransaction transaction,
            bool changed)
        {
            if (!changed || transaction == null)
            {
                return;
            }

            var targets = new List<TimePriorityTarget>(transaction.Schedules.Count);
            for (int i = 0; i < transaction.Schedules.Count; i++)
            {
                TimePriorityLiveScheduleSnapshot snapshot =
                    transaction.Schedules[i]?.Mutation?.PreviousSnapshot;
                if (snapshot != null)
                {
                    targets.Add(snapshot.Target);
                }
            }

            WorkTabApplication.PublishCompletedScheduleMutation(
                changed: true,
                broadScope: true,
                dimensions: WorkTabApplicationDimensions.Schedule,
                affectedTargets: targets);
        }

        private static bool CommitAndPublishScheduleMutation(
            LiveMutationTransaction transaction,
            out bool revisionOwned)
        {
            bool changed = TimePriorityService.CommitMutationBatch();
            revisionOwned = transaction?.ScheduleRevisionReceipt != null &&
                transaction.ScheduleRevisionReceipt.AcceptCommit(
                    changed,
                    TimePriorityService.CurrentVersion);
            PublishScheduleMutation(transaction, changed);
            return changed;
        }

        private static bool RestoreSchedule(
            LiveMutationTransaction transaction,
            AppliedScheduleMutation applied,
            WorkloadV2CommitReport report,
            string subject)
        {
            if (applied?.Mutation?.PreviousSnapshot == null)
            {
                return false;
            }

            if (!WorkloadTimePriorityAdapter.TryCaptureLiveScheduleSnapshot(
                    applied.Mutation.Key,
                    applied.Mutation.PreviousSnapshot.FallbackPriority,
                out TimePriorityLiveScheduleSnapshot current,
                out string reason) ||
                !WorkloadTimePriorityAdapter.TryRestoreLiveScheduleSnapshot(
                    applied.Mutation.PreviousSnapshot,
                    current,
                    applied.Authorization,
                    transaction.ScheduleRevisionReceipt.InitialRevision,
                    transaction.ScheduleRevisionReceipt.OwnedRevision,
                    out reason))
            {
                report.Add(
                    WorkloadV2CommitMessageKind.Fatal,
                    "rollback.schedule",
                    subject,
                    "The exact hourly schedule could not be restored: " + reason);
                return false;
            }

            return true;
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

                if (transaction.SpecificJobBatchRollback != null &&
                    !transaction.SpecificJobBatchRolledBack &&
                    !StringComparer.Ordinal.Equals(
                        WorkGiverReassignmentManager.CurrentStateFingerprint,
                        transaction.SpecificJobBatchRollback.AppliedFingerprint))
                {
                    return true;
                }

                for (int i = 0; i < transaction.Schedules.Count; i++)
                {
                    AppliedScheduleMutation applied = transaction.Schedules[i];
                    if (!WorkloadTimePriorityAdapter.TryCaptureLiveScheduleSnapshot(
                            applied.Mutation.Key,
                            applied.Mutation.PreviousSnapshot.FallbackPriority,
                            out TimePriorityLiveScheduleSnapshot current,
                            out _))
                    {
                        return true;
                    }

                    if (!current.Matches(applied.Mutation.PreviousSnapshot))
                    {
                        return true;
                    }
                }

                for (int i = 0; i < transaction.TypedSpecificPriorities.Count; i++)
                {
                    WorkloadSpecificPriorityBaseline previous =
                        transaction.TypedSpecificPriorities[i].Mutation.Previous;
                    if (previous.IsGlobal)
                    {
                        WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot current =
                            WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(
                                previous.WorkGiver.defName);
                        if (current.State != previous.GlobalSnapshot.State ||
                            (current.State ==
                             WorkGiverReassignmentManager.ExactGlobalStateKind.Set &&
                             current.Priority != previous.GlobalSnapshot.Priority))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        bool hasOverride =
                            WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                                previous.Pawn,
                                previous.WorkGiver,
                                out int priority);
                        if (hasOverride != previous.HasLocalOverride ||
                            (hasOverride && priority != previous.LocalPriority))
                        {
                            return true;
                        }
                    }
                }

                for (int i = 0; i < transaction.TypedWorkTypeOrders.Count; i++)
                {
                    WorkloadWorkTypeOrderBaseline previous =
                        transaction.TypedWorkTypeOrders[i].Mutation.Previous;
                    if (previous.IsGlobal)
                    {
                        WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot current =
                            WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(
                                previous.WorkType.defName);
                        if (current.State != previous.GlobalSnapshot.State ||
                            (current.State ==
                             WorkGiverReassignmentManager.ExactGlobalStateKind.Set &&
                             !SequenceEqual(
                                 current.OrderedWorkGiverNames,
                                 previous.GlobalSnapshot.OrderedWorkGiverNames)))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot current =
                            WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                                previous.Pawn,
                                previous.WorkType);
                        if (current.HasStoredOrder != previous.LocalSnapshot.HasStoredOrder ||
                            (current.HasStoredOrder &&
                             !SequenceEqual(
                                 current.OrderedWorkGiverNames,
                                 previous.LocalSnapshot.OrderedWorkGiverNames)))
                        {
                            return true;
                        }
                    }
                }

                if (transaction.PresentationWasChanged &&
                    transaction.PresentationWriter != null &&
                    transaction.PresentationSnapshot != null &&
                    (!transaction.PresentationWriter.TryCapture(
                    transaction.PresentationSnapshot.Values.Keys,
                    out WorkloadPresentationSettingsSnapshot settingsCurrent,
                    out _) ||
                     !SettingsMatch(
                         transaction.PresentationSnapshot,
                         settingsCurrent)))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool SettingsMatch(
            WorkloadPresentationSettingsSnapshot expected,
            WorkloadPresentationSettingsSnapshot observed)
        {
            if (expected == null || observed == null ||
                expected.Values.Count != observed.Values.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, WorkloadScalarValue> value in expected.Values)
            {
                if (!observed.Values.TryGetValue(value.Key, out WorkloadScalarValue current) ||
                    !current.Equals(value.Value))
                {
                    return false;
                }
            }

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
            internal readonly List<ScheduleMutation> Schedules =
                new List<ScheduleMutation>();
            internal readonly List<TypedSpecificPriorityMutation> TypedSpecificPriorities =
                new List<TypedSpecificPriorityMutation>();
            internal readonly List<TypedWorkTypeOrderMutation> TypedWorkTypeOrders =
                new List<TypedWorkTypeOrderMutation>();
            internal readonly Dictionary<string, WorkloadScalarValue> PresentationValues =
                new Dictionary<string, WorkloadScalarValue>(StringComparer.Ordinal);
            internal WorkloadBackendDimensionBaseline BackendBaseline;
            internal long AuthorityRevision;
            internal bool HasAuthorityRevision;
            internal bool MultiplayerAuthorized;
            internal WorkloadMutationAuthorization MutationAuthorization;
            internal bool SpecificJobBatchApplied;
            internal WorkGiverReassignmentManager.WorkloadSpecificJobBatchRollback SpecificJobBatchRollback;
            internal bool RequiresPriorityAuthority;
            internal int SpecificJobRevision;
            internal bool HasSpecificJobRevision;
            internal bool RequiresSpecificJobRevision;
            internal bool HasManualTarget;
            internal bool ManualTarget;
            internal bool HasLiveMutations => HasManualTarget ||
                ParentPriorities.Count > 0 ||
                SpecificJobOverrides.Count > 0 ||
                SpecificJobOrder.Count > 0 ||
                Schedules.Count > 0 ||
                TypedSpecificPriorities.Count > 0 ||
                TypedWorkTypeOrders.Count > 0 ||
                PresentationValues.Count > 0;
        }

        private sealed class ScheduleMutation
        {
            internal ScheduleMutation(
                WorkloadScheduleTargetKey key,
                TimePriorityLiveScheduleSnapshot previousSnapshot,
                WorkloadIntent<WorkloadSchedulePayload> intent)
            {
                Key = key;
                PreviousSnapshot = previousSnapshot;
                Intent = intent;
            }

            internal WorkloadScheduleTargetKey Key { get; private set; }
            internal TimePriorityLiveScheduleSnapshot PreviousSnapshot { get; private set; }
            internal WorkloadIntent<WorkloadSchedulePayload> Intent { get; private set; }
        }

        private sealed class TypedSpecificPriorityMutation
        {
            internal TypedSpecificPriorityMutation(
                WorkloadSpecificJobTargetKey key,
                WorkloadSpecificPriorityBaseline previous,
                WorkGiverReassignmentManager.ExactGlobalStateKind desiredGlobalState,
                int desiredPriority)
            {
                Key = key;
                Previous = previous;
                DesiredGlobalState = desiredGlobalState;
                DesiredPriority = desiredPriority;
            }

            internal WorkloadSpecificJobTargetKey Key { get; private set; }
            internal WorkloadSpecificPriorityBaseline Previous { get; private set; }
            internal WorkGiverReassignmentManager.ExactGlobalStateKind DesiredGlobalState { get; private set; }
            internal int DesiredPriority { get; private set; }
        }

        private sealed class TypedWorkTypeOrderMutation
        {
            internal TypedWorkTypeOrderMutation(
                WorkloadWorkTypeOrderKey key,
                WorkloadWorkTypeOrderBaseline previous,
                IReadOnlyList<string> desiredOrder,
                bool isClear)
            {
                Key = key;
                Previous = previous;
                DesiredOrder = desiredOrder == null ? null : new List<string>(desiredOrder);
                IsClear = isClear;
            }

            internal WorkloadWorkTypeOrderKey Key { get; private set; }
            internal WorkloadWorkTypeOrderBaseline Previous { get; private set; }
            internal IReadOnlyList<string> DesiredOrder { get; private set; }
            internal bool IsClear { get; private set; }
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

        private sealed class AppliedScheduleMutation
        {
            internal AppliedScheduleMutation(
                ScheduleMutation mutation,
                TimePriorityLiveScheduleSnapshot observedSnapshot,
                WorkloadMutationAuthorization authorization)
            {
                Mutation = mutation;
                ObservedSnapshot = observedSnapshot;
                Authorization = authorization;
            }

            internal ScheduleMutation Mutation { get; private set; }
            internal TimePriorityLiveScheduleSnapshot ObservedSnapshot { get; set; }
            internal WorkloadMutationAuthorization Authorization { get; private set; }
        }

        private sealed class AppliedTypedSpecificPriorityMutation
        {
            internal AppliedTypedSpecificPriorityMutation(
                TypedSpecificPriorityMutation mutation)
            {
                Mutation = mutation;
            }

            internal TypedSpecificPriorityMutation Mutation { get; private set; }
        }

        private sealed class AppliedTypedWorkTypeOrderMutation
        {
            internal AppliedTypedWorkTypeOrderMutation(
                TypedWorkTypeOrderMutation mutation)
            {
                Mutation = mutation;
            }

            internal TypedWorkTypeOrderMutation Mutation { get; private set; }
        }

        private sealed class LiveMutationTransaction
        {
            internal LiveMutationTransaction(RuntimeCommitPlan plan)
            {
                AuthorityRevision = plan?.AuthorityRevision ?? 0L;
                HasAuthorityRevision = plan?.HasAuthorityRevision == true;
                SpecificJobRevision = plan?.SpecificJobRevision ?? 0;
                HasSpecificJobRevision = plan?.HasSpecificJobRevision == true;
                ScheduleRevisionReceipt = new WorkloadScheduleRevisionReceipt(
                    plan?.BackendBaseline?.ScheduleRevision ?? TimePriorityService.CurrentVersion);
            }

            internal readonly List<AppliedPriorityMutation> Priorities =
                new List<AppliedPriorityMutation>();
            internal readonly List<AppliedSpecificJobOverrideMutation> SpecificJobOverrides =
                new List<AppliedSpecificJobOverrideMutation>();
            internal readonly List<AppliedSpecificJobOrderMutation> SpecificJobOrder =
                new List<AppliedSpecificJobOrderMutation>();
            internal readonly List<AppliedScheduleMutation> Schedules =
                new List<AppliedScheduleMutation>();
            internal readonly List<AppliedTypedSpecificPriorityMutation> TypedSpecificPriorities =
                new List<AppliedTypedSpecificPriorityMutation>();
            internal readonly List<AppliedTypedWorkTypeOrderMutation> TypedWorkTypeOrders =
                new List<AppliedTypedWorkTypeOrderMutation>();
            internal long AuthorityRevision;
            internal bool HasAuthorityRevision;
            internal int SpecificJobRevision;
            internal bool HasSpecificJobRevision;
            internal bool RollbackAttempted;
            internal bool ManualWasChanged;
            internal bool PreviousManualMode;
            internal bool PresentationWasChanged;
            internal bool SchedulePublished;
            internal WorkloadScheduleRevisionReceipt ScheduleRevisionReceipt;
            internal WorkloadMutationAuthorization WorkloadAuthorization;
            internal WorkGiverReassignmentManager.WorkloadSpecificJobBatchRollback SpecificJobBatchRollback;
            internal bool SpecificJobBatchRolledBack;
            internal WorkloadPresentationSettingsTransaction PresentationWriter;
            internal WorkloadPresentationSettingsSnapshot PresentationSnapshot;
            internal bool HasChanges => ManualWasChanged ||
                Priorities.Count > 0 ||
                SpecificJobOverrides.Count > 0 ||
                SpecificJobOrder.Count > 0 ||
                Schedules.Count > 0 ||
                TypedSpecificPriorities.Count > 0 ||
                TypedWorkTypeOrders.Count > 0 ||
                SpecificJobBatchRollback != null ||
                PresentationWasChanged;
        }

        internal sealed class WorkloadCommitExecutionContext
        {
            internal bool ValidateOnly;
            internal bool MultiplayerAuthorized;
            internal bool RetainRollbackUntilConfirmation;
            internal Func<WorkloadPersistenceReceipt, bool> PersistenceRebase;
            internal WorkloadMutationAuthorization MutationAuthorization;
            internal string PreparedPlanFingerprint;
            internal object RequestIdentity;
            internal object SessionIdentity;
            internal WorkloadCommitRollbackLease RollbackLease;
            internal WorkloadPersistenceReceipt PersistenceReceipt;
        }

        internal sealed class WorkloadCommitRollbackLease
        {
            private enum LeaseState
            {
                Active = 0,
                Confirmed = 1,
                RolledBack = 2,
                RollbackFailed = 3
            }

            private readonly Func<bool> _rollback;
            private readonly Func<bool> _confirm;
            private LeaseState _state;
            private bool _rollbackInProgress;

            internal WorkloadCommitRollbackLease(
                Func<bool> rollback,
                Func<bool> confirm = null,
                bool recoveryRequired = false)
            {
                _rollback = rollback;
                _confirm = confirm;
                _state = recoveryRequired ? LeaseState.RollbackFailed : LeaseState.Active;
            }

            internal bool Released =>
                _state == LeaseState.Confirmed || _state == LeaseState.RolledBack;

            internal bool RecoveryRequired => _state == LeaseState.RollbackFailed;

            internal bool Rollback()
            {
                if (_state == LeaseState.Confirmed || _state == LeaseState.RolledBack)
                {
                    return true;
                }

                if (_rollbackInProgress)
                {
                    return false;
                }

                _rollbackInProgress = true;
                bool restored;
                try
                {
                    restored = _rollback == null || _rollback();
                }
                catch
                {
                    restored = false;
                }
                finally
                {
                    _rollbackInProgress = false;
                }

                if (_state == LeaseState.RollbackFailed)
                {
                    // A failed rollback remains recovery-required even if a
                    // later recovery callback happens to restore the state.
                    // It must never be reclassified as a successful lease
                    // release by a duplicate protocol callback.
                    return false;
                }

                _state = restored
                    ? LeaseState.RolledBack
                    : LeaseState.RollbackFailed;
                return restored;
            }

            internal bool Confirm()
            {
                if (_state == LeaseState.RollbackFailed ||
                    _state == LeaseState.RolledBack)
                {
                    return false;
                }

                bool finalized;
                try
                {
                    finalized = _confirm == null || _confirm();
                }
                catch
                {
                    finalized = false;
                }

                if (!finalized)
                {
                    // Keep the lease active so the protocol can still issue
                    // its exactly-once rollback request.
                    return false;
                }

                _state = LeaseState.Confirmed;
                return true;
            }
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
                WorkloadV2PersistenceRecord record,
                WorkloadBackendDimensionBaseline baseline)
            {
                Kind = kind;
                StableId = stableId ?? string.Empty;
                Record = record;
                Index = -1;
                ExpectedRevision = baseline?.PersistenceRevision ?? 0;
                ExpectedFingerprint = baseline?.PersistenceFingerprint ?? string.Empty;
            }

            internal PersistenceMutationKind Kind { get; private set; }
            internal string StableId { get; private set; }
            internal WorkloadV2PersistenceRecord Record { get; private set; }
            internal WorkloadV2PersistenceRecord PreviousRecord { get; set; }
            internal int Index { get; set; }
            internal bool AddedRecord { get; set; }
            internal bool WasApplied { get; set; }
            internal int ExpectedRevision { get; private set; }
            internal string ExpectedFingerprint { get; private set; }
            internal int PreviousRevision { get; set; }
            internal string PreviousFingerprint { get; set; }
            internal string PreviousCurrentWorkloadId { get; set; }
            internal bool CurrentWorkloadIdChanged { get; set; }
            internal bool RevisionAdvanced { get; set; }
        }
    }

    /// <summary>
    /// Captures the live Work-tab state for both a new workload and a pawn
    /// added to an existing preview. Global entries stay outside the pawn
    /// loop because they do not belong to any captured pawn.
    /// </summary>
    internal static class WorkloadLiveCapture
    {
        private static readonly Func<WorkloadScheduleTargetKey, int, WorkloadSchedulePayload> LiveScheduleCapture =
            TryCaptureLiveSchedule;

        internal static WorkloadOperationResult<WorkloadTemplate> CaptureCurrentTemplate(
            string stableId,
            string label)
        {
            WorkloadOperationResult preflight = WorkloadLiveCapturePolicy.ValidateTemplateCapture(
                stableId,
                Verse.Find.CurrentMap != null,
                delegate { return WorkPrioritySystem.TryCaptureBwtMutationAuthority(out _); },
                Verse.Find.PlaySettings != null);
            if (!preflight.Succeeded)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    preflight.Code,
                    preflight.Message);
            }

            try
            {
                WorkloadOwnershipDimensions ownership =
                    WorkloadOwnershipDimensions.ParentPriorities |
                    WorkloadOwnershipDimensions.ManualModes |
                    WorkloadOwnershipDimensions.SpecificJobOverrides |
                    WorkloadOwnershipDimensions.SpecificJobOrder;
                var draft = new WorkloadDraft(WorkloadProjectedState.Empty);
                bool capturedSchedule = false;
                List<Pawn> pawns = Verse.Find.CurrentMap.mapPawns?.FreeColonists;
                for (int pawnIndex = 0; pawns != null && pawnIndex < pawns.Count; pawnIndex++)
                {
                    CapturePawn(
                        draft,
                        pawns[pawnIndex],
                        ownership,
                        null,
                        true,
                        ref capturedSchedule);
                }

                CaptureGlobalState(draft, ownership, ref capturedSchedule);
                ownership = WorkloadLiveCapturePolicy.CompleteOwnership(
                    ownership,
                    capturedSchedule);

                var definition = new WorkloadDefinition(
                    stableId,
                    label,
                    WorkloadSchema.CurrentVersion,
                    ownership,
                    WorkloadScope.CurrentMapFreeColonists());
                return WorkloadOperationResult<WorkloadTemplate>.Ok(
                    new WorkloadTemplate(definition, draft.ProjectedState));
            }
            catch (Exception exception)
            {
                Log.Error("[BWT] Typed workload capture failed.\n" + exception);
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "BWT_Workload_CaptureFailed".Translate());
            }
        }

        internal static bool CapturePawn(
            WorkloadDraft draft,
            Pawn pawn,
            WorkloadOwnershipDimensions ownership,
            IWorkTabEffectiveStateProvider liveProvider,
            bool templateCapture,
            ref bool capturedSchedule)
        {
            if (draft == null || pawn?.workSettings == null ||
                (!templateCapture && (!pawn.workSettings.EverWork || liveProvider == null)))
            {
                return false;
            }

            bool capturesSchedules = templateCapture ||
                ownership.Owns(WorkloadStateDimension.Schedules);
            bool ownsSpecificPriority =
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides);
            bool ownsSpecificOrder =
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);
            IReadOnlyList<WorkTypeDef> workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            bool wroteValue = false;
            for (int workTypeIndex = 0;
                 workTypes != null && workTypeIndex < workTypes.Count;
                 workTypeIndex++)
            {
                WorkTypeDef workType = workTypes[workTypeIndex];
                if (workType == null || workType.defName.NullOrEmpty())
                {
                    continue;
                }

                WorkloadParentPriorityKey parentKey = new WorkloadParentPriorityKey(
                    WorkTabEffectiveStateIds.ForPawn(pawn),
                    WorkTabEffectiveStateIds.ForWorkType(workType));
                int parentFallback = templateCapture
                    ? ParentPriorityRead.GetLive(pawn, workType)
                    : ParentPriorityRead.GetObservationalLive(pawn, workType);
                if (ownership.Owns(WorkloadStateDimension.ParentPriorities))
                {
                    int storedPriority = templateCapture
                        ? PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(pawn.workSettings, workType)
                        : parentFallback;
                    int effectivePriority = parentFallback;
                    int priority = WorkloadLiveCapturePolicy.SelectParentPriority(
                        templateCapture,
                        storedPriority,
                        effectivePriority);
                    draft.SetParentPriority(parentKey, priority);
                    wroteValue = true;
                }

                if (ownership.Owns(WorkloadStateDimension.ManualModes))
                {
                    bool manualMode = ParentPriorityRead.GetLiveManualMode(true);
                    draft.SetManualMode(parentKey, manualMode);
                    wroteValue = true;
                }

                if (capturesSchedules && WorkloadLiveCapturePolicy.TryCaptureSchedule(
                        draft,
                        WorkloadScheduleTargetKey.ForParent(
                            parentKey.Pawn,
                            parentKey.WorkType),
                        parentFallback,
                        LiveScheduleCapture))
                {
                    capturedSchedule = true;
                    wroteValue = true;
                }

                if (!templateCapture && !ownsSpecificPriority && !ownsSpecificOrder)
                {
                    continue;
                }

                IReadOnlyList<WorkGiver> workGivers =
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(
                        workType,
                        pawn);
                for (int workGiverIndex = 0;
                     workGivers != null && workGiverIndex < workGivers.Count;
                     workGiverIndex++)
                {
                    WorkGiverDef workGiver = workGivers[workGiverIndex]?.def;
                    if (workGiver == null || workGiver.defName.NullOrEmpty())
                    {
                        continue;
                    }

                    WorkloadSpecificJobKey specificKey =
                        WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver);
                    int inheritedPriority =
                        WorkGiverReassignmentManager.GetWorkGiverPriority(
                            pawn,
                            workGiver,
                            parentFallback);
                    if (ownsSpecificPriority)
                    {
                        if (templateCapture)
                        {
                            if (WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                                    pawn,
                                    workGiver,
                                    out int priority))
                            {
                                draft.SetSpecificPriority(specificKey.ToTargetKey(), priority);
                                wroteValue = true;
                            }
                        }
                        else
                        {
                            draft.SetSpecificJobOverride(
                                specificKey,
                                liveProvider.GetSpecificJobOverride(
                                    specificKey,
                                    WorkloadScalarValue.FromInteger(inheritedPriority)));
                            wroteValue = true;
                        }
                    }

                    if (capturesSchedules && WorkloadLiveCapturePolicy.TryCaptureSchedule(
                            draft,
                            WorkloadScheduleTargetKey.ForWorkGiver(
                                parentKey.Pawn,
                                parentKey.WorkType,
                                WorkTabEffectiveStateIds.ForWorkGiver(workGiver)),
                            inheritedPriority,
                            LiveScheduleCapture))
                    {
                        capturedSchedule = true;
                        wroteValue = true;
                    }

                    if (!templateCapture && ownsSpecificOrder &&
                        liveProvider.TryGetSpecificJobOrder(specificKey, out int order))
                    {
                        draft.SetSpecificJobOrder(specificKey, order);
                        wroteValue = true;
                    }
                }

                if (templateCapture && ownsSpecificOrder &&
                    WorkGiverReassignmentManager.HasPawnOrdering(pawn, workType))
                {
                    WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot snapshot =
                        WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                            pawn,
                            workType);
                    if (WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                            draft,
                            WorkTabEffectiveStateIds.ForWorkTypeOrder(pawn, workType),
                            true,
                            false,
                            snapshot.OrderedWorkGiverNames))
                    {
                        wroteValue = true;
                    }
                }
            }

            return wroteValue;
        }

        private static void CaptureGlobalState(
            WorkloadDraft draft,
            WorkloadOwnershipDimensions ownership,
            ref bool capturedSchedule)
        {
            IReadOnlyList<WorkTypeDef> workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int workTypeIndex = 0;
                 workTypes != null && workTypeIndex < workTypes.Count;
                 workTypeIndex++)
            {
                WorkTypeDef workType = workTypes[workTypeIndex];
                if (workType == null || workType.defName.NullOrEmpty())
                {
                    continue;
                }

                IReadOnlyList<WorkGiver> workGivers =
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType);
                for (int workGiverIndex = 0;
                     workGivers != null && workGiverIndex < workGivers.Count;
                     workGiverIndex++)
                {
                    WorkGiverDef workGiver = workGivers[workGiverIndex]?.def;
                    if (workGiver == null || workGiver.defName.NullOrEmpty())
                    {
                        continue;
                    }

                    if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
                    {
                        WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot snapshot =
                            WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(
                                workGiver.defName);
                        WorkloadSpecificJobTargetKey key =
                            WorkTabEffectiveStateIds.ForGlobalSpecificJobTarget(
                                workType,
                                workGiver);
                        WorkloadLiveCapturePolicy.ApplySpecificPrioritySnapshot(
                            draft,
                            key,
                            snapshot.HasStoredValue,
                            snapshot.IsExplicitlyCleared,
                            snapshot.Priority);
                    }

                    if (WorkloadLiveCapturePolicy.TryCaptureSchedule(
                            draft,
                            WorkloadScheduleTargetKey.GlobalWorkGiver(
                                WorkTabEffectiveStateIds.ForWorkType(workType),
                                WorkTabEffectiveStateIds.ForWorkGiver(workGiver)),
                            WorkGiverReassignmentManager.GetWorkGiverPriority(
                                null,
                                workGiver,
                                WorkPrioritySystem.GetDefaultEnabledPriority()),
                            LiveScheduleCapture))
                    {
                        capturedSchedule = true;
                    }
                }

                if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
                {
                    WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot snapshot =
                        WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(
                            workType.defName);
                    WorkloadWorkTypeOrderKey key =
                        WorkTabEffectiveStateIds.ForGlobalWorkTypeOrder(workType);
                    WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                        draft,
                        key,
                        snapshot.HasStoredValue,
                        snapshot.IsExplicitlyCleared,
                        snapshot.OrderedWorkGiverNames);
                }
            }
        }

        private static WorkloadSchedulePayload TryCaptureLiveSchedule(
            WorkloadScheduleTargetKey key,
            int fallbackPriority)
        {
            return WorkloadTimePriorityAdapter.TryCaptureLiveScheduleSnapshot(
                    key,
                    fallbackPriority,
                    out TimePriorityLiveScheduleSnapshot snapshot,
                    out _) && snapshot.HadSchedule
                ? WorkloadTimePriorityAdapter.ToPayload(snapshot.Schedule)
                : null;
        }
    }
}
