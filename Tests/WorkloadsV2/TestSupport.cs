using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Better_Work_Tab.Features.Workloads.V2;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.Mod_Support.Multiplayer.Features.Workloads;
using Better_Work_Tab.UI.WorkGrid.Projection;

namespace BetterWorkTab.WorkloadsV2.Deterministic
{
    internal static class TestSupport
    {
        internal static string FindRepositoryRoot(
            string relativePath,
            string description)
        {
            string[] starts =
            {
                Environment.GetEnvironmentVariable("BWT_WORKLOADS_SOURCE_ROOT"),
                Environment.GetEnvironmentVariable("BWT_ROOT"),
                Directory.GetCurrentDirectory(),
                AppDomain.CurrentDomain.BaseDirectory
            };

            for (int startIndex = 0; startIndex < starts.Length; startIndex++)
            {
                if (string.IsNullOrWhiteSpace(starts[startIndex]))
                {
                    continue;
                }

                DirectoryInfo directory;
                try
                {
                    directory = new DirectoryInfo(Path.GetFullPath(starts[startIndex]));
                }
                catch (Exception)
                {
                    continue;
                }

                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            throw new InvalidOperationException(
                "Could not locate the Better Work Tab repository for " +
                (description ?? "source contracts") +
                ". Set BWT_WORKLOADS_SOURCE_ROOT to the repository root when " +
                "running test output outside the checkout.");
        }

        public static PawnKey Pawn(string id) => new PawnKey(id);
        public static WorkTypeKey WorkType(string id) => new WorkTypeKey(id);
        public static WorkGiverKey WorkGiver(string id) => new WorkGiverKey(id);

        public static WorkloadSchedulePayload Schedule(int pinnedHourMask, int basePriority = 3)
        {
            var priorities = new int[WorkloadSchedulePayload.HourCount];
            for (var hour = 0; hour < priorities.Length; hour++)
            {
                priorities[hour] = (basePriority + hour) % 6;
            }

            return new WorkloadSchedulePayload(priorities, pinnedHourMask);
        }

        public static WorkloadSchedulePayload ScheduleWithValue(
            int value,
            int pinnedHourMask)
        {
            var priorities = Enumerable.Repeat(value, WorkloadSchedulePayload.HourCount).ToArray();
            return new WorkloadSchedulePayload(priorities, pinnedHourMask);
        }

        public static WorkloadTemplate Template(
            WorkloadProjectedState state,
            WorkloadOwnershipDimensions ownership = WorkloadOwnershipDimensions.All,
            string stableId = "night-shift",
            string label = "Night Shift")
        {
            var definition = new WorkloadDefinition(
                stableId,
                label,
                WorkloadSchema.CurrentVersion,
                ownership,
                WorkloadScope.CurrentMapFreeColonists());
            return new WorkloadTemplate(definition, state ?? WorkloadProjectedState.Empty);
        }

        public static WorkloadParentPriorityIntentEntry ParentPriorityIntent(
            PawnKey pawn,
            WorkTypeKey workType,
            WorkloadIntentState state,
            int priority = 0)
        {
            var intent = state == WorkloadIntentState.Set
                ? WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                    new WorkloadSpecificPriorityPayload(priority))
                : state == WorkloadIntentState.Clear
                    ? WorkloadIntent<WorkloadSpecificPriorityPayload>.Clear
                    : WorkloadIntent<WorkloadSpecificPriorityPayload>.NoOpinion;
            return new WorkloadParentPriorityIntentEntry(
                new WorkloadParentPriorityKey(pawn, workType),
                intent);
        }

        public static WorkloadScheduleIntentEntry ScheduleIntent(
            WorkloadScheduleTargetKey key,
            WorkloadIntent<WorkloadSchedulePayload> intent)
        {
            return new WorkloadScheduleIntentEntry(key, intent);
        }

        public static WorkloadSpecificPriorityIntentEntry SpecificIntent(
            WorkloadSpecificJobTargetKey key,
            WorkloadIntentState state,
            int priority = 0)
        {
            var intent = state == WorkloadIntentState.Set
                ? WorkloadIntent<WorkloadSpecificPriorityPayload>.CreateSet(
                    new WorkloadSpecificPriorityPayload(priority))
                : state == WorkloadIntentState.Clear
                    ? WorkloadIntent<WorkloadSpecificPriorityPayload>.Clear
                    : WorkloadIntent<WorkloadSpecificPriorityPayload>.NoOpinion;
            return new WorkloadSpecificPriorityIntentEntry(key, intent);
        }

        public static WorkloadWorkTypeOrderIntentEntry OrderIntent(
            WorkloadWorkTypeOrderKey key,
            WorkloadIntent<WorkloadWorkTypeOrderPayload> intent)
        {
            return new WorkloadWorkTypeOrderIntentEntry(key, intent);
        }

