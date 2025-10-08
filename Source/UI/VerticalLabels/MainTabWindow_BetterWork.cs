using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using HarmonyLib;
using RimWorld;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Custom Work tab window that replaces vanilla MainTabWindow_Work via XML patch.
    /// Provides full control over UI elements while preserving vanilla PawnTable functionality.
    /// 
    /// Key features:
    /// - Custom angled column headers (via cleaner Harmony postfix)
    /// - Auto-assign and workload management buttons in header
    /// - Skill overlay toggle integration
    /// - Drag-and-drop support (via separate patches)
    /// 
    /// This class inherits from MainTabWindow_Work to maintain compatibility with vanilla
    /// save files, colonist selection, and work priority systems.
    /// </summary>
    public class MainTabWindow_BetterWork : MainTabWindow_Work
    {
        /// <summary>
        /// Constants for positioning UI elements in the window header.
        /// </summary>
        private const float SkillToggleX = 150f;
        private const float SkillToggleY = 5f;
        private const float SkillToggleWidth = 230f;
        private const float SkillToggleHeight = 30f;

        private const float AutoAssignButtonWidth = 150f;
        private const float AutoAssignButtonHeight = 28f;
        private const float AutoAssignButtonMarginX = 6f;
        private const float AutoAssignButtonMarginY = 2f;

        private const float WorkloadButtonWidth = 150f;
        private const float WorkloadButtonHeight = 28f;
        private const float WorkloadButtonMarginX = AutoAssignButtonHeight + AutoAssignButtonWidth + AutoAssignButtonMarginX + 6f;
        private const float WorkloadButtonMarginY = 2f;

        private PawnTable PawnTable =>
            typeof(MainTabWindow_PawnTable)
               .GetField("table", BindingFlags.Instance | BindingFlags.NonPublic)?
               .GetValue(this) as PawnTable;

        /// <summary>
        /// Main window rendering method called every frame.
        /// Draws vanilla PawnTable first, then adds custom UI elements on top.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            // --- Draw the vanilla Work table first ---
            base.DoWindowContents(inRect);

            // --- Optional feature check ---
            if (!BetterWorkTabMod.Settings.enableSkillOverlayFeature)
                return;



            // --- Draw overlay toggles and buttons if not during layout ---
            if (Event.current == null || Event.current.type == EventType.Layout)
                return;

            DrawSkillToggle(inRect);
            DrawAutoAssignButtons(inRect);
            DrawWorkloadButtons(inRect);

            if (Event.current.type == EventType.Repaint && Mouse.IsOver(inRect))
            {
                // 24×24 px gear. 10 px margin from edges.
                const float siz = 24f;
                var gearRect = new Rect(
                    inRect.xMax - siz - 10f,
                    inRect.yMax - siz - 10f,
                    siz,
                    siz);
                if (Widgets.ButtonImage(gearRect, TexButton.Info))
                {
                    // open your mod’s settings dialog
                    var mod = LoadedModManager.GetMod<BetterWorkTabMod>();
                    if (mod != null)
                        Find.WindowStack.Add(new Dialog_ModSettings(mod));
                }
            }
        }
    


        /// <summary>
        /// Draws the skill overlay toggle checkbox in the top-left area.
        /// </summary>
        private void DrawSkillToggle(Rect inRect)
        {
            Rect toggleRect = new Rect(SkillToggleX, SkillToggleY, SkillToggleWidth, SkillToggleHeight);
            bool showSkills = Better_Work_Tab.Patches.SkillOverlayState.ShowSkills;

            Widgets.CheckboxLabeled(toggleRect, "Show skill levels (0–20)", ref showSkills);

            if (showSkills != Better_Work_Tab.Patches.SkillOverlayState.ShowSkills)
            {
                Better_Work_Tab.Patches.SkillOverlayState.ShowSkills = showSkills;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
        }

        /// <summary>
        /// Draws the auto-assign ruleset button and dropdown menu in the top-right area.
        /// </summary>
        private void DrawAutoAssignButtons(Rect inRect)
        {
            var size = new Vector2(AutoAssignButtonWidth, AutoAssignButtonHeight);
            var btn = new Rect(
                inRect.xMax - size.x - size.y - AutoAssignButtonMarginX,
                inRect.y + AutoAssignButtonMarginY,
                size.x,
                size.y
            );

            if (BetterWorkTabMod.Settings.CurrentAutoAssignRuleset == null)
            {
                Log.Error("[Better Work Tab] No ruleset selected.");
                return;
            }

            var curRuleset = BetterWorkTabMod.Settings.CurrentAutoAssignRuleset;

            // Main button - applies the current ruleset
            if (Widgets.ButtonText(btn, curRuleset.Name))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                if (curRuleset.ResetBeforeApplying)
                    Features.WorkAssignmentRuleset.SetAllToZero();
                curRuleset.ApplyAutoAssignments();
            }

            // Dropdown button - shows ruleset selection menu
            var btn2 = new Rect(btn.x + btn.width, btn.y, btn.height, btn.height);
            if (Widgets.ButtonText(btn2, "..."))
            {
                var options = BetterWorkTabMod.Settings.SavedRulesets.Select(ruleset =>
                    new FloatMenuOption(ruleset.Name, () => {
                        BetterWorkTabMod.Settings.CurrentAutoAssignRuleset = ruleset;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    })
                ).ToList();

                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        /// <summary>
        /// Draws the workload management button and dropdown menu in the top-right area.
        /// </summary>
        private void DrawWorkloadButtons(Rect inRect)
        {
            GameComponent_WorkloadSaver workloadSaver = Current.Game.GetComponent<GameComponent_WorkloadSaver>();

            var size = new Vector2(WorkloadButtonWidth, WorkloadButtonHeight);
            var btn = new Rect(
                inRect.xMax - size.x - size.y - WorkloadButtonMarginX,
                inRect.y + WorkloadButtonMarginY,
                size.x,
                size.y
            );

            // Main button - applies current workload or creates new
            string buttonLabel = workloadSaver.CurrentWorklist?.RenamableLabel ?? "New Workload";
            if (Widgets.ButtonText(btn, buttonLabel))
            {
                if (workloadSaver.CurrentWorklist != null)
                {
                    workloadSaver.CurrentWorklist.Apply();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
                else
                {
                    CreateNewWorkload(workloadSaver);
                }
            }

            // Dropdown button - shows workload management menu
            var btn2 = new Rect(btn.x + btn.width, btn.y, btn.height, btn.height);
            if (Widgets.ButtonText(btn2, "..."))
            {
                ShowWorkloadMenu(workloadSaver);
            }
        }

        /// <summary>
        /// Creates and opens the workload management FloatMenu.
        /// </summary>
        private void ShowWorkloadMenu(GameComponent_WorkloadSaver workloadSaver)
        {
            var options = new List<FloatMenuOption>();

            // List existing workloads (most recent first)
            var workloads = workloadSaver.SavedWorklists.ListFullCopy();
            workloads.Reverse();

            foreach (var workload in workloads)
            {
                options.Add(new FloatMenuOption(workload.RenamableLabel, () => {
                    workloadSaver.CurrentWorklist = workload;
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }));
            }

            // Add management options
            options.Add(new FloatMenuOption("New Workload", () => CreateNewWorkload(workloadSaver)));

            if (workloads.Any())
            {
                options.Add(new FloatMenuOption("Rename Workload", () => ShowRenameMenu(workloads)));
                options.Add(new FloatMenuOption("Delete Saved Workload", () => ShowDeleteMenu(workloadSaver, workloads)));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Shows a submenu for renaming workloads.
        /// </summary>
        private void ShowRenameMenu(List<Worklist> workloads)
        {
            var options = workloads.Select(w => new FloatMenuOption($"Rename {w.RenamableLabel}", () => {
                Find.WindowStack.Add(new Dialog_RenameWorkload(w));
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            })).ToList();

            var screenWidth = Verse.UI.screenWidth;
            var screenHeight = Verse.UI.screenHeight;
            var menuRect = new Rect(screenWidth / 2f - 100f, screenHeight / 2f - 75f, 200f, 150f);
            var menu = new FloatMenu(options) { windowRect = menuRect };

            Find.WindowStack.Add(menu);
        }

        /// <summary>
        /// Shows a submenu for deleting workloads.
        /// </summary>
        private void ShowDeleteMenu(GameComponent_WorkloadSaver workloadSaver, List<Worklist> workloads)
        {
            var options = workloads.Select(w => new FloatMenuOption($"Delete {w.RenamableLabel}", () => {
                int index = workloadSaver.SavedWorklists.IndexOf(w);
                workloadSaver.SavedWorklists.Remove(w);

                if (!workloadSaver.SavedWorklists.Any())
                {
                    workloadSaver.CurrentWorklist = null;
                }
                else
                {
                    int newIndex = Mathf.Max(0, Mathf.Min(index - 1, workloadSaver.SavedWorklists.Count - 1));
                    workloadSaver.CurrentWorklist = workloadSaver.SavedWorklists[newIndex];
                }

                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            })).ToList();

            var screenWidth = Verse.UI.screenWidth;
            var screenHeight = Verse.UI.screenHeight;
            var menuRect = new Rect(screenWidth / 2f - 100f, screenHeight / 2f - 75f, 200f, 150f);
            var menu = new FloatMenu(options) { windowRect = menuRect };

            Find.WindowStack.Add(menu);
        }

        /// <summary>
        /// Creates a new workload and opens the naming dialog.
        /// </summary>
        private void CreateNewWorkload(GameComponent_WorkloadSaver workloadSaver)
        {
            var newWorkload = new Worklist($"Custom Workload {workloadSaver.SavedWorklists.Count}");
            Find.WindowStack.Add(new Dialog_NameNewWorklist(newWorkload));
            workloadSaver.SavedWorklists.Add(newWorkload);
            workloadSaver.CurrentWorklist = newWorkload;
        }

        /// <summary>
        /// Called when window is closed. Cleanup highlights and state.
        /// </summary>
        public override void PreClose()
        {
            base.PreClose();
            HighlightManager.ClearHighlight();
            Better_Work_Tab.Patches.WorkTabReorder_PostOpen.ResetAppliedOrderFlag();
        }
    }
}