using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// The bottom bar's own glyphs, resolved once.
    ///
    /// <see cref="ContentFinder{T}"/> logs and searches on every miss, so a
    /// texture that is not there must not be looked up sixty times a second. A
    /// miss also must not remove the control: the icon says which picker this
    /// is, but the name beside it still works without one.
    /// </summary>
    internal static class BWTBottomBarIcons
    {
        private static Texture2D workload;
        private static Texture2D ruleset;
        private static bool resolved;

        internal static Texture2D Workload
        {
            get
            {
                Resolve();
                return workload;
            }
        }

        internal static Texture2D Ruleset
        {
            get
            {
                Resolve();
                return ruleset;
            }
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            workload = ContentFinder<Texture2D>.Get("UI/BWTWorkload", false);
            ruleset = ContentFinder<Texture2D>.Get("UI/BWTRuleset", false);
        }
    }
}
