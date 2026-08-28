using UnityEngine;

namespace Better_Work_Tab.Patches
{
    /// <summary>
    /// Resolves the live Shift-dependent display mode. IMGUI modifier flags are
    /// authoritative for input events; Layout and Repaint use Unity key state.
    /// </summary>
    public static class ShiftHelper
    {
        public static bool IsHeld
        {
            get
            {
                Event current = Event.current;
                bool inputEventHasShift = current != null &&
                    current.type != EventType.Layout &&
                    current.type != EventType.Repaint &&
                    current.type != EventType.Ignore &&
                    current.type != EventType.Used &&
                    current.shift;
                return inputEventHasShift ||
                    Input.GetKey(KeyCode.LeftShift) ||
                    Input.GetKey(KeyCode.RightShift);
            }
        }

        public static BetterWorkTabSettings.ShowUIMode State
        {
            get
            {
                if (IsHeld)
                    return BetterWorkTabSettings.ShowUIMode.Shifted;
                else
                    return BetterWorkTabSettings.ShowUIMode.Unshifted;
            }
        }
    }
}
