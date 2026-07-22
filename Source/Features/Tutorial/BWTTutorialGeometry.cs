using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.WorkGiverReassignments;
using RimWorld;
using Spine.UI.Tutorial;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Tutorial
{
    /// <summary>Resolves the three physical tutorial anchors from live Work-tab geometry.</summary>
    internal static class BWTTutorialGeometry
    {
        private const float AngledHeaderOutlineClearance = 4f;
        private const float VanillaHeaderOutlineClearance = 4f;

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
            WorkTabLayoutColumn? headerColumn = FindMiddleVisibleWorkColumn(inRect, layout, requireSkills: true) ??
                                                      FindMiddleVisibleWorkColumn(inRect, layout, requireSkills: false);
            WorkTabLayoutColumn? priorityColumn = headerColumn;
            TryImprovePriorityPair(inRect, layout, ref pawnRow, ref priorityColumn);
            if (headerColumn.HasValue &&
                priorityColumn.HasValue &&
                IsSameColumn(headerColumn.Value, priorityColumn.Value))
            {
                WorkTabLayoutRow? alternateRow = pawnRow;
                WorkTabLayoutColumn? alternateColumn = null;
                TryImprovePriorityPair(
                    inRect,
                    layout,
                    ref alternateRow,
                    ref alternateColumn,
                    headerColumn);
                if (alternateRow.HasValue && alternateColumn.HasValue)
                {
                    pawnRow = alternateRow;
                    priorityColumn = alternateColumn;
                }
            }

            if (pawnRow.HasValue && nameColumn.HasValue)
            {
                Rect rowRect = layout.GetScreenRect(pawnRow.Value);
                Rect nameRect = new Rect(
                    nameColumn.Value.HeaderRect.x,
                    rowRect.y,
                    nameColumn.Value.Width,
                    rowRect.height).ContractedBy(2f);
                nameRect.xMax += 6f;
                anchors.Add(new BWTTutorialAnchor(
                    TutorialHubAnchor.PawnName,
                    nameRect,
                    pawnRow.Value.Pawn));
            }

            if (headerColumn.HasValue)
            {
                WorkTabLayoutColumn column = headerColumn.Value;
                anchors.Add(GetRenderedHeaderAnchor(column, layout));
            }

            if (pawnRow.HasValue && priorityColumn.HasValue)
            {
                WorkTabLayoutRow row = pawnRow.Value;
                WorkTabLayoutColumn column = priorityColumn.Value;
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

        internal static bool TryResolveAnchorAt(
            Rect inRect,
            IWorkTabLayoutController layout,
            Vector2 pointer,
            out BWTTutorialAnchor anchor)
        {
            anchor = default(BWTTutorialAnchor);
            if (layout?.Rows == null || layout.Columns == null || !inRect.Contains(pointer))
            {
                return false;
            }

            if (layout.TryGetRowAt(pointer, out WorkTabLayoutRow row) && row.Pawn != null)
            {
                if (layout.TryGetBodyColumnAt(pointer, out WorkTabLayoutColumn bodyColumn) &&
                    bodyColumn.Column?.Worker is PawnColumnWorker_WorkPriority)
                {
                    Rect rowRect = layout.GetScreenRect(row);
                    Rect cell = new Rect(bodyColumn.HeaderRect.x, rowRect.y, bodyColumn.Width, rowRect.height);
                    Rect priority = WorkPriorityCellGeometry.GetPriorityBoxRect(cell).ExpandedBy(4f);
                    if (priority.Contains(pointer))
                    {
                        anchor = new BWTTutorialAnchor(
                            TutorialHubAnchor.PriorityCell,
                            priority,
                            row.Pawn,
                            bodyColumn.Column?.workType,
                            bodyColumn.SubWorkGiver);
                        return true;
                    }
                }

                WorkTabLayoutColumn? nameColumn = FindNameColumn(layout);
                if (nameColumn.HasValue)
                {
                    Rect rowRect = layout.GetScreenRect(row);
                    Rect name = new Rect(nameColumn.Value.HeaderRect.x, rowRect.y,
                        nameColumn.Value.Width + 6f, rowRect.height).ContractedBy(2f);
                    if (name.Contains(pointer))
                    {
                        anchor = new BWTTutorialAnchor(TutorialHubAnchor.PawnName, name, row.Pawn);
                        return true;
                    }
                }
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                BWTTutorialAnchor header = GetRenderedHeaderAnchor(column, layout);
                if (!header.Contains(pointer))
                {
                    continue;
                }

                anchor = header;
                return true;
            }

            return false;
        }

        private static BWTTutorialAnchor GetRenderedHeaderAnchor(
            WorkTabLayoutColumn column,
            IWorkTabLayoutController layout)
        {
            if ((BetterWorkTabMod.Settings?.enableAngledHeaders ?? false) &&
                TryGetAngledHeaderQuad(column, layout, out Vector2[] angledQuad))
            {
                Vector2 center = Vector2.zero;
                for (int i = 0; i < angledQuad.Length; i++)
                {
                    center += angledQuad[i];
                }
                center /= angledQuad.Length;

                var outline = new Vector2[angledQuad.Length];
                float xMin = float.MaxValue;
                float yMin = float.MaxValue;
                float xMax = float.MinValue;
                float yMax = float.MinValue;
                for (int i = 0; i < angledQuad.Length; i++)
                {
                    Vector2 direction = angledQuad[i] - center;
                    outline[i] = angledQuad[i] +
                        (direction.sqrMagnitude > 0.001f
                            ? direction.normalized * AngledHeaderOutlineClearance
                            : Vector2.zero);
                    xMin = Mathf.Min(xMin, outline[i].x);
                    yMin = Mathf.Min(yMin, outline[i].y);
                    xMax = Mathf.Max(xMax, outline[i].x);
                    yMax = Mathf.Max(yMax, outline[i].y);
                }

                return new BWTTutorialAnchor(
                    TutorialHubAnchor.WorkHeader,
                    Rect.MinMaxRect(xMin, yMin, xMax, yMax),
                    workType: column.Column?.workType,
                    workGiver: column.SubWorkGiver,
                    outlinePoints: outline);
            }

            Rect vanillaBounds = HeaderDrawingCoordinator.GetVanillaSolver()?.GetBounds(column.Column) ?? Rect.zero;
            if (vanillaBounds.width > 0.5f && vanillaBounds.height > 0.5f)
            {
                // The solver bounds describe the rendered word rather than the
                // narrow priority column. Extra clearance keeps the gold line
                // outside the glyphs so the label remains fully readable.
                vanillaBounds = vanillaBounds.ExpandedBy(VanillaHeaderOutlineClearance);
            }
            else
            {
                vanillaBounds = column.HeaderRect.ContractedBy(2f);
            }

            return new BWTTutorialAnchor(
                TutorialHubAnchor.WorkHeader,
                vanillaBounds,
                workType: column.Column?.workType,
                workGiver: column.SubWorkGiver);
        }

        private static bool TryGetAngledHeaderQuad(
            WorkTabLayoutColumn column,
            IWorkTabLayoutController layout,
            out Vector2[] angledQuad)
        {
            WorkTypeDef workType = column.SubWorkParent ?? column.Column?.workType;
            if (AngledHeaderController.TryGetVisualGeometry(workType, out _, out angledQuad))
            {
                return true;
            }

            // Hosted Fluffy-compatible columns are drawn by BWT's Work-tab host
            // and do not populate the vanilla angled-header geometry cache.
            // Recreate the same draw rectangle used by that renderer so the
            // tutorial follows the visible label instead of its narrow column.
            if (workType == null || layout?.Table == null)
            {
                angledQuad = null;
                return false;
            }

            string label = column.SubWorkGiver != null
                ? WorkGiverDisplayNameService.HeaderLabel(
                    column.SubWorkGiver,
                    WorkGiverHeaderLabelStyle.Standard)
                : WorkTypeDisplayNameService.HeaderLabel(workType);
            AngledHeaderCache.CachedTextMetrics metrics =
                AngledHeaderCache.GetLabelTextMetrics(label);
            Rect drawRect = MainTabWindow_BetterWork.GetHostedAngledHeaderDrawRect(
                column,
                column.HeaderRect,
                metrics.Size,
                metrics.IsCJKVertical,
                layout.Table);
            float cos = metrics.IsCJKVertical ? 1f : AngledLabelDrawer.CurrentRotCos;
            float sin = metrics.IsCJKVertical ? 0f : AngledLabelDrawer.CurrentRotSin;
            angledQuad = AngledHeaderCache.CalculateRotatedQuad(drawRect, cos, sin);
            return angledQuad != null && angledQuad.Length >= 3;
        }

        private static void TryImprovePriorityPair(
            Rect inRect,
            IWorkTabLayoutController layout,
            ref WorkTabLayoutRow? pawnRow,
            ref WorkTabLayoutColumn? workColumn,
            WorkTabLayoutColumn? excludedColumn = null)
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
                    (excludedColumn.HasValue && IsSameColumn(column, excludedColumn.Value)) ||
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

        private static bool IsSameColumn(WorkTabLayoutColumn left, WorkTabLayoutColumn right)
        {
            return ReferenceEquals(left.Column, right.Column) &&
                   left.SubWorkGiver == right.SubWorkGiver;
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
