using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
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
        private const float OverrideRingInset = -2f;
        private const float OverrideInnerInset = 5f;
        private const float OverrideResetAnimationSeconds = 0.35f;
        private static readonly Color OverrideRingColor = new Color(1f, 0.78f, 0.18f, 1f);
        private static readonly Dictionary<string, float> ResetAnimations = new Dictionary<string, float>(StringComparer.Ordinal);

        public static void DrawPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect)
        {
            if (wg?.def == null)
            {
                return;
            }

            int defaultPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);
            bool hasPawnOverride = pawn != null &&
                                   WorkGiverReassignmentManager.HasPawnWorkGiverOverride(pawn, wg.def);
            int workGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, wg.def, defaultPriority);

            if (pawn != null &&
                !pawn.WorkTypeIsDisabled(workType) &&
                defaultPriority <= WorkPrioritySystem.DisabledPriority)
            {
                if (hasPawnOverride &&
                    BetterWorkTabMod.Settings?.subWorkDisabledParentMode != BetterWorkTabSettings.SubWorkDisabledParentMode.LockedSubWorkOverridesParent)
                {
                    DrawParentDisabledOverrideBox(wg, workType, pawn, boxRect, workGiverPriority);
                    return;
                }

                DrawInheritedDisabledPriorityBox(wg, workType, pawn, boxRect);
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

                HandlePriorityClick(pawn.thingIDNumber, wg.def, boxRect, workGiverPriority, hasPawnOverride);
            }
        }

        private static void DrawGlobalPriorityBox(WorkGiver wg, Rect boxRect, int workGiverPriority)
        {
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
            HandleParentDisabledOverrideClick(wg, pawn, workType, boxRect);
        }

        private static void HandleParentDisabledOverrideClick(WorkGiver wg, Pawn pawn, WorkTypeDef workType, Rect boxRect)
        {
            Event evt = Event.current;
            if (evt == null ||
                wg?.def == null ||
                evt.type != EventType.MouseDown ||
                !Mouse.IsOver(boxRect) ||
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

            if (Mouse.IsOver(GetOverrideRingRect(boxRect)) && !Mouse.IsOver(GetOverrideInnerRect(boxRect)))
            {
                ClearPawnOverrideWithFeedback(pawn.thingIDNumber, wg.def, boxRect);
                evt.Use();
                return;
            }

            if (Mouse.IsOver(GetOverrideInnerRect(boxRect)))
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

        private static void HandlePriorityClick(int pawnId, WorkGiverDef workGiverDef, Rect boxRect, int currentPriority, bool hasPawnOverride = false)
        {
            if (BetterWorkTabLocalState.IsHeaderDragging)
            {
                return;
            }

            if (SubWorkDrilldownInput.MatchesGesture(Event.current))
            {
                return;
            }

            if (!Mouse.IsOver(boxRect))
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
                    Rect innerRect = GetOverrideInnerRect(boxRect);
                    if (evt.button == 0 && Mouse.IsOver(GetOverrideRingRect(boxRect)) && !Mouse.IsOver(innerRect))
                    {
                        ClearPawnOverrideWithFeedback(pawnId, workGiverDef, boxRect);
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
                if (hasPawnOverride && !Mouse.IsOver(GetOverrideInnerRect(boxRect)))
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

        private static Rect GetOverrideRingRect(Rect boxRect)
        {
            return boxRect.ContractedBy(OverrideRingInset);
        }

        private static Rect GetOverrideInnerRect(Rect boxRect)
        {
            return boxRect.ContractedBy(OverrideInnerInset);
        }

        private static void DrawOverrideRing(Rect boxRect)
        {
            Rect ringRect = GetOverrideRingRect(boxRect);
            Rect innerRect = GetOverrideInnerRect(boxRect);
            bool ringHovered = Mouse.IsOver(ringRect) && !Mouse.IsOver(innerRect);
            Color oldColor = GUI.color;
            GUI.color = ringHovered ? Color.white : OverrideRingColor;
            Widgets.DrawBox(ringRect, ringHovered ? 3 : 2);
            GUI.color = oldColor;
        }

        private static void ClearPawnOverrideWithFeedback(int pawnId, WorkGiverDef workGiverDef, Rect boxRect)
        {
            if (BetterWorkTabMod.Settings?.enableSubWorkOverrideBreakAnimation ?? DefaultSettings.enableSubWorkOverrideBreakAnimation)
            {
                ResetAnimations[BuildAnimationKey(pawnId, workGiverDef)] = Time.realtimeSinceStartup;
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
            if (!ResetAnimations.TryGetValue(key, out float startedAt))
            {
                return;
            }

            float age = Time.realtimeSinceStartup - startedAt;
            if (age >= OverrideResetAnimationSeconds)
            {
                ResetAnimations.Remove(key);
                return;
            }

            float t = Mathf.Clamp01(age / OverrideResetAnimationSeconds);
            Rect rect = GetOverrideRingRect(boxRect).ExpandedBy(5f * t);
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 1f - t);
            Widgets.DrawBox(rect, 2);
            GUI.color = oldColor;
        }

        private static string BuildAnimationKey(int pawnId, WorkGiverDef workGiverDef)
        {
            return pawnId + ":" + (workGiverDef?.defName ?? string.Empty);
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
