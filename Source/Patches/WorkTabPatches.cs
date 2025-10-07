using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

    /// <summary>
    /// Contains core Harmony patches and utility classes for enhancing the RimWorld Work tab with features from the Better Work Tab mod.
    /// Key enhancements include skill level overlays, pawn and work type highlights, auto-assignment buttons with ruleset dropdowns,
    /// workload management buttons, and integration with float menus. The patches are organized into sections:
    /// - State and Helpers: Classes like SkillOverlayState and ShiftHelper for tracking toggles and input states.
    /// - Drawers: CustomWorkBoxDrawer for rendering skill overlay boxes while preserving vanilla visuals.
    /// - UI Patches: Modifications to MainTabWindow_Work.DoWindowContents for adding toggles, buttons, and dropdowns.
    /// - Cell Patches: Overrides to PawnColumnWorker_WorkPriority.DoCell for replacing priorities with skills and adding corner highlights.
    /// - Table Patches: Extensions to PawnTableList.DoRow for interaction highlights.
    /// - Window Patches: Postfix to Window.PreClose (filtered to MainTabWindow_Work) for cleanup on tab close.
    /// All patches target vanilla RimWorld classes using Harmony Postfix, Prefix, or Transpiler methods to extend functionality without overriding original code.
    /// Initialization occurs in BetterWorkTabMod constructor via Harmony.PatchAll. Preserves vanilla behaviors such as DoCell drawing (via WidgetsWork),
    /// tooltips, click sounds, and respects table state (cachedPawns, Columns). Compatibility focuses on vanilla Work tab structure.
    /// </summary>
    namespace Better_Work_Tab.Patches
    {

        /// <summary>
        /// Global static state management for the skill overlay feature in the work tab.
        /// Toggled via checkbox in the MainTabWindow_Work header; allows showing average skill levels instead of priorities.
        /// </summary>
        public static class SkillOverlayState
        {
            /// <summary>
            /// Flag indicating whether the skill overlay is currently active.
            /// When true, priority cells are replaced with average skill levels (0-20, color-coded per settings). Supports temporary view via Shift key override.
            /// Defaults to false; set via UI toggle in Patch_WorkTab_AddSingleToggle.Postfix.
            /// </summary>
            public static bool ShowSkills = false;
        }

    /// <summary>
    /// Utility class for drawing custom work boxes during skill overlay mode, preserving vanilla visual elements such as
    /// background textures, passion indicators, incapable tints, and age-disability feedback (messages and sounds).
    /// Used in Patch_WorkPriority_DoCell_ReplaceNumber.Prefix to render consistent UI under overlay.
    /// </summary>
    public static class CustomWorkBoxDrawer
    {
        /// <summary>
        /// Draws a work box for the skill overlay mode without drawing the priority number or handling priority clicks.
        /// Handles age-disability by drawing a special texture and showing a message/sound on click if the pawn is too young.
        /// For incapables due to capacities, applies a red tint from settings. Calls vanilla WidgetsWork.DrawWorkBoxBackground for passions and base visuals.
        /// </summary>
        /// <param name="x">The x-coordinate for drawing the box.</param>
        /// <param name="y">The y-coordinate for drawing the box.</param>
        /// <param name="p">The pawn for which the work box is being drawn.</param>
        /// <param name="wType">The WorkTypeDef representing the work column.</param>
        /// <param name="incapableBecauseOfCapacities">Flag indicating if the pawn is incapable due to health capacities; if true, tints the box red.</param>
        public static void DrawWorkBoxForSkillOverlay(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
        {
            if (p.WorkTypeIsDisabled(wType))
            {
                // Vanilla age-disable: Texture and click feedback (Message/SoundDefOf).
                int minAgeRequired;
                if (!p.IsWorkTypeDisabledByAge(wType, out minAgeRequired))
                    return;

                Rect rect = new Rect(x, y, 25f, 25f);

                if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                {
                    Messages.Message("MessageWorkTypeDisabledAge".Translate(p, p.ageTracker.AgeBiologicalYears, wType.labelShort, minAgeRequired), p, MessageTypeDefOf.RejectInput, false);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
                GUI.DrawTexture(rect, WidgetsWork.WorkBoxBGTex_AgeDisabled);
            }
            else
            {
                Rect rect = new Rect(x, y, 25f, 25f);

                if (incapableBecauseOfCapacities)
                    GUI.color = BetterWorkTabMod.Settings.Color_IncapableBecauseOfCapacities;

                WidgetsWork.DrawWorkBoxBackground(rect, p, wType); // Vanilla bg + passions.

                GUI.color = Color.white;
            }
        }
    }

    /// <summary>
    /// Harmony postfix patch on MainTabWindow_Work.DoWindowContents to add custom UI elements to the work tab header.
    /// Adds a skill overlay toggle checkbox positioned right of the "Manual priorities" label.
    /// Includes an auto-assign button displaying the current ruleset name, which applies assignments when clicked,
    /// and a "..." button to open a FloatMenu for selecting/renaming/deleting saved rulesets.
    /// Also adds workload management buttons for applying, creating new, renaming, or deleting workloads via GameComponent_WorkloadSaver.
    /// Skips drawing during Layout events to avoid interference with vanilla UI layout.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class Patch_WorkTab_AddSingleToggle
    {
        /// <summary>
        /// Constants for positioning the skill toggle checkbox in the header.
        /// Positioned at x=150 (right of "Manual priorities"), y=5, size 230x30.
        /// </summary>
        private const float SkillToggleX_RightOfManualPriorities = 150f;
        private const float SkillToggleY_Top = 5f;
        private const float SkillToggleWidth = 230f;
        private const float SkillToggleHeight = 30f;

        /// <summary>
        /// Constants for the auto-assign button size and margins in the header.
        /// Width/height 150x28, margin x=6, y=2.
        /// </summary>
        private const float AutoAssignButtonWidth = 150f;
        private const float AutoAssignButtonHeight = 28f;
        private const float AutoAssignButtonMarginX = 6f;
        private const float AutoAssignButtonMarginY = 2f;

        /// <summary>
        /// Constants for the workload button size and margins in the header.
        /// Width/height 150x28, margin y=2, x positioned after auto-assign.
        /// </summary>
        private const float AssignWorkloadButtonWidth = 150f;
        private const float AssignWorkloadButtonHeight = 28f;
        private const float AssignWorkloadButtonMarginX = AutoAssignButtonHeight + AutoAssignButtonWidth + AutoAssignButtonMarginX + 6f;
        private const float AssignWorkloadButtonMarginY = 2f;

        /// <summary>
        /// Postfix method executed after vanilla DoWindowContents to draw custom UI elements if the skill overlay feature is enabled in settings.
        /// Skips if Event is null or Layout type to avoid layout interference. Draws the skill toggle checkbox and calls private methods for buttons.
        /// Toggles SkillOverlayState.ShowSkills on change, playing a tiny tick sound.
        /// </summary>
        /// <param name="rect">The full window rectangle for the work tab.</param>
        public static void Postfix(Rect rect)
        {
            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature) return;

            if (Event.current == null || Event.current.type == EventType.Layout) return;

            Rect toggleRect = new Rect(SkillToggleX_RightOfManualPriorities, SkillToggleY_Top, SkillToggleWidth, SkillToggleHeight);

            bool show = SkillOverlayState.ShowSkills;
            Widgets.CheckboxLabeled(toggleRect, "Show skill levels (0–20)", ref show);

            if (show != SkillOverlayState.ShowSkills)
            {
                SkillOverlayState.ShowSkills = show;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            DrawAutoAssignButtons(rect);
            DrawCurrentWorkloadsButtons(rect);
        }

        /// <summary>
        /// Private method to draw the auto-assign button and dropdown in the work tab header.
        /// Button displays the name of CurrentAutoAssignRuleset and applies it on click (resets priorities first if flagged).
        /// "..." button opens a FloatMenu listing all saved rulesets for selection. Logs error if no ruleset selected.
        /// Plays low tick sound on interactions.
        /// </summary>
        /// <param name="headerRect">The rectangle for positioning the buttons in the header.</param>
        private static void DrawAutoAssignButtons(Rect headerRect)
        {
            var size = new Vector2(AutoAssignButtonWidth, AutoAssignButtonHeight);
            var btn = new Rect(headerRect.xMax - size.x - size.y - AutoAssignButtonMarginX, headerRect.y + AutoAssignButtonMarginY, size.x, size.y);

            if (BetterWorkTabMod.Settings.CurrentAutoAssignRuleset == null)
            {
                Log.Error("[Better Work Tab] No ruleset selected.");
                return;
            }
            var curRuleset = BetterWorkTabMod.Settings.CurrentAutoAssignRuleset;

            if (Widgets.ButtonText(btn, curRuleset.Name))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                
                if (curRuleset.ResetBeforeApplying)
                    WorkAssignmentRuleset.SetAllToZero();
                curRuleset.ApplyAutoAssignments();
            }

            var btn2 = new Rect(btn.x + btn.width, btn.y, btn.height, btn.height);
            if (Widgets.ButtonText(btn2, "..."))
            {
                var options = new List<FloatMenuOption>();
                foreach (var ruleset in BetterWorkTabMod.Settings.SavedRulesets)
                {
                    var localRuleset = ruleset;
                    options.Add(new FloatMenuOption(ruleset.Name, () => {
                        BetterWorkTabMod.Settings.CurrentAutoAssignRuleset = localRuleset;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        /// <summary>
        /// Private method to draw the workload button and dropdown in the work tab header.
        /// Retrieves saved workloads from GameComponent_WorkloadSaver, reverses for recent-first display.
        /// Button shows current workload name or "New Workload" and applies on click. "..." opens FloatMenu for selection, new, rename, delete.
        /// Rename/delete use sub-FloatMenus positioned at screen center. Logs error if no workload.
        /// Plays low tick sound on interactions.
        /// </summary>
        /// <param name="headerRect">The rectangle for positioning the buttons in the header.</param>
        private static void DrawCurrentWorkloadsButtons(Rect headerRect)
        {
            GameComponent_WorkloadSaver workloadSaver = Current.Game.GetComponent<GameComponent_WorkloadSaver>();
            var size = new Vector2(AssignWorkloadButtonWidth, AssignWorkloadButtonHeight);
            var btn = new Rect(headerRect.xMax - size.x - size.y - AssignWorkloadButtonMarginX, headerRect.y + AssignWorkloadButtonMarginY, size.x, size.y);

            if (workloadSaver.CurrentWorklist == null)
            {
                if (Widgets.ButtonText(btn, "New Workload"))
                    CreateNewWorkload(workloadSaver);
            }
            else
            {
                if (Widgets.ButtonText(btn, workloadSaver.CurrentWorklist.RenamableLabel))
                {
                    if (workloadSaver.CurrentWorklist != null)
                    {
                        workloadSaver.CurrentWorklist.Apply();
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }
                    else
                        Log.Error("[Better Work Tab] No workload selected.");
                }
            }

            var btn2 = new Rect(btn.x + btn.width, btn.y, btn.height, btn.height);
            if (Widgets.ButtonText(btn2, "..."))
            {
                var options = new List<FloatMenuOption>();
                
                var workloads = workloadSaver.SavedWorklists.ListFullCopy();
                workloads.Reverse(); // Show most recently added at the top
                foreach (var workload in workloads)
                {
                    options.Add(new FloatMenuOption(workload.RenamableLabel, () => {
                        workloadSaver.CurrentWorklist = workload;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }));
                }
                options.Add(new FloatMenuOption("New Workload", () => CreateNewWorkload(workloadSaver)));

                if (workloads.Any())
                {
                    options.Add(new FloatMenuOption("Rename Workload", () => {
                        var renamableOptions = workloads.Select(w => new FloatMenuOption($"Rename {w.RenamableLabel}", () => {
                            Find.WindowStack.Add(new Dialog_RenameWorkload(w));
                            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        })).ToList();
                        var screenWidth = Verse.UI.screenWidth;
                        var screenHeight = Verse.UI.screenHeight;
                        var menuRect = new Rect(screenWidth / 2f - 100f, screenHeight / 2f - 75f, 200f, 150f);
                        var renamablesMenu = new FloatMenu(renamableOptions) { windowRect = menuRect };
                        Find.WindowStack.Add(renamablesMenu);
                    }));

                    options.Add(new FloatMenuOption("Delete Saved Workload", () => {
                        var deletableOptions = workloads.Select(w => new FloatMenuOption($"Delete {w.RenamableLabel}", () => {
                            int index = workloadSaver.SavedWorklists.IndexOf(w);
                            workloadSaver.SavedWorklists.Remove(w);
                            if (!workloadSaver.SavedWorklists.Any())
                            {
                                workloadSaver.CurrentWorklist = null;
                            }
                            else
                            {
                                int newCurrentIndex = Mathf.Max(0, Mathf.Min(index - 1, workloadSaver.SavedWorklists.Count - 1));
                                workloadSaver.CurrentWorklist = workloadSaver.SavedWorklists[newCurrentIndex];
                            }
                            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        })).ToList();
                        var screenWidth = Verse.UI.screenWidth;
                        var screenHeight = Verse.UI.screenHeight;
                        var menuRect = new Rect(screenWidth / 2f - 100f, screenHeight / 2f - 75f, 200f, 150f);
                        var deletablesMenu = new FloatMenu(deletableOptions) { windowRect = menuRect };
                        Find.WindowStack.Add(deletablesMenu);
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        /// <summary>
        /// Private method to create a new Worklist instance and open a naming dialog.
        /// Generates a default name "Custom Workload {count}", adds to SavedWorklists, sets as CurrentWorklist.
        /// Called from button clicks in DrawCurrentWorkloadsButtons.
        /// </summary>
        /// <param name="workloadSaver">The GameComponent_WorkloadSaver instance managing workloads.</param>
        private static void CreateNewWorkload(GameComponent_WorkloadSaver workloadSaver)
        {
            var newWorkload = new Worklist($"Custom Workload {workloadSaver.SavedWorklists.Count}");
            Find.WindowStack.Add(new Dialog_NameNewWorklist(newWorkload));
            workloadSaver.SavedWorklists.Add(newWorkload);
            workloadSaver.CurrentWorklist = newWorkload;
        }
    }

    /// <summary>
    /// Harmony prefix patch on PawnColumnWorker_WorkPriority.DoCell to implement the skill overlay mode.
    /// When active (via toggle or Shift key), suppresses vanilla priority drawing and instead displays the pawn's average skill level for the work type
    /// (calculated via pawn.skills.AverageOfRelevantSkillsFor, clamped 0-20). Draws a custom work box using CustomWorkBoxDrawer,
    /// applies color-coding based on skill level from settings, and preserves tooltips, click feedback for age disabilities, and incapable tints.
    /// Skips work types with no relevant skills (e.g., Hauling on Shift-only). Returns false to skip vanilla postfix.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_ReplaceNumber
    {
        /// <summary>
        /// Prefix method that runs before the vanilla DoCell to check conditions and draw the skill overlay if applicable.
        /// Suppresses vanilla drawing by returning false if overlay is active. Calculates average skill, draws the box and number during Repaint event,
        /// handles incapable/age cases, applies tooltip, and restores GUI state. Checks for dead pawns, disabled work settings, or age-disabled types to skip.
        /// </summary>
        /// <param name="__instance">The PawnColumnWorker_WorkPriority instance.</param>
        /// <param name="rect">The rectangle for the cell in the pawn table.</param>
        /// <param name="pawn">The pawn for which the cell is being drawn.</param>
        /// <param name="table">The PawnTable containing the row data.</param>
        /// <returns>True to allow vanilla postfix, false to suppress it (overlay active).</returns>
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            bool shiftHeld = Event.current?.shift ?? false;
            var wt = __instance.def.workType;

            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature || (!SkillOverlayState.ShowSkills && !shiftHeld))
                return true;

            if (!SkillOverlayState.ShowSkills && shiftHeld && wt.relevantSkills.Count == 0)
                return false; // Skip non-skill types on Shift-only.

            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || wt == null || pawn.WorkTypeIsDisabled(wt))
                return false;

            bool incapable = IsIncapableOfWholeWorkType(pawn, wt);

            float x = rect.x + ((rect.width - 25f) / 2f);
            float y = rect.y + 2.5f;
            Rect boxRect = new Rect(x, y, 25f, 25f);

            if (Event.current.type == EventType.Repaint)
                CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(x, y, pawn, wt, incapable);

            float avg = pawn.skills?.AverageOfRelevantSkillsFor(wt) ?? 0f;
            int level = Mathf.RoundToInt(avg);
            level = Mathf.Max(0, Mathf.Min(20, level));

            // Draw level with vanilla-style font/anchor/color (settings-based).
            var oldF = Text.Font; var oldA = Text.Anchor; var oldColor = GUI.color;
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);
            Widgets.Label(boxRect, level.ToString());

            GUI.color = oldColor; Text.Font = oldF; Text.Anchor = oldA;

            TooltipHandler.TipRegion(boxRect, () => WidgetsWork.TipForPawnWorker(pawn, wt, incapable), pawn.thingIDNumber ^ wt.GetHashCode());

            return false;
        }

        /// <summary>
        /// Private method to check if the pawn is incapable of the entire work type by examining all work givers' required capacities.
        /// Loops through workGiversByPriority; if any giver has all capacities met, returns false. Used to determine tint and skip drawing.
        /// </summary>
        /// <param name="p">The pawn to check.</param>
        /// <param name="work">The WorkTypeDef to evaluate.</param>
        /// <returns>True if incapable of all givers in the work type.</returns>
        private static bool IsIncapableOfWholeWorkType(Pawn p, WorkTypeDef work)
        {
            for (int i = 0; i < work.workGiversByPriority.Count; i++)
            {
                bool canDoThisGiver = true;
                var reqs = work.workGiversByPriority[i].requiredCapacities;
                for (int j = 0; j < reqs.Count; j++)
                {
                    if (!p.health.capacities.CapableOf(reqs[j]))
                    {
                        canDoThisGiver = false;
                        break;
                    }
                }
                if (canDoThisGiver) return false;
            }
            return true;
        }

        /// <summary>
        /// Private method to determine the color for a given skill level based on thresholds from settings.
        /// <=3: VeryLowSkill (red), <=9: LowSkill (orange), <=15: GoodLowSkill (white), >15: ExcellentSkill (green).
        /// </summary>
        /// <param name="level">The skill level (0-20).</param>
        /// <returns>The appropriate Color from BetterWorkTabSettings.</returns>
        private static Color ColorForSkillLevel(int level)
        {
            if (level <= 3) return BetterWorkTabMod.Settings.Color_VeryLowSkill;
            if (level <= 9) return BetterWorkTabMod.Settings.Color_LowSkill;
            if (level <= 15) return BetterWorkTabMod.Settings.Color_GoodLowSkill;
            return BetterWorkTabMod.Settings.Color_ExcellentSkill;
        }
    }

    /// <summary>
    /// Utility class for detecting the current keyboard shift key state to control conditional UI elements in the work tab.
    /// Used for features like showing small skill numbers or best-pawn highlights based on settings (e.g., only when shift is held).
    /// Provides a simple state check for Shif ted vs Unshifted modes without polling input repeatedly.
    /// </summary>
    public static class ShiftHelper
    {
        /// <summary>
        /// Read-only property returning the current ShowUIMode based on whether the shift key is held down.
        /// Returns ShowUIMode.Shifted if Event.current.shift is true, otherwise Unshifted.
        /// Used in patches like Patch_WorkPriority_DoCell_CornerNumber to determine visibility of overlays.
        /// </summary>
        public static BetterWorkTabSettings.ShowUIMode State
        {
            get => Event.current?.shift == true ? BetterWorkTabSettings.ShowUIMode.Shifted : BetterWorkTabSettings.ShowUIMode.Unshifted;
        }
    }

    /// <summary>
    /// Harmony postfix patch on PawnColumnWorker_WorkPriority.DoCell to add small skill numbers and corner highlights on top of vanilla priority cells.
    /// Draws conditionally based on settings (ShowUIMode for always, never, shifted, unshifted) and skill overlay state.
    /// Skips dead pawns, disabled work settings, or work types with no relevant skills. Calls private methods for best-pawn box and small numbers.
    /// Integrates with ShiftHelper for shift-based visibility and uses average skill calculation similar to the overlay patch.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_CornerNumber
    {
        /// <summary>
        /// Postfix method executed after vanilla DoCell to draw additional UI elements if conditions are met.
        /// Checks for best-pawn status and draws green outline box if the pawn has the highest average skill for the work type among cached pawns.
        /// Draws small skill numbers if enabled. Early return for invalid pawns or work types.
        /// </summary>
        /// <param name="__instance">The PawnColumnWorker_WorkPriority instance.</param>
        /// <param name="rect">The cell rectangle.</param>
        /// <param name="pawn">The pawn.</param>
        /// <param name="table">The PawnTable.</param>
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
        {
            var worktype = __instance.def.workType;

            if (pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork || worktype == null || pawn.WorkTypeIsDisabled(worktype))
                return;

            if ((ShiftHelper.State == BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare ||
                 BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare == BetterWorkTabSettings.ShowUIMode.Always) &&
                worktype.relevantSkills.Count != 0)
                DrawBestPawnForSkillBox(rect, pawn, table, __instance);

            if ((SkillOverlayState.ShowSkills ||
                 ShiftHelper.State == BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers ||
                 BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers == BetterWorkTabSettings.ShowUIMode.Always) &&
                worktype.relevantSkills.Count != 0)
                DrawSmallSkillNumbers(rect, pawn, worktype);
        }

        /// <summary>
        /// Private method to draw a green outline box around the work cell if this pawn has the highest average skill for the work type.
        /// Compares the pawn against all other cached pawns in the table using the column worker's Compare method. If superior, draws a 29x29 box with 3px outline.
        /// </summary>
        /// <param name="rect">The cell rectangle.</param>
        /// <param name="pawn">The pawn to check.</param>
        /// <param name="table">The PawnTable with cached pawns.</param>
        /// <param name="instance">The PawnColumnWorker_WorkPriority for comparison.</param>
        private static void DrawBestPawnForSkillBox(Rect rect, Pawn pawn, PawnTable table, PawnColumnWorker_WorkPriority instance)
        {
            foreach (var otherPawn in table.cachedPawns)
            {
                if (otherPawn == pawn) continue;
                if (instance.Compare(pawn, otherPawn) == -1)
                    return; // Better pawn found.
            }

            float x = rect.x + (rect.width - 25f) / 2f;
            float y = rect.y + 2.5f;
            Rect boxRect = new Rect(Mathf.FloorToInt(x) - 2, Mathf.FloorToInt(y) - 2, 29f, 29f);
            Widgets.DrawBoxSolidWithOutline(boxRect, Color.clear, BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare, 3);
        }

        /// <summary>
        /// Private method to draw small skill numbers in the corner of the work cell.
        /// Displays the average relevant skill level (0-20) in tiny font at bottom-right of the cell.
        /// </summary>
        /// <param name="rect">The cell rectangle.</param>
        /// <param name="pawn">The pawn.</param>
        /// <param name="worktype">The WorkTypeDef.</param>
        private static void DrawSmallSkillNumbers(Rect rect, Pawn pawn, WorkTypeDef worktype)
        {
            if (worktype.relevantSkills.Count == 0) return;
            float avgSkill = pawn.skills.AverageOfRelevantSkillsFor(worktype);
            int level = Mathf.RoundToInt(avgSkill);
            level = Mathf.Max(0, Mathf.Min(20, level));
            Rect numRect = new Rect(rect.xMax - 20f, rect.yMax - 16f, 20f, 16f);
            var oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = ColorForSkillLevel(level);
            Widgets.Label(numRect, level.ToString());
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;
        }

        /// <summary>
        /// Determines the color for small skill numbers based on level thresholds.
        /// Reuses the same color logic as the main overlay.
        /// </summary>
        /// <param name="level">The skill level.</param>
        /// <returns>The color from settings.</returns>
        private static Color ColorForSkillLevel(int level)
        {
            if (level <= 3) return BetterWorkTabMod.Settings.Color_VeryLowSkill;
            if (level <= 9) return BetterWorkTabMod.Settings.Color_LowSkill;
            if (level <= 15) return BetterWorkTabMod.Settings.Color_GoodLowSkill;
            return BetterWorkTabMod.Settings.Color_ExcellentSkill;
        }
   

        //Patching Window and filtering to MainTabWindow_Work because MainTabWindow_Work does not have a PreClose method to patch. This is the only way to do this.
        [HarmonyPatch(typeof(Window), nameof(Window.PreClose))]
        public static class MainTabWindow_Work_PreClose
        {
            public static void Postfix(Window __instance)
            {
                if (!(__instance is MainTabWindow_Work)) return;

                //Clear the highlighted worktype when closing the work tab. This makes it so the highlight only persists while the tab is open, and it will reset when closed.
                HighlightManager.ClearHighlight();
                Better_Work_Tab.Patches.WorkTabReorder_PostOpen.ResetAppliedOrderFlag();
            }
        }
    }
}
