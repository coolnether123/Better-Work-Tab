using System;
using Better_Work_Tab.Diagnostics;
using Better_Work_Tab.Features.TimePriority;
using Better_Work_Tab.Features.Tutorial;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.PawnOrganizer.API;
using Better_Work_Tab.UI.Headers;
using Better_Work_Tab.UI.WorkGiverReassignments;
using Better_Work_Tab.UI.WorkGrid.Contracts;
using Better_Work_Tab.UI.WorkGrid.Layout;
using RimWorld;
using Spine.Profiling;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    public interface IWorkGridSnapshotLayer
    {
        void BeginRow();
        void EndRow();
        bool TryDrawRowBackground(int rowIndex, Rect rowRect, out Color textColor);
        bool ShouldVisitCell(int rowIndex, int columnIndex);
        bool TryDrawCell(int rowIndex, int columnIndex, Rect cellRect);
    }

    /// <summary>
    /// Narrow drawing capability shared by the vanilla and optimized Work-grid renderers.
    /// </summary>
    public interface IWorkGridDrawingSurface
    {
        void DrawNativeWorkTable(PawnTable table, IWorkTabLayoutController layout, Rect inRect);

        void DrawSnapshotWorkTable(
            in WorkGridRenderContext context,
            IWorkGridSnapshotLayer snapshotLayer);
    }

    /// <summary>
    /// Owns the IMGUI surface choreography shared by both Work-grid renderer strategies.
    /// The window contributes only the scalar geometry that is specific to its host frame.
    /// </summary>
    internal sealed class WorkGridDrawingSurface : IWorkGridDrawingSurface
    {
        private readonly WorkTabViewportController _viewportController;
        private readonly WorkTabBodyRenderer _bodyRenderer;
        private readonly WorkTabHeaderRenderer _headerRenderer;
        private readonly Func<Rect> _windowRectProvider;
        private readonly Func<float> _extraBottomSpaceProvider;

        internal WorkGridDrawingSurface(
            WorkTabViewportController viewportController,
            WorkTabBodyRenderer bodyRenderer,
            WorkTabHeaderRenderer headerRenderer,
            Func<Rect> windowRectProvider,
            Func<float> extraBottomSpaceProvider)
        {
            _viewportController = viewportController ??
                throw new ArgumentNullException(nameof(viewportController));
            _bodyRenderer = bodyRenderer ?? throw new ArgumentNullException(nameof(bodyRenderer));
            _headerRenderer = headerRenderer ?? throw new ArgumentNullException(nameof(headerRenderer));
            _windowRectProvider = windowRectProvider ??
                throw new ArgumentNullException(nameof(windowRectProvider));
            _extraBottomSpaceProvider = extraBottomSpaceProvider ??
                throw new ArgumentNullException(nameof(extraBottomSpaceProvider));
        }

        public void DrawNativeWorkTable(PawnTable table, IWorkTabLayoutController layout, Rect inRect)
        {
            DrawWorkTable(table, layout, inRect, null);
        }

        public void DrawSnapshotWorkTable(
            in WorkGridRenderContext context,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            DrawWorkTable(
                context.Presentation.Table,
                context.Layout,
                context.WindowRect,
                snapshotLayer);
        }

        private void DrawWorkTable(
            PawnTable table,
            IWorkTabLayoutController layout,
            Rect inRect,
            IWorkGridSnapshotLayer snapshotLayer)
        {
            if (layout == null || table == null)
            {
                return;
            }

            var viewportFrame = new WorkTabViewportFrame(
                layout,
                inRect,
                _windowRectProvider(),
                _extraBottomSpaceProvider(),
                WorkGridLayoutMetrics.GetInlineTimePriorityReservedHeight(layout),
                WorkGridLayoutMetrics.GetPinnedRowsHeight(),
                WorkGridLayoutMetrics.GetVisualTableScrollWidth(layout, layout.Table),
                SubWorkDrilldownState.IsExpandBesideTransitioning,
                Time.frameCount);
            WorkTabViewport viewport = default(WorkTabViewport);

            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.CalculateScrollRects", () => viewport = _viewportController.CalculateScrollRects(in viewportFrame));
            }
            else
            {
                viewport = _viewportController.CalculateScrollRects(in viewportFrame);
            }

            WorkTabHeaderFrame headerFrame = new WorkTabHeaderFrame(
                _windowRectProvider().height,
                _extraBottomSpaceProvider(),
                WorkGridLayoutMetrics.GetInlineTimePriorityReservedHeight(layout),
                WorkGridLayoutMetrics.GetPinnedRowsHeight(),
                GetTableViewportWidth(layout),
                WorkGridLayoutMetrics.ScrollViewFitAllowance);

            bool layoutEvent = Event.current.type == EventType.Layout;
            if (!layoutEvent && SpineTiming.Enabled)
            {
                SpineTiming.Time(
                    "WorkTab.DrawHeaders",
                    () => _headerRenderer.DrawHeaders(layout, table, in headerFrame));
            }
            else
            {
                // Vanilla-style headers collect their complete stagger geometry during
                // Unity's Layout event. Skipping this pass leaves every label at offset
                // zero on Repaint, causing the horizontal headers to overlap.
                _headerRenderer.DrawHeaders(layout, table, in headerFrame);
            }
            if (!layoutEvent)
            {
                // The tutorial band is a pinned divider. Paint it after the header
                // pass so Rule Builder's column selection cannot cover its text or
                // separator, while row content below it still renders normally.
                BWTWorkTabTutorial.DrawBand(layout);
            }
            if (!layoutEvent && SubWorkDrilldownState.HasAnyDrilldown)
            {
                if (SpineTiming.Enabled)
                {
                    SpineTiming.Time("WorkTab.DrawSubWorkGlobalRow", () => SubWorkDrilldownBarRenderer.Draw(layout));
                }
                else
                {
                    SubWorkDrilldownBarRenderer.Draw(layout);
                }
            }
            if (SpineTiming.Enabled)
            {
                SpineTiming.Time("WorkTab.DrawRows", () => _bodyRenderer.DrawRows(table, layout, viewport, snapshotLayer));
                if (!layoutEvent)
                {
                    SpineTiming.Time(
                        "WorkTab.DrawSubWorkTransitionWave",
                        () => SubWorkTransitionOverlay.DrawTransitionPixelWave(
                            layout,
                            WorkGridLayoutMetrics.GetPinnedRowsHeight()));
                }
            }
            else
            {
                _bodyRenderer.DrawRows(table, layout, viewport, snapshotLayer);
                if (!layoutEvent)
                {
                    SubWorkTransitionOverlay.DrawTransitionPixelWave(
                        layout,
                        WorkGridLayoutMetrics.GetPinnedRowsHeight());
                }
            }

            if (!layoutEvent)
            {
                WorkTabGeometryDiagnostics.DumpHeaderLayoutIfRequested(layout);
            }
        }

        private static float GetTableViewportWidth(IWorkTabLayoutController layout)
        {
            if (layout?.Table == null)
            {
                return 0f;
            }

            float available = Mathf.Max(1f, Verse.UI.screenWidth - layout.TableOrigin.x - 2f);
            return Mathf.Min(
                WorkGridLayoutMetrics.GetVisualTableScrollWidth(layout, layout.Table),
                available);
        }
    }
}
