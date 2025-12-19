using Better_Work_Tab.Features;
using Better_Work_Tab;
using Spine.DragDropApi.Util;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
        private static Vector2 _lastMousePosChecked = new Vector2(float.NaN, float.NaN);
        private static WorkTypeDef _lastHoverWorkType = null;
        private static int _lastHoverResultFrame = -1;
        private static int _lastColumnsCount = -1;

        public static WorkTypeDef HoveredWorkType
        {
            get
            {
                if (_lastCachedFrame != Time.frameCount)
                {
                    return null;
                }

                return _cachedHoveredWorkType;
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            var evt = Event.current;
            var evtType = evt?.type ?? EventType.Layout;
            bool shouldDraw = evtType == EventType.Repaint;
            bool handleInput = evtType == EventType.MouseDown || evtType == EventType.MouseMove || evtType == EventType.MouseDrag || evtType == EventType.MouseUp;
            if (!shouldDraw && !handleInput)
            {
                return false;
            }

            var workType = __instance?.def?.workType;
            if (workType == null) return false;

            int columnsCount = PawnTableDefOf.Work?.columns?.Count ?? -1;

            // 1. Cache mouse data once per frame to avoid redundant Event.current calls
            int currentFrame = Time.frameCount;
            if (_lastCachedFrame != currentFrame || handleInput)
            {
                _cachedMousePos = evt?.mousePosition ?? Vector2.zero;
                _lastCachedFrame = currentFrame;

                // Reset hover cache for this frame
                _cachedHoveredWorkType = null;
            }

            bool mouseUnchanged = _cachedMousePos == _lastMousePosChecked;
            bool reuseHover = mouseUnchanged && _lastHoverResultFrame == (currentFrame - 1) && _lastColumnsCount == columnsCount;

            // 2. Update the global hover tracking if this specific rect is hovered
            // This replaces the old ColumnHoverManager logic
            if (!AngledHeaderCache.TryGetLayout(rect, workType, AngledLabelDrawer.RotCos, AngledLabelDrawer.RotSin, AngledLabelDrawer.STEM_BOTTOM_GAP, out var cached))
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

            // Store hover computation state for potential reuse next frame
            _lastHoverWorkType = isMouseOver ? workType : null;
            _lastHoverResultFrame = currentFrame;
            _lastMousePosChecked = _cachedMousePos;
            _lastColumnsCount = columnsCount;

            AngledLabelDrawer.HandleInteractions(__instance, table, cached.Layout, cached.Bounds, cached.Quad, isMouseOver, shouldDraw, rect);
            return false; // Skip vanilla header drawing entirely
        }

        // === THIS HIDES THE VANILLA HEADERS ===
        // The transpiler removes vanilla's header drawing code (DoRegion and DrawLineVertical calls)
        // so our custom AngledLabelDrawer can take over completely.
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

            return code;
        }
    }

    /// <summary>
    /// Handles the custom angled-text header drawing for work columns.
    /// Displays the column name rotated -60 degrees and optionally adds a yellow
    /// asterisk (*) for columns that were directly dragged by the player.
    /// </summary>
    public static class AngledLabelDrawer
    {
        public const float ROTATION_ANGLE = -60f;
        internal static readonly float RotCos = Mathf.Cos(ROTATION_ANGLE * Mathf.Deg2Rad);
        internal static readonly float RotSin = Mathf.Sin(ROTATION_ANGLE * Mathf.Deg2Rad);
        internal const float STEM_BOTTOM_GAP = 0f;
        private const float UNDERLINE_THICKNESS = 1f;
        private const float TEXT_UNDERLINE_GAP = 1f;
        private const bool DRAW_UNDERLINE = true;
        private static readonly ClickOrDragGate<PawnColumnDef> ClickTracker = new ClickOrDragGate<PawnColumnDef>();
        private static float DragThreshold
        {
            get
            {
                int v = BetterWorkTabMod.Settings?.dragThreshold ?? DefaultSettings.dragThreshold;
                return Mathf.Max(1f, v);
            }
        }

        public readonly struct AngledLabelLayout
        {
            public readonly string Text;
            public readonly Vector2 Size;
            public readonly Vector2 Pivot;
            public readonly bool ShowMarker;

            public AngledLabelLayout(string text, Vector2 size, Vector2 pivot, bool showMarker)
            {
                Text = text;
                Size = size;
                Pivot = pivot;
                ShowMarker = showMarker;
            }
        }

        /// <summary>
        /// Draws the angled header for a work column using a prepared layout.
        /// </summary>
        public static void Draw(AngledLabelLayout layout, bool isMouseOver, bool isSorted = false, bool sortDescending = false, Rect headerRect = default)
        {
            var savedMatrix = GUI.matrix;
            var savedFont = Text.Font;
            var savedAnchor = Text.Anchor;
            var savedColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                float textWidth = layout.Size.x;
                float lineHeight = layout.Size.y;
                Vector2 pivot = layout.Pivot;

                // Rotate around the pivot point (bottom-center)
                GUIUtility.RotateAroundPivot(ROTATION_ANGLE, pivot);

                // Draw mouse-over highlight if hovering
                if (isMouseOver)
                {
                    Rect highlightRect = new Rect(pivot.x, pivot.y - lineHeight, textWidth, lineHeight).ExpandedBy(2f);
                    GUI.color = new Color(1f, 1f, 1f, 0.35f);
                    GUI.DrawTexture(highlightRect, TexUI.HighlightTex);
                    GUI.color = Color.white;
                }

                // Draw underline beneath the text for visual clarity
                if (DRAW_UNDERLINE)
                {
                    Vector2 lineStart = new Vector2(pivot.x, pivot.y - TEXT_UNDERLINE_GAP);
                    Vector2 lineEnd = new Vector2(pivot.x + textWidth, pivot.y - TEXT_UNDERLINE_GAP);
                    Widgets.DrawLine(lineStart, lineEnd, Color.white, UNDERLINE_THICKNESS);
                }

                // Draw the text itself
                Text.Anchor = TextAnchor.LowerLeft;
                GUI.color = layout.ShowMarker ? new Color(1f, 0.85f, 0.2f, 1f) : Color.white;
                var labelRect = new Rect(pivot.x, pivot.y - lineHeight, 200f, lineHeight);
                Widgets.Label(labelRect, layout.Text);
            }
            finally
            {
                GUI.matrix = savedMatrix;
                Text.Font = savedFont;
                Text.Anchor = savedAnchor;
                GUI.color = savedColor;
            }

            // Draw sort indicator at top-right of header box (fixed position, independent of text size)
            if (isSorted && headerRect != default(Rect))
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.9f); // Grey
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;

                string sortArrow = sortDescending ? "▼" : "▲";

                // Position arrow at top-right corner of the actual header box
                float arrowSize = 14f;
                Rect arrowRect = new Rect(
                    headerRect.xMax - arrowSize - 2f,
                    headerRect.y + 40f,
                    arrowSize,
                    arrowSize);

                Widgets.Label(arrowRect, sortArrow);

                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        public static void HandleInteractions(PawnColumnWorker_WorkPriority worker, PawnTable table, AngledLabelLayout layout, Rect bounds, Vector2[] quad, bool isMouseOver, bool shouldDraw, Rect headerRect)
        {
            // Check if this column is currently sorted
            bool isSorted = table?.SortingBy == worker?.def;
            bool sortDescending = table?.SortingDescending ?? false;

            // Draw visual only on repaint
            if (shouldDraw)
            {
                Draw(layout, isMouseOver, isSorted, sortDescending, headerRect);
            }

            // Handle tooltip on hover
            if (isMouseOver)
            {
                TooltipHandler.TipRegion(bounds, AngledHeaderCache.GetTooltip(worker));
            }

            var evt = Event.current;
            if (evt == null)
            {
                return;
            }

            var columnDef = worker?.def;
            bool headerRectHit = headerRect != default(Rect) && headerRect.Contains(evt.mousePosition);
            bool clickHit = isMouseOver || headerRectHit;

            switch (evt.type)
            {
                case EventType.MouseDown when (evt.button == 0 || evt.button == 1) && clickHit:
                    if (evt.shift)
                    {
                        HandleShiftClick(worker, table, evt.button);
                    }
                    else
                    {
                        if (columnDef != null)
                        {
                            ClickTracker.Begin(columnDef, evt.button, evt.mousePosition);
                        }
                    }
                    evt.Use();
                    break;

                case EventType.MouseDrag:
                    ClickTracker.RegisterDrag(columnDef, evt.mousePosition, DragThreshold);
                    break;

                case EventType.MouseUp:
                    if (ClickTracker.TryComplete(columnDef, evt.button, clickHit))
                    {
                        InvokeBaseHeaderClicked(worker, bounds, table);
                        evt.Use();
                    }
                    break;
            }
        }

        private static void HandleShiftClick(PawnColumnWorker_WorkPriority worker, PawnTable table, int mouseButton)
        {
            if (table == null)
            {
                return;
            }

            var pawns = table.PawnsListForReading;
            var workType = worker?.def?.workType;
            if (workType == null || pawns == null)
            {
                return;
            }

            bool useWorkPriorities = Find.PlaySettings.useWorkPriorities;

            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn?.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType))
                {
                    continue;
                }

                if (useWorkPriorities)
                {
                    int priority = pawn.workSettings.GetPriority(workType);
                    if (mouseButton == 0)
                    {
                        int next = priority - 1;
                        if (next < 0) next = 4;
                        pawn.workSettings.SetPriority(workType, next);
                    }
                    else if (mouseButton == 1)
                    {
                        int next = priority + 1;
                        if (next > 4) next = 0;
                        pawn.workSettings.SetPriority(workType, next);
                    }
                }
                else
                {
                    int current = pawn.workSettings.GetPriority(workType);
                    if (current > 0)
                    {
                        if (mouseButton == 1)
                        {
                            pawn.workSettings.SetPriority(workType, 0);
                        }
                    }
                    else if (mouseButton == 0)
                    {
                        pawn.workSettings.SetPriority(workType, 3);
                    }
                }
            }
        }

        private static void InvokeBaseHeaderClicked(PawnColumnWorker_WorkPriority worker, Rect bounds, PawnTable table)
        {
            if (worker == null)
            {
                return;
            }

            _baseHeaderClicked ??= AccessTools.Method(typeof(PawnColumnWorker), "HeaderClicked", new[] { typeof(Rect), typeof(PawnTable) });
            _baseHeaderClicked?.Invoke(worker, new object[] { bounds, table });
        }

        private static MethodInfo _baseHeaderClicked;

        internal static void NotifyColumnDragStarted(PawnColumnDef column)
        {
            ClickTracker.MarkDragStarted(column);
        }

        internal static void ClearPendingHeaderClick(PawnColumnDef column)
        {
            ClickTracker.ClearIfTracking(column);
        }

    }

    // === HEADER HEIGHT PATCH ===
    // Increases header height to accommodate the angled text without clipping.
    [HarmonyPatch(typeof(PawnTable), "HeaderHeight", MethodType.Getter)]
    public static class Patch_PawnTable_HeaderHeight_Getter
    {
        private static float cachedAngleHeaderHeight = -1f;

        public static void Postfix(ref float __result)
        {
            // Only apply custom header height to the Work tab
            if (Find.MainTabsRoot?.OpenTab?.defName != "Work")
                return;

            // Cache the calculation to avoid recalculating every frame
            if (Event.current?.type == EventType.Layout && cachedAngleHeaderHeight > 0f)
            {
                __result = Mathf.Max(__result, cachedAngleHeaderHeight);
                return;
            }

            // Calculate the space needed for rotated text.
            // We use a representative string to get a typical text height.
            Text.Font = GameFont.Small;
            const string testLabel = "Priority hauling";
            Vector2 size = Text.CalcSize(testLabel);

            // Calculate how much vertical space the rotated text needs
            float angleRad = Mathf.Abs(AngledLabelDrawer.ROTATION_ANGLE) * Mathf.Deg2Rad;
            float needed = Mathf.Abs(size.x * Mathf.Sin(angleRad)) + Mathf.Abs(size.y * Mathf.Cos(angleRad));
            needed += 20f; // Add padding

            cachedAngleHeaderHeight = Mathf.Ceil(needed);
            __result = Mathf.Max(__result, cachedAngleHeaderHeight * 1.7f);
        }
    }

    // === DISABLE VANILLA HIGHLIGHT PATCH ===
    // Prevents vanilla from drawing its default header highlight on work columns,
    // letting our custom AngledLabelDrawer handle all visual feedback.
    [HarmonyPatch(typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader))]
    public static class Patch_PawnColumnWorker_DoHeader_DisableHighlight
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = new List<CodeInstruction>(instructions);
            var drawHighlightMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawHighlightIfMouseover));
            var workPriorityWorkerType = typeof(PawnColumnWorker_WorkPriority);

            // Skip vanilla highlight drawing for work priority columns
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
