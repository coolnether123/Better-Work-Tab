using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// The control the Work tab's bottom bar uses for its "what is currently
    /// selected" pickers.
    ///
    /// One line of plain text could not carry both things the player needs to
    /// know: "BWT Default (Rule Builder 2.0)" never said what kind of thing it
    /// was, and at a fixed 150px the button edge cut it mid-word, so it did not
    /// reliably say its own name either.
    ///
    /// A caption line was the first attempt at the first half of that, and it
    /// cost a second row of height for two words that never change. The compact
    /// control keeps a reserved leading slot so the name has the same geometry
    /// and the whole width is sized to fit rather than truncated.
    /// </summary>
    internal static class BWTBottomBarSelector
    {
        internal const float Height = 30f;
        internal const float MenuWidth = 30f;

        // Keep the leading blank slot stable so removing the former glyphs does
        // not move the selector text or change the bottom-bar geometry.
        private const float LeadingSlotSize = 24f;
        private const float SidePadding = 6f;
        private const float LeadingTextGap = 7f;

        private const float MinTextWidth = 96f;
        private const float MaxTextWidth = 240f;

        private static readonly Color EmptyValueColor = new Color(1f, 1f, 1f, 0.6f);

        /// <summary>
        /// How wide the naming half has to be to hold <paramref name="value"/>.
        /// The clamp keeps a short name from looking lost and a long one from
        /// crossing the tab.
        /// </summary>
        internal static float MeasureWidth(string value)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float text = Text.CalcSize(value ?? string.Empty).x;
            Text.Font = previousFont;
            return Mathf.Clamp(text, MinTextWidth, MaxTextWidth) +
                   (SidePadding * 2f) + LeadingSlotSize + LeadingTextGap;
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

            float textX = rect.x + SidePadding + LeadingSlotSize + LeadingTextGap;
            float textWidth = rect.xMax - SidePadding - textX;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            GUI.color = hasValue ? Color.white : EmptyValueColor;
            Widgets.Label(new Rect(textX, rect.y, textWidth, rect.height), value.Truncate(textWidth));

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
