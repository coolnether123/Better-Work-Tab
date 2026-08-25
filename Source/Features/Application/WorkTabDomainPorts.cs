using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
using Verse;

namespace Better_Work_Tab.Features.Application
{
    internal interface IWorkTabPriorityCapturePort
    {
        int DisabledPriority { get; }
        int DefaultEnabledPriority { get; }
        int Clamp(int priority);
        bool TryCaptureAuthority(out long revision);
        bool IsAuthorityCurrent(long revision);
        long ObservationalAuthorityRevision { get; }
        int ReadStored(Pawn pawn, WorkTypeDef workType);
    }

    internal interface IWorkTabScheduleCapturePort
    {
        int Revision { get; }
        bool TryCapture(
            TimePriorityTarget target,
            int fallbackPriority,
            out TimePriorityLiveScheduleSnapshot snapshot,
            out string reason);
    }

    internal interface IWorkTabSpecificJobCapturePort
    {
        int Revision { get; }
        string StateFingerprint { get; }
        bool RetainsOverridesForDisabledParent { get; }
        WorkTypeDef ResolveWorkType(WorkGiverDef workGiver);
        IReadOnlyList<WorkGiver> GetDisplayWorkGivers(
            WorkTypeDef workType,
            Pawn pawn = null);
        int ReadPriority(Pawn pawn, WorkGiverDef workGiver, int fallbackPriority);
        bool TryReadLocalPriority(Pawn pawn, WorkGiverDef workGiver, out int priority);
        bool HasLocalOrder(Pawn pawn, WorkTypeDef workType);
        bool IsCompleteGlobalOrder(
            string workTypeDefName,
            IReadOnlyList<string> orderedWorkGiverNames);
        bool TryCapturePriority(
            Pawn pawn,
            WorkGiverDef workGiver,
            out WorkTabSpecificPriorityBaseline baseline,
            out int revision,
            out long authorityRevision);
        bool TryCaptureOrder(
            Pawn pawn,
            WorkTypeDef workType,
            out WorkTabSpecificOrderBaseline baseline,
            out int revision,
            out long authorityRevision);
    }

    /// <summary>
    /// Composition boundary for stateless adapters over the live BWT domains.
    /// Consumers depend on the typed ports; concrete storage and authority
    /// managers remain private to these adapters.
    /// </summary>
    internal static class WorkTabDomainPorts
    {
        internal static IWorkTabPriorityCapturePort Priority { get; } =
            new PriorityCapturePort();
        internal static IWorkTabScheduleCapturePort Schedules { get; } =
            new ScheduleCapturePort();
        internal static IWorkTabSpecificJobCapturePort SpecificJobs { get; } =
            new SpecificJobCapturePort();

        private sealed class PriorityCapturePort : IWorkTabPriorityCapturePort
        {
            public int DisabledPriority => WorkPrioritySystem.DisabledPriority;
            public int DefaultEnabledPriority => WorkPrioritySystem.GetDefaultEnabledPriority();
            public int Clamp(int priority) => WorkPrioritySystem.ClampPriority(priority);
            public bool TryCaptureAuthority(out long revision) =>
                WorkPrioritySystem.TryCaptureBwtMutationAuthority(out revision);
            public bool IsAuthorityCurrent(long revision) =>
                WorkPrioritySystem.IsBwtMutationAuthorityCurrent(revision);
            public long ObservationalAuthorityRevision =>
                PriorityAuthorityBroker.GetObservationalAuthorityRevision();
            public int ReadStored(Pawn pawn, WorkTypeDef workType) =>
                pawn?.workSettings == null || workType == null
                    ? DisabledPriority
                    : PriorityAuthorityBroker.GetBetterWorkTabStoredPriority(
                        pawn.workSettings,
                        workType);
        }

        private sealed class ScheduleCapturePort : IWorkTabScheduleCapturePort
        {
            public int Revision => TimePriorityService.CurrentVersion;

            public bool TryCapture(
                TimePriorityTarget target,
                int fallbackPriority,
                out TimePriorityLiveScheduleSnapshot snapshot,
                out string reason) =>
                TimePriorityService.TryCaptureLiveScheduleSnapshot(
                    target,
                    fallbackPriority,
                    out snapshot,
                    out reason);
        }

        private sealed class SpecificJobCapturePort : IWorkTabSpecificJobCapturePort
        {
            public int Revision => WorkGiverReassignmentManager.CurrentSyncVersion;
            public string StateFingerprint =>
                WorkGiverReassignmentManager.CurrentStateFingerprint;
            public bool RetainsOverridesForDisabledParent =>
                WorkGiverReassignmentManager.LockedSubWorkOverridesDisabledParent();

            public WorkTypeDef ResolveWorkType(WorkGiverDef workGiver) =>
                WorkGiverReassignmentManager.GetTargetWorkType(workGiver);

            public IReadOnlyList<WorkGiver> GetDisplayWorkGivers(
                WorkTypeDef workType,
                Pawn pawn = null) =>
                WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(
                    workType,
                    pawn);

