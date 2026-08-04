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
    /// cost a second row of height for two words that never change. An icon says
    /// the same thing in the space the text was already leaving empty, so the
    /// control is back to one line and the name gets the whole width — sized to
    /// fit rather than truncated, because the bottom bar has room to spare and
    /// the only thing to its left is a hint line that already shortens itself.
    /// </summary>
    internal static class BWTBottomBarSelector
    {
        internal const float Height = 30f;
        internal const float MenuWidth = 30f;

        // The glyphs are 24px art. Drawing them at anything else resamples off
        // their own grid and the detail turns to mush — the check on the ruleset
        // icon disappears first. Native size, so the pixels land on pixels.
        private const float IconSize = 24f;
        private const float SidePadding = 6f;
        private const float IconTextGap = 7f;

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
                   (SidePadding * 2f) + IconSize + IconTextGap;
        }

        /// <summary>
        /// Draws the naming half of a picker. <paramref name="hasValue"/> is false
        /// when nothing is selected, which dims the text so an invitation
        /// ("Save current priorities") does not read as the name of a saved thing.
        /// </summary>
        internal static bool DrawMain(Rect rect, Texture2D icon, string value, bool hasValue, string tooltip)
        {
            bool clicked = Widgets.ButtonText(rect, string.Empty);

            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            if (icon != null)
            {
                GUI.DrawTexture(
                    new Rect(rect.x + SidePadding, rect.y + ((rect.height - IconSize) * 0.5f), IconSize, IconSize),
                    icon);
            }

            float textX = rect.x + SidePadding + IconSize + IconTextGap;
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
