using Better_Work_Tab.Features;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit; // Required for Transpiler
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class PawnColumnWorker_WorkPriority_DoHeader_Patch
    {
        // === PERFORMANCE CACHE ===
        private static int _lastCachedFrame = -1;
        private static Vector2 _cachedMousePos = Vector2.zero;
        private static WorkTypeDef _cachedHoveredWorkType = null;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            var workType = __instance?.def?.workType;
            if (workType == null) return;

            // 1. Cache mouse data once per frame
            int currentFrame = Time.frameCount;
            if (_lastCachedFrame != currentFrame)
            {
                _cachedMousePos = Event.current?.mousePosition ?? Vector2.zero;
                _lastCachedFrame = currentFrame;

                // Reset hover cache for this frame
                _cachedHoveredWorkType = null;

                // We can't easily know WHICH rect is hovered here globally without a manager, 
                // but we can check locally very cheaply now.
            }

            // 2. Update the global hover tracking if this specific rect is hovered
            // (This replaces the old ColumnHoverManager logic)
            if (rect.Contains(_cachedMousePos))
            {
                _cachedHoveredWorkType = workType;
            }

            bool isMouseOver = (_cachedHoveredWorkType == workType);
            AngledLabelDrawer.Draw(rect, workType, isMouseOver);
        }

        // === RESTORED TRANSPILER: THIS HIDES THE VANILLA HEADERS ===
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = new List<CodeInstruction>(instructions);
            var doRegionMethod = AccessTools.Method(typeof(Verse.Sound.MouseoverSounds), nameof(Verse.Sound.MouseoverSounds.DoRegion), new[] { typeof(Rect) });
            var drawLineVerticalMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawLineVertical));

            int startIndex = -1;
            int endIndex = -1;

            // Find where vanilla starts drawing the region
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(doRegionMethod))
                {
                    startIndex = i + 1;
                    break;
                }
            }

            // Find where vanilla stops drawing (vertical lines)
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

            // Remove the vanilla drawing instructions
            if (startIndex != -1 && endIndex != -1 && endIndex >= startIndex)
            {
                int count = (endIndex - startIndex) + 1;
                code.RemoveRange(startIndex, count);
            }

            return code.AsEnumerable();
        }
    }

    public static class AngledLabelDrawer
    {
        public const float ROTATION_ANGLE = -60f;
        private const float STEM_BOTTOM_GAP = 0f;
        private const float UNDERLINE_THICKNESS = 1f;
        private const float TEXT_UNDERLINE_GAP = 1f;
        private const bool DRAW_UNDERLINE = true;

        private static Dictionary<string, (Vector2 size, int lastUsedFrame)> _textSizeCache =
    new Dictionary<string, (Vector2, int)>(64);

        private const int TextCacheMaxSize = 100;
        private const int TextCacheInvalidateFrames = 300;  // ~5 seconds at 60fps

        public static void Draw(Rect headerRect, WorkTypeDef workType, bool isMouseOver)
        {
            if (workType == null) return;

            string text = workType.labelShort.CapitalizeFirst();
            bool isReordered = IsColumnReordered(workType);
            string displayText = isReordered ? text + "*" : text;

            int currentFrame = Time.frameCount;

            // Try to get cached size
            if (_textSizeCache.TryGetValue(displayText, out var cached))
            {
                // ✅ Update last-used frame and use cached size
                _textSizeCache[displayText] = (cached.size, currentFrame);
                DrawWithSize(headerRect, displayText, cached.size, isReordered, isMouseOver);
                return;
            }

            // Calculate size
            var oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 textSize = Text.CalcSize(displayText);
            Text.Font = oldFont;

            // ✅ Incremental eviction: remove oldest entry if at capacity
            if (_textSizeCache.Count >= TextCacheMaxSize)
            {
                var oldest = _textSizeCache
                    .OrderBy(kvp => kvp.Value.lastUsedFrame)
                    .First();
                _textSizeCache.Remove(oldest.Key);
            }

            // Cache with timestamp
            _textSizeCache[displayText] = (textSize, currentFrame);

            DrawWithSize(headerRect, displayText, textSize, isReordered, isMouseOver);
        }

        private static bool IsColumnReordered(WorkTypeDef workType)
        {
            return MainTabWindow_BetterWork.IsColumnOutOfVanillaPosition(workType);
        }


        private static void DrawWithSize(Rect headerRect, string displayText, Vector2 textSize, bool isReordered, bool isMouseOver)
        {
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

                float textWidth = textSize.x;
                float lineHeight = textSize.y;

                GUIUtility.RotateAroundPivot(ROTATION_ANGLE, pivot);

                if (isMouseOver)
                {
                    Rect highlightRect = new Rect(pivot.x, pivot.y - lineHeight, textWidth, lineHeight).ExpandedBy(2f);
                    GUI.color = new Color(1f, 1f, 1f, 0.2f);
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
                GUI.color = isReordered ? new Color(1f, 0.85f, 0.2f, 1f) : Color.white;
                var labelRect = new Rect(pivot.x, pivot.y - lineHeight, 200f, lineHeight);
                Widgets.Label(labelRect, displayText);
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

    // === HEADER HEIGHT PATCH ===
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
            // Use a fixed representative string for calc to avoid constant recalculation
            const string testLabel = "Priority hauling";
            Vector2 size = Text.CalcSize(testLabel);

            float angleRad = Mathf.Abs(AngledLabelDrawer.ROTATION_ANGLE) * Mathf.Deg2Rad;
            float needed = Mathf.Abs(size.x * Mathf.Sin(angleRad)) + Mathf.Abs(size.y * Mathf.Cos(angleRad));
            needed += 20f;

            cachedAngleHeaderHeight = Mathf.Ceil(needed);
            __result = Mathf.Max(__result, cachedAngleHeaderHeight * 1.7f);
        }
    }

    // === DISABLE VANILLA HIGHLIGHT PATCH ===
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
