using UnityEngine;
using RimWorld;
using Verse;
using Better_Work_Tab.UI.Headers.Angled;

namespace Better_Work_Tab.UI.Headers
{
    /// <summary>
    /// Interface for header rendering strategies (Angled, Vanilla, etc.).
    /// </summary>
    public interface IHeaderRenderer
    {
        /// <summary>
        /// Draws a single work column header.
        /// </summary>
        /// <param name="layout">Pre-calculated text and pivot layout.</param>
        /// <param name="isMouseOver">Whether the mouse is currently hovering this specific header.</param>
        /// <param name="isSorted">Whether the table is currently sorted by this column.</param>
        /// <param name="sortDescending">Whether the sort direction is descending.</param>
        /// <param name="headerRect">The original base rectangle for the column header.</param>
        /// <param name="column">The column def being rendered.</param>
        /// <param name="showMarker">Whether to show the moved column marker (*).</param>
        void DrawHeader(AngledLabelDrawer.AngledLabelLayout layout, bool isMouseOver, 
                        bool isSorted, bool sortDescending, Rect headerRect, 
                        PawnColumnDef column, bool showMarker);
    }
}
