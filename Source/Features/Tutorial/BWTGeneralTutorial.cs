using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Migration;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.Patches;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.RuleBuilder;
using RimWorld;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>
    /// The single Work-tab tutorial owner. Selection, progress, and action
    /// verification live here; work, schedule, and settings systems remain the
    /// authoritative owners of the actions being taught.
    /// </summary>
    internal static class BWTGeneralTutorial
    {
        internal const int CurrentFlowVersion = 2;
        internal const string PriorityChangeLesson = BWTTutorialLessonCatalog.PriorityChange;
        internal const string PrioritySkillLesson = BWTTutorialLessonCatalog.PrioritySkill;
        internal const string PriorityScheduleLesson = BWTTutorialLessonCatalog.PrioritySchedule;
        internal const string PriorityRangeLesson = BWTTutorialLessonCatalog.PriorityRange;
        internal const string PawnMenuLesson = BWTTutorialLessonCatalog.PawnMenu;
        internal const string PawnDividerLesson = BWTTutorialLessonCatalog.PawnDivider;
        internal const string PawnAppearanceLesson = BWTTutorialLessonCatalog.PawnAppearance;
        internal const string HeaderReorderLesson = BWTTutorialLessonCatalog.HeaderReorder;
        internal const string HeaderGroupLesson = BWTTutorialLessonCatalog.HeaderGroup;
        internal const string HeaderSubWorkLesson = BWTTutorialLessonCatalog.HeaderSubWork;

        private static readonly BWTTutorialSelector Selector = new BWTTutorialSelector();
        private static readonly TutorialOverlayController WelcomeOverlay = new TutorialOverlayController(
            new TutorialOverlayStyle
            {
                CardWidth = 480f,
                DimColor = new Color(0f, 0f, 0f, 0.16f),
                LayoutAnimationSeconds = 0.2f
            });
        private static readonly List<Rect> NoWelcomeFocusRects = new List<Rect>();
        private static readonly List<TutorialOverlayShortcutHint> NoWelcomeShortcutHints =
            new List<TutorialOverlayShortcutHint>();
        private static BWTTutorialAnchor lessonAnchor;
        private static int initialPriority;
        private static int initialScheduleEditRevision;
        private static int initialDividerCount;
        private static int initialHeaderSignature;
        private static int initialHeaderSelectionCount;
        private static bool initialHeaderSelected;
        private static string initialPawnTitle;
        private static bool initialPawnHadColor;
        private static Color initialPawnColor;
        private static float skillVisibleStartedAt = -1f;
        private static bool scheduleOpened;
        private static bool scheduleEdited;
        private static bool initialSubWorkActive;
        private static string observedLessonId = string.Empty;
        private static Vector2 lessonScrollPosition;
        private static bool ownsCurrentPointer;
        private const int FloatingSelectorWindowId = 0x42575451;
        private const int FloatingLessonWindowId = 0x42575452;

        internal static bool OwnsCurrentPointer => ownsCurrentPointer;

        internal static void ShowEntryForSmokeTest()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings.showGeneralTutorial = true;
            settings.tutorialWelcomeCompleted = false;
            settings.activeTutorialLessonId = string.Empty;
            WelcomeOverlay.ResetAnimation();
            settings.Write();
        }

        internal static void StartCourseForSmokeTest(BWTTutorialCourse course)
        {
            StartCourse(course);
        }

        internal static void ResolveCourseForSmokeTest(bool alternateSkipped)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            int index = 0;
            foreach (BWTTutorialLessonDefinition lesson in BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse))
            {
                settings.completedTutorialLessonIds.Remove(lesson.Id);
                settings.skippedTutorialLessonIds.Remove(lesson.Id);
                if (alternateSkipped && index++ % 2 == 1)
                {
                    settings.skippedTutorialLessonIds.Add(lesson.Id);
                }
                else
                {
                    settings.completedTutorialLessonIds.Add(lesson.Id);
                }
            }
            settings.Write();
            ReviewIfCourseResolved(settings);
        }

        internal static bool IsActive
        {
            get
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return settings != null &&
                       TutorialVisibilityPolicy.AllowsWorkTabTutorial(
                           Find.WindowStack?.IsOpen<Dialog_ModSettings>() == true) &&
                       FluffyWorkTabPromptPolicy.AllowsTutorial(
                           FluffyWorkTabMigrationPrompt.BlocksTutorialPresentation ||
                           BWT20UpgradePrompt.BlocksTutorialPresentation) &&
                       TutorialOwnershipPolicy.MainOwnsWorkTab(settings.showGeneralTutorial, legacyBetaFlag: false);
            }
        }

        private static TutorialPresentation Presentation
        {
            get
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return TutorialPresentationPolicy.Resolve(
                    settings?.showGeneralTutorial ?? false,
                    settings?.tutorialWelcomeCompleted ?? false,
                    settings?.activeTutorialLessonId);
            }
        }

        internal static bool TryHandleInput(Rect inRect, IWorkTabLayoutController layout, Event evt)
        {
            if (!IsActive || evt == null)
            {
                return false;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            EnsureState(settings);
            TutorialPresentation presentation = Presentation;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                if (presentation == TutorialPresentation.Welcome)
                {
                    Pause();
                }
                else if (presentation == TutorialPresentation.Lesson)
                {
                    ReturnToSelection();
                }
                else
                {
                    Pause();
                }

                evt.Use();
                return true;
            }

            if (presentation == TutorialPresentation.Welcome)
            {
                return WelcomeOverlay.TryHandleInput(
                    inRect,
                    BuildWelcomeContent(),
                    NoWelcomeFocusRects,
                    evt,
                    StartWhatsNewCourse,
                    StartFullCourse,
                    null,
                    DismissWelcome);
            }

            List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
            Rect workBounds = BWTTutorialGeometry.GetVisibleWorkTabBounds(inRect, layout);

            if (presentation == TutorialPresentation.Lesson)
            {
                // The screen-level lesson window owns its card input. Work-tab
                // interactions remain available everywhere outside that card.
                return false;
            }

            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs = BuildHubDefinitions();
            if (presentation == TutorialPresentation.Selector)
            {
                if (evt.type == EventType.MouseDown && evt.button == 0 &&
                    BWTTutorialGeometry.TryResolveAnchorAt(inRect, layout, evt.mousePosition, out BWTTutorialAnchor selected))
                {
                    return Selector.TryHandleAnchorInput(new[] { selected }, evt);
                }
                return Selector.TryHandleAnchorInput(anchors, evt);
            }
            bool handled = Selector.TryHandleInput(
                inRect,
                workBounds,
                anchors,
                hubs,
                settings.completedTutorialLessonIds,
                evt,
                T("BWT_Tutorial_Recommended"),
                T("BWT_Tutorial_SkipForNow"),
                T("BWT_Tutorial_LeaveTutorial"),
                out BWTTutorialSelectorAction action);
            if (action.Exit)
            {
                Pause();
            }
            else if (action.Leave)
            {
                LeaveTutorial();
            }
            else if (action.HasLesson)
            {
                SelectLesson(action.LessonId, anchors, layout);
            }

            return handled;
        }

        internal static void UpdatePointerOwnership(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 pointer)
        {
            ownsCurrentPointer = IsActive && OwnsPointer(inRect, layout, pointer);
        }

        private static bool OwnsPointer(Rect inRect, IWorkTabLayoutController layout, Vector2 pointer)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return false;
            }

            EnsureState(settings);
            TutorialPresentation presentation = Presentation;
            bool overSurface;
            if (presentation == TutorialPresentation.Welcome)
            {
                overSurface = WelcomeOverlay.ContainsPointer(
                    inRect,
                    BuildWelcomeContent(),
                    NoWelcomeFocusRects,
                    pointer);
            }
            else
            {
                List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
                Rect workBounds = BWTTutorialGeometry.GetVisibleWorkTabBounds(inRect, layout);
                if (presentation == TutorialPresentation.Lesson)
                {
                    Vector2 rootOffset = GUIClipUtility.Unclip(Vector2.zero);
                    Rect rootWorkBounds = OffsetRect(workBounds, rootOffset);
                    BWTTutorialAnchor activeAnchor = ResolveLessonDisplayAnchor(
                        anchors,
                        settings.activeTutorialLessonId,
                        settings.tutorialLessonPhase).OffsetBy(rootOffset);
                    overSurface = BuildLessonLayout(
                        new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight),
                        rootWorkBounds,
                        activeAnchor,
                        GetLessonBody(settings.activeTutorialLessonId, settings.tutorialLessonPhase))
                        .CardRect.Contains(pointer + rootOffset);
                }
                else
                {
                    overSurface = Selector.ContainsPointer(
                        inRect,
                        workBounds,
                        anchors,
                        BuildHubDefinitions(),
                        pointer,
                        T("BWT_Tutorial_Recommended"));
                }
            }

            return TutorialPointerOwnershipPolicy.BlocksUnderlyingPointer(presentation, overSurface);
        }

        private static bool IsPointerEvent(EventType type)
        {
            return type == EventType.MouseDown ||
                   type == EventType.MouseUp ||
                   type == EventType.MouseMove ||
                   type == EventType.MouseDrag ||
                   type == EventType.ScrollWheel;
        }

        internal static bool TryHandleAcceptKey()
        {
            // Every current lesson requires a real Work-tab action and the map
            // has no implicit primary action. Consume Enter so RimWorld does not
            // close the tab, but never advance tutorial state from it.
            if (!IsActive)
            {
                return false;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            EnsureState(settings);
            if (Presentation == TutorialPresentation.Welcome)
            {
                return WelcomeOverlay.TryHandleAcceptKey(BuildWelcomeContent(), StartWhatsNewCourse);
            }

            Event.current?.Use();
            return true;
        }

        internal static void TickAndDraw(Rect inRect, IWorkTabLayoutController layout)
        {
            if (!IsActive)
            {
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            EnsureState(settings);
            TutorialPresentation presentation = Presentation;
            if (presentation == TutorialPresentation.Welcome)
            {
                WelcomeOverlay.Draw(
                    inRect,
                    BuildWelcomeContent(),
                    NoWelcomeFocusRects,
                    NoWelcomeShortcutHints,
                    StartWhatsNewCourse,
                    StartFullCourse,
                    null,
                    DismissWelcome);
                return;
            }

            List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
            Rect workBounds = BWTTutorialGeometry.GetVisibleWorkTabBounds(inRect, layout);
            if (presentation == TutorialPresentation.Selector)
            {
                DrawFloatingSelector(inRect, workBounds, anchors, layout, settings);
                return;
            }

            if (!string.Equals(observedLessonId, settings.activeTutorialLessonId, StringComparison.Ordinal))
            {
                InitializeLessonObservation(settings.activeTutorialLessonId, anchors, layout);
            }
            RefreshLessonAnchor(anchors);
            ObserveActionState(layout);
            if (!IsActive || string.IsNullOrEmpty(settings.activeTutorialLessonId))
            {
                return;
            }

            DrawFloatingLesson(
                workBounds,
                anchors,
                settings.activeTutorialLessonId,
                settings.tutorialLessonPhase);
        }

        internal static void ObserveInteraction(BWTTutorialInteraction interaction)
        {
            if (!IsActive)
            {
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            string lesson = settings.activeTutorialLessonId;
            bool pawnMenuOpened = interaction.Kind == BWTTutorialInteractionKind.PawnName &&
                interaction.MouseButton == 1;
            if (pawnMenuOpened)
            {
                bool wasComplete = settings.completedTutorialLessonIds.Contains(PawnMenuLesson);
                TutorialProgressTransitions.Complete(settings.completedTutorialLessonIds, PawnMenuLesson);
                settings.skippedTutorialLessonIds.Remove(PawnMenuLesson);
                if (lesson == PawnMenuLesson)
                {
                    CompleteLesson(lesson);
                }
                else if (!wasComplete)
                {
                    settings.Write();
                    PlayTutorialSound("Tick_High");
                }
            }
        }

        internal static void NotifyWorkTabClosed()
        {
            ownsCurrentPointer = false;
            Selector.ClearPinnedSelection();
        }

        internal static void EnsureState(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.completedTutorialLessonIds == null)
            {
                settings.completedTutorialLessonIds = new List<string>();
            }

            settings.skippedTutorialLessonIds ??= new List<string>();
            BWTTutorialFeedbackStore.Ensure(settings);
            if (settings.tutorialWelcomeCompleted &&
                settings.selectedTutorialCourse == BWTTutorialCourse.None &&
                settings.showGeneralTutorial)
            {
                // A manually resumed pre-course tutorial should show the entry
                // choices instead of silently assuming the full course.
                settings.tutorialWelcomeCompleted = false;
            }

            settings.activeTutorialLessonId = settings.activeTutorialLessonId ?? string.Empty;
            settings.tutorialLessonPhase = Mathf.Max(0, settings.tutorialLessonPhase);
        }

        private static IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> BuildHubDefinitions()
        {
            // Repaint events do not reliably retain keyboard modifiers. Read
            // the live key state as well so the Shift explanation remains
            // visible for as long as the player holds the key.
            bool shift = (Event.current?.shift ?? false) ||
                         Input.GetKey(KeyCode.LeftShift) ||
                         Input.GetKey(KeyCode.RightShift);
            bool subWork = SubWorkDrilldownState.HasAnyDrilldown;
            BWTTutorialCourse course = BetterWorkTabMod.Settings?.selectedTutorialCourse ?? BWTTutorialCourse.Full;
            BWTTutorialOptionDefinition[] pawnLessons = BuildOptions(course, TutorialHubAnchor.PawnName);
            BWTTutorialOptionDefinition[] headerLessons = BuildOptions(course, TutorialHubAnchor.WorkHeader);
            BWTTutorialOptionDefinition[] priorityLessons = BuildOptions(course, TutorialHubAnchor.PriorityCell);
            return new Dictionary<TutorialHubAnchor, BWTTutorialHubDefinition>
            {
                [TutorialHubAnchor.None] = new BWTTutorialHubDefinition(
                    TutorialHubAnchor.None,
                    T("BWT_Tutorial_Selector_Title"),
                    T("BWT_Tutorial_Selector_DefaultBody"),
                    new BWTTutorialOptionDefinition[0]),
                [TutorialHubAnchor.PawnName] = new BWTTutorialHubDefinition(
                    TutorialHubAnchor.PawnName,
                    T("BWT_Tutorial_PawnHub_Title"),
                    T("BWT_Tutorial_PawnHub_Body"),
                    pawnLessons),
                [TutorialHubAnchor.WorkHeader] = new BWTTutorialHubDefinition(
                    TutorialHubAnchor.WorkHeader,
                    T("BWT_Tutorial_HeaderHub_Title"),
                    T(subWork ? "BWT_Tutorial_HeaderHub_SubWorkBody" : "BWT_Tutorial_HeaderHub_Body"),
                    headerLessons),
                [TutorialHubAnchor.PriorityCell] = new BWTTutorialHubDefinition(
                    TutorialHubAnchor.PriorityCell,
                    T("BWT_Tutorial_PriorityHub_Title"),
                    T(shift ? "BWT_Tutorial_PriorityHub_ShiftBody" : "BWT_Tutorial_PriorityHub_Body"),
                    priorityLessons)
            };
        }

        private static BWTTutorialOptionDefinition[] BuildOptions(
            BWTTutorialCourse course,
            TutorialHubAnchor anchor)
        {
            return BWTTutorialLessonCatalog.ForCourse(course)
                .Where(lesson => lesson.Anchor == anchor)
                .Select(lesson => new BWTTutorialOptionDefinition(
                    lesson.Id,
                    T("BWT_Tutorial_" + lesson.LocalizationStem + "_Label"),
                    T("BWT_Tutorial_" + lesson.LocalizationStem + "_Title"),
                    T("BWT_Tutorial_" + lesson.LocalizationStem + "_Preview"),
                    lesson.Recommended))
                .ToArray();
        }

        private static void DrawFloatingLesson(
            Rect workBounds,
            IList<BWTTutorialAnchor> localAnchors,
            string lessonId,
            int phase)
        {
            BWTTutorialAnchor localAnchor = ResolveLessonDisplayAnchor(localAnchors, lessonId, phase);
            DrawLessonAnchor(localAnchor);

            Vector2 rootOffset = GUIClipUtility.Unclip(Vector2.zero);
            Rect rootWorkBounds = OffsetRect(workBounds, rootOffset);
            Rect screenBounds = new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight);
            BWTTutorialAnchor rootAnchor = localAnchor.OffsetBy(rootOffset);
            string body = GetLessonBody(lessonId, phase);
            LessonLayout rootLayout = BuildLessonLayout(screenBounds, rootWorkBounds, rootAnchor, body);
            LessonLayout localLayout = rootLayout.OffsetBy(-rootLayout.CardRect.position);

            Find.WindowStack.ImmediateWindow(
                FloatingLessonWindowId,
                rootLayout.CardRect,
                WindowLayer.Super,
                () =>
                {
                    Event evt = Event.current;
                    if (evt != null &&
                        evt.type != EventType.Layout &&
                        evt.type != EventType.Repaint &&
                        localLayout.CardRect.Contains(evt.mousePosition))
                    {
                        if (evt.type == EventType.ScrollWheel && localLayout.BodyRect.Contains(evt.mousePosition))
                        {
                            float maximumScroll = Mathf.Max(
                                0f,
                                localLayout.BodyViewRect.height - localLayout.BodyRect.height);
                            lessonScrollPosition.y = Mathf.Clamp(
                                lessonScrollPosition.y + evt.delta.y * 22f,
                                0f,
                                maximumScroll);
                            evt.Use();
                        }
                        else if (evt.type == EventType.MouseDown && evt.button == 0)
                        {
                            if (localLayout.BackRect.Contains(evt.mousePosition))
                            {
                                ReturnToSelection();
                            }
                            else if (localLayout.SkipRect.Contains(evt.mousePosition))
                            {
                                SkipLesson(lessonId);
                            }
                            else if (localLayout.PauseRect.Contains(evt.mousePosition))
                            {
                                Pause();
                            }

                            evt.Use();
                        }
                        else if (IsPointerEvent(evt.type))
                        {
                            evt.Use();
                        }
                    }

                    DrawLessonCard(localLayout, lessonId, body);
                },
                doBackground: false,
                absorbInputAroundWindow: false,
                shadowAlpha: 0f);
        }

        private static void DrawFloatingSelector(
            Rect inRect,
            Rect workBounds,
            IList<BWTTutorialAnchor> localAnchors,
            IWorkTabLayoutController layout,
            BetterWorkTabSettings settings)
        {
            Vector2 rootOffset = GUIClipUtility.Unclip(Vector2.zero);
            var rootAnchors = new List<BWTTutorialAnchor>(localAnchors.Count);
            for (int i = 0; i < localAnchors.Count; i++)
            {
                rootAnchors.Add(localAnchors[i].OffsetBy(rootOffset));
            }

            Rect rootWorkBounds = OffsetRect(workBounds, rootOffset);
            Rect screenBounds = new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight);
            IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs = BuildHubDefinitions();
            Find.WindowStack.ImmediateWindow(
                FloatingSelectorWindowId,
                screenBounds,
                WindowLayer.Super,
                () =>
                {
                    Event evt = Event.current;
                    if (evt != null && evt.type != EventType.Layout && evt.type != EventType.Repaint)
                    {
                        bool handled = Selector.TryHandleInput(
                            screenBounds,
                            rootWorkBounds,
                            rootAnchors,
                            hubs,
                            settings.completedTutorialLessonIds,
                            evt,
                            T("BWT_Tutorial_Recommended"),
                            T("BWT_Tutorial_SkipForNow"),
                            T("BWT_Tutorial_LeaveTutorial"),
                            out BWTTutorialSelectorAction action);
                        if (handled && action.Exit)
                        {
                            Pause();
                        }
                        else if (handled && action.Leave)
                        {
                            LeaveTutorial();
                        }
                        else if (handled && action.HasLesson)
                        {
                            SelectLesson(action.LessonId, localAnchors, layout);
                        }
                    }

                    Selector.Draw(
                        screenBounds,
                        rootWorkBounds,
                        rootAnchors,
                        hubs,
                        settings.completedTutorialLessonIds,
                        T("BWT_Tutorial_DefaultContextHeading"),
                        T("BWT_Tutorial_Recommended"),
                        T("BWT_Tutorial_SkipForNow"),
                        T("BWT_Tutorial_LeaveTutorial"));
                },
                doBackground: false,
                absorbInputAroundWindow: false,
                shadowAlpha: 0f);
        }

        private static void SelectLesson(
            string lessonId,
            IList<BWTTutorialAnchor> anchors,
            IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            BWTTutorialLessonDefinition definition = BWTTutorialLessonCatalog.Find(lessonId);
            if (definition?.Route == BWTTutorialLessonRoute.PrioritySettings)
            {
                BWTSettingsFocusRequest request = BWTWorkTabContextSettingsRouter.BuildPriorityRangeFocusRequest(
                    settings.priorityMode);
                BWTSettingsContextFocus.Request(request);
                bool opened = MainTabWindow_BetterWork.OpenBetterWorkTabSettings(toggleExisting: false);
                if (TutorialProgressTransitions.ShouldCompleteAction(
                        TutorialActionRequirement.SettingsRouteOpened,
                        priorityChanged: false,
                        skillVisibleSeconds: 0d,
                        scheduleOpened: false,
                        scheduleEdited: false,
                        scheduleClosed: false,
                        routedSettings: opened,
                        observedAction: false))
                {
                    CompleteRoutedLesson(settings, lessonId);
                }
                return;
            }

            if (definition?.Route == BWTTutorialLessonRoute.SettingsDiscovery)
            {
                BWTSettingsContextFocus.Request(new BWTSettingsFocusRequest(
                    T("BWT_Tutorial_SettingsDiscovery_Filter"),
                    T("BWT_Tutorial_SettingsDiscovery_FilterTooltip"),
                    SettingIDs.FeaturesSubWorkJobs,
                    false,
                    new[]
                    {
                        SettingIDs.FeaturesSubWorkJobs,
                        SettingIDs.SubWorkDrilldownStyle,
                        SettingIDs.PriorityModeSetting,
                        SettingIDs.RuleBuilder2Use
                    }));
                if (MainTabWindow_BetterWork.OpenBetterWorkTabSettings(toggleExisting: false))
                {
                    CompleteRoutedLesson(settings, lessonId);
                }
                return;
            }

            if (definition?.Route == BWTTutorialLessonRoute.RuleBuilder2)
            {
                RuleBuilderGateway.OpenRuleBuilder2Tutorial();
                CompleteRoutedLesson(settings, lessonId);
                return;
            }

            if (definition?.Route == BWTTutorialLessonRoute.FluffyCoexistence)
            {
                // The coexistence walkthrough owns the screen while it is
                // open. Otherwise the category selector can overlap its
                // switch/return controls because this routed lesson does not
                // use activeTutorialLessonId.
                settings.showGeneralTutorial = false;
                settings.Write();
                Find.WindowStack.Add(new Window_BWTFluffyTutorial(
                    () =>
                    {
                        settings.showGeneralTutorial = true;
                        CompleteLesson(lessonId);
                    },
                    () =>
                    {
                        settings.showGeneralTutorial = true;
                        settings.Write();
                    }));
                return;
            }

            settings.activeTutorialLessonId = lessonId;
            settings.tutorialLessonPhase = 0;
            lessonScrollPosition = Vector2.zero;
            InitializeLessonObservation(lessonId, anchors, layout);
            settings.Write();
            PlayTutorialSound("Tick_High");
        }

        private static void CompleteRoutedLesson(BetterWorkTabSettings settings, string lessonId)
        {
            TutorialProgressTransitions.Complete(settings.completedTutorialLessonIds, lessonId);
            settings.skippedTutorialLessonIds.Remove(lessonId);
            settings.Write();
            PlayTutorialSound("Tick_High");
            ReviewIfCourseResolved(settings);
        }

        private static void InitializeLessonObservation(
            string lessonId,
            IList<BWTTutorialAnchor> anchors,
            IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            observedLessonId = lessonId ?? string.Empty;
            lessonAnchor = FindLessonAnchor(anchors, GetAnchorForLesson(lessonId));
            initialPriority = GetPriority(lessonAnchor);
            initialScheduleEditRevision = TimePriorityScheduleEditor.TutorialEditRevision;
            initialDividerCount = CountDividers(layout);
            initialHeaderSignature = ComputeHeaderSignature(layout);
            initialHeaderSelectionCount = ColumnSelectionManager.SelectionCount;
            initialHeaderSelected = IsLessonHeaderSelected(layout);
            initialPawnTitle = PawnTitleUtility.GetCurrentTitle(lessonAnchor.Pawn);
            initialPawnHadColor = PawnColorDatabase.TryGetColor(lessonAnchor.Pawn, out initialPawnColor);
            initialSubWorkActive = SubWorkDrilldownState.HasAnyDrilldown;
            scheduleOpened = false;
            scheduleEdited = false;
            skillVisibleStartedAt = -1f;
            if (lessonId == PriorityScheduleLesson)
            {
                if (TimePriorityScheduleEditor.IsVisible)
                {
                    TimePriorityScheduleEditor.CloseForTutorial();
                }

                if (settings != null && settings.tutorialLessonPhase > 0)
                {
                    // Schedule UI is transient across reloads. Resume from the
                    // real opening action if controller observations were lost.
                    settings.tutorialLessonPhase = 0;
                }
            }
        }

        private static void ObserveActionState(IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            string lesson = settings.activeTutorialLessonId;
            if (lesson == PriorityChangeLesson)
            {
                if (TutorialProgressTransitions.ShouldCompleteAction(
                        TutorialActionRequirement.PriorityChanged,
                        priorityChanged: GetPriority(lessonAnchor) != initialPriority,
                        skillVisibleSeconds: 0d,
                        scheduleOpened: false,
                        scheduleEdited: false,
                        scheduleClosed: false,
                        routedSettings: false,
                        observedAction: false))
                {
                    CompleteLesson(lesson);
                }
                return;
            }

            if (lesson == PrioritySkillLesson)
            {
                bool visible = Patch_WorkPriority_DoCell_Unified.WasSkillNumberDrawnRecently(
                    lessonAnchor.Pawn,
                    lessonAnchor.WorkType);
                if (visible)
                {
                    if (skillVisibleStartedAt < 0f)
                    {
                        skillVisibleStartedAt = Time.realtimeSinceStartup;
                    }
                    else if (TutorialProgressTransitions.ShouldCompleteAction(
                                 TutorialActionRequirement.SkillVisibleForInterval,
                                 priorityChanged: false,
                                 skillVisibleSeconds: Time.realtimeSinceStartup - skillVisibleStartedAt,
                                 scheduleOpened: false,
                                 scheduleEdited: false,
                                 scheduleClosed: false,
                                 routedSettings: false,
                                 observedAction: false))
                    {
                        CompleteLesson(lesson);
                    }
                }
                else
                {
                    skillVisibleStartedAt = -1f;
                }
                return;
            }

            if (lesson == PriorityScheduleLesson)
            {
                ObserveScheduleLesson(settings);
                return;
            }

            if (lesson == PawnDividerLesson && CountDividers(layout) > initialDividerCount)
            {
                CompleteLesson(lesson);
                return;
            }

            if (lesson == PawnAppearanceLesson && PawnAppearanceChanged())
            {
                CompleteLesson(lesson);
                return;
            }

            if (lesson == HeaderReorderLesson && ComputeHeaderSignature(layout) != initialHeaderSignature)
            {
                CompleteLesson(lesson);
                return;
            }

            if (lesson == HeaderGroupLesson &&
                (ColumnSelectionManager.SelectionCount != initialHeaderSelectionCount ||
                 IsLessonHeaderSelected(layout) != initialHeaderSelected))
            {
                CompleteLesson(lesson);
                return;
            }

            if (lesson == HeaderSubWorkLesson &&
                !initialSubWorkActive &&
                SubWorkDrilldownState.HasAnyDrilldown)
            {
                CompleteLesson(lesson);
            }
        }

        private static void ObserveScheduleLesson(BetterWorkTabSettings settings)
        {
            switch (settings.tutorialLessonPhase)
            {
                case 0:
                    if (TimePriorityScheduleEditor.IsVisible)
                    {
                        scheduleOpened = true;
                        settings.tutorialLessonPhase = 1;
                        initialScheduleEditRevision = TimePriorityScheduleEditor.TutorialEditRevision;
                        settings.Write();
                    }
                    break;
                case 1:
                    if (TimePriorityScheduleEditor.TutorialEditRevision != initialScheduleEditRevision)
                    {
                        scheduleEdited = true;
                        settings.tutorialLessonPhase = 2;
                        settings.Write();
                    }
                    break;
                case 2:
                    if (TutorialProgressTransitions.ShouldCompleteAction(
                            TutorialActionRequirement.ScheduleOpenedEditedAndClosed,
                            priorityChanged: false,
                            skillVisibleSeconds: 0d,
                            scheduleOpened: scheduleOpened,
                            scheduleEdited: scheduleEdited,
                            scheduleClosed: !TimePriorityScheduleEditor.IsVisible,
                            routedSettings: false,
                            observedAction: false))
                    {
                        CompleteLesson(PriorityScheduleLesson);
                    }
                    break;
            }
        }

        private static BWTTutorialAnchor ResolveLessonDisplayAnchor(
            IList<BWTTutorialAnchor> anchors,
            string lessonId,
            int phase)
        {
            BWTTutorialAnchor anchor = lessonAnchor.IsValid
                ? lessonAnchor
                : FindLessonAnchor(anchors, GetAnchorForLesson(lessonId));
            if (lessonId == PriorityScheduleLesson &&
                phase > 0 &&
                TimePriorityScheduleEditor.TryGetLastPanelRect(out Rect scheduleRect))
            {
                anchor = new BWTTutorialAnchor(
                    TutorialHubAnchor.PriorityCell,
                    scheduleRect.ExpandedBy(4f),
                    lessonAnchor.Pawn,
                    lessonAnchor.WorkType,
                    lessonAnchor.WorkGiver);
            }

            return anchor;
        }

        private static void DrawLessonCard(
            LessonLayout layout,
            string lessonId,
            string body)
        {
            Rect card = layout.CardRect;

            Widgets.DrawBoxSolid(card, new Color(0.055f, 0.062f, 0.07f, 0.98f));
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            GUI.color = new Color(0.55f, 0.47f, 0.28f, 0.95f);
            Widgets.DrawBox(card, 1);

            Rect inner = card.ContractedBy(16f);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.85f, 0.36f, 1f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 32f), GetLessonTitle(lessonId));
            Text.Font = GameFont.Small;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = true;
            GUI.color = new Color(0.9f, 0.91f, 0.9f, 1f);
            float maximumScroll = Mathf.Max(0f, layout.BodyViewRect.height - layout.BodyRect.height);
            lessonScrollPosition.y = Mathf.Clamp(lessonScrollPosition.y, 0f, maximumScroll);
            Widgets.BeginScrollView(layout.BodyRect, ref lessonScrollPosition, layout.BodyViewRect);
            Widgets.Label(new Rect(0f, 0f, layout.BodyViewRect.width, layout.BodyViewRect.height), body);
            Widgets.EndScrollView();

            GUI.color = Color.white;
            DrawButtonLabel(layout.BackRect, T("BWT_Tutorial_BackToMap"));
            DrawButtonLabel(layout.SkipRect, T("BWT_Tutorial_SkipLesson"));
            DrawButtonLabel(layout.PauseRect, T("BWT_Tutorial_NotNow"));
            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private static void DrawLessonAnchor(BWTTutorialAnchor anchor)
        {
            if (!anchor.IsValid)
            {
                return;
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4.2f);
            BWTTutorialAnchorRenderer.DrawOutline(
                anchor,
                new Color(1f, 0.78f, 0.22f, Mathf.Lerp(0.72f, 1f, pulse)),
                3f);
        }

        private static void DrawButtonLabel(Rect rect, string label)
        {
            bool hovered = rect.Contains(Event.current?.mousePosition ?? Vector2.zero);
            Widgets.DrawBoxSolid(rect, hovered
                ? new Color(0.20f, 0.18f, 0.11f, 1f)
                : new Color(0.10f, 0.11f, 0.12f, 1f));
            Color old = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GUI.color = hovered ? new Color(1f, 0.83f, 0.32f, 1f) : Color.white;
            Widgets.DrawBox(rect, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = oldAnchor;
            GUI.color = old;
        }

        private static string GetLessonTitle(string lessonId)
        {
            BWTTutorialLessonDefinition lesson = BWTTutorialLessonCatalog.Find(lessonId);
            return lesson == null
                ? T("BWT_Tutorial_Unknown_Title")
                : T("BWT_Tutorial_" + lesson.LocalizationStem + "_Title");
        }

        private static string GetLessonBody(string lessonId, int phase)
        {
            if (lessonId == PrioritySkillLesson)
            {
                return BWTTutorialUserContext.BuildSkillNumberTutorialBody(lessonAnchor.WorkType);
            }

            if (lessonId == PriorityScheduleLesson)
            {
                return phase == 0
                    ? T("BWT_Tutorial_PrioritySchedule_ActionOpen")
                    : phase == 1
                        ? T("BWT_Tutorial_PrioritySchedule_ActionEdit")
                        : T("BWT_Tutorial_PrioritySchedule_ActionClose");
            }

            BWTTutorialLessonDefinition definition = BWTTutorialLessonCatalog.Find(lessonId);
            return definition == null
                ? T("BWT_Tutorial_Unknown_Action")
                : T("BWT_Tutorial_" + definition.LocalizationStem + "_Action");
        }

        private static TutorialHubAnchor GetAnchorForLesson(string lessonId)
        {
            return BWTTutorialLessonCatalog.Find(lessonId)?.Anchor ?? TutorialHubAnchor.PriorityCell;
        }

        private static BWTTutorialAnchor FindLessonAnchor(
            IList<BWTTutorialAnchor> anchors,
            TutorialHubAnchor kind)
        {
            if (anchors != null)
            {
                for (int i = 0; i < anchors.Count; i++)
                {
                    if (anchors[i].Kind == kind)
                    {
                        return anchors[i];
                    }
                }
            }

            return default(BWTTutorialAnchor);
        }

        private static void RefreshLessonAnchor(IList<BWTTutorialAnchor> anchors)
        {
            TutorialHubAnchor kind = GetAnchorForLesson(BetterWorkTabMod.Settings.activeTutorialLessonId);
            BWTTutorialAnchor fresh = FindLessonAnchor(anchors, kind);
            if (!fresh.IsValid)
            {
                return;
            }

            // Keep the action target stable while refreshing only its live rect.
            lessonAnchor = new BWTTutorialAnchor(
                kind,
                fresh.Rect,
                lessonAnchor.Pawn ?? fresh.Pawn,
                lessonAnchor.WorkType ?? fresh.WorkType,
                lessonAnchor.WorkGiver ?? fresh.WorkGiver,
                fresh.OutlinePoints);
        }

        private static int GetPriority(BWTTutorialAnchor anchor)
        {
            return anchor.Pawn == null || anchor.WorkType == null
                ? int.MinValue
                // The lesson teaches editing the stored priority. A daily
                // schedule may override the effective value at the current
                // hour, so observing that value can miss a successful click.
                : WorkPrioritySystem.GetPriorityForPawnWorkType(anchor.Pawn, anchor.WorkType);
        }

        private static int CountDividers(IWorkTabLayoutController layout)
        {
            int count = 0;
            if (layout?.Rows == null)
            {
                return count;
            }

            for (int i = 0; i < layout.Rows.Count; i++)
            {
                if (layout.Rows[i].Divider != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static int ComputeHeaderSignature(IWorkTabLayoutController layout)
        {
            unchecked
            {
                int hash = 17;
                if (layout?.Columns == null)
                {
                    return hash;
                }

                for (int i = 0; i < layout.Columns.Count; i++)
                {
                    WorkTypeDef workType = layout.Columns[i].Column?.workType;
                    if (workType != null)
                    {
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(workType.defName ?? string.Empty);
                    }
                }

                return hash;
            }
        }

        private static bool IsLessonHeaderSelected(IWorkTabLayoutController layout)
        {
            if (layout?.Columns == null || lessonAnchor.WorkType == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                PawnColumnDef column = layout.Columns[i].Column;
                if (column?.workType == lessonAnchor.WorkType)
                {
                    return ColumnSelectionManager.IsSelected(column);
                }
            }

            return false;
        }

        private static bool PawnAppearanceChanged()
        {
            if (!string.Equals(initialPawnTitle, PawnTitleUtility.GetCurrentTitle(lessonAnchor.Pawn), StringComparison.Ordinal))
            {
                return true;
            }

            bool hasColor = PawnColorDatabase.TryGetColor(lessonAnchor.Pawn, out Color color);
            return hasColor != initialPawnHadColor || (hasColor && color != initialPawnColor);
        }

        private static LessonLayout BuildLessonLayout(
            Rect bounds,
            Rect work,
            BWTTutorialAnchor anchor,
            string body)
        {
            const float width = 440f;
            float cardWidth = Mathf.Min(width, bounds.width - 20f);
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            float bodyWidth = Mathf.Max(100f, cardWidth - 48f);
            float measuredBodyHeight = Text.CalcHeight(body ?? string.Empty, bodyWidth);
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
            float height = Mathf.Clamp(measuredBodyHeight + 146f, 244f, Mathf.Min(360f, bounds.height - 20f));
            float rightSpace = bounds.xMax - work.xMax - 12f;
            bool placeRight = rightSpace >= cardWidth;
            float x = placeRight
                ? work.xMax + 12f
                : anchor.Rect.center.x < bounds.center.x
                    ? bounds.xMax - cardWidth - 10f
                    : bounds.xMin + 10f;
            float y = placeRight
                ? Mathf.Clamp(anchor.Rect.center.y - height / 2f, bounds.yMin + 10f, bounds.yMax - height - 10f)
                : Mathf.Max(bounds.yMin + 10f, work.yMin - height - 12f);
            Rect card = new Rect(x, y, cardWidth, height);
            Rect bodyRect = new Rect(card.x + 16f, card.y + 58f, card.width - 32f, card.height - 122f);
            float viewWidth = Mathf.Max(80f, bodyRect.width - (measuredBodyHeight > bodyRect.height ? 16f : 0f));
            Rect bodyView = new Rect(0f, 0f, viewWidth, Mathf.Max(bodyRect.height, measuredBodyHeight));
            return new LessonLayout(
                card,
                bodyRect,
                bodyView,
                GetBackButtonRect(card),
                GetSkipButtonRect(card),
                GetPauseButtonRect(card));
        }

        private static Rect GetBackButtonRect(Rect card)
        {
            float width = (card.width - 44f) / 3f;
            return new Rect(card.x + 12f, card.yMax - 48f, width, 32f);
        }

        private static Rect GetSkipButtonRect(Rect card)
        {
            float width = (card.width - 44f) / 3f;
            return new Rect(card.x + 16f + width, card.yMax - 48f, width, 32f);
        }

        private static Rect GetPauseButtonRect(Rect card)
        {
            float width = (card.width - 44f) / 3f;
            return new Rect(card.xMax - 12f - width, card.yMax - 48f, width, 32f);
        }

        private static Rect OffsetRect(Rect rect, Vector2 offset)
        {
            return new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);
        }

        private static void CompleteLesson(string lessonId)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            TutorialProgressTransitions.Complete(settings.completedTutorialLessonIds, lessonId);
            settings.skippedTutorialLessonIds.Remove(lessonId);
            TutorialProgressTransitions.ReturnToSelection(
                ref settings.activeTutorialLessonId,
                ref settings.tutorialLessonPhase);
            settings.Write();
            lessonAnchor = default(BWTTutorialAnchor);
            observedLessonId = string.Empty;
            lessonScrollPosition = Vector2.zero;
            Selector.Reset();
            PlayTutorialSound("Tick_High");
            ReviewIfCourseResolved(settings);
        }

        private static void SkipLesson(string lessonId)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (!settings.completedTutorialLessonIds.Contains(lessonId) &&
                !settings.skippedTutorialLessonIds.Contains(lessonId))
            {
                settings.skippedTutorialLessonIds.Add(lessonId);
            }
            TutorialProgressTransitions.ReturnToSelection(
                ref settings.activeTutorialLessonId,
                ref settings.tutorialLessonPhase);
            settings.Write();
            lessonAnchor = default(BWTTutorialAnchor);
            observedLessonId = string.Empty;
            lessonScrollPosition = Vector2.zero;
            Selector.Reset();
            PlayTutorialSound("Tick_Low");
            ReviewIfCourseResolved(settings);
        }

        private static void ReturnToSelection()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings.activeTutorialLessonId == PriorityScheduleLesson)
            {
                TimePriorityScheduleEditor.CloseForTutorial();
            }

            TutorialProgressTransitions.ReturnToSelection(
                ref settings.activeTutorialLessonId,
                ref settings.tutorialLessonPhase);
            settings.Write();
            lessonAnchor = default(BWTTutorialAnchor);
            observedLessonId = string.Empty;
            lessonScrollPosition = Vector2.zero;
            Selector.Reset();
            PlayTutorialSound("Tick_Low");
        }

        private static void Pause()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings.showGeneralTutorial = false;
            settings.Write();
            WelcomeOverlay.ResetAnimation();
            lessonScrollPosition = Vector2.zero;
            Selector.Reset();
            PlayTutorialSound("Tick_Low");
        }

        private static void LeaveTutorial()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            TutorialProgressTransitions.ReturnToSelection(
                ref settings.activeTutorialLessonId,
                ref settings.tutorialLessonPhase);
            Pause();
            OpenReview();
        }

        private static TutorialOverlayContent BuildWelcomeContent()
        {
            return new TutorialOverlayContent(
                T("BWT_Tutorial_Welcome_Title"),
                T("BWT_Tutorial_Welcome_Body"),
                primaryButton: T("BWT_Tutorial_Welcome_WhatsNew"),
                dismissButton: T("BWT_Tutorial_NotNow"),
                secondaryButton: T("BWT_Tutorial_Welcome_Full"));
        }

        private static void StartWhatsNewCourse()
        {
            StartCourse(BWTTutorialCourse.WhatsNew20);
        }

        private static void StartFullCourse()
        {
            StartCourse(BWTTutorialCourse.Full);
        }

        private static void StartCourse(BWTTutorialCourse course)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            bool firstPublic105Course = settings.tutorialMigratedFromPublic105 &&
                settings.selectedTutorialCourse == BWTTutorialCourse.None;
            if (firstPublic105Course)
            {
                BWT20SettingsMigration.EnablePublic20TutorialFeatures(settings);
            }

            settings.selectedTutorialCourse = course;
            settings.tutorialWelcomeCompleted = true;
            settings.showGeneralTutorial = true;
            settings.tutorialFlowVersion = CurrentFlowVersion;
            WelcomeOverlay.ResetAnimation();
            Selector.Reset();
            settings.Write();
            PlayTutorialSound("Tick_High");
        }

        private static void ReviewIfCourseResolved(BetterWorkTabSettings settings)
        {
            bool resolved = BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse).All(
                lesson => settings.completedTutorialLessonIds.Contains(lesson.Id) ||
                          settings.skippedTutorialLessonIds.Contains(lesson.Id));
            if (!resolved)
            {
                return;
            }

            settings.showGeneralTutorial = false;
            settings.Write();
            OpenReview();
        }

        private static void OpenReview()
        {
            if (Find.WindowStack != null && !Find.WindowStack.IsOpen<Window_BWTTutorialReview>())
            {
                Find.WindowStack.Add(new Window_BWTTutorialReview());
            }
        }

        private static void DismissWelcome()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            TutorialProgressTransitions.ResolveWelcomeChoice(
                ref settings.tutorialWelcomeCompleted,
                ref settings.showGeneralTutorial,
                continueWalkthrough: false);
            settings.tutorialFlowVersion = CurrentFlowVersion;
            WelcomeOverlay.ResetAnimation();
            Selector.Reset();
            settings.Write();
            PlayTutorialSound("Tick_Low");
        }

        private static void PlayTutorialSound(string defName)
        {
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail(defName);
            sound?.PlayOneShotOnCamera();
        }

        private static string T(string key)
        {
            return key.Translate();
        }

        private readonly struct LessonLayout
        {
            internal LessonLayout(
                Rect cardRect,
                Rect bodyRect,
                Rect bodyViewRect,
                Rect backRect,
                Rect skipRect,
                Rect pauseRect)
            {
                CardRect = cardRect;
                BodyRect = bodyRect;
                BodyViewRect = bodyViewRect;
                BackRect = backRect;
                SkipRect = skipRect;
                PauseRect = pauseRect;
            }

            internal Rect CardRect { get; }
            internal Rect BodyRect { get; }
            internal Rect BodyViewRect { get; }
            internal Rect BackRect { get; }
            internal Rect SkipRect { get; }
            internal Rect PauseRect { get; }

            internal LessonLayout OffsetBy(Vector2 offset)
            {
                return new LessonLayout(
                    OffsetRect(CardRect, offset),
                    OffsetRect(BodyRect, offset),
                    BodyViewRect,
                    OffsetRect(BackRect, offset),
                    OffsetRect(SkipRect, offset),
                    OffsetRect(PauseRect, offset));
            }
        }
    }
}
