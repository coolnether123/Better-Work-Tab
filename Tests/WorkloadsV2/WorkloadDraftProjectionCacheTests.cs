using System;
using System.IO;
using Better_Work_Tab.Features.Workloads.V2;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class WorkloadDraftProjectionCacheTests
    {
        public static void Run()
        {
            CachedProjectionIsStableUntilMutation();
            EveryDraftMutationFamilyInvalidatesTheCache();
            ProductionMutationAndInputProfilingContractsRemainExplicit();
        }

        private static void CachedProjectionIsStableUntilMutation()
        {
            var draft = new WorkloadDraft(WorkloadProjectedState.Empty);
            WorkloadProjectedState first = draft.ProjectedState;
            WorkloadProjectedState second = draft.ProjectedState;

            TestAssert.True(ReferenceEquals(first, second),
                "unchanged draft reads must reuse the immutable projected-state instance");

            var parent = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p1"), TestSupport.WorkType("Crafting"));
            draft.SetParentPriority(parent, 4);
            WorkloadProjectedState changed = draft.ProjectedState;

            TestAssert.False(ReferenceEquals(first, changed),
                "a draft mutation must replace the cached projected-state instance");
            TestAssert.True(ReferenceEquals(changed, draft.ProjectedState),
                "the replacement projected state must remain stable until another mutation");
            TestAssert.Equal(1, changed.ParentPriorities.Count,
                "the replacement state must retain the changed parent priority");
            TestAssert.Equal(4, changed.ParentPriorities[0].Priority,
                "the replacement state must expose the assigned parent priority");
        }

        private static void EveryDraftMutationFamilyInvalidatesTheCache()
        {
            PawnKey pawn = TestSupport.Pawn("p1");
            WorkTypeKey workType = TestSupport.WorkType("Crafting");
            WorkGiverKey workGiver = TestSupport.WorkGiver("SmithWeapons");
            var parent = new WorkloadParentPriorityKey(pawn, workType);
            WorkloadSpecificJobKey legacySpecific = new WorkloadSpecificJobKey(pawn, workType, workGiver);
            WorkloadSpecificJobTargetKey specific = WorkloadSpecificJobTargetKey.ForPawn(pawn, workType, workGiver);
            WorkloadScheduleTargetKey schedule = WorkloadScheduleTargetKey.ForParent(pawn, workType);
            WorkloadWorkTypeOrderKey order = WorkloadWorkTypeOrderKey.ForPawn(pawn, workType);
            WorkloadSchedulePayload schedulePayload = TestSupport.ScheduleWithValue(3, 1 << 4);
            var orderPayload = new WorkloadWorkTypeOrderPayload(new[] { workGiver });

            AssertMutation("parent priority set", draft => draft.SetParentPriority(parent, 3),
                state => state.ParentPriorities.Count == 1 && state.ParentPriorities[0].Priority == 3);
            AssertMutation("parent priority remove", draft => draft.RemoveParentPriority(parent),
                state => state.ParentPriorityIntents.Count == 1 && state.ParentPriorityIntents[0].Intent.IsClear);
            AssertMutation("parent priority no-opinion", draft => draft.SetParentPriorityNoOpinion(parent),
                state => state.ParentPriorityIntents.Count == 0);

            AssertMutation("manual mode set", draft => draft.SetManualMode(parent, true),
                state => state.ManualModes.Count == 1 && state.ManualModes[0].Manual);
            AssertMutation("manual mode remove", draft => draft.RemoveManualMode(parent),
                state => state.ManualModeIntents.Count == 1 && state.ManualModeIntents[0].Intent.IsClear);
            AssertMutation("manual mode no-opinion", draft => draft.SetManualModeNoOpinion(parent),
                state => state.ManualModeIntents.Count == 0);

            AssertMutation("legacy schedule set", draft => draft.SetSchedule(pawn, new ScheduleKey(2)),
                state => state.Schedules.Count == 1);
            AssertMutation("legacy schedule remove", draft => draft.RemoveSchedule(pawn),
                state => state.Schedules.Count == 0);
            AssertMutation("typed schedule set", draft => draft.SetSchedule(schedule, schedulePayload),
                state => state.ScheduleIntents.Count == 1 && state.ScheduleIntents[0].Intent.HasValue);
            AssertMutation("typed schedule clear", draft => draft.ClearSchedule(schedule),
                state => state.ScheduleIntents.Count == 1 && state.ScheduleIntents[0].Intent.IsClear);
            AssertMutation("typed schedule no-opinion", draft => draft.SetScheduleNoOpinion(schedule),
                state => state.ScheduleIntents.Count == 0);

            AssertMutation("legacy specific override set", draft => draft.SetSpecificJobOverride(
                    legacySpecific, WorkloadScalarValue.FromInteger(4)),
                state => state.SpecificJobOverrides.Count == 1);
            AssertMutation("legacy specific override remove", draft => draft.RemoveSpecificJobOverride(legacySpecific),
                state => state.SpecificPriorityIntents.Count == 1 && state.SpecificPriorityIntents[0].Intent.IsClear);
            AssertMutation("typed specific priority set", draft => draft.SetSpecificPriority(specific, 5),
                state => state.SpecificPriorityIntents.Count == 1 && state.SpecificPriorityIntents[0].Intent.HasValue);
            AssertMutation("typed specific priority clear", draft => draft.ClearSpecificPriority(specific),
                state => state.SpecificPriorityIntents.Count == 1 && state.SpecificPriorityIntents[0].Intent.IsClear);
            AssertMutation("typed specific priority no-opinion", draft => draft.SetSpecificPriorityNoOpinion(specific),
                state => state.SpecificPriorityIntents.Count == 0);

            AssertMutation("legacy specific order set", draft => draft.SetSpecificJobOrder(legacySpecific, 2),
                state => state.SpecificJobOrder.Count == 1);
            AssertMutation("legacy specific order remove", draft => draft.RemoveSpecificJobOrder(legacySpecific),
                state => state.SpecificJobOrder.Count == 0);
            AssertMutation("typed work-type order set", draft => draft.SetWorkTypeOrder(order, orderPayload),
                state => state.WorkTypeOrderIntents.Count == 1 && state.WorkTypeOrderIntents[0].Intent.HasValue);
            AssertMutation("typed work-type order clear", draft => draft.ClearWorkTypeOrder(order),
                state => state.WorkTypeOrderIntents.Count == 1 && state.WorkTypeOrderIntents[0].Intent.IsClear);
            AssertMutation("typed work-type order no-opinion", draft => draft.SetWorkTypeOrderNoOpinion(order),
                state => state.WorkTypeOrderIntents.Count == 0);

            AssertMutation("presentation setting set", draft => draft.SetPresentationSetting(
                    "ui.angled", WorkloadScalarValue.FromBoolean(true)),
                state => state.PresentationSettings.Count == 1 && state.PresentationSettingIntents.Count == 1);
            AssertMutation("presentation setting remove", draft => draft.RemovePresentationSetting("ui.angled"),
                state => state.PresentationSettings.Count == 0 && state.PresentationSettingIntents[0].Intent.IsClear);
            AssertMutation("presentation setting clear", draft => draft.ClearPresentationSetting("ui.angled"),
                state => state.PresentationSettingIntents.Count == 1 && state.PresentationSettingIntents[0].Intent.IsClear);
            AssertMutation("presentation setting release", draft => draft.ReleasePresentationSetting("ui.angled"),
                state => state.PresentationSettings.Count == 0 && state.PresentationSettingIntents.Count == 0);
            AssertMutation("presentation setting no-opinion", draft => draft.SetPresentationSettingNoOpinion("ui.angled"),
                state => state.PresentationSettings.Count == 0 && state.PresentationSettingIntents.Count == 0);
        }

        private static void AssertMutation(
            string name,
            Action<WorkloadDraft> mutate,
            Func<WorkloadProjectedState, bool> semantics)
        {
            var draft = new WorkloadDraft(WorkloadProjectedState.Empty);
            WorkloadProjectedState before = draft.ProjectedState;
            TestAssert.True(ReferenceEquals(before, draft.ProjectedState),
                name + " must begin with a stable cached projection");

            mutate(draft);
            WorkloadProjectedState after = draft.ProjectedState;
            TestAssert.False(ReferenceEquals(before, after),
                name + " must invalidate the cached projection");
            TestAssert.True(ReferenceEquals(after, draft.ProjectedState),
                name + " must publish one stable replacement projection");
            TestAssert.True(semantics(after),
                name + " must preserve its projected-state semantics");
        }

        private static void ProductionMutationAndInputProfilingContractsRemainExplicit()
        {
            string root = TestSupport.FindRepositoryRoot(
                Path.Combine("Source", "Features", "Workloads", "V2", "WorkloadState.cs"),
                "workload draft projection cache contracts");
            string state = Read(root, "Source", "Features", "Workloads", "V2", "WorkloadState.cs");
            string window = Read(root, "Source", "UI", "MainTabWindow_BetterWork.cs");

            TestAssert.Contains(state, "public WorkloadProjectedState ProjectedState => GetProjectedState();",
                "projected-state reads must route through the cache boundary");
            TestAssert.Contains(state, "private void InvalidateProjectedState()",
                "draft mutation invalidation must have one explicit owner");

            string[] directMutators =
            {
                "SetParentPriority(WorkloadParentPriorityKey key, int priority)",
                "RemoveParentPriority(WorkloadParentPriorityKey key)",
                "SetParentPriorityIntent(",
                "SetParentPriorityNoOpinion(WorkloadParentPriorityKey key)",
                "SetManualMode(WorkloadParentPriorityKey key, bool manual)",
                "RemoveManualMode(WorkloadParentPriorityKey key)",
                "SetManualModeIntent(",
                "SetManualModeNoOpinion(WorkloadParentPriorityKey key)",
                "SetSchedule(PawnKey pawn, ScheduleKey schedule)",
                "RemoveSchedule(PawnKey pawn)",
                "SetSchedule(\n            WorkloadScheduleTargetKey key,",
                "SetScheduleIntent(",
                "SetScheduleNoOpinion(WorkloadScheduleTargetKey key)",
                "SetSpecificJobOverride(\n            WorkloadSpecificJobKey key,",
                "RemoveSpecificJobOverride(WorkloadSpecificJobKey key)",
                "SetSpecificPriorityIntent(",
                "SetSpecificPriorityNoOpinion(WorkloadSpecificJobTargetKey key)",
                "SetSpecificJobOrder(WorkloadSpecificJobKey key, int order)",
                "RemoveSpecificJobOrder(WorkloadSpecificJobKey key)",
                "SetWorkTypeOrderIntent(",
                "SetWorkTypeOrderNoOpinion(WorkloadWorkTypeOrderKey key)",
                "RemovePresentationSetting(string key)",
                "SetPresentationSettingIntent(",
                "ReleasePresentationSetting(string key)"
            };

            for (int i = 0; i < directMutators.Length; i++)
            {
                TestAssert.Contains(
                    MethodBody(state, directMutators[i]),
                    "InvalidateProjectedState();",
                    "every direct WorkloadDraft mutator must invalidate the projection cache: " + directMutators[i]);
            }

            string inputRoute = MethodBody(window, "private void RouteFrameInput(");
            TestAssert.Contains(inputRoute, "WorkTab.WorkloadPreview.SynchronizeInputMutation",
                "the first input synchronization must have a dedicated timing section");
            TestAssert.Contains(inputRoute, "if (SpineTiming.Enabled)",
                "input synchronization profiling must not allocate a delegate when disabled");
            TestAssert.Contains(window, "WorkTab.WorkloadPreview.SynchronizeFailSafeAfterPass",
                "the retained final synchronization must be labeled as a fail-safe, not click cost");
        }

        private static string Read(string root, params string[] parts)
        {
            string path = root;
            for (int i = 0; i < parts.Length; i++) path = Path.Combine(path, parts[i]);
            TestAssert.True(File.Exists(path), "expected production source file is missing: " + path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            TestAssert.True(start >= 0, "expected production method is missing: " + signature);
            int open = source.IndexOf('{', start);
            TestAssert.True(open >= 0, "expected production method body is missing: " + signature);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                if (source[i] != '}') continue;
                depth--;
                if (depth == 0) return source.Substring(open, i - open + 1);
            }

            throw new InvalidOperationException("expected production method end is missing: " + signature);
        }
    }
}
