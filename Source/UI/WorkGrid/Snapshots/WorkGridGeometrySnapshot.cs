using Spine.Collections;
using UnityEngine;

namespace Better_Work_Tab.UI.WorkGrid.Snapshots
{
    public readonly struct WorkGridIndexRange
    {
        public WorkGridIndexRange(int start, int count)
        {
            Start = start;
            Count = count;
        }

        public int Start { get; }
        public int Count { get; }
        public int EndExclusive => Start + Count;
    }

    public readonly struct WorkGridRowGeometry
    {
        public WorkGridRowGeometry(float offsetY, float height)
        {
            OffsetY = offsetY;
            Height = height;
        }

        public float OffsetY { get; }
        public float Height { get; }
    }

    public readonly struct WorkGridColumnGeometry
    {
        public WorkGridColumnGeometry(Rect headerContentRect, float offsetX, float width)
        {
            HeaderContentRect = headerContentRect;
            OffsetX = offsetX;
            Width = width;
        }

        public Rect HeaderContentRect { get; }
        public float OffsetX { get; }
        public float Width { get; }
    }

    /// <summary>Immutable geometry published by WorkTabLayoutController at its rebuild point.</summary>
    public sealed class WorkGridGeometrySnapshot
    {
        internal WorkGridGeometrySnapshot(
            int revision,
            Vector2 tableOrigin,
            float headerHeight,
            float schedulePinnedHeight,
            float subWorkPinnedHeight,
            float tutorialPinnedHeight,
            float contentHeight,
            float rowWidth,
            ImmutableSnapshotArray<WorkGridRowGeometry> rows,
            ImmutableSnapshotArray<WorkGridColumnGeometry> columns,
            int retainedCapacityBytes)
        {
            Revision = revision;
            TableOrigin = tableOrigin;
            HeaderHeight = headerHeight;
            SchedulePinnedHeight = schedulePinnedHeight;
            SubWorkPinnedHeight = subWorkPinnedHeight;
            TutorialPinnedHeight = tutorialPinnedHeight;
            ContentHeight = contentHeight;
            RowWidth = rowWidth;
            Rows = rows ?? ImmutableSnapshotArray<WorkGridRowGeometry>.Empty;
            Columns = columns ?? ImmutableSnapshotArray<WorkGridColumnGeometry>.Empty;
            RetainedCapacityBytes = retainedCapacityBytes;
        }

        public int Revision { get; }
        public Vector2 TableOrigin { get; }
        public float HeaderHeight { get; }
        public float SchedulePinnedHeight { get; }
        public float SubWorkPinnedHeight { get; }
        public float TutorialPinnedHeight { get; }
        public float PinnedRowsHeight =>
            SchedulePinnedHeight + SubWorkPinnedHeight + TutorialPinnedHeight;
        public float ContentHeight { get; }
        public float RowWidth { get; }
        public ImmutableSnapshotArray<WorkGridRowGeometry> Rows { get; }
        public ImmutableSnapshotArray<WorkGridColumnGeometry> Columns { get; }
        public int RetainedCapacityBytes { get; }
        public float BodyTop => TableOrigin.y + HeaderHeight + PinnedRowsHeight;
        public float BodyBottom => BodyTop + ContentHeight;

        public Rect GetHeaderRect(int columnIndex, float horizontalScroll)
        {
            Rect rect = Columns[columnIndex].HeaderContentRect;
            rect.x -= horizontalScroll;
            return rect;
        }

        public Rect GetRowScreenRect(int rowIndex, Vector2 scrollPosition)
        {
            WorkGridRowGeometry row = Rows[rowIndex];
            return new Rect(
                TableOrigin.x,
                BodyTop + row.OffsetY - scrollPosition.y,
                RowWidth,
                row.Height);
        }

        public bool TryGetRowIndex(Vector2 screenPosition, float verticalScroll, out int rowIndex)
        {
            rowIndex = -1;
            if (screenPosition.y < BodyTop)
            {
                return false;
            }

            float localY = screenPosition.y - BodyTop + verticalScroll;
            for (int i = 0; i < Rows.Count; i++)
            {
                WorkGridRowGeometry row = Rows[i];
                if (localY < row.OffsetY + row.Height)
                {
                    rowIndex = i;
                    return true;
                }
            }
            return false;
        }

        public bool TryGetHeaderColumnIndex(Vector2 screenPosition, float horizontalScroll, out int columnIndex)
        {
            columnIndex = -1;
            for (int i = 0; i < Columns.Count; i++)
            {
                if (GetHeaderRect(i, horizontalScroll).Contains(screenPosition))
                {
                    columnIndex = i;
                    return true;
                }
            }
            return false;
        }

        public bool TryGetBodyColumnIndex(Vector2 screenPosition, Vector2 scrollPosition, out int columnIndex)
        {
            columnIndex = -1;
            if (screenPosition.y < BodyTop || screenPosition.y > BodyBottom)
            {
                return false;
            }

            float localX = screenPosition.x - TableOrigin.x + scrollPosition.x;
            if (localX < 0f)
            {
                return false;
            }

            for (int i = 0; i < Columns.Count; i++)
            {
                WorkGridColumnGeometry column = Columns[i];
                if (localX >= column.OffsetX && localX <= column.OffsetX + column.Width)
                {
                    columnIndex = i;
                    return true;
                }
            }
            return false;
        }

        public WorkGridIndexRange GetVisibleRowRange(Rect viewport, float verticalScroll)
        {
            int first = Rows.Count;
            int last = -1;
            for (int i = 0; i < Rows.Count; i++)
            {
                Rect rect = GetRowScreenRect(i, new Vector2(0f, verticalScroll));
                if (rect.yMax >= viewport.yMin && rect.yMin <= viewport.yMax)
                {
                    if (first == Rows.Count) first = i;
                    last = i;
                }
            }
            return last < first ? new WorkGridIndexRange(0, 0) : new WorkGridIndexRange(first, last - first + 1);
        }

        public WorkGridIndexRange GetVisibleColumnRange(Rect viewport, float horizontalScroll)
        {
            int first = Columns.Count;
            int last = -1;
            for (int i = 0; i < Columns.Count; i++)
            {
                Rect rect = GetHeaderRect(i, horizontalScroll);
                if (rect.xMax >= viewport.xMin && rect.xMin <= viewport.xMax)
                {
                    if (first == Columns.Count) first = i;
                    last = i;
                }
            }
            return last < first ? new WorkGridIndexRange(0, 0) : new WorkGridIndexRange(first, last - first + 1);
        }
    }
}
