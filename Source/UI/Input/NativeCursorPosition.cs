using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Better_Work_Tab.UI.Input
{
    /// <summary>
    /// Converts RimWorld UI coordinates to the native window client area and moves the OS cursor.
    /// </summary>
    internal static class NativeCursorPosition
    {
        private static Vector2? _pendingUiPosition;
        private static int _pendingFrame;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        internal static void ScheduleMoveToUiPosition(Vector2 uiPosition)
        {
            _pendingUiPosition = uiPosition;
            _pendingFrame = Time.frameCount + 4;
        }

        internal static void ProcessPendingMove()
        {
            if (!_pendingUiPosition.HasValue || Time.frameCount < _pendingFrame)
            {
                return;
            }

            Event evt = Event.current;
            if ((evt != null && evt.type != EventType.Repaint) || AnyMouseButtonDown())
            {
                return;
            }

            TryMoveToUiPosition(_pendingUiPosition.Value);
            _pendingUiPosition = null;
        }

        private static bool AnyMouseButtonDown()
        {
            return UnityEngine.Input.GetMouseButton(0) ||
                   UnityEngine.Input.GetMouseButton(1) ||
                   UnityEngine.Input.GetMouseButton(2);
        }

        internal static bool TryGetClientPosition(out Vector2 position)
        {
            position = Vector2.zero;
            if (Application.platform != RuntimePlatform.WindowsPlayer &&
                Application.platform != RuntimePlatform.WindowsEditor)
            {
                return false;
            }

            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero || !GetCursorPos(out var point))
                {
                    return false;
                }

                if (!ScreenToClient(window, ref point))
                {
                    return false;
                }

                position = new Vector2(point.X, point.Y);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryMoveToUiPosition(Vector2 uiPosition)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer &&
                Application.platform != RuntimePlatform.WindowsEditor)
            {
                return false;
            }

            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero || !GetClientRect(window, out var clientRect))
                {
                    return false;
                }

                var point = new NativePoint
                {
                    X = Mathf.RoundToInt(Mathf.Clamp(uiPosition.x, clientRect.Left, clientRect.Right)),
                    Y = Mathf.RoundToInt(Mathf.Clamp(uiPosition.y, clientRect.Top, clientRect.Bottom))
                };

                if (!ClientToScreen(window, ref point))
                {
                    return false;
                }

                return SetCursorPos(point.X, point.Y);
            }
            catch
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
