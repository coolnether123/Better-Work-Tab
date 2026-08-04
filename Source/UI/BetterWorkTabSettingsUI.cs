using System.Linq;
using Better_Work_Tab;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Entry point for rendering Better Work Tab mod settings using the shared drawer.
    /// </summary>
    public static class BetterWorkTabSettingsUI
    {
        private enum SettingsPage
        {
            Settings,
            Controls
        }

        private static SettingsListDrawer _settingsDrawer;
        private static SettingsListDrawer _controlsDrawer;
        // Compatibility alias for the agent harness and older integrations that reflect the
        // single pre-page drawer field. It always points at the currently active page drawer.
        private static SettingsListDrawer _drawer;
        private static SettingsPage _page;
        private static SettingsViewMode _viewMode = SettingsViewMode.Simple;
        private static Vector2 _preservedSettingsScrollPosition = Vector2.zero;
        private static Vector2 _preservedControlsScrollPosition = Vector2.zero;

        /// <summary>
        /// Renders the settings window contents.
        /// </summary>
        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings settings)
        {
            EnsureDrawerInitialized();
            DrawPageTabs(inRect);
            FluffyWorkTabGateway.DrawSettingsBannerIfNeeded(ref inRect);

            _viewMode = settings.settingsViewMode == BetterWorkTabSettings.SettingsViewMode.Simple
                ? SettingsViewMode.Simple
                : SettingsViewMode.Advanced;

            SettingsListDrawer drawer = _page == SettingsPage.Controls ? _controlsDrawer : _settingsDrawer;
            _drawer = drawer;
            drawer.ShowResetIcons = !settings.hideSettingResetIcons;
            drawer.FocusHighlightColor = settings.Color_SettingFocusHighlight;
            drawer.ImportExportActions = BWTSettingsImportExportActions.Create(settings, NotifySettingsChanged);
            if (BWTSettingsContextFocus.TryConsume(out BWTSettingsFocusRequest focusRequest))
            {
                SettingDefinition target = BWTSettingsRegistry.Hierarchy.GetById(focusRequest.TargetSettingId);
                _page = BWTSettingsFilters.IsControlSetting(target)
                    ? SettingsPage.Controls
                    : SettingsPage.Settings;
                drawer = _page == SettingsPage.Controls ? _controlsDrawer : _settingsDrawer;
                _drawer = drawer;
                drawer.ApplyContextFilter(
                    BWTSettingsContextFocus.CreateFilter(focusRequest),
                    focusRequest.TargetSettingId);
            }

            drawer.Draw(inRect, settings, ref _viewMode, () => settings.Write());
            BWTGeneralTutorial.DrawSettingsGesture(
                settingId => drawer.TryGetVisibleSettingScreenRect(settingId, out Rect screenRect)
                    ? (Rect?)ScreenToGuiRect(screenRect)
                    : null);

            settings.settingsViewMode = _viewMode == SettingsViewMode.Simple
                ? BetterWorkTabSettings.SettingsViewMode.Simple
                : BetterWorkTabSettings.SettingsViewMode.Advanced;
        }

        public static void NotifySettingsChanged()
        {
            // Preserve scroll position before destroying drawer
            if (_settingsDrawer != null)
            {
                _preservedSettingsScrollPosition = _settingsDrawer.ScrollPosition;
            }
            if (_controlsDrawer != null)
            {
                _preservedControlsScrollPosition = _controlsDrawer.ScrollPosition;
            }
            _settingsDrawer = null;
            _controlsDrawer = null;
            _drawer = null;
        }

        /// <summary>
        /// Lazily builds the drawer with Better Work Tab specific translators.
        /// </summary>
        private static void EnsureDrawerInitialized()
        {
            if (_settingsDrawer != null && _controlsDrawer != null)
            {
                return;
            }

            BWTSettingsRegistry.EnsureInitialized();
            _settingsDrawer = CreateDrawer(
                new SettingsHierarchy(BWTSettingsRegistry.Definitions.Where(
                    definition => !BWTSettingsFilters.IsControlSetting(definition))),
                _preservedSettingsScrollPosition);
            _controlsDrawer = CreateDrawer(
                new SettingsHierarchy(BWTSettingsRegistry.Definitions.Where(
                    BWTSettingsFilters.IsControlsPageDefinition)),
                _preservedControlsScrollPosition);
            _drawer = _page == SettingsPage.Controls ? _controlsDrawer : _settingsDrawer;
        }

        private static SettingsListDrawer CreateDrawer(
            SettingsHierarchy hierarchy,
            Vector2 scrollPosition)
        {
            return new SettingsListDrawer(hierarchy)
            {
                GetLabel = BWTSettingsTranslation.GetLabel,
                GetTooltip = BWTSettingsTranslation.GetTooltip,
                SimpleLabel = BWTSettingsTranslation.Simple,
                AdvancedLabel = BWTSettingsTranslation.Advanced,
                NoResultsLabel = BWTSettingsTranslation.NoResults,
                EditColorLabel = BWTSettingsTranslation.Edit,
                ColorPreviewTooltip = "Hover here or adjust the picker to preview this color live on the Work tab.",
                ColorPreviewSink = WorkTabColorPreviewController.Instance,
                Filters = BWTSettingsFilters.Create(),
                FilterLabel = "Filter",
                AllSettingsFilterLabel = "All Settings",
                IndentPerLevel = 20f,
                RowHeight = 32f,
                ScrollPosition = scrollPosition,
                OnSettingTooltipViewed = MarkSettingViewed,
                OnSettingInteracted = (definition, _) =>
                    BWTGeneralTutorial.NotifySettingsRowInteracted(definition?.Id)
            };
        }

        private static Rect ScreenToGuiRect(Rect screenRect)
        {
            Vector2 position = GUIUtility.ScreenToGUIPoint(screenRect.position);
            return new Rect(position.x, position.y, screenRect.width, screenRect.height);
        }

        private static void DrawPageTabs(Rect inRect)
        {
            const float height = 30f;
            const float gap = 8f;
            const float titleGap = 18f;
            const float rightInset = 44f;

            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Medium;
            float titleWidth = Text.CalcSize("Better Work Tab").x;
            Text.Font = previousFont;

            float tabsX = inRect.x + titleWidth + titleGap;
            float availableWidth = Mathf.Max(0f, inRect.xMax - rightInset - tabsX);
            float width = Mathf.Min(150f, Mathf.Max(0f, (availableWidth - gap) / 2f));
            Rect tabs = new Rect(tabsX, inRect.y - height - 8f, availableWidth, height);
            Rect settingsRect = new Rect(tabs.x, tabs.y, width, height);
            Rect controlsRect = new Rect(settingsRect.xMax + gap, tabs.y, width, height);

            Color previous = GUI.color;
            GUI.color = _page == SettingsPage.Settings ? Color.white : Color.gray;
            if (Widgets.ButtonText(settingsRect, "BWT_Settings_UI_Settings".Translate()))
            {
                _page = SettingsPage.Settings;
                _drawer = _settingsDrawer;
            }
            GUI.color = _page == SettingsPage.Controls ? Color.white : Color.gray;
            if (Widgets.ButtonText(controlsRect, "BWT_Settings_UI_Controls".Translate()))
            {
                _page = SettingsPage.Controls;
                _drawer = _controlsDrawer;
            }
            GUI.color = previous;
        }

        private static void MarkSettingViewed(SettingDefinition def, object settingsObject)
        {
            if (def == null || !(settingsObject is BetterWorkTabSettings settings))
            {
                return;
            }

            if (settings.RecordViewedSetting(def.Id))
            {
                settings.Write();
            }
        }
    }
}
