using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Completely replaces vanilla header rendering for work type columns.
    /// Stops both the label and the faint white vertical lines from drawing.
    /// Strategy:
    /// - Prefix (FIRST) returns false for work columns, canceling vanilla entirely.
    /// - Postfix (LAST) draws our angled header so it renders after any other mod.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class PawnColumnWorker_WorkPriority_DoHeader_Replace
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            // Let vanilla handle non‑work columns (def.workType == null),
            // but cancel for real work columns to prevent both the centered label
            // and the two faint vertical lines from drawing.
            return __instance?.def?.workType == null;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            var workType = __instance?.def?.workType;
            if (workType == null) return;

            // Draw our custom header LAST so nothing can paint over it.
            AngledLabelDrawer.Draw(rect, workType);
        }
    }

    /// <summary>
    /// Self-contained drawer for angled work type column headers.
    /// Pure rendering logic with no Harmony complexity.
    /// </summary>
    public static class AngledLabelDrawer
    {
        // ===== VISUAL TUNING CONSTANTS =====
        // Adjust these to change the appearance:

        public const float ROTATION_ANGLE = -60f;         // Degrees clockwise (negative = CW in Unity)
        private const float STEM_TOP_GAP = 8f;            // Pixels from header top to stem start
        private const float STEM_BOTTOM_GAP = 0f;        // Pixels from header bottom to pivot point
        private const float STEM_THICKNESS = 1.5f;        // Line width in pixels
        private const float UNDERLINE_THICKNESS = 1f;   // Line height in pixels
        private const float TEXT_UNDERLINE_GAP = 1f;      // Vertical space between text and underline

        // Final visual switches for release:
        private const bool DRAW_BACKGROUND_COVER = false;  // Paint over any remnants
        private const bool DRAW_STEM = false;             // Keep off (the “line” users disliked)
        private const bool DRAW_UNDERLINE = true;         // Full word underline

        /// <summary>
        /// Main drawing entry point called from Postfix patch.
        /// Handles all rendering in a single clean method.
        /// </summary>
        public static void Draw(Rect headerRect, WorkTypeDef workType)
        {
            if (workType == null) return;

            string text = workType.labelShort.CapitalizeFirst();
            if (string.IsNullOrEmpty(text)) return;

            // Column center X-coordinate (where stem would be drawn)
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
                // Optional cover to ensure a clean background regardless of other draws
                if (DRAW_BACKGROUND_COVER)
                {
                    Widgets.DrawBoxSolid(headerRect, new Color(0.16f, 0.16f, 0.16f)); // Vanilla dark BG tone
                }

                // Vertical stem (disabled by default for clarity)
                if (DRAW_STEM)
                {
                    Vector2 stemTop = new Vector2(centerX, headerRect.y + STEM_TOP_GAP);
                    Widgets.DrawLine(stemTop, pivot, Color.white, STEM_THICKNESS);
                }

                // Measure text before rotation to get true single-line width
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Vector2 textSize = Text.CalcSize(text);
                float textWidth = textSize.x;
                float lineHeight = Text.LineHeight;

                // Rotate label space around the pivot
                GUIUtility.RotateAroundPivot(ROTATION_ANGLE, pivot);

                // Underline (full width, starts at column center)
                if (DRAW_UNDERLINE)
                {
                    // Start and end points for underline, in rotated coordinates
                    Vector2 lineStart = new Vector2(pivot.x, pivot.y - TEXT_UNDERLINE_GAP);
                    Vector2 lineEnd = new Vector2(pivot.x + textWidth, pivot.y - TEXT_UNDERLINE_GAP);

                    // Option 1 (recommended): RimWorld helper
                    Widgets.DrawLine(lineStart, lineEnd, Color.white, UNDERLINE_THICKNESS);
                }

                // Text baseline sits just above the underline
                Text.Anchor = TextAnchor.LowerLeft;
                var labelRect = new Rect(pivot.x, pivot.y - lineHeight, 200f, lineHeight);
                Widgets.Label(labelRect, text);
            }
            finally
            {
                // Restore GUI state
                GUI.matrix = savedMatrix;
                Text.Font = savedFont;
                Text.Anchor = savedAnchor;
                GUI.color = savedColor;
            }
        }
    }

    /// <summary>
    /// Increases header height to accommodate angled labels.
    /// This patch is necessary because PawnTable calculates height before rendering.
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


}