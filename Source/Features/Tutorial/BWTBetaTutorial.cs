using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Features.Tutorial
{
    internal enum BWTBetaTutorialStep
    {
        Welcome = 0,
        SubWorkPrompt = 10,
        SubWorkHeaders = 20,
        SubWorkGlobalPriority = 30,
        SubWorkPawnPriority = 40,
        SubWorkChangePawnPriority = 50,
        SubWorkResetOverride = 60,
        SubWorkResetConfirmed = 70,
        SubWorkExitPrompt = 80,
        SubWorkLeft = 85,
        TimePriorityPrompt = 90,
        TimePriorityHours = 100,
        TimePriorityClosePrompt = 110,
        TimePriorityClosed = 115,
        TimePrioritySubWork = 120,
        MaxPriority = 130,
        AltClickSettings = 140,
        Completed = 1000
    }

    /// <summary>
    /// Guided Work tab walkthrough for Better Work Tab 2.0. The tutorial observes existing
    /// Work tab state and never owns sub-work, priority, or schedule behavior.
    /// </summary>
    internal static class BWTBetaTutorial
    {
        private static readonly TutorialOverlayController Overlay = new TutorialOverlayController();

        private static int _lastObservedStep = int.MinValue;
        private static int _observedSyncVersion;
        private static int _observedOverrideCount;
        private static bool _observedSubWorkActive;
        private static bool _hasWorkTabStateSnapshot;
        private static bool _lastSubWorkActive;
        private static bool _lastTimePriorityVisible;

        internal static bool IsActive
        {
            get
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return settings != null &&
                    settings.showBetaTutorial &&
                    NormalizeStep(settings) != BWTBetaTutorialStep.Completed;
            }
        }

        internal static bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showBetaTutorial || evt == null)
            {
                return false;
            }

            BWTBetaTutorialStep step = NormalizeStep(settings);
            if (step == BWTBetaTutorialStep.Completed)
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
                null,
                Deactivate);
        }

        internal static bool TryHandleAcceptKey()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showBetaTutorial)
            {
                return false;
            }

            BWTBetaTutorialStep step = NormalizeStep(settings);
            if (step == BWTBetaTutorialStep.Completed)
            {
                return false;
            }

            TutorialOverlayContent content = BuildContent(step);
            return Overlay.TryHandleAcceptKey(content, () => AdvanceByButton(step));
        }

        internal static void TickAndDraw(Rect inRect, IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showBetaTutorial)
            {
                return;
            }

            BWTBetaTutorialStep step = NormalizeStep(settings);
            if (step == BWTBetaTutorialStep.Completed)
            {
                settings.showBetaTutorial = false;
                settings.Write();
                return;
            }

            InitializeStepObservationIfNeeded(step);
            AdvanceFromObservedActions(step);
            step = NormalizeStep(settings);

            TutorialOverlayContent content = BuildContent(step);
            List<Rect> focusRects = BuildFocusRects(step, inRect, layout);
            Overlay.Draw(
                inRect,
                content,
                focusRects,
                BuildShortcutHints(step, focusRects.Count),
                () => AdvanceByButton(step),
                () => HandleSecondaryButton(step, inRect, layout, focusRects),
                null,
                Deactivate);
        }

        internal static void ObserveWorkTabState()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showBetaTutorial)
            {
                return;
            }

            BWTBetaTutorialStep step = NormalizeStep(settings);
            bool isSubWorkActive = SubWorkDrilldownState.IsActive;
            bool isTimePriorityVisible = TimePriorityScheduleEditor.IsVisible;
            if (!_hasWorkTabStateSnapshot)
            {
                _hasWorkTabStateSnapshot = true;
                _lastSubWorkActive = isSubWorkActive;
                _lastTimePriorityVisible = isTimePriorityVisible;
            }

            bool enteredSubWork = isSubWorkActive && !_lastSubWorkActive;
            bool leftSubWork = !isSubWorkActive && _lastSubWorkActive;
            bool openedTimePriority = isTimePriorityVisible && !_lastTimePriorityVisible;
            bool closedTimePriority = !isTimePriorityVisible && _lastTimePriorityVisible;

            _lastSubWorkActive = isSubWorkActive;
            _lastTimePriorityVisible = isTimePriorityVisible;

            if (leftSubWork &&
                step >= BWTBetaTutorialStep.SubWorkHeaders &&
                step <= BWTBetaTutorialStep.SubWorkExitPrompt)
            {
                SetStep(BWTBetaTutorialStep.SubWorkLeft);
            }
            else if (closedTimePriority &&
                     step >= BWTBetaTutorialStep.TimePriorityHours &&
                     step <= BWTBetaTutorialStep.TimePriorityClosePrompt)
            {
                SetStep(BWTBetaTutorialStep.TimePriorityClosed);
            }
            else if ((enteredSubWork || isSubWorkActive) && step == BWTBetaTutorialStep.SubWorkPrompt)
            {
                SetStep(BWTBetaTutorialStep.SubWorkHeaders);
            }
            else if ((openedTimePriority || isTimePriorityVisible) && step == BWTBetaTutorialStep.TimePriorityPrompt)
            {
                SetStep(BWTBetaTutorialStep.TimePriorityHours);
            }
        }

        internal static bool ObserveInteraction(BWTTutorialInteraction interaction)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || !settings.showBetaTutorial)
            {
                return false;
            }

            BWTBetaTutorialStep step = NormalizeStep(settings);
            if (interaction.Kind == BWTTutorialInteractionKind.SubWorkHeader &&
                step < BWTBetaTutorialStep.SubWorkHeaders)
            {
                SetStep(BWTBetaTutorialStep.SubWorkPrompt);
                return true;
            }
            else if (interaction.Kind == BWTTutorialInteractionKind.PriorityCell &&
                     interaction.Control &&
                     step < BWTBetaTutorialStep.TimePriorityHours &&
                     !SubWorkDrilldownState.IsActive)
            {
                SetStep(BWTBetaTutorialStep.TimePriorityPrompt);
                return true;
            }

            return false;
        }

        internal static void StartAt(BWTBetaTutorialStep step)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.showGeneralTutorial = false;
            settings.showBetaTutorial = true;
            settings.betaTutorialStep = (int)step;
            _lastObservedStep = int.MinValue;
            _hasWorkTabStateSnapshot = false;
            Overlay.ResetAnimation();
            settings.Write();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private static BWTBetaTutorialStep NormalizeStep(BetterWorkTabSettings settings)
        {
            int rawStep = settings.betaTutorialStep;
            if (!Enum.IsDefined(typeof(BWTBetaTutorialStep), rawStep) ||
                rawStep >= (int)BWTBetaTutorialStep.Completed)
            {
                rawStep = (int)BWTBetaTutorialStep.Welcome;
                settings.betaTutorialStep = rawStep;
            }

            return (BWTBetaTutorialStep)rawStep;
        }

        private static void InitializeStepObservationIfNeeded(BWTBetaTutorialStep step)
        {
            if (_lastObservedStep == (int)step)
            {
                return;
            }

            _lastObservedStep = (int)step;
            _observedSyncVersion = WorkGiverReassignmentManager.CurrentSyncVersion;
            _observedOverrideCount = CountActiveSubWorkPawnOverrides();
            _observedSubWorkActive = SubWorkDrilldownState.IsActive;
        }

        private static void AdvanceFromObservedActions(BWTBetaTutorialStep step)
        {
            switch (step)
            {
                case BWTBetaTutorialStep.SubWorkPrompt:
                    if (SubWorkDrilldownState.IsActive)
                    {
                        SetStep(BWTBetaTutorialStep.SubWorkHeaders);
                    }
                    break;

                case BWTBetaTutorialStep.SubWorkChangePawnPriority:
                    if (SubWorkDrilldownState.IsActive &&
                        (CountActiveSubWorkPawnOverrides() > _observedOverrideCount ||
                         WorkGiverReassignmentManager.CurrentSyncVersion != _observedSyncVersion))
                    {
                        SetStep(BWTBetaTutorialStep.SubWorkResetOverride);
                    }
                    break;

                case BWTBetaTutorialStep.SubWorkResetOverride:
                    if (SubWorkDrilldownState.IsActive &&
                        (CountActiveSubWorkPawnOverrides() < _observedOverrideCount ||
                         WorkGiverReassignmentManager.CurrentSyncVersion != _observedSyncVersion))
                    {
                        SetStep(BWTBetaTutorialStep.SubWorkResetConfirmed);
                    }
                    break;

                case BWTBetaTutorialStep.SubWorkExitPrompt:
                    if (_observedSubWorkActive && !SubWorkDrilldownState.IsActive)
                    {
                        SetStep(BWTBetaTutorialStep.SubWorkLeft);
                    }
                    break;

                case BWTBetaTutorialStep.TimePriorityPrompt:
                    if (TimePriorityScheduleEditor.IsVisible)
                    {
                        SetStep(BWTBetaTutorialStep.TimePriorityHours);
                    }
                    break;

                case BWTBetaTutorialStep.TimePriorityHours:
                    if (!TimePriorityScheduleEditor.IsVisible)
                    {
                        SetStep(BWTBetaTutorialStep.TimePriorityClosed);
                    }
                    break;

                case BWTBetaTutorialStep.TimePriorityClosePrompt:
                    if (!TimePriorityScheduleEditor.IsVisible)
                    {
                        SetStep(BWTBetaTutorialStep.TimePriorityClosed);
                    }
                    break;
            }
        }

        private static int CountActiveSubWorkPawnOverrides()
        {
            WorkTypeDef workType = SubWorkDrilldownState.ActiveWorkType;
            return workType == null
                ? 0
                : WorkGiverReassignmentManager.CountPawnPriorityOverrides(workType);
        }

        private static TutorialOverlayContent BuildContent(BWTBetaTutorialStep step)
        {
            switch (step)
            {
                case BWTBetaTutorialStep.Welcome:
                    return new TutorialOverlayContent(
                        "Better Work Tab 2.0",
                        "A short walkthrough for sub-work jobs, time priority schedules, and expanded priority ranges. It follows the Work tab as you try each feature.",
                        "Continue",
                        "Already know Better Work Tab",
                        null);

                case BWTBetaTutorialStep.SubWorkPrompt:
                    return new TutorialOverlayContent(
                        "Sub-work jobs",
                        "Sub-jobs are now part of Better Work Tab. Ctrl-click used to group headers; that is now Shift-click. Hold Control and click any work header to open that work type's sub-work jobs.",
                        null);

                case BWTBetaTutorialStep.SubWorkHeaders:
                    return new TutorialOverlayContent(
                        "Sub-work headers",
                        "The work headers have changed to the sub-work jobs for this work type. They can be rearranged like normal work columns.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkGlobalPriority:
                    return new TutorialOverlayContent(
                        "Global sub-work priority",
                        "This top row is the global priority for each sub-work job. It follows the same left-to-right priority behavior as the normal Work tab.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkPawnPriority:
                    return new TutorialOverlayContent(
                        "Pawn sub-work priority",
                        "Each pawn row can have its own priority for a specific sub-work job. Pawns follow the global priority unless you manually change their cell.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkChangePawnPriority:
                    return new TutorialOverlayContent(
                        "Try an override",
                        "Change one pawn priority in this sub-work view. The cell will get a gold box when it stops following the global priority.",
                        null);

                case BWTBetaTutorialStep.SubWorkResetOverride:
                    return new TutorialOverlayContent(
                        "Gold box means locked",
                        "The gold box means this pawn is locked to the value you chose. Click the gold box to match the global priority again.",
                        null);

                case BWTBetaTutorialStep.SubWorkResetConfirmed:
                    return new TutorialOverlayContent(
                        "Following global again",
                        "Good. The pawn is following the global priority again. This same gold-box behavior also applies to global sub-work priority schedules.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkExitPrompt:
                    return new TutorialOverlayContent(
                        "Exit sub-work",
                        "Return to normal work types by clicking the X at the top right or by Control-clicking a header.",
                        null);

                case BWTBetaTutorialStep.SubWorkLeft:
                    return new TutorialOverlayContent(
                        "Back to normal work",
                        "You left the sub-work job view. The normal work columns are active again, and Ctrl-clicking a header will re-open that work type's sub-work jobs.",
                        "Next");

                case BWTBetaTutorialStep.TimePriorityPrompt:
                    return new TutorialOverlayContent(
                        "Time priority schedules",
                        "You can now schedule a pawn to use different priorities during the day and night. Control-click any pawn priority cell to open its time schedule.",
                        null);

                case BWTBetaTutorialStep.TimePriorityHours:
                    return new TutorialOverlayContent(
                        "Hour blocks",
                        "These are the 24 hour blocks. Each box stores the priority used during that hour. Click or scroll the boxes to change the scheduled priority.",
                        "Next");

                case BWTBetaTutorialStep.TimePriorityClosePrompt:
                    return new TutorialOverlayContent(
                        "Close the schedule",
                        "Control-click any time slot in the open schedule to close it. The Work tab returns to the normal priority row.",
                        null);

                case BWTBetaTutorialStep.TimePriorityClosed:
                    return new TutorialOverlayContent(
                        "Schedule closed",
                        "You closed the time-priority schedule. The normal priority row is visible again, and the saved hourly priorities keep applying in the background.",
                        "Next");

                case BWTBetaTutorialStep.TimePrioritySubWork:
                    return new TutorialOverlayContent(
                        "Sub-work schedules",
                        "Sub-work jobs can have their own time schedules too. For example, a doctor can be scheduled to only do surgery from hours 8 to 12.",
                        "Next");

                case BWTBetaTutorialStep.MaxPriority:
                    return new TutorialOverlayContent(
                        "Priorities beyond 4",
                        BWTTutorialUserContext.BuildPriorityTutorialBody(),
                        "Use BWT 1-9",
                        null,
                        "Keep current");

                case BWTBetaTutorialStep.AltClickSettings:
                    return new TutorialOverlayContent(
                        "Settings by context",
                        "Alt-click anywhere in the Work tab to open settings for that feature. Use Shift+Alt or Ctrl+Alt for more specific settings.",
                        "Finish tutorial");

                default:
                    return new TutorialOverlayContent("Better Work Tab 2.0", "Tutorial complete.", "Finish tutorial");
            }
        }

        private static List<TutorialOverlayShortcutHint> BuildShortcutHints(BWTBetaTutorialStep step, int focusCount)
        {
            string hint = GetShortcutHint(step);
            if (string.IsNullOrEmpty(hint) || focusCount <= 0)
            {
                return null;
            }

            int hintCount = GetShortcutHintFocusCount(step, focusCount);
            var hints = new List<TutorialOverlayShortcutHint>(hintCount);
            for (int i = 0; i < hintCount; i++)
            {
                hints.Add(new TutorialOverlayShortcutHint(hint, i));
            }

            return hints;
        }

        private static List<Rect> BuildFocusRects(BWTBetaTutorialStep step, Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            switch (step)
            {
                case BWTBetaTutorialStep.Welcome:
                    return rects;

                case BWTBetaTutorialStep.SubWorkPrompt:
                case BWTBetaTutorialStep.SubWorkHeaders:
                    return BWTTutorialGeometry.WorkHeaders(inRect, layout);

                case BWTBetaTutorialStep.SubWorkGlobalPriority:
                    return BWTTutorialGeometry.GlobalSubWorkPriorityRow(inRect, layout);

                case BWTBetaTutorialStep.SubWorkPawnPriority:
                case BWTBetaTutorialStep.SubWorkChangePawnPriority:
                case BWTBetaTutorialStep.SubWorkResetOverride:
                    return BWTTutorialGeometry.FirstPriorityCell(
                        inRect,
                        layout,
                        requireSubWorkColumn: SubWorkDrilldownState.IsActive);

                case BWTBetaTutorialStep.SubWorkResetConfirmed:
                    rects.AddRange(BWTTutorialGeometry.GlobalSubWorkPriorityRow(inRect, layout));
                    rects.AddRange(BWTTutorialGeometry.FirstPriorityCell(inRect, layout, requireSubWorkColumn: true));
                    return rects;

                case BWTBetaTutorialStep.SubWorkExitPrompt:
                    return BWTTutorialGeometry.SubWorkExit(inRect, layout);

                case BWTBetaTutorialStep.SubWorkLeft:
                    return BWTTutorialGeometry.WorkHeaders(inRect, layout);

                case BWTBetaTutorialStep.TimePriorityPrompt:
                    return BWTTutorialGeometry.FirstPriorityCell(inRect, layout, requireSubWorkColumn: false);

                case BWTBetaTutorialStep.TimePriorityHours:
                case BWTBetaTutorialStep.TimePriorityClosePrompt:
                    return BWTTutorialGeometry.TimePriorityEditor(inRect, layout);

                case BWTBetaTutorialStep.TimePriorityClosed:
                    return BWTTutorialGeometry.FirstPriorityCell(
                        inRect,
                        layout,
                        requireSubWorkColumn: false,
                        allowDisabledFallback: false);

                case BWTBetaTutorialStep.TimePrioritySubWork:
                    rects.AddRange(BWTTutorialGeometry.WorkHeaders(inRect, layout));
                    rects.AddRange(BWTTutorialGeometry.FirstPriorityCell(inRect, layout, requireSubWorkColumn: false));
                    return rects;

                case BWTBetaTutorialStep.MaxPriority:
                    return BWTTutorialGeometry.FirstPriorityCell(
                        inRect,
                        layout,
                        requireSubWorkColumn: false,
                        allowDisabledFallback: false);

                case BWTBetaTutorialStep.AltClickSettings:
                    rects.Add(new Rect(inRect.xMax - 300f, inRect.y + 2f, 260f, 28f));
                    return rects;

                default:
                    return BWTTutorialGeometry.WholeTab(inRect);
            }
        }

        private static string GetShortcutHint(BWTBetaTutorialStep step)
        {
            switch (step)
            {
                case BWTBetaTutorialStep.SubWorkPrompt:
                case BWTBetaTutorialStep.SubWorkLeft:
                case BWTBetaTutorialStep.TimePriorityPrompt:
                case BWTBetaTutorialStep.TimePriorityClosed:
                case BWTBetaTutorialStep.TimePriorityClosePrompt:
                    return "Ctrl + click";
                case BWTBetaTutorialStep.SubWorkExitPrompt:
                    return "Ctrl + click";
                case BWTBetaTutorialStep.AltClickSettings:
                    return "Alt + click";
                default:
                    return null;
            }
        }

        private static int GetShortcutHintFocusCount(BWTBetaTutorialStep step, int focusCount)
        {
            if (step == BWTBetaTutorialStep.SubWorkExitPrompt)
            {
                // The exit button is an ordinary click target; only the highlighted
                // header area should advertise Ctrl+click.
                return Mathf.Min(1, focusCount);
            }

            return focusCount;
        }

        private static void AdvanceByButton(BWTBetaTutorialStep step)
        {
            switch (step)
            {
                case BWTBetaTutorialStep.Welcome:
                    SetStep(BWTBetaTutorialStep.SubWorkPrompt);
                    break;
                case BWTBetaTutorialStep.SubWorkHeaders:
                    SetStep(BWTBetaTutorialStep.SubWorkGlobalPriority);
                    break;
                case BWTBetaTutorialStep.SubWorkGlobalPriority:
                    SetStep(BWTBetaTutorialStep.SubWorkPawnPriority);
                    break;
                case BWTBetaTutorialStep.SubWorkPawnPriority:
                    SetStep(BWTBetaTutorialStep.SubWorkChangePawnPriority);
                    break;
                case BWTBetaTutorialStep.SubWorkResetConfirmed:
                    SetStep(BWTBetaTutorialStep.SubWorkExitPrompt);
                    break;
                case BWTBetaTutorialStep.SubWorkLeft:
                    SetStep(BWTBetaTutorialStep.TimePriorityPrompt);
                    break;
                case BWTBetaTutorialStep.TimePriorityHours:
                    SetStep(BWTBetaTutorialStep.TimePriorityClosePrompt);
                    break;
                case BWTBetaTutorialStep.TimePriorityClosed:
                    SetStep(BWTBetaTutorialStep.TimePrioritySubWork);
                    break;
                case BWTBetaTutorialStep.TimePrioritySubWork:
                    SetStep(BWTBetaTutorialStep.MaxPriority);
                    break;
                case BWTBetaTutorialStep.MaxPriority:
                    BWTTutorialUserContext.UseBetterWorkTabPriorityDefaults();
                    SetStep(BWTBetaTutorialStep.AltClickSettings);
                    break;
                case BWTBetaTutorialStep.AltClickSettings:
                    Complete();
                    break;
            }
        }

        private static void SetStep(BWTBetaTutorialStep step)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.betaTutorialStep = (int)step;
            _lastObservedStep = int.MinValue;
            settings.Write();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private static void HandleSecondaryButton(
            BWTBetaTutorialStep step,
            Rect inRect,
            IWorkTabLayoutController layout,
            List<Rect> focusRects)
        {
            if (step == BWTBetaTutorialStep.MaxPriority)
            {
                SetStep(BWTBetaTutorialStep.AltClickSettings);
                return;
            }

            BWTWorkTabTutorial.OpenRelatedSettings(inRect, layout, focusRects);
        }

        private static void Deactivate()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.showBetaTutorial = false;
            Overlay.ResetAnimation();
            settings.Write();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void Complete()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.betaTutorialStep = (int)BWTBetaTutorialStep.Completed;
            settings.showBetaTutorial = false;
            Overlay.ResetAnimation();
            settings.Write();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }
    }
}
