using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>Draws the Work tab's bottom-bar selectors.</summary>
    internal static class BWTBottomBarSelector
    {
        internal const float Height = 30f;
        internal const float MenuWidth = 30f;

        private const float SidePadding = 6f;

        private const float MaxTextWidth = 240f;

        private static readonly Color EmptyValueColor = new Color(1f, 1f, 1f, 0.6f);

        /// <summary>
        /// How wide the naming half has to be to hold <paramref name="value"/>.
        /// The upper clamp keeps a long name from crossing the tab.
        /// </summary>
        internal static float MeasureWidth(string value)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float text = Text.CalcSize(value ?? string.Empty).x;
            Text.Font = previousFont;
            return Mathf.Min(text, MaxTextWidth) +
                   (SidePadding * 2f);
        }

        /// <summary>
        /// Draws the naming half of a picker. <paramref name="hasValue"/> is false
        /// when nothing is selected, which dims the text so an invitation
        /// ("Save current priorities") does not read as the name of a saved thing.
        /// </summary>
        internal static bool DrawMain(Rect rect, string value, bool hasValue, string tooltip)
        {
            bool clicked = Widgets.ButtonText(rect, string.Empty);

            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            float textX = rect.x + SidePadding;
            float textWidth = Mathf.Max(0f, rect.xMax - SidePadding - textX);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            GUI.color = hasValue ? Color.white : EmptyValueColor;
            Rect labelRect = new Rect(textX, rect.y, textWidth, rect.height);
            GUI.BeginGroup(labelRect);
            Widgets.Label(new Rect(0f, 0f, textWidth, rect.height), value ?? string.Empty);
            GUI.EndGroup();

            Text.WordWrap = previousWrap;
            GUI.color = previousColor;
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;

            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return clicked;
        }

        /// <summary>Draws the list half of a picker: the square that opens the menu.</summary>
        internal static bool DrawMenu(Rect rect, string tooltip)
        {
            bool clicked = Widgets.ButtonText(rect, "...");
            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return clicked;
        }
    }
}
