using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers;
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
            Widgets.DrawBoxSolid(rowRect, new Color(0.08f, 0.1f, 0.11f, alpha));
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

                if (SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out var workGiver, out _))
                {
                    DrawGlobalPriorityCell(workGiver, cellRect);
                }
            }
        }

        private static void DrawLabelCell(Rect rect)
        {
            Rect buttonRect = new Rect(rect.x + 5f, rect.y + 4f, Mathf.Min(28f, rect.width - 8f), RowHeight - 8f);
            bool canDrawButton = buttonRect.width >= 18f;
            if (canDrawButton)
            {
                if (Widgets.ButtonText(buttonRect, "<"))
                {
                    ExitDrilldown();
                }

                TooltipHandler.TipRegion(buttonRect, "Back to work types");
            }

            Rect labelRect = new Rect(
                canDrawButton ? buttonRect.xMax + 6f : rect.x + 5f,
                rect.y,
                Mathf.Max(0f, rect.xMax - (canDrawButton ? buttonRect.xMax + 10f : rect.x + 10f)),
                RowHeight);

            var oldAnchor = Text.Anchor;
            var oldFont = Text.Font;
            var oldColor = GUI.color;
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
        }

        private static void DrawGlobalPriorityCell(WorkGiver workGiver, Rect cellRect)
        {
            const float boxSize = 25f;
            float x = cellRect.x + (cellRect.width - boxSize) / 2f;
            float y = cellRect.y + (cellRect.height - boxSize) / 2f;
            Rect boxRect = new Rect(x, y, boxSize, boxSize);
            WorkGiverPriorityBoxRenderer.DrawPriorityBox(workGiver, SubWorkDrilldownState.ActiveWorkType, null, boxRect);
        }

        internal static void ExitDrilldown()
        {
            SubWorkDrilldownState.Exit();
            HeaderDrawingCoordinator.NotifyAngledHeadersChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
    }
}
