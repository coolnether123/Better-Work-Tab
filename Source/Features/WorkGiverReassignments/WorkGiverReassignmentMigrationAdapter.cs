using Better_Work_Tab.Features.Workloads;
using Verse;

namespace Better_Work_Tab.Features.WorkGiverReassignments
{
    /// <summary>
    /// Reads the legacy settings record and writes the save-facing component.
    /// Ordinary reassignment reads use the per-game persistence port.
    /// </summary>
    internal static class WorkGiverReassignmentMigrationAdapter
    {
        internal static void MigrateLegacySettingsDataIfNeeded(
            GameComponent_BWTWorldSettings component)
        {
            if (component == null)
            {
                WorkGiverReassignmentManager.OnSettingsLoaded();
                return;
            }

            component.EnsureWorkGiverReassignmentData();

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            WorkGiverReassignmentData legacy = settings?.LegacyWorkGiverReassignments;
            if (legacy != null && legacy.HasAnyData())
            {
                if (!component.WorkGiverReassignments.HasAnyData())
                {
                    component.WorkGiverReassignments = legacy.Clone();
                    BetterWorkTabMod.DebugLog(
                        "Migrated legacy global sub-work reassignment settings into this save.",
                        DebugFeature.General);
                }
                else
                {
                    BetterWorkTabMod.DebugLog(
                        "Ignored legacy global sub-work reassignment settings because this save already has sub-work data.",
                        DebugFeature.General);
                }

                settings.LegacyWorkGiverReassignments = null;
                LongEventHandler.ExecuteWhenFinished(settings.Write);
            }

            WorkGiverReassignmentManager.OnWorldDataLoaded();
        }
    }
}
