using System;
using System.Collections.Generic;
using Better_Work_Tab.API;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    internal sealed class FluffyWorkTabExternalStore : IExternalWorkTabStore, IExternalWorkTabPriorityImporter
    {
        public string StoreId => PriorityProviderIntegrationCatalog.FluffyWorkTabProviderId;
        public string DisplayName => PriorityProviderIntegrationCatalog.FluffyWorkTabDisplayName;
        public bool IsAvailable => FluffyWorkTabGateway.IsPresent;
        public int SortOrder => 60;
        public bool IsMirroringSuspended => FluffyWorkTabGateway.PriorityMirroringSuspended;
        public bool MirrorsTimePrioritySchedules => FluffyWorkTabGateway.TimePriorityScheduleMirroringEnabled;

        public ExternalWorkTabPriorityAuthority PriorityAuthority
        {
            get
            {
                if (!IsAvailable)
                {
                    return ExternalWorkTabPriorityAuthority.NoOpinion;
                }

                if (FluffyWorkTabGateway.ExternalWorkTabOwnsWorkTab)
                {
                    return ExternalWorkTabPriorityAuthority.ExternalStore;
                }

                BetterWorkTabSettings.SubWorkDrilldownStyle style =
                    BetterWorkTabMod.Settings?.subWorkDrilldownStyle ??
                    DefaultSettings.subWorkDrilldownStyle;
                return style == BetterWorkTabSettings.SubWorkDrilldownStyle.ExpandBeside
                    ? ExternalWorkTabPriorityAuthority.ExternalStore
                    : ExternalWorkTabPriorityAuthority.BetterWorkTab;
            }
        }

        public IDisposable SuspendMirroring()
        {
            return FluffyWorkTabGateway.SuspendPriorityMirroring();
        }

        public void PushWorkType(Pawn pawn, WorkTypeDef workType)
        {
            FluffyWorkTabGateway.MirrorWorkType(pawn, workType);
        }

        public void PushWorkGiver(Pawn pawn, WorkGiverDef workGiver)
        {
            FluffyWorkTabGateway.MirrorWorkGiver(pawn, workGiver);
        }

        public void PushWorkTypeForAllPawns(WorkTypeDef workType)
        {
            FluffyWorkTabGateway.MirrorWorkTypeForAllPawns(workType);
        }

        public void PushWorkGiverForAllPawns(WorkGiverDef workGiver)
        {
            FluffyWorkTabGateway.MirrorWorkGiverForAllPawns(workGiver);
        }

        public int PushAllPawns()
        {
            return FluffyWorkTabGateway.MirrorAllPawns();
        }

        public IEnumerable<WorkGiverDef> GetWorkGiversAffectedByWorkTypeCascade(WorkTypeDef workType)
        {
            return FluffyWorkTabSync.GetWorkGiversAffectedByWorkTypeCascade(workType);
        }

        public bool TryGetWorkTypePriority(Pawn pawn, WorkTypeDef workType, int hour, out int priority)
        {
            return FluffyWorkTabGateway.TryGetWorkTypePriority(pawn, workType, hour, out priority);
        }

        public bool TryGetWorkGiverPriority(Pawn pawn, WorkGiverDef workGiver, int hour, out int priority)
        {
            return FluffyWorkTabGateway.TryGetWorkGiverPriority(pawn, workGiver, hour, out priority);
        }

        public bool TryReadPriorityRecords(out IReadOnlyList<ExternalPawnWorkGiverPriorityRecord> records)
        {
            return FluffyWorkTabMigration.TryReadLivePriorityRecords(out records);
        }
    }
}
