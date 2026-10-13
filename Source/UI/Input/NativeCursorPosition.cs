using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.UI.Input
{
    /// <summary>
    /// Restores the cursor only in the focused game window. Coordinates are client pixels.
    /// </summary>
    internal static class NativeCursorPosition
    {
        private const int MoveDelayFrames = 2;
        private const float MoveDurationSeconds = 0.22f;

        private static Vector2? _pendingClientPosition;
        private static int _pendingFrame;
        private static Vector2 _moveStartClientPosition;
        private static Vector2 _moveTargetClientPosition;
        private static float _moveStartedAt;
        private static bool _isAnimatingMove;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        internal static void ScheduleMoveToClientPosition(Vector2 clientPosition)
        {
            if (!Application.isFocused)
            {
                CancelPendingMove();
                return;
            }

            _pendingClientPosition = clientPosition;
            _pendingFrame = Time.frameCount + MoveDelayFrames;
            _moveTargetClientPosition = clientPosition;
            _moveStartClientPosition = GuiMousePosition.TryGetClientPosition(out var currentPosition)
                ? currentPosition
                : clientPosition;
            _moveStartedAt = 0f;
            _isAnimatingMove = true;
        }

        internal static void CancelPendingMove()
        {
            _pendingClientPosition = null;
            _pendingFrame = 0;
            _moveStartedAt = 0f;
            _isAnimatingMove = false;
        }

        internal static void ProcessPendingMove()
        {
            if (!Application.isFocused)
            {
                CancelPendingMove();
                return;
            }

            if (!_pendingClientPosition.HasValue || !_isAnimatingMove || Time.frameCount < _pendingFrame)
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
            Vector2 current = Vector2.Lerp(_moveStartClientPosition, _moveTargetClientPosition, eased);
            TryMoveToClientPosition(current);

            if (rawProgress >= 1f)
            {
                _pendingClientPosition = null;
                _isAnimatingMove = false;
            }
        }

        internal static void DrawPendingMoveCue()
        {
            if (!_isAnimatingMove || !_pendingClientPosition.HasValue || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Vector2 current = GuiMousePosition.TryGetClientPosition(out var clientPosition)
                ? clientPosition
                : _moveStartClientPosition;
            current = GuiMousePosition.ToGuiPosition(current);
            Vector2 target = GuiMousePosition.ToGuiPosition(_moveTargetClientPosition);
            Color color = new Color(1f, 0.78f, 0.18f, 0.68f);
            Widgets.DrawLine(current, target, color, 2f);
            Widgets.DrawBoxSolid(new Rect(target.x - 3f, target.y - 3f, 6f, 6f), color);
        }

        private static bool AnyMouseButtonDown()
        {
            return UnityEngine.Input.GetMouseButton(0) ||
                   UnityEngine.Input.GetMouseButton(1) ||
                   UnityEngine.Input.GetMouseButton(2);
        }

        internal static bool TryMoveToClientPosition(Vector2 clientPosition)
        {
            if (!Application.isFocused ||
                (Application.platform != RuntimePlatform.WindowsPlayer &&
                 Application.platform != RuntimePlatform.WindowsEditor))
            {
                return false;
            }

            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero ||
                    GetWindowThreadProcessId(window, out uint processId) == 0 ||
                    processId != (uint)Environment.ProcessId ||
                    !GetClientRect(window, out var clientRect))
                {
                    return false;
                }

                var point = new NativePoint
                {
                    X = Mathf.RoundToInt(Mathf.Clamp(clientPosition.x, clientRect.Left, clientRect.Right)),
                    Y = Mathf.RoundToInt(Mathf.Clamp(clientPosition.y, clientRect.Top, clientRect.Bottom))
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
