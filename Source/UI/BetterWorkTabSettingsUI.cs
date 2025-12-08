using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using RimWorld;
using Spine.UI.ColourPicker;
using Spine.UI.SettingsFramework;
using Spine.UI.WidgetExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Main settings UI with category navigation and favorites support.
    /// </summary>
    public static class BetterWorkTabSettingsUI
    {
        private static Vector2 _scrollPosition;
        private static bool _initialized;

        // Category definitions
        private static readonly List<CategoryDefinition> Categories = new List<CategoryDefinition>
        {
            new CategoryDefinition(
                "autoassign",
                "Auto-Assign Rules",
                "Configure automatic work priority assignment rulesets",
                DrawAutoAssignCategory),

            new CategoryDefinition(
                "layout",
                "Layout & Interaction",
                "Window behavior, drag settings, and display options",
                DrawLayoutCategory),

            new CategoryDefinition(
                "overlay",
                "Skill Overlay",
                "Skill numbers, best pawn indicators, and overlay modes",
                DrawSkillOverlayCategory),

            new CategoryDefinition(
                "highlights",
                "Highlights & Hover",
                "Row, column, and pawn highlighting behavior",
                DrawHighlightsCategory),

            new CategoryDefinition(
                "colors",
                "Colors & Appearance",
                "Skill colors, highlight colors, and visual styling",
                DrawColorsCategory),

            new CategoryDefinition(
                "columns",
                "Column Management",
                "Work column ordering and reset options",
                DrawColumnManagementCategory),

            new CategoryDefinition(
                "reset",
                "Reset & Restore",
                "Restore defaults and manage saved data",
                DrawResetCategory)
        };

        /// <summary>
        /// Main entry point called by the mod settings window.
        /// </summary>
        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings settings)
        {
            EnsureInitialized();

            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // Favorites section (if any exist)
            DrawFavoritesSection(listing, settings);

            listing.GapLine();

            // Category navigation grid
            DrawCategoryNavigation(listing, settings);

            listing.End();
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            FavoritesManager.Instance.Initialize(GenFilePaths.ConfigFolderPath);
            _initialized = true;
        }


        private static void DrawFavoritesSection(Listing_Standard listing, BetterWorkTabSettings settings)
        {
            var favorites = FavoritesManager.Instance.GetAllFavorites();
            if (favorites.Count == 0)
            {
                Rect hintRect = listing.GetRect(24f);
                var oldColor = GUI.color;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(hintRect, "★ Click stars in category settings to pin favorites here");
                GUI.color = oldColor;
                listing.Gap(4f);
                return;
            }

            RimworldSettingsWidgets.SectionHeader(listing, "★ Pinned Settings");

            foreach (var favId in favorites)
            {
                DrawFavoriteSetting(listing, favId, settings);
            }

            listing.Gap(8f);
        }

        private static void DrawFavoriteSetting(
            Listing_Standard listing,
            string settingId,
            BetterWorkTabSettings settings)
        {
            // Map setting IDs to actual drawing logic
            switch (settingId)
            {
                case "enableSkillOverlay":
                    RimworldSettingsWidgets.CheckboxFavoritable(listing, settingId,
                        "Enable Skill Overlay",
                        ref settings.enableSkillOverlayFeature,
                        "Show skill numbers when holding Shift");
                    break;

                case "enableAutoAssign":
                    RimworldSettingsWidgets.CheckboxFavoritable(listing, settingId,
                        "Enable Auto-Assign Feature",
                        ref settings.enableAutoAssignFeature,
                        "Show auto-assign controls on work tab");
                    break;

                case "requireCtrlForDrag":
                    RimworldSettingsWidgets.CheckboxFavoritable(listing, settingId,
                        "Require Ctrl for Drag",
                        ref settings.requireCtrlForDrag,
                        "Hold Ctrl to drag rows/columns");
                    break;

                case "showPawnCount":
                    RimworldSettingsWidgets.CheckboxFavoritable(listing, settingId,
                        "Show Pawn Count",
                        ref settings.showPawnCountAtBottom,
                        "Display colonist count at bottom");
                    break;

                case "showBedCount":
                    RimworldSettingsWidgets.CheckboxFavoritable(listing, settingId,
                        "Show Bed Count",
                        ref settings.showBedCountAtBottom,
                        "Display bed count at bottom");
                    break;

                case "masterHighlights":
                    RimworldSettingsWidgets.CheckboxFavoritable(listing, settingId,
                        "Enable All Highlights",
                        ref settings.ShowPawnAndWorktypeHighlights,
                        "Master toggle for row/column highlighting");
                    break;

                case "dividerHeight":
                    settings.dividerHeight = RimworldSettingsWidgets.SliderFavoritable(listing, settingId,
                        "Divider Height",
                        settings.dividerHeight,
                        1f, 30f,
                        "Height of divider rows in pixels");
                    break;

                default:
                    // Unknown favorite, just skip
                    break;
            }
        }

        private static void DrawCategoryNavigation(Listing_Standard listing, BetterWorkTabSettings settings)
        {
            RimworldSettingsWidgets.SectionHeader(listing, "Settings Categories");

            listing.Gap(8f);

            // Calculate grid layout
            float availableWidth = listing.ColumnWidth;
            int columns = availableWidth > 500f ? 2 : 1;
            float buttonWidth = (availableWidth - (columns - 1) * 8f) / columns;
            float buttonHeight = 60f;

            int index = 0;
            Rect rowRect = Rect.zero;

            foreach (var category in Categories)
            {
                int col = index % columns;

                if (col == 0)
                {
                    rowRect = listing.GetRect(buttonHeight);
                    listing.Gap(8f);
                }

                Rect buttonRect = new Rect(
                    rowRect.x + col * (buttonWidth + 8f),
                    rowRect.y,
                    buttonWidth,
                    buttonHeight);

                if (RimworldSettingsWidgets.DrawCategoryButton(
                    buttonRect,
                    category.Label,
                    category.Description))
                {
                    OpenCategoryDialog(category);
                }

                index++;
            }

            listing.Gap(16f);

            // Quick access: Edit Rulesets button
            Rect rulesetRect = listing.GetRect(35f);
            DrawQuickRulesetAccess(rulesetRect, settings);
        }

        private static void DrawQuickRulesetAccess(Rect rect, BetterWorkTabSettings settings)
        {
            Rect labelRect = rect.LeftPart(0.5f);
            Rect buttonRect = rect.RightPart(0.48f);

            string activeRuleset = settings.CurrentRuleset?.Name ?? "None";

            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, $"Active Ruleset: {activeRuleset}");
            Text.Anchor = oldAnchor;

            if (Widgets.ButtonText(buttonRect, "Open Ruleset Manager"))
            {
                Find.WindowStack.Add(new Window_RulesManager());
            }
        }

        private static void OpenCategoryDialog(CategoryDefinition category)
        {
            var dialog = new Dialog_SettingsCategory(
                category.Id,
                category.Label,
                category.DrawAction);

            Find.WindowStack.Add(dialog);
        }

        #region Category Drawing Methods

        private static void DrawAutoAssignCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.CheckboxFavoritable(l, "enableAutoAssign",
                "Enable Auto-Assign Feature",
                ref s.enableAutoAssignFeature,
                "Show auto-assign controls on the work tab");

            l.Gap(12f);

            Rect rulesetInfoRect = l.GetRect(60f);
            Widgets.DrawBoxSolid(rulesetInfoRect, new Color(0.15f, 0.15f, 0.15f));
            Widgets.DrawBox(rulesetInfoRect, 1);

            Rect innerRect = rulesetInfoRect.ContractedBy(8f);
            string rulesetName = s.CurrentRuleset?.Name ?? "None selected";
            int ruleCount = s.CurrentRuleset?.Rules?.Count ?? 0;

            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 24f),
                $"Active: {rulesetName}");
            
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(innerRect.x, innerRect.y + 26f, innerRect.width, 20f),
                $"{ruleCount} rule(s) defined");
            GUI.color = Color.white;
            Text.Font = oldFont;

            l.Gap(12f);

            if (l.ButtonText("Open Ruleset Manager"))
            {
                Find.WindowStack.Add(new Window_RulesManager());
            }

            l.Gap(8f);

            l.Label("Quick Apply:", tooltip: "Apply a ruleset without opening the manager");
            l.Gap(4f);

            if (s.SavedRulesets != null)
            {
                foreach (var ruleset in s.SavedRulesets.Take(5))
                {
                    if (l.ButtonText($"  Apply: {ruleset.Name}"))
                    {
                        s.CurrentRuleset = ruleset;
                        if (ruleset.ResetBeforeApplying)
                        {
                            WorkAssignmentRuleset.SetAllToZero();
                        }
                        ruleset.ApplyAutoAssignments();
                        Messages.Message($"Applied ruleset: {ruleset.Name}", MessageTypeDefOf.TaskCompletion, false);
                    }
                }
            }
        }

        private static void DrawLayoutCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Window Behavior");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "disableLeftClickClose",
                "Disable Left-Click Close",
                ref s.disableLeftClickClose,
                "Prevents the tab from closing when clicking outside");

            RimworldSettingsWidgets.SectionHeader(l, "Drag & Drop");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "requireCtrlForDrag",
                "Require Ctrl for Drag Reordering",
                ref s.requireCtrlForDrag,
                "Hold Ctrl to drag rows/columns. Uncheck for direct dragging.");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Row Drag: Line Only",
                ref s.showOnlyLineDragIndicatorRows,
                "Show only insertion line when dragging rows (no ghost)");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Column Drag: Line Only",
                ref s.showOnlyLineDragIndicatorColumns,
                "Show only insertion line when dragging columns (no ghost)");

            RimworldSettingsWidgets.SectionHeader(l, "Bottom Display");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "showPawnCount",
                "Show Pawn Count",
                ref s.showPawnCountAtBottom,
                "Display colonist count in lower-left corner");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "showBedCount",
                "Show Bed Count",
                ref s.showBedCountAtBottom,
                "Display available beds (red if fewer than pawns)");

            RimworldSettingsWidgets.SectionHeader(l, "Dividers");

            s.dividerHeight = RimworldSettingsWidgets.SliderFavoritable(l, "dividerHeight",
                "Divider Height",
                s.dividerHeight, 1f, 30f,
                "Height of section dividers in pixels");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Draw Divider Highlight",
                ref s.drawDividerHighlight,
                "Show white border around dividers");
        }

        private static void DrawSkillOverlayCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.CheckboxFavoritable(l, "enableSkillOverlay",
                "Enable Skill Overlay Feature",
                ref s.enableSkillOverlayFeature,
                "Show skill numbers when holding Shift in work tab");

            l.Gap(12f);

            l.Label("Small Skill Numbers Display:");
            if (l.ButtonText($"  Mode: {s.ShowUIMode_ShowSmallSkillNumbers}"))
            {
                ShowEnumMenu<BetterWorkTabSettings.ShowUIMode>(
                    mode => s.ShowUIMode_ShowSmallSkillNumbers = mode);
            }

            l.Gap(8f);

            l.Label("Best Pawn Indicator Display:");
            if (l.ButtonText($"  Mode: {s.ShowUIMode_ShowPawnForSkillSquare}"))
            {
                ShowEnumMenu<BetterWorkTabSettings.ShowUIMode>(
                    mode => s.ShowUIMode_ShowPawnForSkillSquare = mode);
            }

            l.Gap(12f);

            var oldColor = GUI.color;
            GUI.color = Color.gray;
            l.Label("Modes: Always | Never | Shifted (Shift held) | Unshifted (Shift not held)");
            GUI.color = oldColor;
        }

        private static void DrawHighlightsCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Master Toggle");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "masterHighlights",
                "Enable All Highlights",
                ref s.ShowPawnAndWorktypeHighlights,
                "Master toggle for all row/column highlighting");

            if (!s.ShowPawnAndWorktypeHighlights)
            {
                l.Gap(8f);
                GUI.color = Color.gray;
                l.Label("(Enable master toggle to configure individual highlights)");
                GUI.color = Color.white;
                return;
            }

            RimworldSettingsWidgets.SectionHeader(l, "Cursor Highlights");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Highlight Hovered Row/Column",
                ref s.ShowCursorPawnAndWorktypeHighlight,
                "Highlight rows and columns under the cursor");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Enable Row/Column Tinting",
                ref s.enableRowColumnHighlights,
                "Apply color tint to hovered headers and rows");

            RimworldSettingsWidgets.SectionHeader(l, "Selection Highlights");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Highlight Selected Pawn",
                ref s.DoSelectedPawnHighlight,
                "Highlight the row of the currently selected pawn");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Float Menu Highlight",
                ref s.ShowFloatMenuPawnAndWorktypeHighlight,
                "Highlight pawn/worktype when opened from context menu");

            RimworldSettingsWidgets.SectionHeader(l, "Custom Hover Color");

            RimworldSettingsWidgets.CheckboxFavoritable(l, null,
                "Use Custom Mouse Hover Color",
                ref s.UseCustomMouseHoverHighlight,
                "Use separate color for hover (vs. derived from cursor highlight)");
        }

        private static void DrawColorsCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Skill Level Colors");

            Color c1 = s.Color_VeryLowSkill;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Very Low Skill (0-3)", ref c1);
            s.Color_VeryLowSkill = c1;

            Color c2 = s.Color_LowSkill;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Low Skill (4-9)", ref c2);
            s.Color_LowSkill = c2;

            Color c3 = s.Color_GoodLowSkill;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Good Skill (10-15)", ref c3);
            s.Color_GoodLowSkill = c3;

            Color c4 = s.Color_ExcellentSkill;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Excellent Skill (16+)", ref c4);
            s.Color_ExcellentSkill = c4;

            RimworldSettingsWidgets.SectionHeader(l, "Highlight Colors");

            Color h1 = s.Color_CursorHighlight;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Cursor Highlight", ref h1);
            s.Color_CursorHighlight = h1;

            Color h2 = s.Color_FloatMenuHighlight;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Float Menu Highlight", ref h2);
            s.Color_FloatMenuHighlight = h2;

            if (s.UseCustomMouseHoverHighlight)
            {
                Color h3 = s.Color_CustomMouseHighlight;
                RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Custom Mouse Hover", ref h3);
                s.Color_CustomMouseHighlight = h3;

                Color h4 = s.Color_CustomSimilarWorktypeHighlight;
                RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Similar Worktype", ref h4);
                s.Color_CustomSimilarWorktypeHighlight = h4;
            }

            RimworldSettingsWidgets.SectionHeader(l, "Special Indicators");

            Color s1 = s.Color_IncapableBecauseOfCapacities;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Incapable Indicator", ref s1);
            s.Color_IncapableBecauseOfCapacities = s1;

            Color s2 = s.Color_BestPawnForSkillSquare;
            RimworldSettingsWidgets.ColorPickerFavoritable(l, null, "Best Pawn Indicator", ref s2);
            s.Color_BestPawnForSkillSquare = s2;
        }

        private static void DrawColumnManagementCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            l.Label("Column Order Management", tooltip: "Work columns can be reordered by Ctrl+dragging in the work tab");

            l.Gap(12f);

            Rect infoRect = l.GetRect(40f);
            Widgets.DrawBoxSolid(infoRect, new Color(0.15f, 0.15f, 0.15f));
            infoRect = infoRect.ContractedBy(8f);

            int customCount = s.playerDraggedColumns?.Count ?? 0;
            GUI.color = customCount > 0 ? new Color(1f, 0.85f, 0.2f) : Color.gray;
            Widgets.Label(infoRect, customCount > 0
                ? $"{customCount} column(s) moved from vanilla position"
                : "All columns in vanilla order");
            GUI.color = Color.white;

            l.Gap(12f);

            if (l.ButtonText("Reset Columns to Vanilla Order"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Reset all work columns to vanilla order?\n\nAny custom column positions will be lost.",
                    () =>
                    {
                        WorkColumnOrderManager.ResetToVanilla();
                        Messages.Message("Columns reset to vanilla order", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }

            l.Gap(8f);

            GUI.color = Color.gray;
            l.Label("Tip: Columns marked with * have been moved from their vanilla position.");
            l.Label("Drag columns in the work tab to reorder execution priority.");
            GUI.color = Color.white;
        }

        private static void DrawResetCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Reset Settings");

            l.Gap(8f);

            GUI.color = new Color(1f, 0.7f, 0.7f);
            l.Label("⚠ These actions cannot be undone");
            GUI.color = Color.white;

            l.Gap(12f);

            if (l.ButtonText("Reset All Settings to Defaults"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Reset ALL Better Work Tab settings to defaults?\n\n" +
                    "This includes colors, toggles, and UI preferences.\n" +
                    "Rulesets will NOT be affected.",
                    () =>
                    {
                        s.RestoreDefaults();
                        Messages.Message("Settings restored to defaults", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }

            l.Gap(8f);

            if (l.ButtonText("Restore Default Rulesets"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Restore all default rulesets?\n\n" +
                    "This will ADD the default rulesets back.\n" +
                    "Custom rulesets will be preserved.",
                    () =>
                    {
                        s.AddDefaultRules();
                        Messages.Message("Default rulesets restored", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }

            l.Gap(8f);

            if (l.ButtonText("Reset Rulesets (Delete All Custom)"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "DELETE all rulesets and restore ONLY defaults?\n\n" +
                    "⚠ All custom rulesets will be permanently deleted!",
                    () =>
                    {
                        s.CreateDefaultRulesets();
                        Messages.Message("All rulesets reset to defaults", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }

            l.Gap(8f);

            if (l.ButtonText("Reset Work Column Order"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Reset work columns to vanilla order?",
                    () =>
                    {
                        WorkColumnOrderManager.ResetToVanilla();
                        Messages.Message("Columns reset to vanilla order", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }

            RimworldSettingsWidgets.SectionHeader(l, "Favorites");

            l.Gap(4f);

            int favCount = FavoritesManager.Instance.FavoriteCount;
            l.Label($"Pinned settings: {favCount}");

            if (favCount > 0 && l.ButtonText("Clear All Pinned Settings"))
            {
                foreach (var fav in FavoritesManager.Instance.GetAllFavorites().ToList())
                {
                    FavoritesManager.Instance.SetFavorite(fav, false);
                }
                FavoritesManager.Instance.SaveIfDirty(GenFilePaths.ConfigFolderPath);
                Messages.Message("Cleared all pinned settings", MessageTypeDefOf.TaskCompletion, false);
            }
        }

        #endregion

        #region Helpers

        private static void ShowEnumMenu<T>(Action<T> onSelect) where T : Enum
        {
            var options = new List<FloatMenuOption>();
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                T localValue = value;
                options.Add(new FloatMenuOption(value.ToString(), () => onSelect(localValue)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        #endregion

        /// <summary>
        /// Internal category definition for navigation.
        /// </summary>
        private class CategoryDefinition
        {
            public string Id { get; }
            public string Label { get; }
            public string Description { get; }
            public Action<Listing_Standard, BetterWorkTabSettings> DrawAction { get; }

            public CategoryDefinition(
                string id,
                string label,
                string description,
                Action<Listing_Standard, BetterWorkTabSettings> drawAction)
            {
                Id = id;
                Label = label;
                Description = description;
                DrawAction = drawAction;
            }
        }
    }
}
