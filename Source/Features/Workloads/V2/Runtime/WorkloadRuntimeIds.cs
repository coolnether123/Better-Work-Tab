using System.Globalization;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    /// <summary>Converts live game objects to stable workload identities.</summary>
    public static class WorkTabEffectiveStateIds
    {
        public static PawnKey ForPawn(Pawn pawn) =>
            new PawnKey(pawn == null || pawn.thingIDNumber <= 0
                ? null
                : pawn.thingIDNumber.ToString(CultureInfo.InvariantCulture));

        public static WorkTypeKey ForWorkType(WorkTypeDef workType) =>
            new WorkTypeKey(workType?.defName);

        public static WorkGiverKey ForWorkGiver(WorkGiverDef workGiver) =>
            new WorkGiverKey(workGiver?.defName);

        public static WorkloadSpecificJobKey ForSpecificJob(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver) =>
            new WorkloadSpecificJobKey(ForPawn(pawn), ForWorkType(workType), ForWorkGiver(workGiver));

        public static WorkloadSpecificJobTargetKey ForSpecificJobTarget(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver) =>
            WorkloadSpecificJobTargetKey.ForPawn(
                ForPawn(pawn), ForWorkType(workType), ForWorkGiver(workGiver));

        public static WorkloadSpecificJobTargetKey ForGlobalSpecificJobTarget(
            WorkTypeDef workType,
            WorkGiverDef workGiver) =>
            WorkloadSpecificJobTargetKey.Global(ForWorkType(workType), ForWorkGiver(workGiver));

        public static WorkloadWorkTypeOrderKey ForWorkTypeOrder(Pawn pawn, WorkTypeDef workType) =>
            WorkloadWorkTypeOrderKey.ForPawn(ForPawn(pawn), ForWorkType(workType));

        public static WorkloadWorkTypeOrderKey ForGlobalWorkTypeOrder(WorkTypeDef workType) =>
            WorkloadWorkTypeOrderKey.Global(ForWorkType(workType));

        public static WorkloadScheduleTargetKey ForSchedule(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiver = null) =>
            workGiver == null
                ? WorkloadScheduleTargetKey.ForParent(ForPawn(pawn), ForWorkType(workType))
                : WorkloadScheduleTargetKey.ForWorkGiver(
                    ForPawn(pawn), ForWorkType(workType), ForWorkGiver(workGiver));

        public static WorkloadScheduleTargetKey ForGlobalSchedule(
            WorkTypeDef workType,
            WorkGiverDef workGiver) =>
            WorkloadScheduleTargetKey.GlobalWorkGiver(ForWorkType(workType), ForWorkGiver(workGiver));
    }
}
