using UnityEngine;
using RimWorld;
using Verse;

namespace Better_Work_Tab.UI
{
    public interface IHeaderRenderer
    {
        void DrawHeader(AngledLabelDrawer.AngledLabelLayout layout, bool isMouseOver, 
                        bool isSorted, bool sortDescending, Rect headerRect, 
                        PawnColumnDef column, bool showMarker);
    }
}
