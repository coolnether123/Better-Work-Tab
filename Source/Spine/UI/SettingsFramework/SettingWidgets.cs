using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Spine.UI.ColourPicker;

namespace Better_Work_Tab.UI.SettingsFramework
{
    /// <summary>
    /// Stateless widget renderers for individual setting types.
    /// </summary>
    public static class SettingWidgets
    {
        /// <summary>
        /// Draws a checkbox setting with optional tooltip and disabled state.
        /// </summary>
        public static bool DrawBool(
            Rect rect,
            string label,
            ref bool value,
            string tooltip = null,
            bool disabled = false)
        {
            bool original = value;
            Widgets.CheckboxLabeled(rect, label, ref value, disabled);

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return original != value;
        }

        /// <summary>
        /// Draws a checkbox with header styling (bold label with underline) while remaining clickable.
        /// </summary>
        public static bool DrawHeaderBool(
            Rect rect,
            string label,
            ref bool value,
            Color? headerColor = null,
            string tooltip = null,
            bool disabled = false)
        {
            bool original = value;

            // Header-styled label on the left, checkbox on the right
            var labelRect = rect.LeftPart(0.7f);
            var toggleRect = rect.RightPart(0.25f);

            var oldFont = Text.Font;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            Color resolved = headerColor ?? new Color(0.9f, 0.85f, 0.7f);
            GUI.color = resolved;
            Widgets.Label(labelRect, label);
            Rect lineRect = new Rect(labelRect.x, labelRect.yMax - 4f, labelRect.width, 2f);
            Widgets.DrawBoxSolid(lineRect, resolved);

            Text.Font = oldFont;
            GUI.color = oldColor;

            Widgets.CheckboxLabeled(toggleRect, string.Empty, ref value, disabled);

            // Allow clicking the header label area to toggle as well (when not disabled)
            if (!disabled && Widgets.ButtonInvisible(labelRect))
            {
                value = !value;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return original != value;
        }

        /// <summary>
        /// Draws a horizontal slider for float values with labels.
        /// </summary>
        public static bool DrawFloat(
            Rect rect,
            string label,
            ref float value,
            float min,
            float max,
            string minLabel = null,
            string maxLabel = null,
            string valueFormat = null,
            string tooltip = null,
            bool disabled = false)
        {
            float original = value;

            var labelRect = rect.LeftPart(0.5f);
            var sliderRect = rect.RightPart(0.48f);

            string valueText = string.IsNullOrEmpty(valueFormat)
                ? value.ToString("F1")
                : string.Format(valueFormat, value);
            Widgets.Label(labelRect, $"{label}: {valueText}");

            bool prevEnabled = GUI.enabled;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            value = Widgets.HorizontalSlider(
                sliderRect,
                value,
                min,
                max,
                middleAlignment: true,
                leftAlignedLabel: minLabel,
                rightAlignedLabel: maxLabel);

            if (disabled)
            {
                GUI.enabled = prevEnabled;
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return !Mathf.Approximately(original, value);
        }

        /// <summary>
        /// Draws an integer slider with optional tooltip.
        /// </summary>
        public static bool DrawInt(
            Rect rect,
            string label,
            ref int value,
            int min,
            int max,
            string tooltip = null,
            bool disabled = false)
        {
            int original = value;

            var labelRect = rect.LeftPart(0.5f);
            var sliderRect = rect.RightPart(0.48f);

            Widgets.Label(labelRect, $"{label}: {value}");

            bool prevEnabled = GUI.enabled;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            float sliderValue = Widgets.HorizontalSlider(sliderRect, value, min, max, true);
            int rounded = Mathf.RoundToInt(sliderValue);
            if (rounded < min) rounded = min;
            if (rounded > max) rounded = max;
            value = rounded;

            if (disabled)
            {
                GUI.enabled = prevEnabled;
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return original != value;
        }

        /// <summary>
        /// Draws a color swatch with an edit button that opens a picker.
        /// </summary>
        public static bool DrawColor(
            Rect rect,
            string label,
            ref Color value,
            string tooltip = null,
            bool disabled = false,
            Action<Color, Action<Color>> openColorPicker = null,
            string editLabel = "Edit")
        {
            var labelRect = rect.LeftPart(0.6f);
            var colorRect = new Rect(rect.xMax - 96f, rect.y + 2f, 28f, rect.height - 4f);
            var buttonRect = new Rect(colorRect.xMax + 4f, rect.y + 2f, 60f, rect.height - 4f);

            Widgets.Label(labelRect, label);
            Widgets.DrawBoxSolid(colorRect, value);
            Widgets.DrawBox(colorRect, 1);

            if (!disabled && Widgets.ButtonText(buttonRect, editLabel))
            {
                if (openColorPicker != null)
                {
                    openColorPicker(value, null);
                }
                else
                {
                    Find.WindowStack.Add(new Dialog_ColourPicker(value));
                }
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return false;
        }

        /// <summary>
        /// Draws an enum dropdown button.
        /// </summary>
        public static void DrawEnum(
            Rect rect,
            string label,
            object currentValue,
            Type enumType,
            string tooltip = null,
            bool disabled = false,
            Action<object> onSelected = null)
        {
            var labelRect = rect.LeftPart(0.5f);
            var buttonRect = rect.RightPart(0.48f);

            Widgets.Label(labelRect, label);

            bool prevEnabled = GUI.enabled;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            string currentLabel = ResolveEnumLabel(enumType, currentValue);

            if (Widgets.ButtonText(buttonRect, currentLabel) && enumType != null)
            {
                var options = new List<FloatMenuOption>();
                var optionDescriptions = new Dictionary<FloatMenuOption, string>();
                var seenValues = new HashSet<long>();
                FloatMenuOption selectedOption = null;
                long currentNumericValue = Convert.ToInt64(currentValue);
                foreach (var enumValue in Enum.GetValues(enumType))
                {
                    // Enum aliases are useful for serialized-setting migrations, but should not
                    // create duplicate choices in the player-facing dropdown.
                    if (!seenValues.Add(Convert.ToInt64(enumValue)))
                    {
                        continue;
                    }

                    var local = enumValue;
                    string optionLabel = ResolveEnumLabel(enumType, local);
                    var option = new FloatMenuOption(optionLabel, () => onSelected?.Invoke(local));
                    options.Add(option);
                    optionDescriptions[option] = ResolveEnumDescription(enumType, local, label, tooltip);
                    if (Convert.ToInt64(local) == currentNumericValue)
                    {
                        selectedOption = option;
                    }
                }

                Find.WindowStack.Add(new DescribedFloatMenu(options, selectedOption, label, tooltip, optionDescriptions));
            }

            if (disabled)
            {
                GUI.enabled = prevEnabled;
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(tooltip) && !DescribedFloatMenu.AnyOpen)
            {
                // Keep the value button free of tooltip ownership. Otherwise a tooltip that was
                // opened over the button can remain above the enum menu and obscure its choices.
                TooltipHandler.TipRegion(labelRect, tooltip);
            }
        }

        private static string ResolveEnumLabel(Type enumType, object value)
        {
            if (enumType == null || value == null)
            {
                return string.Empty;
            }

            string key = $"BWT_Enum_{enumType.Name}_{value}";
            if (key.CanTranslate())
            {
                return key.Translate();
            }

            // Localized labels for Better Work Tab hover modes without renaming the enum values.
            if (enumType.Name == "SkillViewHoverMode")
            {
                switch (value.ToString())
                {
                    case "Standard":
                        return "Priority";
                    case "SkillFocused":
                        return "Skill";
                }
            }

            return value.ToString();
        }

        private static string ResolveEnumDescription(
            Type enumType,
            object value,
            string settingLabel,
            string settingDescription)
        {
            if (enumType == null || value == null)
            {
                return settingDescription ?? string.Empty;
            }

            string key = $"BWT_Enum_{enumType.Name}_{value}_Description";
            if (key.CanTranslate())
            {
                return key.Translate();
            }

            string optionLabel = ResolveEnumLabel(enumType, value);
            string action = $"Selecting {optionLabel} sets {settingLabel} to {optionLabel}.";
            return string.IsNullOrEmpty(settingDescription)
                ? action
                : action + "\n\n" + settingDescription;
        }

        /// <summary>
        /// Draws a clickable button.
        /// </summary>
        public static bool DrawButton(
            Rect rect,
            string label,
            string tooltip = null,
            bool disabled = false)
        {
            bool prevEnabled = GUI.enabled;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            bool clicked = Widgets.ButtonText(rect, label);

            if (disabled)
            {
                GUI.enabled = prevEnabled;
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return clicked;
        }

        /// <summary>
        /// Draws a styled section header.
        /// </summary>
        public static void DrawHeader(Rect rect, string label, Color? color = null)
        {
            var oldFont = Text.Font;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            Color resolved = color ?? new Color(0.9f, 0.85f, 0.7f);
            GUI.color = resolved;
            Widgets.Label(rect, label);

            // Underline for visual separation
            Rect lineRect = new Rect(rect.x, rect.yMax - 4f, rect.width, 2f);
            Widgets.DrawBoxSolid(lineRect, resolved);

            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        /// <summary>
        /// Draws empty vertical space.
        /// </summary>
        public static void DrawSpacer(Rect rect)
        {
            // Intentionally left blank
        }

        /// <summary>
        /// Draws a button that opens a dropdown to add items to a list.
        /// </summary>
        public static void DrawDropdownListAdder(
            Rect rect,
            string label,
            Func<IEnumerable<string>> optionsProvider,
            Action<string> onAdded,
            string tooltip = null,
            bool disabled = false)
        {
            var labelRect = rect.LeftPart(0.6f);
            var buttonRect = rect.RightPart(0.38f);

            Widgets.Label(labelRect, label);

            if (!disabled && Widgets.ButtonText(buttonRect, "BWT_AddOption".Translate()))
            {
                var options = new List<FloatMenuOption>();
                var optionDescriptions = new Dictionary<FloatMenuOption, string>();
                var available = optionsProvider?.Invoke();
                if (available != null)
                {
                    foreach (var opt in available)
                    {
                        var local = opt;
                        var option = new FloatMenuOption(local, () => onAdded?.Invoke(local));
                        options.Add(option);
                        optionDescriptions[option] = $"Select {local} to add it to {label}." +
                            (string.IsNullOrEmpty(tooltip) ? string.Empty : "\n\n" + tooltip);
                    }
                }

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("BWT_NoOptionsAvailable".Translate(), null));
                }

                Find.WindowStack.Add(new DescribedFloatMenu(options, null, label, tooltip, optionDescriptions));
            }

            if (!string.IsNullOrEmpty(tooltip) && !DescribedFloatMenu.AnyOpen)
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
            }
        }

        /// <summary>
        /// Draws an integer input with +/- buttons and a numeric text field.
        /// </summary>
        public static bool DrawNumericInt(
            Rect rect,
            string label,
            ref int value,
            int min,
            int max,
            string tooltip = null,
            bool disabled = false)
        {
            int original = value;
            var labelRect = rect.LeftPart(0.5f);
            var controlRect = rect.RightPart(0.48f);

            Widgets.Label(labelRect, label);

            bool prevEnabled = GUI.enabled;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            float buttonWidth = 22f;
            float spacing = 2f;
            float textWidth = 50f;

            Rect btnMinusRect = new Rect(controlRect.x, controlRect.y + (controlRect.height - buttonWidth) / 2f, buttonWidth, buttonWidth);
            Rect btnPlusRect = new Rect(btnMinusRect.xMax + spacing, btnMinusRect.y, buttonWidth, buttonWidth);
            Rect textRect = new Rect(btnPlusRect.xMax + spacing, controlRect.y + (controlRect.height - buttonWidth) / 2f, textWidth, buttonWidth);

            if (Widgets.ButtonText(btnMinusRect, "-"))
            {
                value--;
                if (value < min) value = min;
            }
            if (Widgets.ButtonText(btnPlusRect, "+"))
            {
                value++;
                if (value > max) value = max;
            }

            string buffer = value.ToString();
            Widgets.TextFieldNumeric(textRect, ref value, ref buffer, min, max);

            if (disabled)
            {
                GUI.enabled = prevEnabled;
                GUI.color = Color.white;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return original != value;
        }
    }
}
