using UnityEngine;

namespace Better_Work_Tab.UI.Input
{
    internal static class GuiMousePosition
    {
        // Client pixels include the active GUI transform and clipping-group offset.
        internal static Vector2 ToClientPosition(Vector2 localGuiPosition)
        {
            Vector2 origin = CurrentGroupOrigin;

            // Waylith draws group offsets inside the matrix. Its public point
            // conversion applies them outside the matrix in this engine build.
            return GUI.matrix.MultiplyPoint3x4(new Vector3(
                localGuiPosition.x + origin.x, localGuiPosition.y + origin.y, 0f));
        }

        internal static Vector2 ToGuiPosition(Vector2 clientPosition)
        {
            Vector2 origin = CurrentGroupOrigin;

            Vector2 transformed = GUI.matrix.inverse.MultiplyPoint3x4(
                new Vector3(clientPosition.x, clientPosition.y, 0f));
            return new Vector2(transformed.x - origin.x, transformed.y - origin.y);
        }

        private static Vector2 CurrentGroupOrigin
        {
            get
            {
                Vector2 origin = Vector2.zero;
                var backend = UnityStubs.Gui.GuiBackend.Current;
                if (backend != null && backend.GroupCount > 0)
                    backend.GetGroup(backend.GroupCount - 1, out _, out origin);
                return origin;
            }
        }

        internal static bool TryGetClientPosition(out Vector2 position)
        {
            // Waylith reports device positions from the top-left of its own client.
            position = UnityEngine.Input.mousePosition;
            return UnityEngine.Input.mousePresent;
        }
    }
}
