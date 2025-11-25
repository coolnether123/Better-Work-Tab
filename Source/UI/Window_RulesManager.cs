using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using HarmonyLib;
using RimWorld;
using Spine.DragDropApi;
using Spine.DragDropApi.Util;
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
        private bool _rulesMouseDown;
        private Vector2 _rulesMouseDownPos;
        private WorkAssignmentRule _pendingRuleDrag;

        // Parameter field caching
        private static FieldInfo[] CachedParameterFields;

        /// <summary>
        /// Gets all fields marked with [RuleParameter] attribute.
        /// Results are cached for performance.
        /// </summary>
        public static FieldInfo[] GetParameterFields()
        {
            if (CachedParameterFields == null)
            {
                CachedParameterFields = typeof(WorkAssignmentParameters)
                    .GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.GetCustomAttribute<RuleParameterAttribute>() != null)
                    .ToArray();
            }
            return CachedParameterFields;
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
            contentRect.SplitVerticallyWithMargin(out Rect leftSide, out Rect rightSide, 10f);
            leftSide.SplitVerticallyWithMargin(out Rect leftRect, out Rect midRect, 10f);

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
            rect3.xMin += 10f;

            /// <summary>
            /// Converts a PascalCase field name to a translation key.
            /// </summary>
            string GetTranslationKey(FieldInfo field)
            {
                return $"BWT_{field.Name}";
            }

            rect3.xMax -= 10f;
            rect3.y = rect2.yMax - Window.CloseButSize.y - 10f;
            rect3.height = Window.CloseButSize.y;
            Rect outRect = rect2;
            outRect.yMin -= 24f;
            outRect.yMax = rect3.y + 39f;
            Widgets.DrawMenuSection(rect2);

            int num = GetParameterFields().Length;

            if (rule == null)
            {
                GUI.color = Color.gray;
                var defaultAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(outRect, "No rule selected");
                Text.Anchor = defaultAnchor;
                GUI.color = Color.white;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, outRect.width, (num * 32));
            Widgets.AdjustRectsForScrollView(rect2, ref outRect, ref viewRect);
            Widgets.BeginScrollView(outRect, ref rightScroll, viewRect);

            SelectedRule.Name = SelectedRule.Parameters.RuleName == "" ? "New Rule " + (rule == null ? 0 : SelectedRule.Parameters.Priority) : SelectedRule.Parameters.RuleName;

            float num2 = 32f;

            foreach (FieldInfo field in GetParameterFields())
            {
                // Skip Biotech-exclusive fields if mod is not installed
                if (!ModsConfig.BiotechActive && field.FieldType == typeof(XenotypeDef))
                    continue;

                Rect rect4 = new Rect(0f, num2, outRect.width - 30f, 32f);
                Rect rect5 = rect4;
                rect5.x += 10f;
                num2 += 32f;
                Rect rightPart = rect5.RightPart(0.25f);
                string paramLabel = GetTranslationKey(field).Translate();

                GUI.color = Color.white;
                var fontsize = Text.Font;

                TooltipHandler.TipRegion(rect5, ("BWT_" + field.Name + "_Desc").Translate());

                if (field.FieldType == typeof(bool))
                {
                    bool refValue = (bool)field.GetValue(SelectedRule.Parameters);
                    Widgets.CheckboxLabeled(rect5, paramLabel, ref refValue, disabled: uneditable);
                    field.SetValue(SelectedRule.Parameters, refValue);
                    continue;
                }

                GUI.color = Color.white;

                if (field.FieldType == typeof(int))
                {
                    int refValue = (int)field.GetValue(SelectedRule.Parameters);
                    Widgets.Label(rect5, paramLabel);

                    string editBuffer = refValue.ToString();
                    DrawPlusMinusOneField(rightPart, ref refValue, ref editBuffer, disabled: uneditable);
                    field.SetValue(SelectedRule.Parameters, refValue);

                    continue;
                }

                GUI.color = Color.white;

                if (field.FieldType == typeof(string))
                {
                    string refValue = (string)field.GetValue(SelectedRule.Parameters) == null ? "" : (string)field.GetValue(SelectedRule.Parameters);
                    Widgets.Label(rect5, paramLabel);

                    if (uneditable)
                    {
                        Widgets.Label(rect5.RightHalf(), refValue);
                    }
                    else
                    {
                        refValue = Widgets.TextField(rect5.RightHalf(), refValue, 24);
                        field.SetValue(SelectedRule.Parameters, refValue);
                    }

                    continue;
                }

                Text.Font = fontsize;
                GUI.color = Color.white;

                if (field.FieldType == typeof(Gender?))
                {
                    Gender? refValue = (Gender?)field.GetValue(SelectedRule.Parameters) ?? null;

                    Widgets.Label(rect5, paramLabel);
                    if (uneditable) GUI.color = Color.gray;
                    if (Widgets.ButtonText(rightPart, refValue?.ToString() ?? "Unassigned", active: !uneditable))
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
                    continue;
                }

                Text.Font = fontsize;
                GUI.color = Color.white;

                // WorkTypeDef with serialization fallback
                if (field.FieldType == typeof(WorkTypeDef))
                {
                    WorkTypeDef refValue = (WorkTypeDef)field.GetValue(SelectedRule.Parameters) ?? null;

                    // Fallback: try to get WorkTypeDef by name if null
                    if (refValue == null && SelectedRule.Parameters.WorktypeString != null && SelectedRule.Parameters.WorktypeString != "")
                    {
                        refValue = DefDatabase<WorkTypeDef>.GetNamedSilentFail(SelectedRule.Parameters.WorktypeString);
                        SelectedRule.Parameters.Worktype = refValue;
                    }

                    Widgets.Label(rect5, paramLabel);
                    if (uneditable) GUI.color = Color.gray;

                    // Show "(Nonexistent)" if worktype def doesn't exist but was saved
                    if (SelectedRule.Parameters.IgnoreIfWorktypeNonexistent && SelectedRule.Parameters.WorktypeString != null && SelectedRule.Parameters.WorktypeString != "" && DefDatabase<WorkTypeDef>.GetNamedSilentFail(SelectedRule.Parameters.WorktypeString) == null)
                    {
                        var defaultAnchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleRight;
                        Widgets.Label(rect5.RightPart(0.5f), "\"" + SelectedRule.Parameters.WorktypeString + "\" (Nonexistent)");
                        Text.Anchor = defaultAnchor;
                    }
                    else if (Widgets.ButtonText(rect5.RightPart(0.25f), refValue?.labelShort.CapitalizeFirst() ?? "Unassigned", active: !uneditable))
                    {
                        List<FloatMenuOption> defOptions = new List<FloatMenuOption>()
                        {
                            new FloatMenuOption("Unassigned", delegate
                            {
                                field.SetValue(SelectedRule.Parameters, null);
                                SelectedRule.Parameters.WorktypeString = "";
                                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            })
                        };

                        foreach (var def in DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderByDescending(w => w.naturalPriority))
                        {
                            defOptions.Add(new FloatMenuOption(def.labelShort.CapitalizeFirst(), delegate
                            {
                                field.SetValue(SelectedRule.Parameters, def);
                                SelectedRule.Parameters.WorktypeString = def.defName;
                                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(defOptions));
                    }
                    continue;
                }

                if (field.FieldType == typeof(XenotypeDef))
                {
                    XenotypeDef refValue = (XenotypeDef)field.GetValue(SelectedRule.Parameters) ?? null;
                    Widgets.Label(rect5, paramLabel);

                    if (uneditable)
                    {
                        GUI.color = Color.gray;
                        Widgets.ButtonImageWithBG(rect5.RightPart(0.25f), refValue?.Icon ?? TexButton.CloseXSmall, new Vector2(22f, 22f));
                        GUI.color = Color.white;
                    }
                    else if (Widgets.ButtonImageWithBG(rect5.RightPart(0.25f), refValue?.Icon ?? TexButton.CloseXSmall, new Vector2(22f, 22f)))
                    {
                        List<FloatMenuOption> defOptions = new List<FloatMenuOption>()
                        {
                            new FloatMenuOption("Unassigned", delegate
                            {
                                field.SetValue(SelectedRule.Parameters, null);
                                SelectedRule.Parameters.XenotypeString = "";
                                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            })
                        };
                        foreach (var def in DefDatabase<XenotypeDef>.AllDefsListForReading)
                        {
                            defOptions.Add(new FloatMenuOption(def.LabelCap, delegate
                            {
                                field.SetValue(SelectedRule.Parameters, def);
                                SelectedRule.Parameters.XenotypeString = def.defName;
                                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            }, def.Icon, XenotypeDef.IconColor, MenuOptionPriority.Default));
                        }
                        Find.WindowStack.Add(new FloatMenu(defOptions));
                    }
                    continue;
                }

                if (field.FieldType == typeof(Tuple<TraitDef, int>))
                {
                    Tuple<TraitDef, int> refValue = (Tuple<TraitDef, int>)field.GetValue(SelectedRule.Parameters) ?? null;
                    Widgets.Label(rect5, paramLabel);
                    string label = "Unassigned";
                    if (SelectedRule.Parameters.RequiredTrait != null && refValue?.Item1 != null)
                    {
                        label = refValue.Item1.DataAtDegree(SelectedRule.Parameters.RequiredTrait.Item2).LabelCap;
                    }

                    Rect traitButtonRect = new Rect(rect5);
                    traitButtonRect.width = rect5.width - Text.CalcSize(field.Name).x - 64f;
                    traitButtonRect.x = rect5.xMax - traitButtonRect.width;

                    if (uneditable) GUI.color = Color.gray;
                    if (Widgets.ButtonText(traitButtonRect, label, active: !uneditable))
                    {
                        List<FloatMenuOption> list = new List<FloatMenuOption>()
                        {
                            new FloatMenuOption("Unassigned", delegate
                            {
                                field.SetValue(SelectedRule.Parameters, null);
                                SelectedRule.Parameters.TraitString = "";
                                SelectedRule.Parameters.TraitDegree = null;
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
                                    field.SetValue(SelectedRule.Parameters, new Tuple<TraitDef, int>(localDef, localDeg.degree));
                                    SelectedRule.Parameters.TraitString = localDef.defName;
                                    SelectedRule.Parameters.TraitDegree = localDeg.degree;
                                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                                }));
                            }
                        }
                        Find.WindowStack.Add(new FloatMenu(list));
                    }
                    continue;
                }

                Text.Font = fontsize;
                GUI.color = Color.white;

                if (field.FieldType == typeof(WorkAssignmentParameters))
                {
                    Widgets.Label(rect5, "NESTED RULES NOT SUPPORTED");
                    continue;
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws a UI control with +/- buttons and text field for integer editing.
        /// </summary>
        public static void DrawPlusMinusOneField(Rect rect, ref int value, ref string editBuffer, int multiplier = 1, bool disabled = false)
        {
            if (disabled) GUI.color = Color.gray;

            Rect leftRect = rect.LeftPart(0.33f);
            Rect rightRect = rect.RightPart(0.33f);
            Rect midRect = rect.MiddlePart(0.33f, 1f);

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
                Widgets.Label(midRect, value.ToString());
                Text.Anchor = b4;
            }
            else
            {
                Widgets.TextFieldNumeric(midRect, ref value, ref editBuffer);
            }
            value = Mathf.Clamp(value, -1, 4);
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
                ruleNameBuffer = Widgets.TextField(titleRect, ruleNameBuffer, 21);

            rect2.height -= 10f;
            rect2.y += 10f;

            Rect rect3 = rect2;
            rect3.xMin += 10f;
            rect3.xMax -= 10f;
            rect3.y = rect2.yMax - Window.CloseButSize.y - 10f;
            rect3.height = Window.CloseButSize.y;
            Rect outRect = rect2;
            outRect.yMax = rect3.y - 10f;
            Widgets.DrawMenuSection(rect2);

            rect3.SplitHorizontally(rect3.height * 0.5f, out Rect topRect, out Rect bottomRect);

            if (uneditable) GUI.color = Color.gray;
            if (Widgets.ButtonText(topRect, "New Rule", active: !uneditable))
            {
                WorkAssignmentRule newRule = new WorkAssignmentRule(new WorkAssignmentParameters("New Rule " + (selectedRuleset.Rules.Count + 1), 0));
                selectedRuleset.Rules.Add(newRule);
                SelectedRule = newRule;
            }
            if (Widgets.ButtonText(bottomRect, "Duplicate Rule", active: !uneditable))
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
                Widgets.Label(outRect, "No rules in ruleset");
                Text.Anchor = defaultAnchor;
                GUI.color = Color.white;
                return;
            }

            int num = selectedRuleset.Rules.Count;

            Rect viewRect = new Rect(0f, 0f, outRect.width, num * RuleRowHeight);
            Widgets.AdjustRectsForScrollView(rect2, ref outRect, ref viewRect);

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
                using (new TextBlock(TextAnchor.MiddleLeft))
                {
                    Widgets.Label(rect5, text);
                }

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
                        _rulesMouseDown = true;
                        _rulesMouseDownPos = evt.mousePosition;

                        float localY = evt.mousePosition.y - listScreenRect.y + midScroll.y;
                        int index = Mathf.FloorToInt(localY / RuleRowHeight);

                        if (index >= 0 && index < rules.Count)
                        {
                            _pendingRuleDrag = rules[index];
                            SelectedRule = _pendingRuleDrag;
                        }
                        else
                        {
                            _pendingRuleDrag = null;
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_rulesMouseDown && _pendingRuleDrag != null && !uneditable)
                    {
                        if ((evt.mousePosition - _rulesMouseDownPos).magnitude > 5f)
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
                                    evt.Use();
                                }
                            }

                            _rulesMouseDown = false;
                            _pendingRuleDrag = null;
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (_rulesMouseDown && _pendingRuleDrag != null)
                    {
                        SelectedRule = _pendingRuleDrag;
                        evt.Use();
                    }

                    _rulesMouseDown = false;
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

            if (Widgets.ButtonImage(rect6, TexButton.Delete))
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