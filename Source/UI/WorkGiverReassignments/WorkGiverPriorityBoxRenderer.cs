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
            int workGiverPriority = WorkGiverReassignmentManager.GetWorkGiverPriority(pawn, wg.def, 3);
            
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
            // Save current WorkType priority
            int originalPriority = pawn.workSettings.GetPriority(workType);
            
            try
            {
                // Temporarily set WorkType priority to match WorkGiver priority for vanilla rendering
                if (workGiverPriority != originalPriority)
                {
                    pawn.workSettings.SetPriority(workType, workGiverPriority);
                }
                
                bool incapable = IsIncapable(pawn, wg);

                // Draw vanilla work box - this handles everything: background, flames, priority number, clicks
                WidgetsWork.DrawWorkBoxFor(boxRect.x, boxRect.y, pawn, workType, incapable);
                
                // Consume the click event so drag logic doesn't see it
                // Vanilla may or may not consume the event, so we ensure it's consumed
                if (Mouse.IsOver(boxRect) && Event.current.type == EventType.MouseDown)
                {
                    Event.current.Use();
                }
                
                // Check if the priority changed due to vanilla's click handling
                int newPriority = pawn.workSettings.GetPriority(workType);
                if (newPriority != workGiverPriority)
                {
                    // Vanilla changed it, sync to WorkGiver system
                    WorkGiverReassignmentManager.SyncSetPawnOverride(pawn.thingIDNumber, wg.def.defName, newPriority);
                }
            }
            finally
            {
                // Restore original WorkType priority
                int finalPawnPriority = pawn.workSettings.GetPriority(workType);
                if (originalPriority != finalPawnPriority)
                {
                    pawn.workSettings.SetPriority(workType, originalPriority);
                }
            }
        }

        private static void DrawGlobalPriorityBox(WorkGiver wg, Rect boxRect, int workGiverPriority)
        {
            // Draw background texture based on priority
            Texture2D bgTex = workGiverPriority == 0 
                ? WidgetsWork.WorkBoxBGTex_Bad 
                : WidgetsWork.WorkBoxBGTex_Mid;
            
            GUI.DrawTexture(boxRect, bgTex);
            
            // Draw priority number
            if (workGiverPriority > 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = WidgetsWork.ColorOfPriority(workGiverPriority);
                Widgets.Label(boxRect.ContractedBy(-3f), workGiverPriority.ToString());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            // Handle clicks
            if (Mouse.IsOver(boxRect) && Event.current.type == EventType.MouseDown)
            {
                int newPriority = HandleGlobalPriorityClick(workGiverPriority, Event.current.button);
                
                if (newPriority != workGiverPriority)
                {
                    WorkGiverReassignmentManager.SyncSetPawnOverride(-1, wg.def.defName, newPriority);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                }
                
                Event.current.Use();
            }
            
            if (Mouse.IsOver(boxRect))
            {
                Widgets.DrawHighlight(boxRect);
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

        private static int HandleGlobalPriorityClick(int currentPriority, int button)
        {
            if (button == 0) // Left click - decrease priority
            {
                int newPriority = currentPriority - 1;
                return newPriority < 0 ? 4 : newPriority;
            }
            else if (button == 1) // Right click - increase priority
            {
                int newPriority = currentPriority + 1;
                return newPriority > 4 ? 0 : newPriority;
            }
            
            return currentPriority;
        }
    }
}
