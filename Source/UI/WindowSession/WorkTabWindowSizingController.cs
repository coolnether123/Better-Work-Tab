using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.UI.WindowSession
{
    internal readonly struct WorkTabSizingInputs
    {
        internal readonly PawnTable Table;
        internal readonly IWorkTabLayoutController Layout;
        internal readonly float ExtraTopSpace;
        internal readonly float ExtraBottomSpace;
        internal readonly float Margin;
        internal readonly int ScreenWidth;
        internal readonly int ScreenHeight;
        internal readonly int TimePriorityLayoutSignature;
        internal readonly int MeasurementSignature;
        internal readonly int MaxVisiblePawns;
        internal readonly bool KeepVanillaWorkTabMinimumWidth;

        internal WorkTabSizingInputs(
            PawnTable table,
            IWorkTabLayoutController layout,
            float extraTopSpace,
            float extraBottomSpace,
            float margin,
            int screenWidth,
            int screenHeight,
            int timePriorityLayoutSignature,
            int measurementSignature,
            int maxVisiblePawns,
            bool keepVanillaWorkTabMinimumWidth)
        {
            Table = table;
            Layout = layout;
            ExtraTopSpace = extraTopSpace;
            ExtraBottomSpace = extraBottomSpace;
            Margin = margin;
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            TimePriorityLayoutSignature = timePriorityLayoutSignature;
            MeasurementSignature = measurementSignature;
            MaxVisiblePawns = maxVisiblePawns;
            KeepVanillaWorkTabMinimumWidth = keepVanillaWorkTabMinimumWidth;
        }
    }

    /// <summary>
    /// Owns the Work tab's requested-size cache and bottom-anchored resize staging.
    /// The window supplies an immutable snapshot of live boundary values and the
    /// current table/layout; this class owns the sizing policy and cached geometry.
    /// </summary>
    internal sealed class WorkTabWindowSizingController
    {
        private const float MinWorkTabHeight = 200f;
        private const float DefaultPawnRowHeight = 30f;

        private int _requestedTabSizeCacheSignature = int.MinValue;
        private Vector2 _requestedTabSizeCache;
        private Rect _pendingWindowRect;
        private bool _hasPendingWindowRect;

        internal Vector2 GetRequestedTabSize(in WorkTabSizingInputs inputs)
        {
            if (inputs.Table == null)
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
            if (Event.current == null && inputs.Table.dirty)
            {
                Vector2 cachedSize = inputs.Table.cachedSize;
                float coldWidth = cachedSize.x > 0f
                    ? cachedSize.x + inputs.Margin * 2f
                    : Mathf.Min(inputs.ScreenWidth - 2f, 1400f);
                float coldHeight = cachedSize.y > 0f
                    ? AddWindowFrame(cachedSize.y, in inputs)
                    : MinWorkTabHeight;
                return new Vector2(
                    Mathf.Clamp(
                        coldWidth,
                        1f,
                        Mathf.Max(1f, inputs.ScreenWidth - 2f)),
                    Mathf.Clamp(
                        coldHeight,
                        MinWorkTabHeight,
                        Mathf.Max(MinWorkTabHeight, inputs.ScreenHeight - 35f)));
            }

            float finalHeight;
            float finalWidth;

            int sizeSignature = ComputeRequestedTabSizeSignature(in inputs);
            if (_requestedTabSizeCacheSignature == sizeSignature)
            {
                return _requestedTabSizeCache;
            }

            if (inputs.Layout != null)
            {
                    // MainTabWindow_BetterWork is the sole layout publisher. Sizing only
                    // consumes the geometry already published for this frame; publishing a
                    // second layout here would use a top-origin table and could overwrite
                    // Main's bottom-anchored geometry.
                    float pinnedRowsHeight = WorkGridLayoutMetrics.GetHeaderAnchoredPinnedRowsHeight();
                    float layoutHeight = inputs.Layout.HeaderHeight +
                        pinnedRowsHeight +
                        WorkGridLayoutMetrics.GetHeaderAnchoredContentHeight(inputs.Layout);
                    finalHeight = AddWindowFrame(layoutHeight, in inputs);
                    float tableScrollWidth = WorkGridLayoutMetrics.GetVisualTableScrollWidth(inputs.Layout, inputs.Table);
                    if (inputs.KeepVanillaWorkTabMinimumWidth)
                    {
                        // PawnTable.Size.x is the width vanilla MainTabWindow_PawnTable requests.
                        // Preserve it as a floor while still allowing wider rendered content to grow right.
                        tableScrollWidth = Mathf.Max(tableScrollWidth, inputs.Table.Size.x);
                    }

                    finalWidth = tableScrollWidth + inputs.Margin * 2f;
            }
            else
            {
                    // Fallback to vanilla size if organizer not ready
                    finalHeight = AddWindowFrame(inputs.Table.Size.y, in inputs);
                    finalWidth = inputs.Table.Size.x + inputs.Margin * 2f;
            }

            float maxWindowWidth = Mathf.Max(1f, inputs.ScreenWidth - 2f);
            bool needsHorizontalScrollbar = finalWidth > maxWindowWidth + 0.5f;
            if (needsHorizontalScrollbar)
            {
                finalHeight += WorkGridLayoutMetrics.HorizontalScrollbarHeight;
            }

            finalHeight = Mathf.Min(
                finalHeight,
                GetConfiguredMaxWindowHeight(in inputs, needsHorizontalScrollbar));
            finalWidth = Mathf.Min(finalWidth, maxWindowWidth);

            // Keep this second signature calculation after table.Size. A cache miss
            // may refresh the table and change the authoritative cached fields.
            _requestedTabSizeCacheSignature = ComputeRequestedTabSizeSignature(in inputs);
            _requestedTabSizeCache = new Vector2(finalWidth, finalHeight);
            return _requestedTabSizeCache;
        }

        /// <summary>
        /// Stages a new rect for the next WindowOnGUI pass. The rect is deliberately
        /// not applied here because Unity's current GUI window has already established
        /// its clipping and event coordinates by the time contents are drawn.
        /// </summary>
        internal void StageBottomAnchoredResizeIfRequestedSizeChanged(
            in WorkTabSizingInputs inputs,
            Rect windowRect,
            bool force = false)
        {
            Vector2 requestedSize = GetRequestedTabSize(in inputs);
            if (requestedSize.x <= 0f || requestedSize.y <= 0f)
            {
                return;
            }

            Rect rect = _hasPendingWindowRect ? _pendingWindowRect : windowRect;
            float screenBottom = inputs.ScreenHeight - 35f;
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
                Mathf.Max(0f, inputs.ScreenWidth - rect.width));
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

        private int ComputeRequestedTabSizeSignature(in WorkTabSizingInputs inputs)
        {
            // This is a cache key, so it must never perform the work it is meant to
            // guard. PawnTable.Size calls RecacheIfDirty and previously made this
            // signature cost up to 15.77 ms on first open. Use already-cached fields
            // plus authoritative dirty/layout revisions; a miss will perform the
            // normal refresh in RequestedTabSize's calculation path.
            Vector2 tableSize = inputs.Table?.cachedSize ?? Vector2.zero;
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (inputs.Layout?.LayoutRevision ?? -1);
                hash = (hash * 31) + ((inputs.Table?.dirty ?? true) ? 1 : 0);
                hash = (hash * 31) + (inputs.Table?.cachedPawns?.Count ?? 0);
                hash = (hash * 31) + Mathf.RoundToInt(tableSize.x * 10f);
                hash = (hash * 31) + Mathf.RoundToInt(tableSize.y * 10f);
                hash = (hash * 31) + Mathf.RoundToInt((inputs.Table?.cachedHeaderHeight ?? 0f) * 10f);
                hash = (hash * 31) + Mathf.RoundToInt(inputs.ExtraTopSpace * 10f);
                hash = (hash * 31) + Mathf.RoundToInt(inputs.ExtraBottomSpace * 10f);
                hash = (hash * 31) + Mathf.RoundToInt(inputs.Margin * 10f);
                hash = (hash * 31) + inputs.ScreenWidth;
                hash = (hash * 31) + inputs.ScreenHeight;
                hash = (hash * 31) + inputs.TimePriorityLayoutSignature;
                hash = (hash * 31) + inputs.MeasurementSignature;
                hash = (hash * 31) + inputs.MaxVisiblePawns;
                hash = (hash * 31) + (inputs.KeepVanillaWorkTabMinimumWidth ? 1 : 0);
                return hash;
            }
        }

        private float GetConfiguredMaxWindowHeight(
            in WorkTabSizingInputs inputs,
            bool needsHorizontalScrollbar)
        {
            float screenMaxHeight = Mathf.Max(inputs.ScreenHeight - 35f, MinWorkTabHeight);
            int maxVisiblePawns = inputs.MaxVisiblePawns;
            if (maxVisiblePawns <= 0)
            {
                return screenMaxHeight;
            }

            float headerHeight = inputs.Layout?.HeaderHeight ?? inputs.Table?.cachedHeaderHeight ?? 0f;
            float pinnedRowsHeight = inputs.Layout != null
                ? WorkGridLayoutMetrics.GetHeaderAnchoredPinnedRowsHeight()
                : 0f;
            float pawnRowHeight = GetNominalPawnRowHeight(inputs.Layout);
            float visibleContentHeight = Mathf.Max(1, maxVisiblePawns) * pawnRowHeight;
            float configuredHeight =
                inputs.ExtraTopSpace +
                headerHeight +
                pinnedRowsHeight +
                visibleContentHeight +
                inputs.ExtraBottomSpace +
                inputs.Margin * 2f +
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

        private static float AddWindowFrame(float contentHeight, in WorkTabSizingInputs inputs)
        {
            return contentHeight +
                inputs.ExtraBottomSpace +
                inputs.ExtraTopSpace +
                inputs.Margin * 2f +
                WorkGridLayoutMetrics.ScrollViewFitAllowance;
        }
    }
}
