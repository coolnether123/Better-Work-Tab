using Better_Work_Tab.Features.Workloads;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>
    /// Single crossing point for Better Work Tab code that needs to know about
    /// Fluffy Work Tab. Keep package IDs, external type names, texture paths, and
    /// save-shape details behind this facade.
    /// </summary>
    internal static class FluffyWorkTabGateway
    {
        internal static bool IsPresent => FluffyWorkTabCoexistence.IsFluffyWorkTabPresent;

        internal static string DetectedPackageId => FluffyWorkTabCoexistence.DetectedPackageId;

        internal static bool BetterWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.BetterWorkTabOwnsWorkTab;

        internal static bool ExternalWorkTabOwnsWorkTab => FluffyWorkTabCoexistence.FluffyOwnsWorkTab;

        internal static bool ShouldRunBetterWorkTabFeatures => FluffyWorkTabCoexistence.ShouldRunBetterWorkTabFeatures;

        internal static bool HasRightExpandingDrilldown => IsPresent;

        internal static bool HasScheduleStrip => IsPresent;

        internal static bool HasIconSet => IsPresent;

        internal static string ColumnVisibilitySettingLabel => "Show Fluffy Work Tab columns";

        internal static string ColumnVisibilitySettingTooltip =>
            "When Fluffy Work Tab is loaded and Better Work Tab owns the Work tab, keep Fluffy's Mood, Job, Detailed Copy/Paste, and Favourite columns visible.";

        internal static bool IsKnownPackageId(string packageId)
        {
            return FluffyWorkTabCoexistence.IsKnownFluffyPackageId(packageId);
        }

        internal static void ApplyDesiredOwner(bool reopenIfOpen = false)
        {
            FluffyWorkTabCoexistence.ApplyDesiredOwner(reopenIfOpen);
        }

        internal static void SwitchToBetterWorkTab()
        {
            FluffyWorkTabCoexistence.SwitchToBetterWorkTab();
        }

        internal static void SwitchToExternalWorkTab()
        {
            FluffyWorkTabCoexistence.SwitchToFluffyWorkTab();
        }

        internal static void ApplyColumnVisibility()
        {
            FluffyWorkTabCoexistence.ApplyColumnVisibility();
        }

        internal static void MigratePriorityDataIfNeeded(GameComponent_BWTWorldSettings component)
        {
            FluffyWorkTabMigration.MigrateIfNeeded(component);
        }

        internal static bool HasPriorityMigrationHistory(GameComponent_BWTWorldSettings component)
        {
            return FluffyWorkTabMigration.HasMigrationHistory(component);
        }

        internal static void ExposePriorityMigrationVersion(ref int version)
        {
            FluffyWorkTabMigration.ExposeMigrationVersion(ref version);
        }

        internal static bool TryGetManualPriorityToggleIcon(out Texture2D texture)
        {
            return FluffyWorkTabAssets.TryGetManualPriorityToggleIcon(out texture);
        }

        internal static void DrawWorkTabSwitchButton(Rect inRect)
        {
            FluffyWorkTabCoexistenceUI.DrawWorkTabSwitchButton(inRect);
        }

        internal static void DrawSettingsBannerIfNeeded(ref Rect inRect)
        {
            FluffyWorkTabCoexistenceUI.DrawSettingsBannerIfNeeded(ref inRect);
        }
    }
}
