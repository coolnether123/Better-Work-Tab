using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.RuleBuilder;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI
{
    // Single-purpose file: draw grouped header buttons anchored to the
    // bottom-right (immediately left of the small info/gear button).
    public static class HeaderButtons
    {
        private const float AutoAssignButtonWidth = 150f;
        private const float AutoAssignButtonHeight = 28f;
        private const float WorkloadButtonWidth = 150f;
        private const float WorkloadButtonHeight = 28f;
        private const float InterControlGap = 6f;

        // Public entry point called by the window.
        public static void DrawBottomRightGrouped(Rect inRect, Rect gearRect)
        {
            // vertical position for the buttons (bottom anchored)
            float y = inRect.yMax - AutoAssignButtonHeight - 10f;
            // start anchor: immediate left of the gear
            float xRight = gearRect.x - InterControlGap;

            // Auto-assign group (closest to gear)
            xRight = DrawAutoAssignGroup(inRect, xRight, y);

            // Workload group to the left of the Auto-assign group
            xRight = DrawWorkloadGroup(xRight, y);
        }

        private static float DrawAutoAssignGroup(Rect inRect, float xRight, float y)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableAutoAssignFeature ?? true))
                return xRight;

            var curRuleset = BetterWorkTabMod.Settings.CurrentRuleset;
            var dotRect = new Rect(xRight - AutoAssignButtonHeight, y,
                AutoAssignButtonHeight, AutoAssignButtonHeight);
            var mainRect = new Rect(dotRect.x - AutoAssignButtonWidth, y,
                AutoAssignButtonWidth, AutoAssignButtonHeight);

            float newRight = mainRect.x - 4f;

            string btnLbl = curRuleset != null ? curRuleset.Name : "BWT_NoRuleset".Translate();

            if (Widgets.ButtonText(mainRect, "  " + btnLbl,
                    overrideTextAnchor: TextAnchor.MiddleLeft))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                // Rulesets are now local-only (not synced in multiplayer)
                if (curRuleset != null)
                {
                    if (curRuleset.ResetBeforeApplying)
                    {
                        WorkAssignmentRuleset.SetAllToZero();
                    }
                    curRuleset.ApplyAutoAssignments();
                }
            }

            if (Widgets.ButtonText(dotRect, "..."))
            {
                var options = new List<FloatMenuOption>();
                foreach (var ruleset in BetterWorkTabMod.Settings.SavedRulesets)
                {
                    var local = ruleset;
                    options.Add(new FloatMenuOption(local.Name, () =>
                    {
                        // Rulesets are now local-only (not synced in multiplayer)
                        BetterWorkTabMod.Settings.CurrentRuleset = local;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }));
                }

                AddRulesetManagementOptions(options);

                Find.WindowStack.Add(new FloatMenu(options));
            }

            return newRight;
        }

        private static float DrawWorkloadGroup(float xRight, float y)
        {
            var settings = BetterWorkTabMod.Settings;
            if (!(settings?.enableWorkloads ?? true))
                return xRight;

            var workloadSaver = Current.Game.GetComponent<GameComponent_BWTWorldSettings>();
            if (workloadSaver == null)
                return xRight;

            var dotRect = new Rect(xRight - WorkloadButtonHeight, y,
                WorkloadButtonHeight, WorkloadButtonHeight);
            var mainRect = new Rect(dotRect.x - WorkloadButtonWidth, y,
                WorkloadButtonWidth, WorkloadButtonHeight);

            float newRight = mainRect.x - 4f;

            string buttonLabel = workloadSaver.CurrentWorklist?.RenamableLabel
                ?? "New Workload";

            if (Widgets.ButtonText(mainRect, "  " + buttonLabel,
                    overrideTextAnchor: TextAnchor.MiddleLeft))
            {
                if (workloadSaver.CurrentWorklist != null)
                {
                    BetterWorkTabMultiplayer.RequestApplyWorklist(workloadSaver, workloadSaver.CurrentWorklist);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
                else
                {
                    CreateNewWorkload(workloadSaver);
                }
            }

            if (Widgets.ButtonText(dotRect, "..."))
                ShowWorkloadMenu(workloadSaver);

            return newRight;
        }

        private static void ShowWorkloadMenu(GameComponent_BWTWorldSettings workloadSaver)
        {
            var options = new List<FloatMenuOption>();
            var workloads = workloadSaver.SavedWorklists.ListFullCopy();

            foreach (var wl in workloads)
            {
                var local = wl;
                options.Add(new FloatMenuOption(local.RenamableLabel, () =>
                {
                    // This logic is now reliable because the list order matches.
                    BetterWorkTabMultiplayer.RequestWorklistSelection(workloadSaver, local);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }));
            }

            options.Add(new FloatMenuOption("New Workload", () =>
            {
                CreateNewWorkload(workloadSaver);
            }));

            if (workloads.Any())
            {
                options.Add(new FloatMenuOption("Rename Workload", () =>
                {
                    var ren = new List<FloatMenuOption>();
                    // This loop now uses the correct, non-reversed list.
                    foreach (var wl in workloads)
                    {
                        var local = wl;
                        ren.Add(new FloatMenuOption("Rename " + local.RenamableLabel,
                            () =>
                            {
                                BetterWorkTabMultiplayer.RequestRenameWorklist(workloadSaver, local, local.RenamableLabel);
                                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                            }));
                    }
                    Find.WindowStack.Add(new FloatMenu(ren));
                }));

                options.Add(new FloatMenuOption("Delete Saved Workload", () =>
                {
                    var del = new List<FloatMenuOption>();
                    // This loop also now uses the correct, non-reversed list.
                    foreach (var wl in workloads)
                    {
                        var local = wl;
                        del.Add(new FloatMenuOption("Delete " + local.RenamableLabel,
                            () =>
                            {
                                BetterWorkTabMultiplayer.RequestDeleteWorklist(workloadSaver, local);
                                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                            }));
                    }
                    Find.WindowStack.Add(new FloatMenu(del));
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void CreateNewWorkload(GameComponent_BWTWorldSettings workloadSaver)
        {
            BetterWorkTabMultiplayer.RequestCreateWorklist(workloadSaver);
        }

        /// <summary>
        /// Adds the standard ruleset management options to the provided menu.
        /// Keeps labels and behaviors consistent across entry points.
        /// </summary>
        private static void AddRulesetManagementOptions(List<FloatMenuOption> options)
        {
            options.Add(new FloatMenuOption("BWT_RuleBuilder_OpenBuilder".Translate(), () =>
            {
                Find.WindowStack.Add(new Window_RulesetBuilder());
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }));

            options.Add(new FloatMenuOption("BWT_RuleBuilder_ManageRulesetsClassic".Translate(), () =>
            {
                Find.WindowStack.Add(new Window_RulesManager());
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }));
        }
    }
}
