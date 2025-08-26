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
            var l = new Listing_Standard { ColumnWidth = inRect.width / 2f - 12f };
            l.Begin(inRect);

            l.CheckboxLabeled("Enable skill overlay feature", ref s.enableSkillOverlayFeature,
                "If disabled, overlay-related patches do nothing.");

            l.GapLine();
            l.Label("Default starting priority (0 = don't change, 1..4 = force):");
            IntAdjust(ref s.defaultStartingPriority, 0, 4, l.GetRect(RowHeight));

            l.GapLine();
            l.Label("Core Work Types Priority:");
            l.CheckboxLabeled("Enable", ref s.rule_CoreAlwaysPriorityEnabled);
            l.Label("Priority: " + s.rule_CoreAlwaysPriorityValue);
            s.rule_CoreAlwaysPriorityValue = Mathf.Clamp(
                Mathf.RoundToInt(Widgets.HorizontalSlider(l.GetRect(22f), s.rule_CoreAlwaysPriorityValue, 1, 4, middleAlignment: true)),
                1, 4);

            l.CheckboxLabeled("Firefighter", ref s.core_Firefighter);
            l.CheckboxLabeled("Patient", ref s.core_Patient);
            l.CheckboxLabeled("BedRest", ref s.core_BedRest);
            l.CheckboxLabeled("Basic", ref s.core_Basic);

            

            l.NewColumn();

            l.CheckboxLabeled("Enable Auto-Assign feature", ref s.enableAutoAssignFeature,
                "If disabled, the 'Auto Assign Work' button does nothing.");

            l.GapLine();
            l.Label("Best Doctors Rule:");
            l.CheckboxLabeled("Enable", ref s.rule_BestDoctorsEnabled);
            l.Label("Priority: " + s.rule_BestDoctorsPriority);
            s.rule_BestDoctorsPriority = Mathf.Clamp(
                Mathf.RoundToInt(Widgets.HorizontalSlider(l.GetRect(22f), s.rule_BestDoctorsPriority, 1, 4, middleAlignment: true)),
                1, 4);

            l.GapLine();
            l.Label("Childcare Rule:");
            l.CheckboxLabeled("Enable", ref s.rule_ChildcareEnabled);
            l.Label("Priority: " + s.rule_ChildcarePriority);
            s.rule_ChildcarePriority = Mathf.Clamp(
                Mathf.RoundToInt(Widgets.HorizontalSlider(l.GetRect(22f), s.rule_ChildcarePriority, 1, 4, middleAlignment: true)),
                1, 4);

            l.GapLine();
            l.Label("Passion Overrides (0 = disabled / don't change)");
            l.CheckboxLabeled("Enable passion overrides", ref s.rule_PassionOverrideEnabled,
                "Map each passion level to a fixed priority. 0 = leave as is.");
            DrawPassionField(l, "None", ref s.passion_None);
            DrawPassionField(l, "Minor", ref s.passion_Minor);
            DrawPassionField(l, "Major", ref s.passion_Major);

            l.End();
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