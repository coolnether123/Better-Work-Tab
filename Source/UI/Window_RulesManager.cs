using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using Spine.DragDropApi.Util;
using Better_Work_Tab.Patches;
using HarmonyLib;
using RimWorld;
using Spine.DragDropApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Central window for managing work assignment rulesets and their rules.
    /// Supports drag-drop reordering for both rulesets and individual rules.
    /// Provides a parameter editor for customizing rule conditions and assignments.
    /// </summary>
    internal class Window_RulesManager : Window
    {
        // Drag-drop UI for reordering rulesets in the left column
        private List<WorkAssignmentRuleset> _rulesets;
        private RulesetListDragUI _rulesetListUI;

        // Drag-drop controller for rules in the middle column
        private readonly DragDropController<WorkAssignmentRule> _ruleDragController;
        private Rect _rulesListScreenRect;
        private const float RuleRowHeight = 32f;
        private const float RuleDragThreshold = 5f;
        private readonly ClickOrDragGate<WorkAssignmentRule> _rulesClickGate = new ClickOrDragGate<WorkAssignmentRule>();
        private WorkAssignmentRule _pendingRuleDrag;

        // Parameter field caching
        private static readonly Lazy<FieldInfo[]> CachedParameterFields = new Lazy<FieldInfo[]>(
            () => RuleParameterRegistry.Fields.ToArray());

        private const float ParameterRowHeight = 32f;
        private const float ParameterRowWidthReduction = 30f;
        private const float ParameterRowIndent = 10f;
        private const float ParameterValuePortion = 0.25f;
        private const float TraitButtonMinWidth = 150f;
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
        private static readonly Vector2 XenotypeIconSize = new Vector2(22f, 22f);
#endif

        private delegate void ParameterDrawer(
            Window_RulesManager manager,
            FieldInfo field,
            Rect rowRect,
            Rect valueRect,
            string label);

        private static readonly Dictionary<Type, ParameterDrawer> ParameterDrawers =
            CreateParameterDrawers();

        /// <summary>
        /// Gets all fields marked with [RuleParameter] attribute.
        /// Results are cached for performance.
        /// </summary>
        public static FieldInfo[] GetParameterFields()
        {
            return CachedParameterFields.Value;
        }

        private static Dictionary<Type, ParameterDrawer> CreateParameterDrawers()
        {
            return new Dictionary<Type, ParameterDrawer>
            {
                { typeof(bool), (mgr, field, rowRect, valueRect, label) => mgr.DrawBoolParameter(field, rowRect, label) },
                { typeof(int), (mgr, field, rowRect, valueRect, label) => mgr.DrawIntParameter(field, rowRect, valueRect, label) },
                { typeof(string), (mgr, field, rowRect, valueRect, label) => mgr.DrawStringParameter(field, rowRect, valueRect, label) },
                { typeof(Gender?), (mgr, field, rowRect, valueRect, label) => mgr.DrawGenderParameter(field, rowRect, valueRect, label) },
                { typeof(WorkTypeDef), (mgr, field, rowRect, valueRect, label) => mgr.DrawWorkTypeParameter(field, rowRect, valueRect, label) },
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                { typeof(XenotypeDef), (mgr, field, rowRect, valueRect, label) => mgr.DrawXenotypeParameter(field, rowRect, valueRect, label) },
#endif
                { typeof(Tuple<TraitDef, int>), (mgr, field, rowRect, valueRect, label) => mgr.DrawTraitParameter(field, rowRect, valueRect, label) },
                { typeof(WorkAssignmentParameters), (mgr, field, rowRect, valueRect, label) => mgr.DrawUnsupportedParameter(rowRect) }
            };
        }

        private static string GetTranslationKey(FieldInfo field)
        {
            return $"BWT_{field.Name}";
        }

        public Window_RulesManager()
        {
            forcePause = true;
            doCloseX = true;
            preventCameraMotion = true;
            resizeable = false;

            // Initialize drag controller for rules with target index calculator
            _ruleDragController = new DragDropController<WorkAssignmentRule>(
                mousePos => CalculateRuleTargetIndex(mousePos)
            );
        }

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        private BetterWorkTabSettings Settings => BetterWorkTabMod.Settings;
        private readonly QuickSearchWidget quickSearch = new QuickSearchWidget();
        private Vector2 leftScroll;
        private Vector2 midScroll;
        private Vector2 rightScroll;
        private string ruleNameBuffer = "";

        private bool uneditable => _rulesetListUI?.SelectedRuleset?.IsDefault ?? false;
        private WorkAssignmentRuleset CurrentRuleset => Settings.CurrentRuleset;
        private WorkAssignmentRule SelectedRule;

        public override void PreOpen()
        {
            base.PreOpen();
            _rulesets = BetterWorkTabMod.Settings.SavedRulesets;

            // Initialize drag-drop UI for rulesets with selection callback
            _rulesetListUI = new RulesetListDragUI(_rulesets, OnRulesetSelected);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(inRect, "Manage Rules");
            Text.Font = GameFont.Small;
            float titleHeight = Text.CalcHeight("Manage Rules", 0) + 12f;

            Rect contentRect = inRect;
            contentRect.height -= titleHeight;
            contentRect.y += titleHeight;

            // Split into three columns: Rulesets | Rules | Parameters
            const float columnMargin = 10f;
            float leftWidth = (contentRect.width - columnMargin) * 0.5f;
            Rect leftSide = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            Rect rightSide = new Rect(leftSide.xMax + columnMargin, contentRect.y, contentRect.width - leftWidth - columnMargin, contentRect.height);

            float innerLeftWidth = (leftSide.width - columnMargin) * 0.5f;
            Rect leftRect = new Rect(leftSide.x, leftSide.y, innerLeftWidth, leftSide.height);
            Rect midRect = new Rect(leftRect.xMax + columnMargin, leftSide.y, leftSide.width - innerLeftWidth - columnMargin, leftSide.height);

            // LEFT: Rulesets list with drag-drop reordering
            _rulesetListUI.DoListUI(leftRect);

            // MIDDLE: Rules for selected ruleset
            if (_rulesetListUI.SelectedRuleset != null)
            {
                DoRulesetRulesListing(midRect, _rulesetListUI.SelectedRuleset);

                // RIGHT: Rule parameters editor
                if (SelectedRule != null)
                {
                    DoRuleContents(rightSide, SelectedRule);
                }
            }
        }

        /// <summary>
        /// Called when user selects a ruleset from the drag-drop list.
        /// </summary>
        private void OnRulesetSelected(WorkAssignmentRuleset ruleset)
        {
            if (ruleset != null)
            {
                BetterWorkTabMod.Settings.CurrentRuleset = ruleset;
                ruleNameBuffer = ruleset.Name;
                SelectedRule = ruleset.Rules.FirstOrDefault();
            }
        }

        /// <summary>
        /// Calculate rule insertion index based on mouse position and current list rect.
        /// Bias mouse Y slightly so snapping feels closer to the gap nearest the cursor.
        /// </summary>
        private int CalculateRuleTargetIndex(Vector2 mousePos)
        {
            var ruleset = _rulesetListUI?.SelectedRuleset;
            if (ruleset == null || ruleset.Rules == null || ruleset.Rules.Count == 0)
                return 0;

            float biasedY = mousePos.y + (RuleRowHeight * 0.5f);

            return ListDragCalculator.CalculateInsertionIndex(
                ruleset.Rules.Count,
                biasedY,
                _rulesListScreenRect.y,
                midScroll.y,
                _ => RuleRowHeight
            );
        }

        /// <summary>
        /// Draws the UI for editing the parameters of a selected rule.
        /// Supports all parameter types: bool, int, string, Gender, WorkTypeDef, XenotypeDef, and Trait.
        /// </summary>
        void DoRuleContents(Rect rightRect, WorkAssignmentRule rule)
        {
            Rect rect = rightRect;
            rect.y = rightRect.yMax - 24f;
            rect.height = 24f;
            Rect rect2 = rightRect;
            rect2.yMax = rect.y - 10f;
            Rect rect3 = rect2;
            rect3.xMin += ParameterRowIndent;
            rect3.xMax -= ParameterRowIndent;
            rect3.y = rect2.yMax - this.CloseButSize.y - 10f;
            rect3.height = this.CloseButSize.y;
            Rect outRect = rect2;
            outRect.yMin -= 24f;
            outRect.yMax = rect3.y + 39f;
            Widgets.DrawMenuSection(rect2);

#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
            int parameterCount = GetParameterFields().Count(f => ModsConfig.BiotechActive || f.FieldType != typeof(XenotypeDef));
#else
            int parameterCount = GetParameterFields().Count();
#endif

            if (rule == null)
            {
                var oldColor = GUI.color;
                GUI.color = Color.gray;
                var defaultAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(outRect, "No rule selected");
                Text.Anchor = defaultAnchor;
                GUI.color = oldColor;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, (parameterCount * ParameterRowHeight));
            Widgets.BeginScrollView(outRect, ref rightScroll, viewRect);

            SelectedRule.Name = SelectedRule.Parameters.RuleName == "" ? "New Rule " + (rule == null ? 0 : SelectedRule.Parameters.Priority) : SelectedRule.Parameters.RuleName;

            float curY = ParameterRowHeight;

            foreach (FieldInfo field in GetParameterFields())
            {
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                if (!ModsConfig.BiotechActive && field.FieldType == typeof(XenotypeDef))
                    continue;
#else
                if (field.FieldType.Name == "XenotypeDef") // Fail-safe for 1.3
                    continue;
#endif

                Rect rowRect = new Rect(0f, curY, outRect.width - ParameterRowWidthReduction, ParameterRowHeight);
                rowRect.x += ParameterRowIndent;
                rowRect.width -= ParameterRowIndent;
                curY += ParameterRowHeight;
                Rect valueRect = rowRect.RightPart(ParameterValuePortion);
                string paramLabel = GetTranslationKey(field).Translate();

                TooltipHandler.TipRegion(rowRect, ("BWT_" + field.Name + "_Desc").Translate());

                if (ParameterDrawers.TryGetValue(field.FieldType, out var drawer))
                {
                    drawer(this, field, rowRect, valueRect, paramLabel);
                }
                else
                {
                    DrawUnsupportedParameter(rowRect);
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawBoolParameter(FieldInfo field, Rect rowRect, string label)
        {
            bool value = (bool)field.GetValue(SelectedRule.Parameters);
            var oldColor = GUI.color;
            if (uneditable) GUI.color = Color.gray;

            Widgets.CheckboxLabeled(rowRect, label, ref value, disabled: uneditable);
            field.SetValue(SelectedRule.Parameters, value);

            GUI.color = oldColor;
        }

        private void DrawIntParameter(FieldInfo field, Rect rowRect, Rect valueRect, string label)
        {
            Widgets.Label(rowRect.LeftPart(1f - ParameterValuePortion), label);

            int value = (int)field.GetValue(SelectedRule.Parameters);
            string editBuffer = value.ToString();
            DrawPlusMinusOneField(valueRect, ref value, ref editBuffer, disabled: uneditable);
            field.SetValue(SelectedRule.Parameters, value);
        }

        private void DrawStringParameter(FieldInfo field, Rect rowRect, Rect valueRect, string label)
        {
            Widgets.Label(rowRect.LeftPart(1f - ParameterValuePortion), label);

            string value = (string)field.GetValue(SelectedRule.Parameters) ?? string.Empty;

            if (uneditable)
            {
                Widgets.Label(valueRect, value);
            }
            else
            {
#if v1_2 || v1_1
                value = Widgets12.TextField(valueRect, value, 24);
#else
                value = Widgets.TextField(valueRect, value, 24);
#endif
                field.SetValue(SelectedRule.Parameters, value);
            }
        }

        private void DrawGenderParameter(FieldInfo field, Rect rowRect, Rect valueRect, string label)
        {
            Widgets.Label(rowRect.LeftPart(1f - ParameterValuePortion), label);
            var oldColor = GUI.color;
            if (uneditable) GUI.color = Color.gray;

            Gender? value = (Gender?)field.GetValue(SelectedRule.Parameters);
            if (Widgets.ButtonText(valueRect, value?.ToString() ?? "Unassigned", active: !uneditable))
            {
                List<FloatMenuOption> enums = new List<FloatMenuOption>()
                {
                    new FloatMenuOption("Unassigned", delegate
                    {
                        field.SetValue(SelectedRule.Parameters, null);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };

                foreach (var e in Enum.GetValues(typeof(Gender)))
                {
                    enums.Add(new FloatMenuOption(e.ToString(), delegate
                    {
                        field.SetValue(SelectedRule.Parameters, e);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(enums));
            }

            GUI.color = oldColor;
        }

        private void DrawWorkTypeParameter(FieldInfo field, Rect rowRect, Rect valueRect, string label)
        {
            Widgets.Label(rowRect.LeftPart(1f - ParameterValuePortion), label);

            var parameters = SelectedRule.Parameters;
            WorkTypeDef worktype = (WorkTypeDef)field.GetValue(parameters);

            if (worktype == null && !string.IsNullOrEmpty(parameters.WorktypeString))
            {
                worktype = DefDatabase<WorkTypeDef>.GetNamedSilentFail(parameters.WorktypeString);
                parameters.Worktype = worktype;
            }

            bool missingSavedWorktype = !string.IsNullOrEmpty(parameters.WorktypeString) && worktype == null;
            string buttonLabel = worktype?.labelShort.CapitalizeFirst() ?? "Unassigned";
            if (missingSavedWorktype)
            {
                buttonLabel = $"\"{parameters.WorktypeString}\" (Missing)";
            }

            var oldColor = GUI.color;
            if (uneditable) GUI.color = Color.gray;

            if (Widgets.ButtonText(valueRect, buttonLabel, active: !uneditable))
            {
                List<FloatMenuOption> defOptions = new List<FloatMenuOption>()
                {
                    new FloatMenuOption("Unassigned", delegate
                    {
                        field.SetValue(parameters, null);
                        parameters.WorktypeString = "";
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };

                foreach (var def in DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderByDescending(w => w.naturalPriority))
                {
                    defOptions.Add(new FloatMenuOption(def.labelShort.CapitalizeFirst(), delegate
                    {
                        field.SetValue(parameters, def);
                        parameters.WorktypeString = def.defName;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(defOptions));
            }

            GUI.color = oldColor;
        }


#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
        private void DrawXenotypeParameter(FieldInfo field, Rect rowRect, Rect valueRect, string label)
        {
            Widgets.Label(rowRect.LeftPart(1f - ParameterValuePortion), label);
            var parameters = SelectedRule.Parameters;
            XenotypeDef value = (XenotypeDef)field.GetValue(parameters);
            var oldColor = GUI.color;
            if (uneditable) GUI.color = Color.gray;

            bool clicked = Widgets.ButtonImageWithBG(valueRect, value?.Icon ?? TexButton.CloseXSmall, XenotypeIconSize);
            if (!uneditable && clicked)
            {
                List<FloatMenuOption> defOptions = new List<FloatMenuOption>()
                {
                    new FloatMenuOption("Unassigned", delegate
                    {
                        field.SetValue(parameters, null);
                        parameters.XenotypeString = "";
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };
                foreach (var def in DefDatabase<XenotypeDef>.AllDefsListForReading)
                {
                    defOptions.Add(new FloatMenuOption(def.LabelCap, delegate
                    {
                        field.SetValue(parameters, def);
                        parameters.XenotypeString = def.defName;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }, def.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default));
                }
                Find.WindowStack.Add(new FloatMenu(defOptions));
            }

            GUI.color = oldColor;
        }
#endif

        private void DrawTraitParameter(FieldInfo field, Rect rowRect, Rect valueRect, string label)
        {
            Widgets.Label(rowRect.LeftPart(1f - ParameterValuePortion), label);

            var parameters = SelectedRule.Parameters;
            Tuple<TraitDef, int> trait = (Tuple<TraitDef, int>)field.GetValue(parameters);
            string buttonLabel = "Unassigned";
            if (trait?.Item1 != null)
            {
                buttonLabel = trait.Item1.DataAtDegree(trait.Item2).LabelCap;
            }
            else if (!string.IsNullOrEmpty(parameters.TraitString))
            {
                buttonLabel = $"\"{parameters.TraitString}\" (Missing)";
            }

            var oldColor = GUI.color;
            if (uneditable) GUI.color = Color.gray;

            Rect buttonRect = valueRect;
            if (buttonRect.width < TraitButtonMinWidth)
            {
                buttonRect.width = TraitButtonMinWidth;
                buttonRect.x = rowRect.xMax - buttonRect.width;
            }

            if (Widgets.ButtonText(buttonRect, buttonLabel, active: !uneditable))
            {
                List<FloatMenuOption> list = new List<FloatMenuOption>()
                {
                    new FloatMenuOption("Unassigned", delegate
                    {
                        field.SetValue(parameters, null);
                        parameters.TraitString = "";
                        parameters.TraitDegree = null;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };
                var sorted = DefDatabase<TraitDef>.AllDefs.OrderByDescending((TraitDef td) => td.GetGenderSpecificCommonality(Gender.None));
                var sortedList = sorted.ToList();
                sortedList.SortBy((TraitDef td) => td.defName);
                foreach (TraitDef item in sortedList)
                {
                    foreach (TraitDegreeData degreeData in item.degreeDatas)
                    {
                        TraitDef localDef = item;
                        TraitDegreeData localDeg = degreeData;
                        list.Add(new FloatMenuOption(localDeg.LabelCap, delegate
                        {
                            field.SetValue(parameters, new Tuple<TraitDef, int>(localDef, localDeg.degree));
                            parameters.TraitString = localDef.defName;
                            parameters.TraitDegree = localDeg.degree;
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        }));
                    }
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            GUI.color = oldColor;
        }

        private void DrawUnsupportedParameter(Rect rowRect)
        {
            Widgets.Label(rowRect, "NESTED RULES NOT SUPPORTED");
        }

        /// <summary>
        /// Draws a UI control with +/- buttons and text field for integer editing.
        /// </summary>
        public static void DrawPlusMinusOneField(Rect rect, ref int value, ref string editBuffer, int multiplier = 1, bool disabled = false)
        {
            var oldColor = GUI.color;
            if (disabled) GUI.color = Color.gray;

            Rect leftRect = rect.LeftPart(0.33f);
            Rect rightRect = rect.RightPart(0.33f);
            //Rect midRect = rect.MiddlePart(0.33f, 1f);

            if (Widgets.ButtonText(leftRect, "-1", active: !disabled))
            {
                value -= 1 * multiplier;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                editBuffer = value.ToString();
            }
            if (Widgets.ButtonText(rightRect, "+1", active: !disabled))
            {
                value += 1 * multiplier;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                editBuffer = value.ToString();
            }
            if (disabled)
            {
                var b4 = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                //Widgets.Label(midRect, value.ToString());
                Text.Anchor = b4;
            }
            else
            {
                //Widgets.TextFieldNumeric(midRect, ref value, ref editBuffer);
            }
            value = Mathf.Clamp(value, -1, 4);
            GUI.color = oldColor;
        }

        /// <summary>
        /// Draws the list of rules for the currently selected ruleset.
        /// Supports drag-drop reordering via _ruleDragController.
        /// </summary>
        void DoRulesetRulesListing(Rect midRect, WorkAssignmentRuleset selectedRuleset)
        {
            if (selectedRuleset == null)
            {
                return;
            }

            Rect rect = midRect;
            rect.y = midRect.yMax - 24f;
            rect.height = 24f;
            Rect rect2 = midRect;
            rect2.yMax = rect.y - 10f;

            rect2.SplitHorizontally(32f, out Rect titleRect, out rect2);

            selectedRuleset.Name = ruleNameBuffer == "" ? "New Ruleset " + (Settings.SavedRulesets?.IndexOf(selectedRuleset) + 1) ?? ruleNameBuffer : ruleNameBuffer;

            if (uneditable)
            {
                var b4 = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(titleRect, ruleNameBuffer);
                Text.Anchor = b4;
            }
            else
#if v1_2 || v1_1
                ruleNameBuffer = Widgets12.TextField(titleRect, ruleNameBuffer, 21);
#else
                ruleNameBuffer = Widgets.TextField(titleRect, ruleNameBuffer, 21);
#endif

            rect2.height -= 10f;
            rect2.y += 10f;

            Rect rect3 = rect2;
            rect3.xMin += 10f;
            rect3.xMax -= 10f;
            rect3.y = rect2.yMax - this.CloseButSize.y - 10f;
            rect3.height = this.CloseButSize.y;
            Rect outRect = rect2;
            outRect.yMax = rect3.y - 10f;
            Widgets.DrawMenuSection(rect2);

            rect3.SplitHorizontally(rect3.height * 0.5f, out Rect topRect, out Rect bottomRect);

            if (uneditable) GUI.color = Color.gray;
            if (Widgets.ButtonText(topRect, "BWT_NewRule".Translate(), active: !uneditable))
            {
                string ruleName = $"{ "BWT_NewRule".Translate() } {selectedRuleset.Rules.Count + 1}";
                WorkAssignmentRule newRule = new WorkAssignmentRule(new WorkAssignmentParameters(ruleName, 0));
                selectedRuleset.Rules.Add(newRule);
                SelectedRule = newRule;
            }
            if (Widgets.ButtonText(bottomRect, "BWT_DuplicateRule".Translate(), active: !uneditable))
            {
                if (SelectedRule != null)
                {
                    WorkAssignmentRule newRule = SelectedRule.Copy();
                    selectedRuleset.Rules.Add(newRule);
                    SelectedRule = newRule;
                }
            }
            if (uneditable) GUI.color = Color.white;

            // Check if ruleset has no rules
            if (selectedRuleset.Rules.Count == 0)
            {
                GUI.color = Color.gray;
                var defaultAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(outRect, "BWT_NoRulesInRuleset".Translate());
                Text.Anchor = defaultAnchor;
                GUI.color = Color.white;
                return;
            }

            int num = selectedRuleset.Rules.Count;

            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, num * RuleRowHeight);

            // Store the visible list rect for drag calculations
            _rulesListScreenRect = outRect;

            Widgets.BeginScrollView(outRect, ref midScroll, viewRect);

            float curY = 0f;
            int rowIndex = 0;
            WorkAssignmentRule ruleToRemove = null;

            foreach (var item in selectedRuleset.Rules)
            {
                Rect rect4 = new Rect(0f, curY, outRect.width, RuleRowHeight);
                Rect rect5 = rect4;
                rect5.x += 10f;
                curY += RuleRowHeight;

                if (SelectedRule == item)
                {
                    Widgets.DrawHighlightSelected(rect4);
                }
                else if (Mouse.IsOver(rect4))
                {
                    Widgets.DrawHighlight(rect4);
                }
                else if (rowIndex % 2 == 1)
                {
                    Widgets.DrawLightHighlight(rect4);
                }

                rowIndex++;
                string text = item.Name;
                var oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rect5, text);
                Text.Anchor = oldAnchor;

                if (selectedRuleset != null && !selectedRuleset.IsDefault)
                    DoDeleteButton_Rules(ref ruleToRemove, ref rect4, item, selectedRuleset);


            }

            if (ruleToRemove != null)
                selectedRuleset.Rules.Remove(ruleToRemove);

            Widgets.EndScrollView();

            HandleRulesListInput(_rulesListScreenRect, selectedRuleset);
            DrawRuleDragOverlay(_rulesListScreenRect, selectedRuleset);
        }

        /// <summary>
        /// Handles mouse input for rule list dragging and selection.
        /// </summary>
        private void HandleRulesListInput(Rect listScreenRect, WorkAssignmentRuleset selectedRuleset)
        {
            var evt = Event.current;
            if (evt == null || selectedRuleset == null)
                return;

            var rules = selectedRuleset.Rules;
            if (rules == null)
                return;

            // Active drag in progress
            if (_ruleDragController.IsActive)
            {
                var session = _ruleDragController.CurrentSession;

                _ruleDragController.UpdateDrag(evt.mousePosition);

                float contentHeight = rules.Count * RuleRowHeight;
                _ruleDragController.ApplyAutoScroll(
                    ref midScroll,
                    evt.mousePosition,
                    listScreenRect,
                    contentHeight,
                    Time.deltaTime
                );

                if (evt.type == EventType.MouseUp)
                {
                    _rulesClickGate.ClearIfTracking(session?.DraggedItem);
                    FinalizeRuleDrop(selectedRuleset);
                    evt.Use();
                }

                return;
            }

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && listScreenRect.Contains(evt.mousePosition))
                    {
                        float localY = evt.mousePosition.y - listScreenRect.y + midScroll.y;
                        int index = Mathf.FloorToInt(localY / RuleRowHeight);

                        if (index >= 0 && index < rules.Count)
                        {
                            _pendingRuleDrag = rules[index];
                            SelectedRule = _pendingRuleDrag;

                            _rulesClickGate.Begin(_pendingRuleDrag, evt.button, evt.mousePosition);
                        }
                        else
                        {
                            _pendingRuleDrag = null;
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_pendingRuleDrag != null && !uneditable)
                    {
                        if (_rulesClickGate.RegisterDrag(_pendingRuleDrag, evt.mousePosition, RuleDragThreshold))
                        {
                            int srcIndex = rules.IndexOf(_pendingRuleDrag);
                            if (srcIndex >= 0)
                            {
                                bool started = _ruleDragController.TryStartDrag(
                                    _pendingRuleDrag,
                                    srcIndex,
                                    rules.Count,
                                    evt.mousePosition
                                );

                                if (started)
                                {
                                    _rulesClickGate.MarkDragStarted(_pendingRuleDrag);
                                    evt.Use();
                                }
                            }

                            _pendingRuleDrag = null;
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (_pendingRuleDrag != null &&
                        _rulesClickGate.TryComplete(_pendingRuleDrag, evt.button, listScreenRect.Contains(evt.mousePosition)))
                    {
                        SelectedRule = _pendingRuleDrag;
                        evt.Use();
                    }

                    _pendingRuleDrag = null;
                    break;
            }
        }

        /// <summary>
        /// Draws the insertion line overlay while dragging a rule.
        /// </summary>
        private void DrawRuleDragOverlay(Rect listScreenRect, WorkAssignmentRuleset selectedRuleset)
        {
            if (!_ruleDragController.IsActive)
                return;

            var session = _ruleDragController.CurrentSession;
            if (session == null)
                return;

            float lineY = listScreenRect.y - midScroll.y + (session.TargetIndex * RuleRowHeight);

            if (lineY >= listScreenRect.y && lineY <= listScreenRect.yMax)
            {
                ListDragVisuals.DrawInsertionLine(
                    listScreenRect.x,
                    lineY,
                    listScreenRect.width
                );
            }
        }

        /// <summary>
        /// Finalizes the rule reordering after drop.
        /// </summary>
        private void FinalizeRuleDrop(WorkAssignmentRuleset selectedRuleset)
        {
            if (selectedRuleset == null)
            {
                _ruleDragController.CancelDrag();
                return;
            }

            var rules = selectedRuleset.Rules;
            if (rules == null || rules.Count == 0)
            {
                _ruleDragController.CancelDrag();
                return;
            }

            var reason = _ruleDragController.FinalizeDrag(rules);

            if (reason == DragEndReason.Success)
            {
                BetterWorkTabMod.Settings.Write();
            }
        }

        /// <summary>
        /// Draws delete button for a rule. Only shown for non-default rulesets.
        /// </summary>
        private void DoDeleteButton_Rules(ref WorkAssignmentRule ruleToRemove, ref Rect rect4, WorkAssignmentRule currentRule, WorkAssignmentRuleset selectedRuleset)
        {
            Rect rect6 = new Rect(rect4);
            rect6.width = 24f;
            rect6.height = 24f;
            rect6.x = rect4.xMax - rect6.width - (selectedRuleset.Rules.Count >= 13 ? 20f : 0);
            rect6.y = rect4.y + (rect4.height - rect6.height) / 2f;

            if (Widgets.ButtonImage(rect6, TexButton.DeleteX))
            {
                ruleToRemove = currentRule;
                SelectedRule = null;
            }
        }

        /// <summary>
        /// Deletes the specified ruleset and reassigns CurrentRuleset if needed.
        /// </summary>
        private void DeleteRuleset(WorkAssignmentRuleset rulesetToDelete)
        {
            var rulesets = Settings.SavedRulesets;
            if (rulesetToDelete == null || rulesets == null || !rulesets.Contains(rulesetToDelete))
            {
                return;
            }

            int currentIndex = rulesets.IndexOf(rulesetToDelete);
            rulesets.RemoveAt(currentIndex);

            // Reassign CurrentRuleset if we deleted it
            if (Settings.CurrentRuleset == rulesetToDelete)
            {
                if (currentIndex < rulesets.Count)
                {
                    // Move to the next ruleset
                    Settings.CurrentRuleset = rulesets[currentIndex];
                }
                else if (rulesets.Count > 0)
                {
                    // Move to the last ruleset
                    Settings.CurrentRuleset = rulesets.Last();
                }
                else
                {
                    // No rulesets left
                    Settings.CurrentRuleset = null;
                }
            }

            ruleNameBuffer = Settings.CurrentRuleset?.Name ?? "";
            SelectedRule = Settings.CurrentRuleset?.Rules?.FirstOrDefault() ?? null;
        }

        public override void PostClose()
        {
            base.PostClose();
            BetterWorkTabMod.Settings.Write();
        }
    }
}
