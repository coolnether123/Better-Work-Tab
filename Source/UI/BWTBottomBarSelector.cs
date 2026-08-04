using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// The two-line control the Work tab's bottom bar uses for its
    /// "what is currently selected" pickers.
    ///
    /// One line of text could not carry both things the player needs to know.
    /// "BWT Default (Rule Builder 2.0)" never said what kind of thing it was,
    /// and at the old width the button edge cut it mid-word, so it did not
    /// reliably say its own name either. Giving the caption a line of its own
    /// names the control permanently and leaves the value free to be truncated
    /// on purpose, with the untruncated text in the tooltip.
    /// </summary>
    internal static class BWTBottomBarSelector
    {
        // Two lines of text need room for two lines of text. The first attempt
        // squeezed them into 34px with a 13px caption row, and Unity clipped the
        // caps off "Workload" and "Ruleset" — the caption was unreadable at the
        // one size it is ever drawn at.
        internal const float Height = 42f;
        internal const float MenuWidth = 30f;

        internal const float MinValueWidth = 130f;
        internal const float MaxValueWidth = 250f;

        private const float SidePadding = 8f;
        private const float CaptionTop = 3f;
        private const float CaptionHeight = 16f;
        private const float ValueTop = 18f;
        private const float ValueHeight = 21f;

        private static readonly Color CaptionColor = new Color(1f, 1f, 1f, 0.52f);
        private static readonly Color EmptyValueColor = new Color(1f, 1f, 1f, 0.55f);

        /// <summary>
        /// Draws the naming half of a picker. <paramref name="hasValue"/> is false
        /// when nothing is selected, which dims the value line so an invitation
        /// ("Create one") does not read as the name of an existing thing.
        /// </summary>
        internal static bool DrawMain(Rect rect, string caption, string value, bool hasValue, string tooltip)
        {
            bool clicked = Widgets.ButtonText(rect, string.Empty);

            TextAnchor previousAnchor = Text.Anchor;
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            float x = rect.x + SidePadding;
            float width = rect.width - (SidePadding * 2f);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Tiny;
            GUI.color = CaptionColor;
            Widgets.Label(new Rect(x, rect.y + CaptionTop, width, CaptionHeight), caption);

            Text.Font = GameFont.Small;
            GUI.color = hasValue ? Color.white : EmptyValueColor;
            Widgets.Label(new Rect(x, rect.y + ValueTop, width, ValueHeight), value.Truncate(width));

            GUI.color = previousColor;
            Text.Font = previousFont;
            Text.Anchor = previousAnchor;

            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return clicked;
        }

        /// <summary>
        /// How wide the naming half has to be for <paramref name="value"/> to fit.
        ///
        /// A fixed width guaranteed that some ruleset name would be cut off, and
        /// the bottom bar has horizontal room to spare — the only thing to its
        /// left is a hint line that already shortens itself. The clamp keeps a
        /// short name from looking lost and a long one from crossing the tab.
        /// </summary>
        internal static float MeasureWidth(string value)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float text = Text.CalcSize(value ?? string.Empty).x;
            Text.Font = previousFont;
            return Mathf.Clamp(text + (SidePadding * 2f), MinValueWidth, MaxValueWidth);
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
