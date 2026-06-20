using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.State;
using System;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.RuleBuilder.Widgets
{
    /// <summary>
    /// Minimal condition editor row. This is intentionally simple so the new
    /// rule builder compiles and provides basic toggles for common conditions.
    /// </summary>
    public static class ConditionBuilderWidget
    {
        public static void Draw(
            Rect rect,
            ConditionInfo condition,
            WorkAssignmentParameters parameters,
            bool disabled,
            Action onChanged)
        {
            Rect labelRect = rect.LeftPart(0.55f);
            Rect valueRect = rect.RightPart(0.35f);
            Rect removeRect = new Rect(rect.xMax - 24f, rect.y, 24f, rect.height);

            var oldColor = GUI.color;
            if (disabled) GUI.color = RuleBuilderConstants.DisabledColor;

            Verse.Widgets.Label(labelRect, condition.Label);

            switch (condition.Type)
            {
                case ConditionType.Bool:
                    DrawBool(condition.Key, parameters, valueRect, disabled, onChanged);
                    break;
                case ConditionType.Int:
                    DrawInt(condition.Key, parameters, valueRect, disabled, onChanged);
                    break;
                case ConditionType.Trait:
                case ConditionType.Gender:
#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
                case ConditionType.Xenotype:
#endif
                    Verse.Widgets.Label(valueRect, "Configure via add menu");
                    break;
                default:
                    Verse.Widgets.Label(valueRect, "-");
                    break;
            }

            if (!disabled && Verse.Widgets.ButtonText(removeRect, "X"))
            {
                ClearCondition(condition.Key, parameters);
                onChanged?.Invoke();
            }

            GUI.color = oldColor;
        }

        private static void DrawBool(string key, WorkAssignmentParameters parameters, Rect valueRect, bool disabled, Action onChanged)
        {
            ref bool target = ref GetBoolRef(key, parameters);
            bool val = target;
            WidgetsCompat.Checkbox(valueRect.x, valueRect.y + 4f, ref val, disabled: disabled, paintable: true);
            if (val != target)
            {
                target = val;
                onChanged?.Invoke();
            }
        }

        private static void DrawInt(string key, WorkAssignmentParameters parameters, Rect valueRect, bool disabled, Action onChanged)
        {
            int current = GetIntValue(key, parameters);
            string buffer = current.ToString();
            Verse.Widgets.TextFieldNumeric(valueRect, ref current, ref buffer, 0, 99);

            if (current != GetIntValue(key, parameters))
            {
                SetIntValue(key, parameters, current);
                onChanged?.Invoke();
            }
        }

        private static void ClearCondition(string key, WorkAssignmentParameters parameters)
        {
            switch (key)
            {
                case "HasHighestSkill":
                    parameters.HasHighestSkill = false;
                    break;
                case "IsTopXSkill":
                    parameters.IsTopXSkill = 0;
                    break;
                case "PassionLevel":
                    parameters.PassionLevel = -1;
                    break;
                case "SkillLevelGreaterThan":
                    parameters.SkillLevelGreaterThan = -1;
                    break;
                case "SkillLevelLessThan":
                    parameters.SkillLevelLessThan = -1;
                    break;
                case "IsNaturalAlwaysAssign":
                    parameters.IsNaturalAlwaysAssign = false;
                    break;
                case "RequiredTrait":
                    parameters.RequiredTrait = null;
                    parameters.TraitString = string.Empty;
                    parameters.TraitDegree = null;
                    break;
                case "Gender":
                    parameters.Gender = null;
                    break;
#if !v1_3 && !v1_2 && !v1_1 && !(v1_0 || v0_19)
                case "Xenotype":
                    parameters.Xenotype = null;
                    parameters.XenotypeString = string.Empty;
                    break;
#endif
                case "IsCapableOfViolence":
                    parameters.IsCapableOfViolence = false;
                    break;
                case "HasChildOnMap":
                    parameters.HasChildOnMap = false;
                    break;
                case "IsPregnant":
                    parameters.IsPregnant = false;
                    break;
                case "RandomIfMultiple":
                    parameters.RandomIfMultiple = false;
                    break;
                case "AllowOverwritingHigherPriority":
                    parameters.AllowOverwritingHigherPriority = false;
                    break;
            }
        }

        private static ref bool GetBoolRef(string key, WorkAssignmentParameters p)
        {
            switch (key)
            {
                case "HasHighestSkill":
                    return ref p.HasHighestSkill;
                case "IsNaturalAlwaysAssign":
                    return ref p.IsNaturalAlwaysAssign;
                case "IsCapableOfViolence":
                    return ref p.IsCapableOfViolence;
                case "HasChildOnMap":
                    return ref p.HasChildOnMap;
                case "IsPregnant":
                    return ref p.IsPregnant;
                case "RandomIfMultiple":
                    return ref p.RandomIfMultiple;
                case "AllowOverwritingHigherPriority":
                    return ref p.AllowOverwritingHigherPriority;
                default:
                    return ref p.HasHighestSkill; // Fallback, shouldn't happen
            }
        }

        private static int GetIntValue(string key, WorkAssignmentParameters p)
        {
            switch (key)
            {
                case "IsTopXSkill": return p.IsTopXSkill;
                case "PassionLevel": return p.PassionLevel;
                case "SkillLevelGreaterThan": return p.SkillLevelGreaterThan;
                case "SkillLevelLessThan": return p.SkillLevelLessThan;
                default: return 0;
            }
        }

        private static void SetIntValue(string key, WorkAssignmentParameters p, int value)
        {
            switch (key)
            {
                case "IsTopXSkill": p.IsTopXSkill = value; break;
                case "PassionLevel": p.PassionLevel = value; break;
                case "SkillLevelGreaterThan": p.SkillLevelGreaterThan = value; break;
                case "SkillLevelLessThan": p.SkillLevelLessThan = value; break;
            }
        }
    }
}
