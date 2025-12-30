using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class PawnColumnWorker_WorkPriority_DoHeader_Patch
    {
        private static int _lastCachedFrame = -1;
        private static Vector2 _cachedMousePos;
        private static WorkTypeDef _cachedHoveredWorkType;
        private static Vector2 _lastMousePosChecked = new Vector2(float.NaN, float.NaN);
        private static WorkTypeDef _lastHoverWorkType;
        private static int _lastHoverResultFrame = -1;
        private static int _lastColumnsCount = -1;

        public static WorkTypeDef HoveredWorkType =>
            _lastCachedFrame == Time.frameCount ? _cachedHoveredWorkType : null;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            try
            {
                var evt = Event.current;
                var evtType = evt?.type ?? EventType.Layout;

                if (!BetterWorkTabMod.Settings.enableAngledHeaders)
                {
                    return true;
                }

                bool shouldDraw = evtType == EventType.Repaint;
                bool handleInput = evtType == EventType.MouseDown
                                   || evtType == EventType.MouseMove
                                   || evtType == EventType.MouseDrag
                                   || evtType == EventType.MouseUp;

                if (!shouldDraw && !handleInput)
                {
                    return false;
                }

                var workType = __instance?.def?.workType;
                if (workType == null)
                {
                    return false;
                }

                int columnsCount = PawnTableDefOf.Work?.columns?.Count ?? -1;
                int currentFrame = Time.frameCount;

                if (_lastCachedFrame != currentFrame || handleInput)
                {
                    _cachedMousePos = evt?.mousePosition ?? Vector2.zero;
                    _lastCachedFrame = currentFrame;
                    _cachedHoveredWorkType = null;
                }

                bool mouseUnchanged = _cachedMousePos == _lastMousePosChecked;
                bool reuseHover = mouseUnchanged
                                  && _lastHoverResultFrame == currentFrame - 1
                                  && _lastColumnsCount == columnsCount;

                if (!AngledHeaderCache.TryGetLayout(
                        rect,
                        workType,
                        AngledLabelDrawer.CurrentRotCos,
                        AngledLabelDrawer.CurrentRotSin,
                        AngledLabelDrawer.STEM_BOTTOM_GAP,
                        out var cached))
                {
                    return false;
                }

                bool isMouseOver = false;
                if (reuseHover)
                {
                    isMouseOver = _lastHoverWorkType == workType;
                }
                else if (_cachedMousePos.y >= rect.yMin && _cachedMousePos.y <= rect.yMax)
                {
                    isMouseOver = AngledHeaderCache.IsMouseOver(cached.Quad, _cachedMousePos);
                }

                if (isMouseOver)
                {
                    _cachedHoveredWorkType = workType;
                }

                _lastHoverWorkType = isMouseOver ? workType : null;
                _lastHoverResultFrame = currentFrame;
                _lastMousePosChecked = _cachedMousePos;
                _lastColumnsCount = columnsCount;

                AngledHeaderInteraction.HandleInteractions(
                    __instance,
                    table,
                    cached.Layout,
                    cached.Bounds,
                    cached.Quad,
                    isMouseOver,
                    shouldDraw,
                    rect);

                return false;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BWT] WorkPriority header failed: {ex}");
                // Fall back to vanilla drawing if something went wrong.
                return true;
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = new List<CodeInstruction>(instructions);
            var doRegionMethod = AccessTools.Method(typeof(Verse.Sound.MouseoverSounds), nameof(Verse.Sound.MouseoverSounds.DoRegion), new[] { typeof(Rect) });
            var drawLineVerticalMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawLineVertical));

            int startIndex = -1;
            int endIndex = -1;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(doRegionMethod))
                {
                    startIndex = i + 1;
                    break;
                }
            }

            if (startIndex != -1)
            {
                for (int i = code.Count - 1; i >= startIndex; i--)
                {
                    if (code[i].Calls(drawLineVerticalMethod))
                    {
                        endIndex = i;
                        break;
                    }
                }
            }

            if (startIndex != -1 && endIndex != -1 && endIndex >= startIndex)
            {
                code.RemoveRange(startIndex, endIndex - startIndex + 1);
            }

            return code;
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.GetMinHeaderHeight))]
    public static class Patch_PawnColumnWorker_WorkPriority_GetMinHeaderHeight
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, PawnTable table, ref int __result)
        {
            if (Find.MainTabsRoot?.OpenTab?.defName != "Work")
            {
                return;
            }

            if (!BetterWorkTabMod.Settings.enableAngledHeaders)
            {
                return;
            }

            // To prevent "Diagonal Clipping", we must ensure the header box is tall enough for the longest label.
            float maxTextWidth = 0f;
            var columns = table.def.columns;
            
            var originalFont = Text.Font;
            Text.Font = GameFont.Small;

            foreach (var col in columns)
            {
                if (col.workType != null)
                {
                    string baseText = col.workType.labelShort ?? col.workType.label ?? col.workType.defName ?? "Work";
                    string text = baseText.CapitalizeFirst();
                    if (MainTabWindow_BetterWork.ShouldShowColumnMarker(col.workType))
                    {
                        text += "*";
                    }
                    Vector2 size = Text.CalcSize(text);
                    if (size.x > maxTextWidth) maxTextWidth = size.x;
                }
            }

            float angleRad = Mathf.Abs(AngledLabelDrawer.CurrentRotation) * Mathf.Deg2Rad;
            // Basic trig: opposite side = hypotenuse * sin(theta)
            // We add 30f for icons (sorting) and some bottom padding.
            float neededVertical = (maxTextWidth * Mathf.Sin(angleRad)) + 30f;

            Text.Font = originalFont;

            int required = Mathf.CeilToInt(neededVertical);
            if (__result < required)
            {
                __result = required;
            }
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader))]
    public static class Patch_PawnColumnWorker_DoHeader_DisableHighlight
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);
            var drawHighlightMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawHighlightIfMouseover));
            var workPriorityWorkerType = typeof(PawnColumnWorker_WorkPriority);

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(drawHighlightMethod))
                {
                    var jumpPastHighlight = il.DefineLabel();
                    if (i + 1 < codes.Count)
                    {
                        codes[i + 1].labels.Add(jumpPastHighlight);
                    }

                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Isinst, workPriorityWorkerType);
                    yield return new CodeInstruction(OpCodes.Brtrue, jumpPastHighlight);
                }

                yield return codes[i];
            }
        }
    }
}
