using Better_Work_Tab.Features;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

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
            if (!Event.current.shift)
                return;

            if (!(BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false))
                return;

            if (Time.frameCount != _hoveredHeaderFrame)
            {
                _hoveredHeaderWorkType = null;
                _hoveredHeaderFrame = Time.frameCount;
            }

            Rect labelRect = GetLabelRect(__instance, rect);
            if (Mouse.IsOver(labelRect))
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
        // === FRAME-LEVEL STATE ===
        private static int _lastCachedFrame = -1;
        private static bool _cachedShiftHeld = false;
        private static bool _cachedFeatureEnabled = false;
        private static BetterWorkTabSettings.ShowUIMode _cachedUiState;
        private static bool _cachedHoverCellOverlayEnabled = true;
        private static BetterWorkTabSettings.SkillViewHoverMode _cachedHoverMode = BetterWorkTabSettings.SkillViewHoverMode.Standard;

        // === CACHES ===
        private static readonly Dictionary<int, int> _skillCache = new Dictionary<int, int>(1024);
        private static readonly Dictionary<int, int> _skillCacheTimestamps = new Dictionary<int, int>(1024);
        private static readonly Dictionary<int, byte> _incapableCache = new Dictionary<int, byte>(1024);
        private static readonly Dictionary<int, int> _incapableCacheTimestamps = new Dictionary<int, int>(1024);
        private static readonly Dictionary<int, Pawn> _bestPawnCache = new Dictionary<int, Pawn>(64);
        private static readonly Dictionary<int, int> _bestPawnCacheTimestamps = new Dictionary<int, int>(64);
        private static readonly Dictionary<int, Color> _colorCache = new Dictionary<int, Color>(21);

        // Constants
        private const int SkillCacheFrameValidity = 60;
        private const int IncapableCacheFrameValidity = 120;
        private const int BestPawnCacheFrameValidity = 60;
        private const float SkillBoxSize = 25f;
        private const float SkillBoxVerticalPadding = 2.5f;
        private const float SmallSkillOffsetX = 16f;
        private const float SmallSkillOffsetY = -2f;
        private const float SkillBoxOutlinePadding = 2f;
        private const int BestPawnOutlineThickness = 3;

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
            if (pawn == null || pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return true;

            WorkTypeDef workType = __instance.def.workType;
            if (workType == null) return true;

            UpdateFrameCache();

            if (!_cachedFeatureEnabled || !_cachedShiftHeld || !_cachedHoverCellOverlayEnabled)
                return true;

            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
                return true;

            if (GetIsIncapable(pawn, workType))
                return true;

            if (pawn.WorkTypeIsDisabled(workType))
                return true;

            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
                return false; // Skip vanilla drawing if no relevant skills, to draw nothing or custom

            // Decide whether vanilla should draw based on hover mode.
            if (Mouse.IsOver(rect))
            {
                // Let vanilla draw for interactive priority handling in Standard or SkillFocused.
                if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.Standard ||
                    _cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.SkillFocused)
                {
                    return true;
                }
                return false;
            }

            // If NOT hovering, return FALSE to skip Vanilla.
            // Postfix will draw the Big Skill Number and static background.
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            if (!_cachedFeatureEnabled || !_cachedShiftHeld || !_cachedHoverCellOverlayEnabled)
                return;

            WorkTypeDef workType = __instance.def.workType;
            if (pawn == null || pawn.Dead || workType == null)
                return;

            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
                return;

            if (pawn.WorkTypeIsDisabled(workType))
                return;

            if (GetIsIncapable(pawn, workType))
                return;

            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
                return;

            int skillLevel = GetSkillLevel(pawn, workType);
            bool hovering = Mouse.IsOver(rect);

            float boxXSkill = rect.x + (rect.width - SkillBoxSize) / 2f;
            float boxYSkill = rect.y + SkillBoxVerticalPadding;
            Rect boxRect = new Rect(boxXSkill, boxYSkill, SkillBoxSize, SkillBoxSize);

            bool drawBigSkill = true;
            bool drawSmallSkill = false;
            bool drawSmallPriority = false;

            if (hovering)
            {
                if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.Standard)
                {
                    drawBigSkill = false;
                    drawSmallSkill = true;
                }
                else if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.SkillFocused)
                {
                    // Keep big skill visible and show priority in the small-number slot.
                    drawSmallPriority = true;
                }
            }

            if (drawBigSkill)
            {
                CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(boxXSkill, boxYSkill, pawn, workType, false);
                DrawBigSkillNumber(boxRect, skillLevel);
            }

            if (drawSmallSkill)
            {
                DrawSmallSkillNumbers(rect, skillLevel);
            }

            if (drawSmallPriority)
            {
                int priority = pawn.workSettings.GetPriority(workType);
                if (priority > 0)
                {
                    DrawSmallPriorityNumber(rect, priority);
                }
            }

            if (ShouldShowUI(BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare, _cachedUiState))
            {
                Pawn bestPawn = GetBestPawnForWorktype(table, workType, __instance);
                if (bestPawn == pawn)
                {
                    DrawBestPawnOutline(rect);
                }
            }
        }

        // ... [Rest of the caching and drawing helper methods remain exactly the same] ...

        // ==========================================================
        //  OPTIMIZED CACHING LOGIC
        // ==========================================================

        private static bool GetIsIncapable(Pawn p, WorkTypeDef work)
        {
            int key = (p.thingIDNumber << 16) | work.shortHash;
            int currentFrame = Time.frameCount;

            if (_incapableCacheTimestamps.TryGetValue(key, out int timestamp))
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

            _incapableCache[key] = (byte)(isIncapable ? 1 : 0);
            _incapableCacheTimestamps[key] = currentFrame;
            return isIncapable;
        }

        private static int GetSkillLevel(Pawn pawn, WorkTypeDef workType)
        {
            int key = (pawn.thingIDNumber << 16) | workType.shortHash;
            int currentFrame = Time.frameCount;

            if (_skillCacheTimestamps.TryGetValue(key, out int timestamp))
            {
                if (currentFrame - timestamp < SkillCacheFrameValidity)
                {
                    return _skillCache[key];
                }
            }

            float avg = pawn.skills.AverageOfRelevantSkillsFor(workType);
            int level = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);

            _skillCache[key] = level;
            _skillCacheTimestamps[key] = currentFrame;
            return level;
        }

        private static Pawn GetBestPawnForWorktype(PawnTable table, WorkTypeDef workType, PawnColumnWorker_WorkPriority worker)
        {
            if (table == null || table.cachedPawns == null) return null;

            int key = (table.GetHashCode() << 16) | workType.shortHash;
            int currentFrame = Time.frameCount;

            if (_bestPawnCacheTimestamps.TryGetValue(key, out int timestamp))
            {
                if (currentFrame - timestamp < BestPawnCacheFrameValidity)
                {
                    Pawn cached = _bestPawnCache[key];
                    if (cached != null && !cached.Dead && cached.Map != null)
                        return cached;
                }
            }

            Pawn bestPawn = null;
            var pawns = table.cachedPawns;

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

            _bestPawnCache[key] = bestPawn;
            _bestPawnCacheTimestamps[key] = currentFrame;
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
            Rect boxRect = new Rect(rect.x + SmallSkillOffsetX, rect.y + SmallSkillOffsetY, SkillBoxSize, SkillBoxSize);
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

        private static void DrawSmallPriorityNumber(Rect rect, int priority)
        {
            // Reuse the same placement as the small skill numbers for consistency.
            Rect prioRect = new Rect(rect.x + SmallSkillOffsetX, rect.y + SmallSkillOffsetY, SkillBoxSize, SkillBoxSize);
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            // Match vanilla priority number coloring.
            GUI.color = Color.white;
            Widgets.Label(prioRect, priority.ToString());

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawBestPawnOutline(Rect rect)
        {
            float x = rect.x + (rect.width - SkillBoxSize) / 2f;
            float y = rect.y + SkillBoxVerticalPadding;
            float outlineSize = SkillBoxSize + (SkillBoxOutlinePadding * 2f);

            Rect outlineRect = new Rect(
                Mathf.FloorToInt(x) - SkillBoxOutlinePadding,
                Mathf.FloorToInt(y) - SkillBoxOutlinePadding,
                outlineSize,
                outlineSize);

            Widgets.DrawBoxSolidWithOutline(
                outlineRect,
                Color.clear,
                BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare,
                BestPawnOutlineThickness);
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
