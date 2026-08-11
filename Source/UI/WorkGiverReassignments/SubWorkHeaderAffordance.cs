using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Shared immediate-mode geometry, hit testing, and drawing for the sub-work back button.
    /// </summary>
    internal static class SubWorkHeaderAffordance
    {
        private const float BackBadgeSize = 18f;
        internal static bool DebugForceBackButtonHover;

        internal static Rect GetBackBadgeRect(Rect labelCellRect)
        {
            return new Rect(
                Mathf.Round(labelCellRect.x + 7f),
                Mathf.Round(labelCellRect.center.y - (BackBadgeSize / 2f)),
                BackBadgeSize,
                BackBadgeSize);
        }

        internal static void DrawBackBadge(Rect labelCellRect)
        {
            Rect badgeRect = GetBackBadgeRect(labelCellRect);
            DrawBackArrowBadge(badgeRect, IsBackButtonHovered(labelCellRect));
        }

        internal static bool IsBackButtonHovered(Rect labelCellRect)
        {
            return Mouse.IsOver(labelCellRect) || DebugForceBackButtonHover;
        }

        internal static bool TryGetBackLabelCellRect(IWorkTabLayoutController layout, out Rect labelCellRect)
        {
            labelCellRect = default;
            if (layout?.Columns == null || layout.Table == null)
            {
                return false;
            }

            Rect globalRowRect = WorkGridLayoutMetrics.GetSubWorkBandRect(layout);
            if (globalRowRect == Rect.zero)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_Label))
                {
                    continue;
                }

                labelCellRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(
                    column,
                    globalRowRect);
                return true;
            }

            return false;
        }

        private static void DrawBackArrowBadge(Rect rect, bool hovered)
        {
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            try
            {
                GUI.color = hovered
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.96f);
                GUIUtility.RotateAroundPivot(180f, rect.center);
                GUI.DrawTexture(rect, TexButton.Reveal);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }
    }
}
