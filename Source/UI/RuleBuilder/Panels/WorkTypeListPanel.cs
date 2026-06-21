using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RWWidgets = Better_Work_Tab.WidgetsCompat;

namespace Better_Work_Tab.UI.RuleBuilder.Panels
{
    /// <summary>
    /// Column 1: Displays all work types in a searchable list.
    /// Shows badges indicating how many rules are configured for each.
    /// Clicking selects the work type and updates column 2.
    /// </summary>
    public class WorkTypeListPanel
    {
        private Vector2 _scrollPosition;
        private readonly QuickSearchWidget _searchWidget = new QuickSearchWidget();
        private List<WorkTypeDef> _filteredWorkTypes;
        private string _lastSearchText = "";

        private const float SearchBarHeight = 28f;
        private const float HeaderHeight = 32f;
        private const float RowHeight = 28f;

        /// <summary>
        /// Draws the work type list panel.
        /// </summary>
        public void Draw(Rect rect, RuleBuilderState state)
        {
            RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.PanelBackgroundLight);
            RWWidgets.DrawBox(rect, 1);

            Rect innerRect = rect.ContractedBy(4f);

            // Header
            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, HeaderHeight);
            DrawHeader(headerRect, "BWT_WorkTypes".Translate());

            // Search
            Rect searchRect = new Rect(innerRect.x, headerRect.yMax + 4f, innerRect.width, SearchBarHeight);
            DrawSearchBar(searchRect, state);

            // List
            Rect listRect = new Rect(
                innerRect.x,
                searchRect.yMax + 4f,
                innerRect.width,
                innerRect.height - headerRect.height - searchRect.height - 12f);
            DrawList(listRect, state);
        }

        private void DrawHeader(Rect rect, string text)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(rect, text);

            Rect lineRect = new Rect(rect.x, rect.yMax - 2f, rect.width, 1f);
            RWWidgets.DrawBoxSolid(lineRect, RuleBuilderConstants.HeaderColor * 0.5f);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawSearchBar(Rect rect, RuleBuilderState state)
        {
            _searchWidget.OnGUI(rect);

            if (_searchWidget.filter.Text != _lastSearchText)
            {
                _lastSearchText = _searchWidget.filter.Text;
                state.WorkTypeSearchFilter = _lastSearchText;
                _filteredWorkTypes = null;
            }
        }

        private void DrawList(Rect rect, RuleBuilderState state)
        {
            RefreshFilteredList(state);

            if (_filteredWorkTypes == null || !_filteredWorkTypes.Any())
            {
                GUI.color = RuleBuilderConstants.SubtleTextColor;
                Text.Anchor = TextAnchor.MiddleCenter;
            RWWidgets.Label(rect, "BWT_NoMatchingWorkTypes".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float contentHeight = _filteredWorkTypes.Count * RowHeight;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);

            RWWidgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float yPos = 0f;
            var configuredTypes = state.ConfiguredWorkTypes;

            foreach (var workType in _filteredWorkTypes)
            {
                Rect rowRect = new Rect(0f, yPos, viewRect.width, RowHeight);

                bool isSelected = state.SelectedWorkType == workType;
                bool isConfigured = configuredTypes.Contains(workType);
                int ruleCount = state.GetTotalRuleCount(workType);

                DrawWorkTypeRow(rowRect, workType, isSelected, isConfigured, ruleCount, state);

                yPos += RowHeight;
            }

            RWWidgets.EndScrollView();
        }

        private void DrawWorkTypeRow(
            Rect rect,
            WorkTypeDef workType,
            bool isSelected,
            bool isConfigured,
            int ruleCount,
            RuleBuilderState state)
        {
            // Background
            bool isDragging = state.DragController.IsDragging;
            bool isDragHover = isDragging && Mouse.IsOver(rect);
            
            if (isSelected)
            {
                RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.CardBackgroundSelected);
            }
            else if (isDragHover)
            {
                // Special highlight when dragging over
                RWWidgets.DrawBoxSolid(rect, new Color(0.3f, 0.5f, 0.7f, 0.4f));
                GUI.color = new Color(0.5f, 0.7f, 1f, 0.8f);
                RWWidgets.DrawBox(rect, 2);
                GUI.color = Color.white;
            }
            else if (Mouse.IsOver(rect))
            {
                RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.CardBackgroundHover);
            }

            // Configured indicator bar
            if (isConfigured)
            {
                Rect indicatorRect = new Rect(rect.x, rect.y + 2f, 3f, rect.height - 4f);
                RWWidgets.DrawBoxSolid(indicatorRect, RuleBuilderConstants.SuccessColor);
            }

            // Label
            Rect labelRect = new Rect(rect.x + 8f, rect.y, rect.width - 40f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = isConfigured ? Color.white : RuleBuilderConstants.LabelColor;
            RWWidgets.Label(labelRect, Better_Work_Tab.WorkTypeCompat.LabelShort(workType).CapitalizeFirst());

            // Rule count badge
            if (ruleCount > 0)
            {
                Rect badgeRect = new Rect(rect.xMax - 24f, rect.y + 4f, 20f, rect.height - 8f);
                RWWidgets.DrawBoxSolid(badgeRect, RuleBuilderConstants.SuccessColor);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                GUI.color = Color.white;
                RWWidgets.Label(badgeRect, ruleCount.ToString());
                Text.Font = GameFont.Small;
            }

            // Hover switch for drag
            if (state.DragController.IsDragging && Mouse.IsOver(rect))
            {
                state.DragController.NotifyWorkTypeHover(workType);
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Click handler
            if (RWWidgets.ButtonInvisible(rect))
            {
                state.SelectedWorkType = workType;
            }

            // Tooltip
            TooltipHandler.TipRegion(rect, BuildTooltip(workType, ruleCount));
        }

        private void RefreshFilteredList(RuleBuilderState state)
        {
            if (_filteredWorkTypes != null)
                return;

            var all = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .OrderByDescending(wt => wt.naturalPriority)
                .ToList();

            if (string.IsNullOrEmpty(state.WorkTypeSearchFilter))
            {
                _filteredWorkTypes = all;
            }
            else
            {
                string lower = state.WorkTypeSearchFilter.ToLowerInvariant();
                _filteredWorkTypes = all
                    .Where(wt => Better_Work_Tab.WorkTypeCompat.LabelShort(wt).ToLowerInvariant().Contains(lower) ||
                                 wt.defName.ToLowerInvariant().Contains(lower))
                    .ToList();
            }
        }

        private string BuildTooltip(WorkTypeDef workType, int ruleCount)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>{Better_Work_Tab.WorkTypeCompat.LabelShort(workType).CapitalizeFirst()}</b>");

            if (!string.IsNullOrEmpty(workType.description))
            {
                sb.AppendLine(workType.description);
            }

            sb.AppendLine();

            if (ruleCount > 0)
            {
                sb.AppendLine($"<color=#66CC66>{"BWT_RulesConfigured".Translate(ruleCount)}</color>");
            }
            else
            {
                sb.AppendLine($"<color=#999999>{"BWT_NoRulesConfigured".Translate()}</color>");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Invalidates the cached work type list.
        /// Call when search filter changes.
        /// </summary>
        public void InvalidateCache()
        {
            _filteredWorkTypes = null;
        }
    }
}
