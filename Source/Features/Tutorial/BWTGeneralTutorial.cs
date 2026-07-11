using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    internal enum BWTGeneralTutorialStep
    {
        Welcome = 0,
        ManualPriorities = 10,
        ShiftSkills = 20,
        PawnMenu = 30,
        Dividers = 40,
        PawnTitleAndColor = 50,
        Workloads = 60,
        Rulesets = 70,
        DragDrop = 80,
        GroupDrag = 90,
        ContextSettings = 100,
        SettingsShortcut = 105,
        SubWorkJobs = 110,
        SubWorkLeft = 115,
        TimePriorities = 120,
        TimePrioritiesClosed = 125,
        MaxPriorities = 130,
        Complete = 140,
        Completed = 1000
    }

    /// <summary>
    /// Full Better Work Tab walkthrough. It explains BWT systems without owning
    /// the systems themselves; feature code remains the source of behavior.
    /// </summary>
    internal static class BWTGeneralTutorial
    {
        private static readonly TutorialOverlayController Overlay = new TutorialOverlayController();
        private static bool wasSubWorkActive;
        private static bool wasTimePriorityVisible;

        internal static bool IsActive
        {
            get
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return settings != null &&
                    settings.showGeneralTutorial &&
                    NormalizeStep(settings) != BWTGeneralTutorialStep.Completed;
            }
        }

        internal static bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showGeneralTutorial || evt == null)
            {
                return false;
            }

            BWTGeneralTutorialStep step = NormalizeStep(settings);
            if (step == BWTGeneralTutorialStep.Completed)
            {
                return false;
            }

            TutorialOverlayContent content = BuildContent(step);
            List<Rect> focusRects = BuildFocusRects(step, inRect, layout);
            return Overlay.TryHandleInput(
                inRect,
                content,
                focusRects,
                evt,
                () => AdvanceByButton(step),
                () => HandleSecondaryButton(step, inRect, layout, BuildFocusRects(step, inRect, layout)),
                () => HandleTertiaryButton(step),
                Deactivate);
        }

        internal static bool TryHandleAcceptKey()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showGeneralTutorial)
            {
                return false;
            }

            BWTGeneralTutorialStep step = NormalizeStep(settings);
            if (step == BWTGeneralTutorialStep.Completed)
            {
                return false;
            }

            return Overlay.TryHandleAcceptKey(BuildContent(step), () => AdvanceByButton(step));
        }

        internal static void TickAndDraw(Rect inRect, IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showGeneralTutorial)
            {
                return;
            }

            BWTGeneralTutorialStep step = NormalizeStep(settings);
            if (step == BWTGeneralTutorialStep.Completed)
            {
                settings.showGeneralTutorial = false;
                settings.Write();
                return;
            }

            List<Rect> focusRects = BuildFocusRects(step, inRect, layout);
            Overlay.Draw(
                inRect,
                BuildContent(step),
                focusRects,
                BuildShortcutHints(step, focusRects.Count),
                () => AdvanceByButton(step),
                () => HandleSecondaryButton(step, inRect, layout, focusRects),
                () => HandleTertiaryButton(step),
                Deactivate);
        }

        internal static void ObserveWorkTabState()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            BWTGeneralTutorialStep currentStep = NormalizeStep(settings);
            if (currentStep == BWTGeneralTutorialStep.Welcome)
            {
                if (BWTTutorialUserContext.HasExternalWorkTabPriorityHistory())
                {
                    BWTWorkTabTutorial.Start2TutorialAt(BWTBetaTutorialStep.SubWorkPrompt);
                    return;
                }

                if (BWTTutorialUserContext.HasExternalPriorityProvider() &&
                    BWTTutorialUserContext.PriorityModeIsNotBetterWorkTab())
                {
                    SetStep(BWTGeneralTutorialStep.MaxPriorities);
                    return;
                }
            }

            if (currentStep == BWTGeneralTutorialStep.ShiftSkills &&
                (Event.current?.shift ?? false))
            {
                SetStep(BWTGeneralTutorialStep.PawnMenu);
                return;
            }

            BWTGeneralTutorialStep? pivotStep = null;

            bool isSubWorkActive = SubWorkDrilldownState.IsActive;
            if (isSubWorkActive && !wasSubWorkActive)
            {
                pivotStep = BWTGeneralTutorialStep.SubWorkJobs;
            }
            else if (!isSubWorkActive && wasSubWorkActive)
            {
                pivotStep = BWTGeneralTutorialStep.SubWorkLeft;
            }

            bool isTimePriorityVisible = TimePriorityScheduleEditor.IsVisible;
            if (isTimePriorityVisible && !wasTimePriorityVisible)
            {
                pivotStep = BWTGeneralTutorialStep.TimePriorities;
            }
            else if (!isTimePriorityVisible && wasTimePriorityVisible && !pivotStep.HasValue)
            {
                pivotStep = BWTGeneralTutorialStep.TimePrioritiesClosed;
            }

            wasSubWorkActive = isSubWorkActive;
            wasTimePriorityVisible = isTimePriorityVisible;

            if (pivotStep.HasValue)
            {
                SetStep(pivotStep.Value);
            }
        }

        internal static void ObserveInteraction(BWTTutorialInteraction interaction)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings != null)
            {
                BWTGeneralTutorialStep currentStep = NormalizeStep(settings);
                if (TryAdvanceFromExpectedInteraction(currentStep, interaction))
                {
                    return;
                }
            }

            BWTGeneralTutorialStep? step = GetStepForInteraction(interaction);
            if (step.HasValue)
            {
                SetStep(step.Value);
            }
        }

        internal static bool TryActivateForInteraction(BWTTutorialInteraction interaction)
        {
            BWTGeneralTutorialStep? step = GetStepForInteraction(interaction);
            if (!step.HasValue)
            {
                return false;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return false;
            }

            settings.showGeneralTutorial = true;
            settings.generalTutorialStep = (int)step.Value;
            settings.Write();
            return true;
        }

        private static BWTGeneralTutorialStep? GetStepForInteraction(BWTTutorialInteraction interaction)
        {
            switch (interaction.Kind)
            {
                case BWTTutorialInteractionKind.ManualPriorities:
                case BWTTutorialInteractionKind.PriorityLegend:
                    return BWTGeneralTutorialStep.ManualPriorities;
                case BWTTutorialInteractionKind.PriorityCell:
                    if (interaction.Control)
                    {
                        return BWTGeneralTutorialStep.TimePriorities;
                    }

                    return BWTGeneralTutorialStep.ShiftSkills;
                case BWTTutorialInteractionKind.PawnRow:
                    return BWTGeneralTutorialStep.PawnMenu;
                case BWTTutorialInteractionKind.Divider:
                    return BWTGeneralTutorialStep.Dividers;
                case BWTTutorialInteractionKind.WorkloadButton:
                    return BWTGeneralTutorialStep.Workloads;
                case BWTTutorialInteractionKind.RulesetButton:
                    return BWTGeneralTutorialStep.Rulesets;
                case BWTTutorialInteractionKind.WorkHeader:
                    if (interaction.Control)
                    {
                        return BWTGeneralTutorialStep.SubWorkJobs;
                    }

                    if (interaction.Shift)
                    {
                        return BWTGeneralTutorialStep.GroupDrag;
                    }

                    return BWTGeneralTutorialStep.DragDrop;
                case BWTTutorialInteractionKind.SubWorkHeader:
                    return BWTGeneralTutorialStep.SubWorkJobs;
                case BWTTutorialInteractionKind.TimePriorityCell:
                    return BWTGeneralTutorialStep.TimePriorities;
                case BWTTutorialInteractionKind.InfoButton:
                case BWTTutorialInteractionKind.ContextSettingsHint:
                    return BWTGeneralTutorialStep.SettingsShortcut;
                default:
                    return null;
            }
        }

        private static BWTGeneralTutorialStep NormalizeStep(BetterWorkTabSettings settings)
        {
            int rawStep = settings.generalTutorialStep;
            if (!Enum.IsDefined(typeof(BWTGeneralTutorialStep), rawStep) ||
                rawStep >= (int)BWTGeneralTutorialStep.Completed)
            {
                rawStep = (int)BWTGeneralTutorialStep.Welcome;
                settings.generalTutorialStep = rawStep;
            }

            return (BWTGeneralTutorialStep)rawStep;
        }

        private static TutorialOverlayContent BuildContent(BWTGeneralTutorialStep step)
        {
            switch (step)
            {
                case BWTGeneralTutorialStep.Welcome:
                    return new TutorialOverlayContent(
                        "What do you use the Work tab for?",
                        "Pick the closest path. Better Work Tab will start with that workflow and then pivot when you click into other parts of the tab.",
                        "Sub-work and schedules",
                        "Already know Better Work Tab",
                        "Pawns and layout",
                        "Priorities");

                case BWTGeneralTutorialStep.ManualPriorities:
                    return new TutorialOverlayContent(
                        "Manual priorities",
                        "Manual priorities switch the Work tab from simple checkmarks to numbered priorities. Lower numbers run first, and disabled work stays off.",
                        "Next");

                case BWTGeneralTutorialStep.ShiftSkills:
                    return new TutorialOverlayContent(
                        "Skill overlay",
                        "Hold Shift over the Work tab to show skill information in the work cells. This helps you assign work without opening every pawn bio.",
                        null);

                case BWTGeneralTutorialStep.PawnMenu:
                    return new TutorialOverlayContent(
                        "Pawn row menu",
                        "Right-click a pawn row or pawn name to open Better Work Tab's pawn actions. This is where row tools and pawn-specific display options live.",
                        null);

                case BWTGeneralTutorialStep.Dividers:
                    return new TutorialOverlayContent(
                        "Dividers",
                        "Use the pawn row menu to add dividers above or below pawns. Dividers can be renamed, recolored, resized, collapsed, and expanded.",
                        "Next");

                case BWTGeneralTutorialStep.PawnTitleAndColor:
                    return new TutorialOverlayContent(
                        "Pawn title and color",
                        "The pawn row menu can change the pawn row background and the displayed pawn title, such as miner, doctor, or colonist.",
                        "Next");

                case BWTGeneralTutorialStep.Workloads:
                    return new TutorialOverlayContent(
                        "Workloads",
                        "Workloads save and apply a full Work tab setup. They are useful for quickly swapping colony-wide work patterns.",
                        "Next");

                case BWTGeneralTutorialStep.Rulesets:
                    return new TutorialOverlayContent(
                        "Rulesets",
                        "Rulesets use conditions to assign work automatically. Open the ruleset builder to define who should receive each priority.",
                        "Next");

                case BWTGeneralTutorialStep.DragDrop:
                    return new TutorialOverlayContent(
                        "Drag and drop",
                        "Drag work headers to reorder columns and drag pawn rows to reorder pawns. Moved columns can be marked so custom layout changes are visible.",
                        "Next");

                case BWTGeneralTutorialStep.GroupDrag:
                    return new TutorialOverlayContent(
                        "Grouped column drag",
                        "Shift-click headers to group columns, then drag one grouped header to move the whole group together. Ctrl-click now opens sub-work jobs.",
                        "Next");

                case BWTGeneralTutorialStep.ContextSettings:
                    return new TutorialOverlayContent(
                        "Settings by context",
                        "Alt-click anywhere in the Work tab to open settings related to that exact area. Shift+Alt and Ctrl+Alt narrow the setting focus further.",
                        "Next");

                case BWTGeneralTutorialStep.SettingsShortcut:
                    return new TutorialOverlayContent(
                        "Settings shortcut",
                        "The eye button in the bottom-right opens Better Work Tab settings. Tutorial cards also have Related settings, which opens settings for the lesson being explained.",
                        "Next");

                case BWTGeneralTutorialStep.SubWorkJobs:
                    return new TutorialOverlayContent(
                        "2.0 sub-work jobs",
                        "Ctrl-click a work header to open sub-work jobs. The headers become the sub-jobs for that work type, with global and pawn-specific priorities.",
                        null);

                case BWTGeneralTutorialStep.SubWorkLeft:
                    return new TutorialOverlayContent(
                        "Back to work types",
                        "You left the sub-work job view. The Work tab is back to normal work types, and you can re-enter sub-work jobs with Ctrl-click on a header.",
                        "Next");

                case BWTGeneralTutorialStep.TimePriorities:
                    return new TutorialOverlayContent(
                        "2.0 time priorities",
                        "Ctrl-click a priority cell to schedule different priorities by hour. Sub-work jobs can also have their own time priority schedules.",
                        null);

                case BWTGeneralTutorialStep.TimePrioritiesClosed:
                    return new TutorialOverlayContent(
                        "Schedule closed",
                        "You closed the time-priority schedule. The row returns to its normal priority cells, and the hourly priorities continue applying in the background.",
                        "Next");

                case BWTGeneralTutorialStep.MaxPriorities:
                    return new TutorialOverlayContent(
                        "More priority levels",
                        BWTTutorialUserContext.BuildPriorityTutorialBody(),
                        "Use BWT 1-9",
                        null,
                        "Keep current");

                case BWTGeneralTutorialStep.Complete:
                    return new TutorialOverlayContent(
                        "Tutorial complete",
                        "You can restart this tutorial or the focused 2.0 tutorial from Better Work Tab settings. Please send feedback on Discord after testing.",
                        "Finish",
                        null,
                        "Related settings");

                default:
                    return new TutorialOverlayContent("Better Work Tab", "Tutorial complete.", "Finish");
            }
        }

        private static List<Rect> BuildFocusRects(
            BWTGeneralTutorialStep step,
            Rect inRect,
            IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            switch (step)
            {
                case BWTGeneralTutorialStep.ManualPriorities:
                    rects.AddRange(BWTTutorialGeometry.ManualPriorities(inRect));
                    rects.AddRange(BWTTutorialGeometry.PriorityLegend(inRect));
                    return rects;
                case BWTGeneralTutorialStep.ShiftSkills:
                    return BWTTutorialGeometry.FirstPriorityCell(inRect, layout, requireSubWorkColumn: false);
                case BWTGeneralTutorialStep.PawnMenu:
                case BWTGeneralTutorialStep.PawnTitleAndColor:
                    return BWTTutorialGeometry.PawnNameColumn(inRect, layout);
                case BWTGeneralTutorialStep.Dividers:
                    return BWTTutorialGeometry.FirstDivider(inRect, layout);
                case BWTGeneralTutorialStep.Workloads:
                    return BWTTutorialGeometry.Workloads(inRect);
                case BWTGeneralTutorialStep.Rulesets:
                    return BWTTutorialGeometry.Rulesets(inRect);
                case BWTGeneralTutorialStep.DragDrop:
                case BWTGeneralTutorialStep.GroupDrag:
                case BWTGeneralTutorialStep.SubWorkJobs:
                case BWTGeneralTutorialStep.SubWorkLeft:
                    return BWTTutorialGeometry.WorkHeaders(inRect, layout);
                case BWTGeneralTutorialStep.ContextSettings:
                    return BWTTutorialGeometry.ContextSettingsHint(inRect);
                case BWTGeneralTutorialStep.SettingsShortcut:
                    return BWTTutorialGeometry.InfoButton(inRect);
                case BWTGeneralTutorialStep.TimePriorities:
                case BWTGeneralTutorialStep.TimePrioritiesClosed:
                case BWTGeneralTutorialStep.MaxPriorities:
                    return BWTTutorialGeometry.FirstPriorityCell(
                        inRect,
                        layout,
                        requireSubWorkColumn: false,
                        allowDisabledFallback: false);
                case BWTGeneralTutorialStep.Complete:
                    return BWTTutorialGeometry.InfoButton(inRect);
                default:
                    return BWTTutorialGeometry.WholeTab(inRect);
            }
        }

        private static List<TutorialOverlayShortcutHint> BuildShortcutHints(
            BWTGeneralTutorialStep step,
            int focusCount)
        {
            string hint = GetShortcutHint(step);
            if (string.IsNullOrEmpty(hint) || focusCount <= 0)
            {
                return null;
            }

            return new List<TutorialOverlayShortcutHint>
            {
                new TutorialOverlayShortcutHint(hint, 0)
            };
        }

        private static string GetShortcutHint(BWTGeneralTutorialStep step)
        {
            switch (step)
            {
                case BWTGeneralTutorialStep.ShiftSkills:
                    return "Hold Shift";
                case BWTGeneralTutorialStep.PawnMenu:
                case BWTGeneralTutorialStep.Dividers:
                case BWTGeneralTutorialStep.PawnTitleAndColor:
                    return "Right-click";
                case BWTGeneralTutorialStep.DragDrop:
                    return "Drag";
                case BWTGeneralTutorialStep.GroupDrag:
                    return "Shift + click";
                case BWTGeneralTutorialStep.ContextSettings:
                    return "Alt + click";
                case BWTGeneralTutorialStep.SettingsShortcut:
                    return "Open settings";
                case BWTGeneralTutorialStep.SubWorkJobs:
                case BWTGeneralTutorialStep.TimePriorities:
                case BWTGeneralTutorialStep.SubWorkLeft:
                case BWTGeneralTutorialStep.TimePrioritiesClosed:
                    return "Ctrl + click";
                default:
                    return null;
            }
        }

        private static void AdvanceByButton(BWTGeneralTutorialStep step)
        {
            switch (step)
            {
                case BWTGeneralTutorialStep.Welcome:
                    BWTWorkTabTutorial.Start2TutorialAt(BWTBetaTutorialStep.SubWorkPrompt);
                    break;
                case BWTGeneralTutorialStep.ManualPriorities:
                    SetStep(BWTGeneralTutorialStep.ShiftSkills);
                    break;
                case BWTGeneralTutorialStep.ShiftSkills:
                    SetStep(BWTGeneralTutorialStep.PawnMenu);
                    break;
                case BWTGeneralTutorialStep.PawnMenu:
                    SetStep(BWTGeneralTutorialStep.Dividers);
                    break;
                case BWTGeneralTutorialStep.Dividers:
                    SetStep(BWTGeneralTutorialStep.PawnTitleAndColor);
                    break;
                case BWTGeneralTutorialStep.PawnTitleAndColor:
                    SetStep(BWTGeneralTutorialStep.Workloads);
                    break;
                case BWTGeneralTutorialStep.Workloads:
                    SetStep(BWTGeneralTutorialStep.Rulesets);
                    break;
                case BWTGeneralTutorialStep.Rulesets:
                    SetStep(BWTGeneralTutorialStep.DragDrop);
                    break;
                case BWTGeneralTutorialStep.DragDrop:
                    SetStep(BWTGeneralTutorialStep.GroupDrag);
                    break;
                case BWTGeneralTutorialStep.GroupDrag:
                    SetStep(BWTGeneralTutorialStep.ContextSettings);
                    break;
                case BWTGeneralTutorialStep.ContextSettings:
                    SetStep(BWTGeneralTutorialStep.SettingsShortcut);
                    break;
                case BWTGeneralTutorialStep.SettingsShortcut:
                    SetStep(BWTGeneralTutorialStep.SubWorkJobs);
                    break;
                case BWTGeneralTutorialStep.SubWorkJobs:
                    SetStep(BWTGeneralTutorialStep.TimePriorities);
                    break;
                case BWTGeneralTutorialStep.SubWorkLeft:
                    SetStep(BWTGeneralTutorialStep.TimePriorities);
                    break;
                case BWTGeneralTutorialStep.TimePriorities:
                    SetStep(BWTGeneralTutorialStep.MaxPriorities);
                    break;
                case BWTGeneralTutorialStep.TimePrioritiesClosed:
                    SetStep(BWTGeneralTutorialStep.MaxPriorities);
                    break;
                case BWTGeneralTutorialStep.MaxPriorities:
                    BWTTutorialUserContext.UseBetterWorkTabPriorityDefaults();
                    SetStep(BWTGeneralTutorialStep.Complete);
                    break;
                case BWTGeneralTutorialStep.Complete:
                    Complete();
                    break;
                default:
                    Complete();
                    break;
            }
        }

        private static bool TryAdvanceFromExpectedInteraction(
            BWTGeneralTutorialStep currentStep,
            BWTTutorialInteraction interaction)
        {
            switch (currentStep)
            {
                case BWTGeneralTutorialStep.PawnMenu:
                    if (interaction.Kind == BWTTutorialInteractionKind.PawnRow &&
                        interaction.MouseButton == 1)
                    {
                        SetStep(BWTGeneralTutorialStep.Dividers);
                        return true;
                    }
                    break;
                case BWTGeneralTutorialStep.SubWorkJobs:
                    if ((interaction.Kind == BWTTutorialInteractionKind.WorkHeader ||
                         interaction.Kind == BWTTutorialInteractionKind.SubWorkHeader) &&
                        interaction.Control)
                    {
                        BWTWorkTabTutorial.Start2TutorialAt(BWTBetaTutorialStep.SubWorkHeaders);
                        return true;
                    }
                    break;
                case BWTGeneralTutorialStep.TimePriorities:
                    if ((interaction.Kind == BWTTutorialInteractionKind.PriorityCell ||
                         interaction.Kind == BWTTutorialInteractionKind.TimePriorityCell) &&
                        interaction.Control)
                    {
                        BWTWorkTabTutorial.Start2TutorialAt(BWTBetaTutorialStep.TimePriorityHours);
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static void HandleSecondaryButton(
            BWTGeneralTutorialStep step,
            Rect inRect,
            IWorkTabLayoutController layout,
            List<Rect> focusRects)
        {
            switch (step)
            {
                case BWTGeneralTutorialStep.Welcome:
                    SetStep(BWTGeneralTutorialStep.PawnMenu);
                    break;
                case BWTGeneralTutorialStep.MaxPriorities:
                    SetStep(BWTGeneralTutorialStep.Complete);
                    break;
                default:
                    BWTWorkTabTutorial.OpenRelatedSettings(inRect, layout, focusRects);
                    break;
            }
        }

        private static void HandleTertiaryButton(BWTGeneralTutorialStep step)
        {
            if (step == BWTGeneralTutorialStep.Welcome)
            {
                SetStep(BWTGeneralTutorialStep.ManualPriorities);
            }
        }

        private static void SetStep(BWTGeneralTutorialStep step)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.generalTutorialStep = (int)step;
            settings.Write();
        }

        private static void Deactivate()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.showGeneralTutorial = false;
            settings.Write();
            Overlay.ResetAnimation();
        }

        private static void Complete()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.generalTutorialStep = (int)BWTGeneralTutorialStep.Completed;
            settings.showGeneralTutorial = false;
            settings.Write();
            Overlay.ResetAnimation();
        }
    }
}
