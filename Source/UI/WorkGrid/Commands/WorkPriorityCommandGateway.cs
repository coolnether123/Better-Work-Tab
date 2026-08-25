using System.Collections.Generic;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Contracts;
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
            if (!CanHandleParentPriorityInput(pawn, workType) ||
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

        /// <summary>
        /// Determines whether a parent work-priority cell can accept normal
        /// input. This is deliberately separate from priority authority: an
        /// external provider can make an otherwise actionable cell read-only.
        /// </summary>
        internal static bool CanHandleParentPriorityInput(Pawn pawn, WorkTypeDef workType) =>
            WorkTabActionability.CanApplyParent(pawn, workType);

        internal static bool CanHandleSpecificJobInput(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver)
        {
            return WorkTabActionability.CanApplySpecific(pawn, workType, workGiver);
        }

        internal static bool SetWorkGiverPriority(
            WorkTabApplication application,
            int pawnId,
            WorkGiverDef workGiver,
            int priority)
        {
            bool accepted = (pawnId >= 0 || pawnId == -1) &&
                            workGiver != null &&
                            IsValidPriority(priority);
            WorkTypeDef workType = accepted
                ? WorkGiverReassignmentManager.GetTargetWorkType(workGiver) ?? workGiver.workType
                : null;
            if (accepted &&
                pawnId >= 0 &&
                !CanHandleSpecificJobInput(TimePriorityService.FindPawn(pawnId), workType, workGiver))
            {
                return false;
            }

            if (accepted && !PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                return false;
            }

            if (accepted && WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                WorkTabSpecificJobTarget target = WorkTabSpecificJobTarget.For(
                    pawnId == -1 ? null : TimePriorityService.FindPawn(pawnId),
                    workType,
                    workGiver);
                accepted = WorkTabEffectiveStateRuntime.TrySetPreviewSpecificJobPriority(
                    target,
                    priority,
                    out _);
                return accepted;
            }

            if (accepted)
            {
                accepted = application?
                    .SubmitSpecificPriority(pawnId, workGiver, priority).Accepted == true;
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

        internal static bool TryClearPreviewSpecificJobOverrides(
            WorkTabApplication application,
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
                return application?
                    .ClearSpecificPriorities(pawn, workType).Accepted == true;
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
                if (!WorkTabEffectiveStateRuntime.TryClearPreviewSpecificJobPriority(
                        WorkTabSpecificJobTarget.For(pawn, workType, workGiver),
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
                if (!WorkTabEffectiveStateRuntime.TryClearPreviewSpecificJobPriority(
                        WorkTabSpecificJobTarget.For(pawn, workType, workGiver),
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
