using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Workloads.Projection;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class ProjectionAndSettingsTests
    {
        public static void Run()
        {
            ParentPriorityReadPolicyKeepsTruthAndIdentity();
            LiveFallbackCaptureKeepsResolvedValuesStable();
            CapturedParentPriorityAdvanceKeepsGenericReconciliation();
            var pawn = TestSupport.Pawn("p1");
            var workType = TestSupport.WorkType("PlantWork");
            var workGiver = TestSupport.WorkGiver("PlantCut");
            var scheduleKey = WorkloadScheduleTargetKey.ForParent(pawn, workType);
            var globalSchedule = TestSupport.ScheduleWithValue(2, 1 << 14);
            var global = TestSupport.LiveProvider(
                WorkloadScalarValue.FromBoolean(false),
                new WorkloadSpecificPriorityPayload(5),
                new WorkloadWorkTypeOrderPayload(new[] { workGiver }),
                scheduleKey,
                globalSchedule);

            var projection = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(WorkloadProjectedState.Empty),
                global,
                WorkloadOwnershipDimensions.All,
                "test.preview",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            var v2 = (IWorkTabEffectiveStateV2Editor)projection;

            var secondWorkType = TestSupport.WorkType("Hauling");
            WorkTabEffectiveStateMutationResult manualBatch = projection.SetManualModes(
                new[]
                {
                    new WorkloadParentPriorityKey(pawn, workType),
                    new WorkloadParentPriorityKey(pawn, secondWorkType)
                },
                true);
            TestAssert.True(manualBatch.Accepted,
                "a scoped manual-mode change must be accepted as one projected mutation");
            var projectedManual = WorkloadManualModeSemantics.GetEffectiveEntries(
                projection.ProjectedState);
            WorkloadIntent<bool> firstManual;
            WorkloadIntent<bool> secondManual;
            TestAssert.True(
                projectedManual.TryGetValue(
                    new WorkloadParentPriorityKey(pawn, workType), out firstManual) &&
                firstManual.HasValue && firstManual.Value,
                "the batched manual-mode change must update the first work type in the neutral projection.");
            TestAssert.True(
                projectedManual.TryGetValue(
                    new WorkloadParentPriorityKey(pawn, secondWorkType), out secondManual) &&
                secondManual.HasValue && secondManual.Value,
                "the batched manual-mode change must update every work type in scope through the central reducer.");

            var settingResult = v2.SetPresentationSetting(
                "ui.angled",
                WorkloadSettingValue.WorkloadOwned(WorkloadScalarValue.FromBoolean(true)));
            TestAssert.True(settingResult.Accepted, "owned presentation setting write must stay in the projection");
            var projectedSetting = projection.ResolveEffectivePresentationSetting("ui.angled");
            TestAssert.True(projectedSetting.IsSet && projectedSetting.Value.Scalar.BooleanValue,
                "the Work tab effective setting must immediately use the projected value");
            TestAssert.Equal(WorkloadSettingOwnership.WorkloadOwned, projectedSetting.Value.Ownership,
                "projected setting must retain workload ownership metadata");

            WorkTabEffectiveStateResolution<WorkloadSettingValue> globalSetting =
                global.ResolvePresentationSetting("ui.angled");
            TestAssert.True(globalSetting.IsSet,
                "the global provider must still expose its persisted setting");
            TestAssert.False(globalSetting.Value.Scalar.BooleanValue,
                "preview editing must not mutate the global setting callback state");

            var typedOnlyState = new WorkloadProjectedState(
                presentationSettingIntents: new[]
                {
                    TestSupport.SettingIntent(
                        "ui.angled",
                        WorkloadIntent<WorkloadSettingValue>.CreateSet(
                            WorkloadSettingValue.WorkloadOwned(
                                WorkloadScalarValue.FromBoolean(true))))
                });
            TestAssert.Equal(1, typedOnlyState.PresentationSettings.Count,
                "typed presentation intent must synchronize its legacy scalar compatibility entry");
            TestAssert.True(typedOnlyState.PresentationSettings[0].Value.BooleanValue,
                "typed presentation intent must be the canonical scalar value");
            TestAssert.Equal(1, typedOnlyState.PresentationSettingIntents.Count,
                "typed-only presentation ownership must remain explicit");

            var typedOnlyProjection = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(typedOnlyState),
                global,
                WorkloadOwnershipDimensions.All,
                "test.typed-only",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            WorkTabEffectiveStateResolution<WorkloadSettingValue> typedOnlyEffective =
                typedOnlyProjection.ResolveEffectivePresentationSetting("ui.angled");
            TestAssert.True(typedOnlyEffective.IsSet && typedOnlyEffective.Value.Scalar.BooleanValue,
                "typed-only presentation ownership must render through the effective provider");
            TestAssert.Equal(WorkloadSettingOwnership.WorkloadOwned, typedOnlyEffective.Value.Ownership,
                "typed-only effective presentation values must retain ownership metadata");

            WorkloadOwnershipDimensions withoutPresentation =
                WorkloadOwnershipDimensions.All &
                ~WorkloadOwnershipDimensions.PresentationSettings;
            var unownedProjection = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(WorkloadProjectedState.Empty),
                global,
                withoutPresentation,
                "test.unowned",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            var unownedEditor = (IWorkTabEffectiveStateV2Editor)unownedProjection;
            TestAssert.True(
                unownedEditor.SetPresentationSetting(
                    "ui.angled",
                    WorkloadSettingValue.WorkloadOwned(WorkloadScalarValue.FromBoolean(true))).IsBlocked,
                "ordinary projected writes must not acquire ownership implicitly");
            TestAssert.True(
                unownedProjection.AcquirePresentationSetting(
                    "ui.angled",
                    WorkloadScalarValue.FromBoolean(false)).Accepted,
                "the explicit acquisition path must stage a selected global setting");
            TestAssert.False(globalSetting.Value.Scalar.BooleanValue,
                "acquiring workload ownership must not mutate the global provider");
            TestAssert.True(
                unownedProjection.ResolveEffectivePresentationSetting("ui.angled").IsSet,
                "an acquired setting must immediately be effective in the preview");
            TestAssert.True(
                unownedProjection.ReleasePresentationSetting("ui.angled").Accepted,
                "the explicit removal path must release selected ownership");
            WorkTabEffectiveStateResolution<WorkloadSettingValue> released =
                unownedProjection.ResolveEffectivePresentationSetting("ui.angled");
            TestAssert.True(released.IsSet && !released.Value.Scalar.BooleanValue &&
                            released.Value.Ownership == WorkloadSettingOwnership.Global,
                "released ownership must restore the exact global effective value");

            var reversibleDraft = new WorkloadDraft(typedOnlyState);
            reversibleDraft.SetPresentationSetting(
                "ui.angled",
                WorkloadScalarValue.FromBoolean(false));
            TestAssert.True(reversibleDraft.HasSemanticChanges,
                "a typed presentation edit must produce a semantic diff");
            reversibleDraft.SetPresentationSetting(
                "ui.angled",
                WorkloadScalarValue.FromBoolean(true));
            TestAssert.False(reversibleDraft.HasSemanticChanges,
                "reverting a typed presentation edit must clear the semantic diff");

            var scheduleResult = v2.SetSchedule(scheduleKey, TestSupport.ScheduleWithValue(4, 1 << 14));
            TestAssert.True(scheduleResult.Accepted, "normal schedule editor writes must target the projection");
            var projectedSchedule = projection.ResolveSchedule(scheduleKey);
            TestAssert.True(projectedSchedule.IsSet && projectedSchedule.Value.PriorityAt(14) == 4,
                "the normal schedule projection must expose the edited 24-hour payload");

            WorkTabEffectiveStateRevision capturedRevision = projection.RevisionToken;
            var capturedView = (IWorkTabEffectiveStateV2Provider)
                ((IWorkTabEffectiveStateViewSource)projection)
                .CaptureEffectiveStateView(capturedRevision);

            TestAssert.True(
                v2.ClearSchedule(scheduleKey).Accepted,
                "schedule clear must be accepted by the projected editor");
            TestAssert.True(
                projection.ResolveEffectiveSchedule(scheduleKey).IsClear,
                "an explicit schedule clear must suppress the live schedule in the generic effective-state API");
            WorkTabEffectiveStateResolution<WorkloadSchedulePayload> capturedSchedule =
                capturedView.ResolveSchedule(scheduleKey);
            TestAssert.True(capturedSchedule.IsSet && capturedSchedule.Value.PriorityAt(14) == 4,
                "a captured WorkTab view must retain its pre-input schedule value after the live draft changes");
            TestAssert.Equal(capturedRevision, ((IWorkTabEffectiveStateProvider)capturedView).RevisionToken,
                "the captured view must retain the exact revision token used to build it");

            WorkTabEffectiveStateRevision clearRevision = projection.RevisionToken;
            var clearedView = (IWorkTabComposedEffectiveStateProvider)
                ((IWorkTabEffectiveStateViewSource)projection)
                .CaptureEffectiveStateView(clearRevision);
            TestAssert.True(
                v2.SetSchedule(scheduleKey, TestSupport.ScheduleWithValue(6, 1 << 14)).Accepted,
                "a later input mutation must remain writable on the live projected provider");
            TestAssert.True(clearedView.ResolveEffectiveSchedule(scheduleKey).IsClear,
                "a captured WorkTab view must retain an exact schedule tombstone after the live draft changes");
            TestAssert.True(
                ((IWorkTabEffectiveStateV2Editor)clearedView)
                .SetSchedule(scheduleKey, TestSupport.ScheduleWithValue(7, 1 << 14)).IsBlocked,
                "a captured WorkTab view must fail closed when rendering code attempts a write");

            var clearSetting = v2.ClearPresentationSettingV2("ui.angled");
            TestAssert.True(clearSetting.Accepted, "owned presentation setting clear must be accepted");
            var exactLayer = projection.ResolvePresentationSetting("ui.angled");
            TestAssert.True(exactLayer.IsClear, "clear must remain visible as a typed tombstone at the preview layer");

            var canceledProjection = new ProjectedWorkTabEffectiveStateProvider(
                new WorkloadDraft(WorkloadProjectedState.Empty),
                global,
                WorkloadOwnershipDimensions.All,
                "test.canceled",
                WorkloadScope.Explicit(new[] { "p1" }),
                new[] { pawn });
            var restored = canceledProjection.ResolveEffectivePresentationSetting("ui.angled");
            TestAssert.True(restored.IsSet && !restored.Value.Scalar.BooleanValue,
                "discarding the projection must restore the global presentation value");

            TestAssert.True(projection.Revision > 0, "projection mutations must advance a revision");
            TestAssert.True(projection.RevisionVector.SettingsRevision > 0,
                "presentation writes must advance the settings revision dimension");
            TestAssert.True(projection.RevisionVector.ScheduleRevision > 0,
                "schedule writes must advance the schedule revision dimension");
        }

        private static void LiveFallbackCaptureKeepsResolvedValuesStable()
        {
            WorkTypeKey workType = TestSupport.WorkType("CaptureWork");
            WorkGiverKey workGiver = TestSupport.WorkGiver("CaptureGiver");
            WorkloadSpecificJobTargetKey key =
                WorkloadSpecificJobTargetKey.Global(workType, workGiver);
            int livePriority = 3;
            var live = new LiveWorkTabEffectiveStateProvider(
                new LiveWorkTabEffectiveStateCallbacks("test.mutable-live")
                {
                    Revision = () => 21L,
                    RevisionVector = () => WorkTabEffectiveStateRevisionVector.FromRevision(21L),
                    SpecificJobPriorityV2 = target => target != null && target.Equals(key)
                        ? WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Set(
                            new WorkloadSpecificPriorityPayload(livePriority))
                        : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion
                });

            WorkTabEffectiveStateRevision revision = live.RevisionToken;
            var captured = (IWorkTabEffectiveStateV2Provider)
                ((IWorkTabEffectiveStateViewSource)live).CaptureEffectiveStateView(revision);
            TestAssert.Equal(3, captured.ResolveSpecificJobPriority(key).Value.Priority,
                "the captured live fallback must expose the value resolved for this pass");

            livePriority = 7;
            TestAssert.Equal(3, captured.ResolveSpecificJobPriority(key).Value.Priority,
                "a captured live fallback must not follow a later live callback change for the same target");
            TestAssert.Equal(revision, ((IWorkTabEffectiveStateProvider)captured).RevisionToken,
                "the captured live fallback must retain the view revision token");
        }

        private static void CapturedParentPriorityAdvanceKeepsGenericReconciliation()
        {
            PawnKey pawn = TestSupport.Pawn("p1");
            WorkTypeKey workType = TestSupport.WorkType("Crafting");
            var parent = new WorkloadParentPriorityKey(pawn, workType);
            var initial = new WorkloadProjectedState(
                parentPriorities: new[] { new WorkloadParentPriorityEntry(parent, 3) });
            var draft = new WorkloadDraft(initial);
            var projection = new ProjectedWorkTabEffectiveStateProvider(
                draft,
                ownedDimensions: WorkloadOwnershipDimensions.All,
                scope: WorkloadScope.Explicit(new[] { "p1" }),
                editablePawnIds: new[] { pawn });

            long revision = projection.ProjectionRevision;
            draft.SetParentPriority(parent, 4);
            WorkloadProjectedState capturedParentState = draft.ProjectedState;
            projection.InvalidateDraft();
            TestAssert.False(
                projection.TryPublishCapturedParentPriority(
                    capturedParentState,
                    projection.ProjectionRevision + 1L),
                "a captured parent publish must reject a mismatched provider revision");
            TestAssert.True(
                projection.TryPublishCapturedParentPriority(
                    capturedParentState,
                    projection.ProjectionRevision),
                "a captured parent publish must accept the exact invalidated draft revision");

            TestAssert.Equal(revision + 1L, projection.ProjectionRevision,
                "a captured parent edit must publish exactly one projected provider revision");
            TestAssert.True(ReferenceEquals(capturedParentState, projection.ProjectedState),
                "the next provider read must use the captured parent state without rebuilding the full draft");

            draft.SetPresentationSetting("ui.angled", WorkloadScalarValue.FromBoolean(true));
            projection.InvalidateDraft();
            WorkloadProjectedState reconciled = projection.ProjectedState;
            TestAssert.Equal(4, reconciled.ParentPriorities[0].Priority,
                "a later generic preview edit must retain the captured parent edit");
            TestAssert.True(reconciled.PresentationSettings[0].Value.BooleanValue,
                "a later generic preview edit must still trigger normal full reconciliation");
        }

        private static void ParentPriorityReadPolicyKeepsTruthAndIdentity()
        {
            ParentProjectionValue<int> noOpinion = default(ParentProjectionValue<int>);
            ParentProjectionValue<int> clear = ParentProjectionValue<int>.Clear;
            ParentProjectionValue<int> set = ParentProjectionValue<int>.Set(7);
            ParentProjectionValue<int> zero = ParentProjectionValue<int>.Set(0);

            TestAssert.Equal(4, ParentPriorityReadPolicy.Resolve(false, noOpinion, 4, false, 2, 9),
                "BWT NoOpinion with linked schedule must use stored base.");
            TestAssert.Equal(2, ParentPriorityReadPolicy.Resolve(false, noOpinion, 4, true, 2, 9),
                "BWT NoOpinion with a pinned hour must compose that hour once.");
            TestAssert.Equal(4, ParentPriorityReadPolicy.Resolve(false, clear, 4, false, 2, 9),
                "BWT Clear with a linked schedule must reveal stored base.");
            TestAssert.Equal(2, ParentPriorityReadPolicy.Resolve(false, clear, 4, true, 2, 9),
                "BWT Clear with a pinned hour must reveal then compose the live schedule.");
            TestAssert.Equal(7, ParentPriorityReadPolicy.Resolve(false, set, 4, false, 2, 9),
                "BWT Set with a linked schedule must use its projected base.");
            TestAssert.Equal(2, ParentPriorityReadPolicy.Resolve(false, set, 4, true, 2, 9),
                "BWT Set must compose a pinned hour exactly once.");
            TestAssert.Equal(0, ParentPriorityReadPolicy.Resolve(false, zero, 4, true, 2, 9),
                "A zero projected base must remain disabled even when an hour is pinned.");

            TestAssert.Equal(9, ParentPriorityReadPolicy.Resolve(true, noOpinion, 4, true, 2, 9),
                "External NoOpinion must use external effective priority directly.");
            TestAssert.Equal(9, ParentPriorityReadPolicy.Resolve(true, clear, 4, true, 2, 9),
                "External Clear must use external effective priority directly.");
            TestAssert.Equal(7, ParentPriorityReadPolicy.Resolve(true, set, 4, true, 2, 9),
                "External Set must replace external effective priority directly.");
            TestAssert.Equal(0, ParentPriorityReadPolicy.Resolve(true, zero, 4, true, 2, 9),
                "External zero Set must remain disabled.");
            TestAssert.Equal(ParentPriorityOverlayState.Clear, clear.State,
                "Clear must remain distinct from NoOpinion even when their effective value matches.");

            TestAssert.Equal(PriorityConstants.VanillaDefaultEnabled,
                ParentPriorityReadPolicy.NormalizeForDisplay(true, 1, false),
                "Checkbox-mode display must normalize raw enabled priority 1.");
            TestAssert.Equal(PriorityConstants.VanillaDefaultEnabled,
                ParentPriorityReadPolicy.NormalizeForDisplay(true, 2, false),
                "Checkbox-mode display must normalize raw enabled priority 2.");
            TestAssert.Equal(2, ParentPriorityReadPolicy.NormalizeForDisplay(true, 2, true),
                "Manual-priority display must preserve its explicit raw priority.");
            TestAssert.Equal(2, ParentPriorityReadPolicy.NormalizeForDisplay(true, 2, null),
                "A missing PlaySettings snapshot must not consult or assume checkbox mode.");

            var first = new ParentPriorityTarget(17, "PlantWork");
            var same = new ParentPriorityTarget(17, "PlantWork");
            var otherPawn = new ParentPriorityTarget(18, "PlantWork");
            var values = new Dictionary<ParentPriorityTarget, int> { { first, 6 } };
            TestAssert.True(first.Equals(same) && !first.Equals(otherPawn) && values.ContainsKey(same),
                "Parent target identity must be stable pawn thing ID plus WorkType defName only.");

            TestAssert.Equal(0, (int)WorkTabEffectiveStateDimension.ParentPriority,
                "ParentPriority enum ABI value must remain reserved at zero.");
            TestAssert.Equal(1, (int)WorkTabEffectiveStateDimension.ManualMode,
                "ManualMode enum ABI value must not be renumbered.");
            TestAssert.Equal(2, (int)WorkTabEffectiveStateDimension.Schedule,
                "Schedule enum ABI value must not be renumbered.");
        }
    }
}
