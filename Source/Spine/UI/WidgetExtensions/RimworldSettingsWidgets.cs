using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Spine.UI.WidgetExtensions
{
    /// <summary>
    /// Reusable settings UI widgets. RimWorld-dependent but generic.
    /// </summary>
    public static class RimworldSettingsWidgets
    {
        private const float CategoryButtonHeight = 60f;
        private const float CategoryButtonSpacing = 8f;

        private static readonly Color CategoryButtonColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        private static readonly Color CategoryButtonHoverColor = new Color(0.3f, 0.3f, 0.3f, 0.9f);

        /// <summary>
        /// Draws a large category navigation button.
        /// </summary>
        public static bool DrawCategoryButton(Rect rect, string label, string description, bool hasSettings = true)
        {
            bool hovered = Mouse.IsOver(rect);
            Color bgColor = hovered ? CategoryButtonHoverColor : CategoryButtonColor;
            
            Widgets.DrawBoxSolid(rect, bgColor);
            Widgets.DrawBox(rect, 1, hovered ? Texture2D.whiteTexture : null);

            Rect labelRect = rect.ContractedBy(8f);
            labelRect.height = rect.height * 0.5f;

            Rect descRect = rect.ContractedBy(8f);
            descRect.y += rect.height * 0.45f;
            descRect.height = rect.height * 0.45f;

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = hasSettings ? Color.white : Color.gray;
            Widgets.Label(labelRect, label);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(descRect, description);

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            if (hovered)
            {
                Rect arrowRect = new Rect(rect.xMax - 24f, rect.y + (rect.height - 16f) / 2f, 16f, 16f);
                GUI.color = Color.white;
                Widgets.Label(arrowRect, "▶");
                GUI.color = oldColor;
            }

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// Draws a section header with separator line.
        /// </summary>
        public static void SectionHeader(Listing_Standard listing, string label)
        {
            listing.Gap(8f);
            
            var oldFont = Text.Font;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.85f, 0.7f);
            listing.Label(label);
            
            Text.Font = oldFont;
            GUI.color = oldColor;

            listing.GapLine(4f);
        }

        /// <summary>
        /// Draws a grid of category buttons.
        /// </summary>
        public static string DrawCategoryGrid(
            Rect rect,
            IEnumerable<(string id, string label, string desc)> categories,
            int columns = 2)
        {
            string clicked = null;
            float buttonWidth = (rect.width - (columns - 1) * CategoryButtonSpacing) / columns;
            
            int index = 0;
            foreach (var (id, label, desc) in categories)
            {
                int col = index % columns;
                int row = index / columns;

                Rect buttonRect = new Rect(
                    rect.x + col * (buttonWidth + CategoryButtonSpacing),
                    rect.y + row * (CategoryButtonHeight + CategoryButtonSpacing),
                    buttonWidth,
                    CategoryButtonHeight);

                if (DrawCategoryButton(buttonRect, label, desc))
                {
                    clicked = id;
                }

                index++;
            }

            return clicked;
        }

        /// <summary>
        /// Draws a back button for sub-windows.
        /// </summary>
        public static bool DrawBackButton(Rect rect)
        {
            var oldFont = Text.Font;
            Text.Font = GameFont.Small;

            Rect buttonRect = new Rect(rect.x, rect.y, 80f, 28f);
            bool clicked = Widgets.ButtonText(buttonRect, "◀ Back");

            Text.Font = oldFont;
            return clicked;
        }
    }
}
