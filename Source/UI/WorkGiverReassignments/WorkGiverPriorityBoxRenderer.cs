using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Projection;
using Better_Work_Tab.UI.WorkGrid.Rendering;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.Settings;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Renders priority boxes for WorkGivers with vanilla-style appearance and behavior.
    /// </summary>
    internal static class WorkGiverPriorityBoxRenderer
    {
        private const float OverrideResetAnimationSeconds = 0.42f;
        private const float OverrideResetPulseTravelSeconds = 0.66f;
        private const float OverrideResetPulseDelay = 0.11f;
        private const int OverrideResetPulseCount = 3;
        private const int MaximumResetAnimations = 512;
        private static readonly Dictionary<AnimationKey, ResetAnimationState> ResetAnimations =
            new Dictionary<AnimationKey, ResetAnimationState>(64);
        private static readonly Dictionary<string, Rect> GlobalPriorityTargets = new Dictionary<string, Rect>(StringComparer.Ordinal);
        private static readonly string[] PriorityLabels = new string[PriorityConstants.ExtendedHardMax + 1];
        private static float _visualAlpha = 1f;
        private static float _resetAnimationsExpireAt;
        private static bool _trustedRootInputHit;
        private static WorkTabApplication _inputApplication;

        private static WorkTabApplication InputApplication =>
            _inputApplication ?? WorkTabApplication.Current;

        internal static void ResetForWindowClose()
        {
            ResetAnimations.Clear();
            _resetAnimationsExpireAt = 0f;
            GlobalPriorityTargets.Clear();
            _visualAlpha = 1f;
            _trustedRootInputHit = false;
            _inputApplication = null;
        }

        internal static bool HasActiveResetAnimations => ResetAnimations.Count > 0;

        internal static bool HasResetAnimation(int pawnId, WorkGiverDef workGiverDef)
        {
            return workGiverDef != null &&
                ResetAnimations.ContainsKey(BuildAnimationKey(pawnId, workGiverDef));
        }

        internal static void MaintainResetAnimations()
        {
            if (ResetAnimations.Count == 0 ||
                Time.realtimeSinceStartup < _resetAnimationsExpireAt)
            {
                return;
            }

            ResetAnimations.Clear();
            _resetAnimationsExpireAt = 0f;
        }

        public static void DrawPriorityBox(
            WorkGiver wg,
            WorkTypeDef workType,
            Pawn pawn,
            Rect boxRect,
            float visualAlpha = 1f,
            float visualScale = 1f,
            int knownParentPriority = int.MinValue)
        {
            if (wg?.def == null)
            {
                return;
            }

            WorkGiverCellPresentationCache.CellPresentation presentation =
                WorkGiverCellPresentationCache.Resolve(wg, workType, pawn, knownParentPriority);
            DrawPreparedPriorityBox(
                wg,
                workType,
                pawn,
                boxRect,
                presentation,
                visualAlpha,
                visualScale);
        }

        /// <summary>
        /// Draws a BWT-owned sub-work cell from the presentation prepared for
        /// the current finished view. Callers without that view use
        /// <see cref="DrawPriorityBox"/> and retain the native live fallback.
        /// </summary>
        internal static void DrawPreparedPriorityBox(
            WorkGiver wg,
            WorkTypeDef workType,
            Pawn pawn,
            Rect boxRect,
            WorkGiverCellPresentationCache.CellPresentation presentation,
            float visualAlpha = 1f,
            float visualScale = 1f)
        {
            if (wg?.def == null || presentation == null)
            {
                return;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewActive &&
                FluffyTimeScheduleAssigner.IsOpen)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.Schedule,
                    "BWT_Workload_FluffyScheduleUnavailable".Translate());
                return;
            }

            float oldVisualAlpha = _visualAlpha;
            _visualAlpha = Mathf.Clamp01(_visualAlpha * visualAlpha);
            if (_visualAlpha <= 0.001f)
            {
                _visualAlpha = oldVisualAlpha;
                return;
            }

            boxRect = ScaleRect(boxRect, visualScale);

            try
            {
                int workGiverPriority = FluffyTimeScheduleAssigner.IsOpen
                    ? FluffyTimeScheduleAssigner.GetDisplayPriority(
                        TimePriorityTarget.ForWorkGiver(pawn, wg.def),
                        presentation.BasePriority,
                        pawn)
                    : presentation.EffectivePriority;

                if (pawn != null &&
                    !presentation.WorkTypeDisabled &&
                    presentation.ParentPriority <= WorkPrioritySystem.DisabledPriority)
                {
                    if (presentation.HasPawnOverride && !presentation.LockedOverrides)
                    {
                        DrawParentDisabledOverrideBox(wg, workType, pawn, boxRect, workGiverPriority, presentation);
                        return;
                    }

                    DrawInheritedDisabledPriorityBox(wg, workType, pawn, boxRect);
                    DrawOverrideResetAnimation(pawn.thingIDNumber, wg.def, boxRect);
                    TooltipHandler.TipRegion(boxRect, wg.def.LabelCap);
                    return;
                }

                if (pawn != null)
                {
                    DrawPawnPriorityBox(wg, workType, pawn, boxRect, workGiverPriority, presentation);
                }
                else
                {
                    DrawGlobalPriorityBox(wg, boxRect, workGiverPriority, presentation);
                }

                TooltipHandler.TipRegion(boxRect, wg.def.LabelCap);
            }
            finally
            {
                _visualAlpha = oldVisualAlpha;
            }
        }

        /// <summary>
        /// Draws the live portion of a stable pawn sub-work cell after its base
        /// pixels were presented by the retained row cache.
        /// </summary>
        internal static void DrawPreparedPriorityOverlay(
            WorkGiver workGiver,
            Pawn pawn,
            Rect boxRect,
            bool hasGoldRing)
        {
            if (workGiver?.def == null || pawn == null)
            {
                return;
            }

            if (!hasGoldRing && MouseOverPriorityBox(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }

            DrawOverrideResetAnimation(pawn.thingIDNumber, workGiver.def, boxRect);
            if (hasGoldRing)
            {
                DrawOverrideRingIfVisible(boxRect);
            }

            Event current = Event.current;
            if (current != null &&
                boxRect.Contains(current.mousePosition) &&
                MouseOverPriorityBox(boxRect))
            {
                TooltipHandler.TipRegion(boxRect, workGiver.def.LabelCap);
            }
        }

        /// <summary>
        /// Handles a priority-box event whose hit was already resolved in the work
        /// tab's root coordinate space. Scroll views maintain their own mouse-over
        /// stack, so root-routed input must not be rejected by the nested stack.
        /// </summary>
        internal static bool TryHandleRootInput(
            WorkGiver workGiver,
            WorkTypeDef workType,
            Pawn pawn,
            Rect rootBoxRect,
            WorkTabApplication application,
            int knownParentPriority = int.MinValue)
        {
            Event evt = Event.current;
            if (evt == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel) ||
                !rootBoxRect.Contains(evt.mousePosition))
            {
                return false;
            }

            bool oldTrustedRootInputHit = _trustedRootInputHit;
            WorkTabApplication oldInputApplication = _inputApplication;
            _trustedRootInputHit = true;
            _inputApplication = application;
            try
            {
                DrawPriorityBox(
                    workGiver,
                    workType,
                    pawn,
                    rootBoxRect,
                    knownParentPriority: knownParentPriority);
                return evt.type == EventType.Used;
            }
            finally
            {
                _trustedRootInputHit = oldTrustedRootInputHit;
                _inputApplication = oldInputApplication;
            }
        }

        private static Rect ScaleRect(Rect rect, float scale)
        {
            scale = Mathf.Clamp(scale, 0.01f, 1.25f);
            if (Mathf.Abs(scale - 1f) < 0.001f)
            {
                return rect;
            }

            Vector2 center = rect.center;
            rect.width *= scale;
            rect.height *= scale;
            rect.center = center;
            return rect;
        }

        private static Color WithVisualAlpha(Color color)
        {
            color.a *= _visualAlpha;
            return color;
        }

        private static bool ShouldHandleInput
        {
            get
            {
                Event evt = Event.current;
                return _visualAlpha > 0.999f &&
                    !SubWorkDrilldownState.IsTransitioning &&
                    evt != null &&
                    (evt.type == EventType.MouseDown || evt.type == EventType.ScrollWheel);
            }
        }

        private static bool MouseOverPriorityBox(Rect rect)
        {
            return !TimePriorityScheduleEditor.OwnsCurrentMousePosition &&
                (_trustedRootInputHit || Mouse.IsOver(rect));
        }

        private static void DrawPawnPriorityBox(
            WorkGiver wg,
            WorkTypeDef workType,
            Pawn pawn,
            Rect boxRect,
            int workGiverPriority,
            WorkGiverCellPresentationCache.CellPresentation presentation)
        {
            bool hasGoldRing = presentation.HasPawnOverride || presentation.HasScheduleIndicator;
            if (DrawPawnWorkBoxContents(boxRect, pawn, workType, workGiverPriority, presentation, hasGoldRing))
            {
                DrawOverrideResetAnimation(pawn.thingIDNumber, wg.def, boxRect);
                if (hasGoldRing)
                {
                    DrawOverrideRingIfVisible(boxRect);
                }

                if (ShouldHandleInput)
                {
                    if (TryHandleScheduleIndicatorClick(
                            presentation.HasScheduleIndicator,
                            presentation.ScheduleTarget,
                            presentation.ScheduleFallbackPriority,
                            boxRect))
                    {
                        return;
                    }

                    HandlePriorityClick(
                        pawn.thingIDNumber,
                        workType,
                        wg.def,
                        boxRect,
                        workGiverPriority,
                        presentation.HasPawnOverride,
                        presentation.InheritedPriority);
                }
            }
        }

        private static void DrawGlobalPriorityBox(
            WorkGiver wg,
            Rect boxRect,
            int workGiverPriority,
            WorkGiverCellPresentationCache.CellPresentation presentation)
        {
            RegisterGlobalPriorityTarget(wg.def, boxRect);

            if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.SubWorkGlobalVanillaPriorityBoxes))
            {
                DrawVanillaGlobalPriorityBoxContents(boxRect, workGiverPriority, presentation.HasScheduleIndicator);
            }
            else
            {
                DrawPriorityBoxContents(boxRect, workGiverPriority, false, presentation.HasScheduleIndicator);
            }

            if (presentation.HasScheduleIndicator || presentation.HasGlobalOverride)
            {
                DrawOverrideRingIfVisible(boxRect);
            }

            if (ShouldHandleInput)
            {
                if (TryHandleScheduleIndicatorClick(
                        presentation.HasScheduleIndicator,
                        presentation.ScheduleTarget,
                        presentation.ScheduleFallbackPriority,
                        boxRect))
                {
                    return;
                }

                HandlePriorityClick(-1, null, wg.def, boxRect, workGiverPriority);
            }
        }

        private static void DrawVanillaGlobalPriorityBoxContents(Rect boxRect, int priority, bool suppressHover = false)
        {
            priority = WorkPrioritySystem.ClampPriority(priority);
            Texture2D bgTex = priority == WorkPrioritySystem.DisabledPriority
                ? WidgetsWork.WorkBoxBGTex_Bad
                : WidgetsWork.WorkBoxBGTex_Mid;

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            GUI.color = WithVisualAlpha(oldColor);
            GUI.DrawTexture(boxRect, bgTex);

            if (ParentPriorityRead.GetObservedManualModeForDisplay(
                    true))
            {
                if (priority > WorkPrioritySystem.DisabledPriority)
                {
                    Text.Font = boxRect.width <= WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f
                        ? GameFont.Tiny
                        : GameFont.Medium;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = WithVisualAlpha(WorkPrioritySystem.GetPriorityColor(priority));
                    Widgets.Label(boxRect.ContractedBy(-3f), GetPriorityLabel(priority));
                }
            }
            else if (priority > WorkPrioritySystem.DisabledPriority)
            {
                GUI.color = WithVisualAlpha(oldColor);
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (!suppressHover && MouseOverPriorityBox(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }
        }

        private static void DrawPriorityBoxContents(Rect boxRect, int priority, bool incapable, bool suppressHover = false)
        {
            priority = WorkPrioritySystem.ClampPriority(priority);
            Texture2D bgTex = priority == WorkPrioritySystem.DisabledPriority
                ? WidgetsWork.WorkBoxBGTex_Bad 
                : WidgetsWork.WorkBoxBGTex_Mid;

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            if (incapable)
            {
                GUI.color = WithVisualAlpha(new Color(1f, 0.3f, 0.3f));
            }
            else
            {
                GUI.color = WithVisualAlpha(oldColor);
            }

            GUI.DrawTexture(boxRect, bgTex);
            GUI.color = oldColor;

            if (ParentPriorityRead.GetObservedManualModeForDisplay(
                    true) &&
                priority > WorkPrioritySystem.DisabledPriority)
            {
                Text.Font = boxRect.width <= WorkPriorityCellGeometry.CompactSubWorkBoxSize + 0.01f
                    ? GameFont.Tiny
                    : GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = WithVisualAlpha(WorkPrioritySystem.GetPriorityColor(priority));
                Widgets.Label(boxRect.ContractedBy(-3f), GetPriorityLabel(priority));
            }
            else if (priority > WorkPrioritySystem.DisabledPriority)
            {
                GUI.color = WithVisualAlpha(oldColor);
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (!suppressHover && MouseOverPriorityBox(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }
        }

        private static bool DrawPawnWorkBoxContents(
            Rect boxRect,
            Pawn pawn,
            WorkTypeDef workType,
            int priority,
            WorkGiverCellPresentationCache.CellPresentation presentation,
            bool suppressHover = false)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            if (presentation.WorkTypeDisabled)
            {
                if (presentation.DisabledByAge)
                {
                    if (Event.current.type == EventType.MouseDown && MouseOverPriorityBox(boxRect))
                    {
                        Messages.Message(
                            "MessageWorkTypeDisabledAge".Translate(
                                pawn,
                                pawn.ageTracker.AgeBiologicalYears,
                                workType.labelShort,
                                presentation.MinimumAge),
                            pawn,
                            MessageTypeDefOf.RejectInput,
                            false);
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        Event.current.Use();
                    }

                }
            }

            if (!PreparedWorkBoxRenderer.Draw(
                    boxRect,
                    presentation.WorkBoxVisual,
                    priority,
                    _visualAlpha))
            {
                return false;
            }

            if (!suppressHover && MouseOverPriorityBox(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }

            return true;
        }

        private static void DrawInheritedDisabledPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect)
        {
            Color oldColor = GUI.color;

            GUI.color = WithVisualAlpha(new Color(0.52f, 0.52f, 0.52f, 0.82f));
            GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxBGTex_Bad);
            GUI.color = WithVisualAlpha(new Color(0.18f, 0.18f, 0.18f, 0.42f));
            GUI.DrawTexture(boxRect, BaseContent.WhiteTex);

            GUI.color = oldColor;

            if (MouseOverPriorityBox(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }

            if (ShouldHandleInput)
            {
                HandleInheritedDisabledClick(wg, pawn, workType, boxRect);
            }
        }

        private static void DrawParentDisabledOverrideBox(
            WorkGiver wg,
            WorkTypeDef workType,
            Pawn pawn,
            Rect boxRect,
            int workGiverPriority,
            WorkGiverCellPresentationCache.CellPresentation presentation)
        {
            if (!DrawPawnWorkBoxContents(boxRect, pawn, workType, workGiverPriority, presentation, suppressHover: true))
            {
                return;
            }

            Color oldColor = GUI.color;
            GUI.color = WithVisualAlpha(new Color(0.08f, 0.08f, 0.08f, 0.45f));
            GUI.DrawTexture(boxRect, BaseContent.WhiteTex);
            GUI.color = oldColor;

            DrawOverrideRingIfVisible(boxRect);
            DrawOverrideResetAnimation(pawn.thingIDNumber, wg.def, boxRect);
            TooltipHandler.TipRegion(boxRect, "BWT_SpecificJob_ParentDisabledOverride".Translate());
            if (ShouldHandleInput)
            {
                HandleParentDisabledOverrideClick(wg, pawn, workType, boxRect, workGiverPriority);
            }
        }

        private static void HandleParentDisabledOverrideClick(WorkGiver wg, Pawn pawn, WorkTypeDef workType, Rect boxRect, int currentPriority)
        {
            Event evt = Event.current;
            if (evt == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                wg?.def == null ||
                evt.type != EventType.MouseDown ||
                !PriorityOverrideRing.RingRect(boxRect).Contains(evt.mousePosition) ||
                BetterWorkTabLocalState.IsHeaderDragging ||
                SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return;
            }

            if (evt.button != 0)
            {
                evt.Use();
                return;
            }

            if (PriorityOverrideRing.EventOverVisibleRing(evt, boxRect))
            {
                int targetPriority = WorkTabEffectiveStateRuntime.IsPreviewActive
                    ? WorkGiverReassignmentManager.GetWorkGiverPriority(
                        pawn,
                        wg.def,
                        ParentPriorityRead.GetObserved(pawn, workType))
                    : WorkGiverReassignmentManager.GetInheritedWorkGiverPriority(
                        pawn,
                        workType,
                        wg.def);
                ClearPawnOverrideWithFeedback(
                    pawn,
                    workType,
                    wg.def,
                    boxRect,
                    currentPriority,
                    targetPriority);
                evt.Use();
                return;
            }

            if (PriorityOverrideRing.InnerRect(boxRect).Contains(evt.mousePosition))
            {
                bool accepted = WorkTabEffectiveStateRuntime.IsPreviewActive
                    ? WorkPriorityCommandGateway.TrySetPreviewParentPriority(
                        pawn,
                        workType,
                        WorkPrioritySystem.GetDefaultEnabledPriority())
                    : EnableParentWorkTypeLive(pawn, workType, wg.def);
                if (!accepted)
                {
                    evt.Use();
                    return;
                }

                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                evt.Use();
            }
        }

        private static void HandleInheritedDisabledClick(WorkGiver wg, Pawn pawn, WorkTypeDef workType, Rect boxRect)
        {
            Event evt = Event.current;
            if (evt == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                wg?.def == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel) ||
                !MouseOverPriorityBox(boxRect) ||
                BetterWorkTabLocalState.IsHeaderDragging ||
                SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button != 0 && evt.button != 1)
            {
                return;
            }

            if (evt.type == EventType.ScrollWheel && !(BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false))
            {
                return;
            }

            int newPriority;
            if (evt.type == EventType.ScrollWheel)
            {
                int direction = evt.delta.y > 0f ? -1 : 1;
                newPriority = ParentPriorityRead.GetObservedManualMode(
                        pawn,
                        workType,
                        true)
                    ? WorkPrioritySystem.GetPriorityAfterBoundedStep(WorkPrioritySystem.DisabledPriority, direction)
                    : ToggleNonManualPriority(WorkPrioritySystem.DisabledPriority);
            }
            else
            {
                newPriority = GetNextPriority(
                    WorkPrioritySystem.DisabledPriority,
                    evt.button,
                    pawn,
                    workType);
            }

            if (newPriority > WorkPrioritySystem.DisabledPriority)
            {
                bool accepted;
                if (WorkTabEffectiveStateRuntime.IsPreviewActive)
                {
                    accepted = WorkPriorityCommandGateway.TryClearPreviewSpecificJobOverrides(
                        InputApplication,
                        pawn,
                        workType) &&
                        WorkPriorityCommandGateway.TrySetPreviewParentPriority(
                            pawn,
                            workType,
                            WorkPrioritySystem.GetDefaultEnabledPriority()) &&
                        WorkPriorityCommandGateway.SetWorkGiverPriority(
                            InputApplication,
                            pawn.thingIDNumber,
                            wg.def,
                            newPriority);
                }
                else
                {
                    accepted = InputApplication?
                        .EnableParentFromSpecific(
                            pawn,
                            wg.def,
                            newPriority).Accepted == true;
                }

                if (!accepted)
                {
                    evt.Use();
                    return;
                }

                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            evt.Use();
        }

        private static bool TryHandleScheduleIndicatorClick(
            bool hasScheduleIndicator,
            TimePriorityTarget target,
            int fallbackPriority,
            Rect boxRect)
        {
            if (!hasScheduleIndicator)
            {
                return false;
            }

            Event evt = Event.current;
            if (evt == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                BetterWorkTabLocalState.IsHeaderDragging ||
                SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return false;
            }

            Rect ringRect = PriorityOverrideRing.RingRect(boxRect);
            if (!ringRect.Contains(evt.mousePosition))
            {
                return false;
            }

            if (evt.type == EventType.ScrollWheel && !PriorityOverrideRing.InnerRect(boxRect).Contains(evt.mousePosition))
            {
                evt.Use();
                return true;
            }

            if (evt.type != EventType.MouseDown || !PriorityOverrideRing.EventOverVisibleRing(evt, boxRect))
            {
                return false;
            }

            if (evt.button == 0 &&
                WorkPriorityCommandGateway.OpenSchedule(target, boxRect, fallbackPriority))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }

            evt.Use();
            return true;
        }

        private static void HandlePriorityClick(
            int pawnId,
            WorkTypeDef workType,
            WorkGiverDef workGiverDef,
            Rect boxRect,
            int currentPriority,
            bool hasPawnOverride = false,
            int inheritedPriority = WorkPrioritySystem.DisabledPriority)
        {
            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                return;
            }

            if (SubWorkDrilldownInput.MatchesGesture(Event.current))
            {
                return;
            }

            Rect interactiveRect = hasPawnOverride
                ? PriorityOverrideRing.RingRect(boxRect)
                : boxRect;
            if (!MouseOverPriorityBox(interactiveRect))
            {
                return;
            }

            Event evt = Event.current;
            if (evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown &&
                evt.button == 1 &&
                WorkTabEffectiveStateRuntime.IsPreviewActive &&
                WorkTabEffectiveStateRuntime.IsPreviewDimensionOwned(
                    WorkTabEffectiveStateDimension.SpecificJobOverride))
            {
                ShowPreviewSpecificJobMenu(
                    pawnId,
                    workType ?? WorkGiverReassignmentManager.GetTargetWorkType(workGiverDef) ??
                    workGiverDef?.workType,
                    workGiverDef,
                    currentPriority);
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDown)
            {
                if (hasPawnOverride)
                {
                    Rect innerRect = PriorityOverrideRing.InnerRect(boxRect);
                    if (evt.button == 0 && PriorityOverrideRing.EventOverVisibleRing(evt, boxRect))
                    {
                        Pawn pawn = ResolvePawn(pawnId);
                        if (pawn == null ||
                            !ClearPawnOverrideWithFeedback(
                                pawn,
                                workType ?? WorkGiverReassignmentManager.GetTargetWorkType(workGiverDef),
                                workGiverDef,
                                boxRect,
                                currentPriority,
                                inheritedPriority))
                        {
                            evt.Use();
                            return;
                        }
                        evt.Use();
                        return;
                    }

                    if (!innerRect.Contains(evt.mousePosition))
                    {
                        evt.Use();
                        return;
                    }
                }

                int newPriority = GetNextPriority(
                    currentPriority,
                    evt.button,
                    ResolvePawn(pawnId),
                    workType ?? WorkGiverReassignmentManager.GetTargetWorkType(workGiverDef) ??
                    workGiverDef?.workType);
                
                if (newPriority != currentPriority)
                {
                    bool accepted = ApplyWorkGiverPriority(
                        pawnId,
                        workGiverDef,
                        newPriority);
                    if (accepted)
                    {
                        SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    }
                }
                
                evt.Use();
                return;
            }

            if ((BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false) && evt.type == EventType.ScrollWheel)
            {
                if (hasPawnOverride && !PriorityOverrideRing.InnerRect(boxRect).Contains(evt.mousePosition))
                {
                    evt.Use();
                    return;
                }

                int direction = evt.delta.y > 0f ? -1 : 1;
                Pawn pawn = ResolvePawn(pawnId);
                int newPriority = ParentPriorityRead.GetObservedManualMode(
                        pawn,
                        workType ?? WorkGiverReassignmentManager.GetTargetWorkType(workGiverDef) ??
                        workGiverDef?.workType,
                        true)
                    ? WorkPrioritySystem.GetPriorityAfterBoundedStep(currentPriority, direction)
                    : ToggleNonManualPriority(currentPriority);

                if (newPriority != currentPriority)
                {
                    bool accepted = ApplyWorkGiverPriority(
                        pawnId,
                        workGiverDef,
                        newPriority);
                    if (accepted)
                    {
                        SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    }
                }

                evt.Use();
            }
        }

        private static void ShowPreviewSpecificJobMenu(
            int pawnId,
            WorkTypeDef workType,
            WorkGiverDef workGiverDef,
            int displayedPriority)
        {
            if (workType == null || workGiverDef == null)
            {
                WorkTabEffectiveStateRuntime.ReportBlocked(
                    WorkTabEffectiveStateDimension.SpecificJobOverride,
                    "BWT_Workload_SpecificJobOrderTargetMissing".Translate());
                return;
            }

            WorkTabSpecificJobTarget target = WorkTabSpecificJobTarget.For(
                pawnId >= 0 ? ResolvePawn(pawnId) : null,
                workType,
                workGiverDef);
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "BWT_Workload_ClearSpecificJobPriority".Translate(),
                    () => WorkTabEffectiveStateRuntime.TrySetPreviewSpecificJobPriorityIntent(
                        target,
                        WorkTabEffectiveStateResolution<int>.Clear,
                        out _)),
                new FloatMenuOption(
                    "BWT_Workload_LeaveSpecificJobPriorityUnchanged".Translate(),
                    () => WorkTabEffectiveStateRuntime.TrySetPreviewSpecificJobPriorityIntent(
                        target,
                        WorkTabEffectiveStateResolution<int>.NoOpinion,
                        out _))
            };

            int priority = WorkPrioritySystem.ClampPriority(displayedPriority);
            options.Add(new FloatMenuOption(
                "BWT_Workload_SaveDisplayedSpecificJobPriority".Translate(),
                () => WorkTabEffectiveStateRuntime.TrySetPreviewSpecificJobPriorityIntent(
                    target,
                    WorkTabEffectiveStateResolution<int>.Set(priority),
                    out _)));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static int GetNextPriority(
            int currentPriority,
            int button,
            Pawn pawn,
            WorkTypeDef workType)
        {
            if (ParentPriorityRead.GetObservedManualMode(
                    pawn,
                    workType,
                    true))
            {
                return WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, button);
            }

            if (button != 0)
            {
                return WorkPrioritySystem.ClampPriority(currentPriority);
            }

            return currentPriority > WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : WorkPrioritySystem.GetDefaultEnabledPriority();
        }

        private static bool ApplyWorkGiverPriority(
            int pawnId,
            WorkGiverDef workGiverDef,
            int priority)
        {
            if (WorkTabEffectiveStateRuntime.IsPreviewActive)
            {
                return WorkPriorityCommandGateway.SetWorkGiverPriority(
                    InputApplication,
                    pawnId,
                    workGiverDef,
                    priority);
            }

            if (FluffyTimeScheduleAssigner.ApplyWorkGiverPriority(
                    pawnId,
                    workGiverDef,
                    priority))
            {
                return true;
            }

            return WorkPriorityCommandGateway.SetWorkGiverPriority(
                InputApplication,
                pawnId,
                workGiverDef,
                priority);
        }

        private static int ToggleNonManualPriority(int currentPriority)
        {
            return currentPriority > WorkPrioritySystem.DisabledPriority
                ? WorkPrioritySystem.DisabledPriority
                : WorkPrioritySystem.GetDefaultEnabledPriority();
        }

        private static void DrawOverrideRingIfVisible(Rect boxRect)
        {
            if (_visualAlpha < 0.999f)
            {
                return;
            }

            PriorityOverrideRing.Draw(boxRect);
        }

        private static void RegisterGlobalPriorityTarget(WorkGiverDef workGiverDef, Rect boxRect)
        {
            if (workGiverDef?.defName == null)
            {
                return;
            }

            GlobalPriorityTargets[workGiverDef.defName] = UnclipRect(boxRect);
        }

        private static bool ClearPawnOverrideWithFeedback(
            Pawn pawn,
            WorkTypeDef workType,
            WorkGiverDef workGiverDef,
            Rect boxRect,
            int fromPriority,
            int toPriority)
        {
            if (pawn == null || workGiverDef == null)
            {
                return false;
            }

            if (!WorkTabEffectiveStateRuntime.IsPreviewActive &&
                !PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                return false;
            }

            bool accepted = WorkTabEffectiveStateRuntime.IsPreviewActive
                ? WorkTabEffectiveStateRuntime.TryClearPreviewSpecificJobPriority(
                    WorkTabSpecificJobTarget.For(
                        pawn,
                        workType ?? WorkGiverReassignmentManager.GetTargetWorkType(workGiverDef) ??
                        workGiverDef.workType,
                        workGiverDef),
                    out _)
                : ClearPawnOverrideLive(pawn, workGiverDef);
            if (!accepted)
            {
                return false;
            }

            int pawnId = pawn.thingIDNumber;
            if (BWTWorkTabEffectiveSettings.GetBool(SettingIDs.SubWorkOverrideBreakAnimation))
            {
                if (ResetAnimations.Count >= MaximumResetAnimations)
                {
                    ResetAnimations.Clear();
                }

                float startedAt = Time.realtimeSinceStartup;
                ResetAnimations[BuildAnimationKey(pawnId, workGiverDef)] = new ResetAnimationState
                {
                    StartedAt = startedAt,
                    FromPriority = WorkPrioritySystem.ClampPriority(fromPriority),
                    ToPriority = WorkPrioritySystem.ClampPriority(toPriority),
                    TargetBoxScreen = GetGlobalTargetBox(workGiverDef)
                };
                _resetAnimationsExpireAt = Mathf.Max(
                    _resetAnimationsExpireAt,
                    startedAt + OverrideResetAnimationSeconds);
            }

            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            return true;
        }

        private static bool EnableParentWorkTypeLive(Pawn pawn, WorkTypeDef workType, WorkGiverDef workGiver)
        {
            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData ||
                !WorkTabActionability.CanApplySpecific(pawn, workType, workGiver))
            {
                return false;
            }

            return InputApplication?
                .EnableParentFromSpecific(pawn, workGiver, null).Accepted == true;
        }

        private static bool ClearPawnOverrideLive(Pawn pawn, WorkGiverDef workGiverDef)
        {
            if (!PriorityAuthorityResolver.CanBetterWorkTabMutatePriorityData)
            {
                return false;
            }

            return InputApplication?
                .RemoveSpecificPriority(
                    pawn.thingIDNumber,
                    workGiverDef).Accepted == true;
        }

        private static Pawn ResolvePawn(int pawnId)
        {
            if (pawnId < 0)
            {
                return null;
            }

            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                if (pawn?.thingIDNumber == pawnId)
                {
                    return pawn;
                }
            }

            return null;
        }

        private static void DrawOverrideResetAnimation(int pawnId, WorkGiverDef workGiverDef, Rect boxRect)
        {
            if (!BWTWorkTabEffectiveSettings.GetBool(SettingIDs.SubWorkOverrideBreakAnimation))
            {
                return;
            }

            AnimationKey key = BuildAnimationKey(pawnId, workGiverDef);
            if (!ResetAnimations.TryGetValue(key, out ResetAnimationState animation))
            {
                return;
            }

            float age = Time.realtimeSinceStartup - animation.StartedAt;
            if (age >= OverrideResetAnimationSeconds)
            {
                ResetAnimations.Remove(key);
                return;
            }

            float t = Mathf.Clamp01(age / OverrideResetAnimationSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            DrawResetSourceBreak(boxRect, t);
            DrawResetPulseLines(animation, boxRect, t);
            DrawResetTargetShine(animation, boxRect, t);
            DrawResetPriorityCountdown(animation, boxRect, eased, 1f - t);
        }

        private static Rect? GetGlobalTargetBox(WorkGiverDef workGiverDef)
        {
            if (workGiverDef?.defName != null &&
                GlobalPriorityTargets.TryGetValue(workGiverDef.defName, out Rect rect))
            {
                return rect;
            }

            return null;
        }

        private static void DrawResetSourceBreak(Rect boxRect, float t)
        {
            float alpha = Mathf.Clamp01(1f - (t * 1.8f));
            if (alpha <= 0.001f)
            {
                return;
            }

            Rect rect = PriorityOverrideRing.RingRect(boxRect).ExpandedBy(2f + (4f * t));
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 0.86f, 0.28f, alpha);
            Widgets.DrawBox(rect, 2);
            GUI.color = oldColor;
        }

        private static void DrawResetPulseLines(ResetAnimationState animation, Rect boxRect, float t)
        {
            if (!animation.TargetBoxScreen.HasValue)
            {
                return;
            }

            Rect sourceRect = PriorityOverrideRing.RingRect(boxRect);
            Vector2 sourceScreen = GUIClipUtility.Unclip(boxRect.center);
            Rect targetRect = ToLocalRect(animation.TargetBoxScreen.Value, boxRect.center, sourceScreen);
            if (targetRect.yMin >= sourceRect.yMin - 1f)
            {
                return;
            }

            Vector2 leftStart = new Vector2(sourceRect.xMin, sourceRect.center.y);
            Vector2 rightStart = new Vector2(sourceRect.xMax, sourceRect.center.y);
            Vector2 leftEnd = new Vector2(targetRect.xMin, targetRect.yMax - 2f);
            Vector2 rightEnd = new Vector2(targetRect.xMax, targetRect.yMax - 2f);

            for (int i = 0; i < OverrideResetPulseCount; i++)
            {
                float localT = Mathf.Clamp01((t - (i * OverrideResetPulseDelay)) / OverrideResetPulseTravelSeconds);
                if (localT <= 0f || localT >= 1f)
                {
                    continue;
                }

                float eased = Mathf.SmoothStep(0f, 1f, localT);
                float trail = Mathf.Clamp01(eased - 0.22f);
                float alpha = Mathf.Sin(localT * Mathf.PI) * (0.86f - (i * 0.16f));
                float width = Mathf.Max(1f, 2.4f - (i * 0.35f));
                Color color = new Color(1f, 0.86f, 0.28f, Mathf.Clamp01(alpha));

                DrawPulseSegment(leftStart, leftEnd, trail, eased, color, width);
                DrawPulseSegment(rightStart, rightEnd, trail, eased, color, width);
            }
        }

        private static void DrawPulseSegment(Vector2 start, Vector2 end, float trail, float head, Color color, float width)
        {
            Vector2 trailPoint = Vector2.Lerp(start, end, trail);
            Vector2 headPoint = Vector2.Lerp(start, end, head);
            Widgets.DrawLine(trailPoint, headPoint, color, width);
        }

        private static void DrawResetTargetShine(ResetAnimationState animation, Rect boxRect, float t)
        {
            if (!animation.TargetBoxScreen.HasValue)
            {
                return;
            }

            float build = Mathf.Clamp01((t - 0.28f) / 0.30f);
            if (build <= 0.001f)
            {
                return;
            }

            float fade = 1f - Mathf.Clamp01((t - 0.62f) / 0.38f);
            float alpha = Mathf.Clamp01(Mathf.Sin(build * Mathf.PI * 0.5f) * fade);
            if (alpha <= 0.001f)
            {
                return;
            }

            Vector2 sourceScreen = GUIClipUtility.Unclip(boxRect.center);
            Rect targetRect = ToLocalRect(animation.TargetBoxScreen.Value, boxRect.center, sourceScreen).ExpandedBy(4f);
            DrawBuildBoxFromBottom(targetRect, build, new Color(1f, 0.86f, 0.28f, alpha));

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 0.96f, 0.62f, alpha * 0.14f);
            GUI.DrawTexture(targetRect.ContractedBy(2f), BaseContent.WhiteTex);
            GUI.color = oldColor;
        }

        private static void DrawBuildBoxFromBottom(Rect rect, float build, Color color)
        {
            float topY = Mathf.Lerp(rect.yMax, rect.yMin, build);
            Widgets.DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax), color, 2f);
            Widgets.DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, topY), color, 2f);
            Widgets.DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, topY), color, 2f);

            if (build >= 0.98f)
            {
                Widgets.DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), color, 2f);
            }
        }

        private static Rect UnclipRect(Rect rect)
        {
            Vector2 min = GUIClipUtility.Unclip(new Vector2(rect.xMin, rect.yMin));
            Vector2 max = GUIClipUtility.Unclip(new Vector2(rect.xMax, rect.yMax));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect ToLocalRect(Rect screenRect, Vector2 localReference, Vector2 screenReference)
        {
            Vector2 offset = localReference - screenReference;
            return new Rect(screenRect.x + offset.x, screenRect.y + offset.y, screenRect.width, screenRect.height);
        }

        private static void DrawResetPriorityCountdown(ResetAnimationState animation, Rect boxRect, float eased, float alpha)
        {
            int displayPriority = Mathf.RoundToInt(Mathf.Lerp(animation.FromPriority, animation.ToPriority, eased));
            displayPriority = WorkPrioritySystem.ClampPriority(displayPriority);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            Text.WordWrap = false;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = WorkPrioritySystem.GetPriorityColor(displayPriority);
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, Mathf.Clamp01(alpha));
            Widgets.Label(boxRect.ContractedBy(-3f), GetPriorityLabel(displayPriority));

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private static AnimationKey BuildAnimationKey(int pawnId, WorkGiverDef workGiverDef)
        {
            return new AnimationKey(pawnId, workGiverDef);
        }

        private static string GetPriorityLabel(int priority)
        {
            if (priority < 0 || priority >= PriorityLabels.Length)
            {
                return priority.ToString();
            }

            return PriorityLabels[priority] ?? (PriorityLabels[priority] = priority.ToString());
        }

        private readonly struct AnimationKey : IEquatable<AnimationKey>
        {
            private readonly int _pawnId;
            private readonly WorkGiverDef _workGiverDef;

            internal AnimationKey(int pawnId, WorkGiverDef workGiverDef)
            {
                _pawnId = pawnId;
                _workGiverDef = workGiverDef;
            }

            public bool Equals(AnimationKey other)
            {
                return _pawnId == other._pawnId && ReferenceEquals(_workGiverDef, other._workGiverDef);
            }

            public override bool Equals(object obj)
            {
                return obj is AnimationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_pawnId * 397) ^ (_workGiverDef?.shortHash ?? 0);
                }
            }
        }

        private sealed class ResetAnimationState
        {
            public float StartedAt;
            public int FromPriority;
            public int ToPriority;
            public Rect? TargetBoxScreen;
        }

    }
}
