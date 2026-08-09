using System;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.UI.WindowSession
{
    /// <summary>
    /// Owns the Work tab's requested-size cache and bottom-anchored resize staging.
    /// The window supplies the live values that belong to its base-window boundary;
    /// this class owns the sizing policy and cached geometry.
    /// </summary>
    internal sealed class WorkTabWindowSizingController
    {
        private const float MinWorkTabHeight = 200f;
        private const float DefaultPawnRowHeight = 30f;

        private readonly Func<PawnTable> _pawnTableProvider;
        private readonly Func<PawnOrganizerSystem> _organizerProvider;
        private readonly Func<float> _extraTopSpaceProvider;
        private readonly Func<float> _extraBottomSpaceProvider;
        private readonly Func<float> _marginProvider;
        private readonly Func<Rect> _windowRectProvider;
        private readonly Func<int> _screenWidthProvider;
        private readonly Func<int> _screenHeightProvider;
        private readonly Func<bool> _fluffyScheduleOpenProvider;
        private readonly Func<int> _timePriorityLayoutSignatureProvider;
        private readonly Func<int> _measurementSignatureProvider;
        private readonly Func<int> _maxVisiblePawnsProvider;
        private readonly Func<bool> _keepVanillaWorkTabMinimumWidthProvider;

        private int _requestedTabSizeCacheSignature = int.MinValue;
        private Vector2 _requestedTabSizeCache;
        private Rect _pendingWindowRect;
        private bool _hasPendingWindowRect;

        internal WorkTabWindowSizingController(
            Func<PawnTable> pawnTableProvider,
            Func<PawnOrganizerSystem> organizerProvider,
            Func<float> extraTopSpaceProvider,
            Func<float> extraBottomSpaceProvider,
            Func<float> marginProvider,
            Func<Rect> windowRectProvider,
            Func<int> screenWidthProvider,
            Func<int> screenHeightProvider,
            Func<bool> fluffyScheduleOpenProvider,
            Func<int> timePriorityLayoutSignatureProvider,
            Func<int> measurementSignatureProvider,
            Func<int> maxVisiblePawnsProvider,
            Func<bool> keepVanillaWorkTabMinimumWidthProvider)
        {
            _pawnTableProvider = pawnTableProvider ?? throw new ArgumentNullException(nameof(pawnTableProvider));
            _organizerProvider = organizerProvider ?? throw new ArgumentNullException(nameof(organizerProvider));
            _extraTopSpaceProvider = extraTopSpaceProvider ??
                throw new ArgumentNullException(nameof(extraTopSpaceProvider));
            _extraBottomSpaceProvider = extraBottomSpaceProvider ??
                throw new ArgumentNullException(nameof(extraBottomSpaceProvider));
            _marginProvider = marginProvider ?? throw new ArgumentNullException(nameof(marginProvider));
            _windowRectProvider = windowRectProvider ?? throw new ArgumentNullException(nameof(windowRectProvider));
            _screenWidthProvider = screenWidthProvider ?? throw new ArgumentNullException(nameof(screenWidthProvider));
            _screenHeightProvider = screenHeightProvider ?? throw new ArgumentNullException(nameof(screenHeightProvider));
            _fluffyScheduleOpenProvider = fluffyScheduleOpenProvider ??
                throw new ArgumentNullException(nameof(fluffyScheduleOpenProvider));
            _timePriorityLayoutSignatureProvider = timePriorityLayoutSignatureProvider ??
                throw new ArgumentNullException(nameof(timePriorityLayoutSignatureProvider));
            _measurementSignatureProvider = measurementSignatureProvider ??
                throw new ArgumentNullException(nameof(measurementSignatureProvider));
            _maxVisiblePawnsProvider = maxVisiblePawnsProvider ??
                throw new ArgumentNullException(nameof(maxVisiblePawnsProvider));
            _keepVanillaWorkTabMinimumWidthProvider = keepVanillaWorkTabMinimumWidthProvider ??
                throw new ArgumentNullException(nameof(keepVanillaWorkTabMinimumWidthProvider));
        }

        internal Vector2 RequestedTabSize
        {
            get
            {
                PawnTable table = _pawnTableProvider();
                if (table == null)
                {
                    return Vector2.zero;
                }

                // MainTabWindow.PostOpen can request the initial size from outside
                // Unity's OnGUI loop. Recaching a dirty PawnTable there eventually
                // initializes Verse.Text through GUI.skin, which Unity rejects
                // outside OnGUI and permanently poisons the Text type initializer.
                // Use only already-cached geometry for that cold-open request; the
                // first normal IMGUI pass below will build the live layout and
                // bottom-anchor the window to its exact requested size.
                if (Event.current == null && table.dirty)
                {
                    Vector2 cachedSize = table.cachedSize;
                    float coldWidth = cachedSize.x > 0f
                        ? cachedSize.x + _marginProvider() * 2f
                        : Mathf.Min(_screenWidthProvider() - 2f, 1400f);
                    float coldHeight = cachedSize.y > 0f
                        ? cachedSize.y +
                          _extraBottomSpaceProvider() +
                          _extraTopSpaceProvider() +
                          _marginProvider() * 2f +
                          WorkGridLayoutMetrics.ScrollViewFitAllowance
                        : MinWorkTabHeight;
                    return new Vector2(
                        Mathf.Clamp(
                            coldWidth,
                            1f,
                            Mathf.Max(1f, _screenWidthProvider() - 2f)),
                        Mathf.Clamp(
                            coldHeight,
                            MinWorkTabHeight,
                            Mathf.Max(MinWorkTabHeight, _screenHeightProvider() - 35f)));
                }

                float finalHeight;
                float finalWidth;

                PawnOrganizerSystem organizer = _organizerProvider();
                int sizeSignature = ComputeRequestedTabSizeSignature(table, organizer?.Layout);
                if (_requestedTabSizeCacheSignature == sizeSignature)
                {
                    return _requestedTabSizeCache;
                }

                if (organizer?.Layout != null)
                {
                    // MainTabWindow_BetterWork is the sole layout publisher. Sizing only
                    // consumes the geometry already published for this frame; publishing a
                    // second layout here would use a top-origin table and could overwrite
                    // Main's bottom-anchored geometry.
                    float pinnedRowsHeight = WorkGridLayoutMetrics.GetHeaderAnchoredPinnedRowsHeight();
                    float layoutHeight = organizer.Layout.HeaderHeight +
                        pinnedRowsHeight +
                        WorkGridLayoutMetrics.GetHeaderAnchoredContentHeight(organizer.Layout);
                    finalHeight = layoutHeight +
                        _extraBottomSpaceProvider() +
                        _extraTopSpaceProvider() +
                        _marginProvider() * 2f +
                        WorkGridLayoutMetrics.ScrollViewFitAllowance;
                    float tableScrollWidth = WorkGridLayoutMetrics.GetVisualTableScrollWidth(organizer.Layout, table);
                    if (_keepVanillaWorkTabMinimumWidthProvider())
                    {
                        // PawnTable.Size.x is the width vanilla MainTabWindow_PawnTable requests.
                        // Preserve it as a floor while still allowing wider rendered content to grow right.
                        tableScrollWidth = Mathf.Max(tableScrollWidth, table.Size.x);
                    }

                    finalWidth = tableScrollWidth + _marginProvider() * 2f;
                }
                else
                {
                    // Fallback to vanilla size if organizer not ready
                    finalHeight = table.Size.y +
                        _extraBottomSpaceProvider() +
                        _extraTopSpaceProvider() +
                        _marginProvider() * 2f +
                        WorkGridLayoutMetrics.ScrollViewFitAllowance;
                    finalWidth = table.Size.x + _marginProvider() * 2f;
                }

                float maxWindowWidth = Mathf.Max(1f, _screenWidthProvider() - 2f);
                bool needsHorizontalScrollbar = finalWidth > maxWindowWidth + 0.5f;
                if (needsHorizontalScrollbar)
                {
                    finalHeight += WorkGridLayoutMetrics.HorizontalScrollbarHeight;
                }

                finalHeight = Mathf.Min(
                    finalHeight,
                    GetConfiguredMaxWindowHeight(organizer?.Layout, table, needsHorizontalScrollbar));
                finalWidth = Mathf.Min(finalWidth, maxWindowWidth);

                _requestedTabSizeCacheSignature = ComputeRequestedTabSizeSignature(table, organizer?.Layout);
                _requestedTabSizeCache = new Vector2(finalWidth, finalHeight);
                return _requestedTabSizeCache;
            }
        }

        /// <summary>
        /// Stages a new rect for the next WindowOnGUI pass. The rect is deliberately
        /// not applied here because Unity's current GUI window has already established
        /// its clipping and event coordinates by the time contents are drawn.
        /// </summary>
        internal void StageBottomAnchoredResizeIfRequestedSizeChanged(bool force = false)
        {
            Vector2 requestedSize = RequestedTabSize;
            if (requestedSize.x <= 0f || requestedSize.y <= 0f)
            {
                return;
            }

            Rect rect = _hasPendingWindowRect ? _pendingWindowRect : _windowRectProvider();
            float screenBottom = _screenHeightProvider() - 35f;
            if (!force &&
                Mathf.Abs(rect.width - requestedSize.x) < 0.5f &&
                Mathf.Abs(rect.height - requestedSize.y) < 0.5f)
            {
                return;
            }

            rect.width = requestedSize.x;
            rect.height = requestedSize.y;
            rect.x = Mathf.Clamp(
                rect.x,
                0f,
                Mathf.Max(0f, _screenWidthProvider() - rect.width));
            rect.y = Mathf.Max(0f, screenBottom - rect.height);
            _pendingWindowRect = rect;
            _hasPendingWindowRect = true;
        }

        internal bool TryConsumePendingWindowRect(out Rect pendingWindowRect)
        {
            if (!_hasPendingWindowRect)
            {
                pendingWindowRect = new Rect();
                return false;
            }

            pendingWindowRect = _pendingWindowRect;
            _hasPendingWindowRect = false;
            return true;
        }

        internal void InvalidateRequestedTabSizeCache()
        {
            _requestedTabSizeCacheSignature = int.MinValue;
        }

        /// <summary>
        /// Ends the current window's resize lifecycle without discarding the
        /// signature-validated requested-size cache. Ordinary tab closes may be
        /// followed by a warm reopen, but a staged rect belongs only to the
        /// closing window lifecycle and must not be applied afterward.
        /// </summary>
        internal void ResetForWindowClose()
        {
            _pendingWindowRect = default(Rect);
            _hasPendingWindowRect = false;
        }

        private int ComputeRequestedTabSizeSignature(PawnTable table, IWorkTabLayoutController layout)
        {
            // This is a cache key, so it must never perform the work it is meant to
            // guard. PawnTable.Size calls RecacheIfDirty and previously made this
            // signature cost up to 15.77 ms on first open. Use already-cached fields
            // plus authoritative dirty/layout revisions; a miss will perform the
            // normal refresh in RequestedTabSize's calculation path.
            Vector2 tableSize = table?.cachedSize ?? Vector2.zero;
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (layout?.LayoutRevision ?? -1);
                hash = (hash * 31) + ((table?.dirty ?? true) ? 1 : 0);
                hash = (hash * 31) + (table?.cachedPawns?.Count ?? 0);
                hash = (hash * 31) + Mathf.RoundToInt(tableSize.x * 10f);
                hash = (hash * 31) + Mathf.RoundToInt(tableSize.y * 10f);
                hash = (hash * 31) + Mathf.RoundToInt((table?.cachedHeaderHeight ?? 0f) * 10f);
                hash = (hash * 31) + _screenWidthProvider();
                hash = (hash * 31) + _screenHeightProvider();
                hash = (hash * 31) + (_fluffyScheduleOpenProvider() ? 1 : 0);
                hash = (hash * 31) + _timePriorityLayoutSignatureProvider();
                hash = (hash * 31) + _measurementSignatureProvider();
                hash = (hash * 31) + _maxVisiblePawnsProvider();
                hash = (hash * 31) + (_keepVanillaWorkTabMinimumWidthProvider() ? 1 : 0);
                return hash;
            }
        }

        private float GetConfiguredMaxWindowHeight(
            IWorkTabLayoutController layout,
            PawnTable table,
            bool needsHorizontalScrollbar)
        {
            float screenMaxHeight = Mathf.Max(_screenHeightProvider() - 35f, MinWorkTabHeight);
            int maxVisiblePawns = _maxVisiblePawnsProvider();
            if (maxVisiblePawns <= 0)
            {
                return screenMaxHeight;
            }

            float headerHeight = layout?.HeaderHeight ?? table?.cachedHeaderHeight ?? 0f;
            float pinnedRowsHeight = layout != null
                ? WorkGridLayoutMetrics.GetHeaderAnchoredPinnedRowsHeight()
                : 0f;
            float pawnRowHeight = GetNominalPawnRowHeight(layout);
            float visibleContentHeight = Mathf.Max(1, maxVisiblePawns) * pawnRowHeight;
            float configuredHeight =
                _extraTopSpaceProvider() +
                headerHeight +
                pinnedRowsHeight +
                visibleContentHeight +
                _extraBottomSpaceProvider() +
                _marginProvider() * 2f +
                WorkGridLayoutMetrics.ScrollViewFitAllowance +
                (needsHorizontalScrollbar ? WorkGridLayoutMetrics.HorizontalScrollbarHeight : 0f);

            return Mathf.Min(screenMaxHeight, Mathf.Max(configuredHeight, MinWorkTabHeight));
        }

        private static float GetNominalPawnRowHeight(IWorkTabLayoutController layout)
        {
            var descriptors = layout?.GetRowDescriptors();
            if (descriptors != null)
            {
                for (int i = 0; i < descriptors.Count; i++)
                {
                    if (descriptors[i]?.Pawn != null && descriptors[i].Height > 0f)
                    {
                        return Mathf.Max(DefaultPawnRowHeight, descriptors[i].Height);
                    }
                }
            }

            return DefaultPawnRowHeight;
        }
    }
}
