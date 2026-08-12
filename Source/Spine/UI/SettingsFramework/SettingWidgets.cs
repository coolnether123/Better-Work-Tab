using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Spine.UI.ColourPicker;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Stateless widget renderers for individual setting types.
    /// </summary>
    public static class SettingWidgets
    {
        private static readonly Color SettingLabelColor = new Color(0.78f, 0.77f, 0.74f);
        private static readonly Color DefaultSectionAccent = new Color(0.9f, 0.85f, 0.7f);

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
            const float checkboxSize = 24f;
            Rect checkboxRect = new Rect(
                rect.xMax - checkboxSize,
                rect.y + ((rect.height - checkboxSize) / 2f),
                checkboxSize,
                checkboxSize);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, checkboxRect.x - rect.x - 6f),
                rect.height);
            DrawSettingLabel(labelRect, label, disabled);
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref value, checkboxSize, disabled);
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
        /// Draws a checkbox with header styling while retaining normal toggle behavior.
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
            const float checkboxSize = 24f;
            Rect checkboxRect = new Rect(
                rect.xMax - checkboxSize,
                rect.y + ((rect.height - checkboxSize) / 2f),
                checkboxSize,
                checkboxSize);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, checkboxRect.x - rect.x - 8f),
                rect.height);

            DrawSectionHeader(rect, labelRect, label, headerColor);
            Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref value, checkboxSize, disabled);
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

        /// <summary>Draws a non-interactive label and value pair.</summary>
        public static void DrawReadOnly(
            Rect rect,
            string label,
            string value,
            string tooltip = null,
            bool disabled = false)
        {
            Rect valueRect = rect.RightPart(0.48f);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, valueRect.x - rect.x - 6f),
                rect.height);
            DrawSettingLabel(labelRect, label, disabled);

            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            GUI.color = disabled ? Color.gray : SettingLabelColor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, value ?? string.Empty);
            Text.Anchor = previousAnchor;
            GUI.color = previousColor;

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
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
            var colorRect = new Rect(rect.xMax - 96f, rect.y + 2f, 28f, rect.height - 4f);
            var buttonRect = new Rect(colorRect.xMax + 4f, rect.y + 2f, 60f, rect.height - 4f);
            var labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, colorRect.x - rect.x - 6f),
                rect.height);

            DrawSettingLabel(labelRect, label, disabled);
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
        /// Draws a labelled slider with a numeric readout. Returns true only on
        /// the frames where the value actually moved, so a caller can persist
        /// on change without writing every frame of a drag.
        /// </summary>
        public static bool DrawSlider(
            Rect rect,
            string label,
            ref float value,
            float min,
            float max,
            string valueLabel = null,
            string tooltip = null,
            bool disabled = false,
            float step = 0f)
        {
            const float ReadoutWidth = 54f;
            const float SliderHeight = 18f;

            Rect rightRect = rect.RightPart(0.48f);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, rightRect.x - rect.x - 6f),
                rect.height);
            Rect readoutRect = new Rect(
                rightRect.xMax - ReadoutWidth,
                rect.y,
                ReadoutWidth,
                rect.height);
            Rect sliderRect = new Rect(
                rightRect.x,
                rect.y + (rect.height - SliderHeight) * 0.5f,
                Mathf.Max(24f, rightRect.width - ReadoutWidth - 6f),
                SliderHeight);

            DrawSettingLabel(labelRect, label, disabled);

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(
                readoutRect,
                valueLabel ?? value.ToString("0.00"));
            Text.Anchor = previousAnchor;

            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            float updated = Widgets.HorizontalSlider(sliderRect, value, min, max);

            if (disabled)
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            if (disabled)
            {
                return false;
            }

            if (step > 0f)
            {
                updated = Mathf.Round(updated / step) * step;
            }

            updated = Mathf.Clamp(updated, min, max);
            if (Mathf.Abs(updated - value) < 0.0001f)
            {
                return false;
            }

            value = updated;
            return true;
        }

        /// <summary>Draws a bounded float control with optional endpoint labels.</summary>
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
            Rect sliderRect = rect.RightPart(0.48f);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, sliderRect.x - rect.x - 6f),
                rect.height);

            string valueText;
            try
            {
                valueText = string.IsNullOrEmpty(valueFormat)
                    ? value.ToString("F1")
                    : string.Format(valueFormat, value);
            }
            catch (FormatException)
            {
                valueText = value.ToString("F1");
            }

            DrawSettingLabel(labelRect, label + ": " + valueText, disabled);

            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            value = Widgets.HorizontalSlider(sliderRect, value, min, max);

            if (disabled)
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
            }

            if (!string.IsNullOrEmpty(minLabel) || !string.IsNullOrEmpty(maxLabel))
            {
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.UpperLeft;
                if (!string.IsNullOrEmpty(minLabel))
                {
                    Widgets.Label(
                        new Rect(sliderRect.x, sliderRect.yMax - 10f, sliderRect.width / 2f, 10f),
                        minLabel);
                }

                if (!string.IsNullOrEmpty(maxLabel))
                {
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(
                        new Rect(
                            sliderRect.x + sliderRect.width / 2f,
                            sliderRect.yMax - 10f,
                            sliderRect.width / 2f,
                            10f),
                        maxLabel);
                }

                Text.Anchor = oldAnchor;
            }
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return !Mathf.Approximately(original, value);
        }

        /// <summary>Draws a bounded integer slider.</summary>
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
            Rect sliderRect = rect.RightPart(0.48f);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, sliderRect.x - rect.x - 6f),
                rect.height);
            DrawSettingLabel(labelRect, label + ": " + value, disabled);

            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            float updated = Widgets.HorizontalSlider(sliderRect, value, min, max, true);
            value = Mathf.Clamp(Mathf.RoundToInt(updated), min, max);

            if (disabled)
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return original != value;
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
            Action<object> onSelected = null,
            Func<object, string> labelProvider = null,
            Func<object, string> descriptionProvider = null)
        {
            var buttonRect = rect.RightPart(0.48f);
            var labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, buttonRect.x - rect.x - 6f),
                rect.height);

            DrawSettingLabel(labelRect, label, disabled);

            bool prevEnabled = GUI.enabled;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            string currentLabel = ResolveEnumLabel(
                enumType,
                currentValue,
                labelProvider);

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
                    string optionLabel = ResolveEnumLabel(
                        enumType,
                        local,
                        labelProvider);
                    var option = new FloatMenuOption(optionLabel, () => onSelected?.Invoke(local));
                    options.Add(option);
                    optionDescriptions[option] = ResolveEnumDescription(
                        local,
                        tooltip,
                        descriptionProvider);
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

        private static string ResolveEnumLabel(
            Type enumType,
            object value,
            Func<object, string> labelProvider)
        {
            if (enumType == null || value == null)
            {
                return string.Empty;
            }

            string suppliedLabel = labelProvider?.Invoke(value);
            if (!string.IsNullOrWhiteSpace(suppliedLabel))
            {
                return suppliedLabel;
            }

            return value.ToString();
        }

        private static string ResolveEnumDescription(
            object value,
            string settingDescription,
            Func<object, string> descriptionProvider)
        {
            if (value == null)
            {
                return settingDescription ?? string.Empty;
            }

            string suppliedDescription = descriptionProvider?.Invoke(value);
            if (!string.IsNullOrWhiteSpace(suppliedDescription))
            {
                return suppliedDescription;
            }

            return settingDescription ?? string.Empty;
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

        /// <summary>Draws an empty row used as visual separation.</summary>
        public static void DrawSpacer(Rect rect)
        {
            // Intentionally empty: the row height provides the separation.
        }

        /// <summary>
        /// Draws a button that opens a menu of dynamically supplied options.
        /// </summary>
        public static void DrawDropdownListAdder(
            Rect rect,
            string label,
            Func<IEnumerable<string>> optionsProvider,
            Action<string> onAdded,
            string tooltip = null,
            bool disabled = false)
        {
            Rect buttonRect = rect.RightPart(0.38f);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, buttonRect.x - rect.x - 6f),
                rect.height);
            DrawSettingLabel(labelRect, label, disabled);

            if (!disabled && Widgets.ButtonText(buttonRect, "Add"))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                Dictionary<FloatMenuOption, string> descriptions =
                    new Dictionary<FloatMenuOption, string>();
                IEnumerable<string> available = optionsProvider?.Invoke();
                if (available != null)
                {
                    foreach (string optionText in available)
                    {
                        string localOption = optionText;
                        FloatMenuOption option = new FloatMenuOption(
                            localOption,
                            () => onAdded?.Invoke(localOption));
                        options.Add(option);
                        descriptions[option] = tooltip ?? string.Empty;
                    }
                }

                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("No options available", null));
                }

                Find.WindowStack.Add(new DescribedFloatMenu(
                    options,
                    null,
                    label,
                    tooltip,
                    descriptions));
            }

            if (!string.IsNullOrEmpty(tooltip) && !DescribedFloatMenu.AnyOpen)
            {
                TooltipHandler.TipRegion(labelRect, tooltip);
            }
        }

        /// <summary>Draws an integer input with decrement/increment buttons.</summary>
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
            Rect controlRect = rect.RightPart(0.48f);
            Rect labelRect = new Rect(
                rect.x,
                rect.y,
                Mathf.Max(0f, controlRect.x - rect.x - 6f),
                rect.height);
            DrawSettingLabel(labelRect, label, disabled);

            bool previousEnabled = GUI.enabled;
            Color previousColor = GUI.color;
            if (disabled)
            {
                GUI.enabled = false;
                GUI.color = Color.gray;
            }

            const float buttonSize = 22f;
            const float spacing = 2f;
            const float textWidth = 50f;
            Rect minusRect = new Rect(
                controlRect.x,
                controlRect.y + (controlRect.height - buttonSize) / 2f,
                buttonSize,
                buttonSize);
            Rect plusRect = new Rect(
                minusRect.xMax + spacing,
                minusRect.y,
                buttonSize,
                buttonSize);
            Rect textRect = new Rect(
                plusRect.xMax + spacing,
                minusRect.y,
                textWidth,
                buttonSize);

            if (Widgets.ButtonText(minusRect, "-"))
            {
                value = value == int.MinValue ? int.MinValue : value - 1;
                value = Mathf.Max(value, min);
            }

            if (Widgets.ButtonText(plusRect, "+"))
            {
                value = value == int.MaxValue ? int.MaxValue : value + 1;
                value = Mathf.Min(value, max);
            }

            string buffer = value.ToString();
            Widgets.TextFieldNumeric(textRect, ref value, ref buffer, min, max);

            if (disabled)
            {
                GUI.enabled = previousEnabled;
                GUI.color = previousColor;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return original != value;
        }

        /// <summary>
        /// Draws a styled section header.
        /// </summary>
        private static void DrawSectionHeader(
            Rect rect,
            Rect labelBounds,
            string label,
            Color? color)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Color accent = ResolveSectionAccent(color);
            GUI.color = accent;
            Rect labelRect = new Rect(
                labelBounds.x + 10f,
                rect.y,
                Mathf.Max(0f, labelBounds.width - 10f),
                rect.height);
            Widgets.Label(labelRect, label);
            Widgets.DrawBoxSolid(
                new Rect(labelRect.x, rect.yMax - 4f, labelRect.width, 2f),
                accent);
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        public static void DrawHeader(Rect rect, string label, Color? color = null)
        {
            DrawSectionHeader(rect, rect, label, color);
        }

        internal static void DrawSubheader(Rect rect, string label, Color? color = null)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Color accent = ResolveSectionAccent(color);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.Lerp(SettingLabelColor, accent, 0.72f);
            Rect labelRect = new Rect(rect.x + 8f, rect.y, Mathf.Max(0f, rect.width - 8f), rect.height);
            Widgets.Label(labelRect, label);
            Widgets.DrawBoxSolid(
                new Rect(labelRect.x, rect.yMax - 3f, labelRect.width, 1f),
                new Color(accent.r, accent.g, accent.b, 0.58f));

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        internal static Color ResolveSectionAccent(Color? color = null)
        {
            return color ?? DefaultSectionAccent;
        }

        internal static void DrawSectionPanel(Rect rect, float headerHeight, Color? color = null)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Color accent = ResolveSectionAccent(color);
            Color panelColor = new Color(0.075f, 0.075f, 0.075f, 0.86f);
            Color headerColor = Color.Lerp(
                new Color(0.11f, 0.11f, 0.11f, 0.72f),
                new Color(accent.r, accent.g, accent.b, 0.72f),
                0.12f);
            Color edgeColor = new Color(0.34f, 0.34f, 0.34f, 0.68f);
            Color innerEdgeColor = new Color(accent.r, accent.g, accent.b, 0.34f);
            Widgets.DrawBoxSolid(rect, panelColor);
            Widgets.DrawBoxSolid(new Rect(
                rect.x + 2f,
                rect.y + 2f,
                Mathf.Max(0f, rect.width - 4f),
                Mathf.Min(Mathf.Max(0f, headerHeight - 1f), Mathf.Max(0f, rect.height - 4f))), headerColor);

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), edgeColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), edgeColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), edgeColor);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), edgeColor);
            Widgets.DrawBoxSolid(new Rect(rect.x + 1f, rect.y + headerHeight, Mathf.Max(0f, rect.width - 2f), 1f), innerEdgeColor);

        }

        private static void DrawSettingLabel(Rect rect, string label, bool disabled)
        {
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (!disabled)
            {
                GUI.color = SettingLabelColor;
            }

            Widgets.Label(rect, label);
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }


    }
}
