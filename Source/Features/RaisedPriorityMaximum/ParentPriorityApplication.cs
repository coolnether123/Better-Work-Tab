using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.RaisedPriorityMaximum
{
    internal static class ParentPriorityApplication
    {
        internal static bool SetDisplayedParentPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (!CanApplyParentPriority(pawn, workType, priority) ||
                !TryCaptureStoredExpectation(pawn, workType, out ParentPriorityCommandExpectation stored))
            {
                return false;
            }

            int displayFallback = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            TimePriorityEvaluation presentation = TimePriorityService.EvaluateWorkTypePriority(
                pawn,
                workType,
                displayFallback);
            if (!presentation.HasSchedule || stored.StoredPriority <= WorkPrioritySystem.DisabledPriority)
            {
                return Submit(pawn, workType, priority, stored, default, -1);
            }

            if (!TimePriorityService.TryCaptureLiveScheduleSnapshot(
                    presentation.Target,
                    displayFallback,
                    out TimePriorityLiveScheduleSnapshot snapshot,
                    out _) ||
                !snapshot.HadSchedule ||
                !snapshot.Payload.IsPinned(presentation.Hour) ||
                snapshot.AuthorityRevision != stored.AuthorityRevision)
            {
                return false;
            }

            return Submit(
                pawn,
                workType,
                priority,
                new ParentPriorityCommandExpectation(
                    snapshot.AuthorityRevision,
                    stored.StoredPriority,
                    snapshot.FallbackPriority,
                    snapshot.ServiceVersion,
                    snapshot.Payload.Priorities,
                    snapshot.Payload.PinnedHourMask),
                presentation.Target,
                presentation.Hour);
        }

        internal static bool SetStoredParentPriority(Pawn pawn, WorkTypeDef workType, int priority) =>
            CanApplyParentPriority(pawn, workType, priority) &&
            TryCaptureStoredExpectation(pawn, workType, out ParentPriorityCommandExpectation expectation) &&
            Submit(pawn, workType, priority, expectation, default, -1);

        private static bool Submit(
            Pawn pawn,
            WorkTypeDef workType,
            int priority,
            ParentPriorityCommandExpectation expectation,
            TimePriorityTarget target,
            int hour)
        {
            if (MultiplayerBridge.Active)
            {
                if (hour < 0)
                {
                    SyncSetStoredParentPriority(
                        pawn.thingIDNumber,
                        workType.defName,
                        priority,
                        expectation.AuthorityRevision,
                        expectation.StoredPriority);
                }
                else
                {
                    SyncSetScheduledParentPriority(
                        pawn.thingIDNumber,
                        workType.defName,
                        hour,
                        priority,
                        expectation.AuthorityRevision,
                        expectation.StoredPriority,
                        expectation.ScheduleFallbackPriority,
                        expectation.ScheduleVersion,
                        expectation.PinnedHourMask,
                        expectation.CopySchedulePriorities());
                }

                return true;
            }

            return Apply(pawn, workType, target, hour, priority, expectation) != PriorityMutationOutcome.Rejected;
        }

        [SyncMethod]
        internal static void SyncSetStoredParentPriority(
            int pawnId,
            string workTypeDefName,
            int priority,
            long expectedAuthorityRevision,
            int expectedStoredPriority)
        {
            if (TryResolveSubmittedTarget(pawnId, workTypeDefName, out Pawn pawn, out WorkTypeDef workType) &&
                CanApplyParentPriority(pawn, workType, priority))
            {
                Apply(
                    pawn,
                    workType,
                    default,
                    -1,
                    priority,
                    new ParentPriorityCommandExpectation(expectedAuthorityRevision, expectedStoredPriority));
            }
        }

        [SyncMethod]
        internal static void SyncSetScheduledParentPriority(
            int pawnId,
            string workTypeDefName,
            int hour,
            int priority,
            long expectedAuthorityRevision,
            int expectedStoredPriority,
            int expectedScheduleFallbackPriority,
            int expectedScheduleVersion,
            int expectedPinnedHourMask,
            int[] expectedSchedulePriorities)
        {
            if (!TryResolveSubmittedTarget(pawnId, workTypeDefName, out Pawn pawn, out WorkTypeDef workType) ||
                !CanApplyParentPriority(pawn, workType, priority))
            {
                return;
            }

            var expectation = new ParentPriorityCommandExpectation(
                expectedAuthorityRevision,
                expectedStoredPriority,
                expectedScheduleFallbackPriority,
                expectedScheduleVersion,
                expectedSchedulePriorities,
                expectedPinnedHourMask);
            if (expectation.HasSchedule)
            {
                Apply(
                    pawn,
                    workType,
                    TimePriorityTarget.ForRuntimeWorkType(pawn, workType),
                    hour,
                    priority,
                    expectation);
            }
        }

        private static bool TryCaptureStoredExpectation(
            Pawn pawn,
            WorkTypeDef workType,
            out ParentPriorityCommandExpectation expectation)
        {
            expectation = null;
            if (!WorkPrioritySystem.TryCaptureBwtMutationAuthority(out long authorityRevision) ||
                !WorkPrioritySystem.TryGetRawStoredPriority(pawn.workSettings, workType, out int storedPriority) ||
                !WorkPrioritySystem.IsBwtMutationAuthorityCurrent(authorityRevision))
            {
                return false;
            }

            expectation = new ParentPriorityCommandExpectation(authorityRevision, storedPriority);
            return true;
        }

        private static PriorityMutationOutcome Apply(
            Pawn pawn,
            WorkTypeDef workType,
            TimePriorityTarget target,
            int hour,
            int priority,
            ParentPriorityCommandExpectation expectation)
        {
            return hour < 0
                ? WorkPrioritySystem.ApplyPriority(pawn.workSettings, workType, priority, expectation)
                : TimePriorityService.ApplyPriorityAtHour(
                    target,
                    hour,
                    priority,
                    expectation.ScheduleFallbackPriority,
                    expectation,
                    pawn.workSettings,
                    workType);
        }

        private static bool TryResolveSubmittedTarget(
            int pawnId,
            string workTypeDefName,
            out Pawn pawn,
            out WorkTypeDef workType)
        {
            pawn = null;
            workType = null;
            if (pawnId < 0 || string.IsNullOrEmpty(workTypeDefName))
            {
                return false;
            }

            foreach (Pawn candidate in PawnsFinder.All_AliveOrDead)
            {
                if (candidate?.thingIDNumber != pawnId)
                {
                    continue;
                }

                if (pawn != null && !object.ReferenceEquals(pawn, candidate))
                {
                    return false;
                }

                pawn = candidate;
            }

            workType = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeDefName);
            return pawn != null && workType != null;
        }

        private static bool CanApplyParentPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            bool valid = pawn?.workSettings != null &&
                         workType != null &&
                         !pawn.Dead &&
                         pawn.workSettings.EverWork &&
                         !pawn.WorkTypeIsDisabled(workType) &&
                         priority >= WorkPrioritySystem.DisabledPriority &&
                         priority <= WorkPrioritySystem.GetRequestableMaxPriority();
            if (!valid || PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData) return valid;

            WorkTabEffectiveStateRuntime.ReportBlocked(
                WorkTabEffectiveStateDimension.ParentPriority,
                "BWT_Priority_ReadOnlyExternal".Translate());
            return false;
        }
    }
}
