using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.UI.Headers.Angled;
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
        private static readonly Dictionary<string, ResetAnimationState> ResetAnimations = new Dictionary<string, ResetAnimationState>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Vector2> GlobalPriorityTargets = new Dictionary<string, Vector2>(StringComparer.Ordinal);

        public static void DrawPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect)
        {
            if (wg?.def == null)
            {
                return;
            }

            int defaultPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            bool hasPawnOverride = pawn != null &&
                                   WorkGiverReassignmentManager.HasPawnWorkGiverOverride(pawn, wg.def);
            int baseWorkGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, wg.def, defaultPriority);
            int workGiverPriority = TimePriorityService.GetEffectiveWorkGiverPriority(
                pawn,
                workType,
                wg.def,
                baseWorkGiverPriority);

            if (pawn != null &&
                !pawn.WorkTypeIsDisabled(workType) &&
                defaultPriority <= WorkPrioritySystem.DisabledPriority)
            {
                if (hasPawnOverride &&
                    !WorkGiverReassignmentManager.LockedSubWorkOverridesDisabledParent())
                {
                    DrawParentDisabledOverrideBox(wg, workType, pawn, boxRect, workGiverPriority);
                    return;
                }

                DrawInheritedDisabledPriorityBox(wg, workType, pawn, boxRect);
                DrawOverrideResetAnimation(pawn.thingIDNumber, wg.def, boxRect);
                TooltipHandler.TipRegion(boxRect, wg.def.LabelCap);
                return;
            }

            if (pawn != null)
            {
                DrawPawnPriorityBox(wg, workType, pawn, boxRect, workGiverPriority, hasPawnOverride);
            }
            else
            {
                DrawGlobalPriorityBox(wg, boxRect, workGiverPriority);
            }
            
            TooltipHandler.TipRegion(boxRect, wg.def.LabelCap);
        }

        private static void DrawPawnPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect, int workGiverPriority, bool hasPawnOverride)
        {
            if (DrawPawnWorkBoxContents(boxRect, pawn, workType, workGiverPriority, IsIncapable(pawn, wg)))
            {
                DrawOverrideResetAnimation(pawn.thingIDNumber, wg.def, boxRect);
                if (hasPawnOverride)
                {
                    DrawOverrideRing(boxRect);
                }

                int inheritedPriority = WorkGiverReassignmentManager.GetInheritedWorkGiverPriority(pawn, workType, wg.def);
                HandlePriorityClick(pawn.thingIDNumber, wg.def, boxRect, workGiverPriority, hasPawnOverride, inheritedPriority);
            }
        }

        private static void DrawGlobalPriorityBox(WorkGiver wg, Rect boxRect, int workGiverPriority)
        {
            RegisterGlobalPriorityTarget(wg.def, boxRect);

            if (BetterWorkTabMod.Settings?.useVanillaSubWorkGlobalPriorityBoxes == true)
            {
                DrawVanillaGlobalPriorityBoxContents(boxRect, workGiverPriority);
            }
            else
            {
                DrawPriorityBoxContents(boxRect, workGiverPriority, false);
            }

            HandlePriorityClick(-1, wg.def, boxRect, workGiverPriority);
        }

        private static void DrawVanillaGlobalPriorityBoxContents(Rect boxRect, int priority)
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

            GUI.DrawTexture(boxRect, bgTex);

            if (Find.PlaySettings.useWorkPriorities)
            {
                if (priority > WorkPrioritySystem.DisabledPriority)
                {
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
                    Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
                }
            }
            else if (priority > WorkPrioritySystem.DisabledPriority)
            {
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }
        }

        private static void DrawPriorityBoxContents(Rect boxRect, int priority, bool incapable)
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
                GUI.color = new Color(1f, 0.3f, 0.3f);
            }

            GUI.DrawTexture(boxRect, bgTex);
            GUI.color = oldColor;

            if (priority > 0)
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
                Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }
        }

        private static bool DrawPawnWorkBoxContents(Rect boxRect, Pawn pawn, WorkTypeDef workType, int priority, bool incapable)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            priority = WorkPrioritySystem.ClampPriority(priority);
            if (pawn.WorkTypeIsDisabled(workType))
            {
                int minAgeRequired;
                if (pawn.IsWorkTypeDisabledByAge(workType, out minAgeRequired))
                {
                    if (Event.current.type == EventType.MouseDown && Mouse.IsOver(boxRect))
                    {
                        Messages.Message(
                            "MessageWorkTypeDisabledAge".Translate(pawn, pawn.ageTracker.AgeBiologicalYears, workType.labelShort, minAgeRequired),
                            pawn,
                            MessageTypeDefOf.RejectInput,
                            false);
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        Event.current.Use();
                    }

                    GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxBGTex_AgeDisabled);
                }

                return false;
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;

            if (incapable)
            {
                GUI.color = new Color(1f, 0.3f, 0.3f);
            }

            WidgetsWork.DrawWorkBoxBackground(boxRect, pawn, workType);
            GUI.color = oldColor;

            if (Find.PlaySettings.useWorkPriorities)
            {
                if (priority > WorkPrioritySystem.DisabledPriority)
                {
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
                    Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
                }
            }
            else if (priority > WorkPrioritySystem.DisabledPriority)
            {
                GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
            }

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }

            return true;
        }

        private static void DrawInheritedDisabledPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            GUI.color = new Color(0.52f, 0.52f, 0.52f, 0.82f);
            GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxBGTex_Bad);
            GUI.color = new Color(0.18f, 0.18f, 0.18f, 0.42f);
            GUI.DrawTexture(boxRect, BaseContent.WhiteTex);

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }

            HandleInheritedDisabledClick(wg, pawn, workType, boxRect);
        }

        private static void DrawParentDisabledOverrideBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect, int workGiverPriority)
        {
            if (!DrawPawnWorkBoxContents(boxRect, pawn, workType, workGiverPriority, IsIncapable(pawn, wg)))
            {
                return;
            }

            Color oldColor = GUI.color;
            GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.45f);
            GUI.DrawTexture(boxRect, BaseContent.WhiteTex);
            GUI.color = oldColor;

            DrawOverrideRing(boxRect);
            DrawOverrideResetAnimation(pawn.thingIDNumber, wg.def, boxRect);
            TooltipHandler.TipRegion(boxRect, "Parent work is disabled. Click the priority box to enable the parent work type; click the gold ring to follow the global sub-work priority again.");
            HandleParentDisabledOverrideClick(wg, pawn, workType, boxRect, workGiverPriority);
        }

        private static void HandleParentDisabledOverrideClick(WorkGiver wg, Pawn pawn, WorkTypeDef workType, Rect boxRect, int currentPriority)
        {
            Event evt = Event.current;
            if (evt == null ||
                wg?.def == null ||
                evt.type != EventType.MouseDown ||
                !Mouse.IsOver(PriorityOverrideRing.RingRect(boxRect)) ||
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

            if (PriorityOverrideRing.MouseOverVisibleRing(boxRect))
            {
                int targetPriority = WorkGiverReassignmentManager.GetInheritedWorkGiverPriority(pawn, workType, wg.def);
                ClearPawnOverrideWithFeedback(pawn.thingIDNumber, wg.def, boxRect, currentPriority, targetPriority);
                evt.Use();
                return;
            }

            if (Mouse.IsOver(PriorityOverrideRing.InnerRect(boxRect)))
            {
                WorkGiverReassignmentManager.EnableParentWorkTypeSynced(pawn.thingIDNumber, workType.defName);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                evt.Use();
            }
        }

        private static void HandleInheritedDisabledClick(WorkGiver wg, Pawn pawn, WorkTypeDef workType, Rect boxRect)
        {
            Event evt = Event.current;
            if (evt == null ||
                wg?.def == null ||
                (evt.type != EventType.MouseDown && evt.type != EventType.ScrollWheel) ||
                !Mouse.IsOver(boxRect) ||
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
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            evt.Use();
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
            if (!Mouse.IsOver(interactiveRect))
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
                    if (evt.button == 0 && PriorityOverrideRing.MouseOverVisibleRing(boxRect))
                    {
                        ClearPawnOverrideWithFeedback(pawnId, workGiverDef, boxRect, currentPriority, inheritedPriority);
                        evt.Use();
                        return;
                    }

                    if (!Mouse.IsOver(innerRect))
                    {
                        evt.Use();
                        return;
                    }
                }

                int newPriority = GetNextPriority(currentPriority, evt.button);
                
                if (newPriority != currentPriority)
                {
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(pawnId, workGiverDef.defName, newPriority);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                }
                
                evt.Use();
                return;
            }

            if ((BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false) && evt.type == EventType.ScrollWheel)
            {
                if (hasPawnOverride && !Mouse.IsOver(PriorityOverrideRing.InnerRect(boxRect)))
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
                    WorkGiverReassignmentManager.SetPawnOverrideSynced(pawnId, workGiverDef.defName, newPriority);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
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

        private static void DrawOverrideRing(Rect boxRect)
        {
            PriorityOverrideRing.Draw(boxRect);
        }

        private static void RegisterGlobalPriorityTarget(WorkGiverDef workGiverDef, Rect boxRect)
        {
            if (workGiverDef?.defName == null)
            {
                return;
            }

            GlobalPriorityTargets[workGiverDef.defName] = GUIClipUtility.Unclip(boxRect.center);
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
                ResetAnimations[BuildAnimationKey(pawnId, workGiverDef)] = new ResetAnimationState
                {
                    StartedAt = Time.realtimeSinceStartup,
                    FromPriority = WorkPrioritySystem.ClampPriority(fromPriority),
                    ToPriority = WorkPrioritySystem.ClampPriority(toPriority),
                    SourceCenterScreen = GUIClipUtility.Unclip(boxRect.center),
                    TargetCenterScreen = GetGlobalTargetCenter(workGiverDef)
                };
            }

            WorkGiverReassignmentManager.ClearPawnOverrideSynced(pawnId, workGiverDef.defName);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static void DrawOverrideResetAnimation(int pawnId, WorkGiverDef workGiverDef, Rect boxRect)
        {
            if (!(BetterWorkTabMod.Settings?.enableSubWorkOverrideBreakAnimation ?? DefaultSettings.enableSubWorkOverrideBreakAnimation))
            {
                return;
            }

            string key = BuildAnimationKey(pawnId, workGiverDef);
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
            Rect rect = PriorityOverrideRing.RingRect(boxRect).ExpandedBy(5f * t);
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 1f - t);
            Widgets.DrawBox(rect, 2);
            DrawResetWave(animation, boxRect, eased, 1f - t);
            DrawResetPriorityCountdown(animation, boxRect, eased, 1f - t);
            GUI.color = oldColor;
        }

        private static Vector2? GetGlobalTargetCenter(WorkGiverDef workGiverDef)
        {
            if (workGiverDef?.defName != null &&
                GlobalPriorityTargets.TryGetValue(workGiverDef.defName, out Vector2 center))
            {
                return center;
            }

            return null;
        }

        private static void DrawResetWave(ResetAnimationState animation, Rect boxRect, float eased, float alpha)
        {
            if (!animation.TargetCenterScreen.HasValue)
            {
                return;
            }

            Vector2 sourceLocal = boxRect.center;
            Vector2 sourceScreen = GUIClipUtility.Unclip(sourceLocal);
            Vector2 targetLocal = sourceLocal + (animation.TargetCenterScreen.Value - sourceScreen);
            if ((targetLocal - sourceLocal).sqrMagnitude < 4f)
            {
                return;
            }

            Vector2 pulse = Vector2.Lerp(sourceLocal, targetLocal, eased);
            Vector2 trailStart = Vector2.Lerp(sourceLocal, pulse, Mathf.Max(0f, eased - 0.22f));
            Color color = new Color(1f, 0.86f, 0.28f, Mathf.Clamp01(alpha * 0.85f));
            Widgets.DrawLine(trailStart, pulse, color, 2f);

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            Widgets.DrawBox(new Rect(pulse.x - 3f, pulse.y - 3f, 6f, 6f), 1);
            GUI.color = oldColor;
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
            Widgets.Label(boxRect.ContractedBy(-3f), displayPriority.ToString());

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            Text.WordWrap = oldWordWrap;
        }

        private static string BuildAnimationKey(int pawnId, WorkGiverDef workGiverDef)
        {
            return pawnId + ":" + (workGiverDef?.defName ?? string.Empty);
        }

        private sealed class ResetAnimationState
        {
            public float StartedAt;
            public int FromPriority;
            public int ToPriority;
            public Vector2 SourceCenterScreen;
            public Vector2? TargetCenterScreen;
        }

        private static bool IsIncapable(Pawn pawn, WorkGiver wg)
        {
            if (wg.def.requiredCapacities == null) return false;
            
            foreach (var cap in wg.def.requiredCapacities)
            {
                if (!pawn.health.capacities.CapableOf(cap))
                {
                    return true;
                }
            }
            
            return false;
        }

    }
}
