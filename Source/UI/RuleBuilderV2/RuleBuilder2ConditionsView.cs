using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Rules.RuleBuilder2;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

using static Better_Work_Tab.UI.RuleBuilderV2.RuleBuilder2UiUtility;

namespace Better_Work_Tab.UI.RuleBuilderV2
{
    internal sealed class RuleBuilder2ConditionsView
    {
        private readonly Window_RuleBuilder2 window;
        private readonly RuleBuilder2FlowController flow;
        private readonly RuleBuilder2Layout layout;
        private readonly RuleBuilder2TutorialController tutorial;
        private string conditionSearch = "";
        private Vector2 activeConditionsScroll;

        internal RuleBuilder2ConditionsView(
            Window_RuleBuilder2 window,
            RuleBuilder2FlowController flow,
            RuleBuilder2Layout layout,
            RuleBuilder2TutorialController tutorial)
        {
            this.window = window;
            this.flow = flow;
            this.layout = layout;
            this.tutorial = tutorial;
        }

        internal void DrawConditionsSection(Rect rect, RuleBuilder2Card card)
        {
            Rect outerRect = rect;
            GUI.BeginGroup(outerRect);
            rect = new Rect(0f, 0f, outerRect.width, outerRect.height);
            DrawSectionChrome(rect, T("BWT_RuleBuilder2_ConditionsBlockTitle"));
            Rect add = new Rect(rect.xMax - 142f, rect.y + 6f, 132f, 26f);
            if (Widgets.ButtonText(add, "+ " + T("BWT_RuleBuilder2_AddCondition")))
            {
                ShowAddConditionMenu(card);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(add, T("BWT_RuleBuilder2_AddCondition_Tooltip"));

            Rect active = new Rect(rect.x + 10f, rect.y + 38f, rect.width - 20f, Mathf.Max(0f, rect.height - 48f));
            DrawActiveConditions(active, card);
            GUI.EndGroup();
        }

        internal void DrawActiveConditions(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.45f));
            var conditions = card.Conditions.Conditions;

            if (conditions.Count == 0)
            {
                GUI.color = Color.gray;
                DrawSafeLabel(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 50f), T("BWT_RuleBuilder2_NoConditions"));
                GUI.color = Color.white;
                return;
            }

            Rect inner = rect.ContractedBy(6f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, conditions.Count * layout.Metrics.ConditionRowStride));
            Widgets.BeginScrollView(inner, ref activeConditionsScroll, view);
            float y = 0f;
            for (int i = 0; i < conditions.Count; i++)
            {
                RuleBuilder2Condition condition = conditions[i];
                Rect row = new Rect(0f, y, view.width, layout.Metrics.ConditionRowHeight);
                DrawConditionRow(row, card, condition, i);
                y += layout.Metrics.ConditionRowStride;
            }
            Widgets.EndScrollView();
        }

        internal void DrawConditionRow(Rect rect, RuleBuilder2Card card, RuleBuilder2Condition condition, int index)
        {
            RuleBuilder2ConditionRowRects row = layout.ConditionRow(rect);
            Widgets.DrawBoxSolid(rect, condition.Enabled ? new Color(0.18f, 0.18f, 0.18f, 0.95f) : new Color(0.11f, 0.11f, 0.11f, 0.95f));
            Widgets.DrawBox(rect, 1);

            Widgets.Checkbox(row.Checkbox.position, ref condition.Enabled);

            string text = RuleBuilder2ConditionCatalog.GetConditionText(condition, card.Target.ResolveWorkType());
            DrawFittedLabel(row.Label, text);
            DrawConditionInlineEditor(row.Editor, card, condition);

            if (Widgets.ButtonText(row.Up, "^") && index > 0)
            {
                card.Conditions.Conditions.RemoveAt(index);
                card.Conditions.Conditions.Insert(index - 1, condition);
                flow.RefreshPreview();
            }

            if (Widgets.ButtonText(row.Down, "v") && index < card.Conditions.Conditions.Count - 1)
            {
                card.Conditions.Conditions.RemoveAt(index);
                card.Conditions.Conditions.Insert(index + 1, condition);
                flow.RefreshPreview();
            }

            if (Widgets.ButtonText(row.Remove, "X"))
            {
                card.Conditions.Conditions.Remove(condition);
                flow.RefreshPreview();
            }
        }

        internal void ShowAddConditionMenu(RuleBuilder2Card card)
        {
            bool showAdvanced = BetterWorkTabMod.Settings?.ruleBuilder2ShowAdvancedConditions ?? false;
            var options = new List<FloatMenuOption>();
            foreach (var group in RuleBuilder2ConditionCatalog.Grouped(showAdvanced, ""))
            {
                options.Add(new FloatMenuOption(group.Key, null));
                foreach (var def in group)
                {
                    RuleBuilder2ConditionDefinition local = def;
                    options.Add(new FloatMenuOption("  " + local.Label, () =>
                    {
                        RuleBuilder2Condition condition = local.Create();
                        card.Conditions.Conditions.Add(condition);
                        tutorial.ObserveConditionAdded();
                        flow.RefreshPreview();
                    }));
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        internal void DrawConditionInlineEditor(Rect rect, RuleBuilder2Card card, RuleBuilder2Condition condition)
        {
            switch (condition.Kind)
            {
                case RuleBuilder2ConditionKind.SkillMinimum:
                case RuleBuilder2ConditionKind.SkillMaximum:
                case RuleBuilder2ConditionKind.PassionAtLeast:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 110f, rect.height), GetSkillButtonLabel(condition, card.Target.ResolveWorkType())))
                    {
                        ShowSkillMenu(condition, card.Target.ResolveWorkType());
                    }
                    DrawIntStepper(new Rect(rect.x + 118f, rect.y, 98f, rect.height), ref condition.IntValue, 0, 99);
                    break;
                case RuleBuilder2ConditionKind.Trait:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 180f, rect.height), GetDefButtonLabel<TraitDef>(condition, T("BWT_SelectTrait"))))
                    {
                        ShowDefMenu<TraitDef>(condition);
                    }
                    break;
                case RuleBuilder2ConditionKind.Xenotype:
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19 && !v0_18 && !v0_17 && !v0_16 && !v0_15 && !v0_14 && !v0_13 && !vAlpha4
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 180f, rect.height), GetDefButtonLabel<XenotypeDef>(condition, T("BWT_RuleBuilder2_XenotypeFallback"))))
                    {
                        ShowDefMenu<XenotypeDef>(condition, int.MaxValue);
                    }
