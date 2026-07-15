using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers.Angled;
using RimWorld;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>Resolves the three physical tutorial anchors from live Work-tab geometry.</summary>
    internal static class BWTTutorialGeometry
    {
        internal static List<BWTTutorialAnchor> BuildInitialAnchors(
            Rect inRect,
            IWorkTabLayoutController layout)
        {
            var anchors = new List<BWTTutorialAnchor>(3);
            if (layout?.Rows == null || layout.Columns == null)
            {
                return anchors;
            }

            WorkTabLayoutRow? pawnRow = FindMiddleVisiblePawnRow(inRect, layout);
            WorkTabLayoutColumn? nameColumn = FindNameColumn(layout);
            WorkTabLayoutColumn? workColumn = FindMiddleVisibleWorkColumn(inRect, layout, requireSkills: true) ??
                                                    FindMiddleVisibleWorkColumn(inRect, layout, requireSkills: false);
            TryImprovePriorityPair(inRect, layout, ref pawnRow, ref workColumn);

            if (pawnRow.HasValue && nameColumn.HasValue)
            {
                Rect rowRect = layout.GetScreenRect(pawnRow.Value);
                Rect nameRect = new Rect(
                    nameColumn.Value.HeaderRect.x,
                    rowRect.y,
                    nameColumn.Value.Width,
                    rowRect.height).ContractedBy(2f);
                anchors.Add(new BWTTutorialAnchor(
                    TutorialHubAnchor.PawnName,
                    nameRect,
                    pawnRow.Value.Pawn));
            }

            if (workColumn.HasValue)
            {
                WorkTabLayoutColumn column = workColumn.Value;
                Rect headerAnchorRect = column.HeaderRect.ContractedBy(2f);
                Vector2[] headerOutline = null;
                if ((BetterWorkTabMod.Settings?.enableAngledHeaders ?? false) &&
                    AngledHeaderController.TryGetVisualGeometry(
                        column.Column?.workType,
                        out Rect angledBounds,
                        out Vector2[] angledQuad))
                {
                    headerAnchorRect = angledBounds;
                    headerOutline = angledQuad;
                }

                anchors.Add(new BWTTutorialAnchor(
                    TutorialHubAnchor.WorkHeader,
                    headerAnchorRect,
                    workType: column.Column?.workType,
                    workGiver: column.SubWorkGiver,
                    outlinePoints: headerOutline));
            }

            if (pawnRow.HasValue && workColumn.HasValue)
            {
                WorkTabLayoutRow row = pawnRow.Value;
                WorkTabLayoutColumn column = workColumn.Value;
                Rect rowRect = layout.GetScreenRect(row);
                Rect cellRect = new Rect(column.HeaderRect.x, rowRect.y, column.Width, rowRect.height);
                anchors.Add(new BWTTutorialAnchor(
                    TutorialHubAnchor.PriorityCell,
                    WorkPriorityCellGeometry.GetPriorityBoxRect(cellRect).ExpandedBy(4f),
                    row.Pawn,
                    column.Column?.workType,
                    column.SubWorkGiver));
            }

            return anchors;
        }

        internal static Rect GetVisibleWorkTabBounds(Rect inRect, IWorkTabLayoutController layout)
        {
            if (layout?.Columns == null || layout.Columns.Count == 0)
            {
                return inRect;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                Rect header = layout.Columns[i].HeaderRect;
                if (!IntersectsHorizontally(header, inRect))
                {
                    continue;
                }

                minX = Mathf.Min(minX, header.xMin);
                maxX = Mathf.Max(maxX, header.xMax);
                minY = Mathf.Min(minY, header.yMin);
                maxY = Mathf.Max(maxY, header.yMax);
            }

            for (int i = 0; i < layout.Rows.Count; i++)
            {
                Rect row = layout.GetScreenRect(layout.Rows[i]);
                if (!IntersectsVertically(row, inRect))
                {
                    continue;
                }

                minY = Mathf.Min(minY, row.yMin);
                maxY = Mathf.Max(maxY, row.yMax);
            }

            return minX <= maxX && minY <= maxY
                ? Rect.MinMaxRect(minX, minY, maxX, maxY)
                : inRect;
        }

        private static void TryImprovePriorityPair(
            Rect inRect,
            IWorkTabLayoutController layout,
            ref WorkTabLayoutRow? pawnRow,
            ref WorkTabLayoutColumn? workColumn)
        {
            float rowTarget = pawnRow.HasValue
                ? layout.GetScreenRect(pawnRow.Value).center.y
                : inRect.center.y;
            float columnTarget = workColumn.HasValue
                ? workColumn.Value.HeaderRect.center.x
                : inRect.center.x;
            WorkTabLayoutRow? bestRow = null;
            WorkTabLayoutColumn? bestColumn = null;
            float bestScore = float.MaxValue;
            for (int c = 0; c < layout.Columns.Count; c++)
            {
                WorkTabLayoutColumn column = layout.Columns[c];
                WorkTypeDef workType = column.Column?.workType;
                Rect header = column.HeaderRect;
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    workType == null ||
                    !IntersectsHorizontally(header, inRect))
                {
                    continue;
                }

                float skillPenalty = workType.relevantSkills != null && workType.relevantSkills.Count > 0 ? 0f : 10000f;
                for (int r = 0; r < layout.Rows.Count; r++)
                {
                    WorkTabLayoutRow row = layout.Rows[r];
                    Rect rowRect = layout.GetScreenRect(row);
                    if (row.Pawn == null ||
                        !IntersectsVertically(rowRect, inRect) ||
                        !CanUsePriorityExample(row.Pawn, workType))
                    {
                        continue;
                    }

                    float score = skillPenalty +
                                  Mathf.Abs(rowRect.center.y - rowTarget) +
                                  Mathf.Abs(header.center.x - columnTarget);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestRow = row;
                        bestColumn = column;
                    }
                }
            }

            if (bestRow.HasValue && bestColumn.HasValue)
            {
                pawnRow = bestRow;
                workColumn = bestColumn;
            }
        }

        private static WorkTabLayoutRow? FindMiddleVisiblePawnRow(
            Rect inRect,
            IWorkTabLayoutController layout)
        {
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < layout.Rows.Count; i++)
            {
                WorkTabLayoutRow row = layout.Rows[i];
                Rect rect = layout.GetScreenRect(row);
                if (row.Pawn != null && IntersectsVertically(rect, inRect))
                {
                    minY = Mathf.Min(minY, rect.yMin);
                    maxY = Mathf.Max(maxY, rect.yMax);
                }
            }

            float targetY = minY <= maxY ? (minY + maxY) * 0.5f : inRect.center.y;
            WorkTabLayoutRow? best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < layout.Rows.Count; i++)
            {
                WorkTabLayoutRow row = layout.Rows[i];
                Rect rect = layout.GetScreenRect(row);
                if (row.Pawn == null || !IntersectsVertically(rect, inRect))
                {
                    continue;
                }

                float distance = Mathf.Abs(rect.center.y - targetY);
                if (distance < bestDistance)
                {
                    best = row;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static WorkTabLayoutColumn? FindNameColumn(IWorkTabLayoutController layout)
        {
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    return column;
                }
            }

            return null;
        }

        private static WorkTabLayoutColumn? FindMiddleVisibleWorkColumn(
            Rect inRect,
            IWorkTabLayoutController layout,
            bool requireSkills)
        {
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                Rect rect = column.HeaderRect;
                if (column.Column?.Worker is PawnColumnWorker_WorkPriority && IntersectsHorizontally(rect, inRect))
                {
                    minX = Mathf.Min(minX, rect.xMin);
                    maxX = Mathf.Max(maxX, rect.xMax);
                }
            }

            float targetX = minX <= maxX ? (minX + maxX) * 0.5f : inRect.center.x;
            WorkTabLayoutColumn? best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                WorkTypeDef workType = column.Column?.workType;
                Rect rect = column.HeaderRect;
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority) ||
                    workType == null ||
                    !IntersectsHorizontally(rect, inRect) ||
                    (requireSkills && (workType.relevantSkills == null || workType.relevantSkills.Count == 0)))
                {
                    continue;
                }

                float distance = Mathf.Abs(rect.center.x - targetX);
                if (distance < bestDistance)
                {
                    best = column;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static bool CanUsePriorityExample(Pawn pawn, WorkTypeDef workType)
        {
            return pawn != null &&
                   !pawn.Dead &&
                   pawn.workSettings != null &&
                   pawn.workSettings.EverWork &&
                   workType != null &&
                   !pawn.WorkTypeIsDisabled(workType);
        }

        private static bool IntersectsHorizontally(Rect a, Rect b)
        {
            return a.xMax > b.xMin && a.xMin < b.xMax;
        }

        private static bool IntersectsVertically(Rect a, Rect b)
        {
            return a.yMax > b.yMin && a.yMin < b.yMax;
        }
    }
}
