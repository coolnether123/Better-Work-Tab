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
        TimePriorityPrompt = 90,
        TimePriorityHours = 100,
        TimePriorityClosePrompt = 110,
        TimePrioritySubWork = 120,
        MaxPriority = 130,
        AltClickSettings = 140,
        Completed = 1000
    }

    /// <summary>
    /// Guided Work tab walkthrough for the 2.0 beta. The tutorial observes existing
    /// Work tab state and never owns sub-work, priority, or schedule behavior.
    /// </summary>
    internal static class BWTBetaTutorial
    {
        private const float CardWidth = 440f;
        private const float CardPadding = 14f;
        private const float CardGap = 18f;
        private const float WindowMargin = 12f;
        private const float LayoutAnimationSeconds = 0.22f;
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.56f);
        private static readonly Color CardColor = new Color(0.075f, 0.085f, 0.095f, 0.98f);
        private static readonly Color FocusColor = new Color(1f, 0.82f, 0.22f, 0.92f);
        private static readonly Color FocusFillColor = new Color(1f, 0.82f, 0.22f, 0.10f);

        private static int _lastObservedStep = int.MinValue;
        private static int _observedSyncVersion;
        private static int _observedOverrideCount;
        private static bool _observedSubWorkActive;
        private static bool _observedTimePriorityOpen;
        private static TutorialButton _pressedButton = TutorialButton.None;
        private static bool _hasAnimatedLayout;
        private static float _layoutAnimationStartedAt;
        private static TutorialLayout _animationStartLayout;
        private static TutorialLayout _animationTargetLayout;
        private static TutorialLayout _animatedLayout;

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

            TutorialContent content = BuildContent(step);
            TutorialLayout visualLayout = GetAnimatedLayout(step, inRect, layout, content.Body);
            Rect cardRect = visualLayout.CardRect;

            if (evt.type == EventType.KeyDown &&
                (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            {
                HandleAcceptKey(step);
                evt.Use();
                return true;
            }

            if (!TryGetButtonAt(cardRect, content, evt.mousePosition, out TutorialButton hoveredButton) &&
                !cardRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                _pressedButton = hoveredButton;
                evt.Use();
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                TutorialButton pressedButton = _pressedButton;
                _pressedButton = TutorialButton.None;

                if (pressedButton != TutorialButton.None && pressedButton == hoveredButton)
                {
                    if (pressedButton == TutorialButton.Deactivate)
                    {
                        Deactivate();
                    }
                    else if (pressedButton == TutorialButton.Primary)
                    {
                        AdvanceByButton(step);
                    }
                }

                evt.Use();
                return true;
            }

            return evt.type == EventType.MouseDrag || evt.type == EventType.ScrollWheel;
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

            HandleAcceptKey(step);
            Event.current?.Use();
            return true;
        }

        private static void HandleAcceptKey(BWTBetaTutorialStep step)
        {
            TutorialContent content = BuildContent(step);
            if (!string.IsNullOrEmpty(content.PrimaryButton))
            {
                AdvanceByButton(step);
            }
            else
            {
                UISoundCompat.TickLow.PlayOneShotOnCamera();
            }
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

            TutorialContent content = BuildContent(step);
            TutorialLayout visualLayout = GetAnimatedLayout(step, inRect, layout, content.Body);

            DrawSpotlight(inRect, visualLayout.FocusBounds, visualLayout.FocusRects);
            DrawShortcutHints(step, inRect, visualLayout.FocusRects);
            if (visualLayout.FocusRects.Count > 0)
            {
                DrawConnector(visualLayout.CardRect, visualLayout.FocusBounds);
            }

            DrawCard(visualLayout.CardRect, content, step);
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
            _observedTimePriorityOpen = TimePriorityPlannerPrototype.IsVisible;
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
                        SetStep(BWTBetaTutorialStep.TimePriorityPrompt);
                    }
                    break;

                case BWTBetaTutorialStep.TimePriorityPrompt:
                    if (TimePriorityPlannerPrototype.IsVisible)
                    {
                        SetStep(BWTBetaTutorialStep.TimePriorityHours);
                    }
                    break;

                case BWTBetaTutorialStep.TimePriorityHours:
                    if (!TimePriorityPlannerPrototype.IsVisible)
                    {
                        SetStep(_observedTimePriorityOpen
                            ? BWTBetaTutorialStep.TimePrioritySubWork
                            : BWTBetaTutorialStep.TimePriorityPrompt);
                    }
                    break;

                case BWTBetaTutorialStep.TimePriorityClosePrompt:
                    if (!TimePriorityPlannerPrototype.IsVisible)
                    {
                        SetStep(BWTBetaTutorialStep.TimePrioritySubWork);
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

        private static TutorialContent BuildContent(BWTBetaTutorialStep step)
        {
            switch (step)
            {
                case BWTBetaTutorialStep.Welcome:
                    return new TutorialContent(
                        "Welcome to Better Work Tab 2.0",
                        "This is a beta test for the new 2.0 systems. Follow the tutorial to see what needs testing: sub-work jobs, time priority schedules, and the new priority range.",
                        "Continue tutorial");

                case BWTBetaTutorialStep.SubWorkPrompt:
                    return new TutorialContent(
                        "Sub-work jobs",
                        "Sub-jobs are now part of Better Work Tab. Hold Control and click any work header to open that work type's sub-work jobs.",
                        null);

                case BWTBetaTutorialStep.SubWorkHeaders:
                    return new TutorialContent(
                        "Sub-work headers",
                        "The work headers have changed to the sub-work jobs for this work type. They can be rearranged like normal work columns.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkGlobalPriority:
                    return new TutorialContent(
                        "Global sub-work priority",
                        "This top row is the global priority for each sub-work job. It follows the same left-to-right priority behavior as the normal Work tab.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkPawnPriority:
                    return new TutorialContent(
                        "Pawn sub-work priority",
                        "Each pawn row can have its own priority for a specific sub-work job. Pawns follow the global priority unless you manually change their cell.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkChangePawnPriority:
                    return new TutorialContent(
                        "Try an override",
                        "Change one pawn priority in this sub-work view. The cell will get a gold box when it stops following the global priority.",
                        null);

                case BWTBetaTutorialStep.SubWorkResetOverride:
                    return new TutorialContent(
                        "Gold box means locked",
                        "The gold box means this pawn is locked to the value you chose. Click the gold box to match the global priority again.",
                        null);

                case BWTBetaTutorialStep.SubWorkResetConfirmed:
                    return new TutorialContent(
                        "Following global again",
                        "Good. The pawn is following the global priority again. This same gold-box behavior also applies to global sub-work priority schedules.",
                        "Next");

                case BWTBetaTutorialStep.SubWorkExitPrompt:
                    return new TutorialContent(
                        "Exit sub-work",
                        "Return to normal work types by clicking the X at the top right or by Control-clicking a header.",
                        null);

                case BWTBetaTutorialStep.TimePriorityPrompt:
                    return new TutorialContent(
                        "Time priority schedules",
                        "You can now schedule a pawn to use different priorities during the day and night. Control-click any pawn priority cell to open its time schedule.",
                        null);

                case BWTBetaTutorialStep.TimePriorityHours:
                    return new TutorialContent(
                        "Hour blocks",
                        "These are the 24 hour blocks. Each box stores the priority used during that hour. Click or scroll the boxes to change the scheduled priority.",
                        "Next");

                case BWTBetaTutorialStep.TimePriorityClosePrompt:
                    return new TutorialContent(
                        "Close the schedule",
                        "Control-click any time slot in the open schedule to close it. The Work tab returns to the normal priority row.",
                        null);

                case BWTBetaTutorialStep.TimePrioritySubWork:
                    return new TutorialContent(
                        "Sub-work schedules",
                        "Sub-work jobs can have their own time schedules too. For example, a doctor can be scheduled to only do surgery from hours 8 to 12.",
                        "Next");

                case BWTBetaTutorialStep.MaxPriority:
                    return new TutorialContent(
                        "Priorities beyond 4",
                        "Better Work Tab can now go higher than vanilla 4 priorities. This beta defaults to 1-9, and the setting can go up to 99.",
                        "Next");

                case BWTBetaTutorialStep.AltClickSettings:
                    return new TutorialContent(
                        "Settings by context",
                        "Alt-click anywhere in the Work tab to open settings for that feature. Use Shift+Alt or Ctrl+Alt for more specific settings.",
                        "Finish tutorial");

                default:
                    return new TutorialContent("Better Work Tab 2.0", "Tutorial complete.", "Finish tutorial");
            }
        }

        private static List<Rect> BuildFocusRects(BWTBetaTutorialStep step, Rect inRect, IWorkTabLayoutController layout)
        {
            var rects = new List<Rect>();
            switch (step)
            {
                case BWTBetaTutorialStep.SubWorkPrompt:
                case BWTBetaTutorialStep.SubWorkHeaders:
                    AddWorkHeaderArea(rects, layout);
                    break;

                case BWTBetaTutorialStep.SubWorkGlobalPriority:
                    AddGlobalPriorityRow(rects, layout);
                    break;

                case BWTBetaTutorialStep.SubWorkPawnPriority:
                case BWTBetaTutorialStep.SubWorkChangePawnPriority:
                case BWTBetaTutorialStep.SubWorkResetOverride:
                    AddFirstPriorityCell(rects, layout, requireSubWorkColumn: SubWorkDrilldownState.IsActive);
                    break;

                case BWTBetaTutorialStep.SubWorkResetConfirmed:
                    AddGlobalPriorityRow(rects, layout);
                    AddFirstPriorityCell(rects, layout, requireSubWorkColumn: true);
                    break;

                case BWTBetaTutorialStep.SubWorkExitPrompt:
                    AddWorkHeaderArea(rects, layout);
                    rects.Add(GetSubWorkExitRect(inRect));
                    break;

                case BWTBetaTutorialStep.TimePriorityPrompt:
                    AddFirstPriorityCell(rects, layout, requireSubWorkColumn: false);
                    break;

                case BWTBetaTutorialStep.TimePriorityHours:
                case BWTBetaTutorialStep.TimePriorityClosePrompt:
                    AddTimePriorityEditor(rects);
                    break;

                case BWTBetaTutorialStep.TimePrioritySubWork:
                    AddWorkHeaderArea(rects, layout);
                    AddFirstPriorityCell(rects, layout, requireSubWorkColumn: false);
                    break;

                case BWTBetaTutorialStep.MaxPriority:
                    AddFirstPriorityCell(rects, layout, requireSubWorkColumn: false, allowDisabledFallback: false);
                    break;

                case BWTBetaTutorialStep.AltClickSettings:
                    rects.Add(new Rect(inRect.xMax - 300f, inRect.y + 2f, 260f, 28f));
                    break;

                default:
                    rects.Add(inRect.ContractedBy(8f));
                    break;
            }

            if (rects.Count == 0 &&
                (step == BWTBetaTutorialStep.TimePriorityHours ||
                 step == BWTBetaTutorialStep.TimePriorityClosePrompt))
            {
                AddFirstPriorityCell(rects, layout, requireSubWorkColumn: false);
            }

            if (rects.Count == 0 && AllowsWholeTabFallback(step))
            {
                rects.Add(inRect.ContractedBy(12f));
            }

            return rects;
        }

        private static bool AllowsWholeTabFallback(BWTBetaTutorialStep step)
        {
            return step != BWTBetaTutorialStep.TimePriorityHours &&
                step != BWTBetaTutorialStep.TimePriorityClosePrompt &&
                step != BWTBetaTutorialStep.MaxPriority;
        }

        private static void AddWorkHeaderArea(List<Rect> rects, IWorkTabLayoutController layout)
        {
            if (layout?.Columns == null)
            {
                return;
            }

            Rect union = RectCompat.Zero;
            bool hasAny = false;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                union = hasAny ? Union(union, column.HeaderRect) : column.HeaderRect;
                hasAny = true;
            }

            if (hasAny)
            {
                rects.Add(union.ExpandedBy(6f));
            }
        }

        private static void AddGlobalPriorityRow(List<Rect> rects, IWorkTabLayoutController layout)
        {
            if (layout?.Table == null || !SubWorkDrilldownState.IsActive)
            {
                return;
            }

            Rect rect = new Rect(
                layout.TableOrigin.x,
                layout.TableOrigin.y + layout.HeaderHeight + TimePriorityPlannerPrototype.HeaderPinnedRowsHeight,
                Mathf.Max(layout.Table.Size.x - 16f, 1f),
                Mathf.Max(SubWorkDrilldownState.GlobalRowVisibleHeight, SubWorkDrilldownState.GlobalRowHeight));
            rects.Add(rect.ExpandedBy(4f));
        }

        private static void AddFirstPriorityCell(
            List<Rect> rects,
            IWorkTabLayoutController layout,
            bool requireSubWorkColumn,
            bool allowDisabledFallback = true)
        {
            if (layout?.Rows == null || layout.Columns == null)
            {
                return;
            }

            Rect fallbackRect = RectCompat.Zero;
            bool hasFallback = false;
            for (int r = 0; r < layout.Rows.Count; r++)
            {
                WorkTabLayoutRow row = layout.Rows[r];
                if (row.Pawn == null)
                {
                    continue;
                }

                Rect rowRect = layout.GetScreenRect(row);
                for (int c = 0; c < layout.Columns.Count; c++)
                {
                    WorkTabLayoutColumn column = layout.Columns[c];
                    if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                    {
                        continue;
                    }

                    if (requireSubWorkColumn &&
                        !SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out _, out _))
                    {
                        continue;
                    }

                    Rect cellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, rowRect.height);
                    Rect priorityBoxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect).ExpandedBy(5f);
                    if (!hasFallback)
                    {
                        fallbackRect = priorityBoxRect;
                        hasFallback = true;
                    }

                    if (CanUsePriorityExample(row.Pawn, column.Column.workType))
                    {
                        rects.Add(priorityBoxRect);
                        return;
                    }
                }
            }

            if (allowDisabledFallback && hasFallback)
            {
                rects.Add(fallbackRect);
            }
        }

        private static bool CanUsePriorityExample(Pawn pawn, WorkTypeDef workType)
        {
            return pawn != null &&
                !PawnCompat.IsDead(pawn) &&
                PawnCompat.WorkSettings(pawn) != null &&
                PawnCompat.HasEverWork(pawn) &&
                workType != null &&
                !pawn.WorkTypeIsDisabled(workType);
        }

        private static void AddTimePriorityEditor(List<Rect> rects)
        {
            if (TimePriorityPlannerPrototype.TryGetLastPanelRect(out Rect rect))
            {
                rects.Add(rect.ExpandedBy(5f));
            }
        }

        private static Rect GetSubWorkExitRect(Rect inRect)
        {
            const float buttonSize = 24f;
            return new Rect(inRect.xMax - buttonSize - 10f, inRect.y + 8f, buttonSize, buttonSize).ExpandedBy(5f);
        }

        private static Rect UnionFocusRects(List<Rect> rects, Rect fallback)
        {
            if (rects == null || rects.Count == 0)
            {
                return fallback;
            }

            Rect union = rects[0];
            for (int i = 1; i < rects.Count; i++)
            {
                union = Union(union, rects[i]);
            }

            return union;
        }

        private static Rect Union(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static TutorialLayout GetAnimatedLayout(
            BWTBetaTutorialStep step,
            Rect inRect,
            IWorkTabLayoutController layout,
            string body)
        {
            TutorialLayout target = BuildTargetLayout(step, inRect, layout, body);
            if (!_hasAnimatedLayout)
            {
                _animationStartLayout = target;
                _animationTargetLayout = target;
                _animatedLayout = target;
                _layoutAnimationStartedAt = Time.realtimeSinceStartup;
                _hasAnimatedLayout = true;
                return _animatedLayout;
            }

            if (!LayoutsApproximatelyEqual(_animationTargetLayout, target))
            {
                _animationStartLayout = AlignLayoutForAnimation(_animatedLayout, target);
                _animationTargetLayout = target;
                _layoutAnimationStartedAt = Time.realtimeSinceStartup;
            }

            float progress = LayoutAnimationSeconds <= 0f
                ? 1f
                : Mathf.Clamp01((Time.realtimeSinceStartup - _layoutAnimationStartedAt) / LayoutAnimationSeconds);
            progress = SmoothStep01(progress);
            _animatedLayout = LerpLayout(_animationStartLayout, _animationTargetLayout, progress);
            return _animatedLayout;
        }

        private static TutorialLayout BuildTargetLayout(
            BWTBetaTutorialStep step,
            Rect inRect,
            IWorkTabLayoutController layout,
            string body)
        {
            List<Rect> focusRects = BuildFocusRects(step, inRect, layout);
            Rect focusBounds = UnionFocusRects(focusRects, inRect);
            Rect cardRect = GetCardRect(inRect, focusBounds, focusRects, body);
            return new TutorialLayout(cardRect, focusBounds, focusRects);
        }

        private static TutorialLayout AlignLayoutForAnimation(TutorialLayout current, TutorialLayout target)
        {
            var focusRects = new List<Rect>(target.FocusRects.Count);
            for (int i = 0; i < target.FocusRects.Count; i++)
            {
                focusRects.Add(i < current.FocusRects.Count ? current.FocusRects[i] : current.FocusBounds);
            }

            return new TutorialLayout(current.CardRect, current.FocusBounds, focusRects);
        }

        private static TutorialLayout LerpLayout(TutorialLayout start, TutorialLayout target, float t)
        {
            var focusRects = new List<Rect>(target.FocusRects.Count);
            for (int i = 0; i < target.FocusRects.Count; i++)
            {
                Rect startRect = i < start.FocusRects.Count ? start.FocusRects[i] : start.FocusBounds;
                focusRects.Add(LerpRect(startRect, target.FocusRects[i], t));
            }

            return new TutorialLayout(
                LerpRect(start.CardRect, target.CardRect, t),
                LerpRect(start.FocusBounds, target.FocusBounds, t),
                focusRects);
        }

        private static Rect LerpRect(Rect start, Rect end, float t)
        {
            return new Rect(
                Mathf.Lerp(start.x, end.x, t),
                Mathf.Lerp(start.y, end.y, t),
                Mathf.Lerp(start.width, end.width, t),
                Mathf.Lerp(start.height, end.height, t));
        }

        private static bool LayoutsApproximatelyEqual(TutorialLayout a, TutorialLayout b)
        {
            if (!RectApproximatelyEqual(a.CardRect, b.CardRect) ||
                !RectApproximatelyEqual(a.FocusBounds, b.FocusBounds) ||
                a.FocusRects.Count != b.FocusRects.Count)
            {
                return false;
            }

            for (int i = 0; i < a.FocusRects.Count; i++)
            {
                if (!RectApproximatelyEqual(a.FocusRects[i], b.FocusRects[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RectApproximatelyEqual(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f &&
                   Mathf.Abs(a.y - b.y) < 0.5f &&
                   Mathf.Abs(a.width - b.width) < 0.5f &&
                   Mathf.Abs(a.height - b.height) < 0.5f;
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static Rect GetCardRect(Rect inRect, Rect focusBounds, List<Rect> focusRects, string body)
        {
            Vector2 size = GetCardSize(inRect, body);
            float width = size.x;
            float height = size.y;

            Rect bounds = inRect.ContractedBy(WindowMargin);
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return new Rect(inRect.x, inRect.y, width, height);
            }

            var candidates = new List<Rect>
            {
                ClampCardToBounds(new Rect(focusBounds.center.x - width / 2f, focusBounds.yMin - height - CardGap, width, height), bounds),
                ClampCardToBounds(new Rect(focusBounds.center.x - width / 2f, focusBounds.yMax + CardGap, width, height), bounds),
                ClampCardToBounds(new Rect(focusBounds.xMin - width - CardGap, focusBounds.center.y - height / 2f, width, height), bounds),
                ClampCardToBounds(new Rect(focusBounds.xMax + CardGap, focusBounds.center.y - height / 2f, width, height), bounds),
                new Rect(bounds.xMin, bounds.yMin, width, height),
                new Rect(bounds.xMax - width, bounds.yMin, width, height),
                new Rect(bounds.xMin, bounds.yMax - height, width, height),
                new Rect(bounds.xMax - width, bounds.yMax - height, width, height)
            };

            Rect best = candidates[0];
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                Rect candidate = ClampCardToBounds(candidates[i], bounds);
                float score = ScoreCardPlacement(candidate, focusBounds, focusRects);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static Vector2 GetCardSize(Rect inRect, string body)
        {
            float width = Mathf.Min(CardWidth, Mathf.Max(320f, inRect.width - 32f));
            float bodyHeight;
            GameFont sizeOldFont = Text.Font;
            Text.Font = GameFont.Small;
            bodyHeight = Text.CalcHeight(body, width - CardPadding * 2f);
            Text.Font = sizeOldFont;

            float height = Mathf.Clamp(122f + bodyHeight, 180f, 320f);
            return new Vector2(width, height);
        }

        private static Rect ClampCardToBounds(Rect rect, Rect bounds)
        {
            float x = Mathf.Clamp(rect.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - rect.width));
            float y = Mathf.Clamp(rect.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - rect.height));
            return new Rect(x, y, rect.width, rect.height);
        }

        private static float ScoreCardPlacement(Rect cardRect, Rect focusBounds, List<Rect> focusRects)
        {
            float overlapArea = IntersectionArea(cardRect, focusBounds);
            if (focusRects != null)
            {
                for (int i = 0; i < focusRects.Count; i++)
                {
                    overlapArea += IntersectionArea(cardRect, focusRects[i]) * 2f;
                }
            }

            float distance = Vector2.Distance(cardRect.center, focusBounds.center);
            float sidePreference = cardRect.yMax <= focusBounds.yMin || cardRect.yMin >= focusBounds.yMax ? 10000f : 0f;
            return sidePreference + distance - overlapArea * 100f;
        }

        private static float IntersectionArea(Rect a, Rect b)
        {
            float width = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float height = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            return width * height;
        }

        private static void DrawSpotlight(Rect inRect, Rect focusBounds, List<Rect> focusRects)
        {
            Rect focus = ClampRectToBounds(focusBounds.ExpandedBy(8f), inRect);
            DrawDimRect(new Rect(inRect.xMin, inRect.yMin, inRect.width, Mathf.Max(0f, focus.yMin - inRect.yMin)));
            DrawDimRect(new Rect(inRect.xMin, focus.yMax, inRect.width, Mathf.Max(0f, inRect.yMax - focus.yMax)));
            DrawDimRect(new Rect(inRect.xMin, focus.yMin, Mathf.Max(0f, focus.xMin - inRect.xMin), focus.height));
            DrawDimRect(new Rect(focus.xMax, focus.yMin, Mathf.Max(0f, inRect.xMax - focus.xMax), focus.height));

            Color oldColor = GUI.color;
            for (int i = 0; i < focusRects.Count; i++)
            {
                Rect rect = ClampRectToBounds(focusRects[i], inRect);
                WidgetsCompat.DrawBoxSolid(rect, FocusFillColor);
                GUI.color = FocusColor;
                Widgets.DrawBox(rect, 2);
            }

            GUI.color = oldColor;
        }

        private static void DrawShortcutHints(BWTBetaTutorialStep step, Rect inRect, List<Rect> focusRects)
        {
            string hint = GetShortcutHint(step);
            if (string.IsNullOrEmpty(hint) || focusRects == null || focusRects.Count == 0)
            {
                return;
            }

            int hintCount = GetShortcutHintFocusCount(step, focusRects.Count);
            for (int i = 0; i < hintCount; i++)
            {
                DrawShortcutHint(hint, ClampRectToBounds(focusRects[i], inRect), inRect);
            }
        }

        private static string GetShortcutHint(BWTBetaTutorialStep step)
        {
            switch (step)
            {
                case BWTBetaTutorialStep.SubWorkPrompt:
                case BWTBetaTutorialStep.TimePriorityPrompt:
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

        private static void DrawShortcutHint(string text, Rect focusRect, Rect bounds)
        {
            if (focusRect.width <= 1f || focusRect.height <= 1f)
            {
                return;
            }

            const float badgeHeight = 24f;
            const float padding = 8f;
            Vector2 size;
            GameFont sizeOldFont = Text.Font;
            Text.Font = GameFont.Small;
            size = Text.CalcSize(text);
            Text.Font = sizeOldFont;

            float width = Mathf.Max(92f, size.x + padding * 2f);
            Rect badgeRect = new Rect(
                focusRect.center.x - width / 2f,
                focusRect.yMin - badgeHeight - 6f,
                width,
                badgeHeight);

            if (badgeRect.yMin < bounds.yMin + 4f)
            {
                badgeRect.y = focusRect.yMax + 6f;
            }

            badgeRect = ClampCardToBounds(badgeRect, bounds.ContractedBy(4f));

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            WidgetsCompat.DrawBoxSolid(badgeRect, new Color(0.05f, 0.05f, 0.05f, 0.95f));
            GUI.color = FocusColor;
            Widgets.DrawBox(badgeRect, 1);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Widgets.Label(badgeRect, text);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private static void DrawDimRect(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            WidgetsCompat.DrawBoxSolid(rect, DimColor);
        }

        private static Rect ClampRectToBounds(Rect rect, Rect bounds)
        {
            float xMin = Mathf.Clamp(rect.xMin, bounds.xMin, bounds.xMax);
            float yMin = Mathf.Clamp(rect.yMin, bounds.yMin, bounds.yMax);
            float xMax = Mathf.Clamp(rect.xMax, bounds.xMin, bounds.xMax);
            float yMax = Mathf.Clamp(rect.yMax, bounds.yMin, bounds.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void DrawConnector(Rect cardRect, Rect focusBounds)
        {
            Vector2 start = ClosestPointOnRect(cardRect, focusBounds.center);
            Vector2 end = ClosestPointOnRect(focusBounds, cardRect.center);
            Widgets.DrawLine(start, end, FocusColor, 2f);
        }

        private static Vector2 ClosestPointOnRect(Rect rect, Vector2 target)
        {
            return new Vector2(
                Mathf.Clamp(target.x, rect.xMin, rect.xMax),
                Mathf.Clamp(target.y, rect.yMin, rect.yMax));
        }

        private static void DrawCard(Rect rect, TutorialContent content, BWTBetaTutorialStep step)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            WidgetsCompat.DrawBoxSolid(rect, CardColor);
            GUI.color = new Color(0.82f, 0.78f, 0.66f, 1f);
            Widgets.DrawBox(rect, 1);

            Rect inner = rect.ContractedBy(CardPadding);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(1f, 0.88f, 0.42f, 1f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f), content.Title);

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            float bodyY = inner.y + 34f;
            float bodyHeight = Mathf.Max(64f, inner.height - 92f);
            Widgets.Label(new Rect(inner.x, bodyY, inner.width, bodyHeight), content.Body);

            Rect deactivateRect = GetDeactivateButtonRect(rect);
            if (WidgetsCompat.ButtonText(deactivateRect, "Deactivate tutorial"))
            {
                Deactivate();
            }

            if (!string.IsNullOrEmpty(content.PrimaryButton))
            {
                Rect nextRect = GetPrimaryButtonRect(rect);
                if (WidgetsCompat.ButtonText(nextRect, content.PrimaryButton))
                {
                    AdvanceByButton(step);
                }
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }

        private static Rect GetDeactivateButtonRect(Rect cardRect)
        {
            Rect inner = cardRect.ContractedBy(CardPadding);
            return new Rect(inner.x, inner.yMax - 34f, 150f, 32f);
        }

        private static Rect GetPrimaryButtonRect(Rect cardRect)
        {
            Rect inner = cardRect.ContractedBy(CardPadding);
            return new Rect(inner.xMax - 150f, inner.yMax - 34f, 150f, 32f);
        }

        private static bool TryGetButtonAt(
            Rect cardRect,
            TutorialContent content,
            Vector2 mousePosition,
            out TutorialButton button)
        {
            if (GetDeactivateButtonRect(cardRect).Contains(mousePosition))
            {
                button = TutorialButton.Deactivate;
                return true;
            }

            if (!string.IsNullOrEmpty(content.PrimaryButton) &&
                GetPrimaryButtonRect(cardRect).Contains(mousePosition))
            {
                button = TutorialButton.Primary;
                return true;
            }

            button = TutorialButton.None;
            return false;
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
                case BWTBetaTutorialStep.TimePriorityHours:
                    SetStep(BWTBetaTutorialStep.TimePriorityClosePrompt);
                    break;
                case BWTBetaTutorialStep.TimePrioritySubWork:
                    SetStep(BWTBetaTutorialStep.MaxPriority);
                    break;
                case BWTBetaTutorialStep.MaxPriority:
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
            UISoundCompat.TickHigh.PlayOneShotOnCamera();
        }

        private static void Deactivate()
        {
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.showBetaTutorial = false;
            _hasAnimatedLayout = false;
            settings.Write();
            UISoundCompat.TickLow.PlayOneShotOnCamera();
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
            _hasAnimatedLayout = false;
            settings.Write();
            UISoundCompat.TickHigh.PlayOneShotOnCamera();
        }

        private readonly struct TutorialContent
        {
            public TutorialContent(string title, string body, string primaryButton)
            {
                Title = title;
                Body = body;
                PrimaryButton = primaryButton;
            }

            public string Title { get; }
            public string Body { get; }
            public string PrimaryButton { get; }
        }

        private enum TutorialButton
        {
            None,
            Deactivate,
            Primary
        }

        private readonly struct TutorialLayout
        {
            public TutorialLayout(Rect cardRect, Rect focusBounds, List<Rect> focusRects)
            {
                CardRect = cardRect;
                FocusBounds = focusBounds;
                FocusRects = focusRects ?? new List<Rect>();
            }

            public Rect CardRect { get; }
            public Rect FocusBounds { get; }
            public List<Rect> FocusRects { get; }
        }
    }
}
