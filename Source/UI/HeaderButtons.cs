using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
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
        private const float SelectorHeight = BWTBottomBarSelector.Height;
        private const float SelectorMenuWidth = BWTBottomBarSelector.MenuWidth;
        private const float GroupGap = 6f;
        private const float InterControlGap = 6f;
        private const float FeedbackButtonSize = 24f;
        private const float FluffyTopButtonSize = 30f;
        private const float FluffyTopButtonGap = 4f;

        public struct BottomButtonRects
        {
            public Rect RulesetMain;
            public Rect RulesetMenu;
            public Rect WorkloadMain;
            public Rect WorkloadMenu;
            public Rect Feedback;
            public bool HasRuleset;
            public bool HasWorkload;

            /// <summary>
            /// The left edge of everything in the row, so the footer hint text
            /// knows where it has to stop.
            /// </summary>
            public float LeftEdge;

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

        /// <summary>
        /// The single description of the bottom-right row. Both the painter and
        /// every hit test read it, so a control cannot be drawn in one place and
        /// clicked in another.
        /// </summary>
        public static BottomButtonRects GetBottomButtonRects(Rect inRect, Rect gearRect)
        {
            BottomButtonRects rects = new BottomButtonRects();

            // The row sits on the settings icon's baseline rather than working
            // out its own distance from the bottom edge. Two independent
            // calculations of "just above the bottom" drift the moment either
            // one's padding changes; deriving from the icon means the row cannot
            // end up on a different line from it.
            float y = gearRect.yMax - SelectorHeight;
            float xRight = gearRect.x - InterControlGap;

            var settings = BetterWorkTabMod.Settings;
            if (settings?.enableAutoAssignFeature ?? true)
            {
                float width = BWTBottomBarSelector.MeasureWidth(RuleBuilderGateway.CurrentRulesetLabel());
                rects.RulesetMenu = new Rect(xRight - SelectorMenuWidth, y, SelectorMenuWidth, SelectorHeight);
                rects.RulesetMain = new Rect(rects.RulesetMenu.x - width, y, width, SelectorHeight);
                rects.HasRuleset = true;
                xRight = rects.RulesetMain.x - GroupGap;
            }

            if (settings?.enableWorkloads ?? true)
            {
                var workloadSaver = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
                if (workloadSaver != null)
                {
                    float width = BWTBottomBarSelector.MeasureWidth(WorkloadLabel(workloadSaver));
                    rects.WorkloadMenu = new Rect(xRight - SelectorMenuWidth, y, SelectorMenuWidth, SelectorHeight);
                    rects.WorkloadMain = new Rect(rects.WorkloadMenu.x - width, y, width, SelectorHeight);
                    rects.HasWorkload = true;
                    xRight = rects.WorkloadMain.x - GroupGap;
                }
            }

            // The beta feedback button anchors to the left end of the row rather
            // than to the settings icon. Sitting between the icon and the pickers
            // made it read as another settings affordance; out here it reads as
            // its own thing, and it keeps its place when a picker is switched off.
            rects.Feedback = new Rect(
                xRight - FeedbackButtonSize,
                y + ((SelectorHeight - FeedbackButtonSize) * 0.5f),
                FeedbackButtonSize,
                FeedbackButtonSize);
            rects.LeftEdge = rects.Feedback.x;
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
            BottomButtonRects rects = GetBottomButtonRects(inRect, gearRect);
            if (rects.HasRuleset)
            {
                DrawAutoAssignGroup(rects);
            }

            if (rects.HasWorkload)
            {
                DrawWorkloadGroup(rects);
            }

            BWTBetaFeedbackButton.Draw(rects.Feedback);
        }

        private static void DrawAutoAssignGroup(BottomButtonRects rects)
        {
            string name = RuleBuilderGateway.CurrentRulesetLabel();
            bool hasRuleset = RuleBuilderGateway.HasCurrentRuleset();

            if (BWTBottomBarSelector.DrawMain(
                    rects.RulesetMain,
                    BWTBottomBarIcons.Ruleset,
                    name,
                    hasRuleset,
                    hasRuleset
                        ? "BWT_BottomBar_RulesetTooltip".Translate(name)
                        : "BWT_BottomBar_RulesetTooltipEmpty".Translate()))
            {
                if (hasRuleset)
                {
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                    RuleBuilderGateway.ApplyCurrentRuleset();
                }
                else
                {
                    // Applying nothing looked like a broken button. With no
                    // ruleset to apply, the useful thing is the list.
                    Find.WindowStack.Add(new FloatMenu(RuleBuilderGateway.BuildRulesetMenuOptions()));
                }
            }

            if (BWTBottomBarSelector.DrawMenu(rects.RulesetMenu, "BWT_BottomBar_RulesetMenuTooltip".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(RuleBuilderGateway.BuildRulesetMenuOptions()));
            }
        }

        /// <summary>
        /// The name the workload picker shows. Read by the geometry pass as well
        /// as the painter, so the button is always sized for the text it draws.
        /// </summary>
        private static string WorkloadLabel(GameComponent_BWTWorldSettings workloadSaver)
        {
            return workloadSaver?.CurrentWorklist != null
                ? workloadSaver.CurrentWorklist.RenamableLabel
                : "BWT_BottomBar_WorkloadEmpty".Translate().ToString();
        }

        private static void DrawWorkloadGroup(BottomButtonRects rects)
        {
            var settings = BetterWorkTabMod.Settings;
            var workloadSaver = Current.Game?.GetComponent<GameComponent_BWTWorldSettings>();
            if (workloadSaver == null)
            {
                return;
            }

            bool hasWorkload = workloadSaver.CurrentWorklist != null;
            string name = WorkloadLabel(workloadSaver);

            if (BWTBottomBarSelector.DrawMain(
                    rects.WorkloadMain,
                    BWTBottomBarIcons.Workload,
                    name,
                    hasWorkload,
                    hasWorkload
                        ? "BWT_BottomBar_WorkloadTooltip".Translate(name)
                        : "BWT_BottomBar_WorkloadTooltipEmpty".Translate()))
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

            if (BWTBottomBarSelector.DrawMenu(rects.WorkloadMenu, "BWT_BottomBar_WorkloadMenuTooltip".Translate()))
            {
                ShowWorkloadMenu(workloadSaver);
            }
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

            WorkPrioritySystem.SetManualPriorities(enabled);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void ToggleAllVisibleSubWork(IWorkTabLayoutController layout)
        {
            if (SubWorkDrilldownState.IsExpandBesideActive)
            {
                SubWorkDrilldownState.CollapseAllExpandBeside();
                WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.Columns | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
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

            WorkGrid.Invalidation.WorkTabInvalidationHub.Invalidate(WorkGrid.Contracts.WorkTabDirtyFlags.Columns | WorkGrid.Contracts.WorkTabDirtyFlags.HeaderGeometry);
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
