using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.WorkGrid.Diagnostics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Commands
{
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
        internal static bool SetPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            bool accepted = ExecutePriorityMutation(pawn, workType, priority);

            Observe(
                WorkGridCommandKind.SetPriority,
                accepted,
                accepted ? workType.defName : "invalid priority target or bounds");
            return accepted;
        }

        internal static bool SetWorkGiverPriority(int pawnId, WorkGiverDef workGiver, int priority)
        {
            bool accepted = pawnId >= 0 && workGiver != null &&
                            WorkGridCommandMath.IsValidPriority(
                                priority,
                                WorkPrioritySystem.GetRequestableMaxPriority());
            if (accepted)
            {
                WorkGiverReassignmentManager.SetPawnOverrideSynced(
                    pawnId,
                    workGiver.defName,
                    priority);
            }

            Observe(
                WorkGridCommandKind.SetWorkGiverPriority,
                accepted,
                accepted ? workGiver.defName : "invalid work-giver target or bounds");
            return accepted;
        }

        internal static bool OpenSchedule(TimePriorityTarget target, Rect anchor, int fallbackPriority)
        {
            bool accepted = WorkGridCommandMath.IsValidPriority(
                                fallbackPriority,
                                WorkPrioritySystem.GetRequestableMaxPriority()) &&
                            TimePriorityScheduleEditor.OpenForPriorityBox(
                                target,
                                anchor,
                                fallbackPriority);
            Observe(WorkGridCommandKind.OpenSchedule, accepted, accepted ? "opened" : "schedule rejected");
            return accepted;
        }

        internal static bool SelectRuleTarget(
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            Pawn pawn,
            int priority,
            Rect bounds,
            bool header)
        {
            bool accepted = workType != null;
            if (accepted && header)
            {
                RuleBuilderGateway.SelectHeaderForRuleBuilder2(workType, workGiver, bounds);
            }
            else if (accepted && pawn != null)
            {
                RuleBuilderGateway.SelectPriorityCellForRuleBuilder2(
                    workType,
                    workGiver,
                    pawn,
                    priority,
                    bounds);
            }
            else
            {
                accepted = false;
            }

            Observe(WorkGridCommandKind.SelectRuleTarget, accepted, accepted ? workType.defName : "invalid rule target");
            return accepted;
        }

        private static void Observe(WorkGridCommandKind kind, bool accepted, string detail)
        {
            var observation = new WorkGridCommandObservation(kind, accepted, detail);
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
