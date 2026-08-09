using System;
using System.Collections.Generic;
using Better_Work_Tab.API;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.Features.TimePriority;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities
{
    /// <summary>
    /// Exposes Sleek's shared parent priorities and per-job sidecar to BWT's authority broker.
    /// </summary>
    internal sealed class SleekWorkTabExternalStore :
        IExternalWorkTabStore,
        IExternalWorkTabHandoffImporter
    {
        public string StoreId => SleekWorkTabIdentity.ProviderId;
        public string DisplayName => SleekWorkTabIdentity.DisplayName;
        public bool IsAvailable => SleekWorkTabGateway.IsPresent;
        public int SortOrder => 65;
        public bool IsMirroringSuspended => false;
        public bool MirrorsTimePrioritySchedules => false;

        public ExternalWorkTabPriorityAuthority PriorityAuthority =>
            IsAvailable && SleekWorkTabGateway.SleekOwnsWorkTab
                ? ExternalWorkTabPriorityAuthority.ExternalStore
                : ExternalWorkTabPriorityAuthority.NoOpinion;

        public IDisposable SuspendMirroring()
        {
            // Sleek's per-job setter intentionally refuses writes while another Work-tab owner is
            // active. Parent priorities are shared in Pawn_WorkSettings, so no mirror scope is needed.
            return null;
        }

        public void PushWorkType(Pawn pawn, WorkTypeDef workType)
        {
        }

        public void PushWorkGiver(Pawn pawn, WorkGiverDef workGiver)
        {
        }

        public void PushWorkTypeForAllPawns(WorkTypeDef workType)
        {
        }

        public void PushWorkGiverForAllPawns(WorkGiverDef workGiver)
        {
        }

        public int PushAllPawns()
        {
            return 0;
        }

        public IEnumerable<WorkGiverDef> GetWorkGiversAffectedByWorkTypeCascade(WorkTypeDef workType)
        {
            if (workType?.workGiversByPriority == null)
            {
                yield break;
            }

            for (int i = 0; i < workType.workGiversByPriority.Count; i++)
            {
                WorkGiverDef workGiver = workType.workGiversByPriority[i];
                if (workGiver != null)
                {
                    yield return workGiver;
                }
            }
        }

        public bool TryGetWorkTypePriority(
            Pawn pawn,
            WorkTypeDef workType,
            int hour,
            out int priority)
        {
            priority = 0;
            if (!SleekWorkTabGateway.SleekOwnsWorkTab)
            {
                return false;
            }

            priority = SleekWorkTabGateway.GetSharedWorkTypePriority(pawn, workType);
            return pawn?.workSettings != null && workType != null;
        }

        public bool TryGetWorkGiverPriority(
            Pawn pawn,
            WorkGiverDef workGiver,
            int hour,
            out int priority)
        {
            priority = 0;
            if (!SleekWorkTabGateway.SleekOwnsWorkTab || pawn?.workSettings == null || workGiver == null)
            {
                return false;
            }

            int overridePriority;
            if (SleekWorkTabGateway.TryGetSleekWorkGiverOverride(
                    pawn,
                    workGiver,
                    out overridePriority) &&
                overridePriority >= 0)
            {
                priority = overridePriority;
                return true;
            }

            priority = SleekWorkTabGateway.GetSharedWorkTypePriority(pawn, workGiver.workType);
            return true;
        }

        public int ImportToBetterWorkTab() => SleekWorkTabGateway.ImportChildRanksToBetterWorkTab();
    }
}
