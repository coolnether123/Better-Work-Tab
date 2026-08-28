using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using RimWorld;
using Spine.Profiling;
using Verse;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    public enum WorkloadV2CommitEntryKind
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

    public sealed class WorkloadV2CommitEntry
    {
        internal WorkloadV2CommitEntry(
            WorkloadV2CommitEntryKind kind,
            string code,
            string subject)
        {
            Kind = kind;
            Code = code ?? string.Empty;
            Subject = subject ?? string.Empty;
        }

        public WorkloadV2CommitEntryKind Kind { get; private set; }
        public string Code { get; private set; }
        public string Subject { get; private set; }
    }

    /// <summary>
    /// Structured diagnostics for one V2 commit attempt. Runtime entries that
    /// cannot be applied are retained as explicit diagnostics instead of being
    /// counted as successful writes.
    /// </summary>
    public sealed class WorkloadV2CommitReport
    {
        private readonly List<WorkloadV2CommitEntry> _entries =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _changed =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _unchanged =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _skipped =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _excluded =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _missingOrStale =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _unsupported =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _fatal =
            new List<WorkloadV2CommitEntry>();
        private readonly List<WorkloadV2CommitEntry> _cleared =
            new List<WorkloadV2CommitEntry>();

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

        public IReadOnlyList<WorkloadV2CommitEntry> Entries => _entries.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> Changed => _changed.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> Unchanged => _unchanged.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> Skipped => _skipped.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> Excluded => _excluded.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> MissingOrStale => _missingOrStale.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> MissingStale => MissingOrStale;
        public IReadOnlyList<WorkloadV2CommitEntry> Unsupported => _unsupported.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> Fatal => _fatal.AsReadOnly();
        public IReadOnlyList<WorkloadV2CommitEntry> Cleared => _cleared.AsReadOnly();

        internal void Add(
            WorkloadV2CommitEntryKind kind,
            string code,
            string subject)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                WorkloadV2CommitEntry existing = _entries[i];
                if (existing.Kind == kind &&
                    StringComparer.Ordinal.Equals(existing.Code, code ?? string.Empty) &&
                    StringComparer.Ordinal.Equals(existing.Subject, subject ?? string.Empty))
                {
                    return;
                }
            }

            var diagnostic = new WorkloadV2CommitEntry(kind, code, subject);
            _entries.Add(diagnostic);
            switch (kind)
            {
                case WorkloadV2CommitEntryKind.Changed:
                    _changed.Add(diagnostic);
                    break;
                case WorkloadV2CommitEntryKind.Unchanged:
                    _unchanged.Add(diagnostic);
                    break;
                case WorkloadV2CommitEntryKind.Skipped:
                    _skipped.Add(diagnostic);
                    break;
                case WorkloadV2CommitEntryKind.Excluded:
                    _excluded.Add(diagnostic);
                    break;
                case WorkloadV2CommitEntryKind.MissingOrStale:
                    _missingOrStale.Add(diagnostic);
                    break;
                case WorkloadV2CommitEntryKind.Unsupported:
                    _unsupported.Add(diagnostic);
                    break;
                case WorkloadV2CommitEntryKind.Fatal:
                    _fatal.Add(diagnostic);
                    break;
                case WorkloadV2CommitEntryKind.Cleared:
                    _cleared.Add(diagnostic);
                    break;
            }
        }
    }

    /// <summary>
    /// Describes whether a commit's live application publication has already
    /// been handled. The footer uses this receipt to avoid repeating a global
    /// pawn-table refresh after the application publisher has done its work.
    /// </summary>
    internal enum WorkloadApplicationPublication
    {
        None,
        NoChange,
        Applied,
        Pending
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
            WorkloadV2CommitReport report,
            WorkloadPreviewPlan plan,
            WorkloadTemplate resultTemplate,
            string stableId,
            WorkloadDiagnosticContext context = null)
        {
            Succeeded = succeeded;
            Code = code;
            Context = context ?? (string.IsNullOrEmpty(stableId)
                ? WorkloadDiagnosticContext.Empty
                : new WorkloadDiagnosticContext(stableId: stableId));
            Report = report;
            Plan = plan;
            ResultTemplate = resultTemplate;
            StableId = stableId ?? string.Empty;
        }

        public bool Succeeded { get; private set; }
        public WorkloadDiagnosticCode Code { get; private set; }
        public WorkloadDiagnosticContext Context { get; private set; }
        public WorkloadV2CommitReport Report { get; private set; }
        public WorkloadPreviewPlan Plan { get; private set; }
        public WorkloadTemplate ResultTemplate { get; private set; }
        public string StableId { get; private set; }
        public bool PreviewCleared { get; internal set; }
        internal WorkloadPersistenceReceipt PersistenceReceipt { get; set; }
        internal WorkloadSession RebasedSession { get; set; }
        internal WorkloadApplicationPublication ApplicationPublication { get; set; }
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
        private readonly IWorkloadWorldState _component;
        private readonly WorkloadV2ApplyService _applyService;
        private readonly WorkloadV2DescriptorCatalog _descriptorCatalog =
            new WorkloadV2DescriptorCatalog();
        private WorkloadSession _previewSession;

        internal Workload2Backend(IWorkloadWorldState component)
            : this(component, true)
        {
        }

        private Workload2Backend(
            IWorkloadWorldState component,
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
        internal IWorkloadWorldState WorldState => _component;

        internal static Workload2Backend CreateMultiplayerTransactionBackend()
        {
            IWorkloadWorldState component = WorkloadWorldStates.Current;
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
            WorkloadV2PersistenceEnvelope store = Store;
            if (store == null) return Array.Empty<WorkloadDescriptor>();
            _descriptorCatalog.EnsureCurrent(store);
            return _descriptorCatalog.Descriptors;
        }

        internal WorkloadOperationResult<WorkloadDescriptor> Current()
        {
            WorkloadV2PersistenceEnvelope store = Store;
            if (store == null)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame);
            }

            _descriptorCatalog.EnsureCurrent(store);
            return _descriptorCatalog.Current;
        }

        internal WorkloadOperationResult Select(string workloadId)
        {
            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded) return ready;

            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(workloadId);
            if (!found.Succeeded) return WorkloadOperationResult.Fail(found.Code, found.Context);

            WorkloadOperationResult<WorkloadTemplate> template =
                WorkloadV2RecordConverter.TryToTemplate(found.Value);
            if (!template.Succeeded)
            {
                return WorkloadOperationResult.Fail(template.Code, template.Context);
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
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(ready.Code, ready.Context);
            }

            string finalLabel = WorkloadLiveCapturePolicy.ResolveLabel(label, GetDefaultLabel);
            string stableId = Guid.NewGuid().ToString("N");
            WorkloadOperationResult<WorkloadTemplate> captured =
                WorkloadLiveCapture.CaptureCurrentTemplate(stableId, finalLabel);
            if (!captured.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(captured.Code, captured.Context);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(ready.Code, ready.Context);
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
                    new WorkloadDiagnosticContext(
                        stableId: template?.StableId,
                        path: issue?.Path,
                        validationCode: issue?.Code,
                        expectedVersion: WorkloadSchema.CurrentVersion,
                        actualVersion: template?.SchemaVersion));
            }

            if (WorkloadV2OwnershipResolver.HasUnsupportedLegacyPayload(template))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.UnsupportedLegacyState);
            }

            WorkloadOperationResult<WorkloadV2PersistenceRecord> converted =
                WorkloadV2RecordConverter.TryFromTemplate(template);
            if (!converted.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(converted.Code, converted.Context);
            }

            if (Store.HasDuplicateStableId(converted.Value.StableId))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId);
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
                        existingTemplate.Context);
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
                WorkloadV2DescriptorCatalog.CreateDescriptor(Store, converted.Value, makeCurrent));
        }

        internal WorkloadOperationResult Rename(string workloadId, string newLabel)
        {
            if (string.IsNullOrWhiteSpace(newLabel))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidLabel);
            }

            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded) return ready;
            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(workloadId);
            if (!found.Succeeded) return WorkloadOperationResult.Fail(found.Code, found.Context);

            WorkloadOperationResult<WorkloadTemplate> template =
                WorkloadV2RecordConverter.TryToTemplate(found.Value);
            if (!template.Succeeded) return WorkloadOperationResult.Fail(template.Code, template.Context);

            found.Value.Label = newLabel;
            NotifyChanged();
            return WorkloadOperationResult.Ok();
        }

        internal WorkloadOperationResult Delete(string workloadId)
        {
            WorkloadOperationResult ready = RequireMutableStore();
            if (!ready.Succeeded) return ready;
            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(workloadId);
            if (!found.Succeeded) return WorkloadOperationResult.Fail(found.Code, found.Context);

            WorkloadOperationResult<WorkloadTemplate> template =
                WorkloadV2RecordConverter.TryToTemplate(found.Value, Store.IsReadOnlyDiagnostic);
            if (!template.Succeeded)
            {
                return WorkloadOperationResult.Fail(template.Code, template.Context);
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
                    return WorkloadOperationResult.Fail(opened.Code, opened.Context);
                }
            }
            else if (!string.IsNullOrEmpty(workloadId) &&
                     !string.Equals(_previewSession.SourceTemplate.StableId, workloadId, StringComparison.Ordinal))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }

            WorkloadV2CommitResult result = CommitPreview(WorkloadDecisionKind.Apply, null, null);
            return result.Succeeded
                ? WorkloadOperationResult.Ok()
                : WorkloadOperationResult.Fail(result.Code, result.Context);
        }

        internal WorkloadOperationResult<WorkloadTemplate> GetTemplate(string workloadId = null)
        {
            string id = workloadId;
            if (string.IsNullOrEmpty(id))
            {
                WorkloadOperationResult<WorkloadDescriptor> current = Current();
                if (!current.Succeeded)
                {
                    return WorkloadOperationResult<WorkloadTemplate>.Fail(current.Code, current.Context);
                }

                id = current.Value.StableId;
            }

            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(id);
            if (!found.Succeeded)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(found.Code, found.Context);
            }

            return WorkloadV2RecordConverter.TryToTemplate(found.Value, Store.IsReadOnlyDiagnostic);
        }

        internal WorkloadOperationResult<WorkloadSession> BeginPreview(string workloadId = null)
        {
            if (_previewSession != null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }

            WorkloadOperationResult<WorkloadTemplate> template = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.WorkloadPreview.LoadTemplate",
                    () => GetTemplate(workloadId))
                : GetTemplate(workloadId);
            if (!template.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(template.Code, template.Context);
            }

            WorkloadOperationResult<WorkloadLiveBaselineCapture> liveCapture =
                SpineTiming.Enabled
                    ? SpineTiming.Time(
                        "WorkTab.WorkloadPreview.CaptureLiveBaseline",
                        () => _applyService.CaptureLiveBaselineCapture(template.Value))
                    : _applyService.CaptureLiveBaselineCapture(template.Value);
            if (!liveCapture.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    liveCapture.Code,
                    liveCapture.Context);
            }

            WorkloadSession opened = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.WorkloadPreview.OpenSessionModel",
                    () => WorkloadSession.OpenCaptured(
                        template.Value,
                        liveCapture.Value.State,
                        WorkloadSession.GetSourceIdentity(template.Value),
                        runtimeBaseline: liveCapture.Value.RuntimeBaseline))
                : WorkloadSession.OpenCaptured(
                    template.Value,
                    liveCapture.Value.State,
                    WorkloadSession.GetSourceIdentity(template.Value),
                    runtimeBaseline: liveCapture.Value.RuntimeBaseline);
            if (opened == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                    WorkloadDiagnosticCode.InvalidState);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (_previewSession?.RuntimeBaseline != null &&
                (session.RuntimeBaseline == null ||
                 !session.RuntimeBaseline.Preserves(_previewSession.RuntimeBaseline)))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                            WorkloadDiagnosticCode.InvalidState);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            _previewSession = session;
            return WorkloadOperationResult<WorkloadSession>.Ok(session);
        }

        internal WorkloadOperationResult<WorkloadSession> EditPreview(Action<WorkloadDraft> edit)
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NotFound);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (!StringComparer.Ordinal.Equals(
                    _previewSession.SourceIdentity,
                    candidate.SourceIdentity) ||
                !candidate.HasCapturedLiveBaseline ||
                candidate.RuntimeBaseline == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                    WorkloadDiagnosticCode.InvalidState);
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
                    WorkloadDiagnosticCode.InvalidState);
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
                    capture.Context);
            }

            WorkloadRuntimeBaseline extension = capture.Value.RuntimeBaseline;
            WorkloadRuntimeBaseline existing = previous.RuntimeBaseline;
            if (existing.HasManualMode && extension.HasManualMode &&
                existing.ManualMode != extension.ManualMode)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                    WorkloadDiagnosticCode.InvalidState);
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
                            WorkloadDiagnosticCode.InvalidState);
                    }
                }
            }

            WorkloadRuntimeBaseline merged = existing.ExtendForPawns(extension, added);
            if (merged == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                    refresh.Context);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (!_applyService.TryGetBackendBaseline(
                    previous,
                    out WorkloadBackendDimensionBaseline existing))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                    capture.Context);
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
                    WorkloadDiagnosticCode.NotFound);
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
                    WorkloadDiagnosticCode.NotFound);
            }

            _previewSession = _previewSession.Revert();
            return WorkloadOperationResult<WorkloadSession>.Ok(_previewSession);
        }

        internal WorkloadOperationResult<WorkloadPreviewPlan> PreviewPlan(WorkloadDecisionKind decisionKind)
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                    WorkloadDiagnosticCode.NotFound);
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
                        WorkloadDiagnosticCode.UnsupportedDecision);
            }

            if (decision?.Plan == null)
            {
                return WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                    WorkloadDiagnosticCode.NotFound);
            }

            WorkloadOperationResult<WorkloadPreviewPlan> plan =
                PreviewPlan(WorkloadDecisionKind.Update);
            return plan.Succeeded
                ? WorkloadOperationResult<WorkloadSemanticDiff>.Ok(plan.Value.Diff)
                : WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    plan.Code,
                    plan.Context);
        }

        internal WorkloadOperationResult<WorkloadSemanticDiff> PreviewImpactDiff()
        {
            if (_previewSession == null)
            {
                return WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.NotFound);
            }

            WorkloadOperationResult<WorkloadPreviewPlan> plan =
                PreviewPlan(WorkloadDecisionKind.Apply);
            return plan.Succeeded
                ? WorkloadOperationResult<WorkloadSemanticDiff>.Ok(plan.Value.Diff)
                : WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    plan.Code,
                    plan.Context);
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
                    WorkloadDiagnosticCode.NotFound);
            }

            WorkloadSession rebased = _previewSession.RebaseAfterPersistence(receipt);
            if (rebased == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            WorkloadOperationResult rekey = _applyService.RekeyBackendBaseline(
                _previewSession,
                receipt);
            if (!rekey.Succeeded)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    rekey.Code,
                    rekey.Context);
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
                    WorkloadDiagnosticCode.NotFound);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (string.IsNullOrWhiteSpace(request.SessionId) ||
                request.ExpectedRevisions == null ||
                request.ExpectedRevisions.SessionRevision <= 0 ||
                request.ExpectedRevisions.MembershipRevision <= 0)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }

            WorkloadOperationResult<WorkloadV2PersistenceRecord> found = Find(request.SourceWorkloadId);
            if (!found.Succeeded)
                return WorkloadOperationResult<WorkloadSession>.Fail(found.Code, found.Context);
            WorkloadOperationResult<WorkloadTemplate> source = WorkloadV2RecordConverter.TryToTemplate(found.Value);
            if (!source.Succeeded)
                return WorkloadOperationResult<WorkloadSession>.Fail(source.Code, source.Context);
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    WorkloadSession.GetSourceIdentity(source.Value),
                    request.ExpectedRevisions.SourceTemplateFingerprint))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                return WorkloadOperationResult<WorkloadSession>.Fail(capture.Code, capture.Context);
            WorkloadBackendDimensionBaseline observed = capture.Value.BackendBaseline;
            WorkloadV2PersistenceEnvelope store = _component.EnsureV2Persistence();
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
                    WorkloadDiagnosticCode.BaselineChanged);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (!StringComparer.Ordinal.Equals(
                    session.PreviewSessionId,
                    request.SessionId) ||
                session.SessionRevision != request.ExpectedRevisions.SessionRevision ||
                session.MembershipRevision != request.ExpectedRevisions.MembershipRevision)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.BaselineChanged);
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
                    WorkloadDiagnosticCode.InvalidState);
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
                    validation.Context);
            }

            if (!_applyService.TryGetBackendBaseline(
                    session,
                    out WorkloadBackendDimensionBaseline baseline))
            {
                return WorkloadOperationResult<WorkloadPreparedTransaction>.Fail(
                    WorkloadDiagnosticCode.BaselineChanged);
            }

            return WorkloadOperationResult<WorkloadPreparedTransaction>.Ok(
                new WorkloadPreparedTransaction(
                    request,
                    session,
                    decision,
                    targetStableId,
                    forkLabel,
                    validation,
                    BuildPreparedPlanFingerprint(
                        validation.Plan,
                        decision,
                        targetStableId,
                        baseline)));
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
                    "multiplayer.execute.prepare");
            }

            if (!_applyService.TryGetBackendBaseline(
                    prepared.Session,
                    out WorkloadBackendDimensionBaseline baseline) ||
                prepared.ExecuteStarted ||
                !StringComparer.Ordinal.Equals(
                    BuildPreparedPlanFingerprint(
                        prepared.Validation.Plan,
                        prepared.Decision,
                        prepared.TargetStableId,
                        baseline),
                    prepared.PlanFingerprint))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.InvalidState,
                    "multiplayer.execute.prepare");
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
                    "multiplayer.execute.authorization");
            }

            if (targetTemplate == null)
            {
                authorizationReason =
                    "The synchronized workload prepare has no result template.";
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.MutationCapabilityRejected,
                    "multiplayer.execute.authorization");
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
                    "multiplayer.execute.authorization");
            }

            if (!WorkTabMutationAuthorization.TryCreate(
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
                    out WorkTabMutationAuthorization authorization,
                    out authorizationReason))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    prepared.Decision,
                    WorkloadDiagnosticCode.MutationCapabilityRejected,
                    "multiplayer.execute.authorization");
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
            string targetStableId,
            WorkloadBackendDimensionBaseline baseline = null)
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
                (plan.Diff?.AfterFingerprint ?? string.Empty) + "\u001f" +
                BuildLoweredMutationFingerprint(baseline));
        }

        private static string BuildLoweredMutationFingerprint(
            WorkloadBackendDimensionBaseline baseline)
        {
            if (baseline == null)
                return string.Empty;

            var builder = new StringBuilder();
            builder.Append(baseline.AuthorityRevision).Append('|')
                .Append(baseline.ScheduleRevision).Append('|')
                .Append(baseline.SpecificJobRevision).Append('|')
                .Append(WorkloadCanonical.Encode(
                    baseline.TaxonomyFingerprint ?? string.Empty));

            var priorities = baseline.SpecificPriorities.Values
                .Where(value => value != null)
                .OrderBy(value => value.Key?.ToString() ?? string.Empty, StringComparer.Ordinal);
            foreach (WorkloadSpecificPriorityBaseline value in priorities)
            {
                builder.Append("|p:")
                    .Append(WorkloadCanonical.Encode(value.Key?.ToString() ?? string.Empty))
                    .Append(':').Append((int)value.State.State)
                    .Append(':').Append(value.State.Priority);
            }

            var orders = baseline.WorkTypeOrders.Values
                .Where(value => value != null)
                .OrderBy(value => value.Key?.ToString() ?? string.Empty, StringComparer.Ordinal);
            foreach (WorkloadWorkTypeOrderBaseline value in orders)
            {
                builder.Append("|o:")
                    .Append(WorkloadCanonical.Encode(value.Key?.ToString() ?? string.Empty))
                    .Append(':').Append((int)value.State.State);
                IReadOnlyList<string> names = value.State.OrderedWorkGiverNames;
                for (int i = 0; names != null && i < names.Count; i++)
                {
                    builder.Append(':').Append(WorkloadCanonical.Encode(names[i]));
                }
            }

            var schedules = baseline.Schedules
                .OrderBy(value => value.Key?.ToString() ?? string.Empty, StringComparer.Ordinal);
            foreach (KeyValuePair<WorkloadScheduleTargetKey, TimePriorityLiveScheduleSnapshot> value
                     in schedules)
            {
                TimePriorityLiveScheduleSnapshot snapshot = value.Value;
                builder.Append("|s:")
                    .Append(WorkloadCanonical.Encode(value.Key?.ToString() ?? string.Empty))
                    .Append(':').Append(snapshot?.ServiceVersion ?? -1)
                    .Append(':').Append(snapshot?.HadSchedule == true ? 1 : 0)
                    .Append(':').Append(snapshot?.Schedule?.PinnedHourMask ?? 0);
                TimePriorityScheduleValue schedule = snapshot?.Schedule;
                for (int hour = 0; schedule != null && hour < 24; hour++)
                {
                    builder.Append(':').Append(schedule.PriorityAt(hour));
                }
            }

            return WorkloadCanonical.Fingerprint(builder.ToString());
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
            WorkloadV2PersistenceEnvelope store = _component?.EnsureV2Persistence();
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
                    "multiplayer.pending");
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
                    WorkloadDiagnosticCode.NotFound);
            }

            WorkloadV2PersistenceEnvelope store = Store;
            if (store == null)
            {
                return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame);
            }

            if (store.HasDuplicateStableId(workloadId))
            {
                return WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId);
            }

            WorkloadV2PersistenceRecord record = store.Find(workloadId);
            return record == null
                ? WorkloadOperationResult<WorkloadV2PersistenceRecord>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId)
                : WorkloadOperationResult<WorkloadV2PersistenceRecord>.Ok(record);
        }

        private WorkloadOperationResult RequireMutableStore()
        {
            if (_component == null)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame);
            }

            WorkloadV2PersistenceEnvelope store = Store;
            store.RefreshDiagnostics();
            if (store.IsReadOnlyDiagnostic)
            {
                return WorkloadOperationResult.Fail(
                    store.DiagnosticCode == WorkloadDiagnosticCode.None
                        ? WorkloadDiagnosticCode.ReadOnlyDiagnostic
                        : store.DiagnosticCode);
            }

            return WorkloadOperationResult.Ok();
        }

        private WorkloadV2PersistenceEnvelope Store
        {
            get { return _component?.EnsureV2Persistence(); }
        }

        private void NotifyChanged()
        {
            _component?.NotifyV2Changed();
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
                    WorkloadDiagnosticCode.MultiplayerUnavailable);
            if (backend == null || session == null)
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.NotFound);
            if (!MultiplayerBridge.TryGetWorkloadSessionContext(out var epoch, out var roster) ||
                !MultiplayerBridge.TryGetWorkloadParticipantKeys(out var participants))
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.MultiplayerUnavailable);

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
                    converted.Code);
            string payloadError = null;
            if (!WorkloadMultiplayerPayloadCodec.TrySerialize(converted.Value, out var bytes, out var codecError) ||
                !WorkloadTransactionPayload.TryCreate(bytes, null, out var payload, out payloadError))
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.InvalidState);

            WorkloadV2PersistenceEnvelope store = backend.WorldState?.EnsureV2Persistence();
            if (store == null)
                return Status(string.Empty, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.NoCurrentGame);
            store.RefreshDiagnostics();
            string requestId = Guid.NewGuid().ToString("N");
            string effectiveIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey;
            if (!backend.TryCreateMultiplayerRevisionVector(
                session, epoch, roster, out var revisions))
                return Status(requestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.BaselineChanged);
            if (!WorkloadTransactionRequest.TryCreateCanonical(
                    ToOperation(decision), requestId, effectiveIdempotencyKey,
                    session.PreviewSessionId, session.SourceTemplate.StableId, targetId,
                    MultiplayerBridge.LocalPlayerName, 3600, payload, revisions,
                    participants, out var request, out var diagnostic))
                return Status(requestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.InvalidState);

            long sequence = Verse.Find.TickManager?.TicksGame ?? 0;
            WorkloadTransactionAdmission admission = WorkloadTransactionMultiplayer.TryBegin(request, sequence);
            if (!admission.Accepted)
            {
                string correlatedRequestId = admission.RegisteredRequest?.RequestId ?? requestId;
                if (admission.Code == WorkloadTransactionAdmissionCode.MismatchedDuplicate)
                {
                    return Status(admission.Request?.RequestId ?? requestId,
                        WorkloadMultiplayerCommitState.Rejected,
                        WorkloadDiagnosticCode.PersistenceConflict);
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
                                : WorkloadDiagnosticCode.InvalidState);
                }

                if (admission.Code == WorkloadTransactionAdmissionCode.Duplicate &&
                    admission.State != null &&
                    !admission.State.IsFinal)
                {
                    return Status(
                        correlatedRequestId,
                        WorkloadMultiplayerCommitState.Pending,
                        WorkloadDiagnosticCode.None);
                }

                if (admission.Code == WorkloadTransactionAdmissionCode.RecoveryRequired)
                {
                    return Status(
                        correlatedRequestId,
                        WorkloadMultiplayerCommitState.RollbackFailed,
                        WorkloadDiagnosticCode.RollbackFailed);
                }

                return Status(requestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.MultiplayerUnavailable);
            }
            return Status(requestId, WorkloadMultiplayerCommitState.Pending,
                WorkloadDiagnosticCode.None);
        }

        public void OnRequestAccepted(WorkloadTransactionRequest request) { }

        public void OnAdmissionRejected(WorkloadTransactionAdmission admission)
        {
            if (admission?.Code == WorkloadTransactionAdmissionCode.MismatchedDuplicate)
            {
                Status(admission.Request?.RequestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.PersistenceConflict);
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
                            : WorkloadDiagnosticCode.InvalidState);
                return;
            }

            if (admission?.Code == WorkloadTransactionAdmissionCode.Duplicate &&
                admission.State != null && !admission.State.IsFinal)
            {
                Status(
                    admission.RegisteredRequest?.RequestId ?? admission.Request?.RequestId,
                    WorkloadMultiplayerCommitState.Pending,
                    WorkloadDiagnosticCode.None);
                return;
            }

            if (admission?.Code == WorkloadTransactionAdmissionCode.RecoveryRequired)
            {
                Status(
                    admission.RegisteredRequest?.RequestId ?? admission.Request?.RequestId,
                    WorkloadMultiplayerCommitState.RollbackFailed,
                    WorkloadDiagnosticCode.RollbackFailed);
                return;
            }

            Status(admission?.Request?.RequestId, WorkloadMultiplayerCommitState.Rejected,
                WorkloadDiagnosticCode.MultiplayerUnavailable);
        }

        public void OnPrepareRequested(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            bool accepted = TryPrepare(request, out var pending, out var code, out var detail);
            if (accepted) _pending[request.TableKey] = pending;
            if (accepted) _pendingByRequestId[request.RequestId] = pending;
            if (accepted)
                Status(request.RequestId, WorkloadMultiplayerCommitState.Prepared,
                    WorkloadDiagnosticCode.None);
            else
                Status(request.RequestId, WorkloadMultiplayerCommitState.Rejected,
                    WorkloadDiagnosticCode.InvalidState);
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
                detail = pending.Result.Code.ToString();
                pending.ReportFingerprint = ReportFingerprint(
                    pending.Result,
                    pending.Prepared?.PlanFingerprint);
                if (accepted)
                    Status(request.RequestId,
                        WorkloadMultiplayerCommitState.ExecutedAwaitingConfirmation,
                        WorkloadDiagnosticCode.None, pending.Result);
                else
                    Status(request.RequestId,
                        pending.Result.Code == WorkloadDiagnosticCode.RollbackFailed
                            ? WorkloadMultiplayerCommitState.RollbackFailed
                            : WorkloadMultiplayerCommitState.Failed,
                        pending.Result.Code,
                        pending.Result);
            }
            bool noChange = accepted &&
                pending?.Result?.ApplicationPublication ==
                WorkloadApplicationPublication.NoChange;
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
                Code = accepted
                    ? noChange
                        ? WorkloadTransactionCodes.NoChange
                        : "executed"
                    : "execute-failed",
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
                    result.Code,
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
                state?.IsNoChange == true
                    ? WorkloadTransactionCodes.NoChange
                    : "confirm-requested",
                "The host is requesting per-peer confirmation before terminal success."));
        }

        public void OnConfirmationControlReceived(WorkloadTransactionRequest request, WorkloadTransactionState state)
        {
            _pending.TryGetValue(request.TableKey, out var pending);
            bool noChange = state?.IsNoChange == true;
            bool accepted = state != null && state.ConfirmationControlAccepted &&
                            pending != null &&
                            (noChange
                                ? pending.Result?.ApplicationPublication ==
                                      WorkloadApplicationPublication.NoChange &&
                                  pending.Lease == null
                                : pending.Lease != null &&
                                  !pending.Lease.RecoveryRequired);
            string detail = accepted
                ? noChange
                    ? "The peer confirmed that the synchronized workload transaction made no changes."
                    : "The peer retained its rollback lease and acknowledged host confirmation."
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
                Code = accepted
                    ? noChange
                        ? WorkloadTransactionCodes.NoChange
                        : "confirmation-ready"
                    : "confirmation-failed",
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
                state?.IsNoChange == true
                    ? WorkloadTransactionCodes.NoChange
                    : "confirmed",
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
                WorkloadDiagnosticCode.RollbackRequired);
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
                        : WorkloadDiagnosticCode.InvalidState, pending?.Result);
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
            if (!payload.Succeeded) { detail = payload.Code.ToString(); return false; }
            WorkloadOperationResult<WorkloadSession> rebuilt = backend.BuildMultiplayerSession(request, payload.Value);
            if (!rebuilt.Succeeded) { detail = rebuilt.Code.ToString(); return false; }
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
                detail = prepared.Code.ToString();
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
                ReportFingerprint = ReportFingerprint(
                    prepared.Value.Validation,
                    prepared.Value.PlanFingerprint)
            };
            code = "prepared";
            detail = prepared.Value.Validation.Code.ToString();
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

        private static string ReportFingerprint(
            WorkloadV2CommitResult result,
            string preparedPlanFingerprint = null)
        {
            string canonical = (result?.Succeeded == true ? "1" : "0") + "\u001f" +
                (result?.StableId ?? string.Empty) + "\u001f" +
                (result?.ResultTemplate?.SemanticFingerprint ?? string.Empty) + "\u001f" +
                (result?.Report?.ChangedCount ?? 0).ToString(CultureInfo.InvariantCulture) + "\u001f" +
                (result?.Report?.ClearedCount ?? 0).ToString(CultureInfo.InvariantCulture) + "\u001f" +
                (preparedPlanFingerprint ?? string.Empty);
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
            WorkloadDiagnosticCode code, WorkloadV2CommitResult result = null)
        {
            var status = new WorkloadMultiplayerCommitStatus(requestId, state, code, null, result);
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
        internal WorkTabSpecificPriorityBaseline State { get; set; }
    }

    internal sealed class WorkloadWorkTypeOrderBaseline
    {
        internal WorkloadWorkTypeOrderKey Key { get; set; }
        internal Pawn Pawn { get; set; }
        internal WorkTypeDef WorkType { get; set; }
        internal bool IsGlobal { get; set; }
        internal WorkTabSpecificOrderBaseline State { get; set; }
    }

    internal sealed class WorkloadV2ApplyService
    {
        private static IWorkTabPriorityCapturePort PriorityState =>
            WorkTabDomainPorts.Priority;
        private static IWorkTabScheduleCapturePort ScheduleState =>
            WorkTabDomainPorts.Schedules;
        private static IWorkTabSpecificJobCapturePort SpecificJobState =>
            WorkTabDomainPorts.SpecificJobs;

        private readonly IWorkloadWorldState _component;
        private readonly Dictionary<string, WorkloadBackendDimensionBaseline> _backendBaselines =
            new Dictionary<string, WorkloadBackendDimensionBaseline>(StringComparer.Ordinal);

        internal WorkloadV2ApplyService(IWorkloadWorldState component)
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
                    WorkloadDiagnosticCode.PersistenceConflict);
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
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            WorkloadV2PersistenceEnvelope store = _component?.EnsureV2Persistence();
            if (store == null || store.IsReadOnlyDiagnostic ||
                store.PersistenceRevision != receipt.PersistenceRevision ||
                !StringComparer.Ordinal.Equals(
                    store.PersistenceFingerprint,
                    receipt.PersistenceFingerprint) ||
                store.HasDuplicateStableId(receipt.TargetStableId))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
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
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            if (!StringComparer.Ordinal.Equals(oldIdentity, newIdentity) &&
                _backendBaselines.ContainsKey(newIdentity))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (decisionKind != WorkloadDecisionKind.Update &&
                decisionKind != WorkloadDecisionKind.Fork)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.UnsupportedDecision);
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
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            targetStableId = targetStableId ?? string.Empty;
            if (decisionKind == WorkloadDecisionKind.Update)
            {
                if (!StringComparer.Ordinal.Equals(targetStableId, sourceStableId))
                {
                    return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                        WorkloadDiagnosticCode.InvalidState);
                }
            }
            else if (string.IsNullOrWhiteSpace(targetStableId) ||
                     StringComparer.Ordinal.Equals(targetStableId, sourceStableId))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (!_backendBaselines.TryGetValue(sourceIdentity, out var baseline) ||
                baseline == null ||
                !baseline.HasPersistenceBaseline ||
                baseline.PersistenceRevision < 0 ||
                string.IsNullOrWhiteSpace(baseline.PersistenceFingerprint))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            WorkloadV2PersistenceEnvelope store = _component?.EnsureV2Persistence();
            store?.RefreshDiagnostics();
            if (store == null || store.IsReadOnlyDiagnostic ||
                store.PersistenceRevision < 0)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
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
                : WorkloadOperationResult<WorkloadProjectedState>.Fail(capture.Code, capture.Context);
        }

        internal WorkloadOperationResult<WorkloadLiveBaselineCapture> CaptureLiveBaselineCapture(
            WorkloadTemplate template)
        {
            if (template == null)
            {
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                long profileStart = Stopwatch.GetTimestamp();
                RuntimeContext runtime = BuildRuntimeContext(template, report);
                long profileRuntime = Stopwatch.GetTimestamp();
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
                var livePriorities = new List<WorkloadParentPriorityEntry>();
                var liveManualModes = new List<WorkloadManualModeEntry>();
                var liveSpecificOverrides = new List<WorkloadSpecificJobOverrideEntry>();
                var liveSpecificOrder = new List<WorkloadSpecificJobOrderEntry>();
                List<WorkTypeDef> allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

                foreach (Pawn pawn in runtime.Pawns.Values)
                {
                    if (pawn == null || scope.IsExplicitlyExcluded(WorkTabEffectiveStateIds.ForPawn(pawn)) ||
                        !IsInScope(scope, pawn, runtime))
                    {
                        continue;
                    }

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
                                priority = PriorityState.ReadStored(pawn, workType);
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

                    }
                }
                long profileScope = Stopwatch.GetTimestamp();

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

                    int priority = PriorityState.ReadStored(pawn, workType);
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

                    if (SpecificJobState.TryReadLocalPriority(
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
                long profileLiveEntries = Stopwatch.GetTimestamp();

                var runtimeBaseline = new WorkloadRuntimeBaseline(
                    runtimePriorities,
                    captureManualModes,
                    liveManualMode,
                    specificJobOverrides: null,
                    specificJobOrders: null,
                    captureSpecificOverrides || captureSpecificOrder,
                    SpecificJobState.Revision,
                    captureSpecificOverrides || captureSpecificOrder
                        ? SpecificJobState.StateFingerprint
                        : null);
                long profileRuntimeBaseline = Stopwatch.GetTimestamp();
                WorkloadBackendDimensionBaseline backendBaseline =
                    CaptureBackendDimensions(
                        template,
                        templateState,
                        ownership,
                        runtime,
                        report);
                long profileBackend = Stopwatch.GetTimestamp();
                WorkloadProjectedState liveState = WithLiveBaselineValues(
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
                        : templateState.SpecificJobOrder);
                long profileState = Stopwatch.GetTimestamp();
                if (SpineTiming.Enabled)
                {
                    double tickMs = 1000d / Stopwatch.Frequency;
                    Log.Message(
                        "[BWT Profile] workload baseline ms: runtime=" +
                        ((profileRuntime - profileStart) * tickMs).ToString("F3") +
                        " scope=" + ((profileScope - profileRuntime) * tickMs).ToString("F3") +
                        " liveEntries=" + ((profileLiveEntries - profileScope) * tickMs).ToString("F3") +
                        " runtimeBaseline=" + ((profileRuntimeBaseline - profileLiveEntries) * tickMs).ToString("F3") +
                        " backend=" + ((profileBackend - profileRuntimeBaseline) * tickMs).ToString("F3") +
                        " state=" + ((profileState - profileBackend) * tickMs).ToString("F3") +
                        " counts=pawns:" + runtime.Pawns.Count +
                        ",workTypes:" + (allWorkTypes?.Count ?? 0) +
                        ",workGivers:" + runtime.WorkGivers.Count +
                        ",runtimePriorities:" + runtimePriorities.Count +
                        ",livePriorities:" + livePriorities.Count +
                        ",manualModes:" + liveManualModes.Count +
                        ",specificOverrides:" + liveSpecificOverrides.Count +
                        ",specificOrder:" + liveSpecificOrder.Count);
                }

                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Ok(
                    new WorkloadLiveBaselineCapture(
                        liveState,
                        runtimeBaseline,
                        backendBaseline));
            }
            catch (CommitAbortException exception)
            {
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Fail(
                    exception.Code);
            }
            catch (Exception exception)
            {
                Log.Error("[BWT] Workloads V2 live baseline capture failed: " + exception);
                return WorkloadOperationResult<WorkloadLiveBaselineCapture>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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

            if (SpecificJobState.ResolveWorkType(workGiver) != workType)
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
                SpecificJobState.GetDisplayWorkGivers(workType, pawn);
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
            WorkloadV2PersistenceEnvelope store = _component?.EnsureV2Persistence();
            if (store != null)
            {
                store.RefreshDiagnostics();
                baseline.PersistenceRevision = store.PersistenceRevision;
                baseline.PersistenceFingerprint = store.ComputeContentFingerprint();
                baseline.HasPersistenceBaseline = !store.IsReadOnlyDiagnostic;
            }

            baseline.SpecificJobRevision = SpecificJobState.Revision;
            baseline.ScheduleRevision = ScheduleState.Revision;
            baseline.AuthorityRevision = PriorityState.ObservationalAuthorityRevision;
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
                    baseline.SettingsWriter = WorkloadPresentationServices.CreateTransaction();
                    if (baseline.SettingsWriter == null)
                    {
                        Abort(
                            WorkloadDiagnosticCode.CaptureFailed,
                            "The workload presentation service is unavailable.");
                    }
                    if (!baseline.SettingsWriter.TryCapture(
                            settingIds,
                            out WorkloadPresentationSettingsSnapshot snapshot,
                            out string reason))
                    {
                        Abort(
                            WorkloadDiagnosticCode.CaptureFailed,
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
                WorkloadV2CommitEntryKind.Unsupported,
                "dimension." + dimension + ".legacy",
                dimension.ToString());
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
                    SpecificJobState.GetDisplayWorkGivers(workType);
                for (int j = 0; j < workGivers.Count; j++)
                {
                    WorkGiverDef workGiver = workGivers[j]?.def;
                    if (workGiver == null) continue;
                    builder.Append(WorkloadCanonical.Encode(workGiver.defName)).Append(',');
                    builder.Append(WorkloadCanonical.Encode(
                        SpecificJobState.ResolveWorkType(workGiver)?.defName));
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

                fallbackPriority = PriorityState.ReadStored(pawn, workType);
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
                fallbackPriority = SpecificJobState.ReadPriority(
                    null,
                    workGiver,
                    PriorityState.DefaultEnabledPriority);
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

                int parentPriority = PriorityState.ReadStored(pawn, workType);
                fallbackPriority = SpecificJobState.ReadPriority(
                    pawn,
                    workGiver,
                    parentPriority);
            }

            if (!WorkloadTimePriorityAdapter.TryGetTimePriorityTarget(
                    key,
                    out TimePriorityTarget target,
                    out reason))
                return false;
            return ScheduleState.TryCapture(
                target,
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

                if (!SpecificJobState.TryCapturePriority(
                        null,
                        workGiver,
                        out WorkTabSpecificPriorityBaseline globalCaptured,
                        out _,
                        out _))
                {
                    reason = "the global specific-job state is unavailable";
                    return false;
                }
                baseline = new WorkloadSpecificPriorityBaseline
                {
                    Key = key,
                    WorkType = workType,
                    WorkGiver = workGiver,
                    IsGlobal = true,
                    State = globalCaptured
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
                State = SpecificJobState.TryCapturePriority(
                    pawn,
                    localWorkGiver,
                    out WorkTabSpecificPriorityBaseline localCaptured,
                    out _,
                    out _)
                    ? localCaptured
                    : default(WorkTabSpecificPriorityBaseline)
            };
            return baseline.State.State == WorkTabSpecificPriorityState.LocalSet ||
                   baseline.State.State == WorkTabSpecificPriorityState.LocalInherit;
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

                if (!SpecificJobState.TryCaptureOrder(
                        null,
                        globalWorkType,
                        out WorkTabSpecificOrderBaseline captured,
                        out _,
                        out _))
                {
                    reason = "the global specific-job order is unavailable";
                    return false;
                }
                baseline = new WorkloadWorkTypeOrderBaseline
                {
                    Key = key,
                    WorkType = globalWorkType,
                    IsGlobal = true,
                    State = captured
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

            if (!SpecificJobState.TryCaptureOrder(
                    pawn,
                    workType,
                    out WorkTabSpecificOrderBaseline localSnapshot,
                    out _,
                    out _))
            {
                reason = "the pawn specific-job order is unavailable";
                return false;
            }
            baseline = new WorkloadWorkTypeOrderBaseline
            {
                Key = key,
                Pawn = pawn,
                WorkType = workType,
                IsGlobal = false,
                State = localSnapshot
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

            if (SpecificJobState.ResolveWorkType(workGiver) != workType)
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
                "preview.missing");
        }

        internal static WorkloadV2CommitResult GatewayFailure(
            WorkloadDecisionKind decisionKind,
            WorkloadDiagnosticCode code,
            string subject)
        {
            return Failure(decisionKind, code, subject);
        }

        private static WorkloadV2CommitResult Failure(
            WorkloadDecisionKind decisionKind,
            WorkloadDiagnosticCode code,
            string subject)
        {
            var report = new WorkloadV2CommitReport(decisionKind, string.Empty, string.Empty);
            report.Add(
                WorkloadV2CommitEntryKind.Fatal,
                subject,
                string.Empty);
            return new WorkloadV2CommitResult(
                false,
                code,
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
            WorkTabMutationAuthorization authorization,
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
            bool hadLiveChanges = live?.HasChanges == true;
            bool hadPersistenceChanges = HasPersistenceChange(persistence);
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
                    if (hadLiveChanges || hadPersistenceChanges)
                        NotifyCommitChanged(
                            live,
                            presentationChanged: live?.PresentationWasChanged == true,
                            persistenceChanged: hadPersistenceChanges);
                    if (persistenceRestored && liveRestored)
                    {
                        executionContext?.MutationAuthorization?.Lease.FinalizeLease();
                    }
                    return persistenceRestored && liveRestored;
                },
                () =>
                {
                    if (!FinalizeLiveMutation(live, out string reason))
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Fatal,
                            "confirm.application",
                            "live-state");
                        return false;
                    }
                    if (hadLiveChanges || hadPersistenceChanges)
                        NotifyCommitChanged(
                            live,
                            presentationChanged: live?.PresentationWasChanged == true,
                            persistenceChanged: hadPersistenceChanges);
                    executionContext?.MutationAuthorization?.Lease.FinalizeLease();
                    return true;
                },
                recoveryRequired);
        }

        private static bool HasPersistenceChange(PersistenceMutation persistence)
        {
            return persistence != null &&
                (persistence.WasApplied || persistence.RevisionAdvanced);
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
            bool applicationChangePublished = false;

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
                        WorkloadDiagnosticCode.UnsupportedDecision,
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
                    WorkloadValidationIssue issue = decision?.ValidationIssue;
                    WorkloadDiagnosticCode rejectionCode =
                        decision?.RejectionCode == WorkloadSessionDecisionCode.MissingForkStableId
                            ? WorkloadDiagnosticCode.MissingStableId
                            : decision?.Plan?.Validation?.IsNewerSchema == true
                                ? WorkloadDiagnosticCode.NewerSchema
                                : WorkloadDiagnosticCode.InvalidState;
                    Abort(
                        rejectionCode,
                        "SessionDecision:" +
                        (decision?.RejectionCode.ToString() ?? "MissingDecision"),
                        new WorkloadDiagnosticContext(
                            stableId: targetStableId,
                            path: issue?.Path,
                            validationCode: issue?.Code));
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
                        WorkloadDiagnosticCode.UnsupportedLegacyState,
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

                if (decisionKind != WorkloadDecisionKind.Apply &&
                    session.SessionExcludedPawnIds.Count > 0)
                {
                    for (int i = 0; i < session.SessionExcludedPawnIds.Count; i++)
                    {
                        PawnKey pawn = session.SessionExcludedPawnIds[i];
                        report.Add(
                            WorkloadV2CommitEntryKind.Excluded,
                            "scope.session-excluded",
                            pawn.Value);
                    }
                }

                store = _component.EnsureV2Persistence();
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
                    Abort(currentTemplateResult.Code, currentTemplateResult.Code.ToString());
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

                if (executionContext?.PreparedPlanFingerprint.AnyNonWhitespace() == true &&
                    !StringComparer.Ordinal.Equals(
                        Workload2Backend.BuildPreparedPlanFingerprint(
                            plan,
                            decisionKind,
                            targetStableId,
                            backendBaseline),
                        executionContext.PreparedPlanFingerprint))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The synchronized workload execute no longer matches its immutable prepare plan.");
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
                        Abort(converted.Code, converted.Code.ToString());
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
                        Abort(converted.Code, converted.Code.ToString());
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
                    ApplyLive(
                        live,
                        runtimePlan,
                        targetTemplate,
                        session,
                        report,
                        executionContext?.RetainRollbackUntilConfirmation == true);
                    report.LiveStateChanged = live.HasChanges;
                }

                if (executionContext?.ValidateOnly == true)
                {
                    report.IsSemanticNoOp = plan.Diff.IsEmpty;
                    return new WorkloadV2CommitResult(
                        true,
                        WorkloadDiagnosticCode.None,
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
                            WorkloadV2CommitEntryKind.Changed,
                            persistence.Kind == PersistenceMutationKind.Replace
                                ? "template.updated"
                                : "template.forked",
                            persistence.StableId);
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
                        Abort(receipt.Code, receipt.Code.ToString());
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

                bool provisional =
                    executionContext?.RetainRollbackUntilConfirmation == true;
                CompleteLiveMutation(live, provisional);
                bool hasRetainedChanges = (live != null && live.HasChanges) ||
                    HasPersistenceChange(persistence);
                if (provisional && hasRetainedChanges)
                {
                    executionContext.RollbackLease = CreateRollbackLease(
                        executionContext,
                        store,
                        persistence,
                        live,
                        plan,
                        report);
                }
                else if (!provisional && report.HasNetStateChange)
                {
                    applicationChangePublished = NotifyCommitChanged(
                        live,
                        presentationChanged: live?.PresentationWasChanged == true,
                        persistenceChanged: report.TemplatePersisted);
                }

                report.IsSemanticNoOp = plan.Diff.IsEmpty;
                var successResult = new WorkloadV2CommitResult(
                    true,
                    WorkloadDiagnosticCode.None,
                    report,
                    plan,
                    targetTemplate,
                    targetStableId);
                successResult.PersistenceReceipt = executionContext?.PersistenceReceipt;
                successResult.ApplicationPublication = provisional
                    ? hasRetainedChanges
                        ? WorkloadApplicationPublication.Pending
                        : WorkloadApplicationPublication.NoChange
                    : applicationChangePublished
                        ? WorkloadApplicationPublication.Applied
                        : report.HasNetStateChange
                            ? WorkloadApplicationPublication.None
                            : WorkloadApplicationPublication.NoChange;
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
                Log.Warning(
                    "[BWT] Workloads V2 commit aborted. Code=" + resultCode +
                    ". RollbackSucceeded=" + rollbackSucceeded +
                    ". Detail=" + exception.Message);
                report.Add(
                    WorkloadV2CommitEntryKind.Fatal,
                    resultCode.ToString(),
                    targetStableId);
                return new WorkloadV2CommitResult(
                    false,
                    resultCode,
                    report,
                    plan,
                    null,
                    targetStableId,
                    exception.Context);
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
                WorkloadDiagnosticCode resultCode = rollbackSucceeded
                    ? WorkloadDiagnosticCode.InvalidState
                    : WorkloadDiagnosticCode.RollbackFailed;
                Log.Error(
                    "[BWT] Workloads V2 commit failed. Code=" + resultCode +
                    ". RollbackSucceeded=" + rollbackSucceeded +
                    ". Exception=" + exception);
                report.Add(
                    WorkloadV2CommitEntryKind.Fatal,
                    resultCode.ToString(),
                    targetStableId);
                return new WorkloadV2CommitResult(
                    false,
                    resultCode,
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
                        WorkloadV2CommitEntryKind.Fatal,
                        issue.Code.ToString(),
                        issue.Path);
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
                    WorkloadDiagnosticCode.NotFound,
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
                    WorkloadDiagnosticCode.InvalidState);
            }

            if (store.HasDuplicateStableId(targetStableId))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.AmbiguousStableId);
            }

            WorkloadV2PersistenceRecord record = store.Find(targetStableId);
            if (record == null)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.UnknownWorkloadId);
            }

            WorkloadOperationResult<WorkloadTemplate> target =
                WorkloadV2RecordConverter.TryToTemplate(record);
            if (!target.Succeeded || target.Value == null)
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    target.Code,
                    target.Context);
            }

            string persistenceFingerprint = store.ComputeContentFingerprint();
            if (string.IsNullOrWhiteSpace(store.PersistenceFingerprint) ||
                !StringComparer.Ordinal.Equals(
                    store.PersistenceFingerprint,
                    persistenceFingerprint))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.PersistenceConflict);
            }

            string targetIdentity = WorkloadSession.GetSourceIdentity(target.Value);
            if (!StringComparer.Ordinal.Equals(
                    target.Value.StableId,
                    targetStableId) ||
                string.IsNullOrWhiteSpace(targetIdentity))
            {
                return WorkloadOperationResult<WorkloadPersistenceReceipt>.Fail(
                    WorkloadDiagnosticCode.InvalidState);
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
                        WorkloadV2CommitEntryKind.Excluded,
                        "scope.excluded",
                        pawnKey.Value);
                    continue;
                }

                if (!runtime.Pawns.ContainsKey(pawnKey.Value))
                {
                    report.Add(
                        WorkloadV2CommitEntryKind.MissingOrStale,
                        "scope.pawn.missing",
                        pawnKey.Value);
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
                          PriorityState.Clamp(entry.Intent.Value.Priority) !=
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

                    if (!WorkloadPresentationServices.TryGetScalarKind(entry.Key, out _))
                    {
                        Abort(
                            WorkloadDiagnosticCode.UnsupportedPresentationData,
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            entry.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            entry.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            entry.Key.ToString());
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

            if (hasSchedules && ScheduleState.Revision != baseline.ScheduleRevision)
            {
                AbortBaselineChanged(
                    report,
                    "schedule",
                    "The hourly schedule service revision changed while the workload preview was open.");
            }

            if (hasSpecificJobs)
            {
                if (SpecificJobState.Revision != baseline.SpecificJobRevision)
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
                !PriorityState.IsAuthorityCurrent(baseline.AuthorityRevision))
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
                    WorkloadV2CommitEntryKind.Unsupported,
                    "specific-job-order.permutation",
                    key.ToString());
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

                return SpecificJobState.IsCompleteGlobalOrder(
                    workType.defName,
                    orderedNames);
            }

            if (!runtime.Pawns.TryGetValue(key.Pawn.Value, out Pawn pawn) || pawn == null)
            {
                return false;
            }

            IReadOnlyList<WorkGiver> expected =
                SpecificJobState.GetDisplayWorkGivers(workType, pawn);
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
                ScheduleState.Revision != baseline.ScheduleRevision)
            {
                AbortBaselineChanged(
                    report,
                    subject,
                    "The hourly schedule service revision changed after preview capture.");
            }

            if ((baseline.SpecificPriorities.Count > 0 || baseline.WorkTypeOrders.Count > 0) &&
                SpecificJobState.Revision != baseline.SpecificJobRevision)
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
                !PriorityState.IsAuthorityCurrent(baseline.AuthorityRevision))
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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
                WorkloadV2CommitEntryKind.Unsupported,
                "specific-job.clear.requires-tombstone",
                key?.ToString() ?? string.Empty);
            Abort(WorkloadDiagnosticCode.UnsupportedClear, message);
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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
                        ? PriorityState.Clamp(difference.After)
                        : PriorityState.DisabledPriority;
                    int current = PriorityState.ReadStored(pawn, workType);
                    if (current == desired)
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Unchanged,
                            "parent-priority.unchanged",
                            difference.Key.ToString());
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
                        WorkloadV2CommitEntryKind.Unsupported,
                        "manual-mode.pawn-scoped",
                        scope.Mode.ToString());
                    Abort(WorkloadDiagnosticCode.UnsupportedRuntimeState, message);
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Skipped,
                            "manual-mode.removed",
                            difference.Key.ToString());
                        continue;
                    }

                    result.ManualKeys.Add(difference.Key);

                    if (hasManualTarget && manualTarget != difference.After)
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Unsupported,
                            "manual-mode.conflict",
                            difference.Key.ToString());
                        Abort(
                            WorkloadDiagnosticCode.UnsupportedRuntimeState,
                            "Conflicting V2 manual-mode entries cannot be represented by the global priority authority.");
                    }

                    hasManualTarget = true;
                    manualTarget = difference.After;
                    if (currentManual == difference.After)
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Unchanged,
                            "manual-mode.unchanged",
                            difference.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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

                    int desired = PriorityState.DisabledPriority;
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
                        if (PriorityState.Clamp(desired) != desired)
                        {
                            Abort(
                                WorkloadDiagnosticCode.InvalidState,
                                "The specific-job priority for " + difference.Key +
                                " is outside the current BWT priority range.");
                        }
                    }

                    bool hasCurrent = SpecificJobState.TryReadLocalPriority(
                        pawn,
                        workGiver,
                        out int current);
                    if (difference.HasAfter && hasCurrent && current == desired)
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Unchanged,
                            "specific-job-priority.unchanged",
                            difference.Key.ToString());
                        continue;
                    }

                    if (!difference.HasAfter && !hasCurrent)
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Unchanged,
                            "specific-job-priority.unchanged",
                            difference.Key.ToString());
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
                            WorkloadV2CommitEntryKind.Excluded,
                            "apply.session-excluded",
                            difference.Key.ToString());
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
                    WorkloadV2CommitEntryKind.Unsupported,
                    "apply.multiplayer-acknowledgement",
                    "live-state");
                Abort(WorkloadDiagnosticCode.MultiplayerUnavailable, message);
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
                    WorkloadV2CommitEntryKind.Excluded,
                    "scope.excluded",
                    subject);
                return false;
            }

            if (!runtime.Pawns.TryGetValue(pawnKey.Value, out pawn))
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitEntryKind.MissingOrStale,
                    "pawn.missing",
                    subject);
                return false;
            }

            if (pawn.Dead)
            {
                report.Add(
                    WorkloadV2CommitEntryKind.Skipped,
                    "pawn.dead",
                    subject);
                return false;
            }

            if (!IsInScope(scope, pawn, runtime))
            {
                report.Add(
                    WorkloadV2CommitEntryKind.Skipped,
                    "scope.outside",
                    subject);
                return false;
            }

            if (!runtime.WorkTypes.TryGetValue(workTypeKey.Value, out workType))
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitEntryKind.MissingOrStale,
                    "work-type.missing",
                    subject);
                return false;
            }

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                report.Add(
                    WorkloadV2CommitEntryKind.Skipped,
                    "pawn.work-settings",
                    subject);
                return false;
            }

            if (pawn.WorkTypeIsDisabled(workType))
            {
                report.Add(
                    WorkloadV2CommitEntryKind.Skipped,
                    "work-type.disabled",
                    subject);
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
                    WorkloadV2CommitEntryKind.MissingOrStale,
                    "specific-job.invalid",
                    subject);
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
                    WorkloadV2CommitEntryKind.MissingOrStale,
                    "work-giver.missing",
                    subject);
                return false;
            }

            if (SpecificJobState.ResolveWorkType(workGiver) != workType)
            {
                missingOrStale = true;
                report.Add(
                    WorkloadV2CommitEntryKind.MissingOrStale,
                    "work-giver.mapping.changed",
                    subject);
                return false;
            }

            if (!TryGetEffectiveWorkGiverOrder(
                    pawn,
                    workType,
                    workGiver.defName,
                    out int unusedOrder))
            {
                report.Add(
                    WorkloadV2CommitEntryKind.Skipped,
                    "work-giver.unavailable",
                    subject);
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
                            ? WorkloadV2CommitEntryKind.Unchanged
                            : WorkloadV2CommitEntryKind.Unchanged,
                        "schedule.unchanged",
                        entry.Key.ToString());
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

                WorkTabSpecificPriorityState desiredGlobalState = previous.IsGlobal
                    ? entry.Intent.IsClear
                        ? WorkTabSpecificPriorityState.GlobalClear
                        : WorkTabSpecificPriorityState.GlobalSet
                    : entry.Intent.IsClear
                        ? WorkTabSpecificPriorityState.LocalInherit
                        : WorkTabSpecificPriorityState.LocalSet;
                int desiredPriority = entry.Intent.HasValue
                    ? entry.Intent.Value.Priority
                    : PriorityState.DisabledPriority;
                bool unchanged;
                if (previous.IsGlobal)
                {
                    if (!SpecificJobState.TryCapturePriority(
                            null,
                            previous.WorkGiver,
                            out WorkTabSpecificPriorityBaseline current,
                            out _,
                            out _))
                        Abort(WorkloadDiagnosticCode.InvalidState,
                            "The global specific-job priority baseline is unavailable.");
                    unchanged = current.State == desiredGlobalState &&
                        (desiredGlobalState != WorkTabSpecificPriorityState.GlobalSet ||
                         current.Priority == desiredPriority);
                }
                else
                {
                    bool hasCurrent = SpecificJobState.TryReadLocalPriority(
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
                        WorkloadV2CommitEntryKind.Unchanged,
                        "specific-job-priority.unchanged",
                        entry.Key.ToString());
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
                    if (!SpecificJobState.TryCaptureOrder(
                            null,
                            previous.WorkType,
                            out WorkTabSpecificOrderBaseline current,
                            out _,
                            out _))
                        Abort(WorkloadDiagnosticCode.InvalidState,
                            "The global specific-job order baseline is unavailable.");
                    WorkTabSpecificOrderState desiredState =
                        entry.Intent.IsClear
                            ? WorkTabSpecificOrderState.GlobalClear
                            : WorkTabSpecificOrderState.GlobalSet;
                    unchanged = current.State == desiredState &&
                        (desiredState != WorkTabSpecificOrderState.GlobalSet ||
                         SequenceEqual(current.OrderedWorkGiverNames, desiredOrder));
                }
                else
                {
                    if (!SpecificJobState.TryCaptureOrder(
                            previous.Pawn,
                            previous.WorkType,
                            out WorkTabSpecificOrderBaseline current,
                            out _,
                            out _))
                        Abort(WorkloadDiagnosticCode.InvalidState,
                            "The pawn specific-job order baseline is unavailable.");
                    unchanged = entry.Intent.IsClear
                        ? current.State == WorkTabSpecificOrderState.LocalInherit
                        : current.State == WorkTabSpecificOrderState.LocalStored &&
                          SequenceEqual(current.OrderedWorkGiverNames, desiredOrder);
                }

                if (unchanged)
                {
                    report.Add(
                        WorkloadV2CommitEntryKind.Unchanged,
                        "specific-job-order.unchanged",
                        entry.Key.ToString());
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
                    WorkloadV2CommitEntryKind.Cleared,
                    "presentation.clear",
                    settingId);
                return true;
            }

            WorkloadScalarKind scalarKind;
            if (!intent.HasValue ||
                intent.Value.Ownership != WorkloadSettingOwnership.WorkloadOwned ||
                !WorkloadPresentationServices.TryGetScalarKind(settingId, out scalarKind) ||
                intent.Value.Kind != scalarKind)
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
                if (parent.DesiredPriority > PriorityState.DisabledPriority ||
                    SpecificJobState.RetainsOverridesForDisabledParent)
                {
                    continue;
                }

                string message =
                    "Specific-job state for " + parent.Key +
                    " cannot be committed while its parent priority is disabled unless BWT's locked-specific override mode is active.";
                report.Add(
                    WorkloadV2CommitEntryKind.Unsupported,
                    "specific-job.parent-disabled",
                    parent.Key.ToString());
                Abort(WorkloadDiagnosticCode.UnsupportedRuntimeState, message);
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

            return PriorityState.ReadStored(pawn, workType);
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
            if (!SpecificJobState.TryCaptureOrder(
                    pawn,
                    workType,
                    out WorkTabSpecificOrderBaseline snapshot,
                    out _,
                    out _))
                Abort(WorkloadDiagnosticCode.InvalidState,
                    "The pawn specific-job order baseline is unavailable.");
            var baseline = new WorkTabSpecificOrderBaseline(
                snapshot.State,
                snapshot.OrderedWorkGiverNames);
            IReadOnlyList<WorkGiver> currentWorkGivers =
                SpecificJobState.GetDisplayWorkGivers(workType, pawn);
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
                if (snapshot.State == WorkTabSpecificOrderState.LocalInherit)
                {
                    report.Add(
                        WorkloadV2CommitEntryKind.Unchanged,
                        "specific-job-order.unchanged",
                        parent.ToString());
                    return null;
                }

                return new SpecificJobOrderMutation(
                    parent,
                    pawn,
                    workType,
                    null,
                    baseline);
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

            if (snapshot.State == WorkTabSpecificOrderState.LocalStored &&
                SequenceEqual(snapshot.OrderedWorkGiverNames, desired))
            {
                report.Add(
                    WorkloadV2CommitEntryKind.Unchanged,
                    "specific-job-order.unchanged",
                    parent.ToString());
                return null;
            }

            return new SpecificJobOrderMutation(
                parent,
                pawn,
                workType,
                desired,
                baseline);
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
                if (!PriorityState.TryCaptureAuthority(out long revision))
                {
                    EnsureBetterWorkTabAuthority(report);
                    return;
                }

                plan.AuthorityRevision = revision;
                plan.HasAuthorityRevision = true;
            }

            if (PriorityState.IsAuthorityCurrent(plan.AuthorityRevision))
            {
                return;
            }

            string message =
                "The V2 live mutation was blocked because priority authority changed during " +
                (string.IsNullOrEmpty(subject) ? "planning" : subject) + "; no external handoff was attempted.";
            report.Add(
                WorkloadV2CommitEntryKind.Fatal,
                "priority-authority.changed",
                subject ?? string.Empty);
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

            if (SpecificJobState.Revision == plan.SpecificJobRevision)
            {
                return;
            }

            string message =
                "The V2 specific-job mutation was blocked because BWT work-giver state changed during " +
                (string.IsNullOrEmpty(subject) ? "planning" : subject) + ".";
            report.Add(
                WorkloadV2CommitEntryKind.Fatal,
                "specific-job.revision.changed",
                subject ?? string.Empty);
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

                    int current = PriorityState.ReadStored(pawn, workType);
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

            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
            {
                EnsureSpecificRuntimeBaselineCurrent(
                    session,
                    report,
                    subject + ".specific-job-state");
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

            int current = PriorityState.ReadStored(mutation.Pawn, mutation.WorkType);
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
            EnsureSpecificRuntimeBaselineCurrent(session, report, mutation.Key.ToString());
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
            EnsureSpecificRuntimeBaselineCurrent(session, report, mutation.Parent.ToString());
        }

        private static void EnsureSpecificRuntimeBaselineCurrent(
            WorkloadSession session,
            WorkloadV2CommitReport report,
            string subject)
        {
            WorkloadRuntimeBaseline baseline = session?.RuntimeBaseline;
            if (baseline == null ||
                !baseline.HasSpecificJobRevision ||
                !baseline.HasSpecificJobFingerprint)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The captured BWT specific-job state fingerprint is unavailable.");
            }

            if (baseline.SpecificJobRevision ==
                    SpecificJobState.Revision &&
                StringComparer.Ordinal.Equals(
                    baseline.SpecificJobFingerprint,
                    SpecificJobState.StateFingerprint))
            {
                return;
            }

            AbortBaselineChanged(
                report,
                subject,
                "The BWT specific-job state changed after preview capture.");
        }

        private static void AbortBaselineChanged(
            WorkloadV2CommitReport report,
            string subject,
            string message)
        {
            report.Add(
                WorkloadV2CommitEntryKind.Fatal,
                "runtime-baseline.changed",
                subject ?? string.Empty);
            Abort(WorkloadDiagnosticCode.InvalidState, message);
        }

        private static void EnsureBetterWorkTabAuthority(WorkloadV2CommitReport report)
        {
            try
            {
                if (PriorityState.TryCaptureAuthority(out _))
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
                    WorkloadV2CommitEntryKind.Unsupported,
                    "priority-authority",
                    authority);
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
            WorkloadV2CommitReport report,
            bool provisional)
        {
            if (transaction == null || runtimePlan == null || targetTemplate == null)
            {
                Abort(
                    WorkloadDiagnosticCode.InvalidState,
                    "The V2 live mutation transaction is unavailable.");
            }

            WorkloadScope scope = targetTemplate.Definition.Scope ?? WorkloadScope.Empty;
            RuntimeContext runtime = BuildRuntimeContext(targetTemplate, report);
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

                var staged = new WorkTabStagedMutation
                {
                    AuthorityRevision = runtimePlan.AuthorityRevision,
                    SpecificJobRevision = runtimePlan.SpecificJobRevision,
                    SynchronizedReplay = false,
                    Authorization = runtimePlan.MutationAuthorization?.Lease
                };
                if (!CompileSpecificJobBatch(
                        staged,
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
                            staged.ExpectedManualPriorityMode = previousManualMode;
                            staged.ManualPriorityTarget = runtimePlan.ManualTarget;
                            report.Add(
                                WorkloadV2CommitEntryKind.Changed,
                                "manual-mode.changed",
                                "global");
                        }
                        else
                        {
                            report.Add(
                                WorkloadV2CommitEntryKind.Unchanged,
                                "manual-mode.unchanged",
                                "global");
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
                    int previous = PriorityState.ReadStored(pawn, workType);
                    if (previous == mutation.DesiredPriority)
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Unchanged,
                            "parent-priority.unchanged",
                            mutation.Key.ToString());
                        continue;
                    }

                    staged.ParentPriorities.Add(new WorkTabStagedParentPriority(
                        pawn,
                        workType,
                        previous,
                        mutation.DesiredPriority));

                    report.Add(
                        WorkloadV2CommitEntryKind.Changed,
                        "parent-priority.changed",
                        mutation.Key.ToString());
                }

                CompileSchedules(
                    staged,
                    runtimePlan,
                    report);

                WorkTabApplication application = WorkTabApplication.Current;
                string stageReason = null;
                bool stageSucceeded = !staged.HasRequests;
                WorkTabStagedMutationReceipt receipt = staged.HasRequests
                    ? application?.StageMutation(
                        staged,
                        out stageSucceeded,
                        out stageReason)
                    : null;
                transaction.StagedMutation = receipt;
                if (staged.HasRequests &&
                    (receipt == null || !stageSucceeded))
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The Work-tab application rejected the staged workload mutation: " +
                        stageReason);
                }
                if (receipt != null && runtimePlan.RequiresSpecificJobRevision)
                    runtimePlan.SpecificJobRevision = receipt.SpecificJobRevision;

                ApplyPresentationLive(
                    transaction,
                    runtimePlan,
                    report,
                    provisional);
                if (transaction.PresentationWasChanged)
                {
                    receipt?.IncludePublicationDimensions(
                        WorkTabApplicationDimensions.Presentation);
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

        private static void CompleteLiveMutation(
            LiveMutationTransaction transaction,
            bool provisional)
        {
            WorkTabStagedMutationReceipt staged = transaction?.StagedMutation;
            if (staged == null)
            {
                if (provisional && transaction?.PresentationWasChanged == true)
                {
                    WorkTabApplication.Current?.InvalidateProvisionalMutation(
                        WorkTabApplicationDimensions.Presentation);
                }
                return;
            }

            if (!staged.Commit(
                    out WorkTabApplicationChange change,
                    out string reason,
                    provisional))
                throw new InvalidOperationException(
                    "The staged Work-tab transaction could not commit: " + reason);
            transaction.StagedChange = change;
        }

        private static bool FinalizeLiveMutation(
            LiveMutationTransaction transaction,
            out string reason)
        {
            reason = null;
            if (transaction?.PresentationWasChanged == true &&
                !transaction.PresentationPersisted)
            {
                WorkloadPresentationSettingsMutationReceipt presentation =
                    transaction.PresentationWriter?.TryPersistOwned(
                        transaction.PresentationSnapshot);
                if (presentation == null || !presentation.Succeeded)
                {
                    reason = presentation?.FailureReason ??
                        "The provisional presentation writer is unavailable.";
                    return false;
                }
                transaction.PresentationPersisted = true;
            }
            WorkTabStagedMutationReceipt staged = transaction?.StagedMutation;
            if (staged == null)
                return true;
            if (!staged.FinalizeProvisionalCommit(
                    out WorkTabApplicationChange change,
                    out reason))
            {
                return false;
            }
            transaction.StagedChange = change;
            return true;
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
            WorkTabMutationAuthorization authorization =
                executionContext?.MutationAuthorization;
            if (authorization == null || !authorization.IsUsable ||
                requestIdentity == null || session == null || targetTemplate == null)
            {
                report?.Add(
                    WorkloadV2CommitEntryKind.Fatal,
                    "transaction-capability.missing",
                    targetStableId);
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
                    WorkloadV2CommitEntryKind.Fatal,
                    "transaction-capability.fingerprint",
                    targetStableId);
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

        private static bool CompileSpecificJobBatch(
            WorkTabStagedMutation staged,
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

            var priorities = new List<WorkTabStagedSpecificPriority>();
            var orders = new List<WorkTabStagedSpecificOrder>();

            for (int i = 0; i < plan.SpecificJobOverrides.Count; i++)
            {
                SpecificJobOverrideMutation mutation = plan.SpecificJobOverrides[i];
                RevalidateSpecificOverrideBaseline(
                    session,
                    plan,
                    mutation,
                    report,
                    mutation.Key.ToString());
                bool hasPrevious = SpecificJobState.TryReadLocalPriority(
                    mutation.Pawn,
                    mutation.WorkGiver,
                    out int previousPriority);
                priorities.Add(
                    new WorkTabStagedSpecificPriority(
                        mutation.Pawn.thingIDNumber,
                        mutation.WorkGiver.defName,
                        mutation.HasAfter
                            ? WorkTabSpecificPriorityState.LocalSet
                            : WorkTabSpecificPriorityState.LocalInherit,
                        mutation.DesiredPriority,
                        new WorkTabSpecificPriorityBaseline(
                            hasPrevious
                                ? WorkTabSpecificPriorityState.LocalSet
                                : WorkTabSpecificPriorityState.LocalInherit,
                            previousPriority)));
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
                    new WorkTabStagedSpecificOrder(
                        mutation.Pawn.thingIDNumber,
                        mutation.WorkType.defName,
                        mutation.DesiredOrder == null
                            ? WorkTabSpecificOrderState.LocalInherit
                            : WorkTabSpecificOrderState.LocalStored,
                        mutation.DesiredOrder,
                        mutation.PreviousSnapshot));
            }

            for (int i = 0;
                 i < plan.TypedSpecificPriorities.Count;
                 i++)
            {
                TypedSpecificPriorityMutation mutation = plan.TypedSpecificPriorities[i];
                WorkloadSpecificPriorityBaseline previous = mutation.Previous;
                if (previous == null || previous.WorkGiver == null)
                {
                    return false;
                }

                priorities.Add(
                    new WorkTabStagedSpecificPriority(
                        previous.IsGlobal ? -1 : previous.Pawn.thingIDNumber,
                        previous.WorkGiver.defName,
                        previous.IsGlobal
                            ? mutation.DesiredGlobalState
                            : mutation.DesiredGlobalState == WorkTabSpecificPriorityState.GlobalSet
                                ? WorkTabSpecificPriorityState.LocalSet
                                : WorkTabSpecificPriorityState.LocalInherit,
                        mutation.DesiredPriority,
                        previous.State));
            }

            for (int i = 0;
                 i < plan.TypedWorkTypeOrders.Count;
                 i++)
            {
                TypedWorkTypeOrderMutation mutation = plan.TypedWorkTypeOrders[i];
                WorkloadWorkTypeOrderBaseline previous = mutation.Previous;
                if (previous == null || previous.WorkType == null)
                {
                    return false;
                }

                orders.Add(
                    new WorkTabStagedSpecificOrder(
                        previous.IsGlobal ? -1 : previous.Pawn.thingIDNumber,
                        previous.WorkType.defName,
                        previous.IsGlobal
                            ? mutation.IsClear
                                ? WorkTabSpecificOrderState.GlobalClear
                                : WorkTabSpecificOrderState.GlobalSet
                            : mutation.IsClear
                                ? WorkTabSpecificOrderState.LocalInherit
                                : WorkTabSpecificOrderState.LocalStored,
                        mutation.DesiredOrder,
                        previous.State));
            }

            if (staged == null) return false;
            staged.SpecificPriorities.AddRange(priorities);
            staged.SpecificOrders.AddRange(orders);

            for (int i = 0; i < priorities.Count; i++)
            {
                WorkTabStagedSpecificPriority entry = priorities[i];
                report.Add(
                    entry.DesiredState == WorkTabSpecificPriorityState.GlobalClear ||
                    entry.DesiredState == WorkTabSpecificPriorityState.LocalInherit
                        ? WorkloadV2CommitEntryKind.Cleared
                        : WorkloadV2CommitEntryKind.Changed,
                    entry.DesiredState == WorkTabSpecificPriorityState.GlobalClear ||
                    entry.DesiredState == WorkTabSpecificPriorityState.LocalInherit
                        ? "specific-job-priority.cleared"
                        : "specific-job-priority.changed",
                    entry.CanonicalKey);
            }

            for (int i = 0; i < orders.Count; i++)
            {
                WorkTabStagedSpecificOrder entry = orders[i];
                report.Add(
                    entry.DesiredState == WorkTabSpecificOrderState.GlobalClear ||
                    entry.DesiredState == WorkTabSpecificOrderState.LocalInherit
                        ? WorkloadV2CommitEntryKind.Cleared
                        : WorkloadV2CommitEntryKind.Changed,
                    entry.DesiredState == WorkTabSpecificOrderState.GlobalClear ||
                    entry.DesiredState == WorkTabSpecificOrderState.LocalInherit
                        ? "specific-job-order.cleared"
                        : "specific-job-order.changed",
                    entry.CanonicalKey);
            }

            return true;
        }

        private static void CompileSchedules(
            WorkTabStagedMutation staged,
            RuntimeCommitPlan plan,
            WorkloadV2CommitReport report)
        {
            if (plan == null || plan.BackendBaseline == null) return;

            for (int i = 0; i < plan.Schedules.Count; i++)
            {
                ScheduleMutation mutation = plan.Schedules[i];
                EnsureRuntimeMutationAuthority(plan, report, mutation.Key.ToString() + ".authority");
                string reason = null;
                TimePriorityScheduleValue desired = null;
                bool compiled = mutation.Intent.IsClear ||
                    WorkloadTimePriorityAdapter.TryGetScheduleValue(
                        mutation.Intent.Value,
                        out desired,
                        out reason);
                if (!compiled || staged == null)
                {
                    Abort(
                        WorkloadDiagnosticCode.InvalidState,
                        "The canonical hourly schedule could not be staged for " +
                        mutation.Key + ": " + reason);
                }
                bool expectedHasSchedule = mutation.Intent.HasValue &&
                    mutation.Intent.Value != null &&
                    mutation.Intent.Value.PinnedHourMask != 0;
                staged.Schedules.Add(new WorkTabStagedSchedule(
                    mutation.PreviousSnapshot,
                    desired));
                report.Add(
                    mutation.Intent.IsClear || !expectedHasSchedule
                        ? WorkloadV2CommitEntryKind.Cleared
                        : WorkloadV2CommitEntryKind.Changed,
                    mutation.Intent.IsClear || !expectedHasSchedule
                        ? "schedule.cleared"
                        : "schedule.changed",
                    mutation.Key.ToString());
            }
        }

        private static void ApplyPresentationLive(
            LiveMutationTransaction transaction,
            RuntimeCommitPlan plan,
            WorkloadV2CommitReport report,
            bool provisional)
        {
            if (plan == null || plan.BackendBaseline == null) return;
            WorkloadBackendDimensionBaseline baseline = plan.BackendBaseline;
            if (plan.PresentationValues.Count > 0)
            {
                if (MultiplayerBridge.Active &&
                    (plan.MutationAuthorization == null ||
                     !plan.MutationAuthorization.Lease.IsAcceptedForSettings(
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
                            persist: !provisional);
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
                transaction.PresentationPersisted = presentationReceipt.Persisted;
                foreach (string settingId in presentationReceipt.ChangedSettingIds)
                {
                    report.Add(
                        WorkloadV2CommitEntryKind.Changed,
                        "presentation.changed",
                        settingId);
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
                    WorkloadV2CommitEntryKind.Fatal,
                    "persistence.cas",
                    subject);
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

        private bool NotifyCommitChanged(
            LiveMutationTransaction live,
            bool presentationChanged,
            bool persistenceChanged)
        {
            if (live?.StagedMutation != null)
            {
                bool changed = !live.StagedChange.IsEmpty;
                if (changed)
                {
                    _component.NotifyV2Changed();
                    return true;
                }
            }

            // Presentation-only commits have no staged receipt, so publish
            // them here instead of making the lifecycle caller recache tables.
            if (!presentationChanged && !persistenceChanged)
                return false;

            _component.NotifyV2Changed();

            WorkTabApplication application = WorkTabApplication.Current;
            return application != null && application.PublishAtomicMutation(
                WorkTabApplicationDimensions.Presentation,
                durable: true,
                broadScope: true,
                mirrorExternal: true,
                // A persistence-only workload commit already invalidates the
                // presentation. It does not alter pawn rows, so do not make
                // the application publisher recache every pawn table.
                notifyPawnTables: false).Changed;
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
                    WorkloadV2CommitEntryKind.Fatal,
                    "rollback.persistence",
                    mutation.StableId);
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
                        WorkloadV2CommitEntryKind.Fatal,
                        "rollback.persistence.conflict",
                        mutation.StableId);
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
                            WorkloadV2CommitEntryKind.Fatal,
                            "rollback.persistence.conflict",
                            mutation.StableId);
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
                        WorkloadV2CommitEntryKind.Fatal,
                        "rollback.persistence.verification",
                        mutation.StableId);
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
                Log.Error("[BWT] Workloads V2 persistence rollback failed: " + exception);
                report.Add(
                    WorkloadV2CommitEntryKind.Fatal,
                    "rollback.persistence.failed",
                    mutation.StableId);
                return false;
            }
        }

        private static bool RollbackLive(
            LiveMutationTransaction transaction,
            WorkloadV2CommitReport report)
        {
            if (transaction == null || !transaction.HasChanges) return true;
            if (transaction.RollbackAttempted && !HasLiveNetChanges(transaction)) return true;

            transaction.RollbackAttempted = true;
            bool restored = true;
            try
            {
                if (transaction.PresentationWasChanged)
                {
                    bool settingsCapabilityValid = !MultiplayerBridge.Active ||
                        (transaction.WorkloadAuthorization != null &&
                         transaction.WorkloadAuthorization.Lease.IsAcceptedForSettings(
                             synchronizedExecution: true,
                             transaction.AuthorityRevision,
                             transaction.WorkloadAuthorization.SettingsRevision));
                    WorkloadPresentationSettingsMutationReceipt rollbackReceipt =
                        settingsCapabilityValid
                            ? transaction.PresentationWriter?.TryRollback(
                                transaction.PresentationSnapshot,
                                persist: transaction.PresentationPersisted)
                            : null;
                    if (rollbackReceipt == null || !rollbackReceipt.Succeeded)
                    {
                        report.Add(
                            WorkloadV2CommitEntryKind.Fatal,
                            "rollback.presentation",
                            "presentation");
                        restored = false;
                    }
                    else
                    {
                        transaction.PresentationWasChanged = false;
                        transaction.PresentationPersisted = false;
                        WorkTabApplication.Current?.InvalidateProvisionalMutation(
                            WorkTabApplicationDimensions.Presentation);
                    }
                }
                if (transaction.StagedMutation != null &&
                    !transaction.StagedMutation.Rollback(out string stagedReason))
                {
                    report.Add(
                        WorkloadV2CommitEntryKind.Fatal,
                        "rollback.application",
                        "live-state");
                    restored = false;
                }
            }
            catch (Exception exception)
            {
                Log.Error("[BWT] Workloads V2 live-state rollback failed: " + exception);
                report.Add(
                    WorkloadV2CommitEntryKind.Fatal,
                    "rollback.failed",
                    "live-state");
                restored = false;
            }
            return restored && !HasLiveNetChanges(transaction);
        }

        private static bool HasLiveNetChanges(LiveMutationTransaction transaction)
        {
            if (transaction == null || !transaction.HasChanges) return false;
            try
            {
                if (transaction.StagedMutation?.HasNetChanges == true)
                    return true;

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
            string message,
            WorkloadDiagnosticContext context = null)
        {
            throw new CommitAbortException(code, message, context);
        }

        private sealed class CommitAbortException : Exception
        {
            internal CommitAbortException(
                WorkloadDiagnosticCode code,
                string message,
                WorkloadDiagnosticContext context)
                : base(message)
            {
                Code = code;
                Context = context ?? WorkloadDiagnosticContext.Empty;
            }

            internal WorkloadDiagnosticCode Code { get; private set; }
            internal WorkloadDiagnosticContext Context { get; private set; }
        }

        private sealed class RuntimeContext
        {
            private readonly HashSet<Pawn> _currentMapFreeColonists;

            internal RuntimeContext(
                Dictionary<string, Pawn> pawns,
                Dictionary<string, WorkTypeDef> workTypes,
                Dictionary<string, WorkGiverDef> workGivers)
            {
                Pawns = pawns;
                WorkTypes = workTypes;
                WorkGivers = workGivers;
                _currentMapFreeColonists = new HashSet<Pawn>();
                List<Pawn> freeColonists = Verse.Find.CurrentMap?.mapPawns?.FreeColonists;
                for (int i = 0; freeColonists != null && i < freeColonists.Count; i++)
                {
                    Pawn pawn = freeColonists[i];
                    if (pawn != null) _currentMapFreeColonists.Add(pawn);
                }
            }

            internal Dictionary<string, Pawn> Pawns { get; private set; }
            internal Dictionary<string, WorkTypeDef> WorkTypes { get; private set; }
            internal Dictionary<string, WorkGiverDef> WorkGivers { get; private set; }

            internal bool IsCurrentMapFreeColonist(Pawn pawn)
            {
                return pawn != null && _currentMapFreeColonists.Contains(pawn);
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
            internal WorkTabMutationAuthorization MutationAuthorization;
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
                WorkTabSpecificPriorityState desiredGlobalState,
                int desiredPriority)
            {
                Key = key;
                Previous = previous;
                DesiredGlobalState = desiredGlobalState;
                DesiredPriority = desiredPriority;
            }

            internal WorkloadSpecificJobTargetKey Key { get; private set; }
            internal WorkloadSpecificPriorityBaseline Previous { get; private set; }
            internal WorkTabSpecificPriorityState DesiredGlobalState { get; private set; }
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

        private sealed class SpecificJobOrderMutation
        {
            internal SpecificJobOrderMutation(
                WorkloadParentPriorityKey parent,
                Pawn pawn,
                WorkTypeDef workType,
                IReadOnlyList<string> desiredOrder,
                WorkTabSpecificOrderBaseline previousSnapshot)
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
            internal WorkTabSpecificOrderBaseline PreviousSnapshot { get; private set; }
        }

        private sealed class LiveMutationTransaction
        {
            internal LiveMutationTransaction(RuntimeCommitPlan plan)
            {
                AuthorityRevision = plan?.AuthorityRevision ?? 0L;
                HasAuthorityRevision = plan?.HasAuthorityRevision == true;
            }

            internal long AuthorityRevision;
            internal bool HasAuthorityRevision;
            internal bool RollbackAttempted;
            internal bool PresentationWasChanged;
            internal bool PresentationPersisted;
            internal WorkTabMutationAuthorization WorkloadAuthorization;
            internal WorkTabStagedMutationReceipt StagedMutation;
            internal WorkTabApplicationChange StagedChange;
            internal WorkloadPresentationSettingsTransaction PresentationWriter;
            internal WorkloadPresentationSettingsSnapshot PresentationSnapshot;
            internal bool HasChanges =>
                StagedMutation?.HasNetChanges == true || PresentationWasChanged;
        }

        internal sealed class WorkloadCommitExecutionContext
        {
            internal bool ValidateOnly;
            internal bool MultiplayerAuthorized;
            internal bool RetainRollbackUntilConfirmation;
            internal Func<WorkloadPersistenceReceipt, bool> PersistenceRebase;
            internal WorkTabMutationAuthorization MutationAuthorization;
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
                if (_state == LeaseState.Confirmed)
                {
                    return true;
                }

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
        private static IWorkTabPriorityCapturePort PriorityState =>
            WorkTabDomainPorts.Priority;
        private static IWorkTabScheduleCapturePort ScheduleState =>
            WorkTabDomainPorts.Schedules;
        private static IWorkTabSpecificJobCapturePort SpecificJobState =>
            WorkTabDomainPorts.SpecificJobs;
        private static readonly Func<WorkloadScheduleTargetKey, int, WorkloadSchedulePayload> LiveScheduleCapture =
            TryCaptureLiveSchedule;

        internal static WorkloadOperationResult<WorkloadTemplate> CaptureCurrentTemplate(
            string stableId,
            string label)
        {
            WorkloadOperationResult preflight = WorkloadLiveCapturePolicy.ValidateTemplateCapture(
                stableId,
                Verse.Find.CurrentMap != null,
                delegate { return PriorityState.TryCaptureAuthority(out _); },
                Verse.Find.PlaySettings != null);
            if (!preflight.Succeeded)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    preflight.Code,
                    preflight.Context);
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
                    WorkloadDiagnosticCode.CaptureFailed);
            }
        }

        internal static bool CapturePawn(
            WorkloadDraft draft,
            Pawn pawn,
            WorkloadOwnershipDimensions ownership,
            bool templateCapture,
            ref bool capturedSchedule)
        {
            if (draft == null || pawn?.workSettings == null ||
                (!templateCapture && !pawn.workSettings.EverWork))
            {
                return false;
            }

            bool capturesSchedules = templateCapture ||
                ownership.Owns(WorkloadStateDimension.Schedules);
            bool ownsSpecificPriority =
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides);
            bool ownsSpecificOrder =
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);
            bool ownsManualMode =
                ownership.Owns(WorkloadStateDimension.ManualModes);
            bool liveManualMode = ownsManualMode &&
                ParentPriorityRead.GetLiveManualMode(true);
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
                        ? PriorityState.ReadStored(pawn, workType)
                        : parentFallback;
                    int effectivePriority = parentFallback;
                    int priority = WorkloadLiveCapturePolicy.SelectParentPriority(
                        templateCapture,
                        storedPriority,
                        effectivePriority);
                    draft.SetParentPriority(parentKey, priority);
                    wroteValue = true;
                }

                if (ownsManualMode)
                {
                    draft.SetManualMode(parentKey, liveManualMode);
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
                    SpecificJobState.GetDisplayWorkGivers(
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
                        SpecificJobState.ReadPriority(
                            pawn,
                            workGiver,
                            parentFallback);
                    if (ownsSpecificPriority)
                    {
                        if (templateCapture)
                        {
                            if (SpecificJobState.TryReadLocalPriority(
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
                                WorkloadScalarValue.FromInteger(inheritedPriority));
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

                    if (!templateCapture && ownsSpecificOrder)
                    {
                        draft.SetSpecificJobOrder(specificKey, workGiverIndex);
                        wroteValue = true;
                    }
                }

                if (templateCapture && ownsSpecificOrder &&
                    SpecificJobState.HasLocalOrder(pawn, workType))
                {
                    if (SpecificJobState.TryCaptureOrder(
                            pawn,
                            workType,
                            out WorkTabSpecificOrderBaseline snapshot,
                            out _,
                            out _) &&
                        WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                            draft,
                            WorkTabEffectiveStateIds.ForWorkTypeOrder(pawn, workType),
                            snapshot.State == WorkTabSpecificOrderState.LocalStored,
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
                    SpecificJobState.GetDisplayWorkGivers(workType);
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
                        if (!SpecificJobState.TryCapturePriority(
                                null,
                                workGiver,
                                out WorkTabSpecificPriorityBaseline snapshot,
                                out _,
                                out _))
                            continue;
                        WorkloadSpecificJobTargetKey key =
                            WorkTabEffectiveStateIds.ForGlobalSpecificJobTarget(
                                workType,
                                workGiver);
                        WorkloadLiveCapturePolicy.ApplySpecificPrioritySnapshot(
                            draft,
                            key,
                            snapshot.State == WorkTabSpecificPriorityState.GlobalSet,
                            snapshot.State == WorkTabSpecificPriorityState.GlobalClear,
                            snapshot.Priority);
                    }

                    if (WorkloadLiveCapturePolicy.TryCaptureSchedule(
                            draft,
                            WorkloadScheduleTargetKey.GlobalWorkGiver(
                                WorkTabEffectiveStateIds.ForWorkType(workType),
                                WorkTabEffectiveStateIds.ForWorkGiver(workGiver)),
                            SpecificJobState.ReadPriority(
                                null,
                                workGiver,
                                PriorityState.DefaultEnabledPriority),
                            LiveScheduleCapture))
                    {
                        capturedSchedule = true;
                    }
                }

                if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
                {
                    if (!SpecificJobState.TryCaptureOrder(
                            null,
                            workType,
                            out WorkTabSpecificOrderBaseline snapshot,
                            out _,
                            out _))
                        continue;
                    WorkloadWorkTypeOrderKey key =
                        WorkTabEffectiveStateIds.ForGlobalWorkTypeOrder(workType);
                    WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                        draft,
                        key,
                        snapshot.State == WorkTabSpecificOrderState.GlobalSet,
                        snapshot.State == WorkTabSpecificOrderState.GlobalClear,
                        snapshot.OrderedWorkGiverNames);
                }
            }
        }

        private static WorkloadSchedulePayload TryCaptureLiveSchedule(
            WorkloadScheduleTargetKey key,
            int fallbackPriority)
        {
            return WorkloadTimePriorityAdapter.TryGetTimePriorityTarget(
                    key,
                    out TimePriorityTarget target,
                    out _) &&
                ScheduleState.TryCapture(
                    target,
                    fallbackPriority,
                    out TimePriorityLiveScheduleSnapshot snapshot,
                    out _) && snapshot.HadSchedule
                ? WorkloadTimePriorityAdapter.ToPayload(snapshot.Schedule)
                : null;
        }
    }
}
