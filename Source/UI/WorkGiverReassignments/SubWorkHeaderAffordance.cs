using System.Collections.Generic;
using Better_Work_Tab.DragDrop;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers.Angled;
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
        private const float AffordanceMinWidth = 16f;
        internal const float AffordanceHeight = 11f;
        internal const float AffordanceBottomInset = 2f;
        private const float AffordanceInset = 3f;
        private const float AngledAffordanceRightOffset = 9f;
        private const float BackBadgeSize = 18f;

        private static readonly Dictionary<string, int> WorkGiverCountCache = new Dictionary<string, int>();
        private static readonly Dictionary<PawnColumnDef, Rect> VanillaOpenBadgeRects = new Dictionary<PawnColumnDef, Rect>();
        private static int _cachedSyncVersion = -1;
        private static int _vanillaBadgeRectsFrame = -1;
        private static string _openBadgeTooltip;
        private static string _openBadgeTooltipLanguage;

        internal static string DebugForcedHoveredWorkTypeDefName;
        internal static bool DebugForceBackButtonHover;

        internal static bool ShouldDrawOpenBadge(PawnColumnDef column)
        {
            return SubWorkDrilldownInput.IsEnabled &&
                   (BetterWorkTabMod.Settings?.showSubWorkHeaderBadge ?? true) &&
                   !SubWorkDrilldownState.IsActive &&
                   !SubWorkDrilldownState.IsDrawingExpandBesideChild &&
                   column?.workType != null &&
                   column.Worker is PawnColumnWorker_WorkPriority &&
                   GetDisplayWorkGiverCount(column.workType) > 1;
        }

        internal static Rect GetOpenBadgeRect(Rect headerRect, bool clearVanillaStem)
        {
            float width = Mathf.Max(AffordanceMinWidth, headerRect.width - (AffordanceInset * 2f));
            float xOffset = (BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders)
                ? AngledAffordanceRightOffset
                : 0f;

            return new Rect(
                Mathf.Round(headerRect.center.x - (width / 2f) + xOffset),
                Mathf.Round(headerRect.yMax - AffordanceHeight - AffordanceBottomInset),
                width,
                AffordanceHeight);
        }

        internal static void DrawOpenBadge(Rect headerRect, PawnColumnDef column, bool clearVanillaStem)
        {
            if (!ShouldDrawOpenBadge(column))
            {
                return;
            }

            Rect badgeRect = GetOpenBadgeRect(headerRect, clearVanillaStem);
            bool hovered = IsOpenBadgeHovered(badgeRect, column);
            DrawAngledOpenAffordance(badgeRect, hovered);
            RegisterHoveredBadgeInteraction(badgeRect, hovered);
        }

        internal static void DrawOpenBadge(Rect headerRect, Rect textRect, PawnColumnDef column)
        {
            if (!ShouldDrawOpenBadge(column))
            {
                return;
            }

            Rect badgeRect = GetVanillaOpenBadgeRect(headerRect, textRect);
            RememberVanillaOpenBadgeRect(column, badgeRect);
            bool hovered = IsOpenBadgeHovered(badgeRect, column);
            DrawVanillaOpenAffordance(badgeRect, hovered);
            RegisterHoveredBadgeInteraction(badgeRect, hovered);
        }

        private static void RegisterHoveredBadgeInteraction(Rect badgeRect, bool hovered)
        {
            if (!hovered)
            {
                return;
            }

            TooltipHandler.TipRegion(badgeRect, GetOpenBadgeTooltip());
            MouseoverSounds.DoRegion(badgeRect);
        }

        private static string GetOpenBadgeTooltip()
        {
            string language = LanguageDatabase.activeLanguage?.folderName ?? string.Empty;
            if (_openBadgeTooltip == null || _openBadgeTooltipLanguage != language)
            {
                _openBadgeTooltip = "BWT_SubWork_OpenSpecificJobs".Translate()
                    .ToString()
                    .Colorize(Color.gray);
                _openBadgeTooltipLanguage = language;
            }

            return _openBadgeTooltip;
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
                if (column.IsExpandBesideChild)
                {
                    continue;
                }

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
            float width = Mathf.Max(AffordanceMinWidth, headerRect.width - (AffordanceInset * 2f));
            return new Rect(
                Mathf.Round(headerRect.center.x - (width / 2f)),
                Mathf.Round(headerRect.yMax - AffordanceHeight - AffordanceBottomInset),
                width,
                AffordanceHeight);
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
                TimePriorityScheduleEditor.HeaderPinnedRowsHeight;

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

        private static void DrawAngledOpenAffordance(Rect rect, bool hovered)
        {
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            try
            {
                if (hovered)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.16f);
                    GUI.DrawTexture(rect.ExpandedBy(2f), TexUI.HighlightTex);
                }

                GUI.color = hovered
                    ? Color.white
                    : new Color(0.82f, 0.84f, 0.82f, 0.82f);

                float rotation = BetterWorkTabMod.Settings?.angledHeaderRotation ?? -60f;
                // Header rectangles are local to the Work-window GUI group, while GUI.matrix
                // rotates in root GUI space. Using rect.center directly rotates around a point
                // hundreds of pixels above the actual header and sends the affordance onto the
                // map. Match AngledLabelDrawer by converting the pivot to root coordinates.
                GUI.matrix = Matrix4x4.identity;
                Vector2 rootPivot = GUIClipUtility.Unclip(rect.center);
                GUI.matrix = AngledLabelDrawer.GetTransformMatrix(
                    oldMatrix,
                    rootPivot,
                    rotation,
                    Vector2.one);
                DrawAffordanceRails(rect);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }

        private static void DrawVanillaOpenAffordance(Rect rect, bool hovered)
        {
            Color oldColor = GUI.color;
            try
            {
                if (hovered)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.14f);
                    GUI.DrawTexture(rect.ExpandedBy(2f), TexUI.HighlightTex);
                }

                GUI.color = hovered
                    ? Color.white
                    : new Color(0.82f, 0.84f, 0.82f, 0.82f);
                DrawAffordanceRails(rect);
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static void DrawAffordanceRails(Rect rect)
        {
            float railWidth = Mathf.Max(10f, rect.width - 4f);
            float railX = rect.center.x - (railWidth / 2f);
            GUI.DrawTexture(new Rect(railX, rect.y + 2f, railWidth, 2f), BaseContent.WhiteTex);
            GUI.DrawTexture(
                new Rect(railX + (railWidth * 0.24f), rect.y + 7f, railWidth * 0.52f, 2f),
                BaseContent.WhiteTex);
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
                GUI.DrawTexture(rect, RimWorld.TexButton.CloseXSmall);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }
    }
}
