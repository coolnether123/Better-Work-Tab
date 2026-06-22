using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Input;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Draws the fixed global-priority row used by the sub-work drilldown view.
    /// </summary>
    internal static class SubWorkDrilldownBarRenderer
    {
        internal const float RowHeight = SubWorkDrilldownState.GlobalRowHeight;

        internal static void Draw(IWorkTabLayoutController layout)
        {
            if (!SubWorkDrilldownState.IsActive || layout == null)
            {
                return;
            }

            Rect rowRect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight,
                Mathf.Max(layout.Table != null ? layout.Table.Size.x - 16f : 0f, 1f),
                RowHeight);

            float alpha = Mathf.Lerp(0.45f, 0.72f, SubWorkDrilldownState.TransitionAlpha);
            WidgetsCompat.DrawBoxSolid(rowRect, new Color(0.08f, 0.1f, 0.11f, alpha));
            Widgets.DrawLineHorizontal(rowRect.xMin, rowRect.yMax - 1f, rowRect.width);

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                Rect cellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, RowHeight);

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

            Rect buttonRect = new Rect(rect.x + 6f, rect.y + 5f, Mathf.Min(20f, rect.width - 8f), RowHeight - 10f);
            bool canDrawButton = buttonRect.width >= 18f;
            if (canDrawButton)
            {
                if (WidgetsCompat.ButtonImage(buttonRect, TexButton.CloseXSmall, Color.white, GenUI.MouseoverColor))
                {
                    ExitDrilldown();
                    return;
                }
            }

            Rect clickRect = canDrawButton
                ? new Rect(buttonRect.xMax, rect.y, Mathf.Max(0f, rect.xMax - buttonRect.xMax), rect.height)
                : rect;
            if (WidgetsCompat.ButtonInvisible(clickRect))
            {
                ExitDrilldown();
                return;
            }

            Rect labelRect = new Rect(
                canDrawButton ? buttonRect.xMax + 6f : rect.x + 5f,
                rect.y,
                Mathf.Max(0f, rect.xMax - (canDrawButton ? buttonRect.xMax + 10f : rect.x + 10f)),
                RowHeight);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.82f);

            string label = WorkTypeCompat.LabelShort(SubWorkDrilldownState.ActiveWorkType).CapitalizeFirst();
            if (label.NullOrEmpty())
            {
                label = "Sub-work";
            }
            Widgets.Label(labelRect, label + " global");

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            TooltipHandler.TipRegion(rect, "Back to work types. " + SubWorkDrilldownInput.GestureLabel() + " or press Escape to return.");
        }

        private static void DrawGlobalPriorityCell(WorkGiver workGiver, Rect cellRect)
        {
            const float boxSize = 25f;
            float x = cellRect.x + (cellRect.width - boxSize) / 2f;
            float y = cellRect.y + (cellRect.height - boxSize) / 2f;
            Rect boxRect = new Rect(x, y, boxSize, boxSize);
            WorkGiverPriorityBoxRenderer.DrawPriorityBox(workGiver, SubWorkDrilldownState.ActiveWorkType, null, boxRect);
        }

        internal static void ExitDrilldown(bool restoreMousePosition = false)
        {
            Vector2 returnMousePosition = Vector2.zero;
            bool shouldRestoreMouse = restoreMousePosition &&
                SubWorkDrilldownState.TryGetReturnMousePosition(out returnMousePosition);

            SubWorkDrilldownState.Exit();
            HeaderDrawingCoordinator.NotifyAngledHeadersChanged();

            if (shouldRestoreMouse && (BetterWorkTabMod.Settings?.restoreCursorOnSubWorkExit ?? true))
            {
                NativeCursorPosition.ScheduleMoveToUiPosition(returnMousePosition);
            }

            UISoundCompat.TickLow.PlayOneShotOnCamera();
        }
    }
}
