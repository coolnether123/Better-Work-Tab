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
            //l.Label("Priority: " + s.rule_BestDoctorsPriority);
            //s.rule_BestDoctorsPriority = Mathf.Clamp(
                //Mathf.RoundToInt(Widgets.HorizontalSlider(l.GetRect(22f), s.rule_BestDoctorsPriority, 1, 4, middleAlignment: true)),
                //1, 4);

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

            l.GapLine();
            l.Label("Debug Logging:");
            // Level slider 0..5 with labels
            var row1 = l.GetRect(RowHeight);
            Widgets.Label(new Rect(row1.x, row1.y, LabelWidth, row1.height), "Level");
            var levelRect = new Rect(row1.x + LabelWidth + LabelSliderGap, row1.y + (row1.height - SliderHeight) / 2, row1.width - LabelWidth - LabelSliderGap, SliderHeight);
            int lvl = Mathf.Clamp(s.debugLogLevel, 0, 5);
            int newLvl = Mathf.RoundToInt(Widgets.HorizontalSlider(levelRect, lvl, 0, 5, middleAlignment: true,
                leftAlignedLabel: "Off", rightAlignedLabel: "Trace", roundTo: 1));
            s.debugLogLevel = Mathf.Clamp(newLvl, 0, 5);
            TooltipHandler.TipRegion(levelRect, "0-Off, 1-Error, 2-Warn, 3-Info, 4-Debug, 5-Trace");

            l.CheckboxLabeled("Log drag: columns (verbose)", ref s.debugLogDragColumns, "Extra Debug/Trace logs for column dragging.");
            l.CheckboxLabeled("Log drag: rows (verbose)", ref s.debugLogDragRows, "Extra Debug/Trace logs for row dragging.");

            var row2 = l.GetRect(RowHeight);
            Widgets.Label(new Rect(row2.x, row2.y, LabelWidth, row2.height), "Max/sec");
            var rateRect = new Rect(row2.x + LabelWidth + LabelSliderGap, row2.y + (row2.height - SliderHeight) / 2, row2.width - LabelWidth - LabelSliderGap, SliderHeight);
            int rate = Mathf.Clamp(s.debugLogMaxPerSecond, 0, 60);
            int newRate = Mathf.RoundToInt(Widgets.HorizontalSlider(rateRect, rate, 0, 60, middleAlignment: true));
            s.debugLogMaxPerSecond = Mathf.Clamp(newRate, 0, 60);
            TooltipHandler.TipRegion(rateRect, "Throttle for Debug/Trace logs per category (0 = unlimited)");

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