#else
                    DrawFittedLabel(new Rect(rect.x, rect.y, 180f, rect.height), condition.DefName.NullOrEmpty() ? T("BWT_RuleBuilder2_XenotypeFallback") : condition.DefName);
#endif
                    break;
                case RuleBuilder2ConditionKind.CapacityMinimum:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 150f, rect.height), GetDefButtonLabel<PawnCapacityDef>(condition, T("BWT_RuleBuilder2_CapacityFallback"))))
                    {
                        ShowDefMenu<PawnCapacityDef>(condition);
                    }
                    DrawFloatStepper(new Rect(rect.x + 158f, rect.y, 110f, rect.height), ref condition.FloatValue, 0f, 2f);
                    break;
                case RuleBuilder2ConditionKind.Gender:
                    if (Widgets.ButtonText(new Rect(rect.x, rect.y, 110f, rect.height), condition.TextValue.NullOrEmpty() ? T("BWT_RuleBuilder2_AnyGender") : condition.TextValue))
                    {
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption(T("BWT_RuleBuilder2_GenderMale"), () => condition.TextValue = Gender.Male.ToString()),
                            new FloatMenuOption(T("BWT_RuleBuilder2_GenderFemale"), () => condition.TextValue = Gender.Female.ToString())
                        }));
                    }
                    break;
                case RuleBuilder2ConditionKind.CurrentAssignedWork:
                    Widgets.CheckboxLabeled(new Rect(rect.x, rect.y, 170f, rect.height), T("BWT_RuleBuilder2_Assigned"), ref condition.BoolValue);
                    break;
                default:
                    DrawIntStepper(new Rect(rect.x, rect.y, 98f, rect.height), ref condition.IntValue, 0, RuleBuilder2PriorityRange.Max);
                    RuleBuilder2PriorityRange.NormalizeCondition(condition);
                    break;
            }
        }

        internal void DrawConditionPicker(Rect rect, RuleBuilder2Card card)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.45f));
            Widgets.DrawBox(rect, 1);

            float y = rect.y + 6f;
            bool showAdvanced = BetterWorkTabMod.Settings?.ruleBuilder2ShowAdvancedConditions ?? false;
            foreach (var group in RuleBuilder2ConditionCatalog.Grouped(showAdvanced, conditionSearch))
            {
                GUI.color = Color.yellow;
                DrawFittedLabel(new Rect(rect.x + 8f, y, rect.width - 16f, 24f), group.Key);
                GUI.color = Color.white;
                y += 26f;

                foreach (var def in group)
                {
                    Rect button = new Rect(rect.x + 8f, y, rect.width - 16f, 26f);
                    if (Widgets.ButtonText(button, def.Label))
                    {
                        RuleBuilder2Condition condition = def.Create();
                        card.Conditions.Conditions.Add(condition);
                        tutorial.ObserveConditionAdded();
                    }

                    if (!def.Tooltip.NullOrEmpty())
                    {
                        TooltipHandler.TipRegion(button, def.Tooltip);
                    }

                    y += 30f;
                    if (y > rect.yMax - 26f)
                    {
                        return;
                    }
                }
            }
        }

        internal string GetSkillButtonLabel(RuleBuilder2Condition condition, WorkTypeDef workType)
        {
            SkillDef skill = condition.DefName.NullOrEmpty()
                ? workType?.relevantSkills?.FirstOrDefault()
                : DefDatabase<SkillDef>.GetNamedSilentFail(condition.DefName);
            return skill?.LabelCap.ToString() ?? T("BWT_RuleBuilder2_SkillFallback");
        }

        internal void ShowSkillMenu(RuleBuilder2Condition condition, WorkTypeDef workType)
        {
            var options = RuleBuilder2ConditionCatalog.GetSkillOptions(workType)
                .Select(skill => new FloatMenuOption(skill.LabelCap.ToString(), () => condition.DefName = skill.defName))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        internal static string GetDefButtonLabel<T>(RuleBuilder2Condition condition, string fallback) where T : Def
        {
            if (condition.DefName.NullOrEmpty())
            {
                return fallback;
            }

            T def = DefDatabase<T>.GetNamedSilentFail(condition.DefName);
            return def?.LabelCap.ToString() ?? condition.DefName;
        }

        internal static void ShowDefMenu<T>(RuleBuilder2Condition condition, int maxOptions = 80) where T : Def
        {
            var options = DefDatabase<T>.AllDefsListForReading
                .OrderBy(def => def.label)
                .Take(maxOptions)
                .Select(def => new FloatMenuOption(def.LabelCap.ToString(), () => condition.DefName = def.defName))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
