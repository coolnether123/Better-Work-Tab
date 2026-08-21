using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class ProjectionAndSettingsTests
    {
        public static void Run()
        {
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

            var settingResult = v2.SetPresentationSetting(
                "ui.angled",
                WorkloadSettingValue.WorkloadOwned(WorkloadScalarValue.FromBoolean(true)));
            TestAssert.True(settingResult.Accepted, "owned presentation setting write must stay in the projection");
            var projectedSetting = projection.ResolveEffectivePresentationSetting("ui.angled");
            TestAssert.True(projectedSetting.IsSet && projectedSetting.Value.Scalar.BooleanValue,
                "the Work tab effective setting must immediately use the projected value");
            TestAssert.Equal(WorkloadSettingOwnership.WorkloadOwned, projectedSetting.Value.Ownership,
                "projected setting must retain workload ownership metadata");

            WorkloadScalarValue globalSetting;
            TestAssert.True(global.TryGetPresentationSetting("ui.angled", out globalSetting),
                "the global provider must still expose its persisted setting");
            TestAssert.False(globalSetting.BooleanValue,
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
            TestAssert.False(globalSetting.BooleanValue,
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

            TestAssert.True(
                v2.ClearSchedule(scheduleKey).Accepted,
                "schedule clear must be accepted by the projected editor");
            TestAssert.True(
                projection.ResolveEffectiveSchedule(scheduleKey).IsClear,
                "an explicit schedule clear must suppress the live schedule in the generic effective-state API");

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
    }
}
