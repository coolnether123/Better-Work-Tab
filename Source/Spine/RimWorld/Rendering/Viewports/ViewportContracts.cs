using UnityEngine;

namespace Spine.RimWorld.Api
{
    public readonly struct ViewportRange
    {
        public ViewportRange(int start, int count)
        {
            Start = start;
            Count = count;
        }

        public int Start { get; }
        public int Count { get; }
        public int EndExclusive => Start + Count;
        public bool IsEmpty => Count == 0;
    }

    public readonly struct ResolvedViewport
    {
        public ResolvedViewport(Rect bounds, ViewportRange horizontal, ViewportRange vertical)
        {
            Bounds = bounds;
            Horizontal = horizontal;
            Vertical = vertical;
        }

        public Rect Bounds { get; }
        public ViewportRange Horizontal { get; }
        public ViewportRange Vertical { get; }
    }

    public interface IViewportItemLayout
    {
        int Count { get; }
        float GetStart(int index);
        float GetExtent(int index);
    }

    public interface IViewportResolver
    {
        ResolvedViewport Resolve(
            Rect viewportBounds,
            Vector2 scrollPosition,
            IViewportItemLayout horizontalLayout,
            IViewportItemLayout verticalLayout,
            int overscanItems = 0);
    }
}
