using System;
using System.Collections.Generic;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.Workloads
{
    /// <summary>
    /// Coordinates the two workload-only inline surfaces that can be rendered
    /// by the Work tab. This is deliberately not a WindowStack/window owner:
    /// both surfaces remain part of the one Work-tab window and only one
    /// workload popover may own input at a time.
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
        private static Action _closePreview;
        private static Func<bool> _isPreviewActive;
        private static Action<string> _reportPreviewMessage;

        internal static void RegisterFooterCloser(Action closer)
        {
            _closeFooter = closer;
        }

        internal static void RegisterPreviewCloser(Action closer)
        {
            _closePreview = closer;
        }

        internal static void RegisterPreviewState(
            Func<bool> isActive,
            Action<string> reportMessage)
        {
            _isPreviewActive = isActive;
            _reportPreviewMessage = reportMessage;
        }

        /// <summary>
        /// The workload footer and modern preview are sibling surfaces in the
        /// one Work-tab window. A live preview owns the editing surface, so a
        /// footer click is rejected in-place rather than closing the preview
        /// or leaving an invisible footer hit region behind it.
        /// </summary>
        internal static bool TryOpenFooter()
        {
            if (_isPreviewActive?.Invoke() == true)
            {
                _reportPreviewMessage?.Invoke(
                    "Finish, Update, Save As, or Cancel the active workload preview before opening the workload list.");
                return false;
            }

            if (_owner == SurfaceOwner.Footer)
            {
                return true;
            }

            _closePreview?.Invoke();
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
        private static GameComponent_BWTWorldSettings _boundComponent;
        private static LegacyWorkloadBackend _legacyBackend;
        private static Workload2Backend _modernBackend;

        private const string NoCurrentGameMessage =
            "There is no current Better Work Tab game.";

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
                WorkloadDiagnosticCode.NoCurrentGame,
                NoCurrentGameMessage);
        }

        private static WorkloadOperationResult<T> NoCurrentGame<T>()
        {
            return WorkloadOperationResult<T>.Fail(
                WorkloadDiagnosticCode.NoCurrentGame,
                NoCurrentGameMessage);
        }

        private static WorkloadOperationResult V2Unavailable(string message)
        {
            return WorkloadOperationResult.Fail(
                WorkloadDiagnosticCode.UnsupportedOperation,
                message);
        }

        private static WorkloadOperationResult<T> V2Unavailable<T>(string message)
        {
            return WorkloadOperationResult<T>.Fail(
                WorkloadDiagnosticCode.UnsupportedOperation,
                message);
        }

        private static WorkloadV2CommitResult DispatchV2Commit(
            WorkloadDecisionKind decisionKind,
            Func<Workload2Backend, WorkloadV2CommitResult> modernOperation,
            string unavailableMessage)
        {
            return DispatchV2(
                modernOperation,
                () => WorkloadV2ApplyService.GatewayFailure(
                    decisionKind,
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "gateway.game",
                    NoCurrentGameMessage),
                () => WorkloadV2ApplyService.GatewayFailure(
                    decisionKind,
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "gateway.mode",
                    unavailableMessage));
        }

        internal static WorkloadBackendMode ResolveMode()
        {
            return BetterWorkTabMod.Settings?.useLegacyWorkloads == true
                ? WorkloadBackendMode.Legacy
                : WorkloadBackendMode.Modern;
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

        internal static WorkloadOperationResult<WorkloadTemplate> CaptureCurrentV2Template(
            string stableId,
            string label)
        {
            return DispatchV2(
                modern => modern.CaptureCurrentTemplate(stableId, label),
                NoCurrentGame<WorkloadTemplate>,
                () => V2Unavailable<WorkloadTemplate>(
                    "V2 capture is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadDescriptor> SaveV2Template(
            WorkloadTemplate template,
            bool makeCurrent)
        {
            return DispatchV2(
                modern => modern.SaveTemplate(template, makeCurrent),
                NoCurrentGame<WorkloadDescriptor>,
                () => V2Unavailable<WorkloadDescriptor>(
                    "V2 template storage is unavailable while legacy workloads are active."));
        }

        /// <summary>
        /// Changes the setting only after checking the modern preview boundary.
        /// A blocked transition leaves useLegacyWorkloads untouched.
        /// </summary>
        internal static WorkloadOperationResult TryTransitionMode(WorkloadBackendMode targetMode)
        {
            if (targetMode != WorkloadBackendMode.Legacy && targetMode != WorkloadBackendMode.Modern)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "The requested workload mode is not supported.");
            }

            WorkloadBackendMode currentMode = ResolveMode();
            if (currentMode == targetMode)
            {
                return WorkloadOperationResult.Ok();
            }

            TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern);
            if (modern != null && modern.IsPreviewSessionActive)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.BlockedModeTransition,
                    "Close the active V2 preview before changing workload mode.");
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoSettings,
                    "Better Work Tab settings are not loaded.");
            }

            settings.useLegacyWorkloads = targetMode == WorkloadBackendMode.Legacy;
            settings.Write();
            return WorkloadOperationResult.Ok();
        }

        internal static WorkloadOperationResult TrySetLegacyMode(bool useLegacy)
        {
            return TryTransitionMode(useLegacy ? WorkloadBackendMode.Legacy : WorkloadBackendMode.Modern);
        }

        internal static WorkloadOperationResult<WorkloadSession> BeginV2Preview()
        {
            return DispatchV2(
                modern => modern.BeginPreview(),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>(
                    "V2 preview is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadSession> SetV2PreviewSession(WorkloadSession session)
        {
            return DispatchV2(
                modern => modern.SetPreviewSession(session),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>(
                    "V2 preview is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadSession> EditV2Preview(
            Action<WorkloadDraft> edit)
        {
            return DispatchV2(
                modern => modern.EditPreview(edit),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>(
                    "V2 preview editing is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadSession> SetV2PreviewState(
            WorkloadProjectedState projectedState)
        {
            return DispatchV2(
                modern => modern.SetPreviewState(projectedState),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>(
                    "V2 preview editing is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadSession> RevertV2Preview()
        {
            return DispatchV2(
                modern => modern.RevertPreview(),
                NoCurrentGame<WorkloadSession>,
                () => V2Unavailable<WorkloadSession>(
                    "V2 preview editing is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadPreviewPlan> GetV2PreviewPlan(
            WorkloadDecisionKind decisionKind = WorkloadDecisionKind.Apply)
        {
            return DispatchV2(
                modern => modern.PreviewPlan(decisionKind),
                NoCurrentGame<WorkloadPreviewPlan>,
                () => V2Unavailable<WorkloadPreviewPlan>(
                    "V2 preview planning is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadSemanticDiff> GetV2PreviewDiff()
        {
            return DispatchV2(
                modern => modern.PreviewDiff(),
                NoCurrentGame<WorkloadSemanticDiff>,
                () => V2Unavailable<WorkloadSemanticDiff>(
                    "V2 preview diff is unavailable while legacy workloads are active."));
        }

        internal static WorkloadOperationResult<WorkloadSemanticDiff> GetV2PreviewImpactDiff()
        {
            return DispatchV2(
                modern => modern.PreviewImpactDiff(),
                NoCurrentGame<WorkloadSemanticDiff>,
                () => V2Unavailable<WorkloadSemanticDiff>(
                    "V2 preview impact diff is unavailable while legacy workloads are active."));
        }

        internal static WorkloadV2CommitResult CommitV2Apply()
        {
            return DispatchV2Commit(
                WorkloadDecisionKind.Apply,
                modern => modern.CommitApply(),
                "V2 apply is unavailable while legacy workloads are active.");
        }

        internal static WorkloadV2CommitResult CommitV2Update()
        {
            return DispatchV2Commit(
                WorkloadDecisionKind.Update,
                modern => modern.CommitUpdate(),
                "V2 update is unavailable while legacy workloads are active.");
        }

        internal static WorkloadV2CommitResult CommitV2Fork(
            string stableId,
            string label)
        {
            return DispatchV2Commit(
                WorkloadDecisionKind.Fork,
                modern => modern.CommitFork(stableId, label),
                "V2 fork is unavailable while legacy workloads are active.");
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
            GameComponent_BWTWorldSettings component =
                Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
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
    internal sealed class WorkloadPreviewController
    {
        private const float PreviewRailWidth = 320f;
        private const float PreviewRailMinimumGridWidth = 240f;
        private const float PreviewRailMinimumWidth = 220f;
        private const float PreviewRailBottomClearance = 52f;

        private readonly BwtLiveWorkTabEffectiveStateAdapter _liveAdapter;
        private readonly LiveWorkTabEffectiveStateProvider _liveProvider;
        private WorkloadSession _session;
        private ProjectedWorkTabEffectiveStateProvider _projectedProvider;
        private GameComponent_BWTWorldSettings _boundComponent;
        private string _inspectionFingerprint = string.Empty;
        private readonly HashSet<WorkloadParentPriorityKey> _changedParentKeys =
            new HashSet<WorkloadParentPriorityKey>();
        private readonly HashSet<WorkloadSpecificJobKey> _changedSpecificJobKeys =
            new HashSet<WorkloadSpecificJobKey>();
        private readonly HashSet<PawnKey> _changedSchedulePawns =
            new HashSet<PawnKey>();
        private readonly HashSet<PawnKey> _changedPawns =
            new HashSet<PawnKey>();
        private WorkloadMembershipSnapshot _membershipSnapshot;
        private WorkloadSession _membershipSnapshotSession;
        private long _membershipSnapshotProjectionRevision = long.MinValue;
        private long _membershipSnapshotPawnSetRevision = long.MinValue;

        private enum InlineSurfacePopoverKind
        {
            None,
            Fork,
            Membership
        }

        private Rect _surfaceRect;
        private Rect _applyRect;
        private Rect _cancelRect;
        private Rect _updateRect;
        private Rect _forkRect;
        private Rect _membershipRect;
        private Rect _inspectionPopoverRect;
        private Rect _inlineSurfacePopoverRect;
        private InlineSurfacePopoverKind _inlineSurfacePopover;
        private string _inlineSurfaceName = string.Empty;
        private Vector2 _membershipScroll;
        private bool _inspectionActive;
        private string _lastMessage = string.Empty;
        private int _surfaceReadyFrame;

        internal WorkloadPreviewController()
        {
            _liveAdapter = new BwtLiveWorkTabEffectiveStateAdapter();
            _liveProvider = _liveAdapter.CreateProvider("bwt.live.workload-preview");
            WorkloadSurfaceCoordinator.RegisterPreviewCloser(CloseInlineSurfacePopover);
            WorkloadSurfaceCoordinator.RegisterPreviewState(
                () => IsActive,
                message => SetMessage(message));
            Current = this;
        }

        internal static WorkloadPreviewController Current { get; private set; }

        internal static bool IsInspectionActiveForCurrentTab =>
            Current?.IsInspectionActive == true;

        // Contextual-settings integration can consume this without opening a
        // second settings surface or taking ownership of the preview session.
        internal static string CurrentSettingsIntegrationBanner =>
            Current?.SettingsIntegrationStatus ?? string.Empty;

        internal WorkloadSession Session => _session;
        internal ProjectedWorkTabEffectiveStateProvider ProjectedProvider => _projectedProvider;
        internal IWorkTabEffectiveStateProvider ScopedProvider =>
            _projectedProvider ?? (IWorkTabEffectiveStateProvider)_liveProvider;

        internal bool IsActive => _session != null && _projectedProvider != null;
        internal bool HasTemplateDiff => IsActive && !_session.TemplateDiff.IsEmpty;
        internal bool HasLiveImpact => IsActive && !_session.LiveDiff.IsEmpty;
        // Kept as the template-dirty alias for existing callers. Update and
        // inspection are deliberately about the stored template, not the
        // current colony baseline.
        internal bool HasSemanticDiff => HasTemplateDiff;
        internal bool IsInspectionActive => IsActive && _inspectionActive && HasTemplateDiff;
        internal string LastMessage => _lastMessage ?? string.Empty;

        /// <summary>
        /// Returns the Work-tab content rectangle after reserving the workload
        /// rail. The normal grid is laid out beside the rail, so the preview
        /// controls never paint over pawn rows or headers.
        /// </summary>
        internal Rect GetWorkGridRect(Rect inRect)
        {
            float railWidth = GetPreviewRailWidth(inRect);
            if (railWidth <= 0f)
            {
                return inRect;
            }

            return new Rect(
                inRect.xMin + railWidth,
                inRect.yMin,
                Mathf.Max(1f, inRect.width - railWidth),
                inRect.height);
        }

        private static float GetPreviewRailWidth(Rect inRect)
        {
            if (Current == null || !Current.IsActive)
            {
                return 0f;
            }

            float available = Mathf.Max(0f, inRect.width);
            if (available < PreviewRailMinimumGridWidth + PreviewRailMinimumWidth)
            {
                return 0f;
            }

            return Mathf.Min(
                PreviewRailWidth,
                Mathf.Max(PreviewRailMinimumWidth, available - PreviewRailMinimumGridWidth));
        }

        private float GetPreviewPanelWidth(Rect inRect)
        {
            return Mathf.Max(1f, GetPreviewRailWidth(inRect) - 12f);
        }

        private float GetPreviewPanelHeight(float panelWidth)
        {
            List<List<PreviewButtonKind>> rows = BuildPreviewButtonRows(
                Mathf.Max(1f, panelWidth),
                HasSemanticDiff);
            return 68f +
                   (rows.Count * 22f) +
                   (Mathf.Max(0, rows.Count - 1) * 4f) +
                   6f;
        }

        // Update/Fork compare against the stored template, while Apply compares
        // against the live colony baseline captured when the preview opened.
        // Keep both counts visible so a workload that is dirty as a template
        // cannot be mistaken for one that will change the colony by the same
        // number of entries.
        internal int ColonyImpactCount => IsActive ? _session.LiveDiff.Changes.Count : 0;
        internal int TemplateDirtyCount => IsActive ? _session.TemplateDiff.Changes.Count : 0;

        /// <summary>
        /// The current runtime has writers only for parent priorities and the
        /// global manual-mode bridge. Keep all other owned dimensions visibly
        /// unavailable when they contain state (or a rejected clear), rather
        /// than presenting actions that can only fail after a click.
        /// </summary>
        internal bool HasUnsupportedOwnedPresentationState
        {
            get
            {
                if (!IsActive || _session.SourceTemplate?.Definition == null)
                {
                    return false;
                }

                WorkloadOwnershipDimensions ownership =
                    _session.SourceTemplate.Definition.OwnershipDimensions;
                WorkloadStateDimension[] unsupported =
                {
                    WorkloadStateDimension.Schedules,
                    WorkloadStateDimension.SpecificJobOverrides,
                    WorkloadStateDimension.SpecificJobOrder,
                    WorkloadStateDimension.PresentationSettings
                };
                WorkloadSemanticDiff templateDiff = _session.TemplateDiff;
                WorkloadSemanticDiff liveDiff = _session.LiveDiff;
                IReadOnlyList<WorkloadStateDimension> clearDimensions =
                    _session.UnsupportedClearDimensions;

                for (int i = 0; i < unsupported.Length; i++)
                {
                    WorkloadStateDimension dimension = unsupported[i];
                    if (!ownership.Owns(dimension))
                    {
                        continue;
                    }

                    if (ContainsDimension(templateDiff, dimension) ||
                        ContainsDimension(liveDiff, dimension) ||
                        ContainsDimension(clearDimensions, dimension) ||
                        HasStateEntries(_session.TemplateBaselineState, dimension) ||
                        HasStateEntries(_session.ProjectedState, dimension))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private string UnsupportedPresentationCommitReason =>
            "This workload contains an owned dimension without a supported runtime writer. " +
            "Apply, Update, and Save As are disabled so state cannot be silently dropped.";

        private bool CanApplyPreview => IsActive && !HasUnsupportedOwnedPresentationState;
        private bool CanUpdatePreview => IsActive && HasSemanticDiff && !HasUnsupportedOwnedPresentationState;
        private bool CanForkPreview => IsActive && !HasUnsupportedOwnedPresentationState;

        private static bool ContainsDimension(
            WorkloadSemanticDiff diff,
            WorkloadStateDimension dimension)
        {
            if (diff?.Changes == null)
            {
                return false;
            }

            for (int i = 0; i < diff.Changes.Count; i++)
            {
                if (diff.Changes[i].Dimension == dimension)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsDimension(
            IReadOnlyList<WorkloadStateDimension> dimensions,
            WorkloadStateDimension dimension)
        {
            if (dimensions == null)
            {
                return false;
            }

            for (int i = 0; i < dimensions.Count; i++)
            {
                if (dimensions[i] == dimension)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasStateEntries(
            WorkloadProjectedState state,
            WorkloadStateDimension dimension)
        {
            if (state == null)
            {
                return false;
            }

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

        internal string SourceStableId => _session?.SourceTemplate?.StableId ?? string.Empty;
        internal string SourceLabel => _session?.SourceTemplate?.Label ?? string.Empty;

        internal string SettingsIntegrationStatus
        {
            get
            {
                return IsActive
                    ? "Workload preview is active; Alt-click continues through BWT contextual settings."
                    : string.Empty;
            }
        }

        internal int IncludedCount => GetMembershipCount(WorkloadMembershipClassification.Included);
        internal int UnchangedOutsideScopeCount =>
            GetMembershipCount(WorkloadMembershipClassification.UnchangedOutsideScope);
        internal int UnrepresentedNewCount =>
            GetMembershipCount(WorkloadMembershipClassification.UnrepresentedNew);
        internal int ExplicitlyExcludedCount =>
            GetMembershipCount(WorkloadMembershipClassification.ExplicitlyExcluded);
        internal int StaleMissingCount =>
            GetMembershipCount(WorkloadMembershipClassification.StaleMissing);

        internal string DataAuthorityLabel
        {
            get
            {
                return PriorityAuthorityBroker.CurrentAuthority == PriorityAuthorityOwner.BetterWorkTab
                    ? "BWT owns priority data"
                    : "External priority authority: " +
                      PriorityAuthorityBroker.CurrentAuthority;
            }
        }

        internal string UnsupportedDimensionsLabel
        {
            get
            {
                if (!IsActive)
                {
                    return string.Empty;
                }

                WorkloadOwnershipDimensions ownership =
                    _session.SourceTemplate.Definition.OwnershipDimensions;
                var blocked = new List<string>();
                AddUnsupportedDimension(
                    blocked,
                    ownership,
                    WorkloadStateDimension.Schedules,
                    "schedules");
                AddUnsupportedDimension(
                    blocked,
                    ownership,
                    WorkloadStateDimension.SpecificJobOverrides,
                    "specific-job overrides");
                AddUnsupportedDimension(
                    blocked,
                    ownership,
                    WorkloadStateDimension.SpecificJobOrder,
                    "specific-job order");
                AddUnsupportedDimension(
                    blocked,
                    ownership,
                    WorkloadStateDimension.PresentationSettings,
                    HasUnsupportedOwnedPresentationState
                        ? "presentation settings (preview-only; commit actions disabled)"
                        : "presentation settings");
                return blocked.Count == 0
                    ? string.Empty
                    : "Unsupported dimensions: " + string.Join(", ", blocked.ToArray());
            }
        }

        internal string ValidationLabel
        {
            get
            {
                if (!IsActive || _session.Validation == null || !_session.Validation.HasErrors)
                {
                    return string.Empty;
                }

                var messages = new List<string>();
                for (int i = 0; i < _session.Validation.Issues.Count && messages.Count < 2; i++)
                {
                    WorkloadValidationIssue issue = _session.Validation.Issues[i];
                    if (issue?.Message.AnyNonWhitespace() == true)
                    {
                        messages.Add(issue.Message);
                    }
                }

                return messages.Count == 0
                    ? "Blocked: workload validation failed."
                    : "Blocked: " + string.Join(" | ", messages.ToArray());
            }
        }

        internal void PrepareFrame()
        {
            GameComponent_BWTWorldSettings component =
                Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (!ReferenceEquals(component, _boundComponent))
            {
                _boundComponent = component;
                ClearLocalSession();
            }

            if (WorkloadGateway.CurrentMode != WorkloadBackendMode.Modern)
            {
                if (IsActive || WorkloadGateway.IsV2PreviewSessionActive)
                {
                    WorkloadGateway.CancelV2Preview();
                }

                ClearLocalSession();
            }
        }

        internal IDisposable PushEffectiveStateScope()
        {
            return WorkTabEffectiveStateScope.Push(ScopedProvider);
        }

        internal void SynchronizeAfterInput()
        {
            if (!IsActive)
            {
                return;
            }

            WorkloadProjectedState projected = _projectedProvider.ProjectedState;

            WorkloadOwnershipDimensions ownership =
                _session.SourceTemplate.Definition.OwnershipDimensions;
            if (_session.ProjectedState.SemanticallyEquals(projected, ownership))
            {
                return;
            }

            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.SetV2PreviewState(projected);
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                RebuildProjection(_session.ProjectedState);
                return;
            }

            _session = result.Value;
            RebuildProjection(_session.ProjectedState);
        }

        internal bool BeginCurrentPreview()
        {
            if (WorkloadGateway.CurrentMode != WorkloadBackendMode.Modern)
            {
                SetMessage("Modern workload preview is unavailable in legacy mode.");
                return false;
            }

            if (IsActive)
            {
                WorkloadSurfaceCoordinator.OpenPreview();
                return true;
            }

            WorkloadOperationResult<WorkloadSession> result = WorkloadGateway.BeginV2Preview();
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            OpenSession(result.Value);
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
            if (!HasStagedWorkloadChanges)
            {
                return true;
            }

            SetMessage(
                "Finish, Update, Save As, or Cancel the active workload preview " +
                "before " + (operation ?? "changing workloads") + ".");
            return false;
        }

        internal bool SelectWorkload(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                SetMessage("The workload stable ID is missing.");
                return false;
            }

            bool modern = WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern;
            if (modern && IsActive)
            {
                if (StringComparer.Ordinal.Equals(SourceStableId, stableId))
                {
                    return true;
                }

                if (!CanLeavePreviewForWorkloadOperation("switching workloads"))
                {
                    return false;
                }

                CancelPreview();
            }

            WorkloadOperationResult selected = WorkloadGateway.SelectWorkload(stableId);
            if (!selected.Succeeded)
            {
                SetMessage(selected.Message);
                return false;
            }

            if (modern)
            {
                return BeginCurrentPreview();
            }

            SetMessage("Selected workload.");
            return true;
        }

        internal bool CreateWorkload(string label, out WorkloadDescriptor descriptor)
        {
            descriptor = null;
            if (IsActive)
            {
                if (!CanLeavePreviewForWorkloadOperation("creating a workload"))
                {
                    return false;
                }

                CancelPreview();
            }

            WorkloadOperationResult<WorkloadDescriptor> result =
                WorkloadGateway.CreateWorkload(label);
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            descriptor = result.Value;
            if (WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern &&
                !BeginCurrentPreview())
            {
                return false;
            }

            SetMessage("Created workload " + descriptor.Label + ".");
            return true;
        }

        internal bool RenameWorkload(string stableId, string label)
        {
            if (string.IsNullOrEmpty(stableId) || string.IsNullOrWhiteSpace(label))
            {
                SetMessage("A workload name is required.");
                return false;
            }

            bool reopenPreview = IsActive &&
                StringComparer.Ordinal.Equals(SourceStableId, stableId);
            if (reopenPreview)
            {
                if (!CanLeavePreviewForWorkloadOperation("renaming this workload"))
                {
                    return false;
                }

                CancelPreview();
            }

            WorkloadOperationResult result = WorkloadGateway.RenameWorkload(stableId, label.Trim());
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            if (reopenPreview && WorkloadGateway.CurrentMode == WorkloadBackendMode.Modern)
            {
                if (!BeginCurrentPreview())
                {
                    return false;
                }
            }

            SetMessage("Renamed workload.");
            return true;
        }

        internal bool DeleteWorkload(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                SetMessage("The workload stable ID is missing.");
                return false;
            }

            bool wasPreviewSource = IsActive &&
                StringComparer.Ordinal.Equals(SourceStableId, stableId);
            if (wasPreviewSource)
            {
                if (!CanLeavePreviewForWorkloadOperation("deleting this workload"))
                {
                    return false;
                }

                CancelPreview();
            }

            WorkloadOperationResult result = WorkloadGateway.DeleteWorkload(stableId);
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            SetMessage("Deleted workload.");
            return true;
        }

        internal bool CancelPreview()
        {
            WorkloadOperationResult result = WorkloadGateway.CancelV2Preview();
            ClearLocalSession();
            if (!result.Succeeded && result.Code != WorkloadDiagnosticCode.NotFound)
            {
                SetMessage(result.Message);
                return false;
            }

            SetMessage("Preview canceled; live work priorities were unchanged.");
            return true;
        }

        internal bool ApplyPreview()
        {
            if (!IsActive)
            {
                SetMessage("There is no active workload preview.");
                return false;
            }

            if (!CanApplyPreview)
            {
                SetMessage(UnsupportedPresentationCommitReason);
                return false;
            }

            SynchronizeAfterInput();
            WorkloadV2CommitResult result = WorkloadGateway.CommitV2Apply();
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            ClearLocalSession();
            SetMessage(result.Message);
            return true;
        }

        internal bool UpdatePreview()
        {
            if (!IsActive || !HasSemanticDiff)
            {
                SetMessage("Update is available only when the semantic diff is non-empty.");
                return false;
            }

            if (!CanUpdatePreview)
            {
                SetMessage(UnsupportedPresentationCommitReason);
                return false;
            }

            SynchronizeAfterInput();
            if (!IsActive || !HasSemanticDiff)
            {
                return false;
            }

            WorkloadV2CommitResult result = WorkloadGateway.CommitV2Update();
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            ClearLocalSession();
            SetMessage(result.Message);
            return true;
        }

        internal bool ForkPreview(string label)
        {
            if (!IsActive)
            {
                SetMessage("There is no active workload preview to fork.");
                return false;
            }

            if (!CanForkPreview)
            {
                SetMessage(UnsupportedPresentationCommitReason);
                return false;
            }

            SynchronizeAfterInput();
            string forkId = Guid.NewGuid().ToString("N");
            WorkloadV2CommitResult result = WorkloadGateway.CommitV2Fork(forkId, label);
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            // The gateway's fork target is a new stable ID. The source session
            // ID remains untouched, so Save As never silently rewrites it.
            ClearLocalSession();
            SetMessage(result.Message);
            return true;
        }

        internal void ResetForWindowClose()
        {
            if (IsActive || WorkloadGateway.IsV2PreviewSessionActive)
            {
                WorkloadGateway.CancelV2Preview();
            }

            ClearLocalSession();
            _surfaceRect = Rect.zero;
            _applyRect = Rect.zero;
            _cancelRect = Rect.zero;
            _updateRect = Rect.zero;
            _forkRect = Rect.zero;
            _membershipRect = Rect.zero;
            _inspectionPopoverRect = Rect.zero;
            _inlineSurfacePopoverRect = Rect.zero;
            _inlineSurfacePopover = InlineSurfacePopoverKind.None;
            _inlineSurfaceName = string.Empty;
            _membershipScroll = Vector2.zero;
            _inspectionActive = false;
        }

        internal bool ShouldRouteInspectionWheel(Event evt)
        {
            if (evt == null || evt.type != EventType.ScrollWheel || !HasSemanticDiff)
            {
                return false;
            }

            bool overInspectionSurface = _updateRect.Contains(evt.mousePosition) ||
                                          _inspectionPopoverRect.Contains(evt.mousePosition);
            if (overInspectionSurface)
            {
                _inspectionActive = true;
            }

            return overInspectionSurface;
        }

        internal bool TryHandleSurfaceInput(Event evt)
        {
            if (evt == null || !IsActive ||
                (evt.alt && evt.button == 0))
            {
                return false;
            }

            bool mouseEvent = evt.type == EventType.MouseDown ||
                              evt.type == EventType.MouseUp ||
                              evt.type == EventType.ScrollWheel;
            bool inlineEditorKeyboard = _inlineSurfacePopover == InlineSurfacePopoverKind.Fork &&
                                        (evt.type == EventType.KeyDown ||
                                         evt.type == EventType.KeyUp ||
                                         evt.type == EventType.ValidateCommand) &&
                                        GUI.GetNameOfFocusedControl() == "BWT.WorkloadInlineEditor";
            if (!mouseEvent && !inlineEditorKeyboard)
            {
                return false;
            }

            if (inlineEditorKeyboard)
            {
                return true;
            }

            bool overInlinePopover = _inlineSurfacePopoverRect.Contains(evt.mousePosition);
            if (overInlinePopover)
            {
                // Keep the event available to TextField, ButtonText, and the
                // membership scroll view drawn later in this same Work-tab
                // pass. Returning true is enough to keep the priority router
                // from seeing it.
                return true;
            }

            bool overSurface = _surfaceRect.Contains(evt.mousePosition);
            bool overPopover = _inspectionPopoverRect.Contains(evt.mousePosition);
            if (!overSurface && !overPopover)
            {
                return false;
            }

            if (_inlineSurfacePopover != InlineSurfacePopoverKind.None)
            {
                CloseInlineSurfacePopover();
            }

            if (evt.type == EventType.ScrollWheel)
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (_applyRect.Contains(evt.mousePosition))
                {
                    if (CanApplyPreview)
                    {
                        HandleSurfaceAction(ApplyPreview);
                    }
                }
                else if (_cancelRect.Contains(evt.mousePosition))
                {
                    HandleSurfaceAction(CancelPreview);
                }
                else if (_updateRect.Contains(evt.mousePosition) && CanUpdatePreview)
                {
                    HandleSurfaceAction(UpdatePreview);
                }
                else if (_forkRect.Contains(evt.mousePosition) && CanForkPreview)
                {
                    OpenForkEditor();
                }
                else if (_membershipRect.Contains(evt.mousePosition))
                {
                    OpenMembershipPopover();
                }
            }

            // Prevent a surface or inspection popover click from falling
            // through to the priority-cell router underneath it.
            evt.Use();
            return true;
        }

        private enum PreviewButtonKind
        {
            Apply,
            Cancel,
            Update,
            SaveAs,
            Members
        }

        private static float PreferredPreviewButtonWidth(PreviewButtonKind kind)
        {
            switch (kind)
            {
                case PreviewButtonKind.Apply:
                    return 54f;
                case PreviewButtonKind.Cancel:
                    return 58f;
                case PreviewButtonKind.Update:
                    return 94f;
                case PreviewButtonKind.SaveAs:
                    return 88f;
                case PreviewButtonKind.Members:
                    return 86f;
                default:
                    return 70f;
            }
        }

        private static List<List<PreviewButtonKind>> BuildPreviewButtonRows(
            float availableWidth,
            bool includeUpdate)
        {
            const float gap = 4f;
            var actions = new List<PreviewButtonKind>
            {
                PreviewButtonKind.Apply,
                PreviewButtonKind.Cancel
            };
            if (includeUpdate)
            {
                actions.Add(PreviewButtonKind.Update);
            }

            actions.Add(PreviewButtonKind.SaveAs);
            actions.Add(PreviewButtonKind.Members);

            var rows = new List<List<PreviewButtonKind>>();
            var row = new List<PreviewButtonKind>();
            float rowWidth = 0f;
            for (int i = 0; i < actions.Count; i++)
            {
                PreviewButtonKind action = actions[i];
                float preferred = PreferredPreviewButtonWidth(action);
                float required = row.Count == 0 ? preferred : gap + preferred;
                if (row.Count > 0 && rowWidth + required > availableWidth)
                {
                    rows.Add(row);
                    row = new List<PreviewButtonKind>();
                    rowWidth = 0f;
                }

                row.Add(action);
                rowWidth += row.Count == 1 ? preferred : gap + preferred;
            }

            if (row.Count > 0)
            {
                rows.Add(row);
            }

            return rows;
        }

        private void LayoutPreviewButtonRows(
            Rect panel,
            IReadOnlyList<List<PreviewButtonKind>> rows)
        {
            const float horizontalPadding = 6f;
            const float gap = 4f;
            const float buttonHeight = 22f;
            const float rowGap = 4f;
            float availableWidth = Mathf.Max(1f, panel.width - (horizontalPadding * 2f));
            float y = panel.yMin + 68f;

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<PreviewButtonKind> row = rows[rowIndex];
                float preferredWidth = 0f;
                for (int i = 0; i < row.Count; i++)
                {
                    preferredWidth += PreferredPreviewButtonWidth(row[i]);
                }

                preferredWidth += Mathf.Max(0, row.Count - 1) * gap;
                float shrink = preferredWidth > availableWidth
                    ? availableWidth / Mathf.Max(preferredWidth, 1f)
                    : 1f;
                float x = panel.xMin + horizontalPadding;
                for (int i = 0; i < row.Count; i++)
                {
                    float width = PreferredPreviewButtonWidth(row[i]) * shrink;
                    if (i == row.Count - 1)
                    {
                        width = Mathf.Max(1f, (panel.xMax - horizontalPadding) - x);
                    }

                    Rect rect = new Rect(x, y, Mathf.Max(1f, width), buttonHeight);
                    SetPreviewButtonRect(row[i], rect);
                    x = rect.xMax + gap;
                }

                y += buttonHeight + rowGap;
            }
        }

        private void SetPreviewButtonRect(PreviewButtonKind kind, Rect rect)
        {
            switch (kind)
            {
                case PreviewButtonKind.Apply:
                    _applyRect = rect;
                    break;
                case PreviewButtonKind.Cancel:
                    _cancelRect = rect;
                    break;
                case PreviewButtonKind.Update:
                    _updateRect = rect;
                    break;
                case PreviewButtonKind.SaveAs:
                    _forkRect = rect;
                    break;
                case PreviewButtonKind.Members:
                    _membershipRect = rect;
                    break;
            }
        }

        internal void DrawSurface(Rect inRect)
        {
            if (!IsActive)
            {
                if (_inlineSurfacePopover != InlineSurfacePopoverKind.None)
                {
                    CloseInlineSurfacePopover();
                }

                _surfaceRect = Rect.zero;
                _applyRect = Rect.zero;
                _cancelRect = Rect.zero;
                _updateRect = Rect.zero;
                _forkRect = Rect.zero;
                _membershipRect = Rect.zero;
                _inspectionPopoverRect = Rect.zero;
                _inlineSurfacePopoverRect = Rect.zero;
                _inspectionActive = false;
                return;
            }

            if (_surfaceReadyFrame > Time.frameCount)
            {
                ClearSurfaceRects();
                return;
            }

            float railWidth = GetPreviewRailWidth(inRect);
            float panelWidth = GetPreviewPanelWidth(inRect);
            if (railWidth <= 0f || panelWidth < 180f)
            {
                if (_inlineSurfacePopover != InlineSurfacePopoverKind.None)
                {
                    CloseInlineSurfacePopover();
                }

                ClearSurfaceRects();
                return;
            }

            List<List<PreviewButtonKind>> buttonRows = BuildPreviewButtonRows(
                panelWidth,
                HasSemanticDiff);
            float panelHeight = GetPreviewPanelHeight(panelWidth);
            if (inRect.height < panelHeight + PreviewRailBottomClearance + 12f)
            {
                if (_inlineSurfacePopover != InlineSurfacePopoverKind.None)
                {
                    CloseInlineSurfacePopover();
                }

                ClearSurfaceRects();
                return;
            }

            Rect panel = new Rect(
                inRect.xMin + 6f,
                inRect.yMax - panelHeight - PreviewRailBottomClearance,
                panelWidth,
                panelHeight);

            Rect popover = _inspectionActive && HasSemanticDiff
                ? new Rect(
                    panel.xMin,
                    Mathf.Max(inRect.yMin + 4f, panel.yMin - 126f),
                    Mathf.Min(panel.width, 460f),
                    120f)
                : Rect.zero;

            _surfaceRect = panel;
            _applyRect = Rect.zero;
            _cancelRect = Rect.zero;
            _updateRect = Rect.zero;
            _forkRect = Rect.zero;
            _membershipRect = Rect.zero;
            LayoutPreviewButtonRows(panel, buttonRows);
            _inspectionPopoverRect = popover;
            UpdateInspectionPointer(panel, _updateRect, popover);
            if (_inspectionActive && HasSemanticDiff)
            {
                _inspectionPopoverRect = new Rect(
                    panel.xMin,
                    Mathf.Max(inRect.yMin + 4f, panel.yMin - 126f),
                    Mathf.Min(panel.width, 460f),
                    120f);
            }

            if (_inlineSurfacePopover != InlineSurfacePopoverKind.None)
            {
                _inspectionActive = false;
                _inspectionPopoverRect = Rect.zero;
                _inlineSurfacePopoverRect = BuildInlineSurfacePopoverRect(
                    inRect,
                    panel,
                    _inlineSurfacePopover == InlineSurfacePopoverKind.Membership
                        ? 250f
                        : 124f);
            }
            else
            {
                _inlineSurfacePopoverRect = Rect.zero;
            }

            Color oldGuiColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                Widgets.DrawBoxSolidWithOutline(
                    panel,
                    new Color(0.055f, 0.07f, 0.08f, 0.96f),
                    new Color(0.38f, 0.52f, 0.55f, 0.65f));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = new Color(1f, 1f, 1f, 0.95f);
                Widgets.Label(
                    new Rect(panel.xMin + 7f, panel.yMin + 4f, panel.width - 14f, 16f),
                    "Workload preview: " + SourceLabel);
                GUI.color = new Color(0.78f, 0.86f, 0.87f, 0.9f);
                Widgets.Label(
                    new Rect(panel.xMin + 7f, panel.yMin + 20f, panel.width - 14f, 14f),
                    "" + DataAuthorityLabel + " | " +
                    "colony changes " + ColonyImpactCount +
                    " | workload changes " + TemplateDirtyCount +
                    " | included " + IncludedCount +
                    " | unchanged/outside " + UnchangedOutsideScopeCount);
                Widgets.Label(
                    new Rect(panel.xMin + 7f, panel.yMin + 34f, panel.width - 14f, 14f),
                    "new/unrepresented " + UnrepresentedNewCount +
                    " | excluded " + ExplicitlyExcludedCount +
                    " | stale/missing " + StaleMissingCount);

                if (ValidationLabel.AnyNonWhitespace())
                {
                    GUI.color = new Color(1f, 0.82f, 0.55f, 0.9f);
                    Widgets.Label(
                        new Rect(panel.xMin + 7f, panel.yMin + 48f, panel.width - 14f, 14f),
                        ValidationLabel);
                }
                else if (UnsupportedDimensionsLabel.AnyNonWhitespace())
                {
                    GUI.color = new Color(0.84f, 0.82f, 0.68f, 0.9f);
                    Widgets.Label(
                        new Rect(panel.xMin + 7f, panel.yMin + 48f, panel.width - 14f, 14f),
                        UnsupportedDimensionsLabel);
                }
                else if (LastMessage.AnyNonWhitespace())
                {
                    GUI.color = new Color(0.95f, 0.82f, 0.58f, 0.92f);
                    Widgets.Label(
                        new Rect(panel.xMin + 7f, panel.yMin + 48f, panel.width - 14f, 14f),
                        LastMessage.Truncate(Mathf.Max(1f, panel.width - 14f)));
                }
                else
                {
                    GUI.color = new Color(0.7f, 0.78f, 0.78f, 0.82f);
                    Widgets.Label(
                        new Rect(panel.xMin + 7f, panel.yMin + 48f, panel.width - 14f, 14f),
                        SettingsIntegrationStatus);
                }

                DrawPreviewButton(
                    _applyRect,
                    "Apply",
                    () => ApplyPreview(),
                    CanApplyPreview,
                    UnsupportedPresentationCommitReason);
                DrawPreviewButton(_cancelRect, "Cancel", () => CancelPreview());
                if (HasSemanticDiff)
                {
                    DrawPreviewButton(
                        _updateRect,
                        "Update",
                        () => UpdatePreview(),
                        CanUpdatePreview,
                        UnsupportedPresentationCommitReason);
                    TooltipHandler.TipRegion(
                        _updateRect,
                        HasUnsupportedOwnedPresentationState
                            ? UnsupportedPresentationCommitReason
                            : "Update Workload applies this semantic diff and replaces the same template. " +
                              "Hover to inspect changed pawn/worktype cells. Scroll here to move the Work tab.");
                }

                DrawPreviewButton(
                    _forkRect,
                    "Save As",
                    () =>
                    {
                        OpenForkEditor();
                        return true;
                    },
                    CanForkPreview,
                    UnsupportedPresentationCommitReason);
                DrawPreviewButton(
                    _membershipRect,
                    "Members",
                    () =>
                    {
                        OpenMembershipPopover();
                        return true;
                    });
                TooltipHandler.TipRegion(
                    _membershipRect,
                    "Include or exclude current pawns for this preview session. " +
                    "Unrepresented pawns are never applied unless explicitly included.");

                if (_inspectionActive && HasSemanticDiff)
                {
                    DrawInspectionPopover(_inspectionPopoverRect);
                }

                if (_inlineSurfacePopover != InlineSurfacePopoverKind.None)
                {
                    DrawInlineSurfacePopover(_inlineSurfacePopoverRect);
                }

                if (LastMessage.AnyNonWhitespace())
                {
                    TooltipHandler.TipRegion(panel, LastMessage);
                }
            }
            finally
            {
                GUI.color = oldGuiColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        internal bool IsInspectionRowAffected(Pawn pawn)
        {
            EnsureInspectionIndex();
            PawnKey key = WorkTabEffectiveStateIds.ForPawn(pawn);
            return key.IsValid && _changedPawns.Contains(key);
        }

        internal bool IsInspectionRowLevelChanged(Pawn pawn)
        {
            EnsureInspectionIndex();
            PawnKey key = WorkTabEffectiveStateIds.ForPawn(pawn);
            return key.IsValid && _changedSchedulePawns.Contains(key);
        }

        internal bool IsInspectionCellAffected(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver)
        {
            EnsureInspectionIndex();
            if (pawn == null || workType == null)
            {
                return false;
            }

            WorkloadParentPriorityKey parentKey =
                WorkTabEffectiveStateIds.ForParentPriority(pawn, workType);
            if (_changedParentKeys.Contains(parentKey))
            {
                return true;
            }

            if (workGiver == null)
            {
                return false;
            }

            return _changedSpecificJobKeys.Contains(
                WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver));
        }

        internal IReadOnlyList<WorkloadMembershipRecord> GetMembershipRecords()
        {
            return GetMembershipSnapshot().Records;
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

            long projectionRevision = _projectedProvider?.Revision ?? 0L;
            long pawnSetRevision = ComputeAvailablePawnSetRevision();
            if (_membershipSnapshot == null ||
                !ReferenceEquals(_membershipSnapshotSession, _session) ||
                _membershipSnapshotProjectionRevision != projectionRevision ||
                _membershipSnapshotPawnSetRevision != pawnSetRevision)
            {
                WorkloadTemplate template =
                    _session.SourceTemplate.WithState(_session.ProjectedState);
                WorkloadMembershipResult result =
                    WorkloadMembershipClassifier.Classify(template, BuildAvailableCandidates());
                _membershipSnapshot = new WorkloadMembershipSnapshot(result);
                _membershipSnapshotSession = _session;
                _membershipSnapshotProjectionRevision = projectionRevision;
                _membershipSnapshotPawnSetRevision = pawnSetRevision;
            }

            return _membershipSnapshot;
        }

        internal bool ToggleMembership(PawnKey pawnKey)
        {
            if (!IsActive || pawnKey == null || !pawnKey.IsValid)
            {
                SetMessage("This pawn cannot be changed in the current preview.");
                return false;
            }

            WorkloadMembershipRecord record = FindMembershipRecord(pawnKey);
            if (record == null || !record.IsAvailable)
            {
                SetMessage("This pawn is stale or missing and cannot be included.");
                return false;
            }

            WorkloadScope scope = _session.SourceTemplate.Definition.Scope ?? WorkloadScope.Empty;
            if (scope.IsExplicitlyExcluded(pawnKey))
            {
                SetMessage("This pawn is explicitly excluded by the saved workload scope.");
                return false;
            }

            if (_session.ProjectedState.IsExcluded(pawnKey))
            {
                if (!SetSessionMembership(pawnKey, true))
                {
                    return false;
                }

                SetMessage("Pawn included for this preview session.");
                return true;
            }

            if (record.Classification == WorkloadMembershipClassification.UnrepresentedNew)
            {
                if (scope.Mode != WorkloadScopeMode.CurrentMapFreeColonists)
                {
                    SetMessage("This saved workload scope cannot include a new pawn.");
                    return false;
                }

                if (!AddCurrentLiveBaseline(pawnKey))
                {
                    return false;
                }

                SetMessage("Pawn included with its current live values for this preview.");
                return true;
            }

            if (record.Classification == WorkloadMembershipClassification.Included)
            {
                if (!SetSessionMembership(pawnKey, false))
                {
                    return false;
                }

                SetMessage("Pawn excluded from this preview session; its live values remain unchanged.");
                return true;
            }

            SetMessage("This pawn is outside the workload scope and was left unchanged.");
            return false;
        }

        private void OpenSession(WorkloadSession session)
        {
            WorkloadSurfaceCoordinator.OpenPreview();
            _session = session;
            _boundComponent = Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            // Give the next Work-tab pass a chance to lay out the grid beside
            // the rail before the rail itself is painted.
            _surfaceReadyFrame = Time.frameCount + 1;
            RebuildProjection(_session.ProjectedState);
            SetMessage("Preview open; live work priorities are unchanged until Apply.");
        }

        private void RebuildProjection(WorkloadProjectedState projectedState)
        {
            InvalidateMembershipSnapshot();
            if (_session == null)
            {
                _projectedProvider = null;
                ClearInspectionIndex();
                return;
            }

            var draft = new WorkloadDraft(projectedState ?? WorkloadProjectedState.Empty);
            _projectedProvider = new ProjectedWorkTabEffectiveStateProvider(
                draft,
                _liveProvider,
                _session.SourceTemplate.Definition.OwnershipDimensions,
                "bwt.preview",
                _session.SourceTemplate.Definition.Scope,
                BuildEditablePawnIds());
            ClearInspectionIndex();
        }

        private void ClearLocalSession()
        {
            WorkloadSurfaceCoordinator.NotifyPreviewClosed();
            InvalidateMembershipSnapshot();
            _session = null;
            _projectedProvider = null;
            _inspectionActive = false;
            _surfaceRect = Rect.zero;
            _applyRect = Rect.zero;
            _cancelRect = Rect.zero;
            _updateRect = Rect.zero;
            _forkRect = Rect.zero;
            _membershipRect = Rect.zero;
            _inspectionPopoverRect = Rect.zero;
            _inlineSurfacePopoverRect = Rect.zero;
            _inlineSurfacePopover = InlineSurfacePopoverKind.None;
            _inlineSurfaceName = string.Empty;
            _membershipScroll = Vector2.zero;
            ClearInspectionIndex();
            _surfaceReadyFrame = 0;
        }

        private void SetMessage(string message)
        {
            _lastMessage = message ?? string.Empty;
        }

        private void InvalidateMembershipSnapshot()
        {
            _membershipSnapshot = null;
            _membershipSnapshotSession = null;
            _membershipSnapshotProjectionRevision = long.MinValue;
            _membershipSnapshotPawnSetRevision = long.MinValue;
        }

        private int GetMembershipCount(WorkloadMembershipClassification classification)
        {
            return GetMembershipSnapshot().GetCount(classification);
        }

        private IReadOnlyList<PawnKey> BuildEditablePawnIds()
        {
            var editable = new List<PawnKey>();
            IReadOnlyList<WorkloadMembershipRecord> records = GetMembershipRecords();
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

        private WorkloadMembershipRecord FindMembershipRecord(PawnKey pawnKey)
        {
            return GetMembershipSnapshot().Find(pawnKey);
        }

        private bool SetSessionMembership(PawnKey pawnKey, bool include)
        {
            WorkloadSession changed = include
                ? _session.IncludePawn(pawnKey)
                : _session.ExcludePawn(pawnKey);
            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.SetV2PreviewState(changed.ProjectedState);
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            _session = result.Value;
            RebuildProjection(_session.ProjectedState);
            return true;
        }

        private bool AddCurrentLiveBaseline(PawnKey pawnKey)
        {
            Pawn pawn = ResolvePawn(pawnKey);
            if (pawn == null)
            {
                SetMessage("The current pawn could not be resolved.");
                return false;
            }

            WorkloadOwnershipDimensions ownership =
                _session.SourceTemplate.Definition.OwnershipDimensions;
            // Including a new pawn is a complete projection operation. Do not
            // seed only the dimensions that happen to have a reader today and
            // then present a partial workload as if it were safe to commit.
            // The current runtime has live writers only for these two
            // dimensions; all other owned dimensions fail closed with a
            // visible explanation instead of being silently dropped.
            WorkloadOwnershipDimensions unsupported =
                WorkloadOwnershipDimensions.Schedules |
                WorkloadOwnershipDimensions.SpecificJobOverrides |
                WorkloadOwnershipDimensions.SpecificJobOrder |
                WorkloadOwnershipDimensions.PresentationSettings;
            if ((ownership & unsupported) != WorkloadOwnershipDimensions.None)
            {
                SetMessage(
                    "This pawn cannot be included safely: the workload owns " +
                    FormatUnsupportedInclusionDimensions(ownership & unsupported) +
                    ", but no complete live writer exists for that dimension. " +
                    "The preview was left unchanged.");
                return false;
            }

            var draft = new WorkloadDraft(_session.ProjectedState);
            IReadOnlyList<WorkTypeDef> workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            bool wroteValue = false;
            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef workType = workTypes[i];
                if (workType == null || pawn.workSettings == null || !pawn.workSettings.EverWork)
                {
                    continue;
                }

                WorkloadParentPriorityKey key =
                    WorkTabEffectiveStateIds.ForParentPriority(pawn, workType);
                if (ownership.Owns(WorkloadStateDimension.ParentPriorities))
                {
                    draft.SetParentPriority(
                        key,
                        _liveProvider.GetParentPriority(
                            key,
                            WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType)));
                    wroteValue = true;
                }

                if (ownership.Owns(WorkloadStateDimension.ManualModes))
                {
                    draft.SetManualMode(
                        key,
                        _liveProvider.IsManualMode(
                            key,
                            Find.PlaySettings?.useWorkPriorities ?? true));
                    wroteValue = true;
                }
            }

            if (!wroteValue)
            {
                SetMessage("This workload has no supported current-pawn dimension to include.");
                return false;
            }

            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.SetV2PreviewState(draft.ProjectedState);
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                return false;
            }

            _session = result.Value;
            RebuildProjection(_session.ProjectedState);
            return true;
        }

        private static string FormatUnsupportedInclusionDimensions(
            WorkloadOwnershipDimensions dimensions)
        {
            var values = new List<string>();
            if ((dimensions & WorkloadOwnershipDimensions.Schedules) != 0)
            {
                values.Add("schedules");
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOverrides) != 0)
            {
                values.Add("specific-job overrides");
            }

            if ((dimensions & WorkloadOwnershipDimensions.SpecificJobOrder) != 0)
            {
                values.Add("specific-job order");
            }

            if ((dimensions & WorkloadOwnershipDimensions.PresentationSettings) != 0)
            {
                values.Add("presentation settings");
            }

            return values.Count == 0 ? "unsupported state" : string.Join(", ", values.ToArray());
        }

        private void EnsureInspectionIndex()
        {
            if (!IsActive)
            {
                ClearInspectionIndex();
                return;
            }

            // Update inspection is intentionally the template diff. Apply's
            // colony impact is exposed separately by LiveDiff and must not
            // make the Update button claim it will rewrite the template.
            WorkloadSemanticDiff diff = _session.TemplateDiff;
            string fingerprint = diff.BeforeFingerprint + ":" + diff.AfterFingerprint;
            if (StringComparer.Ordinal.Equals(_inspectionFingerprint, fingerprint))
            {
                return;
            }

            _inspectionFingerprint = fingerprint;
            _changedParentKeys.Clear();
            _changedSpecificJobKeys.Clear();
            _changedSchedulePawns.Clear();
            _changedPawns.Clear();
            for (int i = 0; i < diff.Changes.Count; i++)
            {
                WorkloadChange change = diff.Changes[i];
                if (change == null ||
                    !TryDecodeChangeKey(change.Dimension, change.CanonicalKey, out PawnKey pawn, out WorkTypeKey workType, out WorkGiverKey workGiver))
                {
                    continue;
                }

                if (pawn != null && pawn.IsValid)
                {
                    _changedPawns.Add(pawn);
                }

                switch (change.Dimension)
                {
                    case WorkloadStateDimension.ParentPriorities:
                    case WorkloadStateDimension.ManualModes:
                        if (pawn != null && workType != null)
                        {
                            _changedParentKeys.Add(new WorkloadParentPriorityKey(pawn, workType));
                        }
                        break;
                    case WorkloadStateDimension.Schedules:
                        if (pawn != null)
                        {
                            _changedSchedulePawns.Add(pawn);
                        }
                        break;
                    case WorkloadStateDimension.SpecificJobOverrides:
                    case WorkloadStateDimension.SpecificJobOrder:
                        if (pawn != null && workType != null && workGiver != null)
                        {
                            _changedSpecificJobKeys.Add(
                                new WorkloadSpecificJobKey(pawn, workType, workGiver));
                        }
                        break;
                    case WorkloadStateDimension.Membership:
                        // Membership is intentionally represented by the row
                        // index above; it has no individual cell to paint.
                        break;
                }
            }
        }

        private void ClearInspectionIndex()
        {
            _inspectionFingerprint = string.Empty;
            _changedParentKeys.Clear();
            _changedSpecificJobKeys.Clear();
            _changedSchedulePawns.Clear();
            _changedPawns.Clear();
        }

        private void UpdateInspectionPointer(Rect panel, Rect updateRect, Rect popover)
        {
            if (!HasSemanticDiff)
            {
                _inspectionActive = false;
                return;
            }

            Vector2 pointer = Event.current?.mousePosition ?? Vector2.zero;
            bool overUpdate = updateRect.width > 0f && updateRect.Contains(pointer);
            bool overSurface = panel.Contains(pointer);
            bool overPopover = popover.width > 0f && popover.Contains(pointer);
            if (overUpdate)
            {
                _inspectionActive = true;
            }
            else if (!overSurface && !overPopover)
            {
                _inspectionActive = false;
            }
        }

        private void DrawInspectionPopover(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(0.055f, 0.07f, 0.08f, 0.97f),
                new Color(0.42f, 0.58f, 0.6f, 0.7f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(0.9f, 0.96f, 0.96f, 0.96f);
            Widgets.Label(
                new Rect(rect.xMin + 7f, rect.yMin + 5f, rect.width - 14f, 16f),
                "Update inspection: changed cells use stable pawn/worktype IDs");
            GUI.color = new Color(0.74f, 0.83f, 0.84f, 0.9f);
            int shown = 0;
            IReadOnlyList<WorkloadChange> changes = _session.TemplateDiff.Changes;
            for (int i = 0; i < changes.Count && shown < 4; i++)
            {
                WorkloadChange change = changes[i];
                string line = "- " + ChangeDimensionLabel(change.Dimension) +
                              " - " + Trim(change.CanonicalKey, 34);
                Widgets.Label(
                    new Rect(rect.xMin + 8f, rect.yMin + 22f + shown * 15f, rect.width - 16f, 15f),
                    line);
                shown++;
            }

            GUI.color = new Color(0.82f, 0.88f, 0.88f, 0.92f);
            Widgets.Label(
                new Rect(rect.xMin + 8f, rect.yMax - 21f, rect.width - 16f, 16f),
                "Scroll wheel over this panel to move the Work tab.");
            TooltipHandler.TipRegion(
                rect,
                "Inspection remains active over the panel. Scrolling moves the Work tab and never edits a priority cell.");
        }

        private static Rect BuildInlineSurfacePopoverRect(
            Rect inRect,
            Rect panel,
            float desiredHeight)
        {
            float width = Mathf.Min(panel.width, 360f);
            float height = Mathf.Min(
                desiredHeight,
                Mathf.Max(90f, inRect.height - 8f));
            float x = Mathf.Clamp(
                panel.xMin,
                inRect.xMin + 4f,
                Mathf.Max(inRect.xMin + 4f, inRect.xMax - width - 4f));
            float y = panel.yMin - height - 5f;
            if (y < inRect.yMin + 4f)
            {
                y = panel.yMax + 5f;
            }

            y = Mathf.Clamp(
                y,
                inRect.yMin + 4f,
                Mathf.Max(inRect.yMin + 4f, inRect.yMax - height - 4f));
            return new Rect(x, y, width, height);
        }

        private void DrawInlineSurfacePopover(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Widgets.DrawBoxSolidWithOutline(
                rect,
                new Color(0.055f, 0.07f, 0.08f, 0.98f),
                new Color(0.42f, 0.58f, 0.6f, 0.75f));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(0.92f, 0.97f, 0.97f, 0.98f);

            if (_inlineSurfacePopover == InlineSurfacePopoverKind.Fork)
            {
                DrawInlineForkPopover(rect);
            }
            else
            {
                DrawInlineMembershipPopover(rect);
            }
        }

        private void DrawInlineForkPopover(Rect rect)
        {
            Widgets.Label(
                new Rect(rect.xMin + 8f, rect.yMin + 6f, rect.width - 16f, 20f),
                "Save workload as");
            Rect field = new Rect(
                rect.xMin + 8f,
                rect.yMin + 31f,
                rect.width - 16f,
                26f);
            GUI.SetNextControlName("BWT.WorkloadInlineEditor");
            _inlineSurfaceName = Widgets.TextField(field, _inlineSurfaceName, 64);

            float buttonWidth = (rect.width - 20f) * 0.5f;
            Rect save = new Rect(rect.xMin + 8f, rect.yMax - 30f, buttonWidth, 24f);
            Rect cancel = new Rect(save.xMax + 4f, save.yMin, buttonWidth, 24f);
            if (Widgets.ButtonText(save, "Save As"))
            {
                CommitInlineFork();
            }

            if (Widgets.ButtonText(cancel, "Cancel"))
            {
                CloseInlineSurfacePopover();
            }

            Event evt = Event.current;
            if (evt != null &&
                evt.type == EventType.KeyDown &&
                GUI.GetNameOfFocusedControl() == "BWT.WorkloadInlineEditor")
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    CommitInlineFork();
                    evt.Use();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    CloseInlineSurfacePopover();
                    evt.Use();
                }
            }
        }

        private void DrawInlineMembershipPopover(Rect rect)
        {
            Widgets.Label(
                new Rect(rect.xMin + 8f, rect.yMin + 6f, rect.width - 16f, 20f),
                "Workload membership (preview)");
            GUI.color = new Color(0.76f, 0.84f, 0.85f, 0.95f);
            Widgets.Label(
                new Rect(rect.xMin + 8f, rect.yMin + 27f, rect.width - 16f, 18f),
                "Choose which available pawns this session affects.");
            GUI.color = Color.white;

            IReadOnlyList<WorkloadMembershipRecord> records = GetMembershipRecords();
            Rect listRect = new Rect(
                rect.xMin + 6f,
                rect.yMin + 48f,
                rect.width - 12f,
                Mathf.Max(28f, rect.height - 54f));
            float viewWidth = Mathf.Max(1f, listRect.width - 16f);
            float viewHeight = Mathf.Max(listRect.height, records.Count * 24f);
            Widgets.BeginScrollView(
                listRect,
                ref _membershipScroll,
                new Rect(0f, 0f, viewWidth, viewHeight));

            int rowIndex = 0;
            WorkloadScope scope = _session.SourceTemplate.Definition.Scope ?? WorkloadScope.Empty;
            for (int i = 0; i < records.Count; i++)
            {
                WorkloadMembershipRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                Pawn pawn = ResolvePawn(record.PawnId);
                string label = pawn?.LabelShortCap ?? record.PawnId.Value;
                bool savedExcluded = scope.IsExplicitlyExcluded(record.PawnId);
                bool outsideScope = record.Classification ==
                    WorkloadMembershipClassification.UnchangedOutsideScope;
                bool actionable = record.IsAvailable && !savedExcluded && !outsideScope;
                string rowLabel;
                if (savedExcluded)
                {
                    rowLabel = label + " (saved scope: excluded)";
                }
                else if (outsideScope)
                {
                    rowLabel = label + " (outside saved scope)";
                }
                else
                {
                    bool include = _session.ProjectedState.IsExcluded(record.PawnId) ||
                                   record.Classification == WorkloadMembershipClassification.UnrepresentedNew;
                    rowLabel = (include ? "Include " : "Exclude ") +
                               label + " (" + MembershipLabel(record.Classification) + ")";
                }

                Rect row = new Rect(
                    0f,
                    rowIndex * 24f,
                    viewWidth,
                    22f);
                if (actionable)
                {
                    if (Widgets.ButtonText(row, rowLabel))
                    {
                        if (!ToggleMembership(record.PawnId))
                        {
                            Messages.Message(LastMessage, MessageTypeDefOf.RejectInput, false);
                        }
                    }
                }
                else
                {
                    GUI.color = new Color(0.62f, 0.68f, 0.69f, 0.9f);
                    Widgets.Label(row, rowLabel);
                    GUI.color = Color.white;
                }

                rowIndex++;
            }

            if (rowIndex == 0)
            {
                GUI.color = new Color(0.72f, 0.79f, 0.8f, 0.9f);
                Widgets.Label(new Rect(8f, 8f, viewWidth - 16f, 20f), "No current pawns are available.");
                GUI.color = Color.white;
            }

            Widgets.EndScrollView();
            TooltipHandler.TipRegion(
                rect,
                "Membership changes remain session-local until Apply, Update, or Save As is confirmed.");
        }

        private void DrawPreviewButton(
            Rect rect,
            string label,
            Func<bool> action,
            bool enabled = true,
            string disabledTooltip = null)
        {
            if (rect.width <= 0f)
            {
                return;
            }

            if (!enabled)
            {
                Widgets.ButtonText(rect, label, active: false);
                if (disabledTooltip.AnyNonWhitespace())
                {
                    TooltipHandler.TipRegion(rect, disabledTooltip);
                }

                return;
            }

            if (Widgets.ButtonText(rect, label, active: true))
            {
                if (action != null && !action())
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    Messages.Message(LastMessage, MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            }
        }

        private void HandleSurfaceAction(Func<bool> action)
        {
            if (action == null || action())
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            Messages.Message(LastMessage, MessageTypeDefOf.RejectInput, false);
        }

        private void OpenForkEditor()
        {
            if (!IsActive)
            {
                return;
            }

            WorkloadSurfaceCoordinator.OpenPreview();
            _inspectionActive = false;
            _inlineSurfacePopover = InlineSurfacePopoverKind.Fork;
            _inlineSurfaceName = SourceLabel + " copy";
        }

        private void OpenMembershipPopover()
        {
            if (!IsActive)
            {
                return;
            }

            WorkloadSurfaceCoordinator.OpenPreview();
            _inspectionActive = false;
            _inlineSurfacePopover = InlineSurfacePopoverKind.Membership;
            _membershipScroll = Vector2.zero;
        }

        private void CloseInlineSurfacePopover()
        {
            _inlineSurfacePopover = InlineSurfacePopoverKind.None;
            _inlineSurfaceName = string.Empty;
            _inlineSurfacePopoverRect = Rect.zero;
            _membershipScroll = Vector2.zero;
            // Closing Fork/Members only closes that inline child surface. The
            // preview rail still owns the Work-tab editing surface until the
            // session itself is applied, canceled, updated, forked, or torn
            // down with the window.
        }

        private void ClearSurfaceRects()
        {
            _surfaceRect = Rect.zero;
            _applyRect = Rect.zero;
            _cancelRect = Rect.zero;
            _updateRect = Rect.zero;
            _forkRect = Rect.zero;
            _membershipRect = Rect.zero;
            _inspectionPopoverRect = Rect.zero;
            _inlineSurfacePopoverRect = Rect.zero;
            _inspectionActive = false;
        }

        private void CommitInlineFork()
        {
            string label = (_inlineSurfaceName ?? string.Empty).Trim();
            if (label.Length == 0)
            {
                SetMessage("A workload name is required.");
                Messages.Message(LastMessage, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!ForkPreview(label))
            {
                Messages.Message(LastMessage, MessageTypeDefOf.RejectInput, false);
                return;
            }

            CloseInlineSurfacePopover();
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }

        private static string MembershipLabel(WorkloadMembershipClassification classification)
        {
            switch (classification)
            {
                case WorkloadMembershipClassification.Included:
                    return "included";
                case WorkloadMembershipClassification.UnchangedOutsideScope:
                    return "outside scope";
                case WorkloadMembershipClassification.UnrepresentedNew:
                    return "new/unrepresented";
                case WorkloadMembershipClassification.ExplicitlyExcluded:
                    return "excluded";
                case WorkloadMembershipClassification.StaleMissing:
                    return "stale/missing";
                default:
                    return classification.ToString();
            }
        }

        private IReadOnlyList<PawnScopeCandidate> BuildAvailableCandidates()
        {
            var candidates = new List<PawnScopeCandidate>();
            IReadOnlyList<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            if (pawns == null)
            {
                return candidates;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.thingIDNumber <= 0)
                {
                    continue;
                }

                bool currentMap = pawn.Map == Find.CurrentMap;
                bool freeColonist = currentMap &&
                    Find.CurrentMap?.mapPawns?.FreeColonists?.Contains(pawn) == true;
                candidates.Add(new PawnScopeCandidate(
                    WorkTabEffectiveStateIds.ForPawn(pawn),
                    currentMap,
                    pawn.IsColonist,
                    freeColonist));
            }

            return candidates;
        }

        /// <summary>
        /// A cheap shape token for the candidate set used by the classifier.
        /// It observes only values that affect WorkloadScope.IsInScope. The
        /// token is checked before a snapshot read, while the candidate list is
        /// allocated only when the token actually changes.
        /// </summary>
        private long ComputeAvailablePawnSetRevision()
        {
            unchecked
            {
                long revision = 17L;
                IReadOnlyList<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
                revision = (revision * 31L) + (pawns?.Count ?? 0);
                Map currentMap = Find.CurrentMap;
                if (pawns == null)
                {
                    return revision;
                }

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null)
                    {
                        revision = (revision * 31L) + 1L;
                        continue;
                    }

                    bool currentMapPawn = pawn.Map == currentMap;
                    bool freeColonist = currentMapPawn &&
                        currentMap?.mapPawns?.FreeColonists?.Contains(pawn) == true;
                    revision = (revision * 31L) + pawn.thingIDNumber;
                    revision = (revision * 31L) + (currentMapPawn ? 1L : 0L);
                    revision = (revision * 31L) + (pawn.IsColonist ? 1L : 0L);
                    revision = (revision * 31L) + (freeColonist ? 1L : 0L);
                }

                return revision;
            }
        }

        private static Pawn ResolvePawn(PawnKey key)
        {
            if (key == null || !int.TryParse(key.Value, out int thingId))
            {
                return null;
            }

            IReadOnlyList<Pawn> pawns = PawnsFinder.All_AliveOrDead;
            if (pawns == null)
            {
                return null;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] != null && pawns[i].thingIDNumber == thingId)
                {
                    return pawns[i];
                }
            }

            return null;
        }

        private static bool TryDecodeChangeKey(
            WorkloadStateDimension dimension,
            string canonicalKey,
            out PawnKey pawn,
            out WorkTypeKey workType,
            out WorkGiverKey workGiver)
        {
            pawn = null;
            workType = null;
            workGiver = null;

            // Membership changes use an I:/E: marker followed by one encoded
            // pawn ID. They are row-level keys, not the length-prefixed pawn /
            // WorkType[/WorkGiver] tuples used by cell dimensions.
            if (dimension == WorkloadStateDimension.Membership)
            {
                if (string.IsNullOrEmpty(canonicalKey) || canonicalKey.Length < 3 ||
                    (canonicalKey[0] != 'I' && canonicalKey[0] != 'E') ||
                    canonicalKey[1] != ':')
                {
                    return false;
                }

                int membershipCursor = 2;
                if (!TryReadCanonicalString(
                        canonicalKey,
                        ref membershipCursor,
                        out string membershipPawn) ||
                    membershipCursor != canonicalKey.Length)
                {
                    return false;
                }

                pawn = new PawnKey(membershipPawn);
                return pawn.IsValid;
            }

            int cursor = 0;
            if (!TryReadCanonicalString(canonicalKey, ref cursor, out string pawnValue))
            {
                return false;
            }

            pawn = new PawnKey(pawnValue);
            if (dimension == WorkloadStateDimension.Schedules)
            {
                return pawn.IsValid;
            }

            if (dimension == WorkloadStateDimension.PresentationSettings)
            {
                return false;
            }

            if (!TryReadCanonicalString(canonicalKey, ref cursor, out string workTypeValue))
            {
                return false;
            }

            workType = new WorkTypeKey(workTypeValue);
            if (dimension != WorkloadStateDimension.SpecificJobOverrides &&
                dimension != WorkloadStateDimension.SpecificJobOrder)
            {
                return pawn.IsValid && workType.IsValid;
            }

            if (!TryReadCanonicalString(canonicalKey, ref cursor, out string workGiverValue))
            {
                return false;
            }

            workGiver = new WorkGiverKey(workGiverValue);
            return pawn.IsValid && workType.IsValid && workGiver.IsValid;
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

        private static string ChangeDimensionLabel(WorkloadStateDimension dimension)
        {
            switch (dimension)
            {
                case WorkloadStateDimension.ParentPriorities:
                    return "parent priority";
                case WorkloadStateDimension.ManualModes:
                    return "manual mode";
                case WorkloadStateDimension.Schedules:
                    return "schedule";
                case WorkloadStateDimension.SpecificJobOverrides:
                    return "specific-job override";
                case WorkloadStateDimension.SpecificJobOrder:
                    return "specific-job order";
                case WorkloadStateDimension.PresentationSettings:
                    return "presentation setting";
                default:
                    return dimension.ToString();
            }
        }

        private static void AddUnsupportedDimension(
            List<string> values,
            WorkloadOwnershipDimensions ownership,
            WorkloadStateDimension dimension,
            string label)
        {
            if (ownership.Owns(dimension))
            {
                values.Add(label);
            }
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
