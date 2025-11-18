// --- START OF FILE UI/VerticalLabels/AngledHeaderDrawer.cs (CORRECTED) ---

using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    // ====================================================================
    // This patch and its Transpiler are responsible for REMOVING the vanilla drawing
    // and ADDING our custom angled drawing.
    // ====================================================================
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class PawnColumnWorker_WorkPriority_DoHeader_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            var workType = __instance?.def?.workType;
            if (workType == null) return;

            // Check if the mouse is over the entire header cell
            bool isMouseOver = Mouse.IsOver(rect);

            // Pass the mouseover state to our drawer
            AngledLabelDrawer.Draw(rect, workType, isMouseOver);
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
                int count = (endIndex - startIndex) + 1;
                code.RemoveRange(startIndex, count);
            }

            return code.AsEnumerable();
        }
    }

    // ====================================================================
    // The self-contained drawer class.
    // ====================================================================
    public static class AngledLabelDrawer
    {
        public const float ROTATION_ANGLE = -60f;
        private const float STEM_BOTTOM_GAP = 0f;
        private const float UNDERLINE_THICKNESS = 1f;
        private const float TEXT_UNDERLINE_GAP = 1f;
        private const bool DRAW_UNDERLINE = true;

        public static void Draw(Rect headerRect, WorkTypeDef workType, bool isMouseOver)
        {
            if (workType == null) return;

            string text = workType.labelShort.CapitalizeFirst();
            if (string.IsNullOrEmpty(text)) return;

            float centerX = headerRect.x + headerRect.width * 0.5f;
            Vector2 pivot = new Vector2(centerX, headerRect.yMax - STEM_BOTTOM_GAP);

            var savedMatrix = GUI.matrix;
            var savedFont = Text.Font;
            var savedAnchor = Text.Anchor;
            var savedColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Vector2 textSize = Text.CalcSize(text);
                float textWidth = textSize.x;
                float lineHeight = Text.LineHeight;

                GUIUtility.RotateAroundPivot(ROTATION_ANGLE, pivot);

                // Draw the custom rotated highlight if the mouse is over the cell
                if (isMouseOver)
                {
                    Rect highlightRect = new Rect(pivot.x, pivot.y - lineHeight, textWidth, lineHeight).ExpandedBy(2f);
                    GUI.color = new Color(1f, 1f, 1f, 0.2f);
                    // FIXED: Use TexUI.HighlightTex instead of GenUI.HighlightTex
                    GUI.DrawTexture(highlightRect, TexUI.HighlightTex);
                    GUI.color = Color.white;
                }

                if (DRAW_UNDERLINE)
                {
                    Vector2 lineStart = new Vector2(pivot.x, pivot.y - TEXT_UNDERLINE_GAP);
                    Vector2 lineEnd = new Vector2(pivot.x + textWidth, pivot.y - TEXT_UNDERLINE_GAP);
                    Widgets.DrawLine(lineStart, lineEnd, Color.white, UNDERLINE_THICKNESS);
                }

                Text.Anchor = TextAnchor.LowerLeft;
                var labelRect = new Rect(pivot.x, pivot.y - lineHeight, 200f, lineHeight);
                Widgets.Label(labelRect, text);
            }
            finally
            {
                GUI.matrix = savedMatrix;
                Text.Font = savedFont;
                Text.Anchor = savedAnchor;
                GUI.color = savedColor;
            }
        }
    }

    // ====================================================================
    // The patch for increasing header height. No changes here.
    // ====================================================================
    [HarmonyPatch(typeof(PawnTable), "HeaderHeight", MethodType.Getter)]
    public static class Patch_PawnTable_HeaderHeight_Getter
    {
        private static float cachedAngleHeaderHeight = -1f;

        public static void Postfix(ref float __result)
        {
            if (Find.MainTabsRoot?.OpenTab?.defName != "Work")
                return;

            if (Event.current?.type == EventType.Layout && cachedAngleHeaderHeight > 0f)
            {
                __result = Mathf.Max(__result, cachedAngleHeaderHeight);
                return;
            }

            Text.Font = GameFont.Small;
            const string testLabel = "Priority hauling";
            Vector2 size = Text.CalcSize(testLabel);

            float angleRad = Mathf.Abs(AngledLabelDrawer.ROTATION_ANGLE) * Mathf.Deg2Rad;
            float needed = Mathf.Abs(size.x * Mathf.Sin(angleRad)) + Mathf.Abs(size.y * Mathf.Cos(angleRad));
            needed += 20f;

            cachedAngleHeaderHeight = Mathf.Ceil(needed);
            __result = Mathf.Max(__result, cachedAngleHeaderHeight * 1.7f);
        }
    }

    // ====================================================================
    // The patch for expanding the interactive area. No changes here.
    // ====================================================================
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), "GetInteractableHeaderRect")]
    public static class Patch_PawnColumnWorker_WorkPriority_GetInteractableHeaderRect
    {
        [HarmonyPostfix]
        public static void Postfix(ref Rect __result, Rect headerRect)
        {
            __result = headerRect;
        }
    }

    // ====================================================================
    // The patch for disabling the vanilla rectangular highlight. This is the new one.
    // ====================================================================
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