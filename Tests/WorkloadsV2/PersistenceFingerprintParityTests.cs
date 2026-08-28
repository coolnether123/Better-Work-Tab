using System.Collections.Generic;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class PersistenceFingerprintParityTests
    {
        // This is the established SHA-256 result for the fully populated record
        // fixture below. It protects the persisted CAS representation, including
        // normalization and token order, without inspecting production source.
        private const string GoldenFingerprint = "895699956f27df1361237fc9b26f5780c78299041219a9a49dbea76b57982d42";

        public static void Run()
        {
            string fingerprint = CreateGoldenEnvelope(false).ComputeContentFingerprint();
            TestAssert.Equal(
                GoldenFingerprint,
                fingerprint,
                "canonical persistence fingerprints must preserve the established golden representation");

            string reorderedFingerprint = CreateGoldenEnvelope(true).ComputeContentFingerprint();
            TestAssert.Equal(
                GoldenFingerprint,
                reorderedFingerprint,
                "canonical persistence fingerprints must retain normalized record ordering");
        }

        private static WorkloadV2PersistenceEnvelope CreateGoldenEnvelope(bool reverseRecords)
        {
            var empty = new WorkloadV2PersistenceRecord
            {
                StableId = null,
                Label = null,
                SchemaVersion = WorkloadSchema.CurrentVersion,
                ExplicitPawnIds = null,
                ExcludedPawnIds = null,
                ParentPriorities = null,
                ManualModes = null,
                Schedules = null,
                SpecificJobOverrides = null,
                SpecificJobOrder = null,
                PresentationSettings = null,
                ParentPriorityIntents = null,
                ManualModeIntents = null,
                ScheduleIntents = null,
                SpecificPriorityIntents = null,
                WorkTypeOrderIntents = null,
                PresentationSettingIntents = null,
                MigrationDiagnostic = null
            };
            var rich = new WorkloadV2PersistenceRecord
            {
                StableId = "zeta-雪<color=#ab12cd>markup</color>😺",
                Label = "<i>Night</i> Shift\u001fΩ",
                SchemaVersion = WorkloadSchema.CurrentVersion,
                OwnershipDimensions = (int)WorkloadOwnershipDimensions.All,
                ScopeMode = (int)WorkloadScopeMode.ExplicitPawnIds,
                ExplicitPawnIds = new List<string> { "pawn-z", null, "pawn-a", "pawn-a" },
                ExcludedPawnIds = new List<string> { string.Empty, "pawn-x" },
                ParentPriorities = new List<WorkloadV2ParentPriorityRecord>
                {
                    null,
                    new WorkloadV2ParentPriorityRecord
                    {
                        PawnId = "pawn-雪",
                        WorkTypeDefName = "Cook",
                        Priority = 4
                    }
                },
                ManualModes = new List<WorkloadV2ManualModeRecord>
                {
                    new WorkloadV2ManualModeRecord
                    {
                        PawnId = "pawn-a",
                        WorkTypeDefName = "PlantCut",
                        Manual = true
                    }
                },
                Schedules = new List<WorkloadV2ScheduleRecord>
                {
                    new WorkloadV2ScheduleRecord { PawnId = null, Schedule = -1 },
                    new WorkloadV2ScheduleRecord { PawnId = "pawn-雪", Schedule = 23 }
                },
                SpecificJobOverrides = new List<WorkloadV2SpecificJobOverrideRecord>
                {
                    new WorkloadV2SpecificJobOverrideRecord
                    {
                        PawnId = "pawn-雪",
                        WorkTypeDefName = "Cook",
                        WorkGiverDefName = "DoBillsCook",
                        Value = Scalar(3, false, -7, "<color=#fff>é😺</color>")
                    },
                    new WorkloadV2SpecificJobOverrideRecord
                    {
                        PawnId = "pawn-z",
                        WorkTypeDefName = "Haul",
                        WorkGiverDefName = "HaulGeneral",
                        Value = null
                    }
                },
                SpecificJobOrder = new List<WorkloadV2SpecificJobOrderRecord>
                {
                    new WorkloadV2SpecificJobOrderRecord
                    {
                        PawnId = "pawn-雪",
                        WorkTypeDefName = "Cook",
                        WorkGiverDefName = "DoBillsCook",
                        Order = 2
                    }
                },
                PresentationSettings = new List<WorkloadV2PresentationSettingRecord>
                {
                    new WorkloadV2PresentationSettingRecord
                    {
                        Key = "display.<b>angled</b>",
                        Value = Scalar(1, true, 17, "α")
                    },
                    new WorkloadV2PresentationSettingRecord
                    {
                        Key = "display.empty",
                        Value = null
                    }
                },
                ParentPriorityIntents = new List<WorkloadV2ParentPriorityIntentRecord>
                {
                    new WorkloadV2ParentPriorityIntentRecord
                    {
                        PawnId = "pawn-雪",
                        WorkTypeDefName = "Cook",
                        IntentState = (int)WorkloadIntentState.Set,
                        Priority = 1
                    }
                },
                ManualModeIntents = new List<WorkloadV2ManualModeIntentRecord>
                {
                    new WorkloadV2ManualModeIntentRecord
                    {
                        PawnId = "pawn-a",
                        WorkTypeDefName = "PlantCut",
                        IntentState = (int)WorkloadIntentState.Clear,
                        Manual = false
                    }
                },
                ScheduleIntents = new List<WorkloadV2ScheduleIntentRecord>
                {
                    new WorkloadV2ScheduleIntentRecord
                    {
                        Scope = (int)WorkloadTargetScope.PawnLocal,
                        PawnId = "pawn-雪",
                        TargetKind = (int)WorkloadScheduleTargetKind.WorkGiver,
                        WorkTypeDefName = "Cook",
                        WorkGiverDefName = "DoBillsCook",
                        IntentState = (int)WorkloadIntentState.Set,
                        Priorities = new List<int> { 4, 3, 2, 1 },
                        PinnedHourMask = 1 << 23
                    }
                },
                SpecificPriorityIntents = new List<WorkloadV2SpecificPriorityIntentRecord>
                {
                    new WorkloadV2SpecificPriorityIntentRecord
                    {
                        Scope = (int)WorkloadTargetScope.GlobalShared,
                        PawnId = null,
                        WorkTypeDefName = "Cook",
                        WorkGiverDefName = "DoBillsCook",
                        IntentState = (int)WorkloadIntentState.Set,
                        Priority = 4
                    },
                    new WorkloadV2SpecificPriorityIntentRecord
                    {
                        Scope = (int)WorkloadTargetScope.PawnLocal,
                        PawnId = "removed-by-normalization",
                        WorkTypeDefName = "Mine",
                        WorkGiverDefName = "Mine",
                        IntentState = (int)WorkloadIntentState.NoOpinion,
                        Priority = 99
                    }
                },
                WorkTypeOrderIntents = new List<WorkloadV2WorkTypeOrderIntentRecord>
                {
                    new WorkloadV2WorkTypeOrderIntentRecord
                    {
                        Scope = (int)WorkloadTargetScope.GlobalShared,
                        PawnId = null,
                        WorkTypeDefName = "Cook",
                        IntentState = (int)WorkloadIntentState.Set,
                        IsComplete = true,
                        OrderedWorkGiverDefNames = new List<string> { "CookFine", null, "CookSimple" }
                    },
                    new WorkloadV2WorkTypeOrderIntentRecord
                    {
                        Scope = (int)WorkloadTargetScope.PawnLocal,
                        PawnId = "removed-by-normalization",
                        WorkTypeDefName = "Mine",
                        IntentState = (int)WorkloadIntentState.NoOpinion,
                        IsComplete = false
                    }
                },
                PresentationSettingIntents = new List<WorkloadV2PresentationSettingIntentRecord>
                {
                    new WorkloadV2PresentationSettingIntentRecord
                    {
                        Key = "display.β",
                        IntentState = (int)WorkloadIntentState.Set,
                        Ownership = (int)WorkloadSettingOwnership.WorkloadOwned,
                        Value = Scalar(2, false, -12, "<color=#0f0>雪</color>")
                    }
                },
                LegacyScheduleRequiresReview = true,
                LegacyOrderRequiresReview = true,
                MigrationDiagnostic = "<color=#ff0000>legacy</color> 😺"
            };

            var envelope = WorkloadV2PersistenceEnvelope.CreateEmpty();
            envelope.SchemaVersion = WorkloadSchema.CurrentVersion;
            envelope.CurrentWorkloadId = null;
            envelope.Records = reverseRecords
                ? new List<WorkloadV2PersistenceRecord> { null, rich, empty }
                : new List<WorkloadV2PersistenceRecord> { empty, null, rich };
            return envelope;
        }

        private static WorkloadV2ScalarRecord Scalar(int kind, bool booleanValue, int integerValue, string stringValue)
        {
            return new WorkloadV2ScalarRecord
            {
                Kind = kind,
                BooleanValue = booleanValue,
                IntegerValue = integerValue,
                StringValue = stringValue
            };
        }
    }
}
