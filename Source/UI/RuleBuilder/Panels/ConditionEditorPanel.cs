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
            // Draw drop zone highlight when dragging
            bool isDragging = state.DragController.IsDragging;
            bool isValidDropTarget = isDragging && !state.IsRulesetReadOnly && 
                                     state.SelectedWorkType != null && state.SelectedPriority >= 0;
            
            if (isDragging && Mouse.IsOver(rect))
            {
                // Highlight the entire panel as drop zone
                Color highlightColor = isValidDropTarget 
                    ? new Color(0.3f, 0.6f, 0.4f, 0.3f)  // Green tint for valid
                    : new Color(0.6f, 0.3f, 0.3f, 0.2f); // Red tint for invalid
                RWWidgets.DrawBoxSolid(rect, highlightColor);
                
                // Draw prominent border
                GUI.color = isValidDropTarget 
                    ? new Color(0.4f, 0.8f, 0.5f, 0.9f) 
                    : new Color(0.8f, 0.4f, 0.4f, 0.6f);
                RWWidgets.DrawBox(rect, 3);
                GUI.color = Color.white;
            }
            else
            {
                RWWidgets.DrawBoxSolid(rect, RuleBuilderConstants.PanelBackgroundLight);
                RWWidgets.DrawBox(rect, 1);
            }

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

            // Draw "Drop here" overlay when dragging
            if (isDragging && Mouse.IsOver(rect) && isValidDropTarget)
            {
                DrawDropOverlay(rect);
            }

            HandleDrop(rect, state);
        }

        private void DrawDropOverlay(Rect rect)
        {
            // Draw semi-transparent overlay with "Drop here" text
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.9f, 1f, 0.9f, 0.9f);
            
            Rect labelRect = new Rect(rect.x, rect.yMax - 40f, rect.width, 30f);
            string dropText = "BWT_DropHere".CanTranslate() ? "BWT_DropHere".Translate() : "\u2193 Drop here to duplicate \u2193";
            Verse.Widgets.Label(labelRect, dropText);
            
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void HandleDrop(Rect rect, RuleBuilderState state)
        {
            if (state.DragController.IsDragging &&
                Event.current.type == EventType.MouseUp &&
                Mouse.IsOver(rect))
            {
                if (!state.IsRulesetReadOnly && state.SelectedWorkType != null && state.SelectedPriority >= 0)
                {
                    // Perform the paste
                    var dragged = state.DragController.DraggedRules;
                    foreach (var rule in dragged)
                    {
                        var copy = rule.Copy();
                        copy.Parameters.Worktype = state.SelectedWorkType;
                        copy.Parameters.WorktypeString = state.SelectedWorkType.defName;
                        copy.Parameters.Priority = state.SelectedPriority;
                        state.SelectedRuleset.Rules.Add(copy);
                    }
                    state.NotifyRulesModified();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                state.DragController.EndDrag();
                Event.current.Use();
            }
        }

        private void DrawHeader(Rect rect, RuleBuilderState state)
        {
            // Collapse chain when selected priority changes to mimic initial view
            if (state.SelectedPriority != _lastPriority)
            {
                _expandedChain = false;
                _lastPriority = state.SelectedPriority;
            }

            // Layout: [Title + Chain] [Drag Handle]
            float handleWidth = 60f;
            float handleHeight = rect.height - 8f;
            
            // Drag handle on the right
            Rect handleRect = new Rect(
                rect.xMax - handleWidth - 4f, 
                rect.y + 4f, 
                handleWidth, 
                handleHeight);
            
            // Title and chain on the left
            Rect leftRect = new Rect(rect.x, rect.y, rect.width - handleWidth - 12f, rect.height);
            
            // Title line
            Rect titleRect = new Rect(leftRect.x, leftRect.y, leftRect.width, 28f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = RuleBuilderConstants.HeaderColor;
            RWWidgets.Label(titleRect, "BWT_ConditionsFor".Translate());

            // Work type + priority chain (collapsible)
            Rect chainRect = new Rect(leftRect.x, titleRect.yMax, leftRect.width, 24f);
            _expandedChain = PriorityChainWidget.Draw(chainRect, state, _expandedChain);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // Draw the drag handle (only if we have rules)
            var rules = state.CurrentRules;
            state.DragController.DrawDragHandle(handleRect, rules, state.SelectedWorkType, state.SelectedPriority);
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
