using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using Better_Work_Tab.UI.RuleBuilder.Widgets;
using RimWorld;
using Spine.DragDropApi;
using Spine.DragDropApi.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Panels
{
    /// <summary>
    /// Step 2: Rule editor for the selected work type.
    /// Displays rules as human-readable condition to priority assignments.
    /// Supports adding, editing, reordering, and removing rules.
    /// </summary>
    public class RuleEditorPanel : IRuleBuilderPanel
    {
        public RuleBuilderStep AssociatedStep => RuleBuilderStep.EditRules;

        private Vector2 _ruleListScroll;
        private Vector2 _conditionScroll;
        private WorkAssignmentRule _editingRule;
        private string _priorityBuffer = string.Empty;

        private readonly RuleMatchCalculator _matchCalculator = new RuleMatchCalculator();

        private const float HeaderHeight = 50f;
        private const float RuleRowHeight = 44f;
        private const float ConditionEditorMinHeight = 200f;
        private const float ButtonRowHeight = 32f;

        private readonly DragDropController<WorkAssignmentRule> _ruleDragController;
        private Rect _rulesListScreenRect;
        private List<WorkAssignmentRule> _visibleRules = new List<WorkAssignmentRule>();
        private RuleBuilderState _state;

        public RuleEditorPanel()
        {
            _ruleDragController = new DragDropController<WorkAssignmentRule>(
                mousePos => CalculateRuleTargetIndex(mousePos)
            );
        }

        public void OnActivate(RuleBuilderState state)
        {
            _ruleListScroll = Vector2.zero;
            _conditionScroll = Vector2.zero;

            var rules = state.RulesForSelectedWorkType;
            if (rules.Any() && state.SelectedRule == null)
            {
                state.SelectedRule = rules.First();
            }

            _editingRule = state.SelectedRule;
            _priorityBuffer = state.SelectedRule?.Parameters?.Priority.ToString() ?? string.Empty;
        }

        public void OnDeactivate(RuleBuilderState state)
        {
            ValidateRules(state);
        }

        public void Draw(Rect rect, RuleBuilderState state)
        {
            _state = state;
            if (state.SelectedWorkType == null)
            {
                DrawNoWorkTypeSelected(rect);
                return;
            }

            Rect leftRect = new Rect(
                rect.x,
                rect.y,
                rect.width * 0.4f - 8f,
                rect.height);

            Rect rightRect = new Rect(
                leftRect.xMax + 16f,
                rect.y,
                rect.width * 0.6f - 8f,
                rect.height);

            DrawLeftColumn(leftRect, state);
            DrawRightColumn(rightRect, state);

            if (_ruleDragController.IsActive)
            {
                DrawRuleDragOverlay();
            }
        }

        private void DrawLeftColumn(Rect rect, RuleBuilderState state)
        {
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            DrawWorkTypeHeader(headerRect, state);

            Rect addButtonRect = new Rect(
                rect.x,
                headerRect.yMax + 8f,
                rect.width,
                ButtonRowHeight);
            DrawAddRuleButton(addButtonRect, state);

            Rect listRect = new Rect(
                rect.x,
                addButtonRect.yMax + 8f,
                rect.width,
                rect.height - (addButtonRect.yMax - rect.y) - 16f);
            DrawRuleList(listRect, state);
        }

        private void DrawWorkTypeHeader(Rect rect, RuleBuilderState state)
        {
            Verse.Widgets.DrawBoxSolid(rect, RuleBuilderConstants.PanelBackground);

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Rect backRect = new Rect(rect.x + 8f, rect.y + 8f, 70f, 28f);
            if (Verse.Widgets.ButtonText(backRect, "< " + "BWT_Back".Translate()))
            {
                state.NavigateBack();
            }

            Text.Font = GameFont.Medium;
            GUI.color = RuleBuilderConstants.HeaderColor;
            Text.Anchor = TextAnchor.MiddleLeft;

            Rect titleRect = new Rect(
                backRect.xMax + 12f,
                rect.y,
                rect.width - backRect.width - 24f,
                rect.height);

            string title = state.SelectedWorkType.labelShort.CapitalizeFirst();
            Verse.Widgets.Label(titleRect, title);

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            TooltipHandler.TipRegion(rect,
                "BWT_EditingRulesFor".Translate(state.SelectedWorkType.labelShort));
        }

        private void DrawAddRuleButton(Rect rect, RuleBuilderState state)
        {
            bool isDefault = state.SelectedRuleset?.IsDefault ?? false;

            var oldColor = GUI.color;
            if (isDefault)
            {
                GUI.color = RuleBuilderConstants.DisabledColor;
            }

            if (Verse.Widgets.ButtonText(rect, "+ " + "BWT_AddNewRule".Translate(), active: !isDefault))
            {
                CreateNewRule(state);
            }

            GUI.color = oldColor;

            if (isDefault)
            {
                TooltipHandler.TipRegion(rect, "BWT_CannotModifyDefault".Translate());
            }
        }

        private void DrawRuleList(Rect rect, RuleBuilderState state)
        {
            Verse.Widgets.DrawMenuSection(rect);

            _visibleRules = state.RulesForSelectedWorkType;

            if (!_visibleRules.Any())
            {
                DrawEmptyRuleList(rect);
                return;
            }

            Rect innerRect = rect.ContractedBy(4f);
            float contentHeight = _visibleRules.Count * RuleRowHeight;
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, contentHeight);

            _rulesListScreenRect = innerRect;

            Verse.Widgets.BeginScrollView(innerRect, ref _ruleListScroll, viewRect);

            float yPos = 0f;
            WorkAssignmentRule ruleToDelete = null;

            for (int i = 0; i < _visibleRules.Count; i++)
            {
                var rule = _visibleRules[i];
                Rect rowRect = new Rect(0f, yPos, viewRect.width, RuleRowHeight);

                int matchCount = _matchCalculator.CountMatches(
                    rule,
                    state.SelectedWorkType,
                    GetCurrentPawns());

                bool isSelected = state.SelectedRule == rule;
                var action = RuleRowWidget.Draw(
                    rowRect,
                    rule,
                    i + 1,
                    matchCount,
                    isSelected,
                    state.SelectedRuleset?.IsDefault ?? false);

                switch (action)
                {
                    case RuleRowWidget.RowAction.Select:
                        state.SelectedRule = rule;
                        _editingRule = rule;
                        _priorityBuffer = rule.Parameters?.Priority.ToString() ?? string.Empty;
                        break;
                    case RuleRowWidget.RowAction.Delete:
                        ruleToDelete = rule;
                        break;
                    case RuleRowWidget.RowAction.MoveUp:
                        MoveRule(state, rule, -1);
                        break;
                    case RuleRowWidget.RowAction.MoveDown:
                        MoveRule(state, rule, 1);
                        break;
                }

                yPos += RuleRowHeight;
            }

            Verse.Widgets.EndScrollView();

            HandleRuleDragInput();

            if (ruleToDelete != null)
            {
                DeleteRule(state, ruleToDelete);
            }
        }

        private void HandleRuleDragInput()
        {
            Event evt = Event.current;
            if (evt == null) return;

            if (_ruleDragController.IsActive)
            {
                _ruleDragController.UpdateDrag(evt.mousePosition);

                float contentHeight = _visibleRules.Count * RuleRowHeight;
                _ruleDragController.ApplyAutoScroll(
                    ref _ruleListScroll,
                    evt.mousePosition,
                    _rulesListScreenRect,
                    contentHeight,
                    Time.deltaTime);

                if (evt.type == EventType.MouseUp)
                {
                    FinalizeRuleDrop();
                    evt.Use();
                }

                return;
            }

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && _rulesListScreenRect.Contains(evt.mousePosition))
                    {
                        float localY = evt.mousePosition.y - _rulesListScreenRect.y + _ruleListScroll.y;
                        int index = Mathf.FloorToInt(localY / RuleRowHeight);

                        if (index >= 0 && index < _visibleRules.Count)
                        {
                            var pending = _visibleRules[index];
                            _ruleDragController.TryStartDrag(
                                pending,
                                _visibleRules.IndexOf(pending),
                                _visibleRules.Count,
                                evt.mousePosition);
                        }
                    }
                    break;
                case EventType.MouseUp:
                    if (_ruleDragController.IsActive)
                    {
                        FinalizeRuleDrop();
                        evt.Use();
                    }
                    break;
            }
        }

        private void DrawRuleDragOverlay()
        {
            var session = _ruleDragController.CurrentSession;
            if (session == null) return;

            float targetY = _rulesListScreenRect.y - _ruleListScroll.y + (session.TargetIndex * RuleRowHeight);

            if (targetY >= _rulesListScreenRect.y && targetY <= _rulesListScreenRect.yMax)
            {
                ListDragVisuals.DrawInsertionLine(
                    _rulesListScreenRect.x,
                    targetY,
                    _rulesListScreenRect.width);
            }
        }

        private void FinalizeRuleDrop()
        {
            var session = _ruleDragController.CurrentSession;
            if (session == null)
                return;

            var dragged = session.DraggedItem;
            if (dragged == null)
            {
                _ruleDragController.CancelDrag();
                return;
            }

            var sourceList = _state?.SelectedRuleset?.Rules;
            if (sourceList == null)
            {
                _ruleDragController.CancelDrag();
                return;
            }

            int sourceIndex = sourceList.IndexOf(dragged);
            if (sourceIndex < 0)
            {
                _ruleDragController.CancelDrag();
                return;
            }

            Func<int, int> visualToSourceMapping = (visualIdx) =>
            {
                if (visualIdx >= _visibleRules.Count)
                    return sourceList.Count;

                var targetItem = _visibleRules[visualIdx];
                int mainIdx = sourceList.IndexOf(targetItem);
                return mainIdx >= 0 ? mainIdx : sourceList.Count;
            };

            var reason = _ruleDragController.FinalizeDrag(sourceList, visualToSourceMapping);

            if (reason == DragEndReason.Success)
            {
                _state?.NotifyRulesModified();
                BetterWorkTabMod.Settings.Write();
            }
        }

        private void DrawEmptyRuleList(Rect rect)
        {
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;

            Verse.Widgets.Label(rect, "BWT_NoRulesForWorkType".Translate());

            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        private void DrawRightColumn(Rect rect, RuleBuilderState state)
        {
            if (state.SelectedRule == null)
            {
                DrawNoRuleSelected(rect);
                return;
            }

            Verse.Widgets.DrawMenuSection(rect);

            Rect nameRect = new Rect(
                rect.x + RuleBuilderConstants.PanelPadding,
                rect.y + RuleBuilderConstants.PanelPadding,
                rect.width - RuleBuilderConstants.PanelPadding * 2,
                ButtonRowHeight);
            DrawRuleNameEditor(nameRect, state);

            Rect priorityRect = new Rect(
                nameRect.x,
                nameRect.yMax + 12f,
                nameRect.width,
                ButtonRowHeight);
            DrawPrioritySelector(priorityRect, state);

            Rect conditionRect = new Rect(
                nameRect.x,
                priorityRect.yMax + 16f,
                nameRect.width,
                rect.height - (priorityRect.yMax - rect.y) - 32f);
            DrawConditionEditor(conditionRect, state);
        }

        private void DrawRuleNameEditor(Rect rect, RuleBuilderState state)
        {
            var rule = state.SelectedRule;
            bool isDefault = state.SelectedRuleset?.IsDefault ?? false;

            Rect labelRect = new Rect(rect.x, rect.y, 80f, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + 8f, rect.y, rect.width - 88f, rect.height);

            Verse.Widgets.Label(labelRect, "BWT_RuleName".Translate() + ":");

            if (isDefault)
            {
                GUI.color = Color.gray;
                Verse.Widgets.Label(fieldRect, rule.Name);
                GUI.color = Color.white;
            }
            else
            {
#if v1_2 || v1_1 || (v1_0 || v0_19)
                string newName = Widgets12.TextField(fieldRect, rule.Name,
                    RuleBuilderConstants.MaxRuleNameLength);
#else
                string newName = Verse.Widgets.TextField(fieldRect, rule.Name,
                    RuleBuilderConstants.MaxRuleNameLength);
#endif

                if (newName != rule.Name)
                {
                    rule.Name = newName;
                    rule.Parameters.RuleName = newName;
                    state.NotifyRulesModified();
                }
            }
        }

        private void DrawPrioritySelector(Rect rect, RuleBuilderState state)
        {
            var rule = state.SelectedRule;
            bool isDefault = state.SelectedRuleset?.IsDefault ?? false;
            int maxPriority = Mathf.Max(0, BetterWorkTabMod.Settings?.maxPriorityInt ?? state.MaxPriority);
            int currentPriority = Mathf.Clamp(rule.Parameters.Priority, 0, maxPriority);

            if (_editingRule != rule)
            {
                _editingRule = rule;
                _priorityBuffer = currentPriority.ToString();
            }

            Rect labelRect = new Rect(rect.x, rect.y, 120f, rect.height);
            Verse.Widgets.Label(labelRect, "BWT_AssignPriority".Translate() + ":");

            Rect minusRect = new Rect(labelRect.xMax + 8f, rect.y, 28f, rect.height);
            Rect fieldRect = new Rect(minusRect.xMax + 4f, rect.y, 64f, rect.height);
            Rect plusRect = new Rect(fieldRect.xMax + 4f, rect.y, 28f, rect.height);
            Rect hintRect = new Rect(plusRect.xMax + 8f, rect.y, Mathf.Max(0f, rect.xMax - plusRect.xMax - 8f), rect.height);

            if (string.IsNullOrEmpty(_priorityBuffer))
            {
                _priorityBuffer = currentPriority.ToString();
            }

            int newPriority = currentPriority;

            if (isDefault)
            {
                GUI.color = Color.gray;
                Verse.Widgets.DrawBoxSolid(fieldRect, RuleBuilderConstants.CardBackground);
                Verse.Widgets.DrawBox(fieldRect, 1);
                Text.Anchor = TextAnchor.MiddleCenter;
                Verse.Widgets.Label(fieldRect, currentPriority.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            else
            {
                if (Verse.Widgets.ButtonText(minusRect, "-"))
                {
                    newPriority = Mathf.Max(0, currentPriority - 1);
                }

                Verse.Widgets.TextFieldNumeric(fieldRect, ref newPriority, ref _priorityBuffer, 0, maxPriority);

                if (Verse.Widgets.ButtonText(plusRect, "+"))
                {
                    newPriority = Mathf.Min(maxPriority, newPriority + 1);
                }

                Event evt = Event.current;
                if (evt != null &&
                    evt.type == EventType.ScrollWheel &&
                    (Mouse.IsOver(fieldRect) || Mouse.IsOver(minusRect) || Mouse.IsOver(plusRect)))
                {
                    int delta = evt.delta.y > 0f ? 1 : -1;
                    newPriority = Mathf.Clamp(newPriority + delta, 0, maxPriority);
                    _priorityBuffer = newPriority.ToString();
                    evt.Use();
                }

                if (newPriority != currentPriority)
                {
                    rule.Parameters.Priority = newPriority;
                    state.SelectedPriority = newPriority;
                    _priorityBuffer = newPriority.ToString();
                    state.NotifyRulesModified();
                }
            }

            GUI.color = RuleBuilderConstants.SubtleTextColor;
            Verse.Widgets.Label(hintRect, $"0-{maxPriority}  (mouse wheel adjusts by 1)");
            GUI.color = Color.white;

            TooltipHandler.TipRegion(fieldRect, $"Priority value from 0 to {maxPriority}. Use the mouse wheel to adjust quickly.");
        }

        private void DrawConditionEditor(Rect rect, RuleBuilderState state)
        {
            var rule = state.SelectedRule;
            bool isDefault = state.SelectedRuleset?.IsDefault ?? false;

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, 24f);
            DrawSectionHeader(headerRect, "BWT_Conditions".Translate());

            Rect scrollRect = new Rect(
                rect.x,
                headerRect.yMax + 8f,
                rect.width,
                rect.height - 32f);

            var activeConditions = GetActiveConditions(rule.Parameters);
            float contentHeight = Mathf.Max(
                activeConditions.Count * RuleBuilderConstants.ConditionRowHeight + 40f,
                ConditionEditorMinHeight);

            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, contentHeight);

            Verse.Widgets.BeginScrollView(scrollRect, ref _conditionScroll, viewRect);

            float yPos = 0f;
            foreach (var condition in activeConditions)
            {
                Rect conditionRect = new Rect(
                    0f, yPos,
                    viewRect.width,
                    RuleBuilderConstants.ConditionRowHeight);

                ConditionBuilderWidget.Draw(
                    conditionRect,
                    condition,
                    rule.Parameters,
                    isDefault,
                    () => state.NotifyRulesModified());

                yPos += RuleBuilderConstants.ConditionRowHeight;
            }

            if (!isDefault)
            {
                Rect addCondRect = new Rect(0f, yPos + 8f, viewRect.width, ButtonRowHeight);
                if (Verse.Widgets.ButtonText(addCondRect, "+ " + "BWT_AddCondition".Translate()))
                {
                    ShowAddConditionMenu(rule.Parameters, state);
                }
            }

            Verse.Widgets.EndScrollView();
        }

        private void DrawSectionHeader(Rect rect, string text)
        {
            var oldFont = Text.Font;
            var oldColor = GUI.color;

            Text.Font = GameFont.Small;
            GUI.color = RuleBuilderConstants.HeaderColor;
            Verse.Widgets.Label(rect, text);

            Rect lineRect = new Rect(rect.x, rect.yMax - 2f, rect.width, 1f);
            Verse.Widgets.DrawBoxSolid(lineRect, RuleBuilderConstants.HeaderColor * 0.5f);

            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        private void DrawNoWorkTypeSelected(Rect rect)
        {
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Verse.Widgets.Label(rect, "BWT_SelectWorkTypeFirst".Translate());
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;
        }

        private void DrawNoRuleSelected(Rect rect)
        {
            Verse.Widgets.DrawMenuSection(rect);
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Verse.Widgets.Label(rect, "BWT_SelectOrCreateRule".Translate());
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;
        }

        private void CreateNewRule(RuleBuilderState state)
        {
            var newRule = new WorkAssignmentRule(
                new WorkAssignmentParameters(
                    $"{"BWT_NewRule".Translate()} {state.RulesForSelectedWorkType.Count + 1}",
                    3,
                    state.SelectedWorkType));

            state.SelectedRuleset.Rules.Add(newRule);
            state.SelectedRule = newRule;
            _editingRule = newRule;
            state.NotifyRulesModified();
        }

        private void DeleteRule(RuleBuilderState state, WorkAssignmentRule rule)
        {
            if (state.SelectedRuleset?.Rules == null)
                return;

            state.SelectedRuleset.Rules.Remove(rule);

            if (state.SelectedRule == rule)
            {
                state.SelectedRule = state.RulesForSelectedWorkType.FirstOrDefault();
                _editingRule = state.SelectedRule;
            }

            state.NotifyRulesModified();
        }

        private void MoveRule(RuleBuilderState state, WorkAssignmentRule rule, int direction)
        {
            var rules = state.SelectedRuleset?.Rules;
            if (rules == null)
                return;

            int currentIndex = rules.IndexOf(rule);
            int newIndex = currentIndex + direction;

            if (newIndex < 0 || newIndex >= rules.Count)
                return;

            rules.RemoveAt(currentIndex);
            rules.Insert(newIndex, rule);
            state.NotifyRulesModified();
        }

        private List<Pawn> GetCurrentPawns()
        {
            var map = Find.CurrentMap;
            return map?.mapPawns?.FreeColonists?.ToList() ?? new List<Pawn>();
        }

        private List<ConditionInfo> GetActiveConditions(WorkAssignmentParameters parameters)
        {
            var result = new List<ConditionInfo>();

            if (parameters.HasHighestSkill)
                result.Add(new ConditionInfo("HasHighestSkill", ConditionType.Bool, true));

            if (parameters.IsTopXSkill > 0)
                result.Add(new ConditionInfo("IsTopXSkill", ConditionType.Int, parameters.IsTopXSkill));

            if (parameters.PassionLevel > -1)
                result.Add(new ConditionInfo("PassionLevel", ConditionType.Int, parameters.PassionLevel));

            if (parameters.SkillLevelGreaterThan > -1)
                result.Add(new ConditionInfo("SkillLevelGreaterThan", ConditionType.Int, parameters.SkillLevelGreaterThan));

            if (parameters.SkillLevelLessThan > -1)
                result.Add(new ConditionInfo("SkillLevelLessThan", ConditionType.Int, parameters.SkillLevelLessThan));

            if (parameters.IsNaturalAlwaysAssign)
                result.Add(new ConditionInfo("IsNaturalAlwaysAssign", ConditionType.Bool, true));

            if (parameters.RequiredTrait != null)
                result.Add(new ConditionInfo("RequiredTrait", ConditionType.Trait, parameters.RequiredTrait));

            if (parameters.Gender != null)
                result.Add(new ConditionInfo("Gender", ConditionType.Gender, parameters.Gender));

#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
            if (parameters.Xenotype != null)
                result.Add(new ConditionInfo("Xenotype", ConditionType.Xenotype, parameters.Xenotype));
#endif

            if (parameters.IsCapableOfViolence)
                result.Add(new ConditionInfo("IsCapableOfViolence", ConditionType.Bool, true));

            if (parameters.HasChildOnMap)
                result.Add(new ConditionInfo("HasChildOnMap", ConditionType.Bool, true));

            if (parameters.IsPregnant)
                result.Add(new ConditionInfo("IsPregnant", ConditionType.Bool, true));

            if (parameters.RandomIfMultiple)
                result.Add(new ConditionInfo("RandomIfMultiple", ConditionType.Bool, true));

            if (parameters.AllowOverwritingHigherPriority)
                result.Add(new ConditionInfo("AllowOverwritingHigherPriority", ConditionType.Bool, true));

            return result;
        }

        private void ShowAddConditionMenu(WorkAssignmentParameters parameters, RuleBuilderState state)
        {
            var options = new List<FloatMenuOption>();

            void AddOption(string key, string label, System.Action action)
            {
                options.Add(new FloatMenuOption(label, () =>
                {
                    action();
                    state.NotifyRulesModified();
                }));
            }

            if (!parameters.HasHighestSkill)
                AddOption("HasHighestSkill", "BWT_Add_HasHighestSkill".Translate(), () => parameters.HasHighestSkill = true);

            if (parameters.IsTopXSkill <= 0)
                AddOption("IsTopXSkill", "BWT_Add_IsTopXSkill".Translate(), () => parameters.IsTopXSkill = 1);

            if (parameters.PassionLevel < 0)
                AddOption("PassionLevel", "BWT_Add_PassionLevel".Translate(), () => parameters.PassionLevel = 0);

            if (parameters.SkillLevelGreaterThan < 0)
                AddOption("SkillLevelGreaterThan", "BWT_Add_SkillLevelGreaterThan".Translate(), () => parameters.SkillLevelGreaterThan = 0);

            if (parameters.SkillLevelLessThan < 0)
                AddOption("SkillLevelLessThan", "BWT_Add_SkillLevelLessThan".Translate(), () => parameters.SkillLevelLessThan = 0);

            if (!parameters.IsNaturalAlwaysAssign)
                AddOption("IsNaturalAlwaysAssign", "BWT_Add_IsNaturalAlwaysAssign".Translate(), () => parameters.IsNaturalAlwaysAssign = true);

            if (parameters.RequiredTrait == null)
                AddOption("RequiredTrait", "BWT_Add_RequiredTrait".Translate(), () => { /* placeholder until trait picker */ });

            if (parameters.Gender == null)
                AddOption("Gender", "BWT_Add_Gender".Translate(), () => parameters.Gender = Gender.None);

#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
            if (parameters.Xenotype == null)
                AddOption("Xenotype", "BWT_Add_Xenotype".Translate(), () => { /* placeholder until xenotype picker */ });
#endif

            if (!parameters.IsCapableOfViolence)
                AddOption("IsCapableOfViolence", "BWT_Add_IsCapableOfViolence".Translate(), () => parameters.IsCapableOfViolence = true);

            if (!parameters.HasChildOnMap)
                AddOption("HasChildOnMap", "BWT_Add_HasChildOnMap".Translate(), () => parameters.HasChildOnMap = true);

            if (!parameters.IsPregnant)
                AddOption("IsPregnant", "BWT_Add_IsPregnant".Translate(), () => parameters.IsPregnant = true);

            if (!parameters.RandomIfMultiple)
                AddOption("RandomIfMultiple", "BWT_Add_RandomIfMultiple".Translate(), () => parameters.RandomIfMultiple = true);

            if (!parameters.AllowOverwritingHigherPriority)
                AddOption("AllowOverwritingHigherPriority", "BWT_Add_AllowOverwritingHigherPriority".Translate(), () => parameters.AllowOverwritingHigherPriority = true);

            if (!options.Any())
            {
                options.Add(new FloatMenuOption("BWT_NoConditionsAvailable".Translate(), null));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private int CalculateRuleTargetIndex(Vector2 mousePos)
        {
            float biasedY = mousePos.y + (RuleRowHeight * 0.5f);
            return ListDragCalculator.CalculateInsertionIndex(
                _visibleRules.Count,
                biasedY,
                _rulesListScreenRect.y,
                _ruleListScroll.y,
                _ => RuleRowHeight);
        }

        private void ValidateRules(RuleBuilderState state)
        {
            // Placeholder for future validation service hook.
        }

        public float GetMinimumHeight(RuleBuilderState state) => 600f;
    }
}
