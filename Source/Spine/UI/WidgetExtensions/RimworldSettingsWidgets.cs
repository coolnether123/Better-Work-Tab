using RimWorld;
using Spine.UI.ColourPicker;
using Spine.UI.SettingsFramework;
using System;
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
        private const float FavoriteStarSize = 18f;

        private static readonly Color CategoryButtonColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        private static readonly Color CategoryButtonHoverColor = new Color(0.3f, 0.3f, 0.3f, 0.9f);
        private static readonly Color FavoriteStarActive = new Color(1f, 0.85f, 0.2f);
        private static readonly Color FavoriteStarInactive = new Color(0.5f, 0.5f, 0.5f, 0.5f);

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
        /// Draws a favorite star toggle button.
        /// </summary>
        public static bool DrawFavoriteStar(Rect rect, bool isFavorite, string tooltip = null)
        {
            Color starColor = isFavorite ? FavoriteStarActive : FavoriteStarInactive;

            var oldColor = GUI.color;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;

            GUI.color = starColor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            string starChar = isFavorite ? "★" : "☆";
            Widgets.Label(rect, starChar);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            bool clicked = Widgets.ButtonInvisible(rect);
            if (clicked)
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            return clicked;
        }

        /// <summary>
        /// Draws a setting row with an optional favorite star.
        /// </summary>
        public static Rect DrawSettingRowStart(Listing_Standard listing, string settingId, float height = 24f)
        {
            Rect row = listing.GetRect(height);
            
            if (!string.IsNullOrEmpty(settingId))
            {
                Rect starRect = new Rect(row.x, row.y + (row.height - FavoriteStarSize) / 2f, 
                    FavoriteStarSize, FavoriteStarSize);
                
                bool isFav = FavoritesManager.Instance.IsFavorite(settingId);
                if (DrawFavoriteStar(starRect, isFav, isFav ? "Remove from favorites" : "Add to favorites"))
                {
                    FavoritesManager.Instance.ToggleFavorite(settingId);
                }

                row.xMin += FavoriteStarSize + 4f;
            }

            return row;
        }

        /// <summary>
        /// Draws a checkbox with favorite support.
        /// </summary>
        public static void CheckboxFavoritable(
            Listing_Standard listing,
            string settingId,
            string label,
            ref bool value,
            string tooltip = null,
            bool disabled = false)
        {
            Rect row = DrawSettingRowStart(listing, settingId);

            if (disabled) GUI.color = Color.gray;

            bool oldValue = value;
            Widgets.CheckboxLabeled(row, label, ref value, disabled);
            
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(row, tooltip);
            }

            if (disabled) GUI.color = Color.white;
        }

        /// <summary>
        /// Draws a slider with favorite support.
        /// </summary>
        public static float SliderFavoritable(
            Listing_Standard listing,
            string settingId,
            string label,
            float value,
            float min,
            float max,
            string tooltip = null,
            float labelWidth = 150f)
        {
            Rect row = DrawSettingRowStart(listing, settingId);

            Rect labelRect = new Rect(row.x, row.y, labelWidth, row.height);
            Rect sliderRect = new Rect(row.x + labelWidth + 4f, row.y, row.width - labelWidth - 4f, row.height);

            Widgets.Label(labelRect, label);
            float result = Widgets.HorizontalSlider(sliderRect, value, min, max, true);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(row, tooltip);
            }

            return result;
        }

        /// <summary>
        /// Draws a color picker button with favorite support.
        /// </summary>
        public static void ColorPickerFavoritable(
            Listing_Standard listing,
            string settingId,
            string label,
            ref Color color,
            string tooltip = null)
        {
            Rect row = DrawSettingRowStart(listing, settingId, 32f);

            Rect colorRect = new Rect(row.x, row.y + 2f, 28f, 28f);
            Rect labelRect = new Rect(row.x + 36f, row.y, row.width - 136f, row.height);
            Rect buttonRect = new Rect(row.xMax - 90f, row.y + 2f, 90f, row.height - 4f);

            Widgets.DrawBoxSolid(colorRect, color);
            Widgets.DrawBox(colorRect, 1);

            Widgets.Label(labelRect, label);

            Color localColor = color;
            if (Widgets.ButtonText(buttonRect, "Choose..."))
            {
                Find.WindowStack.Add(new Dialog_ColourPicker(localColor, (newColor, _) =>
                {
                    localColor = newColor;
                }));
            }
            color = localColor;

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(row, tooltip);
            }
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
