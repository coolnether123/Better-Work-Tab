using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public static class BetterWorkTabSettingsUI
    {
        private const float LabelWidth = 90f;
        private const float LabelSliderGap = 2f;
        private const float RowHeight = 24f;
        private const float SliderHeight = 20f;
        private const float ButtonWidth = 24f;
        private const float IntAdjustLabelWidth = 64f;

        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings s)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            var l = new Listing_Standard { ColumnWidth = inRect.width / 2f - 12f };
            l.Begin(inRect);
            DrawRulesUI(l, s);
            l.End();
        }

        private static void DrawRulesUI(Listing_Standard listing, BetterWorkTabSettings s)
        {

        }

        private static void IntAdjust(ref int val, int min, int max, Rect row)
        {
            var minus = new Rect(row.x, row.y, ButtonWidth, ButtonWidth);
            var label = new Rect(row.x + ButtonWidth + LabelSliderGap, row.y, IntAdjustLabelWidth, row.height);
            var plus = new Rect(row.x + ButtonWidth + LabelSliderGap + IntAdjustLabelWidth + LabelSliderGap, row.y, ButtonWidth, ButtonWidth);

            if (Widgets.ButtonText(minus, "–")) val = Mathf.Max(min, val - 1);
            Widgets.Label(label, val.ToString());
            if (Widgets.ButtonText(plus, "+")) val = Mathf.Min(max, val + 1);
        }

        private static void DrawPassionField(Listing_Standard l, string label, ref int value)
        {
            var row = l.GetRect(RowHeight);
            Widgets.Label(new Rect(row.x, row.y, LabelWidth, row.height), label);
            var sliderRect = new Rect(row.x + LabelWidth + LabelSliderGap, row.y + (row.height - SliderHeight) / 2, row.width - LabelWidth - LabelSliderGap, SliderHeight);
            var cur = Mathf.Clamp(value, 0, 4);
            int v = Mathf.RoundToInt(Widgets.HorizontalSlider(sliderRect, cur, 0, 4, middleAlignment: true));
            TooltipHandler.TipRegion(sliderRect, "0 = don't change\n1..4 = set passion to this priority");
            value = Mathf.Clamp(v, 0, 4);
        }
    }
}