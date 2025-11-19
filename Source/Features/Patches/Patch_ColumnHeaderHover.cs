using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    public static class ColumnHoverManager
    {
        public static WorkTypeDef HoveredWorkType { get; private set; }

        public static void Set(WorkTypeDef workType) => HoveredWorkType = workType;
        public static void Clear() => HoveredWorkType = null;
    }

    [StaticConstructorOnStartup]
    public static class Patch_ColumnHeaderHover
    {
        private static readonly List<Rect> HeaderRects =
            new List<Rect>();

        private static readonly List<Rect> CurrentFrameHeaderRects =
            new List<Rect>();

        private static Vector2 _lastMousePos;
        private static bool _mouseMoved;

        static Patch_ColumnHeaderHover()
        {
            var harmony = new Harmony(
                "Coolnether123.betterworktab.columnhover");

            harmony.Patch(
                AccessTools.Method(
                    typeof(MainTabWindow_Work),
                    nameof(MainTabWindow_Work.DoWindowContents)),
                prefix: new HarmonyMethod(
                    typeof(Patch_ColumnHeaderHover),
                    nameof(OnFrameStart))
            );

            harmony.Patch(
                AccessTools.Method(
                    typeof(PawnColumnWorker_WorkPriority),
                    nameof(PawnColumnWorker_WorkPriority.DoHeader)),
                postfix: new HarmonyMethod(
                    typeof(Patch_ColumnHeaderHover),
                    nameof(OnHeaderDraw))
            );
        }

        /// <summary>
        /// Runs once per Work tab frame before drawing.
        /// Only recomputes hover when the mouse actually moved.
        /// </summary>
        private static void OnFrameStart()
        {
            var evt = Event.current;
            if (evt == null)
            {
                return;
            }

            var mousePos = evt.mousePosition;
            _mouseMoved =
                (mousePos - _lastMousePos).sqrMagnitude > 0.25f;
            _lastMousePos = mousePos;

            if (evt.type == EventType.MouseLeaveWindow)
            {
                HeaderRects.Clear();
                CurrentFrameHeaderRects.Clear();
                ColumnHoverManager.Clear();
                return;
            }

            // If mouse did not move, keep previous hover state.
            if (!_mouseMoved)
            {
                return;
            }

            // Reuse the header rects from last frame.
            HeaderRects.Clear();
            HeaderRects.AddRange(CurrentFrameHeaderRects);
            CurrentFrameHeaderRects.Clear();

            // Mouse moved, but not over any header -> clear hover.
            if (!IsMouseOverAnyHeader(mousePos, HeaderRects))
            {
                ColumnHoverManager.Clear();
            }
        }

        /// <summary>
        /// Called once per work column header during repaint.
        /// Only does work when the mouse moved this frame.
        /// </summary>
        private static void OnHeaderDraw(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect)
        {
            var evt = Event.current;
            if (evt == null
                || evt.type != EventType.Repaint
                || !_mouseMoved)
            {
                return;
            }

            CurrentFrameHeaderRects.Add(rect);

            if (rect.Contains(_lastMousePos))
            {
                ColumnHoverManager.Set(__instance.def.workType);
            }
        }

        private static bool IsMouseOverAnyHeader(
            Vector2 mousePosition,
            List<Rect> rects)
        {
            if (rects.Count == 0)
            {
                return false;
            }

            // Cheap span check first.
            float minX = rects[0].xMin;
            float maxX = rects[rects.Count - 1].xMax;

            if (mousePosition.x < minX || mousePosition.x > maxX)
            {
                return false;
            }

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