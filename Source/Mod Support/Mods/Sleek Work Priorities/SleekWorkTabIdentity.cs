using System;

namespace Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities
{
    /// <summary>Stable identifiers for the Sleek Work Priorities Workshop release.</summary>
    internal static class SleekWorkTabIdentity
    {
        internal const string PackageId = "squishyjellyfish.SleekWorkPriorities";
        internal const string ProviderId = "sleek-work-priorities";
        internal const string DisplayName = "Sleek Work Priorities";

        internal static bool IsKnownPackageId(string packageId)
        {
            return !string.IsNullOrEmpty(packageId) &&
                string.Equals(packageId.Trim(), PackageId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
