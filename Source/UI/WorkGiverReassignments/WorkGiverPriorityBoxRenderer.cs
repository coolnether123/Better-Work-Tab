using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Commands;
using Better_Work_Tab.UI.Headers.Angled;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
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
        private static bool _trustedRootInputHit;

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
            WorkGiverCellPresentationCache.CellPresentation presentation =
                WorkGiverCellPresentationCache.Resolve(wg, workType, pawn, knownParentPriority);
            int workGiverPriority = FluffyTimeScheduleAssigner.IsOpen
                ? FluffyTimeScheduleAssigner.GetDisplayPriority(
                    TimePriorityTarget.ForWorkGiver(pawn, workType, wg.def),
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
        /// Handles a priority-box event whose hit was already resolved in the work
        /// tab's root coordinate space. Scroll views maintain their own mouse-over
        /// stack, so root-routed input must not be rejected by the nested stack.
        /// </summary>
        internal static bool TryHandleRootInput(
            WorkGiver workGiver,
            WorkTypeDef workType,
            Pawn pawn,
            Rect rootBoxRect,
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
            _trustedRootInputHit = true;
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

            if (BetterWorkTabMod.Settings?.useVanillaSubWorkGlobalPriorityBoxes == true)
            {
                DrawVanillaGlobalPriorityBoxContents(boxRect, workGiverPriority, presentation.HasScheduleIndicator);
            }
            else
            {
                DrawPriorityBoxContents(boxRect, workGiverPriority, false, presentation.HasScheduleIndicator);
            }

            if (presentation.HasScheduleIndicator)
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

                HandlePriorityClick(-1, wg.def, boxRect, workGiverPriority);
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

            if (Find.PlaySettings.useWorkPriorities)
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

            if (Find.PlaySettings.useWorkPriorities &&
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
            Color oldColor = GUI.color;
            if (presentation.WorkTypeDisabled)
            {
                if (presentation.DisabledByAge)
                {
                    if (Event.current.type == EventType.MouseDown && MouseOverPriorityBox(boxRect))
                    {
                        MessageCompat.Message(
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

                    GUI.color = WithVisualAlpha(oldColor);
                    GUI.DrawTexture(boxRect, WorkGiverPriorityBoxCompatibility.WorkBoxBGTexAgeDisabled);
                    GUI.color = oldColor;
                }

                return false;
            }

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            if (presentation.Incapable)
            {
                GUI.color = WithVisualAlpha(new Color(1f, 0.3f, 0.3f));
            }
            else
            {
                GUI.color = WithVisualAlpha(oldColor);
            }

            WorkGiverPriorityBoxCompatibility.DrawWorkBoxBackground(boxRect, pawn, workType);
            GUI.color = oldColor;

            if (Find.PlaySettings.useWorkPriorities)
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
            TooltipHandler.TipRegion(boxRect, "Parent work is disabled. Click the priority box to enable the parent work type; click the gold ring to follow the global sub-work priority again.");
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
                int targetPriority = WorkGiverReassignmentManager.GetInheritedWorkGiverPriority(pawn, workType, wg.def);
                ClearPawnOverrideWithFeedback(pawn.thingIDNumber, wg.def, boxRect, currentPriority, targetPriority);
                evt.Use();
                return;
            }

            if (PriorityOverrideRing.InnerRect(boxRect).Contains(evt.mousePosition))
            {
                WorkGiverReassignmentManager.EnableParentWorkTypeSynced(pawn.thingIDNumber, workType.defName);
                UISoundCompat.CheckboxTurnedOn.PlayOneShotOnCamera();
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
                newPriority = Find.PlaySettings.useWorkPriorities
                    ? WorkPrioritySystem.GetPriorityAfterBoundedStep(WorkPrioritySystem.DisabledPriority, direction)
                    : ToggleNonManualPriority(WorkPrioritySystem.DisabledPriority);
            }
            else
            {
                newPriority = GetNextPriority(WorkPrioritySystem.DisabledPriority, evt.button);
            }

            if (newPriority > WorkPrioritySystem.DisabledPriority)
            {
                WorkGiverReassignmentManager.EnableParentAndSetOnlySubOverrideSynced(
                    pawn.thingIDNumber,
                    workType.defName,
                    wg.def.defName,
                    newPriority);
                UISoundCompat.CheckboxTurnedOn.PlayOneShotOnCamera();
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
                WorkPriorityCommandGateway.Execute(new OpenScheduleCommand(target, boxRect, fallbackPriority)))
            {
                UISoundCompat.TickHigh.PlayOneShotOnCamera();
            }

            evt.Use();
            return true;
        }

        private static void HandlePriorityClick(
            int pawnId,
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

            if (evt.type == EventType.MouseDown)
            {
                if (hasPawnOverride)
                {
                    Rect innerRect = PriorityOverrideRing.InnerRect(boxRect);
                    if (evt.button == 0 && PriorityOverrideRing.EventOverVisibleRing(evt, boxRect))
                    {
                        ClearPawnOverrideWithFeedback(pawnId, workGiverDef, boxRect, currentPriority, inheritedPriority);
                        evt.Use();
                        return;
                    }

                    if (!innerRect.Contains(evt.mousePosition))
                    {
                        evt.Use();
                        return;
                    }
                }

                int newPriority = GetNextPriority(currentPriority, evt.button);
                
                if (newPriority != currentPriority)
                {
                    if (!FluffyTimeScheduleAssigner.ApplyWorkGiverPriority(pawnId, workGiverDef, newPriority))
                    {
                        WorkPriorityCommandGateway.Execute(new SetWorkGiverPriorityCommand(
                            pawnId,
                            workGiverDef,
                            newPriority));
                    }
                    UISoundCompat.DragSlider.PlayOneShotOnCamera();
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
                int newPriority = Find.PlaySettings.useWorkPriorities
                    ? WorkPrioritySystem.GetPriorityAfterBoundedStep(currentPriority, direction)
                    : ToggleNonManualPriority(currentPriority);

                if (newPriority != currentPriority)
                {
                    if (!FluffyTimeScheduleAssigner.ApplyWorkGiverPriority(pawnId, workGiverDef, newPriority))
                    {
                        WorkPriorityCommandGateway.Execute(new SetWorkGiverPriorityCommand(
                            pawnId,
                            workGiverDef,
                            newPriority));
                    }
                    UISoundCompat.DragSlider.PlayOneShotOnCamera();
                }

                evt.Use();
            }
        }

        private static int GetNextPriority(int currentPriority, int button)
        {
            if (Find.PlaySettings.useWorkPriorities)
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

        private static void ClearPawnOverrideWithFeedback(
            int pawnId,
            WorkGiverDef workGiverDef,
            Rect boxRect,
            int fromPriority,
            int toPriority)
        {
            if (BetterWorkTabMod.Settings?.enableSubWorkOverrideBreakAnimation ?? DefaultSettings.enableSubWorkOverrideBreakAnimation)
            {
                if (ResetAnimations.Count >= MaximumResetAnimations)
                {
                    ResetAnimations.Clear();
                }

                ResetAnimations[BuildAnimationKey(pawnId, workGiverDef)] = new ResetAnimationState
                {
                    StartedAt = Time.realtimeSinceStartup,
                    FromPriority = WorkPrioritySystem.ClampPriority(fromPriority),
                    ToPriority = WorkPrioritySystem.ClampPriority(toPriority),
                    TargetBoxScreen = GetGlobalTargetBox(workGiverDef)
                };
            }

            WorkGiverReassignmentManager.ClearPawnOverrideSynced(pawnId, workGiverDef.defName);
            UISoundCompat.TickLow.PlayOneShotOnCamera();
        }

        private static void DrawOverrideResetAnimation(int pawnId, WorkGiverDef workGiverDef, Rect boxRect)
        {
            if (!(BetterWorkTabMod.Settings?.enableSubWorkOverrideBreakAnimation ?? DefaultSettings.enableSubWorkOverrideBreakAnimation))
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

    internal static class WorkGiverPriorityBoxCompatibility
    {
        private static readonly MethodInfo IsWorkTypeDisabledByAgeMethod = typeof(Pawn).GetMethod(
            "IsWorkTypeDisabledByAge",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(WorkTypeDef), typeof(int).MakeByRefType() },
            null);

        private static readonly FieldInfo WorkBoxBGTexAgeDisabledField = typeof(WidgetsWork).GetField(
            "WorkBoxBGTex_AgeDisabled",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo WorkBoxBGTexAwfulField = typeof(WidgetsWork).GetField(
            "WorkBoxBGTex_Awful",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        internal static Texture2D WorkBoxBGTexAgeDisabled
        {
            get
            {
                try
                {
                    return WorkBoxBGTexAgeDisabledField?.GetValue(null) as Texture2D ?? WidgetsWork.WorkBoxBGTex_Bad;
                }
                catch
                {
                    return WidgetsWork.WorkBoxBGTex_Bad;
                }
            }
        }

        internal static bool IsWorkTypeDisabledByAge(Pawn pawn, WorkTypeDef workType, out int minimumAge)
        {
            minimumAge = 0;
            if (pawn == null || workType == null || IsWorkTypeDisabledByAgeMethod == null)
            {
                return false;
            }

            try
            {
                object[] arguments = { workType, minimumAge };
                bool disabled = (bool)IsWorkTypeDisabledByAgeMethod.Invoke(pawn, arguments);
                minimumAge = arguments[1] is int value ? value : 0;
                return disabled;
            }
            catch
            {
                return false;
            }
        }

        internal static void DrawWorkBoxBackground(Rect rect, Pawn pawn, WorkTypeDef workType)
        {
            SkillRecord skill = GetFirstRelevantSkill(pawn, workType);
            Texture2D background = GetLegacyWorkBoxBackground(skill);

            if (background != null)
            {
                GUI.DrawTexture(rect, background);
            }

            if (skill == null)
            {
                return;
            }

            if (skill.passion == Passion.Minor && WidgetsWork.PassionWorkboxMinorIcon != null)
            {
                GUI.DrawTexture(rect, WidgetsWork.PassionWorkboxMinorIcon);
            }
            else if (skill.passion == Passion.Major && WidgetsWork.PassionWorkboxMajorIcon != null)
            {
                GUI.DrawTexture(rect, WidgetsWork.PassionWorkboxMajorIcon);
            }
        }

        private static SkillRecord GetFirstRelevantSkill(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null || workType?.relevantSkills == null || workType.relevantSkills.Count == 0)
            {
                return null;
            }

            return pawn.skills.GetSkill(workType.relevantSkills[0]);
        }

        private static Texture2D GetLegacyWorkBoxBackground(SkillRecord skill)
        {
            if (skill == null)
            {
                return WidgetsWork.WorkBoxBGTex_Mid;
            }

            int level = SkillCompat.Level(skill);
            if (level <= 3)
            {
                return WorkBoxBGTexAwful;
            }

            if (level <= 7)
            {
                return WidgetsWork.WorkBoxBGTex_Bad;
            }

            if (level <= 13)
            {
                return WidgetsWork.WorkBoxBGTex_Mid;
            }

            return WidgetsWork.WorkBoxBGTex_Excellent;
        }

        private static Texture2D WorkBoxBGTexAwful
        {
            get
            {
                if (WorkBoxBGTexAwfulField == null)
                {
                    return WidgetsWork.WorkBoxBGTex_Bad;
                }

                try
                {
                    return WorkBoxBGTexAwfulField.GetValue(null) as Texture2D ?? WidgetsWork.WorkBoxBGTex_Bad;
                }
                catch
                {
                    return WidgetsWork.WorkBoxBGTex_Bad;
                }
            }
        }
    }
}
