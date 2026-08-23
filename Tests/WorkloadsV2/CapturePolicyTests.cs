using System;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class CapturePolicyTests
    {
        internal static void Run()
        {
            LabelAndPreflightAreFailClosed();
            ParentSelectionAndScheduleOwnershipAreExplicit();
            ScheduleFallbacksAndTypedSnapshotsAreCaptured();
        }

        private static void LabelAndPreflightAreFailClosed()
        {
            bool usedDefault = false;
            TestAssert.Equal("Given", WorkloadLiveCapturePolicy.ResolveLabel("Given", delegate
            {
                usedDefault = true;
                return "Default";
            }), "a supplied capture label must not evaluate the default label");
            TestAssert.False(usedDefault, "a supplied capture label must not consult the default label");
            TestAssert.Equal("Default", WorkloadLiveCapturePolicy.ResolveLabel(null, delegate
            {
                usedDefault = true;
                return "Default";
            }), "an absent capture label must use the default label");

            bool authorityChecked = false;
            WorkloadOperationResult missingId = WorkloadLiveCapturePolicy.ValidateTemplateCapture(
                null, false, delegate { authorityChecked = true; return true; }, false);
            TestAssert.Equal(WorkloadDiagnosticCode.MissingStableId, missingId.Code,
                "capture must reject a missing stable ID before runtime checks");
            TestAssert.False(authorityChecked, "missing IDs must not query authority");

            WorkloadOperationResult missingMap = WorkloadLiveCapturePolicy.ValidateTemplateCapture(
                "capture", false, delegate { authorityChecked = true; return true; }, true);
            TestAssert.Equal(WorkloadDiagnosticCode.NoCurrentMap, missingMap.Code,
                "capture must reject a missing current map");
            TestAssert.False(authorityChecked, "missing maps must not query authority");

            WorkloadOperationResult blocked = WorkloadLiveCapturePolicy.ValidateTemplateCapture(
                "capture", true, delegate { authorityChecked = true; return false; }, true);
            TestAssert.Equal(WorkloadDiagnosticCode.ExternalPriorityAuthority, blocked.Code,
                "capture must reject an external priority authority");
            TestAssert.True(authorityChecked, "a map-backed capture must query authority");

            WorkloadOperationResult noGame = WorkloadLiveCapturePolicy.ValidateTemplateCapture(
                "capture", true, delegate { return true; }, false);
            TestAssert.Equal(WorkloadDiagnosticCode.NoCurrentGame, noGame.Code,
                "capture must reject unavailable global manual-mode state");
            TestAssert.True(WorkloadLiveCapturePolicy.ValidateTemplateCapture(
                    "capture", true, delegate { return true; }, true).Succeeded,
                "capture must accept a complete map, authority, and global-state preflight");
        }

        private static void ParentSelectionAndScheduleOwnershipAreExplicit()
        {
            TestAssert.Equal(4,
                WorkloadLiveCapturePolicy.SelectParentPriority(true, 4, 2),
                "template capture must select stored parent priority");
            TestAssert.Equal(2,
                WorkloadLiveCapturePolicy.SelectParentPriority(false, 4, 2),
                "preview capture must select effective parent priority");

            WorkloadOwnershipDimensions baseOwnership = WorkloadOwnershipDimensions.ParentPriorities;
            TestAssert.False(
                WorkloadLiveCapturePolicy.CompleteOwnership(baseOwnership, false)
                    .Owns(WorkloadStateDimension.Schedules),
                "schedule ownership must remain absent when no schedule was captured");
            TestAssert.True(
                WorkloadLiveCapturePolicy.CompleteOwnership(baseOwnership, true)
                    .Owns(WorkloadStateDimension.Schedules),
                "schedule ownership must be added only after a schedule was captured");
        }

        private static void ScheduleFallbacksAndTypedSnapshotsAreCaptured()
        {
            PawnKey pawn = TestSupport.Pawn("p1");
            WorkTypeKey workType = TestSupport.WorkType("PlantWork");
            WorkGiverKey workGiver = TestSupport.WorkGiver("PlantCut");
            WorkGiverKey alternateWorkGiver = TestSupport.WorkGiver("PlantHarvest");
            WorkTypeKey alternateWorkType = TestSupport.WorkType("CookWork");
            WorkloadSchedulePayload schedule = TestSupport.ScheduleWithValue(3, 1 << 8);
            var draft = new WorkloadDraft(WorkloadProjectedState.Empty);
            int parentFallback = -1;
            int specificFallback = -1;

            TestAssert.True(WorkloadLiveCapturePolicy.TryCaptureSchedule(
                    draft,
                    WorkloadScheduleTargetKey.ForParent(pawn, workType),
                    6,
                    delegate(WorkloadScheduleTargetKey key, int fallback)
                    {
                        parentFallback = fallback;
                        return schedule;
                    }),
                "parent schedule capture must store a valid payload");
            TestAssert.True(WorkloadLiveCapturePolicy.TryCaptureSchedule(
                    draft,
                    WorkloadScheduleTargetKey.ForWorkGiver(pawn, workType, workGiver),
                    2,
                    delegate(WorkloadScheduleTargetKey key, int fallback)
                    {
                        specificFallback = fallback;
                        return schedule;
                    }),
                "specific schedule capture must store a valid payload");
            TestAssert.Equal(6, parentFallback,
                "parent schedule capture must receive the parent fallback priority");
            TestAssert.Equal(2, specificFallback,
                "specific schedule capture must receive the inherited specific fallback priority");

            WorkloadSpecificJobTargetKey localSpecific =
                WorkloadSpecificJobTargetKey.ForPawn(pawn, workType, workGiver);
            WorkloadSpecificJobTargetKey globalSpecific =
                WorkloadSpecificJobTargetKey.Global(workType, workGiver);
            WorkloadSpecificJobTargetKey localSpecificClear =
                WorkloadSpecificJobTargetKey.ForPawn(pawn, workType, alternateWorkGiver);
            WorkloadSpecificJobTargetKey globalSpecificSet =
                WorkloadSpecificJobTargetKey.Global(workType, alternateWorkGiver);
            WorkloadLiveCapturePolicy.ApplySpecificPrioritySnapshot(
                draft, localSpecific, true, false, 4);
            WorkloadLiveCapturePolicy.ApplySpecificPrioritySnapshot(
                draft, globalSpecific, false, true, 0);
            WorkloadLiveCapturePolicy.ApplySpecificPrioritySnapshot(
                draft, localSpecificClear, false, true, 0);
            WorkloadLiveCapturePolicy.ApplySpecificPrioritySnapshot(
                draft, globalSpecificSet, true, false, 5);

            WorkloadWorkTypeOrderKey localOrder = WorkloadWorkTypeOrderKey.ForPawn(pawn, workType);
            WorkloadWorkTypeOrderKey globalOrder = WorkloadWorkTypeOrderKey.Global(workType);
            WorkloadWorkTypeOrderKey localOrderClear =
                WorkloadWorkTypeOrderKey.ForPawn(pawn, alternateWorkType);
            WorkloadWorkTypeOrderKey globalOrderSet =
                WorkloadWorkTypeOrderKey.Global(alternateWorkType);
            TestAssert.True(WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                    draft, localOrder, true, false, new[] { "PlantCut", "PlantHarvest" }),
                "a local stored order must become a typed Set intent");
            TestAssert.True(WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                    draft, globalOrder, false, true, null),
                "a global cleared order must become a typed Clear intent");
            TestAssert.True(WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                    draft, localOrderClear, false, true, null),
                "a local cleared order must become a typed Clear intent");
            TestAssert.True(WorkloadLiveCapturePolicy.ApplyWorkTypeOrderSnapshot(
                    draft, globalOrderSet, true, false, new[] { "CookSimpleMeal", "CookFineMeal" }),
                "a global stored order must become a typed Set intent");

            WorkloadProjectedState state = draft.ProjectedState;
            WorkloadSpecificPriorityIntentEntry localPriority = TestSupport.Find(
                state.SpecificPriorityIntents, value => value.Key.Equals(localSpecific));
            WorkloadSpecificPriorityIntentEntry globalPriority = TestSupport.Find(
                state.SpecificPriorityIntents, value => value.Key.Equals(globalSpecific));
            WorkloadSpecificPriorityIntentEntry localClearPriority = TestSupport.Find(
                state.SpecificPriorityIntents, value => value.Key.Equals(localSpecificClear));
            WorkloadSpecificPriorityIntentEntry globalSetPriority = TestSupport.Find(
                state.SpecificPriorityIntents, value => value.Key.Equals(globalSpecificSet));
            TestAssert.True(localPriority.Intent.HasValue && localPriority.Intent.Value.Priority == 4,
                "a local stored priority must become a typed Set intent");
            TestAssert.True(globalPriority.Intent.IsClear,
                "a global cleared priority must become a typed Clear intent");
            TestAssert.True(localClearPriority.Intent.IsClear,
                "a local cleared priority must become a typed Clear intent");
            TestAssert.True(globalSetPriority.Intent.HasValue && globalSetPriority.Intent.Value.Priority == 5,
                "a global stored priority must become a typed Set intent");
            WorkloadWorkTypeOrderIntentEntry localOrderIntent = TestSupport.Find(
                state.WorkTypeOrderIntents, value => value.Key.Equals(localOrder));
            WorkloadWorkTypeOrderIntentEntry globalOrderIntent = TestSupport.Find(
                state.WorkTypeOrderIntents, value => value.Key.Equals(globalOrder));
            WorkloadWorkTypeOrderIntentEntry localClearOrderIntent = TestSupport.Find(
                state.WorkTypeOrderIntents, value => value.Key.Equals(localOrderClear));
            WorkloadWorkTypeOrderIntentEntry globalSetOrderIntent = TestSupport.Find(
                state.WorkTypeOrderIntents, value => value.Key.Equals(globalOrderSet));
            TestAssert.True(localOrderIntent.Intent.HasValue,
                "a local stored order must retain its typed payload");
            TestAssert.True(globalOrderIntent.Intent.IsClear,
                "a global cleared order must remain a typed tombstone");
            TestAssert.True(localClearOrderIntent.Intent.IsClear,
                "a local cleared order must remain a typed tombstone");
            TestAssert.True(globalSetOrderIntent.Intent.HasValue,
                "a global stored order must retain its typed payload");
        }
    }
}
