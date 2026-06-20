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
            DrawPriorityBoxContents(boxRect, workGiverPriority, IsIncapable(pawn, wg));
            HandlePriorityClick(pawn.thingIDNumber, wg.def, boxRect, workGiverPriority);
        }

        private static void DrawGlobalPriorityBox(WorkGiver wg, Rect boxRect, int workGiverPriority)
        {
            DrawPriorityBoxContents(boxRect, workGiverPriority, false);
            HandlePriorityClick(-1, wg.def, boxRect, workGiverPriority);
        }

        private static void DrawPriorityBoxContents(Rect boxRect, int priority, bool incapable)
        {
            priority = WorkPrioritySystem.ClampPriority(priority);
            Texture2D bgTex = priority == WorkPrioritySystem.DisabledPriority
                ? WidgetsWork.WorkBoxBGTex_Bad 
                : WidgetsWork.WorkBoxBGTex_Mid;

            Color oldColor = GUI.color;
            if (incapable)
            {
                GUI.color = new Color(1f, 0.3f, 0.3f);
            }

            GUI.DrawTexture(boxRect, bgTex);
            GUI.color = oldColor;

            if (priority > 0)
            {
                var oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = WorkPrioritySystem.GetPriorityColor(priority);
                Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
            }

            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
            }
        }

        private static void HandlePriorityClick(int pawnId, WorkGiverDef workGiverDef, Rect boxRect, int currentPriority)
        {
            if (Mouse.IsOver(boxRect) && Event.current.type == EventType.MouseDown)
            {
                int newPriority = WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, Event.current.button);
                
                if (newPriority != currentPriority)
                {
                    WorkGiverReassignmentManager.SyncSetPawnOverride(pawnId, workGiverDef.defName, newPriority);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                }
                
                Event.current.Use();
            }
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
