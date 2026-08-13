using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Input;
using Better_Work_Tab.UI.WorkGrid.Layout;
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

        internal static void Draw(IWorkTabLayoutController layout)
        {
            if (!SubWorkDrilldownState.HasAnyDrilldown || layout == null)
            {
                return;
            }

            Rect reservedBand = WorkGridLayoutMetrics.GetSubWorkBandRect(layout);
            if (reservedBand.height <= 0.5f)
            {
                SubWorkCrossWorkDropTargetRenderer.DrawSettleAnimation(layout);
                return;
            }

            float rowTop = reservedBand.y;
            Rect rowRect = new Rect(
                layout.TableOrigin.x,
                rowTop,
                Mathf.Max(layout.Table != null ? layout.Table.Size.x - 16f : 0f, 1f),
                reservedBand.height);

            float visualAlpha = SubWorkDrilldownState.GlobalRowVisualAlpha;
            float alpha = 0.72f * visualAlpha;
            Widgets.DrawBoxSolid(rowRect, new Color(0.08f, 0.1f, 0.11f, alpha));
            Rect separatorRect = new Rect(rowRect.xMin, rowRect.yMax - 3f, rowRect.width, 1f);
            WorkTabDiagnostics.RecordSubWorkSeparator(separatorRect);
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.28f * visualAlpha);
            Widgets.DrawLineHorizontal(separatorRect.xMin, separatorRect.yMin, separatorRect.width);
            GUI.color = oldColor;

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                Rect cellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(
                    column,
                    rowRect);

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

                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                        column,
                        out var workGiver,
                        out WorkTypeDef parentWorkType,
                        out _))
                {
                    DrawGlobalPriorityCell(workGiver, parentWorkType, cellRect, visualAlpha);
                }
            }

            SubWorkCrossWorkDropTargetRenderer.DrawSettleAnimation(layout);
        }

        private static bool ShouldHighlightGlobalCell(WorkTabLayoutColumn column, Rect cellRect)
        {
            var settings = BetterWorkTabMod.Settings;
            if (settings == null ||
                !settings.ShowPawnAndWorktypeHighlights ||
                !settings.ShowCursorPawnAndWorktypeHighlight ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition)
            {
                return false;
            }

            WorkTypeDef workType = column.SubWorkParent ?? column.Column?.workType;
            if (workType == null)
            {
                return false;
            }

            var hoveredWorkType = PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType;
            return hoveredWorkType == workType ||
                   Mouse.IsOver(WorkGridInteractionGeometry.GetAnimatedHeaderRect(column)) ||
                   Mouse.IsOver(cellRect);
        }

        private static void DrawLabelCell(Rect rect)
        {
            var oldAnchor = Text.Anchor;
            var oldFont = Text.Font;
            var oldColor = GUI.color;

            bool hovered = SubWorkHeaderAffordance.IsBackButtonHovered(rect);
            Rect buttonRect = rect.ContractedBy(1f);
            Color frameColor = hovered
                ? new Color(1f, 1f, 1f, 0.88f)
                : new Color(1f, 1f, 1f, 0.48f);
            Color innerFrameColor = hovered
                ? new Color(1f, 1f, 1f, 0.24f)
                : new Color(1f, 1f, 1f, 0.13f);
            Color fillColor = hovered
                ? new Color(1f, 1f, 1f, 0.18f)
                : new Color(1f, 1f, 1f, 0.09f);
            Widgets.DrawBoxSolid(buttonRect, fillColor);
            GUI.color = frameColor;
            Widgets.DrawBox(buttonRect);
            GUI.color = innerFrameColor;
            Widgets.DrawBox(buttonRect.ContractedBy(1f));
            GUI.color = oldColor;

            if (hovered)
            {
                Widgets.DrawHighlight(rect);
            }

            SubWorkHeaderAffordance.DrawBackBadge(rect);
            TooltipHandler.TipRegion(rect, "Back to work types. Escape also works.");
            MouseoverSounds.DoRegion(rect);

            Rect labelRect = new Rect(
                rect.x + 30f,
                rect.y,
                Mathf.Max(0f, rect.width - 38f),
                rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = hovered ? Color.white : new Color(1f, 1f, 1f, 0.96f);

            string label = SubWorkDrilldownState.ActiveWorkType != null
                ? WorkTypeDisplayNameService.HeaderLabel(SubWorkDrilldownState.ActiveWorkType) + " global"
                : "Specific jobs";
            Widgets.Label(labelRect, label);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private static void DrawGlobalPriorityCell(
            WorkGiver workGiver,
            WorkTypeDef parentWorkType,
            Rect cellRect,
            float visualAlpha)
        {
            if (parentWorkType == null)
            {
                return;
            }

            float boxSize = Mathf.Min(SubWorkDrilldownState.GlobalPriorityBoxSize, Mathf.Max(0f, cellRect.height - 4f));
            if (boxSize <= 6f)
            {
                return;
            }

            Rect boxRect = WorkPriorityCellGeometry.GetCenteredBoxRect(cellRect, boxSize);
            WorkGiverPriorityBoxRenderer.DrawPriorityBox(
                workGiver,
                parentWorkType,
                null,
                boxRect,
                visualAlpha);
        }

        internal static void ExitDrilldown(bool restoreMousePosition = false)
        {
            TimePriorityScheduleEditor.CloseForWorkModeTransition();
            Vector2 returnMousePosition = Vector2.zero;
            string cursorRestoreSuppression = null;
            bool settingAllowsRestore = BetterWorkTabMod.Settings?.restoreCursorOnSubWorkExit ?? true;
            bool shouldRestoreMouse = restoreMousePosition &&
                settingAllowsRestore &&
                SubWorkDrilldownState.TryGetCursorRestorePosition(out returnMousePosition, out cursorRestoreSuppression);

            if (restoreMousePosition)
            {
                BetterWorkTabMod.DebugLog(
                    shouldRestoreMouse
                        ? $"[SubWorkDrilldown] Cursor restore scheduled to {returnMousePosition}."
                        : $"[SubWorkDrilldown] Cursor restore suppressed: {(settingAllowsRestore ? cursorRestoreSuppression : "setting disabled")}.",
                    DebugFeature.SubWork);
            }

            SubWorkDrilldownState.Exit();
            WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.Columns | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);

            if (shouldRestoreMouse)
            {
                NativeCursorPosition.ScheduleMoveToUiPosition(returnMousePosition);
            }

            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
    }
}
