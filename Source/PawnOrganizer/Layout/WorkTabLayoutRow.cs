using Better_Work_Tab.PawnOrganizer.Data;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.PawnOrganizer
{
    /// <summary>
    /// Immutable description of a row (pawn or divider) produced during layout rebuild.
    /// </summary>
    public readonly struct WorkTabLayoutRow
    {
        public WorkTabLayoutRow(DisplayElement element, float offsetY, float height, int visualIndex)
        {
            Element = element;
            OffsetY = offsetY;
            Height = height;
            VisualIndex = visualIndex;
        }

        /// <summary>
        /// Underlying pawn/divider wrapper.
        /// </summary>
        public DisplayElement Element { get; }

        /// <summary>
        /// Offset from top of the scroll content (excludes header height) in pixels.
        /// </summary>
        public float OffsetY { get; }

        /// <summary>
        /// Row height in pixels.
        /// </summary>
        public float Height { get; }

        /// <summary>
        /// Draw order index after layout rebuild.
        /// </summary>
        public int VisualIndex { get; }

        public bool IsDivider => Element.IsDivider;
        public Pawn Pawn => (Element as PawnElement)?.Pawn;
        public PawnDivider Divider => (Element as DividerElement)?.Divider;
    }
}
