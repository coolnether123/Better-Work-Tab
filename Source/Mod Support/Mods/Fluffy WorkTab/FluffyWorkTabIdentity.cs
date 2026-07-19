using System;
using System.Collections.Generic;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    /// <summary>Canonical identifiers used by supported Fluffy Work Tab releases and forks.</summary>
    internal static class FluffyWorkTabIdentity
    {
        private static readonly HashSet<string> KnownPackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "fluffy.worktab",
                "fluffy.worktab.continued",
                "arof.fluffy.worktab.continued"
            };

        internal static bool IsKnownPackageId(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) &&
                KnownPackageIds.Contains(packageId.Trim());
        }
    }
}
