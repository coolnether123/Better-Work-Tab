using Better_Work_Tab.PawnOrganizer.API;
using UnityEngine;

namespace Better_Work_Tab.UI.WorkGrid.Layout
{
    internal readonly struct WorkTabViewportFrame
    {
        internal WorkTabViewportFrame(
            IWorkTabLayoutController layout,
            Rect contentRect,
            Rect windowRect,
            float bottomReservedSpace,
            float inlineReservedHeight,
            float pinnedHeight,
            float tableScrollWidth,
            bool expandBesideTransitioning,
            int frameCount)
        {
            Layout = layout;
            ContentRect = contentRect;
            WindowRect = windowRect;
            BottomReservedSpace = bottomReservedSpace;
            InlineReservedHeight = inlineReservedHeight;
            PinnedHeight = pinnedHeight;
            TableScrollWidth = tableScrollWidth;
            ExpandBesideTransitioning = expandBesideTransitioning;
            FrameCount = frameCount;
        }

        internal IWorkTabLayoutController Layout { get; }
        internal Rect ContentRect { get; }
        internal Rect WindowRect { get; }
        internal float BottomReservedSpace { get; }
        internal float InlineReservedHeight { get; }
        internal float PinnedHeight { get; }
        internal float TableScrollWidth { get; }
        internal bool ExpandBesideTransitioning { get; }
        internal int FrameCount { get; }
    }

    internal readonly struct WorkTabViewport
    {
        internal WorkTabViewport(Rect outRect, Rect viewRect)
        {
            OutRect = outRect;
            ViewRect = viewRect;
        }

        internal Rect OutRect { get; }
        internal Rect ViewRect { get; }
    }

    internal readonly struct WorkTabHorizontalScrollbarDragResult
    {
        internal WorkTabHorizontalScrollbarDragResult(
            bool applyCapturedScroll,
            float capturedScrollX,
            bool captureOnMouseDown,
            float mouseX,
            float pixelsToContent)
        {
            ApplyCapturedScroll = applyCapturedScroll;
            CapturedScrollX = capturedScrollX;
            CaptureOnMouseDown = captureOnMouseDown;
            MouseX = mouseX;
            PixelsToContent = pixelsToContent;
        }

        internal bool ApplyCapturedScroll { get; }
        internal float CapturedScrollX { get; }
        internal bool CaptureOnMouseDown { get; }
        internal float MouseX { get; }
        internal float PixelsToContent { get; }
    }

    internal sealed class WorkTabViewportController
    {
        private bool _stableHorizontalScrollbarVisible;
        private bool _lastRawHorizontalOverflow;
        private bool _lastHorizontalScrollbarVisible;
        private int _holdHorizontalScrollbarUntilFrame = -1;
        private bool _horizontalScrollbarTransitionActive;
        private bool _horizontalScrollbarTransitionVisible;
        private int _horizontalOverflowBeganFrame = -1;
        private float _lastScrollWindowWidth = -1f;
        private float _lastScrollTotalColumnWidth = -1f;
        private float _lastScrollContentHeight = -1f;
        private bool _hasStableVerticalScrollbarState;
        private bool _stableVerticalScrollbarVisible;
        private bool _horizontalScrollbarDragCaptured;
        private float _horizontalScrollbarDragMouseX;
        private float _horizontalScrollbarDragScrollX;
        private float _horizontalScrollbarDragPixelsToContent = 1f;

        internal bool LastRawHorizontalOverflow => _lastRawHorizontalOverflow;
        internal bool LastHorizontalScrollbarVisible => _lastHorizontalScrollbarVisible;

        internal void ResetForWindowClose()
        {
            _stableHorizontalScrollbarVisible = false;
            _lastRawHorizontalOverflow = false;
            _lastHorizontalScrollbarVisible = false;
            _holdHorizontalScrollbarUntilFrame = -1;
            _horizontalScrollbarTransitionActive = false;
            _horizontalScrollbarTransitionVisible = false;
            _horizontalOverflowBeganFrame = -1;
            _lastScrollWindowWidth = -1f;
            _lastScrollTotalColumnWidth = -1f;
            _lastScrollContentHeight = -1f;
            _hasStableVerticalScrollbarState = false;
            _stableVerticalScrollbarVisible = false;
            _horizontalScrollbarDragCaptured = false;
            _horizontalScrollbarDragMouseX = 0f;
            _horizontalScrollbarDragScrollX = 0f;
            _horizontalScrollbarDragPixelsToContent = 1f;
        }

