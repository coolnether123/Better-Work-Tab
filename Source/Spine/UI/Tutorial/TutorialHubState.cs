using System;
using System.Collections.Generic;

namespace Spine.UI.Tutorial
{
    public enum TutorialHubAnchor
    {
        None = -1,
        PawnName = 0,
        WorkHeader = 1,
        PriorityCell = 2
    }

    public enum TutorialActionRequirement
    {
        None,
        PriorityChanged,
        SkillVisibleForInterval,
        ScheduleOpenedEditedAndClosed,
        SettingsRouteOpened,
        PawnMenuOpened,
        DividerCreated,
        PawnAppearanceChanged,
        HeaderMoved,
        HeaderGrouped,
        SubWorkOpened
    }

    public enum TutorialPresentation
    {
        Hidden,
        Welcome,
        Selector,
        Lesson
    }

    /// <summary>Pure, exclusive screen ownership for the unified tutorial flow.</summary>
    public static class TutorialPresentationPolicy
    {
        public static TutorialPresentation Resolve(
            bool showTutorial,
            bool welcomeCompleted,
            string activeLessonId)
        {
            if (!showTutorial)
            {
                return TutorialPresentation.Hidden;
            }

            if (!welcomeCompleted)
            {
                return TutorialPresentation.Welcome;
            }

            return string.IsNullOrEmpty(activeLessonId)
                ? TutorialPresentation.Selector
                : TutorialPresentation.Lesson;
        }
    }

    /// <summary>
    /// Pure policy for deciding whether a visible tutorial surface owns the
    /// pointer instead of the interface drawn beneath it.
    /// </summary>
    public static class TutorialPointerOwnershipPolicy
    {
        public static bool BlocksUnderlyingPointer(
            TutorialPresentation presentation,
            bool overTutorialSurface)
        {
            return presentation != TutorialPresentation.Hidden && overTutorialSurface;
        }
    }

    /// <summary>
    /// Pure overflow policy for tutorial surfaces that normally fit without
    /// scrolling but must remain usable after resolution, UI-scale, or
    /// localization changes make their content taller than the viewport.
    /// </summary>
    public static class TutorialOverflowScrollPolicy
    {
        public static float MaximumOffset(float viewportHeight, float contentHeight)
        {
            return Math.Max(0f, contentHeight - Math.Max(0f, viewportHeight));
        }

        public static float ClampOffset(float offset, float viewportHeight, float contentHeight)
        {
            return Math.Max(0f, Math.Min(offset, MaximumOffset(viewportHeight, contentHeight)));
        }

        public static float ApplyWheel(
            float offset,
            float wheelDelta,
            float viewportHeight,
            float contentHeight,
            float pixelsPerWheelUnit = 28f)
        {
            return ClampOffset(
                offset + wheelDelta * Math.Max(0f, pixelsPerWheelUnit),
                viewportHeight,
                contentHeight);
        }
    }

    /// <summary>
    /// Pure hover ownership model shared by tutorial hubs. Connected option and
    /// description regions retain the originating anchor, and a short grace
    /// interval covers ordinary pointer travel between those regions.
    /// </summary>
    public sealed class TutorialHoverGraceState
    {
        private readonly double graceSeconds;
        private TutorialHubAnchor activeAnchor = TutorialHubAnchor.None;
        private double lastConnectedAt = double.NegativeInfinity;

        public TutorialHoverGraceState(double graceSeconds = 0.22d)
        {
            this.graceSeconds = Math.Max(0d, graceSeconds);
        }

        public TutorialHubAnchor ActiveAnchor => activeAnchor;

        public TutorialHubAnchor Update(
            double nowSeconds,
            TutorialHubAnchor hoveredAnchor,
            bool overConnectedOptions,
            bool overDescription)
        {
            if (hoveredAnchor != TutorialHubAnchor.None)
            {
                activeAnchor = hoveredAnchor;
                lastConnectedAt = nowSeconds;
                return activeAnchor;
            }

            if (activeAnchor != TutorialHubAnchor.None && (overConnectedOptions || overDescription))
            {
                lastConnectedAt = nowSeconds;
                return activeAnchor;
            }

            if (activeAnchor != TutorialHubAnchor.None && nowSeconds - lastConnectedAt <= graceSeconds)
            {
                return activeAnchor;
            }

            activeAnchor = TutorialHubAnchor.None;
            return activeAnchor;
        }

