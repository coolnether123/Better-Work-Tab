using System.Reflection;
using Better_Work_Tab.Foundation.GameState;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WindowSession;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.UI.Headers.Vanilla;
using Better_Work_Tab.UI.Headers.Angled;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Prepared header inputs cached across Unity main-thread frames by the
    /// neutral presentation revision and header invalidation generation.
    /// Geometry, input, animation, and cache-miss text resolution remain live.
    /// </summary>
    internal readonly struct HeaderPresentationPacket
    {
        internal HeaderPresentationPacket(
            bool angledHeadersEnabled,
            IHeaderRenderer activeRenderer,
            float rotation,
            float rotationCos,
            float rotationSin,
            float effectiveHorizontalOffset,
            bool showMovedMarker,
            bool showMovedColorTint,
            bool removeUnderline,
            bool useVerticalStackingForCjk,
            Color angledColor,
            Color underlineColor,
            Color movedMarkerColor)
        {
            AngledHeadersEnabled = angledHeadersEnabled;
            ActiveRenderer = activeRenderer;
            Rotation = rotation;
            RotationCos = rotationCos;
            RotationSin = rotationSin;
            EffectiveHorizontalOffset = effectiveHorizontalOffset;
            ShowMovedMarker = showMovedMarker;
            ShowMovedColorTint = showMovedColorTint;
            RemoveUnderline = removeUnderline;
            UseVerticalStackingForCjk = useVerticalStackingForCjk;
            AngledColor = angledColor;
            UnderlineColor = underlineColor;
            MovedMarkerColor = movedMarkerColor;
        }

        internal bool AngledHeadersEnabled { get; }
        internal IHeaderRenderer ActiveRenderer { get; }
        internal float Rotation { get; }
        internal float RotationCos { get; }
        internal float RotationSin { get; }
        internal float EffectiveHorizontalOffset { get; }
        internal bool ShowMovedMarker { get; }
        internal bool ShowMovedColorTint { get; }
        internal bool RemoveUnderline { get; }
        internal bool UseVerticalStackingForCjk { get; }
        internal Color AngledColor { get; }
        internal Color UnderlineColor { get; }
        internal Color MovedMarkerColor { get; }

        internal Color VanillaStemColor =>
            Approximately(UnderlineColor, DefaultSettings.Color_HeaderUnderline)
                ? HeaderUtility.Colors.DefaultVanillaStemColor
                : UnderlineColor;

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Approximately(left.r, right.r) &&
                   Mathf.Approximately(left.g, right.g) &&
                   Mathf.Approximately(left.b, right.b) &&
                   Mathf.Approximately(left.a, right.a);
        }
    }

    internal interface IHeaderPresentationRenderer
    {
        void DrawHeader(
            Angled.AngledLabelDrawer.AngledLabelLayout layout,
            bool isMouseOver,
            bool isSorted,
            bool sortDescending,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation);
    }

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
        private static IHeaderRenderer _activeRenderer;
        private static bool _activeRendererUsesAngledHeaders;
        private static long _activeRendererSettingsRevision = long.MinValue;
        private static int _activeRendererPresentationVersion = int.MinValue;
        private static int _headerPresentationVersion;
        private static HeaderPresentationPacket _presentationPacket;
        private static long _presentationPacketSettingsRevision = long.MinValue;
        private static int _presentationPacketVersion = int.MinValue;

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
            
            // Only solve for vanilla mode; angled headers do not require this.
            // The mode is stable until the presentation boundary invalidates it.
            if (!AreAngledHeadersEnabled())
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
            EnsureActiveRenderer();
            return _activeRenderer;
        }

        /// <summary>
        /// Captures the pass packet. Rotation, offset, CJK mode, colors, and
        /// renderer choice are prepared inputs; legacy entry points use this
        /// same cache for compatibility. No surface or input state is retained.
        /// </summary>
        internal static HeaderPresentationPacket CapturePresentation()
        {
            EnsureActiveRenderer();
            long settingsRevision = WorkTabPresentationRevision.Current;
            int presentationVersion = _headerPresentationVersion;
            if (_presentationPacketSettingsRevision == settingsRevision &&
                _presentationPacketVersion == presentationVersion)
            {
                return _presentationPacket;
            }

            bool angledHeadersEnabled = _activeRendererUsesAngledHeaders;
            float rotation = angledHeadersEnabled
                ? BWTWorkTabEffectiveSettings.GetInt(SettingIDs.HeadersAngleRotation)
                : Angled.AngledLabelDrawer.DefaultRotationAngle;
            float rotationRadians = rotation * Mathf.Deg2Rad;
            bool removeUnderline = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.DragdropRemoveHeaderUnderline);
            _presentationPacket = new HeaderPresentationPacket(
                angledHeadersEnabled,
                _activeRenderer,
                rotation,
                Mathf.Cos(rotationRadians),
                Mathf.Sin(rotationRadians),
                Mathf.Abs(rotation + 90f) < 0.1f
                    ? 0f
                    : BWTWorkTabEffectiveSettings.GetInt("headers.horizontalOffset"),
                BWTWorkTabEffectiveSettings.GetBool(SettingIDs.ColumnsShowMovedIndicator),
                BWTWorkTabEffectiveSettings.GetBool("columns.showMovedColorTint"),
                removeUnderline,
                BWTWorkTabEffectiveSettings.GetBool(SettingIDs.HeadersUseVerticalStackingForCJK),
                BWTWorkTabEffectiveSettings.GetColor("headers.angledColor"),
                BWTWorkTabEffectiveSettings.GetColor(SettingIDs.HeadersUnderlineColor),
                BWTWorkTabEffectiveSettings.GetColor("columns.movedMarkerColor"));
            _presentationPacketSettingsRevision = settingsRevision;
            _presentationPacketVersion = presentationVersion;
            return _presentationPacket;
        }

        internal static void DrawHeader(
            IHeaderRenderer renderer,
            Angled.AngledLabelDrawer.AngledLabelLayout layout,
            bool isMouseOver,
            bool isSorted,
            bool sortDescending,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation)
        {
            if (renderer is IHeaderPresentationRenderer preparedRenderer)
            {
                preparedRenderer.DrawHeader(
                    layout,
                    isMouseOver,
                    isSorted,
                    sortDescending,
                    headerRect,
                    column,
                    showMarker,
                    in presentation);
                return;
            }

            // Preserve compatibility with an external renderer that only
            // implements the original interface contract.
            renderer?.DrawHeader(
                layout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column,
                showMarker);
        }

        /// <summary>
        /// Returns the current header mode without re-reading the prepared
        /// settings projection for every column in a frame.
        /// </summary>
        internal static bool AreAngledHeadersEnabled()
        {
            EnsureActiveRenderer();
            return _activeRendererUsesAngledHeaders;
        }

        private static void EnsureActiveRenderer()
        {
            long settingsRevision = WorkTabPresentationRevision.Current;
            int presentationVersion = _headerPresentationVersion;
            if (_activeRenderer != null &&
                _activeRendererSettingsRevision == settingsRevision &&
                _activeRendererPresentationVersion == presentationVersion)
            {
                return;
            }

            _activeRendererUsesAngledHeaders = BWTWorkTabEffectiveSettings.GetBool(
                SettingIDs.HeadersAngled);
            _activeRenderer = _activeRendererUsesAngledHeaders
                ? (IHeaderRenderer)_angledRenderer
                : (IHeaderRenderer)_vanillaRenderer;
            _activeRendererSettingsRevision = settingsRevision;
            _activeRendererPresentationVersion = presentationVersion;
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
            HeaderPresentationPacket presentation = CapturePresentation();
            return TryHandleWorkPriorityHeader(worker, rect, table, in presentation);
        }

        internal static bool TryHandleWorkPriorityHeader(
            PawnColumnWorker_WorkPriority worker,
            Rect rect,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || worker?.def?.workType == null)
            {
                return false;
            }

            if (!WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked &&
                SubWorkDrilldownState.IsBlankWorkColumn(worker.def))
            {
                return true;
            }

            try
            {
                HeaderInputController.UpdateCache(Event.current);
                bool allowNative = presentation.AngledHeadersEnabled
                    ? AngledHeaderController.DoHeader(worker, rect, table, in presentation)
                    : VanillaHeaderController.DoHeader(worker, rect, table, in presentation);
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
            _activeRenderer = null;
            _activeRendererSettingsRevision = long.MinValue;
            _activeRendererPresentationVersion = int.MinValue;
            _presentationPacketSettingsRevision = long.MinValue;
            _presentationPacketVersion = int.MinValue;
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
            unchecked
            {
                _headerPresentationVersion++;
            }

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
