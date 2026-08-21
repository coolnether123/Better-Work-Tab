using Better_Work_Tab.UI;
using UnityEngine;

namespace Better_Work_Tab.UI.Chrome
{
    /// <summary>
    /// The hint's draw rectangle and its two established interaction areas are
    /// intentionally distinct; callers select the semantic area they own.
    /// </summary>
    internal enum WorkTabContextSettingsHintArea
    {
        Draw,
        SettingsFocus,
        TutorialInteraction
    }

    /// <summary>
    /// Shared geometry for the small pieces of Work-tab chrome that are drawn
    /// and hit-tested by more than one owner.
    /// </summary>
    internal static class WorkTabChromeGeometry
    {
        internal const float RightEdgeMargin = 10f;
        private const float InfoIconSize = 24f;
        private const float ContextSettingsHintWidth = 230f;

        internal static Rect GetManualPrioritiesCheckboxRect()
        {
            return new Rect(5f, 5f, 140f, 30f);
        }

        internal static Rect GetManualPrioritiesContextRect()
        {
            return new Rect(5f, 5f, 220f, 90f);
        }

        internal static Rect GetInfoIconRect(Rect inRect)
        {
            return new Rect(
                inRect.xMax - InfoIconSize - RightEdgeMargin,
                inRect.yMax - InfoIconSize - 10f,
                InfoIconSize,
                InfoIconSize);
        }

        internal static Rect GetContextSettingsHintRect(Rect inRect)
        {
            return GetContextSettingsHintRect(inRect, WorkTabContextSettingsHintArea.Draw);
        }

        internal static Rect GetContextSettingsHintRect(
            Rect inRect,
            WorkTabContextSettingsHintArea area)
        {
            switch (area)
            {
                case WorkTabContextSettingsHintArea.SettingsFocus:
                    return new Rect(
                        inRect.xMax - ContextSettingsHintWidth - 42f,
                        inRect.y + 5f,
                        ContextSettingsHintWidth,
                        24f);
                case WorkTabContextSettingsHintArea.TutorialInteraction:
                    return new Rect(inRect.xMax - 300f, inRect.y + 2f, 260f, 28f);
                default:
                    float topRightReservedWidth = HeaderButtons.GetTopRightReservedWidth();
                    return new Rect(
                        inRect.xMax - ContextSettingsHintWidth - 42f - topRightReservedWidth,
                        inRect.y + 5f,
                        ContextSettingsHintWidth,
                        24f);
            }
        }
    }
}
