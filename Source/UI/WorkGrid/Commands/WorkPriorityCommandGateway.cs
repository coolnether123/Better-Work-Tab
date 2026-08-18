using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Commands
{
    internal interface IWorkGridCommandObserver
    {
        void OnCommand(in WorkGridCommandObservation observation);
    }

    internal readonly struct WorkGridCommandObservation
    {
        internal WorkGridCommandObservation(WorkGridCommandKind kind, bool accepted, string detail)
        {
            Kind = kind;
            Accepted = accepted;
            Detail = detail ?? string.Empty;
        }

        internal WorkGridCommandKind Kind { get; }
        internal bool Accepted { get; }
        internal string Detail { get; }
    }

    /// <summary>
    /// Validates semantic Work-grid commands and delegates every mutation to its existing
    /// authoritative service. Interaction feedback deliberately remains in the invoking handler.
    /// </summary>
    internal static class WorkPriorityCommandGateway
    {
        private const string ExternalPriorityAuthorityReason =
            "Better Work Tab is read-only while an external priority authority is active.";

        private static IWorkGridCommandObserver _observer;

        internal static IWorkGridCommandObserver Observer
        {
            get => _observer;
            set => _observer = value;
        }

        internal static bool Execute(in SetPriorityCommand command)
        {
            bool accepted = ExecutePriorityMutation(command.Pawn, command.WorkType, command.Priority);

            Observe(
                command.Kind,
                accepted,
                accepted
                    ? command.WorkType.defName
                    : PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData
                        ? "invalid priority target or bounds"
                        : ExternalPriorityAuthorityReason);
            return accepted;
        }

        internal static bool Execute(in StepPriorityCommand command)
        {
            int nextPriority = WorkPrioritySystem.GetPriorityAfterBoundedStep(
                command.CurrentPriority,
                command.Direction);
            bool accepted = ExecutePriorityMutation(
                command.Pawn,
                command.WorkType,
                nextPriority);
            Observe(
                command.Kind,
                accepted,
                accepted
                    ? "stepped"
                    : PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData
                        ? "invalid priority target"
                        : ExternalPriorityAuthorityReason);
            return accepted;
        }

        internal static bool Execute(in TogglePriorityCommand command)
        {
            int nextPriority = command.CurrentPriority > WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : WorkPrioritySystem.GetDefaultEnabledPriority();
            bool accepted = ExecutePriorityMutation(command.Pawn, command.WorkType, nextPriority);
            Observe(
                command.Kind,
                accepted,
                accepted
                    ? "toggled"
                    : PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData
                        ? "invalid priority target"
                        : ExternalPriorityAuthorityReason);
            return accepted;
        }

        internal static bool Execute(in PaintPriorityRangeCommand command)
        {
            bool accepted = PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData &&
                            command.Targets != null &&
                            WorkGridCommandMath.IsValidPriority(
                                command.Priority,
                                WorkPrioritySystem.GetRequestableMaxPriority());
            if (accepted)
            {
                for (int i = 0; i < command.Targets.Count; i++)
                {
                    PriorityPaintTarget target = command.Targets[i];
                    if (!ExecutePriorityMutation(target.Pawn, target.WorkType, command.Priority))
                    {
                        accepted = false;
                        break;
                    }
                }
            }

            Observe(
                command.Kind,
                accepted,
                accepted
                    ? "painted=" + command.Targets.Count
                    : PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData
                        ? "invalid paint range"
                        : ExternalPriorityAuthorityReason);
            return accepted;
        }

        internal static bool Execute(in SetWorkGiverPriorityCommand command)
        {
            bool accepted = command.PawnId >= 0 && command.WorkGiver != null &&
                            WorkGridCommandMath.IsValidPriority(
                                command.Priority,
                                WorkPrioritySystem.GetRequestableMaxPriority());
            if (accepted && !PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                Observe(command.Kind, false, ExternalPriorityAuthorityReason);
                return false;
            }

            if (accepted)
            {
                if (WorkTabEffectiveStateRuntime.IsPreviewActive)
                {
                    WorkTypeDef targetWorkType =
                        WorkGiverReassignmentManager.GetTargetWorkType(command.WorkGiver) ??
                        command.WorkGiver.workType;
                    accepted = WorkTabEffectiveStateRuntime.TrySetSpecificJobPriority(
                        command.PawnId,
                        targetWorkType,
                        command.WorkGiver,
                        command.Priority,
                        out WorkTabEffectiveStateMutationResult result);
                    Observe(
                        command.Kind,
                        accepted,
                        accepted ? result.ToString() : result.Reason);
                    return accepted;
                }

                WorkGiverReassignmentManager.SetPawnOverrideSynced(
                    command.PawnId,
                    command.WorkGiver.defName,
                    command.Priority);
            }

            Observe(command.Kind, accepted, accepted ? command.WorkGiver.defName : "invalid work-giver target or bounds");
            return accepted;
        }

        internal static bool Execute(in OpenScheduleCommand command)
        {
            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.Schedule,
                    ExternalPriorityAuthorityReason);
                Observe(command.Kind, false, ExternalPriorityAuthorityReason);
                return false;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.Schedule,
                    "The live hourly schedule editor has no safe projection adapter.");
                Observe(command.Kind, false, "preview schedule editing is blocked");
                return false;
            }

            bool accepted = WorkGridCommandMath.IsValidPriority(
                                command.FallbackPriority,
                                WorkPrioritySystem.GetRequestableMaxPriority()) &&
                            TimePriorityScheduleEditor.OpenForPriorityBox(
                                command.Target,
                                command.Anchor,
                                command.FallbackPriority);
            Observe(command.Kind, accepted, accepted ? "opened" : "schedule rejected");
            return accepted;
        }

        internal static bool Execute(in SelectRuleTargetCommand command)
        {
            bool accepted = command.WorkType != null;
            if (accepted && command.Header)
            {
                RuleBuilderGateway.SelectHeaderForRuleBuilder2(command.WorkType, command.WorkGiver, command.Bounds);
            }
            else if (accepted && command.Pawn != null)
            {
                RuleBuilderGateway.SelectPriorityCellForRuleBuilder2(
                    command.WorkType,
                    command.WorkGiver,
                    command.Pawn,
                    command.Priority,
                    command.Bounds);
            }
            else
            {
                accepted = false;
            }

            Observe(command.Kind, accepted, accepted ? command.WorkType.defName : "invalid rule target");
            return accepted;
        }

        internal static bool Execute(in MoveWorkGiverCommand command, out string errorMessage)
        {
            errorMessage = null;
            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                errorMessage = ExternalPriorityAuthorityReason;
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    errorMessage);
                Observe(command.Kind, false, errorMessage);
                return false;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                errorMessage = "Specific-job ordering is blocked in preview because the active Work-tab layout owner cannot consume projected order.";
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOrder,
                    errorMessage);
                Observe(command.Kind, false, errorMessage);
                return false;
            }

            bool accepted = WorkGridCommandMath.IsValidMove(
                                command.WorkGiverDefName,
                                command.WorkTypeDefName,
                                command.InsertIndex) &&
                            WorkGiverReassignmentManager.TryMoveWorkGiverLayout(
                                command.WorkGiverDefName,
                                command.WorkTypeDefName,
                                command.InsertIndex,
                                out errorMessage);
            if (!accepted && errorMessage == null)
            {
                errorMessage = "Invalid work-giver move command.";
            }

            Observe(command.Kind, accepted, accepted ? command.WorkGiverDefName : errorMessage);
            return accepted;
        }

        internal static bool TryClearPreviewSpecificJobOverrides(
            Pawn pawn,
            WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    ExternalPriorityAuthorityReason);
                return false;
            }

            if (!WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                WorkGiverReassignmentManager.ClearPawnOverridesForWorkTypeSynced(
                    pawn.thingIDNumber,
                    workType.defName);
                return true;
            }

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            bool attempted = false;
            IList<WorkGiverDef> definitions = workType.workGiversByPriority;
            for (int i = 0; definitions != null && i < definitions.Count; i++)
            {
                WorkGiverDef workGiver = definitions[i];
                if (workGiver?.defName == null || !seen.Add(workGiver.defName))
                {
                    continue;
                }

                attempted = true;
                if (!WorkTabEffectiveStateRuntime.TryClearSpecificJobPriority(
                        pawn,
                        workType,
                        workGiver,
                        out _))
                {
                    return false;
                }
            }

            IReadOnlyList<WorkGiver> display =
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType, pawn);
            for (int i = 0; display != null && i < display.Count; i++)
            {
                WorkGiverDef workGiver = display[i]?.def;
                if (workGiver?.defName == null || !seen.Add(workGiver.defName))
                {
                    continue;
                }

                attempted = true;
                if (!WorkTabEffectiveStateRuntime.TryClearSpecificJobPriority(
                        pawn,
                        workType,
                        workGiver,
                        out _))
                {
                    return false;
                }
            }

            if (!attempted)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    "No specific-job keys were available for the requested work type.");
                return false;
            }

            return true;
        }

        private static void Observe(WorkGridCommandKind kind, bool accepted, string detail)
        {
            var observation = new WorkGridCommandObservation(kind, accepted, detail);
            _observer?.OnCommand(in observation);
            WorkGridRendererDiagnostics.RecordCommand(in observation);
        }

        private static bool ExecutePriorityMutation(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (pawn?.workSettings == null || workType == null || pawn.Dead ||
                !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType) ||
                !WorkGridCommandMath.IsValidPriority(priority, WorkPrioritySystem.GetRequestableMaxPriority()))
            {
                return false;
            }

            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.ParentPriority,
                    ExternalPriorityAuthorityReason);
                return false;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                return WorkTabEffectiveStateRuntime.TrySetParentPriority(
                    pawn,
                    workType,
                    priority,
                    out _);
            }

            int basePriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            TimePriorityEvaluation presentation =
                TimePriorityService.EvaluateWorkTypePriority(pawn, workType, basePriority);
            if (presentation.HasSchedule && basePriority > WorkPrioritySystem.DisabledPriority)
            {
                // A scheduled cell displays the current hour's effective priority. Mutate that
                // same value so a click cannot appear to revert by changing only the hidden base.
                TimePriorityService.SetPriorityAtHourSynced(
                    presentation.Target,
                    presentation.Hour,
                    priority,
                    basePriority);
                return true;
            }

            WorkPrioritySystem.SetPrioritySynced(
                pawn.thingIDNumber,
                workType.defName,
                priority);
            return true;
        }
    }
}
