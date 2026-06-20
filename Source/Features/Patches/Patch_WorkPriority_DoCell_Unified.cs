using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.Patches
{
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class Patch_WorkPriority_DoHeader_HoverTracker
    {
        private static WorkTypeDef _hoveredHeaderWorkType;
        private static int _hoveredHeaderFrame = -1;

        public static WorkTypeDef HoveredHeaderWorkType
        {
            get
            {
                if (Time.frameCount != _hoveredHeaderFrame)
                    return null;
                return _hoveredHeaderWorkType;
            }
        }

        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            // Only apply BWT patches to the Work tab (vanilla or BWT), not other tabs like MechTab
            if (!UI.Headers.PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab())
                return;

            if (!Event.current.shift)
                return;

            if (!(BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false))
                return;

            if (Time.frameCount != _hoveredHeaderFrame)
            {
                _hoveredHeaderWorkType = null;
                _hoveredHeaderFrame = Time.frameCount;
            }

            bool isHovered = UI.Headers.PawnColumnWorker_WorkPriority_DoHeader_Patch.HoveredWorkType == __instance.def.workType;
            if (!isHovered)
            {
                Rect labelRect = GetLabelRect(__instance, rect);
                isHovered = Mouse.IsOver(labelRect);
            }

            if (isHovered)
            {
                _hoveredHeaderWorkType = __instance.def.workType;
            }
        }

        private static Rect GetLabelRect(PawnColumnWorker_WorkPriority worker, Rect headerRect)
        {
            Vector2 labelSize = Text.CalcSize(worker.def.workType.labelShort.CapitalizeFirst());
            Rect labelRect = new Rect(
                headerRect.center.x - labelSize.x / 2f,
                headerRect.y,
                labelSize.x,
                labelSize.y);

            if (worker.def.moveWorkTypeLabelDown)
                labelRect.y += 20f;

            return labelRect;
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_Unified
    {
        // Frame state
        private static int _lastCachedFrame = -1;
        private static bool _cachedShiftHeld = false;
        private static bool _cachedFeatureEnabled = false;
        private static BetterWorkTabSettings.ShowUIMode _cachedUiState;
        private static bool _cachedHoverCellOverlayEnabled = true;
        private static BetterWorkTabSettings.SkillViewHoverMode _cachedHoverMode = BetterWorkTabSettings.SkillViewHoverMode.Standard;
        private static BetterWorkTabSettings.HoverEffectScope _cachedHoverScope = BetterWorkTabSettings.HoverEffectScope.CellOnly;
        private static WorkTypeDef _columnHoveredWorkType;
        private static int _columnHoveredFrame = -1;

        // Cached per-frame and per-worktype values
        private static readonly Dictionary<int, int> _skillCache = new Dictionary<int, int>(1024);
        private static readonly Dictionary<int, int> _skillCacheTimestamps = new Dictionary<int, int>(1024);
        private static readonly Dictionary<int, byte> _incapableCache = new Dictionary<int, byte>(1024);
        private static readonly Dictionary<int, int> _incapableCacheTimestamps = new Dictionary<int, int>(1024);
        private static readonly Dictionary<int, Pawn> _bestPawnCache = new Dictionary<int, Pawn>(64);
        private static readonly Dictionary<int, int> _bestPawnCacheTimestamps = new Dictionary<int, int>(64);
        private static readonly Dictionary<int, Color> _colorCache = new Dictionary<int, Color>(21);

        // Layout and cache settings
        private const int SkillCacheFrameValidity = 60;
        private const int IncapableCacheFrameValidity = 120;
        private const int BestPawnCacheFrameValidity = 60;
        private const float SkillBoxSize = 25f;
        private const float SkillBoxVerticalPadding = 2.5f;
        private const float SmallSkillOffsetY = -2f;
        private const float SkillBoxOutlinePadding = 2f;

        private static void UpdateFrameCache()
        {
            int currentFrame = Time.frameCount;
            if (_lastCachedFrame == currentFrame)
                return;

            _lastCachedFrame = currentFrame;
            _cachedFeatureEnabled = BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false;
            _cachedUiState = ShiftHelper.State;
            _cachedShiftHeld = _cachedUiState == BetterWorkTabSettings.ShowUIMode.Shifted;
            _cachedHoverCellOverlayEnabled = BetterWorkTabMod.Settings?.showHoverCellOverlay ?? true;
            _cachedHoverMode = BetterWorkTabMod.Settings?.skillViewHoverMode ?? BetterWorkTabSettings.SkillViewHoverMode.Standard;
            _cachedHoverScope = BetterWorkTabMod.Settings?.hoverEffectScope ?? BetterWorkTabSettings.HoverEffectScope.CellOnly;
        }

        public static void ClearColorCache()
        {
            _colorCache.Clear();
        }

        [HarmonyPrefix]
        public static bool Prefix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            // Only apply BWT patches to the Work tab (vanilla or BWT), not other tabs like MechTab
            if (!UI.Headers.PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab())
                return true;

            if (pawn == null || pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return true;

            WorkTypeDef workType = __instance.def.workType;
            if (workType == null)
                return true;

            UpdateFrameCache();

            // Handle Scroll Wheel Priority Adjustment
            if (BetterWorkTabMod.Settings.enableScrollWheelPriority && Event.current.type == EventType.ScrollWheel && Mouse.IsOver(rect))
            {
                int currentPriority = pawn.workSettings.GetPriority(workType);
                int delta = Event.current.delta.y > 0 ? -1 : 1;
                if (Find.PlaySettings.useWorkPriorities)
                {
                    int nextPriority = currentPriority;
                    if (delta > 0)
                    {
                        if (currentPriority == 0) nextPriority = BetterWorkTabMod.Settings.maxPriorityInt;
                        else if (currentPriority > 1) nextPriority = currentPriority - 1;
                    }
                    else
                    {
                        if (currentPriority == BetterWorkTabMod.Settings.maxPriorityInt) nextPriority = 0;
                        else if (currentPriority > 0) nextPriority = currentPriority + 1;
                    }

                    if (nextPriority != currentPriority)
                    {
                        pawn.workSettings.SetPriority(workType, nextPriority);
                        SoundDefOf.DragSlider.PlayOneShotOnCamera();
                    }
                }
                else
                {
                    int nextPriority = (currentPriority > 0) ? 0 : 3;
                    if (nextPriority != currentPriority)
                    {
                        pawn.workSettings.SetPriority(workType, nextPriority);
                        SoundDefOf.DragSlider.PlayOneShotOnCamera();

                    }
                }
                Event.current.Use();
            }

            // If skill overlay feature is disabled or shift is not held, use vanilla rendering
            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
                return true;

            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
                return true;

            if (GetIsIncapable(pawn, workType))
                return true;

            if (pawn.WorkTypeIsDisabled(workType))
                return true;

            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
                return true;

            // Track column hover state only if hover cell overlay is enabled
            bool hoveringCell = Mouse.IsOver(rect);
            if (_cachedHoverCellOverlayEnabled && hoveringCell && _cachedHoverScope == BetterWorkTabSettings.HoverEffectScope.ColumnWide)
            {
                _columnHoveredWorkType = workType;
                _columnHoveredFrame = Time.frameCount;
            }
            
            // Determine column hover status (only valid if hover overlay is enabled)
            bool columnHovered = _cachedHoverCellOverlayEnabled &&
                                 _cachedHoverScope == BetterWorkTabSettings.HoverEffectScope.ColumnWide &&
                                 _columnHoveredWorkType != null &&
                                 _columnHoveredWorkType == workType &&
                                 (_columnHoveredFrame == Time.frameCount || _columnHoveredFrame == Time.frameCount - 1);

            // Decide whether vanilla should draw based on hover mode.
            // Only apply hover behavior changes if hover overlay is enabled.
            if (hoveringCell && _cachedHoverCellOverlayEnabled)
            {
                // Let vanilla draw for interactive priority handling in Standard or SkillFocused.
                if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.Standard ||
                    _cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.SkillFocused)
                {
                    return true;
                }
                return false;
            }

            if (columnHovered)
            {
                if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.Standard)
                {
                    return true;
                }
                return false;
            }

            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            // Only apply BWT patches to the Work tab (vanilla or BWT), not other tabs like MechTab
            if (!UI.Headers.PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab())
                return;

            WorkTypeDef workType = __instance.def.workType;
            if (pawn == null || pawn.Dead || workType == null)
                return;

            if (pawn.WorkTypeIsDisabled(workType))
                return;

            if (GetIsIncapable(pawn, workType))
                return;

            // Draw best pawn outline - this uses the ShowUIMode setting to determine when to show
            // (can be Always, Shifted, Unshifted, or Never)
            DrawBestPawnOutlineIfNeeded(__instance, rect, pawn, table, workType);

            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
                return;

            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
                return;

            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
                return;

            int priority = pawn.workSettings.GetPriority(workType);
            int skillLevel = GetSkillLevel(pawn, workType);
            bool hoveringCell = Mouse.IsOver(rect);
            
            // Only check column hover if hover overlay is enabled
            bool columnHovered = _cachedHoverCellOverlayEnabled &&
                                 _cachedHoverScope == BetterWorkTabSettings.HoverEffectScope.ColumnWide &&
                                 _columnHoveredWorkType != null &&
                                 _columnHoveredWorkType == workType &&
                                 (_columnHoveredFrame == Time.frameCount || _columnHoveredFrame == Time.frameCount - 1);
            bool hovering = hoveringCell || columnHovered;

            float boxXSkill = rect.x + (rect.width - SkillBoxSize) / 2f;
            float boxYSkill = rect.y + SkillBoxVerticalPadding;
            Rect boxRect = new Rect(boxXSkill, boxYSkill, SkillBoxSize, SkillBoxSize);

            bool drawBigSkill = true;
            bool drawSmallSkill = false;
            bool showTinySkillNumbers = (BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false) &&
                                        ShouldShowUI(BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers, _cachedUiState);

            if (_cachedHoverCellOverlayEnabled && hovering)
            {
                if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.Standard)
                {
                    drawBigSkill = false;
                    drawSmallSkill = true;
                }
            }

            if (showTinySkillNumbers)
            {
                drawSmallSkill = true;
            }

            CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(boxXSkill, boxYSkill, pawn, workType, false);

            if (priority > 0)
            {
                CustomWorkBoxDrawer.DrawCompactPriority(rect, priority);
            }

            if (drawBigSkill)
            {
                DrawBigSkillNumber(boxRect, skillLevel);
            }

            if (drawSmallSkill)
            {
                DrawSmallSkillNumbers(rect, skillLevel);
            }
        }

        // Caching helpers

        private static bool GetIsIncapable(Pawn p, WorkTypeDef work)
        {
            int key = (p.thingIDNumber << 16) | work.shortHash;
            int currentFrame = Time.frameCount;
            var settings = BetterWorkTabMod.Settings;
            bool useCache = (settings?.enablePerformanceOptimizations ?? true) &&
                            (settings?.cacheIncapabilityChecks ?? true);

            if (useCache && _incapableCacheTimestamps.TryGetValue(key, out int timestamp))
            {
                if (currentFrame - timestamp < IncapableCacheFrameValidity)
                {
                    return _incapableCache[key] == 1;
                }
            }

            bool canDoAny = false;
            for (int i = 0; i < work.workGiversByPriority.Count; i++)
            {
                bool thisGiverOk = true;
                var reqs = work.workGiversByPriority[i].requiredCapacities;
                for (int j = 0; j < reqs.Count; j++)
                {
                    if (!p.health.capacities.CapableOf(reqs[j]))
                    {
                        thisGiverOk = false;
                        break;
                    }
                }
                if (thisGiverOk)
                {
                    canDoAny = true;
                    break;
                }
            }
            bool isIncapable = !canDoAny;

            if (useCache)
            {
                _incapableCache[key] = (byte)(isIncapable ? 1 : 0);
                _incapableCacheTimestamps[key] = currentFrame;
            }
            return isIncapable;
        }

        private static int GetSkillLevel(Pawn pawn, WorkTypeDef workType)
        {
            int key = (pawn.thingIDNumber << 16) | workType.shortHash;
            int currentFrame = Time.frameCount;
            var settings = BetterWorkTabMod.Settings;
            bool useCache = (settings?.enablePerformanceOptimizations ?? true) &&
                            (settings?.cacheSkillLevels ?? true);

            if (useCache && _skillCacheTimestamps.TryGetValue(key, out int timestamp))
            {
                if (currentFrame - timestamp < SkillCacheFrameValidity)
                {
                    return _skillCache[key];
                }
            }

            float avg = pawn.skills.AverageOfRelevantSkillsFor(workType);
            int level = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);

            if (useCache)
            {
                _skillCache[key] = level;
                _skillCacheTimestamps[key] = currentFrame;
            }
            return level;
        }

        private static Pawn GetBestPawnForWorktype(PawnTable table, WorkTypeDef workType, PawnColumnWorker_WorkPriority worker)
        {
            var pawns = PawnTableCompat.GetCachedPawns(table);
            if (pawns.Count == 0) return null;
            var settings = BetterWorkTabMod.Settings;
            bool useCache = (settings?.enablePerformanceOptimizations ?? true) &&
                            (settings?.cacheSkillLevels ?? true);

            int key = (table.GetHashCode() << 16) | workType.shortHash;
            int currentFrame = Time.frameCount;

            if (useCache && _bestPawnCacheTimestamps.TryGetValue(key, out int timestamp))
            {
                if (currentFrame - timestamp < BestPawnCacheFrameValidity)
                {
                    Pawn cached = _bestPawnCache[key];
                    if (cached != null && !cached.Dead && cached.Map != null)
                        return cached;
                }
            }

            Pawn bestPawn = null;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.workSettings == null || !p.workSettings.EverWork) continue;
                if (p.WorkTypeIsDisabled(workType)) continue;
                if (GetIsIncapable(p, workType)) continue;

                if (bestPawn == null)
                {
                    bestPawn = p;
                }
                else if (worker.Compare(p, bestPawn) > 0)
                {
                    bestPawn = p;
                }
            }

            if (useCache)
            {
                _bestPawnCache[key] = bestPawn;
                _bestPawnCacheTimestamps[key] = currentFrame;
            }
            return bestPawn;
        }

        private static Color ColorForSkillLevel(int level)
        {
            if (_colorCache.TryGetValue(level, out var cached))
                return cached;

            Color result = level <= 3 ? BetterWorkTabMod.Settings.Color_VeryLowSkill :
                          level <= 9 ? BetterWorkTabMod.Settings.Color_LowSkill :
                          level <= 15 ? BetterWorkTabMod.Settings.Color_GoodLowSkill :
                          BetterWorkTabMod.Settings.Color_ExcellentSkill;

            _colorCache[level] = result;
            return result;
        }

        private static void DrawBigSkillNumber(Rect rect, int level)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);
            Widgets.Label(rect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawSmallSkillNumbers(Rect rect, int level)
        {
            // Position in top-right corner: right edge minus skill box size minus small padding
            string levelStr = level.ToString();
            float rightPadding = levelStr.Length >= 2 ? 0f : -3f;
            Rect boxRect = new Rect(
                rect.xMax - SkillBoxSize - rightPadding, 
                rect.y + SmallSkillOffsetY, 
                SkillBoxSize, 
                SkillBoxSize);
            
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);
            Widgets.Label(boxRect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        /// <summary>
        /// Draws the best pawn outline if the settings allow it based on ShowUIMode.
        /// This is called BEFORE the shift check so it can work with Always/Shifted/Unshifted modes.
        /// </summary>
        private static void DrawBestPawnOutlineIfNeeded(
            PawnColumnWorker_WorkPriority worker,
            Rect rect,
            Pawn pawn,
            PawnTable table,
            WorkTypeDef workType)
        {
            // Check if best pawn highlight is enabled and the ShowUIMode allows it
            var settings = BetterWorkTabMod.Settings;
            if (settings == null || settings.disableBestPawnHighlight)
                return;

            if (!ShouldShowUI(settings.ShowUIMode_ShowPawnForSkillSquare, _cachedUiState))
                return;

            Pawn bestPawn = GetBestPawnForWorktype(table, workType, worker);
            if (bestPawn == pawn)
            {
                DrawBestPawnOutline(rect);
            }
        }

        private static void DrawBestPawnOutline(Rect rect)
        {
            float x = rect.x + (rect.width - SkillBoxSize) / 2f;
            float y = rect.y + SkillBoxVerticalPadding;

            // Outline extends 1px beyond the skill box on all sides
            Rect outlineRect = new Rect(
                x - 1f,
                y - 1f,
                SkillBoxSize + 2f,
                SkillBoxSize + 2f);

#if v1_2 || v1_1 || (v1_0 || v0_19)
            Verse.Widgets.DrawBoxSolid(outlineRect, Color.clear);
            Color outlineCol = BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare;
            Color oldCol = GUI.color;
            GUI.color = outlineCol;
            Verse.Widgets.DrawBox(outlineRect, (uint)BetterWorkTabMod.Settings.bestPawnHighlightThickness > 0 ? (int)BetterWorkTabMod.Settings.bestPawnHighlightThickness : 1);
            GUI.color = oldCol;
#else
            Widgets.DrawBoxSolidWithOutline(
                outlineRect,
                Color.clear,
                BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare,
                BetterWorkTabMod.Settings.bestPawnHighlightThickness);
#endif
        }

        private static void DrawBestPawnBackground(Rect rect)
        {
            float x = rect.x + (rect.width - SkillBoxSize) / 2f;
            float y = rect.y + SkillBoxVerticalPadding;
            Rect boxRect = new Rect(x, y, SkillBoxSize, SkillBoxSize);

            Color highlightColor = BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare;
            highlightColor.a = 0.5f; // Semi-transparent background
            GUI.DrawTexture(boxRect, BaseContent.WhiteTex);
            Color oldColor = GUI.color;
            GUI.color = highlightColor;
            GUI.DrawTexture(boxRect, BaseContent.WhiteTex);
            GUI.color = oldColor;
        }


        private static bool ShouldShowUI(BetterWorkTabSettings.ShowUIMode mode, BetterWorkTabSettings.ShowUIMode currentState)
        {
            return mode == BetterWorkTabSettings.ShowUIMode.Always || mode == currentState;
        }

        public static void TrimCacheIfNeeded()
        {
            if (_skillCache.Count > 2000)
            {
                _skillCache.Clear();
                _skillCacheTimestamps.Clear();
            }
            if (_incapableCache.Count > 2000)
            {
                _incapableCache.Clear();
                _incapableCacheTimestamps.Clear();
            }
        }

        public static void ClearCaches()
        {
            _lastCachedFrame = -1;
            _skillCache.Clear();
            _skillCacheTimestamps.Clear();
            _incapableCache.Clear();
            _incapableCacheTimestamps.Clear();
            _bestPawnCache.Clear();
            _bestPawnCacheTimestamps.Clear();
            _colorCache.Clear();
        }
    }
}
