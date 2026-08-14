using System.Reflection;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WindowSession;
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
        private static WorkTabInvalidationVersion _lastInvalidationVersions;
        private static int _deferredVanillaSolveDepth;

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

            if (_deferredVanillaSolveDepth > 0)
            {
                return;
            }
            
            // Only solve for vanilla mode; angled headers do not require this
            if (!BetterWorkTabMod.Settings.enableAngledHeaders)
            {
                _vanillaSolver.SolveLayout(table);
            }
        }

        /// <summary>
        /// Defers the vanilla header solve while the BWT-owned Layout pass is
        /// collecting every header. The direct Harmony path does not use this
        /// boundary and keeps its existing per-call behavior.
        /// </summary>
        internal static bool BeginDeferredVanillaSolve()
        {
            if (Event.current == null ||
                Event.current.type != EventType.Layout ||
                BetterWorkTabMod.Settings == null ||
                BetterWorkTabMod.Settings.enableAngledHeaders)
            {
                return false;
            }

            _deferredVanillaSolveDepth++;
            return true;
        }

        /// <summary>
        /// Completes a deferred BWT-owned Layout pass after all headers have
        /// been collected, restoring the normal solve behavior even when the
        /// pass exits through an exception.
        /// </summary>
        internal static void EndDeferredVanillaSolve(PawnTable table, bool deferred)
        {
            if (!deferred)
            {
                return;
            }

            _deferredVanillaSolveDepth = Mathf.Max(0, _deferredVanillaSolveDepth - 1);
            if (_deferredVanillaSolveDepth == 0)
            {
                EnsureLayoutSolved(table);
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
        /// Routes a BWT-owned work-priority header through the configured header
        /// controller. Returning false leaves the caller free to use the native
        /// worker as a compatibility fallback.
        /// </summary>
        internal static bool TryHandleWorkPriorityHeader(
            PawnColumnWorker_WorkPriority worker,
            Rect rect,
            PawnTable table)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || worker?.def?.workType == null)
            {
                return false;
            }

            if (SubWorkDrilldownState.IsBlankWorkColumn(worker.def))
            {
                return true;
            }

            try
            {
                HeaderInputController.UpdateCache(Event.current);
                bool allowNative = settings.enableAngledHeaders
                    ? AngledHeaderController.DoHeader(worker, rect, table)
                    : VanillaHeaderController.DoHeader(worker, rect, table);
                return !allowNative;
            }
            catch (System.Exception exception)
            {
                Log.Error("[BWT] WorkPriority header failed: " + exception);
                return false;
            }
        }

        /// <summary>
        /// Applies renderer-neutral invalidation to the header caches. Header
        /// lifecycle belongs here even when the optimized body renderer is active.
        /// </summary>
        internal static void PrepareFrame(WorkTabInvalidationVersion current)
        {
            bool headerTextChanged = current.HeaderText != _lastInvalidationVersions.HeaderText ||
                                     current.RenderResources != _lastInvalidationVersions.RenderResources;
            bool headerGeometryChanged = current.HeaderGeometry != _lastInvalidationVersions.HeaderGeometry ||
                                         current.Columns != _lastInvalidationVersions.Columns ||
                                         current.CategoryRevisions.Animation !=
                                         _lastInvalidationVersions.CategoryRevisions.Animation;

            if (headerTextChanged)
            {
                InvalidateCaches();
            }
            else if (headerGeometryChanged)
            {
                InvalidateAnimatedLayout();
            }

            _lastInvalidationVersions = current;
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
        /// Notification that header settings or header-affecting presentation
        /// settings have changed. The renderer consumes the invalidation on its
        /// next frame; this owner also refreshes the organizer and the active
        /// Work-tab table in the same order as the former window entry point.
        /// </summary>
        public static void NotifyAngledHeadersChanged()
        {
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.HeaderText |
                WorkTabDirtyFlags.HeaderGeometry |
                WorkTabDirtyFlags.RenderResources |
                WorkTabDirtyFlags.WindowSize);
            PawnOrganizerSystem.Instance?.Layout?.InvalidateRowDescriptors();

            if (Find.MainTabsRoot?.OpenTab?.TabWindow is MainTabWindow_PawnTable workTab &&
                workTab.GetType().Assembly == typeof(HeaderDrawingCoordinator).Assembly)
            {
                PawnTable table = WorkTabWindowSessionState.ReadPawnTable(workTab);
                if (table != null)
                {
                    // Mark the table as dirty to force a full recache of heights and widths.
                    MethodInfo setDirtyMethod = typeof(PawnTable).GetMethod(
                        "SetDirty",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (setDirtyMethod != null)
                    {
                        setDirtyMethod.Invoke(table, null);
                    }
                    else
                    {
                        // Fallback if SetDirty is not found (unlikely in vanilla but safe).
                        MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                    }
                }
            }
        }
    }
}
