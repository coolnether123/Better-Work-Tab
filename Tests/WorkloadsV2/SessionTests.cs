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
            var workGiver = TestSupport.WorkGiver("PlantCut");
            var scheduleKey = WorkloadScheduleTargetKey.ForParent(
                parent.Pawn,
                parent.WorkType);
            var sourceSchedule = TestSupport.ScheduleWithValue(2, 1 << 4);
            var liveSchedule = TestSupport.ScheduleWithValue(1, 1 << 5);
            var specificKey = WorkloadSpecificJobTargetKey.ForPawn(
                parent.Pawn,
                parent.WorkType,
                workGiver);
            var orderKey = WorkloadWorkTypeOrderKey.ForPawn(
                parent.Pawn,
                parent.WorkType);
            var order = new WorkloadWorkTypeOrderPayload(new[] { workGiver });
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
                scheduleIntents: new[]
                {
                    TestSupport.ScheduleIntent(
                        scheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(sourceSchedule))
                },
                specificPriorityIntents: new[]
                {
                    TestSupport.SpecificIntent(
                        specificKey,
                        WorkloadIntentState.Set,
                        6)
                },
                workTypeOrderIntents: new[]
                {
                    TestSupport.OrderIntent(
                        orderKey,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(order))
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
                scheduleIntents: new[]
                {
                    TestSupport.ScheduleIntent(
                        scheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(liveSchedule))
                },
                specificPriorityIntents: new[]
                {
                    TestSupport.SpecificIntent(
                        specificKey,
                        WorkloadIntentState.Set,
                        5)
                },
                workTypeOrderIntents: new[]
                {
                    TestSupport.OrderIntent(
                        orderKey,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(order))
                },
                representedPawnIds: new[] { parent.Pawn });
            var session = WorkloadSession.OpenCaptured(
                template,
                liveState,
                WorkloadSession.GetSourceIdentity(template));
            TestAssert.True(session != null, "the deterministic session must carry an explicit source identity");
            TestAssert.True(
                object.ReferenceEquals(
                    session.ProjectedState.CanonicalForm,
                    session.ProjectedState.CanonicalForm),
                "an immutable projected state must retain its canonical form");
            TestAssert.True(
                !string.IsNullOrWhiteSpace(session.PreviewSessionId),
                "every preview must carry a unique stable session identity");
            TestAssert.True(
                session.SessionRevision > 0 && session.MembershipRevision > 0,
                "every preview must begin with independent positive freshness revisions");
            var secondSession = WorkloadSession.Open(template, null, liveState);
            TestAssert.False(
                string.Equals(
                    session.PreviewSessionId,
                    secondSession.PreviewSessionId,
                    System.StringComparison.Ordinal),
                "separate previews must never reuse a session identity");

            TestAssert.Equal(0, session.UnsupportedClearDimensions.Count,
                "an unchanged typed workload must not be reported as an unsupported clear");
            TestAssert.True(
                object.ReferenceEquals(
                    session.UnsupportedClearDimensions,
                    session.UnsupportedClearDimensions),
                "unsupported-clear analysis must be retained by the immutable session");
            WorkloadSession unsupportedRemoval = session.Edit(draft =>
                draft.SetParentPriorityIntent(
                    parent,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.NoOpinion));
            TestAssert.True(
                unsupportedRemoval.HasUnsupportedClears &&
                unsupportedRemoval.UnsupportedClearDimensions.Count == 1 &&
                unsupportedRemoval.UnsupportedClearDimensions[0] ==
                    WorkloadStateDimension.ParentPriorities,
                "removing an owned value without a typed Clear must remain fail-closed");
            WorkloadSession supportedClear = session.Edit(draft =>
                draft.RemoveParentPriority(parent));
            TestAssert.False(supportedClear.HasUnsupportedClears,
                "an explicit typed Clear must remain a supported workload operation");

            var priorityEdited = session.Edit(draft =>
                draft.SetParentPriorityIntent(
                    parent,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                        new WorkloadSpecificPriorityPayload(4))));
            TestAssert.True(
                priorityEdited.SessionRevision > session.SessionRevision,
                "projected workload edits must advance the session freshness revision");
            TestAssert.Equal(
                session.MembershipRevision,
                priorityEdited.MembershipRevision,
                "non-membership edits must not advance membership freshness");

            TestAssert.True(
                session.TryEditCapturedParentPriority(parent, 4, out WorkloadSession capturedPriorityEdit),
                "an existing captured parent priority must use the narrow preview edit path");
            TestAssert.Equal(4, capturedPriorityEdit.ProjectedState.ParentPriorities[0].Priority,
                "the narrow parent edit must update the exact parent value");
            TestAssert.Equal(
                session.MembershipRevision,
                capturedPriorityEdit.MembershipRevision,
                "a narrow parent edit must not alter preview membership");
            TestAssert.True(
                object.ReferenceEquals(
                    session.ProjectedState.ScheduleIntents,
                    capturedPriorityEdit.ProjectedState.ScheduleIntents) &&
                object.ReferenceEquals(
                    session.ProjectedState.SpecificPriorityIntents,
                    capturedPriorityEdit.ProjectedState.SpecificPriorityIntents),
                "a narrow parent edit must retain unrelated immutable workload dimensions");
            TestAssert.True(
                capturedPriorityEdit.Apply().AppliedState.SemanticallyEquals(
                    capturedPriorityEdit.ProjectedState),
                "Apply must use the state produced by a narrow parent edit");
            bool capturedMissingTarget = session.TryEditCapturedParentPriority(
                new WorkloadParentPriorityKey(parent.Pawn, TestSupport.WorkType("Missing")),
                4,
                out WorkloadSession unsupportedCapturedEdit);
            TestAssert.False(
                capturedMissingTarget,
                "an uncaptured parent target must fall back instead of manufacturing scope state");
            TestAssert.True(
                object.ReferenceEquals(session, unsupportedCapturedEdit),
                "an unsupported narrow edit must leave the current preview session intact");

            var valueOnlyState = new WorkloadProjectedState(
                parentPriorities: new[] { new WorkloadParentPriorityEntry(parent, 3) },
                parentPriorityIntents: new WorkloadParentPriorityIntentEntry[0]);
            WorkloadSession valueOnlySession = WorkloadSession.Open(
                TestSupport.Template(valueOnlyState, stableId: "value-only", label: "Value only"));
            TestAssert.True(
                valueOnlySession.TryEditCapturedParentPriority(
                    parent,
                    4,
                    out WorkloadSession valueOnlyEdit),
                "a captured value-only parent target must add its matching typed intent without a full draft rebuild");
            TestAssert.Equal(
                1,
                valueOnlyEdit.ProjectedState.ParentPriorityIntents.Count,
                "a value-only parent edit must retain an explicit typed Set intent for Apply and Update");
            TestAssert.Equal(
                4,
                valueOnlyEdit.ProjectedState.ParentPriorityIntents[0].Intent.Value.Priority,
                "the added typed intent must match the edited parent priority");

            var excluded = session.ExcludePawn(parent.Pawn);
            TestAssert.Equal(
                session.SessionRevision,
                excluded.SessionRevision,
                "membership-only edits must not consume the projected-state revision");
            TestAssert.True(
                excluded.MembershipRevision > session.MembershipRevision,
                "membership edits must advance membership freshness independently");
            var included = excluded.IncludePawn(parent.Pawn);
            TestAssert.True(
                included.MembershipRevision > excluded.MembershipRevision,
                "reversing membership must advance membership freshness again");
            TestAssert.Equal(
                session.SessionRevision,
                included.SessionRevision,
                "membership revisions must remain independent of projected-state revisions");

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
            TestAssert.True(
                edited.HasTemplateChanges,
                "the lightweight session dirty read must agree with the full template diff");
            TestAssert.True(
                object.ReferenceEquals(edited.TemplateDiff, edited.TemplateDiff),
                "an immutable session must retain its computed template diff");
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
                (edited.Validation.Issues.Count == 0 ? "no diagnostic" : edited.Validation.Issues[0].Code.ToString()));

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
            TestAssert.False(updated.Session.IsTerminal,
                "Save planning must not terminalize the active preview session");
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
            TestAssert.False(forked.Session.IsTerminal,
                "Save As planning must not terminalize the active preview session");
            TestAssert.Equal("day-shift", forked.ResultTemplate.StableId,
                "Fork must create the requested stable workload ID");
            TestAssert.True(
                forked.ResultTemplate.ProjectedState.PresentationSettingIntents.Count == 1 &&
                forked.ResultTemplate.ProjectedState.PresentationSettings.Count == 1,
                "Fork must carry selected presentation ownership data into the new workload");
            TestAssert.True(edited.SourceTemplate.ProjectedState.SemanticallyEquals(sourceState),
                "Fork must preserve the source workload");

            WorkloadSemanticDiff liveDiffBeforeSave = edited.LiveDiff;
            WorkloadSession saved = edited.RebaseAfterPersistence(
                new WorkloadPersistenceReceipt(
                    WorkloadDecisionKind.Update,
                    edited.SourceTemplate.StableId,
                    updated.ResultTemplate.StableId,
                    edited.SourceIdentity,
                    WorkloadSession.GetSourceIdentity(updated.ResultTemplate),
                    updated.ResultTemplate,
                    7,
                    "before-save",
                    8,
                    "after-save",
                    edited.SourceTemplate.StableId));
            TestAssert.True(saved != null, "a matching Save receipt must rebase the open preview");
            TestAssert.False(saved.IsTerminal, "a saved preview must remain nonterminal");
            TestAssert.True(saved.TemplateDiff.IsEmpty,
                "Save must reset the template diff against the committed round-tripped state");
            TestAssert.True(
                saved.TemplateBaselineState.SemanticallyEquals(updated.ResultTemplate.ProjectedState),
                "Save must install the committed target as the new template baseline");
            TestAssert.True(
                saved.TemplateBaselineState.ScheduleIntents.Count == 1 &&
                saved.TemplateBaselineState.ScheduleIntents[0].Key.Equals(scheduleKey) &&
                saved.TemplateBaselineState.ScheduleIntents[0].Intent.HasValue &&
                saved.TemplateBaselineState.ScheduleIntents[0].Intent.Value.Equals(sourceSchedule),
                "Save must preserve typed schedule ownership and payloads");
            TestAssert.True(
                saved.TemplateBaselineState.SpecificPriorityIntents.Count == 1 &&
                saved.TemplateBaselineState.SpecificPriorityIntents[0].Key.Equals(specificKey) &&
                saved.TemplateBaselineState.SpecificPriorityIntents[0].Intent.HasValue &&
                saved.TemplateBaselineState.SpecificPriorityIntents[0].Intent.Value.Priority == 6,
                "Save must preserve typed specific-job priorities and target scope");
            TestAssert.True(
                saved.TemplateBaselineState.WorkTypeOrderIntents.Count == 1 &&
                saved.TemplateBaselineState.WorkTypeOrderIntents[0].Key.Equals(orderKey) &&
                saved.TemplateBaselineState.WorkTypeOrderIntents[0].Intent.HasValue &&
                saved.TemplateBaselineState.WorkTypeOrderIntents[0].Intent.Value.Equals(order),
                "Save must preserve typed specific-job order and taxonomy");
            TestAssert.True(
                saved.LiveBaselineState.SemanticallyEquals(edited.LiveBaselineState),
                "Save must preserve the opening live baseline exactly");
            TestAssert.True(
                saved.LiveDiff.BeforeFingerprint == liveDiffBeforeSave.BeforeFingerprint &&
                saved.LiveDiff.AfterFingerprint == liveDiffBeforeSave.AfterFingerprint,
                "Save must preserve the live-impact diff after the template rebase");
            TestAssert.Equal(
                edited.PreviewSessionId,
                saved.PreviewSessionId,
                "Save must preserve the active preview identity");
            TestAssert.True(
                saved.Apply().AppliedState.SemanticallyEquals(edited.ProjectedState),
                "Apply after Save must use the current projected state and remain the only live path");

            WorkloadSession savedFork = edited.RebaseAfterPersistence(
                new WorkloadPersistenceReceipt(
                    WorkloadDecisionKind.Fork,
                    edited.SourceTemplate.StableId,
                    forked.ResultTemplate.StableId,
                    edited.SourceIdentity,
                    WorkloadSession.GetSourceIdentity(forked.ResultTemplate),
                    forked.ResultTemplate,
                    7,
                    "before-fork",
                    8,
                    "after-fork",
                    forked.ResultTemplate.StableId));
            TestAssert.True(savedFork != null, "a matching Save As receipt must rebase the open preview");
            TestAssert.Equal(
                forked.ResultTemplate.StableId,
                savedFork.SourceTemplate.StableId,
                "Save As must make only the new target identity active");
            TestAssert.Equal(
                "night-shift",
                edited.SourceTemplate.StableId,
                "Save As planning must leave the source workload identity unchanged");
            TestAssert.False(savedFork.TemplateDiff.Changes.Count > 0,
                "Save As must reset the template diff");

            WorkloadSession excludedBeforeSave = edited.ExcludePawn(parent.Pawn);
            WorkloadTemplate excludedTarget = excludedBeforeSave.TargetTemplate;
            WorkloadSession excludedAfterSave = excludedBeforeSave.RebaseAfterPersistence(
                new WorkloadPersistenceReceipt(
                    WorkloadDecisionKind.Update,
                    excludedBeforeSave.SourceTemplate.StableId,
                    excludedTarget.StableId,
                    excludedBeforeSave.SourceIdentity,
                    WorkloadSession.GetSourceIdentity(excludedTarget),
                    excludedTarget,
                    8,
                    "before-excluded-save",
                    9,
                    "after-excluded-save",
                    excludedTarget.StableId));
            TestAssert.NotNull(excludedAfterSave,
                "Save must rebase a preview that has a session-only exclusion");
            TestAssert.Equal(1, excludedAfterSave.SessionExcludedPawnIds.Count,
                "Save must preserve session-only exclusions");
            TestAssert.True(
                excludedAfterSave.SessionExcludedPawnIds[0].Equals(parent.Pawn),
                "Save must preserve the excluded pawn identity");
            TestAssert.True(excludedAfterSave.TemplateDiff.IsEmpty,
                "session-only exclusions must not create a persisted template diff after Save");

            WorkloadSession mismatch = edited.RebaseAfterPersistence(
                new WorkloadPersistenceReceipt(
                    WorkloadDecisionKind.Update,
                    edited.SourceTemplate.StableId,
                    updated.ResultTemplate.StableId,
                    "wrong-source-identity",
                    WorkloadSession.GetSourceIdentity(updated.ResultTemplate),
                    updated.ResultTemplate,
                    7,
                    "before-save",
                    8,
                    "after-save",
                    edited.SourceTemplate.StableId));
            TestAssert.True(mismatch == null,
                "a receipt with a mismatched source identity must be rejected");

            TestAssert.True(saved.Edit(draft =>
                draft.SetParentPriorityIntent(
                    parent,
                    WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                        new WorkloadSpecificPriorityPayload(5)))).IsDirty,
                "edits after Save must re-enable Save");

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
