using RimWorld;
using Spine.UI.WidgetExtensions;
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
            /// Settings TODO:
            l.CheckboxLabeled("Enable Skill OverlayFeature", ref s.enableSkillOverlayFeature, "Whether to show the Skill Overlay when shift is pressed in the work tab.");
            l.CheckboxLabeled("Enable Auto Assign Feature", ref s.enableAutoAssignFeature, "Whether to show the auto assign button on the work tab.");
            l.CheckboxLabeled("Enable Pawn and Worktype Highlights", ref s.ShowPawnAndWorktypeHighlights, "Whether to enable any row/column highlights in the work tab.");
            l.CheckboxLabeled("Show Cursor and Worktype Highlights", ref s.ShowCursorPawnAndWorktypeHighlight, "Whether to highlight rows/columns when hovered with the cursor.");
            l.CheckboxLabeled("Show Float Menu Pawn And Worktype Highlight", ref s.ShowFloatMenuPawnAndWorktypeHighlight, "Whether to show the highlight in the work tab when opened from a float menu.");
            l.CheckboxLabeled("Enable Selected Pawn Highlight", ref s.DoSelectedPawnHighlight, "Whether to highlight the currently selected pawn in the work tab.");
            l.CheckboxLabeled("Use Custom Mouse Hover Highlight", ref s.UseCustomMouseHoverHighlight, "Whether to use a separate color for currently hovered worktype(?)");
            l.CheckboxLabeled("Enable Row and Column Highlighting", ref s.enableRowColumnHighlights, "If disabled, the Better Work Tab will stop tinting hovered headers and rows.");
            l.CheckboxLabeled("Show Pawn Activity Overlay", ref s.showPawnActivityOverlay, "Draw a lightweight heat-map style indicator that shows each pawn's active job.");
            l.CheckboxLabeled("Show Pawn Count at Bottom", ref s.showPawnCountAtBottom, "Adds the current colonist count to the lower left corner of the work tab.");
            l.CheckboxLabeled("Show Bed Count at Bottom", ref s.showBedCountAtBottom, "Also show how many colonist-usable beds exist on the current map.");
            l.CheckboxLabeled("Disable Left-Click Close", ref s.disableLeftClickClose, "Prevents the tab from closing when clicking outside of it.");
            l.CheckboxLabeled("Require Ctrl for Drag Reordering", ref s.requireCtrlForDrag, "Uncheck to allow dragging rows/columns without holding Ctrl.");
            l.CheckboxLabeled("Row Drag Overlay Uses Insertion Line Only", ref s.showOnlyLineDragIndicatorRows, "When enabled, dragging a row only shows the insertion line.");
            l.CheckboxLabeled("Column Drag Overlay Uses Insertion Line Only", ref s.showOnlyLineDragIndicatorColumns, "When enabled, dragging a column only shows the insertion line.");
            SpineWidgets.LS_ChooseFromEnum<BetterWorkTabSettings.ShowUIMode>(l, "Show Small Skill Numbers", s, nameof(s.ShowUIMode_ShowSmallSkillNumbers));
            SpineWidgets.LS_ChooseFromEnum<BetterWorkTabSettings.ShowUIMode>(l, "Show Pawn for Skill Square", s, nameof(s.ShowUIMode_ShowPawnForSkillSquare));
            
            DrawDividerHeightSlider(l, s);
            l.CheckboxLabeled("Draw Divider Highlight", ref s.drawDividerHighlight, "Whether to draw a white highlight around the dividers.");
            
            //public enum ShowUIMode { Always, Never, Shifted, Unshifted }
        //ShowUIMode_ShowSmallSkillNumbers = ShowUIMode.Unshifted;
        //public ShowUIMode ShowUIMode_ShowPawnForSkillSquare = ShowUIMode.Shifted;


        l.NewColumn();
            //l.ColumnWidth = inRect.width / 4f - 12f;
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_VeryLowSkill), "Very Low Skill");
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_LowSkill), "Low Skill");
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_GoodLowSkill), "Good Low Skill");
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_ExcellentSkill), "Excellent Skill");
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_CursorHighlight), "Cursor Highlight");
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_FloatMenuHighlight), "Float Menu Highlight");
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_CustomMouseHighlight), "Custom Mouse Highlight", dependsOn: !s.UseCustomMouseHoverHighlight);
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_CustomSimilarWorktypeHighlight), "Custom Similar Worktype Highlight", dependsOn: !s.UseCustomMouseHoverHighlight);
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_IncapableBecauseOfCapacities), "Incapable Because of Capacities Highlight");
            SpineWidgets.LS_ColorPickButton_Settings(l, s, nameof(s.Color_BestPawnForSkillSquare), "Best Pawn for Skill Highlight");

            l.End();

            int butW = 150;
            int butH = 30;
            Rect resetButRect = new Rect(inRect.width - butW - 29f, 0f, butW, butH);
            if (Widgets.ButtonText(resetButRect, "Reset Defaults"))
            {
                Find.WindowStack.Add(new Dialog_Confirm("Really Restore ALL Defaults?", s.RestoreDefaults));
            }
        }

        private static void DrawRulesUI(Listing_Standard listing, BetterWorkTabSettings s)
        {

        }

        private static void DrawDividerHeightSlider(Listing_Standard l, BetterWorkTabSettings s)
        {
            var row = l.GetRect(RowHeight);
            Widgets.Label(new Rect(row.x, row.y, LabelWidth, row.height), "Divider Height");
            var sliderRect = new Rect(row.x + LabelWidth + LabelSliderGap, row.y + (row.height - SliderHeight) / 2, row.width - LabelWidth - LabelSliderGap, SliderHeight);
            var cur = Mathf.Clamp(s.dividerHeight, 1f, 30f);
            float v = Widgets.HorizontalSlider(sliderRect, cur, 1f, 30f, middleAlignment: true);
            TooltipHandler.TipRegion(sliderRect, "The height of the dividers in the work tab.");
            s.dividerHeight = Mathf.Round(v);
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
