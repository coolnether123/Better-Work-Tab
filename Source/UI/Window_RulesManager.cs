using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using HarmonyLib;
using NAudio.Dmo;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using UnityEngine;
using UnityEngine.UIElements;
using Verse;
using Verse.Noise;
using Verse.Sound;
using static HarmonyLib.Code;

namespace Better_Work_Tab.UI
{
    internal class Window_RulesManager : Window
    {
        private static FieldInfo[] CachedParameterFields;

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
            this.forcePause = true;
            this.doCloseX = true;
            this.preventCameraMotion = true;
            this.resizeable = false;
        }

        public override Vector2 InitialSize => new Vector2(800f, 600f);

        private BetterWorkTabSettings Settings => BetterWorkTabMod.Settings;
        private readonly QuickSearchWidget quickSearch = new QuickSearchWidget();
        private Vector2 leftScroll;
        private Vector2 midScroll;
        private Vector2 rightScroll;
        private string ruleNameBuffer = "";

        // Fixed: Handle null CurrentRuleset safely
        private bool uneditable => CurrentRuleset?.IsDefault ?? false;

        private WorkAssignmentRuleset CurrentRuleset => Settings.CurrentRuleset;

        private List<WorkAssignmentRule> RulesetRules
        {
            get
            {
                return CurrentRuleset?.Rules ?? new List<WorkAssignmentRule>();
            }
        }
        WorkAssignmentRule SelectedRule;

        public override void PreOpen()
        {
            base.PreOpen();
            ruleNameBuffer = CurrentRuleset != null ? CurrentRuleset.Name : "New Rule";
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(inRect, "Manage Rules");
            Text.Font = GameFont.Small;
            float titleHeight = Text.CalcHeight("Manage Rules", 0) + 12f;
            Rect rect = inRect;
            rect.height -= titleHeight;
            rect.y += titleHeight;

            Rect leftRect;
            Rect midRect;
            Rect rightRect;

            rect.SplitVerticallyWithMargin(out Rect leftSide, out rightRect, 10f);
            leftSide.SplitVerticallyWithMargin(out leftRect, out midRect, 10f);

            DoRulesetListing(leftRect);
            if (CurrentRuleset != null)
            {
                DoRulesetRulesListing(midRect);
                DoRuleContents(rightRect, SelectedRule);
            }
        }

        /// <summary>
        /// Draws the UI for editing the parameters of a selected rule.
        /// </summary>
        /// <param name="rightRect">The rectangle to draw the UI in.</param>
        /// <param name="rule">The rule to edit.</param>
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

            int num = typeof(WorkAssignmentParameters).GetConstructors().First().GetParameters().Length;

            Rect viewRect = new Rect(0f, 0f, outRect.width, (float)(num * 32f));
            Widgets.AdjustRectsForScrollView(rect2, ref outRect, ref viewRect);
            Widgets.BeginScrollView(outRect, ref rightScroll, viewRect);

