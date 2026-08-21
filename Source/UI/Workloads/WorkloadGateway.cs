using System;
using System.Collections.Generic;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.Settings;
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
                    "Finish, Save, Save As, Apply, or Cancel the active workload preview before opening the workload list.");
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
            if (!TryBind(out LegacyWorkloadBackend legacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Legacy
                ? legacy.Create(label)
                : CreateModernWorkload(modern, label);
        }

        private static WorkloadOperationResult<WorkloadDescriptor> CreateModernWorkload(
            Workload2Backend modern,
            string label)
        {
            if (modern == null)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab workload backend.");
            }

            // Keep the picker/create surface on the same capture path as the
            // explicit V2 capture wrapper. The backend still owns validation
            // and persistence; this adapter only completes typed UI state
            // that the normal Work-tab surfaces already expose.
            string stableId = Guid.NewGuid().ToString("N");
            WorkloadOperationResult<WorkloadTemplate> captured =
                CaptureCurrentV2Template(stableId, label);
            if (!captured.Succeeded)
            {
                return WorkloadOperationResult<WorkloadDescriptor>.Fail(
                    captured.Code,
                    captured.Message);
            }

            return modern.SaveTemplate(captured.Value, true);
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
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            if (ResolveMode() != WorkloadBackendMode.Modern)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 capture is unavailable while legacy workloads are active.");
            }

            WorkloadOperationResult<WorkloadTemplate> captured =
                modern.CaptureCurrentTemplate(stableId, label);
            if (!captured.Succeeded)
            {
                return captured;
            }

            return CompleteCapturedV2Template(captured.Value);
        }

        private static WorkloadOperationResult<WorkloadTemplate> CompleteCapturedV2Template(
            WorkloadTemplate captured)
        {
            if (captured == null || captured.Definition == null)
            {
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The captured workload template is empty.");
            }

            try
            {
                WorkloadOwnershipDimensions ownership =
                    captured.Definition.OwnershipDimensions;
                WorkloadDraft draft = new WorkloadDraft(captured.ProjectedState);
                // A new capture has no prior ownership bit to consult. Read
                // the canonical schedule service and opt into the dimension
                // only when at least one real 24-hour schedule exists.
                bool captureSchedules = true;
                bool capturedSchedule = false;
                IReadOnlyList<WorkTypeDef> workTypes =
                    DefDatabase<WorkTypeDef>.AllDefsListForReading;
                IReadOnlyList<PawnKey> represented =
                    captured.ProjectedState.RepresentedPawnIds;

                // The backend capture already supplies parent and manual
                // values. Complete typed local priorities, whole orders, and
                // schedules from the same canonical read-only services used
                // by the normal Work-tab drilldown/editor surfaces.
                for (int pawnIndex = 0; pawnIndex < represented.Count; pawnIndex++)
                {
                    Pawn pawn = ResolvePawnForCapture(represented[pawnIndex]);
                    if (pawn == null || pawn.workSettings == null)
                    {
                        continue;
                    }

                    for (int workTypeIndex = 0;
                         workTypes != null && workTypeIndex < workTypes.Count;
                         workTypeIndex++)
                    {
                        WorkTypeDef workType = workTypes[workTypeIndex];
                        if (workType == null || workType.defName.NullOrEmpty())
                        {
                            continue;
                        }

                        WorkloadParentPriorityKey parentKey =
                            WorkTabEffectiveStateIds.ForParentPriority(pawn, workType);
                        int parentFallback =
                            WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(
                                pawn,
                                workType);

                        if (captureSchedules)
                        {
                            CaptureScheduleIntent(
                                draft,
                                WorkloadScheduleTargetKey.ForParent(
                                    parentKey.Pawn,
                                    parentKey.WorkType),
                                parentFallback,
                                ref capturedSchedule);
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

                            if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides) &&
                                WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                                    pawn,
                                    workGiver,
                                    out int specificPriority))
                            {
                                draft.SetSpecificPriority(
                                    WorkTabEffectiveStateIds.ForSpecificJobTarget(
                                        pawn,
                                        workType,
                                        workGiver),
                                    specificPriority);
                            }

                            if (captureSchedules)
                            {
                                int fallback =
                                    WorkGiverReassignmentManager.GetWorkGiverPriority(
                                        pawn,
                                        workGiver,
                                        parentFallback);
                                CaptureScheduleIntent(
                                    draft,
                                    WorkloadScheduleTargetKey.ForWorkGiver(
                                        parentKey.Pawn,
                                        parentKey.WorkType,
                                        WorkTabEffectiveStateIds.ForWorkGiver(workGiver)),
                                    fallback,
                                    ref capturedSchedule);
                            }
                        }

                        if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder) &&
                            WorkGiverReassignmentManager.HasPawnOrdering(pawn, workType))
                        {
                            WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot snapshot =
                                WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                                    pawn,
                                    workType);
                            WorkloadWorkTypeOrderPayload payload =
                                CreateOrderPayload(snapshot.OrderedWorkGiverNames);
                            if (payload != null && payload.IsValid)
                            {
                                draft.SetWorkTypeOrder(
                                    WorkTabEffectiveStateIds.ForWorkTypeOrder(
                                        pawn,
                                        workType),
                                    payload);
                            }
                        }
                    }
                }

                // Global/shared state deliberately has no pawn identity. It
                // must be captured outside the membership loop so stale or
                // newly-added pawns cannot strip it from the template.
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
                            if (snapshot.HasStoredValue)
                            {
                                draft.SetSpecificPriority(key, snapshot.Priority);
                            }
                            else if (snapshot.IsExplicitlyCleared)
                            {
                                draft.ClearSpecificPriority(key);
                            }
                        }

                        if (captureSchedules)
                        {
                            CaptureScheduleIntent(
                                draft,
                                WorkloadScheduleTargetKey.GlobalWorkGiver(
                                    WorkTabEffectiveStateIds.ForWorkType(workType),
                                    WorkTabEffectiveStateIds.ForWorkGiver(workGiver)),
                                WorkGiverReassignmentManager.GetWorkGiverPriority(
                                    null,
                                    workGiver,
                                    WorkPrioritySystem.GetDefaultEnabledPriority()),
                                ref capturedSchedule);
                        }
                    }

                    if (ownership.Owns(WorkloadStateDimension.SpecificJobOrder))
                    {
                        WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot snapshot =
                            WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(
                                workType.defName);
                        WorkloadWorkTypeOrderKey key =
                            WorkTabEffectiveStateIds.ForGlobalWorkTypeOrder(workType);
                        if (snapshot.HasStoredValue)
                        {
                            WorkloadWorkTypeOrderPayload payload =
                                CreateOrderPayload(snapshot.OrderedWorkGiverNames);
                            if (payload != null && payload.IsValid)
                            {
                                draft.SetWorkTypeOrder(key, payload);
                            }
                        }
                        else if (snapshot.IsExplicitlyCleared)
                        {
                            draft.ClearWorkTypeOrder(key);
                        }
                    }
                }

                WorkloadOwnershipDimensions completedOwnership = ownership;
                if (capturedSchedule)
                {
                    completedOwnership |= WorkloadOwnershipDimensions.Schedules;
                }

                WorkloadDefinition completedDefinition =
                    new WorkloadDefinition(
                        captured.Definition.StableId,
                        captured.Definition.Label,
                        captured.Definition.SchemaVersion,
                        completedOwnership,
                        captured.Definition.Scope);
                return WorkloadOperationResult<WorkloadTemplate>.Ok(
                    new WorkloadTemplate(completedDefinition, draft.ProjectedState));
            }
            catch (Exception exception)
            {
                Log.Error("[BWT] Typed workload capture failed.\n" + exception);
                return WorkloadOperationResult<WorkloadTemplate>.Fail(
                    WorkloadDiagnosticCode.InvalidState,
                    "The current workload could not be captured with complete typed UI state.");
            }
        }

        internal static void CaptureScheduleIntent(
            WorkloadDraft draft,
            WorkloadScheduleTargetKey key,
            int fallbackPriority,
            ref bool capturedSchedule)
        {
            if (draft == null || key == null || !key.IsValid)
            {
                return;
            }

            if (!TimePriorityService.TryCaptureLiveScheduleSnapshot(
                    key,
                    fallbackPriority,
                    out TimePriorityLiveScheduleSnapshot snapshot,
                    out _))
            {
                return;
            }

            if (!snapshot.HadSchedule || snapshot.Payload == null || !snapshot.Payload.IsValid)
            {
                return;
            }

            draft.SetSchedule(key, snapshot.Payload);
            capturedSchedule = true;
        }

        private static WorkloadWorkTypeOrderPayload CreateOrderPayload(
            IReadOnlyList<string> orderedNames)
        {
            if (orderedNames == null || orderedNames.Count == 0)
            {
                return null;
            }

            var keys = new List<WorkGiverKey>(orderedNames.Count);
            for (int i = 0; i < orderedNames.Count; i++)
            {
                if (orderedNames[i].NullOrEmpty())
                {
                    return null;
                }

                keys.Add(new WorkGiverKey(orderedNames[i]));
            }

            return new WorkloadWorkTypeOrderPayload(keys);
        }

        private static Pawn ResolvePawnForCapture(PawnKey key)
        {
            if (key == null || !int.TryParse(key.Value, out int pawnId) || pawnId <= 0)
            {
                return null;
            }

            IReadOnlyList<Pawn> pawns = PawnsFinder.All_AliveOrDead;
            for (int i = 0; pawns != null && i < pawns.Count; i++)
            {
                if (pawns[i]?.thingIDNumber == pawnId)
                {
                    return pawns[i];
                }
            }

            return null;
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
            return TryTransitionMode(targetMode, persistSettings: true);
        }

        internal static WorkloadOperationResult TryTransitionMode(
            WorkloadBackendMode targetMode,
            bool persistSettings)
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
            if (persistSettings)
            {
                settings.Write();
            }

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
                return V2Unavailable<WorkloadSession>(
                    "V2 preview rebasing is unavailable while legacy workloads are active.");
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
                        recovered.Message);
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
                    WorkloadDiagnosticCode.NotFound,
                    "There is no authoritative rebased V2 preview session to adopt.");
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

        internal static WorkloadOperationResult<WorkloadSession> ExtendV2PreviewBaseline(
            WorkloadSession candidate,
            PawnKey pawn)
        {
            if (!TryBind(out LegacyWorkloadBackend unusedLegacy, out Workload2Backend modern))
            {
                return WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.NoCurrentGame,
                    "There is no current Better Work Tab game.");
            }

            return ResolveMode() == WorkloadBackendMode.Modern
                ? modern.ExtendPreviewBaseline(candidate, pawn)
                : WorkloadOperationResult<WorkloadSession>.Fail(
                    WorkloadDiagnosticCode.UnsupportedOperation,
                    "V2 preview baseline extension is unavailable while legacy workloads are active.");
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
                "V2 save is unavailable while legacy workloads are active.");
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
    internal enum WorkloadInspectionTargetKind
    {
        ParentPriority = 0,
        SpecificPriority = 1,
        Schedule = 2,
        Ordering = 3
    }

    internal readonly struct WorkloadInspectionTarget
    {
        internal WorkloadInspectionTarget(
            WorkloadInspectionTargetKind kind,
            WorkloadTargetScope scope,
            int scheduleKind,
            int pawnId,
            string workType,
            string workGiver)
        {
            Kind = kind;
            Scope = scope;
            ScheduleKind = scheduleKind;
            PawnId = pawnId;
            WorkType = workType ?? string.Empty;
            WorkGiver = workGiver ?? string.Empty;
        }

        internal WorkloadInspectionTargetKind Kind { get; }
        internal WorkloadTargetScope Scope { get; }
        internal int ScheduleKind { get; }
        internal int PawnId { get; }
        internal string WorkType { get; }
        internal string WorkGiver { get; }
    }

    internal sealed class WorkloadPreviewController
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
        private GameComponent_BWTWorldSettings _boundComponent;
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
        private readonly HashSet<InspectionTargetKey> _changedInspectionTargets =
            new HashSet<InspectionTargetKey>();
        private readonly List<WorkloadInspectionTarget> _inspectionTargets =
            new List<WorkloadInspectionTarget>(16);
        private readonly HashSet<int> _changedSchedulePawnIds =
            new HashSet<int>();
        private WorkloadSession _semanticDiffSession;
        private long _semanticDiffSessionRevision = long.MinValue;
        private WorkloadSemanticDiff _cachedTemplateDiff;
        private WorkloadSemanticDiff _cachedLiveDiff;
        private WorkloadSession _inspectionIndexSession;
        private long _inspectionIndexSessionRevision = long.MinValue;
        private WorkloadInspectionContext _inspectionIndexContext;
        private WorkloadInspectionContext _inspectionContext;
        private bool _hasInspectionCellTargets;
        private bool _hasManualModeInspectionChange;
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
                Message = status?.Message ?? string.Empty;
                Result = status?.Result;
            }

            internal string RequestId { get; }
            internal WorkloadMultiplayerCommitState State { get; }
            internal WorkloadDiagnosticCode Code { get; }
            internal string Message { get; }
            internal WorkloadV2CommitResult Result { get; }
        }

        private bool _inspectionActive;
        private string _lastMessage = string.Empty;

        private enum WorkloadInspectionContext
        {
            None = 0,
            Template = 1,
            Live = 2
        }

        private enum InspectionTargetKind
        {
            ParentPriority = 0,
            SpecificPriority = 1,
            Schedule = 2,
            Ordering = 3
        }

        private readonly struct InspectionTargetKey : IEquatable<InspectionTargetKey>
        {
            internal InspectionTargetKey(
                InspectionTargetKind kind,
                WorkloadTargetScope scope,
                int scheduleKind,
                int pawnId,
                string workType,
                string workGiver)
            {
                Kind = kind;
                Scope = scope;
                ScheduleKind = scheduleKind;
                PawnId = pawnId;
                WorkType = workType ?? string.Empty;
                WorkGiver = workGiver ?? string.Empty;
            }

            internal InspectionTargetKind Kind { get; }
            internal WorkloadTargetScope Scope { get; }
            internal int ScheduleKind { get; }
            internal int PawnId { get; }
            internal string WorkType { get; }
            internal string WorkGiver { get; }

            public bool Equals(InspectionTargetKey other)
            {
                return Kind == other.Kind &&
                    Scope == other.Scope &&
                    ScheduleKind == other.ScheduleKind &&
                    PawnId == other.PawnId &&
                    StringComparer.Ordinal.Equals(WorkType, other.WorkType) &&
                    StringComparer.Ordinal.Equals(WorkGiver, other.WorkGiver);
            }

            public override bool Equals(object obj)
            {
                return obj is InspectionTargetKey && Equals((InspectionTargetKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)Kind;
                    hash = (hash * 397) ^ (int)Scope;
                    hash = (hash * 397) ^ ScheduleKind;
                    hash = (hash * 397) ^ PawnId;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkType);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(WorkGiver);
                    return hash;
                }
            }
        }

        internal WorkloadPreviewController()
        {
            _liveAdapter = new BwtLiveWorkTabEffectiveStateAdapter();
            _liveProvider = _liveAdapter.CreateProvider("bwt.live.workload-preview");
            if (Current != null)
            {
                Workload2Backend.MultiplayerCommitStatusChanged -=
                    Current.OnMultiplayerCommitStatusPublished;
            }

            Workload2Backend.MultiplayerCommitStatusChanged +=
                OnMultiplayerCommitStatusPublished;
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

        internal bool HasTemplateDiff => IsActive && !EffectiveTemplateDiff.IsEmpty;
        internal bool HasLiveImpact => IsActive && !EffectiveLiveDiff.IsEmpty;
        // Kept as the template-dirty alias for existing callers. Update and
        // inspection are deliberately about the stored template, not the
        // current colony baseline.
        internal bool HasSemanticDiff => HasTemplateDiff;
        internal bool IsInspectionActive
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
        internal bool HasInspectionCellTargets
        {
            get
            {
                if (!IsInspectionActive)
                {
                    return false;
                }

                EnsureInspectionIndex();
                return _hasInspectionCellTargets;
            }
        }

        internal bool HasManualModeInspectionChange
        {
            get
            {
                if (!IsInspectionActive)
                {
                    return false;
                }

                EnsureInspectionIndex();
                return _hasManualModeInspectionChange;
            }
        }

        internal IReadOnlyList<WorkloadInspectionTarget> InspectionTargets
        {
            get
            {
                EnsureInspectionIndex();
                return _inspectionTargets;
            }
        }
        internal string LastMessage => _lastMessage ?? string.Empty;

        // Update/Fork compare against the stored template, while Apply compares
        // against the live colony baseline captured when the preview opened.
        // Keep both counts visible so a workload that is dirty as a template
        // cannot be mistaken for one that will change the colony by the same
        // number of entries.
        internal int ColonyImpactCount => IsActive ? EffectiveLiveDiff.Changes.Count : 0;
        internal int TemplateDirtyCount => IsActive ? EffectiveTemplateDiff.Changes.Count : 0;

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
                    return false;
                }

                if ((_session.UnsupportedClearDimensions?.Count ?? 0) > 0)
                {
                    return true;
                }

                WorkloadProjectedState[] states =
                {
                    _session.TemplateBaselineState,
                    _session.ProjectedState
                };
                for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                {
                    WorkloadProjectedState state = states[stateIndex];
                    // Legacy value-only payloads are never promoted by the
                    // resolver.  They must remain fail-closed even if an old
                    // record omitted the ownership bit; otherwise the typed
                    // commit path would silently drop them.
                    if (WorkloadV2OwnershipResolver.HasLegacyPayload(
                            state,
                            WorkloadStateDimension.Schedules))
                    {
                        return true;
                    }

                    if (WorkloadV2OwnershipResolver.HasLegacyPayload(
                            state,
                            WorkloadStateDimension.PresentationSettings))
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
                      : "This workload contains a legacy or unsupported workload-owned " +
                      "payload. Apply, Save, and Save As are disabled so state cannot be " +
                      "silently dropped.";
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
            ? "The active preview is recovery-blocked because its committed persistence identity could not be adopted safely. Cancel the preview before retrying."
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
                    return "Multiplayer workload recovery is required. " +
                           "The preview is locked until the retained synchronized rollback lease is explicitly resolved." +
                           (_multiplayerCommitMessage.AnyNonWhitespace()
                               ? " " + _multiplayerCommitMessage
                               : string.Empty);
                }

                if (IsMultiplayerCommitInFlight)
                {
                    return "Waiting for synchronized workload " +
                           MultiplayerDecisionLabel(_multiplayerDecision) + "." +
                           (_multiplayerCommitMessage.AnyNonWhitespace()
                               ? " " + _multiplayerCommitMessage
                               : string.Empty);
                }

                if (_previewRecoveryBlocked)
                {
                    return "The active workload preview is recovery-blocked until its persisted identity is safely adopted or the preview is cancelled.";
                }

                return _multiplayerCommitMessage ?? string.Empty;
            }
        }

        internal string MultiplayerPreviewLabelSuffix
        {
            get
            {
                if (IsMultiplayerRecoveryBlocked)
                {
                    return " • recovery";
                }

                if (IsMultiplayerCommitInFlight)
                {
                    return " • syncing";
                }

                return " • preview";
            }
        }

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

        private static string MultiplayerDecisionLabel(WorkloadDecisionKind decision)
        {
            switch (decision)
            {
                case WorkloadDecisionKind.Apply:
                    return "apply";
                case WorkloadDecisionKind.Update:
                    return "save";
                case WorkloadDecisionKind.Fork:
                    return "Save As";
                default:
                    return "operation";
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
                _multiplayerCommitMessage =
                    state.TerminalState == WorkloadTransactionTerminalState.RollbackFailed
                        ? "The synchronized commit retained a rollback lease and requires explicit recovery."
                        : "The synchronized commit requires rollback acknowledgement.";
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
            _multiplayerCommitMessage = status.Message ?? string.Empty;

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
                    SetMessage(
                        "Multiplayer workload " +
                        failedDecision +
                        " was not committed; the preview is still open. " +
                        (failedMessage.AnyNonWhitespace()
                            ? failedMessage
                            : "No live or stored workload state was changed.") +
                        " Retry is available without changing the draft.");
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
                    _multiplayerCommitMessage =
                        "The synchronized " + MultiplayerDecisionLabel(_multiplayerDecision) +
                        " completed, but the authoritative rebased preview could not be adopted safely.";
                    SetMessage(MultiplayerStatusExplanation);
                    return;
                }

                _session = adopted.Value;
                RebuildProjection(_session.ProjectedState);
                _multiplayerTerminalHandled = true;
                string saveMessage = status?.Message;
                string confirmedDecision = MultiplayerDecisionLabel(_multiplayerDecision);
                ClearMultiplayerAttempt();
                SetMessage(
                    (saveMessage ?? string.Empty).AnyNonWhitespace()
                        ? saveMessage
                        : "The synchronized " + confirmedDecision +
                          " was confirmed; the preview remains open.");
                return;
            }

            _multiplayerTerminalHandled = true;
            WorkloadOperationResult ended = WorkloadGateway.EndV2Preview();
            if (!ended.Succeeded && ended.Code != WorkloadDiagnosticCode.NotFound)
            {
                _multiplayerRecoveryBlocked = true;
                _multiplayerCommitState = WorkloadMultiplayerCommitState.Failed;
                _multiplayerCommitMessage =
                    "The synchronized commit succeeded, but the local preview could not be closed safely.";
                SetMessage(MultiplayerStatusExplanation);
                return;
            }

            string message = status?.Message;
            ClearLocalSession();
            SetMessage(
                (message ?? string.Empty).AnyNonWhitespace()
                    ? message
                    : "The synchronized workload operation was confirmed.");
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

                var blocked = new List<string>();
                if ((_session.UnsupportedClearDimensions?.Count ?? 0) > 0)
                {
                    blocked.Add("explicit clears without typed state");
                }

                WorkloadProjectedState[] states =
                {
                    _session.TemplateBaselineState,
                    _session.ProjectedState
                };
                for (int i = 0; i < states.Length; i++)
                {
                    if (WorkloadV2OwnershipResolver.HasLegacyPayload(
                            states[i],
                            WorkloadStateDimension.Schedules) &&
                        !blocked.Contains("legacy schedules"))
                    {
                        blocked.Add("legacy schedules");
                    }

                    if (WorkloadV2OwnershipResolver.HasLegacyPayload(
                            states[i],
                            WorkloadStateDimension.PresentationSettings) &&
                        !blocked.Contains("legacy presentation settings"))
                    {
                        blocked.Add("legacy presentation settings");
                    }
                }
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
            // Transport callbacks are consumed here, before the provider scope
            // is entered for this Work-tab pass. Never assume an AsyncLocal
            // effective-state scope exists on the callback thread.
            DrainMultiplayerStatusQueue();
            PollMultiplayerRollbackState();

            GameComponent_BWTWorldSettings component =
                Verse.Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (!ReferenceEquals(component, _boundComponent))
            {
                if (IsMultiplayerCommitInFlight)
                {
                    _multiplayerRecoveryBlocked = true;
                    _multiplayerCommitMessage =
                        "The game context changed while synchronized workload state was in flight.";
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

                return true;
            }

            WorkloadProjectedState projected = _projectedProvider.ProjectedState;

            if (_session.ProjectedState.SemanticallyEquals(
                    projected,
                    WorkloadOwnershipDimensions.All))
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
            ResetCompletedMultiplayerAttemptIfPayloadChanged();
            return true;
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
            if (IsMultiplayerCommitInFlight)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            if (!HasStagedWorkloadChanges)
            {
                return true;
            }

            SetMessage(
                "Finish, Save, Save As, Apply, or Cancel the active workload preview " +
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

        private bool BeginMultiplayerPreviewCommit(
            WorkloadDecisionKind decision,
            string forkStableId,
            string forkLabel)
        {
            if (!IsActive)
            {
                SetMessage("There is no active workload preview.");
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
            _multiplayerCommitMessage =
                "The request was created from one immutable preview snapshot.";

            WorkloadMultiplayerCommitStatus status =
                WorkloadGateway.BeginV2MultiplayerCommit(
                    decision,
                    _multiplayerForkStableId,
                    _multiplayerForkLabel,
                    _multiplayerIdempotencyKey);
            if (status == null)
            {
                _multiplayerCommitState = WorkloadMultiplayerCommitState.Rejected;
                _multiplayerCommitMessage =
                    "The multiplayer workload transaction could not be created.";
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            _multiplayerRequestId = status.RequestId ?? string.Empty;
            _multiplayerCommitState = status.State;
            _multiplayerCommitMessage = status.Message ?? string.Empty;
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
                SetMessage(result.Message);
                return false;
            }

            ClearLocalSession();
            SetMessage(result.Message);
            return true;
        }

        private bool AdoptRebasedPreview(WorkloadV2CommitResult result)
        {
            string operationLabel = result?.Report?.DecisionKind == WorkloadDecisionKind.Fork
                ? "Save As"
                : "Save";
            if (result == null || result.RebasedSession == null)
            {
                _previewRecoveryBlocked = true;
                SetMessage(
                    "The workload " + operationLabel.ToLowerInvariant() +
                    " succeeded, but its authoritative rebased preview was not available. Cancel the preview before retrying.");
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
                SetMessage(
                    "The workload " + operationLabel.ToLowerInvariant() +
                    " succeeded, but the persisted identity could not be adopted safely. Cancel the preview before retrying.");
                return false;
            }

            _session = adopted.Value;
            _previewRecoveryBlocked = false;
            RebuildProjection(_session.ProjectedState);
            ResetCompletedMultiplayerAttemptIfPayloadChanged();
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
                SetMessage("Save is available only when the semantic diff is non-empty.");
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
                SetMessage("Save is available only when the semantic diff is non-empty.");
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

            if (!AdoptRebasedPreview(result))
            {
                return false;
            }

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
                SetMessage(result.Message);
                return false;
            }

            if (!AdoptRebasedPreview(result))
            {
                return false;
            }

            SetMessage(result.Message);
            return true;
        }

        internal void ResetForWindowClose()
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
                           "Finish, Save, Save As, Apply, or Cancel the active preview " +
                           "before switching workloads.";
                }

                return "Finish the active workload preview with Apply, Save As, or Cancel before " +
                       "switching workloads. Save is available when the workload has changes.";
            }
        }

        internal bool ShouldRouteInspectionWheel(Event evt, Rect updateRect)
        {
            return ShouldRouteInspectionWheel(evt, Rect.zero, updateRect, Rect.zero);
        }

        internal bool ShouldRouteInspectionWheel(
            Event evt,
            Rect updateRect,
            Rect applyRect)
        {
            return ShouldRouteInspectionWheel(
                evt,
                Rect.zero,
                updateRect,
                applyRect);
        }

        internal bool ShouldRouteInspectionWheel(
            Event evt,
            Rect saveAsRect,
            Rect updateRect,
            Rect applyRect)
        {
            if (evt == null || evt.type != EventType.ScrollWheel || !IsActive)
            {
                return false;
            }

            bool overApply = applyRect.width > 0f && applyRect.Contains(evt.mousePosition);
            bool overSaveAs = saveAsRect.width > 0f && saveAsRect.Contains(evt.mousePosition);
            bool overUpdate = updateRect.width > 0f && updateRect.Contains(evt.mousePosition);
            bool overInspection =
                (overApply && HasLiveImpact) ||
                (overSaveAs && HasTemplateDiff) ||
                (overUpdate && HasTemplateDiff);
            if (overInspection)
            {
                _inspectionActive = true;
                _inspectionContext = overApply
                    ? WorkloadInspectionContext.Live
                    : WorkloadInspectionContext.Template;
            }

            return overInspection;
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

        internal bool IsInspectionRowLevelChanged(Pawn pawn)
        {
            EnsureInspectionIndex();
            return pawn != null && pawn.thingIDNumber > 0 &&
                _changedSchedulePawnIds.Contains(pawn.thingIDNumber);
        }

        internal bool HasInspectionRowLevelChanges
        {
            get
            {
                if (!IsInspectionActive)
                {
                    return false;
                }

                EnsureInspectionIndex();
                return _changedSchedulePawnIds.Count > 0;
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
                SetMessage("The specific-job preview target is no longer available.");
                return false;
            }

            if (!_session.SourceTemplate.Definition.OwnershipDimensions.Owns(
                    WorkloadStateDimension.SpecificJobOverrides))
            {
                SetMessage("The active workload does not own specific-job priorities.");
                return false;
            }

            return EditPreviewDraft(
                draft => draft.SetSpecificPriorityIntent(key, intent),
                "The specific-job preview could not be changed.");
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
                SetMessage("The WorkGiver order preview target is no longer available.");
                return false;
            }

            if (!_session.SourceTemplate.Definition.OwnershipDimensions.Owns(
                    WorkloadStateDimension.SpecificJobOrder))
            {
                SetMessage("The active workload does not own WorkGiver ordering.");
                return false;
            }

            return EditPreviewDraft(
                draft => draft.SetWorkTypeOrderIntent(key, intent),
                "The WorkGiver order preview could not be changed.");
        }

        internal bool SetSchedulePreviewIntent(
            WorkloadScheduleTargetKey key,
            WorkloadIntent<WorkloadSchedulePayload> intent)
        {
            if (IsUnsafePreviewInputBlocked)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

            if (!IsActive || key == null || !key.IsValid)
            {
                SetMessage("The schedule preview target is no longer available.");
                return false;
            }

            if (!_session.SourceTemplate.Definition.OwnershipDimensions.Owns(
                    WorkloadStateDimension.Schedules))
            {
                SetMessage("The active workload does not own schedules.");
                return false;
            }

            return EditPreviewDraft(
                draft => draft.SetScheduleIntent(key, intent),
                "The schedule preview could not be changed.");
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

            WorkloadOperationResult<WorkloadSession> result =
                WorkloadGateway.EditV2Preview(edit);
            if (!result.Succeeded || result.Value == null)
            {
                SetMessage(string.IsNullOrEmpty(result.Message) ? failureMessage : result.Message);
                return false;
            }

            _session = result.Value;
            RebuildProjection(_session.ProjectedState);
            ResetCompletedMultiplayerAttemptIfPayloadChanged();
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
            if (IsUnsafePreviewInputBlocked)
            {
                SetMessage(MultiplayerStatusExplanation);
                return false;
            }

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
            ResetCompletedMultiplayerAttemptIfPayloadChanged();
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

            var draft = new WorkloadDraft(_session.ProjectedState);
            IReadOnlyList<WorkTypeDef> workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            bool wroteValue = false;
            bool capturedSchedule = false;
            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef workType = workTypes[i];
                if (workType == null || pawn.workSettings == null || !pawn.workSettings.EverWork)
                {
                    continue;
                }

                WorkloadParentPriorityKey key =
                    WorkTabEffectiveStateIds.ForParentPriority(pawn, workType);
                int parentFallback =
                    WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
                if (ownership.Owns(WorkloadStateDimension.ParentPriorities))
                {
                    draft.SetParentPriority(
                        key,
                        _liveProvider.GetParentPriority(
                            key,
                            parentFallback));
                    wroteValue = true;
                }

                if (ownership.Owns(WorkloadStateDimension.Schedules))
                {
                    WorkloadGateway.CaptureScheduleIntent(
                        draft,
                        WorkloadScheduleTargetKey.ForParent(
                            key.Pawn,
                            key.WorkType),
                        parentFallback,
                        ref capturedSchedule);
                    wroteValue |= capturedSchedule;
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
                    int inheritedPriority =
                        WorkGiverReassignmentManager.GetWorkGiverPriority(
                            pawn,
                            workGiver,
                            parentFallback);
                    if (ownership.Owns(WorkloadStateDimension.SpecificJobOverrides))
                    {
                        WorkloadScalarValue specificPriority =
                            _liveProvider.GetSpecificJobOverride(
                                specificKey,
                                WorkloadScalarValue.FromInteger(inheritedPriority));
                        draft.SetSpecificJobOverride(specificKey, specificPriority);
                        wroteValue = true;
                    }

                    if (ownership.Owns(WorkloadStateDimension.Schedules))
                    {
                        bool scheduleBefore = capturedSchedule;
                        WorkloadGateway.CaptureScheduleIntent(
                            draft,
                            WorkloadScheduleTargetKey.ForWorkGiver(
                                key.Pawn,
                                key.WorkType,
                                WorkTabEffectiveStateIds.ForWorkGiver(workGiver)),
                            inheritedPriority,
                            ref capturedSchedule);
                        wroteValue |= capturedSchedule || scheduleBefore;
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
            ClearMultiplayerAttempt();
            _previewRecoveryBlocked = false;
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
            _inspectionContext = WorkloadInspectionContext.None;
            ClearInspectionIndex();
            ClearMultiplayerAttempt();
            _previewRecoveryBlocked = false;
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

        private void EnsureSemanticDiffCache()
        {
            if (!IsActive)
            {
                _semanticDiffSession = null;
                _semanticDiffSessionRevision = long.MinValue;
                _cachedTemplateDiff = null;
                _cachedLiveDiff = null;
                return;
            }

            if (ReferenceEquals(_semanticDiffSession, _session) &&
                _semanticDiffSessionRevision == _session.SessionRevision &&
                _cachedTemplateDiff != null &&
                _cachedLiveDiff != null)
            {
                return;
            }

            WorkloadOperationResult<WorkloadSemanticDiff> templateResult =
                WorkloadGateway.GetV2PreviewDiff();
            WorkloadOperationResult<WorkloadSemanticDiff> liveResult =
                WorkloadGateway.GetV2PreviewImpactDiff();
            _cachedTemplateDiff = templateResult.Succeeded && templateResult.Value != null
                ? templateResult.Value
                : _session.TemplateDiff;
            _cachedLiveDiff = liveResult.Succeeded && liveResult.Value != null
                ? liveResult.Value
                : _session.LiveDiff;
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
            _changedInspectionTargets.Clear();
            _inspectionTargets.Clear();
            _changedSchedulePawnIds.Clear();
            _hasInspectionCellTargets = false;
            _hasManualModeInspectionChange = false;
            if (diff != null)
            {
                WorkloadProjectedState inspectionBaseline =
                    _inspectionContext == WorkloadInspectionContext.Live
                        ? _session.LiveBaselineState
                        : _session.TemplateBaselineState;
                _hasManualModeInspectionChange =
                    WorkloadInspectionSemantics.HasEffectiveManualModeChange(
                        inspectionBaseline,
                        _session.ProjectedState);

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
                                _changedInspectionTargets.Add(new InspectionTargetKey(
                                    InspectionTargetKind.ParentPriority,
                                    WorkloadTargetScope.PawnLocal,
                                    0,
                                    pawnId,
                                    workType.Value,
                                    null));
                                _hasInspectionCellTargets = true;
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
                                _changedSchedulePawnIds.Add(pawnId);
                            }
                            if (workType != null && workType.IsValid &&
                                (scope == WorkloadTargetScope.GlobalShared || pawnId > 0))
                            {
                                _changedInspectionTargets.Add(new InspectionTargetKey(
                                    InspectionTargetKind.Schedule,
                                    scope,
                                    (int)scheduleKind,
                                    scope == WorkloadTargetScope.GlobalShared ? -1 : pawnId,
                                    workType.Value,
                                    workGiver?.Value));
                                _hasInspectionCellTargets = true;
                            }
                            break;
                        case WorkloadStateDimension.SpecificJobOverrides:
                            if (workType != null && workType.IsValid &&
                                workGiver != null && workGiver.IsValid &&
                                (scope == WorkloadTargetScope.GlobalShared || pawnId > 0))
                            {
                                _changedInspectionTargets.Add(new InspectionTargetKey(
                                    InspectionTargetKind.SpecificPriority,
                                    scope,
                                    0,
                                    scope == WorkloadTargetScope.GlobalShared ? -1 : pawnId,
                                    workType.Value,
                                    workGiver.Value));
                                _hasInspectionCellTargets = true;
                            }
                            break;
                        case WorkloadStateDimension.SpecificJobOrder:
                            if (workType != null && workType.IsValid &&
                                (scope == WorkloadTargetScope.GlobalShared || pawnId > 0))
                            {
                                _changedInspectionTargets.Add(new InspectionTargetKey(
                                    InspectionTargetKind.Ordering,
                                    scope,
                                    0,
                                    scope == WorkloadTargetScope.GlobalShared ? -1 : pawnId,
                                    workType.Value,
                                    null));
                                _hasInspectionCellTargets = true;
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

            foreach (InspectionTargetKey target in _changedInspectionTargets)
            {
                _inspectionTargets.Add(new WorkloadInspectionTarget(
                    (WorkloadInspectionTargetKind)target.Kind,
                    target.Scope,
                    target.ScheduleKind,
                    target.PawnId,
                    target.WorkType,
                    target.WorkGiver));
            }

            _inspectionIndexSession = _session;
            _inspectionIndexSessionRevision = _session.SessionRevision;
            _inspectionIndexContext = _inspectionContext;
        }

        private void ClearInspectionIndex()
        {
            _changedInspectionTargets.Clear();
            _inspectionTargets.Clear();
            _changedSchedulePawnIds.Clear();
            _hasInspectionCellTargets = false;
            _hasManualModeInspectionChange = false;
            _inspectionIndexSession = null;
            _inspectionIndexSessionRevision = long.MinValue;
            _inspectionIndexContext = WorkloadInspectionContext.None;
            _semanticDiffSession = null;
            _semanticDiffSessionRevision = long.MinValue;
            _cachedTemplateDiff = null;
            _cachedLiveDiff = null;
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
                    (scopeValue != (int)WorkloadTargetScope.PawnLocal &&
                     scopeValue != (int)WorkloadTargetScope.GlobalShared))
                {
                    return false;
                }

                scope = (WorkloadTargetScope)scopeValue;
                if (dimension == WorkloadStateDimension.Schedules)
                {
                    if (!TryReadIntegerToken(key, ref typedCursor, out int kindValue) ||
                        (kindValue != (int)WorkloadScheduleTargetKind.ParentWorkType &&
                         kindValue != (int)WorkloadScheduleTargetKind.WorkGiver))
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
