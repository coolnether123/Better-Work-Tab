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


        private WorkAssignmentRuleset CurrentRuleset => Settings.CurrentRuleset;

        private List<WorkAssignmentRule> RulesetRules
        {
            get
            {
                return CurrentRuleset.Rules;
            }
        }
        WorkAssignmentRule SelectedRule;


        public override void DoWindowContents(Rect inRect)
        {
            Rect leftRect;
            Rect midRect;
            Rect rightRect;

            inRect.SplitVerticallyWithMargin(out Rect leftSide, out rightRect, 10f);
            leftSide.SplitVerticallyWithMargin(out leftRect, out midRect, 10f);

            DoRulesetListing(leftRect);
            DoRulesetRulesListing(midRect);
            DoRuleContents(rightRect, SelectedRule);
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
            

            float num2 = 0f;
            int num3 = 0;

            //Log.Message("WorkAssignmentParameters Parameters: " + typeof(WorkAssignmentParameters).GetConstructors().First().GetParameters().Count() ?? "null");

            foreach (ParameterInfo param in typeof(WorkAssignmentParameters).GetConstructors().First().GetParameters())
            {
                //Log.Message("Creating entry for: "+param.Name);
                Rect rect4 = new Rect(0f, num2, outRect.width - 30f, 32f);
                Rect rect5 = rect4;
                rect5.x += 10f;
                num2 += 32f;
                Rect rightPart = rect5.RightPart(0.25f);
                string text = param.Name;
                //using (new TextBlock(TextAnchor.MiddleLeft))
                //{
                //    Widgets.Label(rect5, text);
                //}
                GUI.color = Color.white;
                var fontsize = Text.Font;

                if (param.ParameterType == typeof(bool))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    bool refValue = (bool)field.GetValue(SelectedRule.Parameters);
                    Widgets.CheckboxLabeled(rect5, text, ref refValue);
                    field.SetValue(SelectedRule.Parameters, refValue);
                    continue;
                }
                GUI.color = Color.white;

                if (param.ParameterType == typeof(int))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    int refValue = (int)field.GetValue(SelectedRule.Parameters);
                    Widgets.Label(rect5, text);
                    if (param.HasDefaultValue && refValue == (int)param.DefaultValue)
                    {
                        GUI.color = Color.gray;
                    }

                    string editBuffer = refValue.ToString();
                    DrawPlusMinusOneField(rightPart, ref refValue, ref editBuffer);
                    field.SetValue(SelectedRule.Parameters, refValue);

                    continue;

                }
                GUI.color = Color.white;

                if (param.ParameterType == typeof(string))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    string refValue = (string)field.GetValue(SelectedRule.Parameters) == null ? "" : (string)field.GetValue(SelectedRule.Parameters);
                    //Widgets.TextEntryLabeled(rect5, text, refValue);
                    Widgets.Label(rect5, text);
                    Widgets.TextField(rightPart, refValue);

                    field.SetValue(SelectedRule.Parameters, refValue);

                    continue;

                }
                Text.Font = fontsize;
                GUI.color = Color.white;

                Log.Message("type is: " + param.ParameterType);
                if (param.ParameterType == typeof(Gender?))
                {
                    Log.Message("Gender");
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    Gender? refValue = (Gender?)field.GetValue(SelectedRule.Parameters) ?? null;

                    Log.Message(field.Name + " is " + refValue?.ToString() ?? "null");

                    Widgets.Label(rect5, text);
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
                    Widgets.Label(rect5, text);
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
                    Widgets.Label(rect5, text);
                    
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
                    Widgets.Label(rect5, text);
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

                if(param.ParameterType == typeof(float))
                {
                    var field = AccessTools.DeclaredField(typeof(WorkAssignmentParameters), param.Name.CapitalizeFirst());
                    float refValue = (float)field.GetValue(SelectedRule.Parameters);
                    Widgets.Label(rect5, text);
                    if (param.HasDefaultValue && refValue == (float)param.DefaultValue)
                    {
                        GUI.color = Color.gray;
                    }

                    string editBuffer = refValue.ToString();
                    DrawPlusMinusOneField(rightPart, ref refValue, ref editBuffer);
                    field.SetValue(SelectedRule.Parameters, refValue);

                    continue;
                }



                if(param.ParameterType == typeof(WorkAssignmentParameters))
                {

                    Widgets.Label(rect5, "BAHAHA YOU WANT TO DO NESTED RULES??");
                    continue;
                }





            }


            Widgets.EndScrollView();

        }


        public void DefDropdown<T>(Rect rect5, ParameterInfo param, string text) where T : Def
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
            
            
        }
        public static void DrawPlusMinusOneField(Rect rect, ref float value, ref string editBuffer, int multiplier = 1)
        {
            var floatvalue = (int)value;
            DrawPlusMinusOneField(rect, ref floatvalue, ref editBuffer, multiplier);
            value = floatvalue;
        }


        public static void DrawPlusMinusOneField(Rect rect, ref int value, ref string editBuffer, int multiplier = 1)
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
            Rect rect3 = rect2;
            rect3.xMin += 10f;
            rect3.xMax -= 10f;
            rect3.y = rect2.yMax - Window.CloseButSize.y - 10f;
            rect3.height = Window.CloseButSize.y;
            Rect outRect = rect2;
            outRect.yMax = rect3.y - 10f;
            Widgets.DrawMenuSection(rect2);
            if (Widgets.ButtonText(rect3, "New Rule"))
            {
                WorkAssignmentRule newRule = new WorkAssignmentRule(new WorkAssignmentParameters(0));

                Settings.CurrentRuleset.Rules.Add(newRule);
                SelectedRule = newRule ;
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
                    string text = "Rule "+num3;
                    using (new TextBlock(TextAnchor.MiddleLeft))
                    {
                        Widgets.Label(rect5, text);
                    }
                    if (Widgets.ButtonInvisible(rect4))
                    {
                        SelectedRule = item;
                    }
            }
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
            if (Widgets.ButtonText(rect3, "New Ruleset"))
            {
                WorkAssignmentRuleset newRuleset = new WorkAssignmentRuleset("New Ruleset", new List<WorkAssignmentParameters>());
                Settings.SavedRulesets.Add(newRuleset);
                Settings.CurrentRuleset = newRuleset;
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
            var defaultPolicy = Settings.SavedRulesets.First();
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
                    if (Widgets.ButtonInvisible(rect4))
                    {
                        Settings.CurrentRuleset = item;
                        SelectedRule = item.Rules.Any() ? item.Rules.First() : null;
                    }
                }
            }
            Widgets.EndScrollView();
        }
    }
}
