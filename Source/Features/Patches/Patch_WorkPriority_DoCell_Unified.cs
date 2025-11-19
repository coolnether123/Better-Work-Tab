using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Better_Work_Tab.PawnOrganizer.API;
using System.Collections.Generic;

namespace Better_Work_Tab.Patches
{
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), nameof(PawnColumnWorker_WorkPriority.DoCell))]
    public static class Patch_WorkPriority_DoCell_Unified
    {
        // === FRAME-LEVEL CACHING ===
        private static int _lastCachedFrame = -1;
        private static bool _cachedShiftHeld = false;
        private static bool _cachedFeatureEnabled = false;
        private static Vector2 _cachedMousePos = Vector2.zero;
        private static int _cachedMouseFrame = -1;

        /// <summary>
        /// Cache shift/feature state once per frame (not 290k times)
        /// </summary>
        private static void UpdateFrameCache()
        {
            int currentFrame = Time.frameCount;
            if (_lastCachedFrame == currentFrame)
            {
                return;
            }

            _lastCachedFrame = currentFrame;
            _cachedFeatureEnabled = BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false;
            _cachedShiftHeld = ShiftHelper.State == BetterWorkTabSettings.ShowUIMode.Shifted;
        }

        /// <summary>
        /// Cache mouse position once per frame (not 290k times)
        /// </summary>
        private static Vector2 GetCachedMousePosition()
        {
            int currentFrame = Time.frameCount;
            if (_cachedMouseFrame != currentFrame)
            {
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

            // Fast validation
            if (pawn?.Dead != false || pawn.workSettings == null || !pawn.workSettings.EverWork ||
                workType == null || pawn.WorkTypeIsDisabled(workType))
            {
                return true;
            }

            UpdateFrameCache();

            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
            {
                return true; // Let vanilla run
            }

            // Skip vanilla for non-skill work types when shift held
            if (workType.relevantSkills.Count == 0)
            {
                return false; // Don't run vanilla
            }

            return true; // Run vanilla first
        }

        [HarmonyPostfix]
        public static void Postfix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            // === ULTRA-FAST EARLY EXITS ===

            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
            {
                return; // Exit immediately
            }

            var workType = __instance.def.workType;

            if (pawn?.Dead != false || workType?.relevantSkills.Count == 0)
            {
                return;
            }


            // === NOW DO THE EXPENSIVE WORK ===

            int skillLevel = GetSkillLevel(pawn, workType);

            // Use cached mouse position instead of Mouse.IsOver
            Vector2 cachedMouse = GetCachedMousePosition();
            bool hovering = rect.Contains(cachedMouse);

            float boxX = rect.x + (rect.width - 25f) / 2f;
            float boxY = rect.y + 2.5f;
            Rect boxRect = new Rect(boxX, boxY, 25f, 25f);

            if (!hovering)
            {
                bool incapable = IsIncapableOfWholeWorkType(pawn, workType);

                if (Event.current.type == EventType.Repaint)
                {
                    CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(boxX, boxY, pawn, workType, incapable);
                }

                DrawBigSkillNumber(boxRect, skillLevel);
            }
            else
            {
                DrawSmallSkillNumbers(rect, skillLevel);
            }

            // Best-pawn outline
            if (ShouldShowUI(
                    BetterWorkTabMod.Settings.ShowUIMode_ShowPawnForSkillSquare,
                    ShiftHelper.State))
            {
                DrawBestPawnForSkillBox(rect, pawn, table, __instance);
            }
        }

        // ====== CACHING ======
        private static Dictionary<int, Color> _colorCache = new Dictionary<int, Color>(21);
        private static Dictionary<(int, string), (int level, int frame)> _skillCache =
            new Dictionary<(int, string), (int, int)>(256);
        private const int SkillCacheFrameValidity = 30; // 500ms at 60fps

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

        private static bool ShouldShowUI(
            BetterWorkTabSettings.ShowUIMode mode,
            BetterWorkTabSettings.ShowUIMode currentState)
        {
            return mode == BetterWorkTabSettings.ShowUIMode.Always || mode == currentState;
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
            Rect boxRect = new Rect(rect.x + 16f, rect.y - 2f, 25f, 25f);
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

        private static void DrawBestPawnForSkillBox(
            Rect rect, Pawn pawn, PawnTable table, PawnColumnWorker_WorkPriority instance)
        {
            foreach (var otherPawn in table.cachedPawns)
            {
                if (otherPawn != pawn && instance.Compare(pawn, otherPawn) < 0)
                    return;
            }

            float x = rect.x + (rect.width - 25f) / 2f;
            float y = rect.y + 2.5f;
            Rect outlineRect = new Rect(Mathf.FloorToInt(x) - 2, Mathf.FloorToInt(y) - 2, 29f, 29f);

            Widgets.DrawBoxSolidWithOutline(outlineRect, Color.clear,
                BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare, 3);
        }

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