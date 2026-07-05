using System;
using System.Linq;
using Better_Work_Tab.API;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using HarmonyLib;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal static class BWTTutorialUserContext
    {
        internal static bool HasFluffyWorkTabHistory()
        {
            try
            {
                GameComponent_BWTWorldSettings component =
                    Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                if (component != null && component.FluffyWorkTabPriorityMigrationVersion > 0)
                {
                    return true;
                }

                return AccessTools.TypeByName("WorkTab.PriorityManager") != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool HasExternalPriorityProvider()
        {
            try
            {
                return PriorityProviderRegistry.GetAvailableProviders().Any(IsExternalProvider);
            }
            catch
            {
                return false;
            }
        }

        internal static bool PriorityModeIsNotBetterWorkTab()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null && settings.priorityMode != PriorityMode.BetterWorkTab;
        }

        internal static string BuildPriorityTutorialBody()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            int configuredMax = settings?.maxPriorityInt ?? DefaultSettings.maxPriority;
            if (HasExternalPriorityProvider() && PriorityModeIsNotBetterWorkTab())
            {
                return "Another priority mod is available, so Better Work Tab is preserving that setup. You can keep that behavior, or let Better Work Tab manage a 1-" + configuredMax + " priority range from here.";
            }

            return "Better Work Tab can use more than vanilla's 1-4 priorities. The 2.0 default is 1-9, and the setting can go up to 99.";
        }

        internal static void UseBetterWorkTabPriorityDefaults()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.maxPriorityInt = Math.Max(settings.maxPriorityInt, 9);
            settings.SetPriorityMode(PriorityMode.BetterWorkTab);
            settings.NormalizePrioritySettings();
            PriorityAuthorityBroker.InvalidateCaches();
            settings.Write();
        }

        private static bool IsExternalProvider(IMaxPriorityProvider provider)
        {
            if (provider == null || string.IsNullOrEmpty((provider.ProviderId ?? string.Empty).Trim()))
            {
                return false;
            }

            return !string.Equals(provider.ProviderId, PriorityConstants.VanillaProviderId, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(provider.ProviderId, PriorityConstants.BwtProviderId, StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(provider.ProviderId, PriorityConstants.AutoProviderId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
