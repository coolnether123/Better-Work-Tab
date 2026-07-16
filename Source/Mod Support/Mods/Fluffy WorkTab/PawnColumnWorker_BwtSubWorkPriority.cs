using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// BWT-owned work-priority column used by the Fluffy-style expand-beside layout when
    /// Fluffy Work Tab is not installed. Rendering and input remain in BWT's existing
    /// sub-work pipeline; this worker supplies vanilla sizing and stable pawn sorting.
    /// </summary>
    public sealed class PawnColumnWorker_BwtSubWorkPriority : PawnColumnWorker_WorkPriority
    {
        public override int GetMinWidth(PawnTable table)
        {
            return Mathf.Max(base.GetMinWidth(table), 32);
        }

        public override int GetOptimalWidth(PawnTable table)
        {
            return Mathf.Clamp(39, GetMinWidth(table), GetMaxWidth(table));
        }

        public override int GetMaxWidth(PawnTable table)
        {
            return Mathf.Min(base.GetMaxWidth(table), 80);
        }

        public override int Compare(Pawn a, Pawn b)
        {
            WorkGiverDef workGiver = FluffyWorkTabGateway.TryGetHostedWorkGiver(def);
            if (workGiver == null)
            {
                return base.Compare(a, b);
            }

            return GetPrioritySortValue(a, workGiver).CompareTo(GetPrioritySortValue(b, workGiver));
        }

        private static float GetPrioritySortValue(Pawn pawn, WorkGiverDef workGiver)
        {
            WorkTypeDef workType = WorkGiverReassignmentManager.GetTargetWorkType(workGiver) ?? workGiver?.workType;
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork || workType == null)
            {
                return -2f;
            }

            if (pawn.WorkTypeIsDisabled(workType))
            {
                return -1f;
            }

            int parentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            int priority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, workGiver, parentPriority);
            priority = TimePriorityService.GetEffectiveWorkGiverPriority(
                pawn,
                workType,
                workGiver,
                priority);
            if (priority <= WorkPrioritySystem.DisabledPriority)
            {
                return -1f;
            }

            return WorkPrioritySystem.GetMaxPriority() + 1 - priority;
        }
    }
}
