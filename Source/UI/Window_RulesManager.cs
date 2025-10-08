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

        private WorkAssignmentRuleset CurrentRuleset => Settings.CurrentRuleset;

        private List<WorkAssignmentRule> RulesetRules
        {
            get
            {
                return CurrentRuleset.Rules;
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

            //Rect bottomRight = new Rect(windowRect.xMax - 125, windowRect.yMax - 55, 120, 50);
            //if(Widgets.ButtonText(bottomRight, "Add All Defaults"))
            //{
            //    BetterWorkTabMod.Settings.AddDefaultRules();
            //}

            DoRulesetListing(leftRect);
            if (CurrentRuleset != null)
            {
                DoRulesetRulesListing(midRect);
                DoRuleContents(rightRect, SelectedRule);
            }
        }

        void DoRuleContents(Rect rightRect, WorkAssignmentRule rule)
        {

            Rect rect = rightRect;
            rect.y = rightRect.yMax - 24f;
            rect.height = 24f;
            Rect rect2 = rightRect;
            rect2.yMax = rect.y - 10f;
            Rect rect3 = rect2;
            rect3.xMin += 10f;
            rect3.xMax -= 10f;
            rect3.y = rect2.yMax - Window.CloseButSize.y - 10f;
            rect3.height = Window.CloseButSize.y;
            Rect outRect = rect2;
            outRect.yMax = rect3.y - 10f;
            Widgets.DrawMenuSection(rect2);
            int num = 0;
            foreach (var ruleset in typeof(WorkAssignmentParameters).GetConstructors().First().GetParameters())
            {
                num++;
            }
            Rect viewRect = new Rect(0f, 0f, outRect.width, (float)num * 32f);
            Widgets.AdjustRectsForScrollView(rect2, ref outRect, ref viewRect);
            Widgets.BeginScrollView(outRect, ref rightScroll, viewRect);
            
            if(rule == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(rect3, "No rule selected");
                GUI.color = Color.white;
                Widgets.EndScrollView();
                return;
            }
            SelectedRule.Name = SelectedRule.Parameters.RuleName == "" ? "New Rule " + (RulesetRules.IndexOf(SelectedRule) + 1) : SelectedRule.Parameters.RuleName;

            float num2 = 32f;

            //Log.Message("WorkAssignmentParameters Parameters: " + typeof(WorkAssignmentParameters).GetConstructors().First().GetParameters().Count() ?? "null");

            foreach (ParameterInfo param in typeof(WorkAssignmentParameters).GetConstructors().First().GetParameters())
            {
                //Log.Message("Creating entry for: "+param.Name);
                Rect rect4 = new Rect(0f, num2, outRect.width - 30f, 32f);
                Rect rect5 = rect4;
                rect5.x += 10f;
                num2 += 32f;
                Rect rightPart = rect5.RightPart(0.25f);
                string paramLabel = ("BWT_" + param.Name).Translate();
                //using (new TextBlock(TextAnchor.MiddleLeft))
                //{
                //    Widgets.Label(rect5, text);
                //}
                GUI.color = Color.white;
                var fontsize = Text.Font;

                
                TooltipHandler.TipRegion(rect5, ("BWT_" + param.Name + "_Desc").Translate());

                if (param.ParameterType == typeof(bool))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    bool refValue = (bool)field.GetValue(SelectedRule.Parameters);
                    Widgets.CheckboxLabeled(rect5, paramLabel, ref refValue);
                    field.SetValue(SelectedRule.Parameters, refValue);
                    continue;
                }
                GUI.color = Color.white;

                if (param.ParameterType == typeof(int))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    int refValue = (int)field.GetValue(SelectedRule.Parameters);
                    Widgets.Label(rect5, paramLabel);
                    if (param.HasDefaultValue && refValue == (int)param.DefaultValue)
                    {
                        GUI.color = Color.gray;
                    }

                    string editBuffer = refValue.ToString();
                    DrawPlusMinusOneField(rightPart, ref refValue, ref editBuffer, param);
                    field.SetValue(SelectedRule.Parameters, refValue);

                    continue;

                }
                GUI.color = Color.white;

                if (param.ParameterType == typeof(string))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    string refValue = (string)field.GetValue(SelectedRule.Parameters) == null ? "" : (string)field.GetValue(SelectedRule.Parameters);
                    //Widgets.TextEntryLabeled(rect5, text, refValue);
                    Widgets.Label(rect5, paramLabel);
                    refValue = Widgets.TextField(rect5.RightHalf(), refValue, 24);

                    field.SetValue(SelectedRule.Parameters, refValue);

                    continue;

                }
                Text.Font = fontsize;
                GUI.color = Color.white;

                if (param.ParameterType == typeof(Gender?))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    Gender? refValue = (Gender?)field.GetValue(SelectedRule.Parameters) ?? null;


                    Widgets.Label(rect5, paramLabel);
                    if (Widgets.ButtonText(rightPart, refValue?.ToString() ?? "Unassigned"))
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

                if (param.ParameterType == typeof(WorkTypeDef))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    WorkTypeDef refValue = (WorkTypeDef)field.GetValue(SelectedRule.Parameters) ?? null;
                    Widgets.Label(rect5, paramLabel);
                    if (Widgets.ButtonText(rect5.RightPart(0.25f), refValue?.labelShort.CapitalizeFirst() ?? "Unassigned"))
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
                                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(defOptions));
                    }
                    continue;
                }

                if (param.ParameterType == typeof(XenotypeDef))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    XenotypeDef refValue = (XenotypeDef)field.GetValue(SelectedRule.Parameters) ?? null;
                    Widgets.Label(rect5, paramLabel);
                    
                    if (Widgets.ButtonImageWithBG(rect5.RightPart(0.25f), refValue?.Icon ?? TexButton.CloseXSmall, new Vector2(22f,22f)))
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

                if (param.ParameterType == typeof(Tuple<TraitDef, int>))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    Tuple<TraitDef, int> refValue = (Tuple<TraitDef, int>)field.GetValue(SelectedRule.Parameters) ?? null;
                    Widgets.Label(rect5, paramLabel);
                    string label = "Unassigned";
                    if (SelectedRule.Parameters.RequiredTrait != null && refValue.Item1 != null)
                    {
                        label = refValue.Item1.DataAtDegree(SelectedRule.Parameters.RequiredTrait.Item2).LabelCap;

                    }

                    Rect traitButtonRect = new Rect(rect5);
                    traitButtonRect.width = rect5.width - Text.CalcSize(param.Name).x-64f;
                    traitButtonRect.x = rect5.xMax - traitButtonRect.width;

                    if (Widgets.ButtonText(traitButtonRect,  label))
                    {
                        List<FloatMenuOption> list = new List<FloatMenuOption>() {
                            new FloatMenuOption("Unassigned", delegate
                            {
                                field.SetValue(SelectedRule.Parameters, null);
                                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                            })
                        };

                        foreach (TraitDef item in DefDatabase<TraitDef>.AllDefs.OrderByDescending((TraitDef td) => td.GetGenderSpecificCommonality(Gender.None)))
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

                //if(param.ParameterType == typeof(float))
                //{
                //    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                //    float refValue = (float)field.GetValue(SelectedRule.Parameters);
                //    Widgets.Label(rect5, text);
                //    if (param.HasDefaultValue && refValue == (float)param.DefaultValue)
                //    {
                //        GUI.color = Color.gray;
                //    }

                //    string editBuffer = refValue.ToString();
                //    DrawPlusMinusOneField(rightPart, ref refValue, ref editBuffer, param);
                //    field.SetValue(SelectedRule.Parameters, refValue);

                //    continue;
                //}


                if(param.ParameterType == typeof(WorkAssignmentParameters))
                {
                    Widgets.Label(rect5, "BAHAHA YOU WANT TO DO NESTED RULES??");

                    continue;
                }
            }


            Widgets.EndScrollView();

        }


       /* public void DefDropdown<T>(Rect rect5, ParameterInfo param, string text) where T : Def
        {
            var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
            T refValue = (T)field.GetValue(SelectedRule.Parameters) ?? null;
            Widgets.Label(rect5, text);
            if (Widgets.ButtonText(rect5.RightPart(0.25f), refValue?.label ?? "Unassigned"))
            {
                List<FloatMenuOption> defOptions = new List<FloatMenuOption>()
                {
                    new FloatMenuOption("Unassigned", delegate
                    {
                        field.SetValue(SelectedRule.Parameters, null);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };
                foreach (var def in DefDatabase<T>.AllDefsListForReading)
                {

                    if (def is XenotypeDef)
                    {
                        
                    }
                    else if (def is TraitDef)
                    {
                        var trait = def as TraitDef;
                        defOptions.Add(new FloatMenuOption(trait.degreeDatas.First().label, delegate
                        {
                            field.SetValue(SelectedRule.Parameters, def);
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        }));
                    }
                    else if(def is WorkTypeDef)
                    {

                    }
                }
                Find.WindowStack.Add(new FloatMenu(defOptions));
            }
            
            
        }*/
        
        public static void DrawPlusMinusOneField(Rect rect, ref int value, ref string editBuffer, ParameterInfo param, int multiplier = 1)
        {

            Rect leftRect;
            Rect midRect;
            Rect rightRect;

            leftRect = rect.LeftPart(0.33f);
            rightRect = rect.RightPart(0.33f);
            midRect = rect.MiddlePart(0.33f,1f);

            if (Widgets.ButtonText(leftRect, "-1"))
            {
                value -= 1 * multiplier;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                editBuffer = value.ToString();
            }
            if (Widgets.ButtonText(rightRect, "+1"))
            {
                value += 1 * multiplier;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                editBuffer = value.ToString();
            }
            Widgets.TextFieldNumeric(midRect, ref value, ref editBuffer);
            value = Mathf.Clamp(value, -1, 4);
        }

        void DoRulesetRulesListing(Rect midRect)
        {

            Rect rect = midRect;
            rect.y = midRect.yMax - 24f;
            rect.height = 24f;
            Rect rect2 = midRect;
            rect2.yMax = rect.y - 10f;

            rect2.SplitHorizontally(32f, out Rect titleRect, out rect2);
            if(CurrentRuleset != null)
                CurrentRuleset.Name = ruleNameBuffer == "" ? "New Ruleset " + (Settings.SavedRulesets?.IndexOf(CurrentRuleset) + 1) ?? ruleNameBuffer : ruleNameBuffer;

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
          
            if (Widgets.ButtonText(topRect, "New Rule"))
            {
                WorkAssignmentRule newRule = new WorkAssignmentRule(new WorkAssignmentParameters("New Rule " + (RulesetRules.Count+1), 0));

                Settings.CurrentRuleset.Rules.Add(newRule);
                SelectedRule = newRule ;
            }
            if (Widgets.ButtonText(bottomRect, "Duplicate Rule"))
            {

                WorkAssignmentRule newRule = SelectedRule.Copy();
                Settings.CurrentRuleset.Rules.Add(newRule);
                SelectedRule = newRule;
            }

            int num = 0;
            foreach (var ruleset in RulesetRules)
            {
                    num++;
            }

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
                if (SelectedRule == null)
                {
                    SelectedRule = RulesetRules.Any() ? RulesetRules.First() : null;
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
                Rect rect6 = new Rect(rect4);
                rect6.width = 24f;
                rect6.height = 24f;
                rect6.x = rect4.xMax - rect6.width - (RulesetRules.Count >= 13 ? 20f : 0);
                rect6.y = rect4.y + (rect4.height - rect6.height) / 2f;

                if (Widgets.ButtonImage(rect6, TexButton.Delete))
                {
                    var newCurrentIndex = Mathf.Clamp(RulesetRules.IndexOf(SelectedRule) - 1, 0, int.MaxValue);
                    ruleToRemove = SelectedRule;
                    if (RulesetRules.Count - 1 <= 0)
                        SelectedRule = null;
                    else
                        SelectedRule = RulesetRules[newCurrentIndex];
                }
                
                if (Widgets.ButtonInvisible(rect4))
                {
                    if (SelectedRule.Parameters.RuleName == "")
                    {
                        SelectedRule.Parameters.RuleName = "New Rule " + (RulesetRules.IndexOf(SelectedRule) + 1);
                    }
                    SelectedRule = item;
                }
            }
            if(ruleToRemove != null)
                RulesetRules.Remove(ruleToRemove);


            Widgets.EndScrollView();
        }

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
                    if (defaultPolicy == item)
                    {
                        text += "*".Colorize(Color.gray);
                    }
                    using (new TextBlock(TextAnchor.MiddleLeft))
                    {
                        Widgets.Label(rect5, text);
                    }
                    Rect rect6 = new Rect(rect4);
                    rect6.width = 24f;
                    rect6.height = 24f;
                    rect6.x = rect4.xMax - rect6.width - (Settings.SavedRulesets.Count >= 14 ? 20f : 0);
                    rect6.y = rect4.y + (rect4.height - rect6.height) / 2f;
                    

                    if (Widgets.ButtonImage(rect6, TexButton.Delete))
                    {
                        Find.WindowStack.Add(new Dialog_Confirm("Really delete " + CurrentRuleset.Name + "?", () => {

                            var rulesets = BetterWorkTabMod.Settings.SavedRulesets;
                            if(rulesets.Count == 0)
                            {
                                return;
                            }
                            var count = rulesets.Count;
                            WorkAssignmentRuleset ruleset = null;


                            if (count > rulesets.IndexOf(CurrentRuleset) + 1)
                            {
                                Log.Message("+1 : " + (rulesets.IndexOf(CurrentRuleset) + 1));

                                ruleset = rulesets[rulesets.IndexOf(CurrentRuleset) + 1];
                            }
                            else if (count > rulesets.IndexOf(CurrentRuleset) - 1 && rulesets.IndexOf(CurrentRuleset) - 1 > 0)
                            {
                                Log.Message("-1 : " + (rulesets.IndexOf(CurrentRuleset) - 1));

                                ruleset = rulesets[rulesets.IndexOf(CurrentRuleset) - 1];
                            }

                            BetterWorkTabMod.Settings.SavedRulesets.Remove(CurrentRuleset);
                            ruleNameBuffer = ruleset.Name;
                            BetterWorkTabMod.Settings.CurrentRuleset = ruleset;
                        }));
                        
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

        public override void PostClose()
        {
            base.PostClose();
            BetterWorkTabMod.Settings.Write();
        }
    }
}
