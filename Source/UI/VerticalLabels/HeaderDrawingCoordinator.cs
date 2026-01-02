using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Central coordinator for header rendering.
    /// Manages the lifecycle of the layout solver and renderer selection.
    /// 
    /// Responsibilities:
    /// 1. Ensure layout is solved once per frame (before rendering)
    /// 2. Return the active renderer (angled or vanilla)
    /// 3. Handle cache invalidation when needed
    /// </summary>
    public static class HeaderDrawingCoordinator
    {
        private static AngledHeaderRenderer _angledRenderer;
        private static VanillaHeaderRenderer _vanillaRenderer;
        private static VanillaHeaderLayoutSolver _vanillaSolver;

        static HeaderDrawingCoordinator()
        {
            _vanillaSolver = new VanillaHeaderLayoutSolver();
            _angledRenderer = new AngledHeaderRenderer();
            _vanillaRenderer = new VanillaHeaderRenderer(_vanillaSolver);
        }

        /// <summary>
        /// Call this once per frame BEFORE header rendering begins.
        /// Ensures the layout solver has solved for the current frame.
        /// </summary>
        public static void EnsureLayoutSolved(PawnTable table)
        {
            if (table == null) return;
            
            // Only solve for vanilla mode; angled headers don't need this
            if (!BetterWorkTabMod.Settings.enableAngledHeaders)
            {
                _vanillaSolver.SolveLayout(table);
            }
        }

        /// <summary>
        /// Returns the active renderer based on settings.
        /// </summary>
        public static IHeaderRenderer GetActiveRenderer()
        {
            return BetterWorkTabMod.Settings.enableAngledHeaders
                ? (IHeaderRenderer)_angledRenderer
                : (IHeaderRenderer)_vanillaRenderer;
        }

        /// <summary>
        /// Called when user resets columns to vanilla order or changes angled header setting.
        /// Clears all caches to force rebuild.
        /// </summary>
        public static void InvalidateCaches()
        {
            _vanillaSolver = new VanillaHeaderLayoutSolver();
            _vanillaRenderer = new VanillaHeaderRenderer(_vanillaSolver);
            AngledHeaderCache.ClearCache();
        }

        public static void NotifyAngledHeadersChanged()
        {
            InvalidateCaches();
        }
    }
}