            if (rule == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect3, "No rule selected");
                GUI.color = Color.white;
                Widgets.EndScrollView();
                return;
            }

            SelectedRule.Name = SelectedRule.Parameters.RuleName == "" ? "New Rule " + (RulesetRules.IndexOf(SelectedRule) + 1) : SelectedRule.Parameters.RuleName;

            float num2 = 32f;

            foreach (FieldInfo field in GetParameterFields())
            {
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

                //This has updated to assume there will only ever be one worktype per rule.
                if (field.FieldType == typeof(WorkTypeDef))
                {
                    WorkTypeDef refValue = (WorkTypeDef)field.GetValue(SelectedRule.Parameters) ?? null;

                    // Fallback: try to get WorkTypeDef by name if null
                    if (refValue == null && SelectedRule.Parameters.WorktypeString != null &&SelectedRule.Parameters.WorktypeString != "")
                    {
                        Log.Message("Fallback: retrieving WorkTypeDef by name: " + SelectedRule.Parameters.WorktypeString);
                        refValue = DefDatabase<WorkTypeDef>.GetNamedSilentFail(SelectedRule.Parameters.WorktypeString);
                        SelectedRule.Parameters.Worktype = refValue;
                    }

                    Widgets.Label(rect5, paramLabel);
                    if (uneditable) GUI.color = Color.gray;
                    if (Widgets.ButtonText(rect5.RightPart(0.25f), refValue?.labelShort.CapitalizeFirst() ?? "Unassigned", active: !uneditable))
                    {
                        List<FloatMenuOption> defOptions = new List<FloatMenuOption>()
                        {
                            new FloatMenuOption("Unassigned", delegate
                            {
                                field.SetValue(SelectedRule.Parameters, null);
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

                if (ModsConfig.BiotechActive && field.FieldType == typeof(XenotypeDef))
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
                                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            })
                        };
                        foreach (var def in DefDatabase<XenotypeDef>.AllDefsListForReading)
                        {
                            defOptions.Add(new FloatMenuOption(def.LabelCap, delegate
                            {
                                field.SetValue(SelectedRule.Parameters, def);
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
                    if (SelectedRule.Parameters.RequiredTrait != null && refValue.Item1 != null)
                    {
                        label = refValue.Item1.DataAtDegree(SelectedRule.Parameters.RequiredTrait.Item2).LabelCap;
                    }

                    Rect traitButtonRect = new Rect(rect5);
                    traitButtonRect.width = rect5.width - Text.CalcSize(field.Name).x - 64f;
                    traitButtonRect.x = rect5.xMax - traitButtonRect.width;

                    if (Widgets.ButtonText(traitButtonRect, label, active: !uneditable))
                    {
                        List<FloatMenuOption> list = new List<FloatMenuOption>() {
                            new FloatMenuOption("Unassigned", delegate
                            {
                                field.SetValue(SelectedRule.Parameters, null);
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
                    Widgets.Label(rect5, "BAHAHA YOU WANT TO DO NESTED RULES??");
                    continue;
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws a UI control with +/- buttons and text field for integer editing.
        /// Respects the disabled flag to prevent editing of default rulesets.
        /// </summary>
        public static void DrawPlusMinusOneField(Rect rect, ref int value, ref string editBuffer, int multiplier = 1, bool disabled = false)
        {
            if (disabled) GUI.color = Color.gray;

            Rect leftRect;
            Rect midRect;
            Rect rightRect;

            leftRect = rect.LeftPart(0.33f);
            rightRect = rect.RightPart(0.33f);
            midRect = rect.MiddlePart(0.33f, 1f);

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
        /// Rules can only be edited if the parent ruleset is not a default.
        /// </summary>
        void DoRulesetRulesListing(Rect midRect)
        {
            if (CurrentRuleset == null)
            {
                return;
            }

            Rect rect = midRect;
            rect.y = midRect.yMax - 24f;
            rect.height = 24f;
            Rect rect2 = midRect;
            rect2.yMax = rect.y - 10f;

            rect2.SplitHorizontally(32f, out Rect titleRect, out rect2);

            if (CurrentRuleset != null)
                CurrentRuleset.Name = ruleNameBuffer == "" ? "New Ruleset " + (Settings.SavedRulesets?.IndexOf(CurrentRuleset) + 1) ?? ruleNameBuffer : ruleNameBuffer;

            // Show title as read-only if this is a default ruleset
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

            // Disable rule creation/duplication for default rulesets
            if (uneditable) GUI.color = Color.gray;
            if (Widgets.ButtonText(topRect, "New Rule", active: !uneditable))
            {
                WorkAssignmentRule newRule = new WorkAssignmentRule(new WorkAssignmentParameters("New Rule " + (RulesetRules.Count + 1), 0));
                Settings.CurrentRuleset.Rules.Add(newRule);
                SelectedRule = newRule;
            }
            if (Widgets.ButtonText(bottomRect, "Duplicate Rule", active: !uneditable))
            {
                WorkAssignmentRule newRule = SelectedRule.Copy();
                Settings.CurrentRuleset.Rules.Add(newRule);
                SelectedRule = newRule;
            }
            if (uneditable) GUI.color = Color.white;

            int num = RulesetRules.Count;

            Rect viewRect = new Rect(0f, 0f, outRect.width, (float)num * 32f);
            Widgets.AdjustRectsForScrollView(rect2, ref outRect, ref viewRect);
            Widgets.BeginScrollView(outRect, ref midScroll, viewRect);

            float num2 = 0f;
            int num3 = 0;
            WorkAssignmentRule ruleToRemove = null;

            foreach (var item in RulesetRules)
            {
                Rect rect4 = new Rect(0f, num2, outRect.width, 32f);
                Rect rect5 = rect4;
                rect5.x += 10f;
                num2 += 32f;

                if (SelectedRule == null && CurrentRuleset?.Rules?.Any() == true)
                {
                    SelectedRule = CurrentRuleset.Rules.First();
                }

                if (SelectedRule == item)
                {
                    Widgets.DrawHighlightSelected(rect4);
                }
                else if (Mouse.IsOver(rect4))
                {
                    Widgets.DrawHighlight(rect4);
                }
                else if (num3 % 2 == 1)
                {
                    Widgets.DrawLightHighlight(rect4);
                }

                num3++;
                string text = item.Name;
                using (new TextBlock(TextAnchor.MiddleLeft))
                {
                    Widgets.Label(rect5, text);
                }

                // Only show delete button for rules if ruleset is not default
                if (CurrentRuleset != null && !CurrentRuleset.IsDefault)
                    DoDeleteButton_Rules(ref ruleToRemove, ref rect4, item);

                if (Widgets.ButtonInvisible(rect4))
                {
                    if (SelectedRule.Parameters.RuleName == "")
                    {
                        SelectedRule.Parameters.RuleName = "New Rule " + (RulesetRules.IndexOf(SelectedRule) + 1);
                    }
                    SelectedRule = item;
                }
            }

            if (ruleToRemove != null)
                RulesetRules.Remove(ruleToRemove);

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws delete button for a rule. Only appears if ruleset is not default.
        /// </summary>
        private void DoDeleteButton_Rules(ref WorkAssignmentRule ruleToRemove, ref Rect rect4, WorkAssignmentRule currentRule)
        {
            Rect rect6 = new Rect(rect4);
            rect6.width = 24f;
            rect6.height = 24f;
            rect6.x = rect4.xMax - rect6.width - (RulesetRules.Count >= 13 ? 20f : 0);
            rect6.y = rect4.y + (rect4.height - rect6.height) / 2f;

            if (Widgets.ButtonImage(rect6, TexButton.Delete))
            {
                var newCurrentIndex = Mathf.Clamp(RulesetRules.IndexOf(currentRule) - 1, 0, int.MaxValue);
                ruleToRemove = currentRule;
                        SelectedRule = null;
                //if (ruleToRemove == SelectedRule)
                //{

                //    Log.Message("Selected rule is being deleted.");
                //    if (RulesetRules.Count - 1 <= 0)
                //    else
                //        SelectedRule = RulesetRules[newCurrentIndex+1];
                //}
            }
        }

        /// <summary>
        /// Draws the list of saved rulesets.
        /// Default rulesets are marked with an asterisk and cannot be deleted.
        /// </summary>
        void DoRulesetListing(Rect leftRect)
        {
            Rect rect = leftRect;
            rect.y = leftRect.yMax - 24f;
            rect.height = 24f;
            Rect rect2 = leftRect;
            rect2.yMax = rect.y - 10f;
            Rect rect3 = rect2;
            rect3.xMin += 10f;
            rect3.xMax -= 10f;
            rect3.y = rect2.yMax - Window.CloseButSize.y - 10f;
            rect3.height = Window.CloseButSize.y;
            Rect outRect = rect2;
            outRect.yMax = rect3.y - 10f;

            quickSearch.OnGUI(rect);
            Widgets.DrawMenuSection(rect2);

            rect3.SplitHorizontally(rect3.height * 0.5f, out Rect top, out Rect bottom);

            if (Widgets.ButtonText(top, "New Ruleset"))
            {
                WorkAssignmentRuleset newRuleset = new WorkAssignmentRuleset("New Ruleset", new List<WorkAssignmentParameters>());
                Settings.SavedRulesets.Add(newRuleset);
                Settings.CurrentRuleset = newRuleset;
                ruleNameBuffer = Settings.CurrentRuleset.Name;

                WorkAssignmentRule newRule = new WorkAssignmentRule(new WorkAssignmentParameters("New Rule " + (RulesetRules.Count + 1), 0));
                Settings.CurrentRuleset.Rules.Add(newRule);
                SelectedRule = newRule;
            }

            if (Widgets.ButtonText(bottom, "Duplicate Ruleset"))
            {
                var newRules = CurrentRuleset.Copy();
                Settings.SavedRulesets.Add(newRules);
                ruleNameBuffer = newRules.Name;
                BetterWorkTabMod.Settings.CurrentRuleset = newRules;
            }

            int num = 0;
            foreach (var ruleset in Settings.SavedRulesets)
            {
                if (quickSearch.filter.Matches(ruleset.Name))
                {
                    num++;
                }
            }

            Rect viewRect = new Rect(0f, 0f, outRect.width, (float)num * 32f);
            Widgets.AdjustRectsForScrollView(rect2, ref outRect, ref viewRect);
            Widgets.BeginScrollView(outRect, ref leftScroll, viewRect);

            float num2 = 0f;
            int num3 = 0;

            var defaultPolicy = Settings.SavedRulesets.Any() ? Settings.SavedRulesets.First() : null;

            if (defaultPolicy == null)
            {
                Widgets.EndScrollView();
                return;
            }

            // Sort rulesets: default ones first, then alphabetically
            foreach (var item in from x in Settings.SavedRulesets
                                 orderby defaultPolicy != x, x.Name
                                 select x)
            {
                if (quickSearch.filter.Matches(item.Name))
                {
                    Rect rect4 = new Rect(0f, num2, outRect.width, 32f);
                    Rect rect5 = rect4;
                    rect5.x += 10f;
                    num2 += 32f;

                    if (CurrentRuleset == item)
                    {
                        Widgets.DrawHighlightSelected(rect4);
                    }
                    else if (Mouse.IsOver(rect4))
                    {
                        Widgets.DrawHighlight(rect4);
                    }
                    else if (num3 % 2 == 1)
                    {
                        Widgets.DrawLightHighlight(rect4);
                    }

                    num3++;
                    string text = item.Name;

                    // Mark default rulesets with an asterisk
                    if (defaultPolicy == item)
                    {
                        text += "*".Colorize(Color.gray);
                    }

                    using (new TextBlock(TextAnchor.MiddleLeft))
                    {
                        Widgets.Label(rect5, text);
                    }

                    // Only show delete button for non-default rulesets
                    if (item != null && !item.IsDefault)
                    {
                        DoDeleteButton(rect4, item);
                    }

                    if (Widgets.ButtonInvisible(rect4))
                    {
                        if (CurrentRuleset.Name == "")
                        {
                            CurrentRuleset.Name = "New Ruleset " + (Settings.SavedRulesets.IndexOf(CurrentRuleset) + 1);
                        }

                        Settings.CurrentRuleset = item;
                        ruleNameBuffer = Settings.CurrentRuleset.Name;
                        SelectedRule = item.Rules.Any() ? item.Rules.First() : null;
                    }
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws the delete button for a ruleset.
        /// Only shows for non-default rulesets.
        /// Passes the ruleset instance to ensure the correct one is deleted.
        /// </summary>
        private void DoDeleteButton(Rect rect4, WorkAssignmentRuleset ruleset)
        {
            Rect rect6 = new Rect(rect4);
            rect6.width = 24f;
            rect6.height = 24f;
            rect6.x = rect4.xMax - rect6.width - (Settings.SavedRulesets.Count >= 14 ? 20f : 0);
            rect6.y = rect4.y + (rect4.height - rect6.height) / 2f;

            if (Widgets.ButtonImage(rect6, TexButton.Delete))
            {
                Find.WindowStack.Add(new Dialog_Confirm($"Really delete {ruleset.Name}?", () => DeleteRuleset(ruleset)));
            }
        }

        /// <summary>
        /// Deletes the specified ruleset and handles the CurrentRuleset reassignment.
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