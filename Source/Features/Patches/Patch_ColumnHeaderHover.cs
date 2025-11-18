using HarmonyLib;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// A simple static class to track which work type column header is currently being hovered by the mouse.
    /// This state is used by the DoCell patch to dynamically change its rendering.
    /// </summary>
    public static class ColumnHoverManager
    {
        public static WorkTypeDef HoveredWorkType { get; private set; }

        public static void Set(WorkTypeDef workType) => HoveredWorkType = workType;
        public static void Clear() => HoveredWorkType = null;
    }

    /// <summary>
    /// This class contains the Harmony patches responsible for updating the ColumnHoverManager's state each frame.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_ColumnHeaderHover
    {
        private static readonly List<Rect> HeaderRects = new List<Rect>();
        private static readonly List<Rect> _currentFrameHeaderRects = new List<Rect>();

        static Patch_ColumnHeaderHover()
        {
            var harmony = new Harmony("Coolnether123.betterworktab.columnhover");

            // Patch DoWindowContents to clear the hover state at the start of each frame.
            harmony.Patch(
                AccessTools.Method(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents)),
                prefix: new HarmonyMethod(typeof(Patch_ColumnHeaderHover), nameof(ClearHoverState_Prefix))
            );

            // Patch DoHeader to set the hover state when the mouse is over a column header.
            harmony.Patch(
                AccessTools.Method(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader)),
                postfix: new HarmonyMethod(typeof(Patch_ColumnHeaderHover), nameof(SetHoverState_Postfix))
            );
        }

        /// <summary>
        /// Prefix patch that runs before the main window draws, ensuring the hover state is reset every frame.
        /// </summary>
        private static void ClearHoverState_Prefix()
        {
            HeaderRects.Clear();
            HeaderRects.AddRange(_currentFrameHeaderRects);
            _currentFrameHeaderRects.Clear();

            var evtType = Event.current.type;
            switch (evtType)
            {
                case EventType.Repaint:
                    if (!IsMouseOverAnyHeader(Event.current.mousePosition, HeaderRects))
                    {
                        ColumnHoverManager.Clear();
                    }
                    break;
                case EventType.MouseMove:
                    if (!IsMouseOverAnyHeader(Event.current.mousePosition, HeaderRects))
                    {
                        ColumnHoverManager.Clear();
                    }
                    break;
                case EventType.MouseLeaveWindow:
                    HeaderRects.Clear();
                    ColumnHoverManager.Clear();
                    break;
            }
        }

        /// <summary>
        /// Postfix patch that runs after a column header is drawn. It checks if the mouse is
        /// over the header and updates the hover manager accordingly.
        /// </summary>
        private static void SetHoverState_Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            _currentFrameHeaderRects.Add(rect);
            if (Mouse.IsOver(rect))
            {
                ColumnHoverManager.Set(__instance.def.workType);
            }
            else if (!IsMouseOverAnyHeader(Event.current.mousePosition, _currentFrameHeaderRects))
            {
                ColumnHoverManager.Clear();
            }
        }

        private static bool IsMouseOverAnyHeader(Vector2 mousePosition, List<Rect> rects)
        {
            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i].Contains(mousePosition))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
