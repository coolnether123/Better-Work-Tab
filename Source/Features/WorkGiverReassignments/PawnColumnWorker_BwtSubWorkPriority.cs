using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// BWT-owned work-priority column for native expand-beside specific-job columns.
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
            WorkGiverDef workGiver = BwtExpandBesideColumns.TryGetWorkGiver(def);
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

            int parentPriority = ParentPriorityRead.GetLive(pawn, workType);
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
