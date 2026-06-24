using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Input;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Draws the fixed global-priority row used by the sub-work drilldown view.
    /// </summary>
    internal static class SubWorkDrilldownBarRenderer
    {
        internal static float RowHeight => SubWorkDrilldownState.GlobalRowVisibleHeight;
        internal static float ReservedRowHeight => SubWorkDrilldownState.GlobalRowReservedHeight;

        internal static void Draw(IWorkTabLayoutController layout)
        {
            if (!SubWorkDrilldownState.IsActive || layout == null)
            {
                return;
            }

            float rowHeight = RowHeight;
            if (rowHeight <= 0.5f)
            {
                return;
            }

            float reservedHeight = Mathf.Max(0f, ReservedRowHeight);
            float rowTop = layout.TableOrigin.y +
                layout.HeaderHeight +
                TimePriorityPlannerPrototype.HeaderPinnedRowsHeight +
                Mathf.Max(0f, reservedHeight - rowHeight);
            Rect rowRect = new Rect(
                layout.TableOrigin.x,
                rowTop,
                Mathf.Max(layout.Table != null ? layout.Table.Size.x - 16f : 0f, 1f),
                rowHeight);

            float alpha = Mathf.Lerp(0.45f, 0.72f, SubWorkDrilldownState.TransitionAlpha);
            Widgets.DrawBoxSolid(rowRect, new Color(0.08f, 0.1f, 0.11f, alpha));
            Widgets.DrawLineHorizontal(rowRect.xMin, rowRect.yMax - 1f, rowRect.width);

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                float animatedOffset = ColumnReorderAnimationState.GetHeaderOffset(column);
                Rect cellRect = new Rect(column.HeaderRect.x + animatedOffset, rowRect.y, column.Width, rowHeight);

                if (column.Column?.Worker is PawnColumnWorker_Label)
                {
                    DrawLabelCell(cellRect);
                    continue;
                }

                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                if (ShouldHighlightGlobalCell(column, cellRect))
                {
                    HighlightDrawer.DrawHighlight(cellRect, HighlightDrawer.GetColumnHoverColor());
                }

                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out var workGiver, out _))
                {
                    DrawGlobalPriorityCell(workGiver, cellRect);
                }
            }
        }

        private static bool ShouldHighlightGlobalCell(WorkTabLayoutColumn column, Rect cellRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null ||
                !settings.ShowPawnAndWorktypeHighlights ||
                !settings.enableRowColumnHighlights ||
                !settings.ShowCursorPawnAndWorktypeHighlight)
            {
                return false;
            }

            var workType = column.Column?.workType;
            if (workType == null)
            {
                return false;
            }

            var hoveredWorkType = PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType;
            return hoveredWorkType == workType || Mouse.IsOver(column.HeaderRect) || Mouse.IsOver(cellRect);
        }

        private static void DrawLabelCell(Rect rect)
        {
            var oldAnchor = Text.Anchor;
            var oldFont = Text.Font;
            var oldColor = GUI.color;

            if (Widgets.ButtonInvisible(rect))
            {
                ExitDrilldown();
                return;
            }

            Rect labelRect = new Rect(
                rect.x + 8f,
                rect.y,
                Mathf.Max(0f, rect.width - 16f),
                rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.82f);

            string label = SubWorkDrilldownState.ActiveWorkType?.labelShort?.CapitalizeFirst()
                ?? SubWorkDrilldownState.ActiveWorkType?.LabelCap.ToString()
                ?? "Sub-work";
            Widgets.Label(labelRect, label + " global");

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            TooltipHandler.TipRegion(rect, "Back to work types. " + SubWorkDrilldownInput.GestureLabel() + " or press Escape to return.");
        }

        private static void DrawGlobalPriorityCell(WorkGiver workGiver, Rect cellRect)
        {
            float boxSize = Mathf.Min(SubWorkDrilldownState.GlobalPriorityBoxSize, Mathf.Max(0f, cellRect.height - 4f));
            if (boxSize <= 6f)
            {
                return;
            }

            float x = cellRect.x + (cellRect.width - boxSize) / 2f;
            float y = cellRect.y + (cellRect.height - boxSize) / 2f;
            Rect boxRect = new Rect(x, y, boxSize, boxSize);
            WorkGiverPriorityBoxRenderer.DrawPriorityBox(workGiver, SubWorkDrilldownState.ActiveWorkType, null, boxRect);
        }

        internal static void ExitDrilldown(
            bool restoreMousePosition = false,
            int exitWorkColumnSlot = -1,
            float exitWaveSlotPosition = -1f)
        {
            Vector2 returnMousePosition = Vector2.zero;
            bool shouldRestoreMouse = restoreMousePosition &&
                !SubWorkDrilldownState.ShouldSuppressCursorRestoreForRapidExit &&
                SubWorkDrilldownState.TryGetReturnMousePosition(out returnMousePosition);

            SubWorkDrilldownState.Exit(exitWorkColumnSlot, exitWaveSlotPosition);
            HeaderDrawingCoordinator.NotifyAngledHeadersChanged();

            if (shouldRestoreMouse && (BetterWorkTabMod.Settings?.restoreCursorOnSubWorkExit ?? true))
            {
                NativeCursorPosition.ScheduleMoveToUiPosition(returnMousePosition);
            }

            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
    }
}