        public void Pin(TutorialHubAnchor anchor, double nowSeconds)
        {
            activeAnchor = anchor;
            lastConnectedAt = nowSeconds;
        }

        public void Clear()
        {
            activeAnchor = TutorialHubAnchor.None;
            lastConnectedAt = double.NegativeInfinity;
        }
    }

    /// <summary>Pure progress transitions used by the settings-backed controller and tests.</summary>
    public static class TutorialProgressTransitions
    {
        public static bool IsCompleted(ICollection<string> completedLessonIds, string lessonId)
        {
            return completedLessonIds != null &&
                   !string.IsNullOrEmpty(lessonId) &&
                   completedLessonIds.Contains(lessonId);
        }

        public static void Complete(ICollection<string> completedLessonIds, string lessonId)
        {
            if (completedLessonIds == null || string.IsNullOrEmpty(lessonId) || completedLessonIds.Contains(lessonId))
            {
                return;
            }

            completedLessonIds.Add(lessonId);
        }

        public static bool ShouldConsumeEnter(bool hasExplicitPrimaryAction, bool actionRequired)
        {
            return hasExplicitPrimaryAction && !actionRequired;
        }

        public static bool CanStartLesson(bool optionHovered, bool optionClicked)
        {
            return optionClicked;
        }

        public static void ResolveWelcomeChoice(
            ref bool welcomeCompleted,
            ref bool showTutorial,
            bool continueWalkthrough)
        {
            welcomeCompleted = true;
            showTutorial = continueWalkthrough;
        }

        public static void ReturnToSelection(ref string activeLessonId, ref int lessonPhase)
        {
            activeLessonId = string.Empty;
            lessonPhase = 0;
        }

        public static bool ShouldCompleteAction(
            TutorialActionRequirement requirement,
            bool priorityChanged,
            double skillVisibleSeconds,
            bool scheduleOpened,
            bool scheduleEdited,
            bool scheduleClosed,
            bool routedSettings,
            bool observedAction)
        {
            switch (requirement)
            {
                case TutorialActionRequirement.PriorityChanged:
                    return priorityChanged;
                case TutorialActionRequirement.SkillVisibleForInterval:
                    return skillVisibleSeconds >= 0.65d;
                case TutorialActionRequirement.ScheduleOpenedEditedAndClosed:
                    return scheduleOpened && scheduleEdited && scheduleClosed;
                case TutorialActionRequirement.SettingsRouteOpened:
                    return routedSettings;
                case TutorialActionRequirement.None:
                    return false;
                default:
                    return observedAction;
            }
        }
    }

    public enum TutorialPriorityRoutingMode
    {
        Vanilla,
        Auto,
        ExternalProvider,
        BetterWorkTab
    }

    public static class TutorialSettingsRouting
    {
        public static string ResolvePriorityTarget(TutorialPriorityRoutingMode mode)
        {
            switch (mode)
            {
                case TutorialPriorityRoutingMode.BetterWorkTab:
                    return "ui.maxPriority";
                case TutorialPriorityRoutingMode.Auto:
                    // Auto uses the visible maximum-priority ceiling. The
                    // auto-specific value is retained only for old saves and
                    // is intentionally hidden from the settings UI.
                    return "ui.maxPriority";
                default:
                    return "priority.mode";
            }
        }
    }

    /// <summary>
    /// The main tutorial is the sole Work-tab tutorial owner. The legacy beta
    /// flag is migration input only and can never activate a second controller.
    /// </summary>
    public static class TutorialOwnershipPolicy
    {
        public static bool MainOwnsWorkTab(bool showMainTutorial, bool legacyBetaFlag)
        {
            return showMainTutorial;
        }

        public static bool LegacyCanOwnWorkTab(bool legacyBetaFlag)
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps Work-tab tutorial surfaces behind modal settings UI. The tutorial
    /// remains enabled and resumes after the settings window closes.
    /// </summary>
    public static class TutorialVisibilityPolicy
    {
        public static bool AllowsWorkTabTutorial(bool settingsWindowOpen)
        {
            return !settingsWindowOpen;
        }
    }
}
