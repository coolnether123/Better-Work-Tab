using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.WorkGiverReassignments
{
    internal static class PriorityOverrideRing
    {
        private const float RingInset = -2f;
        private const float InnerInset = 3f;
        private static readonly Color RingColor = new Color(1f, 0.78f, 0.18f, 1f);

        internal static Rect RingRect(Rect priorityBox)
        {
            return priorityBox.ContractedBy(RingInset);
        }

        internal static Rect InnerRect(Rect priorityBox)
        {
            return priorityBox.ContractedBy(InnerInset);
        }

        internal static bool MouseOverVisibleRing(Rect priorityBox)
        {
            return Mouse.IsOver(RingRect(priorityBox)) && !Mouse.IsOver(InnerRect(priorityBox));
        }

        internal static bool EventOverVisibleRing(Event evt, Rect priorityBox)
        {
            if (evt == null)
            {
                return false;
            }

            return RingRect(priorityBox).Contains(evt.mousePosition) && !InnerRect(priorityBox).Contains(evt.mousePosition);
        }

        internal static void Draw(Rect priorityBox)
        {
            Rect ringRect = RingRect(priorityBox);
            bool ringHovered = MouseOverVisibleRing(priorityBox);
            Color oldColor = GUI.color;
            GUI.color = ringHovered ? Color.white : RingColor;
            Widgets.DrawBox(ringRect, ringHovered ? 3 : 2);
            GUI.color = oldColor;
        }
    }
}
