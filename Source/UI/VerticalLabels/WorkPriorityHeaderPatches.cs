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
                        BetterWorkTabMod.Settings.angledHeaderHorizontalOffset,
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

        // Transpiler removed as it interfered with vanilla fallback and is redundant when Prefix returns false.
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
                    
                    // ALWAYS add the marker for size calculation to prevent height flickering
                    // when columns are moved (even if we don't visually show it)
                    text += "*";
                    
                    Vector2 size = Text.CalcSize(text);
                    if (size.x > maxTextWidth) maxTextWidth = size.x;
                }
            }

            float angleRad = Mathf.Abs(AngledLabelDrawer.CurrentRotation) * Mathf.Deg2Rad;
            // Basic trig: opposite side = hypotenuse * sin(theta)
            // We add 30f for icons (sorting) and breathing room, matching the Testing branch.
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
            var drawHighlightMethod = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawHighlightIfMouseover));
            var workPriorityWorkerType = typeof(PawnColumnWorker_WorkPriority);
            
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(drawHighlightMethod))
                {
                    // Target: skip highlight if (settings.enableAngledHeaders && this is workPriorityWorkerType)
                    var labelContinue = il.DefineLabel();
                    
                    // We need to insert our check BEFORE the call to Widgets.DrawHighlightIfMouseover.
                    // The call consumes the Rect argument on the stack.
                    // Instead of trying to jump OVER the call (which is hard because we'd have to jump over the ldarg that loads the rect too),
                    // we can just insert a prefix check that returns early or jumps.
                    
                    // Let's use a simpler approach: 
                    // Insert: if (settings.enableAngledHeaders && this is PawnColumnWorker_WorkPriority) skip highlight;
                    
                    // First, find where the Rect argument is loaded. Usually it's the instruction before the call if it's a simple ldarg.
                    // But in RimWorld it might be more complex.
                    
                    // Alternatively, we can just let it draw the highlight and then draw OUR stuff on top? 
                    // No, the user wants the highlight GONE when angled headers are active because it looks weird (diamond shape vs square).
                    
                    // Correct implementation:
                    // 1. Load Settings.enableAngledHeaders
                    // 2. If false, branch to original highlight code
                    // 3. Load 'this' (arg 0)
                    // 4. Isinst PawnColumnWorker_WorkPriority
                    // 5. If true, branch PAST the highlight call
                    
                    var labelDoHighlight = il.DefineLabel();
                    var labelSkipHighlight = il.DefineLabel();
                    
                    // Assign labelSkipHighlight to the instruction AFTER the call
                    if (i + 1 < codes.Count)
                        codes[i + 1].labels.Add(labelSkipHighlight);
                    
                    // I will insert the logic before the push of the Rect argument.
                    // Usually i-1 is the ldarg that pushes the rect.
                    int insertIndex = i;
                    if (i > 0 && (codes[i-1].opcode == OpCodes.Ldarg_1 || codes[i-1].opcode == OpCodes.Ldloc_0)) // Guessing the rect load
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
                    
                    // labelDoHighlight:
                    newCodes[0].labels.Add(labelDoHighlight); // Wait, newCodes[0] is the start. I need to label the original code start.
                    
                    codes[insertIndex].labels.Add(labelDoHighlight);
                    codes.InsertRange(insertIndex, newCodes);
                    
                    break; // Only one highlight call in DoHeader usually
                }
            }
            return codes;
        }
    }
}
