using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.VerticalLabels
{
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class PawnColumnWorker_WorkPriority_DoHeader_Patch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var labelMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new System.Type[] { typeof(Rect), typeof(string) });
            var drawVerticalLabelMethod = AccessTools.Method(typeof(PawnColumnWorker_WorkPriority_DoHeader_Patch), nameof(DrawVerticalLabel));

            foreach (var instruction in instructions)
            {
                if (instruction.Calls(labelMethod))
                {
                    yield return new CodeInstruction(OpCodes.Call, drawVerticalLabelMethod);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        public static void DrawVerticalLabel(Rect rect, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var oldMatrix = GUI.matrix;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;

            const float angleDeg = -60f;
            var pivot = new Vector2(rect.x + rect.width * 0.5f, rect.yMax - 4f);

            GUIUtility.RotateAroundPivot(angleDeg, pivot);

            // Use fixed dimensions to ensure single-line rendering
            var h = Text.LineHeight;
            var w = 200f; // Large fixed width prevents wrapping

            var draw = new Rect(pivot.x - w * 0.5f, pivot.y - h * 0.5f, w, h);
            Widgets.Label(draw, text);

            GUI.matrix = oldMatrix;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;
        }

        [HarmonyPatch(typeof(PawnTable), "HeaderHeight", MethodType.Getter)]
        public static class Patch_PawnTable_HeaderHeight_Getter
        {
            static float AngledHeaderHeightCache = -1f;

            static float GetNeededHeaderHeight(float angleDeg)
            {
                if (Event.current?.type == EventType.Layout && AngledHeaderHeightCache > 0f)
                    return AngledHeaderHeightCache;

                // Estimate by rotating a typical long label
                const string probe = "Priority cleaning"; // longer than most vanilla labels
                var sz = Text.CalcSize(probe);
                float rad = Mathf.Abs(angleDeg) * Mathf.Deg2Rad;

                // Bounding-box height of a rotated rectangle
                float needed = Mathf.Abs(sz.x * Mathf.Sin(rad)) + Mathf.Abs(sz.y * Mathf.Cos(rad));
                needed += 12f; // padding
                AngledHeaderHeightCache = Mathf.Ceil(needed);
                return AngledHeaderHeightCache;
            }

            public static void Postfix(ref float __result)
            {
                // Only bump height while the Work tab is drawing
                if (Find.MainTabsRoot?.OpenTab?.defName == "Work")
                {
                    Text.Font = GameFont.Small;
                    float angled = GetNeededHeaderHeight(-60f);
                    if (angled > __result) __result = angled;
                }
            }
        }
    }
}