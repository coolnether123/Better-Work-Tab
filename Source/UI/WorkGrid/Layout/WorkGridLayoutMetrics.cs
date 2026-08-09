using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using UnityEngine;

namespace Better_Work_Tab.UI.WorkGrid.Layout
{
    /// <summary>
    /// Shared Work-grid measurements used by window sizing and the drawing surface.
    /// Keeping these calculations in one owner prevents the two paths from drifting.
    /// </summary>
    internal static class WorkGridLayoutMetrics
    {
        internal const float ScrollViewFitAllowance = 4f;
        internal const float HorizontalScrollbarHeight = 16f;
        private const float InlineScheduleFooterReclaim = 18f;

        internal static float SchedulePinnedHeight =>
            Mathf.Max(0f, TimePriorityScheduleEditor.HeaderPinnedRowsHeight);

        internal static float SubWorkPinnedHeight =>
            SubWorkDrilldownState.HasAnyDrilldown
                ? Mathf.Max(0f, SubWorkDrilldownState.GlobalRowReservedHeight)
                : 0f;

        internal static float TutorialPinnedHeight =>
            Mathf.Max(0f, BWTTutorialStrip.ReservedHeight);

        internal static float GetPinnedRowsHeight()
        {
            return SchedulePinnedHeight + SubWorkPinnedHeight + TutorialPinnedHeight;
        }

        internal static float GetSubWorkBandTop(IWorkTabLayoutController layout)
        {
            return layout == null
                ? 0f
                : layout.TableOrigin.y + layout.HeaderHeight + SchedulePinnedHeight;
        }

        internal static Rect GetSubWorkBandRect(IWorkTabLayoutController layout)
        {
            if (layout == null || SubWorkPinnedHeight <= 0.5f)
            {
                return Rect.zero;
            }

            return new Rect(
                layout.TableOrigin.x,
                GetSubWorkBandTop(layout),
                Mathf.Max(layout.Table != null ? layout.Table.Size.x - 16f : 0f, 1f),
                SubWorkPinnedHeight);
        }

        internal static Rect GetSubWorkVisibleBandRect(IWorkTabLayoutController layout)
        {
            Rect reserved = GetSubWorkBandRect(layout);
            if (reserved == Rect.zero)
            {
                return Rect.zero;
            }

            reserved.height = Mathf.Min(
                reserved.height,
                Mathf.Max(0f, SubWorkDrilldownBarRenderer.RowHeight));
            return reserved;
        }

        internal static float GetHeaderAnchoredPinnedRowsHeight()
        {
            return Mathf.Max(0f, GetPinnedRowsHeight() -
                Mathf.Min(SchedulePinnedHeight, InlineScheduleFooterReclaim));
        }

        internal static float GetHeaderAnchoredContentHeight(IWorkTabLayoutController layout)
        {
            float transientHeight = GetTransientTimePriorityRowHeight(layout);
            float reclaimedHeight = Mathf.Min(transientHeight, InlineScheduleFooterReclaim);
            return Mathf.Max(0f, (layout?.ContentHeight ?? 0f) - reclaimedHeight);
        }

        internal static float GetInlineTimePriorityReservedHeight(IWorkTabLayoutController layout)
        {
            float reservedHeight = SchedulePinnedHeight +
                GetTransientTimePriorityRowHeight(layout);
            return Mathf.Min(reservedHeight, InlineScheduleFooterReclaim);
        }

        internal static float GetVisualTableScrollWidth(IWorkTabLayoutController layout, PawnTable table)
        {
            float tableWidth = table != null ? Mathf.Max(table.Size.x, table.cachedSize.x) : 0f;
            float visualColumnWidth = GetVisualColumnWidth(layout);
            if (visualColumnWidth <= 0f)
            {
                return tableWidth;
            }

            return Mathf.Ceil(visualColumnWidth + 16f);
        }

        private static float GetTransientTimePriorityRowHeight(IWorkTabLayoutController layout)
        {
            IReadOnlyList<WorkTabLayoutRow> rows = layout?.Rows;
            if (rows == null)
            {
                return 0f;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                WorkTabLayoutRow row = rows[i];
                if (row.Divider != null && TimePriorityScheduleEditor.IsTransientDivider(row.Divider))
                {
                    return row.Height;
                }
            }

            return 0f;
        }

        private static float GetVisualColumnWidth(IWorkTabLayoutController layout)
        {
            if (layout?.Columns == null || layout.Columns.Count == 0)
            {
                return 0f;
            }

            float max = 0f;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                max = Mathf.Max(max, column.OffsetX + column.Width);
            }

            return Mathf.Ceil(max);
        }
    }
}
