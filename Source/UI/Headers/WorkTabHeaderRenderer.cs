using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.Features.Application;
using Better_Work_Tab.ModSupport.Mods.FluffyWorkTab;
using Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities;
using Better_Work_Tab.PawnOrganizer;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Columns;
using Better_Work_Tab.UI.Headers.Angled;
using Better_Work_Tab.UI.RuleBuilder;
using Better_Work_Tab.UI;
using Better_Work_Tab.UI.Settings;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Invalidation;
using Better_Work_Tab.UI.WorkGrid.Layout;
using Better_Work_Tab.UI.WorkGrid.Projection;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Immutable values from the owning work-tab frame that affect header drawing.
    /// These are deliberately scalar inputs rather than a reference to the window.
    /// </summary>
    internal readonly struct WorkTabHeaderFrame
    {
        internal WorkTabHeaderFrame(
            float windowHeight,
            float bottomReservation,
            float inlineTimePriorityReservedHeight,
            float pinnedRowsHeight,
            float tableViewportWidth,
            float scrollViewFitAllowance)
        {
            WindowHeight = windowHeight;
            BottomReservation = bottomReservation;
            InlineTimePriorityReservedHeight = inlineTimePriorityReservedHeight;
            PinnedRowsHeight = pinnedRowsHeight;
            TableViewportWidth = tableViewportWidth;
            ScrollViewFitAllowance = scrollViewFitAllowance;
        }

        internal float WindowHeight { get; }
        internal float BottomReservation { get; }
        internal float InlineTimePriorityReservedHeight { get; }
        internal float PinnedRowsHeight { get; }
        internal float TableViewportWidth { get; }
        internal float ScrollViewFitAllowance { get; }
    }

    /// <summary>
    /// Owns the work-tab header pass, including hosted Fluffy/Sleek composition.
    /// Rule Builder 2 and sub-work transition overlays remain separate owners.
    /// </summary>
    internal sealed class WorkTabHeaderRenderer
    {
        private readonly Func<WorkTabApplication> _application;
        private const float HorizontalCullBuffer = 64f;
        private const float HostedFirstSubWorkAngledHeaderOffsetX = 5f;

        internal WorkTabHeaderRenderer(Func<WorkTabApplication> application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
        }

        internal void DrawHeaders(
            IWorkTabLayoutController layout,
            PawnTable table,
            in WorkTabHeaderFrame frame)
        {
            float totalHeight = GetVisibleHeaderHighlightHeight(layout, in frame);
            FluffyWorkTabGateway.PrepareHostedDraw(table);
            float viewportLeft = layout.TableOrigin.x;
            float viewportRight = viewportLeft + frame.TableViewportWidth;
            bool ruleBuilderListening = RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab;
            bool timePriorityOwnsMouse = TimePriorityScheduleEditor.OwnsCurrentMousePosition;
            BetterWorkTabSettings settings = BetterWorkTabMod.Settings;
            bool showCursorHighlight = BWTWorkTabEffectiveSettings.GetBool(SettingIDs.HighlightsHover);
            HeaderPresentationPacket presentation = HeaderDrawingCoordinator.CapturePresentation();

            DrawStandardHeaders(
                layout,
                table,
                totalHeight,
                viewportLeft,
                viewportRight,
                ruleBuilderListening,
                timePriorityOwnsMouse,
                settings,
                showCursorHighlight,
                in presentation);

            if (SleekWorkTabGateway.BetterWorkTabHostsSleek)
            {
                DrawSleekHeaders(
                    layout,
                    table,
                    totalHeight,
                    viewportLeft,
                    viewportRight,
                    ruleBuilderListening,
                    in presentation);
            }
        }

        private void DrawStandardHeaders(
            IWorkTabLayoutController layout,
            PawnTable table,
            float totalHeight,
            float viewportLeft,
            float viewportRight,
            bool ruleBuilderListening,
            bool timePriorityOwnsMouse,
            BetterWorkTabSettings settings,
            bool showCursorHighlight,
            in HeaderPresentationPacket presentation)
        {
            foreach (var column in layout.Columns)
            {
                WorkGridAnimatedColumnGeometry animatedGeometry =
                    WorkGridInteractionGeometry.GetAnimatedColumn(column);
                Rect animatedHeaderRect = animatedGeometry.HeaderRect;
                if (animatedHeaderRect.xMax < viewportLeft - HorizontalCullBuffer ||
                    animatedHeaderRect.xMin > viewportRight + HorizontalCullBuffer)
                {
                    continue;
                }

                DrawStandardHeaderColumn(
                    layout,
                    table,
                    totalHeight,
                    ruleBuilderListening,
                    timePriorityOwnsMouse,
                    settings,
                    showCursorHighlight,
                    column,
                    animatedGeometry,
                    animatedHeaderRect,
                    in presentation);
            }
        }

        private void DrawStandardHeaderColumn(
            IWorkTabLayoutController layout,
            PawnTable table,
            float totalHeight,
            bool ruleBuilderListening,
            bool timePriorityOwnsMouse,
            BetterWorkTabSettings settings,
            bool showCursorHighlight,
            WorkTabLayoutColumn column,
            WorkGridAnimatedColumnGeometry animatedGeometry,
            Rect animatedHeaderRect,
            in HeaderPresentationPacket presentation)
        {

                bool isWorkColumn = WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column);
                Rect headerRect = FluffyWorkTabGateway.GetHostedHeaderLaneRect(
                    column.Column,
                    table,
                    animatedHeaderRect);
                bool timePrioritySourceColumn = isWorkColumn && TimePriorityScheduleEditor.ShouldHighlightSourceColumn(column);
                bool shouldHighlightRuleBuilderTarget = false;
                if (ruleBuilderListening && isWorkColumn)
                {
                    RuleBuilder2WorkTabOverlay.ResolveTarget(
                        column,
                        out WorkTypeDef workType,
                        out WorkGiverDef workGiver);
                    shouldHighlightRuleBuilderTarget =
                        RuleBuilderGateway.ShouldHighlightRuleBuilder2Target(workType, workGiver);
                }
                bool drawRuleBuilderHighlightAfterHeader =
                    shouldHighlightRuleBuilderTarget && presentation.AngledHeadersEnabled;

                if (showCursorHighlight &&
                    isWorkColumn &&
                    (timePrioritySourceColumn ||
                     (!timePriorityOwnsMouse &&
                      !BWTWorkTabTutorial.OwnsCurrentPointer &&
                      Mouse.IsOver(headerRect))))
                {
                    Color useColor = settings.Color_MouseHoverHighlight;
                    Rect columnRect = new Rect(
                        animatedGeometry.BodyScreenX,
                        layout.TableOrigin.y + layout.HeaderHeight,
                        animatedGeometry.Width,
                        totalHeight);
                    DrawColumnHighlightAroundTutorialBand(layout, columnRect, useColor);
                }

                if (shouldHighlightRuleBuilderTarget && !drawRuleBuilderHighlightAfterHeader)
                {
                    RuleBuilder2WorkTabOverlay.DrawColumnHighlight(
                        layout,
                        column,
                        headerRect,
                        totalHeight,
                        table);
                }

                if (isWorkColumn)
                {
                    SubWorkTransitionOverlay.DrawBlankTransitionFlash(
                        layout,
                        column,
                        headerRect,
                        totalHeight);
                }

                try
                {
                    SubWorkDrilldownState.SetDrawingColumn(column);
                    if (!TryDrawFluffyHeader(column, headerRect, table, in presentation))
                    {
                        if (column.Column?.Worker is PawnColumnWorker_WorkPriority priorityWorker &&
                            !SleekWorkTabGateway.BetterWorkTabHostsSleek)
                        {
                            if (!HeaderDrawingCoordinator.TryHandleWorkPriorityHeader(
                                    priorityWorker,
                                    headerRect,
                                    table,
                                    in presentation))
                            {
                                priorityWorker.DoHeader(headerRect, table);
                            }
                        }
                        else
                        {
                            column.Column.Worker.DoHeader(headerRect, table);
                        }
                    }
                }
                finally
                {
                    SubWorkDrilldownState.ClearDrawingColumn();
                }

                if (FluffyWorkTabGateway.WasHostedWorkTypeCollapsed(column.Column))
                {
                    SubWorkDrilldownState.CollapseAllExpandBeside();
                    WorkTabInvalidationHub.Invalidate(WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
                }

                if (drawRuleBuilderHighlightAfterHeader)
                {
                    RuleBuilder2WorkTabOverlay.DrawColumnHighlight(
                        layout,
                        column,
                        headerRect,
                        totalHeight,
                        table);
                }
        }

        private void DrawSleekHeaders(
            IWorkTabLayoutController layout,
            PawnTable table,
            float totalHeight,
            float viewportLeft,
            float viewportRight,
            bool ruleBuilderListening,
            in HeaderPresentationPacket presentation)
        {
            // Sleek's header prefix still runs above so its frame/order/input state stays live.
            // BWT clears that surface and redraws its angled headers in one pass.
            SleekWorkTabGateway.DrawMixedHeaderBackdrop(
                new Rect(viewportLeft, layout.TableOrigin.y, viewportRight - viewportLeft, layout.HeaderHeight));

            foreach (var column in layout.Columns)
            {
                Rect animatedHeaderRect = WorkGridInteractionGeometry.GetAnimatedHeaderRect(column);
                if (animatedHeaderRect.xMax < viewportLeft - HorizontalCullBuffer ||
                    animatedHeaderRect.xMin > viewportRight + HorizontalCullBuffer)
                {
                    continue;
                }

                Rect headerRect = FluffyWorkTabGateway.GetHostedHeaderLaneRect(
                    column.Column,
                    table,
                    animatedHeaderRect);
                if (column.Column?.Worker is PawnColumnWorker_WorkPriority worker)
                {
                    bool shouldHighlightRuleBuilderTarget = false;
                    if (ruleBuilderListening &&
                        WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column))
                    {
                        RuleBuilder2WorkTabOverlay.ResolveTarget(
                            column,
                            out WorkTypeDef workType,
                            out WorkGiverDef workGiver);
                        shouldHighlightRuleBuilderTarget =
                            RuleBuilderGateway.ShouldHighlightRuleBuilder2Target(workType, workGiver);
                    }

                    try
                    {
                        SubWorkDrilldownState.SetDrawingColumn(column);
                        PawnColumnWorker_WorkPriority_DoHeader_Patch.DrawMixedOverlay(
                            worker,
                            headerRect,
                            table,
                            in presentation);
                    }
                    finally
                    {
                        SubWorkDrilldownState.ClearDrawingColumn();
                    }

                    // The backdrop clears the common lane; restore Rule Builder's header-side
                    // highlight after the BWT redraw. Its body highlight was painted first.
                    if (shouldHighlightRuleBuilderTarget)
                    {
                        RuleBuilder2WorkTabOverlay.DrawColumnHighlight(
                            layout,
                            column,
                            headerRect,
                            totalHeight,
                            table,
                            drawBody: false);
                    }
                }
                else if (SleekWorkTabGateway.IsSleekInlineJobColumn(column.Column))
                {
                    // Inline-job headers are Sleek-owned; redraw them after BWT clears the lane.
                    try
                    {
                        SubWorkDrilldownState.SetDrawingColumn(column);
                        column.Column.Worker.DoHeader(headerRect, table);
                    }
                    finally
                    {
                        SubWorkDrilldownState.ClearDrawingColumn();
                    }
                }
            }
        }

        /// <summary>
        /// Returns the draw rectangle used for a hosted angled header. Diagnostics and tutorial
        /// geometry use the same owner so their outlines follow the rendered label exactly.
        /// </summary>
        internal static Rect GetHostedAngledHeaderDrawRect(
            WorkTabLayoutColumn column,
            Rect headerRect,
            Vector2 labelSize,
            bool isCJKVertical,
            PawnTable table)
        {
            HeaderPresentationPacket presentation = HeaderDrawingCoordinator.CapturePresentation();
            return GetHostedAngledHeaderDrawRect(
                column,
                headerRect,
                labelSize,
                isCJKVertical,
                table,
                in presentation);
        }

        internal static Rect GetHostedAngledHeaderDrawRect(
            WorkTabLayoutColumn column,
            Rect headerRect,
            Vector2 labelSize,
            bool isCJKVertical,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            Rect drawRect;
            if (isCJKVertical)
            {
                drawRect = new Rect(
                    headerRect.center.x - (labelSize.x / 2f) + presentation.EffectiveHorizontalOffset,
                    headerRect.yMax - labelSize.y - AngledLabelDrawer.STEM_BOTTOM_GAP,
                    labelSize.x,
                    labelSize.y);
            }
            else
            {
                float stableDrawWidth = !WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked &&
                    SubWorkDrilldownState.HasAnyDrilldown
                    ? SubWorkDrilldownHeaderGeometry.GetBaseHeaderDrawWidth(table, headerRect.height)
                    : headerRect.height;
                drawRect = new Rect(0f, 0f, Mathf.Max(stableDrawWidth, labelSize.x), labelSize.y)
                {
                    center = headerRect.center
                };
                drawRect.x += presentation.EffectiveHorizontalOffset;
                drawRect.position += SubWorkDrilldownHeaderGeometry.GetExpandBesideAngledAnchorOffset(
                    table,
                    headerRect.height,
                    drawRect.width,
                    drawRect.height,
                    presentation.Rotation);
            }

            if (column.IsExpandBesideChild && column.SubWorkSlot == 0)
            {
                drawRect.x += HostedFirstSubWorkAngledHeaderOffsetX;
            }

            return drawRect;
        }

        private static float GetVisibleHeaderHighlightHeight(
            IWorkTabLayoutController layout,
            in WorkTabHeaderFrame frame)
        {
            float bodyTop = layout.TableOrigin.y + layout.HeaderHeight;
            float tableBottomSpace = Mathf.Max(
                0f,
                frame.BottomReservation - frame.InlineTimePriorityReservedHeight);
            float available = Mathf.Max(
                0f,
                frame.WindowHeight - tableBottomSpace - frame.ScrollViewFitAllowance - bodyTop);
            float logicalHeight = frame.PinnedRowsHeight + layout.ContentHeight;
            float clippedHeight = Mathf.Min(logicalHeight, available);
            if (clippedHeight >= logicalHeight - 0.5f)
            {
                return logicalHeight;
            }

            IReadOnlyList<RowDescriptor> rows = layout.GetRowDescriptors();
            if (rows == null || rows.Count == 0)
            {
                return clippedHeight;
            }

            float scrollTop = layout.Table?.scrollPosition.y ?? 0f;
            float visibleRowsHeight = Mathf.Max(0f, clippedHeight - frame.PinnedRowsHeight);
            float visibleBottom = scrollTop + visibleRowsHeight;
            float rowBottom = 0f;
            float lastFullyVisibleBottom = scrollTop;
            for (int i = 0; i < rows.Count; i++)
            {
                rowBottom += rows[i].Height;
                if (rowBottom <= visibleBottom + 0.5f)
                {
                    lastFullyVisibleBottom = Mathf.Max(lastFullyVisibleBottom, rowBottom);
                    continue;
                }

                break;
            }

            return Mathf.Min(
                clippedHeight,
                frame.PinnedRowsHeight + Mathf.Max(0f, lastFullyVisibleBottom - scrollTop));
        }

        private bool TryDrawFluffyHeader(
            WorkTabLayoutColumn column,
            Rect headerRect,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            if (!FluffyWorkTabGateway.IsFluffyColumn(column.Column))
            {
                return false;
            }

            bool specificJobOrderingBlocked =
                WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked;
            WorkTypeDef parentWorkType = column.SubWorkParent ?? column.Column?.workType;
            WorkGiverDef workGiver = column.SubWorkGiver ??
                (specificJobOrderingBlocked ? null : FluffyWorkTabGateway.TryGetFluffyWorkGiver(column.Column));
            WorkGiver focusedWorkGiver = null;
            WorkTypeDef focusedParentWorkType = null;
            bool resolvedFocusedWorkGiver = !specificJobOrderingBlocked &&
                SubWorkDrilldownState.TryGetWorkGiverForColumn(
                    column,
                    out focusedWorkGiver,
                    out focusedParentWorkType,
                    out _);
            if (resolvedFocusedWorkGiver)
            {
                workGiver = focusedWorkGiver.def;
                parentWorkType = focusedParentWorkType;
            }
            else if (!specificJobOrderingBlocked &&
                     SubWorkDrilldownState.IsActive &&
                     SubWorkDrilldownState.GetVisibleWorkColumnSlot(column.Column) >= 0)
            {
                return true;
            }

            if (parentWorkType == null)
            {
                return true;
            }

            bool isChild = workGiver != null &&
                (resolvedFocusedWorkGiver ||
                 column.IsExpandBesideChild ||
                 FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column));
            WorkGiverHeaderLabelStyle labelStyle = presentation.AngledHeadersEnabled
                ? WorkGiverHeaderLabelStyle.Standard
                : WorkGiverHeaderLabelStyle.VanillaStaggered;

            string label = isChild
                ? WorkGiverDisplayNameService.HeaderLabel(workGiver, labelStyle)
                : WorkTypeDisplayNameService.HeaderLabel(parentWorkType);

            if (FluffyWorkTabGateway.IsHostedFluffyColumn(column.Column) &&
                ShouldSuppressHostedChildLabel(parentWorkType, workGiver, label))
            {
                if (headerRect.Contains(HeaderInputController.MousePosition))
                {
                    TooltipHandler.TipRegion(headerRect, WorkGiverDisplayNameService.FullLabel(workGiver));
                }
                return true;
            }

            bool isMouseOver = !TimePriorityScheduleEditor.OwnsCurrentMousePosition &&
                headerRect.Contains(HeaderInputController.MousePosition);
            if (isMouseOver)
            {
                HeaderInputController.SetHoveredWorkType(parentWorkType, headerRect);
                if (column.Column?.Worker is PawnColumnWorker_WorkPriority priorityWorker &&
                    AngledHeaderInteraction.TryHandleShiftPriorityGesture(
                        priorityWorker,
                        table,
                        Event.current,
                        allowRootGrouping: !isChild,
                        application: _application()))
                {
                    return true;
                }
            }

            if (presentation.AngledHeadersEnabled)
            {
                DrawHostedAngledHeader(column, headerRect, parentWorkType, label, isMouseOver, table, in presentation);
            }
            else
            {
                DrawHostedVanillaHeader(column, headerRect, parentWorkType, label, isMouseOver, table, in presentation);
            }

            if (isMouseOver)
            {
                string tip = isChild
                    ? WorkGiverDisplayNameService.FullLabel(workGiver)
                    : WorkTypeDisplayNameService.FullLabel(parentWorkType);
                TooltipHandler.TipRegion(headerRect, tip);
            }
            return true;
        }

        private static bool ShouldSuppressHostedChildLabel(
            WorkTypeDef parentWorkType,
            WorkGiverDef workGiver,
            string childLabel)
        {
            if (parentWorkType == null || workGiver == null || childLabel.NullOrEmpty())
            {
                return false;
            }

            if (WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked)
            {
                return false;
            }

            var workGivers = WorkGiverReassignmentManager.GetDisplayWorkGiversForWorkType(parentWorkType);
            if (workGivers == null || workGivers.Count != 1)
            {
                return false;
            }

            string parentLabel = WorkTypeDisplayNameService.HeaderLabel(parentWorkType);
            return string.Equals(childLabel, parentLabel, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       WorkGiverDisplayNameService.FullLabel(workGiver),
                       WorkTypeDisplayNameService.FullLabel(parentWorkType),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void DrawHostedAngledHeader(
            WorkTabLayoutColumn column,
            Rect headerRect,
            WorkTypeDef parentWorkType,
            string label,
            bool isMouseOver,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            AngledHeaderCache.CachedTextMetrics textMetrics =
                AngledHeaderCache.GetLabelTextMetrics(label, in presentation);
            bool isCJKVertical = textMetrics.IsCJKVertical;
            Vector2 size = textMetrics.Size;

            Rect drawRect = GetHostedAngledHeaderDrawRect(
                column,
                headerRect,
                size,
                isCJKVertical,
                table,
                in presentation);

            var labelLayout = new AngledLabelDrawer.AngledLabelLayout(
                label,
                size,
                drawRect.center,
                WorkColumnCustomizationService.ShouldShowColumnMarker(parentWorkType, presentation.ShowMovedMarker),
                isCJKVertical,
                drawRect)
                .WithAlpha(GetHostedSubWorkHeaderAlpha(column));

            bool isSorted = table != null && table.SortingBy == column.Column;
            bool sortDescending = table != null && table.SortingDescending;
            HeaderDrawingCoordinator.DrawHeader(
                presentation.ActiveRenderer,
                labelLayout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column.Column,
                labelLayout.ShowMarker,
                in presentation);
        }

        private static void DrawHostedVanillaHeader(
            WorkTabLayoutColumn column,
            Rect headerRect,
            WorkTypeDef parentWorkType,
            string label,
            bool isMouseOver,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            var solver = HeaderDrawingCoordinator.GetVanillaSolver();
            bool isMoved = WorkColumnCustomizationService.ShouldShowColumnMarker(parentWorkType, presentation.ShowMovedMarker);
            if (Event.current.type == EventType.Layout)
            {
                solver?.CollectHeader(column.Column, headerRect, parentWorkType, isMoved);
            }

            HeaderDrawingCoordinator.EnsureLayoutSolved(table);

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Rect bounds = solver?.GetBounds(column.Column) ?? Rect.zero;
            if (bounds.width <= 0.5f || bounds.height <= 0.5f)
            {
                bounds = headerRect;
            }

            var labelLayout = new AngledLabelDrawer.AngledLabelLayout(
                label,
                bounds.size,
                bounds.center,
                isMoved)
                .WithAlpha(GetHostedSubWorkHeaderAlpha(column));

            bool isSorted = table != null && table.SortingBy == column.Column;
            bool sortDescending = table != null && table.SortingDescending;
            HeaderDrawingCoordinator.DrawHeader(
                presentation.ActiveRenderer,
                labelLayout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column.Column,
                labelLayout.ShowMarker,
                in presentation);
        }

        private static float GetHostedSubWorkHeaderAlpha(WorkTabLayoutColumn column)
        {
            if (!column.IsExpandBesideChild)
            {
                return 1f;
            }

            return SubWorkDrilldownState.GetExpandBesideHeaderAlpha(column.SubWorkParent);
        }

        private static void DrawColumnHighlightAroundTutorialBand(
            IWorkTabLayoutController layout,
            Rect columnRect,
            Color color)
        {
            if (!BWTWorkTabTutorial.TryGetBandSpan(layout, out float bandTop, out float bandBottom) ||
                bandBottom <= columnRect.yMin ||
                bandTop >= columnRect.yMax)
            {
                Widgets.DrawBoxSolid(columnRect, color);
                return;
            }

            if (bandTop > columnRect.yMin)
            {
                Widgets.DrawBoxSolid(
                    new Rect(columnRect.x, columnRect.yMin, columnRect.width, bandTop - columnRect.yMin),
                    color);
            }

            if (bandBottom < columnRect.yMax)
            {
                Widgets.DrawBoxSolid(
                    new Rect(columnRect.x, bandBottom, columnRect.width, columnRect.yMax - bandBottom),
                    color);
            }
        }
    }
}
