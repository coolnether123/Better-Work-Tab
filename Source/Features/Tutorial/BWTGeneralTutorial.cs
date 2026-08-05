using System;
using System.Globalization;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.Feedback;
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
    /// The tutorial's own surfaces, declared bottom to top.
    ///
    /// IMGUI has no z-order: what is "on top" is whatever painted last, and
    /// nothing checks that input is resolved in the opposite order. Declaring the
    /// stack once and deriving both walks from it is what keeps the two from
    /// drifting. They did drift: the band paints first and the list paints over
    /// it, but input asked the band first, so every list row crossing the band's
    /// lane was swallowed and its lesson silently never opened.
    ///
    /// Band is painted with the Work tab's other pinned bands, before the
    /// headers. Anchors and Popup are painted afterwards by
    /// <see cref="BWTGeneralTutorial.TickAndDraw"/>, in that order.
    /// </summary>
    internal enum BWTTutorialSurface
    {
        Band,
        Anchors,
        Popup
    }

    /// <summary>
    /// The single Work-tab tutorial owner. Selection, progress, and action
    /// verification live here; work, schedule, and settings systems remain the
    /// authoritative owners of the actions being taught.
    /// </summary>
    internal static class BWTGeneralTutorial
    {
        /// <summary>
        /// Bottom-to-top paint order, taken straight from the enum declaration so
        /// there is no second ordering to keep in step. Hit testing walks it
        /// backwards, which is the only place the inverse relationship is stated.
        /// </summary>
        private static readonly BWTTutorialSurface[] PaintOrder =
            (BWTTutorialSurface[])Enum.GetValues(typeof(BWTTutorialSurface));

        internal const int CurrentFlowVersion = 5;
        internal const string PrioritySkillLesson = BWTTutorialLessonCatalog.PrioritySkill;
        internal const string PriorityScheduleLesson = BWTTutorialLessonCatalog.PrioritySchedule;
        internal const string PriorityRangeLesson = BWTTutorialLessonCatalog.PriorityRange;
        internal const string PawnMenuLesson = BWTTutorialLessonCatalog.PawnMenu;
        internal const string PawnDividerLesson = BWTTutorialLessonCatalog.PawnDivider;
        internal const string PawnAppearanceLesson = BWTTutorialLessonCatalog.PawnAppearance;
        internal const string HeaderReorderLesson = BWTTutorialLessonCatalog.HeaderReorder;
        internal const string HeaderGroupLesson = BWTTutorialLessonCatalog.HeaderGroup;
        internal const string HeaderSubWorkLesson = BWTTutorialLessonCatalog.HeaderSubWork;
        internal const string RuleBuilder2Lesson = BWTTutorialLessonCatalog.RuleBuilder2;
        internal const string FluffyCoexistenceLesson = BWTTutorialLessonCatalog.FluffyCoexistence;

        private static readonly BWTTutorialSelector Selector = new BWTTutorialSelector();
        private static readonly TutorialOverlayController WelcomeOverlay = new TutorialOverlayController(
            new TutorialOverlayStyle
            {
                CardWidth = 480f,
                CardPadding = 12f,
                DimColor = new Color(0f, 0f, 0f, 0.10f),
                LayoutAnimationSeconds = 0.2f
            });
        private static readonly List<Rect> NoWelcomeFocusRects = new List<Rect>();
        private static readonly List<TutorialOverlayShortcutHint> NoWelcomeShortcutHints =
            new List<TutorialOverlayShortcutHint>();
        private static BWTTutorialAnchor lessonAnchor;
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
        private static bool lessonAnchorIsLive;
        private static bool ownsCurrentPointer;
        private const float WorkTabVerticalChrome = 6f;
        private const int CompletionOutcomePhase = 1000;

        internal static bool OwnsCurrentPointer => ownsCurrentPointer;

        /// <summary>
        /// Puts the tutorial back to what a player sees the first time they open
        /// the Work tab.
        ///
        /// Restoring defaults turns the tutorial back on, and turning it on
        /// without clearing where it had got to resumed it mid-course — landing
        /// on whichever lesson was open, still counting the lessons already
        /// finished. "Restore defaults" has to mean the tutorial starts at the
        /// beginning, not that it is switched on wherever it was left.
        ///
        /// Progress is state, so it is cleared. Feedback the tester has written
        /// is not: their words are not a preference to be reset, and the
        /// feedback portal has its own way to clear them.
        /// </summary>
        internal static void ResetToFirstRun(BetterWorkTabSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.tutorialWelcomeCompleted = false;
            settings.activeTutorialLessonId = string.Empty;
            settings.tutorialLessonPhase = 0;
            settings.selectedTutorialCourse = BWTTutorialCourse.None;
            settings.completedTutorialLessonIds?.Clear();
            settings.skippedTutorialLessonIds?.Clear();

            // Stamped current rather than zeroed: a fresh start is not an
            // upgrade, and leaving it behind would send this player down the
            // migration path built for someone arriving from an older flow.
            settings.tutorialFlowVersion = CurrentFlowVersion;

            lessonAnchor = default(BWTTutorialAnchor);
            lessonAnchorIsLive = false;
            WelcomeOverlay.ResetAnimation();
        }

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

        internal static bool OpenLessonFromReview(string lessonId)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            BWTTutorialLessonDefinition definition = BWTTutorialLessonCatalog.Find(lessonId);
            BWTTutorialCourse course = settings?.selectedTutorialCourse ?? BWTTutorialCourse.None;
            if (settings == null || definition == null || !definition.BelongsTo(course))
            {
                return false;
            }

            MainButtonDef workTab = Better_Work_Tab.Patches.MainButtonDefOf.Work ??
                                    DefDatabase<MainButtonDef>.GetNamedSilentFail("Work");
            if (workTab == null || Find.MainTabsRoot == null)
            {
                return false;
            }

            // The review is reachable from mod settings, and that dialog outlives
            // it. Leaving it open meant "Go to tutorial" switched the tab behind a
            // settings window the player was still looking at — and the tutorial
            // suppresses itself while mod settings are open, so nothing drew even
            // once they closed it by hand. Lessons that genuinely live in settings
            // reopen it themselves further down.
            Find.WindowStack.TryRemove(typeof(Dialog_ModSettings), false);

            FluffyWorkTabCoexistence.SwitchToBetterWorkTab();
            Find.MainTabsRoot.SetCurrentTab(workTab, false);
            if (!(Find.MainTabsRoot.OpenTab?.TabWindow is MainTabWindow_BetterWork))
            {
                return false;
            }

            if (course == BWTTutorialCourse.None)
            {
                settings.selectedTutorialCourse = BWTTutorialCourse.Full;
            }
            settings.tutorialWelcomeCompleted = true;
            settings.showGeneralTutorial = true;
            settings.tutorialFlowVersion = CurrentFlowVersion;
            settings.activeTutorialLessonId = string.Empty;
            settings.tutorialLessonPhase = 0;
            lessonAnchor = default(BWTTutorialAnchor);
            observedLessonId = string.Empty;
            WelcomeOverlay.ResetAnimation();
            Selector.Reset();

            if (definition.Route == BWTTutorialLessonRoute.WorkTab)
            {
                settings.activeTutorialLessonId = lessonId;
                settings.Write();
                PlayTutorialSound("Tick_High");
            }
            else
            {
                settings.Write();
                SelectLesson(lessonId, null, null);
            }

            return true;
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
                    if (IsShowingCompletionOutcome(settings.activeTutorialLessonId))
                    {
                        AcknowledgeLessonOutcome();
                    }
                    else
                    {
                        ReturnToSelection();
                    }
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
                    StartRecommendedCourse,
                    null,
                    null,
                    DismissWelcome);
            }

            // Clicks go through the one ordered walk shared with the harness, so
            // there is a single statement of which surface outranks which.
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (!TryHandlePrimaryClick(inRect, layout, evt.mousePosition))
                {
                    return false;
                }

                evt.Use();
                return true;
            }

            // Everything else the tutorial claims is the list holding the pointer
            // during its hover grace, so the grid underneath stops drawing
            // highlights and tooltips through it.
            if (presentation == TutorialPresentation.Selector &&
                BWTTutorialSelector.IsPointerEvent(evt.type) &&
                Selector.ContainsPointer(
                    GetWorkTabAttachmentBounds(inRect, Vector2.zero),
                    BWTTutorialGeometry.BuildInitialAnchors(inRect, layout),
                    BuildHubDefinitions(),
                    evt.mousePosition))
            {
                evt.Use();
                return true;
            }

            return false;
        }


        /// <summary>
        /// Reports what the tutorial is showing and where, in Work-tab local
        /// coordinates, so a harness can drive it by name and assert geometry
        /// without reading pixels.
        /// </summary>
        internal static void DescribeState(
            Rect inRect,
            IWorkTabLayoutController layout,
            StringBuilder report)
        {
            if (report == null)
            {
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            report.AppendLine("active=" + IsActive);
            if (!IsActive || settings == null)
            {
                return;
            }

            EnsureState(settings);
            TutorialPresentation presentation = Presentation;
            report.AppendLine("presentation=" + presentation);
            report.AppendLine("lesson=" + (settings.activeTutorialLessonId ?? string.Empty));
            report.AppendLine("lessonPhase=" + settings.tutorialLessonPhase);
            report.AppendLine("course=" + settings.selectedTutorialCourse);
            report.AppendLine("completed=" + string.Join(",", settings.completedTutorialLessonIds.ToArray()));

            BWTTutorialStripContent content = BuildStripContent(settings, presentation);
            report.AppendLine("bandMode=" + content.Mode);
            report.AppendLine("bandProgress=" + content.CompletedCount + "/" + content.TotalCount);
            report.AppendLine("bandInstruction=" + content.Instruction);
            BWTTutorialStripLayout stripLayout = BWTTutorialStrip.BuildLayout(layout, content);
            report.AppendLine("bandValid=" + stripLayout.IsValid);
            report.AppendLine("bandRect=" + Describe(stripLayout.StripRect));
            report.AppendLine("bandSkipRect=" + Describe(stripLayout.SkipRect));
            report.AppendLine("bandExitRect=" + Describe(stripLayout.ExitRect));

            List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
            report.AppendLine("anchorCount=" + anchors.Count);
            for (int i = 0; i < anchors.Count; i++)
            {
                // Rect is only a bounding box. An angled header's outline is a
                // rotated quad whose box also spans the stem, so the box centre
                // can sit outside the shape Contains() actually tests. Report an
                // interior point too, or a harness aiming at the centre misses.
                Vector2 hit = ResolveAnchorHitPoint(anchors[i]);
                report.AppendLine("anchor." + i + "=" + anchors[i].Kind + " " + Describe(anchors[i].Rect) +
                    " hitx=" + hit.x.ToString("0.#", CultureInfo.InvariantCulture) +
                    " hity=" + hit.y.ToString("0.#", CultureInfo.InvariantCulture) +
                    " hitInside=" + anchors[i].Contains(hit));
            }

            if (Selector.TryDescribePopup(
                    GetWorkTabAttachmentBounds(inRect, Vector2.zero),
                    anchors,
                    BuildHubDefinitions(),
                    out BWTTutorialHubDefinition hub,
                    out Rect popupRect,
                    out IList<Rect> optionRects))
            {
                report.AppendLine("popupOpen=true");
                report.AppendLine("popupAnchor=" + Selector.PinnedAnchor);
                report.AppendLine("popupRect=" + Describe(popupRect));
                for (int i = 0; i < hub.Options.Count && i < optionRects.Count; i++)
                {
                    report.AppendLine(
                        "popupOption." + i + "=" + hub.Options[i].LessonId + " " + Describe(optionRects[i]));
                }
            }
            else
            {
                report.AppendLine("popupOpen=false");
            }
        }

        /// <summary>
        /// A point inside the anchor's clickable shape: the outline's centroid
        /// when it has one, otherwise the rect centre.
        /// </summary>
        private static Vector2 ResolveAnchorHitPoint(BWTTutorialAnchor anchor)
        {
            if (!anchor.HasCustomOutline)
            {
                return anchor.Rect.center;
            }

            Vector2 total = Vector2.zero;
            for (int i = 0; i < anchor.OutlinePoints.Count; i++)
            {
                total += anchor.OutlinePoints[i];
            }

            return total / anchor.OutlinePoints.Count;
        }

        private static string Describe(Rect rect)
        {
            return "x=" + rect.x.ToString("0.#", CultureInfo.InvariantCulture) +
                " y=" + rect.y.ToString("0.#", CultureInfo.InvariantCulture) +
                " w=" + rect.width.ToString("0.#", CultureInfo.InvariantCulture) +
                " h=" + rect.height.ToString("0.#", CultureInfo.InvariantCulture) +
                " cx=" + rect.center.x.ToString("0.#", CultureInfo.InvariantCulture) +
                " cy=" + rect.center.y.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Resolves a left click at a Work-tab local point through the same band,
        /// popup and anchor hit testing a real click takes, without going through
        /// an <see cref="Event"/>.
        ///
        /// Unity reports a synthesised mouse event's <c>type</c> as Ignore during
        /// a Repaint pass, so a harness cannot deliver anything the event-keyed
        /// path will accept. Driving the same resolution from a point keeps the
        /// geometry and the handlers under test and skips only Unity's dispatch.
        /// </summary>
        internal static bool TryHandlePrimaryClick(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 point)
        {
            if (!IsActive)
            {
                return false;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            EnsureState(settings);
            TutorialPresentation presentation = Presentation;
            if (presentation == TutorialPresentation.Welcome)
            {
                return false;
            }

            List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);

            // Backwards through the paint order: the surface drawn last gets
            // first refusal, so whatever the player can see on top is what their
            // click reaches.
            for (int i = PaintOrder.Length - 1; i >= 0; i--)
            {
                if (TryHandleSurfaceClick(PaintOrder[i], inRect, layout, anchors, presentation, point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryHandleSurfaceClick(
            BWTTutorialSurface surface,
            Rect inRect,
            IWorkTabLayoutController layout,
            IList<BWTTutorialAnchor> anchors,
            TutorialPresentation presentation,
            Vector2 point)
        {
            switch (surface)
            {
                case BWTTutorialSurface.Popup:
                    if (presentation != TutorialPresentation.Selector ||
                        !Selector.TryHandlePopupClick(
                            GetWorkTabAttachmentBounds(inRect, Vector2.zero),
                            anchors,
                            BuildHubDefinitions(),
                            point,
                            out BWTTutorialSelectorAction action))
                    {
                        return false;
                    }

                    if (action.HasLesson)
                    {
                        SelectLesson(action.LessonId, anchors, layout);
                    }

                    return true;

                case BWTTutorialSurface.Anchors:
                    // Anchors are only live while browsing; a lesson pins its own.
                    // A miss here also dismisses the open list, and reports it as
                    // unhandled so the click still reaches the grid beneath.
                    return presentation == TutorialPresentation.Selector &&
                           (BWTTutorialGeometry.TryResolveAnchorAt(inRect, layout, point, out BWTTutorialAnchor selected)
                               ? Selector.TrySelectAnchorAt(new[] { selected }, point)
                               : Selector.TrySelectAnchorAt(anchors, point));

                case BWTTutorialSurface.Band:
                    return TryHandleStripClick(layout, point);

                default:
                    return false;
            }
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
            else if (presentation == TutorialPresentation.Lesson)
            {
                overSurface = StripContainsPointer(layout, pointer);
            }
            else
            {
                List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
                overSurface = StripContainsPointer(layout, pointer) ||
                    Selector.ContainsPointer(
                        GetWorkTabAttachmentBounds(inRect, Vector2.zero),
                        anchors,
                        BuildHubDefinitions(),
                        pointer);
            }

            return TutorialPointerOwnershipPolicy.BlocksUnderlyingPointer(presentation, overSurface);
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
                return WelcomeOverlay.TryHandleAcceptKey(BuildWelcomeContent(), StartRecommendedCourse);
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
                    StartRecommendedCourse,
                    null,
                    null,
                    DismissWelcome);
                return;
            }

            List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
            if (presentation == TutorialPresentation.Selector)
            {
                // The band itself has already been drawn with the Work tab's
                // other pinned bands; only the anchor overlay belongs up here.
                DrawSelectorSurface(inRect, anchors, settings);
                return;
            }

            if (!string.Equals(observedLessonId, settings.activeTutorialLessonId, StringComparison.Ordinal))
            {
                InitializeLessonObservation(settings.activeTutorialLessonId, anchors, layout);
            }
            RefreshLessonAnchor(anchors);
            if (!IsShowingCompletionOutcome(settings.activeTutorialLessonId))
            {
                ObserveActionState(layout);
            }
            if (!IsActive || string.IsNullOrEmpty(settings.activeTutorialLessonId))
            {
                return;
            }

            DrawLessonSurface(
                anchors,
                layout,
                settings.activeTutorialLessonId,
                settings.tutorialLessonPhase);
        }

        /// <summary>
        /// Latches whether the tutorial band claims Work-tab height this frame.
        /// </summary>
        internal static void RefreshStripReservation()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (!IsActive || settings == null)
            {
                BWTTutorialStrip.RefreshReservation(false);
                return;
            }

            EnsureState(settings);
            TutorialPresentation presentation = Presentation;
            BWTTutorialStrip.RefreshReservation(
                presentation == TutorialPresentation.Lesson ||
                presentation == TutorialPresentation.Selector);
        }

        private static BWTTutorialStripContent BuildStripContent(
            BetterWorkTabSettings settings,
            TutorialPresentation presentation)
        {
            if (settings == null)
            {
                return default(BWTTutorialStripContent);
            }

            CountCourseProgress(settings, out int completed, out int total);
            if (presentation == TutorialPresentation.Lesson)
            {
                string lessonId = settings.activeTutorialLessonId;
                bool complete = IsShowingCompletionOutcome(lessonId);
                return new BWTTutorialStripContent(
                    complete ? BWTTutorialStripMode.Complete : BWTTutorialStripMode.Lesson,
                    GetLessonBody(lessonId, settings.tutorialLessonPhase),
                    completed,
                    total);
            }

            if (presentation == TutorialPresentation.Selector)
            {
                return new BWTTutorialStripContent(
                    BWTTutorialStripMode.Browse,
                    T("BWT_Tutorial_Selector_DefaultBody"),
                    completed,
                    total);
            }

            return default(BWTTutorialStripContent);
        }

        private static void CountCourseProgress(
            BetterWorkTabSettings settings,
            out int completed,
            out int total)
        {
            completed = 0;
            total = 0;
            foreach (BWTTutorialLessonDefinition lesson in
                BWTTutorialLessonCatalog.ForCourse(settings.selectedTutorialCourse))
            {
                total++;
                if (TutorialProgressTransitions.IsCompleted(
                        settings.completedTutorialLessonIds,
                        lesson.Id))
                {
                    completed++;
                }
            }
        }

        /// <summary>
        /// Draws the tutorial band in the Work tab's pinned lane. Called from the
        /// band draw phase so anything the grid paints afterwards — drag guides,
        /// row overlays — lands on top of it, as it would over any divider.
        /// </summary>
        internal static void DrawBand(IWorkTabLayoutController layout)
        {
            if (!BWTTutorialStrip.IsReserved || !IsActive)
            {
                return;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            BWTTutorialStripContent content = BuildStripContent(settings, Presentation);
            BWTTutorialStrip.Draw(BWTTutorialStrip.BuildLayout(layout, content), content);
        }

        /// <summary>
        /// The band claims clicks anywhere inside itself, not just on its buttons,
        /// so a stray click never falls through to the grid underneath it.
        /// </summary>
        private static bool TryHandleStripClick(IWorkTabLayoutController layout, Vector2 point)
        {
            if (!BWTTutorialStrip.IsReserved)
            {
                return false;
            }

            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            BWTTutorialStripContent content = BuildStripContent(settings, Presentation);
            BWTTutorialStripLayout stripLayout = BWTTutorialStrip.BuildLayout(layout, content);
            if (!stripLayout.IsValid || !stripLayout.StripRect.Contains(point))
            {
                return false;
            }

            if (stripLayout.SkipRect.width > 1f && stripLayout.SkipRect.Contains(point))
            {
                if (content.Mode == BWTTutorialStripMode.Complete)
                {
                    AcknowledgeLessonOutcome();
                }
                else if (content.Mode == BWTTutorialStripMode.Browse)
                {
                    // Feedback is a side trip, not an exit. This used to pause the
                    // tutorial on the way to the review, so a player who wanted to
                    // comment on one lesson lost the whole tour and had to find
                    // the setting again to get it back.
                    OpenReview();
                }
                else
                {
                    SkipLesson(settings.activeTutorialLessonId);
                }
            }
            else if (stripLayout.ExitRect.width > 1f && stripLayout.ExitRect.Contains(point))
            {
                if (content.Mode == BWTTutorialStripMode.Lesson)
                {
                    ReturnToSelection();
                }
                else
                {
                    Pause();
                }
            }

            return true;
        }

        private static bool StripContainsPointer(IWorkTabLayoutController layout, Vector2 pointer)
        {
            if (!BWTTutorialStrip.IsReserved)
            {
                return false;
            }

            BWTTutorialStripLayout stripLayout = BWTTutorialStrip.BuildLayout(
                layout,
                BuildStripContent(BetterWorkTabMod.Settings, Presentation));
            return stripLayout.IsValid && stripLayout.StripRect.Contains(pointer);
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
                else if ((lesson == PawnDividerLesson || lesson == PawnAppearanceLesson) &&
                         settings.tutorialLessonPhase == 0)
                {
                    AdvanceLessonPhase(1);
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
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings?.activeTutorialLessonId == PrioritySkillLesson &&
                settings.tutorialLessonPhase == 1)
            {
                // A held-key lesson is meaningful only while its effect is visible.
                // Do not carry a transient modifier phase across a closed Work tab.
                settings.tutorialLessonPhase = 0;
                skillVisibleStartedAt = -1f;
            }

            ownsCurrentPointer = false;
            Selector.ClearPinnedSelection();
            BWTTutorialGestureDemo.Reset();
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

            // Settings are reachable while a lesson is on screen, so the feature
            // being taught can be switched off mid-lesson. Withdrawing it from the
            // catalog is not enough once it is already open: the instruction would
            // sit there asking for a gesture that no longer does anything. Hand the
            // player back to the list instead.
            if (settings.activeTutorialLessonId.Length > 0)
            {
                BWTTutorialLessonDefinition active =
                    BWTTutorialLessonCatalog.Find(settings.activeTutorialLessonId);
                if (active != null && !active.BelongsTo(settings.selectedTutorialCourse))
                {
                    TutorialProgressTransitions.ReturnToSelection(
                        ref settings.activeTutorialLessonId,
                        ref settings.tutorialLessonPhase);
                    lessonAnchor = default(BWTTutorialAnchor);
                    observedLessonId = string.Empty;
                }
            }
        }

        private static IDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> BuildHubDefinitions()
        {
            // Repaint events do not reliably retain keyboard modifiers. Read
            // the live key state as well so the Shift explanation remains
            // visible for as long as the player holds the key.
            bool shift = ShiftHelper.IsHeld;
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
                    T("BWT_Tutorial_" + lesson.LocalizationStem + "_Preview")))
                .ToArray();
        }

        private static void DrawLessonSurface(
            IList<BWTTutorialAnchor> localAnchors,
            IWorkTabLayoutController layout,
            string lessonId,
            int phase)
        {
            BWTTutorialAnchor localAnchor = ResolveLessonDisplayAnchor(localAnchors, lessonId, phase);
            bool showingCompletion = phase == CompletionOutcomePhase;
            bool skillNumbersAreBeingExplained =
                lessonId == PrioritySkillLesson && phase != 0;

            // Completion is reported by the strip a few pixels below the anchor.
            // Nothing has to fly across the tab to connect them any more.
            if (showingCompletion)
            {
                return;
            }

            // Never point at a target that is not currently on screen. The strip
            // keeps stating the action, so the lesson stays resumable once the
            // player returns to a view where the target exists.
            if (!lessonAnchorIsLive)
            {
                return;
            }

            if (!skillNumbersAreBeingExplained)
            {
                DrawLessonAnchor(localAnchor);
            }

            BWTTutorialGestureDemo.Draw(lessonId, phase, localAnchor, layout);
        }

        /// <summary>
        /// Draws the anchor outlines and the attached lesson list inside the Work
        /// tab's own pass.
        ///
        /// This used to be a full-screen ImmediateWindow on WindowLayer.Super.
        /// RimWorld resolves a click through WindowStack.GetWindowAt, and a
        /// screen-sized window answers that for every point on screen, so the Work
        /// tab stopped receiving mouse-downs entirely while browsing — which left
        /// every control in the tutorial band dead. Drawing here keeps the popup
        /// inside the window that owns the input.
        /// </summary>
        private static void DrawSelectorSurface(
            Rect inRect,
            IList<BWTTutorialAnchor> localAnchors,
            BetterWorkTabSettings settings)
        {
            Selector.Draw(
                GetWorkTabAttachmentBounds(inRect, Vector2.zero),
                localAnchors,
                BuildHubDefinitions(),
                settings.completedTutorialLessonIds);
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
                BeginRoutedLesson(settings, lessonId);
                BWTSettingsFocusRequest request = BWTWorkTabContextSettingsRouter.BuildPriorityRangeFocusRequest(
                    settings.priorityMode);
                BWTSettingsContextFocus.Request(request);
                if (!MainTabWindow_BetterWork.OpenBetterWorkTabSettings(toggleExisting: false))
                {
                    ReturnToSelection();
                }
                return;
            }

            if (definition?.Route == BWTTutorialLessonRoute.RuleBuilder2)
            {
                BeginRoutedLesson(settings, lessonId);
                RuleBuilderGateway.OpenRuleBuilder2Tutorial();
                return;
            }

            if (definition?.Route == BWTTutorialLessonRoute.FluffyCoexistence)
            {
                BeginRoutedLesson(settings, lessonId);
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
                        ReturnToSelection();
                    }));
                return;
            }

            settings.activeTutorialLessonId = lessonId;
            settings.tutorialLessonPhase = 0;
            InitializeLessonObservation(lessonId, anchors, layout);
            settings.Write();
            PlayTutorialSound("Tick_High");
        }

        private static void BeginRoutedLesson(BetterWorkTabSettings settings, string lessonId)
        {
            settings.activeTutorialLessonId = lessonId;
            settings.tutorialLessonPhase = 0;
            observedLessonId = lessonId ?? string.Empty;
            BWTTutorialGestureDemo.Reset();
            settings.Write();
            PlayTutorialSound("Tick_High");
        }

        internal static bool IsActiveLesson(string lessonId, out int phase)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            phase = settings?.tutorialLessonPhase ?? 0;
            return settings != null &&
                   string.Equals(settings.activeTutorialLessonId, lessonId, StringComparison.Ordinal);
        }

        internal static void DrawSettingsGesture(Func<string, Rect?> resolveSettingRect)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            string lessonId = settings?.activeTutorialLessonId;
            int phase = settings?.tutorialLessonPhase ?? 0;
            if (phase == CompletionOutcomePhase || lessonId != PriorityRangeLesson)
            {
                return;
            }

            string targetSettingId = BWTWorkTabContextSettingsRouter
                .BuildPriorityRangeFocusRequest(settings.priorityMode)
                .TargetSettingId;
            Rect? target = string.IsNullOrEmpty(targetSettingId)
                ? null
                : resolveSettingRect?.Invoke(targetSettingId);
            if (target.HasValue)
            {
                BWTTutorialGestureDemo.DrawExternal(
                    lessonId + ":setting:" + phase,
                    target.Value,
                    BWTTutorialGestureDemo.GestureKind.LeftClick,
                    T("BWT_Tutorial_Gesture_SettingPrompt"));
            }
        }

        internal static void NotifySettingsRowInteracted(string settingId)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings?.activeTutorialLessonId != PriorityRangeLesson)
            {
                return;
            }

            BWTSettingsFocusRequest request = BWTWorkTabContextSettingsRouter
                .BuildPriorityRangeFocusRequest(settings.priorityMode);
            if (request.SettingIds.Contains(settingId, StringComparer.OrdinalIgnoreCase))
            {
                CompleteLesson(PriorityRangeLesson);
            }
        }

        internal static void NotifyPawnAppearanceMenuOptionChosen()
        {
            if (IsActiveLesson(PawnAppearanceLesson, out int phase) && phase == 1)
            {
                AdvanceLessonPhase(2);
            }
        }

        internal static void NotifyPawnTitleEdited()
        {
            if (IsActiveLesson(PawnAppearanceLesson, out int phase) && phase == 2)
            {
                AdvanceLessonPhase(3);
            }
        }

        internal static void NotifyRuleBuilderTutorialCompleted()
        {
            if (IsActiveLesson(RuleBuilder2Lesson, out _))
            {
                CompleteLesson(RuleBuilder2Lesson);
            }
        }

        private static void AdvanceLessonPhase(int phase)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || settings.tutorialLessonPhase == phase)
            {
                return;
            }

            settings.tutorialLessonPhase = phase;
            settings.Write();
            BWTTutorialGestureDemo.Reset();
            PlayTutorialSound("Tick_High");
        }

        private static void InitializeLessonObservation(
            string lessonId,
            IList<BWTTutorialAnchor> anchors,
            IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            observedLessonId = lessonId ?? string.Empty;
            lessonAnchor = FindLessonAnchor(anchors, GetAnchorForLesson(lessonId));
            lessonAnchorIsLive = lessonAnchor.IsValid;
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
            BWTTutorialGestureDemo.Reset();
            if (lessonId == PriorityScheduleLesson)
            {
                if (TimePriorityScheduleEditor.IsVisible)
                {
                    TimePriorityScheduleEditor.CloseForTutorial();
                }

                if (settings != null &&
                    settings.tutorialLessonPhase > 0 &&
                    settings.tutorialLessonPhase != CompletionOutcomePhase)
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

                    if (settings.tutorialLessonPhase == 0)
                    {
                        AdvanceLessonPhase(1);
                    }
                }

                bool shiftHeld = ShiftHelper.IsHeld;
                if (settings.tutorialLessonPhase == 1 && !shiftHeld)
                {
                    double visibleSeconds = skillVisibleStartedAt < 0f
                        ? 0d
                        : Time.realtimeSinceStartup - skillVisibleStartedAt;
                    if (TutorialProgressTransitions.ShouldCompleteAction(
                            TutorialActionRequirement.SkillVisibleForInterval,
                            priorityChanged: false,
                            skillVisibleSeconds: visibleSeconds,
                            scheduleOpened: false,
                            scheduleEdited: false,
                            scheduleClosed: false,
                            routedSettings: false,
                            observedAction: false))
                    {
                        CompleteLesson(lesson);
                    }
                    else
                    {
                        AdvanceLessonPhase(0);
                    }
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
                        initialScheduleEditRevision = TimePriorityScheduleEditor.TutorialEditRevision;
                        AdvanceLessonPhase(1);
                    }
                    break;
                case 1:
                    if (TimePriorityScheduleEditor.TutorialEditRevision != initialScheduleEditRevision)
                    {
                        scheduleEdited = true;
                        AdvanceLessonPhase(2);
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
                phase != CompletionOutcomePhase &&
                (TimePriorityScheduleEditor.TryGetTutorialCellRect(out Rect scheduleRect) ||
                 TimePriorityScheduleEditor.TryGetLastPanelRect(out scheduleRect)))
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

        private static void DrawLessonAnchor(BWTTutorialAnchor anchor)
        {
            if (!anchor.IsValid)
            {
                return;
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4.2f);
            BWTTutorialAnchorRenderer.DrawOutline(
                anchor,
                BWTTutorialAnchorRenderer.TutorialGold(Mathf.Lerp(0.72f, 1f, pulse)),
                3f);
        }

        /// <summary>
        /// The instruction to show while the lesson's gesture is already underway.
        ///
        /// "Drag the highlighted Work header to a new position" is the right
        /// thing to say until the header is in the player's hand — after which
        /// it is describing something they have already started, at the exact
        /// moment they most need to be told what finishes it. A gesture with a
        /// middle deserves an instruction for its middle.
        ///
        /// Returns false for lessons whose gesture is instantaneous; a click has
        /// no in-progress state to report.
        /// </summary>
        private static bool TryGetInProgressBody(string lessonId, out string body)
        {
            body = null;
            if (!BetterWorkTabLocalState.IsHeaderDragging)
            {
                return false;
            }

            if (lessonId == HeaderReorderLesson)
            {
                body = T("BWT_Tutorial_HeaderReorder_ActionDragging");
                return true;
            }

            if (lessonId == HeaderGroupLesson)
            {
                body = T("BWT_Tutorial_HeaderGroup_ActionDragging");
                return true;
            }

            return false;
        }

        private static string GetLessonBody(string lessonId, int phase)
        {
            if (phase == CompletionOutcomePhase)
            {
                if (lessonId == HeaderSubWorkLesson)
                {
                    return string.Format(
                        T("BWT_Tutorial_HeaderSubWork_Outcome"),
                        SubWorkDrilldownInput.GestureLabel());
                }

                BWTTutorialLessonDefinition completed = BWTTutorialLessonCatalog.Find(lessonId);
                return completed == null
                    ? T("BWT_Tutorial_Outcome_Default")
                    : T("BWT_Tutorial_" + completed.LocalizationStem + "_Outcome");
            }

            if (TryGetInProgressBody(lessonId, out string inProgress))
            {
                return inProgress;
            }

            if (lessonId == HeaderSubWorkLesson)
            {
                return BuildSubWorkActionBody();
            }

            if (lessonId == PrioritySkillLesson)
            {
                return BWTTutorialUserContext.BuildSkillNumberTutorialBody(
                    lessonAnchor.WorkType,
                    skillNumbersVisible: phase == 1);
            }

            if (lessonId == PriorityScheduleLesson)
            {
                return phase == 0
                    ? T("BWT_Tutorial_PrioritySchedule_ActionOpen")
                    : phase == 1
                        ? T("BWT_Tutorial_PrioritySchedule_ActionEdit")
                        : T("BWT_Tutorial_PrioritySchedule_ActionClose");
            }

            if (lessonId == PawnDividerLesson && phase > 0)
            {
                return T("BWT_Tutorial_PawnDivider_ActionChoose");
            }

            if (lessonId == PawnAppearanceLesson)
            {
                if (phase == 1)
                {
                    return T("BWT_Tutorial_PawnAppearance_ActionChoose");
                }
                if (phase == 2)
                {
                    return T("BWT_Tutorial_PawnAppearance_ActionType");
                }
                if (phase >= 3)
                {
                    return T("BWT_Tutorial_PawnAppearance_ActionConfirm");
                }
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

            // A lesson's target can leave the screen entirely — entering a
            // sub-work drilldown removes every root Work header. Record that so
            // nothing is drawn pointing at a place the target no longer occupies.
            lessonAnchorIsLive = fresh.IsValid;
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

        private static Rect OffsetRect(Rect rect, Vector2 offset)
        {
            return new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);
        }

        private static Rect GetWorkTabAttachmentBounds(Rect inRect, Vector2 rootOffset)
        {
            Rect root = OffsetRect(inRect, rootOffset);
            return new Rect(
                root.x,
                root.y - WorkTabVerticalChrome,
                root.width,
                root.height + WorkTabVerticalChrome * 2f);
        }

        private static void CompleteLesson(string lessonId)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null ||
                string.IsNullOrEmpty(lessonId) ||
                IsShowingCompletionOutcome(lessonId))
            {
                return;
            }

            TutorialProgressTransitions.Complete(settings.completedTutorialLessonIds, lessonId);
            settings.skippedTutorialLessonIds.Remove(lessonId);
            settings.activeTutorialLessonId = lessonId;
            settings.tutorialLessonPhase = CompletionOutcomePhase;
            settings.Write();
            BWTTutorialGestureDemo.Reset();
            PlayTutorialSound("Tick_High");
        }

        private static bool IsShowingCompletionOutcome(string lessonId)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            return settings != null &&
                   settings.tutorialLessonPhase == CompletionOutcomePhase &&
                   string.Equals(settings.activeTutorialLessonId, lessonId, StringComparison.Ordinal);
        }

        private static void AcknowledgeLessonOutcome()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null || settings.tutorialLessonPhase != CompletionOutcomePhase)
            {
                return;
            }

            TutorialProgressTransitions.ReturnToSelection(
                ref settings.activeTutorialLessonId,
                ref settings.tutorialLessonPhase);
            settings.Write();
            lessonAnchor = default(BWTTutorialAnchor);
            observedLessonId = string.Empty;
            BWTTutorialGestureDemo.Reset();
            Selector.Reset();
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
            BWTTutorialGestureDemo.Reset();
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
            BWTTutorialGestureDemo.Reset();
            Selector.Reset();
            PlayTutorialSound("Tick_Low");
        }

        private static void Pause()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings.showGeneralTutorial = false;
            settings.Write();
            WelcomeOverlay.ResetAnimation();
            BWTTutorialGestureDemo.Reset();
            Selector.Reset();
            PlayTutorialSound("Tick_Low");
        }

        private static TutorialOverlayContent BuildWelcomeContent()
        {
            BWTTutorialCourse course = ResolveWelcomeCourse();
            return new TutorialOverlayContent(
                T("BWT_Tutorial_Welcome_Title"),
                T("BWT_Tutorial_Welcome_Body"),
                primaryButton: T(course == BWTTutorialCourse.WhatsNew20
                    ? "BWT_Tutorial_Welcome_WhatsNew"
                    : "BWT_Tutorial_Welcome_Full"),
                dismissButton: T("BWT_Tutorial_NotNow"),
                secondaryButton: null);
        }

        private static void StartRecommendedCourse()
        {
            StartCourse(ResolveWelcomeCourse());
        }

        private static BWTTutorialCourse ResolveWelcomeCourse()
        {
            return BetterWorkTabMod.Settings?.tutorialMigratedFromPublic105 == true
                ? BWTTutorialCourse.WhatsNew20
                : BWTTutorialCourse.Full;
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

        /// <summary>
        /// Opens the beta feedback portal from outside the tutorial.
        ///
        /// The portal covers the whole of 2.0, not just the tour, so it must be
        /// reachable without one. It deliberately does not touch tutorial state:
        /// a player giving feedback has not started, paused or finished anything.
        /// </summary>
        internal static void OpenBetaFeedback()
        {
            OpenReview();
        }

        private static void OpenReview()
        {
            if (Find.WindowStack != null && !Find.WindowStack.IsOpen<Window_BWTBetaFeedback>())
            {
                Find.WindowStack.Add(new Window_BWTBetaFeedback());
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

        /// <summary>
        /// Names whichever routes into specific jobs are actually available.
        ///
        /// Both are configurable and either can change while the lesson is on
        /// screen, so the instruction is composed from the live settings instead
        /// of naming one gesture in the copy. The visible button leads, because a
        /// player who can see it does not need a shortcut explained first.
        /// </summary>
        private static string BuildSubWorkActionBody()
        {
            string gesture = SubWorkDrilldownInput.GestureLabel();
            bool badgeVisible = BetterWorkTabMod.Settings?.showSubWorkHeaderBadge ??
                                DefaultSettings.showSubWorkHeaderBadge;
            return string.Format(
                T(badgeVisible
                    ? "BWT_Tutorial_HeaderSubWork_ActionBoth"
                    : "BWT_Tutorial_HeaderSubWork_ActionShortcut"),
                gesture);
        }

        private static string T(string key)
        {
            return key.Translate();
        }

    }
}
