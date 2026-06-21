using Better_Work_Tab.UI.RuleBuilder.State;
using Better_Work_Tab.UI.RuleBuilder.Widgets;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Panels
{
    /// <summary>
    /// Step 1: Visual grid for selecting which work type to configure.
    /// Shows all available work types with indicators for configured rules.
    /// Supports search filtering and category grouping.
    /// </summary>
    public class WorkTypeSelectorPanel : IRuleBuilderPanel
    {
        public RuleBuilderStep AssociatedStep => RuleBuilderStep.SelectWorkType;

        private Vector2 _scrollPosition;
        private readonly QuickSearchWidget _searchWidget = new QuickSearchWidget();
        private List<WorkTypeDef> _cachedWorkTypes;
        private string _lastSearchText = "";

        private const float SearchBarHeight = 28f;
        private const float FilterToggleHeight = 24f;
        private const float HeaderHeight = 40f;

        public void OnActivate(RuleBuilderState state)
        {
            _cachedWorkTypes = null;
            _scrollPosition = Vector2.zero;
        }

        public void OnDeactivate(RuleBuilderState state)
        {
        }

        public void Draw(Rect rect, RuleBuilderState state)
        {
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            DrawHeader(headerRect);

            Rect searchRect = new Rect(
                rect.x + RuleBuilderConstants.PanelPadding,
                headerRect.yMax + 4f,
                rect.width - RuleBuilderConstants.PanelPadding * 2,
                SearchBarHeight);
            DrawSearchBar(searchRect, state);

            Rect filterRect = new Rect(
                rect.x + RuleBuilderConstants.PanelPadding,
                searchRect.yMax + 4f,
                rect.width - RuleBuilderConstants.PanelPadding * 2,
                FilterToggleHeight);
            DrawFilterToggle(filterRect, state);

            Rect gridRect = new Rect(
                rect.x,
                filterRect.yMax + 8f,
                rect.width,
                rect.height - (filterRect.yMax - rect.y) - 8f);
            DrawWorkTypeGrid(gridRect, state);
        }

        private void DrawHeader(Rect rect)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            GUI.color = RuleBuilderConstants.HeaderColor;
            Text.Anchor = TextAnchor.MiddleLeft;

            Rect titleRect = new Rect(
                rect.x + RuleBuilderConstants.PanelPadding,
                rect.y,
                rect.width * 0.6f,
                rect.height);
            Verse.Widgets.Label(titleRect, "BWT_Step1_Title".Translate());

            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Rect subtitleRect = new Rect(
                titleRect.xMax,
                rect.y,
                rect.width - titleRect.width - RuleBuilderConstants.PanelPadding,
                rect.height);
            Verse.Widgets.Label(subtitleRect, "BWT_Step1_Subtitle".Translate());

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private void DrawSearchBar(Rect rect, RuleBuilderState state)
        {
            _searchWidget.OnGUI(rect);

            if (_searchWidget.filter.Text != _lastSearchText)
            {
                _lastSearchText = _searchWidget.filter.Text;
                state.WorkTypeSearchFilter = _lastSearchText;
                _cachedWorkTypes = null;
            }
        }

        private void DrawFilterToggle(Rect rect, RuleBuilderState state)
        {
            bool showOnlyConfigured = state.ShowOnlyConfiguredWorkTypes;
            int configuredCount = state.ConfiguredWorkTypes.Count;

            string label = "BWT_ShowOnlyConfigured".Translate(configuredCount);
            Better_Work_Tab.WidgetsCompat.CheckboxLabeled(rect, label, ref showOnlyConfigured);

            if (showOnlyConfigured != state.ShowOnlyConfiguredWorkTypes)
            {
                state.ShowOnlyConfiguredWorkTypes = showOnlyConfigured;
                _cachedWorkTypes = null;
            }

            TooltipHandler.TipRegion(rect, "BWT_ShowOnlyConfigured_Tooltip".Translate());
        }

        private void DrawWorkTypeGrid(Rect rect, RuleBuilderState state)
        {
            var workTypes = GetFilteredWorkTypes(state);

            if (!workTypes.Any())
            {
                DrawEmptyState(rect, state);
                return;
            }

            int columns = Mathf.Max(1, Mathf.FloorToInt(
                (rect.width - RuleBuilderConstants.PanelPadding * 2) /
                (RuleBuilderConstants.WorkTypeCardWidth + RuleBuilderConstants.CardSpacing)));

            int rows = Mathf.CeilToInt((float)workTypes.Count / columns);
            float contentHeight = rows * (RuleBuilderConstants.WorkTypeCardHeight + RuleBuilderConstants.CardSpacing);

            Rect viewRect = new Rect(0f, 0f, rect.width - 20f, contentHeight);
            Verse.Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            int index = 0;
            foreach (var workType in workTypes)
            {
                int col = index % columns;
                int row = index / columns;

                Rect cardRect = new Rect(
                    RuleBuilderConstants.PanelPadding + col * (RuleBuilderConstants.WorkTypeCardWidth + RuleBuilderConstants.CardSpacing),
                    row * (RuleBuilderConstants.WorkTypeCardHeight + RuleBuilderConstants.CardSpacing),
                    RuleBuilderConstants.WorkTypeCardWidth,
                    RuleBuilderConstants.WorkTypeCardHeight);

                bool isConfigured = state.ConfiguredWorkTypes.Contains(workType);
                int ruleCount = CountRulesForWorkType(state, workType);

                if (WorkTypeCard.Draw(cardRect, workType, isConfigured, ruleCount))
                {
                    state.SelectedWorkType = workType;
                    state.NavigateTo(RuleBuilderStep.EditRules);
                }

                index++;
            }

            Verse.Widgets.EndScrollView();
        }

        private void DrawEmptyState(Rect rect, RuleBuilderState state)
        {
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;

            string message = state.ShowOnlyConfiguredWorkTypes
                ? "BWT_NoConfiguredWorkTypes".Translate()
                : "BWT_NoMatchingWorkTypes".Translate();

            Verse.Widgets.Label(rect, message);

            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private List<WorkTypeDef> GetFilteredWorkTypes(RuleBuilderState state)
        {
            if (_cachedWorkTypes != null)
                return _cachedWorkTypes;

            var allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .OrderByDescending(wt => wt.naturalPriority)
                .ToList();

            _cachedWorkTypes = allWorkTypes
                .Where(wt => MatchesSearchFilter(wt, state.WorkTypeSearchFilter))
                .Where(wt => !state.ShowOnlyConfiguredWorkTypes || state.ConfiguredWorkTypes.Contains(wt))
                .ToList();

            return _cachedWorkTypes;
        }

        private bool MatchesSearchFilter(WorkTypeDef workType, string filter)
        {
            if (string.IsNullOrEmpty(filter))
                return true;

            string lowerFilter = filter.ToLowerInvariant();
            return workType.labelShort.ToLowerInvariant().Contains(lowerFilter) ||
                   workType.defName.ToLowerInvariant().Contains(lowerFilter);
        }

        private int CountRulesForWorkType(RuleBuilderState state, WorkTypeDef workType)
        {
            if (state.SelectedRuleset?.Rules == null)
                return 0;

            return state.SelectedRuleset.Rules
                .Count(r => state.RuleAppliesToWorkType(r, workType));
        }

        public float GetMinimumHeight(RuleBuilderState state)
        {
            var workTypes = GetFilteredWorkTypes(state);
            int columns = 4;
            int rows = Mathf.CeilToInt((float)workTypes.Count / columns);

            return HeaderHeight + SearchBarHeight + FilterToggleHeight +
                   rows * (RuleBuilderConstants.WorkTypeCardHeight + RuleBuilderConstants.CardSpacing) +
                   100f;
        }
    }
}
