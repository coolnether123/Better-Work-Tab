using Better_Work_Tab.PawnOrganizer.API;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// Tracks which column header is being hovered while shift is held.
    /// When a header is hovered, that column shows priorities instead of skills.
    /// </summary>
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoHeader))]
    public static class Patch_WorkPriority_DoHeader_HoverTracker
    {
        private static WorkTypeDef _hoveredHeaderWorkType;
        private static int _hoveredHeaderFrame = -1;

        public static WorkTypeDef HoveredHeaderWorkType
        {
            get
            {
                // Return null if stale (different frame)
                if (Time.frameCount != _hoveredHeaderFrame)
                    return null;
                return _hoveredHeaderWorkType;
            }
        }

        public static void Postfix(PawnColumnWorker_WorkPriority __instance, Rect rect, PawnTable table)
        {
            // Only track when shift is held and feature is enabled
            if (!Event.current.shift)
                return;

            if (!(BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false))
                return;

            // Clear stale hover state at frame start
            if (Time.frameCount != _hoveredHeaderFrame)
            {
                _hoveredHeaderWorkType = null;
                _hoveredHeaderFrame = Time.frameCount;
            }

            // Check if mouse is over this header's label rect
            Rect labelRect = GetLabelRect(__instance, rect);
            if (Mouse.IsOver(labelRect))
            {
                _hoveredHeaderWorkType = __instance.def.workType;
            }
        }

        /// <summary>
        /// Replicates vanilla's GetLabelRect calculation for header hover detection.
        /// </summary>
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
        // === FRAME-LEVEL CACHING ===
        private static int _lastCachedFrame = -1;
        private static bool _cachedShiftHeld = false;
        private static bool _cachedFeatureEnabled = false;
        private static Vector2 _cachedMousePos = Vector2.zero;
        private static int _cachedMouseFrame = -1;
        private static readonly Dictionary<(PawnTable, WorkTypeDef), (Pawn bestPawn, int pawnCount, int cacheFrame)>
            _bestPawnPerWorktypeCache = new Dictionary<(PawnTable, WorkTypeDef), (Pawn, int, int)>(64);

        private static BetterWorkTabSettings.ShowUIMode _cachedUiState;
        private const int BestPawnCacheFrameValidity = 60;
        private const int MaxBestPawnCacheEntries = 256;

        /// <summary>
        /// Cache shift/feature state once per frame (not per cell)
        /// </summary>
        private static void UpdateFrameCache()
        {
            int currentFrame = Time.frameCount;
            if (_lastCachedFrame == currentFrame)
                return;

            _lastCachedFrame = currentFrame;

            _cachedFeatureEnabled = BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false;
            _cachedUiState = ShiftHelper.State;
            _cachedShiftHeld = _cachedUiState == BetterWorkTabSettings.ShowUIMode.Shifted;
        }

        /// <summary>
        /// Cache mouse position once per frame (not per cell)
        /// </summary>
        private static Vector2 GetCachedMousePosition()
        {
            int currentFrame = Time.frameCount;
            if (_cachedMouseFrame != currentFrame)
            {
                if (Event.current == null)
                {
                    Log.Warning("[BWT] GetCachedMousePosition called outside OnGUI context.");
                    _cachedMouseFrame = currentFrame;
                    return _cachedMousePos;
                }

                _cachedMousePos = Event.current?.mousePosition ?? Vector2.zero;
                _cachedMouseFrame = currentFrame;
            }
            return _cachedMousePos;
        }

        [HarmonyPrefix]
        public static bool Prefix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            var workType = __instance.def.workType;

            // Fast validation - let vanilla handle edge cases
            if (pawn?.Dead != false || pawn.workSettings == null || !pawn.workSettings.EverWork ||
                workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return true;
            }

            UpdateFrameCache();

            // Feature disabled or shift not held - run vanilla
            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
            {
                return true;
            }

            // If this column's header is being hovered, show priorities (vanilla behavior)
            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
            {
                return true;
            }

            // Incapable pawns should show vanilla disabled box
            if (IsIncapableOfWholeWorkType(pawn, workType))
            {
                return true;
            }

            // Skip vanilla for non-skill work types when shift held (we'll draw disabled or nothing)
            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
            {
                return false;
            }

            // When showing the skill overlay, skip vanilla drawing entirely and let postfix render.
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            UpdateFrameCache();

            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
                return;

            var workType = __instance.def.workType;

            if (pawn?.Dead != false || workType == null)
                return;

            // If this column's header is being hovered, vanilla already drew priorities/checkboxes
            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
                return;

            // If pawn is disabled for this work type by settings/backstory/age/etc,
            if (pawn.WorkTypeIsDisabled(workType))
                return;

            // Incapable pawns - vanilla already drew the disabled box
            if (IsIncapableOfWholeWorkType(pawn, workType))
                return;

            // No skills to show - don't draw anything
            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
                return;

            // === DRAW SKILL OVERLAY ===
            int skillLevel = GetSkillLevel(pawn, workType);

            // Use cached mouse position instead of Mouse.IsOver
            Vector2 cachedMouse = GetCachedMousePosition();
            bool hovering = rect.Contains(cachedMouse);

            float boxXSkill = rect.x + (rect.width - 25f) / 2f;
            float boxYSkill = rect.y + 2.5f;
            Rect boxRect = new Rect(boxXSkill, boxYSkill, 25f, 25f);

            if (!hovering)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(boxXSkill, boxYSkill, pawn, workType, false);
                }

                DrawBigSkillNumber(boxRect, skillLevel);
            }
            else
            {
                DrawSmallSkillNumbers(rect, skillLevel);
            }

            // Best-pawn outline — only for capable/allowed pawns (Also filter
            // candidates inside DrawBestPawnForSkillBox below).
            if (ShouldShowUI(
                BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare,
                _cachedUiState))
            {
                var bestPawn = GetBestPawnForWorktype(table, workType, __instance);
                if (bestPawn == pawn)
                {
                    float x = rect.x + (rect.width - 25f) / 2f;
                    float y = rect.y + 2.5f;
                    Rect outlineRect = new Rect(Mathf.FloorToInt(x) - 2, Mathf.FloorToInt(y) - 2, 29f, 29f);

                    Widgets.DrawBoxSolidWithOutline(
                        outlineRect,
                        Color.clear,
                        BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare,
                        3);
                }
            }
        }

        // ====== CACHING ======
        private static Dictionary<int, Color> _colorCache = new Dictionary<int, Color>(21);
        private static Dictionary<(int, string), (int level, int frame)> _skillCache =
            new Dictionary<(int, string), (int, int)>(256);

        private const int SkillCacheMaxSize = 512;
        private const int SkillCacheFrameValidity = 30;

        private static int GetSkillLevel(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills == null)
                return 0;

            var key = (pawn.thingIDNumber, workType.defName);

            if (_skillCache.TryGetValue(key, out var cached))
            {
                if (Time.frameCount - cached.frame < SkillCacheFrameValidity)
                    return cached.level;
            }

            float avg = pawn.skills.AverageOfRelevantSkillsFor(workType);
            int level = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 20);
            _skillCache[key] = (level, Time.frameCount);
            return level;
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

        /// <summary>
        /// Return best pawn for a worktype using cached results to avoid per-cell scans.
        /// </summary>
        private static Pawn GetBestPawnForWorktype(
            PawnTable table,
            WorkTypeDef workType,
            PawnColumnWorker_WorkPriority worker)
        {
            if (table?.cachedPawns == null || workType == null)
            {
                return null;
            }

            var key = (table, workType);
            int currentFrame = Time.frameCount;

            if (_bestPawnPerWorktypeCache.TryGetValue(key, out var cached))
            {
                if (currentFrame - cached.cacheFrame < BestPawnCacheFrameValidity &&
                    cached.pawnCount == table.cachedPawns.Count)
                {
                    return cached.bestPawn;
                }
            }

            Pawn bestPawn = null;
            var pawns = table.cachedPawns;

            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead) continue;
                if (p.workSettings == null || !p.workSettings.EverWork) continue;
                if (p.WorkTypeIsDisabled(workType)) continue;
                if (IsIncapableOfWholeWorkType(p, workType)) continue;

                if (bestPawn == null || (worker != null && worker.Compare(p, bestPawn) > 0))
                {
                    bestPawn = p;
                }
            }

            if (_bestPawnPerWorktypeCache.Count >= MaxBestPawnCacheEntries)
            {
                _bestPawnPerWorktypeCache.Clear();
            }

            _bestPawnPerWorktypeCache[key] = (bestPawn, pawns.Count, currentFrame);
            return bestPawn;
        }

        private static bool ShouldShowUI(
            BetterWorkTabSettings.ShowUIMode mode,
            BetterWorkTabSettings.ShowUIMode currentState)
        {
            return mode == BetterWorkTabSettings.ShowUIMode.Always || mode == currentState;
        }

        private static readonly string[] _skillStrings =
            Enumerable.Range(0, 21).Select(i => i.ToString()).ToArray();

        private static void DrawBigSkillNumber(Rect rect, int level)
        {
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);
            Widgets.Label(rect, _skillStrings[level]);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawSmallSkillNumbers(Rect rect, int level)
        {
            Rect boxRect = new Rect(rect.x + 16f, rect.y - 2f, 25f, 25f);
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColorForSkillLevel(level);
            Widgets.Label(boxRect, _skillStrings[level]);

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        public static void TrimCacheIfNeeded()
        {
            if (_bestPawnPerWorktypeCache.Count > MaxBestPawnCacheEntries)
            {
                _bestPawnPerWorktypeCache.Clear();
            }

            if (_skillCache.Count > SkillCacheMaxSize)
            {
                int currentFrame = Time.frameCount;
                var expiredKeys = new List<(int, string)>();

                foreach (var kvp in _skillCache)
                {
                    int frameAge = currentFrame - kvp.Value.frame;
                    // Remove entries older than 2x validity period
                    if (frameAge > SkillCacheFrameValidity * 2)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                // If we still have too many after removing old entries, nuke everything
                if (expiredKeys.Count == 0)
                {
                    _skillCache.Clear();
                    Log.Warning("[BWT] Skill cache hit max size with no expired entries. Clearing all.");
                }
                else
                {
                    foreach (var key in expiredKeys)
                    {
                        _skillCache.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a pawn is incapable of ALL work givers for a work type.
        /// </summary>
        private static bool IsIncapableOfWholeWorkType(Pawn p, WorkTypeDef work)
        {
            for (int i = 0; i < work.workGiversByPriority.Count; i++)
            {
                bool canDoThisGiver = true;
                var reqs = work.workGiversByPriority[i].requiredCapacities;

                for (int j = 0; j < reqs.Count; j++)
                {
                    if (!p.health.capacities.CapableOf(reqs[j]))
                    {
                        canDoThisGiver = false;
                        break;
                    }
                }

                if (canDoThisGiver)
                    return false;
            }

            return true;
        }
    }
}
