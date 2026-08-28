using System;
using System.Collections.Generic;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Workloads.Projection;
using RimWorld;
using Spine.Profiling;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.Workloads
{
    /// <summary>
    /// Coordinates the workload footer and the modern preview session that are
    /// both hosted by the one Work-tab window. This is deliberately not a
    /// WindowStack/window owner: the footer picker remains the only secondary
    /// workload surface and a live preview keeps it guarded.
    /// </summary>
    internal static class WorkloadSurfaceCoordinator
    {
        private enum SurfaceOwner
        {
            None,
            Footer,
            Preview
        }

        private static SurfaceOwner _owner;
        private static Action _closeFooter;
        private static Func<bool> _isPreviewActive;
        private static Action<string> _reportPreviewMessage;

        internal static void RegisterFooterCloser(Action closer)
        {
            _closeFooter = closer;
        }

        internal static void RegisterPreviewState(
            Func<bool> isActive,
            Action<string> reportMessage)
        {
            _isPreviewActive = isActive;
            _reportPreviewMessage = reportMessage;
        }

        internal static bool TryOpenFooter()
        {
            if (_isPreviewActive?.Invoke() == true)
            {
                _reportPreviewMessage?.Invoke(
                    "BWT_Workload_FinishBeforeOpeningList".Translate());
                return false;
            }

            if (_owner == SurfaceOwner.Footer)
            {
                return true;
            }

            _owner = SurfaceOwner.Footer;
            return true;
        }

        internal static void OpenPreview()
        {
            if (_owner == SurfaceOwner.Preview)
            {
                return;
            }

            _closeFooter?.Invoke();
            _owner = SurfaceOwner.Preview;
        }

        internal static void NotifyFooterClosed()
        {
            if (_owner == SurfaceOwner.Footer)
            {
                _owner = SurfaceOwner.None;
            }
        }

        internal static void NotifyPreviewClosed()
        {
            if (_owner == SurfaceOwner.Preview)
            {
                _owner = SurfaceOwner.None;
            }
        }

        internal static void Reset()
        {
            _owner = SurfaceOwner.None;
        }
    }

    /// <summary>
    /// UI-facing crossing point for legacy Worklists and Workloads V2. The
    /// later UI can depend on this surface without reaching into either
    /// persistence shape or the live-priority authority boundary.
    /// </summary>
    internal static class WorkloadGateway
    {
        private static IWorkloadWorldState _boundComponent;
        private static LegacyWorkloadBackend _legacyBackend;
        private static Workload2Backend _modernBackend;

        private static TResult Dispatch<TResult>(
            Func<LegacyWorkloadBackend, TResult> legacyOperation,
            Func<Workload2Backend, TResult> modernOperation,
            Func<TResult> noGameFailure)
        {
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return noGameFailure();
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacyOperation(legacy)
                : modernOperation(modern);
        }

        private static TResult DispatchV2<TResult>(
            Func<Workload2Backend, TResult> modernOperation,
            Func<TResult> noGameFailure,
            Func<TResult> legacyModeFailure)
        {
            return Dispatch(
                unusedLegacy => legacyModeFailure(),
                modernOperation,
                noGameFailure);
        }

        private static WorkloadOperationResult NoCurrentGame()
        {
            return WorkloadOperationResult.Fail(
                WorkloadDiagnosticCode.NoCurrentGame);
        }

        private static WorkloadOperationResult<T> NoCurrentGame<T>()
        {
            return WorkloadOperationResult<T>.Fail(
                WorkloadDiagnosticCode.NoCurrentGame);
        }

        private static WorkloadOperationResult<T> V2Unavailable<T>()
        {
            return WorkloadOperationResult<T>.Fail(
                WorkloadDiagnosticCode.ModeUnavailable);
        }

        private static WorkloadV2CommitResult DispatchV2Commit(
            WorkloadDecisionKind decisionKind,
            Func<Workload2Backend, WorkloadV2CommitResult> modernOperation)
        {
            return DispatchV2(
                modernOperation,
                () => WorkloadV2ApplyService.GatewayFailure(
                    decisionKind,
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "gateway.game"),
                () => WorkloadV2ApplyService.GatewayFailure(
                    decisionKind,
                    WorkloadDiagnosticCode.ModeUnavailable,
                    "gateway.mode"));
        }

        internal static WorkloadBackendMode ResolveMode()
        {
            return WorkloadModeService.CurrentMode;
        }

        internal static WorkloadBackendMode CurrentMode
        {
            get { return ResolveMode(); }
        }

        internal static string CurrentLabel()
        {
            WorkloadOperationResult<WorkloadDescriptor> current = GetCurrent();
            return current.Succeeded ? current.Value.Label : string.Empty;
        }

        internal static bool HasCurrentWorkload()
        {
            return GetCurrent().Succeeded;
        }

        internal static bool IsV2PreviewSessionActive
        {
            get
            {
                return TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern)
                    && modern.IsPreviewSessionActive;
            }
        }

        internal static WorkloadOperationResult<WorkloadDescriptor> GetCurrent()
        {
            return Dispatch(
                legacy => legacy.Current(),
                modern => modern.Current(),
                NoCurrentGame<WorkloadDescriptor>);
        }

        internal static IReadOnlyList<WorkloadDescriptor> SavedWorkloads()
        {
            return Dispatch(
                legacy => legacy.List(),
                modern => modern.List(),
                () => new List<WorkloadDescriptor>());
        }

        internal static WorkloadOperationResult SelectWorkload(string stableId)
        {
            return Dispatch(
                legacy => legacy.Select(stableId),
                modern => modern.Select(stableId),
                NoCurrentGame);
        }

        internal static WorkloadOperationResult<WorkloadDescriptor> CreateWorkload(string label)
        {
            return Dispatch(
                legacy => legacy.Create(label),
                modern => modern.Create(label),
                NoCurrentGame<WorkloadDescriptor>);
        }

        internal static WorkloadOperationResult DeleteWorkload(string stableId)
        {
            return Dispatch(
                legacy => legacy.Delete(stableId),
                modern => modern.Delete(stableId),
                NoCurrentGame);
        }

        internal static WorkloadOperationResult RenameWorkload(string stableId, string newLabel)
        {
            return Dispatch(
                legacy => legacy.Rename(stableId, newLabel),
                modern => modern.Rename(stableId, newLabel),
                NoCurrentGame);
        }

        internal static WorkloadOperationResult ApplyCurrentWorkload()
        {
            return Dispatch(
                legacy => legacy.Apply(),
                modern => modern.Apply(),
                NoCurrentGame);
        }

        /// <summary>
        /// Changes the setting only after checking the modern preview boundary.
        /// A blocked transition leaves useLegacyWorkloads untouched.
        /// </summary>
        internal static WorkloadOperationResult TryTransitionMode(WorkloadBackendMode targetMode)
        {
            return TryTransitionMode(targetMode, persistSettings: true);
        }

        internal static WorkloadOperationResult TryTransitionMode(
            WorkloadBackendMode targetMode,
            bool persistSettings)
        {
            return WorkloadModeService.TryTransition(targetMode, persistSettings);
        }

        internal static WorkloadOperationResult<WorkloadSession> BeginV2Preview(
            string stableId = null)
        {
            return DispatchV2(
                modern => modern.BeginPreview(stableId),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>());
        }

        internal static WorkloadOperationResult<WorkloadSession> SetV2PreviewSession(WorkloadSession session)
        {
            return DispatchV2(
                modern => modern.SetPreviewSession(session),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>());
        }

        internal static WorkloadOperationResult<WorkloadSession> AdoptV2PreviewSession(
            WorkloadSession session)
        {
            return SetV2PreviewSession(session);
        }

        internal static WorkloadOperationResult<WorkloadSession> RebaseV2PreviewAfterPersistence(
            WorkloadPersistenceReceipt receipt,
            WorkloadDecisionKind decisionKind = WorkloadDecisionKind.Apply,
            string targetStableId = null,
            string forkLabel = null)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return NoCurrentGame<WorkloadSession>();
            }

            if (ResolveMode() != WorkloadBackendMode.Modern)
            {
                return V2Unavailable<WorkloadSession>();
            }

            if (receipt == null &&
                decisionKind != WorkloadDecisionKind.Update &&
                decisionKind != WorkloadDecisionKind.Fork)
            {
                return modern.RebasePreviewAfterPersistence(null);
            }

            if (receipt == null)
            {
                WorkloadOperationResult<WorkloadPersistenceReceipt> recovered =
                    modern.RecoverPersistenceReceipt(
                        decisionKind,
                        targetStableId,
                        forkLabel);
                if (!recovered.Succeeded || recovered.Value == null)
                {
                    return WorkloadOperationResult<WorkloadSession>.Fail(
                        recovered.Code,
                        recovered.Context);
                }

                receipt = recovered.Value;
            }

            return modern.RebasePreviewAfterPersistence(receipt);
        }

        internal static WorkloadOperationResult<WorkloadSession> AdoptV2PreviewSession()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return NoCurrentGame<WorkloadSession>();
            }

            if (ResolveMode() != WorkloadBackendMode.Modern ||
                modern.PreviewSession == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NotFound);
            }

            // Read the session from the active UI backend. Temporary peer
            // transaction backends are never copied into this controller.
            return modern.SetPreviewSession(modern.PreviewSession);
        }

        internal static WorkloadOperationResult<WorkloadSession> EditV2Preview(
            Action<WorkloadDraft> edit)
        {
            return DispatchV2(
                modern => modern.EditPreview(edit),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>());
        }

        internal static WorkloadOperationResult<WorkloadSession> SetV2PreviewState(
            WorkloadProjectedState projectedState)
        {
            return DispatchV2(
                modern => modern.SetPreviewState(projectedState),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>());
        }

        internal static WorkloadOperationResult<WorkloadSession> ExtendV2PreviewBaseline(
            WorkloadSession candidate,
            PawnKey pawn)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame);
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.ExtendPreviewBaseline(candidate, pawn)
                : WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.ModeUnavailable);
        }

        internal static WorkloadOperationResult<WorkloadPreviewPlan> GetV2PreviewPlan(
            WorkloadDecisionKind decisionKind = WorkloadDecisionKind.Apply)
        {
            return DispatchV2(
                modern => modern.PreviewPlan(decisionKind),
                NoCurrentGame<WorkloadPreviewPlan>,
                () => V2Unavailable<WorkloadPreviewPlan>());
        }

        internal static WorkloadOperationResult<WorkloadSemanticDiff> GetV2PreviewDiff()
        {
            return DispatchV2(
                modern => modern.PreviewDiff(),
                NoCurrentGame<WorkloadSemanticDiff>,
                () => V2Unavailable<WorkloadSemanticDiff>());
        }

        internal static WorkloadOperationResult<WorkloadSemanticDiff> GetV2PreviewImpactDiff()
        {
            return DispatchV2(
                modern => modern.PreviewImpactDiff(),
                NoCurrentGame<WorkloadSemanticDiff>,
                () => V2Unavailable<WorkloadSemanticDiff>());
        }

        internal static WorkloadV2CommitResult CommitV2Apply()
        {
            return DispatchV2Commit(
                WorkloadDecisionKind.Apply,
                modern => modern.CommitApply());
        }

        internal static WorkloadV2CommitResult CommitV2Update()
        {
            return DispatchV2Commit(
                WorkloadDecisionKind.Update,
                modern => modern.CommitUpdate());
        }

        internal static WorkloadV2CommitResult CommitV2Fork(
            string stableId,
            string label)
        {
            return DispatchV2Commit(
                WorkloadDecisionKind.Fork,
                modern => modern.CommitFork(stableId, label));
        }

        /// <summary>
        /// Starts the already-validated multiplayer transaction protocol for a
        /// modern preview. The UI deliberately calls this seam once per
        /// lifecycle click instead of calling CommitV2* and interpreting its
        /// asynchronous failure wrapper as a completed commit.
        /// </summary>
        internal static WorkloadMultiplayerCommitStatus BeginV2MultiplayerCommit(
            WorkloadDecisionKind decisionKind,
            string forkStableId,
            string forkLabel,
            string idempotencyKey = null)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return null;
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.BeginMultiplayerCommit(
                    decisionKind,
                    forkStableId,
                    forkLabel,
                    idempotencyKey)
                : null;
        }

        internal static WorkloadOperationResult EndV2Preview()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return NoCurrentGame();
            }

            return modern.EndPreview();
        }

        internal static WorkloadOperationResult CancelV2Preview()
        {
            return EndV2Preview();
        }

        private static bool TryBind(
            out LegacyWorkloadBackend legacy,
            out Workload2Backend modern)
        {
            IWorkloadWorldState component = WorkloadWorldStates.Current;
            if (component == null)
            {
                if (_boundComponent != null)
                {
                    _boundComponent = null;
                    _legacyBackend = null;
                    _modernBackend = null;
                }

                legacy = null;
                modern = null;
                return false;
            }

            if (_boundComponent != component || _legacyBackend == null || _modernBackend == null)
            {
                _boundComponent = component;
                _legacyBackend = new LegacyWorkloadBackend(component);
                _modernBackend = new Workload2Backend(component);
            }

            legacy = _legacyBackend;
            modern = _modernBackend;
            return true;
        }
    }

    /// <summary>
    /// Owns the modern workload preview boundary for the Work tab. The gateway
    /// remains the only persistence/live-commit crossing point; this controller
    /// only keeps the session-local model and its effective-state providers.
    /// </summary>
    internal sealed class WorkloadPreviewController :
        IWorkGridPreviewPort,
        IWorkGridPreviewViewSource,
        IWorkTabPresentationPreviewPort
    {
        private readonly BwtLiveWorkTabEffectiveStateAdapter _liveAdapter;
        private readonly LiveWorkTabEffectiveStateProvider _liveProvider;
        private readonly List<QueuedLifecycleAction> _queuedLifecycleActions =
            new List<QueuedLifecycleAction>();
        private readonly object _multiplayerStatusGate = new object();
        private readonly Queue<MultiplayerStatusSnapshot> _multiplayerStatusQueue =
            new Queue<MultiplayerStatusSnapshot>();
        private WorkloadSession _session;
        private ProjectedWorkTabEffectiveStateProvider _projectedProvider;
        private WorkloadParentPriorityProjection _parentPriorityProjection;
        private IWorkloadWorldState _boundComponent;
        private bool _hasMultiplayerAttempt;
        private string _multiplayerRequestId = string.Empty;
        private string _multiplayerIdempotencyKey = string.Empty;
        private string _multiplayerPayloadFingerprint = string.Empty;
        private WorkloadDecisionKind _multiplayerDecision;
        private string _multiplayerForkStableId = string.Empty;
        private string _multiplayerForkLabel = string.Empty;
        private WorkloadMultiplayerCommitState _multiplayerCommitState =
            WorkloadMultiplayerCommitState.None;
        private string _multiplayerCommitMessage = string.Empty;
        private bool _multiplayerRecoveryBlocked;
        private bool _multiplayerTerminalHandled;
        private bool _previewRecoveryBlocked;
        private InspectionReadState _inspectionReadState = InspectionReadState.Empty;
        private WorkloadSession _semanticDiffSession;
        private long _semanticDiffSessionRevision = long.MinValue;
        private WorkloadSemanticDiff _cachedTemplateDiff;
        private WorkloadSemanticDiff _cachedLiveDiff;
        private WorkloadSession _unsupportedPresentationSession;
        private long _unsupportedPresentationSessionRevision = long.MinValue;
        private bool _cachedUnsupportedOwnedPresentationState;
        private WorkloadSession _inspectionIndexSession;
        private long _inspectionIndexSessionRevision = long.MinValue;
        private WorkloadInspectionContext _inspectionIndexContext;
        private WorkloadInspectionContext _inspectionContext;
        private WorkloadMembershipSnapshot _membershipSnapshot;
        private string _membershipSnapshotSourceIdentity = string.Empty;
        private long _membershipSnapshotMembershipRevision = long.MinValue;
        private long _membershipSnapshotPawnSetRevision = long.MinValue;
        private long _projectedEditablePawnSetRevision = long.MinValue;
        private long _synchronizedProjectedProviderRevision = long.MinValue;
        private readonly BoundedUndoRedoHistory<DraftTransition> _draftHistory =
            new BoundedUndoRedoHistory<DraftTransition>(64);
        private CanceledDraftRecovery _canceledDraft;
        // Repaint/Layout passes frequently share the exact same preview
        // state. Retain the immutable read port until one of its source
        // identities changes instead of allocating a wrapper per IMGUI event.
        private IWorkGridPreviewPort _capturedPreviewView;
        private IWorkTabEffectiveStateProvider _capturedPreviewProvider;
        private WorkTabEffectiveStateRevision _capturedPreviewRevision;
        private bool _capturedPreviewActive;
        private string _capturedPreviewMessage = string.Empty;
        private WorkloadMembershipSnapshot _capturedPreviewMembership;
        private InspectionReadState _capturedPreviewInspection;
        private WorkloadScope _capturedPreviewScope;
        private bool _capturedPreviewInspectionHighlightsEnabled;
        private float _capturedPreviewInspectionOpacity;
        private MembershipTooltipCatalog _capturedPreviewMembershipTooltips;
        private MembershipTooltipCatalog _membershipTooltipCatalog;
        private string _membershipTooltipLanguage = string.Empty;

        private sealed class DraftTransition
        {
            internal DraftTransition(WorkloadProjectedState before, WorkloadProjectedState after)
            {
                Before = before;
                After = after;
            }

            internal WorkloadProjectedState Before { get; }
            internal WorkloadProjectedState After { get; }
        }

        private sealed class CanceledDraftRecovery
        {
            internal CanceledDraftRecovery(
                string sourceStableId,
                string sourceIdentity,
                WorkloadProjectedState projectedState,
                List<DraftTransition> undo,
                List<DraftTransition> redo)
            {
                SourceStableId = sourceStableId ?? string.Empty;
                SourceIdentity = sourceIdentity ?? string.Empty;
                ProjectedState = projectedState;
                Undo = undo ?? new List<DraftTransition>();
                Redo = redo ?? new List<DraftTransition>();
            }

            internal string SourceStableId { get; }
            internal string SourceIdentity { get; }
            internal WorkloadProjectedState ProjectedState { get; }
            internal List<DraftTransition> Undo { get; }
            internal List<DraftTransition> Redo { get; }
        }

        private sealed class QueuedLifecycleAction
        {
            internal QueuedLifecycleAction(
                Func<bool> action,
                Action<bool> completed)
            {
                Action = action;
                Completed = completed;
            }

            internal Func<bool> Action { get; }
            internal Action<bool> Completed { get; }
        }

        private sealed class EffectiveStateScope : IDisposable
        {
            private readonly IDisposable _parentReadScope;
            private readonly IDisposable _stateScope;

            internal EffectiveStateScope(
                IDisposable stateScope,
                IDisposable parentReadScope)
            {
                _stateScope = stateScope;
                _parentReadScope = parentReadScope;
            }

            public void Dispose()
            {
                _parentReadScope?.Dispose();
                _stateScope?.Dispose();
            }
        }

        /// <summary>
        /// Multiplayer callbacks may be raised by the transport while the
        /// Work-tab IMGUI pass is outside its effective-state scope. Copy the
        /// immutable status fields at the callback boundary and consume them
        /// from PrepareFrame on the main thread instead of touching session or
        /// provider state from the callback.
        /// </summary>
        private sealed class MultiplayerStatusSnapshot
        {
            internal MultiplayerStatusSnapshot(WorkloadMultiplayerCommitStatus status)
            {
                RequestId = status?.RequestId ?? string.Empty;
                State = status?.State ?? WorkloadMultiplayerCommitState.None;
                Code = status?.Code ?? WorkloadDiagnosticCode.InvalidState;
                Context = status?.Context ?? WorkloadDiagnosticContext.Empty;
                Result = status?.Result;
            }

            internal string RequestId { get; }
            internal WorkloadMultiplayerCommitState State { get; }
            internal WorkloadDiagnosticCode Code { get; }
            internal WorkloadDiagnosticContext Context { get; }
            internal WorkloadV2CommitResult Result { get; }
        }

        private bool _inspectionActive;
        private string _lastMessage = string.Empty;
        private WorkloadApplicationPublication _lifecycleApplicationPublication =
            WorkloadApplicationPublication.None;

        private enum WorkloadInspectionContext
        {
            None = 0,
            Template = 1,
            Live = 2
        }

        /// <summary>
        /// Language-local membership labels shared by the live and captured
        /// preview ports. Keeping them beside the captured state prevents the
        /// grid from translating a label for every visible row.
        /// </summary>
        private sealed class MembershipTooltipCatalog
        {
            internal static readonly MembershipTooltipCatalog Empty =
                new MembershipTooltipCatalog(
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty);

            internal MembershipTooltipCatalog(
                string included,
                string @new,
                string outside,
                string excluded,
                string missing)
            {
                Included = included ?? string.Empty;
                New = @new ?? string.Empty;
                Outside = outside ?? string.Empty;
                Excluded = excluded ?? string.Empty;
                Missing = missing ?? string.Empty;
            }

            internal string Included { get; }
            internal string New { get; }
            internal string Outside { get; }
            internal string Excluded { get; }
            internal string Missing { get; }

            internal string Get(WorkGridPreviewMembershipState state)
            {
                switch (state)
                {
                    case WorkGridPreviewMembershipState.Included:
                        return Included;
                    case WorkGridPreviewMembershipState.New:
                        return New;
                    case WorkGridPreviewMembershipState.OutsideScope:
                        return Outside;
                    case WorkGridPreviewMembershipState.ExplicitlyExcluded:
                        return Excluded;
                    case WorkGridPreviewMembershipState.Missing:
                        return Missing;
                    default:
                        return string.Empty;
                }
            }
        }

        internal WorkloadPreviewController()
        {
            _liveAdapter = new BwtLiveWorkTabEffectiveStateAdapter();
            _liveProvider = _liveAdapter.CreateProvider("bwt.live.workload-preview");
            WorkloadSurfaceCoordinator.RegisterPreviewState(
                () => Current?.IsActive == true,
                message => Current?.SetMessage(message));
            WorkTabEffectiveStateRuntime.RegisterPreviewScopePusher(
                () => Current?.PushEffectiveStateScope());

            // RimWorld may construct a replacement Work-tab window without opening it.
            // Construction may supply the first controller, but it must never displace a
            // controller that still owns an active preview session.
            if (Current?.IsActive != true)
            {
                ClaimCurrentOwnership();
            }
        }

        private static string T(string key)
        {
            return key.Translate().ToString();
        }

        internal static WorkloadPreviewController Current { get; private set; }

        /// <summary>
        /// Makes this controller the UI owner when its Work-tab window actually opens.
        /// An already-active preview remains authoritative until that session ends.
        /// </summary>
        internal void ActivateForWindow()
        {
            if (ReferenceEquals(Current, this) || Current?.IsActive != true)
            {
                ClaimCurrentOwnership();
            }
        }

        private void ClaimCurrentOwnership()
        {
            if (ReferenceEquals(Current, this))
            {
                return;
            }

            if (Current != null)
            {
                Workload2Backend.MultiplayerCommitStatusChanged -=
                    Current.OnMultiplayerCommitStatusPublished;
            }

            Current = this;
            Workload2Backend.MultiplayerCommitStatusChanged +=
                OnMultiplayerCommitStatusPublished;
        }

        internal static bool IsInspectionActiveForCurrentTab =>
            Current?.IsInspectionActive == true;

        public IWorkTabEffectiveStateProvider ScopedProvider =>
            _projectedProvider ?? (IWorkTabEffectiveStateProvider)_liveProvider;

        public bool IsActive => _session != null && _projectedProvider != null;
        private WorkloadSemanticDiff EffectiveTemplateDiff
        {
            get
            {
                if (!IsActive)
                {
                    return null;
                }

                EnsureSemanticDiffCache();
                return _cachedTemplateDiff;
            }
        }

        private WorkloadSemanticDiff EffectiveLiveDiff
        {
            get
            {
                if (!IsActive)
                {
                    return null;
                }

                EnsureSemanticDiffCache();
                return _cachedLiveDiff;
            }
        }

        internal bool HasTemplateDiff => IsActive && _session.HasTemplateChanges;
        internal bool HasLiveImpact => IsActive && _session.HasLiveChanges;
        // Kept as the template-dirty alias for existing callers. Update and
        // inspection are deliberately about the stored template, not the
        // current colony baseline.
        internal bool HasSemanticDiff => HasTemplateDiff;
        public bool IsInspectionActive
        {
            get
            {
                if (!IsActive || !_inspectionActive ||
                    _inspectionContext == WorkloadInspectionContext.None)
                {
                    return false;
                }

                return _inspectionContext == WorkloadInspectionContext.Live
                    ? HasLiveImpact
                    : HasTemplateDiff;
            }
        }

        public bool InspectionHighlightsEnabled =>
            IsActive &&
            BWTWorkTabEffectiveSettings.GetBool(SettingIDs.WorkloadsInspectionHighlights);

        public float InspectionOpacity =>
            IsActive
                ? BetterWorkTabSettings.ClampWorkloadInspectionOpacity(
                    BWTWorkTabEffectiveSettings.GetInt(SettingIDs.WorkloadsInspectionOpacity)) / 100f
                : 0f;

        public bool HasInspectionCellTargets
        {
            get
            {
                if (!IsInspectionActive)
                {
                    return false;
                }

                EnsureInspectionIndex();
                return _inspectionReadState.HasCellTargets;
            }
        }

        public bool HasManualModeInspectionChange
        {
            get
            {
                if (!IsInspectionActive)
                {
                    return false;
                }

                EnsureInspectionIndex();
                return _inspectionReadState.HasManualModeChange;
            }
        }

        public IReadOnlyList<WorkGridInspectionTarget> InspectionTargets
        {
            get
            {
                EnsureInspectionIndex();
                return _inspectionReadState.Targets;
            }
        }

        /// <summary>
        /// Inspection data is rebuilt only when the session/context changes and
        /// then published as one immutable read state. Completed WorkTabViews
        /// share that state while input prepares its replacement.
        /// </summary>
        private sealed class InspectionReadState
        {
            internal static readonly InspectionReadState Empty = new InspectionReadState(
                false,
                false,
                false,
                new WorkGridInspectionTarget[0],
                new HashSet<int>());

            internal InspectionReadState(
                bool isActive,
                bool hasCellTargets,
                bool hasManualModeChange,
                IReadOnlyList<WorkGridInspectionTarget> targets,
                HashSet<int> changedSchedulePawnIds)
            {
                IsActive = isActive;
                HasCellTargets = hasCellTargets;
                HasManualModeChange = hasManualModeChange;
                Targets = targets ?? new WorkGridInspectionTarget[0];
                ChangedSchedulePawnIds = changedSchedulePawnIds ?? new HashSet<int>();
            }

            internal bool IsActive { get; }
            internal bool HasCellTargets { get; }
            internal bool HasManualModeChange { get; }
            internal IReadOnlyList<WorkGridInspectionTarget> Targets { get; }
            private HashSet<int> ChangedSchedulePawnIds { get; }
            internal bool HasRowLevelChanges => ChangedSchedulePawnIds.Count > 0;

            internal bool IsRowLevelChanged(Pawn pawn)
            {
                return pawn != null && pawn.thingIDNumber > 0 &&
                    ChangedSchedulePawnIds.Contains(pawn.thingIDNumber);
            }
        }

        private sealed class CapturedWorkGridPreviewPort : IWorkGridPreviewPort
        {
            private readonly WorkloadPreviewController _live;
            private readonly IWorkTabEffectiveStateProvider _scopedProvider;
            private readonly bool _isActive;
            private readonly string _lastMessage;
            private readonly WorkloadMembershipSnapshot _membership;
            private readonly InspectionReadState _inspection;
            private readonly WorkloadScope _scope;
            private readonly bool _inspectionHighlightsEnabled;
            private readonly float _inspectionOpacity;
            private readonly MembershipTooltipCatalog _membershipTooltips;

            internal CapturedWorkGridPreviewPort(
                WorkloadPreviewController live,
                IWorkTabEffectiveStateProvider scopedProvider,
                bool isActive,
                string lastMessage,
                WorkloadMembershipSnapshot membership,
                InspectionReadState inspection,
                WorkloadScope scope,
                bool inspectionHighlightsEnabled,
                float inspectionOpacity,
                MembershipTooltipCatalog membershipTooltips)
            {
                _live = live;
                _scopedProvider = scopedProvider;
                _isActive = isActive;
                _lastMessage = lastMessage ?? string.Empty;
                _membership = membership;
                _inspection = inspection ?? InspectionReadState.Empty;
                _scope = scope ?? WorkloadScope.Empty;
                _inspectionHighlightsEnabled = inspectionHighlightsEnabled;
                _inspectionOpacity = inspectionOpacity;
                _membershipTooltips = membershipTooltips ?? MembershipTooltipCatalog.Empty;
            }

            public IWorkTabEffectiveStateProvider ScopedProvider => _scopedProvider;
            public bool IsActive => _isActive;
            public string LastMessage => _lastMessage;
            public bool IsInspectionActive => _inspection.IsActive;
            public bool InspectionHighlightsEnabled => _inspectionHighlightsEnabled;
            public float InspectionOpacity => _inspectionOpacity;
            public bool HasManualModeInspectionChange => _inspection.HasManualModeChange;
            public bool HasInspectionRowLevelChanges => _inspection.HasRowLevelChanges;
            public bool HasInspectionCellTargets => _inspection.HasCellTargets;
            public IReadOnlyList<WorkGridInspectionTarget> InspectionTargets => _inspection.Targets;

            public bool TryHandleHistoryShortcut(Event evt) => _live.TryHandleHistoryShortcut(evt);
            public bool TrySetManualMode(bool manualMode) => _live.TrySetManualMode(manualMode);
            public bool EnsurePawnIncludedForPriorityEdit(Pawn pawn) =>
                _live.EnsurePawnIncludedForPriorityEdit(pawn);
            public WorkGridPreviewMembershipAction GetMembershipAction(Pawn pawn) =>
                ResolveMembershipAction(_isActive, _scope, _membership, pawn);
            public bool ToggleMembership(Pawn pawn) =>
                _live.ToggleMembership(pawn == null ? null : WorkTabEffectiveStateIds.ForPawn(pawn));

            public bool TryGetMembershipPresentation(
                Pawn pawn,
                out WorkGridPreviewMembershipState state)
            {
                return TryGetMembershipPresentation(pawn, out state, out _);
            }

            public bool TryGetMembershipPresentation(
                Pawn pawn,
                out WorkGridPreviewMembershipState state,
                out string tooltip)
            {
                state = WorkGridPreviewMembershipState.None;
                tooltip = string.Empty;
                if (!_isActive || !WorkloadPreviewController.TryGetMembershipPresentation(
                        _membership,
                        pawn,
                        out state))
                {
                    return false;
                }

                tooltip = _membershipTooltips.Get(state);
                return true;
            }

            public bool IsInspectionRowLevelChanged(Pawn pawn) =>
                _inspection.IsRowLevelChanged(pawn);
        }
        public string LastMessage => _lastMessage ?? string.Empty;

        public bool TryGetMembershipPresentation(
            Pawn pawn,
            out WorkGridPreviewMembershipState state)
        {
            return TryGetMembershipPresentation(pawn, out state, out _);
        }

        public bool TryGetMembershipPresentation(
            Pawn pawn,
            out WorkGridPreviewMembershipState state,
            out string tooltip)
        {
            state = WorkGridPreviewMembershipState.None;
            tooltip = string.Empty;
            if (!IsActive)
            {
                return false;
            }

            try
            {
                if (!TryGetMembershipPresentation(
                        GetMembershipSnapshot(),
                        pawn,
                        out state))
                {
                    return false;
                }

                tooltip = GetMembershipTooltipCatalog().Get(state);
                return true;
            }
            catch (Exception exception)
            {
                // Membership accents are optional preview presentation. A
                // transient pawn-scope failure must not quarantine Work-grid
                // rendering or escape again through the native fallback.
                Log.ErrorOnce(
                    "[BWT] Workload membership row presentation failed; continuing without scope accents.\n" +
                    exception,
                    0x4257544D);
                return false;
            }
        }

        IWorkGridPreviewPort IWorkGridPreviewViewSource.CapturePreviewView()
        {
            return CapturePreviewView();
        }

        private IWorkGridPreviewPort CapturePreviewView()
        {
            bool active = IsActive;
            InspectionReadState inspection = InspectionReadState.Empty;
            if (active && IsInspectionActive)
            {
                EnsureInspectionIndex();
                inspection = _inspectionReadState;
            }

            IWorkTabEffectiveStateProvider provider = ScopedProvider;
            WorkTabEffectiveStateRevision revision = provider?.RevisionToken ?? default;
            WorkloadMembershipSnapshot membership = active ? GetMembershipSnapshot() : null;
            WorkloadScope scope = _session?.SourceTemplate?.Definition?.Scope;
            string message = LastMessage;
            bool inspectionHighlightsEnabled = active && InspectionHighlightsEnabled;
            float inspectionOpacity = active ? InspectionOpacity : 0f;
            MembershipTooltipCatalog membershipTooltips = active
                ? GetMembershipTooltipCatalog()
                : MembershipTooltipCatalog.Empty;
            if (_capturedPreviewView != null &&
                ReferenceEquals(_capturedPreviewProvider, provider) &&
                _capturedPreviewRevision == revision &&
                _capturedPreviewActive == active &&
                StringComparer.Ordinal.Equals(_capturedPreviewMessage, message) &&
                ReferenceEquals(_capturedPreviewMembership, membership) &&
                ReferenceEquals(_capturedPreviewInspection, inspection) &&
                ReferenceEquals(_capturedPreviewScope, scope) &&
                _capturedPreviewInspectionHighlightsEnabled == inspectionHighlightsEnabled &&
                _capturedPreviewInspectionOpacity == inspectionOpacity &&
                ReferenceEquals(_capturedPreviewMembershipTooltips, membershipTooltips))
            {
                return _capturedPreviewView;
            }

            _capturedPreviewProvider = provider;
            _capturedPreviewRevision = revision;
            _capturedPreviewActive = active;
            _capturedPreviewMessage = message;
            _capturedPreviewMembership = membership;
            _capturedPreviewInspection = inspection;
            _capturedPreviewScope = scope;
            _capturedPreviewInspectionHighlightsEnabled = inspectionHighlightsEnabled;
            _capturedPreviewInspectionOpacity = inspectionOpacity;
            _capturedPreviewMembershipTooltips = membershipTooltips;
            _capturedPreviewView = new CapturedWorkGridPreviewPort(
                this,
                provider,
                active,
                message,
                membership,
                inspection,
                scope,
                inspectionHighlightsEnabled,
                inspectionOpacity,
                membershipTooltips);
            return _capturedPreviewView;
        }

        bool IWorkTabPresentationPreviewPort.IsPreviewActive => IsActive;

        bool IWorkTabPresentationPreviewPort.TryReadPresentationPreview(
            out WorkTabPresentationPreviewState preview,
            out string reason)
        {
            preview = default;
            reason = string.Empty;
            if (!IsActive || _session == null)
            {
                reason = "The active workload preview is not available for settings.";
                return false;
            }

            preview = new WorkTabPresentationPreviewState(
                _session.PreviewStamp,
                ToPresentationIntents(
                    _session.LiveBaselineState?.PresentationSettingIntents),
                ToPresentationIntents(
                    _session.ProjectedState?.PresentationSettingIntents));
            return true;
        }

        bool IWorkTabPresentationPreviewPort.TryMutatePresentation(
            WorkTabPresentationPreviewMutation mutation,
            out string reason)
        {
            switch (mutation.Kind)
            {
                case WorkTabPresentationPreviewMutationKind.Set:
                    return TryMutatePresentation(
                        provider => provider.SetPresentationSetting(
                            mutation.SettingId,
                            WorkloadSettingValue.WorkloadOwned(
                                ToWorkloadScalar(mutation.Value))),
                        "The projected presentation setting could not be synchronized.",
                        out reason);

                case WorkTabPresentationPreviewMutationKind.Acquire:
                    return TryMutatePresentation(
                        provider => provider.AcquirePresentationSetting(
                            mutation.SettingId,
                            ToWorkloadScalar(mutation.Value)),
                        "The presentation ownership change could not be synchronized.",
                        out reason);

                case WorkTabPresentationPreviewMutationKind.Release:
                    return TryMutatePresentation(
                        provider => provider.ReleasePresentationSetting(mutation.SettingId),
                        "The presentation ownership change could not be synchronized.",
                        out reason);

                default:
                    reason = "The requested presentation ownership action is not supported.";
                    return false;
            }
        }

        private static IReadOnlyList<PresentationIntentEntry> ToPresentationIntents(
            IReadOnlyList<WorkloadPresentationSettingIntentEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return new PresentationIntentEntry[0];
            }

            var result = new List<PresentationIntentEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                WorkloadPresentationSettingIntentEntry entry = entries[i];
                if (entry != null)
                {
                    result.Add(new PresentationIntentEntry(
                        entry.Key,
                        ToPresentationIntent(entry.Intent)));
                }
            }
            return result;
        }

        private static PresentationIntent ToPresentationIntent(
            WorkloadIntent<WorkloadSettingValue> intent)
        {
            if (intent.IsClear)
            {
                return PresentationIntent.Clear;
            }
            if (!intent.HasValue)
            {
                return PresentationIntent.NoOpinion;
            }

            return PresentationIntent.Set(
                ToPresentationValue(intent.Value.Scalar),
                intent.Value.Ownership == WorkloadSettingOwnership.WorkloadOwned
                    ? PresentationOwnership.PreviewOwned
                    : PresentationOwnership.Global);
        }

        private static PresentationValue ToPresentationValue(WorkloadScalarValue value)
        {
            switch (value.Kind)
            {
                case WorkloadScalarKind.Boolean:
                    return PresentationValue.FromBoolean(value.BooleanValue);
                case WorkloadScalarKind.Integer:
                    return PresentationValue.FromInteger(value.IntegerValue);
                case WorkloadScalarKind.String:
                    return PresentationValue.FromString(value.StringValue);
                default:
                    return PresentationValue.Empty;
            }
        }

        private static WorkloadScalarValue ToWorkloadScalar(PresentationValue value)
        {
            switch (value.Kind)
            {
                case PresentationValueKind.Boolean:
                    return WorkloadScalarValue.FromBoolean(value.BooleanValue);
                case PresentationValueKind.Integer:
                    return WorkloadScalarValue.FromInteger(value.IntegerValue);
                case PresentationValueKind.String:
                    return WorkloadScalarValue.FromString(value.StringValue);
                default:
                    return WorkloadScalarValue.Empty;
            }
        }

        private bool TryMutatePresentation(
            Func<ProjectedWorkTabEffectiveStateProvider, WorkTabEffectiveStateMutationResult> mutate,
            string synchronizationFailure,
            out string reason)
        {
            reason = string.Empty;
            if (!IsActive || _projectedProvider == null || mutate == null)
            {
                reason = "The active workload preview is not available for editing.";
                return false;
            }

            WorkTabEffectiveStateMutationResult result = mutate(_projectedProvider);
            WorkTabEffectiveStateRuntime.AcceptPreviewMutation(result);
            if (result.IsBlocked || (!result.Accepted && !result.IsNoOp))
            {
                reason = result.Reason;
                return false;
            }

            if (SynchronizeAfterInput())
            {
                return true;
            }

            reason = LastMessage;
            if (string.IsNullOrEmpty(reason))
            {
                reason = synchronizationFailure;
            }

            return false;
        }

        /// <summary>
        /// Only legacy value-only payloads and explicit unsupported clears are
        /// blocked here. Typed schedules and allowlisted presentation intents
        /// are handled by the central runtime writer and must reach the normal
        /// footer lifecycle actions.
        /// </summary>
        internal bool HasUnsupportedOwnedPresentationState
        {
            get
            {
                if (!IsActive || _session.SourceTemplate?.Definition == null)
                {
                    _unsupportedPresentationSession = null;
                    _unsupportedPresentationSessionRevision = long.MinValue;
                    _cachedUnsupportedOwnedPresentationState = false;
                    return false;
                }

                if (ReferenceEquals(_unsupportedPresentationSession, _session) &&
                    _unsupportedPresentationSessionRevision == _session.SessionRevision)
                {
                    return _cachedUnsupportedOwnedPresentationState;
                }

                _cachedUnsupportedOwnedPresentationState =
                    ComputeUnsupportedOwnedPresentationState();
                _unsupportedPresentationSession = _session;
                _unsupportedPresentationSessionRevision = _session.SessionRevision;
                return _cachedUnsupportedOwnedPresentationState;
            }
        }

        private bool ComputeUnsupportedOwnedPresentationState()
        {
            if ((_session.UnsupportedClearDimensions?.Count ?? 0) > 0)
            {
                return true;
            }

            WorkloadProjectedState baseline = _session.TemplateBaselineState;
            WorkloadProjectedState projected = _session.ProjectedState;

            // Legacy value-only payloads are never promoted by the resolver.
            // Keep the fail-closed scan at the session revision boundary rather
            // than repeating it for every footer button on every IMGUI pass.
            return WorkloadV2OwnershipResolver.HasLegacyPayload(
                       baseline,
                       WorkloadStateDimension.Schedules) ||
                   WorkloadV2OwnershipResolver.HasLegacyPayload(
                       baseline,
                       WorkloadStateDimension.PresentationSettings) ||
                   WorkloadV2OwnershipResolver.HasLegacyPayload(
                       projected,
                       WorkloadStateDimension.Schedules) ||
                   WorkloadV2OwnershipResolver.HasLegacyPayload(
                       projected,
                       WorkloadStateDimension.PresentationSettings);
        }

        private string UnsupportedPresentationCommitReason
        {
            get
            {
                return (_session?.UnsupportedClearDimensions?.Count ?? 0) > 0
                    ? WorkloadPresentationResolver.ResolveUnsupportedClear(
                        _session.UnsupportedClearDimensions)
                    : T("BWT_Workload_UnsupportedData");
            }
        }

        internal bool IsMultiplayerCommitInFlight =>
            IsActive &&
            (IsMultiplayerRecoveryBlocked ||
             _multiplayerCommitState == WorkloadMultiplayerCommitState.Pending ||
             _multiplayerCommitState == WorkloadMultiplayerCommitState.Prepared ||
             _multiplayerCommitState == WorkloadMultiplayerCommitState.ExecutedAwaitingConfirmation);

        internal bool IsMultiplayerRecoveryBlocked =>
            IsActive &&
            (_multiplayerRecoveryBlocked ||
             _multiplayerCommitState == WorkloadMultiplayerCommitState.RollbackFailed);

        /// <summary>
        /// This is consumed by the Work-tab input owner. It blocks writes while
        /// a synchronized transaction or rollback is unresolved, while still
        /// allowing the existing table scroll path to service inspection.
        /// </summary>
        internal bool IsUnsafePreviewInputBlocked =>
            IsMultiplayerCommitInFlight || _previewRecoveryBlocked;

        internal bool CanCancelPreview => IsActive && !IsMultiplayerCommitInFlight;
        internal bool CanApplyPreview =>
            IsActive && !IsUnsafePreviewInputBlocked && !HasUnsupportedOwnedPresentationState;
        internal bool CanUpdatePreview =>
            IsActive && !IsUnsafePreviewInputBlocked && HasSemanticDiff &&
            !HasUnsupportedOwnedPresentationState;
        internal bool CanForkPreview =>
            IsActive && !IsUnsafePreviewInputBlocked && !HasUnsupportedOwnedPresentationState;
        internal string CommitBlockedMessage => _previewRecoveryBlocked
            ? T("BWT_Workload_PreviewRefreshFailed")
            : IsMultiplayerCommitInFlight
            ? MultiplayerStatusExplanation
            : HasUnsupportedOwnedPresentationState
                ? UnsupportedPresentationCommitReason
                : string.Empty;

        internal string MultiplayerStatusExplanation
        {
            get
            {
                if (IsMultiplayerRecoveryBlocked)
                {
                    return T("BWT_Workload_MultiplayerRecovery") +
                           (_multiplayerCommitMessage.AnyNonWhitespace()
                               ? " " + _multiplayerCommitMessage
                               : string.Empty);
                }

                if (IsMultiplayerCommitInFlight)
                {
                    return "BWT_Workload_MultiplayerWaiting"
                               .Translate(MultiplayerDecisionLabel(_multiplayerDecision))
                               .ToString() +
                           (_multiplayerCommitMessage.AnyNonWhitespace()
                               ? " " + _multiplayerCommitMessage
                               : string.Empty);
                }

                if (_previewRecoveryBlocked)
                {
                    return T("BWT_Workload_PreviewRefreshFailed");
                }

                return _multiplayerCommitMessage ?? string.Empty;
            }
        }

        private static string MultiplayerDecisionLabel(WorkloadDecisionKind decision)
        {
            switch (decision)
            {
                case WorkloadDecisionKind.Apply:
                    return T("BWT_Workload_ApplyAction");
                case WorkloadDecisionKind.Update:
                    return T("BWT_Workload_SaveAction");
                case WorkloadDecisionKind.Fork:
                    return T("BWT_Workload_SaveAsAction");
                default:
                    return T("BWT_Workload_OperationAction");
            }
        }

        private void OnMultiplayerCommitStatusPublished(
            WorkloadMultiplayerCommitStatus status)
        {
            if (status == null)
            {
                return;
            }

            var snapshot = new MultiplayerStatusSnapshot(status);
            lock (_multiplayerStatusGate)
            {
                // A bounded queue keeps a misbehaving transport from growing
                // UI-owned memory. The active request is always the newest
                // status for this controller; stale entries are ignored when
                // they are drained.
                while (_multiplayerStatusQueue.Count >= 64)
                {
                    _multiplayerStatusQueue.Dequeue();
                }

                _multiplayerStatusQueue.Enqueue(snapshot);
            }
        }

        private void DrainMultiplayerStatusQueue()
        {
            List<MultiplayerStatusSnapshot> pending = null;
            lock (_multiplayerStatusGate)
            {
                if (_multiplayerStatusQueue.Count == 0)
                {
                    return;
                }

                pending = new List<MultiplayerStatusSnapshot>(
                    _multiplayerStatusQueue);
                _multiplayerStatusQueue.Clear();
            }

            for (int i = 0; i < pending.Count; i++)
            {
                ApplyMultiplayerStatus(pending[i]);
            }
        }

        private void PollMultiplayerRollbackState()
        {
            if (!IsActive || !_hasMultiplayerAttempt ||
                _multiplayerRequestId.Length == 0 || !MultiplayerBridge.Active)
            {
                return;
            }

            WorkloadTransactionState state =
                WorkloadTransactionMultiplayer.Protocol.CurrentState;
            if (state == null ||
                !StringComparer.Ordinal.Equals(state.RequestId, _multiplayerRequestId))
            {
                return;
            }

            if (state.RequiresRollback ||
                state.TerminalState == WorkloadTransactionTerminalState.RollbackFailed)
            {
                _multiplayerRecoveryBlocked = true;
                _multiplayerCommitMessage = T("BWT_Workload_MultiplayerRecovery");
                SetMessage(MultiplayerStatusExplanation);
            }
        }

        private void ApplyMultiplayerStatus(MultiplayerStatusSnapshot status)
        {
            if (status == null || !_hasMultiplayerAttempt ||
                _multiplayerRequestId.Length == 0 ||
                !StringComparer.Ordinal.Equals(status.RequestId, _multiplayerRequestId))
            {
                return;
            }

            if (_multiplayerTerminalHandled)
            {
                return;
            }

            _multiplayerCommitState = status.State;
            _multiplayerCommitMessage =
                status.State != WorkloadMultiplayerCommitState.ExecutedAwaitingConfirmation &&
                status.Result != null &&
                (status.State == WorkloadMultiplayerCommitState.Succeeded ||
                 !status.Result.Succeeded)
                    ? WorkloadPresentationResolver.Resolve(status.Result)
                    : WorkloadPresentationResolver.Resolve(
                        status.Code,
                        status.Context);

            switch (status.State)
            {
                case WorkloadMultiplayerCommitState.Succeeded:
                    CompleteMultiplayerCommit(status);
                    return;
                case WorkloadMultiplayerCommitState.Rejected:
                case WorkloadMultiplayerCommitState.Aborted:
                case WorkloadMultiplayerCommitState.TimedOut:
                case WorkloadMultiplayerCommitState.RolledBack:
                    string failedDecision = MultiplayerDecisionLabel(_multiplayerDecision);
                    string failedMessage = _multiplayerCommitMessage;
                    _multiplayerRecoveryBlocked = false;
                    PrepareMultiplayerRetry();
                    SetMessage("BWT_Workload_MultiplayerNotSaved".Translate(
                        failedDecision,
                        failedMessage.AnyNonWhitespace()
                            ? failedMessage
                            : T("BWT_Workload_NoChangesMade")).ToString());
                    return;
                case WorkloadMultiplayerCommitState.RollbackFailed:
                    _multiplayerRecoveryBlocked = true;
                    SetMessage(MultiplayerStatusExplanation);
                    return;
                case WorkloadMultiplayerCommitState.Failed:
                    // PollMultiplayerRollbackState upgrades this to a locked
                    // recovery state when the protocol is still awaiting
                    // rollback reports. A confirmed persistence mutation with
                    // a failed UI rebase is a distinct local recovery block:
                    // keep the draft and old backend session untouched until
                    // the user cancels it.
                    if (status.Code == WorkloadDiagnosticCode.PersistenceConflict &&
                        status.Result?.PersistenceReceipt != null)
                    {
                        _previewRecoveryBlocked = true;
                        _multiplayerTerminalHandled = true;
                    }
                    SetMessage(MultiplayerStatusExplanation);
                    return;
                default:
                    SetMessage(MultiplayerStatusExplanation);
                    return;
            }
        }

        private void CompleteMultiplayerCommit(MultiplayerStatusSnapshot status)
        {
            if (_multiplayerTerminalHandled || !IsActive)
            {
                return;
            }

            if (_multiplayerDecision != WorkloadDecisionKind.Apply)
            {
                string expectedStableId = _multiplayerDecision == WorkloadDecisionKind.Fork
                    ? _multiplayerForkStableId
                    : SourceStableId;
                WorkloadOperationResult<WorkloadSession> rebased =
                    WorkloadGateway.RebaseV2PreviewAfterPersistence(
                        status?.Result?.PersistenceReceipt,
                        _multiplayerDecision,
                        expectedStableId,
                        _multiplayerForkLabel);
                WorkloadOperationResult<WorkloadSession> adopted =
                    rebased.Succeeded
                        ? WorkloadGateway.AdoptV2PreviewSession()
                        : rebased;
                if (!adopted.Succeeded || adopted.Value == null ||
                    !StringComparer.Ordinal.Equals(
                        adopted.Value.SourceTemplate.StableId,
                        expectedStableId))
                {
                    _previewRecoveryBlocked = true;
                    _multiplayerCommitState = WorkloadMultiplayerCommitState.Failed;
                    _multiplayerCommitMessage = T("BWT_Workload_PreviewRefreshFailed");
                    SetMessage(MultiplayerStatusExplanation);
                    return;
                }

                _session = adopted.Value;
                RebuildProjection(_session.ProjectedState);
                _multiplayerTerminalHandled = true;
                string saveMessage = status?.Result != null
                    ? WorkloadPresentationResolver.Resolve(status.Result)
                    : WorkloadPresentationResolver.Resolve(
                        status?.Code ?? WorkloadDiagnosticCode.InvalidState,
                        status?.Context);
                string confirmedDecision = MultiplayerDecisionLabel(_multiplayerDecision);
                ClearMultiplayerAttempt();
                SetMessage(
                    (saveMessage ?? string.Empty).AnyNonWhitespace()
                        ? saveMessage
                        : "BWT_Workload_MultiplayerSaved".Translate(confirmedDecision).ToString());
                return;
            }

            _multiplayerTerminalHandled = true;
            WorkloadOperationResult ended = WorkloadGateway.EndV2Preview();
            if (!ended.Succeeded && ended.Code != WorkloadDiagnosticCode.NotFound)
            {
                _multiplayerRecoveryBlocked = true;
                _multiplayerCommitState = WorkloadMultiplayerCommitState.Failed;
                _multiplayerCommitMessage = T("BWT_Workload_MultiplayerApplyCloseFailed");
                SetMessage(MultiplayerStatusExplanation);
                return;
            }

            string message = status?.Result != null
                ? WorkloadPresentationResolver.Resolve(status.Result)
                : WorkloadPresentationResolver.Resolve(
                    status?.Code ?? WorkloadDiagnosticCode.InvalidState,
                    status?.Context);
            ClearLocalSession();
            SetMessage(
                (message ?? string.Empty).AnyNonWhitespace()
                    ? message
                    : T("BWT_Workload_MultiplayerConfirmed"));
        }

        private void ClearMultiplayerAttempt()
        {
            _hasMultiplayerAttempt = false;
            _multiplayerRequestId = string.Empty;
            _multiplayerIdempotencyKey = string.Empty;
            _multiplayerPayloadFingerprint = string.Empty;
            _multiplayerDecision = WorkloadDecisionKind.Apply;
            _multiplayerForkStableId = string.Empty;
            _multiplayerForkLabel = string.Empty;
            _multiplayerCommitState = WorkloadMultiplayerCommitState.None;
            _multiplayerCommitMessage = string.Empty;
            _multiplayerRecoveryBlocked = false;
            _multiplayerTerminalHandled = false;
        }

        private void PrepareMultiplayerRetry()
        {
            // A terminal rejection/abort/timeout/rollback is a completed,
            // coherent operation. Retain its table entry for exact replay,
            // but let the unchanged draft create a new logical attempt with a
            // fresh idempotency key. This keeps a late retry from ever
            // reusing an ambiguous request while leaving the preview open.
            _hasMultiplayerAttempt = false;
            _multiplayerRequestId = string.Empty;
            _multiplayerIdempotencyKey = string.Empty;
            _multiplayerPayloadFingerprint = string.Empty;
            _multiplayerDecision = WorkloadDecisionKind.Apply;
            _multiplayerForkStableId = string.Empty;
            _multiplayerForkLabel = string.Empty;
            _multiplayerCommitState = WorkloadMultiplayerCommitState.None;
            _multiplayerCommitMessage = string.Empty;
            _multiplayerRecoveryBlocked = false;
            _multiplayerTerminalHandled = false;
        }

        private string CurrentMultiplayerPayloadFingerprint()
        {
            if (!IsActive)
            {
                return string.Empty;
            }

            WorkloadTemplate template;
            switch (_multiplayerDecision)
            {
                case WorkloadDecisionKind.Apply:
                    template = _session.BuildApplyTemplate();
                    break;
                case WorkloadDecisionKind.Update:
                    template = _session.TargetTemplate;
                    break;
                case WorkloadDecisionKind.Fork:
                    WorkloadSessionDecision fork = _session.Fork(
                        _multiplayerForkStableId,
                        _multiplayerForkLabel);
                    template = fork?.ResultTemplate;
                    break;
                default:
                    template = null;
                    break;
            }

            if (template == null)
            {
                return string.Empty;
            }

            return ((int)_multiplayerDecision).ToString() + "|" +
                   (_multiplayerForkStableId ?? string.Empty) + "|" +
                   (_multiplayerForkLabel ?? string.Empty) + "|" +
                   (template.SemanticFingerprint ?? string.Empty);
        }

        private void ResetCompletedMultiplayerAttemptIfPayloadChanged()
        {
            if (!_hasMultiplayerAttempt || IsMultiplayerCommitInFlight)
            {
                return;
            }

            string current = CurrentMultiplayerPayloadFingerprint();
            if (!StringComparer.Ordinal.Equals(
                    current,
                    _multiplayerPayloadFingerprint))
            {
                ClearMultiplayerAttempt();
            }
        }

        internal string SourceStableId => _session?.SourceTemplate?.StableId ?? string.Empty;
        internal string SourceIdentity => _session?.SourceIdentity ?? string.Empty;
        internal string SourceLabel => _session?.SourceTemplate?.Label ?? string.Empty;

        internal void PrepareFrame()
        {
            // Transport callbacks are consumed here, before the provider scope
            // is entered for this Work-tab pass. Never assume an AsyncLocal
            // effective-state scope exists on the callback thread.
            DrainMultiplayerStatusQueue();
            PollMultiplayerRollbackState();

            IWorkloadWorldState component = WorkloadWorldStates.Current;
            if (!ReferenceEquals(component, _boundComponent))
            {
                if (IsMultiplayerCommitInFlight)
                {
                    _multiplayerRecoveryBlocked = true;
                    _multiplayerCommitMessage = T("BWT_Workload_GameChanged");
                    SetMessage(MultiplayerStatusExplanation);
                }
                else
                {
                    _boundComponent = component;
                    ClearLocalSession();
                }
            }

            bool workloadsEnabled = BetterWorkTabMod.Settings?.enableWorkloads ?? true;
            if (!workloadsEnabled)
            {
                if (IsMultiplayerCommitInFlight)
                {
                    SetMessage(MultiplayerStatusExplanation);
                    return;
                }

                if (IsActive || WorkloadGateway.IsV2PreviewSessionActive)
                {
                    WorkloadGateway.CancelV2Preview();
                }

                ClearLocalSession();
                _queuedLifecycleActions.Clear();
                return;
            }

            if (WorkloadGateway.CurrentMode != WorkloadBackendMode.Modern)
            {
                if (IsMultiplayerCommitInFlight)
                {
                    SetMessage(MultiplayerStatusExplanation);
                    return;
                }

                if (IsActive || WorkloadGateway.IsV2PreviewSessionActive)
                {
                    WorkloadGateway.CancelV2Preview();
                }

                ClearLocalSession();
                _queuedLifecycleActions.Clear();
                return;
            }

            if (IsActive)
            {
                long pawnSetRevision = ComputeAvailablePawnSetRevision();
                if (pawnSetRevision != _projectedEditablePawnSetRevision)
                {
                    RebuildProjection(_session.ProjectedState);
                }
            }
        }

        internal IDisposable PushEffectiveStateScope()
        {
            IDisposable stateScope = WorkTabEffectiveStateScope.Push(ScopedProvider, this);
            try
            {
                return new EffectiveStateScope(
                    stateScope,
                    ParentPriorityRead.PushObservedPass(
                        _parentPriorityProjection,
                        TrySetPreviewParentPriority));
            }
            catch
            {
                stateScope.Dispose();
                throw;
            }
        }

        internal bool TrySetPreviewParentPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int priority)
        {
            string reason = null;
            if (!IsActive || _parentPriorityProjection == null ||
                !_parentPriorityProjection.TrySet(pawn, workType, priority, out reason))
            {
                if (!string.IsNullOrEmpty(reason))
                {
                    SetMessage(reason);
                }

                return false;
            }

            // The generic provider still owns the remaining draft dimensions
            // and session synchronization. Tell it about this boundary-owned
            // draft mutation without retaining a second parent index there.
            _projectedProvider.InvalidateDraft();
            return true;
        }

        internal void QueueLifecycleAction(
            Func<bool> action,
            Action<bool> completed = null)
        {
            if (action == null)
            {
                return;
            }

            _queuedLifecycleActions.Add(new QueuedLifecycleAction(action, completed));
        }

        internal void ResetLifecycleApplicationPublication()
        {
            _lifecycleApplicationPublication = WorkloadApplicationPublication.None;
        }

        internal void RecordLifecycleApplicationPublication(
            WorkloadApplicationPublication publication)
        {
            _lifecycleApplicationPublication = publication;
        }

        internal WorkloadApplicationPublication ConsumeLifecycleApplicationPublication()
        {
            WorkloadApplicationPublication publication =
                _lifecycleApplicationPublication;
            _lifecycleApplicationPublication = WorkloadApplicationPublication.None;
            return publication;
        }

        internal void FlushQueuedLifecycleActions()
        {
            if (_queuedLifecycleActions.Count == 0)
            {
                return;
            }

            List<QueuedLifecycleAction> actions =
                new List<QueuedLifecycleAction>(_queuedLifecycleActions);
            _queuedLifecycleActions.Clear();
            for (int i = 0; i < actions.Count; i++)
            {
                QueuedLifecycleAction queued = actions[i];
                bool succeeded = false;
                try
                {
                    succeeded = queued.Action();
                }
                catch (Exception exception)
                {
                    SetMessage(T("BWT_Workload_OperationFailed"));
                    Log.Error("[BWT] Workload preview lifecycle action failed.\n" + exception);
                }

                if (succeeded)
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
                else
                {
                    string message = LastMessage;
                    if (!message.AnyNonWhitespace())
                    {
                        message = T("BWT_Workload_OperationFailed");
                    }

                    Messages.Message(message, MessageTypeDefOf.RejectInput, false);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }

                queued.Completed?.Invoke(succeeded);
            }
        }

        internal bool SynchronizeAfterInput()
        {
            if (!IsActive)
            {
                return true;
            }

            long providerRevision = _projectedProvider?.ProjectionRevision ?? long.MinValue;
            if (providerRevision == _synchronizedProjectedProviderRevision)
            {
                return true;
            }

            if (IsUnsafePreviewInputBlocked)
            {
                // A third-party input owner may have run after the Work-tab
                // footer disabled its controls. Restore the detached draft so
                // no late projected edit can be carried into the transaction.
                if (!_session.ProjectedState.SemanticallyEquals(
                        _projectedProvider.ProjectedState,
                        WorkloadOwnershipDimensions.All))
                {
                    RebuildProjection(_session.ProjectedState);
                }
                else
                {
                    _synchronizedProjectedProviderRevision = providerRevision;
                }

                return true;
            }

            WorkloadProjectedState projected = _projectedProvider.ProjectedState;

            if (_session.ProjectedState.SemanticallyEquals(
                    projected,
                    WorkloadOwnershipDimensions.All))
            {
                _synchronizedProjectedProviderRevision = providerRevision;
                return true;
            }

            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.SetV2PreviewState(projected);
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                RebuildProjection(_session.ProjectedState);
                return false;
            }

            AcceptDraftReplacement(
                result.Value,
                _session.ProjectedState,
                projected,
                providerRevision);
            return true;
        }

        private void AcceptDraftReplacement(
            WorkloadSession accepted,
            WorkloadProjectedState previous,
            WorkloadProjectedState retainedProjection = null,
            long retainedProjectionRevision = long.MinValue)
        {
            if (accepted == null) return;
            if (previous != null && !previous.SemanticallyEquals(
                    accepted.ProjectedState,
                    WorkloadOwnershipDimensions.All))
            {
                _draftHistory.Record(new DraftTransition(previous, accepted.ProjectedState));
            }

            _session = accepted;
            BWTWorkloadSettingsOwnershipPolicy.ObservePreviewIdentity(
                _session?.PreviewStamp);
            if (retainedProjection != null &&
                _session.ProjectedState.SemanticallyEquals(
                    retainedProjection,
                    WorkloadOwnershipDimensions.All))
            {
                ClearInspectionIndex();
                _synchronizedProjectedProviderRevision = retainedProjectionRevision;
            }
            else
            {
                RebuildProjection(_session.ProjectedState);
            }
            ResetCompletedMultiplayerAttemptIfPayloadChanged();
        }

        internal bool BeginCurrentPreview()
        {
            if (IsMultiplayerCommitInFlight)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            if (WorkloadGateway.CurrentMode != WorkloadBackendMode.Modern)
            {
                SetMessage(T("BWT_Workload_PreviewLegacyUnavailable"));
                return false;
            }

            if (IsActive)
            {
                WorkloadSurfaceCoordinator.OpenPreview();
                return true;
            }

            WorkloadOperationResult<WorkloadSession> result = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.WorkloadPreview.OpenBackend",
                    () => WorkloadGateway.BeginV2Preview())
                : WorkloadGateway.BeginV2Preview();
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.WorkloadPreview.OpenSurface",
                    () => OpenSession(result.Value));
            }
            else
            {
                OpenSession(result.Value);
            }
            return true;
        }

        public bool TryHandleHistoryShortcut(Event evt)
        {
            if (evt == null || evt.type != EventType.KeyDown || !evt.control)
            {
                return false;
            }

            bool redo = evt.keyCode == KeyCode.Y ||
                        (evt.keyCode == KeyCode.Z && evt.shift);
            bool undo = evt.keyCode == KeyCode.Z && !evt.shift;
            if (!undo && !redo) return false;

            bool handled = false;
            if (IsActive)
            {
                handled = TryStepDraftHistory(redo);
                evt.Use();
                if (handled) SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return true;
            }

            if (undo && _canceledDraft != null)
            {
                handled = TryRestoreCanceledDraft();
                evt.Use();
                (handled ? SoundDefOf.Tick_High : SoundDefOf.ClickReject)
                    .PlayOneShotOnCamera();
                return true;
            }

            return false;
        }

        private bool TryStepDraftHistory(bool redo)
        {
            if (IsUnsafePreviewInputBlocked)
            {
                SetMessage(CommitBlockedMessage);
                return false;
            }

            DraftTransition transition = redo
                ? _draftHistory.PeekRedo
                : _draftHistory.PeekUndo;
            if (transition == null) return false;
            WorkloadProjectedState target = redo
                ? transition.After
                : transition.Before;
            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.SetV2PreviewState(target);
            if (!result.Succeeded || result.Value == null ||
                !result.Value.ProjectedState.SemanticallyEquals(
                    target,
                    WorkloadOwnershipDimensions.All))
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            _session = result.Value;
            RebuildProjection(_session.ProjectedState);
            ResetCompletedMultiplayerAttemptIfPayloadChanged();
            Predicate<DraftTransition> same = candidate =>
                ReferenceEquals(candidate, transition);
            return redo
                ? _draftHistory.CompleteRedo(same)
                : _draftHistory.CompleteUndo(same);
        }

        private bool TryRestoreCanceledDraft()
        {
            CanceledDraftRecovery recovery = _canceledDraft;
            WorkloadOperationResult<WorkloadSession> opened =
                WorkloadGateway.BeginV2Preview(recovery.SourceStableId);
            if (!opened.Succeeded || opened.Value == null)
            {
                ClearLocalSession();
                _canceledDraft = recovery;
                SetMessage(WorkloadPresentationResolver.Resolve(opened));
                return false;
            }
            if (!StringComparer.Ordinal.Equals(
                    opened.Value.SourceTemplate.StableId,
                    recovery.SourceStableId) ||
                !StringComparer.Ordinal.Equals(
                    opened.Value.SourceIdentity,
                    recovery.SourceIdentity))
            {
                WorkloadGateway.CancelV2Preview();
                ClearLocalSession();
                SetMessage(T("BWT_Workload_PreviewRefreshFailed"));
                return false;
            }

            WorkloadOperationResult<WorkloadSession> restored =
                WorkloadGateway.SetV2PreviewState(recovery.ProjectedState);
            if (!restored.Succeeded || restored.Value == null)
            {
                WorkloadGateway.CancelV2Preview();
                ClearLocalSession();
                _canceledDraft = recovery;
                SetMessage(WorkloadPresentationResolver.Resolve(restored));
                return false;
            }

            OpenSession(restored.Value, preserveDraftHistory: true);
            _draftHistory.Restore(recovery.Undo, recovery.Redo);
            _canceledDraft = null;
            return true;
        }

        internal bool HasStagedWorkloadChanges
        {
            get
            {
                if (!IsActive)
                {
                    return false;
                }

                SynchronizeAfterInput();
                return HasTemplateDiff ||
                       (_session.SessionExcludedPawnIds?.Count ?? 0) > 0;
            }
        }

        /// <summary>
        /// Workload list operations may leave a preview only when doing so will
        /// not discard user edits. A blocked operation keeps the session and
        /// reports the reason through the same Work-tab surface.
        /// </summary>
        internal bool CanLeavePreviewForWorkloadOperation(string operation)
        {
            if (IsMultiplayerCommitInFlight)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            if (!HasStagedWorkloadChanges)
            {
                return true;
            }

            SetMessage("BWT_Workload_FinishPreviewBefore"
                .Translate(operation ?? T("BWT_Workload_ChangingWorkloads"))
                .ToString());
            return false;
        }

        internal bool SelectWorkload(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                SetMessage(T("BWT_Workload_NotFound"));
                return false;
            }

            bool modern = WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern;
            if (modern && IsActive)
            {
                if (StringComparer.Ordinal.Equals(SourceStableId, stableId))
                {
                    return true;
                }

                SetMessage(ActivePreviewSwitchBlockedMessage);
                return false;
            }

            WorkloadOperationResult selected = WorkloadGateway.SelectWorkload(stableId);
            if (!selected.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(selected));
                return false;
            }

            if (modern)
            {
                return BeginCurrentPreview();
            }

            SetMessage(T("BWT_Workload_Selected"));
            return true;
        }

        internal bool CreateWorkload(string label, out WorkloadDescriptor descriptor)
        {
            descriptor = null;
            if (IsActive)
            {
                if (!CanLeavePreviewForWorkloadOperation(T("BWT_Workload_Creating")))
                {
                    return false;
                }

                CancelPreview();
            }

            WorkloadOperationResult<WorkloadDescriptor> result =
                WorkloadGateway.CreateWorkload(label);
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            descriptor = result.Value;
            // Creating a workload already captured the live Work-tab state in
            // WorkloadGateway.CreateWorkload. Do not immediately reopen it as
            // a projected preview: saving the current tab must not create a
            // second, ghosted presentation of the same state.
            SetMessage("BWT_Workload_Created".Translate(descriptor.Label).ToString());
            return true;
        }

        internal bool RenameWorkload(string stableId, string label)
        {
            if (string.IsNullOrEmpty(stableId) || string.IsNullOrWhiteSpace(label))
            {
                SetMessage(T("BWT_Workload_NameRequired"));
                return false;
            }

            bool reopenPreview = IsActive &&
                StringComparer.Ordinal.Equals(SourceStableId, stableId);
            if (reopenPreview)
            {
                if (!CanLeavePreviewForWorkloadOperation(T("BWT_Workload_Renaming")))
                {
                    return false;
                }

                CancelPreview();
            }

            WorkloadOperationResult result = WorkloadGateway.RenameWorkload(stableId, label.Trim());
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            if (reopenPreview && WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern)
            {
                if (!BeginCurrentPreview())
                {
                    return false;
                }
            }

            SetMessage(T("BWT_Workload_Renamed"));
            return true;
        }

        internal bool DeleteWorkload(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                SetMessage(T("BWT_Workload_NotFound"));
                return false;
            }

            bool wasPreviewSource = IsActive &&
                StringComparer.Ordinal.Equals(SourceStableId, stableId);
            if (wasPreviewSource)
            {
                if (!CanLeavePreviewForWorkloadOperation(T("BWT_Workload_Deleting")))
                {
                    return false;
                }

                CancelPreview();
            }

            WorkloadOperationResult result = WorkloadGateway.DeleteWorkload(stableId);
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            SetMessage(T("BWT_Workload_Deleted"));
            return true;
        }

        private bool BeginMultiplayerPreviewCommit(
            WorkloadDecisionKind decision,
            string forkStableId,
            string forkLabel)
        {
            if (!IsActive)
            {
                SetMessage(T("BWT_Workload_NoActivePreview"));
                return false;
            }

            if (IsMultiplayerCommitInFlight)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            string payloadFingerprint = CurrentMultiplayerPayloadFingerprint(
                decision,
                forkStableId,
                forkLabel);

            ClearMultiplayerAttempt();
            _hasMultiplayerAttempt = true;
            _multiplayerIdempotencyKey = Guid.NewGuid().ToString("N");
            _multiplayerDecision = decision;
            _multiplayerForkStableId = forkStableId ?? string.Empty;
            _multiplayerForkLabel = forkLabel ?? string.Empty;
            _multiplayerPayloadFingerprint = payloadFingerprint;
            _multiplayerCommitMessage = "BWT_Workload_MultiplayerWaiting"
                .Translate(MultiplayerDecisionLabel(decision))
                .ToString();

            WorkloadMultiplayerCommitStatus status =
                WorkloadGateway.BeginV2MultiplayerCommit(
                    decision,
                    _multiplayerForkStableId,
                    _multiplayerForkLabel,
                    _multiplayerIdempotencyKey);
            if (status == null)
            {
                _multiplayerCommitState = WorkloadMultiplayerCommitState.Rejected;
                _multiplayerCommitMessage = T("BWT_Workload_MultiplayerStartFailed");
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            _multiplayerRequestId = status.RequestId ?? string.Empty;
            _multiplayerCommitState = status.State;
            _multiplayerCommitMessage = WorkloadPresentationResolver.Resolve(status);
            bool accepted = status.IsAccepted;
            if (status.IsTerminal)
            {
                // Keep all session/provider transitions on the next
                // PrepareFrame even if a test transport reports synchronously.
                OnMultiplayerCommitStatusPublished(status);
            }

            if (!accepted)
            {
                SetMessage(MultiplayerStatusExplanation);
            }
            else
            {
                // The protocol owns the eventual application publication. Do
                // not let the footer emit a speculative table refresh while
                // the request is pending confirmation.
                RecordLifecycleApplicationPublication(
                    WorkloadApplicationPublication.Pending);
            }

            return accepted;
        }

        private string CurrentMultiplayerPayloadFingerprint(
            WorkloadDecisionKind decision,
            string forkStableId,
            string forkLabel)
        {
            WorkloadDecisionKind previousDecision = _multiplayerDecision;
            string previousForkStableId = _multiplayerForkStableId;
            string previousForkLabel = _multiplayerForkLabel;
            try
            {
                _multiplayerDecision = decision;
                _multiplayerForkStableId = forkStableId ?? string.Empty;
                _multiplayerForkLabel = forkLabel ?? string.Empty;
                return CurrentMultiplayerPayloadFingerprint();
            }
            finally
            {
                _multiplayerDecision = previousDecision;
                _multiplayerForkStableId = previousForkStableId;
                _multiplayerForkLabel = previousForkLabel;
            }
        }

        internal bool CancelPreview()
        {
            if (IsMultiplayerCommitInFlight)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            CanceledDraftRecovery recovery = null;
            if (IsActive && !SynchronizeAfterInput()) return false;
            if (IsActive && (HasTemplateDiff ||
                (_session.SessionExcludedPawnIds?.Count ?? 0) > 0))
            {
                _draftHistory.Capture(
                    out List<DraftTransition> undo,
                    out List<DraftTransition> redo);
                recovery = new CanceledDraftRecovery(
                    SourceStableId,
                    SourceIdentity,
                    _session.ProjectedState,
                    undo,
                    redo);
            }

            WorkloadOperationResult result = SpineTiming.Enabled
                ? SpineTiming.Time(
                    "WorkTab.WorkloadPreview.CloseBackend",
                    () => WorkloadGateway.CancelV2Preview())
                : WorkloadGateway.CancelV2Preview();
            if (!result.Succeeded && result.Code != WorkloadDiagnosticCode.NotFound)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.WorkloadPreview.CloseSurface",
                    ClearLocalSession);
            }
            else
            {
                ClearLocalSession();
            }
            if (result.Succeeded) _canceledDraft = recovery;
            SetMessage(T("BWT_Workload_PreviewCanceled"));
            return true;
        }

        internal bool ApplyPreview()
        {
            ResetLifecycleApplicationPublication();
            if (!IsActive)
            {
                SetMessage(T("BWT_Workload_NoActivePreview"));
                return false;
            }

            if (!SynchronizeAfterInput())
            {
                return false;
            }

            if (!CanApplyPreview)
            {
                SetMessage(CommitBlockedMessage.AnyNonWhitespace()
                    ? CommitBlockedMessage
                    : UnsupportedPresentationCommitReason);
                return false;
            }

            if (MultiplayerBridge.Active)
            {
                return BeginMultiplayerPreviewCommit(
                    WorkloadDecisionKind.Apply,
                    SourceStableId,
                    SourceLabel);
            }

            if (!CanApplyPreview)
            {
                SetMessage(UnsupportedPresentationCommitReason);
                return false;
            }
            WorkloadV2CommitResult result = WorkloadGateway.CommitV2Apply();
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            RecordLifecycleApplicationPublication(result.ApplicationPublication);
            ClearLocalSession();
            SetMessage(WorkloadPresentationResolver.Resolve(result));
            return true;
        }

        private bool AdoptRebasedPreview(WorkloadV2CommitResult result)
        {
            if (result == null || result.RebasedSession == null)
            {
                _previewRecoveryBlocked = true;
                SetMessage(T("BWT_Workload_PreviewRefreshFailed"));
                return false;
            }

            WorkloadOperationResult<WorkloadSession> adopted =
                WorkloadGateway.AdoptV2PreviewSession(result.RebasedSession);
            string expectedStableId = result.Report?.TargetStableId ?? result.StableId;
            if (!adopted.Succeeded || adopted.Value == null ||
                !StringComparer.Ordinal.Equals(
                    adopted.Value.SourceTemplate.StableId,
                    expectedStableId))
            {
                _previewRecoveryBlocked = true;
                SetMessage(T("BWT_Workload_PreviewRefreshFailed"));
                return false;
            }

            _session = adopted.Value;
            _draftHistory.Clear();
            _canceledDraft = null;
            _previewRecoveryBlocked = false;
            RebuildProjection(_session.ProjectedState);
            ResetCompletedMultiplayerAttemptIfPayloadChanged();
            return true;
        }

        internal bool UpdatePreview()
        {
            ResetLifecycleApplicationPublication();
            if (!IsActive)
            {
                SetMessage(T("BWT_Workload_NoActivePreview"));
                return false;
            }

            if (!SynchronizeAfterInput())
            {
                return false;
            }

            if (!HasSemanticDiff)
            {
                SetMessage(T("BWT_Workload_NothingToSave"));
                return false;
            }

            if (!CanUpdatePreview)
            {
                SetMessage(CommitBlockedMessage.AnyNonWhitespace()
                    ? CommitBlockedMessage
                    : UnsupportedPresentationCommitReason);
                return false;
            }

            if (MultiplayerBridge.Active)
            {
                return BeginMultiplayerPreviewCommit(
                    WorkloadDecisionKind.Update,
                    SourceStableId,
                    SourceLabel);
            }

            if (!HasSemanticDiff)
            {
                SetMessage(T("BWT_Workload_NothingToSave"));
                return false;
            }

            if (!CanUpdatePreview)
            {
                SetMessage(UnsupportedPresentationCommitReason);
                return false;
            }

            WorkloadV2CommitResult result = WorkloadGateway.CommitV2Update();
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            RecordLifecycleApplicationPublication(result.ApplicationPublication);
            if (!AdoptRebasedPreview(result))
            {
                return false;
            }

            SetMessage(WorkloadPresentationResolver.Resolve(result));
            return true;
        }

        internal bool ForkPreview(string label)
        {
            ResetLifecycleApplicationPublication();
            if (!IsActive)
            {
                SetMessage(T("BWT_Workload_NoActivePreviewForSaveAs"));
                return false;
            }

            if (!SynchronizeAfterInput())
            {
                return false;
            }

            if (!CanForkPreview)
            {
                SetMessage(CommitBlockedMessage.AnyNonWhitespace()
                    ? CommitBlockedMessage
                    : UnsupportedPresentationCommitReason);
                return false;
            }

            if (MultiplayerBridge.Active)
            {
                return BeginMultiplayerPreviewCommit(
                    WorkloadDecisionKind.Fork,
                    Guid.NewGuid().ToString("N"),
                    label);
            }

            if (!CanForkPreview)
            {
                SetMessage(UnsupportedPresentationCommitReason);
                return false;
            }
            string forkId = Guid.NewGuid().ToString("N");
            WorkloadV2CommitResult result = WorkloadGateway.CommitV2Fork(forkId, label);
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            RecordLifecycleApplicationPublication(result.ApplicationPublication);
            if (!AdoptRebasedPreview(result))
            {
                return false;
            }

                SetMessage(WorkloadPresentationResolver.Resolve(result));
            return true;
        }

        internal void ResetForWindowClose()
        {
            // Closing an inactive cached window must not cancel the preview owned by
            // another Work-tab instance.
            if (!ReferenceEquals(Current, this))
            {
                _inspectionActive = false;
                _queuedLifecycleActions.Clear();
                return;
            }

            if (IsMultiplayerCommitInFlight)
            {
                SetMessage(MultiplayerStatusExplanation);
                return;
            }

            if (IsActive)
            {
                CancelPreview();
            }
            else if (WorkloadGateway.IsV2PreviewSessionActive)
            {
                WorkloadGateway.CancelV2Preview();
                ClearLocalSession();
            }

            _inspectionActive = false;
            _queuedLifecycleActions.Clear();
        }

        internal string ActivePreviewSwitchBlockedMessage
        {
            get
            {
                if (IsMultiplayerCommitInFlight)
                {
                    return MultiplayerStatusExplanation;
                }

                if (_multiplayerCommitMessage.AnyNonWhitespace() &&
                    _hasMultiplayerAttempt)
                {
                    return _multiplayerCommitMessage + " " +
                           "BWT_Workload_FinishPreviewBefore"
                               .Translate(T("BWT_Workload_Switching"))
                               .ToString();
                }

                return T("BWT_Workload_FinishBeforeSwitching");
            }
        }

        internal void UpdateFooterInspectionHover(Rect updateRect)
        {
            UpdateFooterInspectionHover(Rect.zero, updateRect, Rect.zero);
        }

        internal void UpdateFooterInspectionHover(Rect updateRect, Rect applyRect)
        {
            UpdateFooterInspectionHover(Rect.zero, updateRect, applyRect);
        }

        internal void UpdateFooterInspectionHover(
            Rect saveAsRect,
            Rect updateRect,
            Rect applyRect)
        {
            if (!IsActive)
            {
                _inspectionActive = false;
                _inspectionContext = WorkloadInspectionContext.None;
                return;
            }

            Vector2 pointer = Event.current?.mousePosition ?? Vector2.zero;
            bool overApply = applyRect.width > 0f && applyRect.Contains(pointer);
            bool overSaveAs = saveAsRect.width > 0f && saveAsRect.Contains(pointer);
            bool overUpdate = updateRect.width > 0f && updateRect.Contains(pointer);
            if (overApply && HasLiveImpact)
            {
                _inspectionActive = true;
                _inspectionContext = WorkloadInspectionContext.Live;
            }
            else if (overSaveAs && HasTemplateDiff)
            {
                _inspectionActive = true;
                _inspectionContext = WorkloadInspectionContext.Template;
            }
            else if (overUpdate && HasTemplateDiff)
            {
                _inspectionActive = true;
                _inspectionContext = WorkloadInspectionContext.Template;
            }
            else
            {
                _inspectionActive = false;
                _inspectionContext = WorkloadInspectionContext.None;
            }
        }

        public bool IsInspectionRowLevelChanged(Pawn pawn)
        {
            EnsureInspectionIndex();
            return _inspectionReadState.IsRowLevelChanged(pawn);
        }

        public bool HasInspectionRowLevelChanges
        {
            get
            {
                if (!IsInspectionActive)
                {
                    return false;
                }

                EnsureInspectionIndex();
                return _inspectionReadState.HasRowLevelChanges;
            }
        }

        /// <summary>
        /// Applies a typed preview intent from a context-menu callback. These
        /// callbacks run outside the Work-tab provider scope, so they must
        /// cross the session gateway directly instead of using a scoped
        /// runtime editor that would otherwise fall through to live state.
        /// </summary>
        internal bool SetSpecificJobPreviewIntent(
            WorkloadSpecificJobTargetKey key,
            WorkloadIntent<WorkloadSpecificPriorityPayload> intent)
        {
            if (IsUnsafePreviewInputBlocked)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            if (!IsActive || key == null || !key.IsValid)
            {
                SetMessage(T("BWT_Workload_SpecificJobMissing"));
                return false;
            }

            if (!_session.SourceTemplate.Definition.OwnershipDimensions.Owns(
                    WorkloadStateDimension.SpecificJobOverrides))
            {
                SetMessage(T("BWT_Workload_SpecificJobPriorityUnavailable"));
                return false;
            }

            return EditPreviewDraft(
                draft => draft.SetSpecificPriorityIntent(key, intent),
                T("BWT_Workload_SpecificJobChangeFailed"));
        }

        internal bool SetWorkTypeOrderPreviewIntent(
            WorkloadWorkTypeOrderKey key,
            WorkloadIntent<WorkloadWorkTypeOrderPayload> intent)
        {
            if (IsUnsafePreviewInputBlocked)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            if (!IsActive || key == null || !key.IsValid)
            {
                SetMessage(T("BWT_Workload_SpecificJobOrderTargetMissing"));
                return false;
            }

            if (!_session.SourceTemplate.Definition.OwnershipDimensions.Owns(
                    WorkloadStateDimension.SpecificJobOrder))
            {
                SetMessage(T("BWT_Workload_SpecificJobOrderUnavailable"));
                return false;
            }

            return EditPreviewDraft(
                draft => draft.SetWorkTypeOrderIntent(key, intent),
                T("BWT_Workload_SpecificJobOrderChangeFailed"));
        }

        private bool EditPreviewDraft(
            Action<WorkloadDraft> edit,
            string failureMessage)
        {
            if (IsUnsafePreviewInputBlocked)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            WorkloadProjectedState previous = _session.ProjectedState;
            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.EditV2Preview(edit);
            if (!result.Succeeded || result.Value == null)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result, failureMessage));
                return false;
            }

            AcceptDraftReplacement(result.Value, previous);
            return true;
        }

        internal WorkloadMembershipSnapshot GetMembershipSnapshot()
        {
            // RebuildProjection asks for editable pawn IDs while constructing
            // the first projected provider. The session is already authoritative
            // at that point even though _projectedProvider has not been assigned
            // yet, so do not manufacture an empty boundary during construction.
            if (_session == null)
            {
                return new WorkloadMembershipSnapshot(null);
            }

            long pawnSetRevision = ComputeAvailablePawnSetRevision();
            if (_membershipSnapshot == null ||
                !StringComparer.Ordinal.Equals(
                    _membershipSnapshotSourceIdentity,
                    _session.SourceIdentity) ||
                _membershipSnapshotMembershipRevision != _session.MembershipRevision ||
                _membershipSnapshotPawnSetRevision != pawnSetRevision)
            {
                WorkloadTemplate template =
                    _session.SourceTemplate.WithState(_session.ProjectedState);
                WorkloadMembershipResult result =
                    WorkloadMembershipClassifier.Classify(template, BuildAvailableCandidates());
                _membershipSnapshot = new WorkloadMembershipSnapshot(result);
                _membershipSnapshotSourceIdentity = _session.SourceIdentity;
                _membershipSnapshotMembershipRevision = _session.MembershipRevision;
                _membershipSnapshotPawnSetRevision = pawnSetRevision;
            }

            return _membershipSnapshot;
        }

        public WorkGridPreviewMembershipAction GetMembershipAction(Pawn pawn)
        {
            return ResolveMembershipAction(
                IsActive,
                _session?.SourceTemplate?.Definition?.Scope,
                IsActive ? GetMembershipSnapshot() : null,
                pawn);
        }

        private static WorkGridPreviewMembershipAction ResolveMembershipAction(
            bool isActive,
            WorkloadScope scope,
            WorkloadMembershipSnapshot membership,
            Pawn pawn)
        {
            if (!isActive || pawn == null)
            {
                return WorkGridPreviewMembershipAction.None;
            }

            PawnKey pawnKey = WorkTabEffectiveStateIds.ForPawn(pawn);
            WorkloadScope effectiveScope = scope ?? WorkloadScope.Empty;
            if (pawnKey.IsValid && effectiveScope.IsExplicitlyExcluded(pawnKey))
            {
                return WorkGridPreviewMembershipAction.ExplicitlyExcluded;
            }

            WorkloadMembershipRecord record = pawnKey.IsValid
                ? membership?.Find(pawnKey)
                : null;
            if (!pawnKey.IsValid || record == null || !record.IsAvailable)
            {
                return WorkGridPreviewMembershipAction.PawnUnavailable;
            }

            if (record.Classification == WorkloadMembershipClassification.ExplicitlyExcluded ||
                record.Classification == WorkloadMembershipClassification.UnrepresentedNew)
            {
                return WorkGridPreviewMembershipAction.Include;
            }

            if (record.Classification == WorkloadMembershipClassification.Included &&
                record.IsRepresented)
            {
                return WorkGridPreviewMembershipAction.Exclude;
            }

            return record.Classification == WorkloadMembershipClassification.UnchangedOutsideScope
                ? WorkGridPreviewMembershipAction.OutsideScope
                : WorkGridPreviewMembershipAction.Unavailable;
        }

        private MembershipTooltipCatalog GetMembershipTooltipCatalog()
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            if (_membershipTooltipCatalog != null &&
                StringComparer.Ordinal.Equals(_membershipTooltipLanguage, language))
            {
                return _membershipTooltipCatalog;
            }

            _membershipTooltipLanguage = language;
            _membershipTooltipCatalog = new MembershipTooltipCatalog(
                T("BWT_Workload_PawnIncludedTooltip"),
                T("BWT_Workload_PawnNewTooltip"),
                T("BWT_Workload_PawnOutsideTooltip"),
                T("BWT_Workload_PawnExcludedTooltip"),
                T("BWT_Workload_PawnMissingTooltip"));
            return _membershipTooltipCatalog;
        }

        private static bool TryGetMembershipPresentation(
            WorkloadMembershipSnapshot membership,
            Pawn pawn,
            out WorkGridPreviewMembershipState state)
        {
            state = WorkGridPreviewMembershipState.None;
            PawnKey pawnKey = WorkTabEffectiveStateIds.ForPawn(pawn);
            if (membership == null || !pawnKey.IsValid ||
                !membership.TryGetClassification(
                    pawnKey,
                    out WorkloadMembershipClassification classification))
            {
                return false;
            }

            switch (classification)
            {
                case WorkloadMembershipClassification.Included:
                    state = WorkGridPreviewMembershipState.Included;
                    return true;
                case WorkloadMembershipClassification.UnrepresentedNew:
                    state = WorkGridPreviewMembershipState.New;
                    return true;
                case WorkloadMembershipClassification.UnchangedOutsideScope:
                    state = WorkGridPreviewMembershipState.OutsideScope;
                    return true;
                case WorkloadMembershipClassification.ExplicitlyExcluded:
                    state = WorkGridPreviewMembershipState.ExplicitlyExcluded;
                    return true;
                case WorkloadMembershipClassification.StaleMissing:
                    state = WorkGridPreviewMembershipState.Missing;
                    return true;
                default:
                    return false;
            }
        }

        internal bool ToggleMembership(PawnKey pawnKey)
        {
            if (IsUnsafePreviewInputBlocked)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            if (!IsActive || pawnKey == null || !pawnKey.IsValid)
            {
                SetMessage(T("BWT_Workload_PawnCannotChange"));
                return false;
            }

            WorkloadMembershipRecord record = GetMembershipSnapshot().Find(pawnKey);
            if (record == null || !record.IsAvailable)
            {
                SetMessage(T("BWT_Workload_PawnMissing"));
                return false;
            }

            WorkloadScope scope = _session.SourceTemplate.Definition.Scope ?? WorkloadScope.Empty;
            if (scope.IsExplicitlyExcluded(pawnKey))
            {
                SetMessage(T("BWT_Workload_PawnNeverIncluded"));
                return false;
            }

            if (_session.ProjectedState.IsExcluded(pawnKey))
            {
                if (!SetSessionMembership(pawnKey, include: true))
                {
                    return false;
                }

                SetMessage(T("BWT_Workload_PawnIncluded"));
                return true;
            }

            if (record.Classification == WorkloadMembershipClassification.UnrepresentedNew)
            {
                if (!AddCurrentLiveBaseline(pawnKey))
                {
                    return false;
                }

                SetMessage(T("BWT_Workload_PawnIncludedCurrent"));
                return true;
            }

            if (record.Classification == WorkloadMembershipClassification.Included &&
                record.IsRepresented)
            {
                if (!SetSessionMembership(pawnKey, include: false))
                {
                    return false;
                }

                SetMessage(T("BWT_Workload_PawnExcluded"));
                return true;
            }

            SetMessage(T("BWT_Workload_PawnOutside"));
            return false;
        }

        public bool ToggleMembership(Pawn pawn)
        {
            return ToggleMembership(
                pawn == null ? null : WorkTabEffectiveStateIds.ForPawn(pawn));
        }

        public bool EnsurePawnIncludedForPriorityEdit(Pawn pawn)
        {
            if (!IsActive)
            {
                return true;
            }

            PawnKey pawnKey = WorkTabEffectiveStateIds.ForPawn(pawn);
            WorkloadMembershipRecord record = pawnKey.IsValid
                ? GetMembershipSnapshot().Find(pawnKey)
                : null;
            WorkloadScope scope = _session.SourceTemplate.Definition.Scope ?? WorkloadScope.Empty;
            if (record == null ||
                !record.IsAvailable ||
                scope.IsExplicitlyExcluded(pawnKey) ||
                record.Classification == WorkloadMembershipClassification.UnchangedOutsideScope ||
                record.Classification == WorkloadMembershipClassification.StaleMissing)
            {
                SetMessage(T("BWT_Workload_PawnCannotChange"));
                return false;
            }

            if (record.Classification == WorkloadMembershipClassification.Included)
            {
                return true;
            }

            if (_session.ProjectedState.IsExcluded(pawnKey))
            {
                bool included = SetSessionMembership(pawnKey, include: true);
                if (included)
                {
                    SetMessage(T("BWT_Workload_PawnIncluded"));
                }

                return included;
            }

            if (record.Classification != WorkloadMembershipClassification.UnrepresentedNew ||
                !AddCurrentLiveBaseline(pawnKey))
            {
                return false;
            }

            SetMessage(T("BWT_Workload_PawnIncludedCurrent"));
            return true;
        }

        public bool TrySetManualMode(bool manualMode)
        {
            ProjectedWorkTabEffectiveStateProvider provider = _projectedProvider;
            if (!IsActive || provider == null ||
                (provider.OwnedDimensions & WorkloadOwnershipDimensions.ManualModes) == 0)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.ManualMode,
                    T("BWT_Workload_ModeUnavailable"));
                return false;
            }

            var keys = new List<WorkloadParentPriorityKey>();
            IReadOnlyList<WorkTypeDef> workTypes =
                DefDatabase<WorkTypeDef>.AllDefsListForReading;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
                {
                    continue;
                }

                PawnKey pawnKey = WorkTabEffectiveStateIds.ForPawn(pawn);
                if (!provider.CanEditPawn(pawnKey))
                {
                    continue;
                }

                for (int i = 0; i < workTypes.Count; i++)
                {
                    WorkTypeDef workType = workTypes[i];
                    if (workType != null)
                    {
                        keys.Add(new WorkloadParentPriorityKey(
                            pawnKey,
                            WorkTabEffectiveStateIds.ForWorkType(workType)));
                    }
                }
            }

            if (keys.Count == 0)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.ManualMode,
                    T("BWT_Workload_PawnCannotChange"));
                return false;
            }

            return WorkTabEffectiveStateRuntime.AcceptPreviewMutation(
                provider.SetManualModes(keys, manualMode));
        }

        private bool SetSessionMembership(PawnKey pawnKey, bool include)
        {
            WorkloadProjectedState previous = _session.ProjectedState;
            WorkloadSession changed = include
                ? _session.IncludePawn(pawnKey)
                : _session.ExcludePawn(pawnKey);
            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.SetV2PreviewState(changed.ProjectedState);
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            AcceptDraftReplacement(result.Value, previous);
            return true;
        }

        private bool AddCurrentLiveBaseline(PawnKey pawnKey)
        {
            Pawn pawn = ResolvePawn(pawnKey);
            if (pawn == null)
            {
                SetMessage(T("BWT_Workload_PawnMissing"));
                return false;
            }

            WorkloadOwnershipDimensions ownership =
                _session.SourceTemplate.Definition.OwnershipDimensions;

            var draft = new WorkloadDraft(_session.ProjectedState);
            bool capturedSchedule = false;
            bool wroteValue = WorkloadLiveCapture.CapturePawn(
                draft,
                pawn,
                ownership,
                false,
                ref capturedSchedule);

            if (!wroteValue)
            {
                SetMessage(T("BWT_Workload_PawnNoStoredSettings"));
                return false;
            }

            WorkloadProjectedState previous = _session.ProjectedState;
            WorkloadSession candidate = _session.EditState(draft.ProjectedState);
            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.ExtendV2PreviewBaseline(candidate, pawnKey);
            if (!result.Succeeded)
            {
                SetMessage(WorkloadPresentationResolver.Resolve(result));
                return false;
            }

            AcceptDraftReplacement(result.Value, previous);
            return true;
        }

        private void OpenSession(WorkloadSession session, bool preserveDraftHistory = false)
        {
            if (!preserveDraftHistory)
            {
                _draftHistory.Clear();
                _canceledDraft = null;
            }
            ClearMultiplayerAttempt();
            _previewRecoveryBlocked = false;
            _session = session;
            _boundComponent = WorkloadWorldStates.Current;
            ClaimCurrentOwnership();
            WorkloadSurfaceCoordinator.OpenPreview();

            // RimWorld may construct an inactive Work-tab window while another
            // window still owns the active preview. Bind settings to the
            // controller that actually opened the session, not to whichever
            // controller happened to be constructed most recently.
            BWTWorkloadSettingsOwnershipPolicy.RegisterPresentationPreviewPort(this);
            RebuildProjection(_session.ProjectedState);
            ColumnSelectionManager.Clear();
            BWTWorkTabTutorial.NotifyWorkloadPresentationOpened();
            SetMessage(T("BWT_Workload_PreviewOpened"));
        }

        private void RebuildProjection(WorkloadProjectedState projectedState)
        {
            BWTWorkloadSettingsOwnershipPolicy.ObservePreviewIdentity(
                _session?.PreviewStamp);
            if (_session == null)
            {
                _projectedProvider = null;
                _parentPriorityProjection = null;
                _projectedEditablePawnSetRevision = long.MinValue;
                _synchronizedProjectedProviderRevision = long.MinValue;
                ClearInspectionIndex();
                return;
            }

            var draft = new WorkloadDraft(projectedState ?? WorkloadProjectedState.Empty);
            IReadOnlyList<PawnKey> editablePawns = BuildEditablePawnIds();
            _projectedProvider = new ProjectedWorkTabEffectiveStateProvider(
                draft,
                _liveProvider,
                _session.SourceTemplate.Definition.OwnershipDimensions,
                "bwt.preview",
                _session.SourceTemplate.Definition.Scope,
                editablePawns);
            _parentPriorityProjection = new WorkloadParentPriorityProjection(
                draft,
                _session.SourceTemplate.Definition.OwnershipDimensions,
                editablePawns,
                () => _projectedProvider?.ProjectionRevision ?? long.MinValue);
            _synchronizedProjectedProviderRevision = _projectedProvider.ProjectionRevision;
            _projectedEditablePawnSetRevision = ComputeAvailablePawnSetRevision();
            ClearInspectionIndex();
        }

        private void ClearLocalSession()
        {
            WorkTabEffectiveStateRuntime.ClearPreviewCacheResidue();
            WorkloadSurfaceCoordinator.NotifyPreviewClosed();
            BWTWorkloadSettingsOwnershipPolicy.ObservePreviewIdentity(null);
            InvalidateMembershipSnapshot();
            _session = null;
            _projectedProvider = null;
            _parentPriorityProjection = null;
            _projectedEditablePawnSetRevision = long.MinValue;
            _synchronizedProjectedProviderRevision = long.MinValue;
            _inspectionActive = false;
            _inspectionContext = WorkloadInspectionContext.None;
            ClearInspectionIndex();
            ClearSemanticDiffCache();
            ClearMultiplayerAttempt();
            _previewRecoveryBlocked = false;
            _draftHistory.Clear();
            _canceledDraft = null;
        }

        private void SetMessage(string message)
        {
            string next = message ?? string.Empty;
            if (StringComparer.Ordinal.Equals(_lastMessage, next))
            {
                return;
            }

            _lastMessage = next;
            ClearCapturedPreviewView();
        }

        private void InvalidateMembershipSnapshot()
        {
            _membershipSnapshot = null;
            _membershipSnapshotSourceIdentity = string.Empty;
            _membershipSnapshotMembershipRevision = long.MinValue;
            _membershipSnapshotPawnSetRevision = long.MinValue;
            ClearCapturedPreviewView();
        }

        private void ClearCapturedPreviewView()
        {
            _capturedPreviewView = null;
            _capturedPreviewProvider = null;
            _capturedPreviewRevision = default;
            _capturedPreviewActive = false;
            _capturedPreviewMessage = string.Empty;
            _capturedPreviewMembership = null;
            _capturedPreviewInspection = null;
            _capturedPreviewScope = null;
            _capturedPreviewInspectionHighlightsEnabled = false;
            _capturedPreviewInspectionOpacity = 0f;
            _capturedPreviewMembershipTooltips = null;
        }

        private IReadOnlyList<PawnKey> BuildEditablePawnIds()
        {
            var editable = new List<PawnKey>();
            IReadOnlyList<WorkloadMembershipRecord> records =
                GetMembershipSnapshot().Records;
            for (int i = 0; i < records.Count; i++)
            {
                WorkloadMembershipRecord record = records[i];
                if (record != null &&
                    record.Classification == WorkloadMembershipClassification.Included &&
                    record.IsAvailable)
                {
                    editable.Add(record.PawnId);
                }
            }

            return editable.AsReadOnly();
        }

        private void EnsureSemanticDiffCache()
        {
            if (!IsActive)
            {
                ClearSemanticDiffCache();
                return;
            }

            if (ReferenceEquals(_semanticDiffSession, _session) &&
                _semanticDiffSessionRevision == _session.SessionRevision &&
                _cachedTemplateDiff != null &&
                _cachedLiveDiff != null)
            {
                return;
            }

            _cachedTemplateDiff = _session.TemplateDiff;
            _cachedLiveDiff = _session.LiveDiff;
            _semanticDiffSession = _session;
            _semanticDiffSessionRevision = _session.SessionRevision;
        }

        private void EnsureInspectionIndex()
        {
            if (!IsActive || _inspectionContext == WorkloadInspectionContext.None)
            {
                ClearInspectionIndex();
                return;
            }

            EnsureSemanticDiffCache();
            if (ReferenceEquals(_inspectionIndexSession, _session) &&
                _inspectionIndexSessionRevision == _session.SessionRevision &&
                _inspectionIndexContext == _inspectionContext)
            {
                return;
            }

            WorkloadSemanticDiff diff = _inspectionContext == WorkloadInspectionContext.Live
                ? _cachedLiveDiff
                : _cachedTemplateDiff;
            var changedInspectionTargets = new HashSet<WorkGridInspectionTarget>();
            var inspectionTargets = new List<WorkGridInspectionTarget>(16);
            var changedSchedulePawnIds = new HashSet<int>();
            bool hasInspectionCellTargets = false;
            bool hasManualModeInspectionChange = false;
            if (diff != null)
            {
                WorkloadProjectedState inspectionBaseline =
                    _inspectionContext == WorkloadInspectionContext.Live
                        ? _session.LiveBaselineState
                        : _session.TemplateBaselineState;
                hasManualModeInspectionChange =
                    WorkloadManualModeSemantics.TryGetGlobalMode(
                        inspectionBaseline, out bool baselineMode, out _, out _) &&
                    WorkloadManualModeSemantics.TryGetGlobalMode(
                        _session.ProjectedState, out bool projectedMode, out _, out _) &&
                    baselineMode != projectedMode;

                for (int i = 0; i < diff.Changes.Count; i++)
                {
                    WorkloadChange change = diff.Changes[i];
                    if (change == null ||
                        !TryDecodeChangeKey(
                            change.Dimension,
                            change.CanonicalKey,
                            out PawnKey pawn,
                            out WorkTypeKey workType,
                            out WorkGiverKey workGiver,
                            out WorkloadTargetScope scope,
                            out WorkloadScheduleTargetKind scheduleKind))
                    {
                        continue;
                    }

                    int pawnId = -1;
                    if (pawn != null && pawn.IsValid &&
                        int.TryParse(pawn.Value, out int parsedPawnId) && parsedPawnId > 0)
                    {
                        pawnId = parsedPawnId;
                    }

                    switch (change.Dimension)
                    {
                        case WorkloadStateDimension.ParentPriorities:
                            if (pawnId > 0 && workType != null && workType.IsValid)
                            {
                                AddInspectionTarget(changedInspectionTargets, inspectionTargets, new WorkGridInspectionTarget(
                                    WorkGridInspectionTargetKind.ParentPriority,
                                    false,
                                    false,
                                    pawnId,
                                    workType.Value,
                                    null));
                                hasInspectionCellTargets = true;
                            }
                            break;
                        case WorkloadStateDimension.ManualModes:
                            // Manual mode is globally effective. Its
                            // compatibility entries are intentionally not
                            // projected into pawn x WorkType cell targets.
                            break;
                        case WorkloadStateDimension.Schedules:
                            if (pawnId > 0)
                            {
                                changedSchedulePawnIds.Add(pawnId);
                            }
                            if (workType != null && workType.IsValid &&
                                (scope == WorkloadTargetScope.GlobalShared || pawnId > 0))
                            {
                                AddInspectionTarget(changedInspectionTargets, inspectionTargets, new WorkGridInspectionTarget(
                                    WorkGridInspectionTargetKind.Schedule,
                                    scope == WorkloadTargetScope.GlobalShared,
                                    scheduleKind == WorkloadScheduleTargetKind.WorkGiver,
                                    scope == WorkloadTargetScope.GlobalShared ? -1 : pawnId,
                                    workType.Value,
                                    workGiver?.Value));
                                hasInspectionCellTargets = true;
                            }
                            break;
                        case WorkloadStateDimension.SpecificJobOverrides:
                            if (workType != null && workType.IsValid &&
                                workGiver != null && workGiver.IsValid &&
                                (scope == WorkloadTargetScope.GlobalShared || pawnId > 0))
                            {
                                AddInspectionTarget(changedInspectionTargets, inspectionTargets, new WorkGridInspectionTarget(
                                    WorkGridInspectionTargetKind.SpecificPriority,
                                    scope == WorkloadTargetScope.GlobalShared,
                                    false,
                                    scope == WorkloadTargetScope.GlobalShared ? -1 : pawnId,
                                    workType.Value,
                                    workGiver.Value));
                                hasInspectionCellTargets = true;
                            }
                            break;
                        case WorkloadStateDimension.SpecificJobOrder:
                            if (workType != null && workType.IsValid &&
                                (scope == WorkloadTargetScope.GlobalShared || pawnId > 0))
                            {
                                AddInspectionTarget(changedInspectionTargets, inspectionTargets, new WorkGridInspectionTarget(
                                    WorkGridInspectionTargetKind.Ordering,
                                    scope == WorkloadTargetScope.GlobalShared,
                                    false,
                                    scope == WorkloadTargetScope.GlobalShared ? -1 : pawnId,
                                    workType.Value,
                                    null));
                                hasInspectionCellTargets = true;
                            }
                            break;
                        case WorkloadStateDimension.Membership:
                            // Membership is intentionally represented by the
                            // separate row indicator path; it never paints a
                            // priority-cell overlay.
                            break;
                    }
                }
            }

            _inspectionIndexSession = _session;
            _inspectionIndexSessionRevision = _session.SessionRevision;
            _inspectionIndexContext = _inspectionContext;
            _inspectionReadState = new InspectionReadState(
                IsInspectionActive,
                hasInspectionCellTargets,
                hasManualModeInspectionChange,
                inspectionTargets.AsReadOnly(),
                changedSchedulePawnIds);
        }

        private static void AddInspectionTarget(
            HashSet<WorkGridInspectionTarget> changedInspectionTargets,
            List<WorkGridInspectionTarget> inspectionTargets,
            WorkGridInspectionTarget target)
        {
            if (changedInspectionTargets.Add(target))
            {
                inspectionTargets.Add(target);
            }
        }

        private void ClearInspectionIndex()
        {
            _inspectionReadState = InspectionReadState.Empty;
            _inspectionIndexSession = null;
            _inspectionIndexSessionRevision = long.MinValue;
            _inspectionIndexContext = WorkloadInspectionContext.None;
            ClearCapturedPreviewView();
        }

        private void ClearSemanticDiffCache()
        {
            _semanticDiffSession = null;
            _semanticDiffSessionRevision = long.MinValue;
            _cachedTemplateDiff = null;
            _cachedLiveDiff = null;
        }

        private IReadOnlyList<PawnScopeCandidate> BuildAvailableCandidates()
        {
            return WorkloadPawnRosterCache.GetCandidates();
        }

        private static Pawn ResolvePawn(PawnKey key)
        {
            if (key == null || !int.TryParse(key.Value, out int thingId))
            {
                return null;
            }

            return WorkloadPawnRosterCache.ResolvePawn(thingId);
        }

        /// <summary>
        /// Revision of the candidate set used by the classifier. Engine-owned
        /// lifecycle hooks advance it immediately; a low-frequency audit catches
        /// external writers that bypass those hooks.
        /// </summary>
        private long ComputeAvailablePawnSetRevision()
        {
            return WorkloadPawnRosterCache.CurrentRevision;
        }

        private static bool TryDecodeChangeKey(
            WorkloadStateDimension dimension,
            string canonicalKey,
            out PawnKey pawn,
            out WorkTypeKey workType,
            out WorkGiverKey workGiver,
            out WorkloadTargetScope scope,
            out WorkloadScheduleTargetKind scheduleKind)
        {
            pawn = null;
            workType = null;
            workGiver = null;
            scope = WorkloadTargetScope.PawnLocal;
            scheduleKind = WorkloadScheduleTargetKind.ParentWorkType;

            bool typed = canonicalKey != null &&
                canonicalKey.StartsWith("intent:", StringComparison.Ordinal);
            string key = typed
                ? canonicalKey.Substring("intent:".Length)
                : canonicalKey;

            // Membership changes use an I:/E: marker followed by one encoded
            // pawn ID. They are row-level keys, not the length-prefixed pawn /
            // WorkType[/WorkGiver] tuples used by cell dimensions.
            if (dimension == WorkloadStateDimension.Membership)
            {
                if (string.IsNullOrEmpty(key) || key.Length < 3 ||
                    (key[0] != 'I' && key[0] != 'E') ||
                    key[1] != ':')
                {
                    return false;
                }

                int membershipCursor = 2;
                if (!TryReadCanonicalString(
                        key,
                        ref membershipCursor,
                        out string membershipPawn) ||
                    membershipCursor != key.Length)
                {
                    return false;
                }

                pawn = new PawnKey(membershipPawn);
                return pawn.IsValid;
            }

            if (dimension == WorkloadStateDimension.PresentationSettings)
            {
                // Presentation settings are not individual grid cells. Still
                // decode their typed shape so the inspection index can safely
                // ignore them instead of misclassifying the setting key as a
                // pawn ID.
                int presentationCursor = 0;
                return TryReadCanonicalString(
                           key,
                           ref presentationCursor,
                           out string unusedSetting) &&
                       presentationCursor == key.Length;
            }

            if (typed &&
                (dimension == WorkloadStateDimension.Schedules ||
                 dimension == WorkloadStateDimension.SpecificJobOverrides ||
                 dimension == WorkloadStateDimension.SpecificJobOrder))
            {
                int typedCursor = 0;
                if (!TryReadIntegerToken(key, ref typedCursor, out int scopeValue) ||
                    !Enum.IsDefined(typeof(WorkloadTargetScope), scopeValue))
                {
                    return false;
                }
                scope = (WorkloadTargetScope)scopeValue;

                if (dimension == WorkloadStateDimension.Schedules)
                {
                    if (!TryReadIntegerToken(key, ref typedCursor, out int kindValue) ||
                        !Enum.IsDefined(typeof(WorkloadScheduleTargetKind), kindValue))
                    {
                        return false;
                    }
                    scheduleKind = (WorkloadScheduleTargetKind)kindValue;
                }

                if (!TryReadCanonicalString(key, ref typedCursor, out string typedPawnValue) ||
                    !TryReadCanonicalString(key, ref typedCursor, out string typedWorkTypeValue))
                {
                    return false;
                }

                pawn = scope == WorkloadTargetScope.GlobalShared
                    ? null
                    : new PawnKey(typedPawnValue);
                workType = new WorkTypeKey(typedWorkTypeValue);

                if (dimension == WorkloadStateDimension.SpecificJobOrder)
                {
                    return workType.IsValid &&
                           typedCursor == key.Length &&
                           (scope == WorkloadTargetScope.GlobalShared || pawn.IsValid);
                }

                if (!TryReadCanonicalString(key, ref typedCursor, out string typedWorkGiverValue))
                {
                    return false;
                }

                workGiver = new WorkGiverKey(typedWorkGiverValue);
                return workType.IsValid &&
                       workGiver.IsValid &&
                       typedCursor == key.Length &&
                       (scope == WorkloadTargetScope.GlobalShared || pawn.IsValid);
            }

            int cursor = 0;
            if (!TryReadCanonicalString(key, ref cursor, out string pawnValue))
            {
                return false;
            }

            if (dimension == WorkloadStateDimension.Schedules)
            {
                pawn = new PawnKey(pawnValue);
                return pawn.IsValid;
            }

            if (!TryReadCanonicalString(key, ref cursor, out string workTypeValue))
            {
                return false;
            }

            // Legacy global specific-job keys are Pair(WorkType, WorkGiver),
            // while local keys are Triple(Pawn, WorkType, WorkGiver). The
            // second token is already the global WorkGiver; do not consume a
            // nonexistent third token before deciding which shape we have.
            if ((dimension == WorkloadStateDimension.SpecificJobOverrides ||
                 dimension == WorkloadStateDimension.SpecificJobOrder) &&
                cursor == key.Length)
            {
                scope = WorkloadTargetScope.GlobalShared;
                workType = new WorkTypeKey(pawnValue);
                workGiver = new WorkGiverKey(workTypeValue);
                pawn = null;
                return workType.IsValid && workGiver.IsValid;
            }

            pawn = new PawnKey(pawnValue);
            workType = new WorkTypeKey(workTypeValue);
            if (dimension != WorkloadStateDimension.SpecificJobOverrides &&
                dimension != WorkloadStateDimension.SpecificJobOrder)
            {
                return pawn.IsValid && workType.IsValid;
            }

            if (!TryReadCanonicalString(key, ref cursor, out string workGiverValue))
            {
                return false;
            }

            workGiver = new WorkGiverKey(workGiverValue);
            return pawn.IsValid && workType.IsValid && workGiver.IsValid && cursor == key.Length;
        }

        private static bool TryReadIntegerToken(
            string value,
            ref int cursor,
            out int result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value) || cursor < 0 || cursor >= value.Length)
            {
                return false;
            }

            int separator = value.IndexOf(':', cursor);
            if (separator <= cursor ||
                !int.TryParse(value.Substring(cursor, separator - cursor), out result))
            {
                return false;
            }

            cursor = separator + 1;
            return true;
        }

        private static bool TryReadCanonicalString(
            string value,
            ref int cursor,
            out string result)
        {
            result = string.Empty;
            if (string.IsNullOrEmpty(value) || cursor < 0 || cursor >= value.Length)
            {
                return false;
            }

            int separator = value.IndexOf(':', cursor);
            if (separator <= cursor || !int.TryParse(value.Substring(cursor, separator - cursor), out int length))
            {
                return false;
            }

            int start = separator + 1;
            if (length < 0 || start + length > value.Length)
            {
                return false;
            }

            result = value.Substring(start, length);
            cursor = start + length;
            return true;
        }

        private static string Trim(string value, int maxLength)
        {
            string safe = value ?? string.Empty;
            return safe.Length <= maxLength
                ? safe
                : safe.Substring(0, Math.Max(0, maxLength - 1)) + "...";
        }
    }
}