        internal WorkTabViewport CalculateScrollRects(in WorkTabViewportFrame frame)
        {
            IWorkTabLayoutController layout = frame.Layout;
            float headerHeight = layout.HeaderHeight;
            float tableBottomSpace = Mathf.Max(
                0f,
                frame.BottomReservedSpace - frame.InlineReservedHeight);
            float scrollAreaHeight = Mathf.Max(0f,
                frame.ContentRect.yMax -
                tableBottomSpace -
                WorkGridLayoutMetrics.ScrollViewFitAllowance -
                (layout.TableOrigin.y + headerHeight));

            float availableWidth = Mathf.Max(1f, frame.ContentRect.xMax - layout.TableOrigin.x);
            float viewportWidth = Mathf.Min(frame.TableScrollWidth, availableWidth);

            Rect outRect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + headerHeight,
                viewportWidth,
                scrollAreaHeight);

            if (frame.PinnedHeight > 0f)
            {
                outRect.y += frame.PinnedHeight;
                outRect.height = Mathf.Max(0f, outRect.height - frame.PinnedHeight);
            }

            float widthWithoutScrollbar = viewportWidth - WorkGridLayoutMetrics.HorizontalScrollbarHeight;
            float totalColumnWidth = layout.Columns.Count > 0
                ? layout.Columns[layout.Columns.Count - 1].OffsetX + layout.Columns[layout.Columns.Count - 1].Width
                : widthWithoutScrollbar;
            float contentHeight = Mathf.Max(layout.ContentHeight, 1f);
            bool rawHorizontalOverflow = totalColumnWidth > widthWithoutScrollbar + 0.5f;
            bool expandBesideTransitioning = frame.ExpandBesideTransitioning;
            bool layoutWidthChanged = _lastScrollTotalColumnWidth >= 0f &&
                Mathf.Abs(totalColumnWidth - _lastScrollTotalColumnWidth) > 0.5f;
            bool windowWidthChanged = _lastScrollWindowWidth >= 0f &&
                Mathf.Abs(frame.WindowRect.width - _lastScrollWindowWidth) > 0.5f;
            _lastScrollTotalColumnWidth = totalColumnWidth;
            _lastScrollWindowWidth = frame.WindowRect.width;
            bool resizingOverflow = rawHorizontalOverflow &&
                (layoutWidthChanged || windowWidthChanged);
            if (expandBesideTransitioning || resizingOverflow)
            {
                if (!_horizontalScrollbarTransitionActive)
                {
                    _horizontalScrollbarTransitionActive = true;
                    // Expand-beside changes column width before the bottom-anchored window
                    // receives its resized rect. A scrollbar in that catch-up interval is
                    // transient and immediately disappears, so never preserve stale visibility.
                    _horizontalScrollbarTransitionVisible = false;
                }

                // Layout advances before Unity supplies the resized window contents for this
                // GUI pass. Suppress transient overflow through the transition and two catch-up
                // frames, then commit the final overflow state once.
                _holdHorizontalScrollbarUntilFrame = frame.FrameCount + 2;
            }

            bool preserveScrollbarVisibility = _horizontalScrollbarTransitionActive &&
                (expandBesideTransitioning || resizingOverflow ||
                 frame.FrameCount <= _holdHorizontalScrollbarUntilFrame);
            bool needsHorizontalScrollbar;
            if (preserveScrollbarVisibility)
            {
                needsHorizontalScrollbar = _horizontalScrollbarTransitionVisible;
                _horizontalOverflowBeganFrame = -1;
            }
            else
            {
                _horizontalScrollbarTransitionActive = false;
                if (!rawHorizontalOverflow)
                {
                    _horizontalOverflowBeganFrame = -1;
                    needsHorizontalScrollbar = false;
                }
                else if (_stableHorizontalScrollbarVisible)
                {
                    needsHorizontalScrollbar = true;
                }
                else
                {
                    if (_horizontalOverflowBeganFrame < 0)
                    {
                        _horizontalOverflowBeganFrame = frame.FrameCount;
                    }

                    // A drill-down can update its column width one GUI pass before its
                    // transition flag and resized window arrive. Require new overflow to
                    // persist before exposing a scrollbar, while still hiding it immediately.
                    needsHorizontalScrollbar =
                        frame.FrameCount - _horizontalOverflowBeganFrame > 2;
                }

                _stableHorizontalScrollbarVisible = needsHorizontalScrollbar;
            }

