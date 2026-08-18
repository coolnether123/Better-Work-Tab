using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Settings
{
    /// <summary>
    /// BWT-owned settings widgets that are not part of the shared Spine API.
    /// </summary>
    internal static class BWTSettingWidgets
    {
        internal static bool DrawReadOnlyValue(
            Rect rect,
            string label,
            string value,
            string tooltip,
            bool disabled)
        {
            Color previousColor = GUI.color;
            TextAnchor previousAnchor = Text.Anchor;
            if (disabled)
            {
                GUI.color = Color.gray;
            }

            Rect labelRect = rect.LeftPart(0.48f);
            Rect valueRect = rect.RightPart(0.5f);
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(valueRect, value ?? string.Empty);
            Text.Anchor = previousAnchor;
            GUI.color = previousColor;
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }

            return false;
        }
    }
}
