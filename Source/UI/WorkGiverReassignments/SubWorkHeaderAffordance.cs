using System.Collections.Generic;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    /// <summary>
    /// Shared immediate-mode geometry, hit testing, and drawing for sub-work header affordances.
    /// </summary>
    internal static class SubWorkHeaderAffordance
    {
        private const float BadgeWidth = 16f;
        private const float BadgeHeight = 16f;
        private const float VanillaLabelGap = 5f;
        private const float BackBadgeSize = 18f;

        private static readonly Dictionary<string, int> WorkGiverCountCache = new Dictionary<string, int>();
        private static readonly Dictionary<PawnColumnDef, Rect> VanillaOpenBadgeRects = new Dictionary<PawnColumnDef, Rect>();
        private static int _cachedSyncVersion = -1;
        private static int _vanillaBadgeRectsFrame = -1;

        internal static string DebugForcedHoveredWorkTypeDefName;
        internal static bool DebugForceBackButtonHover;

        internal static bool ShouldDrawOpenBadge(PawnColumnDef column)
        {
            return SubWorkDrilldownInput.IsEnabled &&
                   (BetterWorkTabMod.Settings?.showSubWorkHeaderBadge ?? true) &&
                   !SubWorkDrilldownState.IsActive &&
                   column?.workType != null &&
                   column.Worker is PawnColumnWorker_WorkPriority &&
                   GetDisplayWorkGiverCount(column.workType) > 1;
        }

        internal static Rect GetOpenBadgeRect(Rect headerRect, bool clearVanillaStem)
        {
            float x = headerRect.xMax - BadgeWidth - 1f;

            return new Rect(
                Mathf.Round(x),
                Mathf.Round(headerRect.yMax - BadgeHeight - 2f),
                BadgeWidth,
                BadgeHeight);
        }

        internal static void DrawOpenBadge(Rect headerRect, PawnColumnDef column, bool clearVanillaStem)
        {
            if (!ShouldDrawOpenBadge(column))
            {
                return;
            }

            Rect badgeRect = GetOpenBadgeRect(headerRect, clearVanillaStem);
            DrawOpenEllipsisBadge(badgeRect, IsOpenBadgeHovered(badgeRect, column));
            TooltipHandler.TipRegion(badgeRect, "Show specific jobs".Colorize(ColoredText.SubtleGrayColor));
            MouseoverSounds.DoRegion(badgeRect);
        }

        internal static void DrawOpenBadge(Rect headerRect, Rect textRect, PawnColumnDef column)
        {
            if (!ShouldDrawOpenBadge(column))
            {
                return;
            }

            Rect badgeRect = GetVanillaOpenBadgeRect(headerRect, textRect);
            RememberVanillaOpenBadgeRect(column, badgeRect);
            DrawOpenEllipsisBadge(badgeRect, IsOpenBadgeHovered(badgeRect, column));
            TooltipHandler.TipRegion(badgeRect, "Show specific jobs".Colorize(ColoredText.SubtleGrayColor));
            MouseoverSounds.DoRegion(badgeRect);
        }

        internal static bool TryGetOpenBadgeRect(PawnColumnDef column, Rect headerRect, bool isVanillaStaggered, out Rect badgeRect)
        {
            if (isVanillaStaggered && TryGetRememberedVanillaOpenBadgeRect(column, out badgeRect))
            {
                return true;
            }

            badgeRect = GetOpenBadgeRect(headerRect, clearVanillaStem: false);
            return true;
        }

        internal static bool TryGetOpenBadgeTarget(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            out WorkTypeDef workType,
            out Rect badgeRect)
        {
            workType = null;
            badgeRect = default;

            if (layout?.Columns == null)
            {
                return false;
            }

            bool clearVanillaStem = !BetterWorkTabMod.Settings.enableAngledHeaders;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                var def = column.Column;
                if (!ShouldDrawOpenBadge(def))
                {
                    continue;
                }

                float animatedOffset = ColumnReorderAnimationState.GetHeaderOffset(column);
                Rect headerRect = Mathf.Abs(animatedOffset) > 0.01f
                    ? new Rect(column.HeaderRect.x + animatedOffset, column.HeaderRect.y, column.HeaderRect.width, column.HeaderRect.height)
                    : column.HeaderRect;
                if (clearVanillaStem && TryGetRememberedVanillaOpenBadgeRect(def, out Rect rememberedBadgeRect))
                {
                    badgeRect = rememberedBadgeRect;
                }
                else
                {
                    badgeRect = GetOpenBadgeRect(headerRect, clearVanillaStem: false);
                }

                if (!badgeRect.Contains(mousePosition))
                {
                    continue;
                }

                workType = def.workType;
                return true;
            }

            return false;
        }

        private static bool IsOpenBadgeHovered(Rect badgeRect, PawnColumnDef column)
        {
            if (Mouse.IsOver(badgeRect))
            {
                return true;
            }

            return !DebugForcedHoveredWorkTypeDefName.NullOrEmpty() &&
                   column?.workType != null &&
                   column.workType.defName == DebugForcedHoveredWorkTypeDefName;
        }

        private static Rect GetVanillaOpenBadgeRect(Rect headerRect, Rect textRect)
        {
            float x = textRect.xMax + VanillaLabelGap;
            bool hasRightLabelSpace = x <= headerRect.xMax - BadgeWidth - 1f;
            if (!hasRightLabelSpace)
            {
                return GetOpenBadgeRect(headerRect, clearVanillaStem: false);
            }

            float y = textRect.center.y - (BadgeHeight / 2f);
            y = Mathf.Clamp(y, headerRect.yMin + 1f, headerRect.yMax - BadgeHeight - 1f);

            return new Rect(Mathf.Round(x), Mathf.Round(y), BadgeWidth, BadgeHeight);
        }

        private static void RememberVanillaOpenBadgeRect(PawnColumnDef column, Rect badgeRect)
        {
            if (column == null)
            {
                return;
            }

            EnsureVanillaOpenBadgeFrame();
            VanillaOpenBadgeRects[column] = badgeRect;
        }

        private static bool TryGetRememberedVanillaOpenBadgeRect(PawnColumnDef column, out Rect badgeRect)
        {
            badgeRect = default;
            EnsureVanillaOpenBadgeFrame();
            return column != null && VanillaOpenBadgeRects.TryGetValue(column, out badgeRect);
        }

        private static void EnsureVanillaOpenBadgeFrame()
        {
            if (_vanillaBadgeRectsFrame == Time.frameCount)
            {
                return;
            }

            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                VanillaOpenBadgeRects.Clear();
                _vanillaBadgeRectsFrame = Time.frameCount;
                return;
            }

            if (_vanillaBadgeRectsFrame < 0)
            {
                _vanillaBadgeRectsFrame = Time.frameCount;
            }
        }

        internal static Rect GetBackBadgeRect(Rect labelCellRect)
        {
            return new Rect(
                Mathf.Round(labelCellRect.x + 7f),
                Mathf.Round(labelCellRect.center.y - (BackBadgeSize / 2f)),
                BackBadgeSize,
                BackBadgeSize);
        }

        internal static void DrawBackBadge(Rect labelCellRect)
        {
            Rect badgeRect = GetBackBadgeRect(labelCellRect);
            DrawBackArrowBadge(badgeRect, IsBackButtonHovered(labelCellRect));
        }

        internal static bool IsBackButtonHovered(Rect labelCellRect)
        {
            return Mouse.IsOver(labelCellRect) || DebugForceBackButtonHover;
        }

        internal static bool TryGetBackLabelCellRect(IWorkTabLayoutController layout, out Rect labelCellRect)
        {
            labelCellRect = default;
            if (layout?.Columns == null || layout.Table == null)
            {
                return false;
            }

            float rowHeight = SubWorkDrilldownState.GlobalRowReservedHeight;
            if (rowHeight <= 0.5f)
            {
                return false;
            }

            float rowTop = layout.TableOrigin.y +
                layout.HeaderHeight +
                TimePriorityPlannerPrototype.HeaderPinnedRowsHeight;

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_Label))
                {
                    continue;
                }

                float animatedOffset = ColumnReorderAnimationState.GetHeaderOffset(column);
                labelCellRect = new Rect(column.HeaderRect.x + animatedOffset, rowTop, column.Width, rowHeight);
                return true;
            }

            return false;
        }

        private static int GetDisplayWorkGiverCount(WorkTypeDef workType)
        {
            int syncVersion = WorkGiverReassignmentManager.CurrentSyncVersion;
            if (_cachedSyncVersion != syncVersion)
            {
                WorkGiverCountCache.Clear();
                _cachedSyncVersion = syncVersion;
            }

            string key = workType?.defName;
            if (key.NullOrEmpty())
            {
                return 0;
            }

            if (!WorkGiverCountCache.TryGetValue(key, out int count))
            {
                count = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(workType).Count;
                WorkGiverCountCache[key] = count;
            }

            return count;
        }

        private static void DrawOpenEllipsisBadge(Rect rect, bool hovered)
        {
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;
            try
            {
                if (hovered)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.18f);
                    GUI.DrawTexture(rect.ExpandedBy(3f), TexUI.HighlightTex);
                }

                GUI.color = hovered
                    ? Color.white
                    : new Color(0.72f, 0.74f, 0.72f, 0.72f);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                Widgets.Label(new Rect(rect.x - 6f, rect.y - 12f, rect.width + 12f, rect.height + 24f), "...");
            }
            finally
            {
                Text.WordWrap = oldWordWrap;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }

        private static void DrawBackArrowBadge(Rect rect, bool hovered)
        {
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            try
            {
                GUI.color = hovered
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.96f);
                GUIUtility.RotateAroundPivot(180f, rect.center);
                GUI.DrawTexture(rect, TexButton.Reveal);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }
    }
}
