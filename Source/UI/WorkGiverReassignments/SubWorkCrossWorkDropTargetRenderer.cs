using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using UnityEngine;
using Verse;

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

        internal static bool IsEnabled =>
            BetterWorkTabMod.Settings?.enableSubWorkCrossWorkDragDrop ??
            DefaultSettings.enableSubWorkCrossWorkDragDrop;

        internal static bool IsPointerBeyondSubWorkStrip(
            IWorkTabLayoutController layout,
            Vector2 mousePosition,
            WorkTypeDef sourceWorkType)
        {
            if (!TryGetSubWorkStripRect(layout, sourceWorkType, out Rect stripRect))
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
                !IsPointerBeyondSubWorkStrip(layout, mousePosition, activeWorkType))
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (!TryGetDropWorkType(column, out WorkTypeDef workType))
                {
                    continue;
                }

                Rect candidateRect = GetTargetRect(layout, column);
                if (!GetTargetHitRect(layout, column, candidateRect).Contains(mousePosition))
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
                if (TryGetDropWorkType(column, out WorkTypeDef workType) &&
                    workType == targetWorkType)
                {
                    targetRect = GetTargetRect(layout, column);
                    return targetRect.width > 1f && targetRect.height > 1f;
                }
            }

            return false;
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

        internal static void ResetForWindowClose()
        {
            _settleWorkGiver = null;
            _settleTargetWorkType = null;
            _settleSourceRect = Rect.zero;
            _settleTargetRect = Rect.zero;
            _settleStartedAt = 0f;
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

        private static bool TryGetSubWorkStripRect(
            IWorkTabLayoutController layout,
            WorkTypeDef sourceWorkType,
            out Rect stripRect)
        {
            stripRect = Rect.zero;
            if (sourceWorkType == null || layout?.Columns == null)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                var column = layout.Columns[i];
                if (!SubWorkDrilldownState.TryGetWorkGiverForColumn(
                        column,
                        out _,
                        out WorkTypeDef parentWorkType,
                        out _) ||
                    parentWorkType != sourceWorkType)
                {
                    continue;
                }

                Rect headerRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                stripRect = found ? Union(stripRect, headerRect) : headerRect;
                found = true;
            }

            return found;
        }

        private static bool TryGetDropWorkType(
            WorkTabLayoutColumn column,
            out WorkTypeDef workType)
        {
            workType = column.Column?.workType;
            if (workType == null || column.IsExpandBesideChild)
            {
                return false;
            }

            if (column.Column.Worker is PawnColumnWorker_WorkPriority)
            {
                return true;
            }

            return FluffyWorkTabGateway.IsFluffyColumn(column.Column) &&
                !FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column);
        }

        private static bool TryGetDropRowRect(IWorkTabLayoutController layout, out Rect rowRect)
        {
            rowRect = Rect.zero;
            if (layout?.Table == null)
            {
                return false;
            }

            Rect reservedBand = WorkGridLayoutMetrics.GetSubWorkBandRect(layout);
            if (reservedBand.height <= 2f)
            {
                return false;
            }

            rowRect = reservedBand;
            return true;
        }

        private static Rect GetTargetRect(IWorkTabLayoutController layout, WorkTabLayoutColumn column)
        {
            if (!TryGetDropRowRect(layout, out Rect rowRect))
            {
                const float headerTargetHeight = 24f;
                Rect headerRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                return new Rect(
                    headerRect.xMin + 2f,
                    Mathf.Max(headerRect.yMin, headerRect.yMax - headerTargetHeight),
                    Mathf.Max(1f, column.Width - 4f),
                    Mathf.Min(headerTargetHeight - 3f, headerRect.height));
            }

            Rect bodyRect = WorkGridInteractionGeometry.GetAnimatedBodyScreenRect(
                column,
                rowRect);
            return new Rect(
                bodyRect.xMin + 2f,
                rowRect.yMin + 3f,
                Mathf.Max(1f, column.Width - 4f),
                Mathf.Max(1f, rowRect.height - 6f));
        }

        private static Rect GetTargetHitRect(
            IWorkTabLayoutController layout,
            WorkTabLayoutColumn column,
            Rect visualTargetRect)
        {
            Rect headerRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
            float xMin = Mathf.Min(headerRect.xMin, visualTargetRect.xMin) - 2f;
            float xMax = Mathf.Max(headerRect.xMax, visualTargetRect.xMax) + 2f;
            float yMin = headerRect.yMin;
            float yMax = Mathf.Max(headerRect.yMax, visualTargetRect.yMax);

            if (layout?.TableOrigin.y > yMin)
            {
                yMin = layout.TableOrigin.y;
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
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

    }
}
