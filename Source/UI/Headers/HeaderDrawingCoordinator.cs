using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.UI.Headers.Vanilla;
using Better_Work_Tab.UI.Headers.Angled;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Central coordinator for header rendering.
    /// Manages the lifecycle of the layout solver and renderer selection.
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
        /// <param name="table">The pawn table being rendered.</param>
        public static void EnsureLayoutSolved(PawnTable table)
        {
            if (table == null) return;
            
            // Only solve for vanilla mode; angled headers do not require this
            if (!BetterWorkTabMod.Settings.enableAngledHeaders)
            {
                _vanillaSolver.SolveLayout(table);
            }
        }

        /// <summary>
        /// Returns the vanilla solver for collecting header data.
        /// </summary>
        /// <returns>The active VanillaHeaderLayoutSolver instance.</returns>
        public static VanillaHeaderLayoutSolver GetVanillaSolver()
        {
            return _vanillaSolver;
        }

        public static int GetVanillaLayoutVersion()
        {
            return _vanillaSolver?.LayoutVersion ?? 0;
        }

        /// <summary>
        /// Returns the active renderer based on current mod settings.
        /// </summary>
        /// <returns>An implementation of IHeaderRenderer (Angled or Vanilla).</returns>
        public static IHeaderRenderer GetActiveRenderer()
        {
            return BetterWorkTabMod.Settings.enableAngledHeaders
                ? (IHeaderRenderer)_angledRenderer
                : (IHeaderRenderer)_vanillaRenderer;
        }

        /// <summary>
        /// Invalidates only the current solver solution, forcing recalculation on next frame.
        /// Preserves the solver instance and its cached max level to prevent header height jumps.
        /// Use this for column reordering.
        /// </summary>
        public static void InvalidateSolution()
        {
            _vanillaSolver?.InvalidateSolution();
        }

        /// <summary>
        /// Invalidates layout geometry while preserving text measurements that remain valid across animation frames.
        /// </summary>
        public static void InvalidateAnimatedLayout()
        {
            _vanillaSolver?.InvalidateSolution();
            AngledHeaderCache.ClearGeometryCache();
        }

        /// <summary>
        /// Invoked upon column reset to vanilla order or when angled header settings are toggled.
        /// Completely recreates all caches and solvers.
        /// </summary>
        public static void InvalidateCaches()
        {
            // Create new solver (starts with _solutionValid = false, triggering recalculation)
            _vanillaSolver = new VanillaHeaderLayoutSolver();
            _vanillaRenderer = new VanillaHeaderRenderer(_vanillaSolver);
            AngledHeaderCache.ClearCache();
        }

        /// <summary>
        /// Notification that angled header settings (rotation, offset) have changed.
        /// Triggers a cache invalidation.
        /// </summary>
        public static void NotifyAngledHeadersChanged()
        {
            InvalidateCaches();
        }
    }
}
