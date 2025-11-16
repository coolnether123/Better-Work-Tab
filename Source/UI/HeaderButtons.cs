using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.UI;
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
            var curRuleset = BetterWorkTabMod.Settings.CurrentRuleset;
            var dotRect = new Rect(xRight - AutoAssignButtonHeight, y,
                AutoAssignButtonHeight, AutoAssignButtonHeight);
            var mainRect = new Rect(dotRect.x - AutoAssignButtonWidth, y,
                AutoAssignButtonWidth, AutoAssignButtonHeight);

            float newRight = mainRect.x - 4f;

            string btnLbl = curRuleset != null ? curRuleset.Name : "No ruleset";

            if (Widgets.ButtonText(mainRect, "  " + btnLbl,
                    overrideTextAnchor: TextAnchor.MiddleLeft))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                if (curRuleset != null)
                {
                    if (curRuleset.ResetBeforeApplying)
                        WorkAssignmentRuleset.SetAllToZero();
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
                        BetterWorkTabMod.Settings.CurrentRuleset = local;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    }));
                }

                options.Add(new FloatMenuOption("Manage Rulesets...", () =>
                {
                    Find.WindowStack.Add(new Window_RulesManager());
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }));

                Find.WindowStack.Add(new FloatMenu(options));
            }

            return newRight;
        }

        private static float DrawWorkloadGroup(float xRight, float y)
        {
            var workloadSaver = Current.Game.GetComponent<GameComponent_BWTWorldSettings>();
            if (workloadSaver == null)
                workloadSaver = new GameComponent_BWTWorldSettings(Current.Game);

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
                    workloadSaver.CurrentWorklist.Apply();
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
            workloads.Reverse();

            foreach (var wl in workloads)
            {
                var local = wl;
                options.Add(new FloatMenuOption(local.RenamableLabel, () =>
                {
                    workloadSaver.CurrentWorklist = local;
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
                    foreach (var wl in workloads)
                    {
                        var local = wl;
                        ren.Add(new FloatMenuOption("Rename " + local.RenamableLabel,
                            () =>
                            {
                                Find.WindowStack.Add(new Dialog_RenameWorklist(local));
                                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                            }));
                    }
                    Find.WindowStack.Add(new FloatMenu(ren));
                }));

                options.Add(new FloatMenuOption("Delete Saved Workload", () =>
                {
                    var del = new List<FloatMenuOption>();
                    foreach (var wl in workloads)
                    {
                        var local = wl;
                        del.Add(new FloatMenuOption("Delete " + local.RenamableLabel,
                            () =>
                            {
                                var newCurrentIndex = Mathf.Clamp(
                                    workloadSaver.SavedWorklists.IndexOf(local) - 1, 0,
                                    int.MaxValue);
                                workloadSaver.SavedWorklists.Remove(local);
                                if (!workloadSaver.SavedWorklists.Any())
                                    workloadSaver.CurrentWorklist = null;
                                else
                                    workloadSaver.CurrentWorklist =
                                        workloadSaver.SavedWorklists[newCurrentIndex];
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
            var newWorkload = new Worklist($"Custom Workload {workloadSaver.SavedWorklists.Count}");
            Find.WindowStack.Add(new Dialog_NameNewWorklist(newWorkload));
            workloadSaver.SavedWorklists.Add(newWorkload);
            workloadSaver.CurrentWorklist = newWorkload;
        }
    }
}
