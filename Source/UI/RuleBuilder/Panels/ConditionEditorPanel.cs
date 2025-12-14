using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using Better_Work_Tab.UI.RuleBuilder.Widgets;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using RWWidgets = Verse.Widgets;

namespace Better_Work_Tab.UI.RuleBuilder.Panels
{
    /// <summary>
    /// Column 3: Displays and edits condition sets (rules) for the selected
    /// (WorkType, Priority) pair. Each condition set is a group of conditions
    /// ANDed together that result in the pawn being assigned this priority.
    /// </summary>
    public class ConditionEditorPanel
    {
        private Vector2 _scrollPosition;
        private readonly RuleMatchCalculator _matchCalculator = new RuleMatchCalculator();

        private const float HeaderHeight = 48f;
        private const float AddButtonHeight = 32f;
        private const float ConditionSetMinHeight = 80f;
        private const float ConditionSetSpacing = 8f;
        private bool _expandedChain;
        private int _lastPriority = -1;

        /// <summary>
        /// Draws the condition editor panel.
        /// </summary>
        public void Draw(Rect rect, RuleBuilderState state)
        {
            RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.PanelBackgroundLight);
            RWWidgets.DrawBox(rect, 1);

            Rect innerRect = rect.ContractedBy(8f);

            if (state.SelectedWorkType == null || state.SelectedPriority < 0)
            {
                DrawEmptyState(innerRect, "BWT_SelectPriorityFirst".Translate());
                return;
            }

            // Header
            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, HeaderHeight);
            DrawHeader(headerRect, state);

            // Add button (at bottom)
            bool isReadOnly = state.IsRulesetReadOnly;
            Rect addButtonRect = new Rect(
                innerRect.x,
                innerRect.yMax - AddButtonHeight,
                innerRect.width,
                AddButtonHeight);

            if (!isReadOnly)
            {
                DrawAddButton(addButtonRect, state);
            }

            // Condition sets list
            Rect listRect = new Rect(
                innerRect.x,
                headerRect.yMax + 8f,
                innerRect.width,
                innerRect.height - headerRect.height - (isReadOnly ? 8f : AddButtonHeight + 16f));

            DrawConditionSetsList(listRect, state);
        }

        private void DrawHeader(Rect rect, RuleBuilderState state)
        {
            // Collapse chain when selected priority changes to mimic initial view
            // (only show up to the active priority until explicitly expanded).
            // This keeps the header concise while editing.
            // The chain will expand only when user clicks the "more" segment.
            if (state.SelectedPriority != _lastPriority)
            {
                _expandedChain = false;
                _lastPriority = state.SelectedPriority;
            }

            // Title line
            Rect titleRect = new Rect(rect.x, rect.y, rect.width, 28f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(titleRect, "BWT_ConditionsFor".Translate());

            // Work type + priority chain (collapsible, supports dynamic priorities)
            Rect chainRect = new Rect(rect.x, titleRect.yMax, rect.width, 24f);
            _expandedChain = PriorityChainWidget.Draw(chainRect, state, _expandedChain);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawConditionSetsList(Rect rect, RuleBuilderState state)
        {
            var rules = state.CurrentRules;

            if (!rules.Any())
            {
                DrawNoConditionsState(rect, state);
                return;
            }

            // Calculate content height
            float contentHeight = 0f;
            var pawns = GetCurrentPawns();

            foreach (var rule in rules)
            {
                contentHeight += CalculateConditionSetHeight(rule, state.IsRulesetReadOnly);
                contentHeight += ConditionSetSpacing;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);

            RWWidgets.BeginScrollView(rect, ref _scrollPosition, viewRect);

            float yPos = 0f;
            WorkAssignmentRule ruleToDelete = null;

            foreach (var rule in rules)
            {
                float setHeight = CalculateConditionSetHeight(rule, state.IsRulesetReadOnly);
                Rect setRect = new Rect(0f, yPos, viewRect.width, setHeight);

                int matchCount = _matchCalculator.CountMatches(rule, state.SelectedWorkType, pawns);

                var action = ConditionSetCard.Draw(
                    setRect,
                    rule,
                    matchCount,
                    state.IsRulesetReadOnly,
                    state);

                if (action == ConditionSetCard.CardAction.Delete)
                {
                    ruleToDelete = rule;
                }

                yPos += setHeight + ConditionSetSpacing;
            }

            RWWidgets.EndScrollView();

            // Handle deletion outside of loop
            if (ruleToDelete != null)
            {
                state.DeleteRule(ruleToDelete);
            }
        }

        private void DrawNoConditionsState(Rect rect, RuleBuilderState state)
        {
            RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.CardBackground * 0.8f);

            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;
            var oldFont = Text.Font;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = RuleBuilderConstants.SubtleTextColor;

            Rect messageRect = new Rect(rect.x, rect.y + rect.height * 0.3f, rect.width, 40f);
            RWWidgets.Label(messageRect, "BWT_NoConditionsSet".Translate());

            Text.Font = GameFont.Tiny;
            Rect instructionRect = new Rect(rect.x, messageRect.yMax + 8f, rect.width, 30f);
            GUI.color = RuleBuilderConstants.SubtleTextColor * 0.7f;
            RWWidgets.Label(instructionRect, "BWT_AddConditionInstructions".Translate());

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private void DrawAddButton(Rect rect, RuleBuilderState state)
        {
            var oldColor = GUI.color;

            if (RWWidgets.ButtonText(rect, "+ " + "BWT_AddConditionSet".Translate()))
            {
                CreateNewRule(state);
            }

            GUI.color = oldColor;

            TooltipHandler.TipRegion(rect, "BWT_AddConditionSet_Tooltip".Translate());
        }

        private float CalculateConditionSetHeight(WorkAssignmentRule rule, bool isReadOnly)
        {
            var conditions = ConditionRegistry.GetActiveConditions(rule.Parameters);
            int rows = conditions.Count > 0 ? conditions.Count : 1; // empty state takes one row

            float height = NameHeightPadding() + rows * (WidgetsRowHeight() + WidgetsRowSpacing()) + 10f; // padding

            if (!isReadOnly)
            {
                height += 28f; // add button row
            }

            return Mathf.Max(height, ConditionSetMinHeight);
        }

        private float WidgetsRowHeight() => 28f;
        private float WidgetsRowSpacing() => 2f;
        private float NameHeightPadding() => 24f + 6f; // header + small gap
        

        private void CreateNewRule(RuleBuilderState state)
        {
            if (state.SelectedWorkType == null || state.SelectedPriority < 0)
                return;

            var newRule = state.CreateRule();
            if (newRule != null)
            {
                state.SelectedRule = newRule;
            }
        }

        private List<Pawn> GetCurrentPawns()
        {
            var map = Find.CurrentMap;
            return map?.mapPawns?.FreeColonists?.ToList() ?? new List<Pawn>();
        }

        private void DrawEmptyState(Rect rect, string message)
        {
            GUI.color = RuleBuilderConstants.SubtleTextColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            RWWidgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }
}