            public int ReadPriority(
                Pawn pawn,
                WorkGiverDef workGiver,
                int fallbackPriority) =>
                WorkGiverReassignmentManager.GetWorkGiverPriority(
                    pawn,
                    workGiver,
                    fallbackPriority);

            public bool TryReadLocalPriority(
                Pawn pawn,
                WorkGiverDef workGiver,
                out int priority) =>
                WorkGiverReassignmentManager.TryGetPawnWorkGiverOverride(
                    pawn,
                    workGiver,
                    out priority);

            public bool HasLocalOrder(Pawn pawn, WorkTypeDef workType) =>
                WorkGiverReassignmentManager.HasPawnOrdering(pawn, workType);

            public bool IsCompleteGlobalOrder(
                string workTypeDefName,
                IReadOnlyList<string> orderedWorkGiverNames) =>
                WorkGiverReassignmentManager.IsCompleteGlobalWorkTypeOrder(
                    workTypeDefName,
                    orderedWorkGiverNames);

            public bool TryCapturePriority(
                Pawn pawn,
                WorkGiverDef workGiver,
                out WorkTabSpecificPriorityBaseline baseline,
                out int revision,
                out long authorityRevision)
            {
                baseline = default(WorkTabSpecificPriorityBaseline);
                revision = Revision;
                authorityRevision = WorkTabDomainPorts.Priority.ObservationalAuthorityRevision;
                if (workGiver == null)
                    return false;

                if (pawn == null)
                {
                    WorkGiverReassignmentManager.GlobalWorkGiverPrioritySnapshot snapshot =
                        WorkGiverReassignmentManager.CaptureGlobalWorkGiverPrioritySnapshot(
                            workGiver.defName);
                    revision = snapshot.SyncVersion;
                    authorityRevision = snapshot.AuthorityRevision;
                    baseline = new WorkTabSpecificPriorityBaseline(
                        ToPriorityState(snapshot.State),
                        snapshot.Priority);
                    return true;
                }

                if (!WorkGiverReassignmentManager.TryCapturePawnWorkGiverPriority(
                        pawn,
                        workGiver,
                        out revision,
                        out bool hasOverride,
                        out int priority,
                        out authorityRevision))
                    return false;
                baseline = new WorkTabSpecificPriorityBaseline(
                    hasOverride
                        ? WorkTabSpecificPriorityState.LocalSet
                        : WorkTabSpecificPriorityState.LocalInherit,
                    priority);
                return true;
            }

            public bool TryCaptureOrder(
                Pawn pawn,
                WorkTypeDef workType,
                out WorkTabSpecificOrderBaseline baseline,
                out int revision,
                out long authorityRevision)
            {
                baseline = default(WorkTabSpecificOrderBaseline);
                revision = Revision;
                authorityRevision = WorkTabDomainPorts.Priority.ObservationalAuthorityRevision;
                if (workType == null)
                    return false;

                if (pawn == null)
                {
                    WorkGiverReassignmentManager.GlobalWorkTypeOrderSnapshot snapshot =
                        WorkGiverReassignmentManager.CaptureGlobalWorkTypeOrderSnapshot(
                            workType.defName);
                    revision = snapshot.SyncVersion;
                    authorityRevision = snapshot.AuthorityRevision;
                    baseline = new WorkTabSpecificOrderBaseline(
                        ToOrderState(snapshot.State),
                        snapshot.OrderedWorkGiverNames);
                    return true;
                }

                WorkGiverReassignmentManager.PawnWorkGiverOrderSnapshot local =
                    WorkGiverReassignmentManager.CapturePawnWorkGiverOrderSnapshot(
                        pawn,
                        workType);
                if (revision != Revision ||
                    authorityRevision !=
                        WorkTabDomainPorts.Priority.ObservationalAuthorityRevision)
                    return false;
                baseline = new WorkTabSpecificOrderBaseline(
                    local?.HasStoredOrder == true
                        ? WorkTabSpecificOrderState.LocalStored
                        : WorkTabSpecificOrderState.LocalInherit,
                    local?.OrderedWorkGiverNames);
                return true;
            }

            private static WorkTabSpecificPriorityState ToPriorityState(
                WorkGiverReassignmentManager.ExactGlobalStateKind state)
            {
                switch (state)
                {
                    case WorkGiverReassignmentManager.ExactGlobalStateKind.Set:
                        return WorkTabSpecificPriorityState.GlobalSet;
                    case WorkGiverReassignmentManager.ExactGlobalStateKind.Clear:
                        return WorkTabSpecificPriorityState.GlobalClear;
                    default:
                        return WorkTabSpecificPriorityState.GlobalAbsent;
                }
            }

            private static WorkTabSpecificOrderState ToOrderState(
                WorkGiverReassignmentManager.ExactGlobalStateKind state)
            {
                switch (state)
                {
                    case WorkGiverReassignmentManager.ExactGlobalStateKind.Set:
                        return WorkTabSpecificOrderState.GlobalSet;
                    case WorkGiverReassignmentManager.ExactGlobalStateKind.Clear:
                        return WorkTabSpecificOrderState.GlobalClear;
                    default:
                        return WorkTabSpecificOrderState.GlobalAbsent;
                }
            }
        }
    }
}
