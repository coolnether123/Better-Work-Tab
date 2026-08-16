using UnityEngine;

namespace Better_Work_Tab.Patches
{
    // Helper class to determine if shift is held and what the current state is.
    // This is to make it easier to customize how features are rendered: Always, Never, only when shift is NOT held, and only when shift IS held..
    public static class ShiftHelper
    {
        public static bool IsHeld =>
            (Event.current?.shift ?? false) ||
            Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);

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
