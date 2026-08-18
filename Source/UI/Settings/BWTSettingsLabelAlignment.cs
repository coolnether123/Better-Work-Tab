using System.Reflection;
using HarmonyLib;
using Spine.UI.SettingsFramework;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// One geometry rule for settings headings: hierarchy indentation is owned
    /// by SettingsListDrawer's content rect; a widget must not add another
    /// hidden left inset. Normal setting widgets already follow this rule.
    /// </summary>
    internal static class BWTSettingsLabelGeometry
    {
        internal static Rect GetAlignedLabelRect(Rect labelBounds, Rect rowRect)
        {
            return new Rect(
                labelBounds.x,
                rowRect.y,
                Mathf.Max(0f, labelBounds.width),
                rowRect.height);
        }
    }

    [HarmonyPatch]
    internal static class Patch_SettingsSectionHeader_Aligned
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(SettingWidgets),
                "DrawSectionHeader",
                new[] { typeof(Rect), typeof(Rect), typeof(string), typeof(Color?) });
        }

        [HarmonyPrefix]
        private static bool Prefix(
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
            Color accent = color ?? new Color(0.9f, 0.85f, 0.7f);
            GUI.color = accent;
            Rect labelRect = BWTSettingsLabelGeometry.GetAlignedLabelRect(labelBounds, rect);
            Widgets.Label(labelRect, label ?? string.Empty);
            Widgets.DrawBoxSolid(
                new Rect(labelRect.x, rect.yMax - 4f, labelRect.width, 2f),
                accent);
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class Patch_SettingsSubheader_Aligned
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(SettingWidgets),
                "DrawSubheader",
                new[] { typeof(Rect), typeof(string), typeof(Color?) });
        }

        [HarmonyPrefix]
        private static bool Prefix(Rect rect, string label, Color? color)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            Color accent = color ?? new Color(0.9f, 0.85f, 0.7f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.Lerp(
                new Color(0.78f, 0.77f, 0.74f),
                accent,
                0.72f);
            Rect labelRect = BWTSettingsLabelGeometry.GetAlignedLabelRect(rect, rect);
            Widgets.Label(labelRect, label ?? string.Empty);
            Widgets.DrawBoxSolid(
                new Rect(labelRect.x, rect.yMax - 3f, labelRect.width, 1f),
                new Color(accent.r, accent.g, accent.b, 0.58f));
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
            return false;
        }
    }
}
