using Better_Work_Tab.UI.Headers.Angled;
using UnityEngine;

namespace Better_Work_Tab.UI.Input
{
    internal static class GuiMousePosition
    {
        internal static Vector2 ToRootUiPosition(Vector2 localGuiPosition)
        {
            if (NativeCursorPosition.TryGetClientPosition(out var clientPosition))
            {
                return clientPosition;
            }

            return GUIClipUtility.Unclip(localGuiPosition);
        }
    }
}
