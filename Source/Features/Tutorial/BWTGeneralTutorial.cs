using System;
using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.Patches;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
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
        internal const int CurrentFlowVersion = 1;
        internal const string PriorityChangeLesson = "priority.change";
        internal const string PrioritySkillLesson = "priority.skill";
        internal const string PriorityScheduleLesson = "priority.schedule";
        internal const string PriorityRangeLesson = "priority.range";
        internal const string PawnMenuLesson = "pawn.menu";
        internal const string PawnDividerLesson = "pawn.divider";
        internal const string PawnAppearanceLesson = "pawn.appearance";
        internal const string HeaderReorderLesson = "header.reorder";
        internal const string HeaderGroupLesson = "header.group";
        internal const string HeaderSubWorkLesson = "header.subwork";

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

        internal static bool IsActive
        {
            get
            {
                BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
                return settings != null &&
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

        private static bool ShouldReserveSelector =>
            Presentation == TutorialPresentation.Selector || Presentation == TutorialPresentation.Lesson;

        internal static float PreferredReserveWidth => ShouldReserveSelector ? Selector.PreferredReserveWidth : 0f;
        internal static float PreferredReserveHeight => ShouldReserveSelector ? Selector.PreferredReserveHeight : 0f;

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
                    ContinueFromWelcome,
                    null,
                    null,
                    DismissWelcome);
            }

            List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
            Rect workBounds = BWTTutorialGeometry.GetVisibleWorkTabBounds(inRect, layout);

            if (presentation == TutorialPresentation.Lesson)
            {
                BWTTutorialAnchor activeAnchor = lessonAnchor.IsValid
                    ? lessonAnchor
                    : FindLessonAnchor(anchors, GetAnchorForLesson(settings.activeTutorialLessonId));
                LessonLayout lessonLayout = BuildLessonLayout(
                    inRect,
                    workBounds,
                    activeAnchor,
                    GetLessonBody(settings.activeTutorialLessonId, settings.tutorialLessonPhase));
                if (!lessonLayout.CardRect.Contains(evt.mousePosition))
                {
                    return false;
                }

                if (evt.type == EventType.ScrollWheel && lessonLayout.BodyRect.Contains(evt.mousePosition))
                {
                    float maximumScroll = Mathf.Max(0f, lessonLayout.BodyViewRect.height - lessonLayout.BodyRect.height);
                    lessonScrollPosition.y = Mathf.Clamp(
                        lessonScrollPosition.y + evt.delta.y * 22f,
                        0f,
                        maximumScroll);
                    evt.Use();
                    return true;
                }

                if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    if (lessonLayout.BackRect.Contains(evt.mousePosition))
                    {
                        ReturnToSelection();
                    }
                    else if (lessonLayout.PauseRect.Contains(evt.mousePosition))
                    {
                        Pause();
                    }

                    evt.Use();
                    return true;
                }

                return evt.type == EventType.ScrollWheel || evt.type == EventType.MouseDrag;
            }

            IReadOnlyDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> hubs = BuildHubDefinitions();
            bool handled = Selector.TryHandleInput(
                inRect,
                workBounds,
                anchors,
                hubs,
                settings.completedTutorialLessonIds,
                evt,
                T("BWT_Tutorial_Recommended"),
                T("BWT_Tutorial_SkipForNow"),
                out BWTTutorialSelectorAction action);
            if (action.Exit)
            {
                Pause();
            }
            else if (action.HasLesson)
            {
                SelectLesson(action.LessonId, anchors, layout);
            }

            return handled;
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
                return WelcomeOverlay.TryHandleAcceptKey(BuildWelcomeContent(), ContinueFromWelcome);
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
                    ContinueFromWelcome,
                    null,
                    null,
                    DismissWelcome);
                return;
            }

            List<BWTTutorialAnchor> anchors = BWTTutorialGeometry.BuildInitialAnchors(inRect, layout);
            Rect workBounds = BWTTutorialGeometry.GetVisibleWorkTabBounds(inRect, layout);
            if (presentation == TutorialPresentation.Selector)
            {
                Selector.Draw(
                    inRect,
                    workBounds,
                    anchors,
                    BuildHubDefinitions(),
                    settings.completedTutorialLessonIds,
                    T("BWT_Tutorial_DefaultContextHeading"),
                    T("BWT_Tutorial_Recommended"),
                    T("BWT_Tutorial_SkipForNow"));
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

            DrawLesson(inRect, workBounds, anchors, settings.activeTutorialLessonId, settings.tutorialLessonPhase);
        }

        internal static void ObserveInteraction(BWTTutorialInteraction interaction)
        {
            if (!IsActive)
            {
                return;
            }

            string lesson = BetterWorkTabMod.Settings.activeTutorialLessonId;
            bool completed = false;
            if (lesson == PawnMenuLesson)
            {
                completed = interaction.Kind == BWTTutorialInteractionKind.PawnRow && interaction.MouseButton == 1;
            }
            if (completed)
            {
                CompleteLesson(lesson);
            }
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

            settings.activeTutorialLessonId = settings.activeTutorialLessonId ?? string.Empty;
            settings.tutorialLessonPhase = Mathf.Max(0, settings.tutorialLessonPhase);
        }

        private static IReadOnlyDictionary<TutorialHubAnchor, BWTTutorialHubDefinition> BuildHubDefinitions()
        {
            return new Dictionary<TutorialHubAnchor, BWTTutorialHubDefinition>
            {
                [TutorialHubAnchor.PawnName] = new BWTTutorialHubDefinition(
                    TutorialHubAnchor.PawnName,
                    T("BWT_Tutorial_PawnHub_Title"),
                    T("BWT_Tutorial_PawnHub_Body"),
                    new[]
                    {
                        Option(PawnMenuLesson, "BWT_Tutorial_PawnMenu_Label", "BWT_Tutorial_PawnMenu_Title", "BWT_Tutorial_PawnMenu_Preview", true),
                        Option(PawnDividerLesson, "BWT_Tutorial_PawnDivider_Label", "BWT_Tutorial_PawnDivider_Title", "BWT_Tutorial_PawnDivider_Preview"),
                        Option(PawnAppearanceLesson, "BWT_Tutorial_PawnAppearance_Label", "BWT_Tutorial_PawnAppearance_Title", "BWT_Tutorial_PawnAppearance_Preview")
                    }),
                [TutorialHubAnchor.WorkHeader] = new BWTTutorialHubDefinition(
                    TutorialHubAnchor.WorkHeader,
                    T("BWT_Tutorial_HeaderHub_Title"),
                    T("BWT_Tutorial_HeaderHub_Body"),
                    new[]
                    {
                        Option(HeaderReorderLesson, "BWT_Tutorial_HeaderReorder_Label", "BWT_Tutorial_HeaderReorder_Title", "BWT_Tutorial_HeaderReorder_Preview", true),
                        Option(HeaderGroupLesson, "BWT_Tutorial_HeaderGroup_Label", "BWT_Tutorial_HeaderGroup_Title", "BWT_Tutorial_HeaderGroup_Preview"),
                        Option(HeaderSubWorkLesson, "BWT_Tutorial_HeaderSubWork_Label", "BWT_Tutorial_HeaderSubWork_Title", "BWT_Tutorial_HeaderSubWork_Preview")
                    }),
                [TutorialHubAnchor.PriorityCell] = new BWTTutorialHubDefinition(
                    TutorialHubAnchor.PriorityCell,
                    T("BWT_Tutorial_PriorityHub_Title"),
                    T("BWT_Tutorial_PriorityHub_Body"),
                    new[]
                    {
                        Option(PriorityChangeLesson, "BWT_Tutorial_PriorityChange_Label", "BWT_Tutorial_PriorityChange_Title", "BWT_Tutorial_PriorityChange_Preview", true),
                        Option(PrioritySkillLesson, "BWT_Tutorial_PrioritySkill_Label", "BWT_Tutorial_PrioritySkill_Title", "BWT_Tutorial_PrioritySkill_Preview"),
                        Option(PriorityScheduleLesson, "BWT_Tutorial_PrioritySchedule_Label", "BWT_Tutorial_PrioritySchedule_Title", "BWT_Tutorial_PrioritySchedule_Preview"),
                        Option(PriorityRangeLesson, "BWT_Tutorial_PriorityRange_Label", "BWT_Tutorial_PriorityRange_Title", "BWT_Tutorial_PriorityRange_Preview")
                    })
            };
        }

        private static BWTTutorialOptionDefinition Option(
            string lessonId,
            string labelKey,
            string titleKey,
            string bodyKey,
            bool recommended = false)
        {
            return new BWTTutorialOptionDefinition(
                lessonId,
                T(labelKey),
                T(titleKey),
                T(bodyKey),
                recommended);
        }

        private static void SelectLesson(
            string lessonId,
            IReadOnlyList<BWTTutorialAnchor> anchors,
            IWorkTabLayoutController layout)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            if (lessonId == PriorityRangeLesson)
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
                    TutorialProgressTransitions.Complete(settings.completedTutorialLessonIds, lessonId);
                    settings.Write();
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                return;
            }

            settings.activeTutorialLessonId = lessonId;
            settings.tutorialLessonPhase = 0;
            lessonScrollPosition = Vector2.zero;
            InitializeLessonObservation(lessonId, anchors, layout);
            settings.Write();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private static void InitializeLessonObservation(
            string lessonId,
            IReadOnlyList<BWTTutorialAnchor> anchors,
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
            initialSubWorkActive = SubWorkDrilldownState.IsActive;
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

            if (lesson == HeaderSubWorkLesson && !initialSubWorkActive && SubWorkDrilldownState.IsActive)
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

        private static void DrawLesson(
            Rect inRect,
            Rect workBounds,
            IReadOnlyList<BWTTutorialAnchor> anchors,
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
            DrawLessonAnchor(anchor);
            string body = GetLessonBody(lessonId, phase);
            LessonLayout layout = BuildLessonLayout(inRect, workBounds, anchor, body);
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
            DrawButtonLabel(layout.PauseRect, T("BWT_Tutorial_SkipForNow"));
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
            return T("BWT_Tutorial_" + LessonKey(lessonId) + "_Title");
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

            return T("BWT_Tutorial_" + LessonKey(lessonId) + "_Action");
        }

        private static string LessonKey(string lessonId)
        {
            switch (lessonId)
            {
                case PriorityChangeLesson: return "PriorityChange";
                case PrioritySkillLesson: return "PrioritySkill";
                case PriorityScheduleLesson: return "PrioritySchedule";
                case PawnMenuLesson: return "PawnMenu";
                case PawnDividerLesson: return "PawnDivider";
                case PawnAppearanceLesson: return "PawnAppearance";
                case HeaderReorderLesson: return "HeaderReorder";
                case HeaderGroupLesson: return "HeaderGroup";
                case HeaderSubWorkLesson: return "HeaderSubWork";
                default: return "Unknown";
            }
        }

        private static TutorialHubAnchor GetAnchorForLesson(string lessonId)
        {
            if (lessonId != null && lessonId.StartsWith("pawn.", StringComparison.Ordinal))
            {
                return TutorialHubAnchor.PawnName;
            }

            if (lessonId != null && lessonId.StartsWith("header.", StringComparison.Ordinal))
            {
                return TutorialHubAnchor.WorkHeader;
            }

            return TutorialHubAnchor.PriorityCell;
        }

        private static BWTTutorialAnchor FindLessonAnchor(
            IReadOnlyList<BWTTutorialAnchor> anchors,
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

        private static void RefreshLessonAnchor(IReadOnlyList<BWTTutorialAnchor> anchors)
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
                : WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(anchor.Pawn, anchor.WorkType);
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
            float x = rightSpace >= 340f
                ? work.xMax + 12f
                : Mathf.Clamp(work.center.x - cardWidth / 2f, bounds.xMin + 10f, bounds.xMax - cardWidth - 10f);
            float y = rightSpace >= 340f
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
                GetPauseButtonRect(card));
        }

        private static Rect GetBackButtonRect(Rect card)
        {
            return new Rect(card.x + 16f, card.yMax - 48f, 150f, 32f);
        }

        private static Rect GetPauseButtonRect(Rect card)
        {
            return new Rect(card.xMax - 166f, card.yMax - 48f, 150f, 32f);
        }

        private static void CompleteLesson(string lessonId)
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            TutorialProgressTransitions.Complete(settings.completedTutorialLessonIds, lessonId);
            TutorialProgressTransitions.ReturnToSelection(
                ref settings.activeTutorialLessonId,
                ref settings.tutorialLessonPhase);
            settings.Write();
            lessonAnchor = default(BWTTutorialAnchor);
            observedLessonId = string.Empty;
            lessonScrollPosition = Vector2.zero;
            Selector.Reset();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
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
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void Pause()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            settings.showGeneralTutorial = false;
            settings.Write();
            WelcomeOverlay.ResetAnimation();
            lessonScrollPosition = Vector2.zero;
            Selector.Reset();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static TutorialOverlayContent BuildWelcomeContent()
        {
            return new TutorialOverlayContent(
                T("BWT_Tutorial_Welcome_Title"),
                T("BWT_Tutorial_Welcome_Body"),
                primaryButton: T("BWT_Tutorial_Welcome_Continue"),
                dismissButton: T("BWT_Tutorial_Welcome_KnowBWT"),
                secondaryButton: null);
        }

        private static void ContinueFromWelcome()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            TutorialProgressTransitions.ResolveWelcomeChoice(
                ref settings.tutorialWelcomeCompleted,
                ref settings.showGeneralTutorial,
                continueWalkthrough: true);
            settings.tutorialFlowVersion = CurrentFlowVersion;
            WelcomeOverlay.ResetAnimation();
            Selector.Reset();
            settings.Write();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
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
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
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
                Rect pauseRect)
            {
                CardRect = cardRect;
                BodyRect = bodyRect;
                BodyViewRect = bodyViewRect;
                BackRect = backRect;
                PauseRect = pauseRect;
            }

            internal Rect CardRect { get; }
            internal Rect BodyRect { get; }
            internal Rect BodyViewRect { get; }
            internal Rect BackRect { get; }
            internal Rect PauseRect { get; }
        }
    }
}