            float naturalViewWidth = Mathf.Max(widthWithoutScrollbar, totalColumnWidth);
            float viewWidth = needsHorizontalScrollbar
                ? naturalViewWidth
                : widthWithoutScrollbar;
            _lastRawHorizontalOverflow = rawHorizontalOverflow;
            _lastHorizontalScrollbarVisible = needsHorizontalScrollbar;
            float fittedViewportHeight = Mathf.Max(
                1f,
                outRect.height - (needsHorizontalScrollbar ? WorkGridLayoutMetrics.HorizontalScrollbarHeight : 0f));

            bool contentHeightChanged = _lastScrollContentHeight >= 0f &&
                Mathf.Abs(contentHeight - _lastScrollContentHeight) > 0.5f;
            _lastScrollContentHeight = contentHeight;
            bool rawVerticalOverflow = contentHeight > fittedViewportHeight + 0.5f;
            if (!_hasStableVerticalScrollbarState ||
                !preserveScrollbarVisibility ||
                contentHeightChanged)
            {
                _stableVerticalScrollbarVisible = rawVerticalOverflow;
                _hasStableVerticalScrollbarState = true;
            }

            // Expand-beside only changes columns. While the bottom-anchored window catches up,
            // keep the vertical scrollbar in its pre-transition state unless row content really
            // changed. Otherwise Unity briefly measures the same pawn grid against the stale
            // viewport height and flashes a vertical scrollbar during open/close.
            contentHeight = _stableVerticalScrollbarVisible
                ? Mathf.Max(contentHeight, fittedViewportHeight + 1f)
                : fittedViewportHeight;
            return new WorkTabViewport(
                outRect,
                new Rect(0f, 0f, viewWidth, contentHeight));
        }

        internal WorkTabHorizontalScrollbarDragResult PrepareHorizontalScrollbarDrag(
            Rect outRect,
            Rect viewRect,
            Vector2 scrollPosition,
            Event currentEvent)
        {
            bool horizontalOverflow = viewRect.width > outRect.width + 0.5f;
            Rect horizontalScrollbarRect = new Rect(
                outRect.x,
                outRect.yMax - WorkGridLayoutMetrics.HorizontalScrollbarHeight,
                outRect.width,
                WorkGridLayoutMetrics.HorizontalScrollbarHeight);
            float rootMouseX = currentEvent.mousePosition.x;
            bool captureOnMouseDown = horizontalOverflow &&
                currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                horizontalScrollbarRect.Contains(currentEvent.mousePosition);

            bool applyCapturedScroll = false;
            float capturedScrollX = scrollPosition.x;
            if (_horizontalScrollbarDragCaptured)
            {
                bool released = currentEvent.rawType == EventType.MouseUp || !UnityEngine.Input.GetMouseButton(0);
                if (released || !horizontalOverflow)
                {
                    _horizontalScrollbarDragCaptured = false;
                }
                else if (currentEvent.type != EventType.Layout)
                {
                    float maxScrollX = Mathf.Max(0f, viewRect.width - outRect.width);
                    capturedScrollX = Mathf.Clamp(
                        _horizontalScrollbarDragScrollX +
                        (rootMouseX - _horizontalScrollbarDragMouseX) * _horizontalScrollbarDragPixelsToContent,
                        0f,
                        maxScrollX);
                    applyCapturedScroll = true;
                }
            }

            return new WorkTabHorizontalScrollbarDragResult(
                applyCapturedScroll,
                capturedScrollX,
                captureOnMouseDown,
                rootMouseX,
                viewRect.width / Mathf.Max(1f, horizontalScrollbarRect.width));
        }

        internal void CaptureHorizontalScrollbarDragIfNeeded(
            in WorkTabHorizontalScrollbarDragResult drag,
            Vector2 scrollPosition)
        {
            if (!drag.CaptureOnMouseDown || GUIUtility.hotControl == 0)
            {
                return;
            }

            _horizontalScrollbarDragCaptured = true;
            _horizontalScrollbarDragMouseX = drag.MouseX;
            _horizontalScrollbarDragScrollX = scrollPosition.x;
            _horizontalScrollbarDragPixelsToContent = drag.PixelsToContent;
        }
    }
}
