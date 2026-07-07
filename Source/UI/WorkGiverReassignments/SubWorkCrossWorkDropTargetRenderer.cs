using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    internal static class SubWorkCrossWorkDropTargetRenderer
    {
        private const float SettleSeconds = 0.34f;
        private static WorkGiverDef _settleWorkGiver;
        private static WorkTypeDef _settleTargetWorkType;
        private static Rect _settleSourceRect;
        private static Rect _settleTargetRect;
        private static float _settleStartedAt;
        private static WorkTypeDef _liveActiveWorkType;
        private static WorkTypeDef _liveHoveredWorkType;
        private static bool _liveHoveredTargetValid;
        internal static bool DebugForceDrawTargets;
        internal static WorkTypeDef DebugForcedActiveWorkType;
        internal static WorkTypeDef DebugForcedHoveredWorkType;
        internal static bool DebugForcedHoveredTargetValid;

        internal static bool IsEnabled =>
            BetterWorkTabMod.Settings?.enableSubWorkCrossWorkDragDrop ??
            DefaultSettings.enableSubWorkCrossWorkDragDrop;

        internal static bool IsPointerBeyondSubWorkStrip(IWorkTabLayoutController layout, Vector2 mousePosition)
        {
            if (!TryGetSubWorkStripRect(layout, out Rect stripRect))
            {
                return false;
            }

            return !stripRect.ExpandedBy(2f).Contains(mousePosition);
        }

        internal static bool TryGetDropTargetAt(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            WorkTypeDef activeWorkType,
            out WorkTypeDef targetWorkType,
            out Rect targetRect,
            out bool valid)
        {
            targetWorkType = null;
            targetRect = Rect.zero;
            valid = false;

            if (!IsEnabled ||
                layout?.Columns == null ||
                activeWorkType == null ||
                !IsPointerBeyondSubWorkStrip(layout, mousePosition))
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                WorkTypeDef workType = column.Column?.workType;
                if (workType == null || !(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                Rect candidateRect = GetTargetRect(layout, column);
                if (!candidateRect.Contains(mousePosition))
                {
                    continue;
                }

                targetWorkType = workType;
                targetRect = candidateRect;
                valid = workType != activeWorkType;
                return true;
            }

            return false;
        }

        internal static bool TryGetDropTargetRect(
            IWorkTabLayoutController layout,
            WorkTypeDef targetWorkType,
            out Rect targetRect)
        {
            targetRect = Rect.zero;
            if (!IsEnabled || layout?.Columns == null || targetWorkType == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (column.Column?.workType == targetWorkType &&
                    column.Column.Worker is PawnColumnWorker_WorkPriority)
                {
                    targetRect = GetTargetRect(layout, column);
                    return targetRect.width > 1f && targetRect.height > 1f;
                }
            }

            return false;
        }

        internal static void DrawTargets(
            IWorkTabLayoutController layout,
            WorkTypeDef activeWorkType,
            WorkTypeDef hoveredWorkType,
            bool hoveredTargetValid)
        {
            if (!IsEnabled ||
                layout?.Columns == null ||
                activeWorkType == null ||
                !TryGetDropRowRect(layout, out _))
            {
                return;
            }

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                WorkTypeDef workType = column.Column?.workType;
                if (workType == null || !(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                Rect rect = GetTargetRect(layout, column);
                bool sameWork = workType == activeWorkType;
                bool hovered = workType == hoveredWorkType;
                Color fill = sameWork
                    ? new Color(0.25f, 0.25f, 0.25f, 0.34f)
                    : new Color(0.16f, 0.22f, 0.27f, 0.72f);
                Color outline = sameWork
                    ? new Color(1f, 1f, 1f, 0.18f)
                    : new Color(0.72f, 0.86f, 1f, 0.58f);

                if (hovered)
                {
                    fill = hoveredTargetValid
                        ? new Color(0.22f, 0.36f, 0.43f, 0.92f)
                        : new Color(0.34f, 0.24f, 0.2f, 0.68f);
                    outline = hoveredTargetValid
                        ? new Color(0.9f, 0.98f, 1f, 0.95f)
                        : new Color(1f, 0.62f, 0.45f, 0.72f);
                }

                Widgets.DrawBoxSolidWithOutline(rect, fill, outline);
                GUI.color = sameWork
                    ? new Color(1f, 1f, 1f, 0.42f)
                    : new Color(1f, 1f, 1f, 0.94f);
                Widgets.Label(rect.ContractedBy(2f), workType.labelShort?.CapitalizeFirst() ?? workType.LabelCap.ToString());
                GUI.color = oldColor;

                string tooltip = sameWork
                    ? TranslateOrFallback("BWT_SubWork_MoveSpecificJobSameWork", "Already in this Work")
                    : TranslateOrFallback("BWT_SubWork_MoveSpecificJobToWork", "Move specific job to {0}", workType.LabelCap.ToString());
                TooltipHandler.TipRegion(rect, tooltip);
                MouseoverSounds.DoRegion(rect);
            }

            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        internal static void SetLiveTargets(
            WorkTypeDef activeWorkType,
            WorkTypeDef hoveredWorkType,
            bool hoveredTargetValid)
        {
            _liveActiveWorkType = activeWorkType;
            _liveHoveredWorkType = hoveredWorkType;
            _liveHoveredTargetValid = hoveredTargetValid;
        }

        internal static void ClearLiveTargets()
        {
            _liveActiveWorkType = null;
            _liveHoveredWorkType = null;
            _liveHoveredTargetValid = false;
        }

        internal static void DrawDebugTargetsIfNeeded(IWorkTabLayoutController layout)
        {
            if (!IsEnabled)
            {
                ClearLiveTargets();
                return;
            }

            if (DebugForceDrawTargets)
            {
                DrawTargets(
                    layout,
                    DebugForcedActiveWorkType ?? SubWorkDrilldownState.ActiveWorkType,
                    DebugForcedHoveredWorkType,
                    DebugForcedHoveredTargetValid);
                return;
            }

            if (_liveActiveWorkType != null)
            {
                DrawTargets(
                    layout,
                    _liveActiveWorkType,
                    _liveHoveredWorkType,
                    _liveHoveredTargetValid);
            }
        }

        internal static void ClearDebugForcedTargets()
        {
            DebugForceDrawTargets = false;
            DebugForcedActiveWorkType = null;
            DebugForcedHoveredWorkType = null;
            DebugForcedHoveredTargetValid = false;
        }

        internal static void StartSettleAnimation(
            WorkGiverDef workGiverDef,
            WorkTypeDef targetWorkType,
            Rect sourceRect,
            Rect targetRect)
        {
            _settleWorkGiver = workGiverDef;
            _settleTargetWorkType = targetWorkType;
            _settleSourceRect = sourceRect;
            _settleTargetRect = targetRect;
            _settleStartedAt = Time.realtimeSinceStartup;
        }

        internal static void DrawSettleAnimation(IWorkTabLayoutController layout)
        {
            if (_settleWorkGiver == null)
            {
                return;
            }

            if (!IsEnabled)
            {
                _settleWorkGiver = null;
                _settleTargetWorkType = null;
                return;
            }

            if (TryGetDropTargetRect(layout, _settleTargetWorkType, out Rect liveTarget))
            {
                _settleTargetRect = liveTarget;
            }

            float t = Mathf.Clamp01((Time.realtimeSinceStartup - _settleStartedAt) / SettleSeconds);
            float eased = t * t * (3f - 2f * t);
            Rect rect = LerpRect(_settleSourceRect, _settleTargetRect, eased);
            float alpha = Mathf.Sin((1f - t) * Mathf.PI * 0.5f);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            GUI.color = new Color(0.72f, 0.9f, 1f, 0.18f * alpha);
            Widgets.DrawBoxSolid(rect, GUI.color);
            GUI.color = new Color(0.9f, 0.98f, 1f, 0.85f * alpha);
            Widgets.DrawBox(rect);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect.ContractedBy(2f), WorkGiverDisplayNameService.HeaderLabel(_settleWorkGiver));

            GUI.color = oldColor;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;

            if (t >= 1f)
            {
                _settleWorkGiver = null;
                _settleTargetWorkType = null;
            }
        }

        private static bool TryGetSubWorkStripRect(IWorkTabLayoutController layout, out Rect stripRect)
        {
            stripRect = Rect.zero;
            if (!SubWorkDrilldownState.IsActive || layout?.Columns == null)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(column.Column, out _, out _))
                {
                    continue;
                }

                stripRect = found ? Union(stripRect, column.HeaderRect) : column.HeaderRect;
                found = true;
            }

            return found;
        }

        private static bool TryGetDropRowRect(IWorkTabLayoutController layout, out Rect rowRect)
        {
            rowRect = Rect.zero;
            if (layout?.Table == null)
            {
                return false;
            }

            float reservedHeight = Mathf.Max(0f, SubWorkDrilldownState.GlobalRowReservedHeight);
            if (reservedHeight <= 2f)
            {
                return false;
            }

            float rowTop = layout.TableOrigin.y +
                layout.HeaderHeight +
                TimePriorityPlannerPrototype.HeaderPinnedRowsHeight;
            rowRect = new Rect(
                layout.TableOrigin.x,
                rowTop,
                Mathf.Max(layout.Table.Size.x - 16f, 1f),
                reservedHeight);
            return true;
        }

        private static Rect GetTargetRect(IWorkTabLayoutController layout, WorkTabLayoutColumn column)
        {
            if (!TryGetDropRowRect(layout, out Rect rowRect))
            {
                return Rect.zero;
            }

            return new Rect(
                column.HeaderRect.xMin + 2f,
                rowRect.yMin + 3f,
                Mathf.Max(1f, column.Width - 4f),
                Mathf.Max(1f, rowRect.height - 6f));
        }

        private static Rect Union(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin);
            float yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax);
            float yMax = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect LerpRect(Rect from, Rect to, float t)
        {
            return new Rect(
                Mathf.Lerp(from.x, to.x, t),
                Mathf.Lerp(from.y, to.y, t),
                Mathf.Lerp(from.width, to.width, t),
                Mathf.Lerp(from.height, to.height, t));
        }

        private static string TranslateOrFallback(string key, string fallback, params object[] args)
        {
            return key.CanTranslate()
                ? string.Format(key.Translate().ToString(), args)
                : string.Format(fallback, args);
        }
    }
}
