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
        // Frame-based caching for hover detection
        private static int _lastCachedFrame = -1;
        private static Vector2 _cachedMousePos;
        private static WorkTypeDef _cachedHoveredWorkType;
        private static Vector2 _lastMousePosChecked = new Vector2(float.NaN, float.NaN);
        private static WorkTypeDef _lastHoverWorkType;
        private static int _lastHoverResultFrame = -1;
        private static int _lastColumnsCount = -1;
        private static WorkTypeDef _lastFrameHoveredWorkType;

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

                bool enableAngled = BetterWorkTabMod.Settings.enableAngledHeaders;

                // Get work type once at the top
                var workType = __instance?.def?.workType;
                if (workType == null)
                {
                    return false;
                }

                // ===== VANILLA MODE TAKEOVER LOGIC =====
                // If angled headers are OFF, check if ANY columns are moved
                // If so, we take over ALL headers to ensure consistent positioning
                if (!enableAngled)
                {
                    bool hasAnyMovedColumns = CheckIfAnyColumnsAreMoved(table);

                    // If no columns moved, use vanilla rendering
                    if (!hasAnyMovedColumns)
                    {
                        return true; // Fall through to vanilla
                    }

                    // === Collect header data during Layout event ===
                    if (evtType == EventType.Layout)
                    {
                        bool isMoved = MainTabWindow_BetterWork.ShouldShowColumnMarker(workType);
                        var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                        solver.CollectHeader(__instance.def, rect, workType, isMoved);
                    }

                    // === We're taking over: ensure layout is solved ===
                    // This happens during Repaint after all headers have been collected
                    if (evtType == EventType.Repaint)
                    {
                        HeaderDrawingCoordinator.EnsureLayoutSolved(table);
                    }
                }

                // ===== INPUT/RENDER HANDLING =====
                bool shouldDraw = evtType == EventType.Repaint;
                bool handleInput = evtType == EventType.MouseDown
                                   || evtType == EventType.MouseMove
                                   || evtType == EventType.MouseDrag
                                   || evtType == EventType.MouseUp;

                if (!shouldDraw && !handleInput)
                {
                    return false;
                }

                int columnsCount = PawnTableDefOf.Work?.columns?.Count ?? -1;
                int currentFrame = Time.frameCount;

                // Cache mouse position
                if (_lastCachedFrame != currentFrame || handleInput)
                {
                    // capture last frame's hovered type before clearing
                    if (_lastCachedFrame != currentFrame)
                        _lastFrameHoveredWorkType = _cachedHoveredWorkType;

                    _cachedMousePos = evt?.mousePosition ?? Vector2.zero;
                    _lastCachedFrame = currentFrame;
                    _cachedHoveredWorkType = null;
                }

                // Get rotation for angled or vanilla
                float rot = enableAngled ? AngledLabelDrawer.CurrentRotation : 0f;
                float rotCos = Mathf.Cos(rot * Mathf.Deg2Rad);
                float rotSin = Mathf.Sin(rot * Mathf.Deg2Rad);

                // Get layout from cache
                if (!AngledHeaderCache.TryGetLayout(
                        rect,
                        workType,
                        rotCos,
                        rotSin,
                        AngledLabelDrawer.STEM_BOTTOM_GAP,
                        BetterWorkTabMod.Settings.angledHeaderHorizontalOffset,
                        out var cached))
                {
                    return false;
                }

                // Determine if mouse is over this header
                bool isMouseOver = DetermineMouseOver(enableAngled, rect, workType, cached, columnsCount, currentFrame, __instance.def);

                if (isMouseOver)
                {
                    _cachedHoveredWorkType = workType;
                }

                _lastHoverWorkType = isMouseOver ? workType : null;
                _lastHoverResultFrame = currentFrame;
                _lastMousePosChecked = _cachedMousePos;
                _lastColumnsCount = columnsCount;

                // Get the active renderer
                var renderer = HeaderDrawingCoordinator.GetActiveRenderer();

                // Get correct bounds and layout for interactions
                Rect interactionBounds = cached.Bounds;
                AngledLabelDrawer.AngledLabelLayout interactionLayout = cached.Layout;

                if (!enableAngled)
                {
                    var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                    interactionBounds = solver.GetBounds(__instance.def);
                    // For vanilla mode, text layout is simple
                    string baseText = workType.labelShort;
                    if (baseText.NullOrEmpty())
                        baseText = workType.label;
                    if (baseText.NullOrEmpty())
                        baseText = workType.defName;

                    string label = (baseText.NullOrEmpty() ? "Work" : baseText).CapitalizeFirst();
                    interactionLayout = new AngledLabelDrawer.AngledLabelLayout(
                        label,
                        interactionBounds.size,
                        interactionBounds.center,
                        MainTabWindow_BetterWork.ShouldShowColumnMarker(workType)
                    );
                }

                // Handle interactions and rendering
                AngledHeaderInteraction.HandleInteractions(
                    __instance,
                    table,
                    interactionLayout,
                    interactionBounds,
                    cached.Quad,
                    isMouseOver,
                    shouldDraw,
                    rect,
                    renderer);

                return false; // Skip vanilla
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BWT] WorkPriority header failed: {ex}");
                return true; // Fallback to vanilla on error
            }
        }

        /// <summary>
        /// Checks if ANY work priority column has been moved from vanilla position.
        /// </summary>
        private static bool CheckIfAnyColumnsAreMoved(PawnTable table)
        {
            var tableCols = table?.def?.columns;
            if (tableCols == null) return false;

            foreach (var col in tableCols)
            {
                if (col?.workType != null && MainTabWindow_BetterWork.ShouldShowColumnMarker(col.workType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines if the mouse is currently over the header for this column.
        /// Uses a reuse cache to avoid recalculating every frame.
        /// </summary>
        private static bool DetermineMouseOver(bool enableAngled, Rect rect, 
            WorkTypeDef workType, AngledHeaderCache.CachedHeaderData cached, int columnsCount, int currentFrame, PawnColumnDef columnDef)
        {
            bool reuseHover = _cachedMousePos == _lastMousePosChecked
                              && _lastHoverResultFrame == currentFrame - 1
                              && _lastColumnsCount == columnsCount;

            if (reuseHover)
            {
                // Reuse the *specific* header that was hovered last frame.
                // If none was hovered, everything returns false.
                return _lastFrameHoveredWorkType != null && workType == _lastFrameHoveredWorkType;
            }

            // For angled headers, we check Y bounds first
            if (enableAngled)
            {
                if (_cachedMousePos.y < rect.yMin || _cachedMousePos.y > rect.yMax)
                {
                    return false;
                }
                return AngledHeaderCache.IsMouseOver(cached.Quad, _cachedMousePos);
            }
            else
            {
                // Vanilla mode: use the actual staggered bounds from the solver!
                var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                Rect staggeredBounds = solver.GetBounds(columnDef);
                
                // Allow hover if within the staggered label bounds
                if (staggeredBounds.Contains(_cachedMousePos))
                {
                    return true;
                }

                // Also allow hover if within the horizontal center strip of the column (for the stem line)
                // but only above the pawn row
                float centerX = staggeredBounds.center.x;
                Rect stemChannel = new Rect(centerX - 5f, staggeredBounds.yMax, 10f, rect.yMax - staggeredBounds.yMax);
                
                return stemChannel.Contains(_cachedMousePos);
            }
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            // If we took over rendering (prefix returned false), we handled everything
            // If vanilla handled it (prefix returned true), nothing to do here
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

            bool enableAngled = BetterWorkTabMod.Settings.enableAngledHeaders;

            if (!enableAngled)
            {
                // For vanilla mode, calculate height based on number of levels
                bool hasMovedColumns = CheckIfAnyColumnsAreMoved(table);

                if (hasMovedColumns)
                {
                    GameFont oldFont = Text.Font;
                    Text.Font = GameFont.Small;
                    float rowHeight = Text.LineHeight + 2f;

                    var solver = HeaderDrawingCoordinator.GetVanillaSolver();
                    int maxLevel = solver?.GetMaxLevelUsed() ?? 1;

                    // Vanilla is levels 0 and 1.
                    // If maxLevel > 1, we add extra space for those levels.
                    // Level 0: 0, Level 1: 1, Level 2: 2, Level 3: 3
                    // The result should scale with maxLevel.
                    // A multiplier of 1.2 per level beyond the base (which is effectively 2 levels)
                    int minRequired = Mathf.CeilToInt(rowHeight * (maxLevel + 1.5f)); 

                    if (__result < minRequired)
                    {
                        __result = minRequired;
                    }

                    Text.Font = oldFont;
                }

                return;
            }

            // ===== ANGLED HEADERS MODE =====
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

                    // Always include marker for consistent height
                    text += "*";

                    Vector2 size = Text.CalcSize(text);
                    if (size.x > maxTextWidth) maxTextWidth = size.x;
                }
            }

            float angleRad = Mathf.Abs(AngledLabelDrawer.CurrentRotation) * Mathf.Deg2Rad;
            float neededVertical = (maxTextWidth * Mathf.Sin(angleRad)) + 30f;

            Text.Font = originalFont;

            int angledRequired = Mathf.CeilToInt(neededVertical);
            if (__result < angledRequired)
            {
                __result = angledRequired;
            }
        }

        private static bool CheckIfAnyColumnsAreMoved(PawnTable table)
        {
            var tableCols = table?.def?.columns;
            if (tableCols == null) return false;

            foreach (var col in tableCols)
            {
                if (col?.workType != null && MainTabWindow_BetterWork.ShouldShowColumnMarker(col.workType))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker), nameof(PawnColumnWorker.DoHeader))]
    public static class Patch_PawnColumnWorker_DoHeader_DisableHighlight
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var drawHighlightMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawHighlightIfMouseover));
            var workPriorityWorkerType = typeof(PawnColumnWorker_WorkPriority);

            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(drawHighlightMethod))
                {
                    var labelDoHighlight = il.DefineLabel();
                    var labelSkipHighlight = il.DefineLabel();

                    if (i + 1 < codes.Count)
                        codes[i + 1].labels.Add(labelSkipHighlight);

                    int insertIndex = i;
                    if (i > 0 && (codes[i - 1].opcode == OpCodes.Ldarg_1 || codes[i - 1].opcode == OpCodes.Ldloc_0))
                    {
                        insertIndex = i - 1;
                    }

                    var newCodes = new List<CodeInstruction>();

                    // if (!Settings.enableAngledHeaders) goto do_highlight;
                    newCodes.Add(new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(BetterWorkTabMod), nameof(BetterWorkTabMod.Settings))));
                    newCodes.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(BetterWorkTabSettings), nameof(BetterWorkTabSettings.enableAngledHeaders))));
                    newCodes.Add(new CodeInstruction(OpCodes.Brfalse, labelDoHighlight));

                    // if (this is PawnColumnWorker_WorkPriority) goto skip_highlight;
                    newCodes.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    newCodes.Add(new CodeInstruction(OpCodes.Isinst, workPriorityWorkerType));
                    newCodes.Add(new CodeInstruction(OpCodes.Brtrue, labelSkipHighlight));

                    codes[insertIndex].labels.Add(labelDoHighlight);
                    codes.InsertRange(insertIndex, newCodes);

                    break;
                }
            }
            return codes;
        }
    }
}
