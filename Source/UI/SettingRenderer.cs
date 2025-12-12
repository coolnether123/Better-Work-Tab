using Better_Work_Tab;
using HarmonyLib;
using RimWorld;
using Spine.UI.ColourPicker;
using Spine.UI.SettingsFramework;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Shared renderer for drawing settings definitions in both views.
    /// </summary>
    public static class SettingRenderer
    {
        public static void Draw(Rect rect, SettingDefinition def, BetterWorkTabSettings settings, bool showFavoriteStar = true, bool drawHoverOutline = false)
        {
            if (def == null || settings == null)
            {
                return;
            }

            if (drawHoverOutline && Mouse.IsOver(rect))
            {
                DrawRowOutline(rect);
            }

            bool hiddenByCondition = def.VisibleWhen != null && !def.VisibleWhen(settings);
            if (hiddenByCondition)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                GUI.enabled = false;
            }

            Rect contentRect = rect;
            if (showFavoriteStar && def.IsFavoritable)
            {
                Rect starRect = new Rect(rect.x, rect.y + (rect.height - 18f) / 2f, 18f, 18f);
                contentRect.xMin += 22f;

                if (DrawFavoriteStar(starRect, FavoritesManager.Instance.IsFavorite(def.Id)))
                {
                    FavoritesManager.Instance.ToggleFavorite(def.Id);
                    FavoritesManager.Instance.SaveIfDirty(GenFilePaths.ConfigFolderPath);
                }
            }

            string label = SettingsTranslation.GetSettingLabel(def);
            string tooltip = SettingsTranslation.GetSettingTooltip(def);

            switch (def.Type)
            {
                case SettingType.Bool:
                    DrawBool(contentRect, def, settings, label);
                    break;
                case SettingType.Float:
                    DrawSlider(contentRect, def, settings, label);
                    break;
                case SettingType.Color:
                    DrawColor(contentRect, def, settings, label);
                    break;
                case SettingType.Enum:
                    DrawEnum(contentRect, def, settings, label);
                    break;
                case SettingType.Button:
                    DrawButton(contentRect, def, settings, label);
                    break;
                case SettingType.Header:
                    DrawHeader(contentRect, label);
                    break;
            }

            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            GUI.enabled = true;
            GUI.color = Color.white;
        }

        private static void DrawBool(Rect rect, SettingDefinition def, BetterWorkTabSettings settings, string label)
        {
            var field = AccessTools.Field(typeof(BetterWorkTabSettings), def.FieldName);
            if (field == null)
            {
                return;
            }

            bool value = (bool)field.GetValue(settings);
            bool old = value;

            Widgets.CheckboxLabeled(rect, label, ref value);

            if (old != value)
            {
                field.SetValue(settings, value);
                def.OnChanged?.Invoke(settings);
            }
        }

        private static void DrawSlider(Rect rect, SettingDefinition def, BetterWorkTabSettings settings, string label)
        {
            var field = AccessTools.Field(typeof(BetterWorkTabSettings), def.FieldName);
            if (field == null)
            {
                return;
            }

            float value = (float)field.GetValue(settings);
            float original = value;

            Rect labelRect = rect.LeftPart(0.5f);
            Rect sliderRect = rect.RightPart(0.48f);

            Widgets.Label(labelRect, $"{label}: {value:F1}");

            value = Widgets.HorizontalSlider(
                sliderRect,
                value,
                def.MinValue ?? 0f,
                def.MaxValue ?? 1f,
                middleAlignment: true,
                leftAlignedLabel: def.MinLabel,
                rightAlignedLabel: def.MaxLabel);

            if (!Mathf.Approximately(original, value))
            {
                field.SetValue(settings, value);
                def.OnChanged?.Invoke(settings);
            }
        }

        private static void DrawColor(Rect rect, SettingDefinition def, BetterWorkTabSettings settings, string label)
        {
            var field = AccessTools.Field(typeof(BetterWorkTabSettings), def.FieldName);
            if (field == null)
            {
                return;
            }

            Color value = (Color)field.GetValue(settings);
            Rect labelRect = rect.LeftPart(0.6f);
            Rect colorRect = new Rect(rect.xMax - 90f, rect.y + 2f, 28f, rect.height - 4f);
            Rect buttonRect = new Rect(colorRect.xMax + 4f, rect.y + 2f, 56f, rect.height - 4f);

            Widgets.Label(labelRect, label);
            Widgets.DrawBoxSolid(colorRect, value);
            Widgets.DrawBox(colorRect, 1);

            if (Widgets.ButtonText(buttonRect, SettingsTranslation.Edit))
            {
                Find.WindowStack.Add(new Dialog_ColourPicker(value, (newColor, _) =>
                {
                    field.SetValue(settings, newColor);
                    def.OnChanged?.Invoke(settings);
                }));
            }
        }

        private static void DrawEnum(Rect rect, SettingDefinition def, BetterWorkTabSettings settings, string label)
        {
            var field = AccessTools.Field(typeof(BetterWorkTabSettings), def.FieldName);
            if (field == null || def.EnumType == null)
            {
                return;
            }

            object value = field.GetValue(settings);
            Rect labelRect = rect.LeftPart(0.5f);
            Rect buttonRect = rect.RightPart(0.48f);

            Widgets.Label(labelRect, label);

            if (Widgets.ButtonText(buttonRect, value.ToString()))
            {
                var options = new List<FloatMenuOption>();
                foreach (var enumValue in Enum.GetValues(def.EnumType))
                {
                    var local = enumValue;
                    options.Add(new FloatMenuOption(local.ToString(), () =>
                    {
                        if (!Equals(value, local))
                        {
                            field.SetValue(settings, local);
                            def.OnChanged?.Invoke(settings);
                        }
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static void DrawButton(Rect rect, SettingDefinition def, BetterWorkTabSettings settings, string label)
        {
            if (Widgets.ButtonText(rect, label))
            {
                def.OnChanged?.Invoke(settings);
            }
        }

        private static void DrawHeader(Rect rect, string label)
        {
            var old = Text.Font;
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.9f, 0.85f, 0.7f);
            Widgets.Label(rect, label);
            Text.Font = old;
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draws a subtle outline around a row for hover feedback.
        /// </summary>
        private static void DrawRowOutline(Rect rect)
        {
            Color outlineColor = new Color(1f, 1f, 1f, 0.3f);
            float thickness = 1f;

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, thickness), outlineColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), outlineColor);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, thickness, rect.height), outlineColor);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), outlineColor);
        }

        private static bool DrawFavoriteStar(Rect rect, bool isFavorite)
        {
            Color starColor = isFavorite
                ? new Color(1f, 0.85f, 0.2f)
                : new Color(0.5f, 0.5f, 0.5f, 0.5f);

            var oldColor = GUI.color;
            GUI.color = starColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, isFavorite ? "★" : "☆");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = oldColor;

            return Widgets.ButtonInvisible(rect);
        }
    }
}
