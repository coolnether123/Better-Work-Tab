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
using Better_Work_Tab.UI.WorkGrid.Snapshots;
using RimWorld;
using Spine.Profiling;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGrid.Rendering
{
    /// <summary>
    /// Optional optimized-layer capability. The body uses the prepared range
    /// to avoid visiting every off-screen column while keeping the public
    /// drawing and snapshot-layer contracts unchanged.
    /// </summary>
    internal interface IWorkGridVisibleColumnRangeProvider
    {
        WorkGridIndexRange VisibleColumnRange { get; }
    }

    public interface IWorkGridSnapshotLayer
    {
        void BeginRow();
        void EndRow();
        bool TryDrawRowBackground(int rowIndex, Rect rowRect, out Color textColor);
        bool ShouldVisitCell(int rowIndex, int columnIndex);
        bool TryDrawCell(int rowIndex, int columnIndex, Rect cellRect);
    }

    /// <summary>
    /// Optional BWT-owned composition capability. It gives the direct
    /// ExpandBeside renderer the already-finished sub-work presentation while
    /// leaving the native/Harmony fallback on its established live path.
    /// </summary>
    internal interface IWorkGridSubWorkPresentationLayer
    {
        bool TryGetSubWorkPresentation(
            int rowIndex,
            int columnIndex,
            out WorkGiverCellPresentationCache.CellPresentation presentation);
    }

    /// <summary>
    /// Narrow drawing capability shared by the vanilla and optimized Work-grid
    /// renderers.
    /// </summary>
    /// <remarks>
    /// Header composition is deliberately separate from body drawing. The
    /// renderer facade calls <see cref="DrawHeaders"/> once for the requested
    /// header layer, then lets the selected body strategy call
    /// <see cref="DrawBody"/>. This keeps the vanilla-header reorder path on
    /// the same optimized body route as angled headers.
    /// </remarks>
    public interface IWorkGridDrawingSurface
    {
        /// <summary>Draws the BWT-owned header row, including vanilla-header routing.</summary>
        void DrawHeaders(in WorkTabView view);

        /// <summary>
        /// Draws the viewport, pinned body affordances, and body rows. A null
        /// snapshot layer selects the native body-cell path.
        /// </summary>
        void DrawBody(in WorkTabView view, IWorkGridSnapshotLayer snapshotLayer);
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
        internal WorkGridDrawingSurface(
            WorkTabViewportController viewportController,
            WorkTabBodyRenderer bodyRenderer,
            WorkTabHeaderRenderer headerRenderer)
        {
            _viewportController = viewportController ??
                throw new ArgumentNullException(nameof(viewportController));
            _bodyRenderer = bodyRenderer ?? throw new ArgumentNullException(nameof(bodyRenderer));
            _headerRenderer = headerRenderer ?? throw new ArgumentNullException(nameof(headerRenderer));
        }

        public void DrawHeaders(in WorkTabView view)
        {
            PawnTable table = view.Table;
            IWorkTabLayoutController layout = view.Layout;
            if (layout == null || table == null)
            {
                return;
            }

            WorkTabHeaderFrame headerFrame = new WorkTabHeaderFrame(
                view.WindowRect.height,
                view.ExtraBottomSpace,
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
        }

        public void DrawBody(in WorkTabView view, IWorkGridSnapshotLayer snapshotLayer)
        {
            PawnTable table = view.Table;
            IWorkTabLayoutController layout = view.Layout;
            if (layout == null || table == null)
            {
                return;
            }

            var viewportFrame = new WorkTabViewportFrame(
                layout,
                view.Viewport,
                view.WindowRect,
                view.ExtraBottomSpace,
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

            bool layoutEvent = Event.current.type == EventType.Layout;
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
                WorkTabView drawView = view;
                SpineTiming.Time("WorkTab.DrawRows", () => _bodyRenderer.DrawRows(in drawView, viewport, snapshotLayer));
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
                _bodyRenderer.DrawRows(in view, viewport, snapshotLayer);
                if (!layoutEvent)
                {
                    SubWorkTransitionOverlay.DrawTransitionPixelWave(
                        layout,
                        WorkGridLayoutMetrics.GetPinnedRowsHeight());
                }
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
