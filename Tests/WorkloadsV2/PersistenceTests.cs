using System.Collections.Generic;
using System.Xml;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Verse;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PersistenceTests
    {
        public static void Run()
        {
            var pawn = TestSupport.Pawn("p1");
            var workType = TestSupport.WorkType("PlantWork");
            var workGiver = TestSupport.WorkGiver("PlantCut");
            var scheduleKey = WorkloadScheduleTargetKey.ForWorkGiver(pawn, workType, workGiver);
            var specificKey = WorkloadSpecificJobTargetKey.Global(workType, workGiver);
            var orderKey = WorkloadWorkTypeOrderKey.Global(workType);
            var settingKey = "ui.angled";
            var schedule = TestSupport.ScheduleWithValue(3, 1 << 14);
            var order = new WorkloadWorkTypeOrderPayload(new[] { workGiver });
            var state = new WorkloadProjectedState(
                scheduleIntents: new[]
                {
                    TestSupport.ScheduleIntent(
                        scheduleKey,
                        WorkloadIntent<WorkloadSchedulePayload>.CreateSet(schedule))
                },
                specificPriorityIntents: new[]
                {
                    TestSupport.SpecificIntent(specificKey, WorkloadIntentState.Set, 4),
                    TestSupport.SpecificIntent(
                        WorkloadSpecificJobTargetKey.ForPawn(pawn, workType, workGiver),
                        WorkloadIntentState.Clear)
                },
                workTypeOrderIntents: new[]
                {
                    TestSupport.OrderIntent(
                        orderKey,
                        WorkloadIntent<WorkloadWorkTypeOrderPayload>.CreateSet(order))
                },
                presentationSettingIntents: new[]
                {
                    TestSupport.SettingIntent(
                        settingKey,
                        WorkloadIntent<WorkloadSettingValue>.CreateSet(
                            WorkloadSettingValue.WorkloadOwned(WorkloadScalarValue.FromBoolean(true))))
                },
                representedPawnIds: new[] { pawn });
            var template = TestSupport.Template(state);
            var templateValidation = WorkloadValidator.Validate(template);
            TestAssert.True(
                templateValidation.CanApply,
                "typed persistence fixture must validate: " +
                (templateValidation.Issues.Count == 0 ? "no diagnostic" : templateValidation.Issues[0].Message));

            var recordResult = WorkloadV2RecordConverter.TryFromTemplate(template);
            TestAssert.True(recordResult.Succeeded, "typed V2 template must convert to a persistence record");
            TestAssert.NotNull(recordResult.Value, "converted persistence record must be non-null");
            TestAssert.Equal(WorkloadSchema.CurrentVersion, recordResult.Value.SchemaVersion,
                "converted record must use the current workload schema");
            TestAssert.Equal(1, recordResult.Value.ScheduleIntents.Count,
                "24-hour schedule intent must persist as a typed record");
            TestAssert.Equal(1 << 14, recordResult.Value.ScheduleIntents[0].PinnedHourMask,
                "the persisted schedule must retain its pinned-hour mask");
            var globalPriorityRecord = TestSupport.Find(
                recordResult.Value.SpecificPriorityIntents,
                value => value.Scope == (int)WorkloadTargetScope.GlobalShared);
            TestAssert.NotNull(globalPriorityRecord,
                "shared specific-job scope must persist explicitly");
            var clearedPriorityRecord = TestSupport.Find(
                recordResult.Value.SpecificPriorityIntents,
                value => value.IntentState == (int)WorkloadIntentState.Clear);
            TestAssert.NotNull(clearedPriorityRecord,
                "specific-job tombstones must persist explicitly");

            var roundTrip = WorkloadV2RecordConverter.TryToTemplate(recordResult.Value);
            TestAssert.True(roundTrip.Succeeded, "current typed record must convert back to a template");
            TestAssert.True(template.ProjectedState.SemanticallyEquals(roundTrip.Value.ProjectedState),
                "typed persistence round-trip must preserve semantic state");

            var legacy = TestSupport.CurrentRecord("legacy-parent");
            legacy.SchemaVersion = WorkloadSchema.LegacyVersion;
            legacy.OwnershipDimensions = (int)WorkloadOwnershipDimensions.ParentPriorities;
            legacy.ParentPriorities.Add(new WorkloadV2ParentPriorityRecord
            {
                PawnId = "p1",
                WorkTypeDefName = "PlantWork",
                Priority = 3
            });
            var migrated = WorkloadV2RecordConverter.TryToTemplate(legacy);
            TestAssert.True(migrated.Succeeded, "lossless v1 parent priority data must migrate");
            TestAssert.Equal(WorkloadSchema.CurrentVersion, legacy.SchemaVersion,
                "v1 record must be promoted to the typed schema after migration");
            TestAssert.Equal(1, legacy.ParentPriorityIntents.Count,
                "v1 parent priority must gain an explicit Set intent");
            TestAssert.Equal((int)WorkloadIntentState.Set, legacy.ParentPriorityIntents[0].IntentState,
                "legacy positive values must migrate as Set, never as Clear");

            var legacyOrder = TestSupport.CurrentRecord("legacy-order-safe");
            legacyOrder.SchemaVersion = WorkloadSchema.LegacyVersion;
            legacyOrder.OwnershipDimensions = (int)WorkloadOwnershipDimensions.SpecificJobOrder;
            legacyOrder.SpecificJobOrder.Add(new WorkloadV2SpecificJobOrderRecord
            {
                PawnId = "p1",
                WorkTypeDefName = "PlantWork",
                WorkGiverDefName = "PlantCut",
                Order = 0
            });
            legacyOrder.SpecificJobOrder.Add(new WorkloadV2SpecificJobOrderRecord
            {
                PawnId = "p1",
                WorkTypeDefName = "PlantWork",
                WorkGiverDefName = "PlantHarvest",
                Order = 1
            });
            var migratedOrder = WorkloadV2RecordConverter.TryToTemplate(legacyOrder);
            TestAssert.True(migratedOrder.Succeeded,
                "a complete unique legacy order permutation must migrate safely");
            TestAssert.False(legacyOrder.LegacyOrderRequiresReview,
                "a safe legacy order migration must not remain read-only");
            TestAssert.Equal(1, legacyOrder.WorkTypeOrderIntents.Count,
                "a safe legacy order migration must create one typed WorkType order");
            TestAssert.Sequence(
                new[] { "PlantCut", "PlantHarvest" },
                legacyOrder.WorkTypeOrderIntents[0].OrderedWorkGiverDefNames,
                "safe legacy order migration must preserve rank order");

            var unsafeLegacyOrder = TestSupport.CurrentRecord("legacy-order-unsafe");
            unsafeLegacyOrder.SchemaVersion = WorkloadSchema.LegacyVersion;
            unsafeLegacyOrder.OwnershipDimensions = (int)WorkloadOwnershipDimensions.SpecificJobOrder;
            unsafeLegacyOrder.SpecificJobOrder.Add(new WorkloadV2SpecificJobOrderRecord
            {
                PawnId = "p1",
                WorkTypeDefName = "PlantWork",
                WorkGiverDefName = "PlantCut",
                Order = 1
            });
            var unsafeMigratedOrder = WorkloadV2RecordConverter.TryToTemplate(unsafeLegacyOrder);
            TestAssert.False(unsafeMigratedOrder.Succeeded,
                "an incomplete legacy order rank sequence must remain read-only");
            TestAssert.Equal(WorkloadDiagnosticCode.ReadOnlyDiagnostic, unsafeMigratedOrder.Code,
                "unsafe legacy order migration must use the read-only diagnostic");
            TestAssert.True(unsafeLegacyOrder.LegacyOrderRequiresReview,
                "unsafe legacy order migration must retain an explicit review marker");

            var duplicateTyped = TestSupport.CurrentRecord("duplicate-typed");
            duplicateTyped.OwnershipDimensions = (int)WorkloadOwnershipDimensions.SpecificJobOverrides;
            duplicateTyped.SpecificPriorityIntents.Add(new WorkloadV2SpecificPriorityIntentRecord
            {
                Scope = (int)WorkloadTargetScope.GlobalShared,
                WorkTypeDefName = "PlantWork",
                WorkGiverDefName = "PlantCut",
                IntentState = (int)WorkloadIntentState.Set,
                Priority = 2
            });
            duplicateTyped.SpecificPriorityIntents.Add(new WorkloadV2SpecificPriorityIntentRecord
            {
                Scope = (int)WorkloadTargetScope.GlobalShared,
                WorkTypeDefName = "PlantWork",
                WorkGiverDefName = "PlantCut",
                IntentState = (int)WorkloadIntentState.Clear
            });
            var duplicateTypedResult = WorkloadV2RecordConverter.TryToTemplate(duplicateTyped);
            TestAssert.False(duplicateTypedResult.Succeeded,
                "duplicate typed Set/Clear records must be rejected before state construction");
            TestAssert.Equal(WorkloadDiagnosticCode.InvalidState, duplicateTypedResult.Code,
                "duplicate typed targets must return structured InvalidState diagnostics");

            var noOpinionRecord = TestSupport.CurrentRecord("no-opinion-record");
            noOpinionRecord.OwnershipDimensions = (int)WorkloadOwnershipDimensions.SpecificJobOverrides;
            noOpinionRecord.SpecificPriorityIntents.Add(new WorkloadV2SpecificPriorityIntentRecord
            {
                Scope = (int)WorkloadTargetScope.GlobalShared,
                WorkTypeDefName = "PlantWork",
                WorkGiverDefName = "PlantCut",
                IntentState = (int)WorkloadIntentState.NoOpinion
            });
            var noOpinionResult = WorkloadV2RecordConverter.TryToTemplate(noOpinionRecord);
            TestAssert.True(noOpinionResult.Succeeded,
                "a serialized NoOpinion record must normalize to absence");
            TestAssert.Equal(0, noOpinionRecord.SpecificPriorityIntents.Count,
                "NoOpinion must not survive the persistence boundary");

            var legacySchedule = TestSupport.CurrentRecord("legacy-schedule");
            legacySchedule.SchemaVersion = WorkloadSchema.LegacyVersion;
            legacySchedule.OwnershipDimensions = (int)WorkloadOwnershipDimensions.Schedules;
            legacySchedule.Schedules.Add(new WorkloadV2ScheduleRecord
            {
                PawnId = "p1",
                Schedule = 0
            });
            var opaqueLegacy = WorkloadV2RecordConverter.TryToTemplate(legacySchedule);
            TestAssert.False(opaqueLegacy.Succeeded,
                "a v1 schedule without linked/pinned information must fail closed");
            TestAssert.Equal(WorkloadDiagnosticCode.ReadOnlyDiagnostic, opaqueLegacy.Code,
                "lossy v1 schedule migration must be a read-only diagnostic");
            TestAssert.True(legacySchedule.LegacyScheduleRequiresReview,
                "lossy v1 schedule migration must leave an explicit review marker");

            var newer = TestSupport.CurrentRecord("newer");
            newer.SchemaVersion = WorkloadSchema.CurrentVersion + 1;
            var newerResult = WorkloadV2RecordConverter.TryToTemplate(newer);
            TestAssert.False(newerResult.Succeeded, "newer workload schema must be rejected");
            TestAssert.Equal(WorkloadDiagnosticCode.NewerSchema, newerResult.Code,
                "newer workload schema must fail with a precise diagnostic");

            var document = new XmlDocument();
            document.LoadXml(
                "<root><WorkloadsV2><workloadsV2SchemaVersion>2</workloadsV2SchemaVersion>" +
                "<unexpectedFutureField>opaque</unexpectedFutureField></WorkloadsV2></root>");
            Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe.loader = new ScribeLoaderStub { curXmlParent = document.DocumentElement };
            var envelope = WorkloadV2PersistenceEnvelope.CreateEmpty();
            envelope.ExposeData();
            envelope.RefreshDiagnostics();
            TestAssert.True(envelope.HasOpaqueData, "unknown persisted XML must be detected as opaque");
            TestAssert.True(envelope.IsReadOnlyDiagnostic,
                "opaque persisted data must fail closed as a read-only diagnostic");
            TestAssert.Equal(WorkloadV2SchemaState.Opaque, envelope.SchemaState,
                "opaque persisted data must have the opaque schema state");
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.loader = null;

            var cas = WorkloadV2PersistenceEnvelope.CreateEmpty();
            cas.Records = new List<WorkloadV2PersistenceRecord> { recordResult.Value };
            cas.RefreshPersistenceMetadata(true);
            var expectedRevision = cas.PersistenceRevision;
            var expectedFingerprint = cas.PersistenceFingerprint;
            TestAssert.True(cas.TryValidateCompareAndSwap(expectedRevision, expectedFingerprint, out string casError),
                "persistence CAS must accept the captured revision and fingerprint");
            TestAssert.False(cas.TryValidateCompareAndSwap(expectedRevision - 1, expectedFingerprint, out casError),
                "persistence CAS must reject a stale revision");
        }
    }
}
