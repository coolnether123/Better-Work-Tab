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

                    // === We're taking over: ensure layout is solved ===
                    // This is the CRITICAL call that happens once per frame
                    if (evtType == EventType.Layout)
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

                var workType = __instance?.def?.workType;
                if (workType == null)
                {
                    return false;
                }

                int columnsCount = PawnTableDefOf.Work?.columns?.Count ?? -1;
                int currentFrame = Time.frameCount;

                // Cache mouse position
                if (_lastCachedFrame != currentFrame || handleInput)
                {
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
                bool isMouseOver = DetermineMouseOver(enableAngled, rect, cached, columnsCount, currentFrame);

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

                // Handle interactions and rendering
                AngledHeaderInteraction.HandleInteractions(
                    __instance,
                    table,
                    cached.Layout,
                    cached.Bounds,
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
            AngledHeaderCache.CachedHeaderData cached, int columnsCount, int currentFrame)
        {
            bool reuseHover = _cachedMousePos == _lastMousePosChecked
                              && _lastHoverResultFrame == currentFrame - 1
                              && _lastColumnsCount == columnsCount;

            if (reuseHover)
            {
                return _lastHoverWorkType != null;
            }

            // Y bounds check
            if (_cachedMousePos.y < rect.yMin || _cachedMousePos.y > rect.yMax)
            {
                return false;
            }

            // Different checks for angled vs vanilla
            if (enableAngled)
            {
                return AngledHeaderCache.IsMouseOver(cached.Quad, _cachedMousePos);
            }
            else
            {
                // Vanilla: simple rect check (no rotation math needed)
                return rect.Contains(_cachedMousePos);
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

                    // We can have up to 6 levels (0, 1, 2, 3, 4, 5)
                    // Reserve space for all of them
                    int extraHeight = Mathf.CeilToInt(rowHeight * 5f); // 5 extra rows beyond baseline
                    int minRequired = extraHeight + 20; // +20 for base header space

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
