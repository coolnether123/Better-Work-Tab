using UnityEngine;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI.Headers.Angled
{
    /// <summary>
    /// Renders headers at an angle. 
    /// Bridges the IHeaderRenderer interface to the AngledLabelDrawer.
    /// </summary>
    public class AngledHeaderRenderer : IHeaderRenderer, IHeaderPresentationRenderer
    {
        /// <summary>
        /// Draws an angled header.
        /// </summary>
        public void DrawHeader(AngledLabelDrawer.AngledLabelLayout layout, bool isMouseOver, 
                                bool isSorted, bool sortDescending, Rect headerRect, 
                                PawnColumnDef column, bool showMarker)
        {
            HeaderPresentationPacket presentation = HeaderDrawingCoordinator.CapturePresentation();
            DrawPreparedHeader(
                layout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column,
                showMarker,
                in presentation);
        }

        void IHeaderPresentationRenderer.DrawHeader(
            AngledLabelDrawer.AngledLabelLayout layout,
            bool isMouseOver,
            bool isSorted,
            bool sortDescending,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation)
        {
            DrawPreparedHeader(
                layout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column,
                showMarker,
                in presentation);
        }

        /// <summary>
        /// Draws the stable non-glyph pixels into a cache-local surface. Font
        /// glyphs stay at the live IMGUI boundary so transparent-surface alpha
        /// composition cannot thin their anti-aliased edges.
        /// </summary>
        internal void DrawRetainedStable(
            AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            PawnColumnDef column,
            in HeaderPresentationPacket presentation)
        {
            AngledLabelDrawer.DrawRetainedStable(
                layout,
                headerRect,
                column,
                in presentation);
        }

        internal void DrawRetainedText(
            AngledLabelDrawer.AngledLabelLayout layout,
            Rect headerRect,
            PawnColumnDef column,
            in HeaderPresentationPacket presentation)
        {
            AngledLabelDrawer.DrawRetainedText(
                layout,
                headerRect,
                column,
                in presentation);
        }

        private static void DrawPreparedHeader(
            AngledLabelDrawer.AngledLabelLayout layout,
            bool isMouseOver,
            bool isSorted,
            bool sortDescending,
            Rect headerRect,
            PawnColumnDef column,
            bool showMarker,
            in HeaderPresentationPacket presentation)
        {
            // AngledLabelDrawer handles its own marker logic via layout.ShowMarker
            AngledLabelDrawer.Draw(
                layout,
                isMouseOver,
                isSorted,
                sortDescending,
                headerRect,
                column,
                in presentation);
        }
    }
}
