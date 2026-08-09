using Better_Work_Tab.DragDrop;
using Better_Work_Tab.PawnOrganizer;
using UnityEngine;

namespace Better_Work_Tab.UI.WorkGrid.Layout
{
    /// <summary>
    /// The single coordinate calculation shared by Work-tab rendering and input.
    /// HeaderRect is already in window space; BodyContentX remains in scroll-view
    /// content space and BodyScreenX is its window-space counterpart.
    /// </summary>
    internal readonly struct WorkGridAnimatedColumnGeometry
    {
        internal WorkGridAnimatedColumnGeometry(
            Rect headerRect,
            float bodyContentX,
            float bodyScreenX,
            float width)
        {
            HeaderRect = headerRect;
            BodyContentX = bodyContentX;
            BodyScreenX = bodyScreenX;
            Width = width;
        }

        internal Rect HeaderRect { get; }
        internal float BodyContentX { get; }
        internal float BodyScreenX { get; }
        internal float Width { get; }

        internal Rect GetBodyContentRect(Rect rowRect)
        {
            return new Rect(BodyContentX, rowRect.y, Width, rowRect.height);
        }

        internal Rect GetBodyScreenRect(Rect rowRect)
        {
            return new Rect(BodyScreenX, rowRect.y, Width, rowRect.height);
        }
    }

    /// <summary>
    /// Converts the stable layout column into the exact animated coordinates used
    /// by the header and body renderers. Callers must use the returned value as-is;
    /// they must not apply another reorder offset.
    /// </summary>
    internal static class WorkGridInteractionGeometry
    {
        internal static WorkGridAnimatedColumnGeometry GetAnimatedColumn(
            WorkTabLayoutColumn column)
        {
            Rect stableHeader = column.HeaderRect;
            float stableHeaderX = stableHeader.x;
            ColumnReorderAnimationState.GetOffsets(
                column,
                out float headerOffset,
                out float cellOffset);

            stableHeader.x += headerOffset;
            float bodyContentX = column.OffsetX + cellOffset;
            float bodyScreenX = stableHeaderX + cellOffset;
            return new WorkGridAnimatedColumnGeometry(
                stableHeader,
                bodyContentX,
                bodyScreenX,
                column.Width);
        }

        internal static Rect GetAnimatedHeaderRect(WorkTabLayoutColumn column)
        {
            return GetAnimatedColumn(column).HeaderRect;
        }

        internal static Rect GetAnimatedBodyContentRect(
            WorkTabLayoutColumn column,
            Rect rowRect)
        {
            return GetAnimatedColumn(column).GetBodyContentRect(rowRect);
        }

        internal static Rect GetAnimatedBodyScreenRect(
            WorkTabLayoutColumn column,
            Rect rowRect)
        {
            return GetAnimatedColumn(column).GetBodyScreenRect(rowRect);
        }
    }
}
