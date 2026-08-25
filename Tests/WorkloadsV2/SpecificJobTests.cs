using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Workloads.Projection;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class SpecificJobTests
    {
        public static void Run()
        {
            var pawn = TestSupport.Pawn("p1");
            var workType = TestSupport.WorkType("PlantWork");
            var workGiver = TestSupport.WorkGiver("PlantCut");
            var secondWorkGiver = TestSupport.WorkGiver("PlantHarvest");
            var local = WorkloadSpecificJobTargetKey.ForPawn(pawn, workType, workGiver);
            var global = WorkloadSpecificJobTargetKey.Global(workType, workGiver);
            var localOrder = WorkloadWorkTypeOrderKey.ForPawn(pawn, workType);
            var globalOrder = WorkloadWorkTypeOrderKey.Global(workType);
            var order = new WorkloadWorkTypeOrderPayload(new[] { workGiver, secondWorkGiver });

            TestAssert.True(local.IsValid && global.IsValid, "local and global specific-job targets must be valid");
            TestAssert.True(order.IsValid, "a complete unique WorkGiver permutation must be valid");
            TestAssert.False(
                new WorkloadWorkTypeOrderPayload(new[] { workGiver, workGiver }).IsValid,
                "duplicate WorkGiver taxonomy must be rejected");
            TestAssert.False(
                new WorkloadWorkTypeOrderPayload(new[] { workGiver }, false).IsValid,
                "incomplete WorkGiver taxonomy must be rejected");

            var globalProvider = TestSupport.LiveProvider(
                WorkloadScalarValue.FromBoolean(false),
                new WorkloadSpecificPriorityPayload(6),
                order,
                WorkloadScheduleTargetKey.ForParent(pawn, workType),
                TestSupport.Schedule(0));
            var projection = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(WorkloadProjectedState.Empty),
                globalProvider,
                WorkloadOwnershipDimensions.SpecificJobOverrides | WorkloadOwnershipDimensions.SpecificJobOrder,
                "test.specific",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            var v2 = (IWorkTabEffectiveStateV2Editor)projection;

            var globalSet = v2.SetSpecificJobPriority(global, new WorkloadSpecificPriorityPayload(3));
            TestAssert.True(globalSet.Accepted, "shared/global specific-job priority must be writable in preview");
            var globalResolution = projection.ResolveSpecificJobPriority(global);
            TestAssert.True(globalResolution.IsSet && globalResolution.Value.Priority == 3,
                "shared/global priority must be visible through the projection");

            var globalFallback = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(WorkloadProjectedState.Empty),
                globalProvider,
                WorkloadOwnershipDimensions.SpecificJobOverrides | WorkloadOwnershipDimensions.SpecificJobOrder,
                "test.global-fallback",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            var fallbackResolution = globalFallback.ResolveEffectiveSpecificJobPriority(global);
            TestAssert.True(fallbackResolution.IsSet && fallbackResolution.Value.Priority == 6,
                "NoOpinion must fall through to the shared live specific-job value");

            var localSet = v2.SetSpecificJobPriority(local, new WorkloadSpecificPriorityPayload(2));
            TestAssert.True(localSet.Accepted, "pawn-local specific-job priority must be writable in preview");
            TestAssert.Equal(2, projection.ResolveSpecificJobPriority(local).Value.Priority,
                "local specific-job priority must override the shared value");

            var localClear = v2.ClearSpecificJobPriority(local);
            TestAssert.True(localClear.Accepted, "pawn-local specific-job clear must be writable in preview");
            TestAssert.True(projection.ResolveSpecificJobPriority(local).IsClear,
                "specific-job clear must remain an explicit tombstone");

            var localCleared = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(WorkloadProjectedState.Empty),
                globalProvider,
                WorkloadOwnershipDimensions.SpecificJobOverrides | WorkloadOwnershipDimensions.SpecificJobOrder,
                "test.local-clear",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            TestAssert.True(
                ((IWorkTabEffectiveStateV2Editor)localCleared).ClearSpecificJobPriority(local).Accepted,
                "a local Clear must be writable in an isolated preview");
            TestAssert.True(
                localCleared.ResolveEffectiveSpecificJobPriority(local).IsClear,
                "a local Clear must suppress the live local/global fallback instead of resurrecting it");

            var projectedGlobal = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(WorkloadProjectedState.Empty),
                globalProvider,
                WorkloadOwnershipDimensions.SpecificJobOverrides | WorkloadOwnershipDimensions.SpecificJobOrder,
                "test.projected-global",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            var projectedGlobalEditor = (IWorkTabEffectiveStateV2Editor)projectedGlobal;
            TestAssert.True(
                projectedGlobalEditor.SetSpecificJobPriority(global, new WorkloadSpecificPriorityPayload(4)).Accepted,
                "projected global priority must be writable");
            TestAssert.True(
                projectedGlobalEditor.ClearSpecificJobPriority(local).Accepted,
                "local Clear must be writable alongside a projected global value");
            TestAssert.Equal(
                4,
                projectedGlobal.ResolveEffectiveSpecificJobPriority(local).Value.Priority,
                "a local Clear may reveal the projected shared value");

            TestAssert.True(
                new WorkloadDraft(WorkloadProjectedState.Empty)
                    .SetSpecificPriorityIntent(local, WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                        new WorkloadSpecificPriorityPayload(3)))
                    .ClearSpecificPriority(local)
                    .SetSpecificPriority(local, 4)
                    .ProjectedState.SpecificPriorityIntents.Count == 1,
                "set-clear-readd must retain one final typed specific-job intent");
            TestAssert.True(
                new WorkloadDraft(WorkloadProjectedState.Empty)
                    .SetSpecificPriority(local, 4)
                    .ClearSpecificPriority(local)
                    .SetSpecificPriorityNoOpinion(local)
                    .ProjectedState.SpecificPriorityIntents.Count == 0,
                "set-clear-remove must normalize the final NoOpinion to dictionary absence");
            TestAssert.True(
                WorkloadProjectedState.Empty.SemanticallyEquals(
                    new WorkloadProjectedState(
                        specificPriorityIntents: new[]
                        {
                            TestSupport.SpecificIntent(local, WorkloadIntentState.NoOpinion)
                        }),
                    WorkloadOwnershipDimensions.SpecificJobOverrides),
                "an absent specific-job opinion and a NoOpinion record must be semantically equal");

            var orderSet = v2.SetWorkTypeOrder(globalOrder, order);
            TestAssert.True(orderSet.Accepted, "shared/global WorkGiver ordering must be writable in preview");
            TestAssert.True(projection.ResolveWorkTypeOrder(globalOrder).IsSet,
                "shared/global ordering must be projected");
            var orderClear = v2.ClearWorkTypeOrder(globalOrder);
            TestAssert.True(orderClear.Accepted, "shared/global WorkGiver ordering clear must be writable");
            TestAssert.True(projection.ResolveWorkTypeOrder(globalOrder).IsClear,
                "ordering clear must retain tombstone state");
            TestAssert.True(
                projection.ResolveEffectiveWorkTypeOrder(globalOrder).IsClear,
                "ordering Clear must suppress the live shared ordering");
            var invalidPriorityTarget = WorkloadSpecificJobTargetKey.ForPawn(
                pawn,
                workType,
                new WorkGiverKey(string.Empty));
            TestAssert.False(invalidPriorityTarget.IsValid,
                "missing WorkGiver identity must be rejected before mutation");
            TestAssert.True(
                v2.SetSpecificJobPriority(invalidPriorityTarget, new WorkloadSpecificPriorityPayload(1)).IsBlocked,
                "missing WorkGiver mutation must fail closed");

            TestAssert.True(
                v2.SetWorkTypeOrder(globalOrder, new WorkloadWorkTypeOrderPayload(new[] { workGiver, workGiver })).IsBlocked,
                "taxonomy conflicts must fail closed instead of mutating partial order state");

            var clearIntent = WorkloadIntent<WorkloadSpecificPriorityPayload>.Clear;
            var noOpinionIntent = WorkloadIntent<WorkloadSpecificPriorityPayload>.NoOpinion;
            var setIntent = WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                new WorkloadSpecificPriorityPayload(3));
            TestAssert.False(clearIntent.Equals(noOpinionIntent), "clear and NoOpinion must not compare equal");
            TestAssert.False(clearIntent.Equals(setIntent), "clear and Set must not compare equal");
            TestAssert.True(clearIntent.IsClear && noOpinionIntent.IsNoOpinion && setIntent.HasValue,
                "specific-job intent states must remain typed");

            var duplicateSpecificState = new WorkloadProjectedState(
                specificPriorityIntents: new[]
                {
                    TestSupport.SpecificIntent(global, WorkloadIntentState.Set, 2),
                    TestSupport.SpecificIntent(global, WorkloadIntentState.Clear)
                },
                workTypeOrderIntents: new[]
                {
                    TestSupport.OrderIntent(
                        globalOrder,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(order)),
                    TestSupport.OrderIntent(
                        globalOrder,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.Clear)
                });
            TestAssert.True(
                duplicateSpecificState.HasAmbiguousSpecificPriorityIntents &&
                duplicateSpecificState.HasAmbiguousWorkTypeOrderIntents,
                "duplicate typed targets must retain an explicit ambiguity diagnostic");
            TestAssert.Equal(0, duplicateSpecificState.SpecificPriorityIntents.Count,
                "ambiguous priority targets must not select Set over Clear");
            TestAssert.Equal(0, duplicateSpecificState.WorkTypeOrderIntents.Count,
                "ambiguous order targets must not select Set over Clear");
            var duplicateValidation = WorkloadValidator.Validate(
                TestSupport.Template(duplicateSpecificState));
            TestAssert.True(
                duplicateValidation.Contains(WorkloadValidationCode.DuplicateSpecificJobIntent) &&
                duplicateValidation.Contains(WorkloadValidationCode.DuplicateSpecificJobOrderIntent),
                "duplicate typed targets must fail through structured validation");

            TestAssert.True(localOrder.IsValid && globalOrder.IsValid,
                "local and global ordering targets must preserve their scope identity");
        }
    }
}
