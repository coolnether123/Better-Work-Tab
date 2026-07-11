using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Mod_Support.Multiplayer;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
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
        private const float FluffyTopButtonSize = 30f;
        private const float FluffyTopButtonGap = 4f;

        public struct BottomButtonRects
        {
            public Rect RulesetMain;
            public Rect RulesetMenu;
            public Rect WorkloadMain;
            public Rect WorkloadMenu;
            public bool HasRuleset;
            public bool HasWorkload;

            public bool ContainsRuleset(Vector2 position)
            {
                return HasRuleset && (RulesetMain.Contains(position) || RulesetMenu.Contains(position));
            }

            public bool ContainsWorkload(Vector2 position)
            {
                return HasWorkload && (WorkloadMain.Contains(position) || WorkloadMenu.Contains(position));
            }
        }

        private struct TopButtonRects
        {
            public Rect Priority;
            public Rect Scheduler;
            public Rect Expand;
        }

        public static BottomButtonRects GetBottomButtonRects(Rect inRect, Rect gearRect)
        {
            BottomButtonRects rects = new BottomButtonRects();
            float y = inRect.yMax - AutoAssignButtonHeight - 10f;
            float xRight = gearRect.x - InterControlGap;

            var settings = BetterWorkTabMod.Settings;
            if (settings?.enableAutoAssignFeature ?? true)
            {
                rects.RulesetMenu = new Rect(xRight - AutoAssignButtonHeight, y, AutoAssignButtonHeight, AutoAssignButtonHeight);
                rects.RulesetMain = new Rect(rects.RulesetMenu.x - AutoAssignButtonWidth, y, AutoAssignButtonWidth, AutoAssignButtonHeight);
                rects.HasRuleset = true;
                xRight = rects.RulesetMain.x - 4f;
            }

            if (settings?.enableWorkloads ?? true)
            {
                var workloadSaver = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                if (workloadSaver != null)
                {
                    rects.WorkloadMenu = new Rect(xRight - WorkloadButtonHeight, y, WorkloadButtonHeight, WorkloadButtonHeight);
                    rects.WorkloadMain = new Rect(rects.WorkloadMenu.x - WorkloadButtonWidth, y, WorkloadButtonWidth, WorkloadButtonHeight);
                    rects.HasWorkload = true;
                }
            }

            return rects;
        }

        public static bool TryHandleTopRightFluffyStyleInput(
            IWorkTabLayoutController layout,
            Rect inRect,
            Event evt)
        {
            if (!ShouldShowTopRightFluffyStyle() ||
                evt == null ||
                evt.type != EventType.MouseDown ||
                evt.button != 0)
            {
                return false;
            }

            TopButtonRects rects = GetTopButtonRects(inRect);
            if (rects.Priority.Contains(evt.mousePosition))
            {
                ToggleManualPriorities(!Find.PlaySettings.useWorkPriorities);
                evt.Use();
                return true;
            }

            if (rects.Scheduler.Contains(evt.mousePosition))
            {
                if (!ToggleScheduler(layout))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }

                evt.Use();
                return true;
            }

            if (rects.Expand.Contains(evt.mousePosition))
            {
                ToggleAllVisibleSubWork(layout);
                evt.Use();
                return true;
            }

            return false;
        }

        public static void DrawTopRightFluffyStyle(IWorkTabLayoutController layout, Rect inRect)
        {
            if (!ShouldShowTopRightFluffyStyle())
            {
                return;
            }

            TopButtonRects rects = GetTopButtonRects(inRect);
            bool prioritiesEnabled = Find.PlaySettings.useWorkPriorities;
            if (DrawFluffyTopButton(
                    rects.Priority,
                    prioritiesEnabled ? FluffyWorkTabIcon.PrioritiesDetailed : FluffyWorkTabIcon.PrioritiesSimple,
                    prioritiesEnabled ? "Manual priorities" : "Simple priorities",
                    prioritiesEnabled ? "1" : "Y"))
            {
                ToggleManualPriorities(!prioritiesEnabled);
            }

            bool plannerVisible = FluffyTimeScheduleAssigner.IsOpen || TimePriorityScheduleEditor.IsVisible;
            if (DrawFluffyTopButton(
                    rects.Scheduler,
                    plannerVisible ? FluffyWorkTabIcon.PrioritiesTimed : FluffyWorkTabIcon.PrioritiesWholeDay,
                    plannerVisible ? "Close time priorities" : "Open time priorities",
                    plannerVisible ? "T" : "D"))
            {
                if (!ToggleScheduler(layout))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                }
            }

            bool anyExpanded = SubWorkDrilldownState.IsExpandBesideActive;
            if (DrawFluffyTopButton(
                    rects.Expand,
                    anyExpanded ? FluffyWorkTabIcon.Collapse : FluffyWorkTabIcon.Expand,
                    anyExpanded ? "Collapse all specific jobs" : "Expand all specific jobs",
                    anyExpanded ? "-" : "+"))
            {
                ToggleAllVisibleSubWork(layout);
            }
        }

        private static bool ToggleScheduler(IWorkTabLayoutController layout)
        {
            return FluffyTimeScheduleAssigner.IsEnabled
                ? FluffyTimeScheduleAssigner.Toggle()
                : TimePriorityScheduleEditor.ToggleFirstVisiblePrioritySchedule(layout);
        }

        public static float GetTopRightReservedWidth()
        {
            return ShouldShowTopRightFluffyStyle()
                ? (FluffyTopButtonSize * 3f) + (FluffyTopButtonGap * 2f) + InterControlGap
                : 0f;
        }

        private static bool ShouldShowTopRightFluffyStyle()
        {
            if (!FluffyWorkTabGateway.FluffyStyleFeaturesEnabled ||
                !FluffyWorkTabGateway.BetterWorkTabOwnsWorkTab)
            {
                return false;
            }

            return FluffyWorkTabGateway.IsPresent
                ? BetterWorkTabMod.Settings?.showFluffyStyleTopButtons ?? DefaultSettings.showFluffyStyleTopButtons
                : BetterWorkTabMod.Settings?.showStandaloneFluffyStyleTopButtons ??
                  DefaultSettings.showStandaloneFluffyStyleTopButtons;
        }

        private static TopButtonRects GetTopButtonRects(Rect inRect)
        {
            Rect priority = new Rect(
                inRect.xMax - FluffyTopButtonSize,
                inRect.yMin,
                FluffyTopButtonSize,
                FluffyTopButtonSize);

            Rect scheduler = priority;
            scheduler.x -= FluffyTopButtonSize + FluffyTopButtonGap;

            Rect expand = scheduler;
            expand.x -= FluffyTopButtonSize + FluffyTopButtonGap;

            return new TopButtonRects
            {
                Priority = priority,
                Scheduler = scheduler,
                Expand = expand
            };
        }

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

            var dotRect = new Rect(xRight - AutoAssignButtonHeight, y,
                AutoAssignButtonHeight, AutoAssignButtonHeight);
            var mainRect = new Rect(dotRect.x - AutoAssignButtonWidth, y,
                AutoAssignButtonWidth, AutoAssignButtonHeight);

            float newRight = mainRect.x - 4f;

            string btnLbl = RuleBuilderGateway.CurrentRulesetLabel();

            if (Widgets.ButtonText(mainRect, "  " + btnLbl,
                    overrideTextAnchor: TextAnchor.MiddleLeft))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                RuleBuilderGateway.ApplyCurrentRuleset();
            }

            if (Widgets.ButtonText(dotRect, "..."))
            {
                Find.WindowStack.Add(new FloatMenu(RuleBuilderGateway.BuildRulesetMenuOptions()));
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
                    System.Action applyAction = () =>
                    {
                        workloadSaver.CurrentWorklist.Apply();
                        MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    };

                    if (settings.warnOnApplyWorkload)
                    {
                        ConfirmApplyWithResetWarning("Apply workload?", applyAction, (val) =>
                        {
                            settings.warnOnApplyWorkload = !val;
                            settings.Write();
                        });
                    }
                    else
                    {
                        applyAction();
                    }
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

        private static bool DrawFluffyTopButton(
            Rect rect,
            FluffyWorkTabIcon icon,
            string tooltip,
            string fallbackLabel)
        {
            bool clicked;
            if (FluffyWorkTabGateway.TryGetIcon(icon, out Texture2D texture))
            {
                clicked = Widgets.ButtonImage(rect, texture, Color.white, GenUI.MouseoverColor);
            }
            else
            {
                TextAnchor previousAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                clicked = Widgets.ButtonText(rect, fallbackLabel);
                Text.Anchor = previousAnchor;
            }

            TooltipHandler.TipRegion(rect, tooltip);
            MouseoverSounds.DoRegion(rect);
            return clicked;
        }

        private static void ToggleManualPriorities(bool enabled)
        {
            if (Find.PlaySettings.useWorkPriorities == enabled)
            {
                return;
            }

            Find.PlaySettings.useWorkPriorities = enabled;
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                {
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                }
            }

            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void ToggleAllVisibleSubWork(IWorkTabLayoutController layout)
        {
            if (SubWorkDrilldownState.IsExpandBesideActive)
            {
                SubWorkDrilldownState.CollapseAllExpandBeside();
                HeaderDrawingCoordinator.InvalidateSolution();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                return;
            }

            if (layout?.Columns == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            bool expandedAny = false;
            HashSet<string> seenWorkTypes = new HashSet<string>();
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTypeDef workType = layout.Columns[i].Column?.workType;
                string defName = workType?.defName;
                if (defName.NullOrEmpty() ||
                    !seenWorkTypes.Add(defName) ||
                    WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType).Count == 0)
                {
                    continue;
                }

                SubWorkDrilldownState.ToggleExpandBeside(workType);
                expandedAny = true;
            }

            if (!expandedAny)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            HeaderDrawingCoordinator.InvalidateSolution();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
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
                    workloadSaver.CurrentWorklist = local;
                    MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
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
                                Find.WindowStack.Add(new Dialog_RenameWorkload(local));
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
                                workloadSaver.SavedWorklists.Remove(local);
                                if (workloadSaver.CurrentWorklist == local)
                                    workloadSaver.CurrentWorklist = null;

                                MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();

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
            // pick a simple unique default name
            int i = 1;
            string name;
            do
            {
                name = $"Workload {i++}";
            }
            while (workloadSaver.SavedWorklists.Any(w => w != null && w.RenamableLabel == name));

            var wl = new Worklist(name);
            workloadSaver.SavedWorklists.Add(wl);
            workloadSaver.CurrentWorklist = wl;

            // immediately prompt for a nicer name
            Find.WindowStack.Add(new Dialog_RenameWorkload(wl));
            MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
        }


        private static void ConfirmApplyWithResetWarning(string title, System.Action onConfirm, System.Action<bool> setDoNotShowAgain)
        {
            Find.WindowStack.Add(new Dialog_WarningWithCheckbox(
                "Applying this will reset the current work tab priority configuration. Continue?",
                title,
                onConfirm,
                setDoNotShowAgain));
        }
    }
}
