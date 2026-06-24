using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Input
{
    /// <summary>
    /// Converts RimWorld UI coordinates to the native window client area and moves the OS cursor.
    /// </summary>
    internal static class NativeCursorPosition
    {
        private const int MoveDelayFrames = 2;
        private const float MoveDurationSeconds = 0.22f;

        private static Vector2? _pendingUiPosition;
        private static int _pendingFrame;
        private static Vector2 _moveStartUiPosition;
        private static Vector2 _moveTargetUiPosition;
        private static float _moveStartedAt;
        private static bool _isAnimatingMove;

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
            _pendingFrame = Time.frameCount + MoveDelayFrames;
            _moveTargetUiPosition = uiPosition;
            _moveStartUiPosition = TryGetClientPosition(out var currentPosition)
                ? currentPosition
                : uiPosition;
            _moveStartedAt = 0f;
            _isAnimatingMove = true;
        }

        internal static void CancelPendingMove()
        {
            _pendingUiPosition = null;
            _pendingFrame = 0;
            _moveStartedAt = 0f;
            _isAnimatingMove = false;
        }

        internal static void ProcessPendingMove()
        {
            if (!_pendingUiPosition.HasValue || !_isAnimatingMove || Time.frameCount < _pendingFrame)
            {
                return;
            }

            Event evt = Event.current;
            if ((evt != null && evt.type != EventType.Repaint) || AnyMouseButtonDown())
            {
                return;
            }

            if (_moveStartedAt <= 0f)
            {
                _moveStartedAt = Time.realtimeSinceStartup;
            }

            float rawProgress = Mathf.Clamp01((Time.realtimeSinceStartup - _moveStartedAt) / MoveDurationSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, rawProgress);
            Vector2 current = Vector2.Lerp(_moveStartUiPosition, _moveTargetUiPosition, eased);
            TryMoveToUiPosition(current);

            if (rawProgress >= 1f)
            {
                _pendingUiPosition = null;
                _isAnimatingMove = false;
            }
        }

        internal static void DrawPendingMoveCue()
        {
            if (!_isAnimatingMove || !_pendingUiPosition.HasValue || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Vector2 current = TryGetClientPosition(out var clientPosition)
                ? clientPosition
                : _moveStartUiPosition;
            Color color = new Color(1f, 0.78f, 0.18f, 0.68f);
            Widgets.DrawLine(current, _moveTargetUiPosition, color, 2f);
            Better_Work_Tab.WidgetsCompat.DrawBoxSolid(new Rect(_moveTargetUiPosition.x - 3f, _moveTargetUiPosition.y - 3f, 6f, 6f), color);
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
