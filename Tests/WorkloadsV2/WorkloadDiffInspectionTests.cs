using System;
using Better_Work_Tab.Features.Workloads.V2;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkloadDiffInspectionTests
    {
        public static void Run()
        {
            ValueOnlyChangeContractIsPreserved();
            LegacyValueChangesCarryTypedTargets();
            TypedIntentChangesCarryScopedTargets();
            RemovedTypedChangesRetainBeforeTarget();
            MembershipChangesCarryPawnTargets();
        }

        private static void ValueOnlyChangeContractIsPreserved()
        {
            var change = new WorkloadChange(
                WorkloadStateDimension.ParentPriorities,
                WorkloadChangeKind.Changed,
                "legacy-key",
                "before",
                "after");

            TestAssert.Equal("legacy-key", change.CanonicalKey,
                "the original value-only constructor must keep the supplied canonical key");
            TestAssert.Equal("before", change.BeforeValue,
                "the original value-only constructor must keep the before value");
            TestAssert.Equal("after", change.AfterValue,
                "the original value-only constructor must keep the after value");
            TestAssert.True(change.InspectionTarget == null,
                "legacy direct construction must remain value-only when no typed target is supplied");
        }

        private static void LegacyValueChangesCarryTypedTargets()
        {
            PawnKey pawn = TestSupport.Pawn("p1");
            WorkTypeKey workType = TestSupport.WorkType("PlantWork");
            WorkGiverKey workGiver = TestSupport.WorkGiver("PlantCut");
            WorkGiverKey globalWorkGiver = TestSupport.WorkGiver("PlantSow");
            var parentKey = new WorkloadParentPriorityKey(pawn, workType);
            var localJobKey = new WorkloadSpecificJobKey(pawn, workType, workGiver);
            var globalJobKey = new WorkloadSpecificJobKey(
                WorkloadTargetScope.GlobalShared,
                null,
                workType,
                globalWorkGiver);

            var before = new WorkloadProjectedState(
                parentPriorities: new[] { new WorkloadParentPriorityEntry(parentKey, 3) },
                manualModes: new[] { new WorkloadManualModeEntry(parentKey, false) },
                schedules: new[] { new WorkloadScheduleEntry(pawn, new ScheduleKey(2)) },
                specificJobOverrides: new[]
                {
                    new WorkloadSpecificJobOverrideEntry(
                        localJobKey,
                        WorkloadScalarValue.FromInteger(3)),
                    new WorkloadSpecificJobOverrideEntry(
                        globalJobKey,
                        WorkloadScalarValue.FromInteger(4))
                },
                specificJobOrder: new[]
                {
                    new WorkloadSpecificJobOrderEntry(localJobKey, 1),
                    new WorkloadSpecificJobOrderEntry(globalJobKey, 2)
                },
                representedPawnIds: new[] { pawn });
            var after = new WorkloadProjectedState(
                parentPriorities: new[] { new WorkloadParentPriorityEntry(parentKey, 4) },
                manualModes: new[] { new WorkloadManualModeEntry(parentKey, true) },
                schedules: new[] { new WorkloadScheduleEntry(pawn, new ScheduleKey(5)) },
                specificJobOverrides: new[]
                {
                    new WorkloadSpecificJobOverrideEntry(
                        localJobKey,
                        WorkloadScalarValue.FromInteger(6)),
                    new WorkloadSpecificJobOverrideEntry(
                        globalJobKey,
                        WorkloadScalarValue.FromInteger(7))
                },
                specificJobOrder: new[]
                {
                    new WorkloadSpecificJobOrderEntry(localJobKey, 3),
                    new WorkloadSpecificJobOrderEntry(globalJobKey, 4)
                },
                representedPawnIds: new[] { pawn });

            WorkloadSemanticDiff diff = WorkloadSemanticDiff.Between(before, after);
            AssertDiffCanonicalForms(diff, before, after, "legacy values");

            WorkloadChange parent = FindChange(
                diff,
                WorkloadStateDimension.ParentPriorities,
                parentKey.CanonicalKey);
            AssertChangeValues(parent, parentKey.CanonicalKey, "3", "4", "legacy parent priority");
            AssertTarget(parent, "p1", "PlantWork", null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "legacy parent priority");

            WorkloadChange manual = FindChange(
                diff,
                WorkloadStateDimension.ManualModes,
                parentKey.CanonicalKey);
            AssertChangeValues(
                manual,
                parentKey.CanonicalKey,
                WorkloadCanonical.Boolean(false),
                WorkloadCanonical.Boolean(true),
                "legacy manual mode");
            AssertTarget(manual, "p1", "PlantWork", null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "legacy manual mode");

            string legacyScheduleKey = WorkloadCanonical.Encode(pawn.Value);
            WorkloadChange schedule = FindChange(
                diff,
                WorkloadStateDimension.Schedules,
                legacyScheduleKey);
            AssertChangeValues(schedule, legacyScheduleKey, "2", "5", "legacy schedule");
            AssertTarget(schedule, "p1", null, null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "legacy schedule");

            WorkloadChange localOverride = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOverrides,
                localJobKey.CanonicalKey);
            AssertChangeValues(
                localOverride,
                localJobKey.CanonicalKey,
                WorkloadScalarValue.FromInteger(3).CanonicalValue,
                WorkloadScalarValue.FromInteger(6).CanonicalValue,
                "legacy local specific priority");
            AssertTarget(localOverride, "p1", "PlantWork", "PlantCut",
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "legacy local specific priority");

            WorkloadChange globalOverride = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOverrides,
                globalJobKey.CanonicalKey);
            AssertChangeValues(
                globalOverride,
                globalJobKey.CanonicalKey,
                WorkloadScalarValue.FromInteger(4).CanonicalValue,
                WorkloadScalarValue.FromInteger(7).CanonicalValue,
                "legacy global specific priority");
            AssertTarget(globalOverride, null, "PlantWork", "PlantSow",
                WorkloadTargetScope.GlobalShared,
                WorkloadScheduleTargetKind.ParentWorkType,
                "legacy global specific priority");

            WorkloadChange localOrder = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOrder,
                localJobKey.CanonicalKey);
            AssertChangeValues(localOrder, localJobKey.CanonicalKey, "1", "3", "legacy local order");
            AssertTarget(localOrder, "p1", "PlantWork", "PlantCut",
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "legacy local order");

            WorkloadChange globalOrder = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOrder,
                globalJobKey.CanonicalKey);
            AssertChangeValues(globalOrder, globalJobKey.CanonicalKey, "2", "4", "legacy global order");
            AssertTarget(globalOrder, null, "PlantWork", "PlantSow",
                WorkloadTargetScope.GlobalShared,
                WorkloadScheduleTargetKind.ParentWorkType,
                "legacy global order");
        }

        private static void TypedIntentChangesCarryScopedTargets()
        {
            PawnKey pawn = TestSupport.Pawn("p2");
            WorkTypeKey workType = TestSupport.WorkType("PlantWork");
            WorkGiverKey localWorkGiver = TestSupport.WorkGiver("PlantCut");
            WorkGiverKey globalWorkGiver = TestSupport.WorkGiver("PlantSow");
            var parentKey = new WorkloadParentPriorityKey(pawn, workType);
            var parentScheduleKey = WorkloadScheduleTargetKey.ForParent(pawn, workType);
            var localScheduleKey = WorkloadScheduleTargetKey.ForWorkGiver(pawn, workType, localWorkGiver);
            var globalScheduleKey = WorkloadScheduleTargetKey.GlobalWorkGiver(workType, globalWorkGiver);
            var localSpecificKey = WorkloadSpecificJobTargetKey.ForPawn(pawn, workType, localWorkGiver);
            var globalSpecificKey = WorkloadSpecificJobTargetKey.Global(workType, globalWorkGiver);
            var localOrderKey = WorkloadWorkTypeOrderKey.ForPawn(pawn, workType);
            var globalOrderKey = WorkloadWorkTypeOrderKey.Global(workType);

            WorkloadSchedulePayload oldSchedule = TestSupport.ScheduleWithValue(2, 1 << 4);
            WorkloadSchedulePayload newSchedule = TestSupport.ScheduleWithValue(5, 1 << 4);
            var oldOrder = new WorkloadWorkTypeOrderPayload(new[] { localWorkGiver });
            var newOrder = new WorkloadWorkTypeOrderPayload(new[] { globalWorkGiver });

            var before = new WorkloadProjectedState(
                representedPawnIds: new[] { pawn },
                parentPriorityIntents: new[]
                {
                    TestSupport.ParentPriorityIntent(
                        pawn, workType, WorkloadIntentState.Set, 3)
                },
                manualModeIntents: new[]
                {
                    new WorkloadManualModeIntentEntry(
                        parentKey,
                        WorkloadIntent<bool>.CreateSet(false))
                },
                scheduleIntents: new[]
                {
                    TestSupport.ScheduleIntent(parentScheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(oldSchedule)),
                    TestSupport.ScheduleIntent(localScheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(oldSchedule)),
                    TestSupport.ScheduleIntent(globalScheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(oldSchedule))
                },
                specificPriorityIntents: new[]
                {
                    TestSupport.SpecificIntent(localSpecificKey, WorkloadIntentState.Set, 3),
                    TestSupport.SpecificIntent(globalSpecificKey, WorkloadIntentState.Set, 4)
                },
                workTypeOrderIntents: new[]
                {
                    TestSupport.OrderIntent(localOrderKey,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(oldOrder)),
                    TestSupport.OrderIntent(globalOrderKey,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(oldOrder))
                });
            var after = new WorkloadProjectedState(
                representedPawnIds: new[] { pawn },
                parentPriorityIntents: new[]
                {
                    TestSupport.ParentPriorityIntent(
                        pawn, workType, WorkloadIntentState.Set, 6)
                },
                manualModeIntents: new[]
                {
                    new WorkloadManualModeIntentEntry(
                        parentKey,
                        WorkloadIntent<bool>.CreateSet(true))
                },
                scheduleIntents: new[]
                {
                    TestSupport.ScheduleIntent(parentScheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(newSchedule)),
                    TestSupport.ScheduleIntent(localScheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(newSchedule)),
                    TestSupport.ScheduleIntent(globalScheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(newSchedule))
                },
                specificPriorityIntents: new[]
                {
                    TestSupport.SpecificIntent(localSpecificKey, WorkloadIntentState.Set, 7),
                    TestSupport.SpecificIntent(globalSpecificKey, WorkloadIntentState.Set, 8)
                },
                workTypeOrderIntents: new[]
                {
                    TestSupport.OrderIntent(localOrderKey,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(newOrder)),
                    TestSupport.OrderIntent(globalOrderKey,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(newOrder))
                });

            WorkloadSemanticDiff diff = WorkloadSemanticDiff.Between(before, after);
            AssertDiffCanonicalForms(diff, before, after, "typed intents");

            WorkloadChange parent = FindChange(
                diff,
                WorkloadStateDimension.ParentPriorities,
                "intent:" + parentKey.CanonicalKey);
            AssertChangeValues(
                parent,
                "intent:" + parentKey.CanonicalKey,
                TestSupport.ParentPriorityIntent(pawn, workType, WorkloadIntentState.Set, 3).Intent.CanonicalForm,
                TestSupport.ParentPriorityIntent(pawn, workType, WorkloadIntentState.Set, 6).Intent.CanonicalForm,
                "typed parent priority");
            AssertTarget(parent, "p2", "PlantWork", null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "typed parent priority");

            WorkloadChange manual = FindChange(
                diff,
                WorkloadStateDimension.ManualModes,
                "intent:" + parentKey.CanonicalKey);
            AssertChangeValues(
                manual,
                "intent:" + parentKey.CanonicalKey,
                WorkloadIntent<bool>.CreateSet(false).CanonicalForm,
                WorkloadIntent<bool>.CreateSet(true).CanonicalForm,
                "typed manual mode");
            AssertTarget(manual, "p2", "PlantWork", null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "typed manual mode");

            WorkloadChange parentSchedule = FindChange(
                diff,
                WorkloadStateDimension.Schedules,
                "intent:" + parentScheduleKey.CanonicalKey);
            AssertChangeValues(
                parentSchedule,
                "intent:" + parentScheduleKey.CanonicalKey,
                WorkloadIntent<WorkloadSchedulePayload>.CreateSet(oldSchedule).CanonicalForm,
                WorkloadIntent<WorkloadSchedulePayload>.CreateSet(newSchedule).CanonicalForm,
                "typed parent schedule");
            AssertTarget(parentSchedule, "p2", "PlantWork", string.Empty,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "typed parent schedule");

            WorkloadChange localSchedule = FindChange(
                diff,
                WorkloadStateDimension.Schedules,
                "intent:" + localScheduleKey.CanonicalKey);
            AssertTarget(localSchedule, "p2", "PlantWork", "PlantCut",
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.WorkGiver,
                "typed local WorkGiver schedule");

            WorkloadChange globalSchedule = FindChange(
                diff,
                WorkloadStateDimension.Schedules,
                "intent:" + globalScheduleKey.CanonicalKey);
            AssertTarget(globalSchedule, null, "PlantWork", "PlantSow",
                WorkloadTargetScope.GlobalShared,
                WorkloadScheduleTargetKind.WorkGiver,
                "typed global WorkGiver schedule");

            WorkloadChange localSpecific = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOverrides,
                "intent:" + localSpecificKey.CanonicalKey);
            AssertChangeValues(
                localSpecific,
                "intent:" + localSpecificKey.CanonicalKey,
                TestSupport.SpecificIntent(localSpecificKey, WorkloadIntentState.Set, 3).Intent.CanonicalForm,
                TestSupport.SpecificIntent(localSpecificKey, WorkloadIntentState.Set, 7).Intent.CanonicalForm,
                "typed local specific priority");
            AssertTarget(localSpecific, "p2", "PlantWork", "PlantCut",
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "typed local specific priority");

            WorkloadChange globalSpecific = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOverrides,
                "intent:" + globalSpecificKey.CanonicalKey);
            AssertChangeValues(
                globalSpecific,
                "intent:" + globalSpecificKey.CanonicalKey,
                TestSupport.SpecificIntent(globalSpecificKey, WorkloadIntentState.Set, 4).Intent.CanonicalForm,
                TestSupport.SpecificIntent(globalSpecificKey, WorkloadIntentState.Set, 8).Intent.CanonicalForm,
                "typed global specific priority");
            AssertTarget(globalSpecific, null, "PlantWork", "PlantSow",
                WorkloadTargetScope.GlobalShared,
                WorkloadScheduleTargetKind.ParentWorkType,
                "typed global specific priority");

            WorkloadChange localOrder = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOrder,
                "intent:" + localOrderKey.CanonicalKey);
            AssertChangeValues(
                localOrder,
                "intent:" + localOrderKey.CanonicalKey,
                WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(oldOrder).CanonicalForm,
                WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(newOrder).CanonicalForm,
                "typed local work-type order");
            AssertTarget(localOrder, "p2", "PlantWork", null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "typed local work-type order");

            WorkloadChange globalOrder = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOrder,
                "intent:" + globalOrderKey.CanonicalKey);
            AssertChangeValues(
                globalOrder,
                "intent:" + globalOrderKey.CanonicalKey,
                WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(oldOrder).CanonicalForm,
                WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(newOrder).CanonicalForm,
                "typed global work-type order");
            AssertTarget(globalOrder, null, "PlantWork", null,
                WorkloadTargetScope.GlobalShared,
                WorkloadScheduleTargetKind.ParentWorkType,
                "typed global work-type order");
        }

        private static void MembershipChangesCarryPawnTargets()
        {
            PawnKey includedPawn = TestSupport.Pawn("p3");
            PawnKey excludedPawn = TestSupport.Pawn("p4");
            var before = new WorkloadProjectedState();
            var after = new WorkloadProjectedState(
                representedPawnIds: new[] { includedPawn },
                excludedPawnIds: new[] { excludedPawn });
            WorkloadSemanticDiff diff = WorkloadSemanticDiff.Between(before, after);

            string includedKey = "I:" + WorkloadCanonical.Encode(includedPawn.Value);
            WorkloadChange included = FindChange(diff, WorkloadStateDimension.Membership, includedKey);
            TestAssert.Equal(WorkloadChangeKind.Added, included.Kind,
                "adding a represented pawn must preserve its membership diff kind");
            TestAssert.Equal(null, included.BeforeValue,
                "adding a represented pawn must preserve the empty before value");
            TestAssert.Equal("1", included.AfterValue,
                "adding a represented pawn must preserve the legacy membership value");
            AssertTarget(included, "p3", null, null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "included membership");

            string excludedKey = "E:" + WorkloadCanonical.Encode(excludedPawn.Value);
            WorkloadChange excluded = FindChange(diff, WorkloadStateDimension.Membership, excludedKey);
            TestAssert.Equal(WorkloadChangeKind.Added, excluded.Kind,
                "adding an excluded pawn must preserve its membership diff kind");
            TestAssert.Equal("1", excluded.AfterValue,
                "adding an excluded pawn must preserve the legacy membership value");
            AssertTarget(excluded, "p4", null, null,
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "excluded membership");
        }

        private static void RemovedTypedChangesRetainBeforeTarget()
        {
            PawnKey pawn = TestSupport.Pawn("p5");
            WorkTypeKey workType = TestSupport.WorkType("PlantWork");
            WorkGiverKey workGiver = TestSupport.WorkGiver("PlantCut");
            var key = WorkloadSpecificJobTargetKey.ForPawn(pawn, workType, workGiver);
            var before = new WorkloadProjectedState(
                specificPriorityIntents: new[]
                {
                    TestSupport.SpecificIntent(key, WorkloadIntentState.Set, 7)
                });

            WorkloadSemanticDiff diff = WorkloadSemanticDiff.Between(
                before,
                new WorkloadProjectedState());
            WorkloadChange removed = FindChange(
                diff,
                WorkloadStateDimension.SpecificJobOverrides,
                "intent:" + key.CanonicalKey);

            TestAssert.Equal(WorkloadChangeKind.Removed, removed.Kind,
                "removing a typed specific-priority intent must preserve its diff kind");
            TestAssert.True(removed.AfterValue == null,
                "removing a typed specific-priority intent must preserve its empty after value");
            AssertTarget(removed, "p5", "PlantWork", "PlantCut",
                WorkloadTargetScope.PawnLocal,
                WorkloadScheduleTargetKind.ParentWorkType,
                "removed typed specific priority");
        }

        private static void AssertDiffCanonicalForms(
            WorkloadSemanticDiff diff,
            WorkloadProjectedState before,
            WorkloadProjectedState after,
            string context)
        {
            TestAssert.Equal(before.GetCanonicalForm(WorkloadOwnershipDimensions.All), diff.BeforeCanonical,
                context + " must retain the existing before canonical form");
            TestAssert.Equal(after.GetCanonicalForm(WorkloadOwnershipDimensions.All), diff.AfterCanonical,
                context + " must retain the existing after canonical form");
        }

        private static WorkloadChange FindChange(
            WorkloadSemanticDiff diff,
            WorkloadStateDimension dimension,
            string canonicalKey)
        {
            for (int i = 0; i < diff.Changes.Count; i++)
            {
                WorkloadChange change = diff.Changes[i];
                if (change.Dimension == dimension && change.CanonicalKey == canonicalKey)
                {
                    return change;
                }
            }

            throw new InvalidOperationException(
                "Missing workload diff change: " + dimension + "/" + canonicalKey);
        }

        private static void AssertChangeValues(
            WorkloadChange change,
            string canonicalKey,
            string beforeValue,
            string afterValue,
            string context)
        {
            TestAssert.Equal(WorkloadChangeKind.Changed, change.Kind,
                context + " must keep the semantic change kind");
            TestAssert.Equal(canonicalKey, change.CanonicalKey,
                context + " must keep its canonical key");
            TestAssert.Equal(beforeValue, change.BeforeValue,
                context + " must keep its before value");
            TestAssert.Equal(afterValue, change.AfterValue,
                context + " must keep its after value");
        }

        private static void AssertTarget(
            WorkloadChange change,
            string pawn,
            string workType,
            string workGiver,
            WorkloadTargetScope scope,
            WorkloadScheduleTargetKind scheduleKind,
            string context)
        {
            WorkloadChangeInspectionTarget target = change.InspectionTarget;
            TestAssert.NotNull(target, context + " must carry a typed inspection target");
            TestAssert.Equal(pawn, target.Pawn == null ? null : target.Pawn.Value,
                context + " must map the pawn identity");
            TestAssert.Equal(workType, target.WorkType == null ? null : target.WorkType.Value,
                context + " must map the work-type identity");
            TestAssert.Equal(workGiver, target.WorkGiver == null ? null : target.WorkGiver.Value,
                context + " must map the work-giver identity");
            TestAssert.Equal(scope, target.Scope,
                context + " must map local or global scope");
            TestAssert.Equal(scheduleKind, target.ScheduleKind,
                context + " must map the schedule target kind");
        }
    }
}
