using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using Spine.UI.WidgetExtensions;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Resolves and draws Rule Builder 2's work-tab target overlays.
    /// </summary>
    internal static class RuleBuilder2WorkTabOverlay
    {
        internal static WorkTypeDef ResolveWorkType(WorkTabLayoutColumn column)
        {
            ResolveTarget(column, out WorkTypeDef workType, out _);
            return workType;
        }

        internal static void ResolveTarget(
            WorkTabLayoutColumn column,
            out WorkTypeDef workType,
            out WorkGiverDef workGiver)
        {
            if (column.IsExpandBesideChild)
            {
                workGiver = column.SubWorkGiver;
                workType = workGiver?.workType ?? column.SubWorkParent ?? column.Column?.workType;
                return;
            }

            if (SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    column,
                    out var resolvedWorkGiver,
                    out var parentWorkType,
                    out _))
            {
                workGiver = resolvedWorkGiver.def;
                workType = workGiver?.workType ?? parentWorkType;
                return;
            }

            workType = column.Column?.workType;
            workGiver = null;
        }

        internal static bool TryGetTargetHeaderBounds(
            IWorkTabLayoutController layout,
            WorkTypeDef workType,
            WorkGiverDef workGiver,
            out Rect bounds)
        {
            bounds = Rect.zero;
            if (layout?.Columns == null || workType == null)
            {
                return false;
            }

            for (int i = 0; i < layout.Columns.Count; i++)
            {
                WorkTabLayoutColumn column = layout.Columns[i];
                if (!(column.Column?.Worker is PawnColumnWorker_WorkPriority))
                {
                    continue;
                }

                ResolveTarget(column, out WorkTypeDef columnWorkType, out WorkGiverDef columnWorkGiver);
                if (columnWorkType != workType ||
                    (columnWorkGiver?.defName ?? "") != (workGiver?.defName ?? ""))
                {
                    continue;
                }

                return TryGetHeaderHighlight(
                    column,
                    WorkGridInteractionGeometry.GetAnimatedHeaderRect(column),
                    layout.Table,
                    out var headerHighlight) &&
                    IsUsableRect(bounds = headerHighlight.Bounds);
            }

            return false;
        }

        internal static void DrawColumnHighlight(
            IWorkTabLayoutController layout,
            WorkTabLayoutColumn column,
            Rect headerRect,
            float totalHeight,
            PawnTable table,
            bool drawBody = true)
        {
            ResolveTarget(column, out WorkTypeDef workType, out WorkGiverDef workGiver);
            if (RuleBuilderGateway.TryGetRuleBuilder2SelectionTransitionOffset(
                    workType,
                    workGiver,
                    out Vector2 transitionOffset))
            {
                headerRect.position += transitionOffset;
            }

            Rect bodyRect = GetColumnBodyHighlightRect(layout, column, totalHeight);
            bool hasHeaderHighlight = TryGetHeaderHighlight(column, headerRect, table, out var headerHighlight);
            if (hasHeaderHighlight && headerHighlight.IsAngled)
            {
                Vector2[] quad = headerHighlight.VisibleQuad ?? headerHighlight.Quad;
                if (quad != null && quad.Length >= 4)
                {
                    if (drawBody && IsUsableRect(bodyRect))
                    {
                        Widgets.DrawBoxSolid(bodyRect, new Color(1f, 0.82f, 0.18f, 0.12f));
                    }

                    Color outline = new Color(1f, 0.82f, 0.18f, 0.62f);
                    Vector2 bodyBottomLeft = new Vector2(bodyRect.xMin, bodyRect.yMax);
                    Vector2 bodyTopLeft = new Vector2(bodyRect.xMin, bodyRect.yMin);
                    Vector2 bodyTopRight = new Vector2(bodyRect.xMax, bodyRect.yMin);
                    Vector2 bodyBottomRight = new Vector2(bodyRect.xMax, bodyRect.yMax);
                    Vector2 columnJoinLeft = bodyTopLeft;
                    Vector2 columnJoinRight = bodyTopRight;
                    float leftCapY = (quad[0].y + quad[3].y) / 2f;
                    float rightCapY = (quad[1].y + quad[2].y) / 2f;
                    Vector2 headerTopLeft = leftCapY <= rightCapY ? quad[0] : quad[1];
                    Vector2 headerTopRight = leftCapY <= rightCapY ? quad[3] : quad[2];
                    if (headerTopRight.x < headerTopLeft.x)
                    {
                        Vector2 swap = headerTopLeft;
                        headerTopLeft = headerTopRight;
                        headerTopRight = swap;
                    }

                    Vector2 headerRun = quad[1] - quad[0];
                    if (headerRun.x < 0f)
                    {
                        headerRun *= -1f;
                    }

                    if (Mathf.Abs(headerRun.x) > 0.001f)
                    {
                        float leftT = (bodyRect.xMin - headerTopLeft.x) / headerRun.x;
                        float rightT = (bodyRect.xMax - headerTopRight.x) / headerRun.x;
                        columnJoinLeft = new Vector2(
                            bodyRect.xMin,
                            Mathf.Min(bodyRect.yMin, headerTopLeft.y + (headerRun.y * leftT)));
                        columnJoinRight = new Vector2(
                            bodyRect.xMax,
                            Mathf.Min(bodyRect.yMin, headerTopRight.y + (headerRun.y * rightT)));
                    }

                    const float rightConnectorDrop = 4f;
                    Vector2 topEdge = headerTopRight - headerTopLeft;
                    Vector2 connectorDirection = headerRun;
                    if ((bodyRect.xMax - headerTopRight.x) * connectorDirection.x < 0f)
                    {
                        connectorDirection *= -1f;
                    }

                    float intersectionDenominator = (topEdge.x * connectorDirection.y) -
                        (topEdge.y * connectorDirection.x);
                    if (Mathf.Abs(intersectionDenominator) > 0.001f &&
                        Mathf.Abs(connectorDirection.x) > 0.001f)
                    {
                        Vector2 shiftedConnectorOrigin = headerTopRight + new Vector2(0f, rightConnectorDrop);
                        Vector2 originDelta = shiftedConnectorOrigin - headerTopLeft;
                        float topEdgeT = ((originDelta.x * connectorDirection.y) -
                            (originDelta.y * connectorDirection.x)) / intersectionDenominator;
                        if (topEdgeT >= 1f && topEdgeT <= 1.5f)
                        {
                            headerTopRight = headerTopLeft + (topEdge * topEdgeT);
                            float connectorT = (bodyRect.xMax - headerTopRight.x) / connectorDirection.x;
                            if (connectorT > 0f)
                            {
                                Vector2 loweredColumnJoinRight = headerTopRight + (connectorDirection * connectorT);
                                if (loweredColumnJoinRight.y > bodyRect.yMin &&
                                    loweredColumnJoinRight.y < bodyRect.yMax)
                                {
                                    columnJoinRight = loweredColumnJoinRight;
                                }
                            }
                        }
                    }

                    ConnectedOutlineDrawer.DrawClosed(
                        new[]
                        {
                            bodyBottomLeft,
                            columnJoinLeft,
                            headerTopLeft,
                            headerTopRight,
                            columnJoinRight,
                            bodyBottomRight
                        },
                        outline,
                        2f);
                    return;
                }
            }

            if (drawBody)
            {
                DrawHighlightRect(bodyRect);
            }
            if (hasHeaderHighlight)
            {
                DrawHighlightRect(headerHighlight.Bounds);
            }
        }

        internal static bool TryGetHeaderHighlight(
            WorkTabLayoutColumn column,
            Rect headerRect,
            PawnTable table,
            out RuleBuilder2HeaderHighlight headerHighlight)
        {
            headerHighlight = default;
            PawnColumnDef columnDef = column.Column;
            if (columnDef?.workType == null)
            {
                return false;
            }

            if (AreAngledHeadersEnabled())
            {
                float rotation = AngledLabelDrawer.CurrentRotation;
                float cos = Mathf.Cos(rotation * Mathf.Deg2Rad);
                float sin = Mathf.Sin(rotation * Mathf.Deg2Rad);
                if (AngledHeaderCache.TryGetLayout(
                        headerRect,
                        columnDef.workType,
                        cos,
                        sin,
                        AngledLabelDrawer.STEM_BOTTOM_GAP,
                        AngledLabelDrawer.EffectiveHorizontalOffset,
                        out var cached) &&
                    IsUsableRect(cached.Bounds))
                {
                    headerHighlight = RuleBuilder2HeaderHighlight.Angled(
                        cached.Bounds,
                        cached.Quad,
                        cached.Quad);
                    return true;
                }

                return false;
            }

            HeaderDrawingCoordinator.EnsureLayoutSolved(table);
            Rect vanillaBounds = HeaderDrawingCoordinator.GetVanillaSolver(table)?.GetBounds(columnDef) ?? Rect.zero;
            if (!IsUsableRect(vanillaBounds))
            {
                return false;
            }

            vanillaBounds.x += headerRect.x - column.HeaderRect.x;

            headerHighlight = RuleBuilder2HeaderHighlight.Rectangular(vanillaBounds);
            return true;
        }

        private static Rect GetColumnBodyHighlightRect(
            IWorkTabLayoutController layout,
            WorkTabLayoutColumn column,
            float totalHeight)
        {
            WorkGridAnimatedColumnGeometry geometry =
                WorkGridInteractionGeometry.GetAnimatedColumn(column);
            return new Rect(
                geometry.BodyScreenX,
                layout.TableOrigin.y + layout.HeaderHeight,
                geometry.Width,
                totalHeight);
        }

        private static void DrawHighlightRect(Rect rect)
        {
            if (!IsUsableRect(rect))
            {
                return;
            }

            Widgets.DrawBoxSolid(rect, new Color(1f, 0.82f, 0.18f, 0.12f));
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.18f, 0.55f);
            Widgets.DrawBox(rect, 2);
            GUI.color = previousColor;
        }

        private static bool AreAngledHeadersEnabled()
        {
            return BetterWorkTabMod.Settings?.enableAngledHeaders ?? DefaultSettings.enableAngledHeaders;
        }

        private static bool IsUsableRect(Rect rect)
        {
            return rect.width > 0f && rect.height > 0f;
        }

        internal readonly struct RuleBuilder2HeaderHighlight
        {
            private RuleBuilder2HeaderHighlight(
                Rect bounds,
                Vector2[] quad,
                Vector2[] visibleQuad,
                bool isAngled)
            {
                Bounds = bounds;
                Quad = quad;
                VisibleQuad = visibleQuad;
                IsAngled = isAngled;
            }

            internal Rect Bounds { get; }
            internal Vector2[] Quad { get; }
            internal Vector2[] VisibleQuad { get; }
            internal bool IsAngled { get; }

            internal static RuleBuilder2HeaderHighlight Angled(
                Rect bounds,
                Vector2[] quad,
                Vector2[] visibleQuad)
            {
                return new RuleBuilder2HeaderHighlight(bounds, quad, visibleQuad, true);
            }

            internal static RuleBuilder2HeaderHighlight Rectangular(Rect bounds)
            {
                return new RuleBuilder2HeaderHighlight(bounds, null, null, false);
            }

            internal bool Contains(Vector2 point)
            {
                return IsAngled && Quad != null
                    ? AngledHeaderCache.IsMouseOver(Quad, point)
                    : Bounds.Contains(point);
            }
        }
    }
}
