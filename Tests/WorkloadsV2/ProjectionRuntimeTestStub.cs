namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    // The production runtime bridge is RimWorld-bound. The linked projected
    // provider only needs these invalidation/pass tokens for pure tests.
    internal static class WorkTabEffectiveStateRuntime
    {
        internal static long PreparingRenderPassId => 0L;
        internal static long CurrentRenderPassId => 0L;

        internal static void InvalidateRenderPass()
        {
        }
    }
}

namespace Better_Work_Tab.Features.TimePriority
{
    internal readonly struct TimePriorityTarget
    {
    }
}

namespace RimWorld
{
    public sealed class WorkTypeDef
    {
        public string defName;
    }

    public sealed class WorkGiverDef
    {
        public string defName;
    }
}

namespace Verse
{
    public sealed class Pawn
    {
        public int thingIDNumber;
    }
}

namespace Better_Work_Tab.UI.Workloads.Projection
{
    using System.Collections.Generic;
    using Better_Work_Tab.Features.TimePriority;
    using Better_Work_Tab.Features.Workloads.V2;
    using Better_Work_Tab.UI.WorkGrid.Contracts;
    using Better_Work_Tab.UI.WorkGrid.Projection;

    internal static class WorkloadPreviewStateAdapter
    {
        internal static bool TryGetScheduleKey(
            TimePriorityTarget target,
            out WorkloadScheduleTargetKey key,
            out string reason)
        {
            key = null;
            reason = string.Empty;
            return false;
        }

        internal static bool TryGetSpecificJobKey(
            WorkTabSpecificJobTarget target,
            out WorkloadSpecificJobTargetKey key)
        {
            key = null;
            return false;
        }

        internal static bool TryGetWorkTypeOrderKey(
            WorkTabWorkTypeOrderTarget target,
            out WorkloadWorkTypeOrderKey key)
        {
            key = null;
            return false;
        }

        internal static WorkTabEffectiveStateResolution<TimePriorityScheduleValue>
            ToScheduleResolution(
                WorkTabEffectiveStateResolution<WorkloadSchedulePayload> resolution)
        {
            if (resolution.IsClear)
            {
                return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.Clear;
            }

            if (!resolution.IsSet || resolution.Value == null || !resolution.Value.IsValid)
            {
                return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.NoOpinion;
            }

            return WorkTabEffectiveStateResolution<TimePriorityScheduleValue>.Set(
                new TimePriorityScheduleValue(
                    resolution.Value.Priorities,
                    resolution.Value.PinnedHourMask));
        }

        internal static WorkTabEffectiveStateResolution<int>
            ToSpecificPriorityResolution(
                WorkTabEffectiveStateResolution<WorkloadSpecificPriorityPayload> resolution)
        {
            if (resolution.IsClear)
            {
                return WorkTabEffectiveStateResolution<int>.Clear;
            }

            return resolution.IsSet && resolution.Value.IsValid
                ? WorkTabEffectiveStateResolution<int>.Set(resolution.Value.Priority)
                : WorkTabEffectiveStateResolution<int>.NoOpinion;
        }

        internal static WorkTabEffectiveStateResolution<IReadOnlyList<string>>
            ToWorkTypeOrderResolution(
                WorkTabEffectiveStateResolution<WorkloadWorkTypeOrderPayload> resolution)
        {
            if (resolution.IsClear)
            {
                return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.Clear;
            }

            if (!resolution.IsSet || resolution.Value == null || !resolution.Value.IsValid)
            {
                return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.NoOpinion;
            }

            var names = new List<string>(resolution.Value.OrderedWorkGivers.Count);
            for (int index = 0; index < resolution.Value.OrderedWorkGivers.Count; index++)
            {
                names.Add(resolution.Value.OrderedWorkGivers[index].Value);
            }

            return WorkTabEffectiveStateResolution<IReadOnlyList<string>>.Set(names);
        }

        internal static WorkloadSchedulePayload ToSchedulePayload(
            TimePriorityScheduleValue value) =>
            value == null
                ? null
                : new WorkloadSchedulePayload(value.CopyPriorities(), value.PinnedHourMask);

        internal static WorkloadWorkTypeOrderPayload ToWorkTypeOrderPayload(
            IReadOnlyList<string> orderedWorkGiverNames)
        {
            if (orderedWorkGiverNames == null)
            {
                return null;
            }

            var keys = new List<WorkGiverKey>(orderedWorkGiverNames.Count);
            for (int index = 0; index < orderedWorkGiverNames.Count; index++)
            {
                keys.Add(new WorkGiverKey(orderedWorkGiverNames[index]));
            }

            return new WorkloadWorkTypeOrderPayload(keys);
        }
    }
}