        public static WorkloadPresentationSettingIntentEntry SettingIntent(
            string key,
            WorkloadIntent<WorkloadSettingValue> intent)
        {
            return new WorkloadPresentationSettingIntentEntry(key, intent);
        }

        public static LiveWorkTabEffectiveStateProvider LiveProvider(
            WorkloadScalarValue globalSetting,
            WorkloadSpecificPriorityPayload globalPriority,
            WorkloadWorkTypeOrderPayload globalOrder,
            WorkloadScheduleTargetKey scheduleKey,
            WorkloadSchedulePayload schedule)
        {
            var callbacks = new LiveWorkTabEffectiveStateCallbacks("test.live")
            {
                Revision = () => 10L,
                RevisionVector = () => WorkTabEffectiveStateRevisionVector.FromRevision(10L),
                PresentationSettingV2 = delegate(string key)
                {
                    return string.Equals(key, "ui.angled", StringComparison.Ordinal)
                        ? WorkTabEffectiveStateResolution<WorkloadSettingValue>.Set(
                            WorkloadSettingValue.Global(globalSetting))
                        : WorkTabEffectiveStateResolution<WorkloadSettingValue>.NoOpinion;
                },
                SpecificJobPriorityV2 = delegate(WorkloadSpecificJobTargetKey key)
                {
                    return key != null && key.IsGlobal
                        ? WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.Set(globalPriority)
                        : WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload>.NoOpinion;
                },
                WorkTypeOrderV2 = delegate(WorkloadWorkTypeOrderKey key)
                {
                    return key != null && key.IsGlobal
                        ? WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.Set(globalOrder)
                        : WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload>.NoOpinion;
                },
                ScheduleV2 = delegate(WorkloadScheduleTargetKey key)
                {
                    return key != null && key.Equals(scheduleKey)
                        ? WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.Set(schedule)
                        : WorkTabEffectiveStateResolution<WorkloadSchedulePayload>.NoOpinion;
                }
            };
            return new LiveWorkTabEffectiveStateProvider(callbacks);
        }

        public static WorkloadV2PersistenceRecord CurrentRecord(string id = "night-shift")
        {
            return new WorkloadV2PersistenceRecord
            {
                StableId = id,
                Label = "Night Shift",
                SchemaVersion = WorkloadSchema.CurrentVersion,
                OwnershipDimensions = (int)WorkloadOwnershipDimensions.All,
                ScopeMode = (int)WorkloadScopeMode.ExplicitPawnIds,
                ExplicitPawnIds = new List<string> { "p1", "p2" }
            };
        }

        public static WorkloadTransactionRevisionVector RevisionVector(
            string sourceTemplateFingerprint = "template-1",
            long taxonomyRevision = 1,
            string taxonomyFingerprint = "taxonomy-1")
        {
            return new WorkloadTransactionRevisionVector(
                1L,
                2L,
                3L,
                4L,
                5L,
                6L,
                7L,
                8L,
                "bwt",
                "host-1",
                "roster-1",
                sourceTemplateFingerprint,
                taxonomyRevision,
                taxonomyFingerprint);
        }

        public static WorkloadTransactionRequest Request(
            WorkloadTransactionOperation operation = WorkloadTransactionOperation.Apply,
            string requestId = "request-1",
            string idempotencyKey = "idempotency-1",
            string targetId = "night-shift",
            string requesterPlayerKey = "player-1",
            params string[] participantKeys)
        {
            WorkloadTransactionPayload payload;
            string diagnostic;
            TestAssert.True(
                WorkloadTransactionPayload.TryCreate(
                    new byte[] { 0x10, 0x20, 0x30 },
                    null,
                    out payload,
                    out diagnostic),
                "test payload should be accepted: " + diagnostic);
            WorkloadTransactionRequest request;
            TestAssert.True(
                WorkloadTransactionRequest.TryCreateCanonical(
                    operation,
                    requestId,
                    idempotencyKey,
                    "session-1",
                    "night-shift",
                    operation == WorkloadTransactionOperation.Apply ? targetId : targetId,
                    requesterPlayerKey,
                    100,
                    payload,
                    RevisionVector(),
                    participantKeys == null || participantKeys.Length == 0
                        ? new[] { "peer-a", "player-1" }
                        : participantKeys,
                    out request,
                    out diagnostic),
                "test request should be accepted: " + diagnostic);
            return request;
        }

        public static T Find<T>(IEnumerable<T> values, Func<T, bool> predicate)
        {
            return values == null ? default(T) : values.FirstOrDefault(predicate);
        }
    }
}
