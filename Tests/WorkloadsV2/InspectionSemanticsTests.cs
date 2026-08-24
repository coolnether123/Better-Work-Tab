using Better_Work_Tab.Features.Workloads.V2;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class InspectionSemanticsTests
    {
        public static void Run()
        {
            DirectColumnIndexesPreserveDuplicates();
            CompoundMasksComposeAndDeduplicate();
            ManualModeUsesOneEffectiveGlobalValue();
            ManualModeIntentsReplaceLegacyWithoutBreakingClearOnlyScope();
        }

        private static void DirectColumnIndexesPreserveDuplicates()
        {
            var index = new WorkloadInspectionColumnIndex();
            index.Add(2, "PlantWork", null, true);
            index.Add(7, "PlantWork", "PlantCut", true);
            index.Add(11, "PlantWork", "PlantCut", true);
            index.Add(13, "PlantWork", "PlantCut", false);
            index.Add(17, "Cook", null, true);

            TestAssert.Sequence(
                new[] { 2 },
                index.GetParentColumns("PlantWork"),
                "Parent lookup should contain only parent priority columns.");
            TestAssert.Sequence(
                new[] { 7, 11 },
                index.GetSpecificColumns("PlantWork", "PlantCut"),
                "Specific lookup should preserve duplicate projected columns.");
            TestAssert.Sequence(
                new[] { 2, 7, 11 },
                index.GetOrderingColumns("PlantWork"),
                "Ordering lookup should include every parent and specific column.");
            TestAssert.Sequence(
                new[] { 17 },
                index.GetOrderingColumns("Cook"),
                "Ordering lookup should remain keyed by canonical WorkType defName.");
        }

        private static void CompoundMasksComposeAndDeduplicate()
        {
            var masks = new WorkloadInspectionCellMasks();
            masks.Add(3, 9, WorkloadInspectionCellKind.ParentPriority);
            masks.Add(3, 9, WorkloadInspectionCellKind.Schedule);
            masks.Add(3, 9, WorkloadInspectionCellKind.Ordering);
            masks.Add(3, 9, WorkloadInspectionCellKind.SpecificPriority);
            masks.Add(3, 9, WorkloadInspectionCellKind.Schedule);

            WorkloadInspectionCellKind kind;
            TestAssert.True(
                masks.TryGet(3, 9, out kind),
                "A composed inspection cell should be present.");
            TestAssert.Equal(
                WorkloadInspectionCellKind.ParentPriority |
                    WorkloadInspectionCellKind.SpecificPriority |
                    WorkloadInspectionCellKind.Schedule |
                    WorkloadInspectionCellKind.Ordering,
                kind,
                "All semantic flags should compose on one cell.");
            TestAssert.Equal(
                1,
                masks.Count,
                "Duplicate semantic targets should produce one draw mask.");
        }

        private static void ManualModeUsesOneEffectiveGlobalValue()
        {
            WorkloadParentPriorityKey beforeKey = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p1"),
                TestSupport.WorkType("PlantWork"));
            WorkloadParentPriorityKey retainedKey = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p2"),
                TestSupport.WorkType("Cook"));
            WorkloadParentPriorityKey addedKey = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p3"),
                TestSupport.WorkType("Research"));

            var before = new WorkloadProjectedState(
                manualModes: new[]
                {
                    new WorkloadManualModeEntry(beforeKey, true),
                    new WorkloadManualModeEntry(retainedKey, true)
                });
            var sameEffectiveModeAfterMembershipChurn = new WorkloadProjectedState(
                manualModes: new[]
                {
                    new WorkloadManualModeEntry(retainedKey, true),
                    new WorkloadManualModeEntry(addedKey, true)
                });
            var changed = new WorkloadProjectedState(
                manualModes: new[]
                {
                    new WorkloadManualModeEntry(retainedKey, false),
                    new WorkloadManualModeEntry(addedKey, false)
                });

            TestAssert.False(
                WorkloadInspectionSemantics.HasEffectiveManualModeChange(
                    before,
                    sameEffectiveModeAfterMembershipChurn),
                "Membership key churn with the same effective mode is not a global change.");
            TestAssert.True(
                WorkloadInspectionSemantics.HasEffectiveManualModeChange(before, changed),
                "A changed effective manual mode should produce one global semantic marker.");
        }

        private static void ManualModeIntentsReplaceLegacyWithoutBreakingClearOnlyScope()
        {
            WorkloadParentPriorityKey released = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p1"), TestSupport.WorkType("PlantWork"));
            WorkloadParentPriorityKey set = new WorkloadParentPriorityKey(
                TestSupport.Pawn("p2"), TestSupport.WorkType("Cook"));
            var state = new WorkloadProjectedState(
                manualModes: new[]
                {
                    new WorkloadManualModeEntry(released, true),
                    new WorkloadManualModeEntry(set, true)
                },
                manualModeIntents: new[]
                {
                    new WorkloadManualModeIntentEntry(
                        released, WorkloadIntent<bool>.Clear),
                    new WorkloadManualModeIntentEntry(
                        set, WorkloadIntent<bool>.CreateSet(false))
                });

            var effective = WorkloadManualModeSemantics.GetEffectiveEntries(state);
            WorkloadIntent<bool> releasedIntent;
            WorkloadIntent<bool> setIntent;
            TestAssert.True(effective.TryGetValue(released, out releasedIntent) && releasedIntent.IsClear,
                "Typed Clear must replace a legacy manual Set while remaining represented for per-cell fallback.");
            TestAssert.True(effective.TryGetValue(set, out setIntent) && setIntent.HasValue && !setIntent.Value,
                "Typed Set must replace the legacy manual value before aggregate validation.");

            bool mode;
            bool hasEntries;
            bool conflict;
            TestAssert.True(WorkloadManualModeSemantics.TryGetGlobalMode(
                    effective.Values, out mode, out hasEntries, out conflict) && !mode && hasEntries && !conflict,
                "Clear plus one effective Set must retain the one global display value.");

            var clearOnly = new WorkloadProjectedState(
                manualModeIntents: new[]
                {
                    new WorkloadManualModeIntentEntry(
                        released, WorkloadIntent<bool>.Clear)
                });
            TestAssert.False(WorkloadManualModeSemantics.TryGetGlobalMode(
                    clearOnly, out mode, out hasEntries, out conflict),
                "Clear-only state must not demand a global manual-mode write.");
            TestAssert.True(hasEntries && !conflict,
                "Clear-only state remains represented without becoming an effective global Set.");

            var conflicting = new WorkloadProjectedState(
                manualModeIntents: new[]
                {
                    new WorkloadManualModeIntentEntry(
                        released, WorkloadIntent<bool>.CreateSet(true)),
                    new WorkloadManualModeIntentEntry(
                        set, WorkloadIntent<bool>.CreateSet(false))
                });
            TestAssert.False(WorkloadManualModeSemantics.TryGetGlobalMode(
                    conflicting, out mode, out hasEntries, out conflict),
                "Conflicting effective manual Sets must fail closed for aggregate display.");
            TestAssert.True(conflict,
                "Conflicting typed intents must be visible to centralized validation.");
        }
    }
}
