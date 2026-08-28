using System;
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
                (templateValidation.Issues.Count == 0 ? "no diagnostic" : templateValidation.Issues[0].Code.ToString()));

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

            PresentationIntentMigrationPreservesSourceShape();
            SerializationRejectsUnmigratedSchema2Schedule();

            var legacy = TestSupport.CurrentRecord("legacy-parent");
            legacy.SchemaVersion = WorkloadSchema.LegacyVersion;
            legacy.OwnershipDimensions = (int)WorkloadOwnershipDimensions.ParentPriorities;
            legacy.ParentPriorities.Add(new WorkloadV2ParentPriorityRecord
            {
                PawnId = "p1",
                WorkTypeDefName = "PlantWork",
                Priority = 3
            });
            MigrateDuringDocumentLoad(legacy);
            var migrated = WorkloadV2RecordConverter.TryToTemplate(legacy);
            TestAssert.True(migrated.Succeeded, "lossless v1 parent priority data must migrate during document load");
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
            MigrateDuringDocumentLoad(legacyOrder);
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
            MigrateDuringDocumentLoad(unsafeLegacyOrder);
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
            MigrateDuringDocumentLoad(legacySchedule);
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

            var direct = WorkloadV2PersistenceEnvelope.CreateEmpty();
            direct.Records = new List<WorkloadV2PersistenceRecord> { recordResult.Value };
            direct.RefreshPersistenceMetadata(true);
            int directRevision = direct.PersistenceRevision;
            direct.CurrentWorkloadId = "direct-current";
            TestAssert.True(
                direct.TryAdvanceDirectMutationRevision(out string directError),
                "a direct repository mutation must advance its persistence metadata");
            TestAssert.Equal(
                directRevision + 1,
                direct.PersistenceRevision,
                "a direct repository mutation must advance revision exactly once");
            TestAssert.True(
                direct.TryValidateCompareAndSwap(
                    direct.PersistenceRevision,
                    direct.PersistenceFingerprint,
                    out directError),
                "a direct repository mutation must publish its authoritative fingerprint");
            direct.PersistenceRevision = int.MaxValue;
            TestAssert.False(
                direct.TryAdvanceDirectMutationRevision(out directError),
                "direct repository metadata must fail closed at revision exhaustion");
            TestAssert.Equal(
                int.MaxValue,
                direct.PersistenceRevision,
                "revision exhaustion must not wrap direct repository metadata");

            string beforeCurrentIdFingerprint = cas.ComputeContentFingerprint();
            cas.CurrentWorkloadId = "night-shift";
            TestAssert.False(
                StringComparer.Ordinal.Equals(
                    beforeCurrentIdFingerprint,
                    cas.ComputeContentFingerprint()),
                "the active workload identity must participate in the persistence fingerprint");
            TestAssert.False(
                cas.TryValidateCompareAndSwap(
                    expectedRevision,
                    beforeCurrentIdFingerprint,
                    out casError),
                "a current-workload activation must invalidate an older CAS fingerprint");
            cas.RefreshPersistenceMetadata(false);
            TestAssert.True(
                cas.TryValidateCompareAndSwap(
                    expectedRevision,
                    cas.PersistenceFingerprint,
                    out casError),
                "the post-activation persistence receipt must use authoritative metadata");
        }

        private static void PresentationIntentMigrationPreservesSourceShape()
        {
            const string settingKey = "ui.angled";

            var unmigrated = Schema2Record("unmigrated-schema-2");
            unmigrated.PresentationSettings.Add(ScalarSetting(settingKey, true));
            WorkloadOperationResult<WorkloadTemplate> unmigratedResult =
                WorkloadV2RecordConverter.TryToTemplate(unmigrated);
            TestAssert.False(unmigratedResult.Succeeded,
                "ordinary conversion must reject schema-2 records before document-load migration");
            TestAssert.Equal(WorkloadDiagnosticCode.UnsupportedSchema, unmigratedResult.Code,
                "an unmigrated schema-2 record must report the document-load migration boundary");
            TestAssert.Equal(WorkloadDiagnosticCode.UnsupportedSchema, unmigratedResult.Code,
                "the schema-2 runtime diagnostic must remain structured");
            TestAssert.Equal(WorkloadSchema.PresentationIntentVersion, unmigrated.SchemaVersion,
                "ordinary conversion must not promote an unmigrated schema-2 record");

            var absent = Schema2Record("presentation-absent");
            var absentSource = Schema2Record("presentation-absent-source");
            absentSource.PresentationSettingIntents = null;
            XmlElement absentXml = RoundTripPresentationIntentMember(absentSource, absent);
            TestAssert.True(absentXml.SelectSingleNode("presentationSettingIntents") == null,
                "an absent typed member must remain absent when saved and loaded by the Scribe fixture");
            WorkloadV2PersistenceEnvelope absentEnvelope = MigrateDuringDocumentLoad(absent);
            TestAssert.False(absentEnvelope.IsReadOnlyDiagnostic,
                "an absent typed member must migrate at the document-load boundary");
            WorkloadOperationResult<WorkloadTemplate> absentResult =
                WorkloadV2RecordConverter.TryToTemplate(absent);
            TestAssert.True(absentResult.Succeeded,
                "an absent schema-2 presentation payload must remain readable");
            TestAssert.Equal(WorkloadSchema.CurrentVersion, absent.SchemaVersion,
                "an absent schema-2 presentation payload must promote to the current schema");
            TestAssert.Equal(0, absentResult.Value.ProjectedState.PresentationSettingIntents.Count,
                "an absent schema-2 presentation payload must not invent typed intents");
            TestAssert.Equal(0, absentResult.Value.ProjectedState.PresentationSettings.Count,
                "an absent schema-2 presentation payload must not invent scalar values");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> absentRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(absentResult.Value);
            TestAssert.True(absentRoundTrip.Succeeded,
                "an absent presentation payload must serialize after schema promotion");
            TestAssert.Equal(WorkloadSchema.CurrentVersion, absentRoundTrip.Value.SchemaVersion,
                "an absent presentation payload must serialize as the current schema");

            var scalarOnly = Schema2Record("scalar-only");
            scalarOnly.PresentationSettings.Add(ScalarSetting(settingKey, true));
            var scalarOnlySource = Schema2Record("scalar-only-source");
            scalarOnlySource.PresentationSettingIntents = null;
            RoundTripPresentationIntentMember(scalarOnlySource, scalarOnly);
            MigrateDuringDocumentLoad(scalarOnly);
            WorkloadOperationResult<WorkloadTemplate> scalarOnlyResult =
                WorkloadV2RecordConverter.TryToTemplate(scalarOnly);
            TestAssert.True(scalarOnlyResult.Succeeded,
                "a schema-2 scalar-only setting must migrate to typed presentation state");
            TestAssert.Equal(WorkloadSchema.CurrentVersion, scalarOnly.SchemaVersion,
                "scalar-only schema-2 state must promote to the current schema exactly once");
            TestAssert.Equal(1, scalarOnly.PresentationSettingIntents.Count,
                "scalar-only migration must add one typed setting intent");
            TestAssert.True(
                scalarOnlyResult.Value.ProjectedState.PresentationSettingIntents[0]
                    .Intent.Value.Scalar.BooleanValue,
                "scalar-only migration must retain the scalar setting value");
            WorkloadOperationResult<WorkloadTemplate> scalarOnlyRepeat =
                WorkloadV2RecordConverter.TryToTemplate(scalarOnly);
            TestAssert.True(scalarOnlyRepeat.Succeeded,
                "an already-promoted scalar-only record must remain readable");
            TestAssert.Equal(1, scalarOnly.PresentationSettingIntents.Count,
                "re-reading a promoted record must not duplicate scalar promotion");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> scalarOnlyRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(scalarOnlyResult.Value);
            TestAssert.True(scalarOnlyRoundTrip.Succeeded,
                "a promoted scalar-only record must serialize through the current schema");
            TestAssert.Equal(WorkloadSchema.CurrentVersion, scalarOnlyRoundTrip.Value.SchemaVersion,
                "a promoted scalar-only record must not serialize back as schema 2");
            WorkloadOperationResult<WorkloadTemplate> scalarOnlyRoundTripRead =
                WorkloadV2RecordConverter.TryToTemplate(scalarOnlyRoundTrip.Value);
            TestAssert.True(scalarOnlyRoundTripRead.Succeeded,
                "a promoted scalar-only record must round-trip through the current schema");
            TestAssert.True(
                scalarOnlyResult.Value.ProjectedState.SemanticallyEquals(
                    scalarOnlyRoundTripRead.Value.ProjectedState),
                "a promoted scalar-only round-trip must preserve typed presentation state");

            var explicitTypedEmpty = Schema2Record("typed-empty");
            explicitTypedEmpty.PresentationSettings.Add(ScalarSetting(settingKey, true));
            XmlElement typedEmptyXml = RoundTripPresentationIntentMember(
                Schema2Record("typed-empty-source"),
                explicitTypedEmpty);
            TestAssert.True(typedEmptyXml.SelectSingleNode("presentationSettingIntents") != null,
                "an explicit empty typed member must be emitted by the Scribe fixture");
            WorkloadV2PersistenceEnvelope typedEmptyEnvelope =
                MigrateDuringDocumentLoad(explicitTypedEmpty);
            TestAssert.False(typedEmptyEnvelope.IsReadOnlyDiagnostic,
                "an explicit empty typed member must migrate at the document-load boundary");
            WorkloadOperationResult<WorkloadTemplate> explicitTypedEmptyResult =
                WorkloadV2RecordConverter.TryToTemplate(explicitTypedEmpty);
            TestAssert.True(explicitTypedEmptyResult.Succeeded,
                "an explicit empty typed member must remain readable");
            TestAssert.Equal(0, explicitTypedEmpty.PresentationSettingIntents.Count,
                "an explicit empty typed member must not be populated from scalar compatibility data");
            TestAssert.Equal(0, explicitTypedEmptyResult.Value.ProjectedState.PresentationSettingIntents.Count,
                "an explicit empty typed member must remain empty after migration");
            TestAssert.Equal(0, explicitTypedEmptyResult.Value.ProjectedState.PresentationSettings.Count,
                "an explicit empty typed member must suppress stale scalar compatibility data");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> explicitTypedEmptyRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(explicitTypedEmptyResult.Value);
            TestAssert.True(explicitTypedEmptyRoundTrip.Succeeded,
                "an explicit empty typed member must serialize after migration");
            TestAssert.Equal(0, explicitTypedEmptyRoundTrip.Value.PresentationSettingIntents.Count,
                "an explicit empty typed member must remain empty after serialization");
            TestAssert.Equal(0, explicitTypedEmptyRoundTrip.Value.PresentationSettings.Count,
                "an explicit empty typed member must not revive stale scalar compatibility data");

            var typedOnly = Schema2Record("typed-only");
            typedOnly.PresentationSettingIntents.Add(TypedSetting(
                "future.setting",
                WorkloadIntentState.Set,
                true));
            MigrateDuringDocumentLoad(typedOnly);
            WorkloadOperationResult<WorkloadTemplate> typedOnlyResult =
                WorkloadV2RecordConverter.TryToTemplate(typedOnly);
            TestAssert.True(typedOnlyResult.Succeeded,
                "a typed-only setting with an unrecognized key must round-trip without a schema rewrite");
            TestAssert.Equal("future.setting",
                typedOnlyResult.Value.ProjectedState.PresentationSettingIntents[0].Key,
                "the persistence boundary must retain unrecognized setting keys");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> typedOnlyRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(typedOnlyResult.Value);
            TestAssert.True(typedOnlyRoundTrip.Succeeded,
                "a typed-only presentation setting must serialize through the current schema");
            WorkloadOperationResult<WorkloadTemplate> typedOnlyRoundTripRead =
                WorkloadV2RecordConverter.TryToTemplate(typedOnlyRoundTrip.Value);
            TestAssert.True(typedOnlyRoundTripRead.Succeeded,
                "a typed-only presentation setting must round-trip through the current schema");
            TestAssert.Equal("future.setting",
                typedOnlyRoundTripRead.Value.ProjectedState.PresentationSettingIntents[0].Key,
                "a typed-only round-trip must retain unrecognized setting keys");

            var matching = Schema2Record("mixed-matching");
            matching.PresentationSettings.Add(ScalarSetting(settingKey, true));
            matching.PresentationSettingIntents.Add(TypedSetting(
                settingKey,
                WorkloadIntentState.Set,
                true));
            MigrateDuringDocumentLoad(matching);
            WorkloadOperationResult<WorkloadTemplate> matchingResult =
                WorkloadV2RecordConverter.TryToTemplate(matching);
            TestAssert.True(matchingResult.Succeeded,
                "matching typed and scalar presentation data must remain readable");
            TestAssert.True(
                matchingResult.Value.ProjectedState.PresentationSettings[0].Value.BooleanValue,
                "matching mixed state must keep its typed value");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> matchingRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(matchingResult.Value);
            TestAssert.True(matchingRoundTrip.Succeeded,
                "matching mixed state must serialize through the current schema");
            TestAssert.Equal(1, matchingRoundTrip.Value.PresentationSettingIntents.Count,
                "matching mixed state must retain its one authoritative typed intent after serialization");

            var conflicting = Schema2Record("mixed-conflicting");
            conflicting.PresentationSettings.Add(ScalarSetting(settingKey, false));
            conflicting.PresentationSettingIntents.Add(TypedSetting(
                settingKey,
                WorkloadIntentState.Set,
                true));
            MigrateDuringDocumentLoad(conflicting);
            WorkloadOperationResult<WorkloadTemplate> conflictingResult =
                WorkloadV2RecordConverter.TryToTemplate(conflicting);
            TestAssert.True(conflictingResult.Succeeded,
                "conflicting scalar compatibility data must defer to typed presentation state");
            TestAssert.True(
                conflictingResult.Value.ProjectedState.PresentationSettings[0].Value.BooleanValue,
                "typed state must win when scalar and typed presentation values conflict");
            TestAssert.Equal(1, conflicting.PresentationSettingIntents.Count,
                "conflicting mixed state must not append a second typed intent during migration");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> conflictingRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(conflictingResult.Value);
            TestAssert.True(conflictingRoundTrip.Succeeded,
                "conflicting mixed state must serialize through the current schema");
            WorkloadOperationResult<WorkloadTemplate> conflictingRoundTripRead =
                WorkloadV2RecordConverter.TryToTemplate(conflictingRoundTrip.Value);
            TestAssert.True(conflictingRoundTripRead.Succeeded,
                "conflicting mixed state must round-trip through the current schema");
            TestAssert.True(
                conflictingRoundTripRead.Value.ProjectedState.PresentationSettings[0].Value.BooleanValue,
                "conflicting mixed state must retain the typed winner after a current-schema round-trip");

            var clear = Schema2Record("typed-clear");
            clear.PresentationSettings.Add(ScalarSetting(settingKey, true));
            clear.PresentationSettingIntents.Add(TypedSetting(
                settingKey,
                WorkloadIntentState.Clear,
                false));
            MigrateDuringDocumentLoad(clear);
            WorkloadOperationResult<WorkloadTemplate> clearResult =
                WorkloadV2RecordConverter.TryToTemplate(clear);
            TestAssert.True(clearResult.Succeeded,
                "a typed presentation tombstone must survive schema migration");
            TestAssert.True(
                clearResult.Value.ProjectedState.PresentationSettingIntents[0].Intent.IsClear,
                "typed Clear must remain an explicit presentation tombstone");
            TestAssert.Equal(0, clearResult.Value.ProjectedState.PresentationSettings.Count,
                "typed Clear must remove stale scalar compatibility data");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> clearRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(clearResult.Value);
            TestAssert.True(clearRoundTrip.Succeeded,
                "a typed presentation tombstone must serialize after migration");
            TestAssert.Equal((int)WorkloadIntentState.Clear,
                clearRoundTrip.Value.PresentationSettingIntents[0].IntentState,
                "presentation Clear must retain its persisted intent state");

            var noOpinion = Schema2Record("typed-no-opinion");
            noOpinion.PresentationSettings.Add(ScalarSetting(settingKey, true));
            noOpinion.PresentationSettingIntents.Add(TypedSetting(
                settingKey,
                WorkloadIntentState.NoOpinion,
                false));
            MigrateDuringDocumentLoad(noOpinion);
            WorkloadOperationResult<WorkloadTemplate> noOpinionResult =
                WorkloadV2RecordConverter.TryToTemplate(noOpinion);
            TestAssert.True(noOpinionResult.Succeeded,
                "typed NoOpinion presentation data must remain readable during migration");
            TestAssert.Equal(0, noOpinionResult.Value.ProjectedState.PresentationSettingIntents.Count,
                "typed NoOpinion must normalize to absence instead of restoring the scalar value");
            TestAssert.Equal(0, noOpinionResult.Value.ProjectedState.PresentationSettings.Count,
                "typed NoOpinion must not leave stale scalar compatibility data");
            WorkloadOperationResult<WorkloadV2PersistenceRecord> noOpinionRoundTrip =
                WorkloadV2RecordConverter.TryFromTemplate(noOpinionResult.Value);
            TestAssert.True(noOpinionRoundTrip.Succeeded,
                "normalized NoOpinion state must serialize cleanly");
            TestAssert.Equal(0, noOpinionRoundTrip.Value.PresentationSettingIntents.Count,
                "serialized NoOpinion must remain absent after a round-trip");
        }

        private static void SerializationRejectsUnmigratedSchema2Schedule()
        {
            var definition = new WorkloadDefinition(
                "schema-2-legacy-schedule",
                "Schema 2 Legacy Schedule",
                WorkloadSchema.PresentationIntentVersion,
                WorkloadOwnershipDimensions.Schedules,
                WorkloadScope.Explicit(new[] { "p1" }));
            var state = new WorkloadProjectedState(
                schedules: new[] { new WorkloadScheduleEntry("p1", 0) });
            var template = new WorkloadTemplate(definition, state);

            WorkloadOperationResult<WorkloadV2PersistenceRecord> result =
                WorkloadV2RecordConverter.TryFromTemplate(template);
            TestAssert.False(result.Succeeded,
                "schema-2 legacy schedule data must not serialize as nominal schema 3 without typed schedule intent");
            TestAssert.Equal(WorkloadDiagnosticCode.UnsupportedSchema, result.Code,
                "unmigrated schema-2 templates must report the document-load migration boundary");
            TestAssert.Equal(WorkloadDiagnosticCode.UnsupportedSchema, result.Code,
                "the schema-2 serialization diagnostic must remain structured");
        }

        private static WorkloadV2PersistenceEnvelope MigrateDuringDocumentLoad(
            WorkloadV2PersistenceRecord record)
        {
            var envelope = WorkloadV2PersistenceEnvelope.CreateEmpty();
            envelope.SchemaVersion = record.SchemaVersion;
            envelope.Records = new List<WorkloadV2PersistenceRecord> { record };
            envelope.NormalizeAfterLoad();
            return envelope;
        }

        private static WorkloadV2PersistenceRecord Schema2Record(string stableId)
        {
            WorkloadV2PersistenceRecord record = TestSupport.CurrentRecord(stableId);
            record.SchemaVersion = WorkloadSchema.PresentationIntentVersion;
            record.OwnershipDimensions = (int)WorkloadOwnershipDimensions.PresentationSettings;
            return record;
        }

        private static WorkloadV2PresentationSettingRecord ScalarSetting(string key, bool value)
        {
            return new WorkloadV2PresentationSettingRecord
            {
                Key = key,
                Value = new WorkloadV2ScalarRecord
                {
                    Kind = (int)WorkloadScalarKind.Boolean,
                    BooleanValue = value
                }
            };
        }

        private static WorkloadV2PresentationSettingIntentRecord TypedSetting(
            string key,
            WorkloadIntentState intentState,
            bool value)
        {
            return new WorkloadV2PresentationSettingIntentRecord
            {
                Key = key,
                IntentState = (int)intentState,
                Ownership = (int)WorkloadSettingOwnership.WorkloadOwned,
                Value = new WorkloadV2ScalarRecord
                {
                    Kind = (int)WorkloadScalarKind.Boolean,
                    BooleanValue = value
                }
            };
        }

        private static XmlElement RoundTripPresentationIntentMember(
            WorkloadV2PersistenceRecord source,
            WorkloadV2PersistenceRecord target)
        {
            var document = new XmlDocument();
            document.LoadXml("<li />");
            Scribe.mode = LoadSaveMode.Saving;
            Scribe.saver = new ScribeSaverStub { curXmlParent = document.DocumentElement };
            try
            {
                source.ExposeData();
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.saver = null;
            }

            Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe.loader = new ScribeLoaderStub { curXmlParent = document.DocumentElement };
            try
            {
                target.ExposeData();
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.loader = null;
            }

            return document.DocumentElement;
        }
    }
}
