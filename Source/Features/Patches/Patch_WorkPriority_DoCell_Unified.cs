using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Input;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Commands;
using HarmonyLib;
using RimWorld;
using Spine.Profiling;
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

            // A hovered Fluffy work-giver column is not a hovered work type.
            if (FluffyWorkTabGateway.IsFluffyWorkGiverColumn(__instance.def))
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
                isHovered = !BWTWorkTabTutorial.OwnsCurrentPointer && Mouse.IsOver(labelRect);
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
        private static readonly HashSet<long> SkillNumbersDrawnThisFrame = new HashSet<long>();
        private static int _skillNumbersDrawnFrame = -1;

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
        private const float SkillBoxSize = WorkPriorityCellGeometry.BoxSize;
        private const float SkillBoxVerticalPadding = WorkPriorityCellGeometry.BoxTopPadding;
        private const float SmallSkillOffsetY = -2f;
        private const float SmallCornerLabelWidth = 18f;
        private const float SmallCornerLabelHeight = 16f;
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

        internal static bool WasSkillNumberDrawnRecently(Pawn pawn, WorkTypeDef workType)
        {
            return pawn != null &&
                   workType != null &&
                   Time.frameCount == _skillNumbersDrawnFrame &&
                   SkillNumbersDrawnThisFrame.Contains(GetSkillDrawKey(pawn, workType));
        }

        [HarmonyPrefix]
        public static bool Prefix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            if (SpineTiming.Enabled)
            {
                return SpineTiming.Time("Harmony.WorkPriority.DoCell.Prefix", () => PrefixProfiled(__instance, rect, pawn, table));
            }

            return PrefixProfiled(__instance, rect, pawn, table);
        }

        private static bool PrefixProfiled(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            // Only apply BWT patches to the Work tab (vanilla or BWT), not other tabs like MechTab
            if (!UI.Headers.PawnColumnWorker_WorkPriority_DoHeader_Patch.IsWorkTab())
                return true;

            WorkTypeDef workType = __instance.def.workType;
            if (workType == null)
                return true;

            // Fluffy work-giver columns inherit this method but represent a single work giver, not the
            // work type their def points at. Let Fluffy draw its own sub-work boxes.
            if (FluffyWorkTabGateway.IsFluffyWorkGiverColumn(__instance.def))
                return true;

            if (SubWorkDrilldownState.TryGetCurrentDrawingWorkGiver(
                    __instance.def,
                    out var drawingWorkGiver,
                    out var drawingParentWorkType,
                    out _) &&
                !SubWorkDrilldownState.IsActive)
            {
                if (pawn == null || pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                {
                    return false;
                }

                DrawSubWorkPriorityCell(rect, pawn, drawingParentWorkType, drawingWorkGiver);
                return false;
            }

            if (SubWorkDrilldownState.IsActive)
            {
                if (pawn == null || pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                {
                    return false;
                }

                if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(__instance.def, out var workGiver, out _))
                {
                    DrawParentPriorityCellVisual(rect, pawn, workType, SubWorkDrilldownState.ParentWorkContentAlpha);
                    return false;
                }

                DrawParentPriorityCellVisual(rect, pawn, workType, SubWorkDrilldownState.ParentWorkContentAlpha);
                DrawSubWorkPriorityCell(rect, pawn, SubWorkDrilldownState.ActiveWorkType, workGiver);
                return false;
            }

            if (pawn == null || pawn.Dead || pawn.workSettings == null || !pawn.workSettings.EverWork)
                return true;

            if (FluffyTimeScheduleAssigner.TryDrawWorkTypeCell(rect, pawn, workType))
            {
                return false;
            }

            // Vanilla disabled/incapable cells are display-only. Apply the same guard before
            // BWT's wheel handler so it cannot reach Pawn_WorkSettings.SetPriority first.
            if (GetIsIncapable(pawn, workType) || pawn.WorkTypeIsDisabled(workType))
            {
                return true;
            }

            UpdateFrameCache();
            bool timePriorityOwnsMouse = TimePriorityScheduleEditor.OwnsCurrentMousePosition;

            TryHandleWorkPriorityScroll(rect, pawn, workType);

            if (TryHandleParentSubWorkOverrideInput(rect, pawn, workType) ||
                TryHandleScheduleIndicatorInput(rect, pawn, workType))
            {
                return false;
            }

            // If skill overlay feature is disabled or shift is not held, use vanilla rendering
            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
                return true;

            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
                return true;

            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
                return true;

            // Track column hover state only if hover cell overlay is enabled
            bool hoveringCell = !timePriorityOwnsMouse &&
                                !BWTWorkTabTutorial.OwnsCurrentPointer &&
                                Mouse.IsOver(rect);
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

                TryHandleWorkPriorityInput(rect, pawn, workType);
                return false;
            }

            if (columnHovered)
            {
                if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.Standard)
                {
                    return true;
                }

                TryHandleWorkPriorityInput(rect, pawn, workType);
                return false;
            }

            TryHandleWorkPriorityInput(rect, pawn, workType);
            return false;
        }

        [HarmonyPostfix]
        public static void Postfix(
            PawnColumnWorker_WorkPriority __instance,
            Rect rect,
            Pawn pawn,
            PawnTable table)
        {
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("Harmony.WorkPriority.DoCell.Postfix", () => PostfixProfiled(__instance, rect, pawn, table));
                return;
            }

            PostfixProfiled(__instance, rect, pawn, table);
        }

        private static void PostfixProfiled(
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

            if (FluffyTimeScheduleAssigner.IsOpen)
                return;

            if (FluffyWorkTabGateway.IsFluffyWorkGiverColumn(__instance.def))
                return;

            if (pawn.WorkTypeIsDisabled(workType))
                return;

            if (GetIsIncapable(pawn, workType))
                return;

            // Draw best pawn outline - this uses the ShowUIMode setting to determine when to show
            // (can be Always, Shifted, Unshifted, or Never)
            DrawBestPawnOutlineIfNeeded(__instance, rect, pawn, table, workType);

            if (!_cachedFeatureEnabled || !_cachedShiftHeld)
            {
                DrawParentSubWorkOverrideIndicatorIfNeeded(rect, pawn, workType);
                return;
            }

            if (Patch_WorkPriority_DoHeader_HoverTracker.HoveredHeaderWorkType == workType)
            {
                DrawParentSubWorkOverrideIndicatorIfNeeded(rect, pawn, workType);
                return;
            }

            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0)
            {
                DrawParentSubWorkOverrideIndicatorIfNeeded(rect, pawn, workType);
                return;
            }

            int priority = pawn.workSettings.GetPriority(workType);
            int skillLevel = GetSkillLevel(pawn, workType);
            bool hoveringCell = !TimePriorityScheduleEditor.OwnsCurrentMousePosition &&
                                !BWTWorkTabTutorial.OwnsCurrentPointer &&
                                Mouse.IsOver(rect);
            
            // Only check column hover if hover overlay is enabled
            bool columnHovered = _cachedHoverCellOverlayEnabled &&
                                 _cachedHoverScope == BetterWorkTabSettings.HoverEffectScope.ColumnWide &&
                                 _columnHoveredWorkType != null &&
                                 _columnHoveredWorkType == workType &&
                                 (_columnHoveredFrame == Time.frameCount || _columnHoveredFrame == Time.frameCount - 1);
            bool hovering = hoveringCell || columnHovered;

            float boxXSkill = rect.x + (rect.width - SkillBoxSize) / 2f;
            float boxYSkill = rect.y + SkillBoxVerticalPadding;
            Rect boxRect = GetWorkBoxRect(rect);

            bool drawBigSkill = true;
            bool drawSmallSkill = false;
            bool drawSmallPriority = false;
            bool showTinySkillNumbers = (BetterWorkTabMod.Settings?.enableSkillOverlayFeature ?? false) &&
                                        ShouldShowUI(BetterWorkTabMod.Settings.ShowUIMode_ShowSmallSkillNumbers, _cachedUiState);

            if (_cachedHoverCellOverlayEnabled && hovering)
            {
                if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.Standard)
                {
                    drawBigSkill = false;
                    drawSmallSkill = true;
                }
                else if (_cachedHoverMode == BetterWorkTabSettings.SkillViewHoverMode.SkillFocused)
                {
                    drawSmallPriority = true;
                }
            }

            if (showTinySkillNumbers && !drawSmallPriority)
            {
                drawSmallSkill = true;
            }

            if (drawBigSkill)
            {
                CustomWorkBoxDrawer.DrawWorkBoxForSkillOverlay(boxXSkill, boxYSkill, pawn, workType, false);
                DrawBigSkillNumber(boxRect, skillLevel);
                RecordSkillNumberDrawn(pawn, workType);
            }

            if (drawSmallSkill)
            {
                DrawSmallSkillNumbers(rect, skillLevel);
                RecordSkillNumberDrawn(pawn, workType);
            }

            if (drawSmallPriority && priority > 0)
            {
                DrawSmallPriorityNumber(rect, priority);
            }

            DrawParentSubWorkOverrideIndicatorIfNeeded(rect, pawn, workType);
        }

        private static void RecordSkillNumberDrawn(Pawn pawn, WorkTypeDef workType)
        {
            if (_skillNumbersDrawnFrame != Time.frameCount)
            {
                SkillNumbersDrawnThisFrame.Clear();
                _skillNumbersDrawnFrame = Time.frameCount;
            }

            if (pawn != null && workType != null)
            {
                SkillNumbersDrawnThisFrame.Add(GetSkillDrawKey(pawn, workType));
            }
        }

        private static long GetSkillDrawKey(Pawn pawn, WorkTypeDef workType)
        {
            return ((long)pawn.thingIDNumber << 32) | workType.shortHash;
        }

        // Caching helpers

        private static void DrawSubWorkPriorityCell(Rect rect, Pawn pawn, WorkTypeDef parentWorkType, WorkGiver workGiver)
        {
            // BWT Focus View deliberately retains BWT/vanilla-sized Work boxes.
            // Compact 20 px boxes belong only to the Fluffy-style expand-beside path.
            Rect boxRect = WorkPriorityCellGeometry.GetPriorityBoxRect(rect);
            float visualAlpha = 1f;
            float visualScale = 1f;
            if (SubWorkDrilldownState.IsActive)
            {
                SubWorkDrilldownState.TryGetSubWorkContentTransitionVisuals(workGiver, out visualAlpha, out visualScale);
            }
            Better_Work_Tab.UI.WorkGiverReassignments.WorkGiverPriorityBoxRenderer.DrawPriorityBox(
                workGiver,
                parentWorkType,
                pawn,
                boxRect,
                visualAlpha,
                visualScale);
        }

        private static void DrawParentPriorityCellVisual(Rect rect, Pawn pawn, WorkTypeDef workType, float alpha)
        {
            if (alpha <= 0.001f || pawn == null || workType == null || pawn.workSettings == null)
            {
                return;
            }

            Rect boxRect = GetWorkBoxRect(rect);
            int priority = WorkPrioritySystem.ClampPriority(pawn.workSettings.GetPriority(workType));

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            bool oldWordWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;

                GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * alpha);
                WidgetsWork.DrawWorkBoxBackground(boxRect, pawn, workType);

                if (Find.PlaySettings.useWorkPriorities)
                {
                    if (priority > WorkPrioritySystem.DisabledPriority)
                    {
                        Text.Font = GameFont.Medium;
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Color priorityColor = WorkPrioritySystem.GetPriorityColor(priority);
                        priorityColor.a *= alpha;
                        GUI.color = priorityColor;
                        Widgets.Label(boxRect.ContractedBy(-3f), priority.ToString());
                    }
                }
                else if (priority > WorkPrioritySystem.DisabledPriority)
                {
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.DrawTexture(boxRect, WidgetsWork.WorkBoxCheckTex);
                }
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                Text.WordWrap = oldWordWrap;
            }
        }

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

            // Asked of the work type's own givers, exactly as vanilla's
            // PawnColumnWorker_WorkPriority.IsIncapableOfWholeWorkType asks it.
            //
            // This used to read BWT's reassigned, ordered giver list instead.
            // That list answers a different question -- which givers this work
            // type *displays*, after the player has moved things around -- and
            // it can legitimately come back empty or short, because a giver
            // reassigned to another work type drops out of it and only defs
            // whose Worker instantiates are included. Both loops treat "no giver
            // I can do" and "no givers at all" the same way, so a short list
            // reported a perfectly healthy colonist as incapable, and vanilla
            // then tinted the cell red over its skill band -- the olive
            // background on Warden, Hunt and Plant cut.
            //
            // Whether a pawn's body can do the work does not depend on how the
            // player has arranged the columns.
            bool canDoAny = false;
            List<WorkGiverDef> workGivers = work.workGiversByPriority;
            for (int i = 0; workGivers != null && i < workGivers.Count; i++)
            {
                bool thisGiverOk = true;
                var reqs = workGivers[i]?.requiredCapacities;
                for (int j = 0; reqs != null && j < reqs.Count; j++)
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
            if (table == null || table.cachedPawns == null) return null;
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
            Rect labelRect = GetTopRightOverlayRect(rect);
            
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = ColorForSkillLevel(level);
            Widgets.Label(labelRect, level.ToString());

            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        private static void DrawSmallPriorityNumber(Rect rect, int priority)
        {
            string priorityText = priority.ToString();
            Rect priorityRect = GetTopRightOverlayRect(rect);

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldColor = GUI.color;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            Widgets.Label(priorityRect, priorityText);

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

            Widgets.DrawBoxSolidWithOutline(
                outlineRect,
                Color.clear,
                BetterWorkTabMod.Settings.Color_BestPawnForSkillSquare,
                BetterWorkTabMod.Settings.bestPawnHighlightThickness);
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

        private static Rect GetWorkBoxRect(Rect cellRect)
        {
            return WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect);
        }

        private static Rect GetTopRightOverlayRect(Rect cellRect)
        {
            Rect boxRect = GetWorkBoxRect(cellRect);
            return new Rect(
                boxRect.xMax - SmallCornerLabelWidth - 1f,
                boxRect.y + SmallSkillOffsetY,
                SmallCornerLabelWidth,
                SmallCornerLabelHeight);
        }

        private static bool ShouldShowParentSubWorkOverrideIndicator(Pawn pawn, WorkTypeDef workType)
        {
            return WorkGiverReassignmentManager.LockedSubWorkOverridesDisabledParent() &&
                   pawn?.workSettings != null &&
                   workType != null &&
                   !pawn.WorkTypeIsDisabled(workType) &&
                   WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType) <= WorkPrioritySystem.DisabledPriority &&
                   WorkGiverReassignmentManager.HasEnabledPawnOverrideForWorkType(pawn, workType);
        }

        private static void DrawParentSubWorkOverrideIndicatorIfNeeded(Rect cellRect, Pawn pawn, WorkTypeDef workType)
        {
            if (ShouldShowParentSubWorkOverrideIndicator(pawn, workType))
            {
                PriorityOverrideRing.Draw(GetWorkBoxRect(cellRect));
                return;
            }

            DrawScheduleIndicatorIfNeeded(cellRect, pawn, workType);
        }

        /// <summary>
        /// Whether this cell's work has any hour pinned away from the priority
        /// box.
        ///
        /// A schedule is invisible once it is closed, so a player had no way to
        /// tell that a pawn's priority for some hours is not the number in front
        /// of them. The ring says so on the cell itself.
        ///
        /// It yields to the sub-work override ring above rather than drawing
        /// over it. One gold ring should not mean two things at once, and that
        /// one is both rarer and more urgent -- it marks work happening on a
        /// work type the player has switched off.
        /// </summary>
        private static bool ShouldShowScheduleIndicator(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null ||
                workType == null ||
                pawn.WorkTypeIsDisabled(workType) ||
                !TimePriorityService.IsRuntimeEnabled)
            {
                return false;
            }

            return TimePriorityService.HasCustomSchedule(
                TimePriorityTarget.ForWorkType(pawn, workType),
                WorkPrioritySystem.GetPriorityForPawnWorkType(pawn, workType));
        }

        private static void DrawScheduleIndicatorIfNeeded(Rect cellRect, Pawn pawn, WorkTypeDef workType)
        {
            if (!ShouldShowScheduleIndicator(pawn, workType))
            {
                return;
            }

            PriorityOverrideRing.Draw(GetWorkBoxRect(cellRect));
        }

        /// <summary>
        /// Opens the schedule when its ring is clicked, so the pinned hours the
        /// ring is reporting can actually be looked at.
        /// </summary>
        private static bool TryHandleScheduleIndicatorInput(Rect cellRect, Pawn pawn, WorkTypeDef workType)
        {
            Event evt = Event.current;
            if (evt == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                evt.type != EventType.MouseDown ||
                evt.button != 0 ||
                ShouldShowParentSubWorkOverrideIndicator(pawn, workType) ||
                !ShouldShowScheduleIndicator(pawn, workType))
            {
                return false;
            }

            if (!PriorityOverrideRing.EventOverVisibleRing(evt, GetWorkBoxRect(cellRect)))
            {
                return false;
            }

            TimePriorityScheduleEditor.OpenForFloatMenu(pawn, workType);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }

        private static bool TryHandleParentSubWorkOverrideInput(Rect cellRect, Pawn pawn, WorkTypeDef workType)
        {
            Event evt = Event.current;
            if (evt == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                evt.type != EventType.MouseDown ||
                evt.button != 0 ||
                BetterWorkTabLocalState.IsHeaderDragging ||
                SubWorkDrilldownInput.MatchesGesture(evt) ||
                !ShouldShowParentSubWorkOverrideIndicator(pawn, workType))
            {
                return false;
            }

            Rect boxRect = GetWorkBoxRect(cellRect);
            if (!PriorityOverrideRing.EventOverVisibleRing(evt, boxRect))
            {
                return false;
            }

            WorkGiverReassignmentManager.ClearPawnOverridesForWorkTypeSynced(pawn.thingIDNumber, workType.defName);
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            evt.Use();
            return true;
        }

        internal static bool TryHandleRootPriorityInput(Rect rootCellRect, Pawn pawn, WorkTypeDef workType)
        {
            return TryHandleParentSubWorkOverrideInput(rootCellRect, pawn, workType) ||
                TryHandleScheduleIndicatorInput(rootCellRect, pawn, workType) ||
                TryHandleWorkPriorityScroll(rootCellRect, pawn, workType, trustHit: true) ||
                TryHandleWorkPriorityInput(rootCellRect, pawn, workType, trustHit: true);
        }

        private static bool TryHandleWorkPriorityScroll(
            Rect cellRect,
            Pawn pawn,
            WorkTypeDef workType,
            bool trustHit = false)
        {
            Event evt = Event.current;
            if (evt == null ||
                !(BetterWorkTabMod.Settings?.enableScrollWheelPriority ?? false) ||
                evt.type != EventType.ScrollWheel ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                (!trustHit &&
                 (BWTWorkTabTutorial.OwnsCurrentPointer || !Mouse.IsOver(cellRect))))
            {
                return false;
            }

            int currentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
            int direction = evt.delta.y > 0 ? -1 : 1;
            int nextPriority = Find.PlaySettings.useWorkPriorities
                ? WorkPrioritySystem.GetPriorityAfterBoundedStep(currentPriority, direction)
                : currentPriority > WorkPrioritySystem.DisabledPriority
                    ? WorkPrioritySystem.DisabledPriority
                    : WorkPrioritySystem.GetDefaultEnabledPriority();
            if (nextPriority != currentPriority)
            {
                WorkPriorityCommandGateway.Execute(new SetPriorityCommand(pawn, workType, nextPriority));
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            evt.Use();
            return true;
        }

        private static bool TryHandleWorkPriorityInput(
            Rect cellRect,
            Pawn pawn,
            WorkTypeDef workType,
            bool trustHit = false)
        {
            Event evt = Event.current;
            if (evt == null ||
                TimePriorityScheduleEditor.OwnsCurrentMousePosition ||
                evt.type != EventType.MouseDown)
            {
                return false;
            }

            if (SubWorkDrilldownInput.MatchesGesture(evt))
            {
                return false;
            }

            if (!trustHit &&
                (BWTWorkTabTutorial.OwnsCurrentPointer || !Mouse.IsOver(GetWorkBoxRect(cellRect))))
            {
                return false;
            }

            if (Find.PlaySettings.useWorkPriorities)
            {
                if (evt.button != 0 && evt.button != 1)
                {
                    return false;
                }

                bool wasActive = pawn.workSettings.WorkIsActive(workType);
                int currentPriority = WorkPrioritySystem.GetCurrentPriorityForPawnWorkType(pawn, workType);
                int nextPriority = WorkPrioritySystem.GetPriorityAfterMouseButton(currentPriority, evt.button);

                if (nextPriority != currentPriority)
                {
                    WorkPriorityCommandGateway.Execute(new SetPriorityCommand(pawn, workType, nextPriority));
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                }

                NotifyWorkActivatedIfNeeded(pawn, workType, wasActive);
                evt.Use();
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);
                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.ManualWorkPriorities, KnowledgeAmount.SmallInteraction);
                return true;
            }

            if (evt.button != 0)
            {
                return false;
            }

            bool wasEnabled = pawn.workSettings.WorkIsActive(workType);
            if (pawn.workSettings.GetPriority(workType) > 0)
            {
                WorkPriorityCommandGateway.Execute(new SetPriorityCommand(
                    pawn,
                    workType,
                    WorkPrioritySystem.DisabledPriority));
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
            }
            else
            {
                WorkPriorityCommandGateway.Execute(new SetPriorityCommand(
                    pawn,
                    workType,
                    WorkPrioritySystem.GetDefaultEnabledPriority()));
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            }

            NotifyWorkActivatedIfNeeded(pawn, workType, wasEnabled);
            evt.Use();
            PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);
            return true;
        }

        private static void NotifyWorkActivatedIfNeeded(Pawn pawn, WorkTypeDef workType, bool wasActive)
        {
            if (wasActive || !pawn.workSettings.WorkIsActive(workType))
            {
                return;
            }

            if (workType.relevantSkills != null &&
                workType.relevantSkills.Any() &&
                pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
            {
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            }

            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                Messages.Message(
                    "MessageIdeoOpposedWorkTypeSelected".Translate(pawn, workType.gerundLabel),
                    pawn,
                    MessageTypeDefOf.CautionInput,
                    false);
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
            }
        }
    }
}
