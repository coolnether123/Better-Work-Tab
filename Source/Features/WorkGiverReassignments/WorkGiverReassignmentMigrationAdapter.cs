using Better_Work_Tab.Foundation.GameState;
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
            IWorkTabReassignmentState state)
        {
            if (state == null)
            {
                WorkGiverReassignmentManager.OnSettingsLoaded();
                return;
            }

            WorkGiverReassignmentData current = state.EnsureData();

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            WorkGiverReassignmentData legacy = settings?.LegacyWorkGiverReassignments;
            if (legacy != null && legacy.HasAnyData())
            {
                if (current == null || !current.HasAnyData())
                {
                    state.Data = legacy.Clone();
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
