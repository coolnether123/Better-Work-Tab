using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Commands
{
    internal static class WorkPriorityCommandGateway
    {
        private static string ExternalPriorityAuthorityReason =>
            "BWT_Priority_ReadOnlyExternal".Translate();

        internal static bool TrySetPreviewParentPriority(
            Pawn pawn,
            WorkTypeDef workType,
            int priority)
        {
            if (pawn?.workSettings == null || workType == null || pawn.Dead ||
                !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType) ||
                !IsValidPriority(priority))
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

            return ParentPriorityRead.TrySetObserved(
                pawn,
                workType,
                priority);
        }

        internal static bool SetWorkGiverPriority(int pawnId, WorkGiverDef workGiver, int priority)
        {
            bool accepted = (pawnId >= 0 || pawnId == -1) &&
                            workGiver != null &&
                            IsValidPriority(priority);
            if (accepted && !PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                return false;
            }

            if (accepted && WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                WorkTypeDef workType =
                    WorkGiverReassignmentManager.GetTargetWorkType(workGiver) ?? workGiver.workType;
                accepted = pawnId == -1
                    ? WorkTabEffectiveStateRuntime.TrySetSpecificJobPriority(
                        WorkTabEffectiveStateIds.ForGlobalSpecificJobTarget(workType, workGiver),
                        priority,
                        out _)
                    : WorkTabEffectiveStateRuntime.TrySetSpecificJobPriority(
                        pawnId,
                        workType,
                        workGiver,
                        priority,
                        out _);
                return accepted;
            }

            if (accepted)
            {
                WorkGiverReassignmentManager.SetPawnOverrideSynced(pawnId, workGiver.defName, priority);
            }

            return accepted;
        }

        internal static bool OpenSchedule(TimePriorityTarget target, Rect anchor, int fallbackPriority)
        {
            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.Schedule,
                    ExternalPriorityAuthorityReason);
                return false;
            }

            return IsValidPriority(fallbackPriority) &&
                   TimePriorityScheduleEditor.OpenForPriorityBox(target, anchor, fallbackPriority);
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

            return accepted;
        }

        internal static bool TryClearPreviewSpecificJobOverrides(Pawn pawn, WorkTypeDef workType)
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
                    "BWT_Workload_SpecificJobOrderTargetMissing".Translate());
                return false;
            }

            return true;
        }

        private static bool IsValidPriority(int priority) =>
            priority >= WorkPrioritySystem.DisabledPriority &&
            priority <= WorkPrioritySystem.GetRequestableMaxPriority();

    }
}
