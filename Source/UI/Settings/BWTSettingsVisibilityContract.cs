using System;
using System.Collections.Generic;

namespace Better_Work_Tab.UI.Settings
{
    internal enum V10SettingDisposition
    {
        NotPublic,
        PublicReachable,
        IntentionalInternal
    }

    internal static class BWTSettingsVisibilityContract
    {
        // Compatibility obligations are explicit. Prefixes are discovery hints, not evidence.
        private static readonly HashSet<string> PublicV10Ids = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            SettingIDs.OverlayNumbersMode
        };

        private static readonly HashSet<string> IntentionalInternalV10Ids = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            SettingIDs.RuleBuilder2DraftSuggestions,
            SettingIDs.AutoassignConfirm,
            SettingIDs.AutoassignResetBefore,
            SettingIDs.AutoassignVisual,
            SettingIDs.WorkloadsPersistDividers
        };

        internal static V10SettingDisposition Classify(string settingId)
        {
            if (PublicV10Ids.Contains(settingId))
            {
                return V10SettingDisposition.PublicReachable;
            }

            if (IntentionalInternalV10Ids.Contains(settingId))
            {
                return V10SettingDisposition.IntentionalInternal;
            }

            return V10SettingDisposition.NotPublic;
        }

        internal static bool IsIntentionalInternal(string settingId)
        {
            return IntentionalInternalV10Ids.Contains(settingId);
        }

        internal static bool IsPublicV10(string settingId)
        {
            return PublicV10Ids.Contains(settingId);
        }
    }
}
