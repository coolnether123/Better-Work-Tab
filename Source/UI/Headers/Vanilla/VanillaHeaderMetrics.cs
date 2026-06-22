using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Headers.Vanilla
{
    internal static class VanillaHeaderMetrics
    {
        public const float Level0Offset = 19f;
        public const float LevelStepHeight = 20f;
        public const float StemLineGap = 2f;

        public static float GetYOffset(int level)
        {
            return Level0Offset + Mathf.Max(0, level) * LevelStepHeight;
        }

        public static int GetRequiredHeaderHeight(int maxLevel)
        {
            GameFont oldFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                float rowHeight = Text.LineHeight + StemLineGap;
                return Mathf.CeilToInt(rowHeight * (Mathf.Max(0, maxLevel) + 1.2f));
            }
            finally
            {
                Text.Font = oldFont;
            }
        }
    }
}
