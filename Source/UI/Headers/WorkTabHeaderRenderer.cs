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

        /// <summary>
        /// Captures the values shared by every standard column in one header pass.
        /// Ownership and presentation are resolved once so every column observes the
        /// same state while overlays and hosted headers are composed.
        /// </summary>
        private readonly struct StandardHeaderPass
        {
            private StandardHeaderPass(
                IWorkTabLayoutController layout,
                PawnTable table,
                float totalHeight,
                float tableViewportWidth)
            {
                Layout = layout;
                Table = table;
                TotalHeight = totalHeight;
                ViewportLeft = layout.TableOrigin.x;
                ViewportRight = ViewportLeft + tableViewportWidth;
                RuleBuilderListening = RuleBuilderGateway.IsRuleBuilder2ListeningToWorkTab;
                TimePriorityOwnsMouse = TimePriorityScheduleEditor.OwnsCurrentMousePosition;
                Settings = BetterWorkTabMod.Settings;
                ShowCursorHighlight = BWTWorkTabEffectiveSettings.GetBool(
                    SettingIDs.HighlightsHover);
                Presentation = HeaderDrawingCoordinator.CapturePresentation();
            }

            internal static StandardHeaderPass Capture(
                IWorkTabLayoutController layout,
                PawnTable table,
                float totalHeight,
                float tableViewportWidth)
            {
                return new StandardHeaderPass(
                    layout,
                    table,
                    totalHeight,
                    tableViewportWidth);
            }

            internal IWorkTabLayoutController Layout { get; }
            internal PawnTable Table { get; }
            internal float TotalHeight { get; }
            internal float ViewportLeft { get; }
            internal float ViewportRight { get; }
            internal bool RuleBuilderListening { get; }
            internal bool TimePriorityOwnsMouse { get; }
            internal BetterWorkTabSettings Settings { get; }
            internal bool ShowCursorHighlight { get; }
            internal readonly HeaderPresentationPacket Presentation;
        }

        /// <summary>
        /// Holds geometry and ownership decisions that differ by column. The draw
        /// phases consume this state in order without repeating live resolution.
        /// </summary>
        private readonly struct StandardHeaderColumnState
        {
            internal StandardHeaderColumnState(
                WorkTabLayoutColumn column,
                WorkGridAnimatedColumnGeometry animatedGeometry,
                Rect headerRect,
                bool isWorkColumn,
                bool timePrioritySourceColumn,
                bool shouldHighlightRuleBuilderTarget,
                bool drawRuleBuilderHighlightAfterHeader)
            {
                Column = column;
                AnimatedGeometry = animatedGeometry;
                HeaderRect = headerRect;
                IsWorkColumn = isWorkColumn;
                TimePrioritySourceColumn = timePrioritySourceColumn;
                ShouldHighlightRuleBuilderTarget = shouldHighlightRuleBuilderTarget;
                DrawRuleBuilderHighlightAfterHeader = drawRuleBuilderHighlightAfterHeader;
            }

            internal WorkTabLayoutColumn Column { get; }
            internal WorkGridAnimatedColumnGeometry AnimatedGeometry { get; }
            internal Rect HeaderRect { get; }
            internal bool IsWorkColumn { get; }
            internal bool TimePrioritySourceColumn { get; }
            internal bool ShouldHighlightRuleBuilderTarget { get; }
            internal bool DrawRuleBuilderHighlightAfterHeader { get; }
        }

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
            FluffyWorkTabGateway.PrepareExternalFluffyDraw(table);
            StandardHeaderPass pass = StandardHeaderPass.Capture(
                layout,
                table,
                totalHeight,
                frame.TableViewportWidth);

            DrawStandardHeaders(in pass);

            if (SleekWorkTabGateway.BetterWorkTabHostsSleek)
            {
                DrawSleekHeaders(
                    pass.Layout,
                    pass.Table,
                    pass.TotalHeight,
                    pass.ViewportLeft,
                    pass.ViewportRight,
                    pass.RuleBuilderListening,
                    in pass.Presentation);
            }
        }

        private void DrawStandardHeaders(in StandardHeaderPass pass)
        {
            foreach (var column in pass.Layout.Columns)
            {
                WorkGridAnimatedColumnGeometry animatedGeometry =
                    WorkGridInteractionGeometry.GetAnimatedColumn(column);
                Rect animatedHeaderRect = animatedGeometry.HeaderRect;
                if (animatedHeaderRect.xMax < pass.ViewportLeft - HorizontalCullBuffer ||
                    animatedHeaderRect.xMin > pass.ViewportRight + HorizontalCullBuffer)
                {
                    continue;
                }

                DrawStandardHeaderColumn(
                    in pass,
                    column,
                    animatedGeometry,
                    animatedHeaderRect);
            }
        }

        private void DrawStandardHeaderColumn(
            in StandardHeaderPass pass,
            WorkTabLayoutColumn column,
            WorkGridAnimatedColumnGeometry animatedGeometry,
            Rect animatedHeaderRect)
        {
            // This order is intentional: pre-header overlays, the owner/native header, hosted
            // collapse side effects, then the deferred angled overlay. Several integrations use
            // the sequence for their input state and for clipping their highlight pixels.
            StandardHeaderColumnState state = ResolveStandardHeaderColumnState(
                in pass,
                column,
                animatedGeometry,
                animatedHeaderRect);

            DrawStandardHeaderPreOverlays(in pass, in state);
            DrawOwnedOrNativeHeader(in pass, in state);
            ApplyHostedHeaderCollapse(in state);
            DrawStandardHeaderPostOverlays(in pass, in state);
        }

        private static StandardHeaderColumnState ResolveStandardHeaderColumnState(
            in StandardHeaderPass pass,
            WorkTabLayoutColumn column,
            WorkGridAnimatedColumnGeometry animatedGeometry,
            Rect animatedHeaderRect)
        {
            bool isWorkColumn = WorkTabColumnHighlightUtility.IsHighlightableWorkColumn(column);
            Rect headerRect = BwtExpandBesideColumns.GetHeaderLaneRect(
                column.Column,
                pass.Table,
                animatedHeaderRect);
            bool timePrioritySourceColumn = isWorkColumn &&
                TimePriorityScheduleEditor.ShouldHighlightSourceColumn(column);
            bool shouldHighlightRuleBuilderTarget = false;
            if (pass.RuleBuilderListening && isWorkColumn)
            {
                RuleBuilder2WorkTabOverlay.ResolveTarget(
                    column,
                    out WorkTypeDef workType,
                    out WorkGiverDef workGiver);
                shouldHighlightRuleBuilderTarget =
                    RuleBuilderGateway.ShouldHighlightRuleBuilder2Target(workType, workGiver);
            }

            return new StandardHeaderColumnState(
                column,
                animatedGeometry,
                headerRect,
                isWorkColumn,
                timePrioritySourceColumn,
                shouldHighlightRuleBuilderTarget,
                shouldHighlightRuleBuilderTarget && pass.Presentation.AngledHeadersEnabled);
        }

        private static void DrawStandardHeaderPreOverlays(
            in StandardHeaderPass pass,
            in StandardHeaderColumnState state)
        {
            if (pass.ShowCursorHighlight &&
                state.IsWorkColumn &&
                (state.TimePrioritySourceColumn ||
                 (!pass.TimePriorityOwnsMouse &&
                  !BWTWorkTabTutorial.OwnsCurrentPointer &&
                  Mouse.IsOver(state.HeaderRect))))
            {
                Color useColor = pass.Settings.Color_MouseHoverHighlight;
                Rect columnRect = new Rect(
                    state.AnimatedGeometry.BodyScreenX,
                    pass.Layout.TableOrigin.y + pass.Layout.HeaderHeight,
                    state.AnimatedGeometry.Width,
                    pass.TotalHeight);
                DrawColumnHighlightAroundTutorialBand(pass.Layout, columnRect, useColor);
            }

            if (state.ShouldHighlightRuleBuilderTarget &&
                !state.DrawRuleBuilderHighlightAfterHeader)
            {
                RuleBuilder2WorkTabOverlay.DrawColumnHighlight(
                    pass.Layout,
                    state.Column,
                    state.HeaderRect,
                    pass.TotalHeight,
                    pass.Table);
            }

            if (state.IsWorkColumn)
            {
                SubWorkTransitionOverlay.DrawBlankTransitionFlash(
                    pass.Layout,
                    state.Column,
                    state.HeaderRect,
                    pass.TotalHeight);
            }
        }

        private void DrawOwnedOrNativeHeader(
            in StandardHeaderPass pass,
            in StandardHeaderColumnState state)
        {
            try
            {
                SubWorkDrilldownState.SetDrawingColumn(state.Column);
                if (!TryHandleFluffyHeaderOwnership(
                        state.Column,
                        state.HeaderRect,
                        pass.Table,
                        in pass.Presentation))
                {
                    if (state.Column.Column.Worker is PawnColumnWorker_WorkPriority priorityWorker &&
                        !SleekWorkTabGateway.BetterWorkTabHostsSleek)
                    {
                        if (!HeaderDrawingCoordinator.TryHandleWorkPriorityHeader(
                                priorityWorker,
                                state.HeaderRect,
                                pass.Table,
                                in pass.Presentation))
                        {
                            priorityWorker.DoHeader(state.HeaderRect, pass.Table);
                        }
                    }
                    else
                    {
                        state.Column.Column.Worker.DoHeader(state.HeaderRect, pass.Table);
                    }
                }
            }
            finally
            {
                SubWorkDrilldownState.ClearDrawingColumn();
            }
        }

        private static void ApplyHostedHeaderCollapse(
            in StandardHeaderColumnState state)
        {
            if (!FluffyWorkTabGateway.WasExternalFluffyWorkTypeCollapsed(state.Column.Column))
            {
                return;
            }

            SubWorkDrilldownState.CollapseAllExpandBeside();
            WorkTabInvalidationHub.Invalidate(
                WorkTabDirtyFlags.Columns | WorkTabDirtyFlags.HeaderGeometry);
        }

        private static void DrawStandardHeaderPostOverlays(
            in StandardHeaderPass pass,
            in StandardHeaderColumnState state)
        {
            if (!state.DrawRuleBuilderHighlightAfterHeader)
            {
                return;
            }

            RuleBuilder2WorkTabOverlay.DrawColumnHighlight(
                pass.Layout,
                state.Column,
                state.HeaderRect,
                pass.TotalHeight,
                pass.Table);
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

                Rect headerRect = BwtExpandBesideColumns.GetHeaderLaneRect(
                    column.Column,
                    table,
                    animatedHeaderRect);
                if (column.Column.Worker is PawnColumnWorker_WorkPriority worker)
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

        /// <summary>
        /// Gives the hosted Fluffy lane the first opportunity to handle this header. A true
        /// result means the caller must not invoke the native worker: Fluffy may have drawn the
        /// header, intentionally left the lane blank while a hosted column is hidden, or
        /// consumed input such as a shift-priority gesture.
        /// </summary>
        private bool TryHandleFluffyHeaderOwnership(
            WorkTabLayoutColumn column,
            Rect headerRect,
            PawnTable table,
            in HeaderPresentationPacket presentation)
        {
            if (!BwtExpandBesideColumns.IsNativeColumn(column.Column) &&
                !FluffyWorkTabGateway.IsFluffyColumn(column.Column))
            {
                return false;
            }

            bool specificJobOrderingBlocked =
                WorkTabEffectiveStateRuntime.IsPreviewSpecificJobOrderingBlocked;
            WorkTypeDef parentWorkType = column.SubWorkParent ?? column.Column?.workType;
            WorkGiverDef workGiver = column.SubWorkGiver ??
                (specificJobOrderingBlocked
                    ? null
                    : BwtExpandBesideColumns.TryGetWorkGiver(column.Column) ??
                      FluffyWorkTabGateway.TryGetFluffyWorkGiver(column.Column));
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
                 BwtExpandBesideColumns.IsNativeChildColumn(column.Column) ||
                 FluffyWorkTabGateway.IsFluffyWorkGiverColumn(column.Column));
            WorkGiverHeaderLabelStyle labelStyle = presentation.AngledHeadersEnabled
                ? WorkGiverHeaderLabelStyle.Standard
                : WorkGiverHeaderLabelStyle.VanillaStaggered;

            string label = isChild
                ? WorkGiverDisplayNameService.HeaderLabel(workGiver, labelStyle)
                : WorkTypeDisplayNameService.HeaderLabel(parentWorkType);

            if (BwtExpandBesideColumns.IsNativeColumn(column.Column) &&
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
