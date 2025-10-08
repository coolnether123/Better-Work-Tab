using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.VerticalLabels
{
    /// <summary>
    /// Replaces vanilla work type header rendering with angled labels.
    /// 
    /// Changes from vanilla:
    /// - Removes the two semi-transparent vertical lines
    /// - Replaces centered horizontal label with 60° angled text
    /// - Adds vertical stem from column center to text
    /// - Adds horizontal underline beneath the full text width
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class PawnColumnWorker_WorkPriority_DoHeader_Patch
    {
        /// <summary>
        /// Transpiler removes vanilla's label and line drawing, replaces with custom angled label.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var labelMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label),
                new System.Type[] { typeof(Rect), typeof(string) });
            var drawLineMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawLineVertical));
            var drawAngledMethod = AccessTools.Method(typeof(AngledLabelDrawer), nameof(AngledLabelDrawer.Draw));
            var noOpMethod = AccessTools.Method(typeof(PawnColumnWorker_WorkPriority_DoHeader_Patch), nameof(NoOpLine));

            foreach (var instruction in instructions)
            {
                // Replace Widgets.Label with our custom angled drawer
                if (instruction.Calls(labelMethod))
                {
                    instruction.operand = drawAngledMethod;
                    yield return instruction;
                }
                // Replace DrawLineVertical with no-op to consume stack args
                else if (instruction.Calls(drawLineMethod))
                {
                    instruction.operand = noOpMethod;
                    yield return instruction;
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        /// <summary>
        /// No-op method that consumes DrawLineVertical's arguments to keep stack balanced.
        /// </summary>
        private static void NoOpLine(float x, float y, float length) { }
    }

    /// <summary>
    /// Self-contained drawer for angled work type labels.
    /// Draws a vertical stem + angled text + horizontal underline.
    /// </summary>
    public static class AngledLabelDrawer
    {
        // ===== VISUAL TUNING CONSTANTS =====
        // Adjust these to change the appearance:

        public const float ROTATION_ANGLE = -60f;         // Degrees clockwise (negative = CW in Unity)
        private const float STEM_TOP_GAP = 8f;            // Pixels from header top to stem start
        private const float STEM_BOTTOM_GAP = 18f;        // Pixels from header bottom to pivot point
        private const float STEM_THICKNESS = 1.5f;        // Line width in pixels
        private const float UNDERLINE_THICKNESS = 1.5f;   // Line height in pixels
        private const float TEXT_UNDERLINE_GAP = 1f;      // Vertical space between text and underline

        /// <summary>
        /// Main drawing entry point (called from transpiler).
        /// Signature matches Widgets.Label(Rect, string).
        /// </summary>
        public static void Draw(Rect headerRect, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Column center X-coordinate (where stem will be drawn)
            float centerX = headerRect.x + headerRect.width * 0.5f;

            // Pivot point: where stem meets underline (at column center bottom)
            Vector2 pivot = new Vector2(centerX, headerRect.yMax - STEM_BOTTOM_GAP);

            // Save GUI state
            var savedMatrix = GUI.matrix;
            var savedFont = Text.Font;
            var savedAnchor = Text.Anchor;
            var savedColor = GUI.color;

            try
            {
                // ===== PART 1: VERTICAL STEM (screen space, not rotated) =====
                Vector2 stemTop = new Vector2(centerX, headerRect.y + STEM_TOP_GAP);
                Widgets.DrawLine(stemTop, pivot, Color.white, STEM_THICKNESS);

                // ===== PART 2: MEASURE TEXT (before rotation for accurate width) =====
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                // Measure with generous rect to get true single-line width
                Rect measureRect = new Rect(0, 0, 999f, 999f);
                Vector2 textSize = Text.CalcSize(text);
                float actualTextWidth = textSize.x;
                float lineHeight = Text.LineHeight;

                // ===== PART 3: ROTATE COORDINATE SYSTEM =====
                GUIUtility.RotateAroundPivot(ROTATION_ANGLE, pivot);

                // ===== PART 4: DRAW UNDERLINE (in rotated space) =====
                // Underline extends full text width from pivot point
                Rect underlineRect = new Rect(
                    pivot.x,                          // Start at column center
                    pivot.y - TEXT_UNDERLINE_GAP,     // Positioned for visual connection
                    actualTextWidth,                  // Full calculated text width
                    UNDERLINE_THICKNESS
                );
                Widgets.DrawBoxSolid(underlineRect, Color.white);

                // ===== PART 5: DRAW TEXT =====
                // Use wide rect to prevent any wrapping
                Text.Anchor = TextAnchor.LowerLeft;
                Rect labelRect = new Rect(
                    pivot.x,                         // Left edge at column center
                    pivot.y - lineHeight,            // Position above underline
                    200f,                            // Wide fixed width prevents wrapping
                    lineHeight
                );
                Widgets.Label(labelRect, text);
            }
            finally
            {
                // ===== RESTORE GUI STATE =====
                GUI.matrix = savedMatrix;
                Text.Font = savedFont;
                Text.Anchor = savedAnchor;
                GUI.color = savedColor;
            }
        }
    }

    /// <summary>
    /// Increases header height to accommodate angled labels.
    /// Vanilla uses 50px which is too short for 60° rotated text.
    /// </summary>
    [HarmonyPatch(typeof(PawnTable), "HeaderHeight", MethodType.Getter)]
    public static class Patch_PawnTable_HeaderHeight_Getter
    {
        private static float cachedAngleHeaderHeight = -1f;

        public static void Postfix(ref float __result)
        {
            // Only apply to Work tab
            if (Find.MainTabsRoot?.OpenTab?.defName != "Work")
                return;

            // Calculate needed height based on rotation angle and typical text
            if (Event.current?.type == EventType.Layout && cachedAngleHeaderHeight > 0f)
            {
                __result = Mathf.Max(__result, cachedAngleHeaderHeight);
                return;
            }

            Text.Font = GameFont.Small;

            // Test with a representative long label to get maximum height needed
            const string testLabel = "Priority hauling";  // Typical long work type label
            Vector2 size = Text.CalcSize(testLabel);

            // Calculate rotated bounding box height
            // For 60° rotation: height = textWidth * sin(60°) + textHeight * cos(60°)
            float angleRad = Mathf.Abs(AngledLabelDrawer.ROTATION_ANGLE) * Mathf.Deg2Rad;
            float needed = Mathf.Abs(size.x * Mathf.Sin(angleRad)) +
                          Mathf.Abs(size.y * Mathf.Cos(angleRad));
            needed += 20f;  // Padding for stem and spacing

            cachedAngleHeaderHeight = Mathf.Ceil(needed);
            __result = Mathf.Max(__result, cachedAngleHeaderHeight);
        }
    }
}