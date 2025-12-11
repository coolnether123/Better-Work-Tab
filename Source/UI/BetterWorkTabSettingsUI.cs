using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using RimWorld;
using Spine.UI.ColourPicker;
using Spine.UI.SettingsFramework;
using Spine.UI.WidgetExtensions; // Renamed file usage
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    public static class BetterWorkTabSettingsUI
    {
        private static Vector2 _mainScrollPosition;
        private static bool _initialized;
        private static float _mainViewHeight = 1000f;

        // --- THE 6 BIG BUTTON CATEGORIES ---
        private static readonly List<CategoryDefinition> Categories = new List<CategoryDefinition>
        {
            new CategoryDefinition(
                "automation",
                "Automation",
                "Workloads, Auto-Assign rules, and manager behavior",
                DrawAutomationCategory),

            new CategoryDefinition(
                "appearance",
                "Appearance & Colors",
                "Skill levels, custom UI styling, and theming",
                DrawAppearanceCategory),

            new CategoryDefinition(
                "overlay",
                "Skill Overlay",
                "Visuals shown when holding Shift (Skills/Passions)",
                DrawSkillOverlayCategory),

            new CategoryDefinition(
                "interaction",
                "Interaction & Highlights",
                "Drag settings, selection highlights, and mouse behaviors",
                DrawInteractionCategory),

            new CategoryDefinition(
                "layout",
                "Layout & Columns",
                "Dimensions, dividers, bottom counters, and column reset",
                DrawLayoutCategory),

            new CategoryDefinition(
                "maintenance",
                "Maintenance",
                "Restoring defaults, clearing data, and debugging",
                DrawMaintenanceCategory)
        };

        public static void DoSettingsWindowContents(Rect inRect, BetterWorkTabSettings settings)
        {
            EnsureInitialized();

            // Main Scroll View
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, _mainViewHeight);
            Widgets.BeginScrollView(inRect, ref _mainScrollPosition, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // 1. Favorites Section (Dynamic)
            DrawFavoritesSection(listing, settings);
            
            listing.GapLine();
            listing.Gap(10f);

            // 2. Category Grid (6 Buttons)
            DrawCategoryNavigation(listing, settings);

            // Recalculate height for scrolling
            _mainViewHeight = listing.CurHeight + 50f;

            listing.End();
            Widgets.EndScrollView();
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            FavoritesManager.Instance.Initialize(GenFilePaths.ConfigFolderPath);
            _initialized = true;
        }

        private static void DrawFavoritesSection(Listing_Standard listing, BetterWorkTabSettings settings)
        {
            var favorites = FavoritesManager.Instance.GetAllFavorites();
            if (favorites.Count == 0)
            {
                Rect hintRect = listing.GetRect(24f);
                GUI.color = Color.gray;
                Widgets.Label(hintRect, "★ Click stars inside categories to pin your favorite settings here.");
                GUI.color = Color.white;
                return;
            }

            RimworldSettingsWidgets.SectionHeader(listing, "★ Pinned Settings");

            foreach (var favId in favorites)
            {
                // Dispatcher to draw the correct widget based on ID
                // Note: Every setting in the sub-menus must have a matching case here
                DrawFavoriteSetting(listing, favId, settings);
            }
        }

        private static void DrawCategoryNavigation(Listing_Standard listing, BetterWorkTabSettings settings)
        {
            float availableWidth = listing.ColumnWidth;
            // 2 Columns for big buttons
            int columns = 2; 
            float buttonWidth = (availableWidth - (columns - 1) * 10f) / columns;
            float buttonHeight = 70f; // Big buttons

            int index = 0;
            Rect rowRect = Rect.zero;

            foreach (var category in Categories)
            {
                int col = index % columns;

                if (col == 0)
                {
                    rowRect = listing.GetRect(buttonHeight);
                    listing.Gap(10f);
                }

                Rect buttonRect = new Rect(
                    rowRect.x + col * (buttonWidth + 10f),
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
        }

        private static void OpenCategoryDialog(CategoryDefinition category)
        {
            var dialog = new Dialog_SettingsCategory(
                category.Id,
                category.Label,
                category.DrawAction);
            Find.WindowStack.Add(dialog);
        }

        // ==================================================================================
        // CATEGORY DRAWING IMPLEMENTATIONS
        // ==================================================================================

        private static void DrawAutomationCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Auto-Assign Features");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "enableAutoAssign",
                "Enable Auto-Assign System",
                ref s.enableAutoAssignFeature,
                "Show the auto-assign button and enable ruleset logic.");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "hideWorkloadButton",
                "Hide 'Workloads' Button",
                ref s.hideWorkloadButton,
                "Hide the Workload management button from the Work tab footer.");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "hideAutoAssignButton",
                "Hide 'Auto-Assign' Button",
                ref s.hideAutoAssignButton,
                "Hide the specific ruleset button (accessible via Manager only).");

            l.Gap();

            if (l.ButtonText("Open Ruleset Manager"))
            {
                Find.WindowStack.Add(new Window_RulesManager());
            }

            RimworldSettingsWidgets.SectionHeader(l, "Behavior Templates (Future)");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "confirmRulesetApplication",
                "[Template] Confirm Ruleset Application",
                ref s.confirmRulesetApplication,
                "Requires confirmation dialog before overwriting priorities with a ruleset.");
        }

        private static void DrawAppearanceCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Skill Level Colors");

            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_verylow", "Very Low Skill (0-3)", ref s.Color_VeryLowSkill);
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_low", "Low Skill (4-9)", ref s.Color_LowSkill);
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_good", "Good Skill (10-15)", ref s.Color_GoodLowSkill);
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_exc", "Excellent Skill (16+)", ref s.Color_ExcellentSkill);

            RimworldSettingsWidgets.SectionHeader(l, "Highlight Colors");
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_cursor", "Cursor Hover", ref s.Color_CursorHighlight);
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_float", "Float Menu Selection", ref s.Color_FloatMenuHighlight);
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_incapable", "Incapable Warning", ref s.Color_IncapableBecauseOfCapacities);
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_best", "Best Pawn Indicator", ref s.Color_BestPawnForSkillSquare);
            
            if (s.UseCustomMouseHoverHighlight)
            {
                RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_cust_hover", "Custom Hover Tint", ref s.Color_CustomMouseHighlight);
                RimworldSettingsWidgets.ColorPickerFavoritable(l, "col_cust_sim", "Similar Worktype Tint", ref s.Color_CustomSimilarWorktypeHighlight);
            }

            RimworldSettingsWidgets.SectionHeader(l, "UI Theme Templates (Future)");
            
            // Templates for future wiring
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "tpl_header_text", "[Template] Header Text", ref s.Color_HeaderText, "Color of angled headers");
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "tpl_div_text", "[Template] Divider Text", ref s.Color_DividerText, "Default color for divider labels");
            RimworldSettingsWidgets.ColorPickerFavoritable(l, "tpl_borders", "[Template] Borders & Lines", ref s.Color_Borders, "Color of grid lines and borders");
        }

        private static void DrawSkillOverlayCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Main Settings");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "enableSkillOverlay",
                "Enable Skill Overlay",
                ref s.enableSkillOverlayFeature,
                "Show detailed skill info when holding Shift in the work tab.");

            l.Gap();
            l.Label((TaggedString)"Visual Modes:");

            // Note: Enum selection isn't strictly favoritable in the boolean/float sense, 
            // but we can wrap it if we expand the Favorite system. 
            // For now, these remain standard buttons, but we can wrap visibility logic.
            
            if (l.ButtonText($"Numbers: {s.ShowUIMode_ShowSmallSkillNumbers}"))
            {
                ShowEnumMenu<BetterWorkTabSettings.ShowUIMode>(m => s.ShowUIMode_ShowSmallSkillNumbers = m);
            }
            l.Label((TaggedString)"  (Shows small skill numbers in cells)", -1f, default(TipSignal?));
            l.Gap(4f);

            if (l.ButtonText($"Best Pawn: {s.ShowUIMode_ShowPawnForSkillSquare}"))
            {
                ShowEnumMenu<BetterWorkTabSettings.ShowUIMode>(m => s.ShowUIMode_ShowPawnForSkillSquare = m);
            }
            l.Label((TaggedString)"  (Highlights the pawn with the highest skill)", -1f, default(TipSignal?));
        }

        private static void DrawInteractionCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Drag & Drop Behavior");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "requireCtrlForDrag",
                "Require Ctrl for Dragging",
                ref s.requireCtrlForDrag,
                "Prevents accidental drags. Uncheck to drag directly.");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "rowDragLineOnly",
                "Simple Row Drag Overlay",
                ref s.showOnlyLineDragIndicatorRows,
                "Shows only a line instead of a ghost image when moving rows.");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "colDragLineOnly",
                "Simple Column Drag Overlay",
                ref s.showOnlyLineDragIndicatorColumns,
                "Shows only a line instead of a ghost image when moving columns.");

            RimworldSettingsWidgets.SectionHeader(l, "Mouse Highlighting");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "masterHighlights",
                "Enable All Highlights",
                ref s.ShowPawnAndWorktypeHighlights,
                "Master switch for row/column tinting.");

            if (s.ShowPawnAndWorktypeHighlights)
            {
                RimworldSettingsWidgets.CheckboxFavoritable(l, "useOutlineHighlights",
                    "Use Outline Highlights",
                    ref s.useOutlineHighlights,
                    "Draw highlights as outlines instead of solid boxes.");

                RimworldSettingsWidgets.CheckboxFavoritable(l, "hoverHighlights",
                    "Highlight on Hover",
                    ref s.ShowCursorPawnAndWorktypeHighlight,
                    "Tint row/column under cursor.");

                RimworldSettingsWidgets.CheckboxFavoritable(l, "selectHighlight",
                    "Highlight Selected Pawn",
                    ref s.DoSelectedPawnHighlight,
                    "Always highlight the selected pawn's row.");

                RimworldSettingsWidgets.CheckboxFavoritable(l, "floatHighlight",
                    "Highlight Context Source",
                    ref s.ShowFloatMenuPawnAndWorktypeHighlight,
                    "Highlight row/column when right-click menu is open.");

                RimworldSettingsWidgets.CheckboxFavoritable(l, "useCustomHover",
                    "Use Custom Hover Colors",
                    ref s.UseCustomMouseHoverHighlight,
                    "Enable separate color pickers for hover states.");
            }

            RimworldSettingsWidgets.SectionHeader(l, "Interaction Templates (Future)");
            
            s.dragStartThreshold = RimworldSettingsWidgets.SliderFavoritable(l, "tpl_dragThresh", 
                "[Template] Drag Sensitivity", s.dragStartThreshold, 0f, 20f, "Pixels mouse must move to start drag");
            
            s.scrollSpeed = RimworldSettingsWidgets.SliderFavoritable(l, "tpl_scrollSpd", 
                "[Template] Auto-Scroll Speed", s.scrollSpeed, 1f, 50f, "Speed of scroll when dragging near edge");
        }

        private static void DrawLayoutCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "General Layout");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "disableLeftClickClose",
                "Prevent Click-Off Close",
                ref s.disableLeftClickClose,
                "Keep tab open when clicking the map.");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "showPawnCount",
                "Show Colonist Count",
                ref s.showPawnCountAtBottom,
                "Bottom-left counter.");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "showBedCount",
                "Show Bed Count",
                ref s.showBedCountAtBottom,
                "Bottom-left counter (Red if insufficient).");

            RimworldSettingsWidgets.SectionHeader(l, "Dividers");

            s.dividerHeight = RimworldSettingsWidgets.SliderFavoritable(l, "dividerHeight",
                "Divider Height", s.dividerHeight, 10f, 50f, "Height in pixels");

            RimworldSettingsWidgets.CheckboxFavoritable(l, "drawDividerHighlight",
                "Draw Divider Border",
                ref s.drawDividerHighlight,
                "Visual border around divider rows.");

            s.dividerMinAlpha = RimworldSettingsWidgets.SliderFavoritable(l, "dividerMinAlpha",
                "Divider Minimum Alpha",
                s.dividerMinAlpha,
                0f,
                1f,
                "Lower bound for divider background opacity so dividers stay visible.");

            RimworldSettingsWidgets.SectionHeader(l, "Column Management");

            if (l.ButtonText("Reset Columns to Baseline"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Reset all work columns to this save's baseline order? (Includes modded columns in their original spots.)",
                    () => {
                        WorkColumnOrderManager.ResetToBaseline();
                        Messages.Message("Columns reset to baseline order.", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }

            if (l.ButtonText("Reset Columns to True Vanilla"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Reset work columns to RimWorld vanilla order?\n\nModded columns will be moved to the end in their baseline order.",
                    () => {
                        WorkColumnOrderManager.ResetToTrueVanilla();
                        Messages.Message("Columns reset to true vanilla order.", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }
        }

        private static void DrawMaintenanceCategory(Listing_Standard l, BetterWorkTabSettings s)
        {
            RimworldSettingsWidgets.SectionHeader(l, "Reset Configuration");
            
            if (l.ButtonText("Restore Factory Defaults"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Reset ALL settings (colors, behaviors) to default? Rulesets are safe.",
                    s.RestoreDefaults));
            }

            l.Gap(6f);

            if (l.ButtonText("Restore Default Rulesets"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "Re-add default rulesets? (Duplicates may occur if renamed)",
                    s.AddDefaultRules));
            }

            l.Gap(6f);

            if (l.ButtonText("Nuke & Reset Rulesets"))
            {
                Find.WindowStack.Add(new Dialog_Confirm(
                    "DELETE ALL CUSTOM RULES and restore defaults only?",
                    () => {
                        s.CreateDefaultRulesets();
                        Messages.Message("Rulesets reset.", MessageTypeDefOf.TaskCompletion, false);
                    }));
            }

            RimworldSettingsWidgets.SectionHeader(l, "Data Management");

            if (l.ButtonText("Clear Pinned Favorites"))
            {
                foreach (var fav in FavoritesManager.Instance.GetAllFavorites().ToList())
                {
                    FavoritesManager.Instance.SetFavorite(fav, false);
                }
                FavoritesManager.Instance.SaveIfDirty(GenFilePaths.ConfigFolderPath);
                Messages.Message("Favorites cleared.", MessageTypeDefOf.TaskCompletion, false);
            }

            RimworldSettingsWidgets.CheckboxFavoritable(l, "enableDebugLogging",
                "Enable Debug Logging",
                ref s.enableDebugLogging,
                "Shows detailed debug messages in the log.");

            if (s.enableDebugLogging)
            {
                RimworldSettingsWidgets.SectionHeader(l, "Debug Features");

                foreach (var feature in Enum.GetValues(typeof(DebugFeature)).Cast<DebugFeature>())
                {
                    bool enabled = s.debugFeatureToggles[feature];
                    Widgets.CheckboxLabeled(l.GetRect(24f), $"Debug: {feature}", ref enabled);
                    s.debugFeatureToggles[feature] = enabled;
                }
            }
        }

        // ==================================================================================
        // DISPATCHER FOR FAVORITES
        // ==================================================================================

        /// <summary>
        /// Renders a specific setting row based on its ID.
        /// This allows favorites to appear on the main page.
        /// </summary>
        private static void DrawFavoriteSetting(Listing_Standard l, string settingId, BetterWorkTabSettings s)
        {
            // Note: This switch statement essentially mirrors the drawing logic in the categories.
            // When you add a new favoritable setting, add it here too.
            switch (settingId)
            {
                // Automation
                case "enableAutoAssign":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Enable Auto-Assign", ref s.enableAutoAssignFeature); break;
                case "hideWorkloadButton":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Hide Workload Button", ref s.hideWorkloadButton); break;
                case "hideAutoAssignButton":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Hide Auto-Assign Button", ref s.hideAutoAssignButton); break;
                
                // Overlay
                case "enableSkillOverlay":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Enable Skill Overlay", ref s.enableSkillOverlayFeature); break;
                case "useOutlineHighlights":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Use Outline Highlights", ref s.useOutlineHighlights); break;


                // Colors
                case "col_verylow": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Very Low Skill", ref s.Color_VeryLowSkill); break;
                case "col_low": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Low Skill", ref s.Color_LowSkill); break;
                case "col_good": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Good Skill", ref s.Color_GoodLowSkill); break;
                case "col_exc": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Excellent Skill", ref s.Color_ExcellentSkill); break;
                case "col_cursor": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Cursor Highlight", ref s.Color_CursorHighlight); break;
                
                // Interaction
                case "requireCtrlForDrag":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Require Ctrl to Drag", ref s.requireCtrlForDrag); break;
                case "masterHighlights":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Master Highlights", ref s.ShowPawnAndWorktypeHighlights); break;
                case "hoverHighlights":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Hover Highlights", ref s.ShowCursorPawnAndWorktypeHighlight); break;
                case "useCustomHover":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Custom Hover Color", ref s.UseCustomMouseHoverHighlight); break;
                
                // Layout
                case "disableLeftClickClose":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Prevent Click-Off Close", ref s.disableLeftClickClose); break;
                case "dividerHeight":
                    s.dividerHeight = RimworldSettingsWidgets.SliderFavoritable(l, settingId, "Divider Height", s.dividerHeight, 10f, 50f); break;
                case "showPawnCount":
                    RimworldSettingsWidgets.CheckboxFavoritable(l, settingId, "Show Pawn Count", ref s.showPawnCountAtBottom); break;

                // Templates (Future)
                case "tpl_dragThresh": s.dragStartThreshold = RimworldSettingsWidgets.SliderFavoritable(l, settingId, "Drag Sensitivity", s.dragStartThreshold, 0f, 20f); break;
                case "tpl_scrollSpd": s.scrollSpeed = RimworldSettingsWidgets.SliderFavoritable(l, settingId, "Scroll Speed", s.scrollSpeed, 1f, 50f); break;
                case "tpl_header_text": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Header Text", ref s.Color_HeaderText); break;
                case "tpl_div_text": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Divider Text", ref s.Color_DividerText); break;
                case "tpl_borders": RimworldSettingsWidgets.ColorPickerFavoritable(l, settingId, "Borders", ref s.Color_Borders); break;

                default:
                    // Fallback for ID mismatches
                    break;
            }
        }

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
