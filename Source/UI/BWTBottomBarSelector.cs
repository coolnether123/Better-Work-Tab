using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// The two-line control the Work tab's bottom bar uses for its current
    /// workload and ruleset selections.
    ///
    /// The caption names the kind of selection and the value line names the
    /// current selection, using RimWorld's ordinary text-button treatment.
    /// </summary>
    internal static class BWTBottomBarSelector
    {
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

        internal static bool DrawMain(
            Rect rect,
            string caption,
            string value,
            bool hasValue,
            string tooltip)
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

        internal static float MeasureWidth(string value)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            float text = Text.CalcSize(value ?? string.Empty).x;
            Text.Font = previousFont;
            return Mathf.Clamp(text + (SidePadding * 2f), MinValueWidth, MaxValueWidth);
        }

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
