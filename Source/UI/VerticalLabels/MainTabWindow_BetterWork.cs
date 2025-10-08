using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
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

        // Button sizing (unchanged)
        private const float AutoAssignButtonWidth = 150f;
        private const float AutoAssignButtonHeight = 28f;

        private const float WorkloadButtonWidth = 150f;
        private const float WorkloadButtonHeight = 28f;

        // Bottom-right anchoring next to the info button
        private const float BottomEdgeMargin = 10f; // distance from bottom edge
        private const float RightEdgeMargin = 10f;  // distance from right edge
        private const float InterControlGap = 6f;   // gap between buttons
        private const float InfoIconSize = 24f;     // same size as TexButton.Info

        public override Vector2 InitialSize
        {
            get
            {
                Vector2 size = base.InitialSize;
                size.x += 25f;
                return size;
            }
        }

        /// <summary>
        /// Main window rendering method called every frame.
        /// Draws vanilla PawnTable first, then adds custom UI elements on top.
        /// </summary>
        public override void DoWindowContents(Rect inRect)
        {
            // --- Draw the vanilla Work table first ---
            base.DoWindowContents(inRect);

            // --- Draw overlay toggles and buttons if not during layout ---
            if (Event.current == null || Event.current.type == EventType.Layout)
                return;

            // Calculate the settings/info icon rect in the bottom-right,
            // then lay out our buttons immediately to its left.
            var gearRect = GetInfoIconRect(inRect);

            // Draw our two button groups anchored to the right,
            // immediately to the left of the info icon (from right->left).
            DrawBottomRightButtons(inRect, gearRect);

            // Draw the info/settings button (gear) on Repaint so it's above the table.
            if (Event.current.type == EventType.Repaint && Mouse.IsOver(inRect))
            {
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
        /// Returns the rectangle for the small "info/settings" button in the bottom-right.
        /// 24×24 px gear. 10 px margin from edges.
        /// </summary>
        private static Rect GetInfoIconRect(Rect inRect)
        {
            return new Rect(
                inRect.xMax - InfoIconSize - RightEdgeMargin,
                inRect.yMax - InfoIconSize - BottomEdgeMargin,
                InfoIconSize,
                InfoIconSize
            );
        }

        /// <summary>
        /// Draws the workload and auto-assign buttons at the bottom-right,
        /// immediately to the left of the info icon. Anchored to the right.
        /// Order (left-to-right on screen): [Workload][...][Auto-assign][...][Info].
        /// </summary>
        private void DrawBottomRightButtons(Rect inRect, Rect gearRect)
        {
            float y = inRect.yMax - AutoAssignButtonHeight - BottomEdgeMargin;

            // Start laying out from the right, immediately to the left of the gear icon.
            float xRight = gearRect.x - InterControlGap;

            // 1) Auto-assign ruleset (right group, closest to gear)
            xRight = DrawAutoAssignButtons_AnchoredRight(xRight, y);

            // Small gap between groups
            xRight -= InterControlGap;

            // 2) Workload buttons (left group)
            xRight = DrawWorkloadButtons_AnchoredRight(xRight, y);
        }

        /// <summary>
        /// Draws the auto-assign ruleset main button and its "..." dropdown,
        /// positioned using the right edge anchor. Returns the new right anchor
        /// after placing both controls (so the caller can continue placing more to the left).
        /// </summary>
        private float DrawAutoAssignButtons_AnchoredRight(float xRight, float y)
        {
            if (BetterWorkTabMod.Settings.CurrentAutoAssignRuleset == null)
            {
                Log.Error("[Better Work Tab] No ruleset selected.");
                return xRight;
            }

            var curRuleset = BetterWorkTabMod.Settings.CurrentAutoAssignRuleset;

            // Place the small "..." button flush to the current right anchor.
            var dotRect = new Rect(
                xRight - AutoAssignButtonHeight,
                y,
                AutoAssignButtonHeight,
                AutoAssignButtonHeight
            );
            xRight = dotRect.x - InterControlGap;

            // Place the main text button immediately to the left of the "..." button.
            var mainRect = new Rect(
                xRight - AutoAssignButtonWidth,
                y,
                AutoAssignButtonWidth,
                AutoAssignButtonHeight
            );
            xRight = mainRect.x; // update anchor for next group (to the left)

            // Main button - applies the current ruleset
            if (Widgets.ButtonText(mainRect, curRuleset.Name))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                if (curRuleset.ResetBeforeApplying)
                    Features.WorkAssignmentRuleset.SetAllToZero();
                curRuleset.ApplyAutoAssignments();
            }

            // Dropdown button - shows ruleset selection menu
            if (Widgets.ButtonText(dotRect, "..."))
            {
                var options = BetterWorkTabMod.Settings.SavedRulesets
                    .Select(ruleset =>
                        new FloatMenuOption(
                            ruleset.Name,
                            () =>
                            {
                                BetterWorkTabMod.Settings.CurrentAutoAssignRuleset = ruleset;
                                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                            }
                        )
                    )
                    .ToList();

                Find.WindowStack.Add(new FloatMenu(options));
            }

            return xRight;
        }

        /// <summary>
        /// Draws the workload main button and its "..." dropdown,
        /// positioned using the right edge anchor. Returns the new right anchor
        /// after placing both controls (so the caller can continue placing more to the left).
        /// </summary>
        private float DrawWorkloadButtons_AnchoredRight(float xRight, float y)
        {
            GameComponent_WorkloadSaver workloadSaver =
                Current.Game.GetComponent<GameComponent_WorkloadSaver>();

            // Place the small "..." button flush to the current right anchor.
            var dotRect = new Rect(
                xRight - WorkloadButtonHeight,
                y,
                WorkloadButtonHeight,
                WorkloadButtonHeight
            );
            xRight = dotRect.x - InterControlGap;

            // Place the main text button immediately to the left of the "..." button.
            var mainRect = new Rect(
                xRight - WorkloadButtonWidth,
                y,
                WorkloadButtonWidth,
                WorkloadButtonHeight
            );
            xRight = mainRect.x; // update anchor for next group (to the left)

            // Main button - applies current workload or creates new
            string buttonLabel =
                workloadSaver.CurrentWorklist?.RenamableLabel ?? "New Workload";
            if (Widgets.ButtonText(mainRect, buttonLabel))
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
            if (Widgets.ButtonText(dotRect, "..."))
            {
                ShowWorkloadMenu(workloadSaver);
            }

            return xRight;
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
                options.Add(
                    new FloatMenuOption(
                        workload.RenamableLabel,
                        () =>
                        {
                            workloadSaver.CurrentWorklist = workload;
                            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        }
                    )
                );
            }

            // Add management options
            options.Add(
                new FloatMenuOption("New Workload", () => CreateNewWorkload(workloadSaver))
            );

            if (workloads.Any())
            {
                options.Add(
                    new FloatMenuOption(
                        "Rename Workload",
                        () => ShowRenameMenu(workloads)
                    )
                );
                options.Add(
                    new FloatMenuOption(
                        "Delete Saved Workload",
                        () => ShowDeleteMenu(workloadSaver, workloads)
                    )
                );
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Shows a submenu for renaming workloads.
        /// </summary>
        private void ShowRenameMenu(List<Worklist> workloads)
        {
            var options = workloads
                .Select(
                    w =>
                        new FloatMenuOption(
                            $"Rename {w.RenamableLabel}",
                            () =>
                            {
                                Find.WindowStack.Add(new Dialog_RenameWorkload(w));
                                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                            }
                        )
                )
                .ToList();

            var screenWidth = Verse.UI.screenWidth;
            var screenHeight = Verse.UI.screenHeight;
            var menuRect = new Rect(
                screenWidth / 2f - 100f,
                screenHeight / 2f - 75f,
                200f,
                150f
            );
            var menu = new FloatMenu(options) { windowRect = menuRect };

            Find.WindowStack.Add(menu);
        }

        /// <summary>
        /// Shows a submenu for deleting workloads.
        /// </summary>
        private void ShowDeleteMenu(
            GameComponent_WorkloadSaver workloadSaver,
            List<Worklist> workloads
        )
        {
            var options = workloads
                .Select(
                    w =>
                        new FloatMenuOption(
                            $"Delete {w.RenamableLabel}",
                            () =>
                            {
                                int index = workloadSaver.SavedWorklists.IndexOf(w);
                                workloadSaver.SavedWorklists.Remove(w);

                                if (!workloadSaver.SavedWorklists.Any())
                                {
                                    workloadSaver.CurrentWorklist = null;
                                }
                                else
                                {
                                    int newIndex = Mathf.Max(
                                        0,
                                        Mathf.Min(
                                            index - 1,
                                            workloadSaver.SavedWorklists.Count - 1
                                        )
                                    );
                                    workloadSaver.CurrentWorklist =
                                        workloadSaver.SavedWorklists[newIndex];
                                }

                                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                            }
                        )
                )
                .ToList();

            var screenWidth = Verse.UI.screenWidth;
            var screenHeight = Verse.UI.screenHeight;
            var menuRect = new Rect(
                screenWidth / 2f - 100f,
                screenHeight / 2f - 75f,
                200f,
                150f
            );
            var menu = new FloatMenu(options) { windowRect = menuRect };

            Find.WindowStack.Add(menu);
        }

        /// <summary>
        /// Creates a new workload and opens the naming dialog.
        /// </summary>
        private void CreateNewWorkload(GameComponent_WorkloadSaver workloadSaver)
        {
            var newWorkload = new Worklist(
                $"Custom Workload {workloadSaver.SavedWorklists.Count}"
            );
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