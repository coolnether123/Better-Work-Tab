using Better_Work_Tab;
using RimWorld;
using Spine.UI.SettingsFramework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Entry point for drawing the Better Work Tab settings window with simple and advanced views.
    /// </summary>
    public static class BetterWorkTabSettingsUI
    {
        private static Vector2 _scrollPosition;
        private static readonly QuickSearchWidget _searchWidget = new QuickSearchWidget();
        private static string _searchQuery = string.Empty;
        private static bool _favoritesInitialized;
        private const float HeaderHeight = 30f;
        private static bool IsSearchActiveInAdvanced => !string.IsNullOrWhiteSpace(_searchQuery);

        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings settings)
        {
            SettingsRegistry.EnsureInitialized();
            EnsureFavoritesInitialized();

            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            DrawHeader(headerRect, settings);

            Rect contentRect = new Rect(
                inRect.x,
                headerRect.yMax + 10f,
                inRect.width,
                inRect.height - headerRect.height - 10f);

            if (settings.settingsViewMode == BetterWorkTabSettings.SettingsViewMode.Simple)
            {
                DrawSimpleView(contentRect, settings);
            }
            else
            {
                DrawAdvancedView(contentRect, settings);
            }
        }

        private static void EnsureFavoritesInitialized()
        {
            if (_favoritesInitialized)
            {
                return;
            }

            FavoritesManager.Instance.Initialize(GenFilePaths.ConfigFolderPath);
            _favoritesInitialized = true;
        }

        private static void DrawHeader(Rect rect, BetterWorkTabSettings settings)
        {
            Rect searchRect = new Rect(rect.x, rect.y, rect.width * 0.5f, rect.height);
            _searchWidget.OnGUI(searchRect, () => { });
            _searchQuery = _searchWidget.filter.Text ?? string.Empty;

            Rect toggleRect = new Rect(rect.xMax - 200f, rect.y, 200f, rect.height);
            Rect simpleBtn = new Rect(toggleRect.x, toggleRect.y, 95f, toggleRect.height);
            Rect advancedBtn = new Rect(simpleBtn.xMax + 10f, toggleRect.y, 95f, toggleRect.height);

            bool isSimple = settings.settingsViewMode == BetterWorkTabSettings.SettingsViewMode.Simple;

            GUI.color = isSimple ? Color.white : Color.gray;
            if (Widgets.ButtonText(simpleBtn, SettingsTranslation.Simple))
            {
                settings.settingsViewMode = BetterWorkTabSettings.SettingsViewMode.Simple;
            }

            GUI.color = !isSimple ? Color.white : Color.gray;
            if (Widgets.ButtonText(advancedBtn, SettingsTranslation.Advanced))
            {
                settings.settingsViewMode = BetterWorkTabSettings.SettingsViewMode.Advanced;
            }

            GUI.color = Color.white;
        }

        private static void DrawSimpleView(Rect rect, BetterWorkTabSettings settings)
        {
            IEnumerable<SettingDefinition> settingsToShow;
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                settingsToShow = SettingsRegistry.Search(_searchQuery)
                    .Where(s => s.ShowInSimpleView);
            }
            else
            {
                settingsToShow = SettingsRegistry.GetForSimpleView();
            }

            float rowHeight = 32f;
            float headerHeight = 40f;
            float totalHeight = 0f;
            string lastCategory = null;

            var settingsList = settingsToShow.ToList();
            foreach (var setting in settingsList)
            {
                if (setting.CategoryId != lastCategory)
                {
                    totalHeight += headerHeight;
                    lastCategory = setting.CategoryId;
                }
                totalHeight += rowHeight;
            }

            if (settingsList.Count == 0)
            {
                Widgets.Label(rect, SettingsTranslation.NoResults);
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, totalHeight + 20f);
            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float curY = 0f;
            lastCategory = null;

            foreach (var setting in settingsList)
            {
                if (setting.CategoryId != lastCategory)
                {
                    lastCategory = setting.CategoryId;
                    var category = SettingsRegistry.Categories.FirstOrDefault(c => c.Id == lastCategory);
                    if (category != null)
                    {
                        Rect headerRect = new Rect(0f, curY, viewRect.width, headerHeight);
                        DrawCategoryHeader(headerRect, category);
                        curY += headerHeight;
                    }
                }

                Rect rowRect = new Rect(10f, curY, viewRect.width - 20f, rowHeight);
                SettingRenderer.Draw(rowRect, setting, settings, showFavoriteStar: false, drawHoverOutline: true);
                curY += rowHeight;
            }

            Widgets.EndScrollView();
        }

        private static void DrawCategoryHeader(Rect rect, SettingsCategoryDefinition category)
        {
            Rect lineRect = new Rect(rect.x, rect.y + rect.height - 4f, rect.width, 2f);
            Widgets.DrawBoxSolid(lineRect, category.HeaderColor);

            var oldFont = Text.Font;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = category.HeaderColor;

            Rect labelRect = new Rect(rect.x, rect.y + 8f, rect.width, rect.height - 12f);
            Widgets.Label(labelRect, SettingsTranslation.GetCategoryLabel(category));

            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        private static void DrawAdvancedView(Rect rect, BetterWorkTabSettings settings)
        {
            if (IsSearchActiveInAdvanced)
            {
                DrawAdvancedSearchResults(rect, settings);
                return;
            }

            var favorites = FavoritesManager.Instance.GetAllFavorites();
            float favoritesHeight = 0f;

            if (favorites.Count > 0)
            {
                favoritesHeight = DrawFavoritesSection(new Rect(rect.x, rect.y, rect.width, 200f), settings, favorites);
            }

            Rect gridRect = new Rect(
                rect.x,
                rect.y + favoritesHeight + 10f,
                rect.width,
                rect.height - favoritesHeight - 10f);

            DrawCategoryGrid(gridRect, settings);
        }

        private static void DrawAdvancedSearchResults(Rect rect, BetterWorkTabSettings settings)
        {
            var searchResults = SettingsRegistry.Search(_searchQuery)
                .Where(s => s.ShowInAdvancedView)
                .ToList();

            float rowHeight = 32f;
            float headerHeight = 40f;
            float totalHeight = 0f;
            string lastCategory = null;

            foreach (var setting in searchResults)
            {
                if (setting.CategoryId != lastCategory)
                {
                    totalHeight += headerHeight;
                    lastCategory = setting.CategoryId;
                }
                totalHeight += rowHeight;
            }

            if (searchResults.Count == 0)
            {
                Widgets.Label(rect, SettingsTranslation.NoResults);
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, totalHeight + 20f);
            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float curY = 0f;
            lastCategory = null;

            foreach (var setting in searchResults)
            {
                if (setting.CategoryId != lastCategory)
                {
                    lastCategory = setting.CategoryId;
                    var category = SettingsRegistry.Categories.FirstOrDefault(c => c.Id == lastCategory);
                    if (category != null)
                    {
                        Rect headerRect = new Rect(0f, curY, viewRect.width, headerHeight);
                        DrawCategoryHeader(headerRect, category);
                        curY += headerHeight;
                    }
                }

                Rect rowRect = new Rect(10f, curY, viewRect.width - 20f, rowHeight);
                SettingRenderer.Draw(rowRect, setting, settings, showFavoriteStar: true, drawHoverOutline: true);
                curY += rowHeight;
            }

            // Include debug feature toggles when searching for debug while logging is enabled
            if (settings.enableDebugLogging && _searchQuery.Trim().ToLowerInvariant().Contains("debug"))
            {
                settings.EnsureDebugFeatureTogglesInitialized();
                curY += 10f;
                Rect headerRect = new Rect(0f, curY, viewRect.width, headerHeight);
                Widgets.Label(headerRect, "Debug Features");
                curY += headerHeight;

                foreach (DebugFeature feature in System.Enum.GetValues(typeof(DebugFeature)))
                {
                    bool enabled = settings.debugFeatureToggles.TryGetValue(feature, out var val) && val;
                    Rect rowRect = new Rect(10f, curY, viewRect.width - 20f, rowHeight);
                    Widgets.CheckboxLabeled(rowRect, $"Debug: {feature}", ref enabled);
                    settings.debugFeatureToggles[feature] = enabled;
                    curY += rowHeight;
                }
            }

            Widgets.EndScrollView();
        }

        private static float DrawFavoritesSection(Rect rect, BetterWorkTabSettings settings, IReadOnlyCollection<string> favoriteIds)
        {
            float rowHeight = 32f;
            float headerHeight = 30f;
            float contentHeight = headerHeight + (favoriteIds.Count * rowHeight);

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
            var oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.85f, 0.2f);
            Widgets.Label(headerRect, SettingsTranslation.PinnedSettings);
            Text.Font = oldFont;
            GUI.color = Color.white;

            float curY = rect.y + headerHeight;
            foreach (var favId in favoriteIds)
            {
                var def = SettingsRegistry.Settings.FirstOrDefault(s => s.Id == favId);
                if (def == null || !def.ShowInAdvancedView)
                {
                    continue;
                }

                Rect rowRect = new Rect(rect.x, curY, rect.width, rowHeight);
                SettingRenderer.Draw(rowRect, def, settings, showFavoriteStar: true, drawHoverOutline: false);
                curY += rowHeight;
            }

            Widgets.DrawLineHorizontal(rect.x, curY + 5f, rect.width);
            return contentHeight + 15f;
        }

        private static void DrawCategoryGrid(Rect rect, BetterWorkTabSettings settings)
        {
            var categories = SettingsRegistry.Categories.OrderBy(c => c.SortOrder).ToList();

            int columns = 2;
            float buttonWidth = (rect.width - 10f) / columns;
            float buttonHeight = 70f;

            int index = 0;
            foreach (var category in categories)
            {
                int col = index % columns;
                int row = index / columns;

                Rect buttonRect = new Rect(
                    rect.x + col * (buttonWidth + 10f),
                    rect.y + row * (buttonHeight + 10f),
                    buttonWidth,
                    buttonHeight);

                if (DrawCategoryButton(buttonRect, category))
                {
                    Find.WindowStack.Add(new Dialog_SettingsCategory(
                        category.Id,
                        SettingsTranslation.GetCategoryLabel(category),
                        (listing, s) => DrawCategorySettings(listing, s, category.Id)));
                }

                index++;
            }
        }

        private static bool DrawCategoryButton(Rect rect, SettingsCategoryDefinition category)
        {
            bool hovered = Mouse.IsOver(rect);
            Color baseColor = new Color(0.18f, 0.18f, 0.18f, 0.9f);
            Color bgColor = hovered ? Color.Lerp(baseColor, category.HeaderColor, 0.3f) : baseColor;

            Widgets.DrawBoxSolid(rect, bgColor);
            Widgets.DrawBox(rect, 1);

            Rect labelRect = rect.ContractedBy(8f);
            Rect descRect = labelRect;
            descRect.y += rect.height * 0.45f;
            descRect.height = rect.height * 0.45f;

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(labelRect, SettingsTranslation.GetCategoryLabel(category));

            string description = SettingsTranslation.GetCategoryDescription(category);
            if (!string.IsNullOrEmpty(description))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                Widgets.Label(descRect, description);
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            if (hovered && !string.IsNullOrEmpty(description))
            {
                TooltipHandler.TipRegion(rect, description);
            }

            return Widgets.ButtonInvisible(rect);
        }

        private static void DrawCategorySettings(Listing_Standard listing, BetterWorkTabSettings settings, string categoryId)
        {
            if (categoryId == "advanced" && settings.enableDebugLogging)
            {
                settings.EnsureDebugFeatureTogglesInitialized();
            }

            foreach (var def in SettingsRegistry.GetByCategory(categoryId))
            {
                if (!def.ShowInAdvancedView)
                {
                    continue;
                }

                if (def.VisibleWhen != null && !def.VisibleWhen(settings))
                {
                    continue;
                }

                Rect rowRect = listing.GetRect(32f);
                SettingRenderer.Draw(rowRect, def, settings, showFavoriteStar: true, drawHoverOutline: false);
            }

            // Additional debug feature toggles when debug logging is enabled
            if (categoryId == "advanced" && settings.enableDebugLogging)
            {
                listing.GapLine();
                listing.Label("Debug Features:");

                foreach (DebugFeature feature in System.Enum.GetValues(typeof(DebugFeature)))
                {
                    bool enabled = settings.debugFeatureToggles.TryGetValue(feature, out var val) && val;
                    Rect row = listing.GetRect(24f);
                    Widgets.CheckboxLabeled(row, $"Debug: {feature}", ref enabled);
                    settings.debugFeatureToggles[feature] = enabled;
                }
            }
        }
    }
}
