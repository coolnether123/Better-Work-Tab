using System.Reflection;
using System.Runtime.CompilerServices;
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
        private static readonly ConditionalWeakTable<PawnTable, VanillaTableState> _tableStates =
            new ConditionalWeakTable<PawnTable, VanillaTableState>();
        private static readonly VanillaTableState _fallbackTableState = new VanillaTableState();
        private static VanillaTableState _activeTableState;
        private static PawnTable _activeTable;
        private static VanillaTableState _lastTableState;
        private static WorkTabInvalidationVersion _lastInvalidationVersions;
        private static int _cacheGeneration;
        private static int _solutionInvalidationVersion;

        static HeaderDrawingCoordinator()
        {
            _lastTableState = _fallbackTableState;
            _angledRenderer = new AngledHeaderRenderer();
        }

        /// <summary>
        /// Call this once per frame BEFORE header rendering begins.
        /// Ensures the layout solver has solved for the current frame.
        /// </summary>
        /// <param name="table">The pawn table being rendered.</param>
        public static void EnsureLayoutSolved(PawnTable table)
        {
            if (table == null) return;

            VanillaTableState state = GetTableState(table);
            if (state.DeferredDepth > 0)
            {
                return;
            }
            
            // Only solve for vanilla mode; angled headers do not require this
            if (BetterWorkTabMod.Settings != null && !BetterWorkTabMod.Settings.enableAngledHeaders)
            {
                state.Solver.SolveLayout(table);
            }
        }

        /// <summary>
        /// Defers the vanilla header solve while the BWT-owned Layout pass is
        /// collecting every header. The scope carries the active table context
        /// so a nested PawnTable gets an independent depth and solver.
        /// </summary>
        internal static VanillaSolveScope BeginDeferredVanillaSolve(PawnTable table)
        {
            TableContextScope context = EnterTableContext(table);
            bool deferred = Event.current != null &&
                            Event.current.type == EventType.Layout &&
                            BetterWorkTabMod.Settings != null &&
                            !BetterWorkTabMod.Settings.enableAngledHeaders &&
                            table != null;
            VanillaTableState state = _activeTableState;
            if (deferred)
            {
                state.DeferredDepth++;
            }

            return new VanillaSolveScope(table, state, deferred, context);
        }

        /// <summary>
        /// Completes a deferred BWT-owned Layout pass after all headers have
        /// been collected, restoring the previous table context even when the
        /// pass exits through an exception.
        /// </summary>
        internal static void EndDeferredVanillaSolve(VanillaSolveScope scope)
        {
            try
            {
                if (!scope.Deferred || scope.State == null)
                {
                    return;
                }

                if (scope.State.DeferredDepth > 0)
                {
                    scope.State.DeferredDepth--;
                }

                if (scope.State.DeferredDepth == 0)
                {
                    EnsureLayoutSolved(scope.Table);
                }
            }
            finally
            {
                ExitTableContext(scope.Context);
            }
        }

        /// <summary>
        /// Returns the vanilla solver for the active table. This no-argument
        /// form remains for existing geometry consumers; header-owned call
        /// paths use the table overload below.
        /// </summary>
        /// <returns>The active VanillaHeaderLayoutSolver instance.</returns>
        public static VanillaHeaderLayoutSolver GetVanillaSolver()
        {
            return GetCurrentTableState().Solver;
        }

        public static VanillaHeaderLayoutSolver GetVanillaSolver(PawnTable table)
        {
            return GetTableState(table).Solver;
        }

        public static int GetVanillaLayoutVersion()
        {
            return GetCurrentTableState().Solver.LayoutVersion;
        }

        /// <summary>
        /// Returns the active renderer based on current mod settings.
        /// </summary>
        /// <returns>An implementation of IHeaderRenderer (Angled or Vanilla).</returns>
        public static IHeaderRenderer GetActiveRenderer()
        {
            return BetterWorkTabMod.Settings != null && BetterWorkTabMod.Settings.enableAngledHeaders
                ? (IHeaderRenderer)_angledRenderer
                : (IHeaderRenderer)GetCurrentTableState().Renderer;
        }

        public static IHeaderRenderer GetActiveRenderer(PawnTable table)
        {
            return BetterWorkTabMod.Settings != null && BetterWorkTabMod.Settings.enableAngledHeaders
                ? (IHeaderRenderer)_angledRenderer
                : (IHeaderRenderer)GetTableState(table).Renderer;
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

            TableContextScope context = EnterTableContext(table);
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
            finally
            {
                ExitTableContext(context);
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
            _solutionInvalidationVersion++;
            RefreshVisibleTableStates();
        }

        /// <summary>
        /// Invalidates layout geometry while preserving text measurements that remain valid across animation frames.
        /// </summary>
        public static void InvalidateAnimatedLayout()
        {
            InvalidateSolution();
            AngledHeaderCache.ClearGeometryCache();
        }

        /// <summary>
        /// Invoked upon column reset to vanilla order or when angled header settings are toggled.
        /// Completely recreates all caches and solvers.
        /// </summary>
        public static void InvalidateCaches()
        {
            _cacheGeneration++;
            RefreshVisibleTableStates();
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

        private static VanillaTableState GetCurrentTableState()
        {
            VanillaTableState state = _activeTableState ?? _lastTableState ?? _fallbackTableState;
            state.Refresh(_cacheGeneration, _solutionInvalidationVersion);
            return state;
        }

        private static VanillaTableState GetTableState(PawnTable table)
        {
            if (table == null)
            {
                return GetCurrentTableState();
            }

            if (!ReferenceEquals(table, _activeTable) || _activeTableState == null)
            {
                if (!_tableStates.TryGetValue(table, out VanillaTableState state))
                {
                    state = new VanillaTableState();
                    _tableStates.Add(table, state);
                }

                state.Refresh(_cacheGeneration, _solutionInvalidationVersion);
                _lastTableState = state;
                return state;
            }

            _activeTableState.Refresh(_cacheGeneration, _solutionInvalidationVersion);
            _lastTableState = _activeTableState;
            return _activeTableState;
        }

        private static void RefreshVisibleTableStates()
        {
            _fallbackTableState.Refresh(_cacheGeneration, _solutionInvalidationVersion);
            if (_lastTableState != null && !ReferenceEquals(_lastTableState, _fallbackTableState))
            {
                _lastTableState.Refresh(_cacheGeneration, _solutionInvalidationVersion);
            }

            if (_activeTableState != null &&
                !ReferenceEquals(_activeTableState, _lastTableState) &&
                !ReferenceEquals(_activeTableState, _fallbackTableState))
            {
                _activeTableState.Refresh(_cacheGeneration, _solutionInvalidationVersion);
            }
        }

        private static TableContextScope EnterTableContext(PawnTable table)
        {
            TableContextScope scope = new TableContextScope(_activeTable, _activeTableState);
            VanillaTableState state = table == null ? null : GetTableState(table);
            _activeTable = table;
            _activeTableState = state;
            return scope;
        }

        private static void ExitTableContext(TableContextScope scope)
        {
            _activeTable = scope.PreviousTable;
            _activeTableState = scope.PreviousState;
        }

        internal sealed class VanillaTableState
        {
            internal VanillaHeaderLayoutSolver Solver { get; private set; }
            internal VanillaHeaderRenderer Renderer { get; private set; }
            internal int DeferredDepth;

            private int _cacheGeneration = -1;
            private int _solutionInvalidationVersion = -1;

            internal void Refresh(int cacheGeneration, int solutionInvalidationVersion)
            {
                if (_cacheGeneration != cacheGeneration)
                {
                    Solver = new VanillaHeaderLayoutSolver();
                    Renderer = new VanillaHeaderRenderer(Solver);
                    _cacheGeneration = cacheGeneration;
                    _solutionInvalidationVersion = solutionInvalidationVersion;
                    return;
                }

                if (_solutionInvalidationVersion != solutionInvalidationVersion)
                {
                    Solver.InvalidateSolution();
                    _solutionInvalidationVersion = solutionInvalidationVersion;
                }
            }
        }

        internal struct TableContextScope
        {
            internal readonly PawnTable PreviousTable;
            internal readonly VanillaTableState PreviousState;

            internal TableContextScope(PawnTable previousTable, VanillaTableState previousState)
            {
                PreviousTable = previousTable;
                PreviousState = previousState;
            }
        }

        internal struct VanillaSolveScope
        {
            internal readonly PawnTable Table;
            internal readonly VanillaTableState State;
            internal readonly bool Deferred;
            internal readonly TableContextScope Context;

            internal VanillaSolveScope(
                PawnTable table,
                VanillaTableState state,
                bool deferred,
                TableContextScope context)
            {
                Table = table;
                State = state;
                Deferred = deferred;
                Context = context;
            }
        }
    }
}
