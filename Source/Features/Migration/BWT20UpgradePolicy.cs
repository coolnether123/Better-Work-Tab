using System.Collections.Generic;

namespace Better_Work_Tab.Features.Migration
{
    /// <summary>
    /// Version and persisted-key decisions for the one-time 1.x to 2.0 settings migration.
    /// Kept free of RimWorld APIs so the boundary can be covered by the lightweight test suite.
    /// </summary>
    internal static class BWT20UpgradePolicy
    {
        internal const int CurrentSettingsSchemaVersion = 20000;
        internal const int CurrentWorldSchemaVersion = 20000;

        internal static bool NeedsMigration(bool isStartupSettingsLoad, int loadedSchemaVersion)
        {
            return isStartupSettingsLoad && loadedSchemaVersion < CurrentSettingsSchemaVersion;
        }

        internal static bool WasPersisted(ISet<string> persistedKeys, string key)
        {
            return persistedKeys != null &&
                   !string.IsNullOrEmpty(key) &&
                   persistedKeys.Contains(key);
        }

        internal static bool ShouldOfferUpgrade(
            bool hasLegacySettings,
            int loadedWorldSchemaVersion)
        {
            return hasLegacySettings &&
                   loadedWorldSchemaVersion < CurrentWorldSchemaVersion;
        }
    }
}
