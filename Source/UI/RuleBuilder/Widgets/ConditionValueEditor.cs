using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.ModSupport;
using Better_Work_Tab.UI.RuleBuilder.Services;
using Better_Work_Tab.UI.RuleBuilder.State;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    /// <summary>
    /// Inline editor for condition values. Renders controls appropriate to the condition type.
    /// </summary>
    public static class ConditionValueEditor
    {
        private const float FieldHeight = 26f;
        private const float ButtonSize = 24f;
        private const float SmallButtonSize = 22f;

        /// <summary>
        /// Draw an inline editor for the given condition. Returns true if the value changed.
        /// </summary>
        public static bool Draw(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            return condition.Type switch
            {
                ConditionType.Bool => DrawBoolEditor(rect, condition, parameters, state),
                ConditionType.Int => DrawIntEditor(rect, condition, parameters, state),
                ConditionType.IntRange => DrawIntRangeEditor(rect, condition, parameters, state),
                ConditionType.Passion => DrawPassionEditor(rect, condition, parameters, state),
                ConditionType.Gender => DrawGenderEditor(rect, condition, parameters, state),
                ConditionType.Trait => DrawTraitEditor(rect, condition, parameters, state),
#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
                ConditionType.Xenotype => DrawXenotypeEditor(rect, condition, parameters, state),
#endif
                _ => false
            };
        }

        private static bool DrawBoolEditor(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            bool currentValue = (bool)ConditionRegistry.GetValue(condition.Key, parameters);
            bool newValue = currentValue;

            Rect checkRect = new Rect(rect.x + 8f, rect.y + 2f, 24f, 24f);
            Verse.Widgets.Checkbox(checkRect.x, checkRect.y, ref newValue);

            if (newValue != currentValue)
            {
                ConditionRegistry.SetValue(condition.Key, parameters, newValue);
                state.NotifyRulesModified();
                return true;
            }

            return false;
        }

        private static bool DrawIntEditor(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            int currentValue = (int)ConditionRegistry.GetValue(condition.Key, parameters);
            int minValue = condition.MinValue;
            int maxValue = condition.MaxValue;

            if (maxValue - minValue <= 10)
            {
                return DrawIntButtonGrid(rect, condition, currentValue, minValue, maxValue, parameters, state);
            }

            return DrawIntTextField(rect, condition, currentValue, minValue, maxValue, parameters, state);
        }

        private static bool DrawIntButtonGrid(
            Rect rect,
            ConditionInfo condition,
            int currentValue,
            int minValue,
            int maxValue,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            int range = maxValue - minValue + 1;
            float buttonWidth = Mathf.Max(SmallButtonSize, (rect.width - 8f) / range);

            for (int i = minValue; i <= maxValue; i++)
            {
                Rect buttonRect = new Rect(
                    rect.x + (i - minValue) * (buttonWidth + 2f),
                    rect.y + 2f,
                    buttonWidth,
                    SmallButtonSize);

                bool isSelected = currentValue == i;
                Color bgColor = isSelected
                    ? RuleBuilderConstants.CardBackgroundSelected
                    : RuleBuilderConstants.CardBackground;

                if (Mouse.IsOver(buttonRect) && !isSelected)
                {
                    bgColor = RuleBuilderConstants.CardBackgroundHover;
                }

                Verse.Widgets.DrawBoxSolid(buttonRect, bgColor);
                Verse.Widgets.DrawBox(buttonRect, isSelected ? 2 : 1);

                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                GUI.color = isSelected ? Color.white : RuleBuilderConstants.LabelColor;
                Verse.Widgets.Label(buttonRect, i.ToString());
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                if (Verse.Widgets.ButtonInvisible(buttonRect))
                {
                    ConditionRegistry.SetValue(condition.Key, parameters, i);
                    state.NotifyRulesModified();
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    return true;
                }
            }

            return false;
        }

        private static bool DrawIntTextField(
            Rect rect,
            ConditionInfo condition,
            int currentValue,
            int minValue,
            int maxValue,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            bool modified = false;
            int newValue = currentValue;

            Rect minusRect = new Rect(rect.x, rect.y, ButtonSize, FieldHeight);
            if (Verse.Widgets.ButtonText(minusRect, "-"))
            {
                newValue = Mathf.Max(minValue, currentValue - 1);
                modified = true;
            }

            Rect fieldRect = new Rect(minusRect.xMax + 4f, rect.y, rect.width - ButtonSize * 2 - 12f, FieldHeight);
            string buffer = newValue.ToString();
            Verse.Widgets.TextFieldNumeric(fieldRect, ref newValue, ref buffer, minValue, maxValue);
            if (newValue != currentValue)
            {
                modified = true;
            }

            Rect plusRect = new Rect(fieldRect.xMax + 4f, rect.y, ButtonSize, FieldHeight);
            if (Verse.Widgets.ButtonText(plusRect, "+"))
            {
                newValue = Mathf.Min(maxValue, currentValue + 1);
                modified = true;
            }

            if (modified)
            {
                newValue = Mathf.Clamp(newValue, minValue, maxValue);
                ConditionRegistry.SetValue(condition.Key, parameters, newValue);
                state.NotifyRulesModified();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            return modified;
        }

        private static bool DrawPassionEditor(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            int currentValue = (int)ConditionRegistry.GetValue(condition.Key, parameters);
            IList<VanillaSkillsExpandedSupport.PassionOption> options =
                VanillaSkillsExpandedSupport.GetPassionOptions();

            if (options.Count <= 3)
            {
                return DrawPassionButtonGrid(rect, condition, parameters, state, currentValue, options);
            }

            string label = VanillaSkillsExpandedSupport.GetPassionLabel(currentValue);
            float buttonWidth = Mathf.Min(180f, rect.width);
            Rect buttonRect = new Rect(
                rect.x,
                rect.y + (rect.height - FieldHeight) / 2f,
                buttonWidth,
                FieldHeight);

            if (Verse.Widgets.ButtonText(buttonRect, label))
            {
                var menuOptions = new List<FloatMenuOption>();
                foreach (VanillaSkillsExpandedSupport.PassionOption option in options)
                {
                    VanillaSkillsExpandedSupport.PassionOption localOption = option;
                    menuOptions.Add(new FloatMenuOption(localOption.Label, () =>
                    {
                        ConditionRegistry.SetValue(condition.Key, parameters, localOption.Value);
                        state.NotifyRulesModified();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(menuOptions));
                return true;
            }

            return false;
        }

        private static bool DrawPassionButtonGrid(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state,
            int currentValue,
            IList<VanillaSkillsExpandedSupport.PassionOption> options)
        {
            float buttonWidth = (rect.width - 8f) / Mathf.Max(1, options.Count);

            for (int i = 0; i < options.Count; i++)
            {
                VanillaSkillsExpandedSupport.PassionOption option = options[i];
                Rect buttonRect = new Rect(
                    rect.x + i * (buttonWidth + 2f),
                    rect.y + 2f,
                    buttonWidth,
                    FieldHeight);

                bool isSelected = currentValue == option.Value;
                Color bgColor = isSelected
                    ? RuleBuilderConstants.CardBackgroundSelected
                    : RuleBuilderConstants.CardBackground;

                if (Mouse.IsOver(buttonRect) && !isSelected)
                {
                    bgColor = RuleBuilderConstants.CardBackgroundHover;
                }

                Verse.Widgets.DrawBoxSolid(buttonRect, bgColor);
                Verse.Widgets.DrawBox(buttonRect, isSelected ? 2 : 1);

                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Small;
                GUI.color = isSelected ? Color.white : RuleBuilderConstants.LabelColor;
                Verse.Widgets.Label(buttonRect, option.Label);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                if (Verse.Widgets.ButtonInvisible(buttonRect))
                {
                    ConditionRegistry.SetValue(condition.Key, parameters, option.Value);
                    state.NotifyRulesModified();
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    return true;
                }
            }

            return false;
        }

        private static bool DrawGenderEditor(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            var currentValue = (Gender?)ConditionRegistry.GetValue(condition.Key, parameters);
            string label = currentValue?.ToString() ?? "BWT_Any".Translate();

            float buttonWidth = Mathf.Min(95f, rect.width);
            float buttonHeight = Mathf.Max(18f, FieldHeight - 6f);
            Rect buttonRect = new Rect(
                rect.x,
                rect.y + (rect.height - buttonHeight) / 2f,
                buttonWidth,
                buttonHeight);

            if (Verse.Widgets.ButtonText(buttonRect, label))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("BWT_Any".Translate(), () =>
                    {
                        ConditionRegistry.SetValue(condition.Key, parameters, null);
                        state.NotifyRulesModified();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };

                foreach (Gender gender in Enum.GetValues(typeof(Gender)))
                {
                    var localGender = gender;
                    options.Add(new FloatMenuOption(gender.ToString(), () =>
                    {
                        ConditionRegistry.SetValue(condition.Key, parameters, localGender);
                        state.NotifyRulesModified();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }

            return false;
        }

        private static bool DrawTraitEditor(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            var currentValue = (Tuple<TraitDef, int>)ConditionRegistry.GetValue(condition.Key, parameters);
            string label = TraitCompat.LabelCap(currentValue?.Item1?.DataAtDegree(currentValue.Item2))
                ?? "BWT_SelectTrait".Translate();

            float buttonWidth = Mathf.Min(150f, rect.width);
            float buttonHeight = Mathf.Max(18f, FieldHeight - 6f);
            Rect buttonRect = new Rect(
                rect.x,
                rect.y + (rect.height - buttonHeight) / 2f,
                buttonWidth,
                buttonHeight);

            if (Verse.Widgets.ButtonText(buttonRect, label))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("BWT_None".Translate(), () =>
                    {
                        ConditionRegistry.SetValue(condition.Key, parameters, null);
                        state.NotifyRulesModified();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };

                var traits = DefDatabase<TraitDef>.AllDefs
                    .OrderBy(t => t.defName)
                    .ToList();

                foreach (var trait in traits)
                {
                    var localTrait = trait;
                    if (trait.degreeDatas == null || trait.degreeDatas.Count == 0)
                    {
                        options.Add(new FloatMenuOption(localTrait.LabelCap, () =>
                        {
                            var tuple = new Tuple<TraitDef, int>(localTrait, 0);
                            ConditionRegistry.SetValue(condition.Key, parameters, tuple);
                            state.NotifyRulesModified();
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        }));
                        continue;
                    }

                    foreach (var degreeData in trait.degreeDatas)
                    {
                        var localDegree = degreeData;
                        options.Add(new FloatMenuOption(TraitCompat.LabelCap(degreeData), () =>
                        {
                            var tuple = new Tuple<TraitDef, int>(localTrait, localDegree.degree);
                            ConditionRegistry.SetValue(condition.Key, parameters, tuple);
                            state.NotifyRulesModified();
                            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        }));
                    }
                }

                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }

            return false;
        }

#if !v1_3 && !v1_2 && !v1_1 && !v1_0 && !v0_19
        private static bool DrawXenotypeEditor(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            if (!ModsConfig.BiotechActive)
            {
                GUI.color = RuleBuilderConstants.DisabledColor;
                Verse.Widgets.Label(rect, "BWT_BiotechRequired".Translate());
                GUI.color = Color.white;
                return false;
            }

            var currentValue = parameters.Xenotype;
            string label = currentValue?.LabelCap ?? "BWT_Any".Translate();

            if (Verse.Widgets.ButtonText(rect, label))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("BWT_Any".Translate(), () =>
                    {
                        parameters.Xenotype = null;
                        state.NotifyRulesModified();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    })
                };

                foreach (var xenotype in DefDatabase<XenotypeDef>.AllDefs)
                {
                    var localXeno = xenotype;
                    options.Add(new FloatMenuOption(xenotype.LabelCap, () =>
                    {
                        parameters.Xenotype = localXeno;
                        state.NotifyRulesModified();
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
                return true;
            }

            return false;
        }
#endif

        private static bool DrawIntRangeEditor(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            RuleBuilderState state)
        {
            // IntRange not currently used; stub for future expansion
            return false;
        }
    }
}
