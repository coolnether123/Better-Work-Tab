using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using RimWorld;
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
        public static void DrawPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect)
        {
            if (wg?.def == null)
            {
                return;
            }

            int defaultPriority = WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType);

            if (pawn != null &&
                !pawn.WorkTypeIsDisabled(workType) &&
                defaultPriority <= WorkPrioritySystem.DisabledPriority)
            {
                DrawInheritedDisabledPriorityBox(workType, pawn, boxRect);
                TooltipHandler.TipRegion(boxRect, wg.def.LabelCap);
                return;
            }

            int workGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, wg.def, defaultPriority);
            
            if (pawn != null)
            {
                DrawPawnPriorityBox(wg, workType, pawn, boxRect, workGiverPriority);
            }
            else
            {
                DrawGlobalPriorityBox(wg, boxRect, workGiverPriority);
            }
            
            TooltipHandler.TipRegion(boxRect, wg.def.LabelCap);
        }

        private static void DrawPawnPriorityBox(WorkGiver wg, WorkTypeDef workType, Pawn pawn, Rect boxRect, int workGiverPriority)
        {
            if (DrawPawnWorkBoxContents(boxRect, pawn, workType, workGiverPriority, IsIncapable(pawn, wg)))
            {
                HandlePriorityClick(pawn.thingIDNumber, wg.def, boxRect, workGiverPriority);
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

        private static void DrawInheritedDisabledPriorityBox(WorkTypeDef workType, Pawn pawn, Rect boxRect)
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

            HandleInheritedDisabledClick(pawn, workType, boxRect);
        }

        private static void HandleInheritedDisabledClick(Pawn pawn, WorkTypeDef workType, Rect boxRect)
        {
            Event evt = Event.current;
            if (evt == null ||
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

            WorkGiverReassignmentManager.EnableParentAndClearSubOverridesSynced(pawn.thingIDNumber, workType.defName);
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            evt.Use();
        }

        private static void HandlePriorityClick(int pawnId, WorkGiverDef workGiverDef, Rect boxRect, int currentPriority)
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
