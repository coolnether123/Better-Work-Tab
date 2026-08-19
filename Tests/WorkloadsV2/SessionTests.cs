using Better_Work_Tab.Features.Workloads.V2;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class SessionTests
    {
        public static void Run()
        {
            var parent = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p1"),
                TestSupport.WorkType("PlantWork"));
            var sourceState = new WorkloadProjectedState(
                parentPriorities: new[]
                {
                    new WorkloadParentPriorityEntry(parent, 3)
                },
                parentPriorityIntents: new[]
                {
                    TestSupport.ParentPriorityIntent(
                        parent.Pawn,
                        parent.WorkType,
                        WorkloadIntentState.Set,
                        3)
                },
                representedPawnIds: new[] { parent.Pawn });
            var template = TestSupport.Template(sourceState);
            var liveState = new WorkloadProjectedState(
                parentPriorities: new[]
                {
                    new WorkloadParentPriorityEntry(parent, 2)
                },
                parentPriorityIntents: new[]
                {
                    TestSupport.ParentPriorityIntent(
                        parent.Pawn,
                        parent.WorkType,
                        WorkloadIntentState.Set,
                        2)
                },
                representedPawnIds: new[] { parent.Pawn });
            var session = WorkloadSession.Open(template, null, liveState);
            var edited = session.Edit(draft =>
            {
                draft.SetParentPriorityIntent(
                    parent,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                        new WorkloadSpecificPriorityPayload(4)));
                draft.SetPresentationSetting(
                    "ui.angled",
                    WorkloadScalarValue.FromBoolean(true));
            });

            TestAssert.True(edited.IsDirty, "a preview edit must mark the template diff dirty");
            TestAssert.True(edited.TemplateDiff.Changes.Count >= 2,
                "priority and presentation edits must both be represented in the semantic diff");
            var preview = edited.Preview();
            TestAssert.True(preview.IsSideEffectFree && !preview.HasLiveSideEffects,
                "preview planning must not claim live side effects");
            TestAssert.True(preview.Diff.Changes.Count > 0,
                "Apply planning must compare against the captured live baseline");
            TestAssert.True(
                edited.Validation.CanApply,
                "edited session must validate before Apply: " +
                (edited.Validation.Issues.Count == 0 ? "no diagnostic" : edited.Validation.Issues[0].Message));

            var canceled = edited.Revert();
            TestAssert.True(canceled.TemplateDiff.IsEmpty,
                "revert/cancel must restore the exact saved template baseline");
            TestAssert.True(session.SourceTemplate.ProjectedState.SemanticallyEquals(sourceState),
                "editing a session must not mutate the source template");

            var applied = edited.Apply();
            TestAssert.True(applied.Accepted, "pure Apply plan must be accepted after validation");
            TestAssert.Equal("night-shift", applied.ResultTemplate.StableId,
                "Apply must not rewrite the stored workload identity");
            TestAssert.True(applied.AppliedState != null,
                "Apply must return the projected state for the runtime commit boundary");
            TestAssert.True(edited.SourceTemplate.ProjectedState.SemanticallyEquals(sourceState),
                "pure Apply planning must leave the source template unchanged");

            var updated = edited.Update();
            TestAssert.True(updated.Accepted, "Update plan must be accepted for a semantic diff");
            TestAssert.Equal("night-shift", updated.ResultTemplate.StableId,
                "Update must retain the source stable workload ID");
            TestAssert.True(updated.ResultTemplate.ProjectedState.SemanticallyEquals(edited.ProjectedState),
                "Update must persist the projected state only");
            TestAssert.True(
                updated.ResultTemplate.ProjectedState.PresentationSettingIntents.Count == 1 &&
                updated.ResultTemplate.ProjectedState.PresentationSettings.Count == 1 &&
                updated.ResultTemplate.ProjectedState.PresentationSettingIntents[0].Intent.HasValue &&
                updated.ResultTemplate.ProjectedState.PresentationSettingIntents[0].Intent.Value.Scalar.BooleanValue,
                "Update must persist typed presentation ownership and its synchronized scalar value");

            var forked = edited.Fork("day-shift", "Day Shift");
            TestAssert.True(forked.Accepted, "Fork plan must be accepted with a new stable ID");
            TestAssert.Equal("day-shift", forked.ResultTemplate.StableId,
                "Fork must create the requested stable workload ID");
            TestAssert.True(
                forked.ResultTemplate.ProjectedState.PresentationSettingIntents.Count == 1 &&
                forked.ResultTemplate.ProjectedState.PresentationSettings.Count == 1,
                "Fork must carry selected presentation ownership data into the new workload");
            TestAssert.True(edited.SourceTemplate.ProjectedState.SemanticallyEquals(sourceState),
                "Fork must preserve the source workload");

            var revertedToStored = session.Edit(draft =>
                draft.SetParentPriorityIntent(
                    parent,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                        new WorkloadSpecificPriorityPayload(3))));
            TestAssert.False(revertedToStored.IsDirty,
                "reverting 3 -> 4 -> 3 must make Update disappear semantically: " +
                (revertedToStored.TemplateDiff.Changes.Count == 0
                    ? "unknown diff"
                    : revertedToStored.TemplateDiff.Changes[0].CanonicalKey +
                      " before=" + revertedToStored.TemplateDiff.Changes[0].BeforeValue +
                      " after=" + revertedToStored.TemplateDiff.Changes[0].AfterValue));
        }
    }
}
