using System.Collections.Generic;
using UnityEngine;
using Verse;
#if vAlpha4
using Widgets = Better_Work_Tab.Widgets;
#endif

namespace Spine.DragDropApi.Util
{
    /// <summary>
    /// Rendering helpers for drag visuals: ghost rect and insertion line.
    /// Uses RimWorld / Unity drawing APIs.
    /// </summary>
    public static class ListDragVisuals
    {
        /// <summary>
        /// Returns a rect for the drag ghost, centered on the mouse.
        /// </summary>
        public static Rect GetGhostRect(Vector2 mousePos, float width, float height)
        {
            return new Rect(
                mousePos.x - (width * 0.5f),
                mousePos.y - (height * 0.5f),
                width,
                height
            );
        }

        /// <summary>
        /// Returns the screen-space Y position for the insertion line,
        /// based on insertion index and item heights.
        /// </summary>
        public static float GetInsertionLineY(
            int insertionIndex,
            IList<float> itemHeights,
            float listScreenY,
            float scrollOffsetY)
        {
            float y = listScreenY;

            if (itemHeights != null)
            {
                int count = Mathf.Min(insertionIndex, itemHeights.Count);
                for (int i = 0; i < count; i++)
                {
                    y += itemHeights[i];
                }
            }

            return y - scrollOffsetY;
        }

        /// <summary>
        /// Draws a semi-transparent ghost rect with a label.
        /// </summary>
        public static void DrawGhost(Rect rect, string label, float alpha = 0.25f)
        {
            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;

            GUI.color = new Color(1f, 1f, 1f, 1f);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(rect, new Color(0f, 0f, 0f, alpha));
            Widgets.DrawBox(rect, 1);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect.ContractedBy(4f), label ?? string.Empty);

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        /// <summary>
        /// Draws a 2-pixel insertion line at the given X/Y with the given width.
        /// </summary>
        public static void DrawInsertionLine(float screenX, float screenY, float width)
        {
            Rect lineRect = new Rect(screenX, screenY - 1f, width, 2f);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(lineRect, Color.white);
        }
    }
}
