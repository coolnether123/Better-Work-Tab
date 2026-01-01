using UnityEngine;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI
{
    public class AngledHeaderRenderer : IHeaderRenderer
    {
        public void DrawHeader(AngledLabelDrawer.AngledLabelLayout layout, bool isMouseOver, 
                               bool isSorted, bool sortDescending, Rect headerRect, 
                               PawnColumnDef column, bool showMarker)
        {
            // AngledLabelDrawer handles its own marker logic via layout.ShowMarker
            AngledLabelDrawer.Draw(layout, isMouseOver, isSorted, sortDescending, headerRect, column);
        }
    }
}
