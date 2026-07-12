using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
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
        private static IWorkGridCommandObserver _observer;

        internal static IWorkGridCommandObserver Observer
        {
            get => _observer;
            set => _observer = value;
        }

        internal static bool Execute(in SetPriorityCommand command)
        {
            bool accepted = ExecutePriorityMutation(command.Pawn, command.WorkType, command.Priority);

            Observe(command.Kind, accepted, accepted ? command.WorkType.defName : "invalid priority target or bounds");
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
            Observe(command.Kind, accepted, accepted ? "stepped" : "invalid priority target");
            return accepted;
        }

        internal static bool Execute(in TogglePriorityCommand command)
        {
            int nextPriority = command.CurrentPriority > WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : WorkPrioritySystem.GetDefaultEnabledPriority();
            bool accepted = ExecutePriorityMutation(command.Pawn, command.WorkType, nextPriority);
            Observe(command.Kind, accepted, accepted ? "toggled" : "invalid priority target");
            return accepted;
        }

        internal static bool Execute(in PaintPriorityRangeCommand command)
        {
            bool accepted = command.Targets != null &&
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

            Observe(command.Kind, accepted, accepted ? "painted=" + command.Targets.Count : "invalid paint range");
            return accepted;
        }

        internal static bool Execute(in SetWorkGiverPriorityCommand command)
        {
            bool accepted = command.PawnId >= 0 && command.WorkGiver != null &&
                            WorkGridCommandMath.IsValidPriority(
                                command.Priority,
                                WorkPrioritySystem.GetRequestableMaxPriority());
            if (accepted)
            {
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

        private static void Observe(WorkGridCommandKind kind, bool accepted, string detail)
        {
            var observation = new WorkGridCommandObservation(kind, accepted, detail);
            _observer?.OnCommand(in observation);
            WorkGridRendererDiagnostics.RecordCommand(in observation);
        }

        private static bool ExecutePriorityMutation(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (pawn?.workSettings == null || workType == null || pawn.Dead ||
                !WorkGridCommandMath.IsValidPriority(priority, WorkPrioritySystem.GetRequestableMaxPriority()))
            {
                return false;
            }

            WorkPrioritySystem.SetPriority(pawn.workSettings, workType, priority);
            return true;
        }
    }
}
