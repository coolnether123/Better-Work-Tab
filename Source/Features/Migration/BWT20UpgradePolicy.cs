using System;
using System.Collections.Generic;

namespace Better_Work_Tab.Features.Migration
{
    internal enum BWTWorldSchemaState
    {
        Missing = 0,
        KnownOld = 1,
        Current = 2,
        Newer = 3,
        Unknown = 4
    }

    /// <summary>
    /// Version and persisted-key decisions for the one-time 1.x to 2.0 settings migration.
    /// Kept free of RimWorld APIs so the boundary can be covered by the lightweight test suite.
    /// </summary>
    internal static class BWT20UpgradePolicy
    {
        internal const int CurrentSettingsSchemaVersion = 20000;
        internal const int CurrentWorldSchemaVersion = 20000;

        internal static BWTWorldSchemaState ClassifyWorldSchema(int version)
        {
            if (version == 0) return BWTWorldSchemaState.Missing;
            if (version < 0) return BWTWorldSchemaState.Unknown;
            if (version < CurrentWorldSchemaVersion) return BWTWorldSchemaState.KnownOld;
            if (version > CurrentWorldSchemaVersion) return BWTWorldSchemaState.Newer;
            return BWTWorldSchemaState.Current;
        }

        internal static string DescribeWorldSchema(BWTWorldSchemaState state)
        {
            switch (state)
            {
                case BWTWorldSchemaState.Missing:
                    return "missing";
                case BWTWorldSchemaState.KnownOld:
                    return "known-old";
                case BWTWorldSchemaState.Current:
                    return "current";
                case BWTWorldSchemaState.Newer:
                    return "newer";
                default:
                    return "unknown";
            }
        }

        internal static bool CanPersistWorldSchema(int version)
        {
            BWTWorldSchemaState state = ClassifyWorldSchema(version);
            // Allow only states this build has an intentional compatibility story for. An
            // explicit allow-list keeps a future enum/classification addition fail-closed
            // instead of accidentally entering the normal save path.
            return state == BWTWorldSchemaState.Missing ||
                   state == BWTWorldSchemaState.KnownOld ||
                   state == BWTWorldSchemaState.Current;
        }

        // A non-empty XML document is not enough evidence that it came from
        // public 1.0.5. These markers are additive legacy preferences that
        // were present in that document shape. Keep this list conservative:
        // unknown/private documents must remain unclassified and untouched.
        private static readonly string[] Public105Markers =
        {
            "firstTimeSetupDone",
            "columnDraggingEnabled",
            "enableWorkloads",
            "enableCustomWorkLabels",
            "showPriorityLegend",
            "showDragInstructions",
            "workTabMaxVisiblePawns",
            "enableExtendedPriorities",
            "delegateToExternalPriorityMods",
            "selectedPriorityProviderId"
        };

        private static readonly string[] Known20Markers =
        {
            "settingsSchemaVersion",
            "v2UpgradePromptPending",
            "useRuleBuilder2",
            "enableSubWorkDrilldown",
            "enableFluffyStyleFeatures",
            "enableTimePrioritySchedules",
            "tutorialFlowVersion",
            "priorityMode",
            "useLegacyWorkloads"
        };

        internal static bool NeedsMigration(bool isStartupSettingsLoad, int loadedSchemaVersion)
        {
            return isStartupSettingsLoad &&
                   (loadedSchemaVersion == 0 ||
                    (loadedSchemaVersion > 0 && loadedSchemaVersion < CurrentSettingsSchemaVersion));
        }

        internal static bool WasPersisted(HashSet<string> persistedKeys, string key)
        {
            return persistedKeys != null &&
                   !string.IsNullOrEmpty(key) &&
                   persistedKeys.Contains(key);
        }

        internal static bool IsPublic105SettingsDocument(HashSet<string> persistedKeys)
        {
            return persistedKeys != null &&
                   persistedKeys.Count > 0 &&
                   ContainsAny(persistedKeys, Public105Markers) &&
                   !ContainsAny(persistedKeys, Known20Markers);
        }

        internal static bool IsKnown20SettingsDocument(HashSet<string> persistedKeys)
        {
            return persistedKeys != null && ContainsAny(persistedKeys, Known20Markers);
        }

        internal static bool ShouldOfferUpgrade(
            bool hasLegacySettings,
            int loadedWorldSchemaVersion)
        {
            if (!hasLegacySettings) return false;

            BWTWorldSchemaState state = ClassifyWorldSchema(loadedWorldSchemaVersion);
            return state == BWTWorldSchemaState.Missing ||
                   state == BWTWorldSchemaState.KnownOld;
        }

        private static bool ContainsAny(HashSet<string> persistedKeys, string[] keys)
        {
            if (persistedKeys == null || keys == null) return false;

            for (int i = 0; i < keys.Length; i++)
            {
                if (persistedKeys.Contains(keys[i])) return true;
            }

            return false;
        }
    }
}
