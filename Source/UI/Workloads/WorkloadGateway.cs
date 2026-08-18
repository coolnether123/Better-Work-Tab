using System;
using System.Collections.Generic;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Features.WorkGiverReassignments;
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
                    "Finish, Update, Save As, Apply, or Cancel the active workload preview before opening the workload list.");
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
        private static GameComponent_BWTWorldSettings _boundComponent;
        private static LegacyWorkloadBackend _legacyBackend;
        private static Workload2Backend _modernBackend;

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
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.Current()
                : modern.Current();
        }

        internal static IReadOnlyList<WorkloadDescriptor> SavedWorkloads()
        {
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return new List<WorkloadDescriptor>();
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.List()
                : modern.List();
        }

        internal static WorkloadOperationResult SelectWorkload(string stableId)
        {
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.Select(stableId)
                : modern.Select(stableId);
        }

        internal static WorkloadOperationResult<WorkloadDescriptor> CreateWorkload(string label)
        {
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.Create(label)
                : modern.Create(label);
        }

        internal static WorkloadOperationResult DeleteWorkload(string stableId)
        {
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.Delete(stableId)
                : modern.Delete(stableId);
        }

        internal static WorkloadOperationResult RenameWorkload(string stableId, string newLabel)
        {
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.Rename(stableId, newLabel)
                : modern.Rename(stableId, newLabel);
        }

        internal static WorkloadOperationResult ApplyCurrentWorkload()
        {
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.Apply()
                : modern.Apply();
        }

        internal static WorkloadOperationResult<WorkloadTemplate> CaptureCurrentV2Template(
            string stableId,
            string label)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.CaptureCurrentTemplate(stableId, label)
                : WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 capture is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadDescriptor> SaveV2Template(
            WorkloadTemplate template,
            bool makeCurrent)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.SaveTemplate(template, makeCurrent)
                : WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 template storage is unavailable while legacy workloads are active.");
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
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.BeginPreview()
                : WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadSession> SetV2PreviewSession(WorkloadSession session)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.SetPreviewSession(session)
                : WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadSession> EditV2Preview(
            Action<WorkloadDraft> edit)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.EditPreview(edit)
                : WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview editing is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadSession> SetV2PreviewState(
            WorkloadProjectedState projectedState)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.SetPreviewState(projectedState)
                : WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview editing is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadSession> ExtendV2PreviewBaseline(
            WorkloadSession candidate,
            PawnKey pawn)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern) ||
                _boundComponent == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            if (ResolveMode() != WorkloadBackendMode.Modern ||
                candidate == null ||
                candidate.RuntimeBaseline == null ||
                pawn == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The active V2 preview cannot extend its captured live baseline safely.");
            }

            WorkloadOperationResult<WorkloadLiveBaselineCapture> capture =
                new WorkloadV2ApplyService(_boundComponent)
                    .CaptureLiveBaselineCapture(
                        candidate.SourceTemplate.WithState(candidate.ProjectedState));
            if (!capture.Succeeded || capture.Value?.RuntimeBaseline == null)
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    capture.Code,
                    capture.Message);
            }

            WorkloadOwnershipDimensions ownership =
                candidate.SourceTemplate.Definition.OwnershipDimensions;
            bool requiresPawnRuntimeBaseline =
                ownership.Owns(WorkloadStateDimension.ParentPriorities) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) ||
                ownership.Owns(WorkloadStateDimension.SpecificJobOrder);
            if (requiresPawnRuntimeBaseline &&
                !capture.Value.RuntimeBaseline.ContainsPawn(pawn))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The newly included pawn has no authoritative live runtime baseline.");
            }

            PawnKey[] included = { pawn };
            WorkloadRuntimeBaseline merged = candidate.RuntimeBaseline.ExtendForPawns(
                capture.Value.RuntimeBaseline,
                included);
            WorkloadSession extended = candidate.ExtendCapturedBaseline(
                included,
                capture.Value.State,
                merged);
            return modern.SetPreviewSession(extended);
        }

        internal static WorkloadOperationResult<WorkloadSession> RevertV2Preview()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.RevertPreview()
                : WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview editing is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadPreviewPlan> GetV2PreviewPlan(
            WorkloadDecisionKind decisionKind = WorkloadDecisionKind.Apply)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.PreviewPlan(decisionKind)
                : WorkloadOperationResult<WorkloadPreviewPlan>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview planning is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadSemanticDiff> GetV2PreviewDiff()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.PreviewDiff()
                : WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview diff is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult<WorkloadSemanticDiff> GetV2PreviewImpactDiff()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.PreviewImpactDiff()
                : WorkloadOperationResult<WorkloadSemanticDiff>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview impact diff is unavailable while legacy workloads are active.");
        }

        internal static WorkloadV2CommitResult CommitV2Apply()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    WorkloadDecisionKind.Apply,
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "gateway.game",
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.CommitApply()
                : WorkloadV2ApplyService.GatewayFailure(
                    WorkloadDecisionKind.Apply,
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "gateway.mode",
                    "V2 apply is unavailable while legacy workloads are active.");
        }

        internal static WorkloadV2CommitResult CommitV2Update()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    WorkloadDecisionKind.Update,
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "gateway.game",
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.CommitUpdate()
                : WorkloadV2ApplyService.GatewayFailure(
                    WorkloadDecisionKind.Update,
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "gateway.mode",
                    "V2 update is unavailable while legacy workloads are active.");
        }

        internal static WorkloadV2CommitResult CommitV2Fork(
            string stableId,
            string label)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadV2ApplyService.GatewayFailure(
                    WorkloadDecisionKind.Fork,
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "gateway.game",
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.CommitFork(stableId, label)
                : WorkloadV2ApplyService.GatewayFailure(
                    WorkloadDecisionKind.Fork,
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "gateway.mode",
                    "V2 fork is unavailable while legacy workloads are active.");
        }

        internal static WorkloadOperationResult EndV2Preview()
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
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
        private readonly BwtLiveWorkTabEffectiveStateAdapter _liveAdapter;
        private readonly LiveWorkTabEffectiveStateProvider _liveProvider;
        private readonly List<QueuedLifecycleAction> _queuedLifecycleActions =
            new List<QueuedLifecycleAction>();
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
        private long _projectedEditablePawnSetRevision = long.MinValue;

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

        private bool _inspectionActive;
        private string _lastMessage = string.Empty;

        internal WorkloadPreviewController()
        {
            _liveAdapter = new BwtLiveWorkTabEffectiveStateAdapter();
            _liveProvider = _liveAdapter.CreateProvider("bwt.live.workload-preview");
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

        // Update/Fork compare against the stored template, while Apply compares
        // against the live colony baseline captured when the preview opened.
        // Keep both counts visible so a workload that is dirty as a template
        // cannot be mistaken for one that will change the colony by the same
        // number of entries.
        internal int ColonyImpactCount => IsActive ? _session.LiveDiff.Changes.Count : 0;
        internal int TemplateDirtyCount => IsActive ? _session.TemplateDiff.Changes.Count : 0;

        /// <summary>
        /// Schedules and presentation settings still have no complete runtime
        /// writer. Specific-job priorities and ordering do, but every clear
        /// rejected by the session remains a hard commit boundary.
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
                if ((_session.UnsupportedClearDimensions?.Count ?? 0) > 0)
                {
                    return true;
                }

                WorkloadStateDimension[] unsupported =
                {
                    WorkloadStateDimension.Schedules,
                    WorkloadStateDimension.PresentationSettings
                };
                WorkloadSemanticDiff templateDiff = _session.TemplateDiff;
                WorkloadSemanticDiff liveDiff = _session.LiveDiff;
                for (int i = 0; i < unsupported.Length; i++)
                {
                    WorkloadStateDimension dimension = unsupported[i];
                    if (!ownership.Owns(dimension))
                    {
                        continue;
                    }

                    if (ContainsDimension(templateDiff, dimension) ||
                        ContainsDimension(liveDiff, dimension) ||
                        HasStateEntries(_session.TemplateBaselineState, dimension) ||
                        HasStateEntries(_session.ProjectedState, dimension))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private string UnsupportedPresentationCommitReason
        {
            get
            {
                string clearMessage = _session?.GetUnsupportedClearMessage() ?? string.Empty;
                return clearMessage.AnyNonWhitespace()
                    ? clearMessage
                    : "This workload contains schedules or presentation settings without a " +
                      "supported runtime writer. Apply, Update, and Save As are disabled so " +
                      "state cannot be silently dropped.";
            }
        }

        internal bool CanApplyPreview => IsActive && !HasUnsupportedOwnedPresentationState;
        internal bool CanUpdatePreview => IsActive && HasSemanticDiff && !HasUnsupportedOwnedPresentationState;
        internal bool CanForkPreview => IsActive && !HasUnsupportedOwnedPresentationState;
        internal string CommitBlockedMessage => HasUnsupportedOwnedPresentationState
            ? UnsupportedPresentationCommitReason
            : string.Empty;

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

            bool workloadsEnabled = BetterWorkTabMod.Settings?.enableWorkloads ?? true;
            if (!workloadsEnabled)
            {
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
            return WorkTabEffectiveStateScope.Push(ScopedProvider);
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
                    SetMessage("The workload preview operation failed.");
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
                        message = "The workload preview operation could not be completed.";
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

            WorkloadProjectedState projected = _projectedProvider.ProjectedState;

            WorkloadOwnershipDimensions ownership =
                _session.SourceTemplate.Definition.OwnershipDimensions;
            if (_session.ProjectedState.SemanticallyEquals(projected, ownership))
            {
                return true;
            }

            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.SetV2PreviewState(projected);
            if (!result.Succeeded)
            {
                SetMessage(result.Message);
                RebuildProjection(_session.ProjectedState);
                return false;
            }

            _session = result.Value;
            RebuildProjection(_session.ProjectedState);
            return true;
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
                "Finish, Update, Save As, Apply, or Cancel the active workload preview " +
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

                SetMessage(ActivePreviewSwitchBlockedMessage);
                return false;
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

            if (!SynchronizeAfterInput())
            {
                return false;
            }

            if (!CanApplyPreview)
            {
                SetMessage(UnsupportedPresentationCommitReason);
                return false;
            }
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
            if (!IsActive)
            {
                SetMessage("There is no active workload preview.");
                return false;
            }

            if (!SynchronizeAfterInput())
            {
                return false;
            }

            if (!HasSemanticDiff)
            {
                SetMessage("Update is available only when the semantic diff is non-empty.");
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

            if (!SynchronizeAfterInput())
            {
                return false;
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
            _inspectionActive = false;
            _queuedLifecycleActions.Clear();
        }

        internal string ActivePreviewSwitchBlockedMessage =>
            "Finish the active workload preview with Apply, Save As, or Cancel before " +
            "switching workloads. Update is available when the workload has changes.";

        internal bool ShouldRouteInspectionWheel(Event evt, Rect updateRect)
        {
            if (evt == null || evt.type != EventType.ScrollWheel || !HasSemanticDiff)
            {
                return false;
            }

            bool overUpdate = updateRect.width > 0f && updateRect.Contains(evt.mousePosition);
            if (overUpdate)
            {
                _inspectionActive = true;
            }

            return overUpdate;
        }

        internal void UpdateFooterInspectionHover(Rect updateRect)
        {
            if (!IsActive || !HasSemanticDiff || updateRect.width <= 0f)
            {
                _inspectionActive = false;
                return;
            }

            Vector2 pointer = Event.current?.mousePosition ?? Vector2.zero;
            _inspectionActive = updateRect.Contains(pointer);
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

            WorkloadMembershipRecord record = GetMembershipSnapshot().Find(pawnKey);
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
                if (!SetSessionMembership(pawnKey, include: true))
                {
                    return false;
                }

                SetMessage("Pawn included for this application.");
                return true;
            }

            if (record.Classification == WorkloadMembershipClassification.UnrepresentedNew)
            {
                if (!AddCurrentLiveBaseline(pawnKey))
                {
                    return false;
                }

                SetMessage("Pawn included with its current live values for this application.");
                return true;
            }

            if (record.Classification == WorkloadMembershipClassification.Included &&
                record.IsRepresented)
            {
                if (!SetSessionMembership(pawnKey, include: false))
                {
                    return false;
                }

                SetMessage("Pawn excluded from this application; its live values remain unchanged.");
                return true;
            }

            SetMessage("This pawn is outside the saved workload scope and was left unchanged.");
            return false;
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
            WorkloadOwnershipDimensions unsupported =
                WorkloadOwnershipDimensions.Schedules |
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

                if (!ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) &&
                    !ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
                {
                    continue;
                }

                IReadOnlyList<WorkGiver> workGivers =
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(
                        workType,
                        pawn);
                for (int workGiverIndex = 0;
                     workGiverIndex < workGivers.Count;
                     workGiverIndex++)
                {
                    WorkGiverDef workGiver = workGivers[workGiverIndex]?.def;
                    if (workGiver == null || workGiver.defName.NullOrEmpty())
                    {
                        continue;
                    }

                    WorkloadSpecificJobKey specificKey =
                        WorkTabEffectiveStateIds.ForSpecificJob(pawn, workType, workGiver);
                    if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
                    {
                        int inheritedPriority =
                            WorkGiverReassignmentManager.GetWorkGiverPriority(
                                pawn,
                                workGiver,
                                WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(
                                    pawn,
                                    workType));
                        WorkloadScalarValue specificPriority =
                            _liveProvider.GetSpecificJobOverride(
                                specificKey,
                                WorkloadScalarValue.FromInteger(inheritedPriority));
                        draft.SetSpecificJobOverride(specificKey, specificPriority);
                        wroteValue = true;
                    }

                    if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder) &&
                        _liveProvider.TryGetSpecificJobOrder(specificKey, out int order))
                    {
                        draft.SetSpecificJobOrder(specificKey, order);
                        wroteValue = true;
                    }
                }
            }

            if (!wroteValue)
            {
                SetMessage("This workload has no supported current-pawn dimension to include.");
                return false;
            }

            WorkloadSession candidate = _session.EditState(draft.ProjectedState);
            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.ExtendV2PreviewBaseline(candidate, pawnKey);
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

            if ((dimensions & WorkloadOwnershipDimensions.PresentationSettings) != 0)
            {
                values.Add("presentation settings");
            }

            return values.Count == 0
                ? "unsupported state"
                : string.Join(", ", values.ToArray());
        }

        private void OpenSession(WorkloadSession session)
        {
            WorkloadSurfaceCoordinator.OpenPreview();
            _session = session;
            _boundComponent = Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            RebuildProjection(_session.ProjectedState);
            SetMessage("Preview open; live work priorities are unchanged until Apply.");
        }

        private void RebuildProjection(WorkloadProjectedState projectedState)
        {
            InvalidateMembershipSnapshot();
            if (_session == null)
            {
                _projectedProvider = null;
                _projectedEditablePawnSetRevision = long.MinValue;
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
            _projectedEditablePawnSetRevision = ComputeAvailablePawnSetRevision();
            ClearInspectionIndex();
        }

        private void ClearLocalSession()
        {
            WorkloadSurfaceCoordinator.NotifyPreviewClosed();
            InvalidateMembershipSnapshot();
            _session = null;
            _projectedProvider = null;
            _projectedEditablePawnSetRevision = long.MinValue;
            _inspectionActive = false;
            ClearInspectionIndex();
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

        private WorkloadProjectedState BuildStateWithSessionExclusions(
            WorkloadProjectedState projected)
        {
            WorkloadProjectedState baseState = _session.BaseState ?? WorkloadProjectedState.Empty;
            var draft = new WorkloadDraft(baseState);

            for (int i = 0; i < baseState.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = baseState.ParentPriorities[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsParentPriority(projected.ParentPriorities, entry.Key))
                {
                    draft.RemoveParentPriority(entry.Key);
                }
            }

            for (int i = 0; i < projected.ParentPriorities.Count; i++)
            {
                WorkloadParentPriorityEntry entry = projected.ParentPriorities[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn))
                {
                    draft.SetParentPriority(entry.Key, entry.Priority);
                }
            }

            for (int i = 0; i < baseState.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = baseState.ManualModes[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsManualMode(projected.ManualModes, entry.Key))
                {
                    draft.RemoveManualMode(entry.Key);
                }
            }

            for (int i = 0; i < projected.ManualModes.Count; i++)
            {
                WorkloadManualModeEntry entry = projected.ManualModes[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn))
                {
                    draft.SetManualMode(entry.Key, entry.Manual);
                }
            }

            for (int i = 0; i < baseState.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = baseState.Schedules[i];
                if (!_session.ProjectedState.IsExcluded(entry.Pawn) &&
                    !ContainsSchedule(projected.Schedules, entry.Pawn))
                {
                    draft.RemoveSchedule(entry.Pawn);
                }
            }

            for (int i = 0; i < projected.Schedules.Count; i++)
            {
                WorkloadScheduleEntry entry = projected.Schedules[i];
                if (!_session.ProjectedState.IsExcluded(entry.Pawn))
                {
                    draft.SetSchedule(entry.Pawn, entry.Schedule);
                }
            }

            for (int i = 0; i < baseState.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = baseState.SpecificJobOverrides[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsSpecificOverride(projected.SpecificJobOverrides, entry.Key))
                {
                    draft.RemoveSpecificJobOverride(entry.Key);
                }
            }

            for (int i = 0; i < projected.SpecificJobOverrides.Count; i++)
            {
                WorkloadSpecificJobOverrideEntry entry = projected.SpecificJobOverrides[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn))
                {
                    draft.SetSpecificJobOverride(entry.Key, entry.Value);
                }
            }

            for (int i = 0; i < baseState.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = baseState.SpecificJobOrder[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn) &&
                    !ContainsSpecificOrder(projected.SpecificJobOrder, entry.Key))
                {
                    draft.RemoveSpecificJobOrder(entry.Key);
                }
            }

            for (int i = 0; i < projected.SpecificJobOrder.Count; i++)
            {
                WorkloadSpecificJobOrderEntry entry = projected.SpecificJobOrder[i];
                if (!_session.ProjectedState.IsExcluded(entry.Key.Pawn))
                {
                    draft.SetSpecificJobOrder(entry.Key, entry.Order);
                }
            }

            for (int i = 0; i < baseState.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = baseState.PresentationSettings[i];
                if (!ContainsPresentationSetting(projected.PresentationSettings, entry.Key))
                {
                    draft.RemovePresentationSetting(entry.Key);
                }
            }

            for (int i = 0; i < projected.PresentationSettings.Count; i++)
            {
                WorkloadPresentationSettingEntry entry = projected.PresentationSettings[i];
                draft.SetPresentationSetting(entry.Key, entry.Value);
            }

            return draft.ProjectedState;
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
            PawnKey key)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Pawn.Equals(key)) return true;
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
    }
}
